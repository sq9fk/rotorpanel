namespace RotorPanel;

/// <summary>
/// Rysuje zawartosc wyswietlacza wzmacniacza SPE. Nie sklada wlasnego interfejsu
/// z rozebranych pol - pokazuje to, co wzmacniacz naprawde ma na ekranie, zeby
/// menu wygladalo tak samo jak na panelu.
///
/// Zaznaczona pozycja idzie w negatywie, kreski poziome i pionowe tak, jak je
/// przysyla wzmacniacz. Wlasne znaki graficzne (logo, wykresy na ekranie glownym)
/// sa kafelkami mapy bitowej - tych nie da sie odczytac jako tekst, wiec pokazujemy
/// w ich miejscu delikatna teksture.
/// </summary>
public sealed class PodgladLcd : Control
{
    private static readonly Color TloEkranu = Color.FromArgb(0x10, 0x18, 0x14);
    private static readonly Color Litery    = Color.FromArgb(0xC8, 0xF5, 0xD0);
    private static readonly Color KolorGrafiki = Color.FromArgb(0x35, 0x52, 0x40);
    private static readonly Color Uspione   = Color.FromArgb(0x60, 0x74, 0x68);

    private const byte Kreska    = 0x8D;
    private const byte Separator = 0x8F;

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

                Color kolor = zaznaczone ? TloEkranu
                            : CzyGrafika(bajt) ? KolorGrafiki
                            : Litery;

                TextRenderer.DrawText(g, znak.ToString(), Font, new Point(x, y), kolor,
                    TextFormatFlags.NoPadding);
            }
        }
    }

    private static bool CzyGrafika(byte b)
        => b != Kreska && b != Separator && b > 0x7E;

    private static char Znak(byte b)
    {
        if (b >= 0x10 && b <= 0x3F) return (char)(b + 0x20);   // znak z atrybutem
        if (b >= 0x40 && b <= 0x7E) return (char)b;
        if (b == Separator)         return (char)0x2502;       // pionowa kreska
        if (b == Kreska)            return (char)0x2500;       // pozioma kreska
        if (b > 0x7E)               return (char)0x2591;       // kafelek grafiki
        return ' ';
    }
}
