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

        float HandleR { get { return 12 * TypingScale; } }   // op een 4K-scherm even groot als op een gewoon scherm
        // de rand van het tekstkader (om te verplaatsen): even breed op elk scherm, ook op 4K
        float EdgeOut { get { return 8 * TypingScale; } }
        bool OnTextEdge(RectangleF f, PointF p) { float i = 4 * TypingScale; return RectangleF.Inflate(f, EdgeOut, EdgeOut).Contains(p) && !RectangleF.Inflate(f, -i, -i).Contains(p); }
        bool textHover;   // muis boven de tekst: dan pas het kader en het hoekje tonen
        PointF SizeHandle(RectangleF f) { return new PointF(f.Right + 2, f.Bottom + 2); }

        static float Dist2(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }

        bool TextMouseDown(MouseEventArgs e)
        {
            PointF p = e.Location;
            RectangleF f = TextFrame(typing);
            float grab = (HandleR + 6) * (HandleR + 6);
            if (OnTextEdge(f, p) && Dist2(p, SizeHandle(f)) > grab)
            {
                textDrag = 2; textDragFrom = p; textPtFrom = typing.Pts[0]; Capture = true;
                return true;
            }
            if (Dist2(p, SizeHandle(f)) <= grab)
            {
                textDrag = 3; textDragFrom = p; textSizeFrom = typing.FontSize; textDragScale = TypingScale;
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
                    StartHold(p);
                    return true;
                }
                if (e.Clicks >= 2) ed.SelectWordAt(idx);
                else ed.PlaceAt(idx, (Control.ModifierKeys & Keys.Shift) != 0);
                textDrag = e.Clicks >= 2 ? 0 : 1; Capture = textDrag != 0;
                if (textDrag == 1 && (Control.ModifierKeys & Keys.Shift) == 0) StartHold(p);   // Shift+klik vergroot de selectie: niet verplaatsen
                TypingChanged();
                return true;
            }
            return false;
        }

        // Ingedrukt houden in de tekst (0,4 seconde stil): dan verplaats je het hele tekstvak, zonder de rand te pakken.
        // Meteen bewegen = gewoon selecteren of woorden slepen, zoals altijd.
        bool holdPending; DateTime holdAt; PointF holdFrom;
        const int HoldMs = 400;
        void StartHold(PointF p) { holdPending = true; holdAt = DateTime.Now; holdFrom = p; }

        // in de Tick: lang genoeg stil ingedrukt? dan wordt het verplaatsen
        void CheckHold()
        {
            if (!holdPending) return;
            if (typing == null || (Control.MouseButtons & MouseButtons.Left) == 0 || (textDrag != 1 && textDrag != 4)) { holdPending = false; return; }
            if (textDrag == 4 && textDropIdx >= 0) { holdPending = false; return; }   // je sleept al woorden
            if ((DateTime.Now - holdAt).TotalMilliseconds < HoldMs) return;
            holdPending = false;
            if (textDrag == 1 && ed.HasSelection) ed.PlaceAt(ed.Caret, false);   // een klein beetje geselecteerd door trillen: weg
            textDrag = 2; textDropIdx = -1; textDragFrom = holdFrom; textPtFrom = typing.Pts[0];
            SetOverlayCursor(Cursors.SizeAll);
            TypingChanged();
        }

        bool TextMouseMove(MouseEventArgs e)
        {
            PointF p = e.Location;
            if (holdPending && Dist2(p, holdFrom) > 64) holdPending = false;   // duidelijk bewogen voordat de tijd om was: gewoon selecteren (een beetje trillen mag)
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
                Cfg.TextSize = typing.FontSize / textDragScale;
                TypingChanged();
                return true;
            }
            // de muiswijzer laat zien wat er kan
            RectangleF f = TextFrame(typing);
            float grab = (HandleR + 6) * (HandleR + 6);
            Cursor want = Dist2(p, SizeHandle(f)) <= grab ? Cursors.SizeNWSE
                : OnTextEdge(f, p) ? Cursors.SizeAll
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
                    // hetzelfde kader als bij een selectie, in de kleur van je thema; rechtsonder het hoekje voor groter of kleiner
                    RectangleF f = TextFrame(typing);
                    PointF sh = SizeHandle(f);
                    if (textHover || textDrag != 0) DrawPickFrame(g, f, new PointF[] { sh }, TypingScale);   // alleen als je met de muis bij de tekst bent
                    if (textDrag == 4 && textDropIdx >= 0)
                    {
                        // waar de gesleepte woorden terechtkomen: een streepje in de accentkleur
                        RectangleF dr = lay.CaretRect(textDropIdx, 2);
                        g.FillRectangle(Brushes.White, RectangleF.Inflate(dr, 1, 1));
                        using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, dr);
                    }
                    if (caretOn && !ed.HasSelection)
                    {
                        // gewoon streepje zoals in Windows: zwart, op een donker bord wit. Op je gewone scherm
                        // weet NexusPen niet wat eronder staat: dan zwart met een heel dun wit randje, zodat je hem altijd ziet.
                        RectangleF cr = lay.CaretRect(ed.Caret, Math.Max(2, (float)Math.Round(1.5f * TypingScale)));
                        bool darkBoard = BoardMode == Board.Black || BoardMode == Board.Dark;
                        if (BoardMode == Board.None) g.FillRectangle(Brushes.White, RectangleF.Inflate(cr, 1, 1));
                        g.FillRectangle(darkBoard ? Brushes.White : Brushes.Black, cr);
                    }
                }
            }
            catch (Exception ex) { SafeDrawError(typing, ex); }
        }

        bool deleteHeld;   // Delete wordt nog vastgehouden (de toets herhaalt zichzelf)
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Delete) deleteHeld = false;
            if (e.KeyCode == Keys.Z || e.KeyCode == Keys.ControlKey) zHeld = false;
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
                s.Tool = Tool.Text; s.Color = CurrentColor; s.Width = PenWidth; s.FontSize = Cfg.TextSize * ScreenScale; s.Text = "";
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
            catch (Exception ex) { Quiet("Toetsenbord naar de tekst", ex); }
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
            catch (Exception ex) { Quiet("Gebied van de tekst", ex); }
            return r;
        }

        void TypingChanged()
        {
            caretOn = true; caretAt = DateTime.Now;
            inputAt = Perf.Now;
            RenderSoon();
        }

        // Klaar met typen: de tekst komt in de tekening (of terug zoals hij was)
        bool zHeld;   // Ctrl+Z wordt vastgehouden
        // Ctrl+Y: eerst binnen de tekst; in een leeg nieuw vakje de stap daarvoor opnieuw doen (spiegel van Ctrl+Z)
        void RedoInTyping()
        {
            if (ed.Redo()) return;
            if (typingOld == null && (string.IsNullOrEmpty(typing.Text) || typing.Text.Trim().Length == 0)) { CloseTextInput(false); Redo(); RenderFrame(); }
        }

        // Ctrl+Z terwijl er in de tekst niets meer terug te zetten is: het vakje sluiten en de stap daarvoor ongedaan maken
        void UndoPastTyping()
        {
            if (typing == null) { Undo(); return; }
            bool empty = string.IsNullOrEmpty(typing.Text) || typing.Text.Trim().Length == 0;
            if (empty && typingOld == null) CloseTextInput(false);   // leeg nieuw vakje: gewoon weg, zonder spoor
            else CloseTextInput(true);   // een tekst die je net opende en niet veranderde, laat geen stap achter
            Undo();
            RenderFrame();
        }

        // Het tekstvak waarin je typt helemaal weghalen (met Ongedaan maken krijg je het terug)
        void DeleteTypingBox()
        {
            if (typing == null) return;
            if (typingOld == null)
            {
                // nieuwe tekst: eerst bewaren en dan weghalen, zodat Ongedaan maken hem terugbrengt
                Stroke t = typing;
                CloseTextInput(true);
                if (strokes.Contains(t)) { Checkpoint(); strokes.Remove(t); MarkChanged(); Rebuild(); }
            }
            else { typing.Text = ""; CloseTextInput(true); }   // bestaande tekst: leeg = weg; de stap voor Ongedaan maken staat er al
            RenderFrame();
        }

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
    }
}
