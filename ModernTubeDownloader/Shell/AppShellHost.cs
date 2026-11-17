using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Shell;

public enum AppPageId { Downloads, Live, Other, Batch, History, Settings, Help, About }

/// <summary>Only chrome is owned by a shell. Pages and the application runtime outlive it.</summary>
internal interface IAppShell : IDisposable
{
    Panel Root { get; }
    Panel PageHost { get; }
    AppInterfaceMode Mode { get; }
    void Select(AppPageId page);
    void SetStatus(string status, string token);
    void Refresh();
}

/// <summary>Moves the same page host between shells; never constructs application services.</summary>
internal sealed class AppShellHost : Panel
{
    private readonly Panel pages;
    private readonly LocalizationService text;
    private readonly IAppLogger logger;
    private readonly Func<AppInterfaceMode, Action<AppPageId>, IAppShell> factory;
    private IAppShell? shell;
    private int shellGeneration;
    public AppInterfaceMode Mode => shell?.Mode ?? AppInterfaceMode.Classic;
    public AppPageId CurrentPage { get; private set; }
    public event EventHandler<AppPageId>? Navigate;
    public event EventHandler? ThemeRequested;
    public event EventHandler? UpdateRequested;
    public event EventHandler? Fallback;

    public AppShellHost(Panel pages, LocalizationService text, IAppLogger logger,
        Func<AppInterfaceMode, Action<AppPageId>, IAppShell>? factory = null)
    {
        this.pages = pages; this.text = text; this.logger = logger;
        this.factory = factory ?? ((mode, navigate) => mode == AppInterfaceMode.ModernDashboard
            ? new ModernDashboardAppShell(text, navigate, () => ThemeRequested?.Invoke(this, EventArgs.Empty), () => UpdateRequested?.Invoke(this, EventArgs.Empty))
            : new ClassicAppShell(text, navigate));
        Dock = DockStyle.Fill; AccessibleAutomationId = "AppShellHost";
        AppUi.BindBackground(this);
    }

    public void Apply(AppInterfaceMode requested, Action<AppInterfaceMode>? preparePages = null)
    {
        if (shell?.Mode == requested && preparePages is null) { shell.Refresh(); return; }
        PerformanceDiagnostics.Set("shell.generation", ++shellGeneration);
        PerformanceDiagnostics.Set("shell.requestedMode", (int)requested);
        PerformanceDiagnostics.Set("shell.hwnd", FindForm()?.PlatformHandle.Handle.ToInt64() ?? 0);
        PerformanceDiagnostics.Emit("shell.apply.begin");
        // Construct before detaching the working shell. A failed construction cannot lose pages.
        IAppShell next;
        try { next = shell?.Mode == requested ? shell : factory(requested, page => Navigate?.Invoke(this, page)); }
        catch (Exception error) when (requested == AppInterfaceMode.ModernDashboard)
        {
            logger.Error("Modern Dashboard construction failed; Classic recovery selected.", error);
            next = factory(AppInterfaceMode.Classic, page => Navigate?.Invoke(this, page));
            Fallback?.Invoke(this, EventArgs.Empty);
        }
        SuspendLayout();
        try
        {
            pages.Parent?.Controls.Remove(pages); PerformanceDiagnostics.Emit("shell.pages.detached");
            if (shell is { } previous)
            {
                Controls.Remove(previous.Root);
                if (!ReferenceEquals(previous, next)) { previous.Dispose(); PerformanceDiagnostics.Emit("shell.previous.disposed"); }
            }
            shell = next;
            // Prepare the final presentation before its first publication. Previously the
            // same page tree was attached by Apply, then detached and attached again by MainForm.
            next.Root.Bounds = DisplayRectangle;
            AppTypography.SetDashboardPresentation(next.Root, next.Mode == AppInterfaceMode.ModernDashboard);
            next.Select(CurrentPage); next.Refresh(); next.Root.PerformLayout();
            try { preparePages?.Invoke(next.Mode); }
            finally
            {
                // A presentation failure must not leave the canonical pages orphaned.
                pages.Dock = DockStyle.Fill;
                next.Root.Bounds = DisplayRectangle; next.Root.PerformLayout();
                pages.Bounds = next.PageHost.DisplayRectangle; pages.PerformLayout();
                next.PageHost.Controls.Add(pages);
                Controls.Add(next.Root);
            }
        }
        finally { ResumeLayout(true); }
        PerformanceDiagnostics.Set("shell.rootIdentity", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(next.Root));
        PerformanceDiagnostics.Set("shell.pagesIdentity", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pages));
        PerformanceDiagnostics.Set("shell.currentMode", (int)next.Mode);
        PerformanceDiagnostics.Emit("shell.apply.end");
    }

    public void Select(AppPageId page) { CurrentPage = page; shell?.Select(page); }
    public void SetStatus(string status, string token = AppThemeTokens.Success) => shell?.SetStatus(status, token);
    public void RefreshChrome() => shell?.Refresh();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { shell?.Dispose(); shell = null; }
        base.Dispose(disposing);
    }
}

