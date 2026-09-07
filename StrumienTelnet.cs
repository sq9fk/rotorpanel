namespace RotorPanel;

/// <summary>
/// Warstwa Telnet nad strumieniem sieciowym, uzywana przy serwerach RFC 2217.
///
/// Taki serwer nie przesyla surowych bajtow: bajt 255 jest znacznikiem polecenia,
/// a w danych wystepuje podwojony. Do tego przy nawiazaniu polaczenia obie strony
/// negocjuja opcje. Bez rozpakowania tych sekwencji trafialyby one do danych
/// i psuly transmisje.
///
/// Po nawiazaniu polaczenia wysylamy parametry portu: predkosc, format ramki oraz
/// stan linii DTR i RTS. Urzadzenie trzyma swoj port zamkniety, dopoki ich nie pozna.
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
    private const byte OpcjaPortu   = 44;   // COM-PORT-OPTION z RFC 2217

    private const byte PolecPredkosc   = 1;
    private const byte PolecBityDanych = 2;
    private const byte PolecParzystosc = 3;
    private const byte PolecBityStopu  = 4;
    private const byte PolecSterowanie = 5;

    private const byte BezPrzeplywu = 1;    // SET-CONTROL: brak sterowania przeplywem
    private const byte DtrWlacz     = 8;

    // 11 to w RFC 2217 **wylaczenie** RTS - nazwa "RtsWlacz" klamala, choc wartosc
    // byla wlasciwa. I dobrze, bo na tej linii wisi wlacznik zasilania wzmacniacza:
    // AetherSDR ma to sprawdzone na stole (design note SS9) - DTR wysoko, RTS nisko
    // w spoczynku, a wlaczanie jedzie impulsem na samym RTS. Trzymanie RTS wysoko
    // wystawia na wzmacniaczu ostrzezenie "Power switch held by remote".
    private const byte RtsWylacz    = 11;

    private enum Stan { Dane, PoIac, Negocjacja, Podnegocjacja, PodnegocjacjaIac }

    private readonly Stream _siec;
    private readonly byte[] _surowy = new byte[2048];
    private readonly Queue<byte> _dane = new Queue<byte>();
    private readonly List<byte> _odpowiedzi = new List<byte>();

    private Stan _stan = Stan.Dane;
    private byte _polecenie;
    private bool _koniec;

    public StrumienTelnet(Stream siec) => _siec = siec;

    /// <summary>
    /// Zglasza tryb binarny oraz gotowosc do sterowania portem. Bez tego drugiego
    /// czesc urzadzen w ogole nie otwiera swojego portu szeregowego.
    /// </summary>
    public async Task Przywitaj(CancellationToken ct)
    {
        var powitanie = new byte[]
        {
            IAC, WILL, OpcjaBinarna,
            IAC, DO,   OpcjaBinarna,
            IAC, WILL, OpcjaPortu
        };
        await _siec.WriteAsync(powitanie, 0, powitanie.Length, ct);
        await _siec.FlushAsync(ct);
    }

    /// <summary>
    /// Przekazuje parametry transmisji. Urzadzenie otwiera swoj port szeregowy dopiero
    /// po otrzymaniu predkosci - dopoki jej nie zna, zglasza zero i nie przepuszcza danych.
    /// </summary>
    public async Task UstawParametry(Polaczenie p, CancellationToken ct)
    {
        if (p.Predkosc <= 0) return;

        var polecenia = new List<byte>();

        polecenia.AddRange(Podnegocjacja(PolecPredkosc,
            (byte)(p.Predkosc >> 24), (byte)(p.Predkosc >> 16),
            (byte)(p.Predkosc >> 8),  (byte)p.Predkosc));

        polecenia.AddRange(Podnegocjacja(PolecBityDanych, (byte)p.BityDanych));
        polecenia.AddRange(Podnegocjacja(PolecParzystosc, NaKodParzystosci(p.Parzystosc)));
        polecenia.AddRange(Podnegocjacja(PolecBityStopu,  NaKodBitowStopu(p.BityStopu)));

        // Windows otwiera port szeregowy z podniesionym DTR i RTS, a czesc urzadzen
        // (m.in. SPE Expert) bez nich nie odzywa sie ani slowem. Serwer RFC 2217 trzyma
        // te linie opuszczone, dopoki klient ich nie podniesie - stad te trzy polecenia.
        polecenia.AddRange(Podnegocjacja(PolecSterowanie, BezPrzeplywu));
        polecenia.AddRange(Podnegocjacja(PolecSterowanie, DtrWlacz));
        polecenia.AddRange(Podnegocjacja(PolecSterowanie, RtsWylacz));

        var tablica = polecenia.ToArray();
        await _siec.WriteAsync(tablica, 0, tablica.Length, ct);
        await _siec.FlushAsync(ct);
    }

    /// <summary>RFC 2217: 1 brak, 2 parzysta, 3 nieparzysta, 4 znacznik, 5 spacja.</summary>
    private static byte NaKodParzystosci(Parzystosc p)
    {
        switch (p)
        {
            case Parzystosc.Parzysta:    return 2;
            case Parzystosc.Nieparzysta: return 3;
            case Parzystosc.Znacznik:    return 4;
            case Parzystosc.Spacja:      return 5;
            default:                     return 1;
        }
    }

    /// <summary>RFC 2217: 1 jeden bit, 2 dwa bity, 3 poltora.</summary>
    private static byte NaKodBitowStopu(BityStopu b)
    {
        switch (b)
        {
            case BityStopu.Dwa:      return 2;
            case BityStopu.Poltora:  return 3;
            default:                 return 1;
        }
    }

    private static IEnumerable<byte> Podnegocjacja(byte polecenie, params byte[] wartosci)
    {
        var wynik = new List<byte> { IAC, SB, OpcjaPortu, polecenie };
        foreach (var w in wartosci)
        {
            wynik.Add(w);
            if (w == IAC) wynik.Add(IAC);   // takze w podnegocjacji bajt 255 sie podwaja
        }
        wynik.Add(IAC);
        wynik.Add(SE);
        return wynik;
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

    /// <summary>Zgadzamy sie na tryb binarny i sterowanie portem, reszte odrzucamy.</summary>
    private void Odpowiedz(byte polecenie, byte opcja)
    {
        byte odpowiedz;

        bool obslugiwana = opcja == OpcjaBinarna || opcja == OpcjaPortu;

        if (polecenie == DO)        odpowiedz = obslugiwana ? WILL : WONT;
        else if (polecenie == WILL) odpowiedz = obslugiwana ? DO   : DONT;
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
