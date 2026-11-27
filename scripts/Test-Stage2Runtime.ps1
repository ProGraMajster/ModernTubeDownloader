[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$RefuseHttpProxy
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase,System.Drawing
# Share the owned-HWND capture helper used by the native feature tests.
$witness = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-FeaturePerformanceNative.ps1') -Raw
if ($witness -notmatch "(?s)Add-Type -ReferencedAssemblies System.Drawing @'\r?\n(.*?)\r?\n'@") { throw 'Missing capture helper.' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $Matches[1]
$root = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a new evidence directory.' }
if (Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue) { throw 'The app is already running.' }
$evidence = [IO.Directory]::CreateDirectory($EvidenceDirectory).FullName
$profile = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) ('mtd-automation-smoke-stage2-runtime-' + [guid]::NewGuid().ToString('N')))).FullName
$utf8 = New-Object Text.UTF8Encoding($false)
$guiExe = Join-Path $root 'ModernTubeDownloader.exe'
$nativeExe = Join-Path $root 'native-host\ModernTubeDownloader.NativeHost.exe'
$updaterExe = Join-Path $root 'updater\ModernTubeDownloader.Updater.exe'
$records = [Collections.Generic.List[object]]::new()
$ui = [Windows.Automation.AutomationElement]
$tree = [Windows.Automation.TreeScope]::Descendants
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$inventory = [ordered]@{
    windows = (Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber)
    processor = (Get-CimInstance Win32_Processor | Select-Object Name, Architecture)
    elevatedAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    refusedProxy = [bool]$RefuseHttpProxy
    globalFrameworkFolders = @(Get-ChildItem -Path (Join-Path $env:ProgramFiles 'dotnet\shared\*\*') -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
    package = (Get-Item -LiteralPath $guiExe).VersionInfo.ProductVersion
}
$localEndpoint = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$localEndpoint.Start()
$webPort = $localEndpoint.LocalEndpoint.Port
$localEndpoint.Stop()
$rootOverride = [IO.Directory]::CreateDirectory((Join-Path $profile 'empty-dotnet-root')).FullName
function Save-Json([string]$path, $data) {
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $data -Depth 12), $utf8)
}
Save-Json (Join-Path $profile 'settings.json') @{
    language='en'; browserIntegrationEnabled=$false; updates=@{ automaticChecks=$false; policyVersion=1 }
    notifications=@{ enabled=$false }; enableWebRemote=$true
    webRemotePort=$webPort; webRemoteBindAddress='127.0.0.1'; webRemoteRequireAuthentication=$true
    finalOutputDirectory=(Join-Path $profile 'output'); temporaryDirectory=(Join-Path $profile 'temporary')
    startup=@{ closeBehavior=0; showTrayIcon=$false }
}
Save-Json (Join-Path $profile 'experience.json') @{
    schemaVersion=1; existingProfile=$true; setup=2; interfaceChoiceFamily='1.3'
    lastObservedVersion=$inventory.package; lastWhatsNewAcknowledgedVersion=$inventory.package
}

function Start-Owned([string]$exe, [string]$commandLine = '', [switch]$InputPipe) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe, $commandLine)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = Split-Path -Parent $exe
    $start.RedirectStandardInput = [bool]$InputPipe
    $start.EnvironmentVariables['MODERNTUBEDOWNLOADER_DATA_ROOT'] = $profile
    $start.EnvironmentVariables['MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL'] = '1'
    $start.EnvironmentVariables['DOTNET_ROOT'] = $rootOverride
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $rootOverride
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    if ($RefuseHttpProxy) {
        $start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:1'
        $start.EnvironmentVariables['HTTP_PROXY'] = 'http://127.0.0.1:1'
        $start.EnvironmentVariables['NO_PROXY'] = ''
    }
    if ($InputPipe) { $start.CreateNoWindow = $true }
    return [Diagnostics.Process]::Start($start)
}

