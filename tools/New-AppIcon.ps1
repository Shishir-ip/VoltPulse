# Generates the original VoltPulse application icon (a lightning bolt over a pulse line on a
# dark rounded square) in all required sizes and writes a multi-resolution .ico plus PNG previews.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-AppIcon.ps1
param(
    [string]$OutDir = "$PSScriptRoot\..\src\VoltPulse.App\Assets"
)

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $scale = $size / 256.0

        # Rounded-square background with a subtle vertical gradient
        $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
        $radius = [int](56 * $scale)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $radius * 2
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
            [System.Drawing.Color]::FromArgb(255, 21, 32, 38),
            [System.Drawing.Color]::FromArgb(255, 12, 16, 20), 90)
        $g.FillPath($gradient, $path)
        $g.DrawPath((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 255, 255, 255), (2 * $scale))), $path)

        # Lightning bolt, teal-to-orange gradient fill
        $bx = @(0.615, 0.300, 0.470, 0.365, 0.700, 0.520)
        $by = @(0.095, 0.560, 0.560, 0.905, 0.405, 0.405)
        $bolt = New-Object System.Drawing.Drawing2D.GraphicsPath
        for ($i = 0; $i -lt 6; $i++) {
            $pt = New-Object System.Drawing.PointF(($bx[$i] * $size), ($by[$i] * $size))
            if ($i -eq 0) { $bolt.StartFigure() } else { $bolt.AddLine($prev, $pt) }
            $prev = $pt
        }
        $bolt.CloseFigure()
        $boltGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
            [System.Drawing.Color]::FromArgb(255, 0, 191, 165),
            [System.Drawing.Color]::FromArgb(255, 255, 145, 0), 35)
        $g.FillPath($boltGradient, $bolt)

        # White pulse line across the lower third
        $pulseX = @(0.10, 0.30, 0.38, 0.48, 0.56, 0.90)
        $pulseY = @(0.78, 0.78, 0.62, 0.90, 0.74, 0.74)
        $pts = New-Object 'System.Drawing.PointF[]' 6
        for ($i = 0; $i -lt 6; $i++) {
            $pts[$i] = New-Object System.Drawing.PointF(($pulseX[$i] * $size), ($pulseY[$i] * $size))
        }
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 255, 255, 255), (13 * $scale))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLines($pen, $pts)
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

function Get-BmpIcoBytes([System.Drawing.Bitmap]$bmp) {
    # BMP frame: BITMAPINFOHEADER (height doubled) + bottom-up BGRA pixels + AND mask
    $size = $bmp.Width
    $headerSize = 40
    $pixelData = $size * $size * 4
    $maskRow = [Math]::Ceiling($size / 32.0) * 4
    $maskData = $maskRow * $size
    $bytes = New-Object byte[] ($headerSize + $pixelData + $maskData)

    $bytes[0] = 40
    [BitConverter]::GetBytes([int]$size).CopyTo($bytes, 4)
    [BitConverter]::GetBytes([int]($size * 2)).CopyTo($bytes, 8)
    [BitConverter]::GetBytes([uint16]1).CopyTo($bytes, 12)
    [BitConverter]::GetBytes([uint16]32).CopyTo($bytes, 14)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        for ($row = 0; $row -lt $size; $row++) {
            [System.Runtime.InteropServices.Marshal]::Copy(
                [IntPtr]($data.Scan0.ToInt64() + $row * $stride),
                $bytes, $headerSize + ($size - 1 - $row) * $size * 4, $size * 4)
        }
    }
    finally {
        $bmp.UnlockBits($data)
    }
    return ,$bytes
}

function Get-PngIcoBytes([System.Drawing.Bitmap]$bmp) {
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $frames += ,@($size, (Get-PngIcoBytes $bmp))
    $preview = "$OutDir\icon-$size.png"
    $bmp.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ICONDIR + entries + data (PNG frames, valid on Windows Vista and later)
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $size = $frame[0]
    $data = $frame[1]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$data.Length)
    $writer.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($frame in $frames) {
    $writer.Write($frame[1])
}
$writer.Flush()

$icoPath = Join-Path $OutDir "AppIcon.ico"
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
