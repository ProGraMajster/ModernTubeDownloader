using System.Globalization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

internal enum LiveDashboardSort { Newest, Oldest, Title }

/// <summary>A bounded presentation of the canonical LIVE snapshot; never owns recording intents.</summary>
internal sealed record LiveDashboardPage(LiveSession[] Items, int Index, int Total, int ActiveCount, int RecentCount)
{
    internal const int PageSize = 40;
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
    internal static bool IsCurrent(LiveSession session) => session.IsActive || session.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive;

    internal static LiveDashboardPage Create(IReadOnlyList<LiveSession> snapshot, bool recent, SourceBrand? source,
        string query, LiveDashboardSort sort, int page, CultureInfo culture)
    {
        query = query.Trim();
        var matches = snapshot.Where(item =>
            (source is null || SourceBrandLookup.Resolve(item.SourceUrl).Brand == source) &&
            (query.Length == 0 || new[] { item.Title, item.Channel, item.SourceUrl }.Any(value =>
                culture.CompareInfo.IndexOf(value, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))).ToArray();
        var active = matches.Count(IsCurrent);
        var filtered = matches.Where(item => IsCurrent(item) != recent);
        var ordered = sort switch
        {
            LiveDashboardSort.Oldest => filtered.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id),
            LiveDashboardSort.Title => filtered.OrderBy(item => item.Title, StringComparer.Create(culture, true)).ThenBy(item => item.Id),
            _ => filtered.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
        };
        var total = recent ? matches.Length - active : active;
        page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / PageSize));
        return new(ordered.Skip(page * PageSize).Take(PageSize).ToArray(), page, total, active, matches.Length - active);
    }
}
