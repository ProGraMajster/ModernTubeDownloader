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
    public string? LastInstallerRequest { get; set; }
    public int? LastInstallerProcessId { get; set; }
    public long? LastInstallerStartUtcTicks { get; set; }
    public bool InstallationFailed { get; set; }
    public string? AutomaticInstallSuppressedVersion { get; set; }
    public string? LastStartupAttemptVersion { get; set; }
    public DateTimeOffset? LastStartupAttemptAt { get; set; }
    public string? LastStartupResultKey { get; set; }
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

public sealed partial class ApplicationUpdateService : IDisposable
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
    private bool disposed, initialized;
    public ApplicationUpdateState State { get; private set; } = new();
    public string StatusKey { get; private set; } = "Updates.Idle";
    public double Progress { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler<string>? UpdatePrepared;
    public string CurrentVersion { get; }
    public bool StateUnavailable { get; private set; }
    private bool HasStagedArchive => State.StagedArchive is not null && State.StagedHash is not null && File.Exists(State.StagedArchive);
    public bool Ready => initialized && !StateUnavailable && HasStagedArchive;
    public bool InstallationPending => State.LastInstallerRequest is not null && !State.InstallationFailed;
    public bool ShouldInstallOnExit => Ready && State.InstallOnExit && !IsDeferred && !State.InstallationFailed && !InstallationPending;
    private bool IsDeferred => State.Available is null || State.Available.Version == State.SkippedVersion || State.RemindAfter > DateTimeOffset.UtcNow;

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
        await gate.WaitAsync(token);
        try
        {
            if (initialized) return;
            await InitializeCoreAsync(token);
            if (!StateUnavailable && pendingStartupResultKey is { } startupResult)
            {
                State.LastStartupResultKey = startupResult;
                await SaveAsync(token);
                pendingStartupResultKey = null;
            }
            initialized = true;
        }
        finally { gate.Release(); }
        // Panels exist before the background monitor finishes restoring state.
        // Publish after admission is released, including when checks are disabled.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitializeCoreAsync(CancellationToken token)
    {
        try
        {
            FileAttributes? attributes;
            try { attributes = File.GetAttributes(stateFile); }
            catch (FileNotFoundException) { attributes = null; }
            catch (DirectoryNotFoundException) { attributes = null; }
            if (attributes is null) State = new();
            else
            {
                if ((attributes.Value & FileAttributes.Directory) != 0) throw new IOException("An update state file is occupied by a directory.");
                State = await AtomicJsonFile.ReadAsync<ApplicationUpdateState>(stateFile, token) ?? throw new InvalidDataException("Empty stored update state.");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            State = new(); StateUnavailable = true;
            Fail("Update state could not be read; its original contents are retained and automatic updating is disabled until the state is repaired.", error);
            return;
        }
        try
        {
            if (State.Available is not null) ValidateRelease(State.Available);
            if (State.Available is null || !IsNewer(State.Available.Version, CurrentVersion))
            { State.Available = null; ClearStaging(); State.InstallationFailed = false; }
            else if (State.StagedArchive is not null)
            {
                ValidateStagedPath();
                await UpdatePackageSecurity.VerifyAsync(State.StagedArchive, State.StagedHash ?? "", token);
            }
            if (State.LastInstallerRequest is { } request)
            {
                if (!IsOwnedPath(request) || Path.GetFileName(request) != "request.json") throw new InvalidDataException("Unexpected installer result path.");
                UpdatePackageSecurity.EnsureNoReparsePoints(request);
                var result = request + ".result.json";
                UpdatePackageSecurity.EnsureNoReparsePoints(result);
                if (File.Exists(result))
                {
                    if (new FileInfo(result).Length > 65536) throw new InvalidDataException("Installer result exceeds the limit.");
                    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result, token));
                    State.InstallationFailed = !json.RootElement.GetProperty("success").GetBoolean();
                    if (State.LastStartupResultKey == "Updates.StartupInstalling")
                        State.LastStartupResultKey = State.InstallationFailed ? "Updates.InstallFailed" : "Updates.Current";
                    State.LastInstallerRequest = null;
                    State.LastInstallerProcessId = null; State.LastInstallerStartUtcTicks = null;
                    State.InstallOnExit = false;
                    await SaveAsync(token);
                }
                else if (!InstallerProcessIsRunning())
                {
                    State.LastInstallerRequest = null; State.InstallationFailed = true; State.InstallOnExit = false;
                    if (State.LastStartupResultKey == "Updates.StartupInstalling")
                        State.LastStartupResultKey = "Updates.InstallFailed";
                    State.LastInstallerProcessId = null; State.LastInstallerStartUtcTicks = null;
                    await SaveAsync(token);
                }
            }
            if (IsDeferred || State.InstallationFailed) State.InstallOnExit = false;
            StatusKey = State.InstallationFailed ? "Updates.InstallFailed" : InstallationPending ? "Updates.InstallPending" : HasStagedArchive ? "Updates.Ready" : "Updates.Idle";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            ClearStaging(); State.LastInstallerRequest = null; StateUnavailable = true;
            Fail("Stored update state or staged package failed validation; no installation is armed.", error);
        }
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
                    await RunAutomaticCycleAsync(lifetime.Token);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error) { Fail("This application update attempt failed; the monitor will retry later.", error); }
                await Task.Delay(TimeSpan.FromMinutes(15), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { Fail("The application update monitor failed.", error); }
    }

    internal async Task RunAutomaticCycleAsync(CancellationToken token = default)
    {
        await InitializeAsync(token);
        if (StateUnavailable) return;
        var options = settings.Current.Updates;
        if (!options.AutomaticChecks || InstallationPending) return;
        var interval = options.Frequency == UpdateCheckFrequency.Weekly ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        if (!State.LastCheck.HasValue || DateTimeOffset.UtcNow - State.LastCheck >= interval) await CheckAsync(token);
        // Settings may change while the network operation is in progress.
        options = settings.Current.Updates;
        if (!options.AutomaticChecks || options.Mode == UpdateInstallMode.NotifyOnly || IsDeferred || State.InstallationFailed) return;
        if (!Ready) await DownloadAsync(token);
        await gate.WaitAsync(token);
        try
        {
            options = settings.Current.Updates;
            if (Ready && options.AutomaticChecks && options.Mode == UpdateInstallMode.InstallOnExit && !IsDeferred && !State.InstallationFailed && !InstallationPending && State.AutomaticInstallSuppressedVersion != State.Available?.Version)
            { State.InstallOnExit = true; await SaveAsync(token); Changed?.Invoke(this, EventArgs.Empty); }
        }
        finally { gate.Release(); }
    }

    public async Task ApplyPolicyAsync(bool automaticChecks, UpdateCheckFrequency frequency, UpdateInstallMode mode, CancellationToken token = default, bool? installAtStartup = null)
    {
        if (!Enum.IsDefined(frequency) || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await InitializeAsync(token);
        await gate.WaitAsync(token);
        try
        {
            if (StateUnavailable)
            {
                // A user can still disable checks or record preferences without
                // overwriting unreadable Skip/Defer/installer evidence.
                var unavailableOptions = settings.Current.Updates;
                unavailableOptions.AutomaticChecks = automaticChecks; unavailableOptions.Frequency = frequency;
                unavailableOptions.Mode = mode; unavailableOptions.PolicyVersion = 1;
                if (installAtStartup.HasValue) unavailableOptions.InstallAtStartup = installAtStartup.Value;
                await settings.SaveAsync(token); Changed?.Invoke(this, EventArgs.Empty); return;
            }
            // Disarm first, even when a later settings write fails. A staged package
            // must not remain armed under a policy the user was trying to replace.
            State.InstallOnExit = false;
            State.AutomaticInstallSuppressedVersion = State.Available?.Version;
            await SaveAsync(token);
            var options = settings.Current.Updates;
            options.AutomaticChecks = automaticChecks; options.Frequency = frequency; options.Mode = mode; options.PolicyVersion = 1;
            if (installAtStartup.HasValue) options.InstallAtStartup = installAtStartup.Value;
            await settings.SaveAsync(token);
            State.AutomaticInstallSuppressedVersion = null;
            State.InstallOnExit = automaticChecks && mode == UpdateInstallMode.InstallOnExit && Ready && !IsDeferred && !State.InstallationFailed && !InstallationPending;
            await SaveAsync(token);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { gate.Release(); }
    }

    public async Task CheckAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var callerToken = token;
        await InitializeAsync(linked.Token);
        if (StateUnavailable) return;
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
            { ClearStaging(); State.InstallationFailed = false; }
            State.Available = available;
            State.LastCheck = DateTimeOffset.UtcNow;
            await SaveAsync(token);
            Publish(State.InstallationFailed ? "Updates.InstallFailed" : Ready ? "Updates.Ready" : State.Available is null ? "Updates.Current" : "Updates.Available");
        }
        catch (OperationCanceledException error) when (!callerToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
        { Fail("Checking application updates exceeded its deadline; checking can be retried.", error); }
        catch (Exception error) when (error is not OperationCanceledException) { Fail("Checking application updates failed.", error); }
        finally { gate.Release(); }
    }

    public static bool IsNewer(string latest, string current)
    {
        var currentParts = current.Split('+', 2)[0].Split('-', 2);
        return IsStableVersion(latest) && Version.TryParse(latest, out var candidate) && Version.TryParse(currentParts[0], out var installed) &&
            (candidate > installed || candidate == installed && currentParts.Length == 2);
    }

    public async Task DownloadAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await InitializeAsync(token);
        if (StateUnavailable) return;
        await gate.WaitAsync(token);
        string? partial = null;
        string? validationDirectory = null;
        try
        {
            var release = State.Available ?? throw new InvalidOperationException("No update is available.");
            ValidateRelease(release);
            if (Ready)
            {
                ValidateStagedPath();
                await UpdatePackageSecurity.VerifyAsync(State.StagedArchive!, State.StagedHash!, token);
                Publish(State.InstallationFailed ? "Updates.InstallFailed" : "Updates.Ready");
                return;
            }
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
        catch (Exception error) when (error is not OperationCanceledException) { ClearStaging(); Fail("Downloading/verifying the application update failed.", error); }
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
        if (!IsStableVersion(release.Version) || release.Archive.AbsoluteUri != $"{GitHubApplicationReleaseSource.Repository}/releases/download/{tag}/ModernTubeDownloader-{release.Version}-win-x64.zip" ||
            release.Checksum.AbsoluteUri != release.Archive.AbsoluteUri + ".sha256" || release.Page.AbsoluteUri != $"{GitHubApplicationReleaseSource.Repository}/releases/tag/{tag}")
            throw new InvalidDataException("Unexpected release source.");
    }

    private static bool IsStableVersion(string version) => Regex.IsMatch(version, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$");
    private bool InstallerProcessIsRunning()
    {
        if (State.LastInstallerProcessId is not { } id || State.LastInstallerStartUtcTicks is not { } ticks) return false;
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    private bool IsOwnedPath(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private void ValidateStagedPath()
    {
        var expected = Path.Combine(root, Path.GetFileName(State.Available!.Archive.AbsolutePath));
        if (State.StagedArchive is not { } path || !Path.GetFullPath(path).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected staged archive path.");
        UpdatePackageSecurity.EnsureNoReparsePoints(path);
    }
    private void ClearStaging() { State.StagedArchive = null; State.StagedHash = null; State.InstallOnExit = false; }

    public async Task DeferAsync(bool skip, CancellationToken token = default)
    {
        await InitializeAsync(token); EnsureStateWritable();
        await gate.WaitAsync(token);
        try {
        State.RemindAfter = DateTimeOffset.UtcNow.AddDays(1);
        if (skip) State.SkippedVersion = State.Available?.Version;
        State.InstallOnExit = false;
        await SaveAsync(token); Changed?.Invoke(this, EventArgs.Empty);
        } finally { gate.Release(); }
    }
    public async Task SetInstallOnExitAsync(bool enabled, CancellationToken token = default)
    {
        await InitializeAsync(token); EnsureStateWritable();
        await gate.WaitAsync(token);
        try
        {
            State.InstallOnExit = enabled && Ready && !InstallationPending;
            State.AutomaticInstallSuppressedVersion = enabled ? null : State.Available?.Version;
            if (State.InstallOnExit) { State.RemindAfter = null; State.SkippedVersion = null; State.InstallationFailed = false; }
            await SaveAsync(token); Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { gate.Release(); }
    }

    // Narrow process boundary used by isolated tests; the production path always
    // starts the packaged updater copied to its owned runner directory.
    internal string InstallerSourceDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "updater");
    internal Func<ProcessStartInfo, Process?> InstallerProcessFactory { get; set; } = Process.Start;
    internal Action<Process> InstallerProcessStopper { get; set; } = process => process.Kill();
    private bool startupAbortBlocked;
    internal bool StartupAbortBlocked
    {
        get
        {
            if (!startupAbortBlocked) return false;
            try { if (InstallerProcessIsRunning()) return true; }
            catch { return true; } // An unverifiable child is still pending.
            startupAbortBlocked = false;
            return false;
        }
    }

    public Task LaunchInstallerAsync(bool restart, CancellationToken token = default, bool startup = false)
        => LaunchInstallerCoreAsync(restart, token, startup, null, null);

    internal Task LaunchStartupInstallerAsync(Func<bool> workStillAllowsInstallation, Action commitShutdown, CancellationToken token = default)
        => LaunchInstallerCoreAsync(true, token, true, workStillAllowsInstallation, commitShutdown);

    private async Task LaunchInstallerCoreAsync(bool restart, CancellationToken token, bool startup,
        Func<bool>? workStillAllowsInstallation, Action? commitShutdown)
    {
        await InitializeAsync(token); EnsureStateWritable();
        await gate.WaitAsync(token);
        if (InstallationPending) { gate.Release(); throw new InvalidOperationException("An updater process is already pending."); }
        Process? updater = null;
        var committed = false;
        string? startupCommitMarker = null;
        try
        {
        if (!Ready) throw new InvalidOperationException("No verified staged update.");
        if (startup && !CanInstallAtStartup) throw new InvalidOperationException("Startup installation is no longer eligible.");
        CheckStartupAdmission();
        ValidateRelease(State.Available ?? throw new InvalidDataException("No release metadata."));
        ValidateStagedPath();
        if (!IsNewer(State.Available.Version, CurrentVersion)) throw new InvalidDataException("The staged release cannot replace this version.");
        await UpdatePackageSecurity.VerifyAsync(State.StagedArchive!, State.StagedHash!, token);
        var runner = Path.Combine(root, "runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runner);
        var source = InstallerSourceDirectory;
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(runner, Path.GetFileName(file)));
        using var parent = Process.GetCurrentProcess();
        var request = new UpdaterRequest(AppContext.BaseDirectory, State.StagedArchive!, State.StagedHash!, parent.Id, parent.StartTime.ToUniversalTime().Ticks, restart,
            startup ? Guid.NewGuid().ToString("N") : null);
        var requestFile = Path.Combine(runner, "request.json");
        if (startup) startupCommitMarker = StartupUpdateAdmission.MarkerPath(requestFile);
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request), token);
        CheckStartupAdmission();
        State.LastInstallerRequest = requestFile; State.InstallationFailed = false; State.InstallOnExit = false;
        if (startup)
        {
            State.LastStartupAttemptVersion = State.Available.Version;
            State.LastStartupAttemptAt = DateTimeOffset.UtcNow;
            State.LastStartupResultKey = "Updates.StartupInstalling";
        }
        await SaveAsync(token);
        var start = new ProcessStartInfo(Path.Combine(runner, "ModernTubeDownloader.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runner };
        start.ArgumentList.Add("--request"); start.ArgumentList.Add(requestFile);
        updater = InstallerProcessFactory(start) ?? throw new IOException("The external updater process could not be started.");
        State.LastInstallerProcessId = updater.Id; State.LastInstallerStartUtcTicks = updater.StartTime.ToUniversalTime().Ticks;
        await SaveAsync(CancellationToken.None);
        CheckStartupAdmission();
        if (startup && updater.HasExited) throw new IOException("The startup updater exited before shutdown admission.");
        if (startupCommitMarker is not null)
        {
            // This newly created marker is bound to this request's unique token.
            // A crash before this point leaves an inert child, even if it later
            // observes this parent exiting. No await opens a guard/commit gap.
            using var marker = new FileStream(startupCommitMarker, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough);
            marker.Write(System.Text.Encoding.UTF8.GetBytes(request.StartupCommitToken!));
            marker.Flush(flushToDisk: true);
        }
        // No await separates the final guard from shutdown admission. MainForm
        // passes a synchronous UI callback, so external activation cannot slip
        // between this guard and the point at which shutdown becomes authoritative.
        commitShutdown?.Invoke();
        committed = true;
        logger.Info("The verified external updater is waiting for graceful application exit.");
        }
        catch (Exception error)
        {
            if (committed) throw;
            // Only this operation's directly retained child can be stopped. It is
            // still waiting for this live parent and has not begun installation.
            // Termination and marker revocation must be independent: a locked
            // marker must never prevent stopping the owned waiting process.
            var childStopped = updater is null;
            if (updater is not null)
            {
                try
                {
                    if (!updater.HasExited) { InstallerProcessStopper(updater); await updater.WaitForExitAsync(); }
                    childStopped = updater.HasExited;
                }
                catch (Exception stopError) { logger.Error("Stopping the owned waiting updater failed.", stopError); }
            }
            var markerRevoked = false;
            if (startupCommitMarker is not null)
            {
                try { File.Delete(startupCommitMarker); markerRevoked = true; }
                catch (Exception markerError) { logger.Error("Revoking this startup handoff commitment failed.", markerError); }
            }
            if (startup && !childStopped && !markerRevoked)
            {
                // Preserve the exact pending identity. Ordinary shutdown must not
                // release an armed child after a cancelled handoff. The block may
                // clear only after this child's exit can be verified.
                startupAbortBlocked = true;
                State.InstallOnExit = false; State.InstallationFailed = false;
                State.LastStartupResultKey = "Updates.StartupAbortBlocked";
                try { await SaveAsync(CancellationToken.None); }
                catch (Exception persistenceError) { logger.Error("The unresolved updater identity could not be saved again.", persistenceError); }
                Publish("Updates.StartupAbortBlocked");
                throw new IOException("The pending startup updater could neither be stopped nor disarmed; normal exit is blocked.", error);
            }
            State.InstallOnExit = false; State.InstallationFailed = true; State.LastInstallerRequest = null;
            State.LastInstallerProcessId = null; State.LastInstallerStartUtcTicks = null;
            if (startup) State.LastStartupResultKey = error is OperationCanceledException ? "Updates.StartupDeferred" : "Updates.StartupFailed";
            await SaveAsync(CancellationToken.None); Publish("Updates.InstallFailed"); throw;
        }
        finally { updater?.Dispose(); gate.Release(); }

        void CheckStartupAdmission()
        {
            if (!startup) return;
            token.ThrowIfCancellationRequested();
            if (!settings.Current.Updates.AutomaticChecks || !settings.Current.Updates.InstallAtStartup || IsDeferred ||
                State.AutomaticInstallSuppressedVersion == State.Available?.Version || workStillAllowsInstallation?.Invoke() == false)
                throw new OperationCanceledException("Startup installation was interrupted before shutdown admission.", token);
        }
    }

    private void EnsureStateWritable()
    {
        if (StateUnavailable) throw new InvalidDataException("Stored update state is unavailable; the original state must be repaired before this operation.");
    }
    private Task SaveAsync(CancellationToken token)
    {
        EnsureStateWritable();
        return AtomicJsonFile.WriteRawJsonAsync(stateFile, JsonSerializer.Serialize(State, JsonDefaults.Options), token);
    }
    private void Publish(string key) { StatusKey = key; Changed?.Invoke(this, EventArgs.Empty); }
    private void Fail(string message, Exception error) { logger.Error(message, error); Publish("Updates.Failed"); }
    public async Task StopAsync() { lifetime.Cancel(); if (monitor is not null) await monitor; }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); /* StopAsync drains work before disposal. */ }
}
