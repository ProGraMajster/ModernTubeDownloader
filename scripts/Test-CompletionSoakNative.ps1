param([Parameter(Mandatory)][int]$OwnedProcessId, [Parameter(Mandatory)][long]$OwnedStartUtcTicks,
    [Parameter(Mandatory)][string]$ApplicationPath, [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$Evidence, [ValidateRange(60,14400)][int]$DurationSeconds = 7200)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run the UIA client in Windows PowerShell.' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;
public static class CompletionSoakWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static void Capture(IntPtr h,int pid,string path) {
  uint p;GetWindowThreadProcessId(h,out p);if(p!=pid)throw new Exception("Wrong HWND owner");
  var old=SetThreadDpiAwarenessContext(new IntPtr(-4));try {
   Rect r;if(!GetWindowRect(h,out r)||r.R<=r.L||r.B<=r.T)throw new Exception("Invalid HWND bounds");
   using(var b=new System.Drawing.Bitmap(r.R-r.L,r.B-r.T))using(var g=System.Drawing.Graphics.FromImage(b)){
    var dc=g.GetHdc();try{if(!PrintWindow(h,dc,2))throw new Exception("PrintWindow failed");}finally{g.ReleaseHdc(dc);}b.Save(path);
   }
  }finally{SetThreadDpiAwarenessContext(old);}
 }
}
'@
$clock = [Diagnostics.Stopwatch]::StartNew(); $hwnd = [IntPtr]::Zero; $root = $null; $process = $null
$result = 'FAIL'; $failure = $null; $rounds = 0; $shellRounds = 0; $captures = 0
$visitedPages = [Collections.Generic.HashSet[int]]::new()
$events = [IO.StreamWriter]::new((Join-Path $Evidence 'native-actions.jsonl'), $false, [Text.UTF8Encoding]::new($false)); $events.AutoFlush = $true
function Event([string]$phase, [string]$action, $extra = @{}) {
    $row = @{ utc = [DateTime]::UtcNow.ToString('O'); elapsedMs = $clock.Elapsed.TotalMilliseconds; phase = $phase; action = $action; pid = $OwnedProcessId }
    foreach ($key in $extra.Keys) { $row[$key] = $extra[$key] }
    $events.WriteLine(($row | ConvertTo-Json -Depth 6 -Compress))
}
function Assert-Owned {
    $process.Refresh()
    if ($process.HasExited -or $process.StartTime.ToUniversalTime().Ticks -ne $OwnedStartUtcTicks -or
        [IO.Path]::GetFullPath($process.Path) -ne [IO.Path]::GetFullPath($ApplicationPath)) { throw 'Owned application identity changed/exited' }
    if ($hwnd -ne [IntPtr]::Zero) {
        [uint32]$owner = 0; [void][CompletionSoakWindow]::GetWindowThreadProcessId($hwnd, [ref]$owner)
        if ($owner -ne $OwnedProcessId) { throw 'HWND no longer belongs to the owned application' }
    }
}
function Action([string]$name, [scriptblock]$body) {
    Assert-Owned; Event 'begin' $name; $actionClock = [Diagnostics.Stopwatch]::StartNew()
    & $body
    if ($actionClock.Elapsed.TotalSeconds -gt 15) { throw "Native action exceeded existing 15s limit: $name" }
    Event 'end' $name @{ milliseconds = $actionClock.Elapsed.TotalMilliseconds }
}
function Find([string]$id) {
    $findClock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $node = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
        if ($node -and $node.Current.ProcessId -eq $OwnedProcessId -and -not $node.Current.IsOffscreen -and $node.Current.BoundingRectangle.Width -gt 0) { return $node }
        Start-Sleep -Milliseconds 50
    } while ($findClock.Elapsed.TotalSeconds -lt 15)
    throw "Visible owned control absent within15s: $id"
}
function Invoke([string]$id) {
    Action ('invoke.' + $id) { $node = Find $id; if (-not $node.Current.IsEnabled) { throw "Disabled action: $id" }; $node.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}
function Navigate([string]$id, [string]$ready) { Invoke $id; Action ('visible.' + $ready) { [void](Find $ready) } }
function Page([int]$page, [int]$matches, [int]$total) {
    Action ('history.page.' + $page) {
        $summary = Find 'HistoryPageSummary'; $wait = [Diagnostics.Stopwatch]::StartNew()
        do {
            $numbers = @([regex]::Matches($summary.Current.Name, '\d+') | ForEach-Object { [int]$_.Value })
            if ($numbers.Count -eq 4 -and $numbers[0] -eq $page -and $numbers[2] -eq $matches -and $numbers[3] -eq $total) {
                if ($matches -eq $total) { [void]$visitedPages.Add($page) }; return
            }
            Start-Sleep -Milliseconds 50
        } while ($wait.Elapsed.TotalSeconds -lt 15)
        throw "Wrong history page/count: $($summary.Current.Name)"
    }
}
function Set-Search([string]$query) { Action 'history.search' { (Find 'HistorySearch').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($query) } }
function Jump-Page([int]$page) { Action ('history.jump.' + $page) { (Find 'HistoryJumpPage').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($page) } }
function Capture([string]$name) {
    Action ('capture.' + $name) {
        $path = Join-Path $Evidence ($name + '.png'); [CompletionSoakWindow]::Capture($hwnd, $OwnedProcessId, $path)
        Event 'screenshot' $name @{ hwnd = $hwnd.ToInt64(); dpi = [CompletionSoakWindow]::GetDpiForWindow($hwnd); sha256 = (Get-FileHash -LiteralPath $path).Hash }
    }
}
function Switch-Shell([string]$choice, [int]$mode) {
    Navigate 'NavSettings' 'SettingsCategoryPersonalization'; Invoke 'SettingsCategoryPersonalization'
    $transitionStart = [DateTime]::UtcNow; Invoke $choice
    Action ('shell.completed.' + $mode) {
        $wait = [Diagnostics.Stopwatch]::StartNew()
        do {
            # Bounded tail; preserve the complete original file in the evidence.
            $entries = @(Get-Content -LiteralPath (Join-Path $DataRoot ('performance-' + $OwnedProcessId + '.jsonl')) -Tail 50 | ForEach-Object { try { $_ | ConvertFrom-Json } catch { } })
            $last = $entries | Where-Object { $_.name -eq 'shell.apply.end' -and [DateTime]::Parse($_.utc).ToUniversalTime() -ge $transitionStart } | Select-Object -Last 1
            if ($last -and @($last.counters | Where-Object Key -eq 'shell.currentMode')[0].Value -eq $mode) {
                Event 'verified' 'shell.apply.end' @{ target = $mode; transitionUtc = $last.utc; counters = $last.counters }; return
            }
            Start-Sleep -Milliseconds 50
        } while ($wait.Elapsed.TotalSeconds -lt 15)
        throw "Requested shell has no completed diagnostic event within15s: $mode"
    }
    Navigate 'NavDownloads' 'DownloadUrlInput'
}
try {
    $process = Get-Process -Id $OwnedProcessId; Assert-Owned
    Event 'begin' 'startup.hwnd'; $startup = [Diagnostics.Stopwatch]::StartNew()
    while ($process.MainWindowHandle -eq [IntPtr]::Zero -and $startup.Elapsed.TotalSeconds -lt 30) { Start-Sleep -Milliseconds 100; Assert-Owned }
    $hwnd = $process.MainWindowHandle; if ($hwnd -eq [IntPtr]::Zero) { throw 'Main HWND absent within30s' }
    Assert-Owned; $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    Event 'end' 'startup.hwnd' @{ hwnd = $hwnd.ToInt64(); dpi = [CompletionSoakWindow]::GetDpiForWindow($hwnd) }
    $history = Get-Content -LiteralPath (Join-Path $DataRoot 'history.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $recordCount = $history.Count; if ($recordCount -le 40) { throw 'Collection soak requires more than40 history records for paging' }
    Navigate 'NavDownloads' 'DownloadUrlInput'; Capture 'soak-start'
    $soakClock = [Diagnostics.Stopwatch]::StartNew(); $soakStartUtc = [DateTime]::UtcNow
    Event 'soak' 'started' @{ durationSeconds = $DurationSeconds; historyRecords = $recordCount }
    $nextRound = 0; $nextShell = 300; $nextCapture = 600; $lastTick = 0.0; $historyVisit = 0
    $pageCount = [int][Math]::Ceiling($recordCount / 40.0)
    while ($soakClock.Elapsed.TotalSeconds -lt $DurationSeconds) {
        Assert-Owned; $elapsed = $soakClock.Elapsed.TotalSeconds
        if ($elapsed - $lastTick -gt 5) { throw 'Native loop inactivity exceeded5s outside a recorded UI action' }; $lastTick = $elapsed
        if ([Math]::Abs((([DateTime]::UtcNow - $soakStartUtc).TotalSeconds) - $elapsed) -gt 3) { throw 'Wall/monotonic continuity changed during native soak' }
        if ($elapsed -ge $nextRound) {
            switch ($rounds % 5) {
                0 {
                    Navigate 'NavHistory' 'HistorySearch'; Set-Search ''; Page 1 $recordCount $recordCount
                    # Advance through different thumbnails across the long run, instead
                    # of repeatedly exercising only the first two pages of a large store.
                    $target = 1 + (($historyVisit * 2) % $pageCount); $historyVisit++
                    Jump-Page $target; Page $target $recordCount $recordCount
                    if ($target -lt $pageCount) {
                        Invoke 'HistoryNext'; Page ($target + 1) $recordCount $recordCount
                        Invoke 'HistoryPrevious'; Page $target $recordCount $recordCount
                    } else {
                        Invoke 'HistoryPrevious'; Page ($target - 1) $recordCount $recordCount
                        Invoke 'HistoryNext'; Page $target $recordCount $recordCount
                    }
                    Set-Search '__MTD_SOAK_EXPECT_NO_MATCH_9d53c8f6__'; Page 1 0 $recordCount
                    Set-Search ''; Page 1 $recordCount $recordCount
                }
                1 { Navigate 'NavDownloads' 'DownloadUrlInput' }
                2 { Navigate 'NavLive' 'LiveUrlInput' }
                3 { Navigate 'NavOther' 'OtherPageTitle' }
                4 { Navigate 'NavBatch' 'BatchTitle' }
            }
            $rounds++; Event 'verified' 'navigation.round' @{ round = $rounds }; $nextRound += 30
        }
        if ($elapsed -ge $nextShell) { Switch-Shell 'InterfaceClassic' 0; Switch-Shell 'InterfaceModernDashboard' 1; $shellRounds++; $nextShell += 300 }
        if ($elapsed -ge $nextCapture) { Capture ('soak-{0:D4}s' -f [int]$elapsed); $captures++; $nextCapture += 600 }
        $lastTick = $soakClock.Elapsed.TotalSeconds
        Start-Sleep -Milliseconds 250
    }
    $activeSeconds = $soakClock.Elapsed.TotalSeconds
    if ($rounds -lt [Math]::Floor($DurationSeconds / 30) - 1) { throw 'Required navigation cadence was not sustained' }
    if ($shellRounds -lt [Math]::Floor(($DurationSeconds - 1) / 300)) { throw 'Required shell roundtrips were not completed' }
    if ($captures -lt [Math]::Floor(($DurationSeconds - 1) / 600)) { throw 'Required screenshot cadence was not sustained' }
    Capture 'soak-final'; Event 'soak' 'completed' @{ activeSeconds = $activeSeconds; rounds = $rounds; shellRoundtrips = $shellRounds }; $result = 'PASS'
} catch { $failure = $_.ToString(); Event 'failure' 'native' @{ error = $failure }; try { if ($hwnd -ne [IntPtr]::Zero) { Capture 'soak-failure' } } catch { } }
finally {
    if ($hwnd -ne [IntPtr]::Zero -and $process -and -not $process.HasExited) {
        try {
            Assert-Owned; Event 'begin' 'shutdown'; [void][CompletionSoakWindow]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            $closing = [Diagnostics.Stopwatch]::StartNew(); $confirmed = $false
            while (-not $process.HasExited -and $closing.Elapsed.TotalSeconds -lt 15) {
                $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,
                    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $OwnedProcessId))
                foreach ($window in $windows) {
                    $exit = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ShutdownExit'))
                    if ($exit -and -not $confirmed) { $exit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $confirmed = $true }
                }
                Start-Sleep -Milliseconds 100; $process.Refresh()
            }
            if (-not $process.HasExited) { throw 'Owned app did not close within15s' }; Event 'end' 'shutdown'
        } catch { $result = 'FAIL'; $failure = $_.ToString(); Event 'failure' 'shutdown' @{ error = $failure } }
    }
    @{ result = $result; failure = $failure; pid = $OwnedProcessId; startUtcTicks = $OwnedStartUtcTicks; hwnd = $hwnd.ToInt64();
        durationSeconds = $DurationSeconds; activeSeconds = $activeSeconds; rounds = $rounds; shellRoundtrips = $shellRounds; periodicScreenshots = $captures;
        visitedHistoryPages = @($visitedPages | Sort-Object) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Evidence 'native-result.json') -Encoding UTF8
    $events.Dispose()
}
if ($result -ne 'PASS') { exit 1 }
