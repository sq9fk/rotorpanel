using System.Net.Sockets;

namespace RotorPanel;

public enum StanMostka { Zatrzymany, Laczenie, Polaczony }

/// <summary>
/// Mostek dwukierunkowy: port com0com &lt;-&gt; gniazdo TCP ser2net.
/// Zastepuje hub4com - caly ruch przechodzi przez ten proces.
/// </summary>
public sealed class Mostek : IDisposable
{
    private readonly Config _cfg;
    private CancellationTokenSource _cts;
    private Task _petla;
    private long _rx, _tx;
    private int _stan = (int)StanMostka.Zatrzymany;
    private volatile string _blad = "";

    // Odczyt z gniazda w .NET Framework nie reaguje na token anulowania, wiec zeby
    // rozlaczyc natychmiast, trzeba zamknac same uchwyty.
    private volatile TcpClient _biezacyKlient;
    private volatile Stream _biezacyPort;

    // Do gniazda pisza dwie strony: pompa z portu i odpytywanie wzmacniacza.
    // Bez bramki ich ramki potrafilyby sie przeplesc w polowie.
    private readonly SemaphoreSlim _bramka = new SemaphoreSlim(1, 1);
    private volatile StatusSpe _status;
    private volatile CzytnikSpe _czytnikSpe;
    private System.Collections.Concurrent.ConcurrentQueue<byte[]> _kolejkaPortu;
    private SemaphoreSlim _budzikPortu;
    private bool _trybEkranu;
    private volatile bool _trybStanu;
    private long _ostatniPulsRcu;
    private long _ostatnieZapytanieOStan;

    // Strumien do urzadzenia, zeby okno sterowania mialo gdzie wyslac kod klawisza.
    private volatile Stream _biezacaSiec;

    // Strona pary od nas - z niej odczytujemy, czy ktos siedzi po drugiej stronie.
    private volatile StrumienPortu _portPary;

    // Kiedy ostatnio cokolwiek przyszlo od strony portu. To najpewniejszy dowod,
    // ze klient jest podlaczony i pracuje - linie sterujace potrafia milczec.
    private long _ostatniRuchKlienta;

    // Wynik ostatniego sprawdzenia, czy druga strona pary jest zajeta, i jego czas.
    // Wlasciwosc KlientNaPorcie czytana jest z watku interfejsu (odswiezanie karty)
    // i z petli odpytywania, wiec pola musza byc czytane atomowo, a samo badanie -
    // synchroniczne CreateFile na porcie szeregowym, ktore potrafi zablokowac -
    // idzie do puli watkow. Wczesniej wykonywalo sie wprost na watku interfejsu.
    private volatile bool _klientWykryty;
    private long _kiedyBadanaStrona;
    private long _kiedyBadanyPort;
    private int _badanieWToku;

    public Polaczenie Punkt { get; }
    public string Adres => _cfg.AdresDla(Punkt);
    public StanMostka Stan => (StanMostka)Volatile.Read(ref _stan);
    public long Rx => Interlocked.Read(ref _rx);
    public long Tx => Interlocked.Read(ref _tx);
    public string Blad => _blad;

    /// <summary>Ostatni odczytany stan wzmacniacza SPE albo null.</summary>
    public StatusSpe Status => _status;

