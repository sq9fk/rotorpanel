namespace RotorPanel;

/// <summary>
/// Pulapka na podejrzane rozkazy rotora. Dziala **zawsze**, bez wlaczania sladu.
///
/// Powstala, bo nastawa 208 stopni wracala mimo kolejnych poprawek, a zlapanie jej sladem
/// wymagalo szczescia: trzeba bylo miec wlaczony zapis dokladnie wtedy, gdy sie zdarzy.
/// Tutaj jest odwrotnie - program czeka na nia sam i w chwili, gdy ja zobaczy, zapisuje
/// nie tylko sama ramke, ale i to, co szlo w obie strony **przed** nia. Bez tego kontekstu
/// nie da sie odroznic rozkazu, ktory ktos naprawde wyslal, od ramki zlozonej z kawalkow.
///
/// Zapis idzie do <c>podejrzane.txt</c> obok programu i ma limit rozmiaru - to ma byc
/// dowod rzeczowy, nie dziennik.
/// </summary>
public static class Pulapka
{
    /// <summary>
    /// Czy zbierac diagnostyke ramek. **W wersji produkcyjnej domyslnie wylaczone.**
    ///
    /// Pulapka powstala do polowania na rzadkie usterki i do tego jest niezastapiona - ale
    /// kosztuje przy **kazdym kawalku danych**: kopiowanie do buforow obu kierunkow i zlozenie
    /// napisu do dziennika, na watku pompy. Gdy nikt tych danych nie czyta, jest to praca
    /// wykonywana wylacznie po to, zeby ja zaraz wyrzucic.
    ///
    /// Wylacznik jest w Ustawieniach. Gdy jest zgaszony, **nie powstaje nawet plik** - a to
    /// samo w sobie jest informacja: pusty katalog znaczy "nie zbieralismy", nie "nic nie bylo".
    /// Dlatego liczniki, ktore mialy sens diagnostyczny (odrzucone odczyty pozycji, braki
    /// odpowiedzi), zostaly **przeniesione do interfejsu** i licza sie zawsze.
    /// </summary>
    public static volatile bool Wlaczona;

    private static readonly object _zamek = new();
    private const long MaksymalnyRozmiar = 2L * 1024 * 1024;

    /// <summary>Bufor ostatnich bajtow jednego kierunku.</summary>
    public sealed class Bufor
    {
        private readonly byte[] _dane;
        private int _ile;

        public Bufor(int pojemnosc) => _dane = new byte[pojemnosc];

        public void Dopisz(byte[] zrodlo, int ile)
        {
            lock (_dane)
            {
                foreach (byte b in Ostatnie(zrodlo, ile, _dane.Length))
                {
                    if (_ile == _dane.Length)
                    {
                        Array.Copy(_dane, 1, _dane, 0, _dane.Length - 1);
                        _ile--;
                    }
                    _dane[_ile++] = b;
                }
            }
        }

        public string Hex()
        {
            lock (_dane)
            {
                var s = new System.Text.StringBuilder(_ile * 3);
                for (int i = 0; i < _ile; i++) s.Append(_dane[i].ToString("X2")).Append(' ');
                return s.ToString().TrimEnd();
            }
        }

        private static IEnumerable<byte> Ostatnie(byte[] zrodlo, int ile, int limit)
        {
            int od = Math.Max(0, ile - limit);
            for (int i = od; i < ile; i++) yield return zrodlo[i];
        }
    }

    /// <summary>
    /// Sklada strumien w ramki. **Nie wolno szukac ramki w pojedynczej porcji** - ramka
    /// rozkazu ma 13 bajtow i potrafi przyjsc podzielona na dwie porcje (w sladzie widac
    /// porcje po 1 i 4 bajty). Pierwsza wersja pulapki tego nie uwzgledniala i mogla
    /// przepuscic dokladnie to, na co czekala.
    /// </summary>
    public sealed class Wykrywacz
    {
        private readonly List<byte> _bufor = new();

