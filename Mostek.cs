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
        try { _petla?.Wait(2500); } catch { /* nic */ }
        Volatile.Write(ref _stan, (int)StanMostka.Zatrzymany);
    }

    private async Task Petla(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Volatile.Write(ref _stan, (int)StanMostka.Laczenie);

                using var port = PortIo.Otworz(Punkt.Dev);
                using var klient = new TcpClient();
                await PolaczAsync(klient, Adres, Punkt.Port, ct);

                Stream siec = klient.GetStream();

                // Serwer RFC 2217 mowi Telnetem - bez rozpakowania sekwencji IAC
                // trafialyby one do danych.
                if (Punkt.Protokol == Protokol.Rfc2217)
                {
                    var telnet = new StrumienTelnet(siec);
                    await telnet.Przywitaj(ct);
                    siec = telnet;
                }

                _blad = "";
                Volatile.Write(ref _stan, (int)StanMostka.Polaczony);

                try
                {
                    var wGore = Pompa(port, siec, zPortu: true,  ct);
                    var wDol  = Pompa(siec, port, zPortu: false, ct);
                    await Task.WhenAny(wGore, wDol);
                }
                finally { siec.Dispose(); }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _blad = ex.Message; }

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
