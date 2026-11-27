using System.Net;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed partial class ApplicationUpdateTests
{
    [Theory]
    [InlineData(false, UpdateCheckFrequency.StartupDaily)]
    [InlineData(true, UpdateCheckFrequency.Daily)]
    [InlineData(true, UpdateCheckFrequency.Weekly)]
    public async Task ForegroundStartupCheckRespectsSavedPolicy(bool enabled, UpdateCheckFrequency frequency)
    {
        using var fixture = new Fixture();
        using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.Updates.AutomaticChecks = enabled;
        settings.Current.Updates.Frequency = frequency;
        var feed = new StartupFeed();
        using var service = new ApplicationUpdateService(paths, settings, http, logger, feed, "1.2.0");

        Assert.Equal(StartupUpdateOutcome.NotRequested, await service.CheckForStartupAsync(default, Guid.NewGuid()));
        Assert.Equal(0, feed.Requests);
        Assert.False(service.InstallationPending);
    }

    [Fact]
    public async Task StartupLookupUsesExistingCadenceAndDoesNotPrepareAnInstallerWithoutConsent()
    {
        using var fixture = new Fixture();
        using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.Updates.AutomaticChecks = true;
        settings.Current.Updates.InstallAtStartup = false;
        var feed = new StartupFeed(Release());
        using var service = new ApplicationUpdateService(paths, settings, http, logger, feed, "1.1.0");

        Assert.Equal(StartupUpdateOutcome.Available, await service.CheckForStartupAsync(default, Guid.NewGuid()));
        Assert.Equal(StartupUpdateOutcome.Available, await service.CheckForStartupAsync(default, Guid.NewGuid()));
        Assert.Equal(1, feed.Requests);
        Assert.False(service.Ready);
        Assert.False(service.CanInstallAtStartup);
    }

    [Theory]
    [InlineData(null, "Offline")]
    [InlineData(HttpStatusCode.TooManyRequests, "Failed")]
    public async Task StartupLookupClassifiesNetworkOutageAndRateLimitWithoutChangingPolicy(HttpStatusCode? status, string outcome)
    {
        using var fixture = new Fixture();
        using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.Updates.AutomaticChecks = true;
        settings.Current.Updates.Mode = UpdateInstallMode.NotifyOnly;
        var feed = new StartupFeed(error: new HttpRequestException("Synthetic network failure", null, status));
        using var service = new ApplicationUpdateService(paths, settings, http, logger, feed, "1.1.0");

        Assert.Equal(outcome, (await service.CheckForStartupAsync(default, Guid.NewGuid())).ToString());
        await service.RunAutomaticCycleAsync();
        Assert.Equal(1, feed.Requests);
        Assert.Null(service.State.LastCheck);
        Assert.True(settings.Current.Updates.AutomaticChecks);
        Assert.False(service.InstallationPending);
    }

    [Fact]
    public async Task StartupGuardPreservesIncompleteJournalAndAllowsACompletedOne()
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        UpdatePackageSecurity.VerifyStartupInstallation(installation);
        var backup = Directory.CreateDirectory(Path.Combine(installation, ".mtd-update-backup")).FullName;
        var file = Path.Combine(backup, "journal.json");
        var journal = new UpdateJournal(installation, [new("ModernTubeDownloader.exe", true)], false);
        var original = JsonSerializer.Serialize(journal);
        await File.WriteAllTextAsync(file, original);
        Assert.Throws<InvalidDataException>(() => UpdatePackageSecurity.VerifyStartupInstallation(installation));
        Assert.Equal(original, await File.ReadAllTextAsync(file));

        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(journal with { Completed = true }));
        UpdatePackageSecurity.VerifyStartupInstallation(installation);
    }

    private sealed class StartupFeed(ApplicationRelease? release = null, HttpRequestException? error = null) : IApplicationReleaseSource
    {
        internal int Requests { get; private set; }

        public Task<ApplicationRelease?> GetLatestAsync(CancellationToken token)
        {
            Requests++;
            return error is null ? Task.FromResult(release) : Task.FromException<ApplicationRelease?>(error);
        }
    }
}
