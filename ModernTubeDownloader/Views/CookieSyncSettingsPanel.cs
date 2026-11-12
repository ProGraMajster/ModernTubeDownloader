using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Authentication;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Views;

internal sealed class CookieSyncSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly ComboBox source = new() { AccessibleAutomationId = "AuthenticationSource" };
    private readonly ComboBox browser = new() { AccessibleAutomationId = "CookieBridgeBrowser" };
    private readonly TextBox extensionId = new() { AccessibleAutomationId = "CookieBridgeExtensionId" };
    private readonly CheckBox consent = new DashboardToggle { AccessibleAutomationId = "CookieBridgeConsent" };
    private readonly TextBox summary = new() { ReadOnly=true, MultiLine=true, AccessibleAutomationId="CookieProfilesStatus" };
    private readonly Label help = AppUi.Muted();
    private readonly Label feedback = AppUi.Muted();
    private readonly List<(Control Control, string Key)> labels = [];
    private readonly Button refresh = new DashboardButton(), delete = new DashboardButton(), repair = new DashboardButton(), options = new DashboardButton();
    private bool loading, arranging, responsive;
    internal bool ResponsiveLayout
    {
        get => responsive;
        set { responsive = value; if (!value) feedback.Visible = true; LayoutForWidth(Width); }
    }
    public event EventHandler? SourceChanged;
    internal event EventHandler? PreferredHeightChanged;
    public AuthenticationSource Source
    {
        get => source.SelectedIndex switch { 1 => AuthenticationSource.SyncedBrowserProfile, 2 => AuthenticationSource.ManualFile, _ => AuthenticationSource.None };
        set => source.SelectedIndex = value switch { AuthenticationSource.SyncedBrowserProfile => 1, AuthenticationSource.ManualFile => 2, _ => 0 };
    }
    public CookieSyncSettingsPanel(AppServices services)
    {
        this.services = services;
        AccessibleAutomationId = "CookieSyncSection";
        refresh.AccessibleAutomationId="CookieSyncRefresh";delete.AccessibleAutomationId="CookieSyncDelete";
        repair.AccessibleAutomationId = "CookieSyncRepair"; options.AccessibleAutomationId = "CookieSyncOptions";
        help.AccessibleAutomationId = "CookieSyncPrivacy"; feedback.AccessibleAutomationId = "CookieSyncFeedback";
        AppUi.Input(source); AppUi.Input(browser); AppUi.Input(extensionId);
        foreach (var button in new[] { refresh, delete, repair, options }) AppUi.Secondary(button);
        AppUi.Input(summary); AppUi.BindForeground(consent); help.Multiline = feedback.Multiline = true;
        Bind(help,"CookieSync.Privacy"); Bind(consent,"CookieSync.Consent"); Bind(refresh,"CookieSync.Refresh");
        Bind(delete,"CookieSync.Delete"); Bind(repair,"CookieSync.Repair"); Bind(options,"CookieSync.Options");
        browser.Items.AddRange(["Chrome", "Edge", "Brave (experimental)", "Chromium (experimental)"]); browser.SelectedIndex = 0;
        Controls.AddRange([source, browser, extensionId, consent, summary, help, feedback, refresh, delete, repair, options]);
        feedback.TextChanged += (_, _) => LayoutForWidth(Width);
        source.SelectedIndexChanged += (_, _) => { if (!loading) SourceChanged?.Invoke(this,EventArgs.Empty); };
        refresh.Click += (_, _) => Refresh();
        delete.Click += (_, _) => {
            try { services.Authentication!.Store.DeleteAll(); feedback.Text = services.Localization["CookieSync.Deleted"]; Refresh(); }
            catch { feedback.Text = services.Localization["CookieSync.Unavailable"]; }
        };
        repair.Click += (_, _) => {
            try {
                if (!consent.Checked) { feedback.Text=services.Localization["CookieSync.ConsentRequired"]; return; }
                // The shipped host uses the normal per-user Auth root, not an isolated automation profile.
                if (!Path.GetFullPath(services.Paths.AuthenticationDirectory).Equals(Path.GetFullPath(AuthenticationFiles.DefaultRoot),StringComparison.OrdinalIgnoreCase))
                    throw new CookieSecurityException("bridge-profile");
                var host = Path.Combine(AppContext.BaseDirectory,"native-host","ModernTubeDownloader.NativeHost.exe");
                NativeBridgeRegistration.Register(services.Paths.AuthenticationDirectory,Browser,extensionId.Text.Trim(),host);
                consent.Checked=false; feedback.Text=services.Localization["CookieSync.Registered"]; Refresh();
            } catch (CookieSecurityException error) when (error.Message == "bridge-redirected") {
                feedback.Text = services.Localization["CookieSync.Redirected"];
            } catch { feedback.Text=services.Localization["CookieSync.Unavailable"]; }
        };
        options.Click += (_, _) => {
            try {
                var binding=NativeBridgeRegistration.ReadBindings(services.Paths.AuthenticationDirectory).LastOrDefault(b=>b.Browser==Browser);
                if(binding is null) { feedback.Text=services.Localization["CookieSync.Unavailable"]; return; }
                // An owned, declared options_ui page, not an undocumented chrome:// route.
                Process.Start(new ProcessStartInfo(Browser switch { "edge"=>"msedge.exe","brave"=>"brave.exe",_=>"chrome.exe" })
                { UseShellExecute=true, Arguments=binding.Origin+"src/options.html" });
            } catch { feedback.Text=services.Localization["CookieSync.OptionsFallback"]; }
        };
        Localize();
    }
    private string Browser => browser.SelectedIndex switch { 1=>"edge",2=>"brave",3=>"chromium",_=>"chrome" };
    private void Bind(Control control,string key) { labels.Add((control,key)); }
    public void Localize()
    {
        loading=true;var selected=Source;
        foreach(var (control,key) in labels)control.Text=services.Localization[key];
        source.Items.Clear();source.Items.AddRange([services.Localization["CookieSync.None"],services.Localization["CookieSync.Automatic"],services.Localization["CookieSync.Manual"]]);
        Source=selected;loading=false;Refresh(); LayoutForWidth(Width);
    }
    public void Refresh()
    {
        try {
            var profiles=services.Authentication!.Store.GetProfiles();
            var bridge=NativeBridgeRegistration.ReadBindings(services.Paths.AuthenticationDirectory);
            summary.Text=services.Localization[bridge.Count>0?"CookieSync.BridgeInstalled":"CookieSync.BridgeMissing"]+"\n"+
                (profiles.Count==0?services.Localization["CookieSync.NotSynced"]:string.Join("\n",profiles.Select(p=>
                $"{p.Browser} • {string.Join(", ",p.Domains)} • {p.CookieCount} cookies • {p.LastSyncUtc.ToLocalTime():g} • " +
                services.Localization[p.Revoked?"CookieSync.Revoked":DateTimeOffset.UtcNow-p.LastSyncUtc<TimeSpan.FromMinutes(30)?"CookieSync.Fresh":"CookieSync.Stale"] +
                $" • v{p.SnapshotVersion} • {p.ExcludedPartitioned} partitioned")));
        } catch { summary.Text=services.Localization["CookieSync.Unavailable"]; }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (responsive) { LayoutForWidth(Width); return; }
        var width=Math.Max(100,Width); var half=(width-12)/2;
        source.SetBounds(0,0,width,36);help.SetBounds(0,50,width,74);summary.SetBounds(0,134,width,112);
        refresh.SetBounds(0,256,half,38);delete.SetBounds(half+12,256,half,38);
        browser.SetBounds(0,310,150,36);extensionId.SetBounds(162,310,width-162,36);
        consent.SetBounds(0,358,width,50);repair.SetBounds(0,420,half,38);options.SetBounds(half+12,420,half,38);
        feedback.SetBounds(0,474,width,56);
    }

    internal void LayoutForWidth(int width)
    {
        if (!responsive || arranging || width < 60) return;
        var previousHeight = Height;
        arranging = true;
        try
        {
            Width = width;
            var line = AppTypography.TextHeight(36); var gap = 10; var top = 0;
            source.SetBounds(0, top, width, line); top = source.Bottom + gap;
            help.SetBounds(0, top, width, DashboardVisual.LabelHeight(help, width, 20)); top = help.Bottom + gap;
            summary.SetBounds(0, top, width, AppTypography.TextHeight(112)); top = summary.Bottom + gap;
            Commands(refresh, delete);
            var browserWidth = Math.Max(AppTypography.TextHeight(160), DashboardVisual.TextWidth(browser, "Chromium (experimental)") + 36);
            var paired = width >= browserWidth + AppTypography.TextHeight(240) + gap;
            browser.SetBounds(0, top, paired ? browserWidth : width, line);
            extensionId.SetBounds(paired ? browser.Right + gap : 0, paired ? top : browser.Bottom + gap,
                paired ? width - browserWidth - gap : width, line);
            top = extensionId.Bottom + gap;
            var style = consent.CurrentStyle;
            var consentSize = TextMeasurer.MeasureText(consent.Text, style.GetFont(), style.GetFontSize(),
                new System.Drawing.Size(Math.Max(1, width - 52), int.MaxValue), AppTypography.TextStyle(consent));
            consent.SetBounds(0, top, width, Math.Max(line, (int)Math.Ceiling(consentSize.Height) + 12)); top = consent.Bottom + gap;
            Commands(repair, options);
            feedback.SetBounds(0, top, width, string.IsNullOrEmpty(feedback.Text) ? 0 : DashboardVisual.LabelHeight(feedback, width, 20));
            feedback.Visible = feedback.Height > 0;
            Height = feedback.Visible ? feedback.Bottom : top;

            void Commands(params Button[] buttons)
            {
                foreach (var button in buttons) DownloadsVisual.Button(button);
                var widths = buttons.Select(button => DashboardVisual.TextWidth(button, button.Text) + button.Padding.Horizontal + 8).ToArray();
                var pair = widths.All(desired => desired <= (width - gap) / 2);
                for (var index = 0; index < buttons.Length; index++)
                {
                    var button = buttons[index];
                    button.SetBounds(pair ? index * ((width - gap) / 2 + gap) : 0, top,
                        pair ? (width - gap) / 2 : width, line);
                    if (!pair) top = button.Bottom + gap;
                }
                if (pair) top += line + gap;
            }
        }
        finally { arranging = false; }
        // Publish the finished measurement, never an intermediate SizeChanged.
        // The parent rebuilds its Classic coordinates before its Modern layout;
        // reentering it while this editor is arranging would retain those stale bounds.
        if (Height != previousHeight) PreferredHeightChanged?.Invoke(this, EventArgs.Empty);
    }
}
