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

**Kontrolka, ktora niczego nie przyjmuje, nie moze brac fokusu.** `Control` jest domyslnie
zaznaczalny, wiec `PasekLed` i `Led` stawaly sie `ActiveControl` okna. Przewijane okno nie
pozwala wyprowadzic aktywnej kontrolki poza widok, wiec okno stanu przewijalo sie **tylko do
gornej krawedzi pierwszej linijki i ani piksela dalej** - ostatni wiersz tabeli (temperatura
sumatora) byl nieosiagalny. Zmierzone na osobnym stanowisku: przy tresci 666 px i oknie 430 px
przewijanie zatrzymywalo sie na 120 px, czyli dokladnie tam, gdzie pierwsza linijka dotyka gory;
po `SetStyle(ControlStyles.Selectable, false)` dochodzi do 256 px, czyli do konca. Ten sam goly
formularz z pustymi panelami w tej samej geometrii przewijal sie od poczatku poprawnie - roznica
byla wylacznie w kontrolkach. Kazda nowa kontrolka czysto pokazowa ma dostac ten sam styl
i `TabStop = false`.

**Wymiary okien licz z pomiaru napisow, nie ze stalych.** Czcionki podajemy w punktach, wiec
przy powiekszeniu ekranu 150% rosna o polowe, a wspolrzedne w pikselach nie - wiersz wysokosci
18 px obcinal wtedy tekst. `UkladStanu` liczy wszystko z `Font.Height` i `TextRenderer.MeasureText`,
przez co ten sam kod daje przy 100% wiersz 22 px, a przy 200% - 42 px. Sprawdzone dla powiekszen
1,0 / 1,25 / 1,5 / 2,0 na pieciu rozmiarach ekranu.

**Tabela stanu przechodzi na dwie kolumny, gdy nie miesci sie w wysokosci.** Czternascie wierszy
w jednej kolumnie to 699 px z rama - na ekranie GPD (1280x720) nie ma na to miejsca. `UkladStanu`
sklada wtedy tabele po siedem wierszy w dwoch kolumnach: 585x545, czyli miesci sie bez
przewijania. Jesli i to nie starczy szerokosci, zostaje jedna kolumna i przewijanie. Kolejnosc
prob jest wazna: **przewijanie to ostatnia deska ratunku, nie sposob na codzienna prace**.

**Menu zasobnika buduj przed `Show`, nie w zdarzeniu `Opening`.** Pozycje dodawane
w `Opening` przychodza za pozno: w chwili wywolania `Show` menu jest puste, wiec WinForms
go nie pokazuje - i trzeba kliknac prawym drugi raz. Zmierzone w osobnym programie:
pierwsze `Show` daje `Visible=False` i wysokosc 32 (sam margines), drugie `Visible=True`
i wysokosc 54; z pozycjami dodanymi wczesniej pierwsze `Show` pokazuje menu od razu.
Menu i tak trzeba przebudowac przy kazdym otwarciu, bo wypisuje biezacy stan mostkow.

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

**Stan i obraz to dwa niezalezne strumienie, ktore ustepuja sobie nawzajem.** Przy otwartym
podgladzie dziela jedno lacze, a wzmacniacz obsluguje jedno naraz - ale **nie wolno wiazac
jednego z drugim**. Reguly sa trzy:

* `OdpytujSpe` milczy zupelnie, gdy do pary wpiety jest klient, oraz gdy podglad jest otwarty,
  a nikt nie patrzy na okno stanu (`TrybStanu`) - wtedy stan widac na samym ekranie.
* Zapytanie o stan omija okno miedzy pulsem RCU a klatka (`CzekamNaKlatke`, 900 ms). Zmierzone:
  wciskajac sie tam opoznia klatke o ponad sekunde.
* Puls omija zapytanie o stan w locie (`CzekamNaStan`, 400 ms). **Zabezpieczenie musi byc
  obustronne.** Gdy bylo jednostronne, puls trafial w odpowiedz o stan i przepadal: pojedyncza
  klatka przychodzila po 559 ms, ale mediana odstepu miedzy klatkami wynosila 2,72 s.

