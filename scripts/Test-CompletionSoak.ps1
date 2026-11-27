param([Parameter(Mandatory)][string]$ApplicationPath, [Parameter(Mandatory)][string]$Fixture,
    [Parameter(Mandatory)][string]$Evidence, [ValidateRange(60,14400)][int]$DurationSeconds = 7200,
    [ValidatePattern('^\d+\.\d+$')][string]$InterfaceChoiceFamily = '')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7 -or -not $IsWindows) { throw 'The owner/sampler requires PowerShell7 on Windows.' }
$ApplicationPath = [IO.Path]::GetFullPath($ApplicationPath); $Fixture = [IO.Path]::GetFullPath($Fixture); $Evidence = [IO.Path]::GetFullPath($Evidence)
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (-not $Fixture.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $Fixture -Leaf) -notlike 'mtd-automation-smoke-*') { throw 'Only an existing isolated synthetic fixture is permitted.' }
if ($Evidence.Equals($Fixture, [StringComparison]::OrdinalIgnoreCase) -or $Evidence.StartsWith($Fixture.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must remain outside the original fixture.' }
if (-not (Test-Path -LiteralPath $ApplicationPath -PathType Leaf) -or (Split-Path $ApplicationPath -Leaf) -ne 'ModernTubeDownloader.exe') { throw 'Expected a specific ModernTubeDownloader.exe.' }
if (Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue) { throw 'An existing app is active; refusing a single-instance collision.' }
if (Test-Path -LiteralPath $Evidence) { throw 'Refusing to replace previous evidence.' }
function No-Reparse([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point is not an owned fixture: $current" }
        $parent = Split-Path $current -Parent; if ($parent -eq $current) { break }; $current = $parent
    }
}
No-Reparse $Fixture; No-Reparse $Evidence
$fixtureFiles = @(Get-ChildItem -LiteralPath $Fixture -Recurse -Force)
if ($fixtureFiles | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Fixture contains a reparse point.' }
$auth = Join-Path $Fixture 'Auth'
if ((Test-Path -LiteralPath $auth) -and (Get-ChildItem -LiteralPath $auth -File -Recurse -Force)) { throw 'Collection soak never copies authentication profiles.' }
$updateStateFile = Join-Path $Fixture 'Updates/state.json'
if (Test-Path -LiteralPath $updateStateFile) {
    $updateState = Get-Content -LiteralPath $updateStateFile -Raw | ConvertFrom-Json
    if ($updateState.installOnExit -or $updateState.stagedArchive -or $updateState.lastInstallerRequest -or $updateState.lastInstallerProcessId) {
        throw 'Navigation soak rejects staged, armed or pending installation state; installer tests require a separate sandbox.'
    }
}
$settings = Get-Content -LiteralPath (Join-Path $Fixture 'settings.json') -Raw | ConvertFrom-Json -AsHashtable
foreach ($tool in @('YtDlp','Ffmpeg','Deno')) {
    if (-not $settings[('useCustom' + $tool)] -or -not (Test-Path -LiteralPath $settings[('custom' + $tool + 'Path')] -PathType Leaf)) { throw "Fixture must use existing isolated custom tools: $tool" }
}
$experience = Get-Content -LiteralPath (Join-Path $Fixture 'experience.json') -Raw | ConvertFrom-Json
$managedPath = [IO.Path]::ChangeExtension($ApplicationPath, '.dll')
if (-not (Test-Path -LiteralPath $managedPath -PathType Leaf)) { throw 'The published application assembly must be present beside its executable.' }
$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($managedPath).ProductVersion
if ([string]::IsNullOrWhiteSpace($assemblyVersion) -or $assemblyVersion -ne [Diagnostics.FileVersionInfo]::GetVersionInfo($ApplicationPath).ProductVersion) { throw 'Application executable and assembly version metadata must match.' }
# The journey ledger uses the assembly informational version without build metadata.
$experienceVersion = $assemblyVersion.Split('+')[0]
if ($experienceVersion -notmatch '^(\d+)\.(\d+)\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Application version is not a supported release/candidate identity.' }
# The choice policy can span product versions without expiring already recorded consent.
$experienceFamily = if ($InterfaceChoiceFamily) { $InterfaceChoiceFamily } else { $Matches[1] + '.' + $Matches[2] }
if ($experience.setup -notin @(2,'Completed') -or $experience.lastWhatsNewAcknowledgedVersion -cne $experienceVersion -or
    $experience.interfaceChoiceFamily -cne $experienceFamily) { throw 'Fixture onboarding, interface family and WhatsNew acknowledgement must match the actual application assembly.' }
$liveFile = Join-Path $Fixture 'live-sessions.json'
if (Test-Path -LiteralPath $liveFile) {
    $live = Get-Content -LiteralPath $liveFile -Raw | ConvertFrom-Json
    if ($live | Where-Object { $_.state -notin @(6,7,8,9,'Completed','Partial','Failed','Cancelled') }) { throw 'Navigation soak requires terminal synthetic LIVE sessions; mixed recording belongs to a separate test.' }
}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$qaProfile = Join-Path $temporaryRoot ('mtd-automation-smoke-soak-' + [guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $Fixture -Destination $qaProfile -Recurse
# Only the owned copy receives non-recording, non-network admission policies.
$settings.queuePaused = $true; $settings.autoResumeAfterRestart = $false; $settings.autoResumeLiveAfterRestart = $false
$settings.enableWebRemote = $false; $settings.browserIntegrationEnabled = $false
$settings.useCookieFile = $false; $settings.cookieSource = 0; $settings.cookieFilePath = $null
$settings.temporaryDirectory = Join-Path $qaProfile 'SoakTemp'; $settings.finalOutputDirectory = Join-Path $qaProfile 'SoakOutput'
if (-not $settings.startup) { $settings.startup = @{} }; $settings.startup.showTrayIcon = $false; $settings.startup.closeBehavior = 0; $settings.startup.startWithWindows = $false
if (-not $settings.notifications) { $settings.notifications = @{} }; $settings.notifications.enabled = $false
if (-not $settings.updates) { $settings.updates = @{} }; $settings.updates.automaticChecks = $false; $settings.updates.installAtStartup = $false
$settings.updates.mode = 0; $settings.updates.policyVersion = 1
$settings | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $qaProfile 'settings.json') -Encoding UTF8
$appearanceFile = Join-Path $qaProfile 'appearance.json'
$appearance = Get-Content -LiteralPath $appearanceFile -Raw | ConvertFrom-Json -AsHashtable; $appearance.interfaceMode = 1
$appearance | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $appearanceFile -Encoding UTF8
function Hashes([string]$path) {
    $hashes = @{}
    foreach ($name in @('queue.json','history.json','live-sessions.json')) {
        $file = Join-Path $path $name; $hashes[$name] = $(if (Test-Path -LiteralPath $file) { (Get-FileHash -LiteralPath $file).Hash } else { 'ABSENT' })
    }
    foreach ($folder in @('Batch','Organizer')) {
        $directory = Join-Path $path $folder
        $hashes[$folder] = $(if (Test-Path -LiteralPath $directory) { 'PRESENT' } else { 'ABSENT' })
        if (Test-Path -LiteralPath $directory) { Get-ChildItem -LiteralPath $directory -File -Recurse | ForEach-Object { $hashes[[IO.Path]::GetRelativePath($path, $_.FullName)] = (Get-FileHash -LiteralPath $_.FullName).Hash } }
    }
    return $hashes
}
function Identity($process, [string]$expectedExecutable = '') {
    $ticks = $process.StartTime.ToUniversalTime().Ticks
    $observed = Get-Process -Id $process.Id -ErrorAction Stop
    if ($observed.HasExited -or $observed.StartTime.ToUniversalTime().Ticks -ne $ticks) { throw 'Started process exited or changed identity before registration.' }
    $actualPath = $observed.Path
    if ([string]::IsNullOrWhiteSpace($expectedExecutable)) {
        if ([string]::IsNullOrWhiteSpace($actualPath)) { throw 'Parent executable identity could not be observed.' }
        $expectedExecutable = $actualPath
    }
    $expectedExecutable = [IO.Path]::GetFullPath($expectedExecutable)
    if (-not [string]::IsNullOrWhiteSpace($actualPath) -and [IO.Path]::GetFullPath($actualPath) -ne $expectedExecutable) { throw "Started executable does not match its requested path. Expected=$expectedExecutable Observed=$actualPath" }
    # Start-Process may return before Path is queryable. Only the exact executable
    # passed to that launch fills this transient gap; cleanup still verifies it.
    return @{ pid = $process.Id; startUtcTicks = $ticks; path = $expectedExecutable }
}
function Same-Owned($process, $identity) {
    if (-not $process -or -not $identity) { return $false }; $process.Refresh()
    return -not $process.HasExited -and $process.StartTime.ToUniversalTime().Ticks -eq $identity.startUtcTicks -and $process.Path -eq $identity.path
}
function Start-Script([string]$exe, [string]$script, [hashtable]$arguments, [string]$name) {
    $code = '& ' + "'" + $script.Replace("'", "''") + "'"
    foreach ($key in $arguments.Keys) { $code += ' -' + $key + " '" + ([string]$arguments[$key]).Replace("'", "''") + "'" }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
    return Start-Process -FilePath $exe -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $Evidence ($name + '.log')) -RedirectStandardError (Join-Path $Evidence ($name + '-error.log'))
}
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class CompletionSoakClock {
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryUnbiasedInterruptTime(out ulong ticks);
 public static double Seconds(){ulong ticks;if(!QueryUnbiasedInterruptTime(out ticks))throw new System.ComponentModel.Win32Exception();return ticks/10000000.0;}
}
'@
$before = Hashes $qaProfile; $fixtureBefore = Hashes $Fixture
$nativeScript = Join-Path $PSScriptRoot 'Test-CompletionSoakNative.ps1'; $watchdogScript = Join-Path $PSScriptRoot 'Test-CompletionSoakWatchdog.ps1'
$pwsh = (Get-Process -Id $PID).Path; $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$clock = [Diagnostics.Stopwatch]::StartNew(); $startedUtc = [DateTime]::UtcNow; $unbiasedStart = [CompletionSoakClock]::Seconds()
$samples = [IO.StreamWriter]::new((Join-Path $Evidence 'process-samples.jsonl'), $false, [Text.UTF8Encoding]::new($false)); $samples.AutoFlush = $true
$failures = [Collections.Generic.List[string]]::new(); $application = $null; $client = $null; $watchdog = $null; $owned = $null
$lastSample = 0.0; $lastNativeRow = $null; $peakPrivate = 0L; $peakHeartbeat = 0.0; $heartbeatObserved = $false
$oldEnvironment = @{}; foreach ($name in @('MODERNTUBEDOWNLOADER_DATA_ROOT','MTD_PERFORMANCE_DIAGNOSTICS','MTD_RENDER_AUDIT','MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL')) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:MODERNTUBEDOWNLOADER_DATA_ROOT = $qaProfile; $env:MTD_PERFORMANCE_DIAGNOSTICS = '1'; $env:MTD_RENDER_AUDIT = '1'; $env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL = '1'
    $application = Start-Process -FilePath $ApplicationPath -WindowStyle Hidden -PassThru
    $appIdentity = Identity $application $ApplicationPath
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
    $client = Start-Script $windowsPowerShell $nativeScript @{ OwnedProcessId = $application.Id; OwnedStartUtcTicks = $appIdentity.startUtcTicks; ApplicationPath = $ApplicationPath; DataRoot = $qaProfile; Evidence = $Evidence; DurationSeconds = $DurationSeconds } 'native-client'
    $owned = @{ parent = (Identity (Get-Process -Id $PID)); application = $appIdentity; client = (Identity $client $windowsPowerShell) }
    $manifest = Join-Path $Evidence 'owned-processes.json'; $owned | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
    $watchdog = Start-Script $pwsh $watchdogScript @{ Manifest = $manifest; Evidence = $Evidence; MaximumSeconds = ($DurationSeconds + 60) } 'watchdog'
    @{ scope = 'Native collection/navigation soak; not mixed FakeTool downloads'; fixture = $Fixture; profile = $qaProfile; application = $ApplicationPath;
        applicationSha256 = (Get-FileHash -LiteralPath $ApplicationPath).Hash; managedSha256 = (Get-FileHash -LiteralPath $managedPath).Hash;
        assemblyVersion = $assemblyVersion; experienceVersion = $experienceVersion; experienceFamily = $experienceFamily;
        scripts = @($PSCommandPath,$nativeScript,$watchdogScript) | ForEach-Object { @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_).Hash } };
        startedUtc = $startedUtc.ToString('O'); durationSeconds = $DurationSeconds; privateBytesLimit = 6GB; sampleGapSecondsLimit = 5; heartbeatAgeMsLimit = 5000; actionSecondsLimit = 15;
        before = $before; fixtureBefore = $fixtureBefore; owned = $owned } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Evidence 'manifest.json') -Encoding UTF8
    $performanceFile = Join-Path $qaProfile ('performance-' + $application.Id + '.jsonl'); $nativeActions = Join-Path $Evidence 'native-actions.jsonl'
    $lastSample = $clock.Elapsed.TotalSeconds
    while (-not $client.HasExited) {
        $elapsed = $clock.Elapsed.TotalSeconds; $wall = ([DateTime]::UtcNow - $startedUtc).TotalSeconds; $unbiased = [CompletionSoakClock]::Seconds() - $unbiasedStart
        if ($elapsed - $lastSample -gt 5) { throw 'Sampler gap exceeded5s: uninterrupted soak not proven' }
        if ([Math]::Abs($wall - $elapsed) -gt 3 -or [Math]::Abs($wall - $unbiased) -gt 3) { throw 'Sleep, suspend or wall-clock discontinuity detected' }
        $lastSample = $elapsed
        if ($elapsed -ge $DurationSeconds + 60) { throw 'Owned process duration limit exceeded' }
        if (-not (Same-Owned $application $owned.application)) {
            if (-not $client.WaitForExit(5000)) { throw 'Application exited before native completion' }; break
        }
        if ($watchdog.HasExited) { throw 'Independent watchdog exited before disarm' }
        if ($application.PrivateMemorySize64 -gt 6GB) { throw 'Owned application exceeded6GiB private memory limit' }
        $peakPrivate = [Math]::Max($peakPrivate, $application.PrivateMemorySize64)
        $performance = $null; $heartbeat = $null; $heartbeatGap = $null
        if (Test-Path -LiteralPath $performanceFile) { Get-Content -LiteralPath $performanceFile -Tail 3 | ForEach-Object { try { $performance = $_ | ConvertFrom-Json } catch { } } }
        if ($performance) {
            $ages = @($performance.counters | Where-Object Key -eq 'uiHeartbeatAgeMs')
            $gaps = @($performance.counters | Where-Object Key -eq 'uiHeartbeatMaxGapMs')
            $validHeartbeat = $ages.Count -eq 1 -and $gaps.Count -eq 1 -and $null -ne $ages[0].Value -and $null -ne $gaps[0].Value
            if ($validHeartbeat) {
                $heartbeat = [double]$ages[0].Value; $heartbeatGap = [double]$gaps[0].Value
                $validHeartbeat = [double]::IsFinite($heartbeat) -and [double]::IsFinite($heartbeatGap) -and $heartbeat -ge 0 -and $heartbeatGap -ge 0
            }
            if ($validHeartbeat) { $heartbeatObserved = $true }
            elseif ($heartbeatObserved -or $elapsed -gt 30) { throw 'Required UI heartbeat counters are absent, duplicated or invalid after startup' }
            if ($null -ne $heartbeat) { $peakHeartbeat = [Math]::Max($peakHeartbeat, $heartbeat) }
            if ($heartbeat -gt 5000 -or $heartbeatGap -gt 5000) { throw 'UI heartbeat stalled for more than5s' }
            if (([DateTime]::UtcNow - ([DateTime]$performance.utc).ToUniversalTime()).TotalSeconds -gt 5) { throw 'Performance diagnostics stopped for more than5s' }
        } elseif ($elapsed -gt 30) { throw 'Required performance heartbeat diagnostics absent' }
        if (Test-Path -LiteralPath $nativeActions) { Get-Content -LiteralPath $nativeActions -Tail 3 | ForEach-Object { try { $lastNativeRow = $_ | ConvertFrom-Json } catch { } } }
        if ($lastNativeRow -and $lastNativeRow.phase -eq 'begin') {
            $limit = $(if ($lastNativeRow.action -eq 'startup.hwnd') { 30 } else { 15 })
            if (([DateTime]::UtcNow - ([DateTime]$lastNativeRow.utc).ToUniversalTime()).TotalSeconds -gt $limit) { throw "UIA client action deadline: $($lastNativeRow.action)" }
        }
        @{ utc = [DateTime]::UtcNow.ToString('O'); elapsedSeconds = $elapsed; wallSeconds = $wall; unbiasedSeconds = $unbiased; pid = $application.Id; hwnd = $application.MainWindowHandle.ToInt64();
            privateBytes = $application.PrivateMemorySize64; workingSet = $application.WorkingSet64; cpuMs = $application.TotalProcessorTime.TotalMilliseconds; handles = $application.HandleCount; threads = $application.Threads.Count;
            heartbeatAgeMs = $heartbeat; heartbeatMaxGapMs = $heartbeatGap; diagnostics = $performance } | ConvertTo-Json -Depth 8 -Compress | ForEach-Object { $samples.WriteLine($_) }
        Start-Sleep -Milliseconds 1000; $client.Refresh(); $watchdog.Refresh()
    }
    $client.WaitForExit()
    if ($client.ExitCode -ne 0) { throw "Native client failed: exit=$($client.ExitCode)" }
} catch { $failures.Add($_.ToString()) }
finally {
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
    # Never discover a process by name for cleanup, and never kill a reused PID.
    if ($owned) {
        foreach ($pair in @(@($client,$owned.client), @($application,$owned.application))) {
            try {
                if (Same-Owned $pair[0] $pair[1]) {
                    $failures.Add('Forced cleanup of still-running owned process ' + $pair[1].pid)
                    $pair[0].Kill(); [void]$pair[0].WaitForExit(5000)
                }
            } catch { $failures.Add($_.ToString()) }
        }
    } elseif ($application -and $appIdentity -and (Same-Owned $application $appIdentity)) { $application.Kill(); [void]$application.WaitForExit(5000) }
    $samples.Dispose()
    Set-Content -LiteralPath (Join-Path $Evidence 'watchdog.disarm') -Value ([DateTime]::UtcNow.ToString('O')) -Encoding UTF8
    if ($watchdog -and -not $watchdog.WaitForExit(5000)) { $failures.Add('Watchdog did not acknowledge disarm') }
    foreach ($path in @((Join-Path $qaProfile 'logs'),(Join-Path $qaProfile ('performance-' + $application.Id + '.jsonl')))) {
        try { if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $Evidence -Recurse } }
        catch { $failures.Add('Preserving original diagnostics failed: ' + $_.ToString()) }
    }
}
$after = @{}; $fixtureAfter = @{}
try { $after = Hashes $qaProfile; $fixtureAfter = Hashes $Fixture }
catch { $failures.Add('Final data hash verification failed: ' + $_.ToString()) }
foreach ($key in @($before.Keys) + @($after.Keys) | Select-Object -Unique) { if ($before[$key] -ne $after[$key]) { $failures.Add('Owned data changed: ' + $key) } }
foreach ($key in @($fixtureBefore.Keys) + @($fixtureAfter.Keys) | Select-Object -Unique) { if ($fixtureBefore[$key] -ne $fixtureAfter[$key]) { $failures.Add('Original fixture changed: ' + $key) } }
$nativeResult = $null; $nativeResultFile = Join-Path $Evidence 'native-result.json'
try { if (Test-Path -LiteralPath $nativeResultFile) { $nativeResult = Get-Content -LiteralPath $nativeResultFile -Raw | ConvertFrom-Json } }
catch { $failures.Add('Native result could not be read: ' + $_.ToString()) }
if (-not $nativeResult -or $nativeResult.result -ne 'PASS' -or $nativeResult.activeSeconds -lt $DurationSeconds) { $failures.Add('Full continuous native duration was not completed successfully') }
if (-not $heartbeatObserved) { $failures.Add('No valid UI heartbeat counters were observed') }
$watchdogResult = $null; $watchdogResultFile = Join-Path $Evidence 'watchdog-result.json'
try { if (Test-Path -LiteralPath $watchdogResultFile) { $watchdogResult = Get-Content -LiteralPath $watchdogResultFile -Raw | ConvertFrom-Json } }
catch { $failures.Add('Watchdog result could not be read: ' + $_.ToString()) }
if (-not $watchdogResult -or $watchdogResult.result -ne 'DISARMED' -or $watchdogResult.errors.Count -gt 0) { $failures.Add('Independent watchdog did not complete cleanly') }
$stop = $null; $copiedPerformance = Join-Path $Evidence ('performance-' + $application.Id + '.jsonl')
if (Test-Path -LiteralPath $copiedPerformance) { $stop = Get-Content -LiteralPath $copiedPerformance -Tail 3 | ForEach-Object { try { $_ | ConvertFrom-Json } catch { } } | Where-Object name -eq 'process.stop' | Select-Object -Last 1 }
if (-not $stop) { $failures.Add('No graceful process.stop diagnostics') }
else {
    foreach ($key in @('nativeBitmapBytes','thumbnail.displayCopiesBytes','thumbnail.cachedBytes','thumbnail.pending','thumbnail.active')) {
        $counter = @($stop.counters | Where-Object Key -eq $key)
        if ($counter.Count -ne 1 -or $counter[0].Value -ne 0) { $failures.Add('Final bitmap/cache release not proven: ' + $key) }
    }
}
@{ result = $(if ($failures.Count) { 'FAIL' } else { 'PASS' }); scope = 'Native collection/navigation soak, not mixed FakeTool workload';
    failures = @($failures); requestedSeconds = $DurationSeconds; elapsedSeconds = $clock.Elapsed.TotalSeconds; peakPrivateBytes = $peakPrivate; peakHeartbeatAgeMs = $peakHeartbeat; heartbeatObserved = $heartbeatObserved;
    native = $nativeResult; watchdog = $watchdogResult; after = $after; fixtureAfter = $fixtureAfter; retainedProfile = $qaProfile } | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $Evidence 'qualification.json') -Encoding UTF8
Write-Output ('Evidence=' + $Evidence + ' Result=' + $(if ($failures.Count) { 'FAIL' } else { 'PASS' }))
if ($failures.Count) { exit 1 }
