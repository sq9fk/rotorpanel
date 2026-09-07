"""Buduje slownik kafelkow wyswietlacza SPE: kod bajtu -> mapa bitowa 6x8.

UWAGA: program nie uzywa juz tego wyniku. Mapy bitowe bierze z pelnego fontu ROM
(`CzcionkaSpe.cs`, patrz `wczytaj-rom.py` i NOTICE). Ten skrypt zostaje jako
**niezalezny sprawdzian** tamtej tablicy - uczy sie od zera ze zdjecia panelu
i pisze do `obj/`, zeby dalo sie porownac. Kiedy oba zrodla porownano, zgadzalo sie
67 z 68 kafelkow i wszystkie 35 zmierzonych znakow.

Wzmacniacz przysyla w ramce 0x6A same kody komorek, a nie piksele. Znaki wlasne
(logo, kreski, ramki, strzalki) trzeba wiec skads wziac - uczymy sie ich ze zrzutu
ekranu dopasowanego do siatki 40 na 8.

Panel ma wyswietlacz graficzny 240 na 64 piksele, czyli komorka to dokladnie
6 na 8 pikseli. Uczymy sie w tej rozdzielczosci, a program powieksza kafelki
calkowita krotnoscia - inaczej jednopikselowe kreski wypadaja raz grubsze, raz
ciensze i ukosne linie logo maja przerwy.

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
WYJSCIE = sys.argv[6] if len(sys.argv) > 6 else os.path.join(K, "..", "obj", "KafelkiSpe.sprawdzenie.cs")
KOLUMN, WIERSZY = 40, 8

# Natywna rozdzielczosc komorki wyswietlacza: 240x64 piksele na 40x8 znakow.
KOM_SZER, KOM_WYS = 6, 8

# Wypelnienie linijki miernikow. Tych kodow nie da sie na razie nauczyc ze zdjecia:
# w obu wersjach instrukcji wszystkie czternascie zdjec ekranu jest z postoju, z pusta
# linijka, a wlasnego zdjecia z wypelniona nie mamy. Sa za to **wyprowadzone z ksztaltow
# zmierzonych**, a nie zgadniete z powietrza - i tym sie roznia od reszty.
#
# Puste kafelki linijki (nauczone ze zdjecia) rysuja pudelko: gorna krawedz w wierszu 3,
# dolna w wierszu 6, srodek pusty. Wypelnienie to ten sam kafelek z zamalowanym srodkiem,
# czyli wierszami 4 i 5 - geometria nie zostawia tu wyboru:
#     0x81 lewa zaslepka  -> 0x85     0x82 odcinek -> 0x88     0x83 podzialka -> 0x8B
#
# Inaczej jest z 0x86, komorka na koncu belki. Ile ma zamalowanych kolumn, wyliczylem
# z ramki ekranu V PA: podzialki 0x83 i zaslepki 0x81/0x84 maja kreske w kolumnie 2,
# a stoja w komorkach 7, 12, 17, 22 i 27, czyli 0 V wypada w kolumnie 44 siatki pikseli,
# 60 V w kolumnie 164 - dwie kolumny na wolt. Odczyt 33,7 V daje koniec belki w kolumnie
# 111,4, a komorka 18 zaczyna sie w kolumnie 108, wiec zamalowane sa cztery kolumny.
# Niepewnosc to jedna kolumna w kazda strone (nie wiadomo, jak wzmacniacz zaokragla),
# czyli okolo poł wolta na tej skali. Gdy trafi sie zdjecie ekranu z wypelniona linijka,
# `ucz-z-instrukcji.py` nadpisze te cztery kafelki zmierzonymi.
WYPROWADZONE = {
    0x85: (0x08, 0x08, 0x08, 0x0F, 0x0F, 0x0F, 0x0F, 0x00),   # zaslepka, srodek zamalowany
    0x86: (0x00, 0x00, 0x08, 0x3F, 0x3C, 0x3C, 0x3F, 0x00),   # koniec belki, cztery kolumny
    0x88: (0x00, 0x00, 0x08, 0x3F, 0x3F, 0x3F, 0x3F, 0x00),   # odcinek zamalowany
    0x8B: (0x08, 0x08, 0x08, 0x3F, 0x3F, 0x3F, 0x3F, 0x00),   # podzialka zamalowana
}

dane = open(ZRZUT, "rb").read()
ramka = open(RAMKA, "rb").read()


def jasnosc(x, y):
    if not (0 <= x < SZER and 0 <= y < WYS):
        return 255.0
    i = y * STRIDE + x * 4
    return (dane[i] + dane[i + 1] + dane[i + 2]) / 3.0


def tusz(x, y):
    return jasnosc(x, y) < 128


def _dopasuj_poziom():
    """Lewa krawedz i krok siatki z zasiegu tuszu.

    Gorna krawedz ramki i wiersz kresek biegna przez cala szerokosc wyswietlacza,
    wiec skrajne kolumny z tuszem wyznaczaja jego pole. Wczesniej dopasowywalem
    krok do pionowych separatorow - wyszlo o pol komorki obok, bo separator nie
    stoi przy krawedzi komorki, tylko mniej wiecej w jej srodku.
    """
    z_tuszem = [x for x in range(SZER) if any(tusz(x, y) for y in range(WYS))]
    if len(z_tuszem) < 2:
        return SZER / KOLUMN, 0.0
    lewo, prawo = z_tuszem[0], z_tuszem[-1] + 1
    return (prawo - lewo) / KOLUMN, float(lewo)


def _dopasuj_pion():
    """Gora i wysokosc siatki z zasiegu tuszu.

    Pierwszy wiersz ekranu ma na calej szerokosci gorna krawedz ramki i logo,
    ostatni ma separatory siegajace samego dolu komorki, wiec skrajne wiersze
    z tuszem wyznaczaja pole wyswietlacza. Marginesy zrzutu odpadaja same.
    """
    z_tuszem = [y for y in range(WYS) if any(tusz(x, y) for x in range(SZER))]
    if len(z_tuszem) < 2:
        return WYS / WIERSZY, 0.0
    gora, dol = z_tuszem[0], z_tuszem[-1] + 1
    return (dol - gora) / WIERSZY, float(gora)


krok_x, poczatek_x = _dopasuj_poziom()
krok_y, poczatek_y = _dopasuj_pion()
print("siatka: krok %.3f x %.3f px, poczatek %.2f x %.2f px" %
      (krok_x, krok_y, poczatek_x, poczatek_y))
print("piksel wyswietlacza: %.3f x %.3f px zrzutu" % (krok_x / KOM_SZER, krok_y / KOM_WYS))


def zaczernienie(xa, xb, ya, yb):
    """Sredni tusz w prostokacie zrzutu - z odcieniami i wagami brzegow.

    Zrzut jest powiekszeniem wyswietlacza w skali 1,57, wiec krawedzie sa
    wygladzone, a piksel wyswietlacza nie pokrywa sie z pikselami zrzutu. Progowanie
    najpierw na czarno-biale, a dopiero potem usrednianie, dawalo ten sam znak raz
    tak, raz inaczej - zaleznie od tego, w ktorym miejscu rastra wypadla komorka.
    Liczenie po odcieniach usuwa to bez reszty: wszystkie 57 kodow graficznych i 35
    sprawdzalnych liter wychodza z kazdego wystapienia identycznie.
    """
    suma = pole = 0.0
    for py in range(int(ya), int(yb) + 1):
        wy = min(yb, py + 1) - max(ya, py)
        if wy <= 0:
            continue
        for px in range(int(xa), int(xb) + 1):
            wx = min(xb, px + 1) - max(xa, px)
            if wx <= 0:
                continue
            waga = wx * wy
            pole += waga
            suma += waga * (255.0 - jasnosc(px, py)) / 255.0
    return suma / pole if pole > 0 else 0.0


def komorka(wiersz, kolumna):
    """Komorka sprowadzona do 6x8 - jeden bit na piksel wyswietlacza."""
    x0 = poczatek_x + kolumna * krok_x
    y0 = poczatek_y + wiersz * krok_y
    wynik = []
    for wy in range(KOM_WYS):
        bity = 0
        ya, yb = y0 + wy * krok_y / KOM_WYS, y0 + (wy + 1) * krok_y / KOM_WYS
        for wx in range(KOM_SZER):
            xa, xb = x0 + wx * krok_x / KOM_SZER, x0 + (wx + 1) * krok_x / KOM_SZER
            if zaczernienie(xa, xb, ya, yb) > 0.5:
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

for kod, mapa in WYPROWADZONE.items():
    kafelki.setdefault(kod, (mapa, 0, 0))

print("kodow ze zrzutu: %d" % len(kafelki))
graficzne = sorted(kafelki)
print("kody: %s" % " ".join("%02X" % k for k in graficzne))

if "-p" in sys.argv:
    for kod in graficzne:
        print("\nkod 0x%02X (wystapien %d, zgodnych %d):" % (kod, kafelki[kod][1], kafelki[kod][2]))
        for wiersz in kafelki[kod][0]:
            print("   " + "".join("#" if (wiersz >> (KOM_SZER - 1 - i)) & 1 else "."
                                  for i in range(KOM_SZER)))

# zapis do C#
linie = []
for kod in sorted(kafelki):
    wiersze = kafelki[kod][0]
    skad = "wyprowadzony" if kafelki[kod][1] == 0 else "ze zrzutu"
    linie.append("        { 0x%02X, new byte[] { %s } },   // %s" % (
        kod, ", ".join("0x%02X" % w for w in wiersze), skad))

zrodlo = """namespace RotorPanel;

/// <summary>
/// Mapy bitowe kafelkow wyswietlacza SPE, nauczone ze zrzutu ekranu dopasowanego
/// do siatki 40x8. Wzmacniacz przysyla tylko kody, a pikseli w ramce nie ma, wiec
/// znaki wlasne (logo, kreski, strzalki) trzeba miec skads.
///
/// Panel ma wyswietlacz 240x64, czyli komorka to %d na %d pikseli - i w takiej
/// rozdzielczosci sa te mapy. Kazdy wpis to %d bajtow, po jednym na wiersz,
/// liczac od najstarszego z %d bitow.
/// </summary>
public static class KafelkiSpe
{
    public const int Szerokosc = %d;
    public const int Wysokosc = %d;

    public static readonly Dictionary<byte, byte[]> Mapy = new()
    {
%s
    };
}
""" % (KOM_SZER, KOM_WYS, KOM_WYS, KOM_SZER, KOM_SZER, KOM_WYS, "\n".join(linie))

open(WYJSCIE, "w", encoding="utf-8-sig", newline="\r\n").write(zrodlo)
print("\nzapisano %s (%d kafelkow)" % (WYJSCIE, len(kafelki)))
