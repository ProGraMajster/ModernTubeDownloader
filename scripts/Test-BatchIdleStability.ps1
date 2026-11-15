# Dot-sourced only by Test-Automation.ps1 in its owned synthetic profile.
# Twelve seconds is the observation interval requested for the periodic regression,
# not a readiness delay. Every sample asserts popup/identity and records native state.
$batchStabilityResults = [Collections.Generic.List[object]]::new()
Wait-Node 'BatchImport' $mainRoot 'Enabled' | Out-Null
$batchFixture = Join-Path $dataRoot 'youtube-wiadomosci-prywatne-2026-10-16T15-23-59-449Z-INCOMPLETE.txt'
$uniqueCount = $BatchStabilityCount - 4
$batchInput = @(1..$uniqueCount | ForEach-Object {
    if ($BatchActiveStabilitySmoke) { 'https://example.test/review-transfer/stability{0}?id=stability{0}' -f $_ }
    else { "https://example.test/watch/stability$_" }
})
$batchInput += @($batchInput | Select-Object -First 4)
# Active fixtures need distinct extractor IDs as well as distinct URLs. FakeTool
# intentionally returns its default sample ID when no id query is supplied; the
# canonical queue correctly deduplicates that media instead of admitting it three times.
$batchInput | Set-Content -LiteralPath $batchFixture -Encoding utf8
Invoke-NodeAction (Find-Node 'BatchFilePath' $mainRoot) $mainRoot 'SetValue' $batchFixture | Out-Null
Invoke-NodeAction (Find-Node 'BatchImport' $mainRoot) $mainRoot 'Invoke' | Out-Null
Wait-Node 'BatchEntryTitle1' $mainRoot | Out-Null
Wait-Node 'BatchImport' $mainRoot 'Enabled' | Out-Null
Capture-Dashboard 'batch-stability-parsed'
$saved = @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Batch') -Filter entries.jsonl -Recurse -File)
if ($saved.Count -ne 1) { throw 'Expected exactly one imported session.' }
$entries = @(Get-Content -LiteralPath $saved[0].FullName | ForEach-Object { $_ | ConvertFrom-Json })
if ($entries.Count -ne $BatchStabilityCount -or @($entries | Where-Object status -eq 4).Count -ne 4 -or
    @($entries | Where-Object status -eq 0).Count -ne $uniqueCount) { throw 'The unanalysed fixture does not match the requested count/valid/duplicate/error precondition.' }
$beforeHash = (Get-FileHash -LiteralPath $saved[0].FullName).Hash

