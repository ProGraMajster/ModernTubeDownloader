using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Organizer;

/// <summary>Plans and journals relocation of existing MTD downloads; never downloads or decodes media.</summary>
public sealed partial class DownloadOrganizerService : IDisposable
{
    public const int MaximumScanFiles = 100_000, MaximumPlanEntries = 10_000, PageSize = 40;
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".ogg", ".opus", ".flac", ".wav", ".mov", ".avi" };
    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".srt", ".vtt", ".ass", ".lrc", ".jpg", ".jpeg", ".png", ".webp", ".description", ".chapters" };
    private readonly AppPaths paths;
    private readonly HistoryService history;
    private readonly DownloadQueueService queue;
    private readonly SettingsService settings;
    private readonly IAppLogger logger;
    private readonly IOrganizerFaultObserver? observer;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object workSync = new();
    private readonly HashSet<Task> activeTasks = [];
    private int activeCount, disposed;
    private long referenceVersion, indexedReferenceVersion = -1;
    private HashSet<string> protectedReferences = new(OrganizerPathSafety.Comparer);
    private sealed record PlanLookup(Dictionary<string, DownloadHistoryEntry[]> History,
        Dictionary<string, DownloadQueueItem[]> Queue, Dictionary<string, string[]> CompanionsByStem,
        Dictionary<string, OrganizerFile> Fingerprints);
    private volatile bool paused;
    private TaskCompletionSource pauseCompletion = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsBusy => Volatile.Read(ref activeCount) > 0;
    public bool IsPaused => paused;
    public event EventHandler<OrganizerProgress>? ProgressChanged;
    public string JournalDirectory => Path.Combine(paths.RootDirectory, "Organizer");

    public DownloadOrganizerService(AppPaths paths, HistoryService history, DownloadQueueService queue,
        SettingsService settings, IAppLogger logger, IOrganizerFaultObserver? observer = null)
    {
        this.paths = paths; this.history = history; this.queue = queue; this.settings = settings; this.logger = logger; this.observer = observer;
        queue.Changed += QueueReferencesChanged; history.Changed += HistoryReferencesChanged;
    }
    private void QueueReferencesChanged(object? sender, QueueChangedEventArgs args) => Interlocked.Increment(ref referenceVersion);
    private void HistoryReferencesChanged(object? sender, EventArgs args) => Interlocked.Increment(ref referenceVersion);

    public static string SuggestDestination(string root, string listName) => OrganizerPathSafety.ListFolder(root, listName);
    public void SetPaused(bool value)
    {
        if (value == paused) return;
        if (value) { pauseCompletion = NewSignal(); paused = true; }
        else { paused = false; pauseCompletion.TrySetResult(); }
    }
    private async Task PauseBoundary(CancellationToken token)
    { token.ThrowIfCancellationRequested(); while (paused) await pauseCompletion.Task.WaitAsync(token).ConfigureAwait(false); }

    public Task<OrganizerPlan> BuildPlanAsync(IReadOnlyList<OrganizerSourceList> lists, string scanRoot, bool recursive,
        bool requireJson = true, CancellationToken token = default) =>
        RunOwnedAsync(ct => BuildPlanCoreAsync(lists, scanRoot, recursive, requireJson, ct), token);

    public async Task StopAsync()
    {
        Dispose();
        Task[] pending; lock (workSync) pending = activeTasks.ToArray();
        try { await Task.WhenAll(pending).ConfigureAwait(false); } catch { /* each mutation journal records its recovery state */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); SetPaused(false);
        queue.Changed -= QueueReferencesChanged; history.Changed -= HistoryReferencesChanged;
    }

    private Task<T> RunOwnedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        lock (workSync)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref activeCount);
            var task = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
                var entered = false;
                try
                {
                    await operationGate.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
                    return await operation(linked.Token).ConfigureAwait(false);
                }
                finally { if (entered) operationGate.Release(); Interlocked.Decrement(ref activeCount); SetPaused(false); }
            });
            activeTasks.Add(task);
            _ = task.ContinueWith(completed => { lock (workSync) activeTasks.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<OrganizerPlan> BuildPlanCoreAsync(IReadOnlyList<OrganizerSourceList> lists, string scanRoot,
        bool recursive, bool requireJson, CancellationToken token)
    {
        scanRoot = OrganizerPathSafety.DirectoryPath(scanRoot, true);
        if (IsProtected(scanRoot)) throw new IOException("Organizer.Error.OutsideRoot");
        if (lists.Count is < 1 or > 100) throw new IOException("Organizer.Error.Limit");
        var plan = new OrganizerPlan { ScanRoot = scanRoot, Recursive = recursive, RequireJson = requireJson };
        var files = new HashSet<string>(OrganizerPathSafety.Comparer);
        var directories = new Stack<string>(); directories.Push(scanRoot);
        while (directories.TryPop(out var directory))
        {
            await PauseBoundary(token).ConfigureAwait(false);
            OrganizerPathSafety.CheckAncestors(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0 || IsProtected(entry)) { plan.SkippedFiles++; continue; }
                if ((attributes & FileAttributes.Directory) != 0) { if (recursive) directories.Push(entry); continue; }
                if (++plan.ScannedFiles > MaximumScanFiles) throw new IOException("Organizer.Error.Limit");
                if (Path.GetFileName(entry).Contains(".part", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) { plan.SkippedFiles++; continue; }
                files.Add(Path.GetFullPath(entry));
                if (plan.ScannedFiles % 100 == 0) Report("Organizer.Phase.Scanning", plan.ScannedFiles, 0, 0, 0);
            }
        }
        var media = files.Where(file => MediaExtensions.Contains(Path.GetExtension(file))).ToHashSet(OrganizerPathSafety.Comparer);
        var byStem = media.GroupBy(file => Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file)), OrganizerPathSafety.Comparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer);
        var known = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var historyRows = history.Snapshot(); var queueRows = queue.Snapshot();
        var lookup = new PlanLookup(historyRows.GroupBy(entry => entry.FinalPath, OrganizerPathSafety.Comparer)
            .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer),
            queueRows.Where(item => item.FinalFile is not null).GroupBy(item => item.FinalFile!, OrganizerPathSafety.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer),
            files.Where(file => CompanionExtensions.Contains(Path.GetExtension(file)))
                .GroupBy(file => CompanionStem(file, byStem), OrganizerPathSafety.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer), new(OrganizerPathSafety.Comparer));
        void Add(string url, string path)
        {
            if (!media.Contains(path) || !TryIdentity(url, out var identity)) return;
            if (!known.TryGetValue(identity, out var pathsForId)) known[identity] = pathsForId = new(OrganizerPathSafety.Comparer);
            pathsForId.Add(path);
        }
        foreach (var item in historyRows.Where(item => !item.WasLiveRecording && !item.WasPartial)) Add(item.SourceUrl, item.FinalPath);
        foreach (var item in queueRows.Where(item => item.Status == DownloadStatus.Completed && item.FinalFile is not null)) Add(item.SourceUrl, item.FinalFile!);
        foreach (var json in files.Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            await PauseBoundary(token).ConfigureAwait(false);
            var stem = json.EndsWith(".info.json", StringComparison.OrdinalIgnoreCase) ? json[..^10] : json[..^5];
            if (!byStem.TryGetValue(stem, out var mediaFiles)) continue;
            var identity = await MetadataIdentityAsync(json, token).ConfigureAwait(false);
            if (identity is null) continue;
            if (!known.TryGetValue(identity, out var matches)) known[identity] = matches = new(OrganizerPathSafety.Comparer);
            foreach (var file in mediaFiles) matches.Add(file);
        }
        foreach (var list in lists)
        {
            var destination = OrganizerPathSafety.DirectoryPath(list.DestinationDirectory);
            if (IsProtected(destination)) throw new IOException("Organizer.Error.OutsideRoot");
            var session = new BatchImportSession { FileName = list.Name };
            var rows = list.Entries ?? await ReadListAsync(list.SourceFile ?? throw new IOException("Organizer.Error.List"), session, token).ConfigureAwait(false);
            var listIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                await PauseBoundary(token).ConfigureAwait(false);
                if (plan.Matches.Count >= MaximumPlanEntries) throw new IOException("Organizer.Error.Limit");
                var match = new OrganizerMatch { ListName = list.Name, Url = row.Url, Identity = row.Identity, DestinationDirectory = destination,
                    RequestedRange = row.Range, RequestedQuality = row.Quality };
                plan.Matches.Add(match);
                if (row.ErrorKey is not null || !TryIdentity(row.Url, out var identity)) { match.Status = OrganizerMatchStatus.Invalid; match.ReasonKey = "Organizer.Reason.Invalid"; continue; }
                match.Identity = identity;
                var listIdentity = identity + "|" + row.Quality + "|" + JsonSerializer.Serialize(row.Range, JsonDefaults.Options);
                if (!listIdentities.Add(listIdentity)) { match.Status = OrganizerMatchStatus.Duplicate; match.ReasonKey = "Organizer.Reason.Duplicate"; continue; }
                var candidates = known.GetValueOrDefault(identity)?.Where(path =>
                {
                    var recorded = lookup.History.GetValueOrDefault(path) ?? [];
                    var queued = lookup.Queue.GetValueOrDefault(path) ?? [];
                    return recorded.Length == 0 && queued.Length == 0 && row.Range == MediaTimeRange.Full && row.Quality == "best" ||
                        recorded.Any(entry => (entry.RequestedRange ?? MediaTimeRange.Full) == row.Range &&
                        (row.Quality == "best" || string.Equals(entry.QualityPresetId, row.Quality, StringComparison.OrdinalIgnoreCase))) ||
                        queued.Any(entry => (entry.RequestedRange ?? MediaTimeRange.Full) == row.Range &&
                        (row.Quality == "best" || string.Equals(entry.QualityPresetId, row.Quality, StringComparison.OrdinalIgnoreCase)));
                }).ToArray() ?? [];
                match.Candidates = candidates;
                if (candidates.Length == 0 && known.ContainsKey(identity))
                { match.Status = OrganizerMatchStatus.Attention; match.ReasonKey = "Organizer.Reason.Variant"; continue; }
                if (candidates.Length != 1) { match.Status = candidates.Length == 0 ? OrganizerMatchStatus.Missing : OrganizerMatchStatus.Ambiguous; match.ReasonKey = candidates.Length == 0 ? "Organizer.Reason.Missing" : "Organizer.Reason.Ambiguous"; continue; }
                await PopulateMatchAsync(plan, match, candidates[0], files, byStem, lookup, token).ConfigureAwait(false);
            }
        }
        foreach (var group in plan.Matches.Where(match => match.MediaPath is not null).GroupBy(match => match.MediaPath!, OrganizerPathSafety.Comparer).Where(group => group.Count() > 1))
            foreach (var match in group) { match.Status = OrganizerMatchStatus.Conflict; match.ReasonKey = "Organizer.Reason.MultipleLists"; }
        Report("Organizer.Phase.Planned", plan.Matches.Count, plan.Matches.Count, 0, plan.Matches.Sum(match => match.Bytes));
        return plan;
    }

    /// <summary>Explicitly resolves a candidate already proven to have the requested source identity.</summary>
    public Task<OrganizerMatch> ResolveCandidateAsync(OrganizerPlan plan, Guid matchId, string candidate, CancellationToken token = default) =>
        RunOwnedAsync(async ct =>
        {
            var match = plan.Matches.Single(value => value.Id == matchId);
            if (!match.Candidates.Contains(candidate, OrganizerPathSafety.Comparer)) throw new IOException("Organizer.Error.Selection");
            OrganizerPathSafety.CheckFile(candidate, plan.ScanRoot);
            var files = Directory.EnumerateFiles(Path.GetDirectoryName(candidate)!).Take(MaximumScanFiles + 1).ToHashSet(OrganizerPathSafety.Comparer);
            if (files.Count > MaximumScanFiles) throw new IOException("Organizer.Error.Limit");
            var byStem = files.Where(file => MediaExtensions.Contains(Path.GetExtension(file)))
                .GroupBy(file => Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file)), OrganizerPathSafety.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer);
            var lookup = new PlanLookup(history.Snapshot().GroupBy(entry => entry.FinalPath, OrganizerPathSafety.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer),
                queue.Snapshot().Where(item => item.FinalFile is not null).GroupBy(item => item.FinalFile!, OrganizerPathSafety.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer),
                files.Where(file => CompanionExtensions.Contains(Path.GetExtension(file))).GroupBy(file => CompanionStem(file, byStem), OrganizerPathSafety.Comparer)
                .ToDictionary(group => group.Key, group => group.ToArray(), OrganizerPathSafety.Comparer), new(OrganizerPathSafety.Comparer));
            match.Files.Clear();
            await PopulateMatchAsync(plan, match, candidate, files, byStem, lookup, ct).ConfigureAwait(false);
            if (match.Status == OrganizerMatchStatus.Found)
                foreach (var other in plan.Matches.Where(other => other.Id != match.Id && OrganizerPathSafety.Comparer.Equals(other.MediaPath, candidate)))
                { other.Status = OrganizerMatchStatus.Conflict; other.ReasonKey = "Organizer.Reason.MultipleLists"; }
            return match;
        }, token);

    private static async Task<IReadOnlyList<BatchImportEntry>> ReadListAsync(string file, BatchImportSession session, CancellationToken token)
    {
        var rows = new List<BatchImportEntry>();
        await foreach (var raw in BatchImportParser.ParseAsync(file, token))
        {
            if (rows.Count >= MaximumPlanEntries) throw new IOException("Organizer.Error.Limit");
            rows.Add(BatchImportParser.Validate(raw, session, rows.Count + 1));
        }
        return rows;
    }

    private async Task PopulateMatchAsync(OrganizerPlan plan, OrganizerMatch match, string media,
        HashSet<string> files, Dictionary<string, string[]> byStem, PlanLookup lookup, CancellationToken token)
    {
        match.MediaPath = media;
        var historyRecords = lookup.History.GetValueOrDefault(media) ?? [];
        var queueRecords = lookup.Queue.GetValueOrDefault(media) ?? [];
        if (historyRecords.Any(entry => entry.WasLiveRecording || entry.WasPartial) || queueRecords.Any(item => item.Status != DownloadStatus.Completed || item.LiveSession is not null))
        { match.Status = OrganizerMatchStatus.Attention; match.ReasonKey = "Organizer.Error.Active"; return; }
        var stem = Path.Combine(Path.GetDirectoryName(media)!, Path.GetFileNameWithoutExtension(media));
        if (byStem[stem].Length > 1) { match.Status = OrganizerMatchStatus.Ambiguous; match.ReasonKey = "Organizer.Reason.SharedBasename"; return; }
        try
        {
            var ids = historyRecords.Select(entry => entry.VideoId).Concat(queueRecords.Select(item => item.VideoId)).Where(id => id.Length > 0).Distinct().ToArray();
            match.Files.Add(await DescribeForPlanAsync(media, Path.Combine(match.DestinationDirectory, Path.GetFileName(media)), false, lookup, token).ConfigureAwait(false));
            var hasJson = false;
            foreach (var json in new[] { stem + ".json", stem + ".info.json" }.Where(files.Contains))
            {
                if (await MetadataIdentityAsync(json, token, ids.Length == 1 ? ids[0] : null).ConfigureAwait(false) != match.Identity)
                    throw new IOException("Organizer.Error.JsonIdentity");
                match.Files.Add(await DescribeForPlanAsync(json, Path.Combine(match.DestinationDirectory, Path.GetFileName(json)), false, lookup, token).ConfigureAwait(false)); hasJson = true;
            }
            if (!hasJson)
            {
                if (ids.Length == 1 && ids[0].Length > 0 && ids[0].IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                {
                    var central = Path.Combine(paths.MetadataDirectory, ids[0] + ".json");
                    if (File.Exists(central) && await MetadataIdentityAsync(central, token, ids[0]).ConfigureAwait(false) == match.Identity)
                    { match.Files.Add(await DescribeForPlanAsync(central, Path.Combine(match.DestinationDirectory, Path.GetFileName(stem) + ".json"), true, lookup, token).ConfigureAwait(false)); hasJson = true; }
                }
            }
            if (!hasJson && plan.RequireJson) throw new IOException("Organizer.Error.JsonMissing");
            foreach (var file in lookup.CompanionsByStem.GetValueOrDefault(stem) ?? [])
                match.Files.Add(await DescribeForPlanAsync(file, Path.Combine(match.DestinationDirectory, Path.GetFileName(file)), false, lookup, token).ConfigureAwait(false));
            if (match.Files.All(file => OrganizerPathSafety.Comparer.Equals(file.Source, file.Destination)))
            { match.Status = OrganizerMatchStatus.AlreadyOrganized; match.ReasonKey = "Organizer.Reason.Already"; }
            else if (match.Files.Any(file => File.Exists(file.Destination) || Directory.Exists(file.Destination)))
            { match.Status = OrganizerMatchStatus.Conflict; match.ReasonKey = "Organizer.Reason.Collision"; }
            else { match.Status = OrganizerMatchStatus.Found; match.ReasonKey = hasJson ? "Organizer.Reason.Verified" : "Organizer.Reason.JsonOptional"; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { match.Status = OrganizerMatchStatus.Attention; match.ReasonKey = error.Message.StartsWith("Organizer.", StringComparison.Ordinal) ? error.Message : "Organizer.Error.Access"; }
    }

    private bool IsProtected(string path) => new[] { paths.RootDirectory, settings.Current.TemporaryDirectory }
        .Where(root => !string.IsNullOrWhiteSpace(root)).Any(root => OrganizerPathSafety.Comparer.Equals(path, Path.GetFullPath(root)) || OrganizerPathSafety.IsWithin(path, root));
    private static string CompanionStem(string file, Dictionary<string, string[]> mediaByStem)
    {
        var stem = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file));
        if (mediaByStem.ContainsKey(stem)) return stem;
        if (Path.GetExtension(file).ToLowerInvariant() is ".srt" or ".vtt" or ".ass" or ".lrc")
        {
            var language = Path.GetExtension(stem);
            if (System.Text.RegularExpressions.Regex.IsMatch(language, @"^\.[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})?$")) return stem[..^language.Length];
        }
        return stem;
    }
    private static bool TryIdentity(string? url, out string identity)
    {
        identity = "";
        if (!YtDlpMetadataService.TryValidateSourceUrl(url ?? "", out var normalized, out _)) return false;
        identity = BatchImportParser.Identity(normalized); return true;
    }
    private static async Task<string?> MetadataIdentityAsync(string file, CancellationToken token, string? expectedId = null)
    {
        try
        {
            OrganizerPathSafety.CheckAncestors(file);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (stream.Length > 4 * 1024 * 1024) return null;
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            var properties = json.RootElement.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return null;
            if (!json.RootElement.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idValue.GetString())) return null;
            var id = idValue.GetString();
            if (expectedId is not null && id != expectedId) return null;
            foreach (var key in new[] { "webpage_url", "original_url" })
                if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && TryIdentity(value.GetString(), out var identity))
                    return identity.StartsWith("youtube:video:", StringComparison.Ordinal) && identity[14..] != id ? null : identity;
            return null; // An unqualified ID or title is not sufficient cross-provider evidence.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private static async Task<OrganizerFile> DescribeAsync(string source, string destination, bool copyOnly, CancellationToken token)
    {
        OrganizerPathSafety.CheckAncestors(source);
        await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None, 65536, true);
        var info = new FileInfo(source);
        return new() { Source = source, Destination = destination, Length = stream.Length,
            Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)),
            LastWriteUtc = info.LastWriteTimeUtc, CreationUtc = info.CreationTimeUtc, CopyOnly = copyOnly };
    }
    private static async Task<OrganizerFile> DescribeForPlanAsync(string source, string destination, bool copyOnly, PlanLookup lookup, CancellationToken token)
    {
        if (!lookup.Fingerprints.TryGetValue(source, out var fingerprint))
            lookup.Fingerprints[source] = fingerprint = await DescribeAsync(source, destination, copyOnly, token).ConfigureAwait(false);
        return new() { Source = source, Destination = destination, CopyOnly = copyOnly, Length = fingerprint.Length,
            Sha256 = fingerprint.Sha256, LastWriteUtc = fingerprint.LastWriteUtc, CreationUtc = fingerprint.CreationUtc };
    }
    private void Report(string phase, int completed, int total, long bytes, long totalBytes, string? name = null) =>
        ProgressChanged?.Invoke(this, new(phase, completed, total, bytes, totalBytes, name));
}
