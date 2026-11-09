<#
.SYNOPSIS
  Generates app\Assets\app.ico (PNG-compressed, 16-256 px). The icon is the widget's look: a dark rounded card with
  a white temperature line, an amber peak and a "now" dot. Re-run only if you want to change the artwork.
#>
[CmdletBinding()]
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app\Assets\app.ico'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$size

    # rounded dark card
    $r = $s * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $m = $s * 0.04; $w = $s - 2 * $m
    $path.AddArc($m, $m, $r, $r, 180, 90); $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90); $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 16, 20, 24))), $path)
    $borderPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([float][Math]::Max(1, $s * 0.012))
    $g.DrawPath($borderPen, $path)

    # graph line (x, y in 0..1 of the card)
    $pts = @(@(0.14, 0.66), @(0.28, 0.58), @(0.38, 0.70), @(0.50, 0.42), @(0.58, 0.27), @(0.66, 0.46), @(0.78, 0.40), @(0.86, 0.50))
    $P = $pts | % { New-Object System.Drawing.PointF ([float]($_[0] * $s)), ([float]($_[1] * $s)) }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float][Math]::Max(1.5, $s * 0.055))
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]$P)

    # amber peak segment (above the "70" level) and the "now" dot
    $amber = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 179, 64)), ([float][Math]::Max(1.5, $s * 0.055))
    $amber.LineJoin = 'Round'; $amber.StartCap = 'Round'; $amber.EndCap = 'Round'
    $g.DrawLines($amber, [System.Drawing.PointF[]]@($P[3], $P[4], $P[5]))
    $d = $s * 0.075
    $g.FillEllipse([System.Drawing.Brushes]::White, [float]($P[7].X - $d / 2), [float]($P[7].Y - $d / 2), [float]$d, [float]$d)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { , (New-IconPng $sz) }

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)      # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                          # ICONDIRENTRY x N
    $sz = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }                                      # PNG payloads
$bw.Close(); $fs.Close()
Write-Host "Wrote $Out ($((Get-Item $Out).Length) bytes, sizes: $($sizes -join ', '))"
