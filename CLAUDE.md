# Wskazówki dla Claude

Kontekst i decyzje projektowe, które łatwo nieświadomie cofnąć. Przeczytaj przed zmianami.

## Czym to jest

Program mostkuje port szeregowy sterownika rotora SPID Rot1Prog, wystawiony po TCP przez
`ser2net` na Raspberry Pi, do wirtualnego portu COM na Windows, żeby mógł z niego korzystać
PstRotator. Szczegóły użytkowe w [README.md](README.md).

## Polecenia

```
dotnet build
dotnet publish -c Release -p:DebugType=none -o dist\portable48
```

Przed budowaniem zamknij działającą instancję — inaczej MSBuild nie nadpisze pliku
wykonywalnego. Program bywa też trzymany przez otwarty Eksplorator w katalogu projektu.

Sprawdzenie po zmianie: uruchom exe, potwierdź że proces żyje bez okna głównego (startuje
do zasobnika), a przy `autoPolacz` mostki zestawiają połączenia:

```powershell
Get-NetTCPConnection -RemotePort 4001,4002,4003 -State Established
```

Wydanie: podnieś `Version` w `RotorPanel.csproj` **oraz** `assemblyIdentity version` w
`app.manifest`, opublikuj, a potem `gh release create vX.Y.Z dist\portable48\RotorPanel.exe`.
Do wydania idzie **sam plik wykonywalny** — bez paczki instalacyjnej.

## Struktura

Okna są dzielone na pliki częściowe według roli, nie mechanicznie: `MainForm.Stan.cs` to karty
i cykl odświeżania, `MainForm.Siec.cs` diody łączności i oznaczenia nadajników,
`MainForm.Tray.cs` zasobnik. Podobnie `SettingsForm` — nagłówek, tabele, akcje. Trzymaj się
tego podziału, zamiast dokładać do pliku, który akurat otworzyłeś.

## Decyzje, których nie odwracać bez powodu

**.NET Framework 4.8, nie .NET 8.** Windows 11 nie ma .NET 8 w standardzie — ma tylko
Framework 4.8. Wersja samodzielna .NET 8 waży 63 MB, a przycinanie jest dla WinForms
zablokowane przez SDK błędem `NETSDK1175`. Ta wersja to jeden plik 113 KB działający wszędzie.
To był świadomy wybór po zmierzeniu obu wariantów.

**Własny JSON zamiast System.Text.Json.** W Framework 4.8 wymagałby pakietu NuGet i sześciu
bibliotek obok pliku wykonywalnego albo scalania Costurą. Konfiguracja jest płaska, więc
własny parser (`Json.cs`, `JsonZapis.cs`) jest tańszy niż ta zależność. **Nie dodawaj pakietów
NuGet** — cała wartość tej wersji leży w jednym samowystarczalnym pliku.

**Dostęp do portu przez Win32 `CreateFile`, nie `System.IO.Ports`.** Klasa `SerialPort`
odrzuca nazwy nierozpoczynające się od `COM`, a druga strona pary com0com nazywa się
`CNCB10`. `PortIo.cs` otwiera urządzenie bezpośrednio i ustawia `COMMTIMEOUTS` tak, że odczyt
wraca natychmiast po nadejściu danych, a przy ciszy po 200 ms z zerem bajtów — dzięki temu
pętla reaguje na żądanie zatrzymania.

**Odczyt z gniazda w .NET Framework nie reaguje na token anulowania.** Samo odwołanie tokenu
zostawia pompę zawieszoną na `Read` aż do nadejścia danych, więc `Mostek.Stop` zamyka jawnie
uchwyt gniazda i portu — dopiero to przerywa odczyt. Bez tego każde zatrzymanie czekało pełny
limit czasu, a połączenie TCP trwało do końca procesu. Zmierzone: rozłączenie trzech mostków
zajmuje 4 ms zamiast trzech razy po 2,5 sekundy.

**Zero bajtów z portu to cisza, z gniazda to rozłączenie.** Rozróżnienie w `Mostek.Pompa`
jest istotne; potraktowanie ich tak samo albo zapętli program, albo zerwie działające łącze.

