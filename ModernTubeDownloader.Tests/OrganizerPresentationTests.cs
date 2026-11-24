using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Fact]
    public async Task OrganizerShowsSourceBeforeLongListNameAndKeepsInspectorOnVisibleResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-organizer-diagnostics-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(Path.Combine(root, "profile"));
            var source = Path.Combine(root, "recordings"); Directory.CreateDirectory(source);
            const string matchedUrl = "https://example.test/watch/matched";
            foreach (var name in new[] { "matched", "outside" })
            {
                await File.WriteAllBytesAsync(Path.Combine(source, name + ".mp4"), [1, 2, 3]);
                await File.WriteAllTextAsync(Path.Combine(source, name + ".json"),
                    System.Text.Json.JsonSerializer.Serialize(new { id = name, webpage_url = "https://example.test/watch/" + name }));
            }
            await File.WriteAllBytesAsync(Path.Combine(source, "unidentified.mp4"), [4, 5, 6]);
            var session = new BatchImportSession { FileName = new string('L', 180) + ".txt", OutputDirectory = Path.Combine(root, "organized") };
            for (var i = 0; i < 40; i++) session.Entries.Add(new() { Url = "https://example.test/missing/" + i });
            session.Entries.Add(new() { Url = matchedUrl });
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(100);
                var form = new OrganizerForm(services, session); host.Show(form, 1220, 900); host.ProcessPendingWork();
                var surface = Assert.Single(form.Controls);
                void Drain()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (Find<Button>(surface, "OrganizerCancel").Enabled && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
                    host.ProcessPendingWork(); Assert.False(Find<Button>(surface, "OrganizerCancel").Enabled);
                }
                Drain(); Find<TextBox>(surface, "OrganizerSource").Text = source;
                host.Dispatcher.Post(() => Find<Button>(surface, "OrganizerPlan").PerformClick()); host.ProcessPendingWork(); Drain();
                var rows = Find<ExperienceScrollPanel>(surface, "OrganizerRows");
                Assert.Equal(40, rows.Controls.Count);
                var firstTitle = rows.Controls[0].Controls.OfType<Label>().First();
                Assert.Equal("https://example.test/missing/0", firstTitle.Text);
                Assert.Contains(session.FileName, firstTitle.AccessibleName);
                var scan = Find<Label>(surface, "OrganizerScanSummary");
                Assert.Equal(services.Localization.Get("Organizer.ScanSummary", 3, 2, 1, 1), scan.Text);
                Assert.True(scan.Visible); Assert.True(scan.Height > 0);
                var details = Find<TextBox>(surface, "OrganizerDetails");
                Assert.StartsWith(services.Localization["Organizer.Status.Missing"] + " · ", details.Text);
                Assert.Contains(firstTitle.Text, details.Text); Assert.DoesNotContain(matchedUrl, details.Text);
                Assert.True(details.Text.IndexOf(firstTitle.Text, StringComparison.Ordinal) < details.Text.IndexOf(session.FileName, StringComparison.Ordinal));
                Assert.Equal(0, details.SelectionStart); Assert.Equal(0, details.SelectionEnd);

                // Inspect a visible row, then refresh selection without losing it.
                host.Dispatcher.Post(() => rows.Controls[1].Controls.OfType<Button>().Single().PerformClick()); host.ProcessPendingWork();
                Assert.Contains("https://example.test/missing/1", details.Text);
                host.Dispatcher.Post(() => Find<Button>(surface, "OrganizerSelectPage").PerformClick()); host.ProcessPendingWork();
                Assert.Contains("https://example.test/missing/1", details.Text);

                host.Dispatcher.Post(() => Find<Button>(surface, "OrganizerNext").PerformClick()); host.ProcessPendingWork();
                Assert.Single(rows.Controls); Assert.Equal("matched.mp4", rows.Controls[0].Controls.OfType<Label>().First().Text);
                Assert.StartsWith(services.Localization["Organizer.Status.Found"] + " · ", details.Text);
                Assert.Contains(matchedUrl, details.Text);
                var filter = Find<ComboBox>(surface, "OrganizerFilter");
                host.Dispatcher.Post(() => filter.SelectedIndex = (int)ModernTubeDownloader.Organizer.OrganizerMatchStatus.Missing + 1); host.ProcessPendingWork();
                Assert.Equal(40, rows.Controls.Count); Assert.DoesNotContain(matchedUrl, details.Text);
                Assert.StartsWith(services.Localization["Organizer.Status.Missing"] + " · ", details.Text);
                host.Dispatcher.Post(() => filter.SelectedIndex = (int)ModernTubeDownloader.Organizer.OrganizerMatchStatus.Ambiguous + 1); host.ProcessPendingWork();
                Assert.Empty(rows.Controls); Assert.Equal("", details.Text); Assert.Equal(-1, Find<ComboBox>(surface, "OrganizerCandidates").SelectedIndex);
                Assert.False(Find<Button>(surface, "OrganizerMove").Enabled);
                Assert.False(Directory.Exists(session.OutputDirectory)); Assert.Empty(services.Queue.Snapshot());
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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

    [Fact]
    public async Task OrganizerKeepsListEditableWhenSuggestionFailsAndShowsFieldSpecificErrors()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-organizer-path-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(Path.Combine(root, "profile"));
            var media = Directory.CreateDirectory(Path.Combine(root, "Pobrane Cafe\u0301")).FullName;
            services.Settings.Current.FinalOutputDirectory = media;
            await File.WriteAllTextAsync(Path.Combine(media, "Playlist"), "keep");
            var session = new BatchImportSession { FileName = "Playlist.txt" };
            session.Entries.Add(new() { Url = "https://example.test/missing" });

            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                services.Localization.SetLanguage("pl");
                using var form = new OrganizerForm(services, session);
                host.Show(form, 1220, 900);
                var surface = Assert.Single(form.Controls);
                var status = Find<Label>(surface, "OrganizerStatus");
                var source = Find<TextBox>(surface, "OrganizerSource");
                var destination = Find<TextBox>(surface, "OrganizerDestination");

                void AwaitRun()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    do { host.ProcessPendingWork(); Thread.Sleep(5); }
                    while (Find<Button>(surface, "OrganizerCancel").Enabled && DateTime.UtcNow < deadline);
                    Assert.False(Find<Button>(surface, "OrganizerCancel").Enabled);
                }

                void Analyze()
                {
                    host.Dispatcher.Post(() => Find<Button>(surface, "OrganizerPlan").PerformClick());
                    AwaitRun();
                }

                AwaitRun();
                Assert.Contains(services.Localization["Organizer.Error.ListFolder"], status.Text);
                Assert.Contains(services.Localization["Organizer.Error.NotDirectory"], status.Text);
                Assert.Equal(media, source.Text);
                Assert.Equal("", destination.Text);

                destination.Text = Path.Combine(root, "Wynik");
                source.Text = Path.Combine(root, "brak");
                Analyze();
                Assert.StartsWith(services.Localization["Organizer.Source"], status.Text);
                Assert.Contains(services.Localization["Organizer.Error.FolderMissing"], status.Text);

                source.Text = media;
                destination.Text = "relative";
                Analyze();
                Assert.StartsWith(services.Localization["Organizer.Destination"], status.Text);
                Assert.Contains(session.FileName, status.Text);
                Assert.Contains(services.Localization["Organizer.Error.PathType"], status.Text);

                var final = Path.Combine(root, "Wynik (Zażółć)");
                destination.Text = final.Replace('\\', '/') + "/";
                Analyze();
                Assert.Single(Find<ExperienceScrollPanel>(surface, "OrganizerRows").Controls);
                Assert.Contains(final, Find<TextBox>(surface, "OrganizerDetails").Text);
                Assert.False(Directory.Exists(final));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(media, "Playlist")));
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100);
            AppearancePresentation.Current = new();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
