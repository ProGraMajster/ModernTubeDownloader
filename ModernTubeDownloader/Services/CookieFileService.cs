using System.Text;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Services;

public enum CookieFileError { Missing, Unreadable, UnsupportedFormat, Rejected }

public sealed class CookieFileException(CookieFileError reason) : Exception(reason.ToString())
{
    public CookieFileError Reason { get; } = reason;
}

public static class CookieFileService
{
    private const long MaximumBytes = 32L * 1024 * 1024;

    public static string Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new CookieFileException(CookieFileError.Missing);
        LogSanitizer.RegisterCookieFilePath(path);
        string fullPath;
        try { fullPath = Path.GetFullPath(path.Trim()); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CookieFileException(CookieFileError.Missing);
        }
        LogSanitizer.RegisterCookieFilePath(fullPath);
        if (!File.Exists(fullPath))
            throw new CookieFileException(Directory.Exists(fullPath)
                ? CookieFileError.UnsupportedFormat : CookieFileError.Missing);
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 512, FileOptions.SequentialScan);
            if (stream.Length is 0 or > MaximumBytes)
                throw new CookieFileException(CookieFileError.UnsupportedFormat);
            Span<byte> headerBytes = stackalloc byte[256];
            var count = stream.Read(headerBytes);
            var lineEnd = headerBytes[..count].IndexOf((byte)'\n');
            if (lineEnd < 0) lineEnd = count;
            var header = Encoding.UTF8.GetString(headerBytes[..lineEnd]).TrimStart('\uFEFF').TrimEnd('\r');
            if (header is not ("# Netscape HTTP Cookie File" or "# HTTP Cookie File"))
                throw new CookieFileException(CookieFileError.UnsupportedFormat);
            return fullPath;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            throw new CookieFileException(CookieFileError.Unreadable);
        }
    }

    public static void AppendCookieArguments(List<string> arguments, bool enabled, string? path)
    {
        if (enabled)
            arguments.AddRange(["--cookies", Validate(path)]);
    }
}
