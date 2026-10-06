using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class LiveSessionPersistenceService(AppPaths paths, IAppLogger logger)
{
    public async Task<IReadOnlyList<LiveSession>> LoadAsync(CancellationToken token = default)
    {
        JsonElement[] records;
        try { records = await AtomicJsonFile.ReadAsync<JsonElement[]>(paths.LiveSessionsFile, token) ?? []; }
        catch (JsonException ex)
        {
            logger.Error("LIVE persistence is invalid; retaining a recovery copy.", ex);
            File.Copy(paths.LiveSessionsFile, paths.LiveSessionsFile + ".corrupt-" + Guid.NewGuid().ToString("N"));
            return [];
        }
        var sessions = new List<LiveSession>();
        var ids = new HashSet<Guid>();
        foreach (var record in records)
        {
            try
            {
                var session = record.Deserialize<LiveSession>(JsonDefaults.Options)
                    ?? throw new JsonException("Empty LIVE record.");
                if (session.Id == Guid.Empty || !Enum.IsDefined(session.State) ||
                    !Enum.IsDefined(session.StartPolicy) ||
                    !YtDlpMetadataService.TryValidateSourceUrl(session.SourceUrl, out var url, out _))
                    throw new JsonException("Invalid LIVE identity, state or source.");
                if (!ids.Add(session.Id)) continue;
                session.SourceUrl = url;
                session.Parts ??= [];
                session.PartIndex = Math.Max(1, session.PartIndex);
                session.QualityPresetId = QualityPreset.Find(session.QualityPresetId).Id;
                sessions.Add(session);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
            { logger.Warning($"Ignored invalid LIVE persistence record Error={ex.GetType().Name}"); }
        }
        return sessions;
    }

    public Task SaveAsync(IReadOnlyList<LiveSession> sessions, CancellationToken token = default) =>
        AtomicJsonFile.WriteAsync(paths.LiveSessionsFile, sessions, token);

    // Save the destination before removing source entries. Retrying after any
    // interruption is idempotent by the durable original SessionId.
    public async Task MigrateLegacyQueueAsync(SettingsService settings, CancellationToken token = default)
    {
        var queue = await new QueuePersistenceService(paths, logger).LoadAsync(token).ConfigureAwait(false);
        if (!queue.Any(item => item.LiveSession is not null)) return;
        var sessions = (await LoadAsync(token)).ToList();
        foreach (var item in queue.Where(item => item.LiveSession is not null))
        {
            var live = item.LiveSession!;
            if (sessions.Any(session => session.Id == live.Id)) continue;
            live.SourceUrl = item.SourceUrl;
            live.MediaId = item.VideoId;
            live.Title = item.Title;
            live.Channel = item.Channel;
            live.ThumbnailUrl = item.ThumbnailUrl;
            live.QualityPresetId = item.QualityPresetId;
            live.Container = PreferredVideoContainer.Mkv;
            live.CreatedAt = item.AddedAt;
            live.BytesWritten = item.DownloadedBytes ?? 0;
            live.WorkspacePath = Path.Combine(settings.Current.TemporaryDirectory, "sessions", live.Id.ToString("N"));
            live.State = item.Status switch
            {
                DownloadStatus.Queued => LiveSessionState.Pending,
                DownloadStatus.WaitingForLive => LiveSessionState.WaitingForLive,
                DownloadStatus.Completed => LiveSessionState.Completed,
                DownloadStatus.Partial => LiveSessionState.Partial,
                DownloadStatus.Failed => LiveSessionState.Failed,
                DownloadStatus.Cancelled => LiveSessionState.Cancelled,
                _ => LiveSessionState.Interrupted
            };
            live.FailureCategory = item.FailureCategory;
            live.FailureMessageKey = item.FailureMessageKey;
            sessions.Add(live);
        }
        await SaveAsync(sessions, token).ConfigureAwait(false);
        await AtomicJsonFile.WriteAsync(paths.QueueFile,
            queue.Where(item => item.LiveSession is null).ToArray(), token).ConfigureAwait(false);
        logger.Info($"Migrated legacy LIVE sessions out of VOD queue Count={queue.Count(item => item.LiveSession is not null)}");
    }
}
