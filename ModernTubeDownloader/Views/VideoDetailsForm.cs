using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Views;

internal sealed class VideoDetailsForm : Form
{
    private readonly VideoMetadata metadata;
    private readonly LocalizationService text;
    private readonly TabControl tabs;
    private readonly ThumbnailCacheService? thumbnails;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private readonly IDisposable typographyLayout;

    public VideoDetailsForm(VideoMetadata metadata, LocalizationService text, ThumbnailCacheService? thumbnails = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(text);
        this.metadata = metadata;
        this.text = text;
        this.thumbnails = thumbnails;
        AccessibilityObject.AutomationId = "DetailsWindow";
        AppBranding.Apply(this);
        Size = new System.Drawing.Size(980, 740);
        MinimumSize = new System.Drawing.Size(760, 540);
        tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Padding(12), AccessibleAutomationId = "DetailsTabs" };
        AppUi.Surface(tabs);
        Controls.Add(tabs);
        text.LanguageChanged += LanguageChanged;
        BuildTabs();
        typographyLayout = AppTypography.RegisterLayout(BuildTabs);
    }

    private void BuildTabs()
    {
        Text = text.Get("Details.WindowTitle", DisplayValue(metadata.Title, text["Details.Untitled"]));
        var selected = tabs.SelectedIndex;
        foreach (var oldPage in tabs.TabPages.ToArray()) oldPage.Dispose();
        tabs.TabPages.Clear();
        tabs.TabSpacing = AppearancePresentation.Current.Details.Tabs == DetailsTabStyle.Compact ? 0 : 6;
        tabs.TabMinWidth = AppearancePresentation.Current.Details.Tabs == DetailsTabStyle.Compact ? 64 : 100;
        AddTextTab(text["Details.Overview"], "DetailsOverviewContent", BuildOverview());
        AddTextTab(text["Sources.Source"], "DetailsSourceContent", SourcePresentation.Details(metadata, text));
        AddTextTab(text["Details.Metadata"], "DetailsMetadataContent", BuildMetadata());
        AddTextTab(text["Details.Formats"], "DetailsFormatsContent", BuildFormats());
        AddTextTab(text["Details.Subtitles"], "DetailsSubtitlesContent", BuildSubtitles());
        AddTextTab(text["Details.Chapters"], "DetailsChaptersContent", BuildChapters());
        // These are MFN's existing logical TabItem peers, not a parallel automation tree.
        var ids = new[] { "DetailsOverviewTab", "DetailsSourceTab", "DetailsMetadataTab", "DetailsFormatsTab", "DetailsSubtitlesTab", "DetailsChaptersTab" };
        var accessible = tabs.AccessibilityObject;
        var index = 0;
        for (var child = 0; child < accessible.GetChildCount() && index < ids.Length; child++)
        {
            if (accessible.GetChild(child) is { ControlType: AccessibleControlType.TabItem } peer)
                peer.AutomationId = ids[index++];
        }
        if (selected >= 0 && selected < tabs.TabPages.Count) tabs.SelectedIndex = selected;
    }

    private void AddTextTab(string title, string automationId, string value)
    {
        var page = tabs.TabPages.Add(title);
        page.AccessibleAutomationId = automationId;
        AppUi.Surface(page);
        var content = new TextBox
        {
            Dock = DockStyle.Fill,
            MultiLine = true,
            ReadOnly = true,
            Text = value,
            Padding = new Padding(AppearancePresentation.Current.Details.MetadataDensity switch { AppearanceDensity.Compact => 8, AppearanceDensity.Comfortable => 24, _ => 16 }),
            TextAlign = ContentAlignment.TopLeft,
            ScrollBars = ScrollBars.Vertical
        };
        content.AccessibleAutomationId = automationId + "Text";
        AppUi.Input(content);
        page.Controls.Add(content);
        if (automationId == "DetailsMetadataContent" && AppearancePresentation.Current.Details.CollapseTechnical)
        {
            var show = new Button { Text = text["Appearance.ShowTechnical"], Dock = DockStyle.Top, Height = AppTypography.TextHeight(40), AccessibleAutomationId = "DetailsShowTechnical" }; AppUi.Secondary(show);
            content.Visible = false; show.Click += (_, _) => { content.Visible = true; show.Visible = false; }; page.Controls.Add(show);
        }
        if (automationId == "DetailsOverviewContent" && thumbnails is not null && AppearancePresentation.Current.Details.ThumbnailSize != AppearanceSize.None)
        {
            var image = new PictureBox { Dock = DockStyle.Top, Height = AppearancePresentation.ImageWidth(AppearancePresentation.Current.Details.ThumbnailSize, 160) * 9 / 16, SizeMode = PictureBoxSizeMode.Zoom };
            page.Controls.Add(image); _ = LoadImageAsync(image);
        }
    }
    private async Task LoadImageAsync(PictureBox image)
    {
        try { var bitmap = await thumbnails!.GetAsync(metadata.ThumbnailUrl, lifetime.Token); if (!disposed && image.Parent is not null) image.Image = bitmap; }
        catch (OperationCanceledException) { }
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
        if (disposing && !disposed)
        {
            disposed = true; typographyLayout.Dispose(); lifetime.Cancel(); lifetime.Dispose();
            text.LanguageChanged -= LanguageChanged;
        }
        base.Dispose(disposing);
    }
}
