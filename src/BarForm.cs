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
    // ------------------------------------------------------------------ Basis voor de werkbalk en de kleine vensters ernaast
    class Item
    {
        public RectangleF R;
        public string Tip, Icon, Key;
        public Action Click, RightClick;
        public Func<bool> Active, Lit;
        public Action<Graphics, Item, bool> Paint;   // eigen tekenwerk (kleurbolletje, logo, schuifje)
        public bool Muted;
        public string Name;               // korte naam, in kleine venstertjes onderaan te zien als je erover gaat
        public Func<bool> On;             // staat deze keuze aan (voor die naam als je nergens over gaat)
    }

    class BarForm : Form
    {
        protected Overlay ov;
        protected List<Item> items = new List<Item>();
        protected Item hover;
        protected ToolTip tip = new ToolTip();
        public float S = 1;
        protected virtual float ExtraScale { get { return 1f; } }
        protected virtual void AfterDpiChange() { }   // de werkbalk: je eigen grootte erbij
        protected bool draggable;
        protected Point moveStart, moveMouse;
        protected bool vertical;

        public BarForm(Overlay o)
        {
            ov = o;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            tip.InitialDelay = 350;
        }

        protected Theme T { get { return ov.Theme; } }
        protected float Radius { get { return (ov.Cfg.Square ? 6 : 14) * S; } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED: we tekenen zelf het hele venster met zachte randen (geen gekartelde hoekjes)
                cp.ExStyle |= W.WS_EX_NOACTIVATE | W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST | W.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(W.TABLET_FLAGS));
            W.HideFromCapture(Handle, ov.Cfg.HideFromCapture && !AlwaysShared);
            RenderNow();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W.WM_TABLET_QUERYSYSTEMGESTURESTATUS) { m.Result = new IntPtr(W.TABLET_FLAGS); return; }
            if (m.Msg == 0x0231) { moveStart = Location; moveMouse = Control.MousePosition; }   // WM_ENTERSIZEMOVE: slepen begint
            if (m.Msg == 0x0216)   // WM_MOVING
            {
                // Positie rekenen vanaf waar je begon te slepen (anders blijft het venster aan de rand plakken),
                // dicht bij de rand ertegenaan klikken, en nooit buiten het scherm
                W.RECT r = (W.RECT)Marshal.PtrToStructure(m.LParam, typeof(W.RECT));
                Point mouse = Control.MousePosition;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                int x = moveStart.X + mouse.X - moveMouse.X, y = moveStart.Y + mouse.Y - moveMouse.Y;
                Rectangle wa = Screen.FromPoint(mouse).WorkingArea;
                int th = (int)(12 * S);
                if (Math.Abs(x - wa.Left) < th) x = wa.Left; else if (Math.Abs(wa.Right - (x + w)) < th) x = wa.Right - w;
                if (Math.Abs(y - wa.Top) < th) y = wa.Top; else if (Math.Abs(wa.Bottom - (y + h)) < th) y = wa.Bottom - h;
                x = Math.Max(wa.Left, Math.Min(wa.Right - w, x));
                y = Math.Max(wa.Top, Math.Min(wa.Bottom - h, y));
                r.Left = x; r.Top = y; r.Right = x + w; r.Bottom = y + h;
                Marshal.StructureToPtr(r, m.LParam, false);
                m.Result = new IntPtr(1);
                return;
            }
            if (m.Msg == W.WM_DPICHANGED)
            {
                // Naar een scherm met andere schaal gesleept: opnieuw opbouwen op de juiste grootte
                int dpi = m.WParam.ToInt32() & 0xFFFF;
                W.RECT r = (W.RECT)Marshal.PtrToStructure(m.LParam, typeof(W.RECT));
                float d = Math.Max(1f, dpi / 96f);
                S = d * W.ScreenFactorAt(new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2), d) * ExtraScale;
                Location = new Point(r.Left, r.Top);
                Rebuild();
                AfterDpiChange();
                // tijdens het slepen naar een scherm met een andere schaal: opnieuw meten vanaf hier
                moveStart = Location; moveMouse = Control.MousePosition;
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void ApplyCaptureSetting() { if (IsHandleCreated) W.HideFromCapture(Handle, ov.Cfg.HideFromCapture && !AlwaysShared); }

        // Vensters die je cursisten juist moeten zien (de timer): nooit verbergen bij schermdelen
        protected virtual bool AlwaysShared { get { return false; } }

        // ---- zelf tekenen met UpdateLayeredWindow: zachte ronde hoeken, scherpe rand, en direct zichtbaar na een klik
        byte alpha = 255;
        bool renderQueued;
        protected bool building;
        // De grootte die Build heeft uitgerekend. Windows kan het venster even anders maken (bij het aanmaken of
        // bij een ander scherm); dan tekenen we toch op deze grootte en zetten het venster weer goed.
        Size wantSize = Size.Empty;
        Bitmap maskCache; Size maskSize; float maskRadius; int maskKind;

        // Doorzichtigheid van het hele venster (100%, 90% of 80%)
        public byte BarAlpha { get { return alpha; } set { if (alpha != value) { alpha = value; RenderNow(); } } }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            base.OnInvalidated(e);
            if (renderQueued || !IsHandleCreated || IsDisposed) return;
            renderQueued = true;
            try { BeginInvoke((MethodInvoker)RenderQueued); } catch { renderQueued = false; }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) return;
            RenderNow();
            // nog een keer kort daarna: soms zet Windows bij het tonen de grootte of de schaal nog even anders
            System.Windows.Forms.Timer again = new System.Windows.Forms.Timer();
            again.Interval = 250;
            again.Tick += delegate { again.Stop(); again.Dispose(); if (!IsDisposed && Visible) RenderNow(); };
            again.Start();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RenderNow();
        }

        // De vorm van het venster. Standaard een afgeronde rechthoek.
        protected virtual GraphicsPath ShapePath(float w, float h) { return Svg.RoundRect(new RectangleF(0, 0, w, h), Radius); }
        protected virtual int ShapeKind { get { return 0; } }

        // Is er intussen al getekend (bijvoorbeeld direct na een klik), dan hoeft het niet nog een keer
        void RenderQueued() { if (renderQueued) RenderNow(); }

        // Meteen tekenen en aan Windows geven (niet wachten op de volgende verversing)
        public void RenderNow()
        {
            renderQueued = false;
            if (!IsHandleCreated || IsDisposed || building) return;
            Size want = wantSize.IsEmpty ? ClientSize : wantSize;
            int w = want.Width, h = want.Height;
            if (w <= 0 || h <= 0) return;
            if (ClientSize != want)
            {
                building = true;
                try { ClientSize = want; } finally { building = false; }
                // kan intussen naar een ander scherm met een andere schaal zijn gegaan: dan opnieuw de goede maat nemen
                want = wantSize.IsEmpty ? ClientSize : wantSize;
                w = want.Width; h = want.Height;
                if (w <= 0 || h <= 0) return;
            }
            try
            {
                using (Bitmap b = RenderBitmap())
                {
                    IntPtr screen = W.GetDC(IntPtr.Zero);
                    IntPtr mem = W.CreateCompatibleDC(screen);
                    IntPtr hb = IntPtr.Zero, old = IntPtr.Zero;
                    try
                    {
                        hb = b.GetHbitmap(Color.FromArgb(0));
                        old = W.SelectObject(mem, hb);
                        W.SIZE size = new W.SIZE(w, h);
                        W.POINT src = new W.POINT(0, 0);
                        W.BLENDFUNCTION bf = new W.BLENDFUNCTION();
                        bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = alpha; bf.AlphaFormat = 1;
                        W.UpdateLayeredWindowSize(Handle, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref bf, W.ULW_ALPHA);
                    }
                    finally
                    {
                        if (old != IntPtr.Zero) W.SelectObject(mem, old);
                        if (hb != IntPtr.Zero) W.DeleteObject(hb);
                        W.DeleteDC(mem);
                        W.ReleaseDC(IntPtr.Zero, screen);
                    }
                }
            }
            catch (Exception ex) { Log.Error("Venster tekenen", ex); }
        }

        // Het hele venster als plaatje (ook voor de zelftest)
        public Bitmap RenderBitmap()
        {
            Size want = wantSize.IsEmpty ? ClientSize : wantSize;
            int w = Math.Max(1, want.Width), h = Math.Max(1, want.Height);
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b)) PaintAll(g);
            ApplyMask(b);
            return b;
        }

        // Alles buiten de afgeronde vorm doorzichtig maken, met een zachte rand
        void ApplyMask(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            float rad = Radius;
            if (maskCache == null || maskSize != b.Size || maskRadius != rad || maskKind != ShapeKind)
            {
                if (maskCache != null) maskCache.Dispose();
                maskCache = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(maskCache))
                using (GraphicsPath gp = ShapePath(w, h))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.FillPath(Brushes.White, gp);
                }
                maskSize = b.Size; maskRadius = rad; maskKind = ShapeKind;
            }
            Rectangle all = new Rectangle(0, 0, w, h);
            BitmapData bd = b.LockBits(all, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            BitmapData md = maskCache.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int n = w;
                int[] row = new int[n], mrow = new int[n];
                for (int y = 0; y < h; y++)
                {
                    IntPtr pr = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                    IntPtr mr = new IntPtr(md.Scan0.ToInt64() + (long)y * md.Stride);
                    Marshal.Copy(pr, row, 0, n);
                    Marshal.Copy(mr, mrow, 0, n);
                    bool changed = false;
                    for (int x = 0; x < n; x++)
                    {
                        int m = (mrow[x] >> 24) & 0xFF;
                        if (m == 255) continue;
                        changed = true;
                        if (m == 0) { row[x] = 0; continue; }
                        int c = row[x];
                        int a = ((c >> 24) & 0xFF) * m / 255, r = ((c >> 16) & 0xFF) * m / 255, gg = ((c >> 8) & 0xFF) * m / 255, bb = (c & 0xFF) * m / 255;
                        row[x] = (a << 24) | (r << 16) | (gg << 8) | bb;
                    }
                    if (changed) Marshal.Copy(row, 0, pr, n);
                }
            }
            finally { b.UnlockBits(bd); maskCache.UnlockBits(md); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && maskCache != null) { maskCache.Dispose(); maskCache = null; }
            base.Dispose(disposing);
        }

        // Op hele pixels zetten: dan blijven randen en icoontjes scherp
        public static float Px(float v) { return (float)Math.Round(v); }
        public static RectangleF Px(RectangleF r)
        {
            return RectangleF.FromLTRB(Px(r.Left), Px(r.Top), Px(r.Right), Px(r.Bottom));
        }

        protected virtual void Build() { }

        public virtual void Rebuild()
        {
            hover = null;
            items.Clear();
            building = true;
            try { Build(); } finally { building = false; }
            wantSize = ClientSize;
            RenderNow();
        }

        protected Item Add(RectangleF r, string tipText, Action click)
        {
            Item it = new Item();
            it.R = Px(r); it.Tip = tipText; it.Click = click;
            items.Add(it);
            return it;
        }

        protected Item Button(RectangleF r, string icon, string tipText, Action click, Func<bool> active, Func<bool> lit)
        {
            Item it = Add(r, tipText, click);
            it.Icon = icon; it.Active = active; it.Lit = lit;
            return it;
        }

        protected float IconSize
        {
            get { int i = ov.Cfg.SizeIndex; return (i == 0 ? 18 : i == 2 ? 24 : 20) * S; }
        }

        protected float StrokeW { get { return T.Thick ? 2.4f : 2f; } }

        // Windows tekent dit venster niet zelf (UpdateLayeredWindow), dus hier niets dubbel doen
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected void PaintAll(Graphics g)
        {
            Theme t = T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(t.Bar);
            // rand van een hele pixel breed, precies op de pixels: scherp in plaats van wazig
            float bw = Math.Max(1, Px((t.Thick ? 2 : 1) * S));
            Size sz = wantSize.IsEmpty ? ClientSize : wantSize;
            using (Pen p = new Pen(t.Border, bw))
            using (GraphicsPath gp = BorderPath(sz.Width, sz.Height, bw))
                g.DrawPath(p, gp);
            PaintExtra(g);
            foreach (Item it in items) PaintItem(g, it);
            PaintOverlay(g);
        }

        protected virtual GraphicsPath BorderPath(float w, float h, float bw)
        {
            return Svg.RoundRect(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Radius - bw / 2);
        }

        protected virtual void PaintOverlay(Graphics g) { }

        protected virtual void PaintExtra(Graphics g) { }

        // Het vakje achter een knop (aan of eronder met de muis): iets kleiner dan de knop zelf, dan oogt de balk rustiger
        protected RectangleF Highlight(RectangleF r)
        {
            float d = Px(Math.Max(1, r.Width - (IconSize + 12 * S)) / 2);
            float dy = Px(Math.Max(1, r.Height - (IconSize + 12 * S)) / 2);
            return RectangleF.Inflate(r, -d, -dy);
        }

        protected void PaintItem(Graphics g, Item it)
        {
            Theme t = T;
            bool active = it.Active != null && it.Active();
            bool hov = it == hover;
            if (it.Paint != null) { it.Paint(g, it, hov); return; }
            bool quiet = ov.Cfg.ActiveStyle == 0 && !t.Thick;   // rustig: ingedrukt in plaats van gekleurd (niet bij Hoog contrast)
            if (active || hov)
                using (SolidBrush b = new SolidBrush(active ? (quiet ? t.Pressed : t.Accent) : t.Hover))
                using (GraphicsPath gp = Svg.RoundRect(Highlight(it.R), 7 * S))
                {
                    g.FillPath(b, gp);
                    if (active && quiet) using (Pen p = new Pen(t.PressedEdge, Math.Max(1, Px(S)))) g.DrawPath(p, gp);
                }
            Color c = active ? (quiet ? t.Text : t.OnAccent) : (it.Lit != null && it.Lit()) ? t.Accent : it.Muted ? t.Muted : t.Icon;
            if (it.Icon != null) Icons.Draw(g, it.Icon, it.R, IconSize, c, StrokeW);
        }

        protected Item ItemAt(Point p)
        {
            for (int i = items.Count - 1; i >= 0; i--) if (items[i].R.Contains(p)) return items[i];
            return null;
        }

        protected virtual bool OnPress(MouseEventArgs e) { return false; }
        // Zelf slepen (de werkbalk): true = geregeld, anders sleept Windows het venster
        protected virtual bool BeginCustomDrag(MouseEventArgs e) { return false; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            double t0 = Perf.Now;
            if (OnPress(e)) return;
            Item it = ItemAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (it != null && it.RightClick != null) { it.RightClick(); if (!IsDisposed) RenderNow(); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (it != null)
            {
                if (it.Click != null) it.Click();
                if (!IsDisposed) RenderNow();   // knop meteen oranje, in hetzelfde beeldje
                Perf.Click.Add(Perf.Now - t0);
                return;
            }
            if (draggable && !BeginCustomDrag(e))
            {
                W.ReleaseCapture();
                W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item it = ItemAt(e.Location);
            if (it != hover)
            {
                hover = it;
                RenderNow();
                if (it != null && !string.IsNullOrEmpty(it.Tip) && ov.Cfg.ShowTips)
                {
                    Point at = vertical
                        ? new Point((int)(it.R.Right + 10 * S), (int)(it.R.Y + 6 * S))
                        : new Point((int)it.R.X, (int)(it.R.Bottom + 8 * S));
                    tip.Show(it.Tip, this, at, 3000);
                }
                else tip.Hide(this);
            }
            Cursor = it != null ? Cursors.Hand : draggable ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ov.Wheel(e.Delta);
        }

        // Kleurbolletje met witte ring als het gekozen is
        protected void Swatch(Graphics g, RectangleF r, Color c, bool chosen, float d)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            Theme t = T;
            if (chosen)
                using (SolidBrush b = new SolidBrush(t.IsLight ? Color.Black : Color.White))
                    g.FillEllipse(b, cx - d / 2 - 3.5f * S, cy - d / 2 - 3.5f * S, d + 7 * S, d + 7 * S);
            if (chosen)
                using (SolidBrush b = new SolidBrush(t.Bar))
                    g.FillEllipse(b, cx - d / 2 - 2 * S, cy - d / 2 - 2 * S, d + 4 * S, d + 4 * S);
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            if (Theme.Luma(c) > 0.9 || Theme.Luma(c) < 0.12)
                using (Pen p = new Pen(t.IsLight ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(90, 255, 255, 255), 1)) g.DrawEllipse(p, cx - d / 2, cy - d / 2, d, d);
        }

        protected void Label(Graphics g, string text, RectangleF r, Color c, float px, bool bold, StringAlignment align)
        {
            using (Font f = UiFont.Make(px * S, bold))
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                if (L.RightToLeft) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;   // Arabisch
                g.DrawString(text, f, b, r, sf);
            }
        }

        // Knopje met tekst (en eventueel een icoon), aan of uit
        protected void PaintChip(Graphics g, RectangleF r, bool on, bool hov, string text, string icon, Color iconColor)
        {
            Theme t = T;
            Color bg = on ? (t.IsLight ? Color.White : ColorTranslator.FromHtml("#3F3F46")) : hov ? (t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#34343A")) : t.Chip;
            using (SolidBrush b = new SolidBrush(bg)) using (GraphicsPath gp = Svg.RoundRect(r, 8 * S)) g.FillPath(b, gp);
            if (on) using (Pen p = new Pen(t.Accent, 1.5f * S)) using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(r, -0.75f * S, -0.75f * S), 8 * S)) g.DrawPath(p, gp);
            float x = r.X + 10 * S;
            if (icon != null)
            {
                float isz = 16 * S;
                RectangleF ir = string.IsNullOrEmpty(text) ? r : new RectangleF(x, r.Y, isz, r.Height);
                Icons.Draw(g, icon, ir, isz, iconColor.IsEmpty ? t.Icon : iconColor, 2f);
                x += isz + 6 * S;
            }
            if (!string.IsNullOrEmpty(text))
            {
                RectangleF tr = icon == null ? r : new RectangleF(x, r.Y, r.Right - x - 6 * S, r.Height);
                Label(g, text, tr, on ? t.Text : (t.IsLight ? ColorTranslator.FromHtml("#3F3F46") : ColorTranslator.FromHtml("#E4E4E7")), 13, true, icon == null ? StringAlignment.Center : StringAlignment.Near);
            }
        }

        protected float TextWidth(string text, float px)
        {
            using (Font f = UiFont.Make(px * S, true))
            using (Graphics g = CreateGraphics()) return g.MeasureString(text, f).Width;
        }

        // Klein venster naast de werkbalk plaatsen, binnen het scherm
        public void PlaceNear(Rectangle anchor, bool belowOrRight)
        {
            Rectangle wa = Screen.FromRectangle(anchor).WorkingArea;
            int gap = (int)(8 * S);
            int x, y;
            if (!vertical)
            {
                x = anchor.X + anchor.Width / 2 - Width / 2;
                y = anchor.Bottom + gap;
                if (y + Height > wa.Bottom) y = anchor.Top - gap - Height;
            }
            else
            {
                x = anchor.Right + gap;
                y = anchor.Y + anchor.Height / 2 - Height / 2;
                if (x + Width > wa.Right) x = anchor.Left - gap - Width;
            }
            x = Math.Max(wa.Left + 4, Math.Min(wa.Right - Width - 4, x));
            y = Math.Max(wa.Top + 4, Math.Min(wa.Bottom - Height - 4, y));
            Location = new Point(x, y);
        }

        public void SetVertical(bool v) { vertical = v; }
    }

    // Lettertype voor de knoppen en instellingen: Segoe UI, zoals in het ontwerp (Inter / Segoe UI).
    // Dat lettertype zit in Windows, dus Windows kan het zelf haarscherp tekenen (ClearType).
    static class UiFont
    {
        // Chinees staat niet in Segoe UI: dan het Chinese lettertype van Windows (Arabisch en Russisch zitten wel in Segoe UI)
        public static string Name { get { return L.Code == "zh" && Fonts.Has("Microsoft YaHei UI") ? "Microsoft YaHei UI" : "Segoe UI"; } }

        // Vet wordt Semibold: rustiger en minder dik, zoals in het ontwerp
        public static Font Make(float px, bool bold)
        {
            if (bold && Name == "Segoe UI" && Fonts.Has("Segoe UI Semibold")) return Fonts.Make("Segoe UI Semibold", px, FontStyle.Regular);
            return Fonts.Make(Name, px, bold ? FontStyle.Bold : FontStyle.Regular);
        }
    }
}
