#requires -Version 7.0
<#
.SYNOPSIS
Qualifies the NativeHost and Updater executables from an unpacked Windows release.
.DESCRIPTION
Runs only the two published helpers and owned stdin-gated PowerShell children.
Updater installations/archives contain inert synthetic files, never a runnable
application. Restart is always false. This does not qualify browser registration,
authentication, first-run UI, or launching the upgraded application.

EvidenceDirectory must not exist. All fixtures, requests, journals, process output
and hash manifests are retained. No recursive deletion or registry writes occur.
.EXAMPLE
pwsh -NoProfile -File scripts/Test-PublishedHelpers.ps1 -PackageDirectory artifacts/unpacked -EvidenceDirectory artifacts/published-helpers-new
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Published Windows helpers require Windows.' }
$package = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$utf8 = [Text.UTF8Encoding]::new($false)
$deadlineMilliseconds = 20000 # Same bound as the existing process regressions.
$results = [Collections.Generic.List[object]]::new()

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Save-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 12), $utf8)
}
function Assert-NoReparsePoints([string]$Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            Assert-Condition (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Reparse point is not allowed: $current"
        }
    }
}
function Get-TreeHashes([string]$Root) {
    $manifest = [Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $Root -Force -Recurse | Sort-Object FullName) {
        Assert-Condition (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Unexpected reparse point: $($file.FullName)"
        if (-not $file.PSIsContainer) {
            $manifest.Add([pscustomobject]@{
                path = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
                length = $file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            })
        }
    }
    return $manifest.ToArray()
}
function Assert-SameHashes($Before, $After, [string]$Description) {
    $left = ConvertTo-Json -InputObject @($Before) -Depth 4 -Compress
    $right = ConvertTo-Json -InputObject @($After) -Depth 4 -Compress
    Assert-Condition ($left -ceq $right) "$Description changed; compare the retained before/after hash manifests."
}
function Write-Fixture([string]$Path, [string]$Value) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    [IO.File]::WriteAllText($Path, $Value, $utf8)
}
function New-Archive([string]$Path, [Collections.IDictionary]$Entries) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        # Order matters: rollback must undo an overwritten and an added file before
        # the final locked DLL fails. An OrderedDictionary preserves this contract.
        foreach ($entry in $Entries.GetEnumerator()) {
            $output = $zip.CreateEntry([string]$entry.Key).Open()
            try {
                $bytes = $utf8.GetBytes([string]$entry.Value)
                $output.Write($bytes, 0, $bytes.Length)
            } finally { $output.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
function Save-ProcessOutput($Child) {
    if (-not $Child.process.HasExited) { return }
    [IO.File]::WriteAllText(($Child.prefix + '.stdout.txt'), $Child.stdout.GetAwaiter().GetResult(), $utf8)
    [IO.File]::WriteAllText(($Child.prefix + '.stderr.txt'), $Child.stderr.GetAwaiter().GetResult(), $utf8)
    Save-Json ($Child.prefix + '.exit.json') @{ exitCode = $Child.process.ExitCode; observedUtc = [DateTime]::UtcNow.ToString('O') }
}
function Start-OwnedProcess([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$Prefix) {
    $expectedPath = [IO.Path]::GetFullPath($Executable)
    Assert-Condition ([IO.File]::Exists($expectedPath)) "Executable missing: $expectedPath"
    $start = [Diagnostics.ProcessStartInfo]::new($expectedPath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $WorkingDirectory
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $childProcess = [Diagnostics.Process]::new()
    $childProcess.StartInfo = $start
    Assert-Condition ($childProcess.Start()) "Process did not start: $expectedPath"
    $child = [pscustomobject]@{
        process = $childProcess; prefix = $Prefix
        identity = [pscustomobject]@{ pid = $childProcess.Id; startUtcTicks = $null; path = $expectedPath; observedPath = $null }
        stdout = $childProcess.StandardOutput.ReadToEndAsync()
        stderr = $childProcess.StandardError.ReadToEndAsync()
    }
    # Register immediately so finally can record even a failed identity preflight.
    $script:caseChildren.Add($child)
    try {
        $child.identity.startUtcTicks = $childProcess.StartTime.ToUniversalTime().Ticks
        $observed = Get-Process -Id $childProcess.Id -ErrorAction SilentlyContinue
        if ($observed) {
            try {
                # A negative NativeHost probe can finish between the lookup and
                # querying its path. Completed children need no termination right.
                if (-not $childProcess.HasExited) {
                    $actualPath = $observed.Path
                    if ($actualPath) {
                        $child.identity.observedPath = [IO.Path]::GetFullPath($actualPath)
                        Assert-Condition ($child.identity.observedPath -ieq $expectedPath) 'Started process executable identity mismatch.'
                    }
                    Assert-Condition ($observed.StartTime.ToUniversalTime().Ticks -eq $child.identity.startUtcTicks) 'Started process timestamp identity mismatch.'
                }
            } finally { $observed.Dispose() }
        }
    } finally { Save-Json ($Prefix + '.identity.json') $child.identity }
    return $child
}
function Wait-OwnedExit($Child) {
    Assert-Condition ($Child.process.WaitForExit($deadlineMilliseconds)) "Owned helper exceeded the 20-second process regression deadline: $($Child.identity.path)"
    Save-ProcessOutput $Child
}
function Stop-OwnedIfRunning($Child) {
    if ($Child.process.HasExited) { Save-ProcessOutput $Child; return }
    # Never terminate by PID alone, enumerate other processes, or kill a process
    # tree. Missing/changed identity fails closed and is a qualification failure.
    $current = Get-Process -Id $Child.identity.pid -ErrorAction SilentlyContinue
    if (-not $current) { Wait-OwnedExit $Child; return }
    try {
        $observedPath = $current.Path
        Assert-Condition ($null -ne $Child.identity.startUtcTicks -and $observedPath -and
            $current.StartTime.ToUniversalTime().Ticks -eq $Child.identity.startUtcTicks -and
            [IO.Path]::GetFullPath($observedPath) -ieq $Child.identity.path) 'Cannot prove ownership; refusing to stop the process.'
        $current.Kill()
        Assert-Condition ($Child.process.WaitForExit(5000)) 'Owned helper did not exit after termination.'
        Save-Json ($Child.prefix + '.terminated.json') @{ reason = 'Fixture cleanup after an unfinished helper'; identity = $Child.identity }
        Save-ProcessOutput $Child
        throw 'An owned process was still running at case completion and required termination.'
    } finally { $current.Dispose() }
}
function New-GatedParent([string]$CaseDirectory) {
    $ready = Join-Path $CaseDirectory 'parent.ready'
    $escapedReady = $ready.Replace("'", "''")
    $parentCode = "[IO.File]::WriteAllText('$escapedReady', 'ready'); [Console]::ReadLine() | Out-Null"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($parentCode))
    $child = Start-OwnedProcess $script:powerShellPath @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded) $CaseDirectory (Join-Path $CaseDirectory 'parent')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not [IO.File]::Exists($ready)) {
        Assert-Condition (-not $child.process.HasExited) 'Synthetic parent exited before reaching its stdin gate.'
        Assert-Condition ($timer.ElapsedMilliseconds -lt $deadlineMilliseconds) 'Synthetic parent did not reach its stdin gate.'
        Start-Sleep -Milliseconds 25
    }
    return $child
}
function Release-GatedParent($Parent) {
    Assert-Condition (-not $Parent.process.HasExited) 'Synthetic parent unexpectedly exited before release.'
    $Parent.process.StandardInput.WriteLine('release')
    $Parent.process.StandardInput.Flush()
    $Parent.process.StandardInput.Close()
    Wait-OwnedExit $Parent
    Assert-Condition ($Parent.process.ExitCode -eq 0) 'Synthetic parent failed.'
}
function Invoke-Case([string]$Name, [scriptblock]$Body) {
    $directory = Join-Path $evidence $Name
    [void][IO.Directory]::CreateDirectory($directory)
    $script:caseChildren = [Collections.Generic.List[object]]::new()
    $script:caseLock = $null
    $caseClock = [Diagnostics.Stopwatch]::StartNew()
    $errors = [Collections.Generic.List[string]]::new()
    try { & $Body $directory }
    catch { $errors.Add($_.ToString()); [IO.File]::WriteAllText((Join-Path $directory 'failure.txt'), $_.ToString(), $utf8) }
    finally {
        for ($index = $script:caseChildren.Count - 1; $index -ge 0; $index--) {
            $child = $script:caseChildren[$index]
            try { Stop-OwnedIfRunning $child }
            catch { $errors.Add($_.ToString()) }
            finally { $child.process.Dispose() }
        }
        if ($script:caseLock) { $script:caseLock.Dispose(); $script:caseLock = $null }
    }
    $record = [pscustomobject]@{ name = $Name; result = $(if ($errors.Count) { 'FAIL' } else { 'PASS' }); seconds = $caseClock.Elapsed.TotalSeconds; errors = $errors.ToArray() }
    Save-Json (Join-Path $directory 'case-result.json') $record
    $results.Add($record)
    Write-Output "$Name $($record.result)"
}
function Test-UpdaterCase([string]$CaseDirectory, [ValidateSet('ParentGate', 'LockedDll', 'Checksum', 'StartupAbsent', 'StartupPresent')][string]$Kind) {
    $installation = Join-Path $CaseDirectory 'installation'
    $profile = Join-Path $CaseDirectory 'profile'
    $application = Join-Path $installation 'ModernTubeDownloader.exe'
    Write-Fixture $application 'synthetic old executable; never run'
    Write-Fixture (Join-Path $installation 'settings.json') '{"sentinel":"installation settings must survive"}'
    foreach ($name in @('settings.json', 'appearance.json', 'queue.json', 'history.json', 'live-sessions.json', 'Batch/synthetic/session.json', 'Auth/cookies.txt')) {
        Write-Fixture (Join-Path $profile $name) "synthetic private data: $name"
    }
    $entries = [ordered]@{ 'ModernTubeDownloader.exe' = 'synthetic new executable; never run'; 'added.dll' = 'synthetic added DLL' }
    if ($Kind -eq 'LockedDll') {
        Write-Fixture (Join-Path $installation 'blocked.dll') 'synthetic original locked DLL'
        $entries.Add('blocked.dll', 'synthetic replacement DLL')
    }
    $beforeInstallation = @(Get-TreeHashes $installation)
    $beforeProfile = @(Get-TreeHashes $profile)
    Save-Json (Join-Path $CaseDirectory 'installation.before.json') $beforeInstallation
    Save-Json (Join-Path $CaseDirectory 'profile.before.json') $beforeProfile
    $archive = Join-Path $CaseDirectory 'candidate.zip'
    New-Archive $archive $entries
    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    $expectedHash = $archiveHash
    if ($Kind -eq 'Checksum') {
        $expectedHash = ('0' * 64)
        if ($archiveHash -eq $expectedHash) { $expectedHash = ('1' * 64) }
    }
    $parent = New-GatedParent $CaseDirectory
    $requestPath = Join-Path $CaseDirectory 'request.json'
    $request = [ordered]@{
        Installation = $installation; Archive = $archive; Sha256 = $expectedHash
        ParentProcessId = $parent.identity.pid; ParentStartUtcTicks = $parent.identity.startUtcTicks
        Restart = $false; StartupCommitToken = $null
    }
    if ($Kind -in @('StartupAbsent', 'StartupPresent')) { $request.StartupCommitToken = [Guid]::NewGuid().ToString('N') }
    Save-Json $requestPath $request
    if ($Kind -eq 'StartupPresent') { Write-Fixture ($requestPath + '.startup-commit') $request.StartupCommitToken }
    if ($Kind -eq 'LockedDll') {
        # Allow backup/read/hash, deny replacement. Keep this handle throughout
        # rollback, including while proving the unchanged DLL is still readable.
        $script:caseLock = [IO.File]::Open((Join-Path $installation 'blocked.dll'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    }
    try {
        $helper = Start-OwnedProcess $script:updaterPath @('--request', $requestPath) $CaseDirectory (Join-Path $CaseDirectory 'updater')
        if ($Kind -eq 'ParentGate') {
            # Same 500-ms live-parent observation as the existing regression. This
            # is an observed no-write interval, not a claim about an internal stack.
            $gateTimer = [Diagnostics.Stopwatch]::StartNew()
            $observations = 0
            do {
                Assert-Condition (-not $parent.process.HasExited -and -not $helper.process.HasExited) 'Parent/helper exited while the parent gate was held.'
                Assert-Condition (-not [IO.File]::Exists($requestPath + '.result.json')) 'Updater completed before its live parent exited.'
                Assert-SameHashes $beforeInstallation @(Get-TreeHashes $installation) 'Installation while parent was alive'
                $observations++
                Start-Sleep -Milliseconds 25
            } while ($gateTimer.ElapsedMilliseconds -lt 500)
            Save-Json (Join-Path $CaseDirectory 'parent-gate.json') @{ observations = $observations; observedMilliseconds = $gateTimer.ElapsedMilliseconds; parent = $parent.identity; helper = $helper.identity }
        }
        Release-GatedParent $parent
        Wait-OwnedExit $helper
        $expectedSuccess = $Kind -in @('ParentGate', 'StartupPresent')
        $expectedExit = if ($expectedSuccess) { 0 } else { 1 }
        Assert-Condition ($helper.process.ExitCode -eq $expectedExit) "Unexpected updater exit code: $($helper.process.ExitCode), expected $expectedExit."
        Assert-Condition ([IO.File]::Exists($requestPath + '.result.json')) 'Updater did not write its result.'
        $result = Get-Content -LiteralPath ($requestPath + '.result.json') -Raw | ConvertFrom-Json
        Assert-Condition ($result.success -is [bool] -and $result.success -eq $expectedSuccess) 'Updater result does not match the expected transaction outcome.'
        Assert-Condition ($helper.stdout.GetAwaiter().GetResult().Length -eq 0 -and $helper.stderr.GetAwaiter().GetResult().Length -eq 0) 'Updater produced unexpected console output.'
        $journalPath = Join-Path $installation '.mtd-update-backup/journal.json'
        if ($expectedSuccess -or $Kind -eq 'LockedDll') {
            Assert-Condition ([IO.File]::Exists($journalPath)) 'Transaction journal missing.'
            $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
            Assert-Condition ($journal.Completed -is [bool] -and $journal.Completed -eq $true) 'Transaction/rollback journal remains incomplete.'
            Assert-Condition ([IO.Path]::GetFullPath($journal.Installation) -ieq $installation) 'Journal belongs to a different installation.'
            $expectedJournalFiles = @($entries.Keys)
            Assert-Condition ((@($journal.Files.RelativePath) -join '|') -ceq ($expectedJournalFiles -join '|')) 'Transaction did not reach the expected files in archive order.'
            Assert-Condition ([IO.File]::ReadAllText((Join-Path $installation '.mtd-update-backup/files/ModernTubeDownloader.exe')) -ceq 'synthetic old executable; never run') 'Original executable backup missing or damaged.'
        } else {
            Assert-Condition (-not [IO.Directory]::Exists((Join-Path $installation '.mtd-update-backup'))) 'Rejected request touched the transaction backup.'
            Assert-Condition (@(Get-ChildItem -LiteralPath $CaseDirectory -Directory -Filter 'extracted-*').Count -eq 0) 'Rejected request extracted its archive.'
            Assert-Condition ($result.exceptionType -ceq 'InvalidDataException') 'Expected package/admission rejection type was not reported.'
            if ($Kind -eq 'Checksum') { Assert-Condition ($result.error -match 'Update checksum mismatch') 'Rejection was not caused by checksum verification.' }
            else { Assert-Condition ($result.error -match 'did not commit this startup installation') 'Rejection was not caused by missing startup commitment.' }
        }
        if ($expectedSuccess) {
            foreach ($entry in $entries.GetEnumerator()) {
                Assert-Condition ([IO.File]::ReadAllText((Join-Path $installation $entry.Key)) -ceq $entry.Value) "Updated payload differs: $($entry.Key)"
            }
        } else {
            $payloadAfter = @(Get-TreeHashes $installation | Where-Object { -not $_.path.StartsWith('.mtd-update-backup/', [StringComparison]::Ordinal) })
            Assert-SameHashes $beforeInstallation $payloadAfter 'Rejected/rolled-back installation'
            if ($Kind -eq 'LockedDll') {
                Assert-Condition ($result.exceptionType -ceq 'IOException') 'Locked DLL did not fail with the expected copy I/O error.'
                Assert-Condition (-not [IO.File]::Exists((Join-Path $installation 'added.dll'))) 'Rollback retained a newly added file.'
            }
        }
        Assert-Condition ([IO.File]::ReadAllText((Join-Path $installation 'settings.json')) -ceq '{"sentinel":"installation settings must survive"}') 'Installation settings were modified.'
        Assert-SameHashes $beforeProfile @(Get-TreeHashes $profile) 'Synthetic private profile'
        Assert-Condition ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -eq $archiveHash) 'Input archive was modified.'
    } finally {
        Save-Json (Join-Path $CaseDirectory 'installation.after.json') @(Get-TreeHashes $installation)
        Save-Json (Join-Path $CaseDirectory 'profile.after.json') @(Get-TreeHashes $profile)
    }
}

Assert-NoReparsePoints $package
Assert-NoReparsePoints $evidence
Assert-Condition ([IO.Path]::GetFullPath($package) -ine [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($package))) 'A filesystem root is not a package directory.'
Assert-Condition ([IO.Directory]::Exists($package)) 'PackageDirectory must be an existing unpacked package directory.'
Assert-Condition (-not (Test-Path -LiteralPath $evidence)) 'EvidenceDirectory must be new; existing evidence is never overwritten.'
Assert-Condition ($package -ine $evidence -and -not $evidence.StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
    -not $package.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Package and evidence directories must be disjoint.'
$script:updaterPath = Join-Path $package 'updater/ModernTubeDownloader.Updater.exe'
$nativeHostPath = Join-Path $package 'native-host/ModernTubeDownloader.NativeHost.exe'
foreach ($required in @($script:updaterPath, $nativeHostPath, (Join-Path $package 'ModernTubeDownloader.exe'))) {
    Assert-Condition ([IO.File]::Exists($required)) "Required published executable missing: $required"
}
$script:powerShellPath = [Environment]::ProcessPath
Assert-Condition ([IO.File]::Exists($script:powerShellPath) -and [IO.Path]::GetFileName($script:powerShellPath) -ieq 'pwsh.exe') 'Run this script with PowerShell 7 (pwsh.exe).'
[void][IO.Directory]::CreateDirectory($evidence)
$packageBefore = @(Get-TreeHashes $package)
Save-Json (Join-Path $evidence 'package.before.json') $packageBefore
Save-Json (Join-Path $evidence 'qualification.json') @{
    startedUtc = [DateTime]::UtcNow.ToString('O'); package = $package; evidence = $evidence
    helperTimeoutMilliseconds = $deadlineMilliseconds; mainApplicationStarted = $false; browserRegistered = $false
    payload = 'Inert synthetic fixtures; published helper executables are invoked directly from PackageDirectory.'
    exclusions = @('Real browser authentication/registration', 'Main application startup/first-run UI', 'Launching the upgraded application', 'Live release download')
    executables = @($script:updaterPath, $nativeHostPath) | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash; productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($_).ProductVersion }
    }
}
try {
    Invoke-Case 'native-host-no-arguments' {
        param($directory)
        # No origin argument: NativeHost's failure-health path returns before
        # reading bindings/writing the real per-user Auth directory. DATA_ROOT
        # is not an Auth override, so no positive-origin probe is attempted.
        $child = Start-OwnedProcess $nativeHostPath @() $directory (Join-Path $directory 'native-host')
        $child.process.StandardInput.Close()
        Wait-OwnedExit $child
        Assert-Condition ($child.process.ExitCode -eq 2) 'NativeHost did not reject its non-browser parent.'
        Assert-Condition ($child.stdout.GetAwaiter().GetResult().Length -eq 0) 'NativeHost wrote unexpected protocol stdout.'
        Assert-Condition ($child.stderr.GetAwaiter().GetResult().Length -eq 0) 'NativeHost wrote unexpected stderr.'
    }
    foreach ($kind in @('ParentGate', 'LockedDll', 'Checksum', 'StartupAbsent', 'StartupPresent')) {
        Invoke-Case ('updater-' + $kind.ToLowerInvariant()) { param($directory); Test-UpdaterCase $directory $kind }
    }
} finally {
    $packageErrors = @()
    try {
        $packageAfter = @(Get-TreeHashes $package)
        Save-Json (Join-Path $evidence 'package.after.json') $packageAfter
        Assert-SameHashes $packageBefore $packageAfter 'Published source package'
    } catch { $packageErrors += $_.ToString() }
    $passed = $results.Count -eq 6 -and @($results | Where-Object result -eq 'FAIL').Count -eq 0 -and $packageErrors.Count -eq 0
    Save-Json (Join-Path $evidence 'summary.json') @{ result = $(if ($passed) { 'PASS' } else { 'FAIL' }); finishedUtc = [DateTime]::UtcNow.ToString('O'); cases = $results.ToArray(); packageErrors = $packageErrors }
}
if (-not $passed) { throw "Published helper qualification failed. Original evidence retained at: $evidence" }
Write-Output "Published helper qualification PASS: 6 cases. Evidence: $evidence"
