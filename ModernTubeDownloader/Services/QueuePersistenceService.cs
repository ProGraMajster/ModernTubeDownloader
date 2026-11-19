using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public interface IQueuePersistence
{
    bool LoadFailed => false;
    Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken = default);
}

public sealed class QueuePersistenceService(AppPaths paths, IAppLogger logger) : IQueuePersistence
{
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool LoadFailed { get; private set; }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(LiveSession))
            {
                // Legacy LIVE rows derive a missing identity from their durable
                // workspace. Generating one here would defeat migration retries.
                info.OnDeserializing = instance => ((LiveSession)instance).SessionId = Guid.Empty;
                return;
            }
            if (info.Type != typeof(DownloadQueueItem)) return;
            // New jobs generate identities in their constructor, but loading a saved
            // row must never invent one. Keep this requirement local to persistence.
            info.Properties.Single(property => property.Name.Equals("id", StringComparison.OrdinalIgnoreCase)).IsRequired = true;
            // The initial queue format predates JobSessionId. Its stable queue Id
            // provides the legacy workspace identity, rather than a fresh random id.
            info.OnDeserializing = instance => ((DownloadQueueItem)instance).JobSessionId = Guid.Empty;
        });
        return new(JsonDefaults.Options) { TypeInfoResolver = resolver, AllowDuplicateProperties = false };
    }

    public async Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // File.Exists also returns false for access failures. Open the file so
            // an unreadable profile can never be mistaken for a new empty queue.
            await using var stream = new FileStream(paths.QueueFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var loaded = await JsonSerializer.DeserializeAsync<List<DownloadQueueItem?>>(
                stream, ReadOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("The saved queue is null, not a queue collection.");
            var identities = new HashSet<Guid>(loaded.Count);
            var workspaces = new HashSet<Guid>(loaded.Count);
            var result = new DownloadQueueItem[loaded.Count];
            for (var index = 0; index < loaded.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = loaded[index] ?? throw new JsonException("The saved queue contains a null row.");
                if (item.Id == Guid.Empty || !identities.Add(item.Id))
                    throw new JsonException("The saved queue contains an empty or duplicate task identity.");
                if (!Enum.IsDefined(item.Status))
                    throw new JsonException("The saved queue contains an unknown task status.");
                var workspace = item.JobSessionId == Guid.Empty ? item.Id : item.JobSessionId;
                if (!workspaces.Add(workspace))
                    throw new JsonException("The saved queue contains duplicate workspace identities.");
                result[index] = Normalize(item);
            }
            return result;
        }
        catch (FileNotFoundException) { return []; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LoadFailed = true;
            throw;
        }
        catch (Exception ex)
        {
            LoadFailed = true;
            logger.Error("Could not read the saved download queue.", ex);
            return [];
        }
        finally { gate.Release(); }
    }

    private static DownloadQueueItem Normalize(DownloadQueueItem item)
    {
        if (item.JobSessionId == Guid.Empty)
            item.JobSessionId = item.Id;
        if (item.LiveSession is { } live)
        {
            if (live.SessionId == Guid.Empty) live.SessionId = item.JobSessionId;
            if (!Enum.IsDefined(live.StartPolicy)) live.StartPolicy = LiveStartPolicy.FromNow;
            live.PartIndex = Math.Max(1, live.PartIndex);
            live.Parts ??= [];
            if (live.RecordedDuration < TimeSpan.Zero) live.RecordedDuration = TimeSpan.Zero;
            // An older saved session may contain signed HLS/DASH URLs from raw
            // extractor JSON. The session never needs them for reconnect.
            item.RawMetadataJson = string.Empty;
        }
        item.SourceUrl ??= string.Empty;
        item.VideoId ??= string.Empty;
        item.Title ??= string.Empty;
        item.Channel ??= string.Empty;
        item.QualityPresetId = QualityPreset.Find(item.QualityPresetId ?? "best").Id;
        item.RequestedRange ??= MediaTimeRange.Full;
        item.SubtitleOptions ??= new();
        item.SubtitleOptions.Languages ??= [];
        item.SponsorBlockOptions ??= new();
        item.SponsorBlockOptions.Categories ??= ["sponsor"];
        if (!Enum.IsDefined(item.PreferredVideoContainer))
            item.PreferredVideoContainer = PreferredVideoContainer.Auto;
        item.TemporaryFiles ??= [];
        item.RawMetadataJson ??= string.Empty;
        item.StatusMessage ??= string.Empty;
        item.StatusMessageKey = string.IsNullOrWhiteSpace(item.StatusMessageKey)
            ? item.Status switch
            {
                DownloadStatus.Queued => "Queue.Detail.Queued",
                DownloadStatus.Waiting => "Queue.Detail.Preparing",
                DownloadStatus.DownloadingVideo => "Queue.Detail.DownloadingVideo",
                DownloadStatus.DownloadingAudio => "Queue.Detail.DownloadingAudio",
                DownloadStatus.Merging => "Queue.Detail.Merging",
                DownloadStatus.Finalizing => "Queue.Detail.Finalizing",
                DownloadStatus.Completed when item.HasPostProcessingWarnings => "Queue.Detail.CompletedWarnings",
                DownloadStatus.Completed => "Queue.Detail.Completed",
                DownloadStatus.Failed => "Queue.Detail.Failed",
                DownloadStatus.Cancelled => "Queue.Detail.Cancelled",
                DownloadStatus.WaitingForLive => "Queue.Detail.WaitingForLive",
                DownloadStatus.RecordingLive => "Queue.Detail.RecordingLive",
                DownloadStatus.ReconnectingLive => "Queue.Detail.ReconnectingLive",
                DownloadStatus.Partial => "Queue.Detail.Partial",
                DownloadStatus.Interrupted => "Queue.Detail.Interrupted",
                _ => null
            }
            : item.StatusMessageKey;
        return item;
    }

    public async Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (LoadFailed) throw new IOException("Queue could not be loaded; preserve the original file and restart after recovery.");
            await AtomicJsonFile.WriteAsync(paths.QueueFile, items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
