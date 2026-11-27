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
        // ---------- instellingen en uiterlijk
        public void ShowSettings(string page)
        {
            // het bord blijft staan; Instellingen komt er bovenop (met een gewone wijzer)
            noGiveBack = true;
            try { if (DrawMode) SetTool(Tool.Mouse); } finally { noGiveBack = false; }
            Usage.Log("menu", "instellingen", page ?? "");
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (page != null) settingsForm.ShowPage(page);
                settingsForm.Activate();
                return;
            }
            settingsForm = new SettingsForm(this);
            settingsForm.Owner = this;   // altijd boven de tekenlaag en het bord
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
            if (TimerVisible) lesTimer.Restyle();
            bool langChanged = L.Code != Cfg.Language;
            L.Set(Cfg.Language);
            UpdateTrayIcon();
            if (toolbar != null && !langChanged) toolbar.Restyle();
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                W.HideFromCapture(settingsForm.Handle, Cfg.HideFromCapture);
                settingsForm.Invalidate();
            }
            if (Cfg.DrawScreen != lastDrawScreen) { lastDrawScreen = Cfg.DrawScreen; monitorsOrdered = null; Rebuild(); }   // tekenen alleen op een scherm: aan, uit of ander scherm
            else if (DrawMode && BoardMode != Board.None) Rebuild();
            if (selected != null) dirty = true;
            if (FocusOn) { fullDirty = true; dirty = true; }   // bijvoorbeeld spotlicht rond of rechthoek
            UpdateCursor();
        }

        // ---------- modus en gereedschap
        void SetDrawMode(bool on)
        {
            if (!on) DropTapDot("wijzer");
            if (on && allHidden) { collapsedDraw = false; ExpandAll(); }   // je kiest zelf een gereedschap: dat gaat voor
            if (on && !DrawMode) RememberOtherWindow();
            bool giveBack = !on && DrawMode;
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
            if (giveBack) GiveBackFocus();
        }

        // Tekenen uit: het programma waarin je zat weer actief maken. Anders had dat programma
        // eerst nog een klik van je nodig voordat je er weer in kon typen of scrollen.
        IntPtr otherWindow;
        bool noGiveBack;   // Instellingen gaat net open: die krijgt het toetsenbord
        static readonly uint MyPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        static bool IsOurs(IntPtr h) { uint pid; W.GetWindowThreadProcessId(h, out pid); return pid == MyPid; }

        void RememberOtherWindow()
        {
            otherWindow = IntPtr.Zero;   // geen oud venster van een eerdere keer
            try { IntPtr fg = W.GetForegroundWindow(); if (fg != IntPtr.Zero && !IsOurs(fg)) otherWindow = fg; } catch { }
        }

        void GiveBackFocus()
        {
            try
            {
                if (noGiveBack) return;
                // alleen als de tekenlaag zelf nog het actieve venster is (niet als je al elders zit,
                // of in Instellingen of de timer van NexusPen)
                if (W.GetForegroundWindow() != Handle) return;
                IntPtr target = IntPtr.Zero;
                if (otherWindow != IntPtr.Zero && W.IsWindow(otherWindow) && W.IsWindowVisible(otherWindow)) target = otherWindow;
                else
                {
                    // anders het programma onder de muis
                    Point m = Control.MousePosition;
                    IntPtr h = W.GetAncestor(W.WindowFromPoint(new W.POINT(m.X, m.Y)), 2);
                    if (h != IntPtr.Zero && !IsOurs(h)) target = h;
                }
                if (target != IntPtr.Zero) W.ForceForeground(target);
            }
            catch (Exception ex) { Quiet("Toetsenbord teruggeven", ex); }
        }

        static bool HasWidth(Tool t) { return t == Tool.Pen || t == Tool.Marker || t == Tool.Laser || t == Tool.Eraser || IsShape(t); }

        public void SetTool(Tool t)
        {
            if (t != Tool.Mouse) tapDot = null;   // een ander tekengereedschap gekozen: dan was de stip wel bedoeld
            if (typing != null) CloseTextInput(true);
            if (t == Tool.Mouse)
            {
                // op een bord: alleen een gewone wijzer, het bord en wat erop staat blijven gewoon staan
                if (DrawMode && BoardMode != Board.None)
                {
                    Deselect(); FadeLasersNow(); StopFollowLoupe();
                    if (CurrentTool == Tool.Focus) { fullDirty = true; dirty = true; }
                    DropTapDot("wijzer");
                    CurrentTool = Tool.Mouse; Usage.ToolChanged("Mouse");
                    UpdateCursor(); RefreshUi();
                    return;
                }
                if (DrawMode) SetDrawMode(false);
                RefreshUi(); return;
            }
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

        // tekenen aan of uit: op een bord wissel je tussen de wijzer en je pen, het bord blijft staan
        public void ToggleDraw() { if (DrawMode && CurrentTool != Tool.Mouse) SetTool(Tool.Mouse); else SetTool(LastDrawTool); }

        public void ToggleFocus() { if (FocusOn) SetTool(Tool.Mouse); else SetTool(Tool.Focus); }

        // ---------- vergrootglas
        Loupe followLoupe;
        readonly List<Loupe> pinnedLoupes = new List<Loupe>();
        // ---- pen van een tablet: hoe hard je drukt, en de vloeiende lijn
        float penPressure = 0.5f; int penPressureAt = -100000;
        PointF smoothAt; int smoothTick;
        bool PenPressureFresh { get { return Environment.TickCount - penPressureAt < 120; } }

        void ReadPenPressure(IntPtr wParam)
        {
            try
            {
                uint id = (uint)(wParam.ToInt64() & 0xFFFF), type;
                if (!W.GetPointerType(id, out type) || type != 3) return;   // 3 = pen
                W.POINTER_PEN_INFO info = new W.POINTER_PEN_INFO();
                if (!W.GetPointerPenInfo(id, ref info) || (info.penMask & 1) == 0) return;   // deze pen meet geen druk
                if ((info.pointerInfo.pointerFlags & 0x4) == 0) return;   // de pen zweeft boven het scherm (druk 0): dat is geen schrijven
                penPressure = Math.Max(0, Math.Min(1, info.pressure / 1024f));
                penPressureAt = Environment.TickCount;
            }
            catch (Exception ex) { Quiet("Druk van de pen lezen", ex); }
        }

        Loupe drawLoupe;   // je tekent nu in dit vastgezette glas
        W.RECT drawLoupeSrc;

        // punt in het glas (tekenlaag) naar de plek die het glas vergroot (ook in de tekenlaag)
        PointF LoupePoint(Point client)
        {
            if (drawLoupe == null || drawLoupe.IsDisposed) return client;
            Point sp = PointToScreen(client);
            PointF src = drawLoupe.ToSource(sp);
            return new PointF(src.X - vs.X, src.Y - vs.Y);
        }

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
            try { followLoupe = new Loupe(this, true, Control.MousePosition); followLoupe.Show(); Usage.Log("vergrootglas", "aan", ""); UpdateCursor(); }
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
                // vastgezet: het glas staat net boven die plek; meteen weer je pen, dan schrijf je op de echte plek
                // en zie je het vergroot in het glas (Esc haalt de glazen weg)
                SetTool(LastDrawTool);
            }
            catch (Exception ex) { Log.Error("Vergrootglas vastzetten", ex); }
        }

        public void LoupeClosed(Loupe l)
        {
            pinnedLoupes.Remove(l);
            if (followLoupe == l) followLoupe = null;
        }

        // Timer, vergrootglazen en een open menu boven de tekenlaag houden (anders vangt die hun klikken)
        void KeepExtrasOnTop()
        {
            List<Form> fs = new List<Form>();
            foreach (Loupe l in pinnedLoupes) { fs.Add(l); if (l.Strip != null) fs.Add(l.Strip); }
            if (followLoupe != null) fs.Add(followLoupe);
            if (lesTimer != null) fs.Add(lesTimer);
            if (MenuPopup.Current != null) fs.Add(MenuPopup.Current);
            foreach (Form f in fs)
                try { if (f != null && !f.IsDisposed && f.Visible && f.IsHandleCreated) W.KeepOnTop(f.Handle); } catch { }
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
        public bool TimerRunningHidden { get { return lesTimer != null && !lesTimer.IsDisposed && !lesTimer.Visible && lesTimer.Running; } }

        public void ToggleTimer()
        {
            if (TimerVisible) { lesTimer.Hide(); RefreshUi(); return; }
            if (lesTimer == null || lesTimer.IsDisposed) { lesTimer = new TimerForm(this); lesTimer.Owner = this; }
            Point at = toolbar != null ? new Point(toolbar.Left, toolbar.Bottom + 10) : Control.MousePosition;
            lesTimer.ShowNear(at);
            Usage.Log("timer", "open", "");
            RefreshUi();
        }

        // Kleur kiezen pakt weer een tekengereedschap als je aan het gummen of selecteren was
        public void SetColor(int i)
        {
            ColorIndex = Math.Max(0, Math.Min(ColorCount - 1, i));
            Usage.Log("kleur", Palette.KeyOf(PaletteIndex, CurrentColor), "");
            if (selected != null) { RecolorSelected(CurrentColor); return; }
            Cfg.ColorIndex = ColorIndex;
            UseColorTool();   // met de wijzer of de gum: meteen weer tekenen met het laatste gereedschap
            RefreshUi();
        }

        // Kleur gekozen terwijl je een gereedschap zonder kleur hebt (wijzer, gum, spotlicht, vergrootglas, selecteren):
        // dan meteen terug naar het laatste gereedschap waarmee je tekende. Geen extra klik meer nodig.
        public void UseColorTool()
        {
            if (selected != null) return;
            if (DrawMode && UsesColor(CurrentTool)) return;
            SetTool(UsesColor(LastDrawTool) ? LastDrawTool : Tool.Pen);
        }
        static bool UsesColor(Tool t)
        {
            return t == Tool.Pen || t == Tool.Marker || t == Tool.Laser || t == Tool.Line || t == Tool.Arrow || t == Tool.Rect || t == Tool.Ellipse || t == Tool.Text;
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

        public bool HasDrawing { get { return StrokeCountAll() > 0; } }

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
            ColorIndex = Math.Min(ColorIndex, ColorCount - 1); Cfg.ColorIndex = ColorIndex;   // eigen lijsten kunnen korter zijn
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

        // Op een scherm met grotere schaal (4K op 200%) worden tekst en stempels even groot als op een gewoon scherm.
        // De getallen in Instellingen blijven hetzelfde (36 blijft 36); op het scherm wordt het 36 x de schaal.
        // de schaal van Windows (125%, 150%, 200%) op het scherm waar de muis staat, niet hoe groot het scherm is
        float ScreenScale { get { return ScaleAt(Control.MousePosition); } }
        static float ScaleAt(Point screen) { try { return Math.Max(1f, Math.Min(4f, W.DpiScaleAt(screen))); } catch { return 1f; } }
        // de schaal op de plek van de tekst die je typt (de muis kan op een ander scherm staan, bijvoorbeeld de beamer)
        float TypingScale { get { return typing != null ? ScaleAt(PointToScreen(Point.Round(typing.Pts[0]))) : ScreenScale; } }

        void WheelText(int delta)
        {
            float sc = TypingScale;
            float cur = typing != null ? typing.FontSize / sc : Cfg.TextSize;
            float nw = NextSize(TextSizes, cur, delta);
            Cfg.TextSize = nw;
            nw *= sc;
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
            if (toolbar != null) toolbar.ShowWidthTip(Control.MousePosition, Cfg.TextSize, TextSizes[0], TextSizes[TextSizes.Length - 1], L.T("Tekstgrootte"));
        }

        public float StampPx { get { return Cfg.StampPx > 0 ? Cfg.StampPx : Render.StampSizes[Cfg.StampSize]; } }

        void WheelStamp(int delta)
        {
            Cfg.StampPx = NextSize(StampPxSizes, StampPx, delta);
            UpdateCursor();
            if (toolbar != null) toolbar.ShowWidthTip(Control.MousePosition, Cfg.StampPx, StampPxSizes[0], StampPxSizes[StampPxSizes.Length - 1], L.T("Stempel"));
        }
    }
}
