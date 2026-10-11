using ModernFormsNext.Notifications;
using ModernFormsNext.WindowKit.Backend.Windows.Notifications;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Tests;

public sealed class SystemNotificationCoordinatorTests
{
    [Theory]
    [InlineData("pl")] [InlineData("en")]
    public async Task NewFeatureNotificationsRemainAggregateOptInAndRespectMasterSwitch(string language)
    {
        var fake = new FakeNotifications(); var preferences = new NotificationSettings { Enabled = false, UpdateReady = true, BatchAnalysisCompleted = true };
        await using var coordinator = new SystemNotificationCoordinator(fake, () => preferences, new(language), new NullAppLogger());
        coordinator.UpdateReady("1.2.0"); coordinator.BatchCompleted(100, 50000); await coordinator.DrainAsync(); Assert.Empty(fake.Shown);
        preferences.Enabled = true; preferences.BatchAnalysisCompleted = false;
        coordinator.UpdateReady("1.2.0"); coordinator.BatchCompleted(100, 50000); await coordinator.DrainAsync(); Assert.Single(fake.Shown);
        preferences.BatchAnalysisCompleted = true; coordinator.BatchCompleted(100, 50000); await coordinator.DrainAsync(); Assert.Equal(2, fake.Shown.Count);
        Assert.All(fake.Shown, item => { Assert.Empty(item.Images); Assert.DoesNotContain("[Notification", item.Title); Assert.DoesNotContain("[Notification", item.Message); });
    }
    private static DownloadNotificationState Item(Guid id, DownloadStatus state, double percent = 0, int retryDelay = 0)
        => new(id, state, "Private title https://private.invalid", "https://thumbnail.invalid", percent, 1, retryDelay);

