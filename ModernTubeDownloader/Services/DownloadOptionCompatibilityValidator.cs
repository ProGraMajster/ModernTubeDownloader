using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public enum DownloadOptionIssue { None, RangeConflict, RemoveSubtitleConflict, MarkRequiresMkv }

public sealed class DownloadOptionException(DownloadOptionIssue issue) : NotSupportedException(issue.ToString())
{
    public DownloadOptionIssue Issue { get; } = issue;
}

public static class DownloadOptionCompatibilityValidator
{
    public static DownloadOptionIssue Check(MediaTimeRange? range, SubtitleOptions? subtitles,
        SponsorBlockOptions? sponsorBlock, PreferredVideoContainer container)
    {
        if (range?.Mode == MediaRangeMode.Custom &&
            (subtitles?.Enabled == true || sponsorBlock?.Mode != SponsorBlockMode.Off && sponsorBlock is not null))
            return DownloadOptionIssue.RangeConflict;
        if (subtitles?.Enabled == true && sponsorBlock?.Mode == SponsorBlockMode.Remove)
            return DownloadOptionIssue.RemoveSubtitleConflict;
        // Only explicit MKV Mark has been verified with a correct chapter timeline.
        // Auto is intentionally not silently changed to another container.
        if (sponsorBlock?.Mode == SponsorBlockMode.Mark && container != PreferredVideoContainer.Mkv)
            return DownloadOptionIssue.MarkRequiresMkv;
        return DownloadOptionIssue.None;
    }

    public static void EnsureSupported(MediaTimeRange? range, SubtitleOptions? subtitles,
        SponsorBlockOptions? sponsorBlock, PreferredVideoContainer container)
    {
        var issue = Check(range, subtitles, sponsorBlock, container);
        if (issue != DownloadOptionIssue.None) throw new DownloadOptionException(issue);
    }

    public static string MessageKey(DownloadOptionIssue issue) => issue switch
    {
        DownloadOptionIssue.RangeConflict => "Advanced.RangeConflict",
        DownloadOptionIssue.RemoveSubtitleConflict => "Advanced.RemoveSubtitleConflict",
        DownloadOptionIssue.MarkRequiresMkv => "Advanced.MarkRequiresMkv",
        _ => string.Empty
    };

    public static string WebRemoteCode(DownloadOptionIssue issue) => issue switch
    {
        DownloadOptionIssue.MarkRequiresMkv => "sponsorblock-mark-requires-mkv",
        _ => "incompatible-options"
    };

    public static string? WarningKey(SponsorBlockOptions? sponsorBlock) =>
        sponsorBlock?.Mode == SponsorBlockMode.Remove ? "SponsorBlock.RemoveExperimentalWarning" : null;
}
