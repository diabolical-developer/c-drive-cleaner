# Draws the app icon at several sizes and writes a PNG-based .ico file.
param([string]$Out)
Add-Type -AssemblyName System.Drawing

function Draw([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $n / 64.0
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 28 * $s; $x = 2 * $s; $w = 59 * $s
    $path.AddArc($x, $x, $d, $d, 180, 90); $path.AddArc($x + $w - $d, $x, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $x + $w - $d, $d, $d, 0, 90); $path.AddArc($x, $x + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.Rectangle 0, 0, $n, $n
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(59, 130, 246)), ([System.Drawing.Color]::FromArgb(29, 78, 216)), 90.0
    $g.FillPath($brush, $path)
    $pw = 9 * $s; $o = 15 * $s; $r = 34 * $s
    $p1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), $pw
    $p2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $pw
    $p3 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(74, 222, 128)), $pw
    $g.DrawEllipse($p1, $o, $o, $r, $r)
    $g.DrawArc($p2, $o, $o, $r, $r, -90, 250)
    $g.DrawArc($p3, $o, $o, $r, $r, 160, 110)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @(); foreach ($n in $sizes) { $images += ,(Draw $n) }
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $dim = if ($n -ge 256) { 0 } else { $n }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
