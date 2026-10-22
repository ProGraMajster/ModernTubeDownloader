[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$DataRoot,
    [string]$ExpectedVersion = '',
    [switch]$CheckNoUpdate,
    [switch]$CheckBothInterfaces
)
# Run in Windows PowerShell: the OS UIA client uses the .NET Framework assemblies.
# This is an external client of the production accessibility provider, not a Debug bridge.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    [xml]$releaseProperties = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw
    $ExpectedVersion = $releaseProperties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$owned = Get-Process -Id $ProcessId
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($owned.Path).ProductVersion
if ($version -ne $ExpectedVersion) { throw 'Published executable does not match the expected release version.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($owned.MainWindowHandle)
if ($null -eq $root -or $root.Current.ProcessId -ne $ProcessId) { throw 'Owned application HWND missing.' }

function Find-Element([string]$Id, $Parent = $root) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $element = $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    throw "Published UIA element missing: $Id"
}
function Invoke-Element([string]$Id, $Parent = $root) {
    $element = Find-Element $Id $Parent
    $scroll = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$scroll)) {
        $scroll.ScrollIntoView()
    }
    $invoke = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
}
function Set-Text([string]$Id, [string]$Value, $Parent) {
    (Find-Element $Id $Parent).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Wait-Check([scriptblock]$Check, [string]$Failure, [int]$Seconds = 30) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while ($watch.Elapsed.TotalSeconds -lt $Seconds)
    throw $Failure
}
function Select-Filter([int]$Index) {
    $combo = Find-Element 'BatchFilter' $batch
    $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    if ($items.Count -le $Index) { throw 'Batch filter logical items missing.' }
    $items[$Index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Import-List([string]$File, [int]$LastOrdinal) {
    Set-Text 'BatchFilePath' $File $batch
    Invoke-Element 'BatchImport' $batch
    Wait-Check { (Find-Element 'BatchImport' $batch).Current.IsEnabled } 'Batch import did not complete.'
    [void](Find-Element ("BatchEntryStatus" + $LastOrdinal) $batch)
}

Invoke-Element 'NavDownloads'
Invoke-Element 'NavLive'
Invoke-Element 'NavOther'
[void](Find-Element 'OtherPageTitle')
Invoke-Element 'OtherOpenBatch'
$windows = [System.Windows.Automation.AutomationElement]::RootElement
$condition = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'BatchWindow'))
$timer = [Diagnostics.Stopwatch]::StartNew()
do {
    # Owned modal windows can be descendants of their owner, not desktop children.
    # Require the exact semantic ID and PID across either native tree arrangement.
    $batch = $windows.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($batch) { break }
    Start-Sleep -Milliseconds 100
} while ($timer.Elapsed.TotalSeconds -lt 15)
if (-not $batch) {
    $ownedWindows = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))
    foreach ($window in $windows.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ownedWindows)) {
        Write-Output "Owned UIA window: id=$($window.Current.AutomationId); name=$($window.Current.Name); hwnd=$($window.Current.NativeWindowHandle)"
    }
    throw 'Published Batch window did not open.'
}
[void](Find-Element 'BatchFilePath' $batch)
# These inputs live only in the harness-owned profile, never in the release package.
$txt = Join-Path $DataRoot 'release-small.txt'
@('https://example.test/watch/sample?id=small1', 'https://example.test/watch/sample?id=small2') | Set-Content -LiteralPath $txt -Encoding UTF8
Import-List $txt 2
$json = Join-Path $DataRoot 'release-valid.json'
'{"items":[{"url":"https://example.test/watch/sample?id=json1"}]}' | Set-Content -LiteralPath $json -Encoding UTF8
Import-List $json 1
$bad = Join-Path $DataRoot 'release-invalid.json'
'{"unsupported":true}' | Set-Content -LiteralPath $bad -Encoding UTF8
Set-Text 'BatchFilePath' $bad $batch
Invoke-Element 'BatchImport' $batch
Wait-Check { (Find-Element 'BatchStatus' $batch).Current.Name -match '^(Unsupported JSON file format|Nieobsługiwany format pliku JSON)' } 'Localized JSON format error missing.'
$errorText = (Find-Element 'BatchStatus' $batch).Current.Name
if ($errorText -match 'JsonException|JsonElement|List<|\[Batch\.') { throw 'Technical/unlocalized Batch error.' }
Start-Sleep -Milliseconds 1200
if ((Find-Element 'BatchStatus' $batch).Current.Name -ne $errorText) { throw 'Timer erased JSON error.' }
$large = Join-Path $DataRoot 'release-1300.txt'
1..1300 | ForEach-Object { 'https://example.test/watch/sample?id=large{0:D4}' -f $_ } | Set-Content -LiteralPath $large -Encoding UTF8
Import-List $large 20
if ((Find-Element 'BatchStatus' $batch).Current.Name -notmatch '1300') { throw '1300-entry import count missing.' }
Invoke-Element 'BatchNext' $batch
[void](Find-Element 'BatchSelect21' $batch)
Invoke-Element 'BatchPrevious' $batch
Set-Text 'BatchSearch' 'id=large1300' $batch
[void](Find-Element 'BatchSelect1300' $batch)
if ((Find-Element 'BatchNext' $batch).Current.IsEnabled) { throw 'Search did not constrain paging.' }
Set-Text 'BatchSearch' '' $batch
Select-Filter 1 # Ready: no metadata yet.
if ((Find-Element 'BatchNext' $batch).Current.IsEnabled) { throw 'Ready filter includes pending rows.' }
Select-Filter 0
# Select sixty across three pages: bounded real UI/process work, not 1300 downloads.
foreach ($page in 1..3) {
    Invoke-Element 'BatchSelectPage' $batch
    if ($page -lt 3) { Invoke-Element 'BatchNext' $batch }
}
Invoke-Element 'BatchAnalyze' $batch
Wait-Check { (Find-Element 'BatchEntryStatus60' $batch).Current.Name -match 'Ready|Gotow' } 'Selected metadata analysis failed.' 60
Wait-Check { (Find-Element 'BatchAnalyze' $batch).Current.IsEnabled } 'Analysis did not retire.'
Invoke-Element 'BatchAdd' $batch
Wait-Check { (Find-Element 'BatchAdd' $batch).Current.IsEnabled } 'Selected handoff did not complete.'
$queueFile = Join-Path $DataRoot 'queue.json'
Wait-Check { if (-not (Test-Path -LiteralPath $queueFile)) { return $false }; $saved = Get-Content -LiteralPath $queueFile -Raw | ConvertFrom-Json; $saved.Count -eq 60 } 'Handoff did not persist exactly 60 selected jobs.'
# Start the remaining preparation and cancel it through the real enabled action.
Invoke-Element 'BatchClearSelection' $batch
Invoke-Element 'BatchAnalyze' $batch
Wait-Check { (Find-Element 'BatchCancel' $batch).Current.IsEnabled } 'Cancel unavailable during analysis.'
Invoke-Element 'BatchCancel' $batch
Wait-Check { (Find-Element 'BatchAnalyze' $batch).Current.IsEnabled } 'Cancelled analysis did not retire.'
Invoke-Element 'BatchClose' $batch
Invoke-Element 'NavDownloads'
Wait-Check { (Find-Element 'QueuePagePrevious').Current.IsEnabled } 'Queue did not reveal the last admitted page.'
$beforePage = (Find-Element 'QueuePageSummary').Current.Name
Invoke-Element 'QueuePagePrevious'
if ((Find-Element 'QueuePageSummary').Current.Name -eq $beforePage) { throw 'Queue previous page did not change.' }
Invoke-Element 'QueuePageNext'
if ((Find-Element 'QueuePageSummary').Current.Name -ne $beforePage) { throw 'Queue next page did not restore page.' }
Invoke-Element 'NavOther'
$sessionFile = Get-ChildItem -LiteralPath (Join-Path $DataRoot 'Batch') -Filter session.json -Recurse |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).FileName -eq 'release-1300.txt' } | Select-Object -First 1
$session = Get-Content -LiteralPath $sessionFile.FullName -Raw | ConvertFrom-Json
Invoke-Element ('BatchRecent' + $session.Id.Replace('-', ''))
Wait-Check { $script:reopenedBatch = $windows.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition); $null -ne $script:reopenedBatch } 'Batch session did not reopen.'
$batch = $script:reopenedBatch
[void](Find-Element 'BatchSelect1' $batch)
Invoke-Element 'BatchClose' $batch
Write-Output 'Published Batch PASS: TXT/JSON/localized persistent error, 1300 imports, paging/search/filter, cross-page 60 selection/analysis/handoff, analysis cancel, reopen, queue pages. Synthetic tools; handoff cancellation qualified separately by deterministic tests.'
Invoke-Element 'NavSettings'
Invoke-Element 'SettingsCategoryUpdates'
[void](Find-Element 'ApplicationUpdateStatus')
Invoke-Element 'ApplicationUpdateCheck'
$timer.Restart()
do {
    $statePath = Join-Path $DataRoot 'Updates/state.json'
    $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
    $check = Find-Element 'ApplicationUpdateCheck'
    if ($state.LastCheck -and $check.Current.IsEnabled) { break }
    Start-Sleep -Milliseconds 100
} while ($timer.Elapsed.TotalSeconds -lt 40)
if (-not $state.LastCheck -or -not $check.Current.IsEnabled) { throw 'Published manual update check did not complete.' }
if ($CheckNoUpdate) {
    if ($state.Available -or $state.StagedArchive -or $state.InstallOnExit) { throw 'Installed stable release offered an update to itself.' }
    $status = (Find-Element 'ApplicationUpdateStatus').Current.Name
    if ($status -notin @('The installed version is current.', 'Zainstalowana wersja jest aktualna.')) {
        throw "Published update status is not Current: $status"
    }
}
Invoke-Element 'SettingsCategoryStartup'
foreach ($id in @('TrayEnabled','WindowsAutostartEnabled','StartupApply')) { [void](Find-Element $id) }
# Inspect only: never Apply startup settings or change the user's HKCU Run value.
Invoke-Element 'SettingsCategoryPersonalization'
$scale = Find-Element 'AppearanceTextScale'
$range = $scale.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
$range.SetValue(125)
$timer.Restart()
do {
    $profilePath = Join-Path $DataRoot 'appearance.json'
    $profile = if (Test-Path -LiteralPath $profilePath) { Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json } else { $null }
    if ($profile.Typography.AppTextScale -eq 125) { break }
    Start-Sleep -Milliseconds 100
} while ($timer.Elapsed.TotalSeconds -lt 15)
if ($profile.Typography.AppTextScale -ne 125) { throw 'Published text scaling was not persisted.' }
Invoke-Element 'AppearanceTextReset'
if ($CheckBothInterfaces) {
    Invoke-Element 'InterfaceModernDashboard'
    [void](Find-Element 'NavBatch')
    foreach ($page in @('Downloads','Live','Other','Batch','History','Settings','Help','About')) { Invoke-Element ('Nav' + $page) }
    Invoke-Element 'NavDownloads'
    $urlInput = Find-Element 'DownloadUrlInput'
    $urlInput.SetFocus()
    if (-not $urlInput.Current.HasKeyboardFocus) { throw 'Published Modern URL input did not acquire keyboard focus through Windows UIA.' }
    $queueAction = Find-Element 'DashboardQuickQueue'
    if ([string]::IsNullOrWhiteSpace($queueAction.Current.Name)) { throw 'Published primary queue action has no accessible name.' }
    Invoke-Element 'NavSettings'
    Invoke-Element 'SettingsCategoryPersonalization'
    Invoke-Element 'InterfaceClassic'
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'NavBatch')
    if ($root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) { throw 'Published Classic switch retained Modern chrome.' }
    Write-Output 'Published UIA: both interfaces and all Modern routes PASS; one production process.'
}
Invoke-Element 'NavDownloads'
$owned.Refresh()
if ($owned.HasExited) { throw 'Published app exited during UI smoke.' }
Write-Output "Published UIA smoke PASS: Downloads/LIVE/Other/Batch/Updates/Startup/Personalization, text 125/reset; installed=$ExpectedVersion; no-update=$CheckNoUpdate. No Debug endpoint or coordinate clicks."
$owned.CloseMainWindow() | Out-Null
$exitCondition = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ShutdownChoiceWindow'))
Wait-Check { $script:exitWindow = $windows.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $exitCondition); $null -ne $script:exitWindow } 'Pending-work exit confirmation missing.'
Invoke-Element 'ShutdownExit' $script:exitWindow
if (-not $owned.WaitForExit(30000)) { throw 'Published app did not exit gracefully after confirmation.' }
