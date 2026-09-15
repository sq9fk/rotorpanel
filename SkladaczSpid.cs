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

    // Ile razy oddalismy ogon po czasie, nie doczekawszy sie ramki, i ile ramek
    // rozpoznalismy w ogole. Patrz Przezroczysty.
    private int _ogonow;
    private int _ramek;

    /// <summary>
    /// Czy urwany poczatek odpowiedzi ma byc **skasowany** zamiast oddany dalej.
    ///
    /// Wlaczane tylko dla kierunku od sterownika. Piec bajtow `57 H1 H2 H3 20` niesie jedna
    /// liczbe i **polowa tej liczby nie jest liczba** - program sterujacy, ktory dostanie
    /// `57 03 06`, doczyta reszte z wlasnego pustego bufora i pokaze 208 stopni. Lepiej,
    /// zeby nie dostal nic: zapyta znowu za sekunde.
    ///
    /// W druga strone (rozkazy do sterownika) **nie wolno tego wlaczac** - zgubiony rozkaz
    /// to nie jest brak odczytu, tylko niewykonana nastawa.
    ///
    /// Kazde takie skasowanie idzie do <see cref="Odrzucono"/>, bo dane o polozeniu anteny
    /// nie moga znikac po cichu.
    /// </summary>
    public bool KasujUrwaneOdpowiedzi { get; set; }

    /// <summary>Wolane z kawalkiem, ktory zostal skasowany zamiast oddany.</summary>
    public Action<byte[]> Odrzucono { get; set; }

    /// <summary>
    /// Czy skladacz wylaczyl sie sam, bo dane nie wygladaja na SPID.
    ///
    /// Skladanie ramek jest **swiadome protokolu**, a mostek ma byc przezroczysty. Gdyby ktos
    /// podpial pod te sama konfiguracje inny sterownik, kazda wymiana czekalaby na zawor czasowy
    /// (120 ms), bo ramka nigdy by sie nie domknela. Dlatego po dwudziestu takich ogonach **bez
    /// ani jednej rozpoznanej ramki** przestajemy sie wtracac i puszczamy wszystko wprost.
    ///
    /// Prog jest asymetryczny celowo: jedna poprawna ramka wystarczy, by uznac protokol za
    /// znany, a do wycofania sie trzeba dwudziestu nieudanych prob. Lepiej raz za duzo poczekac
    /// niz zepsuc dzialajacy tor.
    /// </summary>
    public bool Przezroczysty => _ramek == 0 && _ogonow >= 20;

    /// <summary>
    /// Po tym czasie oddajemy to, co mamy, nawet jesli nie jest cala ramka. Bez tego
    /// urwana ramka czekalaby do nastepnej odpowiedzi i doklejalaby sie do niej - czyli
    /// lek gorszy od choroby.
    ///
    /// **Osiemset milisekund, nie sto dwadziescia - bo tor idzie przez LTE.** Pierwotna
    /// wartosc dobralem do sieci lokalnej, gdzie odstep miedzy bajtami odpowiedzi wynosi
    /// 16-26 ms (zmierzone zrzutem pakietow). Tunel WireGuard po LTE potrafi jednak stanac
    /// na pol sekundy i wypuscic wszystko naraz - w dzienniku z 14 wrzesnia dwa mostki
    /// zglosily rowno **615 ms** w tej samej milisekundzie. Gdyby taki zastoj trafil
    /// w **srodek** ramki, zawor przy 120 ms wypchnalby jej poczatek jako osobna porcje
    /// i program sterujacy zobaczylby znowu 208 stopni - czyli dokladnie to, co ten
    /// skladacz mial wyeliminowac.
    ///
    /// Gorna granica bierze sie z rytmu odpytywania: PstRotator pyta co sekunde, wiec ogon
    /// oddany po 800 ms i tak wychodzi **przed** nastepna odpowiedzia i nie ma sie z czym
    /// skleic. Osiemset milisekund to najwiecej, ile mozna czekac, nie tracac tej gwarancji.
    /// </summary>
    public TimeSpan Cierpliwosc { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// Wyrzuca to, co zostalo w srodku, i zwraca to albo null.
    ///
    /// **Musi byc wolane przy kazdym zestawieniu lacza.** Skladacz zyje tak dlugo jak mostek,
    /// a polaczenie moze paść w polowie ramki - wtedy w buforze zostaje np. `57 03 06`. Bez
    /// czyszczenia ten ogon czekal na **nastepne** polaczenie i sklejal sie z jego pierwszymi
    /// bajtami w ramke, ktorej nikt nie wyslal. Zgłoszone przez uzytkownika: odczyt skacze na
    /// 208 przy zatrzymywaniu i wznawianiu mostka.
    /// </summary>
    public byte[] Wyczysc()
    {
        lock (_bufor)
        {
            if (_bufor.Count == 0) return null;
            var reszta = _bufor.ToArray();
            _bufor.Clear();
            _odkad = DateTime.MinValue;
            return reszta;
        }
    }

    /// <summary>Dokłada bajty i zwraca porcje gotowe do wyslania na port.</summary>
    public List<byte[]> Dopisz(byte[] dane, int ile)
    {
        lock (_bufor)
        {
            if (_bufor.Count == 0 && ile > 0) _odkad = DateTime.UtcNow;
            for (int i = 0; i < ile; i++) _bufor.Add(dane[i]);

            if (Przezroczysty)
            {
                var wprost = new List<byte[]> { _bufor.ToArray() };
                _bufor.Clear();
                return wprost;
            }

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

            var ogon = _bufor.ToArray();
            _bufor.Clear();
            _ogonow++;

            // Urwany poczatek odpowiedzi to nie sa dane - to polowa liczby. Patrz
            // KasujUrwaneOdpowiedzi.
            if (KasujUrwaneOdpowiedzi && ogon.Length < 5 && ogon[0] == 0x57)
            {
                Odrzucono?.Invoke(ogon);
                return null;
            }

            return new List<byte[]> { ogon };
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

            if (_bufor.Count >= 5 && _bufor[4] == 0x20) { _ramek++; Dodaj(ref gotowe, 5); continue; }
            if (_bufor.Count >= 13 && _bufor[12] == 0x20) { _ramek++; Dodaj(ref gotowe, 13); continue; }

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
