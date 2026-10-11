using ModernFormsNext.Notifications;
using ModernFormsNext.WindowKit.Backend.Windows.Notifications;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Services;

public sealed record DownloadNotificationState(Guid Id, DownloadStatus Status, string Title, string? Thumbnail,
    double Percent, int Attempt, int RetryDelay, double? Speed = null, TimeSpan? Eta = null)
{
    public static DownloadNotificationState From(DownloadQueueItem item) => new(item.Id, item.Status,
        item.Title, item.ThumbnailUrl, item.ProgressPercent, item.AttemptCount, item.RetryDelaySeconds, item.SpeedBytesPerSecond, item.Eta);
    public bool Pending => Status is DownloadStatus.Queued or DownloadStatus.Waiting or DownloadStatus.DownloadingVideo
        or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing;
    public bool Active => Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio
        or DownloadStatus.Merging or DownloadStatus.Finalizing;
}

/// <summary>Strict bounded intent, never a shell command, path, URL or metadata payload.</summary>
public sealed record NotificationNavigation(string Group, Guid ItemId, string View, string Action = "open-app")
{
    public string Encode() => $"mtd-notify:v1|{Group}|{ItemId:N}|{View}|{Action}";
    public static bool TryParse(string? value, out NotificationNavigation? result)
    {
        result = null;
        if (value is null || value.Length > 160) return false;
        var parts = value.Split('|');
        if (parts.Length != 5 || parts[0] != "mtd-notify:v1" || !Guid.TryParseExact(parts[2], "N", out var id)) return false;
        if (parts[1] == "downloads")
        {
            if (parts[3] is not ("downloads" or "history") || parts[4] is not ("open-app" or "open-file" or "open-folder" or "retry")) return false;
        }
        else if (parts[1] == "live")
        {
            if (parts[3] != "live" || parts[4] is not ("open-app" or "stop-save")) return false;
        }
        else if (parts[1] != "application" || id != Guid.Empty || parts[3] != "downloads" || parts[4] != "open-app") return false;
        result = new(parts[1], id, parts[3], parts[4]);
        return true;
    }
}

/// <summary>Application state consumer; owns neither scheduling nor download/recording work.
/// Serial submission, immutable event snapshots and event-driven throttling (no progress timer).</summary>
public sealed class SystemNotificationCoordinator : IAsyncDisposable
{
    private readonly ISystemNotificationService service;
    private readonly Func<NotificationSettings> preferences;
    private readonly LocalizationService text;
    private readonly IAppLogger logger;
    private readonly Func<string?, string?> cachedImage;
    private readonly TimeProvider clock;
    private readonly object sync = new();
    private readonly Dictionary<Guid, DownloadNotificationState> downloads = [];
    private readonly Dictionary<Guid, LiveSession> live = [];
    private readonly HashSet<Guid> round = [];
    private readonly Dictionary<SystemNotificationKey, long> progressTimes = [];
    private readonly HashSet<SystemNotificationKey> progressShown = [];
    private readonly HashSet<SystemNotificationKey> progressPending = [];
    private readonly HashSet<Guid> startedDownloads = [];
    private readonly HashSet<Guid> reconnectingLive = [];
    private Task tail = Task.CompletedTask;
    private bool closing;
    private DownloadQueueService? queueSource;
    private LiveSessionService? liveSource;

    public SystemNotificationCoordinator(ISystemNotificationService service, Func<NotificationSettings> preferences,
        LocalizationService text, IAppLogger logger, Func<string?, string?>? cachedImage = null, TimeProvider? clock = null)
    {
        this.service = service; this.preferences = preferences; this.text = text; this.logger = logger;
        this.cachedImage = cachedImage ?? (_ => null); this.clock = clock ?? TimeProvider.System;
        service.Activated += Activated;
        text.LanguageChanged += LanguageChanged;
    }

    public event EventHandler<NotificationNavigation>? Navigate;
    public SystemNotificationCapabilities Capabilities => service.Capabilities;
    public bool CenterOnlySupported => Capabilities.GetPlatform<WindowsSystemNotificationCapabilities>()?.Supports(WindowsSystemNotificationFeatures.SuppressPopup) == true;
    public Task<SystemNotificationAccess> GetStatusAsync() => service.GetStatusAsync();
    public Task DrainAsync() { lock (sync) return tail; }

