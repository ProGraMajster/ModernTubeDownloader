using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class OtherView
{
    private bool dashboard;
    private Panel? quick, importArea, links;
    private Button? importLaunch;
    private AppVectorIcon? importIcon;
    private readonly List<Button> linkActions = [];
    public event EventHandler<ModernTubeDownloader.Shell.AppPageId>? NavigateRequested;
    private Label? quickTitle;
    private readonly List<Button> quickButtons = [];
    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value) { AutoScroll = false; foreach (var module in modules.Values) { AppUi.Secondary(module.Open); AppTypography.Bind(module.Title, 16, FontStyle.Bold); } }
        if (value && quick is null)
        {
            importArea = new Panel(); importArea.Style.Border.Width = 1; AppUi.BindBorder(importArea, AppThemeTokens.Info); AppUi.BindBackground(importArea, AppThemeTokens.SurfaceSecondary);
            importLaunch = new Button { AccessibleAutomationId = "OtherImportLaunch" }; importIcon = new AppVectorIcon(AppIconKind.Clipboard, 48, AppThemeTokens.Info);
            importLaunch.Click += (_, _) => modules.Values.FirstOrDefault().Open?.PerformClick(); importArea.Controls.AddRange([importIcon, importLaunch]);
            links = new Panel(); AppUi.BindBackground(links);
            foreach (var page in new[] { ModernTubeDownloader.Shell.AppPageId.History, ModernTubeDownloader.Shell.AppPageId.Settings, ModernTubeDownloader.Shell.AppPageId.Help })
            {
                var button = new Button { Tag = page, AccessibleAutomationId = "OtherNavigate" + page };
                button.Click += (_, _) => NavigateRequested?.Invoke(this, page);
                var icon = new AppVectorIcon(page == ModernTubeDownloader.Shell.AppPageId.History ? AppIconKind.History : page == ModernTubeDownloader.Shell.AppPageId.Settings ? AppIconKind.Settings : AppIconKind.Help, 26, AppThemeTokens.Info);
                var title = AppUi.Heading("", 11); var description = AppUi.Muted(); description.Multiline = true;
                foreach (var child in new Control[] { icon,title,description }) { child.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; child.Click += (_,_) => button.PerformClick(); }
                button.Controls.AddRange([icon,title,description]); linkActions.Add(button); links.Controls.Add(button);
            }
            Controls.AddRange([importArea, links]);
            quick = new Panel(); DashboardStyle.Card(quick); quickTitle = AppUi.Heading("", 13); quick.Controls.Add(quickTitle);
            foreach (var key in new[] { "Common.OpenFolder", "Settings.OpenLogs", "Appearance.Export" })
            {
                var button = new Button { Tag = key, AccessibleAutomationId = "OtherQuick" + key.Replace(".", "") }; AppUi.Secondary(button);
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
        if (quick is not null) quick.Visible = value; if (importArea is not null) importArea.Visible = value; if (links is not null) links.Visible = value;
        recent.Parent?.Controls.Remove(recent);
        if (value) Controls.Add(recent); else tools.Controls.Add(recent);
        LayoutPage();
    }
    private void LayoutDashboard()
    {
        AutoScroll = true; var origin = DisplayRectangle.Location; var width = Math.Max(560, ClientSize.Width - 36);
        heading.SetBounds(18 + origin.X, 20 + origin.Y, width, AppTypography.TextHeight(48));
        subtitle.SetBounds(18 + origin.X, heading.Bottom + 6, width, AppTypography.TextHeight(44));
        var inline = width >= AppTypography.TextHeight(960); var left = inline ? width * 2 / 3 - 12 : width;
        tools.SetBounds(18 + origin.X, subtitle.Bottom + 18, left, AppTypography.TextHeight(330));
        AppUi.BindBackground(tools);
        foreach (var module in modules.Values)
        {
            if (module.Title.Parent is not { } card) continue;
            DashboardStyle.Card(card); card.Width = left - 12; card.Height = AppTypography.TextHeight(360);
            AppTypography.Bind(module.Title, 12, FontStyle.Bold); module.Title.SetBounds(22, 24, card.Width - 44, AppTypography.TextHeight(38));
            module.Description.SetBounds(22, module.Title.Bottom + 16, card.Width - 44, AppTypography.TextHeight(64)); module.Description.Multiline = true;
            module.Open.SetBounds(22, AppTypography.TextHeight(300), Math.Min(card.Width - 44, AppTypography.TextHeight(280)), AppTypography.TextHeight(44)); AppUi.Primary(module.Open);
        }
        importArea!.SetBounds(tools.Left + 20, tools.Top + AppTypography.TextHeight(130), left - 52, AppTypography.TextHeight(142));
        importIcon!.SetBounds(20, 20, 48, 48); importLaunch!.Text = services.Localization["Dashboard.OpenBatch"];
        importLaunch.SetBounds(88, 14, importArea.Width - 104, importArea.Height - 28); DashboardVisual.Button(importLaunch);
        tools.Height = AppTypography.TextHeight(390);
        links!.SetBounds(tools.Left, tools.Bottom + 12, left, AppTypography.TextHeight(220));
        var itemWidth = (left - 24) / 2;
        for (var i = 0; i < linkActions.Count; i++)
        {
            var button = linkActions[i]; button.AccessibleName = services.Localization["Nav." + button.Tag]; button.Text = "";
            button.SetBounds((i % 2) * (itemWidth + 12), (i / 2) * AppTypography.TextHeight(110), itemWidth, AppTypography.TextHeight(98)); DashboardVisual.Button(button);
            button.Controls[0].SetBounds(16, 22, 26, 26);
            button.Controls[1].Text = button.AccessibleName; button.Controls[1].SetBounds(56, 12, itemWidth - 72, AppTypography.TextHeight(28));
            button.Controls[2].Text = services.Localization["Dashboard.Route" + button.Tag]; button.Controls[2].SetBounds(56, button.Controls[1].Bottom + 4, itemWidth - 72, AppTypography.TextHeight(44));
        }
        var rightX = inline ? tools.Right + 12 : 18 + origin.X; var rightWidth = inline ? width - left - 12 : width;
        recent.SetBounds(rightX, inline ? tools.Top : links.Bottom + 12, rightWidth, recent.Height);
        foreach (var label in recent.Controls.OfType<Label>()) label.Width = Math.Max(1, rightWidth - 48);
        DashboardStyle.Card(recent); foreach (var child in recent.Controls) child.Width = rightWidth - 48;
        if (quick is null) return;
        quick.SetBounds(rightX, recent.Bottom + 16, rightWidth, AppTypography.TextHeight(230));
        quickTitle!.Text = services.Localization["Dashboard.QuickActions"]; quickTitle.SetBounds(16, 14, rightWidth - 32, AppTypography.TextHeight(30));
        var y = quickTitle.Bottom + 12;
        foreach (var button in quickButtons) { button.Text = services.Localization[(string)button.Tag!]; button.SetBounds(16, y, rightWidth - 32, AppTypography.TextHeight(48)); DashboardVisual.Button(button); y = button.Bottom + 10; }
        quick.Height = y + 8;
    }
}
