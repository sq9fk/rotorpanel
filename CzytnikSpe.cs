namespace RotorPanel;

/// <summary>
/// Wylawia ze strumienia od wzmacniacza ramki, o ktore sam pytal mostek: status
/// (0x43) i - gdy sami wlaczylismy tryb RCU - zawartosc wyswietlacza (0x6A).
/// Reszta bajtow idzie do klienta bez zmian, bo to odpowiedzi na jego polecenia.
///
/// Ramki statusu zdejmujemy **tylko w liczbie wlasnych zapytan**. Pierwsza wersja
/// zjadala kazda, bo zalozylismy, ze klient o status nie pyta - a SPE Term i AetherSDR
/// pytaja same i wtedy nie dostawaly nic: zaden z nich nic nie rysowal. Teraz kazda
/// ramke i tak rozbieramy dla siebie, ale oddajemy ja dalej, jesli nie czekamy na
/// odpowiedz na wlasne zapytanie.
///
/// Bufor przegladamy po kolei, od najwczesniejszego naglowka. Szukanie w nim
/// najpierw jednego rodzaju ramek rozjezdzalo strumien: ramki lezace wczesniej
/// trafialy do klienta zamiast do nas i odczyt stanu zamieral.
/// </summary>
public sealed class CzytnikSpe
{
    private const byte Sync = 0xAA;

    private readonly List<byte> _reszta = new();

    // Ile wlasnych zapytan 0x90 czeka na odpowiedz. Ograniczone, bo przy wylaczonym
    // wzmacniaczu licznik roslby bez konca i potem zjadalibysmy ramki klienta.
    private const int MaksWlasnych = 2;
    private int _wlasneOczekujace;

    public StatusSpe Status { get; private set; }

    /// <summary>Ile wlasnych zapytan czeka na odpowiedz - do sladu diagnostycznego.</summary>
    public int WlasneOczekujace => Volatile.Read(ref _wlasneOczekujace);

