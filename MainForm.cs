namespace RotorPanel;

public partial class MainForm : Form
{
    private const int WysokoscKarty = 72;

    // Karta wzmacniacza jest wyzsza: miesci linijke mocy nadawania i trzeci przycisk.
    private const int WysokoscKartySpe = 112;
    private const int Odstep        = 8;
    private const int GoraListy     = 68;
    private const int SzerokoscOkna = 580;
    private const int SzerokoscListy = 540;

    // Odleglosc miedzy poczatkami sasiednich kolumn listy.
    private const int OdstepKolumn = SzerokoscListy + 16;

    // Wysokosc tego, co jest pod lista: przyciski i stopka.
    private const int PodLista = 16 + 40 + 26;

    private Config _cfg;
    private readonly List<Mostek> _mostki = new();
    private readonly Dictionary<string, Wiersz> _ui = new();
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
        public Button Sterowanie;
        public Button PrzyciskStanu;
        public PasekLed Moc;
        public Label OpisMocy;
        public Znacznik[] Trx;
        public Label Spe;
        public Znacznik Klopot;
        public string Trasa = "";
        public long PoprzedniLacznie;
        public DateTime PoprzedniCzas = DateTime.UtcNow;
        public double Szybkosc;
    }

    /// <summary>Rozmiar, jakiego okno potrzebuje na cala tresc - bez wzgledu na ekran.</summary>
    private Size _trescOkna;

    /// <summary>Karty listy w kolejnosci, we wspolrzednych ukladu (100%).</summary>
    private readonly List<Control> _karty = new();
    private int _kolumn = 1;

    /// <summary>Obszar roboczy uzyty do wyboru liczby kolumn - patrz <see cref="IleKolumn"/>.</summary>
    private readonly Size? _obszarRoboczy;

    /// <param name="obszarRoboczy">
    /// Rozmiar obszaru roboczego ekranu. Podawany wprost tylko przy sprawdzaniu ukladu
    /// dla ekranow, ktorych akurat nie ma pod reka.
    /// </param>
    public MainForm(Config cfg, Size? obszarRoboczy = null)
    {
        _cfg = cfg;
        _obszarRoboczy = obszarRoboczy;

        Text            = "Rotory";
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        Controls.Add(Ui.Etykieta("Rotory", Theme.NaglowekDuzy(), Theme.Tekst,
            new Point(22, 12), new Size(240, 32)));

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

        // Uklad jest skladany we wspolrzednych dla 100%; tu dostaje rozmiar biezacego
        // ekranu. Musi isc **przed** BudujListe, bo lista dokladana pozniej skaluje sie
        // osobno - inaczej karty zbudowane w konstruktorze dostalyby skale dwa razy.
        Ui.SkalujPodEkran(this);

        BudujListe();
        UtworzTray();

        // Program startuje do zasobnika, wiec przy budowaniu listy nie wiadomo jeszcze,
        // na ktorym monitorze okno sie pokaze. Przy pokazaniu wiadomo.
        Shown += (_, _) => Ui.DopasujDoEkranu(this, _trescOkna, mozeRosnac: true);
        Resize += (_, _) => PrzeliczKolumny();

        if (_cfg.AutoPolacz)
            foreach (var m in _mostki) m.Start();

        _timer.Interval = 700;
        _timer.Tick += (_, _) => Odswiez();
        _timer.Start();

        SprawdzAktualizacjeWTle();
    }

    /// <summary>Punkt z ukladu 100% przeliczony na piksele biezacego ekranu.</summary>
    private Point Poz(int x, int y) => new(Ui.Px(this, x), Ui.Px(this, y));

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

    /// <summary>Okno dopasowuje wysokosc do zawartosci - lista nigdy sie nie przewija.</summary>
    private void BudujListe()
    {
        // Okna sterowania i stanu trzymaja mostek, ktory za chwile zniknie.
        OknaMostka.ZamknijWszystkie();

        // Rownolegle, jak przy zamykaniu programu. Mostek.Stop czeka do 2,5 s na
        // zakonczenie petli, wiec sekwencyjnie przy kilku mostkach zapis ustawien
        // potrafil zamrozic okno na kilkanascie sekund.
        RozlaczWszystko();
        _ui.Clear();
        _lista.Controls.Clear();

        // Karty powstaja we wspolrzednych dla 100% i w jednej kolumnie; dopiero pozniej
        // trafiaja do kolumn i dostaja skale ekranu.
        var pozycje = new List<Control>();
        int y = 0;
        y = BudujAnteny(y, pozycje);
        y = BudujUrzadzenia(y, pozycje);

        _karty.Clear();
        _karty.AddRange(pozycje);

        float skala = Ui.Skala(this);
        foreach (var karta in pozycje)
        {
            Ui.Skaluj(karta, skala);
            _lista.Controls.Add(karta);
        }

        _opisMostka.Clear();
        foreach (var grupa in _cfg.Anteny.Where(x => _cfg.RotorAnteny(x) != null)
                                         .GroupBy(x => _cfg.RotorAnteny(x).KluczPary))
            _opisMostka[grupa.Key] = string.Join(", ", grupa.Select(x => x.Etykieta));

        UlozListe(IleKolumn(MiejsceNaEkranie()));

        // Lista rosnie z liczba rotorow i urzadzen, wiec przy kilku pozycjach okno
        // potrafi byc wyzsze niz ekran. Wtedy dostaje paski przewijania zamiast chowac
        // przyciski pod krawedzia pulpitu.
        // Okno wolno rozciagnac: lista sama przeklada karty na wiecej kolumn (PrzeliczKolumny).
        Ui.DopasujDoEkranu(this, _trescOkna, mozeRosnac: true);

        ZerujStanySieci();
        Odswiez();
    }

    /// <summary>Miejsce dla okna - obszar roboczy ekranu, w jednostkach ukladu.</summary>
    private Size MiejsceNaEkranie()
    {
        var ekran = _obszarRoboczy ?? (IsHandleCreated
            ? Screen.FromControl(this)
            : Screen.FromPoint(Cursor.Position)).WorkingArea.Size;

        float skala = Ui.Skala(this);
        // Zapas na ramke okna i pasek tytulu.
        return new Size((int)(ekran.Width / skala) - 16, (int)(ekran.Height / skala) - 48);
    }

    /// <summary>
    /// Uklada karty w kolumnach i przesuwa wszystko, co pod nimi. Wywolywane przy
    /// budowaniu listy i przy zmianie rozmiaru okna.
    /// </summary>
    private void UlozListe(int kolumn)
    {
        _kolumn = kolumn;
        int wysokoscListy = RozlozWKolumnach(_karty, kolumn, Ui.Skala(this));

        _lista.Size = new Size(Ui.Px(this, (kolumn - 1) * OdstepKolumn + SzerokoscListy),
                               wysokoscListy);

        int yPrzyciski = GoraListy + (int)(wysokoscListy / Ui.Skala(this)) + 16;
        _polacz.Location     = Poz(20, yPrzyciski);
        _rozlacz.Location    = Poz(162, yPrzyciski);
        _ustawienia.Location = Poz(324, yPrzyciski);
        _pary.Location       = Poz(444, yPrzyciski);
        _stopka.Location     = Poz(22, yPrzyciski + 40);

        _trescOkna = Ui.Px(this, new Size(SzerokoscOkna + (kolumn - 1) * OdstepKolumn,
                                          yPrzyciski + 40 + 26));
        AutoScrollMinSize = _trescOkna;
    }

    /// <summary>
    /// Przy zmianie rozmiaru okna lista dobiera liczbe kolumn do tego, co widac. Dzieki
    /// temu rozciagniecie okna w bok cos daje - bez tego uchwyt zmiany rozmiaru byl
    /// ozdoba, bo tresc i tak zostawala w jednej kolumnie.
    /// </summary>
    private bool _ukladamListe;

    private void PrzeliczKolumny()
    {
        if (_karty.Count == 0 || WindowState == FormWindowState.Minimized) return;
        if (_ukladamListe) return;

        // Miejsce liczymy tak, jakby paskow przewijania nie bylo. Inaczej decyzja zalezy
        // od paska, ktory sama wywoluje: dwie kolumny wystawialy pionowy pasek, ten zabieral
        // 17 px szerokosci, po czym przy pierwszej zmianie rozmiaru brakowalo tych kilkunastu
        // pikseli i uklad wracal do jednej kolumny, juz na stale.
        float skala = Ui.Skala(this);
        int szerokosc = ClientSize.Width +
            (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
        int wysokosc = ClientSize.Height +
            (HorizontalScroll.Visible ? SystemInformation.HorizontalScrollBarHeight : 0);

        var miejsce = new Size((int)(szerokosc / skala), (int)(wysokosc / skala));
        int kolumn = IleKolumn(miejsce);
        if (kolumn == _kolumn) return;

        _ukladamListe = true;
        try { UlozListe(kolumn); }
        finally { _ukladamListe = false; }
    }

    /// <summary>
    /// Ile kolumn potrzeba, zeby lista zmiescila sie w pionie.
    ///
    /// Lista rosnie z liczba anten i urzadzen, a przy powiekszeniu 150% rosnie jeszcze raz -
    /// na niskim ekranie (GPD ma 768 px wysokosci) jedna kolumna nie ma szans. Szerokosci
    /// za to jest tam az nadto, wiec zamiast kazac przewijac, uklada sie karty obok siebie.
    /// Liczymy w jednostkach ukladu (100%), bo takie sa wszystkie stale w tym pliku.
    /// </summary>
    private int IleKolumn(Size miejsce)
    {
        int kolumn = 1;
        while (kolumn < 4)
        {
            if (GoraListy + WysokoscUkladu(kolumn) + PodLista <= miejsce.Height) break;
            if (SzerokoscOkna + kolumn * OdstepKolumn > miejsce.Width) break;
            kolumn++;
        }
        return kolumn;
    }

    /// <summary>Wysokosc listy w jednostkach ukladu, gdyby ulozyc ja w tylu kolumnach.</summary>
    private int WysokoscUkladu(int kolumn)
        => Rozloz(Wysokosci(), kolumn, null, 1f);

    /// <summary>Ustawia karty w kolumnach i zwraca wysokosc najwyzszej, w pikselach.</summary>
    private static int RozlozWKolumnach(List<Control> karty, int kolumn, float skala)
        => Rozloz(karty.Select(k => (int)Math.Round(k.Height / skala)).ToList(), kolumn, karty, skala);

    /// <summary>Wysokosci kart w jednostkach ukladu - same kontrolki sa juz przeskalowane.</summary>
    private List<int> Wysokosci()
    {
        float skala = Ui.Skala(this);
        return _karty.Select(k => (int)Math.Round(k.Height / skala)).ToList();
    }

    /// <summary>
    /// Wspolna arytmetyka dla pytania "ile to zajmie" i dla samego ukladania. Kolejnosc
    /// pozycji zostaje zachowana: kolumna zbiera karty po kolei, dopoki nie przekroczy
    /// swojego przydzialu wysokosci.
    /// </summary>
    private static int Rozloz(List<int> wysokosci, int kolumn, List<Control> pozycje, float skala)
    {
        int lacznie = wysokosci.Sum() + Math.Max(0, wysokosci.Count - 1) * Odstep;
        int przydzial = kolumn > 1 ? lacznie / kolumn : int.MaxValue;

        int kolumna = 0, y = 0, najwyzsza = 0;
        for (int i = 0; i < wysokosci.Count; i++)
        {
            // Nowa kolumna dopiero wtedy, gdy biezaca ma juz swoj przydzial - i tylko
            // jesli zostalo jeszcze miejsce na kolumny.
            if (y > 0 && y >= przydzial && kolumna < kolumn - 1)
            {
                kolumna++;
                y = 0;
            }

            if (pozycje != null)
                pozycje[i].Location = new Point((int)Math.Round(kolumna * OdstepKolumn * skala),
                                                (int)Math.Round(y * skala));
            y += wysokosci[i] + Odstep;
            najwyzsza = Math.Max(najwyzsza, y - Odstep);
        }
        return (int)Math.Round(Math.Max(najwyzsza, 0) * skala);
    }

    private int BudujAnteny(int y, List<Control> pozycje)
    {
        // Jeden mostek na rotor - anteny wskazujace ten sam rotor dziela polaczenie.
        var wgRotora = new Dictionary<int, Mostek>();

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

            pozycje.Add(BudujKarteAnteny(a, rotor, mostek, y));
            y += WysokoscKarty + Odstep;
        }

        return y;
    }

    private int BudujUrzadzenia(int y, List<Control> pozycje)
    {
        var urzadzenia = _cfg.Urzadzenia.Where(u => u.Gotowy).OrderBy(u => u.Nr).ToList();
        if (urzadzenia.Count == 0) return y;

        var naglowek = Ui.Etykieta("Urządzenia", Theme.Nazwa(), Theme.Tekst,
            new Point(2, y), new Size(300, 26));
        pozycje.Add(naglowek);
        y += 26 + Odstep;

        foreach (var u in urzadzenia)
        {
            var mostek = new Mostek(_cfg, u);
            _mostki.Add(mostek);
            pozycje.Add(BudujKarteUrzadzenia(u, mostek, y));
            y += (u.Spe ? WysokoscKartySpe : WysokoscKarty) + Odstep;
        }

        return y;
    }
}