**Wymuszenie uchwytu okna w `UtworzTray`.** Formularz, który nigdy nie był pokazany, nie
zgłasza `HandleDestroyed`, więc `Close()` nie kończy pętli komunikatów. Program wychodzi przez
`Application.Exit()` po posprzątaniu, nie przez `Close()`.

**RFC 2217 to Telnet, nie surowy strumień.** Bajt 255 jest znacznikiem polecenia i w danych
musi być podwojony, a serwer przy połączeniu negocjuje opcje. `StrumienTelnet` rozpakowuje te
sekwencje i odpowiada na negocjację; bez tego dane byłyby losowo psute. Warstwa włącza się
polem `protokol` przy połączeniu, wspólnym dla rotorów i urządzeń.

**`Polaczenie` jest wspólną podstawą rotora i urządzenia.** `Mostek` pracuje na niej, więc
nowy rodzaj punktu końcowego wystarczy wyprowadzić z tej klasy — nie dubluj logiki mostka.

**Rotor jest bytem osobnym od anteny.** `Rotor` trzyma parę portów i punkt `ser2net`, a `Antena`
tylko numer rotora. Kilka anten na wspólnym maszcie wskazuje ten sam rotor i dzieli jedno
połączenie — inaczej dwa mostki biłyby się o ten sam port. Starszy format, w którym para i port
wisiały przy antenie, jest migrowany w `ConfigIO.CzytajAnteny`; nie usuwaj tego kodu.

**Portów ser2net z działającym mostkiem nie wolno sondować.** Każdy port przyjmuje jedno
połączenie, a przy `kickolduser` próba nawiązania drugiego rozłącza własny mostek. `OdswiezSer2net`
traktuje połączony mostek jako dowód dostępności i pomija badanie.

**Adres sterownika anten trzymamy jako `host[:port]`, nie URL.** `Config.NormalizujHost`
obcina schemat i ścieżkę przy wczytywaniu i zapisie, a `SterownikAnten.Rozdziel` rozbija to
na host i port. Adres URL sam składa się dopiero w momencie pobierania nazw.

**Nazwy anten są tylko do odczytu.** Jedynym źródłem jest sterownik przełącznicy. Ręczna edycja
rozjechałaby się z rzeczywistością przy pierwszym pobraniu.

**Aktualizacja podmienia plik przez przemianowanie.** Windows nie pozwala nadpisać
uruchomionego programu, ale pozwala zmienić mu nazwę — stąd `.old`. Przed startem nowej kopii
`Program.ZwolnijBlokade()` musi puścić muteks jednej instancji, inaczej nowa kopia uzna się za
drugą i tylko obudzi tę kończącą pracę. Nowa kopia dostaje argument `--po-aktualizacji`
i czeka do dziesięciu sekund na zwolnienie blokady.

**Model urzadzenia jest tekstem, nie flaga.** `Urzadzenie.Typ` trzyma nazwe modelu, a
`Urzadzenie.Spe` sprawdza tylko, czy jest ona na liscie `TypyZeStanem`. Dzieki temu w kolumnie
*Typ urzadzenia* mozna wpisac cokolwiek wlasnego, nie wlaczajac przy tym odpytywania protokolem,
ktorego nie znamy. Starsze pliki z `"spe": true` migruje `ConfigIO.TypUrzadzenia`.

**Siatka ekranu to 40 kolumn na 8 wierszy od piatego bajtu.** Dochodzenie do tego szlo przez
dwa bledne zalozenia: najpierw 32 kolumny od bajtu 0 (tekst lamal sie w polowie slowa), potem 48
(SAVE rozjezdzalo sie na dwa wiersze). Rozstrzygnely dwie rzeczy: separatory kolumn `0x8F` stoja
co 40 bajtow, a kreski `0x8D` trafiaja w poczatek i koniec wiersza tylko przy przesunieciu 5.
Sprawdzian koncowy to zdjecia ekranow w instrukcji Experta 1.3K-FA - renderowanie zgadza sie
z nimi co do znaku. Jesli kiedys nie bedzie sie zgadzac, porownaj z `obraz-12.jpg`
(SET ANTENNA ON BANK "A") wyciagnietym z tego PDF-a.

