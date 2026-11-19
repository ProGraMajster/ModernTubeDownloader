param(
    [Parameter(Mandatory)][string[]]$EvidenceDirectories,
    [Parameter(Mandatory)][string]$OutputPath,
    [string[]]$BaselineDirectories = @()
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 is required for read-only evidence aggregation.' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Use a fresh output path; original summaries are retained.' }

function Read-Json([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
}
function Instant($value) {
    if ($value -is [DateTimeOffset]) { return $value }
    if ($value -is [DateTime]) { return [DateTimeOffset]$value }
    return [DateTimeOffset]::Parse([string]$value, [Globalization.CultureInfo]::InvariantCulture)
}
function Distribution($values) {
    $sorted = @($values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if (-not $sorted.Count) { return @{ count = 0; median = $null; p95 = $null; min = $null; max = $null } }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return @{ count = $sorted.Count; median = $median; p95 = $sorted[[int][Math]::Ceiling($sorted.Count * .95) - 1]; min = $sorted[0]; max = $sorted[-1] }
}
function Summarize([string]$directory, [string]$role) {
    $directory = [IO.Path]::GetFullPath($directory)
    $metricsFile = Join-Path $directory 'process-metrics.json'
    $nativeFile = Join-Path $directory 'native-result.json'
    $qualificationFile = Join-Path $directory 'qualification.json'
    $captureFile = Join-Path $directory 'startup-manifest.json'
    $metrics = Read-Json $metricsFile; $native = Read-Json $nativeFile; $qualification = Read-Json $qualificationFile; $capture = Read-Json $captureFile
    if (-not $metrics -or -not $native -or -not $qualification) { throw "Incomplete existing performance evidence: $directory" }
    $files = @(Get-ChildItem -LiteralPath $directory -File -Filter 'performance-*.jsonl')
    if ($files.Count -ne 1) { throw "Expected one native process diagnostic stream: $directory" }
    $rows = [Collections.Generic.List[object]]::new()
    $line = 0
    foreach ($raw in [IO.File]::ReadLines($files[0].FullName)) {
        $line++
        try { $rows.Add(($raw | ConvertFrom-Json -AsHashtable)) }
        catch { throw "Invalid original diagnostics at $($files[0].FullName):$line : $_" }
    }
    $pids = @($rows | ForEach-Object { $_.pid } | Select-Object -Unique)
    if ($pids.Count -ne 1 -or $pids[0] -ne $native.pid) { throw "Diagnostic process identity mismatch: $directory" }
    $counterPeaks = @{}; $counterLast = @{}
    foreach ($row in $rows) {
        foreach ($counter in $row.counters) {
            $key = [string]$counter.Key; $value = [double]$counter.Value
            if (-not $counterPeaks.ContainsKey($key) -or $value -gt $counterPeaks[$key]) { $counterPeaks[$key] = $value }
            $counterLast[$key] = $value
        }
    }
    $processPeaks = @{}
    foreach ($key in @('privateBytes','workingSet','handles','threads','cpuMs')) {
        $processPeaks[$key] = (Distribution ($metrics.samples | ForEach-Object { $_[$key] })).max
    }
    $timings = @{}
    foreach ($group in ($rows | Where-Object { $null -ne $_.elapsedMs } | Group-Object { $_.name })) {
        $timings[$group.Name] = Distribution ($group.Group | ForEach-Object { $_.elapsedMs })
    }
    $uia = @{}
    foreach ($group in ($native.steps | Where-Object { $null -ne $_.ms } | Group-Object { if ($_.action -like 'PlaylistGroupToggle-*') { 'PlaylistGroupToggle' } else { $_.action } })) {
        $uia[$group.Name] = @{ totalMs = (Distribution ($group.Group | ForEach-Object { $_.ms }));
            discoveryMs = (Distribution ($group.Group | ForEach-Object { $_.findMs }));
            invokeMs = (Distribution ($group.Group | ForEach-Object { $_.invokeMs }));
            patternMs = (Distribution ($group.Group | ForEach-Object { $_.patternMs })) }
    }
    $entry = $rows | Where-Object { $_.name -eq 'process.entry' } | Select-Object -First 1
    $milestones = @{}
    foreach ($name in @('ui.first-paint','ui.input-ready','process.stop')) {
        $row = $rows | Where-Object { $_.name -eq $name } | Select-Object -First 1
        $milestones[$name] = if ($entry -and $row) { ((Instant $row.utc) - (Instant $entry.utc)).TotalMilliseconds } else { $null }
    }
    $persistedDifferences = @()
    foreach ($key in @($metrics.before.Keys) + @($metrics.after.Keys) | Select-Object -Unique) {
        if ($metrics.before[$key] -ne $metrics.after[$key]) { $persistedDifferences += $key }
    }
    $firstGroup = $native.steps | Where-Object { $_.action -like 'PlaylistGroupToggle-*' } | Select-Object -First 1
    $sourceFiles = @($metricsFile,$nativeFile,$qualificationFile,$files[0].FullName)
    if ($capture) { $sourceFiles += $captureFile }
    return @{
        role = $role; evidenceDirectory = $directory; result = $qualification.result; nativeResult = $native.result;
        failures = $qualification.failures; nativeError = $native.error; safetyReason = $metrics.reason;
        binary = $metrics.binary; applicationSha256 = $metrics.sha256; managedSha256 = $metrics.managedSha256; nativeScriptSha256 = $metrics.nativeScriptSha256;
        buildConfiguration = $(if ($metrics.binary -match '[\\/]bin[\\/]([^\\/]+)[\\/]') { $Matches[1] } else { 'NOT_RECORDED' });
        executionContext = 'Local Windows native evidence; not a CI execution'; pid = $native.pid; hwnd = $native.hwnd;
        startedUtc = $entry.utc; diagnosticRecordCount = $rows.Count; processSampleCount = @($metrics.samples).Count;
        scenario = $metrics.scenario; reusePager = $metrics.reusePager; fixture = $metrics.fixture;
        geometry = $capture; fixtureHashes = $metrics.before; persistenceDifferences = $persistedDifferences;
        processPeaks = $processPeaks; diagnosticCounterPeaks = $counterPeaks; finalDiagnosticCounters = $counterLast;
        startup = @{ hwndFromHarnessStartMs = $metrics.hwndMs; milestonesFromProcessEntryMs = $milestones };
        applicationEventLatencyMs = $timings; nativeClientLatency = $uia; firstPlaylistToggle = $firstGroup;
        sourceFiles = @($sourceFiles | ForEach-Object { @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash } })
    }
}
$runs = @()
foreach ($directory in $EvidenceDirectories) { $runs += Summarize $directory 'current' }
foreach ($directory in $BaselineDirectories) { $runs += Summarize $directory 'baseline' }
$comparison = @{ status = 'NO_PAIRED_BASELINE'; differences = @(); deltaPercent = $null;
    reason = 'Single current evidence is not a before/after comparison. No improvement claim is inferred.' }
if ($EvidenceDirectories.Count -eq 1 -and $BaselineDirectories.Count -eq 1) {
    $current = $runs[0]; $baseline = $runs[1]; $differences = @()
    foreach ($key in @('scenario','reusePager','buildConfiguration')) {
        if ($current[$key] -ne $baseline[$key]) { $differences += @{ field = $key; current = $current[$key]; baseline = $baseline[$key] } }
    }
    foreach ($key in @('width','height','dpi')) {
        if (-not $current.geometry -or -not $baseline.geometry -or $current.geometry[$key] -ne $baseline.geometry[$key]) {
            $differences += @{ field = 'geometry.' + $key;
                current = $(if ($current.geometry) { $current.geometry[$key] } else { $null });
                baseline = $(if ($baseline.geometry) { $baseline.geometry[$key] } else { $null }) }
        }
    }
    foreach ($key in @('history.json','queue.json','live-sessions.json')) {
        if ($current.fixtureHashes[$key] -ne $baseline.fixtureHashes[$key]) {
            $differences += @{ field = 'fixture.' + $key; current = $current.fixtureHashes[$key]; baseline = $baseline.fixtureHashes[$key] }
        }
    }
    if ($current.diagnosticCounterPeaks['render.auditAvailable'] -ne $baseline.diagnosticCounterPeaks['render.auditAvailable']) {
        $differences += @{ field = 'render.auditAvailable'; current = $current.diagnosticCounterPeaks['render.auditAvailable']; baseline = $baseline.diagnosticCounterPeaks['render.auditAvailable'] }
    }
    $comparison = @{ status = $(if ($differences.Count) { 'RECORDED_CONDITIONS_DIFFER' } else { 'RECORDED_CONDITIONS_MATCH' }); differences = $differences; deltaPercent = $null;
        reason = 'Side-by-side only: machine load, fixture image hashes and complete harness arguments are not fully recorded, so no causal improvement or CI equivalence is asserted.' }
}
@{
    schemaVersion = 1; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); operation = 'Read-only aggregation of existing evidence; no tests executed';
    percentileMethod = 'Median averages the middle pair for even counts; p95 uses nearest rank ceil(0.95*N). No outliers removed.';
    units = 'All latency values milliseconds; memory bytes; counts unitless.'; runs = $runs; comparison = $comparison;
    ciComparison = @{ status = 'NOT_PROVIDED'; reason = 'Native local runs are distinct from Debug/Release CI. No CI result, timing equivalence, exact-head match or remote PASS is inferred from these files.' };
    caveats = @(
        'Process private bytes are total committed process memory; managed bytes are GC.GetTotalMemory snapshots. Subtracting them is not a measured native heap size.',
        'nativeBitmapBytes covers tracked thumbnail allocations; renderer back buffers are separately tracked by render.bufferBytes. Neither is the complete native allocation footprint.',
        'Renderer and control counters may remain at their last live audit during shutdown. Final native bitmap/cache release is evaluated independently by qualification.json.',
        'UIA total/find/pattern/invoke durations and application history.page.render durations are different measurements; they are never pooled.',
        'PlaylistGroupToggle aggregate mixes expansion and collapse. firstPlaylistToggle retains the unaggregated first action.',
        'A counter peak is the maximum recorded sample, not a continuous high-water mark. Missing counters stay absent; absent timings have count zero/null.',
        'Original FAIL and safety reasons are retained. Summary generation does not change product qualification.'
    )
} | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output $OutputPath
