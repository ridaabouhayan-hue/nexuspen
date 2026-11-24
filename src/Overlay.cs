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
    // ------------------------------------------------------------------ Tekenlaag over het hele scherm
    partial class Overlay : Form
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

        List<Stroke> strokes = new List<Stroke>();      // wat je nu ziet: het scherm of de huidige bordpagina
        List<Stroke> screenStrokes = new List<Stroke>();
        List<List<Stroke>> pages = new List<List<Stroke>>();
        int pageIndex;
        List<Stroke> fading = new List<Stroke>();       // "vervaagt na 5 s"
        DateTime lastStrokeAt = DateTime.MinValue;
        public static bool StartCollapsed;
        public static readonly Color ChalkGreen = ColorTranslator.FromHtml("#24463A");
        public static readonly Color DarkBoard = ColorTranslator.FromHtml("#121214");
        public int PageIndex { get { return pageIndex; } }
        public int PageCount { get { return pages.Count; } }
        List<List<Stroke>> undo = new List<List<Stroke>>(), redo = new List<List<Stroke>>();
        List<Stroke> lasers = new List<Stroke>();
        Stroke current;
        bool erasing, erasedThisDrag;

        // selecteren en verplaatsen
        Stroke selected;
        bool dragging, movedThisDrag, lifted, resizing;
        bool quietMove;            // tekst verplaatsen met de Tekst-knop: zonder selectiekader en zonder balkje
        Point quietClickAt;
        PointF dragLast, resizeAnchor;
        float resizeStartDist;
        Stroke resizeOriginal;

        // spotlicht
        PointF focusAt = new PointF(-10000, -10000);

        Rectangle vs;
        IntPtr memDC, hBmp, oldBmp, bits;
        Bitmap frame, committed;
        bool dirty = true;
        System.Windows.Forms.Timer timer;
        Toolbar toolbar;
        NotifyIcon tray;
        // tekst typen, direct op het scherm (zonder tekstvak)
        Stroke typing, typingOld;
        int typingIndex = -1, typingUndoCount = -1;
        bool caretOn;
        TextEditor ed;             // pijltjes, selectie, muis en eigen Ongedaan maken in de tekst
        int textDrag;              // 0 niets, 1 selecteren met de muis, 2 verplaatsen, 3 groter of kleiner,
                                   // 4 geselecteerde woorden slepen, 5 ingedrukt op een tekst (slepen = hele tekst verplaatsen)
        int textDropIdx = -1;
        float textDragScale = 1;
        float lastCursorScale = 1;   // bij groter of kleiner slepen: de schaal van het scherm van de tekst      // bij 4: waar de woorden terechtkomen
        PointF textDragFrom, textPtFrom;
        float textSizeFrom, textDistFrom;
        DateTime caretAt;
        SettingsForm settingsForm;
        bool allHidden;

        // motorkap
        Rectangle lastTransient = Rectangle.Empty, pendingDirty = Rectangle.Empty;
        // natte inkt: het stukje van de lijn dat net is bijgekomen, en gebieden die opnieuw moeten (gum, bewaard-regel)
        Rectangle wetArea = Rectangle.Empty, rebuildArea = Rectangle.Empty;
        double lastRenderAt = -100, inputAt = -1;
        bool perfForceRender;   // zelftest: elke beweging meteen tekenen, om te meten
        bool fullDirty = true, useIndirect = true, lastEsc, needSave, restoredAfterCrash, restoredSaved;
        DateTime changedAt = DateTime.MinValue, lastErrorBalloon = DateTime.MinValue;
        int tickCount;

        public Color[] Colors { get { return Palette.Get(Cfg, PaletteIndex); } }
        public Color CurrentColor { get { Color[] cs = Colors; return cs[Math.Max(0, Math.Min(cs.Length - 1, ColorIndex))]; } }
        public int ColorCount { get { return Colors.Length; } }
        public Theme Theme { get { return Theme.Get(Cfg); } }
        public Toolbar Bar { get { return toolbar; } }
        public bool HasSelection { get { return selected != null; } }
        public static bool IsShape(Tool t) { return t == Tool.Line || t == Tool.Arrow || t == Tool.Rect || t == Tool.Ellipse; }

        public Overlay()
        {
            Instance = this;
            Cfg = Settings.Load();
            L.Set(Cfg.Language);
            Usage.Init(Cfg);
            Shortcuts.Cfg = Cfg;
            StartSession();
            ColorIndex = Cfg.ColorIndex;
            PaletteIndex = Cfg.PaletteIndex;
            PenWidth = Math.Max(1, Math.Min(Settings.MaxWidth, Cfg.Width));
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
            monitors = W.MonitorRects(); namedMons = W.NamedMonitors();
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

        // Een fout die niet erg is (NexusPen draait gewoon door), maar wel in het foutlog moet: hooguit een keer per minuut per plek
        readonly Dictionary<string, DateTime> quietLogged = new Dictionary<string, DateTime>();
        void Quiet(string where, Exception ex)
        {
            try
            {
                lock (quietLogged)   // kan ook vanaf een andere draad komen (bijwerken)
                {
                    DateTime last;
                    if (quietLogged.TryGetValue(where, out last) && (DateTime.Now - last).TotalSeconds < 60) return;
                    quietLogged[where] = DateTime.Now;
                }
                Log.Error(where, ex);
            }
            catch { }
        }
    }
}
