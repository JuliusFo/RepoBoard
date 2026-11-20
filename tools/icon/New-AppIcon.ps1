<#
.SYNOPSIS
    Draws RepoBoard's icon (program, window, notification area) and writes it as a multi-size .ico.
.DESCRIPTION
    A board of four tiles on the accent colour of repoboard.css (--accent, light). Sizes up to 64 px are stored as 32-bit
    bitmaps (System.Drawing.Icon, which the notification area uses, reads those reliably), 256 px as PNG. Windows only.
.EXAMPLE
    pwsh -NoProfile -File tools/icon/New-AppIcon.ps1
#>
param(
    [string] $Path = (Join-Path $PSScriptRoot '../../src/RepoBoard.App/RepoBoard.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$accent = [System.Drawing.Color]::FromArgb(255, 0x3b, 0x6f, 0xe0)
$tile = [System.Drawing.Color]::White
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-RoundedRect([single] $x, [single] $y, [single] $w, [single] $h, [single] $r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-Frame([int] $size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $size / 16.0 # drawn on a 16-unit grid
    $g.FillPath([System.Drawing.SolidBrush]::new($accent), (New-RoundedRect 0 0 $size $size (3.5 * $u)))
    $brush = [System.Drawing.SolidBrush]::new($tile)
    foreach ($cell in @(@(3, 3), @(9, 3), @(3, 9), @(9, 9))) {
        $g.FillPath($brush, (New-RoundedRect ($cell[0] * $u) ($cell[1] * $u) (4 * $u) (4 * $u) (1 * $u)))
    }
    $g.Dispose()
    $bmp
}

function Get-DibBytes([System.Drawing.Bitmap] $bmp) {
    $size = $bmp.Width
    $ms = [System.IO.MemoryStream]::new()
    $w = [System.IO.BinaryWriter]::new($ms)
    # BITMAPINFOHEADER: height counts the colour rows and the AND mask
    $w.Write([int] 40); $w.Write([int] $size); $w.Write([int] ($size * 2)); $w.Write([int16] 1); $w.Write([int16] 32)
    $w.Write([int] 0); $w.Write([int] 0); $w.Write([int] 0); $w.Write([int] 0); $w.Write([int] 0); $w.Write([int] 0)
    for ($y = $size - 1; $y -ge 0; $y--) { # bottom-up, BGRA
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte] $c.B); $w.Write([byte] $c.G); $w.Write([byte] $c.R); $w.Write([byte] $c.A)
        }
    }
    $maskRow = [int] ([math]::Ceiling($size / 32.0) * 4)
    $w.Write([byte[]]::new($maskRow * $size)) # all zero: the alpha channel decides
    $w.Flush()
    $ms.ToArray()
}

$images = foreach ($size in $sizes) {
    $bmp = New-Frame $size
    if ($size -ge 256) {
        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    }
    else {
        , (Get-DibBytes $bmp)
    }
    $bmp.Dispose()
}

$out = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($out)
$w.Write([int16] 0); $w.Write([int16] 1); $w.Write([int16] $sizes.Count) # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) { # ICONDIRENTRY
    $s = $sizes[$i]
    $w.Write([byte] ($s % 256)); $w.Write([byte] ($s % 256)); $w.Write([byte] 0); $w.Write([byte] 0)
    $w.Write([int16] 1); $w.Write([int16] 32); $w.Write([int] $images[$i].Length); $w.Write([int] $offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write([byte[]] $image, 0, $image.Length) }
$w.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($Path), $out.ToArray())
Write-Host "Written: $([System.IO.Path]::GetFullPath($Path)) ($($out.Length) bytes)"
