using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Services;

public sealed class DownloadJobExecutor(
    AppPaths paths,
    SettingsService settings,
    YtDlpProcessRunner ytDlpRunner,
    FfmpegService ffmpeg,
    HistoryService history,
    IAppLogger logger)
{
    private readonly YtDlpDownloadService downloader = new(ytDlpRunner, settings, logger);

    public async Task ExecuteAsync(
        DownloadQueueItem item,
        Action<Action<DownloadQueueItem>, bool> update,
        CancellationToken cancellationToken)
    {
        var selection = item.SelectedFormats ?? throw new InvalidOperationException("The queue item has no selected formats.");
        FileSystemUtilities.EnsureWritableDirectory(settings.Current.TemporaryDirectory);
        FileSystemUtilities.EnsureWritableDirectory(settings.Current.FinalOutputDirectory);

        var attemptDirectory = Path.Combine(
            settings.Current.TemporaryDirectory,
            item.Id.ToString("N"),
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(attemptDirectory);

        string? videoFile = null;
        string? audioFile = null;
        try
        {
            if (!selection.AudioOnly)
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.DownloadingVideo;
                    queueItem.StatusMessage = "Downloading video";
                }, true);
                videoFile = await downloader.DownloadFormatAsync(
                    item.SourceUrl,
                    selection.VideoFormatId!,
                    Path.Combine(attemptDirectory, "video"),
                    CreateDownloadProgress(update, DownloadStatus.DownloadingVideo, "Downloading video"),
                    cancellationToken).ConfigureAwait(false);
                update(queueItem => queueItem.TemporaryFiles.Add(videoFile), true);
            }

            if (selection.AudioOnly || selection.RequiresMerge)
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.DownloadingAudio;
                    queueItem.StatusMessage = "Downloading audio";
                    queueItem.ProgressPercent = 0;
                }, true);
                audioFile = await downloader.DownloadFormatAsync(
                    item.SourceUrl,
                    selection.AudioFormatId!,
                    Path.Combine(attemptDirectory, "audio"),
                    CreateDownloadProgress(update, DownloadStatus.DownloadingAudio, "Downloading audio"),
                    cancellationToken).ConfigureAwait(false);
                update(queueItem => queueItem.TemporaryFiles.Add(audioFile), true);
            }

            var sourceForName = videoFile ?? audioFile ?? throw new InvalidOperationException("No downloaded source file is available.");
            var container = FfmpegService.ChooseContainer(selection);
            var baseName = Path.GetFileNameWithoutExtension(sourceForName);
            var finalPath = FileSystemUtilities.ResolveFinalPath(
                settings.Current.FinalOutputDirectory,
                baseName,
                container,
                settings.Current.ConflictBehavior,
                settings.Current.OverwriteExistingFiles);

            string preparedFile;
            if (selection.RequiresMerge)
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.Merging;
                    queueItem.StatusMessage = "Merging video and audio";
                    queueItem.ProgressPercent = 0;
                    queueItem.SpeedBytesPerSecond = null;
                    queueItem.Eta = null;
                }, true);
                var partialFile = Path.Combine(attemptDirectory, $"final.partial.{container}");
                var mergeProgress = new Progress<double>(percent => update(queueItem => queueItem.ProgressPercent = percent, false));
                await ffmpeg.MergeAsync(videoFile!, audioFile!, partialFile, item.DurationSeconds, mergeProgress, cancellationToken).ConfigureAwait(false);
                preparedFile = partialFile;
            }
            else
            {
                preparedFile = sourceForName;
            }

            update(queueItem =>
            {
                queueItem.Status = DownloadStatus.Finalizing;
                queueItem.StatusMessage = "Finalizing output file";
                queueItem.ProgressPercent = 99;
            }, true);

            File.Move(preparedFile, finalPath, settings.Current.OverwriteExistingFiles || settings.Current.ConflictBehavior == Settings.FileConflictBehavior.Overwrite);
            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length == 0)
                throw new IOException("The final output file is missing or empty.");

            var completionWarnings = new List<string>();
            if (settings.Current.StoreMetadataJson && !string.IsNullOrWhiteSpace(item.RawMetadataJson))
            {
                try
                {
                    Directory.CreateDirectory(paths.MetadataDirectory);
                    var metadataPath = Path.Combine(paths.MetadataDirectory, $"{FileSystemUtilities.SafeIdentifier(item.VideoId)}.json");
                    await AtomicJsonFile.WriteRawJsonAsync(metadataPath, item.RawMetadataJson, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    completionWarnings.Add("internal metadata could not be saved");
                    logger.Error($"Could not save internal metadata for queue item {item.Id}.", ex);
                }
            }

            var postProcessing = await DownloadPostProcessor.ProcessAsync(finalPath, item, settings.Current, logger).ConfigureAwait(false);
            completionWarnings.AddRange(postProcessing.Warnings);

            var completedAt = DateTimeOffset.Now;
            update(queueItem =>
            {
                queueItem.Status = DownloadStatus.Completed;
                queueItem.StatusMessage = completionWarnings.Count == 0 ? "Completed" : $"Completed ({string.Join(", ", completionWarnings)})";
                queueItem.HasPostProcessingWarnings = completionWarnings.Count != 0;
                queueItem.ProgressPercent = 100;
                queueItem.FinalFile = finalPath;
                queueItem.CompletedAt = completedAt;
                queueItem.SpeedBytesPerSecond = null;
                queueItem.Eta = null;
                queueItem.ErrorMessage = null;
            }, true);

            try
            {
                await history.AddAsync(new DownloadHistoryEntry(
                    item.Id,
                    item.Title,
                    item.SourceUrl,
                    item.VideoId,
                    item.Channel,
                    QualityPreset.Find(item.QualityPresetId).DisplayName,
                    finalPath,
                    completedAt,
                    "Completed",
                    item.ThumbnailUrl,
                    item.QualityPresetId), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"The download completed but history could not be saved for queue item {item.Id}.", ex);
                update(queueItem =>
                {
                    queueItem.StatusMessage = "Completed (history could not be saved)";
                    queueItem.HasPostProcessingWarnings = true;
                }, true);
            }

            if (!settings.Current.KeepTemporaryFilesAfterSuccessfulMerge)
                TryDeleteAttemptDirectory(attemptDirectory);
        }
        catch (OperationCanceledException)
        {
            DeleteKnownPartialFiles(attemptDirectory);
            throw;
        }
    }

    private static IProgress<DownloadProgress> CreateDownloadProgress(
        Action<Action<DownloadQueueItem>, bool> update,
        DownloadStatus status,
        string message) => new Progress<DownloadProgress>(progress => update(queueItem =>
        {
            queueItem.Status = status;
            queueItem.StatusMessage = message;
            queueItem.ProgressPercent = progress.Percent;
            queueItem.DownloadedBytes = progress.DownloadedBytes;
            queueItem.TotalBytes = progress.TotalBytes;
            queueItem.SpeedBytesPerSecond = progress.SpeedBytesPerSecond;
            queueItem.Eta = progress.Eta;
        }, false));

    private static void DeleteKnownPartialFiles(string attemptDirectory)
    {
        if (!Directory.Exists(attemptDirectory))
            return;
        foreach (var file in Directory.EnumerateFiles(attemptDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static void TryDeleteAttemptDirectory(string attemptDirectory)
    {
        try { Directory.Delete(attemptDirectory, recursive: true); } catch { }
    }
}
