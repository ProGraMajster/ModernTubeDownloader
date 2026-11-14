using System.Reflection;
using ModernFormsNext;
using ModernFormsNext.Drawing;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Theory]
    [InlineData("History", 1)]
    [InlineData("Batch", 6)]
    [InlineData("Other", 6)]
    public async Task DashboardSurfacesRecoverAfterCanonicalClassicRoundTrip(string screen, int expectedSurfaces)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-dashboard-surface-cycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                UserControl view;
                Action<bool> present;
                if (screen == "History")
                {
                    var history = new HistoryView(services); view = history; present = history.SetDashboard;
                }
                else if (screen == "Batch")
                {
                    var batch = new BatchImportView(services); view = batch; present = batch.SetDashboard;
                }
                else
                {
                    var other = new OtherView(services);
                    other.RegisterTool("Batch", () => { }); other.RegisterTool("Thumbnails", () => { });
                    view = other; present = other.SetDashboard;
                }
                AppTypography.SetDashboardPresentation(view, true); present(true); host.Show(view, 1320, 1024);
                host.ProcessPendingWork();

                Control[] surfaces = screen switch
                {
                    "History" => [PrivateSurface(view, "tableSurface")],
                    "Batch" => [Find<Panel>(view, "BatchTableHeader"), PrivateSurface(view, "resultStatusFrame"),
                        .. Descendants(view).OfType<DashboardMetric>()],
                    _ => Descendants(view).Where(control => control.GetType().Name == "OtherToolIcon" ||
                        control is Panel panel && panel.Controls.OfType<Label>().Any(label => Equals(label.Tag, "Batch.Browse"))).ToArray()
                };
                Assert.Equal(expectedSurfaces, surfaces.Length);
                var colors = surfaces.ToDictionary(control => control,
                    control => Assert.IsType<LinearGradientBrush>(control.BackgroundBrush).GradientStops.Select(stop => stop.Color).ToArray());
                foreach (var palette in colors.Values) Assert.Equal(2, palette.Length);

                for (var cycle = 0; cycle < 2; cycle++)
                {
                    // Mirror MainForm's actual shell transition, including the recursive
                    // reset that does not happen when only SetDashboard(true) is tested.
                    DashboardVisual.RestoreTree(view);
                    foreach (var surface in surfaces) Assert.False(surface.BackgroundBrush is LinearGradientBrush);
                    AppTypography.SetDashboardPresentation(view, false); present(false); host.ProcessPendingWork();
                    AppTypography.SetDashboardPresentation(view, true); present(true); host.ProcessPendingWork();
                    foreach (var surface in surfaces)
                    {
                        Assert.Contains(surface, Descendants(view));
                        Assert.True(surface.Visible && surface.Width > 0 && surface.Height > 0);
                        var restored = Assert.IsType<LinearGradientBrush>(surface.BackgroundBrush);
                        Assert.Equal(colors[surface], restored.GradientStops.Select(stop => stop.Color).ToArray());
                    }
                }
                Assert.Empty(services.Queue.Snapshot());
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static Control PrivateSurface(object view, string name)
        => Assert.IsAssignableFrom<Control>(view.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
}
