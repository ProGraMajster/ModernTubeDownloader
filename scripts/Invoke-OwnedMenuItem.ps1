param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$Caption)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OwnedMenuInput {
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static bool Click(int processId, int window, int x, int y) {
        var hwnd = new IntPtr(window); uint owner; GetWindowThreadProcessId(hwnd, out owner);
        if (owner != processId) return false;
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            var point = new Point { X = x, Y = y };
            if (!ScreenToClient(hwnd, ref point)) return false;
            var packed = new IntPtr((ushort)point.X | ((long)(ushort)point.Y << 16));
            return PostMessage(hwnd, 0x0200, IntPtr.Zero, packed) && PostMessage(hwnd, 0x0201, new IntPtr(1), packed) && PostMessage(hwnd, 0x0202, IntPtr.Zero, packed);
        } finally { SetThreadDpiAwarenessContext(previous); }
    }
}
'@
$condition = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Caption)))
$processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
$watch = [Diagnostics.Stopwatch]::StartNew()
do {
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
    foreach ($window in $windows) {
        $item = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $item) { continue }
        if ($item.Current.ProcessId -ne $ProcessId -or -not $item.Current.IsEnabled -or $item.Current.IsOffscreen) { throw 'Menu item is not available in the owned process.' }
        Write-Output ('Owned native UIA MenuItem: ' + $item.Current.Name + '; bounds=' + $item.Current.BoundingRectangle)
        # MFN's popup UIA Invoke currently rejects this attached menu item. Exercise
        # its actual native mouse route using UIA geometry, without moving the cursor.
        $bounds = $item.Current.BoundingRectangle
        $nativeOwner = $item
        while ($null -ne $nativeOwner -and $nativeOwner.Current.NativeWindowHandle -eq 0) { $nativeOwner = [System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($nativeOwner) }
        if ($null -eq $nativeOwner -or $nativeOwner.Current.ProcessId -ne $ProcessId) { throw 'Menu has no owned native ancestor.' }
        Write-Output ('Menu HWND=' + $nativeOwner.Current.NativeWindowHandle + '; bounds=' + $nativeOwner.Current.BoundingRectangle)
        if (-not [OwnedMenuInput]::Click($ProcessId, $nativeOwner.Current.NativeWindowHandle, [int]($bounds.X+$bounds.Width/2), [int]($bounds.Y+$bounds.Height/2))) { throw 'Owned menu HWND input failed.' }
        Write-Output 'Dispatched owned-HWND menu click; caller must verify the resulting analysis.'
        return
    }
    Start-Sleep -Milliseconds 100
} while ($watch.Elapsed.TotalSeconds -lt 5)
throw ('Owned native UIA menu item unavailable: ' + $Caption)
