using System.Globalization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class LiveDashboardPageTests
{
    [Fact]
    public void TenThousandSessionsArePartitionedOnceWithoutMutatingIdentitiesOrOrder()
    {
        var sessions = Enumerable.Range(0, 10000).Select(i => new LiveSession
        {
            Title = "Session " + i, SourceUrl = "https://example.test/" + i,
            CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(i), State = (LiveSessionState)(i % 11)
        }).ToArray();
        var original = sessions.Select(s => (s.Id, s.State, s.CreatedAt)).ToArray();
        var seen = new HashSet<Guid>();
        foreach (var recent in new[] { false, true })
        {
            var first = LiveDashboardPage.Create(sessions, recent, null, "", LiveDashboardSort.Newest, 0, CultureInfo.InvariantCulture);
            for (var page = 0; page < first.PageCount; page++)
            {
                var result = LiveDashboardPage.Create(sessions, recent, null, "", LiveDashboardSort.Newest, page, CultureInfo.InvariantCulture);
                Assert.InRange(result.Items.Length, 1, LiveDashboardPage.PageSize);
                Assert.All(result.Items, item => Assert.True(seen.Add(item.Id)));
                Assert.All(result.Items, item => Assert.Equal(!recent, LiveDashboardPage.IsCurrent(item)));
            }
        }
        Assert.Equal(10000, seen.Count);
        Assert.Equal(original, sessions.Select(s => (s.Id, s.State, s.CreatedAt)).ToArray());
    }

    [Fact]
    public void SearchSourceCountsStableSortAndPageClampingUseTheSameProjection()
    {
        var a = new LiveSession { Title = "Zürich", Channel = "Alpy", SourceUrl = "https://www.youtube.com/live/a", State = LiveSessionState.WaitingForLive, CreatedAt = DateTimeOffset.UnixEpoch };
        var b = new LiveSession { Title = "Zürich", Channel = "Alpy", SourceUrl = "https://youtube.com/live/b", State = LiveSessionState.Interrupted, CreatedAt = DateTimeOffset.UnixEpoch.AddHours(1) };
        var c = new LiveSession { Title = "Zürich", SourceUrl = "https://youtube.com.evil.test/live", State = LiveSessionState.Recording };
        var result = LiveDashboardPage.Create([a,b,c], true, SourceBrand.YouTube, "zurich", LiveDashboardSort.Title, 999, CultureInfo.GetCultureInfo("pl"));
        Assert.Equal(0, result.Index); Assert.Equal(1, result.PageCount);
        Assert.Equal(1, result.ActiveCount); Assert.Equal(1, result.RecentCount); Assert.Same(b, Assert.Single(result.Items));
        var empty = LiveDashboardPage.Create([a,b,c], false, SourceBrand.Twitch, "", LiveDashboardSort.Oldest, -1, CultureInfo.InvariantCulture);
        Assert.Empty(empty.Items); Assert.Equal(0, empty.Total); Assert.Equal(0, empty.Index);
        var oldest = LiveDashboardPage.Create([a,c], false, null, "", LiveDashboardSort.Oldest, 0, CultureInfo.InvariantCulture);
        Assert.Same(a, oldest.Items[0]);
    }
}
