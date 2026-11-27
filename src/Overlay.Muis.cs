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
            MenuPopup.CloseOpen();
            PointF p = e.Location;
            if (typing != null && e.Button == MouseButtons.Left && TextMouseDown(e)) return;
            if (typing != null && e.Button == MouseButtons.Right && RectangleF.Inflate(TextFrame(typing), EdgeOut, EdgeOut).Contains(p)) { ShowTextMenu(PointToScreen(e.Location)); return; }
            if (typing != null) CloseTextInput(true);   // ergens anders geklikt: de tekst is klaar
            if (CurrentTool == Tool.Focus || CurrentTool == Tool.Mouse) return;   // wijzer op het bord: klikken doet niets
            if (CurrentTool == Tool.Loupe) { if (e.Button == MouseButtons.Left) PinLoupe(PointToScreen(e.Location)); return; }
            // in een vastgezet vergrootglas: tekenen (en gummen) op de plek die het glas vergroot, dan zie je het groot verschijnen
            drawLoupe = null;
            if (Cfg.LoupeDraw && pinnedLoupes.Count > 0 && (UsesColor(CurrentTool) && CurrentTool != Tool.Text || CurrentTool == Tool.Eraser || CurrentTool == Tool.Stamp || e.Button == MouseButtons.Right))
            {
                Point sp = PointToScreen(e.Location);
                for (int li = pinnedLoupes.Count - 1; li >= 0; li--)
                    if (pinnedLoupes[li].Inside(sp)) { drawLoupe = pinnedLoupes[li]; break; }
                if (drawLoupe != null) { p = ClampActive(LoupePoint(e.Location)); drawLoupeSrc = drawLoupe.SourceRect; Capture = true; }
            }
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
            // rechtermuisknop = even gummen, maar niet met Tekst, Selecteren of Stempel: dan verdwijnt er per ongeluk een tekst
            if (e.Button == MouseButtons.Right && (CurrentTool == Tool.Text || CurrentTool == Tool.Select || CurrentTool == Tool.Stamp)) return;
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
                    // ingedrukt houden en bewegen: de tekst gewoon verplaatsen (geen streepje, geen kader);
                    // loslaten zonder te bewegen = een klik: dan pas de tekst openen om te typen
                    selected = onText; quietMove = true; quietClickAt = e.Location;
                    dragging = true; movedThisDrag = false; dragLast = p; Capture = true;
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
                st.FontSize = StampPx * ScreenScale;
                if (drawLoupe != null) { st.FontSize /= Math.Max(1f, drawLoupe.Zoom); drawLoupe = null; Capture = false; }   // in het glas even groot als gewoon
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
            if (CurrentTool == Tool.Laser && Cfg.LaserMode == 2) FadeLasersNow();   // een nieuwe laserlijn: de vorige (die bleef staan) gaat weg
            current = new Stroke();
            current.Tool = CurrentTool;
            current.Color = CurrentColor;
            current.Width = PenWidth;
            if (drawLoupe != null) current.Width = Math.Max(0.6f, PenWidth / Math.Max(1f, drawLoupe.Zoom));   // in het glas even dik als gewoon, dus op het scherm dunner
            if (Cfg.Chalk && CurrentTool == Tool.Pen) current.Style |= Stroke.Chalk;
            else if (Cfg.PenStyle == 1 && CurrentTool == Tool.Pen) current.Style |= Stroke.Callig;
            // drukgevoeligheid: alleen als er net een echte pen op het scherm kwam (met de muis blijft de lijn gewoon even dik)
            if (Cfg.Pressure && CurrentTool == Tool.Pen && PenPressureFresh) current.Press = new List<float> { penPressure };
            smoothAt = p; smoothTick = Environment.TickCount;
            // rand om lijnen en vormen (niet bij krijt: de rand schijnt door de krijtkorrels)
            if (Cfg.LineOutline && Render.CanOutline(CurrentTool) && (current.Style & Stroke.Chalk) == 0 && !Render.IsVarWidth(current)) { current.Outline = true; current.OutlineWidth = Cfg.OutlineWidth; current.OutlineColor = Cfg.OutlineColor; }
            strokeStartAt = DateTime.Now;
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
            // naar een scherm met een andere schaal: de stempelcursor in de goede grootte
            if (CurrentTool == Tool.Stamp) { float sc = ScreenScale; if (Math.Abs(sc - lastCursorScale) > 0.01f) { lastCursorScale = sc; UpdateCursor(); } }
            if (typing != null && TextMouseMove(e)) return;
            if (inputAt < 0) inputAt = Perf.Now;
            // het glas is intussen dicht, verschoven of anders ingezoomd: hier stoppen (anders een lange streep dwars over het scherm)
            if (drawLoupe != null && (drawLoupe.IsDisposed || !drawLoupe.Visible || !drawLoupe.SourceRect.Equals(drawLoupeSrc)))
            {
                drawLoupe = null;
                if (current != null || erasing) { Point mp = e.Location; OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, mp.X, mp.Y, 0)); }
                return;
            }
            PointF p = drawLoupe != null && (current != null || erasing) ? ClampActive(LoupePoint(e.Location)) : ClampActive(e.Location);
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
                    if (quietMove) SetOverlayCursor(Cursors.SizeAll);
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
                    // vloeiende lijn: elk nieuw punt komt maar een stukje richting de muis (een trillende hand wordt rustig)
                    // (op tijd gebaseerd: even rustig met een snelle of een langzame muis)
                    if (Cfg.Smooth && current.Tool != Tool.Laser)
                    {
                        int now = Environment.TickCount, dt = Math.Max(1, Math.Min(100, now - smoothTick)); smoothTick = now;
                        float k = 1f - (float)Math.Exp(-dt / 28.0);
                        smoothAt = new PointF(smoothAt.X + (p.X - smoothAt.X) * k, smoothAt.Y + (p.Y - smoothAt.Y) * k); p = smoothAt;
                    }
                    float ddx = p.X - last.X, ddy = p.Y - last.Y;
                    if (ddx * ddx + ddy * ddy < 2.25f) { inputAt = -1; return; }
                    // natte inkt: alleen het stuk dat verandert (het vorige stukje buigt een beetje mee)
                    Rectangle before = n >= 2 ? WetBox(current, n - 2) : WetBox(current, -1);
                    current.Pts.Add(p);
                    if (current.Press != null) current.Press.Add(PenPressureFresh ? penPressure : current.Press[current.Press.Count - 1]);   // direct na het punt: dan kloppen de maten
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
            if (e.Button == MouseButtons.Right && !erasing) return;   // rechts loslaten zonder gummen: niets afbreken
            if (drawLoupe != null) { drawLoupe = null; Capture = false; }   // klaar met tekenen in het vergrootglas
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
            if (dragging && quietMove && selected != null && selected.Tool == Tool.Text)
            {
                // tekst met de Tekst-knop: verplaatst, of alleen geklikt (dan typen)
                Stroke t = selected; bool moved = movedThisDrag;
                dragging = false; quietMove = false; Capture = false;
                UpdateCursor();   // weer de gewone cursor van Tekst
                selected = null;
                if (lifted) { lifted = false; Rebuild(); }
                dirty = true;
                if (!moved && t != null) OpenTextInput(quietClickAt, t);
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
            // vloeiende lijn: het laatste stukje tot waar je echt losliet
            if (Cfg.Smooth && (current.Tool == Tool.Pen || current.Tool == Tool.Marker) && current.Pts.Count > 1)
            {
                PointF endP = drawLoupe != null ? ClampActive(LoupePoint(e.Location)) : ClampActive(e.Location);   // de echte plek waar je losliet
                PointF last = current.Pts[current.Pts.Count - 1];
                float gap = Math.Abs(endP.X - last.X) + Math.Abs(endP.Y - last.Y);
                if (gap > 1.5f)
                {
                    int steps = Math.Max(1, Math.Min(6, (int)(gap / 12)));   // een paar tussenpuntjes: geen hoekje aan het eind
                    for (int si = 1; si <= steps; si++)
                    {
                        float t = si / (float)steps;
                        current.Pts.Add(new PointF(last.X + (endP.X - last.X) * t, last.Y + (endP.Y - last.Y) * t));
                        if (current.Press != null) current.Press.Add(current.Press[current.Press.Count - 1]);
                    }
                }
            }
            Stroke s = current;
            current = null;
            // een vorm waar je alleen op tikte (rechthoek, lijn, pijl of cirkel van 0 pixels) is nooit bedoeld: niet tekenen
            if (IsShape(s.Tool) && s.Pts.Count >= 2 && Dist(s.Pts[0], s.Pts[s.Pts.Count - 1]) < 3)
            {
                pendingDirty = U(pendingDirty, StrokeBounds(s));
                Usage.Log("tik", "vorm niet getekend", s.Tool.ToString());
                dirty = true;
                return;
            }
            lastDot = s; lastDotAt = DateTime.Now;   // voor dubbelklik: de eerste klik kan een puntje zijn
            // een korte tik met de pen (een stip): als je daarna meteen naar de wijzer gaat, was het een misklik
            bool tap = (s.Tool == Tool.Pen || s.Tool == Tool.Marker) && (DateTime.Now - strokeStartAt).TotalMilliseconds < 300 && Render.Bounds(s).Width <= Render.PenWidest(s) + 6 && Render.Bounds(s).Height <= Render.PenWidest(s) + 6;
            tapDot = tap ? s : null; tapDotAt = DateTime.Now;
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
            LogStroke(s);   // pas na het tekenen: het wegschrijven mag de streep niet ophouden
        }

        // ---- per ongeluk getikte stippen: een korte tik met de pen blijft eerst gewoon staan (het kan een punt of trema zijn).
        // Ga je binnen 3 seconden naar de wijzer, klap je in of druk je op Esc, dan wilde je klikken en niet tekenen: dan gaat hij weg.
        Stroke tapDot; DateTime tapDotAt;

        void DropTapDot(string why)
        {
            Stroke d = tapDot; tapDot = null;
            if (d == null || (DateTime.Now - tapDotAt).TotalSeconds > 3) return;
            // alleen als het echt de laatste streep is (niet als je er intussen iets anders bij tekende)
            bool last = (strokes.Count > 0 && strokes[strokes.Count - 1] == d) || (fading.Count > 0 && fading[fading.Count - 1] == d);
            if (!last) return;
            lastDot = d;
            RemoveDot(d);
            Usage.Log("tik", "stip weggehaald", why);
            RenderSoon();
        }

        // Per streep een paar getallen voor het gebruik (alleen op deze pc): nooit de plek, de vorm of de tekst
        DateTime strokeStartAt;
        void LogStroke(Stroke s)
        {
            try
            {
                if (!Cfg.Usage || (Cfg.UsageCats & Usage.Strokes) == 0) return;
                double len = 0;
                for (int i = 1; i < s.Pts.Count; i++) len += Dist(s.Pts[i - 1], s.Pts[i]);
                string tool = s.Tool.ToString() + ((s.Style & Stroke.Chalk) != 0 ? "/krijt" : "") + ((s.Style & Stroke.Callig) != 0 ? "/kalligrafie" : "") + (Render.HasPress(s) ? "/druk" : "") + (Cfg.Smooth && s.Tool == Tool.Pen ? "/vloeiend" : "");
                int ms = (int)Math.Min(600000, (DateTime.Now - strokeStartAt).TotalMilliseconds);
                Usage.Log("streep", tool + "|kleur " + ColorIndex + "|dikte " + s.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "|" + ms + " ms|" + ((int)Math.Round(len / 10) * 10) + " px|" + (BoardMode == Board.None ? "scherm" : "bord " + BoardMode)
                    + "|rand " + (s.Outline ? "1" : "0") + (Cfg.Fade ? "|vervaagt" : ""), "");
            }
            catch (Exception ex) { Quiet("Lijn vastleggen in gebruik", ex); }
        }

        static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        // Dubbelklik op een tekst (met Selecteren): tekst wijzigen
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
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
            // Ctrl+muiswiel op een bord: in- en uitzoomen (gewoon draaien blijft de dikte)
            if (BoardMode != Board.None && (Control.ModifierKeys & Keys.Control) != 0 && (Control.ModifierKeys & Keys.Shift) == 0
                && typing == null && CurrentTool != Tool.Loupe && !FocusOn && CurrentTool != Tool.Text && CurrentTool != Tool.Stamp)
            {
                if (Math.Sign(e.Delta) != Math.Sign(zoomWheelAcc)) zoomWheelAcc = 0;
                zoomWheelAcc += e.Delta;
                if (Math.Abs(zoomWheelAcc) >= 120) { ZoomBoard(zoomWheelAcc > 0 ? 1 : -1); zoomWheelAcc = 0; if (toolbar != null) toolbar.RefreshBoardPopup(); }
                return;
            }
            Wheel(e.Delta);
        }
    }
}
