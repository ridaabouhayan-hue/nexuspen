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
        int textDropIdx = -1;      // bij 4: waar de woorden terechtkomen
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
            if (DrawMode && BoardMode == Board.Black) return ChalkGreen;
            if (DrawMode && BoardMode == Board.Dark) return DarkBoard;
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
                if (DrawMode && BoardMode != Board.None) DrawBoard(g);
                if (StrokesVisible)
                    foreach (Stroke s in strokes)
                        if (!(lifted && s == selected)) SafeDraw(g, s, 1f);
            }
            fullDirty = true;
            dirty = true;
        }

        // Alleen een stuk van de vaste laag opnieuw tekenen (na gummen, of voor de regel "Automatisch bewaard")
        void RebuildArea(Rectangle r)
        {
            if (committed == null) return;
            r.Intersect(new Rectangle(0, 0, committed.Width, committed.Height));
            if (r.Width <= 0 || r.Height <= 0) return;
            using (Graphics g = Graphics.FromImage(committed))
            {
                g.SetClip(r);
                g.CompositingMode = CompositingMode.SourceCopy;
                using (SolidBrush b = new SolidBrush(Background())) g.FillRectangle(b, r);
                g.CompositingMode = CompositingMode.SourceOver;
                Render.Setup(g);
                if (DrawMode && BoardMode != Board.None) DrawBoard(g);
                if (StrokesVisible)
                    foreach (Stroke s in strokes)
                        if (!(lifted && s == selected) && StrokeBounds(s).IntersectsWith(r)) SafeDraw(g, s, 1f);
            }
            pendingDirty = U(pendingDirty, r);
            dirty = true;
        }

        // Plek van het regeltje rechtsonder op het bord
        Rectangle BoardInfoRect()
        {
            if (committed == null) return Rectangle.Empty;
            int w = committed.Width, h = committed.Height;
            float s = W.DpiScaleAt(new Point(vs.X + w / 2, vs.Y + h / 2));
            Rectangle wa = Screen.FromPoint(new Point(vs.X + w - 10, vs.Y + h - 10)).WorkingArea;
            int right = (int)Math.Ceiling(wa.Right - vs.X - 22 * s) + 4, bottom = (int)Math.Ceiling(wa.Bottom - vs.Y - 12 * s) + 4;
            int bw = (int)(900 * s), bh = (int)(40 * s);
            return Rectangle.FromLTRB(Math.Max(0, right - bw), Math.Max(0, bottom - bh), right, bottom);
        }

        // Schrijflijnen, ruitjes of lijntjes, en rechtsonder een klein regeltje
        void DrawBoard(Graphics g)
        {
            int w = committed.Width, h = committed.Height;
            bool chalk = BoardMode == Board.Black || BoardMode == Board.Dark;
            Color blue = chalk ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(110, 120, 160, 220);
            Color red = chalk ? Color.FromArgb(120, 247, 215, 116) : Color.FromArgb(150, 220, 80, 80);
            float s = W.DpiScaleAt(new Point(vs.X + w / 2, vs.Y + h / 2));
            float margin = 120 * s;
            float z = Math.Max(Settings.MinZoom, Math.Min(Settings.MaxZoom, Cfg.BoardZoom));
            using (Pen pb = new Pen(blue, 1.2f * s * Math.Max(1f, z * 0.8f)))
            using (Pen pr = new Pen(red, 1.6f * s * Math.Max(1f, z * 0.8f)))
            {
                if (Cfg.BoardBg == 1)
                {
                    float step = 32 * s * z;
                    float y0 = 190 * s;
                    // heel ver ingezoomd: past er geen groep onder de balk, zet er dan een in het midden
                    if (y0 + step * 3 >= h - 40 * s) y0 = Math.Max(10 * s, (h - step * 3) / 2);
                    for (float y = y0; y + step * 3 < h - 10 * s; y += step * 5)
                    {
                        g.DrawLine(pb, margin, y, w - margin, y);
                        g.DrawLine(pb, margin, y + step, w - margin, y + step);
                        g.DrawLine(pr, margin, y + step * 2, w - margin, y + step * 2);
                        g.DrawLine(pb, margin, y + step * 3, w - margin, y + step * 3);
                    }
                }
                else if (Cfg.BoardBg == 2)
                {
                    float step = 32 * s * z;
                    using (Pen pg = new Pen(Color.FromArgb(chalk ? 40 : 70, blue), 1f))
                    {
                        for (float x = 0; x < w; x += step) g.DrawLine(pg, x, 0, x, h);
                        for (float y = 0; y < h; y += step) g.DrawLine(pg, 0, y, w, y);
                    }
                }
                else if (Cfg.BoardBg == 3)
                {
                    float step = 44 * s * z;
                    for (float y = 150 * s; y < h - 30 * s; y += step) g.DrawLine(pb, margin, y, w - margin, y);
                }
            }
            string info = (autoSavedAt == DateTime.MinValue ? L.T("Automatisch bewaren staat aan") : L.T("Automatisch bewaard") + " · " + autoSavedAt.ToString("HH:mm"))
                + " · " + L.F("pagina {0} van {1}", pageIndex + 1, pages.Count);
            using (Font f = Fonts.Make(UiFont.Name, 12 * s, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(chalk ? Color.FromArgb(150, 230, 240, 230) : Color.FromArgb(150, 60, 60, 66)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Far; sf.LineAlignment = StringAlignment.Far;
                Rectangle wa = Screen.FromPoint(new Point(vs.X + w - 10, vs.Y + h - 10)).WorkingArea;
                g.DrawString(info, f, b, new RectangleF(0, 0, wa.Right - vs.X - 22 * s, wa.Bottom - vs.Y - 12 * s), sf);
            }
        }

        void CommitToLayer(Stroke s)
        {
            using (Graphics g = Graphics.FromImage(committed)) { Render.Setup(g); SafeDraw(g, s, 1f); }
            pendingDirty = U(pendingDirty, StrokeBounds(s));
            dirty = true;
        }

        bool drawErrorLogged;

        void SafeDraw(Graphics g, Stroke s, float fade) { SafeDraw(g, s, fade, 1f); }

        void SafeDraw(Graphics g, Stroke s, float fade, float alpha)
        {
            try { Render.Draw(g, s, fade, alpha); }
            catch (Exception ex) { SafeDrawError(s, ex); }
        }

        void SafeDrawError(Stroke s, Exception ex)
        {
            if (!drawErrorLogged) { drawErrorLogged = true; Log.Error("Een lijn kon niet getekend worden (" + s.Tool + ", " + s.Pts.Count + " punten)", ex); }
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
            if (s.Tool == Tool.Laser) pad = Render.LaserWidest(s.Width) / 2 + 4;
            else if (s.Tool == Tool.Arrow) pad = Math.Max(14f, s.Width * 4f) + s.Width + 4;
            else if (s.Tool == Tool.Text) pad = 8 + s.FontSize * 0.3f + Render.OutlineWidth(s) * 2;   // letters (en rand) steken soms uit
            else if (s.Tool == Tool.Stamp) pad = 8 + s.FontSize * 0.2f;
            else pad = 4 + s.Width; // de vloeiende curve kan iets buiten de punten lopen
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
            RectangleF h = FocusHole(); h.Inflate(3, 3);
            return h;
        }

        // Het lichte gat: rond, of een brede strook om een zin te lezen
        RectangleF FocusHole()
        {
            float r = Cfg.FocusRadius;
            if (Cfg.FocusShape == 1) { float w = r * 3.4f, hh = r * 0.8f; return new RectangleF(focusAt.X - w / 2, focusAt.Y - hh / 2, w, hh); }
            return new RectangleF(focusAt.X - r, focusAt.Y - r, 2 * r, 2 * r);
        }

        public void SetFocusShape(int shape)
        {
            Cfg.FocusShape = Math.Max(0, Math.Min(1, shape)); Cfg.Save();
            fullDirty = true; dirty = true;
        }

        bool FocusOn { get { return DrawMode && CurrentTool == Tool.Focus; } }

        void Tick()
        {
            Program.Ping();
            tickCount++;
            if (lasers.Count > 0 || fading.Count > 0)
            {
                DateTime now = DateTime.Now;
                lasers.RemoveAll(delegate(Stroke s) { return (now - s.Born).TotalMilliseconds > LaserHold(s) + LaserFadeMs; });
                int before = fading.Count;
                double life = Cfg.FadeSeconds * 1000.0 + 1000;
                fading.RemoveAll(delegate(Stroke s) { return (now - s.Born).TotalMilliseconds > life; });
                if (fading.Count != before) pendingDirty = full2();
                // alleen opnieuw tekenen als er echt iets aan het verdwijnen is (niet tijdens de wachttijd)
                foreach (Stroke s in lasers) if (Animating(s, true, now)) { dirty = true; break; }
                if (!dirty) foreach (Stroke s in fading) if (Animating(s, false, now)) { dirty = true; break; }
            }
            if (dirty) RenderFrame();

            // Esc werkt altijd, ook als de tekenlaag niet het actieve venster is
            bool esc = (W.GetAsyncKeyState(W.VK_ESCAPE) & 0x8000) != 0;
            // Esc: eerst een open venstertje van de balk dicht, dan pas terug naar de muis
            if (esc && !lastEsc && toolbar != null && toolbar.AnyPopupOpen) { toolbar.ClosePopups(); lastEsc = true; return; }
            if (esc && !lastEsc && DrawMode && typing != null) { CloseTextInput(true); lastEsc = true; return; }   // ook als het toetsenbord ergens anders zat
            if (esc && !lastEsc && DrawMode && typing == null)
            {
                if (selected != null) Deselect(); else SetTool(Tool.Mouse);
            }
            lastEsc = esc;

            // Bovenop blijven liggen (taakbalk, startmenu en Zoom duwen ons anders weg)
            if ((DateTime.Now - lastTopAt).TotalMilliseconds > 900 && IsHandleCreated && !W.OtherTopmostInFront())
            {
                lastTopAt = DateTime.Now;
                if (Visible) W.KeepOnTop(Handle);
                if (toolbar != null) toolbar.KeepAllOnTop();
            }

            // Automatisch bewaren, 1,5 seconde na de laatste wijziging
            if (needSave && (DateTime.Now - changedAt).TotalMilliseconds > 1500) SaveNow();

            // knipperend streepje tijdens het typen
            if (typing != null && (DateTime.Now - caretAt).TotalMilliseconds > 530) { caretOn = !caretOn; caretAt = DateTime.Now; dirty = true; }

            // Rustig aan als er niets gebeurt: minder vaak wakker worden spaart de batterij
            bool idle = !DrawMode && lasers.Count == 0 && fading.Count == 0 && !dirty && !needSave;
            int want = idle ? 100 : 15;
            if (timer.Interval != want) timer.Interval = want;
        }

        DateTime lastTopAt = DateTime.MinValue;

        Rectangle full2() { return committed == null ? Rectangle.Empty : new Rectangle(0, 0, committed.Width, committed.Height); }

        void MarkChanged() { needSave = true; changedAt = DateTime.Now; }

        // De lijst die je ziet terugzetten op zijn plek (scherm of bordpagina)
        void StoreActive()
        {
            if (BoardMode == Board.None) screenStrokes = strokes;
            else if (pageIndex >= 0 && pageIndex < pages.Count) pages[pageIndex] = strokes;
        }

        DateTime autoSavedAt = DateTime.MinValue;

        void SaveNow()
        {
            if (typingOld != null) return;   // je bent een tekst aan het wijzigen: pas bewaren als die klaar is
            StoreActive();
            try
            {
                StrokeStore.Save(screenStrokes, StrokeStore.CurrentPath);
                StrokeStore.SaveBoard(pages);
                needSave = false;   // pas na gelukt; anders probeert de timer het straks opnieuw
                autoSavedAt = DateTime.Now;
                if (BoardMode != Board.None && DrawMode) RebuildArea(BoardInfoRect());   // alleen de regel "Automatisch bewaard" bijwerken
            }
            catch (Exception ex) { Log.Error("Tekening bewaren mislukt", ex); changedAt = DateTime.Now.AddSeconds(5); }   // over ongeveer 6 s nog eens
        }

        // De knop Opslaan: tekening en instellingen vastleggen, en de tekening komt bij de volgende start terug
        public bool SaveExplicit()
        {
            CloseTextInput(true);
            SyncCfg();
            Cfg.Save();
            needSave = false;
            StoreActive();
            try
            {
                StrokeStore.Save(screenStrokes, StrokeStore.CurrentPath);
                StrokeStore.SaveBoard(pages);
                bool any = screenStrokes.Count > 0;
                foreach (List<Stroke> pg in pages) if (pg.Count > 0) any = true;
                Usage.Log("opslaan", "", "");
                if (any) File.WriteAllText(StrokeStore.KeepPath, DateTime.Now.ToString("o"));
                else if (File.Exists(StrokeStore.KeepPath)) File.Delete(StrokeStore.KeepPath);
                SavedAt = DateTime.Now;
                RefreshUi();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Opslaan mislukt", ex);
                if (tray != null) tray.ShowBalloonTip(3000, "NexusPen", L.T("Opslaan is niet gelukt") + ": " + ex.Message, ToolTipIcon.Warning);
                return false;
            }
        }

        void StartSession()
        {
            try
            {
                Directory.CreateDirectory(StrokeStore.Dir);
                bool keep = File.Exists(StrokeStore.KeepPath);
                bool crashed = File.Exists(StrokeStore.LockPath);
                // 0 = nooit bewaren, 1 = na Opslaan, 2 = altijd
                bool load = crashed || Cfg.KeepDrawing == 2 || (Cfg.KeepDrawing == 1 && keep);
                if (load)
                {
                    screenStrokes = StrokeStore.Load(StrokeStore.CurrentPath);
                    pages = StrokeStore.LoadBoard();
                    int n = screenStrokes.Count;
                    foreach (List<Stroke> pg in pages) n += pg.Count;
                    if (n > 0) { if (crashed) restoredAfterCrash = true; else restoredSaved = true; }
                    if (crashed) Log.Info("Vorige sessie niet netjes afgesloten, " + n + " lijnen teruggezet");
                }
                else
                {
                    if (File.Exists(StrokeStore.CurrentPath))
                    {
                        File.Copy(StrokeStore.CurrentPath, StrokeStore.PreviousPath, true);
                        File.Delete(StrokeStore.CurrentPath);
                    }
                    try { StrokeStore.ArchiveBoard(); } catch (Exception ex) { Log.Error("Vorig bord bewaren", ex); }
                    StrokeStore.SaveBoard(new List<List<Stroke>>());
                    pages = new List<List<Stroke>>();
                }
                if (pages.Count == 0) pages.Add(new List<Stroke>());
                strokes = screenStrokes;
                File.WriteAllText(StrokeStore.LockPath, DateTime.Now.ToString("o"));
            }
            catch (Exception ex) { Log.Error("Sessie starten", ex); if (pages.Count == 0) pages.Add(new List<Stroke>()); }
        }

        bool previousRestored;

        public void RestorePrevious()
        {
            CloseTextInput(true);
            if (previousRestored)
            {
                if (tray != null) tray.ShowBalloonTip(2500, "NexusPen", L.T("De vorige tekening staat er al."), ToolTipIcon.Info);
                return;
            }
            List<Stroke> prev = StrokeStore.Load(StrokeStore.PreviousPath);
            List<List<Stroke>> prevPages = StrokeStore.LoadPreviousBoard();
            if (prev.Count == 0 && prevPages.Count == 0)
            {
                if (tray != null) tray.ShowBalloonTip(2500, "NexusPen", L.T("Er is geen vorige tekening gevonden."), ToolTipIcon.Info);
                return;
            }
            // alleen schermtekening terug en je zit niet op het bord: dan kan Ongedaan maken het weer weghalen.
            // Anders verandert er meer dan een lijst; dan beginnen Ongedaan maken en Opnieuw vanaf hier.
            bool simple = BoardMode == Board.None && prevPages.Count == 0;
            if (simple) Checkpoint(); else { MarkChanged(); undo.Clear(); redo.Clear(); }
            StoreActive();
            screenStrokes.AddRange(prev);
            if (prevPages.Count > 0)
            {
                // het bord van de vorige keer: op lege pagina's zetten, of achter je huidige pagina's
                bool empty = true;
                foreach (List<Stroke> pg in pages) if (pg.Count > 0) empty = false;
                if (empty) pages = prevPages; else pages.AddRange(prevPages);
                if (pageIndex >= pages.Count) pageIndex = 0;
            }
            strokes = BoardMode == Board.None ? screenStrokes : pages[pageIndex];
            previousRestored = true;
            StrokesVisible = true;
            MarkChanged();
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
                try { CloseTextInput(true); } catch { typing = null; }
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

        static bool IsWet(Stroke s) { return s != null && (s.Tool == Tool.Pen || s.Tool == Tool.Marker || s.Tool == Tool.Laser); }

        // Gebied rond stukje i van een lijn die je aan het schrijven bent
        Rectangle WetBox(Stroke s, int i)
        {
            float widest = s.Tool == Tool.Marker ? Render.MarkerWidth(s.Width) : s.Tool == Tool.Laser ? Render.LaserWidest(s.Width) : s.Width;
            float pad = widest / 2 + 3;
            RectangleF r = i < 0 || s.Pts.Count < 2 ? new RectangleF(s.Pts[0], SizeF.Empty) : Render.SegmentBox(s.Pts, i);
            r.Inflate(pad, pad);
            return Clip(r);
        }

        // Meteen tekenen, maar niet vaker dan eens per 4 ms (komen er sneller bewegingen, dan gaan die mee in het volgende beeldje)
        void RenderSoon()
        {
            dirty = true;
            if (perfForceRender || Perf.Now - lastRenderAt >= 4) RenderFrame();
        }

        // Is deze laser- of vervaaglijn net nieuw of aan het verdwijnen? In de wachttijd ertussen staat hij al
        // op het beeld, en wordt hij alleen opnieuw getekend waar iets anders over hem heen verandert.
        bool Animating(Stroke s, bool laser, DateTime now)
        {
            double age = (now - s.Born).TotalMilliseconds;
            if (age < 400) return true;
            return laser ? age > LaserHold(s) : age > Cfg.FadeSeconds * 1000.0;
        }

        // Hoe lang blijft een laserlijn staan voor hij verdwijnt (instelbaar: snel, langzaam of blijven staan)
        double LaserHold(Stroke s)
        {
            if (s.FadeNow) return 0;
            return Cfg.LaserMode == 1 ? 2500 : Cfg.LaserMode == 2 ? 1e12 : 700;
        }
        double LaserFadeMs { get { return Cfg.LaserMode == 1 ? 1000 : 800; } }

        // Laser die bleef staan: nu laten verdwijnen (bij een nieuwe laserlijn, Esc of een ander gereedschap)
        void FadeLasersNow()
        {
            DateTime now = DateTime.Now;
            foreach (Stroke s in lasers) if (!s.FadeNow) { s.FadeNow = true; s.Born = now; }
            if (lasers.Count > 0) dirty = true;
        }

        void RenderFrame()
        {
            dirty = false;
            if (!IsHandleCreated || frame == null) return;
            double t0 = Perf.Now;
            if (rebuildArea.Width > 0 && rebuildArea.Height > 0) { Rectangle rb = rebuildArea; rebuildArea = Rectangle.Empty; RebuildArea(rb); dirty = false; }
            int w = committed.Width, h = committed.Height;
            Rectangle full = new Rectangle(0, 0, w, h);
            Rectangle transient = Rectangle.Empty;
            bool wet = IsWet(current);
            if (current != null && !wet) transient = U(transient, StrokeBounds(current));
            DateTime nowT = DateTime.Now;
            foreach (Stroke s in lasers) if (Animating(s, true, nowT)) transient = U(transient, StrokeBounds(s));
            foreach (Stroke s in fading) if (Animating(s, false, nowT)) transient = U(transient, StrokeBounds(s));
            if (selected != null)
            {
                RectangleF sb = SelectionBox(); sb.Inflate(3, 3);
                transient = U(transient, U(Clip(sb), StrokeBounds(selected)));
            }
            bool focus = FocusOn;
            if (focus) transient = U(transient, Clip(FocusRect()));
            if (typing != null) transient = U(transient, TypingArea(typing));

            bool wasFull = fullDirty;
            Rectangle area = wasFull ? full : U(U(U(lastTransient, transient), pendingDirty), wetArea);
            area.Intersect(full);
            lastTransient = transient;
            pendingDirty = Rectangle.Empty;
            wetArea = Rectangle.Empty;
            fullDirty = false;
            if (area.Width <= 0 || area.Height <= 0) { inputAt = -1; return; }

            CopyFromCommitted(area);
            if ((transient.Width > 0 && transient.Height > 0) || focus || current != null)
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.SetClip(area);
                    Render.Setup(g);
                    DateTime now = DateTime.Now;
                    foreach (Stroke s in lasers)
                    {
                        double age = (now - s.Born).TotalMilliseconds, hold = LaserHold(s);
                        float fade = age < hold ? 1f : (float)Math.Max(0, 1 - (age - hold) / LaserFadeMs);
                        SafeDraw(g, s, fade);
                    }
                    foreach (Stroke s in fading)
                    {
                        double age = (now - s.Born).TotalMilliseconds;
                        double keep = Cfg.FadeSeconds * 1000.0;
                        float a = age < keep ? 1f : (float)Math.Max(0, 1 - (age - keep) / 1000.0);
                        if (a <= 0) continue;
                        SafeDraw(g, s, 1f, a);
                    }
                    if (current != null)
                    {
                        if (wet) { try { Render.DrawWet(g, current, area); } catch (Exception ex) { SafeDrawError(current, ex); } }
                        else SafeDraw(g, current, 1f);
                    }
                    if (selected != null)
                    {
                        if (lifted) SafeDraw(g, selected, 1f);
                        DrawSelection(g);
                    }
                    if (typing != null) DrawTyping(g);
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
            double t1 = Perf.Now;
            Perf.Frame.Add(t1 - t0);
            if (inputAt >= 0) { Perf.Ink.Add(t1 - inputAt); inputAt = -1; }
            lastRenderAt = t1;
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
                {
                    g.FillRectangle(Brushes.White, c.X - 5, c.Y - 5, 10, 10);
                    g.FillRectangle(b, c.X - 4, c.Y - 4, 8, 8);
                }
        }

        void DrawFocus(Graphics g, Rectangle area)
        {
            RectangleF hole = FocusHole();
            bool rect = Cfg.FocusShape == 1;
            using (GraphicsPath gp = new GraphicsPath(FillMode.Alternate))
            using (GraphicsPath hp = rect ? Svg.RoundRect(hole, 14) : new GraphicsPath())
            {
                if (!rect) hp.AddEllipse(hole);
                gp.AddRectangle(area);
                gp.AddPath(hp, false);
                g.CompositingMode = CompositingMode.SourceOver;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(185, 0, 0, 0))) g.FillPath(b, gp);
                using (Pen p = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f)) g.DrawPath(p, hp);
            }
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

        // Na het wijzigen in Instellingen: de twee sneltoetsen die overal werken opnieuw vastleggen. Geeft een foutmelding terug of null.
        public string RegisterGlobalHotkeys()
        {
            if (!IsHandleCreated) return null;
            W.UnregisterHotKey(Handle, 1); W.UnregisterHotKey(Handle, 2);
            string err = null;
            int mods, vk;
            if (!Settings.ParseHotkey(Cfg.HotkeyDraw, out mods, out vk) || !W.RegisterHotKey(Handle, 1, mods | W.MOD_NOREPEAT, vk))
                err = L.F("{0} is al in gebruik door een ander programma.", Cfg.HotkeyDraw);
            if (!Settings.ParseHotkey(Cfg.HotkeyHide, out mods, out vk) || !W.RegisterHotKey(Handle, 2, mods | W.MOD_NOREPEAT, vk))
                err = L.F("{0} is al in gebruik door een ander programma.", Cfg.HotkeyHide);
            SetupTrayMenu();
            return err;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            toolbar = new Toolbar(this);
            toolbar.Owner = this;
            toolbar.Show();
            SetupTray();
            UpdateTrayIcon();
            L.Changed += delegate { SetupTrayMenu(); if (toolbar != null) toolbar.Restyle(); };
            Rebuild();
            RenderFrame();
            Usage.Log("start", Program.Version, Screen.AllScreens.Length + " scherm(en)");
            // Even later, als alles staat: vensters klaarzetten en een keer onzichtbaar tekenen,
            // dan hapert de eerste echte klik en de eerste lijn niet
            System.Windows.Forms.Timer warm = new System.Windows.Forms.Timer();
            warm.Interval = 400;
            warm.Tick += delegate { warm.Stop(); warm.Dispose(); WarmUp(); };
            warm.Start();
            if (StartCollapsed) CollapseAll();
            if (restoredAfterCrash)
                tray.ShowBalloonTip(4000, "NexusPen", L.T("Je tekening van de vorige keer is teruggezet, want NexusPen was onverwacht gestopt. Alles wissen kan met de Delete-toets."), ToolTipIcon.Info);
            else if (restoredSaved)
                tray.ShowBalloonTip(4000, "NexusPen", L.T("Je opgeslagen tekening staat er weer. Liever leeg beginnen? Druk op Delete en daarna op Opslaan."), ToolTipIcon.Info);

            if (Cfg.UpdateCheck && !Program.SelfTest)
                Updates.CheckLater(20000, delegate(string version, string url)
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            updateUrl = url; updateShownAt = DateTime.Now;
                            tray.ShowBalloonTip(8000, "NexusPen", L.F("Er is een nieuwe versie: {0}. Klik hier om hem te downloaden.", version), ToolTipIcon.Info);
                        });
                    }
                    catch { }
                });
        }

        void WarmUp()
        {
            try
            {
                if (toolbar != null) toolbar.Prewarm();
                using (Bitmap b = new Bitmap(96, 96, PixelFormat.Format32bppPArgb))
                using (Graphics g = Graphics.FromImage(b))
                {
                    Render.Setup(g);
                    Stroke s = new Stroke(); s.Tool = Tool.Pen; s.Color = CurrentColor; s.Width = 4;
                    s.Pts.Add(new PointF(5, 5)); s.Pts.Add(new PointF(40, 30)); s.Pts.Add(new PointF(80, 10));
                    Render.Draw(g, s, 1f);
                    Render.DrawWet(g, s, new Rectangle(0, 0, 96, 96));
                    Render.Bounds(s);
                    Stroke t = new Stroke(); t.Tool = Tool.Text; t.Text = "Ab"; t.FontSize = 20; t.FontName = Cfg.FontName; t.Color = Color.Black; t.Outline = Cfg.TextOutline;
                    t.Pts.Add(new PointF(5, 40));
                    Render.Draw(g, t, 1f); Render.Bounds(t);
                    Stroke st = new Stroke(); st.Tool = Tool.Stamp; st.Text = "vink"; st.FontSize = 28; st.Color = Render.StampColor("vink");
                    st.Pts.Add(new PointF(60, 60));
                    Render.Draw(g, st, 1f);
                }
            }
            catch (Exception ex) { Log.Error("Opwarmen", ex); }
        }

        string updateUrl;
        DateTime updateShownAt;

        // Instellingen opnieuw opbouwen als de balk ergens anders is veranderd
        public void RefreshSettingsWindow() { if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Rebuild(); }

        void OnBalloonClicked(object sender, EventArgs e)
        {
            if (updateUrl == null || (DateTime.Now - updateShownAt).TotalSeconds > 12) return;   // alleen de melding over de nieuwe versie
            string u = updateUrl; updateUrl = null;
            if (!u.StartsWith("https://github.com/")) return;
            try { System.Diagnostics.Process.Start(u); } catch (Exception ex) { Log.Error("Downloadpagina openen", ex); }
        }

        // Voor de automatische test op GitHub: alles een keer openen, plaatjes maken en weer netjes sluiten
        public void RunSelfTest()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            int step = 0;
            string shots = Path.Combine(Environment.CurrentDirectory, Path.Combine("out", "schermen"));
            try { Directory.CreateDirectory(shots); } catch { }
            Action<Control, string> snap = delegate(Control c, string name)
            {
                try
                {
                    if (c == null || !c.Visible || c.Width <= 0) return;
                    BarForm bf = c as BarForm;
                    if (bf != null)
                    {
                        // zo ziet het venster er echt uit, met zachte hoeken, op een grijze achtergrond
                        using (Bitmap img = bf.RenderBitmap())
                        using (Bitmap b = new Bitmap(img.Width + 24, img.Height + 24, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(200, 200, 204)); g.DrawImage(img, new Rectangle(12, 12, img.Width, img.Height)); }
                            b.Save(Path.Combine(shots, name + ".png"), ImageFormat.Png);
                        }
                        return;
                    }
                    using (Bitmap b = new Bitmap(c.Width, c.Height))
                    {
                        c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
                        b.Save(Path.Combine(shots, name + ".png"), ImageFormat.Png);
                    }
                }
                catch (Exception ex) { Log.Error("Zelftest plaatje " + name, ex); }
            };
            Action<string> screen = delegate(string name)
            {
                try
                {
                    Rectangle r = Screen.PrimaryScreen.Bounds;
                    using (Bitmap b = new Bitmap(r.Width, r.Height))
                    {
                        using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        b.Save(Path.Combine(shots, name + ".png"), ImageFormat.Png);
                    }
                }
                catch (Exception ex) { Log.Error("Zelftest scherm " + name, ex); }
            };
            string[] pagesToShow = SettingsForm.PageIds;
            int pageStep = 0;
            t.Interval = 500;
            t.Tick += delegate
            {
                try
                {
                    switch (step++)
                    {
                        case 0:
                            screen("00-start");   // zo staat de balk er echt bij het opstarten
                            snap(toolbar, "01-werkbalk"); SetTool(Tool.Pen);
                            try
                            {
                                List<string> rep = Fonts.Report();
                                using (Bitmap b = new Bitmap(900, 40 + rep.Count * 22 + 5 * 50))
                                using (Graphics g = Graphics.FromImage(b))
                                using (Font f = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
                                {
                                    g.Clear(Color.White);
                                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                                    float yy = 10;
                                    foreach (string line in rep) { g.DrawString(line, f, Brushes.Black, 10, yy); yy += 22; }
                                    foreach (string n in new string[] { "Andika", "Atkinson Hyperlegible", "Lexend", "Nunito", "Patrick Hand" })
                                    {
                                        using (Font ff = Fonts.Make(n, 30, FontStyle.Bold)) g.DrawString(n + ": Ik ga met de trein. (" + ff.Name + ", " + ff.Style + ")", ff, Brushes.Black, 10, yy);
                                        yy += 50;
                                    }
                                    b.Save(Path.Combine(shots, "00-lettertypes.png"), ImageFormat.Png);
                                }
                            }
                            catch (Exception ex) { Log.Error("Zelftest lettertypes", ex); }
                            break;
                        case 1:
                            {
                                MenuPopup mm = new MenuPopup(this);
                                mm.AddCheck(L.T("Rond"), true, null); mm.AddCheck(L.T("Rechthoek (om te lezen)"), false, delegate { });
                                mm.AddSeparator(); mm.Add(L.T("Verbergen uit de balk"), delegate { }); mm.Add(L.T("Werkbalk aanpassen..."), delegate { });
                                mm.ShowAt(new Point(400, 300)); snap(mm, "02-menu"); mm.Close();
                            }
                            toolbar.OpenForTest("kleur"); break;
                        case 2: toolbar.SnapPopupsForTest(snap, "02-kleur"); toolbar.OpenForTest("stempel"); break;
                        case 3: toolbar.SnapPopupsForTest(snap, "03-stempel"); toolbar.OpenForTest("bord"); break;
                        case 4: toolbar.SnapPopupsForTest(snap, "04-bord"); toolbar.ClosePopups(); Cfg.BoardBg = 1; SetBoard(Board.White); break;
                        case 5: screen("05-whiteboard"); NewPage(); PrevPage(); SetBoard(Board.Black); break;
                        case 6: screen("06-krijtbord"); SetBoard(Board.None); SetTool(Tool.Focus); break;
                        case 7: SetTool(Tool.Select); Cfg.Vertical = true; ApplySettings(); break;
                        case 8: snap(toolbar, "07-werkbalk-staand"); Cfg.Vertical = false; Cfg.ShortenAt = 8; ApplySettings(); break;
                        case 9: snap(toolbar, "08-werkbalk-ingekort"); Cfg.ShortenAt = 0; ApplySettings(); CollapseAll(); break;
                        case 10: screen("09-ingeklapt"); ExpandAll(); SetTool(Tool.Mouse); break;
                        case 11: PerfTestDraw(); break;
                        case 12: screen("13-natte-inkt"); PerfTestRelease(); break;
                        case 13: screen("14-droge-inkt"); PerfTestErase(); break;
                        case 14: PerfTestReport(shots); break;
                        case 15: TypeTest(); break;
                        case 16: screen("16-tekst-typen"); TypingKey(new KeyEventArgs(Keys.Escape)); break;
                        case 17:
                            screen("17-tekst-klaar");
                            {
                                // Ctrl+Z en Ctrl+Y met het toetsenbord
                                int before = strokes.Count;
                                OnKeyDown(new KeyEventArgs(Keys.Control | Keys.Z));
                                int afterUndo = strokes.Count;
                                OnKeyDown(new KeyEventArgs(Keys.Control | Keys.Y));
                                int afterRedo = strokes.Count;
                                string r = "Ctrl+Z en Ctrl+Y: " + before + " > " + afterUndo + " > " + afterRedo;
                                Console.WriteLine(r); Log.Info("Zelftest. " + r);
                                if (!(afterUndo == before - 1 && afterRedo == before)) { Program.SelfTestErrors++; Console.Error.WriteLine("Ongedaan maken of opnieuw werkt niet: " + r); }
                            }
                            strokes.Clear(); undo.Clear(); redo.Clear(); Rebuild(); SetTool(Tool.Mouse);
                            break;
                        case 18:
                            // zwart bord, een lijn die zichzelf kruist, en de neon-laser (nog ingedrukt)
                            SetBoard(Board.Dark); SetColor(5); SetTool(Tool.Pen); SetWidth(6);
                            {
                                PointF c0 = TestPoint(0, 1);
                                SimDown(Mouse(MouseButtons.Left, new PointF(c0.X - 300, c0.Y - 120)));
                                for (int i = 1; i <= 120; i++) { double a = i / 120.0 * Math.PI * 3; SimMove(Mouse(MouseButtons.Left, new PointF(c0.X - 300 + i * 4, c0.Y - 120 + (float)Math.Sin(a) * 90))); }
                                for (int i = 0; i <= 120; i++) SimMove(Mouse(MouseButtons.Left, new PointF(c0.X + 180 - i * 4, c0.Y - 120 + (float)Math.Cos(i / 20.0) * 60)));
                                SimUp(Mouse(MouseButtons.Left, new PointF(c0.X, c0.Y)));
                                SetColor(2); SetTool(Tool.Laser); SetWidth(5);
                                SimDown(Mouse(MouseButtons.Left, new PointF(c0.X - 300, c0.Y + 120)));
                                for (int i = 1; i <= 150; i++) { double a = i / 150.0 * Math.PI * 4; SimMove(Mouse(MouseButtons.Left, new PointF(c0.X - 300 + i * 4, c0.Y + 120 + (float)Math.Sin(a) * 70))); }
                                for (int i = 0; i <= 100; i++) SimMove(Mouse(MouseButtons.Left, new PointF(c0.X + 300 - i * 5, c0.Y + 120 + (float)Math.Cos(i / 15.0) * 50)));
                            }
                            break;
                        case 19:
                            screen("18-zwart-bord-laser");
                            SimUp(Mouse(MouseButtons.Left, TestPoint(0, 1)));
                            strokes.Clear(); undo.Clear(); lasers.Clear(); SetBoard(Board.None);
                            Cfg.FocusShape = 1; SetTool(Tool.Focus); focusAt = TestPoint(0, 1); fullDirty = true; RenderFrame();
                            Console.WriteLine("Spotlicht direct: buiten alfa " + ((FramePixels(new Rectangle(20, 20, 1, 1))[0] >> 24) & 0xFF) + ", gat alfa " + ((FramePixels(new Rectangle((int)focusAt.X, (int)focusAt.Y, 1, 1))[0] >> 24) & 0xFF) + ", DrawMode " + DrawMode + ", tool " + CurrentTool);
                            break;
                        case 20:
                            Console.WriteLine("Spotlicht later: buiten alfa " + ((FramePixels(new Rectangle(20, 20, 1, 1))[0] >> 24) & 0xFF) + ", DrawMode " + DrawMode + ", tool " + CurrentTool);
                            screen("19-spotlicht-rechthoek"); Cfg.FocusShape = 0; SetTool(Tool.Mouse); break;
                        case 21:
                            // vergrootglas vast boven de werkbalk, en de timer erbij
                            Console.WriteLine("Vergrootglas beschikbaar: " + Loupe.Available);
                            if (toolbar != null) PinLoupe(new Point(toolbar.Left + 160, toolbar.Top + toolbar.Height / 2 + 40));
                            ToggleTimer();
                            break;
                        case 22:
                            screen("20-vergrootglas-timer");
                            if (lesTimer != null) snap(lesTimer, "21-timer");
                            foreach (Loupe l in pinnedLoupes) if (l.Strip != null) snap(l.Strip, "22-vergrootglas-balkje");
                            CloseAllLoupes(); ToggleTimer();
                            break;
                        case 23: ShowSettings(null); break;
                        default:
                            if (settingsForm != null && pageStep < pagesToShow.Length)
                            {
                                settingsForm.ShowPage(pagesToShow[pageStep]);
                                settingsForm.Refresh();
                                snap(settingsForm, "10-instellingen-" + (pageStep + 1).ToString("00") + "-" + pagesToShow[pageStep]);
                                pageStep++;
                                if (pageStep == pagesToShow.Length) { L.Set("en"); settingsForm.ShowPage("Algemeen"); settingsForm.Refresh(); snap(settingsForm, "11-english"); snap(toolbar, "12-werkbalk-english"); L.Set("nl");
                                    // licht thema: zijn de letters rechts ook scherp?
                                    int keepTheme = Cfg.ThemeIndex; Cfg.ThemeIndex = 1; ApplySettings(); settingsForm.Rebuild(); settingsForm.ShowPage("Uiterlijk"); settingsForm.Refresh(); snap(settingsForm, "11-licht-uiterlijk");
                                    Cfg.ThemeIndex = keepTheme; ApplySettings(); }
                                break;
                            }
                            t.Stop(); Close(); break;
                    }
                }
                catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest stap " + step, ex); Console.Error.WriteLine("Zelftest stap " + step + ": " + ex); }
            };
            Shown += delegate { t.Start(); };
        }

        // ---------- snelheid meten in de zelftest (op een echte Windows-pc van GitHub)
        Perf.Meter testMove = new Perf.Meter("Een beweging met inkt (lange lijn van 2000 punten)", 4000);
        Perf.Meter testErase = new Perf.Meter("Een beweging met de gum (500 lijnen op het scherm)", 1000);
        double testOldFull;
        int testWetDiff = -1, testWetBig = -1, testInked;
        Rectangle testArea;
        int[] testWetPixels;

        static MouseEventArgs Mouse(MouseButtons b, PointF p) { return new MouseEventArgs(b, 1, (int)Math.Round(p.X), (int)Math.Round(p.Y), 0); }

        // In de zelftest bewegen we de muis zelf; de echte muis van de testcomputer telt dan niet mee
        bool simulated;
        void SimDown(MouseEventArgs e) { simulated = true; try { OnMouseDown(e); } finally { simulated = false; } }
        void SimMove(MouseEventArgs e) { simulated = true; try { OnMouseMove(e); } finally { simulated = false; } }
        void SimUp(MouseEventArgs e) { simulated = true; try { OnMouseUp(e); } finally { simulated = false; } }
        bool RealMouseInSelfTest { get { return Program.SelfTest && !simulated; } }

        PointF TestPoint(int i, int n)
        {
            Rectangle pr = Screen.PrimaryScreen.Bounds;
            float cx = pr.X - vs.X + pr.Width / 2f, cy = pr.Y - vs.Y + pr.Height / 2f;
            float rmax = Math.Min(pr.Height, pr.Width) * 0.42f;
            double t = (double)i / n;
            double ang = t * Math.PI * 2 * 9;
            float r = (float)(rmax * (0.15 + 0.85 * t));
            return new PointF(cx + (float)(Math.Cos(ang) * r), cy + (float)(Math.Sin(ang) * r * 0.8));
        }

        int[] FramePixels(Rectangle r)
        {
            int[] px = new int[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
                Marshal.Copy(new IntPtr(bits.ToInt64() + ((long)(r.Y + y) * frame.Width + r.X) * 4), px, y * r.Width, r.Width);
            return px;
        }

        void PerfTestDraw()
        {
            try
            {
                SetTool(Tool.Pen);
                strokes.Clear(); Rebuild(); RenderFrame();
                perfForceRender = true;
                Perf.Ink.Clear(); Perf.Frame.Clear();
                const int n = 2000;
                SimDown(Mouse(MouseButtons.Left, TestPoint(0, n)));
                for (int i = 1; i < n; i++)
                {
                    double t0 = Perf.Now;
                    SimMove(Mouse(MouseButtons.Left, TestPoint(i, n)));
                    testMove.Add(Perf.Now - t0);
                }
                // zo duurde het in 1.2.0: elk beeldje de hele lijn opnieuw tekenen
                if (current != null)
                {
                    Rectangle sb = StrokeBounds(current);
                    using (Bitmap tmp = new Bitmap(Math.Max(1, sb.Right), Math.Max(1, sb.Bottom), PixelFormat.Format32bppPArgb))
                    using (Graphics g = Graphics.FromImage(tmp))
                    {
                        Render.Setup(g);
                        double t0 = Perf.Now;
                        for (int k = 0; k < 10; k++) Render.Draw(g, current, 1f);
                        testOldFull = (Perf.Now - t0) / 10;
                    }
                    testArea = sb;
                    testWetPixels = FramePixels(sb);
                }
            }
            catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest snelheid tekenen", ex); Console.Error.WriteLine(ex); }
        }

        void PerfTestRelease()
        {
            try
            {
                SimUp(Mouse(MouseButtons.Left, TestPoint(1999, 2000)));
                RenderFrame();
                if (testWetPixels != null)
                {
                    int[] dry = FramePixels(testArea);
                    testWetDiff = 0; testWetBig = 0; testInked = 0;
                    for (int i = 0; i < dry.Length; i++)
                    {
                        int a = dry[i], b = testWetPixels[i], d = 0;
                        for (int sh = 0; sh < 32; sh += 8) d = Math.Max(d, Math.Abs(((a >> sh) & 0xFF) - ((b >> sh) & 0xFF)));
                        if (((a >> 24) & 0xFF) > 1) testInked++;
                        if (d > 2) testWetDiff++;
                        if (d > 140) testWetBig++;
                    }
                }
            }
            catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest snelheid loslaten", ex); Console.Error.WriteLine(ex); }
        }

        void PerfTestErase()
        {
            try
            {
                // een volle tekening: 500 korte lijnen verspreid over het scherm
                Random rnd = new Random(3);
                Rectangle pr = Screen.PrimaryScreen.Bounds;
                for (int i = 0; i < 500; i++)
                {
                    Stroke s = new Stroke(); s.Tool = Tool.Pen; s.Color = CurrentColor; s.Width = 4;
                    PointF a = new PointF(pr.X - vs.X + rnd.Next(pr.Width), pr.Y - vs.Y + rnd.Next(pr.Height));
                    for (int k = 0; k < 30; k++) { s.Pts.Add(a); a = new PointF(a.X + rnd.Next(-6, 9), a.Y + rnd.Next(-6, 7)); }
                    strokes.Add(s);
                }
                Rebuild(); RenderFrame();
                SetTool(Tool.Eraser);
                Perf.Erase.Clear();
                PointF p0 = new PointF(pr.X - vs.X + 20, pr.Y - vs.Y + pr.Height / 2f);
                SimDown(Mouse(MouseButtons.Left, p0));
                for (int i = 0; i < 300; i++)
                {
                    PointF p = new PointF(p0.X + i * (pr.Width - 40) / 300f, p0.Y + (float)Math.Sin(i / 20.0) * pr.Height * 0.3f);
                    double t0 = Perf.Now;
                    SimMove(Mouse(MouseButtons.Left, p));
                    testErase.Add(Perf.Now - t0);
                }
                SimUp(Mouse(MouseButtons.Left, p0));
            }
            catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest snelheid gummen", ex); Console.Error.WriteLine(ex); }
        }

        void PerfTestReport(string shots)
        {
            perfForceRender = false;
            List<string> lines = new List<string>();
            lines.Add("NexusPen " + Program.Version + ": snelheid op deze pc (" + vs.Width + " x " + vs.Height + " pixels)");
            lines.Add(testMove.Summary());
            lines.Add(Perf.Ink.Summary());
            lines.Add(Perf.Frame.Summary());
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Zo was het in 1.2.0 (hele lijn opnieuw per beeldje): {0:0.0} ms per beeldje, plus wachten op de timer (tot 16 ms)", testOldFull));
            lines.Add(testErase.Summary());
            lines.Add(Perf.Erase.Summary());
            lines.Add("Natte inkt tegen droge inkt: " + testWetDiff + " van " + testInked + " pixels een fractie anders, " + testWetBig + " duidelijk anders");
            lines.Add(Perf.Click.Summary());
            foreach (string l in lines) { Console.WriteLine(l); Log.Info("Zelftest. " + l); }
            try
            {
                using (Bitmap b = new Bitmap(1100, 40 + lines.Count * 30))
                using (Graphics g = Graphics.FromImage(b))
                using (Font f = new Font("Segoe UI", 15, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    for (int i = 0; i < lines.Count; i++) g.DrawString(lines[i], f, i == 0 ? Brushes.Black : Brushes.DimGray, 14, 14 + i * 30);
                    b.Save(Path.Combine(shots, "15-snelheid.png"), ImageFormat.Png);
                }
            }
            catch (Exception ex) { Log.Error("Zelftest snelheid plaatje", ex); }
            // ruime grenzen: dit vangt het als het ooit weer per beeldje de hele lijn gaat tekenen
            if (testMove.Percentile(0.95) > 25) { Program.SelfTestErrors++; Console.Error.WriteLine("Te traag: inkt bij een lange lijn"); }
            if (testErase.Percentile(0.95) > 60) { Program.SelfTestErrors++; Console.Error.WriteLine("Te traag: gummen in een volle tekening"); }
            if (testWetBig > 20) { Program.SelfTestErrors++; Console.Error.WriteLine("Natte inkt wijkt te veel af van de droge inkt"); }
            strokes.Clear(); undo.Clear(); redo.Clear(); Rebuild();
            SetTool(Tool.Mouse);
        }

        // Zelftest: met T klikken en meteen typen, in zwart (met rand)
        void TypeTest()
        {
            try
            {
                SetColor(0);   // zwart
                SetTool(Tool.Text);
                PointF c = TestPoint(0, 1);
                SimDown(Mouse(MouseButtons.Left, new PointF(c.X - 200, c.Y)));
                foreach (char ch in "Ik ga met de trein.") OnKeyPress(new KeyPressEventArgs(ch));
                TypingKey(new KeyEventArgs(Keys.Enter));
                foreach (char ch in "Één, twee: café!") OnKeyPress(new KeyPressEventArgs(ch));
                if (typing == null || typing.Text != "Ik ga met de trein.\nÉén, twee: café!") { Program.SelfTestErrors++; Console.Error.WriteLine("Typen werkt niet"); }
                // als in een tekstverwerker: omhoog, naar het begin, een woord verder, iets invoegen, en een woord selecteren
                TypingKey(new KeyEventArgs(Keys.Up)); TypingKey(new KeyEventArgs(Keys.Home)); TypingKey(new KeyEventArgs(Keys.Control | Keys.Right));
                foreach (char ch in " echt") OnKeyPress(new KeyPressEventArgs(ch));
                TypingKey(new KeyEventArgs(Keys.Control | Keys.Right)); TypingKey(new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Right));
                string want = "Ik echt ga met de trein.\nÉén, twee: café!";
                Console.WriteLine("Tekstverwerker: '" + (typing != null ? typing.Text.Replace("\n", " / ") : "") + "', geselecteerd '" + (ed != null ? ed.Selected : "") + "'");
                if (typing == null || typing.Text != want || ed.Selected != " met") { Program.SelfTestErrors++; Console.Error.WriteLine("Pijltjes of selecteren in de tekst werken niet"); }
                // tekst met rand in alle lettertypes: geen zwarte vakjes in letters als de t
                float ty = 40;
                foreach (string fn in new string[] { "Andika", "Nunito", "Lexend", "Atkinson Hyperlegible", "Patrick Hand", "Segoe UI" })
                {
                    Stroke ts = new Stroke(); ts.Tool = Tool.Text; ts.Text = "testen " + fn; ts.FontName = fn; ts.FontSize = 40;
                    ts.Color = ColorTranslator.FromHtml("#FF8000"); ts.Outline = true; ts.OutlineWidth = 2; ts.OutlineColor = 0;
                    ts.Pts.Add(new PointF(40, 120 + ty)); ty += 52;
                    strokes.Add(ts);
                }
                Rebuild();
            }
            catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest typen", ex); Console.Error.WriteLine(ex); }
        }

        void SetupTray()
        {
            tray = new NotifyIcon();
            UpdateTrayIcon();
            SetupTrayMenu();
            tray.DoubleClick += delegate { if (allHidden) ExpandAll(); else ToggleDraw(); };
            tray.BalloonTipClicked += OnBalloonClicked;
            tray.Visible = true;
        }

        void SetupTrayMenu()
        {
            if (tray == null) return;
            string tt = "NexusPen  (" + Cfg.HotkeyDraw + " = " + L.T("tekenen aan of uit") + ")";
            tray.Text = tt.Length > 63 ? tt.Substring(0, 63) : tt;   // Windows staat niet meer dan 63 tekens toe
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add(L.T("Tekenen aan of uit") + "\t" + Cfg.HotkeyDraw, delegate { ToggleDraw(); });
            m.MenuItems.Add(L.T("Alles in- of uitklappen") + "\t" + Cfg.HotkeyHide, delegate { ToggleAllHidden(); });
            m.MenuItems.Add(L.T("Opslaan voor de volgende keer") + (Shortcuts.Combo("opslaan").Length > 0 ? "\t" + Shortcuts.Combo("opslaan") : ""), delegate { SaveExplicit(); });
            m.MenuItems.Add(L.T("Schermafbeelding maken"), delegate { Screenshot(); });
            m.MenuItems.Add(L.T("Instellingen..."), delegate { ShowSettings(null); });
            m.MenuItems.Add(L.T("Werkbalk terugzetten"), delegate { if (allHidden) ExpandAll(); toolbar.ResetPosition(); });
            m.MenuItems.Add(L.T("Vorige tekening terughalen"), delegate { RestorePrevious(); });
            m.MenuItems.Add("-");
            m.MenuItems.Add(L.T("Noodstop: terug naar de muis"), delegate { EmergencyMouseMode(); });
            m.MenuItems.Add(L.T("Foutlog openen"), delegate
            {
                if (!File.Exists(Log.FilePath)) { MessageBox.Show(L.T("Er zijn nog geen fouten gelogd."), "NexusPen"); return; }
                try { Process.Start(Log.FilePath); }
                catch { try { Process.Start(SettingsForm.Explorer, "/select,\"" + Log.FilePath + "\""); } catch (Exception ex) { Log.Error("Foutlog openen", ex); } }
            });
            m.MenuItems.Add(L.T("Afsluiten"), delegate { Close(); });
            ContextMenu old = tray.ContextMenu;
            tray.ContextMenu = m;
            if (old != null) old.Dispose();
        }

        Icon trayIcon, barIcon;

        void UpdateTrayIcon()
        {
            if (tray == null) return;
            try
            {
                Theme t = Theme;
                int size = SystemInformation.SmallIconSize.Width;
                Icon ic = Logo.MakeIcon(Math.Max(16, size), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
                tray.Icon = ic;
                // ook het logo voor de werkbalk, als die in de taakbalk staat (anders zie je het standaard-icoon van Windows)
                Icon big = Logo.MakeIcon(Math.Max(32, SystemInformation.IconSize.Width), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
                if (toolbar != null) { Icon old = toolbar.Icon; toolbar.Icon = big; if (old != null && old != big && barIcon == old) old.Dispose(); }
                barIcon = big;
                if (trayIcon != null) trayIcon.Dispose();
                trayIcon = ic;
            }
            catch (Exception ex) { Log.Error("Icoon maken", ex); }
        }

        void SyncCfg()
        {
            Cfg.ColorIndex = ColorIndex; Cfg.PaletteIndex = PaletteIndex; Cfg.Width = PenWidth;
            Cfg.Tool = (LastDrawTool == Tool.Focus || LastDrawTool == Tool.Select || LastDrawTool == Tool.Loupe) ? Tool.Pen : LastDrawTool;
            if (toolbar != null && toolbar.Visible) { Cfg.ToolbarX = toolbar.Left; Cfg.ToolbarY = toolbar.Top; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { CloseAllLoupes(); if (lesTimer != null && !lesTimer.IsDisposed) lesTimer.Dispose(); } catch { }
            try { CloseTextInput(true); } catch (Exception ex) { Log.Error("Tekst afmaken bij afsluiten", ex); }   // getypte tekst niet kwijtraken
            for (int i = 1; i <= 2; i++) W.UnregisterHotKey(Handle, i);
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            timer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            SyncCfg();
            Cfg.Save();
            SaveNow();
            Usage.ToolChanged(null);
            Usage.Log("stop", "", "");
            Usage.Flush();
            try { File.Delete(StrokeStore.LockPath); } catch { }
            Perf.LogSummary();
            Log.Info("Netjes afgesloten");
            FreeBuffers();
            base.OnFormClosed(e);
        }

        // De cursor van de tekenlaag. We zetten hem alleen als de muis echt boven de tekenlaag is,
        // anders flitst bij een klik op de werkbalk heel even de stempel of de pen op de knop.
        Cursor overlayCursor = Cursors.Cross;

        void SetOverlayCursor(Cursor c)
        {
            if (c == null) return;
            overlayCursor = c;
            try
            {
                Point mp = Control.MousePosition;
                if (IsHandleCreated && W.WindowFromPoint(new W.POINT(mp.X, mp.Y)) == Handle) W.SetCursor(c.Handle);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0020 && (m.LParam.ToInt64() & 0xFFFF) == 1)   // WM_SETCURSOR boven de tekenlaag
            {
                W.SetCursor(overlayCursor.Handle); m.Result = new IntPtr(1); return;
            }
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
        public void ShowSettings(string page)
        {
            // het bord gaat even weg voor de instellingen, en komt terug als je ze sluit
            Board keepBoard = BoardMode;
            if (DrawMode) SetTool(Tool.Mouse);
            Usage.Log("menu", "instellingen", page ?? "");
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (page != null) settingsForm.ShowPage(page);
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(this);
            settingsForm.FormClosed += delegate
            {
                settingsForm = null;
                if (keepBoard != Board.None && BoardMode == Board.None && !allHidden) SetBoard(keepBoard);
            };
            if (page != null) settingsForm.ShowPage(page);
            settingsForm.Show();
            settingsForm.Activate();
        }

        // Na elke wijziging in Instellingen: werkbalk, icoon, taal en geopende vensters bijwerken
        public void ApplySettings()
        {
            Cfg.Clamp();
            Cfg.Save();
            bool langChanged = L.Code != Cfg.Language;
            L.Set(Cfg.Language);
            UpdateTrayIcon();
            if (toolbar != null && !langChanged) toolbar.Restyle();
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                W.HideFromCapture(settingsForm.Handle, Cfg.HideFromCapture);
                settingsForm.Invalidate();
            }
            if (DrawMode && BoardMode != Board.None) Rebuild();
            if (selected != null) dirty = true;
            if (FocusOn) { fullDirty = true; dirty = true; }   // bijvoorbeeld spotlicht rond of rechthoek
            UpdateCursor();
        }

        // ---------- modus en gereedschap
        void SetDrawMode(bool on)
        {
            if (on && allHidden) ExpandAll();
            DrawMode = on;
            int ex = W.GetWindowLong(Handle, W.GWL_EXSTYLE);
            if (on) ex &= ~W.WS_EX_TRANSPARENT; else ex |= W.WS_EX_TRANSPARENT;
            W.SetWindowLong(Handle, W.GWL_EXSTYLE, ex);
            if (!on) { current = null; erasing = false; CloseTextInput(true); Deselect(); FadeLasersNow(); StopFollowLoupe(); Usage.ToolChanged("Mouse"); }
            // Met de muis werk je op je gewone scherm: het bord gaat weg. Het komt pas terug als je zelf weer een bord kiest.
            if (!on && BoardMode != Board.None && !keepBoardOnce) SetBoard(Board.None, false);
            Rebuild();
            RenderFrame();
            if (on) Activate();
            RefreshUi();
        }

        static bool HasWidth(Tool t) { return t == Tool.Pen || t == Tool.Marker || t == Tool.Laser || t == Tool.Eraser || IsShape(t); }

        public void SetTool(Tool t)
        {
            if (typing != null) CloseTextInput(true);
            if (t == Tool.Mouse) { if (DrawMode) SetDrawMode(false); RefreshUi(); return; }
            if (t != Tool.Select) Deselect();
            if (t != Tool.Laser) FadeLasersNow();
            bool wasFocus = CurrentTool == Tool.Focus;
            CurrentTool = t;
            if (t != Tool.Focus && t != Tool.Select && t != Tool.Loupe) LastDrawTool = t;
            if (IsShape(t)) LastShape = t;
            if (t == Tool.Loupe) StartFollowLoupe(); else StopFollowLoupe();
            if (Cfg.PerToolWidth && HasWidth(t)) PenWidth = Cfg.WidthFor(t, PenWidth);
            if (Cfg.PerToolWidth && t == Tool.Eraser) PenWidth = Math.Min(PenWidth, ToolCursor.MaxCircle / 4);   // oude, te grote gummaat
            Usage.ToolChanged(t.ToString());
            UpdateCursor();
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

        // ---------- vergrootglas
        Loupe followLoupe;
        readonly List<Loupe> pinnedLoupes = new List<Loupe>();

        public void ToggleLoupe()
        {
            if (DrawMode && CurrentTool == Tool.Loupe) { SetTool(LastDrawTool); return; }
            if (!Loupe.Available) { if (toolbar != null) toolbar.ShowToast(L.T("Het vergrootglas werkt niet op deze computer."), null, null); return; }
            SetTool(Tool.Loupe);
        }

        void StartFollowLoupe()
        {
            if (followLoupe != null && !followLoupe.IsDisposed) return;
            if (!Loupe.Available) return;
            try { followLoupe = new Loupe(this, true, Control.MousePosition); followLoupe.Show(); Usage.Log("vergrootglas", "aan", ""); }
            catch (Exception ex) { Log.Error("Vergrootglas tonen", ex); followLoupe = null; }
        }

        void StopFollowLoupe()
        {
            if (followLoupe == null) return;
            Loupe l = followLoupe; followLoupe = null;
            try { if (!l.IsDisposed) l.Close(); } catch { }
        }

        // Klik met het vergrootglas: een vast glas op deze plek (hoogstens drie tegelijk)
        void PinLoupe(Point screenPt)
        {
            if (!Loupe.Available) return;
            if (pinnedLoupes.Count >= 3) { Loupe oldest = pinnedLoupes[0]; oldest.Close(); }
            try
            {
                Loupe l = new Loupe(this, false, screenPt);
                pinnedLoupes.Add(l);
                l.Show();
                Usage.Log("vergrootglas", "vastzetten", "");
            }
            catch (Exception ex) { Log.Error("Vergrootglas vastzetten", ex); }
        }

        public void LoupeClosed(Loupe l)
        {
            pinnedLoupes.Remove(l);
            if (followLoupe == l) followLoupe = null;
        }

        void CloseAllLoupes()
        {
            StopFollowLoupe();
            foreach (Loupe l in pinnedLoupes.ToArray()) { try { l.Close(); } catch { } }
            pinnedLoupes.Clear();
        }

        public void SetLoupeShape(int shape)
        {
            Cfg.LoupeShape = shape; Cfg.Save();
            if (followLoupe != null) { StopFollowLoupe(); if (DrawMode && CurrentTool == Tool.Loupe) StartFollowLoupe(); }
        }

        public void SetLoupeZoom(float z)
        {
            Cfg.LoupeZoom = z; Cfg.Save();
            if (followLoupe != null) { StopFollowLoupe(); StartFollowLoupe(); }
        }

        // ---------- timer
        TimerForm lesTimer;
        bool timerWasVisible;
        public bool TimerVisible { get { return lesTimer != null && !lesTimer.IsDisposed && lesTimer.Visible; } }

        public void ToggleTimer()
        {
            if (TimerVisible) { lesTimer.Hide(); RefreshUi(); return; }
            if (lesTimer == null || lesTimer.IsDisposed) lesTimer = new TimerForm(this);
            Point at = toolbar != null ? new Point(toolbar.Left, toolbar.Bottom + 10) : Control.MousePosition;
            lesTimer.ShowNear(at);
            Usage.Log("timer", "open", "");
            RefreshUi();
        }

        // Kleur kiezen pakt weer een tekengereedschap als je aan het gummen of selecteren was
        public void SetColor(int i)
        {
            ColorIndex = Math.Max(0, Math.Min(ColorCount - 1, i));
            Usage.Log("kleur", Palette.NameOf(PaletteIndex, CurrentColor), "");
            if (selected != null) { RecolorSelected(CurrentColor); return; }
            // alleen de kleur: geen ander gereedschap en geen bord erbij (dat kies je zelf)
            Cfg.ColorIndex = ColorIndex;
            RefreshUi();
        }

        // Afsluiten met de knop: even vragen, dan gaat het nooit per ongeluk. De tekening wordt bewaard (SaveNow bij sluiten).
        public void AskQuit()
        {
            string msg = Cfg.KeepDrawing == 1 && StrokeCountAll() > 0
                ? L.T("NexusPen afsluiten? Wil je je tekening de volgende keer terugzien, druk dan eerst op Opslaan.")
                : L.T("NexusPen afsluiten?");
            if (MessageBox.Show(this, msg, "NexusPen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Usage.Log("afsluiten", "", "knop");
                Close();
            }
        }

        int StrokeCountAll()
        {
            StoreActive();
            int n = screenStrokes.Count;
            foreach (List<Stroke> pg in pages) n += pg.Count;
            return n;
        }

        // Kleur kiezen vanuit Instellingen: alleen onthouden, niet meteen gaan tekenen
        public void ChooseColorQuietly(int i)
        {
            ColorIndex = Math.Max(0, Math.Min(ColorCount - 1, i));
            Cfg.ColorIndex = ColorIndex;
            RefreshUi();
        }

        public void SetPalette(int p)
        {
            PaletteIndex = Math.Max(0, Math.Min(Palette.Sets.Length - 1, p));
            Cfg.PaletteIndex = PaletteIndex;
            Usage.Log("palet", Palette.Names[PaletteIndex], "");
            RefreshUi();
        }

        public void SetWidth(float w)
        {
            // de gum niet groter dan het rondje van de muis kan worden, anders zie je niet wat je gumt
            // (deelt de gum zijn dikte met de pen en is die al groter, dan alleen kleiner maken, nooit opeens terugspringen)
            float max = CurrentTool == Tool.Eraser ? Math.Max(ToolCursor.MaxCircle / 4, Math.Min(Settings.MaxWidth, PenWidth)) : Settings.MaxWidth;
            w = Math.Max(1, Math.Min(max, w));
            if (Math.Abs(w - PenWidth) < 0.01f) return;   // niets veranderd: niets opnieuw tekenen
            PenWidth = w;
            if (Cfg.PerToolWidth && HasWidth(CurrentTool)) Cfg.SetWidthFor(CurrentTool, PenWidth);
            RefreshUi();
        }

        DateTime lastWidthLog = DateTime.MinValue;

        public void Wheel(int delta)
        {
            if (DrawMode && CurrentTool == Tool.Loupe)
            {
                if (followLoupe != null) followLoupe.ChangeZoom(delta);
                return;
            }
            if (FocusOn)
            {
                Cfg.FocusRadius = Math.Max(60, Math.Min(700, Cfg.FocusRadius + (delta > 0 ? 20 : -20)));
                dirty = true;
                return;
            }
            if (!Cfg.WheelWidth) return;
            if (DrawMode && (CurrentTool == Tool.Text || typing != null || CurrentTool == Tool.Stamp))
            {
                // touchpad en knijpen geven veel kleine stapjes: pas een stand verder na een hele 'klik' van het wiel
                if (Math.Sign(delta) != Math.Sign(wheelAcc)) wheelAcc = 0;
                wheelAcc += delta;
                if (Math.Abs(wheelAcc) < 120) return;
                int d = wheelAcc; wheelAcc = 0;
                if (CurrentTool == Tool.Stamp) WheelStamp(d); else WheelText(d);
                return;
            }
            float step = PenWidth < 6 ? 1 : 2;
            SetWidth(PenWidth + (delta > 0 ? step : -step));
            if ((DateTime.Now - lastWidthLog).TotalSeconds > 3) { lastWidthLog = DateTime.Now; Usage.Log("dikte", ((int)PenWidth).ToString(), "muiswiel"); }
            // bij de gum zie je de grootte al aan het rondje: geen extra venstertje
            if (toolbar != null && CurrentTool != Tool.Eraser) toolbar.ShowWidthTip(Control.MousePosition, PenWidth, 1, Settings.MaxWidth, L.T("Dikte"));
        }

        // Vaste standen voor de grootte van tekst en stempels (zoals bij Epic Pen)
        public static readonly float[] TextSizes = { 14, 18, 22, 26, 30, 36, 42, 48, 56, 64, 72, 84, 96, 120, 144, 170, 200, 240, 300 };
        public static readonly float[] StampPxSizes = { 20, 24, 28, 34, 40, 48, 56, 68, 80, 96, 120, 150 };

        static float NextSize(float[] list, float cur, int delta)
        {
            int best = 0;
            for (int i = 0; i < list.Length; i++) if (Math.Abs(list[i] - cur) < Math.Abs(list[best] - cur)) best = i;
            best = Math.Max(0, Math.Min(list.Length - 1, best + (delta > 0 ? 1 : -1)));
            return list[best];
        }

        int wheelAcc;

        void WheelText(int delta)
        {
            float cur = typing != null ? typing.FontSize : Cfg.TextSize;
            float nw = NextSize(TextSizes, cur, delta);
            Cfg.TextSize = nw;
            if (typing != null)
            {
                // de tekst die je typt groeit mee; de klikplek (midden van de eerste regel) blijft staan
                float oldH = 0, newH = 0;
                try { oldH = Render.LineHeight(typing); } catch { }
                typing.FontSize = nw;
                try { newH = Render.LineHeight(typing); } catch { }
                typing.Pts[0] = new PointF(typing.Pts[0].X, typing.Pts[0].Y - (newH - oldH) / 2);
                TypingChanged();
            }
            UpdateCursor();
            if (toolbar != null) toolbar.ShowWidthTip(Control.MousePosition, nw, TextSizes[0], TextSizes[TextSizes.Length - 1], L.T("Tekstgrootte"));
        }

        public float StampPx { get { return Cfg.StampPx > 0 ? Cfg.StampPx : Render.StampSizes[Cfg.StampSize]; } }

        void WheelStamp(int delta)
        {
            Cfg.StampPx = NextSize(StampPxSizes, StampPx, delta);
            UpdateCursor();
            if (toolbar != null) toolbar.ShowWidthTip(Control.MousePosition, Cfg.StampPx, StampPxSizes[0], StampPxSizes[StampPxSizes.Length - 1], L.T("Stempel"));
        }

        // ---------- bord en pagina's
        List<List<Stroke>> screenUndo = new List<List<Stroke>>(), screenRedo = new List<List<Stroke>>();

        public void SetBoard(Board b) { SetBoard(b, true); }

        void SetBoard(Board b, bool byUser)
        {
            CloseTextInput(true);
            StoreActive();
            Deselect();
            bool wasScreen = BoardMode == Board.None;
            if (wasScreen && b != Board.None) { screenUndo = new List<List<Stroke>>(undo); screenRedo = new List<List<Stroke>>(redo); undo.Clear(); redo.Clear(); }
            else if (!wasScreen && b == Board.None) { undo.Clear(); redo.Clear(); undo.AddRange(screenUndo); redo.AddRange(screenRedo); }
            else if (!wasScreen && b != Board.None && b != BoardMode) { /* zelfde pagina, andere kleur: geschiedenis blijft */ }
            BoardMode = b;
            strokes = b == Board.None ? screenStrokes : pages[pageIndex];
            if (byUser) Usage.Log("bord", b.ToString(), "");
            // een bord kiezen zet geen tekengereedschap aan: je komt op Selecteren en kiest zelf wat je nodig hebt
            if (byUser && b != Board.None && !DrawMode) SetTool(Tool.Select);
            Rebuild();
            RefreshUi();
        }

        public void CycleBoard() { SetBoard(BoardMode == Board.None ? Board.White : BoardMode == Board.White ? Board.Black : BoardMode == Board.Black ? Board.Dark : Board.None); }

        // Lijnen en ruitjes groter of kleiner (om in te zoomen)
        // In- of uitzoomen op het bord: dir +1 groter, -1 kleiner (in stapjes van 20%)
        public void ZoomBoard(int dir)
        {
            float z = dir == 0 ? Cfg.BoardZoom : Cfg.BoardZoom * (dir > 0 ? 1.2f : 1 / 1.2f);
            if (Math.Abs(z - 1) < 0.05f) z = 1;
            Cfg.BoardZoom = Math.Max(Settings.MinZoom, Math.Min(Settings.MaxZoom, z)); Cfg.Save();
            if (BoardMode == Board.None) SetBoard(Board.White); else Rebuild();
        }

        void GoToPage(int i)
        {
            CloseTextInput(true);
            if (BoardMode == Board.None) { SetBoard(Board.White); }
            StoreActive();
            pageIndex = Math.Max(0, Math.Min(pages.Count - 1, i));
            strokes = pages[pageIndex];
            undo.Clear(); redo.Clear();
            Deselect();
            MarkChanged();
            Rebuild();
            RefreshUi();
        }

        public void PrevPage() { GoToPage(pageIndex - 1); }
        public void NextPage() { GoToPage(pageIndex + 1); }
        public void NewPage() { StoreActive(); pages.Insert(pageIndex + 1, new List<Stroke>()); GoToPage(pageIndex + 1); }

        // Oog: alle tekeningen even weg (en terug naar de muis). Nog een keer klikken = weer tonen.
        public void ToggleStrokesVisible()
        {
            StrokesVisible = !StrokesVisible;
            // op een bord blijft het bord staan; anders terug naar de muis
            if (!StrokesVisible) { Deselect(); if (DrawMode && BoardMode == Board.None) SetDrawMode(false); }
            Rebuild();
            RefreshUi();
        }

        // ---------- inklappen: alles weg (balk, tekeningen, bord), alleen het logo blijft
        public bool IsCollapsed { get { return allHidden; } }

        // Inklappen onthoudt waar je was (tekenen, gereedschap, bord en pagina); uitklappen gaat daar precies verder
        bool keepBoardOnce, collapsedDraw;
        Tool collapsedTool = Tool.Pen;

        public void CollapseAll()
        {
            if (allHidden) return;
            collapsedDraw = DrawMode; collapsedTool = CurrentTool;
            // alles weg: ook de vergrootglazen; de timer loopt door maar is even onzichtbaar
            CloseAllLoupes();
            timerWasVisible = TimerVisible; if (timerWasVisible) lesTimer.Hide();
            if (DrawMode) { keepBoardOnce = true; try { SetDrawMode(false); } finally { keepBoardOnce = false; } }
            Deselect();
            allHidden = true;
            Hide();
            if (toolbar != null) toolbar.ShowCollapsed();
            Usage.Log("inklappen", "", "");
        }

        public void ExpandAll()
        {
            if (!allHidden) return;
            allHidden = false;
            Show();
            Rebuild();
            RenderFrame();
            if (toolbar != null) toolbar.ShowExpanded();
            if (timerWasVisible && lesTimer != null && !lesTimer.IsDisposed) { lesTimer.Show(); timerWasVisible = false; }
            if (collapsedDraw && !DrawMode) { collapsedDraw = false; SetDrawMode(true); if (collapsedTool != Tool.Mouse && CurrentTool != collapsedTool) SetTool(collapsedTool); }
            Usage.Log("uitklappen", "", "");
        }

        public void ToggleAllHidden() { if (allHidden) ExpandAll(); else CollapseAll(); }

        void RefreshUi() { UpdateCursor(); if (toolbar != null) toolbar.Refresh2(); }

        // De cursor laat zien wat je gaat doen: dikte en kleur van de pen, grootte van gum, stempel of tekst
        public void UpdateCursor()
        {
            Cursor c = null;
            try
            {
                switch (CurrentTool)
                {
                    case Tool.Pen:
                    {
                        Color col = CurrentColor;
                        c = ToolCursor.For("pen|" + PenWidth + "|" + col.ToArgb(), delegate { return ToolCursor.Circle(PenWidth, col); });
                        break;
                    }
                    case Tool.Marker:
                    {
                        Color col = Color.FromArgb(110, CurrentColor); float w = Render.MarkerWidth(PenWidth);
                        c = ToolCursor.For("marker|" + w + "|" + col.ToArgb(), delegate { return ToolCursor.Circle(w, col); });
                        break;
                    }
                    case Tool.Laser:
                    {
                        Color col = CurrentColor;
                        c = ToolCursor.For("laser|" + col.ToArgb(), delegate { return ToolCursor.Circle(9, col); });
                        break;
                    }
                    case Tool.Eraser:
                    {
                        float d = EraserRadius * 2;
                        c = ToolCursor.For("gum|" + d, delegate { return ToolCursor.Circle(d, Color.FromArgb(70, 255, 255, 255)); });
                        break;
                    }
                    case Tool.Text: c = Cursors.IBeam; break;   // de gewone tekstcursor van Windows
                    case Tool.Stamp:
                    {
                        string kind = Render.StampKinds[Cfg.StampKind];
                        Stroke st = new Stroke(); st.Tool = Tool.Stamp; st.FontSize = StampPx; st.Color = Render.StampColor(kind);
                        st.Text = kind == "nummer" ? "#" + Cfg.StampNext : kind; st.Pts.Add(PointF.Empty);
                        c = ToolCursor.For("stempel|" + st.Text + "|" + st.FontSize, delegate { return ToolCursor.Stamp(st); });
                        break;
                    }
                    case Tool.Select: case Tool.Focus: case Tool.Loupe: c = Cursors.Default; break;
                }
            }
            catch (Exception ex) { Log.Error("Cursor", ex); }
            SetOverlayCursor(c ?? Cursors.Cross);
        }

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
            CloseTextInput(true);
            if (undo.Count == 0) return;
            Deselect();
            redo.Add(strokes);
            strokes = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            StoreActive();
            if ((DateTime.Now - lastStrokeAt).TotalSeconds < 3) Usage.Log("ongedaan", LastDrawTool.ToString(), "direct na tekenen");
            MarkChanged();
            Rebuild();
        }

        public void Redo()
        {
            CloseTextInput(true);
            if (redo.Count == 0)
            {
                // laten zien waarom er niets gebeurt (na iets nieuws tekenen is Opnieuw weg, net als in Word)
                if (toolbar != null) toolbar.ShowToast(L.T("Er is niets om opnieuw te doen. Opnieuw werkt na Ongedaan maken, tot je iets nieuws tekent."), "", null);
                return;
            }
            Deselect();
            undo.Add(strokes);
            strokes = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            StoreActive();
            MarkChanged();
            Rebuild();
        }

        DateTime lastNudge = DateTime.MinValue;

        public void ClearAll()
        {
            CloseTextInput(false);
            lasers.Clear();
            Deselect();
            if (strokes.Count == 0) { dirty = true; return; }
            Checkpoint();
            strokes = new List<Stroke>();
            StoreActive();
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

        public Color SelectedColor { get { return selected == null ? Color.Empty : selected.Color; } }
        public bool SelectionIsText { get { return selected != null && selected.Tool == Tool.Text; } }
        public bool SelectionHasOutline { get { return selected != null && selected.Outline; } }

        // Getal in het selectiebalkje: lettergrootte, stempelgrootte of lijndikte
        public int SelectedSize
        {
            get
            {
                if (selected == null) return 0;
                if (selected.Tool == Tool.Text || selected.Tool == Tool.Stamp) return (int)Math.Round(selected.FontSize);
                return (int)Math.Round(selected.Width);
            }
        }

        public void ScaleSelected(float f)
        {
            if (selected == null) return;
            EditSelected();
            ScaleStroke(selected, f, Center(Render.Bounds(selected)));
            Rebuild();
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect(), true);
        }

        static PointF Center(RectangleF r) { return new PointF(r.X + r.Width / 2, r.Y + r.Height / 2); }

        // Groter of kleiner maken rond een vast punt
        static void ScaleStroke(Stroke s, float f, PointF about)
        {
            if (s.Tool == Tool.Text)
            {
                s.FontSize = Math.Max(8, Math.Min(400, s.FontSize * f));
                PointF p = s.Pts[0];
                s.Pts[0] = new PointF(about.X + (p.X - about.X) * f, about.Y + (p.Y - about.Y) * f);
                return;
            }
            if (s.Tool == Tool.Stamp)
            {
                float z = s.FontSize > 0 ? s.FontSize : Render.StampSize(s.Width);
                s.FontSize = Math.Max(10, Math.Min(400, z * f));
                return;
            }
            for (int i = 0; i < s.Pts.Count; i++)
            {
                PointF p = s.Pts[i];
                s.Pts[i] = new PointF(about.X + (p.X - about.X) * f, about.Y + (p.Y - about.Y) * f);
            }
        }

        public void ToggleOutlineSelected()
        {
            if (selected == null || selected.Tool != Tool.Text) return;
            EditSelected();
            selected.Outline = !selected.Outline;
            if (selected.Outline) { selected.OutlineWidth = Cfg.OutlineWidth; selected.OutlineColor = Cfg.OutlineColor; }
            Rebuild();
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect(), true);
        }

        public void BringSelectedToFront()
        {
            if (selected == null) return;
            Checkpoint();
            strokes.Remove(selected);
            strokes.Add(selected);
            Rebuild();
        }

        // Klik je in de buurt van een hoekje van de selectie? Dan maak je hem groter of kleiner.
        int CornerAt(PointF p)
        {
            if (selected == null) return -1;
            RectangleF r = SelectionBox();
            PointF[] c = { r.Location, new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) };
            for (int i = 0; i < 4; i++) if (Math.Abs(p.X - c[i].X) <= 9 && Math.Abs(p.Y - c[i].Y) <= 9) return i;
            return -1;
        }

        Stroke lastDot;
        DateTime lastDotAt = DateTime.MinValue;
        bool skipDoubleClick;

        // Het puntje van de eerste klik van een dubbelklik (of null)
        Stroke FirstClickDot()
        {
            if (lastDot == null || (DateTime.Now - lastDotAt).TotalMilliseconds > 800) return null;
            if (!strokes.Contains(lastDot) && !fading.Contains(lastDot)) return null;
            RectangleF b = Render.Bounds(lastDot);
            float w = lastDot.Tool == Tool.Marker ? Render.MarkerWidth(lastDot.Width) : lastDot.Width;
            return b.Width <= w + 8 && b.Height <= w + 8 ? lastDot : null;
        }

        void RemoveDot(Stroke dot)
        {
            if (dot == null) return;
            Rectangle area = StrokeBounds(dot);
            if (fading.Remove(dot)) { pendingDirty = U(pendingDirty, area); }
            else if (strokes.Remove(dot))
            {
                if (undo.Count > 0) undo.RemoveAt(undo.Count - 1);
                rebuildArea = U(rebuildArea, area);
                MarkChanged();
            }
            lastDot = null;
        }

        Stroke TopHit(PointF p, Stroke skip)
        {
            for (int i = strokes.Count - 1; i >= 0; i--)
                if (strokes[i] != skip && Render.Hit(strokes[i], p, 6)) return strokes[i];
            return null;
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

        float EraserRadius { get { return Math.Max(4, Math.Min(ToolCursor.MaxCircle / 2, PenWidth * 2)); } }   // ook een kleine gum voor precies werk

        void EraseAt(PointF p)
        {
            double t0 = Perf.Now;
            bool removed = false;
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                if (Render.Hit(strokes[i], p, EraserRadius))
                {
                    if (!erasedThisDrag) { Checkpoint(); erasedThisDrag = true; }
                    if (strokes[i] == selected) Deselect();
                    // alleen het stukje waar de lijn stond opnieuw tekenen, en alles in een beeldje samen
                    rebuildArea = U(rebuildArea, StrokeBounds(strokes[i]));
                    strokes.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed) { MarkChanged(); RenderSoon(); }
            Perf.Erase.Add(Perf.Now - t0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!DrawMode || RealMouseInSelfTest) return;
            if (e.Clicks < 2) skipDoubleClick = false;
            if (toolbar != null) toolbar.ClosePopups();
            PointF p = e.Location;
            if (typing != null && e.Button == MouseButtons.Left && TextMouseDown(e)) return;
            if (typing != null) CloseTextInput(true);   // ergens anders geklikt: de tekst is klaar
            if (CurrentTool == Tool.Focus) return;
            if (CurrentTool == Tool.Loupe) { if (e.Button == MouseButtons.Left) PinLoupe(PointToScreen(e.Location)); return; }
            // dubbelklik op iets wat er staat (met pen, markeerstift of een vorm): meteen selecteren
            if (e.Button == MouseButtons.Left && e.Clicks >= 2 && StrokesVisible && (CurrentTool == Tool.Pen || CurrentTool == Tool.Marker || IsShape(CurrentTool)))
            {
                // alleen als je echt iets raakt (anders is het gewoon een tweede puntje, zoals bij een trema)
                Stroke dot = FirstClickDot();
                Stroke hit = TopHit(p, dot);
                if (hit != null)
                {
                    RemoveDot(dot);
                    SetTool(Tool.Select); SelectStroke(hit);
                    skipDoubleClick = true;   // niet meteen ook de tekst openen
                    return;
                }
            }
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && CurrentTool == Tool.Eraser))
            {
                if (current != null) { pendingDirty = U(pendingDirty, StrokeBounds(current)); current = null; }
                erasing = true; erasedThisDrag = false;
                EraseAt(p);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (CurrentTool == Tool.Select)
            {
                if (!StrokesVisible) return;
                int corner = CornerAt(p);
                if (corner >= 0)
                {
                    // hoekje slepen: groter of kleiner, het tegenoverliggende hoekje blijft staan
                    RectangleF r = SelectionBox();
                    PointF[] c = { r.Location, new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) };
                    resizeAnchor = c[3 - corner];
                    resizeStartDist = Math.Max(4, Dist(p, resizeAnchor));
                    EditSelected();
                    resizeOriginal = selected.Clone();
                    resizing = true; lifted = true; Rebuild();
                    if (toolbar != null) toolbar.HideMiniBar();
                    return;
                }
                Stroke hit = (selected != null && SelectionBox().Contains(p)) ? selected : TopHit(p);
                if (hit == null) { Deselect(); return; }
                if (hit != selected) SelectStroke(hit);
                dragging = true; movedThisDrag = false; dragLast = p;
                if (toolbar != null) toolbar.HideMiniBar();
                return;
            }
            if (CurrentTool == Tool.Text)
            {
                // klik op een tekst die er al staat: die wijzigen. Anders: nieuwe tekst op deze plek.
                Stroke onText = StrokesVisible ? TopHit(p) : null;
                if (onText != null && onText.Tool == Tool.Text)
                {
                    OpenTextInput(e.Location, onText);
                    // ingedrukt houden en bewegen: de hele tekst verplaatsen; gewoon klikken: het streepje komt waar je klikte
                    if (typing != null) { textDrag = 5; textDragFrom = p; textPtFrom = typing.Pts[0]; Capture = true; }
                }
                else OpenTextInput(e.Location);
                return;
            }
            if (!StrokesVisible && CurrentTool != Tool.Laser) { StrokesVisible = true; Rebuild(); RefreshUi(); }
            if (CurrentTool == Tool.Stamp)
            {
                string kind = Render.StampKinds[Cfg.StampKind];
                Stroke st = new Stroke();
                st.Tool = Tool.Stamp; st.Width = PenWidth;
                st.Color = Render.StampColor(kind);
                st.FontSize = StampPx;
                st.Text = kind == "nummer" ? "#" + (Cfg.StampNext++) : kind;
                if (kind == "nummer") UpdateCursor();
                st.Pts.Add(p);
                Usage.Log("stempel", kind, "");
                Checkpoint();
                strokes.Add(st);
                CommitToLayer(st);
                return;
            }
            if (current != null) pendingDirty = U(pendingDirty, StrokeBounds(current));   // oude lijn zonder loslaten: netjes weghalen
            if (CurrentTool == Tool.Laser) FadeLasersNow();   // een nieuwe laserlijn: de vorige (die bleef staan) gaat weg
            current = new Stroke();
            current.Tool = CurrentTool;
            current.Color = CurrentColor;
            current.Width = PenWidth;
            if (Cfg.Chalk && CurrentTool == Tool.Pen) current.Style |= Stroke.Chalk;
            current.Pts.Add(p);
            if (IsShape(CurrentTool)) current.Pts.Add(p);
            if (IsWet(current)) wetArea = U(wetArea, WetBox(current, -1));
            inputAt = Perf.Now;
            RenderSoon();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!DrawMode || RealMouseInSelfTest) return;
            if (typing != null && TextMouseMove(e)) return;
            if (inputAt < 0) inputAt = Perf.Now;
            PointF p = e.Location;
            if (CurrentTool == Tool.Focus) { focusAt = p; dirty = true; inputAt = -1; return; }   // spotlicht: groot vlak, de timer is snel genoeg
            if (erasing) { EraseAt(p); return; }
            if (resizing && selected != null)
            {
                float f = Math.Max(0.1f, Dist(p, resizeAnchor) / resizeStartDist);
                int i = strokes.IndexOf(selected);
                Stroke c = resizeOriginal.Clone();
                ScaleStroke(c, f, resizeAnchor);
                if (i >= 0) strokes[i] = c;
                selected = c;
                RenderSoon();
                return;
            }
            if (CurrentTool == Tool.Select && !dragging)
            {
                int corner = CornerAt(p);
                SetOverlayCursor(corner == 0 || corner == 3 ? Cursors.SizeNWSE : corner >= 0 ? Cursors.SizeNESW : selected != null && SelectionBox().Contains(p) ? Cursors.SizeAll : Cursors.Default);
            }
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
                RenderSoon();
                return;
            }
            if (current == null) { inputAt = -1; return; }
            switch (current.Tool)
            {
                case Tool.Pen: case Tool.Marker: case Tool.Laser:
                {
                    int n = current.Pts.Count;
                    PointF last = current.Pts[n - 1];
                    float ddx = p.X - last.X, ddy = p.Y - last.Y;
                    if (ddx * ddx + ddy * ddy < 2.25f) { inputAt = -1; return; }
                    // natte inkt: alleen het stuk dat verandert (het vorige stukje buigt een beetje mee)
                    Rectangle before = n >= 2 ? WetBox(current, n - 2) : WetBox(current, -1);
                    current.Pts.Add(p);
                    wetArea = U(wetArea, U(before, U(WetBox(current, n - 1), n >= 2 ? WetBox(current, n - 2) : Rectangle.Empty)));
                    break;
                }
                default:
                    current.Pts[1] = Constrain(current.Pts[0], p, current.Tool);
                    break;
            }
            RenderSoon();
        }

        // Muis kwijt (Alt+Tab, Windows-toets, een venster dat ertussen komt): de lijn netjes afmaken
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (textDrag != 0 && (Control.MouseButtons & MouseButtons.Left) == 0) textDrag = 0;   // muis kwijt: stoppen met slepen
            if (Program.SelfTest) return;
            if ((Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) != 0) return;
            if (current != null || erasing || dragging || resizing)
            {
                Point mp = PointToClient(Control.MousePosition);
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, mp.X, mp.Y, 0));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (textHover) { textHover = false; RenderSoon(); }   // kader van de tekst weg als de muis weggaat
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (RealMouseInSelfTest) return;
            if (textDrag == 4 && typing != null && ed != null)
            {
                // losgelaten: de woorden naar die plek (met Ctrl: een kopie). Niet bewogen: gewoon het streepje daar.
                if (textDropIdx >= 0) ed.MoveSelectionTo(textDropIdx, (Control.ModifierKeys & Keys.Control) != 0);
                else using (TextLayout lay = new TextLayout(typing)) ed.PlaceAt(lay.Hit(e.Location), false);
                textDrag = 0; textDropIdx = -1; Capture = false; TypingChanged(); return;
            }
            if (textDrag != 0) { textDrag = 0; textDropIdx = -1; Capture = false; RenderSoon(); return; }
            if (erasing) { erasing = false; return; }
            if (resizing)
            {
                resizing = false;
                if (lifted) { lifted = false; Rebuild(); }
                if (selected != null && toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
                return;
            }
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
            lastDot = s; lastDotAt = DateTime.Now;   // voor dubbelklik: de eerste klik kan een puntje zijn
            if (s.Tool == Tool.Laser)
            {
                s.Born = DateTime.Now;
                lasers.Add(s);
            }
            else if (Cfg.Fade && (s.Tool == Tool.Pen || s.Tool == Tool.Marker))
            {
                // vervaagt na 5 seconden: niet bewaren, gewoon laten verdwijnen
                s.Born = DateTime.Now;
                fading.Add(s);
            }
            else
            {
                Checkpoint();
                strokes.Add(s);
                CommitToLayer(s);
                lastStrokeAt = DateTime.Now;
            }
            dirty = true;
        }

        static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        // Dubbelklik op een tekst (met Selecteren): tekst wijzigen
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (skipDoubleClick) { skipDoubleClick = false; return; }
            if (!DrawMode || CurrentTool != Tool.Select || selected == null || selected.Tool != Tool.Text) return;
            Stroke old = selected;
            Deselect();
            OpenTextInput(e.Location, old);   // het streepje komt waar je dubbelklikte
        }

        // Muiswiel en knijpen op het touchpad (komt binnen als Ctrl+wiel): dikte, of de grootte van het spotlicht
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!DrawMode) return;
            Wheel(e.Delta);
        }

        // ---------- toetsenbord (alleen als de tekenlaag actief is, nooit in een tekstvak)
        // Letters die je typt (met accenten en hoofdletters, zoals Windows ze geeft)
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (typing == null) return;
            e.Handled = true;
            if (e.KeyChar < ' ' || e.KeyChar == (char)127) return;
            if ((Control.ModifierKeys & Keys.Alt) != 0 && (Control.ModifierKeys & Keys.Control) == 0) return;   // Alt+letter is geen letter
            ed.Insert(e.KeyChar.ToString());   // vervangt wat geselecteerd is
            TypingChanged();
        }

        // Tijdens het typen doen sneltoetsen niets; alleen deze toetsen
        bool TypingKey(KeyEventArgs e)
        {
            if (typing == null || ed == null) return false;
            bool ctrl = e.Control && !e.Alt, shift = e.Shift;
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Escape: lastEsc = true; CloseTextInput(true); return true;
                case Keys.Enter:
                    if (ctrl) { CloseTextInput(true); return true; }
                    ed.Insert("\n"); break;
                case Keys.Back: ed.Backspace(ctrl); break;
                case Keys.Delete: ed.Delete(ctrl); break;
                case Keys.Left: ed.Left(ctrl, shift); break;
                case Keys.Right: ed.Right(ctrl, shift); break;
                case Keys.Up: using (TextLayout lay = new TextLayout(typing)) ed.Vertical(-1, shift, lay); break;
                case Keys.Down: using (TextLayout lay = new TextLayout(typing)) ed.Vertical(1, shift, lay); break;
                case Keys.Home: ed.Home(ctrl, shift); break;
                case Keys.End: ed.End(ctrl, shift); break;
                case Keys.A: if (ctrl) ed.SelectAll(); else handled = false; break;
                case Keys.C: if (ctrl) CopyText(false); else handled = false; break;
                case Keys.X: if (ctrl) CopyText(true); else handled = false; break;
                case Keys.V:
                    if (!ctrl) { handled = false; break; }
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string add = Clipboard.GetText().Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
                            if (add.Length > 2000) add = add.Substring(0, 2000);
                            ed.Insert(add);
                        }
                    }
                    catch (Exception ex) { Log.Error("Plakken", ex); }
                    break;
                case Keys.Z: if (ctrl) { if (shift) ed.Redo(); else ed.Undo(); } else handled = false; break;
                case Keys.Y: if (ctrl) ed.Redo(); else handled = false; break;
                case Keys.Oemplus: case Keys.Add: if (ctrl) WheelText(120); else handled = false; break;
                case Keys.OemMinus: case Keys.Subtract: if (ctrl) WheelText(-120); else handled = false; break;
                default: handled = false; break;
            }
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; TypingChanged(); }
            return true;   // tijdens het typen doen sneltoetsen niets; letters komen via OnKeyPress
        }

        void CopyText(bool cut)
        {
            string t = ed.HasSelection ? ed.Selected : ed.Text;
            try { if (t.Length > 0) Clipboard.SetText(t.Replace("\n", "\r\n")); } catch { }
            if (cut && ed.HasSelection) ed.DeleteSelection();
        }

        // ---- muis bij de tekst die je typt: klikken zet het streepje, slepen selecteert,
        // het rondje linksboven (of de rand) verplaatst, het hoekje rechtsonder maakt groter of kleiner
        RectangleF TextFrame(Stroke s)
        {
            RectangleF b;
            using (TextLayout lay = new TextLayout(s)) b = lay.InkBounds();
            float o = Render.OutlineWidth(s) + 6;
            b.Inflate(o, o);
            return b;
        }

        float HandleR { get { return 12; } }
        bool textHover;   // muis boven de tekst: dan pas het kader en het hoekje tonen
        PointF MoveHandle(RectangleF f) { return new PointF(f.Left - HandleR - 2, f.Top - HandleR - 2); }
        PointF SizeHandle(RectangleF f) { return new PointF(f.Right + 2, f.Bottom + 2); }

        static float Dist2(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }

        bool TextMouseDown(MouseEventArgs e)
        {
            PointF p = e.Location;
            RectangleF f = TextFrame(typing);
            float grab = (HandleR + 6) * (HandleR + 6);
            if (Dist2(p, MoveHandle(f)) <= grab || (RectangleF.Inflate(f, 8, 8).Contains(p) && !RectangleF.Inflate(f, -4, -4).Contains(p) && Dist2(p, SizeHandle(f)) > grab))
            {
                textDrag = 2; textDragFrom = p; textPtFrom = typing.Pts[0]; Capture = true;
                return true;
            }
            if (Dist2(p, SizeHandle(f)) <= grab)
            {
                textDrag = 3; textDragFrom = p; textSizeFrom = typing.FontSize;
                textDistFrom = Math.Max(10, (float)Math.Sqrt(Dist2(p, typing.Pts[0]))); Capture = true;
                return true;
            }
            if (RectangleF.Inflate(f, 4, 4).Contains(p))
            {
                int idx;
                bool inSel = false;
                using (TextLayout lay = new TextLayout(typing))
                {
                    idx = lay.Hit(p);
                    if (ed.HasSelection && e.Clicks < 2 && (Control.ModifierKeys & Keys.Shift) == 0)
                        foreach (RectangleF r in lay.SelectionRects(ed.SelStart, ed.SelEnd)) if (r.Contains(p)) inSel = true;
                }
                if (inSel)
                {
                    // op geselecteerde woorden gedrukt: slepen = die woorden verplaatsen (zoals in Word)
                    textDrag = 4; textDragFrom = p; textDropIdx = -1; Capture = true;
                    return true;
                }
                if (e.Clicks >= 2) ed.SelectWordAt(idx);
                else ed.PlaceAt(idx, (Control.ModifierKeys & Keys.Shift) != 0);
                textDrag = e.Clicks >= 2 ? 0 : 1; Capture = textDrag != 0;
                TypingChanged();
                return true;
            }
            return false;
        }

        bool TextMouseMove(MouseEventArgs e)
        {
            PointF p = e.Location;
            if (textDrag == 1)
            {
                int idx;
                using (TextLayout lay = new TextLayout(typing)) idx = lay.Hit(p);
                ed.PlaceAt(idx, true);
                TypingChanged();
                return true;
            }
            if (textDrag == 4)
            {
                if (Dist2(p, textDragFrom) < 16 && textDropIdx < 0) return true;   // nog niet echt bewogen
                using (TextLayout lay = new TextLayout(typing)) textDropIdx = lay.Hit(p);
                SetOverlayCursor(Cursors.Arrow);
                TypingChanged();
                return true;
            }
            if (textDrag == 5)
            {
                if (Dist2(p, textDragFrom) < 25) return true;
                textDrag = 2;   // ingedrukt en bewogen: de hele tekst gaat mee
            }
            if (textDrag == 2)
            {
                typing.Pts[0] = new PointF(textPtFrom.X + p.X - textDragFrom.X, textPtFrom.Y + p.Y - textDragFrom.Y);
                TypingChanged();
                return true;
            }
            if (textDrag == 3)
            {
                float d = (float)Math.Sqrt(Dist2(p, typing.Pts[0]));
                typing.FontSize = Math.Max(8, Math.Min(400, textSizeFrom * d / textDistFrom));
                Cfg.TextSize = typing.FontSize;
                TypingChanged();
                return true;
            }
            // de muiswijzer laat zien wat er kan
            RectangleF f = TextFrame(typing);
            float grab = (HandleR + 6) * (HandleR + 6);
            Cursor want = Dist2(p, SizeHandle(f)) <= grab ? Cursors.SizeNWSE
                : Dist2(p, MoveHandle(f)) <= grab || (RectangleF.Inflate(f, 8, 8).Contains(p) && !RectangleF.Inflate(f, -4, -4).Contains(p)) ? Cursors.SizeAll
                : Cursors.IBeam;
            bool near = RectangleF.Inflate(f, HandleR * 2 + 4, HandleR * 2 + 4).Contains(p);
            if (near != textHover) { textHover = near; RenderSoon(); }
            if (near || CurrentTool == Tool.Text) { SetOverlayCursor(want); return true; }
            return false;
        }

        // Tekst die je typt: selectie in Windows-blauw, het gewone zwarte streepje, en een kader met handvatten
        void DrawTyping(Graphics g)
        {
            try
            {
                using (TextLayout lay = new TextLayout(typing))
                {
                    if (ed.HasSelection)
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 0, 120, 215)))
                            foreach (RectangleF r in lay.SelectionRects(ed.SelStart, ed.SelEnd)) g.FillRectangle(b, r);
                    SafeDraw(g, typing, 1f);
                    RectangleF f = TextFrame(typing);
                    bool showFrame = textHover || textDrag != 0;
                    if (showFrame)
                    {
                        using (Pen p = new Pen(Color.FromArgb(140, 0, 0, 0), 3)) g.DrawRectangle(p, f.X, f.Y, f.Width, f.Height);
                        using (Pen p = new Pen(Color.FromArgb(230, 255, 255, 255), 1.2f)) { p.DashPattern = new float[] { 4, 3 }; g.DrawRectangle(p, f.X, f.Y, f.Width, f.Height); }
                    }
                    // rondje met vier pijltjes: verplaatsen
                    PointF mh = MoveHandle(f); float r0 = HandleR;
                    g.FillEllipse(Brushes.White, mh.X - r0, mh.Y - r0, 2 * r0, 2 * r0);
                    using (Pen p = new Pen(Color.FromArgb(200, 30, 30, 34), 1.2f)) g.DrawEllipse(p, mh.X - r0, mh.Y - r0, 2 * r0, 2 * r0);
                    Icons.Draw(g, "verplaats", new RectangleF(mh.X - r0, mh.Y - r0, 2 * r0, 2 * r0), 18, Color.FromArgb(255, 30, 30, 34), 2f);
                    // hoekje: groter of kleiner (alleen als je met de muis bij de tekst bent)
                    PointF sh = SizeHandle(f);
                    if (showFrame)
                    {
                        g.FillRectangle(Brushes.White, sh.X - 6, sh.Y - 6, 12, 12);
                        using (Pen p = new Pen(Color.FromArgb(200, 30, 30, 34), 1.2f)) g.DrawRectangle(p, sh.X - 6, sh.Y - 6, 12, 12);
                    }
                    if (textDrag == 4 && textDropIdx >= 0)
                    {
                        // waar de gesleepte woorden terechtkomen: een streepje in de accentkleur
                        RectangleF dr = lay.CaretRect(textDropIdx, 2);
                        g.FillRectangle(Brushes.White, RectangleF.Inflate(dr, 1, 1));
                        using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, dr);
                    }
                    if (caretOn && !ed.HasSelection)
                    {
                        // gewoon streepje zoals in Windows: zwart met een dun wit randje, 2 pixels breed
                        RectangleF cr = lay.CaretRect(ed.Caret, 2);
                        g.FillRectangle(Brushes.White, RectangleF.Inflate(cr, 1, 1));
                        g.FillRectangle(Brushes.Black, cr);
                    }
                }
            }
            catch (Exception ex) { SafeDrawError(typing, ex); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (TypingKey(e)) return;
            if (selected != null && !dragging && !e.Control && !e.Alt)
            {
                float step = e.Shift ? 10 : 1;
                float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                float dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                if (dx != 0 || dy != 0)
                {
                    if ((DateTime.Now - lastNudge).TotalMilliseconds > 600) EditSelected(); else MarkChanged();
                    lastNudge = DateTime.Now; Render.Move(selected, dx, dy); Rebuild();
                    if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
                    e.Handled = true; return;
                }
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) { DeleteSelected(); e.Handled = true; return; }
            }
            string act = Shortcuts.ActionFor(e);
            if (act == null && e.KeyCode == Keys.Z && e.Control && e.Shift && !e.Alt && Shortcuts.Combo("redo").Length > 0) act = "redo";   // Ctrl+Shift+Z werkt ook als Opnieuw
            if (act != null)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                Usage.Log("sneltoets", act, Shortcuts.Combo(act));
                switch (act)
                {
                    case "pen": SetTool(Tool.Pen); break;
                    case "markeer": SetTool(Tool.Marker); break;
                    case "laser": SetTool(Tool.Laser); break;
                    case "tekst": SetTool(Tool.Text); break;
                    case "gum": SetTool(Tool.Eraser); break;
                    case "selecteer": SetTool(Tool.Select); break;
                    case "lijn": SetTool(Tool.Line); break;
                    case "pijl": SetTool(Tool.Arrow); break;
                    case "rechthoek": SetTool(Tool.Rect); break;
                    case "cirkel": SetTool(Tool.Ellipse); break;
                    case "stempel": SetTool(Tool.Stamp); break;
                    case "focus": ToggleFocus(); break;
                    case "loep": ToggleLoupe(); break;
                    case "bord": CycleBoard(); break;
                    case "foto": Screenshot(); break;
                    case "oog": ToggleStrokesVisible(); break;
                    case "undo": Undo(); break;
                    case "redo": Redo(); break;
                    case "opslaan": SaveExplicit(); if (toolbar != null) toolbar.Refresh2(); break;
                    case "kopie": DuplicateSelected(); break;
                }
                return;
            }
            int n = Shortcuts.ColorFor(e);
            if (n >= 0) { SetColor(n); e.Handled = true; return; }
            if (e.Control || e.Alt) return;
            if (e.KeyCode == Keys.Delete) { ClearAll(); return; }
            if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) SetWidth(PenWidth + (PenWidth < 6 ? 1 : 2));
            if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) SetWidth(PenWidth - (PenWidth <= 6 ? 1 : 2));
        }

        // ---------- tekst: klik en typ meteen, op de plek waar je klikte (zonder tekstvak)
        void OpenTextInput(Point clientPt) { OpenTextInput(clientPt, null); }

        // Nieuwe tekst, of een bestaande tekst wijzigen (editing != null)
        void OpenTextInput(Point clientPt, Stroke editing)
        {
            CloseTextInput(true);
            if (editing != null)
            {
                // de oude tekst even uit de tekening halen; Ongedaan maken zet hem terug
                Checkpoint();
                typingUndoCount = undo.Count;
                typingIndex = strokes.IndexOf(editing);
                if (typingIndex >= 0) strokes.RemoveAt(typingIndex);
                typingOld = editing;
                typing = editing.Clone();
                int at;
                using (TextLayout lay = new TextLayout(typing)) at = lay.Hit(clientPt);
                ed = new TextEditor(typing, at);   // het streepje komt waar je klikte
                lifted = false;
                Rebuild();
            }
            else
            {
                Stroke s = new Stroke();
                s.Tool = Tool.Text; s.Color = CurrentColor; s.Width = PenWidth; s.FontSize = Cfg.TextSize; s.Text = "";
                s.FontName = Cfg.FontName; s.Outline = Cfg.TextOutline; s.OutlineWidth = Cfg.OutlineWidth; s.OutlineColor = Cfg.OutlineColor;
                // de klik komt midden in de eerste regel, zoals bij Epic Pen
                s.Pts.Add(new PointF(clientPt.X, clientPt.Y));
                float lh = s.FontSize * 1.2f;
                try { lh = Render.LineHeight(s); } catch { }
                s.Pts[0] = new PointF(clientPt.X - Render.OutlineWidth(s), clientPt.Y - lh / 2 - Render.OutlineWidth(s));
                typing = s; typingOld = null; typingIndex = -1;
                ed = new TextEditor(typing, 0);
            }
            if (!StrokesVisible) { StrokesVisible = true; Rebuild(); }
            caretOn = true; caretAt = DateTime.Now;
            // meteen kunnen typen: het toetsenbord zeker naar de tekenlaag halen (ook als je net in een ander programma was)
            try { Activate(); Focus(); W.ForceForeground(Handle); } catch { }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (typing != null && !IsDisposed && W.GetForegroundWindow() != Handle) { W.ForceForeground(Handle); Focus(); }
                });
            }
            catch { }
            RenderSoon();
        }

        Rectangle TypingArea(Stroke s)
        {
            Rectangle r = StrokeBounds(s);
            try
            {
                RectangleF f = TextFrame(s);
                f.Inflate(HandleR * 2 + 8, HandleR * 2 + 8);
                r = U(r, Clip(f));
            }
            catch { }
            return r;
        }

        void TypingChanged()
        {
            caretOn = true; caretAt = DateTime.Now;
            inputAt = Perf.Now;
            RenderSoon();
        }

        // Klaar met typen: de tekst komt in de tekening (of terug zoals hij was)
        void CloseTextInput(bool commit)
        {
            if (typing == null) return;
            Stroke t = typing;
            typing = null;
            ed = null; textDrag = 0; textHover = false;
            UpdateCursor();
            bool empty = string.IsNullOrEmpty(t.Text) || t.Text.Trim().Length == 0;
            if (typingOld != null)
            {
                int i = Math.Max(0, Math.Min(strokes.Count, typingIndex < 0 ? strokes.Count : typingIndex));
                bool same = t.Text == typingOld.Text && t.FontSize == typingOld.FontSize && t.Pts[0] == typingOld.Pts[0];
                if (!commit || same)
                {
                    strokes.Insert(i, typingOld);
                    // er is niets veranderd: de stap voor Ongedaan maken weer weghalen (alleen als hij nog bovenop ligt)
                    if (undo.Count > 0 && undo.Count == typingUndoCount) undo.RemoveAt(undo.Count - 1);
                }
                else if (!empty) strokes.Insert(i, t);
                typingOld = null; typingIndex = -1; typingUndoCount = -1;
                MarkChanged();
                Rebuild();
                return;
            }
            if (!commit || empty) { dirty = true; return; }
            Checkpoint();
            strokes.Add(t);
            CommitToLayer(t);
            lastStrokeAt = DateTime.Now;
            dirty = true;
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
                    Usage.Log("foto", "", "");
                    if (toolbar != null) toolbar.ShowToast(L.T("Opgeslagen in"), L.T("Afbeeldingen") + " > NexusPen " + L.T("en gekopieerd naar het klembord"), ScreenshotDir);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Schermafbeelding mislukt", ex);
                if (tray != null) tray.ShowBalloonTip(3000, "NexusPen", L.T("Schermafbeelding mislukt") + ": " + ex.Message, ToolTipIcon.Warning);
            }
        }

    }
}
