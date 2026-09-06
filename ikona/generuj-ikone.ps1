# Generuje ikone aplikacji: kompas z antena kierunkowa wycelowana w azymut.
# Wynik: ikona\icon.ico z rozmiarami 16..256 px.

Add-Type -AssemblyName System.Drawing

$rozmiary = @(16, 24, 32, 48, 64, 128, 256)
$wynik    = Join-Path $PSScriptRoot "icon.ico"

$tlo      = [Drawing.Color]::FromArgb(255, 31, 35, 40)     # ciemna tarcza
$obwod    = [Drawing.Color]::FromArgb(255, 62, 70, 78)
$podzialka= [Drawing.Color]::FromArgb(255, 150, 158, 168)
$polnoc   = [Drawing.Color]::FromArgb(255, 226, 229, 234)
$antena   = [Drawing.Color]::FromArgb(255, 47, 191, 113)   # zielen jak dioda "polaczony"

function Rysuj([int]$s) {
    $bmp = New-Object Drawing.Bitmap($s, $s)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)

    $m = $s / 32.0        # wszystko projektowane w siatce 32x32

    # --- tarcza kompasu ---
    $pedzelTla = New-Object Drawing.SolidBrush $tlo
    $g.FillEllipse($pedzelTla, 0.6*$m, 0.6*$m, 30.8*$m, 30.8*$m)

    if ($s -ge 32) {
        $pioroObwodu = New-Object Drawing.Pen($obwod, [float](1.1*$m))
        $g.DrawEllipse($pioroObwodu, 1.2*$m, 1.2*$m, 29.6*$m, 29.6*$m)
        $pioroObwodu.Dispose()
    }

    # --- podzialka co 90 stopni, polnoc jasniejsza ---
    if ($s -ge 32) {
        $g.TranslateTransform(16*$m, 16*$m)
        for ($k = 0; $k -lt 4; $k++) {
            $kolor = if ($k -eq 0) { $polnoc } else { $podzialka }
            $pedzel = New-Object Drawing.SolidBrush $kolor
            $dlugosc = if ($k -eq 0) { 4.2 } else { 2.8 }
            $g.FillRectangle($pedzel, -0.75*$m, -13.6*$m, 1.5*$m, $dlugosc*$m)
            $pedzel.Dispose()
            $g.RotateTransform(90)
        }
        $g.ResetTransform()
    }

    # --- antena kierunkowa, obrocona na azymut okolo 35 stopni ---
    $g.TranslateTransform(16*$m, 16*$m)
    $g.RotateTransform(35)

    $pedzelAnteny = New-Object Drawing.SolidBrush $antena

    # Wskaznik kierunku - ta sama forma w kazdym rozmiarze.
    $igla = @(
        (New-Object Drawing.PointF([float](0*$m),     [float](-12.6*$m))),
        (New-Object Drawing.PointF([float](-7.4*$m),  [float](5.6*$m))),
        (New-Object Drawing.PointF([float](0*$m),     [float](1.6*$m))),
        (New-Object Drawing.PointF([float](7.4*$m),   [float](5.6*$m)))
    )
    $g.FillPolygon($pedzelAnteny, $igla)

    $pedzelAnteny.Dispose()
    $g.ResetTransform()
    $g.Dispose()
    return $bmp
}

# --- zlozenie pliku ICO ---------------------------------------------------
# Rozmiary do 64 px zapisujemy klasycznie jako DIB, bo System.Drawing i starsze
# narzedzia nie czytaja wpisow PNG. Wieksze zostaja PNG, zeby plik nie spuchl.

function DoDib([Drawing.Bitmap]$bmp) {
    $w = $bmp.Width
    $h = $bmp.Height
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter($ms)

    $bw.Write([uint32]40)          # biSize
    $bw.Write([int32]$w)           # biWidth
    $bw.Write([int32]($h * 2))     # biHeight - obraz plus maska
    $bw.Write([uint16]1)           # biPlanes
    $bw.Write([uint16]32)          # biBitCount
    $bw.Write([uint32]0)           # biCompression
    $bw.Write([uint32]($w*$h*4))   # biSizeImage
    $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)

    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }

    $bajtowWierszu = [math]::Floor(($w + 31) / 32) * 4
    $pusty = New-Object byte[] $bajtowWierszu
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($pusty) }

    $bw.Flush()
    $dane = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    # Przecinek chroni tablice przed rozwinieciem przez PowerShell.
    return ,$dane
}

$obrazy = @()
foreach ($s in $rozmiary) {
    $bmp = Rysuj $s
    if ($s -le 64) {
        $dane = [byte[]](DoDib $bmp)
    }
    else {
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        $dane = $ms.ToArray()
        $ms.Dispose()
    }
    $obrazy += [pscustomobject]@{ Rozmiar = $s; Dane = $dane }
    $bmp.Dispose()
}

$plik = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter($plik)

$w.Write([uint16]0)
$w.Write([uint16]1)
$w.Write([uint16]$obrazy.Count)

$offset = 6 + 16 * $obrazy.Count
foreach ($o in $obrazy) {
    $bajt = if ($o.Rozmiar -ge 256) { 0 } else { $o.Rozmiar }
    $w.Write([byte]$bajt)
    $w.Write([byte]$bajt)
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([uint16]1)
    $w.Write([uint16]32)
    $w.Write([uint32]$o.Dane.Length)
    $w.Write([uint32]$offset)
    $offset += $o.Dane.Length
}
foreach ($o in $obrazy) { $w.Write($o.Dane) }

$w.Flush()
[IO.File]::WriteAllBytes($wynik, $plik.ToArray())
$w.Dispose(); $plik.Dispose()

Write-Output ("Zapisano: " + $wynik + "  (" + [math]::Round((Get-Item $wynik).Length/1KB,1) + " KB, rozmiary: " + ($rozmiary -join ", ") + ")")
