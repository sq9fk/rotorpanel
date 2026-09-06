namespace RotorPanel;

/// <summary>
/// Sekcja urzadzen: wszystko, co siedzi na porcie szeregowym, a nie jest rotorem -
/// wzmacniacz, sterownik, transceiver. Stad wlasna tabela i wlasna sekcja w oknie glownym.
/// </summary>
public partial class SettingsForm
{
    private const string ProtokolSurowy  = "surowy";
    private const string ProtokolTelnet  = "RFC 2217";
    private const string PredkoscZUrzadzenia = "z urządzenia";

    private DataGridViewComboBoxColumn _kolPredkosc;

    private static readonly int[] Predkosci =
        { 300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

    private static readonly string[] BityDanychNazwy = { "5", "6", "7", "8" };

    private static readonly string[] Parzystosci =
        { "brak", "nieparzysta", "parzysta", "znacznik", "spacja" };

    private static readonly string[] BityStopuNazwy = { "1", "1.5", "2" };

    private void BudujSekcjeUrzadzen()
    {
        Controls.Add(Ui.Etykieta("Urządzenia — port szeregowy przez sieć",
            Theme.Nazwa(), Theme.Tekst, new Point(20, 362), new Size(420, 20)));

        _siatkaUrzadzen = NowaSiatka(new Point(18, 386), new Size(884, 118));

        var kolNr = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nr", Width = 40, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNr.DefaultCellStyle.ForeColor = Theme.TekstSzary;
        _siatkaUrzadzen.Columns.Add(kolNr);

        _siatkaUrzadzen.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Nazwa", Width = 104,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _kolParaU = new DataGridViewComboBoxColumn
        {
            HeaderText = "Para portów",
            Width = 146,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _siatkaUrzadzen.Columns.Add(_kolParaU);

        _siatkaUrzadzen.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Adres", Width = 96,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatkaUrzadzen.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Port TCP", Width = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _kolProtokol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Protokół",
            Width = 80,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _kolProtokol.Items.Add(ProtokolSurowy);
        _kolProtokol.Items.Add(ProtokolTelnet);
        _siatkaUrzadzen.Columns.Add(_kolProtokol);

        // Przy RFC 2217 to my podajemy urzadzeniu parametry portu - stad listy wyboru.
        _kolPredkosc = new DataGridViewComboBoxColumn
        {
            HeaderText = "Prędkość", Width = 104,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _kolPredkosc.Items.Add(PredkoscZUrzadzenia);
        foreach (var v in Predkosci) _kolPredkosc.Items.Add(v.ToString());
        _siatkaUrzadzen.Columns.Add(_kolPredkosc);

        var kolBity = new DataGridViewComboBoxColumn
        {
            HeaderText = "Bity", Width = 50,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        foreach (var v in BityDanychNazwy) kolBity.Items.Add(v);
        _siatkaUrzadzen.Columns.Add(kolBity);

        var kolParzystosc = new DataGridViewComboBoxColumn
        {
            HeaderText = "Parzystość", Width = 84,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        foreach (var v in Parzystosci) kolParzystosc.Items.Add(v);
        _siatkaUrzadzen.Columns.Add(kolParzystosc);

        var kolStop = new DataGridViewComboBoxColumn
        {
            HeaderText = "Stop", Width = 50,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        foreach (var v in BityStopuNazwy) kolStop.Items.Add(v);
        _siatkaUrzadzen.Columns.Add(kolStop);

        // Zaznaczone znaczy: to wzmacniacz SPE Expert, wiec mostek moze go odpytywac
        // o status i pokazywac go na karcie.
        _siatkaUrzadzen.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "SPE", Width = 44,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatkaUrzadzen.CurrentCellDirtyStateChanged += (_, _) =>
        {
            int kol = _siatkaUrzadzen.CurrentCell?.ColumnIndex ?? -1;
            if (_siatkaUrzadzen.IsCurrentCellDirty &&
                (kol == RPara || kol == RProtokol || kol == UPredkosc ||
                 kol == UBityDanych || kol == UParzystosc || kol == UStop))
                _siatkaUrzadzen.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _siatkaUrzadzen.CellValueChanged += (_, e) =>
        {
            if (_przebudowaList || e.RowIndex < 0) return;
            if (e.ColumnIndex == RPara) OdswiezListyPar();
        };

        Controls.Add(_siatkaUrzadzen);

        var dodaj = Ui.Przycisk("Dodaj urządzenie", 148, glowny: true);
        dodaj.Location = new Point(18, 510);
        dodaj.Click += (_, _) => DodajUrzadzenie();
        Controls.Add(dodaj);

        var usun = Ui.Przycisk("Usuń urządzenie", 140);
        usun.Location = new Point(174, 510);
        usun.Click += (_, _) => UsunUrzadzenie();
        Controls.Add(usun);

        Controls.Add(Ui.Etykieta(
            "RFC 2217 to port szeregowy przez Telnet (konwertery microBit); urządzenie otwiera port " +
            "dopiero po podaniu prędkości. Kolumna SPE włącza odpytywanie wzmacniacza Expert o stan.",
            Theme.Maly(), Theme.TekstSzary, new Point(324, 508), new Size(578, 44)));
    }

    private void WypelnijUrzadzenia()
    {
        _siatkaUrzadzen.Rows.Clear();
        foreach (var u in _cfg.Urzadzenia.OrderBy(x => x.Nr))
        {
            string opis = Brak;
            if (!string.IsNullOrWhiteSpace(u.Dev))
            {
                var para = new ParaPortow { A = u.Com, B = u.Dev };
                if (_kolPara.Items.Contains(para.Opis)) opis = para.Opis;
            }

            // predkosc spoza listy (recznie wpisana w rotory.json) musi trafic do pozycji,
            // inaczej DataGridView zglosi blad wartosci komorki
            string predkosc = u.Predkosc > 0 ? u.Predkosc.ToString() : PredkoscZUrzadzenia;
            if (!_kolPredkosc.Items.Contains(predkosc)) _kolPredkosc.Items.Add(predkosc);

            _siatkaUrzadzen.Rows.Add(u.Nr, u.Etykieta, opis, u.Ip,
                                     u.Port > 0 ? u.Port.ToString() : "",
                                     u.Protokol == Protokol.Rfc2217 ? ProtokolTelnet : ProtokolSurowy,
                                     predkosc,
                                     BityDanychNazwy.Contains(u.BityDanych.ToString())
                                         ? u.BityDanych.ToString() : "8",
                                     Parzystosci[(int)u.Parzystosc],
                                     BityStopuNazwy[(int)u.BityStopu],
                                     u.Spe);
        }
    }

    private void DodajUrzadzenie()
    {
        var uzyte = new HashSet<int>();
        foreach (DataGridViewRow w in _siatkaUrzadzen.Rows)
            if (int.TryParse(Kom(w, RNr), out int n)) uzyte.Add(n);

        int nr = 1;
        while (uzyte.Contains(nr)) nr++;

        _siatkaUrzadzen.Rows.Add(nr, "Urządzenie " + nr, Brak, "", "", ProtokolTelnet,
                                 "9600", "8", Parzystosci[0], BityStopuNazwy[0], false);
        OdswiezListyPar();
    }

    private void UsunUrzadzenie()
    {
        if (_siatkaUrzadzen.CurrentRow == null) return;
        _siatkaUrzadzen.Rows.Remove(_siatkaUrzadzen.CurrentRow);
        OdswiezListyPar();
    }

    /// <summary>Urzadzenie odczytane z wiersza tabeli.</summary>
    private Urzadzenie UrzadzenieZWiersza(DataGridViewRow w)
    {
        _mapaPar.TryGetValue(Kom(w, RPara), out ParaPortow para);
        int.TryParse(Kom(w, RNr), out int nr);
        int.TryParse(Kom(w, RPort), out int port);

        int.TryParse(Kom(w, UPredkosc), out int predkosc);
        if (!int.TryParse(Kom(w, UBityDanych), out int bityDanych)) bityDanych = 8;

        int parzystosc = Array.IndexOf(Parzystosci, Kom(w, UParzystosc));
        int stop = Array.IndexOf(BityStopuNazwy, Kom(w, UStop));

        return new Urzadzenie
        {
            Nr = nr,
            Nazwa = Kom(w, RNazwa),
            Com = para?.A ?? "",
            Dev = para?.B ?? "",
            Ip = Kom(w, RIp),
            Port = port,
            Protokol = Kom(w, RProtokol) == ProtokolTelnet ? Protokol.Rfc2217 : Protokol.Surowy,
            Predkosc = predkosc,
            BityDanych = bityDanych,
            Parzystosc = (Parzystosc)Math.Max(parzystosc, 0),
            BityStopu = (BityStopu)Math.Max(stop, 0),
            Spe = w.Cells[USpe].Value is bool zaznaczone && zaznaczone
        };
    }
}
