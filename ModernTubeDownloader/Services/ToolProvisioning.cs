using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed record ToolReleaseInfo(
    ExternalToolKind Kind,
    string ReleaseIdentity,
    string DisplayVersion,
    Uri DownloadUri,
    string AssetName,
    string? Sha256,
    long? Size,
    DateTimeOffset? PublishedAt,
    bool IsArchive);

public sealed record ToolDownloadProgress(long DownloadedBytes, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0 ? Math.Clamp(DownloadedBytes * 100d / TotalBytes.Value, 0, 100) : null;
}

public interface IToolReleaseSource
{
    Task<ToolReleaseInfo> GetLatestAsync(ExternalToolKind kind, CancellationToken cancellationToken = default);
}

public sealed class GitHubToolReleaseSource(HttpClient httpClient) : IToolReleaseSource
{
    public async Task<ToolReleaseInfo> GetLatestAsync(ExternalToolKind kind, CancellationToken cancellationToken = default)
    {
        var endpoint = kind switch
        {
            ExternalToolKind.YtDlp => "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest",
            ExternalToolKind.Ffmpeg => "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest",
            ExternalToolKind.Deno => "https://api.github.com/repos/denoland/deno/releases/latest",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var response = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("GitHub release has no tag.");
        var published = root.TryGetProperty("published_at", out var publishedElement) && publishedElement.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
        var desiredName = kind switch
        {
            ExternalToolKind.YtDlp => "yt-dlp.exe",
            ExternalToolKind.Ffmpeg => GetFfmpegAssetName(),
            ExternalToolKind.Deno => GetDenoAssetName(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var asset = assets.FirstOrDefault(item => string.Equals(item.GetProperty("name").GetString(), desiredName, StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"Release {tag} does not contain {desiredName}.");

        var url = asset.GetProperty("browser_download_url").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri))
            throw new InvalidDataException($"Release asset {desiredName} has no valid download URL.");
        var digest = asset.TryGetProperty("digest", out var digestElement) ? NormalizeDigest(digestElement.GetString()) : null;
        digest ??= await TryReadChecksumAsync(kind, desiredName, assets, cancellationToken).ConfigureAwait(false);
        var size = asset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : (long?)null;
        var identity = $"{tag}-{digest ?? published?.UtcDateTime.Ticks.ToString() ?? size?.ToString() ?? "release"}";
        var displayVersion = kind == ExternalToolKind.Ffmpeg && published is { } publishedAt
            ? $"build {publishedAt:yyyy-MM-dd}"
            : tag;
        return new ToolReleaseInfo(kind, identity, displayVersion, downloadUri, desiredName, digest, size, published, kind != ExternalToolKind.YtDlp);
    }

    private async Task<string?> TryReadChecksumAsync(
        ExternalToolKind kind,
        string desiredName,
        IReadOnlyList<JsonElement> assets,
        CancellationToken cancellationToken)
    {
        var checksumName = kind switch
        {
            ExternalToolKind.YtDlp => "SHA2-256SUMS",
            ExternalToolKind.Ffmpeg => "checksums.sha256",
            ExternalToolKind.Deno => $"{desiredName}.sha256sum",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var checksumAsset = assets.FirstOrDefault(item => string.Equals(item.GetProperty("name").GetString(), checksumName, StringComparison.OrdinalIgnoreCase));
        if (checksumAsset.ValueKind == JsonValueKind.Undefined ||
            !Uri.TryCreate(checksumAsset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var checksumUri))
            return null;

        var text = await httpClient.GetStringAsync(checksumUri, cancellationToken).ConfigureAwait(false);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (kind == ExternalToolKind.Deno && parts.Length >= 1 && parts[0].Length == 64)
                return NormalizeDigest(parts[0]);
            if (parts.Length >= 2 && parts[0].Length == 64 && string.Equals(parts[^1].TrimStart('*'), desiredName, StringComparison.OrdinalIgnoreCase))
                return NormalizeDigest(parts[0]);
        }
        return null;
    }

    private static string GetFfmpegAssetName()
    {
        var platform = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win64",
            Architecture.Arm64 => "winarm64",
            Architecture.X86 => "win32",
            _ => throw new PlatformNotSupportedException($"FFmpeg managed provisioning does not support {RuntimeInformation.ProcessArchitecture}.")
        };
        return $"ffmpeg-master-latest-{platform}-gpl.zip";
    }

    private static string GetDenoAssetName()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException($"Deno managed provisioning does not support {RuntimeInformation.ProcessArchitecture} on Windows.")
        };
        return $"deno-{architecture}-pc-windows-msvc.zip";
    }

    private static string? NormalizeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
        normalized = normalized.Trim().ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }
}

