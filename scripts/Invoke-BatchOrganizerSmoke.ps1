# Runs inside Test-Automation.ps1's isolated profile/owned process. No real files,
# external downloads or global input injection. Pixels are captured from native HWNDs.
function Wait-WorkflowState([scriptblock]$condition, [string]$description) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 30)
    throw "Workflow state did not settle: $description"
}
function Capture-WorkflowDialog([string]$id, [string]$captionKey, [string]$name) {
    $root = Wait-ExperienceRoot $id
    $labels = Get-Content -LiteralPath (Join-Path $repo "ModernTubeDownloader/Resources/Languages/$Language.json") -Raw | ConvertFrom-Json
    $hwnd = [MtdAutomationWindowCloser]::FindOwnCaption($script:automationProcess.Id, $labels.$captionKey)
    if ($hwnd -eq [IntPtr]::Zero) { throw "Owned native dialog missing: $id" }
    if ($WindowWidth -gt 0 -and $WindowHeight -gt 0) {
        if (-not [MtdAutomationWindowCloser]::PlaceWindow($hwnd, $WindowWidth, $WindowHeight, $WindowLeft, $WindowTop)) { throw "Cannot place $id" }
    }
    $tree = Invoke-AutomationCli -CliArguments @('tree','--pid',"$($script:automationProcess.Id)",'--root',$root.rootId,'--depth','12')
    $tree | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath (Join-Path $dataRoot ($name + '-tree.json')) -Encoding utf8
    $size = [MtdAutomationWindowCloser]::OwnWindowSize($script:automationProcess.Id, $hwnd)
    Add-Type -AssemblyName System.Drawing
    $picture = [Drawing.Bitmap]::new($size[0],$size[1]); $graphics = [Drawing.Graphics]::FromImage($picture); $dc = $graphics.GetHdc()
    try { if (-not [MtdAutomationWindowCloser]::CaptureOwnPopup($script:automationProcess.Id,$hwnd,$dc)) { throw "Capture failed: $id" } }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try { $picture.Save((Join-Path $dataRoot ($name + '.png'))) } finally { $picture.Dispose() }
    @{ id=$id; processId=$script:automationProcess.Id; hwnd=$hwnd.ToInt64(); width=$size[0]; height=$size[1];
        dpi=[MtdAutomationWindowCloser]::GetDpiForWindow($hwnd); language=$Language; theme=$Theme; textScale=$AppTextScale;
        evidence='Native HWND PrintWindow; DEBUG semantic actions, not Windows UIA qualification' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot ($name + '-capture.json')) -Encoding utf8
    return $root.rootId
}

