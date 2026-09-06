namespace RotorPanel;

/// <summary>Kontrola parzystosci na porcie szeregowym.</summary>
public enum Parzystosc { Brak, Nieparzysta, Parzysta, Znacznik, Spacja }

/// <summary>Liczba bitow stopu.</summary>
public enum BityStopu { Jeden, Poltora, Dwa }

/// <summary>Sposob rozmowy z drugim koncem: surowy strumien albo Telnet z RFC 2217.</summary>
public enum Protokol
{
    Surowy,
    Rfc2217
}

/// <summary>
/// Wspolna czesc rotora i urzadzenia: para portow com0com plus punkt koncowy w sieci.
/// </summary>
public abstract class Polaczenie
{
    public int      Nr       { get; set; }
    public string   Nazwa    { get; set; } = "";
    public string   Com      { get; set; } = "";
    public string   Dev      { get; set; } = "";
    public string   Ip       { get; set; } = "";
    public int      Port     { get; set; }
    public Protokol Protokol { get; set; } = Protokol.Surowy;

    /// <summary>
    /// Parametry transmisji przekazywane urzadzeniu przy RFC 2217. Predkosc zero
    /// oznacza brak przekazania - serwer uzyje wtedy wlasnych ustawien.
    /// </summary>
    public int Predkosc { get; set; }

    /// <summary>Liczba bitow danych; zmieniana tylko w pliku konfiguracyjnym.</summary>
    public int BityDanych { get; set; } = 8;

    public Parzystosc Parzystosc { get; set; } = Parzystosc.Brak;

    public BityStopu BityStopu { get; set; } = BityStopu.Jeden;

    /// <summary>Zwiezly zapis formatu ramki, na przyklad 9600 8N1.</summary>
    public string OpisTransmisji
    {
        get
        {
            if (Predkosc <= 0) return "ustawienia urządzenia";

            char parzystosc =
                Parzystosc == Parzystosc.Parzysta     ? 'E' :
                Parzystosc == Parzystosc.Nieparzysta  ? 'O' :
                Parzystosc == Parzystosc.Znacznik     ? 'M' :
                Parzystosc == Parzystosc.Spacja       ? 'S' : 'N';

            string stop = BityStopu == BityStopu.Dwa ? "2"
                        : BityStopu == BityStopu.Poltora ? "1.5" : "1";

            return Predkosc + " " + BityDanych + parzystosc + stop;
        }
    }

    /// <summary>Ma komplet danych potrzebnych do zestawienia mostka.</summary>
    public bool Gotowy => !string.IsNullOrWhiteSpace(Dev) && Port > 0;

    public string KluczPary => (Dev ?? "").Trim().ToUpperInvariant();

    protected abstract string DomyslnaNazwa { get; }

    public string Etykieta => string.IsNullOrWhiteSpace(Nazwa) ? DomyslnaNazwa : Nazwa;

    public string NazwaProtokolu => Protokol == Protokol.Rfc2217 ? "RFC 2217" : "surowy";
}

/// <summary>Rotor obracajacy antena. Anteny wskazuja go numerem.</summary>
public class Rotor : Polaczenie
{
    protected override string DomyslnaNazwa => "Rotor " + Nr;
}

/// <summary>
/// Inne urzadzenie na porcie szeregowym - wzmacniacz, sterownik, cokolwiek.
/// Nie jest przypisane do anteny, stad osobna lista.
/// </summary>
public class Urzadzenie : Polaczenie
{
    protected override string DomyslnaNazwa => "Urządzenie " + Nr;

    /// <summary>
    /// Modele, ktorych protokol znamy. Wybor z tej listy wlacza odpytywanie o stan;
    /// wpisany recznie typ znaczy tyle co "jakies urzadzenie szeregowe".
    /// </summary>
    public static readonly string[] TypyZeStanem =
    {
        "SPE Expert 1.3K-FA",
        "SPE Expert 1.5K-FA",
        "SPE Expert 2K-FA"
    };

    /// <summary>Model urzadzenia. Puste znaczy: inne urzadzenie.</summary>
    public string Typ { get; set; } = "";

    /// <summary>Czy umiemy odpytac to urzadzenie o stan.</summary>
    public bool Spe => Array.IndexOf(TypyZeStanem, Typ) >= 0;
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
    public bool   SprawdzajAktualizacje { get; set; } = true;

    public List<Rotor>      Rotory     { get; set; } = new List<Rotor>();
    public List<Urzadzenie> Urzadzenia { get; set; } = new List<Urzadzenie>();
    public List<Antena>     Anteny     { get; set; } = new List<Antena>();

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

    /// <summary>Adres punktu koncowego - wlasny, a gdy pusty to domyslny.</summary>
    public string AdresDla(Polaczenie p) => string.IsNullOrWhiteSpace(p.Ip) ? PiIp : p.Ip.Trim();

    public Rotor ZnajdzRotor(int nr) => nr <= 0 ? null : Rotory.FirstOrDefault(r => r.Nr == nr);

    /// <summary>Rotor przypisany do anteny, o ile jest kompletny.</summary>
    public Rotor RotorAnteny(Antena a)
    {
        var r = ZnajdzRotor(a.Rotor);
        return r != null && r.Gotowy ? r : null;
    }

    /// <summary>Wszystkie punkty koncowe - rotory i urzadzenia razem.</summary>
    public IEnumerable<Polaczenie> WszystkiePolaczenia =>
        Rotory.Cast<Polaczenie>().Concat(Urzadzenia);

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
