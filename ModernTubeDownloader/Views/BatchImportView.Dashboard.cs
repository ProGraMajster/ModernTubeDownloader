using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private bool dashboard;
    private Panel? sourceCard, resultCard;
    private Label? sourceTitle, sourceHint, resultTitle, counters;
    public void SetDashboard(bool value)
    {
        if (dashboard == value) return; dashboard = value;
        if (value && sourceCard is null)
        {
            sourceCard = new(); resultCard = new(); DashboardStyle.Card(sourceCard); DashboardStyle.Card(resultCard);
            sourceTitle = AppUi.Heading("", 13); sourceHint = AppUi.Muted(); sourceHint.Multiline = true;
            resultTitle = AppUi.Heading("", 13); counters = AppUi.Muted(); counters.Multiline = true;
            sourceCard.Controls.AddRange([sourceTitle, sourceHint]); resultCard.Controls.AddRange([resultTitle, counters]);
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
        if (session is not { } s) { counters.Text = t["Batch.Empty"]; return; }
        lock (s.Sync)
        {
            counters.Text = t.Get("Dashboard.BatchCounts", s.ImportedCount, s.Entries.Count(e => e.Status == BatchEntryStatus.Ready), s.Entries.Count(e => e.Status == BatchEntryStatus.Duplicate), s.Entries.Count(e => e.Status is BatchEntryStatus.Failed or BatchEntryStatus.Unsupported));
        }
    }
    private void LayoutDashboard()
    {
        if (sourceCard is null) return;
        var origin = body.DisplayRectangle.Location; var width = Math.Max(560, body.ClientSize.Width - 36);
        var line = AppTypography.TextHeight(36); var inline = width >= AppTypography.TextHeight(940);
        heading.SetBounds(18 + origin.X, 18 + origin.Y, width, AppTypography.TextHeight(38));
        var left = inline ? width * 3 / 5 - 12 : width;
        sourceCard.SetBounds(18 + origin.X, heading.Bottom + 18, left, AppTypography.TextHeight(190));
        sourceTitle!.SetBounds(16, 12, left - 32, line); sourceHint!.SetBounds(16, sourceTitle.Bottom + 6, left - 32, line * 2);
        var x = 16;
        foreach (var key in new[] { "Browse", "Import", "Examples" }) { buttons[key].SetBounds(x, sourceHint.Bottom + 12, (left - 56) / 3, line); x = buttons[key].Right + 10; }
        sourceCard.Height = buttons["Browse"].Bottom + 16;
        resultCard!.SetBounds(inline ? sourceCard.Right + 12 : sourceCard.Left, inline ? sourceCard.Top : sourceCard.Bottom + 12, inline ? width - left - 12 : width, inline ? sourceCard.Height : AppTypography.TextHeight(146));
        resultTitle!.SetBounds(16, 12, resultCard.Width - 32, line); counters!.SetBounds(16, resultTitle.Bottom + 8, resultCard.Width - 32, line * 4);
        var top = Math.Max(sourceCard.Bottom, resultCard.Bottom) + 12;
        path.SetBounds(18 + origin.X, top, Math.Max(180, width - 430), line); quality.SetBounds(path.Right + 10, top, 160, line); container.SetBounds(quality.Right + 10, top, 150, line); workers.SetBounds(container.Right + 10, top, 86, line);
        foreach (var button in buttons.Values.Where(b => b.Parent == toolbar)) { button.Width = Math.Min(width, AppTypography.TextHeight(150)); button.Height = line; }
        toolbar.SetBounds(18 + origin.X, path.Bottom + 12, width, (int)Math.Ceiling(buttons.Values.Count(b => b.Parent == toolbar && b.Visible) / (double)Math.Max(1, width / (AppTypography.TextHeight(150) + 8))) * (line + 8));
        filter.SetBounds(18 + origin.X, toolbar.Bottom + 8, Math.Min(width / 3, AppTypography.TextHeight(220)), line);
        search.SetBounds(filter.Right + 12, filter.Top, Math.Max(180, width - filter.Width - 12), line);
        range.SetBounds(18 + origin.X, filter.Bottom + 10, width, AppTypography.TextHeight(range.IsCustom ? 84 : 38));
        status.SetBounds(18 + origin.X, range.Bottom + 8, width, AppTypography.TextHeight(lastErrorKey is null ? 44 : 90)); status.Multiline = true;
        rows.SetBounds(18 + origin.X, status.Bottom + 14, width, Math.Max(AppTypography.TextHeight(220), body.ClientSize.Height - (status.Bottom - origin.Y) - 76));
        progress.SetBounds(rows.Left, rows.Top - 8, width, 4); pageText.SetBounds(rows.Left, rows.Bottom + 10, width, line);
        LayoutRows();
    }
    private void LayoutDashboardRow(Control panel)
    {
        panel.Width = Math.Max(480, rows.DisplayRectangle.Width - 6); var c = panel.Controls.ToArray();
        var line = AppTypography.TextHeight(24); var action = AppTypography.TextHeight(32); var narrow = panel.Width < AppTypography.TextHeight(900);
        c[0].SetBounds(10, 8, 62, line); c[1].SetBounds(78, 6, panel.Width - 92, line); c[2].SetBounds(78, c[1].Bottom + 2, panel.Width - 92, line);
        c[3].SetBounds(78, c[2].Bottom + 4, Math.Max(100, panel.Width - 640), line);
        var top = narrow ? c[3].Bottom + 8 : c[3].Top; var width = narrow ? (panel.Width - 38) / 4 : 128;
        for (var i = 4; i < 8; i++) { c[i].SetBounds(narrow ? 10 + (i - 4) * (width + 6) : panel.Width - 544 + (i - 4) * 134, top, width, action); }
        if (AppTypography.Scale > 1.25)
        {
            var columns = panel.Width >= AppTypography.TextHeight(720) ? 4 : 2;
            var actionWidth = (panel.Width - 20 - (columns - 1) * 8) / columns;
            for (var i = 4; i < 8; i++) c[i].SetBounds(10 + ((i - 4) % columns) * (actionWidth + 8), c[3].Bottom + 8 + ((i - 4) / columns) * (action + 8), actionWidth, action);
        }
        panel.Height = c[7].Bottom + 10;
        if (c.Length > 8) c[8].SetBounds(panel.Width - 90, 6, 76, 42);
        foreach (var label in c.OfType<Label>()) { label.AutoEllipsis = true; AppTypography.Bind(label, 9.5f); }
        panel.Margin = new Padding(0, 0, 0, 2);
    }
}
