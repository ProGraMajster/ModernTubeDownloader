// External QA observer only. Never reads captions or content of foreign windows.
// Activated/Deactivated are native foreground ownership transitions, not inferred
// managed framework callbacks. Input is limited to the explicit preflight/probe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public static class MtdNativePopupWitness
{
    public sealed class CaptionHitEvidence {
        public int X { get; set; } public int Y { get; set; }
        public long Hwnd { get; set; } public int Pid { get; set; } public long RootHwnd { get; set; }
        public bool OwnedPoint { get; set; } public bool HitTestCallSucceeded { get; set; }
        public ulong HitTestResult { get; set; } public int Win32Error { get; set; }
    }
    public sealed class PreflightEvidence {
        public string Stage { get; set; } public int ExpectedPid { get; set; } public int ActualPid { get; set; }
        public long MainHwnd { get; set; } public bool Visible { get; set; } public long DpiContext { get; set; }
        public bool Raised { get; set; } public int RaiseError { get; set; } public bool BoundsRead { get; set; }
        public uint Dpi { get; set; } public bool CursorMoved { get; set; } public int CursorError { get; set; }
        public bool CursorRead { get; set; } public int ActualCursorX { get; set; } public int ActualCursorY { get; set; }
        public CaptionHitEvidence FirstHit { get; set; } public CaptionHitEvidence FinalHit { get; set; }
        public uint SentInputCount { get; set; } public int InputError { get; set; }
        public long FinalForegroundHwnd { get; set; } public int FinalForegroundPid { get; set; }
    }
    public static PreflightEvidence LastPreflight { get; private set; }
    delegate void EventProc(IntPtr hook, uint kind, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Message { public IntPtr Hwnd; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct GuiInfo { public uint Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretBounds; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, EventProc callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessage(out Message message, IntPtr hwnd, uint first, uint last);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Message message, IntPtr hwnd, uint first, uint last, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    static readonly object Sync = new object();
    static readonly AutoResetEvent Changed = new AutoResetEvent(false);
    static readonly ManualResetEvent Ready = new ManualResetEvent(false);
    static Thread worker;
    static uint workerId;
    static int ownerPid;
    static IntPtr mainWindow, probe;
    static EventProc callback;
    static StreamWriter output;
    static Stopwatch clock;
    static Exception failure;
    static volatile bool interfered;
    static bool wasOwned;
    static int eventCount;
    public static bool Interfered { get { return interfered; } }
    public static int EventCount { get { lock (Sync) return eventCount; } }
    public static string Failure { get { lock (Sync) return failure == null ? null : failure.Message; } }
    public static int Owner(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); return (int)pid; }
    public static long[] FocusSnapshot(IntPtr main)
    {
        uint pid; var thread=GetWindowThreadProcessId(main,out pid);
        var info=new GuiInfo { Size=(uint)Marshal.SizeOf(typeof(GuiInfo)) };
        if(!GetGUIThreadInfo(thread,ref info)) throw new InvalidOperationException("Cannot read owned GUI thread state.");
        var foreground=GetForegroundWindow();
        return new long[] { foreground.ToInt64(),info.Active.ToInt64(),info.Focus.ToInt64(),info.Capture.ToInt64(),Owner(foreground) };
    }
    public static bool WaitChanged(int milliseconds) { return Changed.WaitOne(milliseconds); }
    public static bool ActivateOwnedMain(int pid, IntPtr main)
    {
        var evidence=new PreflightEvidence { Stage="ValidateOwnedMain",ExpectedPid=pid,ActualPid=Owner(main),MainHwnd=main.ToInt64(),Visible=IsWindowVisible(main) };
        LastPreflight=evidence;
        var initialForeground=GetForegroundWindow();
        evidence.FinalForegroundHwnd=initialForeground.ToInt64(); evidence.FinalForegroundPid=Owner(initialForeground);
        if (evidence.ActualPid != pid || !evidence.Visible) return false;
        if (initialForeground == main) { evidence.Stage="AlreadyForeground"; return true; }
        evidence.Stage="EnterPhysicalCoordinates";
        var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
        evidence.DpiContext=prior.ToInt64();
        if (prior == IntPtr.Zero) return false;
        try {
            Rect rect;
            Point actual;
            // Raise only the owned test window. Do not move/resize/activate it,
            // attach to another input queue, or generate an Alt shortcut.
            evidence.Stage="RaiseOwnedWithoutActivation";
            evidence.Raised=SetWindowPos(main,IntPtr.Zero,0,0,0,0,0x0013);
            evidence.RaiseError=evidence.Raised ? 0 : Marshal.GetLastWin32Error();
            if (!evidence.Raised) return false;
            evidence.Stage="ReadOwnedBounds";
            evidence.BoundsRead=GetWindowRect(main,out rect);
            if (!evidence.BoundsRead) return false;
            evidence.Dpi=GetDpiForWindow(main);
            var point=new Point { X=rect.Left+(rect.Right-rect.Left)/2,
                Y=rect.Top+(int)Math.Round(15*evidence.Dpi/96.0) };
            evidence.Stage="InitialCaptionHitTest";
            CaptionHitEvidence hit;
            bool isCaption=IsOwnedCaptionPoint(pid,main,point,out hit); evidence.FirstHit=hit;
            if (!isCaption) return false;
            evidence.Stage="MoveCursorToOwnedCaption";
            evidence.CursorMoved=SetCursorPos(point.X,point.Y);
            evidence.CursorError=evidence.CursorMoved ? 0 : Marshal.GetLastWin32Error();
            if (!evidence.CursorMoved) return false;
            evidence.Stage="VerifyCursorAndCaption";
            evidence.CursorRead=GetCursorPos(out actual); evidence.ActualCursorX=actual.X; evidence.ActualCursorY=actual.Y;
            if (!evidence.CursorRead || actual.X!=point.X || actual.Y!=point.Y) return false;
            isCaption=IsOwnedCaptionPoint(pid,main,actual,out hit); evidence.FinalHit=hit;
            if (!isCaption) return false;
            var click=new[] {
                new Input { Type=0, Data=new InputUnion { Mouse=new MouseInput { Flags=0x0002 } } },
                new Input { Type=0, Data=new InputUnion { Mouse=new MouseInput { Flags=0x0004 } } }
            };
            evidence.Stage="SendSingleCaptionClick";
            evidence.SentInputCount=SendInput(2,click,Marshal.SizeOf(typeof(Input)));
            evidence.InputError=evidence.SentInputCount==2 ? 0 : Marshal.GetLastWin32Error();
            if (evidence.SentInputCount != 2) return false;
            // Readiness wait only; the input is issued exactly once and never
            // repeated after observation starts or foreground is lost later.
            evidence.Stage="WaitForOwnedForeground";
            bool activated=SpinWait.SpinUntil(delegate { return GetForegroundWindow()==main; },500);
            if (activated) evidence.Stage="Complete";
            return activated;
        } finally {
            var foreground=GetForegroundWindow();
            evidence.FinalForegroundHwnd=foreground.ToInt64(); evidence.FinalForegroundPid=Owner(foreground);
            SetThreadDpiAwarenessContext(prior);
        }
    }
    static bool IsOwnedCaptionPoint(int pid,IntPtr main,Point point,out CaptionHitEvidence evidence)
    {
        var hit=WindowFromPoint(point);
        var root=GetAncestor(hit,2);
        evidence=new CaptionHitEvidence { X=point.X,Y=point.Y,Hwnd=hit.ToInt64(),Pid=Owner(hit),RootHwnd=root.ToInt64() };
        evidence.OwnedPoint=Owner(main)==pid && evidence.Pid==pid && (hit==main || root==main);
        if (!evidence.OwnedPoint) return false;
        var packed=new IntPtr(unchecked((int)((uint)(ushort)point.X | ((uint)(ushort)point.Y<<16))));
        UIntPtr result;
        evidence.HitTestCallSucceeded=SendMessageTimeout(main,0x0084,IntPtr.Zero,packed,0x0002,250,out result)!=IntPtr.Zero;
        evidence.Win32Error=evidence.HitTestCallSucceeded ? 0 : Marshal.GetLastWin32Error();
        evidence.HitTestResult=result.ToUInt64();
        return evidence.HitTestCallSucceeded && evidence.HitTestResult==2;
    }
    public static IntPtr[] OwnPopups(int pid, IntPtr main)
    {
        var list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr unused) {
            if (h == main || Owner(h) != pid || !IsWindowVisible(h)) return true;
            var parent = GetWindow(h, 4);
            for (int depth=0; depth<8 && parent != IntPtr.Zero; depth++, parent=GetWindow(parent,4))
                if (parent == main) { list.Add(h); break; }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }
    // Real hover, restricted to an already foreground owned HWND. No activation.
    public static bool HoverOwnPoint(int pid, IntPtr main, int x, int y)
    {
        var prior = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            var target = WindowFromPoint(new Point { X=x, Y=y });
            if (Owner(GetForegroundWindow()) != pid || Owner(target) != pid ||
                (target != main && GetAncestor(target, 2) != main)) return false;
            return SetCursorPos(x, y);
        } finally { if (prior != IntPtr.Zero) SetThreadDpiAwarenessContext(prior); }
    }
    public static void Start(int pid, IntPtr main, string path)
    {
        if (worker != null) throw new InvalidOperationException("Native observer already started.");
        if (Owner(main) != pid) throw new InvalidOperationException("Observer main HWND ownership mismatch.");
        ownerPid=pid; mainWindow=main; interfered=false; failure=null; eventCount=0;
        Ready.Reset(); clock=Stopwatch.StartNew();
        output=new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        output.AutoFlush=true;
        worker=new Thread(Run) { IsBackground=true, Name="MTD QA native observer" };
        worker.Start();
        if (!Ready.WaitOne(2000)) throw new TimeoutException("Native observer did not start.");
        if (failure != null) throw new InvalidOperationException("Native observer failed.", failure);
    }
    static void Run()
    {
        IntPtr foregroundHook=IntPtr.Zero, objectHook=IntPtr.Zero;
        try {
            workerId=GetCurrentThreadId(); Message pending; PeekMessage(out pending, IntPtr.Zero,0,0,0);
            callback=OnEvent;
            foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
            objectHook=SetWinEventHook(0x8001,0x8005,IntPtr.Zero,callback,(uint)ownerPid,0,0);
            if (foregroundHook==IntPtr.Zero || objectHook==IntPtr.Zero) throw new InvalidOperationException("Cannot register WinEvent observer.");
            var foreground=GetForegroundWindow(); wasOwned=Owner(foreground)==ownerPid;
            if (!wasOwned) interfered=true;
            Write("InitialForeground",foreground,0,0,0);
            Ready.Set();
            Message msg;
            while (GetMessage(out msg,IntPtr.Zero,0,0)>0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        } catch (Exception ex) { lock(Sync) failure=ex; Ready.Set(); }
        finally {
            if(foregroundHook!=IntPtr.Zero) UnhookWinEvent(foregroundHook);
            if(objectHook!=IntPtr.Zero) UnhookWinEvent(objectHook);
            lock(Sync) { output.Dispose(); output=null; }
            Changed.Set();
        }
    }
    static void OnEvent(IntPtr hook,uint kind,IntPtr hwnd,int objectId,int childId,uint thread,uint time)
    {
        try {
            if(kind==3) {
                bool owned=Owner(hwnd)==ownerPid;
                if(!owned && hwnd!=probe) interfered=true;
                Write(owned ? wasOwned ? "OwnedForegroundChanged" : "Activated" : wasOwned ? "Deactivated" : "ForegroundChanged",hwnd,kind,objectId,childId,time);
                wasOwned=owned;
            } else if(Owner(hwnd)==ownerPid) {
                string name=kind==0x8001 ? "Destroy" : kind==0x8002 ? "Show" : kind==0x8003 ? "Hide" : kind==0x8005 ? "Focus" : "Reorder";
                Write(name,hwnd,kind,objectId,childId,time);
            }
        } catch(Exception ex) { lock(Sync) failure=ex; }
        Changed.Set();
    }
    static void Write(string name,IntPtr hwnd,uint kind,int objectId,int childId,uint nativeEventTime=0)
    {
        lock(Sync) {
            if(++eventCount>10000) { failure=new InvalidOperationException("Native event witness reached its bounded limit."); return; }
            var foreground=GetForegroundWindow();
            var focus=FocusSnapshot(mainWindow);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{{\"sequence\":{0},\"elapsedMs\":{1:F3},\"event\":\"{2}\",\"winEvent\":{3},\"hwnd\":{4},\"pid\":{5},\"objectId\":{6},\"childId\":{7},\"foregroundHwnd\":{8},\"foregroundPid\":{9},\"visible\":{10},\"controlledProbe\":{11},\"activeHwnd\":{12},\"focusHwnd\":{13},\"captureHwnd\":{14},\"nativeEventTimeMs\":{15}}}",
                eventCount,clock.Elapsed.TotalMilliseconds,name,kind,hwnd.ToInt64(),Owner(hwnd),objectId,childId,foreground.ToInt64(),Owner(foreground),IsWindowVisible(hwnd)?"true":"false",foreground==probe && probe!=IntPtr.Zero?"true":"false",focus[1],focus[2],focus[3],nativeEventTime));
        }
    }
    public static IntPtr ActivateProbe()
    {
        if(Owner(GetForegroundWindow())!=ownerPid) throw new InvalidOperationException("INTERFERED: foreground changed before controlled deactivation.");
        probe=CreateWindowEx(0x80,"STATIC","MTD QA focus probe",0x00CF0000,20,20,260,100,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(probe==IntPtr.Zero) throw new InvalidOperationException("Cannot create owned QA focus probe.");
        ShowWindow(probe,5);
        if(!SetForegroundWindow(probe) || GetForegroundWindow()!=probe) throw new InvalidOperationException("INCONCLUSIVE: controlled foreground activation was denied.");
        return probe;
    }
    public static void CloseProbe()
    {
        if(probe==IntPtr.Zero) return;
        // Only restore our application if the controlled probe still owns focus.
        // Never take foreground back from an unrelated user's window.
        if(GetForegroundWindow()==probe) SetForegroundWindow(mainWindow);
        DestroyWindow(probe); probe=IntPtr.Zero;
    }
    public static void Stop()
    {
        CloseProbe();
        if(worker==null) return;
        PostThreadMessage(workerId,0x12,IntPtr.Zero,IntPtr.Zero);
        if(!worker.Join(2000)) throw new TimeoutException("Native observer did not stop.");
        worker=null;
    }
}
