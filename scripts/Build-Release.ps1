[CmdletBinding()]
param(
    [string] $ModernFormsNextRoot = ""
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'ModernTubeDownloader.slnx'
$applicationProject = Join-Path $repositoryRoot 'ModernTubeDownloader\ModernTubeDownloader.csproj'
$propertiesPath = Join-Path $repositoryRoot 'Directory.Build.props'

if ([string]::IsNullOrWhiteSpace($ModernFormsNextRoot)) {
    $ModernFormsNextRoot = Join-Path $repositoryRoot '.mfn-master-worktree'
}

$ModernFormsNextRoot = [System.IO.Path]::GetFullPath($ModernFormsNextRoot)
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

Invoke-DotNet -Arguments @('restore', $solutionPath, $modernFormsProperty)
Invoke-DotNet -Arguments (@('build', $solutionPath, '-c', 'Debug', '--no-restore') + $serialBuildProperties)
Invoke-DotNet -Arguments (@('build', $solutionPath, '-c', 'Release', '--no-restore') + $serialBuildProperties)
Invoke-DotNet -Arguments (@('test', $solutionPath, '-c', 'Release', '--no-build') + $serialBuildProperties)
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

$forbiddenFiles = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Where-Object {
    $_.Extension -eq '.pdb' -or
    $_.Name -like '*FakeTool*' -or
    $_.Name -like 'ModernFormsNext.WindowKit.Backend.Tools.MicroCom*' -or
    $_.Name -in @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe')
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
