using System.Net.Http;
using System.Security.Cryptography;
using System.Reflection;

namespace RotorPanel;

/// <summary>Opis wydania dostepnego na GitHubie.</summary>
public sealed class Wydanie
{
    public Version Wersja { get; set; }
    public string Tag { get; set; } = "";
    public string Adres { get; set; } = "";

    /// <summary>Oczekiwana suma SHA-256 pliku, szesnastkowo malymi literami.</summary>
    public string Suma { get; set; } = "";
}

/// <summary>
/// Sprawdzanie i instalowanie aktualizacji z wydan projektu na GitHubie.
/// Podmiana dziala tak, ze dzialajacy plik zmieniamy na .old - Windows pozwala
/// przemianowac uruchomiony program, choc nie pozwala go nadpisac.
/// </summary>
public static class Aktualizacja
{
    private const string Repozytorium = "sq9fk/rotorpanel";
    private const string NazwaPliku   = "RotorPanel.exe";

    private static readonly HttpClient Klient = ZrobKlienta();

    private static HttpClient ZrobKlienta()
    {
        var k = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            // Plik wydania wazy okolo ćwierć megabajta; limit chroni przed
            // wciagnieciem czegokolwiek wiekszego do pamieci.
            MaxResponseContentBufferSize = 64L * 1024 * 1024
        };
        // GitHub odrzuca zapytania bez naglowka User-Agent.
        k.DefaultRequestHeaders.Add("User-Agent", "RotorPanel");
        k.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return k;
    }

    public static Version WersjaBiezaca =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public static string SciezkaProgramu => Application.ExecutablePath;

    private static string SciezkaStarego => SciezkaProgramu + ".old";

    /// <summary>Usuwa plik zostawiony po poprzedniej aktualizacji.</summary>
    public static void PosprzatajPoAktualizacji()
    {
        try
        {
            if (File.Exists(SciezkaStarego)) File.Delete(SciezkaStarego);
        }
        catch { /* zajety albo brak praw - sprobujemy nastepnym razem */ }
    }

    /// <summary>Zwraca wydanie nowsze od biezacego albo null.</summary>
    public static async Task<Wydanie> SprawdzNowsze()
    {
        string json = await Klient.GetStringAsync(
            "https://api.github.com/repos/" + Repozytorium + "/releases/latest");

        if (!(Json.Parsuj(json) is Dictionary<string, object> korzen)) return null;

        var wersja = ZTagu(Json.Tekst(korzen, "tag_name"));
        if (wersja == null || wersja <= WersjaBiezaca) return null;

        string adres = null;
        string suma = null;
        if (korzen.TryGetValue("assets", out object lista) && lista is List<object> tablica)
        {
            foreach (var element in tablica)
            {
                if (!(element is Dictionary<string, object> a)) continue;
                if (!string.Equals(Json.Tekst(a, "name"), NazwaPliku, StringComparison.OrdinalIgnoreCase))
                    continue;
                adres = Json.Tekst(a, "browser_download_url");
                // Sume liczy sam GitHub, po swojej stronie, przy wgrywaniu pliku.
                suma = NormalizujSume(Json.Tekst(a, "digest"));
                break;
            }
        }

        if (!AdresZaufany(adres)) return null;

        // Zapasowo suma wpisana w opisie wydania - na wypadek, gdyby pole "digest"
        // kiedys zniknelo albo zasob pochodzil sprzed jego wprowadzenia.
        if (suma == null) suma = ZOpisu(Json.Tekst(korzen, "body"));

        return new Wydanie
        {
            Wersja = wersja,
            Tag = Json.Tekst(korzen, "tag_name"),
            Adres = adres,
            Suma = suma ?? ""
        };
    }

    /// <summary>
    /// Czy adres pobrania wyglada na nasz. Bierzemy go z odpowiedzi API, wiec choc
    /// przychodzi po TLS z api.github.com, nie ma powodu ufac mu na slowo: wymagamy
    /// https i hosta w domenie GitHuba. Program podmienia sam siebie tym plikiem,
    /// wiec to jedyny moment, w ktorym mozemy cokolwiek sprawdzic po adresie.
    ///
    /// To tylko pierwsze sito. O tym, czy plik jest tym, ktory wydano, rozstrzyga suma
    /// SHA-256 - sprawdzana w <see cref="Pobierz"/> i jeszcze raz w <see cref="Podmien"/>.
    /// </summary>
    private static bool AdresZaufany(string adres)
    {
        if (string.IsNullOrEmpty(adres)) return false;
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        string host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Zamienia zapis w rodzaju v1.2.3 na numer wersji.</summary>
    private static Version ZTagu(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string s = tag.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);

        var czesci = s.Split('.');
        var liczby = new int[4];
        for (int i = 0; i < 4; i++)
            if (i >= czesci.Length || !int.TryParse(czesci[i], out liczby[i])) liczby[i] = 0;

        return new Version(liczby[0], liczby[1], liczby[2], liczby[3]);
    }

    /// <summary>Suma SHA-256 zapisana szesnastkowo, malymi literami.</summary>
    private static string Suma(byte[] dane)
    {
        using (var sha = SHA256.Create()) return Szesnastkowo(sha.ComputeHash(dane));
    }

    private static string SumaPliku(string plik)
    {
        using (var strumien = File.OpenRead(plik))
        using (var sha = SHA256.Create())
            return Szesnastkowo(sha.ComputeHash(strumien));
    }

    private static string Szesnastkowo(byte[] skrot)
    {
        var s = new System.Text.StringBuilder(skrot.Length * 2);
        foreach (byte b in skrot) s.Append(b.ToString("x2"));
        return s.ToString();
    }

    private static bool Szesnastkowa(char z) =>
        (z >= '0' && z <= '9') || (z >= 'a' && z <= 'f') || (z >= 'A' && z <= 'F');

    /// <summary>
    /// Sprowadza zapis sumy do 64 znakow szesnastkowych malymi literami albo zwraca null.
    /// Przyjmuje tez zapis z przedrostkiem, w rodzaju <c>sha256:ab12...</c>.
    /// </summary>
    private static string NormalizujSume(string tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst)) return null;

        string s = tekst.Trim();
        int dwukropek = s.IndexOf(':');
        if (dwukropek >= 0)
        {
            if (!s.Substring(0, dwukropek).Trim().Equals("sha256", StringComparison.OrdinalIgnoreCase))
                return null;
            s = s.Substring(dwukropek + 1).Trim();
        }

        if (s.Length != 64) return null;
        foreach (char z in s)
            if (!Szesnastkowa(z)) return null;

        return s.ToLowerInvariant();
    }

    /// <summary>Pierwsza suma SHA-256 znaleziona w opisie wydania.</summary>
    private static string ZOpisu(string opis)
    {
        if (string.IsNullOrEmpty(opis)) return null;

        int i = 0;
        while (i < opis.Length)
        {
            if (!Szesnastkowa(opis[i])) { i++; continue; }

            int poczatek = i;
            while (i < opis.Length && Szesnastkowa(opis[i])) i++;

            // Dokladnie 64 znaki: krotszy ciag to na przyklad skrot commita.
            if (i - poczatek == 64) return opis.Substring(poczatek, 64).ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// Pobiera plik wydania do katalogu tymczasowego i zwraca jego sciezke. Bez zgodnej
    /// sumy SHA-256 nie zapisujemy nic - program podmienia sam siebie tym plikiem, wiec
    /// TLS i nazwa hosta to za malo.
    /// </summary>
    public static async Task<string> Pobierz(Wydanie w)
    {
        if (!AdresZaufany(w.Adres))
            throw new InvalidDataException("Adres pobrania nie pochodzi z GitHuba.");

        string oczekiwana = NormalizujSume(w.Suma);
        if (oczekiwana == null)
            throw new InvalidDataException(
                "Wydanie nie podaje sumy SHA-256 pliku, wiec nie da sie sprawdzic, czy pobrany " +
                "program jest tym, ktory wydano. Pobierz nowa wersje recznie ze strony wydan.");

        var dane = await Klient.GetByteArrayAsync(w.Adres);
        if (dane == null || dane.Length < 32 * 1024)
            throw new InvalidDataException("Pobrany plik jest podejrzanie maly.");

        string mamy = Suma(dane);
        if (!string.Equals(mamy, oczekiwana, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Suma SHA-256 pobranego pliku sie nie zgadza - nie zapisano go na dysku." +
                Environment.NewLine + "oczekiwana: " + oczekiwana +
                Environment.NewLine + "pobrana:    " + mamy);

        string plik = Path.Combine(Path.GetTempPath(),
            "RotorPanel-" + w.Tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
        File.WriteAllBytes(plik, dane);
        return plik;
    }

    /// <summary>
    /// Podmienia plik programu na pobrany. Przy niepowodzeniu przywraca poprzedni,
    /// zeby nie zostawic katalogu bez dzialajacego programu.
    /// </summary>
    public static void Podmien(string pobrany, Wydanie w)
    {
        // Suma liczona drugi raz, juz z pliku na dysku. Miedzy pobraniem a podmiana plik
        // lezy w katalogu tymczasowym i to jedyna chwila, w ktorej moglby sie zmienic.
        string oczekiwana = NormalizujSume(w?.Suma);
        if (oczekiwana == null ||
            !string.Equals(SumaPliku(pobrany), oczekiwana, StringComparison.Ordinal))
        {
            try { File.Delete(pobrany); } catch { /* nieistotne */ }
            throw new InvalidDataException("Pobrany plik zmienil sie przed podmiana - przerwano.");
        }

        PosprzatajPoAktualizacji();

        File.Move(SciezkaProgramu, SciezkaStarego);
        try
        {
            File.Copy(pobrany, SciezkaProgramu);
        }
        catch
        {
            File.Move(SciezkaStarego, SciezkaProgramu);
            throw;
        }

        try { File.Delete(pobrany); } catch { /* nieistotne */ }
    }
}
