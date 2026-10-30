[CmdletBinding()]
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$DataRoot,
    [int]$Count = 12, [int]$TextScale = 100, [string]$Interface = 'ModernDashboard',
    [switch]$ClassicRecovery, [switch]$Transfer, [switch]$Groups, [string]$SourceCommit, [switch]$SourceDirty)
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
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
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
function GroupToggles {
    @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.AutomationId -like 'PlaylistGroupToggle-*' })
}
function ExpandPlaylistGroups {
    if($Interface -ne 'ModernDashboard' -or $ClassicRecovery) { return }
    WaitCheck { @(GroupToggles).Count -gt 0 } 'Canonical playlist group header absent.'
    foreach($button in @(GroupToggles)) {
        if($button.Current.Name -match '^(Rozwiń|Expand):') { Reveal $button; $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    }
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
function ReadQueue {
    $path=Join-Path $DataRoot 'queue.json'
    if(Test-Path $path) {
        # Read one settled checkpoint, never poll the writer by holding its file.
        # Windows MoveFileEx can reject replacement even with FileShare.Delete.
        $deadline=[Diagnostics.Stopwatch]::StartNew()
        do {
            $stamp=(Get-Item -LiteralPath $path).LastWriteTimeUtc
            if(([DateTime]::UtcNow-$stamp).TotalMilliseconds -ge 300) { break }
            Start-Sleep -Milliseconds 25
            if($deadline.Elapsed.TotalSeconds -ge 3) { throw 'Queue checkpoint did not settle for inspection.' }
        } while($true)
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader=[IO.StreamReader]::new($stream)
        try { $json=$reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach($job in ($json | ConvertFrom-Json)) { Write-Output $job }
    }
}
function QueueRows {
    @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.AutomationId -like 'QueueItem-*' })
}
function QueueStatusIs([string]$Id,[string]$State) {
    $names=switch($State) {
        'active' { @('Pobieranie obrazu','Pobieranie dźwięku','Downloading video','Downloading audio') }
        'cancelled' { @('Anulowano','Cancelled') }
        'interrupted' { @('Przerwano','Interrupted') }
        'failed' { @('Niepowodzenie','Failed') }
        'completed' { @('Ukończono','Completed') }
    }
    $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,('QueueStatus-'+([guid]$Id).ToString('N')))
    $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
    if(-not $element) { return $false }
    $name=$element.Current.Name
    foreach($expected in $names) { if($name.StartsWith($expected,[StringComparison]::Ordinal)) { return $true } }
    return $false
}
function AssertPersistedStatus([string]$Id,[int[]]$Statuses) {
    if((@(ReadQueue) | Where-Object id -eq $Id).status -notin $Statuses) { throw "Canonical checkpoint disagrees with UIA status for $Id." }
}
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
function ClickOwnedMenu([string]$Caption) {
    WaitCheck { $null -ne (FindOwnedPopupItem $Caption) } ('Missing owned menu: '+$Caption)
    $item=FindOwnedPopupItem $Caption
    $native=$item
    while($native -and $native.Current.NativeWindowHandle -eq 0) { $native=[System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($native) }
    if(-not $native -or $native.Current.ProcessId -ne $owned.Id) { throw 'Menu ancestor not owned.' }
    $menuHandle=[IntPtr]$native.Current.NativeWindowHandle
    $r=$item.Current.BoundingRectangle
    if(-not [PlaylistWindow]::Click($menuHandle,$owned.Id,[int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))) { throw 'Owned menu click failed.' }
    # A native confirmation may already own the UI thread's modal loop. Do not
    # synchronously scan the underlying MFN tree while waiting for its menu to close.
    WaitCheck { -not [PlaylistWindow]::IsWindowVisible($menuHandle) } 'Group menu did not close.'
}
function RunGroupFixtures($Template) {
    if($Interface -ne 'ModernDashboard' -or $ClassicRecovery) { throw 'Group fixture requires Modern.' }
    $safeRoot=[IO.Path]::GetFullPath($DataRoot)
    if(-not $safeRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $safeRoot -Leaf) -notlike 'mtd-automation-smoke-*') { throw 'Fixture root is not the isolated QA profile.' }
    function MakeJob([int]$Index,[string]$Playlist,[string]$HostName='www.youtube.com',[int]$State=0) {
        $job=$Template | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $job.id=[guid]::NewGuid().ToString(); $job.jobSessionId=[guid]::NewGuid().ToString()
        $job.videoId='group-fixture-'+$Index; $job.sourceUrl="https://$HostName/watch?v=group-fixture-$Index"
        $job.title="SYNTHETIC QA $Index — Zażółć gęślą jaźń · 日本語 🎬"
        $job.playlistId=$Playlist; $job.playlistTitle=if($Playlist){"SYNTHETIC QA — playlista $Playlist 日本語 🎬"}else{$null}
        $job.playlistIndex=if($Playlist){$Index}else{$null}; $job.status=$State
        $job.statusMessageKey='Queue.Detail.Queued'; $job.progressPercent=0
        foreach($property in @('startedAt','completedAt','selectedFormats','finalFile')) { $job | Add-Member -NotePropertyName $property -NotePropertyValue $null -Force }
        return $job
    }
    function Seed($Jobs) {
        CloseOwned
        $settingsPath=Join-Path $DataRoot 'settings.json'
        $settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        $settings.QueuePaused=$true; $settings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settingsPath -Encoding utf8
        ConvertTo-Json -InputObject @($Jobs) -Depth 30 | Set-Content -LiteralPath (Join-Path $DataRoot 'queue.json') -Encoding utf8
        RestartOwned; Invoke 'NavDownloads'
        if((@(ReadQueue).id -join ',') -ne ($Jobs.id -join ',')) { throw 'Fixture restart changed canonical job identity/order.' }
        # First close writes the externally seeded JSON with the canonical serializer
        # (including null omission). Establish byte hashes only after that handoff.
        RestartOwned; Invoke 'NavDownloads'
        if((@(ReadQueue).id -join ',') -ne ($Jobs.id -join ',')) { throw 'Canonical fixture serialization changed identity/order.' }
    }
    $mixed=@((MakeJob 1 'a'),(MakeJob 2 ''),(MakeJob 3 'b'),(MakeJob 4 'a' 'www.youtube.com' 6),(MakeJob 5 ''),(MakeJob 6 'b'),(MakeJob 7 'a' 'www.youtube.com' 7),(MakeJob 8 ''),(MakeJob 9 'a' 'www.youtube.com' 13))
    $completedFile=Join-Path $DataRoot 'group-completed-fixture.mp4'
    Set-Content -LiteralPath $completedFile -Value 'Synthetic file preservation fixture; not actual media.'
    $mixed[3].finalFile=$completedFile
    Seed $mixed
    WaitCheck { @(GroupToggles).Count -eq 2 -and @(QueueRows).Count -eq 3 } 'Two groups and three singles projection differs.'
    Capture 'downloads-two-playlists-and-singles'
    Capture 'downloads-playlist-collapsed'
    $queueHash=(Get-FileHash (Join-Path $DataRoot 'queue.json')).Hash
    $toggleId=@(GroupToggles)[0].Current.AutomationId
    $button=Find $toggleId; Reveal $button; $button.SetFocus()
    WaitCheck { (Find $toggleId).Current.HasKeyboardFocus } 'Group title did not receive keyboard focus.'
    [void][PlaylistWindow]::Key($hwnd,$owned.Id,9)
    WaitCheck { (Find ($toggleId.Replace('PlaylistGroupToggle-','PlaylistGroupMenu-'))).Current.HasKeyboardFocus } 'Tab did not reach the group actions button.'
    $button.SetFocus()
    [void][PlaylistWindow]::Key($hwnd,$owned.Id,32)
    WaitCheck { @(QueueRows).Count -eq 7 } 'Space did not expand four canonical children.'
    $childId='QueueItem-'+([guid]$mixed[3].id).ToString('N')
    SelectRow (Find $childId)
    Invoke $toggleId; WaitCheck { @(QueueRows).Count -eq 3 } 'Collapse retained group child controls.'
    $button=Find $toggleId; Reveal $button; $button.SetFocus(); [void][PlaylistWindow]::Key($hwnd,$owned.Id,13)
    WaitCheck { @(QueueRows).Count -eq 7 } 'Enter did not expand group.'
    if(-not (Find $childId).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Group keyboard toggle lost selected GUID.' }
    Capture 'downloads-group-keyboard-selected'
    foreach($choice in @('InterfaceClassic','InterfaceModernDashboard')) { Invoke 'NavSettings'; Invoke 'SettingsCategoryPersonalization'; Invoke $choice; Invoke 'NavDownloads' }
    WaitCheck { @(QueueRows).Count -eq 7 } 'Shell switch lost session group expansion.'
    if((Get-FileHash (Join-Path $DataRoot 'queue.json')).Hash -ne $queueHash) { throw 'Shell/group presentation mutated queue bytes.' }
    $polish=(Find 'NavDownloads').Current.Name -eq 'Pobieranie'
    $preference=if($polish){'Grupuj playlisty'}else{'Group playlists'}
    Invoke 'QueueMenuButton'; ClickOwnedMenu ('✓ '+$preference)
    WaitCheck { @(GroupToggles).Count -eq 0 -and @(QueueRows).Count -eq 9 } 'Grouping OFF did not restore individual records.'
    RestartOwned; Invoke 'NavDownloads'
    WaitCheck { @(GroupToggles).Count -eq 0 -and @(QueueRows).Count -eq 9 } 'Grouping OFF preference did not persist.'
    Invoke 'QueueMenuButton'; ClickOwnedMenu $preference
    WaitCheck { @(GroupToggles).Count -eq 2 -and @(QueueRows).Count -eq 3 } 'Grouping ON did not restore collapsed groups.'
    if((Get-FileHash (Join-Path $DataRoot 'queue.json')).Hash -ne $queueHash) { throw 'Grouping preference changed canonical queue bytes.' }
    ExpandPlaylistGroups
    Invoke ('QueuePrimaryAction-'+([guid]$mixed[0].id).ToString('N'))
    WaitCheck { @(QueueRows).Count -eq 8 } 'Removing one child did not update group/control count.'
    if(@(ReadQueue).Count -ne 8 -or (@(ReadQueue).id -contains $mixed[0].id)) { throw 'Child removal changed the wrong records.' }
    Capture 'downloads-group-after-child-removal'
    function DialogButton([string[]]$Names) {
        $popup=[PlaylistWindow]::Popup($owned.Id,$hwnd)
        if($popup -eq [IntPtr]::Zero) { return $null }
        $window=[System.Windows.Automation.AutomationElement]::FromHandle($popup)
        foreach($button in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) {
            $c=$button.Current
            if($c.IsEnabled -and -not $c.IsOffscreen -and $c.Name -in $Names -and
                $c.ControlType -eq [System.Windows.Automation.ControlType]::Button) { return $button }
        }
        return $null
    }
    function AcceptDialog([string[]]$Names) {
        WaitCheck { $null -ne (DialogButton $Names) } ('Owned dialog button absent: '+($Names -join '/'))
        $popup=[PlaylistWindow]::Popup($owned.Id,$hwnd)
        $button=DialogButton $Names
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        WaitCheck { -not [PlaylistWindow]::IsWindowVisible($popup) } 'Owned dialog did not dismiss.'
    }
    $groupMenuId=@(GroupToggles | Where-Object { $_.Current.Name -match 'playlista a' })[0].Current.AutomationId.Replace('PlaylistGroupToggle-','PlaylistGroupMenu-')
    Invoke $groupMenuId; ClickOwnedMenu $(if($polish){'Ponów błędne i przerwane'}else{'Retry failed and interrupted'})
    AcceptDialog @('OK')
    $retried=@(ReadQueue)
    foreach($id in @($mixed[6].id,$mixed[8].id)) { if(($retried | Where-Object id -eq $id).status -ne 0) { throw 'Group retry did not affect exactly its failed/interrupted jobs.' } }
    if(($retried | Where-Object id -eq $mixed[3].id).status -ne 6) { throw 'Group retry modified completed child.' }
    Invoke $groupMenuId; ClickOwnedMenu $(if($polish){'Usuń ukończone z listy'}else{'Remove completed from queue'})
    WaitCheck { $null -ne (DialogButton @('Anuluj','Cancel')) } 'MessageBoxForm cancellation button absent.'
    Capture 'playlist-group-remove-confirmation' ([PlaylistWindow]::Popup($owned.Id,$hwnd))
    AcceptDialog @('Anuluj','Cancel')
    if(@(ReadQueue).Count -ne 8) { throw 'Rejected group removal changed records.' }
    Invoke $groupMenuId; ClickOwnedMenu $(if($polish){'Usuń ukończone z listy'}else{'Remove completed from queue'})
    AcceptDialog @('Usuń ukończone z listy','Remove completed from queue')
    AcceptDialog @('OK')
    if(@(ReadQueue).Count -ne 7 -or -not (Test-Path -LiteralPath $completedFile)) { throw 'Group removal scope/file preservation failed.' }
    Capture 'downloads-group-after-bulk-actions'
    $perf=@()
    foreach($size in @(120,1300)) {
        $jobs=@(1..$size | ForEach-Object { MakeJob $_ 'large' })
        Seed $jobs
        $hash=(Get-FileHash (Join-Path $DataRoot 'queue.json')).Hash
        $clock=[Diagnostics.Stopwatch]::StartNew(); ExpandPlaylistGroups
        WaitCheck { @(QueueRows).Count -eq 39 } 'Expanded group exceeded or missed its 39-child page budget.'
        $elapsed=$clock.Elapsed.TotalMilliseconds
        Invoke 'DashboardNextPage'
        WaitCheck { @(QueueRows).Count -eq 39 } 'Second group page did not preserve bounded task controls.'
        $target='QueueItem-'+([guid]$jobs[40].id).ToString('N')
        SelectRow (Find $target); Capture ('downloads-group-'+$size+'-page2')
        $id=@(GroupToggles)[0].Current.AutomationId; Invoke $id
        WaitCheck { @(QueueRows).Count -eq 0 } 'Large collapse retained child controls.'
        Invoke ($id.Replace('PlaylistGroupToggle-','PlaylistGroupMenu-'))
        ClickOwnedMenu $(if($polish){'Przejdź do zadania'}else{'Go to job'})
        WaitCheck { $null -ne ($root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$target))) } 'Go to selected child lost its page.'
        if(-not (Find $target).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Large paging changed selected GUID.' }
        if((Get-FileHash (Join-Path $DataRoot 'queue.json')).Hash -ne $hash) { throw 'Large projection changed queue bytes.' }
        $owned.Refresh(); $perf+=@{jobs=$size;liveTaskCards=@(QueueRows).Count;headers=@(GroupToggles).Count;expandAndUiaObservationMs=$elapsed;workingSetBytes=$owned.WorkingSet64;queueHash=$hash}
    }
    $perf | ConvertTo-Json | Set-Content (Join-Path $DataRoot 'playlist-group-performance.json') -Encoding utf8
    Invoke 'NavSettings'; Invoke 'SettingsCategoryPersonalization'
    Choose 'AppearanceSection' 0
    SetText 'AppearanceColorAccent' '#3388CC'
    Invoke 'AppearanceResetSection'
    WaitCheck { $null -ne (DialogButton @('Anuluj','Cancel')) } 'Appearance MessageBoxForm absent.'
    $appearancePath=Join-Path $DataRoot 'appearance.json'
    $custom=Get-Content -LiteralPath $appearancePath -Raw | ConvertFrom-Json
    if($custom.global.colors.Accent -ne '#3388CC') { throw 'Custom appearance fixture was not saved.' }
    $appearanceHash=(Get-FileHash -LiteralPath $appearancePath).Hash
    Capture 'appearance-reset-confirmation' ([PlaylistWindow]::Popup($owned.Id,$hwnd))
    AcceptDialog @('Anuluj','Cancel')
    if((Get-FileHash -LiteralPath $appearancePath).Hash -ne $appearanceHash) { throw 'Cancelled appearance reset changed the profile.' }
    Invoke 'AppearanceResetSection'
    AcceptDialog @('Resetuj tę sekcję','Reset this section')
    $reset=Get-Content -LiteralPath $appearancePath -Raw | ConvertFrom-Json
    if($reset.global.colors.Accent) { throw 'Confirmed appearance reset retained the custom accent.' }
    if((Get-FileHash (Join-Path $DataRoot 'queue.json')).Hash -ne $hash) { throw 'Appearance reset changed canonical queue bytes.' }
    Write-Output 'MessageBoxForm appearance reset PASS: cancel preserves profile hash; confirmation resets only appearance, queue unchanged.'
    Write-Output 'Group fixtures PASS: mixed/singles; Space/Enter; selection; shell/session; ON/OFF restart; queue hashes; child removal; 120/1300 paged39 children+1header, go-to-selected-child.'
}

