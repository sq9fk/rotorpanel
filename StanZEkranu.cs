namespace RotorPanel;

/// <summary>
/// Stan wzmacniacza odczytany z jego **wlasnego wyswietlacza**, a nie z zapytania o status.
///
/// Po co taka droga naokolo: zapytanie `0x90` wytraca RC-1216H z trybu "Remoted" - zmierzone
/// eksperymentem kontrolnym (samo okno sterowania: stan stabilny; dolozone okno stanu, ktore
/// wznawia zapytania: natychmiast zaczyna przelaczac). Ekran w trybie RCU dostajemy natomiast
/// **za darmo przy kazdym pulsie**, wiec czytajac stan stamtad, mozna miec jedno i drugie:
/// swieze liczby i nieruchome "Remoted".
///
/// Ekran to siatka **40 na 8 znakow**. Dolny pasek stoi w tym samym miejscu w trybie Standby
/// i Operate - sprawdzone na zrzutach z obu trybow:
///
/// <code>
///         0123456789012345678901234567890123456789
///     5 |────┬──────┬───┬─────┬─────┬─────┬──────|
///     6 | IN │ BAND │ANT│ CAT │ OUT │ SWR │ TEMP |
///     7 |  1 │ 15 m │ 1 │NONE │ MID │--.--│ 30°C |
/// </code>
///
/// **Pozycje nie sa zgadniete z obrazka** - pochodza ze zrzutu siatki wyslanego przez mostek
/// do `podejrzane.txt` (1.11.32). W tej sprawie zgadywania bylo juz dosc.
/// </summary>
public sealed class StanZEkranu
{
    // Granice pol wyznaczone przez separatory w wierszu 5: pozycje 4, 11, 15, 21, 27, 33.
    private const int Naglowki = 6;
    private const int Wartosci = 7;

    private static readonly (string Etykieta, int Od, int Ile)[] Pola =
    {
        ("IN",    0,  4),
        ("BAND",  5,  6),
        ("ANT",  12,  3),
        ("CAT",  16,  5),
        ("OUT",  22,  5),
        ("SWR",  28,  5),
        ("TEMP", 34,  6)
    };

    public string Tryb { get; private set; } = "";
    public string Wejscie { get; private set; } = "";
    public string Pasmo { get; private set; } = "";
    public string Antena { get; private set; } = "";
    public string Cat { get; private set; } = "";
    public string Wyjscie { get; private set; } = "";
    public string Swr { get; private set; } = "";
    public string Temperatura { get; private set; } = "";

    /// <summary>Kiedy ekran, z ktorego to przeczytano, przyszedl ze wzmacniacza.</summary>
    public DateTime Kiedy { get; private set; }

    /// <summary>
    /// Czyta pasek stanu albo zwraca null.
    ///
    /// **Zwracamy null przy kazdej watpliwosci.** Gdy uzytkownik wejdzie w menu wzmacniacza,
    /// pasek znika i wtedy jedyna uczciwa odpowiedz brzmi "nie wiem" - lepiej zostawic na karcie
    /// poprzedni odczyt z jego wlasnym znacznikiem czasu niz podstawic liczbe wzieta z innego
    /// ekranu. Dlatego sprawdzamy, czy **wszystkie siedem etykiet** stoi dokladnie tam, gdzie ma.
    /// </summary>
    public static StanZEkranu Czytaj(EkranSpe ekran)
    {
        if (ekran?.Wiersze is null || ekran.Wiersze.Length <= Wartosci) return null;

        string naglowki = ekran.Wiersze[Naglowki];
        string wartosci = ekran.Wiersze[Wartosci];
        if (naglowki is null || wartosci is null) return null;
        if (naglowki.Length < EkranSpe.Kolumn || wartosci.Length < EkranSpe.Kolumn) return null;

        foreach (var pole in Pola)
            if (Wytnij(naglowki, pole.Od, pole.Ile) != pole.Etykieta) return null;

        return new StanZEkranu
        {
            Kiedy       = ekran.Kiedy,
            Wejscie     = Wytnij(wartosci, Pola[0].Od, Pola[0].Ile),
            Pasmo       = Wytnij(wartosci, Pola[1].Od, Pola[1].Ile),
            Antena      = Wytnij(wartosci, Pola[2].Od, Pola[2].Ile),
            Cat         = Wytnij(wartosci, Pola[3].Od, Pola[3].Ile),
            Wyjscie     = Wytnij(wartosci, Pola[4].Od, Pola[4].Ile),
            Swr         = Wytnij(wartosci, Pola[5].Od, Pola[5].Ile),
            Temperatura = Wytnij(wartosci, Pola[6].Od, Pola[6].Ile),
            Tryb        = Rozpoznaj(ekran)
        };
    }

    /// <summary>
    /// Tryb bierzemy z gornej czesci ekranu. W spoczynku wzmacniacz wypisuje tam wprost
    /// "Standby"; w pracy tego slowa **nie ma**, sa za to linijki `PA OUT` i `I PA`. Dlatego
    /// rozpoznajemy po tym, co widac, a nie po tym, czego brakuje - a gdy nie widac zadnego
    /// z dwoch znanych ukladow, mowimy "nie wiem" pustym napisem.
    /// </summary>
    private static string Rozpoznaj(EkranSpe ekran)
    {
        var gora = string.Join(" ", ekran.Wiersze.Take(Naglowki));
        if (gora.Contains("PA OUT")) return "Operate";
        if (gora.Contains("Standby")) return "Standby";
        return "";
    }

    private static string Wytnij(string wiersz, int od, int ile)
        => wiersz.Substring(od, ile).Trim();

    public override string ToString()
        => (Tryb.Length > 0 ? Tryb + ", " : "") +
           "pasmo " + Pasmo + ", ant " + Antena + ", wej " + Wejscie +
           ", wyj " + Wyjscie + ", SWR " + Swr + ", temp " + Temperatura;
}
