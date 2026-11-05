// NexusPen - lichtgewicht tekenprogramma over je scherm (alternatief voor Epic Pen)
// Gemaakt voor Nexus of Language. Geschreven in C# 5 zodat het compileert met de
// compiler die standaard in Windows zit (.NET Framework 4.x). Zie build.bat.
// Let op: alleen C# 5 gebruiken en alleen API's die in .NET Framework 4.8 bestaan.

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

[assembly: System.Reflection.AssemblyTitle("NexusPen")]
[assembly: System.Reflection.AssemblyProduct("NexusPen")]
[assembly: System.Reflection.AssemblyCompany("Nexus of Language")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]

namespace NexusPen
{
    enum Tool { Mouse, Pen, Marker, Laser, Line, Arrow, Rect, Ellipse, Text, Eraser, Select, Stamp, Focus }
    enum Board { None, White, Black }

    class Stroke
    {
        public Tool Tool;
        public Color Color;
        public float Width;
        public List<PointF> Pts = new List<PointF>();
        public string Text;
        public float FontSize;
        public DateTime Born;
        public string FontName;      // lettertype van een tekst (leeg = standaard)
        public bool Outline;         // tekst met zwarte rand

        public Stroke Clone()
        {
            Stroke s = (Stroke)MemberwiseClone();
            s.Pts = new List<PointF>(Pts);
            return s;
        }
    }

