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
            ReadTotalTimeoutConstant   = 200,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant   = 2000
        };
        if (!SetCommTimeouts(uchwyt, ref limity))
        {
            uchwyt.Dispose();
            throw new IOException(nazwa + ": SetCommTimeouts, blad " + Marshal.GetLastWin32Error());
        }
        return new StrumienPortu(uchwyt);
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

    public override void Write(byte[] bufor, int offset, int ile)
    {
        var tymczasowy = new byte[ile];
        Buffer.BlockCopy(bufor, offset, tymczasowy, 0, ile);
        if (!PortIo.WriteFile(_h, tymczasowy, (uint)ile, out _, IntPtr.Zero))
            throw new IOException("WriteFile: blad " + Marshal.GetLastWin32Error());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _h.Dispose();
        base.Dispose(disposing);
    }
}
