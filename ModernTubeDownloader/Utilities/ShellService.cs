using System.Diagnostics;

namespace ModernTubeDownloader.Utilities;

public static class ShellService
{
    public static void OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void OpenFolderForFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
}
