using System.Globalization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

// Projection of the canonical history snapshot. Filtering always precedes paging.
internal static class HistoryPage
{
    internal const int Size = 40;
    private static readonly string[] AudioExtensions = [".mp3", ".m4a", ".aac", ".flac", ".ogg", ".wav", ".opus"];
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".mov", ".avi"];
    internal static DownloadHistoryEntry[] Filter(IReadOnlyList<DownloadHistoryEntry> entries, string query,
        CultureInfo culture, CancellationToken cancellationToken)
    {
        var result = new List<DownloadHistoryEntry>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MatchesSearch(entry, query, culture.CompareInfo)) result.Add(entry);
        }
        return result.ToArray();
    }

    internal static (IReadOnlyList<DownloadHistoryEntry> Matches, Dictionary<string, int> Counts) Project(
        IReadOnlyList<DownloadHistoryEntry> entries, string query, CultureInfo culture, string? filter,
        DateTimeOffset cutoff, bool ascending, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (filter is null) return (query.Length == 0 ? entries : Filter(entries, query, culture, token), new());

        var selected = filter switch
        {
            "Completed" => Category.Completed, "Errors" => Category.Error, "Audio" => Category.Audio,
            "Video" => Category.Video, "Playlists" => Category.Playlist, "Live" => Category.Live,
            _ => Category.All
        };
        var unchanged = selected == Category.All && cutoff == DateTimeOffset.MinValue && query.Length == 0 && !ascending;
        var matches = unchanged ? null : new List<DownloadHistoryEntry>();
        var comparison = culture.CompareInfo;
        int all = 0, completed = 0, audio = 0, video = 0, playlist = 0, live = 0;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.CompletedAt < cutoff) continue;
            var category = Classify(entry);
            all++;
            if ((category & Category.Completed) != 0) completed++;
            if ((category & Category.Audio) != 0) audio++;
            if ((category & Category.Video) != 0) video++;
            if ((category & Category.Playlist) != 0) playlist++;
            if ((category & Category.Live) != 0) live++;
            if (matches is not null && (category & selected) != 0 && MatchesSearch(entry, query, comparison))
                matches.Add(entry);
        }
        if (ascending) matches!.Reverse();
        return (matches ?? entries, new()
        {
            ["All"] = all, ["Completed"] = completed, ["Errors"] = all - completed,
            ["Audio"] = audio, ["Video"] = video, ["Playlists"] = playlist, ["Live"] = live
        });
    }

    [Flags]
    private enum Category { All = 1, Completed = 2, Error = 4, Audio = 8, Video = 16, Playlist = 32, Live = 64 }

    private static Category Classify(DownloadHistoryEntry entry)
    {
        var category = Category.All | (!entry.WasPartial && entry.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            ? Category.Completed : Category.Error);
        var extension = Path.GetExtension(entry.FinalPath.AsSpan());
        foreach (var value in AudioExtensions)
            if (extension.Equals(value, StringComparison.OrdinalIgnoreCase)) { category |= Category.Audio; break; }
        foreach (var value in VideoExtensions)
            if (extension.Equals(value, StringComparison.OrdinalIgnoreCase)) { category |= Category.Video; break; }
        if (!string.IsNullOrEmpty(entry.PlaylistId) || !string.IsNullOrEmpty(entry.PlaylistTitle)) category |= Category.Playlist;
        if (entry.WasLiveRecording) category |= Category.Live;
        return category;
    }

    private static bool MatchesSearch(DownloadHistoryEntry entry, string query, CompareInfo comparison) =>
        query.Length == 0 ||
        comparison.IndexOf(entry.Title, query, CompareOptions.IgnoreCase) >= 0 ||
        comparison.IndexOf(entry.Channel, query, CompareOptions.IgnoreCase) >= 0 ||
        comparison.IndexOf(entry.Quality, query, CompareOptions.IgnoreCase) >= 0 ||
        comparison.IndexOf(entry.FinalPath, query, CompareOptions.IgnoreCase) >= 0;

    internal static int Count(int records) => Math.Max(1, (records + Size - 1) / Size);
    internal static int Clamp(int page, int records) => Math.Clamp(page, 0, Count(records) - 1);
}
