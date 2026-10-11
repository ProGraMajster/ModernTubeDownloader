using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Batch;

public static class BatchImportParser
{
    public const int MaximumEntries = 250_000;
    public const long MaximumFileBytes = 256_000_000;
    public static async IAsyncEnumerable<BatchRawEntry> ParseAsync(string file, [EnumeratorCancellation] CancellationToken token = default)
    {
        var format = Path.GetExtension(file).ToLowerInvariant();
        if (format is not (".txt" or ".csv" or ".json")) throw new InvalidDataException("Batch.Error.Format");
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Batch.Error.Size");
        await using var bounded = new BatchInputStream(stream, json: format == ".json", csv: format == ".csv");
        var count = 0;
        IAsyncEnumerable<BatchRawEntry> rows = format switch { ".txt" => Txt(bounded, token), ".csv" => Csv(bounded, token), _ => Json(bounded, token) };
        await foreach (var row in rows.WithCancellation(token))
        {
            if (++count > MaximumEntries) throw new InvalidDataException("Batch.Error.Count");
            yield return row;
        }
    }

    private static async IAsyncEnumerable<BatchRawEntry> Txt(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192, true);
        while (await reader.ReadLineAsync(token) is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            yield return new(line);
        }
    }
    private static async IAsyncEnumerable<BatchRawEntry> Csv(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var parser = new TextFieldParser(stream, Encoding.UTF8, true, true)
        { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields()?.Select(value => value.Trim().ToLowerInvariant()).ToArray();
        var allowed = new[] { "url", "quality", "container", "start", "end" };
        if (header is null || !header.Contains("url") || header.Distinct().Count() != header.Length || header.Any(value => !allowed.Contains(value)))
            throw new InvalidDataException("Batch.Error.Header");
        var indices = header.Select((value, index) => (value, index)).ToDictionary(pair => pair.value, pair => pair.index);
        while (!parser.EndOfData)
        {
            token.ThrowIfCancellationRequested();
            string[]? fields = null; bool malformed = false;
            try { fields = parser.ReadFields(); }
            catch (MalformedLineException) { malformed = true; }
            if (malformed || fields?.Length != header.Length) { yield return new("", ErrorKey: "Batch.Error.CsvRow"); continue; }
            string? Get(string key) => indices.TryGetValue(key, out var index) ? fields![index] : null;
            yield return new(Get("url")!, Get("quality"), Get("container"), Get("start"), Get("end"));
            await Task.CompletedTask;
        }
    }
    private static async IAsyncEnumerable<BatchRawEntry> Json(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var value in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, new JsonSerializerOptions { MaxDepth = 8 }, token))
        {
            if (value.ValueKind == JsonValueKind.String) { yield return new(value.GetString()!); continue; }
            if (value.ValueKind != JsonValueKind.Object) { yield return new("", ErrorKey: "Batch.Error.Schema"); continue; }
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var invalid = false;
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is not ("url" or "quality" or "container" or "start" or "end") || fields.ContainsKey(property.Name) ||
                    property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) ||
                    property.Name == "url" && property.Value.ValueKind != JsonValueKind.String) { invalid = true; break; }
                fields.Add(property.Name, property.Value.ToString());
            }
            if (invalid || !fields.ContainsKey("url")) { yield return new("", ErrorKey: "Batch.Error.Schema"); continue; }
            yield return new(fields["url"], fields.GetValueOrDefault("quality"), fields.GetValueOrDefault("container"), fields.GetValueOrDefault("start"), fields.GetValueOrDefault("end"));
        }
    }

    public static BatchImportEntry Validate(BatchRawEntry raw, BatchImportSession session, int ordinal)
    {
        var entry = new BatchImportEntry { Ordinal = ordinal, Quality = session.DefaultQuality, Container = session.DefaultContainer, Url = raw.Url.Trim() };
        var error = raw.ErrorKey;
        if (entry.Url.Length > 4096) { entry.Url = ""; error ??= "Batch.Error.Url"; }
        if (!YtDlpMetadataService.TryValidateSourceUrl(entry.Url, out var normalized, out _)) error ??= "Batch.Error.Url";
        else { entry.Url = normalized; entry.Identity = Identity(normalized); entry.Source = new Uri(normalized).Host; }
        if (!string.IsNullOrWhiteSpace(raw.Quality))
        {
            var quality = raw.Quality.Trim().ToLowerInvariant();
            if (quality.EndsWith('p')) quality = quality[..^1];
            if (quality == "4k") quality = "2160";
            if (!QualityPreset.All.Any(value => value.Id == quality)) error ??= "Batch.Error.Quality";
            else entry.Quality = quality;
        }
        if (!string.IsNullOrWhiteSpace(raw.Container))
        {
            if (!PreferredVideoContainerExtensions.TryParseProtocol(raw.Container.Trim(), out var container)) error ??= "Batch.Error.Container";
            else entry.Container = container;
        }
        if (!TryTime(raw.Start, out var start) || !TryTime(raw.End, out var end)) error ??= "Range.Error.Invalid";
        else if (start is not null || end is not null)
        {
            entry.Range = new(MediaRangeMode.Custom, start, end);
            error ??= entry.Range.Validate(null);
        }
        entry.ErrorKey = error; entry.Status = error is null ? BatchEntryStatus.Pending : BatchEntryStatus.Failed;
        return entry;
    }
    private static bool TryTime(string? value, out TimeSpan? time)
    {
        if (!string.IsNullOrWhiteSpace(value) && double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0 && seconds <= 30 * 86400)
        { time = TimeSpan.FromSeconds(seconds); return true; }
        return MediaTimeRange.TryParseTime(value, out time);
    }
    public static string Identity(string url)
    {
        var uri = new Uri(url);
        if (uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be")
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var id = uri.Host == "youtu.be" ? uri.AbsolutePath.Trim('/') : uri.AbsolutePath.StartsWith("/watch", StringComparison.Ordinal) ? query["v"] :
                uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/live/", StringComparison.Ordinal) ? uri.AbsolutePath.Split('/').Last() : null;
            if (!string.IsNullOrEmpty(id)) return "youtube:video:" + id;
            if (uri.AbsolutePath == "/playlist" && query["list"] is { } list) return "youtube:playlist:" + list;
        }
        var builder = new UriBuilder(uri) { Fragment = "" }; return builder.Uri.AbsoluteUri;
    }
    public static string ExportUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0) return "";
        var identity = Identity(url);
        if (identity.StartsWith("youtube:video:", StringComparison.Ordinal)) return "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(identity[14..]);
        if (identity.StartsWith("youtube:playlist:", StringComparison.Ordinal)) return "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(identity[17..]);
        return new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }
}

