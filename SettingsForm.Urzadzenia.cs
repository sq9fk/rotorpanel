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

    private DataGridViewComboBoxColumn _kolPredkosc, _kolTyp;

    private const string TypInny = "— inne urządzenie —";

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

        _siatkaUrzadzen = NowaSiatka(new Point(18, 386), new Size(964, 118));

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
            Width = 147,
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

        // Model z listy wlacza odpytywanie o stan; wpisany recznie znaczy tyle,
        // ze to jakies inne urzadzenie szeregowe.
        _kolTyp = new DataGridViewComboBoxColumn
        {
            HeaderText = "Typ urządzenia", Width = 130,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _kolTyp.Items.Add(TypInny);
        foreach (var t in Urzadzenie.TypyZeStanem) _kolTyp.Items.Add(t);
        _siatkaUrzadzen.Columns.Add(_kolTyp);

        // Lista ma byc podpowiedzia, a nie przymusem - stad pole do wpisania wlasnego.
        _siatkaUrzadzen.EditingControlShowing += (_, e) =>
        {
            if (_siatkaUrzadzen.CurrentCell?.ColumnIndex == UTyp &&
                e.Control is ComboBox lista)
                lista.DropDownStyle = ComboBoxStyle.DropDown;
        };

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
            if (e.ColumnIndex == UTyp) PodstawParametryModelu(e.RowIndex);
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
            "dopiero po podaniu prędkości. Model z listy włącza odczyt stanu i klawiaturę — typ " +
            "wpisany ręcznie jest tylko opisem.",
            Theme.Maly(), Theme.TekstSzary, new Point(324, 508), new Size(618, 44)));
    }

    /// <summary>Pusty typ pokazujemy jako pozycje "inne urzadzenie".</summary>
    private string TypDoKomorki(string typ)
    {
        if (typ.Length == 0) return TypInny;
        if (!_kolTyp.Items.Contains(typ)) _kolTyp.Items.Add(typ);
        return typ;
    }

    private static string TypZKomorki(string komorka)
        => komorka == TypInny ? "" : komorka.Trim();

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
                                     TypDoKomorki(u.Typ));
        }
    }

    /// <summary>
    /// Po wybraniu znanego modelu ustawia parametry transmisji z jego dokumentacji,
    /// zeby nie trzeba bylo ich szukac. Recznie wpisany typ zostawia jak jest.
    /// </summary>
    private void PodstawParametryModelu(int wiersz)
    {
        var w = _siatkaUrzadzen.Rows[wiersz];
        if (TypZKomorki(Kom(w, UTyp)).Length == 0) return;
        if (Array.IndexOf(Urzadzenie.TypyZeStanem, Kom(w, UTyp)) < 0) return;

        // SPE Expert: 8 bitow, 1 stop, bez parzystosci, do 115200.
        w.Cells[RProtokol].Value   = ProtokolTelnet;
        w.Cells[UPredkosc].Value   = "115200";
        w.Cells[UBityDanych].Value = "8";
        w.Cells[UParzystosc].Value = Parzystosci[0];
        w.Cells[UStop].Value       = BityStopuNazwy[0];
    }

    private void DodajUrzadzenie()
    {
        var uzyte = new HashSet<int>();
        foreach (DataGridViewRow w in _siatkaUrzadzen.Rows)
            if (int.TryParse(Kom(w, RNr), out int n)) uzyte.Add(n);

        int nr = 1;
        while (uzyte.Contains(nr)) nr++;

        _siatkaUrzadzen.Rows.Add(nr, "Urządzenie " + nr, Brak, "", "", ProtokolTelnet,
                                 "9600", "8", Parzystosci[0], BityStopuNazwy[0], TypInny);
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
            Typ = TypZKomorki(Kom(w, UTyp))
        };
    }
}
