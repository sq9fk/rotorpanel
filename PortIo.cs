using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RotorPanel;

public enum StanPortu { Brak, Wolny, Zajety }

/// <summary>
/// Dostep do portu szeregowego przez Win32, bez System.IO.Ports.
/// Dzieki temu dziala takze z nazwami spoza wzorca COMxx, np. CNCB10.
/// </summary>
public static class PortIo
{
    private const char BS = (char)92;

    /// <summary>Prefiks urzadzenia Win32: dwa ukosniki wsteczne, kropka, ukosnik wsteczny.</summary>
    public static readonly string Prefiks = new(new[] { BS, BS, '.', BS });

    private const uint GENERIC_READ  = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const int  ERROR_FILE_NOT_FOUND = 2;
    private const int  ERROR_ACCESS_DENIED  = 5;
    private const int  ERROR_SHARING_VIOLATION = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share,
        IntPtr sec, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle h, ref COMMTIMEOUTS t);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetCommModemStatus(SafeFileHandle h, out uint stan);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetupComm(SafeFileHandle h, uint wejscie, uint wyjscie);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EscapeCommFunction(SafeFileHandle h, uint funkcja);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool PurgeComm(SafeFileHandle h, uint co);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ClearCommError(SafeFileHandle h, out uint bledy, out COMSTAT stan);

    [StructLayout(LayoutKind.Sequential)]
    internal struct COMSTAT
    {
        public uint Flagi;
        public uint cbInQue;
        public uint cbOutQue;
    }

    internal const uint PURGE_TXABORT = 0x0001;
    internal const uint PURGE_RXABORT = 0x0002;
    internal const uint PURGE_TXCLEAR = 0x0004;
    internal const uint PURGE_RXCLEAR = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle h, ref DCB dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle h, ref DCB dcb);

    [StructLayout(LayoutKind.Sequential)]
    private struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flagi;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public sbyte XonChar;
        public sbyte XoffChar;
        public sbyte ErrorChar;
        public sbyte EofChar;
        public sbyte EvtChar;
        public ushort wReserved1;
    }

    private const uint SETRTS = 3;
    private const uint CLRRTS = 4;
    private const uint SETDTR = 5;

    // com0com laczy linie miedzy stronami pary: nasze CTS to RTS drugiej strony,
    // nasze DSR to jej DTR. Podniesione znaczy, ze ktos po tamtej stronie ma port
    // otwarty - a wtedy nie wolno nam sie wtracac wlasnym odpytywaniem.
    internal const uint MS_CTS_ON = 0x0010;
    internal const uint MS_DSR_ON = 0x0020;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(SafeFileHandle h, byte[] bufor, uint ile,
        out uint przeczytane, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteFile(SafeFileHandle h, byte[] bufor, uint ile,
        out uint zapisane, IntPtr overlapped);

    /// <summary>Czy port istnieje i czy jest wolny - bez otwierania go na dluzej.</summary>
    /// <summary>Slowny opis stanu portu, z kodem bledu przy nietypowych przypadkach.</summary>
    public static string Opis(string nazwa)
    {
        IntPtr h = CreateFileW(Prefiks + nazwa, GENERIC_READ | GENERIC_WRITE,
                               0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h != new IntPtr(-1)) { CloseHandle(h); return "wolny"; }

        int blad = Marshal.GetLastWin32Error();
        if (blad == ERROR_FILE_NOT_FOUND)     return "nie istnieje";
        if (blad == ERROR_ACCESS_DENIED)      return "zajety";
        if (blad == ERROR_SHARING_VIOLATION)  return "zajety (sharing)";
        return "blad " + blad;
    }

    public static StanPortu Sprawdz(string nazwa)
    {
        IntPtr h = CreateFileW(Prefiks + nazwa, GENERIC_READ | GENERIC_WRITE,
                               0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h != new IntPtr(-1)) { CloseHandle(h); return StanPortu.Wolny; }

        int blad = Marshal.GetLastWin32Error();
        if (blad == ERROR_ACCESS_DENIED) return StanPortu.Zajety;
        return StanPortu.Brak;
    }

    /// <summary>
    /// Otwiera port. Limity czasu ustawione tak, ze odczyt wraca natychmiast po
    /// nadejsciu danych, a przy ciszy po 200 ms z zerem bajtow - to pozwala
    /// petli reagowac na zadanie zatrzymania.
    /// </summary>
    /// <summary>
    /// Czy port o tej nazwie jest przez kogos zajety. Sprawdzamy proba otwarcia:
    /// gdy sie uda, natychmiast zamykamy. To jedyny pewny sposob, zeby wykryc klienta,
    /// ktory ma otwarta druga strone pary, ale akurat milczy - po samych liniach
    /// sterujacych go nie widac, bo SPE Term ich nie podnosi.
    /// </summary>
    public static bool Zajety(string nazwa)
    {
        IntPtr h = CreateFileW(Prefiks + nazwa, GENERIC_READ | GENERIC_WRITE,
                               0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h != new IntPtr(-1))
        {
            CloseHandle(h);
            return false;
        }
        return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED;
    }

    public static Stream Otworz(string nazwa)
    {
        string sciezka = Prefiks + nazwa;
        IntPtr h = CreateFileW(sciezka, GENERIC_READ | GENERIC_WRITE,
                               0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == new IntPtr(-1))
        {
            int blad = Marshal.GetLastWin32Error();
            string opis = blad == ERROR_FILE_NOT_FOUND ? "port nie istnieje"
                        : blad == ERROR_ACCESS_DENIED  ? "port zajety przez inny program"
                        : "blad " + blad;
            throw new IOException(nazwa + ": " + opis);
        }

        var uchwyt = new SafeFileHandle(h, true);
        var limity = new COMMTIMEOUTS
        {
            ReadIntervalTimeout        = uint.MaxValue,
            ReadTotalTimeoutMultiplier = uint.MaxValue,
            ReadTotalTimeoutConstant   = 25,
            WriteTotalTimeoutMultiplier = 0,
            // **Dwiescie milisekund, nie dwie sekundy.** Limit czasu zapisu to jedyna dzwignia,
            // ktora mamy na uchwycie synchronicznym: gdy program po drugiej stronie pary
            // przestaje odbierac, `WriteFile` stoi **dokladnie tyle**. Zmierzone 16 wrzesnia:
            // przy dwoch sekundach jeden zapis 431 bajtow trwal **4542 ms** (dwa limity pod
            // rzad), a czekanie na uchwyt w tym samym wpisie wynioslo **32 ms** - czyli
            // szeregowanie z odczytem bylo juz zalatwione, a blokada siedziala w samym zapisie.
            WriteTotalTimeoutConstant   = 200
        };
        if (!SetCommTimeouts(uchwyt, ref limity))
        {
            uchwyt.Dispose();
            throw new IOException(nazwa + ": SetCommTimeouts, blad " + Marshal.GetLastWin32Error());
        }
        // **Zapas w buforach portu.** Wzmacniacz odpowiada na kazde zapytanie klatka
        // ekranu po 371 bajtow, a SPE Term potrafi pytac co 95 ms - zmierzone 15 wrzesnia:
        // dziesiec zapytan w 700 ms, czyli blisko czterech kilobajtow w serii. Gdy bufor
        // sie zapelni, `WriteFile` **stoi**, az druga strona odbierze; przy domyslnych
        // rozmiarach widzielismy zapis trwajacy 3598 ms. Zapas nic nie kosztuje.
        SetupComm(uchwyt, 65536, 65536);

        UstawOsiemBitow(uchwyt, nazwa);

        // Podnosimy DTR i RTS. Na prawdziwym laczu RS-232 robi to urzadzenie po drugiej
        // stronie, a com0com przenosi te linie na druga strone pary: nasze DTR staje sie
        // dla klienta DSR i DCD, nasze RTS jego CTS. Bez tego SPE Term widzi opuszczone
        // linie, uznaje, ze wzmacniacza nie ma, i mimo otwartego portu tylko podtrzymuje
        // lacze - zmierzone w sladzie: przez cala minute wysylal wylacznie ramke
        // 55 55 55 01 00 00 i nie zapytal o nic, nawet gdy przestalismy mu przeszkadzac.
        // DTR wysoko, RTS nisko - taki sam spoczynek jak na prawdziwej linii do
        // wzmacniacza (patrz StrumienTelnet). Klient po drugiej stronie pary widzi
        // dzieki temu DSR i DCD podniesione, tak jak przy realnym urzadzeniu.
        EscapeCommFunction(uchwyt, SETDTR);
        EscapeCommFunction(uchwyt, CLRRTS);

        return new StrumienPortu(uchwyt);
    }

    /// <summary>
    /// Ustawia na naszej stronie pary osiem bitow danych, bez parzystosci, jeden bit
    /// stopu. **To nie jest kosmetyka - bez tego ginie osmy bit kazdego bajtu.**
    ///
    /// com0com nie dziedziczy ustawien z drugiej strony pary; swiezo otwarty port ma
    /// domyslne 1200 7-E-1, a przy siedmiu bitach danych sterownik obcina najstarszy
    /// bit. Zmierzone w sladzie: klient wysylal `0x90` (zapytanie o status), a do
    /// wzmacniacza szlo `0x10` - czyli **strzalka w prawo**; `0x80` (wlaczenie podgladu
    /// ekranu) zamienialo sie w `0x00`. Dlatego zaden zewnetrzny program nie dzialal
    /// przez mostek, choc bajty plynely w obie strony, a nasze wlasne odpytywanie bylo
    /// poprawne - my piszemy prosto do gniazda i przez pare nie przechodzimy.
    ///
    /// To co innego niz odczytywanie parametrow z pary, zeby podac je dalej przez
    /// RFC 2217 - tamtego nie da sie zrobic i tamten kod slusznie poszedl.
    /// </summary>
    private static void UstawOsiemBitow(SafeFileHandle uchwyt, string nazwa)
    {
        var dcb = new DCB { DCBlength = (uint)Marshal.SizeOf(typeof(DCB)) };
        if (!GetCommState(uchwyt, ref dcb)) return;

        dcb.BaudRate = 115200;
        dcb.ByteSize = 8;
        dcb.Parity   = 0;   // NOPARITY
        dcb.StopBits = 0;   // ONESTOPBIT

        // fBinary = 1 (wymagane przez Windows) oraz fDtrControl = ENABLE; wszystkie
        // bity sterowania przeplywem zostaja wyzerowane, a fRtsControl = DISABLE
        // trzyma RTS nisko - tak ma wygladac spoczynek linii.
        dcb.Flagi = 0x0001u | (1u << 4);

        SetCommState(uchwyt, ref dcb);
    }
}

