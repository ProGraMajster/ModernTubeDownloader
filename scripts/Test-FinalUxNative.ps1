#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$OwnedProcessId,
    [Parameter(Mandatory)][long]$StartUtcTicks,
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$LanguageDirectory,
    [Parameter(Mandatory)][Alias('EvidenceDirectory')][string]$EvidenceDir,
    [switch]$Before
)
# External production UIA + HWND-directed messages. This is NOT physical input,
# foreground acceptance, taskbar qualification, or a substitute for pixel review.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5) { throw 'Use Windows PowerShell 5.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
public sealed class FinalUxNative : IDisposable {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Message { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public Point point; public uint unused; }
    [StructLayout(LayoutKind.Sequential)] struct GuiInfo { public int size; public uint flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public Rect caretRect; }
    public sealed class Event { public string utc; public uint kind; public long hwnd; public uint pid; public int objectId, childId; }
    delegate bool WindowCallback(IntPtr hwnd, IntPtr state);
    delegate void EventCallback(IntPtr hook, uint kind, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("kernel32.dll", SetLastError=true)] static extern SafeWaitHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint GetProcessId(SafeWaitHandle process);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetProcessTimes(SafeWaitHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageNameW(SafeWaitHandle process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(SafeWaitHandle process, uint ms);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll", SetLastError=true)] static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, EventCallback callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint id, uint message, UIntPtr wp, IntPtr lp);
    readonly SafeWaitHandle process; readonly int pid; readonly long ticks; readonly string image;
    readonly ConcurrentQueue<Event> events = new ConcurrentQueue<Event>();
    readonly ManualResetEvent ready = new ManualResetEvent(false); Thread listener; uint listenerId; Exception listenerError;
    public FinalUxNative(int id, long startTicks, string path) {
        pid=id; ticks=startTicks; image=System.IO.Path.GetFullPath(path);
        process=OpenProcess(0x00100000 | 0x1000, false, id);
        if(process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Open owned process failed.");
        Validate();
        listener=new Thread(Listen); listener.IsBackground=true; listener.Start();
        if(!ready.WaitOne(5000)) throw new InvalidOperationException("WinEvent listener failed to initialize.");
        if(listenerError != null) throw listenerError;
    }
    public void Validate() {
        if(WaitForSingleObject(process,0)!=258) throw new InvalidOperationException("Owned process has exited or cannot be verified.");
        long creation,exit,kernel,user; uint length=32768; var value=new StringBuilder((int)length);
        if(GetProcessId(process)!=(uint)pid || !GetProcessTimes(process,out creation,out exit,out kernel,out user) ||
           !QueryFullProcessImageNameW(process,0,value,ref length)) throw new InvalidOperationException("Kernel process identity is unavailable.");
        if(DateTime.FromFileTimeUtc(creation).Ticks!=ticks || !String.Equals(System.IO.Path.GetFullPath(value.ToString()),image,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned process PID/start/image mismatch.");
    }
    void ValidateWindow(IntPtr hwnd) { Validate(); uint actual; GetWindowThreadProcessId(hwnd,out actual); if(actual!=(uint)pid || !IsWindowVisible(hwnd)) throw new InvalidOperationException("Not a visible owned HWND."); }
    public IntPtr[] Windows() { Validate(); var result=new List<IntPtr>(); EnumWindows((h,s)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==(uint)pid && IsWindowVisible(h)) result.Add(h); return true; },IntPtr.Zero); return result.ToArray(); }
    public static uint Owner(IntPtr hwnd) { uint p; GetWindowThreadProcessId(hwnd,out p); return p; }
    public long Focus(IntPtr hwnd) { ValidateWindow(hwnd); uint p; var thread=GetWindowThreadProcessId(hwnd,out p); var info=new GuiInfo {size=Marshal.SizeOf(typeof(GuiInfo))}; if(!GetGUIThreadInfo(thread,ref info)) throw new Win32Exception(); return info.focus.ToInt64(); }
    public Rect Capture(IntPtr hwnd,string path) {
        ValidateWindow(hwnd); var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { Rect rect; if(!GetWindowRect(hwnd,out rect) || rect.Right<=rect.Left || rect.Bottom<=rect.Top) throw new InvalidOperationException("Empty owned window.");
            using(var bitmap=new Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top)) using(var graphics=Graphics.FromImage(bitmap)) {
                var dc=graphics.GetHdc(); try { if(!PrintWindow(hwnd,dc,2)) throw new InvalidOperationException("PrintWindow failed."); } finally { graphics.ReleaseHdc(dc); }
                ValidateWindow(hwnd); bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            } return rect;
        } finally { SetThreadDpiAwarenessContext(previous); }
    }
    public Point Pointer(IntPtr hwnd,uint message,int screenX,int screenY) {
        ValidateWindow(hwnd); var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { var point=new Point {X=screenX,Y=screenY}; Rect client;
            if(!ScreenToClient(hwnd,ref point) || !GetClientRect(hwnd,out client) || point.X<0 || point.Y<0 || point.X>=client.Right || point.Y>=client.Bottom || point.X>32767 || point.Y>32767) throw new InvalidOperationException("Pointer outside owned client.");
            if(message!=0x200 && message!=0x201 && message!=0x202) throw new InvalidOperationException("Unexpected pointer message.");
            if(!PostMessage(hwnd,message,new UIntPtr(message==0x201 ? 1u : 0u),new IntPtr((point.Y<<16)|(point.X&65535)))) throw new Win32Exception(); return point;
        } finally { SetThreadDpiAwarenessContext(previous); }
    }
    public void Escape(IntPtr main) {
        ValidateWindow(main); var focus=new IntPtr(Focus(main)); if(focus==IntPtr.Zero) throw new InvalidOperationException("No owned native keyboard focus."); ValidateWindow(focus);
        if(!PostMessage(focus,0x100,new UIntPtr(27),new IntPtr(1)) || !PostMessage(focus,0x101,new UIntPtr(27),new IntPtr(unchecked((int)0xC0000001)))) throw new Win32Exception();
    }
    void Listen() {
        IntPtr foreground=IntPtr.Zero,focus=IntPtr.Zero; EventCallback callback=(h,k,w,o,c,t,time)=>events.Enqueue(new Event {utc=DateTime.UtcNow.ToString("O"),kind=k,hwnd=w.ToInt64(),pid=Owner(w),objectId=o,childId=c});
        try { listenerId=GetCurrentThreadId(); foreground=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0); focus=SetWinEventHook(0x8005,0x8005,IntPtr.Zero,callback,0,0,0);
            if(foreground==IntPtr.Zero || focus==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"WinEvent hook failed."); ready.Set(); Message message; while(GetMessage(out message,IntPtr.Zero,0,0)>0) {};
        } catch(Exception ex) { listenerError=ex; ready.Set(); }
        finally { if(foreground!=IntPtr.Zero) UnhookWinEvent(foreground); if(focus!=IntPtr.Zero) UnhookWinEvent(focus); GC.KeepAlive(callback); }
    }
    public Event[] Drain() { var result=new List<Event>(); Event value; while(events.TryDequeue(out value)) result.Add(value); return result.ToArray(); }
    public void Dispose() { if(listenerId!=0) PostThreadMessage(listenerId,0x12,UIntPtr.Zero,IntPtr.Zero); if(listener!=null) listener.Join(2000); process.Dispose(); ready.Dispose(); }
}
'@
# A single explicit script-scoped state works both from -File and when the
# production wrapper invokes this source through ScriptBlock.Create.
$script:FinalUxState = @{}
$script:FinalUxState.events = [Collections.Generic.List[object]]::new()
$script:FinalUxState.watch = [Diagnostics.Stopwatch]::StartNew()
$script:FinalUxState.native = $null; $script:FinalUxState.errorText = $null; $script:FinalUxState.interrupted = $false; $script:FinalUxState.main = $null
$script:FinalUxState.initialForeground = [FinalUxNative]::GetForegroundWindow().ToInt64()
$script:FinalUxState.lastForeground = $script:FinalUxState.initialForeground
$phase = if ($Before) { 'BEFORE' } else { 'AFTER' }
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
[IO.Directory]::CreateDirectory($EvidenceDir) | Out-Null
if ((Test-Path -LiteralPath (Join-Path $EvidenceDir 'native-result.json')) -or (Test-Path -LiteralPath (Join-Path $EvidenceDir 'native-events.jsonl'))) { throw 'Evidence directory already contains a run; use a new directory.' }

function Record([string]$Action, $Detail) {
    $entry=@{ action=$Action; utc=[DateTime]::UtcNow.ToString('O'); elapsedMs=$script:FinalUxState.watch.Elapsed.TotalMilliseconds; detail=$Detail }
    $script:FinalUxState.events.Add($entry)
    # Preserve the last started native action even if an external watchdog must
    # terminate an unresponsive UIA client before the final report is written.
    [IO.File]::AppendAllText((Join-Path $EvidenceDir 'native-events.jsonl'),($entry | ConvertTo-Json -Depth 10 -Compress)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
}
function Check-Events {
    $script:FinalUxState.native.Validate()
    foreach ($item in $script:FinalUxState.native.Drain()) {
        Record 'winEvent' $item
        if ($item.kind -eq 3 -and $item.hwnd -ne $script:FinalUxState.lastForeground) {
            if ($item.pid -ne $OwnedProcessId) { $script:FinalUxState.interrupted=$true; Record 'foreground.interruption' $item }
            $script:FinalUxState.lastForeground=$item.hwnd
        }
    }
    $foreground=[FinalUxNative]::GetForegroundWindow()
    if ($foreground.ToInt64() -ne $script:FinalUxState.lastForeground) {
        if ([FinalUxNative]::Owner($foreground) -ne $OwnedProcessId) { $script:FinalUxState.interrupted=$true; Record 'foreground.interruption.sample' @{hwnd=$foreground.ToInt64(); pid=[FinalUxNative]::Owner($foreground)} }
        $script:FinalUxState.lastForeground=$foreground.ToInt64()
    }
    if ($script:FinalUxState.interrupted) { throw 'INTERRUPTED: external foreground changed; this run cannot be PASS.' }
}
function Native-Call([string]$Action, [scriptblock]$Operation) {
    Check-Events
    $timer=[Diagnostics.Stopwatch]::StartNew(); $foreground=[FinalUxNative]::GetForegroundWindow().ToInt64()
    Record 'native.begin' @{action=$Action; foreground=$foreground}
    try { & $Operation } finally {
        $focus=$null; if ($script:FinalUxState.main) { try { $focus=$script:FinalUxState.native.Focus([IntPtr]$script:FinalUxState.main.Current.NativeWindowHandle) } catch { $focus='unavailable' } }
        Record $Action @{durationMs=$timer.Elapsed.TotalMilliseconds; foregroundBefore=$foreground; foregroundAfter=[FinalUxNative]::GetForegroundWindow().ToInt64(); nativeFocus=$focus}
    }
    Check-Events
}
function Wait-Condition([scriptblock]$Condition,[string]$Failure,[int]$Seconds=12) {
    $timer=[Diagnostics.Stopwatch]::StartNew(); $observations=0
    do { Check-Events; $observations++; if (& $Condition) { Record 'condition.ready' @{condition=$Failure; durationMs=$timer.Elapsed.TotalMilliseconds; observations=$observations}; return }; Start-Sleep -Milliseconds 80 } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    throw $Failure
}
function Window-Now([string]$Id) {
    foreach ($hwnd in $script:FinalUxState.native.Windows()) { $element=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd); if ($element.Current.ProcessId -eq $OwnedProcessId -and $element.Current.AutomationId -eq $Id) { return $element } }
    return $null
}
function Find-Now($Parent,[string]$Id) {
    $element=$Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
    if ($element -and $element.Current.ProcessId -ne $OwnedProcessId) { throw 'UIA result belongs to a different process.' }
    return $element
}
function Find($Parent,[string]$Id) {
    $script:FinalUxState.found=$null
    Native-Call ('uia.find.'+$Id) { Wait-Condition { $script:FinalUxState.found=Find-Now $Parent $Id; $script:FinalUxState.found -and -not $script:FinalUxState.found.Current.IsOffscreen } "Visible control missing: $Id" } | Out-Null
    return $script:FinalUxState.found
}
function Invoke($Parent,[string]$Id) {
    $element=Find $Parent $Id
    Native-Call ('uia.invoke.'+$Id) { if (-not $element.Current.IsEnabled) { throw "Disabled control: $Id" }; $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } | Out-Null
}
function Set-Text($Parent,[string]$Id,[string]$Value) {
    $element=Find $Parent $Id
    Native-Call ('uia.value.'+$Id) { $element.SetFocus(); $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value) } | Out-Null
    Wait-Condition { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $Value } "Value did not apply: $Id"
    return $element
}
function File-Hash([string]$Path) {
    if (-not [IO.File]::Exists($Path)) { return $null }; $stream=[IO.File]::OpenRead($Path); $hash=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','') } finally { $hash.Dispose(); $stream.Dispose() }
}
function Capture($Window,[string]$Name) {
    Check-Events; $path=Join-Path $EvidenceDir ($Name+'.png'); if ([IO.File]::Exists($path)) { throw "Capture already exists: $Name" }
    $timer=[Diagnostics.Stopwatch]::StartNew(); $rect=$script:FinalUxState.native.Capture([IntPtr]$Window.Current.NativeWindowHandle,$path)
    Record 'capture' @{file=[IO.Path]::GetFileName($path); width=$rect.Right-$rect.Left; height=$rect.Bottom-$rect.Top; sha256=(File-Hash $path); durationMs=$timer.Elapsed.TotalMilliseconds; hwnd=$Window.Current.NativeWindowHandle; foreground=[FinalUxNative]::GetForegroundWindow().ToInt64()}
    Check-Events
}
function Label-Now([string]$Caption) {
    $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Caption)
    $matches=$script:FinalUxState.main.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
    $visible=@($matches | Where-Object { $_.Current.ProcessId -eq $OwnedProcessId -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Width -gt 0 -and $_.Current.BoundingRectangle.Height -gt 0 })
    if ($visible.Count -gt 1) { throw "Ambiguous exact visible label: $Caption" }
    if ($visible.Count -eq 1) { return $visible[0] }; return $null
}
function Label-States([string]$Key,[string]$Name) {
    $caption=$labels.$Key; if (-not $caption) { throw "Missing trusted localization key: $Key" }
    $script:FinalUxState.label=$null
    Native-Call ('uia.label.'+$Key) { Wait-Condition { $script:FinalUxState.label=Label-Now $caption; $null -ne $script:FinalUxState.label } "Exact label absent: $Key" } | Out-Null
    $b=$script:FinalUxState.label.Current.BoundingRectangle; $x=[int]($b.Left+$b.Width/2); $y=[int]($b.Top+$b.Height/2)
    Record 'label.bounds' @{key=$Key; caption=$caption; x=$b.Left; y=$b.Top; width=$b.Width; height=$b.Height; hwnd=$script:FinalUxState.main.Current.NativeWindowHandle}
    Capture $script:FinalUxState.main ($Name+'-normal')
    $script:FinalUxState.pointerDownQueued=$false
    try {
        foreach ($state in @(@('hover',0x200),@('pressed',0x201),@('released',0x202))) {
            Native-Call ('postMessage.'+$Name+'.'+$state[0]) {
                $point=$script:FinalUxState.native.Pointer([IntPtr]$script:FinalUxState.main.Current.NativeWindowHandle,[uint32]$state[1],$x,$y)
                if ($state[1] -eq 0x201) { $script:FinalUxState.pointerDownQueued=$true }; if ($state[1] -eq 0x202) { $script:FinalUxState.pointerDownQueued=$false }
                Record 'pointer.queued' @{message=$state[1]; clientX=$point.X; clientY=$point.Y}
            } | Out-Null
            # Queueing a Win32 message is not physical hit-testing or proof of a
            # particular visual state. Observe UIA responsiveness before the raw capture;
            # retain this limitation instead of asserting an invented state transition.
            Native-Call 'uia.pointerObservation' { $current=Label-Now $caption; if (-not $current) { throw 'Label vanished after posted pointer message.' } } | Out-Null
            Capture $script:FinalUxState.main ($Name+'-'+$state[0])
        }
    } finally {
        # Pair our queued down even after an interruption. Never release capture or
        # inject input globally: the kernel identity and HWND owner are rechecked.
        if ($script:FinalUxState.pointerDownQueued) {
            try { [void]$script:FinalUxState.native.Pointer([IntPtr]$script:FinalUxState.main.Current.NativeWindowHandle,0x202,$x,$y); Record 'pointer.cleanup-owned-up' $Name }
            catch { Record 'pointer.cleanup-failed' $_.Exception.Message }
        }
    }
}

