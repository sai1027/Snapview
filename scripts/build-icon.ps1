# Render the native vector mark, then convert it to Windows ICO sizes.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$drawingStream = [IO.File]::OpenRead((Join-Path $projectRoot 'Assets\Snapview.drawing.xml'))
try { $drawing = [Windows.Markup.XamlReader]::Load($drawingStream) } finally { $drawingStream.Dispose() }
$visual = [Windows.Media.DrawingVisual]::new()
$context = $visual.RenderOpen()
$context.DrawImage($drawing, [Windows.Rect]::new(0, 0, 512, 512))
$context.Close()
$rendered = [Windows.Media.Imaging.RenderTargetBitmap]::new(512, 512, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$rendered.Render($visual)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rendered))
$pngStream = [IO.File]::Create((Join-Path $projectRoot 'Assets\Snapview.png'))
try { $encoder.Save($pngStream) } finally { $pngStream.Dispose() }
$source = [Drawing.Image]::FromFile((Join-Path $projectRoot 'Assets\Snapview.png'))
function Convert-IconFrame([int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $Size, $Size), 0, 0, $source.Width, $source.Height, [Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
}
try {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = @($sizes | ForEach-Object { Convert-IconFrame $_ })
    $file = [IO.File]::Create((Join-Path $projectRoot 'Assets\Snapview.ico'))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally { $writer.Dispose(); $file.Dispose() }
    [IO.File]::WriteAllBytes((Join-Path $projectRoot 'Assets\Snapview-mark.png'), (Convert-IconFrame 96))
}
finally { $source.Dispose() }
Write-Output 'Updated nine ICO sizes (16–256px) and the 96px title-bar image.'
