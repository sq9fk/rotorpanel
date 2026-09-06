namespace RotorPanel;

public partial class MainForm : Form
{
    private const int WysokoscKarty = 64;
    private const int Odstep        = 8;
    private const int GoraListy     = 68;
    private const int SzerokoscOkna = 580;

    private Config _cfg;
    private readonly List<Mostek> _mostki = new();
    private readonly Dictionary<int, Wiersz> _ui = new();
    private readonly Dictionary<string, string> _opisMostka = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ToolTip _dymek = new();

    private Panel _lista;
    private Label _stopka, _opisSer2net, _opisSterownika;
    private Led _diodaSer2net, _diodaSterownika;
    private Button _polacz, _rozlacz, _ustawienia, _pary;

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
            new Point(22, 16), new Size(240, 24)));

        _opisSer2net    = EtykietaStanu(new Point(272, 16));
        _diodaSer2net   = DiodaStanu(new Point(520, 17));
        _opisSterownika = EtykietaStanu(new Point(272, 38));
        _diodaSterownika = DiodaStanu(new Point(520, 39));

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

        if (_cfg.AutoPolacz)
            foreach (var m in _mostki) m.Start();

        _timer.Interval = 700;
        _timer.Tick += (_, _) => Odswiez();
        _timer.Start();
    }

    private Label EtykietaStanu(Point poz)
    {
        var e = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary, poz, new Size(240, 18));
        e.TextAlign = ContentAlignment.MiddleRight;
        Controls.Add(e);
        return e;
    }

    private Led DiodaStanu(Point poz)
    {
        var d = new Led { Location = poz, Size = new Size(16, 16), BackColor = Theme.Tlo };
        Controls.Add(d);
        return d;
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

        // Jeden mostek na rotor - anteny wskazujace ten sam rotor dziela polaczenie.
        var wgRotora = new Dictionary<int, Mostek>();
        int y = 0;

        foreach (var a in _cfg.Anteny.OrderBy(x => x.Nr))
        {
            var rotor = _cfg.RotorAnteny(a);
            Mostek mostek = null;

            if (rotor != null && !wgRotora.TryGetValue(rotor.Nr, out mostek))
            {
                mostek = new Mostek(_cfg, rotor);
                wgRotora[rotor.Nr] = mostek;
                _mostki.Add(mostek);
            }

            _lista.Controls.Add(BudujKarte(a, rotor, mostek, y));
            y += WysokoscKarty + Odstep;
        }

        _opisMostka.Clear();
        foreach (var grupa in _cfg.Anteny.Where(x => _cfg.RotorAnteny(x) != null)
                                         .GroupBy(x => _cfg.RotorAnteny(x).KluczPary))
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

        ZerujStanySieci();
        Odswiez();
    }
}