/// <summary>Minimalny strumien na uchwycie portu szeregowego.</summary>
internal sealed class StrumienPortu : Stream
{
    private readonly SafeFileHandle _h;

    // Uchwyt synchroniczny szereguje odczyt z zapisem na poziomie systemu - patrz KolejnoscPortu.
    private readonly KolejnoscPortu _kolejnosc = new();

    public StrumienPortu(SafeFileHandle h) => _h = h;

    public override bool CanRead  => true;
    public override bool CanWrite => true;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();

    /// <summary>
    /// Wyrzuca to, co zalega w buforach portu, i zwraca, ile bajtow bylo do odbioru.
    ///
    /// **To jest zabezpieczenie, nie sprzatanie.** Port jest otwarty przez caly czas
    /// laczenia z ser2netem, a w tym czasie nikt z niego nie czyta - sterownik portu
    /// odklada wiec wszystko, co program kliencki zdazy wpisac. PstRotator powtarza
    /// nastawe co sekunde, dopoki rotor nie stanie na azymucie, wiec kilkanascie sekund
    /// zoltej diody to kilkanascie ramek "obroc sie na X" czekajacych w buforze. W chwili
    /// zestawienia lacza poszlyby wszystkie naraz do sterownika rotora - i antena
    /// zaczelaby sie krecic na polecenie sprzed minuty.
    ///
    /// Dane sprzed zestawienia lacza sa z definicji nieaktualne. Nastawa, ktora nie
    /// dotarla, ma przepasc, a nie dotrzec pozniej.
    /// </summary>
    public int Wyczysc()
    {
        int zalegalo = 0;
        try
        {
            if (PortIo.ClearCommError(_h, out _, out var stan)) zalegalo = (int)stan.cbInQue;
            PortIo.PurgeComm(_h, PortIo.PURGE_RXCLEAR | PortIo.PURGE_TXCLEAR |
                                 PortIo.PURGE_RXABORT | PortIo.PURGE_TXABORT);
        }
        catch { /* port mogl wlasnie zniknac */ }
        return zalegalo;
    }

