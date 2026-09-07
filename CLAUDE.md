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

**Nie blokuj watku interfejsu na zadaniach mostka.** `SpeForm.FormClosing` czekal na
wyslanie RCU OFF przez `GetAwaiter().GetResult()`. Zapis czeka na semafor `_bramka`, a jego
kontynuacja wracala na watek interfejsu - czyli na ten sam watek, ktory wlasnie stal na
`GetResult`. Klasyczny zakleszczenie: okno nie dawalo sie zamknac, zasobnik przestawal
odpowiadac i zostawalo zabicie procesu z menedzera zadan. Objawilo sie dopiero wtedy, gdy
`TrybEkranu = false` zaczelo isc **przed** zapisem, bo to odblokowuje `OdpytujSpe`, ktore
potrafi zabrac semafor pierwsze. Poprawka jest dwuczesciowa i obie czesci sa potrzebne:
`Mostek.WyslijKlawisz` uzywa `ConfigureAwait(false)`, wiec jego kontynuacje nie potrzebuja
juz watku interfejsu, a `FormClosing` niczego nie czeka - RCU wylacza w tle i dopiero po
wyslaniu gasi `TrybEkranu`. Nie wracaj do blokowania; jesli kiedys naprawde trzeba poczekac
na mostek z poziomu okna, zrob to `async void` na zdarzeniu, nie synchronicznie.

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

**Wyświetlacz ma 240 na 64 piksele, czyli komórka to 6 na 8.** To jest klucz do
wszystkiego, co dotyczy rysowania. Przez długi czas uczyłem kafelków w 12 na 22 i skalowałem
je ułamkowo — stąd brały się wszystkie skargi na wygląd: kreski raz jedno-, raz dwupikselowe,
przerwy w ukośnych liniach logo, poziome belki liter o piksel za wysoko, rozsypany znak
stopnia. Uczymy się więc w rozdzielczości panelu, a `PodgladLcd.Skala` powiększa **całkowitą
krotnością** (teraz 2, czyli komórka 12 na 16 punktów i cały ekran 480 na 128). Nie wracaj do
skali ułamkowej i nie dobieraj rozmiaru komórki „na oko" — wynika on ze `Skala` razy 6 na 8.

**Siatkę zrzutu wyznacza zasięg tuszu, nie separatory.** Górna krawędź ramki i wiersz kresek
biegną przez całą szerokość, a separatory sięgają samego dołu, więc skrajne wiersze i kolumny
z tuszem dają pole wyświetlacza wprost. Dopasowanie do pionowych separatorów, które było tu
wcześniej, wychodziło o pół komórki obok, bo separator `0x8F` **nie stoi przy krawędzi
komórki, tylko w jej kolumnie 3**. Zmierzone dla zrzutu 385 na 107: pole od (7, 2), krok
9,4 na 12,5 px, czyli piksel panelu to 1,57 px zrzutu.

**Komórki próbkuj po odcieniach, nie po czerni.** Zrzut jest powiększeniem w skali ułamkowej,
więc krawędzie są wygładzone, a piksel panelu nie pokrywa się z pikselami zrzutu. Progowanie
najpierw na czarno-białe, a dopiero potem uśrednianie, dawało ten sam znak raz tak, raz
inaczej — zależnie od tego, w którym miejscu rastra wypadła komórka. Liczenie średniego
zaczernienia z wagami brzegów usuwa to bez reszty: **wszystkie 57 kodów graficznych i 35
sprawdzalnych liter wychodzą z każdego wystąpienia identycznie**. To jest dobry sprawdzian
po każdej zmianie w `narzedzia/ucz-kafelki.py`.

**Cały ekran idzie z map bitowych — tekst też.** `CzcionkaSpe` (generuje ją
`narzedzia/ucz-czcionke.py`) trzyma 96 znaków 6 na 8, `KafelkiSpe` znaki własne panelu.
`PodgladLcd` rysuje jedno i drugie tak samo. Wcześniej tekst szedł Consolas i obraz był
z dwóch światów: grafika pikselowa, litery wygładzone i rozstrzelone, bo żaden krok czcionki
nie pasuje do sześciopikselowej komórki. Czcionka systemowa została tylko do napisu
zastępczego.

**Czcionka jest w części zmierzona, w części dopełniona — i to jest zapisane w wyniku.**
Ze zrzutu da się wyciąć tylko te komórki, o których wiadomo, że pokazują to samo co zapisana
ramka: nazwa modelu, opisy pól paska stanu. Wyszło 35 znaków. Aż 32 zgodziły się co do
piksela z klasyczną czcionką 5x7 układów znakowych, a różniły się `D`, `t` i kropka — i te
wpisane są w tablicy w skrypcie. Taka zgodność wystarczy, żeby resztę wziąć stamtąd. Każde
uruchomienie skryptu wypisuje, które znaki się różnią; **zmierzone zawsze wygrywają**, więc
zrzut z nowymi znakami po prostu ich dołoży. Strzałki `0x99`-`0x9C` są rysowane ręcznie
(nie ma ich na ekranie głównym, nie ma czym zmierzyć) i tak oznaczone w `KafelkiSpe.cs`.

