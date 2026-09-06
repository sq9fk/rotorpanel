namespace RotorPanel;

/// <summary>Wczytywanie i zapis konfiguracji wraz z migracja starszego formatu.</summary>
public partial class Config
{
    private static Config Domyslna()
    {
        var cfg = new Config
        {
            PiIp = "192.168.1.100",
            Setupc = "C:/Program Files (x86)/com0com/setupc.exe",
            SterownikAnten = "",
            AutoPolacz = false,
            SprawdzajAktualizacje = true
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
            AutoPolacz     = Json.Flaga(korzen, "autoPolacz", false),
            SprawdzajAktualizacje = Json.Flaga(korzen, "sprawdzajAktualizacje", true)
        };

        CzytajRotory(cfg, korzen);
        CzytajUrzadzenia(cfg, korzen);
        CzytajAnteny(cfg, korzen);
        Uporzadkuj(cfg);

        return cfg;
    }

    private static void CzytajRotory(Config cfg, Dictionary<string, object> korzen)
    {
        if (!korzen.TryGetValue("rotory", out object lista) || !(lista is List<object> tablica)) return;

        foreach (var element in tablica)
        {
            if (!(element is Dictionary<string, object> o)) continue;

            cfg.Rotory.Add(new Rotor
            {
                Nr    = Json.Liczba(o, "nr"),
                Nazwa = Json.Tekst(o, "nazwa"),
                Com   = Json.Tekst(o, "com"),
                Dev   = Json.Tekst(o, "dev"),
                Ip    = Json.Tekst(o, "ip"),
                Port  = Json.Liczba(o, "port"),
                Protokol   = ZTekstu(Json.Tekst(o, "protokol")),
                Predkosc   = Json.Liczba(o, "predkosc"),
                BityDanych = Json.Liczba(o, "bityDanych", 8),
                Parzystosc = ParzystoscZTekstu(Json.Tekst(o, "parzystosc")),
                BityStopu  = BityStopuZTekstu(Json.Tekst(o, "bityStopu"))
            });
        }
    }

    private static readonly string[] NazwyParzystosci =
        { "brak", "nieparzysta", "parzysta", "znacznik", "spacja" };

    private static readonly string[] NazwyBitowStopu = { "1", "1.5", "2" };

