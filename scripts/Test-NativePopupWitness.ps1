# Shared by narrow popup scenarios. A foreign foreground transition is evidence
# of interference, not a claim about whether a person or another tool caused it.
function Start-NativePopupWitness {
    if ($script:popupWitnessStarted) { return }
    if (-not ('MtdNativePopupWitness' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'NativePopupWitness.cs') }
    # The approved controlled run starts with one explicit activation. From the
    # next line of measurement onward the observer never recovers stolen focus.
    $before = [MtdNativePopupWitness]::FocusSnapshot($script:automationMainWindow)
    $activated = [MtdNativePopupWitness]::ActivateOwnedMain($script:automationProcess.Id, $script:automationMainWindow)
    $after = [MtdNativePopupWitness]::FocusSnapshot($script:automationMainWindow)
    @{ action='Single owned-main activation before observation'; activated=$activated; before=$before; after=$after;
        evidence=[MtdNativePopupWitness]::LastPreflight } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $dataRoot 'native-popup-preflight.json') -Encoding utf8
    if (-not $activated) {
        @{status='INCONCLUSIVE'; assertionsPassed=$false; failure='Controlled preflight activation denied.'} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'native-popup-qualification.json') -Encoding utf8
        throw 'INCONCLUSIVE: controlled preflight could not activate the owned main HWND.'
    }
    [MtdNativePopupWitness]::Start($script:automationProcess.Id, $script:automationMainWindow,
        (Join-Path $dataRoot 'native-popup-events.jsonl'))
    $script:popupWitnessStarted = $true
}
function Assert-NativePopupIsolation {
    if ([MtdNativePopupWitness]::Failure) { throw ('INCONCLUSIVE: ' + [MtdNativePopupWitness]::Failure) }
    if ([MtdNativePopupWitness]::Interfered) { throw 'INTERFERED: an unplanned foreground owner was observed; native qualification is inconclusive.' }
}
function Complete-NativePopupWitness([bool]$passed, [string]$failure = '') {
    if (-not $script:popupWitnessStarted) { return }
    [MtdNativePopupWitness]::Stop()
    $script:popupWitnessStarted = $false
    $interfered = [MtdNativePopupWitness]::Interfered
    $observerFailure = [MtdNativePopupWitness]::Failure
    $events = [MtdNativePopupWitness]::EventCount
    $status = if ($interfered) { 'INTERFERED' } elseif ($observerFailure -or $failure -like 'INCONCLUSIVE:*') { 'INCONCLUSIVE' } elseif ($passed) { 'PASS' } else { 'FAIL' }
    @{ status=$status; assertionsPassed=$passed; failure=$failure; observerFailure=$observerFailure; events=$events;
        activationSource='Native WinEvent foreground ownership transitions; not managed framework callbacks';
        privacy='Only HWND, PID, native visibility and event identifiers; no foreign captions or content' } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dataRoot 'native-popup-qualification.json') -Encoding utf8
    if ($passed -and $status -ne 'PASS') { throw "$status`: native popup qualification is not a PASS; witnesses preserved." }
}
