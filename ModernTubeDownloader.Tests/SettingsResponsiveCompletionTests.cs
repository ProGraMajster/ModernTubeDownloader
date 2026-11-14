using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class SettingsDashboardPresentationTests
{
    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public async Task CategoryCardsReflowWithinNarrowViewportAndClearHiddenBounds(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-settings-responsive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false;
            settings.Current.Startup.ShowTrayIcon = false; settings.Current.Updates.AutomaticChecks = false;
            settings.Current.FinalOutputDirectory = Path.Combine(directory, "output"); settings.Current.TemporaryDirectory = Path.Combine(directory, "temp");
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var tools = Path.Combine(directory, "fake"); Directory.CreateDirectory(tools);
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*")) File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
            settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync(); using var services = AppServices.Create(paths); await services.Tools.Initialization;
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new SettingsView(services); AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true);
                host.Show(view, 1320, 1024); view.Dock = DockStyle.None; host.ProcessPendingWork();
                var scroll = Find<AppScrollPanel>(view, "SettingsScroll");
                var quality = Find<ComboBox>(view, "DefaultContainerSelector"); var originalParent = quality.Parent;
                foreach (var width in new[] { 1320, 760, 1320 })
                {
                    view.Width = width;
                    foreach (var category in new[] { "General", "Downloads", "Live", "Advanced", "Browser", "WebRemote", "Startup", "General" })
                    {
                        Find<Button>(view, "SettingsCategory" + category).PerformClick(); host.ProcessPendingWork();
                        Assert.True(scroll.Bottom <= view.Height && scroll.Height >= 120);
                        foreach (var card in scroll.Controls)
                        {
                            if (!card.Visible)
                            {
                                Assert.True(card.Bounds == System.Drawing.Rectangle.Empty,
                                    $"Hidden {card.GetType().Name} / {card.AccessibleAutomationId} retained {card.Bounds} in {category} at {scale}%/{width}px.");
                                continue;
                            }
                            Assert.True(card.Left >= 0 && card.Right <= scroll.ClientSize.Width, $"{category} at {scale}%/{width}px escapes its viewport.");
                            foreach (var child in card.Controls.Where(control => control.Visible))
                            {
                                Assert.True(child.Width > 0 && child.Height > 0, $"{category}: {child.Text} has no bounds.");
                                Assert.True(child.Left >= 0 && child.Top >= 0 && child.Right <= card.Width && child.Bottom <= card.Height,
                                    $"{category}: {child.AccessibleAutomationId} {child.Text} escapes its card at {scale}%/{width}px.");
                            }
                        }
                    }
                }
                view.SetDashboard(false); AppTypography.SetDashboardPresentation(view, false);
                Find<Button>(view, "SettingsCategoryDownloads").PerformClick(); host.ProcessPendingWork();
                Assert.Same(originalParent, quality.Parent); Assert.True(quality.Visible && quality.Width > 0);
                Assert.False(Find<TextBox>(view, "SettingsCategorySearch").Visible);
                Assert.Empty(services.Queue.Snapshot()); Assert.True(services.Settings.Current.QueuePaused);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
