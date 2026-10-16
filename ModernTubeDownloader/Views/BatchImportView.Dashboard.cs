using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private bool dashboard;
    private Panel? sourceCard, resultCard;
    private Label? sourceTitle, sourceHint, resultTitle, counters;
    private readonly List<DashboardMetric> metrics = [];
    private AppVectorIcon? sourceIcon;
    private readonly ToolTip dashboardTips = new();
    public void SetDashboard(bool value)
    {
        if (dashboard == value) return; dashboard = value;
        if (value && sourceCard is null)
        {
            sourceCard = new(); resultCard = new(); DashboardStyle.Card(sourceCard); DashboardStyle.Card(resultCard);
            sourceTitle = AppUi.Heading("", 13); sourceHint = AppUi.Muted(); sourceHint.Multiline = true;
            resultTitle = AppUi.Heading("", 13); counters = AppUi.Muted(); counters.Multiline = true;
            sourceCard.Controls.AddRange([sourceTitle, sourceHint]); resultCard.Controls.AddRange([resultTitle, counters]);
            sourceIcon = new AppVectorIcon(AppIconKind.Clipboard, 42, AppThemeTokens.Info); sourceCard.Controls.Add(sourceIcon);
            foreach (var token in new[] { AppThemeTokens.Info, AppThemeTokens.Success, AppThemeTokens.Warning, AppThemeTokens.Error }) { var metric = new DashboardMetric(token); metrics.Add(metric); resultCard.Controls.Add(metric); }
            body.Controls.AddRange([sourceCard, resultCard]);
        }
        foreach (var key in new[] { "Browse", "Import", "Examples" })
        {
            var button = buttons[key]; button.Parent?.Controls.Remove(button);
            if (value) sourceCard!.Controls.Add(button); else toolbar.Controls.Add(button);
        }
        if (sourceCard is not null) sourceCard.Visible = value;
        if (resultCard is not null) resultCard.Visible = value;
        buttons["Close"].Visible = !value;
        signature = ""; Render(); LayoutPage();
    }
    private void RefreshDashboardCounters()
    {
        if (!dashboard || counters is null) return;
        var t = services.Localization; sourceTitle!.Text = t["Dashboard.BatchSource"]; sourceHint!.Text = t["Dashboard.BatchHint"];
        resultTitle!.Text = t["Dashboard.Statistics"];
        if (session is not { } s) { counters.Text = t["Batch.Empty"]; for (var i = 0; i < 4; i++) metrics[i].Present(t[i switch { 0 => "Dashboard.All", 1 => "Batch.Filter.Ready", 2 => "Batch.Filter.Duplicates", _ => "Dashboard.Errors" }], 0); return; }
        lock (s.Sync)
        {
            metrics[0].Present(t["Dashboard.All"], s.ImportedCount);
            metrics[1].Present(t["Batch.Filter.Ready"], s.Entries.Count(e => e.Status == BatchEntryStatus.Ready));
            metrics[2].Present(t["Batch.Filter.Duplicates"], s.Entries.Count(e => e.Status == BatchEntryStatus.Duplicate));
            metrics[3].Present(t["Dashboard.Errors"], s.Entries.Count(e => e.Status is BatchEntryStatus.Failed or BatchEntryStatus.Unsupported));
            counters.Text = s.FileName + " · " + t["Dashboard.All"] + ": " + s.ImportedCount;
        }
    }
    private void LayoutDashboard()
    {
        if (sourceCard is null) return;
        var origin = body.DisplayRectangle.Location; var width = Math.Max(560, body.ClientSize.Width - 36);
        var line = AppTypography.TextHeight(36); var inline = width >= AppTypography.TextHeight(940);
        heading.SetBounds(18 + origin.X, 18 + origin.Y, width, AppTypography.TextHeight(48));
        var left = inline ? width * 42 / 100 - 12 : width;
        sourceCard.SetBounds(18 + origin.X, heading.Bottom + 18, left, AppTypography.TextHeight(190));
        sourceTitle!.SetBounds(16, 16, left - 32, line); AppTypography.Bind(sourceTitle, 12);
        sourceIcon!.SetBounds(20, sourceTitle.Bottom + 14, 42, 42);
        sourceHint!.SetBounds(78, sourceTitle.Bottom + 12, left - 96, AppTypography.TextHeight(56));
        var x = 16;
        foreach (var key in new[] { "Browse", "Import", "Examples" }) { buttons[key].SetBounds(x, sourceHint.Bottom + 18, (left - 56) * (key == "Examples" ? 42 : 29) / 100, line); DashboardVisual.Button(buttons[key], key == "Browse"); x = buttons[key].Right + 10; }
        sourceCard.Height = buttons["Browse"].Bottom + 18;
        resultCard!.SetBounds(inline ? sourceCard.Right + 12 : sourceCard.Left, inline ? sourceCard.Top : sourceCard.Bottom + 12, inline ? width - left - 12 : width, sourceCard.Height);
        resultTitle!.SetBounds(16, 12, resultCard.Width - 32, line);
        var metricWidth = (resultCard.Width - 44) / 4;
        for (var i = 0; i < metrics.Count; i++) metrics[i].SetBounds(12 + i * (metricWidth + 6), resultTitle.Bottom + 8, metricWidth, AppTypography.TextHeight(90));
        counters!.SetBounds(16, resultTitle.Bottom + AppTypography.TextHeight(104), resultCard.Width - 32, AppTypography.TextHeight(42)); AppTypography.Bind(counters, 9);
        resultCard.Height = Math.Max(sourceCard.Height, counters.Bottom + 12);
        var top = Math.Max(sourceCard.Bottom, resultCard.Bottom) + 12;
        var qualityWidth = AppTypography.TextHeight(160); var containerWidth = AppTypography.TextHeight(150); var workerWidth = AppTypography.TextHeight(86);
        var pathWidth = Math.Max(180, width - qualityWidth - containerWidth - workerWidth - 30);
        if (pathWidth + qualityWidth + containerWidth + workerWidth + 30 > width) pathWidth = width;
        var fieldX = 0; var fieldY = top;
        foreach (var field in new[] { (Control: (Control)path, Width: pathWidth), (Control: (Control)quality, Width: qualityWidth), (Control: (Control)container, Width: containerWidth), (Control: (Control)workers, Width: workerWidth) })
        {
            var fieldWidth = Math.Min(width, field.Width);
            if (fieldX > 0 && fieldX + fieldWidth > width) { fieldX = 0; fieldY += line + 8; }
            field.Control.SetBounds(18 + origin.X + fieldX, fieldY, fieldWidth, line); fieldX += fieldWidth + 10;
        }
        var tx = 0; var ty = 0;
        foreach (var button in buttons.Values.Where(b => b.Parent == toolbar && b.Visible))
        {
            button.Width = Math.Min(width, AppTypography.TextHeight(Math.Clamp(button.Text.Length * 8 + 28, 116, 260))); button.Height = line;
            if (tx > 0 && tx + button.Width + 8 > width) { tx = 0; ty += line + 8; }
            tx += button.Width + 8; DashboardVisual.Button(button, button == buttons["Add"]);
        }
        toolbar.SetBounds(18 + origin.X, fieldY + line + 12, width, ty + line + 8);
        filter.SetBounds(18 + origin.X, toolbar.Bottom + 8, Math.Min(width / 3, AppTypography.TextHeight(220)), line);
        search.SetBounds(filter.Right + 12, filter.Top, Math.Max(180, width - filter.Width - 12), line);
        range.SetBounds(18 + origin.X, filter.Bottom + 10, width, AppTypography.TextHeight(range.IsCustom ? 84 : 38));
        status.SetBounds(18 + origin.X, range.Bottom + 8, width, AppTypography.TextHeight(lastErrorKey is null ? 44 : 90)); status.Multiline = true;
        AppUi.BindBackground(rows, AppThemeTokens.Surface);
        rows.SetBounds(18 + origin.X, status.Bottom + 14, width, Math.Max(AppTypography.TextHeight(220), body.ClientSize.Height - (status.Bottom - origin.Y) - 76));
        progress.SetBounds(rows.Left, rows.Top - 8, width, 4); pageText.SetBounds(rows.Left, rows.Bottom + 10, width, line);
        LayoutRows();
    }
    private void LayoutDashboardRow(Control panel)
    {
        panel.Width = Math.Max(480, rows.DisplayRectangle.Width - 6); var c = panel.Controls.ToArray();
        var line = AppTypography.TextHeight(23); var action = AppTypography.TextHeight(32); var narrow = panel.Width < AppTypography.TextHeight(850);
        var imageWidth = c.Length > 8 ? 76 : 0; var left = 70 + imageWidth;
        c[0].SetBounds(10, 14, 54, line);
        var actionLeft = panel.Width - 4 * (action + 6) - 12;
        var textRight = narrow ? panel.Width - 16 : Math.Max(left + 180, actionLeft - 12);
        c[1].SetBounds(left, 8, textRight - left, line); c[2].SetBounds(left, c[1].Bottom + 2, textRight - left, line);
        c[3].SetBounds(left, c[2].Bottom + 3, textRight - left, line);
        for (var i = 4; i < 8; i++)
        {
            var button = (Button)c[i];
            if (button.Controls.OfType<AppVectorIcon>().FirstOrDefault() is not { } glyph)
            {
                button.AccessibleName = button.Text;
                glyph = new AppVectorIcon(i switch { 4 => AppIconKind.Search, 5 => AppIconKind.Download, 6 => AppIconKind.More, _ => AppIconKind.Close }, 18);
                glyph.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
                glyph.Click += (_, _) => button.PerformClick(); button.Controls.Add(glyph);
            }
            button.Text = "";
            dashboardTips.SetToolTip(button, button.AccessibleName);
            button.SetBounds(narrow ? left + (i - 4) * (action + 8) : actionLeft + (i - 4) * (action + 6), narrow ? c[3].Bottom + 8 : 24, action, action); DashboardVisual.Button(button);
            glyph.SetBounds((button.Width - 18) / 2, (button.Height - 18) / 2, 18, 18);
        }
        panel.Height = Math.Max(c[3].Bottom + 10, c[7].Bottom + 10); DashboardVisual.Flat(panel, true); AppUi.BindBackground(panel, AppThemeTokens.Surface);
        if (c.Length > 8) c[8].SetBounds(66, 10, 68, 44);
        foreach (var label in c.OfType<Label>()) { label.AutoEllipsis = true; AppTypography.Bind(label, 9.5f); }
        panel.Margin = new Padding(0, 0, 0, 1);
    }
}
