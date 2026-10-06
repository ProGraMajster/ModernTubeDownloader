using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Services;

/// <summary>Captures one LIVE segment. Every retry extracts fresh metadata and
/// starts a new segment; an ffmpeg LIVE capture cannot safely append to its old
/// muxed output, so verified segments are kept as separately playable parts.</summary>
public sealed class LiveRecordingExecutor(
    SettingsService settings,
    YtDlpProcessRunner runner,
    YtDlpMetadataService metadataService,
    FfmpegService ffmpeg,
    HistoryService history,
    IAppLogger logger) : ILiveRecordingExecutor
{
    private readonly YtDlpDownloadService downloader = new(runner, settings, logger);

    public async Task ExecuteAsync(LiveSession live,
        Action<Action<LiveSession>, bool> update, CancellationToken cancellationToken)
    {
        // Never reuse an expired manifest or direct media URL after a reconnect.
        var metadata = await metadataService.AnalyzeAsync(live.SourceUrl, cancellationToken).ConfigureAwait(false);
        var availability = MediaAvailabilityPolicy.Classify(metadata);
        if (availability != MediaAvailabilityKind.ActiveLive)
            throw new MediaAvailabilityException(availability);
        var selection = FormatSelector.SelectLive(metadata, QualityPreset.Find(live.QualityPresetId), live.Container);
        var effectivePolicy = LiveCapturePolicy.EffectiveStartPolicy(metadata, live.StartPolicy);
        if (effectivePolicy != live.StartPolicy)
        {
            logger.Warning($"LIVE from-start unsupported; using from-now LiveSessionId={live.SessionId} MediaId={live.MediaId}");
            update(current => current.RecoveryWarningKey = "Live.FromStartUnavailable", true);
        }

        var format = selection.AudioOnly ? selection.AudioFormatId! : selection.RequiresMerge
            ? $"{selection.VideoFormatId}+{selection.AudioFormatId}" : selection.VideoFormatId!;
        var sessionRoot = live.WorkspacePath ?? Path.Combine(settings.Current.TemporaryDirectory, "LiveSessions", live.Id.ToString("N"));
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(sessionRoot)) != live.Id.ToString("N"))
            throw new InvalidDataException("LIVE workspace identity does not match its session.");
        update(current => current.WorkspacePath = sessionRoot, true);
        // An interrupted ffmpeg capture cannot append safely to its old muxed
        // output. Recover the previous part first, or retain its raw data in a
        // separate directory when it is not yet playable.
        var interruptedPartIndex = live.PartIndex;
        var interruptedPartDirectory = Path.Combine(sessionRoot, $"part-{interruptedPartIndex:D4}");
        if (HasRawData(interruptedPartDirectory))
        {
            var recovered = await FinalizePartAsync(live, interruptedPartDirectory, selection)
                .ConfigureAwait(false);
            if (recovered is not null)
            {
                await RecordPartAsync(live, recovered, interruptedPartIndex, "Partial", update)
                    .ConfigureAwait(false);
                logger.Info($"LIVE interrupted part recovered LiveSessionId={live.SessionId} " +
                    $"PartIndex={interruptedPartIndex} ResumePossible=false ResumeReason=SeparateVerifiedPart");
            }
            else
            {
                update(current =>
                {
                    current.PartIndex = interruptedPartIndex + 1;
                    current.RecoveryWarningKey = "Live.RawDataPreserved";
                }, true);
                logger.Warning($"LIVE interrupted raw part preserved LiveSessionId={live.SessionId} " +
                    $"PartIndex={interruptedPartIndex} ResumePossible=false ResumeReason=UnverifiedRawPart");
            }
        }
        var partIndex = live.PartIndex;
        var partDirectory = Path.Combine(sessionRoot, $"part-{partIndex:D4}");
        Directory.CreateDirectory(partDirectory);
        logger.Info($"LIVE workspace LiveSessionId={live.SessionId} ResumePossible=false " +
            $"ResumeReason=NewFfmpegCapturePart PartIndex={partIndex} ExistingParts={live.Parts.Count}");
        FileSystemUtilities.EnsureWritableDirectory(settings.Current.FinalOutputDirectory);
        var partStartedAt = DateTimeOffset.UtcNow;
        var previousDuration = live.RecordedDuration;
        update(current =>
        {
            current.State = LiveSessionState.Recording;
            current.StatusMessageKey = "Queue.Detail.RecordingLive";
            current.MediaId = metadata.Id;
            current.Title = metadata.Title;
            current.Channel = metadata.DisplayChannel;
            current.ThumbnailUrl = metadata.ThumbnailUrl;
            current.SourceLiveStatus = metadata.LiveStatus;
            current.RecordingStartedAt ??= partStartedAt;
            current.NextCheckAt = null;
        }, true);
        logger.Info($"LIVE capture started LiveSessionId={live.SessionId} LiveState=RecordingLive StartPolicy={effectivePolicy} " +
            $"ResumeAttempt={live.ResumeCount} PartIndex={partIndex} MediaId={live.MediaId} QualityPreset={live.QualityPresetId} CookieFileEnabled={settings.Current.UseCookieFile}");

        var progress = new Progress<DownloadProgress>(value => update(current =>
        {
            if (current.State != LiveSessionState.Recording) return;
            current.StatusMessageKey = "Queue.Detail.RecordingLive";
            current.BytesWritten = value.DownloadedBytes ?? current.BytesWritten;
            current.CurrentSpeed = value.SpeedBytesPerSecond;
            current.RecordedDuration = previousDuration + (DateTimeOffset.UtcNow - partStartedAt);
        }, false));
        using var sampling = new CancellationTokenSource();
        var sampleTask = SampleCaptureAsync(live, partDirectory, previousDuration, partStartedAt, update, sampling.Token);
        try
        {
            var output = await downloader.DownloadFormatAsync(live.SourceUrl, format, partDirectory, progress,
                cancellationToken, mergeOutputFormat: "mkv", liveStartPolicy: effectivePolicy,
                outputTemplate: "capture.%(ext)s").ConfigureAwait(false);
            live.RecordedDuration = previousDuration + (DateTimeOffset.UtcNow - partStartedAt);
            update(current => { current.State = LiveSessionState.Finalizing; current.StatusMessageKey = "Queue.Detail.Finalizing"; }, true);
            var finalized = await FinalizePartAsync(live, partDirectory, selection, output).ConfigureAwait(false);
            if (finalized is null)
                throw new InvalidDataException("LIVE ended without a verified playable recording; raw data remains in the session workspace.");
            await RecordPartAsync(live, finalized, partIndex, live.CancelRequested ? "Partial" : "Completed", update).ConfigureAwait(false);
            update(current =>
            {
                current.State = live.CancelRequested ? LiveSessionState.Partial : LiveSessionState.Completed;
                current.StatusMessageKey = "Queue.Detail.Completed";
                current.RecordingEndedAt = DateTimeOffset.Now;
                current.CurrentSpeed = null;
            }, true);
        }
        catch (OperationCanceledException) when (live.StopRequested || live.CancelRequested && live.PreservePartialOnCancel)
        {
            live.RecordedDuration = previousDuration + (DateTimeOffset.UtcNow - partStartedAt);
            update(current => { current.State = LiveSessionState.Finalizing; current.StatusMessageKey = "Queue.Detail.Finalizing"; }, true);
            var finalized = await FinalizePartAsync(live, partDirectory, selection).ConfigureAwait(false);
            if (finalized is not null)
            {
                await RecordPartAsync(live, finalized, partIndex, live.CancelRequested ? "Partial" : "Completed", update).ConfigureAwait(false);
                update(current =>
                {
                    current.State = live.CancelRequested ? LiveSessionState.Partial : LiveSessionState.Completed;
                    current.StatusMessageKey = "Live.StoppedAndSaved";
                    current.RecordingEndedAt = DateTimeOffset.Now;
                    current.CurrentSpeed = null;
                }, true);
            }
            else
            {
                update(current =>
                {
                    current.State = HasRawData(partDirectory) ? LiveSessionState.Partial : LiveSessionState.Failed;
                    current.StatusMessageKey = HasRawData(partDirectory) ? "Queue.Detail.Partial" : "Live.NoDataToSave";
                    current.RecordingEndedAt = DateTimeOffset.Now;
                    current.CurrentSpeed = null;
                    current.RecoveryWarningKey = "Live.RawDataPreserved";
                }, true);
            }
        }
        catch (OperationCanceledException)
        {
            // A shutdown is not a user stop. Keep the raw workspace for the next
            // launch; LiveRecordingScheduler marks the session Interrupted.
            throw;
        }
        catch
        {
            live.RecordedDuration = previousDuration + (DateTimeOffset.UtcNow - partStartedAt);
            update(current => { current.State = LiveSessionState.Finalizing; current.StatusMessageKey = "Queue.Detail.Finalizing"; }, true);
            var finalized = await FinalizePartAsync(live, partDirectory, selection).ConfigureAwait(false);
            if (finalized is not null)
                await RecordPartAsync(live, finalized, partIndex, "Partial", update).ConfigureAwait(false);
            if (finalized is not null || HasRawData(partDirectory))
            {
                update(current =>
                {
                    current.PartIndex = partIndex + 1;
                    current.RecoveryWarningKey = finalized is null ? "Live.RawDataPreserved" : null;
                }, true);
            }
            if (!live.PreservePartial &&
                live.AttemptCount >= live.MaximumAttempts)
            {
                RemoveRawWorkspace(live, logger);
                update(current => current.RecoveryWarningKey = null, true);
            }
            throw;
        }
        finally
        {
            sampling.Cancel();
            await sampleTask.ConfigureAwait(false);
        }
    }

    private static async Task SampleCaptureAsync(LiveSession live, string directory, TimeSpan previousDuration,
        DateTimeOffset started, Action<Action<LiveSession>, bool> update, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var last = 0L;
        var verifiedBytes = live.Parts.Where(File.Exists).Sum(path => new FileInfo(path).Length);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastSample = TimeSpan.Zero;
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                long bytes;
                try { bytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(IsMediaCandidate).Sum(path => new FileInfo(path).Length); }
                catch (IOException) { continue; }
                var elapsed = clock.Elapsed;
                var speed = Math.Max(0, bytes - last) / Math.Max(.01, (elapsed - lastSample).TotalSeconds);
                last = bytes; lastSample = elapsed;
                update(current =>
                {
                    if (current.State != LiveSessionState.Recording) return;
                    current.BytesWritten = verifiedBytes + bytes;
                    current.CurrentSpeed = speed;
                    current.RecordedDuration = previousDuration + (DateTimeOffset.UtcNow - started);
                }, false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task<string?> FinalizePartAsync(LiveSession live,
        string partDirectory, FormatSelection selection, string? preferredFile = null)
    {
        var candidates = Directory.EnumerateFiles(partDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                           !Path.GetFileName(path).StartsWith("recovered.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => path == preferredFile)
            .ThenByDescending(path => new FileInfo(path).Length)
            .ToArray();
        foreach (var candidate in candidates)
        {
            if (new FileInfo(candidate).Length == 0) continue;
            var probe = await ffmpeg.TryProbeMediaAsync(candidate).ConfigureAwait(false);
            if (probe is null || !probe.HasAudio || !selection.AudioOnly && !probe.HasVideo)
                continue;
            var recovered = Path.Combine(partDirectory, "recovered.mkv");
            try
            {
                await ffmpeg.RemuxAsync(candidate, recovered, probe.DurationSeconds, null, CancellationToken.None)
                    .ConfigureAwait(false);
                if (await CommitVerifiedPartAsync(live, recovered, selection).ConfigureAwait(false) is { } saved)
                    return saved;
            }
            catch (Exception ex)
            {
                logger.Warning($"LIVE part could not be remuxed LiveSessionId={live.SessionId} PartIndex={live.PartIndex} Error={ex.GetType().Name}");
            }
        }
        if (selection.RequiresMerge)
        {
            // Only pair known format IDs inside this exact part; never combine attempts/sessions.
            var video = candidates.FirstOrDefault(path => Path.GetFileName(path).Contains($".f{selection.VideoFormatId}.", StringComparison.Ordinal));
            var audio = candidates.FirstOrDefault(path => Path.GetFileName(path).Contains($".f{selection.AudioFormatId}.", StringComparison.Ordinal));
            if (video is not null && audio is not null &&
                TryCommonDuration(await ffmpeg.TryProbeMediaAsync(video).ConfigureAwait(false),
                    await ffmpeg.TryProbeMediaAsync(audio).ConfigureAwait(false), out var commonDuration))
            {
                try
                {
                    var recovered = Path.Combine(partDirectory, "recovered.mkv");
                    await ffmpeg.MergeAsync(video, audio, recovered, commonDuration, null, CancellationToken.None,
                        maximumDurationSeconds: commonDuration).ConfigureAwait(false);
                    return await CommitVerifiedPartAsync(live, recovered, selection, commonDuration).ConfigureAwait(false);
                }
                catch (Exception ex)
                { logger.Warning($"LIVE split-stream recovery failed LiveSessionId={live.Id} Error={ex.GetType().Name}"); }
            }
        }
        return null;
    }

    internal static bool TryCommonDuration(MediaProbeResult? video, MediaProbeResult? audio, out double duration)
    {
        duration = 0;
        if (video is not { HasVideo: true, HasAudio: false, DurationSeconds: > 0, StartTimeSeconds: not null } ||
            audio is not { HasAudio: true, HasVideo: false, DurationSeconds: > 0, StartTimeSeconds: not null } ||
            !double.IsFinite(video.DurationSeconds.Value) || !double.IsFinite(audio.DurationSeconds.Value) ||
            !double.IsFinite(video.StartTimeSeconds.Value) || !double.IsFinite(audio.StartTimeSeconds.Value) ||
            Math.Abs(video.StartTimeSeconds.Value - audio.StartTimeSeconds.Value) > .1) return false;
        duration = Math.Min(video.DurationSeconds.Value, audio.DurationSeconds.Value);
        return double.IsFinite(duration);
    }

    private async Task<string?> CommitVerifiedPartAsync(LiveSession live, string recovered, FormatSelection selection,
        double? maximumDuration = null)
    {
        var verified = await ffmpeg.TryProbeMediaAsync(recovered).ConfigureAwait(false);
        if (verified is null || verified.DurationSeconds is not > 0 || !verified.HasAudio ||
            !selection.AudioOnly && !verified.HasVideo ||
            maximumDuration is { } cap && verified.DurationSeconds > cap + .1) return null;
        var finalPath = FileSystemUtilities.ResolveFinalPath(settings.Current.FinalOutputDirectory,
            $"{SafeTitle(live.Title)} [{FileSystemUtilities.SafeIdentifier(live.MediaId)}] [Part {live.PartIndex}]",
            "mkv", Settings.FileConflictBehavior.Rename, overwrite: false);
        File.Move(recovered, finalPath);
        logger.Info($"LIVE part verified LiveSessionId={live.Id} PartIndex={live.PartIndex} " +
            $"RecoveredBytes={new FileInfo(finalPath).Length} RecordedDuration={verified.DurationSeconds:F1}");
        return finalPath;
    }

    private async Task RecordPartAsync(LiveSession live, string finalPath,
        int partIndex, string historyStatus, Action<Action<LiveSession>, bool> update)
    {
        var verifiedDuration = TimeSpan.Zero;
        foreach (var part in live.Parts.Append(finalPath).Distinct())
            if (await ffmpeg.TryProbeDurationAsync(part).ConfigureAwait(false) is > 0 and var seconds)
                verifiedDuration += TimeSpan.FromSeconds(seconds);
        update(current =>
        {
            if (!current.Parts.Contains(finalPath)) current.Parts.Add(finalPath);
            current.PartIndex = Math.Max(current.PartIndex, partIndex + 1);
            current.BytesWritten = current.Parts.Where(File.Exists).Sum(path => new FileInfo(path).Length);
            if (verifiedDuration > TimeSpan.Zero) current.RecordedDuration = verifiedDuration;
        }, true);
        try
        {
            await history.AddAsync(new DownloadHistoryEntry(live.Id, live.Title, live.SourceUrl,
                live.MediaId, live.Channel, QualityPreset.Find(live.QualityPresetId).DisplayName,
                finalPath, DateTimeOffset.Now, historyStatus, live.ThumbnailUrl, live.QualityPresetId,
                LivePartIndex: partIndex, WasLiveRecording: true,
                RecordingStartedAt: live.RecordingStartedAt, RecordingEndedAt: DateTimeOffset.UtcNow,
                RecordedDuration: live.RecordedDuration, WasPartial: historyStatus == "Partial", PartsCount: live.Parts.Count),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"LIVE part was saved but history update failed LiveSessionId={live.SessionId} PartIndex={partIndex}.", ex);
        }
    }

    internal static bool HasRawData(string? directory) => directory is not null && Directory.Exists(directory) &&
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Any(path => IsMediaCandidate(path) && new FileInfo(path).Length > 0);

    internal static void RemoveRawWorkspace(LiveSession session, IAppLogger logger)
    {
        var root = session.WorkspacePath;
        if (root is null || Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) != session.Id.ToString("N")) return;
        try
        {
            if (Directory.Exists(root) && !File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) { logger.Warning($"LIVE raw cleanup failed LiveSessionId={session.Id} Error={ex.GetType().Name}"); }
    }

    internal static bool IsMediaCandidate(string path) =>
        !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
        !Path.GetFileName(path).StartsWith(".synthetic-", StringComparison.OrdinalIgnoreCase);

    private static string SafeTitle(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((title ?? string.Empty).Where(character => !invalid.Contains(character)).ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "LIVE" : safe[..Math.Min(safe.Length, 100)];
    }
}
