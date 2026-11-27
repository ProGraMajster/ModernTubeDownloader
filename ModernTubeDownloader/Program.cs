using System.Diagnostics;
using ModernTubeDownloader.Views;
using ModernFormsNext.WindowKit;
using ModernFormsNext.WindowKit.Backend;
using ModernFormsNext.WindowKit.Threading;
using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Services;
using ModernFormsNext.Notifications;
using ModernFormsNext.WindowKit.Backend.Windows.Notifications;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        UiCrashDiagnostics.RegisterUiThread();
        var paths = AppPaths.CreateDefault();
        PerformanceDiagnostics.Start(paths);
        AppServices? services = null;
        FileAppLogger? startupLog = null;
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

        int Fatal(Exception exception)
        {
            FatalErrorReporter.Log(paths, logger, "The application terminated because of an unexpected exception.", exception);
            var title = services?.Localization["Error.FatalTitle"] ?? "ModernTubeDownloader";
            var message = services?.Localization.Get("Error.FatalMessage", paths.LogDirectory)
                ?? $"An unexpected error stopped ModernTubeDownloader. Technical details were saved in:{Environment.NewLine}{paths.LogDirectory}";
            FatalErrorReporter.ShowMessage(title, message);
            return 1;
        }

        try
        {
            if (args.Length == 1 && args[0] == "--register-protocol")
            {
                ExternalProtocolRegistration.RegisterCurrentExecutable();
                return 0;
            }
            var updaterRestart = Guid.TryParseExact(Environment.GetEnvironmentVariable(UpdaterRequest.RestartEnvironmentKey), "N", out _);
            if (updaterRestart) Environment.SetEnvironmentVariable(UpdaterRequest.RestartEnvironmentKey, null);
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
            var operation = Guid.NewGuid();
            try { UpdatePackageSecurity.VerifyStartupInstallation(AppContext.BaseDirectory); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                paths.EnsureCreated();
                using var recoveryLog = new FileAppLogger(paths.LogDirectory);
                recoveryLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Recover, DiagnosticOutcome.Denied,
                    DiagnosticLevel.Error, OperationId: operation, Exception: error));
                var language = System.Globalization.CultureInfo.CurrentUICulture.Name;
                FatalErrorReporter.ShowMessage("ModernTubeDownloader", new LocalizationService(language)["Startup.Recovery"]);
                return 1;
            }
            if (initialRequest is not null)
            {
                using var preflightLog = new FileAppLogger(paths.LogDirectory);
                var preflightSettings = new SettingsService(paths, preflightLog);
                preflightSettings.LoadAsync().GetAwaiter().GetResult();
                if (!preflightSettings.Current.BrowserIntegrationEnabled) return 3;
            }
            FrameworkBootstrap.EnsureInitialized();
            var dispatcher = Dispatcher.UIThread;
            paths.EnsureCreated();
            var bootLog = startupLog = new FileAppLogger(paths.LogDirectory);
            logger = bootLog;
            var lifetime = new BootstrapLifetime();
            MainForm? form = null;
            var exitCode = 0;
            var pending = new Queue<Action<MainForm>>();
            var readyForActivation = false;
            var queuedActivations = 0;
            var visibleBootstrap = !enableAutomation && !autostart && !notificationLaunch && initialRequest is null;
            var clock = Stopwatch.StartNew();
            using var process = Process.GetCurrentProcess();
            var processStart = process.StartTime.ToUniversalTime();
            bootLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Startup, DiagnosticOutcome.Started, OperationId: operation));
            void Activate(Action<MainForm> action)
            {
                try { if (form is { } target) action(target); }
                finally { Interlocked.Decrement(ref queuedActivations); }
            }

            Task Route(Action<MainForm> action)
            {
                if (Interlocked.Increment(ref queuedActivations) > 32)
                {
                    Interlocked.Decrement(ref queuedActivations);
                    bootLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Startup, DiagnosticOutcome.Denied,
                        OperationId: operation, Counts: new(Items: 32)));
                    return Task.FromException(new IOException("The application startup activation queue is full."));
                }
                dispatcher.Post(() =>
                {
                    if (readyForActivation) Activate(action);
                    else pending.Enqueue(action);
                });
                return Task.CompletedTask;
            }
            instance.Start(request => Route(window => _ = window.HandleExternalUrlAsync(request)), bootLog,
                request => Route(window => window.HandleNotification(request)), () => Route(window => window.RestoreWindow()));

            async Task StartAsync()
            {
                StartupUpdateForm? window = null;
                void Report(StartupPhase phase)
                {
                    bootLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Startup, DiagnosticOutcome.Progress,
                        OperationId: operation, DurationMs: clock.ElapsedMilliseconds, Counts: new(Code: (int)phase)));
                    PerformanceDiagnostics.Emit("startup." + phase);
                    if (window is not null) dispatcher.Post(() => window.Report(phase));
                }

                try
                {
                    if (visibleBootstrap)
                    {
                        window = new StartupUpdateForm(new LocalizationService(System.Globalization.CultureInfo.CurrentUICulture.Name));
                        window.Show();
                        if (await Task.WhenAny(window.FirstPaint, Task.Delay(300)) == window.FirstPaint)
                        {
                            PerformanceDiagnostics.Emit("ui.startup-first-paint");
                            bootLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Dispatcher, DiagnosticOutcome.Completed,
                                OperationId: operation, DurationMs: (long)(DateTime.UtcNow - processStart).TotalMilliseconds,
                                Counts: new(Code: (int)StartupPhase.Initialize)));
                        }
                    }
                    notificationService = RegisterNotifications(paths);
                    services = await Task.Run(() => AppServices.CreateCore(paths, null, null, null, notificationService, true,
                        AppBranding.Version, bootLog, operation, Report));
                    if (updaterRestart) services.Updates.RecordUpdaterRestart();
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
                        { exitCode = 3; lifetime.Exit(); return; }
                    var configureProtocol = OperatingSystem.IsWindows();
        #if DEBUG
                    // Native QA uses an isolated data profile, never the owner's URL association.
                    if (enableAutomation)
                        configureProtocol = false;
        #endif
                    // Explicit isolated QA never changes the owner's registered protocol, including Release QA.
                    if (IsolatedQaProfile.SuppressSystemRegistration(paths))
                        configureProtocol = false;
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
                    Report(StartupPhase.ConstructUi);
                    form = new MainForm(services, enableAutomation, initialRequest, classicUi, autostart || notificationLaunch, updaterRestart);
                    form.Closed += (_, _) => lifetime.Exit();
                    form.Shown += (_, _) => bootLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Controls, DiagnosticOutcome.Completed,
                        OperationId: operation, DurationMs: (long)(DateTime.UtcNow - processStart).TotalMilliseconds));
                    services.Diagnostics.Snapshot();
                    if (services.Notifications is { } notifications)
                        notifications.Navigate += (_, request) => _ = Route(target => target.HandleNotification(request));

                    var runtime = form.StartRuntimeAsync(autostart, hiddenArgument, window, operation);
                    readyForActivation = true;
                    while (pending.TryDequeue(out var action)) Activate(action);
                    await runtime;
                }
                catch (Exception error)
                {
                    window?.Finish();
                    bootLog.Event(new(DiagnosticModule.Application, DiagnosticStage.Startup, DiagnosticOutcome.Failed,
                        DiagnosticLevel.Error, operation, DurationMs: clock.ElapsedMilliseconds, Exception: error));
                    exitCode = Fatal(error);
                    lifetime.Exit();
                }
                finally { window?.Dispose(); }
            }

            dispatcher.Post(async () => await StartAsync());
            try { Application.Run(lifetime); }
            finally { form?.Dispose(); }
            return exitCode;
        }
        catch (Exception error) { return Fatal(error); }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= domainHandler;
            TaskScheduler.UnobservedTaskException -= taskHandler;
            instance?.Dispose();
            services?.Dispose();
            startupLog?.Dispose();
            PerformanceDiagnostics.Stop();
        }
    }

    private sealed class BootstrapLifetime : ICloseable
    {
        public event EventHandler? Closed;
        internal void Exit() => Closed?.Invoke(this, EventArgs.Empty);
    }

    private static ISystemNotificationService? RegisterNotifications(AppPaths paths)
    {
        // Toast/COM registration is per Windows user, not per data root. The
        // isolated QA profile must not register its copied EXE as the real app.
        if (IsolatedQaProfile.SuppressSystemRegistration(paths)) return null;
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
            using var notificationLog = new FileAppLogger(paths.LogDirectory);
            notificationLog.Event(new(DiagnosticModule.Notifications, DiagnosticStage.Initialize, DiagnosticOutcome.Unavailable, DiagnosticLevel.Warn, Exception: error));
            return null;
        }
    }
}
