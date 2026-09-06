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

    // Ramka nie ma pola dlugosci - konczy sie tam, gdzie zaczyna sie nastepna
    // synchronizacja. Na Expercie 1.3K-FA wychodzi 367 bajtow, ale trzymamy sie
    // ramowania po synchronizacji, tak jak robi to macexpert-spe.
    public const int DlugoscTypowa = 367;
    public const int DlugoscMaksymalna = 512;
    public const int DlugoscNaglowka = 4;

    // Wyswietlacz jest graficzny, wiec bajty nie tworza rownej siatki znakow.
    // Z pomiaru na Expercie 1.3K-FA wiersz po 48 znakow uklada tekst bez ciecia
    // slow w polowie - to najbardziej czytelny podzial, jaki wyszedl.
    public const int Kolumn = 48;
    public const int Wierszy = 8;

    public string[] Wiersze { get; private set; } = new string[0];

    /// <summary>Ktore wiersze wzmacniacz rysuje jako pasek w negatywie.</summary>
    public bool[] Paski { get; private set; } = new bool[0];

    public string Podpowiedzi { get; private set; } = "";
    public DateTime Kiedy { get; private set; }

    /// <summary>Caly ekran jako jeden tekst - do porownan i do dymka.</summary>
    public string Calosc => string.Join(Environment.NewLine, Wiersze);

    public static EkranSpe Rozbierz(IList<byte> dane, int od, int dlugosc)
    {
        if (dlugosc < Kolumn) return null;

        var e = new EkranSpe { Kiedy = DateTime.UtcNow };

        var wiersze = new List<string>();
        var paski = new List<bool>();

        for (int w = 0; w < Wierszy; w++)
        {
            int poczatek = od + w * Kolumn;
            int ile = Math.Min(Kolumn, od + dlugosc - poczatek);
            if (ile <= 0) { wiersze.Add(""); paski.Add(false); continue; }

            wiersze.Add(Tekst(dane, poczatek, ile));

            // Pasek tytulu wzmacniacz wypelnia znakiem 0x8D po obu stronach napisu.
            int wypelnien = 0;
            for (int i = 0; i < ile; i++)
                if (dane[poczatek + i] == Wypelnienie) wypelnien++;
            paski.Add(wypelnien >= 3);
        }

        e.Wiersze = wiersze.ToArray();
        e.Paski = paski.ToArray();

        int odPodpowiedzi = od + 224;
        if (od + dlugosc - odPodpowiedzi > 0)
            e.Podpowiedzi = Scisnij(Tekst(dane, odPodpowiedzi,
                                         Math.Min(96, od + dlugosc - odPodpowiedzi)));

        return e;
    }

    private const byte Wypelnienie = 0x8D;   // tlo paska tytulu
    private const byte Separator   = 0x8F;   // kreska miedzy polami

    private static string Tekst(IList<byte> dane, int od, int ile)
    {
        var znaki = new char[ile];
        for (int i = 0; i < ile; i++)
        {
            byte b = od + i < dane.Count ? dane[od + i] : (byte)0;

            if (b >= 0x10 && b <= 0x3F)      znaki[i] = (char)(b + 0x20);
            else if (b >= 0x40 && b <= 0x7E) znaki[i] = (char)b;
            else if (b == Separator)         znaki[i] = '|';
            else                             znaki[i] = ' ';
        }
        return new string(znaki);
    }

    /// <summary>Zbija ciagi spacji w jedna.</summary>
    private static string Scisnij(string tekst)
    {
        var wynik = new System.Text.StringBuilder(tekst.Length);
        bool kropka = false;

        foreach (char z in tekst)
        {
            if (z == ' ')
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
