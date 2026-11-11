using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class WhatsNewForm : Form
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 24), version = AppUi.Muted(), notice = AppUi.Muted(), error = AppUi.Muted();
    private readonly ExperienceScrollPanel body = new() { AutoScroll = true };
    private readonly Panel surface = new() { Dock = DockStyle.Fill };
    private readonly Button done = new DashboardButton(), appearance = new DashboardButton(), updates = new DashboardButton(), releases = new DashboardButton();
    private readonly List<(Panel Panel, Label Title, Label Text, AppVectorIcon Icon)> cards = [];
    private bool shown, acknowledging, closeAllowed, closeWhenIdle, arrangingCards;
    public string? Destination { get; private set; }

    internal WhatsNewForm(AppServices services)
    {
        this.services = services; var text = services.Localization;
        Text = text["Experience.WhatsNew"]; AccessibilityObject.AutomationId = "WhatsNewWindow";
        AppBranding.Apply(this); Size = new(1040, 820); MinimumSize = new(720, 560);
        Controls.Add(surface); AppUi.BindBackground(surface); AppUi.BindBackground(body); AppTypography.SetDashboardPresentation(surface, true);
        heading.Text = Text; version.Text = "ModernTubeDownloader " + services.Experience.Version;
        notice.Text = text[services.Experience.IsCandidate ? "Experience.Candidate" : "Experience.Release"];
        heading.Multiline = version.Multiline = notice.Multiline = error.Multiline = true; AppUi.BindForeground(error, AppThemeTokens.Error);
        surface.Controls.AddRange([body, error, done]); body.Controls.AddRange([heading, version, notice]);
        for (var i = 0; i < ReleaseNotesCatalog.Cards.Length; i++)
        {
            var key = ReleaseNotesCatalog.Cards[i]; var card = new Panel(); DashboardStyle.Card(card);
            var title = AppUi.Heading(text[key], 15); var description = AppUi.Muted(text[key + "Body"]); description.Multiline = true;
            var icon = new AppVectorIcon(new[] { AppIconKind.Queue, AppIconKind.History, AppIconKind.Settings }[i], 28, AppThemeTokens.Info);
            card.Controls.AddRange([icon, title, description]); body.Controls.Add(card); cards.Add((card, title, description, icon));
            AppTypography.Bind(description, 11f);
        }
        foreach (var (button, key, id) in new[] { (appearance, "Experience.Appearance", "WhatsNewAppearance"), (updates, "Experience.UpdatePreferences", "WhatsNewUpdates"), (releases, "Experience.ReleaseNotes", "WhatsNewReleases"), (done, "Experience.GotIt", "WhatsNewDone") })
        { button.Text = text[key]; button.AccessibleAutomationId = id; DownloadsVisual.Button(button, button == done); }
        body.Controls.AddRange([appearance, updates, releases]);
        appearance.Click += async (_, _) => await AcknowledgeAsync("Personalization");
        updates.Click += async (_, _) => await AcknowledgeAsync("Updates");
        done.Click += async (_, _) => await AcknowledgeAsync(null);
        releases.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(ReleaseNotesCatalog.OfficialUrl) { UseShellExecute = true }); }
            catch (Exception ex) { services.Logger.Error("Opening official release notes failed.", ex); error.Text = text["Experience.LinkError"]; }
        };
        Shown += (_, _) => shown = true;
        Closing += (_, e) => { if (acknowledging && !closeAllowed) { e.Cancel = true; closeWhenIdle = true; } };
        SizeChanged += (_, _) => LayoutPage(); body.SizeChanged += (_, _) => LayoutCards();
        body.ViewportChanged += (_, _) => LayoutCards();
        surface.SizeChanged += (_, _) => LayoutPage(); AppTypography.RegisterLayout(surface, LayoutPage); AppTypography.CaptureRecipes(surface).Invoke(); LayoutPage();
    }

    private async Task AcknowledgeAsync(string? destination)
    {
        if (!shown || acknowledging) return;
        acknowledging = true; done.Enabled = appearance.Enabled = updates.Enabled = false;
        try { await services.Experience.AcknowledgeWhatsNewAsync(); Destination = destination; closeAllowed = true; Close(); }
        catch (Exception ex) { services.Logger.Error("Release-note acknowledgement could not be saved.", ex); error.Text = services.Localization["Experience.SaveError"]; LayoutPage(); }
        finally { acknowledging = false; done.Enabled = appearance.Enabled = updates.Enabled = true; if (closeWhenIdle) { closeAllowed = true; Close(); } }
    }

    private void LayoutPage()
    {
        var width = Math.Max(1, ClientSize.Width - 48);
        done.SetBounds(24, ClientSize.Height - AppTypography.TextHeight(42) - 24, width, AppTypography.TextHeight(42));
        var errorHeight = string.IsNullOrEmpty(error.Text) ? 0 : DashboardVisual.LabelHeight(error, width, 6);
        error.SetBounds(24, done.Top - errorHeight - 8, width, errorHeight);
        body.SetBounds(24, 18, width, Math.Max(1, error.Top - 30)); LayoutCards();
    }

    private void LayoutCards()
    {
        if (arrangingCards) return;
        arrangingCards = true;
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var width = body.DisplayRectangle.Width;
                ArrangeCards();
                if (body.DisplayRectangle.Width == width) break;
            }
        }
        finally { arrangingCards = false; }
    }
    private void ArrangeCards()
    {
        var viewport = body.DisplayRectangle; var width = Math.Max(1, viewport.Width - 10);
        heading.SetBounds(viewport.X, viewport.Y, width, DashboardVisual.LabelHeight(heading, width, 6));
        version.SetBounds(viewport.X, heading.Bottom + 8, width, DashboardVisual.LabelHeight(version, width, 6));
        notice.SetBounds(viewport.X, version.Bottom + 8, width, DashboardVisual.LabelHeight(notice, width, 10));
        var y = notice.Bottom + 16;
        foreach (var (panel, title, description, icon) in cards)
        {
            icon.SetBounds(16, 18, 28, 28); title.Multiline = true;
            title.SetBounds(56, 14, Math.Max(1, width - 72), DashboardVisual.LabelHeight(title, Math.Max(1, width - 72), 3));
            description.SetBounds(16, title.Bottom + 10, Math.Max(1, width - 32), DashboardVisual.LabelHeight(description, Math.Max(1, width - 32), 6));
            panel.SetBounds(viewport.X, y, width, description.Bottom + 18); y = panel.Bottom + 12;
        }
        foreach (var button in new[] { appearance, updates, releases })
        { button.SetBounds(viewport.X, y, width, AppTypography.TextHeight(42)); y = button.Bottom + 10; }
    }
}
