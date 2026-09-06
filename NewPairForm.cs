namespace RotorPanel;

/// <summary>Okienko zakladania nowej pary portow com0com.</summary>
public class NewPairForm : Form
{
    private TextBox _polePstRotator, _poleMostek;
    private Label _uwaga;

    /// <summary>Nazwa portu widocznego dla PstRotatora.</summary>
    public string NazwaA { get; private set; } = "";

    /// <summary>Nazwa portu, ktory otwiera mostek.</summary>
    public string NazwaB { get; private set; } = "";

    public NewPairForm()
    {
        Text            = "Nowa para portów";
        ClientSize      = new Size(430, 250);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox     = false;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        int wolny = Com0Com.PierwszyWolnyNumer();

        Controls.Add(Ui.Etykieta(
            "Para to dwa połączone porty wirtualne. Jeden otwiera PstRotator, drugi mostek.",
            Theme.Maly(), Theme.TekstSzary, new Point(20, 16), new Size(390, 18)));

        var karta = new Karta { Location = new Point(18, 42), Size = new Size(394, 130) };
        Controls.Add(karta);

        karta.Controls.Add(Ui.Etykieta("Port dla PstRotatora", Theme.Maly(), Theme.TekstSzary,
            new Point(18, 16), new Size(180, 16)));
        _polePstRotator = Pole("COM" + wolny, new Point(18, 34));
        karta.Controls.Add(_polePstRotator);

        karta.Controls.Add(Ui.Etykieta("Port dla mostka", Theme.Maly(), Theme.TekstSzary,
            new Point(210, 16), new Size(180, 16)));
        _poleMostek = Pole("CNCB" + wolny, new Point(210, 34));
        karta.Controls.Add(_poleMostek);

        karta.Controls.Add(Ui.Etykieta(
            "Strona PstRotatora musi nazywać się COMxx, bo tego wymagają programy " +
            "korzystające z portu. Druga strona może mieć dowolną nazwę.",
            Theme.Maly(), Theme.TekstSzary, new Point(18, 70), new Size(360, 36)));

        _uwaga = Ui.Etykieta("", Theme.Maly(), Color.FromArgb(0xB3, 0x26, 0x1E),
            new Point(20, 182), new Size(390, 18));
        Controls.Add(_uwaga);

        var utworz = Ui.Przycisk("Utwórz", 110, glowny: true);
        utworz.Location = new Point(190, 206);
        utworz.Click += (_, _) => Zatwierdz();
        Controls.Add(utworz);

        var anuluj = Ui.Przycisk("Anuluj", 96);
        anuluj.Location = new Point(312, 206);
        anuluj.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(anuluj);

        AcceptButton = utworz;
        CancelButton = anuluj;
    }

    private static TextBox Pole(string tekst, Point poz) => new()
    {
        Text = tekst,
        Location = poz,
        Width = 166,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Zwykly()
    };

    private void Zatwierdz()
    {
        string a = _polePstRotator.Text.Trim();
        string b = _poleMostek.Text.Trim();

        if (a.Length == 0 || b.Length == 0)
        {
            Blad("Obie nazwy muszą być wypełnione.");
            return;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            Blad("Obie strony pary nie mogą nazywać się tak samo.");
            return;
        }

        if (!a.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            Blad("Strona dla PstRotatora musi nazywać się COMxx.");
            return;
        }

        foreach (var nazwa in new[] { a, b })
        {
            if (nazwa.IndexOfAny(new[] { ' ', '\t', ',', '=' }) >= 0)
            {
                Blad("Nazwa portu nie może zawierać spacji, przecinków ani znaku równości.");
                return;
            }

            if (Com0Com.NazwaZajetaPrzezPare(nazwa))
            {
                Blad("Port " + nazwa + " należy już do istniejącej pary.");
                return;
            }

            if (PortIo.Opis(nazwa) != "nie istnieje")
            {
                Blad("Port " + nazwa + " już istnieje w systemie.");
                return;
            }
        }

        NazwaA = a;
        NazwaB = b;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Blad(string tekst) => _uwaga.Text = tekst;
}
