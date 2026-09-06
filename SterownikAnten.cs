using System.Net.Http;
using System.Text.RegularExpressions;

namespace RotorPanel;

/// <summary>
/// Pobiera nazwy anten ze sterownika 6x2. Radzi sobie z dwoma postaciami odpowiedzi:
/// JSON (gdyby firmware kiedys taki wystawil) oraz strona HTML z polami N1..N6.
/// </summary>
public static class SterownikAnten
{
    private static readonly HttpClient Klient = new() { Timeout = TimeSpan.FromSeconds(6) };

    public static async Task<Dictionary<int, string>> PobierzNazwy(string adres)
    {
        if (string.IsNullOrWhiteSpace(adres))
            throw new InvalidOperationException("Nie podano adresu sterownika anten.");

        if (!adres.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            adres = "http://" + adres;

        string tresc = await Klient.GetStringAsync(adres);

        var zJson = SprobujJson(tresc);
        if (zJson.Count > 0) return zJson;

        var zHtml = SprobujHtml(tresc);
        if (zHtml.Count > 0) return zHtml;

        throw new InvalidDataException(
            "Odpowiedz nie zawiera nazw anten ani w postaci JSON, ani w polach N1..N6.");
    }

    /// <summary>Akceptuje tablice nazw albo obiekt postaci klucz-nazwa.</summary>
    private static Dictionary<int, string> SprobujJson(string tresc)
    {
        var wynik = new Dictionary<int, string>();
        string t = tresc.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return wynik;

        object korzen;
        try { korzen = Json.Parsuj(tresc); }
        catch (FormatException) { return wynik; }

        if (korzen is List<object> tablica)
        {
            int nr = 1;
            foreach (var el in tablica)
                if (el is string nazwa) wynik[nr++] = nazwa;
            return wynik;
        }

        if (korzen is Dictionary<string, object> obiekt)
        {
            foreach (var pole in obiekt)
            {
                if (!(pole.Value is string nazwa)) continue;
                string klucz = pole.Key.TrimStart('N', 'n');
                if (int.TryParse(klucz, out int nr) && nr >= 1 && nr <= 32)
                    wynik[nr] = nazwa;
            }
        }

        return wynik;
    }

    /// <summary>Wyciaga value z pol formularza o nazwach N1..N6.</summary>
    private static Dictionary<int, string> SprobujHtml(string tresc)
    {
        var wynik = new Dictionary<int, string>();

        var wzor = new Regex(
            @"<input[^>]*\bname\s*=\s*[""']N(\d+)[""'][^>]*\bvalue\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);

        foreach (Match m in wzor.Matches(tresc))
            if (int.TryParse(m.Groups[1].Value, out int nr))
                wynik[nr] = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value).Trim();

        return wynik;
    }
}
