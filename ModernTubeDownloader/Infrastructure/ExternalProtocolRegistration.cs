using Microsoft.Win32;

namespace ModernTubeDownloader.Infrastructure;

public static class ExternalProtocolRegistration
{
    private const string OwnerValue = "ModernTubeDownloader.PerUser";
    public enum RegistrationStatus { Registered, NeedsRepair }

    /// <summary>Registers the current executable under HKCU; no elevation is required.</summary>
    public static void RegisterCurrentExecutable()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var executable = GetCurrentExecutable();
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true)
            ?? throw new IOException("Cannot open per-user protocol classes.");
        EnsureRegistered(classes, executable);
    }

    public static string GetCurrentExecutable()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !Path.GetFileName(executable).Equals("ModernTubeDownloader.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The application must run from ModernTubeDownloader.exe to register its protocol.");
        return executable;
    }

    public static RegistrationStatus GetStatus(string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
        return classes is null ? RegistrationStatus.NeedsRepair : GetStatus(classes, executable);
    }

    public static RegistrationStatus GetStatus(RegistryKey classes, string executable)
    {
        using var root = classes.OpenSubKey(ExternalMediaRequest.Scheme);
        using var command = root?.OpenSubKey(@"shell\open\command");
        using var icon = root?.OpenSubKey("DefaultIcon");
        return root?.GetValue(null) as string == "URL:ModernTubeDownloader Link" &&
            root.GetValue("MTD Owner") as string == OwnerValue &&
            root.GetValue("URL Protocol") is string &&
            icon?.GetValue(null) as string == $"\"{executable}\",0" &&
            command?.GetValue(null) as string == BuildOpenCommand(executable)
            ? RegistrationStatus.Registered : RegistrationStatus.NeedsRepair;
    }

    /// <returns>True only when a missing or stale registration was written.</returns>
    public static bool EnsureRegistered(RegistryKey classes, string executable)
    {
        if (GetStatus(classes, executable) == RegistrationStatus.Registered) return false;
        using (var existing = classes.OpenSubKey(ExternalMediaRequest.Scheme))
        {
            if (existing is not null &&
                (existing.GetValue("MTD Owner") is string otherOwner && otherOwner != OwnerValue ||
                 existing.GetValue("MTD Owner") is null && existing.GetValue(null) as string != "URL:ModernTubeDownloader Link"))
                throw new IOException("The protocol is managed by another installer or application.");
        }
        using var root = classes.CreateSubKey(ExternalMediaRequest.Scheme, true)
            ?? throw new IOException("Cannot create the per-user protocol key.");
        root.SetValue(null, "URL:ModernTubeDownloader Link", RegistryValueKind.String);
        root.SetValue("MTD Owner", OwnerValue, RegistryValueKind.String);
        root.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
        using var icon = root.CreateSubKey("DefaultIcon", true);
        icon?.SetValue(null, $"\"{executable}\",0", RegistryValueKind.String);
        using var command = root.CreateSubKey(@"shell\open\command", true)
            ?? throw new IOException("Cannot create the protocol command key.");
        command.SetValue(null, BuildOpenCommand(executable), RegistryValueKind.String);
        return true;
    }

    public static bool UnregisterCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
        return classes is not null && Unregister(classes);
    }

    public static bool Unregister(RegistryKey classes)
    {
        using (var root = classes.OpenSubKey(ExternalMediaRequest.Scheme))
        {
            // Never remove an association installed by another application/installer.
            if (root?.GetValue("MTD Owner") as string != OwnerValue) return false;
        }
        classes.DeleteSubKeyTree(ExternalMediaRequest.Scheme, throwOnMissingSubKey: false);
        return true;
    }

    public static string BuildOpenCommand(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (executable.Contains('"') || executable.Any(char.IsControl) || !Path.IsPathFullyQualified(executable) ||
            !Path.GetFileName(executable).Equals("ModernTubeDownloader.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a full ModernTubeDownloader.exe path.", nameof(executable));
        return $"\"{executable}\" \"%1\"";
    }
}
