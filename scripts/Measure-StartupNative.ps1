[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Before,
    [Parameter(Mandatory)][string]$After,
    [Parameter(Mandatory)][string]$FixtureProbe,
    [Parameter(Mandatory)][string]$FakeToolDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [int[]]$Sizes = @(1000, 5000, 10000, 25000, 50000),
    [ValidateSet('Classic','Modern')][string[]]$Interfaces = @('Classic','Modern'),
    [ValidateRange(1, 10)][int]$Repetitions = 3
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
$ui = [Windows.Automation.AutomationElement]
$tree = [Windows.Automation.TreeScope]::Descendants
$utf8 = New-Object Text.UTF8Encoding($false)
$beforeExe = (Resolve-Path -LiteralPath $Before).Path
$afterExe = (Resolve-Path -LiteralPath $After).Path
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
    $fixture = & dotnet $probe $root $records 0 40 $fake
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture generation failed.' }
    [IO.File]::WriteAllLines((Join-Path $evidence "$name-fixture.json"), [string[]]@($fixture), $utf8)
    Save-Json (Join-Path $root 'appearance.json') @{
        schemaVersion = 2; interfaceMode = [int]($mode -eq 'Modern')
        global = @{ theme = 2; animations = $false }
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
    $summary = [ordered]@{ label=$label; mode=$mode; records=$records; iteration=$iteration; pid=$child.Id }
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
        Start-Sleep -Milliseconds 1250
        $child.Refresh()
        $summary.workingSet = $child.WorkingSet64
        $summary.privateBytes = $child.PrivateMemorySize64
        $summary.handles = $child.HandleCount
        $summary.closePosted = $child.CloseMainWindow()
        if (!$child.WaitForExit(12000)) { throw 'The isolated GUI did not exit within 12 seconds.' }
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
        Save-Json (Join-Path $evidence "$name.result.json") $summary
        $child.Dispose()
    }
    [pscustomobject]$summary
}

$results = foreach ($size in $Sizes) {
    foreach ($mode in $Interfaces) {
        for ($i = 1; $i -le $Repetitions; $i++) {
            $variants = if ($i % 2) { 'before','after' } else { 'after','before' }
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
