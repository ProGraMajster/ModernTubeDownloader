using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class WhatsNewForm : Form
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 22), version = AppUi.Muted(), intro = AppUi.Muted(), notice = AppUi.Muted(), provenance = AppUi.Muted(), fallback = AppUi.Muted(), error = AppUi.Muted();
    private readonly InstalledReleaseNotes? installedNotes;
    private readonly Uri? officialUrl;
    private readonly Panel header = new();
    private readonly AppVectorIcon headerIcon = new(AppIconKind.Info, 36, AppThemeTokens.Info);
    private readonly ExperienceScrollPanel body = new() { AutoScroll = true };
    private readonly Panel surface = new() { Dock = DockStyle.Fill };
    private readonly Button done = new DashboardButton(), later = new DashboardButton(), appearance = new DashboardButton(), updates = new DashboardButton(), releases = new DashboardButton(), refresh = new DashboardButton();
    private readonly List<(Panel Panel, Label Title, TextBox Text, AppVectorIcon Icon)> cards = [];
    private readonly CancellationTokenSource notesLifetime = new();
    private readonly string contentLanguage;
    private bool shown, acknowledging, closeAllowed, closeWhenIdle, arrangingCards, closed, disposed, refreshing;
    public string? Destination { get; private set; }

    internal WhatsNewForm(AppServices services)
    {
        this.services = services; var text = services.Localization;
        contentLanguage = text.Language;
        Text = text["Experience.WhatsNew"]; AccessibilityObject.AutomationId = "WhatsNewWindow";
        AppBranding.Apply(this); Size = new(1040, 820); MinimumSize = new(720, 560);
        Controls.Add(surface); ExperienceVisual.Surface(surface); ExperienceVisual.Surface(body); AppTypography.SetDashboardPresentation(surface, true);
        ExperienceVisual.ApplyWindow(this); DownloadsVisual.Card(header);
        installedNotes = ReleaseNotesCatalog.ForInstalled(services.Experience.Version, text.Language);
        officialUrl = ReleaseNotesCatalog.OfficialReleaseUrl(services.Experience.Version);
        heading.Text = Text; version.Text = text.Get("Experience.InstalledVersion", "v" + services.Experience.Version); intro.Text = text["Experience.NewsIntroduction"];
        notice.Text = text[services.Experience.IsCandidate ? "Experience.Candidate" : "Experience.Release"];
        provenance.Text = installedNotes is null ? text.Get("Experience.NoLocalNotes", services.Experience.Version) : text["Experience.LocalNotes"];
        fallback.Text = installedNotes?.IsLanguageFallback == true ? text["Experience.EnglishNotesFallback"] : "";
        provenance.AccessibleAutomationId = "WhatsNewProvenance"; fallback.AccessibleAutomationId = "WhatsNewLanguageFallback";
        heading.Multiline = version.Multiline = intro.Multiline = notice.Multiline = provenance.Multiline = fallback.Multiline = error.Multiline = true; AppUi.BindForeground(error, AppThemeTokens.Error);
        AppUi.BindForeground(version, AppThemeTokens.Info); AppTypography.Bind(version, 10, FontStyle.Bold);
        surface.Controls.AddRange([body, error, later, done]); body.Controls.Add(header); header.Controls.AddRange([headerIcon, heading, version, intro, notice, provenance, fallback]);
        foreach (var section in installedNotes?.Sections ?? [])
        {
            var card = new Panel { AccessibleAutomationId = "WhatsNewSection-" + section.Category }; DownloadsVisual.Card(card);
            var title = AppUi.Heading(section.Title, 14);
            var description = new AppThemeTextBox
            {
                Text = string.Join("\n\n", section.Items.Select(item => "• " + item)), ReadOnly = true, MultiLine = true,
                ScrollBars = ScrollBars.Vertical, TextAlign = ContentAlignment.TopLeft,
                MaxLength = GitHubReleaseNotesService.MaximumBodyBytes,
                AccessibleAutomationId = "WhatsNewText-" + section.Category, AccessibleName = section.Title
            };
            AppUi.Input(description);
            var kind = section.Category switch { "new" => AppIconKind.Plus, "improved" => AppIconKind.Queue, "fixed" => AppIconKind.Check, _ => AppIconKind.Info };
            var icon = new AppVectorIcon(kind, 28, AppThemeTokens.Info);
            card.Controls.AddRange([icon, title, description]); body.Controls.Add(card); cards.Add((card, title, description, icon));
            AppTypography.Bind(description, 10.5f);
        }
        foreach (var (button, key, id) in new[] { (appearance, "Experience.Appearance", "WhatsNewAppearance"), (updates, "Experience.UpdatePreferences", "WhatsNewUpdates"), (releases, "Experience.ReleaseNotes", "WhatsNewReleases"), (refresh, "Experience.RefreshNotes", "WhatsNewRefresh"), (done, "Experience.GotIt", "WhatsNewDone"), (later, "Experience.Later", "WhatsNewLater") })
        { button.Text = text[key]; button.AccessibleAutomationId = id; ExperienceVisual.Action(button, button == done); }
        releases.Text = text.Get("Experience.ExactReleaseNotes", services.Experience.Version); releases.Enabled = officialUrl is not null;
        body.Controls.AddRange([appearance, updates, releases, refresh]);
        refresh.Enabled = officialUrl is not null;
        refresh.Click += async (_, _) => await RefreshNotesAsync(true);
        appearance.Click += async (_, _) => await AcknowledgeAsync("Personalization");
        updates.Click += async (_, _) => await AcknowledgeAsync("Updates");
        done.Click += async (_, _) => await AcknowledgeAsync(null);
        later.Click += (_, _) => Close(); // Deliberately leave acknowledgement unchanged, exactly like closing with X.
        releases.Click += (_, _) =>
        {
            if (officialUrl is null) return;
            try { Process.Start(new ProcessStartInfo(officialUrl.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { services.Logger.Error("Opening official release notes failed.", ex); error.Text = text["Experience.LinkError"]; LayoutPage(); }
        };
        Shown += (_, _) => { shown = true; _ = RefreshNotesAsync(false); };
        Closing += (_, e) => { if (acknowledging && !closeAllowed) { e.Cancel = true; closeWhenIdle = true; } };
        Closed += (_, _) => { closed = true; if (!disposed) notesLifetime.Cancel(); };
        SizeChanged += (_, _) => LayoutPage(); body.SizeChanged += (_, _) => LayoutCards();
        body.ViewportChanged += (_, _) => LayoutCards();
        surface.SizeChanged += (_, _) => LayoutPage(); AppTypography.RegisterLayout(surface, LayoutPage); AppTypography.CaptureRecipes(surface).Invoke(); LayoutPage();
    }

    private async Task RefreshNotesAsync(bool forceRefresh)
    {
        if (refreshing || disposed || closed || officialUrl is null) return;
        refreshing = true;
        // Keep the focused action and all content controls alive during the request.
        // A second click is coalesced rather than changing focus via Enabled=false.
        try
        {
            var notes = await services.ReleaseNotes.FetchAsync(services.Experience.Version, forceRefresh, notesLifetime.Token).ConfigureAwait(false);
            if (notes is null || notesLifetime.IsCancellationRequested) return;
            await ModernFormsNext.WindowKit.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (disposed || closed || notes.Version != services.Experience.Version || contentLanguage != services.Localization.Language) return;
                if (cards.Count != notes.Sections.Count) return;
                var scroll = body.VerticalScrollProperties.Value;
                var sections = notes.Sections.ToDictionary(section => "WhatsNewSection-" + section.Category, StringComparer.Ordinal);
                foreach (var card in cards)
                {
                    if (!sections.ContainsKey(card.Panel.AccessibleAutomationId!)) return;
                }
                foreach (var card in cards)
                {
                    var section = sections[card.Panel.AccessibleAutomationId!];
                    card.Title.Text = section.Title; card.Text.AccessibleName = section.Title;
                    // A bounded, read-only native editor renders the entire text. Large releases
                    // never create full-document Skia bitmaps or turn markup into active content.
                    var content = string.Join("\n\n", section.Items);
                    if (card.Text.Text == content) continue;
                    var selectionStart = card.Text.SelectionStart; var selectionEnd = card.Text.SelectionEnd;
                    var textScroll = card.Text.VerticalScrollBar.Value;
                    card.Text.Text = content;
                    card.Text.SelectionStart = Math.Clamp(selectionStart, -1, content.Length);
                    card.Text.SelectionEnd = Math.Clamp(selectionEnd, -1, content.Length);
                    card.Text.VerticalScrollBar.Value = Math.Clamp(textScroll, card.Text.VerticalScrollBar.Minimum, card.Text.VerticalScrollBar.Maximum);
                }
                provenance.Text = services.Localization[notes.Origin == ReleaseNotesOrigin.GitHub ? "Experience.GitHubNotes" : "Experience.CachedNotes"];
                fallback.Text = contentLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) ? "" : services.Localization["Experience.OnlineEnglishNotes"];
                LayoutCards();
                body.VerticalScrollProperties.Value = Math.Clamp(scroll, body.VerticalScrollProperties.Minimum, body.VerticalScrollProperties.Maximum);
            });
        }
        catch (OperationCanceledException) when (notesLifetime.IsCancellationRequested) { }
        catch (Exception ex) { services.Logger.Error("Exact-version release notes remain local after refresh failed.", ex); }
        finally { refreshing = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; notesLifetime.Cancel(); notesLifetime.Dispose(); }
        base.Dispose(disposing);
    }

    private async Task AcknowledgeAsync(string? destination)
    {
        if (!shown || acknowledging) return;
        // A missing exact record is explanatory UI, never another version's notes.
        if (installedNotes is null) { Destination = destination; closeAllowed = true; Close(); return; }
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
        provenance.SetBounds(18, notice.Bottom + 10, Math.Max(1, width - 36), DashboardVisual.LabelHeight(provenance, Math.Max(1, width - 36), 16));
        fallback.SetBounds(18, provenance.Bottom + (fallback.Text.Length > 0 ? 8 : 0), Math.Max(1, width - 36),
            fallback.Text.Length > 0 ? DashboardVisual.LabelHeight(fallback, Math.Max(1, width - 36), 16) : 0);
        header.SetBounds(viewport.X, viewport.Y, width, fallback.Bottom + 18);
        var y = header.Bottom + 14;
        foreach (var (panel, title, description, icon) in cards)
        {
            icon.SetBounds(16, 18, 28, 28); title.Multiline = true;
            title.SetBounds(56, 14, Math.Max(1, width - 72), DashboardVisual.LabelHeight(title, Math.Max(1, width - 72), 3));
            description.SetBounds(56, title.Bottom + 8, Math.Max(1, width - 72), Math.Min(360, AppTypography.TextHeight(240)));
            panel.SetBounds(viewport.X, y, width, description.Bottom + 18); y = panel.Bottom + 12;
        }
        var x = viewport.X; var line = AppTypography.TextHeight(42);
        foreach (var button in new[] { appearance, updates, releases, refresh })
        {
            var desiredWidth = Math.Min(width, DashboardVisual.ActionWidth(button));
            if (x > viewport.X && x + desiredWidth > viewport.X + width) { x = viewport.X; y += line + 10; }
            button.SetBounds(x, y, desiredWidth, line); x = button.Right + 10;
        }
    }
}
