# Uses canonical semantic actions plus real owned-HWND key input. The external
# WinEvent observer covers transitions between semantic snapshots.
Start-NativePopupWitness
Assert-NativePopupIsolation
$dropdownTrace = Join-Path $dataRoot 'quality-dropdown-native-trace.jsonl'
$qualityHandle = Find-Node 'QualitySelector' $mainRoot
Invoke-NodeAction $qualityHandle $mainRoot 'Focus' | Out-Null
function Inspect-QualityDropdown {
    return (Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
        '--session',$qualityHandle.handle.sessionId,'--node',$qualityHandle.handle.runtimeId)).value
}
function Write-QualityDropdownTrace([string]$stage, $node, [long]$pinned = 0) {
    $width=0; $height=0
    $popup=[MtdAutomationWindowCloser]::FindOwnComboPopup($script:automationProcess.Id,$script:automationMainWindow,
        [IntPtr]::new($pinned),[int]$node.bounds.x,[int]$node.bounds.y,[int]$node.bounds.width,[int]$node.bounds.height,[ref]$width,[ref]$height)
    $native=[MtdAutomationWindowCloser]::NativeFocusSnapshot($script:automationMainWindow)
    $witness=[ordered]@{ timestamp=[DateTimeOffset]::UtcNow.ToString('O'); iteration=$pass; stage=$stage;
        handle=$node.handle; states=$node.states; value=$node.value; availableActions=$node.supportedActions;
        foregroundHwnd=$native[0]; foregroundPid=$native[4]; activeHwnd=$native[1]; nativeFocusHwnd=$native[2]; mouseCaptureHwnd=$native[3];
        expectedPopup=$pinned; visiblePopupHwnd=$popup.ToInt64(); popupWidth=$width; popupHeight=$height }
    $witness | ConvertTo-Json -Depth 6 -Compress | Add-Content -LiteralPath $dropdownTrace -Encoding utf8
    return $witness
}
$initialQuality=Inspect-QualityDropdown
$qualityChoices=@(foreach($id in $initialQuality.childRuntimeIds) {
    $child=(Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
        '--session',$qualityHandle.handle.sessionId,'--node',$id)).value
    if($child.supportedActions -match '\bSelect\b') { $child }
})
if($qualityChoices.Count -lt 2 -or -not @($qualityChoices | Where-Object name -match '1080').Count) { throw 'Quality popup content lacks the expected synthetic selectable formats.' }
$qualityChoices | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'quality-dropdown-content.json') -Encoding utf8
$passes=if($QualityDeactivationSmoke) { 1 } elseif($QualityDropdownSmoke) { $QualityDropdownPasses } else { 1 }
for($pass=1; $pass -le $passes; $pass++) {
    Assert-NativePopupIsolation
    $before=Inspect-QualityDropdown
    [void](Write-QualityDropdownTrace 'before-expand' $before)
    Invoke-NodeAction $before $mainRoot 'Expand' | Out-Null
    $expanded=Inspect-QualityDropdown
    $opened=Write-QualityDropdownTrace 'after-expand' $expanded
    if($expanded.states -notmatch '\bExpanded\b' -or $opened.visiblePopupHwnd -eq 0) { throw "Quality popup did not stay open after Expand; pass=$pass" }
    if($CaptureVisual -and $pass -eq 1) {
        $picture=[Drawing.Bitmap]::new($opened.popupWidth,$opened.popupHeight); $drawing=[Drawing.Graphics]::FromImage($picture)
        try {
            $dc=$drawing.GetHdc()
            try { if(-not [MtdAutomationWindowCloser]::CaptureOwnPopup($script:automationProcess.Id,[IntPtr]::new($opened.visiblePopupHwnd),$dc)) { throw 'Quality own-HWND capture failed.' } }
            finally { $drawing.ReleaseHdc($dc) }
            $picture.Save((Join-Path $dataRoot 'video-quality-dropdown.png'))
        } finally { $drawing.Dispose(); $picture.Dispose() }
    }
    if($QualityDeactivationSmoke) {
        try {
            $probe=[MtdNativePopupWitness]::ActivateProbe()
            $clock=[Diagnostics.Stopwatch]::StartNew()
            do {
                if(-not [MtdNativePopupWitness]::IsWindowVisible([IntPtr]::new($opened.visiblePopupHwnd))) { break }
                [void][MtdNativePopupWitness]::WaitChanged(50)
            } while($clock.ElapsedMilliseconds -lt 5000)
            $deactivated=Inspect-QualityDropdown
            $closed=Write-QualityDropdownTrace 'controlled-deactivation' $deactivated $opened.visiblePopupHwnd
            if($closed.foregroundHwnd -ne $probe.ToInt64() -or $deactivated.states -notmatch '\bCollapsed\b' -or $closed.visiblePopupHwnd -ne 0) {
                throw 'Controlled native deactivation did not dismiss the quality popup while retaining the probe foreground.'
            }
            $events=@(Get-Content -LiteralPath (Join-Path $dataRoot 'native-popup-events.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
            if(-not @($events | Where-Object { $_.event -eq 'Deactivated' -and $_.controlledProbe }).Count) { throw 'Controlled deactivation has no native transition witness.' }
        } finally { [MtdNativePopupWitness]::CloseProbe() }
        Assert-NativePopupIsolation
        continue
    }
    # Arrow input changes the selected item while preserving the open dropdown.
    # The canonical ComboBox keyboard path deliberately suppresses mouse-style close.
    $index=[Array]::IndexOf(@($qualityChoices.name),$expanded.value)
    if($index -lt 0) { throw 'Selected quality is absent from the inspected popup content.' }
    $next=if($index -eq 0) { 1 } else { $index-1 }
    $key=if($index -eq 0) { 40 } else { 38 }
    if(-not [MtdAutomationWindowCloser]::SendOwnedKey($script:automationProcess.Id,$script:automationMainWindow,$key)) { throw 'Owned quality selection key failed.' }
    $selected=Inspect-QualityDropdown
    $selectedWitness=Write-QualityDropdownTrace 'after-key-selection-before-collapse' $selected $opened.visiblePopupHwnd
    Assert-NativePopupIsolation
    if($selected.states -notmatch '\bExpanded\b' -or $selected.value -ne $qualityChoices[$next].name -or
        $selected.handle.runtimeId -ne $expanded.handle.runtimeId -or $selectedWitness.visiblePopupHwnd -ne $opened.visiblePopupHwnd) {
        throw "Quality selection disturbed the popup, identity or expected value before explicit Collapse; pass=$pass"
    }
    Invoke-NodeAction $selected $mainRoot 'Collapse' | Out-Null
    $collapsed=Inspect-QualityDropdown
    $ended=Write-QualityDropdownTrace 'after-explicit-collapse' $collapsed $opened.visiblePopupHwnd
    if($collapsed.states -notmatch '\bCollapsed\b' -or $ended.visiblePopupHwnd -ne 0 -or $collapsed.value -ne $qualityChoices[$next].name) { throw 'Explicit Collapse did not close quality popup and preserve the selected value.' }
}
Assert-NativePopupIsolation
Complete-NativePopupWitness $true
Write-Output "Quality popup PASS: $passes cycles; controlledDeactivation=$QualityDeactivationSmoke; native event and exact HWND witnesses preserved."
