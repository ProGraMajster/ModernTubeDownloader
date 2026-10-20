using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class OtherView : UserControl
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 22);
    private readonly Label subtitle = AppUi.Muted();
    private readonly AppScrollFlowLayoutPanel tools = new() { AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly Dictionary<string, (Label Title, Label Description, Button Open)> modules = [];
    private readonly Panel recent = new() { Height = 310 };
    private bool disposed;
    private int recentRefreshVersion;
    public event EventHandler<ModernTubeDownloader.Batch.BatchImportSession>? ResumeBatch;

    public OtherView(AppServices services)
    {
        this.services = services;
        Dock = DockStyle.Fill;
        heading.AccessibleAutomationId = "OtherPageTitle";
        AppUi.BindBackground(this); AppUi.BindBackground(tools);
        Controls.AddRange([heading, subtitle, tools]);
        SizeChanged += (_, _) => LayoutPage();
        services.Localization.LanguageChanged += LanguageChanged;
        AppUi.Card(recent); tools.Controls.Add(recent);
        VisibleChanged += async (_, _) => { if (Visible) await RefreshRecentAsync(); };
        ApplyLocalization();
        AppTypography.RegisterLayout(this, LayoutPage);
    }
    internal Task RefreshToolsAsync() => RefreshRecentAsync();
    private async Task RefreshRecentAsync()
    {
        var refreshVersion = ++recentRefreshVersion;
        try
        {
            var sessions = await services.Batch.RecentAsync();
            IReadOnlyList<ThumbnailExportRecord> exports = [];
            try { exports = await services.ThumbnailTool.ReadRecentAsync(); }
            catch (Exception error) { services.Logger.Error("Thumbnail export history could not be read.", error); }
            if (disposed || refreshVersion != recentRefreshVersion) return;
            foreach (var child in recent.Controls.ToArray()) { recent.Controls.Remove(child); child.Dispose(); }
            var label = AppUi.Heading(services.Localization["Other.RecentTools"], dashboard ? 12 : 16); label.SetBounds(24, 16, Math.Max(180, recent.Width - 48), AppTypography.TextHeight(36)); recent.Controls.Add(label);
            var y = 56;
            foreach (var export in exports.Take(3))
            {
                var button = new DashboardButton { Text = services.Localization["Other.ThumbnailExport"] + " • " + System.IO.Path.GetFileName(export.OutputPath),
                    Enabled = File.Exists(export.OutputPath), AccessibleAutomationId = "OtherThumbnailExport" + y };
                AppUi.Secondary(button); button.SetBounds(24, y, Math.Max(180, recent.Width - 48), 38); y += 44;
                button.Click += (_, _) => Utilities.ShellService.OpenFile(export.OutputPath); recent.Controls.Add(button);
            }
            foreach (var session in sessions.Take(5))
            {
                var button = new DashboardButton { Text = $"{session.FileName} • {session.ImportedCount}", AccessibleAutomationId = "BatchRecent" + session.Id.ToString("N") };
                AppUi.Secondary(button); button.SetBounds(24, y, Math.Max(180, recent.Width - 48), 38); y += 44;
                button.Click += async (_, _) => { try { ResumeBatch?.Invoke(this, await services.Batch.OpenAsync(session.Id)); } catch (Exception ex) { services.Logger.Error("Batch session could not be reopened.", ex); } };
                recent.Controls.Add(button);
            }
            if (dashboard && sessions.Count == 0 && exports.Count == 0) { var hint = AppUi.Muted(services.Localization["Dashboard.NoRecent"]); hint.Multiline = true; hint.SetBounds(24, y, Math.Max(180, recent.Width - 48), AppTypography.TextHeight(64)); recent.Controls.Add(hint); y = hint.Bottom; }
            recent.Height = Math.Max(100, y + 16); LayoutPage();
        }
        catch (Exception ex) { services.Logger.Error("Recent batch sessions could not be loaded.", ex); }
    }

    /// <summary>Register only a working tool; no disabled or future-feature placeholders.</summary>
    public void RegisterTool(string id, Action open)
    {
        if (modules.ContainsKey(id)) throw new InvalidOperationException("Tool already registered.");
        var card = new Panel { Height = 154, Margin = new Padding(0, 0, 0, 14) };
        AppUi.Card(card);
        var title = AppUi.Heading("", 16); var description = AppUi.Muted(); var button = new DashboardButton { AccessibleAutomationId = "OtherOpen" + id };
        title.SetBounds(24, 18, 600, 32); description.SetBounds(24, 56, 600, 28); button.SetBounds(24, 100, 160, 36);
        AppUi.Secondary(button); button.Click += (_, _) => open();
        card.Controls.AddRange([title, description, button]);
        modules.Add(id, (title, description, button)); tools.Controls.Add(card);
        tools.Controls.Remove(recent); tools.Controls.Add(recent);
        ApplyLocalization(); LayoutPage();
    }
    private void ApplyLocalization()
    {
        heading.Text = services.Localization["Nav.Other"]; subtitle.Text = services.Localization["Other.Description"];
        foreach (var (id, module) in modules)
        {
            module.Title.Text = services.Localization["Other." + id + ".Title"];
            module.Description.Text = services.Localization["Other." + id + ".Description"];
            module.Open.Text = services.Localization["Other.Open"];
        }
    }
    private void LayoutPage()
    {
        if (dashboard) { LayoutDashboard(); return; }
        var width = Math.Max(480, ClientSize.Width - 48);
        heading.SetBounds(24, 20, width, AppTypography.TextHeight(36)); subtitle.SetBounds(24, heading.Bottom + 8, width, AppTypography.TextHeight(32));
        tools.SetBounds(24, subtitle.Bottom + 16, width, Math.Max(154, ClientSize.Height - subtitle.Bottom - 40));
        foreach (var card in tools.Controls)
        {
            AppTypography.UnflowRows(card);
            card.Width = Math.Max(1, tools.DisplayRectangle.Width - card.Margin.Horizontal);
            foreach (var label in card.Controls.OfType<Label>()) label.Width = width - 48;
            if (card != recent) {
                var density = AppearancePresentation.Current.Other.CardDensity;
                var gap = density == AppearanceDensity.Compact ? 4 : density == AppearanceDensity.Comfortable ? 14 : 8;
                var c = card.Controls.ToArray(); c[0].SetBounds(24, 18, width - 48, AppTypography.TextHeight(32)); c[1].SetBounds(24, c[0].Bottom + gap, width - 48, AppTypography.TextHeight(28)); c[2].SetBounds(24, c[1].Bottom + gap, Math.Min(width - 48, AppTypography.TextHeight(180)), AppTypography.TextHeight(36)); card.Height = c[2].Bottom + 18;
            } else { AppTypography.ReflowRows(card); card.Height = card.Controls.Select(c => c.Bottom + 16).DefaultIfEmpty(100).Max(); }
        }
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() => { ApplyLocalization(); LayoutPage(); });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { disposed = true; services.Localization.LanguageChanged -= LanguageChanged; }
        base.Dispose(disposing);
    }
}