    /// <summary>
    /// Czy do drugiej strony pary com0com wpiety jest program kliencki. Wykrywane
    /// dwojako: po liniach sterujacych portu (com0com laczy je miedzy stronami, wiec
    /// otwarcie portu przez klienta podnosi nam CTS/DSR) albo po tym, ze przychodza
    /// ramki statusu, o ktore nie pytalismy. Gdy klient jest, nie wtracamy sie:
    /// wzmacniacz ma jednego pana naraz.
    /// </summary>
    public bool KlientNaPorcie
    {
        get
        {
            if (Stan != StanMostka.Polaczony) return false;

            // Swiezy ruch od strony portu - klient nie tylko jest, ale pracuje.
            long ostatni = Interlocked.Read(ref _ostatniRuchKlienta);
            var odRuchu = ostatni == 0 ? TimeSpan.MaxValue
                                       : DateTime.UtcNow - new DateTime(ostatni);
            if (odRuchu < TimeSpan.FromSeconds(5)) return true;

            if (_czytnikSpe?.KlientPytaSam == true) return true;

            // Reszta to zbuforowany wynik badania w tle. Wlasciwosc jest czytana
            // z watku interfejsu kilka razy na sekunde (karta i okno stanu), a kazde
            // dotkniecie uchwytu portu - nawet samo GetCommModemStatus - potrafi tam
            // czekac na trwajacy ReadFile, bo uchwyt jest synchroniczny. Przy otwartym
            // podgladzie wyswietlacza zbieralo sie z tego tyle zastoju, ze puls RCU
            // (idzie z zegara okna) wypadal za pozno i klatki szly z opoznieniem.
            // Tutaj nie wolno wykonac zadnej operacji na porcie.
            ZaplanujBadanieKlienta(odRuchu);
            return _klientWykryty;
        }
    }

    /// <summary>
    /// Zleca badanie klienta w tle. Linie sterujace sprawdzamy co dwie sekundy,
    /// a otwarcie drugiej strony pary - dopiero po dziesieciu sekundach ciszy i nie
    /// czesciej niz co piec, bo kazda proba na moment zajmuje port i przy pracujacym
    /// kliencie moglibysmy mu go podebrac w chwili, gdy sam go otwiera.
    /// </summary>
    private void ZaplanujBadanieKlienta(TimeSpan odRuchu)
    {
        long ostatnie = Interlocked.Read(ref _kiedyBadanaStrona);
        if (ostatnie != 0 &&
            DateTime.UtcNow - new DateTime(ostatnie) < TimeSpan.FromSeconds(2)) return;

        if (Interlocked.Exchange(ref _badanieWToku, 1) == 1) return;
        Interlocked.Exchange(ref _kiedyBadanaStrona, DateTime.UtcNow.Ticks);

        Task.Run(() =>
        {
            try
            {
                var zegar = System.Diagnostics.Stopwatch.StartNew();

                // Linii CTS/DSR juz nie pytamy. Zmierzone dwojako i oba wyniki byly
                // przeciw: SPE Term ich nie podnosi, wiec niczego nie wykrywaly, a samo
                // GetCommModemStatus na naszym synchronicznym uchwycie potrafilo czekac
                // na trwajacy ReadFile - 71 prob, srednio 387 ms, najdluzsza 2259 ms,
                // co widac bylo jako skoki opoznienia klatek. Zostaje ruch od strony
                // portu (za darmo) i proba otwarcia drugiej strony pary, ktora idzie
                // przez osobny uchwyt i pompie nie przeszkadza.
                bool wynik = false;

                if (odRuchu > TimeSpan.FromSeconds(10) &&
                    !string.IsNullOrWhiteSpace(Punkt.Com))
                {
                    long poprzednie = Interlocked.Read(ref _kiedyBadanyPort);
                    if (poprzednie == 0 ||
                        DateTime.UtcNow - new DateTime(poprzednie) > TimeSpan.FromSeconds(5))
                    {
                        Interlocked.Exchange(ref _kiedyBadanyPort, DateTime.UtcNow.Ticks);
                        wynik = PortIo.Zajety(Punkt.Com);
                    }
                    else wynik = _klientWykryty;
                }

                _klientWykryty = wynik;

                if (Slad.Wlaczony && zegar.ElapsedMilliseconds > 5)
                    Slad.Zapisz("badanie klienta trwalo " + zegar.ElapsedMilliseconds + " ms");
            }
            catch { /* port zniknal - przy nastepnym badaniu sie wyjasni */ }
            finally { Interlocked.Exchange(ref _badanieWToku, 0); }
        });
    }

