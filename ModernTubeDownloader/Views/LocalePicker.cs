using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

/// <summary>One native, searchable language selector shared by setup and Settings.</summary>
internal sealed class LocalePickerButton : DashboardButton
{
    private readonly LocalizationService text;
    private readonly LocaleRegionIcon region = new();
    private readonly ToolTip tips = new();
    private bool choosing;
    public string SelectedLanguage { get; private set; } = "en";
    public Func<Form, IDisposable?>? RegisterDialog { get; set; }
    public event EventHandler? SelectedLanguageChanged;

    public LocalePickerButton(LocalizationService text)
    {
        this.text = text; AutoEllipsis = true;
        Controls.Add(region); region.Click += (_, _) => PerformClick();
        AppUi.Secondary(this); AppUi.BindForeground(this); AppTypography.Bind(this, 10.5f);
        TextAlign = ContentAlignment.MiddleLeft; SizeChanged += (_, _) => LayoutFlag();
        text.LanguageChanged += TextChangedLanguage;
        Click += async (_, _) =>
        {
            if (choosing || FindForm() is not { } owner) return;
            choosing = true;
            try
            {
                using var chooser = new LocalePickerForm(text, SelectedLanguage);
                using var registration = RegisterDialog?.Invoke(chooser);
                if (await chooser.ShowDialog(owner) == DialogResult.OK && chooser.Selection is { } selection) SelectLanguage(selection, true);
            }
            finally { choosing = false; }
        };
        RefreshLabel();
    }

