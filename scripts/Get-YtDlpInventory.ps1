[CmdletBinding()]
param([Parameter(Mandatory)][string]$YtDlpPath, [string]$OutputDirectory = 'artifacts/validation/candidate5/yt-dlp-inventory')
$ErrorActionPreference='Stop'
$tool=[IO.Path]::GetFullPath($YtDlpPath)
if(-not (Test-Path -LiteralPath $tool -PathType Leaf)){throw 'Managed yt-dlp executable was not found.'}
$version=(& $tool --version | Out-String).Trim()
if($LASTEXITCODE -ne 0){throw 'yt-dlp version failed.'}
$help=@(& $tool --help)
if($LASTEXITCODE -ne 0){throw 'yt-dlp help failed.'}
$category='General Options';$rows=[Collections.Generic.List[object]]::new()
foreach($line in $help){
    if($line -match '^  ([A-Z][^:]+):$'){$category=$Matches[1];continue}
    if($line -match '^    (-\S.*)$'){
        $syntax=($Matches[1] -split '\s{2,}',2)[0]
        $options=@([regex]::Matches($syntax,'(?<!\S)--[a-zA-Z0-9-]+') | ForEach-Object Value)
        $rows.Add([pscustomobject]@{Category=$category;Syntax=$syntax;Options=$options})
    }
}
$commit=Invoke-RestMethod 'https://api.github.com/repos/yt-dlp/yt-dlp/commits/master'
$directory=[IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $directory -Force)
# Generated public CLI inventory, not runtime application data or credentials.
$help | Set-Content -LiteralPath (Join-Path $directory 'help.txt') -Encoding utf8
$rows | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'options.json') -Encoding utf8
$evidence=[pscustomobject]@{Version=$version;BinarySha256=(Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash.ToLowerInvariant();
    UpstreamSha=$commit.sha;UpstreamDate=$commit.commit.committer.date;CapturedUtc=[DateTime]::UtcNow.ToString('O');
    Categories=@($rows.Category | Select-Object -Unique);OptionRows=$rows.Count;LongOptions=@($rows.Options | Select-Object -Unique).Count}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'evidence.json') -Encoding utf8
$evidence | ConvertTo-Json -Depth 5