**Zapis do pary com0com potrafi zablokowac cala pompe.** Gdy po drugiej stronie pary nikt
nie czyta, `WriteAsync` na porcie stoi - zmierzone **2,77 sekundy na szesc bajtow**. Robiony
wprost w pompie zatrzymywal odbior z sieci, wiec ramki ekranu czekaly w buforze i podglad
chodzil po 1-2 sekundy zamiast po 600 ms. Dlatego kierunek siec-do-portu idzie przez kolejke
(`Mostek.Oddaj` i `PisarzPortu`): odbior nigdy nie czeka na zapis, a gdy kolejka rosnie ponad
256 porcji, znaczy to, ze odbiorcy nie ma, i najstarsze dane odpadaja. Nie wracaj do zapisu
wprost w pompie.

**Odpytywanie o status przy otwartym podgladzie trzeba wylaczyc.** Zapytanie `0x90` wciskajace
sie miedzy puls a klatke opoznialo ja o ponad sekunde. Przy `TrybEkranu` petla `OdpytujSpe`
tylko spi - stan i tak widac na ekranie wzmacniacza.

**Wzmacniacz czasem przemilcza puls.** Potrafi nie odpowiedziec przez ponad trzy sekundy, do
nastepnego pulsu z zegara. Dlatego po klawiszu czekamy 700 ms na klatke i ponawiamy puls
(dwie proby). Po tych trzech poprawkach zmierzone: typowo **850-950 ms** od klikniecia do
zmiany obrazu, sporadycznie do 2,4 s, bez gubienia klawiszy.

**Puls tuz po klawiszu kasuje ten klawisz.** Zmierzone na Expercie 1.3K-FA: przy zwloce 0
i 20 ms miedzy klawiszem a przelaczeniem RCU wzmacniacz nie zmienia ekranu **w ogole**, przy
60 ms nowa klatka jest po ~550 ms, przy 200 ms po ~720 ms. Stad `SpeForm` czeka 60 ms po
klawiszu i na ten czas wstrzymuje puls z zegara (`_klawiszWToku`) - bez tej blokady timer
potrafil strzelic w zla chwile i klawisz przepadal, a uzytkownik czekal na kolejny cykl.

**Wzmacniacz nie przysyla ekranu sam - nawet po klawiszu.** Zmierzone: po nacisnieciu
strzalki bez wymuszenia nie przychodzi nic przez trzy sekundy. Swieza klatke daje dopiero
przelaczenie RCU wylacz/wlacz z przerwa okolo 20 ms (przy 5 ms wzmacniacz juz nie reaguje),
a od klawisza do ramki mija u niego okolo **500 ms** - to jest podloga, ponizej ktorej program
nie zejdzie. Dlatego `SpeForm` pulsuje zdarzeniowo: kolejny puls dopiero po odebraniu klatki,
plus jeden zaraz po kazdym klawiszu. Nie podkrecaj tego sztywnym taktem - polecenia zaczna sie
pietrzyc szybciej, niz wzmacniacz odpowiada.

**Nie czekaj z ramka ekranu na nastepna synchronizacje.** Ramka nie ma pola dlugosci, ale
czekanie na nastepny naglowek opoznialo podglad o cale odpytanie, bo kolejna ramka przychodzi
dopiero przy nastepnym pulsie. `CzytnikSpe` bierze wiec typowa dlugosc (367 bajtow), gdy juz ja
ma, a na synchronizacje czeka tylko wtedy, gdy ramka jest krotsza.

