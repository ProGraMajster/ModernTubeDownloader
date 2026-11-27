using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

internal static class Stage3Workload
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task MeasureQueueAsync()
    {
        const int waiting = 10_000;
        const int active = 4;
        var jobs = new DownloadQueueItem[waiting + active];
        for (var i = 0; i < jobs.Length; i++)
        {
            jobs[i] = new()
            {
                Status = i < active ? DownloadStatus.DownloadingVideo : DownloadStatus.Queued,
                SourceUrl = $"https://fixture.invalid/{i}",
                EstimatedSizeBytes = 50_000_000,
                SpeedBytesPerSecond = i < active ? 1_000_000 + i : null
            };
        }
        var queue = new DownloadQueueService(new MemoryQueue(jobs), new NullAppLogger());
        // Loading persisted active jobs deliberately recovers them as interrupted. Feed
        // the same synthetic progress through the normal queue update path afterward.
        await queue.LoadAsync();
        var update = typeof(DownloadQueueService).GetMethod("Update", Internal)!
            .CreateDelegate<Action<Guid, Action<DownloadQueueItem>, bool>>(queue);
        for (var i = 0; i < active; i++)
        {
            int speed = 1_000_000 + i;
            update(jobs[i].Id, job => { job.Status = DownloadStatus.DownloadingVideo; job.SpeedBytesPerSecond = speed; }, false);
        }

        Func<QueueProgressSummary> snapshot = () => QueueProgressSummary.Build(queue.ProgressSnapshot(), queue.IsPaused);
        var aggregate = typeof(DownloadQueueService).GetMethod("ReadProgressSummary", Internal)!
            .CreateDelegate<Func<QueueProgressSummary>>(queue);
        var expected = snapshot();
        var status = typeof(DownloadQueueService).GetMethod("StatusCounts", Internal)!
            .CreateDelegate<Func<(int Waiting, int Active, int Completed)>>(queue)();
        if (status.Waiting != waiting || status.Active != active || expected.Total != jobs.Length || expected != aggregate())
            throw new InvalidOperationException("Queue summaries must agree across the synthetic active and waiting jobs.");

        var evidence = new List<object>();
        const int updates = 500;
        for (var run = 0; run < 6; run++)
        {
            bool existing = run % 2 == 0;
            var summarize = existing ? snapshot : aggregate;
            if (summarize() != expected) throw new InvalidOperationException("Queue summary changed before the workload.");
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            var jobId = jobs[run % active].Id;
            for (var i = 0; i < updates; i++)
            {
                var iteration = i;
                update(jobId, job => { job.ProgressPercent = iteration % 100; job.DownloadedBytes = iteration * 1000L; }, false);
                if (summarize() != expected) throw new InvalidOperationException("Progress updates changed the expected settlement and ETA summary.");
            }
            evidence.Add(new { method = existing ? "snapshot" : "direct", updates, elapsedMs = clock.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { active, waiting, evidence }));
    }

    internal static async Task MeasureLogsAsync(string root, string fake)
    {
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("mtd-automation-smoke-", StringComparison.Ordinal)
            || Directory.Exists(root))
            throw new InvalidOperationException("Use a new isolated synthetic TEMP root.");

        const int batches = 100;
        const int eventsPerLevel = 384;
        var operation = Guid.NewGuid();
        var modes = new[] { "Standard", "Debug", "Trace" };
        var levels = new[] { DiagnosticLevel.Info, DiagnosticLevel.Debug, DiagnosticLevel.Trace };
        foreach (var mode in modes)
        {
            var paths = AppPaths.Create(Path.Combine(root, mode));
            paths.EnsureCreated();
            var tools = Directory.CreateDirectory(Path.Combine(paths.RootDirectory, "fake-tools")).FullName;
            foreach (var name in Directory.GetFiles(fake, "ModernTubeDownloader.FakeTool.*"))
                File.Copy(name, Path.Combine(tools, Path.GetFileName(name)));
            foreach (var name in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" })
                File.Copy(Path.Combine(fake, "ModernTubeDownloader.FakeTool.exe"), Path.Combine(tools, name));
            var settings = new SettingsService(paths, new NullAppLogger());
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = Path.Combine(tools, "yt-dlp.exe");
            settings.Current.CustomFfmpegPath = Path.Combine(tools, "ffmpeg.exe");
            settings.Current.CustomFfprobePath = Path.Combine(tools, "ffprobe.exe");
            settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe");
            settings.Current.Updates.AutomaticChecks = false;
            settings.Current.Updates.PolicyVersion = 1;
            await settings.SaveAsync();
            using var process = Process.GetCurrentProcess();
            using var logger = new FileAppLogger(paths.LogDirectory);
            var setDetail = typeof(FileAppLogger).GetMethod("SetDetail", Internal)!.CreateDelegate<Action<bool, bool>>(logger);
            setDetail(mode != "Standard", mode == "Trace");
            var initialCpu = process.TotalProcessorTime;
            var initialAllocation = GC.GetTotalAllocatedBytes();
            var clock = Stopwatch.StartNew();
            for (var batch = 0; batch < batches; batch++)
            {
                for (var item = 0; item < eventsPerLevel; item++)
                {
                    var counts = new DiagnosticCounters(Items: item, Total: eventsPerLevel, Queue: 10_000, Active: 4);
                    foreach (var level in levels)
                        logger.Event(new(DiagnosticModule.Queue, DiagnosticStage.Run, DiagnosticOutcome.Progress, level, operation,
                            DurationMs: item, Counts: counts));
                }
                await Task.Delay(12);
            }
            logger.Dispose();
            process.Refresh();
            var cpu = (process.TotalProcessorTime - initialCpu).TotalMilliseconds;
            var bytesAllocated = GC.GetTotalAllocatedBytes() - initialAllocation;
            var wall = clock.Elapsed.TotalMilliseconds;
            var workingSet = process.WorkingSet64;
            var privateBytes = process.PrivateMemorySize64;
            var logs = Directory.GetFiles(paths.LogDirectory, "MTD-events-v1-*.log");
            int events = 0, drops = 0;
            foreach (var file in logs)
            {
                foreach (var line in File.ReadLines(file))
                {
                    using var payload = JsonDocument.Parse(line);
                    var entry = payload.RootElement;
                    if (entry.GetProperty("eventName").GetString() != "Logging.Run.Skipped") { events++; continue; }
                    var counts = entry.GetProperty("counts");
                    if (counts.TryGetProperty("dropped", out var normal)) drops += normal.GetInt32();
                    if (counts.TryGetProperty("errors", out var priority)) drops += priority.GetInt32();
                }
            }
            if (events + drops != batches * eventsPerLevel * (Array.IndexOf(modes, mode) + 1))
                throw new InvalidOperationException("Written events and logger drop records did not account for the workload.");
            using var offline = new HttpClient(new LocalOnlyHandler());
            using var services = AppServices.Create(paths, httpClient: offline, deferWorkStart: true);
            await services.Tools.Initialization;
            var diagnostics = typeof(AppServices).GetProperty("Diagnostics", Internal)!.GetValue(services)!;
            var export = diagnostics.GetType().GetMethod("ExportAsync", Internal)!
                .CreateDelegate<Func<string, bool, CancellationToken, Task<(int, int)>>>(diagnostics);
            var zip = Path.Combine(paths.RootDirectory, "synthetic-diagnostics.zip");
            clock.Restart();
            var (included, excluded) = await export(zip, true, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, events, drops, logBytes = logs.Sum(file => new FileInfo(file).Length),
                cpu, wall, bytesAllocated, workingSet, privateBytes, included, excluded,
                exportMs = clock.Elapsed.TotalMilliseconds, exportBytes = new FileInfo(zip).Length }));
        }
    }

    private sealed class LocalOnlyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Network access is unavailable in this benchmark.");
    }

    private sealed class MemoryQueue(DownloadQueueItem[] jobs) : IQueuePersistence
    {
        public Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadQueueItem>>(jobs);

        public Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
