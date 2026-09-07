namespace RotorPanel;

/// <summary>Zarzadzanie parami portow com0com: podglad, tworzenie i usuwanie.</summary>
public class PairsForm : Form
{
    private readonly Config _cfg;
    private readonly Action _przedZmiana;
    private ListView _lista;
    private Label _porty;
    private Button _usunZaznaczona;

    public PairsForm(Config cfg, Action przedZmiana)
    {
        _cfg = cfg;
        _przedZmiana = przedZmiana;

        Text            = "Pary portów com0com";
        ClientSize      = new Size(620, 400);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox     = false;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        Controls.Add(Ui.Etykieta(
            "Każdy rotor potrzebuje jednej pary. Lewą stronę wybierasz w PstRotatorze, " +
            "prawą otwiera mostek.",
            Theme.Maly(), Theme.TekstSzary, new Point(20, 16), new Size(580, 18)));

        BudujListe();

        _porty = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(20, 302), new Size(470, 32));
        Controls.Add(_porty);

        var usunWszystkie = Ui.Przycisk("Usuń wszystkie", 106);
        usunWszystkie.Location = new Point(496, 300);
        usunWszystkie.Height = 26;
        usunWszystkie.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
        usunWszystkie.Click += (_, _) => UsunWszystkie();
        Controls.Add(usunWszystkie);

        var nowa = Ui.Przycisk("Nowa para…", 124, glowny: true);
        nowa.Location = new Point(18, 352);
        nowa.Click += (_, _) => Nowa();
        Controls.Add(nowa);

        _usunZaznaczona = Ui.Przycisk("Usuń zaznaczoną", 150);
        _usunZaznaczona.Location = new Point(150, 352);
        _usunZaznaczona.Click += (_, _) => UsunZaznaczona();
        Controls.Add(_usunZaznaczona);

        var odswiez = Ui.Przycisk("Odśwież", 96);
        odswiez.Location = new Point(308, 352);
        odswiez.Click += (_, _) => Odswiez();
        Controls.Add(odswiez);

        var zamknij = Ui.Przycisk("Zamknij", 96);
        zamknij.Location = new Point(506, 352);
        zamknij.Click += (_, _) => Close();
        Controls.Add(zamknij);

        CancelButton = zamknij;

        Odswiez();

