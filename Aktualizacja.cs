using System.Net.Http;
using System.Reflection;

namespace RotorPanel;

/// <summary>Opis wydania dostepnego na GitHubie.</summary>
public sealed class Wydanie
{
    public Version Wersja { get; set; }
    public string Tag { get; set; } = "";
    public string Adres { get; set; } = "";
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
        var k = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
        if (korzen.TryGetValue("assets", out object lista) && lista is List<object> tablica)
        {
            foreach (var element in tablica)
            {
                if (!(element is Dictionary<string, object> a)) continue;
                if (!string.Equals(Json.Tekst(a, "name"), NazwaPliku, StringComparison.OrdinalIgnoreCase))
                    continue;
                adres = Json.Tekst(a, "browser_download_url");
                break;
            }
        }

        if (string.IsNullOrEmpty(adres)) return null;

        return new Wydanie
        {
            Wersja = wersja,
            Tag = Json.Tekst(korzen, "tag_name"),
            Adres = adres
        };
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

    public static async Task<string> Pobierz(Wydanie w)
    {
        var dane = await Klient.GetByteArrayAsync(w.Adres);
        if (dane == null || dane.Length < 32 * 1024)
            throw new InvalidDataException("Pobrany plik jest podejrzanie maly.");

        string plik = Path.Combine(Path.GetTempPath(),
            "RotorPanel-" + w.Tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
        File.WriteAllBytes(plik, dane);
        return plik;
    }

    /// <summary>
    /// Podmienia plik programu na pobrany. Przy niepowodzeniu przywraca poprzedni,
    /// zeby nie zostawic katalogu bez dzialajacego programu.
    /// </summary>
    public static void Podmien(string pobrany)
    {
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
