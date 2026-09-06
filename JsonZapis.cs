using System.Globalization;
using System.Text;

namespace RotorPanel;

public static partial class Json
{
    /// <summary>Ukosnik wsteczny z kodu znaku - w zrodle nie ma wtedy sekwencji ucieczki.</summary>
    internal const char Ukosnik = (char)92;

    // ------------------------------------------------------------- odczyt pol

    public static string Tekst(Dictionary<string, object> o, string klucz, string domyslny = "")
        => o != null && o.TryGetValue(klucz, out object w) && w is string s ? s : domyslny;

    public static int Liczba(Dictionary<string, object> o, string klucz, int domyslna = 0)
    {
        if (o == null || !o.TryGetValue(klucz, out object w)) return domyslna;
        if (w is double d) return (int)Math.Round(d);
        if (w is string s && int.TryParse(s, out int i)) return i;
        return domyslna;
    }

    public static bool Flaga(Dictionary<string, object> o, string klucz, bool domyslna = false)
    {
        if (o == null || !o.TryGetValue(klucz, out object w)) return domyslna;
        if (w is bool b) return b;
        if (w is double d) return d != 0;
        return domyslna;
    }

    // ------------------------------------------------------------- zapis

    public static string Zapisz(object wartosc)
    {
        var sb = new StringBuilder();
        Pisz(sb, wartosc, 0);
        sb.Append(Environment.NewLine);
        return sb.ToString();
    }

    private static void Pisz(StringBuilder sb, object w, int poziom)
    {
        string wciecie    = new string(' ', poziom * 2);
        string wciecieWew = new string(' ', (poziom + 1) * 2);

        if (w is null)   { sb.Append("null"); return; }
        if (w is string s) { PiszNapis(sb, s); return; }
        if (w is bool b) { sb.Append(b ? "true" : "false"); return; }
        if (w is int i)  { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
        if (w is double d) { sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture)); return; }

        if (w is JsonObiekt obiekt)
        {
            if (obiekt.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{').Append(Environment.NewLine);
            for (int k = 0; k < obiekt.Count; k++)
            {
                sb.Append(wciecieWew);
                PiszNapis(sb, obiekt[k].Key);
                sb.Append(": ");
                Pisz(sb, obiekt[k].Value, poziom + 1);
                if (k < obiekt.Count - 1) sb.Append(',');
                sb.Append(Environment.NewLine);
            }
            sb.Append(wciecie).Append('}');
            return;
        }

        if (w is System.Collections.IEnumerable lista)
        {
            var elementy = lista.Cast<object>().ToList();
            if (elementy.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[').Append(Environment.NewLine);
            for (int k = 0; k < elementy.Count; k++)
            {
                sb.Append(wciecieWew);
                Pisz(sb, elementy[k], poziom + 1);
                if (k < elementy.Count - 1) sb.Append(',');
                sb.Append(Environment.NewLine);
            }
            sb.Append(wciecie).Append(']');
            return;
        }

        PiszNapis(sb, Convert.ToString(w, CultureInfo.InvariantCulture));
    }

    private static void PiszNapis(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char z in s ?? "")
        {
            if (z == '"')          sb.Append(Ukosnik).Append('"');
            else if (z == Ukosnik) sb.Append(Ukosnik).Append(Ukosnik);
            else if (z == '\n')    sb.Append(Ukosnik).Append('n');
            else if (z == '\r')    sb.Append(Ukosnik).Append('r');
            else if (z == '\t')    sb.Append(Ukosnik).Append('t');
            else if (z < ' ')      sb.Append(Ukosnik).Append('u').Append(((int)z).ToString("x4"));
            else                   sb.Append(z);
        }
        sb.Append('"');
    }
}
