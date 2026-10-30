using System.Runtime.InteropServices;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>Shell file drops only. MFN still owns every input/layout/semantic operation.
/// Created, enabled and disposed on the HWND's UI thread; the delegate remains rooted until removal.</summary>
internal sealed class WindowsFileDrop : IDisposable
{
    private const uint DropFiles = 0x0233, Destroy = 0x0082;
    private const nuint SubclassId = 0x4D544444;
    private readonly SubclassProcedure procedure;
    private readonly Action<string[]> receive;
    private readonly IAppLogger logger;
    private nint window;
    private bool enabled;

    public WindowsFileDrop(nint window, Action<string[]> receive, IAppLogger logger)
    {
        this.window = window; this.receive = receive; this.logger = logger; procedure = WindowMessage;
        if (!OperatingSystem.IsWindows() || window == 0 || !SetWindowSubclass(window, procedure, SubclassId, 0))
            throw new InvalidOperationException("File drop could not attach to the native window.");
    }

    public bool Enabled
    {
        get => enabled;
        set { if (enabled == value || window == 0) return; enabled = value; DragAcceptFiles(window, value); }
    }

    private nint WindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == Destroy) Dispose();
        if (message != DropFiles) return DefSubclassProc(hwnd, message, wParam, lParam);
        try
        {
            // Never allocate an unbounded list of shell paths. The importer accepts exactly one file.
            var drop = (nint)wParam;
            if (!enabled) return 0;
            if (DragQueryFile(drop, uint.MaxValue, null, 0) != 1) { receive([]); return 0; }
            var length = DragQueryFile(drop, 0, null, 0);
            if (length is 0 or > 32767) { receive([]); return 0; }
            var path = new StringBuilder((int)length + 1);
            if (DragQueryFile(drop, 0, path, (uint)path.Capacity) != length) { receive([]); return 0; }
            receive([path.ToString()]);
        }
        catch (Exception error) { logger.Error("Native file drop failed.", error); }
        finally { DragFinish((nint)wParam); }
        return 0;
    }

    public void Dispose()
    {
        if (window == 0) return;
        DragAcceptFiles(window, false); RemoveWindowSubclass(window, procedure, SubclassId); window = 0;
        GC.KeepAlive(procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProcedure callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProcedure callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll")] private static extern void DragAcceptFiles(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);
    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, uint count);
    [DllImport("shell32.dll")] private static extern void DragFinish(nint drop);
}
