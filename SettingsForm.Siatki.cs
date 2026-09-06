﻿namespace RotorPanel;

/// <summary>Budowa i obsluga obu tabel okna ustawien.</summary>
public partial class SettingsForm
{
    private bool _przebudowaList;

    /// <summary>
    /// Kazdy wiersz dostaje wlasna liste par: bez tych zajetych przez inne rotory.
    /// Wlasny wybor zostaje na liscie, zeby dalo sie go zobaczyc i zmienic.
    /// </summary>
    private void OdswiezListyPar()
    {
        if (_przebudowaList) return;
        _przebudowaList = true;

        try
        {
            foreach (DataGridViewRow w in _siatkaRotorow.Rows)
            {
                if (!(w.Cells[RPara] is DataGridViewComboBoxCell komorka)) continue;

                string wlasna = Kom(w, RPara);

                var zajete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewRow inny in _siatkaRotorow.Rows)
                {
                    if (ReferenceEquals(inny, w)) continue;
                    string cudza = Kom(inny, RPara);
                    if (!string.IsNullOrEmpty(cudza) && cudza != Brak) zajete.Add(cudza);
                }

                komorka.Items.Clear();
                komorka.Items.Add(Brak);

                foreach (string opis in _kolPara.Items)
                {
                    if (opis == Brak) continue;
                    if (zajete.Contains(opis) && opis != wlasna) continue;
                    komorka.Items.Add(opis);
                }

                komorka.Value = komorka.Items.Contains(wlasna) ? wlasna : Brak;
            }
        }
        finally { _przebudowaList = false; }
    }

    private void BudujSekcjeRotorow()
    {
        Controls.Add(Ui.Etykieta("Rotory — para portów i punkt ser2net",
            Theme.Nazwa(), Theme.Tekst, new Point(20, 142), new Size(400, 20)));

        _siatkaRotorow = NowaSiatka(new Point(18, 166), new Size(684, 146));

        var kolNr = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nr", Width = 40, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNr.DefaultCellStyle.ForeColor = Theme.TekstSzary;
        _siatkaRotorow.Columns.Add(kolNr);

        _siatkaRotorow.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Nazwa", Width = 130,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _kolPara = new DataGridViewComboBoxColumn
        {
            HeaderText = "Para portów   (PstRotator - mostek)",
            Width = 216,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _siatkaRotorow.Columns.Add(_kolPara);

        _siatkaRotorow.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Adres ser2net", Width = 130,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatkaRotorow.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Port TCP", Width = 64,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _siatkaRotorow.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_siatkaRotorow.IsCurrentCellDirty && _siatkaRotorow.CurrentCell?.ColumnIndex == RPara)
                _siatkaRotorow.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _siatkaRotorow.CellValueChanged += (_, e) =>
        {
            // W trakcie przebudowy list sami ustawiamy wartosci komorek - bez tego
            // kazde ustawienie wywolywalo by pelne przeliczenie od nowa.
            if (_przebudowaList || e.RowIndex < 0) return;

            if (e.ColumnIndex == RPara) OdswiezListyPar();
            OdswiezListeRotorow();
        };

        Controls.Add(_siatkaRotorow);

        var dodaj = Ui.Przycisk("Dodaj rotor", 120, glowny: true);
        dodaj.Location = new Point(18, 320);
        dodaj.Click += (_, _) => DodajRotor();
        Controls.Add(dodaj);

        var usun = Ui.Przycisk("Usuń rotor", 110);
        usun.Location = new Point(146, 320);
        usun.Click += (_, _) => UsunRotor();
        Controls.Add(usun);

        Controls.Add(Ui.Etykieta(
            "Pusty adres oznacza domyślny, wpisany u góry okna. " +
            "Kilka anten może wskazywać ten sam rotor — dzielą wtedy jeden mostek.",
            Theme.Maly(), Theme.TekstSzary, new Point(266, 318), new Size(436, 32)));
    }

    private void WypelnijRotory()
    {
        _siatkaRotorow.Rows.Clear();
        foreach (var r in _cfg.Rotory.OrderBy(x => x.Nr))
        {
            string opis = Brak;
            if (!string.IsNullOrWhiteSpace(r.Dev))
            {
                var para = new ParaPortow { A = r.Com, B = r.Dev };
                if (_kolPara.Items.Contains(para.Opis)) opis = para.Opis;
            }

            _siatkaRotorow.Rows.Add(r.Nr, r.Etykieta, opis, r.Ip,
                                    r.Port > 0 ? r.Port.ToString() : "");
        }
    }

    private void DodajRotor()
    {
        var uzyte = new HashSet<int>();
        foreach (DataGridViewRow w in _siatkaRotorow.Rows)
            if (int.TryParse(Kom(w, RNr), out int n)) uzyte.Add(n);

        int nr = 1;
        while (uzyte.Contains(nr)) nr++;

        _siatkaRotorow.Rows.Add(nr, WolnaNazwa(nr), Brak, "", "");
        OdswiezListyPar();
        OdswiezListeRotorow();
    }

    /// <summary>Nazwa dla nowego rotora, nietrafiajaca w zadna juz uzyta.</summary>
    private string WolnaNazwa(int nr)
    {
        var uzyte = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow w in _siatkaRotorow.Rows) uzyte.Add(Kom(w, RNazwa));

        string nazwa = "Rotor " + nr;
        int kolejny = nr;
        while (uzyte.Contains(nazwa)) nazwa = "Rotor " + (++kolejny);
        return nazwa;
    }

    private void UsunRotor()
    {
        if (_siatkaRotorow.CurrentRow == null) return;
        _siatkaRotorow.Rows.Remove(_siatkaRotorow.CurrentRow);
        OdswiezListyPar();
        OdswiezListeRotorow();
    }

    private void BudujSekcjeAnten()
    {
        Controls.Add(Ui.Etykieta("Anteny — przypisanie rotora",
            Theme.Nazwa(), Theme.Tekst, new Point(20, 362), new Size(400, 20)));

        // Szesc wierszy plus naglowek, zeby lista anten nigdy sie nie przewijala.
        _siatkaAnten = NowaSiatka(new Point(18, 386), new Size(684, 6 * 28 + 32));

        var kolNr = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nr", Width = 40, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNr.DefaultCellStyle.ForeColor = Theme.TekstSzary;
        _siatkaAnten.Columns.Add(kolNr);

        var kolNazwa = new DataGridViewTextBoxColumn
        {
            HeaderText = "Nazwa anteny", Width = 200, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        kolNazwa.DefaultCellStyle.BackColor = Theme.Tlo;
        _siatkaAnten.Columns.Add(kolNazwa);

        _kolRotor = new DataGridViewComboBoxColumn
        {
            HeaderText = "Rotor",
            Width = 420,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _siatkaAnten.Columns.Add(_kolRotor);

        _siatkaAnten.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_siatkaAnten.IsCurrentCellDirty && _siatkaAnten.CurrentCell?.ColumnIndex == ARotor)
                _siatkaAnten.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _siatkaAnten.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == ARotor)
                _siatkaAnten.Rows[e.RowIndex].Tag =
                    NumerZOpisu(Kom(_siatkaAnten.Rows[e.RowIndex], ARotor), 0);
        };

        Controls.Add(_siatkaAnten);
    }

    private void WypelnijAnteny()
    {
        _siatkaAnten.Rows.Clear();
        foreach (var a in _cfg.Anteny.OrderBy(x => x.Nr))
        {
            int i = _siatkaAnten.Rows.Add(a.Nr, a.Etykieta, Brak);
            _siatkaAnten.Rows[i].Tag = a.Rotor;
        }
        PrzypiszWyborRotora();
    }

    /// <summary>
    /// Lista rotorow do wyboru przy antenach powstaje z tabeli rotorow, wiec zmienia sie
    /// razem z nia. Wybor pamietamy numerem rotora w Tag wiersza, nie tekstem.
    /// </summary>
    private void OdswiezListeRotorow()
    {
        if (_siatkaAnten == null || _kolRotor == null) return;

        foreach (DataGridViewRow w in _siatkaAnten.Rows)
            w.Tag = NumerZOpisu(Kom(w, ARotor), w.Tag is int t ? t : 0);

        _kolRotor.Items.Clear();
        _kolRotor.Items.Add(Brak);

        foreach (DataGridViewRow w in _siatkaRotorow.Rows)
        {
            var r = RotorZWiersza(w);
            if (r.Nr > 0) _kolRotor.Items.Add(OpisRotora(r));
        }

        PrzypiszWyborRotora();
    }

    private void PrzypiszWyborRotora()
    {
        if (_siatkaAnten == null) return;

        foreach (DataGridViewRow w in _siatkaAnten.Rows)
        {
            int nr = w.Tag is int t ? t : 0;
            string opis = OpisRotoraONumerze(nr);
            w.Cells[ARotor].Value =
                opis != null && _kolRotor.Items.Contains(opis) ? opis : Brak;
        }
    }

    private string OpisRotoraONumerze(int nr)
    {
        foreach (DataGridViewRow w in _siatkaRotorow.Rows)
        {
            var r = RotorZWiersza(w);
            if (r.Nr == nr) return OpisRotora(r);
        }
        return null;
    }

    private int NumerZOpisu(string opis, int domyslny)
    {
        if (string.IsNullOrEmpty(opis) || opis == Brak) return 0;

        foreach (DataGridViewRow w in _siatkaRotorow.Rows)
        {
            var r = RotorZWiersza(w);
            if (OpisRotora(r) == opis) return r.Nr;
        }
        return domyslny;
    }

    /// <summary>
    /// Opis rotora na liste wyboru przy antenach. Adres bierzemy z wiersza, a gdy pusty -
    /// z pola domyslnego u gory okna, zeby na liscie zawsze bylo widac dokad to prowadzi.
    /// </summary>
    private string OpisRotora(Rotor r)
    {
        if (!r.Gotowy) return r.Etykieta + "   (niekompletny)";

        string adres = string.IsNullOrWhiteSpace(r.Ip) ? _ip.Text.Trim() : r.Ip.Trim();
        if (adres.Length == 0) adres = "?";

        return r.Etykieta + "   (" + r.Com + " " + ((char)0x2192) + " " + adres + ":" + r.Port + ")";
    }

    /// <summary>Rotor odczytany z wiersza tabeli, bez zapisywania do konfiguracji.</summary>
    private Rotor RotorZWiersza(DataGridViewRow w)
    {
        _mapaPar.TryGetValue(Kom(w, RPara), out ParaPortow para);
        int.TryParse(Kom(w, RNr), out int nr);
        int.TryParse(Kom(w, RPort), out int port);

        return new Rotor
        {
            Nr = nr,
            Nazwa = Kom(w, RNazwa),
            Com = para?.A ?? "",
            Dev = para?.B ?? "",
            Ip = Kom(w, RIp),
            Port = port
        };
    }

    private static string Kom(DataGridViewRow w, int k)
        => (Convert.ToString(w.Cells[k].Value) ?? "").Trim();
}
