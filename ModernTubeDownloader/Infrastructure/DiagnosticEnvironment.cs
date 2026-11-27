using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ModernTubeDownloader.Infrastructure;

internal sealed record DiagnosticMonitor(int Width, int Height, uint? Dpi);
internal sealed record DiagnosticDrive(string Role, DriveType? Type, string State, string? FileSystem = null, long? FreeBytes = null);

internal static class DiagnosticHardware
{
    internal static object Read() => new
    {
        name = OperatingSystem.IsWindows() ? "Windows" : "Other",
        version = Environment.OSVersion.Version.ToString(),
        description = SafeModel(RuntimeInformation.OSDescription),
        architecture = RuntimeInformation.OSArchitecture.ToString(),
        language = CultureInfo.InstalledUICulture.Name,
        culture = CultureInfo.CurrentCulture.Name,
        elevated = Query(() =>
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return (bool?)new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }),
        cpu = Query(() => SafeModel(Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString", null) as string)),
        logicalProcessors = Environment.ProcessorCount,
        physicalCores = Query(ReadCores),
        memory = Query(ReadMemory),
        graphics = Query(ReadGraphics),
        monitors = Query(ReadMonitors)
    };

    internal static DiagnosticDrive ReadDrive(string role, string path) =>
        Query(() =>
        {
            if (!Path.IsPathFullyQualified(path)) return new DiagnosticDrive(role, null, "Unconfigured");
            var drive = new DriveInfo(Path.GetPathRoot(path)!);
            if (drive.DriveType == DriveType.Network) return new DiagnosticDrive(role, DriveType.Network, "Network");
            return drive.IsReady
                ? new DiagnosticDrive(role, drive.DriveType, "Accessible", SafeModel(drive.DriveFormat), drive.AvailableFreeSpace)
                : new DiagnosticDrive(role, drive.DriveType, "Unavailable");
        }) ?? new(role, null, "Unavailable");

    private static T? Query<T>(Func<T?> action)
    {
        try { return action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or
            ArgumentException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or
            System.ComponentModel.Win32Exception) { return default; }
    }

    private static string? SafeModel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 ||
            value.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '.' or ',' or '-' or '+' or '_' or '(' or ')' or '@'))) return null;
        return value.Trim();
    }

    private static object? ReadMemory()
    {
        var info = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref info) ? new { totalBytes = info.TotalPhysical, availableBytes = info.AvailablePhysical } : null;
    }

    private static int? ReadCores()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length is 0 or > 1_000_000) return null;
        var memory = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, memory, ref length)) return null;
            var count = 0;
            for (var offset = 0; offset + 8 <= length;)
            {
                var size = Marshal.ReadInt32(memory, offset + 4);
                if (size < 8 || offset + size > length) return null;
                offset += size;
                count++;
            }
            return count;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string[] ReadGraphics()
    {
        var models = new HashSet<string>();
        for (uint index = 0; index < 32; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if (SafeModel(device.Description) is { } model) models.Add(model);
        }
        return models.ToArray();
    }

    private static DiagnosticMonitor[] ReadMonitors()
    {
        var result = new List<DiagnosticMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect bounds, IntPtr _) =>
        {
            uint? dpi = Query(() => GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? (uint?)x : null);
            result.Add(new(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, dpi));
            return result.Count < 32;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, ExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr argument);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] private static extern bool GetLogicalProcessorInformationEx(int relation, IntPtr buffer, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr argument);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int mode, out uint x, out uint y);
}