        public List<byte[]> Ramki(byte[] dane, int ile)
        {
            var znalezione = new List<byte[]>();

            for (int i = 0; i < ile; i++) _bufor.Add(dane[i]);

            int od = 0;
            while (od + 13 <= _bufor.Count)
            {
                if (_bufor[od] != 0x57 || _bufor[od + 12] != 0x20) { od++; continue; }
                znalezione.Add(_bufor.GetRange(od, 13).ToArray());
                od += 13;
            }

            _bufor.RemoveRange(0, od);

            // Bez tego niedokonczona ramka rosla by w nieskonczonosc.
            if (_bufor.Count > 64) _bufor.RemoveRange(0, _bufor.Count - 64);

            return znalezione;
        }
    }

    /// <summary>
    /// Opis nastawy. Azymut liczymy **na kilka sposobow**, bo bajt rozdzielczosci bywa
    /// rozny, a pomylka w dzielniku ukrylaby wlasnie te nastawe, ktorej szukamy: 5680/10
    /// to 208, ale 1136 z rozdzielczoscia 2 albo 2272 z rozdzielczoscia 4 to tez 208.
    /// </summary>
    public static string OpiszNastawe(byte[] r, out bool podejrzana)
    {
        podejrzana = false;
        if (r.Length < 13 || r[11] != 0x2F) return null;

        bool ascii = true;
        int wartosc = 0;
        for (int k = 1; k <= 4; k++)
        {
            int cyfra = r[k] - '0';
            if (cyfra < 0 || cyfra > 9) { ascii = false; break; }
            wartosc = wartosc * 10 + cyfra;
        }

        if (!ascii)
        {
            podejrzana = true;
            return "NASTAWA z cyframi spoza ASCII (zerowy bajt czytany jak cyfra daje 208): " +
                   Bajty(r, 0, 13);
        }

        int rozdzielczosc = r[5] > 0 ? r[5] : 1;
        var warianty = new List<string>();
        foreach (int dzielnik in new[] { 1, 2, 4, 10, 10 * rozdzielczosc })
        {
            double az = (double)wartosc / dzielnik - 360;
            warianty.Add("/" + dzielnik + " = " + az.ToString("0.#"));
            if (Math.Abs(az - 208) < 0.05) podejrzana = true;
        }

        return (podejrzana ? "NASTAWA 208 " : "nastawa ") + Bajty(r, 0, 13) +
               "   cyfry " + wartosc + ", rozdzielczosc " + rozdzielczosc +
               ", azymut " + string.Join("  ", warianty);
    }

    /// <summary>
    /// Sam azymut nastawy, bez skladania opisu - do listy, ktora mostek trzyma **zawsze**,
    /// takze przy zgaszonej diagnostyce.
    ///
    /// Dzielnik to ten, ktory zgadza sie ze sterownikiem: cyfry 5400 przy rozdzielczosci 1
    /// to 180 stopni, czyli `wartosc / (10 * rozdzielczosc) - 360`. Podejrzenie liczymy
    /// szerzej, tak jak <see cref="OpiszNastawe"/> - jesli **ktorykolwiek** sensowny dzielnik
    /// daje 208, chcemy o tym wiedziec, bo pomylka w dzielniku ukrylaby wlasnie ta nastawe,
    /// ktorej szukamy.
    /// </summary>
    public static double? AzymutNastawy(byte[] r, out bool podejrzana)
    {
        podejrzana = false;
        if (r.Length < 13 || r[11] != 0x2F) return null;

        int wartosc = 0;
        for (int k = 1; k <= 4; k++)
        {
            int cyfra = r[k] - '0';
            if (cyfra < 0 || cyfra > 9) { podejrzana = true; return null; }
            wartosc = wartosc * 10 + cyfra;
        }

        int rozdzielczosc = r[5] > 0 ? r[5] : 1;
        foreach (int dzielnik in new[] { 1, 2, 4, 10, 10 * rozdzielczosc })
            if (Math.Abs((double)wartosc / dzielnik - 360 - 208) < 0.05) podejrzana = true;

        return (double)wartosc / (10 * rozdzielczosc) - 360;
    }

