using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using Xunit.Abstractions;

namespace ModernTubeDownloader.Tests;

public sealed class WorkflowIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("opaque-valid", true, PreferredVideoContainer.Auto)]
    [InlineData("opaque-valid", true, PreferredVideoContainer.Mkv)]
    [InlineData("opaque-invalid", false, PreferredVideoContainer.Auto)]
    [InlineData("opaque-silent", false, PreferredVideoContainer.Auto)]
    [InlineData("opaque-remux-bad", false, PreferredVideoContainer.Mkv)]
    public async Task UnknownCodecs_AreProbedBeforeFinalOutputAndHistory(string format, bool valid,
        PreferredVideoContainer container)
    {
        var paths = AppPaths.Create(Path.Combine(root, format + container));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        try
        {
            var metadata = await services.Metadata.AnalyzeAsync("https://example.test/source/" + format);
            var preset = QualityPreset.Find("best");
            var selection = FormatSelector.Select(metadata, preset, container);
            Assert.True(selection.RequiresStreamProbe);
            var item = DownloadQueueItem.FromMetadata(metadata, preset, selection, container);
            services.Queue.Add(item);
            var terminal = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(25));
            Assert.Equal(valid ? DownloadStatus.Completed : DownloadStatus.Failed, terminal.Status);
            if (valid)
            {
                Assert.True(File.Exists(terminal.FinalFile));
                Assert.Single(services.History.Snapshot());
            }
            else
            {
                Assert.Null(terminal.FinalFile);
                Assert.Empty(services.History.Snapshot());
                Assert.Equal("Error.Download.Format", terminal.FailureMessageKey);
                Assert.True(Directory.EnumerateFiles(services.Settings.Current.TemporaryDirectory,
                    "*.mp4", SearchOption.AllDirectories).Any());
                Assert.Equal(1, terminal.AttemptCount);
            }
        }
        finally { await services.ShutdownAsync(); }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task IncompatibleTimelinePolicies_FailBeforeMediaDownload(
        bool rangeWithSubtitles, bool rangeWithSponsorBlock, bool removalWithSubtitles)
    {
        var paths = AppPaths.Create(Path.Combine(root, $"timeline-{rangeWithSubtitles}-{rangeWithSponsorBlock}-{removalWithSubtitles}"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;
        var item = new DownloadQueueItem
        {
            SourceUrl = "https://example.test/watch/sample123", VideoId = "sample123",
            RequestedRange = rangeWithSubtitles || rangeWithSponsorBlock
                ? new MediaTimeRange(MediaRangeMode.Custom, Start: TimeSpan.FromSeconds(1)) : MediaTimeRange.Full,
            SubtitleOptions = new SubtitleOptions { Enabled = rangeWithSubtitles || removalWithSubtitles, Languages = ["en"] },
            SponsorBlockOptions = new SponsorBlockOptions
                { Mode = rangeWithSponsorBlock ? SponsorBlockMode.Mark : removalWithSubtitles ? SponsorBlockMode.Remove : SponsorBlockMode.Off }
        };
        Assert.Throws<DownloadOptionException>(() => services.Queue.Add(item));
        Assert.Empty(services.Queue.Snapshot());
        Assert.False(Directory.Exists(Path.Combine(paths.RootDirectory, "temp", item.Id.ToString("N"))));
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task SubtitlesCookiesAndSponsorBlockMark_UseOneQueueItemAndSafeArguments()
    {
        var paths = AppPaths.Create(Path.Combine(root, "subtitles-sponsor-mark"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.KeepTemporaryFilesAfterSuccessfulMerge = true;
        var cookieDirectory = Path.Combine(root, "cookie source");
        Directory.CreateDirectory(cookieDirectory);
        var cookiePath = Path.Combine(cookieDirectory, "test cookies.txt");
        await File.WriteAllTextAsync(cookiePath, "# Netscape HTTP Cookie File\n");
        services.Settings.Current.UseCookieFile = true;
        services.Settings.Current.CookieFilePath = cookiePath;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("1080");
        var selectedFormats = FormatSelector.Select(metadata, preset, PreferredVideoContainer.Mkv);
        Assert.True(selectedFormats.RequiresMerge, $"Selected {selectedFormats.VideoFormatId}+{selectedFormats.AudioFormatId}");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, selectedFormats, PreferredVideoContainer.Mkv,
            subtitles: new SubtitleOptions { Enabled = true, Source = SubtitleSource.Both,
                Languages = ["en"], Format = SubtitleFormat.Srt, Embed = true, KeepFiles = true },
            sponsorBlock: new SponsorBlockOptions { Mode = SponsorBlockMode.Mark, Categories = ["sponsor", "intro"] });
        services.Queue.Add(item);
        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(25));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.True(File.Exists(Path.Combine(paths.RootDirectory, "output",
            Path.GetFileNameWithoutExtension(completed.FinalFile) + ".en.srt")));
        var attempt = Assert.Single(Directory.GetDirectories(Path.Combine(paths.RootDirectory, "temp", "sessions", item.JobSessionId.ToString("N"))));
        var mediaArguments = await File.ReadAllTextAsync(Path.Combine(attempt, "combined", "media-arguments.json"));
        var subtitleArguments = await File.ReadAllTextAsync(Path.Combine(attempt, "subtitles", "subtitle-arguments.json"));
        Assert.Contains("--sponsorblock-mark", mediaArguments);
        Assert.Contains("sponsor,intro", mediaArguments);
        var capturedMediaArguments = System.Text.Json.JsonSerializer.Deserialize<string[]>(mediaArguments)!;
        Assert.Equal("137+140", capturedMediaArguments[Array.IndexOf(capturedMediaArguments, "-f") + 1]);
        Assert.Contains("--cookies", mediaArguments);
        Assert.Equal(cookiePath, capturedMediaArguments[Array.IndexOf(capturedMediaArguments, "--cookies") + 1]);
        Assert.Contains("--write-auto-subs", subtitleArguments);
        Assert.Contains("--write-subs", subtitleArguments);
        Assert.Contains("--convert-subs", subtitleArguments);
        var capturedSubtitleArguments = System.Text.Json.JsonSerializer.Deserialize<string[]>(subtitleArguments)!;
        Assert.Equal(cookiePath, capturedSubtitleArguments[Array.IndexOf(capturedSubtitleArguments, "--cookies") + 1]);
        await services.ShutdownAsync();
        services.Dispose();
        var log = string.Join('\n', Directory.EnumerateFiles(paths.LogDirectory, "*.log").Select(File.ReadAllText));
        Assert.DoesNotContain(cookiePath, log);
        Assert.Contains("--cookies <cookie-file>", log);
        Assert.False(Directory.EnumerateFiles(paths.RootDirectory, "test cookies.txt", SearchOption.AllDirectories).Any());
        Assert.Contains("SponsorBlockMode=Mark", log);
    }

    [Fact]
    public async Task SponsorBlockRemove_UsesCombinedDownloadWithoutSubtitles()
    {
        var paths = AppPaths.Create(Path.Combine(root, "sponsor-remove"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.KeepTemporaryFilesAfterSuccessfulMerge = true;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;
        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset),
            sponsorBlock: new SponsorBlockOptions { Mode = SponsorBlockMode.Remove, Categories = ["sponsor"] });
        services.Queue.Add(item);
        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        var attempt = Assert.Single(Directory.GetDirectories(Path.Combine(paths.RootDirectory, "temp", "sessions", item.JobSessionId.ToString("N"))));
        var mediaArguments = await File.ReadAllTextAsync(Path.Combine(attempt, "combined", "media-arguments.json"));
        Assert.Contains("--sponsorblock-remove", mediaArguments);
        Assert.DoesNotContain("--force-keyframes-at-cuts", mediaArguments);
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task MissingSubtitleLanguage_CompletesMediaWithWarningAndNoSidecar()
    {
        var paths = AppPaths.Create(Path.Combine(root, "subtitle-unavailable"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;
        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset),
            subtitles: new SubtitleOptions { Enabled = true, Languages = ["pl"], KeepFiles = true });
        services.Queue.Add(item);
        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.True(completed.HasPostProcessingWarnings);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.Empty(Directory.GetFiles(paths.RootDirectory, "*.pl.*", SearchOption.AllDirectories));
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task CompletedLiveReplay_AnalyzesQueuesAndDownloadsWithFakeTool()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-replay"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/replay");
        Assert.Equal("was_live", metadata.LiveStatus);
        Assert.Equal(MediaAvailabilityKind.Ready, MediaAvailabilityPolicy.Classify(metadata));
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.Single(services.History.Snapshot());
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task FiveVodAndThreeLive_RunFiveDistinctChildProcessesWithIndependentOwners()
    {
        var paths = AppPaths.Create(Path.Combine(root, "five-child-processes"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        try
        {
            services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
            services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
            services.Settings.Current.MaxSimultaneousDownloads = 3;
            services.Settings.Current.MaxSimultaneousLiveRecordings = 2;
            services.Settings.Current.KeepTemporaryFilesAfterSuccessfulMerge = true;
            await services.Settings.SaveAsync();
            await services.Tools.Initialization;
            services.Queue.SetPaused(true);
            for (var index = 0; index < 5; index++)
            {
                var metadata = await services.Metadata.AnalyzeAsync($"https://example.test/watch?id=owned-vod-{index}&hold=1");
                var preset = QualityPreset.Find("720");
                services.Queue.Add(DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset)));
            }
            for (var index = 0; index < 3; index++)
            {
                var metadata = await services.Metadata.AnalyzeAsync($"https://example.test/live/active?behavior=wait&id=owned-live-{index}");
                services.Live.Add(metadata.WebpageUrl, metadata, "720", LiveStartPolicy.FromNow);
            }
            services.Queue.SetPaused(false);
            await WaitUntilAsync(() => Directory.Exists(services.Settings.Current.TemporaryDirectory) &&
                Directory.GetFiles(services.Settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories).Length == 5,
                TimeSpan.FromSeconds(10));
            var markers = Directory.GetFiles(services.Settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories);
            var processIds = await Task.WhenAll(markers.Select(async path => int.Parse(await File.ReadAllTextAsync(path))));
            Assert.Equal(5, processIds.Distinct().Count());
            foreach (var pid in processIds)
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                Assert.False(process.HasExited);
                Assert.Contains("FakeTool", process.ProcessName);
            }
            Assert.Equal(3, services.Processor.ActiveItemIds.Count);
            Assert.Equal(2, services.LiveScheduler.ActiveIds.Count);
            Assert.Single(services.Live.Snapshot(), item => item.State == LiveSessionState.Pending);
            Assert.Equal(5, services.Queue.Snapshot().Count);
            Assert.Equal(3, services.Live.Snapshot().Count);
            Assert.DoesNotContain(services.Queue.Snapshot(), item => item.LiveSession is not null);

            services.Queue.SetPaused(true);
            var vodMarker = markers.First(path => !path.Contains("LiveSessions", StringComparison.Ordinal));
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(vodMarker)!, ".test-finish.json"), "true");
            await WaitUntilAsync(() => services.Queue.Snapshot().Count(item => item.Status == DownloadStatus.Completed) == 1,
                TimeSpan.FromSeconds(10));
            Assert.Equal(2, services.LiveScheduler.ActiveIds.Count);
            Assert.Single(services.Live.Snapshot(), item => item.State == LiveSessionState.Pending);
            Assert.True(services.Live.StopAndSave(services.LiveScheduler.ActiveIds.First()));
            await WaitUntilAsync(() => services.Live.Snapshot().Count(item => item.State == LiveSessionState.Recording) == 2 &&
                !services.Live.Snapshot().Any(item => item.State == LiveSessionState.Pending), TimeSpan.FromSeconds(10));
            Assert.Equal(2, services.Processor.ActiveItemIds.Count);
            await WaitUntilAsync(() => Directory.GetFiles(services.Settings.Current.TemporaryDirectory,
                ".test-started.json", SearchOption.AllDirectories).Length == 6, TimeSpan.FromSeconds(10));
            foreach (var id in services.LiveScheduler.ActiveIds) Assert.True(services.Live.StopAndSave(id));
            await WaitUntilAsync(() => services.Live.Snapshot().All(item => item.State == LiveSessionState.Completed), TimeSpan.FromSeconds(10));
            Assert.Equal(3, services.History.Snapshot().Count(entry => entry.WasLiveRecording));
        }
        catch
        {
            output.WriteLine("VOD states: " + string.Join(", ", services.Queue.Snapshot().Select(item => $"{item.Id}:{item.Status}")));
            output.WriteLine("LIVE states: " + string.Join(", ", services.Live.Snapshot().Select(item =>
                $"{item.Id}:{item.State}:stop={item.StopRequested}:attempt={item.AttemptCount}:failure={item.FailureCategory}")));
            output.WriteLine("Active VOD workers: " + string.Join(", ", services.Processor.ActiveItemIds));
            output.WriteLine("Active LIVE workers: " + string.Join(", ", services.LiveScheduler.ActiveIds));
            foreach (var log in Directory.EnumerateFiles(paths.LogDirectory, "*.log"))
            {
                using var stream = File.Open(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                output.WriteLine(string.Join(Environment.NewLine, reader.ReadToEnd().Split('\n').TakeLast(80)));
            }
            throw;
        }
        finally { await services.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(LiveStartPolicy.FromNow, false)]
    [InlineData(LiveStartPolicy.FromStart, true)]
    public async Task ActiveLive_NaturalEnd_SavesPlayablePartAndHistory(LiveStartPolicy policy, bool fromStartFlag)
    {
        var paths = AppPaths.Create(Path.Combine(root, $"live-natural-{policy}"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var source = "https://example.test/live/active?behavior=natural";
        var metadata = await services.Metadata.AnalyzeAsync(source);
        Assert.Equal(MediaAvailabilityKind.ActiveLive, MediaAvailabilityPolicy.Classify(metadata));
        var preset = QualityPreset.Find("720");
        var item = services.Live.Add(metadata.WebpageUrl, metadata, preset.Id, policy);

        var completed = await WaitForLiveTerminalAsync(services.Live, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(LiveSessionState.Completed, completed.State);
        // Completed is published before worker retirement and its final persistence save.
        await services.ShutdownAsync();
        Assert.Empty(services.LiveScheduler.ActiveIds);
        var persisted = Assert.Single(await new LiveSessionPersistenceService(paths, new NullAppLogger()).LoadAsync());
        Assert.Equal(item.Id, persisted.Id);
        Assert.Equal(LiveSessionState.Completed, persisted.State);
        Assert.Equal(completed.Parts, persisted.Parts);
        Assert.DoesNotContain("manifest_url", await File.ReadAllTextAsync(paths.LiveSessionsFile));
        Assert.True(File.Exists(completed.Parts.Last()));
        Assert.Equal(".mkv", Path.GetExtension(completed.Parts.Last()));
        Assert.Single(completed.Parts);
        Assert.Equal(1, Assert.Single(services.History.Snapshot()).LivePartIndex);
        var argumentsPath = Path.Combine(paths.RootDirectory, "temp", "LiveSessions", item.SessionId.ToString("N"),
            "part-0001", "media-arguments.json");
        var arguments = await File.ReadAllTextAsync(argumentsPath);
        Assert.Equal(fromStartFlag, arguments.Contains("--live-from-start", StringComparison.Ordinal));
        Assert.Contains("--downloader", arguments);
    }

    [Fact]
    public async Task LiveSplitPartial_FinalizesOnlyCommonAlignedAudioVideoRange()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-split-partial"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        try
        {
            services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
            services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
            services.Settings.Current.ResumeLiveOnFailure = false;
            await services.Settings.SaveAsync();
            await services.Tools.Initialization;
            var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=split-fail");
            var session = services.Live.Add(metadata.WebpageUrl, metadata, "1080", LiveStartPolicy.FromNow);
            var partial = await WaitForLiveTerminalAsync(services.Live, session.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(LiveSessionState.Partial, partial.State);
            var saved = Assert.Single(partial.Parts);
            Assert.Contains("duration=6", await File.ReadAllTextAsync(saved));
            Assert.Equal(TimeSpan.FromSeconds(6), partial.RecordedDuration);
            Assert.True(Assert.Single(services.History.Snapshot()).WasPartial);
            Assert.True(File.Exists(Path.Combine(partial.WorkspacePath!, "part-0001", "capture.f137.mp4.part")));
            Assert.True(File.Exists(Path.Combine(partial.WorkspacePath!, "part-0001", "capture.f140.m4a.part")));
        }
        finally { await services.ShutdownAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveCancel_RespectsItsOwnPartialPreservationSetting(bool preserve)
    {
        var paths = AppPaths.Create(Path.Combine(root, $"live-cancel-{preserve}"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        try
        {
            services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
            services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
            services.Settings.Current.PreservePartialLiveOnCancel = preserve;
            await services.Settings.SaveAsync();
            await services.Tools.Initialization;
            var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=wait");
            var session = services.Live.Add(metadata.WebpageUrl, metadata, "720", LiveStartPolicy.FromNow);
            await WaitUntilAsync(() => services.Live.Find(session.Id)?.WorkspacePath is { } path &&
                File.Exists(Path.Combine(path, "part-0001", ".test-started.json")), TimeSpan.FromSeconds(10));
            Assert.True(services.Live.Cancel(session.Id));
            var result = await WaitForLiveTerminalAsync(services.Live, session.Id, TimeSpan.FromSeconds(15));
            Assert.Equal(preserve ? LiveSessionState.Partial : LiveSessionState.Cancelled, result.State);
            Assert.Equal(preserve ? 1 : 0, result.Parts.Count);
            Assert.Equal(preserve ? 1 : 0, services.History.Snapshot().Count);
            if (!preserve) Assert.False(Directory.Exists(result.WorkspacePath));
        }
        finally { await services.ShutdownAsync(); }
    }

    [Fact]
    public async Task ActiveLive_StopAndSave_RecoversPartAndRecordsHistory()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-stop"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=wait");
        var preset = QualityPreset.Find("720");
        var item = services.Live.Add(metadata.WebpageUrl, metadata, preset.Id, LiveStartPolicy.FromNow);
        var partialPath = Path.Combine(paths.RootDirectory, "temp", "LiveSessions", item.SessionId.ToString("N"),
            "part-0001", "capture.mkv.part");
        await WaitUntilAsync(() => File.Exists(partialPath), TimeSpan.FromSeconds(10));
        Assert.True(services.Live.StopAndSave(item.Id));

        var completed = await WaitForLiveTerminalAsync(services.Live, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(LiveSessionState.Completed, completed.State);
        Assert.True(File.Exists(completed.Parts.Last()));
        Assert.Single(services.History.Snapshot());
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData("fail-once", LiveSessionState.Completed, 2)]
    [InlineData("fail-always", LiveSessionState.Partial, 2)]
    public async Task ActiveLive_Disconnection_PreservesVerifiedParts(string behavior, LiveSessionState expected, int parts)
    {
        var paths = AppPaths.Create(Path.Combine(root, $"live-{behavior}"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.RetryFailedDownloads = true;
        services.Settings.Current.AdditionalRetryAttempts = 1;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync($"https://example.test/live/active?behavior={behavior}");
        var preset = QualityPreset.Find("720");
        var item = services.Live.Add(metadata.WebpageUrl, metadata, preset.Id, LiveStartPolicy.FromNow);

        var result = await WaitForLiveTerminalAsync(services.Live, item.Id, TimeSpan.FromSeconds(25));
        Assert.Equal(expected, result.State);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(1, result.ResumeCount);
        Assert.Equal(parts, result.Parts.Count);
        Assert.All(result.Parts, path => Assert.True(File.Exists(path)));
        Assert.Equal(parts, services.History.Snapshot().Count);
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task StopAndSaveDuringLiveReconnect_KeepsVerifiedPartInsteadOfCancelling()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-stop-reconnecting"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.RetryFailedDownloads = true;
        services.Settings.Current.AdditionalRetryAttempts = 1;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=fail-once");
        var preset = QualityPreset.Find("720");
        var item = services.Live.Add(metadata.WebpageUrl, metadata, preset.Id, LiveStartPolicy.FromNow);
        await WaitUntilAsync(() => services.Live.Find(item.Id)?.State == LiveSessionState.Reconnecting,
            TimeSpan.FromSeconds(15));
        Assert.True(services.Live.StopAndSave(item.Id));

        var completed = await WaitForLiveTerminalAsync(services.Live, item.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(LiveSessionState.Completed, completed.State);
        Assert.NotEmpty(completed.Parts);
        Assert.All(completed.Parts, path => Assert.True(File.Exists(path)));
        Assert.NotEmpty(services.History.Snapshot());
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task LiveFinalFailureWithPreservationOff_RemovesOnlyRawWorkspace()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-preservation-off"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.RetryFailedDownloads = false;
        services.Settings.Current.PreservePartialLiveOnFailure = false;
        services.Settings.Current.ResumeLiveOnFailure = false;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=fail-always");
        var preset = QualityPreset.Find("720");
        var item = services.Live.Add(metadata.WebpageUrl, metadata, preset.Id, LiveStartPolicy.FromNow);

        var result = await WaitForLiveTerminalAsync(services.Live, item.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(LiveSessionState.Partial, result.State);
        Assert.True(File.Exists(Assert.Single(result.Parts)));
        Assert.Single(services.History.Snapshot());
        Assert.False(Directory.Exists(Path.Combine(paths.RootDirectory, "temp", "LiveSessions", item.SessionId.ToString("N"))));
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData("1080", PreferredVideoContainer.Mp4, true)]
    [InlineData("720", PreferredVideoContainer.Mkv, false)]
    [InlineData("720", PreferredVideoContainer.WebM, true)]
    public async Task CustomRange_UsesSectionsForEachRequiredStreamAndPreservesHistory(
        string quality, PreferredVideoContainer container, bool separateStreams)
    {
        var paths = AppPaths.Create(Path.Combine(root, $"range-{quality}-{container}"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.KeepTemporaryFilesAfterSuccessfulMerge = true;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/replay");
        if (container == PreferredVideoContainer.WebM)
        {
            metadata.Formats.Add(new MediaFormat { Id = "247", Extension = "webm", Height = 720,
                VideoCodec = "vp9", AudioCodec = "none", VideoBitrateKbps = 2100 });
            metadata.Formats.Add(new MediaFormat { Id = "251", Extension = "webm",
                VideoCodec = "none", AudioCodec = "opus", AudioBitrateKbps = 160 });
        }
        var preset = QualityPreset.Find(quality);
        var range = new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        var selection = FormatSelector.Select(metadata, preset, container);
        Assert.Equal(separateStreams, selection.RequiresMerge);
        var item = DownloadQueueItem.FromMetadata(metadata, preset, selection, container, range);
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.Equal(range, completed.RequestedRange);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.Equal(separateStreams ? 2 : 1, completed.TemporaryFiles.Count);
        foreach (var temporaryFile in completed.TemporaryFiles)
            Assert.Contains("section=*2-10", await File.ReadAllTextAsync(temporaryFile));
        Assert.Equal(range, Assert.Single(services.History.Snapshot()).RequestedRange);
        await services.ShutdownAsync();
        services.Dispose();
        Assert.Contains("FinalDurationSeconds=8", await File.ReadAllTextAsync(
            Assert.Single(Directory.GetFiles(paths.LogDirectory, "*.log"))), StringComparison.Ordinal);

        var queue = new QueuePersistenceService(paths, new NullAppLogger());
        Assert.Equal(range, Assert.Single(await queue.LoadAsync()).RequestedRange);
        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();
        Assert.Equal(range, Assert.Single(history.Snapshot()).RequestedRange);
    }

    [Fact]
    public async Task RetryingFailedItem_DoesNotLoseCustomRange()
    {
        var paths = AppPaths.Create(Path.Combine(root, "range-retry"));
        paths.EnsureCreated();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        var range = new MediaTimeRange(MediaRangeMode.Custom, End: TimeSpan.FromSeconds(10));
        var item = new DownloadQueueItem { SourceUrl = "https://example.test/watch", VideoId = "retry-range", Status = DownloadStatus.Failed, RequestedRange = range };
        queue.Add(item);
        Assert.True(queue.Retry(item.Id));
        Assert.Equal(range, queue.Find(item.Id)?.RequestedRange);
        await queue.SaveAsync();
        Assert.Equal(range, Assert.Single(await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync()).RequestedRange);
    }

    [Fact]
    public async Task Transient403_RetriesWithFreshMetadataAndCompletes()
    {
        var paths = AppPaths.Create(Path.Combine(root, "retry-403"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        var cookiePath = Path.Combine(root, "retry test cookies.txt");
        await File.WriteAllTextAsync(cookiePath, "# Netscape HTTP Cookie File\n");
        services.Settings.Current.UseCookieFile = true;
        services.Settings.Current.CookieFilePath = cookiePath;
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.RetryFailedDownloads = true;
        services.Settings.Current.AdditionalRetryAttempts = 1;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch?id=retry403");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.Equal(2, completed.AttemptCount);
        Assert.True(File.Exists(completed.FinalFile));
        await services.ShutdownAsync();
        services.Dispose();
        var log = string.Join('\n', Directory.EnumerateFiles(paths.LogDirectory, "*.log").Select(File.ReadAllText));
        Assert.True(log.Split("Analyzed media id=retry403", StringSplitOptions.None).Length >= 3,
            "The retry must analyze metadata again before downloading.");
        Assert.Contains($"QueueItemId={item.Id}", log);
        Assert.Contains("Stage=DownloadingVideo", log);
        Assert.Contains("Attempt=1", log);
        Assert.Contains("FailureCategory=HttpForbidden", log);
        Assert.Contains("HttpStatus=403", log);
        Assert.Contains("QualityPreset=720", log);
        Assert.Contains("Container=Auto", log);
        Assert.DoesNotContain("https://example.test/watch?id=retry403", log);
        Assert.DoesNotContain(cookiePath, log);
        Assert.True(log.Split("--cookies <cookie-file>", StringSplitOptions.None).Length >= 4,
            "Initial analysis, media attempt, and retry analysis must all use the cookies file.");
    }

    [Fact]
    public async Task Transient503_ResumesVodPartInStableFormatWorkspace()
    {
        var paths = AppPaths.Create(Path.Combine(root, "resume-vod"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.RetryFailedDownloads = true;
        services.Settings.Current.AdditionalRetryAttempts = 1;
        services.Settings.Current.ResumeInterruptedDownloads = true;
        services.Settings.Current.KeepTemporaryFilesAfterSuccessfulMerge = true;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch?id=resume503");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.Equal(2, completed.AttemptCount);
        Assert.Contains("first-fragments|later-fragments", await File.ReadAllTextAsync(completed.FinalFile!));
        var sessionRoot = Path.Combine(paths.RootDirectory, "temp", "sessions", item.JobSessionId.ToString("N"));
        Assert.Single(Directory.GetDirectories(sessionRoot));
        await services.ShutdownAsync();
        services.Dispose();
        var log = string.Join('\n', Directory.EnumerateFiles(paths.LogDirectory, "*.log").Select(File.ReadAllText));
        Assert.True(log.Split("Analyzed media id=resume503", StringSplitOptions.None).Length >= 3);
    }

    [Fact]
    public async Task PlaylistAnalyzeBatchQueueDownloadAndHistory_PreservePerEntryContext()
    {
        var paths = AppPaths.Create(Path.Combine(root, "playlist"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Queue.SetPaused(true);
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var playlist = await services.Metadata.AnalyzePlaylistAsync("https://example.test/playlist/synthetic");
        Assert.Equal(12, playlist.EntryCount);
        var selected = new PlaylistSelection(playlist);
        Assert.Equal(11, selected.SelectedCount);
        selected.Clear();
        selected.Set(0, true);
        selected.Set(1, true);
        selected.Set(2, true); // Private entry must remain unavailable.
        Assert.Equal(2, selected.SelectedCount);
        var items = selected.CreateQueueItems(QualityPreset.Find("720"));
        var result = services.Queue.AddRangeSkippingDuplicates(items);
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.DuplicatesSkipped);
        Assert.Equal(2, services.Queue.AddRangeSkippingDuplicates(items).DuplicatesSkipped);
        Assert.Equal([1, 2], services.Queue.Snapshot().Select(item => item.PlaylistIndex));
        Assert.All(services.Queue.Snapshot(), item => Assert.Equal(DownloadStatus.Queued, item.Status));

        services.Queue.SetPaused(false);
        foreach (var item in items)
        {
            var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
            Assert.Equal(DownloadStatus.Completed, completed.Status);
            Assert.True(File.Exists(completed.FinalFile));
            Assert.NotNull(completed.SelectedFormats);
            Assert.Equal(item.PlaylistIndex, completed.PlaylistIndex);
        }
        Assert.Equal(2, services.History.Snapshot().Count);
        Assert.Equal([1, 2], services.History.Snapshot().Select(entry => entry.PlaylistIndex).Order());
        await services.ShutdownAsync();

        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();
        Assert.All(history.Snapshot(), entry => Assert.Equal("synthetic-playlist", entry.PlaylistId));
    }

    [Fact]
    public async Task CompletedNotification_PublishesSharedHistoryBeforeTerminalState()
    {
        var paths = AppPaths.Create(Path.Combine(root, "history-completion-order"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;
        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        var observedTerminal = false;
        var missingHistory = false;
        services.Queue.Changed += (_, change) =>
        {
            if (change.ItemId == item.Id && services.Queue.Find(item.Id)?.Status == DownloadStatus.Completed)
            {
                observedTerminal = true;
                missingHistory |= !services.History.Snapshot().Any(entry => entry.QueueItemId == item.Id);
            }
        };
        services.Queue.Add(item);
        await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(20));
        await services.ShutdownAsync();
        Assert.True(observedTerminal);
        Assert.False(missingHistory);
        Assert.Single(services.History.Snapshot());
    }

    [Fact]
    public async Task AnalyzeQueueDownloadMergeAndPersist_CompletesEndToEnd()
    {
        var paths = AppPaths.Create(root);
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(root, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(root, "output");
        services.Settings.Current.KeepTemporaryFilesAfterSuccessfulMerge = false;
        services.Settings.Current.StoreMetadataJson = true;
        services.Settings.Current.SetFileCreationTimeFromMediaPublishDate = true;
        services.Settings.Current.SaveMetadataJsonSidecar = true;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        Assert.True(services.Tools.YtDlp.IsDetected);
        Assert.True(services.Tools.Ffmpeg.IsDetected);
        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        Assert.Equal("sample123", metadata.Id);
        Assert.Equal(3, metadata.Formats.Count);
        var preset = QualityPreset.Find("1080");
        var selection = FormatSelector.Select(metadata, preset);
        var item = DownloadQueueItem.FromMetadata(metadata, preset, selection);
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.NotNull(completed.FinalFile);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.True(new FileInfo(completed.FinalFile!).Length > 0);
        var sidecarPath = Path.ChangeExtension(completed.FinalFile, ".json");
        Assert.Equal(Path.GetFileNameWithoutExtension(completed.FinalFile), Path.GetFileNameWithoutExtension(sidecarPath));
        Assert.True(File.Exists(sidecarPath));
        Assert.Contains("unknown_future_field", await File.ReadAllTextAsync(sidecarPath), StringComparison.Ordinal);
        Assert.Equal(new DateTime(2026, 8, 30), File.GetCreationTime(completed.FinalFile).Date);
        Assert.Single(services.History.Snapshot());
        Assert.True(File.Exists(Path.Combine(paths.MetadataDirectory, "sample123.json")));
        await services.ShutdownAsync();

        var reloadedHistory = new HistoryService(paths, new NullAppLogger());
        await reloadedHistory.LoadAsync();
        Assert.Single(reloadedHistory.Snapshot());
    }

    [Fact]
    public async Task AnalyzeQueueDownloadWithPostProcessingDisabled_LeavesSystemDateAndCreatesNoSidecar()
    {
        var paths = AppPaths.Create(Path.Combine(root, "post-processing-disabled"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.SetFileCreationTimeFromMediaPublishDate = false;
        services.Settings.Current.SaveMetadataJsonSidecar = false;
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        var earliestSystemCreationTime = DateTime.UtcNow.AddMinutes(-1);
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(15));

        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.NotNull(completed.FinalFile);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.False(File.Exists(Path.ChangeExtension(completed.FinalFile, ".json")));
        Assert.True(File.Exists(Path.Combine(paths.MetadataDirectory, "sample123.json")));
        Assert.True(File.GetCreationTimeUtc(completed.FinalFile) >= earliestSystemCreationTime);
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task MkvContainer_RemuxesCombinedInputWithStreamCopyAndKeepsRequestedExtension()
    {
        var paths = AppPaths.Create(Path.Combine(root, "mkv-remux"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var selection = FormatSelector.Select(metadata, preset, PreferredVideoContainer.Mkv);
        var item = DownloadQueueItem.FromMetadata(metadata, preset, selection, PreferredVideoContainer.Mkv);
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.Equal(PreferredVideoContainer.Mkv, completed.PreferredVideoContainer);
        Assert.Equal(".mkv", Path.GetExtension(completed.FinalFile));
        Assert.True(File.Exists(completed.FinalFile));
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task SidecarFailureAfterDownload_MarksCompletedWithWarningAndKeepsVideo()
    {
        var paths = AppPaths.Create(Path.Combine(root, "sidecar-warning"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        services.Settings.Current.SetFileCreationTimeFromMediaPublishDate = false;
        services.Settings.Current.SaveMetadataJsonSidecar = true;
        Directory.CreateDirectory(services.Settings.Current.FinalOutputDirectory);
        Directory.CreateDirectory(Path.Combine(services.Settings.Current.FinalOutputDirectory, "Integration Sample [sample123].json"));
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(15));

        Assert.Equal(DownloadStatus.Completed, completed.Status);
        Assert.True(completed.HasPostProcessingWarnings);
        Assert.Contains("metadata sidecar could not be saved", completed.StatusMessage, StringComparison.Ordinal);
        Assert.NotNull(completed.FinalFile);
        Assert.True(File.Exists(completed.FinalFile));
        Assert.True(new FileInfo(completed.FinalFile).Length > 0);
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task MissingTools_AreReportedWithoutCrashing()
    {
        var paths = AppPaths.Create(Path.Combine(root, "missing"));
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = true;
        settings.Current.CustomYtDlpPath = Path.Combine(root, "does-not-exist", "yt-dlp.exe");
        settings.Current.UseCustomFfmpeg = true;
        settings.Current.CustomFfmpegPath = Path.Combine(root, "does-not-exist", "ffmpeg.exe");
        await settings.SaveAsync();
        using var services = AppServices.Create(paths);
        await services.Tools.Initialization;

        Assert.False(services.Tools.YtDlp.IsDetected);
        Assert.False(services.Tools.Ffmpeg.IsDetected);
        Assert.Contains("could not be validated", services.Tools.YtDlp.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingFfmpeg_DoesNotBlockAnalysisOrCombinedStreamDownload()
    {
        var paths = AppPaths.Create(Path.Combine(root, "degraded-no-ffmpeg"));
        await SaveCustomToolSettingsAsync(paths, missingFfmpeg: true);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        Assert.Equal(DownloadEngineStatus.Degraded, services.Tools.EngineStatus);
        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
        var preset = QualityPreset.Find("720");
        var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
        Assert.False(item.SelectedFormats!.RequiresMerge);
        services.Queue.Add(item);

        var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(15));
        Assert.Equal(DownloadStatus.Completed, completed.Status);
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task SettingsAndInterruptedQueueState_SurviveRestartSafely()
    {
        var paths = AppPaths.Create(Path.Combine(root, "restart"));
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.DefaultQualityPresetId = "720";
        settings.Current.PreferredVideoContainer = PreferredVideoContainer.Mkv;
        settings.Current.CustomYtDlpArguments = "--retries 7";
        await settings.SaveAsync();

        var reloadedSettings = new SettingsService(paths, logger);
        await reloadedSettings.LoadAsync();
        Assert.Equal("720", reloadedSettings.Current.DefaultQualityPresetId);
        Assert.Equal(PreferredVideoContainer.Mkv, reloadedSettings.Current.PreferredVideoContainer);
        Assert.Equal("--retries 7", reloadedSettings.Current.CustomYtDlpArguments);

        var persistence = new QueuePersistenceService(paths, logger);
        await persistence.SaveAsync([
            new DownloadQueueItem
            {
                Id = Guid.NewGuid(),
                SourceUrl = "https://example.test/watch/sample123",
                VideoId = "sample123",
                Title = "Interrupted",
                PreferredVideoContainer = PreferredVideoContainer.WebM,
                Status = DownloadStatus.DownloadingVideo,
                ProgressPercent = 44,
                AttemptCount = 2,
                MaximumAttempts = 3,
                RetryDelaySeconds = 5,
                SelectedFormats = new FormatSelection("137", null, "mp4", null, false, false),
                FailureCategory = "HttpForbidden",
                TemporaryFiles = ["stale-video.part"]
            }
        ]);
        var queue = new DownloadQueueService(persistence, logger);
        await queue.LoadAsync();
        var restored = Assert.Single(queue.Snapshot());
        Assert.Equal(DownloadStatus.Interrupted, restored.Status);
        Assert.Equal(44, restored.ProgressPercent);
        Assert.Equal(0, restored.AttemptCount);
        Assert.Equal(0, restored.RetryDelaySeconds);
        Assert.NotNull(restored.SelectedFormats);
        Assert.Null(restored.FailureCategory);
        Assert.Contains("stale-video.part", restored.TemporaryFiles);
        Assert.Equal(PreferredVideoContainer.WebM, restored.PreferredVideoContainer);
        Assert.Contains("resumable data was preserved", restored.StatusMessage);
    }

    [Theory]
    [InlineData(false, LiveSessionState.Interrupted)]
    [InlineData(true, LiveSessionState.Pending)]
    public async Task RestartDuringLive_PreservesSessionAndDropsSignedManifestJson(
        bool autoResume, LiveSessionState expected)
    {
        var paths = AppPaths.Create(Path.Combine(root, $"live-restart-{autoResume}"));
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.AutoResumeLiveAfterRestart = autoResume;
        await settings.SaveAsync();
        var session = new LiveSession
        {
            PartIndex = 3, ResumeCount = 2, StartPolicy = LiveStartPolicy.FromStart,
            RecordedDuration = TimeSpan.FromMinutes(4)
        };
        var item = new DownloadQueueItem
        {
            VideoId = "live", SourceUrl = "https://example.test/live/active",
            Status = DownloadStatus.RecordingLive, LiveSession = session,
            RawMetadataJson = "{\"manifest_url\":\"https://example.test/signed?token=secret\"}"
        };
        await new QueuePersistenceService(paths, logger).SaveAsync([item]);
        var store = new LiveSessionPersistenceService(paths, logger);
        await store.MigrateLegacyQueueAsync(settings);
        var live = new LiveSessionService(store, settings, logger);
        await live.LoadAsync();
        var restored = Assert.Single(live.Snapshot());
        Assert.Equal(expected, restored.State);
        Assert.Equal(session.SessionId, restored.SessionId);
        Assert.Equal(3, restored.PartIndex);
        Assert.Equal(autoResume ? 3 : 2, restored.ResumeCount);
        Assert.Equal(TimeSpan.FromMinutes(4), restored.RecordedDuration);
        Assert.Empty(await new QueuePersistenceService(paths, logger).LoadAsync());
        Assert.DoesNotContain("manifest_url", await File.ReadAllTextAsync(paths.LiveSessionsFile));
    }

    [Fact]
    public async Task RestartedLive_RecoversOldPartBeforeRecordingNewPart()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-restart-recovery"));
        await SaveCustomToolSettingsAsync(paths);
        using var services = AppServices.Create(paths);
        services.Settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        services.Settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await services.Settings.SaveAsync();
        await services.Tools.Initialization;

        var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=natural");
        // Add a waiting intent so the existing executor cannot start before the raw recovery fixture exists.
        var session = services.Live.Add(metadata.WebpageUrl, metadata, "720", LiveStartPolicy.FromNow, waiting: true);
        services.Live.Update(session.Id, current => { current.State = LiveSessionState.Interrupted; current.RecordedDuration = TimeSpan.FromSeconds(7); });
        var firstPartDirectory = Path.Combine(paths.RootDirectory, "temp", "LiveSessions", session.SessionId.ToString("N"),
            "part-0001");
        Directory.CreateDirectory(firstPartDirectory);
        var firstPart = Path.Combine(firstPartDirectory, "capture.mkv.part");
        await File.WriteAllTextAsync(firstPart, "playable-old-live-fragments");
        Assert.True(services.Live.Resume(session.Id));

        var completed = await WaitForLiveTerminalAsync(services.Live, session.Id, TimeSpan.FromSeconds(20));
        Assert.Equal(LiveSessionState.Completed, completed.State);
        Assert.Equal(3, completed.PartIndex);
        Assert.Equal(2, completed.Parts.Count);
        Assert.Equal([1, 2], services.History.Snapshot().Select(entry => entry.LivePartIndex).Order());
        Assert.All(completed.Parts, path => Assert.True(File.Exists(path)));
        Assert.True(File.Exists(firstPart));
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task AutoResumeAfterGracefulClose_RequeuesPersistedInterruptedLive()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-auto-resume-interrupted"));
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.AutoResumeLiveAfterRestart = true;
        await settings.SaveAsync();
        var session = new LiveSession { PartIndex = 2, ResumeCount = 1,
            SourceUrl = "https://example.test/live/active", State = LiveSessionState.Interrupted };
        var store = new LiveSessionPersistenceService(paths, logger);
        await store.SaveAsync([session]);
        var live = new LiveSessionService(store, settings, logger);
        await live.LoadAsync();
        var restored = Assert.Single(live.Snapshot());
        Assert.Equal(LiveSessionState.Pending, restored.State);
        Assert.Equal(2, restored.PartIndex);
        Assert.Equal(2, restored.ResumeCount);
    }

    [Fact]
    public async Task QueuePauseState_SurvivesRestart()
    {
        var paths = AppPaths.Create(Path.Combine(root, "paused-restart"));
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);

        queue.SetPaused(true);
        await settings.SaveAsync();

        var reloadedSettings = new SettingsService(paths, logger);
        await reloadedSettings.LoadAsync();
        var reloadedQueue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, reloadedSettings);
        Assert.True(reloadedQueue.IsPaused);
    }

    [Fact]
    public async Task QueueDetectsOnlyNonTerminalDuplicates()
    {
        var paths = AppPaths.Create(Path.Combine(root, "duplicates"));
        paths.EnsureCreated();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        var item = new DownloadQueueItem
        {
            VideoId = "sample123",
            QualityPresetId = "1080",
            PreferredVideoContainer = PreferredVideoContainer.Mkv
        };
        queue.Add(item);

        Assert.True(queue.ContainsPendingOrActive("sample123", "1080", PreferredVideoContainer.Mkv));
        item.Status = DownloadStatus.Completed;
        Assert.False(queue.ContainsPendingOrActive("sample123", "1080", PreferredVideoContainer.Mkv));
        item.Status = DownloadStatus.Failed;
        Assert.True(queue.Retry(item.Id));
        Assert.Equal(PreferredVideoContainer.Mkv, queue.Find(item.Id)!.PreferredVideoContainer);
        await queue.SaveAsync();
    }

    [Fact]
    public async Task Persistence_IgnoresNullQueueAndHistoryRecords()
    {
        var paths = AppPaths.Create(Path.Combine(root, "partial-corruption"));
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.QueueFile, "[null]");
        await File.WriteAllTextAsync(paths.HistoryFile, "[null]");

        var logger = new NullAppLogger();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger);
        var history = new HistoryService(paths, logger);
        await queue.LoadAsync();
        await history.LoadAsync();

        Assert.Empty(queue.Snapshot());
        Assert.Empty(history.Snapshot());
    }

    [Fact]
    public async Task SemanticSettingsCorruption_IsNormalizedWithoutDiscardingValidPreferences()
    {
        var paths = AppPaths.Create(Path.Combine(root, "semantic-settings-corruption"));
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFile,
            "{\"language\":null,\"temporaryDirectory\":null,\"finalOutputDirectory\":null," +
            "\"themeMode\":999,\"conflictBehavior\":999,\"storeMetadataJson\":false}");

        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();

        Assert.Equal("en", settings.Current.Language);
        Assert.False(string.IsNullOrWhiteSpace(settings.Current.TemporaryDirectory));
        Assert.False(string.IsNullOrWhiteSpace(settings.Current.FinalOutputDirectory));
        Assert.Equal(Settings.AppThemeMode.System, settings.Current.ThemeMode);
        Assert.Equal(Settings.FileConflictBehavior.Rename, settings.Current.ConflictBehavior);
        Assert.False(settings.Current.StoreMetadataJson);
    }

    [Fact]
    public async Task DisabledThumbnailSetting_PreventsCacheAccess()
    {
        var paths = AppPaths.Create(Path.Combine(root, "thumbnails-disabled"));
        paths.EnsureCreated();
        var enabled = false;
        using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => enabled);

        var image = await thumbnails.GetAsync("https://example.test/thumbnail.jpg");

        Assert.Null(image);
        Assert.Empty(Directory.EnumerateFileSystemEntries(paths.ThumbnailCacheDirectory));
    }

    private static async Task<LiveSession> WaitForLiveTerminalAsync(LiveSessionService live, Guid id, TimeSpan timeout)
    {
        await WaitUntilAsync(() => live.Find(id)?.State is LiveSessionState.Completed or LiveSessionState.Partial
            or LiveSessionState.Cancelled or LiveSessionState.Failed, timeout);
        return live.Find(id)!;
    }

    private static async Task<DownloadQueueItem> WaitForTerminalStateAsync(DownloadQueueService queue, Guid id, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var item = queue.Find(id) ?? throw new InvalidOperationException("Queue item disappeared.");
            if (item.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial)
                return item;
            await Task.Delay(50, cancellation.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
            await Task.Delay(25, cancellation.Token);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    private static async Task SaveCustomToolSettingsAsync(AppPaths paths, bool missingFfmpeg = false)
    {
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = true;
        settings.Current.CustomYtDlpPath = CoreTests.FakeToolPath();
        settings.Current.UseCustomFfmpeg = true;
        settings.Current.CustomFfmpegPath = missingFfmpeg ? Path.Combine(paths.RootDirectory, "missing-ffmpeg.exe") : CoreTests.FakeToolPath();
        settings.Current.CustomFfprobePath = missingFfmpeg ? Path.Combine(paths.RootDirectory, "missing-ffprobe.exe") : CoreTests.FakeToolPath();
        var denoDirectory = Path.Combine(paths.RootDirectory, "custom-deno");
        Directory.CreateDirectory(denoDirectory);
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
            File.Copy(source, Path.Combine(denoDirectory, Path.GetFileName(source)), overwrite: true);
        var denoPath = Path.Combine(denoDirectory, "deno.exe");
        File.Copy(CoreTests.FakeToolPath(), denoPath, overwrite: true);
        settings.Current.UseCustomDeno = true;
        settings.Current.CustomDenoPath = denoPath;
        await settings.SaveAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }
}
