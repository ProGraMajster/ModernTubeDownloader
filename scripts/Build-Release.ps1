[CmdletBinding()]
param(
    [string] $ModernFormsNextRoot = ""
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
$buildTarget = if ([string]::Equals($ModernFormsNextRoot, $defaultModernFormsNextRoot, [System.StringComparison]::OrdinalIgnoreCase)) { $solutionPath } else { $testProject }
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
if ($LASTEXITCODE -ne 0 -or $frameworkChanges) { throw 'ModernFormsNext source checkout must be clean.' }
$licensePath = Join-Path $repositoryRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw 'The application LICENSE is required.' }
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
$serialBuildProperties = @('-m:1', '/p:UseSharedCompilation=false', $modernFormsProperty)

Invoke-DotNet -Arguments @('restore', $buildTarget, $modernFormsProperty)
$validationRun = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
foreach ($configuration in @('Debug', 'Release')) {
    Invoke-DotNet -Arguments (@('build', $buildTarget, '-c', $configuration, '--no-restore', '-warnaserror') + $serialBuildProperties)
    $resultsRoot = Join-Path $artifactsRoot "validation\$validationRun\$configuration"
    Invoke-DotNet -Arguments (@('test', $testProject, '-c', $configuration, '--no-build', '--logger', 'trx;LogFileName=release-validation.trx', '--results-directory', $resultsRoot) + $serialBuildProperties)
    [xml] $testResults = Get-Content -LiteralPath (Join-Path $resultsRoot 'release-validation.trx') -Raw
    $counters = $testResults.TestRun.ResultSummary.Counters
    if ([int]$counters.total -eq 0 -or [int]$counters.total -ne [int]$counters.passed) {
        throw "$configuration must have passing tests with no skipped tests."
    }
}
Push-Location $repositoryRoot
try {
    & node --test browser-extension/tests/*.test.cjs ModernTubeDownloader.Tests/WebRemoteTimeInput.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Browser extension / Web Remote JavaScript tests failed.' }
}
finally { Pop-Location }
Invoke-DotNet -Arguments @(
    'publish',
    $applicationProject,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishProfile=win-x64-self-contained',
    $modernFormsProperty,
    '-m:1',
    '/p:UseSharedCompilation=false',
    '-warnaserror',
    '-o', $publishRoot
)

Get-ChildItem -LiteralPath $publishRoot | ForEach-Object {
    if ($_.Extension -notin @('.pdb', '.xml') -and
        $_.Name -notlike 'ModernFormsNext.WindowKit.Backend.Tools.MicroCom*') {
        Copy-Item -LiteralPath $_.FullName -Destination $releaseRoot -Recurse -Force
    }
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'SUPPORTED_SOURCES.md') -Destination $releaseRoot
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
    $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)\.' })
    if ($requireLicense -and -not @($notices | Where-Object Name -match '^LICENSE\.').Count) {
        throw "Missing redistribution license: $package/$packageVersion"
    }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licensesRoot "$package-$packageVersion-$($notice.Name)")
    }
}
foreach ($package in @('SkiaSharp', 'SkiaSharp.HarfBuzz', 'SkiaSharp.NativeAssets.Win32', 'HarfBuzzSharp', 'HarfBuzzSharp.NativeAssets.Win32', 'System.Drawing.Common', 'QRCoder', 'Microsoft.Win32.SystemEvents')) {
    $library = @($assets.libraries.Keys | Where-Object { $_.Split('/')[0] -eq $package })
    if ($library.Count -ne 1) { throw "Expected one restored version of $package." }
    Copy-PackageNotices $package $library[0].Split('/')[1] ($package -ne 'Microsoft.Win32.SystemEvents')
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishRoot 'ModernTubeDownloader.runtimeconfig.json') -Raw | ConvertFrom-Json
$runtime = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App')
if ($runtime.Count -ne 1) { throw 'Cannot identify the self-contained .NET runtime version.' }
Copy-PackageNotices 'Microsoft.NETCore.App.Runtime.win-x64' $runtime[0].version
$aspNetRuntime = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.AspNetCore.App')
if ($aspNetRuntime.Count -ne 1) { throw 'Cannot identify the self-contained ASP.NET Core runtime version.' }
Copy-PackageNotices 'Microsoft.AspNetCore.App.Runtime.win-x64' $aspNetRuntime[0].version

$forbiddenFiles = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Where-Object {
    $_.Extension -in @('.pdb', '.log', '.trx', '.mp4', '.webm', '.mkv', '.user', '.suo') -or
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

Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
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