    /// <summary>Kiedy przyszla ramka statusu, o ktora nie pytalismy my.</summary>
    public DateTime OstatniObcyStatus { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// Czy po drugiej stronie pary siedzi program, ktory sam odpytuje o status.
    /// Wtedy nie ma po co dokladac wlasnych zapytan - stan przeczytamy z jego ramek,
    /// a wzmacniacz nie lubi nadmiaru ruchu.
    /// </summary>
    public bool KlientPytaSam =>
        DateTime.UtcNow - OstatniObcyStatus < TimeSpan.FromSeconds(3);

    /// <summary>Mostek melduje, ze wyslal wlasne zapytanie o status.</summary>
    public void ZglosWlasneZapytanie()
    {
        if (Volatile.Read(ref _wlasneOczekujace) < MaksWlasnych)
            Interlocked.Increment(ref _wlasneOczekujace);
    }

    public EkranSpe Ekran { get; private set; }

    /// <summary>
    /// Czy zdejmowac ze strumienia ramki wyswietlacza. Wlaczamy to tylko wtedy,
    /// gdy sami wlaczylismy tryb RCU - inaczej zabralibysmy je programowi, ktory
    /// o nie poprosil.
    /// </summary>
    public bool PrzechwytujEkran { get; set; }

    /// <summary>
    /// Przyjmuje surowy kawalek strumienia, zwraca to, co ma isc do klienta.
    /// Niedokonczona ramka zostaje w srodku do nastepnego wywolania.
    /// </summary>
    public byte[] Przepusc(byte[] bufor, int ile)
    {
        for (int i = 0; i < ile; i++) _reszta.Add(bufor[i]);

        var wyjscie = new List<byte>(_reszta.Count);

        while (_reszta.Count > 0)
        {
            int poczatek = Naglowek();

            if (poczatek < 0)
            {
                // Trzymamy tylko koncowe bajty synchronizacji - one moga byc poczatkiem
                // naglowka przecietego miedzy odczytami. Wczesniej zostawaly zawsze dwa
                // ostatnie bajty, przez co szesciobajtowe potwierdzenie szlo do klienta
                // w kawalkach 4 i 2, oddalonych od siebie o kilkadziesiat milisekund.
                int zostaw = 0;
                while (zostaw < 2 && zostaw < _reszta.Count &&
                       _reszta[_reszta.Count - 1 - zostaw] == Sync)
                    zostaw++;

                Oddaj(wyjscie, _reszta.Count - zostaw);
                return wyjscie.ToArray();
            }

            Oddaj(wyjscie, poczatek);

            if (_reszta.Count < 4) return wyjscie.ToArray();

            byte rodzaj = _reszta[3];
            bool czekam;

            if (rodzaj == StatusSpe.DlugoscDanych)
            {
                if (!ZdejmijStatus(wyjscie, out czekam)) Oddaj(wyjscie, 1);
                else if (czekam) return wyjscie.ToArray();
            }
            else if (rodzaj == EkranSpe.Typ && PrzechwytujEkran)
            {
                if (!ZdejmijEkran(out czekam)) Oddaj(wyjscie, 1);
                else if (czekam) return wyjscie.ToArray();
            }
            else
            {
                // Potwierdzenia i wszystko inne naleza do klienta - oddajemy
                // pierwszy bajt i szukamy dalej, zeby nie zjesc kolejnej ramki.
                Oddaj(wyjscie, 1);
            }
        }

        return wyjscie.ToArray();
    }

    /// <summary>
    /// Zdejmuje ramke statusu. Zwraca false, gdy to nie byla ramka statusu;
    /// przez <paramref name="czekam"/> mowi, ze jest jeszcze niekompletna.
    /// </summary>
    private bool ZdejmijStatus(List<byte> wyjscie, out bool czekam)
    {
        czekam = false;

        // Wzmacniacz potrafi urwac ramke w polowie i od razu zaczac nastepna.
        int nastepny = NastepnySync(StatusSpe.DlugoscRamki);
        if (nastepny > 0)
        {
            _reszta.RemoveRange(0, nastepny);
            return true;
        }

        if (_reszta.Count < StatusSpe.DlugoscRamki)
        {
            czekam = true;
            return true;
        }

        var dane = new char[StatusSpe.DlugoscDanych];
        for (int i = 0; i < dane.Length; i++)
            dane[i] = (char)_reszta[StatusSpe.DlugoscNaglowka + i];

        var status = StatusSpe.Rozbierz(new string(dane));
        if (status is null) return false;

        Status = status;

        // Odpowiedz na wlasne zapytanie zdejmujemy, cudza idzie do klienta - rozbior
        // dla siebie zrobilismy juz wyzej, wiec nic na tym nie tracimy.
        if (Volatile.Read(ref _wlasneOczekujace) > 0)
        {
            if (Interlocked.Decrement(ref _wlasneOczekujace) < 0)
                Interlocked.Exchange(ref _wlasneOczekujace, 0);
            _reszta.RemoveRange(0, StatusSpe.DlugoscRamki);
        }
        else
        {
            OstatniObcyStatus = DateTime.UtcNow;
            Oddaj(wyjscie, StatusSpe.DlugoscRamki);
        }

        return true;
    }

    /// <summary>
    /// Ramka ekranu nie ma pola dlugosci. Konczy sie tam, gdzie zaczyna sie
    /// nastepna synchronizacja - ale czekanie na nia opoznialo podglad o cale
    /// odpytanie, bo kolejna ramka przychodzi dopiero przy nastepnym pulsie.
    /// Dlatego gdy mamy juz typowa dlugosc, bierzemy ja od razu.
    /// </summary>
    private bool ZdejmijEkran(out bool czekam)
    {
        czekam = false;

        int koniec = NastepnySync(EkranSpe.DlugoscMaksymalna);

        if (koniec < 0)
        {
            int typowa = EkranSpe.DlugoscNaglowka + EkranSpe.DlugoscTypowa;

            if (_reszta.Count >= typowa)
            {
                koniec = typowa;
            }
            else if (_reszta.Count < EkranSpe.DlugoscMaksymalna)
            {
                czekam = true;
                return true;
            }
            else
            {
                koniec = EkranSpe.DlugoscMaksymalna;
            }
        }

        int dlugosc = koniec - EkranSpe.DlugoscNaglowka;
        var ekran = EkranSpe.Rozbierz(_reszta, EkranSpe.DlugoscNaglowka, dlugosc);
        if (ekran is null) return false;

        Ekran = ekran;
        _reszta.RemoveRange(0, koniec);
        return true;
    }

    /// <summary>Pozycja najblizszej trojki bajtow synchronizacji albo -1.</summary>
    private int Naglowek()
    {
        for (int i = 0; i + 3 <= _reszta.Count; i++)
            if (_reszta[i] == Sync && _reszta[i + 1] == Sync && _reszta[i + 2] == Sync)
                return i;
        return -1;
    }

    /// <summary>Pozycja kolejnej synchronizacji przed koncem biezacej ramki albo -1.</summary>
    private int NastepnySync(int dlugoscRamki)
    {
        for (int i = 3; i + 3 <= _reszta.Count && i < dlugoscRamki; i++)
            if (_reszta[i] == Sync && _reszta[i + 1] == Sync && _reszta[i + 2] == Sync)
                return i;
        return -1;
    }

    private void Oddaj(List<byte> wyjscie, int ile)
    {
        if (ile <= 0) return;
        for (int i = 0; i < ile; i++) wyjscie.Add(_reszta[i]);
        _reszta.RemoveRange(0, ile);
    }
}