**Ramka ekranu glownego jest w danych, logo tez - ale jako kody kafelkow.** Odczytane z ulozenia bajtow: `0x9F` gora
ramki, `0xA0` dol, `0xA1` bok, `0xA2` i `0xA3` rogi, `0x8E` trojnik nad separatorem kolumny.
`0xB0`-`0xDF` to kafelki logo. Tablicy znakow szukalem w `Term_13k_232.exe` - zasoby to formularze
Delphi i bitmapy przyciskow, tablicy tam nie ma; proba uzycia Terma jako wzorca tez nie wyszla,
bo nie rysuje ramek, o ktore sam nie prosil. Zadziałalo dopiero uczenie ze zrzutu.

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

**Wlasne znaki wyswietlacza rozpoznane do tej pory:** `0x81`-`0x84` linijka miernikow
(lewa zaslepka, przedzialka zwykla, przedzialka co piata, prawa zaslepka), `0x8D` pozioma
kreska (wiersz 4 komorki), `0x8F` pionowa (kolumna 3), `0x8E` trojnik nad separatorem kolumny,
`0x99`-`0x9C` strzalki w podpowiedzi klawiszy - **para na jeden nawias**, `[` `0x99` `0x9A` `]`
to `[◁▲]`, a pozioma belka pod trojkatem siega jedna kolumne w lewa komorke, `0x9F`-`0xA3`
ramka i jej rogi, `0xAA` stopien przy temperaturze - na panelu to kwadracik 2 na 2 piksele,
nie kolko, dlatego znak `°` z czcionki wygladal obco. `0xB0`-`0xDF` to kafelki logo.

**Wypelnienie linijki to osobna rodzina kodow.** Widac to w ramce ekranu V PA (Operate,
potem DISPLAY): przy pustej linijce idzie `81 82 82 82 82 83 ...`, a przy wypelnionej do 33,7 V
`85 88 88 88 88 8B 88 88 88 88 8B 86 82 82 82 83 ...`. Czyli `0x85` wypelniona zaslepka,
`0x88` wypelniony odcinek, `0x8B` wypelniona podzialka, `0x86` komorka na koncu belki; dalej
wracaja kody puste. Pozycje sie zgadzaja: podzialki stoja w komorkach 12, 17 i 22, a wypelnione
sa dwie pierwsze.

**Cztery kafelki wypelnienia sa wyprowadzone, nie zmierzone - i tak sa oznaczone.** Zdjecia
z wypelniona linijka nie ma: w obu wersjach instrukcji wszystkie czternascie zdjec ekranu jest
z postoju. Trzy z nich wynikaja wprost z geometrii - puste kafelki rysuja pudelko z gorna
krawedzia w wierszu 3 i dolna w wierszu 6, wiec wypelnienie to ten sam kafelek z zamalowanymi
wierszami 4 i 5; tu nie ma pola do zgadywania. Czwarty, `0x86`, wyliczylem: podzialki maja
kreske w kolumnie 2 komorki i stoja w komorkach 7, 12, 17, 22, 27, wiec 0 V wypada w kolumnie
44 siatki, 60 V w kolumnie 164 - dwie kolumny na wolt. Odczyt 33,7 V daje koniec belki
w kolumnie 111,4, a komorka 18 zaczyna sie w 108, czyli zamalowane sa **cztery kolumny**,
z niepewnoscia jednej kolumny. Wszystko to siedzi w `WYPROWADZONE` w `ucz-kafelki.py`
z wyliczeniem; gdy trafi sie zdjecie z wypelniona linijka, `ucz-z-instrukcji.py` je nadpisze.
Reszty rodziny (`0x87`, `0x89`, `0x8A`, `0x8C` - inne stopnie wypelnienia i prawa zaslepka)
nie widzielismy w zadnej ramce; **nie dopisuj ich na wyczucie**.

**Kolejnosc uruchamiania narzedzi ma znaczenie.** `ucz-kafelki.py` pisze `KafelkiSpe.cs` od
zera, a `ucz-z-instrukcji.py` tylko dopisuje. Po kazdej zmianie w tym pierwszym trzeba wiec
przeliczyc wszystko po kolei:

