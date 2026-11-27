using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Services;

internal sealed class DiagnosticsService : IDisposable
{
    private readonly AppServices services;
    private readonly FileAppLogger logger;
    private readonly CancellationTokenSource stop = new();
    private readonly long startupMs;
    private readonly Task sampling;
    private bool enabled;
    private int disposed;
    internal bool WriteFailed => logger.LastError != 0;

    internal DiagnosticsService(AppServices services, FileAppLogger logger, long startupMs)
    {
        this.services = services;
        this.logger = logger;
        this.startupMs = startupMs;
        enabled = services.Settings.Current.Diagnostics.Advanced;
        logger.SetDetail(enabled, services.Settings.Current.Diagnostics.IncludeTrace);
        services.Settings.Changed += SettingsChanged;
        sampling = Task.Run(SampleAsync);
    }

    private void SettingsChanged(object? sender, EventArgs args)
    {
        var config = services.Settings.Current.Diagnostics;
        logger.SetDetail(config.Advanced, config.IncludeTrace);
        if (config.Advanced && !enabled) Snapshot();
        enabled = config.Advanced;
    }

    internal void Snapshot() => _ = Task.Run(() =>
    {
        try
        {
            if (stop.IsCancellationRequested) return;
            var snapshot = CaptureEnvironment();
            logger.Event(new(DiagnosticModule.Application, DiagnosticStage.Snapshot, DiagnosticOutcome.Completed,
                DurationMs: startupMs, Counts: CollectionState()) { Snapshot = snapshot });
        }
        catch (Exception error)
        {
            logger.Event(new(DiagnosticModule.Application, DiagnosticStage.Snapshot, DiagnosticOutcome.Unavailable,
                DiagnosticLevel.Warn, Exception: error));
        }
    });

    private DiagnosticCounters CollectionState() => new(History: services.History.Count, Queue: services.Queue.Count,
        Live: services.Live.Count, Active: ProcessActivity.Count + (services.Organizer.IsBusy ? 1 : 0));

    private async Task SampleAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        using var process = Process.GetCurrentProcess();
        var previousTick = Stopwatch.GetTimestamp();
        var previousCpu = process.TotalProcessorTime;
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                if (!logger.IsEnabled(DiagnosticLevel.Debug)) { previousTick = 0; continue; }
                try
                {
                    process.Refresh();
                    var cpu = process.TotalProcessorTime;
                    var elapsed = previousTick == 0 ? 0 : Stopwatch.GetElapsedTime(previousTick).TotalMilliseconds;
                    var percent = elapsed <= 0 ? (double?)null :
                        Math.Clamp((cpu - previousCpu).TotalMilliseconds * 100 / elapsed / Environment.ProcessorCount, 0, 100);
                    previousTick = Stopwatch.GetTimestamp();
                    previousCpu = cpu;
                    var counts = CollectionState() with
                    {
                        WorkingSet = process.WorkingSet64, PrivateMemory = process.PrivateMemorySize64, ManagedHeap = GC.GetTotalMemory(false),
                        Threads = process.Threads.Count, CpuPercent = percent,
                        Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2)
                    };
                    logger.Event(new(DiagnosticModule.Performance, DiagnosticStage.Sample, DiagnosticOutcome.Completed, DiagnosticLevel.Debug, Counts: counts));
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    logger.Event(new(DiagnosticModule.Performance, DiagnosticStage.Sample, DiagnosticOutcome.Unavailable, DiagnosticLevel.Debug, Exception: error));
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    internal Task<long> SizeAsync() => Task.Run(() => DiagnosticLogFiles.List(services.Paths.LogDirectory).Sum(DiagnosticLogFiles.Length) +
        InstallerLogs(DateTime.UtcNow.AddDays(-14)).Sum(DiagnosticLogFiles.Length));

    internal Task<int> RemoveOldAsync() => Task.Run(() =>
    {
        var removed = DiagnosticLogFiles.RemoveOld(services.Paths.LogDirectory, DateTimeOffset.UtcNow);
        logger.Event(new(DiagnosticModule.Logging, DiagnosticStage.Retention, DiagnosticOutcome.Completed, Counts: new(Files: removed)));
        return removed;
    });

