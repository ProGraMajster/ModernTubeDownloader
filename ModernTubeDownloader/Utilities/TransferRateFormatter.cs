using System.Globalization;

namespace ModernTubeDownloader.Utilities;

internal static class TransferRateFormatter
{
    // File-size formatting remains separate. Binary byte rates must not be
    // labelled decimal MB/s or confused with Task Manager's bits per second.
    public static string Format(double bytesPerSecond, CultureInfo? culture = null)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0) return "—";
        string[] units = ["B/s", "KiB/s", "MiB/s", "GiB/s", "TiB/s"];
        var index = 0;
        while (bytesPerSecond >= 1024 && index < units.Length - 1) { bytesPerSecond /= 1024; index++; }
        return bytesPerSecond.ToString("0.#", culture ?? CultureInfo.CurrentCulture) + " " + units[index];
    }
}
