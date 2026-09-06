namespace RotorPanel;

/// <summary>
/// Warstwa Telnet nad strumieniem sieciowym, uzywana przy serwerach RFC 2217.
///
/// Taki serwer nie przesyla surowych bajtow: bajt 255 jest znacznikiem polecenia,
/// a w danych wystepuje podwojony. Do tego przy nawiazaniu polaczenia obie strony
/// negocjuja opcje. Bez rozpakowania tych sekwencji trafialyby one do danych
/// i psuly transmisje.
///
/// Nie wysylamy polecen ustawiajacych parametry portu - predkosc i format ramki
/// konfiguruje sie po stronie urzadzenia.
/// </summary>
public sealed class StrumienTelnet : Stream
{
    private const byte IAC  = 255;
    private const byte DONT = 254;
    private const byte DO   = 253;
    private const byte WONT = 252;
    private const byte WILL = 251;
    private const byte SB   = 250;
    private const byte SE   = 240;

    private const byte OpcjaBinarna = 0;

    private enum Stan { Dane, PoIac, Negocjacja, Podnegocjacja, PodnegocjacjaIac }

    private readonly Stream _siec;
    private readonly byte[] _surowy = new byte[2048];
    private readonly Queue<byte> _dane = new Queue<byte>();
    private readonly List<byte> _odpowiedzi = new List<byte>();

    private Stan _stan = Stan.Dane;
    private byte _polecenie;
    private bool _koniec;

    public StrumienTelnet(Stream siec) => _siec = siec;

    /// <summary>Zglasza gotowosc do trybu binarnego - bez niego serwer moze filtrowac bajty.</summary>
    public async Task Przywitaj(CancellationToken ct)
    {
        var powitanie = new byte[] { IAC, WILL, OpcjaBinarna, IAC, DO, OpcjaBinarna };
        await _siec.WriteAsync(powitanie, 0, powitanie.Length, ct);
        await _siec.FlushAsync(ct);
    }

    public override async Task<int> ReadAsync(byte[] bufor, int offset, int ile, CancellationToken ct)
    {
        while (true)
        {
            int gotowe = Wydaj(bufor, offset, ile);
            if (gotowe > 0) return gotowe;
            if (_koniec) return 0;

            int n = await _siec.ReadAsync(_surowy, 0, _surowy.Length, ct);
            if (n <= 0) { _koniec = true; continue; }

            Rozpakuj(n);
            await OdeslijOdpowiedzi(ct);
        }
    }

    public override async Task WriteAsync(byte[] bufor, int offset, int ile, CancellationToken ct)
    {
        // W danych bajt 255 musi byc podwojony, inaczej serwer wezmie go za polecenie.
        var wyjscie = new List<byte>(ile + 8);
        for (int i = 0; i < ile; i++)
        {
            byte b = bufor[offset + i];
            wyjscie.Add(b);
            if (b == IAC) wyjscie.Add(IAC);
        }

        var tablica = wyjscie.ToArray();
        await _siec.WriteAsync(tablica, 0, tablica.Length, ct);
    }

    private int Wydaj(byte[] bufor, int offset, int ile)
    {
        int n = 0;
        while (n < ile && _dane.Count > 0) bufor[offset + n++] = _dane.Dequeue();
        return n;
    }

    private void Rozpakuj(int ile)
    {
        for (int i = 0; i < ile; i++)
        {
            byte b = _surowy[i];

            switch (_stan)
            {
                case Stan.Dane:
                    if (b == IAC) _stan = Stan.PoIac;
                    else _dane.Enqueue(b);
                    break;

                case Stan.PoIac:
                    if (b == IAC) { _dane.Enqueue(IAC); _stan = Stan.Dane; }   // podwojony bajt danych
                    else if (b == SB) _stan = Stan.Podnegocjacja;
                    else if (b == DO || b == DONT || b == WILL || b == WONT)
                    {
                        _polecenie = b;
                        _stan = Stan.Negocjacja;
                    }
                    else _stan = Stan.Dane;                                    // polecenie dwubajtowe
                    break;

                case Stan.Negocjacja:
                    Odpowiedz(_polecenie, b);
                    _stan = Stan.Dane;
                    break;

                case Stan.Podnegocjacja:
                    if (b == IAC) _stan = Stan.PodnegocjacjaIac;
                    break;

                case Stan.PodnegocjacjaIac:
                    _stan = b == SE ? Stan.Dane : Stan.Podnegocjacja;
                    break;
            }
        }
    }

    /// <summary>Zgadzamy sie tylko na tryb binarny, reszte opcji odrzucamy.</summary>
    private void Odpowiedz(byte polecenie, byte opcja)
    {
        byte odpowiedz;

        if (polecenie == DO)        odpowiedz = opcja == OpcjaBinarna ? WILL : WONT;
        else if (polecenie == WILL) odpowiedz = opcja == OpcjaBinarna ? DO   : DONT;
        else                        return;   // DONT i WONT nie wymagaja odpowiedzi

        _odpowiedzi.Add(IAC);
        _odpowiedzi.Add(odpowiedz);
        _odpowiedzi.Add(opcja);
    }

    private async Task OdeslijOdpowiedzi(CancellationToken ct)
    {
        if (_odpowiedzi.Count == 0) return;

        var tablica = _odpowiedzi.ToArray();
        _odpowiedzi.Clear();
        await _siec.WriteAsync(tablica, 0, tablica.Length, ct);
        await _siec.FlushAsync(ct);
    }

    // ---- reszta kontraktu strumienia ----

    public override bool CanRead  => true;
    public override bool CanWrite => true;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _siec.Flush();
    public override Task FlushAsync(CancellationToken ct) => _siec.FlushAsync(ct);
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();

    public override int Read(byte[] b, int o, int i) => ReadAsync(b, o, i, CancellationToken.None).Result;
    public override void Write(byte[] b, int o, int i) => WriteAsync(b, o, i, CancellationToken.None).Wait();
}
