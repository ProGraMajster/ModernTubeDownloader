[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceZip,
    [Parameter(Mandatory)][string]$SourceSha256,
    [Parameter(Mandatory)][string]$CandidateZip,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source = (Resolve-Path -LiteralPath $SourceZip).Path
$candidate = (Resolve-Path -LiteralPath $CandidateZip).Path
if ((Get-FileHash -LiteralPath $source).Hash -ne $SourceSha256) { throw 'The archived source ZIP does not have the expected SHA-256.' }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a new evidence directory.' }
if (Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue) { throw 'The app is already running.' }
$evidence = [IO.Directory]::CreateDirectory($EvidenceDirectory).FullName
$profile = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) ('mtd-automation-smoke-stage2-migration-' + [guid]::NewGuid().ToString('N')))).FullName
$install = Join-Path $evidence 'installation'
[IO.Compression.ZipFile]::ExtractToDirectory($source, $install)
$runner = [IO.Directory]::CreateDirectory((Join-Path $evidence 'runner')).FullName
Copy-Item -Path (Join-Path $install 'updater\*') -Destination $runner -Recurse
$workerExe = Join-Path $runner 'ModernTubeDownloader.Updater.exe'
$appExe = Join-Path $install 'ModernTubeDownloader.exe'
$userPath = Join-Path $install 'synthetic-user-file.txt'
[IO.File]::WriteAllText($userPath, 'Must stay unchanged.')
$originalVersion = (Get-Item -LiteralPath $appExe).VersionInfo.ProductVersion
$oldOnly = [Collections.Generic.List[object]]::new()
$expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$incoming = [IO.Compression.ZipFile]::OpenRead($candidate)
$archive = [IO.Compression.ZipFile]::OpenRead($source)
try {
    foreach ($entry in $incoming.Entries) { [void]$expectedPaths.Add($entry.FullName.Replace('\', '/')) }
    foreach ($entry in $archive.Entries) {
        $relative = $entry.FullName.Replace('\', '/')
        if ($entry.Name -and !$expectedPaths.Contains($relative)) {
            $file = Join-Path $install $relative
            $oldOnly.Add([ordered]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $file).Hash })
        }
    }
} finally { $incoming.Dispose(); $archive.Dispose() }

$utf8 = New-Object Text.UTF8Encoding($false)
function Save-Json([string]$path, $data) { [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $data -Depth 12), $utf8) }
Save-Json (Join-Path $profile 'settings.json') @{
    language='en'; browserIntegrationEnabled=$false; updates=@{ automaticChecks=$false; policyVersion=1 }
    notifications=@{ enabled=$false }; enableWebRemote=$false
    finalOutputDirectory=(Join-Path $profile 'output'); temporaryDirectory=(Join-Path $profile 'temporary')
    startup=@{ closeBehavior=0; showTrayIcon=$false }
}
Save-Json (Join-Path $profile 'experience.json') @{
    schemaVersion=1; existingProfile=$true; setup=2; interfaceChoiceFamily='1.3'
    lastObservedVersion=$originalVersion; lastWhatsNewAcknowledgedVersion=$originalVersion
}
$hash = (Get-FileHash -LiteralPath $candidate).Hash
$request = Join-Path $runner 'request.json'
# The synthetic parent PID has already exited. Its start time distinguishes it if reused.
$markerStart = [Diagnostics.ProcessStartInfo]::new("$env:windir\System32\cmd.exe", '/d /q /c "pause >nul"')
$markerStart.UseShellExecute = $false
$markerStart.CreateNoWindow = $true
$markerStart.RedirectStandardInput = $true
$startMarker = [Diagnostics.Process]::Start($markerStart)
$parentPid = $startMarker.Id
$parentTicks = $startMarker.StartTime.ToUniversalTime().Ticks
$startMarker.StandardInput.WriteLine('x')
$startMarker.StandardInput.Close()
if (!$startMarker.WaitForExit(5000)) { $startMarker.Kill(); throw 'The synthetic parent did not exit.' }
$startMarker.Dispose()
Save-Json $request @{
    Installation=$install; Archive=$candidate; Sha256=$hash; ParentProcessId=$parentPid
    ParentStartUtcTicks=$parentTicks; Restart=$true; OperationId=[guid]::NewGuid()
}
$start = [Diagnostics.ProcessStartInfo]::new($workerExe, ('--request "' + $request + '"'))
$start.UseShellExecute = $false
$start.WorkingDirectory = $runner
$start.EnvironmentVariables['MODERNTUBEDOWNLOADER_DATA_ROOT'] = $profile
$start.EnvironmentVariables['MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL'] = '1'
$start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:1'
$start.EnvironmentVariables['HTTP_PROXY'] = 'http://127.0.0.1:1'
$start.EnvironmentVariables['NO_PROXY'] = ''
$worker = $null
$gui = $null
$summary = [ordered]@{ sourceVersion=$originalVersion; sourceSha256=$SourceSha256; candidateSha256=$hash; oldOnly=$oldOnly.ToArray() }
try {
    $worker = [Diagnostics.Process]::Start($start)
    if (!$worker.WaitForExit(90000)) { throw 'The archived updater did not finish within 90 seconds.' }
    $summary.updaterExitCode = $worker.ExitCode
    $summary.installer = Get-Content -LiteralPath ($request + '.result.json') -Raw | ConvertFrom-Json
    if (!$summary.installer.success -or $worker.ExitCode -ne 0) { throw 'Archived updater did not commit the candidate.' }
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $apps = @([Diagnostics.Process]::GetProcessesByName('ModernTubeDownloader') | Where-Object { $_.Path -eq $appExe })
        if ($apps.Count -eq 1) {
            $gui = $apps[0]
            $gui.Refresh()
            if ($gui.MainWindowHandle -ne [IntPtr]::Zero) { break }
        }
        Start-Sleep -Milliseconds 40
    } while ($wait.Elapsed.TotalSeconds -lt 20)
    if (!$gui -or $gui.HasExited -or $gui.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The updated application did not display an owned window.' }
    $runtime = @($gui.Modules | Where-Object ModuleName -in 'coreclr.dll', 'hostpolicy.dll')
    if ($runtime.Count -ne 2 -or @($runtime | Where-Object {
        $_.FileName -ne (Join-Path $install $_.ModuleName) -or $_.FileVersionInfo.ProductVersion -notmatch '^11[.,]'
    }).Count) { throw 'The migrated application did not load its packaged .NET 11 runtime.' }
    $summary.runtime = @($runtime | ForEach-Object { @{ path=$_.FileName; version=$_.FileVersionInfo.ProductVersion } })
    $summary.targetVersion = (Get-Item -LiteralPath $appExe).VersionInfo.ProductVersion
    if ($summary.targetVersion -notlike '1.4.0-dev.*') { throw 'Wrong installed target version.' }
    foreach ($entry in $oldOnly) {
        if ((Get-FileHash -LiteralPath (Join-Path $install $entry.path)).Hash -ne $entry.sha256) { throw 'A path absent from the candidate changed.' }
    }
    if ([IO.File]::ReadAllText($userPath) -ne 'Must stay unchanged.') { throw 'A path owned by the user changed.' }
    $summary.result = 'PASS'
} catch {
    $summary.result = 'FAIL'; $summary.error = $_.Exception.Message
    throw
} finally {
    foreach ($child in @($gui, $worker)) {
        if (!$child) { continue }
        if (!$child.HasExited) {
            [void]$child.CloseMainWindow()
            if (!$child.WaitForExit(10000)) { $child.Kill(); [void]$child.WaitForExit(4000) }
        }
        $child.Dispose()
    }
    Save-Json (Join-Path $evidence 'result.json') $summary
}
