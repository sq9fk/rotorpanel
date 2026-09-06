namespace RotorPanel;

public partial class MainForm : Form
{
    private const int WysokoscKarty = 64;
    private const int Odstep        = 8;
    private const int GoraListy     = 64;
    private const int SzerokoscOkna = 580;

    private Config _cfg;
    private readonly List<Mostek> _mostki = new();
    private readonly Dictionary<int, Wiersz> _ui = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private Panel _lista;
    private Label _podtytul, _stopka;
    private Button _polacz, _rozlacz, _ustawienia, _pary;
    private readonly Dictionary<string, string> _opisMostka = new();

    private class Wiersz
    {
        public Mostek Mostek;
        public Led Dioda;
        public Label Stan;
        public Label Ruch;
        public Button Przelacz;
        public long PoprzedniLacznie;
        public DateTime PoprzedniCzas = DateTime.UtcNow;
        public double Szybkosc;
    }

    public MainForm(Config cfg)
    {
        _cfg = cfg;

        Text            = "Rotory";
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        Controls.Add(Ui.Etykieta("Anteny", Theme.Naglowek(), Theme.Tekst,
            new Point(22, 16), new Size(300, 24)));

        _podtytul = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(22, 40), new Size(520, 18));
        Controls.Add(_podtytul);

        _lista = new Panel
        {
            Location = new Point(20, GoraListy),
            Size = new Size(540, 100),
            AutoScroll = false,
            BackColor = Theme.Tlo
        };
        Controls.Add(_lista);

        _polacz = Ui.Przycisk("Połącz wszystkie", 134, glowny: true);
        _polacz.Click += (_, _) => { foreach (var m in _mostki) m.Start(); };
        Controls.Add(_polacz);

        _rozlacz = Ui.Przycisk("Rozłącz wszystkie", 134);
        _rozlacz.Click += (_, _) => { foreach (var m in _mostki) m.Stop(); };
        Controls.Add(_rozlacz);

        _ustawienia = Ui.Przycisk("Ustawienia…", 112);
        _ustawienia.Click += (_, _) => OtworzUstawienia();
        Controls.Add(_ustawienia);

        _pary = Ui.Przycisk("Pary COM…", 104);
        _pary.Click += (_, _) =>
        {
            using var okno = new PairsForm(_cfg, () => { foreach (var m in _mostki) m.Stop(); });
            okno.ShowDialog(this);
        };
        Controls.Add(_pary);