    /// <summary>Ostatnia zawartosc wyswietlacza albo null.</summary>
    public EkranSpe Ekran => _czytnikSpe?.Ekran;

    /// <summary>
    /// Czy sami wlaczylismy tryb RCU i mamy zdejmowac ramki wyswietlacza.
    /// Gdy nie - przechodza do klienta, bo to on o nie poprosil.
    /// </summary>
    public bool TrybEkranu
    {
        get => _trybEkranu;
        set
        {
            _trybEkranu = value;
            var czytnik = _czytnikSpe;
            if (czytnik is not null) czytnik.PrzechwytujEkran = value;
        }
    }

    /// <summary>
    /// Czy ktos patrzy na okno stanu. Wtedy ramka statusu jest tym, co uzytkownik
    /// naprawde oglada, wiec odpytujemy normalnie - takze przy wlaczonym podgladzie
    /// wyswietlacza. Bez tego okno stanu po piciu sekundach meldowalo, ze wzmacniacz
    /// nie odpowiada, choc to my przestawalismy pytac.
    /// </summary>
    public bool TrybStanu
    {
        get => _trybStanu;
        set => _trybStanu = value;
    }

    /// <summary>
    /// Okno sterowania melduje tu kazdy puls RCU. Odpytywanie o stan omija okno miedzy
    /// pulsem a klatka - zmierzone: zapytanie wciskajace sie w te przerwe opoznia
    /// klatke o ponad sekunde. Poza tym oknem w cyklu jest cisza.
    /// </summary>
    public void ZglosPulsRcu() => Interlocked.Exchange(ref _ostatniPulsRcu, DateTime.UtcNow.Ticks);

    private bool CzekamNaKlatke
    {
        get
        {
            long puls = Interlocked.Read(ref _ostatniPulsRcu);
            return puls != 0 &&
                   DateTime.UtcNow - new DateTime(puls) < TimeSpan.FromMilliseconds(900);
        }
    }

    /// <summary>
    /// Czy wlasnie czekamy na odpowiedz o stan. Zabezpieczenie musi byc **obustronne**:
    /// stan omijal okno miedzy pulsem a klatka, ale puls nie omijal zapytania o stan
    /// i trafial w moment, gdy wzmacniacz odpowiadal - wtedy przepadal, a klatka
    /// przychodzila dopiero po zapasowym takcie. Zmierzone przed ta poprawka: mediana
    /// odstepu miedzy klatkami 2,72 s przy poprawnym opoznieniu pojedynczej klatki 559 ms.
    /// </summary>
    public bool CzekamNaStan
    {
        get
        {
            long kiedy = Interlocked.Read(ref _ostatnieZapytanieOStan);
            return kiedy != 0 &&
                   DateTime.UtcNow - new DateTime(kiedy) < TimeSpan.FromMilliseconds(400);
        }
    }

    private bool OdpytywacSpe => Punkt is Urzadzenie { Spe: true };

    public Mostek(Config cfg, Polaczenie punkt)
    {
        _cfg = cfg;
        Punkt = punkt;
    }

