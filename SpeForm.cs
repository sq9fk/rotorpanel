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

    // Puls wypadajacy blisko klawisza gubi ten klawisz, wiec na czas obslugi i chwile
    // po niej pulsowanie z zegara jest wstrzymane.
    private bool _klawiszWToku;
    private DateTime _ostatniKlawisz = DateTime.MinValue;

    // Kazdy zbedny puls kosztuje: przy takcie 1 s wzmacniacz odpowiadal kolejno po
    // 218, 1150, 1055 i 2170 ms, a klawisz wyslany 100 ms po pulsie przepadal.
    // Dlatego w tle pulsujemy rzadko, a po klawiszu robimy cisze.
    private static readonly TimeSpan CiszaPoKlawiszu = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan TaktSpoczynku   = TimeSpan.FromSeconds(3);

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

    // Otwarte okna sterowania, zeby dalo sie je zamknac, gdy do pary wepnie sie
    // program kliencki - dwoch panow na jednym laczu konczy sie gubieniem ramek.
    private static readonly List<SpeForm> _otwarte = new();

    /// <summary>Zamyka okno sterowania tym mostkiem, jesli akurat jest otwarte.</summary>
    public static void ZamknijOtwarte(Mostek mostek)
    {
        foreach (var okno in _otwarte.ToArray())
            if (ReferenceEquals(okno._mostek, mostek) && !okno.IsDisposed)
                try { okno.Close(); } catch { /* zamykane w innym watku */ }
    }

    public SpeForm(Mostek mostek, string tytul)
    {
        _mostek = mostek;
        _otwarte.Add(this);

        Text            = tytul;
        ClientSize      = new Size(516, 520);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
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

        // Podglad wyswietlacza: 40 na 8 komorek po 6 na 8 pikseli panelu, kazdy
        // powiekszony dwukrotnie - stad 480 na 128 punktow. Kontrolka jest o osiem
        // wieksza, zeby zostala ramka po cztery piksele, a samo szklo wypadlo w tej
        // samej osi co karta wyzej.
        _lcd = new PodgladLcd { Location = new Point(14, 96), Size = new Size(488, 136) };
        Controls.Add(_lcd);

        int y = 252;
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

        _stopka = Ui.Etykieta(
            "Ctrl+S zapisuje bieżącą ramkę wyświetlacza do podkatalogu „ekrany”.",
            Theme.Maly(), Theme.TekstSzary, new Point(18, y + 4), new Size(480, 32));
        Controls.Add(_stopka);

        _zegar = new System.Windows.Forms.Timer { Interval = 60 };
        _zegar.Tick += (_, _) => Odswiez();
        _zegar.Start();
        Odswiez();

        // Wzmacniacz nie przysyla ekranu sam z siebie i jedna klatka zajmuje mu okolo
        // pol sekundy. Kazdy puls w tle odbiera mu uwage: zmierzone przy takcie 250 ms
        // przepadaly 3 klawisze na 6, przy 600 ms jeden, a bez pulsu w tle - zaden.
        // Dlatego pulsujemy po klawiszu, a w tle dopiero gdy uzytkownik nic nie robi.
        _puls = new System.Windows.Forms.Timer { Interval = 250 };
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

            // Na wzmacniacz nie czekamy tutaj. Blokujace GetResult zawieszalo caly
            // program: zapis czeka na semafor mostka, a jego kontynuacja wraca na watek
            // interfejsu, ktory wlasnie na tym GetResult stoi. Efekt byl taki, ze okno
            // nie dawalo sie zamknac, zasobnik przestawal odpowiadac i zostawalo tylko
            // zabicie procesu. RCU wylaczamy wiec w tle, a tryb ekranu gasimy dopiero
            // po wyslaniu, zeby odpytywanie o stan nie wcisnelo sie przed nim.
            var mostek = _mostek;
            _ = Task.Run(async () =>
            {
                try { await mostek.WyslijKlawisz(EkranSpe.RcuWylacz, CancellationToken.None); }
                finally { mostek.TrybEkranu = false; }
            });
        };

        FormClosed += (_, _) => { _otwarte.Remove(this); _zegar.Dispose(); _puls.Dispose(); };

        Load += (_, _) => Ui.DopasujDoEkranu(this, new Size(516, 520));

        // Ctrl+S zapisuje biezaca ramke ekranu. Mapy bitowe znakow wlasnych uczy sie
        // ze zdjecia panelu zestawionego z ramka z tej samej chwili - bez ramki
        // wiadomo tylko, jak komorka wyglada, a nie jakim kodem wzmacniacz o nia prosi.
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { ZapiszRamke(); e.Handled = true; }
        };
    }

    /// <summary>Zapisuje ostatnia ramke ekranu obok pliku programu i mowi gdzie.</summary>
    private void ZapiszRamke()
    {
        var ekran = _mostek.Ekran;
        if (ekran is null || ekran.Surowe.Length == 0)
        {
            _stopka.Text = "Nie ma czego zapisac - nie przyszla jeszcze zadna ramka ekranu.";
            _stopka.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
            return;
        }

        try
        {
            string katalog = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "ekrany");
            Directory.CreateDirectory(katalog);

            string plik = Path.Combine(katalog,
                "ekran-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin");
            File.WriteAllBytes(plik, ekran.Surowe);

            _stopka.Text = "Zapisano ramkę (" + ekran.Surowe.Length + " B): " + plik;
            _stopka.ForeColor = Theme.TekstSzary;
        }
        catch (Exception ex)
        {
            _stopka.Text = "Nie udało się zapisać ramki: " + ex.Message;
            _stopka.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
        }
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
        _ostatniKlawisz = DateTime.UtcNow;
        try
        {
            await Task.Delay(60);

            // Wzmacniacz czasem puls przemilcza i klatka nie przychodzi wcale.
            // Zmierzone: potrafi tak zamilknac na ponad trzy sekundy, do nastepnego
            // pulsu. Dlatego ponawiamy, zamiast czekac na zegar.
            var przed = _mostek.Ekran;
            await Puls();

            for (int proba = 0; proba < 2; proba++)
            {
                if (await PoczekajNaKlatke(przed, 700)) break;
                await Puls();
            }
        }
        finally
        {
            _ostatniKlawisz = DateTime.UtcNow;
            _klawiszWToku = false;
        }
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

    /// <summary>Czeka na nowa klatke, najwyzej podany czas. Zwraca, czy przyszla.</summary>
    private async Task<bool> PoczekajNaKlatke(EkranSpe przed, int milisekund)
    {
        var koniec = DateTime.UtcNow.AddMilliseconds(milisekund);

        while (DateTime.UtcNow < koniec)
        {
            if (!ReferenceEquals(_mostek.Ekran, przed)) return true;
            await Task.Delay(30);
        }

        return false;
    }

    /// <summary>
    /// Puls z zegara. Wstrzymany tylko na czas obslugi klawisza, bo trafiony zaraz
    /// po nim kasuje ten klawisz.
    /// </summary>
    private async Task PulsGdyTrzeba()
    {
        if (_klawiszWToku) return;

        var teraz = DateTime.UtcNow;
        if (teraz - _ostatniKlawisz < CiszaPoKlawiszu) return;
        if (teraz - _ostatniPuls < TaktSpoczynku) return;

        await Puls();
    }

    private void PokazEkran()
    {
        _lcd.Zastepczy = _mostek.Stan == StanMostka.Polaczony
            ? "czekam na wyświetlacz…"
            : "mostek rozłączony";

        // Ostatni ekran zostaje na widoku, dopoki nie przyjdzie nowy. Kasowanie go po
        // kilku sekundach mrugalo napisem "czekam na wyswietlacz" za kazdym razem, gdy
        // wzmacniacz przemilczal puls - a przeciez nadal pokazuje to samo co ostatnio.
        var ekran = _mostek.Ekran;
        if (ekran is not null && !ReferenceEquals(_lcd.Ekran, ekran)) _lcd.Ekran = ekran;
        else if (ekran is null && _lcd.Ekran is not null) _lcd.Ekran = null;
    }

    private void Odswiez()
    {
        PokazEkran();

        var status = _mostek.Status;
        bool swiezy = status is not null && (DateTime.UtcNow - status.Kiedy).TotalSeconds < 5;

        if (!swiezy)
        {
            // Przy otwartym podgladzie nie odpytujemy o status, zeby nie odbierac
            // wzmacniaczowi czasu na klatki - wszystko widac na samym ekranie.
            _stan.Text = _mostek.Stan != StanMostka.Polaczony ? "mostek rozłączony"
                       : status is not null ? status.Opis
                       : "stan czytany z ekranu poniżej";
            _stan.ForeColor = Theme.TekstSzary;
            return;
        }

        string klopot = status.Klopot;
        _stan.Text = klopot.Length > 0 ? status.Opis + "   —   " + klopot : status.Opis;
        _stan.ForeColor = klopot.Length > 0 ? Color.FromArgb(0xB3, 0x26, 0x1E) : Theme.Tekst;
    }
}