**Ramka ekranu glownego jest w danych, logo nie.** Odczytane z ulozenia bajtow: `0x9F` gora
ramki, `0xA0` dol, `0xA1` bok, `0xA2` i `0xA3` rogi, `0x8E` trojnik nad separatorem kolumny.
Natomiast `0xB0`-`0xDF` to kafelki mapy bitowej logo - kazdy bajt inny wycinek obrazka, wiec bez
tablicy znakow wyswietlacza nie da sie ich narysowac. Szukalem jej w `Term_13k_232.exe`
(zasoby to formularze Delphi, tablicy tam nie ma) i probowalem uzyc Terma jako wzorca, podajac
mu ramki 0x6A przez mostek - nie rysuje ramek, o ktore sam nie prosil. Droga, ktora zostaje:
nauczyc sie kafelkow ze zdjecia ekranu dopasowanego do siatki 40x8.

**Znaki to ASCII minus 0x20 - w calym zakresie.** Pierwsza wersja dekodera traktowala
`0x10`-`0x3F` jako "znaki z atrybutem" (+0x20), a `0x40`-`0x7E` jako zwykly ASCII. Efekt:
"SOLID STATE" zamiast "Solid State", "20 M" zamiast "20 m", a kropka i myslnik z "1.3K-FA"
znikaly, bo siedza w bajtach `0x0E` i `0x0D`. Poprawna regula: **znak = bajt + 0x20 dla
0x01-0x5F**, reszta to wlasne znaki wyswietlacza. Sprawdzone ze zdjeciem panelu przyslanym
przez uzytkownika - zgadza sie co do znaku.

**Ostatniego ekranu nie kasuj po czasie.** Podglad mial go zerowac po szesciu sekundach bez
nowej klatki i pokazywac "czekam na wyswietlacz". Poniewaz wzmacniacz co jakis czas przemilcza
puls, napis mrugal bez powodu - a ekran przeciez nadal pokazuje to samo. Teraz ostatnia klatka
zostaje na widoku do nastepnej.

**Wlasne znaki wyswietlacza rozpoznane do tej pory:** `0x8D` pozioma kreska, `0x8F` pionowa,
`0x8E` trojnik nad separatorem kolumny, `0x99`/`0x9A`/`0x9B`/`0x9C` strzalki w podpowiedzi
klawiszy (para na jeden nawias), `0xAA` stopien przy temperaturze. Kafelki `0x9F`-`0xDF` to mapa bitowa logo i wykresow - rysowanie ich
jednym znakiem dawalo pole szumu, wiec zostaja puste.

**Kursor to jeden bajt na kolumne, bit wskazuje wiersz.** 40 bajtow zaraz za siatka
(`EkranSpe.PoczatekFlag`), potem dwubajtowa suma kontrolna. Zmierzone przez porownanie ramek
przed i po nacisnieciu strzalki: 13 kolejnych bajtow zmienilo sie z `08` na `04`, czyli
podswietlenie przeskoczylo z wiersza 3 na 2 - i zgadza sie to z podpowiedzia u dolu ekranu,
ktora opisuje wybrana pozycje. Reguła jest wspolna dla wszystkich ekranow, wiec nie potrzeba
osobnych dekoderow per ekran, jak ma `macexpert-spe`.

**`0x8D` to kreska, nie tlo w negatywie.** Pierwsza wersja rysowala wiersz z tym znakiem jako
zielony pasek. Na zdjeciach w instrukcji widac, ze to zwykle poziome myslniki obok tytulu -
renderujemy je jako `U+2500`, a `0x8F` jako `U+2502`. Zaznaczenie pozycji (negatyw na panelu)
siedzi w flagach kursora za siatka i nie jest odtworzone.

**Ramka ekranu nie ma dlugosci - ramuj ja synchronizacja.** Poczatkowo zakladalismy stale
367 bajtow. Tak jest w praktyce na 1.3K-FA, ale `macexpert-spe` ramuje od `AA AA AA 6A` do
nastepnej synchronizacji, z limitem 512 bajtow, i to jest odporniejsze. `CzytnikSpe` robi tak
samo; `EkranSpe.Rozbierz` przyjmuje dlugosc, zamiast jej zakladac.

