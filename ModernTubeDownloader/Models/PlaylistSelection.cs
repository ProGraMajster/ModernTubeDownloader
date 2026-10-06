namespace ModernTubeDownloader.Models;

public sealed class PlaylistSelection
{
    private readonly bool[] selected;

    public PlaylistSelection(PlaylistMetadata playlist)
    {
        Playlist = playlist;
        selected = playlist.Entries.Select(entry => entry.IsAvailable).ToArray();
    }

    public PlaylistMetadata Playlist { get; }
    public int SelectedCount => selected.Count(value => value);
    public bool IsSelected(int index) => selected[index];

    public void Set(int index, bool value)
    {
        if (index < 0 || index >= selected.Length) throw new ArgumentOutOfRangeException(nameof(index));
        selected[index] = value && Playlist.Entries[index].IsAvailable;
    }

    public void SelectAll()
    {
        for (var index = 0; index < selected.Length; index++)
            selected[index] = Playlist.Entries[index].IsAvailable;
    }

    public void Clear() => Array.Fill(selected, false);

    public IReadOnlyList<DownloadQueueItem> CreateQueueItems(
        QualityPreset preset,
        PreferredVideoContainer preferredVideoContainer = PreferredVideoContainer.Auto,
        MediaTimeRange? requestedRange = null,
        SubtitleOptions? subtitles = null,
        SponsorBlockOptions? sponsorBlock = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        requestedRange ??= MediaTimeRange.Full;
        var selectedEntries = Playlist.Entries.Select((entry, ordinal) => (entry, ordinal))
            .Where(pair => selected[pair.ordinal] && pair.entry.IsAvailable).ToArray();
        if (selectedEntries.Any(pair => requestedRange.Validate(pair.entry.DurationSeconds) is not null))
            throw new ArgumentException("The requested range exceeds at least one selected playlist entry.", nameof(requestedRange));
        return selectedEntries
            .OrderBy(pair => pair.entry.PlaylistIndex)
            .ThenBy(pair => pair.ordinal)
            .Select(pair => new DownloadQueueItem
            {
                VideoId = string.IsNullOrWhiteSpace(pair.entry.Id) ? pair.entry.SourceUrl : pair.entry.Id,
                Title = pair.entry.Title,
                SourceUrl = pair.entry.SourceUrl,
                Channel = pair.entry.Channel ?? Playlist.Channel ?? string.Empty,
                DurationSeconds = pair.entry.DurationSeconds,
                QualityPresetId = preset.Id,
                PreferredVideoContainer = preferredVideoContainer,
                RequestedRange = requestedRange,
                SubtitleOptions = subtitles?.Copy() ?? new(),
                SponsorBlockOptions = sponsorBlock?.Copy() ?? new(),
                PlaylistId = string.IsNullOrWhiteSpace(Playlist.Id) ? Playlist.SourceUrl : Playlist.Id,
                PlaylistTitle = Playlist.Title,
                PlaylistIndex = pair.entry.PlaylistIndex
                // Flat playlist entries have no formats. The existing FormatSelector runs
                // after each entry is fully analyzed by the ordinary queue worker.
            }).ToArray();
    }
}