    [Fact]
    public async Task MasterOffDoesNoShowUpdateQueryOrImageWorkAndNoDeferredReplay()
    {
        var p = new NotificationSettings { Enabled = false, DownloadProgress = true };
        var fake = new FakeNotifications(); var images = 0;
        await using var coordinator = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger(), _ => { images++; return "cache.png"; });
        var id = Guid.NewGuid();
        coordinator.ObserveDownloads([Item(id, DownloadStatus.DownloadingVideo)]);
        coordinator.ObserveDownloads([Item(id, DownloadStatus.Completed)]);
        coordinator.ObserveLive([new() { State = LiveSessionState.Recording }]);
        await coordinator.SendTestAsync();
        p.Enabled = true; coordinator.ObserveDownloads([Item(id, DownloadStatus.Completed)]);
        await coordinator.DrainAsync();
        Assert.Empty(fake.Shown); Assert.Empty(fake.Updated); Assert.Equal(0, fake.Queries); Assert.Equal(0, images);
    }

    [Fact]
    public async Task ParallelRoundAndRetryWaitingNotifyCompletionOnlyOnceAndCountFinalFailures()
    {
        var fake = new FakeNotifications(); var p = new NotificationSettings();
        await using var c = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger());
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        c.ObserveDownloads([Item(a, DownloadStatus.Queued), Item(b, DownloadStatus.Queued)]);
        c.ObserveDownloads([Item(a, DownloadStatus.Completed), Item(b, DownloadStatus.Waiting, retryDelay: 10)]);
        await c.DrainAsync(); Assert.DoesNotContain(fake.Shown, n => n.Id == "queue-completed");
        c.ObserveDownloads([Item(a, DownloadStatus.Completed), Item(b, DownloadStatus.Failed)]);
        c.ObserveDownloads([Item(a, DownloadStatus.Completed), Item(b, DownloadStatus.Failed)]);
        await c.DrainAsync();
        Assert.Equal(3, fake.Shown.Count); Assert.Contains("Completed: 1 • Failed: 1", Assert.Single(fake.Shown, n => n.Id == "queue-completed").Message);
        Assert.DoesNotContain(fake.Shown, n => n.Title.Contains("Retry"));
    }

    [Fact]
    public async Task ProgressIsEventThrottledAndCompletionUsesSameIdentity()
    {
        var fake = new FakeNotifications(); var p = new NotificationSettings { DownloadProgress = true }; var clock = new ManualClock();
        await using var c = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger(), clock: clock);
        var id = Guid.NewGuid(); c.ObserveDownloads([Item(id, DownloadStatus.Queued)]);
        c.ObserveDownloads([Item(id, DownloadStatus.DownloadingVideo, 2)]); await c.DrainAsync();
        for (var i = 0; i < 200; i++) c.ObserveDownloads([Item(id, DownloadStatus.DownloadingVideo, 3)]);
        await c.DrainAsync(); Assert.Single(fake.Shown); Assert.Empty(fake.Updated);
        clock.Advance(); c.ObserveDownloads([Item(id, DownloadStatus.DownloadingAudio, 65)]); await c.DrainAsync();
        Assert.Equal(.65, Assert.Single(fake.Updated).Progress.Value);
        c.ObserveDownloads([Item(id, DownloadStatus.Completed)]); await c.DrainAsync();
        var media = fake.Shown.Where(n => n.Group == "downloads").ToArray(); Assert.Equal(2, media.Length);
        Assert.Equal(media[0].Id, media[1].Id); Assert.Null(media[1].Progress);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public async Task PrivacyRemovesTitleUrlAndThumbnailsWhileHonoringSoundAndCenterOnly(string language)
    {
        var fake = new FakeNotifications(); var p = new NotificationSettings { PrivacyMode = true, Sound = NotificationSound.Silent, Presentation = NotificationPresentation.CenterOnly };
        var imageCalls = 0;
        await using var c = new SystemNotificationCoordinator(fake, () => p, new(language), new NullAppLogger(), _ => { imageCalls++; return "cache.png"; });
        c.ObserveDownloads([Item(Guid.NewGuid(), DownloadStatus.Completed)]); await c.DrainAsync();
        var n = Assert.Single(fake.Shown); Assert.DoesNotContain("Private", n.Message); Assert.DoesNotContain("https", n.Message);
        Assert.Empty(n.Images); Assert.Equal(0, imageCalls);
        var windows = n.PlatformOptions.Get<WindowsSystemNotificationOptions>(); Assert.True(windows!.Silent); Assert.True(windows.SuppressPopup);
        Assert.Equal(language == "pl" ? "pl-PL" : "en-US", windows.Language);
        Assert.DoesNotContain("[Notification", n.Title);
    }

    [Fact]
    public async Task CachedThumbnailAndTextOnlyCapabilityFallbackDoNotFetchAnyImage()
    {
        var fake = new FakeNotifications(); var p = new NotificationSettings(); var calls = 0;
        await using var c = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger(), _ => { calls++; return "cache.png"; });
        c.ObserveDownloads([Item(Guid.NewGuid(), DownloadStatus.Completed)]); await c.DrainAsync();
        Assert.Equal("cache.png", Assert.Single(Assert.Single(fake.Shown).Images).Source); Assert.Equal(1, calls);
        fake.Capabilities = new("Shell", SystemNotificationFeatures.Basic);
        c.ObserveDownloads([Item(Guid.NewGuid(), DownloadStatus.Completed)]); await c.DrainAsync();
        Assert.Empty(fake.Shown.Last().Images); Assert.Empty(fake.Shown.Last().Actions); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(SystemNotificationAvailability.DisabledByUser)]
    [InlineData(SystemNotificationAvailability.PermissionDenied)]
    [InlineData(SystemNotificationAvailability.Unavailable)]
    public async Task NativePermissionFailureLeavesDownloadEventsUnchanged(SystemNotificationAvailability access)
    {
        var fake = new FakeNotifications { Access = access };
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), new NullAppLogger());
        c.ObserveDownloads([Item(Guid.NewGuid(), DownloadStatus.Completed)]); await c.DrainAsync(); Assert.Empty(fake.Shown);
    }

    [Fact]
    public async Task OptionalNativeExceptionIsContainedAndNextEventStillRuns()
    {
        var fake = new FakeNotifications { Throw = true };
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), new NullAppLogger());
        await c.SendTestAsync(); fake.Throw = false; await c.SendTestAsync(); Assert.Single(fake.Shown);
    }

    [Fact]
    public async Task LivePollingReconnectAndSavedPartHaveDistinctDeduplicatedEvents()
    {
        var fake = new FakeNotifications(); var p = new NotificationSettings();
        await using var c = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger());
        var session = new LiveSession { State = LiveSessionState.WaitingForLive, Title = "Live title" };
        c.ObserveLive([session]); c.ObserveLive([session]);
        session.NextCheckAt = DateTimeOffset.UtcNow; c.ObserveLive([session]);
        session.State = LiveSessionState.Pending; c.ObserveLive([session]);
        session.State = LiveSessionState.Starting; c.ObserveLive([session]);
        session.State = LiveSessionState.Recording; c.ObserveLive([session]);
        session.State = LiveSessionState.Reconnecting; c.ObserveLive([session]); c.ObserveLive([session]);
        session.State = LiveSessionState.Starting; c.ObserveLive([session]);
        session.State = LiveSessionState.Recording; c.ObserveLive([session]);
        session.Parts.Add("trusted.mp4"); c.ObserveLive([session]);
        session.State = LiveSessionState.Partial; c.ObserveLive([session]); await c.DrainAsync();
        Assert.Equal(6, fake.Shown.Count); Assert.Single(fake.Shown.Select(n => n.Id).Distinct());
        Assert.Null(fake.Shown.First(n => n.Progress is not null).Progress!.Value);
        Assert.DoesNotContain(fake.Shown, n => n.Title == "LIVE part saved");
        Assert.Contains(fake.Shown, n => n.Title == "LIVE connection restored");
    }

    [Fact]
    public async Task WarmActivationRejectsPayloadMismatchAndMissingItemsStillGiveSafeNavigation()
    {
        var fake = new FakeNotifications(); NotificationNavigation? navigation = null;
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), new NullAppLogger());
        c.Navigate += (_, value) => navigation = value;
        var intent = new NotificationNavigation("downloads", Guid.NewGuid(), "history", "open-file");
        fake.Activate("download-" + intent.ItemId.ToString("N"), intent.Encode(), "retry", "downloads"); Assert.Null(navigation);
        fake.Activate("download-" + intent.ItemId.ToString("N"), intent.Encode(), "open-file", "downloads"); Assert.Equal(intent, navigation);
        Assert.False(NotificationNavigation.TryParse(intent.Encode() + "|C:\\private.exe", out _));
        Assert.False(NotificationNavigation.TryParse(intent.Encode().Replace("open-file", "run-shell"), out _));
    }

    [Fact]
    public async Task NotificationActivationUsesExistingSingleInstancePipeWithoutSecondWorker()
    {
        var name = "NotifyTest-" + Guid.NewGuid().ToString("N");
        using var primary = new ExternalLinkInstance(name); using var secondary = new ExternalLinkInstance(name);
        NotificationNavigation? delivered = null;
        primary.Start(_ => Task.CompletedTask, onNotification: value => { delivered = value; return Task.CompletedTask; });
        var intent = new NotificationNavigation("live", Guid.NewGuid(), "live", "stop-save");
        Assert.True(await secondary.ForwardNotificationAsync(intent)); Assert.Equal(intent, delivered);
    }

    [Fact]
    public async Task StartedAndProgressTogetherHaveOneInitialShowEvenWithSlowNativeSubmission()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeNotifications { SubmissionGate = gate.Task };
        var p = new NotificationSettings { DownloadStarted = true, DownloadProgress = true };
        var clock = new ManualClock();
        await using var c = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger(), clock: clock);
        var id = Guid.NewGuid();
        for (var i = 0; i < 20; i++) { c.ObserveDownloads([Item(id, DownloadStatus.DownloadingVideo, i)]); clock.Advance(); }
        gate.SetResult(); await c.DrainAsync();
        Assert.Single(fake.Shown); Assert.Empty(fake.Updated);
    }

    [Theory]
    [InlineData("open-file", DownloadStatus.Completed, true, "open-file")]
    [InlineData("open-folder", DownloadStatus.Completed, true, "open-folder")]
    [InlineData("open-file", DownloadStatus.Completed, false, null)]
    [InlineData("retry", DownloadStatus.Failed, false, "retry")]
    [InlineData("retry", DownloadStatus.Completed, false, null)]
    public void ActionsUseCurrentTrustedStateAndDeletedFilesAreNavigationOnly(string action, DownloadStatus state, bool exists, string? expected)
    {
        var intent = new NotificationNavigation("downloads", Guid.NewGuid(), "history", action);
        Assert.Equal(expected, NotificationActionPolicy.Resolve(intent, state, "trusted.mp4", _ => exists, null));
        Assert.Null(NotificationActionPolicy.Resolve(intent, null, null, _ => throw new InvalidOperationException(), null));
    }

    [Fact]
    public void StopSaveOnlyAppliesToCurrentRecordingAndUnknownActionsAreRejected()
    {
        var intent = new NotificationNavigation("live", Guid.NewGuid(), "live", "stop-save");
        Assert.Equal("stop-save", NotificationActionPolicy.Resolve(intent, null, null, _ => false, LiveSessionState.Recording));
        Assert.Null(NotificationActionPolicy.Resolve(intent, null, null, _ => false, LiveSessionState.Reconnecting));
        Assert.Null(NotificationActionPolicy.Resolve(intent with { Action = "run-shell" }, null, "x", _ => true, LiveSessionState.Recording));
    }

    [Fact]
    public async Task ThumbnailPreferenceOffNeverPreparesImageAndEnabledRetryNotifiesOnce()
    {
        var p = new NotificationSettings { ShowThumbnails = false, DownloadRetry = true, QueueCompleted = false };
        var fake = new FakeNotifications();
        await using var c = new SystemNotificationCoordinator(fake, () => p, new("en"), new NullAppLogger(), _ => throw new InvalidOperationException());
        var id = Guid.NewGuid();
        c.ObserveDownloads([Item(id, DownloadStatus.Waiting, retryDelay: 10)]);
        c.ObserveDownloads([Item(id, DownloadStatus.Waiting, retryDelay: 9)]);
        c.ObserveDownloads([Item(id, DownloadStatus.Failed)]); await c.DrainAsync();
        Assert.Equal(2, fake.Shown.Count); Assert.All(fake.Shown, n => Assert.Empty(n.Images));
    }

    [Fact]
    public async Task LiveCompletionAndFailureDoNotRepeatWhileByteProgressIsIndeterminateAndBounded()
    {
        var fake = new FakeNotifications(); var clock = new ManualClock();
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), new NullAppLogger(), clock: clock);
        var session = new LiveSession { State = LiveSessionState.Recording, BytesWritten = 1048576 };
        c.ObserveLive([session]); await c.DrainAsync();
        for (var i = 0; i < 100; i++) { session.BytesWritten++; c.ObserveLive([session]); }
        await c.DrainAsync();
        clock.Advance(); c.ObserveLive([session]); await c.DrainAsync();
        Assert.All(fake.Updated, update => Assert.Null(update.Progress.Value)); Assert.InRange(fake.Updated.Count, 1, 2);
        session.State = LiveSessionState.Completed; c.ObserveLive([session]); c.ObserveLive([session]);
        c.ObserveLive([session, new() { State = LiveSessionState.Failed }]); await c.DrainAsync();
        Assert.Equal(3, fake.Shown.Count);
    }

    [Fact]
    public async Task LanguageChangeDoesNotStartProgressOnUnsupportedProvider()
    {
        var fake = new FakeNotifications { Capabilities = new("Shell", SystemNotificationFeatures.Basic) };
        var p = new NotificationSettings { DownloadProgress = true };
        var language = new LocalizationService("en");
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        await using var c = new SystemNotificationCoordinator(fake, () => p, language, new NullAppLogger());
        try
        {
            c.ObserveDownloads([Item(Guid.NewGuid(), DownloadStatus.DownloadingVideo)]);
            await c.DrainAsync();
            language.SetLanguage("pl"); await c.DrainAsync();
            Assert.Empty(fake.Shown); Assert.Empty(fake.Updated); Assert.Equal(0, fake.Queries);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Fact]
    public async Task LanguageChangeUpdatesOnlyShownLiveProgressAndSharesEventThrottle()
    {
        var fake = new FakeNotifications(); var clock = new ManualClock();
        var p = new NotificationSettings { LiveRecording = false, LiveLost = false };
        var language = new LocalizationService("en");
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        await using var c = new SystemNotificationCoordinator(fake, () => p, language, new NullAppLogger(), clock: clock);
        try
        {
            var session = new LiveSession { State = LiveSessionState.Recording, BytesWritten = 2097152 };
            c.ObserveLive([session], seed: true);
            language.SetLanguage("pl"); await c.DrainAsync();
            Assert.Empty(fake.Shown); Assert.Empty(fake.Updated);

            session.State = LiveSessionState.Reconnecting; c.ObserveLive([session]);
            session.State = LiveSessionState.Recording; c.ObserveLive([session]); await c.DrainAsync();
            Assert.Single(fake.Shown);
            language.SetLanguage("en"); await c.DrainAsync();
            var update = Assert.Single(fake.Updated);
            Assert.Null(update.Progress.Value); Assert.Contains("2.0 MB", update.Progress.ValueText);
            language.SetLanguage("pl"); await c.DrainAsync(); Assert.Single(fake.Updated);
            clock.Advance(); language.SetLanguage("en"); await c.DrainAsync();
            Assert.Equal(2, fake.Updated.Count);
            Assert.Equal(language["Notification.LiveRecording"], fake.Updated.Last().Progress.Status);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Fact]
    public async Task RemovedLiveSessionDoesNotRetainItsProgressIdentity()
    {
        var fake = new FakeNotifications();
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), new NullAppLogger());
        var session = new LiveSession { State = LiveSessionState.Recording };
        c.ObserveLive([session]); await c.DrainAsync();
        c.ObserveLive([]);
        c.ObserveLive([session]); await c.DrainAsync();
        Assert.Equal(2, fake.Shown.Count); Assert.Empty(fake.Updated);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance() => ticks += TimeSpan.TicksPerSecond;
    }
    [Theory]
    [InlineData(WindowsSystemNotificationFeatures.AppLogo, WindowsNotificationImagePlacement.AppLogo, 1)]
    [InlineData(WindowsSystemNotificationFeatures.InlineImages, WindowsNotificationImagePlacement.Inline, 1)]
    [InlineData(WindowsSystemNotificationFeatures.HeroImages, WindowsNotificationImagePlacement.Default, 0)]
    [InlineData(WindowsSystemNotificationFeatures.None, WindowsNotificationImagePlacement.Default, 0)]
    public async Task ImagePlacementIsCapabilityDrivenAndLimitedProvidersRemainTextOnly(
        WindowsSystemNotificationFeatures features, WindowsNotificationImagePlacement placement, int count)
    {
        var fake = new FakeNotifications { Capabilities = new("Windows", SystemNotificationFeatures.Basic | SystemNotificationFeatures.Images)
            { Platform = new WindowsSystemNotificationCapabilities(features) } };
        var lookups = 0;
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), new NullAppLogger(), _ => { lookups++; return "cache.png"; });
        c.ObserveDownloads([Item(Guid.NewGuid(), DownloadStatus.Completed)]); await c.DrainAsync();
        var n = Assert.Single(fake.Shown); Assert.Equal(count, n.Images.Count); Assert.Equal(count, lookups);
        if (count > 0) Assert.Equal(placement, n.Images.Single().PlatformOptions.Get<WindowsSystemNotificationImageOptions>()!.Placement);
    }

    [Fact]
    public async Task DegradationWarningsAreLoggedIndividuallyWithoutChangingCompletedState()
    {
        var fake = new FakeNotifications { Warnings = [new("InvalidImage", "Image could not be displayed."), new("UnsupportedFeature", "Optional layout removed.")] };
        var logger = new RecordingLogger();
        await using var c = new SystemNotificationCoordinator(fake, () => new(), new("en"), logger);
        var item = Item(Guid.NewGuid(), DownloadStatus.Completed);
        c.ObserveDownloads([item]); await c.DrainAsync();
        Assert.Single(fake.Shown); Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Contains(logger.Messages, message => message.Contains("code=InvalidImage") && message.Contains("Image could not be displayed.") && message.Contains("Accepted"));
        Assert.Contains(logger.Messages, message => message.Contains("code=UnsupportedFeature"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("https://thumbnail.invalid"));
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }
    private sealed class FakeNotifications : ISystemNotificationService
    {
        public SystemNotificationCapabilities Capabilities { get; set; } = new("Fake", SystemNotificationFeatures.Basic | SystemNotificationFeatures.Images |
            SystemNotificationFeatures.Actions | SystemNotificationFeatures.Progress | SystemNotificationFeatures.LiveUpdates | SystemNotificationFeatures.IndeterminateProgress |
            SystemNotificationFeatures.Urgency | SystemNotificationFeatures.Replacement)
        { Platform = new WindowsSystemNotificationCapabilities(WindowsSystemNotificationFeatures.SuppressPopup | WindowsSystemNotificationFeatures.Priority | WindowsSystemNotificationFeatures.AppLogo) };
        public IReadOnlyList<SystemNotificationWarning> Warnings { get; set; } = [];
        public SystemNotificationAvailability Access { get; set; } = SystemNotificationAvailability.Ready;
        public bool Throw { get; set; }
        public Task? SubmissionGate { get; set; }
        public int Queries { get; private set; }
        public List<SystemNotification> Shown { get; } = [];
        public List<(SystemNotificationKey Key, SystemNotificationProgress Progress)> Updated { get; } = [];
        public event EventHandler<SystemNotificationActivation>? Activated;
        public event EventHandler<SystemNotificationChange>? Changed { add { } remove { } }
        public void Activate(string id, string payload, string action, string group) => Activated?.Invoke(this, new(id, payload, action, new Dictionary<string, string>()) { Group = group });
        public Task<SystemNotificationAccess> GetStatusAsync(CancellationToken token = default) { Queries++; return Task.FromResult(new SystemNotificationAccess(Access)); }
        public Task<SystemNotificationAccess> RequestPermissionAsync(SystemNotificationPermissionRequest? request = null, CancellationToken token = default) => GetStatusAsync(token);
        public async Task<SystemNotificationResult> ShowAsync(SystemNotification n, CancellationToken token = default)
        { if (Throw) throw new IOException("Native transport failed"); Shown.Add(n); if (SubmissionGate is { } gate) await gate; return new SystemNotificationResult(SystemNotificationStatus.Accepted, new(n.Id, n.Group), Warnings); }
        public Task<SystemNotificationResult> UpdateAsync(SystemNotificationKey key, SystemNotificationProgress progress, CancellationToken token = default)
        { Updated.Add((key, progress)); return Result(); }
        private static Task<SystemNotificationResult> Result() => Task.FromResult(new SystemNotificationResult(SystemNotificationStatus.Accepted));
        public Task<SystemNotificationResult> DismissAsync(SystemNotificationKey key, CancellationToken token = default) => Result();
        public Task<SystemNotificationResult> DismissHistoryAsync(SystemNotificationReference reference, CancellationToken token = default) => Result();
        public Task<SystemNotificationResult> RemoveByIdAsync(string id, CancellationToken token = default) => Result();
        public Task<SystemNotificationResult> RemoveByGroupAsync(string group, CancellationToken token = default) => Result();
        public Task<SystemNotificationResult> ClearAsync(CancellationToken token = default) => Result();
        public Task<IReadOnlyList<SystemNotificationHistoryEntry>> GetHistoryAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SystemNotificationHistoryEntry>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