    internal string Summary() => $"ModernTubeDownloader {FileAppLogger.ApplicationVersion}\n" +
        $"Windows {Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})\n" +
        $".NET {Environment.Version} ({RuntimeInformation.ProcessArchitecture})\n" +
        $"UI: {services.Appearance.InterfaceMode}; {services.Localization.Language}\n" +
        $"Queue: {services.Queue.Count}; History: {services.History.Count}; LIVE: {services.Live.Count}";

    private DiagnosticEnvironment CaptureEnvironment()
    {
        var assembly = typeof(DiagnosticsService).Assembly;
        var framework = AppBranding.FrameworkVersion.Split('+', 2);
        string? Metadata(string name) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(value => value.Key == name)?.Value;
        return new(new
        {
            version = AppBranding.Version, assemblyVersion = assembly.GetName().Version?.ToString(),
            informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            commit = Metadata("SourceCommit"), configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            modernFormsNext = framework[0], modernFormsNextCommit = framework.Length == 2 ? framework[1] : null,
            dotnet = RuntimeInformation.FrameworkDescription, processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ui = services.Appearance.InterfaceMode.ToString(), theme = services.Appearance.Profile.Global.Theme.ToString(),
            language = services.Localization.Language, startupMs,
            migration = new { setup = services.Experience.State.Setup.ToString(), existingProfile = services.Experience.State.ExistingProfile,
                settingsReadable = !services.Settings.LoadFailed, historyReadable = !services.History.LoadFailed, liveReadable = !services.Live.LoadFailed },
            counts = CollectionState(),
            nativeHostHealthRecorded = File.Exists(Path.Combine(services.Paths.AuthenticationDirectory, "bridge-health.json"))
        }, DiagnosticHardware.Read(), new[]
        {
            DiagnosticHardware.ReadDrive("Profile", services.Paths.RootDirectory),
            DiagnosticHardware.ReadDrive("Installation", AppContext.BaseDirectory),
            DiagnosticHardware.ReadDrive("Downloads", services.Settings.Current.FinalOutputDirectory),
            DiagnosticHardware.ReadDrive("Temporary", services.Settings.Current.TemporaryDirectory)
        }, GetAssemblyVersions());
    }

    private static object GetAssemblyVersions()
    {
        return new[]
        {
            "ModernTubeDownloader.dll", "ModernTubeDownloader.Authentication.dll", "ModernFormsNext.dll",
            "ModernFormsNext.WindowKit.dll", "ModernFormsNext.WindowKit.Backend.Windows.dll",
            "ModernFormsNext.SystemNotifications.WinRT.dll", "ModernFormsNext.SystemNotifications.AppSdk.dll",
            "updater/ModernTubeDownloader.Updater.dll", "native-host/ModernTubeDownloader.NativeHost.dll"
        }.Select(name =>
        {
            try { return new { name, status = "Available", version = AssemblyName.GetAssemblyName(Path.Combine(AppContext.BaseDirectory, name)).Version?.ToString() }; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException)
            { return new { name, status = error is FileNotFoundException or DirectoryNotFoundException ? "Missing" : "Unavailable", version = (string?)null }; }
        }).ToArray();
    }

    internal Task<(int Included, int Excluded)> ExportAsync(string destination, bool all, CancellationToken token) =>
        Task.Run(() => ExportCoreAsync(destination, all, token), token);

