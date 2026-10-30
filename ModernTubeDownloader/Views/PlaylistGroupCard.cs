using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal sealed class PlaylistGroupCard : Panel
{
    private readonly Button toggle = new DashboardButton(), more = new DashboardButton();
    private readonly Label counts = AppUi.Muted(), completion = AppUi.Muted(), provider = AppUi.Muted();
    private readonly DownloadsPictureBox image = new() { SizeMode = PictureBoxSizeMode.Zoom, RoundedPreview = true };
    private readonly DashboardProgress progress = new();
    private readonly SourceVectorIcon sourceIcon = new() { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
    private readonly ToolTip tips = new();
    private readonly ThumbnailCacheService thumbnails;
    private readonly LocalizationService text;
    private readonly CancellationTokenSource lifetime = new();
    private SKBitmap? ownedImage;
    private string? thumbnailUrl;
    private bool closed;
    private bool? surfaceSelected;
    public PlaylistGroup Group { get; private set; }
    public event EventHandler? ToggleRequested, MenuRequested;

    public PlaylistGroupCard(PlaylistGroup group, ThumbnailCacheService thumbnails, LocalizationService text)
    {
        Group = group; this.thumbnails = thumbnails; this.text = text;
        var suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            group.Key.Authority + "\n" + group.Key.PlaylistId))).ToLowerInvariant();
        AccessibleAutomationId = "PlaylistGroup-" + suffix;
        toggle.AccessibleAutomationId = "PlaylistGroupToggle-" + suffix;
        more.AccessibleAutomationId = "PlaylistGroupMenu-" + suffix;
        counts.AccessibleAutomationId = "PlaylistGroupCounts-" + suffix;
        completion.AccessibleAutomationId = "PlaylistGroupCompletion-" + suffix;
        AppUi.Secondary(toggle); AppUi.Secondary(more);
        toggle.TextAlign = ContentAlignment.MiddleLeft;
        toggle.AutoEllipsis = true;
        foreach (var style in new[] { toggle.Style, toggle.StyleHover, toggle.StylePressed, toggle.StyleDisabled })
        { style.Border.Width = 0; style.BackgroundBrush = null; style.BackgroundColor = SKColors.Transparent; }
        toggle.StyleFocused.Border.Width = 1; toggle.StyleFocused.Border.Color = AppUi.GetColor(AppThemeTokens.Accent);
        more.Text = "⋮"; more.AccessibleName = text["PlaylistGroup.Actions"];
        AppTypography.Bind(toggle, 10.5f, FontStyle.Bold);
        counts.Multiline = true; counts.AutoEllipsis = true; completion.AutoEllipsis = true;
        AppTypography.Bind(counts, 9.5f); AppTypography.Bind(completion, 9.5f);
        AppTypography.Bind(provider, 9); provider.AutoEllipsis = true;
        toggle.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        more.Click += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);
        toggle.KeyDown += NavigateGroupActions;
        more.KeyDown += NavigateGroupActions;
        foreach (var control in new Control[] { this, counts, completion, image, provider, progress })
            control.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        toggle.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F1) { tips.Show(Group.Title, toggle, new System.Drawing.Point(0, toggle.Height), 5000); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.C)
            {
                e.Handled = true;
                try { await Clipboard.SetTextAsync(Group.Title); }
                catch (Exception) { tips.Show(text["Error.Generic"], toggle, new System.Drawing.Point(0, toggle.Height), 5000); }
            }
        };
        Controls.AddRange([toggle, more, counts, completion, image, provider, progress, sourceIcon]);
        AppTypography.SetDashboardPresentation(this, true);
        Margin = new Padding(0, 0, 0, 4);
        SizeChanged += (_, _) => Arrange();
        Update(group, false, null);
    }
    private static void NavigateGroupActions(object? sender, KeyEventArgs e)
    {
        if (sender is not Control current || e.KeyCode != Keys.Tab || e.Control || e.Alt) return;
        // The pinned native window path filters the translated Tab character.
        // Reuse MFN's canonical traversal for these new actions; do not own focus.
        var root = current;
        while (root.Parent is { } parent) root = parent;
        if (root.SelectNextControl(current, !e.Shift, true, true, true)) e.Handled = e.SuppressKeyPress = true;
    }
    public void Update(PlaylistGroup group, bool expanded, Guid? selected)
    {
        Group = group;
        var c = group.Counts;
        var hasSelection = selected is { } id && group.Children.Any(item => item.Id == id);
        if (surfaceSelected != hasSelection)
        {
            surfaceSelected = hasSelection;
            DownloadsVisual.Card(this);
            if (hasSelection) { AppUi.BindGradient(this, DownloadsVisual.Selected, DownloadsVisual.SelectedEnd); AppUi.BindBorder(this, AppThemeTokens.Accent); }
        }
        toggle.Text = (expanded ? "⌄  " : "›  ") + group.Title;
        toggle.AccessibleName = text[expanded ? "PlaylistGroup.Collapse" : "PlaylistGroup.Expand"] + ": " + group.Title;
        tips.SetToolTip(toggle, group.Title + " · F1 · Ctrl+C");
        counts.Text = text.Get("PlaylistGroup.Counts", c.Completed, c.Active, c.Queued, c.Failed, c.Interrupted, c.Cancelled);
        tips.SetToolTip(counts, counts.Text);
        completion.Text = text.Get("PlaylistGroup.Completion", c.Completed, c.Total) + (hasSelection ? " · " + text["PlaylistGroup.SelectedChild"] : "");
        progress.Value = c.Total == 0 ? 0 : (int)(100d * c.Completed / c.Total);
        var source = SourceBrandLookup.Resolve(group.Children[0].SourceUrl);
        provider.Text = source.Name; sourceIcon.Brand = source.Brand;
        tips.SetToolTip(provider, group.Key.Authority + " · " + text.Get("PlaylistGroup.Jobs", c.Total));
        if (thumbnailUrl != group.Children[0].ThumbnailUrl || image.Image is null)
        {
            thumbnailUrl = group.Children[0].ThumbnailUrl;
            image.Image = AppBranding.Icon;
            _ = LoadImage(thumbnailUrl);
        }
        Arrange();
    }
    private async Task LoadImage(string? url)
    {
        SKBitmap? bitmap;
        try { bitmap = await thumbnails.GetPreviewCopyAsync(url, lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException) { return; }
        if (bitmap is null) return;
        Application.RunOnUIThread(() =>
        {
            if (closed || url != thumbnailUrl) { bitmap.Dispose(); return; }
            image.Image = null; ownedImage?.Dispose(); ownedImage = bitmap; image.Image = bitmap;
        });
    }
    private void Arrange()
    {
        int T(int n) => AppTypography.TextHeight(n);
        var w = Math.Max(300, Width); var thumbnailWidth = Math.Min(124, w / 5);
        image.SetBounds(8, 10, thumbnailWidth, thumbnailWidth * 9 / 16);
        sourceIcon.SetBounds(8, image.Bottom + 8, 16, 16);
        provider.SetBounds(30, image.Bottom + 4, thumbnailWidth - 22, T(24));
        var left = image.Right + 16;
        more.SetBounds(w - 42, 10, 32, T(34));
        toggle.SetBounds(left, 8, Math.Max(0, more.Left - left - 8), T(34));
        var textWidth = Math.Max(0, w - left - 12);
        counts.SetBounds(left, toggle.Bottom + 5, textWidth, DashboardVisual.LabelHeight(counts, textWidth, 3));
        completion.SetBounds(left, counts.Bottom + 3, counts.Width, T(24));
        progress.SetBounds(left, completion.Bottom + 3, counts.Width, 6);
        Height = Math.Max(provider.Bottom + 10, progress.Bottom + 10);
    }
    public void ShowMenu(ContextMenu menu) => menu.Show(more, more.PointToScreen(new(0, more.Height)));
    protected override void Dispose(bool disposing)
    {
        if (disposing && !closed) { closed = true; lifetime.Cancel(); lifetime.Dispose(); image.Image = null; ownedImage?.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