if ($BatchWorkflowSmoke) {
    $batchFile = Join-Path $dataRoot 'native-batch-list.txt'
    @('https://example.test/watch?id=batch001','https://example.test/watch?id=batch002','https://example.test/watch?id=batch001') |
        Set-Content -LiteralPath $batchFile -Encoding utf8
    Invoke-NodeAction (Find-Node 'NavOther' $mainRoot) $mainRoot 'Invoke' | Out-Null
    Invoke-NodeAction (Find-Node 'OtherOpenBatch' $mainRoot) $mainRoot 'Invoke' | Out-Null
    $batchRoot = if ($InterfaceMode -eq 'ModernDashboard') { $mainRoot } else { (Wait-ExperienceRoot 'BatchWindow').rootId }
    Invoke-NodeAction (Find-Node 'BatchFilePath' $batchRoot) $batchRoot 'SetValue' $batchFile | Out-Null
    Invoke-NodeAction (Find-Node 'BatchAutomatic' $batchRoot) $batchRoot 'Invoke' | Out-Null
    Wait-WorkflowState {
        $script:workflowQueue = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json)
        $script:workflowQueue.Count -eq 2 -and @($script:workflowQueue | Where-Object { $_.status -ne 6 -and $_.status -ne 'Completed' }).Count -eq 0
    } 'two real FakeTool downloads completed in canonical queue'
    $expected = Join-Path ($dataRoot + '-media') 'native-batch-list'
    foreach ($item in $script:workflowQueue) {
        if ($item.outputDirectory -ne $expected -or [IO.Path]::GetDirectoryName($item.finalFile) -ne $expected -or -not (Test-Path -LiteralPath $item.finalFile)) { throw 'Per-list media destination mismatch.' }
        if (-not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($item.finalFile, '.json')))) { throw 'Per-list JSON sidecar missing.' }
    }
    $originalIds = @($script:workflowQueue | ForEach-Object id | Sort-Object)
    Wait-Node 'BatchAutomatic' $batchRoot 'Enabled' | Out-Null
    Invoke-NodeAction (Find-Node 'BatchAutomatic' $batchRoot) $batchRoot 'Invoke' | Out-Null
    Wait-Node 'BatchAutomatic' $batchRoot 'Enabled' | Out-Null
    $after = @(Get-Content -LiteralPath (Join-Path $dataRoot 'queue.json') -Raw | ConvertFrom-Json)
    if (@(Compare-Object $originalIds @($after | ForEach-Object id | Sort-Object)).Count -ne 0) { throw 'Repeated one-click workflow changed canonical task identities.' }
    if ($InterfaceMode -eq 'ModernDashboard') { Capture-SmokeMain $mainRoot 'batch-workflow-completed' }
    else { [void](Capture-WorkflowDialog 'BatchWindow' 'Other.Batch.Title' 'batch-workflow-completed') }
    if ($AppTextScale -gt 100 -and $InterfaceMode -eq 'ModernDashboard') {
        Invoke-NodeAction (Find-Node 'BatchWorkflowStage' $batchRoot) $batchRoot 'ScrollIntoView' | Out-Null
        Capture-SmokeMain $mainRoot 'batch-workflow-large-text'
    }

    if ($OrganizerWorkflowSmoke) {
        Invoke-NodeAction (Find-Node 'BatchOrganize' $batchRoot) $batchRoot 'Invoke' | Out-Null
        $organizer = (Wait-ExperienceRoot 'OrganizerWindow').rootId
        $target = Join-Path ($dataRoot + '-media') 'organized-native-list'
        Wait-Node 'OrganizerDestination' $organizer 'Enabled' | Out-Null
        Invoke-NodeAction (Find-Node 'OrganizerDestination' $organizer) $organizer 'SetValue' $target | Out-Null
        Invoke-NodeAction (Find-Node 'OrganizerPlan' $organizer) $organizer 'Invoke' | Out-Null
        Wait-Node 'OrganizerPlan' $organizer 'Enabled' | Out-Null
        Invoke-NodeAction (Find-Node 'OrganizerSelectPage' $organizer) $organizer 'Invoke' | Out-Null
        Wait-Node 'OrganizerMove' $organizer 'Enabled' | Out-Null
        [void](Capture-WorkflowDialog 'OrganizerWindow' 'Organizer.Title' 'organizer-dry-run')
        if ($AppTextScale -gt 100) {
            Invoke-NodeAction (Find-Node 'OrganizerFilter' $organizer) $organizer 'ScrollIntoView' | Out-Null
            [void](Capture-WorkflowDialog 'OrganizerWindow' 'Organizer.Title' 'organizer-results-large-text')
            Invoke-NodeAction (Find-Node 'OrganizerDetails' $organizer) $organizer 'ScrollIntoView' | Out-Null
            [void](Capture-WorkflowDialog 'OrganizerWindow' 'Organizer.Title' 'organizer-inspector-large-text')
        }
        Invoke-NodeAction (Find-Node 'OrganizerMove' $organizer) $organizer 'Invoke' | Out-Null
        $confirm = (Wait-ExperienceRoot 'OrganizerConfirmMoveWindow').rootId
        Invoke-NodeAction (Find-Node 'OrganizerConfirmMoveConfirm' $confirm) $confirm 'Invoke' | Out-Null
        Wait-WorkflowState {
            $history = @(Get-Content -LiteralPath (Join-Path $dataRoot 'history.json') -Raw | ConvertFrom-Json)
            $history.Count -eq 2 -and @($history | Where-Object { [IO.Path]::GetDirectoryName($_.finalPath) -ne $target }).Count -eq 0
        } 'organizer moved selected media/sidecars and committed History paths'
        Wait-Node 'OrganizerUndo' $organizer 'Enabled' | Out-Null
        [void](Capture-WorkflowDialog 'OrganizerWindow' 'Organizer.Title' 'organizer-completed')
        Invoke-NodeAction (Find-Node 'OrganizerUndo' $organizer) $organizer 'Invoke' | Out-Null
        $confirm = (Wait-ExperienceRoot 'OrganizerConfirmJournalWindow').rootId
        Invoke-NodeAction (Find-Node 'OrganizerConfirmJournalConfirm' $confirm) $confirm 'Invoke' | Out-Null
        Wait-WorkflowState {
            $history = @(Get-Content -LiteralPath (Join-Path $dataRoot 'history.json') -Raw | ConvertFrom-Json)
            $history.Count -eq 2 -and @($history | Where-Object { [IO.Path]::GetDirectoryName($_.finalPath) -ne $expected }).Count -eq 0
        } 'verified undo restored History and media locations'
        [void](Capture-WorkflowDialog 'OrganizerWindow' 'Organizer.Title' 'organizer-undone')
        Invoke-NodeAction (Find-Node 'OrganizerClose' $organizer) $organizer 'Invoke' | Out-Null
    }
    if ($InterfaceMode -eq 'Classic') { Invoke-NodeAction (Find-Node 'BatchClose' $batchRoot) $batchRoot 'Invoke' | Out-Null }
}