    public override int Read(byte[] bufor, int offset, int ile) => _kolejnosc.Odczyt(() =>
    {
        var tymczasowy = new byte[ile];
        if (!PortIo.ReadFile(_h, tymczasowy, (uint)ile, out uint przeczytane, IntPtr.Zero))
            throw new IOException("ReadFile: blad " + Marshal.GetLastWin32Error());
        if (przeczytane > 0)
            Buffer.BlockCopy(tymczasowy, 0, bufor, offset, (int)przeczytane);
        return (int)przeczytane;
    });

    /// <summary>
    /// Czy druga strona pary com0com ma port otwarty i podniesione linie sterujace.
    /// Sluzy do wykrycia, ze do pary wpiety jest program kliencki.
    /// </summary>
    public bool DrugaStronaAktywna()
    {
        try
        {
            if (!PortIo.GetCommModemStatus(_h, out uint stan)) return false;
            return (stan & (PortIo.MS_CTS_ON | PortIo.MS_DSR_ON)) != 0;
        }
        catch { return false; }
    }

    /// <summary>Ile razy <c>WriteFile</c> zapisal mniej, niz mial - patrz Write.</summary>
    public int NiepelneZapisy => _niepelne;
    private int _niepelne;

    /// <summary>Ile milisekund zajal sam <c>WriteFile</c>, razem z dopisywaniem reszty.</summary>
    public long OstatniZapisMs { get; private set; }

