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
        // Voor de automatische test op GitHub: alles een keer openen, plaatjes maken en weer netjes sluiten
        public void RunSelfTest()
        {
            Cfg.Language = "nl"; L.Set("nl");   // de schermafbeeldingen van de test in het Nederlands (Rida kijkt mee)
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
                            {
                                // Opnieuw blijft bewaard als je van pagina wisselt of het bord dicht en weer open doet
                                SetBoard(Board.White);
                                Stroke ts = new Stroke(); ts.Tool = Tool.Pen; ts.Color = Color.Red; ts.Width = 4; ts.Pts.Add(new PointF(100, 100)); ts.Pts.Add(new PointF(200, 150));
                                Checkpoint(); strokes.Add(ts); StoreActive();
                                Undo();
                                NewPage(); PrevPage();
                                SetBoard(Board.None); SetBoard(Board.White);
                                Redo();
                                bool ok = strokes.Contains(ts);
                                Console.WriteLine("Opnieuw na pagina en bord wisselen: " + ok);
                                if (!ok) { Program.SelfTestErrors++; Console.Error.WriteLine("Opnieuw gaat verloren bij wisselen van pagina of bord"); }
                                SetBoard(Board.None);
                                pages.Clear(); pages.Add(new List<Stroke>()); pageIndex = 0; pageHist.Clear(); undo.Clear(); redo.Clear(); Rebuild();
                            }
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
                            if (lesTimer != null)
                            {
                                // timer op het hele scherm: vult precies het hele scherm, in alle drie de weergaven
                                int keepView = Cfg.TimerView;
                                for (int v = 0; v < 3; v++) { Cfg.TimerView = v; lesTimer.FullForTest(); snap(lesTimer, "21-timer-heel-scherm-" + (v + 1)); }
                                Rectangle sb = Screen.FromControl(lesTimer).Bounds;
                                bool fills = lesTimer.Bounds == sb;
                                Console.WriteLine("Timer op het hele scherm: " + lesTimer.Bounds + ", scherm " + sb + (fills ? " (vult het hele scherm)" : ""));
                                if (!fills) { Program.SelfTestErrors++; Console.Error.WriteLine("De timer vult niet het hele scherm"); }
                                lesTimer.ExitFull(); Cfg.TimerView = keepView;
                                // schermen anders (zelfde plek, andere resolutie): de tekening schaalt mee en blijft op dat scherm
                                Rectangle mon = Screen.PrimaryScreen.Bounds;
                                Stroke probe = new Stroke(); probe.Tool = Tool.Line; probe.Color = Color.Red; probe.Width = 8;
                                probe.Pts.Add(new PointF(mon.X - vs.X + 200, mon.Y - vs.Y + 200)); probe.Pts.Add(new PointF(mon.X - vs.X + 400, mon.Y - vs.Y + 200));
                                strokes.Add(probe);
                                List<KeyValuePair<string, Rectangle>> fakeOld = new List<KeyValuePair<string, Rectangle>>();
                                foreach (KeyValuePair<string, Rectangle> kv in W.NamedMonitors()) fakeOld.Add(kv.Value == mon ? new KeyValuePair<string, Rectangle>(kv.Key, new Rectangle(mon.X, mon.Y, mon.Width * 2, mon.Height * 2)) : kv);
                                NoteScreensChanging(fakeOld, vs);
                                if (remapTimer != null) remapTimer.Stop();
                                remapStrokes = new HashSet<Stroke>(); remapStrokes.Add(probe);   // alleen de proefstreep (de rest van de test blijft staan)
                                RemapToScreens();
                                PointF q = probe.Pts[0];
                                bool ok = Math.Abs(q.X - (mon.X - vs.X + 100)) < 1 && Math.Abs(q.Y - (mon.Y - vs.Y + 100)) < 1 && Math.Abs(probe.Width - 4) < 0.01f;
                                Console.WriteLine("Tekening na scherm-wissel: " + q + ", dikte " + probe.Width + (ok ? " (klopt)" : ""));
                                if (!ok) { Program.SelfTestErrors++; Console.Error.WriteLine("De tekening schuift niet goed mee met een scherm-wissel"); }
                                strokes.Remove(probe); Rebuild(); RenderFrame();
                            }
                            foreach (Loupe l in pinnedLoupes) if (l.Strip != null) snap(l.Strip, "22-vergrootglas-balkje");
                            CloseAllLoupes();
                            if (lesTimer != null) lesTimer.EditForTest();   // klik op de cijfers: zelf een tijd typen
                            break;
                        case 23:
                            {
                                bool open = lesTimer != null && lesTimer.Editing;
                                Console.WriteLine("Timer: invulvak blijft staan: " + open + ", vooraan: " + (lesTimer != null && lesTimer.EditorInFront));
                                if (!open) { Program.SelfTestErrors++; Console.Error.WriteLine("Het invulvak van de timer sluit meteen weer"); }
                                screen("23-timer-tijd-typen");
                                if (open) lesTimer.FinishEditForTest("2:30");
                            }
                            break;
                        case 24:
                            {
                                bool ok = lesTimer != null && !lesTimer.Editing && lesTimer.SetSecondsForTest == 150;
                                Console.WriteLine("Timer: 2:30 getypt en Enter: " + ok);
                                if (!ok) { Program.SelfTestErrors++; Console.Error.WriteLine("Een getypte tijd in de timer werkt niet"); }
                                ToggleTimer();
                                SetBoard(Board.White);
                                LastShotPath = null; Screenshot();
                            }
                            break;
                        case 25:
                            {
                                bool ok = LastShotPath != null && File.Exists(LastShotPath);
                                Console.WriteLine("Schermafbeelding opgeslagen: " + ok + (ok ? " (" + LastShotPath + ")" : ""));
                                if (!ok) { Program.SelfTestErrors++; Console.Error.WriteLine("Schermafbeelding wordt niet opgeslagen"); }
                                else
                                {
                                    // het witte bord moet op de foto staan (anders mist de tekenlaag)
                                    try
                                    {
                                        using (Bitmap shot = new Bitmap(LastShotPath))
                                        {
                                            Rectangle ps = Screen.PrimaryScreen.Bounds, vs = SystemInformation.VirtualScreen;
                                            // een paar plekken, want een lijntje van het bord kan precies op een plek liggen
                                            Color c = Color.Empty; bool board = false;
                                            foreach (int dy in new int[] { 37, 51, 66, 83, 99 })
                                            {
                                                c = shot.GetPixel(ps.X - vs.X + ps.Width / 2, ps.Y - vs.Y + ps.Height / 2 + dy);
                                                if (c.R > 225 && c.G > 225 && c.B > 225) { board = true; break; }
                                            }
                                            Console.WriteLine("Bord op de schermafbeelding: " + board + " (" + c + ")");
                                            if (!board) { Program.SelfTestErrors++; Console.Error.WriteLine("De tekenlaag staat niet op de schermafbeelding"); }
                                        }
                                    }
                                    catch (Exception ex) { Program.SelfTestErrors++; Console.Error.WriteLine("Schermafbeelding nakijken: " + ex.Message); }
                                }
                                screen("24-melding-schermafbeelding");
                                ShowSettings(null);
                            }
                            break;
                        case 26: break;   // even wachten: de tekenlaag zet zichzelf elke 900 ms weer bovenop
                        case 27:
                            {
                                bool ok = settingsForm != null && settingsForm.Visible && BoardMode == Board.White;
                                if (ok)
                                {
                                    Rectangle sb = settingsForm.Bounds;
                                    W.POINT mid; mid.X = sb.Left + sb.Width / 2; mid.Y = sb.Top + sb.Height / 2;
                                    IntPtr hit = W.GetAncestor(W.WindowFromPoint(mid), 2);   // GA_ROOT
                                    ok = hit == settingsForm.Handle;
                                }
                                Console.WriteLine("Instellingen boven het bord: " + ok);
                                if (!ok) { Program.SelfTestErrors++; Console.Error.WriteLine("Instellingen komt niet boven het bord"); }
                                screen("25-instellingen-op-het-bord");
                            }
                            break;
                        default:
                            if (settingsForm != null && pageStep < pagesToShow.Length)
                            {
                                settingsForm.ShowPage(pagesToShow[pageStep]);
                                settingsForm.Refresh();
                                snap(settingsForm, "10-instellingen-" + (pageStep + 1).ToString("00") + "-" + pagesToShow[pageStep]);
                                pageStep++;
                                if (pageStep == pagesToShow.Length) { L.Set("en"); settingsForm.ShowPage("Algemeen"); settingsForm.Refresh(); snap(settingsForm, "11-english"); snap(toolbar, "12-werkbalk-english");
                                    // Chinees en Arabisch: komen de letters goed op het scherm?
                                    L.Set("zh"); settingsForm.Rebuild(); settingsForm.ShowPage("Taal"); settingsForm.Refresh(); snap(settingsForm, "11-chinees");
                                    L.Set("ar"); settingsForm.Rebuild(); settingsForm.ShowPage("Algemeen"); settingsForm.Refresh(); snap(settingsForm, "11-arabisch");
                                    L.Set("nl"); settingsForm.Rebuild();
                                    // licht thema: zijn de letters rechts ook scherp?
                                    int keepTheme = Cfg.ThemeIndex; Cfg.ThemeIndex = 1; ApplySettings(); settingsForm.Rebuild(); settingsForm.ShowPage("Uiterlijk"); settingsForm.Refresh(); snap(settingsForm, "11-licht-uiterlijk");
                                    Cfg.ThemeIndex = keepTheme; ApplySettings();
                                    settingsForm.ShowPage("Knoppen"); settingsForm.ScrollToEnd(); settingsForm.Refresh(); snap(settingsForm, "11-knoppen-gescrold"); }
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
                // sneltoetsen overal: in een ander programma alleen gereedschap en kleur met Ctrl+Shift, nooit Ctrl+Z of opslaan
                {
                    int keepMode = Cfg.ShortcutMode; Cfg.ShortcutMode = 0;
                    bool penOk = ShortcutAction(new KeyEventArgs(Keys.Control | Keys.Shift | Keys.P), true) != null;
                    bool colorOk = ShortcutAction(new KeyEventArgs(Keys.Control | Keys.Shift | Keys.D2), true) != null;
                    bool undoFree = ShortcutAction(new KeyEventArgs(Keys.Control | Keys.Z), true) == null && ShortcutAction(new KeyEventArgs(Keys.Control | Keys.S), true) == null;
                    bool plainFree = ShortcutAction(new KeyEventArgs(Keys.P), true) == null;
                    Cfg.ShortcutMode = keepMode;
                    bool ok = penOk && colorOk && undoFree && plainFree;
                    Console.WriteLine("Sneltoetsen in een ander programma: pen " + penOk + ", kleur " + colorOk + ", Ctrl+Z en Ctrl+S vrij " + undoFree + ", gewone letter vrij " + plainFree);
                    if (!ok) { Program.SelfTestErrors++; Console.Error.WriteLine("Sneltoetsen in een ander programma kloppen niet"); }
                }
                // klopt het streepje met de getekende letters? per lettertype: verschil tussen de positie na elke letter en de inkt
                foreach (string fn in new string[] { "Nunito", "Andika", "Lexend", "Segoe UI" })
                {
                    Stroke ms = new Stroke(); ms.Tool = Tool.Text; ms.Text = "Het boek is echt niet gescand."; ms.FontName = fn; ms.FontSize = 64; ms.Outline = true; ms.OutlineWidth = 1; ms.Pts.Add(new PointF(0, 0));
                    float worst = 0, worstOld = 0;
                    using (TextLayout lay = new TextLayout(ms))
                    {
                        string l = ms.Text;
                        for (int k = 1; k <= l.Length; k++)
                        {
                            if (l[k - 1] == ' ') continue;
                            using (GraphicsPath gp = new GraphicsPath())
                            {
                                gp.AddString(l.Substring(0, k), lay.Family, (int)lay.Style, lay.Size, lay.Origin, StringFormat.GenericTypographic);
                                float ink = gp.GetBounds().Right;
                                float now = lay.X(0, k), old = lay.Origin.X + lay.Width(l.Substring(0, k));
                                worst = Math.Max(worst, Math.Abs(ink - now)); worstOld = Math.Max(worstOld, Math.Abs(ink - old));
                            }
                        }
                    }
                    Console.WriteLine("Streepje na letters, " + fn + ": grootste afstand tot de inkt " + worst.ToString("0.0") + " px (oude manier " + worstOld.ToString("0.0") + " px, letters 64 px)");
                }
                // tekst met rand in alle lettertypes: geen zwarte vakjes in letters als de t
                float ty = 40;
                foreach (string fn in new string[] { "Andika", "Nunito", "Lexend", "Atkinson Hyperlegible", "Patrick Hand", "Segoe UI", "Arial", "Calibri", "Verdana" })
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
            m.MenuItems.Add(L.T("Opslaan voor de volgende keer") + (Shortcuts.Combo("opslaan").Length > 0 ? "\t" + Shortcuts.Combo("opslaan") : ""), delegate { if (SaveExplicit() && toolbar != null) toolbar.SavedFeedback(); });
            m.MenuItems.Add(L.T("Schermafbeelding maken"), delegate { Screenshot(); });
            if (PendingUpdate != null) m.MenuItems.Add(L.F("Bijwerken naar {0}", PendingUpdate.Version), delegate { InstallUpdate(); });
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
            RemoveKeyHook();
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
            if (m.Msg == W.WM_DPICHANGED) { m.Result = IntPtr.Zero; BeginInvoke((MethodInvoker)delegate { FitToScreens(false); }); return; } // de tekenlaag zelf nooit laten schalen, wel weer passend maken
            if (m.Msg >= 0x0245 && m.Msg <= 0x0247) ReadPenPressure(m.WParam);   // pen bewogen, neergezet of opgetild: druk onthouden (daarna maakt Windows er gewone muis van)
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
    }
}