**Nie doczepiaj zapytania o stan do odebranej klatki.** Probowalem tego, zeby "wyrownac takt".
Wyszlo odwrotnie - lancuch jest tak wolny jak jego najwolniejsze ogniwo, a wzmacniacz co jakis
czas milknie na kilka sekund. Zmierzone: odstepy miedzy klatkami od 1,25 s do 11 s, wiec odczyt
stanu starzal sie do siedmiu sekund i okno stanu meldowalo, ze wzmacniacz nie odpowiada. Po
rozlaczeniu strumieni stan trzyma mediane 1,0-1,6 s **takze wtedy, gdy obraz stoi**, bo idzie
osobnym kanalem.

**Nie pytaj o linie CTS/DSR na uchwycie portu.** Wykrywanie klienta robilo `GetCommModemStatus`
na tym samym synchronicznym uchwycie, po ktorym czyta pompa - zmierzone: 71 prob, srednio
387 ms, najdluzsza **2259 ms**, 24 ponad 200 ms. Blokowalo to sciezke danych i bylo widac jako
skoki opoznienia klatek. Do tego bylo bezuzyteczne: SPE Term tych linii nie podnosi. Zostaje
ruch od strony portu (za darmo) i proba otwarcia drugiej strony pary, ktora idzie przez osobny
uchwyt. Po usunieciu ani jedno badanie nie przekroczylo progu 5 ms.

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

**Mamy caly font ROM wyswietlacza - i to on jest zrodlem prawdy.** `CzcionkaSpe.cs` to
256 glifow po osiem linii, po jednym na kazdy kod z ramki `0x6A`: tekst i znaki wlasne panelu
w jednej tablicy, bo tak siedza w sterowniku. Generuje ja `narzedzia/wczytaj-rom.py`
z tablicy projektu AetherSDR - pochodzenie i licencja w [NOTICE](NOTICE). **Nie dopisuj do niej
niczego recznie** i nie zgaduj glifow; jesli czegos brakuje, brakuje tego w ROM-ie.

`KafelkiSpe.cs` juz nie istnieje - byl drugim zrodlem tych samych ksztaltow, wiec poszedl.
Poszedl tez `ucz-czcionke.py`: jego tablica klasycznej czcionki 5x7 sluzyla do zalepiania dziur
i wlasnie tam sie mylila.
Skrypty `ucz-kafelki.py` i `ucz-z-instrukcji.py` zostaja jako **niezalezny sprawdzian** ROM-u
i pisza do `obj/`, nie do zrodel. Warto ich uzyc, gdy pojawi sie podejrzenie, ze tablica nie
pasuje do konkretnego egzemplarza wzmacniacza.

**Odwzorowanie ze zdjec wyszlo prawie identycznie jak ROM - i to jest miara metody.** Zanim
tablica trafila do programu, zestawilismy ja z tym, co sami odtworzylismy: **67 z 68 kafelkow
graficznych i wszystkie 35 znakow zmierzonych ze zrzutu zgadzalo sie co do bitu**. Roznice byly
wylacznie tam, gdzie zgadywalismy - `0x86` wyliczony rachunkiem wyszedl o kolumne za szeroki
(0x3C zamiast 0x38, czyli dokladnie w podanej niepewnosci), a dwanascie znakow wpisanych
z klasycznej czcionki 5x7 (`! " , 7 Y g j p q | ~` i `0x5F`) bylo nietrafionych. Wniosek na
przyszlosc: **mierzenie ze zdjec dziala, zgadywanie nie** - i to nawet wtedy, gdy zgadywany
wzorzec wyglada rozsadnie.

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

**Cały ekran idzie z map bitowych — tekst też.** `PodgladLcd` rysuje każdą komórkę tak samo,
bez znaczenia, czy to litera, kreska czy kafelek logo. Wcześniej tekst szedł Consolas i obraz był
z dwóch światów: grafika pikselowa, litery wygładzone i rozstrzelone, bo żaden krok czcionki
nie pasuje do sześciopikselowej komórki. Czcionka systemowa została tylko do napisu
zastępczego.

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

**Linijka podswietlenia i kontrastu ma inna rodzine kodow niz mierniki.** Na ekranie ustawien
DISPLAY suwaki ida bajtami `0x92`-`0x98`, a nie `0x81`-`0x8B` jak mierniki PA - czyli wzmacniacz
ma co najmniej dwa zestawy kafelkow linijki. Rozpisane w `macexpert-spe` (`RCUFrame.swift`):
lewa zaslepka `0x92`-`0x97` to rosnace wypelnienie, prawa `0x94`-`0x98` rosnaca pustka, `0x93`
komorka pusta w srodku, a `0x96` to kursor edycji, ktory potrafi stanac w srodku paska. Oni
z tego wyliczaja tylko poziom 0-9 i nic nie rysuja - my mamy ksztalty z ROM-u i rysujemy je
jak kazdy inny znak.

