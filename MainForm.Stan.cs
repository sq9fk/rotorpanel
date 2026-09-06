namespace RotorPanel;

/// <summary>Karty anten oraz odswiezanie stanu: mostki, ser2net i sterownik anten.</summary>
public partial class MainForm
{
    private DateTime _nastepneSer2net = DateTime.MinValue;
    private DateTime _nastepneSterownik = DateTime.MinValue;
    private bool _badanieSer2net, _badanieSterownika;
    private int _osiagalnePorty = -1, _wszystkiePorty;
    private bool? _sterownikOsiagalny;
    private StanSterownika _stanSterownika;

    private void ZerujStanySieci()
    {
        _nastepneSer2net = DateTime.MinValue;
        _nastepneSterownik = DateTime.MinValue;
        _osiagalnePorty = -1;
        _sterownikOsiagalny = null;
    }

    /// <summary>Wspolny szkielet karty - reszte dokladaja metody nizej.</summary>
    private Karta PustaKarta(int y)
        => new Karta { Location = new Point(0, y), Size = new Size(516, WysokoscKarty), Promien = 8 };

    private Button PrzyciskPrzelaczania(Karta karta, Mostek m)
    {
        var przelacz = Ui.Przycisk("Połącz", 92, glowny: true);
        przelacz.Location = new Point(408, 21);
        przelacz.Tag = m;
        przelacz.Click += (s, _) =>
        {
            var mostek = (Mostek)((Button)s).Tag;
            if (mostek.Stan == StanMostka.Zatrzymany) mostek.Start();
            else mostek.Stop();
            Odswiez();
        };
        karta.Controls.Add(przelacz);
        return przelacz;
    }

    private Label EtykietaStanuKarty(Karta karta, bool aktywna)
    {
        var stan = Ui.Etykieta(aktywna ? "zatrzymany" : "", Theme.Zwykly(), Theme.TekstSzary,
            new Point(238, 6), new Size(162, 20));
        stan.TextAlign = ContentAlignment.MiddleRight;
        karta.Controls.Add(stan);
        return stan;
    }