/// <summary>Bounds individual textual records / JSON tokens before serializers can grow an unbounded buffer.</summary>
internal sealed class BatchInputStream(Stream inner, bool json, bool csv = false) : Stream
{
    private long bytes; private int line, stringBytes, objectBytes, depth; private bool quoted, escaped;
    private void Check(ReadOnlySpan<byte> data)
    {
        bytes += data.Length;
        if (bytes > BatchImportParser.MaximumFileBytes) throw new InvalidDataException("Batch.Error.Size");
        foreach (var value in data)
        {
            if (!json) {
                line = value == 10 ? 0 : line + 1; if (line > 16384) throw new InvalidDataException("Batch.Error.RecordSize");
                if (csv) { if (value == 34) quoted = !quoted; objectBytes = value == 10 && !quoted ? 0 : objectBytes + 1; if (objectBytes > 65536) throw new InvalidDataException("Batch.Error.RecordSize"); }
                continue;
            }
            if (depth > 0 && ++objectBytes > 65536) throw new InvalidDataException("Batch.Error.RecordSize");
            if (quoted)
            {
                if (++stringBytes > 16384) throw new InvalidDataException("Batch.Error.RecordSize");
                if (escaped) { escaped = false; continue; }
                if (value == 92) escaped = true; else if (value == 34) quoted = false;
            }
            else if (value == 34) { quoted = true; stringBytes = 0; }
            else if (value == 123) { if (depth++ == 0) objectBytes = 1; }
            else if (value == 125) depth--;
        }
    }
    public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, count); Check(buffer.AsSpan(offset, read)); return read; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    { var read = await inner.ReadAsync(buffer, token); Check(buffer.Span[..read]); return read; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