function Inspect-StableBatchControl($node) {
    return (Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
        '--session',$node.handle.sessionId,'--node',$node.handle.runtimeId)).value
}
function Get-StableBatchPopup($node, [long]$expectedPopup = 0) {
    if ($node.bounds.coordinateSpace -ne 'Screen') { throw 'Popup identity requires the control physical screen bounds.' }
    $width = 0; $height = 0; $anyWidth = 0; $anyHeight = 0
    $popup = [MtdAutomationWindowCloser]::FindOwnComboPopup($script:automationProcess.Id, $script:automationMainWindow,
        [IntPtr]::new($expectedPopup), [int]$node.bounds.x, [int]$node.bounds.y,
        [int]$node.bounds.width, [int]$node.bounds.height, [ref]$width, [ref]$height)
    $any = [MtdAutomationWindowCloser]::FindOwnPopup($script:automationProcess.Id, $script:automationMainWindow, [ref]$anyWidth, [ref]$anyHeight)
    return @{ hwnd = $popup; pinnedHwnd = $popup.ToInt64(); width = $width; height = $height; expected = $expectedPopup; valid = ($popup -ne [IntPtr]::Zero);
        firstAny = $any.ToInt64(); firstAnyWidth = $anyWidth; firstAnyHeight = $anyHeight }
}
function Observe-BatchPopup([string]$id, [string]$inputKind, [string]$stage = 'idle', [bool]$keepOpen = $false) {
    $node = Find-Node $id $mainRoot
    Invoke-NodeAction $node $mainRoot 'ScrollIntoView' | Out-Null
    Invoke-NodeAction $node $mainRoot 'Focus' | Out-Null
    $node = Find-Node $id $mainRoot
    if ($inputKind -eq 'Mouse') {
        $x = [int]($node.bounds.x + $node.bounds.width - 14); $y = [int]($node.bounds.y + $node.bounds.height / 2)
        if (-not [MtdAutomationWindowCloser]::SendMouse($script:automationMainWindow,0x0201,$x,$y,$true) -or
            -not [MtdAutomationWindowCloser]::SendMouse($script:automationMainWindow,0x0202,$x,$y,$false)) { throw 'Owned HWND mouse input failed.' }
    } else {
        # This framework supports Alt+Down (not F4). Use the semantic Expand
        # contract, then actual owned-window arrow input; no direct field writes.
        Invoke-NodeAction $node $mainRoot 'Expand' | Out-Null
        if (-not [MtdAutomationWindowCloser]::SendOwnedKey($script:automationProcess.Id,$script:automationMainWindow,0x28)) { throw 'Owned HWND Down failed.' }
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $first = $null; $failures = [Collections.Generic.List[string]]::new(); $samples = 0
    do {
        $detail = Inspect-StableBatchControl $node
        $expected = if ($null -eq $first) { 0 } else { [long]$first.popup }
        $popupIdentity = Get-StableBatchPopup $detail $expected
        $popup = $popupIdentity.hwnd; $w = $popupIdentity.width; $h = $popupIdentity.height
        $native = [MtdAutomationWindowCloser]::NativeFocusSnapshot($script:automationMainWindow)
        $script:automationProcess.Refresh()
        $witness = @{ id=$id; input=$inputKind; stage=$stage; elapsedMs=$clock.Elapsed.TotalMilliseconds; node=$detail;
            popup=$popup.ToInt64(); width=$w; height=$h; popupIdentity=$popupIdentity; foreground=$native[0]; active=$native[1]; focus=$native[2];
            privateBytes=$script:automationProcess.PrivateMemorySize64; workingSet=$script:automationProcess.WorkingSet64 }
        $witness | ConvertTo-Json -Depth 12 -Compress | Add-Content -LiteralPath (Join-Path $dataRoot 'batch-idle-witness.jsonl') -Encoding utf8
        if ($null -eq $first) {
            $first = $witness
            if ($popup -ne [IntPtr]::Zero -and $w -gt 0 -and $h -gt 0) {
                $bmp=[Drawing.Bitmap]::new($w,$h); $g=[Drawing.Graphics]::FromImage($bmp)
                try { $dc=$g.GetHdc(); try { if (-not [MtdAutomationWindowCloser]::CaptureOwnPopup($script:automationProcess.Id,$popup,$dc)) { $failures.Add('Popup disappeared before its HWND capture.') } } finally { $g.ReleaseHdc($dc) }; $bmp.Save((Join-Path $dataRoot ($stage+'-'+$id+'-popup.png'))) } finally { $g.Dispose(); $bmp.Dispose() }
            }
            Capture-Dashboard ($stage + '-' + $id + '-opened')
        }
        if ($detail.states -notmatch '\bExpanded\b' -or $popup -eq [IntPtr]::Zero) { $failures.Add("Popup collapsed at $([int]$clock.ElapsedMilliseconds) ms") }
        if ($detail.handle.runtimeId -ne $first.node.handle.runtimeId -or $detail.value -ne $first.node.value -or
            $popup.ToInt64() -ne $first.popup) { $failures.Add("Identity/value/popup changed at $([int]$clock.ElapsedMilliseconds) ms") }
        $samples++
        if ($clock.Elapsed.TotalSeconds -lt 12) { Start-Sleep -Milliseconds 250 }
    } while ($clock.Elapsed.TotalSeconds -lt 12)
    $script:batchObservedPopup = $first
    $result = @{ id=$id; input=$inputKind; stage=$stage; seconds=$clock.Elapsed.TotalSeconds; samples=$samples;
        observationPassed=($failures.Count -eq 0); passed=($failures.Count -eq 0); failures=@($failures); cleanup='Pending' }
    $batchStabilityResults.Add($result)
    # Commit observations before capture/cleanup can fail. Never discard an already
    # recorded popup failure because a later Collapse races window deactivation.
    $batchStabilityResults | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'batch-idle-results.json') -Encoding utf8
    try {
        Capture-Dashboard ($stage + '-' + $id + '-after-12s')
        $atEnd = Find-Node $id $mainRoot
        $result.cleanup = if ($keepOpen) { 'KeptOpen' } else { 'AlreadyCollapsed' }
        if (-not $keepOpen -and $atEnd.states -match '\bExpanded\b') {
            $collapse = Invoke-AutomationCli -CliArguments @('action', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $atEnd.handle.sessionId, '--node', $atEnd.handle.runtimeId, '--action', 'Collapse') -AllowedSemanticErrors @('ActionUnsupported')
            if ($collapse.error -eq 'ActionUnsupported') {
                # One inspection only, never another action or a focus-restoration attempt.
                $confirmed = Inspect-StableBatchControl $atEnd
                $identity = Get-StableBatchPopup $confirmed ([long]$first.popup)
                $native = [MtdAutomationWindowCloser]::NativeFocusSnapshot($script:automationMainWindow)
                @{ id=$id; stage=$stage; elapsedMs=$clock.Elapsed.TotalMilliseconds; error=$collapse.error; before=$atEnd; after=$confirmed;
                    popupIdentity=$identity; foreground=$native[0]; active=$native[1]; focus=$native[2] } |
                    ConvertTo-Json -Depth 12 -Compress | Add-Content -LiteralPath (Join-Path $dataRoot 'batch-popup-cleanup.jsonl') -Encoding utf8
                $failures.Add('Cleanup Collapse raced a state change (ActionUnsupported); original observation and this failure are retained.')
                if ($confirmed.states -notmatch '\bCollapsed\b' -or $identity.valid) {
                    throw 'Unsupported cleanup Collapse was not explained by an already collapsed, absent pinned popup.'
                }
                $result.cleanup = 'ConfirmedCollapsedAfterActionUnsupported'
            } else { $result.cleanup = 'CollapsedExplicitly' }
        }
    }
    catch {
        $failures.Add('Capture or cleanup failed: ' + $_.Exception.Message)
        $result.cleanup = 'Failed'
        throw
    }
    finally {
        $result.seconds = $clock.Elapsed.TotalSeconds
        $result.passed = $failures.Count -eq 0; $result.failures = @($failures)
        $batchStabilityResults | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'batch-idle-results.json') -Encoding utf8
    }
    Write-Output ($result | ConvertTo-Json -Depth 4 -Compress)
}

