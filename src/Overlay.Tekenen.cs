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
        // wat op een bord staat, nooit zonder het bord laten zien (bij inklappen en weer openen kwam de tekst anders net eerder)
        bool ShowStrokesNow { get { return StrokesVisible && (BoardMode == Board.None || DrawMode); } }

        public void Rebuild()
        {
            using (Graphics g = Graphics.FromImage(committed))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                FillBackground(g, new Rectangle(0, 0, committed.Width, committed.Height));
                g.CompositingMode = CompositingMode.SourceOver;
                Render.Setup(g);
                ClipActive(g);
                if (DrawMode && BoardMode != Board.None) DrawBoard(g);
                if (ShowStrokesNow)
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
                FillBackground(g, r);
                g.CompositingMode = CompositingMode.SourceOver;
                Render.Setup(g);
                ClipActive(g);
                if (DrawMode && BoardMode != Board.None) DrawBoard(g);
                if (ShowStrokesNow)
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
            float s = W.DpiScaleAt(new Point(vs.X + w - 10, vs.Y + h - 10));   // dezelfde schaal als het regeltje zelf (scherm rechtsonder)
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
            float z = Math.Max(Settings.MinZoom, Math.Min(Settings.MaxZoom, Cfg.BoardZoom));
            // Elk scherm zijn eigen lijnen, in zijn eigen schaal en met zijn eigen randen. Vroeger was het
            // een groot vlak over alle schermen samen: dan klopte het op het ene scherm wel en op het andere niet.
            if (monitors == null || monitors.Count == 0) { monitors = W.MonitorRects(); namedMons = W.NamedMonitors(); }
            List<Rectangle> areas = new List<Rectangle>();
            foreach (Rectangle mon in monitors)
            {
                Rectangle b = mon; b.Offset(-vs.X, -vs.Y);
                b.Intersect(new Rectangle(0, 0, w, h));
                if (b.Width > 0 && b.Height > 0 && !areas.Contains(b)) areas.Add(b);   // gedupliceerd: hetzelfde vlak maar een keer
            }
            if (areas.Count == 0) areas.Add(new Rectangle(0, 0, w, h));
            Rectangle act = ActiveArea();
            if (act.Width > 0) { areas.Clear(); areas.Add(act); }
            GraphicsState keep = g.Save();
            foreach (Rectangle b in areas)
            {
                float s = W.DpiScaleAt(new Point(vs.X + b.X + b.Width / 2, vs.Y + b.Y + b.Height / 2));
                float margin = 120 * s, L0 = b.Left + margin, R0 = b.Right - margin;
                g.SetClip(b, CombineMode.Intersect);
                using (Pen pb = new Pen(blue, 1.2f * s * Math.Max(1f, z * 0.8f)))
                using (Pen pr = new Pen(red, 1.6f * s * Math.Max(1f, z * 0.8f)))
                {
                    if (Cfg.BoardBg == 1)
                    {
                        float step = 32 * s * z;
                        float y0 = b.Top + 190 * s;
                        // heel ver ingezoomd: past er geen groep onder de balk, zet er dan een in het midden
                        if (y0 + step * 3 >= b.Bottom - 40 * s) y0 = b.Top + Math.Max(10 * s, (b.Height - step * 3) / 2);
                        for (float y = y0; y + step * 3 < b.Bottom - 10 * s; y += step * 5)
                        {
                            g.DrawLine(pb, L0, y, R0, y);
                            g.DrawLine(pb, L0, y + step, R0, y + step);
                            g.DrawLine(pr, L0, y + step * 2, R0, y + step * 2);
                            g.DrawLine(pb, L0, y + step * 3, R0, y + step * 3);
                        }
                    }
                    else if (Cfg.BoardBg == 2)
                    {
                        float step = 32 * s * z;
                        using (Pen pg = new Pen(Color.FromArgb(chalk ? 40 : 70, blue), 1f))
                        {
                            for (float x = b.Left; x < b.Right; x += step) g.DrawLine(pg, x, b.Top, x, b.Bottom);
                            for (float y = b.Top; y < b.Bottom; y += step) g.DrawLine(pg, b.Left, y, b.Right, y);
                        }
                    }
                    else if (Cfg.BoardBg == 3)
                    {
                        float step = 44 * s * z;
                        for (float y = b.Top + 150 * s; y < b.Bottom - 30 * s; y += step) g.DrawLine(pb, L0, y, R0, y);
                    }
                }
                g.Restore(keep); keep = g.Save();
            }
            g.Restore(keep);
            float s0 = W.DpiScaleAt(new Point(vs.X + w - 10, vs.Y + h - 10));
            string info = (autoSavedAt == DateTime.MinValue ? L.T("Automatisch bewaren staat aan") : L.T("Automatisch bewaard") + " · " + autoSavedAt.ToString("HH:mm"))
                + " · " + L.F("pagina {0} van {1}", pageIndex + 1, pages.Count);
            using (Font f = Fonts.Make(UiFont.Name, 12 * s0, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(chalk ? Color.FromArgb(150, 230, 240, 230) : Color.FromArgb(150, 60, 60, 66)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Far; sf.LineAlignment = StringAlignment.Far;
                Rectangle wa = Screen.FromPoint(new Point(vs.X + w - 10, vs.Y + h - 10)).WorkingArea;
                g.DrawString(info, f, b, new RectangleF(0, 0, wa.Right - vs.X - 22 * s0, wa.Bottom - vs.Y - 12 * s0), sf);
            }
        }

        void CommitToLayer(Stroke s)
        {
            using (Graphics g = Graphics.FromImage(committed)) { Render.Setup(g); ClipActive(g); SafeDraw(g, s, 1f); }
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
            if (Cfg.FocusShape == 2) { float sq = r * 0.9f; return new RectangleF(focusAt.X - sq, focusAt.Y - sq, 2 * sq, 2 * sq); }   // vierkant
            return new RectangleF(focusAt.X - r, focusAt.Y - r, 2 * r, 2 * r);
        }

        public void SetFocusShape(int shape)
        {
            Cfg.FocusShape = Math.Max(0, Math.Min(2, shape)); Cfg.Save();
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
            bool rawEsc = (W.GetAsyncKeyState(W.VK_ESCAPE) & 0x8000) != 0;
            bool timerTyping = lesTimer != null && !lesTimer.IsDisposed && lesTimer.Editing;
            bool esc = rawEsc && !timerTyping;   // de Esc van het timer-invulvak is niet voor de tekenlaag
            // Esc: eerst een open venstertje van de balk dicht, dan pas terug naar de muis
            if (esc && !lastEsc && MenuPopup.IsOpen) { MenuPopup.CloseOpen(); lastEsc = true; return; }   // eerst een open menu
            if (esc && !lastEsc && toolbar != null && toolbar.Sizing) { toolbar.StopSizing(false); lastEsc = true; return; }   // grootte aanpassen: terug zoals het was
            if (esc && !lastEsc && lesTimer != null && !lesTimer.IsDisposed && lesTimer.Visible && lesTimer.IsFull) { lesTimer.ExitFull(); lastEsc = true; return; }   // timer op het hele scherm: terug
            if (esc && !lastEsc && toolbar != null && toolbar.AnyPopupOpen) { toolbar.ClosePopups(); lastEsc = true; return; }
            if (esc && !lastEsc && DrawMode && typing != null) { CloseTextInput(true); lastEsc = true; return; }   // ook als het toetsenbord ergens anders zat
            if (esc && !lastEsc && DrawMode && pinnedLoupes.Count > 0) { CloseAllLoupes(); lastEsc = true; return; }   // dan de vergrootglazen (alleen als je met NexusPen werkt)
            if (esc && !lastEsc && DrawMode && typing == null)
            {
                // Esc: eerst de selectie weg, dan een gewone wijzer, dan het bord dicht (net als op de bordknop klikken).
                // Alles verbergen gaat alleen met inklappen (het logo), alles weghalen alleen met de prullenbak.
                if (selected != null) Deselect();
                else if (CurrentTool != Tool.Mouse) SetTool(Tool.Mouse);
                else if (BoardMode != Board.None) CloseBoard();
            }
            lastEsc = esc || (timerTyping && rawEsc);   // dezelfde ingedrukte Esc telt na het sluiten van het vakje niet nog een keer

            if (toolbar != null) toolbar.WatchClickElsewhere();

            // Bovenop blijven liggen (taakbalk, startmenu en Zoom duwen ons anders weg)
            if ((DateTime.Now - lastTopAt).TotalMilliseconds > 900 && IsHandleCreated && !W.OtherTopmostInFront())
            {
                lastTopAt = DateTime.Now;
                // vangnet: is het scherm veranderd zonder dat Windows het meldde? Dan alsnog passend maken
                if (IsHandleCreated && Visible && SystemInformation.VirtualScreen != vs) FitToScreens(false);
                if (Visible) W.KeepOnTop(Handle);
                if (toolbar != null) toolbar.KeepAllOnTop();
                KeepExtrasOnTop();
            }

            if (zoomSavePending && (DateTime.Now - zoomChangedAt).TotalMilliseconds > 800) { zoomSavePending = false; Cfg.Save(); }

            // Automatisch bewaren, 1,5 seconde na de laatste wijziging
            if (needSave && (DateTime.Now - changedAt).TotalMilliseconds > 1500) SaveNow();

            CheckHold();
            // knipperend streepje tijdens het typen
            if (typing != null && (DateTime.Now - caretAt).TotalMilliseconds > 530) { caretOn = !caretOn; caretAt = DateTime.Now; dirty = true; }

            // Rustig aan als er niets gebeurt: minder vaak wakker worden spaart de batterij
            // (niet met een open venstertje van de balk: dan moet een korte klik ergens anders gezien worden)
            bool idle = !DrawMode && lasers.Count == 0 && fading.Count == 0 && !dirty && !needSave && !(toolbar != null && (toolbar.AnyPopupOpen || toolbar.Sizing));
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
                // net bijgewerkt: de tekening van voor het bijwerken weer laten zien
                int flag = Updates.ReadFlag();
                justUpdated = flag == 1; updateFailed = flag == -1;
                Updates.CleanUp();
                // 0 = nooit bewaren, 1 = na Opslaan, 2 = altijd
                bool load = crashed || justUpdated || Cfg.KeepDrawing == 2 || (Cfg.KeepDrawing == 1 && keep);
                if (load)
                {
                    screenStrokes = StrokeStore.Load(StrokeStore.CurrentPath);
                    pages = StrokeStore.LoadBoard();
                    int n = screenStrokes.Count;
                    foreach (List<Stroke> pg in pages) n += pg.Count;
                    if (n > 0 && !justUpdated) { if (crashed) restoredAfterCrash = true; else restoredSaved = true; }
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
                pageHist.Clear();
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
                // terug naar het gewone scherm: anders zou je niets meer zien (bordtekst toont alleen met het bord)
                if (BoardMode != Board.None) { try { SetBoard(Board.None, false); } catch { BoardMode = Board.None; strokes = screenStrokes; undo.Clear(); redo.Clear(); } }
                current = null;
                erasing = false;
                dragging = false; quietMove = false;
                lifted = false;
                Rebuild();
                RenderFrame();
                RefreshUi();
            }
            catch (Exception ex) { Quiet("Nooduitgang", ex); }
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
            float widest = s.Tool == Tool.Marker ? Render.MarkerWidth(s.Width) : s.Tool == Tool.Laser ? Render.LaserWidest(s.Width) : Render.PenWidest(s);
            float pad = widest / 2 + Render.OutlineWidth(s) + 3;
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
            // alleen lijnen die nog stilstaan; wat al aan het vervagen is, gaat gewoon verder
            foreach (Stroke s in lasers) if (!s.FadeNow && (now - s.Born).TotalMilliseconds < LaserHold(s)) { s.FadeNow = true; s.Born = now; }
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
                    ClipActive(g);   // tekenen alleen op een scherm: ook spotlicht en lijnen in de maak niet op het vrije scherm
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
            if (quietMove) return;   // tekst gewoon verplaatsen: geen kader eromheen
            RectangleF r = SelectionBox();
            DrawPickFrame(g, r, new PointF[] { r.Location, new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) }, 1f);
        }

        // Eén soort kader voor alles wat je vastpakt (selectie en tekst die je typt): gestippeld in de accentkleur
        // van je thema, met een donkere rand eronder zodat hij op elk bord en scherm te zien is, en vierkante hoekjes.
        void DrawPickFrame(Graphics g, RectangleF r, PointF[] corners, float sc)
        {
            Color acc = Theme.Accent;
            using (GraphicsPath gp = Svg.RoundRect(r, 6 * sc))
            {
                using (Pen p = new Pen(Color.FromArgb(150, 0, 0, 0), 3 * sc)) g.DrawPath(p, gp);
                using (Pen p = new Pen(acc, 1.6f * sc)) { p.DashPattern = new float[] { 4, 3 }; g.DrawPath(p, gp); }
            }
            using (SolidBrush b = new SolidBrush(acc))
                foreach (PointF c in corners)
                {
                    g.FillRectangle(Brushes.White, c.X - 5 * sc, c.Y - 5 * sc, 10 * sc, 10 * sc);
                    g.FillRectangle(b, c.X - 4 * sc, c.Y - 4 * sc, 8 * sc, 8 * sc);
                }
        }

        void DrawFocus(Graphics g, Rectangle area)
        {
            RectangleF hole = FocusHole();
            bool rect = Cfg.FocusShape >= 1;   // strook of vierkant
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
    }
}
