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
    // Deel van de tekenlaag (Overlay). Zelfde klasse, opgesplitst per onderwerp zodat elk bestand overzichtelijk blijft.
    partial class Overlay
    {
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

        // Schermen veranderd (dupliceren, uitbreiden, een beamer erbij of eraf, andere resolutie of schaal).
        // Windows doet dat in een paar stappen; daarom meteen kijken en daarna nog twee keer, als alles klaar is.
        void OnDisplayChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || displayChangePending) return;
            displayChangePending = true;
            BeginInvoke((MethodInvoker)delegate
            {
                displayChangePending = false;
                FitToScreens(true);
                foreach (int ms in new int[] { 700, 2500 })
                {
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                    t.Interval = ms;
                    t.Tick += delegate { t.Stop(); t.Dispose(); if (!IsDisposed) FitToScreens(false); };
                    t.Start();
                }
            });
        }

        // De tekenlaag precies over alle schermen leggen (alleen als er iets anders is, of als het moet)
        void FitToScreens(bool force)
        {
            try
            {
                Rectangle now = SystemInformation.VirtualScreen;
                W.RECT wr; bool rectOk = W.GetWindowRect(Handle, out wr);
                bool windowOff = rectOk && (wr.Left != now.X || wr.Top != now.Y || wr.Right - wr.Left != now.Width || wr.Bottom - wr.Top != now.Height);
                bool sizeChanged = now != vs || committed == null || committed.Width != now.Width || committed.Height != now.Height;
                if (!force && !sizeChanged && !windowOff) return;
                int before = monitors != null ? CountUnique(monitors) : 1;
                List<Rectangle> oldMons = monitors; Rectangle oldVs = vs;
                List<KeyValuePair<string, Rectangle>> oldNamed = namedMons;
                monitors = W.MonitorRects(); monitorsOrdered = null; namedMons = W.NamedMonitors();
                bool screensDiffer = oldMons != null && !SameScreens(oldMons, monitors);
                if (oldNamed != null && (oldVs.Location != now.Location || screensDiffer)) NoteScreensChanging(oldNamed, oldVs);
                // net uitgebreid naar een tweede scherm, en er staat iets getekend: aanbieden om het naar de beamer te zetten
                if (CountUnique(monitors) > before && CountUnique(monitors) >= 2 && HasDrawing && toolbar != null && !Program.SelfTest && !offeredMove)
                {
                    offeredMove = true;   // een keer per keer opstarten (na de slaapstand komt het tweede scherm vaak even weg en terug)
                    toolbar.ShowToastAction(L.T("Tweede scherm erbij. Je tekening daarheen zetten?"), L.T("Verplaatsen"), "naarscherm", delegate { MoveDrawingToOtherScreen(); });
                }
                Log.Info("Schermen veranderd: " + vs + " > " + now + " (" + monitors.Count + " scherm(en))");
                vs = now;
                // rechtstreeks via Windows: WinForms rekent bij verschillende schalen soms om, dan dekte de laag maar een deel
                // (de volgorde laten zoals hij is: de balk en de venstertjes blijven boven de tekenlaag)
                W.SetWindowPos(Handle, IntPtr.Zero, vs.X, vs.Y, vs.Width, vs.Height, W.SWP_NOACTIVATE | W.SWP_NOZORDER);
                if (sizeChanged) SetupBuffers();
                Rebuild();
                RenderFrame();
                // alleen bij een echte wissel alles opnieuw passend maken (Windows meldt soms een wissel zonder dat er iets anders is)
                bool real = sizeChanged || screensDiffer || oldVs.Location != now.Location;
                if (toolbar != null) { if (real) toolbar.ScreensChanged(); else toolbar.EnsureOnScreen(); toolbar.KeepAllOnTop(); }
                if (TimerVisible && real) lesTimer.ScreensChanged();
                foreach (Loupe l in pinnedLoupes) { KeepFormOnScreen(l); if (l.Strip != null && l.Strip.Visible) KeepFormOnScreen(l.Strip); }
                KeepExtrasOnTop();
            }
            catch (Exception ex) { Log.Error("Schermen aanpassen", ex); }
        }

        // Een venster dat na een scherm-wissel nergens meer te zien is: terug op het hoofdscherm
        public static void KeepFormOnScreen(Form f)
        {
            if (f == null || f.IsDisposed || !f.Visible) return;
            Rectangle me = f.Bounds;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(new Rectangle(me.X + me.Width / 2 - 10, me.Y + me.Height / 2 - 10, 20, 20))) return;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            f.Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - me.Width, wa.Left + 60)), Math.Max(wa.Top, Math.Min(wa.Bottom - me.Height, wa.Top + 60)));
        }

        static List<Rectangle> UniqueScreens(List<Rectangle> l) { List<Rectangle> u = new List<Rectangle>(); if (l != null) foreach (Rectangle r in l) if (!u.Contains(r)) u.Add(r); return u; }
        static bool SameScreens(List<Rectangle> a, List<Rectangle> b)
        {
            List<Rectangle> ua = UniqueScreens(a), ub = UniqueScreens(b);
            if (ua.Count != ub.Count) return false;
            foreach (Rectangle r in ua) if (!ub.Contains(r)) return false;
            return true;
        }
        static List<Rectangle> InOrder(List<Rectangle> l)
        {
            List<Rectangle> u = UniqueScreens(l);
            u.Sort(delegate(Rectangle a, Rectangle b)
            {
                bool pa = a.Contains(0, 0), pb = b.Contains(0, 0);
                if (pa != pb) return pa ? -1 : 1;
                return a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
            });
            return u;
        }

        // ---- Schermen anders (uitbreiden, dupliceren, beamer eraf, andere resolutie): de tekening blijft op hetzelfde scherm.
        // Even wachten tot Windows klaar is met wisselen (na de slaapstand verdwijnt een scherm soms heel kort en komt het terug).
        List<KeyValuePair<string, Rectangle>> namedMons, remapFrom; Rectangle remapFromVs; HashSet<Stroke> remapStrokes;
        System.Windows.Forms.Timer remapTimer;

        void NoteScreensChanging(List<KeyValuePair<string, Rectangle>> oldNamed, Rectangle oldVs)
        {
            if (remapFrom == null)
            {
                CloseTextInput(true); Deselect();   // de tekst die je typt hoort er ook bij
                remapFrom = oldNamed; remapFromVs = oldVs;
                remapStrokes = new HashSet<Stroke>();
                ForEachStroke(delegate(Stroke st) { remapStrokes.Add(st); });   // alleen wat er al stond (niet wat je intussen tekent)
            }
            if (remapTimer == null)
            {
                remapTimer = new System.Windows.Forms.Timer();
                remapTimer.Tick += delegate { remapTimer.Stop(); if (!IsDisposed) RemapToScreens(); };
            }
            // Is er alleen een scherm weggevallen en staat de rest nog precies zo? Dan langer wachten:
            // na de slaapstand komt een beamer vaak pas na een paar seconden terug, dan hoeft er niets te verschuiven.
            bool onlyGone = true;
            List<KeyValuePair<string, Rectangle>> nowNamed = W.NamedMonitors();
            foreach (KeyValuePair<string, Rectangle> kv in nowNamed) if (!remapFrom.Contains(kv)) onlyGone = false;
            if (nowNamed.Count >= remapFrom.Count || remapFromVs.Location != SystemInformation.VirtualScreen.Location) onlyGone = false;
            remapTimer.Interval = Program.SelfTest ? 300 : onlyGone ? 8000 : 1500;
            remapTimer.Stop(); remapTimer.Start();
        }

        // alle tekeningen: het scherm, de bordpagina's, en wat in ongedaan maken en opnieuw zit
        void ForEachStroke(Action<Stroke> a)
        {
            HashSet<Stroke> seen = new HashSet<Stroke>();
            Action<List<Stroke>> list = delegate(List<Stroke> l) { if (l != null) foreach (Stroke st in l) if (st != null && seen.Add(st)) a(st); };
            Action<List<List<Stroke>>> lists = delegate(List<List<Stroke>> ll) { if (ll != null) foreach (List<Stroke> l in ll) list(l); };
            list(strokes); list(screenStrokes); list(fading);
            lists(pages); lists(undo); lists(redo); lists(screenUndo); lists(screenRedo);
            foreach (History h in pageHist.Values) { lists(h.U); lists(h.R); }
        }

        void RemapToScreens()
        {
            try
            {
                // eerst zeker weten dat de tekenlaag bij de schermen van nu hoort; is er intussen weer iets veranderd, dan nog even wachten
                FitToScreens(false);
                if (remapTimer != null && remapTimer.Enabled) return;
                List<KeyValuePair<string, Rectangle>> fromNamed = remapFrom; Rectangle fromVs = remapFromVs; HashSet<Stroke> only = remapStrokes;
                remapFrom = null; remapStrokes = null;
                if (namedMons == null || namedMons.Count == 0) namedMons = W.NamedMonitors();
                if (fromNamed == null || fromNamed.Count == 0 || namedMons.Count == 0) { Log.Info("Schermen veranderd: geen schermen om mee te vergelijken"); return; }
                List<Rectangle> from = new List<Rectangle>(), to = new List<Rectangle>();
                foreach (KeyValuePair<string, Rectangle> kv in fromNamed) from.Add(kv.Value);
                foreach (KeyValuePair<string, Rectangle> kv in namedMons) to.Add(kv.Value);
                List<Rectangle> fromOrd = InOrder(from), toOrd = InOrder(to);
                // bij elk oud scherm: waar komt wat erop stond nu?
                Rectangle[] target = new Rectangle[from.Count];
                bool any = fromVs.Location != vs.Location;
                for (int i = 0; i < from.Count; i++)
                {
                    Rectangle o = from[i];
                    Rectangle t = Rectangle.Empty;
                    string name = fromNamed[i].Key;
                    if (!string.IsNullOrEmpty(name)) foreach (KeyValuePair<string, Rectangle> kv in namedMons) if (kv.Key == name) { t = kv.Value; break; }   // hetzelfde scherm (naam van Windows), ook als het nu ergens anders staat
                    if (t.IsEmpty && to.Contains(o)) t = o;                                        // nog precies hetzelfde scherm
                    if (t.IsEmpty) foreach (Rectangle n in to) if (n.Location == o.Location) { t = n; break; }   // zelfde plek, andere resolutie
                    if (t.IsEmpty && fromOrd.Count == toOrd.Count) t = toOrd[fromOrd.IndexOf(o)];   // even veel schermen, anders gerangschikt
                    if (t.IsEmpty) t = toOrd[0];                                                    // scherm weg: naar het hoofdscherm
                    target[i] = t;
                    if (t != o) any = true;
                }
                if (!any) return;
                CloseTextInput(true); Deselect();
                Rectangle nowVs = vs;
                int moved = 0;
                ForEachStroke(delegate(Stroke st)
                {
                    if (st.Pts.Count == 0 || (only != null && !only.Contains(st))) return;
                    RectangleF bb = Render.Bounds(st);
                    PointF c = new PointF(bb.X + bb.Width / 2 + fromVs.X, bb.Y + bb.Height / 2 + fromVs.Y);
                    int mi = 0; double best = double.MaxValue;
                    for (int i = 0; i < from.Count; i++)
                    {
                        Rectangle r = from[i];
                        double dx = Math.Max(0, Math.Max(r.Left - c.X, c.X - r.Right)), dy = Math.Max(0, Math.Max(r.Top - c.Y, c.Y - r.Bottom));
                        double d = dx * dx + dy * dy;
                        if (d < best) { best = d; mi = i; }
                    }
                    Rectangle o = from[mi], n = target[mi];
                    float k = o.Size == n.Size ? 1f : Math.Min((float)n.Width / Math.Max(1, o.Width), (float)n.Height / Math.Max(1, o.Height));
                    float ox = n.X + (n.Width - o.Width * k) / 2 - nowVs.X, oy = n.Y + (n.Height - o.Height * k) / 2 - nowVs.Y;
                    float ax = fromVs.X - o.X, ay = fromVs.Y - o.Y;
                    if (k == 1f && ox + ax == 0 && oy + ay == 0) return;   // staat al goed
                    for (int i = 0; i < st.Pts.Count; i++) st.Pts[i] = new PointF(ox + (st.Pts[i].X + ax) * k, oy + (st.Pts[i].Y + ay) * k);
                    if (k != 1f) { st.Width *= k; if (st.FontSize > 0) st.FontSize *= k; }
                    moved++;
                });
                Log.Info("Schermen veranderd: " + moved + " tekening(en) meegeschoven");
                if (moved > 0) { MarkChanged(); Rebuild(); RenderFrame(); }
            }
            catch (Exception ex) { Log.Error("Tekening meeschuiven met de schermen", ex); }
        }
        List<Rectangle> monitors;   // de schermen bij de laatste wissel (voor het bord per scherm)
        bool offeredMove;
        static int CountUnique(List<Rectangle> l) { List<Rectangle> u = new List<Rectangle>(); foreach (Rectangle r in l) if (!u.Contains(r)) u.Add(r); return u.Count; }

        Color Background()
        {
            if (DrawMode && BoardMode == Board.White) return Color.FromArgb(255, 250, 250, 248);
            if (DrawMode && BoardMode == Board.Black) return ChalkGreen;
            if (DrawMode && BoardMode == Board.Dark) return DarkBoard;
            // alpha 1 = onzichtbaar, maar vangt wel de muis op in tekenmodus
            return DrawMode ? Color.FromArgb(1, 0, 0, 0) : Color.FromArgb(0, 0, 0, 0);
        }

        // Achtergrond (bord, of het bijna onzichtbare laagje dat in tekenmodus de muis opvangt).
        // Tekenen alleen op een scherm: daarbuiten helemaal doorzichtig, dan gaan je klikken daar gewoon naar je programma's.
        void FillBackground(Graphics g, Rectangle r)
        {
            Rectangle act = ActiveArea();
            if (act.Width <= 0 || act.Contains(r)) { using (SolidBrush b = new SolidBrush(Background())) g.FillRectangle(b, r); return; }
            using (SolidBrush clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0))) g.FillRectangle(clear, r);
            Rectangle both = Rectangle.Intersect(r, act);
            if (both.Width > 0 && both.Height > 0) using (SolidBrush b = new SolidBrush(Background())) g.FillRectangle(b, both);
        }

        void ClipActive(Graphics g) { Rectangle a = ActiveArea(); if (a.Width > 0) g.SetClip(a, CombineMode.Intersect); }

        // de muis vasthouden op het tekenscherm (met ingedrukte knop kom je anders op het vrije scherm)
        PointF ClampActive(PointF p)
        {
            Rectangle a = ActiveArea();
            if (a.Width <= 0) return p;
            return new PointF(Math.Max(a.Left, Math.Min(a.Right - 1, p.X)), Math.Max(a.Top, Math.Min(a.Bottom - 1, p.Y)));
        }

        // Schermen op volgorde: het hoofdscherm eerst, dan van links naar rechts
        public static List<Rectangle> OrderedMonitors()
        {
            List<Rectangle> all = W.MonitorRects();
            List<Rectangle> uniq = new List<Rectangle>();
            foreach (Rectangle r in all) if (!uniq.Contains(r)) uniq.Add(r);
            uniq.Sort(delegate(Rectangle a, Rectangle b)
            {
                bool pa = a.Contains(0, 0), pb = b.Contains(0, 0);
                if (pa != pb) return pa ? -1 : 1;
                return a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
            });
            return uniq;
        }

        // Het vlak (in coordinaten van de tekenlaag) waar je tekent; leeg = alle schermen
        Rectangle ActiveArea()
        {
            if (Cfg.DrawScreen <= 0) return Rectangle.Empty;
            List<Rectangle> mons = monitorsOrdered ?? (monitorsOrdered = OrderedMonitors());
            if (mons.Count < 2 || Cfg.DrawScreen > mons.Count) return Rectangle.Empty;
            Rectangle m = mons[Cfg.DrawScreen - 1]; m.Offset(-vs.X, -vs.Y);
            return m;
        }
        List<Rectangle> monitorsOrdered;
        int lastDrawScreen = -1;
        public void ScreensChoiceChanged() { monitorsOrdered = null; Rebuild(); RenderFrame(); }

        // Bord en tekenen alleen op het andere scherm (de beamer), of weer op alle schermen.
        // "Het andere scherm" = het scherm waar de werkbalk niet staat; je eigen scherm blijft dan vrij om op te werken.
        public bool OneScreenOn { get { return Cfg.DrawScreen > 0 && ActiveArea().Width > 0; } }
        public void ToggleOneScreen()
        {
            List<Rectangle> mons = OrderedMonitors();
            if (OneScreenOn || mons.Count < 2) Cfg.DrawScreen = 0;
            else
            {
                Point barAt = toolbar != null ? new Point(toolbar.Left + toolbar.Width / 2, toolbar.Top + toolbar.Height / 2) : Control.MousePosition;
                int mine = 0;
                for (int i = 0; i < mons.Count; i++) if (mons[i].Contains(barAt)) mine = i;
                Cfg.DrawScreen = (mine == 0 ? 1 : 0) + 1;   // het eerste scherm dat niet het jouwe is
            }
            Cfg.Save(); ApplySettings(); RefreshSettingsWindow();
            Usage.Log("bord", Cfg.DrawScreen > 0 ? "een scherm" : "alle schermen", "");
            if (toolbar != null && Cfg.DrawScreen > 0) toolbar.ShowToast(L.T("Bord en tekenen nu alleen op het andere scherm. Op jouw scherm werk je gewoon verder."), "", null, "vink");
        }

        // Tekening naar het andere scherm: alles wat op het scherm van de werkbalk staat, past precies op het andere scherm
        public void MoveDrawingToOtherScreen()
        {
            try
            {
                CloseTextInput(true); Deselect();
                List<Rectangle> mons = OrderedMonitors();
                if (mons.Count < 2) return;
                Point barAt = toolbar != null ? new Point(toolbar.Left + toolbar.Width / 2, toolbar.Top + toolbar.Height / 2) : Control.MousePosition;
                int from = 0;
                for (int i = 0; i < mons.Count; i++) if (mons[i].Contains(barAt)) from = i;
                bool oneScreen = Cfg.DrawScreen > 0 && Cfg.DrawScreen <= mons.Count;
                if (oneScreen) from = Cfg.DrawScreen - 1;   // tekenen op een scherm: daar staat de tekening
                int to = (from + 1) % mons.Count;
                Rectangle src = mons[from], dst = mons[to];
                src.Offset(-vs.X, -vs.Y); dst.Offset(-vs.X, -vs.Y);
                float k = Math.Min((float)dst.Width / src.Width, (float)dst.Height / src.Height);
                float ox = dst.X + (dst.Width - src.Width * k) / 2, oy = dst.Y + (dst.Height - src.Height * k) / 2;
                Converter<Stroke, Stroke> move = delegate(Stroke s0)
                {
                    RectangleF bb = Render.Bounds(s0);
                    PointF c = new PointF(bb.X + bb.Width / 2, bb.Y + bb.Height / 2);
                    if (!src.Contains(Point.Round(c))) return s0;   // staat niet op dit scherm: laten staan
                    Stroke s = s0.Clone();
                    for (int i = 0; i < s.Pts.Count; i++) s.Pts[i] = new PointF(ox + (s.Pts[i].X - src.X) * k, oy + (s.Pts[i].Y - src.Y) * k);
                    s.Width *= k; if (s.FontSize > 0) s.FontSize *= k;
                    return s;
                };
                List<Stroke> moved = new List<Stroke>(strokes.Count);
                int count = 0;
                foreach (Stroke st in strokes) { Stroke m2 = move(st); if (m2 != st) count++; moved.Add(m2); }
                if (BoardMode != Board.None)
                    for (int pi = 0; pi < pages.Count; pi++)
                        if (pi != pageIndex) { int before = count; for (int i = 0; i < pages[pi].Count; i++) { Stroke m2 = move(pages[pi][i]); if (m2 != pages[pi][i]) count++; pages[pi][i] = m2; } if (count != before) pageHist.Remove(pi); }   // verplaatst: oude geschiedenis van die pagina past niet meer
                if (count == 0) { if (toolbar != null) toolbar.ShowToast(L.T("Er staat niets op dit scherm om te verplaatsen"), "", null, "info"); return; }
                Checkpoint();
                strokes.Clear(); strokes.AddRange(moved);
                // tekende je op een scherm: dan teken je voortaan op het scherm waar de tekening nu staat
                if (oneScreen) { Cfg.DrawScreen = to + 1; Cfg.Save(); monitorsOrdered = null; RefreshSettingsWindow(); }
                Usage.Log("menu", "naar-ander-scherm", "");
                Rebuild(); RenderFrame();
                if (toolbar != null) toolbar.ShowToast(L.T("Je tekening staat nu op het andere scherm"), "", null, "vink");
            }
            catch (Exception ex) { Log.Error("Tekening naar ander scherm", ex); }
        }
    }
}
