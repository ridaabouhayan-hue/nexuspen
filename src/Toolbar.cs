using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ De werkbalk (Studio-balk, liggend of staand)
    // De knoppen staan in een catalogus. De volgorde (en welke erin staan) komt uit Settings.BarLayout,
    // zodat je ze in Instellingen kunt slepen en met rechtsklik kunt verbergen.
    class Toolbar : BarForm
    {
        public const string DefaultLayout = "muis,selecteer|pen,markeer,laser|vormen,tekst,gum|kleur,stempel,focus|undo,bord,foto|opslaan,oog,instellingen,weg";

        // alle knoppen die in de balk kunnen; de losse vormen en Opnieuw staan standaard in de bak
        public static readonly string[] Catalog = { "muis", "selecteer", "pen", "markeer", "laser", "vormen", "lijn", "pijl", "rechthoek", "cirkel",
            "tekst", "gum", "kleur", "stempel", "focus", "undo", "redo", "bord", "foto", "opslaan", "oog", "instellingen", "weg" };

        public static string NameOf(string key)
        {
            switch (key)
            {
                case "muis": return L.T("Muis");
                case "selecteer": return L.T("Selecteren");
                case "pen": return L.T("Pen");
                case "markeer": return L.T("Markeerstift");
                case "laser": return L.T("Laserpen");
                case "vormen": return L.T("Vormen");
                case "lijn": return L.T("Lijn");
                case "pijl": return L.T("Pijl");
                case "rechthoek": return L.T("Rechthoek");
                case "cirkel": return L.T("Cirkel");
                case "tekst": return L.T("Tekst");
                case "gum": return L.T("Gum");
                case "kleur": return L.T("Kleur en dikte");
                case "stempel": return L.T("Stempel");
                case "focus": return L.T("Spotlicht");
                case "undo": return L.T("Ongedaan maken");
                case "redo": return L.T("Opnieuw");
                case "bord": return L.T("Bord");
                case "foto": return L.T("Schermafbeelding");
                case "opslaan": return L.T("Opslaan");
                case "oog": return L.T("Tekeningen verbergen");
                case "instellingen": return L.T("Instellingen");
                case "weg": return L.T("Wegschuiven");
                case "|": return L.T("Streep");
            }
            return key;
        }

        public static string IconOf(string key)
        {
            switch (key)
            {
                case "weg": return "op";
                case "kleur": return "kleuren";
                default: return key;
            }
        }

        public static List<string> Layout(Settings c)
        {
            string src = string.IsNullOrEmpty(c.BarLayout) ? DefaultLayout : c.BarLayout;
            List<string> list = new List<string>();
            foreach (string group in src.Split('|'))
            {
                if (list.Count > 0) list.Add("|");
                foreach (string k in group.Split(','))
                    if (Array.IndexOf(Catalog, k.Trim()) >= 0 && !list.Contains(k.Trim())) list.Add(k.Trim());
            }
            return list;
        }

        public static string LayoutString(List<string> keys)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (string k in keys)
            {
                if (k == "|") { sb.Append('|'); first = true; continue; }
                if (!first) sb.Append(',');
                sb.Append(k); first = false;
            }
            return sb.ToString();
        }

        ColorPopup colorPopup;
        ShapePopup shapePopup;
        StampPopup stampPopup;
        BoardPopup boardPopup;
        OverflowPopup overflowPopup;
        MiniBar miniBar;
        Lipje lipje;
        LogoTile tile;
        WidthTip widthTip;
        Toast toast;
        Dictionary<string, Item> byKey = new Dictionary<string, Item>();
        List<Item> seps = new List<Item>();
        List<string> overflow = new List<string>();
        Item gripItem, handleItem, logoItem;
        bool docked, collapsed, hiddenShot;
        bool shortening;
        int shortenPreview;
        List<Item> buttonOrder = new List<Item>();
        System.Windows.Forms.Timer flash;

        public bool Docked { get { return docked; } }
        public bool Collapsed { get { return collapsed; } }

        public Toolbar(Overlay o) : base(o)
        {
            draggable = true;
            Text = "NexusPen";
            Point start = (ov.Cfg.ToolbarX != -1 || ov.Cfg.ToolbarY != -1)
                ? new Point(ov.Cfg.ToolbarX + 20, ov.Cfg.ToolbarY + 20)
                : new Point(Screen.PrimaryScreen.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 40);
            S = W.DpiScaleAt(start);
            vertical = ov.Cfg.Vertical;
            Opacity = OpacityValue;
            flash = new System.Windows.Forms.Timer();
            flash.Interval = 1700;
            flash.Tick += delegate { flash.Stop(); Invalidate(); };
            Rebuild();
            if (ov.Cfg.ToolbarX != -1 || ov.Cfg.ToolbarY != -1) Location = new Point(ov.Cfg.ToolbarX, ov.Cfg.ToolbarY);
            else ResetPosition();
            EnsureOnScreen();
            LocationChanged += delegate { if (Visible) { ov.Cfg.ToolbarX = Left; ov.Cfg.ToolbarY = Top; } ClosePopups(); };
        }

        // In de taakbalk tonen (instelbaar). Klik op het icoon in de taakbalk = in- of uitklappen.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (ov != null && ov.Cfg.ShowInTaskbar) { cp.ExStyle &= ~W.WS_EX_TOOLWINDOW; cp.ExStyle |= 0x40000; }
                return cp;
            }
        }

        double OpacityValue { get { int i = ov.Cfg.OpacityIndex; return i == 1 ? 0.9 : i == 2 ? 0.8 : 1.0; } }

        public void ResetPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (vertical) Location = new Point(wa.Left + (int)(12 * S), wa.Top + (wa.Height - Height) / 2);
            else Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (int)(12 * S));
            if (docked) Undock();
            if (collapsed) ov.ExpandAll();
        }

        public void EnsureOnScreen()
        {
            Rectangle me = Bounds;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(new Rectangle(me.X + 10, me.Y + 10, 20, 20))) return;
            ResetPosition();
        }

        // Thema, grootte, stand, hoeken, volgorde of taal veranderd
        public void Restyle()
        {
            Point keep = Location;
            vertical = ov.Cfg.Vertical;
            Opacity = OpacityValue;
            ClosePopups();
            HideMiniBar();
            if (IsHandleCreated) RecreateHandle();
            Rebuild();
            Location = keep;
            EnsureOnScreen();
            ApplyCaptureSetting();
            foreach (BarForm f in new BarForm[] { lipje, tile, miniBar, widthTip, toast, colorPopup, shapePopup, stampPopup, boardPopup, overflowPopup })
                if (f != null && !f.IsDisposed) { f.ApplyCaptureSetting(); f.S = S; }
            if (lipje != null && lipje.Visible) lipje.Restyle();
            if (tile != null && tile.Visible) tile.Rebuild();
        }

        public void Refresh2()
        {
            Invalidate();
            foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, miniBar, overflowPopup })
                if (f != null && f.Visible) f.Invalidate();
        }

        public void KeepAllOnTop()
        {
            foreach (Form f in new Form[] { this, colorPopup, shapePopup, stampPopup, boardPopup, overflowPopup, miniBar, lipje, tile, toast })
                if (f != null && f.Visible && f.IsHandleCreated) W.KeepOnTop(f.Handle);
        }

        // ---- opbouw
        public float BW { get { int i = ov.Cfg.SizeIndex; return (i == 0 ? 32 : i == 2 ? 46 : 38) * S; } }

        float Long(float bw, float bh) { return vertical ? bh : bw; }
        float Cross(float bw, float bh) { return vertical ? bw : bh; }
        RectangleF Rect(float pos, float cross, float len, float thick) { return vertical ? new RectangleF(cross, pos, thick, len) : new RectangleF(pos, cross, len, thick); }

        public override void Rebuild() { seps.Clear(); byKey.Clear(); buttonOrder.Clear(); gripItem = null; handleItem = null; base.Rebuild(); }

        protected override void Build()
        {
            float bw = BW, bh = vertical ? bw - 6 * S : bw, gap = 2 * S;
            float padMain = (vertical ? 5 : 6) * S, padCross = (vertical ? 8 : 5) * S;
            float pos = padMain, cross = padCross;
            float thick = vertical ? bw : bh;

            float gripLen = vertical ? (4 + 12 + 8) * S : (4 + 12 + 6) * S;
            gripItem = new Item(); gripItem.R = Rect(pos, cross, gripLen, thick); gripItem.Paint = PaintGrip;
            pos += gripLen;

            float lb = vertical ? bh + 4 * S : bw;
            logoItem = Add(Rect(pos, cross, lb, thick), L.T("Alles inklappen: balk en tekeningen even weg") + "  (" + ov.Cfg.HotkeyHide + ")", delegate { ov.CollapseAll(); });
            logoItem.Paint = PaintLogo;
            logoItem.RightClick = delegate { ShowLogoMenu(PointToScreen(new Point((int)logoItem.R.X, (int)logoItem.R.Bottom))); };
            pos += lb;

            List<string> layout = Layout(ov.Cfg);
            int total = 0;
            foreach (string k in layout) if (k != "|") total++;
            int limit = shortening || ov.Cfg.ShortenAt <= 0 || ov.Cfg.ShortenAt >= total ? total : ov.Cfg.ShortenAt;
            overflow.Clear();
            int placed = 0;
            bool needSep = true;
            foreach (string k in layout)
            {
                if (k == "|") { needSep = true; continue; }
                if (placed >= limit) { overflow.Add(k); continue; }
                if (needSep) { AddSep(ref pos, cross, thick); needSep = false; }
                Item it = AddButton(k, Rect(pos, cross, Long(bw, bh), Cross(bw, bh)));
                buttonOrder.Add(it);
                pos += Long(bw, bh) + gap;
                placed++;
            }
            if (overflow.Count > 0)
            {
                AddSep(ref pos, cross, thick);
                float ow = vertical ? bh : bw + 8 * S;
                Item more = Add(Rect(pos, cross, ow, Cross(bw, bh)), L.F("Nog {0} knoppen", overflow.Count), delegate { ToggleOverflow(); });
                more.Paint = PaintOverflow;
                byKey["meer"] = more;
                pos += ow + gap;
            }
            // sleeprandje om de balk in te korten
            float hl = 12 * S;
            handleItem = new Item(); handleItem.R = Rect(pos, cross, hl, thick); handleItem.Paint = PaintHandle;
            handleItem.Tip = L.T("Sleep om de balk in te korten");
            pos += hl + padMain - 2 * S;

            float tot = thick + padCross * 2;
            ClientSize = vertical ? new Size((int)Math.Ceiling(tot), (int)Math.Ceiling(pos)) : new Size((int)Math.Ceiling(pos), (int)Math.Ceiling(tot));
        }

        void AddSep(ref float pos, float cross, float thick)
        {
            float m = (vertical ? 4 : 5) * S;
            Item sep = new Item();
            sep.R = vertical ? new RectangleF(cross + thick / 2 - 11 * S, pos + m, 22 * S, 1 * S) : new RectangleF(pos + m, cross + thick / 2 - 11 * S, 1 * S, 22 * S);
            seps.Add(sep);
            pos += m * 2 + 1 * S;
        }

        // Eén knop uit de catalogus maken
        Item AddButton(string key, RectangleF r)
        {
            Overlay o = ov;
            Item it = null;
            switch (key)
            {
                case "muis": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Muis: gewoon klikken op je scherm"), "") + "  (Esc)", Tool.Mouse); break;
                case "selecteer": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Selecteren en verplaatsen"), "selecteer"), Tool.Select); break;
                case "pen": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Pen"), "pen"), Tool.Pen); break;
                case "markeer": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Markeerstift"), "markeer"), Tool.Marker); break;
                case "laser": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Laserpen, verdwijnt vanzelf"), "laser"), Tool.Laser); break;
                case "lijn": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Lijn"), "lijn"), Tool.Line); break;
                case "pijl": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Pijl"), "pijl"), Tool.Arrow); break;
                case "rechthoek": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Rechthoek"), "rechthoek"), Tool.Rect); break;
                case "cirkel": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Cirkel"), "cirkel"), Tool.Ellipse); break;
                case "tekst": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Tekst"), "tekst"), Tool.Text); break;
                case "gum": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Gum, of rechtermuisknop ingedrukt"), "gum"), Tool.Eraser); break;
                case "vormen":
                    it = Button(r, "vormen", L.T("Vormen: lijn, pijl, rechthoek, cirkel"), delegate { Use(key); TogglePopup(ref shapePopup, key); },
                        delegate { return o.DrawMode && Overlay.IsShape(o.CurrentTool); }, delegate { return IsOpen(shapePopup); });
                    it.Paint = PaintShapeButton;
                    break;
                case "kleur":
                    it = Add(r, L.T("Kleur en dikte (muiswiel of knijpen op het touchpad)"), delegate { Use(key); TogglePopup(ref colorPopup, key); });
                    it.Paint = PaintColorButton;
                    break;
                case "stempel":
                    it = Button(r, "stempel", Shortcuts.Tip(L.T("Stempel: vinkje, kruis, ster, vraagteken of nummer"), "stempel"),
                        delegate { Use(key); o.SetTool(Tool.Stamp); TogglePopup(ref stampPopup, key); },
                        delegate { return o.DrawMode && o.CurrentTool == Tool.Stamp; }, null);
                    break;
                case "focus":
                    it = Button(r, "focus", Shortcuts.Tip(L.T("Spotlicht: alleen rond je muis is het licht. Muiswiel = groter of kleiner"), "focus"),
                        delegate { Use(key); ClosePopups(); o.ToggleFocus(); }, delegate { return o.DrawMode && o.CurrentTool == Tool.Focus; }, null);
                    break;
                case "undo":
                    it = Button(r, "undo", L.T("Ongedaan maken") + "  (Ctrl+Z). " + L.T("Rechtsklik = opnieuw") + " (Ctrl+Y)", delegate { Use(key); o.Undo(); }, null, null);
                    break;
                case "redo":
                    it = Button(r, "redo", L.T("Opnieuw") + "  (Ctrl+Y)", delegate { Use(key); o.Redo(); }, null, null);
                    break;
                case "bord":
                    it = Button(r, "bord", Shortcuts.Tip(L.T("Bord: whiteboard of krijtbord, met lijnen en pagina's"), "bord"),
                        delegate { Use(key); TogglePopup(ref boardPopup, key); }, null, delegate { return o.BoardMode != Board.None && o.DrawMode; });
                    break;
                case "foto":
                    it = Button(r, "foto", Shortcuts.Tip(L.T("Schermafbeelding, naar Afbeeldingen\\NexusPen en het klembord"), "foto"),
                        delegate { Use(key); ClosePopups(); o.Screenshot(); }, null, null);
                    break;
                case "opslaan":
                    it = Button(r, "opslaan", L.T("Opslaan: je tekening staat er de volgende keer weer") + "  (Ctrl+S)",
                        delegate { Use(key); ClosePopups(); if (o.SaveExplicit()) { flash.Stop(); flash.Start(); } }, null, null);
                    it.Paint = PaintSave;
                    break;
                case "oog":
                    it = Button(r, "oog", Shortcuts.Tip(L.T("Tekeningen verbergen of weer tonen"), "oog"),
                        delegate { Use(key); ClosePopups(); o.ToggleStrokesVisible(); }, null, delegate { return !o.StrokesVisible; });
                    it.Paint = PaintEye;
                    break;
                case "instellingen":
                    it = Button(r, "instellingen", L.T("Instellingen"), delegate { Use(key); ClosePopups(); o.ShowSettings(null); }, null, null);
                    break;
                case "weg":
                    it = Button(r, vertical ? "links" : "op", L.T("Wegschuiven naar de rand. Je tekeningen blijven staan"), delegate { Use(key); DockAway(); }, null, null);
                    it.Muted = true;
                    break;
            }
            if (it == null) it = Add(r, key, null);
            it.Key = key;
            if (key == "undo") it.RightClick = delegate { o.Redo(); };
            else it.RightClick = delegate { ShowButtonMenu(it); };
            byKey[key] = it;
            return it;
        }

        Item ToolBtn(RectangleF r, string icon, string tipText, Tool t)
        {
            Overlay o = ov;
            string key = icon;
            return Button(r, icon, tipText, delegate { Use(key); ClosePopups(); o.SetTool(t); },
                delegate { return t == Tool.Mouse ? !o.DrawMode : o.DrawMode && o.CurrentTool == t; }, null);
        }

        void Use(string key) { Usage.Log("klik", key, "balk"); }

        protected override void PaintExtra(Graphics g)
        {
            if (gripItem != null) PaintGrip(g, gripItem, false);
            using (SolidBrush b = new SolidBrush(T.Sep)) foreach (Item s in seps) g.FillRectangle(b, s.R);
            if (handleItem != null) PaintHandle(g, handleItem, hoverHandle || shortening);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // tijdens het inkorten: wat achter het randje valt, wordt doorzichtig
            if (shortening)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(170, T.Bar)))
                    for (int i = shortenPreview; i < buttonOrder.Count; i++) e.Graphics.FillRectangle(b, buttonOrder[i].R);
        }

        void PaintGrip(Graphics g, Item it, bool h)
        {
            int cols = vertical ? 3 : 2, rows = vertical ? 2 : 3;
            float d = 4 * S, gp = 4 * S;
            float w = cols * d + (cols - 1) * gp, hh = rows * d + (rows - 1) * gp;
            float x0 = vertical ? it.R.X + (it.R.Width - w) / 2 : it.R.X + 4 * S;
            float y0 = vertical ? it.R.Y + 4 * S : it.R.Y + (it.R.Height - hh) / 2;
            using (SolidBrush b = new SolidBrush(T.IsLight ? ColorTranslator.FromHtml("#A1A1AA") : ColorTranslator.FromHtml("#71717A")))
                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                        g.FillEllipse(b, x0 + c * (d + gp), y0 + r * (d + gp), d, d);
        }

        void PaintHandle(Graphics g, Item it, bool h)
        {
            Color c = h ? T.Accent : (T.IsLight ? ColorTranslator.FromHtml("#C4C4CC") : ColorTranslator.FromHtml("#52525B"));
            RectangleF r = vertical ? new RectangleF(it.R.X + it.R.Width / 2 - 10 * S, it.R.Y + it.R.Height / 2 - 2 * S, 20 * S, 4 * S)
                                    : new RectangleF(it.R.X + it.R.Width / 2 - 2 * S, it.R.Y + it.R.Height / 2 - 10 * S, 4 * S, 20 * S);
            using (SolidBrush b = new SolidBrush(c)) using (GraphicsPath gp = Svg.RoundRect(r, 2 * S)) g.FillPath(b, gp);
        }

        void PaintLogo(Graphics g, Item it, bool h)
        {
            Theme t = T;
            if (h) using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
            float size = (float)Math.Round(BW * 26f / 38f);
            RectangleF r = new RectangleF(it.R.X + (it.R.Width - size) / 2, it.R.Y + (it.R.Height - size) / 2, size, size);
            Logo.Draw(g, r, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
        }

        void PaintColorButton(Graphics g, Item it, bool h)
        {
            Theme t = T;
            bool open = IsOpen(colorPopup);
            using (SolidBrush b = new SolidBrush(h || open ? (t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46")) : t.Chip))
            using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
            float d = 18 * S * (BW / (38 * S));
            float cx = it.R.X + it.R.Width / 2, cy = it.R.Y + it.R.Height / 2;
            using (SolidBrush b = new SolidBrush(t.IsLight ? Color.Black : Color.White)) g.FillEllipse(b, cx - d / 2 - 3.5f * S, cy - d / 2 - 3.5f * S, d + 7 * S, d + 7 * S);
            using (SolidBrush b = new SolidBrush(t.Chip)) g.FillEllipse(b, cx - d / 2 - 2 * S, cy - d / 2 - 2 * S, d + 4 * S, d + 4 * S);
            using (SolidBrush b = new SolidBrush(ov.CurrentColor)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        }

        void PaintShapeButton(Graphics g, Item it, bool h)
        {
            Tool s = ov.LastShape;
            string ic = s == Tool.Line ? "lijn" : s == Tool.Rect ? "rechthoek" : s == Tool.Ellipse ? "cirkel" : "pijl";
            it.Icon = it.Active() ? ic : "vormen";
            it.Paint = null; PaintItem(g, it); it.Paint = PaintShapeButton;
        }

        void PaintSave(Graphics g, Item it, bool h)
        {
            bool justSaved = flash.Enabled;
            it.Icon = justSaved ? "vink" : "opslaan";
            it.Lit = justSaved ? (Func<bool>)delegate { return true; } : null;
            it.Paint = null; PaintItem(g, it); it.Paint = PaintSave;
        }

        void PaintEye(Graphics g, Item it, bool h)
        {
            it.Icon = ov.StrokesVisible ? "oog" : "oogopen";
            it.Paint = null; PaintItem(g, it); it.Paint = PaintEye;
        }

        void PaintOverflow(Graphics g, Item it, bool h)
        {
            Theme t = T;
            bool open = IsOpen(overflowPopup);
            using (SolidBrush b = new SolidBrush(open || h ? (t.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#3F3F46")) : t.Chip))
            using (GraphicsPath gp = Svg.RoundRect(it.R, 9 * S)) g.FillPath(b, gp);
            using (Pen p = new Pen(t.Accent, 1.5f * S)) using (GraphicsPath gp = Svg.RoundRect(RectangleF.Inflate(it.R, -0.75f * S, -0.75f * S), 9 * S)) g.DrawPath(p, gp);
            float half = it.R.Width / 2;
            if (vertical)
            {
                Icons.Draw(g, "neer", new RectangleF(it.R.X, it.R.Y + 1 * S, it.R.Width, it.R.Height * 0.5f), 14 * S, t.Text, 2.2f);
                Label(g, overflow.Count.ToString(), new RectangleF(it.R.X, it.R.Y + it.R.Height * 0.42f, it.R.Width, it.R.Height * 0.55f), t.Text, 12, true, StringAlignment.Center);
            }
            else
            {
                Icons.Draw(g, "meer", new RectangleF(it.R.X + 3 * S, it.R.Y, half, it.R.Height), 15 * S, t.Text, 2.2f);
                Label(g, overflow.Count.ToString(), new RectangleF(it.R.X + half - 2 * S, it.R.Y, half, it.R.Height), t.Text, 13, true, StringAlignment.Center);
            }
        }

        // ---- inkorten met het sleeprandje
        bool hoverHandle;

        protected override bool OnPress(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && handleItem != null && RectangleF.Inflate(handleItem.R, 4 * S, 4 * S).Contains(e.Location))
            {
                shortening = true;
                Capture = true;
                ClosePopups();
                Rebuild();               // volledige balk laten zien, wat wegvalt wordt doorzichtig
                shortenPreview = buttonOrder.Count;
                if (ov.Cfg.ShortenAt > 0) shortenPreview = Math.Min(ov.Cfg.ShortenAt, buttonOrder.Count);
                Invalidate();
                return true;
            }
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (shortening)
            {
                float p = vertical ? e.Y : e.X;
                int n = 0;
                foreach (Item it in buttonOrder) { float mid = vertical ? it.R.Y + it.R.Height / 2 : it.R.X + it.R.Width / 2; if (mid < p) n++; }
                shortenPreview = Math.Max(1, n);
                Invalidate();
                return;
            }
            bool hh = handleItem != null && RectangleF.Inflate(handleItem.R, 4 * S, 4 * S).Contains(e.Location);
            if (hh != hoverHandle) { hoverHandle = hh; Invalidate(); if (hh) tip.Show(handleItem.Tip, this, TipPoint(handleItem), 2500); }
            base.OnMouseMove(e);
            if (hh) Cursor = vertical ? Cursors.SizeNS : Cursors.SizeWE;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!shortening) return;
            shortening = false;
            Capture = false;
            ov.Cfg.ShortenAt = shortenPreview >= buttonOrder.Count ? 0 : shortenPreview;
            ov.Cfg.Save();
            Usage.Log("inkorten", ov.Cfg.ShortenAt.ToString(), "balk");
            Point k = Location;
            Rebuild();
            Location = k;
        }

        Point TipPoint(Item it)
        {
            return vertical ? new Point((int)(it.R.Right + 10 * S), (int)(it.R.Y + 6 * S)) : new Point((int)it.R.X, (int)(it.R.Bottom + 8 * S));
        }

        // ---- rechtsklik-menu's
        void ShowButtonMenu(Item it)
        {
            ContextMenuStrip m = Menus.Make(T);
            m.Items.Add(L.T("Verbergen uit de balk"), null, delegate { HideKey(it.Key); });
            m.Items.Add(L.T("Werkbalk aanpassen..."), null, delegate { ov.ShowSettings("Werkbalk"); });
            m.Show(PointToScreen(new Point((int)it.R.X, (int)it.R.Bottom)));
        }

        public void ShowLogoMenu(Point at)
        {
            ContextMenuStrip m = Menus.Make(T);
            m.Items.Add(collapsed ? L.T("Uitklappen") : L.T("Inklappen"), null, delegate { if (collapsed) ov.ExpandAll(); else ov.CollapseAll(); });
            m.Items.Add(L.T("Instellingen"), null, delegate { ov.ShowSettings(null); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("NexusPen afsluiten"), null, delegate { ov.Close(); });
            m.Show(at);
        }

        public void HideKey(string key)
        {
            List<string> keys = Layout(ov.Cfg);
            keys.Remove(key);
            ov.Cfg.BarLayout = LayoutString(keys);
            ov.Cfg.Save();
            Usage.Log("verberg", key, "rechtsklik");
            Point k = Location; Rebuild(); Location = k;
        }

        // ---- kleine vensters naast de balk
        bool IsOpen(Form f) { return f != null && !f.IsDisposed && f.Visible; }

        Rectangle ScreenRect(string key)
        {
            Item it;
            if (!byKey.TryGetValue(key, out it)) it = logoItem;
            Point p = PointToScreen(new Point((int)it.R.X, (int)it.R.Y));
            return new Rectangle(p, new Size((int)it.R.Width, (int)it.R.Height));
        }

        void TogglePopup<T2>(ref T2 popup, string key) where T2 : BarForm
        {
            bool open = IsOpen(popup);
            ClosePopups();
            if (open) return;
            if (popup == null || popup.IsDisposed)
            {
                popup = (T2)Activator.CreateInstance(typeof(T2), new object[] { ov });
                popup.Owner = this;
            }
            popup.S = S; popup.SetVertical(vertical); popup.Rebuild();
            Rectangle anchor = byKey.ContainsKey(key) ? ScreenRect(key) : ScreenRect("meer");
            popup.PlaceNear(anchor, true);
            popup.Show();
            Invalidate();
        }

        void ToggleOverflow()
        {
            bool open = IsOpen(overflowPopup);
            ClosePopups();
            if (open) return;
            if (overflowPopup == null || overflowPopup.IsDisposed) { overflowPopup = new OverflowPopup(ov); overflowPopup.Owner = this; }
            overflowPopup.Keys = new List<string>(overflow);
            overflowPopup.S = S; overflowPopup.SetVertical(vertical); overflowPopup.Rebuild();
            overflowPopup.PlaceNear(ScreenRect("meer"), true);
            overflowPopup.Show();
            Invalidate();
        }

        // Knop uit het menu met het getal: doen alsof hij in de balk zat
        public void PressFromOverflow(string key)
        {
            ClosePopups();
            Item it = AddButton(key, new RectangleF(0, 0, 1, 1));
            items.Remove(it);
            byKey.Remove(key);        // dan komt een menuutje bij het knopje met het getal
            Usage.Log("klik", key, "menu met getal");
            if (it.Click != null) it.Click();
            Invalidate();
        }

        public void ShowAllButtons()
        {
            ov.Cfg.ShortenAt = 0; ov.Cfg.Save();
            ClosePopups();
            Point k = Location; Rebuild(); Location = k;
        }

        // Voor de zelftest op GitHub
        public void OpenForTest(string key)
        {
            Item it;
            if (byKey.TryGetValue(key, out it) && it.Click != null) it.Click();
        }

        public void SnapPopupsForTest(Action<Control, string> snap, string name)
        {
            foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, overflowPopup })
                if (IsOpen(f)) snap(f, name);
        }

        public void ClosePopups()
        {
            foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, overflowPopup })
                if (IsOpen(f)) f.Hide();
            Invalidate();
        }

        public void ShowMiniBar(Rectangle sel)
        {
            if (collapsed || hiddenShot) return;
            if (miniBar == null || miniBar.IsDisposed) { miniBar = new MiniBar(ov); miniBar.Owner = this; }
            miniBar.S = W.DpiScaleAt(sel.Location);
            miniBar.Rebuild();
            Rectangle wa = Screen.FromRectangle(sel).WorkingArea;
            int x = sel.Right - miniBar.Width, y = sel.Top - miniBar.Height - (int)(8 * miniBar.S);
            if (y < wa.Top + 4) y = sel.Bottom + (int)(8 * miniBar.S);
            x = Math.Max(wa.Left + 4, Math.Min(wa.Right - miniBar.Width - 4, x));
            y = Math.Max(wa.Top + 4, Math.Min(wa.Bottom - miniBar.Height - 4, y));
            miniBar.Location = new Point(x, y);
            if (!miniBar.Visible) miniBar.Show();
            W.KeepOnTop(miniBar.Handle);
        }

        public void HideMiniBar() { if (IsOpen(miniBar)) miniBar.Hide(); }

        public void ShowWidthTip(Point screenPt, float width, float min, float max, string label)
        {
            if (collapsed) return;
            if (widthTip == null || widthTip.IsDisposed) widthTip = new WidthTip(ov);
            widthTip.S = W.DpiScaleAt(screenPt);
            widthTip.Show(screenPt, width, min, max, label);
        }

        public void ShowToast(string text, string bold, string folder)
        {
            if (collapsed) return;
            if (toast == null || toast.IsDisposed) toast = new Toast(ov);
            toast.S = W.DpiScaleAt(Location);
            toast.ShowMessage(text, bold, folder);
        }

        // ---- wegschuiven naar de rand: de tekeningen blijven staan
        void DockAway()
        {
            ClosePopups();
            HideMiniBar();
            docked = true;
            if (lipje == null || lipje.IsDisposed) lipje = new Lipje(ov, this);
            lipje.Place(Bounds, vertical);
            lipje.Show();
            Hide();
            Usage.Log("wegschuiven", "", "balk");
        }

        public void Undock()
        {
            docked = false;
            if (lipje != null) lipje.Hide();
            if (!collapsed) { Show(); W.KeepOnTop(Handle); }
        }

        // ---- inklappen: alles weg, alleen het logo blijft
        public void ShowCollapsed()
        {
            collapsed = true;
            ClosePopups(); HideMiniBar();
            if (widthTip != null) widthTip.Hide();
            if (lipje != null) lipje.Hide();
            if (tile == null || tile.IsDisposed) tile = new LogoTile(ov, this);
            Point p;
            if (ov.Cfg.TileX != -1 || ov.Cfg.TileY != -1) p = new Point(ov.Cfg.TileX, ov.Cfg.TileY);
            else p = Visible ? PointToScreen(new Point((int)logoItem.R.X, (int)logoItem.R.Y)) : new Point(Left, Top);
            tile.PlaceAt(p);
            tile.Show();
            Hide();
        }

        public void ShowExpanded()
        {
            collapsed = false;
            if (tile != null) tile.Hide();
            if (docked && lipje != null) { lipje.Show(); return; }
            Show();
            W.KeepOnTop(Handle);
        }

        bool shotWasVisible, shotLipje, shotTile;

        public void HideForShot()
        {
            hiddenShot = true;
            shotWasVisible = Visible;
            shotLipje = IsOpen(lipje);
            shotTile = IsOpen(tile);
            ClosePopups(); HideMiniBar();
            if (Visible) Hide();
            if (shotLipje) lipje.Hide();
            if (shotTile) tile.Hide();
        }

        public void ShowAfterShot()
        {
            hiddenShot = false;
            if (shotWasVisible) Show();
            if (shotLipje) lipje.Show();
            if (shotTile) tile.Show();
        }

        // Klik op het icoon in de taakbalk: in- of uitklappen
        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112, SC_MINIMIZE = 0xF020;
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_MINIMIZE) { ov.CollapseAll(); m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
    }

    // Donkere rechtsklik-menu's in de stijl van het thema
    static class Menus
    {
        public static ContextMenuStrip Make(Theme t)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.ShowImageMargin = false;
            m.Font = Fonts.Make(UiFont.Name, 13 * Math.Max(1f, W.DpiScaleAt(Control.MousePosition)), FontStyle.Bold);
            m.Renderer = new MenuRenderer(t);
            m.BackColor = t.Bar;
            m.ForeColor = t.Text;
            m.Padding = new Padding(4);
            return m;
        }

        class MenuRenderer : ToolStripProfessionalRenderer
        {
            Theme t;
            public MenuRenderer(Theme th) : base(new Colors(th)) { t = th; RoundedEdges = true; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = t.Text; base.OnRenderItemText(e); }
        }

        class Colors : ProfessionalColorTable
        {
            Theme t;
            public Colors(Theme th) { t = th; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return t.Bar; } }
            public override Color MenuBorder { get { return t.Border; } }
            public override Color MenuItemSelected { get { return t.IsLight ? ColorTranslator.FromHtml("#F4F4F5") : ColorTranslator.FromHtml("#2E2E33"); } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color SeparatorDark { get { return t.Sep; } }
            public override Color SeparatorLight { get { return t.Sep; } }
            public override Color ImageMarginGradientBegin { get { return t.Bar; } }
            public override Color ImageMarginGradientMiddle { get { return t.Bar; } }
            public override Color ImageMarginGradientEnd { get { return t.Bar; } }
        }
    }
}