    // ------------------------------------------------------------------ Win32
    static class W
    {
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8,
            WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, GWL_EXSTYLE = -20,
            WM_HOTKEY = 0x0312, WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, ULW_ALPHA = 2,
            MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000,
            WM_DPICHANGED = 0x02E0, WM_DISPLAYCHANGE = 0x007E, WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02CC,
            SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, VK_ESCAPE = 0x1B;
        // Windows Ink: geen 'lang drukken = rechtermuisklik', geen tikrondjes, geen flicks
        public const int TABLET_FLAGS = 0x00000001 | 0x00000008 | 0x00000010 | 0x00010000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
            out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr hWnd, string name, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        // Venster wel of niet zichtbaar in schermdelen (Zoom, Teams). Werkt vanaf Windows 10 versie 2004.
        public static void HideFromCapture(IntPtr hwnd, bool hide)
        {
            try
            {
                if (!SetWindowDisplayAffinity(hwnd, hide ? 0x11u : 0u) && hide) SetWindowDisplayAffinity(hwnd, 1u);
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct UPDATELAYEREDWINDOWINFO
        {
            public int cbSize; public IntPtr hdcDst, pptDst, psize, hdcSrc, pptSrc;
            public int crKey; public IntPtr pblend; public int dwFlags; public IntPtr prcDirty;
        }
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindowIndirect(IntPtr hwnd, ref UPDATELAYEREDWINDOWINFO info);

        static IntPtr ulwBuf = IntPtr.Zero;

        // Werkt alleen het gewijzigde stuk van het scherm bij (veel sneller op grote schermen)
        public static bool UpdateLayeredDirty(IntPtr hwnd, POINT dst, SIZE size, IntPtr hdcSrc, BLENDFUNCTION bf, Rectangle dirty)
        {
            if (ulwBuf == IntPtr.Zero) ulwBuf = Marshal.AllocHGlobal(64);
            POINT src = new POINT(0, 0);
            RECT rc = new RECT(); rc.Left = dirty.Left; rc.Top = dirty.Top; rc.Right = dirty.Right; rc.Bottom = dirty.Bottom;
            Marshal.StructureToPtr(dst, ulwBuf, false);
            Marshal.StructureToPtr(size, new IntPtr(ulwBuf.ToInt64() + 8), false);
            Marshal.StructureToPtr(src, new IntPtr(ulwBuf.ToInt64() + 16), false);
            Marshal.StructureToPtr(bf, new IntPtr(ulwBuf.ToInt64() + 24), false);
            Marshal.StructureToPtr(rc, new IntPtr(ulwBuf.ToInt64() + 32), false);
            UPDATELAYEREDWINDOWINFO info = new UPDATELAYEREDWINDOWINFO();
            info.cbSize = Marshal.SizeOf(typeof(UPDATELAYEREDWINDOWINFO));
            info.pptDst = ulwBuf; info.psize = new IntPtr(ulwBuf.ToInt64() + 8); info.hdcSrc = hdcSrc;
            info.pptSrc = new IntPtr(ulwBuf.ToInt64() + 16); info.pblend = new IntPtr(ulwBuf.ToInt64() + 24);
            info.dwFlags = ULW_ALPHA; info.prcDirty = new IntPtr(ulwBuf.ToInt64() + 32);
            return UpdateLayeredWindowIndirect(hwnd, ref info);
        }

        public static void KeepOnTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static float DpiScaleAt(Point p)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT(p.X, p.Y), 2);
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return Math.Max(1f, dx / 96f);
            }
            catch { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f);
        }
    }

    // ------------------------------------------------------------------ Foutlog
    static class Log
    {
        static readonly object gate = new object();
        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen", "foutlog.txt"); }
        }

        public static void Info(string msg) { Write("INFO ", msg); }
        public static void Error(string msg, Exception ex) { Write("FOUT ", msg + (ex != null ? Environment.NewLine + ex.ToString() : "")); }

        static void Write(string level, string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }


    // ------------------------------------------------------------------ Tekening bewaren
    static class StrokeStore
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen"); } }
        public static string CurrentPath { get { return Path.Combine(Dir, "tekening.dat"); } }
        public static string PreviousPath { get { return Path.Combine(Dir, "vorige-tekening.dat"); } }
        public static string LockPath { get { return Path.Combine(Dir, "sessie.lock"); } }
        // Bestaat dit bestand, dan heb je op Opslaan gedrukt: de tekening komt bij de volgende start terug
        public static string KeepPath { get { return Path.Combine(Dir, "bewaren.vlag"); } }

        static string B64(string t) { return string.IsNullOrEmpty(t) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(t)); }
        static string UnB64(string t) { return t.Length == 0 ? null : Encoding.UTF8.GetString(Convert.FromBase64String(t)); }

        public static void Save(List<Stroke> list, string path)
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NEXUSPEN2");
            foreach (Stroke st in list)
            {
                sb.Append(st.Tool).Append('|').Append(st.Color.ToArgb().ToString(C)).Append('|')
                  .Append(st.Width.ToString(C)).Append('|').Append(st.FontSize.ToString(C)).Append('|')
                  .Append(B64(st.Text)).Append('|');
                for (int i = 0; i < st.Pts.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(st.Pts[i].X.ToString("0.#", C)).Append(',').Append(st.Pts[i].Y.ToString("0.#", C));
                }
                sb.Append('|').Append(st.Outline ? "1" : "0").Append('|').Append(B64(st.FontName));
                sb.AppendLine();
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch { File.Delete(path); }
            }
            File.Move(tmp, path);
        }

        public static List<Stroke> Load(string path)
        {
            List<Stroke> list = new List<Stroke>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int n = 1; n < lines.Length; n++)
            {
                try
                {
                    string[] f = lines[n].Split('|');
                    if (f.Length < 6) continue;
                    Stroke st = new Stroke();
                    st.Tool = (Tool)Enum.Parse(typeof(Tool), f[0]);
                    st.Color = Color.FromArgb(int.Parse(f[1], C));
                    st.Width = float.Parse(f[2], C);
                    st.FontSize = float.Parse(f[3], C);
                    st.Text = UnB64(f[4]);
                    foreach (string pt in f[5].Split(' '))
                    {
                        int k = pt.IndexOf(',');
                        if (k > 0) st.Pts.Add(new PointF(float.Parse(pt.Substring(0, k), C), float.Parse(pt.Substring(k + 1), C)));
                    }
                    if (f.Length > 6) st.Outline = f[6] == "1";
                    if (f.Length > 7) st.FontName = UnB64(f[7]);
                    if (st.Pts.Count > 0) list.Add(st);
                }
                catch { }
            }
            return list;
        }
    }

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
        public static readonly string[] Names = { "Fel", "Zacht" };
        public static readonly Color[][] Sets = {
            new Color[] { C("#FF1F3D"), C("#FF7A00"), C("#FFD400"), C("#00C853"), C("#00B8FF"), C("#2962FF"), C("#AA00FF"), C("#FF2D95"), C("#111111"), C("#FFFFFF") },
            new Color[] { C("#F28B82"), C("#F6B26B"), C("#FFE08A"), C("#A8D5A2"), C("#9FD3F2"), C("#8AB4F8"), C("#C3A6F0"), C("#F7A8C4"), C("#5F6368"), C("#F4F1E8") }
        };
        public static readonly string[][] Labels = {
            new string[] { "Rood", "Oranje", "Geel", "Groen", "Lichtblauw", "Blauw", "Paars", "Roze", "Zwart", "Wit" },
            new string[] { "Zacht rood", "Zacht oranje", "Zacht geel", "Zacht groen", "Zacht lichtblauw", "Zacht blauw", "Zacht paars", "Zacht roze", "Grijs", "Krijtwit" }
        };
        static Color C(string h) { return ColorTranslator.FromHtml(h); }
    }

    // ------------------------------------------------------------------ Instellingen
    class Settings
    {
        public int ToolbarX = -1, ToolbarY = -1, ColorIndex = 0, PaletteIndex = 0;
        public float Width = 4;
        public Tool Tool = Tool.Pen;
        public string HotkeyDraw = "Ctrl+Shift+D", HotkeyHide = "Ctrl+Shift+H";
        public int ThemeIndex = 0, SizeIndex = 1, OpacityIndex = 0;
        public Color CustomAccent = ColorTranslator.FromHtml("#A3E635");
        public bool Vertical, Square, Collapsed, HideFromCapture, TextOutline;
        public string FontName = "";
        public float FocusRadius = 160;

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
                    try { vk = (int)(Keys)Enum.Parse(typeof(Keys), part, true); } catch { return false; }
                }
            }
            return vk != 0;
        }

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen", "instellingen.ini"); }
        }

        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        public static Settings Load() { return Load(FilePath); }

        public static Settings Load(string path)
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(path)) return s;
                foreach (string line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n; float f;
                    if (k == "toolbarX" && int.TryParse(v, out n)) s.ToolbarX = n;
                    else if (k == "toolbarY" && int.TryParse(v, out n)) s.ToolbarY = n;
                    else if (k == "kleur" && int.TryParse(v, out n)) s.ColorIndex = n;
                    else if (k == "palet" && int.TryParse(v, out n)) s.PaletteIndex = n;
                    else if (k == "dikte" && float.TryParse(v, NumberStyles.Float, IC, out f)) s.Width = f;
                    else if (k == "spotlicht" && float.TryParse(v, NumberStyles.Float, IC, out f)) s.FocusRadius = f;
                    else if (k == "sneltoetsTekenen") s.HotkeyDraw = v;
                    else if (k == "sneltoetsVerbergen") s.HotkeyHide = v;
                    else if (k == "gereedschap") { try { s.Tool = (Tool)Enum.Parse(typeof(Tool), v); } catch { } }
                    else if (k == "thema" && int.TryParse(v, out n)) s.ThemeIndex = n;
                    else if (k == "eigenKleur") { try { s.CustomAccent = ColorTranslator.FromHtml(v); } catch { } }
                    else if (k == "grootte" && int.TryParse(v, out n)) s.SizeIndex = n;
                    else if (k == "doorzichtig" && int.TryParse(v, out n)) s.OpacityIndex = n;
                    else if (k == "staand") s.Vertical = v == "1";
                    else if (k == "strak") s.Square = v == "1";
                    else if (k == "ingeklapt") s.Collapsed = v == "1";
                    else if (k == "verbergenBijDelen") s.HideFromCapture = v == "1";
                    else if (k == "tekstRand") s.TextOutline = v == "1";
                    else if (k == "lettertype") s.FontName = v;
                }
            }
            catch { }
            s.ThemeIndex = Math.Max(0, Math.Min(Theme.All.Length - 1, s.ThemeIndex));
            s.SizeIndex = Math.Max(0, Math.Min(2, s.SizeIndex));
            s.OpacityIndex = Math.Max(0, Math.Min(2, s.OpacityIndex));
            s.PaletteIndex = Math.Max(0, Math.Min(Palette.Sets.Length - 1, s.PaletteIndex));
            s.ColorIndex = Math.Max(0, Math.Min(9, s.ColorIndex));
            if (s.Tool == Tool.Focus || s.Tool == Tool.Select || s.Tool == Tool.Mouse) s.Tool = Tool.Pen;
            return s;
        }

        public void Save() { Save(FilePath); }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new string[] {
                    "toolbarX=" + ToolbarX, "toolbarY=" + ToolbarY, "kleur=" + ColorIndex, "palet=" + PaletteIndex,
                    "dikte=" + Width.ToString(IC), "spotlicht=" + FocusRadius.ToString(IC),
                    "gereedschap=" + Tool,
                    "thema=" + ThemeIndex, "eigenKleur=" + ColorTranslator.ToHtml(CustomAccent),
                    "grootte=" + SizeIndex, "doorzichtig=" + OpacityIndex,
                    "staand=" + (Vertical ? "1" : "0"), "strak=" + (Square ? "1" : "0"), "ingeklapt=" + (Collapsed ? "1" : "0"),
                    "verbergenBijDelen=" + (HideFromCapture ? "1" : "0"),
                    "tekstRand=" + (TextOutline ? "1" : "0"), "lettertype=" + FontName,
                    "sneltoetsTekenen=" + HotkeyDraw, "sneltoetsVerbergen=" + HotkeyHide });
            }
            catch (Exception ex) { Log.Error("Instellingen bewaren mislukt", ex); }
        }
    }

    // ------------------------------------------------------------------ SVG-paden lezen (icoontjes en logo uit het ontwerp)
    static class Svg
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        static List<string> Tokens(string d)
        {
            List<string> t = new List<string>();
            int i = 0;
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsLetter(ch)) { t.Add(ch.ToString()); i++; continue; }
                if (ch == ',' || char.IsWhiteSpace(ch)) { i++; continue; }
                int start = i;
                bool dot = false, exp = false;
                if (ch == '-' || ch == '+') i++;
                while (i < d.Length)
                {
                    char c = d[i];
                    if (char.IsDigit(c)) { i++; continue; }
                    if (c == '.' && !dot && !exp) { dot = true; i++; continue; }
                    if ((c == 'e' || c == 'E') && !exp) { exp = true; i++; if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++; continue; }
                    break;
                }
                if (i == start) { i++; continue; }
                t.Add(d.Substring(start, i - start));
            }
            return t;
        }

        // Kleine boogvlaggen in paden als "a2 2 0 01-2 2" staan direct achter elkaar
        static float Flag(List<string> t, ref int k)
        {
            string s = t[k];
            if (s.Length > 1 && (s[0] == '0' || s[0] == '1'))
            {
                t[k] = s.Substring(1);
                return s[0] == '1' ? 1 : 0;
            }
            k++;
            return float.Parse(s, C);
        }

        public static GraphicsPath Path(string d)
        {
            GraphicsPath gp = new GraphicsPath();
            List<string> t = Tokens(d);
            int k = 0;
            char cmd = 'M';
            PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
            char lastCmd = ' ';
            bool open = false;
            while (k < t.Count)
            {
                if (char.IsLetter(t[k][0]) && t[k].Length == 1) { cmd = t[k][0]; k++; }
                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);
                if (up == 'Z')
                {
                    gp.CloseFigure(); cur = start; open = false; lastCmd = 'Z';
                    continue;
                }
                if (k >= t.Count) break;
                float ox = rel ? cur.X : 0, oy = rel ? cur.Y : 0;
                switch (up)
                {
                    case 'M':
                    {
                        float x = N(t, ref k) + ox, y = N(t, ref k) + oy;
                        if (open) gp.StartFigure();
                        cur = start = new PointF(x, y);
                        open = true;
                        cmd = rel ? 'l' : 'L';
                        break;
                    }
                    case 'L':
                    {
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        gp.AddLine(cur, p); cur = p; break;
                    }
                    case 'H':
                    {
                        PointF p = new PointF(N(t, ref k) + ox, cur.Y);
                        gp.AddLine(cur, p); cur = p; break;
                    }
                    case 'V':
                    {
                        PointF p = new PointF(cur.X, N(t, ref k) + oy);
                        gp.AddLine(cur, p); cur = p; break;
                    }
                    case 'C':
                    {
                        PointF a = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF b = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        gp.AddBezier(cur, a, b, p); lastCtrl = b; cur = p; break;
                    }
                    case 'S':
                    {
                        PointF a = (lastCmd == 'C' || lastCmd == 'S') ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                        PointF b = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        gp.AddBezier(cur, a, b, p); lastCtrl = b; cur = p; break;
                    }
                    case 'Q':
                    {
                        PointF q = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        Quad(gp, cur, q, p); lastCtrl = q; cur = p; break;
                    }
                    case 'A':
                    {
                        float rx = N(t, ref k), ry = N(t, ref k), rot = N(t, ref k);
                        float large = Flag(t, ref k), sweep = Flag(t, ref k);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        Arc(gp, cur, rx, ry, rot, large != 0, sweep != 0, p);
                        cur = p; break;
                    }
                    default: k++; break;
                }
                lastCmd = up;
            }
            return gp;
        }

        static float N(List<string> t, ref int k) { return float.Parse(t[k++], C); }

        public static void Quad(GraphicsPath gp, PointF a, PointF q, PointF b)
        {
            PointF c1 = new PointF(a.X + 2f / 3 * (q.X - a.X), a.Y + 2f / 3 * (q.Y - a.Y));
            PointF c2 = new PointF(b.X + 2f / 3 * (q.X - b.X), b.Y + 2f / 3 * (q.Y - b.Y));
            gp.AddBezier(a, c1, c2, b);
        }

        // SVG-boog (eindpunten) omzetten naar bezierkrommen
        static void Arc(GraphicsPath gp, PointF p0, float rx, float ry, float rotDeg, bool large, bool sweep, PointF p1)
        {
            if (rx == 0 || ry == 0 || (p0.X == p1.X && p0.Y == p1.Y)) { gp.AddLine(p0, p1); return; }
            double phi = rotDeg * Math.PI / 180, cp = Math.Cos(phi), sp = Math.Sin(phi);
            double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
            double x1 = cp * dx + sp * dy, y1 = -sp * dx + cp * dy;
            double arx = Math.Abs(rx), ary = Math.Abs(ry);
            double lam = (x1 * x1) / (arx * arx) + (y1 * y1) / (ary * ary);
            if (lam > 1) { double s = Math.Sqrt(lam); arx *= s; ary *= s; }
            double num = arx * arx * ary * ary - arx * arx * y1 * y1 - ary * ary * x1 * x1;
            double den = arx * arx * y1 * y1 + ary * ary * x1 * x1;
            double co = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cx1 = co * arx * y1 / ary, cy1 = -co * ary * x1 / arx;
            double cx = cp * cx1 - sp * cy1 + (p0.X + p1.X) / 2.0, cy = sp * cx1 + cp * cy1 + (p0.Y + p1.Y) / 2.0;
            double th1 = Ang(1, 0, (x1 - cx1) / arx, (y1 - cy1) / ary);
            double dth = Ang((x1 - cx1) / arx, (y1 - cy1) / ary, (-x1 - cx1) / arx, (-y1 - cy1) / ary);
            if (!sweep && dth > 0) dth -= 2 * Math.PI;
            if (sweep && dth < 0) dth += 2 * Math.PI;
            int segs = (int)Math.Ceiling(Math.Abs(dth) / (Math.PI / 2));
            double step = dth / segs, kk = 4.0 / 3 * Math.Tan(step / 4);
            double th = th1;
            PointF prev = p0;
            for (int i = 0; i < segs; i++)
            {
                double a = th, b = th + step;
                double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b);
                PointF e1 = Pt(cx, cy, arx, ary, cp, sp, ca - kk * sa, sa + kk * ca);
                PointF e2 = Pt(cx, cy, arx, ary, cp, sp, cb + kk * sb, sb - kk * cb);
                PointF e = i == segs - 1 ? p1 : Pt(cx, cy, arx, ary, cp, sp, cb, sb);
                gp.AddBezier(prev, e1, e2, e);
                prev = e; th = b;
            }
        }

        static PointF Pt(double cx, double cy, double rx, double ry, double cp, double sp, double ux, double uy)
        {
            double x = rx * ux, y = ry * uy;
            return new PointF((float)(cp * x - sp * y + cx), (float)(sp * x + cp * y + cy));
        }

        static double Ang(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath gp = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
            if (rad <= 0.01f) { gp.AddRectangle(r); return gp; }
            float d = rad * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    // ------------------------------------------------------------------ Icoontjes (24x24, lijnen zoals op het canvas)
    static class Icons
    {
        // p = pad, c = cirkel cx cy r [doorzichtigheid], r = rechthoek x y b h hoek
        static readonly Dictionary<string, string[]> Data = new Dictionary<string, string[]>();
        static readonly Dictionary<string, GraphicsPath[]> Cache = new Dictionary<string, GraphicsPath[]>();

        static void D(string name, params string[] parts) { Data[name] = parts; }

        static Icons()
        {
            D("muis", "p:M6 3l13 7-5.5 1.8L11 18z");
            D("pen", "p:M4 20l4-1L19 8l-3-3L5 16z", "p:M14 7l3 3");
            D("markeer", "p:M8 14l8-10 4 3-8 10z", "p:M8 14l-3 6h6l1-3");
            D("laser", "c:12 12 3", "c:12 12 7.5 0.45");
            D("vormen", "r:3.5 6 11 9 1.5", "c:16 15 4.5");
            D("tekst", "p:M5 6V4h14v2", "p:M12 4v16", "p:M9 20h6");
            D("stempel", "p:M5 12.5l4.5 4.5L19 7");
            D("focus", "c:12 12 4.5", "p:M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4");
            D("gum", "p:M8 20h12", "p:M4.5 15.5l9-9 5 5-6 6h-4z");
            D("undo", "p:M9 14L4 9l5-5", "p:M4 9h10a6 6 0 010 12h-3");
            D("bord", "r:3 4 18 12 1.5", "p:M8 20l2-4M16 20l-2-4");
            D("foto", "p:M4 8h3l2-3h6l2 3h3v11H4z", "c:12 13 3.5");
            D("oog", "p:M3 3l18 18", "p:M10.6 5.1A9.7 9.7 0 0112 5c6 0 10 7 10 7a17 17 0 01-3.2 3.9M6.6 6.6A17 17 0 002 12s4 7 10 7a9.6 9.6 0 004.4-1");
            D("oogopen", "p:M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z", "c:12 12 3");
            D("instellingen", "c:12 12 3", "p:M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 01-2.83 2.83l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z");
            D("lijn", "p:M5 19L19 5");
            D("pijl", "p:M5 19L19 5", "p:M10 5h9v9");
            D("rechthoek", "r:4 6 16 12 1.5");
            D("cirkel", "c:12 12 8");
            D("op", "p:M7 14l5-5 5 5");
            D("links", "p:M14 7l-5 5 5 5");
            D("neer", "p:M7 10l5 5 5-5");
            D("rechts", "p:M10 7l5 5-5 5");
            D("selecteer", "p:M4 9V5a1 1 0 011-1h4M15 4h4a1 1 0 011 1v4M4 15v4a1 1 0 001 1h4", "p:M13 13l7 3-3 1-1 3z");
            D("prullenbak", "p:M4 7h16", "p:M9 7V4h6v3", "p:M6 7l1 13h10l1-13");
            D("map", "p:M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2z");
            D("opslaan", "p:M5 4h11l3 3v13H5z", "p:M8 4v5h7V4", "p:M8 20v-6h8v6");
            D("vink", "p:M5 12.5l4.5 4.5L19 7");
            D("kruis", "p:M6 6l12 12M18 6L6 18");
            D("vraag", "c:12 12 9", "p:M9.5 9.5a2.5 2.5 0 015 0c0 1.7-2.5 2-2.5 4", "p:M12 17h.01");
            D("dupliceer", "r:8 8 12 12 2", "p:M16 8V5a1 1 0 00-1-1H5a1 1 0 00-1 1v10a1 1 0 001 1h3");
        }

        static GraphicsPath[] Get(string name)
        {
            GraphicsPath[] g;
            if (Cache.TryGetValue(name, out g)) return g;
            string[] parts;
            if (!Data.TryGetValue(name, out parts)) parts = new string[0];
            g = new GraphicsPath[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string kind = parts[i].Substring(0, 1), body = parts[i].Substring(2);
                string[] n = body.Split(' ');
                if (kind == "p") g[i] = Svg.Path(body);
                else if (kind == "c")
                {
                    float cx = F(n[0]), cy = F(n[1]), r = F(n[2]);
                    g[i] = new GraphicsPath(); g[i].AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                }
                else g[i] = Svg.RoundRect(new RectangleF(F(n[0]), F(n[1]), F(n[2]), F(n[3])), F(n[4]));
            }
            Cache[name] = g;
            return g;
        }

        static float Alpha(string name, int i)
        {
            string[] parts = Data[name];
            if (parts[i][0] != 'c') return 1;
            string[] n = parts[i].Substring(2).Split(' ');
            return n.Length > 3 ? F(n[3]) : 1;
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        // Tekent icoon 'name' gecentreerd in r, 'size' pixels groot
        public static void Draw(Graphics g, string name, RectangleF r, float size, Color c, float stroke)
        {
            GraphicsPath[] paths = Get(name);
            GraphicsState st = g.Save();
            float sc = size / 24f;
            g.TranslateTransform(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2);
            g.ScaleTransform(sc, sc);
            for (int i = 0; i < paths.Length; i++)
            {
                float a = Alpha(name, i);
                using (Pen p = new Pen(Color.FromArgb((int)(c.A * a), c), stroke))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawPath(p, paths[i]);
                }
            }
            g.Restore(st);
        }

        public static GraphicsPath Stamp() { return Svg.Path("M5 12.5l4.5 4.5L19 7"); }
    }

    // ------------------------------------------------------------------ Het NexusPen-logo (zelfde maten als het logopakket)
    static class Logo
    {
        static readonly Color Hout = ColorTranslator.FromHtml("#FBCB93"), HoutL = ColorTranslator.FromHtml("#FFE6C9"),
            HoutD = ColorTranslator.FromHtml("#EEA35A"), Inkt = ColorTranslator.FromHtml("#1A0F00"),
            GrafL = ColorTranslator.FromHtml("#4B3019"), Glans = ColorTranslator.FromHtml("#9C7A58");

        // De N en het potloodlijf zijn wit, behalve op een heel lichte accentkleur: dan donker zodat je ze blijft zien
        public static Color LetterColor(Color tile, Color onAccent) { return Theme.Luma(tile) > 0.72 ? onAccent : Color.White; }

        public static void Draw(Graphics g, RectangleF r, Color tile, Color letter)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float sc = Math.Min(r.Width, r.Height) / 64f;
            g.TranslateTransform(r.X + (r.Width - 64 * sc) / 2, r.Y + (r.Height - 64 * sc) / 2);
            g.ScaleTransform(sc, sc);
            bool small = r.Width <= 24.5f;
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0, 0, 64, 64), 14))
            using (SolidBrush b = new SolidBrush(tile)) g.FillPath(b, gp);
            using (SolidBrush wit = new SolidBrush(letter))
            {
                if (small)
                {
                    Stroke9(g, letter, "M20.00 34.00 L20.00 22.37 Q20.00 13.37 24.87 20.93 L39.13 43.07 Q44.00 50.63 44.00 41.63 L44.00 17.50", 9f);
                    g.FillRectangle(wit, 15.5f, 29.5f, 9f, 9f);
                    using (GraphicsPath gp = Svg.Path("M15.5 38.0 H24.5 L20.0 52.0 Z")) g.FillPath(wit, gp);
                }
                else
                {
                    Stroke9(g, letter, "M19.50 34.50 L19.50 21.87 Q19.50 12.87 24.42 20.41 L39.58 43.59 Q44.50 51.13 44.50 42.13 L44.50 17.00", 8f);
                    g.FillRectangle(wit, 15.5f, 30.5f, 8f, 8f);
                    const string cone = "M15.50 38.50 Q16.10 44.50 19.05 50.55 Q19.50 51.47 19.95 50.55 Q22.90 44.50 23.50 38.50 Z";
                    const string graf = "M17.30 46.40 Q18.04 48.47 19.05 50.55 Q19.50 51.47 19.95 50.55 Q20.96 48.47 21.70 46.40 Z";
                    using (GraphicsPath cp = Svg.Path(cone))
                    {
                        using (SolidBrush b = new SolidBrush(Hout)) g.FillPath(b, cp);
                        Region old = g.Clip;
                        g.SetClip(cp, CombineMode.Intersect);
                        Fill(g, HoutL, "M14.5 37.5 H18.22 L19.50 51.01 L14.5 52.01 Z");
                        Fill(g, HoutD, "M20.78 37.5 H24.5 V52.01 L19.50 51.01 Z");
                        using (GraphicsPath gp = Svg.Path(graf))
                        {
                            using (SolidBrush b = new SolidBrush(Inkt)) g.FillPath(b, gp);
                            g.SetClip(gp, CombineMode.Intersect);
                            Fill(g, GrafL, "M16.80 46.40 H18.51 L19.50 51.01 L16.80 51.01 Z");
                            using (Pen p = new Pen(Color.FromArgb(102, Glans), 0.35f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, 18.2f, 47.0f, 18.6f, 48.81f); }
                        }
                        g.Clip = old;
                    }
                    using (GraphicsPath gp = Svg.Path("M15.5 38.1 V38.5 A2.0 2.0 0 0 0 19.5 38.5 A2.0 2.0 0 0 0 23.5 38.5 V38.1 Z")) g.FillPath(wit, gp);
                }
            }
            g.Restore(st);
        }

        static void Stroke9(Graphics g, Color c, string d, float w)
        {
            using (GraphicsPath gp = Svg.Path(d))
            using (Pen p = new Pen(c, w))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                g.DrawPath(p, gp);
            }
        }

        static void Fill(Graphics g, Color c, string d)
        {
            using (GraphicsPath gp = Svg.Path(d)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, gp);
        }

        public static Bitmap Bitmap(int size, Color tile, Color letter)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) Draw(g, new RectangleF(0, 0, size, size), tile, letter);
            return b;
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static Icon MakeIcon(int size, Color tile, Color letter)
        {
            using (Bitmap b = Bitmap(size, tile, letter))
            {
                IntPtr h = b.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }
    }

    // ------------------------------------------------------------------ Lettertypes: Windows-fonts plus Andika en Nunito die in NexusPen zelf zitten
    static class Fonts
    {
        static readonly PrivateFontCollection own = new PrivateFontCollection();
        static readonly Dictionary<string, FontFamily> cache = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool loaded;

        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        // Ingebouwde lettertypes laden (geheugen blijft bewust bestaan zolang NexusPen draait)
        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                System.Reflection.Assembly asm = typeof(Fonts).Assembly;
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (!res.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream st = asm.GetManifestResourceStream(res))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        st.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, mem, data.Length);
                        own.AddMemoryFont(mem, data.Length);
                        try { uint n = 0; AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n); } catch { }
                    }
                }
                foreach (FontFamily f in own.Families) cache[f.Name] = f;
            }
            catch (Exception ex) { Log.Error("Ingebouwde lettertypes laden", ex); }
        }

        public static bool Has(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Family(name, false) != null;
        }

        static FontFamily Family(string name, bool fallback)
        {
            Load();
            FontFamily f;
            if (!string.IsNullOrEmpty(name))
            {
                if (cache.TryGetValue(name, out f)) return f;
                if (!missing.Contains(name))
                {
                    try
                    {
                        f = new FontFamily(name);
                        if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { cache[name] = f; return f; }
                        f.Dispose();
                    }
                    catch { }
                    missing.Add(name);
                }
            }
            if (!fallback) return null;
            if (name != "Segoe UI" && Has("Segoe UI")) return Family("Segoe UI", true);
            return FontFamily.GenericSansSerif;
        }

        // Nooit weggooien: deze families worden gedeeld
        public static FontFamily Family(string name) { return Family(name, true); }

        public static Font Make(string name, float px, FontStyle style)
        {
            FontFamily f = Family(name);
            try
            {
                if (!f.IsStyleAvailable(style)) style = f.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
                return new Font(f, px, style, GraphicsUnit.Pixel);
            }
            catch { return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
        }
    }

    // ------------------------------------------------------------------ Tekenen van lijnen, vormen, tekst en stempels
    static class Render
    {
        static readonly Graphics measureG = Graphics.FromImage(new Bitmap(1, 1));
        static readonly Dictionary<string, bool> installed = new Dictionary<string, bool>();

        public static float MarkerWidth(float w) { return w * 3 + 8; }
        public static float StampSize(float w) { return 22 + w * 4; }

        public static Pen MakePen(Color c, float w)
        {
            Pen p = new Pen(c, w);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }

        public static bool IsInstalled(string name) { return Fonts.Has(name); }

        public static string FontOrDefault(string name) { return Fonts.Has(name) ? name : "Segoe UI"; }

        public static Font MakeFont(float px) { return MakeFont(px, null); }
        public static Font MakeFont(float px, string name) { return Fonts.Make(name, px, FontStyle.Bold); }

        public static RectangleF Norm(PointF a, PointF b)
        {
            return new RectangleF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(1, Math.Abs(a.X - b.X)), Math.Max(1, Math.Abs(a.Y - b.Y)));
        }

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        static void Free(Graphics g, List<PointF> pts, Color c, float w)
        {
            if (pts.Count == 0) return;
            if (pts.Count == 1)
            {
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, pts[0].X - w / 2, pts[0].Y - w / 2, w, w);
                return;
            }
            using (Pen p = MakePen(c, w))
            {
                PointF[] arr = pts.ToArray();
                if (arr.Length > 2) g.DrawCurve(p, arr, 0.5f); else g.DrawLines(p, arr);
            }
        }

        static void Arrow(Graphics g, PointF a, PointF b, Color c, float w)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 2) return;
            float ux = dx / len, uy = dy / len;
            float head = Math.Min(len, Math.Max(14f, w * 4f)), hw = head * 0.6f;
            PointF bp = new PointF(b.X - ux * head, b.Y - uy * head);
            using (Pen p = MakePen(c, w)) g.DrawLine(p, a, new PointF(b.X - ux * head * 0.6f, b.Y - uy * head * 0.6f));
            PointF[] tri = { b, new PointF(bp.X - uy * hw, bp.Y + ux * hw), new PointF(bp.X + uy * hw, bp.Y - ux * hw) };
            using (SolidBrush br = new SolidBrush(c)) g.FillPolygon(br, tri);
        }

        // Tekst als vorm, zodat er een nette zwarte rand omheen kan
        static GraphicsPath TextPath(Stroke s)
        {
            GraphicsPath gp = new GraphicsPath();
            FontFamily ff = Fonts.Family(s.FontName);
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                gp.AddString(s.Text, ff, (int)FontStyle.Bold, s.FontSize, new PointF(s.Pts[0].X + OutlineWidth(s), s.Pts[0].Y + OutlineWidth(s)), sf);
            }
            return gp;
        }

        public static float OutlineWidth(Stroke s) { return s.Outline ? Math.Max(2f, s.FontSize / 9f) : 0; }

        static void DrawText(Graphics g, Stroke s)
        {
            if (!s.Outline)
            {
                using (Font f = MakeFont(s.FontSize, s.FontName)) using (SolidBrush br = new SolidBrush(s.Color)) g.DrawString(s.Text, f, br, s.Pts[0]);
                return;
            }
            using (GraphicsPath gp = TextPath(s))
            {
                Color edge = Theme.Luma(s.Color) < 0.25 ? Color.White : Color.Black;
                using (Pen p = new Pen(edge, OutlineWidth(s) * 2)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, gp); }
                using (SolidBrush br = new SolidBrush(s.Color)) g.FillPath(br, gp);
            }
        }

        static void DrawStamp(Graphics g, Stroke s)
        {
            float size = s.FontSize > 0 ? s.FontSize : StampSize(s.Width);
            GraphicsState st = g.Save();
            g.TranslateTransform(s.Pts[0].X - size / 2, s.Pts[0].Y - size / 2);
            g.ScaleTransform(size / 24f, size / 24f);
            using (GraphicsPath gp = Icons.Stamp())
            {
                using (Pen p = MakePen(Color.FromArgb(s.Color.A, s.Color), 3.2f)) g.DrawPath(p, gp);
            }
            g.Restore(st);
        }

        public static void Draw(Graphics g, Stroke s, float fade)
        {
            if (s.Pts.Count == 0) return;
            PointF a = s.Pts[0], b = s.Pts[s.Pts.Count - 1];
            switch (s.Tool)
            {
                case Tool.Pen: Free(g, s.Pts, s.Color, s.Width); break;
                case Tool.Marker: Free(g, s.Pts, Color.FromArgb(110, s.Color), MarkerWidth(s.Width)); break;
                case Tool.Laser:
                    Free(g, s.Pts, Color.FromArgb((int)(60 * fade), s.Color), s.Width * 3 + 10);
                    Free(g, s.Pts, Color.FromArgb((int)(255 * fade), s.Color), s.Width + 2);
                    Free(g, s.Pts, Color.FromArgb((int)(200 * fade), 255, 255, 255), Math.Max(1f, s.Width / 2.5f));
                    break;
                case Tool.Line: using (Pen p = MakePen(s.Color, s.Width)) g.DrawLine(p, a, b); break;
                case Tool.Arrow: Arrow(g, a, b, s.Color, s.Width); break;
                case Tool.Rect: using (Pen p = MakePen(s.Color, s.Width)) { RectangleF r = Norm(a, b); g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); } break;
                case Tool.Ellipse: using (Pen p = MakePen(s.Color, s.Width)) g.DrawEllipse(p, Norm(a, b)); break;
                case Tool.Stamp: DrawStamp(g, s); break;
                case Tool.Text:
                    if (string.IsNullOrEmpty(s.Text)) break;
                    DrawText(g, s);
                    break;
            }
        }

        public static RectangleF TextBounds(Stroke s)
        {
            using (Font f = MakeFont(s.FontSize, s.FontName))
            {
                SizeF sz = measureG.MeasureString(s.Text ?? "", f);
                float o = OutlineWidth(s) * 2;
                return new RectangleF(s.Pts[0], new SizeF(sz.Width + o, sz.Height + o));
            }
        }

        // Omtrek van een item: voor selecteren, verplaatsen en het bijwerken van het scherm
        public static RectangleF Bounds(Stroke s)
        {
            if (s.Pts.Count == 0) return RectangleF.Empty;
            if (s.Tool == Tool.Text) return TextBounds(s);
            if (s.Tool == Tool.Stamp)
            {
                float z = s.FontSize > 0 ? s.FontSize : StampSize(s.Width);
                return new RectangleF(s.Pts[0].X - z / 2, s.Pts[0].Y - z / 2, z, z);
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (PointF p in s.Pts)
            {
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y;
            }
            float pad = s.Tool == Tool.Marker ? MarkerWidth(s.Width) / 2 : s.Width / 2;
            return RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
        }

        public static bool Hit(Stroke s, PointF p, float r)
        {
            if (s.Pts.Count == 0) return false;
            if (s.Tool == Tool.Text || s.Tool == Tool.Stamp) { RectangleF tb = Bounds(s); tb.Inflate(r, r); return tb.Contains(p); }
            float w = (s.Tool == Tool.Marker ? MarkerWidth(s.Width) : s.Width) + r * 2;
            if (s.Pts.Count == 1)
            {
                float dx = p.X - s.Pts[0].X, dy = p.Y - s.Pts[0].Y;
                return Math.Sqrt(dx * dx + dy * dy) <= w / 2;
            }
            using (GraphicsPath gp = new GraphicsPath())
            {
                PointF a = s.Pts[0], b = s.Pts[s.Pts.Count - 1];
                switch (s.Tool)
                {
                    case Tool.Line: case Tool.Arrow: gp.AddLine(a, b); break;
                    case Tool.Rect: gp.AddRectangle(Norm(a, b)); break;
                    case Tool.Ellipse: gp.AddEllipse(Norm(a, b)); break;
                    default: gp.AddLines(s.Pts.ToArray()); break;
                }
                using (Pen pen = new Pen(Color.Black, w)) return gp.IsOutlineVisible(p, pen);
            }
        }

        public static void Move(Stroke s, float dx, float dy)
        {
            for (int i = 0; i < s.Pts.Count; i++) s.Pts[i] = new PointF(s.Pts[i].X + dx, s.Pts[i].Y + dy);
        }
    }

    // ------------------------------------------------------------------ Tekenlaag over het hele scherm
    class Overlay : Form
    {
        public Settings Cfg;
        public Tool CurrentTool = Tool.Pen;
        public Tool LastDrawTool = Tool.Pen;
        public Tool LastShape = Tool.Arrow;
        public int ColorIndex = 0, PaletteIndex = 0;
        public float PenWidth = 4;
        public Board BoardMode = Board.None;
        public static Overlay Instance;
        public volatile bool DrawMode = false;
        public bool StrokesVisible = true;
        public DateTime SavedAt = DateTime.MinValue;

        List<Stroke> strokes = new List<Stroke>();
        List<List<Stroke>> undo = new List<List<Stroke>>(), redo = new List<List<Stroke>>();
        List<Stroke> lasers = new List<Stroke>();
        Stroke current;
        bool erasing, erasedThisDrag;

        // selecteren en verplaatsen
        Stroke selected;
        bool dragging, movedThisDrag, lifted;
        PointF dragLast;

        // spotlicht
        PointF focusAt = new PointF(-10000, -10000);

        Rectangle vs;
        IntPtr memDC, hBmp, oldBmp, bits;
        Bitmap frame, committed;
        bool dirty = true;
        System.Windows.Forms.Timer timer;
        Toolbar toolbar;
        NotifyIcon tray;
        TextInput textInput;
        SettingsForm settingsForm;
        bool allHidden;

        // motorkap
        Rectangle lastTransient = Rectangle.Empty, pendingDirty = Rectangle.Empty;
        bool fullDirty = true, useIndirect = true, lastEsc, needSave, restoredAfterCrash, restoredSaved;
        DateTime changedAt = DateTime.MinValue, lastErrorBalloon = DateTime.MinValue;
        int tickCount;

        public Color CurrentColor { get { return Palette.Sets[PaletteIndex][ColorIndex]; } }
        public Theme Theme { get { return Theme.Get(Cfg); } }
        public Toolbar Bar { get { return toolbar; } }
        public bool HasSelection { get { return selected != null; } }
        public static bool IsShape(Tool t) { return t == Tool.Line || t == Tool.Arrow || t == Tool.Rect || t == Tool.Ellipse; }

        public Overlay()
        {
            Instance = this;
            Cfg = Settings.Load();
            StartSession();
            ColorIndex = Cfg.ColorIndex;
            PaletteIndex = Cfg.PaletteIndex;
            PenWidth = Math.Max(1, Math.Min(60, Cfg.Width));
            if (Cfg.Tool != Tool.Mouse) { CurrentTool = Cfg.Tool; LastDrawTool = Cfg.Tool; }
            if (IsShape(Cfg.Tool)) LastShape = Cfg.Tool;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "NexusPen";
            KeyPreview = true;
            vs = SystemInformation.VirtualScreen;
            Bounds = vs;
            SetupBuffers();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += delegate { Tick(); };
            timer.Start();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= W.WS_EX_LAYERED | W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST | W.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        // ---------- buffers
        void SetupBuffers()
        {
            FreeBuffers();
            int w = Math.Max(1, vs.Width), h = Math.Max(1, vs.Height);
            IntPtr screen = W.GetDC(IntPtr.Zero);
            memDC = W.CreateCompatibleDC(screen);
            W.ReleaseDC(IntPtr.Zero, screen);
            W.BITMAPINFOHEADER bi = new W.BITMAPINFOHEADER();
            bi.biSize = (uint)Marshal.SizeOf(typeof(W.BITMAPINFOHEADER));
            bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
            hBmp = W.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = W.SelectObject(memDC, hBmp);
            frame = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            committed = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            fullDirty = true;
            lastTransient = Rectangle.Empty;
            pendingDirty = Rectangle.Empty;
        }

        void FreeBuffers()
        {
            if (frame != null) { frame.Dispose(); frame = null; }
            if (committed != null) { committed.Dispose(); committed = null; }
            if (memDC != IntPtr.Zero)
            {
                W.SelectObject(memDC, oldBmp);
                W.DeleteObject(hBmp);
                W.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
        }

        bool displayChangePending;

        void OnDisplayChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || displayChangePending) return;
            displayChangePending = true;
            BeginInvoke((MethodInvoker)delegate
            {
                displayChangePending = false;
                vs = SystemInformation.VirtualScreen;
                Bounds = vs;
                SetupBuffers();
                Rebuild();
                if (toolbar != null) toolbar.EnsureOnScreen();
            });
        }

        Color Background()
        {
            if (DrawMode && BoardMode == Board.White) return Color.FromArgb(255, 250, 250, 248);
            if (DrawMode && BoardMode == Board.Black) return Color.FromArgb(255, 28, 32, 30);
            // alpha 1 = onzichtbaar, maar vangt wel de muis op in tekenmodus
            return DrawMode ? Color.FromArgb(1, 0, 0, 0) : Color.FromArgb(0, 0, 0, 0);
        }

        public void Rebuild()
        {
            using (Graphics g = Graphics.FromImage(committed))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Background());
                g.CompositingMode = CompositingMode.SourceOver;
                Render.Setup(g);
                if (StrokesVisible)
                    foreach (Stroke s in strokes)
                        if (!(lifted && s == selected)) SafeDraw(g, s, 1f);
            }
            fullDirty = true;
            dirty = true;
        }

        void CommitToLayer(Stroke s)
        {
            using (Graphics g = Graphics.FromImage(committed)) { Render.Setup(g); SafeDraw(g, s, 1f); }
            pendingDirty = U(pendingDirty, StrokeBounds(s));
            dirty = true;
        }

        bool drawErrorLogged;

        void SafeDraw(Graphics g, Stroke s, float fade)
        {
            try { Render.Draw(g, s, fade); }
            catch (Exception ex)
            {
                if (!drawErrorLogged) { drawErrorLogged = true; Log.Error("Een lijn kon niet getekend worden (" + s.Tool + ", " + s.Pts.Count + " punten)", ex); }
            }
        }

        static Rectangle U(Rectangle a, Rectangle b)
        {
            if (a.Width <= 0 || a.Height <= 0) return b;
            if (b.Width <= 0 || b.Height <= 0) return a;
            return Rectangle.Union(a, b);
        }

        Rectangle Clip(RectangleF r)
        {
            Rectangle ri = Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
            ri.Intersect(new Rectangle(0, 0, committed.Width, committed.Height));
            return ri;
        }

        // Gebied dat een lijn/vorm op het scherm inneemt (met marge voor dikte, gloed en pijlpunt)
        Rectangle StrokeBounds(Stroke s)
        {
            if (s.Pts.Count == 0 || committed == null) return Rectangle.Empty;
            RectangleF r = Render.Bounds(s);
            float pad = 8;
            if (s.Tool == Tool.Laser) pad = (s.Width * 3 + 10) / 2 + 4;
            else if (s.Tool == Tool.Arrow) pad = Math.Max(14f, s.Width * 4f) + s.Width + 4;
            else if (s.Tool != Tool.Text && s.Tool != Tool.Stamp) pad = 4 + s.Width; // de vloeiende curve kan iets buiten de punten lopen
            r.Inflate(pad, pad);
            return Clip(r);
        }

        RectangleF SelectionBox()
        {
            RectangleF r = Render.Bounds(selected);
            r.Inflate(7, 7);
            return r;
        }

        RectangleF FocusRect()
        {
            float r = Cfg.FocusRadius + 3;
            return new RectangleF(focusAt.X - r, focusAt.Y - r, 2 * r, 2 * r);
        }

        bool FocusOn { get { return DrawMode && CurrentTool == Tool.Focus; } }

        void Tick()
        {
            Program.Ping();
            tickCount++;
            if (lasers.Count > 0)
            {
                DateTime now = DateTime.Now;
                lasers.RemoveAll(delegate(Stroke s) { return (now - s.Born).TotalMilliseconds > 1500; });
                dirty = true;
            }
            if (dirty) RenderFrame();

            // Esc werkt altijd, ook als de tekenlaag niet het actieve venster is
            bool esc = (W.GetAsyncKeyState(W.VK_ESCAPE) & 0x8000) != 0;
            if (esc && !lastEsc && DrawMode && textInput == null)
            {
                if (selected != null) Deselect(); else SetTool(Tool.Mouse);
            }
            lastEsc = esc;

            // Bovenop blijven liggen (taakbalk, startmenu en Zoom duwen ons anders weg)
            if (tickCount % 60 == 0 && IsHandleCreated)
            {
                if (Visible) W.KeepOnTop(Handle);
                if (toolbar != null) toolbar.KeepAllOnTop();
            }

            // Automatisch bewaren, 1,5 seconde na de laatste wijziging
            if (needSave && (DateTime.Now - changedAt).TotalMilliseconds > 1500) SaveNow();
        }

        void MarkChanged() { needSave = true; changedAt = DateTime.Now; }

        void SaveNow()
        {
            needSave = false;
            try { StrokeStore.Save(strokes, StrokeStore.CurrentPath); }
            catch (Exception ex) { Log.Error("Tekening bewaren mislukt", ex); }
        }

        // De knop Opslaan: tekening en instellingen vastleggen, en de tekening komt bij de volgende start terug
        public bool SaveExplicit()
        {
            SyncCfg();
            Cfg.Save();
            needSave = false;
            try
            {
                StrokeStore.Save(strokes, StrokeStore.CurrentPath);
                if (strokes.Count > 0) File.WriteAllText(StrokeStore.KeepPath, DateTime.Now.ToString("o"));
                else if (File.Exists(StrokeStore.KeepPath)) File.Delete(StrokeStore.KeepPath);
                SavedAt = DateTime.Now;
                RefreshUi();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Opslaan mislukt", ex);
                if (tray != null) tray.ShowBalloonTip(3000, "NexusPen", "Opslaan is niet gelukt: " + ex.Message, ToolTipIcon.Warning);
                return false;
            }
        }

        void StartSession()
        {
            try
            {
                Directory.CreateDirectory(StrokeStore.Dir);
                bool keep = File.Exists(StrokeStore.KeepPath);
                if (File.Exists(StrokeStore.LockPath))
                {
                    // Vorige keer niet netjes afgesloten: tekening terugzetten
                    List<Stroke> rec = StrokeStore.Load(StrokeStore.CurrentPath);
                    if (rec.Count > 0) { strokes = rec; restoredAfterCrash = true; }
                    Log.Info("Vorige sessie niet netjes afgesloten, " + rec.Count + " lijnen teruggezet");
                }
                else if (keep)
                {
                    // Je hebt op Opslaan gedrukt: de tekening staat er weer
                    List<Stroke> rec = StrokeStore.Load(StrokeStore.CurrentPath);
                    if (rec.Count > 0) { strokes = rec; restoredSaved = true; }
                }
                else if (File.Exists(StrokeStore.CurrentPath))
                {
                    File.Copy(StrokeStore.CurrentPath, StrokeStore.PreviousPath, true);
                    File.Delete(StrokeStore.CurrentPath);
                }
                File.WriteAllText(StrokeStore.LockPath, DateTime.Now.ToString("o"));
            }
            catch (Exception ex) { Log.Error("Sessie starten", ex); }
        }

        public void RestorePrevious()
        {
            List<Stroke> prev = StrokeStore.Load(StrokeStore.PreviousPath);
            if (prev.Count == 0)
            {
                if (tray != null) tray.ShowBalloonTip(2500, "NexusPen", "Er is geen vorige tekening gevonden.", ToolTipIcon.Info);
                return;
            }
            Checkpoint();
            strokes.AddRange(prev);
            StrokesVisible = true;
            Rebuild();
            RefreshUi();
        }

        // Nooduitgang: altijd terug naar doorklikken, wat er ook misgaat
        public void EmergencyMouseMode()
        {
            try
            {
                if (IsHandleCreated)
                {
                    int ex = W.GetWindowLong(Handle, W.GWL_EXSTYLE);
                    W.SetWindowLong(Handle, W.GWL_EXSTYLE, ex | W.WS_EX_TRANSPARENT);
                }
                DrawMode = false;
                current = null;
                erasing = false;
                dragging = false;
                lifted = false;
                Rebuild();
                RenderFrame();
                RefreshUi();
            }
            catch { }
        }

        public void ReportError()
        {
            if (tray == null || (DateTime.Now - lastErrorBalloon).TotalSeconds < 60) return;
            lastErrorBalloon = DateTime.Now;
            try { tray.ShowBalloonTip(4000, "NexusPen", "Er ging iets mis, maar NexusPen draait door. Details staan in foutlog.txt (rechtsklik op het icoon).", ToolTipIcon.Warning); }
            catch { }
        }

        void CopyFromCommitted(Rectangle area)
        {
            int w = committed.Width;
            BitmapData bd = committed.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                long stride = (long)w * 4, rowBytes = (long)area.Width * 4;
                if (area.X == 0 && area.Width == w && bd.Stride == stride)
                    W.CopyMemory(new IntPtr(bits.ToInt64() + area.Y * stride), bd.Scan0, new UIntPtr((ulong)(rowBytes * area.Height)));
                else
                    for (int y = 0; y < area.Height; y++)
                        W.CopyMemory(new IntPtr(bits.ToInt64() + (area.Y + y) * stride + area.X * 4L),
                            new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), new UIntPtr((ulong)rowBytes));
            }
            finally { committed.UnlockBits(bd); }
        }

        void RenderFrame()
        {
            dirty = false;
            if (!IsHandleCreated || frame == null) return;
            int w = committed.Width, h = committed.Height;
            Rectangle full = new Rectangle(0, 0, w, h);
            Rectangle transient = Rectangle.Empty;
            if (current != null) transient = U(transient, StrokeBounds(current));
            foreach (Stroke s in lasers) transient = U(transient, StrokeBounds(s));
            if (selected != null)
            {
                RectangleF sb = SelectionBox(); sb.Inflate(3, 3);
                transient = U(transient, U(Clip(sb), StrokeBounds(selected)));
            }
            bool focus = FocusOn;
            if (focus) transient = U(transient, Clip(FocusRect()));

            bool wasFull = fullDirty;
            Rectangle area = wasFull ? full : U(U(lastTransient, transient), pendingDirty);
            area.Intersect(full);
            lastTransient = transient;
            pendingDirty = Rectangle.Empty;
            fullDirty = false;
            if (area.Width <= 0 || area.Height <= 0) return;

            CopyFromCommitted(area);
            if ((transient.Width > 0 && transient.Height > 0) || focus)
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.SetClip(area);
                    Render.Setup(g);
                    DateTime now = DateTime.Now;
                    foreach (Stroke s in lasers)
                    {
                        double age = (now - s.Born).TotalMilliseconds;
                        float fade = age < 700 ? 1f : (float)Math.Max(0, 1 - (age - 700) / 800.0);
                        SafeDraw(g, s, fade);
                    }
                    if (current != null) SafeDraw(g, current, 1f);
                    if (selected != null)
                    {
                        if (lifted) SafeDraw(g, selected, 1f);
                        DrawSelection(g);
                    }
                    if (focus) DrawFocus(g, area);
                }
            }
            W.GdiFlush();
            W.POINT dst = new W.POINT(vs.X, vs.Y), src = new W.POINT(0, 0);
            W.SIZE size = new W.SIZE(w, h);
            W.BLENDFUNCTION bf = new W.BLENDFUNCTION();
            bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
            bool ok = false;
            if (!wasFull && useIndirect)
            {
                try { ok = W.UpdateLayeredDirty(Handle, dst, size, memDC, bf, area); }
                catch (Exception ex) { Log.Error("Gedeeltelijk verversen niet beschikbaar", ex); }
                if (!ok) { useIndirect = false; Log.Info("Gedeeltelijk verversen werkt niet op deze pc (code " + Marshal.GetLastWin32Error() + "), voortaan volledig verversen"); }
            }
            if (!ok) W.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref bf, W.ULW_ALPHA);
        }

        void DrawSelection(Graphics g)
        {
            RectangleF r = SelectionBox();
            Color acc = Theme.Accent;
            using (GraphicsPath gp = Svg.RoundRect(r, 6))
            {
                using (Pen p = new Pen(Color.FromArgb(150, 0, 0, 0), 3)) g.DrawPath(p, gp);
                using (Pen p = new Pen(acc, 1.6f)) { p.DashPattern = new float[] { 4, 3 }; g.DrawPath(p, gp); }
            }
            using (SolidBrush b = new SolidBrush(acc))
                foreach (PointF c in new PointF[] { r.Location, new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) })
                    g.FillEllipse(b, c.X - 3.5f, c.Y - 3.5f, 7, 7);
        }

        void DrawFocus(Graphics g, Rectangle area)
        {
            float r = Cfg.FocusRadius;
            using (GraphicsPath gp = new GraphicsPath(FillMode.Alternate))
            {
                gp.AddRectangle(area);
                gp.AddEllipse(focusAt.X - r, focusAt.Y - r, 2 * r, 2 * r);
                g.CompositingMode = CompositingMode.SourceOver;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(185, 0, 0, 0))) g.FillPath(b, gp);
            }
            using (Pen p = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f)) g.DrawEllipse(p, focusAt.X - r, focusAt.Y - r, 2 * r, 2 * r);
        }

        // ---------- opstarten
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(W.TABLET_FLAGS));
            int mods, vk;
            if (!Settings.ParseHotkey(Cfg.HotkeyDraw, out mods, out vk) || !W.RegisterHotKey(Handle, 1, mods | W.MOD_NOREPEAT, vk))
                Log.Info("Sneltoets " + Cfg.HotkeyDraw + " kon niet worden ingesteld (ongeldig of al in gebruik door een ander programma)");
            if (!Settings.ParseHotkey(Cfg.HotkeyHide, out mods, out vk) || !W.RegisterHotKey(Handle, 2, mods | W.MOD_NOREPEAT, vk))
                Log.Info("Sneltoets " + Cfg.HotkeyHide + " kon niet worden ingesteld (ongeldig of al in gebruik door een ander programma)");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            toolbar = new Toolbar(this);
            toolbar.Owner = this;
            toolbar.Show();
            SetupTray();
            Rebuild();
            RenderFrame();
            if (restoredAfterCrash)
                tray.ShowBalloonTip(4000, "NexusPen", "Je tekening van de vorige keer is teruggezet, want NexusPen was onverwacht gestopt. Wissen kan met de prullenbak.", ToolTipIcon.Info);
            else if (restoredSaved)
                tray.ShowBalloonTip(4000, "NexusPen", "Je opgeslagen tekening staat er weer. Liever leeg beginnen? Wis alles (Delete) en druk op Opslaan.", ToolTipIcon.Info);
        }

        // Voor de automatische test op GitHub: werkbalk, popups en instellingen openen en weer netjes sluiten
        public void RunSelfTest()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            int step = 0;
            t.Interval = 400;
            t.Tick += delegate
            {
                try
                {
                    switch (step++)
                    {
                        case 0: SetTool(Tool.Pen); break;
                        case 1: SetTool(Tool.Focus); break;
                        case 2: SetTool(Tool.Select); break;
                        case 3: ShowSettings(); break;
                        case 4: Cfg.Vertical = !Cfg.Vertical; ApplySettings(); break;
                        case 5: Cfg.Vertical = !Cfg.Vertical; ApplySettings(); SetTool(Tool.Mouse); break;
                        default: t.Stop(); Close(); break;
                    }
                }
                catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest stap " + step, ex); Console.Error.WriteLine(ex); }
            };
            Shown += delegate { t.Start(); };
        }

        void SetupTray()
        {
            tray = new NotifyIcon();
            UpdateTrayIcon();
            tray.Text = "NexusPen  (" + Cfg.HotkeyDraw + " = tekenen aan/uit)";
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add("Tekenen aan/uit\t" + Cfg.HotkeyDraw, delegate { ToggleDraw(); });
            m.MenuItems.Add("Alles tonen/verbergen\t" + Cfg.HotkeyHide, delegate { ToggleAllHidden(); });
            m.MenuItems.Add("Opslaan voor de volgende keer", delegate { SaveExplicit(); });
            m.MenuItems.Add("Schermafbeelding maken", delegate { Screenshot(); });
            m.MenuItems.Add("Instellingen...", delegate { ShowSettings(); });
            m.MenuItems.Add("Werkbalk terugzetten", delegate { if (allHidden) ToggleAllHidden(); toolbar.ResetPosition(); });
            m.MenuItems.Add("Vorige tekening terughalen", delegate { RestorePrevious(); });
            m.MenuItems.Add("Sneltoetsen...", delegate { ShowHelp(); });
            m.MenuItems.Add("-");
            m.MenuItems.Add("Noodstop: terug naar de muis", delegate { EmergencyMouseMode(); });
            m.MenuItems.Add("Foutlog openen", delegate
            {
                if (File.Exists(Log.FilePath)) Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
                else MessageBox.Show("Er zijn nog geen fouten gelogd.", "NexusPen");
            });
            m.MenuItems.Add("Afsluiten", delegate { Close(); });
            tray.ContextMenu = m;
            tray.DoubleClick += delegate { ToggleDraw(); };
            tray.Visible = true;
        }

        Icon trayIcon;

        void UpdateTrayIcon()
        {
            if (tray == null) return;
            try
            {
                Theme t = Theme;
                int size = SystemInformation.SmallIconSize.Width;
                Icon ic = Logo.MakeIcon(Math.Max(16, size), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
                tray.Icon = ic;
                if (trayIcon != null) trayIcon.Dispose();
                trayIcon = ic;
            }
            catch (Exception ex) { Log.Error("Icoon maken", ex); }
        }

        void SyncCfg()
        {
            Cfg.ColorIndex = ColorIndex; Cfg.PaletteIndex = PaletteIndex; Cfg.Width = PenWidth;
            Cfg.Tool = (LastDrawTool == Tool.Focus || LastDrawTool == Tool.Select) ? Tool.Pen : LastDrawTool;
            if (toolbar != null && toolbar.Visible) { Cfg.ToolbarX = toolbar.Left; Cfg.ToolbarY = toolbar.Top; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            for (int i = 1; i <= 2; i++) W.UnregisterHotKey(Handle, i);
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            timer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            SyncCfg();
            Cfg.Save();
            SaveNow();
            try { File.Delete(StrokeStore.LockPath); } catch { }
            Log.Info("Netjes afgesloten");
            FreeBuffers();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; } // de tekenlaag zelf nooit laten schalen
            if (m.Msg == W.WM_TABLET_QUERYSYSTEMGESTURESTATUS) { m.Result = new IntPtr(W.TABLET_FLAGS); return; }
            if (m.Msg == W.WM_DISPLAYCHANGE) OnDisplayChanged(this, EventArgs.Empty);
            if (m.Msg == W.WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case 1: ToggleDraw(); break;
                    case 2: ToggleAllHidden(); break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- instellingen en uiterlijk
        public void ShowSettings()
        {
            if (DrawMode) SetTool(Tool.Mouse);
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(this);
            settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show();
            settingsForm.Activate();
        }

        // Na elke wijziging in Instellingen: werkbalk, icoon en geopende vensters opnieuw opbouwen
        public void ApplySettings()
        {
            Cfg.Save();
            UpdateTrayIcon();
            if (toolbar != null) toolbar.Restyle();
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                W.HideFromCapture(settingsForm.Handle, Cfg.HideFromCapture);
                settingsForm.Invalidate();
            }
            if (selected != null) dirty = true;
        }

        // ---------- modus en gereedschap
        void SetDrawMode(bool on)
        {
            if (on && allHidden) ToggleAllHidden();
            DrawMode = on;
            int ex = W.GetWindowLong(Handle, W.GWL_EXSTYLE);
            if (on) ex &= ~W.WS_EX_TRANSPARENT; else ex |= W.WS_EX_TRANSPARENT;
            W.SetWindowLong(Handle, W.GWL_EXSTYLE, ex);
            if (!on) { current = null; erasing = false; CloseTextInput(true); Deselect(); }
            Rebuild();
            RenderFrame();
            if (on) Activate();
            RefreshUi();
        }

        public void SetTool(Tool t)
        {
            if (t == Tool.Mouse) { if (DrawMode) SetDrawMode(false); RefreshUi(); return; }
            if (t != Tool.Select) Deselect();
            bool wasFocus = CurrentTool == Tool.Focus;
            CurrentTool = t;
            if (t != Tool.Focus && t != Tool.Select) LastDrawTool = t;
            if (IsShape(t)) LastShape = t;
            Cursor = t == Tool.Text ? Cursors.IBeam : t == Tool.Select ? Cursors.Default : t == Tool.Focus ? Cursors.Default : Cursors.Cross;
            if (t == Tool.Focus)
            {
                Point mp = PointToClient(Control.MousePosition);
                focusAt = mp;
            }
            if (!DrawMode) SetDrawMode(true); else { Activate(); if (wasFocus || t == Tool.Focus) { fullDirty = true; dirty = true; } }
            RefreshUi();
        }

        public void ToggleDraw() { if (DrawMode) SetTool(Tool.Mouse); else SetTool(LastDrawTool); }

        public void ToggleFocus() { if (FocusOn) SetTool(Tool.Mouse); else SetTool(Tool.Focus); }

        // Kleur kiezen pakt weer een tekengereedschap als je aan het gummen of selecteren was
        public void SetColor(int i)
        {
            ColorIndex = Math.Max(0, Math.Min(9, i));
            if (selected != null) { RecolorSelected(CurrentColor); return; }
            if (!DrawMode || CurrentTool == Tool.Eraser || CurrentTool == Tool.Focus)
                SetTool(CurrentTool == Tool.Eraser || CurrentTool == Tool.Mouse || CurrentTool == Tool.Focus ? LastDrawTool : CurrentTool);
            RefreshUi();
        }

        public void SetPalette(int p)
        {
            PaletteIndex = Math.Max(0, Math.Min(Palette.Sets.Length - 1, p));
            Cfg.PaletteIndex = PaletteIndex;
            RefreshUi();
        }

        public void SetWidth(float w)
        {
            PenWidth = Math.Max(1, Math.Min(60, w));
            RefreshUi();
        }

        public void Wheel(int delta)
        {
            if (FocusOn)
            {
                Cfg.FocusRadius = Math.Max(60, Math.Min(700, Cfg.FocusRadius + (delta > 0 ? 20 : -20)));
                dirty = true;
                return;
            }
            float step = PenWidth < 6 ? 1 : 2;
            SetWidth(PenWidth + (delta > 0 ? step : -step));
        }

        public void CycleBoard()
        {
            BoardMode = BoardMode == Board.None ? Board.White : BoardMode == Board.White ? Board.Black : Board.None;
            if (BoardMode != Board.None && !DrawMode) SetTool(LastDrawTool);
            Rebuild();
            RefreshUi();
        }

        // Oog: alle tekeningen even weg (en terug naar de muis). Nog een keer klikken = weer tonen.
        public void ToggleStrokesVisible()
        {
            StrokesVisible = !StrokesVisible;
            if (!StrokesVisible) { Deselect(); if (DrawMode) SetDrawMode(false); }
            Rebuild();
            RefreshUi();
        }

        public void ToggleAllHidden()
        {
            allHidden = !allHidden;
            if (allHidden)
            {
                if (DrawMode) SetDrawMode(false);
                if (toolbar != null) toolbar.HideAll();
                Hide();
            }
            else
            {
                Show();
                Rebuild();
                RenderFrame();
                if (toolbar != null) toolbar.ShowAgain();
            }
        }

        void RefreshUi() { if (toolbar != null) toolbar.Refresh2(); }

        // ---------- ongedaan maken
        void Checkpoint()
        {
            MarkChanged();
            undo.Add(new List<Stroke>(strokes));
            if (undo.Count > 300) undo.RemoveAt(0);
            redo.Clear();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            Deselect();
            redo.Add(strokes);
            strokes = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            MarkChanged();
            Rebuild();
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            Deselect();
            undo.Add(strokes);
            strokes = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            MarkChanged();
            Rebuild();
        }

        public void ClearAll()
        {
            lasers.Clear();
            Deselect();
            if (strokes.Count == 0) { dirty = true; return; }
            Checkpoint();
            strokes = new List<Stroke>();
            Rebuild();
        }

        // ---------- selecteren
        void SelectStroke(Stroke s)
        {
            selected = s;
            dirty = true;
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
        }

        public void Deselect()
        {
            if (selected == null) return;
            selected = null;
            dragging = false;
            if (lifted) { lifted = false; Rebuild(); }
            dirty = true;
            if (toolbar != null) toolbar.HideMiniBar();
        }

        Rectangle SelectionScreenRect()
        {
            RectangleF r = SelectionBox();
            return new Rectangle((int)r.X + vs.X, (int)r.Y + vs.Y, (int)r.Width, (int)r.Height);
        }

        // Een wijziging aan het geselecteerde item: eerst een kopie, zodat Ongedaan maken de oude toestand terugzet
        void EditSelected()
        {
            Checkpoint();
            int i = strokes.IndexOf(selected);
            Stroke c = selected.Clone();
            if (i >= 0) strokes[i] = c;
            selected = c;
        }

        public void RecolorSelected(Color c)
        {
            if (selected == null) return;
            EditSelected();
            selected.Color = c;
            Rebuild();
            RefreshUi();
        }

        public void DeleteSelected()
        {
            if (selected == null) return;
            Checkpoint();
            strokes.Remove(selected);
            Deselect();
            Rebuild();
        }

        public void DuplicateSelected()
        {
            if (selected == null) return;
            Checkpoint();
            Stroke c = selected.Clone();
            Render.Move(c, 24, 24);
            strokes.Add(c);
            Rebuild();
            SelectStroke(c);
        }

        Stroke TopHit(PointF p)
        {
            for (int i = strokes.Count - 1; i >= 0; i--)
                if (Render.Hit(strokes[i], p, 6)) return strokes[i];
            return null;
        }

        // ---------- muis
        PointF Constrain(PointF a, PointF b, Tool t)
        {
            if ((ModifierKeys & Keys.Shift) == 0) return b;
            float dx = b.X - a.X, dy = b.Y - a.Y;
            if (t == Tool.Rect || t == Tool.Ellipse)
            {
                float s = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new PointF(a.X + Math.Sign(dx) * s, a.Y + Math.Sign(dy) * s);
            }
            double step = Math.PI / 4;
            double ang = Math.Round(Math.Atan2(dy, dx) / step) * step;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return new PointF((float)(a.X + Math.Cos(ang) * len), (float)(a.Y + Math.Sin(ang) * len));
        }

        float EraserRadius { get { return Math.Max(8, PenWidth * 2); } }

        void EraseAt(PointF p)
        {
            bool removed = false;
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                if (Render.Hit(strokes[i], p, EraserRadius))
                {
                    if (!erasedThisDrag) { Checkpoint(); erasedThisDrag = true; }
                    if (strokes[i] == selected) Deselect();
                    strokes.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed) { MarkChanged(); Rebuild(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!DrawMode) return;
            if (toolbar != null) toolbar.ClosePopups();
            PointF p = e.Location;
            if (CurrentTool == Tool.Focus) return;
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && CurrentTool == Tool.Eraser))
            {
                erasing = true; erasedThisDrag = false; current = null;
                EraseAt(p);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (CurrentTool == Tool.Select)
            {
                if (!StrokesVisible) return;
                Stroke hit = (selected != null && SelectionBox().Contains(p)) ? selected : TopHit(p);
                if (hit == null) { Deselect(); return; }
                if (hit != selected) SelectStroke(hit);
                dragging = true; movedThisDrag = false; dragLast = p;
                if (toolbar != null) toolbar.HideMiniBar();
                return;
            }
            if (CurrentTool == Tool.Text) { OpenTextInput(e.Location); return; }
            if (!StrokesVisible && CurrentTool != Tool.Laser) { StrokesVisible = true; Rebuild(); RefreshUi(); }
            if (CurrentTool == Tool.Stamp)
            {
                Stroke st = new Stroke();
                st.Tool = Tool.Stamp; st.Color = CurrentColor; st.Width = PenWidth; st.FontSize = Render.StampSize(PenWidth);
                st.Pts.Add(p);
                Checkpoint();
                strokes.Add(st);
                CommitToLayer(st);
                return;
            }
            current = new Stroke();
            current.Tool = CurrentTool;
            current.Color = CurrentColor;
            current.Width = PenWidth;
            current.Pts.Add(p);
            if (IsShape(CurrentTool)) current.Pts.Add(p);
            dirty = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!DrawMode) return;
            PointF p = e.Location;
            if (CurrentTool == Tool.Focus) { focusAt = p; dirty = true; return; }
            if (erasing) { EraseAt(p); return; }
            if (dragging && selected != null)
            {
                float dx = p.X - dragLast.X, dy = p.Y - dragLast.Y;
                if (!movedThisDrag)
                {
                    if (dx * dx + dy * dy < 9) return;
                    EditSelected();
                    movedThisDrag = true;
                    lifted = true;
                    Rebuild();
                }
                Render.Move(selected, dx, dy);
                dragLast = p;
                dirty = true;
                return;
            }
            if (current == null) return;
            switch (current.Tool)
            {
                case Tool.Pen: case Tool.Marker: case Tool.Laser:
                    PointF last = current.Pts[current.Pts.Count - 1];
                    float ddx = p.X - last.X, ddy = p.Y - last.Y;
                    if (ddx * ddx + ddy * ddy >= 2.25f) current.Pts.Add(p);
                    break;
                default:
                    current.Pts[1] = Constrain(current.Pts[0], p, current.Tool);
                    break;
            }
            dirty = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (erasing) { erasing = false; return; }
            if (dragging)
            {
                dragging = false;
                if (lifted) { lifted = false; Rebuild(); }
                if (selected != null && toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
                return;
            }
            if (current == null) return;
            Stroke s = current;
            current = null;
            if (s.Tool == Tool.Laser)
            {
                s.Born = DateTime.Now;
                lasers.Add(s);
            }
            else
            {
                Checkpoint();
                strokes.Add(s);
                CommitToLayer(s);
            }
            dirty = true;
        }

        // Muiswiel en knijpen op het touchpad (komt binnen als Ctrl+wiel): dikte, of de grootte van het spotlicht
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!DrawMode) return;
            Wheel(e.Delta);
        }

        // ---------- toetsenbord (alleen als de tekenlaag actief is)
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); return; }
            if (e.Control && e.KeyCode == Keys.Y) { Redo(); return; }
            if (e.Control && e.KeyCode == Keys.S) { SaveExplicit(); return; }
            if (e.Control && e.KeyCode == Keys.D) { DuplicateSelected(); return; }
            if (e.Control || e.Alt) return;
            if (selected != null && !dragging)
            {
                float step = e.Shift ? 10 : 1;
                float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                float dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                if (dx != 0 || dy != 0)
                {
                    EditSelected(); Render.Move(selected, dx, dy); Rebuild();
                    if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
                    e.Handled = true; return;
                }
            }
            switch (e.KeyCode)
            {
                case Keys.P: SetTool(Tool.Pen); break;
                case Keys.M: SetTool(Tool.Marker); break;
                case Keys.L: SetTool(Tool.Laser); break;
                case Keys.N: SetTool(Tool.Line); break;
                case Keys.A: SetTool(Tool.Arrow); break;
                case Keys.R: SetTool(Tool.Rect); break;
                case Keys.O: SetTool(Tool.Ellipse); break;
                case Keys.T: SetTool(Tool.Text); break;
                case Keys.E: SetTool(Tool.Eraser); break;
                case Keys.V: SetTool(Tool.Select); break;
                case Keys.S: SetTool(Tool.Stamp); break;
                case Keys.F: ToggleFocus(); break;
                case Keys.B: CycleBoard(); break;
                case Keys.H: ToggleStrokesVisible(); break;
                case Keys.C: Screenshot(); break;
                case Keys.Delete: case Keys.Back: if (selected != null) DeleteSelected(); else if (e.KeyCode == Keys.Delete) ClearAll(); break;
                case Keys.Oemplus: case Keys.Add: SetWidth(PenWidth + (PenWidth < 6 ? 1 : 2)); break;
                case Keys.OemMinus: case Keys.Subtract: SetWidth(PenWidth - (PenWidth <= 6 ? 1 : 2)); break;
                default:
                    int n = -1;
                    if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) n = e.KeyCode - Keys.D1;
                    if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) n = e.KeyCode - Keys.NumPad1;
                    if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) n = 9;
                    if (n >= 0) SetColor(n);
                    break;
            }
        }

        // ---------- tekst
        void OpenTextInput(Point clientPt)
        {
            CloseTextInput(true);
            float px = 12 + PenWidth * 2.5f;
            Point screenPt = new Point(clientPt.X + vs.X, clientPt.Y + vs.Y);
            PointF strokePt = clientPt;
            Color c = CurrentColor;
            string font = Cfg.FontName;
            bool outline = Cfg.TextOutline;
            textInput = new TextInput(screenPt, Render.MakeFont(px, font), c, delegate(string text)
            {
                textInput = null;
                if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return;
                Stroke s = new Stroke();
                s.Tool = Tool.Text; s.Color = c; s.Width = PenWidth; s.FontSize = px; s.Text = text;
                s.FontName = font; s.Outline = outline;
                s.Pts.Add(strokePt);
                if (!StrokesVisible) { StrokesVisible = true; Rebuild(); }
                Checkpoint();
                strokes.Add(s);
                CommitToLayer(s);
                dirty = true;
            });
            textInput.Owner = this;
            textInput.Show();
            textInput.Activate();
        }

        void CloseTextInput(bool commit)
        {
            if (textInput != null) { TextInput t = textInput; textInput = null; t.Finish(commit); }
        }

        // ---------- schermafbeelding
        bool screenshotBusy;
        public static string ScreenshotDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NexusPen"); } }

        public void Screenshot()
        {
            if (screenshotBusy) return;
            screenshotBusy = true;
            bool hadSel = selected != null;
            if (hadSel) Deselect();
            if (toolbar != null) toolbar.HideForShot();
            RenderFrame();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 150;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                try { CaptureScreen(); }
                finally
                {
                    screenshotBusy = false;
                    if (toolbar != null && !allHidden) toolbar.ShowAfterShot();
                }
            };
            t.Start();
        }

        void CaptureScreen()
        {
            try
            {
                Rectangle r = SystemInformation.VirtualScreen;
                using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                    Directory.CreateDirectory(ScreenshotDir);
                    string file = Path.Combine(ScreenshotDir, "NexusPen_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                    bmp.Save(file, ImageFormat.Png);
                    try { Clipboard.SetImage(bmp); } catch { }
                    if (tray != null) tray.ShowBalloonTip(2500, "Schermafbeelding opgeslagen", file + "\n(ook gekopieerd naar het klembord)", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Schermafbeelding mislukt", ex);
                if (tray != null) tray.ShowBalloonTip(3000, "NexusPen", "Schermafbeelding mislukt: " + ex.Message, ToolTipIcon.Warning);
            }
        }

        public const string HelpText =
            "OVERAL (ook als je niet tekent)\n" +
            "{0}   tekenen aan/uit\n" +
            "{1}   alles verbergen/tonen\n\n" +
            "TIJDENS HET TEKENEN\n" +
            "P pen   M markeerstift   L laser   E gum   T tekst\n" +
            "N lijn   A pijl   R rechthoek   O cirkel\n" +
            "V selecteren en verplaatsen   S stempel   F spotlicht\n" +
            "1 t/m 9 en 0  kleur     + / -  of muiswiel  dikte\n" +
            "Ctrl+Z ongedaan   Ctrl+Y opnieuw   Ctrl+S opslaan\n" +
            "Delete alles wissen (of het geselecteerde item)\n" +
            "B bord wisselen   H tekeningen even verbergen\n" +
            "C schermafbeelding (map Afbeeldingen\\NexusPen + klembord)\n" +
            "Esc  terug naar de muis (werkt altijd)\n\n" +
            "Rechtermuisknop ingedrukt houden = gummen.\n" +
            "Shift bij lijn of pijl = rechte hoeken, bij rechthoek of cirkel = vierkant of rond.\n" +
            "Selecteren: pijltjestoetsen schuiven, Ctrl+D kopie.\n" +
            "Tekst: Enter = klaar, Shift+Enter = nieuwe regel, Esc = annuleren.";

        public void ShowHelp()
        {
            if (DrawMode) SetTool(Tool.Mouse);
            MessageBox.Show(toolbar, string.Format(HelpText, Cfg.HotkeyDraw, Cfg.HotkeyHide) +
                "\n\nTekening kwijt? Rechtsklik op het icoon bij de klok > Vorige tekening terughalen.",
                "NexusPen sneltoetsen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    // ------------------------------------------------------------------ Tekstinvoer
    class TextInput : Form
    {
        TextBox tb;
        Action<string> done;
        bool finished;

        public TextInput(Point screenPt, Font font, Color color, Action<string> onDone)
        {
            done = onDone;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            bool light = color.GetBrightness() > 0.75f;
            BackColor = light ? Color.FromArgb(60, 60, 60) : Color.FromArgb(245, 245, 245);
            Padding = new Padding(1);
            tb = new TextBox();
            tb.Multiline = true;
            tb.AcceptsReturn = true;
            tb.WordWrap = false;
            tb.BorderStyle = BorderStyle.None;
            tb.Font = font;
            tb.ForeColor = color;
            tb.BackColor = BackColor;
            tb.Dock = DockStyle.Fill;
            Controls.Add(tb);
            Location = new Point(screenPt.X - 2, screenPt.Y - 2);
            Grow();
            tb.TextChanged += delegate { Grow(); };
            tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Finish(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
            };
            Deactivate += delegate { Finish(true); };
        }

        void Grow()
        {
            Size sz = TextRenderer.MeasureText((tb.Text.Length == 0 ? "Typ hier" : tb.Text) + "WW", tb.Font);
            Size = new Size(Math.Max(120, sz.Width + 8), Math.Max(tb.Font.Height + 8, sz.Height + 8));
        }

        public void Finish(bool commit)
        {
            if (finished) return;
            finished = true;
            string text = commit ? tb.Text : null;
            Close();
            if (done != null) done(text);
        }
    }


    // ------------------------------------------------------------------ Basis voor de werkbalk en de kleine vensters ernaast
    class Item
    {
        public RectangleF R;
        public string Tip, Icon;
        public Action Click, RightClick;
        public Func<bool> Active, Lit;
        public Action<Graphics, Item, bool> Paint;   // eigen tekenwerk (kleurbolletje, logo, schuifje)
        public bool Muted;
    }

    class BarForm : Form
    {
        protected Overlay ov;
        protected List<Item> items = new List<Item>();
        protected Item hover;
        protected ToolTip tip = new ToolTip();
        public float S = 1;
        protected bool draggable;
        protected bool vertical;

        public BarForm(Overlay o)
        {
            ov = o;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            tip.InitialDelay = 350;
        }

        protected Theme T { get { return ov.Theme; } }
        protected float Radius { get { return (ov.Cfg.Square ? 6 : 14) * S; } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= W.WS_EX_NOACTIVATE | W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(W.TABLET_FLAGS));
            W.HideFromCapture(Handle, ov.Cfg.HideFromCapture);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W.WM_TABLET_QUERYSYSTEMGESTURESTATUS) { m.Result = new IntPtr(W.TABLET_FLAGS); return; }
            if (m.Msg == W.WM_DPICHANGED)
            {
                // Naar een scherm met andere schaal gesleept: opnieuw opbouwen op de juiste grootte
                int dpi = m.WParam.ToInt32() & 0xFFFF;
                S = Math.Max(1f, dpi / 96f);
                W.RECT r = (W.RECT)Marshal.PtrToStructure(m.LParam, typeof(W.RECT));
                Location = new Point(r.Left, r.Top);
                Rebuild();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void ApplyCaptureSetting() { if (IsHandleCreated) W.HideFromCapture(Handle, ov.Cfg.HideFromCapture); }

        protected virtual void Build() { }

        public virtual void Rebuild()
        {
            hover = null;
            items.Clear();
            Build();
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), Radius))
                Region = new Region(gp);
            Invalidate();
        }

        protected Item Add(RectangleF r, string tipText, Action click)
        {
            Item it = new Item();
            it.R = r; it.Tip = tipText; it.Click = click;
            items.Add(it);
            return it;
        }

        protected Item Button(RectangleF r, string icon, string tipText, Action click, Func<bool> active, Func<bool> lit)
        {
            Item it = Add(r, tipText, click);
            it.Icon = icon; it.Active = active; it.Lit = lit;
            return it;
        }

        protected float IconSize
        {
            get { int i = ov.Cfg.SizeIndex; return (i == 0 ? 18 : i == 2 ? 24 : 20) * S; }
        }

        protected float StrokeW { get { return T.Thick ? 2.2f : 1.8f; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme t = T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(t.Bar);
            float bw = (t.Thick ? 2 : 1) * S;
            using (Pen p = new Pen(t.Border, bw))
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(bw / 2, bw / 2, ClientSize.Width - bw - 0.5f, ClientSize.Height - bw - 0.5f), Radius - bw / 2))
                g.DrawPath(p, gp);
            PaintExtra(g);
            foreach (Item it in items) PaintItem(g, it);
        }

        protected virtual void PaintExtra(Graphics g) { }

        protected void PaintItem(Graphics g, Item it)
        {
            Theme t = T;
            bool active = it.Active != null && it.Active();
            bool hov = it == hover;
            if (it.Paint != null) { it.Paint(g, it, hov); return; }
            if (active || hov)
                using (SolidBrush b = new SolidBrush(active ? t.Accent : t.Hover))
                using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S))
                    g.FillPath(b, gp);
            Color c = active ? t.OnAccent : (it.Lit != null && it.Lit()) ? t.Accent : it.Muted ? t.Muted : t.Icon;
            if (it.Icon != null) Icons.Draw(g, it.Icon, it.R, IconSize, c, StrokeW);
        }

        protected Item ItemAt(Point p)
        {
            for (int i = items.Count - 1; i >= 0; i--) if (items[i].R.Contains(p)) return items[i];
            return null;
        }

        protected virtual bool OnPress(MouseEventArgs e) { return false; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (OnPress(e)) return;
            Item it = ItemAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (it != null && it.RightClick != null) { it.RightClick(); Invalidate(); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (it != null) { if (it.Click != null) it.Click(); if (!IsDisposed) Invalidate(); return; }
            if (draggable)
            {
                W.ReleaseCapture();
                W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item it = ItemAt(e.Location);
            if (it != hover)
            {
                hover = it;
                Invalidate();
                if (it != null && !string.IsNullOrEmpty(it.Tip))
                {
                    Point at = vertical
                        ? new Point((int)(it.R.Right + 10 * S), (int)(it.R.Y + 6 * S))
                        : new Point((int)it.R.X, (int)(it.R.Bottom + 8 * S));
                    tip.Show(it.Tip, this, at, 3000);
                }
                else tip.Hide(this);
            }
            Cursor = it != null ? Cursors.Hand : draggable ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ov.Wheel(e.Delta);
        }

        // Kleurbolletje met witte ring als het gekozen is
        protected void Swatch(Graphics g, RectangleF r, Color c, bool chosen, float d)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            Theme t = T;
            if (chosen)
                using (SolidBrush b = new SolidBrush(t.IsLight ? Color.Black : Color.White))
                    g.FillEllipse(b, cx - d / 2 - 3.5f * S, cy - d / 2 - 3.5f * S, d + 7 * S, d + 7 * S);
            if (chosen)
                using (SolidBrush b = new SolidBrush(t.Bar))
                    g.FillEllipse(b, cx - d / 2 - 2 * S, cy - d / 2 - 2 * S, d + 4 * S, d + 4 * S);
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            if (Theme.Luma(c) > 0.9 || Theme.Luma(c) < 0.12)
                using (Pen p = new Pen(t.IsLight ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(90, 255, 255, 255), 1)) g.DrawEllipse(p, cx - d / 2, cy - d / 2, d, d);
        }

        protected void Label(Graphics g, string text, RectangleF r, Color c, float px, bool bold, StringAlignment align)
        {
            using (Font f = Fonts.Make(UiFont.Name, px * S, bold ? FontStyle.Bold : FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, f, b, r, sf);
            }
        }

        // Klein venster naast de werkbalk plaatsen, binnen het scherm
        public void PlaceNear(Rectangle anchor, bool belowOrRight)
        {
            Rectangle wa = Screen.FromRectangle(anchor).WorkingArea;
            int gap = (int)(8 * S);
            int x, y;
            if (!vertical)
            {
                x = anchor.X + anchor.Width / 2 - Width / 2;
                y = anchor.Bottom + gap;
                if (y + Height > wa.Bottom) y = anchor.Top - gap - Height;
            }
            else
            {
                x = anchor.Right + gap;
                y = anchor.Y + anchor.Height / 2 - Height / 2;
                if (x + Width > wa.Right) x = anchor.Left - gap - Width;
            }
            x = Math.Max(wa.Left + 4, Math.Min(wa.Right - Width - 4, x));
            y = Math.Max(wa.Top + 4, Math.Min(wa.Bottom - Height - 4, y));
            Location = new Point(x, y);
        }

        public void SetVertical(bool v) { vertical = v; }
    }

    // Lettertype voor de knoppen en instellingen: Nunito (huisstijl) als het er is, anders Segoe UI
    static class UiFont
    {
        static string name;
        public static string Name
        {
            get
            {
                if (name == null) name = Render.IsInstalled("Nunito") ? "Nunito" : "Segoe UI";
                return name;
            }
        }
    }

    // ------------------------------------------------------------------ De werkbalk (Studio-balk, liggend of staand)
    class Toolbar : BarForm
    {
        ColorPopup colorPopup;
        ShapePopup shapePopup;
        MiniBar miniBar;
        Lipje lipje;
        Item colorItem, shapeItem;
        bool docked, hiddenAll, hiddenShot;
        System.Windows.Forms.Timer flash;

        public Toolbar(Overlay o) : base(o)
        {
            draggable = true;
            Text = "NexusPen werkbalk";
            Point start = (ov.Cfg.ToolbarX != -1 || ov.Cfg.ToolbarY != -1)
                ? new Point(ov.Cfg.ToolbarX + 20, ov.Cfg.ToolbarY + 20)
                : new Point(Screen.PrimaryScreen.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 40);
            S = W.DpiScaleAt(start);
            vertical = ov.Cfg.Vertical;
            Opacity = OpacityValue;
            Rebuild();
            if (ov.Cfg.ToolbarX != -1 || ov.Cfg.ToolbarY != -1) Location = new Point(ov.Cfg.ToolbarX, ov.Cfg.ToolbarY);
            else ResetPosition();
            EnsureOnScreen();
            LocationChanged += delegate { if (Visible) { ov.Cfg.ToolbarX = Left; ov.Cfg.ToolbarY = Top; } ClosePopups(); };
            flash = new System.Windows.Forms.Timer();
            flash.Interval = 1700;
            flash.Tick += delegate { flash.Stop(); Invalidate(); };
        }

        double OpacityValue { get { int i = ov.Cfg.OpacityIndex; return i == 1 ? 0.9 : i == 2 ? 0.8 : 1.0; } }

        public void ResetPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (vertical) Location = new Point(wa.Left + (int)(12 * S), wa.Top + (wa.Height - Height) / 2);
            else Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (int)(12 * S));
            if (docked) Undock();
        }

        public void EnsureOnScreen()
        {
            Rectangle me = Bounds;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(new Rectangle(me.X + 10, me.Y + 10, 20, 20))) return;
            ResetPosition();
        }

        // Thema, grootte, stand of hoeken veranderd
        public void Restyle()
        {
            Point keep = Location;
            vertical = ov.Cfg.Vertical;
            Opacity = OpacityValue;
            ClosePopups();
            HideMiniBar();
            Rebuild();
            Location = keep;
            EnsureOnScreen();
            ApplyCaptureSetting();
            if (lipje != null) { lipje.ApplyCaptureSetting(); lipje.Restyle(); }
        }

        public void Refresh2()
        {
            Invalidate();
            if (colorPopup != null && colorPopup.Visible) colorPopup.Invalidate();
            if (shapePopup != null && shapePopup.Visible) shapePopup.Invalidate();
            if (miniBar != null && miniBar.Visible) miniBar.Invalidate();
        }

        public void KeepAllOnTop()
        {
            foreach (Form f in new Form[] { this, colorPopup, shapePopup, miniBar, lipje })
                if (f != null && f.Visible && f.IsHandleCreated) W.KeepOnTop(f.Handle);
        }

        // ---- opbouw
        float BW { get { int i = ov.Cfg.SizeIndex; return (i == 0 ? 32 : i == 2 ? 46 : 38) * S; } }

        protected override void Build()
        {
            Overlay o = ov;
            float bw = BW, bh = vertical ? bw - 6 * S : bw, gap = 2 * S;
            float padMain = (vertical ? 5 : 6) * S, padCross = (vertical ? 8 : 5) * S;
            // vertical: x = dwars, y = lang. horizontaal: x = lang, y = dwars
            float pos = padMain, cross = padCross;
            float thick = vertical ? bw : bh;

            // greep (6 puntjes)
            float gripLen = vertical ? (4 + 12 + 8) * S : (4 + 12 + 6) * S;
            Item grip = Add(Rect(pos, cross, gripLen, thick), null, null);
            grip.Click = null; grip.Paint = PaintGrip;
            items.Remove(grip); gripItem = grip;
            pos += gripLen;

            // logo: alles in- of uitklappen
            float lb = bw;
            Item logo = Add(Rect(pos, cross, vertical ? bh + 4 * S : lb, thick),
                o.Cfg.Collapsed ? "Werkbalk uitklappen" : "Werkbalk inklappen (alleen het logo blijft staan)",
                delegate { o.Cfg.Collapsed = !o.Cfg.Collapsed; o.Cfg.Save(); ClosePopups(); Point k = Location; Rebuild(); Location = k; EnsureOnScreen(); });
            logo.Paint = PaintLogo;
            pos += vertical ? bh + 4 * S : lb;

            if (!o.Cfg.Collapsed)
            {
                Group(ref pos, cross, bw, bh, thick, gap);
                AddTool(ref pos, cross, bw, bh, gap, "muis", "Muis: gewoon klikken op je scherm (Esc)", NexusPen.Tool.Mouse);
                AddTool(ref pos, cross, bw, bh, gap, "selecteer", "Selecteren en verplaatsen (V). Klik iets aan en sleep het", NexusPen.Tool.Select);

                Group(ref pos, cross, bw, bh, thick, gap);
                AddTool(ref pos, cross, bw, bh, gap, "pen", "Pen (P)", NexusPen.Tool.Pen);
                AddTool(ref pos, cross, bw, bh, gap, "markeer", "Markeerstift (M)", NexusPen.Tool.Marker);
                AddTool(ref pos, cross, bw, bh, gap, "laser", "Laserpen, verdwijnt vanzelf (L)", NexusPen.Tool.Laser);

                Group(ref pos, cross, bw, bh, thick, gap);
                shapeItem = Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "vormen", "Vormen: lijn, pijl, rechthoek, cirkel",
                    delegate { ToggleShapes(); }, delegate { return o.DrawMode && Overlay.IsShape(o.CurrentTool); }, delegate { return shapePopup != null && shapePopup.Visible; });
                shapeItem.Paint = PaintShapeButton;
                pos += Long(bw, bh) + gap;
                AddTool(ref pos, cross, bw, bh, gap, "tekst", "Tekst (T)", NexusPen.Tool.Text);
                AddTool(ref pos, cross, bw, bh, gap, "gum", "Gum (E), of rechtermuisknop ingedrukt", NexusPen.Tool.Eraser);

                Group(ref pos, cross, bw, bh, thick, gap);
                colorItem = Add(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "Kleur en dikte (1 t/m 0, muiswiel)", delegate { ToggleColors(); });
                colorItem.Paint = PaintColorButton;
                pos += Long(bw, bh) + gap;
                AddTool(ref pos, cross, bw, bh, gap, "stempel", "Stempel: zet een vinkje waar je klikt (S)", NexusPen.Tool.Stamp);
                Item f = Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "focus", "Spotlicht: alleen rond je muis is het licht (F). Muiswiel = groter of kleiner",
                    delegate { ClosePopups(); o.ToggleFocus(); }, delegate { return o.DrawMode && o.CurrentTool == NexusPen.Tool.Focus; }, null);
                pos += Long(bw, bh) + gap;

                Group(ref pos, cross, bw, bh, thick, gap);
                Item u = Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "undo", "Ongedaan maken (Ctrl+Z). Rechtsklik = opnieuw (Ctrl+Y)",
                    delegate { o.Undo(); }, null, null);
                u.RightClick = delegate { o.Redo(); };
                pos += Long(bw, bh) + gap;
                Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "bord", "Bord: whiteboard, krijtbord of uit (B)",
                    delegate { ClosePopups(); o.CycleBoard(); }, null, delegate { return o.BoardMode != Board.None && o.DrawMode; });
                pos += Long(bw, bh) + gap;
                Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "foto", "Schermafbeelding (C). Komt in Afbeeldingen\\NexusPen en op het klembord",
                    delegate { ClosePopups(); o.Screenshot(); }, null, null);
                pos += Long(bw, bh) + gap;

                Group(ref pos, cross, bw, bh, thick, gap);
                Item sv = Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "opslaan",
                    "Opslaan (Ctrl+S). Je tekening en instellingen staan er de volgende keer weer",
                    delegate { ClosePopups(); if (o.SaveExplicit()) { flash.Stop(); flash.Start(); } }, null, null);
                sv.Paint = PaintSave;
                pos += Long(bw, bh) + gap;
                Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "oog", "Alle tekeningen verbergen of weer tonen (H)",
                    delegate { ClosePopups(); o.ToggleStrokesVisible(); }, null, delegate { return !o.StrokesVisible; }).Paint = PaintEye;
                pos += Long(bw, bh) + gap;
                Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), "instellingen", "Instellingen: thema, werkbalk, tekst en sneltoetsen",
                    delegate { ClosePopups(); o.ShowSettings(); }, null, null);
                pos += Long(bw, bh) + gap;
                Item weg = Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), vertical ? "links" : "op",
                    "Werkbalk wegschuiven naar de rand. Klik op het lipje om hem terug te halen", delegate { DockAway(); }, null, null);
                weg.Muted = true;
                pos += Long(bw, bh);
            }
            pos += padMain;
            float total = thick + padCross * 2;
            ClientSize = vertical ? new Size((int)Math.Ceiling(total), (int)Math.Ceiling(pos)) : new Size((int)Math.Ceiling(pos), (int)Math.Ceiling(total));
        }

        Item gripItem;

        float Long(float bw, float bh) { return vertical ? bh : bw; }
        float Cross(float bw, float bh) { return vertical ? bw : bh; }

        RectangleF Rect(float pos, float cross, float len, float thick)
        {
            return vertical ? new RectangleF(cross, pos, thick, len) : new RectangleF(pos, cross, len, thick);
        }

        void Group(ref float pos, float cross, float bw, float bh, float thick, float gap)
        {
            float m = (vertical ? 4 : 5) * S;
            float sepPos = pos + m;
            Item sep = Add(vertical ? new RectangleF(cross + thick / 2 - 11 * S, sepPos, 22 * S, 1 * S) : new RectangleF(sepPos, cross + thick / 2 - 11 * S, 1 * S, 22 * S), null, null);
            sep.Paint = delegate(Graphics g, Item it, bool h) { using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, it.R); };
            items.Remove(sep); seps.Add(sep);
            pos += m * 2 + 1 * S;
        }

        List<Item> seps = new List<Item>();

        void AddTool(ref float pos, float cross, float bw, float bh, float gap, string icon, string tipText, Tool t)
        {
            Overlay o = ov;
            Button(Rect(pos, cross, Long(bw, bh), Cross(bw, bh)), icon, tipText,
                delegate { ClosePopups(); o.SetTool(t); },
                delegate { return t == NexusPen.Tool.Mouse ? !o.DrawMode : o.DrawMode && o.CurrentTool == t; }, null);
            pos += Long(bw, bh) + gap;
        }

        protected override void PaintExtra(Graphics g)
        {
            if (gripItem != null) PaintGrip(g, gripItem, false);
            foreach (Item s in seps) s.Paint(g, s, false);
        }

        public override void Rebuild() { seps.Clear(); gripItem = null; base.Rebuild(); }

        void PaintGrip(Graphics g, Item it, bool h)
        {
            int cols = vertical ? 3 : 2, rows = vertical ? 2 : 3;
            float d = 4 * S, gp = 4 * S;
            float w = cols * d + (cols - 1) * gp, hh = rows * d + (rows - 1) * gp;
            float x0 = vertical ? it.R.X + (it.R.Width - w) / 2 : it.R.X + 4 * S;
            float y0 = vertical ? it.R.Y + 4 * S : it.R.Y + (it.R.Height - hh) / 2;
            using (SolidBrush b = new SolidBrush(T.IsLight ? ColorTranslator.FromHtml("#A1A1AA") : ColorTranslator.FromHtml("#71717A")))
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                        g.FillEllipse(b, x0 + c * (d + gp), y0 + r * (d + gp), d, d);
        }

        void PaintLogo(Graphics g, Item it, bool h)
        {
            Theme t = T;
            if (h)
                using (SolidBrush b = new SolidBrush(t.Hover))
                using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
            float size = (float)Math.Round(BW * 26f / 38f);
            RectangleF r = new RectangleF(it.R.X + (it.R.Width - size) / 2, it.R.Y + (it.R.Height - size) / 2, size, size);
            Logo.Draw(g, r, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
        }

        void PaintColorButton(Graphics g, Item it, bool h)
        {
            Theme t = T;
            bool open = colorPopup != null && colorPopup.Visible;
            using (SolidBrush b = new SolidBrush(h || open ? (t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46")) : t.Chip))
            using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
            float d = 18 * S * (BW / (38 * S));
            float cx = it.R.X + it.R.Width / 2, cy = it.R.Y + it.R.Height / 2;
            using (SolidBrush b = new SolidBrush(t.IsLight ? Color.Black : Color.White)) g.FillEllipse(b, cx - d / 2 - 3.5f * S, cy - d / 2 - 3.5f * S, d + 7 * S, d + 7 * S);
            using (SolidBrush b = new SolidBrush(t.Chip)) g.FillEllipse(b, cx - d / 2 - 2 * S, cy - d / 2 - 2 * S, d + 4 * S, d + 4 * S);
            using (SolidBrush b = new SolidBrush(ov.CurrentColor)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        }

        void PaintShapeButton(Graphics g, Item it, bool h)
        {
            // laat zien welke vorm je het laatst gebruikte
            string ic = ov.LastShape == NexusPen.Tool.Line ? "lijn" : ov.LastShape == NexusPen.Tool.Rect ? "rechthoek" : ov.LastShape == NexusPen.Tool.Ellipse ? "cirkel" : ov.LastShape == NexusPen.Tool.Arrow ? "pijl" : "vormen";
            bool active = it.Active();
            it.Icon = active ? ic : "vormen";
            it.Paint = null;
            PaintItem(g, it);
            it.Paint = PaintShapeButton;
        }

        void PaintSave(Graphics g, Item it, bool h)
        {
            bool justSaved = flash.Enabled;
            it.Icon = justSaved ? "vink" : "opslaan";
            it.Lit = justSaved ? (Func<bool>)delegate { return true; } : null;
            it.Paint = null;
            PaintItem(g, it);
            it.Paint = PaintSave;
        }

        void PaintEye(Graphics g, Item it, bool h)
        {
            it.Icon = ov.StrokesVisible ? "oog" : "oogopen";
            it.Paint = null;
            PaintItem(g, it);
            it.Paint = PaintEye;
        }

        Rectangle ScreenRect(Item it)
        {
            Point p = PointToScreen(new Point((int)it.R.X, (int)it.R.Y));
            return new Rectangle(p, new Size((int)it.R.Width, (int)it.R.Height));
        }

        // ---- kleine vensters
        void ToggleColors()
        {
            bool open = colorPopup != null && colorPopup.Visible;
            ClosePopups();
            if (open) return;
            if (colorPopup == null || colorPopup.IsDisposed) { colorPopup = new ColorPopup(ov); colorPopup.Owner = this; }
            colorPopup.S = S; colorPopup.SetVertical(vertical); colorPopup.Rebuild();
            colorPopup.PlaceNear(ScreenRect(colorItem), true);
            colorPopup.Show();
            Invalidate();
        }

        void ToggleShapes()
        {
            bool open = shapePopup != null && shapePopup.Visible;
            ClosePopups();
            if (open) return;
            if (shapePopup == null || shapePopup.IsDisposed) { shapePopup = new ShapePopup(ov, this); shapePopup.Owner = this; }
            shapePopup.S = S; shapePopup.SetVertical(vertical); shapePopup.Rebuild();
            shapePopup.PlaceNear(ScreenRect(shapeItem), true);
            shapePopup.Show();
            Invalidate();
        }

        public void ClosePopups()
        {
            if (colorPopup != null && colorPopup.Visible) colorPopup.Hide();
            if (shapePopup != null && shapePopup.Visible) shapePopup.Hide();
            Invalidate();
        }

        public void ShowMiniBar(Rectangle sel)
        {
            if (hiddenAll || hiddenShot) return;
            if (miniBar == null || miniBar.IsDisposed) { miniBar = new MiniBar(ov); miniBar.Owner = this; }
            miniBar.S = W.DpiScaleAt(sel.Location);
            miniBar.Rebuild();
            Rectangle wa = Screen.FromRectangle(sel).WorkingArea;
            int x = sel.Right - miniBar.Width, y = sel.Top - miniBar.Height - (int)(8 * miniBar.S);
            if (y < wa.Top + 4) y = sel.Bottom + (int)(8 * miniBar.S);
            x = Math.Max(wa.Left + 4, Math.Min(wa.Right - miniBar.Width - 4, x));
            y = Math.Max(wa.Top + 4, Math.Min(wa.Bottom - miniBar.Height - 4, y));
            miniBar.Location = new Point(x, y);
            if (!miniBar.Visible) miniBar.Show();
            W.KeepOnTop(miniBar.Handle);
        }

        public void HideMiniBar() { if (miniBar != null && miniBar.Visible) miniBar.Hide(); }

        // ---- wegschuiven naar de rand
        void DockAway()
        {
            ClosePopups();
            docked = true;
            if (lipje == null || lipje.IsDisposed) { lipje = new Lipje(ov, this); }
            lipje.Place(Bounds, vertical);
            lipje.Show();
            Hide();
        }

        public void Undock()
        {
            docked = false;
            if (lipje != null) lipje.Hide();
            if (!hiddenAll) { Show(); W.KeepOnTop(Handle); }
        }

        public void HideAll()
        {
            hiddenAll = true;
            ClosePopups(); HideMiniBar();
            Hide();
            if (lipje != null) lipje.Hide();
        }

        public void ShowAgain()
        {
            hiddenAll = false;
            if (docked && lipje != null) lipje.Show(); else Show();
        }

        bool shotWasVisible, shotLipje;

        public void HideForShot()
        {
            hiddenShot = true;
            shotWasVisible = Visible;
            shotLipje = lipje != null && lipje.Visible;
            ClosePopups(); HideMiniBar();
            if (Visible) Hide();
            if (shotLipje) lipje.Hide();
        }

        public void ShowAfterShot()
        {
            hiddenShot = false;
            if (shotWasVisible) Show();
            if (shotLipje) lipje.Show();
        }
    }

    // ------------------------------------------------------------------ Kleur en dikte
    class ColorPopup : BarForm
    {
        RectangleF track;
        bool sliding;
        const float MaxW = 40;

        public ColorPopup(Overlay o) : base(o) { Text = "NexusPen kleur"; }

        protected override void Build()
        {
            Overlay o = ov;
            float pad = 14 * S, sw = 28 * S, gapX = 12 * S, rowGap = 12 * S;
            float w = pad * 2 + sw * 5 + gapX * 4;
            float y = pad;
            // Fel / Zacht
            float segW = (w - pad * 2 - 6 * S) / 2;
            for (int i = 0; i < Palette.Names.Length; i++)
            {
                int idx = i;
                Item it = Add(new RectangleF(pad + 3 * S + i * segW, y + 3 * S, segW, 28 * S), i == 0 ? "Felle kleuren" : "Zachte kleuren", delegate { o.SetPalette(idx); Invalidate(); });
                it.Paint = delegate(Graphics g, Item item, bool h)
                {
                    Theme t = T;
                    bool on = o.PaletteIndex == idx;
                    if (on)
                    {
                        using (SolidBrush b = new SolidBrush(t.IsLight ? Color.White : ColorTranslator.FromHtml("#3F3F46")))
                        using (GraphicsPath gp = Svg.RoundRect(item.R, 7 * S)) g.FillPath(b, gp);
                        using (Pen p = new Pen(t.Accent, 1.5f * S))
                        using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(item.R, -0.75f * S, -0.75f * S), 7 * S)) g.DrawPath(p, gp);
                    }
                    Label(g, Palette.Names[idx], item.R, on ? t.Text : t.Muted, 12, true, StringAlignment.Center);
                };
            }
            segBox = new RectangleF(pad, y, w - pad * 2, 34 * S);
            y += 34 * S + 14 * S;
            for (int i = 0; i < 10; i++)
            {
                int idx = i;
                RectangleF r = new RectangleF(pad + (i % 5) * (sw + gapX), y + (i / 5) * (sw + rowGap), sw, sw);
                Item it = Add(RectangleF.Inflate(r, 4 * S, 4 * S), "", delegate { o.SetColor(idx); Invalidate(); });
                it.Paint = delegate(Graphics g, Item item, bool h)
                {
                    RectangleF rr = RectangleF.Inflate(item.R, -4 * S, -4 * S);
                    Swatch(g, rr, Palette.Sets[o.PaletteIndex][idx], o.ColorIndex == idx, sw * (h ? 1.06f : 1f));
                };
                it.Tip = null;
                tipFor[it] = idx;
            }
            y += sw * 2 + rowGap + 16 * S;
            // dikte
            labelRow = new RectangleF(pad, y, w - pad * 2, 20 * S);
            track = new RectangleF(pad + 46 * S, y + 8 * S, w - pad * 2 - 46 * S - 34 * S, 4 * S);
            y += 20 * S + pad;
            ClientSize = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(y));
        }

        RectangleF segBox, labelRow;
        Dictionary<Item, int> tipFor = new Dictionary<Item, int>();

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (sliding) { Slide(e.X); return; }
            Item before = hover;
            base.OnMouseMove(e);
            int idx;
            if (hover != before && hover != null && tipFor.TryGetValue(hover, out idx))
                tip.Show(Palette.Labels[ov.PaletteIndex][idx] + "  (toets " + ((idx + 1) % 10) + ")", this, new Point((int)hover.R.X, (int)(hover.R.Bottom + 4 * S)), 2500);
        }

        protected override bool OnPress(MouseEventArgs e)
        {
            RectangleF hit = RectangleF.Inflate(track, 10 * S, 10 * S);
            if (e.Button == MouseButtons.Left && hit.Contains(e.Location)) { sliding = true; Capture = true; Slide(e.X); return true; }
            return false;
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (sliding) { sliding = false; Capture = false; } }

        void Slide(int x)
        {
            float f = Math.Max(0, Math.Min(1, (x - track.X) / track.Width));
            ov.SetWidth((float)Math.Round(1 + f * (MaxW - 1)));
            Invalidate();
        }

        protected override void PaintExtra(Graphics g)
        {
            Theme t = T;
            using (SolidBrush b = new SolidBrush(t.Chip))
            using (GraphicsPath gp = Svg.RoundRect(segBox, 9 * S)) g.FillPath(b, gp);
            Label(g, "Dikte", new RectangleF(labelRow.X, labelRow.Y, 46 * S, labelRow.Height), t.Muted, 12, true, StringAlignment.Near);
            Label(g, ((int)ov.PenWidth).ToString(), new RectangleF(labelRow.Right - 30 * S, labelRow.Y, 30 * S, labelRow.Height), t.Text, 13, true, StringAlignment.Far);
            float f = Math.Max(0, Math.Min(1, (ov.PenWidth - 1) / (MaxW - 1)));
            using (SolidBrush b = new SolidBrush(t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46")))
            using (GraphicsPath gp = Svg.RoundRect(track, 2 * S)) g.FillPath(b, gp);
            using (SolidBrush b = new SolidBrush(t.Accent))
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * f), track.Height), 2 * S)) g.FillPath(b, gp);
            float kx = track.X + track.Width * f, ky = track.Y + track.Height / 2;
            using (SolidBrush b = new SolidBrush(t.IsLight ? Color.White : Color.White)) g.FillEllipse(b, kx - 8 * S, ky - 8 * S, 16 * S, 16 * S);
            if (t.IsLight) using (Pen p = new Pen(ColorTranslator.FromHtml("#D4D4D8"), 1)) g.DrawEllipse(p, kx - 8 * S, ky - 8 * S, 16 * S, 16 * S);
        }
    }

    // ------------------------------------------------------------------ Vormen: lijn, pijl, rechthoek, cirkel
    class ShapePopup : BarForm
    {
        Toolbar bar;
        public ShapePopup(Overlay o, Toolbar b) : base(o) { bar = b; Text = "NexusPen vormen"; }

        protected override void Build()
        {
            Overlay o = ov;
            float bw = (ov.Cfg.SizeIndex == 0 ? 32 : ov.Cfg.SizeIndex == 2 ? 46 : 38) * S, pad = 6 * S, gap = 2 * S;
            string[] icons = { "lijn", "pijl", "rechthoek", "cirkel" };
            string[] tips = { "Lijn (N). Shift = recht", "Pijl (A). Shift = recht", "Rechthoek (R). Shift = vierkant", "Cirkel (O). Shift = rond" };
            Tool[] tools = { NexusPen.Tool.Line, NexusPen.Tool.Arrow, NexusPen.Tool.Rect, NexusPen.Tool.Ellipse };
            for (int i = 0; i < 4; i++)
            {
                Tool t = tools[i];
                RectangleF r = vertical ? new RectangleF(pad, pad + i * (bw + gap), bw, bw) : new RectangleF(pad + i * (bw + gap), pad, bw, bw);
                Button(r, icons[i], tips[i], delegate { o.SetTool(t); bar.ClosePopups(); },
                    delegate { return o.DrawMode && o.CurrentTool == t; }, null);
            }
            float len = pad * 2 + bw * 4 + gap * 3, th = pad * 2 + bw;
            ClientSize = vertical ? new Size((int)th, (int)len) : new Size((int)len, (int)th);
        }
    }

    // ------------------------------------------------------------------ Balkje bij een geselecteerd item
    class MiniBar : BarForm
    {
        public MiniBar(Overlay o) : base(o) { Text = "NexusPen selectie"; }

        protected override void Build()
        {
            Overlay o = ov;
            float pad = 6 * S, sw = 26 * S, d = 16 * S;
            float x = pad;
            for (int i = 0; i < 10; i++)
            {
                int idx = i;
                Item it = Add(new RectangleF(x, pad, sw, sw), Palette.Labels[o.PaletteIndex][idx], delegate { o.RecolorSelected(Palette.Sets[o.PaletteIndex][idx]); Invalidate(); });
                it.Paint = delegate(Graphics g, Item item, bool h) { Swatch(g, item.R, Palette.Sets[o.PaletteIndex][idx], false, h ? d + 2 * S : d); };
                x += sw;
            }
            x += 5 * S;
            sepX = x; x += 6 * S;
            Button(new RectangleF(x, pad, sw + 4 * S, sw), "dupliceer", "Kopie maken (Ctrl+D)", delegate { o.DuplicateSelected(); }, null, null);
            x += sw + 4 * S;
            Button(new RectangleF(x, pad, sw + 4 * S, sw), "prullenbak", "Verwijderen (Delete)", delegate { o.DeleteSelected(); }, null, null);
            x += sw + 4 * S + pad;
            ClientSize = new Size((int)x, (int)(sw + pad * 2));
        }

        float sepX;

        protected override void PaintExtra(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, sepX, (ClientSize.Height - 18 * S) / 2, 1 * S, 18 * S);
        }

        protected override bool OnPress(MouseEventArgs e) { return false; }
    }

    // ------------------------------------------------------------------ Lipje aan de schermrand als de werkbalk weggeschoven is
    class Lipje : BarForm
    {
        Toolbar bar;
        bool vert;

        public Lipje(Overlay o, Toolbar b) : base(o) { bar = b; Text = "NexusPen lipje"; }

        public void Place(Rectangle barBounds, bool vertical)
        {
            vert = vertical;
            S = W.DpiScaleAt(barBounds.Location);
            Rebuild();
            Screen sc = Screen.FromRectangle(barBounds);
            Rectangle bnd = sc.Bounds;
            if (vertical) Location = new Point(bnd.Left, Math.Max(bnd.Top, Math.Min(bnd.Bottom - Height, barBounds.Top + barBounds.Height / 2 - Height / 2)));
            else Location = new Point(Math.Max(bnd.Left, Math.Min(bnd.Right - Width, barBounds.Left + barBounds.Width / 2 - Width / 2)), bnd.Top);
        }

        public void Restyle() { Point k = Location; Rebuild(); Location = k; }

        protected override void Build()
        {
            ClientSize = vert ? new Size((int)(22 * S), (int)(76 * S)) : new Size((int)(84 * S), (int)(22 * S));
            Item it = Add(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), "Werkbalk tevoorschijn halen", delegate { bar.Undock(); });
            it.Paint = delegate(Graphics g, Item item, bool h)
            {
                Theme t = T;
                if (h) using (SolidBrush b = new SolidBrush(t.Hover)) g.FillRectangle(b, item.R);
                if (vert)
                {
                    using (SolidBrush b = new SolidBrush(t.Accent))
                    using (GraphicsPath gp = Svg.RoundRect(new RectangleF(item.R.Width / 2 - 2 * S, 14 * S, 4 * S, 16 * S), 2 * S)) g.FillPath(b, gp);
                    Icons.Draw(g, "rechts", new RectangleF(0, 36 * S, item.R.Width, 20 * S), 16 * S, t.Icon, 1.8f);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(t.Accent))
                    using (GraphicsPath gp = Svg.RoundRect(new RectangleF(20 * S, item.R.Height / 2 - 2 * S, 16 * S, 4 * S), 2 * S)) g.FillPath(b, gp);
                    Icons.Draw(g, "neer", new RectangleF(42 * S, 0, 20 * S, item.R.Height), 16 * S, t.Icon, 1.8f);
                }
            };
        }

        // Alleen de kant van het scherm af is rond
        public override void Rebuild()
        {
            hover = null;
            items.Clear();
            Build();
            float r = 12 * S;
            using (GraphicsPath gp = new GraphicsPath())
            {
                RectangleF b = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
                if (vert)
                {
                    gp.AddLine(b.X, b.Y, b.Right - r, b.Y);
                    gp.AddArc(b.Right - 2 * r, b.Y, 2 * r, 2 * r, 270, 90);
                    gp.AddArc(b.Right - 2 * r, b.Bottom - 2 * r, 2 * r, 2 * r, 0, 90);
                    gp.AddLine(b.Right - r, b.Bottom, b.X, b.Bottom);
                }
                else
                {
                    gp.AddLine(b.X, b.Y, b.Right, b.Y);
                    gp.AddArc(b.Right - 2 * r, b.Bottom - 2 * r, 2 * r, 2 * r, 0, 90);
                    gp.AddArc(b.X, b.Bottom - 2 * r, 2 * r, 2 * r, 90, 90);
                }
                gp.CloseFigure();
                Region = new Region(gp);
            }
            Invalidate();
        }
    }

    // ------------------------------------------------------------------ Instellingen (in de stijl van de werkbalk, volgt het thema)
    class SettingsForm : Form
    {
        Overlay ov;
        List<Item> items = new List<Item>();
        Item hover;
        ToolTip tip = new ToolTip();
        float S;
        int page;
        static readonly string[] Pages = { "Uiterlijk", "Werkbalk", "Tekst", "Delen en opslaan", "Sneltoetsen" };
        static readonly string[] PageIcons = { "cirkel", "rechthoek", "tekst", "opslaan", "vraag" };
        static readonly string[] FontChoices = { "Segoe UI", "Andika", "Nunito", "Arial", "Verdana", "Calibri", "Century Gothic", "Comic Sans MS" };
        string notice;
        DateTime noticeAt;

        public SettingsForm(Overlay o)
        {
            ov = o;
            Text = "NexusPen instellingen";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Rectangle wa = Screen.FromPoint(Control.MousePosition).WorkingArea;
            S = W.DpiScaleAt(new Point(wa.X + wa.Width / 2, wa.Y + wa.Height / 2));
            ClientSize = new Size((int)(880 * S), (int)(580 * S));
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            try
            {
                Theme t = ov.Theme;
                Icon = Logo.MakeIcon(32, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
            }
            catch { }
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 14 * S)) Region = new Region(gp);
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.HideFromCapture(Handle, ov.Cfg.HideFromCapture);
        }

        Theme T { get { return ov.Theme; } }
        Settings Cfg { get { return ov.Cfg; } }

        void Changed()
        {
            ov.ApplySettings();
            try
            {
                Theme t = T;
                Icon old = Icon;
                Icon = Logo.MakeIcon(32, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
                if (old != null) old.Dispose();
            }
            catch { }
            Invalidate();
        }

        void Notice(string text) { notice = text; noticeAt = DateTime.Now; Invalidate(); System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 2600; t.Tick += delegate { t.Stop(); t.Dispose(); Invalidate(); }; t.Start(); }

        // ---- tekenhulpjes
        Font F(float px, bool bold) { return Fonts.Make(UiFont.Name, px * S, bold ? FontStyle.Bold : FontStyle.Regular); }

        void Txt(Graphics g, string text, RectangleF r, Color c, float px, bool bold, StringAlignment align, bool wrap)
        {
            using (Font f = F(px, bold)) using (SolidBrush b = new SolidBrush(c)) using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align; sf.LineAlignment = wrap ? StringAlignment.Near : StringAlignment.Center;
                if (!wrap) { sf.FormatFlags = StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter; }
                g.DrawString(text, f, b, r, sf);
            }
        }

        Color Card { get { return T.Chip; } }
        Color Raised { get { return T.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#2E2E33"); } }

        Item Add(RectangleF r, string tipText, Action click, Action<Graphics, Item, bool> paint)
        {
            Item it = new Item(); it.R = r; it.Tip = tipText; it.Click = click; it.Paint = paint;
            items.Add(it);
            return it;
        }

        // Rij met keuzeknoppen (segmenten)
        float Segments(float x, float y, float w, string label, string help, string[] options, Func<int> get, Action<int> set)
        {
            Theme t = T;
            labels.Add(new object[] { label, new RectangleF(x, y, w, 20 * S), help });
            y += 24 * S;
            if (help != null) y += 18 * S;
            float segH = 34 * S, inner = 3 * S;
            RectangleF box = new RectangleF(x, y, Math.Min(w, options.Length * 120 * S), segH);
            boxes.Add(box);
            float sw = (box.Width - inner * 2) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                Add(new RectangleF(box.X + inner + i * sw, box.Y + inner, sw, segH - inner * 2), null, delegate { set(idx); Changed(); },
                    delegate(Graphics g, Item it, bool h)
                    {
                        Theme th = T;
                        bool on = get() == idx;
                        if (on || h)
                            using (SolidBrush b = new SolidBrush(on ? Raised : th.Hover))
                            using (GraphicsPath gp = Svg.RoundRect(it.R, 7 * S)) g.FillPath(b, gp);
                        if (on)
                            using (Pen p = new Pen(th.Accent, 1.5f * S))
                            using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(it.R, -0.75f * S, -0.75f * S), 7 * S)) g.DrawPath(p, gp);
                        Txt(g, options[idx], it.R, on ? th.Text : th.Muted, 13, true, StringAlignment.Center, false);
                    });
            }
            return y + segH + 22 * S;
        }

        List<object[]> labels = new List<object[]>();
        List<RectangleF> boxes = new List<RectangleF>();
        List<object[]> paras = new List<object[]>();
        RectangleF preview, logoBox;

        float Para(float x, float y, float w, string text, float h)
        {
            paras.Add(new object[] { text, new RectangleF(x, y, w, h) });
            return y + h;
        }

        float ActionButton(float x, float y, string label, string icon, Action click, bool primary)
        {
            float w;
            using (Font f = F(13, true)) w = TextRenderer.MeasureText(label, f).Width + 52 * S;
            Add(new RectangleF(x, y, w, 36 * S), null, click, delegate(Graphics g, Item it, bool h)
            {
                Theme t = T;
                Color bg = primary ? t.Accent : h ? Raised : Card;
                using (SolidBrush b = new SolidBrush(bg)) using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
                if (primary && h) using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
                Color fg = primary ? t.OnAccent : t.Text;
                Icons.Draw(g, icon, new RectangleF(it.R.X + 10 * S, it.R.Y, 20 * S, it.R.Height), 18 * S, fg, 1.8f);
                Txt(g, label, new RectangleF(it.R.X + 36 * S, it.R.Y, it.R.Width - 40 * S, it.R.Height), fg, 13, true, StringAlignment.Near, false);
            });
            return w;
        }

        void BuildItems()
        {
            items.Clear(); labels.Clear(); boxes.Clear(); paras.Clear();
            hover = null; preview = RectangleF.Empty; logoBox = RectangleF.Empty;
            float W0 = ClientSize.Width, H0 = ClientSize.Height;
            // sluiten
            Add(new RectangleF(W0 - 46 * S, 12 * S, 32 * S, 32 * S), "Sluiten (Esc)", delegate { Close(); }, delegate(Graphics g, Item it, bool h)
            {
                if (h) using (SolidBrush b = new SolidBrush(T.Hover)) using (GraphicsPath gp = Svg.RoundRect(it.R, 8 * S)) g.FillPath(b, gp);
                Icons.Draw(g, "kruis", it.R, 18 * S, T.Icon, 1.8f);
            });
            // navigatie
            float ny = 76 * S;
            for (int i = 0; i < Pages.Length; i++)
            {
                int idx = i;
                Add(new RectangleF(12 * S, ny, 196 * S, 38 * S), null, delegate { page = idx; BuildItems(); Invalidate(); }, delegate(Graphics g, Item it, bool h)
                {
                    Theme t = T;
                    bool on = page == idx;
                    if (on || h) using (SolidBrush b = new SolidBrush(on ? Raised : t.Hover)) using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
                    Icons.Draw(g, PageIcons[idx], new RectangleF(it.R.X + 10 * S, it.R.Y, 20 * S, it.R.Height), 18 * S, on ? t.Accent : t.Muted, 1.8f);
                    Txt(g, Pages[idx], new RectangleF(it.R.X + 40 * S, it.R.Y, it.R.Width - 44 * S, it.R.Height), on ? t.Text : t.Muted, 14, on, StringAlignment.Near, false);
                });
                ny += 42 * S;
            }
            Add(new RectangleF(W0 - 120 * S, H0 - 56 * S, 100 * S, 38 * S), null, delegate { Close(); }, delegate(Graphics g, Item it, bool h)
            {
                Theme t = T;
                using (SolidBrush b = new SolidBrush(t.Accent)) using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
                if (h) using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
                Txt(g, "Klaar", it.R, t.OnAccent, 14, true, StringAlignment.Center, false);
            });

            float x = 240 * S, y = 76 * S, w = W0 - x - 28 * S;
            switch (page)
            {
                case 0: BuildLook(x, y, w); break;
                case 1: BuildBar(x, y, w); break;
                case 2: BuildText(x, y, w); break;
                case 3: BuildShare(x, y, w); break;
            }
        }

        void BuildLook(float x, float y, float w)
        {
            labels.Add(new object[] { "Thema", new RectangleF(x, y, w, 20 * S), "Kleuren van de werkbalk. Het logo kleurt mee met de accentkleur." });
            logoBox = new RectangleF(x + w - 56 * S, y - 6 * S, 56 * S, 56 * S);
            y += 58 * S;
            float tw = (w - 10 * S) / 2, th = 54 * S;
            for (int i = 0; i < Theme.All.Length; i++)
            {
                int idx = i;
                RectangleF r = new RectangleF(x + (i % 2) * (tw + 10 * S), y + (i / 2) * (th + 8 * S), tw, th);
                Add(r, idx == Theme.Custom ? "Klik om je eigen accentkleur te kiezen" : null, delegate
                {
                    if (idx == Theme.Custom)
                    {
                        using (ColorDialog cd = new ColorDialog())
                        {
                            cd.Color = Cfg.CustomAccent; cd.FullOpen = true;
                            if (cd.ShowDialog(this) != DialogResult.OK) return;
                            Cfg.CustomAccent = cd.Color;
                        }
                    }
                    Cfg.ThemeIndex = idx; Changed();
                }, PaintTheme(idx));
            }
        }

        Action<Graphics, Item, bool> PaintTheme(int idx)
        {
            return delegate(Graphics g, Item it, bool h)
            {
                Theme cur = T;
                Theme t = idx == Theme.Custom ? CustomPreview() : Theme.All[idx];
                bool on = Cfg.ThemeIndex == idx;
                using (SolidBrush b = new SolidBrush(h && !on ? Raised : Card)) using (GraphicsPath gp = Svg.RoundRect(it.R, 10 * S)) g.FillPath(b, gp);
                if (on) using (Pen p = new Pen(t.Accent, 2 * S)) using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(it.R, -1 * S, -1 * S), 10 * S)) g.DrawPath(p, gp);
                // mini-werkbalk
                RectangleF bar = new RectangleF(it.R.X + 10 * S, it.R.Y + (it.R.Height - 28 * S) / 2, 66 * S, 28 * S);
                using (SolidBrush b = new SolidBrush(t.Bar)) using (GraphicsPath gp = Svg.RoundRect(bar, 8 * S)) g.FillPath(b, gp);
                using (Pen p = new Pen(t.Border, t.Thick ? 2 : 1)) using (GraphicsPath gp = Svg.RoundRect(bar, 8 * S)) g.DrawPath(p, gp);
                Logo.Draw(g, new RectangleF(bar.X + 5 * S, bar.Y + 5 * S, 18 * S, 18 * S), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
                using (SolidBrush b = new SolidBrush(t.Accent)) using (GraphicsPath gp = Svg.RoundRect(new RectangleF(bar.X + 27 * S, bar.Y + 5 * S, 18 * S, 18 * S), 5 * S)) g.FillPath(b, gp);
                using (SolidBrush b = new SolidBrush(t.Icon)) g.FillEllipse(b, bar.X + 51 * S, bar.Y + 11 * S, 6 * S, 6 * S);
                float tx = bar.Right + 12 * S;
                Txt(g, t.Name, new RectangleF(tx, it.R.Y + 8 * S, it.R.Right - tx - 8 * S, 20 * S), cur.Text, 14, true, StringAlignment.Near, false);
                Txt(g, t.Desc, new RectangleF(tx, it.R.Y + 28 * S, it.R.Right - tx - 8 * S, 18 * S), cur.Muted, 12, false, StringAlignment.Near, false);
            };
        }

        Theme CustomPreview()
        {
            int keep = Cfg.ThemeIndex;
            Cfg.ThemeIndex = Theme.Custom;
            Theme t = Theme.Get(Cfg);
            Cfg.ThemeIndex = keep;
            return t;
        }

        void BuildBar(float x, float y, float w)
        {
            Settings c = Cfg;
            y = Segments(x, y, w, "Stand", null, new string[] { "Liggend", "Staand" }, delegate { return c.Vertical ? 1 : 0; }, delegate(int i) { c.Vertical = i == 1; });
            y = Segments(x, y, w, "Grootte van de knoppen", null, new string[] { "Klein", "Normaal", "Groot" }, delegate { return c.SizeIndex; }, delegate(int i) { c.SizeIndex = i; });
            y = Segments(x, y, w, "Hoeken", null, new string[] { "Rond", "Strak" }, delegate { return c.Square ? 1 : 0; }, delegate(int i) { c.Square = i == 1; });
            y = Segments(x, y, w, "Doorzichtigheid", null, new string[] { "Dekkend", "90%", "80%" }, delegate { return c.OpacityIndex; }, delegate(int i) { c.OpacityIndex = i; });
            y = Segments(x, y, w, "Werkbalk", "Ingeklapt zie je alleen het logo. Klik erop om alles weer te zien.", new string[] { "Uitgeklapt", "Ingeklapt" }, delegate { return c.Collapsed ? 1 : 0; }, delegate(int i) { c.Collapsed = i == 1; });
            ActionButton(x, y - 4 * S, "Werkbalk terugzetten naar de standaardplek", "op", delegate { if (ov.Bar != null) ov.Bar.ResetPosition(); Notice("De werkbalk staat weer op zijn plek."); }, false);
        }

        void BuildText(float x, float y, float w)
        {
            Settings c = Cfg;
            labels.Add(new object[] { "Lettertype", new RectangleF(x, y, w, 20 * S), "Voor nieuwe teksten. Andika en Nunito zitten in NexusPen zelf en zijn extra duidelijk voor beginnende lezers." });
            y += 46 * S;
            float cx = x;
            List<string> fonts = new List<string>();
            foreach (string f in FontChoices) if (f == "Segoe UI" || Render.IsInstalled(f)) fonts.Add(f);
            float rowY = y;
            foreach (string name in fonts)
            {
                string fn = name;
                float cw;
                using (Font f = Fonts.Make(fn, 14 * S, FontStyle.Bold)) cw = TextRenderer.MeasureText(fn, f).Width + 26 * S;
                if (cx + cw > x + w) { cx = x; rowY += 42 * S; }
                Add(new RectangleF(cx, rowY, cw, 34 * S), null, delegate { c.FontName = fn == "Segoe UI" ? "" : fn; Changed(); }, delegate(Graphics g, Item it, bool h)
                {
                    Theme t = T;
                    bool on = Render.FontOrDefault(c.FontName) == fn;
                    using (SolidBrush b = new SolidBrush(on ? Raised : h ? t.Hover : Card)) using (GraphicsPath gp = Svg.RoundRect(it.R, 8 * S)) g.FillPath(b, gp);
                    if (on) using (Pen p = new Pen(t.Accent, 1.5f * S)) using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(it.R, -0.75f * S, -0.75f * S), 8 * S)) g.DrawPath(p, gp);
                    using (Font f = Fonts.Make(fn, 14 * S, FontStyle.Bold))
                    using (SolidBrush b = new SolidBrush(on ? t.Text : t.Muted))
                    using (StringFormat sf = new StringFormat()) { sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; g.DrawString(fn, f, b, it.R, sf); }
                });
                cx += cw + 8 * S;
            }
            y = rowY + 34 * S + 26 * S;
            y = Segments(x, y, w, "Zwarte rand om tekst", "Goed leesbaar op elke achtergrond, ook op foto's en video.", new string[] { "Uit", "Aan" }, delegate { return c.TextOutline ? 1 : 0; }, delegate(int i) { c.TextOutline = i == 1; });
            preview = new RectangleF(x, y, w, Math.Max(80 * S, ClientSize.Height - y - 80 * S));
        }

        void BuildShare(float x, float y, float w)
        {
            Settings c = Cfg;
            y = Segments(x, y, w, "Werkbalk verbergen bij schermdelen", "Cursisten zien in Zoom of Teams je tekeningen wel, maar de werkbalk niet (vanaf Windows 10, versie 2004).",
                new string[] { "Uit", "Aan" }, delegate { return c.HideFromCapture ? 1 : 0; }, delegate(int i) { c.HideFromCapture = i == 1; });
            labels.Add(new object[] { "Opslaan", new RectangleF(x, y, w, 20 * S), null });
            y += 26 * S;
            y = Para(x, y, w, "NexusPen bewaart je tekening automatisch, zodat je niets kwijtraakt als er iets misgaat. Druk je op Opslaan (op de werkbalk of Ctrl+S), dan staat je tekening er de volgende keer dat je NexusPen opent gewoon weer. Leeg beginnen: wis alles en druk nog een keer op Opslaan.", 76 * S);
            y += 10 * S;
            float bx = x;
            bx += ActionButton(bx, y, "Nu opslaan", "opslaan", delegate { if (ov.SaveExplicit()) Notice("Opgeslagen. Je tekening en instellingen staan er de volgende keer weer."); }, true) + 10 * S;
            ActionButton(bx, y, "Vorige tekening terughalen", "undo", delegate { ov.RestorePrevious(); Notice("Klaar. Kijk op je scherm."); }, false);
            y += 36 * S + 30 * S;
            labels.Add(new object[] { "Mappen", new RectangleF(x, y, w, 20 * S), null });
            y += 26 * S;
            bx = x;
            bx += ActionButton(bx, y, "Schermafbeeldingen", "map", delegate
            {
                try { Directory.CreateDirectory(Overlay.ScreenshotDir); Process.Start("explorer.exe", "\"" + Overlay.ScreenshotDir + "\""); } catch (Exception ex) { Log.Error("Map openen", ex); }
            }, false) + 10 * S;
            ActionButton(bx, y, "Tekeningen en instellingen", "map", delegate
            {
                try { Directory.CreateDirectory(StrokeStore.Dir); Process.Start("explorer.exe", "\"" + StrokeStore.Dir + "\""); } catch (Exception ex) { Log.Error("Map openen", ex); }
            }, false);
        }

        // ---- tekenen
        protected override void OnPaint(PaintEventArgs e)
        {
            if (items.Count == 0) BuildItems();
            Graphics g = e.Graphics;
            Theme t = T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(t.Bar);
            float W0 = ClientSize.Width, H0 = ClientSize.Height;
            using (Pen p = new Pen(t.Border, (t.Thick ? 2 : 1) * S)) using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0.5f, 0.5f, W0 - 1.5f, H0 - 1.5f), 14 * S)) g.DrawPath(p, gp);
            // kop
            Logo.Draw(g, new RectangleF(20 * S, 16 * S, 32 * S, 32 * S), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
            Txt(g, "Instellingen", new RectangleF(62 * S, 14 * S, 300 * S, 22 * S), t.Text, 17, true, StringAlignment.Near, false);
            Txt(g, "NexusPen 1.2, Nexus of Language", new RectangleF(62 * S, 34 * S, 300 * S, 16 * S), t.Muted, 12, false, StringAlignment.Near, false);
            using (SolidBrush b = new SolidBrush(t.Sep)) { g.FillRectangle(b, 0, 62 * S, W0, 1 * S); g.FillRectangle(b, 220 * S, 62 * S, 1 * S, H0 - 62 * S); }

            if (page == 4) PaintHelp(g, 240 * S, 76 * S, W0 - 268 * S);

            foreach (object[] l in labels)
            {
                RectangleF r = (RectangleF)l[1];
                Txt(g, (string)l[0], r, t.Text, 15, true, StringAlignment.Near, false);
                if (l[2] != null) Txt(g, (string)l[2], new RectangleF(r.X, r.Y + 22 * S, r.Width - (logoBox.IsEmpty ? 0 : 70 * S), 20 * S), t.Muted, 12, false, StringAlignment.Near, false);
            }
            foreach (RectangleF b in boxes)
                using (SolidBrush br = new SolidBrush(Card)) using (GraphicsPath gp = Svg.RoundRect(b, 9 * S)) g.FillPath(br, gp);
            foreach (object[] p in paras) Txt(g, (string)p[0], (RectangleF)p[1], t.Muted, 13, false, StringAlignment.Near, true);
            if (!logoBox.IsEmpty) Logo.Draw(g, logoBox, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
            if (!preview.IsEmpty) PaintPreview(g, preview);

            foreach (Item it in items) it.Paint(g, it, it == hover);

            if (notice != null && (DateTime.Now - noticeAt).TotalMilliseconds < 2500)
                Txt(g, notice, new RectangleF(240 * S, H0 - 56 * S, W0 - 380 * S, 38 * S), t.Accent, 13, true, StringAlignment.Near, false);
        }

        void PaintPreview(Graphics g, RectangleF r)
        {
            Theme t = T;
            using (SolidBrush b = new SolidBrush(ColorTranslator.FromHtml("#8C8C8C"))) using (GraphicsPath gp = Svg.RoundRect(r, 10 * S)) g.FillPath(b, gp);
            // half licht, half donker: zo zie je wat de rand doet
            GraphicsState st = g.Save();
            using (GraphicsPath gp = Svg.RoundRect(r, 10 * S)) g.SetClip(gp);
            using (SolidBrush b = new SolidBrush(ColorTranslator.FromHtml("#F4F1E8"))) g.FillRectangle(b, r.X, r.Y, r.Width / 2, r.Height);
            using (SolidBrush b = new SolidBrush(ColorTranslator.FromHtml("#1C1C1C"))) g.FillRectangle(b, r.X + r.Width / 2, r.Y, r.Width / 2, r.Height);
            Stroke s = new Stroke();
            s.Tool = NexusPen.Tool.Text; s.Text = "Goedemorgen! Lees de tekst."; s.FontSize = 30 * S;
            s.Color = ov.CurrentColor; s.FontName = Cfg.FontName; s.Outline = Cfg.TextOutline;
            RectangleF tb;
            s.Pts.Add(PointF.Empty);
            tb = Render.TextBounds(s);
            s.Pts[0] = new PointF(r.X + (r.Width - tb.Width) / 2, r.Y + (r.Height - tb.Height) / 2);
            Render.Setup(g);
            Render.Draw(g, s, 1);
            g.Restore(st);
        }

        void PaintHelp(Graphics g, float x, float y, float w)
        {
            Theme t = T;
            Txt(g, "Sneltoetsen", new RectangleF(x, y, w, 20 * S), t.Text, 15, true, StringAlignment.Near, false);
            string[] lines = string.Format(Overlay.HelpText, Cfg.HotkeyDraw, Cfg.HotkeyHide).Split('\n');
            float ly = y + 32 * S;
            foreach (string line in lines)
            {
                if (line.Length == 0) { ly += 8 * S; continue; }
                bool head = line.StartsWith("OVERAL") || line.StartsWith("TIJDENS");
                Txt(g, head ? (line.Substring(0, 1) + line.Substring(1).ToLowerInvariant()) : line, new RectangleF(x, ly, w, 20 * S), head ? t.Accent : t.Muted, head ? 13 : 13, head, StringAlignment.Near, false);
                ly += 21 * S;
            }
        }

        // ---- muis
        Item ItemAt(Point p) { for (int i = items.Count - 1; i >= 0; i--) if (items[i].R.Contains(p)) return items[i]; return null; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Item it = ItemAt(e.Location);
            if (it != null) { it.Click(); if (!IsDisposed) Invalidate(); return; }
            if (e.Y < 62 * S)
            {
                W.ReleaseCapture();
                W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item it = ItemAt(e.Location);
            if (it != hover)
            {
                hover = it;
                Invalidate();
                if (it != null && it.Tip != null) tip.Show(it.Tip, this, (int)it.R.X, (int)(it.R.Bottom + 6 * S), 2500); else tip.Hide(this);
            }
            Cursor = it != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = null; Invalidate(); }
    }

    // ------------------------------------------------------------------ Start
    static class Program
    {
        static long uiPing;
        public static void Ping() { Interlocked.Exchange(ref uiPing, DateTime.UtcNow.Ticks); }

        static void SetDpiAwareness()
        {
            // Per scherm de juiste schaal (100%, 125%, 150% door elkaar). Het manifest regelt dit ook al.
            // Lukt de aanroep niet omdat het manifest het al geregeld heeft, dan is dat goed en stoppen we.
            try { W.SetProcessDpiAwarenessContext(new IntPtr(-4)); return; } catch (EntryPointNotFoundException) { }
            try { W.SetProcessDpiAwareness(2); return; } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
            try { W.SetProcessDPIAware(); } catch { }
        }

        // Waakhond: als het programma in tekenmodus vastloopt, blokkeert het je hele scherm.
        // Dan stoppen we het programma, zodat je scherm altijd vrij komt. De tekening is al bewaard.
        static void StartWatchdog()
        {
            Thread t = new Thread(delegate()
            {
                long lastLoop = DateTime.UtcNow.Ticks;
                int strikes = 0;
                while (true)
                {
                    Thread.Sleep(1000);
                    long now = DateTime.UtcNow.Ticks;
                    bool resumed = (now - lastLoop) / 1e7 > 3; // pc kwam net uit slaapstand: niet meetellen
                    lastLoop = now;
                    if (resumed) { strikes = 0; Ping(); continue; }
                    long last = Interlocked.Read(ref uiPing);
                    Overlay ov = Overlay.Instance;
                    if (last == 0 || ov == null || !ov.DrawMode) { strikes = 0; continue; }
                    double sec = (now - last) / 1e7;
                    strikes = sec > 8 ? strikes + 1 : 0;
                    if (strikes >= 2)
                    {
                        Log.Info("Programma reageerde " + sec.ToString("0") + " s niet in tekenmodus: noodstop zodat het scherm vrij komt");
                        try { Process.GetCurrentProcess().Kill(); } catch { }
                    }
                }
            });
            t.IsBackground = true;
            t.Name = "NexusPen waakhond";
            t.Start();
        }

        public static bool SelfTest;
        public static int SelfTestErrors;

        [STAThread]
        static int Main(string[] args)
        {
            SelfTest = Array.IndexOf(args, "--zelftest") >= 0;
            bool created;
            using (Mutex m = new Mutex(true, SelfTest ? "NexusPen_Zelftest" : "NexusPen_EenKeerStarten", out created))
            {
                if (!created) { MessageBox.Show("NexusPen draait al. Kijk rechtsonder bij de klok.", "NexusPen"); return 0; }
                SetDpiAwareness();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    SelfTestErrors++;
                    Log.Error("Onverwachte fout", e.Exception);
                    if (SelfTest) { Console.Error.WriteLine(e.Exception); return; }
                    Overlay ov = Overlay.Instance;
                    if (ov != null) { ov.EmergencyMouseMode(); ov.ReportError(); }
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Error("Fatale fout", e.ExceptionObject as Exception);
                };
                Log.Info("Start NexusPen " + Version + (SelfTest ? " (zelftest)" : "") + " op " + Environment.OSVersion + ", .NET " + Environment.Version + ", " + Screen.AllScreens.Length + " scherm(en)");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (!SelfTest) StartWatchdog();
                Overlay main = new Overlay();
                if (SelfTest) main.RunSelfTest();
                Application.Run(main);
                return SelfTest ? (SelfTestErrors == 0 ? 0 : 1) : 0;
            }
        }

        public const string Version = "1.2.0";
    }
}
