using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

public sealed class PlaylistQueueProjectionTests
{
    private static DownloadQueueItem Job(string? playlist = "p", string host = "youtube.com", int index = 1) => new()
    { SourceUrl = $"https://{host}/watch?v={Guid.NewGuid():N}", PlaylistId = playlist, PlaylistTitle = playlist is null ? null : "Same title 日本語 🎬", PlaylistIndex = playlist is null ? null : index };
    [Fact]
    public void EmptyAndSingletonsHaveNoInventedGroups()
    {
        Assert.Empty(PlaylistQueueProjection.Build([], true, new HashSet<PlaylistGroupKey>()).Pages.Single());
        var items = new[] { Job(null), Job(), Job("other") };
        var p = PlaylistQueueProjection.Build(items, true, new HashSet<PlaylistGroupKey>());
        Assert.Empty(p.Groups); Assert.Equal(items, p.Pages.Single().Select(r => r.Item));
    }
    [Fact]
    public void InterleavedGroupsUseFirstCanonicalOccurrenceWithoutReorderingRecords()
    {
        var items = new[] { Job("a"), Job(null), Job("b"), Job("a", index: 2), Job(null), Job("b", index: 2) };
        var ids = items.Select(i => i.Id).ToArray();
        var open = new HashSet<PlaylistGroupKey>(items.Select(PlaylistGroupKey.From).OfType<PlaylistGroupKey>());
        var p = PlaylistQueueProjection.Build(items, true, open);
        Assert.Equal(new[] { items[0], items[3], items[1], items[2], items[5], items[4] }, p.Pages.Single().Where(r => r.Item is not null).Select(r => r.Item));
        Assert.Equal(ids, items.Select(i => i.Id));
        Assert.Equal(2, p.Groups.Count);
    }
    [Fact]
    public void AuthoritySeparatesSamePlaylistIdAndInvalidProvenanceStaysSingle()
    {
        var items = new[] { Job(), Job(host: "www.youtube.com"), Job(host: "example.test"), Job(host: "example.test"), Job(null), Job(index: 0), Job() };
        items[^1].SourceUrl = "file:///C:/playlist";
        var p = PlaylistQueueProjection.Build(items, true, new HashSet<PlaylistGroupKey>());
        Assert.Equal(2, p.Groups.Count); Assert.Equal(3, p.Pages.Single().Count(r => r.Item is not null));
        Assert.All(p.Groups.Values, g => Assert.Equal(2, g.Children.Count));
    }
    [Theory]
    [InlineData("https://youtube.com.evil.test/watch", "youtube.com.evil.test")]
    [InlineData("https://youtube.com:8443/watch", "youtube.com:8443")]
    [InlineData("https://youtu.be/id", "youtube.com")]
    public void SourceIdentityUsesParsedAuthority(string url, string expected)
    { var item = Job(); item.SourceUrl = url; Assert.Equal(expected, PlaylistGroupKey.From(item)!.Authority); }
    [Fact]
    public void MissingAndCredentialedIdentityCannotGroup()
    {
        var item = Job(); item.SourceUrl = "https://user@youtube.com/watch"; Assert.Null(PlaylistGroupKey.From(item));
        item = Job(); item.PlaylistTitle = ""; Assert.Null(PlaylistGroupKey.From(item));
    }
    [Fact]
    public void ExhaustiveStatusBucketsCountEveryCanonicalStateExactlyOnce()
    {
        var jobs = Enum.GetValues<DownloadStatus>().Select(status => { var item = Job(); item.Status = status; return item; }).ToArray();
        var counts = PlaylistGroupCounts.Build(jobs);
        Assert.Equal(jobs.Length, counts.Total);
        Assert.Equal(new PlaylistGroupCounts(1, 6, 3, 1, 2, 1), counts);
    }
    [Theory]
    [InlineData(120)]
    [InlineData(501)]
    [InlineData(1300)]
    public void ExpandedLargeGroupPagesBoundAllRowsAndLocateExactJob(int count)
    {
        var jobs = Enumerable.Range(1, count).Select(i => Job(index: i)).ToArray();
        var key = PlaylistGroupKey.From(jobs[0])!;
        var p = PlaylistQueueProjection.Build(jobs, true, new HashSet<PlaylistGroupKey> { key });
        Assert.All(p.Pages, page => { Assert.InRange(page.Count, 1, 40); Assert.Null(page[0].Item); Assert.Equal(key, page[0].Group!.Key); });
        Assert.Equal(jobs, p.Pages.SelectMany(page => page).Where(r => r.Item is not null).Select(r => r.Item));
        foreach (var job in jobs) Assert.Contains(p.Pages[p.PageOf(job.Id)], r => r.Item?.Id == job.Id);
        var collapsed = PlaylistQueueProjection.Build(jobs, true, new HashSet<PlaylistGroupKey>());
        Assert.Single(collapsed.Pages.Single()); Assert.Equal(0, collapsed.PageOf(jobs[^1].Id));
    }
    [Fact]
    public void ManySmallGroupsCannotAllocateFortyChildrenPerGroup()
    {
        var jobs = Enumerable.Range(0, 650).SelectMany(i => new[] { Job($"p{i}"), Job($"p{i}", index: 2) }).ToArray();
        var keys = jobs.Select(PlaylistGroupKey.From).OfType<PlaylistGroupKey>().ToHashSet();
        var p = PlaylistQueueProjection.Build(jobs, true, keys);
        Assert.All(p.Pages, page => Assert.InRange(page.Count, 1, 40));
        Assert.Equal(1300, p.Pages.Sum(page => page.Count(r => r.Item is not null)));
    }
    [Fact]
    public void DuplicateAndRemovedChildrenDoNotProduceGhostGroups()
    {
        var a = Job(); var b = Job(index: 2);
        Assert.Equal(2, PlaylistQueueProjection.Build([a, a, b], true, new HashSet<PlaylistGroupKey>()).Groups.Single().Value.Children.Count);
        Assert.Empty(PlaylistQueueProjection.Build([b], true, new HashSet<PlaylistGroupKey>()).Groups);
        Assert.Empty(PlaylistQueueProjection.Build([], true, new HashSet<PlaylistGroupKey>()).Groups);
    }
    [Fact]
    public void DisabledGroupingRestoresCanonicalFlatPagesAndPreferenceRoundTrips()
    {
        var jobs = Enumerable.Range(0, 83).Select(_ => Job()).ToArray();
        var p = PlaylistQueueProjection.Build(jobs, false, new HashSet<PlaylistGroupKey>());
        Assert.Equal(jobs, p.Pages.SelectMany(page => page).Select(r => r.Item)); Assert.Equal(3, p.Pages.Count);
        Assert.True(AppearanceProfileStore.Validate("{\"schemaVersion\":1}").ModernDashboardShell.GroupPlaylists);
        var profile = new AppearanceProfile(); profile.ModernDashboardShell.GroupPlaylists = false;
        Assert.False(profile.Copy().ModernDashboardShell.GroupPlaylists);
    }
}