internal class ClassicAppShell : IAppShell
{
    protected readonly LocalizationService Text;
    protected readonly Panel Sidebar = new() { Dock = DockStyle.Left, Width = 236 };
    protected readonly Panel Footer = new() { Dock = DockStyle.Bottom, Height = 44 };
    protected readonly Label Status = AppUi.Muted();
    protected readonly Label Brand = AppUi.Heading("ModernTube\nDownloader", 12.5f);
    protected readonly PictureBox Logo = new() { Image = AppBranding.Icon, SizeMode = PictureBoxSizeMode.Zoom };
    protected readonly Dictionary<AppPageId, Button> Buttons = [];
    protected readonly Dictionary<AppPageId, AppVectorIcon> Icons = [];
    protected readonly ToolTip Tips = new();
    protected AppPageId Selected;
    public Panel Root { get; } = new() { Dock = DockStyle.Fill };
    public Panel PageHost { get; } = new() { Dock = DockStyle.Fill };
    public virtual AppInterfaceMode Mode => AppInterfaceMode.Classic;

    public ClassicAppShell(LocalizationService text, Action<AppPageId> navigate) : this(text, navigate, false) { }
    protected ClassicAppShell(LocalizationService text, Action<AppPageId> navigate, bool modern)
    {
        Text = text;
        AppUi.BindBackground(Root); AppUi.BindBackground(PageHost);
        AppUi.BindBackground(Sidebar, AppThemeTokens.Navigation); AppUi.BindBackground(Footer, modern ? AppThemeTokens.Surface : AppThemeTokens.SurfaceSecondary);
        var main = new Panel { Dock = DockStyle.Fill }; AppUi.BindBackground(main);
        main.Controls.Add(PageHost); main.Controls.Add(Footer);
        if (!modern) { var top = new Panel { Dock = DockStyle.Top, Height = 12 }; AppUi.BindBackground(top, AppThemeTokens.Surface); main.Controls.Add(top); }
        Status.Dock = DockStyle.Fill; Status.Padding = new Padding(26, 10, 26, 8); Footer.Controls.Add(Status);
        Root.Controls.Add(main); Root.Controls.Add(Sidebar);
        if (modern) { main.Controls.Remove(Footer); Root.Controls.Add(Footer); }
        Logo.SetBounds(24, 24, 44, 44); Brand.SetBounds(86, 22, 142, 52); Brand.Multiline = true;
        if (modern) Sidebar.Controls.Add(Logo);
        else
        {
            var frame = new Panel { Left = 18, Top = 18, Width = 56, Height = 56, Padding = new Padding(6) }; AppUi.Card(frame, true);
            Logo.Dock = DockStyle.Fill; frame.Controls.Add(Logo); Sidebar.Controls.Add(frame);
        }
        Sidebar.Controls.Add(Brand); Logo.AccessibleName = "ModernTubeDownloader"; Tips.SetToolTip(Logo, "ModernTubeDownloader");
        var order = modern ? new[] { AppPageId.Downloads, AppPageId.Live, AppPageId.Other, AppPageId.Batch, AppPageId.History, AppPageId.Settings, AppPageId.Help, AppPageId.About }
            : [AppPageId.Downloads, AppPageId.Live, AppPageId.History, AppPageId.Other, AppPageId.Settings];
        foreach (var page in order)
        {
            var button = new ShellNavigationButton { AccessibleAutomationId = "Nav" + page, TextAlign = ContentAlignment.MiddleLeft };
            var icon = new AppVectorIcon(page switch { AppPageId.Downloads => AppIconKind.Download, AppPageId.Live => AppIconKind.Live, AppPageId.History => AppIconKind.History, AppPageId.Settings => AppIconKind.Settings, AppPageId.Batch => AppIconKind.Clipboard, AppPageId.Help => AppIconKind.Help, AppPageId.About => AppIconKind.Info, _ => AppIconKind.Queue }, 24);
            if (modern) icon.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
            button.Click += (_, _) => navigate(page); icon.Click += (_, _) => button.PerformClick();
            button.Controls.Add(icon); Sidebar.Controls.Add(button); Buttons[page] = button; Icons[page] = icon;
        }
        Root.SizeChanged += LayoutChanged;
        AppTypography.RegisterLayout(Root, Refresh);
        if (!modern) Refresh();
    }
    private void LayoutChanged(object? sender, EventArgs e) => Refresh();
    public virtual void Refresh()
    {
        var profile = AppearancePresentation.Current;
        var compact = profile.Navigation.Mode == NavigationMode.Icons;
        Sidebar.Width = compact ? 84 : (profile.Navigation.Width switch { NavigationWidth.Narrow => 212, NavigationWidth.Wide => 310, _ => 236 }) + (AppTypography.Scale > 1.25 ? 64 : 0);
        Brand.Visible = !compact; Brand.Width = Sidebar.Width - 102;
        var y = 120;
        foreach (var page in Buttons.Keys)
        {
            SetButton(page, compact, 18, y, Sidebar.Width - 36, AppTypography.TextHeight(46));
            y += AppTypography.TextHeight(46) + AppearancePresentation.Spacing(8);
        }
        Select(Selected);
    }
    protected void SetButton(AppPageId page, bool compact, int x, int y, int width, int height)
    {
        var label = Text[page == AppPageId.Batch ? "Dashboard.Batch" : "Nav." + page];
        var button = Buttons[page]; var icon = Icons[page];
        button.SetBounds(x, y, Math.Max(40, width), height); button.Text = compact ? "" : label;
        button.AccessibleName = label; Tips.SetToolTip(button, label); Tips.SetToolTip(icon, label);
        button.Padding = new Padding(compact ? 0 : 46, 0, 8, 0);
        icon.Left = compact ? (button.Width - 24) / 2 : 14; icon.Top = (height - 24) / 2;
    }
    public virtual void Select(AppPageId page)
    {
        Selected = page;
        foreach (var (id, button) in Buttons) { if (button is ShellNavigationButton nav) nav.IsCurrent = id == page; AppUi.NavigationButton(button, id == page); Icons[id].SetColorToken(id == page ? AppThemeTokens.NavigationSelectedText : AppThemeTokens.TextPrimary); }
    }
    public virtual void SetStatus(string status, string token) => Status.Text = status;
    public virtual void Dispose() { Tips.Dispose(); Root.Dispose(); }
}