    /// <summary>
    /// Ile milisekund zapis **czekal na uchwyt**, zanim w ogole ruszyl.
    ///
    /// Bez rozdzielenia tych dwoch liczb wpis o zwloce nie odpowiada na pytanie, ktore ma
    /// znaczenie. W pliku z 16 wrzesnia stoi `98 B w 622 ms` - i dopoki obie fazy byly liczone
    /// razem, nie dalo sie powiedziec, czy to **system nie przyjmuje danych**, czy **my
    /// czekamy w kolejce po uchwyt**. Pierwsze znaczy prace przy `FILE_FLAG_OVERLAPPED`,
    /// drugie - ze `KolejnoscPortu` nadal przegrywa z pompa odczytu. Dwie zupelnie rozne
    /// naprawy, wiec przyrzad musi je rozroznic.
    /// </summary>
    public long OstatnieCzekanieMs { get; private set; }

    /// <summary>Ile bajtow porzucilismy, bo nikt ich po drugiej stronie nie odbieral.</summary>
    public int PorzuconeBajty => _porzucone;
    private int _porzucone;

    /// <summary>
    /// Ile lacznie wolno stac na jednym kawalku, zanim uznamy, ze nikt go nie odbierze.
    ///
    /// **Zgubiony kawalek obrazu jest tanszy niz zamrozony mostek.** Nastepna klatka przychodzi
    /// za okolo 95 ms, wiec strata jest ledwie widoczna; czterosekundowy przestoj widac
    /// natychmiast i zatrzymuje takze ramki statusu.
    /// </summary>
    private static readonly TimeSpan LimitZapisu = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Ile bajtow szlo w ostatnim zapisie. Razem z <see cref="OstatniZapisMs"/> daje **realna
    /// przepustowosc pary** - a ta odpowiada na pytanie, czy zapis czeka na program po drugiej
    /// stronie, czy jest **dlawiony emulacja predkosci transmisji** przez com0com. Przy
    /// `EmuBR=yes` para przepuszcza tyle, ile wynosi ustawiona predkosc: 1200 bodow to okolo
    /// 120 bajtow na sekunde, czyli klatka ekranu SPE (371 B) idzie **trzy sekundy**.
    /// </summary>
    public int OstatniZapisBajtow { get; private set; }

