namespace RotorPanel;

/// <summary>
/// Wykrywa zastoje **wlasnego procesu** - i tylko po to istnieje.
///
/// W dzienniku z 14 wrzesnia, 20:47:29, dwa mostki naraz zglosily odpowiedz po **615 ms**,
/// z zapisami oddalonymi o jedna milisekunde. Do tego w sladzie widac, ze pieciobajtowa
/// odpowiedz, ktora normalnie przychodzi **pieciona osobnymi odczytami co 20 ms**, tym razem
/// wpadla naraz: `57 03 06 00` jednym odczytem i `20` w tej samej milisekundzie. Bajty
/// spietrzyly sie gdzies i zostaly wypuszczone jednym strzalem.
///
/// Dwa wyjasnienia pasuja do tego obrazu identycznie:
///
/// * zastoj **na drodze** do Pi - wtedy bajty naprawde przyszly pozno,
/// * zastoj **u nas** - nikt przez chwile nie czytal gniazd, wiec bajty czekaly w buforze
///   systemu i zostaly odebrane hurtem, gdy proces wrocil do zycia.
///
/// Z wnetrza mostka nie da sie ich rozroznic, bo obie wygladaja tak samo: pozny odczyt.
/// Dlatego mierzymy to osobno. Zegar tyka co 100 ms; jesli tyknie **za pozno**, znaczy to,
/// ze proces stal - odsmiecanie, glodzenie puli watkow, uspienie systemu, cokolwiek.
/// Wynik trafia do kazdego wpisu w `podejrzane.txt`, wiec korelacja jest natychmiastowa:
/// jesli obok zatoru 615 ms stoi "proces stanal 400 ms", to wina jest nasza i nie ma sensu
/// szukac jej w sieci ani w sterowniku.
/// </summary>
public static class CzujnikZastoju
{
    private const int Okres = 100;
    private const double Prog = 250;

    private static System.Threading.Timer _zegar;
    private static long _poprzednieTykniecie;
    private static long _kiedyZastoj;
    private static double _dlugoscZastoju;
    private static int _ileZastojow;

    /// <summary>Wlacza czujnik. Wolane raz, przy starcie programu.</summary>
    public static void Uruchom()
    {
        if (_zegar != null) return;
        Volatile.Write(ref _poprzednieTykniecie, DateTime.UtcNow.Ticks);
        _zegar = new System.Threading.Timer(_ => Tyknij(), null, Okres, Okres);
    }

    private static void Tyknij()
    {
        long teraz = DateTime.UtcNow.Ticks;
        long poprzednie = Interlocked.Exchange(ref _poprzednieTykniecie, teraz);
        double minelo = TimeSpan.FromTicks(teraz - poprzednie).TotalMilliseconds;

        if (minelo < Prog) return;

        Volatile.Write(ref _kiedyZastoj, teraz);
        Volatile.Write(ref _dlugoscZastoju, minelo - Okres);
        Interlocked.Increment(ref _ileZastojow);
    }

    /// <summary>
    /// Opis ostatniego zastoju albo pusty tekst. Podajemy **jak dawno**, bo to jest jedyna
    /// liczba, ktora pozwala powiazac zastoj z konkretna spozniona odpowiedzia.
    /// </summary>
    public static string Opis
    {
        get
        {
            // **Liczniki odsmiecania ida do kazdego wpisu.** Zastoj i odsmiecanie to dwie
            // rozne rzeczy, ktore wygladaja tak samo z zewnatrz; dopiero zestawione obok
            // siebie mowia, czy warto bylo oszczedzac pamiec. Patrz Optymalizacje.
            string odsmiecanie = ", odsmiecen: " + GC.CollectionCount(0) + " / " +
                                 GC.CollectionCount(1) + " / " + GC.CollectionCount(2) +
                                 (Optymalizacje.OszczedzajBufory ? ", oszczedzanie wlaczone"
                                                                 : ", oszczedzanie wylaczone");

            long kiedy = Volatile.Read(ref _kiedyZastoj);
            if (kiedy == 0) return "proces nie stanal ani razu" + odsmiecanie;

            double temu = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - kiedy).TotalSeconds;
            return "ostatni zastoj procesu: " + Volatile.Read(ref _dlugoscZastoju).ToString("0") +
                   " ms, " + temu.ToString("0.0") + " s temu (razem " +
                   Volatile.Read(ref _ileZastojow) + ")" + odsmiecanie;
        }
    }
}
