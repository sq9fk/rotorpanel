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
# Rozdzielczosc kafelka rowna rozmiarowi komorki na ekranie - program rysuje je
# wtedy jeden do jednego i kreski zostaja o grubosci jednego piksela.
KOM_SZER, KOM_WYS = 12, 22
PRZES_X, PRZES_Y = 2.9, 0.3

dane = open(ZRZUT, "rb").read()
ramka = open(RAMKA, "rb").read()

krok_x = SZER / KOLUMN
krok_y = WYS / WIERSZY


def _tusz_zrodlowy(x, y):
    if not (0 <= x < SZER and 0 <= y < WYS):
        return False
    i = y * STRIDE + x * 4
    return (dane[i] + dane[i + 1] + dane[i + 2]) / 3 < 128


def scienij(maska, szer=None, wys=None):
    """Zhang-Suen: sprowadza kreski do grubosci jednego piksela.

    Na wyswietlaczu kreski maja jeden piksel, ale na zrzucie sa grubsze i po
    przeniesieniu do kafelkow logo wychodzilo nierowno. Scienienie daje linie
    o stalej grubosci, takiej jak ramka."""
    m = [wiersz[:] for wiersz in maska]
    szer = szer or SZER
    wys = wys or WYS
    zmiana = True

    def sasiedzi(x, y):
        return [m[y - 1][x], m[y - 1][x + 1], m[y][x + 1], m[y + 1][x + 1],
                m[y + 1][x], m[y + 1][x - 1], m[y][x - 1], m[y - 1][x - 1]]

    while zmiana:
        zmiana = False
        for krok in (0, 1):
            doUsuniecia = []
            for y in range(1, wys - 1):
                for x in range(1, szer - 1):
                    if not m[y][x]:
                        continue
                    s8 = sasiedzi(x, y)
                    ile = sum(s8)
                    if not (2 <= ile <= 6):
                        continue
                    przejsc = sum(1 for i in range(8)
                                  if s8[i] == 0 and s8[(i + 1) % 8] == 1)
                    if przejsc != 1:
                        continue
                    p2, p3, p4, p5, p6, p7 = s8[0], s8[1], s8[2], s8[3], s8[4], s8[5]
                    if krok == 0:
                        if p2 * p4 * p6 or p4 * p6 * s8[6]:
                            continue
                    else:
                        if p2 * p4 * s8[6] or p2 * p6 * s8[6]:
                            continue
                    doUsuniecia.append((x, y))
            for x, y in doUsuniecia:
                m[y][x] = 0
                zmiana = True
    return m


MASKA = scienij([[1 if _tusz_zrodlowy(x, y) else 0 for x in range(SZER)]
                 for y in range(WYS)])


def tusz(x, y):
    return 0 <= x < SZER and 0 <= y < WYS and MASKA[y][x] == 1


def komorka(wiersz, kolumna):
    x0 = kolumna * krok_x + PRZES_X
    y0 = wiersz * krok_y + PRZES_Y
    wynik = []
    for wy in range(KOM_WYS):
        bity = 0
        for wx in range(KOM_SZER):
            # Bierzemy caly wycinek, a nie jego srodek: kreski na wyswietlaczu maja
            # jeden piksel i przy probkowaniu punktowym wypadaly, robiac dziury w logo.
            ax = x0 + wx * krok_x / KOM_SZER
            ay = y0 + wy * krok_y / KOM_WYS
            bx = x0 + (wx + 1) * krok_x / KOM_SZER
            by = y0 + (wy + 1) * krok_y / KOM_WYS

            zapalony = False
            px = int(ax)
            while px <= int(bx) and not zapalony:
                py = int(ay)
                while py <= int(by):
                    if tusz(px, py):
                        zapalony = True
                        break
                    py += 1
                px += 1

            if zapalony:
                bity |= 1 << (KOM_SZER - 1 - wx)
        wynik.append(bity)
    return tuple(wynik)


# Skladamy caly ekran w docelowej rozdzielczosci i scieniamy go tam, gdzie bedzie
# rysowany - inaczej powiekszenie z 9,6 piksela na 12 rozdmuchuje kreski do dwoch.
SIATKA_SZER = KOLUMN * KOM_SZER
SIATKA_WYS = WIERSZY * KOM_WYS
siatka = [[0] * SIATKA_SZER for _ in range(SIATKA_WYS)]

for w in range(WIERSZY):
    for k in range(KOLUMN):
        bity = komorka(w, k)
        for wy in range(KOM_WYS):
            for wx in range(KOM_SZER):
                if (bity[wy] >> (KOM_SZER - 1 - wx)) & 1:
                    siatka[w * KOM_WYS + wy][k * KOM_SZER + wx] = 1

siatka = scienij(siatka, SIATKA_SZER, SIATKA_WYS)


def z_siatki(wiersz, kolumna):
    wynik = []
    for wy in range(KOM_WYS):
        bity = 0
        for wx in range(KOM_SZER):
            if siatka[wiersz * KOM_WYS + wy][kolumna * KOM_SZER + wx]:
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
        zebrane[kod].append(z_siatki(w, k))

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
        kod, ", ".join("0x%04X" % w for w in wiersze)))

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
