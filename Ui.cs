using System.Drawing.Drawing2D;

namespace RotorPanel;

/// <summary>Biala karta z zaokraglonymi rogami i delikatna ramka.</summary>
public class Karta : Panel
{
    public int Promien { get; set; } = 10;

    public Karta()
    {
        DoubleBuffered = true;
        BackColor = Theme.Tlo;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var sciezka = Theme.Zaokraglony(r, Promien);
        using var wypelnienie = new SolidBrush(Theme.Karta);
        using var pioro = new Pen(Theme.Ramka);
        g.FillPath(wypelnienie, sciezka);
        g.DrawPath(pioro, sciezka);
        base.OnPaint(e);
    }
}

/// <summary>Okragla dioda stanu z poswiata.</summary>
public class Led : Control
{
    private Color _kolor = Theme.Szary;

    public Color Kolor
    {
        get => _kolor;
        set { if (_kolor != value) { _kolor = value; Invalidate(); } }
    }

    public Led()
    {
        DoubleBuffered = true;
        Size = new Size(20, 20);
        BackColor = Theme.Karta;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Proporcjonalnie do rozmiaru - inaczej mniejsza dioda gubi wypelnienie.
        float margines = Math.Max(2f, Width * 0.22f);

        using (var poswiata = new SolidBrush(Color.FromArgb(64, _kolor)))
            g.FillEllipse(poswiata, 0, 0, Width - 1, Height - 1);
        using (var srodek = new SolidBrush(_kolor))
            g.FillEllipse(srodek, margines, margines,
                          Width - 1 - 2 * margines, Height - 1 - 2 * margines);
    }
}

/// <summary>Male oznaczenie w ksztalcie pigulki - numer nadajnika przy antenie.</summary>
public class Znacznik : Label
{
    public Color Tlo { get; set; } = Theme.Akcent;

    public Znacznik()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        AutoSize = false;
        ForeColor = Color.White;
        Font = Theme.Maly();
        BackColor = Theme.Karta;
        Size = new Size(42, 18);
        Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using (var sciezka = Theme.Zaokraglony(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
        using (var pedzel = new SolidBrush(Tlo))
            g.FillPath(pedzel, sciezka);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public static class Ui
{
    /// <summary>
    /// Mniejsze z dwojga: rozmiar tresci albo tyle, ile daje ekran. Gdy tresc sie nie
    /// miesci, okno dostaje paski przewijania zamiast wystawac poza krawedz.
    ///
    /// Okna maja polozenia kontrolek wpisane na sztywno, wiec na malym ekranie dolne
    /// przyciski Ustawien (1000 na 852 punkty) wychodzily pod krawedz pulpitu i nie
    /// dalo sie zapisac zmian - a przy oknie wyzszym niz ekran samo wysrodkowanie
    /// wypychalo jeszcze pasek tytulu nad gorna krawedz.
    /// </summary>
    public static void DopasujDoEkranu(Form okno, Size tresc)
    {
        okno.AutoScroll = true;
        okno.AutoScrollMinSize = tresc;

        // Bez uchwytu Screen.FromControl wymusilby jego utworzenie, a MainForm
        // startuje do zasobnika i nie chcemy tego przyspieszac.
        var obszar = (okno.IsHandleCreated
            ? Screen.FromControl(okno)
            : Screen.FromPoint(Cursor.Position)).WorkingArea;

        var rama = new Size(okno.Width - okno.ClientSize.Width,
                            okno.Height - okno.ClientSize.Height);

        okno.MaximumSize = new Size(tresc.Width + rama.Width, tresc.Height + rama.Height);
        okno.ClientSize = new Size(
            Math.Max(320, Math.Min(tresc.Width, obszar.Width - rama.Width)),
            Math.Max(240, Math.Min(tresc.Height, obszar.Height - rama.Height)));

        if (!okno.IsHandleCreated) return;

        // Po zmniejszeniu okno bywa juz wysrodkowane pod stary rozmiar - wciagamy je
        // z powrotem w obszar roboczy, zeby pasek tytulu zostal na widoku.
        okno.Location = new Point(
            Math.Max(obszar.Left, Math.Min(okno.Left, obszar.Right - okno.Width)),
            Math.Max(obszar.Top, Math.Min(okno.Top, obszar.Bottom - okno.Height)));
    }

    public static Button Przycisk(string tekst, int szerokosc, bool glowny = false)
    {
        var b = new Button
        {
            Text = tekst,
            Width = szerokosc,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.Zwykly(),
            Cursor = Cursors.Hand,
            BackColor = glowny ? Theme.Akcent : Color.White,
            ForeColor = glowny ? Color.White : Theme.Tekst
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = glowny ? Theme.Akcent : Theme.Ramka;
        b.FlatAppearance.MouseOverBackColor = glowny
            ? Theme.AkcentCien
            : Color.FromArgb(0xF0, 0xF2, 0xF5);
        return b;
    }

    public static Label Etykieta(string tekst, Font font, Color kolor, Point poz, Size rozmiar)
        => new()
        {
            Text = tekst,
            Font = font,
            ForeColor = kolor,
            Location = poz,
            Size = rozmiar,
            BackColor = Color.Transparent
        };
}
