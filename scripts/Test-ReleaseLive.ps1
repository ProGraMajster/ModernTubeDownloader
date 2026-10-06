[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceUrl,
    [Parameter(Mandatory)][string]$YtDlpPath,
    [Parameter(Mandatory)][string]$FfmpegPath,
    [Parameter(Mandatory)][string]$FfprobePath,
    [Parameter(Mandatory)][string]$DenoPath,
    [ValidateRange(5, 90)][int]$CaptureSeconds = 30,
    [switch]$ParallelDownloads,
    [switch]$RestartDuringCapture,
    [switch]$AutoResumeAfterRestart,
    [switch]$InterruptDownloader,
    [switch]$NoRetryAfterInterruption
)

if ($RestartDuringCapture -and $InterruptDownloader) {
    throw 'Choose only one interruption mode per smoke run.'
}
if ($AutoResumeAfterRestart -and -not $RestartDuringCapture) {
    throw 'AutoResumeAfterRestart requires RestartDuringCapture.'
}
if ($NoRetryAfterInterruption -and -not $InterruptDownloader) {
    throw 'NoRetryAfterInterruption requires InterruptDownloader.'
}

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $repo 'artifacts/release/win-x64/ModernTubeDownloader.exe'
foreach ($required in @($app, $YtDlpPath, $FfmpegPath, $FfprobePath, $DenoPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing binary: $required" }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('mtd-release-live-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$sessionId = [Guid]::NewGuid()
$queueId = $sessionId
@{
    BrowserIntegrationEnabled = $false
    Language = 'en'
    TemporaryDirectory = (Join-Path $root 'temp')
    FinalOutputDirectory = (Join-Path $root 'output')
    UseCustomYtDlp = $true
    CustomYtDlpPath = $YtDlpPath
    UseCustomFfmpeg = $true
    CustomFfmpegPath = $FfmpegPath
    CustomFfprobePath = $FfprobePath
    UseCustomDeno = $true
    CustomDenoPath = $DenoPath
    EnableWebRemote = $true
    WebRemoteRequireAuthentication = $false
    WebRemoteBindAddress = '127.0.0.1'
    WebRemotePort = $port
    ResumeLiveOnFailure = [bool]($InterruptDownloader -and -not $NoRetryAfterInterruption)
    MaxSimultaneousDownloads = 3
    MaxSimultaneousLiveRecordings = 1
    EnableInterDownloadDelay = $false
    AdditionalRetryAttempts = if ($InterruptDownloader -and -not $NoRetryAfterInterruption) { 1 } else { 0 }
    AutoResumeLiveAfterRestart = [bool]$AutoResumeAfterRestart
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'settings.json') -Encoding utf8
$initialLive = @(@{
    sessionId = $sessionId
    sourceUrl = $SourceUrl
    mediaId = 'release-live-smoke'
    title = 'Release LIVE smoke'
    qualityPresetId = '720'
    state = 0
    startPolicy = 0
    partIndex = 1
    resumeEnabled = [bool]($InterruptDownloader -and -not $NoRetryAfterInterruption)
})
ConvertTo-Json -InputObject $initialLive -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'live-sessions.json') -Encoding utf8
'[]' | Set-Content -LiteralPath (Join-Path $root 'queue.json') -Encoding utf8

$previousRoot = $env:MODERNTUBEDOWNLOADER_DATA_ROOT
$env:MODERNTUBEDOWNLOADER_DATA_ROOT = $root
$process = $null
try {
    $process = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
    $base = "http://127.0.0.1:$port"
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    $item = $null
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'Published application exited before recording.' }
        try { $items = @(Invoke-RestMethod -Uri "$base/api/live" -TimeoutSec 3) }
        catch { $items = @() }
        $item = $items | Where-Object { [string]$_.id -eq [string]$queueId } | Select-Object -First 1
        if ($item -and $item.state -eq 'Recording') { break }
        if ($item -and $item.state -in @('Failed','Partial')) { throw "LIVE entered $($item.state) before capture." }
        Start-Sleep -Seconds 1
    } while ($deadline.Elapsed.TotalSeconds -lt 45)
    if (-not $item -or $item.state -ne 'Recording') { throw 'Published application did not enter Recording.' }

    $maxVodActive = 0
    $maxCaptureChildren = 0
    if ($ParallelDownloads) {
        Invoke-RestMethod -Method Post -Uri "$base/api/queue/pause" -TimeoutSec 10 | Out-Null
        # Three bounded sections of Blender's public Sintel video. The remote
        # keeps its production YouTube-only allowlist; the harness does not bypass it.
        foreach ($offset in @(0,2,4)) {
            $body = @{ url='https://www.youtube.com/watch?v=eRsGyueVLvQ'
                quality='360'; container='Mkv'; rangeMode='custom'
                from=([TimeSpan]::FromSeconds($offset).ToString('hh\:mm\:ss'))
                to=([TimeSpan]::FromSeconds($offset+8).ToString('hh\:mm\:ss')) } | ConvertTo-Json
            Invoke-RestMethod -Method Post -Uri "$base/api/queue/add" -ContentType 'application/json' -Body $body -TimeoutSec 40 | Out-Null
        }
        Invoke-RestMethod -Method Post -Uri "$base/api/queue/resume" -TimeoutSec 10 | Out-Null
        $parallelClock = [System.Diagnostics.Stopwatch]::StartNew()
        do {
            # Invoke-RestMethod writes a JSON array as one pipeline object;
            # explicitly enumerate it before per-job counts/assertions.
            $vodItems = @(Invoke-RestMethod -Uri "$base/api/queue" -TimeoutSec 3 | Write-Output)
            $activeCount = @($vodItems | Where-Object status -in @('Waiting','DownloadingVideo','DownloadingAudio','Merging','Finalizing')).Count
            $maxVodActive = [Math]::Max($maxVodActive, $activeCount)
            $maxCaptureChildren = [Math]::Max($maxCaptureChildren, @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" |
                Where-Object { $_.Name -eq 'yt-dlp.exe' -and $_.CommandLine -like "*$root*" -and $_.CommandLine -like '*--no-simulate*' }).Count)
            $liveNow = @(Invoke-RestMethod -Uri "$base/api/live" -TimeoutSec 3)[0]
            if ($liveNow.state -ne 'Recording') { throw 'LIVE stopped while VOD workers were running.' }
            if (@($vodItems | Where-Object status -eq 'Completed').Count -eq 3) { break }
            if (@($vodItems | Where-Object status -in @('Failed','Partial','Cancelled')).Count) { throw 'A parallel VOD failed.' }
            Start-Sleep -Milliseconds 700
        } while ($parallelClock.Elapsed.TotalSeconds -lt 45)
        if ($maxVodActive -ne 3 -or @($vodItems | Where-Object status -eq 'Completed').Count -ne 3) {
            throw "Parallel VOD did not prove three active jobs and three completions; peak=$maxVodActive."
        }
        Write-Output "REAL PARALLEL PASS: VOD peak=$maxVodActive + LIVE=1; yt-dlp capture children peak=$maxCaptureChildren"
    }

    if ($InterruptDownloader) {
        Start-Sleep -Seconds 7
        $target = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" |
            Where-Object { $_.Name -eq 'yt-dlp.exe' -and
                $_.CommandLine -like '*--downloader ffmpeg*' -and
                $_.CommandLine -like "*$root*" })
        if ($target.Count -ne 1) { throw "Expected exactly one isolated yt-dlp capture process; found $($target.Count)." }
        Stop-Process -Id $target[0].ProcessId -Force
        $deadline.Restart()
        do {
            try { $items = @(Invoke-RestMethod -Uri "$base/api/live" -TimeoutSec 3) }
            catch { $items = @() }
            $item = $items | Where-Object { [string]$_.id -eq [string]$queueId } | Select-Object -First 1
            if ($NoRetryAfterInterruption -and $item -and $item.state -eq 'Partial') { break }
            if (-not $NoRetryAfterInterruption -and $item -and $item.state -eq 'Recording' -and $item.retryCount -ge 1) { break }
            if (($item -and $item.state -eq 'Failed') -or
                (-not $NoRetryAfterInterruption -and $item -and $item.state -eq 'Partial')) {
                throw "Interrupted yt-dlp ended as $($item.state), not the expected recovery state."
            }
            Start-Sleep -Seconds 1
        } while ($deadline.Elapsed.TotalSeconds -lt 45)
        if ($NoRetryAfterInterruption -and $item.state -ne 'Partial') {
            throw 'Published LIVE did not preserve a Partial recording after retry exhaustion.'
        }
        if (-not $NoRetryAfterInterruption -and
            (-not $item -or $item.state -ne 'Recording' -or $item.retryCount -lt 1)) {
            throw 'Published LIVE did not reconnect after controlled yt-dlp termination.'
        }
    }

    if ($RestartDuringCapture) {
        Start-Sleep -Seconds 7
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(15000)) {
            throw 'Published application did not close gracefully during LIVE.'
        }
        $process.Dispose()
        $process = $null
        $interrupted = @(Get-Content -LiteralPath (Join-Path $root 'live-sessions.json') -Raw | ConvertFrom-Json)[0]
        if ($interrupted.state -ne 10) { throw "Published LIVE did not persist Interrupted ($($interrupted.state))." }
        $rawParts = @(Get-ChildItem -LiteralPath (Join-Path $root 'temp/LiveSessions') -File -Recurse |
            Where-Object { $_.Length -gt 0 -and $_.Name -notmatch '\.(json|ytdl|log)$' })
        if ($rawParts.Count -eq 0) { throw 'Published LIVE lost all raw capture data during shutdown.' }
        $process = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
        if (-not $AutoResumeAfterRestart) {
            $deadline.Restart()
            do {
                $process.Refresh()
                if ($process.HasExited) { throw 'Published application exited after restart.' }
                try { $items = @(Invoke-RestMethod -Uri "$base/api/live" -TimeoutSec 3) }
                catch { $items = @() }
                $item = $items | Where-Object { [string]$_.id -eq [string]$queueId } | Select-Object -First 1
                if ($item -and $item.state -eq 'Interrupted') { break }
                Start-Sleep -Seconds 1
            } while ($deadline.Elapsed.TotalSeconds -lt 20)
            if (-not $item -or $item.state -ne 'Interrupted') { throw 'Published LIVE was not resumable after restart.' }
            Invoke-RestMethod -Method Post -Uri "$base/api/live/$queueId/resume" -TimeoutSec 10 | Out-Null
        }
        $deadline.Restart()
        do {
            $items = @(Invoke-RestMethod -Uri "$base/api/live" -TimeoutSec 3)
            $item = $items | Where-Object { [string]$_.id -eq [string]$queueId } | Select-Object -First 1
            if ($item -and $item.state -eq 'Recording' -and $item.partsCount -ge 1) { break }
            if ($item -and $item.state -in @('Failed','Partial')) { throw "Resumed LIVE entered $($item.state)." }
            Start-Sleep -Seconds 1
        } while ($deadline.Elapsed.TotalSeconds -lt 45)
        if (-not $item -or $item.state -ne 'Recording' -or $item.partsCount -lt 1) {
            throw 'Published LIVE did not resume recording after preserving Part 1.'
        }
    }

    if (-not $NoRetryAfterInterruption) {
        Start-Sleep -Seconds $CaptureSeconds
        Invoke-RestMethod -Method Post -Uri "$base/api/live/$queueId/stop" -TimeoutSec 10 | Out-Null
        $deadline.Restart()
        do {
            $items = @(Invoke-RestMethod -Uri "$base/api/live" -TimeoutSec 3)
            $item = $items | Where-Object { [string]$_.id -eq [string]$queueId } | Select-Object -First 1
            if ($item.state -in @('Completed','Partial','Failed')) { break }
            Start-Sleep -Seconds 1
        } while ($deadline.Elapsed.TotalSeconds -lt 45)
        if ($item.state -ne 'Completed') { throw "Published LIVE ended as $($item.state)." }
    }

    $saved = @(Get-Content -LiteralPath (Join-Path $root 'live-sessions.json') -Raw | ConvertFrom-Json)[0]
    $allHistory = @(Get-Content -LiteralPath (Join-Path $root 'history.json') -Raw | ConvertFrom-Json)
    $history = @($allHistory | Where-Object wasLiveRecording -eq $true)
    $expectedParts = if ($RestartDuringCapture -or
        ($InterruptDownloader -and -not $NoRetryAfterInterruption)) { 2 } else { 1 }
    if (-not (Test-Path -LiteralPath $saved.parts[-1] -PathType Leaf) -or
        $history.Count -ne $expectedParts -or @($saved.parts).Count -ne $expectedParts -or
        (@($history | ForEach-Object { [int]$_.livePartIndex } | Sort-Object) -join ',') -ne
        ((1..$expectedParts) -join ',')) {
        throw 'Published LIVE has no verified output or History part.'
    }
    $vodPersistence = @(Get-Content -LiteralPath (Join-Path $root 'queue.json') -Raw | ConvertFrom-Json)
    if (@($vodPersistence | Where-Object { $_.liveSession }).Count) { throw 'LIVE leaked into normal queue persistence.' }
    $probe = $null
    foreach ($entry in $history) {
        if (-not (Test-Path -LiteralPath $entry.finalPath -PathType Leaf)) {
            throw "Published LIVE History part $($entry.livePartIndex) is missing."
        }
        $partProbe = & $FfprobePath -v error -show_entries 'format=duration,format_name:stream=codec_type' -of json $entry.finalPath | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or [double]$partProbe.format.duration -le 0 -or
            @($partProbe.streams | Where-Object codec_type -eq 'video').Count -eq 0 -or
            @($partProbe.streams | Where-Object codec_type -eq 'audio').Count -eq 0) {
            throw "Published LIVE part $($entry.livePartIndex) failed ffprobe verification."
        }
        & $FfmpegPath -v error -i $entry.finalPath -t 2 -f null - 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Published LIVE part $($entry.livePartIndex) failed decode sanity." }
        if ($entry.finalPath -eq $saved.parts[-1]) { $probe = $partProbe }
    }
    if (-not $probe) { throw 'Published LIVE final file is missing from History.' }
    [pscustomobject]@{
        Result = 'PASS'
        FinalState = $item.state
        Profile = $root
        File = $saved.parts[-1]
        Bytes = (Get-Item -LiteralPath $saved.parts[-1]).Length
        DurationSeconds = [double]$probe.format.duration
        HistoryCount = $history.Count
        ParallelVodPeak = $maxVodActive
        CaptureChildPeak = $maxCaptureChildren
        LiveSessionId = $sessionId
    }
}
finally {
    $env:MODERNTUBEDOWNLOADER_DATA_ROOT = $previousRoot
    if ($process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit() }
        }
        $process.Dispose()
    }
    Write-Output "Release LIVE profile retained: $root"
}
