using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class WhatsNewForm : Form
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 22), version = AppUi.Muted(), intro = AppUi.Muted(), notice = AppUi.Muted(), error = AppUi.Muted();
    private readonly Panel header = new();
    private readonly AppVectorIcon headerIcon = new(AppIconKind.Info, 36, AppThemeTokens.Info);
    private readonly ExperienceScrollPanel body = new() { AutoScroll = true };
    private readonly Panel surface = new() { Dock = DockStyle.Fill };
    private readonly Button done = new DashboardButton(), later = new DashboardButton(), appearance = new DashboardButton(), updates = new DashboardButton(), releases = new DashboardButton();
    private readonly List<(Panel Panel, Label Title, Label Text, AppVectorIcon Icon)> cards = [];
    private bool shown, acknowledging, closeAllowed, closeWhenIdle, arrangingCards;
    public string? Destination { get; private set; }

    internal WhatsNewForm(AppServices services)
    {
        this.services = services; var text = services.Localization;
        Text = text["Experience.WhatsNew"]; AccessibilityObject.AutomationId = "WhatsNewWindow";
        AppBranding.Apply(this); Size = new(1040, 820); MinimumSize = new(720, 560);
        Controls.Add(surface); ExperienceVisual.Surface(surface); ExperienceVisual.Surface(body); AppTypography.SetDashboardPresentation(surface, true);
        ExperienceVisual.ApplyWindow(this); DownloadsVisual.Card(header);
        heading.Text = Text; version.Text = "v" + services.Experience.Version; intro.Text = text["Experience.NewsIntroduction"];
        notice.Text = text[services.Experience.IsCandidate ? "Experience.Candidate" : "Experience.Release"];
        heading.Multiline = version.Multiline = intro.Multiline = notice.Multiline = error.Multiline = true; AppUi.BindForeground(error, AppThemeTokens.Error);
        AppUi.BindForeground(version, AppThemeTokens.Info); AppTypography.Bind(version, 10, FontStyle.Bold);
        surface.Controls.AddRange([body, error, later, done]); body.Controls.Add(header); header.Controls.AddRange([headerIcon, heading, version, intro, notice]);
        for (var i = 0; i < ReleaseNotesCatalog.Cards.Length; i++)
        {
            var key = ReleaseNotesCatalog.Cards[i]; var card = new Panel(); DownloadsVisual.Card(card);
            var title = AppUi.Heading(text[key], 14); var description = AppUi.Muted(text[key + "Body"]); description.Multiline = true;
            var icon = new AppVectorIcon(new[] { AppIconKind.Queue, AppIconKind.History, AppIconKind.Settings }[i], 28, AppThemeTokens.Info);
            card.Controls.AddRange([icon, title, description]); body.Controls.Add(card); cards.Add((card, title, description, icon));
            AppTypography.Bind(description, 10.5f);
        }
        foreach (var (button, key, id) in new[] { (appearance, "Experience.Appearance", "WhatsNewAppearance"), (updates, "Experience.UpdatePreferences", "WhatsNewUpdates"), (releases, "Experience.ReleaseNotes", "WhatsNewReleases"), (done, "Experience.GotIt", "WhatsNewDone"), (later, "Experience.Later", "WhatsNewLater") })
        { button.Text = text[key]; button.AccessibleAutomationId = id; ExperienceVisual.Action(button, button == done); }
        body.Controls.AddRange([appearance, updates, releases]);
        appearance.Click += async (_, _) => await AcknowledgeAsync("Personalization");
        updates.Click += async (_, _) => await AcknowledgeAsync("Updates");
        done.Click += async (_, _) => await AcknowledgeAsync(null);
        later.Click += (_, _) => Close(); // Deliberately leave acknowledgement unchanged, exactly like closing with X.
        releases.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(ReleaseNotesCatalog.OfficialUrl) { UseShellExecute = true }); }
            catch (Exception ex) { services.Logger.Error("Opening official release notes failed.", ex); error.Text = text["Experience.LinkError"]; LayoutPage(); }
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
        acknowledging = true; done.Enabled = later.Enabled = appearance.Enabled = updates.Enabled = false;
        try { await services.Experience.AcknowledgeWhatsNewAsync(); Destination = destination; closeAllowed = true; Close(); }
        catch (Exception ex) { services.Logger.Error("Release-note acknowledgement could not be saved.", ex); error.Text = services.Localization["Experience.SaveError"]; LayoutPage(); }
        finally { acknowledging = false; done.Enabled = later.Enabled = appearance.Enabled = updates.Enabled = true; if (closeWhenIdle) { closeAllowed = true; Close(); } }
    }

    private void LayoutPage()
    {
        var width = Math.Max(1, ClientSize.Width - 48);
        var doneWidth = Math.Min(width * 3 / 5, DashboardVisual.ActionWidth(done));
        done.SetBounds(24 + width - doneWidth, ClientSize.Height - AppTypography.TextHeight(42) - 24, doneWidth, AppTypography.TextHeight(42));
        later.SetBounds(24, done.Top, Math.Min(Math.Max(1, done.Left - 36), DashboardVisual.ActionWidth(later)), done.Height);
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
        var headerTextWidth = Math.Max(1, width - 90);
        headerIcon.SetBounds(18, 24, 36, 36);
        version.SetBounds(70, 16, headerTextWidth, DashboardVisual.LabelHeight(version, headerTextWidth, 5));
        heading.SetBounds(70, version.Bottom + 6, headerTextWidth, DashboardVisual.LabelHeight(heading, headerTextWidth, 8));
        intro.SetBounds(18, heading.Bottom + 12, Math.Max(1, width - 36), DashboardVisual.LabelHeight(intro, Math.Max(1, width - 36), 10));
        notice.SetBounds(18, intro.Bottom + 10, Math.Max(1, width - 36), DashboardVisual.LabelHeight(notice, Math.Max(1, width - 36), 16));
        header.SetBounds(viewport.X, viewport.Y, width, notice.Bottom + 18);
        var y = header.Bottom + 14;
        foreach (var (panel, title, description, icon) in cards)
        {
            icon.SetBounds(16, 18, 28, 28); title.Multiline = true;
            title.SetBounds(56, 14, Math.Max(1, width - 72), DashboardVisual.LabelHeight(title, Math.Max(1, width - 72), 3));
            description.SetBounds(56, title.Bottom + 8, Math.Max(1, width - 72), DashboardVisual.LabelHeight(description, Math.Max(1, width - 72), 30));
            panel.SetBounds(viewport.X, y, width, description.Bottom + 18); y = panel.Bottom + 12;
        }
        var x = viewport.X; var line = AppTypography.TextHeight(42);
        foreach (var button in new[] { appearance, updates, releases })
        {
            var desiredWidth = Math.Min(width, DashboardVisual.ActionWidth(button));
            if (x > viewport.X && x + desiredWidth > viewport.X + width) { x = viewport.X; y += line + 10; }
            button.SetBounds(x, y, desiredWidth, line); x = button.Right + 10;
        }
    }
}