    /// <summary>
    /// Dziennik obu kierunkow w **jednej** osi czasu.
    ///
    /// Bufory "do sterownika" i "od sterownika" pokazuja, co przeszlo, ale nie pokazuja
    /// **kolejnosci miedzy nimi** - a to jest roznica miedzy "sterownik odpowiedzial 208 na
    /// STOP" a "program wyslal STOP, bo zobaczyl 208". Na tym wlasnie sie potknalem:
    /// korelacja byla zupelna, a mimo to wniosek mogl byc odwrotny.
    /// </summary>
    public sealed class Dziennik
    {
        private readonly Queue<string> _wpisy = new();
        private const int Ile = 150;

        /// <summary>
        /// Czy rozbierac bajty jak ramki SPID. Przy wzmacniaczu **nie** - `SladSpid` opisywalby
        /// wtedy kazdy bajt jako "poza ramka", zasmiecajac dziennik udawana wiedza.
        /// </summary>
        public bool RozbierajSpid { get; set; } = true;

        public void Dopisz(string kierunek, byte[] dane, int ile)
        {
            string opis = RozbierajSpid ? SladSpid.Opis(dane, ile) : "";
            string wpis = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + kierunek + "  " +
                          Slad.Podglad(dane, ile) + (opis.Length > 0 ? "   " + opis : "");

            lock (_wpisy)
            {
                _wpisy.Enqueue(wpis);
                while (_wpisy.Count > Ile) _wpisy.Dequeue();
            }
        }

        public string Wypisz()
        {
            lock (_wpisy) return string.Join(Environment.NewLine + "      ", _wpisy);
        }
    }

    /// <summary>
    /// Znak, ze pulapka chodzi. Bez tego "nie ma pliku" znaczy dwie rzeczy naraz: albo nic
    /// podejrzanego nie przeszlo, albo wersja z pulapka w ogole nie byla uruchomiona.
    /// </summary>
    public static void Uzbrojono(string podpis)
    {
        if (!Wlaczona) return;
        Zapisz(podpis, "pulapka uzbrojona, wersja " +
               System.Reflection.Assembly.GetExecutingAssembly().GetName().Version, null, null,
               TimeSpan.Zero);
    }

    /// <summary>
    /// Sklada wpis **w watku wolajacym** i oddaje go do zapisu w tle.
    ///
    /// **Zapis do pliku nie moze stac na drodze danych.** Do 1.11.18 `Zapisz` otwieral plik,
    /// dopisywal kilka kilobajtow i zamykal - wszystko synchronicznie, w watku pompy, ktora
    /// w tym czasie **nie czytala gniazda**. Plik lezy przy pliku wykonywalnym, a ten u
    /// uzytkownika stoi w katalogu synchronizowanym przez OneDrive, wiec kazdy dopis budzi
    /// synchronizacje. Czujnik zastoju zlapal to wprost: **27 z 38** wpisow "BRAK ODPOWIEDZI"
    /// mialo zastoj procesu w ciagu poltorej sekundy. Narzedzie do szukania usterki zaczelo
    /// ja wspolwytwarzac.
    ///
    /// Tresc skladamy nadal na miejscu - dziennik i bufory musza byc sfotografowane **w chwili
    /// zdarzenia**, nie kilkaset milisekund pozniej. Do tla idzie wylacznie gotowy tekst.
    /// </summary>
    public static void Zapisz(string podpis, string powod, Bufor doSterownika, Bufor odSterownika,
                              TimeSpan odPolaczenia, Dziennik dziennik = null)
    {
        if (!Wlaczona) return;

        try
        {
            var tekst = new System.Text.StringBuilder();
            tekst.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + podpis);
            tekst.AppendLine("    " + powod);
            if (doSterownika != null)
            {
                tekst.AppendLine("    od zestawienia lacza: " +
                                 odPolaczenia.TotalSeconds.ToString("0.0") + " s");
                tekst.AppendLine("    do sterownika (ostatnie bajty): " + doSterownika.Hex());
                tekst.AppendLine("    od sterownika (ostatnie bajty): " + odSterownika.Hex());
            }
            if (dziennik != null)
            {
                tekst.AppendLine("    przebieg w jednej osi czasu (najstarsze u gory):");
                tekst.AppendLine("      " + dziennik.Wypisz());
            }
            tekst.AppendLine();

            Dopisz(tekst.ToString());
        }
        catch { /* pulapka nie moze przeszkadzac w pracy */ }
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _doZapisu = new();
    private static readonly SemaphoreSlim _budzik = new(0);
    private static Thread _pisarz;

