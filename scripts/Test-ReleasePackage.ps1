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
        'updater/ModernTubeDownloader.Updater.exe', 'updater/ModernTubeDownloader.Updater.dll', 'UPDATES.md', 'TRAY_AND_STARTUP.md', 'BATCH_IMPORT.md', 'PERSONALIZATION.md', 'COOKIE_SYNC.md',
        'ONBOARDING.md', 'FILE_ORGANIZER_SAFETY.md', 'STARTUP_UPDATE_POLICY.md', 'LOCALIZATION_COVERAGE.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md')) {
        if ($required -notin $names) { throw "Missing package entry: $required" }
    }
    if ($names -contains 'ModernTubeDownloader.Updater.dll') {
        throw 'The GUI package must keep the external updater in its own directory.'
    }
    $packageJson = @{}
    foreach ($file in @('ModernTubeDownloader.deps.json', 'ModernTubeDownloader.runtimeconfig.json',
        'updater/ModernTubeDownloader.Updater.deps.json', 'updater/ModernTubeDownloader.Updater.runtimeconfig.json')) {
        $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $file }
        if (-not $entry) { throw "Missing package manifest: $file" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $packageJson[$file] = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
    }
    $appDeps = $packageJson['ModernTubeDownloader.deps.json']
    $runnerDeps = $packageJson['updater/ModernTubeDownloader.Updater.deps.json']
    if (@($appDeps.libraries.PSObject.Properties.Name -like 'ModernTubeDownloader.Updater/*').Count -or
        @($runnerDeps.libraries.PSObject.Properties.Name -like 'ModernTubeDownloader.Updater/*').Count -ne 1) {
        throw 'The GUI and external updater dependencies are not isolated.'
    }
    foreach ($config in @('ModernTubeDownloader.runtimeconfig.json', 'updater/ModernTubeDownloader.Updater.runtimeconfig.json')) {
        if (@($packageJson[$config].runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').Count -ne 1) {
            throw "Missing self-contained runtime in $config"
        }
    }
    Write-Output "Package hygiene PASS: $($names.Count) entries."
}
finally { $archive.Dispose() }
