# Generates src/OpenController.App/Assets/app.ico from scratch (no third-party artwork).
# The output is an original drawing covered by the project's GPL-3.0-or-later license.
# Usage (Windows PowerShell 5.1 or PowerShell 7 on Windows):  ./build/make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\src\OpenController.App\Assets\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    # Rounded dark tile
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 14 * $s; $w = 62 * $s; $o = 1 * $s
    $tile.AddArc($o, $o, $r, $r, 180, 90); $tile.AddArc($o + $w - $r, $o, $r, $r, 270, 90)
    $tile.AddArc($o + $w - $r, $o + $w - $r, $r, $r, 0, 90); $tile.AddArc($o, $o + $w - $r, $r, $r, 90, 90)
    $tile.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 28, 34))), $tile)

    # Gamepad body: two grips + bridge
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 16, 185, 90))
    $g.FillEllipse($green, 6 * $s, 20 * $s, 24 * $s, 30 * $s)
    $g.FillEllipse($green, 34 * $s, 20 * $s, 24 * $s, 30 * $s)
    $g.FillRectangle($green, 16 * $s, 20 * $s, 32 * $s, 18 * $s)

    # Sticks and buttons
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 28, 34))
    $g.FillEllipse($dark, 13 * $s, 26 * $s, 9 * $s, 9 * $s)
    $g.FillEllipse($dark, 37 * $s, 34 * $s, 8 * $s, 8 * $s)
    $g.FillEllipse($dark, 45 * $s, 24 * $s, 5 * $s, 5 * $s)
    $g.FillRectangle($dark, 23 * $s, 37 * $s, 3 * $s, 8 * $s)
    $g.FillRectangle($dark, 20.5 * $s, 39.5 * $s, 8 * $s, 3 * $s)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { , (New-IconPng $sz) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out) | Join-Path -ChildPath (Split-Path $Out -Leaf)))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Dispose()
Write-Host "Wrote $Out"
