namespace RotorPanel;

/// <summary>
/// Sklada bajty ze strony sieci w cale ramki SPID, zanim trafia na port com0com.
///
/// **To nie jest optymalizacja, tylko poprawka bledu.** Sterownik nadaje 1200 bodow, wiec
/// piecio bajtowa odpowiedz idzie przez lacze ponad 40 ms i dociera do nas w kawalkach.
/// Mostek oddawal kazdy kawalek osobno, a zapis na pare com0com potrafi stanac - zmierzone
/// w sladzie: <c>zapisano na port 1 B w 203 ms</c> i <c>4 B w 589 ms</c>. Program sterujacy
/// dostawal wiec `57`, czekal, nie doczekiwal sie reszty i czytal wlasny pusty bufor.
/// A zerowy bajt wziety za cyfre ASCII daje <c>0x00 - '0' = 0xD0 = 208</c> - stad slynny
/// odczyt "208 stopni", zawsze ten sam, bo pustka jest zawsze taka sama.
///
/// Dlatego oddajemy **cala ramke naraz albo nic**. Ramka odpowiedzi ma piec bajtow
/// (<c>57 H1 H2 H3 20</c>), rozkaz trzynascie (<c>57 ... 20</c>).
///
/// Zasada jest ogolniejsza niz ten protokol: jesli po drodze wiadomo, gdzie konczy sie
/// wiadomosc, nie wolno jej dzielic tylko dlatego, ze tak przyszla z sieci.
/// </summary>
public sealed class SkladaczSpid
{
    private readonly List<byte> _bufor = new();
    private DateTime _odkad = DateTime.MinValue;

    /// <summary>
    /// Po tym czasie oddajemy to, co mamy, nawet jesli nie jest cala ramka. Bez tego
    /// urwana ramka czekalaby do nastepnej odpowiedzi i doklejalaby sie do niej - czyli
    /// lek gorszy od choroby.
    /// </summary>
    private static readonly TimeSpan Cierpliwosc = TimeSpan.FromMilliseconds(120);

    /// <summary>Dokłada bajty i zwraca porcje gotowe do wyslania na port.</summary>
    public List<byte[]> Dopisz(byte[] dane, int ile)
    {
        lock (_bufor)
        {
            if (_bufor.Count == 0 && ile > 0) _odkad = DateTime.UtcNow;
            for (int i = 0; i < ile; i++) _bufor.Add(dane[i]);
            return Wyjmij();
        }
    }

    /// <summary>
    /// Oddaje zalegly ogon, gdy czekanie nie ma juz sensu. Wolane z zegara, bo odczyt
    /// z sieci potrafi stanac na sekunde i bez tego ostatni kawalek czekalby az tyle.
    /// </summary>
    public List<byte[]> Dopchnij()
    {
        lock (_bufor)
        {
            if (_bufor.Count == 0) return null;
            if (DateTime.UtcNow - _odkad < Cierpliwosc) return null;

            var reszta = new List<byte[]> { _bufor.ToArray() };
            _bufor.Clear();
            return reszta;
        }
    }

    private List<byte[]> Wyjmij()
    {
        List<byte[]> gotowe = null;

        while (_bufor.Count > 0)
        {
            // Smieci przed poczatkiem ramki puszczamy dalej bez zmian - mostek ma byc
            // przezroczysty, a nie madrzejszy od protokolu.
            if (_bufor[0] != 0x57)
            {
                int poczatek = _bufor.IndexOf(0x57);
                int ile = poczatek < 0 ? _bufor.Count : poczatek;
                Dodaj(ref gotowe, ile);
                continue;
            }

            if (_bufor.Count >= 5 && _bufor[4] == 0x20) { Dodaj(ref gotowe, 5); continue; }
            if (_bufor.Count >= 13 && _bufor[12] == 0x20) { Dodaj(ref gotowe, 13); continue; }

            // Trzynascie bajtow bez zamkniecia to nie jest zadna znana ramka - oddajemy
            // pierwszy bajt i szukamy poczatku dalej, zeby sie nie zapetlic.
            if (_bufor.Count >= 13) { Dodaj(ref gotowe, 1); continue; }

            break;      // za malo bajtow, czekamy na reszte
        }

        if (_bufor.Count > 0) _odkad = DateTime.UtcNow;
        return gotowe;
    }

    private void Dodaj(ref List<byte[]> gotowe, int ile)
    {
        (gotowe ??= new List<byte[]>()).Add(_bufor.GetRange(0, ile).ToArray());
        _bufor.RemoveRange(0, ile);
    }
}