    public void SelectLanguage(string code, bool notify = false)
    {
        var normalized = LocaleRegistry.Normalize(code); var changed = SelectedLanguage != normalized;
        SelectedLanguage = normalized; RefreshLabel();
        if (changed && notify) SelectedLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshLabel()
    {
        var locale = LocaleRegistry.Get(SelectedLanguage);
        Text = locale.DisplayName(text); AccessibleName = text["Settings.Language"] + ": " + Text;
        region.Region = locale.Region; tips.SetToolTip(this, Text + " (" + locale.CultureName + ")"); LayoutFlag();
    }
    private void LayoutFlag()
    {
        var size = Math.Min(30, Math.Max(12, Height - 12)); region.SetBounds(10, Math.Max(0, (Height - size * 2 / 3) / 2), size, size * 2 / 3);
        DashboardVisual.SetPadding(this, new Padding(size + 20, 0, 10, 0));
    }
    private void TextChangedLanguage(object? sender, EventArgs e) => RefreshLabel();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { text.LanguageChanged -= TextChangedLanguage; tips.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class LocalePickerForm : Form
{
    private readonly LocalizationService text;
    private readonly string selected;
    private readonly Panel surface = new() { Dock = DockStyle.Fill };
    private readonly Label heading = AppUi.Heading("", 18), hint = AppUi.Muted(), count = AppUi.Muted();
    private readonly TextBox search = new() { AccessibleAutomationId = "LocaleSearch" };
    private readonly ExperienceScrollPanel list = new() { AutoScroll = true, AccessibleAutomationId = "LocaleList" };
    private readonly Button close = new DashboardButton();
    private readonly List<(AppLocale Locale, Button Button)> rows = [];
    private bool arranging;
    public string? Selection { get; private set; }

    public LocalePickerForm(LocalizationService text, string selected)
    {
        this.text = text; this.selected = LocaleRegistry.Normalize(selected);
        Text = text["Settings.Language"]; AccessibilityObject.AutomationId = "LocalePickerWindow";
        Size = new(720, 760); MinimumSize = new(560, 520); AppBranding.Apply(this);
        Controls.Add(surface); ExperienceVisual.Surface(surface); ExperienceVisual.Surface(list);
        AppTypography.SetDashboardPresentation(surface, true); ExperienceVisual.ApplyWindow(this);
        heading.Text = Text; hint.Text = text["Language.RegionHint"]; hint.Multiline = true;
        search.Placeholder = text["Language.Search"]; search.AccessibleName = search.Placeholder; AppUi.Input(search);
        close.Text = text["Common.Close"]; close.AccessibleAutomationId = "LocaleClose"; ExperienceVisual.Action(close);
        close.Click += (_, _) => Close(); search.TextChanged += (_, _) => Filter();
        surface.Controls.AddRange([heading, hint, search, list, count, close]);
        foreach (var locale in LocaleRegistry.Search(null, text))
        {
            var button = new DashboardButton { Text = locale.DisplayName(text), TextAlign = ContentAlignment.MiddleLeft,
                AccessibleAutomationId = "LocaleChoice-" + locale.Code, AccessibleName = locale.DisplayName(text) + (locale.Code == this.selected ? " · " + text["Experience.Selected"] : "") };
            var flag = new LocaleRegionIcon { Region = locale.Region }; button.Controls.Add(flag);
            flag.Click += (_, _) => button.PerformClick();
            button.SizeChanged += (_, _) => { flag.SetBounds(14, Math.Max(0, (button.Height - 22) / 2), 33, 22); DashboardVisual.SetPadding(button, new Padding(60, 0, 14, 0)); };
            ExperienceVisual.Action(button, locale.Code == this.selected); button.AutoEllipsis = true;
            button.Click += (_, _) => { Selection = locale.Code; DialogResult = DialogResult.OK; };
            rows.Add((locale, button)); list.Controls.Add(button);
        }
        SizeChanged += (_, _) => LayoutPage(); surface.SizeChanged += (_, _) => LayoutPage(); list.ViewportChanged += (_, _) => ArrangeRows();
        AppTypography.RegisterLayout(surface, LayoutPage); AppTypography.CaptureRecipes(surface).Invoke(); Filter();
        Shown += (_, _) => search.Select();
    }

    private void Filter()
    {
        var matches = LocaleRegistry.Search(search.Text, text).Select(locale => locale.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var (locale, button) in rows) button.Visible = matches.Contains(locale.Code);
        count.Text = text.Get("Language.Results", matches.Count, rows.Count);
        list.VerticalScrollProperties.Value = list.VerticalScrollProperties.Minimum; LayoutPage();
    }
    private void LayoutPage()
    {
        var width = Math.Max(1, ClientSize.Width - 40); var line = AppTypography.TextHeight(40);
        heading.SetBounds(20, 16, width, DashboardVisual.LabelHeight(heading, width, 3));
        hint.SetBounds(20, heading.Bottom + 10, width, DashboardVisual.LabelHeight(hint, width, 6));
        search.SetBounds(20, hint.Bottom + 12, width, line);
        close.SetBounds(20 + width - Math.Min(width / 2, DashboardVisual.ActionWidth(close)), ClientSize.Height - line - 20, Math.Min(width / 2, DashboardVisual.ActionWidth(close)), line);
        count.SetBounds(20, close.Top, Math.Max(1, close.Left - 30), line);
        list.SetBounds(20, search.Bottom + 12, width, Math.Max(1, close.Top - search.Bottom - 24)); ArrangeRows();
    }
    private void ArrangeRows()
    {
        if (arranging) return; arranging = true;
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var viewport = list.DisplayRectangle; var y = viewport.Y; var width = Math.Max(1, viewport.Width - 6);
                foreach (var (_, button) in rows)
                {
                    if (!button.Visible) { button.SetBounds(0, 0, 0, 0); continue; }
                    button.SetBounds(viewport.X, y, width, AppTypography.TextHeight(46)); y = button.Bottom + 7;
                }
                if (list.DisplayRectangle.Width == viewport.Width) break;
            }
        }
        finally { arranging = false; }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    { base.OnKeyDown(e); if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; Close(); } }
}

/// <summary>Local geometric region cues, drawn at native device resolution; English uses a neutral globe.</summary>
internal sealed class LocaleRegionIcon : Control
{
    private string region = "neutral";
    public string Region { get => region; set { region = value; Invalidate(); } }
    public LocaleRegionIcon()
    {
        TabStop = false; SetControlBehavior(ControlBehaviors.Selectable, false);
        AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
        Style.BackgroundColor = SKColors.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Canvas.Save();
        try
        {
            e.Canvas.Scale(LogicalToDeviceUnits(Width) / 30f, LogicalToDeviceUnits(Height) / 20f);
            using var paint = new SKPaint { IsAntialias = true };
            void Rect(float x, float y, float width, float height, string color)
            { paint.Style = SKPaintStyle.Fill; paint.Color = SKColor.Parse(color); e.Canvas.DrawRect(x, y, width, height, paint); }
            void Stripes(bool vertical, params string[] colors)
            { for (var i = 0; i < colors.Length; i++) Rect(vertical ? i * 30f / colors.Length : 0, vertical ? 0 : i * 20f / colors.Length, vertical ? 30f / colors.Length : 30, vertical ? 20 : 20f / colors.Length, colors[i]); }
            void Polygon(string color, params SKPoint[] points)
            {
                using var path = new SKPath(); path.AddPoly(points); paint.Style = SKPaintStyle.Fill;
                paint.Color = SKColor.Parse(color); e.Canvas.DrawPath(path, paint);
            }
            void Star(float x, float y, float radius, string color, float angle = -90)
            {
                var points = Enumerable.Range(0, 10).Select(i =>
                { var radians = (angle + i * 36) * MathF.PI / 180; var size = radius * (i % 2 == 0 ? 1 : .382f); return new SKPoint(x + MathF.Cos(radians) * size, y + MathF.Sin(radians) * size); }).ToArray();
                Polygon(color, points);
            }
            switch (region)
            {
                case "PL": Stripes(false, "#FFFFFF", "#DC143C"); break;
                case "DE": Stripes(false, "#000000", "#DD0000", "#FFCC00"); break;
                case "FR": Stripes(true, "#002395", "#FFFFFF", "#ED2939"); break;
                case "IT": Stripes(true, "#009246", "#FFFFFF", "#CE2B37"); break;
                case "NL": Stripes(false, "#AE1C28", "#FFFFFF", "#21468B"); break;
                case "RU": Stripes(false, "#FFFFFF", "#0039A6", "#D52B1E"); break;
                case "HU": Stripes(false, "#CD2A3E", "#FFFFFF", "#436F4D"); break;
                case "JP": Rect(0,0,30,20,"#FFFFFF"); paint.Color = SKColor.Parse("#BC002D"); e.Canvas.DrawCircle(15,10,6,paint); break;
                case "CZ":
                    Stripes(false,"#FFFFFF","#D7141A"); Polygon("#11457E",new(0,0),new(15,10),new(0,20)); break;
                case "SK":
                    Stripes(false,"#FFFFFF","#0B4EA2","#EE1C25");
                    Polygon("#FFFFFF",new(6,5),new(15,5),new(14,12),new(10.5f,15),new(7,12));
                    Polygon("#EE1C25",new(7,6),new(14,6),new(13,11.5f),new(10.5f,14),new(8,11.5f));
                    Rect(10,6.5f,1,6,"#FFFFFF"); Rect(8.5f,8,4,1,"#FFFFFF"); Rect(8,10,5,1,"#FFFFFF");
                    paint.Color=SKColor.Parse("#0B4EA2"); e.Canvas.DrawCircle(9,12.5f,1.5f,paint); e.Canvas.DrawCircle(12,12.5f,1.5f,paint); e.Canvas.DrawCircle(10.5f,12,1.5f,paint); break;
                case "ES":
                    Rect(0,0,30,20,"#AA151B"); Rect(0,5,30,10,"#F1BF00");
                    Polygon("#FFFFFF",new(7,7),new(12,7),new(12,12),new(9.5f,14),new(7,12));
                    Rect(7.5f,7.5f,2,2,"#AA151B"); Rect(10,10,1.5f,2,"#AA151B"); Rect(8,6,3,1,"#AA151B");
                    Rect(6,8,1,5,"#FFFFFF"); Rect(13,8,1,5,"#FFFFFF"); break;
                case "BR":
                    Rect(0,0,30,20,"#009C3B"); Polygon("#FFDF00",new(15,2),new(28,10),new(15,18),new(2,10));
                    paint.Color=SKColor.Parse("#002776"); e.Canvas.DrawCircle(15,10,5,paint);
                    paint.Color=SKColors.White; paint.Style=SKPaintStyle.Stroke; paint.StrokeWidth=.8f;
                    using (var band=new SKPath()) { band.MoveTo(10.2f,8.5f); band.QuadTo(15,7.8f,19.7f,11.5f); e.Canvas.DrawPath(band,paint); }
                    paint.Style=SKPaintStyle.Fill; e.Canvas.DrawCircle(14,12,.35f,paint); e.Canvas.DrawCircle(16,10.5f,.3f,paint); break;
                case "TR":
                    Rect(0,0,30,20,"#E30A17"); paint.Color=SKColors.White; e.Canvas.DrawCircle(12,10,5,paint);
                    paint.Color=SKColor.Parse("#E30A17"); e.Canvas.DrawCircle(13.3f,10,4,paint); Star(18.5f,10,3,"#FFFFFF",-18); break;
                case "CN":
                    Rect(0,0,30,20,"#DE2910"); Star(5,5,3,"#FFDE00");
                    Star(10,2,1,"#FFDE00",156); Star(12,4,1,"#FFDE00",172); Star(12,7,1,"#FFDE00",196); Star(10,9,1,"#FFDE00",218); break;
                case "KR":
                    Rect(0,0,30,20,"#FFFFFF"); paint.Color=SKColor.Parse("#CD2E3A"); e.Canvas.DrawCircle(15,10,4.8f,paint);
                    using (var lower = new SKPath())
                    { lower.MoveTo(10.2f,10); lower.CubicTo(10.2f,16.4f,19.8f,16.4f,19.8f,10); lower.CubicTo(19.8f,6.8f,15,6.8f,15,10); lower.CubicTo(15,13.2f,10.2f,13.2f,10.2f,10); lower.Close(); paint.Color=SKColor.Parse("#0047A0"); e.Canvas.DrawPath(lower,paint); }
                    void Trigram(float x,float y,float angle,int breaks)
                    {
                        e.Canvas.Save(); e.Canvas.Translate(x,y); e.Canvas.RotateDegrees(angle);
                        for(var i=0;i<3;i++)
                        { if((breaks&(1<<i))==0) Rect(-2.5f,-1.8f+i*1.4f,5,.8f,"#000000"); else { Rect(-2.5f,-1.8f+i*1.4f,2.1f,.8f,"#000000"); Rect(.4f,-1.8f+i*1.4f,2.1f,.8f,"#000000"); } }
                        e.Canvas.Restore();
                    }
                    Trigram(7,4.8f,-55,0); Trigram(23,15.2f,-55,7); Trigram(23,4.8f,55,5); Trigram(7,15.2f,55,2); break;
                default:
                    paint.Color = AppUi.GetColor(AppThemeTokens.TextSecondary); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1.5f;
                    e.Canvas.DrawCircle(15,10,8,paint); e.Canvas.DrawOval(new SKRect(11,2,19,18),paint); e.Canvas.DrawLine(7,10,23,10,paint); break;
            }
        }
        finally { e.Canvas.Restore(); }
    }
}
