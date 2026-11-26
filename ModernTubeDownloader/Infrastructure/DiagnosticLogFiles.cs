using System.Text.RegularExpressions;

namespace ModernTubeDownloader.Infrastructure;

internal sealed record DiagnosticLogLimits(long FileBytes = 25L << 20, long TotalBytes = 250L << 20,
    int Days = 14, int NormalCapacity = 1024, int PriorityCapacity = 128);

internal static partial class DiagnosticLogFiles
{
    [GeneratedRegex(@"^MTD-events-v1-[0-9]{8}-[a-f0-9]{32}-[0-9]{6,10}\.log$", RegexOptions.CultureInvariant)]
    private static partial Regex EventName();
    [GeneratedRegex(@"^ModernTubeDownloader-[0-9]{8}\.log$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyName();

    internal static bool IsEventFile(string name) => EventName().IsMatch(name);

    internal static FileInfo[] List(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return [];
            return info.EnumerateFiles("*.log")
                .Where(file => (IsEventFile(file.Name) || LegacyName().IsMatch(file.Name)) &&
                    !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
    }

    internal static int Prune(string directory, DiagnosticLogLimits limits, DateTimeOffset now, long reserve)
    {
        var files = List(directory);
        var total = files.Sum(Length);
        var removed = 0;
        foreach (var file in files.Reverse())
        {
            var length = Length(file);
            if (total <= limits.TotalBytes - reserve && file.LastWriteTimeUtc >= now.UtcDateTime.AddDays(-limits.Days)) continue;
            if (TryDelete(file))
            {
                ++removed;
                total -= length;
            }
        }
        return removed;
    }

    internal static int RemoveOld(string directory, DateTimeOffset now)
    {
        // Keep today's diagnostics and any log that is actively opened by a writer.
        return List(directory).Count(file => file.LastWriteTimeUtc < now.UtcDateTime.Date && TryDelete(file));
    }

    internal static long Length(FileInfo file)
    {
        try { return file.Length; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            if (File.GetAttributes(file.FullName).HasFlag(FileAttributes.ReparsePoint)) return false;
            file.Delete();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
