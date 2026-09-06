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

    public Polaczenie Punkt { get; }
    public string Adres => _cfg.AdresDla(Punkt);
    public StanMostka Stan => (StanMostka)Volatile.Read(ref _stan);
    public long Rx => Interlocked.Read(ref _rx);
    public long Tx => Interlocked.Read(ref _tx);
    public string Blad => _blad;

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

                _blad = "";
                Volatile.Write(ref _stan, (int)StanMostka.Polaczony);

                var wGore = Pompa(port, siec, zPortu: true,  ct);
                var wDol  = Pompa(siec, port, zPortu: false, ct);
                await Task.WhenAny(wGore, wDol);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { _blad = ex.Message; }
            finally
            {
                _biezacyKlient = null;
                _biezacyPort = null;

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

    private async Task Pompa(Stream skad, Stream dokad, bool zPortu, CancellationToken ct)
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

            await dokad.WriteAsync(bufor, 0, n, ct);
            await dokad.FlushAsync(ct);

            if (zPortu) Interlocked.Add(ref _tx, n);
            else        Interlocked.Add(ref _rx, n);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
