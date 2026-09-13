namespace RotorPanel;

/// <summary>
/// Rozbior ramek SPID na potrzeby sladu. **Nic nie filtruje i niczego nie zmienia** -
/// tlumaczy tylko bajty na czytelny opis, zeby w sladzie bylo widac nie "13 B: 57 30 35...",
/// tylko "nastawa 208 st.".
///
/// Powstal, gdy sterownik rotora zaczal dostawac nastawe, ktorej nikt nie wydal, a slad
/// nie pozwalal odpowiedziec na dwa najwazniejsze pytania: z ktorej strony ta ramka
/// przyszla i ktorego mostka dotyczy. Przy jednym mostku dalo sie to wywnioskowac,
/// przy dwoch juz nie.
///
/// Format (patrz README): rozkaz ma 13 bajtow `57 ... 1F 20` (zapytanie) albo
/// `57 ... 2F 20` (nastawa), z cyframi w ASCII; odpowiedz Rot1Prog ma 5 bajtow
/// `57 H1 H2 H3 20` z cyframi surowymi, a azymut = H1*100 + H2*10 + H3 - 360.
/// </summary>
public static class SladSpid
{
    public static string Opis(byte[] dane, int ile)
    {
        var opis = new System.Text.StringBuilder();
        int i = 0;

        while (i < ile)
        {
            if (dane[i] != 0x57)
            {
                // Bajt poza ramka. Takie smieci sa tu najciekawsze, wiec pokazujemy je
                // pojedynczo, razem z wartoscia dziesietna - bledy bitowe latwiej wtedy
                // rozpoznac (0xD0 to 208, a 0x50 to 'P' - roznia sie jednym bitem).
                Dopisz(opis, "poza ramka " + Bajt(dane[i]));
                i++;
                continue;
            }

            if (i + 13 <= ile && dane[i + 12] == 0x20)
            {
                byte rozkaz = dane[i + 11];
                string co = rozkaz == 0x1F ? "zapytanie o pozycje"
                          : rozkaz == 0x2F ? "NASTAWA " + AzymutAscii(dane, i + 1) + " st."
                          : rozkaz == 0x0F ? "stop"
                          : "rozkaz 0x" + rozkaz.ToString("X2");
                Dopisz(opis, co);
                i += 13;
                continue;
            }

            if (i + 5 <= ile && dane[i + 4] == 0x20)
            {
                Dopisz(opis, "odpowiedz " + AzymutSurowy(dane, i + 1) + " st.");
                i += 5;
                continue;
            }

            Dopisz(opis, "URWANA RAMKA (" + (ile - i) + " B)");
            break;
        }

        return opis.ToString();
    }

    /// <summary>Cyfry ASCII rozkazu: wartosc to (az + 360) * rozdzielczosc.</summary>
    private static string AzymutAscii(byte[] d, int od)
    {
        int wartosc = 0;
        for (int k = 0; k < 4; k++)
        {
            int cyfra = d[od + k] - '0';
            if (cyfra < 0 || cyfra > 9) return "? (cyfry " + Bajt(d[od]) + " " + Bajt(d[od + 1]) +
                                               " " + Bajt(d[od + 2]) + " " + Bajt(d[od + 3]) + ")";
            wartosc = wartosc * 10 + cyfra;
        }

        int rozdzielczosc = d[od + 4];
        if (rozdzielczosc <= 0) return "? (rozdzielczosc " + Bajt(d[od + 4]) + ")";
        return (wartosc / rozdzielczosc - 360).ToString();
    }

    /// <summary>Cyfry odpowiedzi sa surowe, nie w ASCII.</summary>
    private static string AzymutSurowy(byte[] d, int od)
        => (d[od] * 100 + d[od + 1] * 10 + d[od + 2] - 360).ToString();

    private static string Bajt(byte b) => "0x" + b.ToString("X2") + "(" + b + ")";

    private static void Dopisz(System.Text.StringBuilder s, string co)
    {
        if (s.Length > 0) s.Append(" | ");
        s.Append(co);
    }
}
