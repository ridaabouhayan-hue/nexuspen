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
            T("Eigen", "Je eigen accentkleur", "#18181B", "#27272A", "#D4D4D8", "#3F3F46", "#27272A", "#A3E635", "#111407", false)
        };

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
        public Color Text { get { return IsLight ? Color.Black : Color.White; } }
    }

    // ------------------------------------------------------------------ Kleuren: fel en zacht
    static class Palette
    {
        public static readonly string[] Names = { "Fel", "Zacht" }; // vertaal
        public static readonly Color[][] Sets = {
            new Color[] { C("#FF1F3D"), C("#FF7A00"), C("#FFD400"), C("#00C853"), C("#00B8FF"), C("#2962FF"), C("#AA00FF"), C("#FF2D95"), C("#111111"), C("#FFFFFF") },
            new Color[] { C("#F28B82"), C("#F6B26B"), C("#FFE08A"), C("#A8D5A2"), C("#9FD3F2"), C("#8AB4F8"), C("#C3A6F0"), C("#F7A8C4"), C("#5F6368"), C("#F4F1E8") }
        };
        public static readonly string[][] Labels = {
            new string[] { "Rood", "Oranje", "Geel", "Groen", "Lichtblauw", "Blauw", "Paars", "Roze", "Zwart", "Wit" }, // vertaal
            new string[] { "Zacht rood", "Zacht oranje", "Zacht geel", "Zacht groen", "Zacht lichtblauw", "Zacht blauw", "Zacht paars", "Zacht roze", "Grijs", "Krijtwit" } // vertaal
        };
        static Color C(string h) { return ColorTranslator.FromHtml(h); }
    }
}
