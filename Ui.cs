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

/// <summary>
/// Linijka segmentowa - miernik w stylu paska LED. Rysuje wypelnienie w segmentach,
/// bo ciagly pasek przy zmiennej mocy migocze i trudno z niego cokolwiek odczytac,
/// a segmenty daja podzialke i widac, gdzie stoi wskazanie.
///
/// Trzyma szczyt przez sekunde i pol - moc przy nadawaniu skacze w takt modulacji,
/// wiec bez tego nie da sie zobaczyc wartosci szczytowej.
/// </summary>
public sealed class PasekLed : Control
{
    private const int Segmentow = 40;
    private static readonly TimeSpan TrwanieSzczytu = TimeSpan.FromMilliseconds(1500);

    private double _wartosc, _maksimum = 100, _szczyt;
    private DateTime _kiedySzczyt = DateTime.MinValue;

    public PasekLed()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Karta;
        Height = 14;
    }

    public double Maksimum
    {
        get => _maksimum;
        set { _maksimum = value > 0 ? value : 1; Invalidate(); }
    }

    /// <summary>Ile procent skali liczymy juz za duzo - powyzej segmenty ida na czerwono.</summary>
    public double ProgCzerwony { get; set; } = 0.90;

    public double ProgPomaranczowy { get; set; } = 0.75;

    public double Wartosc
    {
        get => _wartosc;
        set
        {
            if (Math.Abs(_wartosc - value) < 0.0001 && _szczyt <= _wartosc) return;
            _wartosc = value;

            if (value >= _szczyt || DateTime.UtcNow - _kiedySzczyt > TrwanieSzczytu)
            {
                _szczyt = value;
                _kiedySzczyt = DateTime.UtcNow;
            }
            Invalidate();
        }
    }

    /// <summary>Zeruje wskazanie i szczyt - po rozlaczeniu nie ma czego pokazywac.</summary>
    public void Wyczysc()
    {
        _wartosc = _szczyt = 0;
        _kiedySzczyt = DateTime.MinValue;
        Invalidate();
    }

    private Color KolorSegmentu(int nr)
    {
        double udzial = (nr + 1.0) / Segmentow;
        if (udzial > ProgCzerwony) return Color.FromArgb(0xD9, 0x3B, 0x2B);
        if (udzial > ProgPomaranczowy) return Theme.Pomarancz;
        return Theme.Zielony;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        double udzial = Math.Max(0, Math.Min(1, _wartosc / _maksimum));
        int zapalonych = (int)Math.Round(udzial * Segmentow);

        // Szczyt gasnie sam, zeby nie zostawal na ekranie po zakonczeniu nadawania.
        if (DateTime.UtcNow - _kiedySzczyt > TrwanieSzczytu) _szczyt = _wartosc;
        int segmentSzczytu = (int)Math.Round(
            Math.Max(0, Math.Min(1, _szczyt / _maksimum)) * Segmentow) - 1;

        float szerokosc = (float)Width / Segmentow;
        for (int i = 0; i < Segmentow; i++)
        {
            bool zapalony = i < zapalonych;
            bool szczyt = i == segmentSzczytu && segmentSzczytu >= zapalonych;
            if (!zapalony && !szczyt)
            {
                using var zgaszony = new SolidBrush(Color.FromArgb(0x38, Theme.Szary));
                g.FillRectangle(zgaszony, i * szerokosc, 0, szerokosc - 1, Height);
                continue;
            }

            using var pedzel = new SolidBrush(KolorSegmentu(i));
            g.FillRectangle(pedzel, i * szerokosc, 0, szerokosc - 1, Height);
        }
    }
}

/// <summary>
/// Okna wzmacniacza - sterowanie i stan - sa **niezalezne od okna glownego**: bez
/// wlasciciela, w pasku zadan, otwierane przez Show, nie ShowDialog. Dzieki temu
/// panel mozna schowac do zasobnika i zostawic je na widoku, a one dalej odswiezaja
/// sie z mostka. Wczesniej byly modalne: blokowaly panel i znikaly razem z nim.
///
/// Rejestr pilnuje, zeby na jeden mostek przypadalo jedno okno danego rodzaju -
/// drugie klikniecie przywraca to otwarte zamiast mnozyc kopie - i pozwala je
/// pozamykac, gdy mostki sa przebudowywane albo gdy do pary wepnie sie klient.
/// </summary>
public static class OknaMostka
{
    private static readonly List<(Mostek Mostek, Form Okno)> _otwarte = new();

    /// <summary>Pokazuje okno albo przywraca juz otwarte dla tego mostka.</summary>
    public static void Pokaz<T>(Mostek mostek, Func<T> utworz) where T : Form
    {
        var istniejace = _otwarte.FirstOrDefault(
            o => ReferenceEquals(o.Mostek, mostek) && o.Okno is T && !o.Okno.IsDisposed);

        if (istniejace.Okno is not null)
        {
            if (istniejace.Okno.WindowState == FormWindowState.Minimized)
                istniejace.Okno.WindowState = FormWindowState.Normal;
            istniejace.Okno.Activate();
            return;
        }

        var okno = utworz();

        // Kaskada, zeby drugie okno nie stanelo dokladnie na pierwszym.
        if (_otwarte.Count > 0 && okno.StartPosition == FormStartPosition.CenterScreen)
        {
            okno.StartPosition = FormStartPosition.Manual;
            var srodek = Screen.FromPoint(Cursor.Position).WorkingArea;
            okno.Location = new Point(
                srodek.X + (srodek.Width - okno.Width) / 2 + _otwarte.Count * 28,
                srodek.Y + (srodek.Height - okno.Height) / 2 + _otwarte.Count * 28);
        }

        _otwarte.Add((mostek, okno));
        okno.FormClosed += (_, _) => _otwarte.RemoveAll(o => ReferenceEquals(o.Okno, okno));
        okno.Show();
    }

    /// <summary>Zamyka okna danego rodzaju dla tego mostka; null - dla wszystkich.</summary>
    public static void Zamknij<T>(Mostek mostek = null) where T : Form
    {
        foreach (var (m, okno) in _otwarte.ToArray())
        {
            if (okno is not T || okno.IsDisposed) continue;
            if (mostek is not null && !ReferenceEquals(m, mostek)) continue;
            try { okno.Close(); } catch { /* zamykane gdzie indziej */ }
        }
    }

    /// <summary>Zamyka wszystkie - mostki wlasnie znikaja i okna nie maja co pokazywac.</summary>
    public static void ZamknijWszystkie()
    {
        foreach (var (_, okno) in _otwarte.ToArray())
            if (!okno.IsDisposed)
                try { okno.Close(); } catch { /* jak wyzej */ }
        _otwarte.Clear();
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
