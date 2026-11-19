using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class UpdateSettingsPresentationTests
{
    [Theory]
    [InlineData("pl", 100, 1050)] [InlineData("en", 150, 800)] [InlineData("pl", 200, 640)]
    public async Task UpdatePolicyAndAllActionsRemainInsideScrollableCard(string language, int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-update-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.Language = language; settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false;
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
                var panel = new UpdateSettingsPanel(services); AppTypography.SetDashboardPresentation(panel, true);
                host.Show(panel, width, 900); panel.Dock = DockStyle.None; panel.Width = width; panel.LayoutForWidth(width); host.ProcessPendingWork();
                Assert.All(panel.Controls.Where(c => c.Visible), c =>
                {
                    Assert.True(c.Width > 0 && c.Height > 0);
                    Assert.True(c.Left >= 0 && c.Right <= panel.Width, $"{c.AccessibleAutomationId} overflows at {scale}%.");
                    Assert.True(c.Top >= 0 && c.Bottom <= panel.Height);
                });
                var actions = panel.Controls.OfType<Button>().ToArray(); Assert.Equal(7, actions.Length);
                foreach (var a in actions) foreach (var b in actions.Where(b => b != a)) Assert.False(a.Bounds.IntersectsWith(b.Bounds));
                var automatic = panel.Controls.OfType<CheckBox>().Single(c => c.AccessibleAutomationId == "ApplicationUpdateAutomatic");
                Assert.False(automatic.Checked);
                Assert.True(panel.Controls.Single(c => c.AccessibleAutomationId == "ApplicationUpdatePolicyHelp").Visible);
                AppTypography.SetDashboardPresentation(panel, false); panel.LayoutForWidth(1050);
                Assert.Equal(7, panel.Controls.OfType<Button>().Count());
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
