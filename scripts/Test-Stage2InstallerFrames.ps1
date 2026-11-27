[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Updater,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [ValidateSet('en','pl')][string]$Language = 'en'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Drawing
$witness = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-FeaturePerformanceNative.ps1') -Raw
if ($witness -notmatch "(?s)Add-Type -ReferencedAssemblies System.Drawing @'\r?\n(.*?)\r?\n'@") { throw 'Missing capture helper.' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $Matches[1]
$exe = (Resolve-Path -LiteralPath $Updater).Path
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a new evidence directory.' }
$evidence = [IO.Directory]::CreateDirectory($EvidenceDirectory).FullName
$localePath = Join-Path $PSScriptRoot '..\ModernTubeDownloader.Updater\InstallerMessages.json'
$messages = (Get-Content -LiteralPath $localePath -Raw -Encoding UTF8 | ConvertFrom-Json).$Language
$ui = [Windows.Automation.AutomationElement]
$tree = [Windows.Automation.TreeScope]::Descendants
$phases = @('Detail','Wait','Check','Extract','Backup','VerifyBackup','Replace','VerifyInstallation','Commit','Rollback',
    'Restart','Complete','RestartFailed','Failed','Restored','RecoveryRequired','Disconnected')
$results = [Collections.Generic.List[object]]::new()

function Show-Frames([int[]]$indices, [switch]$Stale) {
    $nonce = [guid]::NewGuid()
    $operation = [guid]::NewGuid()
    $pipe = [IO.Pipes.NamedPipeServerStream]::new(('ModernTubeDownloader.Update.' + $nonce.ToString('N')),
        [IO.Pipes.PipeDirection]::Out, 1, [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous)
    $observer = $null
    try {
        $start = [Diagnostics.ProcessStartInfo]::new($exe,
            ("--progress {0} {1} {2} {3}" -f $nonce.ToString('N'),$operation.ToString('N'),$PID,$Language))
        $start.UseShellExecute = $false
        $observer = [Diagnostics.Process]::Start($start)
        if (!$pipe.WaitForConnectionAsync().Wait(5000)) { throw 'The observer did not connect.' }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        do {
            $observer.Refresh()
            if ($observer.HasExited) { throw 'The observer exited before showing a window.' }
            if ($observer.MainWindowHandle -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 25
        } while ($clock.Elapsed.TotalSeconds -lt 8)
        if ($observer.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The native window did not appear.' }
        $hwnd = $observer.MainWindowHandle
        $window = $ui::FromHandle($hwnd)
        $sequence = 0L
        foreach ($phase in $indices) {
            $sequence++
            $done = 0
            $total = 0
            if ($phase -in 3,4,6,7,9) { $done = 3; $total = 8 }
            $data = New-Object byte[] 40
            [BitConverter]::GetBytes([int]1).CopyTo($data,0)
            $operation.ToByteArray().CopyTo($data,4)
            [BitConverter]::GetBytes([long]$(if ($Stale) { 0 } else { $sequence })).CopyTo($data,20)
            [BitConverter]::GetBytes([int]$phase).CopyTo($data,28)
            [BitConverter]::GetBytes([int]$done).CopyTo($data,32)
            [BitConverter]::GetBytes([int]$total).CopyTo($data,36)
            $pipe.Write($data,0,$data.Length)
            $pipe.Flush()
            $visible = if ($Stale) { 16 } else { $phase }
            $condition = [Windows.Automation.PropertyCondition]::new($ui::NameProperty, [string]$messages[$visible])
            $clock.Restart()
            do {
                if ($window.FindFirst($tree,$condition)) { break }
                Start-Sleep -Milliseconds 30
            } while ($clock.Elapsed.TotalSeconds -lt 6)
            if (!$window.FindFirst($tree,$condition)) { throw "No native label for $($phases[$visible])." }
            $file = if ($Stale) { 'stale-rejected.png' } else { '{0:D2}-{1}.png' -f $phase,$phases[$phase] }
            # Let the native progress control finish painting the reported position before capture.
            Start-Sleep -Milliseconds 900
            $dimensions = [PerfWindow]::Capture($hwnd,$observer.Id,(Join-Path $evidence $file))
            $results.Add(@{
                file=$file; pendingOrTerminal=$phases[$visible]; label=$messages[$visible]; fixture='synthetic one-way frame'
                dpi=[PerfWindow]::GetDpiForWindow($hwnd); width=$dimensions.R-$dimensions.L; height=$dimensions.B-$dimensions.T
            })
        }
    } finally {
        $pipe.Dispose()
        if ($observer) {
            if (!$observer.HasExited) {
                [void]$observer.CloseMainWindow()
                if (!$observer.WaitForExit(4000)) { $observer.Kill(); [void]$observer.WaitForExit(3000) }
            }
            $observer.Dispose()
        }
    }
}

try {
    Show-Frames (1..11)
    foreach ($phase in 12..16) { Show-Frames @($phase) }
    Show-Frames @(6) -Stale
} finally {
    $results.ToArray() | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'captures.json') -Encoding UTF8
}
