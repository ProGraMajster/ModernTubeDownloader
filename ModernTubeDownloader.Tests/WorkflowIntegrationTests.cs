using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class WorkflowIntegrationTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));

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
    public async Task SettingsAndInterruptedQueueState_SurviveRestartSafely()
    {
        var paths = AppPaths.Create(Path.Combine(root, "restart"));
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.DefaultQualityPresetId = "720";
        settings.Current.CustomYtDlpArguments = "--retries 7";
        await settings.SaveAsync();

        var reloadedSettings = new SettingsService(paths, logger);
        await reloadedSettings.LoadAsync();
        Assert.Equal("720", reloadedSettings.Current.DefaultQualityPresetId);
        Assert.Equal("--retries 7", reloadedSettings.Current.CustomYtDlpArguments);

        var persistence = new QueuePersistenceService(paths, logger);
        await persistence.SaveAsync([
            new DownloadQueueItem
            {
                Id = Guid.NewGuid(),
                SourceUrl = "https://example.test/watch/sample123",
                VideoId = "sample123",
                Title = "Interrupted",
                Status = DownloadStatus.DownloadingVideo,
                ProgressPercent = 44
            }
        ]);
        var queue = new DownloadQueueService(persistence, logger);
        await queue.LoadAsync();
        var restored = Assert.Single(queue.Snapshot());
        Assert.Equal(DownloadStatus.Queued, restored.Status);
        Assert.Equal(0, restored.ProgressPercent);
        Assert.Contains("Recovered", restored.StatusMessage);
    }

    private static async Task<DownloadQueueItem> WaitForTerminalStateAsync(DownloadQueueService queue, Guid id, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (true)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var item = queue.Find(id) ?? throw new InvalidOperationException("Queue item disappeared.");
            if (item.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled)
                return item;
            await Task.Delay(50, cancellation.Token);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    private static async Task SaveCustomToolSettingsAsync(AppPaths paths)
    {
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = true;
        settings.Current.CustomYtDlpPath = CoreTests.FakeToolPath();
        settings.Current.UseCustomFfmpeg = true;
        settings.Current.CustomFfmpegPath = CoreTests.FakeToolPath();
        settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        await settings.SaveAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }
}
