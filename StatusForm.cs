namespace RotorPanel;

/// <summary>
/// Wszystko, co niesie ramka statusu wzmacniacza SPE - bez podgladu wyswietlacza.
///
/// Ten widok jest celowo niezalezny od trybu RCU: dziala takze wtedy, gdy do pary
/// portow wpiety jest program kliencki i sterowanie z naszej strony jest zablokowane.
/// Stan mamy wtedy z ramek, o ktore poprosil klient - czytamy je po drodze, zamiast
/// dokladac wlasne zapytania.
/// </summary>
public sealed class StatusForm : Form
{
    private readonly Mostek _mostek;
    private readonly System.Windows.Forms.Timer _zegar;

    private readonly Label _naglowek, _stanDanych, _klopot;
    private readonly PasekLed _pasekMocy, _pasekPradu, _pasekNapiecia, _pasekSwr;
    private readonly Label _opisMocy, _opisPradu, _opisNapiecia, _opisSwr;
    private readonly Dictionary<string, Label> _pola = new();

    // Skale miernikow wziete z samego panelu: linijki na ekranach Operate i V PA maja
    // podzialki 0-50 A oraz 0-60 V (zmierzone w ramkach 0x6A). Zakres mocy zalezy od
    // modelu i liczymy go z pola identyfikacyjnego ramki.
    private const double MaksPrad = 50;
    private const double MaksNapiecie = 60;
    private const double MaksSwr = 3;

    private static readonly string[] Wiersze =
    {
        "Model", "Tryb", "Pasmo", "Wejście", "Bank",
        "Antena TX", "ATU", "Antena RX", "Poziom mocy",
        "SWR anteny", "SWR ATU",
        "Temperatura górna", "Temperatura dolna", "Temperatura sumatora"
    };

    /// <summary>Otwiera podglad stanu albo przywraca juz otwarty dla tego mostka.</summary>
    public static void Pokaz(Mostek mostek, string tytul)
        => OknaMostka.Pokaz(mostek, () => new StatusForm(mostek, tytul));

    public StatusForm(Mostek mostek, string tytul)
    {
        _mostek = mostek;

        Text            = tytul;
        ClientSize      = new Size(460, 470);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox     = true;
        MaximizeBox     = false;
        ShowInTaskbar   = true;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        var karta = new Karta { Location = new Point(16, 14), Size = new Size(428, 66) };
        Controls.Add(karta);

        _naglowek = Ui.Etykieta("", Theme.Nazwa(), Theme.Tekst,
            new Point(16, 8), new Size(396, 22));
        karta.Controls.Add(_naglowek);

        _klopot = Ui.Etykieta("", Theme.Maly(), Color.FromArgb(0xB3, 0x26, 0x1E),
            new Point(16, 30), new Size(396, 16));
        karta.Controls.Add(_klopot);

        _stanDanych = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(16, 46), new Size(396, 16));
        karta.Controls.Add(_stanDanych);

        var mierniki = new Karta { Location = new Point(16, 90), Size = new Size(428, 206) };
        Controls.Add(mierniki);

        int y = 12;
        (_pasekMocy, _opisMocy)         = Miernik(mierniki, "Moc wyjściowa", ref y);
        (_pasekPradu, _opisPradu)       = Miernik(mierniki, "Prąd PA", ref y);
        (_pasekNapiecia, _opisNapiecia) = Miernik(mierniki, "Napięcie PA", ref y);
        (_pasekSwr, _opisSwr)           = Miernik(mierniki, "SWR anteny", ref y);
        mierniki.Height = y + 4;

        _pasekPradu.Maksimum = MaksPrad;
        _pasekNapiecia.Maksimum = MaksNapiecie;
        _pasekSwr.Maksimum = MaksSwr;

        // Napiecie zasilania nie jest miernikiem "im wiecej tym gorzej" - u niego
        // caly zakres roboczy jest w porzadku, wiec nie strasz kolorami.
        _pasekNapiecia.ProgPomaranczowy = 1.0;
        _pasekNapiecia.ProgCzerwony = 1.0;
        _pasekSwr.ProgPomaranczowy = 0.5;   // SWR 1,5
        _pasekSwr.ProgCzerwony = 0.67;      // SWR 2,0

        y = mierniki.Bottom + 12;
        var tabela = new Karta { Location = new Point(16, y), Size = new Size(428, 24 * Wiersze.Length + 12) };
        Controls.Add(tabela);

        for (int i = 0; i < Wiersze.Length; i++)
        {
            tabela.Controls.Add(Ui.Etykieta(Wiersze[i], Theme.Maly(), Theme.TekstSzary,
                new Point(16, 10 + i * 24), new Size(190, 18)));

            var wartosc = Ui.Etykieta("—", Theme.Zwykly(), Theme.Tekst,
                new Point(210, 8 + i * 24), new Size(202, 18));
            tabela.Controls.Add(wartosc);
            _pola[Wiersze[i]] = wartosc;
        }

        int wysokosc = y + tabela.Height + 20;
        ClientSize = new Size(460, wysokosc);

        // Status przychodzi najwyzej raz na sekunde, a przy otwartym podgladzie
        // wyswietlacza wcale, wiec czestsze odswiezanie to sama praca na watku
        // interfejsu - dzielonym z rysowaniem ekranu wzmacniacza.
        _zegar = new System.Windows.Forms.Timer { Interval = 500 };
        _zegar.Tick += (_, _) => Odswiez();
        _zegar.Start();
        Odswiez();

