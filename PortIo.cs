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
    private static extern bool EscapeCommFunction(SafeFileHandle h, uint funkcja);

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
            WriteTotalTimeoutConstant   = 2000
        };
        if (!SetCommTimeouts(uchwyt, ref limity))
        {
            uchwyt.Dispose();
            throw new IOException(nazwa + ": SetCommTimeouts, blad " + Marshal.GetLastWin32Error());
        }
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

    public StrumienPortu(SafeFileHandle h) => _h = h;

    public override bool CanRead  => true;
    public override bool CanWrite => true;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();

    public override int Read(byte[] bufor, int offset, int ile)
    {
        var tymczasowy = new byte[ile];
        if (!PortIo.ReadFile(_h, tymczasowy, (uint)ile, out uint przeczytane, IntPtr.Zero))
            throw new IOException("ReadFile: blad " + Marshal.GetLastWin32Error());
        if (przeczytane > 0)
            Buffer.BlockCopy(tymczasowy, 0, bufor, offset, (int)przeczytane);
        return (int)przeczytane;
    }

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

    public override void Write(byte[] bufor, int offset, int ile)
    {
        var tymczasowy = new byte[ile];
        Buffer.BlockCopy(bufor, offset, tymczasowy, 0, ile);
        if (!PortIo.WriteFile(_h, tymczasowy, (uint)ile, out _, IntPtr.Zero))
            throw new IOException("WriteFile: blad " + Marshal.GetLastWin32Error());
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
