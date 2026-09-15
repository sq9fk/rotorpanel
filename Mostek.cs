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

    /// <summary>Ile razy z rzedu polaczenie padlo od razu - do stopniowania zwloki.</summary>
    private int _nieudanePodejscia;

    // Czyste zamkniecie przez druga strone znaczy, ze ser2net oddal port komus innemu
    // (kickolduser). Pojedyncze zdarza sie przy restarcie ser2neta; powtarzajace sie
    // znaczy, ze ktos walczy z nami o port - i to warto pokazac, bo inaczej objawia sie
    // to tylko jako program sterujacy bez odpowiedzi.
    private int _obcePrzejecia;
    private long _pierwszePrzejecie;

    // Ostatnie bajty w obie strony - kontekst dla pulapki na podejrzane rozkazy.
    // Sama ramka nie wystarcza: trzeba widziec, co ja poprzedzalo.
    private readonly Pulapka.Bufor _doSterownika = new(256);
    private readonly Pulapka.Bufor _odSterownika = new(256);
    private readonly Pulapka.Wykrywacz _rozkazy = new();
    private readonly Pulapka.Dziennik _dziennik = new();
    private long _ostatniZrzut;

    // Odpowiedzi sterownika skladamy w cale ramki, zanim trafia na port - patrz SkladaczSpid.
    private readonly SkladaczSpid _skladacz = new();

    // Ten sam skladacz w druga strone: rozkazy do sterownika tez maja wychodzic
    // calymi ramkami. Patrz SkladaczRozkazow w Pompa.
    private readonly SkladaczSpid _skladaczRozkazow = new();

    /// <summary>
    /// Pilnuje **kolejnosci** wkladania do kolejki portu. Do skladacza sa dwa wejscia: pompa
    /// z sieci i zegar dopychajacy zalegly ogon. Samo skladanie jest zamkniete, ale odcinek
    /// "wyjmij ramki" - "wloz do kolejki" juz nie byl: dopychacz mogl wejsc w te szczeline
    /// i wrzucic ogon **przed** ramka, ktora pompa dopiero wkladala. Bajty dotarlyby wtedy
    /// do klienta w zlej kolejnosci - czyli dokladnie to, przed czym ma chronic skladanie ramek.
    /// </summary>
    private readonly object _kolejnosc = new();

    // Ostatnia wiarygodna pozycja, czas jej przyjecia i licznik odrzucen z rzedu.
    // Patrz ZanotujZapytanie. Zapytania wyslane, na ktore nie ma jeszcze odpowiedzi.
    private readonly Queue<long> _wymiany = new();
    private int _brakow, _spoznionych, _ileWymian, _zapytan, _odpowiedzi, _statusowPoprzednio;
    private double _sumaMs, _minMs = double.MaxValue, _maxMs;
    private long _ostatniZrzutBraku;

    // Patrz OdrzucicNieprawdopodobnyOdczyt.
    private int _ostatniaPozycja = -1;
    private DateTime _kiedyPozycja = DateTime.MinValue;
    private int _odrzuconeZRzedu;
    private System.Diagnostics.Stopwatch _odPolaczenia = System.Diagnostics.Stopwatch.StartNew();

    public Polaczenie Punkt { get; }
    public string Adres => _cfg.AdresDla(Punkt);
    public StanMostka Stan => (StanMostka)Volatile.Read(ref _stan);
    public long Rx => Interlocked.Read(ref _rx);
    public long Tx => Interlocked.Read(ref _tx);
    public string Blad => _blad;

    /// <summary>
    /// Ile razy druga strona zamknela nam polaczenie czysto, bez bledu, w ostatnich
    /// minutach. Rosnie tylko wtedy, gdy zdarza sie to wielokrotnie - jednorazowe
    /// zamkniecie (restart ser2neta) nie jest jeszcze podejrzane.
    /// </summary>
    public int ObcePrzejecia => Volatile.Read(ref _obcePrzejecia) >= 3
        ? Volatile.Read(ref _obcePrzejecia) : 0;

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
                    Zapisz("badanie klienta trwalo " + zegar.ElapsedMilliseconds + " ms");
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

    /// <summary>
    /// Podpis mostka w sladzie. Przy dwoch pracujacych mostkach linie sladu mieszaja sie
    /// ze soba i bez tego nie sposob powiedziec, do ktorego portu poszedl ktory bajt -
    /// a to jest pierwsze pytanie przy podejrzeniu, ze ruch przecieka miedzy mostkami.
    /// </summary>
    private string Podpis => Punkt.Etykieta + " [" + Punkt.Dev + " " + (char)0x2192 + " " +
                             Adres + ":" + Punkt.Port + "] ";

    private void Zapisz(string tekst) => Slad.Zapisz(Podpis + tekst);

    /// <summary>Slad porcji danych; dla rotorow z rozebrana ramka SPID.</summary>
    private void ZapiszRamke(string kierunek, byte[] dane, int ile)
    {
        string opis = OdpytywacSpe ? "" : "   " + SladSpid.Opis(dane, ile);
        Slad.Zapisz(Podpis + kierunek + " " + ile + " B: " + Slad.Podglad(dane, ile) + opis);
    }

    public Mostek(Config cfg, Polaczenie punkt)
    {
        _cfg = cfg;
        Punkt = punkt;

        // Tylko kierunek od sterownika. Urwana odpowiedz to polowa liczby i lepiej jej
        // nie oddawac wcale; urwany rozkaz to niewykonana nastawa i tego gubic nie wolno.
        _skladacz.KasujUrwaneOdpowiedzi = punkt is Rotor;
        _skladacz.Odrzucono = ogon => Pulapka.Zapisz(Podpis,
            "SKASOWANY URWANY POCZATEK ODPOWIEDZI: " + Slad.Podglad(ogon, ogon.Length) +
            " - nie doczekal sie reszty. " +
            "Polowa ramki pozycji czytana przez program sterujacy daje 208 stopni, " +
            "wiec lepiej, zeby nie dostal nic. Od zestawienia lacza: " + BilansWymian +
            Environment.NewLine + "    " + CzujnikZastoju.Opis,
            _doSterownika, _odSterownika, _odPolaczenia.Elapsed, _dziennik);
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
            var zegarPolaczenia = System.Diagnostics.Stopwatch.StartNew();
            _odPolaczenia = zegarPolaczenia;

            try
            {
                Volatile.Write(ref _stan, (int)StanMostka.Laczenie);

                port = PortIo.Otworz(Punkt.Dev);
                _biezacyPort = port;
                _portPary = port as StrumienPortu;

                klient = new TcpClient();
                _biezacyKlient = klient;
                await PolaczAsync(klient, Adres, Punkt.Port, ct);
                WlaczKeepAlive(klient);

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

                Pulapka.Uzbrojono(Podpis);

                // Ogon ramki z poprzedniego polaczenia skleilby sie z pierwszymi bajtami
                // tego - i powstalaby ramka, ktorej nikt nie wyslal. Patrz SkladaczSpid.Wyczysc.
                foreach (var (co, ogon) in new[]
                         {
                             ("od sterownika", _skladacz.Wyczysc()),
                             ("do sterownika", _skladaczRozkazow.Wyczysc())
                         })
                {
                    if (ogon is null) continue;
                    Pulapka.Zapisz(Podpis,
                        "OGON Z POPRZEDNIEGO POLACZENIA (" + co + "): " +
                        Slad.Podglad(ogon, ogon.Length) +
                        " - wyrzucony, zeby nie skleil sie z pierwsza ramka nowego polaczenia",
                        null, null, TimeSpan.Zero);
                }

                // Po przerwie antena mogla zostac przekrecona recznie - pierwszy odczyt
                // po polaczeniu nie ma sie do czego porownac i nie wolno go odrzucic.
                _ostatniaPozycja = -1;
                _kiedyPozycja = DateTime.MinValue;
                _odrzuconeZRzedu = 0;

                // Liczniki opisuja **to** polaczenie, nie caly dzien.
                lock (_wymiany)
                {
                    _wymiany.Clear();
                    _ileWymian = 0; _sumaMs = 0; _minMs = double.MaxValue; _maxMs = 0;
                    _zapytan = 0; _odpowiedzi = 0; _statusowPoprzednio = 0;
                }
                Volatile.Write(ref _brakow, 0);
                Volatile.Write(ref _spoznionych, 0);
                Interlocked.Exchange(ref _ostatniZrzutBraku, 0);

                var czytnik = OdpytywacSpe ? new CzytnikSpe { PrzechwytujEkran = _trybEkranu } : null;
                _czytnikSpe = czytnik;

                // Bufor portu zbieral dane przez caly czas laczenia - patrz
                // StrumienPortu.Wyczysc. Musi poleciec, zanim ruszy pompa.
                int zalegalo = (port as StrumienPortu)?.Wyczysc() ?? 0;
                if (zalegalo > 0)
                    Zapisz("odrzucono " + zalegalo + " B zaleglych z czasu laczenia");

                _kolejkaPortu = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
                _budzikPortu = new SemaphoreSlim(0);
                var pisarz = PisarzPortu(port, ct);

                var wGore = Pompa(port, siec, zPortu: true,  null, ct);
                var wDol  = Pompa(siec, port, zPortu: false, czytnik, ct);
                var pytania = czytnik is null ? Task.Delay(Timeout.Infinite, ct)
                                              : OdpytujSpe(siec, czytnik, ct);

                // Odczyt z sieci potrafi stanac na sekunde, wiec zalegly ogon ramki
                // musi miec kto wypchnac.
                var dopychacz = OdpytywacSpe ? Task.Delay(Timeout.Infinite, ct)
                                             : DopychajRamki(port, siec, ct);

                await Task.WhenAny(wGore, wDol, pytania, pisarz, dopychacz);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { _blad = ex.Message; }
            finally
            {
                // Bez tego slad milczy o najwazniejszym: czemu lacze padlo. Przy zrywaniu
                // przez ser2net (kickolduser) i przy zwyklym zaniku sieci wyglada to inaczej.
                if (Slad.Wlaczony)
                    Zapisz("polaczenie zakonczone" +
                           (_blad.Length > 0 ? ": " + _blad : " bez bledu (druga strona zamknela)"));

                if (_blad.Length == 0 && Volatile.Read(ref _stan) == (int)StanMostka.Polaczony)
                    PoliczPrzejecie();

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

            // Kazda sekunda bez lacza to sekunda, w ktorej program sterujacy nie dostaje
            // odpowiedzi na zapytanie o pozycje - a PstRotator czyta wtedy swoj pusty bufor
            // i pokazuje 208 stopni (`0x00 - '0'` daje na bajcie 208). Dlatego po zerwaniu
            // wracamy od razu, a dopiero gdy nie idzie, zwalniamy do dwoch sekund; inaczej
            // przy niedostepnym Pi dobijalibysmy sie bez przerwy.
            bool bylaUdana = zegarPolaczenia.Elapsed > TimeSpan.FromSeconds(5);
            _nieudanePodejscia = bylaUdana ? 0 : Math.Min(_nieudanePodejscia + 1, 3);

            int zwloka = _nieudanePodejscia switch { 0 => 250, 1 => 500, 2 => 1000, _ => 2000 };
            if (Slad.Wlaczony) Zapisz("ponowne laczenie za " + zwloka + " ms");

            try { await Task.Delay(zwloka, ct); }
            catch (OperationCanceledException) { break; }
        }

        Volatile.Write(ref _stan, (int)StanMostka.Zatrzymany);
    }

    /// <summary>
    /// Wlacza podtrzymywanie polaczenia TCP: po 10 s ciszy sonda co 2 s.
    ///
    /// Ma znaczenie po stronie ser2neta. Gdy komputer padnie albo zniknie siec, jego sesja
    /// zostaje na Pi jako zywa i **blokuje port** - domyslne keepalive w Linuksie rusza po
    /// dwoch godzinach. Z wlaczonym podtrzymywaniem martwa sesja znika w kilkanascie sekund
    /// i port wraca do uzytku. To jest warunek, ktory pozwala **wylaczyc `kickolduser`**
    /// po stronie ser2neta, a to z kolei jedyny sposob, zeby przypadkowe polaczenie z innego
    /// programu nie wyrzucalo dzialajacego mostka.
    /// </summary>
    private static void WlaczKeepAlive(TcpClient klient)
    {
        try
        {
            var ustawienia = new byte[12];
            BitConverter.GetBytes(1u).CopyTo(ustawienia, 0);        // wlaczone
            BitConverter.GetBytes(10000u).CopyTo(ustawienia, 4);    // po 10 s ciszy
            BitConverter.GetBytes(2000u).CopyTo(ustawienia, 8);     // sonda co 2 s
            klient.Client.IOControl(IOControlCode.KeepAliveValues, ustawienia, null);
        }
        catch { /* starszy system - zostaja domyslne */ }
    }

    /// <summary>
    /// Zlicza czyste zamkniecia przez druga strone w oknie pieciu minut. Trzy takie
    /// zamkniecia to juz nie przypadek: ser2net oddaje port jednemu klientowi naraz,
    /// wiec ktos inny laczy sie do niego cyklicznie i za kazdym razem nas wypycha.
    /// </summary>
    private void PoliczPrzejecie()
    {
        long teraz = DateTime.UtcNow.Ticks;
        long pierwsze = Interlocked.Read(ref _pierwszePrzejecie);

        if (pierwsze == 0 || new TimeSpan(teraz - pierwsze) > TimeSpan.FromMinutes(5))
        {
            Interlocked.Exchange(ref _pierwszePrzejecie, teraz);
            Volatile.Write(ref _obcePrzejecia, 1);
            return;
        }

        Volatile.Write(ref _obcePrzejecia, Volatile.Read(ref _obcePrzejecia) + 1);
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

        // Zaczynamy od granicy ramki. Czyszczenie bufora moglo trafic w srodek ramki,
        // ktora klient wlasnie wpisywal - jej ogon, puszczony dalej, przesunalby
        // sterownikowi caly strumien i kolejna ramka zlozylaby mu sie z polowek dwoch
        // roznych. Cisza na porcie (odczyt bez danych, limit 25 ms) to jedyny znak
        // granicy, jaki mamy bez wnikania w protokol.
        bool czekamNaCisze = zPortu;

        // Bezpiecznik: gdyby klient z jakiegos powodu nadawal bez przerwy, mostek nie
        // moze utknac w odrzucaniu na zawsze - to byloby gorsze niz to, przed czym
        // bronimy, bo wygladaloby na dzialajace polaczenie, ktore nic nie przepuszcza.
        var odSynchronizacji = System.Diagnostics.Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            int n = await skad.ReadAsync(bufor, 0, bufor.Length, ct);

            if (n <= 0)
            {
                // Z portu zero oznacza cisze na linii, z sieci - zerwane polaczenie.
                if (!zPortu) return;
                czekamNaCisze = false;
                continue;
            }

            if (czekamNaCisze)
            {
                if (odSynchronizacji.ElapsedMilliseconds > 1000)
                {
                    czekamNaCisze = false;
                    Zapisz("port->siec cisza nie nadeszla w 1 s - przepuszczam dalej");
                }
                else
                {
                    if (Slad.Wlaczony)
                        ZapiszRamke("port->siec ODRZUCONO przed cisza:", bufor, n);
                    continue;
                }
            }

            if (zPortu) Interlocked.Exchange(ref _ostatniRuchKlienta, DateTime.UtcNow.Ticks);

            // Pulapka dziala zawsze, takze przy wylaczonym sladzie - inaczej zlapanie
            // rzadkiego objawu wymaga szczescia. Mostki SPE pomijamy, bo to inny protokol.
            if (!OdpytywacSpe)
            {
                (zPortu ? _doSterownika : _odSterownika).Dopisz(bufor, n);
                _dziennik.Dopisz(zPortu ? "PC ->" : "   <- sterownik", bufor, n);

                // Zapisujemy **kazda** nastawe, nie tylko podejrzana. Jest ich kilka na
                // godzine, a bez pelnej listy nie da sie powiedziec, czy 208 przyszlo
                // z komputera, czy pojawilo sie gdzies dalej.
                if (zPortu)
                    foreach (var ramka in _rozkazy.Ramki(bufor, n))
                    {
                        string opis = Pulapka.OpiszNastawe(ramka, out _);
                        if (opis != null)
                            Pulapka.Zapisz(Podpis, opis, _doSterownika, _odSterownika,
                                           _odPolaczenia.Elapsed);
                    }
            }

            if (czytnik is null)
            {
                if (zPortu) Interlocked.Add(ref _tx, n);
                else        Interlocked.Add(ref _rx, n);
            }

            if (Slad.Wlaczony)
                ZapiszRamke(zPortu ? "port->siec" : "siec->port", bufor, n);

            if (czytnik is not null)
            {
                // Odpowiedzi na wlasne zapytania o status zdejmujemy ze strumienia -
                // program po drugiej stronie pary o nie nie prosil. Liczniki pokazuja
                // ruch klienta, wiec nasze wlasne odpytywanie do nich nie wchodzi.
                var dalej = czytnik.Przepusc(bufor, n);

                // Kazda rozebrana ramka statusu to jedna odpowiedz - czyja, nie ma znaczenia:
                // interesuje nas, czy wzmacniacz odpowiada w ogole i jak szybko. Wzmacniacz
                // idzie tym samym tunelem po LTE co rotory, wiec podlega tym samym zastojom.
                for (int i = _statusowPoprzednio; i < czytnik.IleStatusow; i++) ZanotujOdbior();
                _statusowPoprzednio = czytnik.IleStatusow;

                _status = czytnik.Status ?? _status;
                Interlocked.Add(ref _rx, dalej.Length);

                if (Slad.Wlaczony)
                    Zapisz("  czytnik przepuscil " + dalej.Length + " z " + n +
                                " B, wlasnych zapytan w toku " + czytnik.WlasneOczekujace +
                                ", klient pyta sam: " + czytnik.KlientPytaSam);

                if (dalej.Length == 0) continue;

                Oddaj(dokad, dalej, ct);
                continue;
            }

            if (zPortu && OdpytywacSpe)
            {
                await _bramka.WaitAsync(ct);
                try
                {
                    await dokad.WriteAsync(bufor, 0, n, ct);
                    await dokad.FlushAsync(ct);
                }
                finally { _bramka.Release(); }
            }
            else if (zPortu)
            {
                // Rotor: rozkaz wychodzi cala ramka albo wcale. Wyjmowanie ramek i zapis
                // ida pod tym samym semaforem, bo do gniazda pisze jeszcze zegar dopychajacy
                // zalegly ogon - gdyby tylko zapis byl chroniony, mogliby sie wyprzedzic
                // i sterownik dostalby bajty w zlej kolejnosci.
                await _bramka.WaitAsync(ct);
                try
                {
                    var rozkazy = _skladaczRozkazow.Dopisz(bufor, n);
                    if (rozkazy != null)
                    {
                        foreach (var ramka in rozkazy)
                        {
                            ZanotujZapytanie(ramka);
                            await dokad.WriteAsync(ramka, 0, ramka.Length, ct);
                        }
                        await dokad.FlushAsync(ct);
                    }
                }
                finally { _bramka.Release(); }
            }
            else if (OdpytywacSpe)
            {
                var kopia = new byte[n];
                Array.Copy(bufor, kopia, n);
                Oddaj(dokad, kopia, ct);
            }
            else
            {
                // Rotor: oddajemy cale ramki albo nic. Kawalek ramki wpisany na port
                // konczy sie tym, ze program sterujacy czyta pusty bufor i pokazuje
                // 208 stopni - patrz SkladaczSpid.
                lock (_kolejnosc)
                {
                    var gotowe = _skladacz.Dopisz(bufor, n);
                    if (gotowe != null)
                        foreach (var ramka in gotowe)
                        {
                            ZanotujOdpowiedz(ramka);
                            if (!OdrzucicNieprawdopodobnyOdczyt(ramka))
                                Oddaj(dokad, ramka, ct);
                        }
                }
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
            Zapisz("  do kolejki portu " + dane.Length + " B, w kolejce " + _kolejkaPortu.Count);
    }

    /// <summary>
    /// Ile razy sterownik nie odpowiedzial na zapytanie o pozycje od zestawienia lacza.
    /// </summary>
    public int BrakiOdpowiedzi => Volatile.Read(ref _brakow);

    /// <summary>Ile odpowiedzi przyszlo po terminie, gdy juz nikt na nie nie czekal.</summary>
    public int SpoznioneOdpowiedzi => Volatile.Read(ref _spoznionych);

    /// <summary>
    /// Ile zapytan o pozycje wyszlo i ile odpowiedzi wrocilo. **Sama roznica tych dwoch liczb
    /// jest odporna na wszystko** - nie wymaga dopasowywania odpowiedzi do zapytan, wiec nie da
    /// sie jej rozjechac. Pierwsza wersja pomiaru liczyla czasy przez kolejke FIFO i gdy raz
    /// zabraklo odpowiedzi, kazda nastepna byla przypisywana do zapytania sprzed sekundy -
    /// w podpowiedzi wychodzily wtedy srednie po cztery sekundy, ktore z rzeczywistoscia nie
    /// mialy nic wspolnego. Licznik nie klamie nawet wtedy, gdy dopasowanie zawiedzie.
    /// </summary>
    public string BilansWymian
    {
        get
        {
            int z = Volatile.Read(ref _zapytan), o = Volatile.Read(ref _odpowiedzi);
            if (z == 0) return "";

            // Zapytania wyslane, na ktore odpowiedz ma prawo dopiero nadejsc. Bez tego
            // odjecia bilans pokazywal "brak 1" przez trzy dziesiate kazdej sekundy - bo
            // tyle trwa wymiana - i wygladalo to na usterke, a bylo zwykla chwila w locie.
            int wLocie;
            lock (_wymiany) wLocie = _wymiany.Count;

            int brak = z - o - wLocie;

            return "zapytań " + z + ", odpowiedzi " + o +
                   (brak > 0 ? " (brak " + brak + ")" : "") +
                   (o > z ? ", nadmiarowych " + (o - z) : "");
        }
    }

    /// <summary>
    /// Czasy obrotu zapytanie-odpowiedz, w milisekundach. Sam **rozrzut** jest tu informacja:
    /// sonda wpieta wprost w port szeregowy Pi zmierzyla na sterowniku A3S 243/245/247 ms
    /// przy 3479 wymianach - sterownik odpowiada jak metronom. Jesli ten sam tor mierzony
    /// przez mostek daje wieksza srednia albo rozrzut, to roznica powstaje **nad** portem
    /// szeregowym i tam trzeba jej szukac, a nie w maszcie.
    /// </summary>
    public string OpisWymiany
    {
        get
        {
            lock (_wymiany)
            {
                if (_ileWymian == 0) return "";
                return _minMs.ToString("0") + "/" + (_sumaMs / _ileWymian).ToString("0") +
                       "/" + _maxMs.ToString("0") + " ms z " + _ileWymian;
            }
        }
    }

    /// <summary>
    /// Zapytanie o pozycje wyszlo do sterownika.
    ///
    /// **Brak odpowiedzi jest wskaznikiem wyprzedzajacym.** W dzienniku z 14 wrzesnia widac,
    /// ze sekunde przed odczytem 208 jedno zapytanie zostalo bez odpowiedzi, a nastepna
    /// odpowiedz przyszla 5 ms po kolejnym zapytaniu - czyli strumien przesunal sie o jedno.
    /// Samo 208 wylapuje filtr pozycji, ale tylko wtedy, gdy jest wlaczony i gdy liczba jest
    /// dostatecznie nieprawdopodobna. Dziura w wymianie widac zawsze i wczesniej.
    ///
    /// Nie mierzymy tu zadnego wlasnego limitu czasu, tylko korzystamy z rytmu klienta:
    /// jesli idzie **nastepne** zapytanie, a poprzednie wciaz czeka dluzej niz pol sekundy,
    /// to cos sie stalo. Prog 500 ms odsiewa klienta wysylajacego dwa zapytania pod rzad -
    /// normalny obrot trwa na tym torze 250-350 ms.
    ///
    /// **Zgloszone zapytanie zostaje w kolejce.** To jest sedno: dopoki odpowiedz nie przyjdzie,
    /// nie wiadomo, czy przepadla, czy tylko stoi w zatorze. Gdy przyjdzie pozniej, mowimy to
    /// wprost jako "ODPOWIEDZ SPOZNIONA" - i wtedy wiadomo, ze **nic sie nie zgubilo, tylko
    /// czekalo**. Te dwie rzeczy prowadza w zupelnie inne miejsca, wiec nie wolno ich mylic.
    /// </summary>
    private void ZanotujZapytanie(byte[] ramka)
    {
        if (!(Punkt is Rotor) || ramka.Length != 13 || ramka[0] != 0x57 ||
            ramka[12] != 0x20 || ramka[11] != 0x1F) return;

        ZanotujWyslanie();
    }

    /// <summary>
    /// Zapytanie poszlo. Wydzielone z <see cref="ZanotujZapytanie"/>, bo **wzmacniacz tez
    /// odpytujemy** i tez idzie przez ten tunel - rozny jest tylko sposob rozpoznania ramki.
    /// </summary>
    private void ZanotujWyslanie()
    {
        long teraz = DateTime.UtcNow.Ticks;
        Interlocked.Increment(ref _zapytan);

        PorzucPrzeterminowane(teraz);
        lock (_wymiany) _wymiany.Enqueue(teraz);
    }

    /// <summary>
    /// Wyrzuca z kolejki zapytania, na ktore odpowiedz nie ma juz prawa przyjsc.
    ///
    /// **Prog musi byc krotszy od odstepu odpytywania, inaczej pomiar zamiera po pierwszej
    /// zgubie.** Przy progu poltorej sekundy i odpytywaniu co sekunde osierocone zapytanie
    /// nigdy nie zdazylo sie przeterminowac: kolejna odpowiedz przypisywala sie do niego,
    /// zostawiajac osierocone nastepne, i tak w kolko. Widac to bylo w podpowiedzi jako czasy
    /// zamrozone na "z 106" przez pol godziny, przy rosnacej liczbie wymian.
    ///
    /// Sterownik odpowiada w 220-442 ms (zmierzone zrzutem pakietow), wiec zapytanie czekajace
    /// **ponad 700 ms jest martwe**. Po jego usunieciu nastepna odpowiedz znowu tworzy pare
    /// bez watpliwosci i pomiar sam wraca do zdrowia.
    /// </summary>
    /// <summary>
    /// Po ilu milisekundach uznajemy zapytanie za stracone. **Musi byc krotszy od odstepu
    /// odpytywania** - patrz PorzucPrzeterminowane. Rotor jest pytany co sekunde, wzmacniacz
    /// rzadziej i odpowiada wolniej, wiec dostaje wiecej luzu.
    /// </summary>
    private double ProgPorzucenia => OdpytywacSpe ? 2000 : 700;

    private void PorzucPrzeterminowane(long teraz)
    {
        int porzucone = 0;
        double najstarsze = 0;

        lock (_wymiany)
        {
            while (_wymiany.Count > 0)
            {
                double czeka = TimeSpan.FromTicks(teraz - _wymiany.Peek()).TotalMilliseconds;
                if (czeka < ProgPorzucenia) break;

                _wymiany.Dequeue();
                porzucone++;
                najstarsze = Math.Max(najstarsze, czeka);
            }
        }

        if (porzucone > 0)
        {
            Interlocked.Add(ref _brakow, porzucone);
            ZglosWymiane("BRAK ODPOWIEDZI: porzucam " + porzucone +
                         " zapytanie(a) bez odpowiedzi, najstarsze czekalo " +
                         najstarsze.ToString("0") + " ms");
        }
    }

    /// <summary>
    /// Odpowiedz przyszla. Dopasowujemy ja do **najstarszego** czekajacego zapytania - przy
    /// zatorze odpowiedz potrafi przyjsc juz po wyslaniu nastepnego zapytania i liczenie jej
    /// od tego nowego dawaloby absurdalne "5 ms" zamiast prawdziwej sekundy.
    /// </summary>
    private void ZanotujOdpowiedz(byte[] ramka)
    {
        if (ramka.Length != 5 || ramka[0] != 0x57 || ramka[4] != 0x20) return;
        ZanotujOdbior();
    }

    /// <summary>Odpowiedz przyszla - wspolne dla rotora i wzmacniacza.</summary>
    private void ZanotujOdbior()
    {
        Interlocked.Increment(ref _odpowiedzi);

        // Najpierw sprzataczka, potem dopasowanie - inaczej ta odpowiedz zostalaby przypisana
        // do zapytania, ktore juz dawno przepadlo, i pomiar rozjechalby sie na dobre.
        PorzucPrzeterminowane(DateTime.UtcNow.Ticks);

        string doZgloszenia = null;

        lock (_wymiany)
        {
            if (_wymiany.Count == 0)
            {
                // Odpowiedz na zapytanie juz porzucone. Czasu nie da sie tu policzyc
                // i **nie wolno zgadywac** - to byla droga do tamtych czterosekundowych
                // srednich, ktore braly sie z dopasowania do cudzego zapytania.
                Interlocked.Increment(ref _spoznionych);
                doZgloszenia = "ODPOWIEDZ PO TERMINIE: przyszla, gdy nikt juz na nia nie czekal";
            }
            else
            {
                bool jednoznaczna = _wymiany.Count == 1;
                long wyslano = _wymiany.Dequeue();
                double ms = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - wyslano).TotalMilliseconds;

                // Czas liczymy **tylko z par bez watpliwosci**: jedno zapytanie w locie,
                // jedna odpowiedz. Przy dwoch czekajacych nie wiadomo, ktora jest ktora,
                // a zmyslona liczba jest gorsza niz jej brak.
                if (jednoznaczna)
                {
                    _ileWymian++;
                    _sumaMs += ms;
                    if (ms < _minMs) _minMs = ms;
                    if (ms > _maxMs) _maxMs = ms;

                    if (ms >= 600) doZgloszenia = "ZATOR: odpowiedz po " + ms.ToString("0") + " ms";
                }
            }
        }

        if (doZgloszenia != null) ZglosWymiane(doZgloszenia);
    }

    /// <summary>
    /// Wpis o chorej wymianie. Ma **wlasny** dlawik, osobny od filtru pozycji: dziura
    /// i wywolany przez nia zly odczyt dziela sie zwykle sekunda i wspolny limit piecio
    /// sekundowy zjadlby ten drugi wpis - czyli dokladnie ten, po ktory sie tu przychodzi.
    /// </summary>
    private void ZglosWymiane(string powod)
    {
        long ostatni = Interlocked.Read(ref _ostatniZrzutBraku);
        if (ostatni != 0 && DateTime.UtcNow - new DateTime(ostatni) <= TimeSpan.FromSeconds(5)) return;

        Interlocked.Exchange(ref _ostatniZrzutBraku, DateTime.UtcNow.Ticks);
        Pulapka.Zapisz(Podpis,
            powod + ". Od zestawienia lacza: " + BilansWymian +
            ", po terminie " + SpoznioneOdpowiedzi + ", czasy " + OpisWymiany +
            Environment.NewLine + "    " + CzujnikZastoju.Opis,
            _doSterownika, _odSterownika, _odPolaczenia.Elapsed, _dziennik);
    }

    /// <summary>
    /// Czy ten odczyt jest fizycznie niemozliwy, a wiec nie jest odczytem.
    ///
    /// Rotor robi okolo 2,5 stopnia na sekunde, wiec miedzy odczytami moze zmienic sie
    /// o tyle, ile minelo czasu. **Prog musi zalezec od czasu, nie byc stala** - i to byl moj
    /// blad w 1.11.3: sztywne 30 stopni. Gdy antena zostala przekrecona w czasie, gdy nikt nie
    /// odpytywal (program zamkniety, recznie z panelu), pierwszy odczyt po przerwie roznil sie
    /// o 60 stopni, wiec zostal odrzucony - a poniewaz odrzuconej ramki nie zapamietujemy,
    /// **nastepne tez**, i filtr zablokowal sie na dobre. W sladzie widac to jak na dloni:
    /// antena jechala rowno 302, 305, 310, 313, 316, 319, 321, 326, 329, a filtr porownywal
    /// wszystko z pozycja 360 sprzed przerwy i odrzucal po kolei.
    ///
    /// Dwa zabezpieczenia, obydwa potrzebne:
    ///
    /// * **prog rosnie z czasem** - piec stopni na sekunde (dwa razy wiecej, niz rotor potrafi)
    ///   plus dziesiec stopni tolerancji, a po minucie ciszy nie odrzucamy juz nic,
    /// * **po trzech odrzuceniach z rzedu przyjmujemy odczyt i synchronizujemy sie od nowa** -
    ///   bo skoro sterownik uparcie mowi to samo, to zla pamiec mamy my, nie on. Filtr, ktory
    ///   potrafi sie zablokowac, jest gorszy od braku filtra.
    ///
    /// **To proteza na czas szukania usterki sprzetowej**, nie naprawa: przyczyna siedzi przed
    /// mostkiem i wychodzi tylko przy dwoch pracujacych rotorach. Kazde odrzucenie idzie do
    /// `podejrzane.txt` razem z dziennikiem obu kierunkow.
    ///
    /// Wlasnie dlatego, ze to proteza, **da sie ja wylaczyc** w Ustawieniach
    /// (<see cref="Config.FiltrPozycji"/>). Wylaczona przepuszcza wszystko, ale nadal opisuje
    /// nieprawdopodobne odczyty w `podejrzane.txt` - inaczej wylacznik zabieralby razem
    /// z filtrem jedyny slad usterki.
    ///
    /// Milknie tez sama, gdy <see cref="SkladaczSpid.Przezroczysty"/> - filtr zna protokol SPID
    /// i nie ma prawa kasowac danych, ktorych nie rozumie.
    /// </summary>
    private bool OdrzucicNieprawdopodobnyOdczyt(byte[] ramka)
    {
        // Filtr zna protokol SPID, wiec musi milczec tam, gdzie skladacz juz sie wycofal.
        // Inaczej w obcym protokole piec bajtow zaczynajacych sie od 0x57 i konczacych 0x20
        // byloby czytane jak pozycja rotora i mogloby zostac **skasowane** - a mostek
        // nie ma prawa gubic danych, ktorych nie rozumie.
        if (_skladacz.Przezroczysty) return false;

        // Pozycje maja tylko rotory. Przy zwyklym urzadzeniu szeregowym piec bajtow
        // zaczynajacych sie od 0x57 to przypadek, a nie azymut.
        if (!(Punkt is Rotor rotor)) return false;

        if (ramka.Length != 5 || ramka[0] != 0x57 || ramka[4] != 0x20) return false;

        int pozycja = ramka[1] * 100 + ramka[2] * 10 + ramka[3];
        int poprzednia = _ostatniaPozycja;

        double sekundy = _kiedyPozycja == DateTime.MinValue
            ? double.MaxValue
            : (DateTime.UtcNow - _kiedyPozycja).TotalSeconds;

        double dopuszczalny = sekundy >= 60 ? double.MaxValue : 5 * sekundy + 10;

        bool wiarygodny = poprzednia < 0 ||
                          Math.Abs(pozycja - poprzednia) <= dopuszczalny ||
                          _odrzuconeZRzedu >= 3;

        if (wiarygodny)
        {
            Zapamietaj(pozycja);
            return false;
        }

        // Filtr wylaczony w Ustawieniach: odczyt idzie dalej, ale **nadal go opisujemy**.
        // Wylacznik ma zdejmowac kasowanie danych, a nie diagnostyke - bez zapisu uzytkownik
        // stracilby jedyny slad usterki, ktorej wlasnie szuka.
        if (!rotor.FiltrPozycji)
        {
            ZglosNieprawdopodobny("PRZEPUSZCZONY (filtr wylaczony dla tego rotora)",
                                  poprzednia, pozycja, ramka, sekundy, dopuszczalny);
            Zapamietaj(pozycja);
            return false;
        }

        _odrzuconeZRzedu++;
        ZglosNieprawdopodobny("ODRZUCONY ODCZYT", poprzednia, pozycja, ramka, sekundy, dopuszczalny);
        return true;
    }

    private void Zapamietaj(int pozycja)
    {
        _ostatniaPozycja = pozycja;
        _kiedyPozycja = DateTime.UtcNow;
        _odrzuconeZRzedu = 0;
    }

    /// <summary>
    /// Wpis do <c>podejrzane.txt</c>, nie czesciej niz co piec sekund - przy zerwanym torze
    /// takich odczytow potrafi byc kilka na sekunde i plik zamienilby sie w dziennik.
    /// </summary>
    private void ZglosNieprawdopodobny(string co, int poprzednia, int pozycja, byte[] ramka,
                                       double sekundy, double dopuszczalny)
    {
        long ostatni = Interlocked.Read(ref _ostatniZrzut);
        if (ostatni != 0 && DateTime.UtcNow - new DateTime(ostatni) <= TimeSpan.FromSeconds(5)) return;

        Interlocked.Exchange(ref _ostatniZrzut, DateTime.UtcNow.Ticks);
        Pulapka.Zapisz(Podpis,
            co + ": " + poprzednia + " -> " + pozycja + " (" +
            Slad.Podglad(ramka, 5) + "), po " + sekundy.ToString("0.0") +
            " s dopuszczalne bylo " + dopuszczalny.ToString("0") + " st.",
            _doSterownika, _odSterownika, _odPolaczenia.Elapsed, _dziennik);
    }

    /// <summary>
    /// Wypycha na port ogon ramki, ktory nie doczekal sie dokonczenia. Patrz
    /// <see cref="SkladaczSpid.Dopchnij"/> - bez tego czekalby do nastepnej odpowiedzi.
    /// </summary>
    private async Task DopychajRamki(Stream port, Stream siec, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct);

            lock (_kolejnosc)
            {
                var zalegle = _skladacz.Dopchnij();
                if (zalegle != null)
                    foreach (var ramka in zalegle)
                    {
                        if (Slad.Wlaczony)
                            ZapiszRamke("siec->port OGON po ciszy", ramka, ramka.Length);
                        Oddaj(port, ramka, ct);
                    }
            }

            // Ogon rozkazu idzie do gniazda pod tym samym semaforem co pompa.
            await _bramka.WaitAsync(ct);
            try
            {
                var zalegleRozkazy = _skladaczRozkazow.Dopchnij();
                if (zalegleRozkazy == null) continue;

                foreach (var ramka in zalegleRozkazy)
                {
                    if (Slad.Wlaczony)
                        ZapiszRamke("port->siec OGON po ciszy", ramka, ramka.Length);
                    await siec.WriteAsync(ramka, 0, ramka.Length, ct);
                }
                await siec.FlushAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            finally { _bramka.Release(); }
        }
    }

    private async Task PisarzPortu(Stream port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _budzikPortu.WaitAsync(ct);
            if (!_kolejkaPortu.TryDequeue(out var dane)) continue;

            try
            {
                // **Bez zetonu anulowania.** Zapis na pare com0com potrafi stanac na sekundy,
                // wiec anulowanie w jego trakcie zostawialo na porcie **pol ramki** - a program
                // sterujacy, ktory dostanie `57 03`, doczyta reszte z wlasnego pustego bufora
                // i pokaze 208 stopni. Dokladnie to zglosil uzytkownik: zatrzymanie mostka przy
                // pracujacym PstRotatorze czasem konczylo sie skokiem na 208.
                //
                // Ramka ma piec albo trzynascie bajtow, wiec dopisanie jej do konca jest
                // krotsze niz jakikolwiek pozytek z przerwania w polowie. Petla i tak wyjdzie
                // przy nastepnym sprawdzeniu zetonu.
                var zegar = System.Diagnostics.Stopwatch.StartNew();
                await port.WriteAsync(dane, 0, dane.Length, CancellationToken.None);
                await port.FlushAsync(CancellationToken.None);
                if (Slad.Wlaczony)
                    Zapisz("  zapisano na port " + dane.Length + " B w " +
                           zegar.ElapsedMilliseconds + " ms");
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                Zapisz("  BLAD zapisu na port: " + ex.Message);
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
        if (poszlo) { czytnik?.ZglosWlasneZapytanie(); ZanotujWyslanie(); }
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

            Zapisz("wysylam wlasne zapytanie 0x90");

            await _bramka.WaitAsync(ct);
            try
            {
                await siec.WriteAsync(zapytanie, 0, zapytanie.Length, ct);
                await siec.FlushAsync(ct);
                czytnik.ZglosWlasneZapytanie();
                ZanotujWyslanie();
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
