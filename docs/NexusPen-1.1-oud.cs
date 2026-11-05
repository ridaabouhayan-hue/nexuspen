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
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace NexusPen
{
    enum Tool { Mouse, Pen, Marker, Laser, Line, Arrow, Rect, Ellipse, Text, Eraser }
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

    // ------------------------------------------------------------------ Tekening bewaren (automatisch)
    static class StrokeStore
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen"); } }
        public static string CurrentPath { get { return Path.Combine(Dir, "tekening.dat"); } }
        public static string PreviousPath { get { return Path.Combine(Dir, "vorige-tekening.dat"); } }
        public static string LockPath { get { return Path.Combine(Dir, "sessie.lock"); } }

        public static void Save(List<Stroke> list, string path)
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NEXUSPEN1");
            foreach (Stroke st in list)
            {
                sb.Append(st.Tool).Append('|').Append(st.Color.ToArgb().ToString(C)).Append('|')
                  .Append(st.Width.ToString(C)).Append('|').Append(st.FontSize.ToString(C)).Append('|')
                  .Append(st.Text == null ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(st.Text))).Append('|');
                for (int i = 0; i < st.Pts.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(st.Pts[i].X.ToString("0.#", C)).Append(',').Append(st.Pts[i].Y.ToString("0.#", C));
                }
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
                    if (f[4].Length > 0) st.Text = Encoding.UTF8.GetString(Convert.FromBase64String(f[4]));
                    foreach (string pt in f[5].Split(' '))
                    {
                        int k = pt.IndexOf(',');
                        if (k > 0) st.Pts.Add(new PointF(float.Parse(pt.Substring(0, k), C), float.Parse(pt.Substring(k + 1), C)));
                    }
                    if (st.Pts.Count > 0) list.Add(st);
                }
                catch { }
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ Instellingen
    class Settings
    {
        public int ToolbarX = -1, ToolbarY = -1, ColorIndex = 0;
        public float Width = 4;
        public Tool Tool = Tool.Pen;
        public string HotkeyDraw = "Ctrl+Shift+D", HotkeyHide = "Ctrl+Shift+H";

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

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n; float f;
                    if (k == "toolbarX" && int.TryParse(v, out n)) s.ToolbarX = n;
                    else if (k == "toolbarY" && int.TryParse(v, out n)) s.ToolbarY = n;
                    else if (k == "kleur" && int.TryParse(v, out n)) s.ColorIndex = n;
                    else if (k == "dikte" && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) s.Width = f;
                    else if (k == "sneltoetsTekenen") s.HotkeyDraw = v;
                    else if (k == "sneltoetsVerbergen") s.HotkeyHide = v;
                    else if (k == "gereedschap") { try { s.Tool = (Tool)Enum.Parse(typeof(Tool), v); } catch { } }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new string[] {
                    "toolbarX=" + ToolbarX, "toolbarY=" + ToolbarY, "kleur=" + ColorIndex,
                    "dikte=" + Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "gereedschap=" + Tool,
                    "sneltoetsTekenen=" + HotkeyDraw, "sneltoetsVerbergen=" + HotkeyHide });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Tekenen van lijnen/vormen
    static class Render
    {
        static readonly Graphics measureG = Graphics.FromImage(new Bitmap(1, 1));

        public static float MarkerWidth(float w) { return w * 3 + 8; }

        public static Pen MakePen(Color c, float w)
        {
            Pen p = new Pen(c, w);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }

        public static Font MakeFont(float px) { return new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel); }

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
                case Tool.Text:
                    if (string.IsNullOrEmpty(s.Text)) break;
                    using (Font f = MakeFont(s.FontSize)) using (SolidBrush br = new SolidBrush(s.Color)) g.DrawString(s.Text, f, br, a);
                    break;
            }
        }

        public static RectangleF TextBounds(Stroke s)
        {
            using (Font f = MakeFont(s.FontSize))
            {
                SizeF sz = measureG.MeasureString(s.Text ?? "", f);
                return new RectangleF(s.Pts[0], sz);
            }
        }

        public static bool Hit(Stroke s, PointF p, float r)
        {
            if (s.Pts.Count == 0) return false;
            if (s.Tool == Tool.Text) { RectangleF tb = TextBounds(s); tb.Inflate(r, r); return tb.Contains(p); }
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
    }

    // ------------------------------------------------------------------ Tekenlaag over het hele scherm
    class Overlay : Form
    {
        public static readonly Color[] Colors = {
            Color.FromArgb(232, 17, 35), Color.FromArgb(255, 140, 0), Color.FromArgb(255, 214, 0), Color.FromArgb(16, 170, 80),
            Color.FromArgb(0, 120, 215), Color.FromArgb(136, 23, 152), Color.FromArgb(20, 20, 20), Color.FromArgb(255, 255, 255) };
        public static readonly float[] Sizes = { 2, 4, 8, 16 };

        public Settings Cfg;
        public Tool CurrentTool = Tool.Pen;
        public Tool LastDrawTool = Tool.Pen;
        public int ColorIndex = 0;
        public float PenWidth = 4;
        public Board BoardMode = Board.None;
        public static Overlay Instance;
        public volatile bool DrawMode = false;
        public bool StrokesVisible = true;

        List<Stroke> strokes = new List<Stroke>();
        List<List<Stroke>> undo = new List<List<Stroke>>(), redo = new List<List<Stroke>>();
        List<Stroke> lasers = new List<Stroke>();
        Stroke current;
        bool erasing, erasedThisDrag;

        Rectangle vs;
        IntPtr memDC, hBmp, oldBmp, bits;
        Bitmap frame, committed;
        bool dirty = true;
        System.Windows.Forms.Timer timer;
        Toolbar toolbar;
        NotifyIcon tray;
        TextInput textInput;
        bool allHidden;

        // motorkap
        Rectangle lastTransient = Rectangle.Empty, pendingDirty = Rectangle.Empty;
        bool fullDirty = true, useIndirect = true, lastEsc, needSave, restoredAfterCrash;
        DateTime changedAt = DateTime.MinValue, lastErrorBalloon = DateTime.MinValue;
        int tickCount;

        public Color CurrentColor { get { return Colors[ColorIndex]; } }

        public Overlay()
        {
            Instance = this;
            Cfg = Settings.Load();
            StartSession();
            ColorIndex = Math.Max(0, Math.Min(Colors.Length - 1, Cfg.ColorIndex));
            PenWidth = Math.Max(1, Math.Min(60, Cfg.Width));
            if (Cfg.Tool != Tool.Mouse) { CurrentTool = Cfg.Tool; LastDrawTool = Cfg.Tool; }

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
                if (StrokesVisible) foreach (Stroke s in strokes) SafeDraw(g, s, 1f);
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

        // Gebied dat een lijn/vorm op het scherm inneemt (met marge voor dikte, gloed en pijlpunt)
        Rectangle StrokeBounds(Stroke s)
        {
            if (s.Pts.Count == 0 || committed == null) return Rectangle.Empty;
            RectangleF r;
            if (s.Tool == Tool.Text)
            {
                r = Render.TextBounds(s);
                r.Inflate(8, 8);
            }
            else
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (PointF p in s.Pts)
                {
                    if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y;
                }
                float pad = s.Width / 2;
                if (s.Tool == Tool.Marker) pad = Render.MarkerWidth(s.Width) / 2;
                else if (s.Tool == Tool.Laser) pad = (s.Width * 3 + 10) / 2;
                else if (s.Tool == Tool.Arrow) pad = Math.Max(14f, s.Width * 4f) + s.Width;
                pad += 4 + s.Width; // marge: de vloeiende curve kan iets buiten de punten lopen
                r = RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
            }
            Rectangle ri = Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
            ri.Intersect(new Rectangle(0, 0, committed.Width, committed.Height));
            return ri;
        }

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
            if (esc && !lastEsc && DrawMode && textInput == null) SetTool(Tool.Mouse);
            lastEsc = esc;

            // Bovenop blijven liggen (taakbalk, startmenu en Zoom duwen ons anders weg)
            if (tickCount % 60 == 0 && IsHandleCreated)
            {
                if (Visible) W.KeepOnTop(Handle);
                if (toolbar != null && toolbar.Visible && toolbar.IsHandleCreated) W.KeepOnTop(toolbar.Handle);
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

        void StartSession()
        {
            try
            {
                Directory.CreateDirectory(StrokeStore.Dir);
                if (File.Exists(StrokeStore.LockPath))
                {
                    // Vorige keer niet netjes afgesloten: tekening terugzetten
                    List<Stroke> rec = StrokeStore.Load(StrokeStore.CurrentPath);
                    if (rec.Count > 0) { strokes = rec; restoredAfterCrash = true; }
                    Log.Info("Vorige sessie niet netjes afgesloten, " + rec.Count + " lijnen teruggezet");
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

            bool wasFull = fullDirty;
            Rectangle area = wasFull ? full : U(U(lastTransient, transient), pendingDirty);
            area.Intersect(full);
            lastTransient = transient;
            pendingDirty = Rectangle.Empty;
            fullDirty = false;
            if (area.Width <= 0 || area.Height <= 0) return;

            CopyFromCommitted(area);
            if (transient.Width > 0 && transient.Height > 0)
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
        }

        void SetupTray()
        {
            tray = new NotifyIcon();
            tray.Icon = MakeAppIcon();
            tray.Text = "NexusPen  (" + Cfg.HotkeyDraw + " = tekenen aan/uit)";
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add("Tekenen aan/uit\t" + Cfg.HotkeyDraw, delegate { ToggleDraw(); });
            m.MenuItems.Add("Alles tonen/verbergen\t" + Cfg.HotkeyHide, delegate { ToggleAllHidden(); });
            m.MenuItems.Add("Schermafbeelding maken", delegate { Screenshot(); });
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

        public static Icon MakeAppIcon()
        {
            Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(Color.FromArgb(0, 120, 215))) g.FillEllipse(br, 1, 1, 30, 30);
                using (Pen p = Render.MakePen(Color.White, 4)) g.DrawLine(p, 9, 23, 22, 10);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 214, 0))) g.FillEllipse(br, 6, 21, 6, 6);
            }
            return Icon.FromHandle(b.GetHicon());
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            for (int i = 1; i <= 2; i++) W.UnregisterHotKey(Handle, i);
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            timer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Cfg.ColorIndex = ColorIndex; Cfg.Width = PenWidth; Cfg.Tool = LastDrawTool;
            if (toolbar != null) { Cfg.ToolbarX = toolbar.Left; Cfg.ToolbarY = toolbar.Top; }
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

        // ---------- modus en gereedschap
        void SetDrawMode(bool on)
        {
            if (on && allHidden) ToggleAllHidden();
            DrawMode = on;
            int ex = W.GetWindowLong(Handle, W.GWL_EXSTYLE);
            if (on) ex &= ~W.WS_EX_TRANSPARENT; else ex |= W.WS_EX_TRANSPARENT;
            W.SetWindowLong(Handle, W.GWL_EXSTYLE, ex);
            if (!on) { current = null; erasing = false; CloseTextInput(true); }
            Rebuild();
            RenderFrame();
            if (on) Activate();
            RefreshUi();
        }

        public void SetTool(Tool t)
        {
            if (t == Tool.Mouse) { if (DrawMode) SetDrawMode(false); RefreshUi(); return; }
            CurrentTool = t;
            LastDrawTool = t;
            Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            if (!DrawMode) SetDrawMode(true); else Activate();
            RefreshUi();
        }

        public void ToggleDraw() { if (DrawMode) SetTool(Tool.Mouse); else SetTool(LastDrawTool); }

        public void SetColor(int i)
        {
            ColorIndex = i;
            if (!DrawMode || CurrentTool == Tool.Eraser) SetTool(CurrentTool == Tool.Eraser || CurrentTool == Tool.Mouse ? Tool.Pen : CurrentTool);
            RefreshUi();
        }

        public void SetWidth(float w)
        {
            PenWidth = Math.Max(1, Math.Min(60, w));
            RefreshUi();
        }

        public void CycleBoard()
        {
            BoardMode = BoardMode == Board.None ? Board.White : BoardMode == Board.White ? Board.Black : Board.None;
            if (BoardMode != Board.None && !DrawMode) SetTool(LastDrawTool);
            Rebuild();
            RefreshUi();
        }

        public void ToggleStrokesVisible() { StrokesVisible = !StrokesVisible; Rebuild(); RefreshUi(); }

        public void ToggleAllHidden()
        {
            allHidden = !allHidden;
            if (allHidden)
            {
                if (DrawMode) SetDrawMode(false);
                if (toolbar != null) toolbar.Hide();
                Hide();
            }
            else
            {
                Show();
                Rebuild();
                RenderFrame();
                if (toolbar != null) toolbar.Show();
            }
        }

        void RefreshUi() { if (toolbar != null) toolbar.Invalidate(); }

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
            redo.Add(strokes);
            strokes = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            MarkChanged();
            Rebuild();
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(strokes);
            strokes = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            MarkChanged();
            Rebuild();
        }

        public void ClearAll()
        {
            lasers.Clear();
            if (strokes.Count == 0) { dirty = true; return; }
            Checkpoint();
            strokes = new List<Stroke>();
            Rebuild();
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
            PointF p = e.Location;
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && CurrentTool == Tool.Eraser))
            {
                erasing = true; erasedThisDrag = false; current = null;
                EraseAt(p);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (CurrentTool == Tool.Text) { OpenTextInput(e.Location); return; }
            if (!StrokesVisible && CurrentTool != Tool.Laser) { StrokesVisible = true; Rebuild(); RefreshUi(); }
            current = new Stroke();
            current.Tool = CurrentTool;
            current.Color = CurrentColor;
            current.Width = PenWidth;
            current.Pts.Add(p);
            if (CurrentTool == Tool.Line || CurrentTool == Tool.Arrow || CurrentTool == Tool.Rect || CurrentTool == Tool.Ellipse)
                current.Pts.Add(p);
            dirty = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!DrawMode) return;
            PointF p = e.Location;
            if (erasing) { EraseAt(p); return; }
            if (current == null) return;
            switch (current.Tool)
            {
                case Tool.Pen: case Tool.Marker: case Tool.Laser:
                    PointF last = current.Pts[current.Pts.Count - 1];
                    float dx = p.X - last.X, dy = p.Y - last.Y;
                    if (dx * dx + dy * dy >= 2.25f) current.Pts.Add(p);
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!DrawMode) return;
            float step = PenWidth < 6 ? 1 : 2;
            SetWidth(PenWidth + (e.Delta > 0 ? step : -step));
        }

        // ---------- toetsenbord (alleen als de tekenlaag actief is)
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); return; }
            if (e.Control && e.KeyCode == Keys.Y) { Redo(); return; }
            if (e.Control || e.Alt) return;
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
                case Keys.B: CycleBoard(); break;
                case Keys.V: ToggleStrokesVisible(); break;
                case Keys.S: Screenshot(); break;
                case Keys.Delete: ClearAll(); break;
                case Keys.Oemplus: case Keys.Add: SetWidth(PenWidth + (PenWidth < 6 ? 1 : 2)); break;
                case Keys.OemMinus: case Keys.Subtract: SetWidth(PenWidth - (PenWidth <= 6 ? 1 : 2)); break;
                default:
                    int n = -1;
                    if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D8) n = e.KeyCode - Keys.D1;
                    if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad8) n = e.KeyCode - Keys.NumPad1;
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
            textInput = new TextInput(screenPt, Render.MakeFont(px), c, delegate(string text)
            {
                textInput = null;
                if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return;
                Stroke s = new Stroke();
                s.Tool = Tool.Text; s.Color = c; s.Width = PenWidth; s.FontSize = px; s.Text = text;
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

        public void Screenshot()
        {
            if (screenshotBusy) return;
            screenshotBusy = true;
            bool tbVisible = toolbar != null && toolbar.Visible;
            if (tbVisible) toolbar.Hide();
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
                    if (tbVisible && toolbar != null && !allHidden) toolbar.Show();
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
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NexusPen");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "NexusPen_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
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

        public void ShowHelp()
        {
            if (DrawMode) SetTool(Tool.Mouse);
            MessageBox.Show(toolbar,
                "OVERAL (ook als je niet tekent)\n" +
                Cfg.HotkeyDraw + "   tekenen aan/uit\n" +
                Cfg.HotkeyHide + "   alles verbergen/tonen\n\n" +
                "TIJDENS HET TEKENEN\n" +
                "P pen   M markeerstift   L laser   E gum   T tekst\n" +
                "N lijn   A pijl   R rechthoek   O ovaal\n" +
                "1 t/m 8  kleur kiezen     + / -  of muiswiel  dikte\n" +
                "Ctrl+Z ongedaan   Ctrl+Y opnieuw   Delete alles wissen\n" +
                "B bord wisselen   V tekeningen even verbergen\n" +
                "S schermafbeelding (map Afbeeldingen\\NexusPen + klembord)\n" +
                "Esc  terug naar de muis (werkt altijd)\n\n" +
                "Rechtermuisknop ingedrukt houden = gummen.\n" +
                "Shift ingedrukt bij lijn/pijl = rechte hoeken (45 graden),\n" +
                "bij rechthoek/ovaal = vierkant/cirkel.\n" +
                "Tekst: Enter = klaar, Shift+Enter = nieuwe regel, Esc = annuleren.\n\n" +
                "Tekening kwijt? Rechtsklik op het icoon bij de klok > Vorige tekening terughalen.",
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

    // ------------------------------------------------------------------ Werkbalk
    class Toolbar : Form
    {
        class Item
        {
            public RectangleF R;
            public string Tip;
            public Action Click;
            public Func<bool> Active;
            public Action<Graphics, RectangleF> Icon;
        }

        Overlay ov;
        List<Item> items = new List<Item>();
        Item hover;
        ToolTip tip = new ToolTip();
        float S;
        static readonly Color Bg = Color.FromArgb(32, 33, 37), Border = Color.FromArgb(70, 72, 78),
            Hover = Color.FromArgb(60, 62, 68), Accent = Color.FromArgb(0, 120, 215), Ink = Color.FromArgb(235, 235, 235);

        public Toolbar(Overlay o)
        {
            ov = o;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Bg;
            Text = "NexusPen werkbalk";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Point start = (ov.Cfg.ToolbarX != -1 || ov.Cfg.ToolbarY != -1)
                ? new Point(ov.Cfg.ToolbarX + 20, ov.Cfg.ToolbarY + 20)
                : new Point(Screen.PrimaryScreen.WorkingArea.Left + 20, Screen.PrimaryScreen.WorkingArea.Top + 20);
            S = W.DpiScaleAt(start);
            BuildItems();
            if (ov.Cfg.ToolbarX != -1 || ov.Cfg.ToolbarY != -1) Location = new Point(ov.Cfg.ToolbarX, ov.Cfg.ToolbarY);
            else ResetPosition();
            EnsureOnScreen();
            LocationChanged += delegate { ov.Cfg.ToolbarX = Left; ov.Cfg.ToolbarY = Top; };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(W.TABLET_FLAGS));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W.WM_TABLET_QUERYSYSTEMGESTURESTATUS) { m.Result = new IntPtr(W.TABLET_FLAGS); return; }
            if (m.Msg == W.WM_DPICHANGED)
            {
                // Werkbalk naar een scherm met andere schaal gesleept: opnieuw opbouwen op de juiste grootte
                int dpi = m.WParam.ToInt32() & 0xFFFF;
                S = Math.Max(1f, dpi / 96f);
                W.RECT r = (W.RECT)Marshal.PtrToStructure(m.LParam, typeof(W.RECT));
                hover = null;
                items.Clear();
                BuildItems();
                Location = new Point(r.Left, r.Top);
                Invalidate();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= W.WS_EX_NOACTIVATE | W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ResetPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (int)(12 * S), wa.Top + (wa.Height - Height) / 2);
        }

        public void EnsureOnScreen()
        {
            Rectangle me = Bounds;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(new Rectangle(me.X + 10, me.Y + 10, 20, 20))) return;
            ResetPosition();
        }

        // ---- opbouw
        Item Add(float x, float y, float w, float h, string tipText, Action click, Func<bool> active, Action<Graphics, RectangleF> icon)
        {
            Item it = new Item();
            it.R = new RectangleF(x, y, w, h); it.Tip = tipText; it.Click = click; it.Active = active; it.Icon = icon;
            items.Add(it);
            return it;
        }

        void ToolItem(float x, float y, float c, Tool t, string tipText, Action<Graphics, RectangleF> icon)
        {
            Add(x, y, c, c, tipText, delegate { ov.SetTool(t); },
                delegate { return t == Tool.Mouse ? !ov.DrawMode : ov.DrawMode && ov.CurrentTool == t; }, icon);
        }

        void BuildItems()
        {
            float pad = 6 * S, c = 40 * S, grip = 16 * S;
            float x0 = pad, x1 = pad + c, y = grip;

            ToolItem(x0, y, c, Tool.Mouse, "Muis: gewoon klikken op je scherm (Esc)", IconMouse);
            ToolItem(x1, y, c, Tool.Pen, "Pen (P)", IconPen); y += c;
            ToolItem(x0, y, c, Tool.Marker, "Markeerstift (M)", IconMarker);
            ToolItem(x1, y, c, Tool.Laser, "Laserpen, verdwijnt vanzelf (L)", IconLaser); y += c;
            ToolItem(x0, y, c, Tool.Line, "Lijn (N), Shift = recht", IconLine);
            ToolItem(x1, y, c, Tool.Arrow, "Pijl (A), Shift = recht", IconArrow); y += c;
            ToolItem(x0, y, c, Tool.Rect, "Rechthoek (R), Shift = vierkant", IconRect);
            ToolItem(x1, y, c, Tool.Ellipse, "Ovaal (O), Shift = cirkel", IconEllipse); y += c;
            ToolItem(x0, y, c, Tool.Text, "Tekst (T)", IconText);
            ToolItem(x1, y, c, Tool.Eraser, "Gum (E), of rechtermuisknop", IconEraser); y += c;

            y += pad;
            float sw = c / 2;
            for (int i = 0; i < Overlay.Colors.Length; i++)
            {
                int idx = i;
                float sx = x0 + (i % 4) * sw, sy = y + (i / 4) * sw;
                Add(sx, sy, sw, sw, "Kleur " + (i + 1), delegate { ov.SetColor(idx); }, delegate { return false; },
                    delegate(Graphics g, RectangleF r) { DrawSwatch(g, r, idx); });
            }
            y += sw * 2 + pad / 2;
            for (int i = 0; i < Overlay.Sizes.Length; i++)
            {
                float size = Overlay.Sizes[i];
                Add(x0 + i * sw, y, sw, sw, "Dikte " + size + " (ook met muiswiel)", delegate { ov.SetWidth(size); },
                    delegate { return Math.Abs(ov.PenWidth - size) < 0.01f; },
                    delegate(Graphics g, RectangleF r) { DrawSizeDot(g, r, size); });
            }
            y += sw + pad;

            Add(x0, y, c, c, "Ongedaan maken (Ctrl+Z)", delegate { ov.Undo(); }, null, IconUndo);
            Add(x1, y, c, c, "Opnieuw (Ctrl+Y)", delegate { ov.Redo(); }, null, IconRedo); y += c;
            Add(x0, y, c, c, "Alles wissen (Delete)", delegate { ov.ClearAll(); }, null, IconTrash);
            Add(x1, y, c, c, "Bord: whiteboard / krijtbord / uit (B)", delegate { ov.CycleBoard(); },
                delegate { return ov.BoardMode != Board.None && ov.DrawMode; }, IconBoard); y += c;
            Add(x0, y, c, c, "Schermafbeelding opslaan (S)", delegate { ov.Screenshot(); }, null, IconCamera);
            Add(x1, y, c, c, "Tekeningen verbergen/tonen (V)", delegate { ov.ToggleStrokesVisible(); },
                delegate { return !ov.StrokesVisible; }, IconEye); y += c;
            Add(x0, y, c, c, "Sneltoetsen en uitleg", delegate { ov.ShowHelp(); }, null, IconHelp);
            Add(x1, y, c, c, "Afsluiten", delegate { ov.Close(); }, null, IconClose); y += c;

            ClientSize = new Size((int)(pad * 2 + c * 2), (int)(y + pad));
            using (GraphicsPath gp = Rounded(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 10 * S))
                Region = new Region(gp);
        }

        static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath gp = new GraphicsPath();
            float d = rad * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        // ---- tekenen van de werkbalk
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Pen p = new Pen(Border, 1))
            using (GraphicsPath gp = Rounded(new RectangleF(0.5f, 0.5f, ClientSize.Width - 1.5f, ClientSize.Height - 1.5f), 10 * S))
                g.DrawPath(p, gp);
            // greep om te slepen
            using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 112, 118)))
                for (int i = 0; i < 6; i++)
                    g.FillEllipse(b, ClientSize.Width / 2f - 15 * S + i * 6 * S, 6 * S, 3 * S, 3 * S);

            foreach (Item it in items)
            {
                RectangleF r = it.R;
                RectangleF inner = new RectangleF(r.X + 2 * S, r.Y + 2 * S, r.Width - 4 * S, r.Height - 4 * S);
                bool active = it.Active != null && it.Active();
                if (active || it == hover)
                    using (SolidBrush b = new SolidBrush(active ? Accent : Hover))
                    using (GraphicsPath gp = Rounded(inner, 6 * S))
                        g.FillPath(b, gp);
                it.Icon(g, inner);
            }
        }

        Pen InkPen(float w) { return Render.MakePen(Ink, w * S); }
        RectangleF Shrink(RectangleF r, float f) { float dx = r.Width * f, dy = r.Height * f; return new RectangleF(r.X + dx, r.Y + dy, r.Width - 2 * dx, r.Height - 2 * dy); }

        void IconMouse(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.25f);
            PointF[] pts = {
                new PointF(b.X + b.Width * 0.2f, b.Y), new PointF(b.X + b.Width * 0.2f, b.Bottom * 1f - b.Height * 0.15f),
                new PointF(b.X + b.Width * 0.42f, b.Y + b.Height * 0.65f), new PointF(b.X + b.Width * 0.62f, b.Bottom),
                new PointF(b.X + b.Width * 0.75f, b.Y + b.Height * 0.93f), new PointF(b.X + b.Width * 0.55f, b.Y + b.Height * 0.6f),
                new PointF(b.X + b.Width * 0.85f, b.Y + b.Height * 0.6f) };
            using (SolidBrush br = new SolidBrush(Ink)) g.FillPolygon(br, pts);
        }

        void IconPen(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.27f);
            using (Pen p = InkPen(3)) g.DrawLine(p, b.X + b.Width * 0.15f, b.Bottom - b.Height * 0.15f, b.Right, b.Y);
            using (SolidBrush br = new SolidBrush(ov.CurrentColor)) g.FillEllipse(br, b.X - 1 * S, b.Bottom - 5 * S, 6 * S, 6 * S);
        }

        void IconMarker(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.25f);
            using (Pen p = Render.MakePen(Color.FromArgb(150, ov.CurrentColor), 8 * S)) g.DrawLine(p, b.X + 2 * S, b.Y + b.Height * 0.62f, b.Right - 2 * S, b.Y + b.Height * 0.62f);
            using (Pen p = InkPen(2)) g.DrawLine(p, b.X + b.Width * 0.35f, b.Y + b.Height * 0.45f, b.Right - 2 * S, b.Y);
        }

        void IconLaser(Graphics g, RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (SolidBrush br = new SolidBrush(Color.FromArgb(70, 255, 40, 40))) g.FillEllipse(br, cx - 10 * S, cy - 10 * S, 20 * S, 20 * S);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 50, 50))) g.FillEllipse(br, cx - 5 * S, cy - 5 * S, 10 * S, 10 * S);
            using (SolidBrush br = new SolidBrush(Color.White)) g.FillEllipse(br, cx - 2 * S, cy - 2 * S, 4 * S, 4 * S);
        }

        void IconLine(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.28f);
            using (Pen p = InkPen(2.5f)) g.DrawLine(p, b.X, b.Bottom, b.Right, b.Y);
        }

        void IconArrow(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.28f);
            using (Pen p = InkPen(2.5f))
            {
                g.DrawLine(p, b.X, b.Bottom, b.Right, b.Y);
                g.DrawLine(p, b.Right, b.Y, b.Right - b.Width * 0.45f, b.Y);
                g.DrawLine(p, b.Right, b.Y, b.Right, b.Y + b.Height * 0.45f);
            }
        }

        void IconRect(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.28f);
            using (Pen p = InkPen(2.5f)) g.DrawRectangle(p, b.X, b.Y + b.Height * 0.1f, b.Width, b.Height * 0.8f);
        }

        void IconEllipse(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.27f);
            using (Pen p = InkPen(2.5f)) g.DrawEllipse(p, b.X, b.Y + b.Height * 0.12f, b.Width, b.Height * 0.76f);
        }

        void IconText(Graphics g, RectangleF r)
        {
            using (Font f = new Font("Segoe UI", 17 * S, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush br = new SolidBrush(Ink))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString("T", f, br, r, sf);
            }
        }

        void IconEraser(Graphics g, RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-40);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(240, 130, 150))) g.FillRectangle(br, -10 * S, -5 * S, 11 * S, 10 * S);
            using (SolidBrush br = new SolidBrush(Ink)) g.FillRectangle(br, 1 * S, -5 * S, 9 * S, 10 * S);
            g.Restore(st);
        }

        void DrawSwatch(Graphics g, RectangleF r, int idx)
        {
            float d = r.Width * 0.62f, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (SolidBrush br = new SolidBrush(Overlay.Colors[idx])) g.FillEllipse(br, cx - d / 2, cy - d / 2, d, d);
            using (Pen p = new Pen(Color.FromArgb(90, 255, 255, 255), 1)) g.DrawEllipse(p, cx - d / 2, cy - d / 2, d, d);
            if (idx == ov.ColorIndex)
                using (Pen p = new Pen(Color.White, 2 * S)) g.DrawEllipse(p, cx - d / 2 - 2.5f * S, cy - d / 2 - 2.5f * S, d + 5 * S, d + 5 * S);
        }

        void DrawSizeDot(Graphics g, RectangleF r, float size)
        {
            float d = Math.Min(r.Width * 0.7f, (3 + size * 0.75f) * S), cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (SolidBrush br = new SolidBrush(Ink)) g.FillEllipse(br, cx - d / 2, cy - d / 2, d, d);
        }

        void IconUndo(Graphics g, RectangleF r) { DrawCurvedArrow(g, Shrink(r, 0.28f), false); }
        void IconRedo(Graphics g, RectangleF r) { DrawCurvedArrow(g, Shrink(r, 0.28f), true); }

        void DrawCurvedArrow(Graphics g, RectangleF b, bool right)
        {
            using (Pen p = InkPen(2.2f))
            {
                RectangleF arc = new RectangleF(b.X + b.Width * 0.1f, b.Y + b.Height * 0.2f, b.Width * 0.8f, b.Height * 0.75f);
                if (right)
                {
                    g.DrawArc(p, arc, 200, 230);
                    g.DrawLine(p, b.Right, b.Y + b.Height * 0.15f, b.Right, b.Y + b.Height * 0.48f);
                    g.DrawLine(p, b.Right, b.Y + b.Height * 0.48f, b.Right - b.Width * 0.33f, b.Y + b.Height * 0.48f);
                }
                else
                {
                    g.DrawArc(p, arc, 110, 230);
                    g.DrawLine(p, b.X, b.Y + b.Height * 0.15f, b.X, b.Y + b.Height * 0.48f);
                    g.DrawLine(p, b.X, b.Y + b.Height * 0.48f, b.X + b.Width * 0.33f, b.Y + b.Height * 0.48f);
                }
            }
        }

        void IconTrash(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.28f);
            using (Pen p = InkPen(2))
            {
                g.DrawLine(p, b.X, b.Y + b.Height * 0.18f, b.Right, b.Y + b.Height * 0.18f);
                g.DrawLine(p, b.X + b.Width * 0.38f, b.Y, b.X + b.Width * 0.62f, b.Y);
                g.DrawRectangle(p, b.X + b.Width * 0.15f, b.Y + b.Height * 0.32f, b.Width * 0.7f, b.Height * 0.68f);
            }
        }

        void IconBoard(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.26f);
            Color fill = ov.BoardMode == Board.Black ? Color.FromArgb(28, 32, 30) : Color.FromArgb(250, 250, 248);
            using (SolidBrush br = new SolidBrush(fill)) g.FillRectangle(br, b.X, b.Y + b.Height * 0.1f, b.Width, b.Height * 0.7f);
            using (Pen p = InkPen(1.5f))
            {
                g.DrawRectangle(p, b.X, b.Y + b.Height * 0.1f, b.Width, b.Height * 0.7f);
                g.DrawLine(p, b.X + b.Width * 0.3f, b.Bottom, b.X + b.Width * 0.4f, b.Y + b.Height * 0.8f);
                g.DrawLine(p, b.X + b.Width * 0.7f, b.Bottom, b.X + b.Width * 0.6f, b.Y + b.Height * 0.8f);
            }
        }

        void IconCamera(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.26f);
            using (Pen p = InkPen(2))
            {
                g.DrawRectangle(p, b.X, b.Y + b.Height * 0.25f, b.Width, b.Height * 0.65f);
                g.DrawEllipse(p, b.X + b.Width * 0.32f, b.Y + b.Height * 0.38f, b.Width * 0.36f, b.Width * 0.36f);
                g.DrawLine(p, b.X + b.Width * 0.3f, b.Y + b.Height * 0.25f, b.X + b.Width * 0.38f, b.Y + b.Height * 0.08f);
                g.DrawLine(p, b.X + b.Width * 0.38f, b.Y + b.Height * 0.08f, b.X + b.Width * 0.62f, b.Y + b.Height * 0.08f);
                g.DrawLine(p, b.X + b.Width * 0.62f, b.Y + b.Height * 0.08f, b.X + b.Width * 0.7f, b.Y + b.Height * 0.25f);
            }
        }

        void IconEye(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.24f);
            using (Pen p = InkPen(2))
            {
                g.DrawEllipse(p, b.X, b.Y + b.Height * 0.25f, b.Width, b.Height * 0.5f);
                using (SolidBrush br = new SolidBrush(Ink)) g.FillEllipse(br, b.X + b.Width * 0.38f, b.Y + b.Height * 0.38f, b.Width * 0.24f, b.Height * 0.24f);
                if (!ov.StrokesVisible) g.DrawLine(p, b.X, b.Bottom, b.Right, b.Y);
            }
        }

        void IconHelp(Graphics g, RectangleF r)
        {
            using (Font f = new Font("Segoe UI", 17 * S, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush br = new SolidBrush(Ink))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.DrawString("?", f, br, r, sf);
            }
        }

        void IconClose(Graphics g, RectangleF r)
        {
            RectangleF b = Shrink(r, 0.33f);
            using (Pen p = Render.MakePen(Color.FromArgb(240, 110, 110), 2.5f * S))
            {
                g.DrawLine(p, b.X, b.Y, b.Right, b.Bottom);
                g.DrawLine(p, b.Right, b.Y, b.X, b.Bottom);
            }
        }

        // ---- muis op de werkbalk
        Item ItemAt(Point p)
        {
            foreach (Item it in items) if (it.R.Contains(p)) return it;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Item it = ItemAt(e.Location);
            if (it != null) { it.Click(); Invalidate(); return; }
            W.ReleaseCapture();
            W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item it = ItemAt(e.Location);
            if (it != hover)
            {
                hover = it;
                Invalidate();
                if (it != null) tip.Show(it.Tip, this, (int)it.R.Right + (int)(10 * S), (int)it.R.Y + (int)(8 * S), 2500);
                else tip.Hide(this);
            }
            Cursor = it != null ? Cursors.Hand : Cursors.SizeAll;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            tip.Hide(this);
            Invalidate();
        }
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

        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex m = new Mutex(true, "NexusPen_EenKeerStarten", out created))
            {
                if (!created) { MessageBox.Show("NexusPen draait al. Kijk rechtsonder bij de klok.", "NexusPen"); return; }
                SetDpiAwareness();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Error("Onverwachte fout", e.Exception);
                    Overlay ov = Overlay.Instance;
                    if (ov != null) { ov.EmergencyMouseMode(); ov.ReportError(); }
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Error("Fatale fout", e.ExceptionObject as Exception);
                };
                Log.Info("Start NexusPen 1.1 op " + Environment.OSVersion + ", .NET " + Environment.Version + ", " + Screen.AllScreens.Length + " scherm(en)");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                StartWatchdog();
                Application.Run(new Overlay());
            }
        }
    }
}
