using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

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

        UnhandledExceptionEventHandler domainHandler = (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                FatalErrorReporter.Log(paths, logger, "An unhandled application-domain exception occurred.", exception);
        };
        EventHandler<UnobservedTaskExceptionEventArgs> taskHandler = (_, eventArgs) =>
        {
            FatalErrorReporter.Log(paths, logger, "An unobserved task exception occurred.", eventArgs.Exception);
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
            var enableAutomation = args.Length == 1 && args[0] == "--automation";
            ExternalMediaRequest? initialRequest = null;
            if (args.Length == 1 && !enableAutomation &&
                !ExternalMediaRequest.TryParseProtocol(args[0], out initialRequest))
                return 2;
            if (args.Length > 1) return 2;
#if !DEBUG
            if (enableAutomation)
                throw new InvalidOperationException("UI automation is available only in Debug builds.");
#endif
            instance = new ExternalLinkInstance();
            if (!instance.IsPrimary)
            {
                if (initialRequest is null) return 0;
                return instance.ForwardAsync(args[0]).GetAwaiter().GetResult() ? 0 : 1;
            }
            services = AppServices.Create(paths);
            logger = services.Logger;
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
            if (OperatingSystem.IsWindows())
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
            var form = new MainForm(services, enableAutomation, initialRequest);
            instance.Start(request =>
            {
                Application.RunOnUIThread(() => _ = form.HandleExternalUrlAsync(request));
                return Task.CompletedTask;
            }, logger);
            Application.Run(form);
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
}
