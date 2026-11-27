[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [ValidateSet('Offline','Continue','CurrentStable')][string]$Scenario = 'Offline',
    [ValidateSet('Light','Dark')][string]$Theme = 'Dark',
    [ValidateSet('Classic','Modern')][string]$Interface = 'Modern'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Drawing
$witness = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-FeaturePerformanceNative.ps1') -Raw
if ($witness -notmatch "(?s)Add-Type -ReferencedAssemblies System.Drawing @'\r?\n(.*?)\r?\n'@") { throw 'Missing capture helper.' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $Matches[1]
$exe = (Resolve-Path -LiteralPath $Application).Path
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a new evidence directory.' }
if (Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue) { throw 'The app is already running.' }
$evidence = [IO.Directory]::CreateDirectory($EvidenceDirectory).FullName
$profile = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) ('mtd-automation-smoke-stage2-startup-' + [guid]::NewGuid().ToString('N')))).FullName
$utf8 = New-Object Text.UTF8Encoding($false)
$ui = [Windows.Automation.AutomationElement]
$tree = [Windows.Automation.TreeScope]::Descendants
$proxy = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$proxy.Start()
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
$result = [ordered]@{ version=$version; scenario=$Scenario; theme=$Theme; interface=$Interface; captures=@() }

function Save-Json([string]$name,$data) {
    [IO.File]::WriteAllText((Join-Path $profile $name),(ConvertTo-Json -InputObject $data -Depth 10),$utf8)
}
Save-Json 'settings.json' @{
    language='en'; browserIntegrationEnabled=$false
    updates=@{ automaticChecks=$true; frequency=3; mode=0; policyVersion=1 }
    notifications=@{ enabled=$false }; enableWebRemote=$false
    finalOutputDirectory=(Join-Path $profile 'output'); temporaryDirectory=(Join-Path $profile 'temporary')
    startup=@{ closeBehavior=0; showTrayIcon=$false }
}
Save-Json 'appearance.json' @{
    schemaVersion=2; interfaceMode=[int]($Interface -eq 'Modern')
    global=@{ theme=$(if ($Theme -eq 'Light') { 1 } else { 2 }); animations=$false }
}
Save-Json 'experience.json' @{
    schemaVersion=1; existingProfile=$true; setup=2; interfaceChoiceFamily='1.3'
    lastObservedVersion=$version; lastWhatsNewAcknowledgedVersion=$version
}

function Child-Control($parent,[string]$id) {
    $condition = [Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty,$id)
    return $parent.FindFirst($tree,$condition)
}
function Capture($window,[string]$file,[string]$label) {
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    [void][PerfWindow]::Capture($hwnd,$child.Id,(Join-Path $evidence ($file + '.png')))
    $result.captures += [ordered]@{file=$file; label=$label; dpi=[PerfWindow]::GetDpiForWindow($hwnd)}
}

$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute = $false
$start.WorkingDirectory = Split-Path -Parent $exe
$start.EnvironmentVariables['MODERNTUBEDOWNLOADER_DATA_ROOT'] = $profile
$start.EnvironmentVariables['MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL'] = '1'
if ($Scenario -ne 'CurrentStable') {
    $start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:' + $proxy.LocalEndpoint.Port
    $start.EnvironmentVariables['HTTP_PROXY'] = $start.EnvironmentVariables['HTTPS_PROXY']
    $start.EnvironmentVariables['NO_PROXY'] = ''
}
$child = $null
try {
    $child = [Diagnostics.Process]::Start($start)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $seen = [Collections.Generic.HashSet[string]]::new()
    $main = $null
    do {
        $child.Refresh()
        if ($child.HasExited) { throw 'The candidate exited before the main window.' }
        if ($child.MainWindowHandle -ne [IntPtr]::Zero) {
            try {
                $window = $ui::FromHandle($child.MainWindowHandle)
                # Dismissed startup windows may lose their UIA provider between refresh and query.
                if ($window.Current.ProcessId -ne $child.Id) { continue }
                if ($window.Current.AutomationId -eq 'MainWindow' -and !$window.Current.IsOffscreen) {
                    $main = $window
                    break
                }
                if ($window.Current.AutomationId -eq 'StartupUpdateWindow') {
                    $status = Child-Control $window 'StartupUpdateStatus'
                    if ($status -and $status.Current.Name -and $seen.Add($status.Current.Name)) {
                        $label = $status.Current.Name
                        Capture $window ("startup-" + $seen.Count) $label
                        if ($label -match '^Checking for updates' -and $Scenario -eq 'Continue') {
                            (Child-Control $window 'StartupUpdateContinue').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
                            $result.continued = $true
                        }
                    }
                }
            } catch [Windows.Automation.ElementNotAvailableException] {}
        }
        Start-Sleep -Milliseconds 18
    } while ($clock.Elapsed.TotalSeconds -lt 25)
    if (!$main) { throw 'The candidate did not show a main window.' }
    Capture $main 'main-tools' 'Main window with isolated tools'
    (Child-Control $main 'NavHistory').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $clock.Restart()
    do {
        $history = Child-Control $main 'HistorySearch'
        if ($history -and !$history.Current.IsOffscreen) { break }
        Start-Sleep -Milliseconds 35
    } while ($clock.Elapsed.TotalSeconds -lt 8)
    if (!$history -or $history.Current.IsOffscreen) { throw 'History did not remain reachable during unavailable tools.' }
    Capture $main 'history' 'History available independently of tools'

    $settings = Child-Control $main 'NavSettings'
    if (!$settings.Current.IsEnabled) { throw 'Settings navigation is disabled.' }
    if (!$child.CloseMainWindow() -or !$child.WaitForExit(12000) -or $child.ExitCode -ne 0) { throw 'The isolated GUI did not exit cleanly.' }
    if ($Scenario -eq 'Continue' -and !$result.continued) { throw 'The native update skip action was not observed.' }
    $events = @(Get-ChildItem -Path (Join-Path $profile 'Logs') -Filter 'MTD-events-*.log' -File | ForEach-Object {
        Get-Content -LiteralPath $_.FullName | ForEach-Object { $_ | ConvertFrom-Json }
    })
    $startup = $events | Where-Object eventName -eq 'Application.Startup.Started' | Select-Object -First 1
    if (!$startup -or !$startup.operationId) { throw 'The application did not assign a startup operation.' }
    $result.events = @($events | Where-Object operationId -eq $startup.operationId)
    if (!($result.events | Where-Object eventName -eq 'Application.Startup.Completed')) { throw 'Startup completion was not logged.' }
    if ($Scenario -eq 'CurrentStable') {
        $state = Get-Content -LiteralPath (Join-Path $profile 'Updates\state.json') -Raw | ConvertFrom-Json
        if (!$state.LastCheck -or $state.Available -or $state.StagedArchive) { throw 'The stable feed did not resolve as already current.' }
        $result.lastStableCheck = $state.LastCheck
    }
    $result.result = 'PASS'
} catch {
    $result.result = 'FAIL'; $result.error = $_.Exception.Message
    throw
} finally {
    $proxy.Stop()
    if ($child) {
        if (!$child.HasExited) {
            [void]$child.CloseMainWindow()
            if (!$child.WaitForExit(4000)) { $child.Kill(); [void]$child.WaitForExit(4000) }
        }
        $child.Dispose()
    }
    [IO.File]::WriteAllText((Join-Path $evidence 'result.json'),(ConvertTo-Json -InputObject $result -Depth 12),$utf8)
}
