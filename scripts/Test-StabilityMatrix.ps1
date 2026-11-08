param(
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$FixtureManifest,
    [Parameter(Mandatory)][string]$EvidenceRoot
)
# Sequential, fresh-process qualification. Each manifest entry names an isolated
# synthetic fixture and arguments for Test-Performance. Failures are retained, never retried.
$ErrorActionPreference = 'Stop'
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'Refusing to overwrite earlier matrix evidence.' }
New-Item -ItemType Directory -Path $EvidenceRoot | Out-Null
$matrix = @(Get-Content -LiteralPath $FixtureManifest -Raw | ConvertFrom-Json)
Copy-Item -LiteralPath $FixtureManifest -Destination (Join-Path $EvidenceRoot 'matrix.json')
$results = [Collections.Generic.List[object]]::new()
$previousAudit = $env:MTD_RENDER_AUDIT
$env:MTD_RENDER_AUDIT = '1'
try {
    foreach ($entry in $matrix) {
        if ($entry.name -notmatch '^[a-z0-9-]+$') { throw 'Unsafe scenario name.' }
        $arguments = @{ ApplicationPath = $ApplicationPath; Fixture = $entry.fixture;
            Evidence = (Join-Path $EvidenceRoot $entry.name); RequireBitmapRelease = $true; RequirePaging = $true }
        foreach ($property in $entry.arguments.psobject.Properties) { $arguments[$property.Name] = $property.Value }
        $result = 'PASS'; $failure = $null
        try {
            $global:LASTEXITCODE = 0
            & (Join-Path $PSScriptRoot 'Test-Performance.ps1') @arguments *> (Join-Path $EvidenceRoot ($entry.name + '.log'))
            if ($LASTEXITCODE -ne 0) { throw "Qualification failed with exit code $LASTEXITCODE" }
            $qualification = Get-Content -LiteralPath (Join-Path $arguments.Evidence 'qualification.json') -Raw | ConvertFrom-Json
            if ($qualification.result -ne 'PASS') { throw 'Qualification did not report PASS.' }
        }
        catch { $result = 'FAIL'; $failure = $_.Exception.Message }
        $results.Add(@{ name = $entry.name; result = $result; error = $failure })
        $results.ToArray() | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $EvidenceRoot 'results.json')
        Write-Output ($entry.name + ': ' + $result)
    }
}
finally { $env:MTD_RENDER_AUDIT = $previousAudit }
if (@($results | Where-Object result -eq 'FAIL').Count) { throw 'Native matrix contains failures. Original evidence retained.' }