        // Dopoki to okno jest otwarte, ramka statusu ma pierwszenstwo - odpytywanie
        // idzie takze przy wlaczonym podgladzie wyswietlacza.
        _mostek.TrybStanu = true;
        FormClosed += (_, _) => { _mostek.TrybStanu = false; _zegar.Dispose(); };
        Load += (_, _) => Ui.DopasujDoEkranu(this, new Size(460, wysokosc));
    }

    /// <summary>Linijka z podpisem i odczytem liczbowym po prawej.</summary>
    private (PasekLed, Label) Miernik(Control rodzic, string podpis, ref int y)
    {
        rodzic.Controls.Add(Ui.Etykieta(podpis, Theme.Maly(), Theme.TekstSzary,
            new Point(16, y), new Size(200, 16)));

        var odczyt = Ui.Etykieta("—", Theme.Zwykly(), Theme.Tekst,
            new Point(216, y - 2), new Size(196, 18));
        odczyt.TextAlign = ContentAlignment.MiddleRight;
        rodzic.Controls.Add(odczyt);

        var pasek = new PasekLed
        {
            Location = new Point(16, y + 18),
            Size = new Size(396, 14)
        };
        rodzic.Controls.Add(pasek);

        y += 50;
        return (pasek, odczyt);
    }

    private StatusSpe _pokazany;
    private StanMostka _pokazanyStan = (StanMostka)(-1);

    private void Odswiez()
    {
        var s = _mostek.Status;

        // Nowa ramka to nowy obiekt, wiec porownanie referencji wystarcza. Bez tego
        // przemalowywalibysmy cztery linijki i czternascie etykiet dwa razy na sekunde
        // bez zadnego powodu - a ten sam watek rysuje wyswietlacz w oknie sterowania.
        // Wiek odczytu pokazujemy dalej, wiec licznik sekund musi isc mimo wszystko.
        bool zmiana = !ReferenceEquals(s, _pokazany) || _mostek.Stan != _pokazanyStan;
        _pokazany = s;
        _pokazanyStan = _mostek.Stan;

        if (s is null)
        {
            _naglowek.Text = "brak odczytu stanu";
            _naglowek.ForeColor = Theme.TekstSzary;
            _klopot.Text = "";
            _stanDanych.Text = _mostek.Stan == StanMostka.Polaczony
                ? "Mostek połączony — czekam na pierwszą ramkę statusu."
                : "Mostek rozłączony.";
            foreach (var pole in _pola.Values) pole.Text = "—";
            _pasekMocy.Wyczysc(); _pasekPradu.Wyczysc();
            _pasekNapiecia.Wyczysc(); _pasekSwr.Wyczysc();
            _opisMocy.Text = _opisPradu.Text = _opisNapiecia.Text = _opisSwr.Text = "—";
            return;
        }

        double wiek = (DateTime.UtcNow - s.Kiedy).TotalSeconds;

        _naglowek.Text = (s.Operate ? "Operate" : "Standby") + "  ·  " + (s.Nadaje ? "TX" : "RX");
        _naglowek.ForeColor = s.Nadaje ? Color.FromArgb(0xB3, 0x26, 0x1E) : Theme.Tekst;

        _klopot.Text = s.Klopot;
        _stanDanych.Text = wiek < 5
            ? "Odczyt sprzed " + wiek.ToString("0.0") + " s" +
              (_mostek.KlientNaPorcie ? " — z ramek programu na porcie." : ".")
            : _mostek.TrybEkranu
                ? "Odczyt sprzed " + wiek.ToString("0") +
                  " s — przy otwartym podglądzie wyświetlacza stan idzie rzadziej."
                : "Odczyt sprzed " + wiek.ToString("0") + " s — wzmacniacz nie odpowiada.";

        if (!zmiana) return;   // wiek juz odswiezony, reszta bez zmian

        _pasekMocy.Maksimum = s.MocMaksymalna;
        _pasekMocy.Wartosc = s.MocWatow;
        _pasekPradu.Wartosc = s.PradAmperow;
        _pasekNapiecia.Wartosc = s.NapiecieWoltow;
        _pasekSwr.Wartosc = s.SwrLiczba;

        _opisMocy.Text     = s.Moc + " W  /  " + s.MocMaksymalna.ToString("0") + " W";
        _opisPradu.Text    = s.PradPa + " A";
        _opisNapiecia.Text = s.NapieciePa + " V";
        _opisSwr.Text      = s.SwrAnteny.Length > 0 ? s.SwrAnteny : "—";

        string stopnie = " " + (char)0x00B0 + "C";
        Ustaw("Model", s.Model);
        Ustaw("Tryb", s.Operate ? "Operate" : "Standby");
        Ustaw("Pasmo", s.Pasmo);
        Ustaw("Wejście", s.Wejscie);
        Ustaw("Bank", s.Bank);
        Ustaw("Antena TX", s.Antena);
        Ustaw("ATU", s.OpisAtu);
        Ustaw("Antena RX", s.AntenaRx);
        Ustaw("Poziom mocy", s.OpisPoziomuMocy);
        Ustaw("SWR anteny", s.SwrAnteny);
        Ustaw("SWR ATU", s.SwrAtu);
        Ustaw("Temperatura górna", s.Temperatura + stopnie);
        Ustaw("Temperatura dolna", s.TemperaturaDolna + stopnie);
        Ustaw("Temperatura sumatora", s.TemperaturaSumatora + stopnie);
    }

    private void Ustaw(string nazwa, string wartosc)
    {
        if (_pola.TryGetValue(nazwa, out var pole))
            pole.Text = string.IsNullOrWhiteSpace(wartosc) ? "—" : wartosc;
    }
}
