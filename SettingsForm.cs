namespace RotorPanel;

/// <summary>Edycja konfiguracji: anteny, obecnosc rotora, para portow, adres i port ser2net.</summary>
public class SettingsForm : Form
{
    private readonly Config _cfg;
    private TextBox _ip, _sterownik, _setupc;
    private CheckBox _autoPolacz;
    private DataGridView _siatka;
    private DataGridViewComboBoxColumn _kolPara;
    private Label _info;

    private const string Brak = "— brak —";
    private readonly Dictionary<string, ParaPortow> _mapaPar = new();

    private const int KolNr = 0, KolRotor = 1, KolNazwa = 2,
                      KolPara = 3, KolIp = 4, KolPort = 5;

    public SettingsForm(Config cfg)
    {
        _cfg = cfg;

        Text            = "Ustawienia";
        ClientSize      = new Size(720, 520);
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
        odswiezPary.Click += (_, _) => { WczytajPary(); _info.ForeColor = Theme.TekstSzary;
                                         _info.Text = "Lista par odświeżona."; };
        karta.Controls.Add(odswiezPary);

        Controls.Add(Ui.Etykieta(
            "Nazwy pochodzą ze sterownika anten — użyj „Pobierz nazwy”. Zaznacz „Rotor” tam, " +
            "gdzie antena jest obracana.",
            Theme.Maly(), Theme.TekstSzary, new Point(20, 140), new Size(450, 18)));

        _autoPolacz = new CheckBox
        {
            Text = "Łącz automatycznie po uruchomieniu",
            Checked = cfg.AutoPolacz,
            Location = new Point(474, 136),
            Size = new Size(230, 22),
            Font = Theme.Zwykly(),
            ForeColor = Theme.Tekst,
            BackColor = Color.Transparent
        };
        Controls.Add(_autoPolacz);

        BudujSiatke();
        WczytajPary();
        WypelnijSiatke();

        _info = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(20, 466), new Size(430, 18));
        Controls.Add(_info);

        var zapisz = Ui.Przycisk("Zapisz", 110, glowny: true);
        zapisz.Location = new Point(478, 462);
        zapisz.Click += (_, _) => Zapisz();
        Controls.Add(zapisz);

