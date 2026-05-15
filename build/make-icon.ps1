# Generate FloatShot app icon (Fluent-style)
# - Squircle background (Win11 app icon style)
# - Single accent color (#0078D4 with subtle gradient)
# - Segoe Fluent Icons camera glyph centered
# Output: multi-size .ico (16, 24, 32, 48, 64, 128, 256) + per-size PNGs
[CmdletBinding()]
param(
    [string]$OutPath = ".\assets\floatshot.ico"
)

Add-Type -AssemblyName System.Drawing

function New-IconBitmap {
    param([int]$size)
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint  = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Squircle background (Win11 app icon corner radius ~22% of side)
    $r = [Math]::Max(2, [int]($size * 0.22))
    $rect = New-Object System.Drawing.RectangleF 0.0, 0.0, $size, $size
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Y, $r * 2, $r * 2, 270, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()

    # Subtle vertical gradient (top brighter, bottom Win11 accent #0078D4)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 38,  148, 234),
        [System.Drawing.Color]::FromArgb(255, 0,   95,  184),
        90.0)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # Inner highlight (1px white at top edge for "glass" feel) — only for >=48
    if ($size -ge 48) {
        $hi = New-Object System.Drawing.Drawing2D.GraphicsPath
        $hi.AddArc($rect.X + 1, $rect.Y + 1, ($r - 1) * 2, ($r - 1) * 2, 180, 90)
        $hi.AddLine([float]($r), 1.0, [float]($size - $r), 1.0)
        $hi.AddArc($rect.Right - $r * 2 - 1, $rect.Y + 1, ($r - 1) * 2, ($r - 1) * 2, 270, 90)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(80, 255, 255, 255)), 1.0
        $g.DrawPath($pen, $hi)
        $pen.Dispose()
        $hi.Dispose()
    }

    # Centered Segoe Fluent Icons camera (E722)
    # Pick a font size that fills ~55% of the icon
    $fontSize = [int]($size * 0.46)
    $iconFont = $null
    foreach ($name in 'Segoe Fluent Icons', 'Segoe MDL2 Assets') {
        try {
            $f = New-Object System.Drawing.Font($name, $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
            if ($f.FontFamily.Name -ieq $name) { $iconFont = $f; break }
            $f.Dispose()
        } catch { }
    }
    if (-not $iconFont) {
        $iconFont = New-Object System.Drawing.Font('Segoe UI Symbol', $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    }

    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment     = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.DrawString([char]0xE722, $iconFont, $white, $rect, $sf)
    $white.Dispose()
    $iconFont.Dispose()

    $g.Dispose()
    return $bmp
}

# Generate sizes
$sizes = 16, 24, 32, 48, 64, 128, 256
$bitmaps = @{}
foreach ($s in $sizes) { $bitmaps[$s] = New-IconBitmap -size $s }

# PNGs for documentation
$assetsDir = Split-Path $OutPath -Parent
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null }
foreach ($s in $sizes) {
    $bitmaps[$s].Save((Join-Path $assetsDir "icon-$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}

# Compose multi-size ICO
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$pngBytesArr = @()
foreach ($s in $sizes) {
    $tmp = New-Object System.IO.MemoryStream
    $bitmaps[$s].Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesArr += , $tmp.ToArray()
    $tmp.Dispose()
}

$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bytes = $pngBytesArr[$i]
    $bw.Write([Byte]($s -band 0xFF))
    $bw.Write([Byte]($s -band 0xFF))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$dataOffset)
    $dataOffset += $bytes.Length
}
foreach ($bytes in $pngBytesArr) { $bw.Write($bytes) }

[System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
foreach ($s in $sizes) { $bitmaps[$s].Dispose() }

Write-Host "ICO created: $OutPath ($((Get-Item $OutPath).Length) bytes)"
