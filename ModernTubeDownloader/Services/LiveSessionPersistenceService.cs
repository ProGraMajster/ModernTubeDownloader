using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class LiveSessionPersistenceService(AppPaths paths, IAppLogger logger)
{
    public bool LoadFailed { get; private set; }
    public async Task<IReadOnlyList<LiveSession>> LoadAsync(CancellationToken token = default)
    {
        JsonElement[] records;
        try
        {
            await using var stream = new FileStream(paths.LiveSessionsFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            records = await JsonSerializer.DeserializeAsync<JsonElement[]>(stream, JsonDefaults.Options, token).ConfigureAwait(false)
                ?? throw new JsonException("LIVE storage is null, not a session collection.");
        }
        catch (FileNotFoundException) { return []; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { LoadFailed = true; throw; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadFailed = true;
            logger.Error("LIVE persistence could not be read; writes and recording admission are blocked, original data retained.", ex);
            return [];
        }
        var sessions = new List<LiveSession>();
        var ids = new HashSet<Guid>();
        foreach (var record in records)
        {
            try
            {
                if (record.ValueKind != JsonValueKind.Object)
                    throw new JsonException("LIVE record is not an object.");
                var identity = record.EnumerateObject().Where(property =>
                    property.Name.Equals("sessionId", StringComparison.OrdinalIgnoreCase)).ToArray();
                // The model initializer is for newly admitted sessions, never a
                // replacement identity for damaged saved data.
                if (identity.Length != 1 || identity[0].Value.ValueKind != JsonValueKind.String ||
                    !identity[0].Value.TryGetGuid(out var savedId) || savedId == Guid.Empty)
                    throw new JsonException("Missing or ambiguous saved LIVE identity.");
                var session = record.Deserialize<LiveSession>(JsonDefaults.Options)
                    ?? throw new JsonException("Empty LIVE record.");
                if (session.Id != savedId || !Enum.IsDefined(session.State) ||
                    !Enum.IsDefined(session.StartPolicy) ||
                    !YtDlpMetadataService.TryValidateSourceUrl(session.SourceUrl, out var url, out _))
                    throw new JsonException("Invalid LIVE identity, state or source.");
                if (!ids.Add(session.Id)) throw new JsonException("Duplicate LIVE identity.");
                session.SourceUrl = url;
                session.Parts ??= [];
                session.PartIndex = Math.Max(1, session.PartIndex);
                session.QualityPresetId = QualityPreset.Find(session.QualityPresetId).Id;
                sessions.Add(session);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
            {
                LoadFailed = true;
                logger.Warning($"Invalid LIVE record retained in original storage; recording admission and writes blocked. Error={ex.GetType().Name}");
            }
        }
        return sessions;
    }

    public Task SaveAsync(IReadOnlyList<LiveSession> sessions, CancellationToken token = default)
    {
        if (LoadFailed) throw new IOException("LIVE storage could not be loaded; restore the original file and reopen the profile.");
        return AtomicJsonFile.WriteAsync(paths.LiveSessionsFile, sessions, token);
    }

    // Save the destination before removing source entries. Retrying after any
    // interruption is idempotent by the durable original SessionId.
    public async Task MigrateLegacyQueueAsync(SettingsService settings, CancellationToken token = default)
    {
        var queue = await new QueuePersistenceService(paths, logger).LoadAsync(token).ConfigureAwait(false);
        if (!queue.Any(item => item.LiveSession is not null)) return;
        var sessions = (await LoadAsync(token)).ToList();
        // Keep both source and destination intact and permit a diagnostic UI.
        // Queue.LoadAsync independently blocks any unmigrated LIVE source rows.
        if (LoadFailed) return;
        foreach (var item in queue.Where(item => item.LiveSession is not null))
        {
            var live = item.LiveSession!;
            var wrapperFields = LegacyQueueFields(item);
            var migrated = sessions.Find(session => session.Id == live.Id);
            if (migrated is not null)
            {
                // A crash can leave both stores. Preserve unknown source evidence
                // before removing the legacy row, without replacing newer state.
                migrated.AdditionalData = MergeFields(migrated.AdditionalData, live.AdditionalData);
                migrated.LegacyQueueAdditionalData = MergeFields(MergeFields(migrated.LegacyQueueAdditionalData, live.LegacyQueueAdditionalData), wrapperFields);
                continue;
            }
            live.LegacyQueueAdditionalData = MergeFields(live.LegacyQueueAdditionalData, wrapperFields);
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

    private static Dictionary<string, JsonElement>? MergeFields(Dictionary<string, JsonElement>? destination, Dictionary<string, JsonElement>? source)
    {
        if (source is null) return JsonExtensionFields.Copy(destination);
        var merged = JsonExtensionFields.Copy(destination) ?? new(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (merged.TryGetValue(key, out var existing) && !JsonElement.DeepEquals(existing, value))
                throw new InvalidDataException("Conflicting legacy LIVE extension fields; both original stores are retained.");
            merged[key] = value.Clone();
        }
        return merged;
    }

    private static Dictionary<string, JsonElement>? LegacyQueueFields(DownloadQueueItem item)
    {
        var fields = JsonExtensionFields.Copy(item.AdditionalData);
        Preserve("requestedRange", item.RequestedRange.AdditionalData);
        Preserve("subtitleOptions", item.SubtitleOptions.AdditionalData);
        Preserve("sponsorBlockOptions", item.SponsorBlockOptions.AdditionalData);
        Preserve("selectedFormats", item.SelectedFormats?.AdditionalData);
        return fields;

        void Preserve(string property, Dictionary<string, JsonElement>? unknown)
        {
            if (unknown is not { Count: > 0 }) return;
            // Preserve only extension portions. Known VOD option semantics do not
            // acquire new meaning in the independent LIVE runtime.
            fields = MergeFields(fields, new() { [property] = JsonSerializer.SerializeToElement(unknown, JsonDefaults.Options) });
        }
    }
}
