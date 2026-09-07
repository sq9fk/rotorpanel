using System.Globalization;
using System.Text;

namespace RotorPanel;

/// <summary>
/// Obiekt JSON z zachowana kolejnoscia pol - potrzebny przy zapisie, zeby plik
/// konfiguracyjny wygladal zawsze tak samo.
/// </summary>
public sealed class JsonObiekt : List<KeyValuePair<string, object>>
{
    public void Dodaj(string klucz, object wartosc)
        => Add(new KeyValuePair<string, object>(klucz, wartosc));
}

/// <summary>
/// Minimalny czytnik i zapisywacz JSON-a. Wystarcza na plaski plik konfiguracyjny,
/// a pozwala obejsc sie bez System.Text.Json, ktorego .NET Framework nie ma w sobie.
/// Dzieki temu program jest jednym plikiem exe, bez zadnych bibliotek obok.
/// </summary>
public static partial class Json
{
    /// <summary>
    /// Ile poziomow zagnizdzenia przepuszczamy. Bez tego limitu wejscie w rodzaju
    /// "[[[[..." konczy sie przepelnieniem stosu, a **StackOverflowException w .NET
    /// jest nieprzechwytywalny** - proces ginie natychmiast, bez zadnego komunikatu,
    /// wiec nawet obudowa "Blad konfiguracji" w Program.Main by sie nie odezwala.
    /// Plik konfiguracyjny jest edytowalny recznie, a odpowiedz z API GitHuba
    /// przychodzi z sieci, wiec obie drogi warto zamknac.
    /// </summary>
    private const int MaksymalneZagniezdzenie = 64;

    public static object Parsuj(string tekst)
    {
        int i = 0;
        object wynik = Wartosc(tekst, ref i, 0);
        PomijBiale(tekst, ref i);
        return wynik;
    }

    private static void PomijBiale(string t, ref int i)
    {
        while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
    }

    private static object Wartosc(string t, ref int i, int glebokosc)
    {
        PomijBiale(t, ref i);
        if (i >= t.Length) throw new FormatException("Nieoczekiwany koniec danych.");
        if (glebokosc > MaksymalneZagniezdzenie)
            throw new FormatException("Za gleboko zagniezdzony JSON.");

        switch (t[i])
        {
            case '{': return Obiekt(t, ref i, glebokosc + 1);
            case '[': return Tablica(t, ref i, glebokosc + 1);
            case '"': return Napis(t, ref i);
            default:  return Prosta(t, ref i);
        }
    }

    private static Dictionary<string, object> Obiekt(string t, ref int i, int glebokosc)
    {
        var wynik = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        i++;
        PomijBiale(t, ref i);
        if (i < t.Length && t[i] == '}') { i++; return wynik; }

        while (i < t.Length)
        {
            PomijBiale(t, ref i);
            string klucz = Napis(t, ref i);
            PomijBiale(t, ref i);
            if (i >= t.Length || t[i] != ':') throw new FormatException("Brak dwukropka po kluczu " + klucz);
            i++;
            wynik[klucz] = Wartosc(t, ref i, glebokosc);
            PomijBiale(t, ref i);

            if (i < t.Length && t[i] == ',') { i++; continue; }
            if (i < t.Length && t[i] == '}') { i++; return wynik; }
            throw new FormatException("Nieprawidlowy obiekt JSON.");
        }
        throw new FormatException("Niedomkniety obiekt JSON.");
    }

    private static List<object> Tablica(string t, ref int i, int glebokosc)
    {
        var wynik = new List<object>();
        i++;
        PomijBiale(t, ref i);
        if (i < t.Length && t[i] == ']') { i++; return wynik; }

        while (i < t.Length)
        {
            wynik.Add(Wartosc(t, ref i, glebokosc));
            PomijBiale(t, ref i);

            if (i < t.Length && t[i] == ',') { i++; continue; }
            if (i < t.Length && t[i] == ']') { i++; return wynik; }
            throw new FormatException("Nieprawidlowa tablica JSON.");
        }
        throw new FormatException("Niedomknieta tablica JSON.");
    }

    private static string Napis(string t, ref int i)
    {
        if (i >= t.Length || t[i] != '"') throw new FormatException("Oczekiwano napisu.");
        i++;
        var sb = new StringBuilder();

        while (i < t.Length)
        {
            char z = t[i++];
            if (z == '"') return sb.ToString();

            if (z != Ukosnik) { sb.Append(z); continue; }
            if (i >= t.Length) break;

            char e = t[i++];
            switch (e)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > t.Length) throw new FormatException("Uciety kod znaku.");
                    sb.Append((char)Convert.ToInt32(t.Substring(i, 4), 16));
                    i += 4;
                    break;
                default:  sb.Append(e); break;
            }
        }
        throw new FormatException("Niedomkniety napis JSON.");
    }

    private static object Prosta(string t, ref int i)
    {
        int start = i;
        while (i < t.Length && t[i] != ',' && t[i] != '}' && t[i] != ']' && !char.IsWhiteSpace(t[i])) i++;
        string s = t.Substring(start, i - start);

        if (s.Equals("true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Equals("null",  StringComparison.OrdinalIgnoreCase)) return null;

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double liczba))
            return liczba;

        throw new FormatException("Nieznana wartosc: " + s);
    }
}
