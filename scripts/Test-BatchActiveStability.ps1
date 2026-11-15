# Called from the idle harness after a real 122-row import. Three selected entries
# traverse the existing metadata worker, canonical queue and file pipeline.
function Read-StabilityEntries {
    $map = @{}
    foreach ($line in Get-Content -LiteralPath $saved[0].FullName) { $e=$line|ConvertFrom-Json; $map[$e.id]=$e }
    $updates = Join-Path $saved[0].DirectoryName 'updates.jsonl'
    if (Test-Path -LiteralPath $updates) {
        foreach ($line in Get-Content -LiteralPath $updates) {
            if (-not $line) { continue }; $e=$line|ConvertFrom-Json
            if (-not $map.ContainsKey($e.id) -or $e.ordinal -ne $map[$e.id].ordinal -or $e.revision -lt $map[$e.id].revision) { throw 'Invalid Batch durable identity/revision.' }
            $map[$e.id]=$e
        }
    }
    return @($map.Values | Sort-Object ordinal)
}
function Wait-Stability([scriptblock]$condition,[string]$description) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do { if (& $condition) { return }; Start-Sleep -Milliseconds 100 } while ($watch.Elapsed.TotalSeconds -lt 30)
    throw "Batch state did not settle: $description"
}
foreach ($ordinal in 1..3) { Invoke-NodeAction (Find-Node "BatchSelect$ordinal" $mainRoot) $mainRoot 'Toggle' | Out-Null }
$beforeRows = @(1..3 | ForEach-Object { (Find-Node "BatchEntryTitle$_" $mainRoot).handle.runtimeId })
[IO.File]::WriteAllText((Join-Path $batchMetadataGate 'request-prefix.txt'), $batchInput[0])
Invoke-NodeAction (Find-Node 'BatchAnalyze' $mainRoot) $mainRoot 'Invoke' | Out-Null
Wait-Node 'BatchPause' $mainRoot 'Enabled' | Out-Null
Wait-Stability { @(Get-ChildItem -LiteralPath $batchMetadataGate -Filter '*.ready.json').Count -gt 0 } 'owned metadata process reached its gate'
Observe-BatchPopup 'BatchFilter' 'Mouse' 'analysis' $true
if ((Find-Node 'BatchPause' $mainRoot).states -match 'Unavailable') { throw 'Analysis ceased before its active-popup observation completed.' }
[IO.File]::WriteAllText((Join-Path $batchMetadataGate 'release'),'observed')
Wait-Node 'BatchAnalyze' $mainRoot 'Enabled' | Out-Null
$analyzed=Read-StabilityEntries
if (@($analyzed | Where-Object { $_.status -eq 3 -and $_.hasMetadata }).Count -ne 3) { throw 'Three selected rows did not persist Ready metadata.' }
$afterCompletion = Inspect-StableBatchControl (Find-Node 'BatchFilter' $mainRoot)
$popupIdentity = Get-StableBatchPopup $afterCompletion ([long]$script:batchObservedPopup.popup)
$popup = $popupIdentity.hwnd
if ($afterCompletion.states -notmatch '\bExpanded\b' -or $afterCompletion.handle.runtimeId -ne $script:batchObservedPopup.node.handle.runtimeId -or
    $afterCompletion.value -ne $script:batchObservedPopup.node.value -or $popup.ToInt64() -ne $script:batchObservedPopup.popup) { throw 'Analysis completion disturbed the open popup/control/value.' }
