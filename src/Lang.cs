using System;
using System.Collections.Generic;

namespace NexusPen
{
    // ------------------------------------------------------------------ Talen
    // In de code staan alle teksten als sleutel in het Nederlands: L.T("..."). Engels is de standaardtaal:
    // ontbreekt een vertaling, dan komt de Engelse tekst. De vertalingen staan in LangData.cs.
    static class L
    {
        // volgorde op de pagina Taal (Engels eerst)
        public static readonly string[] Codes = { "en", "nl", "fr", "es", "pt", "tr", "ru", "ar", "zh" };
        public static readonly string[] Names = { "English", "Nederlands", "Français", "Español", "Português", "Türkçe", "Русский", "العربية", "中文" };
        // volgorde van de kolommen in LangData (de eerste is de Nederlandse sleutel)
        public static readonly string[] Columns = { "nl", "en", "fr", "es", "pt", "tr", "ru", "ar", "zh" };
        public static string Code = "en";
        public static event Action Changed;

        static Dictionary<string, string[]> table;

        static void Ensure()
        {
            if (table != null) return;
            table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string[] row in LangData.Rows)
                if (row.Length == Columns.Length && !table.ContainsKey(row[0])) table[row[0]] = row;
        }

        public static void Set(string code)
        {
            if (Array.IndexOf(Codes, code) < 0) code = "en";
            if (code == Code) return;
            Code = code;
            if (Changed != null) Changed();
        }

        public static string T(string nl) { return TIn(Code, nl); }

        // Een tekst in een bepaalde taal (bijvoorbeeld voor de voorbeelden op de pagina Taal)
        public static string TIn(string code, string nl)
        {
            if (code == "nl" || string.IsNullOrEmpty(nl)) return nl;
            Ensure();
            string[] row;
            if (!table.TryGetValue(nl, out row)) return nl;
            int i = Array.IndexOf(Columns, code);
            if (i > 0 && i < row.Length && !string.IsNullOrEmpty(row[i])) return row[i];
            return !string.IsNullOrEmpty(row[1]) ? row[1] : nl;   // geen vertaling: dan Engels
        }

        // Met invulplekken: L.F("Pagina {0} van {1}", 2, 3)
        public static string F(string nl, params object[] args) { return string.Format(T(nl), args); }

        public static bool HasTranslation(string nl) { Ensure(); return table.ContainsKey(nl); }

        // de taal van Windows, als we die hebben; anders Engels
        public static string WindowsLanguage()
        {
            string two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Array.IndexOf(Codes, two) >= 0 ? two : "en";
        }

        // Talen die van rechts naar links gaan (Arabisch)
        public static bool RightToLeft { get { return Code == "ar"; } }
    }
}
