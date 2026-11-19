using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed partial class WorkflowIntegrationTests
{
    [Theory]
    [InlineData("1080", PreferredVideoContainer.Mp4, ".mp4")]
    [InlineData("1080", PreferredVideoContainer.Mkv, ".mkv")]
    [InlineData("audio", PreferredVideoContainer.Auto, ".m4a")]
    public async Task BatchAutomaticPerListFinalizationPreservesMediaJsonSubtitlesHistoryAndLegacyDestination(string quality,
        PreferredVideoContainer container, string extension)
    {
        var paths = AppPaths.Create(Path.Combine(root, "batch-output")); await SaveCustomToolSettingsAsync(paths);
        var preparedSettings = new SettingsService(paths, new NullAppLogger()); await preparedSettings.LoadAsync();
        preparedSettings.Current.QueuePaused = true;
        preparedSettings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "downloads");
        preparedSettings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        preparedSettings.Current.StoreMetadataJson = preparedSettings.Current.SaveMetadataJsonSidecar = true;
        preparedSettings.Current.SubtitleDefaults = new SubtitleOptions { Enabled = quality != "audio", KeepFiles = true, Languages = ["en"], Format = SubtitleFormat.Srt };
        await preparedSettings.SaveAsync();
        using var services = AppServices.Create(paths);
        try
        {
            await services.Tools.Initialization;
            var source = Path.Combine(paths.RootDirectory, "My recordings.txt");
            await File.WriteAllTextAsync(source, "https://example.test/watch/sample123");
            var batch = await services.Batch.ImportAsync(source, quality, container);
            await services.Batch.RunAutomaticAsync(batch);
            var item = Assert.Single(services.Queue.Snapshot());
            Assert.Equal(Path.Combine(preparedSettings.Current.FinalOutputDirectory, "My recordings"), item.OutputDirectory);
            Assert.True(services.Queue.IsPaused); Assert.Equal(DownloadStatus.Queued, item.Status);
            var changedGlobal = Path.Combine(paths.RootDirectory, "new-global");
            services.Settings.Current.FinalOutputDirectory = changedGlobal;
            await services.Settings.SaveAsync();
            Assert.True(await services.Queue.ResumeAsync());
            var completed = await WaitForTerminalStateAsync(services.Queue, item.Id, TimeSpan.FromSeconds(25));
            Assert.Equal(DownloadStatus.Completed, completed.Status);
            Assert.Equal(item.OutputDirectory, Path.GetDirectoryName(completed.FinalFile));
            Assert.Equal(extension, Path.GetExtension(completed.FinalFile));
            Assert.True(File.Exists(completed.FinalFile));
            Assert.Contains("unknown_future_field", await File.ReadAllTextAsync(Path.ChangeExtension(completed.FinalFile!, ".json")));
            Assert.Equal(completed.FinalFile, Assert.Single(services.History.Snapshot()).FinalPath);
            Assert.True(File.Exists(Path.Combine(paths.MetadataDirectory, "sample123.json")));
            if (quality != "audio") Assert.True(File.Exists(Path.ChangeExtension(completed.FinalFile, ".en.srt")));
            await services.Batch.RefreshQueueAsync(batch);
            Assert.Equal(ModernTubeDownloader.Batch.BatchWorkflowPhase.Completed, batch.WorkflowPhase);
            Assert.False(await services.Batch.HasUnfinishedSessionsAsync());

            var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch/sample123");
            var preset = QualityPreset.Find("720");
            var legacy = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset));
            Assert.Null(legacy.OutputDirectory); services.Queue.Add(legacy);
            var legacyCompleted = await WaitForTerminalStateAsync(services.Queue, legacy.Id, TimeSpan.FromSeconds(25));
            Assert.Equal(DownloadStatus.Completed, legacyCompleted.Status);
            Assert.Equal(changedGlobal, Path.GetDirectoryName(legacyCompleted.FinalFile));
            await services.Queue.CheckpointBeforeStartAsync(CancellationToken.None);
            var saved = await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync();
            Assert.Equal(item.OutputDirectory, saved.Single(value => value.Id == item.Id).OutputDirectory);
            Assert.Null(saved.Single(value => value.Id == legacy.Id).OutputDirectory);
        }
        finally { await services.ShutdownAsync(); }
    }
}
