namespace RotorPanel;

/// <summary>
/// Rotor to para portow com0com plus punkt koncowy ser2net. Anteny wskazuja rotor
/// numerem, wiec kilka anten na wspolnym maszcie po prostu wskazuje ten sam.
/// </summary>
public class Rotor
{
    public int    Nr    { get; set; }
    public string Nazwa { get; set; } = "";
    public string Com   { get; set; } = "";
    public string Dev   { get; set; } = "";
    public string Ip    { get; set; } = "";
    public int    Port  { get; set; }

    /// <summary>Ma komplet danych potrzebnych do zestawienia mostka.</summary>
    public bool Gotowy => !string.IsNullOrWhiteSpace(Dev) && Port > 0;

    public string KluczPary => (Dev ?? "").Trim().ToUpperInvariant();

    public string Etykieta => string.IsNullOrWhiteSpace(Nazwa) ? "Rotor " + Nr : Nazwa;

    /// <summary>Opis do list wyboru: nazwa wraz z trasa.</summary>
    public string Opis => Gotowy
        ? Etykieta + "   (" + Com + " " + ((char)0x2192) + " :" + Port + ")"
        : Etykieta + "   (niekompletny)";
}

public class Antena
{
    public int    Nr    { get; set; }
    public string Nazwa { get; set; } = "";

    /// <summary>Numer przypisanego rotora; zero oznacza antene bez rotora.</summary>
    public int Rotor { get; set; }

    public string Etykieta => string.IsNullOrWhiteSpace(Nazwa) ? "ANT" + Nr : Nazwa;
}

public partial class Config
{
    public string PiIp           { get; set; } = "127.0.0.1";
    public string Setupc         { get; set; } = "";
    public string SterownikAnten { get; set; } = "";
    public bool   AutoPolacz     { get; set; }

    public List<Rotor>  Rotory { get; set; } = new List<Rotor>();
    public List<Antena> Anteny { get; set; } = new List<Antena>();

    private static string _sciezka;

    public static string Sciezka
    {
        get
        {
            if (_sciezka != null) return _sciezka;

            string obok = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rotory.json");
            if (File.Exists(obok) || KatalogZapisywalny(AppDomain.CurrentDomain.BaseDirectory))
            {
                _sciezka = obok;
                return _sciezka;
            }

            string profil = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RotorPanel");
            Directory.CreateDirectory(profil);
            _sciezka = Path.Combine(profil, "rotory.json");
            return _sciezka;
        }
    }

    private static bool KatalogZapisywalny(string katalog)
    {
        try
        {
            string probny = Path.Combine(katalog, "." + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probny, "");
            File.Delete(probny);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Adres ser2net dla rotora - wlasny, a gdy pusty to domyslny.</summary>
    public string AdresDla(Rotor r) => string.IsNullOrWhiteSpace(r.Ip) ? PiIp : r.Ip.Trim();

    public Rotor ZnajdzRotor(int nr) => nr <= 0 ? null : Rotory.FirstOrDefault(r => r.Nr == nr);

    /// <summary>Rotor przypisany do anteny, o ile jest kompletny.</summary>
    public Rotor RotorAnteny(Antena a)
    {
        var r = ZnajdzRotor(a.Rotor);
        return r != null && r.Gotowy ? r : null;
    }

    public int WolnyNumerRotora()
    {
        int nr = 1;
        while (Rotory.Any(r => r.Nr == nr)) nr++;
        return nr;
    }

    /// <summary>
    /// Sprowadza adres sterownika anten do postaci host[:port]. Przyjmuje takze pelny
    /// adres URL, bo tak wygladaly starsze konfiguracje.
    /// </summary>
    public static string NormalizujHost(string wartosc)
    {
        string s = (wartosc ?? "").Trim();
        if (s.Length == 0) return "";

        int schemat = s.IndexOf("://", StringComparison.Ordinal);
        if (schemat >= 0) s = s.Substring(schemat + 3);

        int ukosnik = s.IndexOf('/');
        if (ukosnik >= 0) s = s.Substring(0, ukosnik);

        return s.Trim();
    }

    /// <summary>Zamienia ukosniki w przod na wsteczne - potrzebne przy budowaniu plikow .bat.</summary>
    public static string NaWindows(string p) => p.Replace('/', (char)92);
}
