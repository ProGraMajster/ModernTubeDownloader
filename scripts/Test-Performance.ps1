param([Parameter(Mandatory)][string]$ApplicationPath,[Parameter(Mandatory)][string]$Fixture,[Parameter(Mandatory)][string]$Evidence,[int]$Cycles=20,[int]$Width=1536,[int]$Height=1024,[int]$Left=0,[switch]$RequirePaging,[int]$PageTurns=30,[switch]$RequireBitmapRelease,[switch]$Modern,[switch]$ReusePager,[switch]$Groups,[int]$ShellCycles=0,[ValidateSet('Full','ShellOnly')][string]$Scenario='Full')
$ErrorActionPreference='Stop'
if($ReusePager -and -not $RequirePaging){throw 'ReusePager requires paging and per-page identity verification.'}
if($Scenario -eq 'ShellOnly' -and (-not $Modern -or $ShellCycles -lt 1)){throw 'ShellOnly requires -Modern and at least one shell cycle.'}
if(Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue){throw 'An existing app is active; no isolation test started.'}
$Fixture=[IO.Path]::GetFullPath($Fixture);$ApplicationPath=[IO.Path]::GetFullPath($ApplicationPath);$Evidence=[IO.Path]::GetFullPath($Evidence)
if(-not $Fixture.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $Fixture -Leaf) -notlike 'mtd-automation-smoke-*'){throw 'Unsafe fixture root'}
if(Test-Path -LiteralPath $Evidence){throw 'Refusing to overwrite an earlier run'}
New-Item -ItemType Directory -Path $Evidence | Out-Null
$qaProfile=Join-Path ([IO.Path]::GetTempPath()) ('mtd-automation-smoke-perf-run-'+[guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $Fixture -Destination $qaProfile -Recurse
function Hashes {param($path)
 $r=@{};foreach($file in @('queue.json','history.json','live-sessions.json')){ $target=Join-Path $path $file; $r[$file]=if(Test-Path -LiteralPath $target){(Get-FileHash -LiteralPath $target).Hash}else{'ABSENT'} }
 $batch=Join-Path $path 'Batch';$r['Batch']='ABSENT'
 if(Test-Path -LiteralPath $batch){$r['Batch']='PRESENT';Get-ChildItem -LiteralPath $batch -File -Recurse|ForEach-Object{$r[[IO.Path]::GetRelativePath($path,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName).Hash}}
 return $r
}
$before=Hashes $qaProfile
$oldRoot=$env:MODERNTUBEDOWNLOADER_DATA_ROOT;$oldPerf=$env:MTD_PERFORMANCE_DIAGNOSTICS;$oldSkip=$env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL
$env:MODERNTUBEDOWNLOADER_DATA_ROOT=$qaProfile;$env:MTD_PERFORMANCE_DIAGNOSTICS='1';$env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL='1'
$monitor=[Collections.Generic.List[object]]::new();$watch=[Diagnostics.Stopwatch]::StartNew();$p=$null;$client=$null;$reason=$null;$hwndMs=$null
try{
 $p=Start-Process -FilePath $ApplicationPath -WindowStyle Hidden -PassThru
 $script=Join-Path $PSScriptRoot $(if($Modern -or $Groups -or $ShellCycles){'Test-FeaturePerformanceNative.ps1'}else{'Test-PerformanceNative.ps1'})
 $nativeScriptSha=(Get-FileHash -LiteralPath $script).Hash
 $command="& ([scriptblock]::Create([IO.File]::ReadAllText('"+$script.Replace("'","''")+"'))) -OwnedProcessId "+$p.Id+" -DataRoot '"+$qaProfile.Replace("'","''")+"' -Evidence '"+$Evidence.Replace("'","''")+"' -Cycles $Cycles -Width $Width -Height $Height -Left $Left -PageTurns $PageTurns"
 if($RequirePaging){$command+=" -RequirePaging"}
 if($ReusePager){$command+=" -ReusePager"}
 if($Scenario -ne 'Full'){$command+=" -Scenario $Scenario"}
 if($Modern){$command+=" -Modern"};if($Groups){$command+=" -Groups"};if($ShellCycles){$command+=" -ShellCycles $ShellCycles"}
 $client=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-Command',$command) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Evidence 'client.log') -RedirectStandardError (Join-Path $Evidence 'client-error.log')
 while(-not $p.HasExited){
  $p.Refresh();$elapsed=$watch.Elapsed.TotalMilliseconds
  if($null -eq $hwndMs -and $p.MainWindowHandle -ne [IntPtr]::Zero){$hwndMs=$elapsed}
  $monitor.Add(@{elapsedMs=$elapsed;workingSet=$p.WorkingSet64;privateBytes=$p.PrivateMemorySize64;cpuMs=$p.TotalProcessorTime.TotalMilliseconds;threads=$p.Threads.Count;handles=$p.HandleCount;hwnd=$p.MainWindowHandle.ToInt64()})
  if(-not $stackStarted -and $watch.Elapsed.TotalSeconds -gt 30){$stackStarted=$true;$stackTool=Join-Path $PSScriptRoot '../artifacts/performance-large-history/tools/dotnet-stack.exe';if(Test-Path -LiteralPath $stackTool){Start-Process -FilePath $stackTool -ArgumentList @('report','-p',$p.Id) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Evidence 'stack-30s.txt') -RedirectStandardError (Join-Path $Evidence 'stack-error.log') | Out-Null}}
  # Capture a stalled UI while it is stalled, not only at an arbitrary wall-clock offset.
  if($ShellCycles -and $watch.Elapsed.TotalSeconds -gt 5 -and ($null -eq $lastStallCheck -or $elapsed-$lastStallCheck -gt 1000)){
   $lastStallCheck=$elapsed;$liveLog=Join-Path $qaProfile ('performance-'+$p.Id+'.jsonl')
   if((Test-Path -LiteralPath $liveLog) -and $stallStacks -lt 2){
    try{$sample=Get-Content -LiteralPath $liveLog -Tail 1|ConvertFrom-Json;$age=@($sample.counters|Where-Object Key -eq 'uiHeartbeatAgeMs')[0].Value}catch{$age=0}
    if($age -gt 3500 -and ($null -eq $lastStallStack -or $elapsed-$lastStallStack -gt 10000)){
     $lastStallStack=$elapsed;$stallStacks++;$stackTool=Join-Path $PSScriptRoot '../artifacts/performance-large-history/tools/dotnet-stack.exe'
     if(Test-Path -LiteralPath $stackTool){Start-Process -FilePath $stackTool -ArgumentList @('report','-p',$p.Id) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Evidence ('stack-stall-'+$stallStacks+'.txt')) -RedirectStandardError (Join-Path $Evidence ('stack-stall-'+$stallStacks+'-error.log'))|Out-Null}
    }
   }
  }
  if($watch.Elapsed.TotalSeconds -gt 180 -or $p.PrivateMemorySize64 -gt 6GB){$reason='Owned QA safety limit:180s or6GiB';break}
  if($client.HasExited -and $watch.Elapsed.TotalSeconds -gt 60){$p.CloseMainWindow()|Out-Null;if(-not $p.WaitForExit(15000)){$reason='Owned app failed graceful shutdown';break}}
  Start-Sleep -Milliseconds 250
 }
}finally{
 if($p -and -not $p.HasExited){$p.CloseMainWindow()|Out-Null;if(-not $p.WaitForExit(5000)){$p.Kill($true);$p.WaitForExit()}}
 if($client -and -not $client.HasExited){if(-not $client.WaitForExit(5000)){$client.Kill();$client.WaitForExit()}}
 $env:MODERNTUBEDOWNLOADER_DATA_ROOT=$oldRoot;$env:MTD_PERFORMANCE_DIAGNOSTICS=$oldPerf;$env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL=$oldSkip
 Get-ChildItem -LiteralPath $qaProfile -Filter ('performance-'+$p.Id+'.jsonl') | Copy-Item -Destination $Evidence
 $logs=Join-Path $qaProfile 'logs';if(Test-Path -LiteralPath $logs){Copy-Item -LiteralPath $logs -Destination (Join-Path $Evidence 'logs') -Recurse}
 @{scenario=$Scenario;reusePager=[bool]$ReusePager;nativeScriptSha256=$nativeScriptSha;profile=$qaProfile;fixture=$Fixture;binary=$ApplicationPath;sha256=(Get-FileHash -LiteralPath $ApplicationPath).Hash;managedSha256=(Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($ApplicationPath,'.dll'))).Hash;hwndMs=$hwndMs;reason=$reason;before=$before;after=(Hashes $qaProfile);samples=@($monitor.ToArray())}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Evidence 'process-metrics.json')
}
Write-Output ("Evidence="+$Evidence+" HWNDms="+$hwndMs+" safety="+$reason)