    public void Start()
    {
        if (!Punkt.Gotowy) return;
        if (_petla is { IsCompleted: false }) return;
        Interlocked.Exchange(ref _rx, 0);
        Interlocked.Exchange(ref _tx, 0);
        _blad = "";
        _status = null;
        _cts = new CancellationTokenSource();
        Volatile.Write(ref _stan, (int)StanMostka.Laczenie);
        _petla = Task.Run(() => Petla(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* nic */ }

        // Zamkniecie uchwytow przerywa zawieszone odczyty - bez tego kazde
        // zatrzymanie czekaloby caly limit czasu, a polaczenie TCP trwaloby
        // az do konca procesu.
        try { _biezacyKlient?.Close(); } catch { /* juz zamkniete */ }
        try { _biezacyPort?.Dispose(); } catch { /* jak wyzej */ }

        try { _petla?.Wait(2500); } catch { /* nic */ }
        Volatile.Write(ref _stan, (int)StanMostka.Zatrzymany);
        _status = null;
    }

    private async Task Petla(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream port = null;
            TcpClient klient = null;
            Stream siec = null;

            try
            {
                Volatile.Write(ref _stan, (int)StanMostka.Laczenie);

                port = PortIo.Otworz(Punkt.Dev);
                _biezacyPort = port;
                _portPary = port as StrumienPortu;

                klient = new TcpClient();
                _biezacyKlient = klient;
                await PolaczAsync(klient, Adres, Punkt.Port, ct);

                siec = klient.GetStream();

                // Serwer RFC 2217 mowi Telnetem - bez rozpakowania sekwencji IAC
                // trafialyby one do danych.
                if (Punkt.Protokol == Protokol.Rfc2217)
                {
                    var telnet = new StrumienTelnet(siec);
                    await telnet.Przywitaj(ct);
                    await telnet.UstawParametry(Punkt, ct);
                    siec = telnet;
                }

                _biezacaSiec = siec;
                _blad = "";
                Volatile.Write(ref _stan, (int)StanMostka.Polaczony);

                var czytnik = OdpytywacSpe ? new CzytnikSpe { PrzechwytujEkran = _trybEkranu } : null;
                _czytnikSpe = czytnik;

                _kolejkaPortu = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
                _budzikPortu = new SemaphoreSlim(0);
                var pisarz = PisarzPortu(port, ct);

                var wGore = Pompa(port, siec, zPortu: true,  null, ct);
                var wDol  = Pompa(siec, port, zPortu: false, czytnik, ct);
                var pytania = czytnik is null ? Task.Delay(Timeout.Infinite, ct)
                                              : OdpytujSpe(siec, czytnik, ct);

                await Task.WhenAny(wGore, wDol, pytania, pisarz);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { _blad = ex.Message; }
            finally
            {
                _biezacyKlient = null;
                _biezacyPort = null;
                _biezacaSiec = null;
                _czytnikSpe = null;
                _portPary = null;

                try { siec?.Dispose(); } catch { }
                try { klient?.Close(); } catch { }
                try { port?.Dispose(); } catch { }
            }

            if (ct.IsCancellationRequested) break;

            Volatile.Write(ref _stan, (int)StanMostka.Laczenie);
            try { await Task.Delay(2000, ct); }
            catch (OperationCanceledException) { break; }
        }

        Volatile.Write(ref _stan, (int)StanMostka.Zatrzymany);
    }

    /// <summary>
    /// .NET Framework nie ma ConnectAsync z tokenem anulowania, wiec skladamy to
    /// z wyscigu miedzy laczeniem a zadaniem zatrzymania.
    /// </summary>
    private static async Task PolaczAsync(TcpClient klient, string adres, int port, CancellationToken ct)
    {
        var laczenie = klient.ConnectAsync(adres, port);
        var przerwanie = Task.Delay(Timeout.Infinite, ct);

        if (await Task.WhenAny(laczenie, przerwanie) == przerwanie)
        {
            klient.Close();
            throw new OperationCanceledException(ct);
        }

        await laczenie;   // przenosi ewentualny wyjatek polaczenia
    }

    private async Task Pompa(Stream skad, Stream dokad, bool zPortu, CzytnikSpe czytnik,
                             CancellationToken ct)
    {
        var bufor = new byte[1024];
        while (!ct.IsCancellationRequested)
        {
            int n = await skad.ReadAsync(bufor, 0, bufor.Length, ct);

            if (n <= 0)
            {
                // Z portu zero oznacza cisze na linii, z sieci - zerwane polaczenie.
                if (!zPortu) return;
                continue;
            }

            if (zPortu) Interlocked.Exchange(ref _ostatniRuchKlienta, DateTime.UtcNow.Ticks);

            if (czytnik is null)
            {
                if (zPortu) Interlocked.Add(ref _tx, n);
                else        Interlocked.Add(ref _rx, n);
            }

            if (Slad.Wlaczony)
                Slad.Zapisz((zPortu ? "port->siec " : "siec->port ") + n + " B: " +
                            Slad.Podglad(bufor, n));

            if (czytnik is not null)
            {
                // Odpowiedzi na wlasne zapytania o status zdejmujemy ze strumienia -
                // program po drugiej stronie pary o nie nie prosil. Liczniki pokazuja
                // ruch klienta, wiec nasze wlasne odpytywanie do nich nie wchodzi.
                var dalej = czytnik.Przepusc(bufor, n);
                _status = czytnik.Status ?? _status;
                Interlocked.Add(ref _rx, dalej.Length);

                if (Slad.Wlaczony)
                    Slad.Zapisz("  czytnik przepuscil " + dalej.Length + " z " + n +
                                " B, wlasnych zapytan w toku " + czytnik.WlasneOczekujace +
                                ", klient pyta sam: " + czytnik.KlientPytaSam);

                if (dalej.Length == 0) continue;

                Oddaj(dokad, dalej, ct);
                continue;
            }

            if (zPortu)
            {
                await _bramka.WaitAsync(ct);
                try
                {
                    await dokad.WriteAsync(bufor, 0, n, ct);
                    await dokad.FlushAsync(ct);
                }
                finally { _bramka.Release(); }
            }
            else
            {
                var kopia = new byte[n];
                Array.Copy(bufor, kopia, n);
                Oddaj(dokad, kopia, ct);
            }
        }
    }

    /// <summary>
    /// Wklada dane do kolejki zapisu na port. Zapis do pary com0com potrafi stanac
    /// na sekundy, gdy po drugiej stronie nikt nie czyta - zmierzone 2,77 s na szesc
    /// bajtow. Robiony wprost w pompie zatrzymywal odbior z sieci, wiec klatki ekranu
    /// czekaly w buforze. Kolejka trzyma odbior wolnym; gdy sie przepelni, znaczy to,
    /// ze odbiorcy nie ma, i najstarsze dane odpadaja.
    /// </summary>
    private void Oddaj(Stream port, byte[] dane, CancellationToken ct)
    {
        if (_kolejkaPortu is null) return;
        if (_kolejkaPortu.Count > 256) _kolejkaPortu.TryDequeue(out _);
        _kolejkaPortu.Enqueue(dane);
        _budzikPortu.Release();

        if (Slad.Wlaczony)
            Slad.Zapisz("  do kolejki portu " + dane.Length + " B, w kolejce " +
                        _kolejkaPortu.Count);
    }

    private async Task PisarzPortu(Stream port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _budzikPortu.WaitAsync(ct);
            if (!_kolejkaPortu.TryDequeue(out var dane)) continue;

            try
            {
                var zegar = System.Diagnostics.Stopwatch.StartNew();
                await port.WriteAsync(dane, 0, dane.Length, ct);
                await port.FlushAsync(ct);
                if (Slad.Wlaczony)
                    Slad.Zapisz("  zapisano na port " + dane.Length + " B w " +
                                zegar.ElapsedMilliseconds + " ms");
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                Slad.Zapisz("  BLAD zapisu na port: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Wysyla jedno zapytanie o stan i melduje czytnikowi, ze odpowiedz nalezy do nas
    /// (inaczej poszlaby do klienta na drugiej stronie pary). Sluzy oknu sterowania:
    /// przy wlaczonym podgladzie petla OdpytujSpe milczy, wiec ktos musi zapytac
    /// w spokojnej chwili cyklu, tuz po odebranej klatce.
    /// </summary>
    public async Task<bool> ZapytajOStan(CancellationToken ct)
    {
        var czytnik = _czytnikSpe;
        bool poszlo = await WyslijKlawisz(StatusSpe.Zapytanie, ct);
        if (poszlo) czytnik?.ZglosWlasneZapytanie();
        return poszlo;
    }

    /// <summary>
    /// Wysyla wzmacniaczowi kod klawisza. Zwraca false, gdy mostek nie jest polaczony
    /// albo pisanie sie nie udalo - wtedy okno sterowania ma o czym powiedziec.
    /// </summary>
    public async Task<bool> WyslijKlawisz(byte kod, CancellationToken ct)
    {
        var siec = _biezacaSiec;
        if (siec is null || Stan != StanMostka.Polaczony) return false;

        var ramka = new byte[] { 0x55, 0x55, 0x55, 0x01, kod, kod };

        try
        {
            // ConfigureAwait(false) nie jest tu kosmetyka. Metode wola okno sterowania
            // z watku interfejsu, a bez tego kontynuacje wracaja na ten watek - i gdy
            // ktos po drugiej stronie na nie zaczeka, program staje.
            await _bramka.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await siec.WriteAsync(ramka, 0, ramka.Length, ct).ConfigureAwait(false);
                await siec.FlushAsync(ct).ConfigureAwait(false);
            }
            finally { _bramka.Release(); }

            return true;
        }
        catch (Exception ex)
        {
            _blad = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Pyta wzmacniacz SPE o status raz na sekunde. Ramka: 55 55 55, jeden bajt
    /// dlugosci, kod polecenia i suma kontrolna rowna temu bajtowi.
    /// </summary>
    private async Task OdpytujSpe(Stream siec, CzytnikSpe czytnik, CancellationToken ct)
    {
        var zapytanie = new byte[]
            { 0x55, 0x55, 0x55, 0x01, StatusSpe.Zapytanie, StatusSpe.Zapytanie };

        while (!ct.IsCancellationRequested)
        {
            // Gdy do pary wpiety jest program kliencki (SPE Term, AetherSDR), milkniemy
            // zupelnie - on jest wtedy panem lacza. Wykrywamy go po ruchu od strony
            // portu: linie CTS/DSR potrafia milczec (zmierzone - Term ich nie podnosi),
            // a wlasnych ramek statusu klient wcale nie musi zamawiac. Stan czytamy
            // z jego ramek po drodze, a wzmacniacz nie dostaje podwojnego ruchu.
            if (KlientNaPorcie)
            {
                await Task.Delay(500, ct);
                continue;
            }

            // Przy wlaczonym podgladzie pytamy tylko wtedy, gdy ktos patrzy na okno
            // stanu - samo okno sterowania pokazuje stan na ekranie wzmacniacza.
            if (_trybEkranu && !_trybStanu)
            {
                await Task.Delay(500, ct);
                continue;
            }

            // Stan chodzi **wlasnym taktem**, niezaleznie od klatek. Probowalem
            // doczepic go do klatki (zapytanie zaraz po niej) i wyszlo zle: wzmacniacz
            // co jakis czas milknie na kilka sekund, a wtedy razem z klatka ginal stan.
            // Zmierzone: odstepy miedzy klatkami od 1,25 s do 11 s, wiec odczyt stanu
            // starzal sie do siedmiu sekund. Jedyne ograniczenie to nie wchodzic miedzy
            // puls a klatke - poza tym oknem zapytanie nikomu nie przeszkadza.
            if (_trybEkranu && CzekamNaKlatke)
            {
                await Task.Delay(100, ct);
                continue;
            }

            Slad.Zapisz("wysylam wlasne zapytanie 0x90");

            await _bramka.WaitAsync(ct);
            try
            {
                await siec.WriteAsync(zapytanie, 0, zapytanie.Length, ct);
                await siec.FlushAsync(ct);
                czytnik.ZglosWlasneZapytanie();
                Interlocked.Exchange(ref _ostatnieZapytanieOStan, DateTime.UtcNow.Ticks);
            }
            finally { _bramka.Release(); }


            await Task.Delay(1000, ct);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _bramka.Dispose();
    }
}
