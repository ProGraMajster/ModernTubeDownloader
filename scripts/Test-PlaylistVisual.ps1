[CmdletBinding()]
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$DataRoot,
    [int]$Count = 12, [int]$TextScale = 100, [string]$Interface = 'ModernDashboard',
    [switch]$ClassicRecovery, [switch]$Transfer, [string]$SourceCommit, [switch]$SourceDirty)
# External Windows UIA client: runs against Debug normal startup and the production
# Release binary. No automation bridge, desktop capture, cursor movement or input injection.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PlaylistWindow {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
    public static Rect Bounds(IntPtr h) { var old=SetThreadDpiAwarenessContext(new IntPtr(-4)); try { Rect r; GetWindowRect(h,out r); return r; } finally { SetThreadDpiAwarenessContext(old); } }
    public static void Place(IntPtr h, Rect r) { var old=SetThreadDpiAwarenessContext(new IntPtr(-4)); try { if(!SetWindowPos(h,IntPtr.Zero,r.L,r.T,r.R-r.L,r.B-r.T,0x14)) throw new Exception("Place failed"); } finally { SetThreadDpiAwarenessContext(old); } }
    delegate bool EnumCallback(IntPtr h, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref Point p);
    public static bool Click(IntPtr h,int pid,int x,int y) {
        uint owner; GetWindowThreadProcessId(h,out owner); if(owner!=pid)return false;
        var old=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { var p=new Point { X=x,Y=y }; if(!ScreenToClient(h,ref p))return false;
            var l=(IntPtr)((p.Y<<16)|(p.X&0xffff)); return PostMessage(h,0x201,(IntPtr)1,l)&&PostMessage(h,0x202,IntPtr.Zero,l);
        } finally { SetThreadDpiAwarenessContext(old); }
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out IntPtr result);
    public static bool Key(IntPtr h,int pid,int key) { uint owner; IntPtr result; GetWindowThreadProcessId(h,out owner); return owner==pid && SendMessageTimeout(h,0x100,(IntPtr)key,IntPtr.Zero,2,2000,out result)!=IntPtr.Zero && SendMessageTimeout(h,0x101,(IntPtr)key,IntPtr.Zero,2,2000,out result)!=IntPtr.Zero; }
    public static IntPtr Popup(int pid,IntPtr main) {
        IntPtr found=IntPtr.Zero;
        EnumWindows((h,d)=>{ uint owner; GetWindowThreadProcessId(h,out owner); if(owner==pid && h!=main && IsWindowVisible(h)) { found=h; return false; } return true; },IntPtr.Zero);
        return found;
    }
    public static bool Capture(IntPtr h, int pid, IntPtr dc) { uint owner; GetWindowThreadProcessId(h,out owner); return owner==pid && PrintWindow(h,dc,2); }
}
'@
$script:owned = Get-Process -Id $ProcessId
$appPath = $owned.Path
$script:hwnd = $owned.MainWindowHandle
$originalBounds = [PlaylistWindow]::Bounds($hwnd)
$script:root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$script:playlistCaptures = [System.Collections.Generic.List[object]]::new()
function Find([string]$Id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    throw "Missing playlist UIA element: $Id"
}
function Reveal($Element) {
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern,[ref]$pattern)) { $pattern.ScrollIntoView() }
}
function Invoke([string]$Id) { Write-Output "Invoke $Id"; $e=Find $Id; Reveal $e; $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Toggle([int]$Index) { $e=Find "PlaylistEntryToggle$Index"; Reveal $e; $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
function SelectRow($Row) {
    Reveal $Row
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { $Row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
}
function SetText([string]$Id,[string]$Text) { (Find $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text) }
function Choose([string]$Id,[int]$Index) {
    $e=Find $Id
    $items=$e.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
    $items[$Index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function WaitCheck([scriptblock]$Check,[string]$Message) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do { if (& $Check) { return }; Start-Sleep -Milliseconds 100 } while($clock.Elapsed.TotalSeconds -lt 30)
    throw $Message
}
function FindOwnedPopupItem([string]$Caption) {
    $captionCondition=[System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Caption),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::MenuItem))
    $processCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$owned.Id)
    foreach($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$processCondition)) {
        # UIA can expose the owned popup as a child of the main window.
        $item=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$captionCondition)
        if($item -and $item.Current.IsEnabled -and -not $item.Current.IsOffscreen -and $item.Current.ProcessId -eq $owned.Id) { return $item }
    }
    return $null
}
function SaveOwnedWindowDiagnostics {
    $processCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$owned.Id)
    $windows=@([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$processCondition) | ForEach-Object {
        $window=$_; $current=$window.Current
        @{name=$current.Name;handle=$current.NativeWindowHandle;type=$current.ControlType.ProgrammaticName;nodes=@($window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
            $c=$_.Current; @{name=$c.Name;id=$c.AutomationId;type=$c.ControlType.ProgrammaticName;handle=$c.NativeWindowHandle;pid=$c.ProcessId}
        })}
    })
    ConvertTo-Json -InputObject $windows -Depth 6 | Set-Content (Join-Path $DataRoot 'playlist-owned-window-diagnostics.json') -Encoding UTF8
    $popup=[PlaylistWindow]::Popup($owned.Id,$hwnd)
    if($popup -ne [IntPtr]::Zero) { Capture 'playlist-unexpected-popup' $popup }
}
function ReadQueue { if(Test-Path (Join-Path $DataRoot 'queue.json')) { foreach($job in (Get-Content (Join-Path $DataRoot 'queue.json') -Raw | ConvertFrom-Json)) { Write-Output $job } } }
function CloseOwned {
    if($owned.HasExited) { return }
    [void]$owned.CloseMainWindow()
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $condition=[System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$owned.Id),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ShutdownExit'))
    do {
        if($owned.HasExited) { return }
        $exitButton=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        if($exitButton) { $exitButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
        Start-Sleep -Milliseconds 100
    } while($timer.Elapsed.TotalSeconds -lt 10)
    if(-not $owned.WaitForExit(30000)) { throw 'Owned playlist process did not close gracefully.' }
}
function RestartOwned {
    CloseOwned
    $restartArgs=@(); if($ClassicRecovery) { $restartArgs=@('--classic-ui') }
    $script:owned=if($restartArgs.Count) { Start-Process -FilePath $appPath -ArgumentList $restartArgs -PassThru -WindowStyle Hidden } else { Start-Process -FilePath $appPath -PassThru -WindowStyle Hidden }
    if(-not $owned.WaitForInputIdle(30000)) { throw 'Restart did not reach UI idle.' }
    WaitCheck { $owned.Refresh(); $owned.MainWindowHandle -ne [IntPtr]::Zero } 'Restart HWND absent.'
    $script:hwnd=$owned.MainWindowHandle; [PlaylistWindow]::Place($hwnd,$originalBounds)
    $script:root=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
}
function Capture([string]$Name, [IntPtr]$Window = $hwnd) {
    Start-Sleep -Milliseconds 300
    $b=[PlaylistWindow]::Bounds($Window)
    $bitmap=[Drawing.Bitmap]::new($b.R-$b.L,$b.B-$b.T); $g=[Drawing.Graphics]::FromImage($bitmap); $dc=$g.GetHdc()
    try { if(-not [PlaylistWindow]::Capture($Window,$owned.Id,$dc)) { throw 'Owned HWND PrintWindow failed' } }
    finally { $g.ReleaseHdc($dc); $g.Dispose() }
    $path=Join-Path $DataRoot ($Name+'.png')
    try { $bitmap.Save($path) } finally { $bitmap.Dispose() }
    $nodes=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        $c=$_.Current; $r=$c.BoundingRectangle
        @{id=$c.AutomationId;name=$c.Name;enabled=$c.IsEnabled;offscreen=$c.IsOffscreen;bounds=@($r.X,$r.Y,$r.Width,$r.Height)}
    })
    $nodes | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $DataRoot ($Name+'-uia.json')) -Encoding UTF8
    ConvertTo-Json -InputObject @(ReadQueue) -Depth 30 | Set-Content (Join-Path $DataRoot ($Name+'-queue.json')) -Encoding UTF8
    $script:playlistCaptures.Add(@{file=$Name+'.png';width=$b.R-$b.L;height=$b.B-$b.T;dpi=[PlaylistWindow]::GetDpiForWindow($hwnd);textScale=$TextScale;
        sha256=(Get-FileHash $path -Algorithm SHA256).Hash;sourceCommit=$SourceCommit;sourceDirty=[bool]$SourceDirty;kind=$(if($Window -eq $hwnd){'full-window'}else{'owned-popup'});pid=$owned.Id;binarySha256=(Get-FileHash $appPath -Algorithm SHA256).Hash;
        uia=$Name+'-uia.json';queue=$Name+'-queue.json';utc=[DateTime]::UtcNow.ToString('O')})
    $script:playlistCaptures | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $DataRoot 'playlist-native-manifest.json') -Encoding UTF8
    Write-Output "Captured $Name, $($b.R-$b.L)x$($b.B-$b.T), DPI=$([PlaylistWindow]::GetDpiForWindow($hwnd)), TextScale=$TextScale"
}
try {
    Invoke 'NavDownloads'
    SetText 'DownloadUrlInput' 'https://www.youtube.com/playlist?list=synthetic-playlist'
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { Invoke 'DashboardChipAnalyze' } else { Invoke 'AnalyzeButton' }
    [void](Find 'PlaylistEntryToggle1')
    WaitCheck { (Find 'PlaylistAddSelectedButton').Current.IsEnabled } 'Playlist did not become ready.'
    if((Find 'PlaylistEntryToggle3').Current.IsEnabled) { throw 'Private entry is selectable.' }
    if((Find 'PlaylistEntryToggle9').Current.IsEnabled) { throw 'Deleted entry is selectable.' }
    Reveal (Find 'PlaylistTitle'); Capture 'playlist-preview-12'
    Reveal (Find 'PlaylistEntryToggle12'); Capture 'playlist-preview-last'
    Reveal (Find 'PlaylistEntryToggle1')
    Invoke 'PlaylistClearSelectionButton'
    if((Find 'PlaylistAddSelectedButton').Current.IsEnabled) { throw 'Empty selection enabled handoff.' }
    Invoke 'PlaylistSelectAllButton'
    if((Find 'PlaylistSelectedCount').Current.Name -notmatch "\b$($Count-2)\b") { throw 'Select all included unavailable entries.' }
    Invoke 'PlaylistClearSelectionButton'
    foreach($i in @(1,2,4)) { Toggle $i }
    if((Find 'PlaylistSelectedCount').Current.Name -notmatch '\b3\b') { throw 'Partial selection count differs.' }
    Choose 'PlaylistQualitySelector' 3
    Choose 'PlaylistContainerSelector' 2
    (Find 'PlaylistEntriesList').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,0)
    Reveal (Find 'PlaylistTitle'); Capture 'playlist-partial-selection'
    if($Count -gt 40) {
        Invoke 'PlaylistNextPageButton'; [void](Find 'PlaylistEntryToggle41'); Toggle 41
        $rows=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {$_.Current.AutomationId -like 'PlaylistEntryToggle*'})
        if($rows.Count -ne [Math]::Min(40,$Count-40)) { throw 'Playlist allocated beyond the page limit.' }
        Reveal (Find 'PlaylistEntryToggle70'); Capture 'playlist-long-paged'
        Invoke 'PlaylistPreviousPageButton'
        if((Find 'PlaylistEntryToggle1').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne 'On') { throw 'Page switch lost selection.' }
        Invoke 'PlaylistNextPageButton'
        if((Find 'PlaylistEntryToggle41').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne 'On') { throw 'Second-page selection was lost.' }
        Toggle 41; Invoke 'PlaylistPreviousPageButton'
    }
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery -and $TextScale -eq 100 -and $Count -eq 12) {
        foreach($choice in @('InterfaceClassic','InterfaceModernDashboard')) {
            Invoke 'NavSettings'; Invoke 'SettingsCategoryPersonalization'; Invoke $choice; Invoke 'NavDownloads'
            if((Find 'PlaylistSelectedCount').Current.Name -notmatch '\b3\b') { throw 'Shell switch lost playlist selection.' }
        }
    }
    Invoke 'PlaylistAddSelectedButton'
    WaitCheck { @(ReadQueue).Count -eq 3 } 'Handoff did not persist exactly three jobs.'
    $before=@(ReadQueue)
    if(($before.videoId -join ',') -ne 'p001,p002,p004') { throw 'Handoff changed entry IDs/order.' }
    foreach($job in $before) {
        if($job.qualityPresetId -ne '1080' -or $job.preferredVideoContainer -ne 2 -or $job.playlistId -ne 'synthetic-playlist') { throw 'Handoff lost shared options/provenance.' }
    }
    if(-not $before[0].thumbnailUrl -or $before[2].thumbnailUrl) { throw 'Handoff lost thumbnail/missing-metadata semantics.' }
    Invoke 'PlaylistAddSelectedButton'
    WaitCheck { (Find 'PlaylistAddResult').Current.Name -match '\b3\b' } 'Duplicate handoff result missing.'
    if(@(ReadQueue).Count -ne 3) { throw 'Duplicate handoff added jobs.' }
    Reveal (Find 'PlaylistAddResult'); Capture 'playlist-unavailable-duplicates'
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { Invoke 'PlaylistClosePreview' }
    $firstId=([guid]$before[0].id).ToString('N')
    $row=Find "QueueItem-$firstId"; Reveal $row
    SelectRow $row
    Capture 'playlist-added-to-queue'
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) {
        $detailsCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'InspectorDetails')
        if(-not $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$detailsCondition)) { Invoke 'DashboardToggleInspector' }
        Reveal (Find 'InspectorDetails'); Capture 'playlist-queue-selected-inspector'
        Invoke 'InspectorOptions'; Capture 'playlist-queue-options'
        Invoke 'InspectorDetails'
        if($Transfer) {
            $row=Find "QueueItem-$firstId"; Reveal $row; $row.SetFocus()
            foreach($key in @(32,13)) {
                if(-not [PlaylistWindow]::Key($hwnd,$owned.Id,$key)) { throw 'Owned row key dispatch failed.' }
                WaitCheck { (Find "QueueItem-$firstId").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } 'Keyboard did not select the playlist row.'
            }
            Capture 'playlist-keyboard-selection'
            $selectCaption=if((Find 'InspectorDetails').Current.Name -eq 'Szczegóły'){'Wybierz'}else{'Select'}
            Invoke "QueueMoreAction-$firstId"
            # A pre-existing owned tooltip is not evidence that the asynchronous
            # menu invocation has completed. Wait for the actual menu command.
            try { WaitCheck { $null -ne (FindOwnedPopupItem $selectCaption) } 'Queue Select menu item absent.' }
            catch { SaveOwnedWindowDiagnostics; throw }
            $menuItem=FindOwnedPopupItem $selectCaption
            if(-not $menuItem -or -not $menuItem.Current.IsEnabled -or $menuItem.Current.ProcessId -ne $owned.Id) { throw 'Owned Select menu item unavailable.' }
            $r=$menuItem.Current.BoundingRectangle
            $nativeOwner=$menuItem
            while($nativeOwner -and $nativeOwner.Current.NativeWindowHandle -eq 0) { $nativeOwner=[System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($nativeOwner) }
            if(-not $nativeOwner -or $nativeOwner.Current.ProcessId -ne $owned.Id -or $nativeOwner.Current.NativeWindowHandle -eq $hwnd.ToInt64()) { throw 'Menu has no distinct owned native ancestor.' }
            Capture 'playlist-context-menu' ([IntPtr]$nativeOwner.Current.NativeWindowHandle)
            if(-not [PlaylistWindow]::Click([IntPtr]$nativeOwner.Current.NativeWindowHandle,$owned.Id,[int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))) { throw 'Owned menu click failed.' }
            WaitCheck { $null -eq (FindOwnedPopupItem $selectCaption) } 'Context menu did not close after Select.'
            foreach($id in @('InspectorTitle','InspectorPlaylist')) {
                $label=Find $id; Reveal $label; $label.SetFocus()
                if(-not [PlaylistWindow]::Key($hwnd,$owned.Id,112)) { throw 'Owned F1 dispatch failed.' }
                WaitCheck { [PlaylistWindow]::Popup($owned.Id,$hwnd) -ne [IntPtr]::Zero } 'Full value popup absent.'
                Capture ('playlist-full-value-'+$id) ([PlaylistWindow]::Popup($owned.Id,$hwnd))
                [void][PlaylistWindow]::Key($hwnd,$owned.Id,27)
                # F1 popups have their existing five-second lifetime; no global hover simulation.
                Start-Sleep -Milliseconds 5100
            }
            $clipboardBefore=[Windows.Forms.Clipboard]::GetDataObject()
            try {
                Invoke 'InspectorCopyUrl'
                WaitCheck { [Windows.Forms.Clipboard]::GetText() -eq $before[0].sourceUrl } 'Copy URL differed from the selected canonical source.'
            } finally {
                if($null -ne $clipboardBefore){[Windows.Forms.Clipboard]::SetDataObject($clipboardBefore,$true)}else{[Windows.Forms.Clipboard]::Clear()}
            }
            Write-Output 'Playlist input PASS: owned HWND Space/Enter, real context menu Select, F1 title/provenance popup, canonical URL copy with clipboard restoration.'
        }
    }
    RestartOwned
    Invoke 'NavDownloads'
    $row=Find "QueueItem-$firstId"; Reveal $row; SelectRow $row
    if((@(ReadQueue).id -join ',') -ne ($before.id -join ',')) { throw 'Restart changed queue IDs/order.' }
    Capture 'playlist-after-restart'
    if($Transfer) {
        Invoke 'DashboardStartAll'
        WaitCheck { @(Get-ChildItem $DataRoot -Recurse -Filter '.review-transfer-ready').Count -gt 0 } 'Actual byte transfer did not reach its gate.'
        SelectRow (Find "QueueItem-$firstId"); Capture 'playlist-downloading'
        Invoke "QueuePrimaryAction-$firstId"
        WaitCheck { (@(ReadQueue) | Where-Object id -eq $before[0].id).status -eq 8 } 'Cancel did not persist.'
        Capture 'playlist-cancelled'
        Invoke "QueuePrimaryAction-$firstId"
        WaitCheck { (@(ReadQueue) | Where-Object id -eq $before[0].id).status -in @(2,3) } 'Retry did not restart the first job.'
        RestartOwned
        Invoke 'NavDownloads'
        WaitCheck { (@(ReadQueue) | Where-Object id -eq $before[0].id).status -eq 13 } 'Restart did not recover Interrupted.'
        SelectRow (Find "QueueItem-$firstId"); Capture 'playlist-interrupted'
        WaitCheck { (@(ReadQueue) | Where-Object id -eq $before[1].id).status -eq 7 } 'Synthetic executor failure did not persist.'
        $secondId=([guid]$before[1].id).ToString('N')
        SelectRow (Find "QueueItem-$secondId"); Capture 'playlist-failed'
        Get-ChildItem $DataRoot -Recurse -Filter '.playlist-failure-ready' | ForEach-Object { Set-Content (Join-Path $_.DirectoryName '.playlist-retry-allow') 'allowed' }
        Invoke "QueuePrimaryAction-$secondId"
        Invoke "QueuePrimaryAction-$firstId"
        WaitCheck {
            Get-ChildItem $DataRoot -Recurse -Filter '.review-transfer-ready' | ForEach-Object { Set-Content (Join-Path $_.DirectoryName '.review-transfer-release') 'allowed' }
            @(@(ReadQueue) | Where-Object status -eq 6).Count -eq 3
        } 'Retry did not complete all three canonical jobs.'
        $completed=@(ReadQueue)
        if(($completed.videoId -join ',') -ne 'p001,p002,p004') { throw 'Per-entry metadata changed stable media IDs.' }
        if(($completed.id -join ',') -ne ($before.id -join ',')) { throw 'Retry changed IDs/order.' }
        foreach($job in $completed) { if(-not (Test-Path -LiteralPath $job.finalFile)) { throw 'Completed job has no output file.' } }
        WaitCheck {
            $widgetNames=@((Find 'DashboardWidgets').FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name })
            ($widgetNames -contains '3') -and (@($widgetNames | Where-Object { $_ -match '^(Aktywne procesy|Active processes) \(0\)$' }).Count -gt 0)
        } 'Widgets did not converge to three completed jobs and no owned processes.'
        SelectRow (Find "QueueItem-$firstId"); Capture 'playlist-completed-after-retry'
    }
    SetText 'DownloadUrlInput' 'https://www.youtube.com/watch?v=p006&list=synthetic-playlist'
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { Invoke 'DashboardChipVideo' } else { Invoke 'AnalyzeButton' }
    WaitCheck { (Find 'AddToQueueButton').Current.IsEnabled } 'Watch+list did not use single video analysis.'
    Reveal (Find 'AddToQueueButton'); Capture 'single-video-watch-with-list'
    if(@(ReadQueue).Count -ne 3) { throw 'Watch+list unexpectedly expanded queue.' }
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery -and $TextScale -eq 100 -and $Count -eq 12) {
        SetText 'DownloadUrlInput' 'https://www.youtube.com/playlist?list=empty'
        Invoke 'DashboardChipAnalyze'; [void](Find 'PlaylistPreview')
        WaitCheck { (Find 'PlaylistSelectedCount').Current.Name -match '\b0\b' } 'Empty playlist did not reset selection.'
        if((Find 'PlaylistAddSelectedButton').Current.IsEnabled) { throw 'Empty playlist can be added.' }
        Reveal (Find 'PlaylistTitle'); Capture 'playlist-empty'
        SetText 'DownloadUrlInput' 'https://www.youtube.com/playlist?list=error'
        Invoke 'DashboardChipAnalyze'
        WaitCheck { (Find 'AnalyzeButton').Current.IsEnabled } 'Failed analysis did not restore analysis action.'
        Reveal (Find 'PreviewCard'); Capture 'playlist-analysis-error'
        if(@(ReadQueue).Count -ne 3) { throw 'Failed analysis changed queue.' }
        SetText 'DownloadUrlInput' 'https://www.youtube.com/playlist?list=slow'
        Invoke 'DashboardChipAnalyze'
        Start-Sleep -Milliseconds 300
        SetText 'DownloadUrlInput' 'https://www.youtube.com/playlist?list=synthetic-playlist'
        Invoke 'DashboardChipAnalyze'
        WaitCheck { (Find 'PlaylistSelectedCount').Current.Name -match '\b10\b' } 'Replacement analysis did not cancel the preceding request.'
        Reveal (Find 'PlaylistTitle'); Capture 'playlist-reanalyzed-after-cancel'
    }
    if($Transfer) {
        $closeCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'PlaylistClosePreview')
        if($root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$closeCondition)) { Invoke 'PlaylistClosePreview' }
        SetText 'DownloadUrlInput' 'https://www.youtube.com/playlist?list=synthetic-playlist'
        Invoke 'DashboardChipAnalyze'; [void](Find 'PlaylistEntryToggle5')
        Invoke 'PlaylistClearSelectionButton'; Toggle 5; Choose 'PlaylistQualitySelector' 7
        Invoke 'PlaylistAddSelectedButton'
        WaitCheck { @(ReadQueue).Count -eq 4 } 'Audio-only selection did not reach the canonical queue.'
        WaitCheck { (@(ReadQueue) | Where-Object videoId -eq 'p005').status -eq 6 } 'Audio-only selection did not complete.'
        $audioJob=@(ReadQueue) | Where-Object videoId -eq 'p005'
        if($audioJob.qualityPresetId -ne 'audio' -or -not $audioJob.selectedFormats.audioOnly -or $audioJob.selectedFormats.videoFormatId) { throw 'Audio-only selection resolved a video stream.' }
        Invoke 'PlaylistClosePreview'
        SelectRow (Find ('QueueItem-'+([guid]$audioJob.id).ToString('N')))
        Capture 'playlist-audio-only-completed'
    }
    Write-Output "Playlist Windows UIA PASS: $Count metadata entries; private+deleted excluded; 3 selected/added; repeat add skipped 3; 1080/Mkv; ordered IDs and thumbnails persisted through restart; watch+list single video. Profile: $DataRoot"
}
catch { Write-Output ($_.ToString() + "`n" + $_.ScriptStackTrace); throw }
finally {
    if($owned -and -not $owned.HasExited) {
        CloseOwned
    }
}


