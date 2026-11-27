using System.Runtime.InteropServices;

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

        // Write synchronously here so a failing process does not depend on its queue draining.
        FileAppLogger.Emergency(paths.LogDirectory, exception);
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
