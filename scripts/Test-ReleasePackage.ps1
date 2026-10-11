[CmdletBinding()]
param([Parameter(Mandatory)][string] $ZipPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ZipPath))
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($name in $names) {
        if ($name -match '(^/|(^|/)\.\.(/|$)|^[A-Za-z]:)' -or
            $name -match '(?i)(^|/)(\.codex|Auth|profiles?|cache|logs|TestResults|bin|obj|tools|screenshots)(/|$)' -or
            $name -match '(?i)(FakeTool|ModernTubeDownloader\.Tests|ModernFormsNext\.(Automation|Testing)|MicroCom)' -or
            $name -match '(?i)\.(pdb|trx|log|mp4|mkv|webm|user|suo|pfx|p12|pem|key|sqlite|db|dpapi)$' -or
            $name -match '(?i)(^|/)(yt-dlp\.exe|ffmpeg\.exe|ffprobe\.exe|deno\.exe|settings\.json|queue\.json|history\.json|live\.json|cookies\.txt|bridge\.json|bridge-health\.json|native-(chrome|edge|brave|chromium)\.json)$') {
            throw "Forbidden or unsafe package entry: $name"
        }
    }
    foreach ($required in @('ModernTubeDownloader.exe', 'ModernTubeDownloader.dll', 'ModernFormsNext.dll', 'ModernTubeDownloader.Authentication.dll',
        'native-host/ModernTubeDownloader.NativeHost.exe', 'native-host/ModernTubeDownloader.NativeHost.dll', 'COOKIE_SYNC.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md')) {
        if ($required -notin $names) { throw "Missing package entry: $required" }
    }
    Write-Output "Package hygiene PASS: $($names.Count) entries."
}
finally { $archive.Dispose() }
