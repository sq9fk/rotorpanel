namespace RotorPanel;

/// <summary>
/// Slad diagnostyczny mostka. Wlacza sie sam, gdy obok pliku programu lezy pusty
/// plik <c>slad.wlacz</c>; pisze do <c>slad.txt</c> w tym samym katalogu.
///
/// Powstal, gdy SPE Term nie dostawal przez mostek nic, a wszystkie poszlaki
/// wskazywaly, ze dane docieraja do programu i gina dopiero po drodze do portu.
/// Zgadywanie nic nie dawalo - dopiero licznik na kazdym odcinku pokazal miejsce.
/// </summary>
public static class Slad
{
    private static readonly object _zamek = new();
    private static string _plik;
    private static bool _sprawdzono;
    private static StreamWriter _pisarz;
    private static long _zapisane;

    // Zmierzone przy diagnozie: okolo 460 kB w trzy minuty, czyli ~9 MB na godzine.
    // Bez limitu zapomniany plik slad.wlacz po cichu zapelnia dysk. Po przekroczeniu
    // zaczynamy nowy plik, a poprzedni zostaje jako .old - swieze zdarzenia sa
    // wazniejsze niz pierwsze sekundy nagrania.
    private const long MaksymalnyRozmiar = 10L * 1024 * 1024;

    public static bool Wlaczony
    {
        get
        {
            if (!_sprawdzono)
            {
                _sprawdzono = true;
                try
                {
                    string katalog = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
                    if (File.Exists(Path.Combine(katalog, "slad.wlacz")))
                        _plik = Path.Combine(katalog, "slad.txt");
                }
                catch { _plik = null; }
            }
            return _plik is not null;
        }
    }

    public static void Zapisz(string tekst)
    {
        if (!Wlaczony) return;
        try
        {
            lock (_zamek)
            {
                // Otwarty uchwyt zamiast otwierania pliku przy kazdej linii - zapis
                // siedzi w sciezce danych mostka i nie ma prawa jej spowalniac.
                if (_pisarz is null)
                {
                    _zapisane = new FileInfo(_plik).Exists ? new FileInfo(_plik).Length : 0;
                    _pisarz = new StreamWriter(_plik, append: true) { AutoFlush = true };
                }

                string linia = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + tekst;
                _pisarz.WriteLine(linia);
                _zapisane += linia.Length + 2;

                if (_zapisane >= MaksymalnyRozmiar) Przewin();
            }
        }
        catch { /* slad nie moze przeszkadzac w pracy */ }
    }

    /// <summary>Zaczyna nowy plik, poprzedni zostawiajac jako .old.</summary>
    private static void Przewin()
    {
        try
        {
            _pisarz.Dispose();
            _pisarz = null;
            _zapisane = 0;

            string stary = _plik + ".old";
            if (File.Exists(stary)) File.Delete(stary);
            File.Move(_plik, stary);
        }
        catch { _pisarz = null; _zapisane = 0; }
    }

    /// <summary>Pierwsze bajty porcji, zeby dalo sie rozpoznac ramke.</summary>
    public static string Podglad(byte[] dane, int ile)
    {
        int n = Math.Min(ile, 12);
        var s = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++) s.Append(dane[i].ToString("X2")).Append(' ');
        if (ile > n) s.Append("...");
        return s.ToString();
    }
}
