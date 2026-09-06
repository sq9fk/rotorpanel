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

**Zero bajtów z portu to cisza, z gniazda to rozłączenie.** Rozróżnienie w `Mostek.Pompa`
jest istotne; potraktowanie ich tak samo albo zapętli program, albo zerwie działające łącze.

**Wymuszenie uchwytu okna w `UtworzTray`.** Formularz, który nigdy nie był pokazany, nie
zgłasza `HandleDestroyed`, więc `Close()` nie kończy pętli komunikatów. Program wychodzi przez
`Application.Exit()` po posprzątaniu, nie przez `Close()`.

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

**PowerShell rozwija tablice zwracane z funkcji.** W generatorze ikony (`ikona/generuj-ikone.ps1`)
`return ,$dane` z przecinkiem chroni tablicę bajtów. Bez tego plik `.ico` wychodził uszkodzony,
mimo że wyglądał poprawnie.

**`System.Drawing` nie czyta wpisów PNG w ikonach.** Rozmiary do 64 px zapisujemy jako DIB,
PNG zostaje dla 128 i 256.

**Paczki com0com bez przyrostka `-signed` są niepodpisane.** Decyduje przyrostek, nie numer
wersji — gałąź 2.2.2.0 ma zarówno warianty podpisane, jak i niepodpisane. W dokumentacji było
kiedyś napisane odwrotnie; nie przywracaj tego.

**Przy generowaniu plików uważaj na sekwencje ucieczki.** Literały regex i ścieżki `\.\`
łatwo tracą backslashe, gdy plik powstaje przez narzędzie pośredniczące. Po wygenerowaniu
sprawdź je w pliku, zanim uruchomisz kompilator — `\s` w zwykłym literale C# nie skompiluje
się w ogóle, ale `` skompiluje się jako znak backspace i po cichu zepsuje wyrażenie.

## Konwencje

- Komentarze i identyfikatory w kodzie **bez polskich znaków diakrytycznych**; napisy widoczne
  w interfejsie mogą je mieć, bo pliki źródłowe mają BOM UTF-8.
- Komentarz wyjaśnia **dlaczego**, nie co robi linijka.
- Kolory i czcionki wyłącznie z `Theme.cs`. Kontrolki własne w `Ui.cs`.
- `rotory.json` jest w `.gitignore` — w repo trzymamy `rotory.przyklad.json`.
- Numer wersji żyje w dwóch miejscach: `RotorPanel.csproj` i `app.manifest`. Podnoś oba naraz.
