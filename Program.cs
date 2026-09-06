namespace RotorPanel;

internal static class Program
{
    // Nazwy bez prefiksu zyja w przestrzeni sesji - kazdy zalogowany uzytkownik
    // moze miec wlasna instancje, ale w obrebie sesji tylko jedna.
    private const string NazwaMutexu  = "RotorPanel.JednaInstancja";
    private const string NazwaSygnalu = "RotorPanel.PokazOkno";

    [STAThread]
    private static void Main()
    {
        using var blokada = new Mutex(true, NazwaMutexu, out bool pierwszaInstancja);

        if (!pierwszaInstancja)
        {
            ObudzDzialajacaInstancje();
            return;
        }

        // Odpowiednik ApplicationConfiguration.Initialize() z .NET 6+.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Bez tego kazdy nieprzechwycony wyjatek konczy sie surowym oknem .NET,
        // z ktorego trudno cokolwiek wyczytac.
        Application.ThreadException += (_, e) => PokazBlad(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => PokazBlad(e.ExceptionObject as Exception);

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