```
ucz-kafelki.py zrzut.raw ekran.bin 385 107 1540
ucz-z-instrukcji.py operate.raw 734 211 2936 operate.bin 3.5  9.0  3.00  81 82 83 84
ucz-z-instrukcji.py setcat.raw  719 202 2876 setup.bin   0.75 5.80 3.03  99 9A 9B 9C
```

**Znaku `0xAE` nadal nie znamy.** Stoi w wierszu "FAN SPINNING: --:--" ekranu V PA, po dwie
komorki z kazdej strony dwukropka. Zadne zdjecie w instrukcji tego ekranu nie pokazuje, wiec
komorki zostaja puste.

**Do nauki znakow potrzebne sa dwie rzeczy naraz: obraz i ramka.** Ramka mowi, jakim kodem
wzmacniacz prosi o komorke, obraz mowi, jak ta komorka wyglada - jedno bez drugiego jest
bezuzyteczne. Ekran glowny mielismy w obu postaciach i stad `KafelkiSpe`. Ekrany Operate
i SET maja wlasne symbole (linijki miernikow `PA OUT` i `I PA`, strzalki w podpowiedziach),
ktorych na glownym nie ma, wiec wychodza puste. Dlatego okno sterowania zapisuje ramke na
**Ctrl+S** do podkatalogu `ekrany`, a `EkranSpe.Surowe` trzyma ja w calosci. Obrazy tych
ekranow sa w instrukcji 1.3K-FA (`manual/obraz-09.jpg` to Operate z pustymi linijkami).
Nie zgaduj tych map bitowych - bez pary obraz-ramka nie da sie zwiazac ksztaltu z kodem.

**Kursor to jeden bajt na kolumne, bit wskazuje wiersz.** 40 bajtow zaraz za siatka
(`EkranSpe.PoczatekFlag`), potem dwubajtowa suma kontrolna. Zmierzone przez porownanie ramek
przed i po nacisnieciu strzalki: 13 kolejnych bajtow zmienilo sie z `08` na `04`, czyli
podswietlenie przeskoczylo z wiersza 3 na 2 - i zgadza sie to z podpowiedzia u dolu ekranu,
ktora opisuje wybrana pozycje. Reguła jest wspolna dla wszystkich ekranow, wiec nie potrzeba
osobnych dekoderow per ekran, jak ma `macexpert-spe`.

**`0x8D` to kreska, nie tlo w negatywie.** Pierwsza wersja rysowala wiersz z tym znakiem jako
zielony pasek. Na zdjeciach w instrukcji widac, ze to zwykla pozioma kreska obok tytulu.
Zaznaczenie pozycji (negatyw na panelu) siedzi w flagach kursora za siatka.

**Ramka ekranu nie ma dlugosci - ramuj ja synchronizacja.** Poczatkowo zakladalismy stale
367 bajtow. Tak jest w praktyce na 1.3K-FA, ale `macexpert-spe` ramuje od `AA AA AA 6A` do
nastepnej synchronizacji, z limitem 512 bajtow, i to jest odporniejsze. `CzytnikSpe` robi tak
samo; `EkranSpe.Rozbierz` przyjmuje dlugosc, zamiast jej zakladac.

**Pokazujemy ekran wzmacniacza, nie skladamy wlasnego.** `macexpert-spe` rozbiera ramke na pola
i rysuje wlasny interfejs z szescioma dekoderami kursora per ekran. My renderujemy siatke znakow
w `PodgladLcd` - mniej kodu, a menu wyglada tak jak na panelu i dziala dla ekranow, ktorych nikt
nie rozbieral.

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

**Okna musza sie miescic na ekranie, na ktorym stoja.** Polozenia kontrolek sa wpisane na
sztywno, wiec Ustawienia maja 1000 na 852 punkty - na laptopie z ekranem 1366 na 768 dolny
pasek z przyciskiem *Zapisz* wychodzil pod krawedz pulpitu, a wysrodkowanie wypychalo jeszcze
pasek tytulu nad gorna krawedz. `Ui.DopasujDoEkranu` wlacza `AutoScroll`, ustawia
`AutoScrollMinSize` na naturalny rozmiar tresci, przycina okno do obszaru roboczego
i wciaga je z powrotem na ekran. Kazde okno wola to ze swoim naturalnym rozmiarem, a nie
z biezacym `ClientSize` - ten po wlaczeniu przewijania jest juz pomniejszony o paski.
`MainForm` robi to takze przy pokazaniu, bo startuje do zasobnika i przy budowaniu listy
nie wiadomo jeszcze, na ktorym monitorze sie pojawi. Nowe okno tez ma to wywolac, a nie
zakladac, ze uzytkownik ma duzy ekran.

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
wysyłane z okna sterowania nie wchodzą do `Rx`/`Tx`, a z odbioru liczymy tylko to, co po
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
