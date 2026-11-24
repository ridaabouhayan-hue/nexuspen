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
                case Keys.Escape:
                    lastEsc = true;
                    // eerst alleen het menu dicht, je typt gewoon verder (het menu kan door dezelfde Esc net al dicht zijn)
                    if (MenuPopup.IsOpen || (DateTime.Now - MenuPopup.ClosedAt).TotalMilliseconds < 400) { MenuPopup.CloseOpen(); return true; }
                    CloseTextInput(true); return true;
                case Keys.Enter:
                    if (ctrl) { CloseTextInput(true); return true; }
                    ed.Insert("\n"); break;
                case Keys.Back: ed.Backspace(ctrl); break;
                case Keys.Delete:
                    // Delete aan het eind van de tekst (er staat niets meer achter het streepje): het hele tekstvak weg
                    // (alleen bij een nieuwe druk: wie Delete vasthoudt om letters te wissen, verliest niet ineens het hele vak)
                    if (!ctrl && !shift && !ed.HasSelection && ed.Caret >= ed.Text.Length) { if (!deleteHeld) DeleteTypingBox(); deleteHeld = true; return true; }
                    deleteHeld = true;
                    ed.Delete(ctrl); break;
                case Keys.Left: ed.Left(ctrl, shift); break;
                case Keys.Right: ed.Right(ctrl, shift); break;
                case Keys.Up: using (TextLayout lay = new TextLayout(typing)) ed.Vertical(-1, shift, lay); break;
                case Keys.Down: using (TextLayout lay = new TextLayout(typing)) ed.Vertical(1, shift, lay); break;
                case Keys.Home: ed.Home(ctrl, shift); break;
                case Keys.End: ed.End(ctrl, shift); break;
                case Keys.A: if (ctrl) ed.SelectAll(); else handled = false; break;
                case Keys.C: if (ctrl) CopyText(false); else handled = false; break;
                case Keys.X: if (ctrl) CopyText(true); else handled = false; break;
                case Keys.V: if (ctrl) PasteText(); else handled = false; break;
                case Keys.Z:
                    if (!ctrl) { handled = false; break; }
                    if (shift) { RedoInTyping(); break; }
                    if (ed.Undo()) { zHeld = true; break; }
                    // niets meer terug te zetten in deze tekst: dan verder terug, zoals in Word.
                    // (bijv. net ergens anders geklikt met Tekst: dan staat er een leeg nieuw vakje, en dat telt niet mee)
                    // Alleen bij een nieuwe druk: Ctrl+Z vasthouden schiet niet door naar eerdere tekeningen.
                    if (zHeld) break;
                    zHeld = true;
                    e.Handled = true; e.SuppressKeyPress = true;
                    UndoPastTyping();
                    return true;
                case Keys.Y: if (ctrl) RedoInTyping(); else handled = false; break;
                case Keys.Oemplus: case Keys.Add: if (ctrl) WheelText(120); else handled = false; break;
                case Keys.OemMinus: case Keys.Subtract: if (ctrl) WheelText(-120); else handled = false; break;
                default: handled = false; break;
            }
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; TypingChanged(); }
            return true;   // tijdens het typen doen sneltoetsen niets; letters komen via OnKeyPress
        }

        void PasteText()
        {
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
        }

        // Rechtermuisknop in de tekst: het gewone menu van Windows (knippen, kopiëren, plakken, alles selecteren)
        void ShowTextMenu(Point screen)
        {
            bool sel = ed.HasSelection, any = ed.Text.Length > 0, paste = false;
            try { paste = Clipboard.ContainsText(); } catch (Exception ex) { Quiet("Klembord lezen", ex); }
            MenuPopup m = new MenuPopup(this);
            Action<Action> run = delegate(Action a) { if (typing == null) return; a(); TypingChanged(); };
            m.Add(L.T("Knippen"), "Ctrl+X", sel ? (Action)delegate { run(delegate { CopyText(true); }); } : null);
            m.Add(L.T("Kopiëren"), "Ctrl+C", sel ? (Action)delegate { run(delegate { CopyText(false); }); } : null);
            m.Add(L.T("Plakken"), "Ctrl+V", paste ? (Action)delegate { run(PasteText); } : null);
            m.AddSeparator();
            m.Add(L.T("Alles selecteren"), "Ctrl+A", any ? (Action)delegate { run(delegate { ed.SelectAll(); }); } : null);
            m.ShowAt(screen);
        }

        void CopyText(bool cut)
        {
            string t = ed.HasSelection ? ed.Selected : ed.Text;
            try { if (t.Length > 0) Clipboard.SetText(t.Replace("\n", "\r\n")); } catch (Exception ex) { Quiet("Klembord schrijven", ex); }
            if (cut && ed.HasSelection) ed.DeleteSelection();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete && deleteHeld && typing == null) { e.Handled = true; return; }   // herhaling van dezelfde Delete na het weghalen van een tekstvak
            if (TypingKey(e)) return;
            if (!DrawMode) return;   // de tekenlaag kreeg per ongeluk het toetsenbord (bijv. na Instellingen sluiten): toetsen zijn dan voor je eigen programma
            Action a = ShortcutAction(e, false);
            if (a != null) { e.Handled = true; e.SuppressKeyPress = true; a(); }
        }

        // Welke actie hoort bij deze toets? null = geen (dan is de toets voor je eigen programma).
        // toolsOnly: alleen gereedschap en kleuren (als je in een ander programma werkt en nog niet tekent).
        Action ShortcutAction(KeyEventArgs e, bool toolsOnly)
        {
            if (!toolsOnly)
            {
                // op een bord: Ctrl+plus en Ctrl+min zoomen in en uit, Ctrl+0 terug naar 100%
                if (BoardMode != Board.None && e.Control && !e.Alt)
                {
                    Keys k = e.KeyCode;
                    // de + zit op veel toetsenborden onder Shift: Ctrl+Shift+= telt ook
                    int dir = (k == Keys.Oemplus || k == Keys.Add) ? 1 : (!e.Shift && (k == Keys.OemMinus || k == Keys.Subtract)) ? -1 : (!e.Shift && (k == Keys.D0 || k == Keys.NumPad0)) ? 0 : 99;
                    if (dir != 99)
                        return delegate
                        {
                            if (dir == 0) Cfg.BoardZoom = 1;
                            ZoomBoard(dir);
                            if (toolbar != null) toolbar.RefreshBoardPopup();
                            if ((DateTime.Now - lastZoomLog).TotalSeconds > 2) { lastZoomLog = DateTime.Now; Usage.Log("bord", "zoom", "sneltoets"); }
                        };
                }
                if (selected != null && !dragging && !e.Control && !e.Alt)
                {
                    float step = e.Shift ? 10 : 1;
                    float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                    float dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                    if (dx != 0 || dy != 0)
                        return delegate
                        {
                            if (selected == null) return;
                            if ((DateTime.Now - lastNudge).TotalMilliseconds > 600) EditSelected(); else MarkChanged();
                            lastNudge = DateTime.Now; Render.Move(selected, dx, dy); Rebuild();
                            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
                        };
                    if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) return delegate { if (selected != null) DeleteSelected(); };
                }
            }
            string act = Shortcuts.ActionFor(e);
            if (act == null && e.KeyCode == Keys.Z && e.Control && e.Shift && !e.Alt && Shortcuts.Combo("redo").Length > 0) act = "redo";   // Ctrl+Shift+Z werkt ook als Opnieuw
            if (act != null && toolsOnly && (act == "undo" || act == "redo" || act == "opslaan" || act == "kopie")) act = null;   // die horen bij je eigen programma
            if (act != null)
            {
                string a2 = act;
                return delegate
                {
                    Usage.Log("sneltoets", a2, Shortcuts.Combo(a2));
                    switch (a2)
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
                        case "bord": ToggleBoard(); break;
                        case "foto": Screenshot(); break;
                        case "undo": Undo(); break;
                        case "redo": Redo(); break;
                        case "opslaan": if (SaveExplicit() && toolbar != null) toolbar.SavedFeedback(); break;
                        case "kopie": DuplicateSelected(); break;
                    }
                };
            }
            int n = Shortcuts.ColorFor(e);
            if (n >= 0) return delegate { if (n < ColorCount) SetColor(n); };
            if (toolsOnly || e.Control || e.Alt) return null;
            // Delete zonder selectie doet niets: alles in een keer weghalen kan alleen met de prullenbak
            if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) return delegate { SetWidth(PenWidth + (PenWidth < 6 ? 1 : 2)); };
            if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) return delegate { SetWidth(PenWidth - (PenWidth <= 6 ? 1 : 2)); };
            return null;
        }

        // ---------- Sneltoetsen overal
        // De tekenlaag krijgt het toetsenbord niet (dan zou je eigen programma stoppen met werken). Daarom kijkt NexusPen
        // met een toetsenbord-haak van Windows mee naar alleen de toetsen die een NexusPen-sneltoets zijn:
        // - tijdens het tekenen: alle sneltoetsen (ook Ctrl+Z, kleuren, dikte), welk programma ook vooraan staat;
        // - als je in een ander programma werkt: alleen Ctrl+Shift + letter of cijfer (gereedschap en kleur), als dat aanstaat.
        // Elke andere toets gaat ongemoeid door. Er wordt niets onthouden of bewaard van wat je typt.
        // De haak draait op een eigen, rustige draad: dan wacht typen in andere programma's nooit op NexusPen
        // (ook niet als die even druk is met tekenen of bewaren), en haalt Windows de haak niet weg.
        W.LowLevelKeyboardProc keyProc;
        volatile IntPtr keyHook = IntPtr.Zero;
        System.Threading.Thread keyThread;
        volatile uint keyThreadId;
        // toetsen die NexusPen nu vasthoudt, met het moment dat we ze het laatst zagen (alleen op de draad van de haak).
        // Een ingeslikte toets telt Windows niet als ingedrukt, dus herhalingen herkennen we zelf aan de tijd.
        readonly Dictionary<int, int> keysHeld = new Dictionary<int, int>();
        static readonly Action NoOp = delegate { };
        System.Threading.ManualResetEvent keyReady;

        public void ApplyKeyHook()
        {
            bool want = Cfg.ShortcutMode != 2 && !Program.SelfTest;
            try
            {
                if (want && keyThread == null)
                {
                    keyReady = new System.Threading.ManualResetEvent(false);
                    keyThread = new System.Threading.Thread(KeyThreadRun);
                    keyThread.IsBackground = true;
                    keyThread.Name = "NexusPen sneltoetsen";
                    keyThread.Start();
                    keyReady.WaitOne(2000);   // wachten tot de draad klaar is: dan kan hij ook altijd weer netjes stoppen
                }
                else if (!want && keyThread != null) RemoveKeyHook();
            }
            catch (Exception ex) { Log.Error("Sneltoetsen overal", ex); }
        }

        void KeyThreadRun()
        {
            try
            {
                W.MSG m;
                W.PeekMessage(out m, IntPtr.Zero, 0, 0, 0);   // zo heeft deze draad een berichtenrij (anders komt het stopbericht niet aan)
                keyThreadId = W.GetCurrentThreadId();
                keyProc = KeyHookProc;   // vasthouden, anders ruimt .NET hem op terwijl Windows hem nog gebruikt
                keyHook = W.SetWindowsHookEx(13, keyProc, W.GetModuleHandle(null), 0);
                System.Threading.ManualResetEvent ready = keyReady;
                if (keyHook == IntPtr.Zero)
                {
                    Log.Info("Sneltoetsen overal: kon niet worden aangezet (" + Marshal.GetLastWin32Error() + ")");
                    BeginInvoke((MethodInvoker)delegate { keyThread = null; keyThreadId = 0; });   // de volgende keer opnieuw proberen
                    if (ready != null) ready.Set();
                    return;
                }
                if (ready != null) ready.Set();
                while (W.GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }   // alleen wachten; Windows roept de haak vanzelf aan
            }
            catch (Exception ex) { Log.Error("Sneltoetsen overal", ex); }
            finally
            {
                try { if (keyHook != IntPtr.Zero) W.UnhookWindowsHookEx(keyHook); } catch { }
                keyHook = IntPtr.Zero;
                lock (keysHeld) keysHeld.Clear();
            }
        }

        void RemoveKeyHook()
        {
            System.Threading.Thread t = keyThread;
            try { if (keyThreadId != 0) W.PostThreadMessage(keyThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero); } catch { }   // WM_QUIT: de draad stopt en haalt de haak weg
            try { if (t != null) t.Join(1000); } catch { }   // echt weg voordat er eventueel een nieuwe komt (anders twee haken: alles dubbel)
            keyThread = null; keyThreadId = 0;
        }

        IntPtr KeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    int vk = Marshal.ReadInt32(lParam);
                    int flags = Marshal.ReadInt32(lParam, 8);
                    if ((flags & 0x10) != 0) return W.CallNextHookEx(keyHook, nCode, wParam, lParam);   // nagemaakte toetsen (ook die van onszelf): niet aankomen
                    bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                    lock (keysHeld)
                    {
                        if (up && keysHeld.Remove(vk)) return new IntPtr(1);   // ook het loslaten van een toets die NexusPen gebruikte
                        if (down)
                        {
                            int now = Environment.TickCount, seen;
                            // nog vast (herhaling komt elke 30 tot 500 ms); na meer dan een seconde stilte is het loslaten gemist: een nieuwe druk
                            if (keysHeld.TryGetValue(vk, out seen) && now - seen > 1000) keysHeld.Remove(vk);
                            if (keysHeld.ContainsKey(vk))
                            {
                                keysHeld[vk] = now;
                                // ingedrukt gehouden: blijft van NexusPen tot je loslaat (anders krijgt je programma een halve toets)
                                Action again = GlobalShortcut(vk, true);
                                if (again != null && again != NoOp) BeginInvoke(again);
                                return new IntPtr(1);
                            }
                            Action a = GlobalShortcut(vk, false);
                            if (a != null)
                            {
                                keysHeld[vk] = now;
                                // Ctrl, Shift of Alt ingedrukt: een lege toets erachteraan, anders denkt Windows dat je alleen Ctrl+Shift
                                // drukte (dat wisselt de toetsenbordtaal) of alleen Alt (dat opent het menu van je programma)
                                if (KeyDown(0x11) || KeyDown(0x10) || KeyDown(0x12)) W.TapMaskKey();
                                BeginInvoke(a);   // meteen terug naar Windows; het werk zelf gebeurt in NexusPen
                                return new IntPtr(1);
                            }
                        }
                    }
                }
            }
            catch { }
            return W.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        static bool KeyDown(int vk) { return (W.GetAsyncKeyState(vk) & 0x8000) != 0; }

        // Leest alleen (vanaf de draad van de haak); het werk zelf gaat via BeginInvoke naar NexusPen
        Action GlobalShortcut(int vk, bool repeat)
        {
            if (IsDisposed || !IsHandleCreated || Cfg.ShortcutMode == 2 || typing != null || allHidden) return null;
            if ((vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C || vk == 0x1B) return null;   // Ctrl, Shift, Alt, Windows, Esc
            if (KeyDown(0x5B) || KeyDown(0x5C)) return null;   // Windows-toets erbij: altijd van Windows
            // een venster van NexusPen zelf vooraan (Instellingen, typen): dan gaat het zoals altijd
            IntPtr fg = W.GetForegroundWindow();
            if (fg != IntPtr.Zero && IsOurs(fg)) return null;
            Keys k = (Keys)vk;
            if (KeyDown(0x11)) k |= Keys.Control;
            if (KeyDown(0x10)) k |= Keys.Shift;
            if (KeyDown(0x12)) k |= Keys.Alt;
            KeyEventArgs e = new KeyEventArgs(k);
            Action a;
            if (DrawMode) a = ShortcutAction(e, false);   // je tekent: alle sneltoetsen zijn van NexusPen
            else
            {
                // je werkt in een ander programma: alleen als je dat aanzette, en alleen Ctrl+Shift + letter of cijfer
                if (!Cfg.ShortcutsEverywhere || Cfg.ShortcutMode != 0 || !e.Control || !e.Shift || e.Alt) return null;
                string act = Shortcuts.ActionFor(e);
                if (act == "foto" || act == "bord") return null;   // nooit ongevraagd een schermafbeelding of het bord over je les
                a = ShortcutAction(e, true);
            }
            if (a == null) return null;
            // ingedrukt houden: alleen schuiven, dikte, zoom en ongedaan maken herhalen (geen tien schermafbeeldingen)
            if (repeat)
            {
                Keys c = e.KeyCode;
                bool again = c == Keys.Left || c == Keys.Right || c == Keys.Up || c == Keys.Down || c == Keys.Oemplus || c == Keys.Add || c == Keys.OemMinus || c == Keys.Subtract
                    || (e.Control && (c == Keys.Z || c == Keys.Y));
                if (!again) return NoOp;
            }
            return a;
        }
    }
}
