using ModernFormsNext;
using ModernFormsNext.Notifications;
using ModernFormsNext.WindowKit;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>Secondary COM activation receiver using the existing per-user IPC and MFN UI dispatcher.
/// A closeable lifetime root avoids creating a second main window or any download workers.</summary>
internal sealed class NotificationActivationRelay : ICloseable
{
    private readonly SystemNotificationCoordinator coordinator;
    private readonly ExternalLinkInstance instance;
    private readonly IAppLogger logger;
    private readonly CancellationTokenSource expiry = new();
    private bool completing;
    public bool Forwarded { get; private set; }
    public event EventHandler? Closed;

    public NotificationActivationRelay(ISystemNotificationService service, ExternalLinkInstance instance, IAppLogger logger)
    {
        this.instance = instance; this.logger = logger;
        coordinator = new(service, () => new NotificationSettings(), new LocalizationService("en"), logger);
        coordinator.Navigate += async (_, navigation) => await CompleteAsync(navigation);
        _ = ExpireAsync();
    }
    private async Task ExpireAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(8), expiry.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        Application.RunOnUIThread(() => _ = CompleteAsync(null));
    }
    private async Task CompleteAsync(NotificationNavigation? navigation)
    {
        if (completing) return;
        completing = true;
        expiry.Cancel();
        try
        {
            if (navigation is not null) Forwarded = await instance.ForwardNotificationAsync(navigation);
            if (!Forwarded) logger.Warning("Notification activation was not forwarded; no secondary workers were started.");
        }
        catch (Exception error) { logger.Error("Notification activation forwarding failed.", error); }
        finally { await coordinator.DisposeAsync(); expiry.Dispose(); Closed?.Invoke(this, EventArgs.Empty); }
    }
}
