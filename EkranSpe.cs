namespace RotorPanel;

/// <summary>
/// Zawartosc wyswietlacza wzmacniacza SPE Expert, czytana z ramki 0x6A.
///
/// Producent tego nie opisuje - uklad ramki i kodowanie znakow pochodza z pracy
/// nad odtworzeniem protokolu w projekcie vu2cpl/macexpert-spe. Tryb podgladu
/// wlacza komenda RCU ON (0x80), wylacza RCU OFF (0x81).
///
/// Kodowanie znakow: wyswietlacz przesyla ASCII pomniejszone o 0x20, czyli
/// prawdziwy znak = bajt + 0x20 dla calego zakresu 0x01-0x5F. Stad male litery
/// (0x41-0x5A), kropka (0x0E) i myslnik (0x0D). Traktowanie 0x40-0x7E jako
/// zwyklego ASCII dawalo "SOLID STATE" zamiast "Solid State" i "20 M" zamiast
/// "20 m" - sprawdzone ze zdjeciem panelu.
/// Bajty od 0x60 w gore to wlasne znaki wyswietlacza.
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

    // Za siatka ida flagi kursora - po jednym bajcie na kolumne, a ustawiony bit
    // wskazuje wiersz. Zmierzone: nacisniecie strzalki przesuwa te bity o jeden
    // w gore albo w dol, a podpowiedz u dolu ekranu zmienia sie razem z nimi.
    public const int PoczatekFlag = PoczatekSiatki + Wierszy * Kolumn;

    public string[] Wiersze { get; private set; } = new string[0];

    /// <summary>Surowe bajty siatki - kontrolka sama decyduje, jak je narysowac.</summary>
    public byte[][] Bajty { get; private set; } = new byte[0][];

    /// <summary>Komorki, ktore wzmacniacz pokazuje w negatywie (zaznaczenie).</summary>
    public bool[][] Zaznaczone { get; private set; } = new bool[0][];

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
        e.Bajty = Siatka(dane, od, dlugosc);
        e.Zaznaczone = Zaznaczenia(dane, od, dlugosc);

        // Ostatni wiersz to podpowiedzi klawiszy - przydaje sie osobno w dymku.
        if (e.Wiersze.Length == Wierszy) e.Podpowiedzi = Scisnij(e.Wiersze[Wierszy - 1]);

        return e;
    }

    // Zdjecia ekranow w instrukcji pokazuja, ze 0x8D to pozioma kreska obok tytulu,
    // a nie tlo w negatywie; 0x8F rozdziela kolumny.
    private static byte[][] Siatka(IList<byte> dane, int od, int dlugosc)
    {
        var siatka = new byte[Wierszy][];
        for (int w = 0; w < Wierszy; w++)
        {
            siatka[w] = new byte[Kolumn];
            for (int k = 0; k < Kolumn; k++)
            {
                int i = od + PoczatekSiatki + w * Kolumn + k;
                siatka[w][k] = i < od + dlugosc && i < dane.Count ? dane[i] : (byte)0;
            }
        }
        return siatka;
    }

    private static bool[][] Zaznaczenia(IList<byte> dane, int od, int dlugosc)
    {
        var zaznaczone = new bool[Wierszy][];
        for (int w = 0; w < Wierszy; w++) zaznaczone[w] = new bool[Kolumn];

        for (int k = 0; k < Kolumn; k++)
        {
            int i = od + PoczatekFlag + k;
            if (i >= od + dlugosc || i >= dane.Count) break;

            byte flaga = dane[i];
            for (int w = 0; w < Wierszy; w++)
                zaznaczone[w][k] = (flaga & (1 << w)) != 0;
        }

        return zaznaczone;
    }

    private const byte Kreska    = 0x8D;   // pozioma
    private const byte Trojnik   = 0x8E;   // polaczenie poziomej z pionowa
    private const byte Separator = 0x8F;   // pionowa

    // Ramka wokol napisow na ekranie glownym - odczytane z ulozenia bajtow:
    // 0x9F biegnie gora, 0xA0 dolem, 0xA1 pionowo po prawej, a 0xA2 i 0xA3 to rogi.
    private const byte RamkaGora     = 0x9F;
    private const byte RamkaDol      = 0xA0;
    private const byte RamkaBok      = 0xA1;
    private const byte RamkaRogGorny = 0xA2;
    private const byte RamkaRogDolny = 0xA3;

    // Wlasne znaki wyswietlacza rozpoznane po bajtach w ramce: strzalki w podpowiedzi
    // klawiszy stoja parami miedzy nawiasami ([99 9A] i [9B 9C]), a 0xAA trafia sie
    // przed "C" przy temperaturze.
    private const byte StrzalkaLewo  = 0x99;
    private const byte StrzalkaGora   = 0x9A;
    private const byte StrzalkaDol    = 0x9B;
    private const byte StrzalkaPrawo  = 0x9C;
    private const byte Stopien        = 0xAA;

    private static string Tekst(IList<byte> dane, int od, int ile)
    {
        var znaki = new char[ile];
        for (int i = 0; i < ile; i++)
        {
            byte b = od + i < dane.Count ? dane[od + i] : (byte)0;

            if (b >= 0x01 && b <= 0x5F)      znaki[i] = (char)(b + 0x20);
            else if (b == Separator)         znaki[i] = (char)0x2502;
            else if (b == Kreska)            znaki[i] = (char)0x2500;
            else if (b == Trojnik)           znaki[i] = (char)0x252C;
            else if (b == RamkaGora)         znaki[i] = (char)0x2500;
            else if (b == RamkaDol)          znaki[i] = (char)0x2500;
            else if (b == RamkaBok)          znaki[i] = (char)0x2502;
            else if (b == RamkaRogGorny)     znaki[i] = (char)0x2510;
            else if (b == RamkaRogDolny)     znaki[i] = (char)0x2518;
            else if (b == StrzalkaLewo)      znaki[i] = (char)0x25C0;
            else if (b == StrzalkaGora)      znaki[i] = (char)0x25B2;
            else if (b == StrzalkaDol)       znaki[i] = (char)0x25BC;
            else if (b == StrzalkaPrawo)     znaki[i] = (char)0x25B6;
            else if (b == Stopien)           znaki[i] = (char)0x00B0;
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
