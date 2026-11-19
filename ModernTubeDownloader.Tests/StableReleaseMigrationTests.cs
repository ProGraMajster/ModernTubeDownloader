using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

public sealed class StableReleaseMigrationTests
{
    private const string StableVersion = "1.3.0";
    private const string CandidateVersion = "1.3.0-candidate.1";

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "MTDStableMigration-" + Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public string Ledger => Path.Combine(Root, "experience.json");
        public Fixture() { Directory.CreateDirectory(Root); Paths = AppPaths.Create(Root); }

        public async Task<Dictionary<string, byte[]>> SeedLegacyProfile()
        {
            // These are disposable synthetic bytes, never the owner's collection.
            var id = Guid.NewGuid();
            var files = new Dictionary<string, string>
            {
                [Paths.SettingsFile] = """
                    {"language":"pl","queuePaused":true,"futureSetting":{"keep":[7,11,13]},
                     "updates":{"automaticChecks":false,"mode":0,"policyVersion":1,"installAtStartup":false,
                     "futurePolicy":{"keep":true}}}
                    """,
                [Paths.QueueFile] = JsonSerializer.Serialize(new[] { new { id, sourceUrl = "https://example.invalid/watch/synthetic", status = 0, futureJob = "keep" } }),
                [Paths.HistoryFile] = JsonSerializer.Serialize(new[] { new { queueItemId = id, title = "Synthetic legacy recording", futureHistory = "keep" } }),
                [Paths.LiveSessionsFile] = "{\"sessions\":[],\"futureLive\":{\"keep\":true}}",
                [Path.Combine(Root, "Batch", "session.json")] = "{\"schemaVersion\":1,\"futureBatch\":{\"keep\":true}}",
                [Path.Combine(Root, "Auth", "cookies.txt")] = "# Netscape HTTP Cookie File\n# Synthetic empty fixture; no credentials\n",
                [Path.Combine(Root, "metadata", "synthetic.json")] = "{\"id\":\"synthetic\",\"webpage_url\":\"https://example.invalid/watch/synthetic\",\"futureMetadata\":true}"
            };
            foreach (var (path, contents) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, contents);
            }
            return files.Keys.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        }

        public Task<UserExperienceService> Open(string version = StableVersion) =>
            UserExperienceService.OpenAsync(Paths, new NullAppLogger(), version);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static StartupExperience Next(UserExperienceService service, AppInterfaceMode mode) =>
        service.Next(false, false, false, mode, false);

    private static void AssertPreserved(IReadOnlyDictionary<string, byte[]> hashes)
    {
        foreach (var (path, hash) in hashes) Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData(AppInterfaceMode.Classic)]
    [InlineData(AppInterfaceMode.ModernDashboard)]
    public async Task Legacy121ChoiceAndStableAcknowledgementPreserveAllStoresAndOptOut(AppInterfaceMode chosen)
    {
        using var fixture = new Fixture();
        var hashes = await fixture.SeedLegacyProfile();
        var settings = new SettingsService(fixture.Paths, new NullAppLogger());
        await settings.LoadAsync();
        Assert.False(settings.LoadFailed);
        Assert.False(settings.UpdatePolicyNeedsConsent);
        Assert.False(settings.Current.Updates.AutomaticChecks);
        Assert.False(settings.Current.Updates.InstallAtStartup);
        Assert.Equal(UpdateInstallMode.NotifyOnly, settings.Current.Updates.Mode);

        var service = await fixture.Open();
        Assert.True(service.State.ExistingProfile);
        Assert.False(service.IsCandidate);
        Assert.Equal(StableVersion, service.Version);
        Assert.Equal(StartupExperience.UpgradeChoice, Next(service, AppInterfaceMode.Classic));
        await service.BeginAsync(new SetupDraft { Language = "pl", Interface = chosen, AutomaticUpdates = false,
            UpdateMode = UpdateInstallMode.NotifyOnly, InstallUpdatesAtStartup = false });
        service = await fixture.Open();
        Assert.Equal(chosen, service.State.Draft!.Interface);
        Assert.False(service.State.Draft.AutomaticUpdates);
        await service.CompleteAsync(upgrade: true);
        Assert.Equal(StartupExperience.WhatsNew, Next(service, chosen));
        Assert.Null(service.State.LastWhatsNewAcknowledgedVersion);
        var ledgerBeforeAcknowledgement = await File.ReadAllBytesAsync(fixture.Ledger);

        // Closing/reopening without explicit acknowledgement must not silently
        // consume the stable release or repeat the already completed UI choice.
        service = await fixture.Open();
        Assert.Equal(ledgerBeforeAcknowledgement, await File.ReadAllBytesAsync(fixture.Ledger));
        Assert.Equal(StartupExperience.WhatsNew, Next(service, chosen));
        Assert.Equal(UserExperienceService.ChoiceFamily, service.State.InterfaceChoiceFamily);
        await service.AcknowledgeWhatsNewAsync();
        service = await fixture.Open();
        Assert.Equal(StableVersion, service.State.LastWhatsNewAcknowledgedVersion);
        Assert.Equal(StartupExperience.None, Next(service, chosen));
        AssertPreserved(hashes);

        var restoredSettings = new SettingsService(fixture.Paths, new NullAppLogger());
        await restoredSettings.LoadAsync();
        Assert.False(restoredSettings.Current.Updates.AutomaticChecks);
        Assert.False(restoredSettings.Current.Updates.InstallAtStartup);
        Assert.Equal(UpdateInstallMode.NotifyOnly, restoredSettings.Current.Updates.Mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidatePromotionShowsStableNotesOnceWithoutRepeatingOrLosingPriorChoice(bool skippedChoice)
    {
        using var fixture = new Fixture();
        var hashes = await fixture.SeedLegacyProfile();
        var candidate = await fixture.Open(CandidateVersion);
        if (skippedChoice) await candidate.SkipAsync(upgrade: true);
        else await candidate.CompleteAsync(upgrade: true);
        await candidate.AcknowledgeWhatsNewAsync();
        var candidateLedger = await File.ReadAllBytesAsync(fixture.Ledger);

        var stable = await fixture.Open();
        Assert.Equal(candidateLedger, await File.ReadAllBytesAsync(fixture.Ledger));
        Assert.Equal(skippedChoice, stable.State.InterfaceChoiceSkipped);
        Assert.Equal(CandidateVersion, stable.State.LastWhatsNewAcknowledgedVersion);
        Assert.Equal(StartupExperience.WhatsNew, Next(stable, AppInterfaceMode.Classic));
        var notes = Assert.IsType<InstalledReleaseNotes>(ReleaseNotesCatalog.ForInstalled(stable.Version, "pl"));
        Assert.Equal(StableVersion, notes.Version);
        Assert.False(notes.IsPrerelease);
        Assert.Equal("https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.3.0", notes.OfficialUrl.AbsoluteUri);

        // A failed durable ACK must leave both the in-memory and persisted
        // candidate acknowledgement intact, so the next launch still shows 1.3.0.
        await using (var held = new FileStream(fixture.Ledger, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(stable.AcknowledgeWhatsNewAsync);
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Locked ledger accepted replacement.");
            Assert.Equal(CandidateVersion, stable.State.LastWhatsNewAcknowledgedVersion);
        }
        Assert.Equal(candidateLedger, await File.ReadAllBytesAsync(fixture.Ledger));
        stable = await fixture.Open();
        Assert.Equal(StartupExperience.WhatsNew, Next(stable, AppInterfaceMode.Classic));
        await stable.AcknowledgeWhatsNewAsync();
        var restarted = await fixture.Open(StableVersion + "+rebuild");
        Assert.Equal(StableVersion, restarted.State.LastWhatsNewAcknowledgedVersion);
        Assert.Equal(skippedChoice, restarted.State.InterfaceChoiceSkipped);
        Assert.Equal(StartupExperience.None, Next(restarted, AppInterfaceMode.Classic));
        AssertPreserved(hashes);
    }

    private sealed class StableFeedHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            Assert.Equal("https://api.github.com/repos/ProGraMajster/ModernTubeDownloader/releases/latest", request.RequestUri!.AbsoluteUri);
            const string archive = "ModernTubeDownloader-1.3.0-win-x64.zip";
            var repository = GitHubApplicationReleaseSource.Repository;
            var body = JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v1.3.0",
                html_url = repository + "/releases/tag/v1.3.0", body = "Stable 1.3.0 synthetic release metadata",
                assets = new[] { archive + ".sha256", "ModernTubeDownloader-1.3.0-win-x64-symbols.zip", archive }
                    .Select(name => new { name, browser_download_url = repository + "/releases/download/v1.3.0/" + name }) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Theory]
    [InlineData("1.2.1", false)]
    [InlineData("1.2.1", true)]
    [InlineData(CandidateVersion, false)]
    [InlineData(CandidateVersion, true)]
    public async Task StableFeedSelectsExactAssetsAndPreservesLaterOrSkipAcrossRestart(string installed, bool skip)
    {
        using var fixture = new Fixture();
        var hashes = await fixture.SeedLegacyProfile();
        var settings = new SettingsService(fixture.Paths, new NullAppLogger());
        await settings.LoadAsync();
        using var handler = new StableFeedHandler();
        using var http = new HttpClient(handler);
        using (var service = new ApplicationUpdateService(fixture.Paths, settings, http, new NullAppLogger(),
                   new GitHubApplicationReleaseSource(http), installed))
        {
            await service.CheckAsync();
            var release = Assert.IsType<ApplicationRelease>(service.State.Available);
            Assert.Equal(StableVersion, release.Version);
            Assert.Equal(GitHubApplicationReleaseSource.Repository + "/releases/download/v1.3.0/ModernTubeDownloader-1.3.0-win-x64.zip", release.Archive.AbsoluteUri);
            Assert.Equal(release.Archive.AbsoluteUri + ".sha256", release.Checksum.AbsoluteUri);
            Assert.False(service.Ready);
            Assert.False(service.ShouldInstallOnExit);
            await service.DeferAsync(skip);
        }
        using var restored = new ApplicationUpdateService(fixture.Paths, settings, http, new NullAppLogger(),
            new GitHubApplicationReleaseSource(http), installed);
        await restored.InitializeAsync();
        await restored.RunAutomaticCycleAsync();
        Assert.Equal(StableVersion, restored.State.Available?.Version);
        Assert.Equal(skip ? StableVersion : null, restored.State.SkippedVersion);
        Assert.True(restored.State.RemindAfter > DateTimeOffset.UtcNow);
        Assert.False(restored.State.InstallOnExit);
        Assert.False(restored.ShouldInstallOnExit);
        Assert.Null(restored.State.StagedArchive);
        Assert.Single(handler.Requests); // No archive, checksum or installer request.
        AssertPreserved(hashes);
    }

    [Fact]
    public async Task InstalledStableReleaseDoesNotOfferItselfOrCreateInstallIntent()
    {
        using var fixture = new Fixture();
        var settings = new SettingsService(fixture.Paths, new NullAppLogger());
        await settings.LoadAsync();
        using var http = new HttpClient(new StableFeedHandler());
        using var service = new ApplicationUpdateService(fixture.Paths, settings, http, new NullAppLogger(),
            new GitHubApplicationReleaseSource(http), StableVersion);
        await service.CheckAsync();
        Assert.Equal("Updates.Current", service.StatusKey);
        Assert.Null(service.State.Available);
        Assert.Null(service.State.StagedArchive);
        Assert.False(service.State.InstallOnExit);
        Assert.False(service.ShouldInstallOnExit);
    }
}
