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
        // ---------- bord en pagina's
        List<List<Stroke>> screenUndo = new List<List<Stroke>>(), screenRedo = new List<List<Stroke>>();

        public void SetBoard(Board b) { SetBoard(b, true); }

        void SetBoard(Board b, bool byUser)
        {
            CloseTextInput(true);
            StoreActive();
            Deselect();
            bool wasScreen = BoardMode == Board.None;
            // scherm en elke bordpagina hebben hun eigen Ongedaan maken en Opnieuw: bij wisselen bewaren en terugzetten
            bool switching = wasScreen != (b == Board.None);
            if (switching) SaveHist();
            BoardMode = b;
            if (switching) LoadHist();
            if (b != Board.None) Cfg.LastBoard = (int)b;   // de bordknop opent de volgende keer dit bord
            strokes = b == Board.None ? screenStrokes : pages[pageIndex];
            if (byUser) Usage.Log("bord", b.ToString(), "");
            // een bord kiezen zet geen tekengereedschap aan: je komt op Selecteren en kiest zelf wat je nodig hebt
            if (byUser && b != Board.None && !DrawMode) SetTool(Tool.Select);
            Rebuild();
            RefreshUi();
            MouseOffBoard();
        }

        // De bordknop: staat er een bord, dan gaat het dicht; anders opent het bord van de vorige keer (of wat je in Instellingen koos)
        public void ToggleBoard()
        {
            if (BoardMode != Board.None && DrawMode) { CloseBoard(); return; }
            int k = Cfg.BoardOpenAs > 0 ? Cfg.BoardOpenAs : Cfg.LastBoard;
            SetBoard(k == 2 ? Board.Black : k == 3 ? Board.Dark : Board.White);
        }

        // Bord dicht, terug naar het gewone scherm. Wat erop staat blijft bewaard, tenzij je in Instellingen "wissen" koos.
        public void CloseBoard()
        {
            if (BoardMode == Board.None) return;
            bool clear = Cfg.BoardClearOnClose && HasBoardDrawing();
            if (clear)
            {
                // eerst bewaren als "vorige", zodat Vorige tekening terugzetten (Instellingen > Opslaan) het bord terugbrengt
                try { StoreActive(); SaveNow(); StrokeStore.ArchiveBoard(); } catch (Exception ex) { Log.Error("Bord bewaren voor wissen", ex); }
            }
            SetBoard(Board.None);
            if (clear)
            {
                pages.Clear(); pages.Add(new List<Stroke>()); pageIndex = 0; pageHist.Clear();
                MarkChanged();
            }
            Usage.Log("bord", "dicht", Cfg.BoardClearOnClose ? "wissen" : "bewaren");
        }

        bool HasBoardDrawing() { foreach (List<Stroke> pg in pages) if (pg.Count > 0) return true; return strokes.Count > 0 && BoardMode != Board.None; }

        // De pagina waar je op staat weghalen (er blijft altijd minstens een pagina)
        public void DeletePage()
        {
            if (BoardMode == Board.None) return;
            CloseTextInput(true); Deselect();
            if (strokes.Count > 0)
            {
                // de vraag boven de tekenlaag (met de tekenlaag als eigenaar), en daarna kijken of alles nog hetzelfde is
                Board keepBoard = BoardMode; int keepIndex = pageIndex, keepCount = pages.Count; List<Stroke> keepList = strokes;
                if (MessageBox.Show(this, L.T("Deze pagina met alles wat erop staat verwijderen?"), "NexusPen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                if (BoardMode != keepBoard || pageIndex != keepIndex || pages.Count != keepCount || strokes != keepList) return;   // intussen bord dicht of andere pagina
            }
            if (pages.Count <= 1) { strokes.Clear(); undo.Clear(); redo.Clear(); pageHist.Clear(); }
            else
            {
                pageHist.Remove(pageIndex);
                ShiftHist(pageIndex + 1, -1);
                pages.RemoveAt(pageIndex);
                pageIndex = Math.Max(0, Math.Min(pages.Count - 1, pageIndex));
                strokes = pages[pageIndex];
                LoadHist();
            }
            MarkChanged(); Rebuild(); RefreshUi();
            Usage.Log("bord", "pagina weg", "");
        }

        public void CycleBoard() { SetBoard(BoardMode == Board.None ? Board.White : BoardMode == Board.White ? Board.Black : BoardMode == Board.Black ? Board.Dark : Board.None); }

        // Lijnen en ruitjes groter of kleiner (om in te zoomen)
        // In- of uitzoomen op het bord: dir +1 groter, -1 kleiner (in stapjes van 20%)
        int zoomWheelAcc;
        DateTime lastZoomLog = DateTime.MinValue, zoomChangedAt = DateTime.MinValue;
        bool zoomSavePending;

        public void ZoomBoard(int dir)
        {
            float z = dir == 0 ? Cfg.BoardZoom : Cfg.BoardZoom * (dir > 0 ? 1.2f : 1 / 1.2f);
            if (Math.Abs(z - 1) < 0.05f) z = 1;
            Cfg.BoardZoom = Math.Max(Settings.MinZoom, Math.Min(Settings.MaxZoom, z));
            zoomSavePending = true; zoomChangedAt = DateTime.Now;   // bewaren gebeurt even later (niet bij elke stap)
            if (BoardMode == Board.None) SetBoard(Board.White); else Rebuild();
        }

        void GoToPage(int i)
        {
            CloseTextInput(true);
            if (BoardMode == Board.None) { SetBoard(Board.White); }
            StoreActive();
            SaveHist();
            pageIndex = Math.Max(0, Math.Min(pages.Count - 1, i));
            strokes = pages[pageIndex];
            LoadHist();   // elke pagina zijn eigen Ongedaan maken en Opnieuw
            Deselect();
            MarkChanged();
            Rebuild();
            RefreshUi();
        }

        public void PrevPage() { GoToPage(pageIndex - 1); }
        public void NextPage() { GoToPage(pageIndex + 1); }
        public void NewPage() { if (pages.Count >= 200) { if (toolbar != null) toolbar.ShowToast(L.T("Het bord heeft al 200 pagina's."), "", null, "info"); return; } StoreActive(); SaveHist(); ShiftHist(pageIndex + 1, 1); pages.Insert(pageIndex + 1, new List<Stroke>()); GoToPage(pageIndex + 1); }

        // ---- geschiedenis per plek: het scherm, en elke bordpagina apart
        class History { public List<List<Stroke>> U = new List<List<Stroke>>(), R = new List<List<Stroke>>(); }
        Dictionary<int, History> pageHist = new Dictionary<int, History>();

        void SaveHist()
        {
            if (BoardMode == Board.None) { screenUndo = new List<List<Stroke>>(undo); screenRedo = new List<List<Stroke>>(redo); return; }
            History h = new History(); h.U.AddRange(undo); h.R.AddRange(redo);
            pageHist[pageIndex] = h;
        }

        void LoadHist()
        {
            undo.Clear(); redo.Clear();
            if (BoardMode == Board.None) { undo.AddRange(screenUndo); redo.AddRange(screenRedo); return; }
            History h;
            if (pageHist.TryGetValue(pageIndex, out h)) { undo.AddRange(h.U); redo.AddRange(h.R); }
        }

        // een pagina erbij of eraf: de geschiedenis van de pagina's erachter schuift mee
        void ShiftHist(int from, int delta)
        {
            Dictionary<int, History> n = new Dictionary<int, History>();
            foreach (KeyValuePair<int, History> kv in pageHist)
            {
                int k = kv.Key < from ? kv.Key : kv.Key + delta;
                if (k >= 0) n[k] = kv.Value;
            }
            pageHist = n;
        }


        // ---------- inklappen: alles weg (balk, tekeningen, bord), alleen het logo blijft
        public bool IsCollapsed { get { return allHidden; } }

        // Inklappen onthoudt waar je was (tekenen, gereedschap, bord en pagina); uitklappen gaat daar precies verder
        bool keepBoardOnce, collapsedDraw;
        Tool collapsedTool = Tool.Pen;

        public void CollapseAll()
        {
            if (allHidden) return;
            DropTapDot("inklappen");
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
            if (collapsedDraw && !DrawMode)
            {
                collapsedDraw = false;
                SetDrawMode(true);
                if (collapsedTool != Tool.Mouse && CurrentTool != collapsedTool) SetTool(collapsedTool);
            }
            if (DrawMode && CurrentTool == Tool.Loupe) StartFollowLoupe();   // het vergrootglas weer laten meegaan
            MouseOffBoard();
            Usage.Log("uitklappen", "", "");
        }

        // de wijzer zonder bord: dan gewoon met de muis op je scherm werken (klikken gaan weer naar je programma's)
        void MouseOffBoard() { if (DrawMode && CurrentTool == Tool.Mouse && BoardMode == Board.None) SetDrawMode(false); }

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
                        Stroke st = new Stroke(); st.Tool = Tool.Stamp; st.FontSize = StampPx * ScreenScale; st.Color = Render.StampColor(kind);
                        st.Text = kind == "nummer" ? "#" + Cfg.StampNext : kind; st.Pts.Add(PointF.Empty);
                        c = ToolCursor.For("stempel|" + st.Text + "|" + st.FontSize, delegate { return ToolCursor.Stamp(st); });
                        break;
                    }
                    case Tool.Loupe:
                        // vergrootglas zonder muisaanwijzer: het glas laat al zien waar je bent
                        c = Cfg.LoupeCursor || followLoupe == null || followLoupe.IsDisposed ? Cursors.Default : ToolCursor.For("leeg", delegate { return new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb); });
                        break;
                    case Tool.Select: case Tool.Focus: case Tool.Mouse: c = Cursors.Default; break;
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
    }
}