    private static Parzystosc ParzystoscZTekstu(string t)
    {
        for (int i = 0; i < NazwyParzystosci.Length; i++)
            if (string.Equals(NazwyParzystosci[i], (t ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return (Parzystosc)i;
        return Parzystosc.Brak;
    }

    private static BityStopu BityStopuZTekstu(string t)
    {
        for (int i = 0; i < NazwyBitowStopu.Length; i++)
            if (string.Equals(NazwyBitowStopu[i], (t ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return (BityStopu)i;
        return BityStopu.Jeden;
    }

    private static Protokol ZTekstu(string t)
        => string.Equals((t ?? "").Trim(), "rfc2217", StringComparison.OrdinalIgnoreCase)
            ? Protokol.Rfc2217
            : Protokol.Surowy;

    private static string NaTekst(Protokol p)
        => p == Protokol.Rfc2217 ? "rfc2217" : "surowy";

    private static void CzytajUrzadzenia(Config cfg, Dictionary<string, object> korzen)
    {
        if (!korzen.TryGetValue("urzadzenia", out object lista) || !(lista is List<object> tablica)) return;

        foreach (var element in tablica)
        {
            if (!(element is Dictionary<string, object> o)) continue;

            cfg.Urzadzenia.Add(new Urzadzenie
            {
                Nr       = Json.Liczba(o, "nr"),
                Nazwa    = Json.Tekst(o, "nazwa"),
                Com      = Json.Tekst(o, "com"),
                Dev      = Json.Tekst(o, "dev"),
                Ip       = Json.Tekst(o, "ip"),
                Port     = Json.Liczba(o, "port"),
                Protokol   = ZTekstu(Json.Tekst(o, "protokol")),
                Predkosc   = Json.Liczba(o, "predkosc"),
                BityDanych = Json.Liczba(o, "bityDanych", 8),
                Parzystosc = ParzystoscZTekstu(Json.Tekst(o, "parzystosc")),
                BityStopu  = BityStopuZTekstu(Json.Tekst(o, "bityStopu"))
            });
        }
    }

    private static void CzytajAnteny(Config cfg, Dictionary<string, object> korzen)
    {
        if (!korzen.TryGetValue("anteny", out object lista) || !(lista is List<object> tablica)) return;

        foreach (var element in tablica)
        {
            if (!(element is Dictionary<string, object> o)) continue;

            var antena = new Antena
            {
                Nr    = Json.Liczba(o, "nr"),
                Nazwa = Json.Tekst(o, "nazwa"),
                Rotor = Json.Liczba(o, "rotor")
            };

            // Starszy format trzymal pare i port przy antenie - zamieniamy to na rotor.
            if (antena.Rotor == 0 && Json.Flaga(o, "maRotor"))
            {
                string dev  = Json.Tekst(o, "dev");
                int    port = Json.Liczba(o, "port");

                if (!string.IsNullOrWhiteSpace(dev) && port > 0)
                {
                    var istniejacy = cfg.Rotory.FirstOrDefault(r =>
                        string.Equals(r.Dev, dev, StringComparison.OrdinalIgnoreCase));

                    if (istniejacy == null)
                    {
                        istniejacy = new Rotor
                        {
                            Nr = cfg.WolnyNumerRotora(),
                            Com = Json.Tekst(o, "com"),
                            Dev = dev,
                            Ip = Json.Tekst(o, "ip"),
                            Port = port
                        };
                        cfg.Rotory.Add(istniejacy);
                    }

                    antena.Rotor = istniejacy.Nr;
                }
            }

            cfg.Anteny.Add(antena);
        }
    }

    private static void Uporzadkuj(Config cfg)
    {
        if (cfg.Anteny.Count == 0) cfg.Anteny = Domyslna().Anteny;

        for (int i = 0; i < cfg.Anteny.Count; i++)
        {
            if (cfg.Anteny[i].Nr == 0) cfg.Anteny[i].Nr = i + 1;
            // Nazwy pochodza ze sterownika anten; gdy go nie ma, zostaje ANT1..ANTn.
            if (string.IsNullOrWhiteSpace(cfg.Anteny[i].Nazwa))
                cfg.Anteny[i].Nazwa = "ANT" + cfg.Anteny[i].Nr;
        }

        for (int i = 0; i < cfg.Rotory.Count; i++)
            if (cfg.Rotory[i].Nr == 0) cfg.Rotory[i].Nr = i + 1;

        for (int i = 0; i < cfg.Urzadzenia.Count; i++)
            if (cfg.Urzadzenia[i].Nr == 0) cfg.Urzadzenia[i].Nr = i + 1;

        // Odwolania do nieistniejacych rotorow czyscimy, zeby nie zostawaly puste karty.
        foreach (var a in cfg.Anteny)
            if (a.Rotor != 0 && cfg.ZnajdzRotor(a.Rotor) == null) a.Rotor = 0;
    }

    public void Zapisz()
    {
        var rotory = new List<object>();
        foreach (var r in Rotory)
        {
            var wpis = new JsonObiekt();
            wpis.Dodaj("nr", r.Nr);
            wpis.Dodaj("nazwa", r.Nazwa);
            wpis.Dodaj("com", r.Com);
            wpis.Dodaj("dev", r.Dev);
            wpis.Dodaj("ip", r.Ip);
            wpis.Dodaj("port", r.Port);
            wpis.Dodaj("protokol", NaTekst(r.Protokol));
            wpis.Dodaj("predkosc", r.Predkosc);
            wpis.Dodaj("bityDanych", r.BityDanych);
            wpis.Dodaj("parzystosc", NazwyParzystosci[(int)r.Parzystosc]);
            wpis.Dodaj("bityStopu", NazwyBitowStopu[(int)r.BityStopu]);
            rotory.Add(wpis);
        }

        var urzadzenia = new List<object>();
        foreach (var u in Urzadzenia)
        {
            var wpis = new JsonObiekt();
            wpis.Dodaj("nr", u.Nr);
            wpis.Dodaj("nazwa", u.Nazwa);
            wpis.Dodaj("com", u.Com);
            wpis.Dodaj("dev", u.Dev);
            wpis.Dodaj("ip", u.Ip);
            wpis.Dodaj("port", u.Port);
            wpis.Dodaj("protokol", NaTekst(u.Protokol));
            wpis.Dodaj("predkosc", u.Predkosc);
            wpis.Dodaj("bityDanych", u.BityDanych);
            wpis.Dodaj("parzystosc", NazwyParzystosci[(int)u.Parzystosc]);
            wpis.Dodaj("bityStopu", NazwyBitowStopu[(int)u.BityStopu]);
            urzadzenia.Add(wpis);
        }

        var anteny = new List<object>();
        foreach (var a in Anteny)
        {
            var wpis = new JsonObiekt();
            wpis.Dodaj("nr", a.Nr);
            wpis.Dodaj("nazwa", a.Nazwa);
            wpis.Dodaj("rotor", a.Rotor);
            anteny.Add(wpis);
        }

        var korzen = new JsonObiekt();
        korzen.Dodaj("piIp", PiIp);
        korzen.Dodaj("setupc", Setupc);
        korzen.Dodaj("sterownikAnten", SterownikAnten);
        korzen.Dodaj("autoPolacz", AutoPolacz);
        korzen.Dodaj("sprawdzajAktualizacje", SprawdzajAktualizacje);
        korzen.Dodaj("rotory", rotory);
        korzen.Dodaj("urzadzenia", urzadzenia);
        korzen.Dodaj("anteny", anteny);

        File.WriteAllText(Sciezka, Json.Zapisz(korzen));
    }
}
