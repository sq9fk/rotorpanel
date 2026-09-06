namespace RotorPanel;

/// <summary>Diody lacznosci: ser2net oraz sterownik anten.</summary>
public partial class MainForm
{
    // --------------------------------------------------------------- ser2net

    /// <summary>
    /// Stan portow ser2net. Portow z dzialajacym mostkiem NIE badamy - kazdy port
    /// przyjmuje jedno polaczenie, a przy ustawieniu kickolduser proba nawiazania
    /// drugiego rozlaczylaby wlasny mostek. Dzialajacy mostek i tak jest dowodem,
    /// ze port odpowiada.
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

        string stan = _osiagalnePorty < 0
            ? "sprawdzanie…"
            : _osiagalnePorty + " z " + _wszystkiePorty + " portów odpowiada";
        Podpowiedz(_diodaSer2net, _opisSer2net, _cfg.PiIp + " — " + stan);

        _diodaSer2net.Kolor =
            _osiagalnePorty < 0            ? Theme.Pomarancz :
            _osiagalnePorty == 0           ? Color.FromArgb(0xB3, 0x26, 0x1E) :
            _osiagalnePorty < _wszystkiePorty ? Theme.Pomarancz :
                                             Theme.Zielony;

        if (_badanieSer2net || DateTime.UtcNow < _nastepneSer2net) return;

        _badanieSer2net = true;
        SprawdzSer2net(rotory);
    }

    private async void SprawdzSer2net(List<Rotor> rotory)
    {
        int osiagalne = 0;
        var opisy = new List<string>();

        foreach (var rotor in rotory)
        {
            string adres = _cfg.AdresDla(rotor);
            var mostek = _mostki.FirstOrDefault(m => m.Rotor.Nr == rotor.Nr);

            bool ok;
            string skad;

            if (mostek != null && mostek.Stan == StanMostka.Polaczony)
            {
                ok = true;
                skad = "mostek połączony";
            }
            else
            {
                ok = await SterownikAnten.Dostepny(adres + ":" + rotor.Port, 2500);
                skad = ok ? "odpowiada" : "brak odpowiedzi";
            }

            if (ok) osiagalne++;
            opisy.Add(rotor.Etykieta + " " + adres + ":" + rotor.Port + " — " + skad);
        }

        if (IsDisposed) return;

        _osiagalnePorty = osiagalne;
        _wszystkiePorty = rotory.Count;
        _badanieSer2net = false;
        _nastepneSer2net = DateTime.UtcNow.AddSeconds(osiagalne == rotory.Count ? 60 : 20);

        string dymek = string.Join(Environment.NewLine, opisy);
        Podpowiedz(_diodaSer2net, _opisSer2net, dymek);
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
        bool osiagalny = await SterownikAnten.Dostepny(adres);

        if (IsDisposed) return;

        _sterownikOsiagalny = osiagalny;
        _badanieSterownika = false;
        // Po udanym sprawdzeniu wystarczy zagladac rzadziej niz po nieudanym.
        _nastepneSterownik = DateTime.UtcNow.AddSeconds(osiagalny ? 60 : 20);
    }

    private void Podpowiedz(Control a, Control b, string tekst)
    {
        _dymek.SetToolTip(a, tekst);
        _dymek.SetToolTip(b, tekst);
    }
}