**Sprawdzian ROM-u odtwarza sie tak.** Skrypty pisza do `obj/`, a `ucz-kafelki.py` zaczyna od
zera i `ucz-z-instrukcji.py` tylko dopisuje, wiec kolejnosc ma znaczenie:

```
ucz-kafelki.py zrzut.raw ekran.bin 385 107 1540
ucz-z-instrukcji.py operate.raw 734 211 2936 operate.bin 3.5  9.0  3.00  81 82 83 84
ucz-z-instrukcji.py setcat.raw  719 202 2876 setup.bin   0.75 5.80 3.03  99 9A 9B 9C
```

**`0xAE` to ptaszek z pol wyboru.** Stoi tez w wierszu "FAN SPINNING: --:--" ekranu V PA.
Znaczenie potwierdza `macexpert-spe` - w dekoderze stanu konfiguracji `filled(i) =
bytes[i] == 0xAE` czyta pola wyboru (BNK A/B, REMOTE ANT SWITCH, SO2R MATRIX, COMBINER) -
a ksztalt jest w ROM-ie i zgadza sie ze zdjeciem `[✓]` z `manual/obraz-14.jpg`.

**Do nauki znakow potrzebne sa dwie rzeczy naraz: obraz i ramka.** Ramka mowi, jakim kodem
wzmacniacz prosi o komorke, obraz mowi, jak ta komorka wyglada - jedno bez drugiego jest
bezuzyteczne. Dzis to juz tylko sprawdzian ROM-u, ale zasada zostaje na wypadek, gdyby ktorys
egzemplarz mial inny generator znakow. Dlatego okno sterowania zapisuje ramke na **Ctrl+S**
do podkatalogu `ekrany`, a `EkranSpe.Surowe` trzyma ja w calosci; obrazy ekranow sa
w instrukcji 1.3K-FA (`manual/obraz-09.jpg` to Operate z pustymi linijkami).

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

**U nich nie ma ani jednej mapy bitowej - i dlatego nie ma tam czego szukac.** Sprawdzone
w zrodlach: `LCDText.swift` zamienia kazdy znak wlasny wyswietlacza na kropke (z komentarzem,
ze chodzi o zachowanie wyrownania bajt-do-znaku przy wyszukiwaniu), wiec linijki, logo, ramki
i strzalki sa u nich kropkami. Liczby na wskaznikach biora nie z wyswietlacza, tylko z **ramki
statusu `0x43`** - `SPEProtocol.swift` czyta z CSV pola 10-14 (moc, SWR na ATU, SWR na antenie,
napiecie, prad drenu) i rysuje na tym wlasne kontrolki. Ekran sluzy im tylko do rozpoznania,
gdzie stoi wzmacniacz, i do menu. Jesli szukasz ksztaltow kafelkow, to nie tam - trzeba ich
uczyc ze zdjec, tak jak robimy.

**Ramki od wzmacniacza rozbieraj po kolei, nie po rodzaju.** Pierwsza wersja `CzytnikSpe`
szukała najpierw ramek ekranu w całym buforze i oddawała klientowi wszystko, co leżało przed
nimi — razem z ramkami statusu. Efekt: podgląd zamierał, a karta pokazywała „czekam na odczyt
stanu". Teraz pętla bierze **najwcześniejszy** nagłówek `AA AA AA`, patrzy na bajt rodzaju
(`0x43` status, `0x6A` ekran) i dopiero wtedy decyduje. Potwierdzenia i wszystko inne idą do
klienta.

**Ustaw 8N1 na swojej stronie pary com0com — inaczej ginie ósmy bit każdego bajtu.**
To była przyczyna tego, że **żaden** zewnętrzny program nie działał przez mostek: ani SPE Term,
ani AetherSDR. com0com nie dziedziczy ustawień z drugiej strony pary, a świeżo otwarty port ma
domyślne **1200 7-E-1**; przy siedmiu bitach danych sterownik obcina najstarszy bit. Zmierzone
w śladzie: AetherSDR wysyłał `0x90` (zapytanie o status), a do wzmacniacza szło `0x10` — czyli
**strzałka w prawo**, 1523 razy w trzy minuty. Term wysyłał `0x80` (włączenie podglądu ekranu),
a szło `0x00`. Dlatego oba programy tylko pingowały i poddawały się, a wzmacniacz przez ten czas
wędrował po menu.

