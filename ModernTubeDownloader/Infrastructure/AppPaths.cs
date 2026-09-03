namespace ModernTubeDownloader.Infrastructure;

public sealed record AppPaths(
    string RootDirectory,
    string SettingsFile,
    string QueueFile,
    string HistoryFile,
    string MetadataDirectory,
    string ThumbnailCacheDirectory,
    string LogDirectory,
    string ToolsDirectory,
    string ToolStateFile,
    string ToolStagingDirectory)
{
    public static AppPaths CreateDefault()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("MODERNTUBEDOWNLOADER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return Create(configuredRoot);

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModernTubeDownloader");
        return Create(root);
    }

    public static AppPaths Create(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        return new AppPaths(
            root,
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "queue.json"),
            Path.Combine(root, "history.json"),
            Path.Combine(root, "metadata"),
            Path.Combine(root, "thumbnails"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "Tools"),
            Path.Combine(root, "Tools", "state.json"),
            Path.Combine(root, "Tools", ".staging"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(MetadataDirectory);
        Directory.CreateDirectory(ThumbnailCacheDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(ToolStagingDirectory);
    }
}
