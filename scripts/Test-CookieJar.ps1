[CmdletBinding()]
param([Parameter(Mandatory)][string]$YtDlpPath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$library=Join-Path $repo 'ModernTubeDownloader.Authentication/bin/Debug/net10.0-windows/ModernTubeDownloader.Authentication.dll'
[void][Reflection.Assembly]::LoadFrom($library)
$tempBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot=Join-Path $tempBase ('mtd cookie parser '+[Guid]::NewGuid().ToString('N'))
$jar=$null
try {
    $store=[ModernTubeDownloader.Authentication.CookieStore]::new($testRoot)
    $cookie=[ModernTubeDownloader.Authentication.BrowserCookie]::new()
    $cookie.Domain='.example.com';$cookie.Name='synthetic_qa';$cookie.Value=[Guid]::NewGuid().ToString('N')
    $cookie.Session=$true;$cookie.Secure=$true;$cookie.HttpOnly=$true
    $message=[ModernTubeDownloader.Authentication.CookieMessage]::new()
    $message.Type='cookieSnapshot';$message.ProfileId=[Guid]::NewGuid().ToString();$message.Browser='chrome';$message.SnapshotVersion=1
    $message.GeneratedAtUtc=[DateTimeOffset]::UtcNow;$message.Scopes=@('example.com');$message.Cookies=@($cookie)
    [void]$store.Apply('chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/',$message)
    $jar=$store.CreateJar('https://example.com/')
    $original=[IO.File]::ReadAllText($jar.Path)
    if($original.Contains("`r") -or -not $original.StartsWith("# Netscape HTTP Cookie File`n#HttpOnly_")){throw 'Invalid generated Netscape format.'}
    $output=@(& $YtDlpPath --ignore-config --cookies $jar.Path --simulate --skip-download --socket-timeout 15 -- 'https://example.com/' 2>&1 | ForEach-Object ToString)
    $exitCode=$LASTEXITCODE
    $text=$output -join "`n"
    if($text.Contains($cookie.Value)){throw 'Cookie value appeared in process output.'}
    if($text -match '(?i)(invalid cookie|failed to load cookie|skipping cookie|does not look like a netscape|LoadError)'){throw 'yt-dlp cookie parser warning.'}
    # example.com is deliberately unsupported media. Jar rewrite is evidence of actual load/save,
    # not just an early --version/--list-extractors path that never initializes the cookiejar.
    $rewritten=[IO.File]::ReadAllText($jar.Path)
    if(-not $rewritten.Contains('generated') -or -not $rewritten.Contains($cookie.Value)){throw 'yt-dlp did not load/save the generated jar.'}
    if($exitCode -ne 1 -or $text -notmatch 'Unsupported URL'){throw 'Unexpected public parser-test result.'}
    $jarPath=$jar.Path;$jar.Dispose();$jar=$null
    if(Test-Path -LiteralPath $jarPath){throw 'Job jar was not cleaned.'}
    Write-Output 'Managed yt-dlp Netscape parser PASS: LF, HttpOnly, session expiry, path with spaces, actual jar load/save, no parser warning, no value in output, cleanup. Media extraction intentionally unsupported (example.com).'
}
finally {
    if($jar){$jar.Dispose()}
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(-not $resolved.StartsWith($tempBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^mtd cookie parser [a-f0-9]{32}$'){throw 'Refusing cleanup outside the exact synthetic QA directory.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
