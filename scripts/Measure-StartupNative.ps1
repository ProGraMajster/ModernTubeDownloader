[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Before,
    [string]$After,
    [ValidateSet('Both','Before','After')][string]$Variant = 'Both',
    [Parameter(Mandatory)][string]$FixtureProbe,
    [Parameter(Mandatory)][string]$FakeToolDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [int[]]$Sizes = @(1000, 5000, 10000, 25000, 50000, 100000),
    [ValidateSet('Classic','Modern')][string[]]$Interfaces = @('Classic','Modern'),
    [ValidateRange(1, 10)][int]$Repetitions = 3,
    [ValidateRange(0, 10000)][int]$ThumbnailCount = 40,
    [switch]$MixedFixture,
    [ValidateRange(0, 1000)][int]$ViewCycles = 0,
    [ValidateSet('Standard','Debug','Trace')][string]$DiagnosticMode = 'Standard'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
$ui = [Windows.Automation.AutomationElement]
$tree = [Windows.Automation.TreeScope]::Descendants
$utf8 = New-Object Text.UTF8Encoding($false)
$beforeExe = (Resolve-Path -LiteralPath $Before).Path
$afterExe = if ($Variant -ne 'Before') {
    if (!$After) { throw 'After is required for Both or After.' }
    (Resolve-Path -LiteralPath $After).Path
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StartupResources {
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flag);
}
'@
$probe = (Resolve-Path -LiteralPath $FixtureProbe).Path
$fake = (Resolve-Path -LiteralPath $FakeToolDirectory).Path
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a new evidence directory.' }
$evidence = [IO.Directory]::CreateDirectory($EvidenceDirectory).FullName
if (Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue) { throw 'The app is already running.' }

function Find-Control($window, [string]$automationId) {
    $condition = [Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty, $automationId)
    return $window.FindFirst($tree, $condition)
}

function Save-Json([string]$path, $data) {
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $data -Depth 12), $utf8)
}

