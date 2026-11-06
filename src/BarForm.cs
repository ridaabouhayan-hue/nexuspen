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
    }

    class BarForm : Form
    {
        protected Overlay ov;
        protected List<Item> items = new List<Item>();
        protected Item hover;
        protected ToolTip tip = new ToolTip();
        public float S = 1;
        protected bool draggable;
        protected bool vertical;

        public BarForm(Overlay o)
        {
            ov = o;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
                cp.ExStyle |= W.WS_EX_NOACTIVATE | W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(W.TABLET_FLAGS));
            W.HideFromCapture(Handle, ov.Cfg.HideFromCapture);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W.WM_TABLET_QUERYSYSTEMGESTURESTATUS) { m.Result = new IntPtr(W.TABLET_FLAGS); return; }
            if (m.Msg == W.WM_DPICHANGED)
            {
                // Naar een scherm met andere schaal gesleept: opnieuw opbouwen op de juiste grootte
                int dpi = m.WParam.ToInt32() & 0xFFFF;
                S = Math.Max(1f, dpi / 96f);
                W.RECT r = (W.RECT)Marshal.PtrToStructure(m.LParam, typeof(W.RECT));
                Location = new Point(r.Left, r.Top);
                Rebuild();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void ApplyCaptureSetting() { if (IsHandleCreated) W.HideFromCapture(Handle, ov.Cfg.HideFromCapture); }

        protected virtual void Build() { }

        public virtual void Rebuild()
        {
            hover = null;
            items.Clear();
            Build();
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), Radius))
                Region = new Region(gp);
            Invalidate();
        }

        protected Item Add(RectangleF r, string tipText, Action click)
        {
            Item it = new Item();
            it.R = r; it.Tip = tipText; it.Click = click;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme t = T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(t.Bar);
            float bw = (t.Thick ? 2 : 1) * S;
            using (Pen p = new Pen(t.Border, bw))
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(bw / 2, bw / 2, ClientSize.Width - bw - 0.5f, ClientSize.Height - bw - 0.5f), Radius - bw / 2))
                g.DrawPath(p, gp);
            PaintExtra(g);
            foreach (Item it in items) PaintItem(g, it);
        }

        protected virtual void PaintExtra(Graphics g) { }

        protected void PaintItem(Graphics g, Item it)
        {
            Theme t = T;
            bool active = it.Active != null && it.Active();
            bool hov = it == hover;
            if (it.Paint != null) { it.Paint(g, it, hov); return; }
            if (active || hov)
                using (SolidBrush b = new SolidBrush(active ? t.Accent : t.Hover))
                using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S))
                    g.FillPath(b, gp);
            Color c = active ? t.OnAccent : (it.Lit != null && it.Lit()) ? t.Accent : it.Muted ? t.Muted : t.Icon;
            if (it.Icon != null) Icons.Draw(g, it.Icon, it.R, IconSize, c, StrokeW);
        }

        protected Item ItemAt(Point p)
        {
            for (int i = items.Count - 1; i >= 0; i--) if (items[i].R.Contains(p)) return items[i];
            return null;
        }

        protected virtual bool OnPress(MouseEventArgs e) { return false; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (OnPress(e)) return;
            Item it = ItemAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (it != null && it.RightClick != null) { it.RightClick(); Invalidate(); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (it != null) { if (it.Click != null) it.Click(); if (!IsDisposed) Invalidate(); return; }
            if (draggable)
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
                Invalidate();
                if (it != null && !string.IsNullOrEmpty(it.Tip))
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
            using (Font f = Fonts.Make(UiFont.Name, px * S, bold ? FontStyle.Bold : FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
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
            using (Font f = Fonts.Make(UiFont.Name, px * S, FontStyle.Bold))
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

    // Lettertype voor de knoppen en instellingen: Nunito (huisstijl) als het er is, anders Segoe UI
    static class UiFont
    {
        static string name;
        public static string Name
        {
            get
            {
                if (name == null) name = Render.IsInstalled("Nunito") ? "Nunito" : "Segoe UI";
                return name;
            }
        }
    }
}
