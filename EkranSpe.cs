namespace RotorPanel;

/// <summary>
/// Zawartosc wyswietlacza wzmacniacza SPE Expert, czytana z ramki 0x6A.
///
/// Producent tego nie opisuje - uklad ramki i kodowanie znakow pochodza z pracy
/// nad odtworzeniem protokolu w projekcie vu2cpl/macexpert-spe. Tryb podgladu
/// wlacza komenda RCU ON (0x80), wylacza RCU OFF (0x81).
///
/// Kodowanie znakow:
///   0x10-0x3F  znak z atrybutem, prawdziwy ASCII = bajt + 0x20
///   0x40-0x7E  zwykly ASCII
///   reszta     wlasne znaki wyswietlacza (ramki, ikony)
/// </summary>
public sealed class EkranSpe
{
    public const byte Typ = 0x6A;
    public const byte RcuWlacz = 0x80;
    public const byte RcuWylacz = 0x81;

    public const int DlugoscDanych = 367;
    public const int DlugoscRamki = 3 + 1 + DlugoscDanych;

    // Wyswietlacz jest graficzny, wiec bajty nie tworza rownej siatki znakow.
    // Z pomiaru na Expercie 1.3K-FA wiersz po 48 znakow uklada tekst bez ciecia
    // slow w polowie - to najbardziej czytelny podzial, jaki wyszedl.
    public const int Kolumn = 48;
    public const int Wierszy = 8;

    public string[] Wiersze { get; private set; } = new string[0];
    public string Podpowiedzi { get; private set; } = "";
    public DateTime Kiedy { get; private set; }

    /// <summary>Caly ekran jako jeden tekst - do porownan i do dymka.</summary>
    public string Calosc => string.Join(Environment.NewLine, Wiersze);

    public static EkranSpe Rozbierz(IList<byte> dane, int od)
    {
        if (dane.Count - od < DlugoscDanych) return null;

        var e = new EkranSpe { Kiedy = DateTime.UtcNow };

        var wiersze = new List<string>();
        for (int w = 0; w < Wierszy; w++)
        {
            int poczatek = od + w * Kolumn;
            int ile = Math.Min(Kolumn, od + DlugoscDanych - poczatek);
            wiersze.Add(ile > 0 ? Tekst(dane, poczatek, ile) : "");
        }
        e.Wiersze = wiersze.ToArray();
        e.Podpowiedzi = Scisnij(Tekst(dane, od + 224, 96));

        return e;
    }

    private static string Tekst(IList<byte> dane, int od, int ile)
    {
        var znaki = new char[ile];
        for (int i = 0; i < ile; i++)
        {
            byte b = od + i < dane.Count ? dane[od + i] : (byte)0;

            if (b >= 0x10 && b <= 0x3F)      znaki[i] = (char)(b + 0x20);
            else if (b >= 0x40 && b <= 0x7E) znaki[i] = (char)b;
            else                             znaki[i] = '.';
        }
        return new string(znaki);
    }

    /// <summary>Zbija ciagi kropek (pustych komorek) w jedna spacje.</summary>
    private static string Scisnij(string tekst)
    {
        var wynik = new System.Text.StringBuilder(tekst.Length);
        bool kropka = false;

        foreach (char z in tekst)
        {
            if (z == '.')
            {
                if (!kropka) wynik.Append(' ');
                kropka = true;
            }
            else
            {
                wynik.Append(z);
                kropka = false;
            }
        }

        return wynik.ToString().Trim();
    }
}
