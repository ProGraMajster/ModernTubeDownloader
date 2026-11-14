using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Tests;

public sealed class UserExperienceServiceTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "MTDExperience-" + Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public string Marker => Path.Combine(Root, "experience.json");
        public Fixture() { Directory.CreateDirectory(Root); Paths = AppPaths.Create(Root); }
        public Task<UserExperienceService> Open(string version = "1.3.0-candidate.1+test") => UserExperienceService.OpenAsync(Paths, new NullAppLogger(), version);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static StartupExperience Next(UserExperienceService service, bool consent = false) =>
        service.Next(false, false, false, AppInterfaceMode.Classic, consent);

    [Fact]
    public async Task NewProfileResumesSavedDraftAndCompletedSetupDoesNotRepeatAfterRestart()
    {
        using var fixture = new Fixture(); var first = await fixture.Open();
        Assert.False(first.State.ExistingProfile); Assert.False(first.SettingsExisted);
        Assert.Equal(StartupExperience.Welcome, Next(first));
        await first.BeginAsync(new SetupDraft { Language = "pl", Interface = AppInterfaceMode.ModernDashboard, Step = 1 });
        await first.SaveDraftAsync(new SetupDraft { Language = "en", Interface = AppInterfaceMode.Classic, Step = 4,
            Output = Path.Combine(fixture.Root, "Output"), AutomaticUpdates = false, UpdateMode = UpdateInstallMode.NotifyOnly });
        var resumed = await fixture.Open();
        Assert.Equal(SetupProgress.InProgress, resumed.State.Setup); Assert.Equal(StartupExperience.Welcome, Next(resumed));
        Assert.Equal(4, resumed.State.Draft!.Step); Assert.Equal("en", resumed.State.Draft.Language);
        Assert.Equal(AppInterfaceMode.Classic, resumed.State.Draft.Interface); Assert.False(resumed.State.Draft.AutomaticUpdates);
        await resumed.CompleteAsync(false);
        var completed = await fixture.Open();
        Assert.Equal(SetupProgress.Completed, completed.State.Setup); Assert.Null(completed.State.Draft);
        Assert.Equal(StartupExperience.WhatsNew, Next(completed));
        await completed.AcknowledgeWhatsNewAsync();
        Assert.Equal(StartupExperience.None, Next(await fixture.Open()));
    }

    [Fact]
    public async Task ExplicitSkipIsDurableAndManualReopenCanCompleteSetup()
    {
        using var fixture = new Fixture(); var service = await fixture.Open();
        await service.BeginAsync(new SetupDraft { Step = 2 }); await service.SkipAsync(false);
        service = await fixture.Open(); Assert.Equal(SetupProgress.Skipped, service.State.Setup); Assert.Null(service.State.Draft);
        Assert.NotEqual(StartupExperience.Welcome, Next(service));
        await service.BeginAsync(new SetupDraft { Step = 0 }); Assert.Equal(SetupProgress.InProgress, service.State.Setup);
        await service.CompleteAsync(false); Assert.Equal(SetupProgress.Completed, (await fixture.Open()).State.Setup);
    }

    [Theory]
    [InlineData("settings.json")] [InlineData("history.json")] [InlineData("queue.json")]
    [InlineData("live-sessions.json")] [InlineData("appearance.json")]
    [InlineData("Auth/cookies.txt")] [InlineData("Batch/import.json")]
    public async Task ExistingProfileDetectionNeedsNoAccessToPrivateFileContents(string relative)
    {
        using var fixture = new Fixture(); var file = Path.Combine(fixture.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var bytes = "private synthetic contents; intentionally not JSON"u8.ToArray(); await File.WriteAllBytesAsync(file, bytes);
        await using (var held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var service = await fixture.Open();
            Assert.True(service.State.ExistingProfile); Assert.Equal(relative == "settings.json", service.SettingsExisted);
            Assert.Equal(StartupExperience.UpgradeChoice, Next(service));
            await service.SkipAsync(true); await service.AcknowledgeWhatsNewAsync();
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
        Assert.Equal(StartupExperience.None, Next(await fixture.Open(), true));
    }

    [Fact]
    public async Task MigrationJourneyPreservesTenThousandIdsAndExactPrivateStoreBytes()
    {
        using var fixture = new Fixture();
        var ids = Enumerable.Range(0, 10_000).Select(_ => Guid.NewGuid()).ToArray();
        var queue = ids.Select((id, i) => new { id, title = "Synthetic " + i, sourceUrl = "https://example.invalid/" + i, status = 0 });
        var history = ids.Select((id, i) => new { queueItemId = id, title = "Synthetic " + i, status = "Completed" });
        await File.WriteAllTextAsync(fixture.Paths.QueueFile, JsonSerializer.Serialize(queue, JsonDefaults.Options));
        await File.WriteAllTextAsync(fixture.Paths.HistoryFile, JsonSerializer.Serialize(history, JsonDefaults.Options));
        Directory.CreateDirectory(fixture.Paths.AuthenticationDirectory);
        var cookieFile = Path.Combine(fixture.Paths.AuthenticationDirectory, "cookies.txt");
        await File.WriteAllTextAsync(cookieFile, "# Synthetic cookies; no user credentials\n");
        var files = new[] { fixture.Paths.QueueFile, fixture.Paths.HistoryFile, cookieFile };
        var before = files.ToDictionary(f => f, f => SHA256.HashData(File.ReadAllBytes(f)));
        var held = files.Select(f => new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToArray();
        try
        {
            var service = await fixture.Open(); await service.BeginAsync(new SetupDraft { Interface = AppInterfaceMode.ModernDashboard });
            await service.CompleteAsync(true); await service.AcknowledgeWhatsNewAsync();
            Assert.Equal(StartupExperience.None, Next(await fixture.Open()));
        }
        finally { foreach (var stream in held) stream.Dispose(); }
        foreach (var file in files) Assert.Equal(before[file], SHA256.HashData(await File.ReadAllBytesAsync(file)));
        using var queueJson = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Paths.QueueFile));
        using var historyJson = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Paths.HistoryFile));
        Assert.Equal(ids, queueJson.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(ids, historyJson.RootElement.EnumerateArray().Select(e => e.GetProperty("queueItemId").GetGuid()));
    }

    [Theory]
    [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(false, false, true)]
    public async Task RecoveryBackgroundAndExternalActivationNeverInterruptWithPrompts(bool classic, bool background, bool external)
    {
        using var fixture = new Fixture(); var service = await fixture.Open();
        Assert.Equal(StartupExperience.None, service.Next(classic, background, external, AppInterfaceMode.Classic, true));
        await service.CompleteAsync(false);
        Assert.Equal(StartupExperience.None, service.Next(classic, background, external, AppInterfaceMode.ModernDashboard, false));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExistingClassicChoiceIsOfferedOnceAndPreservesSkipDecision(bool skip)
    {
        using var fixture = new Fixture(); await File.WriteAllTextAsync(fixture.Paths.SettingsFile, "{}");
        var service = await fixture.Open(); Assert.Equal(StartupExperience.UpgradeChoice, Next(service));
        if (skip) await service.SkipAsync(true); else await service.CompleteAsync(true);
        service = await fixture.Open(); Assert.Equal(UserExperienceService.ChoiceFamily, service.State.InterfaceChoiceFamily);
        Assert.Equal(skip, service.State.InterfaceChoiceSkipped); Assert.NotEqual(StartupExperience.UpgradeChoice, Next(service, true));
    }

    [Fact]
    public async Task ExistingModernProfileOnlyNeedsChoiceWhenUpdatePolicyIsAmbiguous()
    {
        using var fixture = new Fixture(); await File.WriteAllTextAsync(fixture.Paths.SettingsFile, "{}");
        var service = await fixture.Open();
        Assert.Equal(StartupExperience.WhatsNew, service.Next(false, false, false, AppInterfaceMode.ModernDashboard, false));
        Assert.Equal(StartupExperience.UpgradeChoice, service.Next(false, false, false, AppInterfaceMode.ModernDashboard, true));
    }

    [Theory]
    [InlineData("{not json")] [InlineData("{\"schemaVersion\":99,\"future\":{\"keep\":true}}")]
    [InlineData("{}")] [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"setup\":0}")]
    [InlineData("{\"schemaVersion\":1,\"existingProfile\":false}")]
    public async Task CorruptOrFutureMarkerSuppressesAutomaticPromptsWithoutOverwritingOriginal(string json)
    {
        using var fixture = new Fixture(); await File.WriteAllTextAsync(fixture.Marker, json);
        await File.WriteAllTextAsync(fixture.Paths.HistoryFile, "[]");
        var before = await File.ReadAllBytesAsync(fixture.Marker); var service = await fixture.Open();
        Assert.False(service.Available); Assert.Equal(StartupExperience.None, Next(service, true));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Marker));
        Assert.False((await fixture.Open()).Available);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Marker));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AcknowledgeWhatsNewAsync());
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Marker));
    }

    [Fact]
    public async Task FutureMarkerCannotBeReplacedEvenByManualSetup()
    {
        using var fixture = new Fixture(); const string original = "{\"schemaVersion\":99,\"future\":true}";
        await File.WriteAllTextAsync(fixture.Marker, original); var service = await fixture.Open();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.BeginAsync(new SetupDraft()));
        Assert.Equal(original, await File.ReadAllTextAsync(fixture.Marker));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "experience.json.preserved-*"));
    }

    [Fact]
    public async Task ManualRecoveryOfMalformedMarkerRetainsItsExactBytes()
    {
        using var fixture = new Fixture(); const string original = "{interrupted json 🛠";
        await File.WriteAllTextAsync(fixture.Marker, original); var service = await fixture.Open();
        await service.BeginAsync(new SetupDraft { Language = "pl", Step = 2 });
        Assert.True(service.Available); Assert.Equal(SetupProgress.InProgress, service.State.Setup);
        var backup = Assert.Single(Directory.EnumerateFiles(fixture.Root, "experience.json.preserved-*"));
        Assert.Equal(original, await File.ReadAllTextAsync(backup));
        Assert.Equal(2, (await fixture.Open()).State.Draft!.Step);
    }

    [Fact]
    public async Task ProfilePathOccupiedByAFileSuppressesJourneyWithoutStoppingStartupOrReplacingFile()
    {
        using var fixture = new Fixture(); var occupied = Path.Combine(fixture.Root, "profile-path");
        const string original = "Existing opaque profile-path evidence"; await File.WriteAllTextAsync(occupied, original);
        var service = await UserExperienceService.OpenAsync(AppPaths.Create(occupied), new NullAppLogger(), "1.3.0-candidate.1");
        Assert.False(service.Available); Assert.True(service.State.ExistingProfile);
        Assert.Equal(StartupExperience.None, Next(service, true));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.BeginAsync(new SetupDraft()));
        Assert.Equal(original, await File.ReadAllTextAsync(occupied)); Assert.Single(Directory.EnumerateFileSystemEntries(fixture.Root));
    }

    [Fact]
    public async Task MarkerPathOccupiedByDirectoryRetainsContentsAndCannotBeManuallyReplaced()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.Marker);
        var evidence = Path.Combine(fixture.Marker, "preserve.txt"); await File.WriteAllTextAsync(evidence, "original");
        var service = await fixture.Open();
        Assert.False(service.Available); Assert.True(service.State.ExistingProfile); Assert.Equal(StartupExperience.None, Next(service));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.BeginAsync(new SetupDraft()));
        Assert.Equal("original", await File.ReadAllTextAsync(evidence));
        Assert.Single(Directory.EnumerateFileSystemEntries(fixture.Root));
    }

    [Fact]
    public async Task UnreadableMarkerIsNotAssumedMalformedOrOverwrittenByManualRecovery()
    {
        using var fixture = new Fixture(); const string original = "{\"schemaVersion\":99,\"future\":true}";
        await File.WriteAllTextAsync(fixture.Marker, original);
        await using (var held = new FileStream(fixture.Marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var service = await fixture.Open();
            Assert.False(service.Available); Assert.True(service.State.ExistingProfile);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.BeginAsync(new SetupDraft()));
            Assert.Empty(Directory.EnumerateFiles(fixture.Root, "experience.json.preserved-*"));
        }
        Assert.Equal(original, await File.ReadAllTextAsync(fixture.Marker));
        Assert.False((await fixture.Open()).Available);
    }

    [Theory]
    [InlineData("Auth")] [InlineData("Batch")]
    public async Task LegacyEvidencePathOccupiedByFileStillIdentifiesExistingProfile(string relative)
    {
        using var fixture = new Fixture(); var evidence = Path.Combine(fixture.Root, relative);
        await File.WriteAllTextAsync(evidence, "Existing private evidence");
        var service = await fixture.Open(); Assert.True(service.Available); Assert.True(service.State.ExistingProfile);
        Assert.Equal("Existing private evidence", await File.ReadAllTextAsync(evidence));
    }

    [Fact]
    public async Task CreatingMarkerDoesNotOverwriteAnOlderInterruptedWriteArtifact()
    {
        using var fixture = new Fixture(); var previous = fixture.Marker + ".tmp";
        await File.WriteAllTextAsync(previous, "Preserved interrupted write");
        var service = await fixture.Open(); Assert.True(service.Available);
        Assert.Equal("Preserved interrupted write", await File.ReadAllTextAsync(previous));
        Assert.True(File.Exists(fixture.Marker));
    }

    [Theory]
    [InlineData("interface")] [InlineData("updateMode")] [InlineData("container")]
    [InlineData("conflict")] [InlineData("frequency")]
    public async Task InvalidDraftEnumsAreDiscardedWithoutRewritingStoredEvidence(string field)
    {
        using var fixture = new Fixture();
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, existingProfile = false, setup = 1,
            draft = new Dictionary<string, object?> { [field] = 999, ["step"] = 2 } });
        await File.WriteAllTextAsync(fixture.Marker, json);
        var service = await fixture.Open();
        Assert.True(service.Available); Assert.Null(service.State.Draft); Assert.Equal(StartupExperience.Welcome, Next(service));
        Assert.Equal(json, await File.ReadAllTextAsync(fixture.Marker));
    }

    [Theory]
    [InlineData(null, null, "en")]
    [InlineData("unknown-locale", "unknown-quality", "en")]
    [InlineData("PL", "best", "pl")]
    public async Task DraftNullStringsAndUnknownSelectionsHaveSafeUiValues(string? language, string? quality, string expectedLanguage)
    {
        using var fixture = new Fixture();
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, existingProfile = false, setup = 1,
            draft = new { language, quality, output = (string?)null, temporary = (string?)null, filename = (string?)null, step = 999 } });
        await File.WriteAllTextAsync(fixture.Marker, json);
        var service = await fixture.Open(); var draft = Assert.IsType<SetupDraft>(service.State.Draft);
        Assert.Equal(expectedLanguage, draft.Language); Assert.Equal("best", draft.Quality); Assert.Equal(5, draft.Step);
        Assert.Equal("", draft.Output); Assert.Equal("", draft.Temporary); Assert.Equal("", draft.Filename);
        Assert.Throws<InvalidDataException>(() => UserExperienceService.ValidateFolders(draft));
        Assert.Equal(json, await File.ReadAllTextAsync(fixture.Marker));
    }

    [Fact]
    public async Task MarkerUnknownFieldsSurviveAcknowledgementAndInterfaceChoice()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Marker, "{\"schemaVersion\":1,\"existingProfile\":true,\"setup\":0,\"futureOption\":{\"value\":42}}");
        var service = await fixture.Open(); await service.CompleteAsync(true); await service.AcknowledgeWhatsNewAsync();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Marker));
        Assert.Equal(42, json.RootElement.GetProperty("futureOption").GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("1.3.0-candidate.1", "1.3.0-candidate.1+different", false)]
    [InlineData("1.3.0-candidate.1", "1.3.0-candidate.2", true)]
    [InlineData("1.3.0-candidate.2", "1.3.0-candidate.1", false)]
    [InlineData("1.3.0-candidate.2", "1.3.0", true)]
    [InlineData("1.3.0", "1.3.0-candidate.3", false)]
    [InlineData("1.3.0", "1.3.0+rebuild", false)]
    [InlineData("1.4.0", "1.3.0", false)]
    public async Task WhatsNewIsShownOnlyForUnacknowledgedForwardVersion(string acknowledged, string current, bool expected)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Marker, JsonSerializer.Serialize(new UserExperienceState
        { ExistingProfile = true, InterfaceChoiceFamily = UserExperienceService.ChoiceFamily, LastWhatsNewAcknowledgedVersion = acknowledged }, JsonDefaults.Options));
        var service = await fixture.Open(current);
        Assert.Equal(expected ? StartupExperience.WhatsNew : StartupExperience.None, Next(service));
    }

    [Fact]
    public void FolderValidationChecksWritabilityWithoutCreatingSelectedDirectories()
    {
        using var fixture = new Fixture();
        var draft = new SetupDraft { Output = Path.Combine(fixture.Root, "not-created", "output"), Temporary = Path.Combine(fixture.Root, "not-created", "temp"), Filename = "%(title)s.%(ext)s" };
        UserExperienceService.ValidateFolders(draft); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "not-created")));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, ".mtd-write-probe-*"));
        draft.Output = "relative"; Assert.Throws<InvalidDataException>(() => UserExperienceService.ValidateFolders(draft));
        draft.Output = fixture.Root; draft.Filename = "bad\nname";
        Assert.Throws<InvalidDataException>(() => UserExperienceService.ValidateFolders(draft));
    }
}
