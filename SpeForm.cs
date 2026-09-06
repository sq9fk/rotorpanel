namespace RotorPanel;

/// <summary>
/// Klawiatura wzmacniacza SPE Expert. Kody klawiszy pochodza wprost z firmowego
/// Application Programmer's Guide (rev. 1.1) - kazdy jest odpowiednikiem nacisniecia
/// przycisku na przednim panelu.
///
/// Po otwarciu okna wlaczamy tryb RCU (0x80), w ktorym wzmacniacz przysyla ramki
/// 0x6A z zawartoscia wyswietlacza - dzieki temu po jego menu da sie chodzic,
/// a nie klikac na slepo. Przy zamknieciu tryb jest wylaczany (0x81).
/// </summary>
public sealed class SpeForm : Form
{
    private readonly Mostek _mostek;
    private readonly Label _stan, _stopka;
    private readonly PodgladLcd _lcd;
    private readonly System.Windows.Forms.Timer _zegar, _puls;
    private DateTime _ostatniPuls = DateTime.MinValue;

    // Puls wypadajacy tuz po klawiszu gubi ten klawisz, wiec na czas jego obslugi
    // wstrzymujemy pulsowanie z zegara.
    private bool _klawiszWToku;

    // Klawisze, ktore zmieniaja stan nadawania albo zasilania - pytamy przed wyslaniem.
    private static readonly byte[] Ostrozne = { 0x09, 0x0A, 0x0B, 0x0D };

    private readonly struct Klawisz
    {
        public readonly string Napis;
        public readonly byte Kod;
        public readonly string Opis;

        public Klawisz(string napis, byte kod, string opis)
        {
            Napis = napis;
            Kod = kod;
            Opis = opis;
        }
    }

    private static readonly Klawisz[][] Uklad =
    {
        new[]
        {
            new Klawisz("INPUT", 0x01, "przełącza wejście"),
            new Klawisz("ANT",   0x04, "przełącza antenę"),
            new Klawisz("BAND −", 0x02, "pasmo w dół"),
            new Klawisz("BAND +", 0x03, "pasmo w górę")
        },
        new[]
        {
            new Klawisz("L −", 0x05, "strojnik: L w dół"),
            new Klawisz("L +", 0x06, "strojnik: L w górę"),
            new Klawisz("C −", 0x07, "strojnik: C w dół"),
            new Klawisz("C +", 0x08, "strojnik: C w górę")
        },
        new[]
        {
            new Klawisz("◀", 0x0F, "strzałka w lewo"),
            new Klawisz("▶", 0x10, "strzałka w prawo"),
            new Klawisz("SET", 0x11, "wchodzi w menu i zatwierdza wybór"),
            new Klawisz("CAT", 0x0E, "ustawienia CAT")
        },
        new[]
        {
            new Klawisz("DISPLAY", 0x0C, "przełącza widok wyświetlacza"),
            new Klawisz("Podświetlenie wł.", 0x82, "włącza podświetlenie wyświetlacza"),
            new Klawisz("Podświetlenie wył.", 0x83, "wyłącza podświetlenie wyświetlacza")
        },
        new[]
        {
            new Klawisz("OPERATE", 0x0D, "przełącza Operate / Standby"),
            new Klawisz("TUNE", 0x09, "uruchamia strojenie"),
            new Klawisz("POWER", 0x0B, "zmienia poziom mocy"),
            new Klawisz("WYŁĄCZ", 0x0A, "wyłącza wzmacniacz")
        }
    };

    public SpeForm(Mostek mostek, string tytul)
    {
        _mostek = mostek;

        Text            = tytul;
        ClientSize      = new Size(516, 584);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox     = false;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        var karta = new Karta { Location = new Point(18, 16), Size = new Size(480, 74) };
        Controls.Add(karta);

        _stan = Ui.Etykieta("", Theme.Zwykly(), Theme.Tekst,
            new Point(16, 8), new Size(448, 20));
        karta.Controls.Add(_stan);

        karta.Controls.Add(Ui.Etykieta(
            "Podgląd wyświetlacza działa w trybie RCU — włączanym na czas tego okna.",
            Theme.Maly(), Theme.TekstSzary, new Point(16, 30), new Size(448, 16)));

        karta.Controls.Add(Ui.Etykieta(
            "Dopóki mostek jest połączony, strona RC-1216H nie odświeża stanu.",
            Theme.Maly(), Theme.TekstSzary, new Point(16, 48), new Size(448, 16)));

        // Podglad wyswietlacza: pieciu wierszy po 32 znaki, czcionka o stalej
        // szerokosci, zeby kolumny stoly tak jak na panelu wzmacniacza.
        _lcd = new PodgladLcd { Location = new Point(18, 100), Size = new Size(480, 180) };
        Controls.Add(_lcd);

        int y = 296;
        foreach (var rzad in Uklad)
        {
            int x = 18;
            foreach (var k in rzad)
            {
                var przycisk = Przycisk(k);
                przycisk.Location = new Point(x, y);
                Controls.Add(przycisk);
                x += przycisk.Width + 8;
            }
            y += 44;
        }

        _stopka = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(18, y + 4), new Size(480, 32));
        Controls.Add(_stopka);

        _zegar = new System.Windows.Forms.Timer { Interval = 60 };
        _zegar.Tick += (_, _) => Odswiez();
        _zegar.Start();
        Odswiez();

        // Wzmacniacz nie przysyla ekranu sam z siebie - nawet po nacisnieciu klawisza.
        // Swieza klatke wymusza dopiero przelaczenie RCU wylacz/wlacz, a od polecenia
        // do ramki mija u niego okolo pol sekundy (zmierzone). Dlatego nie pulsujemy
        // na sztywny takt, tylko zaraz po tym, jak przyjdzie poprzednia klatka.
        _puls = new System.Windows.Forms.Timer { Interval = 120 };
        _puls.Tick += async (_, _) => await PulsGdyTrzeba();