function Run-Trial([string]$label, [string]$exe, [int]$records, [string]$mode, [int]$iteration) {
    $name = '{0}-{1}-{2}-{3}' -f $records,$mode,$iteration,$label
    $root = Join-Path ([IO.Path]::GetTempPath()) ('mtd-automation-smoke-stage2-perf-' + [guid]::NewGuid().ToString('N'))
    $queueRecords = if ($MixedFixture) { [Math]::Min(10000, [Math]::Max(200, [int]($records / 5))) } else { 0 }
    $fixtureArgs = @($root,$records,$queueRecords,$ThumbnailCount,$fake)
    if ($MixedFixture) { $fixtureArgs += '--scalability' }
    $fixture = & dotnet $probe @fixtureArgs
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture generation failed.' }
    [IO.File]::WriteAllLines((Join-Path $evidence "$name-fixture.json"), [string[]]@($fixture), $utf8)
    Save-Json (Join-Path $root 'appearance.json') @{
        schemaVersion = 2; interfaceMode = [int]($mode -eq 'Modern')
        global = @{ theme = 2; animations = $false }
    }

    if ($DiagnosticMode -ne 'Standard') {
        $settingsFile = Join-Path $root 'settings.json'
        $settings = Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $settings | Add-Member -Force NoteProperty diagnostics @{ advanced=$true; includeTrace=($DiagnosticMode -eq 'Trace') }
        Save-Json $settingsFile $settings
    }
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.WorkingDirectory = Split-Path -Parent $exe
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['MODERNTUBEDOWNLOADER_DATA_ROOT'] = $root
    $start.EnvironmentVariables['MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL'] = '1'
    $start.EnvironmentVariables['MTD_PERFORMANCE_DIAGNOSTICS'] = '1'
    $start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:1'
    $start.EnvironmentVariables['HTTP_PROXY'] = 'http://127.0.0.1:1'
    $start.EnvironmentVariables['NO_PROXY'] = ''
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $child = [Diagnostics.Process]::Start($start)
    $summary = [ordered]@{ label=$label; mode=$mode; records=$records; iteration=$iteration; pid=$child.Id; fixtureQueue=$queueRecords; thumbnails=$ThumbnailCount; diagnosticMode=$DiagnosticMode }
    try {
        $first = $null
        $main = $null
        do {
            $child.Refresh()
            if ($child.HasExited) { throw "App exited with $($child.ExitCode) before its main window." }
            if ($child.MainWindowHandle -ne [IntPtr]::Zero) {
                if ($null -eq $first) { $first = $watch.Elapsed.TotalMilliseconds }
                try {
                    $candidate = $ui::FromHandle($child.MainWindowHandle)
                    if ($candidate.Current.ProcessId -eq $child.Id -and $candidate.Current.AutomationId -eq 'MainWindow' -and
                        -not $candidate.Current.IsOffscreen -and (Find-Control $candidate 'NavHistory')) {
                        $main = $candidate
                        break
                    }
                } catch [Windows.Automation.ElementNotAvailableException] {}
            }
            Start-Sleep -Milliseconds 15
        } while ($watch.Elapsed.TotalSeconds -lt 30)
        if ($null -eq $main) { throw 'Main window did not become visible within 30 seconds.' }
        $summary.firstWindowMs = $first
        $summary.mainNavigationReadyMs = $watch.Elapsed.TotalMilliseconds
        $history = [Diagnostics.Stopwatch]::StartNew()
        (Find-Control $main 'NavHistory').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $expected = $false
        do {
            $page = Find-Control $main 'HistoryPageSummary'
            if ($page -and !$page.Current.IsOffscreen) {
                $counts = @([regex]::Matches($page.Current.Name, '[0-9]+') | ForEach-Object { [int]$_.Value })
                $expected = $counts.Count -eq 4 -and $counts[0] -eq 1 -and $counts[2] -eq $records -and $counts[3] -eq $records
                if ($expected) { break }
            }
            Start-Sleep -Milliseconds 20
        } while ($history.Elapsed.TotalSeconds -lt 15)
        if (!$expected) { throw "History did not confirm the $records-record fixture." }

        $summary.historyReadyMs = $history.Elapsed.TotalMilliseconds
        $search = (Find-Control $main 'HistorySearch').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
        function Wait-Matches([int]$expectedHits) {
            $deadline = [Diagnostics.Stopwatch]::StartNew()
            do {
                $display = Find-Control $main 'HistoryPageSummary'
                if ($display -and !$display.Current.IsOffscreen) {
                    $numbers = @([regex]::Matches($display.Current.Name, '[0-9]+') | ForEach-Object { [int]$_.Value })
                    if ($numbers.Count -eq 4 -and $numbers[2] -eq $expectedHits -and $numbers[3] -eq $records) { return }
                }
                Start-Sleep -Milliseconds 20
            } while ($deadline.Elapsed.TotalSeconds -lt 15)
            throw "History did not publish $expectedHits expected results."
        }
        $searchClock = [Diagnostics.Stopwatch]::StartNew()
        $search.SetValue('SYNTHETIC QA 00049')
        Wait-Matches 1
        $summary.searchMs = $searchClock.Elapsed.TotalMilliseconds
        $search.SetValue('')
        Wait-Matches $records
        $navigation = [Collections.Generic.List[double]]::new()
        for ($cycle = 0; $cycle -lt $ViewCycles; $cycle++) {
            $switch = [Diagnostics.Stopwatch]::StartNew()
            (Find-Control $main 'NavDownloads').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            do {
                $downloads = Find-Control $main 'DownloadUrlInput'
                if ($downloads -and !$downloads.Current.IsOffscreen) { break }
                Start-Sleep -Milliseconds 20
            } while ($switch.Elapsed.TotalSeconds -lt 15)
            if (!$downloads -or $downloads.Current.IsOffscreen) { throw 'Downloads view did not become visible.' }
            (Find-Control $main 'NavHistory').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Wait-Matches $records
            $navigation.Add($switch.Elapsed.TotalMilliseconds)
        }
        $summary.historyRoundTripsMs = $navigation.ToArray()
        Start-Sleep -Milliseconds 1250
        $child.Refresh()
        $summary.workingSet = $child.WorkingSet64
        $summary.privateBytes = $child.PrivateMemorySize64
        $summary.handles = $child.HandleCount
        $summary.threads = $child.Threads.Count
        $summary.cpuMs = $child.TotalProcessorTime.TotalMilliseconds
        $summary.gdi = [StartupResources]::GetGuiResources($child.Handle, 0)
        $summary.user = [StartupResources]::GetGuiResources($child.Handle, 1)
        $summary.diagnosticBytes = [long](Get-ChildItem -LiteralPath (Join-Path $root 'Logs') -Filter '*.log' -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        $summary.closePosted = $child.CloseMainWindow()
        $shutdown = [Diagnostics.Stopwatch]::StartNew()
        $position = [Windows.Automation.PropertyCondition]::new($ui::ProcessIdProperty,$child.Id)
        $confirmed = $false
        while (!$child.HasExited -and $shutdown.Elapsed.TotalSeconds -lt 12) {
            if (!$confirmed) {
                $ownedWindows = $ui::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$position)
                foreach ($ownedWindow in $ownedWindows) {
                    $exit = Find-Control $ownedWindow 'ShutdownExit'
                    if ($exit -and $exit.Current.IsEnabled -and $exit.Current.ProcessId -eq $child.Id) {
                        $exit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                        $confirmed = $true
                        break
                    }
                }
            }
            if ($child.WaitForExit(100)) { break }
        }
        if (!$child.HasExited) { throw 'The isolated GUI did not exit within 12 seconds.' }
        $summary.exitCode = $child.ExitCode
        if ($child.ExitCode -ne 0) { throw 'The isolated GUI returned an error.' }
        $summary.result = 'PASS'
    } catch {
        $summary.result = 'FAIL'
        $summary.error = $_.Exception.Message
    } finally {
        if (!$child.HasExited) {
            [void]$child.CloseMainWindow()
            if (!$child.WaitForExit(4000)) { $child.Kill(); [void]$child.WaitForExit(4000) }
        }
        $perfFile = Join-Path $root ('performance-' + $child.Id + '.jsonl')
        if (Test-Path -LiteralPath $perfFile) { Copy-Item -LiteralPath $perfFile -Destination (Join-Path $evidence "$name-events.jsonl") }
        $log = Join-Path $root "Logs"
        if (Test-Path -LiteralPath $log) {
            $logs = @(Get-ChildItem -LiteralPath $log -Filter "MTD-events-v1-*.log" -File)
            $summary.diagnosticBytes = [long]($logs | Measure-Object Length -Sum).Sum
            $summary.diagnosticEvents = 0; $summary.droppedEvents = 0
            foreach ($file in $logs) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $evidence "$name-$($file.Name)")
                foreach ($line in [IO.File]::ReadLines($file.FullName)) {
                    $summary.diagnosticEvents++
                    $entry = $line | ConvertFrom-Json
                    if ($entry.eventName -eq "Logging.Run.Skipped") {
                        $summary.droppedEvents += [long]$entry.counts.dropped + [long]$entry.counts.errors
                    }
                }
            }
        }
        Save-Json (Join-Path $evidence "$name.result.json") $summary
        $child.Dispose()
    }
    [pscustomobject]$summary
}

$results = foreach ($size in $Sizes) {
    foreach ($mode in $Interfaces) {
        for ($i = 1; $i -le $Repetitions; $i++) {
            $variants = if ($Variant -ne 'Both') { $Variant.ToLowerInvariant() } elseif ($i % 2) { 'before','after' } else { 'after','before' }
            foreach ($variant in $variants) {
                $exe = if ($variant -eq 'before') { $beforeExe } else { $afterExe }
                $result = Run-Trial $variant $exe $size $mode $i
                Write-Host "$size $mode $i $variant $($result.result)"
                $result
            }
        }
    }
}
Save-Json (Join-Path $evidence 'trials.json') @($results)
if ($results.result -contains 'FAIL') { throw 'At least one performance trial failed. See the trial evidence.' }