@{ before=$script:batchObservedPopup; after=$afterCompletion; afterPopup=$popup.ToInt64(); popupIdentity=$popupIdentity } | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $dataRoot 'batch-analysis-completion-popup.json') -Encoding utf8
Capture-Dashboard 'batch-analysis-completed-open'
Invoke-NodeAction (Find-Node 'BatchFilter' $mainRoot) $mainRoot 'Collapse' | Out-Null
$afterRows = @(1..3 | ForEach-Object { (Find-Node "BatchEntryTitle$_" $mainRoot).handle.runtimeId })
if (($beforeRows -join ',') -ne ($afterRows -join ',')) { throw 'Metadata progress rebuilt retained visible row controls.' }
foreach ($ordinal in 1..3) { if ((Find-Node "BatchSelect$ordinal" $mainRoot).states -notmatch 'Checked') { throw 'Analysis lost a checked entry.' } }
Observe-BatchPopup 'BatchFilter' 'Mouse' 'analysis-complete'
Invoke-NodeAction (Find-Node 'BatchAdd' $mainRoot) $mainRoot 'Invoke' | Out-Null
Wait-Stability { @((Read-StabilityEntries) | Where-Object { $_.queueItemId }).Count -eq 3 } 'three entries admitted to canonical queue'
$linkedIds = @((Read-StabilityEntries) | Where-Object queueItemId | ForEach-Object queueItemId)
# DownloadStatus: 2=DownloadingVideo, 3=DownloadingAudio, 4=Merging, 5=Finalizing.
# Admission alone does not prove that this observation exercises active transfer.
Wait-Stability {
    $jobs = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
    @($jobs | Where-Object { $_.id -in $linkedIds -and $_.status -in @(2, 3, 4, 5) }).Count -gt 0
} 'at least one linked canonical job is actively transferring or finalizing'
$transferStart = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
if (@($transferStart | Where-Object { $_.id -in $linkedIds -and $_.status -in @(2, 3, 4, 5) }).Count -eq 0) {
    throw 'Active-transfer precondition ended before popup observation began.'
}
$transferStart | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $dataRoot 'batch-active-transfer-precondition.json') -Encoding utf8
# Keep the genuine byte-producing child at its explicit gate during observation.
# Each later video/audio subprocess gets its own gate, released only after the
# observation interval. Serial queue admission therefore adds no artificial delay.
$transferRoot = [IO.Path]::GetFullPath((Join-Path $dataRoot 'temp'))
$transferRootPrefix = $transferRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Wait-Stability {
    @(Get-ChildItem -LiteralPath $transferRoot -Filter '.review-transfer-ready' -Recurse -File).Count -gt 0
} 'an owned transfer reached its byte-progress gate'
Observe-BatchPopup 'BatchFilter' 'Mouse' 'downloading' $true
$transferPopup = $script:batchObservedPopup
$drainClock = [Diagnostics.Stopwatch]::StartNew()
$drainWitness = @{ last = $null; samples = 0 }
Wait-Stability {
    # Native reads only: no CLI process, tree scan, input or focus restoration in
    # this bounded drain loop. Persist changes so deactivation remains visible.
    $native = [MtdAutomationWindowCloser]::NativeFocusSnapshot($script:automationMainWindow)
    $identity = Get-StableBatchPopup $transferPopup.node ([long]$transferPopup.popup)
    $drainWitness.samples++
    $state = @($native[0], $native[1], $native[2], $native[3], $identity.pinnedHwnd,
        $identity.valid, $identity.width, $identity.height, $identity.firstAny) -join '|'
    if ($state -ne $drainWitness.last) {
        @{ elapsedMs=$drainClock.Elapsed.TotalMilliseconds; sample=$drainWitness.samples;
            foreground=$native[0]; active=$native[1]; focus=$native[2]; capture=$native[3];
            expectedPopup=$transferPopup.popup; popup=$identity.pinnedHwnd; popupValid=$identity.valid;
            width=$identity.width; height=$identity.height; firstAny=$identity.firstAny } |
            ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $dataRoot 'batch-transfer-drain.jsonl') -Encoding utf8
        $drainWitness.last = $state
    }
    foreach ($barrier in Get-ChildItem -LiteralPath $transferRoot -Filter '.review-transfer-ready' -Recurse -File) {
        $release = [IO.Path]::GetFullPath((Join-Path $barrier.DirectoryName '.review-transfer-release'))
        if (-not $release.StartsWith($transferRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A transfer release path escaped the owned synthetic temp directory.'
        }
        if (-not (Test-Path -LiteralPath $release)) {
            [IO.File]::WriteAllText($release, 'observed')
            @{ ready=$barrier.FullName; release=$release; utc=[DateTimeOffset]::UtcNow.ToString('O') } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $dataRoot 'batch-transfer-gates.jsonl') -Encoding utf8
        }
    }
    @((Read-StabilityEntries) | Where-Object status -eq 9).Count -eq 3
} 'three real FakeTool downloads settled after releasing their byte-progress gates'
$afterTransfer = Inspect-StableBatchControl (Find-Node 'BatchFilter' $mainRoot)
$transferPopupIdentity = Get-StableBatchPopup $afterTransfer ([long]$transferPopup.popup)
$afterTransferNative = [MtdAutomationWindowCloser]::NativeFocusSnapshot($script:automationMainWindow)
@{ before=$transferPopup; after=$afterTransfer; popupIdentity=$transferPopupIdentity;
    native=@{ foreground=$afterTransferNative[0]; active=$afterTransferNative[1]; focus=$afterTransferNative[2]; capture=$afterTransferNative[3] };
    drainSamples=$drainWitness.samples; drainElapsedMs=$drainClock.Elapsed.TotalMilliseconds } | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $dataRoot 'batch-transfer-completion-popup.json') -Encoding utf8
