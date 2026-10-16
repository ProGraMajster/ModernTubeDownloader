param(
    [string]$ApplicationPath,
    [switch]$PublishStartupSmoke,
    [switch]$PublishedUiSmoke,
    [switch]$PublishedDashboardSmoke,
    [switch]$PublishedNoUpdate,
    [switch]$TraySmoke,
    [switch]$UpdateSmoke,
    [switch]$OtherSmoke,
    [switch]$BatchSmoke,
    [switch]$HotfixSmoke,
    [switch]$RealBatchSmoke,
    [switch]$TextScaleSmoke,
    [switch]$AppearanceSmoke,
    [string]$SampleUrl = 'https://example.test/watch/sample123',
    [switch]$PlaylistSmoke,
    [switch]$PreviewOnly,
    [switch]$ClearPreviewSmoke,
    [switch]$ScrollProbe,
    [switch]$CaptureVisual,
    [switch]$DashboardSmoke,
    [switch]$DashboardSwitchStress,
    [switch]$DashboardFixture,
    [switch]$DashboardVisualFixture,
    [switch]$DashboardEmptyFixture,
    [switch]$ClassicUiRecovery,
    [switch]$QualityDropdownSmoke,
    [ValidateRange(1, 100)][int]$QualityDropdownPasses = 20,
    [switch]$RangeSmoke,
    [switch]$QueueScrollRepro,
    [switch]$SettingsScrollRepro,
    [switch]$CookieSettingsSmoke,
    [switch]$NotificationsSmoke,
    [ValidateRange(0, 300)][int]$NotificationManualHoldSeconds = 0,
    [switch]$WebRemotePairingSmoke,
    [ValidateRange(0, 900)][int]$RemoteManualHoldSeconds = 0,
    [switch]$ExternalProtocolSmoke,
    [switch]$CrashStress,
    [switch]$LiveSmoke,
    [switch]$LiveReplayNavigationSmoke,
    [switch]$RealLiveSmoke,
    [switch]$SupportedSourcesSmoke,
    [switch]$RealSourcesSmoke,
    [switch]$SourcesFromSettings,
    [switch]$NavigateCheckedSource,
    [string[]]$SourceCheckUrls = @('https://example.test/watch/sample123'),
    [string]$YtDlpPath,
    [string]$FfmpegPath,
    [string]$FfprobePath,
    [string]$DenoPath,
    [ValidateRange(5, 60)][int]$LiveCaptureSeconds = 15,
    [ValidateRange(1, 200)][int]$CrashStressPasses = 15,
    [string]$ModernFormsNextRoot = '.mfn-master-worktree',
    [ValidateSet('en', 'pl')][string]$Language = 'en',
    [ValidateSet('Dark', 'Light')][string]$Theme = 'Dark',
    [ValidateRange(80, 200)][int]$AppTextScale = 100,
    [ValidateSet('Classic', 'ModernDashboard')][string]$InterfaceMode = 'Classic',
    [ValidateRange(0, 4000)][int]$WindowWidth = 0,
    [ValidateRange(0, 3000)][int]$WindowHeight = 0,
    [ValidateRange(0, 500)][int]$PlaylistCount = 12
)

$ErrorActionPreference = 'Stop'
if ($ClassicUiRecovery) { $DashboardSmoke = $true }
if ($DashboardVisualFixture) { $DashboardFixture = $true; $DashboardSmoke = $true }
if ($DashboardEmptyFixture) { $DashboardSmoke = $true }
if ($DashboardSmoke) { $InterfaceMode = 'ModernDashboard' }
if (Get-Process -Name ModernTubeDownloader -ErrorAction SilentlyContinue) {
    throw 'Close the existing MTD instance (including other QA sessions) before starting an isolated smoke. No process was stopped.'
}
if ($RealLiveSmoke) {
    $LiveSmoke = $true
    foreach ($requiredTool in @($YtDlpPath, $FfmpegPath, $FfprobePath, $DenoPath)) {
        if (-not $requiredTool -or -not (Test-Path -LiteralPath $requiredTool -PathType Leaf)) {
            throw 'RealLiveSmoke requires valid YtDlpPath, FfmpegPath, FfprobePath and DenoPath.'
        }
    }
}
if ($RealSourcesSmoke) {
    $SupportedSourcesSmoke = $true
    foreach ($requiredTool in @($YtDlpPath, $FfmpegPath, $FfprobePath, $DenoPath)) {
        if (-not $requiredTool -or -not (Test-Path -LiteralPath $requiredTool -PathType Leaf)) {
            throw 'RealSourcesSmoke requires valid YtDlpPath, FfmpegPath, FfprobePath and DenoPath.'
        }
    }
}
if ($LiveReplayNavigationSmoke) { $LiveSmoke = $true; $SampleUrl = 'https://example.test/live/replay' }
if ($PlaylistSmoke -and $SampleUrl -eq 'https://example.test/watch/sample123') {
    $SampleUrl = 'https://example.test/playlist/synthetic'
}
$repo = Split-Path -Parent $PSScriptRoot
[xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw
$appFramework = $buildProperties.SelectSingleNode('/Project/PropertyGroup/ModernTubeDownloaderTargetFramework').InnerText.Trim()
$app = Join-Path $repo "ModernTubeDownloader/bin/Debug/$appFramework/ModernTubeDownloader.exe"
if ($ApplicationPath) { $app = [IO.Path]::GetFullPath($ApplicationPath) }
$cli = Join-Path $ModernFormsNextRoot 'ModernFormsNext.Automation.Cli/bin/Debug/net10.0-windows/ModernFormsNext.Automation.Cli.dll'
if (-not [System.IO.Path]::IsPathRooted($cli)) { $cli = Join-Path $repo $cli }
$fake = Join-Path $repo 'ModernTubeDownloader.FakeTool/bin/Debug/net10.0'
foreach ($required in @($app, $cli, (Join-Path $fake 'ModernTubeDownloader.FakeTool.exe'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Build Debug application, MFN Automation CLI and FakeTool first: $required" }
}

function Invoke-AutomationCli {
    param([string[]]$CliArguments, [string]$InputValue)
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($cli)
    foreach ($argument in $CliArguments) { $start.ArgumentList.Add($argument) }
    $start.ArgumentList.Add('--json')
    $client = [System.Diagnostics.Process]::Start($start)
    try {
        if ($PSBoundParameters.ContainsKey('InputValue')) { $client.StandardInput.Write($InputValue) }
        $client.StandardInput.Close()
        $stdoutTask = $client.StandardOutput.ReadToEndAsync()
        $stderrTask = $client.StandardError.ReadToEndAsync()
        if (-not $client.WaitForExit(65000)) { throw "Automation CLI timed out: $($CliArguments -join ' ')" }
        $output = $stdoutTask.GetAwaiter().GetResult()
        $errorText = $stderrTask.GetAwaiter().GetResult()
        if ($client.ExitCode -ne 0) {
            $excerpt = if ($output.Length -gt 1000) { $output.Substring(0, 1000) + '…' } else { $output }
            throw "Automation CLI exit $($client.ExitCode): $($CliArguments -join ' ') $excerpt $errorText"
        }
        return $output | ConvertFrom-Json
    }
    finally { $client.Dispose() }
}

function Find-Node([string]$id, [string]$rootId) {
    $result = Invoke-AutomationCli -CliArguments @('find', '--pid', "$($script:automationProcess.Id)", '--root', $rootId, '--automation-id', $id)
    if ($result.error -ne 'None' -or -not $result.value) { throw "AutomationId not found: $id" }
    return $result.value
}

function Invoke-NodeAction($node, [string]$rootId, [string]$action, [string]$value) {
    $arguments = @('action', '--pid', "$($script:automationProcess.Id)", '--root', $rootId,
        '--session', $node.handle.sessionId, '--node', $node.handle.runtimeId, '--action', $action)
    if ($PSBoundParameters.ContainsKey('value')) {
        $arguments += '--value-stdin'
        return Invoke-AutomationCli -CliArguments $arguments -InputValue $value
    }
    return Invoke-AutomationCli -CliArguments $arguments
}

function Wait-Node([string]$id, [string]$rootId, [string]$condition = 'NodeExists') {
    $result = Invoke-AutomationCli -CliArguments @('wait', '--pid', "$($script:automationProcess.Id)", '--root', $rootId,
        '--condition', $condition, '--automation-id', $id, '--timeout-ms', '30000')
    if ($result.status -ne 'Satisfied') { throw "Wait failed for $id ($condition): $($result.status)" }
    return $result
}

function Wait-Bridge([int]$processId, [bool]$expected) {
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $list = @(Invoke-AutomationCli -CliArguments @('list'))
        $found = @($list | Where-Object { $_.processId -eq $processId }).Count -gt 0
        if ($found -eq $expected) { return }
    } while ($deadline.Elapsed.TotalSeconds -lt 30)
    throw "Bridge discovery expected=$expected for PID=$processId"
}

function Wait-MainWindow([System.Diagnostics.Process]$process) {
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Application PID=$($process.Id) exited before its main window appeared." }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { return }
    } while ($deadline.Elapsed.TotalSeconds -lt 30)
    throw "Application PID=$($process.Id) did not expose a main window."
}

function Capture-OwnedBitmap($graphics, [bool]$dialog = $false) {
    # Capture only this test's HWND; foreground desktop pixels are not evidence of the app.
    $dc = $graphics.GetHdc()
    try {
        if (-not [MtdAutomationWindowCloser]::CaptureOwnWindow($script:automationProcess.Id,
            $script:automationProcess.MainWindowHandle, $dialog, $dc)) { throw 'Own application HWND capture failed.' }
    }
    finally { $graphics.ReleaseHdc($dc) }
}

if (-not ('MtdAutomationWindowCloser' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class MtdAutomationWindowCloser {
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    private struct NativePoint { public int X; public int Y; }
    public static int[] OwnWindowSize(int processId, IntPtr hwnd) {
        GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != processId || !IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out NativeRect rect))
            throw new InvalidOperationException("Cannot measure the owned capture HWND.");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Owned capture HWND has invalid dimensions.");
        return new[] { width, height };
    }
    private struct GuiThreadInfo {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public NativeRect CaretBounds;
    }
    public static long[] NativeFocusSnapshot(IntPtr mainWindow) {
        uint threadId = GetWindowThreadProcessId(mainWindow, out uint owner);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf(typeof(GuiThreadInfo)) };
        if (!GetGUIThreadInfo(threadId, ref info)) throw new InvalidOperationException("Cannot inspect own GUI thread focus.");
        return new[] { GetForegroundWindow().ToInt64(), info.Active.ToInt64(), info.Focus.ToInt64(), info.Capture.ToInt64() };
    }
    public static IntPtr FindOwnPopup(int processId, IntPtr mainWindow, out int width, out int height) {
        IntPtr target = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || hwnd == mainWindow || !IsWindowVisible(hwnd)) return true;
            if ((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80) == 0) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length != 0) return true;
            target = hwnd; return false;
        }, IntPtr.Zero);
        width = height = 0;
        if (target != IntPtr.Zero && GetWindowRect(target, out NativeRect rect)) {
            width = rect.Right - rect.Left; height = rect.Bottom - rect.Top;
        }
        return target;
    }
    public static bool CaptureOwnPopup(int processId, IntPtr popup, IntPtr dc) {
        GetWindowThreadProcessId(popup, out uint owner);
        return owner == processId && IsWindowVisible(popup) && PrintWindow(popup, dc, 2);
    }
    public static bool CaptureOwnWindow(int processId, IntPtr mainWindow, bool details, IntPtr dc) {
        IntPtr target = mainWindow;
        if (details) {
            target = IntPtr.Zero;
            EnumWindows((hwnd, data) => {
                GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != processId || hwnd == mainWindow || !IsWindowVisible(hwnd)) return true;
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                if (title.Length == 0) return true;
                target = hwnd; return false;
            }, IntPtr.Zero);
        }
        return target != IntPtr.Zero && PrintWindow(target, dc, 2);
    }
    public static bool CloseDetails(int processId, IntPtr mainWindow) {
        IntPtr details = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || hwnd == mainWindow || !IsWindowVisible(hwnd)) return true;
            var text = new StringBuilder(512);
            GetWindowText(hwnd, text, text.Capacity);
            if (text.Length == 0) return true;
            details = hwnd;
            return false;
        }, IntPtr.Zero);
        return details != IntPtr.Zero && PostMessage(details, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }
    public static bool FocusDialog(int processId, IntPtr mainWindow) {
        IntPtr dialog = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || hwnd == mainWindow || !IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            dialog = hwnd;
            return false;
        }, IntPtr.Zero);
        if (dialog == IntPtr.Zero || !SetForegroundWindow(dialog)) return false;
        Thread.Sleep(300);
        return GetForegroundWindow() == dialog;
    }
    public static bool SendWheel(IntPtr window, int screenX, int screenY, int delta) {
        long wheel = ((long)(ushort)(short)delta) << 16;
        long point = (ushort)screenX | ((long)(ushort)screenY << 16);
        return PostMessage(window, 0x020A, (IntPtr)wheel, (IntPtr)point);
    }
    public static bool SendDialogWheel(int processId, IntPtr mainWindow, int screenX, int screenY, int delta) {
        IntPtr dialog = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || hwnd == mainWindow || !IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            dialog = hwnd; return false;
        }, IntPtr.Zero);
        return dialog != IntPtr.Zero && SendWheel(dialog, screenX, screenY, delta);
    }
    public static bool ResizeDialog(int processId, IntPtr mainWindow, int width, int height) {
        IntPtr dialog = IntPtr.Zero;
        EnumWindows((hwnd, data) => {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || hwnd == mainWindow || !IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            dialog = hwnd; return false;
        }, IntPtr.Zero);
        return dialog != IntPtr.Zero && ResizeWindow(dialog, width, height);
    }
    public static bool SendMouse(IntPtr window, uint message, int screenX, int screenY, bool buttonDown) {
        var point = new NativePoint { X = screenX, Y = screenY };
        if (!ScreenToClient(window, ref point)) return false;
        long packed = (ushort)point.X | ((long)(ushort)point.Y << 16);
        return PostMessage(window, message, buttonDown ? (IntPtr)1 : IntPtr.Zero, (IntPtr)packed);
    }
    public static bool PlaceWindow(IntPtr window, int width, int height) => SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, 0x0004);
    public static bool ResizeWindow(IntPtr window, int width, int height) => SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, 0x0006);
    private static Thread mouseFlood;
    private static volatile bool floodRunning;
    public static void StartMouseFlood(IntPtr window, int screenX, int screenY, int width, int height) {
        if (mouseFlood != null) throw new InvalidOperationException("Mouse flood already active.");
        floodRunning = true;
        mouseFlood = new Thread(() => {
            int tick = 0;
            while (floodRunning) {
                SendMouse(window, 0x0200, screenX + 12 + (tick % Math.Max(1, width - 24)),
                    screenY + 12 + (tick % Math.Max(1, height - 24)), false);
                tick++;
                Thread.Sleep(2);
            }
        }) { IsBackground = true };
        mouseFlood.Start();
    }
    public static void StopMouseFlood() {
        floodRunning = false;
        mouseFlood?.Join(5000);
        mouseFlood = null;
    }
}
'@
}

