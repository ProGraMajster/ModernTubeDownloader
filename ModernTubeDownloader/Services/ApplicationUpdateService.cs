using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Services;

public sealed record ApplicationRelease(string Version, string Notes, Uri Page, Uri Archive, Uri Checksum);
public sealed class ApplicationUpdateState
{
    public DateTimeOffset? LastCheck { get; set; }
    public string? SkippedVersion { get; set; }
    public DateTimeOffset? RemindAfter { get; set; }
    public ApplicationRelease? Available { get; set; }
    public string? StagedArchive { get; set; }
    public string? StagedHash { get; set; }
    public bool InstallOnExit { get; set; }
}

public interface IApplicationReleaseSource { Task<ApplicationRelease?> GetLatestAsync(CancellationToken token); }

public sealed partial class GitHubApplicationReleaseSource(HttpClient http) : IApplicationReleaseSource
{
    public const string Repository = "https://github.com/ProGraMajster/ModernTubeDownloader";
    public async Task<ApplicationRelease?> GetLatestAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ProGraMajster/ModernTubeDownloader/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(response, 1_000_000, token);
        using var json = JsonDocument.Parse(bytes);
        return Parse(json.RootElement);
    }

    public static ApplicationRelease Parse(JsonElement release)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("Stable updates require a public non-prerelease release.");
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!StableTag().IsMatch(tag)) throw new InvalidDataException("Unexpected release version.");
        var version = tag[1..];
        var file = $"ModernTubeDownloader-{version}-win-x64.zip";
        var page = new Uri(release.GetProperty("html_url").GetString()!);
        if (page.AbsoluteUri != $"{Repository}/releases/tag/{tag}") throw new InvalidDataException("Unexpected release repository.");
        Uri Select(string name)
        {
            var assets = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
            if (assets.Length != 1) throw new InvalidDataException("Exactly one expected release asset is required.");
            var uri = new Uri(assets[0].GetProperty("browser_download_url").GetString()!);
            if (uri.AbsoluteUri != $"{Repository}/releases/download/{tag}/{name}") throw new InvalidDataException("Unexpected release asset URL.");
            return uri;
        }
        return new(version, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", page, Select(file), Select(file + ".sha256"));
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update metadata exceeds the limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Update metadata exceeds the limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    [GeneratedRegex(@"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")]
    private static partial Regex StableTag();
}

