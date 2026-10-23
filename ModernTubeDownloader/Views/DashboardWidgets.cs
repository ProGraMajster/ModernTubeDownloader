using System.Diagnostics;
using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

/// <summary>Bounded presentation history. No timer, service, job or persistence ownership.</summary>
internal sealed class SpeedSamples(int capacity = 120)
{
    private readonly double[] samples = new double[capacity > 1 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private int next, count;
    public int Count => count;
    public int Capacity => samples.Length;
    public double HorizontalFraction(int index)
    {
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return 1 - (count - 1 - index) / (double)samples.Length;
    }
    public void Add(double value) { samples[next] = double.IsFinite(value) ? Math.Max(0, value) : 0; next = (next + 1) % samples.Length; count = Math.Min(samples.Length, count + 1); }
    public double[] Snapshot() => Enumerable.Range(0, count).Select(i => samples[(next - count + i + samples.Length) % samples.Length]).ToArray();
}

internal sealed class SpeedChart : Control
{
    internal readonly SpeedSamples Samples = new();
    public string NoDataText { get; set; } = "";
    public bool CompactAxes { get; set; }
    public SpeedChart() { AppUi.BindBackground(this, AppThemeTokens.Surface); AppTypography.Bind(this, 8.5f); }
    public void Sample(double speed) { Samples.Add(speed); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Canvas.Save(); e.Canvas.Scale((float)Scaling);
        try
        {
            var values = Samples.Snapshot();
            var maximum = Math.Max(1, values.DefaultIfEmpty(0).Max());
            var maximumText = DashboardStyle.Bytes((long)maximum) + "/s";
            var middleText = DashboardStyle.Bytes((long)(maximum / 2)) + "/s";
            var lineHeight = AppTypography.TextHeight(20);
            var left = Math.Max(AppTypography.TextHeight(32), Math.Max(DashboardVisual.TextWidth(this, maximumText), DashboardVisual.TextWidth(this, middleText)) + 10);
            var width = Math.Max(1, Width - left - 8); var height = Math.Max(1, Height - (CompactAxes ? 0 : lineHeight) - 8);
            using var grid = new SKPaint { Color = AppUi.GetColor(AppThemeTokens.Border).WithAlpha(CompactAxes ? (byte)55 : (byte)110), StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke };
            for (var i = 0; i <= 4; i++) e.Canvas.DrawLine(left, 4 + height * i / 4f, width + left, 4 + height * i / 4f, grid);
            for (var i = 0; i <= 4; i++) e.Canvas.DrawLine(left + width * i / 4f, 4, left + width * i / 4f, height + 4, grid);
            void Text(string text, int x, int y, int w, int h, ModernFormsNext.ContentAlignment align)
                => e.Canvas.DrawText(text, CurrentStyle.GetFont(), CurrentStyle.GetFontSize(), new System.Drawing.Rectangle(x,y,w,h), AppUi.GetColor(AppThemeTokens.TextSecondary), align, maxLines:1, ellipsis:true);
            Text("0", 0, height - lineHeight / 2, left - 8, lineHeight, ModernFormsNext.ContentAlignment.MiddleRight);
            if (!CompactAxes)
            {
                Text(Samples.Capacity + " s", left, height + 4, AppTypography.TextHeight(64), lineHeight, ModernFormsNext.ContentAlignment.MiddleLeft);
                Text("0 s", Width - AppTypography.TextHeight(48), height + 4, AppTypography.TextHeight(40), lineHeight, ModernFormsNext.ContentAlignment.MiddleRight);
            }
            if (values.Length < 2 || !values.Any(v => v > 0))
            { Text(NoDataText, left + 8, (height - lineHeight) / 2, width - 16, lineHeight, ModernFormsNext.ContentAlignment.MiddleCenter); return; }
            Text(maximumText, 0, 0, left - 8, lineHeight, ModernFormsNext.ContentAlignment.MiddleRight);
            Text(middleText, 0, (height - lineHeight) / 2, left - 8, lineHeight, ModernFormsNext.ContentAlignment.MiddleRight);
            using var path = new SKPath();
            for (var i = 0; i < values.Length; i++) { var x = left + width * (float)Samples.HorizontalFraction(i); var y = 4 + height * (1 - (float)(values[i] / maximum)); if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y); }
            using var area = new SKPath(path); area.LineTo(left + width, height + 4); area.LineTo(left + width * (float)Samples.HorizontalFraction(0), height + 4); area.Close();
            using var fill = new SKPaint { Color = AppUi.GetColor(AppThemeTokens.Accent).WithAlpha(30), IsAntialias = true };
            e.Canvas.DrawPath(area, fill);
            using var line = new SKPaint { Color = AppUi.GetColor(AppThemeTokens.Accent), StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            e.Canvas.DrawPath(path, line);
        }
        finally { e.Canvas.Restore(); }
    }
}

internal sealed class DashboardWidgets : Panel
{
    private readonly AppServices services;
    private readonly bool live;
    private readonly Panel statistics = new(), speed = new(), processes = new();
    private readonly Label statisticsTitle = AppUi.Heading("", 10.5f), disk = AppUi.Muted(), timing = AppUi.Muted();
    private readonly List<(Label Caption, Label Value)> counts = [];
    private readonly Label speedTitle = AppUi.Heading("", 10.5f), speedValue = AppUi.Muted(), processTitle = AppUi.Heading("", 10.5f), processBody = AppUi.Muted();
    private readonly DashboardProgress diskBar = new() { Minimum = 0, Maximum = 100 };
    private readonly Panel statisticsDivider = new();
    private readonly Label rateCaption = AppUi.Muted(), rateValue = AppUi.Muted(), etaCaption = AppUi.Muted(), etaValue = AppUi.Muted(), diskCaption = AppUi.Muted();
    private readonly SpeedChart chart = new();
    private readonly Label currentCaption = AppUi.Muted();
    private readonly ModernFormsNext.Timer timer = new() { Interval = 1000 };
    private int ticks;
    private bool disposed, diskPending;
    private string diskPath = "";
    public Func<string?>? SelectedPath { get; set; }
    public DashboardWidgets(AppServices services, bool live = false)
    {
        this.services = services; this.live = live; AccessibleAutomationId = live ? "LiveDashboardWidgets" : "DashboardWidgets";
        AppUi.BindBackground(this);
        foreach (var panel in new[] { statistics, speed, processes }) { DashboardStyle.Card(panel); Controls.Add(panel); }
        disk.Multiline = processBody.Multiline = true;
        AppTypography.Bind(speedValue, 11);
        AppTypography.Bind(disk, 9.5f); AppTypography.Bind(processBody, 9.5f);
        statistics.Controls.AddRange([statisticsTitle, disk, timing, diskBar]);
        for (var i = 0; i < 4; i++)
        {
            var caption = AppUi.Muted(); var value = AppUi.Muted();
            AppTypography.Bind(caption, 9.5f); AppTypography.Bind(value, 9.5f);
            caption.AutoEllipsis = true; value.TextAlign = ModernFormsNext.ContentAlignment.MiddleRight;
            counts.Add((caption, value)); statistics.Controls.AddRange([caption, value]);
        }
        for (var i=0;i<4;i++)
        {
            var token = new[] { AppThemeTokens.Info, AppThemeTokens.TextSecondary, AppThemeTokens.Success, AppThemeTokens.Error }[i];
            Control marker;
            if (live) { var dot = new Panel(); AppUi.BindBackground(dot, token); DashboardVisual.Flat(dot); dot.Style.Border.Radius = 3; marker = dot; }
            else marker = new AppVectorIcon(new[] { AppIconKind.Download, AppIconKind.Queue, AppIconKind.Check, AppIconKind.Info }[i], 14, token);
            marker.Name = "StatDot" + i; marker.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; statistics.Controls.Add(marker);
        }
        if (!live)
        {
            chart.CompactAxes = true;
            foreach (var panel in new[] { statistics, speed, processes }) DownloadsVisual.Card(panel);
            foreach (var label in new[] { statisticsTitle, speedTitle, processTitle }) AppTypography.BindSemibold(label, 10.5f);
            AppTypography.Bind(currentCaption, 9); speed.Controls.Add(currentCaption);
            AppUi.BindGradient(this, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd);
            DashboardVisual.Flat(statisticsDivider); AppUi.BindBackground(statisticsDivider, AppThemeTokens.Border);
            foreach (var label in new[] { rateCaption, rateValue, etaCaption, etaValue, diskCaption }) AppTypography.Bind(label, 9.5f);
            AppUi.BindForeground(rateValue); AppUi.BindForeground(etaValue);
            statistics.Controls.AddRange([statisticsDivider, rateCaption, rateValue, etaCaption, etaValue, diskCaption]);
        }
        diskBar.Padding = new Padding(0); diskBar.Style.Border.Width = 0; AppUi.BindBackground(diskBar, AppThemeTokens.SurfaceSecondary);
        speed.Controls.AddRange([speedTitle, speedValue, chart]); processes.Controls.AddRange([processTitle, processBody]);
        timer.Tick += Tick; VisibleChanged += (_, _) => { if (Visible) { timer.Start(); RefreshValues(); } else timer.Stop(); };
        SizeChanged += (_, _) => LayoutWidgets(); AppTypography.RegisterLayout(this, LayoutWidgets);
    }
    private void Tick(object? sender, EventArgs e) { if (!Visible || disposed) return; ticks++; RefreshValues(sample: true); }
    internal void RefreshValues(bool sample = false)
    {
        var text = services.Localization; var items = services.Queue.Snapshot(); var sessions = services.Live.Snapshot();
        var active = items.Where(i => i.Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing).ToArray();
        var rate = live ? sessions.Where(s => s.IsActive).Sum(s => s.CurrentSpeed ?? 0) : active.Sum(i => i.SpeedBytesPerSecond ?? 0);
        statisticsTitle.Text = text[live ? "Dashboard.LiveStatistics" : "Dashboard.Statistics"];
        var countText = live ? text.Get("Dashboard.LiveCounts", sessions.Count(s => s.IsActive), sessions.Count(s => s.State == LiveSessionState.WaitingForLive), sessions.Count(s => s.State == LiveSessionState.Completed), DashboardStyle.Bytes(sessions.Sum(s => s.BytesWritten)))
            : text.Get("Dashboard.Counts", active.Length, items.Count(i => i.Status is DownloadStatus.Queued or DownloadStatus.Waiting), items.Count(i => i.Status == DownloadStatus.Completed), items.Count(i => i.Status is DownloadStatus.Failed or DownloadStatus.Partial));
        var rows = countText.Split('\n');
        for (var i = 0; i < counts.Count; i++)
        {
            var fields = System.Text.RegularExpressions.Regex.Split(rows[i], @"\s{2,}", System.Text.RegularExpressions.RegexOptions.None);
            counts[i].Caption.Text = fields[0]; counts[i].Value.Text = fields.Length > 1 ? fields[^1] : "";
        }
        var eta = active.Length > 0 && active.All(i => i.Eta.HasValue) ? active.Max(i => i.Eta!.Value).ToString(@"hh\:mm\:ss") : "—";
        timing.Text = text.Get("Dashboard.EtaActive", eta);
        timing.Visible = !live; // VOD's remaining time is not an estimate for a live recording.
        if (!live)
        {
            timing.Visible = false;
            rateCaption.Text = text["Dashboard.TransferRate"]; rateValue.Text = DashboardStyle.Bytes((long)rate) + "/s";
            etaCaption.Text = text["Dashboard.RemainingActive"]; etaValue.Text = eta;
            diskCaption.Text = text["Dashboard.DiskSpace"];
        }
        chart.NoDataText = text["Dashboard.NoSpeed"]; speedTitle.Text = text["Dashboard.SpeedChart"]; speedValue.Text = DashboardStyle.Bytes((long)rate) + "/s";
        if (!live) currentCaption.Text = text["Dashboard.Current"];
        if (sample && (rate > 0 || chart.Samples.Snapshot().Any(v => v > 0))) chart.Sample(rate);
        var observed = ProcessActivity.Snapshot(); processTitle.Text = text["Dashboard.ActiveProcesses"] + $" ({observed.Count})"; processBody.Text = observed.Count == 0 ? text["Dashboard.NoProcesses"] : string.Join("\n", observed.Select(p => p.Name + "  ·  " + p.Elapsed.ToString(@"mm\:ss")));
        var storage = services.Queue.Storage.State;
        if (storage.IsBlocked) { disk.Text = text[storage.MessageKey]; AppUi.BindForeground(disk, AppThemeTokens.Warning); }
        var target = SelectedPath?.Invoke() ?? services.Settings.Current.FinalOutputDirectory;
        if (!diskPending && (target != diskPath || ticks % 15 == 0)) { diskPath = target; _ = RefreshDiskAsync(target); }
        LayoutWidgets();
    }
    private async Task RefreshDiskAsync(string target)
    {
        diskPending = true;
        var result = await Task.Run(() =>
        {
            try { var drive = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(target))!); return (Ok: drive.IsReady, Free: drive.AvailableFreeSpace, Total: drive.TotalSize); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return (Ok: false, Free: 0L, Total: 0L); }
        });
        Application.RunOnUIThread(() =>
        {
            diskPending = false; if (disposed || diskPath != target || services.Queue.Storage.State.IsBlocked) return;
            disk.Text = result.Ok ? services.Localization.Get(live ? "Dashboard.Disk" : "Dashboard.DiskUsed", DashboardStyle.Bytes(live ? result.Free : result.Total - result.Free), DashboardStyle.Bytes(result.Total)) : services.Localization["Dashboard.DiskUnavailable"];
            AppUi.BindForeground(disk, result.Ok && result.Free > 1073741824 ? AppThemeTokens.TextSecondary : AppThemeTokens.Warning);
            diskBar.Value = result.Total > 0 ? (int)Math.Clamp((result.Total - result.Free) * 100d / result.Total, 0, 100) : 0;
        });
    }
    private void LayoutWidgets()
    {
        // Hidden dashboard chrome must not recreate a scroll extent after recovery.
        if (Width <= 0) { Height = 0; return; }
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var chartVisible = p.SpeedChart && p.Widgets == DashboardWidgetsMode.All;
        var processesVisible = p.ActiveProcesses && p.Widgets == DashboardWidgetsMode.All;
        statistics.Visible = p.Statistics; speed.Visible = chartVisible; processes.Visible = processesVisible;
        var n = (p.Statistics ? 1 : 0) + (chartVisible ? 1 : 0) + (processesVisible ? 1 : 0); var stacked = Width < 860 || AppTypography.Scale > 1.25;
        if (n == 0) { Height = 0; return; }
        var h = AppTypography.TextHeight(146); Height = stacked ? h * n + 10 * (n - 1) : h;
        var w = stacked ? Width : (Width - 12 * (n - 1)) / n; var index = 0; var x = 0;
        var visible = new[] { statistics, speed, processes }.Where(c => c.Visible).ToArray();
        foreach (var panel in visible)
        {
            var cellWidth = !stacked && n == 3 ? panel == statistics ? (int)((Width - 24) * .42) : panel == speed ? (int)((Width - 24) * .32) : Width - 24 - (int)((Width - 24) * .42) - (int)((Width - 24) * .32) : w;
            panel.SetBounds(stacked ? 0 : x, stacked ? index * (h + 10) : 0, Math.Max(1, cellWidth), h); index++; x += cellWidth + 12;
        }
        var line = AppTypography.TextHeight(23); var sw = statistics.Width; var cw = speed.Width; var pw = processes.Width;
        statisticsTitle.SetBounds(14, 8, sw - 28, line);
        for (var i = 0; i < counts.Count; i++)
        {
            counts[i].Caption.SetBounds(28, line + 10 + i * line, Math.Max(1, sw / 2 - 90), line);
            counts[i].Value.SetBounds(sw / 2 - 58, line + 10 + i * line, 44, line);
        }
        foreach(var dot in statistics.Controls.Where(c => c.Name.StartsWith("StatDot"))) { var i=int.Parse(dot.Name[7..]); dot.SetBounds(14,line+17+i*line,6,6); }
        timing.SetBounds(sw / 2, line * 3 + 10, Math.Max(1, sw / 2 - 14), line);
        disk.SetBounds(sw / 2, line + 10, Math.Max(1, sw / 2 - 14), line * 3); diskBar.SetBounds(sw / 2, h - 20, Math.Max(1, sw / 2 - 14), 5);
        if (!live)
        {
            var dividerX = Math.Clamp((int)(sw * .33), AppTypography.TextHeight(160), Math.Max(AppTypography.TextHeight(160), sw / 2));
            statisticsDivider.SetBounds(dividerX, 16, 1, h - 32);
            for (var i = 0; i < counts.Count; i++)
            {
                counts[i].Caption.SetBounds(36, line + 18 + i * line, Math.Max(1, dividerX - 80), line);
                counts[i].Value.SetBounds(dividerX - 42, line + 18 + i * line, 28, line);
                statistics.Controls.First(c => c.Name == "StatDot" + i).SetBounds(14, line + 22 + i * line, 14, 14);
            }
            var rightX = dividerX + 20; var rightWidth = Math.Max(1, sw - rightX - 16);
            var captionWidth = Math.Min(rightWidth * 2 / 3, Math.Max(DashboardVisual.TextWidth(rateCaption, rateCaption.Text), DashboardVisual.TextWidth(etaCaption, etaCaption.Text)) + 12);
            rateCaption.SetBounds(rightX, 16, captionWidth, line); rateValue.SetBounds(rightX + captionWidth, 16, rightWidth - captionWidth, line);
            etaCaption.SetBounds(rightX, 16 + line, captionWidth, line); etaValue.SetBounds(rightX + captionWidth, etaCaption.Top, rightWidth - captionWidth, line);
            diskCaption.SetBounds(rightX, 24 + line * 2, rightWidth, line);
            diskBar.SetBounds(rightX, diskCaption.Bottom + 4, rightWidth, 7);
            disk.SetBounds(rightX, diskBar.Bottom + 4, rightWidth, line);
        }
        speedTitle.SetBounds(14, 8, cw - 116, line); speedValue.SetBounds(cw - 100, 8, 86, line); speedValue.TextAlign = ContentAlignment.MiddleRight;
        chart.SetBounds(12, line + 14, cw - 24, Math.Max(20, h - line - 24));
        if (!live)
        {
            currentCaption.SetBounds(cw - 94, 10, 80, line); currentCaption.TextAlign = ContentAlignment.MiddleRight;
            speedValue.SetBounds(cw - 100, 10 + line, 86, line);
            chart.SetBounds(12, AppTypography.TextHeight(50), cw - 24, Math.Max(20, h - AppTypography.TextHeight(58)));
            speedValue.BringToFront(); currentCaption.BringToFront();
            AppUi.BindGradient(chart, DownloadsVisual.Panel, DownloadsVisual.PanelEnd);
        }
        processTitle.SetBounds(14, 8, pw - 28, line); processBody.SetBounds(14, line + 10, pw - 28, h - line - 18);
        if (!live) processBody.Top += 8;
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; timer.Tick -= Tick; timer.Dispose(); } base.Dispose(disposing); }
}

