[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourcePath,
    [string] $OutputDirectory = ""
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'ModernTubeDownloader\Assets'
}

$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Icon source was not found at '$SourcePath'."
}

[void] (New-Item -ItemType Directory -Path $OutputDirectory -Force)
$pngPath = Join-Path $OutputDirectory 'AppIcon.png'
$icoPath = Join-Path $OutputDirectory 'AppIcon.ico'

Add-Type -AssemblyName System.Drawing
$drawingReferences = @(
    [System.Drawing.Bitmap].Assembly.Location,
    [System.Drawing.Point].Assembly.Location,
    (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'),
    (Join-Path $PSHOME 'System.Private.Windows.Core.dll'),
    (Join-Path $PSHOME 'System.Collections.dll'),
    [object].Assembly.Location
)
Add-Type -ReferencedAssemblies $drawingReferences @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class ModernTubeDownloaderIconBuilder
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    public static void Build(string sourcePath, string pngPath, string icoPath)
    {
        using var source = new Bitmap(sourcePath);
        if (source.Width != source.Height || source.Width < 256)
            throw new InvalidOperationException("The icon source must be square and at least 256x256 pixels.");

        using var transparent = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(transparent))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        MakeConnectedDarkBackgroundTransparent(transparent);
        transparent.Save(pngPath, ImageFormat.Png);

        var frames = new byte[Sizes.Length][];
        for (var index = 0; index < Sizes.Length; index++)
            frames[index] = EncodePng(transparent, Sizes[index]);
        using var output = File.Create(icoPath);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);

        var offset = 6 + frames.Length * 16;
        for (var index = 0; index < frames.Length; index++)
        {
            var size = Sizes[index];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frames[index].Length);
            writer.Write(offset);
            offset += frames[index].Length;
        }

        foreach (var frame in frames)
            writer.Write(frame);
    }

    private static byte[] EncodePng(Bitmap source, int size)
    {
        using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        resized.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(resized))
        using (var attributes = new ImageAttributes())
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, size, size),
                0,
                0,
                source.Width,
                source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        using var stream = new MemoryStream();
        resized.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void MakeConnectedDarkBackgroundTransparent(Bitmap bitmap)
    {
        const byte threshold = 64;
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var visited = new bool[bitmap.Width * bitmap.Height];
            var queue = new int[bitmap.Width * bitmap.Height];
            var queueRead = 0;
            var queueWrite = 0;

            void TryEnqueue(int x, int y)
            {
                var point = y * bitmap.Width + x;
                if (visited[point])
                    return;

                var pixel = y * data.Stride + x * 4;
                var maximum = Math.Max(bytes[pixel], Math.Max(bytes[pixel + 1], bytes[pixel + 2]));
                if (maximum > threshold)
                    return;

                visited[point] = true;
                queue[queueWrite++] = point;
            }

            for (var x = 0; x < bitmap.Width; x++)
            {
                TryEnqueue(x, 0);
                TryEnqueue(x, bitmap.Height - 1);
            }
            for (var y = 0; y < bitmap.Height; y++)
            {
                TryEnqueue(0, y);
                TryEnqueue(bitmap.Width - 1, y);
            }

            while (queueRead < queueWrite)
            {
                var point = queue[queueRead++];
                var x = point % bitmap.Width;
                var y = point / bitmap.Width;
                if (x > 0) TryEnqueue(x - 1, y);
                if (x + 1 < bitmap.Width) TryEnqueue(x + 1, y);
                if (y > 0) TryEnqueue(x, y - 1);
                if (y + 1 < bitmap.Height) TryEnqueue(x, y + 1);
            }

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (!visited[y * bitmap.Width + x])
                        continue;

                    var pixel = y * data.Stride + x * 4;
                    var maximum = Math.Max(bytes[pixel], Math.Max(bytes[pixel + 1], bytes[pixel + 2]));
                    bytes[pixel + 3] = maximum <= 2
                        ? (byte)0
                        : (byte)Math.Min(255, (maximum - 2) * 255 / (threshold - 2));
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
'@

[ModernTubeDownloaderIconBuilder]::Build($SourcePath, $pngPath, $icoPath)

[pscustomobject]@{
    Source = $SourcePath
    Png = $pngPath
    Ico = $icoPath
    Sizes = '16, 24, 32, 48, 64, 128, 256'
}
