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
    /// Czy w porcji jest rozkaz, ktory nie powinien sie tam znalezc. Sprawdzamy dwie rzeczy:
    /// nastawe rowna 208 stopni (ta, ktorej nikt nie wydaje) oraz cyfry, ktore nie sa cyframi
    /// ASCII - bo zerowy bajt czytany jak cyfra daje na bajcie dokladnie 208.
    /// </summary>
    public static bool Podejrzany(byte[] dane, int ile, out string powod)
    {
        powod = null;

        for (int i = 0; i + 13 <= ile; i++)
        {
            if (dane[i] != 0x57 || dane[i + 12] != 0x20) continue;
            if (dane[i + 11] != 0x2F) continue;          // tylko nastawy, nie zapytania

            bool ascii = true;
            int wartosc = 0;
            for (int k = 1; k <= 4; k++)
            {
                int cyfra = dane[i + k] - '0';
                if (cyfra < 0 || cyfra > 9) { ascii = false; break; }
                wartosc = wartosc * 10 + cyfra;
            }

            if (!ascii)
            {
                powod = "nastawa z cyframi spoza ASCII: " + Bajty(dane, i, 13);
                return true;
            }

            double azymut = wartosc / 10.0 - 360;
            if (Math.Abs(azymut - 208) < 0.05)
            {
                powod = "NASTAWA 208 stopni: " + Bajty(dane, i, 13);
                return true;
            }
        }

        return false;
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
                pisarz.WriteLine("    od zestawienia lacza: " + odPolaczenia.TotalSeconds.ToString("0.0") + " s");
                pisarz.WriteLine("    do sterownika (ostatnie bajty): " + doSterownika.Hex());
                pisarz.WriteLine("    od sterownika (ostatnie bajty): " + odSterownika.Hex());
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