    private async Task<(int Included, int Excluded)> ExportCoreAsync(string destination, bool all, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var watch = Stopwatch.StartNew();
        logger.Event(new(DiagnosticModule.Logging, DiagnosticStage.Export, DiagnosticOutcome.Started, OperationId: id));
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, $".mtd-diagnostics-{id:N}.tmp");
        var created = false;
        try
        {
            var files = DiagnosticLogFiles.List(services.Paths.LogDirectory);
            var since = DateTime.UtcNow.AddDays(all ? -14 : -1);
            var eligible = files.Where(file => DiagnosticLogFiles.IsEventFile(file.Name) && file.LastWriteTimeUtc >= since).ToArray();
            var updater = InstallerLogs(since);
            var available = files.Length + updater.Length;
            int included = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                created = true;
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                var environment = CaptureEnvironment();
                Write("system-info.json", environment.System);
                Write("application-info.json", environment.Application);
                Write("assembly-versions.json", environment.Assemblies);
                Write("storage-summary.json", environment.Storage);
                var settings = services.Settings.Current;
                Write("settings-redacted.json", new { language = services.Localization.Language, settings.MaxSimultaneousDownloads,
                    settings.Updates.AutomaticChecks, mode = settings.Updates.Mode.ToString(), settings.Updates.InstallAtStartup,
                    settings.Diagnostics.Advanced, settings.Diagnostics.IncludeTrace, webRemoteEnabled = settings.EnableWebRemote });
                var lines = new List<string>(3);
                foreach (var file in eligible)
                {
                    token.ThrowIfCancellationRequested();
                    if (await CopyEventsAsync(file, archive, "logs", included, token).ConfigureAwait(false)) ++included;
                }
                foreach (var file in updater)
                {
                    token.ThrowIfCancellationRequested();
                    if (await CopyEventsAsync(file, archive, "updater", included, token).ConfigureAwait(false)) ++included;
                }
                lines.Add(Summary());
                lines.Add($"Structured log files included: {included}. Files excluded by age, format, access or safety: {available - included}.");
                using var text = new StreamWriter(archive.CreateEntry("diagnostic-summary.txt").Open());
                text.Write(string.Join("\n\n", lines));

                void Write(string name, object value)
                {
                    token.ThrowIfCancellationRequested();
                    using var entry = archive.CreateEntry(name).Open();
                    JsonSerializer.Serialize(entry, value, DiagnosticRecord.Json);
                }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            created = false;
            logger.Event(new(DiagnosticModule.Logging, DiagnosticStage.Export, DiagnosticOutcome.Completed, OperationId: id,
                DurationMs: watch.ElapsedMilliseconds, Counts: new(Files: included, Errors: available - included)));
            return (included, available - included);
        }
        catch (Exception error)
        {
            logger.Event(new(DiagnosticModule.Logging, DiagnosticStage.Export,
                error is OperationCanceledException ? DiagnosticOutcome.Cancelled : DiagnosticOutcome.Failed,
                error is OperationCanceledException ? DiagnosticLevel.Info : DiagnosticLevel.Error,
                OperationId: id, DurationMs: watch.ElapsedMilliseconds, Exception: error));
            throw;
        }
        finally
        {
            if (created)
                try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private FileInfo[] InstallerLogs(DateTime since)
    {
        try
        {
            var root = new DirectoryInfo(Path.Combine(services.Paths.RootDirectory, "Updates"));
            if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint)) return [];
            return root.EnumerateDirectories("runner-*")
                .Where(directory => directory.Name.Length == 39 && Guid.TryParseExact(directory.Name[7..], "N", out _) &&
                    !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(directory => new FileInfo(Path.Combine(directory.FullName, "request.json.diagnostics.jsonl")))
                .Where(file => file.Exists && file.LastWriteTimeUtc >= since && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderByDescending(file => file.LastWriteTimeUtc).Take(64).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static async Task<bool> CopyEventsAsync(FileInfo file, ZipArchive zip, string folder, int index, CancellationToken token)
    {
        FileStream input;
        try
        {
            if (File.GetAttributes(file.FullName).HasFlag(FileAttributes.ReparsePoint)) return false;
            input = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 32768, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        using (input)
        {
            // Read one bounded prefix. A partial last line may belong to a concurrent
            // append, and unsupported or corrupt lines never reach the archive.
            var length = input.Length;
            if (length is <= 0 or > (25L << 20)) return false;
            var buffer = new byte[32768];
            var line = new byte[FileAppLogger.MaximumLine];
            var used = 0;
            StreamWriter? output = null;
            try
            {
                while (length > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), token).ConfigureAwait(false);
                    if (read == 0) break;
                    length -= read;
                    for (var indexInBuffer = 0; indexInBuffer < read; indexInBuffer++)
                    {
                        if (buffer[indexInBuffer] != '\n')
                        {
                            if (used < line.Length) line[used++] = buffer[indexInBuffer];
                            continue;
                        }
                        var value = used < line.Length ? DiagnosticRecord.ReadForExport(Encoding.UTF8.GetString(line, 0, used)) : null;
                        used = 0;
                        if (value is null) continue;
                        token.ThrowIfCancellationRequested();
                        output ??= new StreamWriter(zip.CreateEntry($"{folder}/events-{index:D3}.jsonl").Open());
                        await output.WriteLineAsync(JsonSerializer.Serialize(value, DiagnosticRecord.Json).AsMemory(), token).ConfigureAwait(false);
                    }
                }
                return output is not null;
            }
            finally { if (output is not null) await output.DisposeAsync().ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        services.Settings.Changed -= SettingsChanged;
        stop.Cancel();
        _ = sampling.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
