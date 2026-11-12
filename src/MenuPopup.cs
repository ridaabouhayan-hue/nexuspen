using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ Rechtsklikmenu, helemaal zelf getekend
    // Zelfde stijl als de andere venstertjes van de balk: overal dezelfde ruimte rondom, een afgerond vlakje
    // onder de muis, een rustig vinkje. Sluit bij een klik ergens anders of met Esc.
    class MenuPopup : BarForm
    {
        class Entry { public string Text, Hint, Font; public Action Click; public bool Check, HasCheck, Sep; }

        readonly List<Entry> entries = new List<Entry>();
        readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer();
        static MenuPopup open;
        bool armed;   // pas sluiten op een klik die na het openen begon

        public MenuPopup(Overlay o) : base(o)
        {
            Text = "NexusPen menu";
            Owner = o;   // altijd boven de tekenlaag
            watch.Interval = 20;   // ook een korte tik met pen of vinger buiten het menu zien
            watch.Tick += delegate { Watch(); };
        }

        public void Add(string text, Action click) { Entry e = new Entry(); e.Text = text; e.Click = click; entries.Add(e); }
        public void AddCheck(string text, bool on, Action click) { Entry e = new Entry(); e.Text = text; e.Click = click; e.HasCheck = true; e.Check = on; entries.Add(e); }
        // met de sneltoets rechts in de regel, zoals in Windows (Kopiëren   Ctrl+C); click null = grijs
        public void Add(string text, string hint, Action click) { Entry e = new Entry(); e.Text = text; e.Hint = hint; e.Click = click; entries.Add(e); }
        // een keuze die in zijn eigen lettertype staat (zo zie je hoe het eruitziet)
        public void AddCheckFont(string text, string font, bool on, Action click) { AddCheck(text, on, click); entries[entries.Count - 1].Font = font; }

        public void AddSeparator() { Entry e = new Entry(); e.Sep = true; entries.Add(e); }

        // Maten (in pixels bij 100%): rand 6, regel 36, tekst 12 van het vlakje, scheidingslijn 9
        const float Pad = 6, RowH = 36, TextPad = 12, SepH = 9, CheckW = 22;

        protected override void Build()
        {
            bool checks = false;
            foreach (Entry e in entries) if (e.HasCheck) checks = true;
            float textX = Pad + TextPad + (checks ? CheckW : 0);
            float maxW = 0;
            float hintW = 0;
            foreach (Entry e in entries) if (!e.Sep) maxW = Math.Max(maxW, e.Font != null ? TextWidthFont(e.Text, e.Font, 15) : TextWidthPlain(e.Text, 14));
            foreach (Entry e in entries) if (!e.Sep && e.Hint != null) hintW = Math.Max(hintW, TextWidthPlain(e.Hint, 13));
            if (hintW > 0) maxW += hintW + 28 * S;
            float w = Math.Max(180 * S, textX * S + maxW + (TextPad + Pad + 6) * S);
            float y = Pad * S;
            foreach (Entry en in entries)
            {
                Entry e = en;
                if (e.Sep) { Item sep = Add(new RectangleF(0, y, w, SepH * S), null, null); sep.Paint = PaintSep; y += SepH * S; continue; }
                RectangleF r = new RectangleF(Pad * S, y, w - Pad * 2 * S, RowH * S);
                Item it = Add(r, null, e.Click == null ? (Action)null : delegate { Choose(e); });
                float tx = textX * S;
                it.Paint = delegate(Graphics g, Item item, bool hv)
                {
                    Theme t = T;
                    // de achtergrond van het menu is helemaal dicht: dan kan de scherpe ClearType-tekst van Windows (zoals in Verkenner)
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    if (hv && e.Click != null) using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(item.R, 7 * S)) g.FillPath(b, gp);
                    if (e.HasCheck && e.Check)
                    {
                        float cs = 16 * S;
                        Icons.Draw(g, "vink", new RectangleF(item.R.X + TextPad * S - 2 * S, item.R.Y + (item.R.Height - cs) / 2, cs, cs), cs, t.Accent, 2.2f);
                    }
                    if (e.Font != null)
                    {
                        using (Font f = Fonts.Make(e.Font, 15 * S, FontStyle.Regular))
                        using (SolidBrush b = new SolidBrush(t.Text))
                        using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
                        {
                            sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
                            g.DrawString(e.Text, f, b, new RectangleF(tx, item.R.Y, item.R.Right - tx - TextPad * S, item.R.Height), sf);
                        }
                    }
                    else Label(g, e.Text, new RectangleF(tx, item.R.Y, item.R.Right - tx - TextPad * S, item.R.Height), e.Click != null ? t.Text : t.Muted, 14, false, StringAlignment.Near);
                    if (e.Hint != null) Label(g, e.Hint, new RectangleF(tx, item.R.Y, item.R.Right - tx - TextPad * S, item.R.Height), t.Muted, 13, false, StringAlignment.Far);
                };
                y += RowH * S;
            }
            y += Pad * S;
            ClientSize = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(y));
        }

        void PaintSep(Graphics g, Item it, bool hv)
        {
            float one = Math.Max(1, Px(S));
            using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, Px(it.R.X + (Pad + 8) * S), Px(it.R.Y + it.R.Height / 2), Px(it.R.Width - (Pad + 8) * 2 * S), one);
        }

        float TextWidthFont(string text, string font, float px)
        {
            using (Font f = Fonts.Make(font, px * S, FontStyle.Regular))
            using (Graphics g = CreateGraphics()) return g.MeasureString(text ?? "", f).Width + 4 * S;
        }

        float TextWidthPlain(string text, float px)
        {
            using (Font f = UiFont.Make(px * S, false))
            using (Graphics g = CreateGraphics()) return g.MeasureString(text ?? "", f).Width;
        }

        void Choose(Entry e)
        {
            Action a = e.Click;
            Close();
            if (a != null) try { ov.BeginInvoke(a); } catch { }
        }

        // Openen bij dit punt op het scherm (passend binnen het scherm; zo nodig erboven of links ervan)
        public void ShowAt(Point at)
        {
            if (open != null && open != this && !open.IsDisposed) open.Close();
            open = this;
            S = Math.Max(1f, W.UiScaleAt(at));
            Rebuild();
            Rectangle wa = Screen.FromPoint(at).WorkingArea;
            int x = at.X, y = at.Y;
            if (x + Width > wa.Right) x = Math.Max(wa.Left, at.X - Width);
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, at.Y - Height);
            Location = new Point(x, y);
            armed = false;
            if (Anchor.IsEmpty) Anchor = new Rectangle(Control.MousePosition, Size.Empty);   // geen knop: dan het punt waar je klikte
            Show();
            watch.Start();
        }

        public static void CloseOpen() { if (open != null && !open.IsDisposed) open.Close(); }
        public static DateTime ClosedAt = DateTime.MinValue;   // net dicht (bijv. door Esc): dan is die Esc niet ook voor de tekst
        public static bool IsOpen { get { return open != null && !open.IsDisposed && open.Visible; } }
        public static MenuPopup Current { get { return IsOpen ? open : null; } }

        void Watch()
        {
            // & 1: er is sinds de vorige keer geklikt (ook een korte tik met pen of touchpad)
            short l = W.GetAsyncKeyState(0x01), r = W.GetAsyncKeyState(0x02);
            bool down = (l & 0x8000) != 0 || (r & 0x8000) != 0, tapped = (l & 1) != 0 || (r & 1) != 0;
            if (!down && !tapped) { armed = true; }
            else if (armed && !Bounds.Contains(Control.MousePosition)) { Close(); return; }
            else if (!down) armed = true;
            if ((W.GetAsyncKeyState(W.VK_ESCAPE) & 0x8000) != 0) { Close(); return; }
            // Muis weg van het menu (en van de knop waar het bij hoort)? Dan na ruim een halve seconde vanzelf dicht.
            // Niet meteen: zo kun je rustig schuin naar een regel bewegen, of even over de rand schieten.
            Point m = Control.MousePosition;
            Rectangle near = Rectangle.Inflate(Bounds, (int)(16 * S), (int)(16 * S));
            bool over = near.Contains(m) || (!Anchor.IsEmpty && Rectangle.Inflate(Anchor, (int)(6 * S), (int)(6 * S)).Contains(m));
            if (over || down) awaySince = -1;
            else if (awaySince < 0) awaySince = Environment.TickCount;
            else if (Environment.TickCount - awaySince >= 400) Close();
        }
        int awaySince = -1;
        public Rectangle Anchor;   // de knop waar het menu bij hoort (op het scherm): daar mag de muis ook zijn

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = Cursors.Default;   // een menu, geen knoppen: gewone pijl
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            watch.Stop(); watch.Dispose();
            if (open == this) open = null;
            ClosedAt = DateTime.Now;
            base.OnFormClosed(e);   // Close() ruimt het venster zelf op
        }
    }
}
