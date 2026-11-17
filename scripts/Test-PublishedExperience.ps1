#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
# Actual published application, three independent process lifetimes, native Windows UIA.
# This runner never writes settings/experience fixtures and never supplies --automation.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This qualification requires Windows and an interactive desktop.' }
$ApplicationPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
if ([IO.Path]::GetExtension($ApplicationPath) -ne '.exe') { throw 'ApplicationPath must name the published Windows executable.' }
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
$sourceApplication = $ApplicationPath
$sourceDirectory = [IO.Path]::GetDirectoryName($sourceApplication)
if ($EvidenceDirectory.Equals($sourceDirectory, [StringComparison]::OrdinalIgnoreCase) -or
    $EvidenceDirectory.StartsWith($sourceDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceDirectory must be outside the published application directory.'
}
if (Test-Path -LiteralPath $EvidenceDirectory) {
    if ((Get-Item -LiteralPath $EvidenceDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'EvidenceDirectory cannot be a reparse point.' }
    if (@(Get-ChildItem -LiteralPath $EvidenceDirectory -Force).Count) { throw 'EvidenceDirectory must be new or empty; previous evidence is never overwritten.' }
} else { [void][IO.Directory]::CreateDirectory($EvidenceDirectory) }
# The new-profile updater starts before the wizard. Isolate its installation
# target as well as its profile, retaining the exact published package contents.
if (((Get-Item -LiteralPath $sourceDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    @(Get-ChildItem -LiteralPath $sourceDirectory -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'Published input must contain ordinary files/directories, not reparse points.'
}
$installation = Join-Path $EvidenceDirectory 'installation'
Copy-Item -LiteralPath $sourceDirectory -Destination $installation -Recurse
$ApplicationPath = Join-Path $installation ([IO.Path]::GetFileName($sourceApplication))
if ((Get-FileHash -LiteralPath $sourceApplication -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $ApplicationPath -Algorithm SHA256).Hash) {
    throw 'Isolated executable does not match the supplied published executable.'
}
$sourceManaged = [IO.Path]::ChangeExtension($sourceApplication, '.dll')
if ((Test-Path -LiteralPath $sourceManaged) -and
    (Get-FileHash -LiteralPath $sourceManaged -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($ApplicationPath, '.dll')) -Algorithm SHA256).Hash) {
    throw 'Isolated managed assembly does not match the supplied published assembly.'
}
$profile = Join-Path ([IO.Path]::GetTempPath()) ('mtd-automation-smoke-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($profile)
$nativeScript = Join-Path $PSScriptRoot 'Test-PublishedExperienceNative.ps1'
$powershell5 = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$storeFiles = @('settings.json', 'appearance.json', 'experience.json', 'queue.json', 'history.json', 'live-sessions.json', 'Updates/state.json', 'Tools/state.json')
$stages = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$application = $null; $client = $null; $ownedStart = $null
$binaryHash = (Get-FileHash -LiteralPath $ApplicationPath -Algorithm SHA256).Hash
$managedPath = [IO.Path]::ChangeExtension($ApplicationPath, '.dll')
$managedHash = if (Test-Path -LiteralPath $managedPath) { (Get-FileHash -LiteralPath $managedPath -Algorithm SHA256).Hash } else { $null }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($ApplicationPath).ProductVersion

function Write-Json($Value, [string]$Path) { $Value | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $Path -Encoding utf8 }
function Save-ProfileEvidence([string]$Stage) {
    $directory = Join-Path $EvidenceDirectory $Stage
    [void][IO.Directory]::CreateDirectory($directory)
    $manifest = foreach ($relative in $storeFiles) {
        $source = Join-Path $profile $relative
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            $destination = Join-Path $directory $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
            Copy-Item -LiteralPath $source -Destination $destination
            @{ path = $relative; length = (Get-Item -LiteralPath $source).Length; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
        } else { @{ path = $relative; absent = $true } }
    }
    Write-Json @($manifest) (Join-Path $directory 'store-hashes.json')
}
function Assert-NoOtherApplication {
    # The production single-instance endpoint is per user, not per data root. Never
    # launch a second instance that would activate an unrelated user's window.
    if (@(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ApplicationPath)) -ErrorAction SilentlyContinue).Count) {
        throw 'An application instance is already running. No existing process was closed.'
    }
    $semaphore = $null
    try {
        $semaphore = [Threading.Semaphore]::OpenExisting('Local\ProGraMajster.ModernTubeDownloader.SingleInstance')
        if (-not $semaphore.WaitOne(0)) { throw 'The production single-instance endpoint is occupied.' }
        [void]$semaphore.Release()
    } catch [Threading.WaitHandleCannotBeOpenedException] { } finally { if ($semaphore) { $semaphore.Dispose() } }
}
function Assert-OwnedApplication {
    if ($application.HasExited) { return }
    $current = Get-Process -Id $application.Id -ErrorAction Stop
    if ($current.StartTime.ToUniversalTime().Ticks -ne $ownedStart -or $current.Path -ne $ApplicationPath) {
        throw 'Application PID identity changed; refusing to signal it.'
    }
}
function Stop-OwnedProcesses {
    # A stuck external UIA call cannot block cleanup indefinitely. Only this runner's
    # Process objects are terminated; a watchdog termination always leaves FAIL.
    if ($client -and -not $client.HasExited) { $client.Kill(); [void]$client.WaitForExit(5000) }
    if ($application -and -not $application.HasExited) {
        Assert-OwnedApplication
        [void]$application.CloseMainWindow()
        if (-not $application.WaitForExit(15000)) {
            $failures.Add('Owned application required watchdog termination; graceful shutdown NOT QUALIFIED.')
            Assert-OwnedApplication
            $application.Kill($true)
            [void]$application.WaitForExit(5000)
        }
    }
}

try {
    if (@(Get-ChildItem -LiteralPath $profile -Force).Count -ne 0) { throw 'Initial profile must be completely empty.' }
    Save-ProfileEvidence '00-empty-profile'
    foreach ($phase in @('Draft', 'Complete', 'Restart')) {
        Assert-NoOtherApplication
        $stage = Join-Path $EvidenceDirectory $phase.ToLowerInvariant()
        [void][IO.Directory]::CreateDirectory($stage)
        $start = [Diagnostics.ProcessStartInfo]::new($ApplicationPath)
        $start.UseShellExecute = $false
        $start.WorkingDirectory = [IO.Path]::GetDirectoryName($ApplicationPath)
        # Do not inherit another test's debug fixtures or QA switches.
        foreach ($key in @($start.Environment.Keys)) {
            if ($key -like 'MTD_*' -or $key -like 'MODERNTUBEDOWNLOADER_*') { [void]$start.Environment.Remove($key) }
        }
        $start.Environment['MODERNTUBEDOWNLOADER_DATA_ROOT'] = $profile
        $start.Environment['MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL'] = '1'
        $application = [Diagnostics.Process]::Start($start)
        $ownedStart = $application.StartTime.ToUniversalTime().Ticks
        Write-Json @{ phase = $phase; pid = $application.Id; startUtcTicks = $ownedStart; executable = $ApplicationPath; arguments = @(); profile = $profile } (Join-Path $stage 'owner.json')
        # Base64 contains code with independently single-quoted arguments, never
        # shell-interpolated file names. The helper is hidden; the app remains visible.
        $arguments = @($nativeScript, [string]$application.Id, [string]$ownedStart, $ApplicationPath, $profile, $stage, $phase)
        $quoted = @($arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" })
        $command = '& ' + $quoted[0] + ' -OwnedProcessId ' + $quoted[1] + ' -StartUtcTicks ' + $quoted[2] + ' -ApplicationPath ' + $quoted[3] + ' -DataRoot ' + $quoted[4] + ' -EvidenceDirectory ' + $quoted[5] + ' -Phase ' + $quoted[6]
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $client = Start-Process -FilePath $powershell5 -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stage 'native.log') -RedirectStandardError (Join-Path $stage 'native-error.log')
        # First-ever genuine provisioning is separate from the 15/30 second UI
        # action watchdogs. No timeout is retried and no failed stage is rerun.
        $limitMs = if ($phase -eq 'Draft') { 300000 } else { 180000 }
        if (-not $client.WaitForExit($limitMs)) { throw "Native $phase client exceeded its bounded watchdog." }
        $nativeResult = Join-Path $stage 'native-result.json'
        if (-not (Test-Path -LiteralPath $nativeResult)) { throw "Native $phase did not produce a result." }
        $result = Get-Content -LiteralPath $nativeResult -Raw | ConvertFrom-Json
        $stages.Add($result)
        if ($client.ExitCode -ne 0 -or $result.result -ne 'PASS') { throw "Native $phase failed; inspect its original result and log." }
        if (-not $application.WaitForExit(30000)) { throw "Application did not exit after $phase native Close." }
        if ($application.ExitCode -ne 0) { throw "Application exited with code $($application.ExitCode) after $phase." }
        Save-ProfileEvidence ($phase.ToLowerInvariant() + '-closed')
        $application.Dispose(); $application = $null
        $client.Dispose(); $client = $null
    }
} catch {
    $failures.Add($_.Exception.Message)
    Write-Warning $_.Exception.Message
} finally {
    try { Stop-OwnedProcesses } catch { $failures.Add('Cleanup: ' + $_.Exception.Message) }
    try { Save-ProfileEvidence 'final-profile' } catch { $failures.Add('Evidence: ' + $_.Exception.Message) }
    $logs = Join-Path $profile 'logs'
    if (Test-Path -LiteralPath $logs -PathType Container) { Copy-Item -LiteralPath $logs -Destination (Join-Path $EvidenceDirectory 'logs') -Recurse }
    if ((Get-FileHash -LiteralPath $ApplicationPath -Algorithm SHA256).Hash -ne $binaryHash) { $failures.Add('Application executable changed during qualification.') }
    if ($managedHash -and (Get-FileHash -LiteralPath $managedPath -Algorithm SHA256).Hash -ne $managedHash) { $failures.Add('Application managed assembly changed during qualification.') }
    Write-Json @{
        result = $(if ($failures.Count -eq 0 -and $stages.Count -eq 3) { 'PASS' } else { 'FAIL' })
        scenario = 'Published empty-profile setup, durable draft restart, six steps, Modern, WhatsNew acknowledgement and restart'
        profile = $profile; application = $ApplicationPath; suppliedApplication = $sourceApplication; version = $version; executableSha256 = $binaryHash; managedSha256 = $managedHash
        nativeScriptSha256 = (Get-FileHash -LiteralPath $nativeScript -Algorithm SHA256).Hash
        semanticBridge = $false; preseededSettings = $false; elapsedSeconds = $watch.Elapsed.TotalSeconds
        phases = @($stages.ToArray()); failures = @($failures.ToArray())
        boundaries = @('Actual tool provisioning uses the network; no fixture tools/settings.', 'Published installation is copied to the evidence directory before launch; only the isolated copy can be updated.', 'Interruption uses native Close, not power loss.', 'Update mode is explicitly changed through UI to NotifyOnly before completion; no installer transaction is qualified.', 'No profiles or original evidence are deleted.')
    } (Join-Path $EvidenceDirectory 'result.json')
}
Write-Output "Evidence: $EvidenceDirectory"
Write-Output "Retained profile: $profile"
if ($failures.Count -or $stages.Count -ne 3) { exit 1 }
exit 0
