namespace RotorPanel;

/// <summary>
/// Pulapka na podejrzane rozkazy rotora. Dziala **zawsze**, bez wlaczania sladu.
///
/// Powstala, bo nastawa 208 stopni wracala mimo kolejnych poprawek, a zlapanie jej sladem
/// wymagalo szczescia: trzeba bylo miec wlaczony zapis dokladnie wtedy, gdy sie zdarzy.
/// Tutaj jest odwrotnie - program czeka na nia sam i w chwili, gdy ja zobaczy, zapisuje
/// nie tylko sama ramke, ale i to, co szlo w obie strony **przed** nia. Bez tego kontekstu
/// nie da sie odroznic rozkazu, ktory ktos naprawde wyslal, od ramki zlozonej z kawalkow.
///
/// Zapis idzie do <c>podejrzane.txt</c> obok programu i ma limit rozmiaru - to ma byc
/// dowod rzeczowy, nie dziennik.
/// </summary>
public static class Pulapka
{
    private static readonly object _zamek = new();
    private const long MaksymalnyRozmiar = 2L * 1024 * 1024;

    /// <summary>Bufor ostatnich bajtow jednego kierunku.</summary>
    public sealed class Bufor
    {
        private readonly byte[] _dane;
        private int _ile;

        public Bufor(int pojemnosc) => _dane = new byte[pojemnosc];

        public void Dopisz(byte[] zrodlo, int ile)
        {
            lock (_dane)
            {
                foreach (byte b in Ostatnie(zrodlo, ile, _dane.Length))
                {
                    if (_ile == _dane.Length)
                    {
                        Array.Copy(_dane, 1, _dane, 0, _dane.Length - 1);
                        _ile--;
                    }
                    _dane[_ile++] = b;
                }
            }
        }

        public string Hex()
        {
            lock (_dane)
            {
                var s = new System.Text.StringBuilder(_ile * 3);
                for (int i = 0; i < _ile; i++) s.Append(_dane[i].ToString("X2")).Append(' ');
                return s.ToString().TrimEnd();
            }
        }

        private static IEnumerable<byte> Ostatnie(byte[] zrodlo, int ile, int limit)
        {
            int od = Math.Max(0, ile - limit);
            for (int i = od; i < ile; i++) yield return zrodlo[i];
        }
    }

    /// <summary>
    /// Sklada strumien w ramki. **Nie wolno szukac ramki w pojedynczej porcji** - ramka
    /// rozkazu ma 13 bajtow i potrafi przyjsc podzielona na dwie porcje (w sladzie widac
    /// porcje po 1 i 4 bajty). Pierwsza wersja pulapki tego nie uwzgledniala i mogla
    /// przepuscic dokladnie to, na co czekala.
    /// </summary>
    public sealed class Wykrywacz
    {
        private readonly List<byte> _bufor = new();

        public List<byte[]> Ramki(byte[] dane, int ile)
        {
            var znalezione = new List<byte[]>();

            for (int i = 0; i < ile; i++) _bufor.Add(dane[i]);

            int od = 0;
            while (od + 13 <= _bufor.Count)
            {
                if (_bufor[od] != 0x57 || _bufor[od + 12] != 0x20) { od++; continue; }
                znalezione.Add(_bufor.GetRange(od, 13).ToArray());
                od += 13;
            }

            _bufor.RemoveRange(0, od);

            // Bez tego niedokonczona ramka rosla by w nieskonczonosc.
            if (_bufor.Count > 64) _bufor.RemoveRange(0, _bufor.Count - 64);

            return znalezione;
        }
    }

    /// <summary>
    /// Opis nastawy. Azymut liczymy **na kilka sposobow**, bo bajt rozdzielczosci bywa
    /// rozny, a pomylka w dzielniku ukrylaby wlasnie te nastawe, ktorej szukamy: 5680/10
    /// to 208, ale 1136 z rozdzielczoscia 2 albo 2272 z rozdzielczoscia 4 to tez 208.
    /// </summary>
    public static string OpiszNastawe(byte[] r, out bool podejrzana)
    {
        podejrzana = false;
        if (r.Length < 13 || r[11] != 0x2F) return null;

        bool ascii = true;
        int wartosc = 0;
        for (int k = 1; k <= 4; k++)
        {
            int cyfra = r[k] - '0';
            if (cyfra < 0 || cyfra > 9) { ascii = false; break; }
            wartosc = wartosc * 10 + cyfra;
        }

        if (!ascii)
        {
            podejrzana = true;
            return "NASTAWA z cyframi spoza ASCII (zerowy bajt czytany jak cyfra daje 208): " +
                   Bajty(r, 0, 13);
        }

        int rozdzielczosc = r[5] > 0 ? r[5] : 1;
        var warianty = new List<string>();
        foreach (int dzielnik in new[] { 1, 2, 4, 10, 10 * rozdzielczosc })
        {
            double az = (double)wartosc / dzielnik - 360;
            warianty.Add("/" + dzielnik + " = " + az.ToString("0.#"));
            if (Math.Abs(az - 208) < 0.05) podejrzana = true;
        }

        return (podejrzana ? "NASTAWA 208 " : "nastawa ") + Bajty(r, 0, 13) +
               "   cyfry " + wartosc + ", rozdzielczosc " + rozdzielczosc +
               ", azymut " + string.Join("  ", warianty);
    }

    /// <summary>
    /// Znak, ze pulapka chodzi. Bez tego "nie ma pliku" znaczy dwie rzeczy naraz: albo nic
    /// podejrzanego nie przeszlo, albo wersja z pulapka w ogole nie byla uruchomiona.
    /// </summary>
    public static void Uzbrojono(string podpis)
    {
        Zapisz(podpis, "pulapka uzbrojona, wersja " +
               System.Reflection.Assembly.GetExecutingAssembly().GetName().Version, null, null,
               TimeSpan.Zero);
    }

    public static void Zapisz(string podpis, string powod, Bufor doSterownika, Bufor odSterownika,
                              TimeSpan odPolaczenia)
    {
        try
        {
            string katalog = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            string plik = Path.Combine(katalog, "podejrzane.txt");

            lock (_zamek)
            {
                if (File.Exists(plik) && new FileInfo(plik).Length > MaksymalnyRozmiar) return;

                using var pisarz = new StreamWriter(plik, append: true);
                pisarz.WriteLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + podpis);
                pisarz.WriteLine("    " + powod);
                if (doSterownika != null)
                {
                    pisarz.WriteLine("    od zestawienia lacza: " +
                                     odPolaczenia.TotalSeconds.ToString("0.0") + " s");
                    pisarz.WriteLine("    do sterownika (ostatnie bajty): " + doSterownika.Hex());
                    pisarz.WriteLine("    od sterownika (ostatnie bajty): " + odSterownika.Hex());
                }
                pisarz.WriteLine();
            }
        }
        catch { /* pulapka nie moze przeszkadzac w pracy */ }
    }

    private static string Bajty(byte[] dane, int od, int ile)
    {
        var s = new System.Text.StringBuilder(ile * 3);
        for (int i = od; i < od + ile && i < dane.Length; i++)
            s.Append(dane[i].ToString("X2")).Append(' ');
        return s.ToString().TrimEnd();
    }
}