try {
    $script:FinalUxState.native=[FinalUxNative]::new($OwnedProcessId,$StartUtcTicks,$ApplicationPath)
    Record 'identity.verified' @{pid=$OwnedProcessId; startUtcTicks=$StartUtcTicks; imageQuery='QueryFullProcessImageNameW'; initialForeground=$script:FinalUxState.initialForeground; directedMessagesOnly=$true}
    $settingsPath=Join-Path $DataRoot 'settings.json'
    $settings=Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $language=[string]$settings.language
    # Caller supplies the reviewed resource directory explicitly: the production
    # wrapper invokes this source as a scriptblock, where PSScriptRoot is empty.
    $languageRoot=[IO.Path]::GetFullPath($LanguageDirectory)
    $languageFiles=@(Get-ChildItem -LiteralPath $languageRoot -Filter '*.json' -File)
    $languageFile=$languageFiles | Where-Object { $_.BaseName -eq $language } | Select-Object -First 1
    if (-not $languageFile) { throw "No exact trusted localization file for profile language: $language" }
    $labels=Get-Content -LiteralPath $languageFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $stores=@{}; foreach ($name in @('settings.json','queue.json','history.json')) { $stores[$name]=File-Hash (Join-Path $DataRoot $name) }
    Wait-Condition { $script:FinalUxState.main=Window-Now 'MainWindow'; $null -ne $script:FinalUxState.main } 'MainWindow absent.'
    Invoke $script:FinalUxState.main 'NavDownloads'
    Label-States 'Downloads.QueueEmptyTitle' '01-downloads-empty'
    Label-States 'Dashboard.SpeedChart' '02-speed-title'
    Label-States 'Dashboard.DiskSpace' '03-disk-label'
    Invoke $script:FinalUxState.main 'NavLive'; Capture $script:FinalUxState.main '04-live'
    Invoke $script:FinalUxState.main 'NavHelp'; Capture $script:FinalUxState.main '05-help'
    if ($Before) { Label-States 'Dashboard.HelpBody' '06-help-body' }
    Invoke $script:FinalUxState.main 'NavSettings'; Capture $script:FinalUxState.main '07-settings'
    if (-not $Before) {
        $search=Set-Text $script:FinalUxState.main 'SettingsCategorySearch' ([string]$labels.'Settings.Language')
        $result=Find $script:FinalUxState.main 'SettingsSearchResult0'
        if (-not $result.Current.Name.StartsWith([string]$labels.'Settings.Language',[StringComparison]::Ordinal)) { throw 'First result is not the exact language option.' }
        Capture $script:FinalUxState.main '08-settings-language-results'
        # Resolve target before Invoke: no full-tree search between activation and
        # the transient 3.5-second highlight capture.
        $target=Find-Now $script:FinalUxState.main 'LanguageSelector'
        if (-not $target) { throw 'Canonical LanguageSelector absent before reveal.' }
        $timer=[Diagnostics.Stopwatch]::StartNew()
        Native-Call 'uia.invoke.SettingsSearchResult0' { $result.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } | Out-Null
        Capture $script:FinalUxState.main '09-settings-language-highlight'
        if ($timer.Elapsed.TotalMilliseconds -ge 3500) { throw 'NOT QUALIFIED: native round-trip exceeded the highlight lifetime; screenshot cannot prove highlight.' }
        if (-not $target.Current.HasKeyboardFocus -or $target.Current.IsOffscreen) { throw 'Search did not reveal and focus the canonical language control.' }
        Record 'search.reveal' @{nativeRoundTripMs=$timer.Elapsed.TotalMilliseconds; target='LanguageSelector'; focused=$true}
        $search=Set-Text $script:FinalUxState.main 'SettingsCategorySearch' 'no-setting-match-73f89c'
        [void](Find $script:FinalUxState.main 'SettingsSearchEmpty'); Capture $script:FinalUxState.main '10-settings-no-results'
        if (Find-Now $script:FinalUxState.main 'SettingsSearchResult0') { $row=Find-Now $script:FinalUxState.main 'SettingsSearchResult0'; if (-not $row.Current.IsOffscreen) { throw 'A search result remains visible for a nonexistent caption.' } }
        Native-Call 'postMessage.escape' { $script:FinalUxState.native.Escape([IntPtr]$script:FinalUxState.main.Current.NativeWindowHandle) } | Out-Null
        Wait-Condition { $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '' } 'Escape did not clear the search.'
        $search=Set-Text $script:FinalUxState.main 'SettingsCategorySearch' ([string]$labels.'Settings.Language')
        Invoke $script:FinalUxState.main 'SettingsSearchClear'
        Wait-Condition { $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '' } 'Clear did not clear the search.'
        Capture $script:FinalUxState.main '11-settings-reset'
        Invoke $script:FinalUxState.main 'NavHelp'; Invoke $script:FinalUxState.main 'HelpWhatsNew'
        $script:FinalUxState.news=$null; Wait-Condition { $script:FinalUxState.news=Window-Now 'WhatsNewWindow'; $null -ne $script:FinalUxState.news } 'WhatsNewWindow absent.'
        $provenance=Find $script:FinalUxState.news 'WhatsNewProvenance'
        if ($provenance.Current.Name -ne $labels.'Experience.LocalNotes') { throw 'Notes do not identify the bundled offline source.' }
        foreach ($id in @('new','improved','fixed','limitations')) { if (-not (Find-Now $script:FinalUxState.news ('WhatsNewSection-'+$id))) { throw "Missing release section: $id" } }
        Capture $script:FinalUxState.news '12-whats-new-bundled'
        Record 'notes.source' @{source='embedded-manifest'; noOnlineLinkInvoked=$true; networkIsolation='NOT PROVIDED BY THIS HARNESS'}
        Invoke $script:FinalUxState.news 'WhatsNewLater'; Wait-Condition { $null -eq (Window-Now 'WhatsNewWindow') } 'Later did not close WhatsNew.'
        foreach ($name in $stores.Keys) { if ((File-Hash (Join-Path $DataRoot $name)) -ne $stores[$name]) { throw "Read-only UX actions changed $name." } }
        Record 'stores.unchanged' @('settings.json','queue.json','history.json')
    }
    Check-Events
} catch {
    $script:FinalUxState.errorText=$_.Exception.ToString(); Record 'failure' $script:FinalUxState.errorText
    if ($script:FinalUxState.native) { try { foreach ($hwnd in $script:FinalUxState.native.Windows()) { $path=Join-Path $EvidenceDir ('failure-owned-'+$hwnd.ToInt64()+'.png'); if (-not [IO.File]::Exists($path)) { [void]$script:FinalUxState.native.Capture($hwnd,$path) } } } catch { Record 'failure.capture' $_.Exception.Message } }
} finally {
    if ($script:FinalUxState.native) {
        foreach ($item in $script:FinalUxState.native.Drain()) {
            Record 'winEvent.final' $item
            if ($item.kind -eq 3 -and $item.hwnd -ne $script:FinalUxState.lastForeground -and $item.pid -ne $OwnedProcessId) { $script:FinalUxState.interrupted=$true }
            if ($item.kind -eq 3) { $script:FinalUxState.lastForeground=$item.hwnd }
        }
        $script:FinalUxState.native.Dispose()
    }
    @{phase=$phase; result=$(if ($script:FinalUxState.interrupted) {'INTERRUPTED'} elseif ($script:FinalUxState.errorText) {'FAIL'} else {'EVIDENCE COLLECTED'}); assertions=$(if ($Before) {'baseline observation only'} elseif ($script:FinalUxState.errorText) {'not qualified'} else {'targeted UIA actions passed'}); visualAcceptance='MANUAL REVIEW REQUIRED'; physicalInput='NOT EXECUTED'; semanticBridge=$false; elapsedSeconds=$script:FinalUxState.watch.Elapsed.TotalSeconds; processId=$OwnedProcessId; error=$script:FinalUxState.errorText; events=@($script:FinalUxState.events.ToArray())} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $EvidenceDir 'native-result.json') -Encoding UTF8
}
if ($script:FinalUxState.errorText) { Write-Error $script:FinalUxState.errorText; exit 1 }; if ($script:FinalUxState.interrupted) { exit 2 }; exit 0
