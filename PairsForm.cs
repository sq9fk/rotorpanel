namespace RotorPanel;

/// <summary>Okno zarzadzania parami com0com - opcja dodatkowa.</summary>
public class PairsForm : Form
{
    private readonly Config _cfg;
    private readonly Action _przedZmiana;
    private TextBox _raport;

    public PairsForm(Config cfg, Action przedZmiana)
    {
        _cfg = cfg;
        _przedZmiana = przedZmiana;

        Text            = "Pary portow com0com";
        ClientSize      = new Size(560, 400);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox     = false;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        Controls.Add(Ui.Etykieta(
            "Pary lacza port widziany przez PstRotator z portem uzywanym przez mostek.",
            Theme.Zwykly(), Theme.TekstSzary, new Point(18, 16), new Size(520, 20)));

        var karta = new Karta { Location = new Point(18, 44), Size = new Size(524, 268) };
        Controls.Add(karta);

        _raport = new TextBox
        {
            Location = new Point(14, 14),
            Size = new Size(496, 240),
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = Theme.Mono(),
            BackColor = Theme.Karta,
            ForeColor = Theme.Tekst
        };
        karta.Controls.Add(_raport);

        var odswiez = Ui.Przycisk("Odswiez", 96);
        odswiez.Location = new Point(18, 328);
        odswiez.Click += (_, _) => Odswiez();
        Controls.Add(odswiez);

        var utworz = Ui.Przycisk("Utworz pary", 130, glowny: true);
        utworz.Location = new Point(122, 328);
        utworz.Click += (_, _) => Utworz();
        Controls.Add(utworz);

        var usun = Ui.Przycisk("Usun wszystkie pary", 160);
        usun.Location = new Point(260, 328);
        usun.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
        usun.Click += (_, _) => Usun();
        Controls.Add(usun);

        var zamknij = Ui.Przycisk("Zamknij", 90);
        zamknij.Location = new Point(452, 328);
        zamknij.Click += (_, _) => Close();
        Controls.Add(zamknij);

        Odswiez();
    }

    private void Odswiez() => _raport.Text = Com0Com.Raport(_cfg);

    private void Utworz()
    {
        _przedZmiana();
        var cmds = _cfg.Anteny.Where(a => a.Gotowa)
            .Select(a => "install PortName=" + a.Com + " PortName=" + a.Dev)
            .Append("list")
            .ToList();
        Com0Com.Wykonaj(_cfg, cmds, "Tworzenie par com0com", this);
        Odswiez();
    }

    private void Usun()
    {
        var odp = MessageBox.Show(this,
            "Zostana usuniete WSZYSTKIE pary com0com w systemie, takze te uzywane przez inne programy."
            + Environment.NewLine + Environment.NewLine + "Kontynuowac?",
            "RotorPanel", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (odp != DialogResult.Yes) return;

        _przedZmiana();
        Com0Com.Wykonaj(_cfg, new[] { "uninstall", "list" }, "Usuwanie par com0com", this);
        Odswiez();
    }
}
