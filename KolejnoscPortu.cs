namespace RotorPanel;

/// <summary>
/// Rozstrzyga, kto w danej chwili trzyma uchwyt portu: odczyt czy zapis - **z pierwszenstwem
/// dla zapisu**.
///
/// **Skad to sie wzielo.** Port otwieramy uchwytem synchronicznym, a Windows **szereguje
/// operacje na takim uchwycie**: dopoki trwa `ReadFile`, `WriteFile` czeka. Pompa odczytu
/// chodzi w ciasnej petli z limitem 25 ms na odczyt, wiec zaraz po kazdym powrocie wydaje
/// kolejne zadanie. Pisarz siedzi na innym watku puli i musi sie w te petle **wcisnac**;
/// gdy przegrywa wyscig kilkadziesiat razy z rzedu, jego zapis stoi sekundami.
///
/// Zmierzone 15 wrzesnia na zywym SPE Term, juz po powiekszeniu buforow do 64 kB
/// (`zapisow niepelnych 0`, wiec **bufor nie byl pelny**):
///
/// | zapis | czas | tempo |
/// |---|---|---|
/// | 70 B | 1222 ms | 57 B/s |
/// | 56 B | 777 ms | 72 B/s |
/// | 63 B | **6056 ms** | 10 B/s |
///
/// Siedemdziesiat bajtow przez sekunde na **wirtualnej** parze portow to nie jest transmisja -
/// to czekanie na uchwyt. I to nie jest dlawienie ze stalym tempem: 57, 72 i 10 bajtow
/// na sekunde to **wyscig o zasob**, a nie emulowana predkosc portu, ktora dawalaby
/// za kazdym razem tyle samo.
///
/// Rozwiazanie docelowe to uchwyt nakladkowy (`FILE_FLAG_OVERLAPPED`), przy ktorym odczyt
/// i zapis w ogole sobie nie przeszkadzaja. To jest przebudowa warstwy wejscia-wyjscia
/// wszystkich mostkow, wiec najpierw robimy rzecz mala i sprawdzalna: **skoro system i tak
/// szereguje, szeregujmy sami - uczciwie**. Odczyt ustepuje, gdy ktos czeka z zapisem,
/// wiec zapis czeka najwyzej jeden limit odczytu zamiast kilkudziesieciu.
/// </summary>
public sealed class KolejnoscPortu
{
    private readonly object _uchwyt = new();
    private int _zapisyCzekaja;

    /// <summary>Ile zapisow czeka w tej chwili na uchwyt - do sladu i do testu.</summary>
    public int ZapisyCzekaja => Volatile.Read(ref _zapisyCzekaja);

    /// <summary>
    /// Wykonuje odczyt. **Ustepuje zapisom**: dopoki ktorys czeka, nie wchodzimy po uchwyt.
    /// Czekamy krotkimi krokami, bo zapis trwa milisekundy - nie ma po co budowac tu
    /// kolejki ze zdarzeniami.
    /// </summary>
    public T Odczyt<T>(Func<T> co)
    {
        while (Volatile.Read(ref _zapisyCzekaja) > 0) Thread.Sleep(1);
        lock (_uchwyt) return co();
    }

    /// <summary>
    /// Wykonuje zapis. Zglasza sie **przed** wejsciem po uchwyt, zeby trwajacy odczyt
    /// byl ostatnim, na ktory czekamy.
    /// </summary>
    public void Zapis(Action co)
    {
        Interlocked.Increment(ref _zapisyCzekaja);
        try
        {
            lock (_uchwyt) co();
        }
        finally { Interlocked.Decrement(ref _zapisyCzekaja); }
    }
}
