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

public static class Ui
{
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
