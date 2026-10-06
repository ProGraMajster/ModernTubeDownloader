using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>
/// Keeps a tool and its descendants in one Windows job. yt-dlp may spawn
/// FFmpeg with inherited redirected handles; if yt-dlp exits unexpectedly,
/// the descendant must be closed before stdout/stderr can finish draining.
/// </summary>
internal sealed class WindowsProcessJob(SafeFileHandle handle) : IDisposable
{
    private const uint KillOnJobClose = 0x00002000;
    private const int ExtendedLimitInformation = 9;

    public static WindowsProcessJob? TryAssign(Process process, IAppLogger logger)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            logger.Warning($"Could not create process job: Win32 error {Marshal.GetLastWin32Error()}.");
            handle.Dispose();
            return null;
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = KillOnJobClose }
        };
        if (!SetInformationJobObject(handle, ExtendedLimitInformation, ref limits,
                Marshal.SizeOf<JobObjectExtendedLimitInformation>()) ||
            !AssignProcessToJobObject(handle, process.SafeHandle))
        {
            logger.Warning($"Could not assign tool to process job: Win32 error {Marshal.GetLastWin32Error()}.");
            handle.Dispose();
            return null;
        }

        return new WindowsProcessJob(handle);
    }

    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass,
        ref JobObjectExtendedLimitInformation info, int infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
