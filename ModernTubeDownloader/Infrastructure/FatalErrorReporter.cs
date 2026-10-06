using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

internal static class FatalErrorReporter
{
    private const uint Ok = 0x00000000;
    private const uint ErrorIcon = 0x00000010;
    private const uint TopMost = 0x00040000;

    public static void Log(AppPaths paths, IAppLogger? logger, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(exception);

        var report = BuildReport(message, exception);
        try
        {
            var directory = Path.Combine(paths.RootDirectory, "CrashReports");
            Directory.CreateDirectory(directory);
            var reportPath = Path.Combine(directory,
                $"ModernTubeDownloader-crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log");
            File.WriteAllText(reportPath, report, new UTF8Encoding(false));
        }
        catch
        {
            // Keep the normal logger and fallback available if the separate report fails.
        }

        if (logger is not null)
        {
            try
            {
                logger.Error(report);
                return;
            }
            catch
            {
                // Fall through to the last-resort file logger.
            }
        }

        try
        {
            Directory.CreateDirectory(paths.LogDirectory);
            var path = Path.Combine(paths.LogDirectory, $"ModernTubeDownloader-{DateTimeOffset.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [FTL] {report}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch
        {
            // No further recovery is possible if even the fallback log cannot be written.
        }
    }

    internal static string BuildReport(string message, Exception exception)
    {
        var assembly = typeof(FatalErrorReporter).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var mfnSha = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ModernFormsNextCommit")?.Value ?? "unknown";
        var raw = $"ModernTubeDownloader fatal crash {DateTimeOffset.Now:O}{Environment.NewLine}" +
            $"App version: {version}; ModernFormsNext SHA: {mfnSha}{Environment.NewLine}" +
            $"OS: {RuntimeInformation.OSDescription}; architecture: {RuntimeInformation.ProcessArchitecture}{Environment.NewLine}" +
            $"{message}{Environment.NewLine}{exception}{Environment.NewLine}" +
            UiCrashDiagnostics.Snapshot();
        var redacted = LogSanitizer.Redact(raw);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(userProfile)
            ? redacted
            : redacted.Replace(userProfile, "<user-profile>", StringComparison.OrdinalIgnoreCase);
    }

    public static void ShowMessage(string title, string message)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            _ = MessageBox(IntPtr.Zero, message, title, Ok | ErrorIcon | TopMost);
        }
        catch
        {
            // The process is already failing; displaying the fallback dialog must not recurse.
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr windowHandle, string text, string caption, uint type);
}