internal static class DashboardStyle
{
    public static string OutputFormat(DownloadQueueItem item)
    {
        if (item.Status is DownloadStatus.Completed or DownloadStatus.Partial && !string.IsNullOrWhiteSpace(item.FinalFile))
        {
            var extension = System.IO.Path.GetExtension(item.FinalFile).TrimStart('.');
            if (extension.Length > 0) return extension.ToUpperInvariant();
        }
        if (item.SelectedFormats is { } formats)
            return FfmpegService.ChooseContainer(formats, item.PreferredVideoContainer).ToUpperInvariant();
        if (QualityPreset.Find(item.QualityPresetId).AudioOnly || item.PreferredVideoContainer == PreferredVideoContainer.Auto) return "—";
        return item.PreferredVideoContainer.ToString().ToUpperInvariant();
    }
    public static void Card(Control control) { AppUi.Card(control); AppUi.BindBorder(control, AppThemeTokens.Border); AppUi.BindGradient(control, AppThemeTokens.SurfaceSecondary, AppThemeTokens.Surface); control.Style.Border.Width = 1; control.Style.Border.Radius = Math.Min(9, AppearancePresentation.Current.Global.CornerRadius); }
    public static string Duration(double? seconds)
    {
        if (seconds is null) return "";
        if (!double.IsFinite(seconds.Value) || seconds < 0 || seconds >= TimeSpan.MaxValue.TotalSeconds) return "—";
        var time = TimeSpan.FromSeconds(seconds.Value);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}" : $"{time.Minutes}:{time.Seconds:D2}";
    }
    public static string Bytes(long bytes) { string[] units = ["B", "KB", "MB", "GB", "TB"]; var value = (double)Math.Max(0, bytes); var i = 0; while (value >= 1024 && i < 4) { value /= 1024; i++; } return $"{value:0.#} {units[i]}"; }
}