$failures=[Collections.Generic.List[string]]::new()
$nativeFile=Join-Path $Evidence 'native-result.json'
if(-not(Test-Path -LiteralPath $nativeFile)){$failures.Add('Native result absent')}elseif((Get-Content -LiteralPath $nativeFile -Raw|ConvertFrom-Json).result -ne 'PASS'){$failures.Add('Native UI scenario failed')}
if($reason){$failures.Add($reason)}
$after=Hashes $qaProfile
foreach($key in @($before.Keys)+@($after.Keys)|Select-Object -Unique){if($before[$key] -ne $after[$key]){$failures.Add('Persistence changed: '+$key)}}
$stops=@(Get-ChildItem -LiteralPath $Evidence -Filter 'performance-*.jsonl'|ForEach-Object{Get-Content -LiteralPath $_.FullName|ForEach-Object{$_|ConvertFrom-Json}|Where-Object name -eq 'process.stop'})
if($RequireBitmapRelease){
 if($stops.Count -ne 1){$failures.Add('Final memory sample absent')}
 else{foreach($key in @('nativeBitmapBytes','thumbnail.displayCopiesBytes','thumbnail.cachedBytes','thumbnail.pending','thumbnail.active')){ $value=@($stops[0].counters|Where-Object Key -eq $key);if($value.Count -ne 1 -or $value[0].Value -ne 0){$failures.Add('Not released: '+$key)} }}
}
@{result=if($failures.Count){'FAIL'}else{'PASS'};failures=@($failures);requireBitmapRelease=[bool]$RequireBitmapRelease}|ConvertTo-Json|Set-Content (Join-Path $Evidence 'qualification.json')
if($failures.Count){exit 1}
