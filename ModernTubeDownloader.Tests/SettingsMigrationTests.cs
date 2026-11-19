using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Tests;

public sealed class SettingsMigrationTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "MTDSettingsMigration-" + Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public Fixture() { Directory.CreateDirectory(Root); Paths = AppPaths.Create(Root); }
        public async Task<SettingsService> Load(string? json = null)
        {
            if (json is not null) await File.WriteAllTextAsync(Paths.SettingsFile, json);
            var service = new SettingsService(Paths, new NullAppLogger()); await service.LoadAsync(); return service;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public async Task NewProfileUsesAutomaticDailyInstallOnExitWithoutInventingLegacyConsent()
    {
        using var fixture = new Fixture(); var service = await fixture.Load();
        Assert.False(service.LoadFailed); Assert.False(service.UpdatePolicyNeedsConsent);
        Assert.True(service.Current.Updates.AutomaticChecks); Assert.Equal(UpdateCheckFrequency.StartupDaily, service.Current.Updates.Frequency);
        Assert.Equal(UpdateInstallMode.InstallOnExit, service.Current.Updates.Mode);
        Assert.False(File.Exists(fixture.Paths.SettingsFile));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"updates\":null}")]
    [InlineData("{\"updates\":{\"automaticChecks\":true}}")]
    [InlineData("{\"updates\":{\"automaticChecks\":true,\"mode\":0}}")]
    [InlineData("{\"updates\":{\"automaticChecks\":true,\"mode\":999}}")]
    [InlineData("{\"UPDATES\":{\"AUTOMATICCHECKS\":true,\"MODE\":0}}")]
    public async Task LegacyAmbiguousOrMissingModeNeverSilentlyEnablesInstallation(string json)
    {
        using var fixture = new Fixture(); var service = await fixture.Load(json);
        var original = await File.ReadAllBytesAsync(fixture.Paths.SettingsFile);
        Assert.False(service.LoadFailed); Assert.True(service.UpdatePolicyNeedsConsent);
        Assert.True(service.Current.Updates.AutomaticChecks); Assert.Equal(UpdateInstallMode.NotifyOnly, service.Current.Updates.Mode);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Paths.SettingsFile));
        await service.SaveAsync(); service = await fixture.Load();
        Assert.True(service.UpdatePolicyNeedsConsent); Assert.Equal(UpdateInstallMode.NotifyOnly, service.Current.Updates.Mode);
    }

    [Theory]
    [InlineData(false, 0, 0)] [InlineData(false, 2, 0)]
    [InlineData(true, 0, 1)] [InlineData(true, 1, 0)] [InlineData(true, 2, 0)]
    public async Task ExplicitDisabledConfirmedNotifyOrExistingAutomaticPolicyIsPreserved(bool automatic, int mode, int policyVersion)
    {
        using var fixture = new Fixture();
        var json = JsonSerializer.Serialize(new { updates = new { automaticChecks = automatic, mode, policyVersion, frequency = 2 } });
        var service = await fixture.Load(json);
        Assert.False(service.UpdatePolicyNeedsConsent); Assert.Equal(automatic, service.Current.Updates.AutomaticChecks);
        Assert.Equal((UpdateInstallMode)mode, service.Current.Updates.Mode); Assert.Equal(UpdateCheckFrequency.Weekly, service.Current.Updates.Frequency);
        await service.SaveAsync(); service = await fixture.Load();
        Assert.False(service.UpdatePolicyNeedsConsent); Assert.Equal(automatic, service.Current.Updates.AutomaticChecks);
        Assert.Equal((UpdateInstallMode)mode, service.Current.Updates.Mode);
    }

    [Fact]
    public async Task RecognizableDisabledPolicyWithoutModeStaysDisabledAndNeedsNoOptInPrompt()
    {
        using var fixture = new Fixture(); var service = await fixture.Load("{\"updates\":{\"automaticChecks\":false}}");
        Assert.False(service.Current.Updates.AutomaticChecks); Assert.False(service.UpdatePolicyNeedsConsent);
        Assert.Equal(UpdateInstallMode.NotifyOnly, service.Current.Updates.Mode);
    }

    [Fact]
    public async Task LegacyStoreWithoutSettingsUsesConservativePolicyUntilExplicitChoice()
    {
        using var fixture = new Fixture(); var service = await fixture.Load();
        service.PreserveLegacyUpdatePolicyWithoutSettings();
        Assert.True(service.UpdatePolicyNeedsConsent); Assert.Equal(UpdateInstallMode.NotifyOnly, service.Current.Updates.Mode);
        await service.SaveAsync(); service = await fixture.Load();
        Assert.True(service.UpdatePolicyNeedsConsent); Assert.Equal(UpdateInstallMode.NotifyOnly, service.Current.Updates.Mode);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"updates\":\"unexpected\"}")]
    public async Task CorruptSettingsAreNeverOverwrittenByDefaultsOrImplicitShutdownSave(string json)
    {
        using var fixture = new Fixture(); var service = await fixture.Load(json);
        var original = await File.ReadAllBytesAsync(fixture.Paths.SettingsFile);
        Assert.True(service.LoadFailed); Assert.False(service.Current.Updates.AutomaticChecks);
        Assert.Equal(UpdateInstallMode.NotifyOnly, service.Current.Updates.Mode);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Paths.SettingsFile));
        Assert.False(File.Exists(fixture.Paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public async Task DirectoryAtSettingsPathFailsClosedAndPreservesItsContents()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Paths.SettingsFile);
        var sentinel = Path.Combine(fixture.Paths.SettingsFile, "sentinel.json");
        await File.WriteAllTextAsync(sentinel, "retain");
        var service = await fixture.Load();
        Assert.True(service.LoadFailed); Assert.False(service.Current.Updates.AutomaticChecks);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync());
        Assert.Equal("retain", await File.ReadAllTextAsync(sentinel));
    }

    [Fact]
    public async Task LockedSettingsAreNotTreatedAsANewProfileOrOverwritten()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Paths.SettingsFile, "{\"updates\":{\"automaticChecks\":false}}");
        var original = await File.ReadAllBytesAsync(fixture.Paths.SettingsFile);
        using (var locked = new FileStream(fixture.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = await fixture.Load();
            Assert.True(service.LoadFailed); Assert.False(service.Current.Updates.AutomaticChecks);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync());
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Paths.SettingsFile));
    }

    [Fact]
    public async Task SavingUserChoicePreservesUnknownSettingsAndUpdaterFields()
    {
        using var fixture = new Fixture();
        var service = await fixture.Load("""
            {"language":"pl","queuePaused":true,"maxSimultaneousDownloads":3,
             "futureSettings":{"key":"retain","ids":[7,11,13]},
             "updates":{"automaticChecks":false,"mode":0,"futureUpdatePolicy":{"channel":"stable-custom","keep":true}}}
            """);
        service.Current.Updates.PolicyVersion = 1;
        await service.SaveAsync();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Paths.SettingsFile));
        Assert.Equal("retain", json.RootElement.GetProperty("futureSettings").GetProperty("key").GetString());
        Assert.Equal(new[] { 7, 11, 13 }, json.RootElement.GetProperty("futureSettings").GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal("stable-custom", json.RootElement.GetProperty("updates").GetProperty("futureUpdatePolicy").GetProperty("channel").GetString());
        Assert.True(json.RootElement.GetProperty("updates").GetProperty("futureUpdatePolicy").GetProperty("keep").GetBoolean());
        service = await fixture.Load(); Assert.True(service.Current.QueuePaused); Assert.Equal(3, service.Current.MaxSimultaneousDownloads);
        Assert.Equal("pl", service.Current.Language); Assert.False(service.Current.Updates.AutomaticChecks);
    }

    [Fact]
    public async Task CancelledLoadDoesNotMisdiagnoseExistingSettingsAsCorrupt()
    {
        using var fixture = new Fixture(); await File.WriteAllTextAsync(fixture.Paths.SettingsFile, "{}");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = new SettingsService(fixture.Paths, new NullAppLogger());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LoadAsync(cancellation.Token));
        Assert.False(service.LoadFailed); Assert.Equal("{}", await File.ReadAllTextAsync(fixture.Paths.SettingsFile));
    }
}
