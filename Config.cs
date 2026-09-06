namespace RotorPanel;

/// <summary>Jedna antena: nazwa ze sterownika plus opcjonalne przypisanie rotora.</summary>
public class Antena
{
    public int    Nr    { get; set; }
    public string Nazwa { get; set; } = "";
    public string Com   { get; set; } = "";
    public string Dev   { get; set; } = "";
    public string Ip    { get; set; } = "";
    public int    Port  { get; set; }

    /// <summary>Jawny przelacznik: czy do tej anteny podpiety jest rotor.</summary>
    public bool MaRotor { get; set; }

    /// <summary>Ma rotor i komplet danych potrzebnych do zestawienia mostka.</summary>
    public bool Gotowa => MaRotor && !string.IsNullOrWhiteSpace(Dev) && Port > 0;

    /// <summary>Klucz pary portow - anteny na tym samym maszcie dziela jeden mostek.</summary>
    public string KluczPary => (Dev ?? "").Trim().ToUpperInvariant();

    public string Etykieta => string.IsNullOrWhiteSpace(Nazwa) ? "ANT" + Nr : Nazwa;
}

public class Config
{
    public string PiIp           { get; set; } = "127.0.0.1";
    public string Setupc         { get; set; } = "";
    public string SterownikAnten { get; set; } = "";
    public bool   AutoPolacz     { get; set; }
    public List<Antena> Anteny   { get; set; } = new List<Antena>();

    private static string _sciezka;

    /// <summary>
    /// Konfiguracja lezy obok pliku exe - dzieki temu wersja przenosna nosi swoje
    /// ustawienia ze soba. Gdy katalog jest tylko do odczytu, przenosimy sie do profilu.
    /// </summary>
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

    /// <summary>Adres ser2net dla danej anteny - wlasny, a gdy pusty to domyslny.</summary>
    public string AdresDla(Antena a) => string.IsNullOrWhiteSpace(a.Ip) ? PiIp : a.Ip.Trim();

    /// <summary>Konfiguracja startowa, gdy pliku jeszcze nie ma.</summary>
    private static Config Domyslna()
    {
        var cfg = new Config
        {
            PiIp = "192.168.1.100",
            Setupc = "C:/Program Files (x86)/com0com/setupc.exe",
            SterownikAnten = "",
            AutoPolacz = false
        };
        for (int i = 1; i <= 6; i++)
            cfg.Anteny.Add(new Antena { Nr = i, Nazwa = "ANT" + i });
        return cfg;
    }

    public static Config Wczytaj()
    {
        if (!File.Exists(Sciezka))
        {
            var startowa = Domyslna();
            startowa.Zapisz();
            return startowa;
        }

        var korzen = Json.Parsuj(File.ReadAllText(Sciezka)) as Dictionary<string, object>;
        if (korzen == null) throw new InvalidDataException("Konfiguracja nie jest obiektem JSON.");

        var cfg = new Config
        {
            PiIp           = Json.Tekst(korzen, "piIp", "127.0.0.1"),
            Setupc         = Json.Tekst(korzen, "setupc"),
            SterownikAnten = NormalizujHost(Json.Tekst(korzen, "sterownikAnten")),
            AutoPolacz     = Json.Flaga(korzen, "autoPolacz", false)
        };

        if (korzen.TryGetValue("anteny", out object lista) && lista is List<object> tablica)
        {
            foreach (var element in tablica)
            {
                var o = element as Dictionary<string, object>;
                if (o == null) continue;

                cfg.Anteny.Add(new Antena
                {
                    Nr      = Json.Liczba(o, "nr"),
                    Nazwa   = Json.Tekst(o, "nazwa"),
                    Com     = Json.Tekst(o, "com"),
                    Dev     = Json.Tekst(o, "dev"),
                    Ip      = Json.Tekst(o, "ip"),
                    Port    = Json.Liczba(o, "port"),
                    MaRotor = Json.Flaga(o, "maRotor")
                });
            }
        }

        if (cfg.Anteny.Count == 0) cfg.Anteny = Domyslna().Anteny;

        for (int i = 0; i < cfg.Anteny.Count; i++)
        {
            if (cfg.Anteny[i].Nr == 0) cfg.Anteny[i].Nr = i + 1;
            // Nazwy pochodza ze sterownika anten; gdy go nie ma, zostaje ANT1..ANTn.
            if (string.IsNullOrWhiteSpace(cfg.Anteny[i].Nazwa))
                cfg.Anteny[i].Nazwa = "ANT" + cfg.Anteny[i].Nr;
        }

        return cfg;
    }

    public void Zapisz()
    {
        var anteny = new List<object>();
        foreach (var a in Anteny)
        {
            var wpis = new JsonObiekt();
            wpis.Dodaj("nr", a.Nr);
            wpis.Dodaj("nazwa", a.Nazwa);
            wpis.Dodaj("maRotor", a.MaRotor);
            wpis.Dodaj("com", a.Com);
            wpis.Dodaj("dev", a.Dev);
            wpis.Dodaj("ip", a.Ip);
            wpis.Dodaj("port", a.Port);
            anteny.Add(wpis);
        }

        var korzen = new JsonObiekt();
        korzen.Dodaj("piIp", PiIp);
        korzen.Dodaj("setupc", Setupc);
        korzen.Dodaj("sterownikAnten", SterownikAnten);
        korzen.Dodaj("autoPolacz", AutoPolacz);
        korzen.Dodaj("anteny", anteny);

        File.WriteAllText(Sciezka, Json.Zapisz(korzen));
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
