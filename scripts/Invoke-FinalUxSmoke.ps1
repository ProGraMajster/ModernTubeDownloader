# Dot-sourced by Test-Automation.ps1. Uses its isolated profile and PID-owned HWND.
# Default: native rendering/Win32-message probe. Physical pointer mode is explicit
# and requires a bounded user-approved desktop slot; neither mode is Windows UIA.
$labels = Get-Content -LiteralPath (Join-Path $repo "ModernTubeDownloader/Resources/Languages/$Language.json") -Raw | ConvertFrom-Json
$witness = [Collections.Generic.List[object]]::new()
if ($FinalUxPhysical) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FinalUxPointer {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window,int flags);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    public static bool OwnedPoint(IntPtr window,int x,int y) => GetForegroundWindow()==window && GetAncestor(WindowFromPoint(new Point {X=x,Y=y}),2)==window;
}
'@
    [FinalUxPointer]::SetForegroundWindow($script:automationMainWindow) | Out-Null
    if ([FinalUxPointer]::GetForegroundWindow() -ne $script:automationMainWindow) { throw 'INTERRUPTED: cannot acquire owned foreground window.' }
}
function Capture-LabelStates([string]$key, [string]$name) {
    $snapshot = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--depth','14')
    $found = @($snapshot.nodes | Where-Object { $_.node.name -eq $labels.$key -and $_.node.bounds.width -gt 0 -and $_.node.bounds.height -gt 0 })
    if ($found.Count -ne 1) { throw "Expected one visible label for $key, found $($found.Count)." }
    $node = $found[0].node; $b = $node.bounds
    $x = [int]($b.x + $b.width / 2); $y = [int]($b.y + $b.height / 2)
    Capture-SmokeMain $mainRoot ($name + '-normal')
    $physicalPressed = $false
    try { foreach ($state in @(@('hover',0x0200,$false),@('pressed',0x0201,$true),@('released',0x0202,$false))) {
        if ($FinalUxPhysical) {
            if (-not [FinalUxPointer]::OwnedPoint($script:automationMainWindow,$x,$y)) { throw 'INTERRUPTED: foreground or hit-test owner changed.' }
            [FinalUxPointer]::SetCursorPos($x,$y) | Out-Null
            if ($state[0] -eq 'pressed') { [FinalUxPointer]::mouse_event(2,0,0,0,[UIntPtr]::Zero); $physicalPressed = $true }
            if ($state[0] -eq 'released') { [FinalUxPointer]::mouse_event(4,0,0,0,[UIntPtr]::Zero); $physicalPressed = $false }
        } elseif (-not [MtdAutomationWindowCloser]::SendMouse($script:automationMainWindow, $state[1], $x, $y, $state[2])) { throw 'Owned mouse message failed.' }
        # A synchronous bridge request supplies the UI-thread barrier before the native capture.
        $barrier = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--depth','1')
        if ($barrier.error -ne 'None') { throw 'UI-thread barrier failed.' }
        Capture-SmokeMain $mainRoot ($name + '-' + $state[0])
        if ($FinalUxPhysical -and [FinalUxPointer]::GetForegroundWindow() -ne $script:automationMainWindow) { throw 'INTERRUPTED: foreground changed during capture.' }
        $witness.Add(@{ label=$key; state=$state[0]; bounds=$b; hwnd=$script:automationMainWindow.ToInt64(); processId=$script:automationProcess.Id;
            physicalPointer=[bool]$FinalUxPhysical; foregroundVerified=[bool]$FinalUxPhysical })
    } } finally { if ($physicalPressed) { [FinalUxPointer]::mouse_event(4,0,0,0,[UIntPtr]::Zero) } }
    # Restore pointer presentation without moving the user's physical cursor.
    [MtdAutomationWindowCloser]::SendMouse($script:automationMainWindow,0x0200,0,0,$false) | Out-Null
}
Capture-LabelStates 'Downloads.QueueEmptyTitle' 'downloads-empty'
Capture-LabelStates 'Dashboard.SpeedChart' 'speed-title'
Capture-LabelStates 'Dashboard.DiskSpace' 'disk-label'
Invoke-NodeAction (Find-Node 'NavLive' $mainRoot) $mainRoot 'Invoke' | Out-Null
Capture-LabelStates 'Live.EmptySection' 'live-empty'
Invoke-NodeAction (Find-Node 'NavHelp' $mainRoot) $mainRoot 'Invoke' | Out-Null
Capture-SmokeMain $mainRoot 'help'
Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
Capture-SmokeMain $mainRoot 'settings'
Invoke-NodeAction (Find-Node 'SettingsCategorySearch' $mainRoot) $mainRoot 'SetValue' ([string]$labels.'Settings.Language') | Out-Null
$result = (Wait-Node 'SettingsSearchResult0' $mainRoot 'Enabled').snapshot
Capture-SmokeMain $mainRoot 'settings-language-results'
Invoke-NodeAction $result $mainRoot 'Invoke' | Out-Null
Capture-SmokeMain $mainRoot 'settings-language-highlight'
Invoke-NodeAction (Find-Node 'SettingsSearchClear' $mainRoot) $mainRoot 'Invoke' | Out-Null
$witness | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $dataRoot 'label-state-witness.json')
Write-Output "Final UX native rendering probe completed ($Language/$Theme/$InterfaceMode; profile: $dataRoot). Pixel acceptance requires review."
