using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DownloadsDashboardPresentationTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public async Task HiddenClassicPanelsDoNotKeepWideScrollCanvasAfterModernResize(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-downloads-canvas-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new DownloadsView(services); host.Show(view, 2880, 1200); host.ProcessPendingWork();
                var sharedControls = view.Controls.ToArray();
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true);
                host.Resize(1469, 900); host.ProcessPendingWork();
                Assert.Equal(1469, view.Width);
                var scroll = Assert.IsType<AccessibleScrollInfo>(view.AccessibilityObject.ScrollInfo);
                var geometry = string.Join("; ", view.Controls.Select(control => $"{control.GetType().Name}/{control.AccessibleAutomationId}: visible={control.Visible} bounds={control.Bounds}"));
                Assert.False(scroll.Horizontal.IsScrollable, geometry);
                Assert.All(sharedControls.Where(control => !control.Visible), control => Assert.Equal(System.Drawing.Rectangle.Empty, control.Bounds));
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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
