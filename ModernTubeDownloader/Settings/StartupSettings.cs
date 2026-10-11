namespace ModernTubeDownloader.Settings;

public enum TrayMinimizeBehavior { Normal, HideToTray }
public enum TrayCloseBehavior { Exit, HideToTray, Ask }

public sealed class StartupSettings
{
    public bool ShowTrayIcon { get; set; } = true;
    public TrayMinimizeBehavior MinimizeBehavior { get; set; }
    public TrayCloseBehavior CloseBehavior { get; set; }
    public bool FirstHideNotice { get; set; } = true;
    public bool FirstHideNoticeShown { get; set; }
    public bool StartWithWindows { get; set; }
    public bool AutostartInTray { get; set; } = true;
}