function Assert-Runtime($child, [string]$exe, [string]$role) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        $child.Refresh()
        if ($child.HasExited) { throw "$role exited before runtime sampling." }
        $loaded = @($child.Modules | Where-Object ModuleName -in 'coreclr.dll', 'hostpolicy.dll')
        if ($loaded.Count -eq 2) { break }
        Start-Sleep -Milliseconds 30
    } while ($deadline.Elapsed.TotalSeconds -lt 10)
    if ($loaded.Count -ne 2) { throw "$role did not load both runtime modules." }
    $source = Split-Path -Parent $exe
    $modules = @($loaded | ForEach-Object {
        if (![string]::Equals($_.FileName, (Join-Path $source $_.ModuleName), [StringComparison]::OrdinalIgnoreCase)) {
            throw "$role loaded a runtime module outside its own application directory."
        }
        if ($_.FileVersionInfo.ProductVersion -notmatch '^11[.,]') { throw "$role did not load .NET 11." }
        [ordered]@{ name=$_.ModuleName; location=$_.FileName; productVersion=$_.FileVersionInfo.ProductVersion }
    })
    $metadata = (Get-Item -LiteralPath $exe).VersionInfo
    $records.Add([ordered]@{
        role=$role; pid=$child.Id; path=$exe; modules=$modules; fileVersion=$metadata.FileVersion
        productVersion=$metadata.ProductVersion
    })
}
function Wait-Window($child, [string]$automationId) {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    do {
        $child.Refresh()
        if ($child.HasExited) { throw 'Owned process exited before showing its expected window.' }
        if ($child.MainWindowHandle -ne [IntPtr]::Zero) {
            try {
                $window = $ui::FromHandle($child.MainWindowHandle)
                if ($window.Current.ProcessId -eq $child.Id -and
                    (!$automationId -or $window.Current.AutomationId -eq $automationId) -and !$window.Current.IsOffscreen) {
                    return $window
                }
            } catch [Windows.Automation.ElementNotAvailableException] {}
        }
        Start-Sleep -Milliseconds 25
    } while ($elapsed.Elapsed.TotalSeconds -lt 20)
    throw "Expected native window was not visible. PID=$($child.Id) automation ID=$automationId"
}
function Save-Window($window, $child, [string]$file) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    [void][PerfWindow]::Capture($handle, $child.Id, (Join-Path $evidence $file))
}
function Wait-Text($window, [string]$line) {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    do {
        $condition = [Windows.Automation.PropertyCondition]::new($ui::NameProperty, $line)
        if ($window.FindFirst($tree, $condition)) { return }
        Start-Sleep -Milliseconds 25
    } while ($elapsed.Elapsed.TotalSeconds -lt 8)
    throw "The installer observer did not show '$line'."
}
function Close-Owned($child) {
    if ($null -eq $child) { return }
    if (!$child.HasExited) {
        [void]$child.CloseMainWindow()
        if (!$child.WaitForExit(2500)) { $child.Kill(); [void]$child.WaitForExit(4000) }
    }
    $child.Dispose()
}