public sealed class ApplicationUpdateService : IDisposable
{
    private readonly SettingsService settings;
    private readonly HttpClient http;
    private readonly IApplicationReleaseSource releases;
    private readonly IAppLogger logger;
    private readonly string root, stateFile;
    private readonly TimeSpan checkTimeout;
    private readonly TimeProvider checkClock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? monitor;
    private bool disposed;
    public ApplicationUpdateState State { get; private set; } = new();
    public string StatusKey { get; private set; } = "Updates.Idle";
    public double Progress { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler<string>? UpdatePrepared;
    public string CurrentVersion { get; }
    public bool Ready => State.StagedArchive is not null && State.StagedHash is not null && File.Exists(State.StagedArchive);

    public ApplicationUpdateService(AppPaths paths, SettingsService settings, HttpClient http, IAppLogger logger,
        IApplicationReleaseSource? releases = null, string? currentVersion = null, TimeSpan? checkTimeout = null, TimeProvider? checkClock = null)
    {
        this.settings = settings; this.http = http; this.logger = logger;
        this.releases = releases ?? new GitHubApplicationReleaseSource(http);
        this.checkTimeout = checkTimeout ?? TimeSpan.FromSeconds(30);
        this.checkClock = checkClock ?? TimeProvider.System;
        if (this.checkTimeout <= TimeSpan.Zero || this.checkTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(checkTimeout));
        root = Path.Combine(paths.RootDirectory, "Updates"); stateFile = Path.Combine(root, "state.json");
        CurrentVersion = currentVersion ?? typeof(ApplicationUpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        try { State = await AtomicJsonFile.ReadAsync<ApplicationUpdateState>(stateFile, token) ?? new(); }
        catch (Exception error) { logger.Error("Update state could not be read; no update will be installed.", error); State = new(); }
        if (State.StagedArchive is { } path && (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)))
        { State.StagedArchive = null; State.StagedHash = null; State.InstallOnExit = false; }
        if (State.Available is null || !IsNewer(State.Available.Version, CurrentVersion))
        { State.Available = null; State.StagedArchive = null; State.StagedHash = null; State.InstallOnExit = false; }
        StatusKey = Ready ? "Updates.Ready" : "Updates.Idle";
    }

    public void Start() => monitor ??= Task.Run(MonitorAsync);
    private async Task MonitorAsync()
    {
        try
        {
            await InitializeAsync(lifetime.Token);
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                var options = settings.Current.Updates;
                var interval = options.Frequency == UpdateCheckFrequency.Weekly ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
                if (options.AutomaticChecks && (!State.LastCheck.HasValue || DateTimeOffset.UtcNow - State.LastCheck >= interval))
                {
                    await CheckAsync(lifetime.Token);
                    if (options.Mode != UpdateInstallMode.NotifyOnly && State.Available is not null &&
                        State.Available.Version != State.SkippedVersion && (!State.RemindAfter.HasValue || State.RemindAfter <= DateTimeOffset.UtcNow) && !Ready)
                        await DownloadAsync(lifetime.Token);
                    if (Ready && options.Mode == UpdateInstallMode.InstallOnExit) await SetInstallOnExitAsync(true, lifetime.Token);
                }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error) { Fail("This application update attempt failed; the monitor will retry later.", error); }
                await Task.Delay(TimeSpan.FromMinutes(15), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { Fail("The application update monitor failed.", error); }
    }

    public async Task CheckAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var callerToken = token;
        await gate.WaitAsync(linked.Token);
        // Start after admission to the serialized check, exactly as CancelAfter did.
        // Production uses the system clock; tests can advance this same deadline
        // independently of runner scheduling and durable filesystem latency.
        using var deadline = new CancellationTokenSource(checkTimeout, checkClock);
        using var deadlineRegistration = deadline.Token.Register(linked.Cancel);
        token = linked.Token;
        try
        {
            Publish("Updates.Checking");
            var latest = await releases.GetLatestAsync(token);
            var available = latest is not null && IsNewer(latest.Version, CurrentVersion) ? latest : null;
            if (State.Available?.Version != available?.Version || available is null)
            { State.StagedArchive = null; State.StagedHash = null; State.InstallOnExit = false; }
            State.Available = available;
            State.LastCheck = DateTimeOffset.UtcNow;
            await SaveAsync(token);
            Publish(Ready ? "Updates.Ready" : State.Available is null ? "Updates.Current" : "Updates.Available");
        }
        catch (OperationCanceledException error) when (!callerToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
        { Fail("Checking application updates exceeded its deadline; checking can be retried.", error); }
        catch (Exception error) when (error is not OperationCanceledException) { Fail("Checking application updates failed.", error); }
        finally { gate.Release(); }
    }

    public static bool IsNewer(string latest, string current)
    {
        var currentParts = current.Split('-', 2);
        return Version.TryParse(latest, out var candidate) && Version.TryParse(currentParts[0], out var installed) &&
            (candidate > installed || candidate == installed && currentParts.Length == 2);
    }

    public async Task DownloadAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await gate.WaitAsync(token);
        string? partial = null;
        string? validationDirectory = null;
        try
        {
            var release = State.Available ?? throw new InvalidOperationException("No update is available.");
            ValidateRelease(release);
            Publish("Updates.Downloading"); Progress = 0;
            using var checksumResponse = await http.GetAsync(release.Checksum, HttpCompletionOption.ResponseHeadersRead, token);
            checksumResponse.EnsureSuccessStatusCode();
            var checksum = System.Text.Encoding.ASCII.GetString(await GitHubApplicationReleaseSource.ReadBoundedAsync(checksumResponse, 1024, token)).Trim();
            var fields = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var fileName = Path.GetFileName(release.Archive.AbsolutePath);
            if (fields.Length != 2 || fields[1] != fileName || fields[0].Length != 64 || !fields[0].All(Uri.IsHexDigit))
                throw new InvalidDataException("Unexpected checksum asset format.");
            Directory.CreateDirectory(root);
            var staged = Path.Combine(root, fileName); partial = staged + ".download";
            using var response = await http.GetAsync(release.Archive, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > UpdatePackageSecurity.MaximumArchiveBytes) throw new InvalidDataException("Update archive exceeds the size limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; int count; var lastProgress = Stopwatch.StartNew();
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    if (output.Length + count > UpdatePackageSecurity.MaximumArchiveBytes) throw new InvalidDataException("Update archive exceeds the size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if (lastProgress.ElapsedMilliseconds >= 250)
                    { Progress = response.Content.Headers.ContentLength is > 0 ? output.Length * 100d / response.Content.Headers.ContentLength.Value : 0; Changed?.Invoke(this, EventArgs.Empty); lastProgress.Restart(); }
                }
            }
            await UpdatePackageSecurity.VerifyAsync(partial, fields[0], token);
            // Validate all paths before accepting staging; extraction happens again in the external updater.
            validationDirectory = Path.Combine(root, "validate-" + Guid.NewGuid().ToString("N"));
            await UpdatePackageSecurity.ExtractAsync(partial, validationDirectory, token);
            File.Move(partial, staged, true); partial = null;
            State.StagedArchive = staged; State.StagedHash = fields[0]; Progress = 100;
            await SaveAsync(token); Publish("Updates.Ready"); UpdatePrepared?.Invoke(this, release.Version);
        }
        catch (Exception error) when (error is not OperationCanceledException) { Fail("Downloading/verifying the application update failed.", error); }
        finally
        {
            try
            {
                if (partial is not null && File.Exists(partial)) File.Delete(partial);
                if (validationDirectory is not null && Directory.Exists(validationDirectory))
                {
                    UpdatePackageSecurity.EnsureNoReparsePoints(validationDirectory);
                    if (Path.GetDirectoryName(Path.GetFullPath(validationDirectory)) != Path.GetFullPath(root)) throw new IOException("Invalid validation cleanup path.");
                    Directory.Delete(validationDirectory, true);
                }
            }
            catch (Exception error) { logger.Error("Cleaning this operation's temporary update staging failed.", error); }
            finally { gate.Release(); }
        }
    }

    private static void ValidateRelease(ApplicationRelease release)
    {
        var tag = "v" + release.Version;
        if (!Version.TryParse(release.Version, out _) || release.Archive.AbsoluteUri != $"{GitHubApplicationReleaseSource.Repository}/releases/download/{tag}/ModernTubeDownloader-{release.Version}-win-x64.zip" ||
            release.Checksum.AbsoluteUri != release.Archive.AbsoluteUri + ".sha256" || release.Page.AbsoluteUri != $"{GitHubApplicationReleaseSource.Repository}/releases/tag/{tag}")
            throw new InvalidDataException("Unexpected release source.");
    }

    public async Task DeferAsync(bool skip, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try {
        State.RemindAfter = DateTimeOffset.UtcNow.AddDays(1);
        if (skip) State.SkippedVersion = State.Available?.Version;
        await SaveAsync(token); Changed?.Invoke(this, EventArgs.Empty);
        } finally { gate.Release(); }
    }
    public async Task SetInstallOnExitAsync(bool enabled, CancellationToken token = default)
    { await gate.WaitAsync(token); try { State.InstallOnExit = enabled && Ready; await SaveAsync(token); Changed?.Invoke(this, EventArgs.Empty); } finally { gate.Release(); } }

    public async Task LaunchInstallerAsync(bool restart, CancellationToken token = default)
    {
        if (!Ready) throw new InvalidOperationException("No verified staged update.");
        await UpdatePackageSecurity.VerifyAsync(State.StagedArchive!, State.StagedHash!, token);
        var runner = Path.Combine(root, "runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runner);
        var source = Path.Combine(AppContext.BaseDirectory, "updater");
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(runner, Path.GetFileName(file)));
        using var parent = Process.GetCurrentProcess();
        var request = new UpdaterRequest(AppContext.BaseDirectory, State.StagedArchive!, State.StagedHash!, parent.Id, parent.StartTime.ToUniversalTime().Ticks, restart);
        var requestFile = Path.Combine(runner, "request.json");
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request), token);
        var start = new ProcessStartInfo(Path.Combine(runner, "ModernTubeDownloader.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runner };
        start.ArgumentList.Add("--request"); start.ArgumentList.Add(requestFile);
        using var updater = Process.Start(start) ?? throw new IOException("The external updater process could not be started.");
        logger.Info("The verified external updater is waiting for graceful application exit.");
    }

    private Task SaveAsync(CancellationToken token) => AtomicJsonFile.WriteAsync(stateFile, State, token);
    private void Publish(string key) { StatusKey = key; Changed?.Invoke(this, EventArgs.Empty); }
    private void Fail(string message, Exception error) { logger.Error(message, error); Publish("Updates.Failed"); }
    public async Task StopAsync() { lifetime.Cancel(); if (monitor is not null) await monitor; }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); /* StopAsync drains work before disposal. */ }
}