    public void Attach(DownloadQueueService queue, LiveSessionService sessions)
    {
        queueSource = queue; liveSource = sessions;
        ObserveDownloads(queue.NotificationSnapshot(), seed: true);
        ObserveLive(sessions.Snapshot(), seed: true);
        queue.Changed += QueueChanged; sessions.Changed += LiveChanged;
    }
    private void QueueChanged(object? sender, QueueChangedEventArgs e) => ObserveDownloads(queueSource!.NotificationSnapshot());
    private void LiveChanged(object? sender, EventArgs e) => ObserveLive(liveSource!.Snapshot());

    public void ObserveDownloads(IReadOnlyList<DownloadNotificationState> items, bool seed = false)
    {
        lock (sync)
        {
            if (closing) return;
            var prefs = preferences();
            foreach (var item in items)
            {
                downloads.TryGetValue(item.Id, out var previous);
                downloads[item.Id] = item;
                if (item.Pending) round.Add(item.Id);
                if (seed) { if (item.Active) startedDownloads.Add(item.Id); continue; }
                var firstActive = item.Active && startedDownloads.Add(item.Id);
                if (item.Status == DownloadStatus.Queued) startedDownloads.Remove(item.Id);
                if (!prefs.Enabled) continue;
                string? eventKey = null;
                if (firstActive && prefs.DownloadStarted) eventKey = "DownloadStarted";
                if (item.RetryDelay > 0 && (previous?.RetryDelay ?? 0) == 0 && prefs.DownloadRetry) eventKey = "DownloadRetry";
                if (previous?.Status != item.Status)
                {
                    eventKey = item.Status switch
                    {
                        DownloadStatus.Completed when prefs.DownloadCompleted => "DownloadCompleted",
                        DownloadStatus.Failed when prefs.DownloadFailed => "DownloadFailed",
                        DownloadStatus.Cancelled when prefs.DownloadCancelled => "DownloadCancelled",
                        _ => eventKey
                    };
                    if (item.Status == DownloadStatus.Cancelled && prefs.DismissOnCancel && Capabilities.Supports(SystemNotificationFeatures.Removal))
                        Enqueue(async () => await service.DismissAsync(Key("downloads", item.Id)));
                }
                var key = Key("downloads", item.Id);
                if (!item.Pending) { progressShown.Remove(key); progressPending.Remove(key); progressTimes.Remove(key); }
                var showProgress = item.Active && prefs.DownloadProgress && Capabilities.Supports(SystemNotificationFeatures.Progress | SystemNotificationFeatures.LiveUpdates | SystemNotificationFeatures.Replacement);
                if (eventKey is not null && !(eventKey == "DownloadStarted" && showProgress))
                    Enqueue(() => ShowDownloadAsync(item, eventKey));
                if (showProgress)
                    Progress(key, () => DownloadProgress(item), () => ShowDownloadAsync(item, "DownloadProgress"));
            }
            foreach (var id in downloads.Keys.Except(items.Select(item => item.Id)).ToArray())
            { downloads.Remove(id); startedDownloads.Remove(id); progressShown.Remove(Key("downloads", id)); progressPending.Remove(Key("downloads", id)); progressTimes.Remove(Key("downloads", id)); }
            if (!seed && round.Count > 0 && !items.Any(item => item.Pending))
            {
                var completed = items.Count(item => round.Contains(item.Id) && item.Status == DownloadStatus.Completed);
                var failed = items.Count(item => round.Contains(item.Id) && item.Status == DownloadStatus.Failed);
                round.Clear();
                if (prefs.Enabled && prefs.QueueCompleted && completed + failed > 0)
                    Enqueue(() => ShowAsync("queue-completed", "application", Guid.Empty, "downloads", "QueueCompleted",
                        () => text.Get("Notification.QueueCounts", completed, failed)));
            }
        }
    }

