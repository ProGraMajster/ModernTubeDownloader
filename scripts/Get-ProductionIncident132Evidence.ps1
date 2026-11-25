#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installation,
    [string]$ProfileRoot = (Join-Path $env:LOCALAPPDATA 'ModernTubeDownloader'),
    [string]$Source,
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

function Get-FileEvidence([string]$Relative) {
    $path = Join-Path $Installation $Relative
    $item = [ordered]@{ file = $Relative; present = $false }
    try {
        $attributes = [IO.File]::GetAttributes($path)
        $item.present = $true
        if ($attributes -band ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint)) {
            $item.state = 'DirectoryOrReparsePoint'
            return [pscustomobject]$item
        }
        $item.sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($Relative.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
            $item.assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString()
        }
        elseif ($Relative.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
            $item.fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
        }
        elseif ($Relative.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase)) {
            if (([IO.FileInfo]::new($path)).Length -gt 4MB) { throw [IO.InvalidDataException]::new() }
            $deps = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            $item.updaterEntries = @($deps.libraries.PSObject.Properties.Name |
                Where-Object { $_ -match '^ModernTubeDownloader\.Updater/\d+(\.\d+){1,3}$' })
        }
    }
    catch {
        $errorDetail = $_.Exception.GetBaseException()
        $item.errorType = $errorDetail.GetType().Name
        $item.hresult = '0x{0:X8}' -f $errorDetail.HResult
    }
    [pscustomobject]$item
}

function Get-FolderEvidence([string]$Role, [string]$Requested) {
    if ([string]::IsNullOrWhiteSpace($Requested)) { return [pscustomobject]@{ role = $Role; state = 'Empty' } }
    if ($Requested -notmatch '^[a-zA-Z]:[\\/]') { return [pscustomobject]@{ role = $Role; state = 'NotAnAbsoluteDrivePath' } }
    $evidence = [ordered]@{ role = $Role }
    $stage = 'Normalize'
    try {
        $full = [IO.Path]::GetFullPath($Requested)
        $root = [IO.Path]::GetPathRoot($full)
        $drive = [IO.DriveInfo]::new($root)
        $evidence.driveType = $drive.DriveType.ToString()
        if ($drive.DriveType -eq [IO.DriveType]::Network) {
            $evidence.state = 'Network'
            return [pscustomobject]$evidence
        }
        $stage = 'Root'
        $null = [IO.File]::GetAttributes($root)
        $stage = 'Ancestors'
        $current = $root
        foreach ($part in ($full.Substring($root.Length).Split('\') | Where-Object { $_ })) {
            $current = Join-Path $current $part
            $attributes = [IO.File]::GetAttributes($current)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
                $evidence.state = 'ReparsePoint'
                return [pscustomobject]$evidence
            }
            if (!($attributes -band [IO.FileAttributes]::Directory)) {
                $evidence.state = 'FileInDirectoryPath'
                return [pscustomobject]$evidence
            }
        }
        $evidence.state = 'Accessible'
    }
    catch {
        $errorDetail = $_.Exception.GetBaseException()
        $evidence.state = $stage + 'Failed'
        $evidence.errorType = $errorDetail.GetType().Name
        $evidence.hresult = '0x{0:X8}' -f $errorDetail.HResult
    }
    [pscustomobject]$evidence
}

$sessionCounts = [ordered]@{ supported = 0; explicitTerminalMarker = 0; metadataMissing = 0; invalidSchemaIdOrPhase = 0; unreadable = 0; rootUnreadable = $false }
$batch = Join-Path $ProfileRoot 'Batch'
try {
    if ([IO.Directory]::Exists($batch)) {
        foreach ($directory in ([IO.Directory]::EnumerateDirectories($batch) | Where-Object { [IO.Path]::GetFileName($_) -match '^[0-9a-f]{32}$' })) {
            $id = [IO.Path]::GetFileName($directory)
            $metadata = Join-Path $directory 'session.json'
            try {
                if ([IO.File]::GetAttributes($directory) -band [IO.FileAttributes]::ReparsePoint) { throw [IO.InvalidDataException]::new() }
                if (![IO.File]::Exists($metadata)) {
                    if ([IO.File]::Exists((Join-Path $directory 'import-terminal.json'))) { $sessionCounts.explicitTerminalMarker++ }
                    else { $sessionCounts.metadataMissing++ }
                    continue
                }
                if (([IO.FileInfo]::new($metadata)).Length -gt 1MB) { throw [IO.InvalidDataException]::new() }
                $session = [IO.File]::ReadAllText($metadata) | ConvertFrom-Json
                $sessionId = [guid]::Empty
                if ($session.schemaVersion -ne 1 -or ![guid]::TryParse([string]$session.id, [ref]$sessionId) -or
                    $sessionId.ToString('N') -ne $id -or ($null -ne $session.workflowPhase -and $session.workflowPhase -notmatch '^[0-9]$')) {
                    $sessionCounts.invalidSchemaIdOrPhase++
                }
                else { $sessionCounts.supported++ }
            }
            catch { $sessionCounts.unreadable++ }
        }
    }
}
catch { $sessionCounts.rootUnreadable = $true }

$identity = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$files = @('ModernTubeDownloader.exe','ModernTubeDownloader.dll','ModernTubeDownloader.deps.json','ModernTubeDownloader.runtimeconfig.json',
    'ModernTubeDownloader.Updater.dll','updater\ModernTubeDownloader.Updater.exe','updater\ModernTubeDownloader.Updater.dll',
    'updater\ModernTubeDownloader.Updater.deps.json','updater\ModernTubeDownloader.Updater.runtimeconfig.json',
    'updater\hostfxr.dll','updater\hostpolicy.dll','updater\coreclr.dll')
[pscustomobject]@{
    windowsVersion = [Environment]::OSVersion.Version.ToString()
    elevated = $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    files = @($files | ForEach-Object { Get-FileEvidence $_ })
    folders = @((Get-FolderEvidence 'Source' $Source), (Get-FolderEvidence 'Destination' $Destination))
    batch = $sessionCounts
} | ConvertTo-Json -Depth 5
