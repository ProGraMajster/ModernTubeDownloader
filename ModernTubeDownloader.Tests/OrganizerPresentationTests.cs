using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Theory]
    [InlineData(100, 1220, 900)]
    [InlineData(150, 960, 900)]
    [InlineData(200, 960, 1000)]
    public async Task OrganizerTenThousandEntriesKeepFortyRowsAndBoundedActions(int scale, int width, int height)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-organizer-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(Path.Combine(root, "profile"));
            var source = Path.Combine(root, "recordings"); Directory.CreateDirectory(source);
            var session = new BatchImportSession { FileName = "Synthetic 10k URLs", OutputDirectory = Path.Combine(root, "organized") };
            for (var i = 0; i < 10000; i++) session.Entries.Add(new() { Url = "https://example.test/" + i });
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var form = new OrganizerForm(services, session); host.Show(form, width, height); host.ProcessPendingWork();
                var surface = Assert.Single(form.Controls);
                void Drain()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (Find<Button>(surface, "OrganizerCancel").Enabled && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
                    host.ProcessPendingWork(); Assert.False(Find<Button>(surface, "OrganizerCancel").Enabled);
                }
                Drain(); Find<TextBox>(surface, "OrganizerSource").Text = source;
                // Async handlers must enter through the test host dispatcher, which owns
                // the same UI synchronization context as a native input event.
                host.Dispatcher.Post(() => Find<Button>(surface, "OrganizerPlan").PerformClick()); host.ProcessPendingWork(); Drain();
                var rows = Find<ExperienceScrollPanel>(surface, "OrganizerRows"); Assert.Equal(40, rows.Controls.Count);
                Assert.False(Find<Button>(surface, "OrganizerMove").Enabled); Assert.Empty(services.Queue.Snapshot());
                host.Dispatcher.Post(() => Find<Button>(surface, "OrganizerNext").PerformClick()); host.ProcessPendingWork(); Assert.Equal(40, rows.Controls.Count);
                foreach (var row in rows.Controls)
                    Assert.All(row.Controls, child => Assert.True(child.Left >= 0 && child.Top >= 0 && child.Right <= row.Width && child.Bottom <= row.Height));
                var footer = Find<Button>(surface, "OrganizerClose").Parent!;
                Assert.All(footer.Controls, child => Assert.True(child.Left >= 0 && child.Top >= 0 && child.Right <= footer.Width && child.Bottom <= footer.Height));
                Assert.Equal(10000, session.Snapshot().Count); Assert.False(Directory.Exists(session.OutputDirectory));
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
