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

    private int _ileStatusow;

    /// <summary>
    /// Ile ramek statusu udalo sie rozebrac od poczatku. Mostek porownuje to z liczba
    /// **wlasnych zapytan**, zeby wiedziec, ile z nich zostalo bez odpowiedzi - tak samo,
    /// jak robi to przy rotorach. Wzmacniacz idzie tym samym tunelem po LTE, wiec podlega
    /// tym samym zastojom, a bez licznika nie bylo o tym **zadnej** informacji.
    /// </summary>
    public int IleStatusow => Volatile.Read(ref _ileStatusow);

    private int _ileWlasnych;

    // Bufor rozbioru na kopii - patrz Przezroczysty i Podgladaj.
    private readonly List<byte> _podglad = new();

    /// <summary>
    /// Ile ramek statusu bylo odpowiedzia na **nasze** zapytanie.
    ///
    /// Do bilansu wymian wolno liczyc tylko te. Pierwsza wersja (1.11.18) liczyla **wszystkie**
    /// ramki statusu, takze te, o ktore poprosil klient na drugiej stronie pary - i dopasowywala
    /// je do naszych zapytan. Wychodzily z tego czasy w rodzaju **61 ms**, fizycznie niemozliwe,
    /// oraz "brak 18" i "po terminie 10" wziete z powietrza. Przyrzad mierzyl cudzy ruch.
    /// </summary>
    public int IleWlasnychOdpowiedzi => Volatile.Read(ref _ileWlasnych);

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
    /// <summary>
    /// Melduje, ze wyslalismy wlasne zapytanie. **Zwraca false, gdy zapytanie sie nie zmiescilo**
    /// w limicie oczekujacych - i to jest wazne dla bilansu wymian: takiej odpowiedzi nie
    /// zdejmiemy ze strumienia, wiec nie wolno jej tez liczyc jako wyslanego zapytania.
    ///
    /// Bez tego licznik wyslanych rosl, licznik odpowiedzi nie nadazal i w podpowiedzi
    /// wychodzil **wymyslony brak** - a czasy schodzily do 25 ms, czyli ponizej fizycznej
    /// mozliwosci. Drugi raz ten sam blad na tym samym mostku.
    /// </summary>
    /// <summary>
    /// Zapomina wlasne zapytania czekajace na odpowiedz.
    ///
    /// Wolamy to w chwili, gdy port przejmuje klient. Licznik oczekujacych decyduje o tym,
    /// czy ramke statusu zdejmiemy ze strumienia, czy przepuscimy - a on nie wie, czyja ona
    /// jest. Zostawione po nas "jeszcze dwie nasze" zjadloby dwie pierwsze ramki, o ktore
    /// poprosil klient, i jego odczyt zaczynalby sie od dziury.
    /// </summary>
    public void ZapomnijWlasne() => Interlocked.Exchange(ref _wlasneOczekujace, 0);

    public bool ZglosWlasneZapytanie()
    {
        if (Volatile.Read(ref _wlasneOczekujace) >= MaksWlasnych) return false;
        Interlocked.Increment(ref _wlasneOczekujace);
        return true;
    }

    public EkranSpe Ekran { get; private set; }

    /// <summary>
    /// Czy zdejmowac ze strumienia ramki wyswietlacza. Wlaczamy to tylko wtedy,
    /// gdy sami wlaczylismy tryb RCU - inaczej zabralibysmy je programowi, ktory
    /// o nie poprosil.
    /// </summary>
    public bool PrzechwytujEkran { get; set; }

    /// <summary>
    /// Tryb przezroczysty: **kazdy bajt idzie do klienta natychmiast i bez zmian**, a rozbior
    /// robimy na kopii. Wlaczamy go wtedy, gdy port trzyma program kliencki.
    ///
    /// Zwykla droga - ta ponizej - musi ramki **skladac**, a skladanie oznacza trzy rzeczy,
    /// ktorych nie wolno robic cudzemu strumieniowi:
    ///
    /// 1. **trzyma** niedokonczona ramke do nastepnego kawalka (a ser2net potrafi przysylac
    ///    po jednym bajcie na segment, co zmierzylismy po stronie rotorow),
    /// 2. **kasuje** ramke urwana w polowie - `ZdejmijStatus` przy napotkaniu wczesniejszej
    ///    synchronizacji wyrzuca poczatek bez sladu, a wzmacniacz urywa ramki regularnie,
    /// 3. **przeramowuje** strumien - trzy bajty `AA` wewnatrz klatki wyswietlacza wygladaja
    ///    jak naglowek i dalszy rozbior idzie od zlego miejsca.
    ///
    /// Kazda z tych trzech rzeczy widac u klienta jako migniecie obrazu. Dlatego przy kliencie
    /// na porcie nie skladamy **nic**: rozbior na kopii moze sie mylic, bo nikomu nie szkodzi.
    /// </summary>
    public bool Przezroczysty { get; set; }

    /// <summary>
    /// Przyjmuje surowy kawalek strumienia, zwraca to, co ma isc do klienta.
    /// Niedokonczona ramka zostaje w srodku do nastepnego wywolania.
    /// </summary>
    public byte[] Przepusc(byte[] bufor, int ile)
    {
        if (Przezroczysty)
        {
            // Zaleglosci z trybu skladania oddajemy razem z nowym kawalkiem - zmiana trybu
            // nie moze zjesc bajtow, ktore juz do nas przyszly.
            var kopia = new byte[_reszta.Count + ile];
            _reszta.CopyTo(kopia, 0);
            Array.Copy(bufor, 0, kopia, _reszta.Count, ile);
            _reszta.Clear();

            Podgladaj(kopia);
            return kopia;
        }

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
    /// Rozbior ramek statusu **na kopii**, do wlasnego uzytku: karta i okno stanu pokazuja
    /// wtedy to, o co poprosil klient, nie wysylajac ani jednego bajtu. Ze strumienia nie
    /// znika nic - gdybysmy tu trafili w zly bajt, klient i tak dostal juz swoje.
    /// </summary>
    private void Podgladaj(byte[] dane)
    {
        _podglad.AddRange(dane);

        // Bufor podgladu nie moze rosnac bez konca: gdy klient nie pyta o status, plyna przez
        // nas same klatki wyswietlacza i nie ma czego z nich zdjac.
        if (_podglad.Count > 8192) _podglad.RemoveRange(0, _podglad.Count - 1024);

        while (true)
        {
            int i = Szukaj(_podglad, StatusSpe.DlugoscRamki);
            if (i < 0) return;

            var znaki = new char[StatusSpe.DlugoscDanych];
            for (int z = 0; z < znaki.Length; z++)
                znaki[z] = (char)_podglad[i + StatusSpe.DlugoscNaglowka + z];

            var status = StatusSpe.Rozbierz(new string(znaki));

            if (status is null)
            {
                // Falszywy trop wewnatrz innej ramki - idziemy o bajt dalej.
                _podglad.RemoveRange(0, i + 1);
                continue;
            }

            Status = status;
            OstatniObcyStatus = DateTime.UtcNow;
            Interlocked.Increment(ref _ileStatusow);
            _podglad.RemoveRange(0, i + StatusSpe.DlugoscRamki);
        }
    }

    /// <summary>
    /// Pozycja pelnej ramki statusu w buforze podgladu albo -1.
    ///
    /// Tu sprawdzamy **wiecej niz w drodze skladania**: naglowek, dlugosc i zakonczenie `CR LF`.
    /// Powod jest asymetryczny - skladacz zdejmuje ramke ze strumienia, wiec falszywy trop
    /// jest tam widoczny od razu, a podglad tylko czyta cudzy ruch i nikt by sie nie dowiedzial,
    /// ze karta pokazuje liczby zlozone z dwoch roznych ramek. Wolimy nie pokazac nic.
    /// </summary>
    private static int Szukaj(List<byte> gdzie, int dlugosc)
    {
        for (int i = 0; i + dlugosc <= gdzie.Count; i++)
            if (gdzie[i] == Sync && gdzie[i + 1] == Sync && gdzie[i + 2] == Sync &&
                gdzie[i + 3] == StatusSpe.DlugoscDanych &&
                gdzie[i + dlugosc - 2] == 0x0D && gdzie[i + dlugosc - 1] == 0x0A)
                return i;
        return -1;
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
        Interlocked.Increment(ref _ileStatusow);

        // Odpowiedz na wlasne zapytanie zdejmujemy, cudza idzie do klienta - rozbior
        // dla siebie zrobilismy juz wyzej, wiec nic na tym nie tracimy.
        if (Volatile.Read(ref _wlasneOczekujace) > 0)
        {
            if (Interlocked.Decrement(ref _wlasneOczekujace) < 0)
                Interlocked.Exchange(ref _wlasneOczekujace, 0);
            Interlocked.Increment(ref _ileWlasnych);
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
