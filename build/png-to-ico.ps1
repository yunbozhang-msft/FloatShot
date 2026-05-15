# Convert square source PNG to multi-size .ico.
# Small sizes (16/24/32) crop & re-mask with rounded rect to remove the AI's
# outer highlight/shadow rim that becomes a "white halo" when downscaled
# against dark taskbar backgrounds. Large sizes (48+) keep the full original.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Source,
    [Parameter(Mandatory=$true)] [string]$OutPath,
    [string]$AssetsDir = $null,
    [double]$SmallInset = 0.12     # 12% inward crop to chop the AI's outer halo
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Source)) { throw "Source not found: $Source" }
$srcImg = [System.Drawing.Bitmap]::new($Source)

function New-CroppedRounded {
    param([int]$size, [bool]$inset)

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    if ($inset) {
        # Crop source: remove the outer rim that contains highlight/shadow stroke
        $srcSize = [Math]::Min($srcImg.Width, $srcImg.Height)
        $cut     = [int]($srcSize * $SmallInset)
        $cropRect = New-Object System.Drawing.Rectangle $cut, $cut, ($srcSize - $cut * 2), ($srcSize - $cut * 2)

        # Apply rounded mask so corners are clean transparent
        $r = [Math]::Max(2, [int]($size * 0.20))
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
        $path.AddArc($size - $r * 2, 0, $r * 2, $r * 2, 270, 90)
        $path.AddArc($size - $r * 2, $size - $r * 2, $r * 2, $r * 2, 0, 90)
        $path.AddArc(0, $size - $r * 2, $r * 2, $r * 2, 90, 90)
        $path.CloseFigure()
        $g.SetClip($path)

        $g.DrawImage($srcImg, (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
            $cropRect.X, $cropRect.Y, $cropRect.Width, $cropRect.Height,
            [System.Drawing.GraphicsUnit]::Pixel)
        $g.ResetClip()
    } else {
        $g.DrawImage($srcImg, 0, 0, $size, $size)
    }
    $g.Dispose()

    # Hard alpha threshold: any pixel with alpha < 250 -> fully transparent.
    # This kills the "halo" left by AI-generated outer drop shadows, which
    # otherwise becomes a visible white/grey rim against dark taskbars.
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $data.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        for ($i = 3; $i -lt $bytes.Length; $i += 4) {
            if ($bytes[$i] -lt 250) {
                $bytes[$i]     = 0
                $bytes[$i - 1] = 0
                $bytes[$i - 2] = 0
                $bytes[$i - 3] = 0
            }
        }
        [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    } finally {
        $bmp.UnlockBits($data)
    }

    return $bmp
}

# Inset all sizes to chop AI halo + force alpha threshold
$sizes = 16, 24, 32, 48, 64, 128, 256
$bitmaps = @{}
foreach ($s in $sizes) {
    $bitmaps[$s] = New-CroppedRounded -size $s -inset $true
}
$srcImg.Dispose()

# Optional per-size PNG export
if ($AssetsDir) {
    if (-not (Test-Path $AssetsDir)) { New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null }
    foreach ($s in $sizes) {
        $bitmaps[$s].Save((Join-Path $AssetsDir "icon-$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
}

# Build ICO with embedded PNGs
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)

$pngArr = @()
foreach ($s in $sizes) {
    $tmp = New-Object System.IO.MemoryStream
    $bitmaps[$s].Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngArr += , $tmp.ToArray()
    $tmp.Dispose()
}
$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $bytes = $pngArr[$i]
    $bw.Write([Byte]($s -band 0xFF)); $bw.Write([Byte]($s -band 0xFF))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$dataOffset)
    $dataOffset += $bytes.Length
}
foreach ($bytes in $pngArr) { $bw.Write($bytes) }

$dir = Split-Path $OutPath -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
foreach ($s in $sizes) { $bitmaps[$s].Dispose() }

Write-Host "ICO created: $OutPath ($((Get-Item $OutPath).Length) bytes)"
if ($AssetsDir) { Write-Host "PNGs:        $AssetsDir" }
