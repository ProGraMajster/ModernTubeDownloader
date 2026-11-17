namespace ModernTubeDownloader.Infrastructure;

/// <summary>Explicit, narrowly scoped QA opt-out from persistent per-user OS registration.</summary>
internal static class IsolatedQaProfile
{
    private const string Prefix = "mtd-automation-smoke-";

    internal static bool SuppressSystemRegistration(AppPaths paths) => IsEligible(
        paths.RootDirectory,
        Environment.GetEnvironmentVariable("MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL"),
        Path.GetTempPath(),
        File.GetAttributes);

    internal static bool SuppressCurrentSystemRegistration() => SuppressSystemRegistration(AppPaths.CreateDefault());

    internal static void DemandSystemRegistrationAllowed(Func<bool> suppress)
    {
        if (suppress())
            throw new InvalidOperationException("Per-user system registration changes are disabled for an isolated QA profile.");
    }

    // The injected attribute reader keeps policy tests independent of the owner's
    // filesystem. Production uses the real attributes, including all ancestors.
    internal static bool IsEligible(string root, string? requested, string temporaryDirectory,
        Func<string, FileAttributes> attributes)
    {
        if (requested != "1") return false;
        try
        {
            if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(temporaryDirectory)) return false;
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var fullTemp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
            var name = Path.GetFileName(fullRoot);
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length == Prefix.Length ||
                !string.Equals(Path.GetDirectoryName(fullRoot), fullTemp, StringComparison.OrdinalIgnoreCase)) return false;

            // A lexical temp child must not resolve through a junction/symlink to
            // another profile. Missing/inaccessible paths also fail closed.
            for (string? current = fullRoot; current is not null; current = Path.GetDirectoryName(current))
            {
                var flags = attributes(current);
                if ((flags & FileAttributes.Directory) == 0 || (flags & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
