[CmdletBinding()]
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][long]$MainHwnd,
    [Parameter(Mandatory)][string]$DataRoot, [Parameter(Mandatory)][string]$WitnessPath)
# Windows PowerShell external UIA client; never inspects foreign window contents.
$ErrorActionPreference='Stop'
$ownedRoot=[IO.Path]::GetFullPath($DataRoot)
if([IO.Path]::GetFileName($ownedRoot) -notmatch '^mtd-automation-smoke-[a-f0-9]{32}$' -or
    -not $ownedRoot.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase)) { throw 'Not an owned synthetic QA profile.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type -Path $WitnessPath
$main=[IntPtr]::new($MainHwnd)
if([MtdNativePopupWitness]::Owner($main) -ne $ProcessId) { throw 'Tooltip main HWND ownership mismatch.' }
$queue=Get-Content -LiteralPath (Join-Path $ownedRoot 'queue.json') -Raw | ConvertFrom-Json
$first=$queue[0]; $expected=[string]$first.title
$result=[ordered]@{status='FAIL'; targetId=('QueueItem-'+([guid]$first.id).ToString('N')); popup=0; expectedText=$expected}
$observerStarted=$false
try {
    [MtdNativePopupWitness]::Start($ProcessId,$main,(Join-Path $ownedRoot 'tooltip-native-events.jsonl')); $observerStarted=$true
    $root=[Windows.Automation.AutomationElement]::FromHandle($main)
    $card=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$result.targetId))
    if(-not $card) { throw 'Owned queue row missing for tooltip hit-test.' }
    $nameCondition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$expected)
    $typeCondition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text)
    $title=$card.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.AndCondition]::new($nameCondition,$typeCondition))
    if(-not $title) { throw 'Tooltip title text is not exposed inside the expected canonical row.' }
    $bounds=$title.Current.BoundingRectangle
    if($bounds.Width -le 0 -or $bounds.Height -le 0 -or $title.Current.IsOffscreen) { throw 'Tooltip target has no visible geometry.' }
    $x=[int]($bounds.X+[Math]::Min(8,$bounds.Width/2)); $y=[int]($bounds.Y+$bounds.Height/2)
    $hit=[Windows.Automation.AutomationElement]::FromPoint([Windows.Point]::new($x,$y))
    if($hit.Current.ProcessId -ne $ProcessId) { throw 'INTERFERED: tooltip point is covered by another process.' }
    if(($hit.GetRuntimeId() -join ',') -ne ($title.GetRuntimeId() -join ',')) { throw 'Tooltip point does not hit the expected title control.' }
    $result.targetRuntimeId=@($title.GetRuntimeId()); $result.hitRuntimeId=@($hit.GetRuntimeId()); $result.bounds=@{x=$bounds.X;y=$bounds.Y;width=$bounds.Width;height=$bounds.Height}
    if(-not [MtdNativePopupWitness]::HoverOwnPoint($ProcessId,$main,$x,$y)) { throw 'INTERFERED: cannot hover the foreground-owned title point.' }
    $watch=[Diagnostics.Stopwatch]::StartNew(); $tooltip=$null; $popup=[IntPtr]::Zero
    # Preserve the old 800 ms readiness budget. WinEvent wakes the condition
    # check; no fixed sleep and no arbitrary first WS_EX_TOOLWINDOW match.
    do {
        foreach($candidate in [MtdNativePopupWitness]::OwnPopups($ProcessId,$main)) {
            $window=[Windows.Automation.AutomationElement]::FromHandle($candidate)
            if($window.Current.ProcessId -ne $ProcessId) { continue }
            $tipType=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ToolTip)
            $found=$window.FindFirst([Windows.Automation.TreeScope]::Subtree,$tipType)
            if($found -and $found.Current.Name -eq $expected -and -not $found.Current.IsOffscreen) { $tooltip=$found; $popup=$candidate; break }
        }
        if($tooltip) { break }
        [void][MtdNativePopupWitness]::WaitChanged([Math]::Max(1,[Math]::Min(50,800-[int]$watch.ElapsedMilliseconds)))
    } while($watch.ElapsedMilliseconds -lt 800)
    $result.showElapsedMs=$watch.Elapsed.TotalMilliseconds; $result.focusAfterHover=[MtdNativePopupWitness]::FocusSnapshot($main)
    if(-not $tooltip) { throw 'Owned tooltip with the exact title content did not become visible within 800 ms.' }
    $result.popup=$popup.ToInt64(); $result.actualText=$tooltip.Current.Name; $result.tooltipRuntimeId=@($tooltip.GetRuntimeId())
    $rect=New-Object MtdNativePopupWitness+Rect
    if(-not [MtdNativePopupWitness]::GetWindowRect($popup,[ref]$rect)) { throw 'Cannot measure the qualified tooltip HWND.' }
    $bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top); $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc=$graphics.GetHdc()
        try { if(-not [MtdNativePopupWitness]::PrintWindow($popup,$dc,2)) { throw 'Owned tooltip screenshot failed.' } }
        finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save((Join-Path $ownedRoot 'downloads-title-tooltip.png'))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $mainBounds=$root.Current.BoundingRectangle
    if(-not [MtdNativePopupWitness]::HoverOwnPoint($ProcessId,$main,[int]($mainBounds.X+$mainBounds.Width/2),[int]($mainBounds.Y+10))) { throw 'INTERFERED: cannot leave the title within the owned foreground window.' }
    $watch.Restart()
    while([MtdNativePopupWitness]::IsWindowVisible($popup) -and $watch.ElapsedMilliseconds -lt 800) { [void][MtdNativePopupWitness]::WaitChanged(25) }
    $result.closeElapsedMs=$watch.Elapsed.TotalMilliseconds; $result.focusAfterLeave=[MtdNativePopupWitness]::FocusSnapshot($main)
    if([MtdNativePopupWitness]::IsWindowVisible($popup)) { throw 'The exact tooltip HWND remained visible after leaving its target.' }
    if([MtdNativePopupWitness]::Interfered) { throw 'INTERFERED: external foreground change during tooltip qualification.' }
    if([MtdNativePopupWitness]::Failure) { throw ('INCONCLUSIVE: '+[MtdNativePopupWitness]::Failure) }
    $result.status='PASS'
} catch {
    $result.failure=$_.Exception.Message
    $result.status=if([MtdNativePopupWitness]::Interfered -or $result.failure -like 'INTERFERED:*') { 'INTERFERED' } elseif($result.failure -like 'INCONCLUSIVE:*') { 'INCONCLUSIVE' } else { 'FAIL' }
    throw
} finally {
    if($observerStarted) { [MtdNativePopupWitness]::Stop() }
    $lateObserverFailure=$false
    if($result.status -eq 'PASS' -and [MtdNativePopupWitness]::Interfered) {
        $result.status='INTERFERED'; $result.failure='External foreground transition observed before the watcher stopped.'; $lateObserverFailure=$true
    } elseif($result.status -eq 'PASS' -and [MtdNativePopupWitness]::Failure) {
        $result.status='INCONCLUSIVE'; $result.failure=[MtdNativePopupWitness]::Failure; $lateObserverFailure=$true
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ownedRoot 'tooltip-native-result.json') -Encoding utf8
    if($lateObserverFailure) { throw ($result.status + ': ' + $result.failure) }
}
