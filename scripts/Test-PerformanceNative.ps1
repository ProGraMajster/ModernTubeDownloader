param([int]$OwnedProcessId,[string]$DataRoot,[string]$Evidence,[int]$Cycles=20,[int]$Width=1536,[int]$Height=1024,[int]$Left=0,[switch]$RequirePaging,[int]$PageTurns=30,[switch]$ReusePager)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;
public static class PerfWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int L,T,R,B;}
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static Rect Capture(IntPtr h,int pid,string path){uint p;GetWindowThreadProcessId(h,out p);if(p!=pid)throw new Exception("Wrong PID");var old=SetThreadDpiAwarenessContext(new IntPtr(-4));try{Rect r;GetWindowRect(h,out r);using(var b=new System.Drawing.Bitmap(r.R-r.L,r.B-r.T))using(var g=System.Drawing.Graphics.FromImage(b)){var dc=g.GetHdc();try{if(!PrintWindow(h,dc,2))throw new Exception("PrintWindow failed");}finally{g.ReleaseHdc(dc);}b.Save(path);}return r;}finally{SetThreadDpiAwarenessContext(old);}}
 public static void Place(IntPtr h,int x,int width,int height){var old=SetThreadDpiAwarenessContext(new IntPtr(-4));try{SetWindowPos(h,IntPtr.Zero,x,0,width,height,0x14);}finally{SetThreadDpiAwarenessContext(old);}}
}
'@
$steps=[Collections.Generic.List[object]]::new()
$p=Get-Process -Id $OwnedProcessId
$watch=[Diagnostics.Stopwatch]::StartNew()
while($watch.Elapsed.TotalSeconds -lt 60){$p.Refresh();if($p.MainWindowHandle -ne [IntPtr]::Zero){break};Start-Sleep -Milliseconds 100}
$hwnd=$p.MainWindowHandle
if($hwnd -eq [IntPtr]::Zero){throw 'Main HWND absent within60s'}
[PerfWindow]::Place($hwnd,$Left,$Width,$Height)
$root=[System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
function Find([string]$id){$t=[Diagnostics.Stopwatch]::StartNew();do{$node=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id));if($node){return $node};Start-Sleep -Milliseconds 100}while($t.Elapsed.TotalSeconds -lt 15);throw "Missing $id within15s"}
function Invoke([string]$id){$t=[Diagnostics.Stopwatch]::StartNew();$node=Find $id;$findMs=$t.Elapsed.TotalMilliseconds;$pattern=$node.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern);$patternMs=$t.Elapsed.TotalMilliseconds;$pattern.Invoke();$total=$t.Elapsed.TotalMilliseconds;$steps.Add(@{action=$id;ms=$total;findMs=$findMs;patternMs=$patternMs-$findMs;invokeMs=$total-$patternMs;utc=[DateTime]::UtcNow.ToString('O')})}
function Capture([string]$name){$file=Join-Path $Evidence ($name+'.png');$rect=[PerfWindow]::Capture($hwnd,$OwnedProcessId,$file);@{pid=$OwnedProcessId;hwnd=$hwnd.ToInt64();dpi=[PerfWindow]::GetDpiForWindow($hwnd);width=$rect.R-$rect.L;height=$rect.B-$rect.T;sha256=([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($file))).Replace('-',''))}|ConvertTo-Json|Set-Content -Encoding UTF8 (Join-Path $Evidence ($name+'-manifest.json'))}
function WaitPage([int]$page,[int]$expectedMatches,[int]$total,$summary=$null){
 $t=[Diagnostics.Stopwatch]::StartNew();if($null -eq $summary){$summary=Find 'HistoryPageSummary'}
 do{$numbers=@([regex]::Matches($summary.Current.Name,'\d+')|ForEach-Object{[int]$_.Value});if($numbers.Count -eq 4 -and $numbers[0] -eq $page -and $numbers[2] -eq $expectedMatches -and $numbers[3] -eq $total){return};Start-Sleep -Milliseconds 20}while($t.Elapsed.TotalSeconds -lt 15)
 throw "History did not publish page=$page matches=$expectedMatches total=$total within15s; observed=$($summary.Current.Name)"
}
function AssertPageIdentities([int]$page){
 $prefix='HistoryCard-';$missingPart=''
 $actual=@($scanList.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)|ForEach-Object{$_.Current.AutomationId}|Where-Object{$_ -like ($prefix+'*')})
 $expected=@($orderedRecords|Select-Object -Skip (($page-1)*40) -First 40|ForEach-Object{$part=if($null -eq $_.LivePartIndex){$missingPart}else{[string]$_.LivePartIndex};$prefix+([guid]$_.QueueItemId).ToString('N')+'-'+$part})
 if(($actual -join ',') -ne ($expected -join ',')){throw "History page $page has incorrect canonical IDs/order"}
 $steps.Add(@{action='page.ids.verified';page=$page;count=$actual.Count})
}
try{
 $historyRecords=Get-Content -LiteralPath (Join-Path $DataRoot 'history.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $recordCount=$historyRecords.Count
 [void](Find 'NavHistory');Capture 'startup'
 $firstHistory=[Diagnostics.Stopwatch]::StartNew()
 Invoke 'NavHistory';[void](Find 'HistorySearch')
 if($RequirePaging){WaitPage 1 $recordCount $recordCount}
 $steps.Add(@{action='history.ready';ms=$firstHistory.Elapsed.TotalMilliseconds;utc=[DateTime]::UtcNow.ToString('O')})
 Capture 'history-first'
 if($RequirePaging -and $recordCount -gt 0){
  $list=Find 'HistoryList';$scroll=$null
  if($list.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$scroll)){
   $steps.Add(@{action='history.scroll';horizontal=$scroll.Current.HorizontallyScrollable;vertical=$scroll.Current.VerticallyScrollable})
   if($scroll.Current.HorizontallyScrollable){throw 'History list overflows horizontally'}
  }
 }
 $search=Find 'HistorySearch';$value=$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
 foreach($query in @('00049','日本語','NO-MATCH-XYZ','')){
  $t=[Diagnostics.Stopwatch]::StartNew();$value.SetValue($query)
  if($RequirePaging){$expectedMatches=if($query -eq '00049'){[int]($recordCount -gt 49)}elseif($query -eq 'NO-MATCH-XYZ'){0}else{$recordCount};WaitPage 1 $expectedMatches $recordCount}else{Start-Sleep -Milliseconds 400}
  $steps.Add(@{action='search';query=$query;ms=$t.Elapsed.TotalMilliseconds;utc=[DateTime]::UtcNow.ToString('O')})
 }
 for($i=0;$i -lt $Cycles;$i++){Invoke 'NavDownloads';Invoke 'NavHistory';if($RequirePaging){WaitPage 1 $recordCount $recordCount}}
 $next=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'HistoryNextPage'))
 if($RequirePaging -and -not $next){throw 'Required history pager is absent'}
 $visited=1
 if($next){
  # A second, explicit audit uses the same native pager instance throughout the
  # scan and verifies every displayed canonical GUID. Discovery-mode measurements
  # remain available unchanged; never compare their total client time as UI work.
  if($ReusePager){$scanSummary=Find 'HistoryPageSummary';$scanList=Find 'HistoryList';$orderedRecords=@($historyRecords|Sort-Object CompletedAt -Descending);AssertPageIdentities 1}
  for($i=0;$i -lt $PageTurns -and $next.Current.IsEnabled;$i++){
   if($ReusePager){$t=[Diagnostics.Stopwatch]::StartNew();$next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();$steps.Add(@{action='pager.invoke.cached';ms=$t.Elapsed.TotalMilliseconds})}
   else{Invoke 'HistoryNextPage'}
   $visited++
   if($RequirePaging){if($ReusePager){WaitPage $visited $recordCount $recordCount $scanSummary;AssertPageIdentities $visited}else{WaitPage $visited $recordCount $recordCount}}
  }
  Capture 'history-paged'
  if($visited -gt 1){Invoke 'HistoryPreviousPage';if($RequirePaging){WaitPage ($visited-1) $recordCount $recordCount}}
 }
 $steps.Add(@{action='pages.visited';count=$visited;records=$recordCount})
 foreach($id in @('NavLive','NavOther','NavSettings','NavDownloads')){Invoke $id;Capture ('view-'+$id)}
 Capture 'downloads-final';$result='PASS'
}catch{$result='FAIL';$errorText=$_.ToString();$errorText|Set-Content -Encoding UTF8 (Join-Path $Evidence 'native-error.txt');try{Capture 'failure'}catch{}}
finally{
 [void][PerfWindow]::PostMessage($hwnd,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
 try{
  $closing=[Diagnostics.Stopwatch]::StartNew();$confirmed=$false
  while(-not $p.HasExited -and $closing.Elapsed.TotalSeconds -lt 15){
   $p.Refresh()
   $ownedWindows=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$OwnedProcessId))
   foreach($owned in $ownedWindows){
    $exit=$owned.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ShutdownExit'))
    if($exit -and -not $confirmed){$exit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();$confirmed=$true;$steps.Add(@{action='ShutdownExit';utc=[DateTime]::UtcNow.ToString('O')})}
   }
   Start-Sleep -Milliseconds 100
  }
  if(-not $p.HasExited){throw 'Owned QA process did not exit within15s after close/confirmation'}
 }catch{$result='FAIL';$errorText=$_.ToString();$errorText|Set-Content -Encoding UTF8 (Join-Path $Evidence 'close-error.txt')}
 @{result=$result;error=$errorText;steps=@($steps.ToArray());pid=$OwnedProcessId;hwnd=$hwnd.ToInt64()}|ConvertTo-Json -Depth 8|Set-Content -Encoding UTF8 (Join-Path $Evidence 'native-result.json')
}
if($result -ne 'PASS'){exit 1}
