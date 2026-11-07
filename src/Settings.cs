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
    // ------------------------------------------------------------------ Instellingen (instellingen.ini in %APPDATA%\NexusPen)
    // Elk publiek veld wordt automatisch bewaard en geladen onder zijn eigen naam.
    class Settings
    {
        // werkbalk
        public int ToolbarX = -1, ToolbarY = -1, TileX = -1, TileY = -1;
        public int ThemeIndex = 0, SizeIndex = 1, OpacityIndex = 0;
        public Color CustomAccent = ColorTranslator.FromHtml("#A3E635");
        public bool Vertical, Square, HideFromCapture;
        public string BarLayout = "";          // leeg = standaardvolgorde
        public int ShortenAt = 0;               // 0 = niet ingekort, anders het aantal knoppen voor het menu met het getal
        // tekenen
        public int ColorIndex = 2, PaletteIndex = 0;   // 2 = rood
        public string ColorsFel = "", ColorsZacht = "";   // eigen kleurenlijst (#RRGGBB,...); leeg = de 16 standaardkleuren
        public bool PaletteV2 = true;                     // false = bewaard met de oude 10 kleuren (tot 1.2.9)
        public float Width = 4;
        public Tool Tool = Tool.Pen;
        public bool WheelWidth = true, PerToolWidth = true, Chalk, Fade;
        public string ToolWidths = "";
        public float FocusRadius = 160;
        public int FocusShape = 0;              // spotlicht: 0 rond, 1 rechthoek (om zinnen te lezen)
        public int FadeSeconds = 5;             // vervagen na 3, 5, 7 of 12 seconden
        public float StampPx = 0;               // eigen stempelgrootte (muiswiel); 0 = S, M of L
        public int BoardScale = 1;              // (oud, niet meer gebruikt)
        public float BoardZoom = 1;             // lijnen en ruitjes: van 0,5 (veel kleine regels) tot 6 (een grote regel)
        public float LoupeZoom = 2f;            // vergrootglas: 1,5 tot 6 keer
        public int LoupeShape = 0;              // 0 rechthoek (om te lezen), 1 rond
        public bool Buttons130;                 // eenmalig: timer en vergrootglas in een eigen balk zetten (vanaf 1.3.0)
        public string ShotFolder = "";          // eigen map voor schermafbeeldingen; leeg = Afbeeldingen\NexusPen
        public int LaserMode = 0;               // laser: 0 snel weg, 1 langzaam, 2 blijft staan tot een nieuwe lijn of Esc
        public int ActiveStyle = 0;             // gekozen knop: 0 rustig (ingedrukt), 1 in de accentkleur
        public bool ChalkChecked;               // eenmalige controle bij het bijwerken naar 1.2.6
        // tekst
        public string FontName = "Andika";
        public bool TextOutline = true;
        public int OutlineWidth = 1, OutlineColor = 0;   // dun, middel, dik / zwart, wit, automatisch
        public float TextSize = 36;
        // stempels en bord
        public int StampKind = 0, StampSize = 1, StampNext = 1, BoardBg = 0;
        // algemeen
        public string Language = "nl";
        public bool ShowInTaskbar = true, UpdateCheck = true;   // ShowInTaskbar: oud, niet meer gebruikt
        public bool InTaakbalk = false;         // als programma in de taakbalk; standaard alleen het icoon bij de klok
        public int KeepDrawing = 1;             // nooit, na Opslaan, altijd
        // sneltoetsen
        public string HotkeyDraw = "Ctrl+Shift+D", HotkeyHide = "Ctrl+Shift+H";
        public int ShortcutMode = 0;            // Ctrl+Shift + letter, alleen een letter, uit
        public string ShortcutOverrides = "";   // actie=combinatie;actie=combinatie
        // gebruik
        public bool ShowTips = false;           // uitleg tonen als je met de muis over een knop gaat
        public bool Usage = true;
        public int UsageCats = 127;

        public const float MaxWidth = 68;
        public static readonly int[] FadeChoices = { 3, 5, 7, 12 };
        public static readonly float[] BoardScales = { 0.75f, 1f, 1.4f, 2f };
        public const float MinZoom = 0.5f, MaxZoom = 6f;

        public static bool ParseHotkey(string text, out int mods, out int vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim().ToLowerInvariant();
                if (part == "ctrl" || part == "control") mods |= W.MOD_CONTROL;
                else if (part == "shift") mods |= W.MOD_SHIFT;
                else if (part == "alt") mods |= 1;
                else if (part == "win") mods |= 8;
                else if (part.Length > 0)
                {
                    if (part.Length == 1 && char.IsDigit(part[0])) vk = (int)Keys.D0 + (part[0] - '0');   // "5" is de toets 5, niet Keys-nummer 5
                    else { try { vk = (int)(Keys)Enum.Parse(typeof(Keys), part, true); } catch { return false; } }
                }
            }
            return vk != 0;
        }

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen", "instellingen.ini"); }
        }

        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        // namen uit versie 1.1
        static readonly Dictionary<string, string> OldNames = new Dictionary<string, string> {
            { "toolbarX", "ToolbarX" }, { "toolbarY", "ToolbarY" }, { "kleur", "ColorIndex" }, { "palet", "PaletteIndex" },
            { "dikte", "Width" }, { "spotlicht", "FocusRadius" }, { "gereedschap", "Tool" }, { "thema", "ThemeIndex" },
            { "eigenKleur", "CustomAccent" }, { "grootte", "SizeIndex" }, { "doorzichtig", "OpacityIndex" }, { "staand", "Vertical" },
            { "strak", "Square" }, { "verbergenBijDelen", "HideFromCapture" }, { "tekstRand", "TextOutline" }, { "lettertype", "FontName" },
            { "sneltoetsTekenen", "HotkeyDraw" }, { "sneltoetsVerbergen", "HotkeyHide" } };

        static string Write(object v)
        {
            if (v is Color) return ColorTranslator.ToHtml((Color)v);
            if (v is bool) return (bool)v ? "1" : "0";
            if (v is float) return ((float)v).ToString(IC);
            return v == null ? "" : v.ToString();
        }

        static bool Read(System.Reflection.FieldInfo f, string v, Settings s)
        {
            Type t = f.FieldType;
            try
            {
                if (t == typeof(int)) { int n; if (!int.TryParse(v, NumberStyles.Integer, IC, out n)) return false; f.SetValue(s, n); }
                else if (t == typeof(float)) { float n; if (!float.TryParse(v, NumberStyles.Float, IC, out n)) return false; f.SetValue(s, n); }
                else if (t == typeof(bool)) f.SetValue(s, v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (t == typeof(string)) f.SetValue(s, v);
                else if (t == typeof(Color)) f.SetValue(s, ColorTranslator.FromHtml(v));
                else if (t.IsEnum) f.SetValue(s, Enum.Parse(t, v));
                return true;
            }
            catch { return false; }
        }

        static System.Reflection.FieldInfo[] Fields { get { return typeof(Settings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance); } }

        public static Settings Load() { return Load(FilePath); }

        public static Settings Load(string path)
        {
            Settings s = new Settings();
            bool isOld = false, hasV2 = false, existed = false;
            try
            {
                if (File.Exists(path))
                {
                    existed = true;
                    Dictionary<string, System.Reflection.FieldInfo> byName = new Dictionary<string, System.Reflection.FieldInfo>();
                    foreach (System.Reflection.FieldInfo f in Fields) byName[f.Name] = f;
                    foreach (string line in File.ReadAllLines(path))
                    {
                        int i = line.IndexOf('=');
                        if (i < 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        string mapped;
                        if (OldNames.TryGetValue(k, out mapped)) { k = mapped; isOld = true; }
                        if (k == "PaletteV2") hasV2 = true;
                        System.Reflection.FieldInfo f;
                        if (byName.TryGetValue(k, out f)) Read(f, v, s);
                    }
                }
            }
            catch { }
            if (isOld && s.FontName == "") s.FontName = "Andika";
            // van 10 naar 16 kleuren: je gekozen kleur blijft dezelfde kleur
            if (existed && !hasV2) { if (s.ColorIndex >= 0 && s.ColorIndex < Palette.OldToNew.Length) s.ColorIndex = Palette.OldToNew[s.ColorIndex]; s.PaletteV2 = true; }
            s.Clamp();
            return s;
        }

        public void Clamp()
        {
            ThemeIndex = Math.Max(0, Math.Min(Theme.All.Length - 1, ThemeIndex));
            SizeIndex = Math.Max(0, Math.Min(2, SizeIndex));
            OpacityIndex = Math.Max(0, Math.Min(2, OpacityIndex));
            PaletteIndex = Math.Max(0, Math.Min(Palette.Sets.Length - 1, PaletteIndex));
            ColorIndex = Math.Max(0, Math.Min(Palette.MaxColors - 1, ColorIndex));
            if (ShotFolder == null) ShotFolder = "";
            if (ColorsFel == null) ColorsFel = ""; if (ColorsZacht == null) ColorsZacht = "";
            if (float.IsNaN(Width) || float.IsInfinity(Width)) Width = 4;
            if (float.IsNaN(TextSize) || float.IsInfinity(TextSize)) TextSize = 36;
            if (!Enum.IsDefined(typeof(Tool), Tool)) Tool = Tool.Pen;
            Width = Math.Max(1, Math.Min(MaxWidth, Width));
            FocusShape = Math.Max(0, Math.Min(1, FocusShape));
            ActiveStyle = Math.Max(0, Math.Min(1, ActiveStyle));
            LaserMode = Math.Max(0, Math.Min(2, LaserMode));
            if (float.IsNaN(LoupeZoom) || float.IsInfinity(LoupeZoom)) LoupeZoom = 2f;
            LoupeZoom = Math.Max(1.5f, Math.Min(6f, LoupeZoom));
            LoupeShape = Math.Max(0, Math.Min(1, LoupeShape));
            if (FadeSeconds != 3 && FadeSeconds != 5 && FadeSeconds != 7 && FadeSeconds != 12) FadeSeconds = 5;
            if (float.IsNaN(StampPx) || StampPx < 0) StampPx = 0;
            StampPx = Math.Min(200, StampPx);
            BoardScale = Math.Max(0, Math.Min(3, BoardScale));
            if (float.IsNaN(BoardZoom) || float.IsInfinity(BoardZoom)) BoardZoom = 1;
            if (BoardScale != 1 && Math.Abs(BoardZoom - 1) < 0.01f) { BoardZoom = BoardScales[BoardScale]; BoardScale = 1; }   // oude keuze overnemen
            BoardZoom = Math.Max(MinZoom, Math.Min(MaxZoom, BoardZoom));
            if (float.IsNaN(FocusRadius)) FocusRadius = 200;
            FocusRadius = Math.Max(60, Math.Min(700, FocusRadius));
            OutlineWidth = Math.Max(0, Math.Min(2, OutlineWidth));
            OutlineColor = Math.Max(0, Math.Min(2, OutlineColor));
            TextSize = Math.Max(12, Math.Min(300, TextSize));
            StampKind = Math.Max(0, Math.Min(Render.StampKinds.Length - 1, StampKind));
            StampSize = Math.Max(0, Math.Min(2, StampSize));
            StampNext = Math.Max(1, Math.Min(999, StampNext));
            BoardBg = Math.Max(0, Math.Min(3, BoardBg));
            KeepDrawing = Math.Max(0, Math.Min(2, KeepDrawing));
            ShortcutMode = Math.Max(0, Math.Min(2, ShortcutMode));
            ShortenAt = Math.Max(0, ShortenAt);
            if (Tool == Tool.Focus || Tool == Tool.Select || Tool == Tool.Mouse || Tool == Tool.Loupe) Tool = Tool.Pen;
            if (Array.IndexOf(L.Codes, Language) < 0) Language = "nl";
        }

        public void Save() { Save(FilePath); }

        public void Save(string path) { TrySave(path); }

        // Bewaren en zeggen of het lukte (voor exporteren naar een stick of een andere map)
        public bool TrySave(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                List<string> lines = new List<string>();
                lines.Add("# NexusPen instellingen");
                foreach (System.Reflection.FieldInfo f in Fields) lines.Add(f.Name + "=" + Write(f.GetValue(this)));
                string tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray(), Encoding.UTF8);
                if (File.Exists(path)) { try { File.Replace(tmp, path, null); } catch { File.Copy(tmp, path, true); File.Delete(tmp); } }
                else File.Move(tmp, path);
                return File.Exists(path);
            }
            catch (Exception ex) { Log.Error("Instellingen bewaren mislukt", ex); return false; }
        }

        // Is dit echt een bestand met NexusPen-instellingen? (kopregel en genoeg bekende namen)
        public static bool LooksLikeSettings(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || !lines[0].StartsWith("# NexusPen")) return false;
                Dictionary<string, bool> known = new Dictionary<string, bool>();
                foreach (System.Reflection.FieldInfo f in Fields) known[f.Name] = true;
                int hits = 0;
                foreach (string l in lines) { int i = l.IndexOf('='); if (i > 0 && known.ContainsKey(l.Substring(0, i).Trim())) hits++; }
                return hits >= 10;
            }
            catch { return false; }
        }

        // ---- breedte per gereedschap
        public float WidthFor(Tool t, float fallback)
        {
            foreach (string part in ToolWidths.Split(';'))
            {
                int i = part.IndexOf('=');
                float f;
                if (i > 0 && part.Substring(0, i) == t.ToString() && float.TryParse(part.Substring(i + 1), NumberStyles.Float, IC, out f) && !float.IsNaN(f)) return Math.Max(1, Math.Min(MaxWidth, f));
            }
            if (t == Tool.Marker) return Math.Max(fallback, 6);
            return fallback;
        }

        public void SetWidthFor(Tool t, float w)
        {
            List<string> keep = new List<string>();
            foreach (string part in ToolWidths.Split(';'))
                if (part.Length > 0 && !part.StartsWith(t + "=")) keep.Add(part);
            keep.Add(t + "=" + w.ToString(IC));
            ToolWidths = string.Join(";", keep.ToArray());
        }
    }
}