internal sealed class ModernDashboardAppShell : ClassicAppShell
{
    private readonly Button theme = new() { AccessibleAutomationId = "DashboardTheme" };
    private readonly Button updates = new() { AccessibleAutomationId = "DashboardUpdates" };
    private readonly Label identity = AppUi.Muted();
    private readonly Panel statusDot = new() { Width = 8, Height = 8 };
    private readonly Panel sectionRule = new(), activeMarker = new();
    private bool refreshing;
    public override AppInterfaceMode Mode => AppInterfaceMode.ModernDashboard;
    public ModernDashboardAppShell(LocalizationService text, Action<AppPageId> navigate, Action toggleTheme, Action checkUpdates) : base(text, navigate, true)
    {
        statusDot.Style.Border.Radius = 4; statusDot.Style.Border.Width = 0; Footer.Controls.Add(statusDot);
        AppUi.BindBackground(sectionRule, AppThemeTokens.Border); DashboardVisual.Flat(sectionRule);
        AppUi.BindBackground(activeMarker, AppThemeTokens.Accent); DashboardVisual.Flat(activeMarker); activeMarker.Style.Border.Radius = 2;
        Sidebar.Controls.AddRange([sectionRule, activeMarker]);
        Sidebar.AutoScroll = true; Sidebar.AccessibleAutomationId = "NavigationSidebar";
        AppUi.BindGradient(Sidebar, AppThemeTokens.Surface, AppThemeTokens.Navigation);
        Sidebar.Style.Border.Right.Width = 1; Sidebar.Style.Border.Right.Color = AppUi.GetColor(AppThemeTokens.Border);
        AppUi.BindGradient(Footer, AppThemeTokens.Surface, AppThemeTokens.Background);
        Footer.Style.Border.Top.Width = 1; Footer.Style.Border.Top.Color = AppUi.GetColor(AppThemeTokens.Border);
        Footer.Height = 32; Status.Padding = new Padding(10, 5, 10, 0);
        identity.Dock = DockStyle.Left; identity.Width = 290; identity.Padding = new Padding(12, 5, 4, 0);
        identity.Text = "ModernTubeDownloader v" + AppBranding.Version;
        updates.Dock = DockStyle.Right; updates.Width = 210; AppUi.Secondary(updates);
        theme.Dock = DockStyle.Right; theme.Width = 110; AppUi.Secondary(theme);
        theme.Click += (_, _) => toggleTheme(); updates.Click += (_, _) => checkUpdates();
        theme.Visible = false; Footer.Controls.AddRange([identity, updates, theme]);
        // Modern branding belongs to the caption. Invisible controls still contribute
        // to the scroll canvas: Brand.Right=60 overflowed the compact 50px viewport
        // once its vertical scrollbar appeared. Remove the unused chrome, not scrolling.
        Sidebar.Controls.Remove(Logo); Sidebar.Controls.Remove(Brand);
        Logo.Dispose(); Brand.Dispose();
        Sidebar.SizeChanged += SidebarSizeChanged;
        Refresh();
    }
    private void SidebarSizeChanged(object? sender, EventArgs e) => Refresh();
    public override void Select(AppPageId page)
    {
        base.Select(page);
        foreach (var (id, button) in Buttons)
        {
            DashboardVisual.Flat(button); AppTypography.Bind(button, 10.5f);
            if (id != page) button.Style.BackgroundColor = SkiaSharp.SKColors.Transparent;
            button.StyleFocused.Border.Width = 1; button.StyleFocused.Border.Color = AppUi.GetColor(AppThemeTokens.Accent);
        }
        if (activeMarker is not null && Buttons.TryGetValue(page, out var selected)) activeMarker.SetBounds(0, selected.Top + 4, 4, selected.Height - 8);
    }
    public override void SetStatus(string status, string token) { base.SetStatus(status, token); AppUi.BindBackground(statusDot, token); }
    public override void Refresh()
    {
        // The base constructor may receive a layout callback before derived fields exist.
        if (theme is null || refreshing || Root.Disposing) return;
        refreshing = true;
        try
        {
            Sidebar.Style.Border.Right.Color = Footer.Style.Border.Top.Color = AppUi.GetColor(AppThemeTokens.Border);
            var p = AppearancePresentation.Current.ModernDashboardShell;
            var compact = p.Sidebar == DashboardSidebarMode.Compact || p.Sidebar == DashboardSidebarMode.Auto && (Root.Width < 1280 || AppTypography.Scale > 1.25);
            Select(Selected); // Bind the actual Modern font before measuring translated labels.
            var labelWidth = Buttons.Max(entry => DashboardVisual.TextWidth(entry.Value,
                Text[entry.Key == AppPageId.Batch ? "Dashboard.Batch" : "Nav." + entry.Key]));
            // Include icon/text padding, focus border, both gutters and the vertical rail.
            Sidebar.Width = compact ? 66 : Math.Max(Math.Max(212, AppTypography.TextHeight(170)), labelWidth + 96);
            Footer.Height = AppTypography.TextHeight(32); identity.Width = Math.Min(DashboardVisual.TextWidth(identity, identity.Text) + 30, Root.Width / 2);
            statusDot.SetBounds(identity.Width + 8, (Footer.Height - 8) / 2, 8, 8);
            Status.Padding = new Padding(26, 5, 8, 0);
            theme.Text = Text["Dashboard.Theme"]; updates.Text = Text["Updates.Check"];
            DashboardVisual.Flat(updates);
            AppUi.BindInteractiveSurface(updates, AppThemeTokens.Background, AppThemeTokens.SurfaceHover);
            AppTypography.Bind(identity, 9); AppTypography.Bind(Status, 9); AppTypography.Bind(updates, 9);
            AppUi.BindForeground(updates, AppThemeTokens.Info);
            ArrangeNavigation(compact);
        }
        finally { refreshing = false; }
    }