**Pokazujemy ekran wzmacniacza, nie skladamy wlasnego.** `macexpert-spe` rozbiera ramke na pola
i rysuje wlasny interfejs z szescioma dekoderami kursora per ekran. My renderujemy siatke znakow
w `PodgladLcd` - mniej kodu, a menu wyglada tak jak na panelu i dziala dla ekranow, ktorych nikt
nie rozbieral. Znaki `0x8F` (kreska) i `0x8D` (tlo paska tytulu) sa odwzorowane; reszta symboli
idzie jako spacja.

**Ramki od wzmacniacza rozbieraj po kolei, nie po rodzaju.** Pierwsza wersja `CzytnikSpe`
szukała najpierw ramek ekranu w całym buforze i oddawała klientowi wszystko, co leżało przed
nimi — razem z ramkami statusu. Efekt: podgląd zamierał, a karta pokazywała „czekam na odczyt
stanu". Teraz pętla bierze **najwcześniejszy** nagłówek `AA AA AA`, patrzy na bajt rodzaju
(`0x43` status, `0x6A` ekran) i dopiero wtedy decyduje. Potwierdzenia i wszystko inne idą do
klienta.

**Status SPE czytamy sami i sami go zjadamy.** `Mostek` przy `Urzadzenie.Spe` wstrzykuje
w strumień własne zapytanie `0x90` raz na sekundę, a `CzytnikSpe` wyjmuje odpowiedzi zanim
trafią na drugą stronę pary portów. Klient (SPE Term) o nie nie prosił, więc nie może ich
dostać. Do gniazda piszą wtedy dwie strony — pompa i odpytywanie — stąd semafor `_bramka`;
bez niego ramki potrafiłyby się przepleść w połowie.

## Pułapki, na które już wpadliśmy

**`setupc` wymaga katalogu roboczego.** Szuka `com0com.inf` w katalogu bieżącym; wywołany
skądinąd zwraca `SetupOpenInfFile ... ERROR: 2`. Generowany plik `.bat` zaczyna się od
`cd /d` do katalogu com0com.

**Przypisanie anten do nadajników czytamy z klas CSS.** Przyciski wyboru anteny nazywają się
`S{trx}{antena}`, a ten odpowiadający aktualnemu wyborowi ma klasę `g`. Tytuły przycisków
`F{trx}0` dają opis nadajnika. Parsowanie jest w `SterownikAnten.CzytajTrx`; jeśli firmware
zmieni nazwy klas, to jest miejsce do poprawy.

**Sterownik przełącznicy nie wystawia JSON-a.** Firmware `sq9fk/ant-sw-2x6` oddaje tę samą
stronę HTML na każdą ścieżkę i przyjmuje polecenia jako parametry zapytania, np. `GET /?N1=...`.
Nazwy czytamy z pól formularza `N1`..`N6`. Obsługa JSON w `SterownikAnten.cs` jest zapasem na
wypadek, gdyby firmware kiedyś taki endpoint dostał — nie usuwaj jej, ale nie zakładaj, że działa.

**Parametry par com0com potrafią ukryć port.** `ExclusiveMode=1` sprawia, że port COM powstaje
dopiero po otwarciu drugiej strony pary, więc w systemie go nie widać mimo wpisu w rejestrze.
`EmuBR=1` dławi transmisję. Okno *Pary COM* oznacza takie parametry wykrzyknikiem.

**`setupc remove` zostawia wpisy w rejestrze.** Usuwa urządzenia, ale podklucze `CNCAn`
i `CNCBn` z `PortName` zostają. Dlatego usuwanie pary w `PairsForm` dokłada do tej samej
operacji `reg delete` obu podkluczy — inaczej po każdym skasowaniu zostawałby duch.
Niezależnie od tego `ParaPortow.Istnieje` sprawdza, czy choć jedna strona pary jest obecna
w systemie; osieroconych wpisów nie proponujemy przy wyborze pary i nie liczymy jako
zajmujących numery COM, bo mogą pochodzić z usunięcia zrobionego poza programem.

**Rot1Prog odpowiada ramką 5-bajtową**, nie 12-bajtową jak Rot2Prog. Format: `57 H1 H2 H3 20`,
azymut = `H1*100 + H2*10 + H3 − 360`.

