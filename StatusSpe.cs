namespace RotorPanel;

/// <summary>
/// Stan wzmacniacza SPE Expert odczytany komenda STATUS (0x90).
///
/// Ramka odpowiedzi: AA AA AA | 0x43 | 67 znakow ASCII | 2 bajty sumy | CR LF.
/// Znaki to wartosci rozdzielone przecinkami o stalej dlugosci, opisane
/// w firmowym Application Programmer's Guide (rev. 1.1).
/// </summary>
public sealed class StatusSpe
{
    public const byte Zapytanie = 0x90;
    public const int DlugoscDanych = 0x43;                  // 67 znakow
    public const int DlugoscNaglowka = 3 + 1;               // synchronizacja i dlugosc
    public const int DlugoscZSuma = DlugoscNaglowka + DlugoscDanych + 2;

    // Zmierzone na Expercie 1.3K-FA: po sumie kontrolnej ida jeszcze przecinek,
    // CR i LF, czyli cala ramka ma 76 bajtow.
    public const int DlugoscRamki = DlugoscZSuma + 3;

    /// <summary>
    /// Pola ramki po kolei, wedlug firmowej tabeli. Numery zostawiam w komentarzach,
    /// bo bez nich nie da sie tego zweryfikowac ze zrodlem:
    /// 0 model, 1 O/S, 2 T/R, 3 bank, 4 wejscie, 5 pasmo, 6 antena TX i stan ATU,
    /// 7 antena RX, 8 poziom mocy, 9 moc, 10 SWR na ATU, 11 SWR anteny, 12 napiecie PA,
    /// 13 prad PA, 14-16 temperatury (gorna, dolna, sumator), 17 ostrzezenie, 18 alarm.
    /// </summary>
    public string Model { get; private set; } = "";
    public bool Operate { get; private set; }
    public bool Nadaje { get; private set; }
    public string Bank { get; private set; } = "";
    public string Wejscie { get; private set; } = "";
    public string Pasmo { get; private set; } = "";
    public string Antena { get; private set; } = "";
    public string AntenaRx { get; private set; } = "";

    /// <summary>Stan ATU zapisany litera przy antenie: t strojenie, b obejscie, a wlaczony.</summary>
    public char Atu { get; private set; } = ' ';

    public string PoziomMocy { get; private set; } = "";
    public string Moc { get; private set; } = "";
    public string SwrAtu { get; private set; } = "";
    public string SwrAnteny { get; private set; } = "";
    public string NapieciePa { get; private set; } = "";
    public string PradPa { get; private set; } = "";
    public string Temperatura { get; private set; } = "";
    public string TemperaturaDolna { get; private set; } = "";
    public string TemperaturaSumatora { get; private set; } = "";

    /// <summary>Moc wyjsciowa jako liczba - do linijki. Zero, gdy pola nie da sie odczytac.</summary>
    public double MocWatow => Liczba(Moc);

    public double NapiecieWoltow => Liczba(NapieciePa);
    public double PradAmperow => Liczba(PradPa);
    public double SwrLiczba => Liczba(SwrAnteny);

    private static double Liczba(string tekst)
        => double.TryParse(tekst, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out double w)
           ? w : 0.0;

    /// <summary>
    /// Zakres linijki mocy, wziety z pola identyfikacyjnego ramki: "13K" to 1,3 kW,
    /// "2K" to 2 kW. Dwie cyfry znacza dziesiate czesci kilowata, jedna - pelne
    /// kilowaty. To moc znamionowa modelu, wiec chwilowy szczyt moze ja przekroczyc
    /// i linijka dobija wtedy do konca.
    /// </summary>
    public double MocMaksymalna
    {
        get
        {
            var cyfry = new string(Model.Where(char.IsDigit).ToArray());
            if (cyfry.Length == 0 || !int.TryParse(cyfry, out int n) || n <= 0) return 1500;
            return cyfry.Length >= 2 ? n * 100 : n * 1000;
        }
    }

    /// <summary>Opis poziomu mocy - w ramce idzie litera.</summary>
    public string OpisPoziomuMocy => PoziomMocy switch
    {
        "L" => "niski",
        "M" => "sredni",
        "H" => "wysoki",
        _   => PoziomMocy
    };

    /// <summary>Opis stanu ATU.</summary>
    public string OpisAtu => Atu switch
    {
        't' => "strojenie",
        'b' => "obejscie",
        'a' => "wlaczony",
        _   => ""
    };
    public char Ostrzezenie { get; private set; } = 'N';
    public char Alarm { get; private set; } = 'N';
    public DateTime Kiedy { get; private set; }

    public bool Zdrowy => Ostrzezenie == 'N' && Alarm == 'N';

    private static readonly string[] Pasma =
    {
        "160 m", "80 m", "60 m", "40 m", "30 m", "20 m", "17 m",
        "15 m", "12 m", "10 m", "6 m", "4 m"
    };

