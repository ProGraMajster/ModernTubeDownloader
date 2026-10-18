using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Batch;

/// <summary>Preparation only. Download ownership, authentication, recovery and concurrency remain canonical.</summary>
public sealed class BatchImportService : IDisposable
{
    private readonly BatchSessionStore store;
    private readonly Func<string, CancellationToken, Task<SourceCheckResult>> check;
    private readonly DownloadQueueService queue;
    private readonly SettingsService settings;
    private readonly IAppLogger logger;
    private readonly Dictionary<Guid, BatchImportSession> loaded = [];
    private readonly object sync = new();
    private CancellationTokenSource? analysisCancellation;
    private Task analysis = Task.CompletedTask;
    private readonly HashSet<Task> handoffs = [];
    private TaskCompletionSource resume = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public BatchImportService(BatchSessionStore store, Func<string, CancellationToken, Task<SourceCheckResult>> check,
        DownloadQueueService queue, SettingsService settings, IAppLogger logger)
    { this.store = store; this.check = check; this.queue = queue; this.settings = settings; this.logger = logger; }
    public Guid? ActiveSessionId { get; private set; }
    public event EventHandler<(int Analyzed, int Total)>? AnalysisCompleted;
    public bool IsAnalyzing { get { lock (sync) return !analysis.IsCompleted; } }
    public Task<IReadOnlyList<BatchImportSession>> RecentAsync(CancellationToken token = default) => store.RecentAsync(token);
    public async Task<BatchImportSession> OpenAsync(Guid id, CancellationToken token = default)
    {
        lock (sync) if (loaded.TryGetValue(id, out var existing)) return existing;
        var session = await Task.Run(() => store.LoadAsync(id, token), token).ConfigureAwait(false);
        lock (sync) { if (loaded.TryGetValue(id, out var existing)) return existing; loaded.Add(id, session); }
        await RefreshQueueAsync(session, token); return session;
    }
    public async Task<BatchImportSession> ImportAsync(string file, string quality = "best", PreferredVideoContainer container = PreferredVideoContainer.Auto,
        int workers = 1, CancellationToken token = default, IProgress<BatchOperationProgress>? progress = null)
    {
        if (!QualityPreset.All.Any(value => value.Id == quality) || !Enum.IsDefined(container) || workers is < 1 or > 8)
            throw new ArgumentException("Batch.Error.Options");
        var session = new BatchImportSession { FileName = Path.GetFileName(file), DefaultQuality = quality, DefaultContainer = container, AnalysisWorkers = workers };
        await Task.Run(() => store.CreateAsync(session, Rows(token), token), token);
        lock (sync) loaded.Add(session.Id, session);
        progress?.Report(new("Import", session.ImportedCount, session.ImportedCount));
        return session;
        async IAsyncEnumerable<BatchImportEntry> Rows([EnumeratorCancellation] CancellationToken cancellation)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal); var ordinal = 0;
            await foreach (var raw in BatchImportParser.ParseAsync(file, cancellation))
            {
                var entry = BatchImportParser.Validate(raw, session, ++ordinal);
                if (entry.Status == BatchEntryStatus.Pending && !identities.Add(entry.Identity)) entry.Status = BatchEntryStatus.Duplicate;
                yield return entry;
                if (ordinal % 1000 == 0) progress?.Report(new("Import", ordinal));
            }
        }
    }
    public Task StartAsync(BatchImportSession session, IReadOnlySet<Guid>? selected = null)
    {
        lock (sync)
        {
            if (!analysis.IsCompleted) throw new InvalidOperationException("Batch.Error.Busy");
            analysisCancellation?.Dispose(); analysisCancellation = new();
            ActiveSessionId = session.Id; session.Paused = false; resume.TrySetResult();
            Guid[] ids;
            lock (session.Sync) ids = session.Entries.Where(entry => !entry.Removed &&
                entry.QueueItemId is null &&
                (entry.Status is BatchEntryStatus.Pending or BatchEntryStatus.Failed or BatchEntryStatus.Cancelled or BatchEntryStatus.AuthRequired) &&
                (selected is null || selected.Contains(entry.Id))).Select(entry => entry.Id).ToArray();
            analysis = Task.Run(() => AnalyzeAsync(session, ids, analysisCancellation.Token));
            return analysis;
        }
    }
    public async Task SetPausedAsync(BatchImportSession session, bool paused)
    {
        lock (sync) { if (session.Paused != paused) { session.Paused = paused; if (paused) resume = NewSignal(); else resume.TrySetResult(); } }
        await store.SaveSessionAsync(session);
    }
    public async Task CancelAsync()
    { Task pending; lock (sync) { analysisCancellation?.Cancel(); pending = analysis; } await pending; }
    private async Task AnalyzeAsync(BatchImportSession session, Guid[] ids, CancellationToken token)
    {
        var cursor = -1;
        try { await Task.WhenAll(Enumerable.Range(0, Math.Clamp(session.AnalysisWorkers, 1, 8)).Select(_ => Worker())); }
        finally { session.Paused = true; await store.SaveSessionAsync(session); if (!token.IsCancellationRequested) AnalysisCompleted?.Invoke(this, (ids.Length, session.ImportedCount)); }
        async Task Worker()
        {
            while (!token.IsCancellationRequested)
            {
                Task wait; lock (sync) wait = session.Paused ? resume.Task : Task.CompletedTask;
                try { await wait.WaitAsync(token); } catch (OperationCanceledException) { return; }
                var index = Interlocked.Increment(ref cursor); if (index >= ids.Length) return;
                BatchImportEntry entry;
                lock (session.Sync)
                { var row = session.ById[ids[index]]; if (row.Removed) continue; row.Status = BatchEntryStatus.Analyzing; row.Revision++; entry = row.Copy(); }
                if (entry.Removed) continue;
                await store.SaveEntryAsync(session, entry.Copy());
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(2));
                    var result = await check(entry.Url, deadline.Token);
                    entry.ErrorKey = result.MessageKey;
                    if (result.AuthenticationMayBeRequired) entry.Status = BatchEntryStatus.AuthRequired;
                    else if (result.OpenLive || result.Playlist is not null || result.Facts?.Kind == SourceMediaKind.Playlist)
                    { entry.Status = BatchEntryStatus.Unsupported; entry.ErrorKey = "Batch.Error.Interactive"; }
                    else if (result.Metadata is not { } metadata) entry.Status = result.MessageKey == "Sources.Unsupported" ? BatchEntryStatus.Unsupported : BatchEntryStatus.Failed;
                    else
                    {
                        entry.Title = metadata.Title; entry.ErrorKey = entry.Range.Validate(metadata.DurationSeconds);
                        if (entry.ErrorKey is not null) entry.Status = BatchEntryStatus.Failed;
                        else
                        {
                            var format = FormatSelector.Select(metadata, QualityPreset.Find(entry.Quality), entry.Container);
                            entry.EstimatedSizeBytes = MediaSizeEstimator.Estimate(metadata, format, entry.Range);
                            entry.DurationSeconds = metadata.DurationSeconds;
                            await store.SaveMetadataAsync(session.Id, entry.Id, metadata, token);
                            entry.HasMetadata = true;
                            entry.Status = queue.ContainsPendingOrActive(metadata.Id, entry.Quality, entry.Container, entry.Range,
                                settings.Current.SubtitleDefaults, settings.Current.SponsorBlockDefaults) ? BatchEntryStatus.Duplicate : BatchEntryStatus.Ready;
                        }
                    }
                }
                catch (OperationCanceledException) { entry.Status = BatchEntryStatus.Cancelled; entry.ErrorKey = null; }
                catch (Exception ex) { entry.Status = BatchEntryStatus.Failed; entry.ErrorKey = "Batch.Error.Analysis"; logger.Error("Batch analysis failed for entry " + entry.Id, ex); }
                lock (session.Sync)
                { entry.Revision = session.ById[entry.Id].Revision + 1; session.ById[entry.Id] = entry; session.Entries[entry.Ordinal - 1] = entry; entry = entry.Copy(); }
                await store.SaveEntryAsync(session, entry);
            }
        }
    }
    public BatchPage Page(BatchImportSession session, BatchFilter filter, string search, int page, int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100); search = search.Trim();
        lock (session.Sync)
        {
            var rows = session.Entries.Where(entry => Matches(entry, filter, search));
            var total = rows.Count(); var pages = Math.Max(1, (total + pageSize - 1) / pageSize); page = Math.Clamp(page, 0, pages - 1);
            return new(rows.Skip(page * pageSize).Take(pageSize).Select(entry => entry.Copy()).ToArray(), total, page, pages);
        }
    }
    private static bool Matches(BatchImportEntry entry, BatchFilter filter, string search)
        => !entry.Removed && Matches(entry.Status, filter) && (search.Length == 0 ||
            entry.Url.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Source.Contains(search, StringComparison.OrdinalIgnoreCase));

    /// <summary>Actions apply to the filtered set across pages, intersected with explicit selection. Never null/all by accident.</summary>
    public IReadOnlySet<Guid> ActionSelection(BatchImportSession session, BatchFilter filter, string search, IReadOnlySet<Guid>? selected = null)
    {
        search = search.Trim();
        lock (session.Sync) return session.Entries.Where(entry => Matches(entry, filter, search) &&
            (selected is null || selected.Count == 0 || selected.Contains(entry.Id))).Select(entry => entry.Id).ToHashSet();
    }
    private static bool Matches(BatchEntryStatus status, BatchFilter filter) => filter switch
    {
        BatchFilter.Ready => status == BatchEntryStatus.Ready, BatchFilter.Errors => status is BatchEntryStatus.Failed or BatchEntryStatus.Cancelled,
        BatchFilter.Duplicates => status == BatchEntryStatus.Duplicate, BatchFilter.Unsupported => status == BatchEntryStatus.Unsupported,
        BatchFilter.AuthRequired => status == BatchEntryStatus.AuthRequired, BatchFilter.Queued => status is BatchEntryStatus.AddedToQueue or BatchEntryStatus.Downloading,
        BatchFilter.Completed => status == BatchEntryStatus.Completed, _ => true
    };
    public Task EditAsync(BatchImportSession session, IReadOnlySet<Guid> ids, string? quality = null,
        PreferredVideoContainer? container = null, MediaTimeRange? range = null, bool remove = false)
    {
        var selection = ids.ToHashSet();
        return Task.Run(() => EditCoreAsync(session, selection, quality, container, range, remove));
    }
    private async Task EditCoreAsync(BatchImportSession session, IReadOnlySet<Guid> ids, string? quality,
        PreferredVideoContainer? container, MediaTimeRange? range, bool remove)
    {
        if (quality is not null && !QualityPreset.All.Any(value => value.Id == quality) || container is { } c && !Enum.IsDefined(c) || range?.Validate(null) is not null)
            throw new ArgumentException("Batch.Error.Options");
        foreach (var id in ids)
        {
            BatchImportEntry copy;
            lock (session.Sync)
            {
                if (!session.ById.TryGetValue(id, out var entry) || entry.Removed || entry.Status is BatchEntryStatus.Analyzing or BatchEntryStatus.Validating || entry.QueueItemId is not null) continue;
                if (remove) { entry.Removed = true; entry.Status = BatchEntryStatus.Skipped; }
                else { if (quality is not null) entry.Quality = quality; if (container is not null) entry.Container = container.Value; if (range is not null) entry.Range = range; entry.EstimatedSizeBytes = null; }
                entry.Revision++; copy = entry.Copy();
            }
            await store.SaveEntryAsync(session, copy);
        }
    }
    public Task<VideoMetadata?> MetadataAsync(BatchImportSession session, Guid id, CancellationToken token = default)
        => Task.Run(() => store.MetadataAsync(session.Id, id, token), token);
    public Task<int> AddReadyAsync(BatchImportSession session, IReadOnlySet<Guid>? selected = null, CancellationToken token = default,
        IProgress<BatchOperationProgress>? progress = null)
    {
        var selection = selected?.ToHashSet();
        // Metadata deserialization, format validation, journal writes and synchronous
        // canonical queue observers must never execute on the window's context.
        var task = Task.Run(() => AddReadyCoreAsync(session, selection, token, progress), token);
        lock (sync) handoffs.Add(task);
        return AwaitHandoffAsync(task);
    }
    private async Task<int> AwaitHandoffAsync(Task<int> task)
    {
        try { return await task.ConfigureAwait(false); }
        finally { lock (sync) handoffs.Remove(task); }
    }
    private async Task<int> AddReadyCoreAsync(BatchImportSession session, IReadOnlySet<Guid>? selected, CancellationToken token,
        IProgress<BatchOperationProgress>? progress)
    {
        BatchImportEntry[] entries;
        lock (session.Sync)
        {
            entries = session.Entries.Where(entry => !entry.Removed && entry.Status == BatchEntryStatus.Ready && (selected is null || selected.Contains(entry.Id))).Select(entry => entry.Copy()).ToArray();
            foreach (var entry in entries) session.ById[entry.Id].Status = BatchEntryStatus.Validating;
        }
        var added = 0; var processed = 0;
        progress?.Report(new("Add", 0, entries.Length));
        try
        {
        // Bounded handoff chunks: one canonical event/save per chunk, never a second download scheduler.
        foreach (var chunk in entries.Chunk(100))
        {
            var candidates = new List<(BatchImportEntry Entry, DownloadQueueItem Item)>();
            foreach (var entry in chunk)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var metadata = await store.MetadataAsync(session.Id, entry.Id, token) ?? throw new InvalidDataException("Batch.Error.Metadata");
                    if (string.IsNullOrWhiteSpace(metadata.WebpageUrl)) metadata.WebpageUrl = entry.Url;
                    if (entry.Range.Validate(metadata.DurationSeconds) is not null) throw new InvalidDataException("Range.Error.Invalid");
                    var preset = QualityPreset.Find(entry.Quality);
                    var item = DownloadQueueItem.FromMetadata(metadata, preset, FormatSelector.Select(metadata, preset, entry.Container), entry.Container, entry.Range,
                        settings.Current.SubtitleDefaults.Copy(), settings.Current.SponsorBlockDefaults.Copy());
                    candidates.Add((entry, item));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { BatchImportEntry failed; lock (session.Sync) { var row = session.ById[entry.Id]; row.Status = BatchEntryStatus.Failed; row.ErrorKey = "Batch.Error.Options"; row.Revision++; failed = row.Copy(); } logger.Error("Batch queue preparation failed for entry " + entry.Id, ex); await store.SaveEntryAsync(session, failed); }
            }
            var result = queue.AddRangeSkippingDuplicates(candidates.Select(value => value.Item).ToArray()); added += result.Added;
            var queueIds = queue.Snapshot().Select(item => item.Id).ToHashSet();
            var updates = new List<BatchImportEntry>();
            foreach (var candidate in candidates)
            {
                BatchImportEntry updated;
                lock (session.Sync)
                { updated = session.ById[candidate.Entry.Id]; updated.EstimatedSizeBytes = candidate.Item.EstimatedSizeBytes; if (queueIds.Contains(candidate.Item.Id)) { updated.QueueItemId = candidate.Item.Id; updated.Status = BatchEntryStatus.AddedToQueue; } else updated.Status = BatchEntryStatus.Duplicate; updated.Revision++; updated = updated.Copy(); }
                updates.Add(updated);
            }
            // Keep links to admitted runtime jobs even if their durable checkpoint
            // fails; never reset those rows to Ready and submit them a second time.
            // Admission already happened: cancellation stops the next chunk, not
            // this chunk's link journal/durable checkpoint. Shutdown awaits it too.
            await store.SaveEntriesAsync(session, updates, CancellationToken.None).ConfigureAwait(false);
            await queue.CheckpointBeforeStartAsync(CancellationToken.None).ConfigureAwait(false);
            processed += chunk.Length; progress?.Report(new("Add", processed, entries.Length));
        }
        return added;
        }
        finally
        {
            var resets = new List<BatchImportEntry>();
            foreach (var entry in entries)
            {
                BatchImportEntry? reset = null;
                lock (session.Sync) { var row = session.ById[entry.Id]; if (row.Status == BatchEntryStatus.Validating) { row.Status = BatchEntryStatus.Ready; row.Revision++; reset = row.Copy(); } }
                if (reset is not null) resets.Add(reset);
            }
            if (resets.Count > 0) await store.SaveEntriesAsync(session, resets).ConfigureAwait(false);
        }
    }
    public Task RefreshQueueAsync(BatchImportSession session, CancellationToken token = default)
        => Task.Run(() => RefreshQueueCoreAsync(session, token), token);
    private async Task RefreshQueueCoreAsync(BatchImportSession session, CancellationToken token)
    {
        var updates = new List<BatchImportEntry>();
        BatchImportEntry[] entries; lock (session.Sync) entries = session.Entries.Where(value => value.QueueItemId is not null).Select(value => value.Copy()).ToArray();
        // Links are created only after canonical admission. Copy them first so a
        // concurrent handoff cannot look missing in an older queue snapshot.
        var queueItems = queue.ProgressSnapshot().ToDictionary(item => item.Id);
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (!queueItems.TryGetValue(entry.QueueItemId!.Value, out var item))
            {
                lock (session.Sync)
                {
                    var row = session.ById[entry.Id];
                    if (row.QueueItemId != entry.QueueItemId) continue;
                    row.Status = BatchEntryStatus.Failed;
                    row.QueueItemId = null; row.ErrorKey = "Batch.Error.QueueMissing"; row.Revision++; updates.Add(row.Copy());
                }
                continue;
            }
            var status = item.Status == DownloadStatus.Completed ? BatchEntryStatus.Completed : item.Status is DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial or DownloadStatus.Interrupted ? BatchEntryStatus.Failed :
                item.Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing ? BatchEntryStatus.Downloading : BatchEntryStatus.AddedToQueue;
            if (status == entry.Status) continue;
            BatchImportEntry updated; lock (session.Sync) { var row = session.ById[entry.Id]; if (row.QueueItemId != entry.QueueItemId) continue; row.Status = status; row.Revision++; updated = row.Copy(); }
            updates.Add(updated);
        }
        if (updates.Count > 0) await store.SaveEntriesAsync(session, updates, token).ConfigureAwait(false);
    }
    public Task ExportAsync(BatchImportSession session, string file, CancellationToken token = default)
        => Task.Run(() => ExportCoreAsync(session, file, token), token);
    private async Task ExportCoreAsync(BatchImportSession session, string file, CancellationToken token)
    {
        var json = Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase);
        if (!json && !Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Batch.Error.Format");
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        if (json)
        {
            await using var writer = new Utf8JsonWriter(stream, new() { Indented = true }); writer.WriteStartArray();
            foreach (var entry in session.Snapshot())
            {
                token.ThrowIfCancellationRequested(); writer.WriteStartObject(); writer.WriteString("url", BatchImportParser.ExportUrl(entry.Url));
                writer.WriteString("quality", entry.Quality); writer.WriteString("container", entry.Container.ToString());
                writer.WriteString("title", entry.Title); writer.WriteString("status", entry.Status.ToString()); writer.WriteString("source", entry.Source); writer.WriteString("errorCode", entry.ErrorKey);
                if (entry.Range.Start is { } start) writer.WriteNumber("start", start.TotalSeconds); if (entry.Range.End is { } end) writer.WriteNumber("end", end.TotalSeconds);
                writer.WriteEndObject();
                if (writer.BytesPending > 81920) await writer.FlushAsync(token);
            }
            writer.WriteEndArray(); await writer.FlushAsync(token);
        }
        else
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)); await writer.WriteLineAsync("url,quality,container,start,end,status,title,errorCode,source");
            foreach (var entry in session.Snapshot())
            {
                token.ThrowIfCancellationRequested();
                var fields = new[] { BatchImportParser.ExportUrl(entry.Url), entry.Quality, entry.Container.ToString(), entry.Range.Start?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", entry.Range.End?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", entry.Status.ToString(), entry.Title, entry.ErrorKey ?? "", entry.Source };
                await writer.WriteLineAsync(string.Join(',', fields.Select(Csv)).AsMemory(), token);
            }
        }
        static string Csv(string value) => "\"" + (value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
    }
    public async Task StopAsync()
    {
        await CancelAsync();
        Task[] pending; BatchImportSession[] sessions;
        lock (sync) { pending = handoffs.ToArray(); sessions = loaded.Values.ToArray(); }
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* Cancelled handoffs have finished their admitted chunk's checkpoint. */ }
        foreach (var session in sessions) await RefreshQueueAsync(session).ConfigureAwait(false);
    }
    public void Dispose() { lock (sync) analysisCancellation?.Cancel(); }
}
