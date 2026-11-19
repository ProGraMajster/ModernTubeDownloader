param([Parameter(Mandatory)][string]$Manifest, [Parameter(Mandatory)][string]$Evidence, [Parameter(Mandatory)][int]$MaximumSeconds)
$ErrorActionPreference = 'Stop'
# Separate process: still terminates only the two recorded children if the sampler
# is stopped, or if a synchronous native UIA call cannot return to its caller.
$owned = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$clock = [Diagnostics.Stopwatch]::StartNew(); $started = [DateTime]::UtcNow
function Get-Owned($identity) {
    try {
        $process = Get-Process -Id $identity.pid -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq [long]$identity.startUtcTicks -and
            [IO.Path]::GetFullPath($process.Path) -eq [IO.Path]::GetFullPath($identity.path)) { return $process }
    } catch {
        # A process can exit between lookup and StartTime/Path access. Failure to
        # prove its identity never authorizes stopping an unknown replacement.
    }
    return $null
}
$reason = $null
while (-not (Test-Path -LiteralPath (Join-Path $Evidence 'watchdog.disarm'))) {
    if (-not (Get-Owned $owned.parent)) { $reason = 'Owning sampler exited without disarming watchdog'; break }
    if ($clock.Elapsed.TotalSeconds -ge $MaximumSeconds -or ([DateTime]::UtcNow - $started).TotalSeconds -ge $MaximumSeconds) {
        $reason = 'Independent maximum-duration deadline exceeded'; break
    }
    Start-Sleep -Milliseconds 500
}
$stopped = @(); $errors = @()
if ($reason) {
    foreach ($identity in @($owned.client, $owned.application)) {
        try {
            $process = Get-Owned $identity
            if ($process) { $process.Kill(); $stopped += $identity.pid }
        } catch { $errors += $_.ToString() }
    }
}
@{ result = $(if ($reason) { 'FAIL' } else { 'DISARMED' }); reason = $reason; stoppedOwnedPids = $stopped; errors = $errors;
    utc = [DateTime]::UtcNow.ToString('O'); elapsedSeconds = $clock.Elapsed.TotalSeconds } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Evidence 'watchdog-result.json') -Encoding UTF8
if ($reason) { exit 1 }
