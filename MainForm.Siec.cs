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
        var wiersze = new List<string[]>();
        var uwagi = new List<string>();

        foreach (var rotor in rotory)
        {
            var mostek = _mostki.FirstOrDefault(m => ReferenceEquals(m.Punkt, rotor));
            string stan;

            switch (mostek?.Stan)
            {
                case StanMostka.Polaczony:
                    polaczone++;
                    stan = "połączony";
                    break;

                case StanMostka.Laczenie when mostek.Blad.Length > 0:
                    wbledzie++;
                    stan = "bez łączności";
                    uwagi.Add(rotor.Etykieta + ": " + mostek.Blad);
                    break;

                case StanMostka.Laczenie:
                    stan = "łączenie…";
                    break;

                default:
                    zatrzymane++;
                    stan = "zatrzymany";
                    break;
            }

            if (mostek is { ObcePrzejecia: > 0 })
                uwagi.Add(rotor.Etykieta + ": port przejmowany przez inny program (" +
                          mostek.ObcePrzejecia + "×)");

            // Odrzucone odczyty pozycji pokazujemy **zawsze**, takze przy wylaczonej
            // diagnostyce ramek - inaczej przy produkcyjnych ustawieniach znikalyby bez sladu.
            if (mostek is { OdrzuconeOdczyty: > 0 })
                uwagi.Add(rotor.Etykieta + ": odrzucone odczyty pozycji — " +
                          mostek.OdrzuconeOdczyty + "×");

            // Ustapienie filtra to moment, w ktorym program **zmienil zdanie** o polozeniu
            // anteny. Wazniejsze od samego odrzucenia i dlatego osobno.
            // Sam naglowek zamiast pozycji to sterownik poza trybem A. Nazwane wprost,
            // bo rozpoznanie tego z surowych bajtow zajelo dwa dni.
            if (mostek is { SamychNaglowkow: >= 5 })
                uwagi.Add(rotor.Etykieta + ": sterownik odpowiada samym nagłówkiem (" +
                          mostek.SamychNaglowkow + "× z rzędu) — sprawdź tryb A (Auto)");

            // Lista nastaw jest najwazniejsza uwaga przy ucieczce rotora: albo pokazuje,
            // kto kazal antenie tam pojechac, albo jest pusta - i wtedy nie kazalismy my.
            if (mostek != null && mostek.OstatnieNastawy.Length > 0)
                uwagi.Add(rotor.Etykieta + ": nastawy — " + mostek.OstatnieNastawy);

            if (mostek is { UstapieniaFiltra: > 0 })
                uwagi.Add(rotor.Etykieta + ": filtr pozycji ustąpił — " +
                          mostek.UstapieniaFiltra + "× przyjął odczyt, który odrzucał");

            int zapytan = mostek?.Zapytan ?? 0;
            int odpowiedzi = mostek?.Odpowiedzi ?? 0;
            int braki = mostek?.BrakiOdpowiedzi ?? 0;

            // **Odpowiedzi sa w tabeli po to, zeby dalo sie ja sprawdzic bez pytania nikogo.**
            // Zapytania, odpowiedzi i braki to trzy liczby z trzech roznych miejsc kodu:
            // pierwsza rosnie przy wysylce ramki, druga przy rozebraniu odpowiedzi, trzecia
            // przy porzuceniu zapytania starszego niz 700 ms. Jesli sie nie skladaja - poza
            // jednym zapytaniem, ktore akurat jest w locie - to nie lacze jest chore, tylko
            // licznik. Pytanie "czy zero brakow przy 11 000 zapytan jest poprawne" ma sie
            // rozstrzygac w tabeli, a nie w rozmowie.
            wiersze.Add(new[]
            {
                rotor.Port.ToString(),
                rotor.Etykieta,
                stan,
                zapytan > 0 ? zapytan.ToString() : "—",
                zapytan > 0 ? odpowiedzi.ToString() : "—",
                zapytan > 0 ? braki.ToString() : "—",
                zapytan > 0 ? (braki * 100.0 / zapytan).ToString("0.0") + "%" : "—",
                mostek != null && mostek.OpisPozycji.Length > 0 ? mostek.OpisPozycji : "—",
                mostek != null && mostek.OpisWymiany.Length > 0 ? mostek.OpisWymiany : "—"
            });
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

        var tekst = new System.Text.StringBuilder();
        tekst.AppendLine(_cfg.PiIp + " — " + podsumowanie);
        tekst.AppendLine();
        tekst.Append(Tabela(
            new[] { "Port", "Rotor", "Stan", "Zapytań", "Odpowiedzi", "Braki", "%",
                    "Pozycja", "Czasy min/śr/max" },
            new[] { false, false, false, true, true, true, true, true, false },
            wiersze));

        if (uwagi.Count > 0)
        {
            tekst.AppendLine();
            foreach (var u in uwagi) tekst.AppendLine(u);
        }

        tekst.AppendLine();
        tekst.Append("Portów nie badamy własnym połączeniem: ser2net oddaje port nowemu" +
                     Environment.NewLine +
                     "klientowi i zerwałoby to mostek — także w innej kopii programu.");

        PodpowiedzTabela(_diodaSer2net, _opisSer2net, tekst.ToString());
    }

    /// <summary>
    /// Sklada tabele o stalej szerokosci kolumn. Liczby wyrownujemy do prawej, tekst do lewej -
    /// inaczej procenty i liczniki nie daja sie porownac wzrokiem, a o to w tabeli chodzi.
    /// </summary>
    internal static string Tabela(string[] naglowki, bool[] doPrawej, List<string[]> wiersze)
    {
        int kolumn = naglowki.Length;
        var szerokosci = new int[kolumn];

        for (int k = 0; k < kolumn; k++)
        {
            szerokosci[k] = naglowki[k].Length;
            foreach (var w in wiersze) szerokosci[k] = Math.Max(szerokosci[k], w[k].Length);
        }

        string Linia(string[] pola)
        {
            var s = new System.Text.StringBuilder();
            for (int k = 0; k < kolumn; k++)
            {
                if (k > 0) s.Append("  ");
                s.Append(doPrawej[k] ? pola[k].PadLeft(szerokosci[k])
                                     : pola[k].PadRight(szerokosci[k]));
            }
            return s.ToString().TrimEnd();
        }

        var tekst = new System.Text.StringBuilder();
        tekst.AppendLine(Linia(naglowki));
        tekst.AppendLine(Linia(szerokosci.Select(s => new string('─', s)).ToArray()));
        foreach (var w in wiersze) tekst.AppendLine(Linia(w));
        return tekst.ToString().TrimEnd();
    }

    /// <summary>
    /// Podpowiedz rysowana czcionka o stalej szerokosci - bez niej tabela sie rozjezdza,
    /// bo domyslna czcionka dymka jest proporcjonalna i kolumny nie stoja w pionie.
    /// </summary>
    private void PodpowiedzTabela(Control a, Control b, string tekst)
    {
        var dymek = DymekTabeli();
        dymek.SetToolTip(a, tekst);
        dymek.SetToolTip(b, tekst);
    }

    private ToolTip _dymekTabeli;
    private readonly Font _fontTabeli = Theme.Mono();

    private ToolTip DymekTabeli()
    {
        if (_dymekTabeli != null) return _dymekTabeli;

        // Dluzszy czas wyswietlania: tabela ma kilkanascie linii i domyslne piec sekund
        // nie wystarcza, zeby ja przeczytac.
        _dymekTabeli = new ToolTip { OwnerDraw = true, AutoPopDelay = 30000 };

        _dymekTabeli.Popup += (_, e) =>
        {
            var rozmiar = TextRenderer.MeasureText(_dymekTabeli.GetToolTip(e.AssociatedControl),
                                                   _fontTabeli);
            e.ToolTipSize = new Size(rozmiar.Width + 16, rozmiar.Height + 12);
        };

        _dymekTabeli.Draw += (_, e) =>
        {
            e.DrawBackground();
            e.DrawBorder();
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, _fontTabeli,
                                  new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 6,
                                                e.Bounds.Width - 16, e.Bounds.Height - 12),
                                  SystemColors.InfoText,
                                  TextFormatFlags.Left | TextFormatFlags.Top |
                                  TextFormatFlags.NoPadding);
        };

        return _dymekTabeli;
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
