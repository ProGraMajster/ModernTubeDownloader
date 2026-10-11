using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Services;

public enum TrayCloseDecision { Hide, Confirm, Exit }

public static class TrayPolicy
{
    public static TrayCloseDecision Close(StartupSettings settings, bool available, bool activeWork, bool explicitExit)
    {
        if (!explicitExit && available && settings.CloseBehavior == TrayCloseBehavior.HideToTray)
            return TrayCloseDecision.Hide;
        if (activeWork || !explicitExit && settings.CloseBehavior == TrayCloseBehavior.Ask)
            return TrayCloseDecision.Confirm;
        return TrayCloseDecision.Exit;
    }

    public static bool StartHidden(StartupSettings settings, bool available, bool autostart, bool hiddenArgument)
        => available && settings.ShowTrayIcon && (hiddenArgument || autostart && settings.AutostartInTray);
}