    public void ObserveLive(IReadOnlyList<LiveSession> sessions, bool seed = false)
    {
        lock (sync)
        {
            if (closing) return;
            var p = preferences();
            foreach (var item in sessions)
            {
                live.TryGetValue(item.Id, out var previous); live[item.Id] = item.Copy();
                var wasReconnecting = reconnectingLive.Contains(item.Id);
                if (item.State == LiveSessionState.Reconnecting) reconnectingLive.Add(item.Id);
                else if (item.State is LiveSessionState.Recording or LiveSessionState.Completed or LiveSessionState.Partial or LiveSessionState.Failed or LiveSessionState.Cancelled)
                    reconnectingLive.Remove(item.Id);
                if (seed || !p.Enabled) continue;
                string? eventKey = null;
                if (previous?.State != item.State)
                    eventKey = item.State switch
                    {
                        LiveSessionState.WaitingForLive when p.LiveWaiting => "LiveWaiting",
                        LiveSessionState.Pending when previous?.State == LiveSessionState.WaitingForLive && p.LiveStarted => "LiveStarted",
                        LiveSessionState.Recording when wasReconnecting && p.LiveReconnected => "LiveReconnected",
                        LiveSessionState.Recording when !wasReconnecting && p.LiveRecording => "LiveRecording",
                        LiveSessionState.Reconnecting when p.LiveLost => "LiveLost",
                        LiveSessionState.Completed when p.LiveCompleted => "LiveCompleted",
                        LiveSessionState.Partial when p.LivePartial => "LivePartial",
                        LiveSessionState.Failed when p.LiveFailed => "LiveFailed",
                        LiveSessionState.Cancelled when p.LiveCancelled => "LiveCancelled",
                        _ => null
                    };
                if (previous is not null && item.Parts.Count > previous.Parts.Count && p.LivePart && item.IsActive) eventKey ??= "LivePart";
                if (eventKey is not null)
                {
                    var copy = item.Copy();
                    Enqueue(() => ShowLiveAsync(copy, eventKey));
                }
                if (item.State == LiveSessionState.Recording && progressShown.Contains(Key("live", item.Id)) &&
                    Capabilities.Supports(SystemNotificationFeatures.LiveUpdates | SystemNotificationFeatures.IndeterminateProgress))
                {
                    var copy = item.Copy();
                    Progress(Key("live", copy.Id), () => LiveProgress(copy), () => Task.CompletedTask);
                }
                if (!item.IsActive) { progressShown.Remove(Key("live", item.Id)); progressTimes.Remove(Key("live", item.Id)); }
                if (previous?.State != item.State && item.State == LiveSessionState.Cancelled && p.DismissOnCancel && Capabilities.Supports(SystemNotificationFeatures.Removal))
                    Enqueue(async () => await service.DismissAsync(Key("live", item.Id)));
            }
            foreach (var id in live.Keys.Except(sessions.Select(item => item.Id)).ToArray())
            {
                live.Remove(id); reconnectingLive.Remove(id);
                var key = Key("live", id);
                progressShown.Remove(key); progressPending.Remove(key); progressTimes.Remove(key);
            }
        }
    }

    private static SystemNotificationKey Key(string group, Guid id) => new((group == "live" ? "live-" : "download-") + id.ToString("N"), group);
    private void Progress(SystemNotificationKey key, Func<SystemNotificationProgress> progress, Func<Task> show)
    {
        var now = clock.GetTimestamp();
        if (progressPending.Contains(key)) return;
        if (progressTimes.TryGetValue(key, out var last) && clock.GetElapsedTime(last, now) < TimeSpan.FromSeconds(1)) return;
        progressTimes[key] = now;
        if (!progressShown.Contains(key))
        {
            progressPending.Add(key);
            Enqueue(async () => { try { await show().ConfigureAwait(false); } finally { lock (sync) progressPending.Remove(key); } });
        }
        else Enqueue(async () => Record(await service.UpdateAsync(key, progress())));
    }
    private SystemNotificationProgress DownloadProgress(DownloadNotificationState item) => new()
    {
        Value = double.IsFinite(item.Percent) ? Math.Clamp(item.Percent / 100d, 0, 1) : 0,
        Status = text["Notification.DownloadProgress"] +
            (item.Speed is { } speed && double.IsFinite(speed) ? $" • {(speed / 1048576d).ToString("N1", text.Culture)} MB/s" : string.Empty) +
            (item.Eta is { } eta ? $" • ETA {eta.ToString(@"hh\:mm\:ss")}" : string.Empty),
        Title = preferences().PrivacyMode ? text["Notification.PrivateMedia"] : item.Title
    };
    private SystemNotificationProgress LiveProgress(LiveSession item) => new()
    {
        Value = null, Status = text["Notification.LiveRecording"],
        Title = preferences().PrivacyMode ? text["Notification.PrivateMedia"] : item.Title,
        ValueText = $"{item.RecordedDuration.ToString(@"hh\:mm\:ss")} • {(item.BytesWritten / 1048576d).ToString("N1", text.Culture)} MB"
    };
    private Task ShowDownloadAsync(DownloadNotificationState item, string eventKey)
    {
        var completed = item.Status == DownloadStatus.Completed;
        var actions = completed ? new[] { "open-file", "open-folder" } : item.Status == DownloadStatus.Failed ? ["retry", "open-app"] : ["open-app"];
        return ShowAsync(Key("downloads", item.Id).Id, "downloads", item.Id, completed ? "history" : "downloads", eventKey,
            () => item.Title, item.Thumbnail, actions, eventKey == "DownloadProgress" ? DownloadProgress(item) : null);
    }
    private Task ShowLiveAsync(LiveSession item, string eventKey) => ShowAsync(Key("live", item.Id).Id,
        "live", item.Id, "live", eventKey, () => item.Title, item.ThumbnailUrl,
        item.State == LiveSessionState.Recording ? ["open-app", "stop-save"] : ["open-app"],
        item.State == LiveSessionState.Recording && Capabilities.Supports(SystemNotificationFeatures.Progress | SystemNotificationFeatures.IndeterminateProgress)
            ? LiveProgress(item) : null);

