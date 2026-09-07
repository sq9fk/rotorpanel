namespace RotorPanel;

/// <summary>
/// Rysuje zawartosc wyswietlacza wzmacniacza SPE. Nie sklada wlasnego interfejsu
/// z rozebranych pol - pokazuje to, co wzmacniacz naprawde ma na ekranie, zeby
/// menu wygladalo tak samo jak na panelu.
///
/// Zaznaczona pozycja idzie w negatywie, kreski poziome i pionowe tak, jak je
/// przysyla wzmacniacz. Wlasne znaki graficzne (logo i wykresy na ekranie glownym)
/// sa kafelkami mapy bitowej - kazdy bajt to inny wycinek obrazka. Rysowanie ich
/// jednym znakiem dawalo pole szumu, wiec zostawiamy tam puste miejsce.
/// </summary>
public sealed class PodgladLcd : Control
{
    private static readonly Color TloEkranu = Color.FromArgb(0x10, 0x18, 0x14);
    private static readonly Color Litery    = Color.FromArgb(0xC8, 0xF5, 0xD0);
    private static readonly Color Uspione   = Color.FromArgb(0x60, 0x74, 0x68);

    private const byte Kreska    = 0x8D;
    private const byte Trojnik   = 0x8E;
    private const byte Separator = 0x8F;

    private const byte RamkaGora     = 0x9F;
    private const byte RamkaDol      = 0xA0;
    private const byte RamkaBok      = 0xA1;
    private const byte RamkaRogGorny = 0xA2;
    private const byte RamkaRogDolny = 0xA3;

    // Kafelki, z ktorych wzmacniacz sklada logo na ekranie glownym.
    private const byte LogoOd = 0xB0;
    private const byte LogoDo = 0xDF;
    private const byte StrzalkaLewo = 0x99, StrzalkaGora = 0x9A;
    private const byte StrzalkaDol  = 0x9B, StrzalkaPrawo = 0x9C;
    private const byte Stopien      = 0xAA;

    private EkranSpe _ekran;
    private string _zastepczy = "";

    public PodgladLcd()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = TloEkranu;
        Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
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

        // Szerokosc znaku bierzemy z pomiaru, zeby kolumny stoly rowno niezaleznie
        // od tego, jaka czcionka o stalej szerokosci jest w systemie.
        var miara = TextRenderer.MeasureText(g, new string('0', 10), Font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        float szerokoscZnaku = miara.Width / 10f;
        int wysokoscWiersza = miara.Height + 1;

        int marginesX = Math.Max(4,
            (int)((ClientSize.Width - szerokoscZnaku * EkranSpe.Kolumn) / 2));
        int marginesY = Math.Max(2,
            (ClientSize.Height - wysokoscWiersza * EkranSpe.Wierszy) / 2);

        RysujLogo(g, marginesX, marginesY, szerokoscZnaku, wysokoscWiersza);

        using var pedzelZaznaczenia = new SolidBrush(Litery);

        for (int w = 0; w < _ekran.Bajty.Length; w++)
        {
            int y = marginesY + w * wysokoscWiersza;

            for (int k = 0; k < _ekran.Bajty[w].Length; k++)
            {
                byte bajt = _ekran.Bajty[w][k];
                int x = (int)(marginesX + k * szerokoscZnaku);

                bool zaznaczone = w < _ekran.Zaznaczone.Length &&
                                  k < _ekran.Zaznaczone[w].Length &&
                                  _ekran.Zaznaczone[w][k];

                if (zaznaczone)
                    g.FillRectangle(pedzelZaznaczenia, x, y,
                        (int)Math.Ceiling(szerokoscZnaku), wysokoscWiersza);

                char znak = Znak(bajt);
                if (znak == ' ') continue;

                Color kolor = zaznaczone ? TloEkranu : Litery;

                TextRenderer.DrawText(g, znak.ToString(), Font, new Point(x, y), kolor,
                    TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>
    /// Logo na ekranie glownym wzmacniacz sklada z kafelkow mapy bitowej i kazdy bajt
    /// to inny wycinek obrazka - bez zawartosci znakow nie da sie tego odtworzyc.
    /// Rysujemy wiec w tym miejscu wlasny znak zastepczy o tych samych wymiarach,
    /// zeby ekran mial ten sam uklad co panel.
    /// </summary>
    private void RysujLogo(Graphics g, int marginesX, int marginesY,
                           float szerokoscZnaku, int wysokoscWiersza)
    {
        int pierwszyW = int.MaxValue, ostatniW = -1;
        int pierwszaK = int.MaxValue, ostatniaK = -1;

        for (int w = 0; w < _ekran.Bajty.Length; w++)
            for (int k = 0; k < _ekran.Bajty[w].Length; k++)
            {
                byte b = _ekran.Bajty[w][k];
                if (b < LogoOd || b > LogoDo) continue;

                pierwszyW = Math.Min(pierwszyW, w);
                ostatniW  = Math.Max(ostatniW, w);
                pierwszaK = Math.Min(pierwszaK, k);
                ostatniaK = Math.Max(ostatniaK, k);
            }

        if (ostatniW < 0 || ostatniaK - pierwszaK < 4) return;

        var pole = new Rectangle(
            (int)(marginesX + pierwszaK * szerokoscZnaku) + 1,
            marginesY + pierwszyW * wysokoscWiersza + 1,
            (int)((ostatniaK - pierwszaK + 1) * szerokoscZnaku) - 2,
            (ostatniW - pierwszyW + 1) * wysokoscWiersza - 2);

        using var piso = new Pen(Litery, 1.6f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.DrawRectangle(piso, pole);

        using var czcionkaLogo = new Font("Segoe UI", pole.Height * 0.42f,
            FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "SPE", czcionkaLogo, pole, Litery,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
    }

    private static char Znak(byte b)
    {
        // Wyswietlacz przesyla ASCII pomniejszone o 0x20 - dla calego zakresu,
        // nie tylko dla wielkich liter.
        if (b >= 0x01 && b <= 0x5F) return (char)(b + 0x20);
        if (b == Separator)         return (char)0x2502;       // pionowa kreska
        if (b == Kreska)            return (char)0x2500;       // pozioma kreska
        if (b == Trojnik)           return (char)0x252C;       // trojnik
        if (b == RamkaGora)         return (char)0x2500;
        if (b == RamkaDol)          return (char)0x2500;
        if (b == RamkaBok)          return (char)0x2502;
        if (b == RamkaRogGorny)     return (char)0x2510;
        if (b == RamkaRogDolny)     return (char)0x2518;
        if (b == StrzalkaLewo)      return (char)0x25C0;
        if (b == StrzalkaGora)      return (char)0x25B2;
        if (b == StrzalkaDol)       return (char)0x25BC;
        if (b == StrzalkaPrawo)     return (char)0x25B6;
        if (b == Stopien)           return (char)0x00B0;
        return ' ';                                            // pusto albo kafelek grafiki
    }
}
