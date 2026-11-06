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
            L.Set(Cfg.Language);
            Usage.Init(Cfg);
            Shortcuts.Cfg = Cfg;
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
            if (DrawMode && BoardMode == Board.Black) return ChalkGreen;
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

        // Schrijflijnen, ruitjes of lijntjes, en rechtsonder een klein regeltje
        void DrawBoard(Graphics g)
        {
            int w = committed.Width, h = committed.Height;
            bool chalk = BoardMode == Board.Black;
            Color blue = chalk ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(110, 120, 160, 220);
            Color red = chalk ? Color.FromArgb(120, 247, 215, 116) : Color.FromArgb(150, 220, 80, 80);
            float s = W.DpiScaleAt(new Point(vs.X + w / 2, vs.Y + h / 2));
            float margin = 120 * s;
            using (Pen pb = new Pen(blue, 1.2f * s))
            using (Pen pr = new Pen(red, 1.6f * s))
            {
                if (Cfg.BoardBg == 1)
                {
                    float step = 32 * s;
                    for (float y = 190 * s; y + step * 3 < h - 40 * s; y += step * 5)
                    {
                        g.DrawLine(pb, margin, y, w - margin, y);
                        g.DrawLine(pb, margin, y + step, w - margin, y + step);
                        g.DrawLine(pr, margin, y + step * 2, w - margin, y + step * 2);
                        g.DrawLine(pb, margin, y + step * 3, w - margin, y + step * 3);
                    }
                }
                else if (Cfg.BoardBg == 2)
                {
                    float step = 32 * s;
                    using (Pen pg = new Pen(Color.FromArgb(chalk ? 40 : 70, blue), 1f))
                    {
                        for (float x = 0; x < w; x += step) g.DrawLine(pg, x, 0, x, h);
                        for (float y = 0; y < h; y += step) g.DrawLine(pg, 0, y, w, y);
                    }
                }
                else if (Cfg.BoardBg == 3)
                {
                    float step = 44 * s;
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
            if (lasers.Count > 0 || fading.Count > 0)
            {
                DateTime now = DateTime.Now;
                lasers.RemoveAll(delegate(Stroke s) { return (now - s.Born).TotalMilliseconds > 1500; });
                int before = fading.Count;
                fading.RemoveAll(delegate(Stroke s) { return (now - s.Born).TotalMilliseconds > 6000; });
                if (fading.Count != before) pendingDirty = full2();
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
            if ((DateTime.Now - lastTopAt).TotalMilliseconds > 900 && IsHandleCreated)
            {
                lastTopAt = DateTime.Now;
                if (Visible) W.KeepOnTop(Handle);
                if (toolbar != null) toolbar.KeepAllOnTop();
            }

            // Automatisch bewaren, 1,5 seconde na de laatste wijziging
            if (needSave && (DateTime.Now - changedAt).TotalMilliseconds > 1500) SaveNow();

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
            needSave = false;
            StoreActive();
            try
            {
                StrokeStore.Save(screenStrokes, StrokeStore.CurrentPath);
                StrokeStore.SaveBoard(pages);
                autoSavedAt = DateTime.Now;
                if (BoardMode != Board.None && DrawMode) Rebuild();   // "Automatisch bewaard" bijwerken
            }
            catch (Exception ex) { Log.Error("Tekening bewaren mislukt", ex); }
        }

        // De knop Opslaan: tekening en instellingen vastleggen, en de tekening komt bij de volgende start terug
        public bool SaveExplicit()
        {
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
                    StrokeStore.SaveBoard(new List<List<Stroke>>());
                    pages = new List<List<Stroke>>();
                }
                if (pages.Count == 0) pages.Add(new List<Stroke>());
                strokes = screenStrokes;
                File.WriteAllText(StrokeStore.LockPath, DateTime.Now.ToString("o"));
            }
            catch (Exception ex) { Log.Error("Sessie starten", ex); if (pages.Count == 0) pages.Add(new List<Stroke>()); }
        }

        public void RestorePrevious()
        {
            List<Stroke> prev = StrokeStore.Load(StrokeStore.PreviousPath);
            if (prev.Count == 0)
            {
                if (tray != null) tray.ShowBalloonTip(2500, "NexusPen", L.T("Er is geen vorige tekening gevonden."), ToolTipIcon.Info);
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
            foreach (Stroke s in fading) transient = U(transient, StrokeBounds(s));
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
                    foreach (Stroke s in fading)
                    {
                        double age = (now - s.Born).TotalMilliseconds;
                        float a = age < 5000 ? 1f : (float)Math.Max(0, 1 - (age - 5000) / 1000.0);
                        if (a <= 0) continue;
                        Stroke c = s.Clone(); c.Color = Color.FromArgb((int)(s.Color.A * a), s.Color);
                        SafeDraw(g, c, 1f);
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
                {
                    g.FillRectangle(Brushes.White, c.X - 5, c.Y - 5, 10, 10);
                    g.FillRectangle(b, c.X - 4, c.Y - 4, 8, 8);
                }
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
            L.Changed += delegate { SetupTrayMenu(); if (toolbar != null) toolbar.Restyle(); };
            Rebuild();
            RenderFrame();
            Usage.Log("start", Program.Version, Screen.AllScreens.Length + " scherm(en)");
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
                        case 0: snap(toolbar, "01-werkbalk"); SetTool(Tool.Pen); break;
                        case 1: toolbar.OpenForTest("kleur"); break;
                        case 2: toolbar.SnapPopupsForTest(snap, "02-kleur"); toolbar.OpenForTest("stempel"); break;
                        case 3: toolbar.SnapPopupsForTest(snap, "03-stempel"); toolbar.OpenForTest("bord"); break;
                        case 4: toolbar.SnapPopupsForTest(snap, "04-bord"); toolbar.ClosePopups(); Cfg.BoardBg = 1; SetBoard(Board.White); break;
                        case 5: screen("05-whiteboard"); NewPage(); PrevPage(); SetBoard(Board.Black); break;
                        case 6: screen("06-krijtbord"); SetBoard(Board.None); SetTool(Tool.Focus); break;
                        case 7: SetTool(Tool.Select); Cfg.Vertical = true; ApplySettings(); break;
                        case 8: snap(toolbar, "07-werkbalk-staand"); Cfg.Vertical = false; Cfg.ShortenAt = 8; ApplySettings(); break;
                        case 9: snap(toolbar, "08-werkbalk-ingekort"); Cfg.ShortenAt = 0; ApplySettings(); CollapseAll(); break;
                        case 10: screen("09-ingeklapt"); ExpandAll(); SetTool(Tool.Mouse); break;
                        case 11: ShowSettings(null); break;
                        default:
                            if (settingsForm != null && pageStep < pagesToShow.Length)
                            {
                                settingsForm.ShowPage(pagesToShow[pageStep]);
                                settingsForm.Refresh();
                                snap(settingsForm, "10-instellingen-" + (pageStep + 1).ToString("00") + "-" + pagesToShow[pageStep]);
                                pageStep++;
                                if (pageStep == pagesToShow.Length) { L.Set("en"); settingsForm.ShowPage("Algemeen"); settingsForm.Refresh(); snap(settingsForm, "11-english"); snap(toolbar, "12-werkbalk-english"); L.Set("nl"); }
                                break;
                            }
                            t.Stop(); Close(); break;
                    }
                }
                catch (Exception ex) { Program.SelfTestErrors++; Log.Error("Zelftest stap " + step, ex); Console.Error.WriteLine("Zelftest stap " + step + ": " + ex); }
            };
            Shown += delegate { t.Start(); };
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
            m.MenuItems.Add(L.T("Opslaan voor de volgende keer") + "\tCtrl+S", delegate { SaveExplicit(); });
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
            Usage.ToolChanged(null);
            Usage.Log("stop", "", "");
            Usage.Flush();
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
        public void ShowSettings(string page)
        {
            if (DrawMode) SetTool(Tool.Mouse);
            Usage.Log("menu", "instellingen", page ?? "");
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (page != null) settingsForm.ShowPage(page);
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(this);
            settingsForm.FormClosed += delegate { settingsForm = null; };
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
        }

        // ---------- modus en gereedschap
        void SetDrawMode(bool on)
        {
            if (on && allHidden) ExpandAll();
            DrawMode = on;
            int ex = W.GetWindowLong(Handle, W.GWL_EXSTYLE);
            if (on) ex &= ~W.WS_EX_TRANSPARENT; else ex |= W.WS_EX_TRANSPARENT;
            W.SetWindowLong(Handle, W.GWL_EXSTYLE, ex);
            if (!on) { current = null; erasing = false; CloseTextInput(true); Deselect(); Usage.ToolChanged("Mouse"); }
            // Met de muis werk je op je gewone scherm: het bord gaat even weg en komt terug zodra je weer tekent
            if (!on && BoardMode != Board.None) { boardBeforeMouse = BoardMode; SetBoard(Board.None, false); }
            else if (on && boardBeforeMouse != Board.None) { Board b = boardBeforeMouse; boardBeforeMouse = Board.None; SetBoard(b, false); }
            Rebuild();
            RenderFrame();
            if (on) Activate();
            RefreshUi();
        }

        static bool HasWidth(Tool t) { return t == Tool.Pen || t == Tool.Marker || t == Tool.Laser || t == Tool.Eraser || IsShape(t); }

        public void SetTool(Tool t)
        {
            if (t == Tool.Mouse) { if (DrawMode) SetDrawMode(false); RefreshUi(); return; }
            if (t != Tool.Select) Deselect();
            bool wasFocus = CurrentTool == Tool.Focus;
            CurrentTool = t;
            if (t != Tool.Focus && t != Tool.Select) LastDrawTool = t;
            if (IsShape(t)) LastShape = t;
            if (Cfg.PerToolWidth && HasWidth(t)) PenWidth = Cfg.WidthFor(t, PenWidth);
            Usage.ToolChanged(t.ToString());
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
            Usage.Log("kleur", Palette.Labels[PaletteIndex][ColorIndex], "");
            if (selected != null) { RecolorSelected(CurrentColor); return; }
            if (!DrawMode || CurrentTool == Tool.Eraser || CurrentTool == Tool.Focus || CurrentTool == Tool.Stamp)
                SetTool(CurrentTool == Tool.Eraser || CurrentTool == Tool.Mouse || CurrentTool == Tool.Focus || CurrentTool == Tool.Stamp ? LastDrawTool : CurrentTool);
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
            PenWidth = Math.Max(1, Math.Min(60, w));
            if (Cfg.PerToolWidth && HasWidth(CurrentTool)) Cfg.SetWidthFor(CurrentTool, PenWidth);
            RefreshUi();
        }

        DateTime lastWidthLog = DateTime.MinValue;

        public void Wheel(int delta)
        {
            if (FocusOn)
            {
                Cfg.FocusRadius = Math.Max(60, Math.Min(700, Cfg.FocusRadius + (delta > 0 ? 20 : -20)));
                dirty = true;
                return;
            }
            if (!Cfg.WheelWidth) return;
            float step = PenWidth < 6 ? 1 : 2;
            SetWidth(PenWidth + (delta > 0 ? step : -step));
            if ((DateTime.Now - lastWidthLog).TotalSeconds > 3) { lastWidthLog = DateTime.Now; Usage.Log("dikte", ((int)PenWidth).ToString(), "muiswiel"); }
            if (toolbar != null) toolbar.ShowWidthTip(Control.MousePosition, PenWidth, 1, 60, L.T("Dikte"));
        }

        // ---------- bord en pagina's
        Board boardBeforeMouse = Board.None;
        List<List<Stroke>> screenUndo = new List<List<Stroke>>(), screenRedo = new List<List<Stroke>>();

        public void SetBoard(Board b) { boardBeforeMouse = Board.None; SetBoard(b, true); }

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
            if (byUser && b != Board.None && !DrawMode) SetTool(LastDrawTool);
            Rebuild();
            RefreshUi();
        }

        public void CycleBoard() { SetBoard(BoardMode == Board.None ? Board.White : BoardMode == Board.White ? Board.Black : Board.None); }

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
            if (!StrokesVisible) { Deselect(); if (DrawMode) SetDrawMode(false); }
            Rebuild();
            RefreshUi();
        }

        // ---------- inklappen: alles weg (balk, tekeningen, bord), alleen het logo blijft
        public bool IsCollapsed { get { return allHidden; } }

        public void CollapseAll()
        {
            if (allHidden) return;
            if (DrawMode) SetDrawMode(false);
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
            Usage.Log("uitklappen", "", "");
        }

        public void ToggleAllHidden() { if (allHidden) ExpandAll(); else CollapseAll(); }

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
            if (redo.Count == 0) return;
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
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
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
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
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
            if (CurrentTool == Tool.Text) { OpenTextInput(e.Location); return; }
            if (!StrokesVisible && CurrentTool != Tool.Laser) { StrokesVisible = true; Rebuild(); RefreshUi(); }
            if (CurrentTool == Tool.Stamp)
            {
                string kind = Render.StampKinds[Cfg.StampKind];
                Stroke st = new Stroke();
                st.Tool = Tool.Stamp; st.Width = PenWidth;
                st.Color = Render.StampColor(kind);
                st.FontSize = Render.StampSizes[Cfg.StampSize];
                st.Text = kind == "nummer" ? "#" + (Cfg.StampNext++) : kind;
                st.Pts.Add(p);
                Usage.Log("stempel", kind, "");
                Checkpoint();
                strokes.Add(st);
                CommitToLayer(st);
                return;
            }
            current = new Stroke();
            current.Tool = CurrentTool;
            current.Color = CurrentColor;
            current.Width = PenWidth;
            if (Cfg.Chalk && CurrentTool == Tool.Pen) current.Style |= Stroke.Chalk;
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
            if (resizing && selected != null)
            {
                float f = Math.Max(0.1f, Dist(p, resizeAnchor) / resizeStartDist);
                int i = strokes.IndexOf(selected);
                Stroke c = resizeOriginal.Clone();
                ScaleStroke(c, f, resizeAnchor);
                if (i >= 0) strokes[i] = c;
                selected = c;
                dirty = true;
                return;
            }
            if (CurrentTool == Tool.Select && !dragging)
            {
                int corner = CornerAt(p);
                Cursor = corner == 0 || corner == 3 ? Cursors.SizeNWSE : corner >= 0 ? Cursors.SizeNESW : selected != null && SelectionBox().Contains(p) ? Cursors.SizeAll : Cursors.Default;
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
            if (!DrawMode || CurrentTool != Tool.Select || selected == null || selected.Tool != Tool.Text) return;
            Stroke old = selected;
            Deselect();
            OpenTextInput(new Point((int)old.Pts[0].X, (int)old.Pts[0].Y), old);
        }

        // Muiswiel en knijpen op het touchpad (komt binnen als Ctrl+wiel): dikte, of de grootte van het spotlicht
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!DrawMode) return;
            Wheel(e.Delta);
        }

        // ---------- toetsenbord (alleen als de tekenlaag actief is, nooit in een tekstvak)
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
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

        // ---------- tekst
        void OpenTextInput(Point clientPt) { OpenTextInput(clientPt, null); }

        // Nieuwe tekst, of een bestaande tekst wijzigen (editing != null)
        void OpenTextInput(Point clientPt, Stroke editing)
        {
            CloseTextInput(true);
            float px = editing != null ? editing.FontSize : Cfg.TextSize;
            Point screenPt = new Point(clientPt.X + vs.X, clientPt.Y + vs.Y);
            PointF strokePt = clientPt;
            Color c = editing != null ? editing.Color : CurrentColor;
            string font = editing != null ? editing.FontName : Cfg.FontName;
            bool outline = editing != null ? editing.Outline : Cfg.TextOutline;
            int ow = editing != null ? editing.OutlineWidth : Cfg.OutlineWidth, oc = editing != null ? editing.OutlineColor : Cfg.OutlineColor;
            if (editing != null) { lifted = false; }
            textInput = new TextInput(screenPt, Render.MakeFont(px, font), c, delegate(string text)
            {
                textInput = null;
                bool empty = string.IsNullOrEmpty(text) || text.Trim().Length == 0;
                if (editing != null)
                {
                    if (text == null || text == editing.Text) return;   // geannuleerd of niets veranderd
                    Checkpoint();
                    int i = strokes.IndexOf(editing);
                    if (empty) { if (i >= 0) strokes.RemoveAt(i); }
                    else if (i >= 0) { Stroke n = editing.Clone(); n.Text = text; strokes[i] = n; }
                    Rebuild();
                    return;
                }
                if (empty) return;
                Stroke s = new Stroke();
                s.Tool = Tool.Text; s.Color = c; s.Width = PenWidth; s.FontSize = px; s.Text = text;
                s.FontName = font; s.Outline = outline; s.OutlineWidth = ow; s.OutlineColor = oc;
                s.Pts.Add(strokePt);
                if (!StrokesVisible) { StrokesVisible = true; Rebuild(); }
                Checkpoint();
                strokes.Add(s);
                CommitToLayer(s);
                lastStrokeAt = DateTime.Now;
                dirty = true;
            });
            if (editing != null) textInput.SetText(editing.Text);
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