    /// <summary>
    /// Zapis na port **do skutku**, a nie "ile sie uda".
    ///
    /// Port jest otwarty z <c>WriteTotalTimeoutConstant = 2000</c>, wiec gdy bufor pary
    /// com0com sie zapelni - a zapelnia sie, kiedy program po drugiej stronie nie nadaza
    /// czytac - <c>WriteFile</c> **konczy sie sukcesem po dwoch sekundach, zapisujac tylko
    /// czesc bajtow**. Poprzednia wersja ignorowala licznik zapisanych bajtow (<c>out _</c>),
    /// wiec reszta kawalka **przepadala po cichu**. Do klienta szla polowa klatki ekranu
    /// i obraz mrugal.
    ///
    /// Zmierzone 15 wrzesnia przyrzadem "TRZYMALISMY DANE KLIENTA": 398, **1994** i 4543 ms
    /// oczekiwania w kolejce przy pustej kolejce i bez towarzyszacego zastoju procesu.
    /// **1994 ms to nie przypadkowa liczba - to dokladnie limit czasu zapisu**, czyli slad
    /// zapisu, ktory sie o niego oparl.
    ///
    /// Po pieciu sekundach bez postepu rezygnujemy z kawalka i mowimy o tym glosno. Lepiej
    /// stracic jeden kawalek ze sladem niz stac w nieskonczonosc na kliencie, ktory nie czyta.
    /// </summary>
    public override void Write(byte[] bufor, int offset, int ile)
    {
        var zegar = System.Diagnostics.Stopwatch.StartNew();

        // **Port szeregowy nie moze zatrzymac mostka.** Gdy po drugiej stronie pary nikt nie
        // odbiera - a tak jest zawsze przez pierwsze sekundy polaczenia, zanim program kliencki
        // otworzy port - `WriteFile` stoi az do swojego limitu czasu. Prawdziwa linia szeregowa
        // zachowuje sie tak, jak robimy to nizej: bajty wychodza i **gina, jesli nikt ich nie
        // slucha**. Patrz LimitZapisu.
        var czekanie = System.Diagnostics.Stopwatch.StartNew();

        // Pierwszenstwo przed pompa odczytu - patrz KolejnoscPortu. Bez tego zapis
        // siedemdziesieciu bajtow potrafil czekac 1222 ms na wirtualnej parze portow,
        // bo system szereguje operacje na uchwycie synchronicznym, a odczyt wydaje
        // kolejne zadanie zaraz po kazdym powrocie.
        _kolejnosc.Zapis(() =>
        {
            OstatnieCzekanieMs = czekanie.ElapsedMilliseconds;
            zegar.Restart();

            int poszlo = 0;

            while (poszlo < ile)
            {
                var kawalek = new byte[ile - poszlo];
                Buffer.BlockCopy(bufor, offset + poszlo, kawalek, 0, kawalek.Length);

                if (!PortIo.WriteFile(_h, kawalek, (uint)kawalek.Length, out uint teraz, IntPtr.Zero))
                    throw new IOException("WriteFile: blad " + Marshal.GetLastWin32Error());

                if (teraz < kawalek.Length) _niepelne++;
                poszlo += (int)teraz;

                // **Po limicie porzucamy reszte, zamiast dobijac sie dalej.**
                //
                // Pytanie "czy kolejka nadawcza jest dluga" na com0com **nie dziala** -
                // zmierzone 16 wrzesnia: zapis stal 4542 ms, a licznik porzuconych zostal
                // na zerze, bo `cbOutQue` przez caly ten czas byl ponizej progu. Para nie
                // buforuje i nie zglasza zatoru; ona po prostu **wstrzymuje zapis**, dopoki
                // druga strona nie odbierze. Jedyne, co widzimy, to czas - wiec na czasie
                // stawiamy granice.
                if (poszlo < ile && zegar.Elapsed > LimitZapisu)
                {
                    Interlocked.Add(ref _porzucone, ile - poszlo);
                    break;
                }
            }
        });

        OstatniZapisMs = zegar.ElapsedMilliseconds;
        OstatniZapisBajtow = ile;
    }

    // Domyslne ReadAsync i WriteAsync klasy Stream przepuszczaja obie operacje przez
    // **jeden wspolny semafor** na strumien. Pompa odczytu prawie zawsze siedzi
    // w ReadFile, wiec zapis czekal, az tamten skonczy. Zmierzone na zywo z SPE Term:
    // 408 zapisow, mediana 92 ms, srednia 312 ms, najdluzszy 1,5 s - przy szesciu
    // bajtach ramki. Term tego nie wytrzymywal i w kolko ponawial powitanie.
    // Dlatego omijamy tamten semafor i puszczamy operacje wprost do puli watkow.
    public override Task<int> ReadAsync(byte[] bufor, int offset, int ile, CancellationToken ct)
        => Task.Factory.StartNew(() => Read(bufor, offset, ile), ct,
                                 TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

    public override Task WriteAsync(byte[] bufor, int offset, int ile, CancellationToken ct)
        => Task.Factory.StartNew(() => Write(bufor, offset, ile), ct,
                                 TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _h.Dispose();
        base.Dispose(disposing);
    }
}
