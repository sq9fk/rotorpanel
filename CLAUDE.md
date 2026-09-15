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

**Korelacja bez kolejnosci to nie przyczyna.** Wniosek "sterownik odpowiada 208 na STOP"
opieral sie na tym, ze 208 wystepowalo **wylacznie** w przebiegach ze STOP-em - dwadziescia
prob, korelacja zupelna. A mimo to moze byc odwrotnie: program sterujacy wysyla STOP **bo**
zobaczyl 208. Pulapka zapisywala oba kierunki w osobnych buforach, wiec kolejnosci miedzy nimi
**nie bylo widac** - i to byl blad narzedzia, nie danych. `Pulapka.Dziennik` prowadzi teraz
jedna os czasu dla obu kierunkow i zrzuca ja przy **kazdym** nieprawdopodobnym skoku odczytu,
niezaleznie od tego, czy STOP w ogole wystapil.

**Odczyt 208 przychodzi po odpowiedzi, ktorej nie bylo - a nie po STOP-ie.** Rozstrzygnal
dziennik w jednej osi czasu:

```
00:37:10.735  PC ->  zapytanie        00:37:11.721  PC ->  zapytanie
00:37:10.931  <-  57 03 02 05 20      (brak odpowiedzi)
                  = 325               00:37:12.728  PC ->  zapytanie
                                      00:37:13.018  <-  57 02 00 08 20  = 208
```

