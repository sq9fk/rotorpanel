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
                File.AppendAllText(_plik,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + tekst + Environment.NewLine);
        }
        catch { /* slad nie moze przeszkadzac w pracy */ }
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
