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

    // Strumien do urzadzenia, zeby okno sterowania mialo gdzie wyslac kod klawisza.
    private volatile Stream _biezacaSiec;

    public Polaczenie Punkt { get; }
    public string Adres => _cfg.AdresDla(Punkt);
    public StanMostka Stan => (StanMostka)Volatile.Read(ref _stan);
    public long Rx => Interlocked.Read(ref _rx);
    public long Tx => Interlocked.Read(ref _tx);
    public string Blad => _blad;

    /// <summary>Ostatni odczytany stan wzmacniacza SPE albo null.</summary>
    public StatusSpe Status => _status;

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

            if (czytnik is null)
            {
                if (zPortu) Interlocked.Add(ref _tx, n);
                else        Interlocked.Add(ref _rx, n);
            }

            if (czytnik is not null)
            {
                // Odpowiedzi na wlasne zapytania o status zdejmujemy ze strumienia -
                // program po drugiej stronie pary o nie nie prosil. Liczniki pokazuja
                // ruch klienta, wiec nasze wlasne odpytywanie do nich nie wchodzi.
                var dalej = czytnik.Przepusc(bufor, n);
                _status = czytnik.Status ?? _status;
                Interlocked.Add(ref _rx, dalej.Length);
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
    }

    private async Task PisarzPortu(Stream port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _budzikPortu.WaitAsync(ct);
            if (!_kolejkaPortu.TryDequeue(out var dane)) continue;

            try
            {
                await port.WriteAsync(dane, 0, dane.Length, ct);
                await port.FlushAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch { /* port zniknal - petla mostka to zauwazy */ }
        }
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
            // Przy otwartym podgladzie ekranu w ogole nie pytamy o status. Zmierzone:
            // zapytanie wciskajace sie miedzy puls a klatke opoznialo ja o ponad sekunde,
            // a stan i tak widac wtedy na samym ekranie wzmacniacza.
            if (_trybEkranu)
            {
                await Task.Delay(500, ct);
                continue;
            }

            await _bramka.WaitAsync(ct);
            try
            {
                await siec.WriteAsync(zapytanie, 0, zapytanie.Length, ct);
                await siec.FlushAsync(ct);
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
