namespace RotorPanel;

/// <summary>
/// Rysuje zawartosc wyswietlacza wzmacniacza SPE. Nie sklada wlasnego interfejsu
/// z rozebranych pol - pokazuje to, co wzmacniacz naprawde ma na ekranie, zeby
/// menu wygladalo tak samo jak na panelu.
///
/// Kreski poziome i pionowe rysujemy tak, jak przysyla je wzmacniacz - dzieki
/// temu ekran wyglada jak na zdjeciach w instrukcji.
/// </summary>
public sealed class PodgladLcd : Control
{
    private static readonly Color TloEkranu   = Color.FromArgb(0x10, 0x18, 0x14);
    private static readonly Color Litery      = Color.FromArgb(0xC8, 0xF5, 0xD0);
    private static readonly Color Uspione     = Color.FromArgb(0x60, 0x74, 0x68);

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

        if (_ekran is null)
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
            (ClientSize.Height - wysokoscWiersza * _ekran.Wiersze.Length) / 2);

        for (int w = 0; w < _ekran.Wiersze.Length; w++)
        {
            string tekst = _ekran.Wiersze[w];
            int y = marginesY + w * wysokoscWiersza;
            for (int k = 0; k < tekst.Length; k++)
            {
                if (tekst[k] == ' ') continue;

                TextRenderer.DrawText(g, tekst[k].ToString(), Font,
                    new Point((int)(marginesX + k * szerokoscZnaku), y),
                    Litery, TextFormatFlags.NoPadding);
            }
        }
    }
}
