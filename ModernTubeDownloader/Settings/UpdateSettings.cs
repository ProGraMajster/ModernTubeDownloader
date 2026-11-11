using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernTubeDownloader.Settings;

public enum UpdateCheckFrequency { StartupDaily, Daily, Weekly }
public enum UpdateInstallMode { NotifyOnly, AutoDownload, InstallOnExit }
public sealed class UpdateSettings
{
    public bool AutomaticChecks { get; set; } = true;
    public UpdateCheckFrequency Frequency { get; set; } = UpdateCheckFrequency.StartupDaily;
    public UpdateInstallMode Mode { get; set; } = UpdateInstallMode.InstallOnExit;
    /// <summary>Zero means legacy/unconfirmed policy; provenance is determined before migration.</summary>
    public int PolicyVersion { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
