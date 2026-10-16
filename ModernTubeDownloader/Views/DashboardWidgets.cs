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
    public void Add(double value) { samples[next] = double.IsFinite(value) ? Math.Max(0, value) : 0; next = (next + 1) % samples.Length; count = Math.Min(samples.Length, count + 1); }
    public double[] Snapshot() => Enumerable.Range(0, count).Select(i => samples[(next - count + i + samples.Length) % samples.Length]).ToArray();
}

internal sealed class SpeedChart : Control
{
    internal readonly SpeedSamples Samples = new();
    public SpeedChart() { AppUi.BindBackground(this, AppThemeTokens.Surface); }
    public void Sample(double speed) { Samples.Add(speed); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var values = Samples.Snapshot();
        using var grid = new SKPaint { Color = AppUi.GetColor(AppThemeTokens.Border), StrokeWidth = 1, IsAntialias = true, Style = SKPaintStyle.Stroke };
        var width = Math.Max(1, Width - 8); var height = Math.Max(1, Height - 8);
        for (var i = 0; i <= 4; i++) e.Canvas.DrawLine(4, 4 + height * i / 4f, width + 4, 4 + height * i / 4f, grid);
        if (values.Length < 2) return;
        var max = Math.Max(1, values.Max()); using var path = new SKPath();
        for (var i = 0; i < values.Length; i++) { var x = 4 + width * i / 119f; var y = 4 + height * (1 - (float)(values[i] / max)); if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y); }
        using var line = new SKPaint { Color = AppUi.GetColor(AppThemeTokens.Accent), StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        e.Canvas.DrawPath(path, line);
    }
}