public sealed class ToolDownloadService(HttpClient httpClient)
{
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024;

    public async Task<string> DownloadAsync(
        ToolReleaseInfo release,
        string destination,
        IProgress<ToolDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await httpClient.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > MaximumDownloadBytes || release.Size is > MaximumDownloadBytes)
            throw new InvalidDataException($"{release.AssetName} exceeds the managed download size limit.");
        if (release.Size is > 0 && contentLength is > 0 && release.Size != contentLength)
            throw new InvalidDataException($"Unexpected content length for {release.AssetName}.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long downloaded = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            downloaded += read;
            if (downloaded > MaximumDownloadBytes)
                throw new InvalidDataException($"{release.AssetName} exceeds the managed download size limit.");
            progress?.Report(new ToolDownloadProgress(downloaded, contentLength ?? release.Size));
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (contentLength is > 0 && downloaded != contentLength)
            throw new EndOfStreamException($"Download ended after {downloaded} of {contentLength} bytes.");
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (release.Sha256 is { Length: > 0 } expected && !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
            throw new InvalidDataException($"SHA-256 verification failed for {release.AssetName}.");
        return actual;
    }
}

public interface IToolBinaryValidator
{
    Task<string> ValidateAsync(ExternalToolKind kind, string executablePath, string? ffprobePath, CancellationToken cancellationToken = default);
}

public sealed class ProcessToolBinaryValidator(AsyncProcessRunner processRunner) : IToolBinaryValidator
{
    public async Task<string> ValidateAsync(ExternalToolKind kind, string executablePath, string? ffprobePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("Tool executable was not found.", executablePath);
        var arguments = kind == ExternalToolKind.Ffmpeg ? new[] { "-version" } : new[] { "--version" };
        var result = await processRunner.RunAsync(new ProcessRunRequest(executablePath, arguments), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidDataException($"{Path.GetFileName(executablePath)} validation exited with code {result.ExitCode}.");
        if (kind == ExternalToolKind.Ffmpeg)
        {
            if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
                throw new FileNotFoundException("ffprobe.exe was not found next to FFmpeg.", ffprobePath);
            var probeResult = await processRunner.RunAsync(new ProcessRunRequest(ffprobePath, ["-version"]), cancellationToken).ConfigureAwait(false);
            if (!probeResult.IsSuccess) throw new InvalidDataException($"ffprobe validation exited with code {probeResult.ExitCode}.");
        }
        var versionText = result.StandardOutput.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            ?? result.StandardError.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            ?? "unknown";
        if (kind == ExternalToolKind.Deno)
            ValidateDenoVersion(versionText);
        return versionText;
    }

    internal static void ValidateDenoVersion(string versionText)
    {
        var versionValue = versionText.Split(' ', StringSplitOptions.RemoveEmptyEntries).SkipWhile(part => !char.IsDigit(part.FirstOrDefault())).FirstOrDefault();
        if (versionValue is null || !Version.TryParse(versionValue.TrimStart('v'), out var version) || version < new Version(2, 3, 0))
            throw new InvalidDataException($"Deno 2.3.0 or newer is required; detected '{versionText}'.");
    }
}

public sealed record PreparedToolInstallation(
    ExternalToolKind Kind,
    string ReleaseIdentity,
    string Version,
    string ExecutablePath,
    string? FfprobePath,
    string Sha256);

public sealed class ToolPackageInstaller(
    AppPaths paths,
    ToolDownloadService downloader,
    IToolBinaryValidator validator)
{
    private const long MaximumExpandedArchiveBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumArchiveEntries = 100_000;

    public async Task<PreparedToolInstallation> PrepareAsync(
        ToolReleaseInfo release,
        IProgress<ToolDownloadProgress>? progress,
        Action? installing,
        CancellationToken cancellationToken = default)
    {
        var safeIdentity = SanitizeSegment(release.ReleaseIdentity);
        var toolDirectory = Path.Combine(paths.ToolsDirectory, ToolDirectoryName(release.Kind), "versions");
        var finalDirectory = Path.Combine(toolDirectory, safeIdentity);
        if (Directory.Exists(finalDirectory))
        {
            var existing = LocateExecutables(release.Kind, finalDirectory);
            var version = await validator.ValidateAsync(release.Kind, existing.Executable, existing.Ffprobe, cancellationToken).ConfigureAwait(false);
            return new PreparedToolInstallation(release.Kind, release.ReleaseIdentity, version, existing.Executable, existing.Ffprobe, release.Sha256 ?? string.Empty);
        }

        var stagingRoot = Path.Combine(paths.ToolStagingDirectory, $"{ToolDirectoryName(release.Kind)}-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(stagingRoot, "payload");
        var downloadPath = Path.Combine(stagingRoot, release.AssetName);
        Directory.CreateDirectory(payloadDirectory);
        try
        {
            var sha256 = await downloader.DownloadAsync(release, downloadPath, progress, cancellationToken).ConfigureAwait(false);
            installing?.Invoke();
            if (release.IsArchive)
                ExtractZipSafely(downloadPath, payloadDirectory, cancellationToken);
            else if (release.Kind == ExternalToolKind.YtDlp)
                File.Move(downloadPath, Path.Combine(payloadDirectory, "yt-dlp.exe"));
            else
                throw new InvalidDataException($"{release.Kind} must be supplied as an archive.");

            var staged = LocateExecutables(release.Kind, payloadDirectory);
            var version = await validator.ValidateAsync(release.Kind, staged.Executable, staged.Ffprobe, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(toolDirectory);
            Directory.Move(payloadDirectory, finalDirectory);
            var installed = LocateExecutables(release.Kind, finalDirectory);
            return new PreparedToolInstallation(release.Kind, release.ReleaseIdentity, version, installed.Executable, installed.Ffprobe, sha256);
        }
        finally
        {
            TryDeleteStaging(stagingRoot);
        }
    }

    private static (string Executable, string? Ffprobe) LocateExecutables(ExternalToolKind kind, string root)
    {
        if (kind == ExternalToolKind.YtDlp)
        {
            var ytDlp = Directory.EnumerateFiles(root, "yt-dlp.exe", SearchOption.AllDirectories).SingleOrDefault();
            return (ytDlp ?? throw new InvalidDataException("yt-dlp.exe is missing from the prepared package."), null);
        }
        if (kind == ExternalToolKind.Deno)
        {
            var deno = Directory.EnumerateFiles(root, "deno.exe", SearchOption.AllDirectories).SingleOrDefault();
            return (deno ?? throw new InvalidDataException("deno.exe is missing from the prepared package."), null);
        }
        var ffmpeg = Directory.EnumerateFiles(root, "ffmpeg.exe", SearchOption.AllDirectories).SingleOrDefault();
        var ffprobe = Directory.EnumerateFiles(root, "ffprobe.exe", SearchOption.AllDirectories).SingleOrDefault();
        return (ffmpeg ?? throw new InvalidDataException("ffmpeg.exe is missing from the prepared package."),
            ffprobe ?? throw new InvalidDataException("ffprobe.exe is missing from the prepared package."));
    }

    private static void ExtractZipSafely(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long expanded = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidDataException("The FFmpeg archive contains too many entries.");
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancellationSafeLengthCheck(entry.Length, ref expanded);
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The FFmpeg archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }

        static void cancellationSafeLengthCheck(long length, ref long total)
        {
            if (length < 0 || total > MaximumExpandedArchiveBytes - length)
                throw new InvalidDataException("The FFmpeg archive exceeds the extraction size limit.");
            total += length;
        }
    }

    private void TryDeleteStaging(string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);
            var staging = Path.GetFullPath(paths.ToolStagingDirectory) + Path.DirectorySeparatorChar;
            if (full.StartsWith(staging, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
        catch { }
    }

    private static string ToolDirectoryName(ExternalToolKind kind) => kind switch
    {
        ExternalToolKind.YtDlp => "yt-dlp",
        ExternalToolKind.Ffmpeg => "ffmpeg",
        ExternalToolKind.Deno => "deno",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string SanitizeSegment(string value)
    {
        var result = new string(value.Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(result)) result = "release";
        return result.Length <= 120 ? result : result[..120];
    }
}
