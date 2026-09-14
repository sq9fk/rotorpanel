namespace RotorPanel;

/// <summary>Diody lacznosci: ser2net oraz sterownik anten.</summary>
public partial class MainForm
{
    // --------------------------------------------------------------- ser2net

    /// <summary>
    /// Stan portow ser2net. **Nie badamy ich zadnym wlasnym polaczeniem.**
    ///
    /// Kazdy port ser2neta przyjmuje jednego klienta, a przy ustawieniu `kickolduser` nowe
    /// polaczenie **wyrzuca** poprzednie. Sprawdzanie dostepnosci bylo wiec strzelaniem
    /// w stope: program z zatrzymanymi mostkami nie mial czego pomijac, wiec co 60 sekund
    /// laczyl sie po kolei do wszystkich portow - i kopal mostki **innej kopii programu**,
    /// uruchomionej na drugim komputerze. Zmierzone w sladzie u uzytkownika: trzy mostki
    /// zrywane w tej samej milisekundzie, czysto, w rytmie dokladnie 60 sekund. Sprawdzanie,
    /// ktore psuje to, co sprawdza, jest gorsze niz brak sprawdzania.
    ///
    /// Dlatego dioda mowi tylko to, co wiemy **za darmo**, z wlasnych mostkow: polaczony
    /// mostek jest dowodem, ze port odpowiada, a mostek, ktory sie nie moze polaczyc, niesie
    /// gotowy komunikat bledu. Gdy wszystkie sa zatrzymane, uczciwa odpowiedz brzmi
    /// "nie wiem" - i tak jest napisane.
    /// </summary>
    private void OdswiezSer2net()
    {
        var rotory = _cfg.Rotory.Where(r => r.Gotowy).ToList();

        if (rotory.Count == 0)
        {
            _diodaSer2net.Kolor = Theme.Szary;
            _opisSer2net.Text = "ser2net: brak rotorów";
            Podpowiedz(_diodaSer2net, _opisSer2net, "Rotory definiuje się w Ustawieniach.");
            return;
        }

        _opisSer2net.Text = "ser2net " + _cfg.PiIp;

        int polaczone = 0, wbledzie = 0, zatrzymane = 0;
        var opisy = new List<string>();

        foreach (var rotor in rotory)
        {
            var mostek = _mostki.FirstOrDefault(m => ReferenceEquals(m.Punkt, rotor));
            string adres = _cfg.AdresDla(rotor) + ":" + rotor.Port;
            string skad;

            switch (mostek?.Stan)
            {
                case StanMostka.Polaczony:
                    polaczone++;
                    skad = "odpowiada — mostek połączony";
                    break;

                case StanMostka.Laczenie when mostek.Blad.Length > 0:
                    wbledzie++;
                    skad = "nie odpowiada: " + mostek.Blad;
                    break;

                case StanMostka.Laczenie:
                    skad = "łączenie…";
                    break;

                default:
                    zatrzymane++;
                    skad = "nie sprawdzam — mostek zatrzymany";
                    break;
            }

            if (mostek is { ObcePrzejecia: > 0 })
                skad += "; port przejmowany przez inny program (" + mostek.ObcePrzejecia + "×)";

            // Czasy wymiany przy KAZDYM rotorze osobno. Sonda wpieta wprost w port szeregowy
            // Pi zmierzyla na sterowniku 243/245/247 ms, wiec wszystko ponad to dokladamy
            // my albo ser2net - a zeby to zobaczyc, trzeba miec te trzy tory obok siebie.
            if (mostek != null && mostek.OpisWymiany.Length > 0)
            {
                skad += "; wymiana " + mostek.OpisWymiany;
                if (mostek.BrakiOdpowiedzi > 0)
                    skad += ", bez odpowiedzi " + mostek.BrakiOdpowiedzi +
                            " (spóźnionych " + mostek.SpoznioneOdpowiedzi + ")";
            }

            opisy.Add(rotor.Etykieta + " " + adres + " — " + skad);
        }

        bool przejmowany = _mostki.Any(m => m.ObcePrzejecia > 0);

        _diodaSer2net.Kolor =
            przejmowany                    ? Theme.Pomarancz :
            polaczone == rotory.Count      ? Theme.Zielony :
            wbledzie == rotory.Count       ? Color.FromArgb(0xB3, 0x26, 0x1E) :
            zatrzymane == rotory.Count     ? Theme.Szary :
                                             Theme.Pomarancz;

        string podsumowanie =
            zatrzymane == rotory.Count ? "mostki zatrzymane — nie sprawdzam portów"
                                       : polaczone + " z " + rotory.Count + " portów odpowiada";

        Podpowiedz(_diodaSer2net, _opisSer2net,
            _cfg.PiIp + " — " + podsumowanie + Environment.NewLine +
            string.Join(Environment.NewLine, opisy) + Environment.NewLine +
            "Portów nie badamy własnym połączeniem: ser2net oddaje port nowemu klientowi " +
            "i zerwałoby to mostek — także w innej kopii programu.");
    }

