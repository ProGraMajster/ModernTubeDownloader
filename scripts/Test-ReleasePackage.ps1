[CmdletBinding()]
param([Parameter(Mandatory)][string] $ZipPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ZipPath))
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($name in $names) {
        if ($name -match '(^/|(^|/)\.\.(/|$)|^[A-Za-z]:)' -or
            $name -match '(?i)(^|/)(\.codex|Auth|Batch|Updates|CrashReports|\.mtd-update-backup|profiles?|qa-profiles|test-profiles|cache|logs|TestResults|bin|obj|tools|screenshots)(/|$)' -or
            $name -match '(?i)(FakeTool|ModernTubeDownloader\.Tests|ModernFormsNext\.(Automation|Testing)|MicroCom)' -or
            $name -match '(?i)\.(pdb|trx|log|mp4|mkv|webm|user|suo|pfx|p12|pem|key|sqlite|db|dpapi)$' -or
            $name -match '(?i)(^|/)(yt-dlp\.exe|ffmpeg\.exe|ffprobe\.exe|deno\.exe|settings\.json|appearance\.json|session\.json|entries\.jsonl|updates\.jsonl|queue\.json|history\.json|live(?:-sessions)?\.json|cookies\.txt|bridge\.json|bridge-health\.json|native-(chrome|edge|brave|chromium)\.json)$') {
            throw "Forbidden or unsafe package entry: $name"
        }
    }
    foreach ($required in @('ModernTubeDownloader.exe', 'ModernTubeDownloader.dll', 'ModernFormsNext.dll', 'ModernTubeDownloader.Authentication.dll',
        'native-host/ModernTubeDownloader.NativeHost.exe', 'native-host/ModernTubeDownloader.NativeHost.dll',
        'updater/ModernTubeDownloader.Updater.exe', 'updater/ModernTubeDownloader.Updater.dll', 'UPDATES.md', 'TRAY_AND_STARTUP.md', 'BATCH_IMPORT.md', 'PERSONALIZATION.md', 'COOKIE_SYNC.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md')) {
        if ($required -notin $names) { throw "Missing package entry: $required" }
    }
    Write-Output "Package hygiene PASS: $($names.Count) entries."
}
finally { $archive.Dispose() }