    private static void Dopisz(string tekst)
    {
        lock (_zamek)
        {
            if (_pisarz is null)
            {
                // Wlasny watek, nie pula - zapis bywa dlugi, a pula jest wspoldzielona
                // z pompami mostkow i to wlasnie jej zaglodzenie chcemy tu wyeliminowac.
                _pisarz = new Thread(Petla) { IsBackground = true, Name = "pulapka" };
                _pisarz.Start();
            }
        }

        // Przy zalewie wpisow wolimy zgubic najstarsze niz rosnac bez konca.
        if (_doZapisu.Count > 200) _doZapisu.TryDequeue(out _);

        _doZapisu.Enqueue(tekst);
        _budzik.Release();
    }

    private static void Petla()
    {
        string katalog = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        string plik = Path.Combine(katalog, "podejrzane.txt");
        string poprzedni = Path.Combine(katalog, "podejrzane.1.txt");

        while (true)
        {
            _budzik.Wait();
            if (!_doZapisu.TryDequeue(out string tekst)) continue;

            try { Dopisz(plik, poprzedni, tekst, MaksymalnyRozmiar); }
            catch { /* pulapka nie moze przeszkadzac w pracy */ }
        }
    }

    /// <summary>
    /// Dopisuje wpis, a po przekroczeniu limitu **zaczyna nowy plik** zamiast zamilknac.
    ///
    /// Do 1.12.3 zapis po prostu ustawal. Plik konczyl sie w polowie usterki, a jego ostatnia
    /// linia wygladala dokladnie tak samo jak koniec spokojnej sesji - czyli narzedzie klamalo
    /// w najgorszym momencie, bo "nic wiecej nie ma" znaczylo naraz "nic sie nie dzialo"
    /// i "przestalem patrzec". 21 wrzesnia tak wlasnie wyszlo: sto piecdziesiat wpisow
    /// "BRAK ODPOWIEDZI", kazdy z pelnym dziennikiem, wypelnilo dwa megabajty w dwie i pol
    /// godziny - a najnowsze zdarzenia, czyli jedyne istotne, nie mialy sie juz gdzie zapisac.
    ///
    /// Zostawiamy **jedno pokolenie wstecz** (`podejrzane.1.txt`). Nowsze jest wazniejsze od
    /// starszego, bo usterki szuka sie od konca; stare trzymamy, zeby nie zgubic poczatku
    /// dlugiej usterki. Stary plik dostaje na koncu linie, ktora mowi, gdzie szukac dalej -
    /// inaczej "koniec pliku" nadal bylby dwuznaczny.
    ///
    /// Wydzielone z <see cref="Petla"/>, zeby dalo sie to sprawdzic w harnessie na plikach
    /// tymczasowych. Bez tego jedynym testem byloby uruchomienie programu na dwie godziny.
    /// </summary>
    internal static void Dopisz(string plik, string poprzedni, string tekst, long limit)
    {
        if (File.Exists(plik) && new FileInfo(plik).Length > limit)
        {
            // Rozmiar piszemy tak, zeby zawsze byl prawdziwy - "limit 0 MB" w pliku
            // diagnostycznym podwaza zaufanie do wszystkiego, co jest obok.
            string ile = limit >= 1024 * 1024 ? limit / 1024 / 1024 + " MB"
                       : limit >= 1024       ? limit / 1024 + " kB"
                                             : limit + " B";

            File.AppendAllText(plik, "=== plik osiagnal limit " + ile +
                                     " - dalszy ciag w podejrzane.txt ===" +
                                     Environment.NewLine + Environment.NewLine);
            File.Delete(poprzedni);
            File.Move(plik, poprzedni);
        }

        File.AppendAllText(plik, tekst);
    }

    private static string Bajty(byte[] dane, int od, int ile)
    {
        var s = new System.Text.StringBuilder(ile * 3);
        for (int i = od; i < od + ile && i < dane.Length; i++)
            s.Append(dane[i].ToString("X2")).Append(' ');
        return s.ToString().TrimEnd();
    }
}