        Shown += async (_, _) =>
        {
            _mostek.TrybEkranu = true;
            await _mostek.WyslijKlawisz(EkranSpe.RcuWlacz, CancellationToken.None);
            _puls.Start();
        };

        FormClosing += (_, _) =>
        {
            _puls.Stop();
            _mostek.TrybEkranu = false;
            _mostek.WyslijKlawisz(EkranSpe.RcuWylacz, CancellationToken.None)
                   .GetAwaiter().GetResult();
        };

        FormClosed += (_, _) => { _zegar.Dispose(); _puls.Dispose(); };
    }

    private Button Przycisk(Klawisz k)
    {
        bool ostrozny = Array.IndexOf(Ostrozne, k.Kod) >= 0;

        int szerokosc = k.Napis.Length > 10 ? 152 : 112;
        var przycisk = Ui.Przycisk(k.Napis, szerokosc, glowny: false);
        if (ostrozny) przycisk.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);

        przycisk.Click += async (_, _) =>
        {
            if (ostrozny && !Potwierdz(k)) return;
            await Nacisnij(k);
        };

        return przycisk;
    }

    private bool Potwierdz(Klawisz k)
        => MessageBox.Show(this,
               "Wysłać do wzmacniacza klawisz " + k.Napis + "?" + Environment.NewLine +
               Environment.NewLine + k.Opis + ".",
               "Potwierdzenie", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
               MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private async Task Nacisnij(Klawisz k)
    {
        bool poszlo = await _mostek.WyslijKlawisz(k.Kod, CancellationToken.None);

        _stopka.Text = poszlo
            ? k.Napis + " wysłany (kod 0x" + k.Kod.ToString("X2") + ") — " + k.Opis
            : "Nie wysłano: mostek nie jest połączony.";

        _stopka.ForeColor = poszlo ? Theme.TekstSzary : Color.FromArgb(0xB3, 0x26, 0x1E);

        if (!poszlo) return;

        // Puls zaraz po klawiszu gubi go: przy zwloce 0 i 20 ms wzmacniacz nie zmienia
        // ekranu w ogole, przy 60 ms nowa klatka jest po ~550 ms, przy 200 ms po ~720 ms.
        // 60 ms to zmierzone minimum, ktore dziala. Na ten czas wstrzymujemy tez puls
        // z zegara, bo trafiony w zla chwile kasuje klawisz.
        _klawiszWToku = true;
        try
        {
            await Task.Delay(60);
            await Puls();
        }
        finally { _klawiszWToku = false; }
    }

    /// <summary>
    /// RCU OFF, chwila, RCU ON. Wzmacniacz traktuje to jak nowe podlaczenie podgladu
    /// i przysyla ekran nawet wtedy, gdy nic sie na nim nie zmienilo. Przerwa miedzy
    /// poleceniami jest potrzebna - przy 5 ms wzmacniacz juz nie reaguje.
    /// </summary>
    private async Task Puls()
    {
        if (_mostek.Stan != StanMostka.Polaczony) return;

        _ostatniPuls = DateTime.UtcNow;
        await _mostek.WyslijKlawisz(EkranSpe.RcuWylacz, CancellationToken.None);
        await Task.Delay(20);
        await _mostek.WyslijKlawisz(EkranSpe.RcuWlacz, CancellationToken.None);
    }

    /// <summary>
    /// Pulsuje dopiero wtedy, gdy poprzednia klatka juz przyszla - inaczej polecenia
    /// pietrzylyby sie szybciej, niz wzmacniacz zdazy odpowiedziec. Po sekundzie bez
    /// odpowiedzi probujemy mimo wszystko, zeby podglad nie zamarl na dobre.
    /// </summary>
    private async Task PulsGdyTrzeba()
    {
        if (_klawiszWToku) return;

        var ekran = _mostek.Ekran;
        bool klatkaPoPulsie = ekran is not null && ekran.Kiedy > _ostatniPuls;
        bool czekamyZaDlugo = DateTime.UtcNow - _ostatniPuls > TimeSpan.FromSeconds(1);

        if (klatkaPoPulsie || czekamyZaDlugo) await Puls();
    }

    private void PokazEkran()
    {
        var ekran = _mostek.Ekran;
        bool swiezy = ekran is not null && (DateTime.UtcNow - ekran.Kiedy).TotalSeconds < 6;

        _lcd.Zastepczy = _mostek.Stan == StanMostka.Polaczony
            ? "czekam na wyświetlacz…"
            : "mostek rozłączony";

        // Podmieniamy tylko przy nowej klatce - inaczej kontrolka przerysowywalaby sie
        // kilkanascie razy na sekunde bez powodu.
        var doPokazania = swiezy ? ekran : null;
        if (!ReferenceEquals(_lcd.Ekran, doPokazania)) _lcd.Ekran = doPokazania;
    }

    private void Odswiez()
    {
        PokazEkran();

        var status = _mostek.Status;
        bool swiezy = status is not null && (DateTime.UtcNow - status.Kiedy).TotalSeconds < 5;

        if (!swiezy)
        {
            _stan.Text = _mostek.Stan == StanMostka.Polaczony
                ? "czekam na odczyt stanu…"
                : "mostek rozłączony";
            _stan.ForeColor = Theme.TekstSzary;
            return;
        }

        string klopot = status.Klopot;
        _stan.Text = klopot.Length > 0 ? status.Opis + "   —   " + klopot : status.Opis;
        _stan.ForeColor = klopot.Length > 0 ? Color.FromArgb(0xB3, 0x26, 0x1E) : Theme.Tekst;
    }
}
