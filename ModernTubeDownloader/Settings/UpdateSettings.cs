namespace ModernTubeDownloader.Settings;

public enum UpdateCheckFrequency { StartupDaily, Daily, Weekly }
public enum UpdateInstallMode { NotifyOnly, AutoDownload, InstallOnExit }
public sealed class UpdateSettings
{
    public bool AutomaticChecks { get; set; } = true;
    public UpdateCheckFrequency Frequency { get; set; }
    public UpdateInstallMode Mode { get; set; }
}