$gui = $null
$native = $null
try {
    $gui = Start-Owned $guiExe
    $main = Wait-Window $gui 'MainWindow'
    Save-Window $main $gui 'gui.png'
    Assert-Runtime $gui $guiExe 'GUI'
    $web = [Net.WebRequest]::Create(('http://127.0.0.1:{0}/api/auth-mode' -f $webPort))
    $web.Proxy = $null
    $web.KeepAlive = $false
    $web.Timeout = 4000
    $response = $web.GetResponse()
    try {
        $reader = [IO.StreamReader]::new($response.GetResponseStream())
        try { $mode = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if (!$mode.authenticationRequired -or $mode.desktopLanguage -ne 'en') { throw 'Unexpected response from packaged Web Remote.' }
        $inventory.aspNetCoreLoopback = [int]$response.StatusCode
    } finally { $response.Dispose() }
    $guiHandle = [IntPtr]$main.Current.NativeWindowHandle
    if ($main.Current.ProcessId -ne $gui.Id) { throw 'The owned main window disappeared before shutdown.' }
    $inventory.guiClosePosted = [PerfWindow]::PostMessage($guiHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
    if (!$inventory.guiClosePosted) { throw 'Could not post the close request to the owned main window.' }
    $ownerCondition = [Windows.Automation.PropertyCondition]::new($ui::ProcessIdProperty, $gui.Id)
    $exitCondition = [Windows.Automation.PropertyCondition]::new($ui::AutomationIdProperty, 'ShutdownExit')
    $confirmClock = [Diagnostics.Stopwatch]::StartNew()
    $exit = $null
    do {
        $ownedWindows = $ui::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $ownerCondition)
        foreach ($window in $ownedWindows) {
            $exit = $window.FindFirst($tree, $exitCondition)
            if ($exit -and $exit.Current.IsEnabled -and $exit.Current.ProcessId -eq $gui.Id) { break }
            $exit = $null
        }
        if ($exit) { break }
        Start-Sleep -Milliseconds 25
    } while ($confirmClock.Elapsed.TotalSeconds -lt 8)
    if (!$exit) { throw 'The running loopback Web Remote did not expose the owned Exit confirmation.' }
    $exit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $inventory.guiShutdownConfirmed = $true
    if (!$gui.WaitForExit(12000)) { throw 'The owned main window did not close after confirmation.' }
    $inventory.guiExitCode = $gui.ExitCode
    if ($gui.ExitCode -ne 0) { throw 'The isolated GUI returned an error.' }
    $native = Start-Owned $nativeExe -InputPipe
    Assert-Runtime $native $nativeExe 'NativeHost'
    $native.StandardInput.Close()
    if (!$native.WaitForExit(10000)) { throw 'NativeHost did not finish on input EOF.' }

    foreach ($scenario in @('complete','close-observer','kill-observer','kill-worker','bad-checksum')) {
        $trial = [IO.Directory]::CreateDirectory((Join-Path $evidence $scenario)).FullName
        $copy = [IO.Directory]::CreateDirectory((Join-Path $trial 'runner')).FullName
        Copy-Item -Path ((Split-Path -Parent $updaterExe) + '\*') -Destination $copy -Recurse
        $workerExe = Join-Path $copy ([IO.Path]::GetFileName($updaterExe))
        $installation = [IO.Directory]::CreateDirectory((Join-Path $trial 'installation')).FullName
        $payload = [IO.Directory]::CreateDirectory((Join-Path $trial 'payload')).FullName
        $name = 'ModernTubeDownloader.exe'
        [IO.File]::WriteAllText((Join-Path $installation $name), 'original synthetic file')
        [IO.File]::WriteAllText((Join-Path $payload $name), 'replacement synthetic file')
        [IO.File]::WriteAllText((Join-Path $installation 'keep.user'), 'untouched synthetic file')
        $archive = Join-Path $trial 'candidate.zip'
        Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
        $hash = if ($scenario -eq 'bad-checksum') { '0' * 64 } else { (Get-FileHash -LiteralPath $archive).Hash }
        $parent = Start-Owned "$env:windir\System32\cmd.exe" '/d /q /c "pause >nul"' -InputPipe
        $worker = $null
        $observer = $null
        try {
            $request = Join-Path $copy 'request.json'
            Save-Json $request @{
                Installation=$installation; Archive=$archive; Sha256=$hash; ParentProcessId=$parent.Id
                ParentStartUtcTicks=$parent.StartTime.ToUniversalTime().Ticks; Restart=$false
                OperationId=[guid]::NewGuid(); ProgressLanguage='en'
            }
            $worker = Start-Owned $workerExe ('--request "' + $request + '"')
            Assert-Runtime $worker $workerExe 'Updater'
            $wait = [Diagnostics.Stopwatch]::StartNew()
            do {
                $candidate = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($worker.Id)" | Where-Object {
                    $_.ExecutablePath -eq $workerExe -and $_.CommandLine -match '--progress'
                })
                if ($candidate.Count -eq 1) {
                    $observer = [Diagnostics.Process]::GetProcessById($candidate[0].ProcessId)
                    if ($observer.Path -ne $workerExe) { throw 'Unexpected observer executable.' }
                    break
                }
                Start-Sleep -Milliseconds 50
            } while ($wait.Elapsed.TotalSeconds -lt 8)
            if ($null -eq $observer) { throw 'The owned observer process did not start.' }
            Assert-Runtime $observer $workerExe 'Presentation'
            $window = Wait-Window $observer ''
            Wait-Text $window 'Waiting for the application to close…'
            Save-Window $window $observer ($scenario + '-waiting.png')
            if (Test-Path -LiteralPath ($request + '.result.json')) { throw 'Updater passed the parent gate prematurely.' }
            if ($scenario -eq 'close-observer') {
                if (!$observer.CloseMainWindow() -or !$observer.WaitForExit(5000)) { throw 'Observer did not close.' }
            }
            if ($scenario -eq 'kill-observer') { $observer.Kill(); [void]$observer.WaitForExit(4000) }
            if ($scenario -eq 'kill-worker') {
                $worker.Kill()
                if (!$worker.WaitForExit(4000)) { throw 'The owned worker could not be stopped.' }
                Wait-Text $window 'The installation result could not be confirmed.'
                Save-Window $window $observer 'worker-disconnected.png'
                if (Test-Path -LiteralPath ($request + '.result.json')) { throw 'A killed waiting worker produced a terminal result.' }
                continue
            }
            $parent.StandardInput.WriteLine('x')
            $parent.StandardInput.Close()
            if (!$parent.WaitForExit(5000)) { throw 'The synthetic parent did not exit.' }
            if (!$worker.WaitForExit(15000)) { throw 'The installer did not complete.' }
            $result = Get-Content -LiteralPath ($request + '.result.json') -Raw | ConvertFrom-Json
            $expected = $scenario -ne 'bad-checksum'
            $expectedContent = if ($expected) { 'replacement synthetic file' } else { 'original synthetic file' }
            if ($result.success -ne $expected -or
                [IO.File]::ReadAllText((Join-Path $installation $name)) -ne $expectedContent -or
                [IO.File]::ReadAllText((Join-Path $installation 'keep.user')) -ne 'untouched synthetic file') {
                throw "Unexpected installation result in $scenario."
            }
            if ($scenario -in 'complete','bad-checksum') {
                $terminal = if ($expected) { 'Update installed.' } else { 'The update failed.' }
                Wait-Text $window $terminal
                Save-Window $window $observer ($scenario + '-terminal.png')
            }
        } finally {
            Close-Owned $observer
            Close-Owned $worker
            Close-Owned $parent
        }
    }
    $inventory.result = 'PASS'
} catch {
    $inventory.result = 'FAIL'; $inventory.error = $_.Exception.Message
    throw
} finally {
    Close-Owned $native
    Close-Owned $gui
    $inventory.components = $records.ToArray()
    Save-Json (Join-Path $evidence 'runtime-evidence.json') $inventory
}
