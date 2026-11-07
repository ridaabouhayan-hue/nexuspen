using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ Vergrootglas
    // Gebruikt de vergrootfunctie van Windows zelf (Magnification), dezelfde als het Windows-vergrootglas.
    // Meegaand: het glas volgt de muis en je klikt er gewoon doorheen. Vastgezet: het blijft op zijn plek,
    // je kunt het verslepen, en met het kruisje gaat het weg. Cursisten zien het ook bij schermdelen.
    class Loupe : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MAGTRANSFORM { [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)] public float[] v; }

        [DllImport("Magnification.dll")] static extern bool MagInitialize();
        [DllImport("Magnification.dll")] static extern bool MagSetWindowSource(IntPtr hwnd, W.RECT rect);
        [DllImport("Magnification.dll")] static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM t);
        [DllImport("Magnification.dll")] static extern bool MagSetWindowFilterList(IntPtr hwnd, int mode, int count, IntPtr[] list);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int key, byte alpha, int flags);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, MS_SHOWMAGNIFIEDCURSOR = 0x1, LWA_ALPHA = 0x2, MW_FILTERMODE_EXCLUDE = 0;

        static bool inited, initOk;
        public static bool Available
        {
            get
            {
                if (!inited) { inited = true; try { initOk = MagInitialize(); } catch (Exception ex) { initOk = false; Log.Error("Vergrootglas starten", ex); } }
                return initOk;
            }
        }

        readonly Overlay ov;
        readonly bool follow;
        IntPtr mag = IntPtr.Zero;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        Point source;              // midden van wat je vergroot (vast bij een vastgezet glas)
        float zoom;
        const int Border = 3;
        float sc = 1;
        W.RECT lastSrc;

        // Alle glazen en hun balkjes: elk glas laat die weg bij het vergroten (anders zie je een tunnel)
        static readonly System.Collections.Generic.List<IntPtr> skip = new System.Collections.Generic.List<IntPtr>();
        static readonly System.Collections.Generic.List<Loupe> all = new System.Collections.Generic.List<Loupe>();

        static void RefreshFilters()
        {
            IntPtr[] list = skip.ToArray();
            foreach (Loupe l in all)
                if (l.mag != IntPtr.Zero && list.Length > 0)
                    try { if (!MagSetWindowFilterList(l.mag, MW_FILTERMODE_EXCLUDE, list.Length, list)) Log.Info("Vergrootglas: uitsluiten mislukt"); } catch { }
        }

        public static void Skip(IntPtr h, bool add) { if (add) { if (!skip.Contains(h)) skip.Add(h); } else skip.Remove(h); RefreshFilters(); }

        public Loupe(Overlay o, bool following, Point at)
        {
            ov = o; follow = following; source = at;
            zoom = Math.Max(1.5f, Math.Min(6f, o.Cfg.LoupeZoom));
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Text = "NexusPen vergrootglas";
            BackColor = o.Theme.Accent;   // de rand in de accentkleur
            Owner = o;                    // altijd boven de tekenlaag
            sc = Math.Max(1f, W.UiScaleAt(at));
            Size sz = o.Cfg.LoupeShape == 1 ? new Size((int)(260 * sc), (int)(260 * sc)) : new Size((int)(440 * sc), (int)(200 * sc));
            Size = sz;
            int x0 = at.X - sz.Width / 2, y0 = at.Y - sz.Height / 2;
            if (!following)
            {
                // vastgezet: glas plus balkje eronder moeten op het scherm passen
                Rectangle wa = Screen.FromPoint(at).WorkingArea;
                int stripH = (int)(40 * sc);
                x0 = Math.Max(wa.Left, Math.Min(wa.Right - sz.Width, x0));
                y0 = Math.Max(wa.Top, Math.Min(wa.Bottom - sz.Height - stripH, y0));
            }
            Location = new Point(x0, y0);
            tick.Interval = following ? 16 : 40;   // vastgezet hoeft minder vaak: alleen nieuwe inkt eronder
            tick.Tick += delegate { Step(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= W.WS_EX_LAYERED | W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST | W.WS_EX_NOACTIVATE;
                if (follow) cp.ExStyle |= W.WS_EX_TRANSPARENT;   // meegaand: je klikt er gewoon doorheen
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
                ApplyShape();
                mag = CreateWindowEx(0, "Magnifier", "NexusPenMagnifier", WS_CHILD | WS_VISIBLE | (follow ? MS_SHOWMAGNIFIEDCURSOR : 0),
                    Border, Border, Width - Border * 2, Height - Border * 2, Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                if (mag == IntPtr.Zero) { Log.Info("Vergrootglas: geen Magnifier-venster"); return; }
                if (ov.Cfg.LoupeShape == 1)
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        // rond: ook het beeld zelf rond, zodat de rand overal even dik is
                        gp.AddEllipse(0, 0, Width - Border * 2, Height - Border * 2);
                        using (Region rg = new Region(gp)) using (Graphics g = CreateGraphics()) SetWindowRgn(mag, rg.GetHrgn(g), true);
                    }
                all.Add(this);
                Skip(Handle, true);   // het glas zelf (en de andere glazen) niet vergroten
                ApplyZoom();
                tick.Start();
            }
            catch (Exception ex) { Log.Error("Vergrootglas", ex); }
        }

        void ApplyShape()
        {
            using (GraphicsPath gp = ov.Cfg.LoupeShape == 1 ? Ellipse() : Svg.RoundRect(new RectangleF(0, 0, Width, Height), 16 * sc))
                Region = new Region(gp);
        }

        GraphicsPath Ellipse() { GraphicsPath gp = new GraphicsPath(); gp.AddEllipse(0, 0, Width, Height); return gp; }

        void ApplyZoom()
        {
            if (mag == IntPtr.Zero) return;
            MAGTRANSFORM t = new MAGTRANSFORM();
            t.v = new float[] { zoom, 0, 0, 0, zoom, 0, 0, 0, 1 };
            MagSetWindowTransform(mag, ref t);
        }

        public void ChangeZoom(int dir)
        {
            zoom = Math.Max(1.5f, Math.Min(6f, zoom + (dir > 0 ? 0.5f : -0.5f)));
            ov.Cfg.LoupeZoom = zoom;
            ApplyZoom();
        }

        public float Zoom { get { return zoom; } }

        void Step()
        {
            if (mag == IntPtr.Zero || !Visible) return;
            if (follow)
            {
                Point m = Control.MousePosition;
                if (m != source) { source = m; Location = new Point(m.X - Width / 2, m.Y - Height / 2); }
            }
            int iw = Width - Border * 2, ih = Height - Border * 2;
            int sw = (int)(iw / zoom), sh = (int)(ih / zoom);
            Rectangle vs = Screen.FromPoint(source).Bounds;   // binnen het scherm waar je bent (geen zwarte stukken tussen schermen)
            int x = Math.Max(vs.Left, Math.Min(vs.Right - sw, source.X - sw / 2));
            int y = Math.Max(vs.Top, Math.Min(vs.Bottom - sh, source.Y - sh / 2));
            W.RECT r = new W.RECT(); r.Left = x; r.Top = y; r.Right = x + sw; r.Bottom = y + sh;
            try
            {
                if (r.Left != lastSrc.Left || r.Top != lastSrc.Top || r.Right != lastSrc.Right || r.Bottom != lastSrc.Bottom) { MagSetWindowSource(mag, r); lastSrc = r; }
                InvalidateRect(mag, IntPtr.Zero, true);   // ook als er niets verschuift: nieuwe inkt eronder laten zien
            }
            catch { }
        }

        // ---- vastgezet glas: een klein balkje eronder om te verslepen, in of uit te zoomen en te sluiten
        LoupeBar strip;
        public LoupeBar Strip { get { return strip; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (follow) return;
            strip = new LoupeBar(ov, this);
            strip.Place();
            strip.Show();
        }

        // het balkje is versleept: het glas gaat mee
        public void FollowStrip(Point stripLoc, Size stripSize)
        {
            Location = new Point(stripLoc.X + stripSize.Width / 2 - Width / 2, stripLoc.Y - Height - 4);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }   // maat zelf houden (vorm en beeld blijven kloppen)
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop(); tick.Dispose();
            all.Remove(this);
            if (IsHandleCreated) Skip(Handle, false);
            if (strip != null && !strip.IsDisposed) strip.Close();
            ov.LoupeClosed(this);
            base.OnFormClosed(e);
        }
    }

    // Balkje onder een vastgezet vergrootglas: slepen = verplaatsen, min en plus = zoom, kruisje = weg
    class LoupeBar : BarForm
    {
        readonly Loupe glass;
        bool placing;

        public LoupeBar(Overlay o, Loupe g) : base(o)
        {
            glass = g; draggable = true; Text = "NexusPen vergrootglas balk";
            Owner = g;
            S = Math.Max(1f, W.UiScaleAt(g.Location));
            LocationChanged += delegate { if (!placing && Visible) glass.FollowStrip(Location, Size); };
        }

        protected override bool AlwaysShared { get { return true; } }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Loupe.Skip(Handle, true); }
        protected override void OnFormClosed(FormClosedEventArgs e) { Loupe.Skip(Handle, false); base.OnFormClosed(e); }

        public void Place()
        {
            Rebuild();
            placing = true;
            Location = new Point(glass.Left + glass.Width / 2 - Width / 2, glass.Bottom + 4);
            placing = false;
        }

        protected override void Build()
        {
            float pad = 4 * S, b = 28 * S;
            float x = pad;
            Item grip = Add(new RectangleF(x, pad, b, b), L.T("Sleep om het vergrootglas te verplaatsen"), null);
            grip.Paint = delegate(Graphics g, Item it, bool h) { Icons.Draw(g, "verplaats", it.R, 15 * S, T.Muted, 2f); };
            x += b;
            Item minus = Add(new RectangleF(x, pad, b, b), L.T("Minder groot"), delegate { glass.ChangeZoom(-1); Invalidate(); });
            minus.Icon = "min";
            x += b;
            Item zoomLabel = Add(new RectangleF(x, pad, b + 8 * S, b), null, null);
            zoomLabel.Paint = delegate(Graphics g, Item it, bool h) { Label(g, glass.Zoom.ToString("0.#") + "x", it.R, T.Text, 12, true, StringAlignment.Center); };
            x += b + 8 * S;
            Item plus = Add(new RectangleF(x, pad, b, b), L.T("Groter"), delegate { glass.ChangeZoom(1); Invalidate(); });
            plus.Icon = "plus";
            x += b;
            Item close = Add(new RectangleF(x, pad, b, b), L.T("Vergrootglas weghalen"), delegate { glass.Close(); });
            close.Icon = "kruis";
            x += b + pad;
            ClientSize = new Size((int)Math.Ceiling(x), (int)Math.Ceiling(b + pad * 2));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // het sleepicoontje is geen knop: daar begint het slepen
            Item it = ItemAt(e.Location);
            if (it != null && it.Click == null && e.Button == MouseButtons.Left)
            {
                W.ReleaseCapture();
                W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
                return;
            }
            base.OnMouseDown(e);
        }
    }
}
