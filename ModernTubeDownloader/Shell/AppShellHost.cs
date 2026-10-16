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

    public void Apply(AppInterfaceMode requested)
    {
        if (shell?.Mode == requested) { shell.Refresh(); return; }
        // Construct before detaching the working shell. A failed construction cannot lose pages.
        IAppShell next;
        try { next = factory(requested, page => Navigate?.Invoke(this, page)); }
        catch (Exception error) when (requested == AppInterfaceMode.ModernDashboard)
        {
            logger.Error("Modern Dashboard construction failed; Classic recovery selected.", error);
            next = factory(AppInterfaceMode.Classic, page => Navigate?.Invoke(this, page));
            Fallback?.Invoke(this, EventArgs.Empty);
        }
        SuspendLayout();
        try
        {
            pages.Parent?.Controls.Remove(pages);
            if (shell is { } previous) { Controls.Remove(previous.Root); previous.Dispose(); }
            shell = next; pages.Dock = DockStyle.Fill; next.PageHost.Controls.Add(pages);
            Controls.Add(next.Root); AppTypography.SetDashboardPresentation(next.Root, next.Mode == AppInterfaceMode.ModernDashboard); next.Select(CurrentPage); next.Refresh();
        }
        finally { ResumeLayout(true); }
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
    public void Select(AppPageId page)
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
    public override AppInterfaceMode Mode => AppInterfaceMode.ModernDashboard;
    public ModernDashboardAppShell(LocalizationService text, Action<AppPageId> navigate, Action toggleTheme, Action checkUpdates) : base(text, navigate, true)
    {
        statusDot.Style.Border.Radius = 4; statusDot.Style.Border.Width = 0; Footer.Controls.Add(statusDot);
        Sidebar.AutoScroll = true;
        Footer.Height = 32; Status.Padding = new Padding(10, 5, 10, 0);
        identity.Dock = DockStyle.Left; identity.Width = 290; identity.Padding = new Padding(12, 5, 4, 0);
        identity.Text = "ModernTubeDownloader v" + AppBranding.Version;
        updates.Dock = DockStyle.Right; updates.Width = 210; AppUi.Secondary(updates);
        theme.Dock = DockStyle.Right; theme.Width = 110; AppUi.Secondary(theme);
        theme.Click += (_, _) => toggleTheme(); updates.Click += (_, _) => checkUpdates();
        theme.Visible = false; Footer.Controls.AddRange([identity, updates, theme]);
        Logo.SetBounds(16, 0, 28, 28); Brand.SetBounds(54, 0, 150, 28); Brand.Text = "ModernTubeDownloader"; Brand.Multiline = false; Brand.AutoEllipsis = true; AppTypography.Bind(Brand, 10, FontStyle.Bold);
        Refresh();
    }
    public override void SetStatus(string status, string token) { base.SetStatus(status, token); AppUi.BindBackground(statusDot, token); }
    public override void Refresh()
    {
        // The base constructor may receive a layout callback before derived fields exist.
        if (theme is null) return;
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var compact = p.Sidebar == DashboardSidebarMode.Compact || p.Sidebar == DashboardSidebarMode.Auto && (Root.Width < 1280 || AppTypography.Scale > 1.25);
        Sidebar.Width = compact ? 66 : Math.Max(212, AppTypography.TextHeight(170));
        Brand.Visible = !compact; Logo.Left = compact ? 19 : 16; Brand.Width = Math.Max(0, Sidebar.Width - 60);
        var row = AppTypography.TextHeight(44); var y = 40;
        foreach (var page in new[] { AppPageId.Downloads, AppPageId.Live, AppPageId.Other, AppPageId.Batch, AppPageId.History, AppPageId.Settings })
        {
            if (page == AppPageId.Settings) y += 22;
            SetButton(page, compact, 10, y, Sidebar.Width - 20, row); y += row + 6;
        }
        var bottom = Math.Max(y + 20, Sidebar.Height - 2 * (row + 6) - 14);
        SetButton(AppPageId.Help, compact, 10, bottom, Sidebar.Width - 20, row);
        SetButton(AppPageId.About, compact, 10, bottom + row + 6, Sidebar.Width - 20, row);
        Footer.Height = AppTypography.TextHeight(32); identity.Width = Math.Min(360, Root.Width / 3);
        statusDot.SetBounds(identity.Width + 8, (Footer.Height - 8) / 2, 8, 8);
        Status.Padding = new Padding(26, 5, 8, 0);
        theme.Text = Text["Dashboard.Theme"]; updates.Text = Text["Updates.Check"];
        Select(Selected);
    }
}
