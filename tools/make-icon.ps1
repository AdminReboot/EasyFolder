# Tạo app.ico (4 kích thước, mỗi ảnh là PNG nhúng trong ICO). Chạy lại khi muốn đổi biểu tượng.
Add-Type -AssemblyName System.Drawing

$sizes = 16, 32, 48, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $back = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 120, 210))
    $pane = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 160, 0))

    $g.FillRectangle($back, 0, 0, $s, $s)
    $m = [Math]::Max(1, [int]($s * 0.12))      # lề ngoài
    $gap = [Math]::Max(1, [int]($s * 0.08))    # khe giữa các khung
    $cell = [int](($s - 2 * $m - $gap) / 2)
    foreach ($r in 0, 1) {
        foreach ($c in 0, 1) {
            $x = $m + $c * ($cell + $gap)
            $y = $m + $r * ($cell + $gap)
            $g.FillRectangle($pane, $x, $y, $cell, $cell)
            $g.FillRectangle($accent, $x, $y, $cell, [Math]::Max(1, [int]($cell * 0.22)))
        }
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'app.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Dispose()
"Created $out"