Antena jechala rowno przez 325, jedna odpowiedz przepadla, a nastepna byla bzdurna.
**Zadnego STOP-a w tym przebiegu nie ma.** Wczesniejszy wniosek ("sterownik odpowiada 208 na
STOP") byl bledny: STOP takze powoduje pominiecie odpowiedzi, wiec byl **wspolnym skutkiem**,
nie przyczyna - a korelacja bez kolejnosci tego nie odroznia.

Objaw wychodzi **tylko przy dwoch pracujacych rotorach**, co wskazuje na zaklocenie od drugiego
silnika w torze sterownik - przejsciowka - Pi. Protokol SPID nie ma sumy kontrolnej, wiec
przekrecona ramka wyglada jak poprawny azymut.

**Prog odrzucania musi zalezec od czasu, a filtr nie moze sie zablokowac.** Wersja 1.11.3
miala sztywne 30 stopni i to bylo zle: gdy antena zostala przekrecona w czasie, gdy nikt nie
odpytywal, pierwszy odczyt po przerwie roznil sie o 60 stopni, wiec zostal odrzucony - a skoro
odrzuconej ramki nie zapamietujemy, nastepne tez, i filtr **zablokowal sie na dobre**. W sladzie
bylo to widac natychmiast: antena jechala rowno 302, 305, 310, 313, 316, 319, 321, 326, 329,
a filtr porownywal wszystko z pozycja 360 sprzed przerwy. Zrobilem gorzej, niz bylo.

Teraz prog rosnie z czasem (piec stopni na sekunde plus dziesiec tolerancji, a po minucie ciszy
nie odrzucamy nic), a po **trzech odrzuceniach z rzedu** przyjmujemy odczyt i synchronizujemy
sie od nowa - skoro sterownik uparcie mowi swoje, to zla pamiec mamy my. **Filtr, ktory potrafi
sie zablokowac, jest gorszy od braku filtra.**

`Mostek.OdrzucicNieprawdopodobnyOdczyt` odrzuca odczyt, ktory zmienia sie bardziej, niz rotor
zdazy sie obrocic - rotor robi okolo 2,5 stopnia na sekunde, wiec taki skok nie moze
byc prawda. Ramki nie zapamietujemy, zeby nastepny prawdziwy odczyt porownal sie z ostatnia
**wiarygodna** pozycja. Po kazdym zestawieniu lacza pamiec pozycji sie zeruje, bo antena mogla
zostac przekrecona recznie. **To jest proteza, nie naprawa** - i dlatego kazde odrzucenie idzie
do `podejrzane.txt` razem z dziennikiem.

**I dlatego tez da sie ja wylaczyc** - **osobno dla kazdego rotora** (1.11.9). W Ustawieniach
jest kolumna *Filtr* w tabeli rotorow, w pliku `rotory[].filtrPozycji`, domyslnie wlaczona.
Ustawienie siedzi przy rotorze, a nie przy programie, bo usterka, dla ktorej filtr powstal,
siedzi w **jednym** sterowniku (A3S) - wylaczanie go wszedzie naraz zdejmowaloby oslone takze
z masztow, na ktorych nic sie nie dzieje. W 1.11.8 byl jeden przelacznik globalny; stara
wartosc z korzenia pliku jest przy wczytaniu **domyslna dla kazdego rotora**, wiec
aktualizacja niczego nie zmienia pod reka.

Trzy rzeczy sa tu wazne:

* **wylacznik zdejmuje kasowanie danych, nie diagnostyke** - przy wylaczonym filtrze
  nieprawdopodobny odczyt idzie do klienta, ale nadal trafia do `podejrzane.txt` jako
  `PRZEPUSZCZONY (filtr wylaczony w Ustawieniach)`. Inaczej uzytkownik, ktory wylacza filtr
  **wlasnie po to, zeby zobaczyc surowa prawde**, straciłby jedyny slad usterki;
* **wylaczony filtr nadal zapamietuje pozycje** (`Zapamietaj`), inaczej po ponownym wlaczeniu
  porownanie szloby od pozycji sprzed godziny;
* **przy urzadzeniu innym niz rotor filtr milczy z zasady** (`Punkt is Rotor`). Wczesniej
  dzialal na kazdym mostku - przy zwyklym urzadzeniu szeregowym piec bajtow zaczynajacych sie
  od `0x57` to przypadek, a nie azymut.

Zapis ustawien i tak przebudowuje liste i mostki, wiec zmiana dziala od zaraz;
`OdrzucicNieprawdopodobnyOdczyt` czyta `rotor.FiltrPozycji` przy kazdej ramce.

**Brak odpowiedzi jest wskaznikiem wyprzedzajacym zlego odczytu** (1.11.10). Dziennik
z 14 wrzesnia 2026, 12:11, mostek A3S, lacze stojace od 46 minut, **nic sie nie krecilo**,
w buforze "do sterownika" same zapytania `1F` - ani jednej nastawy:

```
12:11:53.662  PC ->  zapytanie o pozycje        <- bez odpowiedzi
12:11:54.658  PC ->  zapytanie o pozycje
12:11:54.663     <- sterownik  57               <- 5 ms po zapytaniu: to odpowiedz na poprzednie
12:11:54.756     <- sterownik  03 06 00 20
12:11:55.657  PC ->  zapytanie o pozycje
12:11:55.863     <- sterownik  57
12:11:55.963     <- sterownik  02 00 08 20      <- 208
```

Trzy wnioski, kazdy zmienia obraz sprawy:

* **To nie byla pocieta ramka ani pusty bufor.** Do klienta poszla cala, poprawna ramka
  `57 02 00 08 20`, przyslana przez sterownik. Stara przyczyna 208 (`0x00 - '0' = 0xD0`)
  tu nie zachodzi - skladacz zrobil swoje.
* **`57 02 00 08 20` to prawdopodobnie firmowa odpowiedz "nie mam waznej pozycji", a nie
  azymut.** To ta sama ramka co do bajtu, ktora wczesniej zapisalismy jako odpowiedz na STOP.
  Obie obserwacje - po STOP i po zgubionej wymianie - skladaja sie w jedno wyjasnienie.
* **Warunek "tylko gdy krece dwoma naraz" upadl.** Rotory staly. Zakloceniami od drugiego
  silnika nie da sie tego wytlumaczyc.

Widac tez, ze samo lacze jest ciasne: **kazda** odpowiedz przychodzi rozbita na `57` + 90-100 ms
ciszy + `03 06 00 20`, a pelny obrot zapytanie-odpowiedz trwa 250-350 ms przy 1200 bodach,
gdzie same dane to 42 ms.

**Mostek SPE migocze - zestawia i traci lacze co dwie sekundy** (1.11.21). Wydobyte przez
wlasna zmiane: do 1.11.18 pulapka byla przy SPE **wylaczona calkowicie**, wiec nie bylo o tym
zadnej informacji. Po jej wlaczeniu plik z 15 wrzesnia pokazuje:

```
mostek        zestawien lacza w 90 minut
SPE Expert    227          (mediana odstepu: 2 s)
RAU 10m         3
RAK 15m         3
RAU A3S         3
```

Rotory stoja stabilnie, wzmacniacz zestawia lacze i traci je w rytmie dwoch sekund. Problem
istnial pewnie od dawna i **byl niewidoczny**, bo objawia sie tylko jako wzmacniacz, ktory
"czasem nie pokazuje stanu".

Sama linia "pulapka uzbrojona" mowi, ze lacze wstalo, ale nie mowi, **dlaczego padlo
poprzednie** - a to jest jedyne pytanie, ktore ma tu sens. Stad `ZglosZerwanie`: wpis
`LACZE PADLO po X s` z powodem (blad albo czyste zamkniecie przez druga strone), licznikiem
czystych przejec portu i bilansem wymian. Czyste zamkniecie w takim rytmie to **podpis walki
o port**: ktos inny laczy sie do 13100, a serwer oddaje mu polaczenie.

Oba wpisy sa dlawione do jednego na trzydziesci sekund na mostek - inaczej przy migotaniu co
dwie sekundy plik zamienia sie w dziennik, a ma byc dowodem rzeczowym.

**Przeniesienie katalogu roboczego poza OneDrive zadzialalo - zmierzone.** Korelacja brakow
odpowiedzi z zastojem wlasnego procesu spadla z **27 na 38** do **4 na 47**, a najdluzszy
zastoj z 525 ms do 357 ms. To potwierdza rozpoznanie z 1.11.19: to pulapka piszaca
synchronicznie do katalogu synchronizowanego przez OneDrive wspolwytwarzala braki.

**Zatrzymanie mostka przy pracujacym kliencie potrafilo dac 208 - dwa osobne bledy** (1.11.20).
Zgloszone przez uzytkownika: *"jak pstrotator jest wlaczony a wylacze w RotorPanel polaczenie
z ser2net, to przeskakuje mi czasem na 208 pomimo filtru"*.

* **`PisarzPortu` zapisywal na port z zetonem anulowania.** Zapis na pare com0com potrafi stanac
  na sekundy (zmierzone: 203 ms, 589 ms, 2,77 s), wiec anulowanie w jego trakcie zostawialo na
  porcie **pol ramki**. Program sterujacy, ktory dostanie `57 03`, doczyta reszte z wlasnego
  pustego bufora - i mamy 208. Teraz ramka dopisuje sie do konca (`CancellationToken.None`);
  ma piec albo trzynascie bajtow, wiec nie ma zadnego pozytku z przerwania jej w polowie.
* **Skladacze nigdy nie byly czyszczone miedzy polaczeniami.** Zyja tak dlugo jak mostek, a lacze
  moze paść w srodku ramki - wtedy w buforze zostawal np. `57 03 06` i czekal na **nastepne**
  polaczenie, gdzie sklejal sie z jego pierwszymi bajtami w ramke, ktorej nikt nie wyslal.
  `SkladaczSpid.Wyczysc` jest teraz wolane przy kazdym zestawieniu lacza, a wyrzucony ogon idzie
  do `podejrzane.txt`.

**Czego to nadal nie zalatwia i nie zalatwi:** gdy mostek jest zatrzymany, a klient dalej pyta,
**zadna ramka nie przechodzi przez mostek** - klient czyta wlasny pusty bufor i sam sobie robi
208. Filtr pozycji nie ma tam czego filtrowac, bo nie ma danych. To jest poza programem i tak
zostanie; jedyna obrona to nie zostawiac klienta odpytujacego martwa pare.

**Pulapka wspolwytwarzala usterke, ktorej szukala** (1.11.19). Dziewiec godzin pracy, 33 255
wymian na kazdym z trzech rotorow, braki 13 / 8 / 17 czyli **0,02-0,05 %** - i czujnik zastoju
dal odpowiedz na pytanie, ktorego nie dalo sie rozstrzygnac z wnetrza mostka:

```
rodzaj wpisu            zastoj procesu w ciagu 1,5 s   ile
BRAK ODPOWIEDZI         TAK                             27
BRAK ODPOWIEDZI         nie                             11
ZATOR                   nie                             11
ZATOR                   TAK                              1
```

**Dwie rozne przyczyny, wyraznie rozdzielone.** Zatory (odpowiedz po 600-700 ms) to w wiekszosci
tunel - proces wtedy chodzil. Braki odpowiedzi to w 27 przypadkach na 38 **nasz wlasny zastoj**.

A zrodlo tego zastoju siedzialo w pulapce: `Zapisz` otwieral plik, dopisywal kilka kilobajtow
i zamykal, **synchronicznie, w watku pompy**, ktora w tym czasie nie czytala gniazda. Plik lezy
przy pliku wykonywalnym, a ten u uzytkownika stoi w katalogu **synchronizowanym przez OneDrive**,
wiec kazdy dopis budzi synchronizacje. Kazdy wpis podnosil wiec szanse na nastepny.

Teraz tresc skladana jest na miejscu - dziennik i bufory musza byc sfotografowane w chwili
zdarzenia - a do tla idzie wylacznie gotowy tekst, wlasnym watkiem (nie pula, bo to wlasnie
jej zaglodzenie eliminujemy).

**Zasada ogolniejsza: narzedzie diagnostyczne na drodze danych musi byc bezkosztowe albo
asynchroniczne.** Inaczej mierzy wlasny wplyw i prowadzi sledztwo w kolko. Ten sam blad
popelnilem tu dwa razy - raz w pomiarze czasow (kolejka FIFO, ktora rozjezdzala sie po pierwszej
zgubie), raz w zapisie. **Zawsze pytaj, czy przyrzad nie zmienia tego, co mierzy.**

Osobno warto powiedziec uzytkownikowi: **katalog roboczy programu nie powinien lezec w OneDrive**.

**Co z tej diagnostyki przenosi sie na wzmacniacz SPE, a co nie** (1.11.18). Wzmacniacz idzie
**tym samym tunelem** (192.168.6.7, ta sama podsiec co Pi), wiec podlega tym samym zastojom.
Przeglad, pozycja po pozycji:

* **Skladanie ramek - juz tam jest i jest lepsze niz przy rotorze.** `CzytnikSpe.Przepusc`
  akumuluje w `_reszta` miedzy odczytami, czeka na cala ramke (`czekam`) i radzi sobie
  z naglowkiem przecietym miedzy porcjami. Co wiecej, ma **resynchronizacje po bajcie `0xAA`**
  (`NastepnySync`), czyli rozwiazanie lepsze od zaworu czasowego: urwana ramka nie czeka na
  zegar, tylko od razu ustepuje nastepnej. Nic tu nie trzeba dokladac.
* **Odczyt 208 sie nie przenosi.** To byla arytmetyka PstRotatora na wlasnym pustym buforze
  przy protokole bez sumy kontrolnej i z surowymi cyframi binarnymi. Ramka SPE ma naglowek
  synchronizacji, typ i dlugosc, a `StatusSpe.Rozbierz` zwraca `null` na smieciach. Ten
  protokol jest z budowy odporny na to, na co SPID nie byl.
* **Filtr pozycji ma tam milczec i milczy** - `Punkt is Rotor`. Pieciobajtowy ciag zaczynajacy
  sie od `0x57` w strumieniu wzmacniacza to przypadek, nie azymut.
* **Czego brakowalo: pomiaru.** Liczniki wymian byly liczone **tylko dla rotorow**, wiec
  o wzmacniaczu nie wiedzielismy nic - ani ile wlasnych zapytan o status zostaje bez
  odpowiedzi, ani jak dlugo trwa wymiana. Teraz `CzytnikSpe.IleStatusow` liczy rozebrane ramki,
  a mostek zestawia to z liczba wlasnych zapytan, dokladnie tak jak przy rotorze. Prog
  porzucenia jest inny - **2000 ms zamiast 700 ms** - bo wzmacniacz odpytujemy rzadziej
  i odpowiada wolniej, a prog musi zostac **krotszy od odstepu odpytywania**.
* **Pulapka uzbraja sie teraz takze przy wzmacniaczu.** Wczesniej `if (!OdpytywacSpe)`
  wylaczalo ja calkowicie, wiec `podejrzane.txt` nie mial o tym torze ani jednej linii.

**Tor idzie przez WireGuard po LTE - i to jest odpowiedz na wszystko powyzej** (1.11.17).
Potwierdzone przez uzytkownika, zgodne z pomiarem: ping 38-56 ms przy rozrzucie 18 ms
i **TTL 62**, czyli dwa przeskoki routowane - to nigdy nie byla siec lokalna.

Trzy polaczenia TCP do trzech sterownikow ida **jednym tunelem, jednym nosnikiem radiowym**.
Gdy LTE przeplanuje transmisje, wszystko w tunelu staje razem i rusza razem - stad zatory
identyczne co do milisekundy na dwoch mostkach naraz. **Tego nie da sie naprawic programem,
mozna tylko byc na to odpornym**, i wlasnie po to sa skladacz ramek, filtr pozycji i pulapka.

Dwie nastawy dobrane pierwotnie pod siec lokalna wymagaly korekty:

* **`SkladaczSpid.Cierpliwosc` ze 120 ms na 800 ms.** Odstep miedzy bajtami odpowiedzi to
  16-26 ms, ale tunel potrafi stanac na 615 ms. Zastoj w **srodku** ramki wypchnalby jej
  poczatek osobna porcja i program sterujacy zobaczylby znowu 208 - czyli dokladnie to, czemu
  ten skladacz mial zapobiec. Gorna granica bierze sie z rytmu odpytywania: PstRotator pyta co
  sekunde, wiec ogon oddany po 800 ms wychodzi **przed** nastepna odpowiedzia i nie ma sie
  z czym skleic.
* **Urwany poczatek odpowiedzi jest teraz kasowany, nie oddawany**
  (`SkladaczSpid.KasujUrwaneOdpowiedzi`). Piec bajtow `57 H1 H2 H3 20` niesie jedna liczbe,
  a **polowa liczby nie jest liczba**: program sterujacy, ktory dostanie `57 03 06`, doczyta
  reszte z wlasnego pustego bufora i pokaze 208 stopni. Lepiej, zeby nie dostal nic - zapyta
  znowu za sekunde. Kazde takie skasowanie idzie do `podejrzane.txt`, bo dane o polozeniu
  anteny nie moga znikac po cichu.

**W druga strone tego nie wlaczamy.** Zgubiony rozkaz to nie jest brak odczytu, tylko
niewykonana nastawa - urwany rozkaz idzie do sterownika taki, jaki jest.

**Zatory zdarzaja sie kilku mostkom w tej samej milisekundzie - wiec nie robia ich sterowniki**
(1.11.16). Dwie godziny spokojnego odpytywania, bez nastaw i bez STOP-u, 7100 wymian na kazdym
z trzech rotorow:

```
                braki   udzial    czasy min/sr/max
RAU A3S             2   0,03 %    281 / 305 / 615 ms
RAK 15m            13   0,18 %    270 / 321 / 615 ms
RAU 10m            13   0,18 %    257 / 304 / 592 ms
```

Zadnego 208, zadnego odrzuconego odczytu, zero odpowiedzi po terminie. Ale wpisy ukladaja sie
parami, co do milisekundy:

```
20:47:29.383  A3S   ZATOR: odpowiedz po 615 ms
20:47:29.384  15m   ZATOR: odpowiedz po 615 ms
20:32:27.423  10m   BRAK ODPOWIEDZI, czekalo 987 ms
20:32:27.424  15m   BRAK ODPOWIEDZI, czekalo 987 ms
21:23:35.734  10m   BRAK ODPOWIEDZI, czekalo 994 ms
21:23:35.735  A3S   BRAK ODPOWIEDZI, czekalo 994 ms
```

**Dwa niezalezne sterowniki, dwa osobne porty USB, dwa osobne polaczenia TCP - i identyczne
opoznienie w tej samej milisekundzie.** To nie jest przypadek do obronienia. Zrodlo jest
wspolne i lezy nad sterownikami.

Slad pokazuje tez, **jak** te bajty przychodza przy zatorze. Normalnie piec bajtow odpowiedzi
to piec osobnych odczytow co 20 ms (ser2net wysyla kazdy osobnym segmentem TCP - potwierdzone
zrzutem pakietow). Przy zatorze:

```
20:47:28.767  PC ->  zapytanie o pozycje
20:47:29.382     <- sterownik  57 03 06 00     <- cztery bajty jednym odczytem
20:47:29.382     <- sterownik  20              <- piaty w tej samej milisekundzie
```

Bajty spietrzyly sie i zostaly wypuszczone jednym strzalem. Pasuja do tego dwa wyjasnienia
i **z wnetrza mostka sa nie do odroznienia**: albo droga do Pi stanela, albo stanal nasz proces
i nikt przez chwile nie czytal gniazd. Dlatego powstal `CzujnikZastoju` - zegar tykajacy co
100 ms, ktorego **spoznione tykniecie** jest dowodem, ze stanelismy my. Jego odczyt idzie do
kazdego wpisu w `podejrzane.txt`, wiec korelacja jest natychmiastowa: jesli obok zatoru 615 ms
stoi "ostatni zastoj procesu: 400 ms, 0,2 s temu", wina jest nasza i nie ma po co szukac jej
w sieci. Jesli czujnik milczy, zostaje siec i Pi - i wtedy rozstrzyga `tcpdump`: **ACK-i
z komputera** pokazuja, czy bajty doszly na czas, a tylko program je pozno odczytal.

**Sterownik w trybie recznym nie odpowiada na port - i to tlumaczy cisze w dzienniku.**
Zgloszone przez uzytkownika: *"nie bede w stanie krecic i odczytywac azymutu w tym samym czasie
z PstRotatora, bo sterownik jest w trybie recznym"*. W dzienniku wyglada to na powazna awarie -
o 19:25:52 mostek A3S przestal dostawac cokolwiek, zapytania szly dalej co sekunde (2176, 2181,
2186), a licznik odpowiedzi stal na 2173. Przyczyna byla taka, ze uzytkownik krecil rotorem
recznie z panelu.

**Wniosek na przyszlosc: zanim uznasz cisze sterownika za usterke, sprawdz, w jakim trybie stoi
panel.** To dotyczy takze planowania testow - proba "krec recznie i czytaj sonda" jest
niewykonalna z zalozenia, a ja ja zaproponowalem, nie wiedzac o tym ograniczeniu. Test na
gubienie impulsow przez A3S musi isc **z trybu automatycznego, nastawa z PstRotatora**.

**Dlawienie gestych zapytan usuniete** (1.11.15). Wprowadzone w 1.11.13 na podstawie **dwoch**
przypadkow ze zrzutu pakietow. Pieciogodzinny przebieg go nie potwierdzil: na rotorze 10m
dlawienie zadzialalo trzy razy, a brakow odpowiedzi bylo i tak piec; na 15m brakow bylo szesc
przy zerowym dlawieniu. Skoro nie poprawilo bilansu, poszlo won - zgodnie z tym, co bylo
zapisane przy jego wprowadzaniu.

**Prog porzucania zapytania musi byc krotszy od odstepu odpytywania.** Inaczej pomiar czasow
**zamiera po pierwszej zgubie**: przy progu 1500 ms i odpytywaniu co sekunde osierocone
zapytanie nigdy sie nie przeterminowuje, bo gdy przychodzi nastepne, ma dopiero 1000 ms.
Kolejna odpowiedz przypisuje sie wiec do osieroconego, zostawiajac osierocone nastepne - i tak
w kolko, a zadna para nie jest juz jednoznaczna. Widac to bylo wprost: czasy zamrozone na
"z 106" przez pol godziny przy dwoch tysiacach wymian. Teraz prog to 700 ms - powyzej
zmierzonego maksimum odpowiedzi (442 ms), ponizej odstepu odpytywania - i sprzatanie idzie
takze **przed** dopasowaniem odpowiedzi, wiec pomiar sam wraca do zdrowia.

**Zrzut pakietow na Pi zamknal sprawe lacza** (1.11.13). Dziesiec minut ruchu na porcie 4101,
`tcpdump` na Pi, przy pracujacych wszystkich trzech rotorach:

```
zapytan                        600
odpowiedzi pelnych (5 bajtow)  597
odpowiedzi pustych (0 bajtow)    3
odpowiedzi niepelnych            0
retransmisji TCP                 0
czas do skompletowania ramki   220 / 249 / 252 ms   (min / mediana / max)
odstep miedzy bajtami ramki     16 /  23 /  26 ms
```

Co z tego wynika, po kolei:

* **Siec i ser2net sa czyste.** Zero retransmisji, odpowiedz opuszcza Pi w 249 ms - dokladnie
  tyle, ile zmierzyla sonda wpieta w port szeregowy. Ser2net nie dokłada nic.
* **`nodelay` dziala, ale byl nieistotny.** Ser2net wysyla **kazdy bajt osobnym segmentem TCP**
  (piec segmentow `length 1` na odpowiedz), nie czekajac na ACK. Hipoteza o Nagle byla chybiona.
* **Skladacz ramek jest niezbedny i ma dobry zawor.** Odstep miedzy bajtami to 16-26 ms,
  **ani razu** nie przekroczyl 120 ms - `SkladaczSpid.Cierpliwosc` nigdy nie wypuszcza ogona
  w srodku ramki. To domyka przyczyne odczytu 208 z 1.11.0.
* **PstRotator nie zalewa sterownika.** Odstep miedzy zapytaniami 453-1046 ms, mediana 1000,
  zadnej paczki zapytan po zatorze. Hipoteza o spietrzeniu zapytan - obalona.
* **Sterownik ignoruje czesc zapytan calkowicie** - zero bajtow, nie kawalek ramki. I to jest
  jedyna nieprawidlowosc, jaka w tym zrzucie zostala.

Rozklad tych trzech milczen jest wymowny:

```
odstep od poprzedniego zapytania    ile    bez odpowiedzi
ponizej 800 ms                        2          2  (100%)
950 ms i wiecej                     596          1  (0,2%)
```

Stad `Mostek.ZadlawicZapytanie`: mostek pilnuje, zeby dwa zapytania **o pozycje** nie poszly
do sterownika gescej niz co 800 ms. Nastawa i STOP ida zawsze i natychmiast. Pominiete
zapytanie nic nie kosztuje - PstRotator pyta znowu za sekunde, a sterownik i tak by nie
odpowiedzial. **Podstawa dowodowa to dwa przypadki**, wiec licznik `ZdlawioneZapytania` stoi
obok bilansu zapytan i odpowiedzi: jesli dlawienie nie poprawi tego bilansu, jest to jedna
linijka do usuniecia.

**Pomiar czasu wymiany przez kolejke FIFO rozjezdza sie po pierwszej zgubie - i o tym trzeba
pamietac.** Pierwsza wersja (1.11.11) trzymala osierocone zapytanie w kolejce i przypisywala
mu **nastepna** odpowiedz, a potem kolejnym zapytaniom kolejne odpowiedzi - z przesunieciem
o caly cykl odpytywania. W podpowiedzi wychodzily wtedy srednie po **4247 ms** i "brak
odpowiedzi 1531 z 2260", podczas gdy zrzut pakietow z tego samego toru pokazywal **trzy**
milczenia na szescset. Zdradzil to max 7416 ms - rowno osiem cykli, czyli tyle, ile wynosil
limit kolejki.

**Wymiana w locie to nie brak odpowiedzi** (1.11.14). Bilans odejmuje zapytania, na ktore
odpowiedz ma dopiero prawo nadejsc. Bez tego odjecia przez okolo trzy dziesiate kazdej sekundy -
bo tyle trwa wymiana - kazdy rotor pokazywal "brak 1", co wygladalo na usterke pojawiajaca sie
losowo na wszystkich trzech naraz, a bylo zwykla chwila miedzy zapytaniem a odpowiedzia.
Odwrotny przypadek (odpowiedzi wiecej niz zapytan) tez ma wlasny opis - "nadmiarowych" - bo
oznacza odpowiedz na rozkaz, ktorego nie liczymy jako zapytania: Rot1Prog odpowiada na STOP
ramka w formacie pozycji i taka odpowiedz nie ma swojego `1F`.

Poprawka ma dwie czesci: zapytanie starsze niz poltorej sekundy jest **porzucane** (kolejka
sie resynchronizuje zamiast dryfowac), a czas mierzymy **wylacznie z par bez watpliwosci** -
jedno zapytanie w locie, jedna odpowiedz. Nad tym wszystkim stoi `BilansWymian`: gole liczniki
wyslanych zapytan i otrzymanych odpowiedzi, ktore **nie wymagaja dopasowywania niczego do
niczego** i dlatego nie klamia nawet wtedy, gdy dopasowanie zawiedzie. Gdy pomiar pochodny
kloci sie z licznikiem, to licznik ma racje.

**Sterownik odpowiada jak metronom - zmierzone.** Sonda wpieta **wprost w `/dev/antA3S`
na Pi**, z pominieciem ser2neta, sieci i calej strony windowsowej, dala przy 3479 wymianach:

```
min 243 ms   srednio 245 ms   max 247 ms      zero brakow, zero zlych ramek
```

Rozrzut **czterech milisekund na trzy i pol tysiaca wymian**. Z tego wynikaja dwie rzeczy,
obie mocne:

* **245 ms to wlasny czas obrotu sterownika**, a nie narzut ser2neta. Nie ma sensu szukac
  winnego opieszalosci lacza gdzie indziej - tyle po prostu trwa odpowiedz Rot1Proga.
* **Sterownik nie gubi odpowiedzi i nie ma zatorow.** Wszystko, co mostek widzi ponad te
  245 ms - a widzi pierwszy bajt po 246 ms i **cala ramke dopiero po 339 ms** - powstaje
  **nad portem szeregowym** Pi. Przy 1200 bodach piec bajtow idzie 42 ms, wiec te 93 ms ciszy
  w srodku ramki nie ma prawa pochodzic od sterownika.

Pierwszy podejrzany o te 93 ms to **Nagle po stronie ser2neta w parze z opoznionym ACK
Windowsa**: ser2net oddaje pierwszy bajt od razu, kolejne cztery czekaja na potwierdzenie,
a to przychodzi dopiero z zegara opoznionego ACK. Objaw pasuje idealnie, bo jest
**powtarzalny co do ramki**, a nie losowy. Rozstrzyga to `tcpdump` na Pi: jesli odpowiedz
wychodzi jako dwa segmenty (1 B, potem 4 B) rozdzielone ACK-iem z PC, to jest to.

Nie potwierdzone, dopoki nie bedzie zrzutu - ale to jest nastepne miejsce do sprawdzenia,
a nie maszt.

Stad `Mostek.ZanotujZapytanie`/`ZanotujOdpowiedz`: gdy wychodzi **nastepne** zapytanie o pozycje,
a poprzednie wciaz czeka dluzej niz 500 ms, liczymy zgubiona odpowiedz i piszemy do
`podejrzane.txt`. Nie mierzymy zadnego wlasnego limitu czasu, tylko korzystamy z rytmu klienta -
to jedyny prog, ktorego nie trzeba zgadywac. Wpis ma **wlasny dlawik** (`_ostatniZrzutBraku`),
osobny od filtru pozycji: dziura i wywolany przez nia zly odczyt dziela sie zwykle sekunda,
a wspolny limit pieciosekundowy zjadlby ten drugi wpis - czyli ten, po ktory sie tu przychodzi.
**Zgubione i spoznione to nie to samo i nie wolno ich mylic** (1.11.11). Zgloszone zapytanie
**zostaje w kolejce**; gdy odpowiedz przyjdzie pozniej, piszemy `ODPOWIEDZ SPOZNIONA: przyszla
po X ms - nic sie nie zgubilo, to byl zator`. Odpowiedzi dopasowujemy do **najstarszego**
czekajacego zapytania, bo przy zatorze odpowiedz potrafi przyjsc juz po wyslaniu nastepnego
i liczenie od tego nowego dawaloby absurdalne "5 ms" zamiast prawdziwej sekundy. Te dwie
diagnozy prowadza w zupelnie inne miejsca: zguba do warstwy, ktora gubi dane, zator do tej,
ktora je przetrzymuje.

Liczniki `BrakiOdpowiedzi`, `SpoznioneOdpowiedzi` i `OpisWymiany` (min/srednia/max) zeruja sie
przy kazdym zestawieniu lacza, ida do kazdego wpisu w `podejrzane.txt` i pokazuja sie w stopce
okna glownego - zeby dalo sie porownac wlasny tor z tymi 243/245/247 ms ze sondy i od razu
zobaczyc, ile dokladamy sami.

**[NIEAKTUALNE, zostawione jako przestroga] Sterownik odpowiada na STOP ramka w formacie pozycji.** Zmierzone: po rozkazie `0x0F` Rot1Prog odsyla `57 02 00 08 20`,
czyli **208** czytane jak azymut, niezaleznie od tego, gdzie antena stoi. W sladzie widac to
wprost: `295 -> [208] -> 297` i `352 -> [208] -> 348` - antena byla w ruchu i jechala dalej
swoim torem, a miedzy jej odczyty wpadala ta jedna ramka.

Korelacja jest zupelna: na **dwadziescia** zapisanych przebiegow 208 pojawilo sie **wylacznie
w tych dwoch, w ktorych uzytkownik nacisnal STOP** - w pozostalych osiemnastu ani razu.
Stad tez warunek "tylko gdy krece dwoma naraz": przy dwoch rotorach STOP jest naciskany
znacznie czesciej.

Droga do tego wniosku prowadzila przez cztery bledne hipotezy - zalegle rozkazy w buforze,
wlasna sonda portow, dzielenie ramek przy 1200 bodach, wreszcie zaklocenia od drugiego silnika.
Dwie z nich byly prawdziwymi bledami i zostaly naprawione, ale zadna nie byla **ta** przyczyna.
Rozstrzygnelo zdanie uzytkownika, ze **skacze odczyt, a nie nastawa**, i drugie, ze dzieje sie
to **tylko przy dwoch rotorach** - dopiero wtedy policzylem korelacje ze STOP-em zamiast
szukac przeklamanych bajtow.

`Mostek.OdrzucicOdpowiedzNaStop` odrzuca **jedna** ramke: pierwsza po STOP i tylko wtedy, gdy
skacze o wiecej, niz rotor zdazy sie obrocic miedzy odpytaniami (30 stopni, okno dwoch sekund).
Prawdziwa pozycja tuz po zatrzymaniu rozni sie o kilka stopni i przechodzi bez zmian. Kazde
odrzucenie ida do `podejrzane.txt` - **nic nie znika po cichu**, bo filtr na danych o polozeniu
anteny musi byc rozliczalny.

**Dwa wejscia do jednej kolejki wymagaja zamka na calym odcinku, nie tylko na skladaniu.**
Do `_kolejkaPortu` wkladaja dwa watki: pompa z sieci i zegar dopychajacy zalegly ogon ramki.
Sam `SkladaczSpid` jest zamkniety, ale odcinek **"wyjmij ramki" - "wloz do kolejki"** juz nie
byl: dopychacz mogl wejsc w te szczeline i wrzucic ogon **przed** ramka, ktora pompa dopiero
wkladala. Do klienta poszlyby bajty w zlej kolejnosci - czyli dokladnie to, przed czym ma
chronic skladanie ramek. Stad `_kolejnosc`: obejmuje **pobranie i wlozenie razem**.

Zasada ogolna: jesli dwa watki produkuja do wspolnej kolejki, samo zamkniecie producenta nie
wystarcza - zamek musi obejmowac takze wlozenie, inaczej kolejnosc jest przypadkiem.

**Audyt komunikacji - kto pisze i gdzie sa bufory.** Do **gniazda** pisze pompa z portu, pod
semaforem `_bramka`; drugi pisarz (`WyslijKlawisz`, `OdpytujSpe`) istnieje tylko dla SPE i uzywa
tego samego semafora - przy rotorze pisarz jest jeden, wiec kolizja jest niemozliwa. Do **portu**
pisze wylacznie `PisarzPortu`, zdejmujac z jednej kolejki.

Bufory w torze, od sprzetu: UART sterownika, przejsciowka USB na Pi, bufory gniazda TCP po obu
stronach, odczyt mostka (1024 B, bez akumulacji), `SkladaczSpid` (skladana ramka, zwykle ponizej
pieciu bajtow, oprozniany po 120 ms ciszy), `_kolejkaPortu` (do 256 porcji, przy przepelnieniu
odpadaja **najstarsze**, bo najswiezsza pozycja jest najcenniejsza) i bufory com0coma, czyszczone
przy kazdym zestawieniu lacza.

**Rozkazy do sterownika tez wychodza calymi ramkami** (od 1.11.6). Ten sam `SkladaczSpid`
pracuje w obie strony, ale po stronie gniazda wyjmowanie ramek i zapis ida pod semaforem
`_bramka` - bo do gniazda pisze jeszcze zegar dopychajacy zalegly ogon, a gdyby chroniony byl
tylko zapis, obaj mogliby sie wyprzedzic. Sprawdzone: rozkaz podzielony na 4+9 bajtow wychodzi
**jednym zapisem**, a zapytanie o pozycje (same zera w polach cyfr) nie zostaje wziete za ramke
pieciobajtowa, bo jego piaty bajt to `0x00`, nie `0x20`.

**Mostek NIE jest juz w pelni przezroczysty - i sam o tym wie.** Skladanie ramek oraz filtr
pozycji **znaja protokol SPID**. Gdyby ktos wpial pod te sama pare com0com sterownik mowiacy
czyms innym (Yaesu GS-232, Hy-Gain DCU-1, rotctld), skladacz nie rozpoznalby ani jednej ramki
i kazda wymiana czekalaby na zawor czasowy - **120 ms opoznienia na kazda wiadomosc**, a piec
bajtow zaczynajacych sie od `0x57` i konczacych `0x20` moglby trafic pod filtr pozycji i zostac
skasowany. To nie jest teoretyczne: GS-232 ustawia azymut rozkazem zaczynajacym sie wlasnie
od `W` = `0x57`.

Dlatego od 1.11.7 skladacz **wycofuje sie sam**. `SkladaczSpid.Przezroczysty` jest prawdziwe,
gdy oddal **dwadziescia ogonow po czasie, nie rozpoznawszy ani jednej ramki** - wtedy `Dopisz`
puszcza wszystko wprost, a `OdrzucicNieprawdopodobnyOdczyt` od razu zwraca `false`. Prog jest
asymetryczny **celowo**: jedna poprawna ramka wystarczy, zeby uznac protokol za znany, a do
wycofania sie trzeba dwudziestu nieudanych prob. Lepiej raz za duzo poczekac niz zepsuc
dzialajacy tor.

Czego to **nie** zalatwia: pierwsze ~2,5 sekundy obcego protokolu (dwadziescia ogonow po
120 ms) i tak ida z opoznieniem, a dane, ktore przypadkiem wygladaja jak ramka SPID, nadal beda
skladane. Jesli kiedys naprawde zmienisz protokol, **wylacz skladanie jawnie** przy tworzeniu
mostka zamiast liczyc na samowycofanie - to jest siatka bezpieczenstwa, nie funkcja.

Sprawdzone w `TestSkladacza`: ciag `57 58 31 32 33 0D` (zaczyna sie od `0x57`, ale nie ma `0x20`
na piatym bajcie) po dwudziestu probach przelacza skladacz w tryb przezroczysty i od tej chwili
wychodzi natychmiast; jedna poprawna odpowiedz SPID na poczatku sprawia, ze **nie przelacza sie
nigdy**, nawet po dwudziestu pieciu smieciach.

**[historyczne] Rozkazy do sterownika szly tak, jak przyszly z pary - bez skladania.** W dzienniku widac, ze
PstRotator wpisuje cale trzynascie bajtow jednym zapisem, wiec w praktyce idzie cala ramka - ale
to wlasciwosc klienta, nie gwarancja mostka. Po drugiej stronie jest UART, gdzie bajty i tak
docieraja pojedynczo, wiec podzial sam w sobie nie szkodzi; gdyby kiedys okazalo sie, ze firmware
resynchronizuje sie po przerwie w srodku ramki, ten sam `SkladaczSpid` mozna wpiac takze w te
strone.

**Nie dziel ramki tylko dlatego, ze tak przyszla z sieci.** To byla przyczyna odczytu
208 stopni - i szukalem jej daleko, bo pytanie brzmialo "kto wysyla taka nastawe", a wlasciwe
brzmialo "kto czyta taka pozycje".

Sterownik nadaje **1200 bodow**, wiec pieciobajtowa odpowiedz `57 H1 H2 H3 20` idzie laczem
ponad 40 ms i dociera do nas w kawalkach. Mostek oddawal kazdy kawalek osobno, a zapis na pare
com0com potrafi stanac - zmierzone w sladzie: `zapisano na port 1 B w 203 ms` i `4 B w 589 ms`.
Program sterujacy dostawal wiec `57`, czekal, nie doczekiwal sie reszty i czytal swoj pusty
bufor. Zerowy bajt wziety za cyfre ASCII daje `0x00 - '0' = 0xD0 = 208` - stad **zawsze ta sama
liczba**, bo pustka jest zawsze taka sama. Potem PstRotator, wierzac, ze antena stoi na 208,
kazal jej tam pojechac - i stad **fizyczny** obrot, mimo ze nikt takiej nastawy nie wydal.

`SkladaczSpid` oddaje teraz **cala ramke naraz albo nic**, z zaworem bezpieczenstwa: ogon,
ktory nie doczekal sie dokonczenia przez 120 ms, idzie dalej taki, jaki jest (inaczej doklejalby
sie do nastepnej odpowiedzi). Sprawdzone osmioma przypadkami, w tym odpowiedzia przychodzaca
bajt po bajcie i niepelna ramka, ktora **nie wychodzi**, dopoki nie minie cisza.

To tlumaczy tez dwie rzeczy, ktore przez caly czas mnie mylily: objaw wymagal **dwoch
pracujacych portow** (wiecej ruchu to dluzsze przerwy miedzy kawalkami) i chodzil w parze
z **zolta dioda** (po zestawieniu lacza pierwsze ramki sa najbardziej poszarpane).

**"Ucieczka na 208 stopni" to nie azymut, tylko arytmetyka na pustym buforze.**
`0x00 - '0'` = `0x00 - 0x30` = `0xD0` = **208** przy odejmowaniu bez znaku na bajcie. Jedyna
stala, zawsze obecna ramka w tym torze to zapytanie o pozycje
`57 00 00 00 00 00 00 00 00 00 00 1F 20` - w polach cyfr **same zera**. Kto potraktuje zerowy
bajt jak cyfre ASCII, dostaje 208, **zawsze te sama wartosc, bo wejsciem jest pustka**. Stad
"nastawa 208" pojawia sie dokladnie wtedy, gdy lacze pada i program sterujacy nie dostaje
odpowiedzi. Szukaj wiec nie zrodla liczby 208, tylko **zrodla zerwania lacza**.

**Po zerwaniu wracaj od razu, nie po dwoch sekundach.** Kazda sekunda bez lacza to sekunda,
w ktorej program sterujacy nie dostaje odpowiedzi na zapytanie o pozycje - a wtedy czyta swoj
pusty bufor i wychodzi mu 208 stopni (patrz wyzej). Zwloka jest teraz stopniowana: 250 ms po
pierwszym zerwaniu, potem 500, 1000 i 2000 ms, a licznik zeruje sie po polaczeniu, ktore
przetrwalo ponad piec sekund. Przy niedostepnym Pi nie dobijamy sie wiec bez konca, a przy
pojedynczym kopnieciu przerwa jest osmiokrotnie krotsza.

**Ramki szukaj w strumieniu, nie w porcji.** Pierwsza wersja pulapki skanowala kazda porcje
z osobna - a ramka rozkazu ma 13 bajtow i potrafi przyjsc podzielona (w sladzie widac porcje
po 1 i 4 bajty). Pulapka mogla wiec przepuscic dokladnie to, na co czekala, i "nie ma pliku"
nic nie dowodzilo. `Pulapka.Wykrywacz` sklada teraz bajty i wyjmuje z nich cale ramki.
Ta sama zasada dotyczy wszystkiego, co czyta ten strumien.

**Loguj kazda nastawe, nie tylko podejrzana.** Nastaw jest kilka na godzine, a bez pelnej
listy nie da sie powiedziec, czy dziwna wartosc przyszla z komputera, czy pojawila sie dalej.
Do tego przy kazdym zestawieniu lacza idzie linia "pulapka uzbrojona" z numerem wersji -
inaczej brak pliku znaczy dwie rzeczy naraz: nic nie przeszlo albo wersja z pulapka nie byla
uruchomiona.

**Azymut licz kilkoma dzielnikami.** Zmierzone: PstRotator wysyla `(azymut + 360) * 10`
przy bajcie rozdzielczosci 1, ale `1136` przy rozdzielczosci 2 to tez 208 stopni. Pomylka
w dzielniku ukrylaby wlasnie te nastawe, ktorej szukamy.

**Rzadki objaw lap pulapka, nie sladem.** Slad trzeba wlaczyc **przed** zdarzeniem, wiec
zlapanie czegos, co zdarza sie raz na kilkanascie minut, wymaga szczescia albo megabajtow
zapisu. `Pulapka` dziala odwrotnie: chodzi **zawsze**, nic nie zapisuje, dopoki nie zobaczy
rozkazu, ktorego nie powinno byc - nastawy 208 stopni albo cyfr spoza ASCII (zerowy bajt
czytany jak cyfra daje na bajcie dokladnie 208). Wtedy dopisuje do `podejrzane.txt` sama ramke
**oraz 256 ostatnich bajtow w obie strony** i czas od zestawienia lacza. Bez tego kontekstu nie
da sie odroznic rozkazu, ktory ktos naprawde wyslal, od ramki zlozonej z kawalkow dwoch innych.

**Obrona przed przejeciem portu jest trojwarstwowa i zadna warstwa nie wystarcza sama.**
Port ser2neta ma jednego wlasciciela, wiec pytanie brzmi nie "czy ktos go zabierze", tylko
"co sie stanie, gdy sprobuje":

1. **Program nie prosi** - zadnego badania portow (nizej). To usuwa jedynego znanego sprawce.
2. **ser2net nie oddaje** - `kickolduser: false` w konfiguracji. Wtedy przypadkowe polaczenie
   z innego programu dostaje odmowe, zamiast wypychac dzialajacy mostek. Warunkiem jest punkt
   trzeci, inaczej martwa sesja zablokuje port na godziny.
3. **Martwa sesja musi znikac** - i tu uwaga na latwa pomylke, ktora sam popelnilem:
   keepalive TCP wykrywa martwego rozmowce **tylko tej stronie, ktora wysyla sondy**.
   `WlaczKeepAlive` (10 s ciszy, sonda co 2 s) chroni wiec **nas**: gdy Pi zniknie, mostek
   dowiaduje sie o tym w kilkanascie sekund zamiast siedziec na martwym gniezdzie. Nie
   przyspiesza natomiast sprzatania po **naszej** martwej sesji po stronie Pi - gdy padnie
   komputer, sond nie ma kto wysylac. Tamta strona potrzebuje wlasnego srodka: `timeout`
   w konfiguracji ser2neta (zamyka polaczenie po N sekundach bez danych) albo keepalive
   wlaczonego u siebie. Bez tego `kickolduser: false` oznacza, ze po padzie komputera port
   bywa zablokowany az do domyslnych dwoch godzin keepalive Linuksa.

Do tego **widocznosc**: `Mostek.ObcePrzejecia` liczy czyste zamkniecia przez druga strone
w oknie pieciu minut i dopiero trzecie uznaje za podejrzane (jedno zdarza sie przy restarcie
ser2neta). Wtedy dioda ser2neta robi sie pomaranczowa, a w stopce okna stoi wprost "port 4001
przejmowany przez inny program (3x)". Bez tego objaw wyglada wylacznie jak program sterujacy
bez odpowiedzi - czyli jak nastawa 208 - i szuka sie nie tam, gdzie trzeba.

**Nie badaj portow ser2neta zadnym wlasnym polaczeniem. Nigdy.** To byla przyczyna slynnej
"nastawy 208": nie przeklamanie bajtow, tylko **program, ktory sam sobie (i innej swojej kopii)
zrywal lacze**.

Droga do tego wniosku ma trzy etapy i warto ja zapamietac, bo dwa pierwsze byly bledne:

1. Najpierw podejrzenie padlo na zaleglosci w buforze portu. Odpadlo, gdy uzytkownik
   powiedzial, ze **208 nigdy nie bylo jego nastawa**.
2. Potem na sonde, ktora pomijala tylko mostek **polaczony**, a sondowala mostek w trakcie
   laczenia. To bylo prawda i wymagalo poprawki, ale po niej zerwania **zostaly**.
3. Rozstrzygnal slad z wlasnym powodem rozlaczenia: ser2net zamykal polaczenie **czysto**,
   w rytmie **dokladnie 60 sekund**, a dwa polaczenia do dwoch roznych portow konczyly sie
   **w tej samej milisekundzie**. 60 sekund to nasz wlasny odstep miedzy sprawdzeniami
   (`osiagalne == rotory.Count ? 60 : 20`), a jeden przebieg laczy sie po kolei do wszystkich
   portow - w sieci lokalnej w ulamkach milisekundy. Sprawcą była **druga kopia programu na
   innym komputerze**, z zatrzymanymi mostkami: nie miala czego pomijac, wiec badala wszystko.

Dlatego badania nie ma w ogole. Dioda mowi to, co wiemy za darmo: polaczony mostek jest dowodem,
ze port odpowiada, mostek z bledem niesie gotowy komunikat, a gdy wszystkie sa zatrzymane,
uczciwa odpowiedz brzmi "nie wiem" - i tak jest napisane w podpowiedzi. **Sprawdzanie, ktore
psuje to, co sprawdza, jest gorsze niz brak sprawdzania.**

**Rozkaz nastawy ma azymut w dziesiatych czesciach stopnia.** Zmierzone na zywym torze:
`57 36 36 30 30 01 ... 2F 20` to `6600` czyli **300,0 st.**, a `3800` to 20,0 st. Czyli cztery
cyfry ASCII = (azymut + 360) * 10. Odpowiedz sterownika ma format inny - piec bajtow z cyframi
**surowymi** (`57 H1 H2 H3 20`, azymut = H1*100 + H2*10 + H3 - 360). Nie myl tych dwoch.

**Nastawa 208, ktorej nikt nie wydal: policzone, nie zgadniete.** Uczciwy rozkaz "obroc na
208" to `57 30 35 36 38 01 30 30 30 30 01 2F 20`. Przeliczone dla wszystkich azymutow 0-359
i wszystkich przeklaman, jakie moze dac lacze szeregowe (`narzedzia`-owy odpowiednik siedzi
w stanowisku testowym, klasa `Skad208`):

* **pojedynczy przekrecony bit** daje 208 tylko w **6** przypadkach i zawsze z azymutu
  sasiedniego: 108, 168, 188, 200, 209, 218. Z dowolnego innego azymutu jeden bit 208 nie da,
* **obciety bit 7** (lacze siedmiobitowe, ktore raz juz nas ugryzlo przy com0com) - **0**
  przypadkow. Ta sciezka jest wykluczona,
* **dostawiony bit parzystosci** - **0** przypadkow,
* **sklejka ogona jednej ramki z glowa nastepnej** - **0** przypadkow.

Wniosek: jesli sterownik dostaje 208, to ramka jest **poprawna i ktos ja naprawde wyslal**.
Szukaj wiec nie przeklamania bitow, tylko **krzyzowania sie strumieni**: rozkaz dla jednego
rotora trafiajacy do drugiego. Pierwsze miejsce do sprawdzenia jest po stronie Pi - regula
udev dla PL2303 bez numeru seryjnego wiaze urzadzenie **po sciezce USB**, wiec po przelaczeniu
sie urzadzenia `/dev/antA3S` moze wskazywac inny sterownik. To tlumaczy tez, czemu objaw
wymaga **dwoch** pracujacych portow i czemu chodzi w parze z zolta dioda: przeenumerowanie
urzadzenia zrywa polaczenie ser2neta.

**Slad musi mowic, ktorego mostka dotyczy linia.** Przy dwoch pracujacych mostkach linie
mieszaja sie ze soba i nie da sie powiedziec, do ktorego portu poszedl ktory bajt - a to jest
pierwsze pytanie przy podejrzeniu przecieku. Kazda linia ma teraz podpis
`etykieta [CNCB10 -> 192.168.6.3:4001]`, a dla mostkow rotorowych `SladSpid` rozbiera ramke
na czytelne "NASTAWA 208 st." zamiast samego heksu.

**Dane sprzed zestawienia lacza sa nieaktualne i nie wolno ich przepuscic.** Port jest
otwierany **przed** polaczeniem z ser2netem i przez cala faze laczenia nikt z niego nie czyta -
sterownik portu odklada wiec wszystko, co klient zdazy wpisac. PstRotator powtarza nastawe co
sekunde, dopoki rotor nie stanie na azymucie, wiec kilkanascie sekund zoltej diody to
kilkanascie ramek "obroc sie na X" czekajacych w buforze; w chwili zestawienia lacza szly
wszystkie naraz do sterownika i **antena ruszala na polecenie sprzed minuty**. Objaw zgloszony
przez uzytkownika: w momencie zapalenia zoltej diody sterownik dostaje w kolko te sama nastawe.

Poprawka ma dwie czesci i obie sa potrzebne:

* `StrumienPortu.Wyczysc` (PurgeComm) kasuje bufory portu **tuz przed ruszeniem pomp**,
  nie przy otwarciu - miedzy otwarciem a polaczeniem mija cala faza laczenia.
* Pompa z portu odrzuca wszystko **do pierwszej ciszy** na linii (odczyt bez danych, limit
  25 ms). Czyszczenie moglo trafic w srodek ramki, a jej ogon przesunalby sterownikowi caly
  strumien: kolejna ramka zlozylaby mu sie z polowek dwoch roznych. Cisza to jedyna granica
  ramki, jaka mamy bez wnikania w protokol - mostek wozi tez SPE i nie moze znac tresci.

Bezpiecznik: jesli cisza nie nadejdzie przez sekunde, przepuszczamy mimo wszystko. Mostek,
ktory wyglada na polaczony i nic nie przepuszcza, bylby gorszy niz to, przed czym bronimy.

**Nie pytaj `Control.DeviceDpi` o powiekszenie ekranu.** W .NET Framework ta wlasciwosc
sledzi monitor okna dopiero wtedy, gdy program ma w `app.config` sekcje `DpiAwareness` -
a bez niej zwraca DPI z chwili startu procesu, czyli **monitora glownego**. Na maszynie,
gdzie pulpit ma 100%, a drugi ekran 150%, dawalo to 96: system rysowal czcionki w pelnej
wielkosci, a program uwazal, ze nie ma czego skalowac. Objaw byl **identyczny** jak przed
poprawka skalowania - latwo uznac, ze poprawka nie dziala, i szukac bledu nie tam, gdzie
trzeba. `Ui.DpiEkranu` pyta wiec system wprost: `GetDpiForWindow` dla okna z uchwytem,
a wczesniej `GetDpiForMonitor` dla monitora pod kursorem. Po pokazaniu okna `Ui.PoprawSkale`
sprawdza to jeszcze raz i doklada roznice, bo dopiero wtedy wiadomo, na ktorym monitorze
okno naprawde stoi.

**Tytul okna glownego niesie wersje i wykryte powiekszenie.** "Rotory 1.10.1 · 150%".
Bez tego z drugiej strony ekranu nie da sie odroznic trzech roznych rzeczy, ktore wygladaja
tak samo: aktualizacja nie weszla, program zle odczytal powiekszenie, uklad ma blad.

**Okna skladaj w jednostkach dla 100%, a na koniec przeskaluj.** Czcionki podajemy
w punktach, wiec przy powiekszeniu ekranu 150% system rysuje je o polowe wieksze - ale
wspolrzedne i rozmiary kontrolek sa w pikselach i same nie urosna. Zmierzone na ekranie
1920x768 przy 150%: **48 napisow nie miescilo sie w swoich ramkach** (przyciski pokazywaly
"Sterowa" zamiast "Sterowanie", naglowki sekcji byly przyciete w pol wysokosci). Poprawka to
jedno wywolanie `Ui.SkalujPodEkran(this)` na koncu konstruktora - `Control.Scale` przelicza
wspolrzedne, rozmiary i marginesy calego drzewa. Dwie rzeczy, ktore trzeba o nim wiedziec,
bo obie sa sprawdzone pomiarem, nie domyslem:

* **Czcionek nie rusza.** Po `Scale(1,5)` czcionka 9 pt zostaje 9 pt, a kontrolka 200x18
  robi sie 300x27. To jest dokladnie to, czego chcemy: punkty skaluja sie same z DPI.
  Gdyby skalowal takze czcionki, tekst uroslby dwa razy (1,5 x 1,5).
* **Siatek nie rusza.** Szerokosci kolumn, wysokosc wierszy i wysokosc naglowka zostaja
  co do piksela takie same - stad "SPE Exper..." w kolumnie szerokiej na 180 px. Poprawia
  je `SkalujSiatki` recznie.

Kontrolki dokladane **po** przeskalowaniu okna (karty listy w oknie glownym powstaja na nowo
po kazdej zmianie ustawien) nie dostana skali z zadnej innej strony - trzeba je przepuscic
przez `Ui.Skaluj`. Stale ukladu przeliczaj przez `Ui.Px`.

Do sprawdzania jest jeden uchwyt: `Ui.SkalaWymuszona`. Na maszynie ze 100% inaczej nie da
sie zobaczyc, co okna zrobia przy 150%; w dzialajacym programie zostaje `null`.

**Okno stanu jest wyjatkiem - ono nie skaluje, tylko mierzy.** `UkladStanu` liczy wysokosci
z `Font.Height`, a szerokosci z `TextRenderer.MeasureText`, bo musi jeszcze zdecydowac,
czy tabela idzie na jedna czy dwie kolumny. Nie wolno wiec wolac na nim `SkalujPodEkran` -
uklad dostalby skale dwa razy.

**Lista w oknie glownym dobiera liczbe kolumn do miejsca.** Szesc anten plus wzmacniacz to
626 jednostek wysokosci, czyli 939 px przy 150% - na ekranie wysokim na 768 px jedna kolumna
nie ma szans, a szerokosci jest tam az nadto. `IleKolumn` bierze najmniejsza liczbe kolumn
(do trzech), przy ktorej lista miesci sie w pionie, o ile starczy szerokosci; `PrzeliczKolumny`
robi to samo przy kazdej zmianie rozmiaru okna, wiec rozciagniecie okna w bok cos daje.
Zmierzone dla 1920x768 przy 150%: 1720x732, dwie kolumny, wszystko widac.

**Decyzja o ukladzie nie moze zalezec od paska przewijania, ktory sama wywoluje.** Dwie
kolumny wystawialy pionowy pasek, ten zabieral 17 px szerokosci, przy nastepnej zmianie
rozmiaru brakowalo tych kilkunastu pikseli i uklad wracal do jednej kolumny **na stale**.
`PrzeliczKolumny` liczy wiec miejsce tak, jakby paskow nie bylo, a samo ukladanie chroni
flaga `_ukladamListe` przed wejsciem w siebie.

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

**Kolumny licza sie wzdluz grup, a nie tylko wzdluz wysokosci.** `MainForm.Rozloz` probuje
najpierw dac kazdej grupie wlasna kolumne (anteny osobno, urzadzenia osobno) i dopiero gdy
najwyzsza kolumna nie miesci sie w ekranie, wraca do rownego podzialu na wysokosc. Rowny
podzial daje slupki podobnej dlugosci, ale rozrywa to, co dla patrzacego jest calosci: przy
szesciu antenach i jednym wzmacniaczu wychodzilo *cztery anteny | dwie anteny + Urzadzenia +
wzmacniacz*, czyli naglowek grupy siedzial w polowie drugiej kolumny. Zmierzone: przy 150% na
ekranie 1920x1080 uklad to teraz *szesc anten | Urzadzenia + wzmacniacz*.

Podzial wzdluz grup stosujemy **tylko wtedy, gdy grup jest dokladnie tyle, ile kolumn**, i gdy
najwyzsza kolumna miesci sie w tym, co zostaje po naglowku i przyciskach. Na ekranie GPD
(1920x768 przy 150%) szesc anten w jednej kolumnie to 472 jednostki ukladu, a zostaje ich
okolo 314 - tam wiec nadal obowiazuje podzial rowny, bo **paski przewijania sa gorsze niz
nierowne slupki**. Trzeciej kolumny nie ma z czego zrobic: `580 + 2 * 556` nie miesci sie
w `1920/1.5`.

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