**RC-1216H nie otwiera portu szeregowego, dopóki nie dostanie prędkości.** Połączenie TCP
zestawia się normalnie, negocjacja Telnetu przechodzi, dioda jest zielona — i nie przychodzi ani
jeden bajt. Zapytanie SET-BAUDRATE z wartością 0 zwraca w odpowiedzi 0, co potwierdza, że port po
stronie urządzenia jest zamknięty. Dlatego `StrumienTelnet.UstawParametry` wysyła po nawiązaniu
połączenia komplet SET-BAUDRATE / SET-DATASIZE / SET-PARITY / SET-STOPSIZE, a parametry są
w konfiguracji urządzenia. Przy `Predkosc = 0` nie wysyłamy nic — to tryb „z urządzenia”.

**Ustawienie parametrów RFC 2217 odbiera port kontrolerowi RC-1216H.** Pomiar czterema
wariantami (samo TCP, powitanie Telnetu, powitanie z parametrami, plus DTR/RTS) przy próbkowaniu
`ampdta.srv` co sekundę: dwa pierwsze warianty zero przełączeń, trzeci przełącza `Remoted` na
`Standby`, czwarty zostawia `Off/Unknown` na stałe. Wzmacniacz przez cały czas odpowiada na
`0x90`, więc to kontroler traci własny odczyt, nie sprzęt. Nie da się tego obejść — bez
parametrów urządzenie nie otwiera portu. Nie szukaj winy w mostku, gdy strona kontrolera miga.

**Zerowy licznik RX nie musi znaczyć, że coś jest nie tak z programem.** Przy SPE Expert
za RC-1216H okazało się, że wzmacniacz był wyłączony: kontroler na własnej stronie
(`http://<ip>/ampdta.srv?d=N1D<czas>`, pole 11) raportował `Off/Unknown`, a temperatury `--`.
Zanim zaczniesz szukać błędu w warstwie Telnetu, sprawdź ten endpoint — jest tylko do odczytu.
Poleceń `ampcmd?c=` nie ruszaj, one sterują wzmacniaczem.

**Sondy do RFC 2217 warto trzymać osobno.** RC-1216H przyjmuje **jednego klienta TCP** — przy
podłączonym programie druga sesja jest natychmiast zrywana. Sondy w `scratchpad` (`sonda` —
sama negocjacja, `most` — mostek na prawdziwych klasach z licznikami) wymagają rozłączenia
programu.

**com0com nie przenosi ustawień portu na drugą stronę pary.** Pomiar `GetCommState` na obu
stronach dał domyślne `1200 7-E-1` niezależnie od tego, co ustawił program po stronie aplikacji.
Nie da się więc odczytać parametrów z pary i podać ich dalej przez RFC 2217 — stąd jawne listy
w Ustawieniach. Kod, który to próbował robić, został usunięty z `PortIo.cs`; nie przywracaj go.

**Kolumny `DataGridViewComboBoxColumn` sypią wyjątkiem przy wartości spoza listy.** Prędkość
wpisana ręcznie w `rotory.json` (na przykład 7200) nie jest jedną z pozycji, więc
`WypelnijUrzadzenia` dokłada ją do `Items`, zanim doda wiersz.

**Okno Ustawień oglądaj przez `DrawToBitmap`, nie przez klikanie po ekranie.** Kontrolki z `Ui.cs`
są rysowane własnoręcznie i nie mają wzorca `Invoke` w UI Automation, a program startuje do
zasobnika, więc głównego okna zwykle nie ma na ekranie. Mały program pomocniczy z referencją do
`RotorPanel.exe` tworzy `SettingsForm`, pokazuje ją poza ekranem i zrzuca do pliku PNG — to
sprawdza układ bez ruszania pulpitu. `MainForm` tak się nie da obejrzeć, bo sama się ukrywa.

