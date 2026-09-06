namespace RotorPanel;

/// <summary>
/// Edycja konfiguracji. Rotor definiuje sie raz - para portow plus punkt ser2net -
/// a antena tylko wskazuje, ktory rotor nia obraca.
/// </summary>
public partial class SettingsForm : Form
{
    private readonly Config _cfg;

    private TextBox _ip, _sterownik, _setupc;
    private CheckBox _autoPolacz;
    private Label _info;

    private DataGridView _siatkaRotorow, _siatkaAnten;
    private DataGridViewComboBoxColumn _kolPara, _kolRotor;

    private const string Brak = "— brak —";
    private readonly Dictionary<string, ParaPortow> _mapaPar = new();

    private const int RNr = 0, RNazwa = 1, RPara = 2, RIp = 3, RPort = 4;
    private const int ANr = 0, ANazwa = 1, ARotor = 2;

    public SettingsForm(Config cfg)
    {
        _cfg = cfg;

        Text            = "Ustawienia";
        ClientSize      = new Size(720, 636);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox     = false;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        var karta = new Karta { Location = new Point(18, 16), Size = new Size(684, 116) };
        Controls.Add(karta);

        karta.Controls.Add(Ui.Etykieta("Adres ser2net (domyślny)", Theme.Maly(), Theme.TekstSzary,
            new Point(18, 16), new Size(200, 16)));
        _ip = Pole(cfg.PiIp, new Point(18, 34), 200);
        karta.Controls.Add(_ip);

        karta.Controls.Add(Ui.Etykieta("Sterownik anten — adres lub nazwa, opcjonalnie :port",
            Theme.Maly(), Theme.TekstSzary, new Point(240, 16), new Size(300, 16)));
        _sterownik = Pole(cfg.SterownikAnten, new Point(240, 34), 220);
        karta.Controls.Add(_sterownik);

        var pobierz = Ui.Przycisk("Pobierz nazwy", 130);
        pobierz.Location = new Point(478, 33);
        pobierz.Click += async (_, _) => await PobierzNazwy();
        karta.Controls.Add(pobierz);

        karta.Controls.Add(Ui.Etykieta("Ścieżka do setupc.exe (com0com)", Theme.Maly(), Theme.TekstSzary,
            new Point(18, 66), new Size(260, 16)));
        _setupc = Pole(cfg.Setupc, new Point(18, 84), 468);
        karta.Controls.Add(_setupc);

        var przegladaj = Ui.Przycisk("…", 34);
        przegladaj.Location = new Point(492, 83);
        przegladaj.Click += (_, _) => WybierzSetupc();
        karta.Controls.Add(przegladaj);
        new ToolTip().SetToolTip(przegladaj, "Wskaż plik setupc.exe w Eksploratorze");

        var odswiezPary = Ui.Przycisk("Odśwież pary", 152);
        odswiezPary.Location = new Point(532, 83);
        odswiezPary.Click += (_, _) =>
        {
            WczytajPary();
            _info.ForeColor = Theme.TekstSzary;
            _info.Text = "Lista par odświeżona.";
        };
        karta.Controls.Add(odswiezPary);

        BudujSekcjeRotorow();
        BudujSekcjeAnten();

        _autoPolacz = new CheckBox
        {
            Text = "Łącz automatycznie po uruchomieniu",
            Checked = cfg.AutoPolacz,
            Location = new Point(20, 594),
            Size = new Size(240, 22),
            Font = Theme.Zwykly(),
            ForeColor = Theme.Tekst,
            BackColor = Color.Transparent
        };
        Controls.Add(_autoPolacz);

        _info = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(266, 598), new Size(200, 18));
        Controls.Add(_info);

        var zapisz = Ui.Przycisk("Zapisz", 110, glowny: true);
        zapisz.Location = new Point(478, 592);
        zapisz.Click += (_, _) => Zapisz();
        Controls.Add(zapisz);

        var anuluj = Ui.Przycisk("Anuluj", 96);
        anuluj.Location = new Point(600, 592);
        anuluj.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(anuluj);

        WczytajPary();
        WypelnijRotory();
        OdswiezListeRotorow();
        WypelnijAnteny();
    }

    private static TextBox Pole(string tekst, Point poz, int szerokosc) => new()
    {
        Text = tekst,
        Location = poz,
        Width = szerokosc,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Zwykly()
    };

    private static DataGridView NowaSiatka(Point poz, Size rozmiar)
    {
        var s = new DataGridView
        {
            Location = poz,
            Size = rozmiar,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Theme.Karta,
            BorderStyle = BorderStyle.FixedSingle,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            Font = Theme.Zwykly()
        };
        s.EnableHeadersVisualStyles = false;
        s.ColumnHeadersDefaultCellStyle.BackColor = Theme.Tlo;
        s.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TekstSzary;
        s.ColumnHeadersDefaultCellStyle.Font = Theme.Maly();
        s.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        s.ColumnHeadersHeight = 30;
        s.RowTemplate.Height = 28;
        s.DataError += (_, e) => e.ThrowException = false;
        return s;
    }
}