if ($BatchActiveStabilitySmoke) {
    . (Join-Path $PSScriptRoot 'Test-BatchActiveStability.ps1')
    return
}
Observe-BatchPopup 'BatchWorkflowMode' 'Mouse'
Observe-BatchPopup 'BatchFilter' 'Keyboard'
# A second independent import makes session selection actionable.
$second = Join-Path $dataRoot 'second-list.txt'
'https://example.test/watch/second-list' | Set-Content -LiteralPath $second -Encoding utf8
Invoke-NodeAction (Find-Node 'BatchFilePath' $mainRoot) $mainRoot 'SetValue' $second | Out-Null
Invoke-NodeAction (Find-Node 'BatchImport' $mainRoot) $mainRoot 'Invoke' | Out-Null
Wait-Node 'BatchSessionChoice' $mainRoot 'Enabled' | Out-Null
Observe-BatchPopup 'BatchSessionChoice' 'Mouse'
Invoke-NodeAction (Find-Node 'BatchOptions' $mainRoot) $mainRoot 'Invoke' | Out-Null
Observe-BatchPopup 'BatchQuality' 'Keyboard'
Observe-BatchPopup 'BatchContainer' 'Mouse'
# Return to the first remembered list through keyboard navigation, then exercise
# the actual page/filter/search controls without touching persisted entries.
Invoke-NodeAction (Find-Node 'BatchSessionChoice' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
Invoke-NodeAction (Find-Node 'BatchSessionChoice' $mainRoot) $mainRoot 'Focus' | Out-Null
Invoke-NodeAction (Find-Node 'BatchSessionChoice' $mainRoot) $mainRoot 'Expand' | Out-Null
if (-not [MtdAutomationWindowCloser]::SendOwnedKey($script:automationProcess.Id,$script:automationMainWindow,0x26) -or
    -not [MtdAutomationWindowCloser]::SendOwnedKey($script:automationProcess.Id,$script:automationMainWindow,0x0D)) { throw 'Owned session keyboard selection failed.' }
Invoke-NodeAction (Find-Node 'BatchFilter' $mainRoot) $mainRoot 'Focus' | Out-Null
Invoke-NodeAction (Find-Node 'BatchFilter' $mainRoot) $mainRoot 'Expand' | Out-Null
if (-not [MtdAutomationWindowCloser]::SendOwnedKey($script:automationProcess.Id,$script:automationMainWindow,0x26) -or
    -not [MtdAutomationWindowCloser]::SendOwnedKey($script:automationProcess.Id,$script:automationMainWindow,0x0D)) { throw 'Owned filter keyboard selection failed.' }
Wait-Node 'BatchEntryTitle20' $mainRoot | Out-Null
Invoke-NodeAction (Find-Node 'BatchSearch' $mainRoot) $mainRoot 'SetValue' "stability$uniqueCount" | Out-Null
Wait-Node "BatchEntryTitle$uniqueCount" $mainRoot | Out-Null
Capture-Dashboard 'batch-last-entry-search'
Invoke-NodeAction (Find-Node 'BatchSearch' $mainRoot) $mainRoot 'SetValue' '' | Out-Null
Invoke-NodeAction (Find-Node 'BatchNext' $mainRoot) $mainRoot 'Invoke' | Out-Null
Wait-Node 'BatchEntryTitle21' $mainRoot | Out-Null
Capture-Dashboard 'batch-second-page'
Invoke-NodeAction (Find-Node 'BatchPrevious' $mainRoot) $mainRoot 'Invoke' | Out-Null
Wait-Node 'BatchEntryTitle1' $mainRoot | Out-Null
if ((Get-FileHash -LiteralPath $saved[0].FullName).Hash -ne $beforeHash) { throw 'Passive browsing modified the original entries journal.' }
if (@($batchStabilityResults | Where-Object { -not $_.passed }).Count -gt 0) { throw "Batch popup stability FAIL. Original witnesses/screens/profile preserved: $dataRoot" }
