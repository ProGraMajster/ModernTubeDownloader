using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class InterfaceSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 15);
    private readonly Button classic = new() { AccessibleAutomationId = "InterfaceClassic" };
    private readonly Button modern = new() { AccessibleAutomationId = "InterfaceModernDashboard" };
    private readonly Panel classicPreview = new(), modernPreview = new();
    private readonly Label description = AppUi.Muted();
    public InterfaceSettingsPanel(AppServices services)
    {
        this.services = services; AppUi.Card(this); description.Multiline = true;
        Controls.AddRange([heading, classic, modern, classicPreview, modernPreview, description]);
        BuildPreview(classicPreview, false); BuildPreview(modernPreview, true);
        classic.Click += async (_, _) => await SelectAsync(AppInterfaceMode.Classic);
        modern.Click += async (_, _) => await SelectAsync(AppInterfaceMode.ModernDashboard);
        services.Appearance.ModeChanged += Changed; services.Localization.LanguageChanged += Changed; RefreshText();
    }
    private static void BuildPreview(Panel preview, bool modern)
    {
        AppUi.Card(preview, true);
        var nav = new Panel { Dock = DockStyle.Left, Width = 34 }; AppUi.BindBackground(nav, AppThemeTokens.Navigation);
        var canvas = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) }; AppUi.BindBackground(canvas);
        preview.Controls.Add(canvas); preview.Controls.Add(nav);
        for (var i = 0; i < 3; i++)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = modern ? 15 : 22, Margin = new Padding(0, 3, 0, 3) }; AppUi.Card(row); canvas.Controls.Add(row);
        }
        if (modern) { var detail = new Panel { Dock = DockStyle.Right, Width = 55 }; AppUi.Card(detail); canvas.Controls.Add(detail); }
        var input = new Panel { Dock = DockStyle.Top, Height = 12 }; AppUi.BindBackground(input, AppThemeTokens.Accent); canvas.Controls.Add(input);
    }
    private async Task SelectAsync(AppInterfaceMode mode)
    {
        var profile = services.Appearance.Profile.Copy(); profile.InterfaceMode = mode;
        try { await services.Appearance.ApplyProfileAsync(profile); }
        catch (Exception error) { services.Logger.Error("Interface preference could not be saved.", error); description.Text = services.Localization["Appearance.Error.Profile"]; }
    }
    private void Changed(object? sender, EventArgs e) => Application.RunOnUIThread(RefreshText);
    private void RefreshText()
    {
        heading.Text = services.Localization["Dashboard.InterfaceStyle"];
        classic.Text = services.Localization["Appearance.Value.Classic"]; modern.Text = services.Localization["Appearance.Value.ModernDashboard"];
        description.Text = services.Localization[services.Appearance.Profile.InterfaceMode == AppInterfaceMode.Classic ? "Dashboard.ClassicDescription" : "Dashboard.ModernDescription"];
        AppUi.NavigationButton(classic, services.Appearance.Profile.InterfaceMode == AppInterfaceMode.Classic);
        AppUi.NavigationButton(modern, services.Appearance.Profile.InterfaceMode == AppInterfaceMode.ModernDashboard);
    }
    public void LayoutForWidth(int width)
    {
        Width = width; var half = (width - 60) / 2; var h = AppTypography.TextHeight(40);
        heading.SetBounds(24, 16, width - 48, h);
        classic.SetBounds(24, heading.Bottom + 8, half, h); modern.SetBounds(classic.Right + 12, classic.Top, half, h);
        classicPreview.SetBounds(24, classic.Bottom + 10, half, 94); modernPreview.SetBounds(modern.Left, classicPreview.Top, half, 94);
        description.SetBounds(24, classicPreview.Bottom + 10, width - 48, AppTypography.TextHeight(54)); Height = description.Bottom + 18;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { services.Appearance.ModeChanged -= Changed; services.Localization.LanguageChanged -= Changed; }
        base.Dispose(disposing);
    }
}
