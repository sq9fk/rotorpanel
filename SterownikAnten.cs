using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace RotorPanel;

/// <summary>Odczyt ze sterownika: nazwy anten oraz przypisanie anten do nadajnikow.</summary>
public sealed class StanSterownika
{
    /// <summary>Numer anteny na nazwe.</summary>
    public Dictionary<int, string> Nazwy { get; } = new Dictionary<int, string>();

    /// <summary>Numer nadajnika na numer wybranej anteny; zero oznacza brak wyboru.</summary>
    public Dictionary<int, int> Trx { get; } = new Dictionary<int, int>();

    /// <summary>Opisy nadajnikow z tytulow przyciskow, np. "Radio Flex TRX1".</summary>
    public Dictionary<int, string> OpisyTrx { get; } = new Dictionary<int, string>();

    /// <summary>Nadajniki wskazujace podana antene.</summary>
    public List<int> TrxNaAntenie(int antena)
        => Trx.Where(p => p.Value == antena).Select(p => p.Key).OrderBy(n => n).ToList();
}

public static class SterownikAnten
{
    private static readonly HttpClient Klient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Rozdziela zapis host[:port] na skladniki; bez portu przyjmujemy 80.</summary>
    public static bool Rozdziel(string adres, out string host, out int port)
    {
        host = Config.NormalizujHost(adres);
        port = 80;
        if (host.Length == 0) return false;

        int dwukropek = host.LastIndexOf(':');
        if (dwukropek > 0 && int.TryParse(host.Substring(dwukropek + 1), out int p) && p > 0 && p < 65536)
        {
            port = p;
            host = host.Substring(0, dwukropek);
        }

        return host.Length > 0;
    }

    /// <summary>Samo nawiazanie polaczenia TCP - tanszy test niz pobranie strony.</summary>
    public static async Task<bool> Dostepny(string adres, int limitMs = 3000)
    {
        if (!Rozdziel(adres, out string host, out int port)) return false;

        try
        {
            using var klient = new TcpClient();
            var laczenie = klient.ConnectAsync(host, port);
            if (await Task.WhenAny(laczenie, Task.Delay(limitMs)) != laczenie) return false;
            await laczenie;
            return klient.Connected;
        }
        catch { return false; }
    }

    public static async Task<StanSterownika> PobierzStan(string adres)
    {
        if (!Rozdziel(adres, out string host, out int port))
            throw new InvalidOperationException("Nie podano adresu sterownika anten.");

        string url = "http://" + host + (port == 80 ? "" : ":" + port) + "/";
        string tresc = await Klient.GetStringAsync(url);

        var stan = new StanSterownika();
        CzytajNazwy(tresc, stan);
        CzytajTrx(tresc, stan);

        if (stan.Nazwy.Count == 0 && stan.Trx.Count == 0)
            throw new InvalidDataException("Odpowiedz nie wyglada na strone sterownika anten.");

        return stan;
    }

    /// <summary>Zgodnosc wsteczna dla okna ustawien.</summary>
    public static async Task<Dictionary<int, string>> PobierzNazwy(string adres)
        => (await PobierzStan(adres)).Nazwy;

    private static readonly Regex Znaczniki =
        new Regex(@"<(input|button)\b[^>]*>", RegexOptions.IgnoreCase);

    private static string Atrybut(string znacznik, string nazwa)
    {
        var m = Regex.Match(znacznik,
            @"\b" + nazwa + @"\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Nazwy anten siedza w polach formularza N1..N6.</summary>
    private static void CzytajNazwy(string tresc, StanSterownika stan)
    {
        foreach (Match m in Znaczniki.Matches(tresc))
        {
            string nazwa = Atrybut(m.Value, "name");
            string wartosc = Atrybut(m.Value, "value");
            if (nazwa == null || wartosc == null) continue;
            if (nazwa.Length < 2 || (nazwa[0] != 'N' && nazwa[0] != 'n')) continue;

            if (int.TryParse(nazwa.Substring(1), out int nr) && nr >= 1 && nr <= 32)
                stan.Nazwy[nr] = System.Net.WebUtility.HtmlDecode(wartosc).Trim();
        }
    }

    /// <summary>
    /// Przyciski wyboru anteny nazywaja sie S{trx}{antena}, a ten odpowiadajacy
    /// aktualnemu wyborowi ma klase "g". Tytuly przyciskow F{trx}0 daja opis nadajnika.
    /// </summary>
    private static void CzytajTrx(string tresc, StanSterownika stan)
    {
        foreach (Match m in Znaczniki.Matches(tresc))
        {
            string nazwa = Atrybut(m.Value, "name");
            if (string.IsNullOrEmpty(nazwa)) continue;

            if ((nazwa[0] == 'S' || nazwa[0] == 's') && nazwa.Length == 4)
            {
                if (!int.TryParse(nazwa.Substring(1, 1), out int trx)) continue;
                if (!int.TryParse(nazwa.Substring(2, 2), out int antena)) continue;

                if (!stan.Trx.ContainsKey(trx)) stan.Trx[trx] = 0;

                string klasa = Atrybut(m.Value, "class") ?? "";
                if (Regex.IsMatch(klasa, @"(^|\s)g(\s|$)", RegexOptions.IgnoreCase))
                    stan.Trx[trx] = antena;
            }
            else if ((nazwa[0] == 'F' || nazwa[0] == 'f') && nazwa.Length == 3 && nazwa[2] == '0')
            {
                if (!int.TryParse(nazwa.Substring(1, 1), out int trx)) continue;
                string tytul = Atrybut(m.Value, "title");
                if (!string.IsNullOrWhiteSpace(tytul))
                    stan.OpisyTrx[trx] = System.Net.WebUtility.HtmlDecode(tytul).Trim();
            }
        }
    }
}
