"""Uczy map bitowych ze zdjecia ekranu w instrukcji, zwiazanego z ramka z panelu.

Ekrany inne niz glowny maja wlasne symbole - linijki miernikow w trybie Operate,
strzalki w podpowiedziach SET - a nauczyc ich sie mozna tylko z pary: obraz mowi,
jak komorka wyglada, ramka mowi, jakim kodem wzmacniacz o nia prosi.

Zdjecie z instrukcji i ramka z wlasnego wzmacniacza pochodza z roznych chwil i nie
musza sie zgadzac co do wartosci (inna moc, inne pasmo, inna temperatura). Zgadza sie
natomiast **uklad** ekranu, a to wystarczy: siatke dopasowujemy tak, zeby komorki,
ktorych kod znamy juz z `CzcionkaSpe` i `KafelkiSpe`, wyszly ze zdjecia dokladnie tak
samo. Jesli takich trafien jest duzo, uklad na pewno sie pokrywa i komorkom z kodami
nieznanymi mozna zaufac. Skrypt sam pokazuje, ile trafil - przy zlym dopasowaniu
trafien jest kilka, przy dobrym prawie wszystkie.

Uzycie:
    ucz-z-instrukcji.py zrzut.raw szer wys stride ramka.bin x0 y0 skala kod [kod ...]

Polozenie siatki podaje sie wprost, bo zdjecia z instrukcji sa przyciete roznie
i szukanie po omacku bywa niejednoznaczne: przy literach szerokich na piec kolumn
w komorce szerokiej na szesc kilka roznych przesuniec daje ten sam wynik, a znaki
wypelniajace komorke do konca - jak strzalki - juz nie. Skrypt wypisuje zgodnosc
wiersz po wierszu; dobre polozenie poznac po tym, ze w wierszu z uczonymi znakami
nie zgadzaja sie **tylko** te znaki.

Zmierzone dotad:
    obraz-09.jpg (Operate)  734x211  x0=3.5  y0=9.0  s=3.00   kody 81 82 83 84
    obraz-13.jpg (SET CAT)  719x202  x0=0.75 y0=5.80 s=3.03   kody 99 9A 9B 9C

Kody podaje sie szesnastkowo. Wynik dopisuje sie do KafelkiSpe.cs.
"""

import io, os, re, sys

K = os.path.dirname(os.path.abspath(__file__))
KOM_SZER, KOM_WYS = 6, 8
KOLUMN, WIERSZY = 40, 8

ZRZUT, SZER, WYS, STRIDE = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
RAMKA = sys.argv[5]
X0, Y0, SKALA = float(sys.argv[6]), float(sys.argv[7]), float(sys.argv[8])
DO_NAUKI = [int(x, 16) for x in sys.argv[9:]]

dane = open(ZRZUT, "rb").read()
ramka = open(RAMKA, "rb").read()

# Suma prefiksowa zaczernienia - dopasowanie przeglada tysiace polozen siatki
# i liczenie kazdej komorki od zera trwaloby minuty.
_S = [[0.0] * (SZER + 1) for _ in range(WYS + 1)]
for _y in range(WYS):
    _biez = 0.0
    for _x in range(SZER):
        _i = _y * STRIDE + _x * 4
        _biez += (255.0 - (dane[_i] + dane[_i + 1] + dane[_i + 2]) / 3.0) / 255.0
        _S[_y + 1][_x + 1] = _S[_y][_x + 1] + _biez


def zaczernienie(xa, xb, ya, yb):
    x0, x1 = max(0, int(round(xa))), min(SZER, int(round(xb)))
    y0, y1 = max(0, int(round(ya))), min(WYS, int(round(yb)))
    if x1 <= x0 or y1 <= y0:
        return 0.0
    suma = _S[y1][x1] - _S[y0][x1] - _S[y1][x0] + _S[y0][x0]
    return suma / ((x1 - x0) * (y1 - y0))


def komorka(w, k, x0, y0, s):
    kx, ky = KOM_SZER * s, KOM_WYS * s
    xk, yk = x0 + k * kx, y0 + w * ky
    wynik = []
    for wy in range(KOM_WYS):
        bity = 0
        ya, yb = yk + wy * s, yk + (wy + 1) * s
        for wx in range(KOM_SZER):
            if zaczernienie(xk + wx * s, xk + (wx + 1) * s, ya, yb) > 0.5:
                bity |= 1 << (KOM_SZER - 1 - wx)
        wynik.append(bity)
    return tuple(wynik)


