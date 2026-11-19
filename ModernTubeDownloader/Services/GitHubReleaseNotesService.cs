using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Services;

internal enum ReleaseNotesOrigin { GitHub, Cache }

/// <summary>Body is retained Markdown; Sections contain text only, never active markup.</summary>
internal sealed record RemoteReleaseNotes(string Version, string Body, IReadOnlyList<ReleaseNoteSection> Sections,
    Uri OfficialUrl, ReleaseNotesOrigin Origin);

/// <summary>
/// Optional, exact-installed-version release information using the application's shared HTTP client.
/// This service neither discovers updates nor changes settings, the read acknowledgement, or user stores.
/// </summary>
internal sealed class GitHubReleaseNotesService
{
    internal const int MaximumBodyBytes = 64 * 1024;
    internal const int MaximumPayloadBytes = 1024 * 1024;
    internal const int MaximumCacheBytes = 2 * 1024 * 1024;
    internal const int MaximumCacheEntries = 4;
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    internal static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(30);
    private readonly string cacheDirectory;
    private readonly HttpClient http;
    private readonly IAppLogger logger;
    private readonly TimeProvider clock;
    private readonly TimeSpan requestTimeout;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset requestAllowedAfter;

    internal GitHubReleaseNotesService(AppPaths paths, HttpClient http, IAppLogger logger,
        TimeProvider? timeProvider = null, TimeSpan? requestTimeout = null)
    {
        this.http = http;
        this.logger = logger;
        clock = timeProvider ?? TimeProvider.System;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);
        if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        cacheDirectory = Path.Combine(Path.GetFullPath(paths.RootDirectory), "release-notes-cache");
    }

    internal async Task<RemoteReleaseNotes?> FetchAsync(string installedVersion, bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = ReleaseNotesCatalog.NormalizeVersion(installedVersion);
        if (version is null) return null;

        // Admission is asynchronous. A close cancels only that window's work; the next open can try again.
        // Queued opens see the first successful cache and the shared request limit, avoiding duplicate HTTP.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = await ReadCacheAsync(version, cancellationToken).ConfigureAwait(false);
            var now = clock.GetUtcNow();
            if ((!forceRefresh && cached is not null && now >= cached.ValidatedAt && now - cached.ValidatedAt < CacheLifetime)
                || now < requestAllowedAfter)
                return cached?.Notes;

            requestAllowedAfter = now + MinimumRequestInterval;
            using var deadline = new CancellationTokenSource(requestTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                var endpoint = ExactTagEndpoint(version);
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (cached?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var parsedTag))
                    request.Headers.IfNoneMatch.Add(parsedTag);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                // The shared client also handles updater asset redirects. Never consume/cache a redirected
                // notes response from another URI, and never follow links embedded in release content.
                if (response.RequestMessage?.RequestUri is { } finalUri && finalUri.AbsoluteUri != endpoint.AbsoluteUri)
                    throw new InvalidDataException("Release-note response URI does not match the installed tag.");

                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    var retry = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date - now)
                        ?? TimeSpan.FromMinutes(5);
                    requestAllowedAfter = now + TimeSpan.FromSeconds(Math.Clamp(retry.TotalSeconds, 30, 3600));
                    return cached?.Notes;
                }
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    if (cached is null) return null;
                    await SaveCacheAsync(cached with { ValidatedAt = now }, cancellationToken).ConfigureAwait(false);
                    return cached.Notes;
                }
                if (response.StatusCode != HttpStatusCode.OK) return cached?.Notes;

                var bytes = await GitHubApplicationReleaseSource.ReadBoundedAsync(response, MaximumPayloadBytes, linked.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                var notes = ParseRelease(document.RootElement, version, ReleaseNotesOrigin.GitHub);
                cancellationToken.ThrowIfCancellationRequested();
                var responseTag = response.Headers.ETag?.ToString();
                if (responseTag?.Length > 256) responseTag = null;
                await SaveCacheAsync(new(notes, now, responseTag), cancellationToken).ConfigureAwait(false);
                return notes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                requestAllowedAfter = default;
                throw;
            }
            catch (Exception error) when (IsExpectedFailure(error))
            {
                // Do not log release bodies, tokens, URLs supplied by a response, or private profile paths.
                logger.Info("Installed release notes are unavailable; keeping validated cache or bundled notes (" + error.GetType().Name + ").");
                return cached?.Notes;
            }
        }
        finally { gate.Release(); }
    }

    private static bool IsExpectedFailure(Exception error) => error is HttpRequestException or IOException or InvalidDataException
        or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException
        or OperationCanceledException or KeyNotFoundException or NotSupportedException;

    private static Uri ExactTagEndpoint(string version)
    {
        var repository = new Uri(GitHubApplicationReleaseSource.Repository).AbsolutePath;
        return new Uri("https://api.github.com/repos" + repository + "/releases/tags/v" + version);
    }

    private static RemoteReleaseNotes ParseRelease(JsonElement release, string version, ReleaseNotesOrigin origin)
    {
        if (release.ValueKind != JsonValueKind.Object
            || release.GetProperty("tag_name").GetString() != "v" + version
            || release.GetProperty("draft").GetBoolean()
            || release.GetProperty("prerelease").GetBoolean() != version.Contains('-'))
            throw new InvalidDataException("Unexpected release-note tag or publication state.");
        return CreateNotes(version, release.GetProperty("body").GetString(), release.GetProperty("html_url").GetString(), origin);
    }

    private static RemoteReleaseNotes CreateNotes(string version, string? body, string? page, ReleaseNotesOrigin origin)
    {
        var expectedPage = GitHubApplicationReleaseSource.Repository + "/releases/tag/v" + version;
        if (page != expectedPage || string.IsNullOrWhiteSpace(body) || Encoding.UTF8.GetByteCount(body) > MaximumBodyBytes)
            throw new InvalidDataException("Invalid installed release-note content.");
        return new(version, body, ReleaseNotesMarkdown.Parse(body), new Uri(expectedPage), origin);
    }

    private sealed record CachedNotes(RemoteReleaseNotes Notes, DateTimeOffset ValidatedAt, string? ETag);
    private sealed record CacheEnvelope(int Schema, string Version, string Body, string OfficialUrl,
        DateTimeOffset ValidatedAt, string? ETag);

    private string CachePath(string version) => Path.Combine(cacheDirectory, "v" + version + ".json");

    private async Task<CachedNotes?> ReadCacheAsync(string version, CancellationToken token)
    {
        try
        {
            var path = CachePath(version);
            UpdatePackageSecurity.EnsureNoReparsePoints(path);
            if (!File.Exists(path)) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, FileOptions.Asynchronous);
            if (stream.Length > MaximumCacheBytes) return null;
            // Bound the stream independently of the initial file length (the file may change externally).
            using var response = new HttpResponseMessage { Content = new StreamContent(stream) };
            var bytes = await GitHubApplicationReleaseSource.ReadBoundedAsync(response, MaximumCacheBytes, token).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(bytes, new JsonSerializerOptions { MaxDepth = 16 });
            if (envelope is null || envelope.Schema != 1 || envelope.Version != version
                || envelope.ValidatedAt == default || envelope.ValidatedAt > clock.GetUtcNow() + TimeSpan.FromMinutes(5)
                || envelope.ETag is { } etag && (etag.Length > 256 || !EntityTagHeaderValue.TryParse(etag, out _)))
                return null;
            return new(CreateNotes(version, envelope.Body, envelope.OfficialUrl, ReleaseNotesOrigin.Cache), envelope.ValidatedAt, envelope.ETag);
        }
        catch (Exception error) when (error is not OperationCanceledException && IsExpectedFailure(error))
        {
            logger.Info("Installed release-note cache could not be read (" + error.GetType().Name + ").");
            return null;
        }
    }

    private async Task SaveCacheAsync(CachedNotes entry, CancellationToken token)
    {
        string? temporaryPath = null;
        try
        {
            var destination = CachePath(entry.Notes.Version);
            UpdatePackageSecurity.EnsureNoReparsePoints(destination);
            Directory.CreateDirectory(cacheDirectory);
            UpdatePackageSecurity.EnsureNoReparsePoints(destination);
            var envelope = new CacheEnvelope(1, entry.Notes.Version, entry.Notes.Body, entry.Notes.OfficialUrl.AbsoluteUri, entry.ValidatedAt, entry.ETag);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (bytes.Length > MaximumCacheBytes) throw new InvalidDataException("Release-note cache exceeds its size limit.");
            temporaryPath = Path.Combine(cacheDirectory, ".notes-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            UpdatePackageSecurity.EnsureNoReparsePoints(temporaryPath);
            UpdatePackageSecurity.EnsureNoReparsePoints(destination);
            File.Move(temporaryPath, destination, true);
            temporaryPath = null;
            TrimCache(destination);
        }
        catch (Exception error) when (error is not OperationCanceledException && IsExpectedFailure(error))
        {
            // An unwritable disposable cache must not hide a valid HTTP response or touch other stores.
            logger.Info("Installed release-note cache could not be updated (" + error.GetType().Name + ").");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { UpdatePackageSecurity.EnsureNoReparsePoints(temporaryPath); File.Delete(temporaryPath); }
                catch (Exception error) when (IsExpectedFailure(error)) { }
            }
        }
    }

    private void TrimCache(string current)
    {
        UpdatePackageSecurity.EnsureNoReparsePoints(cacheDirectory);
        var entries = Directory.EnumerateFiles(cacheDirectory, "v*.json", SearchOption.TopDirectoryOnly)
            .Where(path => ReleaseNotesCatalog.NormalizeVersion(Path.GetFileNameWithoutExtension(path)[1..]) == Path.GetFileNameWithoutExtension(path)[1..])
            .Select(path => new FileInfo(path)).OrderByDescending(file => file.FullName == current)
            .ThenByDescending(file => file.LastWriteTimeUtc).Skip(MaximumCacheEntries);
        foreach (var entry in entries)
        {
            UpdatePackageSecurity.EnsureNoReparsePoints(entry.FullName);
            File.Delete(entry.FullName);
        }
    }
}

