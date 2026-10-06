namespace ModernTubeDownloader.Services;

/// <summary>Lists profile directory names only; never opens cookie databases or browser data files.</summary>
public static class BrowserProfileDetector
{
    public static IReadOnlyList<string> FindNames(string browser)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = browser.ToLowerInvariant() switch
        {
            "chrome" => Path.Combine(local, "Google", "Chrome", "User Data"),
            "edge" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            "brave" => Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            "chromium" => Path.Combine(local, "Chromium", "User Data"),
            "firefox" => Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"),
            _ => null
        };
        if (root is null) return [];
        try
        {
            if (!Directory.Exists(root)) return [];
            return Directory.EnumerateDirectories(root)
                .Select(Path.GetFileName)
                .Where(name => name is not null && (browser.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
                .Select(name => name!)
                .Take(30)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
