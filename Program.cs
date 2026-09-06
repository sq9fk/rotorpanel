using System.Net;

namespace RotorPanel;

internal static class Program
{
    // Nazwy bez prefiksu zyja w przestrzeni sesji - kazdy zalogowany uzytkownik
    // moze miec wlasna instancje, ale w obrebie sesji tylko jedna.
    private const string NazwaMutexu  = "RotorPanel.JednaInstancja";
    private const string NazwaSygnalu = "RotorPanel.PokazOkno";

    /// <summary>Program uruchomiony ponownie po podmianie pliku czeka na zwolnienie blokady.</summary>
    public const string ArgumentPoAktualizacji = "--po-aktualizacji";

    private static Mutex _blokada;

    [STAThread]
    private static void Main()
    {
        bool poAktualizacji = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, ArgumentPoAktualizacji, StringComparison.OrdinalIgnoreCase));

        _blokada = new Mutex(true, NazwaMutexu, out bool pierwszaInstancja);

        // Poprzednia kopia moze jeszcze konczyc prace - dajemy jej chwile.
        if (!pierwszaInstancja && poAktualizacji)
        {
            for (int i = 0; i < 40 && !pierwszaInstancja; i++)
            {
                Thread.Sleep(250);
                try { pierwszaInstancja = _blokada.WaitOne(0); } catch { break; }
            }
        }

        if (!pierwszaInstancja)
        {
            ObudzDzialajacaInstancje();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Bez tego kazdy nieprzechwycony wyjatek konczy sie surowym oknem .NET,
        // z ktorego trudno cokolwiek wyczytac.
        Application.ThreadException += (_, e) => PokazBlad(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => PokazBlad(e.ExceptionObject as Exception);

        // Starsze wersje .NET Framework nie negocjuja TLS 1.2 samoczynnie,
        // a bez niego GitHub odrzuca polaczenie.
        try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

        Aktualizacja.PosprzatajPoAktualizacji();

        try
        {
            var cfg = Config.Wczytaj();
            if (cfg.Anteny.Count == 0)
                throw new InvalidDataException("Lista anten w konfiguracji jest pusta.");

            var okno = new MainForm(cfg);
            NasluchujSygnalu(okno);
            Application.Run(okno);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Blad konfiguracji (" + Config.Sciezka + "):"
                + Environment.NewLine + Environment.NewLine + ex.Message,
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Zwalnia blokade, zeby nowa kopia programu mogla ja przejac.</summary>
    public static void ZwolnijBlokade()
    {
        try
        {
            _blokada?.ReleaseMutex();
            _blokada?.Dispose();
        }
        catch { /* i tak zaraz konczymy prace */ }
        finally { _blokada = null; }
    }

    /// <summary>Druga instancja tylko budzi pierwsza i konczy sie po cichu.</summary>
    private static void ObudzDzialajacaInstancje()
    {
        try
        {
            using var sygnal = EventWaitHandle.OpenExisting(NazwaSygnalu);
            sygnal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Pierwsza instancja jeszcze sie nie zdazyla zarejestrowac albo wlasnie konczy.
        }
    }

    /// <summary>Watek w tle czekajacy, az ktoras kopia poprosi o pokazanie okna.</summary>
    private static void NasluchujSygnalu(MainForm okno)
    {
        var sygnal = new EventWaitHandle(false, EventResetMode.AutoReset, NazwaSygnalu);

        var watek = new Thread(() =>
        {
            while (true)
            {
                sygnal.WaitOne();
                try
                {
                    if (okno.IsDisposed) return;
                    okno.BeginInvoke(new Action(okno.PokazZZewnatrz));
                }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
            }
        })
        {
            IsBackground = true,
            Name = "RotorPanel-sygnal"
        };

        watek.Start();
    }

    private static void PokazBlad(Exception ex)
    {
        MessageBox.Show(
            (ex?.GetType().Name ?? "Blad") + Environment.NewLine + Environment.NewLine +
            (ex?.Message ?? "nieznany") + Environment.NewLine + Environment.NewLine +
            (ex?.StackTrace ?? ""),
            "RotorPanel - blad", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
