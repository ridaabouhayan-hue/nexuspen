using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ De werkbalk (Studio-balk, liggend of staand)
    // De knoppen staan in een catalogus. De volgorde (en welke erin staan) komt uit Settings.BarLayout,
    // zodat je ze in Instellingen kunt slepen en met rechtsklik kunt verbergen.
    class Toolbar : BarForm
    {
        public const string DefaultLayout = "muis,selecteer|pen,markeer,laser|vormen,tekst,gum|kleur,stempel,focus|undo,bord,foto|opslaan,oog,wis,instellingen,afsluiten,weg";

        // alle knoppen die in de balk kunnen; de losse vormen, Krijt en Opnieuw staan standaard in de bak
        public static readonly string[] Catalog = { "muis", "selecteer", "pen", "krijt", "markeer", "laser", "vormen", "lijn", "pijl", "rechthoek", "cirkel",
            "tekst", "gum", "kleur", "stempel", "focus", "undo", "redo", "wis", "bord", "foto", "opslaan", "oog", "instellingen", "afsluiten", "weg" };

        public static string NameOf(string key)
        {
            switch (key)
            {
                case "muis": return L.T("Muis");
                case "selecteer": return L.T("Selecteren");
                case "pen": return L.T("Pen");
                case "krijt": return L.T("Krijt");
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
                case "wis": return L.T("Alles wissen");
                case "bord": return L.T("Bord");
                case "foto": return L.T("Schermafbeelding");
                case "opslaan": return L.T("Opslaan");
                case "oog": return L.T("Tekeningen verbergen");
                case "instellingen": return L.T("Instellingen");
                case "afsluiten": return L.T("NexusPen afsluiten");
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
                case "wis": return "prullenbak";
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
        EraserPopup eraserPopup;
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
            S = W.UiScaleAt(start);
            vertical = ov.Cfg.Vertical;
            BarAlpha = OpacityValue;
            // vanaf 1.2.6 staat krijt niet meer standaard in de balk: stond krijt nog aan van de krijtknop, dan weer gewone pen
            if (!ov.Cfg.ChalkChecked) { ov.Cfg.ChalkChecked = true; if (!Layout(ov.Cfg).Contains("krijt")) ov.Cfg.Chalk = false; ov.Cfg.Save(); }
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
                madeInTaskbar = ov != null && ov.Cfg.InTaakbalk;
                if (madeInTaskbar) { cp.ExStyle &= ~W.WS_EX_TOOLWINDOW; cp.ExStyle |= 0x40000; }
                return cp;
            }
        }

        byte OpacityValue { get { int i = ov.Cfg.OpacityIndex; return (byte)(i == 1 ? 230 : i == 2 ? 204 : 255); } }

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
        bool madeInTaskbar;
        public void Restyle()
        {
            Point keep = Location;
            vertical = ov.Cfg.Vertical;
            BarAlpha = OpacityValue;
            ClosePopups();
            HideMiniBar();
            // het venster alleen opnieuw maken als de taakbalk-keuze verandert (dat zit in CreateParams)
            if (IsHandleCreated && madeInTaskbar != ov.Cfg.InTaakbalk) RecreateHandle();
            madeInTaskbar = ov.Cfg.InTaakbalk;
            Rebuild();
            Location = keep;
            EnsureOnScreen();
            ApplyCaptureSetting();
            foreach (BarForm f in new BarForm[] { lipje, tile, miniBar, widthTip, toast, colorPopup, shapePopup, stampPopup, boardPopup, eraserPopup, overflowPopup })
                if (f != null && !f.IsDisposed) { f.ApplyCaptureSetting(); f.S = S; }
            if (lipje != null && lipje.Visible) lipje.Restyle();
            if (tile != null && tile.Visible) tile.Rebuild();
        }

        public void Refresh2()
        {
            Invalidate();
            foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, eraserPopup, miniBar, overflowPopup })
                if (f != null && f.Visible) f.Invalidate();
        }

        public void KeepAllOnTop()
        {
            foreach (Form f in new Form[] { this, colorPopup, shapePopup, stampPopup, boardPopup, eraserPopup, overflowPopup, miniBar, lipje, tile, toast })
                if (f != null && f.Visible && f.IsHandleCreated) W.KeepOnTop(f.Handle);
        }

        // ---- opbouw
        public float BW { get { int i = ov.Cfg.SizeIndex; return (i == 0 ? 31 : i == 2 ? 44 : 36) * S; } }

        // ruimte langs de balk en dwars erop; het logo krijgt rondom dezelfde afstand tot de rand
        float PadMain { get { return (vertical ? 5 : 6) * S; } }
        float PadCross { get { return (vertical ? 6 : 4) * S; } }
        float LogoLen { get { return BW + 2 * (PadCross - PadMain); } }

        float Long(float bw, float bh) { return vertical ? bh : bw; }
        float Cross(float bw, float bh) { return vertical ? bw : bh; }
        RectangleF Rect(float pos, float cross, float len, float thick) { return vertical ? new RectangleF(cross, pos, thick, len) : new RectangleF(pos, cross, len, thick); }

        public override void Rebuild() { seps.Clear(); byKey.Clear(); buttonOrder.Clear(); gripItem = null; handleItem = null; base.Rebuild(); }

        protected override void Build()
        {
            float bw = BW, bh = vertical ? bw - 6 * S : bw, gap = 2 * S;
            float padMain = PadMain, padCross = PadCross;
            float pos = padMain, cross = padCross;
            float thick = vertical ? bw : bh;

            // geen greep meer: met het logo versleep je de balk, en een klik op het logo klapt alles in
            float lb = LogoLen;
            logoItem = Add(Rect(pos, cross, lb, thick), L.T("Klik = alles inklappen, slepen = de balk verplaatsen") + "  (" + ov.Cfg.HotkeyHide + ")", null);
            gripItem = logoItem;
            logoItem.Paint = PaintLogo;
            logoItem.RightClick = delegate { ShowLogoMenu(PointToScreen(new Point((int)logoItem.R.X, (int)logoItem.R.Bottom))); };
            pos += lb;

            List<string> layout = Layout(ov.Cfg);
            int total = 0;
            foreach (string k in layout) if (k != "|") total++;
            int limit = shortening ? Math.Max(1, Math.Min(total, shortenPreview)) : ov.Cfg.ShortenAt <= 0 || ov.Cfg.ShortenAt >= total ? total : ov.Cfg.ShortenAt;
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
                float ow = vertical ? bh : bw;
                Item more = Add(Rect(pos, cross, ow, Cross(bw, bh)), L.F("Nog {0} knoppen", overflow.Count), delegate { ToggleOverflow(); });
                more.Paint = PaintOverflow;
                byKey["meer"] = more;
                pos += ow + gap;
            }
            // sleeprandje om de balk in te korten
            float hl = 12 * S;
            handleItem = new Item(); handleItem.R = Px(Rect(pos, cross, hl, thick)); handleItem.Paint = PaintHandle;
            handleItem.Tip = L.T("Sleep om de balk in te korten");
            pos += hl + padMain - 2 * S;

            float tot = thick + padCross * 2;
            ClientSize = vertical ? new Size((int)Math.Ceiling(tot), (int)Math.Ceiling(pos)) : new Size((int)Math.Ceiling(pos), (int)Math.Ceiling(tot));
        }

        void AddSep(ref float pos, float cross, float thick)
        {
            float m = (vertical ? 4 : 5) * S;
            Item sep = new Item();
            float one = Math.Max(1, Px(S));   // streepje van hele pixels: scherp
            sep.R = vertical ? new RectangleF(Px(cross + thick / 2 - 11 * S), Px(pos + m), Px(22 * S), one) : new RectangleF(Px(pos + m), Px(cross + thick / 2 - 11 * S), one, Px(22 * S));
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
                case "pen":
                    it = Button(r, "pen", Shortcuts.Tip(L.T("Pen"), "pen"), delegate { Use(key); ClosePopups(); if (Layout(o.Cfg).Contains("krijt")) { o.Cfg.Chalk = false; o.Cfg.Save(); } o.SetTool(Tool.Pen); o.RefreshSettingsWindow(); },
                        delegate { return o.DrawMode && o.CurrentTool == Tool.Pen && !(o.Cfg.Chalk && Layout(o.Cfg).Contains("krijt")); }, null);
                    break;
                case "krijt":
                    it = Button(r, "krijt", L.T("Krijt: een pen met een korrelige streep, zoals krijt op een bord"), delegate { Use(key); ClosePopups(); o.Cfg.Chalk = true; o.Cfg.Save(); o.SetTool(Tool.Pen); o.RefreshSettingsWindow(); },
                        delegate { return o.DrawMode && o.CurrentTool == Tool.Pen && o.Cfg.Chalk; }, null);
                    break;
                case "markeer": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Markeerstift"), "markeer"), Tool.Marker); break;
                case "laser": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Laserpen, verdwijnt vanzelf"), "laser"), Tool.Laser); break;
                case "lijn": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Lijn"), "lijn"), Tool.Line); break;
                case "pijl": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Pijl"), "pijl"), Tool.Arrow); break;
                case "rechthoek": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Rechthoek"), "rechthoek"), Tool.Rect); break;
                case "cirkel": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Cirkel"), "cirkel"), Tool.Ellipse); break;
                case "tekst": it = ToolBtn(r, key, Shortcuts.Tip(L.T("Tekst"), "tekst"), Tool.Text); break;
                case "gum":
                    it = Button(r, "gum", Shortcuts.Tip(L.T("Gum, of rechtermuisknop ingedrukt. Muiswiel of knijpen = groter of kleiner"), "gum"),
                        delegate { Use(key); ClosePopups(); o.SetTool(Tool.Eraser); }, delegate { return o.DrawMode && o.CurrentTool == Tool.Eraser; }, null);
                    break;
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
                    it = Button(r, "focus", Shortcuts.Tip(L.T("Spotlicht: alleen rond je muis is het licht. Muiswiel = groter of kleiner. Rechtsklik = rond of rechthoek"), "focus"),
                        delegate { Use(key); ClosePopups(); o.ToggleFocus(); }, delegate { return o.DrawMode && o.CurrentTool == Tool.Focus; }, null);
                    break;
                case "undo":
                    it = Button(r, "undo", Shortcuts.Tip(L.T("Ongedaan maken"), "undo") + ". " + Shortcuts.Tip(L.T("Rechtsklik = opnieuw"), "redo"), delegate { Use(key); o.Undo(); }, null, null);
                    break;
                case "wis":
                    it = Button(r, "prullenbak", L.T("Alles wissen") + "  (Delete). " + L.T("Terug met Ctrl+Z"), delegate { Use(key); o.ClearAll(); }, null, null);
                    break;
                case "redo":
                    it = Button(r, "redo", Shortcuts.Tip(L.T("Opnieuw"), "redo"), delegate { Use(key); o.Redo(); }, null, null);
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
                    it = Button(r, "opslaan", Shortcuts.Tip(L.T("Opslaan: je tekening staat er de volgende keer weer"), "opslaan"),
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
                case "afsluiten":
                    it = Button(r, "afsluiten", L.T("NexusPen afsluiten. Je tekening wordt eerst bewaard"), delegate { Use(key); ClosePopups(); ov.AskQuit(); }, null, null);
                    it.Muted = true;
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
            using (SolidBrush b = new SolidBrush(T.Sep)) foreach (Item s in seps) g.FillRectangle(b, s.R);
            if (handleItem != null) PaintHandle(g, handleItem, hoverHandle || shortening);
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
            if (h) using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(Highlight(it.R), 7 * S)) g.FillPath(b, gp);
            float size = (float)Math.Round(BW * 26f / 38f);
            RectangleF r = new RectangleF(it.R.X + (it.R.Width - size) / 2, it.R.Y + (it.R.Height - size) / 2, size, size);
            Logo.Draw(g, r, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
        }

        void PaintColorButton(Graphics g, Item it, bool h)
        {
            Theme t = T;
            bool open = IsOpen(colorPopup);
            // net als de andere knoppen: alleen een achtergrond als je erover beweegt of als het venster open is
            if (h || open)
                using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(Highlight(it.R), 7 * S)) g.FillPath(b, gp);
            // alleen een gekleurd rondje, ongeveer even groot als een icoontje (zonder witte ring)
            float d = Px(IconSize * 0.85f);
            float cx = Px(it.R.X + it.R.Width / 2), cy = Px(it.R.Y + it.R.Height / 2);
            using (SolidBrush b = new SolidBrush(ov.CurrentColor)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            if (Theme.Luma(ov.CurrentColor) < 0.15 || Theme.Luma(ov.CurrentColor) > 0.9)   // zwart of wit: een dun randje om hem te zien
                using (Pen p = new Pen(Color.FromArgb(120, t.Icon), 1)) g.DrawEllipse(p, cx - d / 2, cy - d / 2, d, d);
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
            // gewoon een knop met pijltjes, zoals de andere knoppen (zonder rand en zonder getal)
            Theme t = T;
            bool open = IsOpen(overflowPopup);
            if (h || open)
                using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(Highlight(it.R), 7 * S)) g.FillPath(b, gp);
            Icons.Draw(g, vertical ? "neer" : "meer", it.R, IconSize, t.Icon, StrokeW);
        }

        // ---- inkorten met het sleeprandje
        bool hoverHandle;
        int shortenTotal;
        float shortenOffset;   // afstand tussen de muis en het begin van het randje bij het vastpakken

        // Waar zou het randje beginnen met n knoppen? Zelfde rekensom als Build, maar zonder iets te tekenen.
        float HandleStart(int n)
        {
            float bw = BW, bh = vertical ? bw - 6 * S : bw, gap = 2 * S;
            float m = (vertical ? 4 : 5) * S, sepW = m * 2 + 1 * S;
            float pos = PadMain + LogoLen;
            int placed = 0, rest = 0;
            bool needSep = true;
            foreach (string k in Layout(ov.Cfg))
            {
                if (k == "|") { needSep = true; continue; }
                if (placed >= n) { rest++; continue; }
                if (needSep) { pos += sepW; needSep = false; }
                pos += Long(bw, bh) + gap;
                placed++;
            }
            if (rest > 0) pos += sepW + (vertical ? bh : bw) + gap;
            return pos;
        }

        bool logoDown;
        Point logoDownAt;

        protected override bool OnPress(MouseEventArgs e)
        {
            // het logo: klikken = inklappen, slepen = de balk verplaatsen
            if (e.Button == MouseButtons.Left && logoItem != null && logoItem.R.Contains(e.Location))
            {
                logoDown = true; logoDownAt = e.Location; Capture = true;
                return true;
            }
            if (e.Button == MouseButtons.Left && handleItem != null && RectangleF.Inflate(handleItem.R, 4 * S, 4 * S).Contains(e.Location))
            {
                // eerst de hele balk opmeten (waar zou elke knop staan), dan laten we hem live groeien of krimpen
                // het randje volgt de muis; de balk groeit of krimpt per knop, zonder eerst de hele balk te tonen
                Capture = true;
                ClosePopups();
                shortenTotal = 0;
                foreach (string k in Layout(ov.Cfg)) if (k != "|") shortenTotal++;
                shortenPreview = ov.Cfg.ShortenAt > 0 ? Math.Min(ov.Cfg.ShortenAt, shortenTotal) : shortenTotal;
                shortenOffset = (vertical ? e.Y : e.X) - HandleStart(shortenPreview);
                shortening = true;
                return true;
            }
            return false;
        }

        // ---- zelf slepen: tegen de zijkant wordt de balk meteen staand, tegen boven of onder meteen liggend
        bool dragging;
        Point grab;

        protected override bool BeginCustomDrag(MouseEventArgs e)
        {
            dragging = true; grab = e.Location; Capture = true;
            ClosePopups(); HideMiniBar();
            return true;
        }

        void DragMove()
        {
            Point m = Control.MousePosition;
            Rectangle wa = Screen.FromPoint(m).WorkingArea;
            int edge = (int)(28 * S);
            // alleen aan een echte buitenrand (niet de naad tussen twee schermen), en alleen als de balk in de andere stand past
            bool nl = m.X - wa.Left < edge && Outer(new Point(wa.Left - 4, m.Y)), nr = wa.Right - m.X < edge && Outer(new Point(wa.Right + 4, m.Y));
            bool nt = m.Y - wa.Top < edge && Outer(new Point(m.X, wa.Top - 4)), nb = wa.Bottom - m.Y < edge && Outer(new Point(m.X, wa.Bottom + 4));
            bool want = vertical;
            if (!vertical && (nl || nr) && !nt && !nb && Width <= wa.Height - 20) want = true;
            else if (vertical && (nt || nb) && !nl && !nr && Height <= wa.Width - 20) want = false;
            if (want != vertical)
            {
                vertical = want; ov.Cfg.Vertical = want;
                Rebuild();
                // de muis houdt de balk op ongeveer dezelfde plek vast (bij de greep)
                grab = gripItem != null ? new Point((int)(gripItem.R.X + gripItem.R.Width / 2), (int)(gripItem.R.Y + gripItem.R.Height / 2)) : new Point(Width / 2, Height / 2);
                Usage.Log("stand", want ? "staand" : "liggend", "slepen naar de rand");
            }
            int x = m.X - grab.X, y = m.Y - grab.Y;
            int th = (int)(12 * S);
            if (Math.Abs(x - wa.Left) < th) x = wa.Left; else if (Math.Abs(wa.Right - (x + Width)) < th) x = wa.Right - Width;
            if (Math.Abs(y - wa.Top) < th) y = wa.Top; else if (Math.Abs(wa.Bottom - (y + Height)) < th) y = wa.Bottom - Height;
            x = Math.Max(wa.Left, Math.Min(wa.Right - Width, x));
            y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, y));
            Location = new Point(x, y);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture) EndDrag();
            if (logoDown && !Capture) logoDown = false;
            if (shortening && !Capture) { shortening = false; Rebuild(); }   // muis kwijt (Alt+Tab): terug naar de bewaarde stand
        }

        // Ligt hier geen ander scherm? Dan is dit een echte rand.
        static bool Outer(Point p)
        {
            foreach (Screen sc in Screen.AllScreens) if (sc.Bounds.Contains(p)) return false;
            return true;
        }

        void EndDrag()
        {
            dragging = false;
            ov.Cfg.ToolbarX = Left; ov.Cfg.ToolbarY = Top; ov.Cfg.Save();
            ov.RefreshSettingsWindow();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) { DragMove(); Cursor = Cursors.SizeAll; return; }
            if (logoDown)
            {
                if (Math.Abs(e.X - logoDownAt.X) + Math.Abs(e.Y - logoDownAt.Y) > 4)
                {
                    logoDown = false;
                    BeginCustomDrag(new MouseEventArgs(MouseButtons.Left, 1, logoDownAt.X, logoDownAt.Y, 0));
                    DragMove();
                }
                return;
            }
            if (shortening)
            {
                // een knop erbij of eraf zodra je de muis voorbij het midden van die knop sleept
                float p = (vertical ? e.Y : e.X) - shortenOffset;
                int n = shortenPreview;
                float best = float.MaxValue;
                for (int i = 1; i <= shortenTotal; i++)
                {
                    float d = Math.Abs(HandleStart(i) - p);
                    if (d < best - 0.5f) { best = d; n = i; }
                }
                if (n != shortenPreview) { shortenPreview = n; Rebuild(); }
                return;
            }
            bool hh = handleItem != null && RectangleF.Inflate(handleItem.R, 4 * S, 4 * S).Contains(e.Location);
            if (hh != hoverHandle) { hoverHandle = hh; Invalidate(); if (hh && ov.Cfg.ShowTips) tip.Show(handleItem.Tip, this, TipPoint(handleItem), 2500); }
            base.OnMouseMove(e);
            HoverCheck();   // na base: dan weten we over welke knop je nu bent
            if (hh) Cursor = vertical ? Cursors.SizeNS : Cursors.SizeWE;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { Capture = false; if (dragging) EndDrag(); return; }
            if (logoDown) { logoDown = false; Capture = false; if (logoItem != null && logoItem.R.Contains(e.Location)) ov.CollapseAll(); return; }
            if (!shortening) return;
            shortening = false;
            Capture = false;
            ov.Cfg.ShortenAt = shortenPreview >= shortenTotal ? 0 : shortenPreview;
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
            if (it.Key == "focus")
            {
                ToolStripMenuItem rond = new ToolStripMenuItem(L.T("Rond"), null, delegate { ov.SetFocusShape(0); });
                ToolStripMenuItem recht = new ToolStripMenuItem(L.T("Rechthoek (om te lezen)"), null, delegate { ov.SetFocusShape(1); });
                rond.Checked = ov.Cfg.FocusShape == 0; recht.Checked = ov.Cfg.FocusShape == 1;
                m.Items.Add(rond); m.Items.Add(recht);
                m.Items.Add(new ToolStripSeparator());
            }
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
            m.Items.Add(L.T("NexusPen afsluiten"), null, delegate { ov.AskQuit(); });
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
            ov.RefreshSettingsWindow();
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
            StopHoverOpen();   // een klik telt, niet meer het wachtende 'erover bewegen'
            // al open door erover te bewegen: een klik laat hem gewoon open (dan sluit hij niet meer vanzelf)
            if (open && popup != null && (object)popup == (object)hoverPopup) { hoverPopup = null; return; }
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
            ov.RefreshSettingsWindow();
        }

        // ---- vensters openen door er alleen over te bewegen (vormen, kleur, stempel, bord)
        System.Windows.Forms.Timer hoverOpen, hoverClose;
        string hoverKey;
        BarForm hoverPopup;
        string hoverPopupKey;
        int leaveTicks;

        void StopHoverOpen() { if (hoverOpen != null) hoverOpen.Stop(); hoverKey = null; }

        static bool HasPopup(string k) { return k == "vormen" || k == "kleur" || k == "stempel" || k == "bord"; }

        void HoverCheck()
        {
            string k = hover != null ? hover.Key : null;
            if (k != null && HasPopup(k))
            {
                if (k == hoverKey) return;
                hoverKey = k;
                if (hoverOpen == null)
                {
                    hoverOpen = new System.Windows.Forms.Timer(); hoverOpen.Interval = 280;
                    hoverOpen.Tick += delegate { hoverOpen.Stop(); if (hover != null && hover.Key == hoverKey) OpenByHover(hoverKey); };
                }
                hoverOpen.Stop(); hoverOpen.Start();
            }
            else { hoverKey = null; if (hoverOpen != null) hoverOpen.Stop(); }
        }

        void OpenByHover(string k)
        {
            BarForm f = k == "vormen" ? (BarForm)shapePopup : k == "kleur" ? (BarForm)colorPopup : k == "stempel" ? (BarForm)stampPopup : k == "gum" ? (BarForm)eraserPopup : boardPopup;
            if (IsOpen(f)) return;
            switch (k)
            {
                case "vormen": TogglePopup(ref shapePopup, k); f = shapePopup; break;
                case "kleur": TogglePopup(ref colorPopup, k); f = colorPopup; break;
                case "stempel": TogglePopup(ref stampPopup, k); f = stampPopup; break;
                case "gum": TogglePopup(ref eraserPopup, k); f = eraserPopup; break;
                default: TogglePopup(ref boardPopup, k); f = boardPopup; break;
            }
            hoverPopup = f; hoverPopupKey = k; leaveTicks = 0;
            if (hoverClose == null)
            {
                hoverClose = new System.Windows.Forms.Timer(); hoverClose.Interval = 150;
                hoverClose.Tick += delegate { HoverCloseTick(); };
            }
            hoverClose.Start();
        }

        // Ben je met de muis weg van de knop en het venster, dan gaat het venster na een halve seconde dicht
        void HoverCloseTick()
        {
            if (hoverPopup == null || hoverPopup.IsDisposed || !hoverPopup.Visible) { hoverPopup = null; hoverClose.Stop(); return; }
            if (Control.MouseButtons != MouseButtons.None) { leaveTicks = 0; return; }   // je sleept nog (bijvoorbeeld het schuifje)
            Point m = Control.MousePosition;
            // het venster, de knop, en de strook ertussen
            Rectangle pr = hoverPopup.Bounds, br = byKey.ContainsKey(hoverPopupKey) ? ScreenRect(hoverPopupKey) : Bounds;
            pr.Inflate((int)(10 * S), (int)(10 * S)); br.Inflate((int)(4 * S), (int)(4 * S));
            bool over = pr.Contains(m) || br.Contains(m);
            // de strook tussen knop en venster (zo breed als de knop), zodat je er rustig naartoe kunt
            Rectangle bridge = vertical ? Rectangle.FromLTRB(Math.Min(br.Right, pr.Left), br.Top, Math.Max(br.Right, pr.Left), br.Bottom)
                                        : Rectangle.FromLTRB(br.Left, Math.Min(br.Bottom, pr.Top), br.Right, Math.Max(br.Bottom, pr.Top));
            if (over || bridge.Contains(m)) { leaveTicks = 0; return; }
            if (++leaveTicks >= 4) { hoverPopup.Hide(); hoverPopup = null; hoverClose.Stop(); Invalidate(); }
        }

        // De uitleg van een knop (voor de pagina "Wat doet elke knop" in Instellingen)
        public string TipFor(string key)
        {
            Item old;
            bool had = byKey.TryGetValue(key, out old);
            Item it = AddButton(key, new RectangleF(0, 0, 1, 1));
            items.Remove(it);
            if (had) byKey[key] = old; else byKey.Remove(key);
            return it.Tip ?? "";
        }

        // Kleine vensters alvast klaarzetten (onzichtbaar), zodat ze bij de eerste klik meteen openen
        public void Prewarm()
        {
            try
            {
                if (colorPopup == null) { colorPopup = new ColorPopup(ov); colorPopup.Owner = this; }
                if (shapePopup == null) { shapePopup = new ShapePopup(ov); shapePopup.Owner = this; }
                if (stampPopup == null) { stampPopup = new StampPopup(ov); stampPopup.Owner = this; }
                if (boardPopup == null) { boardPopup = new BoardPopup(ov); boardPopup.Owner = this; }
                foreach (BarForm f in new BarForm[] { colorPopup, shapePopup, stampPopup, boardPopup })
                {
                    f.S = S; f.SetVertical(vertical);
                    IntPtr h = f.Handle;   // venster aanmaken zonder te tonen
                    f.Rebuild();
                    using (Bitmap b = f.RenderBitmap()) { }
                }
            }
            catch (Exception ex) { Log.Error("Vensters klaarzetten", ex); }
        }

        // Voor de zelftest op GitHub
        public void OpenForTest(string key)
        {
            Item it;
            if (byKey.TryGetValue(key, out it) && it.Click != null) it.Click();
        }

        public void SnapPopupsForTest(Action<Control, string> snap, string name)
        {
            foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, eraserPopup, overflowPopup })
                if (IsOpen(f)) snap(f, name);
        }

        // Staat er een klein venster van de balk open (kleur, vormen, stempel, bord, verborgen knoppen)?
        public bool AnyPopupOpen
        {
            get
            {
                foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, eraserPopup, overflowPopup })
                    if (IsOpen(f)) return true;
                return false;
            }
        }

        public void ClosePopups()
        {
            StopHoverOpen();
            bool any = false;
            foreach (Form f in new Form[] { colorPopup, shapePopup, stampPopup, boardPopup, eraserPopup, overflowPopup })
                if (IsOpen(f)) { f.Hide(); any = true; }
            if (any) Invalidate();   // bij slepen van de balk niet elke beweging opnieuw tekenen
        }

        public void ShowMiniBar(Rectangle sel) { ShowMiniBar(sel, false); }

        // keepPlace: het balkje blijft staan waar het staat (bij groter of kleiner maken), zodat je knop onder je muis blijft
        public void ShowMiniBar(Rectangle sel, bool keepPlace)
        {
            if (collapsed || hiddenShot) return;
            if (keepPlace && IsOpen(miniBar)) { Point keep = miniBar.Location; miniBar.Rebuild(); miniBar.Location = keep; return; }
            if (miniBar == null || miniBar.IsDisposed) { miniBar = new MiniBar(ov); miniBar.Owner = this; }
            miniBar.S = W.UiScaleAt(sel.Location);
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
            widthTip.S = W.UiScaleAt(screenPt);
            widthTip.Show(screenPt, width, min, max, label);
        }

        public void ShowToast(string text, string bold, string folder)
        {
            if (collapsed) return;
            if (toast == null || toast.IsDisposed) toast = new Toast(ov);
            toast.S = W.UiScaleAt(Location);
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

        // Balk op deze plek zetten, maar helemaal binnen het scherm, en de plek onthouden
        void PlaceInside(Point p, Point onScreen)
        {
            Rectangle wa = Screen.FromPoint(onScreen).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(wa.Right - Width, p.X)), y = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, p.Y));
            Location = new Point(x, y);
            ov.Cfg.ToolbarX = x; ov.Cfg.ToolbarY = y;
        }

        public void Undock()
        {
            docked = false;
            if (lipje != null && lipje.Visible && lipjeMoved)
            {
                // het lipje is versleept: de balk komt terug bij het lipje, staand aan een zijkant en liggend boven of onder
                Point c = lipje.Center;
                if (lipje.Vertical != vertical) { ov.Cfg.Vertical = lipje.Vertical; ov.ApplySettings(); }
                PlaceInside(new Point(c.X - Width / 2, c.Y - Height / 2), c);
            }
            lipjeMoved = false;
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
            int logoSize = (int)Math.Round(BW * 26f / 38f);
            int tileSide = vertical ? Width : Height;
            Point c;
            if (Visible && logoItem != null) c = PointToScreen(new Point((int)(logoItem.R.X + logoItem.R.Width / 2), (int)(logoItem.R.Y + logoItem.R.Height / 2)));   // het logo blijft precies staan
            else if (docked && lipje != null) c = new Point(lipje.Left + lipje.Width / 2, lipje.Top + lipje.Height / 2);
            else if (ov.Cfg.TileX != -1 || ov.Cfg.TileY != -1) c = new Point(ov.Cfg.TileX + tileSide / 2, ov.Cfg.TileY + tileSide / 2);
            else c = new Point(Left + tileSide / 2, Top + tileSide / 2);
            tile.PlaceAt(c, S, tileSide, logoSize);
            tile.Show();
            Hide();
        }

        public void ShowExpanded()
        {
            collapsed = false;
            if (tile != null && tile.Visible && logoItem != null && !docked)
            {
                // de balk komt terug met zijn logo precies op de plek van het logo (ook als je het logo hebt versleept),
                // maar altijd helemaal op het scherm
                Point c = tile.LogoCenter;
                PlaceInside(new Point(c.X - (int)(logoItem.R.X + logoItem.R.Width / 2), c.Y - (int)(logoItem.R.Y + logoItem.R.Height / 2)), c);
            }
            if (tile != null) tile.Hide();
            if (docked && lipje != null) { lipje.Show(); return; }
            Show();
            W.KeepOnTop(Handle);
        }

        // Na het slepen: tegen de zijkant = staand, tegen boven of onder = liggend
        void AutoOrientation()
        {
            if (IsDisposed || !Visible || collapsed || docked) return;
            Point mouse = Control.MousePosition;
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea, b = Bounds;
            // alleen als je de balk echt hebt versleept, en alleen als hij in de andere stand ook past
            if (Math.Abs(b.X - moveStart.X) + Math.Abs(b.Y - moveStart.Y) < 8) return;
            if (!vertical && b.Width > wa.Height - 40) return;
            if (vertical && b.Height > wa.Width - 40) return;
            if (!vertical && b.Width > wa.Width - 120) return;
            if (vertical && b.Height > wa.Height - 120) return;
            bool left = b.Left <= wa.Left, right = b.Right >= wa.Right, top = b.Top <= wa.Top, bottom = b.Bottom >= wa.Bottom;
            bool wantVertical = vertical;
            if (!vertical && (left || right) && !top && !bottom) wantVertical = true;
            else if (vertical && (top || bottom) && !left && !right) wantVertical = false;
            if (wantVertical == vertical) return;
            ov.Cfg.Vertical = wantVertical;
            ov.ApplySettings();   // balk opnieuw opbouwen in de andere stand
            Rectangle nb = Bounds;
            int x, y;
            if (wantVertical) { x = left ? wa.Left : wa.Right - nb.Width; y = Math.Max(wa.Top, Math.Min(wa.Bottom - nb.Height, mouse.Y - nb.Height / 2)); }
            else { y = top ? wa.Top : wa.Bottom - nb.Height; x = Math.Max(wa.Left, Math.Min(wa.Right - nb.Width, mouse.X - nb.Width / 2)); }
            Location = new Point(x, y);
            Usage.Log("stand", wantVertical ? "staand" : "liggend", "slepen naar de rand");
            ov.RefreshSettingsWindow();
        }

        bool lipjeMoved;
        public void LipjeMoved() { lipjeMoved = true; }

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
            const int WM_SYSCOMMAND = 0x0112, SC_MINIMIZE = 0xF020, WM_EXITSIZEMOVE = 0x0232;
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SC_MINIMIZE) { ov.CollapseAll(); m.Result = IntPtr.Zero; return; }
            if (m.Msg == WM_EXITSIZEMOVE) { base.WndProc(ref m); try { BeginInvoke((MethodInvoker)AutoOrientation); } catch { } return; }
            base.WndProc(ref m);
        }
    }

    // Rechtsklik-menu's in de stijl van het thema: ruim, goed leesbaar, met afgeronde hoeken
    static class Menus
    {
        public static ContextMenuStrip Make(Theme t)
        {
            float sc = Math.Max(1f, W.UiScaleAt(Control.MousePosition));
            ContextMenuStrip m = new ContextMenuStrip();
            m.ShowImageMargin = false;
            m.Font = UiFont.Make(14 * sc, false);
            m.Renderer = new MenuRenderer(t, sc);
            m.BackColor = t.Bar;
            m.ForeColor = t.Text;
            m.Padding = new Padding((int)(6 * sc));
            m.Opening += delegate
            {
                // zit er een vinkje in het menu, dan krijgt elke regel dezelfde ruimte links (anders springen de teksten heen en weer)
                bool checks = false;
                foreach (ToolStripItem it in m.Items) { ToolStripMenuItem mi = it as ToolStripMenuItem; if (mi != null && (mi.Checked || mi.CheckOnClick)) checks = true; }
                m.ShowCheckMargin = checks;
                int n = m.Items.Count;
                for (int i = 0; i < n; i++)
                {
                    ToolStripItem it = m.Items[i];
                    if (it is ToolStripSeparator) { it.Margin = new Padding(0, (int)(4 * sc), 0, (int)(4 * sc)); continue; }
                    it.Padding = new Padding((int)((checks ? 4 : 10) * sc), (int)(7 * sc), (int)(18 * sc), (int)(7 * sc));
                    // de eerste en laatste regel net zo ver van de rand als van elkaar
                    it.Margin = new Padding(0, (int)((i == 0 ? 3 : 1) * sc), 0, (int)((i == n - 1 ? 3 : 1) * sc));
                }
            };
            // Windows 11: echt afgeronde hoeken voor het menu
            m.Opened += delegate { try { W.RoundCorners(m.Handle, t.Border); } catch { } };
            m.Closed += delegate { try { m.BeginInvoke((MethodInvoker)delegate { Font f = m.Font; m.Dispose(); f.Dispose(); }); } catch { } };
            return m;
        }

        class MenuRenderer : ToolStripProfessionalRenderer
        {
            Theme t; float sc;
            public MenuRenderer(Theme th, float s) : base(new Colors(th)) { t = th; sc = s; RoundedEdges = true; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? t.Text : t.Muted;
                base.OnRenderItemText(e);
            }

            // de regel onder de muis: een afgerond vlakje, zoals de knoppen in de balk
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(2 * sc, 1 * sc, e.Item.Width - 4 * sc, e.Item.Height - 2 * sc);
                using (SolidBrush b = new SolidBrush(t.Hover)) using (GraphicsPath gp = Svg.RoundRect(r, 6 * sc)) g.FillPath(b, gp);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = e.ImageRectangle;
                float sz = 16 * sc;
                Icons.Draw(g, "vink", new RectangleF(r.X + (r.Width - sz) / 2, r.Y + (r.Height - sz) / 2, sz, sz), sz, t.Accent, 2.2f);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(t.Border, 1)) using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8 * sc)) g.DrawPath(p, gp);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen p = new Pen(t.Sep, 1)) e.Graphics.DrawLine(p, 8 * sc, y, e.Item.Width - 8 * sc, y);
            }
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
