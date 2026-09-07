"""Przepisuje font ROM sterownika LCD wzmacniacza SPE na plik C#.

Zrodlem jest `SpeLcdFontRom.inc` z projektu AetherSDR (GPL-3.0) - kompletna tablica
256 glifow po osiem linii, szesc bitow szerokosci, najstarszy bit z lewej. Ich
komentarz mowi, ze tablica zostala wyciagnieta z aplikacji sterujacej jednego
ze wspolautorow.

Zanim ja wzielismy, zestawilismy ja z tym, co sami odtworzylismy ze zdjec panelu:
  * 67 z 68 kafelkow graficznych zgodnych co do bitu,
  * wszystkie 35 znakow zmierzonych ze zrzutu zgodnych co do bitu,
  * roznice tylko tam, gdzie zgadywalismy - jeden kafelek wyprowadzony rachunkiem
    (0x86, o kolumne za szeroki) i dwanascie znakow wpisanych z klasycznej czcionki
    5x7 (! " , 7 Y g j p q | ~ i znak wypelnienia 0x5F).
Skrypty `ucz-kafelki.py` i `ucz-z-instrukcji.py` zostaja jako niezalezny sprawdzian
tej tablicy - pisza juz tylko do `obj/`, nie do zrodel programu.

Uzycie:
    wczytaj-rom.py SpeLcdFontRom.inc [wyjscie.cs]
"""

import io, os, re, sys

K = os.path.dirname(os.path.abspath(__file__))
WE = sys.argv[1]
WYJSCIE = sys.argv[2] if len(sys.argv) > 2 else os.path.join(K, "..", "CzcionkaSpe.cs")

SZEROKOSC, WYSOKOSC = 6, 8

glify = {}
for m in re.finditer(r"\{([^}]*)\},\s*//\s*0x([0-9a-fA-F]{2})", io.open(WE).read()):
    glify[int(m.group(2), 16)] = [int(x, 16) for x in m.group(1).split(",")]

brak = [k for k in range(256) if k not in glify]
if brak:
    print("brakuje kodow: %s" % " ".join("%02X" % k for k in brak))
    sys.exit(1)

for kod, wiersze in glify.items():
    if len(wiersze) != WYSOKOSC or any(w > 0x3F for w in wiersze):
        print("kod 0x%02X ma zly ksztalt: %s" % (kod, wiersze))
        sys.exit(1)

linie = []
for kod in range(256):
    znak = chr(kod + 0x20) if 0x00 <= kod <= 0x5F else ""
    opis = ("znak %r" % znak).replace("'", "") if znak.strip() else ""
    linie.append("        /* 0x%02X %-10s */ new byte[] { %s }," % (
        kod, opis, ", ".join("0x%02X" % w for w in glify[kod])))

zrodlo = """namespace RotorPanel;

/// <summary>
/// Font ROM sterownika wyswietlacza SPE Expert: kod bajtu z ramki 0x6A -> mapa
/// bitowa %d na %d pikseli. Rysunek znaku siedzi w kolumnach 1-5 i wierszach 0-6,
/// reszta to odstep; wiersz 7 wykorzystuja ogonki liter g, j, p, q, y.
///
/// Znaki tekstu to ASCII pomniejszone o 0x20, czyli prawdziwy znak to kod + 0x20
/// dla calego zakresu 0x00-0x5F. Od 0x80 w gore ida znaki wlasne wyswietlacza:
/// linijki miernikow, ramki, strzalki, kafelki logo.
///
/// Tablica pochodzi z projektu AetherSDR (GPL-3.0), plik src/gui/SpeLcdFontRom.inc -
/// patrz LICENSE i sekcja o pochodzeniu w README. Zestawiona z tym, co sami
/// odtworzylismy ze zdjec panelu: wszystkie zmierzone znaki i 67 z 68 kafelkow
/// zgadzaly sie co do bitu. Plik generuje narzedzia/wczytaj-rom.py - nie poprawiaj
/// go recznie.
/// </summary>
public static class CzcionkaSpe
{
    public const int Szerokosc = %d;
    public const int Wysokosc = %d;

    /// <summary>Mapa bitowa dla kazdego z 256 kodow, indeksowana bajtem z ramki.</summary>
    public static readonly byte[][] Glify =
    {
%s
    };
}
""" % (SZEROKOSC, WYSOKOSC, SZEROKOSC, WYSOKOSC, "\n".join(linie))

io.open(WYJSCIE, "w", encoding="utf-8-sig", newline="\r\n").write(zrodlo)
print("zapisano %s (%d glifow)" % (os.path.normpath(WYJSCIE), len(glify)))
