# Renders the padlock application icon as a multi-size .ico file.
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath([single] $left, [single] $top, [single] $width, [single] $height, [single] $radius) {
    $diameter = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($left, $top, $diameter, $diameter, 180, 90)
    $path.AddArc($left + $width - $diameter, $top, $diameter, $diameter, 270, 90)
    $path.AddArc($left + $width - $diameter, $top + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($left, $top + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws on a 16-unit grid so every size shares the same proportions.
function New-IconBitmap([int] $size) {
    $unit = $size / 16.0
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $lightBlue = [System.Drawing.Color]::FromArgb(59, 130, 246)
    $darkBlue = [System.Drawing.Color]::FromArgb(29, 78, 216)
    $tile = New-RoundedRectanglePath (0.5 * $unit) (0.5 * $unit) (15 * $unit) (15 * $unit) (3.2 * $unit)
    $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $size), $lightBlue, $darkBlue
    $graphics.FillPath($tileBrush, $tile)

    $shacklePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (1.7 * $unit)
    $shackle = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shackle.AddLine((5.2 * $unit), (8.0 * $unit), (5.2 * $unit), (6.0 * $unit))
    $shackle.AddArc((5.2 * $unit), (3.2 * $unit), (5.6 * $unit), (5.6 * $unit), 180, 180)
    $shackle.AddLine((10.8 * $unit), (6.0 * $unit), (10.8 * $unit), (8.0 * $unit))
    $graphics.DrawPath($shacklePen, $shackle)

    $body = New-RoundedRectanglePath (3.4 * $unit) (7.4 * $unit) (9.2 * $unit) (6.6 * $unit) (1.3 * $unit)
    $graphics.FillPath([System.Drawing.Brushes]::White, $body)

    $keyholeBrush = New-Object System.Drawing.SolidBrush $darkBlue
    $graphics.FillEllipse($keyholeBrush, (7.0 * $unit), (9.0 * $unit), (2.0 * $unit), (2.0 * $unit))
    $graphics.FillRectangle($keyholeBrush, (7.5 * $unit), (10.2 * $unit), (1.0 * $unit), (2.2 * $unit))

    $keyholeBrush.Dispose(); $body.Dispose(); $shackle.Dispose(); $shacklePen.Dispose(); $tileBrush.Dispose(); $tile.Dispose()
    $graphics.Dispose()
    return $bitmap
}

# Small sizes are stored as classic 32-bit DIBs, which every icon reader accepts.
function ConvertTo-IconBitmapBytes([System.Drawing.Bitmap] $bitmap) {
    $size = $bitmap.Width
    $bounds = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $pixelData = $bitmap.LockBits($bounds, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($pixelData.Stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($pixelData.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($pixelData)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    # BITMAPINFOHEADER; icon DIBs declare double height to cover the color rows plus the AND mask.
    $writer.Write([UInt32]40); $writer.Write([Int32]$size); $writer.Write([Int32]($size * 2))
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]0); $writer.Write([UInt32]0)
    $writer.Write([Int32]0); $writer.Write([Int32]0); $writer.Write([UInt32]0); $writer.Write([UInt32]0)
    for ($row = $size - 1; $row -ge 0; $row--) {
        $writer.Write($pixels, $row * $pixelData.Stride, $size * 4)
    }
    $maskRowBytes = [int][Math]::Ceiling($size / 32.0) * 4
    $writer.Write((New-Object byte[] ($maskRowBytes * $size)))
    $writer.Flush()
    return , $stream.ToArray()
}

$frames = @()
foreach ($size in @(256, 48, 32, 24, 16)) {
    $bitmap = New-IconBitmap $size
    if ($size -ge 256) {
        $pngStream = New-Object System.IO.MemoryStream
        $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $imageBytes = $pngStream.ToArray()
    }
    else {
        $imageBytes = ConvertTo-IconBitmapBytes $bitmap
    }
    $bitmap.Dispose()
    $frames += @{ Size = $size; Bytes = $imageBytes }
}

$iconStream = New-Object System.IO.MemoryStream
$iconWriter = New-Object System.IO.BinaryWriter $iconStream
$iconWriter.Write([UInt16]0); $iconWriter.Write([UInt16]1); $iconWriter.Write([UInt16]$frames.Count)
$imageOffset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    # A dimension byte of 0 means 256 pixels.
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $iconWriter.Write([byte]$dimension); $iconWriter.Write([byte]$dimension); $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
    $iconWriter.Write([UInt16]1); $iconWriter.Write([UInt16]32)
    $iconWriter.Write([UInt32]$frame.Bytes.Length); $iconWriter.Write([UInt32]$imageOffset)
    $imageOffset += $frame.Bytes.Length
}
foreach ($frame in $frames) {
    $iconWriter.Write([byte[]]$frame.Bytes)
}
$iconWriter.Flush()

$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
[System.IO.File]::WriteAllBytes($resolvedOutputPath, $iconStream.ToArray())
Write-Host "Wrote $resolvedOutputPath"