$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('mtd-automation-smoke-' + [guid]::NewGuid().ToString('N'))
$toolRoot = Join-Path $dataRoot 'fake-tools'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $dataRoot 'Tools') -Force | Out-Null
foreach ($extension in @('exe', 'dll', 'deps.json', 'runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $fake "ModernTubeDownloader.FakeTool.$extension") -Destination $toolRoot
}
foreach ($name in @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe')) {
    Copy-Item -LiteralPath (Join-Path $fake 'ModernTubeDownloader.FakeTool.exe') -Destination (Join-Path $toolRoot $name)
}
$settings = @{
    Updates = @{ AutomaticChecks = $false }
    Language = $Language; ThemeMode = if ($Theme -eq 'Light') { 1 } else { 2 }
    TemporaryDirectory = (Join-Path $dataRoot 'temp'); FinalOutputDirectory = (Join-Path $dataRoot 'output')
    QueuePaused = $true; UseCustomYtDlp = $true; CustomYtDlpPath = (Join-Path $toolRoot 'yt-dlp.exe')
    UseCustomFfmpeg = $true; CustomFfmpegPath = (Join-Path $toolRoot 'ffmpeg.exe')
    UseCustomDeno = $true; CustomDenoPath = (Join-Path $toolRoot 'deno.exe')
}
if ($HotfixSmoke) {
    $settings.TemporaryDirectory = Join-Path $dataRoot 'simulate-merge-diskfull'
    $settings.MaxSimultaneousDownloads = 1
    $settings.PreservePartialDownloadsOnFailure = $false
    $settings.BrowserIntegrationEnabled = $false
}
if ($TraySmoke) {
    $settings.Startup = @{ ShowTrayIcon = $true; CloseBehavior = 2; MinimizeBehavior = 1; FirstHideNotice = $false }
    $settings.BrowserIntegrationEnabled = $false # Do not mutate the owner's protocol registration for this test.
}
if ($WebRemotePairingSmoke) {
    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    $remotePort = $portProbe.LocalEndpoint.Port
    $portProbe.Stop()
    $settings.EnableWebRemote = $false
    $settings.WebRemoteRequireAuthentication = $true
    $settings.WebRemoteBindAddress = '127.0.0.1'
    $settings.WebRemotePort = $remotePort
}
if ($RealLiveSmoke -or $RealSourcesSmoke -or $RealBatchSmoke) {
    $settings.CustomYtDlpPath = $YtDlpPath
    $settings.CustomFfmpegPath = $FfmpegPath
    $settings.CustomFfprobePath = $FfprobePath
    $settings.CustomDenoPath = $DenoPath
    $settings.BrowserIntegrationEnabled = $false
    $settings.TemporaryDirectory = Join-Path $dataRoot 'temp'
    $settings.FinalOutputDirectory = Join-Path $dataRoot 'output'
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Encoding utf8
if ($DashboardEmptyFixture) { '[]' | Set-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Encoding utf8 }
@{ LastToolUpdateCheck = [DateTimeOffset]::UtcNow } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'Tools/state.json') -Encoding utf8
if ($QueueScrollRepro -or $CrashStress -or $DashboardFixture) {
    $seededQueue = @()
    $seededHistory = @()
    $fixtureCount = if ($DashboardFixture) { 100 } else { 15 }
    for ($index = 1; $index -le $fixtureCount; $index++) {
        $itemId = [Guid]::NewGuid()
        $seededQueue += @{
            Id = $itemId; SourceUrl = "https://example.test/watch/repro$index"; VideoId = "repro$index"
            Title = "Synthetic QA item $index — Łódź 日本語"; Channel = 'Synthetic channel'
            QualityPresetId = 'best'; Status = if ($CrashStress) { 0 } else { 6 }
            ProgressPercent = if ($CrashStress) { 0 } else { 100 }
            StatusMessageKey = if ($CrashStress) { 'Queue.Detail.Preparing' } else { 'Queue.Detail.Completed' }
            RawMetadataJson = '{}'; FinalFile = (Join-Path $dataRoot "video-$index.mp4")
        }
        $seededHistory += @{
            QueueItemId = $itemId; Title = "Recording-like completed item $index"
            SourceUrl = "https://example.test/watch/repro$index"; VideoId = "repro$index"
            Channel = 'Synthetic channel'; Quality = '1080p'; FinalPath = (Join-Path $dataRoot "video-$index.mp4")
            CompletedAt = [DateTimeOffset]::UtcNow.AddMinutes(-$index); Status = 'Completed'
        }
    }
    if ($DashboardVisualFixture) { & (Join-Path $PSScriptRoot 'New-DashboardVisualFixture.ps1') -DataRoot $dataRoot -Queue $seededQueue -History $seededHistory }
    $seededQueue | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Encoding utf8
    $seededHistory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'history.json') -Encoding utf8
}
$previousDataRoot = $env:MODERNTUBEDOWNLOADER_DATA_ROOT
$previousSkipProtocol = $env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL
$previousAllSettings = $env:MODERNTUBEDOWNLOADER_QA_ALL_SETTINGS
$previousPlaylistCount = $env:MTD_FAKE_PLAYLIST_COUNT
$env:MODERNTUBEDOWNLOADER_DATA_ROOT = $dataRoot
$env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL = '1'
if ($SettingsScrollRepro) { $env:MODERNTUBEDOWNLOADER_QA_ALL_SETTINGS = '1' }
if ($AppTextScale -ne 100 -or $InterfaceMode -eq 'ModernDashboard') {
    @{ schemaVersion = 2; interfaceMode = if ($InterfaceMode -eq 'ModernDashboard') { 1 } else { 0 }; global = @{ theme = if ($Theme -eq 'Light') { 1 } else { 2 } }; typography = @{ appTextScale = $AppTextScale } } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dataRoot 'appearance.json') -Encoding utf8
}
if ($PlaylistSmoke) { $env:MTD_FAKE_PLAYLIST_COUNT = "$PlaylistCount" }
$normalProcess = $null
$script:automationProcess = $null
$protocolSnapshot = @()
if ($PublishStartupSmoke) {
    # Release intentionally has no DEBUG skip-registration seam. Preserve the exact
    # public URI association around this owned package smoke; never export credentials.
    foreach ($subkey in @('Software\Classes\moderntubedownloader',
        'Software\Classes\moderntubedownloader\DefaultIcon',
        'Software\Classes\moderntubedownloader\shell\open\command')) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subkey)
        $values = @()
        if ($key) {
            foreach ($name in $key.GetValueNames()) {
                $values += @{ Name = $name; Value = $key.GetValue($name); Kind = $key.GetValueKind($name) }
            }
            $key.Dispose()
        }
        $protocolSnapshot += @{ Path = $subkey; Existed = $null -ne $key; Values = $values }
    }
}
try {
    if ($TraySmoke) {
        $settings.Startup.CloseBehavior = 0
        $settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Encoding utf8
        $normalProcess = Start-Process -FilePath $app -ArgumentList @('--autostart','--start-hidden') -PassThru -WindowStyle Hidden
        if (-not $normalProcess.WaitForInputIdle(30000)) { throw 'Hidden autostart did not reach the UI loop.' }
        Start-Sleep -Milliseconds 700
        $normalProcess.Refresh()
        if ($normalProcess.HasExited -or $normalProcess.MainWindowHandle -ne [IntPtr]::Zero) { throw 'Autostart showed a main window or exited.' }
        $secondary = Start-Process -FilePath $app -PassThru -WindowStyle Hidden
        try {
            if (-not $secondary.WaitForExit(10000) -or $secondary.ExitCode -ne 0) { throw 'Hidden autostart manual restore failed.' }
        } finally { $secondary.Dispose() }
        Wait-MainWindow $normalProcess
        if (-not $normalProcess.CloseMainWindow() -or -not $normalProcess.WaitForExit(15000)) { throw 'Autostart instance did not exit cleanly.' }
        $settings.Startup.CloseBehavior = 2
        $settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Encoding utf8
        Write-Output 'Hidden autostart native smoke PASS: no visible main window, same-instance manual restore, clean exit.'
    }
    else {
    $startupMeasurement = [Diagnostics.Stopwatch]::StartNew()
    $normalArguments = if ($ClassicUiRecovery) { @('--classic-ui') } else { @() }
    $normalProcess = if ($normalArguments.Count) { Start-Process -FilePath $app -ArgumentList $normalArguments -PassThru -WindowStyle Hidden } else { Start-Process -FilePath $app -PassThru -WindowStyle Hidden }
    if (-not $normalProcess.WaitForInputIdle(30000) -or $normalProcess.HasExited) { throw 'Normal startup did not reach the UI loop.' }
    Wait-MainWindow $normalProcess
    Write-Output "Indicative startup to native HWND/input-idle: $([Math]::Round($startupMeasurement.Elapsed.TotalMilliseconds,1)) ms (not tools ready or cold-PC qualification)."
    Wait-Bridge $normalProcess.Id $false
    if ($PublishStartupSmoke) {
        $readyWatch = [Diagnostics.Stopwatch]::StartNew()
        do {
            $startupLog = (Get-ChildItem -LiteralPath (Join-Path $dataRoot 'logs') -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
            $validatedTools = @(@('yt-dlp', 'ffmpeg', 'ffprobe', 'deno') | Where-Object { $startupLog -match "Process ended: $([regex]::Escape($_)).exe, exit=0" })
            $normalProcess.Refresh()
            if ($normalProcess.HasExited) { throw 'Published application exited during startup.' }
            if ($validatedTools.Count -eq 4) { break }
            Start-Sleep -Milliseconds 100
        } while ($readyWatch.Elapsed.TotalSeconds -lt 30)
        if ($validatedTools.Count -ne 4) { throw 'Published app did not validate custom QA tools.' }
        if ($PublishedUiSmoke) {
            $stableVersion = $buildProperties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
            # UIA requires the desktop .NET Framework host. Invoke our reviewed local
            # client as a normal inline command; do not override execution policy.
            $clientSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-PublishedUI.ps1') -Raw
            $publishedCommand = '& { ' + $clientSource + ' } -ProcessId ' + $normalProcess.Id +
                " -DataRoot '" + $dataRoot.Replace("'", "''") + "' -ExpectedVersion '" + $stableVersion.Replace("'", "''") + "'"
            if ($PublishedNoUpdate) { $publishedCommand += ' -CheckNoUpdate' }
            if ($PublishedDashboardSmoke) { $publishedCommand += ' -CheckBothInterfaces' }
            & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command $publishedCommand
            if ($LASTEXITCODE -ne 0) { throw 'Published native UIA smoke failed.' }
        }
    }
    if ($CrashStress) {
        Start-Sleep -Milliseconds 1200
        $normalProcess.Refresh()
        if ($normalProcess.HasExited) { throw 'Normal startup exited during the ready transition.' }
    }
    if (-not $normalProcess.CloseMainWindow() -or -not $normalProcess.WaitForExit(30000)) { throw 'Normal startup did not close cleanly.' }
    Write-Output 'Normal startup: no Automation bridge; clean shutdown.'
    if ($PublishStartupSmoke) { Write-Output "Published startup smoke PASS (isolated profile=$dataRoot; custom QA tools, not real provisioning/download)."; return }
    }

    if ($DashboardVisualFixture) {
        # Normal startup uses completed entries, so its no-work shutdown is still qualified.
        # Pending/error presentation cases are installed only for the owned Automation process.
        $seededQueue[1].Status = 0; $seededQueue[1].ProgressPercent = 0; $seededQueue[1].StatusMessageKey = 'Queue.Detail.Queued'
        $seededQueue[2].Status = 7; $seededQueue[2].ProgressPercent = 0
        $seededQueue[3].Status = 13; $seededQueue[3].ProgressPercent = 0
        $seededQueue[4].Status = 8; $seededQueue[4].ProgressPercent = 0
        $seededQueue | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Encoding utf8
    }
    $automationArguments = @('--automation'); if ($ClassicUiRecovery) { $automationArguments += '--classic-ui' }
    $script:automationProcess = Start-Process -FilePath $app -ArgumentList $automationArguments -PassThru -WindowStyle Hidden
    Wait-Bridge $script:automationProcess.Id $true
    Wait-MainWindow $script:automationProcess
    if ($WindowWidth -gt 0 -and $WindowHeight -gt 0) {
        $placed = if ($DashboardSmoke) { [MtdAutomationWindowCloser]::PlaceWindow($script:automationProcess.MainWindowHandle, $WindowWidth, $WindowHeight) } else { [MtdAutomationWindowCloser]::ResizeWindow($script:automationProcess.MainWindowHandle, $WindowWidth, $WindowHeight) }
        if (-not $placed) {
            throw 'Could not resize the application window.'
        }
    }
    if ($DashboardSmoke) { Write-Output "Native window DPI: $([MtdAutomationWindowCloser]::GetDpiForWindow($script:automationProcess.MainWindowHandle)); app text scale: $AppTextScale%." }
    $roots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
    $mainRoot = $roots.value[0].rootId
    $tree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '8')
    if (-not @($tree.nodes | Where-Object { $_.node.automationId -eq 'MainWindow' }).Count) { throw 'MainWindow absent from tree.' }
    if ($DashboardSmoke) {
        function Capture-Dashboard([string]$name) {
            if (-not $CaptureVisual) { return }
            Start-Sleep -Milliseconds 250
            $size = [MtdAutomationWindowCloser]::OwnWindowSize($script:automationProcess.Id, $script:automationProcess.MainWindowHandle)
            Add-Type -AssemblyName System.Drawing
            $bitmap = [Drawing.Bitmap]::new($size[0], $size[1]); $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { Capture-OwnedBitmap $graphics; $bitmap.Save((Join-Path $dataRoot ($name + '.png'))) }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
            $snapshot = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--depth','12')
            $snapshot | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath (Join-Path $dataRoot ($name + '-tree.json')) -Encoding utf8
        }
        if ($ClassicUiRecovery) {
            [void](Find-Node 'NavDownloads' $mainRoot)
            $recoveryTree = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--depth','10')
            if (@($recoveryTree.nodes | Where-Object { $_.node.automationId -eq 'NavBatch' }).Count) { throw 'Classic recovery opened the dashboard.' }
            Capture-Dashboard 'classic-recovery'
            if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(15000)) { throw 'Classic recovery did not exit.' }
            $saved = Get-Content -LiteralPath (Join-Path $dataRoot 'appearance.json') -Raw | ConvertFrom-Json
            if ($saved.interfaceMode -ne 1) { throw 'Recovery overwrote the persisted interface preference.' }
            Write-Output "Classic recovery native smoke PASS; saved Modern preference retained; profile=$dataRoot"
            return
        }
        Wait-Node 'DashboardQuickQueue' $mainRoot 'Enabled' | Out-Null
        Start-Sleep -Milliseconds 1000
        if ($DashboardFixture) {
            Invoke-NodeAction (Find-Node ('QueueItem-' + $seededQueue[0].Id.ToString('N')) $mainRoot) $mainRoot 'Select' | Out-Null
            $paged = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--depth','12')
            if (@($paged.nodes | Where-Object { $_.node.automationId -like 'QueueItem-*' }).Count -ne 40) { throw 'Dashboard queue did not bound 100 entries to 40 rows.' }
        }
        if (-not $DashboardFixture -and -not $DashboardEmptyFixture) {
            Invoke-NodeAction (Find-Node 'DownloadUrlInput' $mainRoot) $mainRoot 'SetValue' $SampleUrl | Out-Null
            Invoke-NodeAction (Find-Node 'DashboardQuickQueue' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Wait-Node 'DashboardQuickQueue' $mainRoot 'Enabled' | Out-Null
            $queueClock = [Diagnostics.Stopwatch]::StartNew(); $prepared = @()
            do {
                if (Test-Path -LiteralPath (Join-Path $dataRoot 'queue.json')) { $prepared = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json) }
                if ($prepared.Count -eq 1) { break }; Start-Sleep -Milliseconds 100
            } while ($queueClock.Elapsed.TotalSeconds -lt 15)
            if ($prepared.Count -ne 1) { throw 'Dashboard QuickQueue did not prepare exactly one canonical queue item.' }
            $id = ([guid]$prepared[0].id).ToString('N')
            Invoke-NodeAction (Find-Node ('QueueItem-' + $id) $mainRoot) $mainRoot 'Select' | Out-Null
            Write-Output 'Dashboard QuickQueue/selection PASS using the isolated FakeTool source, not a real source download.'
        }
        Capture-Dashboard 'dashboard-downloads'
        foreach ($page in @('Live','Other','Batch','History','Settings','Help','About')) {
            Invoke-NodeAction (Find-Node ('Nav' + $page) $mainRoot) $mainRoot 'Invoke' | Out-Null
            Start-Sleep -Milliseconds 250
            if ($page -eq 'History' -and $DashboardFixture) { Invoke-NodeAction (Find-Node ('HistoryItem-' + $seededQueue[0].Id.ToString('N') + '-vod') $mainRoot) $mainRoot 'Select' | Out-Null }
            Capture-Dashboard ('dashboard-' + $page.ToLowerInvariant())
        }
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryPersonalization' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Capture-Dashboard 'dashboard-personalization'
        $queueBeforeSwitch = (Get-FileHash -LiteralPath (Join-Path $dataRoot 'queue.json') -Algorithm SHA256).Hash
        $script:automationProcess.Refresh(); $memoryBefore = $script:automationProcess.PrivateMemorySize64
        $passes = if ($DashboardSwitchStress) { 20 } else { 1 }
        # Page controls survive chrome swaps, so these semantic handles must remain valid.
        $classicChoice = Find-Node 'InterfaceClassic' $mainRoot
        $modernChoice = Find-Node 'InterfaceModernDashboard' $mainRoot
        Invoke-NodeAction $classicChoice $mainRoot 'ScrollIntoView' | Out-Null
        for ($i = 0; $i -lt $passes; $i++) {
            Invoke-NodeAction $classicChoice $mainRoot 'Invoke' | Out-Null
            [void](Find-Node 'NavDownloads' $mainRoot)
            if ($i -eq 0) { Capture-Dashboard 'classic-after-switch' }
            Invoke-NodeAction $modernChoice $mainRoot 'Invoke' | Out-Null
            [void](Find-Node 'NavBatch' $mainRoot)
        }
        $runtimeLog = (Get-ChildItem -LiteralPath (Join-Path $dataRoot 'logs') -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
        if ([regex]::Matches($runtimeLog, 'Native tray icon registered\.').Count -ne 2) { throw 'Shell switches registered an extra tray icon (two owned QA processes expected).' }
        $queueAfterSwitch = (Get-FileHash -LiteralPath (Join-Path $dataRoot 'queue.json') -Algorithm SHA256).Hash
        if ($queueBeforeSwitch -ne $queueAfterSwitch) { throw 'Shell switches changed the persisted queue.' }
        $script:automationProcess.Refresh(); Write-Output "Indicative private bytes before/after switches: $memoryBefore / $($script:automationProcess.PrivateMemorySize64); not a GC-normalized leak proof."
        Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
        if (-not $script:automationProcess.CloseMainWindow()) { throw 'Dashboard close request failed.' }
        if (-not $script:automationProcess.WaitForExit(2000)) {
            $closing = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
            $choice = @($closing.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
            if (-not $choice) { throw 'Dashboard work-aware close confirmation missing.' }
            Invoke-NodeAction (Find-Node 'ShutdownExit' $choice.rootId) $choice.rootId 'Invoke' | Out-Null
            if (-not $script:automationProcess.WaitForExit(15000)) { throw 'Dashboard did not close cleanly.' }
        }
        $profile = Get-Content -LiteralPath (Join-Path $dataRoot 'appearance.json') -Raw | ConvertFrom-Json
        if ($profile.interfaceMode -ne 1 -or $profile.schemaVersion -ne 2) { throw 'Dashboard interface preference was not persisted.' }
        Write-Output "Dashboard native smoke PASS: all routes; $passes Classic/Modern switches; $Language/$Theme/$AppTextScale%; profile=$dataRoot"
        return
    }
    if ($TraySmoke) {
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryStartup' $mainRoot) $mainRoot 'Invoke' | Out-Null
        foreach ($id in @('TrayEnabled','TrayMinimizeBehavior','TrayCloseBehavior','WindowsAutostartEnabled','StartupApply')) {
            [void](Find-Node $id $mainRoot)
        }
        $trayLog = (Get-ChildItem -LiteralPath (Join-Path $dataRoot 'logs') -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
        if ($trayLog -notmatch 'Native tray icon registered\.') { throw 'Native tray icon registration was not confirmed.' }
        $script:automationProcess.CloseMainWindow() | Out-Null
        $choiceRoots = $null
        $choiceTimer = [Diagnostics.Stopwatch]::StartNew()
        do {
            $choiceRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
            $choiceRoot = @($choiceRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        } while (-not $choiceRoot -and $choiceTimer.Elapsed.TotalSeconds -lt 15)
        if (-not $choiceRoot) { throw 'Close confirmation was not exposed.' }
        Invoke-NodeAction (Find-Node 'ShutdownHide' $choiceRoot.rootId) $choiceRoot.rootId 'Invoke' | Out-Null
        Start-Sleep -Milliseconds 300
        $script:automationProcess.Refresh()
        if ($script:automationProcess.HasExited -or $script:automationProcess.MainWindowHandle -ne [IntPtr]::Zero) {
            throw 'Hide-to-tray did not keep an accessible background instance without a visible main window.'
        }
        $secondary = Start-Process -FilePath $app -PassThru -WindowStyle Hidden
        try {
            if (-not $secondary.WaitForExit(10000) -or $secondary.ExitCode -ne 0) { throw 'Manual secondary launch did not forward restore.' }
        } finally { $secondary.Dispose() }
        Wait-MainWindow $script:automationProcess
        [void](Find-Node 'NavSettings' $mainRoot)
        $script:automationProcess.CloseMainWindow() | Out-Null
        $choiceRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
        $choiceRoot = @($choiceRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        if (-not $choiceRoot) { throw 'Reopened close confirmation missing.' }
        Invoke-NodeAction (Find-Node 'ShutdownExit' $choiceRoot.rootId) $choiceRoot.rootId 'Invoke' | Out-Null
        if (-not $script:automationProcess.WaitForExit(15000) -or $script:automationProcess.ExitCode -ne 0) { throw 'Graceful tray shutdown failed.' }
        Write-Output "Tray native smoke PASS ($Language/$Theme): native icon, Ask/Hide, same-instance manual restore, graceful Exit. No HKCU Run writes. Profile=$dataRoot"
        return
    }
    if ($UpdateSmoke) {
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryUpdates' $mainRoot) $mainRoot 'Invoke' | Out-Null
        foreach ($id in @('ApplicationUpdatesSection','ApplicationUpdateFrequency','ApplicationUpdateMode','ApplicationUpdateStatus','ApplicationUpdateCheck')) {
            [void](Find-Node $id $mainRoot)
        }
        Invoke-NodeAction (Find-Node 'ApplicationUpdateCheck' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
        Invoke-NodeAction (Find-Node 'ApplicationUpdateCheck' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Wait-Node 'ApplicationUpdateCheck' $mainRoot 'Enabled' | Out-Null
        $updateTree = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--depth','10')
        if (($updateTree | ConvertTo-Json -Depth 40) -match '\[Updates\.') { throw 'Unlocalized update text in native UI.' }
        Write-Output "Application updates native smoke PASS ($Language/$Theme): controls, manual Stable check, localized status. No download/install of a public release. Profile=$dataRoot"
        return
    }
    if ($TextScaleSmoke -or $AppearanceSmoke) {
        if ($AppearanceSmoke) {
            Invoke-NodeAction (Find-Node 'DownloadUrlInput' $mainRoot) $mainRoot 'SetValue' $SampleUrl | Out-Null
            Invoke-NodeAction (Find-Node 'AnalyzeButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Wait-Node 'DetailsButton' $mainRoot 'Enabled' | Out-Null
        }
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryPersonalization' $mainRoot) $mainRoot 'Invoke' | Out-Null
        foreach ($percent in @(125,150,175,200,100)) {
            $textScaleNode = Find-Node 'AppearanceTextScale' $mainRoot
            Invoke-NodeAction $textScaleNode $mainRoot 'ScrollIntoView' | Out-Null
            $numeric = Find-Node 'AppearanceTextScale' $mainRoot
            Invoke-AutomationCli -CliArguments @('action','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,'--session',$numeric.handle.sessionId,'--node',"$($numeric.handle.runtimeId)",'--action','SetValue','--number',"$percent") | Out-Null
            $deadline = [Diagnostics.Stopwatch]::StartNew()
            do { $profile = Get-Content -LiteralPath (Join-Path $dataRoot 'appearance.json') -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json } while ($profile.typography.appTextScale -ne $percent -and $deadline.Elapsed.TotalSeconds -lt 15)
            if ($profile.typography.appTextScale -ne $percent) { throw 'Live text scale was not saved.' }
            if ($AppearanceSmoke) {
                foreach ($id in @('NavDownloads','NavLive','NavHistory','NavOther','NavSettings')) {
                    Invoke-NodeAction (Find-Node $id $mainRoot) $mainRoot 'Invoke' | Out-Null
                    Start-Sleep -Milliseconds 350
                    # Size the PrintWindow destination from the same owned native HWND.
                    # Semantic peer bounds are not a reliable bitmap allocation rectangle.
                    $captureSize = [MtdAutomationWindowCloser]::OwnWindowSize($script:automationProcess.Id, $script:automationProcess.MainWindowHandle)
                    Add-Type -AssemblyName System.Drawing
                    $picture = [Drawing.Bitmap]::new($captureSize[0], $captureSize[1])
                    $drawing = [Drawing.Graphics]::FromImage($picture)
                    try { Capture-OwnedBitmap $drawing; $picture.Save((Join-Path $dataRoot "$id-$percent.png")) } finally { $drawing.Dispose(); $picture.Dispose() }
                }
                if ($percent -in @(150,200)) {
                    Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
                    Invoke-NodeAction (Find-Node 'DetailsButton' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
                    Invoke-NodeAction (Find-Node 'DetailsButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
                    $roots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
                    $detailsRoot = @($roots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
                    if (-not $detailsRoot) { throw 'Appearance Details root missing.' }
                    foreach ($tab in @('Overview','Metadata','Formats','Subtitles','Chapters')) { Invoke-NodeAction (Find-Node "Details${tab}Tab" $detailsRoot.rootId) $detailsRoot.rootId 'Select' | Out-Null }
                    $bounds = (Find-Node 'DetailsWindow' $detailsRoot.rootId).bounds
                    $picture = [Drawing.Bitmap]::new([int]$bounds.width,[int]$bounds.height); $drawing = [Drawing.Graphics]::FromImage($picture)
                    try { Capture-OwnedBitmap $drawing $true; $picture.Save((Join-Path $dataRoot "Details-$percent.png")) } finally { $drawing.Dispose(); $picture.Dispose() }
                    [MtdAutomationWindowCloser]::CloseDetails($script:automationProcess.Id,$script:automationProcess.MainWindowHandle) | Out-Null
                    Start-Sleep -Milliseconds 300
                    Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
                }
                Invoke-NodeAction (Find-Node 'SettingsCategoryPersonalization' $mainRoot) $mainRoot 'Invoke' | Out-Null
            }
        }
        foreach ($id in @('NavDownloads','NavLive','NavHistory','NavOther','NavSettings')) { Invoke-NodeAction (Find-Node $id $mainRoot) $mainRoot 'Invoke' | Out-Null }
        if ($AppearanceSmoke) {
            foreach ($preset in @('Compact','Large','Minimal','Default')) {
                $id = 'AppearancePreset' + $preset
                Invoke-NodeAction (Find-Node $id $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
                Invoke-NodeAction (Find-Node $id $mainRoot) $mainRoot 'Invoke' | Out-Null
                Start-Sleep -Milliseconds 350
            }
            Invoke-NodeAction (Find-Node 'AppearanceColorAccent' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
            Invoke-NodeAction (Find-Node 'AppearanceColorAccent' $mainRoot) $mainRoot 'SetValue' '#3388CC' | Out-Null
            Start-Sleep -Milliseconds 500
            $saved = Get-Content -LiteralPath (Join-Path $dataRoot 'appearance.json') -Raw | ConvertFrom-Json
            if ($saved.global.theme -ne 3 -or $saved.global.colors.Accent -ne '#3388CC') { throw 'Custom color preview/save failed.' }
        }
        Write-Output "Text scale native smoke PASS ($Language/$Theme): live 100/125/150/175/200/reset, persisted, page navigation. This is not physical DPI or visual-clipping qualification. Profile=$dataRoot"
        return
    }
    if ($HotfixSmoke) {
        Invoke-NodeAction (Find-Node 'NavOther' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'OtherOpenBatch' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $batchRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
        $batchRoot = @($batchRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        if (-not $batchRoot) { throw 'Hotfix Batch window missing.' }
        $bad = Join-Path $dataRoot 'invalid.json'
        '{"wrongRoot":true}' | Set-Content -LiteralPath $bad -Encoding utf8
        Invoke-NodeAction (Find-Node 'BatchFilePath' $batchRoot.rootId) $batchRoot.rootId 'SetValue' $bad | Out-Null
        Invoke-NodeAction (Find-Node 'BatchImport' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        do { $errorNode = Find-Node 'BatchStatus' $batchRoot.rootId } while ($errorNode.name -notmatch 'JSON' -and $deadline.Elapsed.TotalSeconds -lt 10)
        if ($errorNode.name -notmatch 'JSON' -or $errorNode.name -match 'JsonException|JsonElement|List<') { throw 'Batch localized JSON error missing or technical.' }
        Start-Sleep -Milliseconds 1200
        if ((Find-Node 'BatchStatus' $batchRoot.rootId).name -notmatch 'JSON') { throw 'Batch timer erased the import error.' }
        Invoke-NodeAction (Find-Node 'BatchExamples' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        $exampleRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
        $exampleRoot = @($exampleRoots.value | Where-Object { $_.rootId -ne $mainRoot -and $_.rootId -ne $batchRoot.rootId }) | Select-Object -First 1
        if (-not $exampleRoot) { throw 'Batch examples dialog missing.' }
        if ((Find-Node 'BatchExamplesText' $exampleRoot.rootId).name -notmatch '"urls"') { throw 'Batch examples are incomplete.' }
        Invoke-NodeAction (Find-Node 'BatchExamplesClose' $exampleRoot.rootId) $exampleRoot.rootId 'Invoke' | Out-Null
        foreach ($json in @('{"url":"https://example.test/watch/single"}', '{"urls":["https://example.test/watch/sample123?id=one","https://example.test/watch/sample456?id=two"]}')) {
            $valid = Join-Path $dataRoot 'valid.json'
            $json | Set-Content -LiteralPath $valid -Encoding utf8
            Invoke-NodeAction (Find-Node 'BatchFilePath' $batchRoot.rootId) $batchRoot.rootId 'SetValue' $valid | Out-Null
            Invoke-NodeAction (Find-Node 'BatchImport' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
            Wait-Node 'BatchEntryStatus1' $batchRoot.rootId | Out-Null
        }
        Wait-Node 'BatchEntryStatus2' $batchRoot.rootId | Out-Null
        Invoke-NodeAction (Find-Node 'BatchAnalyze' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        Wait-Node 'BatchAdd' $batchRoot.rootId 'Enabled' | Out-Null
        $deadline.Restart()
        # Wait on rendered status, not a guessed persistence filename.
        do { $ready = (Find-Node 'BatchEntryStatus2' $batchRoot.rootId).name } while ($ready -notmatch 'Ready|Gotow' -and $deadline.Elapsed.TotalSeconds -lt 15)
        if ($ready -notmatch 'Ready|Gotow') { throw 'Batch metadata did not finish.' }
        Invoke-NodeAction (Find-Node 'BatchAdd' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        Wait-Node 'BatchAdd' $batchRoot.rootId 'Enabled' | Out-Null
        if ($InterfaceMode -ne 'ModernDashboard') { Invoke-NodeAction (Find-Node 'BatchClose' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null }
        Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'QueuePauseButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Wait-Node 'QueueStorageWarning' $mainRoot | Out-Null
        $deadline.Restart()
        do {
            $queue = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
        } while (@($queue | Where-Object failureCategory -eq 'Disk').Count -ne 1 -and $deadline.Elapsed.TotalSeconds -lt 15)
        if (@($queue | Where-Object failureCategory -eq 'Disk').Count -ne 1 -or @($queue | Where-Object status -eq 0).Count -ne 1) { throw 'Disk-full native flow did not leave one failed and one pending item.' }
        $parts = @(Get-ChildItem -LiteralPath $settings.TemporaryDirectory -Recurse -File)
        if (-not @($parts | Where-Object Name -like '*.partial.mp4').Count) { throw 'Native storage failure lost partials.' }
        $warning = Find-Node 'QueueStorageWarning' $mainRoot
        if ($warning.name -notmatch 'disk|dysku') { throw 'Localized storage banner missing.' }
        # Safely make only this isolated QA checkpoint unwritable as a file. No
        # disk filling, ACL changes, user queue or real application data is involved.
        $qaQueue = Join-Path $dataRoot 'queue.json'
        $qaQueueBackup = Join-Path $dataRoot 'queue-before-checkpoint-fault.json'
        Move-Item -LiteralPath $qaQueue -Destination $qaQueueBackup
        New-Item -ItemType Directory -Path $qaQueue | Out-Null
        try {
            Invoke-NodeAction (Find-Node 'QueuePauseButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
            $deadline.Restart()
            do { $warning = Find-Node 'QueueStorageWarning' $mainRoot } while ($warning.name -notmatch 'memory|pamięci' -and $deadline.Elapsed.TotalSeconds -lt 10)
            if ($warning.name -notmatch 'memory|pamięci') { throw 'Failed Resume checkpoint did not expose persistence degradation.' }
            if ((Find-Node 'QueuePauseButton' $mainRoot).name -notmatch 'Resume|Wznów') { throw 'Failed checkpoint reopened queue admission.' }
        }
        finally {
            # The deliberately created target is an empty directory, never recurse.
            if (Test-Path -LiteralPath $qaQueue -PathType Container) { Remove-Item -LiteralPath $qaQueue }
            Move-Item -LiteralPath $qaQueueBackup -Destination $qaQueue
        }
        if ($CaptureVisual) {
            Add-Type -AssemblyName System.Drawing
            $bounds = (Find-Node 'MainWindow' $mainRoot).bounds
            $picture = [Drawing.Bitmap]::new([int]$bounds.width, [int]$bounds.height)
            $drawing = [Drawing.Graphics]::FromImage($picture)
            try { Capture-OwnedBitmap $drawing; $capture = Join-Path $dataRoot 'hotfix-storage-native.png'; $picture.Save($capture); Write-Output "Hotfix storage capture: $capture" }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
        # Retire this owned QA instance through the real close-confirmation path;
        # a forced finally cleanup is not evidence of a successful clean close.
        $script:automationProcess.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 400
        if (-not $script:automationProcess.HasExited) {
            $closeRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
            $closeRoot = @($closeRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
            if (-not $closeRoot) { throw 'Close confirmation missing with pending work.' }
            Invoke-NodeAction (Find-Node 'ShutdownExit' $closeRoot.rootId) $closeRoot.rootId 'Invoke' | Out-Null
        }
        if (-not $script:automationProcess.WaitForExit(30000)) { throw 'Hotfix QA did not close cleanly.' }
        Write-Output "Hotfix native smoke PASS ($Language/$Theme): normal startup, valid single/envelope JSON, safe persistent JSON error, examples, canonical handoff, ENOSPC pause, pending job blocked, partials retained, failed Resume checkpoint stays paused with unsaved-state warning, clean close. Profile=$dataRoot"
        return
    }
    if ($BatchSmoke -or $RealBatchSmoke) {
        $batchFile = Join-Path $dataRoot 'batch-smoke.txt'
        if ($RealBatchSmoke) {
            foreach ($tool in @($YtDlpPath,$FfmpegPath,$FfprobePath,$DenoPath)) { if (-not $tool -or -not (Test-Path -LiteralPath $tool)) { throw 'RealBatchSmoke requires real installed tools.' } }
            # 20 public addresses / 4 identities, including normalized duplicate aliases from three sources.
            $addresses = @('https://www.youtube.com/watch?v=eRsGyueVLvQ','https://vimeo.com/1084537','https://www.youtube.com/watch?v=brLbwajyGNI','https://archive.org/download/BigBuckBunny_328/BigBuckBunny_512kb.mp4')
            $fixture = @(); foreach ($index in 0..4) { foreach ($address in $addresses) { $fixture += ($address + $(if ($index -eq 0) { '' } else { "#mtd-qa-$index" })) } }
            $fixture | Set-Content -LiteralPath $batchFile -Encoding utf8
        } else { @('https://example.test/watch/sample123', 'https://example.test/watch/sample456', 'https://example.test/watch/sample123') | Set-Content -LiteralPath $batchFile -Encoding utf8 }
        Invoke-NodeAction (Find-Node 'NavOther' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'OtherOpenBatch' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $batchRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
        $batchRoot = @($batchRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        if ($InterfaceMode -eq 'ModernDashboard') { $batchRoot = @{ rootId = $mainRoot }; [void](Find-Node 'BatchView' $mainRoot) }
        if (-not $batchRoot) { throw 'Batch window root missing.' }
        Invoke-NodeAction (Find-Node 'BatchFilePath' $batchRoot.rootId) $batchRoot.rootId 'SetValue' $batchFile | Out-Null
        Invoke-NodeAction (Find-Node 'BatchImport' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        Wait-Node 'BatchEntryStatus3' $batchRoot.rootId | Out-Null
        if ($RealBatchSmoke) { foreach ($ordinal in @(1,2)) { Invoke-NodeAction (Find-Node "BatchSelect$ordinal" $batchRoot.rootId) $batchRoot.rootId 'Toggle' | Out-Null } }
        Invoke-NodeAction (Find-Node 'BatchAnalyze' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        Wait-Node 'BatchAnalyze' $batchRoot.rootId 'Enabled' | Out-Null
        if ($RealBatchSmoke) { Invoke-NodeAction (Find-Node 'BatchClearSelection' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null }
        $batchTree = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$batchRoot.rootId,'--depth','12')
        if (($batchTree | ConvertTo-Json -Depth 40) -match '\[Batch\.') { throw 'Unlocalized batch text.' }
        Invoke-NodeAction (Find-Node 'BatchSelect1' $batchRoot.rootId) $batchRoot.rootId 'Toggle' | Out-Null
        Invoke-NodeAction (Find-Node 'BatchAdd' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        Wait-Node 'BatchAdd' $batchRoot.rootId 'Enabled' | Out-Null
        if ($CaptureVisual) {
            $bounds = if ($InterfaceMode -eq 'ModernDashboard') { (Find-Node 'MainWindow' $mainRoot).bounds } else { (Find-Node 'BatchWindow' $batchRoot.rootId).bounds }
            Add-Type -AssemblyName System.Drawing
            $picture = [Drawing.Bitmap]::new([int]$bounds.width,[int]$bounds.height); $drawing = [Drawing.Graphics]::FromImage($picture)
            try { Capture-OwnedBitmap $drawing ($InterfaceMode -ne 'ModernDashboard'); $capture = Join-Path $dataRoot 'batch-native.png'; $picture.Save($capture); Write-Output "Batch native capture: $capture" }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
        Invoke-NodeAction (Find-Node 'BatchCancel' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null
        if ($InterfaceMode -ne 'ModernDashboard') { Invoke-NodeAction (Find-Node 'BatchClose' $batchRoot.rootId) $batchRoot.rootId 'Invoke' | Out-Null }
        Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $savedQueue = Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json
        if (@($savedQueue).Count -ne 1) { throw 'Batch selected handoff did not add exactly one canonical queue item.' }
        Write-Output "Batch native smoke PASS ($Language/$Theme, real=$RealBatchSmoke): import, duplicate, analyze, selected handoff into paused canonical queue, cancel, close. Profile=$dataRoot"
        return
    }
    if ($OtherSmoke) {
        Invoke-NodeAction (Find-Node 'NavOther' $mainRoot) $mainRoot 'Invoke' | Out-Null
        [void](Find-Node 'OtherPageTitle' $mainRoot)
        Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
        [void](Find-Node 'DownloadUrlInput' $mainRoot)
        Write-Output "Other hub navigation native smoke PASS ($Language/$Theme). Tool registration is validated with Batch in stage D. Profile=$dataRoot"
        return
    }
    if ($SupportedSourcesSmoke) {
        if ($SourcesFromSettings) {
            Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Invoke-NodeAction (Find-Node 'SettingsSupportedSourcesButton' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
            Invoke-NodeAction (Find-Node 'SettingsSupportedSourcesButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        }
        else { Invoke-NodeAction (Find-Node 'SupportedSourcesButton' $mainRoot) $mainRoot 'Invoke' | Out-Null }
        $sourceRoot = $null; $sourceDeadline = [Diagnostics.Stopwatch]::StartNew()
        do {
            $sourceRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
            $sourceRoot = @($sourceRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        } while (-not $sourceRoot -and $sourceDeadline.Elapsed.TotalSeconds -lt 30)
        if (-not $sourceRoot) { throw 'Supported sources root did not appear.' }
        if ($WindowWidth -gt 0 -and $WindowHeight -gt 0) {
            if (-not [MtdAutomationWindowCloser]::ResizeDialog($script:automationProcess.Id,$script:automationProcess.MainWindowHandle,$WindowWidth,$WindowHeight)) {
                throw 'Could not resize the Supported services window.'
            }
        }
        foreach ($id in @('SupportedSourcesWindow','SupportedSourcesSearch','SupportedSourcesList','SupportedSourcesTechnicalToggle','SupportedSourcesUrlInput','SupportedSourcesCheckButton','SupportedSourcesResult')) {
            Wait-Node $id $sourceRoot.rootId | Out-Null
        }
        function Inspect-SourceNode([string]$id) {
            $node = Find-Node $id $sourceRoot.rootId
            $detail = Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$sourceRoot.rootId,'--session',$node.handle.sessionId,'--node',$node.handle.runtimeId)
            return $detail.value
        }
        Wait-Node 'SupportedSourcesSearch' $sourceRoot.rootId 'Enabled' | Out-Null
        Write-Output ((Inspect-SourceNode 'SupportedSourcesVersion').name)
        Wait-Node 'SupportedSourceRow0' $sourceRoot.rootId | Out-Null
        $beforeScroll = Inspect-SourceNode 'SupportedSourcesList'
        if (-not $beforeScroll.scrollInfo.vertical.isScrollable) { throw 'Source list is not scrollable.' }
        Invoke-NodeAction (Find-Node 'SupportedSourceRow0' $sourceRoot.rootId) $sourceRoot.rootId 'Focus' | Out-Null
        $scrollBounds = $beforeScroll.bounds
        if (-not [MtdAutomationWindowCloser]::SendDialogWheel($script:automationProcess.Id,$script:automationProcess.MainWindowHandle,
            [int]($scrollBounds.x + $scrollBounds.width / 2),[int]($scrollBounds.y + 30),-120)) { throw 'Source dialog wheel event failed.' }
        $scrollTimer = [Diagnostics.Stopwatch]::StartNew()
        do { $afterScroll = Inspect-SourceNode 'SupportedSourcesList' }
        while ($afterScroll.scrollInfo.vertical.offset -eq $beforeScroll.scrollInfo.vertical.offset -and $scrollTimer.Elapsed.TotalSeconds -lt 5)
        if ($afterScroll.scrollInfo.vertical.offset -le $beforeScroll.scrollInfo.vertical.offset) { throw 'Source list wheel did not move.' }
        Invoke-NodeAction (Find-Node 'SupportedSourcesTechnicalToggle' $sourceRoot.rootId) $sourceRoot.rootId 'Toggle' | Out-Null
        if ($RealSourcesSmoke) {
            Invoke-NodeAction (Wait-Node 'SupportedSourcesNext' $sourceRoot.rootId 'Enabled').snapshot $sourceRoot.rootId 'Invoke' | Out-Null
            Invoke-NodeAction (Wait-Node 'SupportedSourcesPrevious' $sourceRoot.rootId 'Enabled').snapshot $sourceRoot.rootId 'Invoke' | Out-Null
        }
        Invoke-NodeAction (Find-Node 'SupportedSourcesSearch' $sourceRoot.rootId) $sourceRoot.rootId 'SetValue' 'Twitch' | Out-Null
        if ((Inspect-SourceNode 'SupportedSourceRow0').name -notmatch '(?i)twitch') { throw 'Source search did not find Twitch.' }
        Invoke-NodeAction (Find-Node 'SupportedSourcesSearch' $sourceRoot.rootId) $sourceRoot.rootId 'SetValue' '' | Out-Null
        foreach ($sourceUrl in $SourceCheckUrls) {
            Invoke-NodeAction (Find-Node 'SupportedSourcesUrlInput' $sourceRoot.rootId) $sourceRoot.rootId 'SetValue' $sourceUrl | Out-Null
            Invoke-NodeAction (Wait-Node 'SupportedSourcesCheckButton' $sourceRoot.rootId 'Enabled').snapshot $sourceRoot.rootId 'Invoke' | Out-Null
            Wait-Node 'SupportedSourcesCheckButton' $sourceRoot.rootId 'Enabled' | Out-Null
            $sourceResult = (Inspect-SourceNode 'SupportedSourcesResult').value
            if (-not $sourceResult -or $sourceResult -match 'Analyzing source metadata|Analizowanie metadanych') { throw 'No completed source check result.' }
            if (-not $RealSourcesSmoke) {
                $expected = switch -Regex ($sourceUrl) {
                    '/live/active' { 'Trwająca transmisja LIVE|Active live stream'; break }
                    '/live/upcoming' { 'Zaplanowana transmisja|Upcoming broadcast'; break }
                    '/live/replay' { 'Zakończona transmisja|Completed replay'; break }
                    '/playlist/' { 'Playlista|Playlist'; break }
                    '/source/generic' { 'generic'; break }
                    '/source/unsupported' { 'Nie rozpoznano|No supported source'; break }
                    '/source/private|/source/auth' { 'uwierzytelnienie|Authentication'; break }
                    default { 'Film / materiał|Video / media' }
                }
                if ($sourceResult -notmatch $expected) { throw "Incorrect classification for $sourceUrl" }
            }
            Write-Output ([pscustomobject]@{ Url=$sourceUrl; Result=$sourceResult } | ConvertTo-Json -Compress)
        }
        if ($CaptureVisual) {
            $bounds = (Find-Node 'SupportedSourcesWindow' $sourceRoot.rootId).bounds
            Add-Type -AssemblyName System.Drawing
            $sourcePicture = [Drawing.Bitmap]::new([int]$bounds.width,[int]$bounds.height)
            $sourceGraphics = [Drawing.Graphics]::FromImage($sourcePicture)
            try {
                $dc = $sourceGraphics.GetHdc()
                try { if (-not [MtdAutomationWindowCloser]::CaptureOwnWindow($script:automationProcess.Id,$script:automationProcess.MainWindowHandle,$true,$dc)) { throw 'Source window capture failed.' } }
                finally { $sourceGraphics.ReleaseHdc($dc) }
                $sourcePicture.Save((Join-Path $dataRoot 'supported-sources.png'),[Drawing.Imaging.ImageFormat]::Png)
            } finally { $sourceGraphics.Dispose(); $sourcePicture.Dispose() }
        }
        if ($NavigateCheckedSource) {
            Invoke-NodeAction (Wait-Node 'SupportedSourcesOpenButton' $sourceRoot.rootId 'Enabled').snapshot $sourceRoot.rootId 'Invoke' | Out-Null
            if ($SourceCheckUrls[-1] -match '/live/active|/live/upcoming|twitch.tv/wildlifecam') {
                Wait-Node 'LiveStartButton' $mainRoot 'Enabled' | Out-Null
                Wait-Node 'LiveAnalysisSummary' $mainRoot | Out-Null
            }
            else {
                $summaryNode = (Wait-Node 'AnalyzedSourceSummary' $mainRoot).snapshot
                if (-not $summaryNode.name) { throw 'Opened Downloads has no analyzed source identity.' }
                $queueAction = Find-Node 'AddToQueueButton' $mainRoot
                Write-Output "Desktop source preview: $($summaryNode.name); queue action states: $($queueAction.states)"
            }
        }
        else { Invoke-NodeAction (Find-Node 'SupportedSourcesCloseButton' $sourceRoot.rootId) $sourceRoot.rootId 'Invoke' | Out-Null }
        $queueFile = Join-Path $dataRoot 'queue.json'
        $liveFile = Join-Path $dataRoot 'live-sessions.json'
        if ((Test-Path $queueFile) -and @((Get-Content -LiteralPath $queueFile -Raw | ConvertFrom-Json) | Write-Output).Count -ne 0) { throw 'Source check added a VOD queue item.' }
        if ((Test-Path $liveFile) -and @((Get-Content -LiteralPath $liveFile -Raw | ConvertFrom-Json) | Write-Output).Count -ne 0) { throw 'Source check started LIVE.' }
        # Reopen the same singleton catalog: caching must survive dialog disposal.
        if ($SourcesFromSettings -or $NavigateCheckedSource) {
            Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
        }
        Invoke-NodeAction (Find-Node 'SupportedSourcesButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $reopenTimer = [Diagnostics.Stopwatch]::StartNew()
        do {
            $reopenRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
            $reopenRoot = @($reopenRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        } while (-not $reopenRoot -and $reopenTimer.Elapsed.TotalSeconds -lt 30)
        if (-not $reopenRoot) { throw 'Supported services did not reopen.' }
        Wait-Node 'SupportedSourcesSearch' $reopenRoot.rootId 'Enabled' | Out-Null
        Invoke-NodeAction (Find-Node 'SupportedSourcesCloseButton' $reopenRoot.rootId) $reopenRoot.rootId 'Invoke' | Out-Null
        $catalogLog = Join-Path $dataRoot "logs/ModernTubeDownloader-$([DateTime]::Now.ToString('yyyyMMdd')).log"
        $catalogStarts = @(Select-String -LiteralPath $catalogLog -Pattern 'Starting process:.*--list-extractors').Count
        if ($catalogStarts -ne 1) { throw "Expected one catalog process pair across two opens; got $catalogStarts list commands." }
        Write-Output "Supported sources native smoke PASS ($Language/$Theme; profile: $dataRoot)"
        return
    }
    if ($CrashStress) {
        function Move-ReproMouse($node, [int]$count) {
            $bounds = $node.bounds
            for ($tick = 0; $tick -lt $count; $tick++) {
                $x = [int]($bounds.x + 12 + ($tick % [Math]::Max(1, [int]$bounds.width - 24)))
                $y = [int]($bounds.y + 12 + ($tick % [Math]::Max(1, [int]$bounds.height - 24)))
                if (-not [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle,
                    0x0200, $x, $y, $false)) { throw 'Could not post WM_MOUSEMOVE.' }
            }
        }
        $navDownloads = Find-Node 'NavDownloads' $mainRoot
        $navHistory = Find-Node 'NavHistory' $mainRoot
        $navSettings = Find-Node 'NavSettings' $mainRoot
        # Startup's ready overlay is intentionally brief; let it dismiss before caching view handles.
        Start-Sleep -Milliseconds 1200
        $queueNode = Find-Node 'QueueList' $mainRoot
        Invoke-NodeAction (Find-Node 'QueuePauseButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction $navHistory $mainRoot 'Invoke' | Out-Null
        $historyNode = Find-Node 'HistoryList' $mainRoot
        Invoke-NodeAction $navSettings $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryWebRemote' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $settingsNode = Find-Node 'SettingsScroll' $mainRoot
        $remoteToggle = Find-Node 'WebRemoteEnable' $mainRoot
        $saveButton = Find-Node 'SettingsSaveButton' $mainRoot
        $settingsBounds = $settingsNode.bounds
        [MtdAutomationWindowCloser]::StartMouseFlood($script:automationProcess.MainWindowHandle,
            [int]$settingsBounds.x, [int]$settingsBounds.y, [int]$settingsBounds.width, [int]$settingsBounds.height)
        try {
            for ($pass = 1; $pass -le $CrashStressPasses; $pass++) {
                Invoke-NodeAction $navSettings $mainRoot 'Invoke' | Out-Null
                Invoke-NodeAction $remoteToggle $mainRoot 'Toggle' | Out-Null
                Invoke-NodeAction $saveButton $mainRoot 'Invoke' | Out-Null
                Move-ReproMouse $settingsNode 40
                Invoke-NodeAction $navDownloads $mainRoot 'Invoke' | Out-Null
                Move-ReproMouse $queueNode 40
                Invoke-NodeAction $navHistory $mainRoot 'Invoke' | Out-Null
                Move-ReproMouse $historyNode 40
                $script:automationProcess.Refresh()
                if ($script:automationProcess.HasExited) { throw "Application crashed during stress pass $pass." }
            }
        }
        finally {
            [MtdAutomationWindowCloser]::StopMouseFlood()
        }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
            throw 'Automation process did not close cleanly after crash stress.'
        }
        Write-Output "Crash sequence stress PASS: $CrashStressPasses Settings/Downloads/History cycles with concurrent WM_MOUSEMOVE (profile: $dataRoot)"
        return
    }
    if ($QueueScrollRepro) {
        $firstQueueId = 'QueueItem-' + $seededQueue[0].Id.ToString('N')
        Wait-Node $firstQueueId $mainRoot | Out-Null
        function Get-ReproState([string]$listId, [string]$itemPrefix, [string]$phase) {
            $listNode = Find-Node $listId $mainRoot
            $details = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
            $treeState = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '10')
            $items = @($treeState.nodes | Where-Object { $_.node.automationId -like "$itemPrefix*" })
            $bounds = $details.value.bounds
            $visible = @($items | Where-Object {
                $_.node.bounds.y -lt ($bounds.y + $bounds.height) -and
                ($_.node.bounds.y + $_.node.bounds.height) -gt $bounds.y
            }).Count
            $firstY = if ($items.Count) { $items[0].node.bounds.y } else { 'none' }
            $lastY = if ($items.Count) { $items[-1].node.bounds.y } else { 'none' }
            $axis = $details.value.scrollInfo.vertical
            Write-Host "REPRO $phase list=$listId offset=$($axis.offset) max=$($axis.maximum) viewport=$($axis.viewportLength) visible=$visible/$($items.Count) firstY=$firstY lastY=$lastY listY=$($bounds.y) listH=$($bounds.height)"
            return $details.value
        }
        function Send-ReproWheel($list, [int]$count) {
            $bounds = $list.bounds
            for ($tick = 0; $tick -lt $count; $tick++) {
                if (-not [MtdAutomationWindowCloser]::SendWheel($script:automationProcess.MainWindowHandle,
                    [int]($bounds.x + $bounds.width / 2), [int]($bounds.y + $bounds.height / 2), -120)) {
                    throw 'Could not send queue/history wheel event.'
                }
            }
        }
        for ($pass = 1; $pass -le 3; $pass++) {
            $queueState = Get-ReproState 'QueueList' 'QueueItem-' "queue-start-$pass"
            Send-ReproWheel $queueState 24
            $queueState = Get-ReproState 'QueueList' 'QueueItem-' "queue-scrolled-$pass"
            Invoke-NodeAction (Find-Node 'NavHistory' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Start-Sleep -Milliseconds 250
            $historyState = Get-ReproState 'HistoryList' 'HistoryItem-' "history-start-$pass"
            Send-ReproWheel $historyState 12
            Get-ReproState 'HistoryList' 'HistoryItem-' "history-scrolled-$pass" | Out-Null
            Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Start-Sleep -Milliseconds 250
            Get-ReproState 'QueueList' 'QueueItem-' "queue-return-$pass" | Out-Null
        }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
            throw 'Automation process did not close cleanly.'
        }
        Write-Output "Recording sequence diagnostic complete (profile: $dataRoot)"
        return
    }
    if ($WebRemotePairingSmoke) {
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryWebRemote' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'WebRemoteEnable' $mainRoot) $mainRoot 'Toggle' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsSaveButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $remoteAddress = "http://127.0.0.1:$remotePort/"
        $remoteReady = [Diagnostics.Stopwatch]::StartNew()
        do {
            try { $bootstrap = Invoke-RestMethod -Uri ($remoteAddress + 'api/auth-mode') -TimeoutSec 2 }
            catch { $bootstrap = $null; Start-Sleep -Milliseconds 100 }
        } while (-not $bootstrap -and $remoteReady.Elapsed.TotalSeconds -lt 30)
        if (-not $bootstrap.authenticationRequired -or $bootstrap.desktopLanguage -ne $Language) { throw 'Saved Settings did not enable authenticated remote with the desktop language.' }
        foreach ($endpoint in @('api/status','api/queue','api/live')) {
            $response = Invoke-WebRequest -Uri ($remoteAddress + $endpoint) -SkipHttpErrorCheck
            if ($response.StatusCode -ne 401) { throw "Unauthenticated endpoint did not return 401: $endpoint" }
        }
        $pairingNode = Find-Node 'WebRemotePairingCode' $mainRoot
        $pairingState = Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
            '--session',$pairingNode.handle.sessionId,'--node',$pairingNode.handle.runtimeId)
        if ($pairingState.value.value -notmatch '^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}$') { throw 'Fresh Settings code is unavailable.' }
        Invoke-NodeAction $pairingNode $mainRoot 'ScrollIntoView' | Out-Null
        Write-Output "Remote Settings enable/save: PASS; anonymous status/queue/live=401; code available (not logged). PID=$($script:automationProcess.Id) root=$mainRoot address=$remoteAddress profile=$dataRoot"
        if ($RemoteManualHoldSeconds -gt 0) {
            $manualHold = [Diagnostics.Stopwatch]::StartNew()
            while ($manualHold.Elapsed.TotalSeconds -lt $RemoteManualHoldSeconds -and -not $script:automationProcess.HasExited) {
                Start-Sleep -Milliseconds 250
            }
        }
        if (-not $script:automationProcess.CloseMainWindow()) { throw 'Pairing QA close request failed.' }
        # A running remote host intentionally counts as active work. Accept the
        # real confirmation in this owned test instead of waiting behind its dialog.
        $closeTimer = [Diagnostics.Stopwatch]::StartNew(); $closeRoot = $null
        do {
            $closeRoots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
            $closeRoot = @($closeRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
        } while (-not $closeRoot -and $closeTimer.Elapsed.TotalSeconds -lt 10)
        if (-not $closeRoot) { throw 'Active Web Remote close confirmation missing.' }
        Invoke-NodeAction (Find-Node 'ShutdownExit' $closeRoot.rootId) $closeRoot.rootId 'Invoke' | Out-Null
        if (-not $script:automationProcess.WaitForExit(30000) -or $script:automationProcess.ExitCode -ne 0) { throw 'Pairing QA did not close cleanly.' }
        Write-Output 'Web Remote native smoke PASS: protected endpoints, pairing code and work-aware clean shutdown.'
        return
    }
    if ($NotificationsSmoke) {
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryNotifications' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $section = Find-Node 'NotificationsSection' $mainRoot
        Invoke-NodeAction $section $mainRoot 'ScrollIntoView' | Out-Null
        $test = Find-Node 'NotificationsTest' $mainRoot
        Invoke-NodeAction $test $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'NotificationDiagnostics' $mainRoot) $mainRoot 'Toggle' | Out-Null
        $tree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '9')
        $tree | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath (Join-Path $dataRoot 'notifications-tree.json') -Encoding utf8
        if ($CaptureVisual) {
            Add-Type -AssemblyName System.Drawing
            $bounds = ($tree.nodes | Where-Object { $_.node.automationId -eq 'MainWindow' }).node.bounds
            $picture = [Drawing.Bitmap]::new([int]$bounds.width, [int]$bounds.height)
            $drawing = [Drawing.Graphics]::FromImage($picture)
            try {
                Capture-OwnedBitmap $drawing
                $picture.Save((Join-Path $dataRoot 'notifications-settings.png'))
            } finally { $drawing.Dispose(); $picture.Dispose() }
        }
        Write-Output "Notifications native UI smoke: test invoked ($Language/$Theme), profile=$dataRoot PID=$($script:automationProcess.Id). OS visibility/click require separate human verification."
        if ($NotificationManualHoldSeconds -gt 0) { Start-Sleep -Seconds $NotificationManualHoldSeconds }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) { throw 'Notification QA did not close cleanly.' }
        return
    }
    if ($CookieSettingsSmoke) {
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsCategoryAuthentication' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $source=Find-Node 'AuthenticationSource' $mainRoot
        Invoke-NodeAction $source $mainRoot 'ScrollIntoView' | Out-Null
        $detail=Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
            '--session',$source.handle.sessionId,'--node',$source.handle.runtimeId)
        $choices=@($detail.value.childRuntimeIds | ForEach-Object {
            (Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
                '--session',$source.handle.sessionId,'--node',$_)).value
        })
        if($choices.Count -ne 3){throw 'Expected exactly three exclusive authentication sources.'}
        Invoke-NodeAction $choices[1] $mainRoot 'Select' | Out-Null
        Invoke-NodeAction (Find-Node 'CookieSyncRefresh' $mainRoot) $mainRoot 'Invoke' | Out-Null
        foreach($id in @('CookieProfilesStatus','CookieBridgeBrowser','CookieBridgeExtensionId','CookieBridgeConsent')){[void](Find-Node $id $mainRoot)}
        if($CaptureVisual){
            Invoke-NodeAction (Find-Node 'CookieSyncSection' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
            Add-Type -AssemblyName System.Drawing
            $bounds=@($tree.nodes | Where-Object {$_.node.automationId -eq 'MainWindow'})[0].node.bounds
            if(-not $bounds){$bounds=$tree.nodes[0].node.bounds}
            $picture=[Drawing.Bitmap]::new([int]$bounds.width,[int]$bounds.height);$drawing=[Drawing.Graphics]::FromImage($picture)
            try{Capture-OwnedBitmap $drawing;$path=Join-Path $dataRoot 'cookie-settings.png';$picture.Save($path);Write-Output "Cookie settings capture: $path"}
            finally{$drawing.Dispose();$picture.Dispose()}
        }
        Invoke-NodeAction $choices[0] $mainRoot 'Select' | Out-Null
        Invoke-NodeAction (Find-Node 'SettingsSaveButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        if(-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)){throw 'Cookie settings smoke did not close cleanly.'}
        $saved=Get-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Raw | ConvertFrom-Json
        if($saved.cookieSource -ne 0 -or $saved.useCookieFile){throw 'Authentication source exclusivity did not persist.'}
        Write-Output "Cookie settings native smoke PASS ($Language/$Theme): three sources, status, controls, save; no registration or real cookies."
        return
    }
    if ($SettingsScrollRepro) {
        Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $settingsNode = Find-Node 'SettingsScroll' $mainRoot
        function Get-SettingsScrollState([string]$phase) {
            for ($sample = 0; $sample -lt 4; $sample++) {
                $panel = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                    '--session', $settingsNode.handle.sessionId, '--node', $settingsNode.handle.runtimeId)
                $tree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '6')
                $cards = @($tree.nodes | Where-Object { $_.node.parentRuntimeId -eq $settingsNode.handle.runtimeId } | ForEach-Object { $_.node })
                $view = $panel.value.scrollInfo.viewportBounds
                $visible = @($cards | Where-Object { $_.bounds.y -lt $view.bottom -and ($_.bounds.y + $_.bounds.height) -gt $view.top }).Count
                $axis = $panel.value.scrollInfo.vertical
                $firstY = if ($cards.Count) { $cards[0].bounds.y } else { 'none' }
                $lastY = if ($cards.Count) { $cards[-1].bounds.y } else { 'none' }
                $aligned = $cards.Count -eq 19 -and [Math]::Abs($firstY - ($panel.value.bounds.y - $axis.offset)) -le 1
                if ($aligned) { break }
                Start-Sleep -Milliseconds 60
            }
            Write-Host "SETTINGS $phase value=$($axis.offset) min=$($axis.minimum) max=$($axis.maximum) viewport=$($axis.viewportLength) bounds=$($panel.value.bounds.x),$($panel.value.bounds.y),$($panel.value.bounds.width),$($panel.value.bounds.height) firstY=$firstY lastY=$lastY visible=$visible/$($cards.Count) aligned=$aligned"
            return @{ panel = $panel.value; cards = $cards; visible = $visible; aligned = $aligned }
        }
        function Save-SettingsVisual([string]$phase, $state) {
            if (-not $CaptureVisual) { return }
            $bounds = $state.panel.bounds
            Add-Type -AssemblyName System.Drawing
            $picture = [System.Drawing.Bitmap]::new([int]$bounds.width, [int]$bounds.height)
            $drawing = [System.Drawing.Graphics]::FromImage($picture)
            try {
                Capture-OwnedBitmap $drawing
                $path = Join-Path $dataRoot "settings-scroll-$phase.png"
                $picture.Save($path)
                Write-Output "Settings visual capture: $path"
            }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
        $initial = Get-SettingsScrollState 'initial'
        Save-SettingsVisual 'initial' $initial
        $auth = Find-Node 'WebRemoteRequireAuthentication' $mainRoot
        $failure = $false
        for ($pass = 1; $pass -le 20; $pass++) {
            Invoke-NodeAction $auth $mainRoot 'ScrollIntoView' | Out-Null
            $before = Get-SettingsScrollState "pass-$pass-before-toggle"
            if ($pass -eq 1) { Save-SettingsVisual 'before' $before }
            if ($pass -eq 1) {
                $visibleAuth = Find-Node 'WebRemoteRequireAuthentication' $mainRoot
                $clickX = [int]($visibleAuth.bounds.x + 15)
                $clickY = [int]($visibleAuth.bounds.y + $visibleAuth.bounds.height / 2)
                [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0201, $clickX, $clickY, $true) | Out-Null
                [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0202, $clickX, $clickY, $false) | Out-Null
            }
            else { Invoke-NodeAction $auth $mainRoot 'Toggle' | Out-Null }
            $after = Get-SettingsScrollState "pass-$pass-after-toggle"
            if ($pass -eq 1) { Save-SettingsVisual 'after' $after }
            if (-not $after.aligned -or $after.visible -eq 0 -or
                # The pairing block now contains code plus multiline state row (100px), rather than the old 44px token row.
                [Math]::Abs($after.panel.scrollInfo.vertical.maximum - $before.panel.scrollInfo.vertical.maximum) -ne 100 -or
                $after.panel.scrollInfo.vertical.maximum -gt $initial.panel.scrollInfo.vertical.maximum + 150) {
                Write-Output "SETTINGS LAYOUT FAILURE pass=$pass profile=$dataRoot"
                $failure = $true
                break
            }
        }
        if (-not $failure) {
            $bounds = $initial.panel.bounds
            [MtdAutomationWindowCloser]::SendWheel($script:automationProcess.MainWindowHandle,
                [int]($bounds.x + $bounds.width / 2), [int]($bounds.y + $bounds.height / 2), -120) | Out-Null
            $wheel = Get-SettingsScrollState 'wheel-after-toggle'
            if (-not $wheel.aligned -or $wheel.panel.scrollInfo.vertical.offset -le $after.panel.scrollInfo.vertical.offset) {
                throw 'Settings wheel did not advance after changing Web Remote authentication.'
            }
            $trackX = [int]($bounds.x + $bounds.width - 7)
            $trackY = [int]($bounds.y + $bounds.height * 0.83)
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0201, $trackX, $trackY, $true) | Out-Null
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0202, $trackX, $trackY, $false) | Out-Null
            $track = Get-SettingsScrollState 'track-after-toggle'
            if (-not $track.aligned -or $track.panel.scrollInfo.vertical.offset -le $wheel.panel.scrollInfo.vertical.offset) {
                throw 'Settings scrollbar track did not advance after changing Web Remote authentication.'
            }
            for ($tick = 0; $tick -lt 18; $tick++) {
                [MtdAutomationWindowCloser]::SendWheel($script:automationProcess.MainWindowHandle,
                    [int]($bounds.x + $bounds.width / 2), [int]($bounds.y + $bounds.height / 2), 120) | Out-Null
            }
            $fastWheel = Get-SettingsScrollState 'fast-wheel-after-toggle'
            if (-not $fastWheel.aligned -or $fastWheel.panel.scrollInfo.vertical.offset -ge $track.panel.scrollInfo.vertical.offset) {
                throw 'Settings rapid wheel did not move upward after changing Web Remote authentication.'
            }
            $topTrackY = [int]($bounds.y + $bounds.height * 0.12)
            $top = $null
            for ($click = 0; $click -lt 16; $click++) {
                $previousOffset = if ($top) { $top.panel.scrollInfo.vertical.offset } else { -1 }
                [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0201, $trackX, $topTrackY, $true) | Out-Null
                [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0202, $trackX, $topTrackY, $false) | Out-Null
                $top = Get-SettingsScrollState "top-track-$click"
                if ($top.panel.scrollInfo.vertical.offset -eq 0 -or
                    $top.panel.scrollInfo.vertical.offset -eq $previousOffset) { break }
            }
            if ($top.panel.scrollInfo.vertical.offset -gt 0) {
                for ($tick = 0; $tick -lt 12; $tick++) {
                    [MtdAutomationWindowCloser]::SendWheel($script:automationProcess.MainWindowHandle,
                        [int]($bounds.x + $bounds.width / 2), [int]($bounds.y + $bounds.height / 2), 120) | Out-Null
                }
                $top = Get-SettingsScrollState 'top-after-wheel'
            }
            if (-not $top.aligned -or $top.panel.scrollInfo.vertical.offset -ne 0) {
                throw 'Settings scrollbar did not return to the top before thumb drag.'
            }
            $axis = $top.panel.scrollInfo.vertical
            $thumbHeight = [Math]::Max(8, 1 + [int](($axis.largeChange / ($axis.maximum - $axis.minimum + 1 + $axis.largeChange)) * ($bounds.height - 30)))
            $thumbY = [int]($bounds.y + 15 + $thumbHeight / 2)
            $dragY = [int]($bounds.y + $bounds.height * 0.62)
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0201, $trackX, $thumbY, $true) | Out-Null
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0200, $trackX, $dragY, $true) | Out-Null
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0202, $trackX, $dragY, $false) | Out-Null
            $drag = Get-SettingsScrollState 'thumb-drag-after-toggle'
            if (-not $drag.aligned -or $drag.panel.scrollInfo.vertical.offset -le 0) {
                throw 'Settings thumb drag did not move content after changing Web Remote authentication.'
            }
        }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
            throw 'Automation process did not close cleanly.'
        }
        if ($failure) { throw 'Settings content lost its scroll position after toggling Web Remote authentication.' }
        Write-Output 'Settings checkbox scroll regression PASS: 20 toggles while scrolled.'
        return
    }
    if ($ExternalProtocolSmoke) {
        function Send-ExternalProtocol([bool]$autoAnalyze, [string]$sourceUrl = 'https://youtu.be/abc', [bool]$autoQueue = $false) {
            $link = 'moderntubedownloader://open?url=' + [Uri]::EscapeDataString($sourceUrl) + '&analyze=' + [int]$autoAnalyze + '&queue=' + [int]$autoQueue
            $start = [System.Diagnostics.ProcessStartInfo]::new($app)
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.ArgumentList.Add($link)
            $secondary = [System.Diagnostics.Process]::Start($start)
            try {
                if (-not $secondary.WaitForExit(12000) -or $secondary.ExitCode -ne 0) {
                    throw 'Protocol-launched secondary instance did not forward and exit cleanly.'
                }
            }
            finally { $secondary.Dispose() }
        }
        Send-ExternalProtocol $false
        $deadline = [System.Diagnostics.Stopwatch]::StartNew()
        do {
            $inputNode = Find-Node 'DownloadUrlInput' $mainRoot
            $inputState = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $inputNode.handle.sessionId, '--node', $inputNode.handle.runtimeId)
            if ($inputState.value.value -eq 'https://youtu.be/abc') { break }
            Start-Sleep -Milliseconds 100
        } while ($deadline.Elapsed.TotalSeconds -lt 10)
        if ($inputState.value.value -ne 'https://youtu.be/abc') { throw 'Primary instance did not receive the external URL.' }
        $unanalysedTree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '10')
        if (@($unanalysedTree.nodes | Where-Object { $_.node.automationId -eq 'DetailsButton' }).Count) {
            throw 'Analyze ran despite analyze=0.'
        }
        Send-ExternalProtocol $true
        Wait-Node 'DetailsButton' $mainRoot 'Enabled' | Out-Null
        Send-ExternalProtocol $true 'https://www.youtube.com/live/active' $true
        Wait-Node 'LiveStartButton' $mainRoot 'Enabled' | Out-Null
        if (@(Get-Content -LiteralPath (Join-Path $dataRoot 'live-sessions.json') -Raw | ConvertFrom-Json).Count -ne 0) {
            throw 'External LIVE request started a recording without desktop confirmation.'
        }
        Send-ExternalProtocol $true 'https://www.youtube.com/live/replay'
        Invoke-NodeAction (Wait-Node 'LiveOpenDownloadsButton' $mainRoot 'Enabled').snapshot $mainRoot 'Invoke' | Out-Null
        Wait-Node 'DetailsButton' $mainRoot 'Enabled' | Out-Null
        Send-ExternalProtocol $true 'https://www.youtube.com/live/upcoming'
        Wait-Node 'LiveStartButton' $mainRoot 'Enabled' | Out-Null
        if (@(Get-Content -LiteralPath (Join-Path $dataRoot 'live-sessions.json') -Raw | ConvertFrom-Json).Count -ne 0) {
            throw 'External upcoming request scheduled recording without desktop confirmation.'
        }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
            throw 'Primary instance did not close cleanly after external handoff.'
        }
        Write-Output "External protocol process smoke PASS (profile: $dataRoot)"
        return
    }
    if ($LiveSmoke) {
        function Read-LiveState([string]$path) {
            # Allow atomic replacement while this QA observer reads the old
            # snapshot. Get-Content's default sharing can deny File.Move on Windows.
            $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
                ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
            $reader = [System.IO.StreamReader]::new($stream)
            try { return $reader.ReadToEnd() | ConvertFrom-Json | Write-Output }
            finally { $reader.Dispose() }
        }
        function Save-LiveVisual([string]$phase, [string]$rootId = $mainRoot, [string]$windowId = 'MainWindow') {
            if (-not $CaptureVisual) { return }
            $bounds = (Find-Node $windowId $rootId).bounds
            Add-Type -AssemblyName System.Drawing
            $picture = [System.Drawing.Bitmap]::new([int]$bounds.width, [int]$bounds.height)
            $drawing = [System.Drawing.Graphics]::FromImage($picture)
            try {
                $dc = $drawing.GetHdc()
                try {
                    if (-not [MtdAutomationWindowCloser]::CaptureOwnWindow($script:automationProcess.Id,
                        $script:automationProcess.MainWindowHandle, ($windowId -ne 'MainWindow'), $dc)) {
                        throw 'Could not capture the test application window.'
                    }
                }
                finally { $drawing.ReleaseHdc($dc) }
                $path = Join-Path $dataRoot "live-$phase.png"
                $picture.Save($path)
                Write-Output "LIVE visual capture: $path"
            }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
        Invoke-NodeAction (Find-Node 'NavLive' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'LiveUrlInput' $mainRoot) $mainRoot 'SetValue' $SampleUrl | Out-Null
        Invoke-NodeAction (Wait-Node 'LiveAnalyzeButton' $mainRoot 'Enabled').snapshot $mainRoot 'Invoke' | Out-Null
        if ($LiveReplayNavigationSmoke) {
            Invoke-NodeAction (Wait-Node 'LiveOpenDownloadsButton' $mainRoot 'Enabled').snapshot $mainRoot 'Invoke' | Out-Null
            Wait-Node 'AddToQueueButton' $mainRoot 'Enabled' | Out-Null
            if ((Test-Path (Join-Path $dataRoot 'queue.json')) -and @((Get-Content (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json) | Write-Output).Count -ne 0) { throw 'Replay navigation queued media.' }
            if ((Test-Path (Join-Path $dataRoot 'live-sessions.json')) -and @((Get-Content (Join-Path $dataRoot 'live-sessions.json') -Raw | ConvertFrom-Json) | Write-Output).Count -ne 0) { throw 'Replay navigation started LIVE.' }
            Write-Output 'Non-YouTube desktop LIVE replay → Downloads navigation PASS without queueing.'
            return
        }
        Wait-Node 'LiveStartButton' $mainRoot 'Enabled' | Out-Null
        Wait-Node 'LiveFromStartOption' $mainRoot | Out-Null
        Save-LiveVisual 'analyzed'
        Invoke-NodeAction (Find-Node 'LiveStartButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        $activeLive = $RealLiveSmoke -or $SampleUrl -match '/live/active'
        $store = Join-Path $dataRoot 'live-sessions.json'
        $deadline = [System.Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 100
            if (Test-Path -LiteralPath $store) {
                $items = @(Read-LiveState $store)
                if ($items.Count -eq 1 -and $items[0].state -eq $(if ($activeLive) { if ($RealLiveSmoke) { 3 } else { 6 } } else { 1 })) { break }
            }
        } while ($deadline.Elapsed.TotalSeconds -lt 25)
        if ($RealLiveSmoke -and $items.Count -eq 1 -and $items[0].state -eq 3) {
            Save-LiveVisual 'recording'
            Start-Sleep -Seconds $LiveCaptureSeconds
            $stopId = 'LiveStopSaveButton-' + ([Guid]$items[0].sessionId).ToString('N')
            Invoke-NodeAction (Find-Node $stopId $mainRoot) $mainRoot 'Invoke' | Out-Null
            $deadline.Restart()
            do {
                Start-Sleep -Milliseconds 200
                $items = @(Read-LiveState $store)
                if ($items[0].state -in @(6,7,8)) { break }
            } while ($deadline.Elapsed.TotalSeconds -lt 45)
        }
        if ($items.Count -ne 1) { throw 'Expected one independent LIVE session.' }
        if ($activeLive) {
            $finalFile = @($items[0].parts)[-1]
            if ($items[0].state -ne 6 -or -not (Test-Path -LiteralPath $finalFile)) { throw 'LIVE page did not complete a recording.' }
            if ($InterfaceMode -eq 'ModernDashboard') { Invoke-NodeAction (Find-Node 'DashboardLiveRecent' $mainRoot) $mainRoot 'Invoke' | Out-Null }
            $detailsId = 'LiveDetailsButton-' + ([Guid]$items[0].sessionId).ToString('N')
            Invoke-NodeAction (Find-Node $detailsId $mainRoot) $mainRoot 'Invoke' | Out-Null
            $deadline.Restart()
            do {
                Start-Sleep -Milliseconds 100
                $roots = Invoke-AutomationCli -CliArguments @('roots','--pid',"$($script:automationProcess.Id)")
                $detailsRoot = @($roots.value | Where-Object { $_.rootId -ne $mainRoot }) | Select-Object -First 1
            } while (-not $detailsRoot -and $deadline.Elapsed.TotalSeconds -lt 10)
            if (-not $detailsRoot) { throw 'LIVE details root was not registered.' }
            Wait-Node 'LiveDetailsContent' $detailsRoot.rootId | Out-Null
            Save-LiveVisual 'details' $detailsRoot.rootId 'LiveDetailsWindow'
            [MtdAutomationWindowCloser]::CloseDetails($script:automationProcess.Id, $script:automationProcess.MainWindowHandle) | Out-Null
            if ($RealLiveSmoke) {
                $probeJson = & $FfprobePath -v error -show_entries 'format=duration,format_name:stream=codec_type' -of json $finalFile | ConvertFrom-Json
                if ($LASTEXITCODE -ne 0 -or [double]$probeJson.format.duration -le 0 -or
                    @($probeJson.streams | Where-Object codec_type -eq 'video').Count -eq 0 -or
                    @($probeJson.streams | Where-Object codec_type -eq 'audio').Count -eq 0) { throw 'Real LIVE output failed ffprobe.' }
                & $FfmpegPath -v error -i $finalFile -t 2 -f null - 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Real LIVE output failed decode sanity.' }
                Write-Output "REAL LIVE file=$finalFile duration=$($probeJson.format.duration)"
            }
        }
        elseif ($items[0].state -ne 1 -or -not $items[0].waitForScheduledStart) { throw 'Upcoming LIVE intent was not persisted.' }
        $queueItems = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
        if ($queueItems.Count -ne 0) { throw 'LIVE leaked into queue.json.' }
        $statisticsId = if ($InterfaceMode -eq 'ModernDashboard') { 'LiveDashboardWidgets' } else { 'LiveStatistics' }
        Wait-Node $statisticsId $mainRoot | Out-Null
        Invoke-NodeAction (Find-Node ('LiveSession-' + ([Guid]$items[0].sessionId).ToString('N')) $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
        Save-LiveVisual 'final'
        if (-not $script:automationProcess.CloseMainWindow()) { throw 'LIVE close request failed.' }
        if (-not $activeLive) {
            # WaitingForLive is active work: require the production confirmation,
            # then explicitly exit rather than disabling tray/work protections.
            $deadline.Restart()
            $closeRoot = $null
            do {
                $closeRoots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
                $closeRoot = @($closeRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
            } while (-not $closeRoot -and $deadline.Elapsed.TotalSeconds -lt 15)
            if (-not $closeRoot) { throw 'Waiting LIVE did not expose a shutdown confirmation.' }
            [void](Find-Node 'ShutdownChoiceWindow' $closeRoot.rootId)
            Invoke-NodeAction (Find-Node 'ShutdownExit' $closeRoot.rootId) $closeRoot.rootId 'Invoke' | Out-Null
        }
        if (-not $script:automationProcess.WaitForExit(30000) -or $script:automationProcess.ExitCode -ne 0) { throw 'LIVE window did not close cleanly.' }
        Write-Output "LIVE Automation smoke PASS (active=$activeLive; profile: $dataRoot)"
        return
    }
    $url = Find-Node 'DownloadUrlInput' $mainRoot
    Invoke-NodeAction $url $mainRoot 'SetValue' $SampleUrl | Out-Null
    $analyze = Wait-Node 'AnalyzeButton' $mainRoot 'Enabled'
    Invoke-NodeAction $analyze.snapshot $mainRoot 'Invoke' | Out-Null
    if ($PlaylistSmoke -and $PreviewOnly) {
        Wait-Node 'PlaylistPreview' $mainRoot | Out-Null
        if ($CaptureVisual) {
            $windowNode = Find-Node 'MainWindow' $mainRoot
            $windowBounds = $windowNode.bounds
            Add-Type -AssemblyName System.Drawing
            $picture = [System.Drawing.Bitmap]::new([int]$windowBounds.width, [int]$windowBounds.height)
            $drawing = [System.Drawing.Graphics]::FromImage($picture)
            try {
                Capture-OwnedBitmap $drawing
                $visualPath = Join-Path $dataRoot 'playlist-preview.png'
                $picture.Save($visualPath)
                Write-Output "Playlist visual capture: $visualPath"
            }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
        if ($ScrollProbe) {
            $listNode = Find-Node 'PlaylistEntriesList' $mainRoot
            $listBefore = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
            $bounds = $listBefore.value.bounds
            if (-not $listBefore.value.scrollInfo.vertical.isScrollable -or $listBefore.value.scrollInfo.vertical.maximum -le 0) {
                throw 'Playlist viewport does not report a scrollable vertical range.'
            }
            Invoke-NodeAction (Find-Node 'PlaylistEntryToggle1' $mainRoot) $mainRoot 'Focus' | Out-Null
            Wait-Node 'PlaylistEntryToggle1' $mainRoot 'Focused' | Out-Null
            if (-not [MtdAutomationWindowCloser]::SendWheel($script:automationProcess.MainWindowHandle,
                [int]($bounds.x + $bounds.width / 2), [int]($bounds.y + $bounds.height / 2), -120)) {
                throw 'Could not send a native mouse-wheel event to the playlist viewport.'
            }
            $deadline = [System.Diagnostics.Stopwatch]::StartNew()
            do {
                $listWheel = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                    '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
                if ($listWheel.value.scrollInfo.vertical.offset -ne $listBefore.value.scrollInfo.vertical.offset) { break }
            } while ($deadline.Elapsed.TotalSeconds -lt 5)
            $expectedWheel = [Math]::Min($listBefore.value.scrollInfo.vertical.maximum,
                $listBefore.value.scrollInfo.vertical.offset + $listBefore.value.scrollInfo.vertical.smallChange)
            if ($listWheel.value.scrollInfo.vertical.offset -ne $expectedWheel) {
                throw "One wheel notch should scroll exactly once to $expectedWheel; got $($listWheel.value.scrollInfo.vertical.offset)."
            }
            Invoke-NodeAction (Find-Node 'PlaylistQualitySelector' $mainRoot) $mainRoot 'Focus' | Out-Null
            [MtdAutomationWindowCloser]::SendWheel($script:automationProcess.MainWindowHandle,
                [int]($bounds.x + $bounds.width / 2), [int]($bounds.y + $bounds.height / 2), -120) | Out-Null
            $listOutsideFocus = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
            if ($listOutsideFocus.value.scrollInfo.vertical.offset -le $listWheel.value.scrollInfo.vertical.offset) {
                throw 'Wheel did not reach playlist when keyboard focus was outside the list.'
            }
            $trackX = [int]($bounds.x + $bounds.width - 7)
            $trackY = [int]($bounds.y + $bounds.height * 0.72)
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0201, $trackX, $trackY, $true) | Out-Null
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0202, $trackX, $trackY, $false) | Out-Null
            $listTrack = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
            if ($listTrack.value.scrollInfo.vertical.offset -le $listOutsideFocus.value.scrollInfo.vertical.offset) {
                throw 'Clicking the scrollbar track did not advance the playlist.'
            }
            $firstRow = Find-Node 'PlaylistEntryToggle1' $mainRoot
            Invoke-NodeAction $firstRow $mainRoot 'ScrollIntoView' | Out-Null
            $axis = $listBefore.value.scrollInfo.vertical
            $thumbHeight = [Math]::Max(8, 1 + [int](($axis.largeChange / ($axis.maximum - $axis.minimum + 1 + $axis.largeChange)) * ($bounds.height - 30)))
            $thumbY = [int]($bounds.y + 15 + $thumbHeight / 2)
            $dragY = [int]($bounds.y + $bounds.height * 0.65)
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0201, $trackX, $thumbY, $true) | Out-Null
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0200, $trackX, $dragY, $true) | Out-Null
            [MtdAutomationWindowCloser]::SendMouse($script:automationProcess.MainWindowHandle, 0x0202, $trackX, $dragY, $false) | Out-Null
            $listDrag = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
            if ($listDrag.value.scrollInfo.vertical.offset -le 0) { throw 'Dragging the scrollbar thumb did not scroll.' }
            $lastRow = Find-Node "PlaylistEntryToggle$([Math]::Min(40, $PlaylistCount))" $mainRoot
            Invoke-NodeAction $lastRow $mainRoot 'ScrollIntoView' | Out-Null
            $listAfter = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
            if ($listAfter.value.scrollInfo.vertical.offset -lt 0 -or
                $listAfter.value.scrollInfo.vertical.offset -gt $listAfter.value.scrollInfo.vertical.maximum) {
                throw 'Playlist scroll offset left the valid range.'
            }
            Write-Output "Scroll probe count=$PlaylistCount before=$($listBefore.value.scrollInfo.vertical | ConvertTo-Json -Compress) wheel=$($listWheel.value.scrollInfo.vertical.offset) track=$($listTrack.value.scrollInfo.vertical.offset) drag=$($listDrag.value.scrollInfo.vertical.offset) afterReveal=$($listAfter.value.scrollInfo.vertical.offset)"
        }
        $firstPage = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '9')
        $firstRows = @($firstPage.nodes | Where-Object { $_.node.automationId -like 'PlaylistEntryToggle*' })
        if ($firstRows.Count -ne [Math]::Min(40, $PlaylistCount)) { throw "First page rendered $($firstRows.Count) entries instead of at most 40." }
        if ($PlaylistCount -gt 40) {
            Invoke-NodeAction (Find-Node 'PlaylistNextPageButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Wait-Node 'PlaylistEntryToggle41' $mainRoot | Out-Null
            if ($ScrollProbe) {
                $listNode = Find-Node 'PlaylistEntriesList' $mainRoot
                $newPage = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                    '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
                if ($newPage.value.scrollInfo.vertical.offset -ne 0) { throw 'Playlist page switch did not reset scroll to top.' }
                Write-Output "Scroll probe nextPage=$($newPage.value.scrollInfo.vertical | ConvertTo-Json -Compress)"
            }
            $secondPage = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '9')
            $secondRows = @($secondPage.nodes | Where-Object { $_.node.automationId -like 'PlaylistEntryToggle*' })
            if ($secondRows.Count -ne [Math]::Min(40, $PlaylistCount - 40)) { throw "Second page rendered $($secondRows.Count) entries instead of at most 40." }
            Invoke-NodeAction (Find-Node 'PlaylistPreviousPageButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
            Wait-Node 'PlaylistEntryToggle1' $mainRoot | Out-Null
            if ($ScrollProbe) {
                $firstPageAgain = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                    '--session', $listNode.handle.sessionId, '--node', $listNode.handle.runtimeId)
                if ($firstPageAgain.value.scrollInfo.vertical.offset -ne 0) { throw 'Previous page did not reset scroll to top.' }
            }
        }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
            throw 'Automation process did not close cleanly.'
        }
        Write-Output "Large playlist preview PASS ($PlaylistCount entries, paged semantic UI; profile: $dataRoot)"
        return
    }
    if ($PlaylistSmoke) {
        Wait-Node 'PlaylistAddSelectedButton' $mainRoot 'Enabled' | Out-Null
        Invoke-NodeAction (Find-Node 'PlaylistClearSelectionButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'PlaylistSelectAllButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
        Invoke-NodeAction (Find-Node 'PlaylistEntryToggle2' $mainRoot) $mainRoot 'Toggle' | Out-Null
        $expectedCount = $PlaylistCount - 2 # one unavailable entry and one unchecked entry
        $countNode = Find-Node 'PlaylistSelectedCount' $mainRoot
        if ($countNode.name -notmatch "\b$expectedCount\b") { throw "Expected $expectedCount selected playlist entries, got '$($countNode.name)'" }
        $qualityId = 'PlaylistQualitySelector'
        $addId = 'PlaylistAddSelectedButton'
    }
    else {
        Wait-Node 'AddToQueueButton' $mainRoot 'Enabled' | Out-Null
        if ($ClearPreviewSmoke) {
            $preview = Find-Node 'PreviewCard' $mainRoot
            $details = Find-Node 'DetailsButton' $mainRoot
            $add = Find-Node 'AddToQueueButton' $mainRoot
            $clear = Find-Node 'ClearAnalyzedPreviewButton' $mainRoot
            if ($preview.bounds.height -gt 230) { throw "Analyzed preview is too tall: $($preview.bounds.height)" }
            foreach ($button in @($details, $add, $clear)) {
                if ($button.bounds.y -lt $preview.bounds.y -or
                    $button.bounds.y + $button.bounds.height -gt $preview.bounds.y + $preview.bounds.height) {
                    throw "Preview action $($button.automationId) is clipped."
                }
            }
            Invoke-NodeAction $clear $mainRoot 'Invoke' | Out-Null
            Wait-Node 'ClearAnalyzedPreviewButton' $mainRoot 'NodeNotExposed' | Out-Null
            Wait-Node 'AddToQueueButton' $mainRoot 'NodeNotExposed' | Out-Null
            $urlAfter = Find-Node 'DownloadUrlInput' $mainRoot
            $urlState = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $urlAfter.handle.sessionId, '--node', $urlAfter.handle.runtimeId)
            if ($urlState.value.value -ne $SampleUrl) { throw 'Clear analysis changed the URL.' }
            $emptyPreview = Find-Node 'PreviewCard' $mainRoot
            if ($emptyPreview.bounds.height -gt 160) { throw "Empty preview is too tall: $($emptyPreview.bounds.height)" }
            if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
                throw 'Automation process did not close cleanly after clear-preview smoke.'
            }
            Write-Output "Clear preview/layout smoke PASS (analyzed=$($preview.bounds.height), empty=$($emptyPreview.bounds.height); profile: $dataRoot)"
            return
        }
        if ($CaptureVisual) {
            $windowNode = Find-Node 'MainWindow' $mainRoot
            $windowBounds = $windowNode.bounds
            Add-Type -AssemblyName System.Drawing
            $picture = [System.Drawing.Bitmap]::new([int]$windowBounds.width, [int]$windowBounds.height)
            $drawing = [System.Drawing.Graphics]::FromImage($picture)
            try {
                Capture-OwnedBitmap $drawing
                $visualPath = Join-Path $dataRoot 'video-range-preview.png'
                $picture.Save($visualPath)
                Write-Output "Video range visual capture: $visualPath"
            }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
        if ($CaptureVisual -or $QualityDropdownSmoke) {
            $dropdownTrace = Join-Path $dataRoot 'quality-dropdown-native-trace.jsonl'
            function Write-QualityDropdownTrace([string]$stage, $node, $actionResult) {
                $popupWidth = 0; $popupHeight = 0
                $mainHwnd = $script:automationProcess.MainWindowHandle
                $popup = [MtdAutomationWindowCloser]::FindOwnPopup($script:automationProcess.Id,
                    $mainHwnd, [ref]$popupWidth, [ref]$popupHeight)
                $nativeFocus = [MtdAutomationWindowCloser]::NativeFocusSnapshot($mainHwnd)
                [ordered]@{
                    timestamp = [DateTimeOffset]::Now.ToString('o'); iteration = $pass; stage = $stage
                    automationId = 'QualitySelector'; handle = $node.handle
                    states = $node.states; availableActions = $node.supportedActions
                    actionResult = $actionResult; mainHwnd = $mainHwnd.ToInt64()
                    foregroundHwnd = $nativeFocus[0]; activeHwnd = $nativeFocus[1]
                    nativeFocusHwnd = $nativeFocus[2]; mouseCaptureHwnd = $nativeFocus[3]
                    visiblePopupHwnd = $popup.ToInt64(); popupWidth = $popupWidth; popupHeight = $popupHeight
                } | ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath $dropdownTrace -Encoding utf8
            }
            function Inspect-QualityDropdown {
                $node = Find-Node 'QualitySelector' $mainRoot
                return (Invoke-AutomationCli -CliArguments @('inspect','--pid',"$($script:automationProcess.Id)",'--root',$mainRoot,
                    '--session',$node.handle.sessionId,'--node',$node.handle.runtimeId)).value
            }
            $passes = if ($QualityDropdownSmoke) { $QualityDropdownPasses } else { 1 }
            for ($pass = 1; $pass -le $passes; $pass++) {
                $qualityDropdown = Inspect-QualityDropdown
                Write-QualityDropdownTrace 'before-expand' $qualityDropdown $null
                $expandResult = Invoke-NodeAction $qualityDropdown $mainRoot 'Expand'
                $expanded = Inspect-QualityDropdown
                Write-QualityDropdownTrace 'after-expand' $expanded $expandResult
                if ($expanded.states -notmatch '\bExpanded\b' -or $expanded.supportedActions -notmatch '\bCollapse\b') {
                    throw "Quality popup did not stay open after Expand; pass=$pass states=$($expanded.states) actions=$($expanded.supportedActions)"
                }
                if ($CaptureVisual -and $pass -eq 1) {
                    $popupWidth = 0; $popupHeight = 0
                    $popup = [MtdAutomationWindowCloser]::FindOwnPopup($script:automationProcess.Id,
                        $script:automationProcess.MainWindowHandle, [ref]$popupWidth, [ref]$popupHeight)
                    if ($popup -eq [IntPtr]::Zero -or $popupWidth -le 0 -or $popupHeight -le 0) {
                        throw 'Expanded quality popup has no visible own native HWND.'
                    }
                    $popupPicture = [Drawing.Bitmap]::new($popupWidth, $popupHeight)
                    $popupDrawing = [Drawing.Graphics]::FromImage($popupPicture)
                    try {
                        $dc = $popupDrawing.GetHdc()
                        try {
                            if (-not [MtdAutomationWindowCloser]::CaptureOwnPopup($script:automationProcess.Id,$popup,$dc)) {
                                throw 'Quality popup own-HWND capture failed.'
                            }
                        } finally { $popupDrawing.ReleaseHdc($dc) }
                        $popupPath = Join-Path $dataRoot 'video-quality-dropdown.png'
                        $popupPicture.Save($popupPath)
                        Write-Output "Quality dropdown own-popup capture: $popupPath"
                    } finally { $popupDrawing.Dispose(); $popupPicture.Dispose() }
                }
                $beforeCollapse = Inspect-QualityDropdown
                Write-QualityDropdownTrace 'before-collapse' $beforeCollapse $null
                if ($beforeCollapse.states -notmatch '\bExpanded\b') {
                    throw "Quality popup closed before Collapse; pass=$pass states=$($beforeCollapse.states)"
                }
                $collapseResult = Invoke-NodeAction $beforeCollapse $mainRoot 'Collapse'
                $collapsed = Inspect-QualityDropdown
                Write-QualityDropdownTrace 'after-collapse' $collapsed $collapseResult
                if ($collapsed.states -notmatch '\bCollapsed\b') { throw 'Collapse did not close quality popup.' }
            }
            Write-Output "Quality popup Expand/inspect/Collapse PASS: $passes cycles on current MFN; captures target the popup HWND, not the main HWND."
        }
        $qualityId = 'QualitySelector'
        $addId = 'AddToQueueButton'
    }
    $quality = Find-Node $qualityId $mainRoot
    $qualityDetails = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
        '--session', $quality.handle.sessionId, '--node', $quality.handle.runtimeId)
    $choiceDetails = $null
    foreach ($childId in $qualityDetails.value.childRuntimeIds) {
        $candidate = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
            '--session', $quality.handle.sessionId, '--node', $childId)
        if ($candidate.value.name -match '1080') { $choiceDetails = $candidate; break }
    }
    if (-not $choiceDetails) { throw 'The synthetic video has no 1080p quality choice.' }
    if ($choiceDetails.value.supportedActions -notmatch 'Select') { throw 'Quality choice does not support Select.' }
    Invoke-NodeAction $choiceDetails.value $mainRoot 'Select' | Out-Null
    $selectedQuality = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
        '--session', $quality.handle.sessionId, '--node', $quality.handle.runtimeId)
    if ($selectedQuality.value.value -ne $choiceDetails.value.name) { throw 'Quality selection did not change the ComboBox value.' }
    if (-not $qualityDetails.value.value) { throw 'QualitySelector has no selected value.' }
    if ($RangeSmoke -and -not $PlaylistSmoke) {
        $rangeMode = Find-Node 'VideoRangeMode' $mainRoot
        $rangeDetails = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
            '--session', $rangeMode.handle.sessionId, '--node', $rangeMode.handle.runtimeId)
        $custom = $null
        foreach ($childId in $rangeDetails.value.childRuntimeIds) {
            $candidate = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
                '--session', $rangeMode.handle.sessionId, '--node', $childId)
            if ($candidate.value.name -match 'Custom|Niestandardowy') { $custom = $candidate.value; break }
        }
        if (-not $custom) { throw 'Custom media range is not exposed to Automation.' }
        Invoke-NodeAction $custom $mainRoot 'Select' | Out-Null
        $hint = Find-Node 'VideoRangeKeyframeHint' $mainRoot
        $hintDetails = Invoke-AutomationCli -CliArguments @('inspect', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot,
            '--session', $hint.handle.sessionId, '--node', $hint.handle.runtimeId)
        $hintPattern = if ($Language -eq 'pl') { 'klatki kluczowej' } else { 'keyframe' }
        if ($hintDetails.value.name -notmatch $hintPattern) { throw "Localized keyframe warning is missing for $Language." }
        Invoke-NodeAction (Find-Node 'VideoRangeStart' $mainRoot) $mainRoot 'SetValue' '00:00:02' | Out-Null
        Invoke-NodeAction (Find-Node 'VideoRangeEnd' $mainRoot) $mainRoot 'SetValue' '00:00:10' | Out-Null
        if ($CaptureVisual) {
            $windowNode = Find-Node 'MainWindow' $mainRoot
            $windowBounds = $windowNode.bounds
            Add-Type -AssemblyName System.Drawing
            $picture = [System.Drawing.Bitmap]::new([int]$windowBounds.width, [int]$windowBounds.height)
            $drawing = [System.Drawing.Graphics]::FromImage($picture)
            try {
                Capture-OwnedBitmap $drawing
                $visualPath = Join-Path $dataRoot 'video-custom-range.png'
                $picture.Save($visualPath)
                Write-Output "Custom range visual capture: $visualPath"
            }
            finally { $drawing.Dispose(); $picture.Dispose() }
        }
    }
    Invoke-NodeAction (Find-Node $addId $mainRoot) $mainRoot 'Invoke' | Out-Null
    Wait-Node 'QueueList' $mainRoot | Out-Null
    if ($PlaylistSmoke) {
        Invoke-NodeAction (Find-Node $addId $mainRoot) $mainRoot 'Invoke' | Out-Null
        if ($expectedCount -le 30) {
            $queueTree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $mainRoot, '--depth', '12')
            $badges = @($queueTree.nodes | Where-Object { $_.node.automationId -like 'QueuePosition-*' })
            if ($badges.Count -ne $expectedCount) { throw "Expected $expectedCount semantic queue number badges, got $($badges.Count)." }
            $numbers = @($badges | ForEach-Object { [int]$_.node.name } | Sort-Object)
            if ($numbers[0] -ne 1 -or $numbers[-1] -ne $expectedCount) { throw "Queue numbering did not cover 1 through $expectedCount." }
        }
        else {
            $deadline = [System.Diagnostics.Stopwatch]::StartNew()
            do {
                try { $queuedBeforeClose = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json) }
                catch { $queuedBeforeClose = @() }
                if ($queuedBeforeClose.Count -eq $expectedCount) { break }
                Start-Sleep -Milliseconds 100
            } while ($deadline.Elapsed.TotalSeconds -lt 30)
            if ($queuedBeforeClose.Count -ne $expectedCount) { throw 'Large queue was not persisted before inspection.' }
            $firstId = ([guid]$queuedBeforeClose[0].id).ToString('N')
            $lastId = ([guid]$queuedBeforeClose[-1].id).ToString('N')
            $firstBadge = Find-Node "QueuePosition-$firstId" $mainRoot
            $lastBadge = Find-Node "QueuePosition-$lastId" $mainRoot
            if ($firstBadge.name -ne '1' -or $lastBadge.name -ne "$expectedCount") {
                throw 'First or last semantic queue badge has the wrong number.'
            }
        }
        if (-not $script:automationProcess.CloseMainWindow() -or -not $script:automationProcess.WaitForExit(30000)) {
            throw 'Automation process did not close cleanly.'
        }
        Wait-Bridge $script:automationProcess.Id $false
        $queue = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
        if ($queue.Count -ne $expectedCount) { throw "Expected $expectedCount persisted queue items after duplicate add, got $($queue.Count)." }
        $expected = @(1..$PlaylistCount | Where-Object { $_ -notin @(2, 3) })
        for ($index = 0; $index -lt $queue.Count; $index++) {
            if ($queue[$index].playlistIndex -ne $expected[$index] -or $queue[$index].qualityPresetId -ne '1080') {
                throw "Playlist queue order or quality differs at $index."
            }
        }
        Write-Output "Playlist automation smoke PASS (profile: $dataRoot; queued: $($queue.Count))"
    }
    else {
    Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
    Invoke-NodeAction (Find-Node 'SettingsCategoryDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
    $toggle = (Wait-Node 'ShowQueueNumbersToggle' $mainRoot).snapshot
    Invoke-NodeAction $toggle $mainRoot 'Toggle' | Out-Null
    Invoke-NodeAction (Find-Node 'SettingsSaveButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
    Invoke-NodeAction (Find-Node 'NavDownloads' $mainRoot) $mainRoot 'Invoke' | Out-Null
    Wait-Node 'QueueList' $mainRoot | Out-Null
    Invoke-NodeAction (Find-Node 'AdvancedOptionsButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
    $advancedRoot = $null
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $roots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
        $advancedRoot = @($roots.value | Where-Object { $_.rootId -ne $mainRoot }) | Select-Object -First 1
    } while (-not $advancedRoot -and $deadline.Elapsed.TotalSeconds -lt 30)
    if (-not $advancedRoot) { throw 'Advanced options root did not appear.' }
    $advancedTree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $advancedRoot.rootId, '--depth', '8')
    foreach ($id in @('AdvancedDownloadOptionsWindow', 'AdvancedSubtitlesEnabled', 'AdvancedSubtitleSource',
        'AdvancedSubtitleLanguages', 'AdvancedSubtitleFormat', 'AdvancedSponsorBlockMode', 'AdvancedOptionsApply')) {
        if (-not @($advancedTree.nodes | Where-Object { $_.node.automationId -eq $id }).Count) { throw "Advanced options lacks $id" }
    }
    if ($CaptureVisual) {
        $bounds = @($advancedTree.nodes | Where-Object { $_.node.automationId -eq 'AdvancedDownloadOptionsWindow' })[0].node.bounds
        Add-Type -AssemblyName System.Drawing
        $picture = [System.Drawing.Bitmap]::new([int]$bounds.width, [int]$bounds.height)
        $drawing = [System.Drawing.Graphics]::FromImage($picture)
        try {
            Capture-OwnedBitmap $drawing $true
            $visualPath = Join-Path $dataRoot 'advanced-options.png'
            $picture.Save($visualPath)
            Write-Output "Advanced options visual capture: $visualPath"
        }
        finally { $drawing.Dispose(); $picture.Dispose() }
    }
    Invoke-NodeAction (Find-Node 'AdvancedOptionsCancel' $advancedRoot.rootId) $advancedRoot.rootId 'Invoke' | Out-Null
    $deadline.Restart()
    do {
        $roots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
    } while ($roots.value.Count -ne 1 -and $deadline.Elapsed.TotalSeconds -lt 30)
    if ($roots.value.Count -ne 1) { throw 'Advanced options root did not unregister after close.' }
    Invoke-NodeAction (Find-Node 'DetailsButton' $mainRoot) $mainRoot 'Invoke' | Out-Null
    $detailsRoot = $null
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $roots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
        $detailsRoot = @($roots.value | Where-Object { $_.rootId -ne $mainRoot }) | Select-Object -First 1
    } while (-not $detailsRoot -and $deadline.Elapsed.TotalSeconds -lt 30)
    if (-not $detailsRoot) { throw 'Details root did not appear.' }
    $detailsTree = Invoke-AutomationCli -CliArguments @('tree', '--pid', "$($script:automationProcess.Id)", '--root', $detailsRoot.rootId, '--depth', '8')
    foreach ($id in @('DetailsWindow', 'DetailsTabs', 'DetailsOverviewTab', 'DetailsSourceTab', 'DetailsMetadataTab', 'DetailsFormatsTab', 'DetailsSubtitlesTab', 'DetailsChaptersTab')) {
        if (-not @($detailsTree.nodes | Where-Object { $_.node.automationId -eq $id }).Count) { throw "Details tree lacks $id" }
    }
    foreach ($section in @('Source', 'Metadata', 'Formats', 'Subtitles', 'Chapters')) {
        Invoke-NodeAction (Find-Node "Details${section}Tab" $detailsRoot.rootId) $detailsRoot.rootId 'Select' | Out-Null
        Wait-Node "Details${section}Content" $detailsRoot.rootId | Out-Null
    }
    Invoke-NodeAction (Find-Node 'DetailsSourceTab' $detailsRoot.rootId) $detailsRoot.rootId 'Select' | Out-Null
    $sourceText = Find-Node 'DetailsSourceContentText' $detailsRoot.rootId
    if ($sourceText.value -notmatch 'Extractor key: Test') { throw 'Details Source does not expose the actual extractor key.' }
    if ($CaptureVisual) {
        $sourceDetailsBounds = (Find-Node 'DetailsWindow' $detailsRoot.rootId).bounds
        $sourceDetailsPicture = [Drawing.Bitmap]::new([int]$sourceDetailsBounds.width,[int]$sourceDetailsBounds.height)
        $sourceDetailsGraphics = [Drawing.Graphics]::FromImage($sourceDetailsPicture)
        try {
            Capture-OwnedBitmap $sourceDetailsGraphics $true
            $sourceDetailsPicture.Save((Join-Path $dataRoot 'details-source.png'),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $sourceDetailsGraphics.Dispose(); $sourceDetailsPicture.Dispose() }
    }
    if (-not [MtdAutomationWindowCloser]::CloseDetails($script:automationProcess.Id, $script:automationProcess.MainWindowHandle)) {
        throw 'Could not close Details window through WM_CLOSE.'
    }
    $deadline.Restart()
    do {
        $roots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
    } while ($roots.value.Count -ne 1 -and $deadline.Elapsed.TotalSeconds -lt 30)
    if ($roots.value.Count -ne 1) { throw 'Details root did not unregister after close.' }
    # The queued fixture is intentionally still pending. The new production tray
    # policy must expose an active-work confirmation; don't bypass it or kill to pass.
    if (-not $script:automationProcess.CloseMainWindow()) { throw 'Automation main-window close request failed.' }
    $deadline.Restart()
    do {
        $closeRoots = Invoke-AutomationCli -CliArguments @('roots', '--pid', "$($script:automationProcess.Id)")
        $closeRoot = @($closeRoots.value | Where-Object rootId -ne $mainRoot) | Select-Object -First 1
    } while (-not $closeRoot -and $deadline.Elapsed.TotalSeconds -lt 15)
    if (-not $closeRoot) { throw 'Active queued work did not expose a shutdown confirmation.' }
    [void](Find-Node 'ShutdownChoiceWindow' $closeRoot.rootId)
    Invoke-NodeAction (Find-Node 'ShutdownExit' $closeRoot.rootId) $closeRoot.rootId 'Invoke' | Out-Null
    if (-not $script:automationProcess.WaitForExit(30000) -or $script:automationProcess.ExitCode -ne 0) {
        throw 'Automation process did not close cleanly.'
    }
    Wait-Bridge $script:automationProcess.Id $false
    $savedSettings = Get-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Raw | ConvertFrom-Json
    if ($savedSettings.showQueuePositionNumbers -ne $false) { throw 'Settings toggle was not persisted.' }
    if ($RangeSmoke) {
        $savedQueue = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
        if ($savedQueue.Count -ne 1 -or $savedQueue[0].requestedRange.start -ne '00:00:02' -or
            $savedQueue[0].requestedRange.end -ne '00:00:10') {
            throw 'Custom media range was not persisted after Automation queueing.'
        }
    }
    Write-Output "Automation smoke PASS (profile: $dataRoot)"
    }
}
finally {
    $env:MODERNTUBEDOWNLOADER_QA_ALL_SETTINGS = $previousAllSettings
    $env:MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL = $previousSkipProtocol
    $env:MODERNTUBEDOWNLOADER_DATA_ROOT = $previousDataRoot
    $env:MTD_FAKE_PLAYLIST_COUNT = $previousPlaylistCount
    foreach ($process in @($normalProcess, $script:automationProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(10000)) { $process.Kill(); $process.WaitForExit() }
        }
        if ($null -ne $process) { $process.Dispose() }
    }
    if ($PublishStartupSmoke) {
        $protocolRoot = 'Software\Classes\moderntubedownloader'
        $command = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($protocolRoot + '\shell\open\command')
        $currentCommand = if ($command) { $command.GetValue('') } else { $null }
        if ($command) { $command.Dispose() }
        if ($currentCommand -eq ('"' + $app + '" "%1"')) {
            if (-not $protocolSnapshot[0].Existed) {
                [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($protocolRoot, $false)
            } else {
                foreach ($snapshot in $protocolSnapshot) {
                    if (-not $snapshot.Existed) {
                        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($snapshot.Path, $false)
                    } else {
                        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($snapshot.Path)
                        try {
                            foreach ($name in $key.GetValueNames()) { $key.DeleteValue($name, $false) }
                            foreach ($value in $snapshot.Values) { $key.SetValue($value.Name, $value.Value, $value.Kind) }
                        } finally { $key.Dispose() }
                    }
                }
            }
            Write-Output 'Published smoke restored the prior public URI association. HKCU Run was not changed.'
        }
    }
}
