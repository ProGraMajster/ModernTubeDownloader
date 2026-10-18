[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$DataRoot,
    [string]$ExpectedVersion = '1.2.0',
    [switch]$CheckNoUpdate,
    [switch]$CheckBothInterfaces
)
# Run in Windows PowerShell: the OS UIA client uses the .NET Framework assemblies.
# This is an external client of the production accessibility provider, not a Debug bridge.
$ErrorActionPreference = 'Stop'
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
Invoke-Element 'BatchClose' $batch
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
