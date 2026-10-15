[CmdletBinding()]
param(
    [string] $ModernFormsNextRoot = "",
    [switch] $AllowUncommittedFrameworkForCandidate
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'ModernTubeDownloader.slnx'
$applicationProject = Join-Path $repositoryRoot 'ModernTubeDownloader\ModernTubeDownloader.csproj'
$testProject = Join-Path $repositoryRoot 'ModernTubeDownloader.Tests\ModernTubeDownloader.Tests.csproj'
$propertiesPath = Join-Path $repositoryRoot 'Directory.Build.props'
$defaultModernFormsNextRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot '.mfn-master-worktree'))

if ([string]::IsNullOrWhiteSpace($ModernFormsNextRoot)) {
    $ModernFormsNextRoot = $defaultModernFormsNextRoot
}

$ModernFormsNextRoot = [System.IO.Path]::GetFullPath($ModernFormsNextRoot)
$buildTarget = if ([string]::Equals($ModernFormsNextRoot, $defaultModernFormsNextRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
    (Test-Path -LiteralPath $solutionPath -PathType Leaf)) { $solutionPath } else { $testProject }
$modernFormsProject = Join-Path $ModernFormsNextRoot 'ModernFormsNext\ModernFormsNext.csproj'
if (-not (Test-Path -LiteralPath $modernFormsProject -PathType Leaf)) {
    throw "ModernFormsNext was not found at '$ModernFormsNextRoot'. Pass -ModernFormsNextRoot with a checkout containing ModernFormsNext/ModernFormsNext.csproj."
}

[xml] $properties = Get-Content -LiteralPath $propertiesPath -Raw
$versionNode = $properties.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Directory.Build.props does not contain the application Version.'
}

$version = $versionNode.InnerText.Trim()
$frameworkCommit = $properties.SelectSingleNode('/Project/PropertyGroup/ModernFormsNextCommit').InnerText.Trim()
$actualFrameworkCommit = & git -C $ModernFormsNextRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualFrameworkCommit -ne $frameworkCommit) {
    throw 'ModernFormsNext checkout must match the central release pin.'
}
$frameworkChanges = & git -C $ModernFormsNextRoot status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect ModernFormsNext source checkout.' }
$frameworkProof = [ordered]@{ BaseCommit = $frameworkCommit; Uncommitted = [bool]$frameworkChanges; PatchSha256 = $null; Files = @() }
if ($AllowUncommittedFrameworkForCandidate -and $version -notmatch '^\d+\.\d+\.\d+-candidate\.\d+$') {
    throw 'Uncommitted framework qualification is allowed only for an explicit local candidate, never a stable release.'
}
if ($frameworkChanges) {
    if (-not $AllowUncommittedFrameworkForCandidate) { throw 'ModernFormsNext source checkout must be clean.' }
    $changedFiles = @(@(& git -c core.safecrlf=false -C $ModernFormsNextRoot diff --name-only HEAD) + @(& git -C $ModernFormsNextRoot ls-files --others --exclude-standard) | Sort-Object -Unique)
    $frameworkProof.Files = @(foreach ($relative in $changedFiles) {
        if ($relative -notmatch '^(ModernFormsNext\.WindowKit\.Backend\.Windows/Notifications/|ModernFormsNext\.WindowKit\.Backend\.Windows\.Tests/|ModernFormsNext\.SystemNotifications\.WinRT/|docs/)') {
            throw "Candidate framework changes outside the reviewed notification scope: $relative"
        }
        $source = Join-Path $ModernFormsNextRoot $relative
        [ordered]@{ Path = $relative; Sha256 = if (Test-Path -LiteralPath $source -PathType Leaf) { (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'deleted' } }
    })
    $canonical = ($frameworkProof.Files | ForEach-Object { $_.Path + ':' + $_.Sha256 }) -join "`n"
    $frameworkProof.PatchSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
    Write-Output "LOCAL CANDIDATE ONLY: framework $frameworkCommit plus uncommitted notification patch $($frameworkProof.PatchSha256). No publication or Git action is performed."
}
$licensePath = Join-Path $repositoryRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw 'The application LICENSE is required.' }
# Do not race another release build/package operation in this checkout, or rebuild
# assemblies held by a running testhost/app. Never kill unrelated user processes.
$mutexKey = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repositoryRoot))).Substring(0, 24)
$buildMutex = [Threading.Mutex]::new($false, "Local\MTD-Release-$mutexKey")
$ownsMutex = $false
try {
    $ownsMutex = $buildMutex.WaitOne(0)
    if (-not $ownsMutex) { throw 'Another release operation is running in this checkout.' }
    $running = @(Get-Process -Name ModernTubeDownloader,ModernTubeDownloader.NativeHost,testhost -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($running) { throw 'Close the app/testhost or disconnect the extension native bridge using this checkout before rebuilding. No processes were killed.' }
    $testHosts = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='testhost.exe'" | Where-Object {
        $_.CommandLine -match 'testhost' -and $_.CommandLine -match [regex]::Escape($repositoryRoot)
    })
    if ($testHosts) { throw 'An active testhost is using this checkout. Wait for it to finish before rebuilding.' }
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot 'publish\win-x64'
$releaseRoot = Join-Path $artifactsRoot 'release\win-x64'
$symbolsRoot = Join-Path $artifactsRoot 'symbols\win-x64'
$zipPath = Join-Path $artifactsRoot "release\ModernTubeDownloader-$version-win-x64.zip"
$symbolsZipPath = Join-Path $artifactsRoot "release\ModernTubeDownloader-$version-win-x64-symbols.zip"
$checksumPath = "$zipPath.sha256"

