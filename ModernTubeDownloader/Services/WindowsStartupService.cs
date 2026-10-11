using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace ModernTubeDownloader.Services;

public interface IStartupRegistration
{
    string? Read();
    void Write(string command);
    void Delete();
}

/// <summary>Only this application's value in the current user's Run key.</summary>
public sealed class CurrentUserStartupRegistration : IStartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ProGraMajster.ModernTubeDownloader";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }
    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }
    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        key?.DeleteValue(ValueName, false);
    }
}

public enum StartupRegistrationStatus { Disabled, Current, NeedsRepair, Foreign }

public sealed partial class WindowsStartupService(IStartupRegistration registration, string executable)
{
    public static string BuildCommand(string executable, bool hidden)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Any(c => c == '"' || char.IsControl(c)) ||
            !Path.GetFileName(executable).Equals("ModernTubeDownloader.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute application executable is required.", nameof(executable));
        var command = $"\"{Path.GetFullPath(executable)}\" --autostart" + (hidden ? " --start-hidden" : "");
        if (command.Length > 260) throw new ArgumentException("The Windows Run command exceeds 260 characters.", nameof(executable));
        return command;
    }

    public StartupRegistrationStatus Inspect(bool hidden)
    {
        var current = registration.Read();
        if (current is null) return StartupRegistrationStatus.Disabled;
        if (!OwnedCommand().IsMatch(current)) return StartupRegistrationStatus.Foreign;
        return current.Equals(BuildCommand(executable, hidden), StringComparison.OrdinalIgnoreCase)
            ? StartupRegistrationStatus.Current : StartupRegistrationStatus.NeedsRepair;
    }

    public void Apply(bool enabled, bool hidden)
    {
        var status = Inspect(hidden);
        if (status == StartupRegistrationStatus.Foreign)
            throw new InvalidOperationException("The startup value does not belong to ModernTubeDownloader; it was left unchanged.");
        if (enabled) registration.Write(BuildCommand(executable, hidden));
        else if (status != StartupRegistrationStatus.Disabled) registration.Delete();
    }

    [GeneratedRegex("^\"[^\"\\r\\n]+[\\\\/]ModernTubeDownloader\\.exe\" --autostart(?: --start-hidden)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnedCommand();
}