function Capture([string]$Name, [IntPtr]$Window = $hwnd) {
    Start-Sleep -Milliseconds 300
    $b=[PlaylistWindow]::Bounds($Window)
    $bitmap=[Drawing.Bitmap]::new($b.R-$b.L,$b.B-$b.T); $g=[Drawing.Graphics]::FromImage($bitmap); $dc=$g.GetHdc()
    try { if(-not [PlaylistWindow]::Capture($Window,$owned.Id,$dc)) { throw 'Owned HWND PrintWindow failed' } }
    finally { $g.ReleaseHdc($dc); $g.Dispose() }
    $path=Join-Path $DataRoot ($Name+'.png')
    try { $bitmap.Save($path) } finally { $bitmap.Dispose() }
    $captureRoot=if($Window -eq $hwnd){$root}else{[System.Windows.Automation.AutomationElement]::FromHandle($Window)}
    $nodes=@($captureRoot.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        $c=$_.Current; $r=$c.BoundingRectangle
        @{id=$c.AutomationId;name=$c.Name;enabled=$c.IsEnabled;offscreen=$c.IsOffscreen;bounds=@($r.X,$r.Y,$r.Width,$r.Height)}
    })
    $nodes | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $DataRoot ($Name+'-uia.json')) -Encoding UTF8
    ConvertTo-Json -InputObject @(ReadQueue) -Depth 30 | Set-Content (Join-Path $DataRoot ($Name+'-queue.json')) -Encoding UTF8
    $script:playlistCaptures.Add(@{file=$Name+'.png';width=$b.R-$b.L;height=$b.B-$b.T;dpi=[PlaylistWindow]::GetDpiForWindow($hwnd);textScale=$TextScale;
        sha256=(Get-FileHash $path -Algorithm SHA256).Hash;sourceCommit=$SourceCommit;sourceDirty=[bool]$SourceDirty;kind=$(if($Window -eq $hwnd){'full-window'}else{'owned-popup'});pid=$owned.Id;binarySha256=(Get-FileHash $appPath -Algorithm SHA256).Hash;
        managedBinarySha256=(Get-FileHash ([IO.Path]::ChangeExtension($appPath,'.dll')) -Algorithm SHA256).Hash;
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
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { Invoke 'PlaylistSharedOptionsToggle' }
    Choose 'PlaylistQualitySelector' 3
    Choose 'PlaylistContainerSelector' 2
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { Invoke 'PlaylistSharedOptionsToggle' }
    (Find 'PlaylistEntriesList').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1,0)
    Reveal (Find 'PlaylistTitle'); Capture 'playlist-partial-selection'
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) {
        $previewBounds=(Find 'PlaylistPreview').Current.BoundingRectangle
        foreach($id in @('PlaylistAddSelectedButton','PlaylistSharedOptionsToggle','PlaylistEntriesToggle','PlaylistSelectAllButton','PlaylistClearSelectionButton','PlaylistNextPageButton')) {
            $b=(Find $id).Current.BoundingRectangle
            if($b.Left -lt $previewBounds.Left -or $b.Right -gt $previewBounds.Right) { throw ('Preview horizontal overflow: '+$id) }
        }
        if((Find 'PlaylistEntriesList').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.HorizontallyScrollable) { throw 'Preview unexpectedly scrolls horizontally.' }
    }
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) { Capture 'playlist-preview-selection' }
    if($Count -gt 40) {
        Invoke 'PlaylistNextPageButton'; [void](Find 'PlaylistEntryToggle41'); Toggle 41
        $rows=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {$_.Current.AutomationId -like 'PlaylistEntryToggle*'})
        if($rows.Count -ne [Math]::Min(40,$Count-40)) { throw 'Playlist allocated beyond the page limit.' }
        Reveal (Find 'PlaylistEntryToggle70'); Capture 'playlist-long-paged'
        Capture 'playlist-preview-long120-page2'
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
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) {
        WaitCheck { @(GroupToggles).Count -eq 1 } 'Handoff did not create exactly one playlist group.'
        if(@(QueueRows).Count -ne 0) { throw 'Collapsed group materialized task controls.' }
        ExpandPlaylistGroups
    }
    WaitCheck { @(QueueRows).Count -eq 3 } 'Handoff did not expose exactly three jobs.'
    $before=@(ReadQueue)
    if($before.Count -ne 3) { throw 'Handoff did not persist exactly three jobs.' }
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
        Capture 'downloads-playlist-expanded'
        $groupToggle=@(GroupToggles)[0]; $groupId=$groupToggle.Current.AutomationId
        Invoke $groupId
        WaitCheck { @(QueueRows).Count -eq 0 } 'Collapse retained live child controls.'
        Capture 'downloads-group-selected-collapsed'
        Invoke $groupId
        WaitCheck { @(QueueRows).Count -eq 3 } 'Expand did not restore the three canonical children.'
        if(-not (Find "QueueItem-$firstId").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Collapse changed the selected GUID.' }
    }
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) {
        $detailsCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'InspectorDetails')
        if(-not $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$detailsCondition)) { Invoke 'DashboardToggleInspector' }
        Reveal (Find 'InspectorDetails'); Capture 'playlist-queue-selected-inspector'
        Capture 'downloads-group-selected-inspector'
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
            $script:observedSelectMenuItem=$null
            try { WaitCheck { $script:observedSelectMenuItem=FindOwnedPopupItem $selectCaption; $null -ne $script:observedSelectMenuItem } 'Queue Select menu item absent.' }
            catch { SaveOwnedWindowDiagnostics; throw }
            $menuItem=$script:observedSelectMenuItem
            if(-not $menuItem -or -not $menuItem.Current.IsEnabled -or $menuItem.Current.ProcessId -ne $owned.Id) { SaveOwnedWindowDiagnostics; throw 'Owned Select menu item unavailable.' }
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
    if($Interface -eq 'ModernDashboard' -and -not $ClassicRecovery) {
        WaitCheck { @(GroupToggles).Count -eq 1 } 'Restart lost grouping preference.'
        if(@(QueueRows).Count -ne 0) { throw 'Restart did not default to collapsed.' }
        Capture 'downloads-group-after-restart'
        ExpandPlaylistGroups
    }
    $row=Find "QueueItem-$firstId"; Reveal $row; SelectRow $row
    if((@(ReadQueue).id -join ',') -ne ($before.id -join ',')) { throw 'Restart changed queue IDs/order.' }
    Capture 'playlist-after-restart'
    if($Transfer) {
        Invoke 'DashboardStartAll'
        WaitCheck { @(Get-ChildItem $DataRoot -Recurse -Filter '.review-transfer-ready').Count -gt 0 } 'Actual byte transfer did not reach its gate.'
        SelectRow (Find "QueueItem-$firstId"); Capture 'playlist-downloading'
        $groupToggle=@(GroupToggles)[0].Current.AutomationId
        $countsId=$groupToggle.Replace('PlaylistGroupToggle-','PlaylistGroupCounts-')
        Invoke $groupToggle
        WaitCheck { (Find $countsId).Current.Name -match '[1-3] (aktywnych|active)' } 'Collapsed group did not report actual active transfer.'
        Capture 'downloads-playlist-mixed-statuses'
        ExpandPlaylistGroups
        Invoke "QueuePrimaryAction-$firstId"
        WaitCheck { QueueStatusIs $before[0].id 'cancelled' } 'Cancel did not update the canonical row.'
        AssertPersistedStatus $before[0].id @(8)
        Capture 'playlist-cancelled'
        Invoke "QueuePrimaryAction-$firstId"
        WaitCheck { QueueStatusIs $before[0].id 'active' } 'Retry did not restart the first job.'
        AssertPersistedStatus $before[0].id @(2,3)
        RestartOwned
        Invoke 'NavDownloads'
        ExpandPlaylistGroups
        WaitCheck { QueueStatusIs $before[0].id 'interrupted' } 'Restart did not recover Interrupted.'
        AssertPersistedStatus $before[0].id @(13)
        SelectRow (Find "QueueItem-$firstId"); Capture 'playlist-interrupted'
        Capture 'downloads-group-interrupted-and-active'
        WaitCheck { QueueStatusIs $before[1].id 'failed' } 'Synthetic executor failure did not update the row.'
        AssertPersistedStatus $before[1].id @(7)
        $secondId=([guid]$before[1].id).ToString('N')
        SelectRow (Find "QueueItem-$secondId"); Capture 'playlist-failed'
        Get-ChildItem $DataRoot -Recurse -Filter '.playlist-failure-ready' | ForEach-Object { Set-Content (Join-Path $_.DirectoryName '.playlist-retry-allow') 'allowed' }
        Invoke "QueuePrimaryAction-$secondId"
        Invoke "QueuePrimaryAction-$firstId"
        WaitCheck {
            Get-ChildItem $DataRoot -Recurse -Filter '.review-transfer-ready' | ForEach-Object { Set-Content (Join-Path $_.DirectoryName '.review-transfer-release') 'allowed' }
            @($before | Where-Object { QueueStatusIs $_.id 'completed' }).Count -eq 3
        } 'Retry did not complete all three canonical jobs.'
        $completed=@(ReadQueue)
        if(@($completed | Where-Object status -eq 6).Count -ne 3) { throw 'Completion did not persist for all three jobs.' }
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
        Invoke 'PlaylistClearSelectionButton'; Toggle 5
        Invoke 'PlaylistSharedOptionsToggle'; Choose 'PlaylistQualitySelector' 7; Invoke 'PlaylistSharedOptionsToggle'
        Invoke 'PlaylistAddSelectedButton'
        WaitCheck { @(QueueRows).Count -eq 4 } 'Audio-only selection did not reach the canonical queue.'
        $audioRow=@(QueueRows) | Where-Object { $_.Current.AutomationId -notin @($before | ForEach-Object { 'QueueItem-'+([guid]$_.id).ToString('N') }) }
        $audioId=$audioRow.Current.AutomationId.Substring('QueueItem-'.Length)
        WaitCheck { QueueStatusIs $audioId 'completed' } 'Audio-only selection did not complete.'
        $audioJob=@(ReadQueue) | Where-Object videoId -eq 'p005'
        if($audioJob.status -ne 6) { throw 'Audio-only completion did not persist.' }
        if($audioJob.qualityPresetId -ne 'audio' -or -not $audioJob.selectedFormats.audioOnly -or $audioJob.selectedFormats.videoFormatId) { throw 'Audio-only selection resolved a video stream.' }
        Invoke 'PlaylistClosePreview'
        SelectRow (Find ('QueueItem-'+([guid]$audioJob.id).ToString('N')))
        Capture 'playlist-audio-only-completed'
    }
    if($Groups) { RunGroupFixtures $before[0] }
    Write-Output "Playlist Windows UIA PASS: $Count metadata entries; private+deleted excluded; 3 selected/added; repeat add skipped 3; 1080/Mkv; ordered IDs and thumbnails persisted through restart; watch+list single video. Profile: $DataRoot"
}
catch { Write-Output ($_.ToString() + "`n" + $_.ScriptStackTrace); throw }
finally {
    if($owned -and -not $owned.HasExited) {
        CloseOwned
    }
}