    private Label EtykietaRuchu(Karta karta)
    {
        var ruch = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(240, 28), new Size(160, 16));
        ruch.TextAlign = ContentAlignment.MiddleRight;
        karta.Controls.Add(ruch);
        return ruch;
    }

    private string OpisTrasy(Polaczenie p, Mostek m)
        => p.Com + " " + ((char)0x2192) + " " + m.Adres + ":" + p.Port;

    private Karta BudujKarteAnteny(Antena a, Rotor rotor, Mostek m, int y)
    {
        var karta = PustaKarta(y);

        var dioda = new Led { Location = new Point(16, 26) };
        karta.Controls.Add(dioda);

        string podpis = a.Nr + ".  " + a.Etykieta;
        int szerokoscPodpisu = Math.Min(
            TextRenderer.MeasureText(podpis, Theme.Nazwa()).Width + 4, 170);

        karta.Controls.Add(Ui.Etykieta(podpis, Theme.Nazwa(), Theme.Tekst,
            new Point(44, 6), new Size(szerokoscPodpisu, 20)));

        // Oznaczenia nadajnikow stoja tuz za nazwa anteny, wiec nie odjezdzaja
        // od niej przy krotkich nazwach.
        var znaczniki = new Znacznik[2];
        for (int i = 0; i < znaczniki.Length; i++)
        {
            znaczniki[i] = new Znacznik
            {
                Location = new Point(44 + szerokoscPodpisu + 6 + i * 46, 7)
            };
            karta.Controls.Add(znaczniki[i]);
        }

        karta.Controls.Add(Ui.Etykieta(rotor == null ? "bez rotora" : rotor.Etykieta, Theme.Maly(),
            rotor == null ? Theme.TekstSzary : Theme.Tekst,
            new Point(44, 28), new Size(190, 16)));

        string trasa = rotor == null ? "" : OpisTrasy(rotor, m);
        var etykietaTrasy = Ui.Etykieta(trasa, Theme.Maly(), Theme.TekstSzary,
            new Point(44, 46), new Size(220, 16));
        karta.Controls.Add(etykietaTrasy);
        if (rotor != null) _dymek.SetToolTip(etykietaTrasy, rotor.Etykieta + Environment.NewLine + trasa);

        var stan = EtykietaStanuKarty(karta, rotor != null);
        var ruch = EtykietaRuchu(karta);
        Button przelacz = rotor == null ? null : PrzyciskPrzelaczania(karta, m);

        _ui["a" + a.Nr] = new Wiersz
        {
            Mostek = m, Dioda = dioda, Stan = stan, Ruch = ruch,
            Przelacz = przelacz, Trx = znaczniki
        };
        return karta;
    }

    private Karta BudujKarteUrzadzenia(Urzadzenie u, Mostek m, int y)
    {
        var karta = PustaKarta(y);

        var dioda = new Led { Location = new Point(16, 26) };
        karta.Controls.Add(dioda);

        karta.Controls.Add(Ui.Etykieta(u.Etykieta, Theme.Nazwa(), Theme.Tekst,
            new Point(44, 6), new Size(230, 20)));

        karta.Controls.Add(Ui.Etykieta(u.NazwaProtokolu, Theme.Maly(), Theme.TekstSzary,
            new Point(44, 28), new Size(190, 16)));

        string trasa = OpisTrasy(u, m);
        var etykietaTrasy = Ui.Etykieta(trasa, Theme.Maly(), Theme.TekstSzary,
            new Point(44, 46), new Size(220, 16));
        karta.Controls.Add(etykietaTrasy);
        _dymek.SetToolTip(etykietaTrasy, u.Etykieta + Environment.NewLine + trasa +
                          Environment.NewLine + "protokół: " + u.NazwaProtokolu);

        var stan = EtykietaStanuKarty(karta, true);
        var ruch = EtykietaRuchu(karta);
        var przelacz = PrzyciskPrzelaczania(karta, m);

        _ui["u" + u.Nr] = new Wiersz
        {
            Mostek = m, Dioda = dioda, Stan = stan, Ruch = ruch, Przelacz = przelacz
        };
        return karta;
    }

    private static string Bajty(long n)
    {
        if (n < 1024) return n + " B";
        if (n < 1024 * 1024) return (n / 1024.0).ToString("0.0") + " kB";
        return (n / (1024.0 * 1024.0)).ToString("0.0") + " MB";
    }

    private void Odswiez()
    {
        string ostatniBlad = "";
        string kropka = " " + ((char)0x00B7).ToString() + " ";

        foreach (var wpis in _ui)
        {
            var u = wpis.Value;
            var m = u.Mostek;
            if (m is null || u.Przelacz is null) continue;

            switch (m.Stan)
            {
                case StanMostka.Polaczony:
                    u.Dioda.Kolor = Theme.Zielony;
                    u.Stan.Text = "połączony";
                    u.Stan.ForeColor = Theme.Tekst;
                    UstawPrzycisk(u.Przelacz, "Rozłącz", glowny: false);
                    break;

                case StanMostka.Laczenie:
                    u.Dioda.Kolor = Theme.Pomarancz;
                    u.Stan.Text = "łączenie…";
                    u.Stan.ForeColor = Theme.Tekst;
                    UstawPrzycisk(u.Przelacz, "Rozłącz", glowny: false);
                    break;

                default:
                    u.Dioda.Kolor = Theme.Szary;
                    u.Stan.Text = "zatrzymany";
                    u.Stan.ForeColor = Theme.TekstSzary;
                    UstawPrzycisk(u.Przelacz, "Połącz", glowny: true);
                    break;
            }

            if (m.Stan == StanMostka.Zatrzymany)
            {
                u.Ruch.Text = "";
                u.PoprzedniLacznie = 0;
                u.Szybkosc = 0;
            }
            else
            {
                long lacznie = m.Rx + m.Tx;
                var teraz = DateTime.UtcNow;
                double sekundy = (teraz - u.PoprzedniCzas).TotalSeconds;
                if (sekundy >= 0.5)
                {
                    u.Szybkosc = (lacznie - u.PoprzedniLacznie) / sekundy;
                    u.PoprzedniLacznie = lacznie;
                    u.PoprzedniCzas = teraz;
                }

                u.Ruch.Text = "RX " + Bajty(m.Rx) + kropka + "TX " + Bajty(m.Tx) +
                              kropka + u.Szybkosc.ToString("0") + " B/s";
            }

            if (!string.IsNullOrEmpty(m.Blad)) ostatniBlad = m.Punkt.Etykieta + ": " + m.Blad;
        }

        _stopka.Text = ostatniBlad;
        _stopka.ForeColor = string.IsNullOrEmpty(ostatniBlad)
            ? Theme.TekstSzary
            : Color.FromArgb(0xB3, 0x26, 0x1E);

        AktualizujTray();
        OdswiezSer2net();
        OdswiezSterownika();
        OdswiezZnaczniki();
    }

    private static void UstawPrzycisk(Button b, string tekst, bool glowny)
    {
        if (b is null || b.Text == tekst) return;
        b.Text = tekst;
        b.BackColor = glowny ? Theme.Akcent : Color.White;
        b.ForeColor = glowny ? Color.White : Theme.Tekst;
        b.FlatAppearance.BorderColor = glowny ? Theme.Akcent : Theme.Ramka;
        b.FlatAppearance.MouseOverBackColor = glowny ? Theme.AkcentCien : Color.FromArgb(0xF0, 0xF2, 0xF5);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Krzyzyk chowa program do zasobnika; wyjscie tylko przez menu ikony.
        if (e.CloseReason == CloseReason.UserClosing && !_naprawdeZamykam)
        {
            e.Cancel = true;
            UkryjDoZasobnika();
            return;
        }

        _timer.Stop();
        if (_tray is not null) _tray.Visible = false;
        RozlaczWszystko();
        base.OnFormClosing(e);
    }
}
