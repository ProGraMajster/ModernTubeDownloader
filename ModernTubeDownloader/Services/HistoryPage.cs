using System.Globalization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

// Projection of the canonical history snapshot. Filtering always precedes paging.
internal static class HistoryPage
{
    internal const int Size = 40;
    internal static DownloadHistoryEntry[] Filter(IReadOnlyList<DownloadHistoryEntry> entries, string query,
        CultureInfo culture, CancellationToken cancellationToken)
    {
        var result = new List<DownloadHistoryEntry>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.Length == 0 || Contains(entry.Title) || Contains(entry.Channel) || Contains(entry.Quality) || Contains(entry.FinalPath))
                result.Add(entry);
        }
        return result.ToArray();
        bool Contains(string value) => culture.CompareInfo.IndexOf(value, query, CompareOptions.IgnoreCase) >= 0;
    }
    internal static int Count(int records) => Math.Max(1, (records + Size - 1) / Size);
    internal static int Clamp(int page, int records) => Math.Clamp(page, 0, Count(records) - 1);
}