def znane_mapy():
    """Kod komorki -> mapa bitowa, wszystko, co juz umiemy narysowac."""
    czc = re.findall(r"new byte\[\] \{ ([^}]*) \}",
                     io.open(os.path.join(K, "..", "CzcionkaSpe.cs"), encoding="utf-8-sig").read())
    mapy = {kod: tuple(int(x, 16) for x in czc[kod].split(", ")) for kod in range(len(czc))}
    for m in re.finditer(r"\{ 0x([0-9A-F]{2}), new byte\[\] \{ ([^}]*) \}",
                         io.open(os.path.join(K, "..", "KafelkiSpe.cs"), encoding="utf-8-sig").read()):
        mapy[int(m.group(1), 16)] = tuple(int(x, 16) for x in m.group(2).split(", "))
    return mapy


ZNANE = znane_mapy()


def ocen_wiersz(w, x0, y0, s):
    """Ile komorek o znanym kodzie wyszlo ze zdjecia dokladnie tak, jak powinny.

    To jest caly dowod, ze zdjecie i ramka opisuja ten sam uklad. Zdjecie z instrukcji
    pokazuje inne wartosci niz wlasny wzmacniacz, wiec w wierszach z liczbami zgodnosc
    bywa niska i tak ma byc - liczy sie wiersz, z ktorego uczymy.
    """
    trafione = sprawdzalne = 0
    for k in range(KOLUMN):
        kod = ramka[5 + w * KOLUMN + k]
        wzor = ZNANE.get(kod)
        if wzor is None or not any(wzor):
            continue
        sprawdzalne += 1
        if komorka(w, k, x0, y0, s) == wzor:
            trafione += 1
    return trafione, sprawdzalne


x0, y0, s = X0, Y0, SKALA
print("siatka: poczatek %.2f x %.2f, piksel %.3f px" % (x0, y0, s))

for w in range(WIERSZY):
    t, n = ocen_wiersz(w, x0, y0, s)
    uczone = [k for k in range(KOLUMN) if ramka[5 + w * KOLUMN + k] in DO_NAUKI]
    print("  wiersz %d: zgodnych %d z %d%s" % (
        w, t, n, ("   <- tu uczymy %d komorek" % len(uczone)) if uczone else ""))

# Kod moze stac w kilku komorkach; bierzemy najczestszy wyglad i mowimy, czy byl zgodny.
from collections import Counter, defaultdict

zebrane = defaultdict(list)
for w in range(WIERSZY):
    for k in range(KOLUMN):
        kod = ramka[5 + w * KOLUMN + k]
        if kod in DO_NAUKI:
            zebrane[kod].append(komorka(w, k, x0, y0, s))

nowe = {}
for kod in DO_NAUKI:
    lista = zebrane.get(kod)
    if not lista:
        print("kodu 0x%02X nie ma w ramce - pomijam" % kod)
        continue
    mapa, ile = Counter(lista).most_common(1)[0]
    print("\n0x%02X (wystapien %d, zgodnych %d):" % (kod, len(lista), ile))
    for wiersz in mapa:
        print("   " + "".join("#" if (wiersz >> (KOM_SZER - 1 - i)) & 1 else "."
                              for i in range(KOM_SZER)))
    nowe[kod] = mapa

if not nowe:
    sys.exit(0)

# Dopisanie do KafelkiSpe.cs: kody uczone tutaj sa oznaczone zrodlem.
plik = os.path.join(K, "..", "KafelkiSpe.cs")
tresc = io.open(plik, encoding="utf-8-sig").read()
for kod, mapa in sorted(nowe.items()):
    wiersz = "        { 0x%02X, new byte[] { %s } },   // z instrukcji\n" % (
        kod, ", ".join("0x%02X" % w for w in mapa))
    wzor = re.compile(r"^        \{ 0x%02X, .*\n" % kod, re.M)
    if wzor.search(tresc):
        tresc = wzor.sub(wiersz, tresc)
    else:
        # wstawiamy tak, zeby kody zostaly po kolei
        miejsca = [(m.start(), int(m.group(1), 16))
                   for m in re.finditer(r"^        \{ 0x([0-9A-F]{2}), ", tresc, re.M)]
        poz = next((p for p, k in miejsca if k > kod), tresc.index("    };"))
        tresc = tresc[:poz] + wiersz + tresc[poz:]

io.open(plik, "w", encoding="utf-8-sig", newline="\r\n").write(tresc)
print("\ndopisano %d kafelkow do %s" % (len(nowe), os.path.normpath(plik)))
