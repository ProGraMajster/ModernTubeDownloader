using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class LiveDashboardPresentationTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task PolishReconnectTimingIsMeasuredInsteadOfClippedToTwoLines(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-timing-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); services.Localization.SetLanguage("pl"); AppTypography.SetScale(scale);
                var session = new LiveSession
                {
                    Title = "Przywracanie połączenia", Channel = "QA", SourceUrl = "https://example.test/live",
                    State = LiveSessionState.Reconnecting, RecordedDuration = TimeSpan.FromHours(12),
                    BytesWritten = 123456789, ResumeCount = 123, RetryAt = DateTimeOffset.UtcNow.AddMinutes(12),
                    Parts = ["part1.mkv", "part2.mkv", "part3.mkv"]
                };
                var row = new LiveSessionCard(services, _ => { }) { Width = AppTypography.TextHeight(1200) };
                var root = new Panel(); root.Controls.Add(row); host.Show(root, row.Width, 1000);
                row.SetDashboard(true); row.UpdateSession(session); host.ProcessPendingWork();
                var timing = row.Controls.OfType<Label>().Single(label => label.Visible && label.Text.Contains(services.Localization["Live.ReconnectCount"], StringComparison.Ordinal));
                var style = timing.CurrentStyle;
                var measured = TextMeasurer.MeasureText(timing.Text, style.GetFont(), style.GetFontSize(),
                    new System.Drawing.Size(timing.Width, int.MaxValue), style.GetFontStyle());
                Assert.True(measured.Height > AppTypography.TextHeight(48), "The fixture must exercise more than the old two-line timing box.");
                Assert.True(timing.Height >= Math.Ceiling(measured.Height), "Reconnect details must fit their measured height.");
                Assert.True(timing.Bottom + 10 <= row.Height);
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(100, 1536, 1200, false)]
    [InlineData(200, 2400, 1200, false)]
    [InlineData(100, 1536, 360, true)]
    [InlineData(200, 2400, 600, true)]
    public async Task HiddenInspectorUsesFullLiveWidthAndClassicDoesNotRetainModernScrollExtent(int scale, int width, int height, bool scrolls)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-hidden-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new LiveView(services); host.Show(view, width, height); host.ProcessPendingWork();
                var url = Find<TextBox>(view, "LiveUrlInput");
                var scroll = Assert.IsAssignableFrom<ScrollableControl>(url.Parent!.Parent);
                var classicChildren = scroll.Controls.ToArray();
                var classicBounds = classicChildren.Select(control => (control.GetType().Name, control.Bounds)).ToArray();
                // A hidden MFN scrollbar retains its last Maximum. ScrollInfo exposes the
                // canonical available axes and viewport, including zero range when hidden.
                var classicScroll = Assert.IsType<AccessibleScrollInfo>(scroll.AccessibilityObject.ScrollInfo);
                Assert.Equal(scrolls, classicScroll.Vertical.IsScrollable);
                var classicPadding = url.Padding;

                AppearancePresentation.Current.ModernDashboardShell.Inspector = DashboardInspectorMode.Always;
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true); host.ProcessPendingWork();
                var inspector = Find<DashboardInspector>(view, "DashboardInspector");
                var widgets = Find<DashboardWidgets>(view, "LiveDashboardWidgets");
                var active = Find<FlowLayoutPanel>(view, "LiveActiveList");
                var narrowWidth = active.Parent!.Width;
                Assert.True(inspector.Visible); Assert.True(inspector.Width > 0);

                AppearancePresentation.Current.ModernDashboardShell.Inspector = DashboardInspectorMode.Hidden;
                AppearancePresentation.Current.ModernDashboardShell.Widgets = DashboardWidgetsMode.Hidden;
                view.SetDashboard(true); host.ProcessPendingWork();
                Assert.True(active.Parent.Width > narrowWidth);
                Assert.InRange(scroll.ClientSize.Width - active.Parent.Width, 36, 54);
                Assert.False(inspector.Visible); Assert.Equal(System.Drawing.Rectangle.Empty, inspector.Bounds);
                Assert.False(widgets.Visible); Assert.Equal(System.Drawing.Rectangle.Empty, widgets.Bounds);

                // Reopen both sections before returning to Classic, so the test catches stale
                // large rectangles even when the hidden preference was not chosen by the user.
                AppearancePresentation.Current.ModernDashboardShell.Inspector = DashboardInspectorMode.Always;
                AppearancePresentation.Current.ModernDashboardShell.Widgets = DashboardWidgetsMode.All;
                view.SetDashboard(true); host.ProcessPendingWork();
                Assert.True(inspector.Visible); Assert.True(widgets.Visible);
                var modernChrome = scroll.Controls.Except(classicChildren).ToArray(); Assert.NotEmpty(modernChrome);

                DashboardVisual.RestoreTree(view); AppTypography.SetDashboardPresentation(view, false);
                view.SetDashboard(false); host.ProcessPendingWork();
                Assert.All(modernChrome, control =>
                {
                    Assert.False(control.Visible);
                    Assert.Equal(System.Drawing.Rectangle.Empty, control.Bounds);
                });
                Assert.Equal(classicPadding, url.Padding);
                Assert.Equal(classicBounds, classicChildren.Select(control => (control.GetType().Name, control.Bounds)).ToArray());
                Assert.Equal(classicScroll, scroll.AccessibilityObject.ScrollInfo);
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ClassicLiveCardRoundtripRestoresPresentationAndKeepsItsCanonicalEditAction(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-presentation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                AppTypography.SetScale(scale);
                var session = new LiveSession
                {
                    Title = "Zaplanowana transmisja — Łódź 日本語", Channel = "Presentation QA",
                    SourceUrl = "https://example.test/live/upcoming", State = LiveSessionState.WaitingForLive,
                    ScheduledStartAt = new DateTimeOffset(2030, 1, 2, 14, 0, 0, TimeSpan.Zero),
                    RecordedDuration = TimeSpan.FromSeconds(15), BytesWritten = 1048576,
                    QualityPresetId = "best", Container = PreferredVideoContainer.Mkv
                };
                var edited = new List<LiveSession>();
                var row = new LiveSessionCard(services, edited.Add) { Width = 1400 };
                row.UpdateSession(session);
                host.Show(row, 1440, 720);
                var labels = row.Controls.OfType<Label>().ToArray();
                var original = labels.Select(label => (Label: label, Font: label.Font, Text: label.Text, Ellipsis: label.AutoEllipsis,
                    Styles: Styles(label).Select(style => (Font: style.GetFont(), Size: style.GetFontSize(), Color: style.GetForegroundColor())).ToArray())).ToArray();
                var primary = row.Controls.OfType<Button>().Single(button => button.AccessibleAutomationId == "LiveEditButton-" + session.Id.ToString("N"));
                var originalPrimary = Styles(primary).Select(style => (Background: style.GetBackgroundColor(), Foreground: style.GetForegroundColor(), Font: style.GetFont(), Size: style.GetFontSize())).ToArray();
                var originalPadding = primary.Padding;
                var provider = primary.AccessibilityObject;
                Assert.Equal(AccessibleControlType.Button, provider.ControlType);

                for (var cycle = 0; cycle < 2; cycle++)
                {
                    row.SetDashboard(true); row.SetSelected(true); row.UpdateSession(session); host.ProcessPendingWork();
                    var modernLabels = row.Controls.OfType<Label>().Except(labels).ToArray();
                    Assert.NotEmpty(modernLabels); Assert.All(modernLabels, label => Assert.True(label.Visible));
                    Assert.Contains(row.Controls.OfType<AppVectorIcon>(), icon => icon.Visible);
                    Assert.Contains(primary.Controls.OfType<AppVectorIcon>(), icon => icon.Visible);
                    Assert.True(provider.PerformAction(AccessibleActions.Invoke));
                    Assert.Same(session, edited[^1]);

                    row.SetDashboard(false); row.UpdateSession(session); host.ProcessPendingWork();
                    foreach (var saved in original)
                    {
                        Assert.Equal(saved.Font, saved.Label.Font);
                        Assert.Equal(saved.Text, saved.Label.Text);
                        Assert.Equal(saved.Ellipsis, saved.Label.AutoEllipsis);
                        Assert.Equal(saved.Styles, Styles(saved.Label).Select(style => (style.GetFont(), style.GetFontSize(), style.GetForegroundColor())).ToArray());
                    }
                    Assert.Equal(originalPrimary, Styles(primary).Select(style => (style.GetBackgroundColor(), style.GetForegroundColor(), style.GetFont(), style.GetFontSize())).ToArray());
                    Assert.Equal(originalPadding, primary.Padding);
                    Assert.All(modernLabels, label => Assert.False(label.Visible));
                    Assert.All(row.Controls.OfType<AppVectorIcon>(), icon => Assert.False(icon.Visible));
                    Assert.All(primary.Controls.OfType<AppVectorIcon>(), icon => Assert.False(icon.Visible));
                    Assert.Same(provider, primary.AccessibilityObject);
                    Assert.Equal(services.Localization["Live.Edit"], primary.Text);
                    Assert.True(provider.PerformAction(AccessibleActions.Invoke));
                    Assert.Same(session, edited[^1]);
                }
                Assert.Equal(4, edited.Count);
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static ControlStyle[] Styles(Control control) => [control.Style, control.StyleHover, control.StyleFocused, control.StylePressed, control.StyleDisabled];

    private static T Find<T>(Control root, string id) where T : Control
    {
        foreach (var child in root.Controls)
        {
            if (child is T match && child.AccessibleAutomationId == id) return match;
            try { return Find<T>(child, id); } catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }

    private static async Task<AppServices> CreateServicesAsync(string directory)
    {
        var paths = AppPaths.Create(directory); paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false; settings.Current.Startup.ShowTrayIcon = false;
        settings.Current.Updates.AutomaticChecks = false; settings.Current.FinalOutputDirectory = Path.Combine(directory, "output"); settings.Current.TemporaryDirectory = Path.Combine(directory, "temp");
        settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
        settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var tools = Path.Combine(directory, "fake"); Directory.CreateDirectory(tools);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*")) File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
        settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
        await settings.SaveAsync(); var services = AppServices.Create(paths); await services.Tools.Initialization; return services;
    }
}
