using System.Net;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

public sealed partial class ExperiencePresentationTests
{
    [Theory]
    [InlineData("en", 100, AppThemeMode.Dark, false)]
    [InlineData("pl", 150, AppThemeMode.Light, false)]
    [InlineData("ja-JP", 200, AppThemeMode.Dark, true)]
    public async Task VersionedNotesRemainOfflineAndExplicitlyExplainContentFallback(string language, int scale, AppThemeMode theme, bool fallback)
    {
        using var fixture = new Fixture(); using var handler = new UnavailableGitHub(); using var client = new HttpClient(handler);
        using var services = await fixture.Services(theme, client);
        var beforeRequests = handler.Requests;
        var queue = services.Paths.QueueFile; var history = services.Paths.HistoryFile;
        await File.WriteAllTextAsync(queue, "[]"); await File.WriteAllTextAsync(history, "[]");
        var beforeQueue = await File.ReadAllBytesAsync(queue); var beforeHistory = await File.ReadAllBytesAsync(history);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize(); services.Localization.SetLanguage(language); AppTypography.SetScale(scale);
            using var news = new WhatsNewForm(services); var window = host.Show(news, 720, 560); host.ProcessPendingWork();
            var root = Root(news); AssertGeometry(root, "Offline versioned notes " + language);
            Assert.Equal(4, Descendants(root).OfType<Panel>().Count(panel => panel.AccessibleAutomationId?.StartsWith("WhatsNewSection-", StringComparison.Ordinal) == true));
            Assert.Equal(fallback, Find<Label>(root, "WhatsNewLanguageFallback").Text.Length > 0);
            Assert.Equal(services.Localization["Experience.LocalNotes"], Find<Label>(root, "WhatsNewProvenance").Text);
            Assert.Contains(services.Experience.Version, Find<Button>(root, "WhatsNewReleases").Text);
            Assert.Equal(beforeRequests, handler.Requests); // Even an HTTP 404 endpoint is irrelevant: opening notes never calls it.
            Click(host, root, "WhatsNewLater"); Pump(host, () => window.IsClosed);
            Assert.Null(services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        Assert.Equal(beforeQueue, await File.ReadAllBytesAsync(queue)); Assert.Equal(beforeHistory, await File.ReadAllBytesAsync(history));
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData("pl", 100, AppThemeMode.Dark)]
    [InlineData("en", 150, AppThemeMode.Light)]
    [InlineData("ja-JP", 200, AppThemeMode.Dark)]
    public async Task ModernHelpKeepsRealActionsResponsiveAndRestoresClassic(string language, int scale, AppThemeMode theme)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(theme);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize(); services.Localization.SetLanguage(language); AppTypography.SetScale(scale);
            using var help = new InformationView(services, false); AppTypography.SetDashboardPresentation(help, true); help.SetDashboard(true);
            var window = host.Show(help, 1100, 820); host.ProcessPendingWork();
            foreach (var size in new[] { (1100, 820), (720, 560) })
            {
                window.Resize(size.Item1, size.Item2); host.ProcessPendingWork(); AssertGeometry(help, "Help " + language + " " + size);
            }
            Assert.Equal(6, Descendants(help).OfType<Panel>().Count(panel => panel.AccessibleAutomationId?.StartsWith("HelpTopic-", StringComparison.Ordinal) == true));
            Assert.Equal(language == "ja-JP", Find<Label>(help, "HelpLanguageFallback").Text.Length > 0);
            var routes = new List<AppPageId>(); var settings = new List<string>(); var setupCount = 0; var newsCount = 0; var batchCount = 0; var organizerCount = 0;
            help.NavigateRequested += (_, page) => routes.Add(page); help.SettingsRequested += (_, category) => settings.Add(category);
            help.SetupRequested += (_, _) => setupCount++; help.WhatsNewRequested += (_, _) => newsCount++;
            help.BatchRequested += (_, _) => batchCount++; help.OrganizerRequested += (_, _) => organizerCount++;
            foreach (var id in new[] { "HelpSetup", "HelpWhatsNew", "HelpDownloads", "HelpHistory", "HelpLive", "HelpBatch", "HelpOrganizer", "HelpSettings", "HelpUpdates" }) Click(host, help, id);
            Assert.Equal(new[] { AppPageId.Downloads, AppPageId.History, AppPageId.Live, AppPageId.Settings }, routes);
            Assert.Equal(new[] { "Updates" }, settings); Assert.Equal(1, setupCount); Assert.Equal(1, newsCount); Assert.Equal(1, batchCount); Assert.Equal(1, organizerCount);
            Assert.Empty(services.Queue.Snapshot()); Assert.Empty(services.History.Snapshot());
            Ui(host, () => help.SetDashboard(false)); Assert.False(Find<ExperienceScrollPanel>(help, "HelpTopics").Visible);
            Assert.True(Find<Button>(help, "InformationSetup").Visible); Assert.True(Find<Button>(help, "InformationWhatsNew").Visible);
            Ui(host, () => help.SetDashboard(true)); Assert.True(Find<ExperienceScrollPanel>(help, "HelpTopics").Visible);
            AssertGeometry(help, "Help restored " + language);
        }
        await services.ShutdownAsync();
    }

    private sealed class UnavailableGitHub : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }
}
