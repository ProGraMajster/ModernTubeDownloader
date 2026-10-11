[CmdletBinding()]
param([Parameter(Mandatory)][string] $ResultsDirectory)
$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter '*.trx' -File)
if ($files.Count -eq 0) { throw 'No TRX results were produced.' }
foreach ($file in $files) {
    [xml] $results = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $results.TestRun.ResultSummary.Counters
    if ([int]$counters.total -eq 0 -or [int]$counters.total -ne [int]$counters.passed) {
        throw "All tests must pass, with no skips: $($file.FullName)"
    }
}
