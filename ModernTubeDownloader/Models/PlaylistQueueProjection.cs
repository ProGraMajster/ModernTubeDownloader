namespace ModernTubeDownloader.Models;

// A presentation of canonical records, never a second queue or scheduling order.
internal sealed record PlaylistGroupKey(string Authority, string PlaylistId)
{
    public static PlaylistGroupKey? From(DownloadQueueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PlaylistId) || string.IsNullOrWhiteSpace(item.PlaylistTitle) ||
            item.PlaylistIndex is not > 0 || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Host.Length == 0)
            return null;
        string host;
        try { host = uri.IdnHost.ToLowerInvariant(); }
        catch (UriFormatException) { return null; }
        if (host.EndsWith('.')) host = host[..^1];
        if (host.Length == 0 || host.Split('.').Any(label => label.Length == 0)) return null;
        if (uri.IsDefaultPort && host is "www.youtube.com" or "m.youtube.com" or "youtu.be") host = "youtube.com";
        return new(uri.IsDefaultPort ? host : $"{host}:{uri.Port}", item.PlaylistId);
    }
}

internal sealed record PlaylistGroup(PlaylistGroupKey Key, IReadOnlyList<DownloadQueueItem> Children)
{
    public string Title => Children[0].PlaylistTitle!;
    public PlaylistGroupCounts Counts => PlaylistGroupCounts.Build(Children);
}

internal sealed record PlaylistGroupCounts(int Completed, int Active, int Queued, int Failed, int Interrupted, int Cancelled)
{
    public int Total => Completed + Active + Queued + Failed + Interrupted + Cancelled;
    public static PlaylistGroupCounts Build(IEnumerable<DownloadQueueItem> items)
    {
        int completed = 0, active = 0, queued = 0, failed = 0, interrupted = 0, cancelled = 0;
        foreach (var item in items)
            switch (item.Status)
            {
                case DownloadStatus.Completed: completed++; break;
                case DownloadStatus.DownloadingVideo: case DownloadStatus.DownloadingAudio:
                case DownloadStatus.Merging: case DownloadStatus.Finalizing:
                case DownloadStatus.RecordingLive: case DownloadStatus.ReconnectingLive: active++; break;
                case DownloadStatus.Queued: case DownloadStatus.Waiting: case DownloadStatus.WaitingForLive: queued++; break;
                case DownloadStatus.Failed: failed++; break;
                case DownloadStatus.Interrupted: case DownloadStatus.Partial: interrupted++; break;
                case DownloadStatus.Cancelled: cancelled++; break;
                default: throw new ArgumentOutOfRangeException(nameof(item.Status));
            }
        return new(completed, active, queued, failed, interrupted, cancelled);
    }
}

internal sealed record PlaylistQueueRow(PlaylistGroup? Group, DownloadQueueItem? Item);

internal sealed class PlaylistQueueProjection
{
    public const int PageSize = 40;
    public IReadOnlyList<IReadOnlyList<PlaylistQueueRow>> Pages { get; }
    public IReadOnlyDictionary<PlaylistGroupKey, PlaylistGroup> Groups { get; }
    private readonly Dictionary<Guid, int> pagesByJob;
    private PlaylistQueueProjection(List<IReadOnlyList<PlaylistQueueRow>> pages,
        Dictionary<PlaylistGroupKey, PlaylistGroup> groups, Dictionary<Guid, int> pagesByJob)
        => (Pages, Groups, this.pagesByJob) = (pages, groups, pagesByJob);
    public int PageOf(Guid id) => pagesByJob.GetValueOrDefault(id, 0);

    public static PlaylistQueueProjection Build(IReadOnlyList<DownloadQueueItem> snapshot, bool grouping,
        IReadOnlySet<PlaylistGroupKey> expanded)
    {
        var groups = new Dictionary<PlaylistGroupKey, PlaylistGroup>();
        var members = new Dictionary<PlaylistGroupKey, List<DownloadQueueItem>>();
        var keys = new Dictionary<Guid, PlaylistGroupKey>();
        var unique = new HashSet<Guid>();
        var items = snapshot.Where(item => unique.Add(item.Id)).ToArray();
        if (grouping)
            foreach (var item in items)
                if (PlaylistGroupKey.From(item) is { } key)
                {
                    keys.Add(item.Id, key);
                    if (!members.TryGetValue(key, out var children)) members.Add(key, children = []);
                    children.Add(item);
                }
        foreach (var (key, children) in members)
            if (children.Count > 1) groups.Add(key, new(key, children));

        var pages = new List<IReadOnlyList<PlaylistQueueRow>>();
        var current = new List<PlaylistQueueRow>(); pages.Add(current);
        var pagesByJob = new Dictionary<Guid, int>();
        var emitted = new HashSet<PlaylistGroupKey>();
        void NewPage() { current = []; pages.Add(current); }
        foreach (var item in items)
        {
            if (keys.TryGetValue(item.Id, out var key) && groups.TryGetValue(key, out var group))
            {
                if (!emitted.Add(key)) continue;
                var open = expanded.Contains(key);
                if (current.Count >= PageSize - (open ? 1 : 0)) NewPage();
                current.Add(new(group, null));
                foreach (var child in group.Children)
                {
                    if (open && current.Count == PageSize) { NewPage(); current.Add(new(group, null)); }
                    pagesByJob.Add(child.Id, pages.Count - 1);
                    if (open) current.Add(new(group, child));
                }
            }
            else
            {
                if (current.Count == PageSize) NewPage();
                pagesByJob.Add(item.Id, pages.Count - 1);
                current.Add(new(null, item));
            }
        }
        return new(pages, groups, pagesByJob);
    }
}
