using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class SettingsDashboardPresentationTests
{
    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public async Task SearchFitsAndTransientShellWidthRecoversWithoutInvalidCommandGeometry(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-settings-layout-" + Guid.NewGuid().ToString("N"));
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
                host.Show(view, 1853, 900); view.Dock = DockStyle.None; host.ProcessPendingWork();
                var search = Find<TextBox>(view, "SettingsCategorySearch");
                Assert.True(DashboardVisual.TextWidth(search, search.Placeholder) + 20 <= search.Width);
                Find<Button>(view, "SettingsCategoryPersonalization").PerformClick();
                foreach (var width in new[] { 0, 40, 1853, 1600, 1853 }) { view.Width = width; host.ProcessPendingWork(); }
                var first = Find<Button>(view, "AppearancePresetDefault");
                var commands = first.Parent!;
                Assert.Equal(9, commands.Controls.Count);
                var personalization = Assert.IsType<PersonalizationSettingsPanel>(commands.Parent);
                var allocated = commands.Controls.Select(button => button.Bounds).ToArray();
                foreach (var unallocated in new[] { 0, 40, 48 })
                {
                    personalization.LayoutForWidth(unallocated);
                    Assert.Equal(allocated, commands.Controls.Select(button => button.Bounds).ToArray());
                }
                Assert.All(commands.Controls, button =>
                {
                    Assert.True(button.Width > 0 && button.Height > 0);
                    Assert.True(button.Left >= 0 && button.Right <= commands.Width && button.Bottom <= commands.Height);
                });
                view.SetDashboard(false); AppTypography.SetDashboardPresentation(view, false); host.ProcessPendingWork();
                Assert.False(search.Visible);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static T Find<T>(Control parent, string id) where T : Control
    {
        foreach (var child in parent.Controls)
        {
            if (child.AccessibleAutomationId == id && child is T result) return result;
            try { return Find<T>(child, id); } catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }
}
