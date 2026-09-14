namespace RotorPanel;

/// <summary>Akcje okna ustawien: pary, wybor pliku, pobieranie nazw, zapis.</summary>
public partial class SettingsForm
{
    /// <summary>
    /// Lista par do wyboru przy rotorach. Osierocone wpisy po usunietych parach
    /// pomijamy, a pary uzywane przez konfiguracje dokladamy nawet gdy sterownik
    /// ich chwilowo nie zglasza - zeby ustawienie nie zniknelo po cichu.
    /// </summary>
    private void WczytajPary()
    {
        _mapaPar.Clear();
        _kolPara.Items.Clear();
        _kolPara.Items.Add(Brak);

        foreach (var para in Com0Com.Pary().Where(p => p.Istnieje))
        {
            if (_mapaPar.ContainsKey(para.Opis)) continue;
            _mapaPar[para.Opis] = para;
            _kolPara.Items.Add(para.Opis);
        }

        foreach (var r in _cfg.Rotory.Where(x => !string.IsNullOrWhiteSpace(x.Dev)))
        {
            var para = new ParaPortow { A = r.Com, B = r.Dev };
            if (_mapaPar.ContainsKey(para.Opis)) continue;
            _mapaPar[para.Opis] = para;
            _kolPara.Items.Add(para.Opis);
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
            CheckFileExists = true,
            InitialDirectory = KatalogStartowy()
        };

        if (okno.ShowDialog(this) != DialogResult.OK) return;

        // W konfiguracji trzymamy sciezki z ukosnikami w przod.
        _setupc.Text = okno.FileName.Replace((char)92, '/');
    }

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

            foreach (DataGridViewRow w in _siatkaAnten.Rows)
            {
                if (!int.TryParse(Kom(w, ANr), out int nr)) continue;
                if (!nazwy.TryGetValue(nr, out string nazwa) || string.IsNullOrWhiteSpace(nazwa)) continue;
                if (Kom(w, ANazwa) == nazwa) continue;
                w.Cells[ANazwa].Value = nazwa;
                zmienione++;
            }

            _info.Text = "Pobrano " + nazwy.Count + ", zmieniono " + zmienione + ".";
        }
        catch (Exception ex)
        {
            _info.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
            _info.Text = "Błąd: " + ex.Message;
        }
    }

    private void Zapisz()
    {
        _siatkaRotorow.EndEdit();
        _siatkaUrzadzen.EndEdit();
        _siatkaAnten.EndEdit();

        var rotory = new List<Rotor>();
        foreach (DataGridViewRow w in _siatkaRotorow.Rows)
        {
            var r = RotorZWiersza(w);
            if (r.Nr <= 0) continue;

            if (rotory.Any(x => x.Nr == r.Nr))
            {
                Ostrzez("Dwa rotory mają ten sam numer " + r.Nr + ".");
                return;
            }

            // Porownujemy etykiety, bo pusta nazwa i tak wyswietla sie jako "Rotor N"
            // i moze wejsc w kolizje z nazwa wpisana recznie.
            if (rotory.Any(x => string.Equals(x.Etykieta, r.Etykieta, StringComparison.OrdinalIgnoreCase)))
            {
                Ostrzez("Dwa rotory nazywają się „" + r.Etykieta + "”. " +
                        "Nazwy muszą być różne, bo po nich wybiera się rotor przy antenie.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(r.Dev) &&
                rotory.Any(x => x.KluczPary == r.KluczPary))
            {
                Ostrzez("Para " + r.Dev + " jest przypisana do dwóch rotorów. " +
                        "Anteny na wspólnym maszcie powinny wskazywać jeden rotor.");
                return;
            }

            rotory.Add(r);
        }

        var niekompletny = rotory.FirstOrDefault(
            r => !r.Gotowy && (!string.IsNullOrWhiteSpace(r.Dev) || r.Port > 0));
        if (niekompletny != null)
        {
            Ostrzez(niekompletny.Etykieta + " ma tylko część danych — potrzebna jest para portów " +
                    "oraz port TCP.");
            return;
        }

        var urzadzenia = new List<Urzadzenie>();
        foreach (DataGridViewRow w in _siatkaUrzadzen.Rows)
        {
            var u = UrzadzenieZWiersza(w);
            if (u.Nr <= 0) continue;

            if (urzadzenia.Any(x => string.Equals(x.Etykieta, u.Etykieta, StringComparison.OrdinalIgnoreCase)))
            {
                Ostrzez("Dwa urządzenia nazywają się „" + u.Etykieta + "”. Nazwy muszą być różne.");
                return;
            }

            if (!u.Gotowy && (!string.IsNullOrWhiteSpace(u.Dev) || u.Port > 0))
            {
                Ostrzez(u.Etykieta + " ma tylko część danych — potrzebna jest para portów " +
                        "oraz port TCP.");
                return;
            }

            urzadzenia.Add(u);
        }

        // Para moze nalezec tylko do jednego punktu, niezaleznie od tego, czy to
        // rotor czy urzadzenie - inaczej dwa mostki bilyby sie o ten sam port.
        var zajete = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in rotory.Cast<Polaczenie>().Concat(urzadzenia).Where(x => x.Gotowy))
        {
            if (zajete.TryGetValue(p.KluczPary, out string kto))
            {
                Ostrzez("Para " + p.Dev + " jest przypisana do dwóch pozycji: " +
                        kto + " oraz " + p.Etykieta + ".");
                return;
            }
            zajete[p.KluczPary] = p.Etykieta;
        }

        var anteny = new List<Antena>();
        foreach (DataGridViewRow w in _siatkaAnten.Rows)
        {
            int.TryParse(Kom(w, ANr), out int nr);
            int rotor = w.Tag is int t ? t : 0;
            if (rotor != 0 && rotory.All(r => r.Nr != rotor)) rotor = 0;

            anteny.Add(new Antena { Nr = nr, Nazwa = Kom(w, ANazwa), Rotor = rotor });
        }

        _cfg.AutoPolacz     = _autoPolacz.Checked;
        _cfg.SprawdzajAktualizacje = _sprawdzajAktualizacje.Checked;
        _cfg.PiIp           = _ip.Text.Trim();
        _cfg.SterownikAnten = Config.NormalizujHost(_sterownik.Text);
        _cfg.Setupc         = _setupc.Text.Trim();
        _cfg.Rotory         = rotory;
        _cfg.Urzadzenia     = urzadzenia;
        _cfg.Anteny         = anteny;

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

    private void Ostrzez(string tekst)
        => MessageBox.Show(this, tekst, "Ustawienia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
