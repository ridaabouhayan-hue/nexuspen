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
    // ------------------------------------------------------------------ Kleur en dikte (pen-paneel)
    class ColorPopup : BarForm
    {
        RectangleF track;
        bool sliding;
        public const float MaxW = Settings.MaxWidth;

        public ColorPopup(Overlay o) : base(o) { Text = "NexusPen kleur"; }

        protected override void Build()
        {
            Overlay o = ov;
            tipFor.Clear();
            float pad = 14 * S, sw = 28 * S, gapX = 12 * S, rowGap = 12 * S;
            Color[] cols = o.Colors;
            int perRow = 8, rows = Math.Max(1, (cols.Length + perRow - 1) / perRow);
            gapX = 10 * S; rowGap = 10 * S;
            float w = pad * 2 + sw * perRow + gapX * (perRow - 1);
            float chipW = Math.Max(TextWidth(L.T("Krijtstreep"), 13), TextWidth(L.T("Vervagen"), 13) + 22 * S) + 30 * S;
            w = Math.Max(w, pad * 2 + 8 * S + chipW * 2);
            float y = pad;
            titleRow = new RectangleF(pad, y, w - pad * 2, 16 * S);
            y += 24 * S;
            // Fel / Zacht
            float segW = (w - pad * 2 - 6 * S) / 2;
            for (int i = 0; i < Palette.Names.Length; i++)
            {
                int idx = i;
                Item it = Add(new RectangleF(pad + 3 * S + i * segW, y + 3 * S, segW, 28 * S), i == 0 ? L.T("Felle kleuren") : L.T("Zachte kleuren"), delegate { o.SetPalette(idx); Invalidate(); });
                it.Paint = delegate(Graphics g, Item item, bool h)
                {
                    Theme t = T;
                    bool on = o.PaletteIndex == idx;
                    if (on)
                    {
                        using (SolidBrush b = new SolidBrush(t.IsLight ? Color.White : ColorTranslator.FromHtml("#3F3F46")))
                        using (GraphicsPath gp = Svg.RoundRect(item.R, 7 * S)) g.FillPath(b, gp);
                        using (Pen p = new Pen(t.Accent, 1.5f * S))
                        using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(item.R, -0.75f * S, -0.75f * S), 7 * S)) g.DrawPath(p, gp);
                    }
                    Label(g, L.T(Palette.Names[idx]), item.R, on ? t.Text : t.Muted, 12, true, StringAlignment.Center);
                };
            }
            segBox = new RectangleF(pad, y, w - pad * 2, 34 * S);
            y += 34 * S + 14 * S;
            for (int i = 0; i < cols.Length; i++)
            {
                int idx = i;
                Color col = cols[i];
                float gx = (w - pad * 2 - sw * perRow) / (perRow - 1);  // kleuren gelijk verdeeld over de breedte
                RectangleF r = new RectangleF(pad + (i % perRow) * (sw + gx), y + (i / perRow) * (sw + rowGap), sw, sw);
                Item it = Add(RectangleF.Inflate(r, 4 * S, 4 * S), "", delegate { o.SetColor(idx); Invalidate(); });
                it.Paint = delegate(Graphics g, Item item, bool h)
                {
                    RectangleF rr = RectangleF.Inflate(item.R, -4 * S, -4 * S);
                    Swatch(g, rr, col, o.ColorIndex == idx, sw * (h ? 1.06f : 1f));
                };
                it.Tip = null;
                tipFor[it] = idx;   // tipFor wordt aan het begin van Build leeggemaakt
            }
            y += sw * rows + rowGap * (rows - 1) + 16 * S;
            // dikte
            labelRow = new RectangleF(pad, y, w - pad * 2, 20 * S);
            track = new RectangleF(pad + 46 * S, y + 8 * S, w - pad * 2 - 46 * S - 34 * S, 4 * S);
            y += 20 * S + 14 * S;
            // krijtstreep en vervagen (voor de pen)
            float half = (w - pad * 2 - 8 * S) / 2;
            Item chalk = Add(new RectangleF(pad, y, half, 32 * S), L.T("Lijnen zien eruit als krijt op een bord"), delegate { o.Cfg.Chalk = !o.Cfg.Chalk; o.Cfg.Save(); Invalidate(); if (o.Bar != null) o.Bar.Refresh2(); });
            chalk.Paint = delegate(Graphics g, Item item, bool h) { PaintChip(g, item.R, o.Cfg.Chalk, h, L.T("Krijtstreep"), "krijt", o.Cfg.Chalk ? T.Text : T.Icon); };
            // vervagen: gewoon aan of uit; hoe lang het blijft staan kies je in Instellingen > Pen en kleuren
            Item fade = Add(new RectangleF(pad + half + 8 * S, y, half, 32 * S), L.F("Wat je tekent verdwijnt vanzelf na {0} seconden. De tijd kies je in Instellingen", o.Cfg.FadeSeconds), delegate
            {
                o.Cfg.Fade = !o.Cfg.Fade; o.Cfg.Save(); Invalidate();
            });
            fade.Paint = delegate(Graphics g, Item item, bool h) { PaintChip(g, item.R, o.Cfg.Fade, h, L.T("Vervagen"), "zandloper", o.Cfg.Fade ? T.Text : T.Icon); };
            y += 32 * S + pad;
            ClientSize = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(y));
        }

        RectangleF segBox, labelRow, titleRow;
        Dictionary<Item, int> tipFor = new Dictionary<Item, int>();

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (sliding) { Slide(e.X); return; }
            Item before = hover;
            base.OnMouseMove(e);
            int idx;
            if (hover != before && hover != null && tipFor.TryGetValue(hover, out idx))
                if (ov.Cfg.ShowTips) tip.Show(Palette.NameOf(ov.PaletteIndex, ov.Colors[Math.Min(idx, ov.Colors.Length - 1)]) + (Shortcuts.ColorCombo(idx).Length > 0 ? "  (" + Shortcuts.ColorCombo(idx) + ")" : ""), this, new Point((int)hover.R.X, (int)(hover.R.Bottom + 4 * S)), 2500);
        }

        protected override bool OnPress(MouseEventArgs e)
        {
            RectangleF hit = RectangleF.Inflate(track, 10 * S, 10 * S);
            if (e.Button == MouseButtons.Left && hit.Contains(e.Location)) { sliding = true; Capture = true; Slide(e.X); return true; }
            return false;
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (sliding) { sliding = false; Capture = false; } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) sliding = false; }

        void Slide(int x)
        {
            float f = Math.Max(0, Math.Min(1, (x - track.X) / track.Width));
            ov.SetWidth((float)Math.Round(1 + f * (MaxW - 1)));
            Invalidate();
        }

        protected override void PaintExtra(Graphics g)
        {
            Theme t = T;
            using (SolidBrush b = new SolidBrush(t.Chip))
            using (GraphicsPath gp = Svg.RoundRect(segBox, 9 * S)) g.FillPath(b, gp);
            Label(g, L.T("Pen").ToUpperInvariant(), titleRow, t.Muted, 11, true, StringAlignment.Near);
            Label(g, L.T("Dikte"), new RectangleF(labelRow.X, labelRow.Y, 46 * S, labelRow.Height), t.Muted, 12, true, StringAlignment.Near);
            Label(g, ((int)ov.PenWidth).ToString(), new RectangleF(labelRow.Right - 30 * S, labelRow.Y, 30 * S, labelRow.Height), t.Text, 13, true, StringAlignment.Far);
            float f = Math.Max(0, Math.Min(1, (ov.PenWidth - 1) / (MaxW - 1)));
            using (SolidBrush b = new SolidBrush(t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46")))
            using (GraphicsPath gp = Svg.RoundRect(track, 2 * S)) g.FillPath(b, gp);
            using (SolidBrush b = new SolidBrush(t.Accent))
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * f), track.Height), 2 * S)) g.FillPath(b, gp);
            float kx = track.X + track.Width * f, ky = track.Y + track.Height / 2;
            using (SolidBrush b = new SolidBrush(t.IsLight ? Color.White : Color.White)) g.FillEllipse(b, kx - 8 * S, ky - 8 * S, 16 * S, 16 * S);
            if (t.IsLight) using (Pen p = new Pen(ColorTranslator.FromHtml("#D4D4D8"), 1)) g.DrawEllipse(p, kx - 8 * S, ky - 8 * S, 16 * S, 16 * S);
        }
    }


    // ------------------------------------------------------------------ Rij met opties onder een knop (vormen, stempels, bord)
    class RowPopup : BarForm
    {
        protected float x, pad = 6 * 1, h;

        public RowPopup(Overlay o) : base(o) { }

        protected void Start(string title)
        {
            pad = 6 * S; h = 32 * S;
            x = 12 * S;
            if (title != null) { titleText = title; titleW = TextWidth(title, 12) + 8 * S; x += titleW; }
            else titleText = null;
        }

        string titleText; float titleW;
        List<RectangleF> rowSeps = new List<RectangleF>();

        public override void Rebuild() { rowSeps.Clear(); base.Rebuild(); }

        protected Item Chip(string text, string icon, Color iconColor, string tipText, Func<bool> on, Action click)
        {
            float w = (text == null ? 0 : TextWidth(text, 13) + 20 * S) + (icon != null ? (text == null ? 32 * S : 22 * S) : 0);
            Item it = Add(new RectangleF(x, pad, w, h), tipText, click);
            it.Paint = delegate(Graphics g, Item item, bool hv) { PaintChip(g, item.R, on != null && on(), hv, text, icon, iconColor); };
            x += w + 6 * S;
            return it;
        }

        protected void Sep() { rowSeps.Add(Px(new RectangleF(x + 2 * S, pad + (h - 22 * S) / 2, Math.Max(1, Px(S)), 22 * S))); x += 11 * S; }

        protected void Finish() { ClientSize = new Size((int)Math.Ceiling(x + 2 * S), (int)Math.Ceiling(h + pad * 2)); }

        protected override void PaintExtra(Graphics g)
        {
            if (titleText != null) Label(g, titleText, new RectangleF(12 * S, 0, titleW, ClientSize.Height), T.Muted, 12, true, StringAlignment.Near);
            using (SolidBrush b = new SolidBrush(T.Sep)) foreach (RectangleF r in rowSeps) g.FillRectangle(b, r);
        }
    }

    // ------------------------------------------------------------------ Vormen: lijn, pijl, rechthoek, cirkel
    // ------------------------------------------------------------------ Gum: groottes en alles wissen
    class EraserPopup : RowPopup
    {
        public EraserPopup(Overlay o) : base(o) { Text = "NexusPen gum"; }

        public static readonly float[] Widths = { 3, 8, 18, 40 };   // de gum is twee keer zo breed als dit getal

        protected override void Build()
        {
            Overlay o = ov;
            Start(L.T("Gum"));
            string[] names = { L.T("Klein"), L.T("Normaal"), L.T("Groot"), L.T("Extra groot") };
            for (int i = 0; i < Widths.Length; i++)
            {
                float w = Widths[i];
                Chip(names[i], null, Color.Empty, L.T("Grootte van de gum. Muiswiel = groter of kleiner"),
                    delegate { return o.CurrentTool == Tool.Eraser && Math.Abs(o.PenWidth - w) < 0.5f; },
                    delegate { o.SetTool(Tool.Eraser); o.SetWidth(w); Invalidate(); });
            }
            Sep();
            Chip(L.T("Alles wissen"), "prullenbak", Color.Empty, L.T("Alles wissen") + "  (Delete). " + L.T("Terug met Ctrl+Z"), null,
                delegate { o.ClearAll(); if (o.Bar != null) o.Bar.ClosePopups(); });
            Finish();
        }
    }

    class ShapePopup : RowPopup
    {
        public ShapePopup(Overlay o) : base(o) { Text = "NexusPen vormen"; }

        protected override void Build()
        {
            Overlay o = ov;
            Start(null);
            string[] icons = { "lijn", "pijl", "rechthoek", "cirkel" };
            string[] tips = { L.T("Lijn. Shift = recht"), L.T("Pijl. Shift = recht"), L.T("Rechthoek. Shift = vierkant"), L.T("Cirkel. Shift = rond") };
            Tool[] tools = { Tool.Line, Tool.Arrow, Tool.Rect, Tool.Ellipse };
            string[] ids = { "lijn", "pijl", "rechthoek", "cirkel" };
            for (int i = 0; i < 4; i++)
            {
                Tool t = tools[i];
                Chip(null, icons[i], Color.Empty, Shortcuts.Tip(tips[i], ids[i]), delegate { return o.DrawMode && o.CurrentTool == t; },
                    delegate { o.SetTool(t); if (o.Bar != null) o.Bar.ClosePopups(); });
            }
            Finish();
        }
    }

    // ------------------------------------------------------------------ Stempels
    class StampPopup : RowPopup
    {
        public StampPopup(Overlay o) : base(o) { Text = "NexusPen stempels"; }

        protected override void Build()
        {
            Overlay o = ov;
            Start(L.T("Stempel"));
            string[] tips = { L.T("Vinkje"), L.T("Kruis"), L.T("Ster"), L.T("Vraagteken") };
            string[] icons = { "vink", "kruis", "ster", "vraag" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Chip(null, icons[i], Render.StampColor(Render.StampKinds[i]), tips[i], delegate { return o.Cfg.StampKind == idx; },
                    delegate { o.Cfg.StampKind = idx; o.Cfg.Save(); o.SetTool(Tool.Stamp); Invalidate(); });
            }
            Item num = Chip(L.T("Nummer"), null, Color.Empty, L.T("Telt vanzelf op: 1, 2, 3. Rechtsklik = weer bij 1 beginnen"),
                delegate { return o.Cfg.StampKind == 4; }, delegate { o.Cfg.StampKind = 4; o.Cfg.Save(); o.SetTool(Tool.Stamp); Invalidate(); });
            num.R = Px(new RectangleF(num.R.X, num.R.Y, num.R.Width + 26 * S, num.R.Height));
            x += 26 * S;
            num.RightClick = delegate { o.Cfg.StampNext = 1; o.Cfg.Save(); o.UpdateCursor(); Invalidate(); };
            num.Paint = delegate(Graphics g, Item item, bool hv)
            {
                PaintChip(g, item.R, o.Cfg.StampKind == 4, hv, null, null, Color.Empty);
                float d = 20 * S, cx = item.R.X + 8 * S;
                using (SolidBrush b = new SolidBrush(T.IsLight ? Color.Black : Color.White)) g.FillEllipse(b, cx, item.R.Y + (item.R.Height - d) / 2, d, d);
                Label(g, o.Cfg.StampNext.ToString(), new RectangleF(cx, item.R.Y, d, item.R.Height), T.IsLight ? Color.White : Color.Black, 11, true, StringAlignment.Center);
                Label(g, L.T("Nummer"), new RectangleF(cx + d + 6 * S, item.R.Y, item.R.Width - d - 18 * S, item.R.Height), T.Text, 13, true, StringAlignment.Near);
            };
            Sep();
            string[] sizes = { "S", "M", "L" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Chip(sizes[i], null, Color.Empty, L.T("Grootte van de stempel"), delegate { return o.Cfg.StampPx <= 0 && o.Cfg.StampSize == idx; }, delegate { o.Cfg.StampSize = idx; o.Cfg.StampPx = 0; o.Cfg.Save(); o.UpdateCursor(); Invalidate(); });
            }
            Finish();
        }
    }

    // ------------------------------------------------------------------ Bord: whiteboard, krijtbord, achtergrond en pagina's
    class BoardPopup : RowPopup
    {
        public BoardPopup(Overlay o) : base(o) { Text = "NexusPen bord"; }

        protected override void Build()
        {
            Overlay o = ov;
            Start(L.T("Bord"));
            Chip(L.T("Scherm"), null, Color.Empty, L.T("Gewoon over je scherm tekenen"), delegate { return o.BoardMode == Board.None; }, delegate { o.SetBoard(Board.None); Invalidate(); });
            Item wb = Chip(L.T("Whiteboard"), "leeg", Color.Empty, L.T("Wit bord"), delegate { return o.BoardMode == Board.White; }, delegate { o.SetBoard(Board.White); Invalidate(); });
            Item kb = Chip(L.T("Krijtbord"), "leeg", Color.Empty, L.T("Groen krijtbord"), delegate { return o.BoardMode == Board.Black; }, delegate { o.SetBoard(Board.Black); Invalidate(); });
            Item zb = Chip(L.T("Zwart bord"), "leeg", Color.Empty, L.T("Zwart bord"), delegate { return o.BoardMode == Board.Dark; }, delegate { o.SetBoard(Board.Dark); Invalidate(); });
            Swatchify(wb, Color.White, () => o.BoardMode == Board.White, L.T("Whiteboard"));
            Swatchify(kb, Overlay.ChalkGreen, () => o.BoardMode == Board.Black, L.T("Krijtbord"));
            Swatchify(zb, Overlay.DarkBoard, () => o.BoardMode == Board.Dark, L.T("Zwart bord"));
            Sep();
            string[] bgs = { L.T("Leeg"), L.T("Schrijflijnen"), L.T("Ruitjes"), L.T("Lijntjes") };
            string[] icons = { null, "schrijflijnen", "ruitjes", "lijnen" };
            string[] tips = { L.T("Geen lijnen"), L.T("Schrijflijnen, handig voor Alfa"), L.T("Ruitjes"), L.T("Lijntjes, zoals in een schrift") };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Chip(bgs[i], icons[i], Color.Empty, tips[i], delegate { return o.Cfg.BoardBg == idx; },
                    delegate { o.Cfg.BoardBg = idx; o.Cfg.Save(); if (o.BoardMode == Board.None) o.SetBoard(Board.White); else o.Rebuild(); Invalidate(); });
            }
            // zelf in- en uitzoomen: van heel veel kleine regels tot een grote regel
            Chip(null, "min", Color.Empty, L.T("Lijnen en ruitjes kleiner (meer regels)"), null, delegate { o.ZoomBoard(-1); Rebuild(); });
            Item zl = Add(new RectangleF(x, pad, 56 * S, h), L.T("Grootte van lijnen en ruitjes"), delegate { o.Cfg.BoardZoom = 1; o.Cfg.Save(); o.ZoomBoard(0); Rebuild(); });
            zl.Paint = delegate(Graphics g, Item item, bool hv) { if (hv) PaintChip(g, item.R, false, true, null, null, Color.Empty); Label(g, Math.Round(o.Cfg.BoardZoom * 100) + "%", item.R, T.Text, 13, true, StringAlignment.Center); };
            x += 56 * S + 6 * S;
            Chip(null, "zoom", Color.Empty, L.T("Lijnen en ruitjes groter (minder regels)"), null, delegate { o.ZoomBoard(1); Rebuild(); });
            Sep();
            Chip(null, "links", Color.Empty, L.T("Vorige pagina"), null, delegate { o.PrevPage(); Rebuild(); });
            Item pg = Add(new RectangleF(x, pad, 52 * S, h), L.T("Pagina"), null);
            pg.Paint = delegate(Graphics g, Item item, bool hv) { Label(g, (o.PageIndex + 1) + " / " + o.PageCount, item.R, T.Text, 13, true, StringAlignment.Center); };
            x += 52 * S + 6 * S;
            Chip(null, "rechts", Color.Empty, L.T("Volgende pagina"), null, delegate { o.NextPage(); Rebuild(); });
            Chip(null, "plus", Color.Empty, L.T("Nieuwe pagina"), null, delegate { o.NewPage(); Rebuild(); });
            Finish();
        }

        void Swatchify(Item it, Color c, Func<bool> on, string text)
        {
            it.Paint = delegate(Graphics g, Item item, bool hv)
            {
                PaintChip(g, item.R, on(), hv, null, null, Color.Empty);
                RectangleF sq = new RectangleF(item.R.X + 10 * S, item.R.Y + (item.R.Height - 14 * S) / 2, 14 * S, 14 * S);
                using (SolidBrush b = new SolidBrush(c)) using (GraphicsPath gp = Svg.RoundRect(sq, 3 * S)) g.FillPath(b, gp);
                using (Pen p = new Pen(Color.FromArgb(90, 128, 128, 128), 1)) using (GraphicsPath gp = Svg.RoundRect(sq, 3 * S)) g.DrawPath(p, gp);
                Label(g, text, new RectangleF(sq.Right + 6 * S, item.R.Y, item.R.Right - sq.Right - 10 * S, item.R.Height), T.Text, 13, true, StringAlignment.Near);
            };
        }
    }

    // ------------------------------------------------------------------ Menu met het getal: de knoppen achter het sleeprandje
    class OverflowPopup : BarForm
    {
        public List<string> Keys = new List<string>();
        public OverflowPopup(Overlay o) : base(o) { Text = "NexusPen meer"; }

        protected override void Build()
        {
            Overlay o = ov;
            float pad = 14 * S, cw = 70 * S, ch = 62 * S;
            int cols = Math.Min(5, Math.Max(3, Keys.Count));
            float w = pad * 2 + cols * cw;
            float y = pad + 22 * S;
            for (int i = 0; i < Keys.Count; i++)
            {
                string k = Keys[i];
                RectangleF r = new RectangleF(pad + (i % cols) * cw, y + (i / cols) * ch, cw, ch);
                Item it = Add(r, Toolbar.NameOf(k), delegate { if (o.Bar != null) o.Bar.PressFromOverflow(k); });
                it.Paint = delegate(Graphics g, Item item, bool hv)
                {
                    RectangleF b = new RectangleF(item.R.X + (item.R.Width - 40 * S) / 2, item.R.Y, 40 * S, 40 * S);
                    using (SolidBrush br = new SolidBrush(hv ? (T.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46")) : T.Chip))
                    using (GraphicsPath gp = Svg.RoundRect(b, 9 * S)) g.FillPath(br, gp);
                    if (k == "kleur")
                        using (SolidBrush br = new SolidBrush(o.CurrentColor)) g.FillEllipse(br, b.X + 11 * S, b.Y + 11 * S, 18 * S, 18 * S);
                    else Icons.Draw(g, Toolbar.IconOf(k), b, 20 * S, T.Icon, 2f);
                    Label(g, Toolbar.NameOf(k), new RectangleF(item.R.X, b.Bottom + 2 * S, item.R.Width, 18 * S), T.Muted, 11, false, StringAlignment.Center);
                };
            }
            y += ((Keys.Count + cols - 1) / cols) * ch + 6 * S;
            Item all = Add(new RectangleF(pad, y, w - pad * 2, 34 * S), L.T("Het sleeprandje gaat weer naar het einde"), delegate { if (o.Bar != null) o.Bar.ShowAllButtons(); });
            all.Paint = delegate(Graphics g, Item item, bool hv)
            {
                using (SolidBrush br = new SolidBrush(T.Accent)) using (GraphicsPath gp = Svg.RoundRect(item.R, 9 * S)) g.FillPath(br, gp);
                Label(g, L.T("Alles weer tonen"), item.R, T.OnAccent, 13, true, StringAlignment.Center);
            };
            y += 34 * S + pad;
            ClientSize = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(y));
        }

        protected override void PaintExtra(Graphics g)
        {
            Label(g, L.T("Verborgen knoppen"), new RectangleF(14 * S, 10 * S, ClientSize.Width - 28 * S, 20 * S), T.Text, 13, true, StringAlignment.Near);
        }
    }

    // ------------------------------------------------------------------ Balkje bij een geselecteerd item
    class MiniBar : BarForm
    {
        public MiniBar(Overlay o) : base(o) { Text = "NexusPen selectie"; }
        List<RectangleF> barSeps = new List<RectangleF>();

        public override void Rebuild() { barSeps.Clear(); base.Rebuild(); }

        protected override void Build()
        {
            Overlay o = ov;
            float pad = 6 * S, sw = 26 * S, d = 18 * S, h = 30 * S;
            float x = pad + 2 * S;
            // de eerste zes kleuren uit jouw lijst (je meest gebruikte), plus een knop voor een andere kleur
            Color[] cols = o.Colors;
            for (int pi = 0; pi < Math.Min(6, cols.Length); pi++)
            {
                Color c = cols[pi];
                Item it = Add(new RectangleF(x, pad, sw, h), Palette.NameOf(o.PaletteIndex, c), delegate { o.RecolorSelected(c); Invalidate(); });
                it.Paint = delegate(Graphics g, Item item, bool hv)
                {
                    Swatch(g, item.R, c, o.SelectedColor.ToArgb() == c.ToArgb(), hv ? d + 2 * S : d);
                };
                x += sw;
            }
            Item more = Add(new RectangleF(x, pad, sw, h), L.T("Andere kleur"), delegate { PickColor(); });
            more.Paint = delegate(Graphics g, Item item, bool hv)
            {
                RectangleF c = new RectangleF(item.R.X + (item.R.Width - d) / 2, item.R.Y + (item.R.Height - d) / 2, d, d);
                using (Pen p = new Pen(T.Muted, 1.2f * S) { DashPattern = new float[] { 2, 2 } }) g.DrawEllipse(p, c);
                Icons.Draw(g, "plus", c, 12 * S, T.Muted, 2.4f);
            };
            x += sw;
            barSeps.Add(Px(new RectangleF(x + 4 * S, pad + (h - 20 * S) / 2, Math.Max(1, Px(S)), 20 * S))); x += 9 * S;
            Button(new RectangleF(x, pad, h, h), "min", L.T("Kleiner"), delegate { o.ScaleSelected(1 / 1.12f); Invalidate(); }, null, null); x += h + 2 * S;
            Item size = Add(new RectangleF(x, pad, 34 * S, h), L.T("Grootte"), null);
            size.Paint = delegate(Graphics g, Item item, bool hv) { Label(g, o.SelectedSize.ToString(), item.R, T.Text, 13, true, StringAlignment.Center); };
            x += 36 * S;
            Button(new RectangleF(x, pad, h, h), "plus", L.T("Groter"), delegate { o.ScaleSelected(1.12f); Invalidate(); }, null, null); x += h;
            if (o.SelectionIsText)
            {
                barSeps.Add(Px(new RectangleF(x + 4 * S, pad + (h - 20 * S) / 2, Math.Max(1, Px(S)), 20 * S))); x += 9 * S;
                float rw = TextWidth(L.T("Rand"), 13) + 40 * S;
                Item rand = Add(new RectangleF(x, pad, rw, h), L.T("Rand om de tekst aan of uit"), delegate { o.ToggleOutlineSelected(); Invalidate(); });
                rand.Paint = delegate(Graphics g, Item item, bool hv) { PaintChip(g, item.R, o.SelectionHasOutline, hv, L.T("Rand"), "tekst", Color.Empty); };
                x += rw + 2 * S;
            }
            barSeps.Add(Px(new RectangleF(x + 4 * S, pad + (h - 20 * S) / 2, Math.Max(1, Px(S)), 20 * S))); x += 9 * S;
            Button(new RectangleF(x, pad, h, h), "dupliceer", Shortcuts.Tip(L.T("Kopie maken"), "kopie"), delegate { o.DuplicateSelected(); }, null, null); x += h + 2 * S;
            Button(new RectangleF(x, pad, h, h), "voren", L.T("Naar voren"), delegate { o.BringSelectedToFront(); }, null, null); x += h + 2 * S;
            barSeps.Add(Px(new RectangleF(x + 4 * S, pad + (h - 20 * S) / 2, Math.Max(1, Px(S)), 20 * S))); x += 9 * S;
            Item del = Add(new RectangleF(x, pad, h, h), L.T("Verwijderen") + "  (Delete)", delegate { o.DeleteSelected(); });
            del.Paint = delegate(Graphics g, Item item, bool hv)
            {
                if (hv) using (SolidBrush b = new SolidBrush(T.Hover)) using (GraphicsPath gp = Svg.RoundRect(item.R, 8 * S)) g.FillPath(b, gp);
                Icons.Draw(g, "prullenbak", item.R, 18 * S, ColorTranslator.FromHtml("#EF4444"), 2f);
            };
            x += h + pad + 2 * S;
            ClientSize = new Size((int)x, (int)(h + pad * 2));
        }

        void PickColor()
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true;
                cd.Color = ov.SelectedColor;
                if (cd.ShowDialog() == DialogResult.OK) ov.RecolorSelected(cd.Color);
            }
        }

        protected override void PaintExtra(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(T.Sep)) foreach (RectangleF r in barSeps) g.FillRectangle(b, r);
        }
    }

    // ------------------------------------------------------------------ Klein logo aan de rand als de balk weggeschoven is
    class Lipje : BarForm
    {
        Toolbar bar;
        int edge;   // aan welke rand: 0 links, 1 boven, 2 rechts, 3 onder
        bool vert { get { return edge == 0 || edge == 2; } }

        public Lipje(Overlay o, Toolbar b) : base(o) { bar = b; Text = "NexusPen"; }

        public void Place(Rectangle barBounds, bool vertical)
        {
            S = W.UiScaleAt(barBounds.Location);
            Rectangle wa = Screen.FromRectangle(barBounds).WorkingArea;
            Point c = new Point(barBounds.Left + barBounds.Width / 2, barBounds.Top + barBounds.Height / 2);
            // staande balk naar de dichtstbijzijnde zijkant, liggende naar boven of onder
            if (vertical) edge = c.X - wa.Left <= wa.Right - c.X ? 0 : 2;
            else edge = c.Y - wa.Top <= wa.Bottom - c.Y ? 1 : 3;
            Rebuild();
            PutAt(c, wa);
        }

        void PutAt(Point c, Rectangle wa)
        {
            int x, y;
            if (vert) { x = edge == 0 ? wa.Left : wa.Right - Width; y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, c.Y - Height / 2)); }
            else { y = edge == 1 ? wa.Top : wa.Bottom - Height; x = Math.Max(wa.Left, Math.Min(wa.Right - Width, c.X - Width / 2)); }
            Location = new Point(x, y);
        }

        public void Restyle() { Point c = Center; Rebuild(); PutAt(c, Screen.FromPoint(c).WorkingArea); }

        // Lipje slepen naar elke rand van het scherm; gewoon klikken = de balk komt terug
        bool pressed, dragged;
        Point downAt;

        protected override bool OnPress(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;
            pressed = true; dragged = false; downAt = Control.MousePosition; Capture = true;
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed) return;
            Point m = Control.MousePosition;
            if (!dragged && Math.Abs(m.X - downAt.X) + Math.Abs(m.Y - downAt.Y) < 5) return;
            if (!dragged) bar.LipjeMoved();
            dragged = true;
            Rectangle wa = Screen.FromPoint(m).WorkingArea;
            int[] d = { m.X - wa.Left, m.Y - wa.Top, wa.Right - m.X, wa.Bottom - m.Y };
            int best = 0;
            for (int i = 1; i < 4; i++) if (d[i] < d[best]) best = i;
            if (best != edge) { edge = best; Rebuild(); }
            PutAt(m, wa);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!pressed) return;
            pressed = false; Capture = false;
            if (!dragged) bar.Undock();
        }

        // Muis kwijt tijdens het slepen (Alt+Tab, Windows-toets): stoppen met slepen
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (pressed && !Capture) pressed = false;
        }

        public Point Center { get { return new Point(Left + Width / 2, Top + Height / 2); } }
        public bool Vertical { get { return vert; } }

        protected override void Build()
        {
            ClientSize = vert ? new Size((int)(38 * S), (int)(34 * S)) : new Size((int)(34 * S), (int)(38 * S));
            Item it = Add(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), L.T("Werkbalk terughalen"), delegate { bar.Undock(); });
            it.Paint = delegate(Graphics g, Item item, bool h)
            {
                Theme t = T;
                float W0 = item.R.Width, H0 = item.R.Height;
                if (h) using (SolidBrush b = new SolidBrush(t.Hover)) g.FillRectangle(b, item.R);
                // oranje streepje aan de randkant, logo ernaast
                RectangleF bar2 = edge == 0 ? new RectangleF(4 * S, H0 / 2 - 7 * S, 3 * S, 14 * S)
                    : edge == 2 ? new RectangleF(W0 - 7 * S, H0 / 2 - 7 * S, 3 * S, 14 * S)
                    : edge == 1 ? new RectangleF(W0 / 2 - 7 * S, 4 * S, 14 * S, 3 * S)
                    : new RectangleF(W0 / 2 - 7 * S, H0 - 7 * S, 14 * S, 3 * S);
                using (SolidBrush b = new SolidBrush(t.Accent)) using (GraphicsPath gp = Svg.RoundRect(bar2, 1.5f * S)) g.FillPath(b, gp);
                float z = 20 * S;
                RectangleF lr = edge == 0 ? new RectangleF(14 * S, (H0 - z) / 2, z, z)
                    : edge == 2 ? new RectangleF(W0 - 14 * S - z, (H0 - z) / 2, z, z)
                    : edge == 1 ? new RectangleF((W0 - z) / 2, 13 * S, z, z)
                    : new RectangleF((W0 - z) / 2, H0 - 13 * S - z, z, z);
                Logo.Draw(g, lr, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
            };
        }

        // Alleen de kant van het scherm af is rond: de afgeronde rechthoek loopt aan de randkant door buiten het venster
        protected override int ShapeKind { get { return 1 + edge; } }
        protected override GraphicsPath ShapePath(float w, float h) { return LipShape(w, h, 0); }
        protected override GraphicsPath BorderPath(float w, float h, float bw) { return LipShape(w, h, bw / 2); }

        GraphicsPath LipShape(float w, float h, float i)
        {
            float r = 10 * S, out1 = r + 2;
            RectangleF b = RectangleF.FromLTRB(i, i, w - i, h - i);
            if (edge == 0) b = RectangleF.FromLTRB(-out1, i, w - i, h - i);
            else if (edge == 2) b = RectangleF.FromLTRB(i, i, w + out1, h - i);
            else if (edge == 1) b = RectangleF.FromLTRB(i, -out1, w - i, h - i);
            else b = RectangleF.FromLTRB(i, i, w - i, h + out1);
            return Svg.RoundRect(b, r - i);
        }
    }

    // ------------------------------------------------------------------ Het logo als alles ingeklapt is
    class LogoTile : BarForm
    {
        Toolbar bar;
        Point downAt;
        bool pressed;

        public LogoTile(Overlay o, Toolbar b) : base(o) { bar = b; Text = "NexusPen"; }

        int side, logo;

        // Precies op de plek en in de maat van het logo in de balk: 'center' is het midden van het logo
        public void PlaceAt(Point center, float scale, int tileSide, int logoSize)
        {
            S = scale; side = tileSide; logo = logoSize;
            Rebuild();
            Rectangle wa = Screen.FromPoint(center).WorkingArea;
            int x = center.X - Width / 2, y = center.Y - Height / 2;
            Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, x)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, y)));
        }

        public Point LogoCenter { get { return new Point(Left + Width / 2, Top + Height / 2); } }

        protected override void Build()
        {
            int sd = side > 0 ? side : (int)(48 * S);
            ClientSize = new Size(sd, sd);
            Item it = Add(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), L.T("Klik om alles weer te tonen. Slepen = verplaatsen, rechtsklik = menu"), null);
            it.Paint = delegate(Graphics g, Item item, bool h)
            {
                Theme t = T;
                if (h) using (SolidBrush b = new SolidBrush(t.Hover)) g.FillRectangle(b, item.R);
                float z = logo > 0 ? logo : 34 * S;
                Logo.Draw(g, new RectangleF((item.R.Width - z) / 2, (item.R.Height - z) / 2, z, z), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
            };
        }

        protected override bool OnPress(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { bar.ShowLogoMenu(PointToScreen(new Point(0, Height))); return true; }
            if (e.Button == MouseButtons.Left) { pressed = true; downAt = e.Location; return true; }
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed && (Math.Abs(e.X - downAt.X) > 4 || Math.Abs(e.Y - downAt.Y) > 4))
            {
                pressed = false;
                W.ReleaseCapture();
                W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
                ov.Cfg.TileX = Left; ov.Cfg.TileY = Top; ov.Cfg.Save();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressed && e.Button == MouseButtons.Left) { pressed = false; ov.ExpandAll(); }
        }
    }

    // ------------------------------------------------------------------ Tipje met de dikte naast de cursor
    class WidthTip : BarForm
    {
        System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        float value, min, max; string label = "";

        public WidthTip(Overlay o) : base(o) { hideTimer.Interval = 1200; hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); }; }

        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= W.WS_EX_TRANSPARENT; return cp; } }

        public void Show(Point at, float v, float mn, float mx, string lbl)
        {
            value = v; min = mn; max = mx; label = lbl;
            Rebuild();
            Location = new Point(at.X + (int)(22 * S), at.Y - Height / 2);
            if (!Visible) Show();
            W.KeepOnTop(Handle);
            Invalidate();
            hideTimer.Stop(); hideTimer.Start();
        }

        protected override void Build() { ClientSize = new Size((int)(138 * S), (int)(62 * S)); }

        protected override void PaintExtra(Graphics g)
        {
            Theme t = T;
            Label(g, label + " " + (int)Math.Round(value), new RectangleF(12 * S, 8 * S, ClientSize.Width - 20 * S, 18 * S), t.Text, 13, true, StringAlignment.Near);
            RectangleF tr = new RectangleF(12 * S, 31 * S, ClientSize.Width - 24 * S, 4 * S);
            float f = Math.Max(0, Math.Min(1, (value - min) / (max - min)));
            using (SolidBrush b = new SolidBrush(t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46"))) using (GraphicsPath gp = Svg.RoundRect(tr, 2 * S)) g.FillPath(b, gp);
            using (SolidBrush b = new SolidBrush(t.Accent)) using (GraphicsPath gp = Svg.RoundRect(new RectangleF(tr.X, tr.Y, Math.Max(tr.Height, tr.Width * f), tr.Height), 2 * S)) g.FillPath(b, gp);
            Label(g, L.F("{0} tot {1}, scrol of knijp", (int)min, (int)max), new RectangleF(12 * S, 39 * S, ClientSize.Width - 20 * S, 16 * S), t.Muted, 11, false, StringAlignment.Near);
        }
    }

    // ------------------------------------------------------------------ Melding onderin (na een schermafbeelding)
    class Toast : BarForm
    {
        System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        string text = "", bold = "", folder;

        public Toast(Overlay o) : base(o) { hideTimer.Interval = 4500; hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); }; }

        public void ShowMessage(string t, string b, string dir)
        {
            text = t; bold = b; folder = dir;
            Rebuild();
            Rectangle wa = Screen.FromPoint(Control.MousePosition).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(40 * S));
            if (!Visible) Show();
            W.KeepOnTop(Handle);
            hideTimer.Stop(); hideTimer.Start();
        }

        protected override void Build()
        {
            float tw = TextWidth(text + " " + bold, 13) + 60 * S;
            float bw = folder != null ? TextWidth(L.T("Map openen"), 13) + 46 * S : 0;
            float w = tw + bw + 16 * S, h = 50 * S;
            if (folder != null)
            {
                Item open = Add(new RectangleF(tw, 9 * S, bw, 32 * S), folder, delegate
                {
                    try { System.Diagnostics.Process.Start(SettingsForm.Explorer, "\"" + folder + "\""); } catch (Exception ex) { Log.Error("Map openen", ex); }
                    Hide();
                });
                open.Paint = delegate(Graphics g, Item item, bool hv) { PaintChip(g, item.R, false, hv, L.T("Map openen"), "map", Color.Empty); };
            }
            ClientSize = new Size((int)w, (int)h);
        }

        protected override void PaintExtra(Graphics g)
        {
            Theme t = T;
            Icons.Draw(g, "foto", new RectangleF(14 * S, 0, 20 * S, ClientSize.Height), 18 * S, t.Accent, 2f);
            using (Font f = Fonts.Make(UiFont.Name, 13 * S, FontStyle.Regular))
            using (Font fb = UiFont.Make(13 * S, true))
            using (SolidBrush b = new SolidBrush(t.Text))
            {
                float x = 44 * S, y = (ClientSize.Height - f.GetHeight(g)) / 2;
                g.DrawString(text, f, b, x, y);
                x += g.MeasureString(text + " ", f).Width - 4 * S;
                g.DrawString(bold, fb, b, x, y);
            }
        }
    }
}