    private void ArrangeNavigation(bool compact)
    {
        // A new vertical rail changes the available width. Arrange again only when
        // that viewport changes; ordinary selection/scrolling retains the same tree.
        for (var pass = 0; pass < 2; pass++)
        {
            var viewport = Sidebar.DisplayRectangle;
            var row = AppTypography.TextHeight(42);
            var width = compact ? 40 : Math.Max(40, viewport.Width - 24);
            var x = viewport.X + (compact ? Math.Max(4, (viewport.Width - width) / 2) : 12);
            var y = 32;
            Sidebar.SuspendLayout();
            try
            {
                foreach (var page in new[] { AppPageId.Downloads, AppPageId.Live, AppPageId.Other, AppPageId.Batch, AppPageId.History, AppPageId.Settings })
                {
                    if (page == AppPageId.Settings) y += 22;
                    SetButton(page, compact, x, viewport.Y + y, width, row); y += row + 8;
                }
                var bottom = Math.Max(y + 20, viewport.Height - 2 * (row + 6) - 36);
                SetButton(AppPageId.Help, compact, x, viewport.Y + bottom, width, row);
                SetButton(AppPageId.About, compact, x, viewport.Y + bottom + row + 6, width, row);
                sectionRule.SetBounds(x + 4, Buttons[AppPageId.Settings].Top - 16, width - 8, 1);
                var selected = Buttons[Selected];
                activeMarker.SetBounds(viewport.X, selected.Top + 4, 4, selected.Height - 8);
            }
            finally { Sidebar.ResumeLayout(true); }
            if (Sidebar.DisplayRectangle == viewport) break;
        }
    }
}
