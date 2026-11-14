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
    [InlineData(100, "pl")] [InlineData(150, "pl")] [InlineData(200, "pl")]
    [InlineData(100, "en")] [InlineData(150, "en")] [InlineData(200, "en")]
    public async Task DownloadPathsAndNestedCookieEditorFitTheActualScrollViewport(int scale, string language)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-settings-viewport-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false;
            settings.Current.Startup.ShowTrayIcon = false; settings.Current.Updates.AutomaticChecks = false;
            settings.Current.Language = language;
            settings.Current.FinalOutputDirectory = Path.Combine(directory, "output-with-a-long-fully-copyable-directory-name");
            settings.Current.TemporaryDirectory = Path.Combine(directory, "temporary-with-a-long-fully-copyable-directory-name");
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
                host.Show(view, 1320, 900); view.Dock = DockStyle.None; host.ProcessPendingWork();
                var scroll = Find<AppScrollPanel>(view, "SettingsScroll");
                var cookie = Find<CookieSyncSettingsPanel>(view, "CookieSyncSection");
                var output = Find<TextBox>(view, "SettingsOutputDirectory");
                var originalParent = output.Parent;
                foreach (var width in new[] { 1320, 760, 1320 })
                {
                    view.Width = width;
                    foreach (var category in new[] { "Downloads", "Authentication", "General", "Downloads" })
                    {
                        Find<Button>(view, "SettingsCategory" + category).PerformClick(); host.ProcessPendingWork();
                        Assert.Equal(scroll.ClientSize.Height, scroll.DisplayRectangle.Height); // no horizontal bar
                        foreach (var card in scroll.Controls.Where(control => control.Visible))
                            Assert.True(card.Right <= scroll.DisplayRectangle.Right,
                                $"{category}/{scale}%/{width}px: {card.Bounds} exceeds viewport {scroll.DisplayRectangle}.");
                        if (category == "Authentication")
                        {
                            foreach (var child in cookie.Controls.Where(control => control.Visible))
                            {
                                Assert.True(child.Left >= 0 && child.Top >= 0 && child.Width > 0 && child.Height > 0);
                                Assert.True(child.Right <= cookie.Width && child.Bottom <= cookie.Height,
                                    $"{child.AccessibleAutomationId} exceeds nested editor at {scale}%/{width}px: {child.Bounds} in {cookie.Size}.");
                            }
                            Find<Button>(cookie, "CookieSyncRepair").PerformClick(); host.ProcessPendingWork();
                            var feedback = Find<Label>(cookie, "CookieSyncFeedback");
                            Assert.Equal(services.Localization["CookieSync.ConsentRequired"], feedback.Text);
                            Assert.True(feedback.Visible && feedback.Bottom <= cookie.Height,
                                $"Feedback {feedback.Bounds}/visible={feedback.Visible} exceeds {cookie.Size} at {scale}%/{width}px after validation.");
                            Assert.False(Find<CheckBox>(cookie, "CookieBridgeConsent").Checked);
                        }
                        if (category == "Downloads")
                        {
                            Assert.Equal(settings.Current.FinalOutputDirectory, output.Text);
                            foreach (var id in new[] { "SettingsBrowseOutput", "SettingsBrowseTemporary" })
                            {
                                var browse = Find<Button>(view, id);
                                Assert.True(browse.Width >= DashboardVisual.TextWidth(browse, browse.Text) + browse.Padding.Horizontal,
                                    $"{id} clips its command at {scale}%/{width}px.");
                            }
                        }
                        if (scroll.VerticalScrollProperties.Maximum > 0)
                        {
                            scroll.VerticalScrollProperties.Value = Math.Min(240, scroll.VerticalScrollProperties.Maximum);
                            host.ProcessPendingWork(); Assert.Equal(0, scroll.HorizontalScrollProperties.Value);
                        }
                    }
                }
                view.SetDashboard(false); AppTypography.SetDashboardPresentation(view, false);
                Find<Button>(view, "SettingsCategoryAuthentication").PerformClick(); host.ProcessPendingWork();
                Assert.False(cookie.ResponsiveLayout);
                Assert.Equal(ModernTubeDownloader.Authentication.AuthenticationSource.None, cookie.Source);
                Assert.Equal(settings.Current.FinalOutputDirectory, output.Text);
                Assert.Same(originalParent, output.Parent); Assert.Empty(services.Queue.Snapshot()); Assert.True(services.Settings.Current.QueuePaused);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
