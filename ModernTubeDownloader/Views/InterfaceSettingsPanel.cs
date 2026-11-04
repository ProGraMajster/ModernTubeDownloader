using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal sealed class InterfaceSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 15);
    private readonly Button classic = new DashboardButton { AccessibleAutomationId = "InterfaceClassic" };
    private readonly Button modern = new DashboardButton { AccessibleAutomationId = "InterfaceModernDashboard" };
    private readonly Panel classicPreview = new(), modernPreview = new();
    private readonly InterfaceLayoutPreview classicDiagram = new(false), modernDiagram = new(true);
    private readonly Label description = AppUi.Muted();
    public InterfaceSettingsPanel(AppServices services)
    {
        this.services = services; AppUi.Card(this); description.Multiline = true;
        Controls.AddRange([heading, classic, modern, classicPreview, modernPreview, description, classicDiagram, modernDiagram]);
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
        heading.Text = services.Localization["Dashboard.InterfaceStyle"] + (AppTypography.IsDashboard(this) ? " · " + services.Localization["Dashboard.Preview"] : "");
        classic.Text = services.Localization["Appearance.Value.Classic"]; modern.Text = services.Localization["Appearance.Value.ModernDashboard"];
        description.Text = services.Localization[services.Appearance.InterfaceMode != services.Appearance.Profile.InterfaceMode ? "Dashboard.RecoveryActive" : services.Appearance.Profile.InterfaceMode == AppInterfaceMode.Classic ? "Dashboard.ClassicDescription" : "Dashboard.ModernDescription"];
        RefreshPresentation();
    }
    internal void RefreshPresentation()
    {
        var dashboard = AppTypography.IsDashboard(this);
        heading.Text = services.Localization["Dashboard.InterfaceStyle"] + (dashboard ? " · " + services.Localization["Dashboard.Preview"] : "");
        foreach (var (button, preview, selected) in new[]
        {
            (classic, classicDiagram, services.Appearance.Profile.InterfaceMode == AppInterfaceMode.Classic),
            (modern, modernDiagram, services.Appearance.Profile.InterfaceMode == AppInterfaceMode.ModernDashboard)
        })
        {
            if (dashboard) DashboardVisual.Button(button, selected);
            else AppUi.NavigationButton(button, selected);
            preview.Style.Border.Color = AppUi.GetColor(selected ? AppThemeTokens.Accent : AppThemeTokens.Border);
            preview.Style.Border.Width = selected ? 2 : 1;
            preview.Invalidate();
        }
    }
    public void LayoutForWidth(int width)
    {
        Width = width; var half = (width - 60) / 2; var h = AppTypography.TextHeight(40);
        heading.SetBounds(24, 16, width - 48, h);
        classic.SetBounds(24, heading.Bottom + 8, half, h); modern.SetBounds(classic.Right + 12, classic.Top, half, h);
        classicPreview.SetBounds(24, classic.Bottom + 10, half, 94); modernPreview.SetBounds(modern.Left, classicPreview.Top, half, 94);
        description.SetBounds(24, classicPreview.Bottom + 10, width - 48, AppTypography.TextHeight(54)); Height = description.Bottom + 18;
        var dashboard = AppTypography.IsDashboard(this);
        classicPreview.Visible = modernPreview.Visible = !dashboard;
        classicDiagram.Visible = modernDiagram.Visible = dashboard;
        if (dashboard)
        {
            heading.SetBounds(16, 16, width - 32, AppTypography.TextHeight(32));
            half = (width - 44) / 2;
            classic.SetBounds(16, heading.Bottom + 12, half, h); modern.SetBounds(classic.Right + 12, classic.Top, half, h);
            classicDiagram.SetBounds(classic.Left, classic.Bottom + 10, half, 112);
            modernDiagram.SetBounds(modern.Left, classicDiagram.Top, half, 112);
            description.SetBounds(16, classicDiagram.Bottom + 12, width - 32, AppTypography.TextHeight(48)); Height = description.Bottom + 16;
        }
        RefreshPresentation();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { services.Appearance.ModeChanged -= Changed; services.Localization.LanguageChanged -= Changed; }
        base.Dispose(disposing);
    }
}

/// <summary>Labelled interface schematic, with no media, tasks or fabricated runtime statistics.</summary>
internal sealed class InterfaceLayoutPreview : Control
{
    private readonly bool modern;
    public InterfaceLayoutPreview(bool modern)
    {
        this.modern = modern; Visible = false; TabStop = false; AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
        SetControlBehavior(ControlBehaviors.Selectable, false);
        AppUi.BindBackground(this, AppThemeTokens.Background); Style.Border.Radius = 7;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Canvas.Save(); e.Canvas.Scale((float)Scaling);
        try
        {
            using var paint = new SKPaint { IsAntialias = true };
            void Rect(float x, float y, float width, float height, string token, float radius = 2)
            {
                paint.Color = AppUi.GetColor(token);
                e.Canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), radius, radius, paint);
            }
            var nav = modern ? 29 : 20; var content = Width - nav - 18;
            Rect(8, 8, nav, Height - 16, AppThemeTokens.Navigation, 3);
            for (var i = 0; i < 5; i++) Rect(12, 16 + i * 14, nav - 8, 7, i == 1 ? AppThemeTokens.Accent : AppThemeTokens.SurfaceSecondary);
            var left = nav + 14; var inspectorWidth = modern ? Math.Max(26, content / 3) : 0;
            Rect(left, 10, content, 9, AppThemeTokens.SurfaceSecondary);
            var rowWidth = content - (modern ? inspectorWidth + 6 : 0);
            for (var i = 0; i < 3; i++)
            {
                var y = 28 + i * 22;
                Rect(left, y, rowWidth, 18, AppThemeTokens.Surface);
                if (modern) Rect(left + 3, y + 3, Math.Min(22, rowWidth / 3), 12, AppThemeTokens.Info, 1);
                var textLeft = left + (modern ? Math.Min(22, rowWidth / 3) + 7 : 4);
                Rect(textLeft, y + 4, Math.Max(5, left + rowWidth - textLeft - 5), 3, AppThemeTokens.TextSecondary, 1);
                Rect(textLeft, y + 11, Math.Max(4, (left + rowWidth - textLeft - 5) * .7f), 3, AppThemeTokens.Border, 1);
            }
            if (modern)
            {
                var x = left + rowWidth + 6;
                Rect(x, 28, inspectorWidth, 62, AppThemeTokens.Surface);
                Rect(x + 3, 32, inspectorWidth - 6, 21, AppThemeTokens.Info);
                for (var i = 0; i < 3; i++) Rect(x + 4, 60 + i * 7, inspectorWidth - 8, 3, AppThemeTokens.Border, 1);
                Rect(left, 96, rowWidth, 5, AppThemeTokens.Accent, 1);
            }
        }
        finally { e.Canvas.Restore(); }
    }
}