/// <summary>A bounded text-only subset. No HTML, browser, image loader or URI activation is involved.</summary>
internal static class ReleaseNotesMarkdown
{
    internal static IReadOnlyList<ReleaseNoteSection> Parse(string body)
    {
        var sections = new List<ReleaseNoteSection>();
        var preamble = new List<string>();
        List<string>? items = null;
        var paragraph = new StringBuilder();
        var fenced = false;
        void Flush()
        {
            if (paragraph.Length == 0) return;
            var target = items ?? preamble;
            if (paragraph.Length > 16 * 1024 || target.Count >= 192)
                throw new InvalidDataException("Release-note text exceeds the rendering limits.");
            target.Add(paragraph.ToString()); paragraph.Clear();
        }
        foreach (var raw in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal)) { Flush(); fenced = !fenced; }
            if (!fenced && line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                var title = line[3..].Trim();
                var category = title switch
                {
                    "New" => "new", "Improvements" => "improved", "Fixes" => "fixed", "Known Issues" => "limitations",
                    _ => throw new InvalidDataException("Unexpected release-note category.")
                };
                if (sections.Any(section => section.Category == category)) throw new InvalidDataException("Duplicate release-note category.");
                items = [];
                if (sections.Count == 0) items.AddRange(preamble);
                sections.Add(new(category, title, items.AsReadOnly()));
                continue;
            }
            if (!fenced && line.StartsWith("# ", StringComparison.Ordinal) && sections.Count == 0) continue;
            if (line.Length == 0) { Flush(); continue; }
            if (!fenced && (line.StartsWith("###", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal)))
            {
                Flush();
                line = line.StartsWith('#') ? line.TrimStart('#').Trim() : line[2..];
            }
            if (paragraph.Length > 0) paragraph.AppendLine();
            // Keep unrecognized markup literal. In particular HTML, JS links and image syntax remain
            // harmless text, and cannot trigger downloads or navigation when consumed by a Label.
            paragraph.Append(line);
        }
        Flush();
        if (sections.Count != 4 || sections.Any(section => section.Items.Count == 0))
            throw new InvalidDataException("Release notes must include all four populated categories.");
        return sections.AsReadOnly();
    }
}
