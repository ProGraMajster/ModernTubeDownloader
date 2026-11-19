using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class LiveDashboardPresentationTests
{
    [Theory]
    [InlineData(100, 900, 720)]
    [InlineData(150, 900, 720)]
    [InlineData(200, 600, 500)]
    public async Task LiveDetailsKeepFullPathsReadableAndActionsInsideTheViewport(int scale, int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-details-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = services.Live.Add("https://youtube.com/watch?v=synthetic-details", null, "1080", LiveStartPolicy.FromNow, true);
            var parts = Enumerable.Range(1, 4).Select(i => Path.Combine(directory, "Nagrania Łódź 日本語", new string('x', 120), "part-" + i + ".mkv")).ToList();
            services.Live.Update(session.Id, value =>
            {
                value.Title = "Długie nagranie transmisji — Łódź 日本語 " + new string('x', 160);
                value.Channel = "QA"; value.State = LiveSessionState.Partial; value.Parts = parts;
                value.RecordedDuration = TimeSpan.FromHours(12); value.BytesWritten = 987654321;
                value.ResumeCount = 4; value.RecoveryWarningKey = "Live.State.Partial";
            }, persist: false);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); services.Localization.SetLanguage("pl"); AppTypography.SetScale(scale);
                var form = new LiveSessionDetailsForm(services, services.Live.Find(session.Id)!);
                host.Show(form, width, height); host.ProcessPendingWork();
                var surface = Assert.Single(form.Controls);
                var body = Find<AppScrollPanel>(surface, "LiveDetailsContent");
                var output = Find<TextBox>(surface, "LiveDetailsParts");
                var source = Find<TextBox>(surface, "LiveDetailsSource");
                Assert.True(output.ReadOnly); Assert.True(source.ReadOnly);
                Assert.Equal(string.Join(Environment.NewLine, parts), output.Text); Assert.Equal(session.SourceUrl, source.Text);
                Assert.Equal(4, body.Controls.Count); // Header, parameters, recovery, saved files.
                Assert.False(Assert.IsType<AccessibleScrollInfo>(body.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                var footer = Find<Button>(surface, "LiveDetailsClose").Parent!;
                Assert.Equal(footer.Top, body.Bottom);
                var actions = footer.Controls.Where(control => control.Visible).ToArray();
                Assert.All(actions, action =>
                {
                    Assert.True(action.Left >= 0 && action.Top >= 0);
                    Assert.True(action.Right <= footer.Width && action.Bottom <= footer.Height);
                });
                for (var i = 0; i < actions.Length; i++)
                    for (var j = i + 1; j < actions.Length; j++) Assert.False(actions[i].Bounds.IntersectsWith(actions[j].Bounds));
                Assert.Equal(parts, services.Live.Find(session.Id)!.Parts);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LiveDetailsNeverResumeARecordingThatFinishedAfterTheButtonWasPainted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-details-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = services.Live.Add("https://example.test/live/details", null, "720", LiveStartPolicy.FromNow, true);
            services.Live.Update(session.Id, value => value.State = LiveSessionState.Partial, persist: false);
            using (var host = ModernFormsTestHost.Create())
            {
                var form = new LiveSessionDetailsForm(services, services.Live.Find(session.Id)!);
                host.Show(form, 900, 720); host.ProcessPendingWork();
                var surface = Assert.Single(form.Controls);
                var action = Find<Button>(surface, "LiveDetailsAction"); Assert.True(action.Visible); Assert.True(action.Enabled);
                services.Live.Update(session.Id, value => value.State = LiveSessionState.Completed, persist: false);
                action.PerformClick();
                Assert.Equal(LiveSessionState.Completed, services.Live.Find(session.Id)!.State);
                host.ProcessPendingWork(); Assert.False(action.Visible);
                Assert.False(Find<Button>(surface, "LiveDetailsOpenFile").Enabled);
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
