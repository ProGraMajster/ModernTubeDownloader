using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class OtherView
{
    private bool dashboard;
    private Panel? quick, links;
    private readonly Dictionary<Control, AppVectorIcon> moduleIcons = [];
    private readonly Dictionary<Control, Panel> moduleWorkflows = [];
    private readonly List<Button> linkActions = [];
    public event EventHandler<ModernTubeDownloader.Shell.AppPageId>? NavigateRequested;
    private Label? quickTitle, toolsTitle, tipTitle, tipText, quickStatus;
    private Panel? tip;
    private readonly List<Button> quickButtons = [];
    private DashboardDropZone? importDropZone;
    private Label? importDropTitle, importDropHint;
    private Button? importPickFile;
    public event EventHandler? PickBatchFileRequested;

    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value)
        {
            DashboardVisual.RestoreTree(this);
            AutoScroll = false; tools.AutoScroll = true;
            foreach (var module in modules.Values)
            {
                AppUi.Secondary(module.Open); AppTypography.Bind(module.Title, 16, FontStyle.Bold);
                module.Description.Multiline = false;
                if (module.Title.Parent is { } card) { AppUi.Card(card); card.Margin = new Padding(0, 0, 0, 14); }
            }
            foreach (var icon in moduleIcons.Values) icon.Visible = false;
            foreach (var workflow in moduleWorkflows.Values) workflow.Visible = false;
            if (importDropZone is not null) importDropZone.Visible = false;
            foreach (var module in modules.Values)
            {
                if (module.Title.Parent is not { } card || card.Parent == tools) continue;
                card.Parent?.Controls.Remove(card); tools.Controls.Add(card);
            }
            ApplyLocalization();
            AppUi.Card(recent);
        }
        if (value && quick is null)
        {
            links = new Panel(); AppUi.BindBackground(links);
            foreach (var page in new[] { ModernTubeDownloader.Shell.AppPageId.History, ModernTubeDownloader.Shell.AppPageId.Settings, ModernTubeDownloader.Shell.AppPageId.Help })
            {
                var button = new DashboardButton { Tag = page, AccessibleAutomationId = "OtherNavigate" + page };
                button.Click += (_, _) => NavigateRequested?.Invoke(this, page);
                var kind = page == ModernTubeDownloader.Shell.AppPageId.History ? AppIconKind.History : page == ModernTubeDownloader.Shell.AppPageId.Settings ? AppIconKind.Settings : AppIconKind.Help;
                var icon = new AppVectorIcon(kind, 28, AppThemeTokens.Info);
                var title = AppUi.Heading("", 11); var description = AppUi.Muted(); description.Multiline = true;
                foreach (var child in new Control[] { icon, title, description })
                {
                    child.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
                    child.Click += (_, _) => button.PerformClick();
                }
                button.Controls.AddRange([icon, title, description]); linkActions.Add(button); links.Controls.Add(button);
            }
            toolsTitle = AppUi.Heading("", 13); links.Controls.Add(toolsTitle);
            Controls.Add(links);
            quick = new Panel(); DashboardStyle.Card(quick); quickTitle = AppUi.Heading("", 13); quick.Controls.Add(quickTitle);
            foreach (var key in new[] { "Common.OpenFolder", "Settings.OpenLogs", "Appearance.Export", "Updates.Check" })
            {
                var button = new DashboardButton { Tag = key, AccessibleAutomationId = "OtherQuick" + key.Replace(".", "") };
                var icon = new AppVectorIcon(key == "Appearance.Export" ? AppIconKind.Download : key == "Updates.Check" ? AppIconKind.Retry : AppIconKind.Folder, 24);
                var caption = AppUi.Muted(); caption.Multiline = true; caption.TextAlign = ModernFormsNext.ContentAlignment.MiddleCenter;
                AppUi.BindForeground(caption);
                foreach (var child in new Control[] { icon, caption })
                {
                    child.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
                    child.Click += (_, _) => button.PerformClick();
                }
                button.Controls.AddRange([icon, caption]);
                button.Click += async (_, _) =>
                {
                    button.Enabled = false;
                    try
                    {
                        if (key == "Common.OpenFolder") Utilities.ShellService.OpenFolderForFile(services.Settings.Current.FinalOutputDirectory);
                        else if (key == "Settings.OpenLogs") Utilities.ShellService.OpenFolderForFile(services.Paths.LogDirectory);
                        else if (key == "Updates.Check") { await services.Updates.CheckAsync(); if (!disposed) quickStatus!.Text = services.Localization[services.Updates.StatusKey]; }
                        else if (FindForm() is { } form)
                        {
                            var dialog = new SaveFileDialog { FileName = "appearance.json", DefaultExtension = "json" }; dialog.AddFilter("JSON", "*.json");
                            if (await dialog.ShowDialog(form) == DialogResult.OK)
                                await File.WriteAllTextAsync(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(services.Appearance.Profile, AppearanceProfileStore.Options));
                        }
                    }
                    catch (Exception error)
                    {
                        services.Logger.Error("Other dashboard action failed.", error);
                        if (!disposed) quickStatus!.Text = services.Localization["Appearance.Error.Profile"];
                    }
                    finally { if (!disposed) { button.Enabled = true; LayoutPage(); } }
                };
                quickButtons.Add(button); quick.Controls.Add(button);
            }
            quickStatus = AppUi.Muted(); quickStatus.Multiline = true; quickStatus.AccessibleAutomationId = "OtherQuickStatus";
            quick.Controls.Add(quickStatus); Controls.Add(quick);
            tip = new Panel(); DownloadsVisual.Card(tip); tipTitle = AppUi.Heading("", 12); tipText = AppUi.Muted(); tipText.Multiline = true;
            tip.Controls.AddRange([tipTitle, tipText]); Controls.Add(tip);
        }
        if (quick is not null) quick.Visible = value;
        if (links is not null) links.Visible = value;
        if (tip is not null) tip.Visible = value;
        recent.Parent?.Controls.Remove(recent);
        if (value) Controls.Add(recent); else tools.Controls.Add(recent);
        LayoutPage();
        _ = RefreshRecentAsync();
    }

    private sealed record RecentToolRow(Label Title, Label Subtitle, AppVectorIcon Icon);
    private static Button RecentToolButton(string title, string subtitle, AppIconKind kind)
    {
        var button = new DashboardButton { AccessibleName = title + " · " + subtitle };
        var heading = AppUi.Heading(title, 10); heading.AutoEllipsis = true;
        var detail = AppUi.Muted(subtitle); detail.AutoEllipsis = true; AppTypography.Bind(detail, 9);
        var icon = new AppVectorIcon(kind, 24, AppThemeTokens.Info);
        foreach (var child in new Control[] { heading, detail, icon })
        { child.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; child.Click += (_, _) => button.PerformClick(); }
        button.Controls.AddRange([heading, detail, icon]); button.Tag = new RecentToolRow(heading, detail, icon);
        return button;
    }

    private void LayoutDashboard()
    {
        AutoScroll = true; tools.AutoScroll = false; var origin = DisplayRectangle.Location; var width = Math.Max(1, DisplayRectangle.Width - 36);
        heading.SetBounds(18 + origin.X, 20 + origin.Y, width, AppTypography.TextHeight(48));
        subtitle.SetBounds(heading.Left, heading.Bottom + 4, width, AppTypography.TextHeight(28));
        var inline = width >= AppTypography.TextHeight(960); var left = inline ? width * 2 / 3 - 12 : width;
        tools.SetBounds(heading.Left, subtitle.Bottom + 18, left, 1); AppUi.BindBackground(tools);
        var toolHeight = 0;
        foreach (var (id, module) in modules)
        {
            if (module.Title.Parent is not { } card) continue;
            if (id != "Batch") { if (card.Parent != links) { card.Parent?.Controls.Remove(card); links!.Controls.Add(card); } continue; }
            DownloadsVisual.Card(card); card.Width = left - 2;
            if (!moduleIcons.TryGetValue(card, out var icon))
            {
                icon = new AppVectorIcon(AppIconKind.Clipboard, 36, AppThemeTokens.Info) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
                moduleIcons.Add(card, icon); card.Controls.Add(icon);
            }
            icon.Visible = true; icon.SetBounds(16, 20, 36, 36);
            icon.SetKind(id == "Batch" ? AppIconKind.Clipboard : AppIconKind.Media);
            AppTypography.Bind(module.Title, 12, FontStyle.Bold);
            module.Title.SetBounds(66, 16, card.Width - 86, AppTypography.TextHeight(28));
            module.Description.Multiline = true;
            var descriptionStyle = module.Description.CurrentStyle;
            var descriptionSize = TextMeasurer.MeasureText(module.Description.Text, descriptionStyle.GetFont(), descriptionStyle.GetFontSize(), new System.Drawing.Size(card.Width - 86, int.MaxValue), descriptionStyle.GetFontStyle());
            module.Description.SetBounds(66, module.Title.Bottom + 4, card.Width - 86, Math.Max(AppTypography.TextHeight(24), (int)Math.Ceiling(descriptionSize.Height) + 4));
            if (importDropZone is null)
            {
                importDropZone = new(); importDropTitle = AppUi.Heading("", 12); importDropHint = AppUi.Muted();
                importDropTitle.TextAlign = importDropHint.TextAlign = ContentAlignment.MiddleCenter;
                importDropHint.Multiline = true;
                importPickFile = new DashboardButton { AccessibleAutomationId = "OtherPickBatchFile" };
                importPickFile.Click += (_, _) => PickBatchFileRequested?.Invoke(this, EventArgs.Empty);
                foreach (var surface in new Control[] { importDropZone, importDropTitle, importDropHint })
                    surface.Click += (_, _) => importPickFile.PerformClick();
                importDropZone.Controls.AddRange([importDropTitle, importDropHint, importPickFile]); card.Controls.Add(importDropZone);
            }
            importDropZone.Visible = true;
            importDropZone.SetBounds(16, module.Description.Bottom + 12, card.Width - 32, AppTypography.TextHeight(110));
            importDropTitle!.Text = services.Localization["Dashboard.DropFile"];
            importDropHint!.Text = services.Localization["Dashboard.DropFormats"];
            importDropTitle.SetBounds(12, 14, importDropZone.Width - 24, AppTypography.TextHeight(28));
            importDropHint.SetBounds(12, importDropTitle.Bottom + 6, importDropZone.Width - 24, DashboardVisual.LabelHeight(importDropHint, importDropZone.Width - 24, 2));
            importPickFile!.Text = services.Localization["Batch.Browse"]; DownloadsVisual.Button(importPickFile, true);
            var pickWidth = Math.Min(importDropZone.Width - 24, DashboardVisual.ActionWidth(importPickFile, true));
            importPickFile.SetBounds((importDropZone.Width - pickWidth) / 2, importDropHint.Bottom + 12, pickWidth, AppTypography.TextHeight(36));
            DashboardVisual.Icon(importPickFile, AppIconKind.Folder);
            importDropZone.Height = importPickFile.Bottom + 16;
            if (!moduleWorkflows.TryGetValue(card, out var workflow))
            {
                workflow = new Panel(); DashboardVisual.Flat(workflow); AppUi.BindBackground(workflow, AppThemeTokens.SurfaceSecondary);
                foreach (var key in new[] { "Batch.Browse", "Batch.Analyze", "Batch.Add" })
                {
                    var step = AppUi.Muted(); step.Multiline = true; step.Tag = key; workflow.Controls.Add(step);
                }
                moduleWorkflows.Add(card, workflow); card.Controls.Add(workflow);
            }
            workflow.Visible = true; workflow.SetBounds(16, importDropZone.Bottom + 10, card.Width - 32, 1);
            var stepColumns = workflow.Width >= AppTypography.TextHeight(570) ? 3 : 1;
            var stepWidth = (workflow.Width - 24 - (stepColumns - 1) * 12) / stepColumns;
            var stepHeight = 0;
            foreach (var step in workflow.Controls.Cast<Label>())
            {
                step.Text = $"{workflow.Controls.IndexOf(step) + 1}. {services.Localization[(string)step.Tag!]}";
                stepHeight = Math.Max(stepHeight, DashboardVisual.LabelHeight(step, stepWidth, 3));
            }
            for (var stepIndex = 0; stepIndex < workflow.Controls.Count; stepIndex++)
            {
                workflow.Controls[stepIndex].SetBounds(12 + stepIndex % stepColumns * (stepWidth + 12),
                    12 + stepIndex / stepColumns * (stepHeight + 10), stepWidth, stepHeight);
            }
            workflow.Height = workflow.Controls.Max(step => step.Bottom) + 12;
            module.Open.Text = services.Localization["Dashboard.OpenBatch"];
            DashboardVisual.Button(module.Open, true); DashboardVisual.Icon(module.Open, AppIconKind.Clipboard);
            var actionWidth = Math.Min(card.Width - 48, DashboardVisual.ActionWidth(module.Open, true));
            module.Open.SetBounds(card.Width - 16 - actionWidth, workflow.Bottom + 12, actionWidth, AppTypography.TextHeight(36));
            DashboardVisual.Icon(module.Open, AppIconKind.Clipboard);
            card.Height = module.Open.Bottom + 16; toolHeight += card.Height + card.Margin.Bottom;
        }
        tools.Height = Math.Max(AppTypography.TextHeight(220), toolHeight);
        links!.SetBounds(tools.Left, tools.Bottom + 10, left, 1);
        toolsTitle!.Text = services.Localization["Other.ToolsTitle"]; toolsTitle.SetBounds(0, 0, left, AppTypography.TextHeight(30));
        var linkColumns = Math.Clamp(left / AppTypography.TextHeight(310), 1, 2);
        var itemWidth = (left - (linkColumns - 1) * 12) / linkColumns;
        var tileHeight = AppTypography.TextHeight(116);
        var tiles = modules.Where(pair => pair.Key != "Batch").Select(pair => pair.Value.Title.Parent!).Concat(linkActions).ToArray();
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i]; tile.Margin = Padding.Empty;
            tile.SetBounds((i % linkColumns) * (itemWidth + 12), toolsTitle.Bottom + 10 + (i / linkColumns) * (tileHeight + 12), itemWidth, tileHeight);
            if (tile is Button button)
            {
                button.AccessibleName = services.Localization["Nav." + button.Tag]; button.Text = ""; DownloadsVisual.Button(button);
                button.Controls[0].SetBounds(16, 18, 28, 28);
                button.Controls[1].Text = button.AccessibleName; button.Controls[1].SetBounds(58, 12, itemWidth - 76, AppTypography.TextHeight(28));
                button.Controls[2].Text = services.Localization["Dashboard.Route" + button.Tag];
                button.Controls[2].SetBounds(58, button.Controls[1].Bottom + 4, itemWidth - 76, DashboardVisual.LabelHeight((Label)button.Controls[2], itemWidth - 76, 3));
            }
            else
            {
                var pair = modules.First(pair => pair.Value.Title.Parent == tile); var module = pair.Value;
                DownloadsVisual.Card(tile);
                if (!moduleIcons.TryGetValue(tile, out var icon)) { icon = new AppVectorIcon(AppIconKind.Media, 28, AppThemeTokens.Info); moduleIcons.Add(tile, icon); tile.Controls.Add(icon); }
                icon.Visible = true; icon.SetBounds(16, 18, 28, 28);
                AppTypography.Bind(module.Title, 11, FontStyle.Bold); module.Title.SetBounds(58, 12, itemWidth - 104, AppTypography.TextHeight(28));
                module.Description.Multiline = true; module.Description.SetBounds(58, module.Title.Bottom + 4, itemWidth - 76, DashboardVisual.LabelHeight(module.Description, itemWidth - 76, 3));
                module.Open.Text = ""; module.Open.AccessibleName = services.Localization["Other.Open"] + " — " + module.Title.Text;
                module.Open.SetBounds(itemWidth - 38, 12, 28, AppTypography.TextHeight(30)); DownloadsVisual.Button(module.Open); DashboardVisual.Icon(module.Open, AppIconKind.ChevronRight, true);
            }
        }
        links.Height = tiles.Select(tile => tile.Bottom).DefaultIfEmpty(toolsTitle.Bottom).Max();
        var rightX = inline ? tools.Right + 12 : heading.Left; var rightWidth = inline ? width - left - 12 : width;
        recent.SetBounds(rightX, inline ? tools.Top : links.Bottom + 16, rightWidth, recent.Height); DownloadsVisual.Card(recent);
        var recentTop = 16;
        foreach (var child in recent.Controls)
        {
            var childWidth = Math.Max(1, rightWidth - 48);
            if (child is Button button)
            {
                DownloadsVisual.Button(button); button.AutoEllipsis = true;
                if (button.Tag is RecentToolRow row)
                {
                    row.Icon.SetBounds(10, 16, 24, 24);
                    row.Title.SetBounds(44, 8, childWidth - 56, AppTypography.TextHeight(22));
                    row.Subtitle.SetBounds(44, row.Title.Bottom + 3, childWidth - 56, AppTypography.TextHeight(20));
                    AppTypography.CaptureRecipes(button).Invoke();
                }
                else button.AccessibleName = button.Text;
            }
            var childHeight = child is Label label ? DashboardVisual.LabelHeight(label, childWidth, 4) : AppTypography.TextHeight(62);
            child.SetBounds(24, recentTop, childWidth, childHeight); recentTop = child.Bottom + 10;
        }
        recent.Height = recentTop + 10;
        if (quick is null) return;
        quick.SetBounds(rightX, recent.Bottom + 16, rightWidth, 1);
        quickTitle!.Text = services.Localization["Dashboard.QuickActions"]; quickTitle.SetBounds(16, 14, rightWidth - 32, AppTypography.TextHeight(30));
        DownloadsVisual.Card(quick);
        var quickWidth = (rightWidth - 44) / 2; var quickHeight = AppTypography.TextHeight(88);
        for (var i = 0; i < quickButtons.Count; i++)
        {
            var button = quickButtons[i]; button.Text = ""; button.AccessibleName = services.Localization[(string)button.Tag!];
            button.SetBounds(16 + (i % 2) * (quickWidth + 12), quickTitle.Bottom + 12 + (i / 2) * (quickHeight + 12), quickWidth, quickHeight);
            DashboardVisual.Button(button);
            button.Controls[0].SetBounds((button.Width - 24) / 2, 12, 24, 24);
            button.Controls[1].Text = button.AccessibleName; button.Controls[1].SetBounds(10, 42, button.Width - 20, quickHeight - 48);
        }
        quickStatus!.Visible = !string.IsNullOrEmpty(quickStatus.Text);
        quickStatus.SetBounds(16, quickButtons.Max(button => button.Bottom) + 10, rightWidth - 32,
            quickStatus.Visible ? DashboardVisual.LabelHeight(quickStatus, rightWidth - 32, 3) : 0);
        quick.Height = quickStatus.Bottom + 12;
        tip!.SetBounds(rightX, quick.Bottom + 16, rightWidth, 1);
        tipTitle!.Text = services.Localization["Other.TipTitle"]; tipTitle.SetBounds(16, 14, rightWidth - 32, AppTypography.TextHeight(26));
        tipText!.Text = services.Localization["Other.TipText"]; tipText.SetBounds(16, tipTitle.Bottom + 10, rightWidth - 32, DashboardVisual.LabelHeight(tipText, rightWidth - 32, 5));
        tip.Height = tipText.Bottom + 16;
    }
}