foreach ($path in @($publishRoot, $releaseRoot, $symbolsRoot)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the artifacts directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

foreach ($path in @($publishRoot, $releaseRoot, $symbolsRoot, (Split-Path -Parent $zipPath))) {
    [void] (New-Item -ItemType Directory -Path $path -Force)
}

foreach ($path in @($zipPath, $symbolsZipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$modernFormsProperty = "-p:ModernFormsNextRoot=$ModernFormsNextRoot"
$serialBuildProperties = @('-m:1', '/p:UseSharedCompilation=false', '-p:GeneratePackageOnBuild=false', $modernFormsProperty)
if ($frameworkProof.PatchSha256) { $serialBuildProperties += "-p:ModernFormsNextLocalPatchSha256=$($frameworkProof.PatchSha256)" }

Invoke-DotNet -Arguments @('restore', $buildTarget, $modernFormsProperty)
$validationRun = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
foreach ($configuration in @('Debug', 'Release')) {
    Invoke-DotNet -Arguments (@('build', $buildTarget, '-c', $configuration, '--no-restore', '-warnaserror') + $serialBuildProperties)
    $resultsRoot = Join-Path $artifactsRoot "validation\$validationRun\$configuration"
    Invoke-DotNet -Arguments (@('test', $testProject, '-c', $configuration, '--no-build', '--no-restore', '--logger', 'console;verbosity=minimal', '--logger', 'trx;LogFileName=release-validation.trx', '--results-directory', $resultsRoot, '--blame-hang-timeout', '3m', '--blame-hang-dump-type', 'none') + $serialBuildProperties)
    & (Join-Path $PSScriptRoot 'Assert-TestResults.ps1') -ResultsDirectory $resultsRoot
}
Push-Location $repositoryRoot
try {
    & node --test browser-extension/tests/*.test.cjs ModernTubeDownloader.Tests/WebRemote*.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Browser extension / Web Remote JavaScript tests failed.' }
}
finally { Pop-Location }
# RID/self-contained assets differ from the generic test graph. Restore that
# graph explicitly once, then publish without another implicit restore.
Invoke-DotNet -Arguments (@('restore', $applicationProject, '-r', 'win-x64', '-p:SelfContained=true', '-p:PublishProfile=win-x64-self-contained') + $serialBuildProperties)
Invoke-DotNet -Arguments @(
    'publish',
    $applicationProject,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishProfile=win-x64-self-contained',
    "-p:ModernFormsNextLocalPatchSha256=$($frameworkProof.PatchSha256)",
    $modernFormsProperty,
    '-m:1',
    '/p:UseSharedCompilation=false',
    '-p:GeneratePackageOnBuild=false',
    '-warnaserror',
    '-o', $publishRoot
)

# Native Messaging is a dedicated process with its own runtime; never bootstrap the GUI.
$nativeProject = Join-Path $repositoryRoot 'ModernTubeDownloader.NativeHost\ModernTubeDownloader.NativeHost.csproj'
$nativePublish = Join-Path $publishRoot 'native-host'
Invoke-DotNet -Arguments (@('restore', $nativeProject, '-r', 'win-x64', '-p:SelfContained=true') + $serialBuildProperties)
Invoke-DotNet -Arguments (@('publish', $nativeProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
    '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-warnaserror', '-o', $nativePublish) + $serialBuildProperties)
Get-ChildItem -LiteralPath $nativePublish -File | Where-Object Extension -in @('.pdb','.xml') | Remove-Item -Force

# Updater runs from a private staging copy after waiting for the GUI process to exit.
$updaterProject = Join-Path $repositoryRoot 'ModernTubeDownloader.Updater\ModernTubeDownloader.Updater.csproj'
$updaterPublish = Join-Path $publishRoot 'updater'
Invoke-DotNet -Arguments (@('restore', $updaterProject, '-r', 'win-x64', '-p:SelfContained=true') + $serialBuildProperties)
Invoke-DotNet -Arguments (@('publish', $updaterProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
    '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-warnaserror', '-o', $updaterPublish) + $serialBuildProperties)
Get-ChildItem -LiteralPath $updaterPublish -File | Where-Object Extension -in @('.pdb','.xml') | Remove-Item -Force

Get-ChildItem -LiteralPath $publishRoot | ForEach-Object {
    if ($_.Extension -notin @('.pdb', '.xml') -and
        $_.Name -notlike 'ModernFormsNext.WindowKit.Backend.Tools.MicroCom*') {
        Copy-Item -LiteralPath $_.FullName -Destination $releaseRoot -Recurse -Force
    }
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'SUPPORTED_SOURCES.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'SYSTEM_NOTIFICATIONS.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'COOKIE_SYNC.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'UPDATES.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'TRAY_AND_STARTUP.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'BATCH_IMPORT.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PERSONALIZATION.md') -Destination $releaseRoot
Copy-Item -LiteralPath $licensePath -Destination $releaseRoot

# Keep redistribution notices from the exact restored packages, not a moving web page.
$licensesRoot = Join-Path $releaseRoot 'licenses'
[void] (New-Item -ItemType Directory -Path $licensesRoot -Force)
Copy-Item -LiteralPath (Join-Path $ModernFormsNextRoot 'LICENSE.txt') -Destination (Join-Path $licensesRoot 'ModernFormsNext.txt')
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licensesRoot
}
$assets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'ModernTubeDownloader\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
function Copy-PackageNotices([string]$package, [string]$packageVersion, [bool]$requireLicense = $true) {
    $packageDirectory = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder "$($package.ToLowerInvariant())\$packageVersion"
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
    }
    if (-not $packageDirectory) { throw "Missing restored package notices: $package/$packageVersion" }
    $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES)\.' })
    if ($requireLicense -and -not @($notices | Where-Object Name -match '^LICENSE\.').Count) {
        # This .NET platform package supplies an SPDX expression and notices, not
        # a standalone LICENSE file. Preserve its exact attribution; the required
        # self-contained runtime MIT text is copied below (failure remains fatal).
        if ($package -ne 'System.Security.Cryptography.ProtectedData') {
            throw "Missing redistribution license: $package/$packageVersion"
        }
        $metadata = Join-Path $packageDirectory "$($package.ToLowerInvariant()).nuspec"
        [xml]$packageXml = Get-Content -LiteralPath $metadata -Raw
        $license = $packageXml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="license"]')
        if ($null -eq $license -or $license.type -ne 'expression' -or $license.InnerText -ne 'MIT') {
            throw "Expected exact MIT package metadata: $package/$packageVersion"
        }
        Copy-Item -LiteralPath $metadata -Destination (Join-Path $licensesRoot "$package-$packageVersion.nuspec")
    }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licensesRoot "$package-$packageVersion-$($notice.Name)")
    }
}
foreach ($package in @('System.Security.Cryptography.ProtectedData', 'SkiaSharp', 'SkiaSharp.HarfBuzz', 'SkiaSharp.NativeAssets.Win32', 'HarfBuzzSharp', 'HarfBuzzSharp.NativeAssets.Win32', 'System.Drawing.Common', 'QRCoder', 'Microsoft.Win32.SystemEvents', 'Microsoft.WindowsAppSDK.Base', 'Microsoft.WindowsAppSDK.Foundation', 'Microsoft.WindowsAppSDK.InteractiveExperiences', 'Microsoft.WindowsAppSDK.Runtime')) {
    $library = @($assets.libraries.Keys | Where-Object { $_.Split('/')[0] -eq $package })
    if ($library.Count -ne 1) { throw "Expected one restored version of $package." }
    Copy-PackageNotices $package $library[0].Split('/')[1] ($package -ne 'Microsoft.Win32.SystemEvents')
}
# The Windows projection is an SDK downloadDependency, not an ordinary library.
# Preserve its exact package metadata/license link, plus CsWinRT's runtime license.
$projectionVersion = $properties.SelectSingleNode('/Project/PropertyGroup/WindowsSdkPackageVersion').InnerText.Trim()
foreach ($folder in $assets.packageFolders.Keys) {
    $metadataPath = Join-Path $folder "microsoft.windows.sdk.net.ref\$projectionVersion\microsoft.windows.sdk.net.ref.nuspec"
    if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
        Copy-Item -LiteralPath $metadataPath -Destination (Join-Path $licensesRoot "Microsoft.Windows.SDK.NET.Ref-$projectionVersion.nuspec")
        break
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $licensesRoot "Microsoft.Windows.SDK.NET.Ref-$projectionVersion.nuspec"))) { throw 'Windows SDK projection metadata is missing.' }
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishRoot 'ModernTubeDownloader.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtime = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App')
if ($runtime.Count -ne 1) { throw 'Cannot identify the self-contained .NET runtime version.' }
Copy-PackageNotices 'Microsoft.NETCore.App.Runtime.win-x64' $runtime[0].version
$aspNetRuntime = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.AspNetCore.App')
if ($aspNetRuntime.Count -ne 1) { throw 'Cannot identify the self-contained ASP.NET Core runtime version.' }
Copy-PackageNotices 'Microsoft.AspNetCore.App.Runtime.win-x64' $aspNetRuntime[0].version

$forbiddenFiles = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Where-Object {
    $_.Extension -in @('.pdb', '.log', '.trx', '.mp4', '.webm', '.mkv', '.user', '.suo', '.pfx', '.p12', '.pem', '.key', '.sqlite', '.db', '.dpapi') -or
    $_.Name -like '*FakeTool*' -or
    $_.Name -like 'ModernTubeDownloader.Tests*' -or
    $_.Name -like 'ModernFormsNext.Automation*' -or
    $_.Name -like 'ModernFormsNext.WindowKit.Backend.Tools.MicroCom*' -or
    $_.Name -in @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe', 'settings.json', 'queue.json', 'history.json', 'cookies.txt')
}
if ($forbiddenFiles) {
    throw "Release folder contains forbidden files: $($forbiddenFiles.FullName -join ', ')"
}

$publishedSymbols = Get-ChildItem -LiteralPath $publishRoot -Recurse -Filter '*.pdb' -File
foreach ($symbol in $publishedSymbols) {
    $relativePath = [System.IO.Path]::GetRelativePath($publishRoot, $symbol.FullName)
    $destination = Join-Path $symbolsRoot $relativePath
    [void] (New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force)
    Copy-Item -LiteralPath $symbol.FullName -Destination $destination
}

$finalFrameworkFiles = @(@(& git -c core.safecrlf=false -C $ModernFormsNextRoot diff --name-only HEAD) + @(& git -C $ModernFormsNextRoot ls-files --others --exclude-standard) | Sort-Object -Unique)
if (($finalFrameworkFiles -join "`n") -ne (($frameworkProof.Files | ForEach-Object Path) -join "`n")) {
    throw 'Framework change set changed during qualification.'
}
foreach ($entry in $frameworkProof.Files) {
    $source = Join-Path $ModernFormsNextRoot $entry.Path
    $actual = if (Test-Path -LiteralPath $source -PathType Leaf) { (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'deleted' }
    if ($actual -ne $entry.Sha256) { throw "Framework source changed during qualification: $($entry.Path)" }
}
if ($frameworkProof.Uncommitted) {
    $frameworkProof | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'framework-provenance.json') -Encoding utf8NoBOM
}
Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -ZipPath $zipPath
if ($publishedSymbols.Count -gt 0) {
    Compress-Archive -Path (Join-Path $symbolsRoot '*') -DestinationPath $symbolsZipPath -CompressionLevel Optimal
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding ascii

[pscustomobject]@{
    Version = $version
    Runtime = 'win-x64'
    Deployment = 'self-contained, multi-file, untrimmed'
    ReleaseDirectory = $releaseRoot
    Zip = $zipPath
    ZipBytes = (Get-Item -LiteralPath $zipPath).Length
    SymbolsZip = if (Test-Path -LiteralPath $symbolsZipPath) { $symbolsZipPath } else { $null }
    Sha256 = $hash
}
}
finally {
    if ($ownsMutex) { $buildMutex.ReleaseMutex() }
    $buildMutex.Dispose()
}
