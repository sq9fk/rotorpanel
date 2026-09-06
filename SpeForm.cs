namespace RotorPanel;

/// <summary>
/// Klawiatura wzmacniacza SPE Expert. Kody klawiszy pochodza wprost z firmowego
/// Application Programmer's Guide (rev. 1.1) - kazdy jest odpowiednikiem nacisniecia
/// przycisku na przednim panelu.
///
/// Wyswietlacza wzmacniacza tedy nie widac: jego odbicie to zamknieta czesc protokolu
/// programu KTerm. Zamiast niego u gory stoi stan czytany komenda STATUS.
/// </summary>
public sealed class SpeForm : Form
{
    private readonly Mostek _mostek;
    private readonly Label _stan, _stopka;
    private readonly System.Windows.Forms.Timer _zegar;

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
            new Klawisz("S", 0x11, "klawisz S"),
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
        ClientSize      = new Size(516, 348);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox     = false;
        MaximizeBox     = false;
        BackColor       = Theme.Tlo;
        Font            = Theme.Zwykly();

        var karta = new Karta { Location = new Point(18, 16), Size = new Size(480, 56) };
        Controls.Add(karta);

        _stan = Ui.Etykieta("", Theme.Zwykly(), Theme.Tekst,
            new Point(16, 8), new Size(448, 20));
        karta.Controls.Add(_stan);

        karta.Controls.Add(Ui.Etykieta(
            "Wyświetlacza wzmacniacza nie widać — to zamknięta część protokołu.",
            Theme.Maly(), Theme.TekstSzary, new Point(16, 30), new Size(448, 16)));

        int y = 88;
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

        _zegar = new System.Windows.Forms.Timer { Interval = 500 };
        _zegar.Tick += (_, _) => Odswiez();
        _zegar.Start();
        Odswiez();

        FormClosed += (_, _) => _zegar.Dispose();
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
    }

    private void Odswiez()
    {
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
