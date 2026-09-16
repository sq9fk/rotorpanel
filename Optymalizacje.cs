namespace RotorPanel;

/// <summary>
/// Przelacznik proby: **mniej alokacji w goracej sciezce mostka**.
///
/// **Po co przelacznik, skoro to zwykla optymalizacja.** Bo to nie jest naprawa czegos, co
/// zmierzylismy i zrozumieli, tylko **hipoteza poparta korelacja**. Stan na 16 wrzesnia po
/// 1.11.49: zapis na port idzie w 0 ms, czekanie na uchwyt 0-24 ms, nic nie jest porzucane -
/// a mimo to kawalek potrafi przeczekac w kolejce 221-885 ms. Przy najdluzszym z nich zastoj
/// procesu wypadl **0,0 s wczesniej**, wiec podejrzany numer jeden to odsmiecanie.
///
/// Podejrzany, nie sprawca. Dlatego zmiana ma wylacznik w Ustawieniach i mozna ja cofnac
/// bez nowego wydania, a do wpisow pulapki dochodza liczniki odsmiecen - zeby nastepny plik
/// powiedzial, czy w ogole ruszylo.
///
/// Co oszczedzamy:
/// - odczyt z portu pisze **wprost do bufora pompy**, zamiast do kopii posredniej
///   (1 kB na kazdy odczyt, okolo czterdziestu odczytow na sekunde na mostek),
/// - zapis na port oddaje **ten sam bufor**, gdy idzie w calosci i od poczatku,
/// - dziennik pulapki przy wzmacniaczu **odklada formatowanie** - zamiast skladac napis
///   przy kazdym kawalku, kopiuje sobie sesnascie bajtow i zamienia je na tekst dopiero
///   wtedy, gdy naprawde powstaje wpis. Przy rotorach zostaje po staremu, bo tam do opisu
///   potrzebny jest **caly** kawalek (rozbior ramek SPID) i kopia bylaby drozsza niz napis.
/// </summary>
public static class Optymalizacje
{
    /// <summary>
    /// Czy oszczedzac bufory i odkladac formatowanie dziennika. Domyslnie wlaczone; wylacznik
    /// stoi w Ustawieniach i dziala **od razu**, bez ponownego uruchamiania programu.
    /// </summary>
    public static volatile bool OszczedzajBufory = true;
}