        var anuluj = Ui.Przycisk("Anuluj", 96);
        anuluj.Location = new Point(600, 462);
        anuluj.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(anuluj);
    }

    private static TextBox Pole(string tekst, Point poz, int szerokosc) => new()
    {
        Text = tekst,
        Location = poz,
        Width = szerokosc,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Zwykly()
    };

    /// <summary>
    /// Buduje liste rozwijana z par zdefiniowanych w sterowniku com0com. Dokłada tez pary
    /// wystepujace w konfiguracji, a nieobecne w systemie, zeby nie gubic ustawien.
    /// </summary>
    private void WczytajPary()
    {
        string wybrane = null;
        var zapamietane = new Dictionary<int, string>();
        foreach (DataGridViewRow w in _siatka.Rows)
            zapamietane[w.Index] = Convert.ToString(w.Cells[KolPara].Value);

        _mapaPar.Clear();
        _kolPara.Items.Clear();
        _kolPara.Items.Add(Brak);

        // Osierocone wpisy po usunietych parach nie trafiaja na liste wyboru.
        foreach (var para in Com0Com.Pary().Where(p => p.Istnieje))
        {
            if (_mapaPar.ContainsKey(para.Opis)) continue;
            _mapaPar[para.Opis] = para;
            _kolPara.Items.Add(para.Opis);
        }

        foreach (var a in _cfg.Anteny.Where(x => !string.IsNullOrWhiteSpace(x.Dev)))
        {
            var para = new ParaPortow { A = a.Com, B = a.Dev };
            if (_mapaPar.ContainsKey(para.Opis)) continue;
            _mapaPar[para.Opis] = para;
            _kolPara.Items.Add(para.Opis);
        }

        foreach (DataGridViewRow w in _siatka.Rows)
        {
            string poprzednie = zapamietane.TryGetValue(w.Index, out wybrane) ? wybrane : null;
            w.Cells[KolPara].Value = poprzednie is not null && _kolPara.Items.Contains(poprzednie)
                ? poprzednie
                : Brak;
        }
    }

    private void BudujSiatke()
    {
        _siatka = new DataGridView
        {
            Location = new Point(18, 164),
            Size = new Size(684, 286),
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
        _siatka.EnableHeadersVisualStyles = false;
        _siatka.ColumnHeadersDefaultCellStyle.BackColor = Theme.Tlo;
        _siatka.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TekstSzary;
        _siatka.ColumnHeadersDefaultCellStyle.Font = Theme.Maly();
        _siatka.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _siatka.ColumnHeadersHeight = 32;
        _siatka.RowTemplate.Height = 28;

        var kolNr = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nr", Width = 40, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNr.DefaultCellStyle.ForeColor = Theme.TekstSzary;
        _siatka.Columns.Add(kolNr);

        _siatka.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Rotor", Width = 52,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        var kolNazwa = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nazwa anteny", Width = 150, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNazwa.DefaultCellStyle.BackColor = Theme.Tlo;
        _siatka.Columns.Add(kolNazwa);

        _kolPara = new DataGridViewComboBoxColumn
        {
            HeaderText = "Para portów   (PstRotator - mostek)",
            Width = 232,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _siatka.Columns.Add(_kolPara);

        _siatka.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "IP (puste = domyślne)", Width = 110,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatka.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Port TCP", Width = 58,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatka.CurrentCellDirtyStateChanged += (_, _) =>
        {
            int kol = _siatka.CurrentCell?.ColumnIndex ?? -1;
            if (_siatka.IsCurrentCellDirty && (kol == KolRotor || kol == KolPara))
                _siatka.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _siatka.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var wiersz = _siatka.Rows[e.RowIndex];

            if (e.ColumnIndex == KolRotor)
            {
                OdswiezWiersz(wiersz);
                return;
            }

            if (e.ColumnIndex != KolPara) return;

            // Wybranie konkretnej pary oznacza, ze antena ma rotor - nie ma sensu
            // wymagac osobnego kliknięcia w checkbox.
            string wybrana = Convert.ToString(wiersz.Cells[KolPara].Value);
            bool jestPara = !string.IsNullOrEmpty(wybrana) && wybrana != Brak;
            bool zaznaczony = wiersz.Cells[KolRotor].Value is bool z && z;

            if (jestPara && !zaznaczony)
            {
                wiersz.Cells[KolRotor].Value = true;   // wywola OdswiezWiersz przez zdarzenie
            }
            else
            {
                OdswiezWiersz(wiersz);
            }
        };
        _siatka.DataError += (_, e) => e.ThrowException = false;

        Controls.Add(_siatka);
    }

    private void WypelnijSiatke()
    {
        _siatka.Rows.Clear();
        foreach (var a in _cfg.Anteny.OrderBy(x => x.Nr))
        {
            string opis = Brak;
            if (!string.IsNullOrWhiteSpace(a.Dev))
            {
                var para = new ParaPortow { A = a.Com, B = a.Dev };
                if (_kolPara.Items.Contains(para.Opis)) opis = para.Opis;
            }

            int i = _siatka.Rows.Add(a.Nr, a.MaRotor, a.Nazwa, opis, a.Ip,
                                     a.Port > 0 ? a.Port.ToString() : "");
            OdswiezWiersz(_siatka.Rows[i]);
        }
    }

    /// <summary>
    /// Kolumna z para jest zawsze klikalna - wybranie pary samo zaznacza "Rotor".
    /// Adres i port pozostaja zablokowane, dopoki rotor nie jest zaznaczony.
    /// </summary>
    private void OdswiezWiersz(DataGridViewRow w)
    {
        bool ma = w.Cells[KolRotor].Value is bool b && b;

        var komorkaPary = w.Cells[KolPara];
        komorkaPary.ReadOnly = false;
        komorkaPary.Style.ForeColor = ma ? Theme.Tekst : Theme.TekstSzary;
        komorkaPary.Style.BackColor = Color.White;

        foreach (int k in new[] { KolIp, KolPort })
        {
            var komorka = w.Cells[k];
            komorka.ReadOnly = !ma;
            komorka.Style.ForeColor = ma ? Theme.Tekst : Theme.TekstSzary;
            komorka.Style.BackColor = ma ? Color.White : Theme.Tlo;
        }

        if (!ma)
        {
            w.Cells[KolPara].Value = Brak;
            w.Cells[KolIp].Value = "";
            w.Cells[KolPort].Value = "";
        }
    }

    /// <summary>Wskazanie setupc.exe przez okno wyboru pliku.</summary>
    private void WybierzSetupc()
    {
        using var okno = new OpenFileDialog
        {
            Title = "Wskaż plik setupc.exe ze sterownika com0com",
            Filter = "setupc.exe|setupc.exe|Pliki wykonywalne (*.exe)|*.exe|Wszystkie pliki (*.*)|*.*",
            FileName = "setupc.exe",
            CheckFileExists = true
        };

        okno.InitialDirectory = KatalogStartowy();

        if (okno.ShowDialog(this) != DialogResult.OK) return;

        // W konfiguracji trzymamy sciezki z ukosnikami w przod.
        _setupc.Text = okno.FileName.Replace((char)92, '/');
    }

    /// <summary>Katalog obecnej sciezki, a gdy jej nie ma - typowe miejsce instalacji com0com.</summary>
    private string KatalogStartowy()
    {
        try
        {
            string katalog = Path.GetDirectoryName(Config.NaWindows(_setupc.Text.Trim()));
            if (!string.IsNullOrEmpty(katalog) && Directory.Exists(katalog)) return katalog;
        }
        catch { /* sciezka moze byc bezsensowna - lecimy dalej */ }

        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86,
                                       Environment.SpecialFolder.ProgramFiles })
        {
            string kandydat = Path.Combine(Environment.GetFolderPath(folder), "com0com");
            if (Directory.Exists(kandydat)) return kandydat;
        }

        return "";
    }

    private async Task PobierzNazwy()
    {
        _info.ForeColor = Theme.TekstSzary;
        _info.Text = "Pobieram…";
        try
        {
            _sterownik.Text = Config.NormalizujHost(_sterownik.Text);
            var nazwy = await SterownikAnten.PobierzNazwy(_sterownik.Text);
            int zmienione = 0;

            foreach (DataGridViewRow w in _siatka.Rows)
            {
                if (!int.TryParse(Convert.ToString(w.Cells[KolNr].Value), out int nr)) continue;
                if (!nazwy.TryGetValue(nr, out string nazwa) || string.IsNullOrWhiteSpace(nazwa)) continue;
                if (Convert.ToString(w.Cells[KolNazwa].Value) == nazwa) continue;
                w.Cells[KolNazwa].Value = nazwa;
                zmienione++;
            }

            _info.Text = "Pobrano " + nazwy.Count + " nazw, zaktualizowano " + zmienione + ".";
        }
        catch (Exception ex)
        {
            _info.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
            _info.Text = "Błąd: " + ex.Message;
        }
    }

    private static string Kom(DataGridViewRow w, int k)
        => (Convert.ToString(w.Cells[k].Value) ?? "").Trim();

    private void Zapisz()
    {
        _siatka.EndEdit();
        var nowe = new List<Antena>();

        foreach (DataGridViewRow w in _siatka.Rows)
        {
            int.TryParse(Kom(w, KolNr), out int nr);
            int.TryParse(Kom(w, KolPort), out int port);
            bool ma = w.Cells[KolRotor].Value is bool b && b;

            _mapaPar.TryGetValue(Kom(w, KolPara), out ParaPortow para);

            nowe.Add(new Antena
            {
                Nr      = nr,
                Nazwa   = Kom(w, KolNazwa),
                MaRotor = ma,
                Com     = ma && para is not null ? para.A : "",
                Dev     = ma && para is not null ? para.B : "",
                Ip      = ma ? Kom(w, KolIp) : "",
                Port    = ma ? port : 0
            });
        }

        var niekompletna = nowe.FirstOrDefault(a => a.MaRotor && !a.Gotowa);
        if (niekompletna is not null)
        {
            MessageBox.Show(this,
                "Antena " + niekompletna.Nr + " (" + niekompletna.Etykieta + ") ma zaznaczony rotor, "
                + "ale nie wybrano pary portów albo brakuje portu TCP.",
                "Ustawienia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Wspoldzielenie pary jest dozwolone, ale ustawienia polaczenia musza byc identyczne.
        foreach (var grupa in nowe.Where(a => a.Gotowa).GroupBy(a => a.KluczPary))
        {
            var pierwsza = grupa.First();
            var rozna = grupa.FirstOrDefault(a =>
                !string.Equals(a.Ip, pierwsza.Ip, StringComparison.OrdinalIgnoreCase) ||
                a.Port != pierwsza.Port);

            if (rozna is not null)
            {
                MessageBox.Show(this,
                    "Anteny " + pierwsza.Nr + " i " + rozna.Nr + " wskazują tę samą parę ("
                    + pierwsza.Dev + "), ale mają różny adres albo port TCP."
                    + Environment.NewLine + Environment.NewLine
                    + "Anteny na wspólnym maszcie muszą mieć identyczne ustawienia połączenia.",
                    "Ustawienia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _cfg.AutoPolacz     = _autoPolacz.Checked;
        _cfg.PiIp           = _ip.Text.Trim();
        _cfg.SterownikAnten = Config.NormalizujHost(_sterownik.Text);
        _cfg.Setupc         = _setupc.Text.Trim();
        _cfg.Anteny         = nowe;

        try
        {
            _cfg.Zapisz();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Nie udało się zapisać konfiguracji:" + Environment.NewLine + ex.Message,
                "Ustawienia", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