**Liczniki na karcie pokazują ruch klienta, nie nasz.** Odpytywanie o status i klawisze
wysyłane z okna klawiatury nie wchodzą do `Rx`/`Tx`, a z odbioru liczymy tylko to, co po
odfiltrowaniu ramek statusu idzie dalej. Inaczej RX rósłby od własnych zapytań przy TX równym
zeru i licznik kłamałby o ruchu programu sterującego.

**W WinForms etykieta dodana wcześniej zasłania późniejszą.** Licznik ruchu na karcie miał
ucięty początek (`,1 kB` zamiast `RX 1,1 kB`), bo etykieta protokołu, dodana przed nim, jest
wyżej w kolejności rysowania i zamalowywała mu lewą stronę. Nie chodziło o za małą szerokość —
naprawą było skrócenie etykiety protokołu do szerokości jej tekstu i wyliczenie pozycji
licznika z tego pomiaru (`EtykietaRuchu` przyjmuje tekst stojący obok).

**Wzmacniacz potrafi urwać ramkę statusu w połowie.** W nagraniu z Expert 1.3K-FA jedna
odpowiedź na siedem kończy się po 56 bajtach i od razu zaczyna się kolejna ramka.
`CzytnikSpe` wykrywa to po następnym nagłówku `AA AA AA` bliżej niż długość ramki i odrzuca
ogryzek zamiast puszczać go dalej. Test w `scratchpad/testspe` przepuszcza nagranie przez
czytnik kawałkami po 1, 5, 76 i 1130 bajtów — wynik ma być identyczny.

**PowerShell rozwija tablice zwracane z funkcji.** W generatorze ikony (`ikona/generuj-ikone.ps1`)
`return ,$dane` z przecinkiem chroni tablicę bajtów. Bez tego plik `.ico` wychodził uszkodzony,
mimo że wyglądał poprawnie.

**`System.Drawing` nie czyta wpisów PNG w ikonach.** Rozmiary do 64 px zapisujemy jako DIB,
PNG zostaje dla 128 i 256.

**Plik `.old` bywa zajęty tuż po starcie.** Poprzednia kopia kończy pracę już po
uruchomieniu nowej, więc sprzątanie z `Program.Main` trafia na zablokowany plik. Dlatego
`SprawdzAktualizacjeWTle` powtarza je po czterech sekundach.

**Paczki com0com bez przyrostka `-signed` są niepodpisane.** Decyduje przyrostek, nie numer
wersji — gałąź 2.2.2.0 ma zarówno warianty podpisane, jak i niepodpisane. W dokumentacji było
kiedyś napisane odwrotnie; nie przywracaj tego.

**Przy generowaniu plików uważaj na sekwencje ucieczki.** Literały regex i ścieżki `\.\`
łatwo tracą backslashe, gdy plik powstaje przez narzędzie pośredniczące. Po wygenerowaniu
sprawdź je w pliku, zanim uruchomisz kompilator — `\s` w zwykłym literale C# nie skompiluje
się w ogóle, ale `` skompiluje się jako znak backspace i po cichu zepsuje wyrażenie.

**Konfiguracji nie kopiujemy do katalogu wyjściowego.** `rotory.json` był w `.csproj` jako
`None Update` z `CopyToOutputDirectory`, przez co budowanie nadpisywało żywe ustawienia
użytkownika leżące obok pliku wykonywalnego, a pełna przebudowa (`--no-incremental`) najpierw
je kasowała jako plik z poprzedniej listy. Wpis jest usunięty — nie przywracaj go. Przykład
konfiguracji trzymamy w `rotory.przyklad.json`.

## Konwencje

- Komentarze i identyfikatory w kodzie **bez polskich znaków diakrytycznych**; napisy widoczne
  w interfejsie mogą je mieć, bo pliki źródłowe mają BOM UTF-8.
- Komentarz wyjaśnia **dlaczego**, nie co robi linijka.
- Kolory i czcionki wyłącznie z `Theme.cs`. Kontrolki własne w `Ui.cs`.
- `rotory.json` jest w `.gitignore` — w repo trzymamy `rotory.przyklad.json`.
- Numer wersji żyje w dwóch miejscach: `RotorPanel.csproj` i `app.manifest`. Podnoś oba naraz.