Mylące było to, że **nasze własne odpytywanie działało poprawnie** — piszemy prosto do gniazda
TCP i przez parę w ogóle nie przechodzimy. Karta pokazywała stan, licznik UART kontrolera rósł
w obie strony, a klient nie dostawał nic. Jeśli kiedyś znów pojawi się taki obraz — u nas dobrze,
u klienta pusto — najpierw sprawdź, co naprawdę wychodzi z pary, a nie co powinno.

`PortIo.UstawOsiemBitow` woła `SetCommState` zaraz po otwarciu. To **co innego** niż odczytywanie
parametrów z pary, żeby podać je dalej przez RFC 2217 — tamtego nie da się zrobić i tamten kod
słusznie poszedł.

**Zapis na port nie może czekać na odczyt.** Domyślne `ReadAsync` i `WriteAsync` klasy `Stream`
przepuszczają obie operacje przez **jeden wspólny semafor na strumień**. Pompa odczytu prawie
zawsze siedzi w `ReadFile`, więc każdy zapis czekał, aż tamten skończy — przy limicie odczytu
200 ms dawało to medianę 92 ms i średnią 312 ms na sześciobajtową ramkę. `StrumienPortu` ma
własne `ReadAsync`/`WriteAsync` omijające ten semafor, a limit odczytu zszedł do 25 ms. To jest
też prawdziwa przyczyna zapisu zmierzonego kiedyś jako „2,77 s na sześć bajtów" — kolejka
`Oddaj`/`PisarzPortu` leczyła objaw.

**Nie oddawaj klientowi ramek w kawałkach.** `CzytnikSpe` zostawiał sobie zawsze dwa ostatnie
bajty „na wypadek przeciętego nagłówka", przez co sześciobajtowe potwierdzenie szło do klienta
jako 4 i 2 bajty, oddalone o kilkadziesiąt milisekund. Teraz zatrzymujemy **tylko końcowe bajty
`0xAA`**, bo tylko one mogą być początkiem nagłówka.

**Gdy do pary wpięty jest klient, milczymy i blokujemy sterowanie.** Wzmacniacz ma jednego pana
naraz. Klienta wykrywamy dwoma sposobami: świeżym ruchem od strony portu (do 5 s), a po
dziesięciu sekundach ciszy — próbą otwarcia drugiej strony pary. Ta druga jest jedyna, która
wykryje klienta trzymającego port otwarty i milczącego, i **to ona decyduje, kiedy sterowanie
wraca**. Próby nie robimy przy pracującym kliencie, żeby nie podebrać mu portu w chwili, gdy sam
go otwiera. Linii CTS/DSR **nie pytamy** — patrz wyżej: kosztowały do 2,3 s blokady ścieżki
danych, a SPE Term ich i tak nie podnosi.

**Ślad diagnostyczny mostka.** Pusty plik `slad.wlacz` obok programu włącza zapis do `slad.txt`:
bajty na każdym odcinku osobno, czas każdego zapisu na port i podgląd pierwszych bajtów ramki.
Bez niego cała ta sprawa byłaby nie do rozwiązania — trzy kolejne hipotezy (zjadanie ramek
statusu, linie DTR/RTS, opóźnienia) okazały się nietrafione albo niewystarczające, a rozstrzygnął
dopiero widok tego, co naprawdę wychodzi z pary.

**Zdejmuj ze strumienia tylko tyle ramek statusu, ile sam zamówiłeś.** `Mostek` przy
`Urzadzenie.Spe` wstrzykuje własne zapytanie `0x90` raz na sekundę, a `CzytnikSpe` wyjmuje
odpowiedzi, żeby nie trafiły do klienta, który o nie nie prosił. Pierwsza wersja zjadała
**każdą** ramkę statusu — i to był błąd, bo SPE Term oraz AetherSDR odpytują wzmacniacz
same. Efekt: żaden z nich nic nie rysował i nic nie odczytywał, choć mostek stał, port był
otwarty, a nasza własna karta pokazywała stan poprawnie. Teraz `ZglosWlasneZapytanie` liczy
niezaspokojone własne zapytania (najwyżej dwa, żeby przy wyłączonym wzmacniaczu licznik nie
urósł) i tylko tyle ramek znika; resztę rozbieramy dla siebie **i oddajemy dalej**.
Sprawdzian na nagraniu `rx-term.bin`: bez własnych zapytań do klienta idzie 7 z 8 ramek
(ósma to ta urwana w połowie, którą i tak odrzucamy), po dwóch własnych — 5.

