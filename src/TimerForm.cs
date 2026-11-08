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
        bool running, big, done;
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
            if (ov.Bar != null) ov.Bar.Refresh2();
        }

        public bool Running { get { return running; } }

        public void ShowNear(Point at)
        {
            baseS = Math.Max(1f, W.UiScaleAt(at));
            S = big ? baseS * 2.2f : baseS;
            Rebuild();
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
            S = big ? baseS * 2.2f : baseS;
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

        void Reset()
        {
            running = false; done = false; clock.Stop();
            leftMs = setSeconds * 1000.0;
            Invalidate();
        }

        void ToggleBig()
        {
            big = !big;
            Point keep = Location;
            S = big ? baseS * 2.2f : baseS;
            Rebuild();
            Rectangle wa = Screen.FromPoint(keep).WorkingArea;
            Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, keep.X)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, keep.Y)));
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
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
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
            Item grow = Add(new RectangleF(x, by, btn, btn), L.T("Groot of klein"), delegate { ToggleBig(); });
            grow.Paint = delegate(Graphics g, Item it, bool h) { PaintIconButton(g, it, h, "groter", false); };
            x += btn + gap;
            Item close = Add(new RectangleF(x, by, btn, btn), L.T("Timer sluiten"), delegate { running = false; clock.Stop(); Hide(); });
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
            me.FormClosed += delegate { if (editor == me) editor = null; };
            me.Show();
        }

        // de Esc van het invulvakje is niet voor de tekenlaag
        public bool Editing { get { return editor != null && !editor.IsDisposed; } }

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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (hover == null) Cursor = Cursors.SizeAll;   // overal waar geen knop zit: slepen
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // niet echt sluiten (dan blijft de tijd bewaard), alleen verbergen
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; running = false; clock.Stop(); Hide(); return; }
            clock.Stop(); clock.Dispose();
            base.OnFormClosing(e);
        }
    }
}
