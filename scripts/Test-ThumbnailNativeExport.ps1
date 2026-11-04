[CmdletBinding()]
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$DataRoot)
# External Windows UIA client. Writes only inside this run's owned QA profile.
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($DataRoot)
if ([IO.Path]::GetFileName($rootPath) -notmatch '^mtd-automation-smoke-[a-f0-9]{32}$' -or
    -not $rootPath.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) { throw 'Not an owned QA profile.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class ThumbnailDialogCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint f);
    delegate bool ChildCallback(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,ChildCallback c,IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(ChildCallback c,IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int n);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,string l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    public static IntPtr FindOwnedDialog(int pid) {
        IntPtr result=IntPtr.Zero;
        EnumWindows(delegate(IntPtr h,IntPtr p) {
            uint actual; GetWindowThreadProcessId(h,out actual);
            if(actual!=(uint)pid) return true;
            var b=new StringBuilder(128); GetClassName(h,b,128);
            if(b.ToString()=="#32770" && IsWindowVisible(h)) { result=h; return false; }
            return true;
        },IntPtr.Zero); return result;
    }
    public static IntPtr FindChild(IntPtr root,string cls,int id) {
        IntPtr result=IntPtr.Zero;
        EnumChildWindows(root,delegate(IntPtr h,IntPtr p) {
            var b=new StringBuilder(128); GetClassName(h,b,128);
            if(b.ToString()==cls && (id<0 || GetDlgCtrlID(h)==id)) { result=h; return false; }
            return true;
        },IntPtr.Zero);
        return result;
    }
    public static string[] DescribeChildren(IntPtr root) {
        var result=new List<string>();
        EnumChildWindows(root,delegate(IntPtr h,IntPtr p) {
            var b=new StringBuilder(128); GetClassName(h,b,128);
            result.Add(b.ToString()+":"+GetDlgCtrlID(h)); return true;
        },IntPtr.Zero); return result.ToArray();
    }
}
'@
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
function Find-Owned([string]$property, [string]$value) {
    $automationProperty = if ($property -eq 'AutomationIdProperty') { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::ClassNameProperty }
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
        [System.Windows.Automation.PropertyCondition]::new($automationProperty, $value))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $found = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    throw "Owned native UIA element missing: $property=$value"
}
function Find-Id($parent, [string]$id) {
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
$tool = Find-Owned 'AutomationIdProperty' 'ThumbnailToolWindow'
$destination = Join-Path $rootPath 'thumbnail-native-exports'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($format in @('Png','Jpeg')) {
    $extension = if ($format -eq 'Png') { 'png' } else { 'jpg' }
    $target = Join-Path $destination ('native-preview.' + $extension)
    if (Test-Path -LiteralPath $target) { throw 'Export target already exists; do not overwrite QA evidence.' }
    $action = Find-Id $tool ('ThumbnailSave' + $format)
    Write-Output "Invoking native thumbnail $format action."
    $action.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $timer = [Diagnostics.Stopwatch]::StartNew(); $dialogHandle = [IntPtr]::Zero
    do {
        $dialogHandle = [ThumbnailDialogCapture]::FindOwnedDialog($ProcessId)
        if ($dialogHandle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    } while ($dialogHandle -eq [IntPtr]::Zero -and $timer.Elapsed.TotalSeconds -lt 15)
    if ($dialogHandle -eq [IntPtr]::Zero) { throw 'No live native save dialog HWND for this process.' }
    Write-Output "Owned system save dialog HWND: $dialogHandle."
    $rectangle = New-Object ThumbnailDialogCapture+Rect
    if ([ThumbnailDialogCapture]::GetWindowRect($dialogHandle, [ref]$rectangle)) {
        $bitmap = [Drawing.Bitmap]::new($rectangle.Right-$rectangle.Left, $rectangle.Bottom-$rectangle.Top)
        $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
        try { [void][ThumbnailDialogCapture]::PrintWindow($dialogHandle,$dc,2) }
        finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
        try { $bitmap.Save((Join-Path $rootPath ('thumbnail-save-dialog-' + $extension + '.png'))) }
        finally { $bitmap.Dispose() }
    }
    # Native window creation precedes its child controls. This test deliberately
    # qualifies Windows HWND controls separately from the tool's UIA provider.
    $timer.Restart()
    do {
        $nativeName = [ThumbnailDialogCapture]::FindChild($dialogHandle,'Edit',1001)
        if ($nativeName -eq [IntPtr]::Zero) { $nativeName = [ThumbnailDialogCapture]::FindChild($dialogHandle,'Edit',1148) }
        $nativeSave = [ThumbnailDialogCapture]::FindChild($dialogHandle,'Button',1)
        if ($nativeName -eq [IntPtr]::Zero -or $nativeSave -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    } while (($nativeName -eq [IntPtr]::Zero -or $nativeSave -eq [IntPtr]::Zero) -and $timer.Elapsed.TotalSeconds -lt 15)
        if ($nativeName -eq [IntPtr]::Zero -or $nativeSave -eq [IntPtr]::Zero) {
            Write-Output ('Owned dialog child classes/IDs: ' + ([ThumbnailDialogCapture]::DescribeChildren($dialogHandle) -join ', '))
            throw "Owned save dialog native controls missing: filename=$nativeName; save=$nativeSave."
        }
        if ([ThumbnailDialogCapture]::SendMessage($nativeName,12,[IntPtr]::Zero,$target) -eq [IntPtr]::Zero) { throw 'Native filename edit rejected its value.' }
        if (-not [ThumbnailDialogCapture]::PostMessage($dialogHandle,273,[IntPtr]1,$nativeSave)) { throw 'Native Save command failed.' }
        Write-Output 'System save dialog filename and Save exercised through owned Win32 child HWNDs; not a common-dialog UIA qualification.'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if (Test-Path -LiteralPath $target) { break }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    if (-not (Test-Path -LiteralPath $target)) { throw 'Native export did not produce its image.' }
    $picture = [Drawing.Image]::FromFile($target)
    try {
        $expected = if ($format -eq 'Png') { [Drawing.Imaging.ImageFormat]::Png.Guid } else { [Drawing.Imaging.ImageFormat]::Jpeg.Guid }
        if ($picture.RawFormat.Guid -ne $expected -or $picture.Width -ne 960 -or $picture.Height -ne 540) { throw 'Native export codec or dimensions differ from the source fixture.' }
    } finally { $picture.Dispose() }
    # Wait for the real export record, not just an enabled button or filename.
    $timer.Restart(); $record = $null
    do {
        $history = Join-Path $rootPath 'thumbnail-exports.json'
        if (Test-Path -LiteralPath $history) {
            $stream = [IO.File]::Open($history, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = [IO.StreamReader]::new($stream)
            try { $records = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            $record = @($records | Where-Object OutputPath -eq $target)
        }
        if ($record.Count -gt 0 -and $action.Current.IsEnabled) { break }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    if ($record.Count -ne 1 -or -not $action.Current.IsEnabled) { throw 'Export did not commit exactly one history entry and return to idle.' }
    Write-Output "Native $format export PASS: tool UIA action, Windows save dialog, decoded 960x540 output, persisted history."
}
