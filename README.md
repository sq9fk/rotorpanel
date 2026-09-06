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
| `rotory[].nr` | numer rotora, do którego odwołują się anteny |
| `rotory[].nazwa` | nazwa własna, np. „maszt A” |
| `rotory[].com` | port widoczny dla PstRotatora |
| `rotory[].dev` | druga strona pary, używana przez mostek |
| `rotory[].ip` | adres `ser2net` dla tego rotora; puste znaczy `piIp` |
| `rotory[].port` | port TCP wystawiony przez `ser2net` |
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

## Interfejs

### Zasobnik systemowy

Program startuje **zminimalizowany**, jako ikona przy zegarze. Kolor igły kompasu pokazuje
stan zbiorczy: szary gdy nic nie działa, pomarańczowy przy łączeniu, zielony gdy przynajmniej
jeden mostek jest połączony.

| gest | efekt |
|---|---|
| kliknięcie lewym | pokazuje panel, a gdy jest otwarty — chowa go |
| kliknięcie prawym | menu: otwarcie panelu, łączenie pojedynczego rotora lub wszystkich, wyjście |
| krzyżyk w oknie | chowa program do zasobnika, **nie** zamyka go |

Program kończy się wyłącznie przez pozycję **Zamknij** w menu ikony.

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
  wybiera rotor z listy, która powstaje automatycznie z tabeli rotorów.
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
| `MainForm.cs` | okno główne: budowa listy i układ |
| `MainForm.Stan.cs` | karty anten i cykl odświeżania |
| `MainForm.Siec.cs` | diody łączności oraz oznaczenia nadajników |
| `MainForm.Tray.cs` | ikona w zasobniku, menu, chowanie i zamykanie |
| `SettingsForm.cs` | okno ustawień: nagłówek i przyciski |
| `SettingsForm.Siatki.cs` | tabele rotorów i anten |
| `SettingsForm.Akcje.cs` | pary, wybór pliku, pobieranie nazw, zapis |
| `PairsForm.cs` | lista par com0com, zakładanie i usuwanie |
| `NewPairForm.cs` | okienko nowej pary z walidacją nazw |
| `Com0Com.cs` | odczyt par z rejestru i wywołania `setupc` |
| `SterownikAnten.cs` | odczyt nazw anten i przypisania nadajników ze sterownika |
| `Theme.cs`, `Ui.cs` | paleta oraz kontrolki własne: karta, dioda, znacznik |
| `instalator/` | skrypty instalacyjne i skrypt składania paczki |
| `ikona/` | generator ikony i gotowy plik `.ico` |

## Licencja

MIT — patrz [LICENSE](LICENSE).

## Powiązane projekty

- [ant-sw-2x6](https://github.com/sq9fk/ant-sw-2x6) — przełącznica antenowa 6x2, źródło nazw anten
- [k3ng_controler_nano_light](https://github.com/sq9fk/k3ng_controler_nano_light) — firmware sterownika rotora
- [rotator_wifi_bridge](https://github.com/sq9fk/rotator_wifi_bridge) — mostek WiFi dla rotora
