Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # ---- Rounded square background (radius scales with size) ----
    $r = [float]($size * 0.22)
    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2.0
    $bgPath.AddArc(0.0, 0.0, $d, $d, 180.0, 90.0)
    $bgPath.AddArc($size - $d, 0.0, $d, $d, 270.0, 90.0)
    $bgPath.AddArc($size - $d, $size - $d, $d, $d, 0.0, 90.0)
    $bgPath.AddArc(0.0, $size - $d, $d, $d, 90.0, 90.0)
    $bgPath.CloseFigure()

    $gradBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 0.0, 0.0),
        (New-Object System.Drawing.PointF ([float]$size), ([float]$size)),
        [System.Drawing.Color]::FromArgb(255, 37, 99, 235),
        [System.Drawing.Color]::FromArgb(255, 15, 23, 42))
    $g.FillPath($gradBrush, $bgPath)
    $gradBrush.Dispose()

    # ---- Bar chart ----
    $marginX     = [float]($size * 0.22)
    $marginTop   = [float]($size * 0.30)
    $marginBot   = [float]($size * 0.22)
    $chartW      = [float]($size - $marginX * 2.0)
    $chartH      = [float]($size - $marginTop - $marginBot)
    $barCount    = 4
    $gap         = [float]($chartW * 0.08)
    $barW        = [float](($chartW - $gap * ($barCount - 1)) / $barCount)
    $heights     = @(0.55, 0.85, 0.32, 0.70)   # bar index 2 is the "weak" bar
    $weakIndex   = 2

    $whiteBar  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 255, 255))
    $orangeBar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 249, 115, 22))

    # Top corner radius for bars
    $barCorner = [float]([Math]::Max(2.0, $barW * 0.22))

    for ($i = 0; $i -lt $barCount; $i++) {
        $h = [float]($chartH * $heights[$i])
        $x = [float]($marginX + $i * ($barW + $gap))
        $y = [float]($size - $marginBot - $h)

        $cd = [float]([Math]::Min($barCorner * 2.0, [Math]::Min($h, $barW)))
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        # top-left arc
        $path.AddArc($x, $y, $cd, $cd, 180.0, 90.0)
        # top-right arc
        $path.AddArc($x + $barW - $cd, $y, $cd, $cd, 270.0, 90.0)
        # right side down
        $path.AddLine($x + $barW, $y + $cd / 2.0, $x + $barW, $y + $h)
        # bottom
        $path.AddLine($x + $barW, $y + $h, $x, $y + $h)
        # left side up
        $path.AddLine($x, $y + $h, $x, $y + $cd / 2.0)
        $path.CloseFigure()

        if ($i -eq $weakIndex) {
            $g.FillPath($orangeBar, $path)
        } else {
            $g.FillPath($whiteBar, $path)
        }
        $path.Dispose()
    }

    $whiteBar.Dispose()
    $orangeBar.Dispose()

    # ---- Magnifying glass over the weak bar ----
    $weakX = [float]($marginX + $weakIndex * ($barW + $gap))
    $weakTopY = [float]($size - $marginBot - $chartH * $heights[$weakIndex])

    # Center the lens on the weak bar's top area
    $lensR = [float]($size * 0.17)
    $lensCx = [float]($weakX + $barW / 2.0)
    $lensCy = [float]($weakTopY - $lensR * 0.15)
    if ($lensCy - $lensR -lt $size * 0.06) {
        $lensCy = [float]($size * 0.06 + $lensR)
    }

    $strokeW = [float]([Math]::Max(2.0, $size * 0.04))

    # White inner fill so it reads on dark bg
    $lensFill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 255, 255, 255))
    $g.FillEllipse($lensFill, $lensCx - $lensR, $lensCy - $lensR, $lensR * 2.0, $lensR * 2.0)
    $lensFill.Dispose()

    $lensPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $strokeW
    $lensPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $lensPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawEllipse($lensPen, $lensCx - $lensR, $lensCy - $lensR, $lensR * 2.0, $lensR * 2.0)

    # Handle: 45° down-right from the lens edge
    $angle = [Math]::PI / 4.0
    $handleStartX = [float]($lensCx + [Math]::Cos($angle) * $lensR)
    $handleStartY = [float]($lensCy + [Math]::Sin($angle) * $lensR)
    $handleLen = [float]($lensR * 0.95)
    $handleEndX = [float]($handleStartX + [Math]::Cos($angle) * $handleLen)
    $handleEndY = [float]($handleStartY + [Math]::Sin($angle) * $handleLen)
    $handlePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ($strokeW * 1.25)
    $handlePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handlePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($handlePen, $handleStartX, $handleStartY, $handleEndX, $handleEndY)

    $lensPen.Dispose()
    $handlePen.Dispose()
    $g.Dispose()
    return $bmp
}

# ---- Render at the standard Windows icon sizes ----
$sizes = @(256, 128, 64, 48, 32, 16)
$bitmaps = @()
foreach ($s in $sizes) {
    Write-Host "Rendering $s x $s..."
    $bitmaps += ,(New-IconBitmap $s)
}

# Save a preview PNG of the 256 version (handy for README / store listings)
$assetsDir = Join-Path $PSScriptRoot '..\src\App\Assets'
$assetsDir = [System.IO.Path]::GetFullPath($assetsDir)
$pngPath = Join-Path $assetsDir 'app-icon.png'
$bitmaps[0].Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $pngPath"

# ---- Build a multi-resolution .ico (each image stored as PNG inside the .ico) ----
$pngs = @()
foreach ($bmp in $bitmaps) {
    $pms = New-Object System.IO.MemoryStream
    $bmp.Save($pms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$pms.ToArray()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$count = $sizes.Length
$bw.Write([UInt16]0)            # reserved
$bw.Write([UInt16]1)            # type 1 = icon
$bw.Write([UInt16]$count)

$dataOffset = 6 + $count * 16
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    # 256 is encoded as 0 in the byte field per ICO spec
    $wb = if ($s -ge 256) { 0 } else { $s }
    $hb = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$wb)
    $bw.Write([byte]$hb)
    $bw.Write([byte]0)              # palette colors
    $bw.Write([byte]0)              # reserved
    $bw.Write([UInt16]1)            # planes
    $bw.Write([UInt16]32)           # bit count
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$dataOffset)
    $dataOffset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()

$icoPath = Join-Path $assetsDir 'app-icon.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
Write-Host "Wrote $icoPath ($([math]::Round($ms.Length/1024,1)) KB)"

foreach ($bmp in $bitmaps) { $bmp.Dispose() }
