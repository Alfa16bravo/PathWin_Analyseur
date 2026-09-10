# Génère l'icône de PathWin Analyzer : embranchement blanc sur carré arrondi indigo.
Add-Type -AssemblyName System.Drawing

$out = "E:\application systeme\PathAnalyzer\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

function New-Artwork([int]$S) {
  $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  # --- fond : carré arrondi avec dégradé indigo -> violet
  $k = $S / 256.0
  $pad = [float](8 * $k)
  $r = [float](56 * $k)
  $w = [float]($S - 2 * $pad)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc($pad, $pad, $r, $r, 180, 90)
  $path.AddArc($pad + $w - $r, $pad, $r, $r, 270, 90)
  $path.AddArc($pad + $w - $r, $pad + $w - $r, $r, $r, 0, 90)
  $path.AddArc($pad, $pad + $w - $r, $r, $r, 90, 90)
  $path.CloseFigure()

  $rect = New-Object System.Drawing.RectangleF $pad, $pad, $w, $w
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 0x63, 0x66, 0xF1),
    [System.Drawing.Color]::FromArgb(255, 0x8B, 0x5C, 0xF6),
    45.0)
  $g.FillPath($brush, $path)

  # --- embranchement blanc
  $thick = [float](17 * $k)
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $thick
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

  $xSrc = [float](74 * $k)      # point de départ
  $xFork = [float](132 * $k)    # colonne de l'embranchement
  $xDst = [float](182 * $k)     # points d'arrivée
  $yMid = [float](128 * $k)
  $yTop = [float](72 * $k)
  $yBot = [float](184 * $k)
  $dot = [float](15 * $k)

  # tronc
  $g.DrawLine($pen, $xSrc, $yMid, $xFork, $yMid)
  # colonne verticale reliant les trois branches
  $g.DrawLine($pen, $xFork, $yTop, $xFork, $yBot)
  # branches
  $g.DrawLine($pen, $xFork, $yTop, $xDst, $yTop)
  $g.DrawLine($pen, $xFork, $yMid, $xDst, $yMid)
  $g.DrawLine($pen, $xFork, $yBot, $xDst, $yBot)

  foreach ($p in @(@($xSrc, $yMid), @($xDst, $yTop), @($xDst, $yMid), @($xDst, $yBot))) {
    $g.FillEllipse($white, $p[0] - $dot, $p[1] - $dot, 2 * $dot, 2 * $dot)
  }

  $g.Dispose(); $pen.Dispose(); $brush.Dispose(); $white.Dispose(); $path.Dispose()
  return $bmp
}

# PNG de référence pour le README
$big = New-Artwork 256
$big.Save("$out\icon-256.png", [System.Drawing.Imaging.ImageFormat]::Png)

# --- assemblage du .ico (PNG embarqués, accepté depuis Windows Vista)
$sizes = 16, 24, 32, 48, 64, 128, 256
$blobs = @()
foreach ($s in $sizes) {
  $bmp = New-Artwork $s
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $blobs += ,@($s, $ms.ToArray())
  $bmp.Dispose(); $ms.Dispose()
}

$fs = [System.IO.File]::Create("$out\app.ico")
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0)                  # réservé
$bw.Write([uint16]1)                  # type : icône
$bw.Write([uint16]$blobs.Count)
$offset = 6 + 16 * $blobs.Count
foreach ($b in $blobs) {
  $s = $b[0]; $data = $b[1]
  $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))   # largeur (0 = 256)
  $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))   # hauteur
  $bw.Write([byte]0)                  # palette
  $bw.Write([byte]0)                  # réservé
  $bw.Write([uint16]1)                # plans
  $bw.Write([uint16]32)               # bits par pixel
  $bw.Write([uint32]$data.Length)
  $bw.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($b in $blobs) { $bw.Write($b[1]) }
$bw.Flush(); $bw.Close(); $fs.Close()
$big.Dispose()

"icone ecrite : $out\app.ico ($((Get-Item "$out\app.ico").Length) octets, $($blobs.Count) tailles)"
