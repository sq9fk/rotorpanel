using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace RotorPanel;

/// <summary>Jedna para portow com0com: strona A (dla aplikacji) i strona B (dla mostka).</summary>
public sealed class ParaPortow
{
    /// <summary>Numer pary w sterowniku - potrzebny do "setupc remove".</summary>
    public string Numer { get; set; } = "";

    public string A { get; set; } = "";
    public string B { get; set; } = "";

    /// <summary>
    /// Czy para faktycznie istnieje w systemie. "setupc remove" kasuje urzadzenia,
    /// ale zostawia wpisy w rejestrze - takie osierocone pary trzeba odsiac.
    /// </summary>
    public bool Istnieje { get; set; }

    public string Opis => A + "  ⇄  " + B;
    public override string ToString() => Opis;
}

/// <summary>Podglad i zarzadzanie parami com0com.</summary>
public static class Com0Com
{
    private static string Klucz(params string[] czesci) => string.Join(((char)92).ToString(), czesci);

    private static string Cudzyslow(string s)
    {
        char q = (char)34;
        return q + s + q;
    }

    /// <summary>Lista par zdefiniowanych w sterowniku - zrodlo dla listy rozwijanej.</summary>
    public static List<ParaPortow> Pary()
    {
        var wynik = new List<ParaPortow>();
        try
        {
            using var klucz = Registry.LocalMachine.OpenSubKey(
                Klucz("SYSTEM", "CurrentControlSet", "services", "com0com", "Parameters"));
            if (klucz is null) return wynik;

            var nazwy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var podklucz in klucz.GetSubKeyNames())
            {
                using var pk = klucz.OpenSubKey(podklucz);
                string port = pk?.GetValue("PortName") as string;
                if (!string.IsNullOrEmpty(port)) nazwy[podklucz] = port;
            }

            foreach (var wpis in nazwy.Keys.Where(k => k.StartsWith("CNCA", StringComparison.OrdinalIgnoreCase))
                                           .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                string numer = wpis.Substring(4);
                if (!nazwy.TryGetValue("CNCB" + numer, out string b)) continue;
                string a = nazwy[wpis];
                bool istnieje = PortIo.Opis(a) != "nie istnieje"
                             || PortIo.Opis(b) != "nie istnieje";

                wynik.Add(new ParaPortow { Numer = numer, A = a, B = b, Istnieje = istnieje });
            }
        }
        catch { /* brak sterownika albo brak dostepu */ }