$completionFailures = [Collections.Generic.List[string]]::new()
if ($afterTransfer.states -notmatch '\bExpanded\b' -or $afterTransfer.handle.runtimeId -ne $transferPopup.node.handle.runtimeId -or
    $afterTransfer.value -ne $transferPopup.node.value -or $transferPopupIdentity.hwnd.ToInt64() -ne $transferPopup.popup) {
    $completionFailures.Add('Download completion disturbed the open popup/control/value.')
}
$settled=Read-StabilityEntries
$queue=@(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
$completedJobs = @($queue | Where-Object { $_.id -in $linkedIds })
if ($completedJobs.Count -ne 3 -or @($completedJobs.id | Sort-Object -Unique).Count -ne 3 -or
    @($completedJobs.finalFile | Sort-Object -Unique).Count -ne 3) { $completionFailures.Add('Three transfers did not retain distinct canonical IDs and final paths.') }
foreach ($entry in $settled | Where-Object queueItemId) {
    $jobs=@($queue | Where-Object id -eq $entry.queueItemId)
    if ($jobs.Count -ne 1 -or $jobs[0].status -ne 6 -or [string]::IsNullOrWhiteSpace($jobs[0].finalFile) -or
        -not (Test-Path -LiteralPath $jobs[0].finalFile)) { $completionFailures.Add('A completed Batch entry has no unique completed canonical file.') }
}
$afterHash = (Get-FileHash -LiteralPath $saved[0].FullName).Hash
if ($afterHash -ne $beforeHash) { $completionFailures.Add('The immutable import journal changed during transfer.') }
$settled | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'batch-active-durable.json') -Encoding utf8
@{ passed=($completionFailures.Count -eq 0); failures=@($completionFailures); linkedIds=$linkedIds;
    completedJobs=$completedJobs; beforeJournalHash=$beforeHash; afterJournalHash=$afterHash } | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath (Join-Path $dataRoot 'batch-active-completion-validation.json') -Encoding utf8
Capture-Dashboard 'batch-active-completed-open'
if ($completionFailures.Count -gt 0) { throw ($completionFailures -join ' ') }
Invoke-NodeAction (Find-Node 'BatchFilter' $mainRoot) $mainRoot 'Collapse' | Out-Null
Capture-Dashboard 'batch-active-completed'
if (@($batchStabilityResults | Where-Object { -not $_.passed }).Count -gt 0) { throw 'Batch active popup stability FAIL; original samples retained.' }
