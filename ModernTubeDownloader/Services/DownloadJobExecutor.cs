using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Services;

public interface IDownloadJobExecutor
{
    Task ExecuteAsync(
        DownloadQueueItem item,
        Action<Action<DownloadQueueItem>, bool> update,
        CancellationToken cancellationToken);
}

public sealed class DownloadJobExecutor(
    AppPaths paths,
    SettingsService settings,
    YtDlpProcessRunner ytDlpRunner,
    YtDlpMetadataService metadataService,
    FfmpegService ffmpeg,
    HistoryService history,
    IAppLogger logger) : IDownloadJobExecutor
{
    private readonly YtDlpDownloadService downloader = new(ytDlpRunner, settings, logger);
    private readonly SubtitleDownloadService subtitles = new(ytDlpRunner, settings, logger);

    public async Task ExecuteAsync(
        DownloadQueueItem item,
        Action<Action<DownloadQueueItem>, bool> update,
        CancellationToken cancellationToken)
    {
        if (item.LiveSession is not null)
            throw new InvalidOperationException("LIVE sessions must use the independent LIVE subsystem.");
        var requestedRange = item.RequestedRange ?? MediaTimeRange.Full;
        if (requestedRange.Validate(item.DurationSeconds) is { } initialRangeError)
            throw new ArgumentException(initialRangeError, nameof(item));
        item.SubtitleOptions ??= new();
        item.SponsorBlockOptions ??= new();
        item.SubtitleOptions.Validate();
        item.SponsorBlockOptions.ToYtDlpCategories();
        DownloadOptionCompatibilityValidator.EnsureSupported(requestedRange, item.SubtitleOptions,
            item.SponsorBlockOptions, item.PreferredVideoContainer);
        // A transient stream URL may have expired. Resolve formats again on every retry.
        var selection = item.AttemptCount > 1 ? null : item.SelectedFormats;
        if (selection is null)
        {
            var metadata = await metadataService.AnalyzeAsync(item.SourceUrl, cancellationToken).ConfigureAwait(false);
            if (requestedRange.Validate(metadata.DurationSeconds, metadata.IsLive == true ||
                    string.Equals(metadata.LiveStatus, "is_live", StringComparison.OrdinalIgnoreCase)) is { } rangeError)
                throw new ArgumentException(rangeError, nameof(item));
            selection = FormatSelector.Select(metadata, QualityPreset.Find(item.QualityPresetId), item.PreferredVideoContainer);
            update(queueItem =>
            {
                queueItem.SelectedFormats = selection;
                queueItem.VideoId = metadata.Id;
                queueItem.Title = metadata.Title;
                queueItem.Channel = metadata.DisplayChannel;
                queueItem.DurationSeconds = metadata.DurationSeconds;
                queueItem.ThumbnailUrl = metadata.ThumbnailUrl;
                queueItem.RawMetadataJson = metadata.RawJson;
            }, true);
        }
        FileSystemUtilities.EnsureWritableDirectory(settings.Current.TemporaryDirectory);
        FileSystemUtilities.EnsureWritableDirectory(settings.Current.FinalOutputDirectory);

        // A format-specific directory is stable across retries and restarts. yt-dlp's
        // default .part/.ytdl continuation can only be reused with the same stream
        // selection; a changed format gets a different workspace without deleting
        // the old partial data.
        var formatKey = $"{selection.VideoFormatId}|{selection.AudioFormatId}|{selection.AudioOnly}";
        var formatHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(formatKey)))[..12];
        var sessionRoot = Path.Combine(settings.Current.TemporaryDirectory, "sessions", item.JobSessionId.ToString("N"));
        var attemptDirectory = settings.Current.ResumeInterruptedDownloads
            ? Path.Combine(sessionRoot, $"formats-{formatHash}")
            : Path.Combine(sessionRoot, $"attempt-{item.AttemptCount:D2}-{Guid.NewGuid():N}");
        var resumablePart = settings.Current.ResumeInterruptedDownloads && Directory.Exists(attemptDirectory) &&
            Directory.EnumerateFiles(attemptDirectory, "*.part", SearchOption.AllDirectories).Any();
        logger.Info($"VOD workspace QueueItemId={item.Id} JobSessionId={item.JobSessionId} " +
            $"ResumePossible={resumablePart} ResumeReason={(resumablePart ? "MatchingFormatPartial" :
                settings.Current.ResumeInterruptedDownloads ? "NoMatchingFormatPartial" : "DisabledBySetting")} " +
            $"FormatKey={formatHash} Attempt={item.AttemptCount}");
        Directory.CreateDirectory(attemptDirectory);
        var container = FfmpegService.ChooseContainer(selection, item.PreferredVideoContainer);
        var combinedDownload = item.SponsorBlockOptions.Mode != SponsorBlockMode.Off;
        logger.Info($"Download selection QueueItemId={item.Id} MediaId={item.VideoId} Attempt={item.AttemptCount} QualityPreset={item.QualityPresetId} Container={item.PreferredVideoContainer} VideoFormatId={selection.VideoFormatId ?? "none"} AudioFormatId={selection.AudioFormatId ?? "none"} Range={requestedRange.DisplayText} SubtitleLanguages={string.Join(',', item.SubtitleOptions.Languages)} SubtitleSource={item.SubtitleOptions.Source} SubtitleEmbed={item.SubtitleOptions.Embed} SponsorBlockMode={item.SponsorBlockOptions.Mode} SponsorBlockCategories={string.Join(',', item.SponsorBlockOptions.Categories)} CookieFileEnabled={settings.Current.UseCookieFile}");

        string? videoFile = null;
        string? audioFile = null;
        var completionWarnings = new List<string>();
        try
        {
            if (combinedDownload)
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.DownloadingVideo;
                    queueItem.StatusMessageKey = "Queue.Detail.DownloadingVideo";
                }, true);
                var format = selection.AudioOnly ? selection.AudioFormatId! : selection.RequiresMerge
                    ? $"{selection.VideoFormatId}+{selection.AudioFormatId}" : selection.VideoFormatId!;
                videoFile = await downloader.DownloadFormatAsync(item.SourceUrl, format,
                    Path.Combine(attemptDirectory, "combined"),
                    CreateDownloadProgress(update, DownloadStatus.DownloadingVideo, "Downloading video"),
                    cancellationToken, requestedRange, item.SponsorBlockOptions,
                    selection.RequiresMerge ? container : null).ConfigureAwait(false);
                update(queueItem => queueItem.TemporaryFiles.Add(videoFile), true);
            }
            else if (!selection.AudioOnly)
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.DownloadingVideo;
                    queueItem.StatusMessage = "Downloading video";
                    queueItem.StatusMessageKey = "Queue.Detail.DownloadingVideo";
                }, true);
                videoFile = await downloader.DownloadFormatAsync(
                    item.SourceUrl,
                    selection.VideoFormatId!,
                    Path.Combine(attemptDirectory, "video"),
                    CreateDownloadProgress(update, DownloadStatus.DownloadingVideo, "Downloading video"),
                    cancellationToken, requestedRange).ConfigureAwait(false);
                update(queueItem => queueItem.TemporaryFiles.Add(videoFile), true);
            }

            if (!combinedDownload && (selection.AudioOnly || selection.RequiresMerge))
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.DownloadingAudio;
                    queueItem.StatusMessage = "Downloading audio";
                    queueItem.StatusMessageKey = "Queue.Detail.DownloadingAudio";
                    queueItem.ProgressPercent = 0;
                }, true);
                audioFile = await downloader.DownloadFormatAsync(
                    item.SourceUrl,
                    selection.AudioFormatId!,
                    Path.Combine(attemptDirectory, "audio"),
                    CreateDownloadProgress(update, DownloadStatus.DownloadingAudio, "Downloading audio"),
                    cancellationToken, requestedRange).ConfigureAwait(false);
                update(queueItem => queueItem.TemporaryFiles.Add(audioFile), true);
            }

            var sourceForName = videoFile ?? audioFile ?? throw new InvalidOperationException("No downloaded source file is available.");
            if (selection.RequiresStreamProbe)
            {
                var probe = await ffmpeg.TryProbeMediaAsync(sourceForName, cancellationToken).ConfigureAwait(false);
                if (probe is null || !probe.HasVideo || !probe.HasAudio || probe.DurationSeconds is not > 0 ||
                    string.IsNullOrWhiteSpace(probe.FormatName))
                    throw new InvalidOperationException("No downloadable audio/video format could be verified in the downloaded direct media file.");
                logger.Info($"Direct media validated QueueItemId={item.Id} HasVideo={probe.HasVideo} HasAudio={probe.HasAudio} DurationSeconds={probe.DurationSeconds.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            var baseName = Path.GetFileNameWithoutExtension(sourceForName);

            string preparedFile;
            if (!combinedDownload && selection.RequiresMerge || FfmpegService.RequiresRemux(selection, item.PreferredVideoContainer))
            {
                update(queueItem =>
                {
                    queueItem.Status = DownloadStatus.Merging;
                    queueItem.StatusMessage = "Merging video and audio";
                    queueItem.StatusMessageKey = "Queue.Detail.Merging";
                    queueItem.ProgressPercent = 0;
                    queueItem.SpeedBytesPerSecond = null;
                    queueItem.Eta = null;
                }, true);
                var partialFile = Path.Combine(attemptDirectory, $"final.partial.{container}");
                var mergeProgress = new Progress<double>(percent => update(queueItem => queueItem.ProgressPercent = percent, false));
                if (selection.RequiresMerge)
                    await ffmpeg.MergeAsync(videoFile!, audioFile!, partialFile, RequestedDuration(item.DurationSeconds, requestedRange), mergeProgress, cancellationToken).ConfigureAwait(false);
                else
                    await ffmpeg.RemuxAsync(sourceForName, partialFile, RequestedDuration(item.DurationSeconds, requestedRange), mergeProgress, cancellationToken).ConfigureAwait(false);
                preparedFile = partialFile;
            }
            else
            {
                preparedFile = sourceForName;
            }

            if (selection.RequiresStreamProbe && preparedFile != sourceForName)
            {
                var preparedProbe = await ffmpeg.TryProbeMediaAsync(preparedFile, cancellationToken).ConfigureAwait(false);
                if (preparedProbe is null || !preparedProbe.HasVideo || !preparedProbe.HasAudio ||
                    preparedProbe.DurationSeconds is not > 0 || string.IsNullOrWhiteSpace(preparedProbe.FormatName))
                    throw new InvalidOperationException("No downloadable audio/video format could be verified after direct media remux.");
            }

            IReadOnlyList<string> subtitleFiles = [];
            var subtitlesEmbedded = false;
            if (item.SubtitleOptions.Enabled)
            {
                try
                {
                    var subtitleMetadata = await metadataService.AnalyzeAsync(item.SourceUrl, cancellationToken).ConfigureAwait(false);
                    subtitleFiles = await subtitles.DownloadAsync(subtitleMetadata, item.SubtitleOptions,
                        Path.Combine(attemptDirectory, "subtitles"), container, cancellationToken).ConfigureAwait(false);
                    if (subtitleFiles.Count == 0)
                        completionWarnings.Add("requested subtitles were unavailable");
                    else if (item.SubtitleOptions.Embed && !selection.AudioOnly)
                    {
                        var embeddedFile = Path.Combine(attemptDirectory, $"with-subtitles.partial.{container}");
                        await ffmpeg.EmbedSubtitlesAsync(preparedFile, subtitleFiles, embeddedFile, cancellationToken).ConfigureAwait(false);
                        preparedFile = embeddedFile;
                        subtitlesEmbedded = true;
                    }
                    else if (item.SubtitleOptions.Embed)
                        completionWarnings.Add("subtitles cannot be embedded in audio-only output");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    completionWarnings.Add("subtitles could not be processed");
                    logger.Error($"Subtitle processing failed QueueItemId={item.Id}.", ex);
                }
            }

            update(queueItem =>
            {
                queueItem.Status = DownloadStatus.Finalizing;
                queueItem.StatusMessage = "Finalizing output file";
                queueItem.StatusMessageKey = "Queue.Detail.Finalizing";
                queueItem.ProgressPercent = 99;
            }, true);

            var keepSubtitleSidecars = subtitleFiles.Count > 0 &&
                (item.SubtitleOptions.KeepFiles || !subtitlesEmbedded);
            var sidecarSuffixes = keepSubtitleSidecars
                ? subtitleFiles.Select(file => Path.GetFileName(file)["subtitle.".Length..]).ToArray()
                : [];
            var finalPath = FileSystemUtilities.ResolveFinalPath(
                settings.Current.FinalOutputDirectory,
                baseName,
                container,
                settings.Current.ConflictBehavior,
                settings.Current.OverwriteExistingFiles,
                sidecarSuffixes);
            File.Move(preparedFile, finalPath, settings.Current.OverwriteExistingFiles || settings.Current.ConflictBehavior == Settings.FileConflictBehavior.Overwrite);
            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length == 0)
                throw new IOException("The final output file is missing or empty.");
            if (keepSubtitleSidecars)
            {
                foreach (var subtitleFile in subtitleFiles)
                {
                    try
                    {
                        var suffix = Path.GetFileName(subtitleFile)["subtitle.".Length..];
                        var sidecarPath = Path.Combine(Path.GetDirectoryName(finalPath)!, $"{Path.GetFileNameWithoutExtension(finalPath)}.{suffix}");
                        File.Copy(subtitleFile, sidecarPath,
                            overwrite: settings.Current.OverwriteExistingFiles ||
                                       settings.Current.ConflictBehavior == Settings.FileConflictBehavior.Overwrite);
                    }
                    catch (Exception ex)
                    {
                        completionWarnings.Add("a subtitle sidecar could not be saved");
                        logger.Error($"Subtitle sidecar could not be saved QueueItemId={item.Id}.", ex);
                    }
                }
            }
            if (requestedRange.Mode == MediaRangeMode.Custom)
            {
                var finalDuration = await ffmpeg.TryProbeDurationAsync(finalPath).ConfigureAwait(false);
                logger.Info($"Segment finalized QueueItemId={item.Id} RequestedRange={requestedRange.DisplayText} FinalDurationSeconds=" +
                    (finalDuration?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown"));
            }

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
                    item.QualityPresetId,
                    item.PlaylistId,
                    item.PlaylistTitle,
                    item.PlaylistIndex,
                    requestedRange), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"The download completed but history could not be saved for queue item {item.Id}.", ex);
                completionWarnings.Add("history could not be saved");
            }

            // VOD and LIVE share History: terminal observers must see the saved output record.
            update(queueItem =>
            {
                queueItem.Status = DownloadStatus.Completed;
                queueItem.StatusMessage = completionWarnings.Count == 0 ? "Completed" : $"Completed ({string.Join(", ", completionWarnings)})";
                queueItem.StatusMessageKey = completionWarnings.Count == 0 ? "Queue.Detail.Completed" : "Queue.Detail.CompletedWarnings";
                queueItem.HasPostProcessingWarnings = completionWarnings.Count != 0;
                queueItem.ProgressPercent = 100;
                queueItem.FinalFile = finalPath;
                queueItem.CompletedAt = completedAt;
                queueItem.SpeedBytesPerSecond = null;
                queueItem.Eta = null;
                queueItem.ErrorMessage = null;
            }, true);

            if (!settings.Current.KeepTemporaryFilesAfterSuccessfulMerge)
                TryDeleteAttemptDirectory(attemptDirectory);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (!settings.Current.PreservePartialDownloadsOnFailure &&
                item.AttemptCount >= item.MaximumAttempts)
                TryDeleteAttemptDirectory(attemptDirectory);
            throw;
        }
    }

    private static double? RequestedDuration(double? mediaDuration, MediaTimeRange range)
    {
        if (range.Mode == MediaRangeMode.Full) return mediaDuration;
        var end = range.End?.TotalSeconds ?? mediaDuration;
        return end is null ? null : Math.Max(0, end.Value - (range.Start?.TotalSeconds ?? 0));
    }

    private static IProgress<DownloadProgress> CreateDownloadProgress(
        Action<Action<DownloadQueueItem>, bool> update,
        DownloadStatus status,
        string message) => new Progress<DownloadProgress>(progress => update(queueItem =>
        {
            queueItem.Status = status;
            queueItem.StatusMessage = message;
            queueItem.StatusMessageKey = status switch
            {
                DownloadStatus.DownloadingVideo => "Queue.Detail.DownloadingVideo",
                DownloadStatus.DownloadingAudio => "Queue.Detail.DownloadingAudio",
                _ => queueItem.StatusMessageKey
            };
            queueItem.ProgressPercent = progress.Percent;
            queueItem.DownloadedBytes = progress.DownloadedBytes;
            queueItem.TotalBytes = progress.TotalBytes;
            queueItem.SpeedBytesPerSecond = progress.SpeedBytesPerSecond;
            queueItem.Eta = progress.Eta;
        }, false));

    private static void TryDeleteAttemptDirectory(string attemptDirectory)
    {
        try { Directory.Delete(attemptDirectory, recursive: true); } catch { }
    }
}
