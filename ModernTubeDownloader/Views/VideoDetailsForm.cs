using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class VideoDetailsForm : Form
{
    private readonly VideoMetadata metadata;
    private readonly LocalizationService text;
    private readonly TabControl tabs;

    public VideoDetailsForm(VideoMetadata metadata, LocalizationService text)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(text);
        this.metadata = metadata;
        this.text = text;
        AppBranding.Apply(this);
        Size = new System.Drawing.Size(980, 740);
        MinimumSize = new System.Drawing.Size(760, 540);
        tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Padding(12) };
        AppUi.Surface(tabs);
        Controls.Add(tabs);
        text.LanguageChanged += LanguageChanged;
        BuildTabs();
    }

    private void BuildTabs()
    {
        Text = text.Get("Details.WindowTitle", DisplayValue(metadata.Title, text["Details.Untitled"]));
        tabs.TabPages.Clear();
        AddTextTab(text["Details.Overview"], BuildOverview());
        AddTextTab(text["Details.Metadata"], BuildMetadata());
        AddTextTab(text["Details.Formats"], BuildFormats());
        AddTextTab(text["Details.Subtitles"], BuildSubtitles());
        AddTextTab(text["Details.Chapters"], BuildChapters());
    }

    private void AddTextTab(string title, string value)
    {
        var page = tabs.TabPages.Add(title);
        AppUi.Surface(page);
        var content = new TextBox
        {
            Dock = DockStyle.Fill,
            MultiLine = true,
            ReadOnly = true,
            Text = value,
            Padding = new Padding(16),
            TextAlign = ContentAlignment.TopLeft,
            ScrollBars = ScrollBars.Vertical
        };
        AppUi.Input(content);
        page.Controls.Add(content);
    }

    private string BuildOverview() => string.Join(Environment.NewLine,
    [
        Row("Details.Title", metadata.Title),
        Row("Details.VideoId", metadata.Id),
        Row("Details.Url", metadata.WebpageUrl),
        Row("Details.Channel", metadata.DisplayChannel),
        Row("Details.ChannelId", metadata.ChannelId),
        Row("Details.Uploader", metadata.Uploader),
        Row("Details.Duration", metadata.DurationText ?? FormatDuration(metadata.DurationSeconds)),
        Row("Details.UploadDate", metadata.UploadDate),
        Row("Details.ReleaseDate", metadata.ReleaseDate),
        Row("Details.Views", metadata.ViewCount?.ToString("N0", text.Culture)),
        Row("Details.Likes", metadata.LikeCount?.ToString("N0", text.Culture)),
        Row("Details.Comments", metadata.CommentCount?.ToString("N0", text.Culture)),
        Row("Details.Language", metadata.Language),
        Row("Details.Availability", metadata.Availability),
        Row("Details.AgeLimit", metadata.AgeLimit?.ToString(text.Culture)),
        Row("Details.LiveStatus", metadata.LiveStatus),
        Row("Details.Extractor", $"{DisplayValue(metadata.Extractor)} ({DisplayValue(metadata.ExtractorKey)})"),
        string.Empty,
        text["Details.Description"],
        "-----------",
        DisplayValue(metadata.Description, text["Details.NoDescription"])
    ]);

    private string Row(string key, string? value)
        => $"{text[key]}: {DisplayValue(value)}";

    private string BuildFormats()
    {
        var formats = metadata.Formats.OfType<MediaFormat>().ToArray();
        return formats.Length == 0
            ? text["Details.None"]
            : string.Join(Environment.NewLine, formats.Select(format =>
                $"{format.Id,-8} {format.Extension,-5} {format.Resolution ?? (format.Height is { } height ? $"{height}p" : "audio"),-12} " +
                $"fps={format.Fps?.ToString("0.##", text.Culture) ?? "—"} v={format.VideoCodec ?? "—"} a={format.AudioCodec ?? "—"} " +
                $"tbr={format.TotalBitrateKbps?.ToString("0.#", text.Culture) ?? "—"} size={FormatSize(format.FileSize ?? format.ApproximateFileSize)} " +
                $"dr={format.DynamicRange ?? "—"} protocol={format.Protocol ?? "—"} note={format.Note ?? "—"}"));
    }

    private string BuildMetadata() => string.Join(Environment.NewLine,
    [
        Row("Details.MediaUrl", metadata.MediaUrl),
        Row("Details.OriginalUrl", metadata.OriginalUrl),
        Row("Details.UploaderId", metadata.UploaderId),
        Row("Details.UploaderUrl", metadata.UploaderUrl),
        Row("Details.ChannelUrl", metadata.ChannelUrl),
        Row("Details.Timestamp", metadata.Timestamp?.ToString(text.Culture)),
        Row("Details.ReleaseTimestamp", metadata.ReleaseTimestamp?.ToString(text.Culture)),
        Row("Details.Tags", JoinValues(metadata.Tags)),
        Row("Details.Categories", JoinValues(metadata.Categories)),
        Row("Details.Thumbnails", metadata.Thumbnails.Count.ToString("N0", text.Culture)),
        Row("Details.FormatCount", metadata.Formats.Count.ToString("N0", text.Culture)),
        Row("Details.SubtitleLanguages", JoinValues(metadata.Subtitles.Keys)),
        Row("Details.AutomaticCaptionLanguages", JoinValues(metadata.AutomaticCaptions.Keys)),
        Row("Details.ChapterCount", metadata.Chapters.Count.ToString("N0", text.Culture))
    ]);

    private string BuildSubtitles()
    {
        var manual = metadata.Subtitles.Count == 0 ? text["Details.None"] : string.Join(", ", metadata.Subtitles.Keys.Order());
        var automatic = metadata.AutomaticCaptions.Count == 0 ? text["Details.None"] : string.Join(", ", metadata.AutomaticCaptions.Keys.Order());
        return $"{text["Details.Subtitles"]}: {manual}{Environment.NewLine}{Environment.NewLine}{text["Details.AutomaticCaptions"]}: {automatic}";
    }

    private string BuildChapters()
    {
        var chapters = metadata.Chapters.OfType<ChapterInfo>().ToArray();
        return chapters.Length == 0
            ? text["Details.NoChapters"]
            : string.Join(Environment.NewLine, chapters.Select((chapter, index) =>
                $"{index + 1,2}. {FormatChapterTime(chapter.StartTime)} — {DisplayValue(chapter.Title, text["Details.Untitled"])}"));
    }

    private static string FormatChapterTime(double? seconds)
    {
        var value = seconds ?? 0;
        if (!double.IsFinite(value) || value < 0 || value > TimeSpan.MaxValue.TotalSeconds)
            value = 0;
        return TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss");
    }

    private static string? FormatDuration(double? seconds)
    {
        if (seconds is not { } value || !double.IsFinite(value) || value < 0 || value > TimeSpan.MaxValue.TotalSeconds)
            return null;
        return TimeSpan.FromSeconds(value).ToString();
    }

    private string FormatSize(long? value)
        => value is null ? text["Common.NotAvailable"] : $"{value.Value / 1024d / 1024d:F1} MiB";

    private string? JoinValues(IEnumerable<string?> values)
    {
        var available = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        return available.Length == 0 ? null : string.Join(", ", available);
    }

    private string DisplayValue(string? value, string? fallback = null)
        => string.IsNullOrWhiteSpace(value)
            ? fallback ?? text["Common.NotAvailable"]
            : value;

    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(BuildTabs);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            text.LanguageChanged -= LanguageChanged;
        base.Dispose(disposing);
    }
}
