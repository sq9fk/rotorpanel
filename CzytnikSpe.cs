namespace RotorPanel;

/// <summary>
/// Wylawia ze strumienia od wzmacniacza ramki, o ktore sam pytal mostek: status
/// (0x43) i - gdy sami wlaczylismy tryb RCU - zawartosc wyswietlacza (0x6A).
/// Reszta bajtow idzie do klienta bez zmian, bo to odpowiedzi na jego polecenia.
///
/// Bufor przegladamy po kolei, od najwczesniejszego naglowka. Szukanie w nim
/// najpierw jednego rodzaju ramek rozjezdzalo strumien: ramki lezace wczesniej
/// trafialy do klienta zamiast do nas i odczyt stanu zamieral.
/// </summary>
public sealed class CzytnikSpe
{
    private const byte Sync = 0xAA;

    private readonly List<byte> _reszta = new();

    public StatusSpe Status { get; private set; }

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
                // Koncowka moze byc poczatkiem naglowka przecietego miedzy odczytami.
                int zostaw = Math.Min(2, _reszta.Count);
                Oddaj(wyjscie, _reszta.Count - zostaw);
                return wyjscie.ToArray();
            }

            Oddaj(wyjscie, poczatek);

            if (_reszta.Count < 4) return wyjscie.ToArray();

            byte rodzaj = _reszta[3];
            bool czekam;

            if (rodzaj == StatusSpe.DlugoscDanych)
            {
                if (!ZdejmijStatus(out czekam)) Oddaj(wyjscie, 1);
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
    private bool ZdejmijStatus(out bool czekam)
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
        _reszta.RemoveRange(0, StatusSpe.DlugoscRamki);
        return true;
    }

    private bool ZdejmijEkran(out bool czekam)
    {
        czekam = false;

        // Ramka ekranu tez bywa urwana - wtedy nastepna synchronizacja stoi
        // blizej niz jej koniec.
        int nastepny = NastepnySync(EkranSpe.DlugoscRamki);
        if (nastepny > 0)
        {
            _reszta.RemoveRange(0, nastepny);
            return true;
        }

        if (_reszta.Count < EkranSpe.DlugoscRamki)
        {
            czekam = true;
            return true;
        }

        var ekran = EkranSpe.Rozbierz(_reszta, 4);
        if (ekran is null) return false;

        Ekran = ekran;
        _reszta.RemoveRange(0, EkranSpe.DlugoscRamki);
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
