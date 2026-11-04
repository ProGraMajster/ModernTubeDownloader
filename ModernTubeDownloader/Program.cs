using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernFormsNext.Notifications;
using ModernFormsNext.WindowKit.Backend.Windows.Notifications;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        UiCrashDiagnostics.RegisterUiThread();
        var paths = AppPaths.CreateDefault();
        AppServices? services = null;
        IAppLogger? logger = null;
        ExternalLinkInstance? instance = null;
        ISystemNotificationService? notificationService = null;

        UnhandledExceptionEventHandler domainHandler = (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                FatalErrorReporter.Log(paths, logger, "An unhandled application-domain exception occurred.", exception);
        };
        EventHandler<UnobservedTaskExceptionEventArgs> taskHandler = (_, eventArgs) =>
        {
            FatalErrorReporter.Log(paths, logger, "An unobserved task exception occurred.", eventArgs.Exception);
            services?.Notifications?.ReportCriticalError();
            eventArgs.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += domainHandler;
        TaskScheduler.UnobservedTaskException += taskHandler;

        try
        {
            if (args.Length == 1 && args[0] == "--register-protocol")
            {
                ExternalProtocolRegistration.RegisterCurrentExecutable();
                return 0;
            }
            var classicUi = args.Contains("--classic-ui", StringComparer.Ordinal);
            args = args.Where(arg => arg != "--classic-ui").ToArray();
            var enableAutomation = args.Length == 1 && args[0] == "--automation";
            var autostart = args.Length is 1 or 2 && args[0] == "--autostart" &&
                (args.Length == 1 || args[1] == "--start-hidden");
            var hiddenArgument = autostart && args.Length == 2;
            // Windows App SDK COM startup markers carry no application command/payload.
            // Register lets the official provider decode the copied native activation instead.
            var notificationLaunch = args.Length is 1 or 2 && args[0] == "----AppNotificationActivated:" &&
                (args.Length == 1 || args[1].Equals("-Embedding", StringComparison.OrdinalIgnoreCase));
            ExternalMediaRequest? initialRequest = null;
            if (args.Length == 1 && !enableAutomation && !notificationLaunch && !autostart &&
                !ExternalMediaRequest.TryParseProtocol(args[0], out initialRequest))
                return 2;
            if (args.Length > 1 && !notificationLaunch && !autostart) return 2;
#if !DEBUG
            if (enableAutomation)
                throw new InvalidOperationException("UI automation is available only in Debug builds.");
#endif
            instance = new ExternalLinkInstance();
            if (!instance.IsPrimary)
            {
                if (notificationLaunch)
                {
                    notificationService = RegisterNotifications(paths);
                    if (notificationService is null) return 1;
                    using var relayLog = new FileAppLogger(paths.LogDirectory);
                    var relay = new NotificationActivationRelay(notificationService, instance, relayLog);
                    Application.Run(relay); // No Form, AppServices, tool setup, scheduler or worker in this process.
                    return relay.Forwarded ? 0 : 1;
                }
                if (initialRequest is null) return autostart ? 0 : instance.ForwardOpenWindowAsync().GetAwaiter().GetResult() ? 0 : 1;
                return instance.ForwardAsync(args[0]).GetAwaiter().GetResult() ? 0 : 1;
            }
            if (initialRequest is not null)
            {
                using var preflightLog = new FileAppLogger(paths.LogDirectory);
                var preflightSettings = new SettingsService(paths, preflightLog);
                preflightSettings.LoadAsync().GetAwaiter().GetResult();
                if (!preflightSettings.Current.BrowserIntegrationEnabled) return 3;
            }
            notificationService = RegisterNotifications(paths);
            services = AppServices.Create(paths, notifications: notificationService);
            logger = services.Logger;
            logger.Info($"Application version={typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion}; Windows={Environment.OSVersion.Version}; runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; process64={Environment.Is64BitProcess}.");
            var diagnosticServices = services;
            UiCrashDiagnostics.RegisterStateProvider(_ =>
            {
                var items = diagnosticServices.Queue.Snapshot();
                return $"Queue: total={items.Count}, queued={items.Count(item => item.Status == Models.DownloadStatus.Queued)}, " +
                    $"active={items.Count(item => item.Status is Models.DownloadStatus.Waiting or Models.DownloadStatus.DownloadingVideo or Models.DownloadStatus.DownloadingAudio or Models.DownloadStatus.Merging or Models.DownloadStatus.Finalizing)}, " +
                    $"paused={diagnosticServices.Queue.IsPaused}; Web Remote: enabled={diagnosticServices.Settings.Current.EnableWebRemote}, " +
                    $"running={diagnosticServices.WebRemote.IsRunning}";
            });
            services.Queue.Changed += (_, change) =>
            {
                if (change.Kind is Models.QueueChangeKind.Collection or Models.QueueChangeKind.Order or Models.QueueChangeKind.Pause)
                    UiCrashDiagnostics.Record($"queue.{change.Kind}");
            };
            services.Settings.Changed += (_, _) => UiCrashDiagnostics.Record("settings.changed");
            services.WebRemote.Changed += (_, _) => UiCrashDiagnostics.Record("web-remote.changed");
            if (initialRequest is not null && !services.Settings.Current.BrowserIntegrationEnabled)
                return 3;
            var configureProtocol = OperatingSystem.IsWindows();
#if DEBUG
            // Native QA uses an isolated data profile, never the owner's URL association.
            if (enableAutomation || Environment.GetEnvironmentVariable("MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL") == "1")
                configureProtocol = false;
#endif
            if (configureProtocol)
            {
                try
                {
                    if (services.Settings.Current.BrowserIntegrationEnabled)
                        ExternalProtocolRegistration.RegisterCurrentExecutable();
                    else
                        ExternalProtocolRegistration.UnregisterCurrentUser();
                }
                catch (Exception error)
                {
                    logger.Error("Browser protocol integration could not be configured.", error);
                }
            }
            var form = new MainForm(services, enableAutomation, initialRequest, classicUi);
            if (services.Notifications is { } notifications)
                notifications.Navigate += (_, request) => Application.RunOnUIThread(() => form.HandleNotification(request));
            instance.Start(request =>
            {
                Application.RunOnUIThread(() => _ = form.HandleExternalUrlAsync(request));
                return Task.CompletedTask;
            }, logger, request =>
            {
                Application.RunOnUIThread(() => form.HandleNotification(request));
                return Task.CompletedTask;
            }, () =>
            {
                Application.RunOnUIThread(form.RestoreWindow);
                return Task.CompletedTask;
            });
            ModernFormsNext.WindowKit.Threading.Dispatcher.UIThread.Post(() => form.StartRuntime(autostart, hiddenArgument));
            Application.Run((ModernFormsNext.WindowKit.ICloseable)form);
            return 0;
        }
        catch (Exception exception)
        {
            FatalErrorReporter.Log(paths, logger, "The application terminated because of an unexpected exception.", exception);
            var title = services?.Localization["Error.FatalTitle"] ?? "ModernTubeDownloader";
            var message = services?.Localization.Get("Error.FatalMessage", paths.LogDirectory)
                ?? $"An unexpected error stopped ModernTubeDownloader. Technical details were saved in:{Environment.NewLine}{paths.LogDirectory}";
            FatalErrorReporter.ShowMessage(title, message);
            return 1;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= domainHandler;
            TaskScheduler.UnobservedTaskException -= taskHandler;
            instance?.Dispose();
            services?.Dispose();
        }
    }

    private static ISystemNotificationService? RegisterNotifications(AppPaths paths)
    {
        try
        {
            paths.EnsureCreated();
            return WindowsSystemNotificationService.Register(new()
            {
                Backend = WindowsNotificationBackendKind.Automatic,
                AppUserModelId = "ProGraMajster.ModernTubeDownloader",
                DisplayName = "ModernTubeDownloader",
                DisplayIcon = new Uri(AppBranding.ExportNotificationIcon(paths.RootDirectory))
            });
        }
        catch (Exception error)
        {
            // Last-resort optional feature boundary, including an inaccessible icon
            // location. Automatic provider fallback handles known SDK load failures.
            FatalErrorReporter.Log(paths, null, "Optional notification registration failed; application startup will continue.", error);
            return null;
        }
    }
}
