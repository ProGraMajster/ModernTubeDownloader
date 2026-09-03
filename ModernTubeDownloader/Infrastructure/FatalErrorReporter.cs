using System.Runtime.InteropServices;
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

        if (logger is not null)
        {
            try
            {
                logger.Error(message, exception);
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
            var entry = $"{DateTimeOffset.Now:O} [FTL] {message}{Environment.NewLine}{exception}{Environment.NewLine}";
            File.AppendAllText(path, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // No further recovery is possible if even the fallback log cannot be written.
        }
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