if ($LocalePickerSmoke) {
    Invoke-NodeAction (Find-Node 'NavSettings' $mainRoot) $mainRoot 'Invoke' | Out-Null
    if ($InterfaceMode -eq 'ModernDashboard') { Invoke-NodeAction (Find-Node 'SettingsCategoryGeneral' $mainRoot) $mainRoot 'Invoke' | Out-Null }
    Invoke-NodeAction (Find-Node 'LanguageSelector' $mainRoot) $mainRoot 'Invoke' | Out-Null
    $picker = Capture-WorkflowDialog 'LocalePickerWindow' 'Settings.Language' 'locale-picker-all'
    Invoke-NodeAction (Find-Node 'LocaleSearch' $picker) $picker 'SetValue' $Language | Out-Null
    [void](Capture-WorkflowDialog 'LocalePickerWindow' 'Settings.Language' 'locale-picker-filtered')
    Invoke-NodeAction (Find-Node ('LocaleChoice-' + $Language) $picker) $picker 'Invoke' | Out-Null
    Wait-ExperienceRoot 'LocalePickerWindow' $false | Out-Null
    if ($Language -eq 'pl') {
        # Exercise an actual preference change and its canonical persistence,
        # then restore this lane's language before its final screenshots.
        foreach ($choice in @('en', 'pl')) {
            Invoke-NodeAction (Find-Node 'LanguageSelector' $mainRoot) $mainRoot 'Invoke' | Out-Null
            $choiceRoot = (Wait-ExperienceRoot 'LocalePickerWindow').rootId
            Invoke-NodeAction (Find-Node 'LocaleSearch' $choiceRoot) $choiceRoot 'SetValue' $choice | Out-Null
            Invoke-NodeAction (Find-Node ('LocaleChoice-' + $choice) $choiceRoot) $choiceRoot 'Invoke' | Out-Null
            Wait-ExperienceRoot 'LocalePickerWindow' $false | Out-Null
            Wait-WorkflowState { (Get-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Raw | ConvertFrom-Json).language -eq $choice } 'native language selection persisted'
        }
    }
    Capture-SmokeMain $mainRoot 'settings-language-selected'
    Invoke-NodeAction (Find-Node 'SettingsCategoryUpdates' $mainRoot) $mainRoot 'Invoke' | Out-Null
    Wait-Node 'ApplicationUpdateAtStartup' $mainRoot | Out-Null
    Capture-SmokeMain $mainRoot 'settings-startup-update-policy'
    Invoke-NodeAction (Find-Node 'ApplicationUpdateStartupResult' $mainRoot) $mainRoot 'ScrollIntoView' | Out-Null
    Capture-SmokeMain $mainRoot 'settings-startup-update-result'
}
@{ batch=[bool]$BatchWorkflowSmoke; organizer=[bool]$OrganizerWorkflowSmoke; picker=[bool]$LocalePickerSmoke;
    profile=$dataRoot; media=($dataRoot+'-media'); language=$Language; theme=$Theme; textScale=$AppTextScale;
    nativeWindowsUia='NOT EXECUTED'; physicalDpiMatrix='NOT EXECUTED' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'batch-organizer-smoke-result.json') -Encoding utf8
Write-Output "Batch/Organizer/Locale native smoke PASS; Batch=$BatchWorkflowSmoke Organizer=$OrganizerWorkflowSmoke Picker=$LocalePickerSmoke; $Language/$Theme/$AppTextScale; profile=$dataRoot"
