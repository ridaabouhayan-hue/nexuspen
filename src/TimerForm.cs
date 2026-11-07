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
        DateTime lastTick, doneAt;
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
            setSeconds = Math.Max(10, Math.Min(99 * 60, seconds));
            leftMs = setSeconds * 1000.0;
            done = false;
            Usage.Log("timer", (setSeconds / 60).ToString(), "kiezen");
            Invalidate();
        }

        void StartPause()
        {
            if (done || leftMs <= 0) { leftMs = setSeconds * 1000.0; done = false; }
            running = !running;
            lastTick = DateTime.Now;
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
                leftMs -= (now - lastTick).TotalMilliseconds;
                lastTick = now;
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
            Item time = Add(new RectangleF(x, y, timeW, 40 * S), L.T("Klik om te starten of te pauzeren"), delegate { StartPause(); });
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
            Item minus = Add(new RectangleF(cx, y, chip, 26 * S), L.T("Een minuut minder"), delegate { SetTime(setSeconds - 60); });
            minus.Paint = delegate(Graphics g, Item it, bool h) { PaintSmall(g, it, h, "-1", false); };
            cx += chip + gap;
            Item plus = Add(new RectangleF(cx, y, chip, 26 * S), L.T("Een minuut meer"), delegate { SetTime(setSeconds + 60); });
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
