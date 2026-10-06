using System.Globalization;
using System.Text.Json;

namespace ModernTubeDownloader.Models;

public sealed record PlaylistEntry(
    string Id,
    string Title,
    string SourceUrl,
    int PlaylistIndex,
    double? DurationSeconds,
    string? Channel,
    string? ThumbnailUrl,
    string? Availability,
    string? ErrorReason)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(SourceUrl) && string.IsNullOrWhiteSpace(ErrorReason) &&
        Availability is not ("private" or "deleted" or "unavailable" or "needs_auth" or "premium_only" or "subscriber_only") &&
        !Title.Equals("[Private video]", StringComparison.OrdinalIgnoreCase) &&
        !Title.Equals("[Deleted video]", StringComparison.OrdinalIgnoreCase);
}

public sealed record PlaylistMetadata(
    string Id,
    string Title,
    string? Channel,
    string SourceUrl,
    string? ThumbnailUrl,
    IReadOnlyList<PlaylistEntry> Entries)
{
    public int EntryCount => Entries.Count;

    public static PlaylistMetadata Parse(string json, string sourceUrl)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            (String(root, "_type") is { } type && type is not ("playlist" or "multi_video")))
            throw new InvalidDataException("yt-dlp did not return a playlist metadata object.");

        if (!root.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("yt-dlp playlist metadata has no entries array.");

        var entries = new List<PlaylistEntry>(entriesElement.GetArrayLength());
        var ordinal = 0;
        foreach (var element in entriesElement.EnumerateArray())
        {
            ordinal++;
            if (element.ValueKind != JsonValueKind.Object)
            {
                entries.Add(new PlaylistEntry(string.Empty, string.Empty, string.Empty, ordinal, null, null, null, "unavailable", null));
                continue;
            }

            var id = String(element, "id") ?? string.Empty;
            var title = String(element, "title") ?? id;
            var source = EntryUrl(element, id);
            var availability = String(element, "availability");
            var error = String(element, "error");
            entries.Add(new PlaylistEntry(
                id, title, source, PositiveIndex(element, "playlist_index") ?? ordinal,
                Number(element, "duration"), String(element, "channel") ?? String(element, "uploader"),
                Thumbnail(element), availability, error));
        }

        return new PlaylistMetadata(
            String(root, "id") ?? string.Empty,
            String(root, "title") ?? string.Empty,
            String(root, "channel") ?? String(root, "uploader"),
            AbsoluteHttp(String(root, "webpage_url")) ?? sourceUrl,
            Thumbnail(root), entries);
    }

    private static string EntryUrl(JsonElement entry, string id)
    {
        foreach (var field in new[] { "webpage_url", "original_url", "url" })
            if (AbsoluteHttp(String(entry, field)) is { } absolute)
                return absolute;

        var extractor = String(entry, "ie_key") ?? String(entry, "extractor_key");
        if (string.Equals(extractor, "Youtube", StringComparison.OrdinalIgnoreCase))
        {
            var videoId = String(entry, "url") ?? id;
            if (!string.IsNullOrWhiteSpace(videoId) && videoId.Length <= 64 &&
                videoId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
                return "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(videoId);
        }
        return string.Empty;
    }

    private static string? Thumbnail(JsonElement element)
    {
        if (AbsoluteHttp(String(element, "thumbnail")) is { } direct)
            return direct;
        if (!element.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var thumbnail in thumbnails.EnumerateArray().Reverse())
            if (thumbnail.ValueKind == JsonValueKind.Object && AbsoluteHttp(String(thumbnail, "url")) is { } url)
                return url;
        return null;
    }

    private static string? AbsoluteHttp(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri : null;

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? PositiveIndex(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        var text = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index > 0 ? index : null;
    }

    private static double? Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) && double.IsFinite(result) && result >= 0
            ? result : null;
}
