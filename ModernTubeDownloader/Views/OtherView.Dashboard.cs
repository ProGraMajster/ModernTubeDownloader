using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class OtherView
{
    private bool dashboard;
    private Panel? quick;
    private Label? quickTitle;
    private readonly List<Button> quickButtons = [];
    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value) { AutoScroll = false; foreach (var module in modules.Values) AppUi.Secondary(module.Open); }
        if (value && quick is null)
        {
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
        if (quick is not null) quick.Visible = value;
        recent.Parent?.Controls.Remove(recent);
        if (value) Controls.Add(recent); else tools.Controls.Add(recent);
        LayoutPage();
    }
    private void LayoutDashboard()
    {
        AutoScroll = true; var origin = DisplayRectangle.Location; var width = Math.Max(560, ClientSize.Width - 36);
        heading.SetBounds(18 + origin.X, 20 + origin.Y, width, AppTypography.TextHeight(40));
        subtitle.SetBounds(18 + origin.X, heading.Bottom + 6, width, AppTypography.TextHeight(44));
        var inline = width >= AppTypography.TextHeight(960); var left = inline ? width * 2 / 3 - 12 : width;
        tools.SetBounds(18 + origin.X, subtitle.Bottom + 18, left, AppTypography.TextHeight(330));
        AppUi.BindBackground(tools);
        foreach (var module in modules.Values)
        {
            if (module.Title.Parent is not { } card) continue;
            DashboardStyle.Card(card); card.Width = left - 12; card.Height = AppTypography.TextHeight(240);
            module.Title.SetBounds(22, 24, card.Width - 44, AppTypography.TextHeight(38));
            module.Description.SetBounds(22, module.Title.Bottom + 16, card.Width - 44, AppTypography.TextHeight(64)); module.Description.Multiline = true;
            module.Open.SetBounds(22, module.Description.Bottom + 20, Math.Min(card.Width - 44, AppTypography.TextHeight(280)), AppTypography.TextHeight(44)); AppUi.Primary(module.Open);
        }
        var rightX = inline ? tools.Right + 12 : 18 + origin.X; var rightWidth = inline ? width - left - 12 : width;
        recent.SetBounds(rightX, inline ? tools.Top : tools.Bottom + 12, rightWidth, recent.Height);
        foreach (var child in recent.Controls) child.Width = rightWidth - 48;
        if (quick is null) return;
        quick.SetBounds(rightX, recent.Bottom + 16, rightWidth, AppTypography.TextHeight(230));
        quickTitle!.Text = services.Localization["Dashboard.QuickActions"]; quickTitle.SetBounds(16, 14, rightWidth - 32, AppTypography.TextHeight(30));
        var y = quickTitle.Bottom + 12;
        foreach (var button in quickButtons) { button.Text = services.Localization[(string)button.Tag!]; button.SetBounds(16, y, rightWidth - 32, AppTypography.TextHeight(42)); y = button.Bottom + 10; }
        quick.Height = y + 8;
    }
}
