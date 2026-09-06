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

    // Siatka znakow: 40 kolumn na 8 wierszy, zaczyna sie od piatego bajtu ramki.
    // Wyprowadzone z pomiaru: separatory kolumn stoja co 40 bajtow (58, 98, 138,
    // 178), a wypelnienia paskow tytulu trafiaja dokladnie w poczatek i koniec
    // wiersza wlasnie przy tym przesunieciu. Wynik zgadza sie ze zdjeciami ekranow
    // w instrukcji Experta 1.3K-FA co do znaku.
    public const int PoczatekSiatki = 5;
    public const int Kolumn = 40;
    public const int Wierszy = 8;

    // Za siatka ida jeszcze flagi kursora i dwubajtowa suma kontrolna.

    public string[] Wiersze { get; private set; } = new string[0];

    public string Podpowiedzi { get; private set; } = "";
    public DateTime Kiedy { get; private set; }

    /// <summary>Caly ekran jako jeden tekst - do porownan i do dymka.</summary>
    public string Calosc => string.Join(Environment.NewLine, Wiersze);

    public static EkranSpe Rozbierz(IList<byte> dane, int od, int dlugosc)
    {
        if (dlugosc < PoczatekSiatki + Kolumn) return null;

        var e = new EkranSpe { Kiedy = DateTime.UtcNow };

        var wiersze = new List<string>();

        for (int w = 0; w < Wierszy; w++)
        {
            int poczatek = od + PoczatekSiatki + w * Kolumn;
            int ile = Math.Min(Kolumn, od + dlugosc - poczatek);
            if (ile <= 0) { wiersze.Add(""); continue; }

            wiersze.Add(Tekst(dane, poczatek, ile));
        }

        e.Wiersze = wiersze.ToArray();

        // Ostatni wiersz to podpowiedzi klawiszy - przydaje sie osobno w dymku.
        if (e.Wiersze.Length == Wierszy) e.Podpowiedzi = Scisnij(e.Wiersze[Wierszy - 1]);

        return e;
    }

    // Zdjecia ekranow w instrukcji pokazuja, ze 0x8D to pozioma kreska obok tytulu,
    // a nie tlo w negatywie; 0x8F rozdziela kolumny.
    private const byte Kreska    = 0x8D;
    private const byte Separator = 0x8F;

    private static string Tekst(IList<byte> dane, int od, int ile)
    {
        var znaki = new char[ile];
        for (int i = 0; i < ile; i++)
        {
            byte b = od + i < dane.Count ? dane[od + i] : (byte)0;

            if (b >= 0x10 && b <= 0x3F)      znaki[i] = (char)(b + 0x20);
            else if (b >= 0x40 && b <= 0x7E) znaki[i] = (char)b;
            else if (b == Separator)         znaki[i] = (char)0x2502;   // pionowa kreska
            else if (b == Kreska)            znaki[i] = (char)0x2500;   // pozioma kreska
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