    private static readonly Dictionary<char, string> OpisyOstrzezen = new()
    {
        ['M'] = "wzmacniacz",
        ['A'] = "brak wybranej anteny",
        ['S'] = "SWR anteny",
        ['B'] = "brak poprawnego pasma",
        ['P'] = "przekroczony limit mocy",
        ['O'] = "przegrzanie",
        ['Y'] = "ATU niedostepny",
        ['W'] = "strojenie bez mocy",
        ['K'] = "ATU obejsciem",
        ['R'] = "zasilanie trzymane zdalnie",
        ['T'] = "przegrzanie sumatora",
        ['C'] = "usterka sumatora"
    };

    private static readonly Dictionary<char, string> OpisyAlarmow = new()
    {
        ['S'] = "SWR ponad limit",
        ['A'] = "zabezpieczenie wzmacniacza",
        ['D'] = "przesterowanie wejscia",
        ['H'] = "silne przegrzanie",
        ['C'] = "usterka sumatora"
    };

    /// <summary>Zwiezle podsumowanie na karte w oknie glownym.</summary>
    public string Opis
    {
        get
        {
            string kropka = " " + (char)0x00B7 + " ";
            string tryb = Operate ? "Operate" : "Standby";
            string kierunek = Nadaje ? "TX" : "RX";

            var czesci = new List<string> { tryb, kierunek };
            if (Pasmo.Length > 0) czesci.Add(Pasmo);
            if (Antena.Length > 0) czesci.Add("ant " + Antena);
            if (Nadaje && Moc.Length > 0) czesci.Add(Moc + " W");
            if (Nadaje && SwrAnteny.Length > 0) czesci.Add("SWR " + SwrAnteny);
            if (Temperatura.Length > 0) czesci.Add(Temperatura + " " + (char)0x00B0 + "C");

            return string.Join(kropka, czesci);
        }
    }

    /// <summary>Tekst na znacznik, gdy wzmacniacz zglasza ostrzezenie albo alarm.</summary>
    public string Klopot
    {
        get
        {
            if (Alarm != 'N')
                return "ALARM: " + (OpisyAlarmow.TryGetValue(Alarm, out var a) ? a : Alarm.ToString());
            if (Ostrzezenie != 'N')
                return "uwaga: " + (OpisyOstrzezen.TryGetValue(Ostrzezenie, out var o) ? o : Ostrzezenie.ToString());
            return "";
        }
    }

    /// <summary>
    /// Rozklada 67-znakowy ciag na pola. Zwraca null, gdy ciag nie ma
    /// spodziewanej budowy - lepiej nie pokazac nic niz pokazac zmyslone dane.
    /// </summary>
    public static StatusSpe Rozbierz(string dane)
    {
        var p = dane.Split(',');

        // Ciag zaczyna sie przecinkiem, wiec pierwsze pole jest puste; bywa tez
        // wariant ze znacznikiem "C". Po pominieciu zostaje 19 pol z tabeli.
        int i = p.Length > 0 && (p[0].Trim().Length == 0 || p[0].Trim() == "C") ? 1 : 0;
        if (p.Length - i < 19) return null;

        string Pole(int nr) => nr + i < p.Length ? p[nr + i].Trim() : "";

        var s = new StatusSpe
        {
            Model       = Pole(0),
            Operate     = Pole(1) == "O",
            Nadaje      = Pole(2) == "T",
            Bank        = Pole(3),
            Wejscie     = Pole(4),
            AntenaRx    = Pole(7),
            PoziomMocy  = Pole(8),
            Moc         = Pole(9).TrimStart('0'),
            SwrAtu      = Pole(10),
            SwrAnteny   = Pole(11),
            NapieciePa  = Pole(12).TrimStart('0'),
            PradPa      = Pole(13).TrimStart('0'),
            Temperatura = Pole(14).TrimStart('0'),
            TemperaturaDolna    = Pole(15).TrimStart('0'),
            TemperaturaSumatora = Pole(16).TrimStart('0'),
            Kiedy       = DateTime.UtcNow
        };

        // Pole anteny nadawczej laczy cyfre ze stanem ATU, np. "1a" to antena 1
        // z wlaczonym ATU. Rozdzielamy, zeby dalo sie pokazac jedno i drugie.
        foreach (char z in Pole(6))
        {
            if (char.IsDigit(z)) s.Antena += z;
            else if (z is 't' or 'b' or 'a') s.Atu = z;
        }

        if (int.TryParse(Pole(5), out int pasmo) && pasmo >= 0 && pasmo < Pasma.Length)
            s.Pasmo = Pasma[pasmo];

        string ostrzezenie = Pole(17);
        string alarm = Pole(18);
        if (ostrzezenie.Length > 0) s.Ostrzezenie = ostrzezenie[0];
        if (alarm.Length > 0) s.Alarm = alarm[0];

        if (s.Moc.Length == 0) s.Moc = "0";
        if (s.Temperatura.Length == 0) s.Temperatura = "0";
        if (s.NapieciePa.Length == 0) s.NapieciePa = "0";
        if (s.PradPa.Length == 0) s.PradPa = "0";

        return s.Model.Length > 0 ? s : null;
    }
}