internal sealed class DashboardWidgets : Panel
{
    private readonly AppServices services;
    private readonly bool live;
    private readonly Panel statistics = new(), speed = new(), processes = new();
    private readonly Label statisticsTitle = AppUi.Heading("", 10.5f), statisticsBody = AppUi.Muted(), disk = AppUi.Muted(), timing = AppUi.Muted();
    private readonly Label speedTitle = AppUi.Heading("", 10.5f), speedValue = AppUi.Muted(), processTitle = AppUi.Heading("", 10.5f), processBody = AppUi.Muted();
    private readonly ProgressBar diskBar = new() { Minimum = 0, Maximum = 100 };
    private readonly SpeedChart chart = new();
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
        statisticsBody.Multiline = disk.Multiline = processBody.Multiline = true;
        AppTypography.Bind(statisticsBody, 9.5f); AppTypography.Bind(disk, 9.5f); AppTypography.Bind(processBody, 9.5f);
        statistics.Controls.AddRange([statisticsTitle, statisticsBody, disk, timing, diskBar]);
        diskBar.Padding = new Padding(0); diskBar.Style.Border.Width = 0; AppUi.BindBackground(diskBar, AppThemeTokens.SurfaceSecondary);
        speed.Controls.AddRange([speedTitle, speedValue, chart]); processes.Controls.AddRange([processTitle, processBody]);
        timer.Tick += Tick; VisibleChanged += (_, _) => { if (Visible) { timer.Start(); RefreshValues(); } else timer.Stop(); };
        SizeChanged += (_, _) => LayoutWidgets(); AppTypography.RegisterLayout(this, LayoutWidgets);
    }
    private void Tick(object? sender, EventArgs e) { if (!Visible || disposed) return; ticks++; RefreshValues(); }
    internal void RefreshValues()
    {
        var text = services.Localization; var items = services.Queue.Snapshot(); var sessions = services.Live.Snapshot();
        var active = items.Where(i => i.Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing).ToArray();
        var rate = live ? sessions.Where(s => s.IsActive).Sum(s => s.CurrentSpeed ?? 0) : active.Sum(i => i.SpeedBytesPerSecond ?? 0);
        statisticsTitle.Text = text[live ? "Dashboard.LiveStatistics" : "Dashboard.Statistics"];
        statisticsBody.Text = live ? text.Get("Dashboard.LiveCounts", sessions.Count(s => s.IsActive), sessions.Count(s => s.State == LiveSessionState.WaitingForLive), sessions.Count(s => s.State == LiveSessionState.Completed), DashboardStyle.Bytes(sessions.Sum(s => s.BytesWritten)))
            : text.Get("Dashboard.Counts", active.Length, items.Count(i => i.Status is DownloadStatus.Queued or DownloadStatus.Waiting), items.Count(i => i.Status == DownloadStatus.Completed), items.Count(i => i.Status is DownloadStatus.Failed or DownloadStatus.Partial));
        var eta = active.Length > 0 && active.All(i => i.Eta.HasValue) ? active.Max(i => i.Eta!.Value).ToString(@"hh\:mm\:ss") : "—";
        timing.Text = text.Get("Dashboard.EtaActive", eta);
        speedTitle.Text = text["Dashboard.SpeedChart"]; speedValue.Text = DashboardStyle.Bytes((long)rate) + "/s · " + text.Get("Dashboard.ChartScale", DashboardStyle.Bytes((long)Math.Max(1, chart.Samples.Snapshot().DefaultIfEmpty(0).Max())));
        if (rate > 0 || chart.Samples.Snapshot().Any(v => v > 0)) chart.Sample(rate);
        processTitle.Text = text["Dashboard.ActiveProcesses"];
        var observed = ProcessActivity.Snapshot(); processBody.Text = observed.Count == 0 ? text["Dashboard.NoProcesses"] : string.Join("\n", observed.Select(p => p.Name + "  ·  " + p.Elapsed.ToString(@"mm\:ss")));
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
            disk.Text = result.Ok ? services.Localization.Get("Dashboard.Disk", DashboardStyle.Bytes(result.Free), DashboardStyle.Bytes(result.Total)) : services.Localization["Dashboard.DiskUnavailable"];
            AppUi.BindForeground(disk, result.Ok && result.Free > 1073741824 ? AppThemeTokens.TextSecondary : AppThemeTokens.Warning);
            diskBar.Value = result.Total > 0 ? (int)Math.Clamp((result.Total - result.Free) * 100d / result.Total, 0, 100) : 0;
        });
    }
    private void LayoutWidgets()
    {
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var chartVisible = p.SpeedChart && p.Widgets == DashboardWidgetsMode.All;
        var processesVisible = p.ActiveProcesses && p.Widgets == DashboardWidgetsMode.All;
        statistics.Visible = p.Statistics; speed.Visible = chartVisible; processes.Visible = processesVisible;
        var n = (p.Statistics ? 1 : 0) + (chartVisible ? 1 : 0) + (processesVisible ? 1 : 0); var stacked = Width < 860 || AppTypography.Scale > 1.25;
        if (n == 0) { Height = 0; return; }
        var h = AppTypography.TextHeight(140); Height = stacked ? h * n + 10 * (n - 1) : h;
        var w = stacked ? Width : (Width - 12 * (n - 1)) / n; var index = 0; var x = 0;
        var visible = new[] { statistics, speed, processes }.Where(c => c.Visible).ToArray();
        foreach (var panel in visible)
        {
            var cellWidth = !stacked && n == 3 ? panel == statistics ? (int)((Width - 24) * .42) : panel == speed ? (int)((Width - 24) * .32) : Width - 24 - (int)((Width - 24) * .42) - (int)((Width - 24) * .32) : w;
            panel.SetBounds(stacked ? 0 : x, stacked ? index * (h + 10) : 0, Math.Max(1, cellWidth), h); index++; x += cellWidth + 12;
        }
        var line = AppTypography.TextHeight(23); var sw = statistics.Width; var cw = speed.Width; var pw = processes.Width;
        statisticsTitle.SetBounds(14, 8, sw - 28, line); statisticsBody.SetBounds(14, line + 10, Math.Max(1, sw / 2 - 12), line * 4);
        timing.SetBounds(sw / 2, line * 3 + 10, Math.Max(1, sw / 2 - 14), line);
        disk.SetBounds(sw / 2, line + 10, Math.Max(1, sw / 2 - 14), line * 3); diskBar.SetBounds(sw / 2, h - 20, Math.Max(1, sw / 2 - 14), 5);
        speedTitle.SetBounds(14, 8, cw - 28, line); speedValue.SetBounds(14, line + 10, cw - 28, line); chart.SetBounds(12, line * 2 + 14, cw - 24, Math.Max(20, h - line * 2 - 24));
        processTitle.SetBounds(14, 8, pw - 28, line); processBody.SetBounds(14, line + 10, pw - 28, h - line - 18);
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; timer.Tick -= Tick; timer.Dispose(); } base.Dispose(disposing); }
}

internal static class DashboardStyle
{
    public static void Card(Control control) { AppUi.Card(control); AppUi.BindBorder(control, AppThemeTokens.Border); control.Style.Border.Radius = Math.Min(12, AppearancePresentation.Current.Global.CornerRadius); }
    public static string Bytes(long bytes) { string[] units = ["B", "KB", "MB", "GB", "TB"]; var value = (double)Math.Max(0, bytes); var i = 0; while (value >= 1024 && i < 4) { value /= 1024; i++; } return $"{value:0.#} {units[i]}"; }
}
