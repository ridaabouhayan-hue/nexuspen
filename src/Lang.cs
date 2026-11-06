using System;
using System.Collections.Generic;

namespace NexusPen
{
    // ------------------------------------------------------------------ Talen
    // Alle teksten staan in de code in het Nederlands, als L.T("...").
    // De vertalingen staan in LangData.cs: per Nederlandse tekst de Engelse, Franse en Spaanse versie.
    static class L
    {
        public static readonly string[] Codes = { "nl", "en", "fr", "es" };
        public static readonly string[] Names = { "Nederlands", "English", "Français", "Español" };
        public static string Code = "nl";
        public static event Action Changed;

        static Dictionary<string, string[]> table;

        static void Ensure()
        {
            if (table != null) return;
            table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string[] row in LangData.Rows)
                if (row.Length == 4 && !table.ContainsKey(row[0])) table[row[0]] = row;
        }

        public static void Set(string code)
        {
            if (Array.IndexOf(Codes, code) < 0) code = "nl";
            if (code == Code) return;
            Code = code;
            if (Changed != null) Changed();
        }

        public static string T(string nl)
        {
            if (Code == "nl" || string.IsNullOrEmpty(nl)) return nl;
            Ensure();
            string[] row;
            if (!table.TryGetValue(nl, out row)) return nl;
            int i = Array.IndexOf(Codes, Code);
            return i > 0 && i < row.Length && !string.IsNullOrEmpty(row[i]) ? row[i] : nl;
        }

        // Met invulplekken: L.F("Pagina {0} van {1}", 2, 3)
        public static string F(string nl, params object[] args) { return string.Format(T(nl), args); }

        public static bool HasTranslation(string nl) { Ensure(); return table.ContainsKey(nl); }

        public static string WindowsLanguage()
        {
            string two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Array.IndexOf(Codes, two) >= 0 ? two : "nl";
        }
    }
}