    private async Task ShowAsync(string id, string group, Guid itemId, string view, string eventKey,
        Func<string>? message = null, string? thumbnail = null, string[]? actions = null, SystemNotificationProgress? progress = null)
    {
        var p = preferences();
        if (!p.Enabled || !Capabilities.IsSupported) return;
        var access = await service.GetStatusAsync().ConfigureAwait(false);
        if (!preferences().Enabled || access.Availability is not (SystemNotificationAvailability.Ready or SystemNotificationAvailability.Unknown)) return;
        p = preferences();
        var intent = new NotificationNavigation(group, itemId, view);
        var windows = Capabilities.GetPlatform<WindowsSystemNotificationCapabilities>();
        // A compact, uncropped media thumbnail, not the role's default wide Hero layout.
        // Limited Windows providers degrade to inline, then text only; portable providers retain the role.
        var placement = windows?.Supports(WindowsSystemNotificationFeatures.AppLogo) == true ? WindowsNotificationImagePlacement.AppLogo
            : windows?.Supports(WindowsSystemNotificationFeatures.InlineImages) == true ? WindowsNotificationImagePlacement.Inline
            : WindowsNotificationImagePlacement.Default;
        var supportsImage = Capabilities.Supports(SystemNotificationFeatures.Images) && (windows is null || placement != WindowsNotificationImagePlacement.Default);
        var images = !p.PrivacyMode && p.ShowThumbnails && !string.IsNullOrWhiteSpace(thumbnail) && supportsImage
            && cachedImage(thumbnail) is { } image ? new[] { new SystemNotificationImage(image, SystemNotificationImageRole.Thumbnail)
            { PlatformOptions = windows is null ? new() : new(new WindowsSystemNotificationImageOptions { Placement = placement }) } } : [];
        logger.Info($"System notification image selection: group={group}, id={id}, thumbnailUrlPresent={!string.IsNullOrWhiteSpace(thumbnail)}, privacy={p.PrivacyMode}, showThumbnails={p.ShowThumbnails}, provider={Capabilities.Backend}, features={Capabilities.Features}, windows={windows?.Features}, images={images.Length}, placement={placement}.");
        foreach (var selected in images)
            logger.Info($"System notification image before Show: role={selected.Role}, placement={placement}, path={selected.Source}, exists={File.Exists(selected.Source)}.");
        var notification = new SystemNotification
        {
            Id = id, Group = group, Title = text["Notification." + eventKey],
            Message = p.PrivacyMode ? text["Notification.PrivateMedia"] : message?.Invoke() ?? text["Notification.TestMessage"],
            ActivationData = intent.Encode(), Images = images,
            Actions = Capabilities.Supports(SystemNotificationFeatures.Actions) ? (actions ?? ["open-app"])
                .Take(Capabilities.Limits.Actions ?? 2).Select(action => new SystemNotificationAction(text["Notification.Action." + action],
                    (intent with { Action = action }).Encode()) { Id = action }).ToArray() : [],
            Progress = Capabilities.Supports(SystemNotificationFeatures.Progress) && progress is not null
                ? progress with { Title = p.PrivacyMode ? text["Notification.PrivateMedia"] : message?.Invoke() ?? string.Empty } : null,
            Urgency = eventKey is "DownloadFailed" or "LiveFailed" or "CriticalError" && Capabilities.Supports(SystemNotificationFeatures.Urgency)
                ? SystemNotificationUrgency.High : SystemNotificationUrgency.Default,
            PlatformOptions = new(new WindowsSystemNotificationOptions
            {
                Silent = p.Sound == NotificationSound.Silent,
                SuppressPopup = p.Presentation == NotificationPresentation.CenterOnly && CenterOnlySupported,
                Language = text.Culture.Name,
                HighPriority = eventKey == "CriticalError" && windows?.Supports(WindowsSystemNotificationFeatures.Priority) == true
            })
        };
        var result = await service.ShowAsync(notification).ConfigureAwait(false); Record(result);
        foreach (var selected in images)
            logger.Info($"System notification image after Show: path={selected.Source}, exists={File.Exists(selected.Source)}; cache retained for Windows.");
        logger.Info($"System notification submitted: group={group}, id={id}, backend={Capabilities.Backend}, status={result.Status}. Acceptance does not prove a visible banner.");
        if (result.IsAccepted && progress is not null && result.Key is not null)
            lock (sync)
            {
                // A terminal state may already be queued while native submission awaits.
                if (group == "downloads" ? downloads.GetValueOrDefault(itemId)?.Active == true : live.GetValueOrDefault(itemId)?.State == LiveSessionState.Recording)
                    progressShown.Add(new(id, group));
            }
    }
    private void Record(SystemNotificationResult result)
    {
        if (!result.IsAccepted) logger.Warning($"System notification status={result.Status} code={result.ErrorCode}; download state is unchanged.");
        foreach (var warning in result.Warnings ?? [])
            logger.Warning($"System notification degraded feature: code={warning.Code}, message={warning.Message}; status={result.Status}.");
    }
    private void Enqueue(Func<Task> operation)
    {
        if (closing) return;
        tail = tail.ContinueWith(async _ =>
        {
            if (!preferences().Enabled) return;
            try { await operation().ConfigureAwait(false); }
            catch (Exception error) { logger.Error("Optional system notification operation failed.", error); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }
    public Task SendTestAsync()
    {
        lock (sync) { Enqueue(() => ShowAsync("settings-test", "application", Guid.Empty, "downloads", "Test")); return tail; }
    }
    public void ReportCriticalError()
    {
        lock (sync) if (preferences().Enabled && preferences().CriticalErrors)
            Enqueue(() => ShowAsync("critical-error", "application", Guid.Empty, "downloads", "CriticalError"));
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        lock (sync)
        {
            if (!preferences().Enabled) return;
            // Language changes only update existing notifications; they neither start
            // progress on a limited backend nor bypass the ordinary event throttle.
            if (preferences().DownloadProgress && Capabilities.Supports(SystemNotificationFeatures.Progress | SystemNotificationFeatures.LiveUpdates | SystemNotificationFeatures.Replacement))
                foreach (var item in downloads.Values.Where(item => item.Active && progressShown.Contains(Key("downloads", item.Id))))
                    Progress(Key("downloads", item.Id), () => DownloadProgress(item), () => Task.CompletedTask);
            if (Capabilities.Supports(SystemNotificationFeatures.Progress | SystemNotificationFeatures.LiveUpdates | SystemNotificationFeatures.IndeterminateProgress))
                foreach (var item in live.Values.Where(item => item.State == LiveSessionState.Recording && progressShown.Contains(Key("live", item.Id))))
                {
                    var copy = item.Copy();
                    Progress(Key("live", copy.Id), () => LiveProgress(copy), () => Task.CompletedTask);
                }
        }
    }
    private void Activated(object? sender, SystemNotificationActivation activation)
    {
        if (!NotificationNavigation.TryParse(activation.ActivationData, out var navigation) || navigation is null) return;
        if (navigation.Group == "application" && activation.NotificationId is not ("settings-test" or "queue-completed" or "critical-error")) return;
        var expectedId = navigation.Group is "downloads" or "live" ? Key(navigation.Group, navigation.ItemId).Id : activation.NotificationId;
        if (activation.NotificationId != expectedId || activation.Group != navigation.Group ||
            activation.ActionId is { Length: > 0 } action && action != navigation.Action) return;
        if (!closing) Navigate?.Invoke(this, navigation);
    }
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (sync) { if (closing) return; closing = true; pending = tail; }
        if (queueSource is not null) queueSource.Changed -= QueueChanged;
        if (liveSource is not null) liveSource.Changed -= LiveChanged;
        text.LanguageChanged -= LanguageChanged; service.Activated -= Activated;
        await pending.ConfigureAwait(false);
        try { await service.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { logger.Error("Optional system notification shutdown failed.", error); }
    }
}
