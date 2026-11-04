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
    private Label? quickTitle;
    private readonly List<Button> quickButtons = [];
    private DashboardDropZone? importDropZone;
    private Label? importDropTitle, importDropHint;

    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value)
        {
            AutoScroll = false;
            foreach (var module in modules.Values)
            {
                AppUi.Secondary(module.Open); AppTypography.Bind(module.Title, 16, FontStyle.Bold);
                module.Description.Multiline = false;
            }
            foreach (var icon in moduleIcons.Values) icon.Visible = false;
            foreach (var workflow in moduleWorkflows.Values) workflow.Visible = false;
            if (importDropZone is not null) importDropZone.Visible = false;
            ApplyLocalization();
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
            Controls.Add(links);
            quick = new Panel(); DashboardStyle.Card(quick); quickTitle = AppUi.Heading("", 13); quick.Controls.Add(quickTitle);
            foreach (var key in new[] { "Common.OpenFolder", "Settings.OpenLogs", "Appearance.Export" })
            {
                var button = new DashboardButton { Tag = key, AccessibleAutomationId = "OtherQuick" + key.Replace(".", "") };
                var icon = new AppVectorIcon(key == "Appearance.Export" ? AppIconKind.Download : AppIconKind.Folder, 24);
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
                    if (key == "Common.OpenFolder") Utilities.ShellService.OpenFolderForFile(services.Settings.Current.FinalOutputDirectory);
                    else if (key == "Settings.OpenLogs") Utilities.ShellService.OpenFolderForFile(services.Paths.LogDirectory);
                    else if (FindForm() is { } form)
                    {
                        var dialog = new SaveFileDialog { FileName = "appearance.json", DefaultExtension = "json" }; dialog.AddFilter("JSON", "*.json");
                        if (await dialog.ShowDialog(form) == DialogResult.OK) await File.WriteAllTextAsync(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(services.Appearance.Profile, AppearanceProfileStore.Options));
                    }
                };
                quickButtons.Add(button); quick.Controls.Add(button);
            }
            Controls.Add(quick);
        }
        if (quick is not null) quick.Visible = value;
        if (links is not null) links.Visible = value;
        recent.Parent?.Controls.Remove(recent);
        if (value) Controls.Add(recent); else tools.Controls.Add(recent);
        LayoutPage();
    }

    private void LayoutDashboard()
    {
        AutoScroll = true; var origin = DisplayRectangle.Location; var width = Math.Max(1, DisplayRectangle.Width - 36);
        heading.SetBounds(18 + origin.X, 20 + origin.Y, width, AppTypography.TextHeight(48));
        subtitle.SetBounds(heading.Left, heading.Bottom + 4, width, AppTypography.TextHeight(28));
        var inline = width >= AppTypography.TextHeight(960); var left = inline ? width * 2 / 3 - 12 : width;
        tools.SetBounds(heading.Left, subtitle.Bottom + 18, left, 1); AppUi.BindBackground(tools);
        var toolHeight = 0;
        foreach (var (id, module) in modules)
        {
            if (module.Title.Parent is not { } card) continue;
            DashboardStyle.Card(card); card.Width = left - 2;
            if (!moduleIcons.TryGetValue(card, out var icon))
            {
                icon = new AppVectorIcon(AppIconKind.Clipboard, 36, AppThemeTokens.Info) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
                moduleIcons.Add(card, icon); card.Controls.Add(icon);
            }
            icon.Visible = true; icon.SetBounds(24, 24, 36, 36);
            icon.SetKind(id == "Batch" ? AppIconKind.Clipboard : AppIconKind.Media);
            AppTypography.Bind(module.Title, 14, FontStyle.Bold);
            module.Title.SetBounds(80, 20, card.Width - 104, AppTypography.TextHeight(36));
            module.Description.Multiline = true;
            var descriptionStyle = module.Description.CurrentStyle;
            var descriptionSize = TextMeasurer.MeasureText(module.Description.Text, descriptionStyle.GetFont(), descriptionStyle.GetFontSize(), new System.Drawing.Size(card.Width - 104, int.MaxValue), descriptionStyle.GetFontStyle());
            module.Description.SetBounds(80, module.Title.Bottom + 6, card.Width - 104, Math.Max(AppTypography.TextHeight(24), (int)Math.Ceiling(descriptionSize.Height) + 4));
            if (id != "Batch")
            {
                module.Open.Text = services.Localization["Other.Open"];
                DashboardVisual.Button(module.Open); DashboardVisual.Icon(module.Open, AppIconKind.Media);
                var openWidth = Math.Min(card.Width - 104, DashboardVisual.ActionWidth(module.Open, true));
                module.Open.SetBounds(card.Width - 24 - openWidth, module.Description.Bottom + 12, openWidth, AppTypography.TextHeight(36));
                card.Height = module.Open.Bottom + 18; toolHeight += card.Height + card.Margin.Bottom;
                continue;
            }
            if (importDropZone is null)
            {
                importDropZone = new(); importDropTitle = AppUi.Heading("", 12); importDropHint = AppUi.Muted();
                importDropTitle.TextAlign = importDropHint.TextAlign = ContentAlignment.MiddleCenter;
                importDropHint.Multiline = true;
                importDropZone.Controls.AddRange([importDropTitle, importDropHint]); card.Controls.Add(importDropZone);
            }
            importDropZone.Visible = true;
            importDropZone.SetBounds(24, module.Description.Bottom + 16, card.Width - 48, AppTypography.TextHeight(110));
            importDropTitle!.Text = services.Localization["Dashboard.DropFile"];
            importDropHint!.Text = services.Localization["Dashboard.DropFormats"];
            importDropTitle.SetBounds(12, 20, importDropZone.Width - 24, AppTypography.TextHeight(28));
            importDropHint.SetBounds(12, importDropTitle.Bottom + 6, importDropZone.Width - 24, DashboardVisual.LabelHeight(importDropHint, importDropZone.Width - 24, 2));
            if (!moduleWorkflows.TryGetValue(card, out var workflow))
            {
                workflow = new Panel(); DashboardVisual.Flat(workflow); AppUi.BindBackground(workflow, AppThemeTokens.SurfaceSecondary);
                foreach (var key in new[] { "Batch.Browse", "Batch.Analyze", "Batch.Add" })
                {
                    var step = AppUi.Muted(); step.Multiline = true; step.Tag = key; workflow.Controls.Add(step);
                }
                moduleWorkflows.Add(card, workflow); card.Controls.Add(workflow);
            }
            workflow.Visible = true; workflow.SetBounds(24, importDropZone.Bottom + 12, card.Width - 48, 1);
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
            module.Open.SetBounds(card.Width - 24 - actionWidth, workflow.Bottom + 20, actionWidth, AppTypography.TextHeight(44));
            DashboardVisual.Icon(module.Open, AppIconKind.Clipboard);
            card.Height = module.Open.Bottom + 28; toolHeight += card.Height + card.Margin.Bottom;
        }
        tools.Height = Math.Max(AppTypography.TextHeight(220), toolHeight);
        links!.SetBounds(tools.Left, tools.Bottom + 12, left, 1);
        var linkColumns = Math.Clamp(left / AppTypography.TextHeight(330), 1, 2);
        var itemWidth = (left - (linkColumns - 1) * 12) / linkColumns;
        var tileHeight = AppTypography.TextHeight(112);
        for (var i = 0; i < linkActions.Count; i++)
        {
            var button = linkActions[i]; button.AccessibleName = services.Localization["Nav." + button.Tag]; button.Text = "";
            button.SetBounds((i % linkColumns) * (itemWidth + 12), (i / linkColumns) * (tileHeight + 12), itemWidth, tileHeight); DashboardVisual.Button(button);
            button.Controls[0].SetBounds(18, 18, 28, 28);
            button.Controls[1].Text = button.AccessibleName; button.Controls[1].SetBounds(62, 14, itemWidth - 80, AppTypography.TextHeight(28));
            button.Controls[2].Text = services.Localization["Dashboard.Route" + button.Tag];
            button.Controls[2].SetBounds(62, button.Controls[1].Bottom + 4, itemWidth - 80,
                DashboardVisual.LabelHeight((Label)button.Controls[2], itemWidth - 80, 3));
        }
        links.Height = linkActions.Max(button => button.Bottom);
        var rightX = inline ? tools.Right + 12 : heading.Left; var rightWidth = inline ? width - left - 12 : width;
        recent.SetBounds(rightX, inline ? tools.Top : links.Bottom + 16, rightWidth, recent.Height); DashboardStyle.Card(recent);
        var recentTop = 16;
        foreach (var child in recent.Controls)
        {
            var childWidth = Math.Max(1, rightWidth - 48);
            if (child is Button button) { DashboardVisual.Button(button); button.AutoEllipsis = true; button.AccessibleName = button.Text; }
            var childHeight = child is Label label ? DashboardVisual.LabelHeight(label, childWidth, 4) : AppTypography.TextHeight(38);
            child.SetBounds(24, recentTop, childWidth, childHeight); recentTop = child.Bottom + 10;
        }
        recent.Height = recentTop + 10;
        if (quick is null) return;
        quick.SetBounds(rightX, recent.Bottom + 16, rightWidth, 1);
        quickTitle!.Text = services.Localization["Dashboard.QuickActions"]; quickTitle.SetBounds(16, 14, rightWidth - 32, AppTypography.TextHeight(30));
        var quickWidth = (rightWidth - 44) / 2; var quickHeight = AppTypography.TextHeight(104);
        for (var i = 0; i < quickButtons.Count; i++)
        {
            var button = quickButtons[i]; button.Text = ""; button.AccessibleName = services.Localization[(string)button.Tag!];
            button.SetBounds(16 + (i % 2) * (quickWidth + 12), quickTitle.Bottom + 12 + (i / 2) * (quickHeight + 12), i == 2 ? rightWidth - 32 : quickWidth, quickHeight);
            DashboardVisual.Button(button);
            button.Controls[0].SetBounds((button.Width - 24) / 2, 16, 24, 24);
            button.Controls[1].Text = button.AccessibleName; button.Controls[1].SetBounds(12, 48, button.Width - 24, quickHeight - 56);
        }
        quick.Height = quickButtons.Max(button => button.Bottom) + 16;
    }
}