    // ------------------------------------------------------- sterownik anten

    /// <summary>
    /// Stan lacznosci ze sterownikiem anten. Sprawdzamy rzadko i tylko nawiazaniem
    /// polaczenia TCP - uklad na Arduino ma kilka gniazd i nie warto go meczyc.
    /// </summary>
    private void OdswiezSterownika()
    {
        string adres = (_cfg.SterownikAnten ?? "").Trim();

        if (adres.Length == 0)
        {
            _diodaSterownika.Kolor = Theme.Szary;
            _opisSterownika.Text = "sterownik anten: nie ustawiono";
            Podpowiedz(_diodaSterownika, _opisSterownika,
                "Adres sterownika anten podasz w Ustawieniach.");
            return;
        }

        _opisSterownika.Text = "sterownik anten " + adres;

        string stan = _sterownikOsiagalny switch
        {
            true  => "odpowiada",
            false => "nie odpowiada",
            _     => "sprawdzanie…"
        };
        Podpowiedz(_diodaSterownika, _opisSterownika, adres + " — " + stan);

        _diodaSterownika.Kolor = _sterownikOsiagalny switch
        {
            true  => Theme.Zielony,
            false => Color.FromArgb(0xB3, 0x26, 0x1E),
            _     => Theme.Pomarancz
        };

        if (_badanieSterownika || DateTime.UtcNow < _nastepneSterownik) return;

        _badanieSterownika = true;
        SprawdzSterownika(adres);
    }

    private async void SprawdzSterownika(string adres)
    {
        StanSterownika stan = null;
        try { stan = await SterownikAnten.PobierzStan(adres); }
        catch { /* brak lacznosci albo nieoczekiwana strona */ }

        if (IsDisposed) return;

        _stanSterownika = stan;
        _sterownikOsiagalny = stan != null;
        _badanieSterownika = false;

        // Pobieramy cala strone, bo daje tez przypisanie anten do nadajnikow.
        // Sam sterownik odswieza swoja strone co 10 sekund, wiec 15 go nie meczy.
        _nastepneSterownik = DateTime.UtcNow.AddSeconds(stan != null ? 15 : 20);

        OdswiezZnaczniki();
    }

    /// <summary>Oznaczenia nadajnikow przy nazwach anten.</summary>
    private void OdswiezZnaczniki()
    {
        foreach (var antena in _cfg.Anteny)
        {
            if (!_ui.TryGetValue("a" + antena.Nr, out var wiersz)) continue;

            var znaczniki = wiersz.Trx;
            if (znaczniki == null) continue;

            var nadajniki = _stanSterownika?.TrxNaAntenie(antena.Nr) ?? new List<int>();

            for (int i = 0; i < znaczniki.Length; i++)
            {
                if (i < nadajniki.Count)
                {
                    int trx = nadajniki[i];
                    string podpis = "TRX" + trx;
                    if (znaczniki[i].Text != podpis) znaczniki[i].Text = podpis;
                    znaczniki[i].Tlo = Theme.KolorNadajnika(trx);
                    znaczniki[i].Visible = true;

                    string opis = "Nadajnik " + trx;
                    if (_stanSterownika != null &&
                        _stanSterownika.OpisyTrx.TryGetValue(trx, out string wlasny) &&
                        !string.IsNullOrWhiteSpace(wlasny))
                        opis = wlasny;

                    _dymek.SetToolTip(znaczniki[i], opis);
                }
                else
                {
                    znaczniki[i].Visible = false;
                }
            }
        }
    }

    private void Podpowiedz(Control a, Control b, string tekst)
    {
        _dymek.SetToolTip(a, tekst);
        _dymek.SetToolTip(b, tekst);
    }
}
