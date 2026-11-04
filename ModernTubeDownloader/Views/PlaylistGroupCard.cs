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
    private readonly Label jobs = AppUi.Muted();
    private readonly AppVectorIcon chevron = new(AppIconKind.ChevronRight, 18), menuIcon = new(AppIconKind.More, 18);
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
        jobs.AccessibleAutomationId = "PlaylistGroupJobs-" + suffix;
        AppUi.Secondary(toggle); AppUi.Secondary(more);
        toggle.TextAlign = ContentAlignment.MiddleLeft;
        toggle.AutoEllipsis = true;
        foreach (var style in new[] { toggle.Style, toggle.StyleHover, toggle.StylePressed, toggle.StyleDisabled })
        { style.Border.Width = 0; style.BackgroundBrush = null; style.BackgroundColor = SKColors.Transparent; }
        toggle.StyleFocused.Border.Width = 1; toggle.StyleFocused.Border.Color = AppUi.GetColor(AppThemeTokens.Accent);
        more.AccessibleName = text["PlaylistGroup.Actions"];
        chevron.AccessibilityView = menuIcon.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
        chevron.TabStop = menuIcon.TabStop = false;
        ExcludeDecorationFromTabOrder(menuIcon);
        toggle.Controls.Add(chevron); more.Controls.Add(menuIcon);
        chevron.Click += (_, _) => toggle.PerformClick(); menuIcon.Click += (_, _) => more.PerformClick();
        DashboardVisual.SetPadding(toggle, new Padding(28, 0, 4, 0));
        AppTypography.Bind(toggle, 10.5f, FontStyle.Bold);
        counts.Multiline = true; counts.AutoEllipsis = true; completion.AutoEllipsis = true;
        AppTypography.Bind(counts, 9.5f); AppTypography.Bind(completion, 9.5f);
        AppTypography.Bind(provider, 9); provider.AutoEllipsis = true;
        AppTypography.Bind(jobs, 9, FontStyle.Bold); jobs.TextAlign = ContentAlignment.MiddleCenter;
        AppUi.BindBackground(jobs, DownloadsVisual.Selected); AppUi.BindForeground(jobs, AppThemeTokens.TextPrimary);
        jobs.Style.Border.Radius = 4; jobs.Style.Border.Width = 0;
        toggle.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        more.Click += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);
        toggle.KeyDown += NavigateGroupActions;
        more.KeyDown += NavigateGroupActions;
        foreach (var control in new Control[] { this, counts, completion, image, provider, progress, jobs })
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
        Controls.AddRange([toggle, more, counts, completion, image, provider, progress, sourceIcon, jobs]);
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
        toggle.Text = group.Title;
        chevron.SetKind(expanded ? AppIconKind.ChevronDown : AppIconKind.ChevronRight);
        ExcludeDecorationFromTabOrder(chevron);
        toggle.AccessibleName = text[expanded ? "PlaylistGroup.Collapse" : "PlaylistGroup.Expand"] + ": " + group.Title;
        tips.SetToolTip(toggle, group.Title + " · F1 · Ctrl+C");
        var fullCounts = text.Get("PlaylistGroup.Counts", c.Completed, c.Active, c.Queued, c.Failed, c.Interrupted, c.Cancelled);
        var totals = new[] { c.Completed, c.Active, c.Queued, c.Failed, c.Interrupted, c.Cancelled };
        var keys = new[] { "Completed", "Active", "Queued", "Failed", "Interrupted", "Cancelled" };
        counts.Text = string.Join(" · ", keys.Select((key, i) => totals[i] > 0 ? text.Get("PlaylistGroup.Count" + key, totals[i]) : null).Where(value => value is not null));
        counts.AccessibleName = fullCounts;
        tips.SetToolTip(counts, fullCounts);
        jobs.Text = c.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        jobs.AccessibleName = text.Get("PlaylistGroup.Jobs", c.Total);
        tips.SetToolTip(jobs, jobs.AccessibleName);
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
    private static void ExcludeDecorationFromTabOrder(Control control)
    {
        control.TabStop = false;
        foreach (var child in control.Controls) ExcludeDecorationFromTabOrder(child);
    }
    private void Arrange()
    {
        int T(int n) => AppTypography.TextHeight(n);
        var w = Math.Max(300, Width); var thumbnailWidth = Math.Min(124, w / 5);
        image.SetBounds(8, 10, thumbnailWidth, thumbnailWidth * 9 / 16);
        jobs.SetBounds(image.Right - Math.Min(thumbnailWidth, T(42)), image.Bottom - T(24), Math.Min(thumbnailWidth, T(42)), T(24));
        sourceIcon.SetBounds(8, image.Bottom + 8, 16, 16);
        provider.SetBounds(30, image.Bottom + 4, thumbnailWidth - 22, T(24));
        var left = image.Right + 16;
        more.SetBounds(w - 42, 10, 32, T(34));
        toggle.SetBounds(left, 8, Math.Max(0, more.Left - left - 8), T(34));
        chevron.SetBounds(4, (toggle.Height - 18) / 2, 18, 18);
        menuIcon.SetBounds((more.Width - 18) / 2, (more.Height - 18) / 2, 18, 18);
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
