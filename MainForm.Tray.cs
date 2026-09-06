using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RotorPanel;

/// <summary>Obsluga ikony w zasobniku: menu, stan, ukrywanie i zamykanie programu.</summary>
public partial class MainForm
{
    private NotifyIcon _tray;
    private ContextMenuStrip _menu;
    private bool _naprawdeZamykam;
    private bool _pierwszePokazanie = true;

    private static Icon _ikonaSzara, _ikonaPomarancz, _ikonaZielona;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr uchwyt);

    // ---------------------------------------------------------------- ikony

    /// <summary>
    /// Rysuje ikonke w stylu ikony programu: kompas z igla w kolorze stanu.
    /// </summary>
    private static Icon ZrobIkone(Color kolor)
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var tarcza = new SolidBrush(Color.FromArgb(31, 35, 40)))
                g.FillEllipse(tarcza, 0.6f, 0.6f, 30.8f, 30.8f);

            using (var obwod = new Pen(Color.FromArgb(62, 70, 78), 1.1f))
                g.DrawEllipse(obwod, 1.2f, 1.2f, 29.6f, 29.6f);

            g.TranslateTransform(16f, 16f);
            for (int k = 0; k < 4; k++)
            {
                Color odcien = k == 0
                    ? Color.FromArgb(226, 229, 234)
                    : Color.FromArgb(150, 158, 168);
                using (var znacznik = new SolidBrush(odcien))
                    g.FillRectangle(znacznik, -0.75f, -13.6f, 1.5f, k == 0 ? 4.2f : 2.8f);
                g.RotateTransform(90);
            }
            g.ResetTransform();

            g.TranslateTransform(16f, 16f);
            g.RotateTransform(35);
            using (var igla = new SolidBrush(kolor))
                g.FillPolygon(igla, new[]
                {
                    new PointF(0f, -12.6f),
                    new PointF(-7.4f, 5.6f),
                    new PointF(0f, 1.6f),
                    new PointF(7.4f, 5.6f)
                });
            g.ResetTransform();
        }

        var ikona = Icon.FromHandle(bmp.GetHicon());
        bmp.Dispose();
        return ikona;
    }

    private static Icon IkonaDla(StanMostka stan) => stan switch
    {
        StanMostka.Polaczony  => _ikonaZielona   ??= ZrobIkone(Theme.Zielony),
        StanMostka.Laczenie   => _ikonaPomarancz ??= ZrobIkone(Theme.Pomarancz),
        _                     => _ikonaSzara     ??= ZrobIkone(Theme.Szary)
    };

    // ---------------------------------------------------------------- zasobnik

    private void UtworzTray()
    {
        // Wymuszamy utworzenie uchwytu okna. Bez tego formularz, ktory nigdy nie byl
        // pokazany, nie zglasza HandleDestroyed i Close() nie konczy petli komunikatow.
        _ = Handle;

        _menu = new ContextMenuStrip { Font = Theme.Zwykly() };
        _menu.Opening += (_, _) => ZbudujMenu();

        // Menu pokazujemy sami w obsludze prawego przycisku - przypisanie
        // ContextMenuStrip do NotifyIcon reaguje niekonsekwentnie.
        _tray = new NotifyIcon
        {
            Icon = IkonaDla(StanMostka.Zatrzymany),
            Text = "RotorPanel",
            Visible = true
        };
        // Lewy przycisk przelacza panel: pokazuje albo chowa. Prawy rozwija menu.
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;

            if (Visible && WindowState != FormWindowState.Minimized)
                UkryjDoZasobnika();
            else
                PokazOkno();
        };

        _tray.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;

            // Bez wysuniecia okna na pierwszy plan menu nie znika po klikniecu obok.
            SetForegroundWindow(Handle);
            _menu.Show(Cursor.Position);
        };

        // Okno i pasek zadan dostaja wlasciwa ikone programu; w zasobniku
        // kolor igly niesie informacje o stanie, wiec tam zostaje dynamiczna.
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { Icon = _tray.Icon; }
    }

    private void ZbudujMenu()
    {
        _menu.Items.Clear();

        var otworz = new ToolStripMenuItem("Otwórz panel", null, (_, _) => PokazOkno())
        {
            Font = new Font(Theme.Zwykly(), FontStyle.Bold)
        };
        _menu.Items.Add(otworz);
        _menu.Items.Add(new ToolStripSeparator());

        if (_mostki.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("brak skonfigurowanych rotorów") { Enabled = false });
        }
        else
        {
            foreach (var m in _mostki)
            {
                string opis = _opisMostka.TryGetValue(m.Antena.KluczPary, out string o)
                    ? o
                    : m.Antena.Etykieta;

                string stan = m.Stan switch
                {
                    StanMostka.Polaczony => "połączony",
                    StanMostka.Laczenie  => "łączenie…",
                    _                    => "rozłączony"
                };

                var pozycja = new ToolStripMenuItem(opis + "   —   " + stan)
                {
                    Checked = m.Stan != StanMostka.Zatrzymany,
                    CheckOnClick = false,
                    Tag = m
                };
                pozycja.Click += (s, _) =>
                {
                    var mostek = (Mostek)((ToolStripMenuItem)s).Tag;
                    if (mostek.Stan == StanMostka.Zatrzymany) mostek.Start();
                    else mostek.Stop();
                    Odswiez();
                };
                _menu.Items.Add(pozycja);
            }

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Połącz wszystkie", null,
                (_, _) => { foreach (var m in _mostki) m.Start(); Odswiez(); }));
            _menu.Items.Add(new ToolStripMenuItem("Rozłącz wszystkie", null,
                (_, _) => { foreach (var m in _mostki) m.Stop(); Odswiez(); }));
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Zamknij", null, (_, _) => ZamknijNaprawde()));
    }

    /// <summary>Kolor ikony i podpowiedz odzwierciedlaja stan wszystkich mostkow.</summary>
    private void AktualizujTray()
    {
        if (_tray is null) return;

        int polaczone = _mostki.Count(m => m.Stan == StanMostka.Polaczony);
        bool laczenie = _mostki.Any(m => m.Stan == StanMostka.Laczenie);

        StanMostka zbiorczy = polaczone > 0 ? StanMostka.Polaczony
                            : laczenie     ? StanMostka.Laczenie
                            : StanMostka.Zatrzymany;

        var ikona = IkonaDla(zbiorczy);
        if (!ReferenceEquals(_tray.Icon, ikona))
        {
            _tray.Icon = ikona;
        }

        string podpis = _mostki.Count == 0
            ? "RotorPanel — brak rotorów"
            : "RotorPanel — " + polaczone + " z " + _mostki.Count + " połączonych";
        if (_tray.Text != podpis) _tray.Text = podpis;
    }

    // ---------------------------------------------------------------- okno

    /// <summary>Pierwsze pokazanie jest blokowane - program startuje do zasobnika.</summary>
    protected override void SetVisibleCore(bool value)
    {
        if (_pierwszePokazanie)
        {
            _pierwszePokazanie = false;
            base.SetVisibleCore(false);
            return;
        }
        base.SetVisibleCore(value);
    }

    /// <summary>Wywolywane przez druga uruchomiona kopie programu.</summary>
    public void PokazZZewnatrz() => PokazOkno();

    private void PokazOkno()
    {
        if (IsDisposed) return;

        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
    }

    private void UkryjDoZasobnika()
    {
        Hide();
    }

    /// <summary>
    /// Konczy program. Nie polegamy na Close(), bo formularz moze nie miec
    /// utworzonego uchwytu - Application.Exit domyka petle komunikatow niezawodnie.
    /// </summary>
    private void ZamknijNaprawde()
    {
        _naprawdeZamykam = true;

        _timer?.Stop();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        foreach (var m in _mostki) m.Dispose();
        _mostki.Clear();

        Application.Exit();
    }
}
