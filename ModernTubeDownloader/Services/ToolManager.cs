using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class ToolManagerState
{
    public DateTimeOffset? LastToolUpdateCheck { get; set; }
    public ToolInstallationState? YtDlp { get; set; }
    public ToolInstallationState? Ffmpeg { get; set; }
    public ToolInstallationState? PendingYtDlp { get; set; }
    public ToolInstallationState? PendingFfmpeg { get; set; }
}

public sealed class ToolInstallationState
{
    public string ReleaseIdentity { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ExecutableRelativePath { get; set; } = string.Empty;
    public string? FfprobeRelativePath { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ToolUsageLease : IAsyncDisposable
{
    private Func<ValueTask>? release;

    internal ToolUsageLease(ExternalToolKind kind, string executablePath, string? ffprobePath, Func<ValueTask> release)
    {
        Kind = kind;
        ExecutablePath = executablePath;
        FfprobePath = ffprobePath;
        this.release = release;
    }

    public ExternalToolKind Kind { get; }
    public string ExecutablePath { get; }
    public string? FfprobePath { get; }

    public ValueTask DisposeAsync() => Interlocked.Exchange(ref release, null)?.Invoke() ?? ValueTask.CompletedTask;
}

public sealed class ToolManager
{
    public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly AppPaths paths;
    private readonly SettingsService settings;
    private readonly IToolReleaseSource releaseSource;
    private readonly ToolPackageInstaller installer;
    private readonly IToolBinaryValidator validator;
    private readonly IAppLogger logger;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object stateLock = new();
    private readonly Dictionary<ExternalToolKind, int> usageCounts = new()
    {
        [ExternalToolKind.YtDlp] = 0,
        [ExternalToolKind.Ffmpeg] = 0
    };
    private ToolManagerState persistedState = new();
    private Task? initializationTask;
    private Task? backgroundUpdateTask;

    public ToolManager(
        AppPaths paths,
        SettingsService settings,
        IToolReleaseSource releaseSource,
        ToolPackageInstaller installer,
        IToolBinaryValidator validator,
        IAppLogger logger,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.paths = paths;
        this.settings = settings;
        this.releaseSource = releaseSource;
        this.installer = installer;
        this.validator = validator;
        this.logger = logger;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ToolInfo YtDlp { get; private set; } = ToolInfo.Missing(ExternalToolKind.YtDlp, "Preparing yt-dlp.");
    public ToolInfo Ffmpeg { get; private set; } = ToolInfo.Missing(ExternalToolKind.Ffmpeg, "Preparing FFmpeg.");
    public event EventHandler? Changed;

    public DownloadEngineStatus EngineStatus
    {
        get
        {
            lock (stateLock)
            {
                if (YtDlp.Installed && Ffmpeg.Installed) return DownloadEngineStatus.Ready;
                if (YtDlp.Status == ToolStatus.Failed || Ffmpeg.Status == ToolStatus.Failed) return DownloadEngineStatus.Failed;
                return DownloadEngineStatus.Preparing;
            }
        }
    }

    public Task Initialization => Start();

    public Task Start()
    {
        lock (stateLock)
            return initializationTask ??= InitializeAsync();
    }

    public async Task StopAsync()
    {
        lifetimeCancellation.Cancel();
        Task? task;
        lock (stateLock) task = initializationTask;
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        Task? updateTask;
        lock (stateLock) updateTask = backgroundUpdateTask;
        if (updateTask is not null)
        {
            try { await updateTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.Error("Background tool update check ended during shutdown.", ex); }
        }
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await Start().WaitAsync(cancellationToken).ConfigureAwait(false);
        ToolInfo ytDlp;
        ToolInfo ffmpeg;
        lock (stateLock)
        {
            ytDlp = YtDlp;
            ffmpeg = Ffmpeg;
        }
        if (!ytDlp.Installed || !ffmpeg.Installed)
            throw new InvalidOperationException(ytDlp.ErrorMessage ?? ffmpeg.ErrorMessage ?? "The download engine is not ready.");
    }

    public async Task<ToolUsageLease> AcquireAsync(ExternalToolKind kind, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        lock (stateLock)
        {
            var tool = GetInfo(kind);
            if (!tool.Installed || string.IsNullOrWhiteSpace(tool.ExecutablePath))
                throw new InvalidOperationException(tool.ErrorMessage ?? $"{tool.ToolName} is not ready.");
            usageCounts[kind]++;
            return new ToolUsageLease(kind, tool.ExecutablePath, tool.FfprobePath, () => ReleaseAsync(kind));
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => ReconfigureAsync(cancellationToken);

    public async Task ReconfigureAsync(CancellationToken cancellationToken = default)
    {
        await Start().WaitAsync(cancellationToken).ConfigureAwait(false);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InspectConfiguredToolAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            await InspectConfiguredToolAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            await ProvisionIfMissingAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            await ProvisionIfMissingAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task RetryAsync(CancellationToken cancellationToken = default)
    {
        await Start().WaitAsync(cancellationToken).ConfigureAwait(false);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!YtDlp.Installed) await InspectConfiguredToolAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            if (!Ffmpeg.Installed) await InspectConfiguredToolAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            await ProvisionIfMissingAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            await ProvisionIfMissingAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task CheckForUpdatesAsync(bool force = true, CancellationToken cancellationToken = default)
    {
        await Start().WaitAsync(cancellationToken).ConfigureAwait(false);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && persistedState.LastToolUpdateCheck is { } last && utcNow() - last < UpdateCheckInterval)
                return;
            await CheckToolForUpdateAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            await CheckToolForUpdateAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            persistedState.LastToolUpdateCheck = utcNow();
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task InitializeAsync()
    {
        await operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                persistedState = await AtomicJsonFile.ReadAsync<ToolManagerState>(paths.ToolStateFile).ConfigureAwait(false) ?? new ToolManagerState();
            }
            catch (Exception ex)
            {
                logger.Error("Could not read the managed-tool state; installed paths will be rediscovered through a fresh install.", ex);
                persistedState = new ToolManagerState();
            }
            ApplyPendingAtStartup();
            var cancellationToken = lifetimeCancellation.Token;
            await InspectConfiguredToolAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            await InspectConfiguredToolAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            await ProvisionIfMissingAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
            await ProvisionIfMissingAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.Error("Tool manager initialization failed.", ex);
        }
        finally
        {
            operationGate.Release();
        }

        if (EngineStatus == DownloadEngineStatus.Ready &&
            (persistedState.LastToolUpdateCheck is null || utcNow() - persistedState.LastToolUpdateCheck >= UpdateCheckInterval))
        {
            lock (stateLock)
                backgroundUpdateTask = Task.Run(() => CheckForUpdatesAsync(force: false, lifetimeCancellation.Token));
        }
    }

    private void ApplyPendingAtStartup()
    {
        if (persistedState.PendingYtDlp is not null)
        {
            persistedState.YtDlp = persistedState.PendingYtDlp;
            persistedState.PendingYtDlp = null;
        }
        if (persistedState.PendingFfmpeg is not null)
        {
            persistedState.Ffmpeg = persistedState.PendingFfmpeg;
            persistedState.PendingFfmpeg = null;
        }
    }

    private async Task InspectConfiguredToolAsync(ExternalToolKind kind, CancellationToken cancellationToken)
    {
        var custom = GetCustomConfiguration(kind);
        if (custom.Enabled)
        {
            try
            {
                var executable = ResolveExecutable(custom.Executable, kind == ExternalToolKind.YtDlp ? "yt-dlp.exe" : "ffmpeg.exe");
                var ffprobe = kind == ExternalToolKind.Ffmpeg ? ResolveCustomFfprobe(executable, custom.Ffprobe) : null;
                var version = await validator.ValidateAsync(kind, executable, ffprobe, cancellationToken).ConfigureAwait(false);
                SetInfo(new ToolInfo
                {
                    Kind = kind,
                    ToolName = ToolName(kind),
                    Status = ToolStatus.Ready,
                    Installed = true,
                    InstalledVersion = version,
                    ExecutablePath = executable,
                    FfprobePath = ffprobe,
                    ManagedByApplication = false,
                    LastChecked = persistedState.LastToolUpdateCheck
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetInfo(ToolInfo.Missing(kind, $"Custom {ToolName(kind)} could not be validated: {ex.Message}") with
                {
                    Status = ToolStatus.Failed,
                    ManagedByApplication = false
                });
            }
            return;
        }

        var installation = GetInstallation(kind);
        if (installation is null)
        {
            SetInfo(ToolInfo.Missing(kind, $"{ToolName(kind)} is not installed yet."));
            return;
        }
        try
        {
            var executable = ResolveManagedPath(installation.ExecutableRelativePath);
            var ffprobe = installation.FfprobeRelativePath is { Length: > 0 } relative ? ResolveManagedPath(relative) : null;
            var version = await validator.ValidateAsync(kind, executable, ffprobe, cancellationToken).ConfigureAwait(false);
            SetInfo(new ToolInfo
            {
                Kind = kind,
                ToolName = ToolName(kind),
                Status = ToolStatus.Ready,
                Installed = true,
                InstalledVersion = string.IsNullOrWhiteSpace(installation.Version) ? version : installation.Version,
                ExecutablePath = executable,
                FfprobePath = ffprobe,
                ManagedByApplication = true,
                LastChecked = persistedState.LastToolUpdateCheck
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning($"Stored {ToolName(kind)} installation is invalid: {ex.Message}");
            SetInstallation(kind, null);
            SetInfo(ToolInfo.Missing(kind, $"The stored {ToolName(kind)} installation is invalid and will be replaced."));
        }
    }

    private async Task ProvisionIfMissingAsync(ExternalToolKind kind, CancellationToken cancellationToken)
    {
        if (GetInfo(kind).Installed || GetCustomConfiguration(kind).Enabled)
            return;
        await InstallLatestAsync(kind, isUpdate: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckToolForUpdateAsync(ExternalToolKind kind, CancellationToken cancellationToken)
    {
        var current = GetInfo(kind);
        if (!current.ManagedByApplication || !current.Installed)
            return;
        SetInfo(current with { Status = ToolStatus.CheckingForUpdates, ErrorMessage = null });
        try
        {
            var release = await releaseSource.GetLatestAsync(kind, cancellationToken).ConfigureAwait(false);
            var active = GetInstallation(kind);
            if (active is not null && string.Equals(active.ReleaseIdentity, release.ReleaseIdentity, StringComparison.Ordinal))
            {
                SetInfo(GetInfo(kind) with
                {
                    Status = ToolStatus.Ready,
                    LatestVersion = release.DisplayVersion,
                    LastChecked = utcNow(),
                    UpdateAvailable = false,
                    ErrorMessage = null
                });
                return;
            }
            SetInfo(GetInfo(kind) with
            {
                Status = ToolStatus.UpdateAvailable,
                LatestVersion = release.DisplayVersion,
                LastChecked = utcNow(),
                UpdateAvailable = true
            });
            await InstallReleaseAsync(release, isUpdate: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error($"Could not update {ToolName(kind)}; the active version is unchanged.", ex);
            var preserved = GetInfo(kind);
            SetInfo(preserved with
            {
                Status = ToolStatus.Failed,
                Installed = preserved.Installed,
                LastChecked = utcNow(),
                ErrorMessage = $"Update failed; the installed version remains active. {ex.Message}"
            });
        }
    }

    private async Task InstallLatestAsync(ExternalToolKind kind, bool isUpdate, CancellationToken cancellationToken)
    {
        try
        {
            SetInfo(GetInfo(kind) with { Status = isUpdate ? ToolStatus.Updating : ToolStatus.Downloading, ErrorMessage = null });
            var release = await releaseSource.GetLatestAsync(kind, cancellationToken).ConfigureAwait(false);
            await InstallReleaseAsync(release, isUpdate, cancellationToken).ConfigureAwait(false);
            persistedState.LastToolUpdateCheck = utcNow();
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error($"Could not install {ToolName(kind)}.", ex);
            var current = GetInfo(kind);
            SetInfo(current with
            {
                Status = ToolStatus.Failed,
                ErrorMessage = current.Installed
                    ? $"Update failed; the installed version remains active. {ex.Message}"
                    : $"Automatic setup failed: {ex.Message}"
            });
        }
    }

    private async Task InstallReleaseAsync(ToolReleaseInfo release, bool isUpdate, CancellationToken cancellationToken)
    {
        var kind = release.Kind;
        var previous = GetInfo(kind);
        SetInfo(previous with
        {
            Status = isUpdate ? ToolStatus.Updating : ToolStatus.Downloading,
            LatestVersion = release.DisplayVersion,
            UpdateAvailable = isUpdate,
            DownloadedBytes = 0,
            TotalBytes = release.Size,
            ProgressPercent = 0,
            ErrorMessage = null
        });
        long lastPublishedBytes = -1;
        var progress = new InlineProgress<ToolDownloadProgress>(value =>
        {
            if (lastPublishedBytes >= 0 && value.DownloadedBytes - lastPublishedBytes < 1024 * 1024 &&
                value.DownloadedBytes != value.TotalBytes)
                return;
            lastPublishedBytes = value.DownloadedBytes;
            var current = GetInfo(kind);
            SetInfo(current with
            {
                Status = isUpdate ? ToolStatus.Updating : ToolStatus.Downloading,
                DownloadedBytes = value.DownloadedBytes,
                TotalBytes = value.TotalBytes,
                ProgressPercent = value.Percent
            });
        });
        var prepared = await installer.PrepareAsync(release, progress, () =>
        {
            if (!isUpdate)
                SetInfo(GetInfo(kind) with { Status = ToolStatus.Installing, ProgressPercent = 100 });
        }, cancellationToken).ConfigureAwait(false);
        var installation = ToState(prepared);
        var pending = false;
        lock (stateLock)
        {
            if (isUpdate && usageCounts[kind] > 0)
            {
                SetPendingInstallationUnsafe(kind, installation);
                pending = true;
            }
            else
            {
                SetInstallationUnsafe(kind, installation);
                SetPendingInstallationUnsafe(kind, null);
            }
        }
        if (pending)
        {
            SetInfo(previous with
            {
                Status = ToolStatus.UpdatePending,
                LatestVersion = release.DisplayVersion,
                UpdateAvailable = true,
                ErrorMessage = "The verified update will activate when the current operation finishes."
            });
        }
        else
        {
            SetInfo(new ToolInfo
            {
                Kind = kind,
                ToolName = ToolName(kind),
                Status = ToolStatus.Ready,
                Installed = true,
                InstalledVersion = prepared.Version,
                LatestVersion = release.DisplayVersion,
                ExecutablePath = prepared.ExecutablePath,
                FfprobePath = prepared.FfprobePath,
                ManagedByApplication = true,
                LastChecked = utcNow()
            });
        }
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask ReleaseAsync(ExternalToolKind kind)
    {
        var shouldActivate = false;
        lock (stateLock)
        {
            usageCounts[kind] = Math.Max(0, usageCounts[kind] - 1);
            shouldActivate = usageCounts[kind] == 0 && GetPendingInstallationUnsafe(kind) is not null;
        }
        if (shouldActivate)
            _ = ActivatePendingAsync(kind);
        return ValueTask.CompletedTask;
    }

    private async Task ActivatePendingAsync(ExternalToolKind kind)
    {
        await operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ToolInstallationState? pending;
            lock (stateLock)
            {
                if (usageCounts[kind] != 0 || (pending = GetPendingInstallationUnsafe(kind)) is null)
                    return;
                SetInstallationUnsafe(kind, pending);
                SetPendingInstallationUnsafe(kind, null);
            }
            await InspectConfiguredToolAsync(kind, CancellationToken.None).ConfigureAwait(false);
            await SaveStateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"Could not activate the pending {ToolName(kind)} update.", ex);
            var current = GetInfo(kind);
            SetInfo(current with { Status = ToolStatus.Failed, ErrorMessage = $"Pending update activation failed: {ex.Message}" });
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        try { await AtomicJsonFile.WriteAsync(paths.ToolStateFile, persistedState, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.Error("Could not persist managed-tool state.", ex); }
    }

    private (bool Enabled, string? Executable, string? Ffprobe) GetCustomConfiguration(ExternalToolKind kind) => kind switch
    {
        ExternalToolKind.YtDlp => (settings.Current.UseCustomYtDlp, settings.Current.CustomYtDlpPath, null),
        _ => (settings.Current.UseCustomFfmpeg, settings.Current.CustomFfmpegPath, settings.Current.CustomFfprobePath)
    };

    private static string ResolveExecutable(string? configured, string fileName)
    {
        if (string.IsNullOrWhiteSpace(configured)) throw new FileNotFoundException($"No custom path was provided for {fileName}.");
        var path = Directory.Exists(configured) ? Path.Combine(configured, fileName) : configured;
        return Path.GetFullPath(path);
    }

    private static string ResolveCustomFfprobe(string ffmpegPath, string? configured)
    {
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe")
            : ResolveExecutable(configured, "ffprobe.exe");
        return Path.GetFullPath(path);
    }

    private string ResolveManagedPath(string relative)
    {
        var root = Path.GetFullPath(paths.ToolsDirectory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(paths.ToolsDirectory, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Managed tool state contains an unsafe path.");
        return full;
    }

    private ToolInstallationState ToState(PreparedToolInstallation installation) => new()
    {
        ReleaseIdentity = installation.ReleaseIdentity,
        Version = installation.Version,
        ExecutableRelativePath = Path.GetRelativePath(paths.ToolsDirectory, installation.ExecutablePath),
        FfprobeRelativePath = installation.FfprobePath is { } path ? Path.GetRelativePath(paths.ToolsDirectory, path) : null,
        Sha256 = installation.Sha256
    };

    private ToolInfo GetInfo(ExternalToolKind kind)
    {
        lock (stateLock) return kind == ExternalToolKind.YtDlp ? YtDlp : Ffmpeg;
    }

    private void SetInfo(ToolInfo info)
    {
        lock (stateLock)
        {
            if (info.Kind == ExternalToolKind.YtDlp) YtDlp = info;
            else Ffmpeg = info;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ToolInstallationState? GetInstallation(ExternalToolKind kind)
    {
        lock (stateLock) return kind == ExternalToolKind.YtDlp ? persistedState.YtDlp : persistedState.Ffmpeg;
    }

    private void SetInstallation(ExternalToolKind kind, ToolInstallationState? value)
    {
        lock (stateLock) SetInstallationUnsafe(kind, value);
    }

    private void SetInstallationUnsafe(ExternalToolKind kind, ToolInstallationState? value)
    {
        if (kind == ExternalToolKind.YtDlp) persistedState.YtDlp = value;
        else persistedState.Ffmpeg = value;
    }

    private ToolInstallationState? GetPendingInstallationUnsafe(ExternalToolKind kind) =>
        kind == ExternalToolKind.YtDlp ? persistedState.PendingYtDlp : persistedState.PendingFfmpeg;

    private void SetPendingInstallationUnsafe(ExternalToolKind kind, ToolInstallationState? value)
    {
        if (kind == ExternalToolKind.YtDlp) persistedState.PendingYtDlp = value;
        else persistedState.PendingFfmpeg = value;
    }

    private static string ToolName(ExternalToolKind kind) => kind == ExternalToolKind.YtDlp ? "yt-dlp" : "FFmpeg";

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