**Gdy klient odpytuje sam, my przestajemy.** `CzytnikSpe.KlientPytaSam` jest prawdziwe przez
trzy sekundy od ramki statusu, o którą nie pytaliśmy; `OdpytujSpe` wtedy tylko śpi. Stan
czytamy z ramek klienta po drodze, a wzmacniacz ma o połowę mniej roboty — co jest istotne,
bo zmierzone wcześniej: nadmiar ruchu gubi mu odpowiedzi.

Do gniazda piszą dwie strony — pompa i odpytywanie — stąd semafor `_bramka`; bez niego ramki
potrafiłyby się przepleść w połowie.

**Ramki ekranu `0x6A` mają ten problem wciąż otwarty.** Zdejmujemy je zawsze, gdy nasze okno
sterowania jest otwarte (`PrzechwytujEkran`). Jeśli w tym samym czasie klient też poprosi
o tryb RCU, jego klatki zjemy. Przy zamkniętym oknie przechodzą bez zmian. Gdyby to zaczęło
przeszkadzać, trzeba policzyć własne pulsy tak samo jak zapytania o status.

**Stan wzmacniacza czytamy niezaleznie od trybu RCU.** `StatusForm` pokazuje wszystkie
dziewietnascie pol ramki `0x43` plus linijki miernikow, i **dziala takze wtedy, gdy do pary
wpiety jest klient** - wtedy nie odpytujemy sami, tylko rozbieramy ramki, o ktore poprosil on.
To wazne rozroznienie: sterowanie (RCU) blokujemy przy kliencie, ale podglad stanu nie ma
powodu znikac. Zakres linijki mocy bierze sie z pola identyfikacyjnego ramki
(`StatusSpe.MocMaksymalna`: "13K" to 1,3 kW), a skale pradu i napiecia - 0-50 A i 0-60 V -
odczytalem z podzialek na wlasnych ekranach panelu w ramkach `0x6A`, wiec nie sa zgadniete.

**Okna wzmacniacza sa niezalezne od panelu - i to jest celowe.** Sterowanie i stan otwieraja
sie przez `Show`, **bez wlasciciela**, z wlasnym przyciskiem w pasku zadan. Dzieki temu panel
mozna schowac do zasobnika i zostawic je na widoku - mostki pracuja dalej, wiec okna sie
odswiezaja. Wczesniej byly modalne (`ShowDialog(this)`): blokowaly panel i znikaly razem z nim.
Nie wracaj do `ShowDialog` dla tych dwoch; `SettingsForm` i `PairsForm` maja byc modalne, bo
zmieniaja konfiguracje pod reka.

`OknaMostka` pilnuje, zeby na jeden mostek przypadalo jedno okno danego rodzaju - drugie
klikniecie przywraca otwarte zamiast mnozyc kopie - i pozwala je pozamykac. Dwa miejsca musza
to wolac: `BudujListe` przed `m.Dispose()`, bo okna trzymaja mostek, ktory zaraz zniknie, oraz
`OdswiezSpe`, gdy do pary wepnie sie klient (wtedy tylko sterowanie, stan zostaje). Sprawdzone:
drugie `Pokaz` nie tworzy kopii, oba okna maja `Owner == null` i `ShowInTaskbar`, a kaskada
odsuwa drugie o 28 punktow.

**Karta wzmacniacza jest wyzsza niz pozostale.** `WysokoscKartySpe` to 112 zamiast 72, bo
miesci linijke mocy nadawania i trzeci przycisk. `BudujUrzadzenia` przesuwa sie o wysokosc
zalezna od `u.Spe` - jesli dolozysz cos do tej karty, popraw obie liczby naraz, inaczej karty
zaczna na siebie nachodzic. Ukladu tej karty **nie da sie sprawdzic zrzutem** (`MainForm` sama
sie ukrywa), wiec pozycje licz na piechote: przyciski stoja na y=8, 40 i 72, kazdy po 30 px.

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

## Wnioski z audytu (wrzesień 2026)

