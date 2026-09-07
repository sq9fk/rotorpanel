"""Buduje slownik kafelkow wyswietlacza SPE: kod bajtu -> mapa bitowa.

Wzmacniacz przysyla w ramce 0x6A same kody komorek, a nie piksele. Znaki wlasne
(logo, kreski, ramki, strzalki) trzeba wiec skads wziac - uczymy sie ich ze zrzutu
ekranu dopasowanego do siatki 40 na 8.

Uzycie:
    ucz-kafelki.py zrzut.raw ekran.bin szerokosc wysokosc stride [wyjscie.cs]

zrzut.raw to surowe piksele BGRA (mozna je zapisac z PowerShella przez
System.Drawing), ekran.bin to zawartosc ramki 0x6A z tego samego ekranu. Uczymy sie
tylko kodow od 0x80 w gore: tekst renderujemy czcionka, a przy zrzucie z innej
chwili komorki z wartosciami nie odpowiadalyby swoim kodom.
"""

import os, sys
from collections import Counter, defaultdict

K = os.path.dirname(os.path.abspath(__file__))
ZRZUT = sys.argv[1]
RAMKA = sys.argv[2]
SZER, WYS, STRIDE = int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
WYJSCIE = sys.argv[6] if len(sys.argv) > 6 else os.path.join(K, "..", "KafelkiSpe.cs")
KOLUMN, WIERSZY = 40, 8
KOM_SZER, KOM_WYS = 10, 14
PRZES_X, PRZES_Y = 2.9, 0.3

dane = open(ZRZUT, "rb").read()
ramka = open(RAMKA, "rb").read()

krok_x = SZER / KOLUMN
krok_y = WYS / WIERSZY


def tusz(x, y):
    if not (0 <= x < SZER and 0 <= y < WYS):
        return False
    i = y * STRIDE + x * 4
    return (dane[i] + dane[i + 1] + dane[i + 2]) / 3 < 128


def komorka(wiersz, kolumna):
    x0 = kolumna * krok_x + PRZES_X
    y0 = wiersz * krok_y + PRZES_Y
    wynik = []
    for wy in range(KOM_WYS):
        bity = 0
        for wx in range(KOM_SZER):
            px = int(x0 + (wx + 0.5) * krok_x / KOM_SZER)
            py = int(y0 + (wy + 0.5) * krok_y / KOM_WYS)
            if tusz(px, py):
                bity |= 1 << (KOM_SZER - 1 - wx)
        wynik.append(bity)
    return tuple(wynik)


zebrane = defaultdict(list)
for w in range(WIERSZY):
    for k in range(KOLUMN):
        i = 5 + w * KOLUMN + k
        if i >= len(ramka):
            continue
        kod = ramka[i]
        # Uczymy sie tylko znakow wlasnych wyswietlacza. Tekst pomijamy: zrzut jest
        # z innego stanu wzmacniacza niz zapisana ramka, wiec komorki z wartosciami
        # (pasmo, temperatura) nie odpowiadaja swoim kodom.
        if kod < 0x80:
            continue
        zebrane[kod].append(komorka(w, k))

# gdy kod wystapil kilka razy, bierzemy najczestszy wyglad
kafelki = {}
for kod, lista in zebrane.items():
    najczestszy, ile = Counter(lista).most_common(1)[0]
    if any(najczestszy):                      # pomijamy puste
        kafelki[kod] = (najczestszy, len(lista), ile)

print("kodow ze zrzutu: %d" % len(kafelki))
graficzne = sorted(k for k in kafelki if k >= 0x80)
print("w tym graficznych (>= 0x80): %d  %s" % (
    len(graficzne), " ".join("%02X" % k for k in graficzne)))

# podglad kilku kafelkow logo
for kod in graficzne[:2]:
    print("\nkod 0x%02X:" % kod)
    for wiersz in kafelki[kod][0]:
        print("   " + "".join("#" if (wiersz >> (KOM_SZER - 1 - i)) & 1 else "." for i in range(KOM_SZER)))

# zapis do C#
linie = []
for kod in sorted(kafelki):
    wiersze = kafelki[kod][0]
    linie.append("        { 0x%02X, new ushort[] { %s } }," % (
        kod, ", ".join("0x%03X" % w for w in wiersze)))

zrodlo = """namespace RotorPanel;

/// <summary>
/// Mapy bitowe kafelkow wyswietlacza SPE, nauczone ze zrzutu ekranu dopasowanego
/// do siatki 40x8. Wzmacniacz przysyla tylko kody, a pikseli w ramce nie ma, wiec
/// znaki wlasne (logo, kreski, strzalki) trzeba miec skads. Kazdy wpis to %d
/// wierszy po %d pikseli, licząc od najstarszego bitu.
/// </summary>
public static class KafelkiSpe
{
    public const int Szerokosc = %d;
    public const int Wysokosc = %d;

    public static readonly Dictionary<byte, ushort[]> Mapy = new()
    {
%s
    };
}
""" % (KOM_WYS, KOM_SZER, KOM_SZER, KOM_WYS, "\n".join(linie))

open(WYJSCIE, "w", encoding="utf-8-sig", newline="\r\n").write(zrodlo)
print("\nzapisano %s (%d kafelkow)" % (WYJSCIE, len(kafelki)))
