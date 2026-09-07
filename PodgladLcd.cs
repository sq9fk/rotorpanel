namespace RotorPanel;

/// <summary>
/// Rysuje zawartosc wyswietlacza wzmacniacza SPE. Nie sklada wlasnego interfejsu
/// z rozebranych pol - pokazuje to, co wzmacniacz naprawde ma na ekranie, zeby
/// menu wygladalo tak samo jak na panelu.
///
/// Kazda komorka idzie z mapy bitowej: znaki z <see cref="CzcionkaSpe"/>, a logo,
/// kreski i ramki z <see cref="KafelkiSpe"/>. Zaznaczona pozycja idzie w negatywie.
/// Rysowanie tekstu czcionka systemowa dawalo obraz z dwoch swiatow - grafika
/// pikselowa, litery wygladzone i rozstrzelone, bo zaden krok czcionki nie pasuje
/// do szesciopikselowej komorki panelu.
///
/// Kafelki sa w natywnej rozdzielczosci wyswietlacza (6 na 8 pikseli) i powieksza
/// je <see cref="Skala"/> - calkowita, zeby kazdy piksel panelu byl kwadratem tej
/// samej wielkosci. Przy skali ulamkowej jednopikselowe kreski wychodzily raz
/// grubsze, raz ciensze, ukosne linie logo mialy przerwy, a belki liter siadaly
/// piksel za wysoko.
/// </summary>
public sealed class PodgladLcd : Control
{
    private static readonly Color TloEkranu = Color.FromArgb(0x10, 0x18, 0x14);
    private static readonly Color Litery    = Color.FromArgb(0xC8, 0xF5, 0xD0);
    private static readonly Color Uspione   = Color.FromArgb(0x60, 0x74, 0x68);

    private EkranSpe _ekran;
    private string _zastepczy = "";

    // Gotowe kafelki. Klucz laczy kod i to, czy rysujemy w negatywie.
    private readonly Dictionary<int, Bitmap> _pamiec = new();

    /// <summary>Ile pikseli ekranu na jeden piksel wyswietlacza.</summary>
    public const int Skala = 2;

    private const int SzerokoscZnaku  = KafelkiSpe.Szerokosc * Skala;
    private const int WysokoscWiersza = KafelkiSpe.Wysokosc * Skala;

    public PodgladLcd()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = TloEkranu;
        // Czcionka sluzy juz tylko do napisu zastepczego - tresc ekranu idzie z map bitowych.
        Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point);
    }

    /// <summary>Ekran do pokazania albo null, gdy nie ma swiezego odczytu.</summary>
    public EkranSpe Ekran
    {
        get => _ekran;
        set { _ekran = value; Invalidate(); }
    }

    /// <summary>Napis na srodku, gdy ekranu nie ma.</summary>
    public string Zastepczy
    {
        get => _zastepczy;
        set { _zastepczy = value ?? ""; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(TloEkranu);

        if (_ekran is null || _ekran.Bajty.Length == 0)
        {
            TextRenderer.DrawText(g, _zastepczy, Font, ClientRectangle, Uspione,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int szerokoscZnaku = SzerokoscZnaku;
        int wysokoscWiersza = WysokoscWiersza;

        // Bez wymuszania marginesu. Kontrolka o szerokosci rownej siatce dostawala
        // wczesniej dwa piksele z lewej, przez co prawa krawedz ramki wypadala poza
        // obraz i wygladalo to jak uciety ekran.
        int marginesX = Math.Max(0, (ClientSize.Width - szerokoscZnaku * EkranSpe.Kolumn) / 2);
        int marginesY = Math.Max(0, (ClientSize.Height - wysokoscWiersza * EkranSpe.Wierszy) / 2);

        using var pedzelZaznaczenia = new SolidBrush(Litery);

        for (int w = 0; w < _ekran.Bajty.Length; w++)
        {
            int y = marginesY + w * wysokoscWiersza;

            for (int k = 0; k < _ekran.Bajty[w].Length; k++)
            {
                byte bajt = _ekran.Bajty[w][k];

                int x = marginesX + k * szerokoscZnaku;

                bool zaznaczone = w < _ekran.Zaznaczone.Length &&
                                  k < _ekran.Zaznaczone[w].Length &&
                                  _ekran.Zaznaczone[w][k];

                if (zaznaczone)
                    g.FillRectangle(pedzelZaznaczenia, x, y, szerokoscZnaku, wysokoscWiersza);

                var kafelek = Kafelek(bajt, zaznaczone);
                if (kafelek is not null) g.DrawImageUnscaled(kafelek, x, y);
            }
        }
    }

    /// <summary>
    /// Gotowy obrazek komorki, powiekszony <see cref="Skala"/> razy, albo null,
    /// gdy kodu nie umiemy narysowac.
    /// </summary>
    private Bitmap Kafelek(byte kod, bool negatyw)
    {
        var mapa = Mapa(kod);
        if (mapa is null) return null;

        int klucz = kod | (negatyw ? 0x100 : 0);
        if (_pamiec.TryGetValue(klucz, out var gotowy)) return gotowy;

        var obraz = new Bitmap(SzerokoscZnaku, WysokoscWiersza);
        Color tusz = negatyw ? TloEkranu : Litery;

        for (int y = 0; y < KafelkiSpe.Wysokosc && y < mapa.Length; y++)
            for (int x = 0; x < KafelkiSpe.Szerokosc; x++)
            {
                if ((mapa[y] >> (KafelkiSpe.Szerokosc - 1 - x) & 1) == 0) continue;

                // Kazdy piksel panelu to kwadrat Skala na Skala - powiekszenie robimy
                // sami, bo GDI+ przy skalowaniu obrazu wygladzilby krawedzie.
                for (int py = 0; py < Skala; py++)
                    for (int px = 0; px < Skala; px++)
                        obraz.SetPixel(x * Skala + px, y * Skala + py, tusz);
            }

        _pamiec[klucz] = obraz;
        return obraz;
    }

    /// <summary>
    /// Mapa bitowa komorki: najpierw znaki wlasne wyswietlacza, potem czcionka.
    /// Kod znaku to bajt plus 0x20 - i to dla calego zakresu, nie tylko dla liter.
    /// </summary>
    private static byte[] Mapa(byte kod)
    {
        if (KafelkiSpe.Mapy.TryGetValue(kod, out var kafelek)) return kafelek;
        if (kod < CzcionkaSpe.Glify.Length) return CzcionkaSpe.Glify[kod];
        return null;
    }

    protected override void Dispose(bool zwalniamy)
    {
        if (zwalniamy)
        {
            foreach (var b in _pamiec.Values) b.Dispose();
            _pamiec.Clear();
        }
        base.Dispose(zwalniamy);
    }

}
