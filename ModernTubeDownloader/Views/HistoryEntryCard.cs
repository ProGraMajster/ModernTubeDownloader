using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

internal sealed class HistoryEntryCard : UserControl
{
    private readonly DownloadHistoryEntry entry;
    private readonly LocalizationService text;
    private readonly ThumbnailCacheService thumbnails;
    private readonly Panel placeholder;
    private readonly Label placeholderText;
    private readonly PictureBox thumbnail;
    private readonly Label title;
    private readonly Label facts;
    private readonly Label path;
    private readonly Button openFile;
    private readonly Button openFolder;

    public HistoryEntryCard(DownloadHistoryEntry entry, LocalizationService text, ThumbnailCacheService thumbnails)
    {
        this.entry = entry;
        this.text = text;
        this.thumbnails = thumbnails;
        Height = 132;
        Margin = new Padding(0, 0, 0, 14);
        AppUi.Card(this);

        placeholder = new Panel();
        AppUi.Card(placeholder, secondary: true);
        placeholderText = AppUi.Heading("MTD", 13f);
        placeholderText.Dock = DockStyle.Fill;
        placeholderText.TextAlign = ContentAlignment.MiddleCenter;
        placeholder.Controls.Add(placeholderText);
        thumbnail = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        thumbnail.Style.Border.Radius = 10;
        thumbnail.Style.Border.Width = 0;
        AppUi.BindBackground(thumbnail, AppThemeTokens.SurfaceSecondary);
        title = AppUi.Heading(entry.Title, 13.5f);
        facts = AppUi.Muted();
        path = AppUi.Muted();
        openFile = new Button();
        openFolder = new Button();
        AppUi.Primary(openFile);
        AppUi.Secondary(openFolder);
        openFile.Enabled = File.Exists(entry.FinalPath);
        openFolder.Enabled = Directory.Exists(System.IO.Path.GetDirectoryName(entry.FinalPath));
        openFile.Click += (_, _) => ShellService.OpenFile(entry.FinalPath);
        openFolder.Click += (_, _) => ShellService.OpenFolderForFile(entry.FinalPath);
        Controls.AddRange([placeholder, thumbnail, title, facts, path, openFile, openFolder]);
        SizeChanged += CardSizeChanged;
        text.LanguageChanged += LanguageChanged;
        ApplyLocalization();
        LayoutControls();
        _ = LoadThumbnailAsync();
    }

    private void LayoutControls()
    {
        var compact = Width < 760;
        Height = compact ? 184 : 132;
        placeholder.SetBounds(18, 18, compact ? 144 : 168, compact ? 81 : 94);
        thumbnail.SetBounds(18, 18, compact ? 144 : 168, compact ? 81 : 94);
        var textLeft = placeholder.Right + 16;
        var actionWidth = compact ? 0 : 132;
        var textRight = compact ? Width - 18 : Width - 18 - actionWidth - 18;
        var textWidth = Math.Max(230, textRight - textLeft);
        title.SetBounds(textLeft, 16, textWidth, 30);
        facts.SetBounds(textLeft, 49, textWidth, 24);
        path.SetBounds(textLeft, 78, textWidth, 24);
        if (compact)
        {
            openFile.SetBounds(18, 125, 134, 40);
            openFolder.SetBounds(162, 125, 146, 40);
        }
        else
        {
            openFile.SetBounds(Width - 150, 18, 132, 40);
            openFolder.SetBounds(Width - 150, 68, 132, 40);
        }
    }

    private void ApplyLocalization()
    {
        openFile.Text = text["Common.OpenFile"];
        openFolder.Text = text["Common.OpenFolder"];
        var qualityId = entry.QualityPresetId ?? QualityPreset.All
            .FirstOrDefault(item => string.Equals(item.DisplayName, entry.Quality, StringComparison.OrdinalIgnoreCase))?.Id;
        var quality = qualityId is not null ? text[$"Quality.{qualityId}"] : entry.Quality;
        facts.Text = $"{entry.Channel}  •  {quality}  •  {text.Get("History.CompletedAt", entry.CompletedAt.ToLocalTime().ToString("g", text.Culture))}";
        path.Text = text.Get("History.Path", entry.FinalPath);
    }

    private async Task LoadThumbnailAsync()
    {
        var image = await thumbnails.GetAsync(entry.ThumbnailUrl).ConfigureAwait(false);
        if (image is null)
            return;
        Application.RunOnUIThread(() =>
        {
            thumbnail.Image = image;
            thumbnail.Visible = true;
            placeholder.Visible = false;
        });
    }

    private void CardSizeChanged(object? sender, EventArgs e) => LayoutControls();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SizeChanged -= CardSizeChanged;
            text.LanguageChanged -= LanguageChanged;
        }
        base.Dispose(disposing);
    }
}
