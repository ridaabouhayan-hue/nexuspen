using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ Timer op het scherm
    // Een tijd kiezen (snelknoppen of min en plus), starten en pauzeren, klein of groot, verslepen.
    // Aan het eind een zacht geluidje en de tijd knippert even in de accentkleur. Cursisten zien hem ook bij schermdelen.
    class TimerForm : BarForm
    {
        int setSeconds = 5 * 60;          // de gekozen tijd
        double leftMs = 5 * 60 * 1000;    // wat er nog over is
        bool running, done;
        DateTime doneAt;
        readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();   // niet van de Windows-klok afhankelijk
        double lastTick;
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer();
        float baseS = 1;

        static readonly int[] Presets = { 1, 3, 5, 10, 15, 20 };

        public TimerForm(Overlay o) : base(o)
        {
            Text = "NexusPen timer";
            draggable = true;
            clock.Interval = 200;
            clock.Tick += delegate { Step(); };
        }

        protected override bool AlwaysShared { get { return true; } }

        // de timerknop in de balk laat zien of de timer open is
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && editFinish != null) editFinish(false);   // timer weg (kruisje of inklappen): het invulvak ook
            if (ov.Bar != null) ov.Bar.Refresh2();
        }

        public bool Running { get { return running; } }

        public void ShowNear(Point at)
        {
            baseS = Math.Max(1f, W.UiScaleAt(at));
            if (full) ExitFull();
            S = baseS * ov.Cfg.TimerZoom;
            Rebuild();
            Rectangle wz = Screen.FromPoint(at).WorkingArea;
            float fz = Math.Min(wz.Width * 0.95f / Width, wz.Height * 0.95f / Height);
            if (fz < 1f) { S *= fz; Rebuild(); }
            if (!Visible)
            {
                Rectangle wa = Screen.FromPoint(at).WorkingArea;
                Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, at.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, at.Y)));
                Show();
            }
        }

        string TimeText()
        {
            int sec = (int)Math.Ceiling(Math.Max(0, leftMs) / 1000.0);
            return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
        }

        void SetTime(int seconds)
        {
            setSeconds = Math.Max(1, Math.Min(99 * 60 + 59, seconds));
            leftMs = setSeconds * 1000.0;
            done = false;
            Usage.Log("timer", (setSeconds / 60).ToString(), "kiezen");
            Invalidate();
        }

        // Een minuut erbij of eraf, ook terwijl de timer loopt (nooit onder een minuut als hij stilstaat)
        void AddMinute(int dir)
        {
            if (running || leftMs < setSeconds * 1000.0 - 500)
            {
                leftMs = Math.Max(1000, Math.Min(99 * 60 * 1000.0, leftMs + dir * 60000.0));
                done = false;
                Invalidate();
                return;
            }
            SetTime(Math.Max(60, setSeconds + dir * 60));
        }

        // Ander thema, taal of grootte: opnieuw opbouwen op dezelfde plek
        public void Restyle()
        {
            Point keep = Location;
            baseS = Math.Max(1f, W.UiScaleAt(keep));
            if (full) { FitFull(); return; }
            S = baseS * ov.Cfg.TimerZoom;
            Rebuild();
            Location = keep;
        }

        void StartPause()
        {
            if (done || leftMs <= 0) { leftMs = setSeconds * 1000.0; done = false; }
            running = !running;
            lastTick = watch.Elapsed.TotalMilliseconds;
            if (running) clock.Start(); else clock.Stop();
            Usage.Log("timer", running ? "start" : "pauze", "");
            Invalidate();
        }

        // een klik op de timer zet een wekker die nog klinkt stil
        protected override void OnMouseDown(MouseEventArgs e) { TimerSound.Stop(); base.OnMouseDown(e); }

        void Reset()
        {
            running = false; done = false; clock.Stop();
            leftMs = setSeconds * 1000.0;
            Invalidate();
        }

        // ---- groter en kleiner: muiswiel boven de timer, of het hoekje rechtsonder slepen
        void SetZoom(float z, Point anchor)
        {
            z = Math.Max(0.7f, Math.Min(4f, z));
            Rectangle wa = Screen.FromPoint(anchor).WorkingArea;
            S = baseS * z;
            Rebuild();
            // nooit groter dan het scherm (anders valt de sluitknop eraf)
            float fit = Math.Min(wa.Width * 0.95f / Width, wa.Height * 0.95f / Height);
            if (fit < 1f) { z *= fit; S = baseS * z; Rebuild(); }
            ov.Cfg.TimerZoom = z;
            Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, anchor.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, anchor.Y)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (full || Editing) return;
            SetZoom(ov.Cfg.TimerZoom * (e.Delta > 0 ? 1.1f : 1 / 1.1f), Location);
            saveLater.Stop(); saveLater.Start();   // een keer bewaren als je klaar bent met draaien
        }

        System.Windows.Forms.Timer saveTimer;
        System.Windows.Forms.Timer saveLater
        {
            get
            {
                if (saveTimer == null) { saveTimer = new System.Windows.Forms.Timer(); saveTimer.Interval = 800; saveTimer.Tick += delegate { saveTimer.Stop(); ov.Cfg.Save(); }; }
                return saveTimer;
            }
        }
        bool resizing; int resizeFromX; float resizeFromZoom, resizeFromW;
        bool InCorner(Point p) { return !full && p.X > ClientSize.Width - 10 * S && p.Y > ClientSize.Height - 10 * S; }

        protected override bool OnPress(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !InCorner(e.Location) || Editing) return false;
            resizing = true; resizeFromX = Control.MousePosition.X; resizeFromZoom = ov.Cfg.TimerZoom; resizeFromW = Width; Capture = true;
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (resizing && (Control.MouseButtons & MouseButtons.Left) == 0) { resizing = false; ov.Cfg.Save(); }   // knop losgelaten buiten het venster
            if (resizing)
            {
                float w = Math.Max(40, resizeFromW + Control.MousePosition.X - resizeFromX);
                SetZoom(resizeFromZoom * w / resizeFromW, Location);
                return;
            }
            base.OnMouseMove(e);
            if (InCorner(e.Location)) Cursor = Cursors.SizeNWSE;
            else if (hover == null) Cursor = Cursors.SizeAll;   // overal waar geen knop zit: slepen
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (resizing) { resizing = false; ov.Cfg.Save(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (resizing) { resizing = false; Capture = false; ov.Cfg.Save(); return; }
            base.OnMouseUp(e);
        }

        // het hoekje om groter of kleiner te slepen: twee schuine streepjes
        protected override void PaintExtra(Graphics g)
        {
            if (full) return;
            float x = ClientSize.Width - 5 * S, y = ClientSize.Height - 5 * S;
            using (Pen p = new Pen(Color.FromArgb(120, T.Muted), 1.3f * S))
            {
                g.DrawLine(p, x - 8 * S, y, x, y - 8 * S);
                g.DrawLine(p, x - 4 * S, y, x, y - 4 * S);
            }
        }

        // ---- het hele scherm: grote cijfers voor de hele groep. Esc of dezelfde knop: terug
        bool full; Point beforeFull;
        public bool IsFull { get { return full; } }

        void ToggleBig() { if (full) ExitFull(); else EnterFull(); }

        void EnterFull()
        {
            if (Editing && editFinish != null) editFinish(true);
            beforeFull = Location; full = true;
            FitFull();
            Usage.Log("timer", "volledig scherm", "");
        }

        void FitFull()
        {
            Rectangle sb = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2)).Bounds;
            if (full && beforeFull != Point.Empty) sb = Screen.FromPoint(beforeFull).Bounds;
            S = baseS; Rebuild();
            float k = Math.Min(sb.Width * 0.94f / Width, sb.Height * 0.9f / Height);
            S = baseS * Math.Max(1f, k);
            Rebuild();
            Location = new Point(sb.Left + (sb.Width - Width) / 2, sb.Top + (sb.Height - Height) / 2);
        }

        public void ExitFull()
        {
            if (!full) return;
            full = false;
            S = baseS * ov.Cfg.TimerZoom;
            Rebuild();
            Rectangle wa = Screen.FromPoint(beforeFull).WorkingArea;
            Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, beforeFull.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, beforeFull.Y)));
        }

        void Step()
        {
            DateTime now = DateTime.Now;
            if (running)
            {
                double t = watch.Elapsed.TotalMilliseconds;
                leftMs -= t - lastTick;
                lastTick = t;
                if (leftMs <= 0)
                {
                    leftMs = 0; running = false; done = true; doneAt = now;
                    TimerSound.Play(ov.Cfg);
                    Usage.Log("timer", "klaar", "");
                }
            }
            if (!running && !done) clock.Stop();
            if (done && (now - doneAt).TotalSeconds > 6) clock.Stop();   // na een paar keer knipperen blijft 00:00 gewoon staan
            Invalidate();
        }

        protected override void Build()
        {
            float pad = 10 * S, btn = 30 * S, gap = 4 * S;
            float timeW = TextWidth("00:00", 30) + 8 * S;
            float x = pad, y = pad;
            // de tijd zelf (klik = starten of pauzeren)
            Item time = Add(new RectangleF(x, y, timeW, 40 * S), L.T("Klik om zelf een tijd te typen"), null);
            timeItem = time;
            time.Click = delegate { EditTime(); };
            time.Paint = delegate(Graphics g, Item it, bool h)
            {
                Theme t = T;
                bool blink = done && ((int)((DateTime.Now - doneAt).TotalMilliseconds / 450) % 2 == 0) && (DateTime.Now - doneAt).TotalSeconds < 6;
                if (blink) using (SolidBrush b = new SolidBrush(t.Accent)) using (GraphicsPath gp = Svg.RoundRect(it.R, 8 * S)) g.FillPath(b, gp);
                else if (h) using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(it.R, 8 * S)) g.FillPath(b, gp);
                Label(g, TimeText(), it.R, blink ? t.OnAccent : done ? t.Accent : t.Text, 30, true, StringAlignment.Center);
            };
            x += timeW + 8 * S;
            float by = y + (40 * S - btn) / 2;
            Item play = Add(new RectangleF(x, by, btn, btn), L.T("Starten of pauzeren"), delegate { StartPause(); });
            play.Paint = delegate(Graphics g, Item it, bool h) { PaintIconButton(g, it, h, running ? "pauze" : "afspelen", true); };
            x += btn + gap;
            Item reset = Add(new RectangleF(x, by, btn, btn), L.T("Opnieuw beginnen"), delegate { Reset(); });
            reset.Paint = delegate(Graphics g, Item it, bool h) { PaintIconButton(g, it, h, "terugzetten", false); };
            x += btn + gap;
            Item grow = Add(new RectangleF(x, by, btn, btn), L.T("Hele scherm (Esc = terug)"), delegate { ToggleBig(); });
            grow.Paint = delegate(Graphics g, Item it, bool h) { PaintIconButton(g, it, h, full ? "volterug" : "vol", false); };
            x += btn + gap;
            Item mute = Add(new RectangleF(x, by, btn, btn), L.T("Geluid aan of uit"), delegate { ov.Cfg.TimerMute = !ov.Cfg.TimerMute; if (ov.Cfg.TimerMute) TimerSound.Stop(); ov.Cfg.Save(); });
            mute.Paint = delegate(Graphics g, Item it, bool h) { PaintIconButton(g, it, h, ov.Cfg.TimerMute ? "geluiduit" : "geluid", false); };
            x += btn + gap;
            Item close = Add(new RectangleF(x, by, btn, btn), L.T("Timer sluiten"), delegate { running = false; clock.Stop(); TimerSound.Stop(); ExitFull(); Hide(); });
            close.Paint = delegate(Graphics g, Item it, bool h) { PaintIconButton(g, it, h, "kruis", false); };
            x += btn;
            float w = x + pad;
            y += 40 * S + 8 * S;
            // snelknoppen: min, plus en een paar vaste tijden
            float chip = (w - pad * 2 - gap * (Presets.Length + 1)) / (Presets.Length + 2);
            float cx = pad;
            Item minus = Add(new RectangleF(cx, y, chip, 26 * S), L.T("Een minuut minder"), delegate { AddMinute(-1); });
            minus.Paint = delegate(Graphics g, Item it, bool h) { PaintSmall(g, it, h, "-1", false); };
            cx += chip + gap;
            Item plus = Add(new RectangleF(cx, y, chip, 26 * S), L.T("Een minuut meer"), delegate { AddMinute(1); });
            plus.Paint = delegate(Graphics g, Item it, bool h) { PaintSmall(g, it, h, "+1", false); };
            cx += chip + gap;
            foreach (int m0 in Presets)
            {
                int m = m0;
                Item p = Add(new RectangleF(cx, y, chip, 26 * S), L.F("{0} minuten", m), delegate { running = false; clock.Stop(); SetTime(m * 60); });
                p.Paint = delegate(Graphics g, Item it, bool h) { PaintSmall(g, it, h, m.ToString(), setSeconds == m * 60); };
                cx += chip + gap;
            }
            y += 26 * S + pad;
            ClientSize = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(y));
        }

        // ---- zelf een tijd typen: een klein invulvakje precies over de cijfers
        Item timeItem;
        Form editor;
        Action<bool> editFinish;

        void EditTime()
        {
            if (editor != null && !editor.IsDisposed) return;
            bool wasRunning = running;
            if (running) { running = false; clock.Stop(); }
            Theme t = T;
            RectangleF r = timeItem.R;
            editor = new Form();
            editor.FormBorderStyle = FormBorderStyle.None; editor.ShowInTaskbar = false; editor.TopMost = true;
            editor.StartPosition = FormStartPosition.Manual; editor.BackColor = t.Bar;
            editor.Bounds = new Rectangle(Left + (int)r.X, Top + (int)r.Y, (int)r.Width, (int)r.Height);
            editor.Owner = this;
            TextBox tb = new TextBox();
            tb.BorderStyle = BorderStyle.None; tb.TextAlign = HorizontalAlignment.Center;
            tb.BackColor = t.Bar; tb.ForeColor = t.Text;
            tb.Font = UiFont.Make(26 * S, true);
            tb.Text = TimeText(); tb.MaxLength = 6;
            tb.Width = editor.ClientSize.Width - 8; tb.Left = 4; tb.Top = Math.Max(0, (editor.ClientSize.Height - tb.PreferredHeight) / 2);
            editor.Controls.Add(tb);
            Form me = editor;
            bool done = false;
            Action<bool> finish = delegate(bool keep)
            {
                if (done) return; done = true;
                int sec;
                if (keep && TryParseTime(tb.Text, out sec)) { SetTime(sec); Usage.Log("timer", "zelf getypt", ""); }
                else if (wasRunning) StartPause();
                try { me.Close(); } catch { }
                if (!IsDisposed) RenderNow();
            };
            tb.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; finish(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; finish(false); }
            };
            // Ergens anders klikken = klaar. Pas luisteren als het vakje echt vooraan staat: tijdens het
            // naar voren halen verliest het heel even de focus, en dan sloot het vroeger meteen weer.
            me.Shown += delegate
            {
                W.ForceForeground(me.Handle);   // meteen kunnen typen, ook als je net in een ander programma was
                me.Activate(); tb.Focus(); tb.SelectAll();
                me.BeginInvoke((Action)delegate { if (!done && !me.IsDisposed) me.Deactivate += delegate { finish(true); }; });
            };
            editFinish = finish;
            me.FormClosed += delegate
            {
                if (editor == me) { editor = null; editFinish = null; }
                try { tb.Font.Dispose(); } catch { }
            };
            me.Show();
        }

        // de Esc van het invulvakje is niet voor de tekenlaag
        public bool Editing { get { return editor != null && !editor.IsDisposed; } }
        public bool EditorInFront { get { return Editing && W.GetForegroundWindow() == editor.Handle; } }

        // voor de zelftest: het vakje openen, kijken of het blijft staan, en een tijd invullen
        public void EditForTest() { EditTime(); }
        public bool FinishEditForTest(string text)
        {
            if (!Editing) return false;
            TextBox tb = editor.Controls.Count > 0 ? editor.Controls[0] as TextBox : null;
            if (tb == null) return false;
            tb.Text = text;
            tb.Focus();
            W.SendMessage(tb.Handle, 0x0100, new IntPtr((int)Keys.Enter), IntPtr.Zero);   // Enter
            return true;
        }
        public int SetSecondsForTest { get { return setSeconds; } }

        // "5" = 5 minuten, "5:30" of "5.30" = 5 minuten en 30 seconden, "90s" = 90 seconden
        public static bool TryParseTime(string text, out int seconds)
        {
            seconds = 0;
            string t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace(".", ":").Replace(",", ":");
            if (t.Length == 0) return false;
            int m, sec;
            if (t.EndsWith("s") && int.TryParse(t.TrimEnd('s'), out sec)) { seconds = sec; }
            else if (t.Contains(":"))
            {
                string[] p = t.Split(':');
                if (p.Length != 2 || !int.TryParse(p[0].Length == 0 ? "0" : p[0], out m) || !int.TryParse(p[1].Length == 0 ? "0" : p[1], out sec) || m < 0 || sec < 0 || sec > 59) return false;
                seconds = m * 60 + sec;
            }
            else if (int.TryParse(t.TrimEnd('m'), out m)) seconds = m * 60;
            else return false;
            return seconds >= 1 && seconds <= 99 * 60 + 59;
        }

        void PaintIconButton(Graphics g, Item it, bool h, string icon, bool primary)
        {
            Theme t = T;
            if (primary) using (SolidBrush b = new SolidBrush(t.Accent)) using (GraphicsPath gp = Svg.RoundRect(it.R, 7 * S)) g.FillPath(b, gp);
            else if (h) using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(it.R, 7 * S)) g.FillPath(b, gp);
            Icons.Draw(g, icon, it.R, 16 * S, primary ? t.OnAccent : t.Icon, 2f);
        }

        void PaintSmall(Graphics g, Item it, bool h, string text, bool on)
        {
            Theme t = T;
            Color bg = on ? t.Pressed : h ? t.Hover : t.Chip;
            using (SolidBrush b = new SolidBrush(bg)) using (GraphicsPath gp = Svg.RoundRect(it.R, 6 * S)) g.FillPath(b, gp);
            Label(g, text, it.R, on ? t.Text : t.Muted, 12, true, StringAlignment.Center);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // niet echt sluiten (dan blijft de tijd bewaard), alleen verbergen
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; running = false; clock.Stop(); ExitFull(); Hide(); return; }
            clock.Stop(); clock.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------ Geluid als de timer klaar is
    // Vijf rustige eindsignalen van ongeveer 4,5 seconden (gemaakt met ElevenLabs, zie assets/sounds/LEESMIJ.txt),
    // ingebouwd in het programma zelf. Herhalen: 1, 2 of 3 keer, of tot je op de timer klikt (hooguit 30 seconden).
    static class TimerSound
    {
        public static readonly string[] Names = { "Klankschaal", "Drie tonen", "Piano", "Harp", "Belletjes" }; // vertaal
        static readonly string[] Files = { "timer-1-klankschaal.wav", "timer-2-drie-tonen.wav", "timer-3-piano.wav", "timer-4-harp.wav", "timer-5-belletjes.wav" };
        public static readonly string[] RepeatNames = { "1 keer", "2 keer", "3 keer", "Tot je op de timer klikt" }; // vertaal
        const int SoundMs = 4600;   // een geluid duurt ongeveer 4,5 seconden

        static System.Media.SoundPlayer player;
        static System.Windows.Forms.Timer again;
        static int left;
        static DateTime startedAt;
        static byte[][] cache = new byte[Files.Length][];

        static byte[] Data(int kind)
        {
            kind = Math.Max(0, Math.Min(Files.Length - 1, kind));
            if (cache[kind] != null) return cache[kind];
            using (System.IO.Stream st = typeof(TimerSound).Assembly.GetManifestResourceStream(Files[kind]))
            {
                if (st == null) return null;
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream()) { st.CopyTo(ms); cache[kind] = ms.ToArray(); }
            }
            return cache[kind];
        }

        // voor de test: zitten alle geluiden echt in het programma?
        public static bool AllPresent() { for (int i = 0; i < Files.Length; i++) { byte[] d = Data(i); if (d == null || d.Length < 10000) return false; } return true; }

        // de tijd is om: het gekozen geluid, zo vaak als ingesteld
        public static void Play(Settings c)
        {
            if (c == null || c.TimerMute) return;
            int times = c.TimerRepeat == 3 ? 100 : c.TimerRepeat + 1;
            Start(c.TimerSound, times);
        }

        // voorbeeld in Instellingen of het menu: een keer
        public static void Play(int kind, bool preview) { Start(kind, 1); }

        static void Start(int kind, int times)
        {
            Stop();
            try
            {
                byte[] d = Data(kind);
                if (d == null) { System.Media.SystemSounds.Asterisk.Play(); return; }
                player = new System.Media.SoundPlayer(new System.IO.MemoryStream(d));
                player.Play();
                left = times - 1; startedAt = DateTime.Now;
                if (left > 0)
                {
                    again = new System.Windows.Forms.Timer();
                    again.Interval = SoundMs;
                    again.Tick += delegate
                    {
                        // nooit langer dan 30 seconden, ook niet bij "tot je klikt"
                        if (left <= 0 || (DateTime.Now - startedAt).TotalSeconds > 30) { Stop(); return; }
                        left--;
                        try { if (player != null) player.Play(); } catch { Stop(); }
                    };
                    again.Start();
                }
            }
            catch (Exception ex) { Log.Error("Timergeluid", ex); try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }
        }

        public static void Stop()
        {
            try { if (again != null) { again.Stop(); again.Dispose(); again = null; } } catch { }
            try { if (player != null) { player.Stop(); player.Dispose(); player = null; } } catch { }
            left = 0;
        }
    }
}