        _stopka = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(22, 0), new Size(536, 18));
        Controls.Add(_stopka);

        BudujListe();
        UtworzTray();

        // Przy pracy w zasobniku i autostarcie reczne klikanie po kazdym
        // uruchomieniu nie mialoby sensu.
        if (_cfg.AutoPolacz)
            foreach (var m in _mostki) m.Start();

        _timer.Interval = 700;
        _timer.Tick += (_, _) => Odswiez();
        _timer.Start();
    }

    private void OtworzUstawienia()
    {
        using var okno = new SettingsForm(_cfg);
        if (okno.ShowDialog(this) != DialogResult.OK) return;

        try { _cfg = Config.Wczytaj(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        BudujListe();
    }

    /// <summary>Okno dopasowuje wysokosc do liczby anten - lista nigdy sie nie przewija.</summary>
    private void BudujListe()
    {
        foreach (var m in _mostki) m.Dispose();
        _mostki.Clear();
        _ui.Clear();
        _lista.Controls.Clear();

        string zrodlo = string.IsNullOrWhiteSpace(_cfg.SterownikAnten)
            ? ""
            : "   ·   sterownik anten " + _cfg.SterownikAnten;
        _podtytul.Text = "ser2net na " + _cfg.PiIp + zrodlo;

        // Jeden mostek na pare portow - anteny na wspolnym maszcie dziela go.
        var wgPary = new Dictionary<string, Mostek>();
        int y = 0;

        foreach (var a in _cfg.Anteny.OrderBy(x => x.Nr))
        {
            Mostek mostek = null;
            if (a.Gotowa && !wgPary.TryGetValue(a.KluczPary, out mostek))
            {
                mostek = new Mostek(_cfg, a);
                wgPary[a.KluczPary] = mostek;
                _mostki.Add(mostek);
            }

            _lista.Controls.Add(BudujKarte(a, mostek, y));
            y += WysokoscKarty + Odstep;
        }

        _opisMostka.Clear();
        foreach (var grupa in _cfg.Anteny.Where(x => x.Gotowa).GroupBy(x => x.KluczPary))
            _opisMostka[grupa.Key] = string.Join(", ", grupa.Select(x => x.Etykieta));

        int wysokoscListy = Math.Max(y - Odstep, 0);
        _lista.Height = wysokoscListy;

        int yPrzyciski = GoraListy + wysokoscListy + 16;
        _polacz.Location     = new Point(20, yPrzyciski);
        _rozlacz.Location    = new Point(162, yPrzyciski);
        _ustawienia.Location = new Point(324, yPrzyciski);
        _pary.Location       = new Point(444, yPrzyciski);

        _stopka.Location = new Point(22, yPrzyciski + 40);
        ClientSize = new Size(SzerokoscOkna, yPrzyciski + 40 + 26);

        Odswiez();
    }

    private Karta BudujKarte(Antena a, Mostek m, int y)
    {
        var karta = new Karta
        {
            Location = new Point(0, y),
            Size = new Size(516, WysokoscKarty),
            Promien = 8
        };

        var dioda = new Led { Location = new Point(16, 22) };
        karta.Controls.Add(dioda);

        karta.Controls.Add(Ui.Etykieta(a.Nr + ".  " + a.Etykieta, Theme.Nazwa(), Theme.Tekst,
            new Point(44, 8), new Size(190, 20)));

        string strzalka = ((char)0x2192).ToString();
        string trasa;
        if (!a.MaRotor)     trasa = "bez rotora";
        else if (!a.Gotowa) trasa = "brak pary lub portu";
        else                trasa = $"{a.Com}  {strzalka}  {m.Adres}:{a.Port}";

        karta.Controls.Add(Ui.Etykieta(trasa, Theme.Maly(),
            a.MaRotor && !a.Gotowa ? Color.FromArgb(0xB3, 0x26, 0x1E) : Theme.TekstSzary,
            new Point(44, 30), new Size(190, 16)));

        var stan = Ui.Etykieta(a.Gotowa ? "zatrzymany" : "", Theme.Zwykly(), Theme.TekstSzary,
            new Point(238, 8), new Size(162, 20));
        stan.TextAlign = ContentAlignment.MiddleRight;
        karta.Controls.Add(stan);

        var ruch = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(228, 30), new Size(172, 16));
        ruch.TextAlign = ContentAlignment.MiddleRight;
        karta.Controls.Add(ruch);

        Button przelacz = null;
        if (a.Gotowa)
        {
            przelacz = Ui.Przycisk("Połącz", 92, glowny: true);
            przelacz.Location = new Point(408, 17);
            przelacz.Tag = m;
            przelacz.Click += (s, _) =>
            {
                var mostek = (Mostek)((Button)s).Tag;
                if (mostek.Stan == StanMostka.Zatrzymany) mostek.Start();
                else mostek.Stop();
                Odswiez();
            };
            karta.Controls.Add(przelacz);
        }

        _ui[a.Nr] = new Wiersz
        {
            Mostek = m, Dioda = dioda, Stan = stan, Ruch = ruch, Przelacz = przelacz
        };
        return karta;
    }

    private static string Bajty(long n)
    {
        if (n < 1024) return n + " B";
        if (n < 1024 * 1024) return (n / 1024.0).ToString("0.0") + " kB";
        return (n / (1024.0 * 1024.0)).ToString("0.0") + " MB";
    }

    private void Odswiez()
    {
        string ostatniBlad = "";
        string kropka = " " + ((char)0x00B7).ToString() + " ";

        foreach (var wpis in _ui)
        {
            var u = wpis.Value;
            var m = u.Mostek;
            if (m is null || u.Przelacz is null) continue;

            switch (m.Stan)
            {
                case StanMostka.Polaczony:
                    u.Dioda.Kolor = Theme.Zielony;
                    u.Stan.Text = "połączony";
                    u.Stan.ForeColor = Theme.Tekst;
                    UstawPrzycisk(u.Przelacz, "Rozłącz", glowny: false);
                    break;

                case StanMostka.Laczenie:
                    u.Dioda.Kolor = Theme.Pomarancz;
                    u.Stan.Text = "łączenie…";
                    u.Stan.ForeColor = Theme.Tekst;
                    UstawPrzycisk(u.Przelacz, "Rozłącz", glowny: false);
                    break;

                default:
                    u.Dioda.Kolor = Theme.Szary;
                    u.Stan.Text = "zatrzymany";
                    u.Stan.ForeColor = Theme.TekstSzary;
                    UstawPrzycisk(u.Przelacz, "Połącz", glowny: true);
                    break;
            }

            if (m.Stan == StanMostka.Zatrzymany)
            {
                u.Ruch.Text = "";
                u.PoprzedniLacznie = 0;
                u.Szybkosc = 0;
            }
            else
            {
                long lacznie = m.Rx + m.Tx;
                var teraz = DateTime.UtcNow;
                double sekundy = (teraz - u.PoprzedniCzas).TotalSeconds;
                if (sekundy >= 0.5)
                {
                    u.Szybkosc = (lacznie - u.PoprzedniLacznie) / sekundy;
                    u.PoprzedniLacznie = lacznie;
                    u.PoprzedniCzas = teraz;
                }

                u.Ruch.Text = "RX " + Bajty(m.Rx) + kropka + "TX " + Bajty(m.Tx) +
                              kropka + u.Szybkosc.ToString("0") + " B/s";
            }

            if (!string.IsNullOrEmpty(m.Blad)) ostatniBlad = m.Antena.Etykieta + ": " + m.Blad;
        }

        _stopka.Text = ostatniBlad;
        _stopka.ForeColor = string.IsNullOrEmpty(ostatniBlad)
            ? Theme.TekstSzary
            : Color.FromArgb(0xB3, 0x26, 0x1E);

        AktualizujTray();
    }

    private static void UstawPrzycisk(Button b, string tekst, bool glowny)
    {
        if (b is null || b.Text == tekst) return;
        b.Text = tekst;
        b.BackColor = glowny ? Theme.Akcent : Color.White;
        b.ForeColor = glowny ? Color.White : Theme.Tekst;
        b.FlatAppearance.BorderColor = glowny ? Theme.Akcent : Theme.Ramka;
        b.FlatAppearance.MouseOverBackColor = glowny ? Theme.AkcentCien : Color.FromArgb(0xF0, 0xF2, 0xF5);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Krzyzyk chowa program do zasobnika; wyjscie tylko przez menu ikony.
        if (e.CloseReason == CloseReason.UserClosing && !_naprawdeZamykam)
        {
            e.Cancel = true;
            UkryjDoZasobnika();
            return;
        }

        _timer.Stop();
        if (_tray is not null) _tray.Visible = false;
        foreach (var m in _mostki) m.Dispose();
        base.OnFormClosing(e);
    }
}
