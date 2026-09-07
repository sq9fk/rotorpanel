namespace RotorPanel;

/// <summary>
/// Wymiary okna stanu policzone z rzeczywistych rozmiarow napisow.
///
/// Sztywne wspolrzedne w pikselach dzialaja tylko przy powiekszeniu 100%. Czcionki
/// podajemy w punktach, wiec przy 150% rosna o polowe, a wiersz wysokosci 18 px
/// obcinal tekst - na malym ekranie GPD widac to bylo najlepiej na ostatnim wierszu
/// tabeli. Dlatego kazda wysokosc bierze sie tu z <c>Font.Height</c>, a kazda
/// szerokosc z pomiaru najszerszego napisu, ktory w danej kolumnie moze wystapic.
///
/// Druga rzecz, ktora tu rozstrzygamy, to liczba kolumn tabeli. Czternascie wierszy
/// jedno pod drugim nie miesci sie na niskim ekranie, wiec gdy okno nie zmiesci sie
/// w obszarze roboczym, a szerokosc na to pozwala, tabela idzie na dwie kolumny.
/// Przewijanie zostaje jako ostatnia deska ratunku, nie jako sposob na codzienna prace.
/// </summary>
public sealed class UkladStanu
{
    public int Margines;        // odstep od krawedzi okna i miedzy kartami
    public int Wiersz;          // odstep miedzy kolejnymi wierszami tabeli
    public int WysokoscTekstu;  // wysokosc pojedynczej etykiety
    public int SzerokoscOpisu;  // kolumna z nazwa pola
    public int SzerokoscWartosci;
    public int Kolumn;          // 1 albo 2
    public int WierszyWKolumnie;
    public int SzerokoscKarty;
    public int SzerokoscOkna;
    public int WysokoscOkna;
    public int WysokoscNaglowka;
    public int WysokoscMiernikow;
    public int WysokoscTabeli;
    public int OdstepMiernika;  // pelna wysokosc jednego miernika z linijka
    public int WysokoscLinijki;

    /// <summary>Napisy, ktore moga sie pojawic w kolumnie wartosci - do pomiaru szerokosci.</summary>
    private static readonly string[] Przyklady =
    {
        "1.3K-FA", "Standby", "Operate", "160 m", "ANT 1", "Bank 2", "ANT 3 / ATU",
        "auto (dostrojony)", "RX ANT 2", "MAX (1300 W)", "1.5", "1.2", "45 °C", "—"
    };

    public static UkladStanu Policz(Font maly, Font zwykly, Font nazwa,
                                    string[] wiersze, Size obszarRoboczy, Size rama)
    {
        var u = new UkladStanu();

        int tekst = Math.Max(maly.Height, zwykly.Height);
        u.WysokoscTekstu = tekst;

        // Odstepy skalujemy tak samo jak tekst - inaczej przy 150% napisy sie zlewaja.
        u.Margines = Math.Max(12, tekst * 16 / 15);
        u.Wiersz = tekst + Math.Max(6, tekst / 3);
        u.WysokoscLinijki = Math.Max(14, tekst);

        u.SzerokoscOpisu = Zmierz(wiersze, maly) + tekst / 2;
        u.SzerokoscWartosci = Zmierz(Przyklady, zwykly) + tekst / 2;

        // Naglowek: tryb pracy, ostrzezenie i wiek odczytu, jedno pod drugim.
        u.WysokoscNaglowka = Math.Max(nazwa.Height, tekst) + 2 * maly.Height + 3 * (tekst / 3);

        // Miernik: podpis z odczytem w jednej linii, linijka pod spodem.
        u.OdstepMiernika = tekst + u.WysokoscLinijki + tekst;
        u.WysokoscMiernikow = 4 * u.OdstepMiernika + tekst / 2;

        // Jedna kolumna, jesli okno zmiesci sie w obszarze roboczym. Jesli nie -
        // dwie, o ile starczy szerokosci. Gdy i to nie pomoze, zostaje przewijanie.
        u.Kolumn = 1;
        Zloz(u, wiersze.Length);

        if (u.WysokoscOkna + rama.Height > obszarRoboczy.Height)
        {
            var dwie = new UkladStanu
            {
                Margines = u.Margines, Wiersz = u.Wiersz, WysokoscTekstu = u.WysokoscTekstu,
                SzerokoscOpisu = u.SzerokoscOpisu, SzerokoscWartosci = u.SzerokoscWartosci,
                WysokoscNaglowka = u.WysokoscNaglowka, WysokoscMiernikow = u.WysokoscMiernikow,
                OdstepMiernika = u.OdstepMiernika, WysokoscLinijki = u.WysokoscLinijki,
                Kolumn = 2
            };
            Zloz(dwie, wiersze.Length);

            if (dwie.SzerokoscOkna + rama.Width <= obszarRoboczy.Width) u = dwie;
        }

        return u;
    }

    /// <summary>Skleja wymiary kart i okna dla ustalonej liczby kolumn.</summary>
    private static void Zloz(UkladStanu u, int wierszy)
    {
        u.WierszyWKolumnie = (wierszy + u.Kolumn - 1) / u.Kolumn;
        u.WysokoscTabeli = u.WierszyWKolumnie * u.Wiersz + u.Margines;

        int kolumna = u.SzerokoscOpisu + u.SzerokoscWartosci;
        u.SzerokoscKarty = u.Kolumn * kolumna + (u.Kolumn + 1) * u.Margines;

        // Linijki miernikow potrzebuja swojego minimum, zeby czterdziesci segmentow
        // dalo sie jeszcze rozroznic.
        u.SzerokoscKarty = Math.Max(u.SzerokoscKarty, 40 * 8 + 2 * u.Margines);

        u.SzerokoscOkna = u.SzerokoscKarty + 2 * u.Margines;
        u.WysokoscOkna = u.Margines + u.WysokoscNaglowka
                       + u.Margines + u.WysokoscMiernikow
                       + u.Margines + u.WysokoscTabeli
                       + u.Margines;
    }

    private static int Zmierz(string[] napisy, Font czcionka)
    {
        int szerokosc = 0;
        foreach (string n in napisy)
            szerokosc = Math.Max(szerokosc, TextRenderer.MeasureText(n, czcionka).Width);
        return szerokosc;
    }

    /// <summary>Rama okna - potrzebna, zanim okno w ogole powstanie.</summary>
    public static Size RamaOkna() => new(
        2 * SystemInformation.FrameBorderSize.Width,
        2 * SystemInformation.FrameBorderSize.Height + SystemInformation.CaptionHeight);
}
