using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class InformationView
{
    private readonly ExperienceScrollPanel helpScroll = new() { AutoScroll = true, Visible = false, AccessibleAutomationId = "HelpTopics" };
    private readonly Label helpTitle = AppUi.Heading("", 24), helpIntro = AppUi.Muted(), helpFallback = AppUi.Muted(), helpError = AppUi.Muted();
    private readonly List<(string Id, Panel Panel, Label Title, Label Body, AppVectorIcon Icon, List<(Button Button, string Key)> Actions)> helpCards = [];
    private bool dashboardHelp, arrangingHelp;
    public event EventHandler<AppPageId>? NavigateRequested;
    public event EventHandler<string>? SettingsRequested;
    public event EventHandler? BatchRequested;
    public event EventHandler? OrganizerRequested;

    private void InitializeHelp()
    {
        helpTitle.Multiline = helpIntro.Multiline = helpFallback.Multiline = helpError.Multiline = true;
        helpFallback.AccessibleAutomationId = "HelpLanguageFallback"; helpError.AccessibleAutomationId = "HelpError";
        AppUi.BindForeground(helpError, AppThemeTokens.Error);
        Controls.Add(helpScroll); ExperienceVisual.Surface(helpScroll);
        helpScroll.Controls.AddRange([helpTitle, helpIntro, helpFallback, helpError]);
        foreach (var topic in HelpCatalog.ForLanguage("en").Topics)
        {
            var panel = new Panel { AccessibleAutomationId = "HelpTopic-" + topic.Id }; DownloadsVisual.Card(panel);
            var heading = AppUi.Heading("", 14); var paragraph = AppUi.Muted(); heading.Multiline = paragraph.Multiline = true;
            var kind = topic.Id switch { "start" => AppIconKind.Play, "downloads" => AppIconKind.Download, "live" => AppIconKind.Live, "batch" => AppIconKind.Queue, "settings" => AppIconKind.Settings, _ => AppIconKind.Help };
            var topicIcon = new AppVectorIcon(kind, 28, AppThemeTokens.Info);
            panel.Controls.AddRange([topicIcon, heading, paragraph]); helpScroll.Controls.Add(panel);
            var actions = new List<(Button Button, string Key)>();
            void Action(string id, string key, Action invoked)
            {
                var action = new DashboardButton { AccessibleAutomationId = id }; ExperienceVisual.Action(action);
                action.Click += (_, _) => invoked(); panel.Controls.Add(action); actions.Add((action, key));
            }
            switch (topic.Id)
            {
                case "start":
                    Action("HelpSetup", "Experience.Reopen", () => SetupRequested?.Invoke(this, EventArgs.Empty));
                    Action("HelpWhatsNew", "Experience.WhatsNew", () => WhatsNewRequested?.Invoke(this, EventArgs.Empty)); break;
                case "downloads":
                    Action("HelpDownloads", "Nav.Downloads", () => NavigateRequested?.Invoke(this, AppPageId.Downloads));
                    Action("HelpHistory", "Nav.History", () => NavigateRequested?.Invoke(this, AppPageId.History)); break;
                case "live": Action("HelpLive", "Nav.Live", () => NavigateRequested?.Invoke(this, AppPageId.Live)); break;
                case "batch":
                    Action("HelpBatch", "Dashboard.Batch", () => BatchRequested?.Invoke(this, EventArgs.Empty));
                    Action("HelpOrganizer", "Organizer.Title", () => OrganizerRequested?.Invoke(this, EventArgs.Empty)); break;
                case "settings":
                    Action("HelpSettings", "Nav.Settings", () => NavigateRequested?.Invoke(this, AppPageId.Settings));
                    Action("HelpUpdates", "Experience.UpdatePreferences", () => SettingsRequested?.Invoke(this, "Updates")); break;
                case "diagnostics":
                    Action("HelpLogs", "Settings.OpenLogs", () => OpenHelpResource(() => Utilities.ShellService.OpenFolderForFile(services.Paths.LogDirectory)));
                    Action("HelpDocumentation", "Dashboard.Repository", () => OpenHelpResource(() => Process.Start(new ProcessStartInfo("https://github.com/ProGraMajster/ModernTubeDownloader#readme") { UseShellExecute = true }))); break;
            }
            helpCards.Add((topic.Id, panel, heading, paragraph, topicIcon, actions));
        }
        helpScroll.ViewportChanged += (_, _) => LayoutHelp(); helpScroll.SizeChanged += (_, _) => LayoutHelp();
    }

    internal void SetDashboard(bool enabled)
    {
        if (about) return;
        dashboardHelp = enabled;
        foreach (var control in new Control[] { icon, title, body, repository, diagnostics, setup, whatsNew }) control.Visible = !enabled;
        helpScroll.Visible = enabled; AutoScroll = !enabled;
        if (enabled)
        {
            ExperienceVisual.Surface(helpScroll);
            foreach (var card in helpCards) { DownloadsVisual.Card(card.Panel); foreach (var action in card.Actions) ExperienceVisual.Action(action.Button); }
        }
        LayoutPage();
    }

    private void OpenHelpResource(Action action)
    {
        try { action(); helpError.Text = ""; }
        catch (Exception error) { services.Logger.Error("Opening help resource failed.", error); helpError.Text = services.Localization["Experience.LinkError"]; }
        LayoutHelp();
    }

    private void RefreshHelp()
    {
        var text = services.Localization; var content = HelpCatalog.ForLanguage(text.Language);
        helpTitle.Text = text["Nav.Help"]; helpIntro.Text = text["Help.Introduction"];
        helpFallback.Text = content.IsLanguageFallback ? text["Help.EnglishContentFallback"] : "";
        foreach (var card in helpCards)
        {
            var topic = content.Topics.Single(topic => topic.Id == card.Id);
            card.Title.Text = topic.Title; card.Body.Text = topic.Body;
            foreach (var action in card.Actions) action.Button.Text = text[action.Key];
        }
    }

    private void LayoutHelp()
    {
        if (arrangingHelp || !dashboardHelp || about) return;
        arrangingHelp = true;
        try
        {
            helpScroll.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
            for (var pass = 0; pass < 3; pass++)
            {
                var viewport = helpScroll.DisplayRectangle; var width = Math.Max(1, viewport.Width - 40); var x = viewport.X + 20; var y = viewport.Y + 20;
                helpTitle.SetBounds(x, y, width, DashboardVisual.LabelHeight(helpTitle, width, 6));
                helpIntro.SetBounds(x, helpTitle.Bottom + 8, width, DashboardVisual.LabelHeight(helpIntro, width, 8));
                helpFallback.SetBounds(x, helpIntro.Bottom + 8, width, helpFallback.Text.Length > 0 ? DashboardVisual.LabelHeight(helpFallback, width, 12) : 0);
                helpError.SetBounds(x, helpFallback.Bottom + 6, width, helpError.Text.Length > 0 ? DashboardVisual.LabelHeight(helpError, width, 12) : 0);
                y = helpError.Bottom + 12;
                var columns = width >= AppTypography.TextHeight(900) ? 2 : 1; var cardWidth = (width - (columns - 1) * 14) / columns;
                for (var i = 0; i < helpCards.Count; i += columns)
                {
                    var height = 0;
                    for (var column = 0; column < columns && i + column < helpCards.Count; column++)
                    {
                        var card = helpCards[i + column]; var textWidth = Math.Max(1, cardWidth - 76);
                        card.Icon.SetBounds(16, 18, 28, 28);
                        card.Title.SetBounds(56, 16, textWidth, DashboardVisual.LabelHeight(card.Title, textWidth, 12));
                        card.Body.SetBounds(18, Math.Max(48, card.Title.Bottom) + 12, Math.Max(1, cardWidth - 36), DashboardVisual.LabelHeight(card.Body, Math.Max(1, cardWidth - 36), 100));
                        var actionX = 18; var actionY = card.Body.Bottom + 16; var line = AppTypography.TextHeight(42);
                        foreach (var action in card.Actions)
                        {
                            var desired = Math.Min(cardWidth - 36, DashboardVisual.ActionWidth(action.Button));
                            if (actionX > 18 && actionX + desired > cardWidth - 18) { actionX = 18; actionY += line + 8; }
                            action.Button.SetBounds(actionX, actionY, desired, line); actionX = action.Button.Right + 8;
                        }
                        card.Panel.SetBounds(x + column * (cardWidth + 14), y, cardWidth, actionY + line + 18);
                        height = Math.Max(height, card.Panel.Height);
                    }
                    for (var column = 0; column < columns && i + column < helpCards.Count; column++) helpCards[i + column].Panel.Height = height;
                    y += height + 14;
                }
                if (helpScroll.DisplayRectangle.Width == viewport.Width) break;
            }
        }
        finally { arrangingHelp = false; }
    }
}
