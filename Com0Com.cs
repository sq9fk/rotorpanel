using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace RotorPanel;

/// <summary>Jedna para portow com0com: strona A (dla aplikacji) i strona B (dla mostka).</summary>
public sealed class ParaPortow
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";

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
                wynik.Add(new ParaPortow { A = nazwy[wpis], B = b });
            }
        }
        catch { /* brak sterownika albo brak dostepu */ }

        return wynik;
    }

    public static string Raport(Config cfg)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Porty z konfiguracji");
        sb.AppendLine("--------------------");
        foreach (var a in cfg.Anteny.Where(x => x.Gotowa))
        {
            sb.AppendLine(
                "  " + a.Etykieta.PadRight(12) +
                a.Com.PadRight(7) + PortIo.Opis(a.Com).PadRight(18) +
                a.Dev.PadRight(9) + PortIo.Opis(a.Dev));
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
    {
        if (!File.Exists(cfg.Setupc))
        {
            MessageBox.Show(wlasciciel, "Nie znaleziono setupc.exe:" + Environment.NewLine + cfg.Setupc,
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        string bat = Path.Combine(Path.GetTempPath(),
            "rotorpanel_" + Guid.NewGuid().ToString("N") + ".bat");

        string exe = Config.NaWindows(cfg.Setupc);
        string katalog = Path.GetDirectoryName(exe) ?? "";

        // setupc szuka com0com.inf w katalogu biezacym - bez tego "install" konczy sie
        // bledem "SetupOpenInfFile ... ERROR: 2".
        var linie = new List<string>
        {
            "@echo off",
            "title " + tytul,
            "cd /d " + Cudzyslow(katalog)
        };
        foreach (var p in polecenia) linie.Add(Cudzyslow(exe) + " " + p);
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
