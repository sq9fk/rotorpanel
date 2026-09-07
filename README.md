# RotorPanel

Most szeregowo-sieciowy dla sterowników rotorów **SPID Rot1Prog** wystawionych po TCP przez
`ser2net` na Raspberry Pi. Pozwala korzystać z nich w PstRotatorze tak, jakby były podłączone
lokalnie do portu COM.

```
Rot1Prog ──RS232──▶ /dev/antA3S ──ser2net:4001──▶ LAN ──RotorPanel──▶ CNCB10 ⇄ COM10 ──▶ PstRotator
```

Program **sam obsługuje transmisję** — nie potrzebuje `hub4com` ani żadnego zewnętrznego
mostka. Jedyną zależnością jest sterownik **com0com**, i to wyłącznie dlatego, że PstRotator
wymaga portu COM.

Jeden plik wykonywalny, około 113 KB, bez instalatora i bez środowiska uruchomieniowego —
działa na .NET Framework 4.8 wbudowanym w Windows 10 i 11.

## Pobieranie

Gotowy plik wykonywalny jest w [wydaniach](https://github.com/sq9fk/rotorpanel/releases/latest)
— jeden plik około 136 KB, bez instalatora.

---

## Wymagania

| | |
|---|---|
| System | Windows 10 lub 11 |
| Środowisko | .NET Framework 4.8 — **część systemu**, nic nie trzeba doinstalowywać |
| Sterownik | [com0com](https://sourceforge.net/projects/com0com/files/com0com/) w wersji **podpisanej** |
| Po stronie Pi | `ser2net` z portami TCP na urządzeniach szeregowych |

> Bierz paczkę, której nazwa kończy się na **`-signed`** — decyduje przyrostek, nie numer
> wersji. Najwygodniejsza jest `com0com-3.0.0.0-i386-and-x64-signed.zip`, bo obsługuje obie
> architektury naraz. Gałąź 2.2.2.0 też ma warianty podpisane, osobne dla x64 i i386
> (`com0com-2.2.2.0-x64-fre-signed.zip`). Paczki bez przyrostka `-signed`, czyli `-fre.zip`,
> zawierają sterownik niepodpisany i wymagają wyłączenia wymuszania podpisów w Windows —
> tych nie używaj.

---

## Instalacja

### Wariant przenośny

Skopiuj `RotorPanel.exe` gdziekolwiek i uruchom. Przy pierwszym starcie tworzy obok siebie
`rotory.json` z ustawieniami domyślnymi. Jeśli katalog jest tylko do odczytu, konfiguracja
ląduje w `%APPDATA%\RotorPanel`.

Zabierając ze sobą także `rotory.json`, przenosisz pełną konfigurację — na przykład na pendrive.

### Wariant z instalatorem

Paczka jest wygodna, gdy stawiasz program na kilku komputerach — sam plik wykonywalny
wystarcza, ale skróty i autostart trzeba by wtedy robić ręcznie. Składa ją
`instalator\zloz-paczke.bat`, do `dist\RotorPanel-paczka.zip`:

| plik | rola |
|---|---|
| `install.bat` | kopiuje program do `%LOCALAPPDATA%\RotorPanel`, tworzy skróty na pulpicie i w menu Start |
| `autostart.bat` | włącza lub wyłącza uruchamianie przy logowaniu |

Pary portów zakłada się już z samego programu, więc paczka nie zawiera osobnego skryptu.

---

## Pary portów com0com

Każdy rotor potrzebuje jednej pary portów wirtualnych. Jedna strona należy do PstRotatora,
druga do mostka:

```
COM10  ⇄  CNCB10        COM11  ⇄  CNCB11        COM12  ⇄  CNCB12
  │          │
  │          └── otwiera RotorPanel
  └── wybierasz w PstRotatorze
```

Pary zakłada się w oknie **Pary COM…**, przyciskiem *Nowa para…*. Program wywołuje `setupc.exe`
z podniesionymi uprawnieniami tylko na czas tej operacji.

> `setupc` szuka pliku `com0com.inf` w katalogu bieżącym. Wywołanie go z innego katalogu
> kończy się błędem `SetupOpenInfFile ... ERROR: 2`. Skrypty i program ustawiają katalog
> roboczy samodzielnie.

---

## Konfiguracja — `rotory.json`

Wszystko da się ustawić w oknie **Ustawienia…**; plik jest tylko magazynem. Ścieżki pisz
z ukośnikiem w przód — .NET na Windows traktuje je tak samo, a plik pozostaje poprawnym
JSON-em bez podwajania znaków.

| pole | znaczenie |
|---|---|
| `piIp` | domyślny adres serwera `ser2net` |
| `setupc` | ścieżka do `setupc.exe` z com0com |
| `sterownikAnten` | adres sterownika anten w postaci `host` albo `host:port`, np. `192.168.1.101` |
| `autoPolacz` | czy zestawiać mostki od razu po uruchomieniu |
| `sprawdzajAktualizacje` | czy sprawdzać przy starcie, czy jest nowsze wydanie |
| `rotory[].nr` | numer rotora, do którego odwołują się anteny |
| `rotory[].nazwa` | nazwa własna, np. „maszt A” |
| `rotory[].com` | port widoczny dla PstRotatora |
| `rotory[].dev` | druga strona pary, używana przez mostek |
| `rotory[].ip` | adres `ser2net` dla tego rotora; puste znaczy `piIp` |
| `rotory[].port` | port TCP wystawiony przez `ser2net` |
| `rotory[].protokol` | `surowy` albo `rfc2217` |
| `urzadzenia[]` | urządzenia inne niż rotory; te same pola co przy rotorze |
| `anteny[].nr` | numer wyjścia w przełącznicy antenowej |
| `anteny[].nazwa` | nazwa anteny, pobierana ze sterownika |
| `anteny[].rotor` | numer przypisanego rotora; zero oznacza antenę bez rotora |

Wzór znajdziesz w [`rotory.przyklad.json`](rotory.przyklad.json).

**Rotor definiuje się raz** — jako para portów plus punkt końcowy `ser2net` — a antena tylko
wskazuje, który rotor nią obraca. Maszt z kilkoma antenami to po prostu kilka anten wskazujących
ten sam rotor; mostek powstaje jeden, bo fizycznie to jedno połączenie.

Starsze konfiguracje, w których para i port były przypisane wprost do anteny, są przy wczytaniu
zamieniane na ten model. Wystarczy raz zapisać ustawienia, żeby plik zapisał się w nowej postaci.

---

## Urządzenia inne niż rotory

Osobna sekcja — w oknie głównym pod antenami, w Ustawieniach jako własna tabela — obsługuje
wszystko, co siedzi na porcie szeregowym, a rotorem nie jest: wzmacniacz, sterownik,
transceiver. Takie urządzenie nie jest przypisane do anteny, więc ma tylko parę portów,
adres, port i protokół.

### RFC 2217

Konwertery szeregowo-sieciowe, na przykład microBit RC-1216H, wystawiają port jako **RFC 2217**,
czyli Telnet z opcjami sterowania portem. To nie jest surowy strumień: bajt 255 jest tam
znacznikiem polecenia i w danych występuje podwojony, a przy nawiązaniu połączenia obie strony
negocjują opcje. Pompowanie bajtów bez zmian wpuściłoby te sekwencje do danych i popsuło
transmisję — dlatego protokół wybiera się przy urządzeniu.

Program zgłasza gotowość do trybu binarnego, rozpakowuje sekwencje sterujące i podwaja bajt 255
przy wysyłaniu.

### Parametry transmisji

RC-1216H trzyma swój port szeregowy zamknięty, dopóki klient nie poda prędkości — połączenie
zestawia się wtedy poprawnie, dioda świeci na zielono, a licznik odebranych bajtów stoi na zerze.
Dlatego w tabeli **Urządzenia** każdy wiersz ma cztery listy: **Prędkość**, **Bity**,
**Parzystość** i **Stop**. Po zestawieniu połączenia program wysyła je urządzeniu opcjami
COM-PORT-OPTION (SET-BAUDRATE, SET-DATASIZE, SET-PARITY, SET-STOPSIZE).

Wybór `z urządzenia` w kolumnie Prędkość nie wysyła niczego i zostawia urządzeniu jego własne
ustawienia — tak działa `ser2net`, gdzie format ramki jest w konfiguracji serwera.

Razem z parametrami program podnosi **DTR i RTS** i wyłącza sterowanie przepływem
(SET-CONTROL). Windows otwiera port szeregowy z podniesionymi liniami, a serwer RFC 2217 trzyma
je opuszczone, dopóki klient ich nie podniesie — bez tego część urządzeń milczy.

Na karcie w oknie głównym format widać obok nazwy protokołu, na przykład `RFC 2217 · 115200 8N1`.
Dla SPE Expert 1.3K-FA za konwerterem właściwe jest **115200 8N1**.

Ten sam wybór jest dostępny przy rotorach, gdyby `ser2net` był u kogoś skonfigurowany
z akcepterem `telnet` zamiast `tcp`.

---

## Wzmacniacz SPE Expert

W tabeli **Urządzenia** kolumna *Typ urządzenia* jest listą znanych modeli — wybór jednego
z nich włącza odczyt stanu i sterowanie, a przy okazji podstawia parametry transmisji
(RFC 2217, 115200 8N1). Listę można też zignorować i wpisać własny typ; wtedy urządzenie jest
zwykłym mostkiem szeregowym, dokładnie jak przedtem.

Po wybraniu modelu mostek raz na sekundę wysyła udokumentowaną komendę STATUS (`0x90`)
i rozkłada odpowiedź na pola, a karta w oknie głównym pokazuje stan zamiast trasy:

```
SPE Expert                                    połączony
RFC 2217 · 115200 8N1        RX 2,0 kB · TX 468 B · 185 B/s
Standby · RX · 20 m · ant 1a · 27 °C
```

Widać tryb (Standby/Operate), kierunek (RX/TX), pasmo, antenę z ATU, temperaturę, a przy
nadawaniu moc wyjściową i SWR anteny. Ostrzeżenie albo alarm wzmacniacza pojawia się jako
czerwony znacznik przy nazwie — na przykład `ALARM: SWR ponad limit` czy
`uwaga: przegrzanie`. Gdy odczyt się zestarzeje (ponad pięć sekund), karta wraca do opisu
trasy; lepiej nie pokazać nic niż nieaktualną temperaturę.

Ramka wygląda tak: `55 55 55` (do wzmacniacza) albo `AA AA AA` (od niego), bajt długości,
dane i suma kontrolna modulo 256. Odpowiedź STATUS ma 67 znaków rozdzielonych przecinkami.
Odpowiedzi na własne zapytania mostek **zdejmuje ze strumienia** — program po drugiej stronie
pary portów o nie nie prosił i nie ma powodu ich oglądać. Wszystko inne przechodzi nietknięte,
więc program sterujący (na przykład SPE Term) działa równolegle.

### Kontroler i mostek nie dzielą się portem

Zmierzone na RC-1216H: samo połączenie TCP, nawet z powitaniem Telnetu, niczego nie psuje —
kontroler stabilnie pokazuje stan wzmacniacza. Dopiero **ustawienie parametrów portu**
(SET-BAUDRATE i reszta) przejmuje łącze szeregowe, a wtedy kontroler przestaje odpytywać
wzmacniacz: jego strona *Amplifier* zaczyna przeskakiwać między `Remoted` i `Standby`,
a po dłuższej chwili pokazuje `Off/Unknown`, mimo że wzmacniacz działa i normalnie odpowiada.

Bez parametrów port po stronie urządzenia w ogóle się nie otwiera, więc nie da się tego ominąć.
To ograniczenie kontrolera, nie mostka: **albo strona kontrolera, albo pass-through**.
Kiedy mostek jest połączony, aktualny stan wzmacniacza widać w RotorPanelu, a nie na stronie
RC-1216H. Po rozłączeniu kontroler potrafi nie wrócić sam do odczytu — pomaga *Restart device*
na jego stronie; wzmacniacza to nie dotyczy.

### Sterowanie

Przycisk **Sterowanie…** na karcie otwiera okno z klawiaturą przedniego panelu i podglądem
wyświetlacza. Kody klawiszy pochodzą wprost
z firmowej tabeli poleceń: INPUT `0x01`, BAND −/+ `0x02`/`0x03`, ANTENNA `0x04`, L −/+
`0x05`/`0x06`, C −/+ `0x07`/`0x08`, TUNE `0x09`, WYŁĄCZ `0x0A`, POWER `0x0B`, DISPLAY `0x0C`,
OPERATE `0x0D`, CAT `0x0E`, strzałki `0x0F`/`0x10`, S `0x11`, podświetlenie `0x82`/`0x83`.

Cztery klawisze zmieniają stan nadawania albo zasilania — **OPERATE, TUNE, POWER i WYŁĄCZ** —
więc są wyróżnione na czerwono i pytają o potwierdzenie. U góry okna stoi ten sam odczyt stanu
co na karcie, a na dole informacja, co poszło do wzmacniacza.

### Podgląd wyświetlacza

Okno sterowania pokazuje **zawartość wyświetlacza wzmacniacza**, więc po jego menu da się
chodzić normalnie, a nie na ślepo. Producent tego nie opisuje — tryb podglądu włącza komenda
`0x80` (RCU ON), wyłącza `0x81`, a wzmacniacz przysyła wtedy ramki `0x6A` z 367 bajtami stanu
ekranu. Znaki są kodowane jako **ASCII pomniejszone o `0x20`** w całym zakresie `0x01`–`0x5F`,
więc prawdziwy znak to bajt + `0x20`. Stąd małe litery (`0x41`–`0x5A`), kropka (`0x0E`)
i myślnik (`0x0D`). Bajty od `0x60` w górę to własne znaki wyświetlacza.

Tryb RCU jest włączany na czas otwarcia okna i wyłączany przy zamknięciu. Wzmacniacz nie
przysyła ekranu sam z siebie — nawet po naciśnięciu klawisza — więc program wymusza świeżą
klatkę przełączeniem RCU wyłącz/włącz: raz zaraz po każdym klawiszu i dalej za każdym razem,
gdy poprzednia klatka już dotarła. Od polecenia do gotowej ramki mija u wzmacniacza około
**pół sekundy** i tego się nie przeskoczy — tyle zajmuje mu odrysowanie ekranu.

Ekran to **siatka 40 znaków na 8 wierszy, zaczynająca się od piątego bajtu ramki**. Nie było
tego w żadnym opisie — wyszło z pomiaru: separatory kolumn stoją co 40 bajtów (58, 98, 138,
178), a kreski obok tytułów trafiają dokładnie w początek i koniec wiersza właśnie przy tym
przesunięciu. Wynik zgadza się co do znaku ze zdjęciami ekranów w instrukcji Experta 1.3K-FA.
Za siatką idą jeszcze flagi kursora i dwubajtowa suma kontrolna.

Ramka nie ma pola długości: kończy się tam, gdzie zaczyna się następna synchronizacja
`AA AA AA`, a gdy ta nie przyjdzie — po 512 bajtach.

Poza znakami w ramce siedzą własne symbole wyświetlacza i program rysuje wszystkie: `0x8D` to
pozioma kreska, `0x8E` trójnik, `0x8F` pionowa kreska między kolumnami, `0x9F`–`0xA3` ramka
z rogami, `0xAA` stopień przy temperaturze, `0x99`–`0x9C` strzałki w podpowiedzi klawiszy,
a `0xB0`–`0xDF` kafelki logo.

**Zaznaczenie pozycji** siedzi w 40 bajtach za siatką — po jednym na kolumnę, a ustawiony bit
wskazuje wiersz. Wyszło z pomiaru: naciśnięcie strzałki przesuwa te bity o jeden, a podpowiedź
u dołu ekranu zmienia się razem z nimi. Program rysuje takie komórki w negatywie, więc po menu
widać, gdzie się stoi.

**Ramka wokół napisów** na ekranie głównym też jest w danych: `0x9F` biegnie górą, `0xA0` dołem,
`0xA1` pionowo po prawej, a `0xA2` i `0xA3` to rogi.

### Piksele, nie znaki

Wyświetlacz Experta ma **240 na 64 piksele**, czyli komórka to dokładnie 6 na 8. To jedna
liczba, ale wynika z niej cały sposób rysowania podglądu: program trzyma mapy bitowe w tej
rozdzielczości i powiększa je **całkowitą krotnością** (dwukrotnie, więc komórka ma 12 na 16
punktów, a cały ekran 480 na 128). Przy skali ułamkowej jedna i ta sama kreska wypada raz na
jednym, raz na dwóch pikselach — stąd brały się przerwy w ukośnych liniach logo, poziome belki
liter przesunięte o piksel i rozsypany znak stopnia.

Rysowane jest **wszystko z map bitowych, tekst też**. Czcionka systemowa dawała obraz z dwóch
światów: grafika pikselowa, a litery wygładzone i rozstrzelone, bo żaden krok czcionki nie
pasuje do sześciopikselowej komórki.

Skąd te mapy, skoro w ramce `0x6A` są same kody komórek, a pikseli nie ma:

* `narzedzia/ucz-kafelki.py` bierze zrzut ekranu wyświetlacza i ramkę `0x6A` z tego samego
  ekranu, dopasowuje zrzut do siatki 40×8 i wycina każdą komórkę, wiążąc ją z kodem z ramki.
  Wynik to `KafelkiSpe.cs` — znaki własne panelu, od `0x80` w górę.
* `narzedzia/ucz-czcionke.py` robi to samo dla tekstu i zapisuje `CzcionkaSpe.cs` — 96 znaków
  6 na 8.

Siatkę zrzutu wyznacza zasięg tuszu: górna krawędź ramki i wiersz kresek biegną przez całą
szerokość, a separatory sięgają samego dołu. Dopasowanie do pionowych separatorów wychodziło
o pół piksela obok, bo separator `0x8F` nie stoi przy krawędzi komórki, tylko w jej kolumnie 3.
Same komórki próbkujemy po odcieniach z wagami brzegów — zrzut jest powiększeniem w skali
ułamkowej, więc progowanie najpierw na czarno-białe dawało ten sam znak raz tak, raz inaczej,
zależnie od tego, w którym miejscu rastra wypadła komórka. Sprawdzian, że siatka jest trafiona:
**wszystkie 57 kodów graficznych i 35 sprawdzalnych liter wychodzą z każdego wystąpienia
identycznie**.

Ekrany inne niż główny mają własne symbole, których na głównym nie ma. Żeby dało się je
nauczyć, okno sterowania zapisuje na **Ctrl+S** bieżącą ramkę `0x6A` do podkatalogu `ekrany`
obok pliku programu. Sama ramka nie wystarczy — mówi tylko, jakim kodem wzmacniacz prosi
o daną komórkę — więc drugim źródłem są zdjęcia ekranów z instrukcji 1.3K-FA, a wiąże je
`narzedzia/ucz-z-instrukcji.py`. Tak powstały linijki mierników `PA OUT` i `I PA` w trybie
Operate (`0x81`–`0x84`) oraz strzałki `[◁▲][▽▷]` w podpowiedziach menu SET (`0x99`–`0x9C`).

Dowód, że zdjęcie z instrukcji i ramka z własnego wzmacniacza opisują ten sam układ, jest
w samym wyniku: przy dobrym dopasowaniu komórki, których kod już znamy, muszą wyjść ze
zdjęcia co do piksela takie same. W wierszu podpowiedzi zgodziły się 24 komórki z 28,
a niezgodne były **dokładnie te cztery strzałki**, o które chodziło.

Wypełnienie linijki to osobna rodzina kodów — widać ją w ramce ekranu V PA (Operate, potem
DISPLAY): `0x85` wypełniona zaślepka, `0x88` wypełniony odcinek, `0x8B` wypełniona podziałka,
`0x86` komórka na końcu belki. Te cztery kafelki są **wyprowadzone, nie zmierzone**, bo zdjęcia
z wypełnioną linijką nie ma w żadnej wersji instrukcji. Trzy wynikają wprost z geometrii: puste
kafelki rysują pudełko z krawędziami w wierszach 3 i 6, więc wypełnienie to ten sam kafelek
z zamalowanym środkiem. Czwarty wyliczyłem ze skali — podziałki stoją co pięć komórek, co daje
dwie kolumny pikseli na wolt, a odczyt 33,7 V wypada w komórce 18 na jej czwartej kolumnie.
Niepewność to jedna kolumna. W `KafelkiSpe.cs` są oznaczone jako `wyprowadzony`; zdjęcie ekranu
z wypełnioną linijką zastąpi je zmierzonymi.

Nieznany zostaje `0xAE` z wiersza „FAN SPINNING" na tym samym ekranie — żadne zdjęcie
w instrukcji go nie pokazuje, więc te komórki są puste.

Nie wszystko da się zmierzyć i to jest zapisane w wynikach:

* Ze zrzutu wolno wyciąć tylko komórki, o których wiadomo, że pokazują to samo co zapisana
  ramka — nazwę modelu i opisy pól paska stanu. Wyszło z tego 35 znaków. Aż 32 zgodziły się co
  do piksela z klasyczną czcionką 5×7 układów znakowych, a różniły się `D`, `t` i kropka; przy
  takiej zgodności resztę tablicy można było wziąć stamtąd. Zmierzone zawsze wygrywają, więc
  zrzut z nowymi znakami po prostu ich dołoży.
* Strzałki `0x99`–`0x9C` są narysowane ręcznie, bo nie ma ich na ekranie głównym i nie było
  czym ich zmierzyć. W `KafelkiSpe.cs` są tak oznaczone.

Dzięki temu ekran główny wygląda jak na panelu: logo SPE, ramka wokół napisów i linie działowe
są rysowane naprawdę, a nie zastępowane czymkolwiek.

Wiedza o ramce `0x6A` i o komendach RCU pochodzi z projektu
[vu2cpl/macexpert-spe](https://github.com/vu2cpl/macexpert-spe), gdzie ten protokół został
odtworzony.

---

## Aktualizacje

Kilka sekund po uruchomieniu program sprawdza, czy w [wydaniach](https://github.com/sq9fk/rotorpanel/releases)
pojawiła się nowsza wersja. Jeśli tak, pokazuje panel i pyta, czy ją pobrać. Po zgodzie
ściąga plik, podmienia się nim i uruchamia ponownie — bez instalatora i bez ręcznego
kopiowania.

Podmiana działa tak, że działający plik jest **przemianowywany** na `.old`: Windows nie
pozwala nadpisać uruchomionego programu, ale pozwala zmienić mu nazwę. Gdyby kopiowanie
nowego pliku się nie powiodło, poprzedni wraca na miejsce. Plik `.old` znika przy następnym
uruchomieniu.

Sprawdzanie można wyłączyć w Ustawieniach. Ręcznie wywołasz je z menu ikony w zasobniku,
pozycją *Sprawdź aktualizacje* — wtedy program mówi też, gdy wersja jest już najnowsza.

Jeśli program leży w katalogu wymagającym uprawnień, na przykład w `Program Files`, podmiana
się nie uda i zobaczysz komunikat z podpowiedzią, żeby przenieść go w inne miejsce.

---

## Interfejs

Okna dopasowują się do ekranu, na którym stoją. Gdy zawartość się nie mieści — a Ustawienia
potrzebują 1000 na 852 punkty, więc na laptopie bywa ciasno — okno zmniejsza się do obszaru
roboczego i włącza paski przewijania, zamiast chować dolne przyciski pod krawędzią pulpitu.
Okna główne, Ustawień, par COM i sterowania SPE można też zmieniać rozmiar ręcznie.


### Zasobnik systemowy

Program startuje **zminimalizowany**, jako ikona przy zegarze. Kolor igły kompasu pokazuje
stan zbiorczy: szary gdy nic nie działa, pomarańczowy przy łączeniu, zielony gdy przynajmniej
jeden mostek jest połączony.

| gest | efekt |
|---|---|
| kliknięcie lewym | pokazuje panel, a gdy jest otwarty — chowa go |
| kliknięcie prawym | menu: otwarcie panelu, łączenie pojedynczego rotora lub wszystkich, wyjście |
| krzyżyk w oknie | chowa program do zasobnika, **nie** zamyka go |

Program kończy się wyłącznie przez pozycję **Zamknij** w menu ikony. Przy zamykaniu wszystkie
mostki są rozłączane, więc porty po stronie `ser2net` i urządzeń zwalniają się od razu, a nie
dopiero po wygaśnięciu sesji. To samo dzieje się przy wylogowaniu i zamykaniu systemu.

Może działać tylko w jednej kopii. Ponowne uruchomienie — ze skrótu, z autostartu czy
z pliku exe — nie tworzy drugiej instancji, tylko wyciąga z zasobnika tę już działającą.

### Okno główne

Każda antena ma kartę z diodą stanu, opisem trasy, licznikami ruchu i przyciskiem
przełączającym. Okno dopasowuje wysokość do liczby anten, więc lista nigdy się nie przewija.

| dioda | znaczenie |
|---|---|
| szara | mostek zatrzymany |
| pomarańczowa | trwa łączenie albo ponawianie po zerwaniu |
| zielona | port otwarty i połączenie TCP zestawione |

Przy nazwie anteny pojawiają się **kolorowe oznaczenia nadajników** — `TRX1`, `TRX2` —
pokazujące, który nadajnik jest w danej chwili przełączony na tę antenę. Dane pochodzą wprost
ze sterownika przełącznicy i odświeżają się co 15 sekund, więc widać je na bieżąco, także gdy
ktoś przełączy antenę z panelu urządzenia albo z jego strony. Pod kursorem jest pełny opis
nadajnika, odczytany z jego etykiety w sterowniku.

W nagłówku okna, po prawej stronie, są dwie diody łączności. Górna dotyczy **`ser2net`**, dolna
**sterownika anten**. Pod kursorem pokazują szczegóły — przy `ser2net` stan każdego portu z osobna.

| kolor | ser2net | sterownik anten |
|---|---|---|
| zielona | wszystkie porty odpowiadają | odpowiada |
| pomarańczowa | część portów odpowiada | trwa sprawdzanie |
| czerwona | żaden port nie odpowiada | nie odpowiada |
| szara | nie zdefiniowano rotorów | nie podano adresu |

`ser2net` sprawdzany jest samym nawiązaniem połączenia TCP, co 60 sekund po udanej próbie
i co 20 po nieudanej. Sterownik anten odpytywany jest pełnym zapytaniem HTTP co 15 sekund,
bo przy okazji dostarcza przypisanie anten do nadajników — jego własna strona odświeża się
co 10 sekund, więc takie tempo go nie obciąża. **Porty z działającym mostkiem nie są badane** — każdy port `ser2net` przyjmuje jedno
połączenie, a przy ustawieniu `kickolduser` próba nawiązania drugiego rozłączyłaby własny mostek.
Działający mostek i tak jest dowodem, że port odpowiada.

Liczniki `RX` i `TX` pokazują bajty, które faktycznie przeszły, oraz bieżącą przepustowość.
To najprostszy sposób sprawdzenia, czy PstRotator w ogóle odpytuje sterownik: zielona dioda
przy zerowych licznikach znaczy, że łącze stoi, ale nikt z niego nie korzysta.

Po zerwaniu połączenia mostek ponawia próbę co 2 sekundy, więc restart Raspberry albo
chwilowy zanik sieci nie wymagają żadnej reakcji.

### Ustawienia

Tabela anten: numer, checkbox **Rotor**, nazwa, para portów z listy rozwijanej, adres i port TCP.

- **Adres sterownika anten** podaje się jako `host` albo `host:port`, bez `http://` i bez
  ścieżki. Starsze zapisy w postaci pełnego adresu URL są przy wczytywaniu sprowadzane do tej
  postaci.
- **Ścieżkę do `setupc.exe`** można wskazać przyciskiem `…`, który otwiera okno wyboru pliku.
  Startuje z obecnego katalogu, a gdy go nie ma — z typowego miejsca instalacji com0com.
- **Rotory i anteny są w osobnych tabelach.** Rotor to para portów, adres i port TCP; antena
  wybiera rotor z listy, która powstaje automatycznie z tabeli rotorów i pokazuje pełną trasę.
- **Para zajęta przez jeden rotor nie pojawia się na liście u pozostałych**, więc nie da się
  przypisać jej dwa razy. Nazwy rotorów też muszą być różne — to po nich wybiera się rotor
  przy antenie.
- **Nazwy anten są nieedytowalne** — pochodzą wyłącznie ze sterownika, przycisk *Pobierz nazwy*.
  Przy braku łączności zostają `ANT1`–`ANT6`.
- **Pary wybiera się z listy**, budowanej z rejestru sterownika com0com. Nie da się wpisać
  nieistniejącego portu ani pomylić stron pary.
- Wybranie pary samo zaznacza *Rotor*; odznaczenie *Rotora* czyści parę, adres i port.
- Pole adresu jest opcjonalne — puste znaczy „użyj domyślnego". Pozwala trzymać część
  rotorów na innym Raspberry.

### Pary COM

Lista par istniejących w systemie, z osobnym stanem każdej strony (`wolny`, `zajęty`,
`nie istnieje`, albo konkretny kod błędu Win32). Pogrubione są pary używane przez
konfigurację anten.

- **Nowa para…** zakłada parę o dowolnych nazwach. Numery są podpowiadane z pierwszego
  wolnego, ustalanego z arbitra nazw COM, listy portów systemu i istniejących par.
  Strona dla PstRotatora musi nazywać się `COMxx`, druga może mieć dowolną nazwę.
- **Usuń zaznaczoną** kasuje jedną parę. Jeśli używa jej konfiguracja, program mówi
  których anten dotyczy, zanim cokolwiek zrobi.
- **Usuń wszystkie** czyści wszystkie pary com0com w systemie, także cudze — stąd osobne
  ostrzeżenie.

Usuwanie sprząta też rejestr. `setupc remove` kasuje urządzenia, ale zostawia po sobie wpisy —
program dokłada ich skasowanie do tej samej operacji, więc po usunięciu pary nie zostaje żaden
ślad. Dotyczy to również przycisku *Usuń wszystkie*.

Gdyby mimo to trafił się wiersz wyszarzony i opisany jako *osierocony* — na przykład po parze
usuniętej wcześniej z wiersza poleceń — można go zaznaczyć i usunąć tym samym przyciskiem.
Takie wpisy nie są proponowane przy wyborze pary i nie blokują numerów COM.

---

## PstRotator

Wybierz sterownik **Alfa SPID RAK / Rot1Prog**, port `COM10` (lub odpowiedni), 1200 8N1.

> **Nie wybieraj Rot2Prog.** Rot1Prog odpowiada ramką **5-bajtową** `57 H1 H2 H3 20`, gdzie
> azymut = `H1*100 + H2*10 + H3 − 360`. Rot2Prog czeka na 12 bajtów i azymut nigdy się nie
> pojawi. W liście PstRotatora modele SPID występują pod oznaczeniami handlowymi: **RAK**
> używa protokołu Rot1Prog, natomiast RAU, RAS i BIG-RAS używają Rot2Prog.

Przy kilku rotorach potrzeba kilku instancji PstRotatora — każda w osobnym katalogu i z innym
portem „rotor" (12040, 12042, 12044).

PstRotator **nie jest klientem `rotctld`**. Jego własne TCP, domyślnie na portach 4001 i 4002,
to para klient–serwer między dwoma komputerami z PstRotatorem, a nie połączenie do serwera
szeregowego.

---

## Strona Raspberry Pi

Przykładowy fragment `/etc/ser2net.yaml`:

```yaml
connection: &antA3S
  accepter: tcp,0.0.0.0,4001
  enable: on
  options:
    kickolduser: true
  connector: serialdev,/dev/antA3S,1200n81,local
```

Istotne szczegóły:

- `tcp`, nie `telnet` — protokół SPID jest binarny, a negocjacja telnetu psuje ramki
- `1200n81` — fabryczna prędkość Rot1Prog
- `local` — ignoruje linie modemowe, bez tego tanie przejściówki potrafią nie otworzyć portu
- `kickolduser` — nowy klient wywala zawieszoną starą sesję

Stałe nazwy urządzeń warto nadać regułami udev, na przykład po numerze seryjnym przejściówki
FTDI, a przy układach bez numeru seryjnego po gnieździe USB.

---

## Diagnostyka

**Brak azymutu w PstRotatorze przy zielonej diodzie i zerowych licznikach.** PstRotator nie
otworzył portu albo ma wybrany zły sterownik. Sprawdź, czy to na pewno RAK / Rot1Prog.

**Port COM nie istnieje, mimo że para jest w rejestrze.** Sprawdź parametry pary w oknie
*Pary COM*. `ExclusiveMode=1` powoduje, że port powstaje dopiero, gdy druga strona pary jest
otwarta, więc w systemie go nie widać. `EmuBR=1` dławi transmisję do ustawionej prędkości.
Naprawa:

```
setupc change CNCA1 PortName=COM11,EmuBR=no,ExclusiveMode=no
```

Uwaga: przeładowanie urządzenia com0com powoduje, że sterownik zapisuje te wartości z
powrotem do rejestru.

**Mostek łączy się i natychmiast rozłącza.** Sprawdź, czy `ser2net` po stronie Pi na pewno
działa — każdy port przyjmuje **jedno** połączenie na raz.

**Nic nie przechodzi mimo zestawionego łącza.** Upewnij się, że po stronie Raspberry
`rotctld` jest wyłączony dla tych urządzeń. Port szeregowy ma jednego właściciela.

Test toru z pominięciem com0com, prosto do `ser2net` (zatrzymaj wcześniej mostek):

```powershell
$c=[Net.Sockets.TcpClient]::new('192.168.1.100',4001); $s=$c.GetStream()
$s.Write([byte[]](0x57,0,0,0,0,0,0,0,0,0,0,0x1F,0x20),0,13)
Start-Sleep -Milliseconds 500
$r=New-Object byte[] 12; $n=$s.Read($r,0,12)
($r[0..($n-1)] | % { $_.ToString('X2') }) -join ' '
$c.Close()
```

Poprawna odpowiedź zaczyna się od `57` i kończy `20`.

---

## Budowanie

Wymaga .NET SDK (dowolnego nowoczesnego — projekt jest w formacie SDK-style, choć celuje
w .NET Framework 4.8).

```
dotnet build
dotnet run
```

Wersja do rozdania, jeden plik wykonywalny:

```
dotnet publish -c Release -p:DebugType=none -o dist\portable48
```

Ikonę generuje skrypt `ikona\generuj-ikone.ps1` — rysuje kompas ze wskaźnikiem kierunku
w siedmiu rozmiarach i składa plik `.ico`. Uruchamiać tylko po zmianie wyglądu ikony.

Wydanie powstaje z opublikowanego pliku wykonywalnego:

```
gh release create v1.2.0 dist\portable48\RotorPanel.exe --title "RotorPanel 1.2.0" --notes-file notatki.md
```

## Układ projektu

Klasy okien są dzielone na pliki częściowe, żeby żaden nie urósł ponad czytelną długość.

| plik | rola |
|---|---|
| `Program.cs` | punkt wejścia, blokada jednej instancji, obsługa nieprzechwyconych wyjątków |
| `Config.cs` | model: rotory, anteny, ustawienia ogólne |
| `ConfigIO.cs` | wczytywanie i zapis konfiguracji wraz z migracją starszego formatu |
| `Json.cs`, `JsonZapis.cs` | własny czytnik i zapisywacz JSON — brak zależności zewnętrznych |
| `PortIo.cs` | dostęp do portu szeregowego przez Win32 |
| `Mostek.cs` | mostek dwukierunkowy port ⇄ TCP, ponawianie, liczniki |
| `StrumienTelnet.cs` | warstwa Telnet dla serwerów RFC 2217 |
| `MainForm.cs` | okno główne: budowa listy i układ |
| `MainForm.Stan.cs` | karty anten i cykl odświeżania |
| `MainForm.Siec.cs` | diody łączności oraz oznaczenia nadajników |
| `MainForm.Tray.cs` | ikona w zasobniku, menu, chowanie i zamykanie |
| `MainForm.Aktualizacja.cs` | pytanie o aktualizację i jej instalacja |
| `Aktualizacja.cs` | odczyt wydań z GitHuba i podmiana pliku programu |
| `SettingsForm.cs` | okno ustawień: nagłówek i przyciski |
| `SettingsForm.Siatki.cs` | tabele rotorów i anten |
| `SettingsForm.Urzadzenia.cs` | tabela urządzeń |
| `SettingsForm.Akcje.cs` | pary, wybór pliku, pobieranie nazw, zapis |
| `PairsForm.cs` | lista par com0com, zakładanie i usuwanie |
| `NewPairForm.cs` | okienko nowej pary z walidacją nazw |
| `Com0Com.cs` | odczyt par z rejestru i wywołania `setupc` |
| `SterownikAnten.cs` | odczyt nazw anten i przypisania nadajników ze sterownika |
| `SpeForm.cs` | okno sterowania wzmacniaczem SPE: klawiatura i podgląd |
| `StatusSpe.cs` | rozbiór odpowiedzi na `0x90` — model, pasmo, moc, temperatura |
| `CzytnikSpe.cs` | wyjmowanie ramek statusu i ekranu ze strumienia do klienta |
| `EkranSpe.cs` | rozbiór ramki `0x6A` na siatkę 40×8 i flagi kursora |
| `PodgladLcd.cs` | rysowanie wyświetlacza z map bitowych |
| `KafelkiSpe.cs`, `CzcionkaSpe.cs` | mapy bitowe znaków panelu — **generowane**, patrz `narzedzia/` |
| `narzedzia/` | skrypty uczące map bitowych ze zrzutu wyświetlacza |
| `Theme.cs`, `Ui.cs` | paleta oraz kontrolki własne: karta, dioda, znacznik |
| `instalator/` | skrypty instalacyjne i skrypt składania paczki |
| `ikona/` | generator ikony i gotowy plik `.ico` |

## Licencja

MIT — patrz [LICENSE](LICENSE).

## Powiązane projekty

- [ant-sw-2x6](https://github.com/sq9fk/ant-sw-2x6) — przełącznica antenowa 6x2, źródło nazw anten
- [k3ng_controler_nano_light](https://github.com/sq9fk/k3ng_controler_nano_light) — firmware sterownika rotora
- [rotator_wifi_bridge](https://github.com/sq9fk/rotator_wifi_bridge) — mostek WiFi dla rotora
