using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexusPen
{
    // ------------------------------------------------------------------ Thema's (gelijk aan het ontwerpcanvas)
    class Theme
    {
        public string Name, Desc;
        public Color Bar, Border, Icon, Sep, Chip, Accent, OnAccent;
        public bool Thick;

        static Color H(string hex) { return ColorTranslator.FromHtml(hex); }

        static Theme T(string name, string desc, string bar, string border, string icon, string sep, string chip, string accent, string onAccent, bool thick)
        {
            Theme t = new Theme();
            t.Name = name; t.Desc = desc; t.Bar = H(bar); t.Border = H(border); t.Icon = H(icon); t.Sep = H(sep);
            t.Chip = H(chip); t.Accent = H(accent); t.OnAccent = H(onAccent); t.Thick = thick;
            return t;
        }

        public static readonly Theme[] All = {
            T("Nexus", "Huisstijl. Donker met NOL-oranje", "#1C1C1C", "#303030", "#FFFFFF", "#303030", "#262626", "#FF8000", "#1A0F00", false),
            T("Nexus licht", "Huisstijl. Wit met NOL-oranje", "#FFFFFF", "#E5E5E5", "#000000", "#E5E5E5", "#F5F5F5", "#FF8000", "#1A0F00", false),
            T("Limoen", "Grafiet met fris groen", "#18181B", "#27272A", "#D4D4D8", "#3F3F46", "#27272A", "#A3E635", "#111407", false),
            T("Oceaan", "Donkerblauw met hemelsblauw", "#0F1B2A", "#1E3247", "#CFE3F5", "#24405C", "#1A2C40", "#38BDF8", "#04131F", false),
            T("Zonsondergang", "Warm donker met oranje", "#1F1612", "#3A2A22", "#F1DDD0", "#4A372D", "#2E211B", "#FB923C", "#1F0E03", false),
            T("Framboos", "Donkerpaars met roze", "#1C1420", "#34253B", "#EADCF0", "#45324E", "#2A1E30", "#F472B6", "#26061A", false),
            T("Krijtbord", "Bordgroen met krijtgeel", "#1B2E26", "#2E4A3E", "#E8F0E8", "#3B5A4C", "#253D33", "#F7D774", "#1B1405", false),
            T("Licht", "Wit met blauw", "#FFFFFF", "#D4D4D8", "#3F3F46", "#E4E4E7", "#F4F4F5", "#2563EB", "#FFFFFF", false),
            T("Hoog contrast", "Zwart, wit en geel", "#000000", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#1A1A1A", "#FFE600", "#000000", true),
            T("Eigen", "Je eigen accentkleur", "#18181B", "#27272A", "#D4D4D8", "#3F3F46", "#27272A", "#A3E635", "#111407", false),
            // rustig: geen kleur, alleen wit of zwart met grijs (nieuw achteraan, dan blijven oude keuzes kloppen)
            T("Rustig wit", "Gewoon wit en grijs, valt niet op", "#FFFFFF", "#E4E4E7", "#3F3F46", "#E4E4E7", "#F4F4F5", "#52525B", "#FFFFFF", false),
            T("Rustig zwart", "Gewoon zwart en grijs, valt niet op", "#121212", "#262626", "#D4D4D4", "#2E2E2E", "#1E1E1E", "#A3A3A3", "#121212", false)
        };

        // volgorde op de pagina Uiterlijk: de rustige bij de huisstijl, Eigen als laatste
        public static readonly int[] Order = { 0, 1, 10, 11, 2, 3, 4, 5, 6, 7, 8, 9 };

        public const int Custom = 9;

        public static Theme Get(Settings s)
        {
            int i = Math.Max(0, Math.Min(All.Length - 1, s.ThemeIndex));
            Theme t = All[i];
            if (i == Custom)
            {
                Theme c = (Theme)t.MemberwiseClone();
                c.Accent = s.CustomAccent;
                c.OnAccent = Luma(s.CustomAccent) > 0.55 ? H("#111407") : Color.White;
                return c;
            }
            return t;
        }

        public static double Luma(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }
        public bool IsLight { get { return Luma(Bar) > 0.6; } }
        public Color Muted { get { return IsLight ? H("#71717A") : H("#A1A1AA"); } }
        public Color Hover { get { return IsLight ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255); } }
        // de gekozen knop in de rustige stand: alsof hij ingedrukt is
        // duidelijk anders dan Hover, zodat 'muis erover' niet op 'gekozen' lijkt
        public Color Pressed { get { return IsLight ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(60, 255, 255, 255); } }
        public Color PressedEdge { get { return IsLight ? Color.FromArgb(55, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255); } }
        public Color Text { get { return IsLight ? Color.Black : Color.White; } }
    }

    // ------------------------------------------------------------------ Kleuren: fel en zacht
    // 16 standaardkleuren per set, van meest naar minst gebruikt. In Instellingen kun je kleuren weghalen,
    // toevoegen en in je eigen volgorde zetten; dat wordt per set bewaard als een rij #RRGGBB-codes.
    static class Palette
    {
        public static readonly string[] Names = { "Fel", "Zacht" }; // vertaal
        public static readonly Color[][] Sets = {
            new Color[] { C("#111111"), C("#2962FF"), C("#FF1F3D"), C("#00C853"), C("#FFD400"), C("#FF7A00"), C("#AA00FF"), C("#FF2D95"),
                          C("#8D5524"), C("#8E8E93"), C("#FFFFFF"), C("#00B8FF"), C("#00BFA5"), C("#1B5E20"), C("#0D2C6C"), C("#8E1B3A") },
            new Color[] { C("#5F6368"), C("#8AB4F8"), C("#F28B82"), C("#A8D5A2"), C("#FFE08A"), C("#F6B26B"), C("#C3A6F0"), C("#F7A8C4"),
                          C("#C8A27A"), C("#BDBDBD"), C("#F4F1E8"), C("#9FD3F2"), C("#8FD9CF"), C("#6B9E6E"), C("#6C86B8"), C("#C17A8A") }
        };
        public static readonly string[][] Labels = {
            new string[] { "Zwart", "Blauw", "Rood", "Groen", "Geel", "Oranje", "Paars", "Roze", "Bruin", "Grijs", "Wit", "Lichtblauw", "Turquoise", "Donkergroen", "Donkerblauw", "Bordeaux" }, // vertaal
            new string[] { "Donkergrijs", "Zacht blauw", "Zacht rood", "Zacht groen", "Zacht geel", "Zacht oranje", "Zacht paars", "Zacht roze", "Zacht bruin", "Lichtgrijs", "Krijtwit", "Zacht lichtblauw", "Zacht turquoise", "Zacht donkergroen", "Zacht donkerblauw", "Zacht bordeaux" } // vertaal
        };
        public const int MaxColors = 24;

        // De oude volgorde (tot 1.2.9) naar de nieuwe: rood, oranje, geel, groen, lichtblauw, blauw, paars, roze, zwart, wit
        public static readonly int[] OldToNew = { 2, 5, 4, 3, 11, 1, 6, 7, 0, 10 };

        static Color C(string h) { return ColorTranslator.FromHtml(h); }

        static string cacheKey; static Color[] cacheColors;

        // De kleuren die je nu gebruikt in deze set (eigen lijst of de standaard)
        public static Color[] Get(Settings s, int set)
        {
            set = Math.Max(0, Math.Min(Sets.Length - 1, set));
            string own = s == null ? "" : set == 0 ? s.ColorsFel : s.ColorsZacht;
            if (string.IsNullOrEmpty(own)) return Sets[set];
            string key = set + "|" + own;
            if (key == cacheKey && cacheColors != null) return cacheColors;
            List<Color> list = new List<Color>();
            foreach (string part in own.Split(','))
            {
                string h = part.Trim();
                if (h.Length != 7 || h[0] != '#') continue;
                int v;
                if (!int.TryParse(h.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) continue;
                list.Add(Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF));
                if (list.Count >= MaxColors) break;
            }
            if (list.Count == 0) return Sets[set];
            cacheKey = key; cacheColors = list.ToArray();
            return cacheColors;
        }

        public static string ToText(IList<Color> colors)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Color c in colors) { if (sb.Length > 0) sb.Append(','); sb.Append('#').Append(c.R.ToString("X2")).Append(c.G.ToString("X2")).Append(c.B.ToString("X2")); }
            return sb.ToString();
        }

        // Voor het gebruikslogboek: altijd de Nederlandse naam (niet afhankelijk van de taal), of "eigen"
        public static string KeyOf(int set, Color c)
        {
            set = Math.Max(0, Math.Min(Sets.Length - 1, set));
            for (int i = 0; i < Sets[set].Length; i++) if (Sets[set][i].ToArgb() == c.ToArgb()) return Labels[set][i];
            return "eigen";
        }

        // Naam van een kleur: de standaardnaam als het een standaardkleur is, anders "Eigen kleur"
        public static string NameOf(int set, Color c)
        {
            set = Math.Max(0, Math.Min(Sets.Length - 1, set));
            for (int i = 0; i < Sets[set].Length; i++) if (Sets[set][i].ToArgb() == c.ToArgb()) return L.T(Labels[set][i]);
            return L.T("Eigen kleur");
        }
    }
}
