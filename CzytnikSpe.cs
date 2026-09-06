namespace RotorPanel;

/// <summary>
/// Wylawia ze strumienia od wzmacniacza ramki statusu, o ktore sam pytal mostek,
/// i nie przepuszcza ich dalej. Program po drugiej stronie pary portow nie prosil
/// o nie i nie ma powodu ich ogladac.
///
/// Reszta bajtow idzie bez zmian - odpowiedzi na polecenia klienta musza dotrzec
/// nietkniete.
/// </summary>
public sealed class CzytnikSpe
{
    private static readonly byte[] Naglowek = { 0xAA, 0xAA, 0xAA, StatusSpe.DlugoscDanych };

    private readonly List<byte> _reszta = new();

    public StatusSpe Status { get; private set; }

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
            int poczatek = Znajdz();

            if (poczatek < 0)
            {
                // Nic nie zapowiada ramki, ale koncowka moze byc jej poczatkiem -
                // trzymamy trzy bajty na wypadek, gdyby naglowek byl przeciety.
                int zostaw = Math.Min(Naglowek.Length - 1, _reszta.Count);
                Oddaj(wyjscie, _reszta.Count - zostaw);
                return wyjscie.ToArray();
            }

            Oddaj(wyjscie, poczatek);

            // Wzmacniacz potrafi urwac ramke statusu w polowie i od razu zaczac
            // nastepna. Taki ogryzek tez jest nasza odpowiedzia, wiec nie ma po co
            // podawac go dalej - obcinamy do nastepnego naglowka.
            int nastepny = NastepnyNaglowek();
            if (nastepny > 0 && nastepny < StatusSpe.DlugoscRamki)
            {
                _reszta.RemoveRange(0, nastepny);
                continue;
            }

            if (_reszta.Count < StatusSpe.DlugoscRamki) return wyjscie.ToArray();

            var dane = new char[StatusSpe.DlugoscDanych];
            for (int i = 0; i < dane.Length; i++)
                dane[i] = (char)_reszta[StatusSpe.DlugoscNaglowka + i];

            var status = StatusSpe.Rozbierz(new string(dane));

            if (status is null)
            {
                // To nie byla ramka statusu, tylko przypadkowa zbieznosc bajtow.
                Oddaj(wyjscie, 1);
                continue;
            }

            Status = status;
            _reszta.RemoveRange(0, StatusSpe.DlugoscRamki);
        }

        return wyjscie.ToArray();
    }

    /// <summary>Pozycja kolejnej trojki bajtow synchronizacji za biezaca ramka albo -1.</summary>
    private int NastepnyNaglowek()
    {
        for (int i = 3; i + 3 <= _reszta.Count && i < StatusSpe.DlugoscRamki; i++)
            if (_reszta[i] == 0xAA && _reszta[i + 1] == 0xAA && _reszta[i + 2] == 0xAA)
                return i;
        return -1;
    }

    /// <summary>Pozycja naglowka ramki statusu albo -1.</summary>
    private int Znajdz()
    {
        for (int i = 0; i + Naglowek.Length <= _reszta.Count; i++)
        {
            bool zgodny = true;
            for (int j = 0; j < Naglowek.Length && zgodny; j++)
                zgodny = _reszta[i + j] == Naglowek[j];
            if (zgodny) return i;
        }
        return -1;
    }

    private void Oddaj(List<byte> wyjscie, int ile)
    {
        if (ile <= 0) return;
        for (int i = 0; i < ile; i++) wyjscie.Add(_reszta[i]);
        _reszta.RemoveRange(0, ile);
    }
}
