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

    private Karta BudujKarte(Antena a, Rotor rotor, Mostek m, int y)
    {
        var karta = new Karta
        {
            Location = new Point(0, y),
            Size = new Size(516, WysokoscKarty),
            Promien = 8
        };

        var dioda = new Led { Location = new Point(16, 22) };
        karta.Controls.Add(dioda);

        string podpis = a.Nr + ".  " + a.Etykieta;
        int szerokoscPodpisu = Math.Min(
            TextRenderer.MeasureText(podpis, Theme.Nazwa()).Width + 4, 170);

        karta.Controls.Add(Ui.Etykieta(podpis, Theme.Nazwa(), Theme.Tekst,
            new Point(44, 8), new Size(szerokoscPodpisu, 20)));

        // Oznaczenia nadajnikow stoja tuz za nazwa anteny, wiec nie odjezdzaja
        // od niej przy krotkich nazwach.
        var znaczniki = new Znacznik[2];
        for (int i = 0; i < znaczniki.Length; i++)
        {
            znaczniki[i] = new Znacznik
            {
                Location = new Point(44 + szerokoscPodpisu + 6 + i * 46, 9)
            };
            karta.Controls.Add(znaczniki[i]);
        }

        string strzalka = ((char)0x2192).ToString();
        string trasa = rotor == null
            ? "bez rotora"
            : rotor.Etykieta + ":  " + rotor.Com + " " + strzalka + " " + m.Adres + ":" + rotor.Port;

        karta.Controls.Add(Ui.Etykieta(trasa, Theme.Maly(), Theme.TekstSzary,
            new Point(44, 30), new Size(200, 16)));

        var stan = Ui.Etykieta(rotor != null ? "zatrzymany" : "", Theme.Zwykly(), Theme.TekstSzary,
            new Point(238, 8), new Size(162, 20));
        stan.TextAlign = ContentAlignment.MiddleRight;
        karta.Controls.Add(stan);

        var ruch = Ui.Etykieta("", Theme.Maly(), Theme.TekstSzary,
            new Point(228, 30), new Size(172, 16));
        ruch.TextAlign = ContentAlignment.MiddleRight;
        karta.Controls.Add(ruch);

        Button przelacz = null;
        if (rotor != null)
        {
            przelacz = Ui.Przycisk("Połącz", 92, glowny: true);
            przelacz.Location = new Point(408, 17);
            przelacz.Tag = m;
            przelacz.Click += (s, _) =>
            {
                var mostek = (Mostek)((Button)s).Tag;
                if (mostek.Stan == StanMostka.Zatrzymany) mostek.Start();
                else mostek.Stop();
                Odswiez();
            };
            karta.Controls.Add(przelacz);
        }

        _ui[a.Nr] = new Wiersz
        {
            Mostek = m, Dioda = dioda, Stan = stan, Ruch = ruch,
            Przelacz = przelacz, Trx = znaczniki
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

            if (!string.IsNullOrEmpty(m.Blad)) ostatniBlad = m.Rotor.Etykieta + ": " + m.Blad;
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
        foreach (var m in _mostki) m.Dispose();
        base.OnFormClosing(e);
    }
}
