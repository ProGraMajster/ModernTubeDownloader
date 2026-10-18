param([Parameter(Mandatory)][string]$DataRoot, [Parameter(Mandatory)][array]$Queue, [Parameter(Mandatory)][array]$History)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($DataRoot)
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $root.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($root) -notmatch '^mtd-automation-smoke-[a-f0-9]{32}$') {
    throw 'Visual fixtures are restricted to the owned temporary automation profile.'
}
Add-Type -AssemblyName System.Drawing
$cache = Join-Path $root 'thumbnails'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
# Original geometric QA illustrations. No reference media or production assets are copied.
for ($i = 0; $i -lt [Math]::Min(6, $Queue.Count); $i++) {
    $url = "https://example.test/dashboard/fixture-$i.png"
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($url)))
    $bitmap = [Drawing.Bitmap]::new(960, 540)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $sky = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0), [Drawing.Point]::new(0,540),
        [Drawing.Color]::FromArgb(28 + $i * 8, 58, 94 + $i * 15), [Drawing.Color]::FromArgb(120, 175, 205))
    $mountain = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(36, 63, 77))
    $snow = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(225, 237, 244))
    $water = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(22, 94, 121))
    $ink = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $font = [Drawing.Font]::new('Segoe UI', 20)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.FillRectangle($sky, 0, 0, 960, 540)
        $graphics.FillPolygon($mountain, [Drawing.Point[]]@([Drawing.Point]::new(-60,390),[Drawing.Point]::new(250,90 + $i * 12),[Drawing.Point]::new(530,390)))
        $graphics.FillPolygon($mountain, [Drawing.Point[]]@([Drawing.Point]::new(330,390),[Drawing.Point]::new(670,40 + $i * 14),[Drawing.Point]::new(1030,390)))
        $graphics.FillPolygon($snow, [Drawing.Point[]]@([Drawing.Point]::new(570,145 + $i * 14),[Drawing.Point]::new(670,40 + $i * 14),[Drawing.Point]::new(772,145 + $i * 14)))
        $graphics.FillRectangle($water, 0, 390, 960, 150)
        $graphics.DrawString("SYNTHETIC QA · $($i + 1)", $font, $ink, 24, 478)
        $bitmap.Save((Join-Path $cache ($hash + '.img')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $font.Dispose(); $ink.Dispose(); $water.Dispose(); $snow.Dispose(); $mountain.Dispose(); $sky.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    foreach ($entry in @($Queue[$i], $History[$i])) { $entry.ThumbnailUrl = $url }
    $Queue[$i].DurationSeconds = @(754,497,3735,902,660,190)[$i]
}
$longIndex = [Math]::Min(5, $Queue.Count - 1)
$Queue[$longIndex].Title = 'Synthetic QA — bardzo długi tytuł filmu, Łódź, 日本語の長いタイトル, Unicode and a long descriptive suffix for overflow verification'
$Queue[$longIndex].ThumbnailUrl = $null
$History[$longIndex].Title = $Queue[$longIndex].Title
$History[$longIndex].ThumbnailUrl = $null
$History[2].Status = 'Failed'
$History[3].WasPartial = $true
