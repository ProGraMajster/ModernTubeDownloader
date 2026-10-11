using System.Runtime.InteropServices;
using System.Text;

namespace ModernTubeDownloader.Tests;

internal static class FileLockDiagnostics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { public uint Pid; public System.Runtime.InteropServices.ComTypes.FILETIME StartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public uint Type, Status, Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint count, string[] files, uint applications, nint processes, uint services, nint names);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? processes, ref uint reasons);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
    public static string Owners(string file)
    {
        if (RmStartSession(out var session, 0, new StringBuilder(33)) != 0) return "unavailable";
        try
        {
            if (RmRegisterResources(session, 1, [file], 0, 0, 0, 0) != 0) return "unavailable";
            uint count = 0, reasons = 0;
            var result = RmGetList(session, out var needed, ref count, null, ref reasons);
            if (result == 0) return "none-reported";
            if (result != 234 || needed > 100) return "unavailable";
            var processes = new ProcessInfo[needed]; count = needed;
            if (RmGetList(session, out _, ref count, processes, ref reasons) != 0) return "changed";
            return string.Join(",", processes.Take((int)count).Select(p => p.Process.Pid + ":" + p.AppName));
        }
        finally { RmEndSession(session); }
    }
}
