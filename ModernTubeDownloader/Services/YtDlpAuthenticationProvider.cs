using ModernTubeDownloader.Authentication;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Settings;
using System.Text.Json.Nodes;

namespace ModernTubeDownloader.Services;

/// <summary>One auth source, one ownership scope per logical attempt; no browser database access.</summary>
public sealed class YtDlpAuthenticationProvider(AppPaths paths, SettingsService settings, TimeProvider? refreshClock = null)
{
    private readonly SettingsService configuration = settings;
    private readonly AsyncLocal<Session?> active = new();
    private readonly CookieStore store = new(paths.AuthenticationDirectory, refreshClock);
    public CookieStore Store => store;
    public AuthenticationSource Source => configuration.Current.CookieSource ??
        (configuration.Current.UseCookieFile ? AuthenticationSource.ManualFile : AuthenticationSource.None);
    internal TimeSpan RefreshTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public async Task<T> ExecuteAsync<T>(string url, Func<Task<T>> invocation, CancellationToken token, bool retryAtAttemptBoundary = false)
    {
        if (active.Value?.DeferRetry == true && !retryAtAttemptBoundary) return await invocation().ConfigureAwait(false);
        using var individual = active.Value is null ? BeginSession(url) : null;
        var session = active.Value!;
        var previous = session.DeferRetry;
        session.DeferRetry |= retryAtAttemptBoundary;
        try { return await ExecuteWithRefreshAsync(session, url, invocation, token).ConfigureAwait(false); }
        finally { session.DeferRetry = previous; }
    }
    private async Task<T> ExecuteWithRefreshAsync<T>(Session session, string url, Func<Task<T>> invocation, CancellationToken token)
    {
        try { return await invocation().ConfigureAwait(false); }
        catch (ExternalProcessException error) when (session.Host == new Uri(url).IdnHost && IsAuthenticationFailure(error) &&
            session.Target?.Consented == true && Source == AuthenticationSource.SyncedBrowserProfile)
        {
            if (session.Retried) throw new CookieRefreshException("second-auth-failure", error);
            session.Retried = true; // The logical attempt owns one budget across metadata, download and subtitle stages.
            CookieRefreshResult refreshed;
            try { refreshed = await store.RequestRefreshAsync(session.Target, RefreshTimeout, token).ConfigureAwait(false); }
            catch (CookieSecurityException) { throw new CookieRefreshException("rejected", error); }
            if (!refreshed.Refreshed) throw new CookieRefreshException(refreshed.Status, error);
            if (!session.ReplaceJar(this, url, refreshed.SnapshotVersion)) throw new CookieRefreshException("rejected", error);
            try { return await invocation().ConfigureAwait(false); }
            catch (ExternalProcessException second) when (IsAuthenticationFailure(second))
            { throw new CookieRefreshException("second-auth-failure", second); }
        }
    }
    private static bool IsAuthenticationFailure(ExternalProcessException error) => DownloadFailureClassifier.Classify(error).Category is
        DownloadFailureCategory.Authentication or DownloadFailureCategory.Private or DownloadFailureCategory.CookieUnavailable or DownloadFailureCategory.CookieFileRejected;
    public IDisposable BeginSession(string url)
    {
        var previous = active.Value;
        var session = new Session(this, url);
        active.Value = session;
        return new Ownership(() => { active.Value = previous; session.Dispose(); });
    }
    public AuthenticationInvocation Prepare(string url)
    {
        if (active.Value is { } shared && shared.Host == new Uri(url).IdnHost)
        {
            shared.RefreshSecrets(); // yt-dlp can update this attempt's jar between stages.
            return new(shared.CookiePath, shared.Redact, null);
        }
        var individual = new Session(this, url);
        return new(individual.CookiePath, individual.Redact, individual);
    }
    private sealed class Session : IDisposable
    {
        private CookieJarLease? lease;
        private string[] secretValues = [];
        public string Host { get; }
        public string? CookiePath { get; private set; }
        public CookieRefreshTarget? Target { get; private set; }
        public bool Retried { get; set; }
        public bool DeferRetry { get; set; }
        public Session(YtDlpAuthenticationProvider provider, string url)
        {
            Host = new Uri(url).IdnHost;
            Target = provider.Source == AuthenticationSource.SyncedBrowserProfile ? provider.store.GetRefreshTarget(url) : null;
            CookiePath = provider.Source switch
            {
                AuthenticationSource.ManualFile => CookieFileService.Validate(provider.configuration.Current.CookieFilePath),
                AuthenticationSource.SyncedBrowserProfile => (lease = provider.store.CreateJar(url, Target?.ProfileId, Target?.Origin))?.Path,
                _ => null
            };
            if (lease is not null && Target is not null) Target = Target with { SnapshotVersion = lease.SnapshotVersion };
            if (CookiePath is null) return;
            LogSanitizer.RegisterCookieFilePath(CookiePath);
            // Redact tool diagnostics before any callback, logger or user-facing process error.
            RefreshSecrets();
        }
        public bool ReplaceJar(YtDlpAuthenticationProvider provider, string url, long version)
        {
            var target = provider.store.GetRefreshTarget(url);
            if (target is null || target.ProfileId != Target?.ProfileId || target.Origin != Target?.Origin || !target.Consented || target.SnapshotVersion < version || version <= Target.SnapshotVersion)
                return false;
            var fresh = provider.store.CreateJar(url, target.ProfileId, target.Origin);
            if (fresh is not null && fresh.SnapshotVersion < version) { fresh.Dispose(); return false; }
            // The old invocation has completed. Never replace the contents of its leased jar.
            lease?.Dispose(); lease = fresh; CookiePath = fresh?.Path; Target = target;
            if (CookiePath is not null) LogSanitizer.RegisterCookieFilePath(CookiePath);
            RefreshSecrets(); return true;
        }
        public void RefreshSecrets()
        {
            if (CookiePath is null) return;
            try { secretValues = secretValues.Concat(File.ReadLines(CookiePath).Where(line => !line.StartsWith('#') || line.StartsWith("#HttpOnly_", StringComparison.Ordinal))
                .Select(line => line.Split('\t')).Where(columns => columns.Length == 7 && columns[6].Length > 0)
                .Select(columns => columns[6])).Distinct().OrderByDescending(value => value.Length).ToArray(); }
            catch { lease?.Dispose(); throw new CookieSecurityException("cookie-file-read"); }
        }
        public string Redact(string line) => secretValues.Any(line.Contains) ? "<authentication-diagnostic-redacted>" : LogSanitizer.Redact(line);
        public void Dispose() => lease?.Dispose();
    }
    private sealed class Ownership(Action close) : IDisposable
    {
        private Action? release = close;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}

public sealed class CookieRefreshException(string status, Exception cause) : Exception("Browser cookie refresh: " + status, cause)
{
    public string Status { get; } = status;
}

public sealed class AuthenticationInvocation(string? path, Func<string, string> redact, IDisposable? owner) : IDisposable
{
    public string? CookiePath => path;
    public string Redact(string line) => redact(line);
    public string SanitizeOutput(string line)
    {
        if (!line.TrimStart().StartsWith('{')) return Redact(line);
        try
        {
            var node = JsonNode.Parse(line);
            Scrub(node);
            return node?.ToJsonString() ?? "{}";
        }
        catch (System.Text.Json.JsonException) { return Redact(line); }
    }
    private void Scrub(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (key.Contains("headers", StringComparison.OrdinalIgnoreCase) || key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("password", StringComparison.OrdinalIgnoreCase) || key.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                { obj.Remove(key); continue; }
                var value = obj[key];
                if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && Redact(text) == "<authentication-diagnostic-redacted>")
                    obj[key] = "<authentication-diagnostic-redacted>";
                else Scrub(value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
                if (array[index] is JsonValue scalar && scalar.TryGetValue<string>(out var text) && Redact(text) == "<authentication-diagnostic-redacted>")
                    array[index] = "<authentication-diagnostic-redacted>";
                else Scrub(array[index]);
        }
    }
    public void Dispose() => owner?.Dispose();
}