        Load += (_, _) => Ui.DopasujDoEkranu(this, new Size(620, 400));
    }

    private void BudujListe()
    {
        _lista = new ListView
        {
            Location = new Point(18, 44),
            Size = new Size(584, 250),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Karta,
            ForeColor = Theme.Tekst,
            Font = Theme.Zwykly()
        };

        _lista.Columns.Add("Nr", 40);
        _lista.Columns.Add("Port dla PstRotatora", 150);
        _lista.Columns.Add("Stan", 96);
        _lista.Columns.Add("Port mostka", 140);
        _lista.Columns.Add("Stan", 96);

        _lista.SelectedIndexChanged += (_, _) =>
            _usunZaznaczona.Enabled = _lista.SelectedItems.Count > 0;

        Controls.Add(_lista);
    }

    private void Odswiez()
    {
        string zaznaczony = _lista.SelectedItems.Count > 0
            ? _lista.SelectedItems[0].SubItems[0].Text
            : null;

        _lista.BeginUpdate();
        _lista.Items.Clear();

        foreach (var para in Com0Com.Pary())
        {
            var wiersz = new ListViewItem(new[]
            {
                para.Numer,
                para.A,
                PortIo.Opis(para.A),
                para.B,
                PortIo.Opis(para.B)
            })
            { Tag = para };

            if (!para.Istnieje)
            {
                // Wpis w rejestrze po usunietej parze - com0com ich nie sprzata.
                wiersz.ForeColor = Theme.TekstSzary;
                wiersz.SubItems[2].Text = "osierocony";
                wiersz.SubItems[4].Text = "osierocony";
            }
            else if (_cfg.WszystkiePolaczenia.Any(p => p.Gotowy &&
                    string.Equals(p.Dev, para.B, StringComparison.OrdinalIgnoreCase)))
            {
                wiersz.Font = new Font(_lista.Font, FontStyle.Bold);
            }

            _lista.Items.Add(wiersz);

            if (para.Numer == zaznaczony) wiersz.Selected = true;
        }

        _lista.EndUpdate();
        _usunZaznaczona.Enabled = _lista.SelectedItems.Count > 0;

        int osierocone = _lista.Items.Count - _lista.Items.Cast<ListViewItem>()
            .Count(w => w.Tag is ParaPortow p && p.Istnieje);

        if (_lista.Items.Count == 0)
        {
            _porty.Text = "Brak par. Użyj „Nowa para…”, żeby założyć pierwszą.";
        }
        else
        {
            string ogon = osierocone > 0
                ? "  Szare wiersze to pozostałości po usuniętych parach — można je usunąć."
                : "";
            _porty.Text = "Par w systemie: " + (_lista.Items.Count - osierocone) +
                          ".  Pogrubione są używane przez konfigurację." + ogon +
                          Environment.NewLine +
                          "Pierwszy wolny numer portu: COM" + Com0Com.PierwszyWolnyNumer() + ".";
        }
    }

    private void Nowa()
    {
        using var okno = new NewPairForm();
        if (okno.ShowDialog(this) != DialogResult.OK) return;

        _przedZmiana();
        Com0Com.Wykonaj(_cfg,
            new[] { "install PortName=" + okno.NazwaA + " PortName=" + okno.NazwaB, "list" },
            "Tworzenie pary " + okno.NazwaA + " - " + okno.NazwaB, this);
        Odswiez();
    }

    private void UsunZaznaczona()
    {
        if (_lista.SelectedItems.Count == 0) return;
        if (_lista.SelectedItems[0].Tag is not ParaPortow para) return;

        var uzywajace = _cfg.WszystkiePolaczenia
            .Where(p => p.Gotowy && string.Equals(p.Dev, para.B, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Etykieta)
            .ToList();

        string ostrzezenie = uzywajace.Count > 0
            ? Environment.NewLine + Environment.NewLine +
              "UWAGA: tej pary używa: " + string.Join(", ", uzywajace) +
              ". Po usunięciu te rotory przestaną działać, dopóki nie wskażesz im innej pary."
            : "";

        string co = para.Istnieje
            ? "Usunąć parę " + para.A + " ⇄ " + para.B + " ?"
            : "Usunąć osierocony wpis po parze " + para.A + " ⇄ " + para.B + " ?";

        var odp = MessageBox.Show(this, co + ostrzezenie,
            "RotorPanel", MessageBoxButtons.YesNo,
            uzywajace.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question);

        if (odp != DialogResult.Yes) return;

        // setupc kasuje urzadzenia, ale zostawia wpisy w rejestrze - bez ich usuniecia
        // po kazdym skasowaniu pary zostawalby duch.
        var linie = new List<string>();
        if (para.Istnieje) linie.Add(Com0Com.LiniaSetupc(_cfg, "remove " + para.Numer));
        linie.AddRange(Com0Com.LinieSprzatajaceRejestr(para.Numer));
        linie.Add(Com0Com.LiniaSetupc(_cfg, "list"));

        _przedZmiana();
        Com0Com.WykonajLinie(_cfg, linie,
            "Usuwanie pary " + para.A + " - " + para.B, this);
        Odswiez();
    }

    private void UsunWszystkie()
    {
        var odp = MessageBox.Show(this,
            "Zostaną usunięte WSZYSTKIE pary com0com w systemie, także te używane przez inne programy."
            + Environment.NewLine + Environment.NewLine + "Kontynuować?",
            "RotorPanel", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (odp != DialogResult.Yes) return;

        var numery = Com0Com.Pary().Select(x => x.Numer).Distinct().ToList();

        var linie = new List<string> { Com0Com.LiniaSetupc(_cfg, "uninstall") };
        foreach (var numer in numery) linie.AddRange(Com0Com.LinieSprzatajaceRejestr(numer));
        linie.Add(Com0Com.LiniaSetupc(_cfg, "list"));

        _przedZmiana();
        Com0Com.WykonajLinie(_cfg, linie, "Usuwanie wszystkich par com0com", this);
        Odswiez();
    }
}