**Nie uruchamiaj niczego z podniesieniem UAC przez plik w `%TEMP%`.** Tak bylo do 1.9.4:
`Com0Com.WykonajLinie` pisala plik `.bat` do katalogu tymczasowego i uruchamiala go
z `Verb = "runas"`. `%TEMP%` jest zapisywalny dla uzytkownika, wiec dowolny proces na tym
koncie mogl podmienic plik w okienku miedzy zapisem a startem i dostac prawa administratora.
Teraz polecenia ida **jako argumenty** `cmd.exe /c` - CreateProcess dostaje je wprost i nie
da sie ich po drodze podmienic. Zapisu do pliku nie przywracaj.

**Nazwy portow filtruj biala lista, nie czarna.** `NewPairForm` przepuszczal wszystko poza
spacja, tabulatorem, przecinkiem i `=`. Nazwa `COM9&calc` trafiala do polecenia uruchamianego
jako administrator. Teraz przechodza wylacznie litery i cyfry; ta walidacja jest czescia
zabezpieczenia powyzej, nie kosmetyka.

**Parser JSON ma limit zagniezdzenia i musi go miec.** Bez niego wejscie `[[[[...` daje
`StackOverflowException`, ktorego **w .NET nie da sie przechwycic** - proces ginie bez slowa,
wiec nawet komunikat "Blad konfiguracji" w `Program.Main` by sie nie pojawil. Limit to 64
poziomy. Sprawdzone: 60 przechodzi, 70 i 20000 daja `FormatException`, proces zyje.

**Klawisze przelaczajace tor pytaja, gdy nie wiadomo, czy wzmacniacz stoi.** INPUT, ANT
i BAND± przy zalaczonym RF to gorace przelaczanie przekaznika. Odczyt stanu jest przy otwartym
podgladzie prawie zawsze nieswiezy (nie odpytujemy, bo to opoznia klatki), wiec drugim zrodlem
jest sam odzwierciedlony wyswietlacz: ekran glowny wypisuje "Standby" tylko wtedy, gdy
przekazniki sa w obejsciu. Brak tego slowa niczego nie dowodzi - wtedy pytamy. Nie zamieniaj
tego na ciche wyslanie tylko dlatego, ze pytanie bywa uciazliwe.

**Aktualizacje sprawdzamy po sumie SHA-256, nie po adresie.** Adres pobrania bierzemy
z JSON-a odpowiedzi API, wiec wymagamy `https` i hosta w domenie GitHuba - ale to tylko
pierwsze sito. Rozstrzyga suma: GitHub podaje ja przy zasobie wydania w polu `digest`
(zapis `sha256:<64 znaki hex>`), zapasowo szukamy 64-znakowego ciagu szesnastkowego
w opisie wydania. **Bez sumy nie aktualizujemy w ogole** - `Pobierz` rzuca wyjatkiem
i mowi, zeby pobrac wersje recznie. Sume liczymy dwa razy: raz na pobranych bajtach,
zanim cokolwiek trafi na dysk, i drugi raz w `Podmien`, juz z pliku - miedzy pobraniem
a podmiana plik lezy w `%TEMP%` i to jedyna chwila, w ktorej moglby sie zmienic. Nie
upraszczaj tego do jednego sprawdzenia i nie przepuszczaj wydania bez sumy: program
podmienia sam siebie tym plikiem, wiec TLS i nazwa hosta to za malo.

Wydajac nowa wersje **nie trzeba** nic dopisywac - GitHub liczy `digest` sam przy wgrywaniu
zasobu. Sprawdzone na zywym wydaniu v1.9.6: `digest` z API zgadza sie z `sha256sum` pliku,
a `Pobierz` z podmieniona suma nie zostawia niczego w katalogu tymczasowym.

**Nie badaj portu i nie niszcz mostkow na watku interfejsu.** `KlientNaPorcie` wykonywalo
`CreateFile` na porcie szeregowym wprost z odswiezania karty (co 700 ms), a `BudujListe`
niszczylo mostki sekwencyjnie, gdzie `Mostek.Stop` czeka do 2,5 s na kazdy - przy kilku
mostkach zapis ustawien zamrazal okno na kilkanascie sekund. Badanie idzie teraz do puli
watkow (`ZaplanujBadanieStrony`, pola czytane atomowo), a przebudowa listy uzywa
`RozlaczWszystko`, ktore rozlacza rownolegle.

**Slad ma limit rozmiaru.** Rosl bez ograniczenia (~9 MB na godzine) i otwieral plik przy
kazdej linii, w sciezce danych mostka. Teraz trzyma otwarty uchwyt, a po 10 MB przewija plik
na `.old`.

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
