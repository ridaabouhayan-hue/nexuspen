using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexusPen
{
    // ------------------------------------------------------------------ Instellingen (eigen tekenwerk, volgt het thema, alle teksten vertaald)
    class SettingsForm : Form
    {
        public static readonly string[] PageIds = { "Algemeen", "Taal", "Uiterlijk", "Werkbalk", "Pen", "Tekst", "Stempels", "Bord", "Voorlezen", "Sneltoetsen", "Opslaan", "Schermdelen", "Gebruik", "Knoppen", "Bijwerken", "Hulp" };
        static readonly string[] PageNames = { "Algemeen", "Taal", "Uiterlijk", "Werkbalk", "Pen en kleuren", "Tekst", "Stempels", "Bord en achtergrond", "Voorlezen", "Sneltoetsen", "Opslaan", "Schermdelen", "Gebruik", "Wat doet elke knop", "Bijwerken", "Hulp en foutlog" }; // vertaal
        static readonly string[] PageIcons = { "huis", "taal", "kleuren", "rechthoek", "pen", "tekst", "stempel", "bord", "voorlezen", "toetsen", "opslaan", "schermdelen", "grafiek", "muis", "terugzetten", "info" };
        // na deze pagina's een dun streepje in de zijbalk: basis, tekenen, bewaren en delen, hulp
        static readonly string[] GroupEnds = { "Werkbalk", "Voorlezen", "Schermdelen" };
        static readonly string[] FontChoices = { "Segoe UI", "Andika", "Atkinson Hyperlegible", "Lexend", "Nunito", "Patrick Hand" };
        static readonly string[] FontDescs = { "Standaard van Windows", "Gemaakt voor beginnende lezers en Alfa", "Extra goed leesbaar, ook voor slechtzienden", "Rustig en ruim, prettig bij lange zinnen", "Rond en vriendelijk, de huisstijl van Nexus", "Handschrift, toch goed te lezen" }; // vertaal

        Overlay ov;
        List<Item> items = new List<Item>();
        Item hover;
        ToolTip tip = new ToolTip();
        float S;
        int page;
        string notice;
        DateTime noticeAt;
        List<Action<Graphics>> painters = new List<Action<Graphics>>();
        float contentX, contentW, contentTop;

        // sneltoets opnemen
        string recording;      // id van de sneltoets die je aan het wijzigen bent

        // slepen in Werkbalk
        string dragKey; int dragFrom = -1; Point dragPos; bool dragActive;
        List<RectangleF> barSlots = new List<RectangleF>();
        RectangleF barArea, trayArea;

        public SettingsForm(Overlay o)
        {
            ov = o;
            Text = L.T("NexusPen instellingen");
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Rectangle wa = Screen.FromPoint(Control.MousePosition).WorkingArea;
            S = W.UiScaleAt(new Point(wa.X + wa.Width / 2, wa.Y + wa.Height / 2));   // groeit mee op een groot scherm
            // past altijd op het scherm, ook op een kleine laptop
            float fit = Math.Min((wa.Width - 40) / 1040f, (wa.Height - 40) / 700f);
            if (fit < S) S = Math.Max(0.75f, fit);
            ClientSize = new Size((int)(1040 * S), (int)(700 * S));
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            try { Theme t = ov.Theme; Icon = Logo.MakeIcon(32, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent)); } catch { }
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 14 * S)) Region = new Region(gp);
            KeyDown += OnKey;
            L.Changed += LangChanged;
            FormClosed += delegate { L.Changed -= LangChanged; };
        }

        void LangChanged() { if (IsDisposed) return; Text = L.T("NexusPen instellingen"); BuildItems(); Invalidate(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.HideFromCapture(Handle, ov.Cfg.HideFromCapture);
            // Windows 11 maakt de hoeken zelf glad rond; op Windows 10 blijft de oude vorm
            if (W.RoundCorners(Handle, ov.Theme.Border)) { dwmCorners = true; Region = null; }
        }

        bool dwmCorners;

        public void Rebuild() { if (dwmCorners && IsHandleCreated) W.RoundCorners(Handle, ov.Theme.Border); BuildItems(); Invalidate(); }

        public void ShowPage(string id)
        {
            int i = Array.IndexOf(PageIds, id);
            if (i < 0) return;
            page = i; recording = null; scroll = 0;
            BuildItems();
            Invalidate();
        }

        Theme T { get { return ov.Theme; } }
        Settings Cfg { get { return ov.Cfg; } }
        Color Card { get { return T.IsLight ? ColorTranslator.FromHtml("#F4F4F5") : ColorTranslator.FromHtml("#1F1F23"); } }
        Color Field { get { return T.Chip; } }
        Color Raised { get { return T.IsLight ? ColorTranslator.FromHtml("#E4E4E7") : ColorTranslator.FromHtml("#2E2E33"); } }
        Color Side { get { return T.IsLight ? ColorTranslator.FromHtml("#F4F4F5") : ColorTranslator.FromHtml("#131316"); } }

        void Changed()
        {
            ov.ApplySettings();
            try { Theme t = T; Icon old = Icon; Icon = Logo.MakeIcon(32, t.Accent, Logo.LetterColor(t.Accent, t.OnAccent)); if (old != null) old.Dispose(); } catch { }
            Invalidate();
        }

        void Notice(string text)
        {
            notice = text; noticeAt = DateTime.Now; Invalidate();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 3200;
            t.Tick += delegate { t.Stop(); t.Dispose(); if (!IsDisposed) Invalidate(); };
            t.Start();
        }

        // ---- tekenhulpjes
        Font F(float px, bool bold) { return UiFont.Make(px * S, bold); }

        // Tekst via Windows zelf (ClearType): scherp en met de normale letterafstand
        void Txt(Graphics g, string text, RectangleF r, Color c, float px, bool bold, StringAlignment align, bool wrap)
        {
            Txt(g, text, r, c, px, bold, align, wrap, T.Bar);
        }

        // bg: de kleur waar de tekst echt op staat, zodat de randjes van de letters goed mengen
        void Txt(Graphics g, string text, RectangleF r, Color c, float px, bool bold, StringAlignment align, bool wrap, Color bg)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (c.A < 255)
            {
                // Windows tekent tekst altijd dekkend: doorzichtige kleur zelf mengen met de achtergrond
                c = Mix(bg, c);
            }
            TextFormatFlags fl = TextFormatFlags.NoPrefix;
            fl |= align == StringAlignment.Center ? TextFormatFlags.HorizontalCenter : align == StringAlignment.Far ? TextFormatFlags.Right : TextFormatFlags.Left;
            if (wrap) fl |= TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.EndEllipsis;
            else fl |= TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            Rectangle ri = Rectangle.FromLTRB((int)Math.Round(r.Left), (int)Math.Round(r.Top), (int)Math.Round(r.Right), (int)Math.Round(r.Bottom));
            using (Font f = F(px, bold)) TextRenderer.DrawText(g, text, f, ri, c, fl);
        }

        // een doorzichtige kleur over een dekkende: wat je echt ziet
        static Color Mix(Color under, Color over)
        {
            float a = over.A / 255f;
            return Color.FromArgb((int)(over.R * a + under.R * (1 - a)), (int)(over.G * a + under.G * (1 - a)), (int)(over.B * a + under.B * (1 - a)));
        }

        float Measure(string text, float px, bool bold)
        {
            using (Font f = F(px, bold)) return TextRenderer.MeasureText(text ?? "", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        }

        Item Add(RectangleF r, string tipText, Action click, Action<Graphics, Item, bool> paint)
        {
            Item it = new Item(); it.R = r; it.Tip = tipText; it.Click = click; it.Paint = paint;
            items.Add(it);
            if (buildingContent) Mark(r.Bottom);
            return it;
        }

        void Later(Action<Graphics> p) { painters.Add(p); }

        // ---- scrollen: een pagina mag langer zijn dan het venster
        List<float> navSeps = new List<float>();
        float scroll, contentEnd;      // hoe ver je naar beneden bent gescrold, en waar de pagina ophoudt
        int fixedItems;                // de eerste items (zijbalk en kruisje) scrollen niet mee
        bool buildingContent;
        void Mark(float y) { if (y > contentEnd) contentEnd = y; }
        float MaxScroll { get { return Math.Max(0, contentEnd + scroll + 30 * S - ClientSize.Height); } }

        // voor de zelftest: helemaal naar beneden
        public void ScrollToEnd() { BuildItems(); scroll = MaxScroll; BuildItems(); Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (dragActive || pressed != null) return;
            float before = scroll;
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta / 120f * 56 * S));
            if (Math.Abs(scroll - before) > 0.5f) { BuildItems(); Invalidate(); }
        }

        void RoundFill(Graphics g, RectangleF r, Color c, float rad)
        {
            RectangleF rr = RectangleF.FromLTRB((float)Math.Round(r.Left), (float)Math.Round(r.Top), (float)Math.Round(r.Right), (float)Math.Round(r.Bottom));
            using (SolidBrush b = new SolidBrush(c)) using (GraphicsPath gp = Svg.RoundRect(rr, rad * S)) g.FillPath(b, gp);
        }
        // Randen van hele pixels, precies op de pixels: strak in plaats van rafelig
        void RoundLine(Graphics g, RectangleF r, Color c, float w, float rad)
        {
            float pw = Math.Max(1, (float)Math.Round(w * S));
            RectangleF rr = RectangleF.FromLTRB((float)Math.Round(r.Left), (float)Math.Round(r.Top), (float)Math.Round(r.Right), (float)Math.Round(r.Bottom));
            rr.Inflate(-pw / 2, -pw / 2);
            using (Pen p = new Pen(c, pw)) using (GraphicsPath gp = Svg.RoundRect(rr, rad * S - pw / 2)) g.DrawPath(p, gp);
        }

        float Heading(float y, string title, string sub)
        {
            float x = contentX, w = contentW;
            Mark(y + (sub != null ? 70 * S : 44 * S));
            Later(delegate(Graphics g)
            {
                Txt(g, title, new RectangleF(x, y, w, 30 * S), T.Text, 22, true, StringAlignment.Near, false);
                if (sub != null) Txt(g, sub, new RectangleF(x, y + 34 * S, w, 40 * S), T.Muted, 13, false, StringAlignment.Near, true);
            });
            return y + (sub != null ? 70 * S : 44 * S);
        }

        float Section(float y, string title)
        {
            float x = contentX, w = contentW;
            Later(delegate(Graphics g) { Txt(g, title, new RectangleF(x, y, w, 18 * S), T.Accent, 12, true, StringAlignment.Near, false); });
            Mark(y + 24 * S);
            return y + 24 * S;
        }

        float Para(float y, string text, float h)
        {
            float x = contentX, w = contentW;
            Later(delegate(Graphics g) { Txt(g, text, new RectangleF(x, y, w, h), T.Muted, 13, false, StringAlignment.Near, true); });
            Mark(y + h);
            return y + h;
        }

        // Regel met titel, uitleg en rechts een schakelaar
        float Toggle(float y, string title, string sub, Func<bool> get, Action<bool> set)
        {
            float x = contentX, w = contentW, h = 52 * S;
            Item it = Add(new RectangleF(x, y, w, h), null, delegate { set(!get()); Changed(); }, delegate(Graphics g, Item item, bool hv)
            {
                Theme t = T;
                if (hv) RoundFill(g, item.R, t.Hover, 8);
                Txt(g, title, new RectangleF(item.R.X + 10 * S, item.R.Y + 7 * S, item.R.Width - 90 * S, 20 * S), t.Text, 14, true, StringAlignment.Near, false);
                if (sub != null) Txt(g, sub, new RectangleF(item.R.X + 10 * S, item.R.Y + 27 * S, item.R.Width - 90 * S, 18 * S), t.Muted, 12, false, StringAlignment.Near, false);
                DrawSwitch(g, new RectangleF(item.R.Right - 54 * S, item.R.Y + (h - 24 * S) / 2, 44 * S, 24 * S), get());
            });
            Later(delegate(Graphics g) { using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, x, y + h, w, 1 * S); });
            return y + h + 2 * S;
        }

        void DrawSwitch(Graphics g, RectangleF r, bool on)
        {
            Theme t = T;
            RoundFill(g, r, on ? t.Accent : (t.IsLight ? ColorTranslator.FromHtml("#D4D4D8") : ColorTranslator.FromHtml("#3F3F46")), 12);
            float d = r.Height - 6 * S;
            using (SolidBrush b = new SolidBrush(on ? t.OnAccent : (t.IsLight ? Color.White : ColorTranslator.FromHtml("#A1A1AA"))))
                g.FillEllipse(b, on ? r.Right - d - 3 * S : r.X + 3 * S, r.Y + 3 * S, d, d);
        }

        // Rij keuzeknoppen, met een titel links
        float Segments(float y, string title, string sub, string[] options, Func<int> get, Action<int> set)
        {
            float x = contentX, w = contentW, h = 52 * S;
            float optW = 0;
            foreach (string o in options) optW = Math.Max(optW, Measure(o, 12, true) + 24 * S);
            float boxW = optW * options.Length + 6 * S, boxX = x + w - boxW - 10 * S;
            bool twoLines = sub != null && Measure(sub, 12, false) > boxX - x - 24 * S;
            if (twoLines) h = 70 * S;
            float boxY = y + (h - 34 * S) / 2;
            Later(delegate(Graphics g)
            {
                Txt(g, title, new RectangleF(x + 10 * S, y + (sub != null ? 7 : 16) * S, boxX - x - 20 * S, 20 * S), T.Text, 14, true, StringAlignment.Near, false);
                if (sub != null) Txt(g, sub, new RectangleF(x + 10 * S, y + 27 * S, boxX - x - 20 * S, (twoLines ? 36 : 18) * S), T.Muted, 12, false, StringAlignment.Near, twoLines);
                RoundFill(g, new RectangleF(boxX, boxY, boxW, 34 * S), Field, 9);
                using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, x, y + h, w, 1 * S);
            });
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                Add(new RectangleF(boxX + 3 * S + i * optW, boxY + 3 * S, optW, 28 * S), null, delegate { set(idx); Changed(); }, delegate(Graphics g, Item it, bool hv)
                {
                    Theme t = T;
                    bool on = get() == idx;
                    if (on) { RoundFill(g, it.R, t.IsLight ? Color.White : ColorTranslator.FromHtml("#3F3F46"), 7); RoundLine(g, RectangleF.Inflate(it.R, -0.75f * S, -0.75f * S), t.Accent, 1.5f, 7); }
                    else if (hv) RoundFill(g, it.R, t.Hover, 7);
                    // op een licht thema is grijs op lichtgrijs te zwak en ziet er rafelig uit: iets donkerder
                    Color fg = on ? t.Text : t.IsLight ? ColorTranslator.FromHtml("#52525B") : t.Muted;
                    Color under = on ? (t.IsLight ? Color.White : ColorTranslator.FromHtml("#3F3F46")) : hv ? Mix(Field, t.Hover) : Field;
                    Txt(g, options[idx], it.R, fg, 12, true, StringAlignment.Center, false, under);
                });
            }
            return y + h + 2 * S;
        }

        float Button(float x, float y, string label, string icon, Action click, bool primary)
        {
            float w = Measure(label, 13, true) + (icon != null ? 50 : 30) * S;
            Add(new RectangleF(x, y, w, 36 * S), null, click, delegate(Graphics g, Item it, bool h)
            {
                Theme t = T;
                RoundFill(g, it.R, primary ? t.Accent : h ? Raised : Field, 9);
                if (primary && h) RoundFill(g, it.R, Color.FromArgb(30, 255, 255, 255), 9);
                Color fg = primary ? t.OnAccent : t.Text;
                float tx = it.R.X + 15 * S;
                if (icon != null) { Icons.Draw(g, icon, new RectangleF(it.R.X + 10 * S, it.R.Y, 20 * S, it.R.Height), 17 * S, fg, 2f); tx = it.R.X + 36 * S; }
                Txt(g, label, new RectangleF(tx, it.R.Y, it.R.Right - tx - 8 * S, it.R.Height), fg, 13, true, StringAlignment.Near, false);
            });
            return w;
        }

        // ---- opbouw
        void BuildItems()
        {
            items.Clear(); painters.Clear(); barSlots.Clear();
            hover = null;
            float W0 = ClientSize.Width, H0 = ClientSize.Height;
            float navW = 236 * S;
            Add(new RectangleF(W0 - 48 * S, 14 * S, 34 * S, 34 * S), L.T("Sluiten") + " (Esc)", delegate { Close(); }, delegate(Graphics g, Item it, bool h)
            {
                RoundFill(g, it.R, h ? Raised : Field, 8);
                Icons.Draw(g, "kruis", it.R, 18 * S, T.Icon, 2f);
            });
            // zijbalk: overal 12 ruimte links en rechts, regels van 34 hoog, een streepje tussen de groepen
            float ny = 62 * S;
            navSeps.Clear();
            for (int i = 0; i < PageIds.Length; i++)
            {
                int idx = i;
                Add(new RectangleF(12 * S, ny, navW - 24 * S, 34 * S), null, delegate { page = idx; recording = null; scroll = 0; BuildItems(); Invalidate(); }, delegate(Graphics g, Item it, bool h)
                {
                    Theme t = T;
                    bool on = page == idx;
                    bool soon = PageIds[idx] == "Voorlezen";
                    if (on || h) RoundFill(g, it.R, on ? Raised : t.Hover, 8);
                    Icons.Draw(g, PageIcons[idx], new RectangleF(it.R.X + 12 * S, it.R.Y, 18 * S, it.R.Height), 17 * S, on ? t.Accent : soon ? Color.FromArgb(120, t.Muted) : t.Muted, 2f);
                    Txt(g, L.T(PageNames[idx]), new RectangleF(it.R.X + 42 * S, it.R.Y, it.R.Width - 50 * S, it.R.Height), on ? t.Text : soon ? Color.FromArgb(140, t.Muted) : (t.IsLight ? ColorTranslator.FromHtml("#3F3F46") : ColorTranslator.FromHtml("#D4D4D8")), 14, on, StringAlignment.Near, false);
                    if (PageIds[idx] == "Bijwerken" && ov.PendingUpdate != null)
                        using (SolidBrush b = new SolidBrush(t.Accent)) g.FillEllipse(b, it.R.Right - 18 * S, it.R.Y + it.R.Height / 2 - 4 * S, 8 * S, 8 * S);   // er staat een nieuwe versie klaar
                });
                ny += 36 * S;
                if (Array.IndexOf(GroupEnds, PageIds[idx]) >= 0) { navSeps.Add(ny + 3 * S); ny += 8 * S; }
            }
            contentX = navW + 30 * S; contentW = W0 - contentX - 30 * S; contentTop = 22 * S - scroll;
            fixedItems = items.Count; contentEnd = 0; buildingContent = true;
            try { BuildPage(); } finally { buildingContent = false; }
            // na een andere pagina of grootte: niet verder scrollen dan de pagina lang is
            if (scroll > MaxScroll + 0.5f) { scroll = MaxScroll; BuildItems(); }
        }

        void BuildPage()
        {
            switch (PageIds[page])
            {
                case "Algemeen": BuildGeneral(); break;
                case "Taal": BuildLanguage(); break;
                case "Uiterlijk": BuildLook(); break;
                case "Werkbalk": BuildBar(); break;
                case "Pen": BuildPen(); break;
                case "Tekst": BuildText(); break;
                case "Stempels": BuildStamps(); break;
                case "Bord": BuildBoard(); break;
                case "Voorlezen": BuildSoon(); break;
                case "Sneltoetsen": BuildKeys(); break;
                case "Opslaan": BuildSave(); break;
                case "Schermdelen": BuildShare(); break;
                case "Gebruik": BuildUsage(); break;
                case "Knoppen": BuildButtons(); break;
                case "Bijwerken": BuildUpdate(); break;
                case "Hulp": BuildHelp(); break;
            }
        }

        // ---- Algemeen
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool Autostart
        {
            get { try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("NexusPen") != null; } catch { return false; } }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue("NexusPen", "\"" + Application.ExecutablePath + "\" --opstarten");
                        else k.DeleteValue("NexusPen", false);
                    }
                }
                catch (Exception ex) { Log.Error("Starten met Windows instellen", ex); }
            }
        }

        void BuildGeneral()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Algemeen"), null);
            y = Section(y, L.T("Starten"));
            y = Toggle(y, L.T("Starten met Windows"), L.T("NexusPen staat klaar zodra je inlogt, ingeklapt"), delegate { return Autostart; }, delegate(bool v) { Autostart = v; });
            y = Toggle(y, L.T("Tonen in de taakbalk"), L.T("Klik op het icoon in de taakbalk om alles in of uit te klappen"), delegate { return c.InTaakbalk; }, delegate(bool v) { c.InTaakbalk = v; });
            y = Section(y + 8 * S, L.T("Tekenen"));
            y = Toggle(y, L.T("Muiswiel en touchpad veranderen de dikte"), L.T("Knijpen op het touchpad werkt ook"), delegate { return c.WheelWidth; }, delegate(bool v) { c.WheelWidth = v; });
            y = Toggle(y, L.T("Elk gereedschap onthoudt zijn eigen dikte"), L.T("Pen dun, markeerstift dik, zonder steeds te wisselen"), delegate { return c.PerToolWidth; }, delegate(bool v) { c.PerToolWidth = v; });
            y = Segments(y, L.T("Spotlicht"), L.T("Een rechthoek is handig om een zin of een woord te lezen"), new string[] { L.T("Rond"), L.T("Rechthoek") }, delegate { return c.FocusShape; }, delegate(int i) { c.FocusShape = i; });
            y = Segments(y, L.T("Tekening bewaren na afsluiten"), L.T("Na Opslaan staat je tekening er de volgende keer weer"), new string[] { L.T("Nooit"), L.T("Na Opslaan"), L.T("Altijd") },
                delegate { return c.KeepDrawing; }, delegate(int i) { c.KeepDrawing = i; });
        }

        // ---- Taal
        void BuildLanguage()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Taal"), L.T("De taal van alle knoppen, tips, menu's en instellingen. Je tekeningen en teksten veranderen niet."));
            string[] examples = { "Pen, Markeerstift, Opslaan", "Pen, Highlighter, Save", "Stylo, Surligneur, Enregistrer", "Lápiz, Resaltador, Guardar" };
            float cw = (contentW - 12 * S) / 2, ch = 76 * S;
            for (int i = 0; i < L.Codes.Length; i++)
            {
                int idx = i;
                RectangleF r = new RectangleF(contentX + (i % 2) * (cw + 12 * S), y + (i / 2) * (ch + 12 * S), cw, ch);
                Add(r, null, delegate { c.Language = L.Codes[idx]; Changed(); Usage.Log("instelling", "taal", L.Codes[idx]); }, delegate(Graphics g, Item it, bool h)
                {
                    Theme t = T;
                    bool on = c.Language == L.Codes[idx];
                    RoundFill(g, it.R, on ? Raised : h ? t.Hover : Card, 12);
                    if (on) RoundLine(g, RectangleF.Inflate(it.R, -1 * S, -1 * S), t.Accent, 2, 12);
                    RectangleF code = new RectangleF(it.R.X + 16 * S, it.R.Y + (ch - 44 * S) / 2, 44 * S, 44 * S);
                    RoundFill(g, code, Field, 10);
                    Txt(g, L.Codes[idx].ToUpperInvariant(), code, t.Text, 14, true, StringAlignment.Center, false);
                    Txt(g, L.Names[idx], new RectangleF(code.Right + 14 * S, it.R.Y + 16 * S, it.R.Width - 120 * S, 22 * S), t.Text, 16, true, StringAlignment.Near, false);
                    Txt(g, examples[idx], new RectangleF(code.Right + 14 * S, it.R.Y + 40 * S, it.R.Width - 120 * S, 18 * S), t.Muted, 12, false, StringAlignment.Near, false);
                    if (on)
                    {
                        RectangleF ck = new RectangleF(it.R.Right - 40 * S, it.R.Y + (ch - 24 * S) / 2, 24 * S, 24 * S);
                        using (SolidBrush b = new SolidBrush(t.Accent)) g.FillEllipse(b, ck);
                        Icons.Draw(g, "vink", ck, 15 * S, t.OnAccent, 2.6f);
                    }
                });
            }
            y += 2 * (ch + 12 * S) + 16 * S;
            Button(contentX, y, L.T("Taal van Windows gebruiken"), "taal", delegate { c.Language = L.WindowsLanguage(); Changed(); }, false);
        }

        // ---- Uiterlijk
        void BuildLook()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Uiterlijk"), L.T("Kleuren van de werkbalk. Het logo kleurt mee met de accentkleur."));
            float tw = (contentW - 10 * S) / 2, th = 48 * S;
            for (int i = 0; i < Theme.Order.Length; i++)
            {
                int idx = Theme.Order[i];
                RectangleF r = new RectangleF(contentX + (i % 2) * (tw + 10 * S), y + (i / 2) * (th + 6 * S), tw, th);
                Add(r, idx == Theme.Custom ? L.T("Klik om je eigen accentkleur te kiezen") : null, delegate
                {
                    if (idx == Theme.Custom)
                        using (ColorDialog cd = new ColorDialog())
                        {
                            cd.Color = c.CustomAccent; cd.FullOpen = true;
                            if (cd.ShowDialog(this) != DialogResult.OK) return;
                            c.CustomAccent = cd.Color;
                        }
                    c.ThemeIndex = idx; Changed();
                }, PaintTheme(idx));
            }
            y += (Theme.Order.Length + 1) / 2 * (th + 6 * S) + 8 * S;
            y = Segments(y, L.T("Grootte van de knoppen"), null, new string[] { L.T("Klein"), L.T("Normaal"), L.T("Groot") }, delegate { return c.SizeIndex; }, delegate(int i) { c.SizeIndex = i; });
            y = Segments(y, L.T("Gekozen knop"), L.T("Rustig valt minder op tijdens de les"), new string[] { L.T("Rustig"), L.T("Accentkleur") }, delegate { return c.ActiveStyle; }, delegate(int i) { c.ActiveStyle = i; });
            y = Segments(y, L.T("Hoeken"), null, new string[] { L.T("Rond"), L.T("Strak") }, delegate { return c.Square ? 1 : 0; }, delegate(int i) { c.Square = i == 1; });
            y = Segments(y, L.T("Doorzichtigheid"), null, new string[] { L.T("Dekkend"), "90%", "80%" }, delegate { return c.OpacityIndex; }, delegate(int i) { c.OpacityIndex = i; });
        }

        Action<Graphics, Item, bool> PaintTheme(int idx)
        {
            return delegate(Graphics g, Item it, bool h)
            {
                Theme cur = T;
                Theme t = idx == Theme.Custom ? CustomPreview() : Theme.All[idx];
                bool on = Cfg.ThemeIndex == idx;
                RoundFill(g, it.R, h && !on ? Raised : Card, 10);
                if (on) RoundLine(g, RectangleF.Inflate(it.R, -1 * S, -1 * S), t.Accent, 2, 10);
                // een klein stukje werkbalk: logo, gekozen knop in de accentkleur, en een paar echte icoontjes
                string[] ics = { "pen", "tekst", "gum" };
                float cell = 20 * S;
                RectangleF bar = new RectangleF(it.R.X + 10 * S, it.R.Y + (it.R.Height - 30 * S) / 2, 8 * S + cell * (2 + ics.Length) + 4 * S, 30 * S);
                RoundFill(g, bar, t.Bar, 8);
                using (Pen p = new Pen(t.Border, t.Thick ? 2 : 1)) using (GraphicsPath gp = Svg.RoundRect(bar, 8 * S)) g.DrawPath(p, gp);
                float bx = bar.X + 5 * S, by = bar.Y + (bar.Height - cell) / 2;
                Logo.Draw(g, new RectangleF(bx + 1 * S, by + 1 * S, cell - 2 * S, cell - 2 * S), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
                bx += cell + 2 * S;
                // zoals in de echte balk: rustig (ingedrukt) of in de accentkleur
                bool quiet = Cfg.ActiveStyle == 0 && !t.Thick;
                RectangleF sel = new RectangleF(bx, by, cell, cell);
                RoundFill(g, sel, quiet ? t.Pressed : t.Accent, 5);
                if (quiet) RoundLine(g, RectangleF.Inflate(sel, -0.5f * S, -0.5f * S), t.PressedEdge, 1, 5);
                Icons.Draw(g, "muis", sel, 13 * S, quiet ? t.Text : t.OnAccent, 2f);
                bx += cell;
                foreach (string ic in ics) { Icons.Draw(g, ic, new RectangleF(bx, by, cell, cell), 13 * S, t.Icon, 2f); bx += cell; }
                float tx = bar.Right + 12 * S;
                Txt(g, L.T(t.Name), new RectangleF(tx, it.R.Y + 5 * S, it.R.Right - tx - 8 * S, 20 * S), cur.Text, 14, true, StringAlignment.Near, false);
                Txt(g, L.T(t.Desc), new RectangleF(tx, it.R.Y + 25 * S, it.R.Right - tx - 8 * S, 18 * S), cur.Muted, 12, false, StringAlignment.Near, false);
            };
        }

        Theme CustomPreview()
        {
            int keep = Cfg.ThemeIndex;
            Cfg.ThemeIndex = Theme.Custom;
            Theme t = Theme.Get(Cfg);
            Cfg.ThemeIndex = keep;
            return t;
        }

        // ---- Werkbalk: slepen tussen je balk en de bak
        void BuildBar()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Werkbalk"), L.T("Slepen = verplaatsen. Kruisje = weghalen. Plusje = erbij."));
            List<string> layout = Toolbar.Layout(c);
            float cell = 46 * S, sepW = 16 * S;
            y = Section(y, L.T("Jouw balk"));
            float bx = contentX + 12 * S, by = y + 10 * S;
            float rowH = cell + 22 * S;
            float maxX = contentX + contentW - 12 * S;
            int rows = 1;
            List<RectangleF> slots = new List<RectangleF>();
            foreach (string k in layout)
            {
                float w = k == "|" ? sepW : cell;
                if (bx + w > maxX) { bx = contentX + 12 * S; by += rowH; rows++; }
                slots.Add(new RectangleF(bx, by, w, cell));
                bx += w + (k == "|" ? 6 : 20) * S;
            }
            barArea = new RectangleF(contentX, y, contentW, rows * rowH + 14 * S);
            barSlots = slots;
            for (int i = 0; i < layout.Count; i++)
            {
                int idx = i; string k = layout[i];
                Item it = Add(slots[i], Toolbar.NameOf(k), null, delegate(Graphics g, Item item, bool hv)
                {
                    PaintKey(g, item.R, k, hv, dragActive && dragFrom == idx);
                    if (hv && !dragActive && k != "instellingen" && k != "|") PaintBadge(g, item.R, "kruis");
                });
                it.Key = "bar:" + idx + ":" + k;
            }
            y += barArea.Height + 18 * S;
            y = Section(y, L.T("Niet in je balk"));
            List<string> tray = new List<string>();
            foreach (string k in Toolbar.Catalog) if (!layout.Contains(k)) tray.Add(k);
            tray.Add("|");
            float tx = contentX + 12 * S, ty = y + 10 * S;
            int trows = 1;
            foreach (string k in tray)
            {
                if (tx + 74 * S > maxX) { tx = contentX + 12 * S; ty += rowH; trows++; }
                string kk = k;
                RectangleF r = new RectangleF(tx + 14 * S, ty, cell, cell);
                Item it = Add(r, Toolbar.NameOf(k), null, delegate(Graphics g, Item item, bool hv)
                {
                    PaintKey(g, item.R, kk, hv, false);
                    if (hv && !dragActive) PaintBadge(g, item.R, "plus");
                });
                it.Key = "tray:" + k;
                tx += 74 * S;
            }
            trayArea = new RectangleF(contentX, y, contentW, trows * rowH + 14 * S);
            RectangleF ba = barArea, ta = trayArea;
            painters.Insert(0, delegate(Graphics g)
            {
                RoundFill(g, ba, T.IsLight ? ColorTranslator.FromHtml("#FAFAFA") : ColorTranslator.FromHtml("#101013"), 12);
                RoundLine(g, ba, T.Accent, 1.5f, 12);
                RoundFill(g, ta, T.IsLight ? ColorTranslator.FromHtml("#FAFAFA") : ColorTranslator.FromHtml("#101013"), 12);
                using (Pen p = new Pen(T.IsLight ? ColorTranslator.FromHtml("#D4D4D8") : ColorTranslator.FromHtml("#3F3F46"), 1.5f * S) { DashPattern = new float[] { 4, 3 } })
                using (GraphicsPath gp = Svg.RoundRect(ta, 12 * S)) g.DrawPath(p, gp);
            });
            y += trayArea.Height + 16 * S;
            y = Segments(y + 4 * S, L.T("Stand"), null, new string[] { L.T("Liggend"), L.T("Staand") }, delegate { return c.Vertical ? 1 : 0; }, delegate(int i) { c.Vertical = i == 1; });
        }

        // klein rondje rechtsboven op een knop: kruisje (weghalen) of plusje (erbij)
        RectangleF KeyBadge(RectangleF r) { float d = 18 * S; return new RectangleF(r.Right - d + 5 * S, r.Y - 5 * S, d, d); }

        void PaintBadge(Graphics g, RectangleF r, string icon)
        {
            RectangleF b = KeyBadge(r);
            Color bg = icon == "plus" ? T.Accent : (T.IsLight ? Color.FromArgb(240, 60, 60, 66) : Color.FromArgb(240, 228, 228, 231));
            Color fg = icon == "plus" ? T.OnAccent : (T.IsLight ? Color.White : ColorTranslator.FromHtml("#18181B"));
            using (SolidBrush br = new SolidBrush(bg)) g.FillEllipse(br, b);
            Icons.Draw(g, icon, b, 11 * S, fg, 2.4f);
        }

        // Een knop uit de bak met een klik achteraan in je balk zetten (voor Instellingen)
        void AddKeyToBar(string k)
        {
            List<string> layout = Toolbar.Layout(Cfg);
            int at = layout.IndexOf("instellingen");
            if (k == "|")
            {
                int pos = at < 0 ? layout.Count : at;
                if (pos == 0 || layout[pos - 1] == "|") return;   // daar staat al een streep
                layout.Insert(pos, "|");
            }
            else if (!layout.Contains(k)) { if (at < 0) layout.Add(k); else layout.Insert(at, k); }
            Cfg.BarLayout = Toolbar.LayoutString(layout);
            Usage.Log("instelling", "erbij", k);
            Changed(); BuildItems(); Invalidate();
        }

        void RemoveKeyFromBar(int idx)
        {
            List<string> layout = Toolbar.Layout(Cfg);
            if (idx < 0 || idx >= layout.Count || layout[idx] == "instellingen") return;
            Usage.Log("instelling", "verberg", layout[idx]);
            layout.RemoveAt(idx);
            List<string> clean = new List<string>();
            foreach (string k in layout) { if (k == "|" && (clean.Count == 0 || clean[clean.Count - 1] == "|")) continue; clean.Add(k); }
            while (clean.Count > 0 && clean[clean.Count - 1] == "|") clean.RemoveAt(clean.Count - 1);
            Cfg.BarLayout = Toolbar.LayoutString(clean);
            Changed(); BuildItems(); Invalidate();
        }

        void PaintKey(Graphics g, RectangleF r, string k, bool hv, bool ghost)
        {
            Theme t = T;
            if (k == "|")
            {
                using (SolidBrush b = new SolidBrush(hv ? t.Accent : t.Muted)) g.FillRectangle(b, r.X + r.Width / 2 - 1 * S, r.Y + 8 * S, 2 * S, r.Height - 16 * S);
                return;
            }
            if (ghost) { using (Pen p = new Pen(t.Accent, 1.5f * S) { DashPattern = new float[] { 3, 3 } }) using (GraphicsPath gp = Svg.RoundRect(r, 10 * S)) g.DrawPath(p, gp); return; }
            RoundFill(g, r, hv ? Raised : Field, 10);
            if (k == "kleur") using (SolidBrush b = new SolidBrush(ov.CurrentColor)) g.FillEllipse(b, r.X + r.Width / 2 - 9 * S, r.Y + r.Height / 2 - 9 * S, 18 * S, 18 * S);
            else Icons.Draw(g, Toolbar.IconOf(k), r, 21 * S, t.Icon, 2f);
            Txt(g, Toolbar.NameOf(k), new RectangleF(r.X - 9 * S, r.Bottom + 2 * S, r.Width + 18 * S, 16 * S), t.Muted, 10, false, StringAlignment.Center, false);
        }

        int InsertIndex(Point p)
        {
            if (!barArea.Contains(p)) return -1;
            for (int i = 0; i < barSlots.Count; i++)
            {
                RectangleF r = barSlots[i];
                if (p.Y < r.Bottom + 20 * S && p.Y > r.Y - 10 * S && p.X < r.X + r.Width / 2) return i;
            }
            return barSlots.Count;
        }

        void DropKey(Point p)
        {
            List<string> layout = Toolbar.Layout(Cfg);
            int ins = InsertIndex(p);
            if (dragFrom >= 0)
            {
                if (ins < 0 && trayArea.Contains(p) && dragKey == "instellingen") { Notice(L.T("Instellingen blijft altijd in de balk.")); return; }
                if (ins < 0 && trayArea.Contains(p)) { layout.RemoveAt(dragFrom); Usage.Log("instelling", "verberg", dragKey); }
                else if (ins >= 0) { layout.RemoveAt(dragFrom); if (ins > dragFrom) ins--; layout.Insert(ins, dragKey); }
                else return;
            }
            else if (ins >= 0) { layout.Insert(ins, dragKey); Usage.Log("instelling", "erbij", dragKey); }
            else return;
            // geen dubbele of losse strepen
            List<string> clean = new List<string>();
            foreach (string k in layout)
            {
                if (k == "|" && (clean.Count == 0 || clean[clean.Count - 1] == "|")) continue;
                clean.Add(k);
            }
            while (clean.Count > 0 && clean[clean.Count - 1] == "|") clean.RemoveAt(clean.Count - 1);
            Cfg.BarLayout = Toolbar.LayoutString(clean);   // inkorten blijft staan, net als bij verbergen met rechtsklik
            Changed();
            BuildItems();
        }

        // ---- Pen en kleuren
        void BuildPen()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Pen en kleuren"), null);
            y = Segments(y, L.T("Kleuren"), L.T("Felle kleuren vallen op, zachte zijn rustiger voor de ogen"), new string[] { L.T("Fel"), L.T("Zacht") }, delegate { return ov.PaletteIndex; }, delegate(int i) { ov.SetPalette(i); });
            // je eigen kleurenlijst: klik = kiezen, dan met de knoppen eronder verplaatsen of weghalen
            float sx = contentX + 10 * S, cell = 40 * S;
            int perRow = Math.Max(8, (int)((contentW - 20 * S) / cell));
            // klik = kiezen, slepen = verplaatsen, kruisje (als je erop staat) = weghalen, plusje = erbij
            Color[] cols = ov.Colors;
            colorSlots.Clear();
            int total = cols.Length + 2;   // plus de knoppen "erbij" en "standaard"
            for (int i = 0; i < cols.Length; i++)
            {
                int idx = i;
                Color col = cols[i];
                RectangleF r = new RectangleF(sx + (i % perRow) * cell, y + 6 * S + (i / perRow) * cell, 34 * S, 34 * S);
                colorSlots.Add(r);
                Item sw = Add(r, Palette.NameOf(ov.PaletteIndex, col), delegate { ov.ChooseColorQuietly(idx); Invalidate(); }, delegate(Graphics g, Item it, bool h)
                {
                    if (dragActive && dragKey == "col" && dragFrom == idx) { using (Pen p = new Pen(T.Muted, 1.2f * S) { DashPattern = new float[] { 2, 2 } }) g.DrawEllipse(p, RectangleF.Inflate(it.R, -4 * S, -4 * S)); return; }
                    if (ov.ColorIndex == idx) using (SolidBrush b = new SolidBrush(T.Accent)) g.FillEllipse(b, it.R);
                    using (SolidBrush b = new SolidBrush(col)) g.FillEllipse(b, RectangleF.Inflate(it.R, -4 * S, -4 * S));
                    using (Pen p = new Pen(Color.FromArgb(90, 128, 128, 128), 1)) g.DrawEllipse(p, RectangleF.Inflate(it.R, -4 * S, -4 * S));
                    if (h && !dragActive && cols.Length > 1)
                    {
                        // klein kruisje rechtsboven: weghalen
                        RectangleF x = ColorBadge(it.R);
                        using (SolidBrush b = new SolidBrush(T.IsLight ? Color.FromArgb(240, 60, 60, 66) : Color.FromArgb(240, 228, 228, 231))) g.FillEllipse(b, x);
                        Icons.Draw(g, "kruis", x, 10 * S, T.IsLight ? Color.White : ColorTranslator.FromHtml("#18181B"), 2.4f);
                    }
                });
                sw.Key = "col:" + i;
            }
            int n = cols.Length;
            RectangleF addR = new RectangleF(sx + (n % perRow) * cell, y + 6 * S + (n / perRow) * cell, 34 * S, 34 * S);
            Add(addR, L.T("Kleur toevoegen"), delegate { AddColor(); }, delegate(Graphics g, Item it, bool h)
            {
                RectangleF c0 = RectangleF.Inflate(it.R, -4 * S, -4 * S);
                if (h) using (SolidBrush b = new SolidBrush(T.Hover)) g.FillEllipse(b, c0);
                using (Pen p = new Pen(T.Muted, 1.2f * S) { DashPattern = new float[] { 2, 2 } }) g.DrawEllipse(p, c0);
                Icons.Draw(g, "plus", c0, 14 * S, T.Muted, 2.2f);
            });
            n++;
            RectangleF resetR = new RectangleF(sx + (n % perRow) * cell, y + 6 * S + (n / perRow) * cell, 34 * S, 34 * S);
            Add(resetR, L.T("Standaardkleuren terugzetten"), delegate { ResetColors(); }, delegate(Graphics g, Item it, bool h)
            {
                RectangleF c0 = RectangleF.Inflate(it.R, -4 * S, -4 * S);
                if (h) using (SolidBrush b = new SolidBrush(T.Hover)) g.FillEllipse(b, c0);
                Icons.Draw(g, "terugzetten", c0, 14 * S, T.Muted, 2f);
            });
            y += ((total + perRow - 1) / perRow) * cell + 14 * S;
            y = Toggle(y, L.T("Krijtstreep"), L.T("Lijnen van de pen zien eruit als krijt op een bord"), delegate { return c.Chalk; }, delegate(bool v) { c.Chalk = v; });
            y = Toggle(y, L.T("Vervagen"), L.T("Wat je met pen of markeerstift tekent, verdwijnt vanzelf. Handig om iets snel aan te wijzen"), delegate { return c.Fade; }, delegate(bool v) { c.Fade = v; });
            y = Segments(y, L.T("Hoe lang blijft het staan?"), null, new string[] { "3 s", "5 s", "7 s", "12 s" }, delegate { return Array.IndexOf(Settings.FadeChoices, c.FadeSeconds); }, delegate(int i) { c.FadeSeconds = Settings.FadeChoices[i]; });
            y = Segments(y, L.T("Laserpen"), L.T("Hoe lang blijft de laser zichtbaar?"), new string[] { L.T("Snel weg"), L.T("Langzaam"), L.T("Blijft staan") }, delegate { return c.LaserMode; }, delegate(int i) { c.LaserMode = i; });
        }

        // ---- je eigen kleurenlijst aanpassen (voor de set die nu aan staat: fel of zacht)
        List<RectangleF> colorSlots = new List<RectangleF>();
        RectangleF ColorBadge(RectangleF r) { float d = 14 * S; return new RectangleF(r.Right - d + 5 * S, r.Y - 5 * S, d, d); }

        // Waar komt een gesleepte kleur terecht? (de plek waar je hem loslaat)
        int ColorInsertIndex(Point p)
        {
            for (int i = 0; i < colorSlots.Count; i++)
            {
                RectangleF r = colorSlots[i];
                if (p.Y >= r.Y - 6 * S && p.Y <= r.Bottom + 6 * S && p.X < r.X + r.Width / 2) return i;
            }
            for (int i = colorSlots.Count - 1; i >= 0; i--) if (p.Y >= colorSlots[i].Y - 6 * S) return i + 1;
            return 0;
        }

        void DropColor(Point p)
        {
            List<Color> list = OwnColors();
            if (dragFrom < 0 || dragFrom >= list.Count) return;
            int ins = ColorInsertIndex(p);
            Color c = list[dragFrom];
            list.RemoveAt(dragFrom);
            if (ins > dragFrom) ins--;
            ins = Math.Max(0, Math.Min(list.Count, ins));
            list.Insert(ins, c);
            SaveColors(list, ins);
        }

        void RemoveColorAt(int i)
        {
            List<Color> list = OwnColors();
            if (list.Count <= 1 || i < 0 || i >= list.Count) return;
            list.RemoveAt(i);
            SaveColors(list, Math.Min(ov.ColorIndex > i ? ov.ColorIndex - 1 : ov.ColorIndex, list.Count - 1));
        }
        List<Color> OwnColors() { return new List<Color>(ov.Colors); }

        void SaveColors(List<Color> list, int select)
        {
            string text = Palette.ToText(list);
            if (text == Palette.ToText(Palette.Sets[ov.PaletteIndex])) text = "";   // weer gelijk aan de standaard
            if (ov.PaletteIndex == 0) Cfg.ColorsFel = text; else Cfg.ColorsZacht = text;
            ov.ChooseColorQuietly(Math.Max(0, Math.Min(list.Count - 1, select)));
            Changed();
            BuildItems(); Invalidate();
        }

        void AddColor()
        {
            List<Color> list = OwnColors();
            if (list.Count >= Palette.MaxColors) { Notice(L.F("Hoogstens {0} kleuren.", Palette.MaxColors)); return; }
            using (ColorDialog cd = new ColorDialog())
            {
                cd.FullOpen = true; cd.Color = ov.CurrentColor;
                if (cd.ShowDialog(this) != DialogResult.OK) return;
                Color c = Color.FromArgb(255, cd.Color);
                foreach (Color k in list) if (k.ToArgb() == c.ToArgb()) { Notice(L.T("Deze kleur staat er al.")); return; }
                list.Add(c);
                SaveColors(list, list.Count - 1);
            }
        }

        void ResetColors()
        {
            if (ov.PaletteIndex == 0) Cfg.ColorsFel = ""; else Cfg.ColorsZacht = "";
            ov.ChooseColorQuietly(2);
            Changed();
            BuildItems(); Invalidate();
        }

        // ---- Tekst
        void BuildText()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Tekst"), null);
            y = Section(y, L.T("Lettertype"));
            float cw = (contentW - 20 * S) / 3, ch = 84 * S;
            int shown = 0;
            for (int i = 0; i < FontChoices.Length; i++)
            {
                int idx = i;
                string fn = FontChoices[i];
                if (fn != "Segoe UI" && !Fonts.Has(fn)) continue;   // lettertype kon niet geladen worden: niet aanbieden
                int pos = shown++;
                RectangleF r = new RectangleF(contentX + (pos % 3) * (cw + 10 * S), y + (pos / 3) * (ch + 10 * S), cw, ch);
                Add(r, null, delegate { c.FontName = fn; Changed(); }, delegate(Graphics g, Item it, bool h)
                {
                    Theme t = T;
                    bool on = Render.FontOrDefault(c.FontName) == fn || (fn == "Segoe UI" && string.IsNullOrEmpty(c.FontName));
                    RoundFill(g, it.R, on ? Raised : h ? t.Hover : Card, 10);
                    if (on) RoundLine(g, RectangleF.Inflate(it.R, -1 * S, -1 * S), t.Accent, 2, 10);
                    Txt(g, fn + (fn == "Andika" ? "  ·  " + L.T("Aanbevolen") : ""), new RectangleF(it.R.X + 12 * S, it.R.Y + 8 * S, it.R.Width - 20 * S, 18 * S), t.Text, 13, true, StringAlignment.Near, false);
                    Txt(g, L.T(FontDescs[idx]), new RectangleF(it.R.X + 12 * S, it.R.Y + 27 * S, it.R.Width - 20 * S, 16 * S), t.Muted, 11, false, StringAlignment.Near, false);
                    using (Font f = Fonts.Make(fn, 20 * S, FontStyle.Bold)) using (SolidBrush b = new SolidBrush(t.Text))
                        g.DrawString(L.T("Ik ga met de trein."), f, b, it.R.X + 10 * S, it.R.Y + 46 * S);
                });
            }
            y += 2 * (ch + 10 * S) + 10 * S;
            y = Toggle(y, L.T("Rand om tekst"), L.T("Goed leesbaar op elke achtergrond, ook op foto's en video"), delegate { return c.TextOutline; }, delegate(bool v) { c.TextOutline = v; });
            y = Segments(y, L.T("Dikte van de rand"), null, new string[] { L.T("Dun"), L.T("Middel"), L.T("Dik") }, delegate { return c.OutlineWidth; }, delegate(int i) { c.OutlineWidth = i; });
            y = Segments(y, L.T("Randkleur"), null, new string[] { L.T("Zwart"), L.T("Wit"), L.T("Automatisch") }, delegate { return c.OutlineColor; }, delegate(int i) { c.OutlineColor = i; });
            y = Segments(y, L.T("Standaardgrootte"), null, new string[] { "24", "36", "48", "64" }, delegate { int[] v = { 24, 36, 48, 64 }; int b = 1; for (int i = 0; i < 4; i++) if (Math.Abs(v[i] - c.TextSize) < 1) b = i; return b; },
                delegate(int i) { int[] v = { 24, 36, 48, 64 }; c.TextSize = v[i]; });
            RectangleF prev = new RectangleF(contentX, y + 10 * S, contentW, Math.Max(64 * S, ClientSize.Height - y - 40 * S));
            Later(delegate(Graphics g) { PaintPreview(g, prev); });
        }

        // Voorbeeld op drie achtergronden, in het gekozen lettertype en met de gekozen rand
        void PaintPreview(Graphics g, RectangleF r)
        {
            Color[] bgs = { Color.White, Overlay.ChalkGreen, ColorTranslator.FromHtml("#E8590C") };
            Color[] fills = { ColorTranslator.FromHtml("#FACC15"), Color.White, Color.White };
            float w = r.Width / 3;
            GraphicsState st = g.Save();
            using (GraphicsPath gp = Svg.RoundRect(r, 10 * S)) g.SetClip(gp);
            for (int i = 0; i < 3; i++)
            {
                RectangleF cell = new RectangleF(r.X + i * w, r.Y, w, r.Height);
                using (SolidBrush b = new SolidBrush(bgs[i])) g.FillRectangle(b, cell);
                Stroke s = new Stroke();
                s.Tool = Tool.Text; s.Text = L.T("Goed zo!"); s.FontSize = Math.Min(34 * S, r.Height * 0.5f);
                s.Color = fills[i]; s.FontName = Cfg.FontName; s.Outline = Cfg.TextOutline; s.OutlineWidth = Cfg.OutlineWidth; s.OutlineColor = Cfg.OutlineColor;
                s.Pts.Add(PointF.Empty);
                RectangleF tb = Render.TextBounds(s);
                s.Pts[0] = new PointF(cell.X + (cell.Width - tb.Width) / 2, cell.Y + (cell.Height - tb.Height) / 2);
                Render.Setup(g);
                Render.Draw(g, s, 1);
            }
            g.Restore(st);
        }

        // ---- Stempels
        void BuildStamps()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Stempels"), L.T("Klik op de stempelknop in de balk en daarna waar je hem wilt hebben."));
            int nk = Render.StampOrder.Length;
            float cw = (contentW - 10 * S * (nk - 1)) / nk, ch = 96 * S;
            for (int i = 0; i < nk; i++)
            {
                int idx = Render.StampOrder[i];
                RectangleF r = new RectangleF(contentX + i * (cw + 10 * S), y, cw, ch);
                Add(r, null, delegate { c.StampKind = idx; Changed(); }, delegate(Graphics g, Item it, bool h)
                {
                    Theme t = T;
                    bool on = c.StampKind == idx;
                    RoundFill(g, it.R, on ? Raised : h ? t.Hover : Card, 10);
                    if (on) RoundLine(g, RectangleF.Inflate(it.R, -1 * S, -1 * S), t.Accent, 2, 10);
                    Stroke s = new Stroke(); s.Tool = Tool.Stamp; s.FontSize = 40 * S;
                    string kind = Render.StampKinds[idx];
                    s.Text = kind == "nummer" ? "#" + c.StampNext : kind; s.Color = Render.StampColor(kind);
                    s.Pts.Add(new PointF(it.R.X + it.R.Width / 2, it.R.Y + 38 * S));
                    Render.Setup(g); Render.Draw(g, s, 1);
                    Txt(g, L.T(Render.StampName(kind)), new RectangleF(it.R.X, it.R.Bottom - 28 * S, it.R.Width, 20 * S), t.Text, 13, true, StringAlignment.Center, false);
                });
            }
            y += ch + 16 * S;
            y = Segments(y, L.T("Grootte"), null, new string[] { "S", "M", "L" }, delegate { return c.StampPx > 0 ? -1 : c.StampSize; }, delegate(int i) { c.StampSize = i; c.StampPx = 0; });
            // volgende nummer: het getal zelf, en een knopje om weer bij 1 te beginnen
            float ny = y;
            Later(delegate(Graphics g)
            {
                Txt(g, L.T("Volgende nummer"), new RectangleF(contentX + 10 * S, ny + 16 * S, contentW - 140 * S, 20 * S), T.Text, 14, true, StringAlignment.Near, false);
                RectangleF nb = new RectangleF(contentX + contentW - 96 * S, ny + 10 * S, 34 * S, 34 * S);
                using (SolidBrush b = new SolidBrush(T.IsLight ? Color.Black : Color.White)) g.FillEllipse(b, nb);
                Txt(g, c.StampNext.ToString(), nb, T.IsLight ? Color.White : Color.Black, 13, true, StringAlignment.Center, false);
                using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, contentX, ny + 54 * S, contentW, 1 * S);
            });
            Add(new RectangleF(contentX + contentW - 52 * S, ny + 10 * S, 34 * S, 34 * S), L.T("Weer bij 1 beginnen"), delegate { c.StampNext = 1; Changed(); BuildItems(); }, delegate(Graphics g, Item it, bool h)
            {
                RoundFill(g, it.R, h ? Raised : Field, 9);
                Icons.Draw(g, "terugzetten", it.R, 16 * S, T.Icon, 2f);
            });
            y += 56 * S;
        }

        // ---- Bord
        void BuildBoard()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Bord en achtergrond"), L.T("Zoomen op het bord: Ctrl+plus, Ctrl+min, Ctrl+0 of Ctrl+muiswiel."));
            y = Segments(y, L.T("Achtergrond"), null, new string[] { L.T("Leeg"), L.T("Schrijflijnen"), L.T("Ruitjes"), L.T("Lijntjes") }, delegate { return c.BoardBg; }, delegate(int i) { c.BoardBg = i; });
            float[] zs = { 0.6f, 1f, 2f, 4f };
            y = Segments(y, L.T("Grootte van lijnen en ruitjes"), null, new string[] { L.T("Klein"), L.T("Normaal"), L.T("Groot"), L.T("Extra groot") },
                delegate { int b = -1; for (int i = 0; i < zs.Length; i++) if (Math.Abs(zs[i] - c.BoardZoom) < 0.05f) b = i; return b; }, delegate(int i) { c.BoardZoom = zs[i]; });
        }

        void BuildSoon()
        {
            float y = Heading(contentTop, L.T("Voorlezen"), null);
            Para(y, L.T("Komt in versie 1.3: tekst op je scherm laten voorlezen, en tekst van het scherm lezen en overnemen. Ook de timer komt dan."), 60 * S);
        }

        // ---- Sneltoetsen
        void BuildKeys()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Sneltoetsen"), null);
            y = Segments(y, L.T("Sneltoetsen voor gereedschap"), L.T("Met Ctrl+Shift kies je nooit per ongeluk een gereedschap als je typt"),
                new string[] { "Ctrl+Shift + " + L.T("letter"), L.T("Alleen een letter"), L.T("Uit") }, delegate { return c.ShortcutMode; }, delegate(int i) { c.ShortcutMode = i; BuildItems(); });
            y = Section(y + 6 * S, L.T("Werken overal, ook als je niet tekent"));
            float cw = (contentW - 14 * S) / 2, rh = 38 * S;
            KeyRow(contentX, y, cw, "global:draw", "pen", L.T("Tekenen aan of uit"), delegate { return c.HotkeyDraw; });
            KeyRow(contentX + cw + 14 * S, y, cw, "global:hide", "oog", L.T("Alles in- of uitklappen"), delegate { return c.HotkeyHide; });
            y += rh + 14 * S;
            y = Section(y, L.T("Tijdens het tekenen"));
            int n = Shortcuts.All.Length, half = (n + 1) / 2;
            for (int i = 0; i < n; i++)
            {
                Shortcut sc = Shortcuts.All[i];
                float x = i < half ? contentX : contentX + cw + 14 * S;
                float yy = y + (i < half ? i : i - half) * (rh + 4 * S);
                string id = sc.Id;
                KeyRow(x, yy, cw, id, sc.Icon, L.T(sc.Label), delegate { return Shortcuts.Combo(id); });
            }
            foreach (Action a in laterItems) a();
            laterItems.Clear();
            y += half * (rh + 4 * S) + 8 * S;
            Button(contentX, y, L.T("Standaard terugzetten"), "undo", delegate { c.ShortcutOverrides = ""; c.HotkeyDraw = "Ctrl+Shift+D"; c.HotkeyHide = "Ctrl+Shift+H"; ov.RegisterGlobalHotkeys(); Changed(); BuildItems(); }, false);
        }

        List<Action> laterItems = new List<Action>();

        void KeyRow(float x, float y, float w, string id, string icon, string label, Func<string> combo)
        {
            if (!id.StartsWith("global:"))
            {
                // klikvak voor het prullenbakje (uitzetten), over het icoontje heen (later toegevoegd = gaat voor de hele regel)
                RectangleF offR = new RectangleF(x + w - 10 * S - 18 * S - 10 * S - 24 * S, y + 7 * S, 24 * S, 24 * S);
                string rid = id;
                laterItems.Add(delegate
                {
                    Add(offR, L.T("Deze sneltoets uitzetten"), delegate
                    {
                        // tijdens het opnemen staat hier "Annuleren": dan alleen stoppen met opnemen
                        if (recording == rid) { recording = null; Invalidate(); return; }
                        recording = null; Shortcuts.SetOverride(rid, ""); Changed(); Invalidate();
                    }, delegate(Graphics g, Item it, bool h) { if (h && recording != rid && !string.IsNullOrEmpty(combo())) RoundFill(g, it.R, T.Hover, 6); });
                });
            }
            Add(new RectangleF(x, y, w, 38 * S), L.T("Klik en druk je toetsen in. Backspace = geen sneltoets"), delegate { recording = recording == id ? null : id; Invalidate(); }, delegate(Graphics g, Item it, bool h)
            {
                Theme t = T;
                bool rec = recording == id;
                RoundFill(g, it.R, rec ? Raised : Card, 8);
                Icons.Draw(g, icon, new RectangleF(it.R.X + 8 * S, it.R.Y, 22 * S, it.R.Height), 17 * S, t.Icon, 2f);
                float right = it.R.Right - 10 * S;
                float labelX = it.R.X + 38 * S;
                // potloodje = wijzigen (tijdens het opnemen: kruisje = annuleren), prullenbakje = uitzetten
                Icons.Draw(g, rec ? "kruis" : "pen", new RectangleF(right - 18 * S, it.R.Y, 18 * S, it.R.Height), 15 * S, h || rec ? t.Icon : t.Muted, 2f);
                right -= 18 * S + 10 * S;
                if (!id.StartsWith("global:") && !rec)
                {
                    bool none = string.IsNullOrEmpty(combo());
                    Icons.Draw(g, "prullenbak", new RectangleF(right - 24 * S, it.R.Y, 24 * S, it.R.Height), 15 * S, none ? Color.FromArgb(70, t.Muted) : t.Muted, 2f);
                    right -= 24 * S + 10 * S;
                }
                if (rec)
                {
                    string msg = L.T("Druk nu je toetsen in");
                    float mw = Measure(msg, 12, true) + 26 * S;
                    RectangleF b = new RectangleF(right - mw, it.R.Y + 6 * S, mw, 26 * S);
                    RoundFill(g, b, t.Bar, 7); RoundLine(g, b, t.Accent, 1.5f, 7);
                    using (SolidBrush br = new SolidBrush(t.Accent)) g.FillEllipse(br, b.X + 9 * S, b.Y + 10 * S, 7 * S, 7 * S);
                    Txt(g, msg, new RectangleF(b.X + 20 * S, b.Y, b.Width - 22 * S, b.Height), t.Accent, 12, true, StringAlignment.Near, false);
                    Txt(g, label, new RectangleF(labelX, it.R.Y, Math.Max(10, b.X - labelX - 8 * S), it.R.Height), t.Text, 13, true, StringAlignment.Near, false);
                    return;
                }
                string cmb = combo();
                if (string.IsNullOrEmpty(cmb))
                {
                    Txt(g, L.T("geen"), new RectangleF(right - 80 * S, it.R.Y, 80 * S, it.R.Height), t.Muted, 12, false, StringAlignment.Far, false);
                    Txt(g, label, new RectangleF(labelX, it.R.Y, Math.Max(10, right - 80 * S - labelX), it.R.Height), t.Text, 13, true, StringAlignment.Near, false);
                    return;
                }
                string[] keys = cmb.Split('+');
                for (int i = keys.Length - 1; i >= 0; i--)
                {
                    float kw = Math.Max(24 * S, Measure(keys[i], 12, true) + 14 * S);
                    RectangleF kr = new RectangleF(right - kw, it.R.Y + 6 * S, kw, 26 * S);
                    RoundFill(g, kr, Field, 6);
                    using (SolidBrush b = new SolidBrush(t.IsLight ? ColorTranslator.FromHtml("#D4D4D8") : ColorTranslator.FromHtml("#3F3F46"))) g.FillRectangle(b, kr.X + 3 * S, kr.Bottom - 2 * S, kr.Width - 6 * S, 2 * S);
                    Txt(g, keys[i], kr, t.Text, 12, true, StringAlignment.Center, false);
                    right -= kw + 4 * S;
                }
                Txt(g, label, new RectangleF(labelX, it.R.Y, Math.Max(10, right - labelX - 4 * S), it.R.Height), t.Text, 13, true, StringAlignment.Near, false);
            });
        }

        // Esc sluit de instellingen, ook als het toetsenbord net ergens anders in het venster zat
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && recording == null) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (recording == null) return;   // Esc om te sluiten: zie ProcessCmdKey
            e.Handled = true; e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { recording = null; Invalidate(); return; }
            Keys code = e.KeyCode;
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin) return;  // wacht op de echte toets
            string combo = code == Keys.Back ? "" : Shortcuts.FromKeys(e.KeyData);
            if (combo == null || (combo.Length > 0 && combo.EndsWith("+"))) return;
            if (combo.Length > 0 && !combo.Contains("+") && recording.StartsWith("global:")) { Notice(L.T("Kies voor deze sneltoets een combinatie met Ctrl, Shift of Alt.")); return; }
            if (recording.StartsWith("global:"))
            {
                if (combo.Length == 0) { Notice(L.T("Deze sneltoets kan niet leeg zijn.")); return; }
                string clash = Shortcuts.ConflictGlobal(combo);
                if (clash != null) { Notice(clash); return; }
                string oldDraw = Cfg.HotkeyDraw, oldHide = Cfg.HotkeyHide;
                if (recording == "global:draw") Cfg.HotkeyDraw = combo; else Cfg.HotkeyHide = combo;
                string err = ov.RegisterGlobalHotkeys();
                if (err != null) { Cfg.HotkeyDraw = oldDraw; Cfg.HotkeyHide = oldHide; ov.RegisterGlobalHotkeys(); Notice(err); }
                else Notice(L.T("Opgeslagen."));
            }
            else
            {
                string conflict = Shortcuts.Conflict(recording, combo);
                if (conflict != null) { Notice(conflict); return; }
                Shortcuts.SetOverride(recording, combo);
                Notice(L.T("Opgeslagen."));
            }
            recording = null;
            Changed();
        }

        // ---- Opslaan
        void BuildSave()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Opslaan"), L.T("Alles wordt vanzelf bewaard. Met Opslaan staat je tekening er de volgende keer weer."));
            float bx = contentX;
            bx += Button(bx, y + 6 * S, L.T("Nu opslaan"), "opslaan", delegate { if (ov.SaveExplicit()) Notice(L.T("Opgeslagen. Je tekening en instellingen staan er de volgende keer weer.")); }, true) + 10 * S;
            Button(bx, y + 6 * S, L.T("Vorige tekening terughalen"), "undo", delegate { ov.RestorePrevious(); Notice(L.T("Klaar. Kijk op je scherm.")); }, false);
            y += 62 * S;
            // map voor schermafbeeldingen: zelf kiezen
            y = Section(y, L.T("Schermafbeeldingen"));
            string dir = Overlay.ScreenshotDir;
            float ry = y;
            Add(new RectangleF(contentX, ry, contentW - 96 * S, 40 * S), L.T("Map openen"), delegate { OpenFolder(Overlay.ScreenshotDir); }, delegate(Graphics g, Item it, bool h)
            {
                RoundFill(g, it.R, h ? Raised : Card, 9);
                Icons.Draw(g, "map", new RectangleF(it.R.X + 8 * S, it.R.Y, 24 * S, it.R.Height), 16 * S, T.Icon, 2f);
                Txt(g, dir, new RectangleF(it.R.X + 38 * S, it.R.Y, it.R.Width - 46 * S, it.R.Height), T.Text, 12, false, StringAlignment.Near, false);
            });
            Add(new RectangleF(contentX + contentW - 88 * S, ry, 40 * S, 40 * S), L.T("Andere map kiezen"), delegate { ChooseShotFolder(); }, delegate(Graphics g, Item it, bool h)
            {
                RoundFill(g, it.R, h ? Raised : Field, 9);
                Icons.Draw(g, "pen", it.R, 16 * S, T.Icon, 2f);
            });
            Add(new RectangleF(contentX + contentW - 42 * S, ry, 40 * S, 40 * S), L.T("Terug naar Afbeeldingen\\NexusPen"), delegate { c.ShotFolder = ""; Changed(); BuildItems(); Invalidate(); }, delegate(Graphics g, Item it, bool h)
            {
                RoundFill(g, it.R, h ? Raised : Field, 9);
                Icons.Draw(g, "terugzetten", it.R, 16 * S, string.IsNullOrEmpty(c.ShotFolder) ? T.Muted : T.Icon, 2f);
            });
            y += 52 * S;
            // naar een andere computer: een bestandje met je instellingen
            y = Section(y, L.T("Naar een andere computer"));
            bx = contentX;
            bx += Button(bx, y, L.T("Instellingen exporteren"), "aftekenen", delegate { ExportSettings(); }, false) + 10 * S;
            bx += Button(bx, y, L.T("Instellingen importeren"), "map", delegate { ImportSettings(); }, false) + 10 * S;
            Button(bx, y, L.T("Tekeningen en instellingen"), "map", delegate { OpenFolder(StrokeStore.Dir); }, false);
        }

        void ChooseShotFolder()
        {
            using (FolderBrowserDialog fb = new FolderBrowserDialog())
            {
                fb.Description = L.T("Waar wil je je schermafbeeldingen bewaren?");
                fb.SelectedPath = Overlay.ScreenshotDir;
                fb.ShowNewFolderButton = true;
                if (fb.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(fb.SelectedPath)) return;
                Cfg.ShotFolder = fb.SelectedPath;
                Changed(); BuildItems(); Invalidate();
                Notice(L.T("Nieuwe schermafbeeldingen komen nu in deze map."));
            }
        }

        // Dingen die bij deze computer horen (plek van de balk, opstarten): niet meenemen naar een andere computer
        // (en je keuzes over privacy: gebruiksgegevens en de check op een nieuwe versie blijven zoals ze hier staan)
        static readonly string[] PerComputer = { "ToolbarX", "ToolbarY", "TileX", "TileY", "ShotFolder", "Vertical", "Usage", "UsageCats", "UpdateCheck" };

        void ExportSettings()
        {
            using (SaveFileDialog sd = new SaveFileDialog())
            {
                sd.Title = L.T("Instellingen exporteren");
                sd.Filter = L.T("NexusPen-instellingen") + " (*.nexuspen)|*.nexuspen";
                sd.FileName = "NexusPen-instellingen.nexuspen";
                sd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                // een kopie zonder wat bij deze computer hoort (plek van de balk, je eigen map met je Windows-naam erin)
                Settings copy = new Settings(), fresh = new Settings();
                foreach (System.Reflection.FieldInfo f in typeof(Settings).GetFields())
                    if (!f.IsStatic && !f.IsInitOnly) f.SetValue(copy, Array.IndexOf(PerComputer, f.Name) < 0 ? f.GetValue(Cfg) : f.GetValue(fresh));
                if (copy.TrySave(sd.FileName)) { Usage.Log("instelling", "exporteren", ""); Notice(L.T("Klaar. Zet dit bestandje op je andere computer en kies daar Importeren.")); }
                else Notice(L.T("Exporteren lukte niet."));
            }
        }

        void ImportSettings()
        {
            using (OpenFileDialog od = new OpenFileDialog())
            {
                od.Title = L.T("Instellingen importeren");
                od.Filter = L.T("NexusPen-instellingen") + " (*.nexuspen)|*.nexuspen";
                od.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (od.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (new FileInfo(od.FileName).Length > 256 * 1024 || !Settings.LooksLikeSettings(od.FileName)) { Notice(L.T("Dit is geen bestand met NexusPen-instellingen.")); return; }
                    if (MessageBox.Show(this, L.T("Je instellingen op deze computer vervangen door die uit het bestand? Je tekeningen blijven staan."), "NexusPen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { File.Copy(Settings.FilePath, Settings.FilePath + ".voor-import", true); } catch { }   // voor de zekerheid: de oude bewaren
                    Settings other = Settings.Load(od.FileName);   // leest alleen bekende instellingen, met de gewone controles
                    foreach (System.Reflection.FieldInfo f in typeof(Settings).GetFields())
                        if (!f.IsStatic && !f.IsInitOnly && Array.IndexOf(PerComputer, f.Name) < 0) f.SetValue(Cfg, f.GetValue(other));
                    Cfg.Clamp();
                    ov.RegisterGlobalHotkeys();
                    Usage.Log("instelling", "importeren", "");
                    Changed(); BuildItems(); Invalidate();
                    Notice(L.T("Je instellingen zijn overgezet."));
                }
                catch (Exception ex) { Log.Error("Instellingen importeren", ex); Notice(L.T("Importeren lukte niet.")); }
            }
        }

        static void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); Process.Start(Explorer, "\"" + dir + "\""); } catch (Exception ex) { Log.Error("Map openen", ex); }
        }

        public static string Explorer { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"); } }

        void BuildShare()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Schermdelen"), null);
            y = Toggle(y, L.T("Werkbalk verbergen bij schermdelen"), L.T("Cursisten zien in Zoom of Teams je tekeningen wel, maar de werkbalk niet"), delegate { return c.HideFromCapture; }, delegate(bool v) { c.HideFromCapture = v; });
            Para(y + 12 * S, L.T("Dit werkt vanaf Windows 10 versie 2004. Op een oudere Windows ziet de ander de werkbalk gewoon."), 40 * S);
        }

        // ---- Gebruik
        Usage.Summary summary;

        void BuildUsage()
        {
            Settings c = Cfg;
            summary = Usage.Make(7);
            float colW = contentW * 0.52f;
            float y = Heading(contentTop, L.T("Gebruik"), L.T("Wat NexusPen bijhoudt, zodat we zien wat je echt gebruikt en wat beter kan. Alleen op deze pc."));
            float keepW = contentW; contentW = colW;
            y = Toggle(y, L.T("Gebruik bijhouden"), L.T("Er gaat niets naar internet"), delegate { return c.Usage; }, delegate(bool v) { c.Usage = v; });
            string[] names = { "Klikken op knoppen", "Gereedschap en tijd", "Kleuren en dikte", "Menu's en instellingen", "Twijfel en fouten", "Lessen", "Scherm en snelheid" }; // vertaal
            for (int i = 0; i < names.Length; i++)
            {
                int bit = Usage.Categories[i];
                y = Toggle(y, L.T(names[i]), null, delegate { return (c.UsageCats & bit) != 0 && c.Usage; }, delegate(bool v) { if (v) c.UsageCats |= bit; else c.UsageCats &= ~bit; });
            }
            y = Para(y + 8 * S, L.T("Nooit: wat je schrijft of tekent, wat er op je scherm staat, namen of bestanden."), 40 * S);
            contentW = keepW;
            float rx = contentX + colW + 24 * S, rw = contentW - colW - 24 * S, ry = contentTop + 70 * S;
            Usage.Summary sm = summary;
            Later(delegate(Graphics g)
            {
                Theme t = T;
                RectangleF card = new RectangleF(rx, ry, rw, 360 * S);
                RoundFill(g, card, Card, 12);
                Txt(g, L.T("Deze week"), new RectangleF(rx + 16 * S, ry + 14 * S, rw, 20 * S), t.Text, 14, true, StringAlignment.Near, false);
                string[] vals = { sm.Lessons.ToString(), Math.Round(sm.DrawMinutes).ToString(), sm.Screenshots.ToString() };
                string[] labs = { L.T("keer gestart"), L.T("minuten getekend"), L.T("schermafbeeldingen") };
                float bw = (rw - 32 * S - 16 * S) / 3;
                for (int i = 0; i < 3; i++)
                {
                    RectangleF b = new RectangleF(rx + 16 * S + i * (bw + 8 * S), ry + 44 * S, bw, 58 * S);
                    RoundFill(g, b, Field, 8);
                    Txt(g, vals[i], new RectangleF(b.X + 10 * S, b.Y + 6 * S, b.Width, 26 * S), t.Text, 20, true, StringAlignment.Near, false);
                    Txt(g, labs[i], new RectangleF(b.X + 10 * S, b.Y + 32 * S, b.Width - 12 * S, 26 * S), t.Muted, 10, false, StringAlignment.Near, true);
                }
                Txt(g, L.T("Meest gebruikt"), new RectangleF(rx + 16 * S, ry + 118 * S, rw, 18 * S), t.Text, 12, true, StringAlignment.Near, false);
                int max = sm.TopClicks.Count > 0 ? sm.TopClicks[0].Value : 1;
                float yy = ry + 142 * S;
                for (int i = 0; i < Math.Min(6, sm.TopClicks.Count); i++)
                {
                    KeyValuePair<string, int> kv = sm.TopClicks[i];
                    Txt(g, Toolbar.NameOf(kv.Key), new RectangleF(rx + 16 * S, yy, 110 * S, 16 * S), t.Muted, 12, false, StringAlignment.Near, false);
                    RectangleF track = new RectangleF(rx + 130 * S, yy + 5 * S, rw - 190 * S, 7 * S);
                    RoundFill(g, track, Field, 3);
                    RoundFill(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * kv.Value / max), track.Height), t.Accent, 3);
                    Txt(g, kv.Value.ToString(), new RectangleF(rx + rw - 54 * S, yy, 40 * S, 16 * S), t.Muted, 12, false, StringAlignment.Far, false);
                    yy += 22 * S;
                }
                if (sm.TopClicks.Count == 0) Txt(g, L.T("Nog niets bijgehouden. Gebruik NexusPen gewoon, dan verschijnt het hier."), new RectangleF(rx + 16 * S, yy, rw - 32 * S, 40 * S), t.Muted, 12, false, StringAlignment.Near, true);
                List<string> never = sm.NeverUsed.ConvertAll(delegate(string k) { return Toolbar.NameOf(k); });
                if (never.Count > 0 && sm.TopClicks.Count > 0)
                    Txt(g, L.T("Nog nooit gebruikt") + ": " + string.Join(", ", never.ToArray()), new RectangleF(rx + 16 * S, ry + 284 * S, rw - 32 * S, 70 * S), t.Muted, 11, false, StringAlignment.Near, true);
            });
            float by = ry + 376 * S, bx = rx;
            bx += Button(bx, by, L.T("Exporteren voor Claude"), "map", delegate
            {
                try { string f = Usage.Export(); Notice(L.T("Klaar: het bestand staat op je bureaublad.")); try { Process.Start(Explorer, "/select,\"" + f + "\""); } catch { } }
                catch (Exception ex) { Log.Error("Gebruik exporteren", ex); Notice(L.T("Exporteren is niet gelukt.")); }
            }, true) + 8 * S;
            Button(bx, by, L.T("Wissen"), "prullenbak", delegate
            {
                if (MessageBox.Show(this, L.T("Alle bijgehouden gebruiksgegevens wissen?"), "NexusPen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { Usage.Clear(); BuildItems(); Invalidate(); }
            }, false);
        }

        // Uitleg bij elke knop van de werkbalk (in plaats van een tekstje als je erover beweegt)
        void BuildButtons()
        {
            float y = Heading(contentTop, L.T("Wat doet elke knop"), null);
            string[] keys = Toolbar.Catalog;
            int rows = (keys.Length + 1) / 2;
            float colW = (contentW - 24 * S) / 2, rowH = 66 * S;   // ruimte voor drie regels uitleg
            Mark(y + rows * rowH + 10 * S);
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                float x = contentX + (i / rows) * (colW + 24 * S), yy = y + (i % rows) * rowH;
                string name = Toolbar.NameOf(k);
                string tipText = ov.Bar != null ? ov.Bar.TipFor(k) : "";
                // de naam staat er al boven: niet nog een keer aan het begin van de uitleg
                if (tipText.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    tipText = tipText.Substring(name.Length).TrimStart(' ', ':', ',', '.');
                    if (tipText.Length > 0) tipText = char.ToUpper(tipText[0]) + tipText.Substring(1);
                }
                Later(delegate(Graphics g)
                {
                    Theme t = T;
                    if (k == "kleur")
                        using (SolidBrush b = new SolidBrush(ov.CurrentColor)) g.FillEllipse(b, x + 2 * S, yy + 4 * S, 16 * S, 16 * S);
                    else Icons.Draw(g, Toolbar.IconOf(k), new RectangleF(x, yy + 2 * S, 20 * S, 20 * S), 18 * S, t.Icon, 2f);
                    Txt(g, name, new RectangleF(x + 32 * S, yy, colW - 32 * S, 20 * S), t.Text, 13, true, StringAlignment.Near, false);
                    if (tipText.Length > 0) Txt(g, tipText, new RectangleF(x + 32 * S, yy + 20 * S, colW - 32 * S, rowH - 20 * S), t.Muted, 12, false, StringAlignment.Near, true);
                });
            }
        }

        // ---- Bijwerken: een kaart met hoe het ervoor staat, en een knop die past bij die stand
        int updState;          // 0 nog niet gekeken, 1 bezig, 2 nieuwste, 3 kon niet kijken
        DateTime updCheckedAt;

        void CheckNow()
        {
            updState = 1; BuildItems(); Invalidate();
            ov.CheckForUpdateNow(delegate(int r)
            {
                if (IsDisposed) return;
                updState = r == 1 ? 0 : r == 0 ? 2 : 3;
                updCheckedAt = DateTime.Now;
                BuildItems(); Invalidate();
            });
        }

        void BuildUpdate()
        {
            Settings c = Cfg;
            float y = Heading(contentTop, L.T("Bijwerken"), null);
            Updates.Info pu = ov.PendingUpdate;
            float cardH = 96 * S;
            RectangleF card = new RectangleF(contentX, y, contentW, cardH);
            string title, sub, icon; bool good = false, warn = false;
            if (pu != null) { title = L.F("Versie {0} staat klaar", pu.Version); sub = L.T("Je tekening en instellingen blijven staan. NexusPen start daarna vanzelf weer."); icon = "aftekenen"; }
            else if (updState == 1) { title = L.T("Even kijken..."); sub = L.T("NexusPen vraagt GitHub of er een nieuwe versie is."); icon = "terugzetten"; }
            else if (updState == 2) { title = L.T("Je hebt de nieuwste versie"); sub = L.F("NexusPen {0}, gekeken om {1}", Program.Version, updCheckedAt.ToString("HH:mm")); icon = "vink"; good = true; }
            else if (updState == 3) { title = L.T("Kijken lukte niet"); sub = L.T("Geen internet? Probeer het straks nog eens."); icon = "info"; warn = true; }
            else { title = "NexusPen " + Program.Version; sub = L.T("Kijk of er een nieuwe versie is."); icon = "info"; }
            Later(delegate(Graphics g)
            {
                Theme t = T;
                RoundFill(g, card, Card, 12);
                if (pu != null) RoundLine(g, card, t.Accent, 1.5f, 12);
                // rondje met een icoon: groen vinkje als alles goed is, oranje bij een nieuwe versie
                RectangleF dot = new RectangleF(card.X + 20 * S, card.Y + (cardH - 44 * S) / 2, 44 * S, 44 * S);
                Color dc = good ? ColorTranslator.FromHtml("#16A34A") : pu != null ? t.Accent : warn ? ColorTranslator.FromHtml("#DC2626") : Field;
                using (SolidBrush b = new SolidBrush(dc)) g.FillEllipse(b, dot);
                Icons.Draw(g, icon, dot, 20 * S, good || pu != null || warn ? Color.White : t.Icon, 2.4f);
                float tx = dot.Right + 16 * S, tw = card.Right - tx - 190 * S;
                Txt(g, title, new RectangleF(tx, card.Y + 24 * S, tw, 24 * S), t.Text, 16, true, StringAlignment.Near, false);
                Txt(g, sub, new RectangleF(tx, card.Y + 50 * S, tw, 22 * S), t.Muted, 12, false, StringAlignment.Near, false);
            });
            // knop rechts in de kaart
            string bl = pu != null ? L.T("Nu bijwerken") : L.T("Nu kijken");
            float bw = Measure(bl, 13, true) + 50 * S;
            Item b0 = Add(new RectangleF(card.Right - bw - 20 * S, card.Y + (cardH - 38 * S) / 2, bw, 38 * S), null, delegate
            {
                if (ov.PendingUpdate != null) ov.InstallUpdate();
                else if (updState != 1) CheckNow();
            }, delegate(Graphics g, Item it, bool h)
            {
                Theme t = T;
                bool primary = ov.PendingUpdate != null;
                RoundFill(g, it.R, primary ? t.Accent : h ? Raised : Field, 9);
                if (primary && h) RoundFill(g, it.R, Color.FromArgb(30, 255, 255, 255), 9);
                Icons.Draw(g, primary ? "aftekenen" : "terugzetten", new RectangleF(it.R.X + 12 * S, it.R.Y, 18 * S, it.R.Height), 16 * S, primary ? t.OnAccent : t.Icon, 2f);
                Txt(g, bl, new RectangleF(it.R.X + 36 * S, it.R.Y, it.R.Width - 44 * S, it.R.Height), primary ? t.OnAccent : t.Text, 13, true, StringAlignment.Near, false);
            });
            y += cardH + 16 * S;
            y = Toggle(y, L.T("Melding als er een nieuwe versie is"), L.T("NexusPen kijkt bij het starten even bij GitHub. Er gaat niets van jou mee."), delegate { return c.UpdateCheck; }, delegate(bool v) { c.UpdateCheck = v; });
            Button(contentX, y + 12 * S, L.T("Wat is er nieuw?"), "info", delegate
            {
                try { Process.Start(Updates.Page); } catch (Exception ex) { Log.Error("Downloadpagina openen", ex); }
            }, false);
        }

        // Terugzetten: twee duidelijk verschillende regels, allebei met een vraag vooraf
        float ResetRow(float y, string title, string sub, string button, Action run)
        {
            float x = contentX, w = contentW, h = 60 * S;
            float bw = Measure(button, 13, true) + 30 * S;
            Later(delegate(Graphics g)
            {
                Txt(g, title, new RectangleF(x + 10 * S, y + 10 * S, w - bw - 40 * S, 20 * S), T.Text, 14, true, StringAlignment.Near, false);
                Txt(g, sub, new RectangleF(x + 10 * S, y + 31 * S, w - bw - 40 * S, 20 * S), T.Muted, 12, false, StringAlignment.Near, false);
                using (SolidBrush b = new SolidBrush(T.Sep)) g.FillRectangle(b, x, y + h, w, 1 * S);
            });
            Add(new RectangleF(x + w - bw - 10 * S, y + (h - 34 * S) / 2, bw, 34 * S), null, run, delegate(Graphics g, Item it, bool hv)
            {
                RoundFill(g, it.R, hv ? Raised : Field, 9);
                Txt(g, button, it.R, T.Text, 13, true, StringAlignment.Center, false);
            });
            return y + h + 2 * S;
        }

        void BuildHelp()
        {
            float y = Heading(contentTop, L.T("Hulp en foutlog"), "NexusPen " + Program.Version + ", Nexus of Language");
            y = Toggle(y, L.T("Uitleg bij de knoppen"), L.T("Een tekstje tonen als je met de muis over een knop gaat"), delegate { return Cfg.ShowTips; }, delegate(bool v) { Cfg.ShowTips = v; }) + 8 * S;
            y = Para(y, L.T("Loopt er iets vast? Druk op Esc: je bent dan altijd terug bij de muis. Werkt dat niet, rechtsklik dan op het logo bij de klok en kies Noodstop. Is je tekening weg, kies daar Vorige tekening terughalen."), 64 * S);
            float bx = contentX;
            bx += Button(bx, y + 6 * S, L.T("Foutlog openen"), "info", delegate
            {
                if (!File.Exists(Log.FilePath)) { Notice(L.T("Er zijn nog geen fouten gelogd.")); return; }
                try { Process.Start(Log.FilePath); }
                catch { try { Process.Start(Explorer, "/select,\"" + Log.FilePath + "\""); } catch (Exception ex) { Log.Error("Foutlog openen", ex); } }
            }, false) + 10 * S;
            Button(bx, y + 6 * S, L.T("Map met licenties"), "map", delegate { OpenFolder(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "licenties")); }, false);
            y += 64 * S;
            y = Para(y, L.T("Icoontjes: Lucide (ISC-licentie). Lettertypes Andika, Atkinson Hyperlegible, Lexend, Nunito en Patrick Hand: SIL Open Font License."), 40 * S);
            // terugzetten: hier achteraan, niet op een pagina die je vaak gebruikt
            Settings c = Cfg;
            y = Section(y + 10 * S, L.T("Terugzetten"));
            y = ResetRow(y, L.T("Alleen de werkbalk"), L.T("De standaardknoppen in de standaardvolgorde"), L.T("Werkbalk terugzetten"), delegate
            {
                if (MessageBox.Show(this, L.T("De knoppen in de balk terugzetten naar de standaard? Je eigen volgorde en verborgen knoppen gaan dan verloren."), "NexusPen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                c.BarLayout = ""; c.ShortenAt = 0; Changed(); BuildItems(); Notice(L.T("De werkbalk heeft weer de standaardknoppen."));
            });
            y = ResetRow(y, L.T("Alle instellingen"), L.T("Thema, kleuren, sneltoetsen en de rest. Je tekeningen blijven staan."), L.T("Alles terugzetten"), delegate
            {
                if (MessageBox.Show(this, L.T("Alle instellingen terugzetten naar standaard? Je tekeningen blijven staan."), "NexusPen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Settings fresh = new Settings();
                bool keepUsage = c.Usage; int keepCats = c.UsageCats; string keepLang = c.Language;
                foreach (System.Reflection.FieldInfo f in typeof(Settings).GetFields()) if (!f.IsStatic && !f.IsInitOnly) f.SetValue(c, f.GetValue(fresh));
                c.Usage = keepUsage; c.UsageCats = keepCats; c.Language = keepLang;   // jouw keuze over gebruik en taal blijft
                ov.RegisterGlobalHotkeys();
                Changed(); BuildItems(); Notice(L.T("Alles staat weer op standaard."));
            });
            // snelheidsmeter: alleen tijden, nooit wat je tekent
            if (Perf.Ink.Count > 0 || Perf.Click.Count > 0)
                Para(y, L.F("Snelheid sinds het starten: inkt op het scherm na {0} ms (traagst {1} ms), een klik op een knop na {2} ms.",
                    Perf.Ink.Percentile(0.5).ToString("0.0"), Perf.Ink.Percentile(0.95).ToString("0.0"), Perf.Click.Percentile(0.5).ToString("0.0")), 40 * S);
        }

        // ---- tekenen
        protected override void OnPaint(PaintEventArgs e)
        {
            if (items.Count == 0) BuildItems();
            Graphics g = e.Graphics;
            Theme t = T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;   // ClearType en GridFit geven hier rare letterafstand
            g.Clear(t.Bar);
            float W0 = ClientSize.Width, H0 = ClientSize.Height, navW = 236 * S;
            using (SolidBrush b = new SolidBrush(Side)) g.FillRectangle(b, 0, 0, navW, H0);
            using (SolidBrush b = new SolidBrush(t.Sep)) g.FillRectangle(b, (float)Math.Round(navW), 0, Math.Max(1, (float)Math.Round(S)), H0);
            if (!dwmCorners)
            {
                float bw = Math.Max(1, (float)Math.Round((t.Thick ? 2 : 1) * S));
                using (Pen p = new Pen(t.Border, bw)) using (GraphicsPath gp = Svg.RoundRect(new RectangleF(bw / 2, bw / 2, W0 - bw, H0 - bw), 14 * S - bw / 2)) g.DrawPath(p, gp);
            }
            Logo.Draw(g, new RectangleF(22 * S, 18 * S, 28 * S, 28 * S), t.Accent, Logo.LetterColor(t.Accent, t.OnAccent));
            Txt(g, L.T("Instellingen"), new RectangleF(58 * S, 16 * S, navW - 64 * S, 32 * S), t.Text, 16, true, StringAlignment.Near, false);
            using (SolidBrush b = new SolidBrush(t.Sep)) foreach (float sy in navSeps) g.FillRectangle(b, 24 * S, (float)Math.Round(sy), navW - 48 * S, Math.Max(1, (float)Math.Round(S)));
            // de pagina zelf (kan scrollen): alleen binnen het vak rechts van de zijbalk
            RectangleF view = new RectangleF(navW + 1, 0, W0 - navW - 1, H0);
            GraphicsState gs = g.Save();
            g.SetClip(view);
            foreach (Action<Graphics> p in painters) p(g);
            for (int i = fixedItems; i < items.Count; i++) items[i].Paint(g, items[i], items[i] == hover && !dragActive);
            if (dragActive) PaintDrag(g);
            // gescrold: bovenin een vaste strook met de naam van de pagina, dan schuift er niets onder het kruisje door
            if (scroll > 1)
            {
                float bandH = 60 * S;
                using (SolidBrush b = new SolidBrush(t.Bar)) g.FillRectangle(b, navW + 1, 0, W0 - navW - 1, bandH);
                using (SolidBrush b = new SolidBrush(t.Sep)) g.FillRectangle(b, navW + 1, bandH, W0 - navW - 1, Math.Max(1, (float)Math.Round(S)));
                Txt(g, L.T(PageNames[page]), new RectangleF(navW + 30 * S, 0, W0 - navW - 110 * S, bandH), t.Text, 16, true, StringAlignment.Near, false);
            }
            g.Restore(gs);
            for (int i = 0; i < fixedItems && i < items.Count; i++) items[i].Paint(g, items[i], items[i] == hover && !dragActive);
            // dun schuifbalkje rechts als de pagina langer is dan het venster
            float ms = MaxScroll;
            if (ms > 0)
            {
                float total = H0 + ms, barH = Math.Max(40 * S, H0 * H0 / total), barY = (H0 - barH) * (scroll / ms);
                RoundFill(g, new RectangleF(W0 - 7 * S, barY + 4 * S, 4 * S, barH - 8 * S), T.IsLight ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255), 2);
            }
            if (notice != null && (DateTime.Now - noticeAt).TotalMilliseconds < 3000)
            {
                float nw = Measure(notice, 13, true) + 40 * S;
                RectangleF nr = new RectangleF(W0 - nw - 24 * S, H0 - 56 * S, nw, 38 * S);
                RoundFill(g, nr, Raised, 10); RoundLine(g, nr, t.Accent, 1.5f, 10);
                Txt(g, notice, nr, t.Text, 13, true, StringAlignment.Center, false);
            }
        }

        void PaintDrag(Graphics g)
        {
            if (dragKey == "col")
            {
                // streepje waar de kleur komt, en de kleur zelf onder je muis
                int ci = ColorInsertIndex(dragPos);
                if (colorSlots.Count > 0)
                {
                    RectangleF r = ci < colorSlots.Count ? colorSlots[ci] : colorSlots[colorSlots.Count - 1];
                    float x = ci < colorSlots.Count ? r.X - 4 * S : r.Right + 2 * S;
                    using (SolidBrush b = new SolidBrush(T.Accent)) g.FillRectangle(b, x, r.Y, 3 * S, r.Height);
                }
                Color[] cs = ov.Colors;
                if (dragFrom >= 0 && dragFrom < cs.Length)
                    using (SolidBrush b = new SolidBrush(cs[dragFrom])) g.FillEllipse(b, dragPos.X - 14 * S, dragPos.Y - 14 * S, 28 * S, 28 * S);
                return;
            }
            int ins = InsertIndex(dragPos);
            if (ins >= 0)
            {
                RectangleF r = ins < barSlots.Count ? barSlots[ins] : (barSlots.Count > 0 ? new RectangleF(barSlots[barSlots.Count - 1].Right + 4 * S, barSlots[barSlots.Count - 1].Y, 1, barSlots[barSlots.Count - 1].Height) : new RectangleF(barArea.X + 12 * S, barArea.Y + 10 * S, 1, 46 * S));
                using (SolidBrush b = new SolidBrush(T.Accent)) g.FillRectangle(b, r.X - 4 * S, r.Y, 3 * S, r.Height);
            }
            RectangleF ghost = new RectangleF(dragPos.X - 23 * S, dragPos.Y - 23 * S, 46 * S, 46 * S);
            GraphicsState st = g.Save();
            g.TranslateTransform(ghost.X + ghost.Width / 2, ghost.Y + ghost.Height / 2); g.RotateTransform(-4); g.TranslateTransform(-(ghost.X + ghost.Width / 2), -(ghost.Y + ghost.Height / 2));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) using (GraphicsPath gp = Svg.RoundRect(new RectangleF(ghost.X + 3 * S, ghost.Y + 6 * S, ghost.Width, ghost.Height), 11 * S)) g.FillPath(b, gp);
            RoundFill(g, ghost, Raised, 11);
            RoundLine(g, ghost, (dragFrom >= 0 && trayArea.Contains(dragPos)) ? ColorTranslator.FromHtml("#EF4444") : T.Accent, 1.5f, 11);
            if (dragKey == "|") using (SolidBrush b = new SolidBrush(T.Muted)) g.FillRectangle(b, ghost.X + ghost.Width / 2 - 1 * S, ghost.Y + 10 * S, 2 * S, ghost.Height - 20 * S);
            else Icons.Draw(g, Toolbar.IconOf(dragKey), ghost, 22 * S, T.Text, 2f);
            g.Restore(st);
        }

        // ---- muis
        // eerst de zijbalk en het kruisje (die liggen bovenop), dan de pagina (alleen wat te zien is)
        Item ItemAt(Point p)
        {
            for (int i = Math.Min(fixedItems, items.Count) - 1; i >= 0; i--) if (items[i].R.Contains(p)) return items[i];
            if (p.X <= 236 * S) return null;
            if (scroll > 1 && p.Y < 60 * S) return null;   // onder de vaste kop: niets klikbaar
            for (int i = items.Count - 1; i >= fixedItems; i--) if (items[i].R.Contains(p)) return items[i];
            return null;
        }
        Point pressAt; Item pressed;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Item it = ItemAt(e.Location);
            if (it != null && it.Key != null && it.Key.StartsWith("col:"))
            {
                int ci = int.Parse(it.Key.Substring(4));
                // kruisje rechtsboven: meteen weghalen
                if (hover == it && ColorBadge(it.R).Contains(e.Location) && ov.ColorCount > 1) { RemoveColorAt(ci); return; }   // alleen als het kruisje al te zien was
                pressed = it; pressAt = e.Location; dragActive = false; dragFrom = ci; dragKey = "col"; Capture = true;
                return;
            }
            if (it != null && hover == it && it.Key != null && it.Key.StartsWith("bar:") && !it.Key.EndsWith(":|") && KeyBadge(it.R).Contains(e.Location))
            {
                string[] bp = it.Key.Split(new char[] { ':' }, 3);
                int bi;
                if (bp.Length == 3 && bp[2] != "instellingen" && int.TryParse(bp[1], out bi)) { RemoveKeyFromBar(bi); return; }
            }
            if (it != null && it.Key != null)
            {
                // knop in Werkbalk: slepen begint pas als je beweegt
                pressed = it; pressAt = e.Location; dragActive = false;
                List<string> layout = Toolbar.Layout(Cfg);
                if (it.Key.StartsWith("bar:"))
                {
                    string[] parts = it.Key.Split(new char[] { ':' }, 3);
                    int idx;
                    if (parts.Length < 3 || !int.TryParse(parts[1], out idx) || idx >= layout.Count || layout[idx] != parts[2]) { pressed = null; BuildItems(); Invalidate(); return; }  // balk is intussen veranderd
                    dragFrom = idx; dragKey = parts[2];
                }
                else { dragFrom = -1; dragKey = it.Key.Substring(5); }
                Capture = true;
                return;
            }
            if (it != null) { if (it.Click != null) it.Click(); if (!IsDisposed) Invalidate(); return; }
            if (e.X > 236 * S && e.Y < 60 * S || e.X < 236 * S && e.Y < 56 * S)
            {
                W.ReleaseCapture();
                W.SendMessage(Handle, W.WM_NCLBUTTONDOWN, new IntPtr(W.HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed != null)
            {
                if (!dragActive && (Math.Abs(e.X - pressAt.X) > 4 || Math.Abs(e.Y - pressAt.Y) > 4)) dragActive = true;
                if (dragActive) { dragPos = e.Location; Cursor = Cursors.SizeAll; Invalidate(); }
                return;
            }
            Item it = ItemAt(e.Location);
            if (it != hover)
            {
                hover = it;
                Invalidate();
                if (it != null && it.Tip != null) tip.Show(it.Tip, this, (int)it.R.X, (int)(it.R.Bottom + 6 * S), 2500); else tip.Hide(this);
            }
            Cursor = it != null ? (it.Key != null ? Cursors.SizeAll : Cursors.Hand) : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressed == null) return;
            bool was = dragActive;
            Item wasItem = pressed;
            pressed = null; dragActive = false;   // eerst opruimen, dan loslaten (anders ruimt CaptureChanged het al op)
            Capture = false;
            if (dragKey == "col") { dragKey = null; if (was) DropColor(e.Location); else if (wasItem.Click != null) wasItem.Click(); Invalidate(); return; }
            if (was) DropKey(e.Location);
            else if (wasItem.Key != null && wasItem.Key.StartsWith("tray:")) { AddKeyToBar(wasItem.Key.Substring(5)); return; }   // klik op een knop in de bak: erbij
            Invalidate();
        }

        // muis kwijt tijdens slepen (Alt+Tab): geen spookknop laten hangen
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && pressed != null) { pressed = null; dragActive = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = null; Invalidate(); }
    }
}
