#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$OwnedProcessId,
    [Parameter(Mandatory)][long]$StartUtcTicks,
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidateSet('Draft', 'Complete', 'Restart')][string]$Phase
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5) { throw 'Run this client using Windows PowerShell 5.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class PublishedExperienceWindow {
    delegate bool EnumCallback(IntPtr hwnd, IntPtr value);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr value);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static IntPtr[] OwnedWindows(int pid) {
        var found = new List<IntPtr>();
        EnumWindows((h, unused) => { uint p; GetWindowThreadProcessId(h, out p); if(p == pid && IsWindowVisible(h)) found.Add(h); return true; }, IntPtr.Zero);
        return found.ToArray();
    }
    public static Rect Capture(IntPtr hwnd, int pid, string path) {
        uint actual; GetWindowThreadProcessId(hwnd, out actual);
        if(actual != pid || !IsWindowVisible(hwnd)) throw new InvalidOperationException("Capture target is not the visible owned HWND.");
        var old = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            Rect rect; if(!GetWindowRect(hwnd, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) throw new InvalidOperationException("Empty window bounds.");
            using(var bitmap = new System.Drawing.Bitmap(rect.Right-rect.Left, rect.Bottom-rect.Top))
            using(var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
                var dc = graphics.GetHdc();
                try { if(!PrintWindow(hwnd, dc, 2)) throw new InvalidOperationException("PrintWindow failed."); }
                finally { graphics.ReleaseHdc(dc); }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            return rect;
        } finally { SetThreadDpiAwarenessContext(old); }
    }
}
'@
$events = [Collections.Generic.List[object]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$errorText = $null
$experiencePath = Join-Path $DataRoot 'experience.json'
$output = Join-Path $DataRoot 'wizard-output'
$temporary = Join-Path $DataRoot 'wizard-temporary'
$owned = Get-Process -Id $OwnedProcessId
if ($owned.Path -ne $ApplicationPath -or $owned.StartTime.ToUniversalTime().Ticks -ne $StartUtcTicks) { throw 'Owned PID/path/start-time mismatch.' }
# Get-Process attaches by PID rather than starting the child. Retain its native
# process handle before shutdown so .NET Framework can read the real exit code
# after the process object has disappeared from the system process list.
[void]$owned.Handle

function Record([string]$Action, $Detail) { $events.Add(@{ action = $Action; elapsedMs = $watch.Elapsed.TotalMilliseconds; utc = [DateTime]::UtcNow.ToString('O'); detail = $Detail }) }
function Assert-Alive {
    $owned.Refresh()
    if ($owned.HasExited) { throw 'Owned application exited before scenario completion.' }
}
function Wait-Condition([scriptblock]$Condition, [string]$Failure, [int]$Seconds = 15) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Alive
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100 # Bounded external UI observation, never a completion guess.
    } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    throw $Failure
}
function Window-Now([string]$Id) {
    foreach ($handle in [PublishedExperienceWindow]::OwnedWindows($OwnedProcessId)) {
        $element = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        if ($element -and $element.Current.ProcessId -eq $OwnedProcessId -and $element.Current.AutomationId -eq $Id) { return $element }
    }
    return $null
}
function Wait-Window([string]$Id, [int]$Seconds = 15) {
    $script:foundWindow = $null
    Wait-Condition { $script:foundWindow = Window-Now $Id; $null -ne $script:foundWindow } "Window missing: $Id" $Seconds
    Record 'window.ready' @{ id = $Id; hwnd = $script:foundWindow.Current.NativeWindowHandle }
    return $script:foundWindow
}
function Find-Now($Parent, [string]$Id) {
    return $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Find($Parent, [string]$Id) {
    $script:foundElement = $null
    Wait-Condition { $script:foundElement = Find-Now $Parent $Id; $null -ne $script:foundElement } "Control missing: $Id"
    return $script:foundElement
}
function Invoke($Parent, [string]$Id) {
    $element = Find $Parent $Id
    Wait-Condition { $element.Current.IsEnabled } "Control remains disabled: $Id"
    $scroll = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$scroll)) { $scroll.ScrollIntoView() }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Record 'invoke' $Id
}
function Set-Text($Parent, [string]$Id, [string]$Value) {
    $element = Find $Parent $Id
    Wait-Condition { $element.Current.IsEnabled -and (Find-Now $Parent 'SetupNext').Current.IsEnabled } "Wizard input remains disabled: $Id"
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
    Wait-Condition { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $Value } "Text was not applied: $Id"
    Record 'value.set' $Id
}
function Read-Experience {
    if (-not (Test-Path -LiteralPath $experiencePath -PathType Leaf)) { return $null }
    # Call only after a UI boundary that follows the awaited application save.
    # Windows Move(overwrite) can fail even with delete sharing while this handle
    # is open. Never poll persistence during a write; read once and fail on I/O.
    # Close the snapshot handle before JSON parsing as an additional precaution.
    $stream=$null; $snapshot=$null
    try {
        $stream=[IO.File]::Open($experiencePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $snapshot=[IO.MemoryStream]::new()
        $stream.CopyTo($snapshot)
        $bytes=$snapshot.ToArray()
    }
    finally { if ($stream) { $stream.Dispose() }; if ($snapshot) { $snapshot.Dispose() } }
    return [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF) | ConvertFrom-Json
}
function Step($Wizard, [int]$Index, [string]$ControlId) {
    # MoveToStepAsync awaits persistence before BuildStep changes this range;
    # RunAsync then enables the body/Next. The exact numeric step also prevents
    # the shared Next button or the textual denominator "6" satisfying step 6.
    $stepProgress = Find $Wizard 'SetupStepProgress'
    Wait-Condition {
        $range = $stepProgress.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current
        $control = Find-Now $Wizard $ControlId
        $next = Find-Now $Wizard 'SetupNext'
        $range.Minimum -eq 0 -and $range.Maximum -eq 6 -and $range.Value -eq ($Index + 1) -and
            $control -and $control.Current.IsEnabled -and -not $control.Current.IsOffscreen -and
            $next -and $next.Current.IsEnabled
    } "Wizard did not finish building visible step $Index."
    $progress = (Find $Wizard 'SetupProgress').Current.Name
    if ($progress -notmatch [regex]::Escape([string]($Index + 1)) -or $progress -notmatch '6') { throw "Visible progress disagrees with six-step draft: $progress" }
    $saved = Read-Experience
    if (-not $saved -or $saved.setup -ne 1 -or $saved.draft.step -ne $Index) { throw "Draft step $Index was not persisted." }
    Record 'step.verified' @{ zeroBasedStep = $Index; visibleProgress = $progress; numericProgress = $Index + 1; durableSnapshotAfterUiReady = $true }
    return $saved
}
function Get-CaptureHash([string]$Path) {
    # The Windows PowerShell UIA child can inherit PowerShell 7's module search
    # path. Use the .NET Framework crypto API directly; do not depend on optional
    # cmdlet auto-loading or weaken the screenshot identity assertion.
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $hash.Dispose(); $stream.Dispose() }
}
function Capture($Window, [string]$Name) {
    $path = Join-Path $EvidenceDirectory ($Name + '.png')
    $rect = [PublishedExperienceWindow]::Capture([IntPtr]$Window.Current.NativeWindowHandle, $OwnedProcessId, $path)
    Record 'capture' @{ file = [IO.Path]::GetFileName($path); width = $rect.Right - $rect.Left; height = $rect.Bottom - $rect.Top; sha256 = (Get-CaptureHash $path) }
}
function Close-Window($Window) { $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
function Assert-PersistedSettings {
    $settings = Get-Content -LiteralPath (Join-Path $DataRoot 'settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $appearance = Get-Content -LiteralPath (Join-Path $DataRoot 'appearance.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $state = Read-Experience
    if ($settings.finalOutputDirectory -ne $output -or $settings.temporaryDirectory -ne $temporary -or $appearance.interfaceMode -ne 1) { throw 'Completed preferences were not durably applied.' }
    if (-not $settings.updates.automaticChecks -or $settings.updates.mode -ne 0 -or $settings.updates.installAtStartup) { throw 'Explicit safe update policy was not saved.' }
    if ($state.existingProfile -or $state.setup -ne 2 -or $null -ne $state.draft -or $state.interfaceChoiceFamily -ne '1.3') { throw 'Fresh-profile completion/provenance is incorrect.' }
    Record 'settings.verified' @{ modern = $true; syntheticFolders = $true; automaticChecks = $true; mode = 'NotifyOnly'; installAtStartup = $false }
    return $state
}

try {
    $main = Wait-Window 'MainWindow' 30
    if ($Phase -eq 'Draft') {
        # No synthetic tools: allow first-launch provisioning up to 180 seconds,
        # reported separately from UI action readiness and never silently retried.
        $wizard = Wait-Window 'SetupWizardWindow' 180
        [void](Step $wizard 0 'SetupLanguage'); Capture $wizard '01-welcome'
        Invoke $wizard 'SetupNext'
        [void](Step $wizard 1 'SetupModern'); Invoke $wizard 'SetupModern'
        $selectedMarker = ' ' + [char]0x00B7 + ' '
        Wait-Condition { (Find-Now $wizard 'SetupModern').Current.Name.Contains($selectedMarker) -and -not (Find-Now $wizard 'SetupClassic').Current.Name.Contains($selectedMarker) } 'Modern choice was not visibly selected.'
        Capture $wizard '02-interface-choice'
        Invoke $wizard 'SetupNext'
        $state = Step $wizard 2 'SetupOutput'
        if ($state.draft.interface -ne 1) { throw 'Modern choice was not saved.' }
        Set-Text $wizard 'SetupOutput' $output
        Set-Text $wizard 'SetupTemporary' $temporary
        Capture $wizard '03-folders-before-interruption'
        Close-Window $wizard
        Wait-Condition { $null -eq (Window-Now 'SetupWizardWindow') } 'Wizard did not close after saving its draft.'
        $state = Read-Experience
        if ($state.setup -ne 1 -or $state.draft.step -ne 2 -or $state.existingProfile) { throw 'Closing wizard lost resumable fresh-profile state.' }
        if ($state.draft.output -ne $output -or $state.draft.temporary -ne $temporary) { throw 'Folder draft was not saved.' }
        if (Window-Now 'WhatsNewWindow') { throw 'Closing an incomplete wizard incorrectly opened WhatsNew.' }
        Record 'interrupted' @{ zeroBasedStep = 2; displayedStep = 3; method = 'native WindowPattern.Close'; skipped = $false }
        Close-Window $main
    } elseif ($Phase -eq 'Complete') {
        $wizard = Wait-Window 'SetupWizardWindow' 30
        $state = Step $wizard 2 'SetupOutput'
        foreach ($pair in @(@('SetupOutput', $output), @('SetupTemporary', $temporary))) {
            if ((Find $wizard $pair[0]).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $pair[1]) { throw 'Restart did not restore the visible folder draft.' }
        }
        if ($state.draft.interface -ne 1) { throw 'Restart lost selected Modern interface.' }
        Capture $wizard '04-resumed-folders'
        Invoke $wizard 'SetupNext'; [void](Step $wizard 3 'SetupQuality'); [void](Find $wizard 'SetupContainer')
        Capture $wizard '05-format-preferences'
        Invoke $wizard 'SetupNext'; $state = Step $wizard 4 'SetupAutomaticUpdates'
        $automatic = (Find $wizard 'SetupAutomaticUpdates').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $startup = (Find $wizard 'SetupStartupUpdate').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($automatic.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On -or $startup.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off -or $state.draft.updateMode -ne 2) { throw 'New-profile update defaults disagree with the documented policy.' }
        Capture $wizard '06-default-update-policy'
        # Exercise a genuine choice. NotifyOnly prevents this UI test from
        # replacing its release installation while preserving automatic checks.
        $mode = Find $wizard 'SetupUpdateMode'
        $items = $mode.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        if ($items.Count -ne 3) { throw 'Expected three update policy choices.' }
        Wait-Condition { $mode.Current.IsEnabled -and (Find-Now $wizard 'SetupNext').Current.IsEnabled } 'Update policy input remains disabled.'
        $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Wait-Condition { $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } 'NotifyOnly choice was not visibly selected.'
        Invoke $wizard 'SetupNext'; $state = Step $wizard 5 'SetupNext'
        if ($state.draft.updateMode -ne 0) { throw 'NotifyOnly choice was not saved.' }
        Capture $wizard '07-summary'
        Invoke $wizard 'SetupNext'
        $news = Wait-Window 'WhatsNewWindow' 30
        [void](Assert-PersistedSettings)
        Capture $news '08-whats-new'
        Invoke $news 'WhatsNewDone'
        Wait-Condition { $null -eq (Window-Now 'WhatsNewWindow') } 'WhatsNew did not close after acknowledgement.'
        $saved = Read-Experience
        if (-not $saved.lastWhatsNewAcknowledgedVersion -or $saved.lastWhatsNewAcknowledgedVersion -ne $saved.lastObservedVersion) { throw 'WhatsNew acknowledgement was not durably saved.' }
        [void](Find $main 'NavBatch')
        Invoke $main 'NavDownloads'
        Capture $main '09-modern-after-setup'
        Close-Window $main
    } else {
        $state = Assert-PersistedSettings
        if (-not $state.lastWhatsNewAcknowledgedVersion -or $state.lastWhatsNewAcknowledgedVersion -ne $state.lastObservedVersion) { throw 'Acknowledgement was lost on restart.' }
        [void](Find $main 'NavBatch')
        # Native readiness, not a delay standing in for initialization: wait for
        # the actual dashboard Ready label in the fresh-profile EN/PL language.
        $settings = Get-Content -LiteralPath (Join-Path $DataRoot 'settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $readyText = if ($settings.language -eq 'pl') { 'Gotowe' } else { 'Ready' }
        $readyCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $readyText)
        Wait-Condition { $main.Current.IsEnabled -and $null -ne $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $readyCondition) } 'Restart did not reach the actual Ready dashboard.' 30
        $observation = [Diagnostics.Stopwatch]::StartNew()
        $checks = 0
        do {
            foreach ($id in @('SetupWizardWindow', 'UpgradeChoiceWindow', 'WhatsNewWindow')) {
                if (Window-Now $id) { throw "Acknowledged experience repeated on restart: $id" }
            }
            Invoke $main $(if ($checks % 2 -eq 0) { 'NavHistory' } else { 'NavDownloads' })
            $checks++
            Start-Sleep -Milliseconds 200
        } while ($observation.Elapsed.TotalSeconds -lt 5)
        Invoke $main 'NavDownloads'
        Capture $main '10-restart-without-repeated-experience'
        Record 'restart.no-repetition' @{ observedSeconds = $observation.Elapsed.TotalSeconds; navigationRoundTrips = $checks; acknowledgedVersion = $state.lastWhatsNewAcknowledgedVersion }
        Close-Window $main
    }
    if (-not $owned.WaitForExit(30000)) { throw 'Native Close did not finish graceful application shutdown.' }
    if ($owned.ExitCode -ne 0) { throw "Application shutdown exit code: $($owned.ExitCode)" }
    Record 'application.exited' @{ code = $owned.ExitCode }
} catch {
    $errorText = $_.Exception.ToString()
    Record 'failure' $errorText
    foreach ($handle in [PublishedExperienceWindow]::OwnedWindows($OwnedProcessId)) {
        try {
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            Capture $element ('failure-owned-' + $handle.ToInt64())
        } catch { Record 'failure.capture-error' $_.Exception.Message }
    }
} finally {
    @{ phase = $Phase; result = $(if ($errorText) { 'FAIL' } else { 'PASS' }); processId = $OwnedProcessId; semanticBridge = $false; elapsedSeconds = $watch.Elapsed.TotalSeconds; error = $errorText; events = @($events.ToArray()) } |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'native-result.json') -Encoding UTF8
}
if ($errorText) { Write-Error $errorText; exit 1 }
exit 0
