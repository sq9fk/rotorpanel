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

---

## Wymagania

| | |
|---|---|
| System | Windows 10 lub 11 |
| Środowisko | .NET Framework 4.8 — **część systemu**, nic nie trzeba doinstalowywać |
| Sterownik | [com0com 3.0.0.0](https://com0com.sourceforge.net/) w wersji **podpisanej** |
| Po stronie Pi | `ser2net` z portami TCP na urządzeniach szeregowych |

> Wersja com0com 2.2.2.0 jest niepodpisana i wymaga wyłączania wymuszania podpisów
> sterowników. Nie używać.

---

## Instalacja

### Wariant przenośny

Skopiuj `RotorPanel.exe` gdziekolwiek i uruchom. Przy pierwszym starcie tworzy obok siebie
`rotory.json` z ustawieniami domyślnymi. Jeśli katalog jest tylko do odczytu, konfiguracja
ląduje w `%APPDATA%\RotorPanel`.

Zabierając ze sobą także `rotory.json`, przenosisz pełną konfigurację — na przykład na pendrive.

### Wariant z instalatorem

Paczkę składa `instalator\zloz-paczke.bat` — buduje wersję wynikową i pakuje ją razem ze
skryptami do `dist\RotorPanel-paczka.zip`. W środku są trzy pliki wsadowe:

| plik | rola |
|---|---|
| `install.bat` | kopiuje program do `%LOCALAPPDATA%\RotorPanel`, tworzy skróty na pulpicie i w menu Start |
| `utworz-pary.bat` | zakłada pary portów com0com, sam prosi o uprawnienia administratora |
| `autostart.bat` | włącza lub wyłącza uruchamianie przy logowaniu |

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

Pary zakłada `utworz-pary.bat` albo okno **Pary COM…** w samym programie. Oba wywołują
`setupc.exe` z podniesionymi uprawnieniami.

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
| `sterownikAnten` | adres sterownika anten, np. `http://192.168.1.101/` |
| `autoPolacz` | czy zestawiać mostki od razu po uruchomieniu |
| `anteny[].nr` | numer wyjścia w przełącznicy antenowej |
| `anteny[].nazwa` | nazwa anteny, pobierana ze sterownika |
| `anteny[].maRotor` | czy do anteny podpięty jest rotor |
| `anteny[].com` | port widoczny dla PstRotatora |
| `anteny[].dev` | druga strona pary, używana przez mostek |
| `anteny[].ip` | adres `ser2net` dla tej anteny; puste znaczy `piIp` |
| `anteny[].port` | port TCP wystawiony przez `ser2net` |

Wzór znajdziesz w [`rotory.przyklad.json`](rotory.przyklad.json).

Kilka anten może wskazywać **tę samą parę portów** — tak wygląda maszt z kilkoma antenami.
Program tworzy wtedy jeden mostek współdzielony przez te anteny, bo fizycznie to jedno
połączenie.

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

Liczniki `RX` i `TX` pokazują bajty, które faktycznie przeszły, oraz bieżącą przepustowość.
To najprostszy sposób sprawdzenia, czy PstRotator w ogóle odpytuje sterownik: zielona dioda
przy zerowych licznikach znaczy, że łącze stoi, ale nikt z niego nie korzysta.

Po zerwaniu połączenia mostek ponawia próbę co 2 sekundy, więc restart Raspberry albo
chwilowy zanik sieci nie wymagają żadnej reakcji.

### Ustawienia

Tabela anten: numer, checkbox **Rotor**, nazwa, para portów z listy rozwijanej, adres i port TCP.

- **Nazwy anten są nieedytowalne** — pochodzą wyłącznie ze sterownika, przycisk *Pobierz nazwy*.
  Przy braku łączności zostają `ANT1`–`ANT6`.
- **Pary wybiera się z listy**, budowanej z rejestru sterownika com0com. Nie da się wpisać
  nieistniejącego portu ani pomylić stron pary.
- Wybranie pary samo zaznacza *Rotor*; odznaczenie *Rotora* czyści parę, adres i port.
- Pole adresu jest opcjonalne — puste znaczy „użyj domyślnego". Pozwala trzymać część
  rotorów na innym Raspberry.

### Pary COM

Podgląd stanu portów (`wolny`, `zajęty`, `nie istnieje`, albo konkretny kod błędu Win32),
lista par z rejestru wraz z ich parametrami, oraz przyciski tworzenia i usuwania par.
Wykrzyknik przy parze oznacza parametr odbiegający od domyślnego.

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

Ikonę generuje skrypt `ikona\generuj-ikone.ps1` — rysuje kompas z anteną kierunkową
w siedmiu rozmiarach i składa plik `.ico`. Uruchamiać tylko po zmianie wyglądu ikony.

## Układ projektu

| plik | rola |
|---|---|
| `Program.cs` | punkt wejścia, blokada jednej instancji, obsługa nieprzechwyconych wyjątków |
| `Config.cs` | model konfiguracji, wczytywanie i zapis |
| `Json.cs`, `JsonZapis.cs` | własny czytnik i zapisywacz JSON — brak zależności zewnętrznych |
| `PortIo.cs` | dostęp do portu szeregowego przez Win32 |
| `Mostek.cs` | mostek dwukierunkowy port ⇄ TCP, ponawianie, liczniki |
| `MainForm.cs` | okno główne |
| `MainForm.Tray.cs` | ikona w zasobniku, menu, chowanie i zamykanie |
| `SettingsForm.cs` | edycja konfiguracji |
| `PairsForm.cs`, `Com0Com.cs` | zarządzanie parami com0com |
| `SterownikAnten.cs` | pobieranie nazw anten ze sterownika przełącznicy |
| `Theme.cs`, `Ui.cs` | paleta i kontrolki własne |
| `instalator/` | skrypty instalacyjne i skrypt składania paczki |
| `ikona/` | generator ikony i gotowy plik `.ico` |

## Licencja

MIT — patrz [LICENSE](LICENSE).

## Powiązane projekty

- [ant-sw-2x6](https://github.com/sq9fk/ant-sw-2x6) — przełącznica antenowa 6x2, źródło nazw anten
- [k3ng_controler_nano_light](https://github.com/sq9fk/k3ng_controler_nano_light) — firmware sterownika rotora
- [rotator_wifi_bridge](https://github.com/sq9fk/rotator_wifi_bridge) — mostek WiFi dla rotora
