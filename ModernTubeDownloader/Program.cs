using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var paths = AppPaths.CreateDefault();
        AppServices? services = null;
        IAppLogger? logger = null;

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
            services = AppServices.Create(paths);
            logger = services.Logger;
            Application.Run(new MainForm(services));
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
            services?.Dispose();
        }
    }
}