        return wynik;
    }

    /// <summary>
    /// Numery COM zajete w systemie - z arbitra nazw, z listy portow oraz z samych par
    /// com0com. Arbiter zna tez numery zarezerwowane przez urzadzenia obecnie odlaczone.
    /// </summary>
    public static SortedSet<int> ZajeteNumeryCom()
    {
        var zajete = new SortedSet<int>();

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                Klucz("SYSTEM", "CurrentControlSet", "Control", "COM Name Arbiter"));
            if (k?.GetValue("ComDB") is byte[] db)
                for (int i = 0; i < db.Length; i++)
                    for (int b = 0; b < 8; b++)
                        if ((db[i] & (1 << b)) != 0) zajete.Add(i * 8 + b + 1);
        }
        catch { /* brak dostepu - trudno */ }

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                Klucz("HARDWARE", "DEVICEMAP", "SERIALCOMM"));
            if (k != null)
                foreach (var wartosc in k.GetValueNames())
                    Dodaj(zajete, k.GetValue(wartosc) as string);
        }
        catch { /* jak wyzej */ }

        foreach (var para in Pary().Where(p => p.Istnieje))
        {
            Dodaj(zajete, para.A);
            Dodaj(zajete, para.B);
        }

        return zajete;
    }

    private static void Dodaj(SortedSet<int> zbior, string nazwaPortu)
    {
        if (string.IsNullOrEmpty(nazwaPortu)) return;
        if (!nazwaPortu.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return;
        if (int.TryParse(nazwaPortu.Substring(3), out int nr)) zbior.Add(nr);
    }

    /// <summary>Pierwszy numer COM niezajety w systemie, poczawszy od podanego.</summary>
    public static int PierwszyWolnyNumer(int od = 10)
    {
        var zajete = ZajeteNumeryCom();
        int n = Math.Max(od, 1);
        while (zajete.Contains(n) && n < 256) n++;
        return n;
    }

    /// <summary>Czy nazwa portu jest juz uzywana przez ktoras ze stron istniejacych par.</summary>
    public static bool NazwaZajetaPrzezPare(string nazwa)
    {
        if (string.IsNullOrWhiteSpace(nazwa)) return false;
        foreach (var para in Pary().Where(p => p.Istnieje))
            if (string.Equals(para.A, nazwa, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(para.B, nazwa, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Sciezka w rejestrze do jednej strony pary, w formacie polecenia "reg".</summary>
    public static string SciezkaRejestruPary(string strona, string numer)
        => "HKLM" + (char)92 +
           Klucz("SYSTEM", "CurrentControlSet", "services", "com0com", "Parameters") +
           (char)92 + strona + numer;

    /// <summary>Gotowa linia wsadowa wywolujaca setupc z podanym poleceniem.</summary>
    public static string LiniaSetupc(Config cfg, string polecenie)
        => Cudzyslow(Config.NaWindows(cfg.Setupc)) + " " + polecenie;

    /// <summary>Linie kasujace osierocone wpisy rejestru po usunietej parze.</summary>
    public static IEnumerable<string> LinieSprzatajaceRejestr(string numer)
    {
        yield return "reg delete " + Cudzyslow(SciezkaRejestruPary("CNCA", numer)) + " /f >nul 2>&1";
        yield return "reg delete " + Cudzyslow(SciezkaRejestruPary("CNCB", numer)) + " /f >nul 2>&1";
    }

    public static string Raport(Config cfg)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Porty z konfiguracji");
        sb.AppendLine("--------------------");
        foreach (var r in cfg.Rotory.Where(x => x.Gotowy))
        {
            sb.AppendLine(
                "  " + r.Etykieta.PadRight(12) +
                r.Com.PadRight(7) + PortIo.Opis(r.Com).PadRight(18) +
                r.Dev.PadRight(9) + PortIo.Opis(r.Dev));
        }

        sb.AppendLine();
        sb.AppendLine("Pary com0com (z rejestru)");
        sb.AppendLine("-------------------------");
        sb.Append(ParyZRejestru());

        sb.AppendLine();
        sb.AppendLine("Porty szeregowe widziane przez system");
        sb.AppendLine("-------------------------------------");
        sb.Append(PortyZRejestru());

        return sb.ToString();
    }

    /// <summary>
    /// Czyta konfiguracje com0com prosto z rejestru. Nie wymaga uprawnien administratora,
    /// w przeciwienstwie do "setupc list".
    /// </summary>
    private static string ParyZRejestru()
    {
        try
        {
            using var klucz = Registry.LocalMachine.OpenSubKey(
                Klucz("SYSTEM", "CurrentControlSet", "services", "com0com", "Parameters"));

            if (klucz is null)
                return "  sterownik com0com nie jest zainstalowany" + Environment.NewLine;

            var sb = new StringBuilder();
            bool cokolwiek = false;

            foreach (var podklucz in klucz.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                using var pk = klucz.OpenSubKey(podklucz);
                if (pk is null) continue;

                string port = pk.GetValue("PortName") as string;
                if (string.IsNullOrEmpty(port)) continue;
                cokolwiek = true;

                var inne = pk.GetValueNames()
                    .Where(n => !string.Equals(n, "PortName", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => n + "=" + pk.GetValue(n))
                    .ToList();

                bool niestandardowe = inne.Any(w => w.EndsWith("=1") || w.EndsWith("=yes"));

                sb.AppendLine("  " + (niestandardowe ? "! " : "  ") +
                              podklucz.PadRight(8) + port.PadRight(10) +
                              (inne.Count == 0 ? "(domyślne)" : string.Join(", ", inne)));
            }

            if (!cokolwiek)
                return "  brak zdefiniowanych par" + Environment.NewLine;

            sb.AppendLine();
            sb.AppendLine("  Wykrzyknik oznacza parametr odbiegajacy od domyslnego.");
            sb.AppendLine("  EmuBR=1 emuluje predkosc transmisji, ExclusiveMode=1 ukrywa port");
            sb.AppendLine("  dopoki druga strona pary nie zostanie otwarta.");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "  " + ex.Message + Environment.NewLine;
        }
    }

    private static string PortyZRejestru()
    {
        try
        {
            using var klucz = Registry.LocalMachine.OpenSubKey(
                Klucz("HARDWARE", "DEVICEMAP", "SERIALCOMM"));

            if (klucz is null) return "  brak wpisow" + Environment.NewLine;

            var porty = klucz.GetValueNames()
                .Select(n => klucz.GetValue(n) as string)
                .Where(v => !string.IsNullOrEmpty(v))
                .OrderBy(v => v.Length)
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return porty.Count == 0
                ? "  brak wpisow" + Environment.NewLine
                : "  " + string.Join(", ", porty) + Environment.NewLine;
        }
        catch (Exception ex)
        {
            return "  " + ex.Message + Environment.NewLine;
        }
    }

    /// <summary>Uruchamia setupc z podanymi poleceniami w oknie podniesionym przez UAC.</summary>
    public static bool Wykonaj(Config cfg, IEnumerable<string> polecenia, string tytul, IWin32Window wlasciciel)
        => WykonajLinie(cfg, polecenia.Select(x => LiniaSetupc(cfg, x)), tytul, wlasciciel);

    /// <summary>
    /// Uruchamia gotowe linie wsadowe w oknie podniesionym przez UAC. Pozwala mieszac
    /// wywolania setupc z innymi poleceniami, na przyklad sprzataniem rejestru.
    /// </summary>
    public static bool WykonajLinie(Config cfg, IEnumerable<string> polecenia, string tytul, IWin32Window wlasciciel)
    {
        if (!File.Exists(cfg.Setupc))
        {
            MessageBox.Show(wlasciciel, "Nie znaleziono setupc.exe:" + Environment.NewLine + cfg.Setupc,
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        string bat = Path.Combine(Path.GetTempPath(),
            "rotorpanel_" + Guid.NewGuid().ToString("N") + ".bat");

        string katalog = Path.GetDirectoryName(Config.NaWindows(cfg.Setupc)) ?? "";

        // setupc szuka com0com.inf w katalogu biezacym - bez tego "install" konczy sie
        // bledem "SetupOpenInfFile ... ERROR: 2".
        var linie = new List<string>
        {
            "@echo off",
            "title " + tytul,
            "cd /d " + Cudzyslow(katalog)
        };
        linie.AddRange(polecenia);
        linie.Add("echo.");
        linie.Add("echo Gotowe. Zamknij to okno.");
        linie.Add("pause");

        try
        {
            File.WriteAllLines(bat, linie, Encoding.ASCII);
            var psi = new ProcessStartInfo { FileName = bat, UseShellExecute = true, Verb = "runas" };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            return true;
        }
        catch (Win32Exception)
        {
            MessageBox.Show(wlasciciel, "Anulowano podniesienie uprawnien.",
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(wlasciciel, ex.Message, "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            try { File.Delete(bat); } catch { /* nieistotne */ }
        }
    }
}
