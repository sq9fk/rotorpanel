namespace RotorPanel;

/// <summary>
/// Sekcja urzadzen: wszystko, co siedzi na porcie szeregowym, a nie jest rotorem -
/// wzmacniacz, sterownik, transceiver. Stad wlasna tabela i wlasna sekcja w oknie glownym.
/// </summary>
public partial class SettingsForm
{
    private const string ProtokolSurowy  = "surowy";
    private const string ProtokolTelnet  = "RFC 2217";

    private void BudujSekcjeUrzadzen()
    {
        Controls.Add(Ui.Etykieta("Urządzenia — port szeregowy przez sieć",
            Theme.Nazwa(), Theme.Tekst, new Point(20, 362), new Size(420, 20)));

        _siatkaUrzadzen = NowaSiatka(new Point(18, 386), new Size(684, 118));

        var kolNr = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nr", Width = 40, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNr.DefaultCellStyle.ForeColor = Theme.TekstSzary;
        _siatkaUrzadzen.Columns.Add(kolNr);

        _siatkaUrzadzen.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Nazwa", Width = 150,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _kolParaU = new DataGridViewComboBoxColumn
        {
            HeaderText = "Para portów   (aplikacja - mostek)",
            Width = 216,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _siatkaUrzadzen.Columns.Add(_kolParaU);

        _siatkaUrzadzen.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Adres", Width = 116,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatkaUrzadzen.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Port TCP", Width = 64,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _kolProtokol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Protokół",
            Width = 92,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _kolProtokol.Items.Add(ProtokolSurowy);
        _kolProtokol.Items.Add(ProtokolTelnet);
        _siatkaUrzadzen.Columns.Add(_kolProtokol);

        _siatkaUrzadzen.CurrentCellDirtyStateChanged += (_, _) =>
        {
            int kol = _siatkaUrzadzen.CurrentCell?.ColumnIndex ?? -1;
            if (_siatkaUrzadzen.IsCurrentCellDirty && (kol == RPara || kol == RProtokol))
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
            "RFC 2217 to port szeregowy przez Telnet — tak wystawiają go m.in. konwertery " +
            "microBit. Parametry transmisji ustawia się po stronie urządzenia.",
            Theme.Maly(), Theme.TekstSzary, new Point(324, 506), new Size(378, 32)));
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

            _siatkaUrzadzen.Rows.Add(u.Nr, u.Etykieta, opis, u.Ip,
                                     u.Port > 0 ? u.Port.ToString() : "",
                                     u.Protokol == Protokol.Rfc2217 ? ProtokolTelnet : ProtokolSurowy);
        }
    }

    private void DodajUrzadzenie()
    {
        var uzyte = new HashSet<int>();
        foreach (DataGridViewRow w in _siatkaUrzadzen.Rows)
            if (int.TryParse(Kom(w, RNr), out int n)) uzyte.Add(n);

        int nr = 1;
        while (uzyte.Contains(nr)) nr++;

        _siatkaUrzadzen.Rows.Add(nr, "Urządzenie " + nr, Brak, "", "", ProtokolTelnet);
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

        return new Urzadzenie
        {
            Nr = nr,
            Nazwa = Kom(w, RNazwa),
            Com = para?.A ?? "",
            Dev = para?.B ?? "",
            Ip = Kom(w, RIp),
            Port = port,
            Protokol = Kom(w, RProtokol) == ProtokolTelnet ? Protokol.Rfc2217 : Protokol.Surowy
        };
    }
}
