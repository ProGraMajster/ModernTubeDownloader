[CmdletBinding()]
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$WorktreeRoot, [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][int]$TargetX, [Parameter(Mandatory)][int]$TargetY)
# Windows PowerShell / real Explorer drag. Only a newly created Explorer HWND for
# this owned temporary folder may be moved or closed. No Explorer process is killed.
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($DataRoot)
$file = [IO.Path]::GetFullPath($FilePath)
if ([IO.Path]::GetFileName($rootPath) -notmatch '^mtd-automation-smoke-[a-f0-9]{32}$' -or
    -not $rootPath.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -or
    -not $file.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Drop fixture is outside the owned QA profile.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ExplorerDropInput {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint f);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,uint a,out int v,int size);
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] input,int size);
    public static void Button(bool down) {
        var input=new Input { Data=new Union { Mouse=new Mouse { Flags=down?2u:4u } } };
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1) throw new InvalidOperationException("SendInput rejected mouse button.");
    }
    public static bool OwnPoint(IntPtr root,int x,int y) { return GetAncestor(WindowFromPoint(new Point { X=x,Y=y }),2)==root; }
}
'@
$owner = Get-Process -Id $ProcessId
if (-not $owner.Path.StartsWith([IO.Path]::GetFullPath($WorktreeRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Application is outside the worktree.' }
$shell = New-Object -ComObject Shell.Application
$before = @($shell.Windows() | ForEach-Object { [long]$_.HWND })
$directory = [IO.Path]::GetDirectoryName($file)
$explorer = $null; $mouseDown = $false
$point = New-Object ExplorerDropInput+Point
[ExplorerDropInput]::GetCursorPos([ref]$point) | Out-Null
$dpi = [ExplorerDropInput]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    Start-Process -FilePath explorer.exe -ArgumentList ('/n,/separate,"' + $directory + '"') | Out-Null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        foreach ($window in @($shell.Windows())) {
            if ($before -contains [long]$window.HWND) { continue }
            try { if ([IO.Path]::GetFullPath($window.Document.Folder.Self.Path) -eq $directory) { $explorer = $window; break } } catch { }
        }
        if (-not $explorer) { Start-Sleep -Milliseconds 100 }
    } while (-not $explorer -and $clock.Elapsed.TotalSeconds -lt 15)
    if (-not $explorer) { throw 'No new, owned Explorer window was found.' }
    $handle = [ExplorerDropInput]::GetAncestor([IntPtr]([long]$explorer.HWND), 2)
    $rightEdge = [ExplorerDropInput]::GetSystemMetrics(76) + [ExplorerDropInput]::GetSystemMetrics(78)
    $appRect = New-Object ExplorerDropInput+Rect
    if (-not [ExplorerDropInput]::GetWindowRect($owner.MainWindowHandle,[ref]$appRect)) { throw 'Owned application geometry unavailable.' }
    $explorerX = if ($appRect.Right + 656 -lt $rightEdge) { $appRect.Right + 16 }
        elseif ($appRect.Left - 656 -ge [ExplorerDropInput]::GetSystemMetrics(76)) { $appRect.Left - 656 }
        elseif ($TargetX + 760 -lt $rightEdge) { $TargetX + 120 } else { $TargetX - 720 }
    [ExplorerDropInput]::SetForegroundWindow($owner.MainWindowHandle) | Out-Null
    # Temporarily raise only the two owned test windows above unrelated topmost
    # windows. Restore the application's normal band in finally.
    [ExplorerDropInput]::SetWindowPos($owner.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x0053) | Out-Null
    if (-not [ExplorerDropInput]::SetWindowPos($handle, [IntPtr](-1), $explorerX, [Math]::Max(0,$TargetY - 80), 640, 480, 0x0040)) {
        throw "Owned Explorer placement failed, Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
    }
    [ExplorerDropInput]::SetForegroundWindow($handle) | Out-Null
    $explorer.Document.SelectItem($explorer.Document.Folder.ParseName([IO.Path]::GetFileName($file)), 0x1D)
    $automation = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $fileName = [IO.Path]::GetFileName($file); $shortName = [IO.Path]::GetFileNameWithoutExtension($file)
    $condition = New-Object System.Windows.Automation.OrCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$fileName)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$shortName)))
    $item = $null; $clock.Restart()
    do {
        $candidates = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
        foreach ($candidate in $candidates) {
            if (-not $candidate.Current.IsOffscreen -and $candidate.Current.BoundingRectangle.Width -gt 0 -and
                $candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $item=$candidate; break }
        }
        if (-not $item) { Start-Sleep -Milliseconds 100 }
    } while (-not $item -and $clock.Elapsed.TotalSeconds -lt 10)
    if (-not $item) { throw 'Fixture file has no visible Explorer UIA item.' }
    if (-not [ExplorerDropInput]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0, 0, 0, 0x0043)) {
        throw "Owned Explorer raise failed, Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
    }
    $bounds = $item.Current.BoundingRectangle
    $startX = [int]($bounds.Left + [Math]::Min(60,$bounds.Width / 2)); $startY = [int]($bounds.Top + $bounds.Height / 2)
    $sourceOwned = [ExplorerDropInput]::OwnPoint($handle,$startX,$startY)
    $targetOwned = [ExplorerDropInput]::OwnPoint($owner.MainWindowHandle,$TargetX,$TargetY)
    if (-not $sourceOwned -or -not $targetOwned) {
        $hit = New-Object ExplorerDropInput+Point; $hit.X=$startX; $hit.Y=$startY
        $sourceRoot = [ExplorerDropInput]::GetAncestor([ExplorerDropInput]::WindowFromPoint($hit),2)
        $cloaked=0; [void][ExplorerDropInput]::DwmGetWindowAttribute($handle,14,[ref]$cloaked,4)
        $rect = New-Object ExplorerDropInput+Rect
        if ([ExplorerDropInput]::GetWindowRect($handle,[ref]$rect)) {
            $bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top); $graphics=[Drawing.Graphics]::FromImage($bitmap); $dc=$graphics.GetHdc()
            try { [void][ExplorerDropInput]::PrintWindow($handle,$dc,2) } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
            try { $bitmap.Save((Join-Path $rootPath 'explorer-drop-source.png')) } finally { $bitmap.Dispose() }
        }
        throw "Drag endpoints are obscured: sourceOwned=$sourceOwned ($startX,$startY), expected=$handle, actual=$sourceRoot, app=$($owner.MainWindowHandle), ExplorerRect=$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom), DwmCloaked=$cloaked; targetOwned=$targetOwned ($TargetX,$TargetY)."
    }
    [ExplorerDropInput]::SetCursorPos($startX,$startY) | Out-Null
    [ExplorerDropInput]::Button($true); $mouseDown=$true
    foreach ($step in 1..24) {
        [ExplorerDropInput]::SetCursorPos([int]($startX+($TargetX-$startX)*$step/24),[int]($startY+($TargetY-$startY)*$step/24)) | Out-Null
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 150
    [ExplorerDropInput]::Button($false); $mouseDown=$false
    Write-Output 'Real Explorer drag delivered to the owned application HWND. The caller must verify parser results.'
} finally {
    if ($mouseDown) { [ExplorerDropInput]::Button($false) }
    [ExplorerDropInput]::SetCursorPos($point.X,$point.Y) | Out-Null
    if ($explorer -and $before -notcontains [long]$explorer.HWND) { $explorer.Quit() }
    [ExplorerDropInput]::SetWindowPos($owner.MainWindowHandle, [IntPtr](-2), 0, 0, 0, 0, 0x0013) | Out-Null
    [ExplorerDropInput]::SetThreadDpiAwarenessContext($dpi) | Out-Null
}
