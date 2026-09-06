using System.Drawing.Drawing2D;

namespace RotorPanel;

/// <summary>Paleta i pomocniki rysowania - jedno miejsce na zmiane wygladu.</summary>
public static class Theme
{
    public static readonly Color Tlo        = Color.FromArgb(0xF4, 0xF5, 0xF7);
    public static readonly Color Karta      = Color.White;
    public static readonly Color Ramka      = Color.FromArgb(0xE2, 0xE5, 0xEA);
    public static readonly Color Tekst      = Color.FromArgb(0x1F, 0x23, 0x28);
    public static readonly Color TekstSzary = Color.FromArgb(0x6E, 0x77, 0x81);
    public static readonly Color Zielony    = Color.FromArgb(0x2F, 0xBF, 0x71);
    public static readonly Color Pomarancz  = Color.FromArgb(0xF0, 0xA2, 0x02);
    public static readonly Color Szary      = Color.FromArgb(0xA8, 0xAE, 0xB6);
    public static readonly Color Akcent     = Color.FromArgb(0x2D, 0x6C, 0xDF);
    public static readonly Color AkcentCien = Color.FromArgb(0x24, 0x57, 0xB2);

    public static Font NaglowekDuzy() => new("Segoe UI Semibold", 16F);
    public static Font Naglowek()     => new("Segoe UI Semibold", 12F);
    public static Font Nazwa()    => new("Segoe UI Semibold", 11F);
    public static Font Zwykly()   => new("Segoe UI", 9F);
    public static Font Maly()     => new("Segoe UI", 8.5F);
    public static Font Mono()     => new("Consolas", 9F);

    public static GraphicsPath Zaokraglony(Rectangle r, int promien)
    {
        var p = new GraphicsPath();
        int d = promien * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
