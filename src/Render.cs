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
    // ------------------------------------------------------------------ Lettertypes: Windows-fonts plus Andika en Nunito die in NexusPen zelf zitten
    static class Fonts
    {
        static readonly PrivateFontCollection own = new PrivateFontCollection();
        static readonly Dictionary<string, FontFamily> cache = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool loaded;

        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        // Ingebouwde lettertypes laden (geheugen blijft bewust bestaan zolang NexusPen draait)
        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                System.Reflection.Assembly asm = typeof(Fonts).Assembly;
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (!res.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] data = null;
                    int before = own.Families.Length;
                    try
                    {
                        using (Stream st = asm.GetManifestResourceStream(res))
                        using (MemoryStream ms = new MemoryStream())
                        {
                            st.CopyTo(ms);
                            data = ms.ToArray();
                            IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                            Marshal.Copy(data, 0, mem, data.Length);
                            own.AddMemoryFont(mem, data.Length);
                            try { uint n = 0; AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n); } catch { }
                        }
                    }
                    catch (Exception ex) { Log.Error("Lettertype " + res + " uit het geheugen laden", ex); }
                    // Lukt het niet uit het geheugen, dan via een tijdelijk bestand (Windows is daar soms soepeler)
                    if (data != null && own.Families.Length == before && !res.Contains("-Bold"))
                    {
                        try
                        {
                            string dir = Path.Combine(Path.GetTempPath(), "NexusPen-lettertypes");
                            Directory.CreateDirectory(dir);
                            string file = Path.Combine(dir, Path.GetFileName(res));
                            if (!File.Exists(file) || new FileInfo(file).Length != data.Length) File.WriteAllBytes(file, data);
                            own.AddFontFile(file);
                        }
                        catch (Exception ex) { Log.Error("Lettertype " + res + " uit een bestand laden", ex); }
                    }
                }
                foreach (FontFamily f in own.Families) cache[f.Name] = f;
            }
            catch (Exception ex) { Log.Error("Ingebouwde lettertypes laden", ex); }
        }

        // Ingebouwde lettertypes zonder vette versie. Windows maakt die anders zelf na en dan klopt de letter niet meer.
        static readonly string[] RegularOnly = { "Patrick Hand" };

        // Voor de zelftest: welke lettertypes zijn er echt geladen?
        public static List<string> Report()
        {
            Load();
            List<string> r = new List<string>();
            foreach (string res in typeof(Fonts).Assembly.GetManifestResourceNames()) if (res.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) r.Add("bron: " + res);
            foreach (FontFamily f in own.Families) r.Add("geladen: " + f.Name + "  vet=" + f.IsStyleAvailable(FontStyle.Bold) + " gewoon=" + f.IsStyleAvailable(FontStyle.Regular));
            foreach (string n in new string[] { "Andika", "Atkinson Hyperlegible", "Lexend", "Nunito", "Patrick Hand" })
            {
                FontFamily f = Family(n);
                r.Add("zoek " + n + " -> " + f.Name);
            }
            return r;
        }

        public static bool HasBold(FontFamily f)
        {
            if (Array.IndexOf(RegularOnly, f.Name) >= 0) return false;
            try { return f.IsStyleAvailable(FontStyle.Bold); } catch { return false; }
        }

        public static bool Has(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Family(name, false) != null;
        }

        static FontFamily Family(string name, bool fallback)
        {
            Load();
            FontFamily f;
            if (!string.IsNullOrEmpty(name))
            {
                if (cache.TryGetValue(name, out f)) return f;
                if (!missing.Contains(name))
                {
                    try
                    {
                        f = new FontFamily(name);
                        if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { cache[name] = f; return f; }
                        f.Dispose();
                    }
                    catch { }
                    missing.Add(name);
                }
            }
            if (!fallback) return null;
            if (name != "Segoe UI" && Has("Segoe UI")) return Family("Segoe UI", true);
            return FontFamily.GenericSansSerif;
        }

        // Nooit weggooien: deze families worden gedeeld
        public static FontFamily Family(string name) { return Family(name, true); }

        public static Font Make(string name, float px, FontStyle style)
        {
            FontFamily f = Family(name);
            try
            {
                if ((style & FontStyle.Bold) != 0 && !HasBold(f)) style &= ~FontStyle.Bold;
                if (!f.IsStyleAvailable(style)) style = HasBold(f) ? FontStyle.Bold : FontStyle.Regular;
                return new Font(f, px, style, GraphicsUnit.Pixel);
            }
            catch { return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
        }
    }

    // ------------------------------------------------------------------ Tekenen van lijnen, vormen, tekst en stempels
    static class Render
    {
        static readonly Graphics measureG = Graphics.FromImage(new Bitmap(1, 1));
        static readonly Dictionary<string, bool> installed = new Dictionary<string, bool>();

        public static float MarkerWidth(float w) { return w * 3 + 8; }
        public static float StampSize(float w) { return 22 + w * 4; }

        public static Pen MakePen(Color c, float w)
        {
            Pen p = new Pen(c, w);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }

        public static bool IsInstalled(string name) { return Fonts.Has(name); }

        public static string FontOrDefault(string name) { return Fonts.Has(name) ? name : "Segoe UI"; }

        public static Font MakeFont(float px) { return MakeFont(px, null); }
        public static Font MakeFont(float px, string name) { return Fonts.Make(name, px, FontStyle.Bold); }

        public static RectangleF Norm(PointF a, PointF b)
        {
            return new RectangleF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(1, Math.Abs(a.X - b.X)), Math.Max(1, Math.Abs(a.Y - b.Y)));
        }

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        static void Free(Graphics g, List<PointF> pts, Color c, float w)
        {
            if (pts.Count == 0) return;
            if (pts.Count == 1)
            {
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, pts[0].X - w / 2, pts[0].Y - w / 2, w, w);
                return;
            }
            using (Pen p = MakePen(c, w))
            using (GraphicsPath gp = CurvePath(pts, 0, pts.Count - 2))
                g.DrawPath(p, gp);
        }

        // ---- vloeiende lijn door de punten (zoals DrawCurve met spanning 0,5), maar per stukje uit te rekenen.
        // Daardoor kunnen we tijdens het schrijven alleen het nieuwste stukje tekenen, precies gelijk aan de hele lijn.
        public const float CurveK = 0.15f;

        // Stuurpunten van stukje i (van punt i naar punt i+1)
        public static void Segment(List<PointF> p, int i, out PointF c1, out PointF c2)
        {
            int n = p.Count;
            PointF a = p[i], b = p[i + 1];
            PointF prev = i > 0 ? p[i - 1] : a, next = i + 2 < n ? p[i + 2] : b;
            if (n == 2) { c1 = a; c2 = b; return; }
            float k = CurveK;
            if (i == 0) c1 = new PointF(a.X + k * (b.X - a.X), a.Y + k * (b.Y - a.Y));
            else c1 = new PointF(a.X + k * (b.X - prev.X), a.Y + k * (b.Y - prev.Y));
            if (i + 2 >= n) c2 = new PointF(b.X + k * (a.X - b.X), b.Y + k * (a.Y - b.Y));
            else c2 = new PointF(b.X - k * (next.X - a.X), b.Y - k * (next.Y - a.Y));
        }

        // Omtrek van stukje i, inclusief de stuurpunten (de bocht blijft daar altijd binnen)
        public static RectangleF SegmentBox(List<PointF> p, int i)
        {
            PointF c1, c2;
            Segment(p, i, out c1, out c2);
            PointF a = p[i], b = p[i + 1];
            float x0 = Math.Min(Math.Min(a.X, b.X), Math.Min(c1.X, c2.X)), x1 = Math.Max(Math.Max(a.X, b.X), Math.Max(c1.X, c2.X));
            float y0 = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c1.Y, c2.Y)), y1 = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c1.Y, c2.Y));
            return RectangleF.FromLTRB(x0, y0, x1, y1);
        }

        // Pad met de stukjes van 'from' tot en met 'to' als een doorlopende lijn
        public static GraphicsPath CurvePath(List<PointF> p, int from, int to)
        {
            GraphicsPath gp = new GraphicsPath();
            if (p.Count < 2 || to < from) return gp;
            PointF[] pts = new PointF[(to - from + 1) * 3 + 1];
            pts[0] = p[from];
            int k = 1;
            for (int i = from; i <= to; i++)
            {
                PointF c1, c2;
                Segment(p, i, out c1, out c2);
                pts[k++] = c1; pts[k++] = c2; pts[k++] = p[i + 1];
            }
            gp.AddBeziers(pts);
            return gp;
        }

        // Alleen de stukjes die het gebied raken (voor natte inkt). Aaneengesloten stukjes blijven een lijn.
        static GraphicsPath PartialPath(List<PointF> p, RectangleF area, float pad)
        {
            GraphicsPath gp = new GraphicsPath();
            RectangleF a = RectangleF.Inflate(area, pad, pad);
            int runStart = -1;
            for (int i = 0; i <= p.Count - 2; i++)
            {
                bool hit = SegmentBox(p, i).IntersectsWith(a) || PointIn(p[i], a) || PointIn(p[i + 1], a);
                if (hit) { if (runStart < 0) runStart = i; continue; }
                if (runStart >= 0) { AddRun(gp, p, runStart, i - 1); runStart = -1; }
            }
            if (runStart >= 0) AddRun(gp, p, runStart, p.Count - 2);
            return gp;
        }

        static bool PointIn(PointF q, RectangleF r) { return q.X >= r.Left && q.X <= r.Right && q.Y >= r.Top && q.Y <= r.Bottom; }

        static void AddRun(GraphicsPath gp, List<PointF> p, int from, int to)
        {
            using (GraphicsPath run = CurvePath(p, from, to)) { gp.StartFigure(); gp.AddPath(run, false); }
        }

        // Natte inkt: teken van een lijn die je nog aan het schrijven bent alleen wat in 'area' valt.
        // Het resultaat in 'area' is pixel voor pixel gelijk aan de hele lijn tekenen.
        public static void DrawWet(Graphics g, Stroke s, Rectangle area)
        {
            if (s.Pts.Count < 2 || (s.Tool != Tool.Pen && s.Tool != Tool.Marker && s.Tool != Tool.Laser)) { Draw(g, s, 1f); return; }
            float widest = s.Tool == Tool.Marker ? MarkerWidth(s.Width) : s.Tool == Tool.Laser ? s.Width * 3 + 10 : s.Width;
            // stukjes ruim meenemen: het begin en eind van een stuk liggen dan altijd buiten 'area',
            // zodat binnen 'area' de lijn precies zo getekend wordt als de hele lijn
            using (GraphicsPath gp = PartialPath(s.Pts, area, widest + 8))
            {
                if (gp.PointCount == 0) return;
                switch (s.Tool)
                {
                    case Tool.Pen:
                        if ((s.Style & Stroke.Chalk) != 0) using (Pen p = ChalkPen(s.Color, s.Width)) g.DrawPath(p, gp);
                        else using (Pen p = MakePen(s.Color, s.Width)) g.DrawPath(p, gp);
                        break;
                    case Tool.Marker: using (Pen p = MakePen(Color.FromArgb(110, s.Color), MarkerWidth(s.Width))) g.DrawPath(p, gp); break;
                    case Tool.Laser:
                        using (Pen p = MakePen(Color.FromArgb(60, s.Color), s.Width * 3 + 10)) g.DrawPath(p, gp);
                        using (Pen p = MakePen(Color.FromArgb(255, s.Color), s.Width + 2)) g.DrawPath(p, gp);
                        using (Pen p = MakePen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, s.Width / 2.5f))) g.DrawPath(p, gp);
                        break;
                }
            }
        }

        static Pen ChalkPen(Color c, float w)
        {
            Pen p = new Pen(ChalkBrush(c), w);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }

        static void Arrow(Graphics g, PointF a, PointF b, Color c, float w)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 2) return;
            float ux = dx / len, uy = dy / len;
            float head = Math.Min(len, Math.Max(14f, w * 4f)), hw = head * 0.6f;
            PointF bp = new PointF(b.X - ux * head, b.Y - uy * head);
            using (Pen p = MakePen(c, w)) g.DrawLine(p, a, new PointF(b.X - ux * head * 0.6f, b.Y - uy * head * 0.6f));
            PointF[] tri = { b, new PointF(bp.X - uy * hw, bp.Y + ux * hw), new PointF(bp.X + uy * hw, bp.Y - ux * hw) };
            using (SolidBrush br = new SolidBrush(c)) g.FillPolygon(br, tri);
        }

        // Tekst als vorm, zodat er een nette zwarte rand omheen kan
        static GraphicsPath TextPath(Stroke s)
        {
            GraphicsPath gp = new GraphicsPath();
            FontFamily ff = Fonts.Family(s.FontName);
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                gp.AddString(s.Text, ff, (int)(Fonts.HasBold(ff) ? FontStyle.Bold : FontStyle.Regular), s.FontSize, new PointF(s.Pts[0].X + OutlineWidth(s), s.Pts[0].Y + OutlineWidth(s)), sf);
            }
            return gp;
        }

        public static float OutlineWidth(Stroke s)
        {
            if (!s.Outline) return 0;
            float div = s.OutlineWidth == 0 ? 14f : s.OutlineWidth == 2 ? 6f : 9f;
            return Math.Max(1.5f, s.FontSize / div);
        }

        public static Color OutlineColorOf(Stroke s)
        {
            if (s.OutlineColor == 1) return Color.White;
            if (s.OutlineColor == 2) return Theme.Luma(s.Color) < 0.35 ? Color.White : Color.Black;
            return Color.Black;
        }

        // Krijtstreep: dezelfde lijn, maar met een korrelige structuur zoals krijt op een bord
        static readonly Dictionary<int, TextureBrush> chalkCache = new Dictionary<int, TextureBrush>();

        static TextureBrush ChalkBrush(Color c)
        {
            TextureBrush tb;
            if (chalkCache.TryGetValue(c.ToArgb(), out tb)) return tb;
            Bitmap b = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
            Random rnd = new Random(c.ToArgb());
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    int r = rnd.Next(100);
                    int a = r < 12 ? 40 : r < 30 ? 150 : 235;
                    b.SetPixel(x, y, Color.FromArgb(a * c.A / 255, c));
                }
            tb = new TextureBrush(b, WrapMode.Tile);
            if (chalkCache.Count > 40) { foreach (TextureBrush old in chalkCache.Values) { try { old.Image.Dispose(); } catch { } old.Dispose(); } chalkCache.Clear(); }
            chalkCache[c.ToArgb()] = tb;
            return tb;
        }

        static void FreeChalk(Graphics g, List<PointF> pts, Color c, float w)
        {
            if (pts.Count < 2) { Free(g, pts, c, w); return; }
            using (Pen p = ChalkPen(c, w))
            using (GraphicsPath gp = CurvePath(pts, 0, pts.Count - 2))
                g.DrawPath(p, gp);
        }

        static void DrawText(Graphics g, Stroke s)
        {
            if (!s.Outline)
            {
                using (Font f = MakeFont(s.FontSize, s.FontName)) using (SolidBrush br = new SolidBrush(s.Color)) g.DrawString(s.Text, f, br, s.Pts[0]);
                return;
            }
            using (GraphicsPath gp = TextPath(s))
            {
                Color edge = OutlineColorOf(s);
                using (Pen p = new Pen(edge, OutlineWidth(s) * 2)) { p.LineJoin = LineJoin.Round; g.DrawPath(p, gp); }
                using (SolidBrush br = new SolidBrush(s.Color)) g.FillPath(br, gp);
            }
        }

        // Stempels: vinkje, kruis, ster, vraagteken of een nummer. Het soort staat in Text ("vink", "kruis", "ster", "vraag" of "#3").
        public static readonly string[] StampKinds = { "vink", "kruis", "ster", "vraag", "nummer" };
        public static readonly float[] StampSizes = { 28, 40, 56 };

        public static Color StampColor(string kind)
        {
            switch (kind)
            {
                case "kruis": return ColorTranslator.FromHtml("#FF1F3D");
                case "ster": return ColorTranslator.FromHtml("#FFD400");
                case "vraag": return ColorTranslator.FromHtml("#2962FF");
                case "nummer": return ColorTranslator.FromHtml("#1F1F23");
                default: return ColorTranslator.FromHtml("#00C853");
            }
        }

        static void DrawStamp(Graphics g, Stroke s)
        {
            float size = s.FontSize > 0 ? s.FontSize : StampSize(s.Width);
            string kind = string.IsNullOrEmpty(s.Text) ? "vink" : s.Text;
            RectangleF r = new RectangleF(s.Pts[0].X - size / 2, s.Pts[0].Y - size / 2, size, size);
            if (kind.StartsWith("#"))
            {
                using (SolidBrush b = new SolidBrush(s.Color)) g.FillEllipse(b, r);
                using (Pen p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1.5f, size / 22f))) g.DrawEllipse(p, r);
                using (Font f = Fonts.Make("Nunito", size * 0.5f, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(Theme.Luma(s.Color) > 0.6 ? Color.Black : Color.White))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(kind.Substring(1), f, b, new RectangleF(r.X, r.Y + size * 0.02f, r.Width, r.Height), sf);
                }
                return;
            }
            if (kind == "ster")
            {
                Icons.Fill(g, "ster", r, s.Color);
                GraphicsState st2 = g.Save();
                g.TranslateTransform(r.X, r.Y); g.ScaleTransform(size / 24f, size / 24f);
                using (Pen p = new Pen(Color.FromArgb(160, 90, 60, 0), 1.4f)) { p.LineJoin = LineJoin.Round; foreach (GraphicsPath gp in Icons.Paths("ster")) g.DrawPath(p, gp); }
                g.Restore(st2);
                return;
            }
            string icon = kind == "kruis" ? "kruis" : kind == "vraag" ? "vraag" : "vink";
            // donkere rand eronder, dan zie je hem op elke achtergrond
            Icons.Draw(g, icon, r, size, Color.FromArgb(110, 0, 0, 0), 4.4f);
            Icons.Draw(g, icon, r, size, s.Color, 3f);
        }

        public static void Draw(Graphics g, Stroke s, float fade) { Draw(g, s, fade, 1f); }

        // 'alpha' maakt pen en markeerstift doorzichtiger (voor lijnen die vervagen), zonder de lijn te kopieren
        public static void Draw(Graphics g, Stroke s, float fade, float alpha)
        {
            if (s.Pts.Count == 0) return;
            PointF a = s.Pts[0], b = s.Pts[s.Pts.Count - 1];
            switch (s.Tool)
            {
                case Tool.Pen:
                {
                    Color pc = alpha >= 1f ? s.Color : Color.FromArgb(Math.Max(0, Math.Min(255, (int)(s.Color.A * alpha))), s.Color);
                    if ((s.Style & Stroke.Chalk) != 0) FreeChalk(g, s.Pts, pc, s.Width); else Free(g, s.Pts, pc, s.Width);
                    break;
                }
                case Tool.Marker: Free(g, s.Pts, Color.FromArgb(Math.Max(0, Math.Min(255, (int)(110 * alpha))), s.Color), MarkerWidth(s.Width)); break;
                case Tool.Laser:
                    Free(g, s.Pts, Color.FromArgb((int)(60 * fade), s.Color), s.Width * 3 + 10);
                    Free(g, s.Pts, Color.FromArgb((int)(255 * fade), s.Color), s.Width + 2);
                    Free(g, s.Pts, Color.FromArgb((int)(200 * fade), 255, 255, 255), Math.Max(1f, s.Width / 2.5f));
                    break;
                case Tool.Line: using (Pen p = MakePen(s.Color, s.Width)) g.DrawLine(p, a, b); break;
                case Tool.Arrow: Arrow(g, a, b, s.Color, s.Width); break;
                case Tool.Rect: using (Pen p = MakePen(s.Color, s.Width)) { RectangleF r = Norm(a, b); g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); } break;
                case Tool.Ellipse: using (Pen p = MakePen(s.Color, s.Width)) g.DrawEllipse(p, Norm(a, b)); break;
                case Tool.Stamp: DrawStamp(g, s); break;
                case Tool.Text:
                    if (string.IsNullOrEmpty(s.Text)) break;
                    DrawText(g, s);
                    break;
            }
        }

        public static RectangleF TextBounds(Stroke s)
        {
            using (Font f = MakeFont(s.FontSize, s.FontName))
            {
                SizeF sz = measureG.MeasureString(s.Text ?? "", f);
                float o = OutlineWidth(s) * 2;
                return new RectangleF(s.Pts[0], new SizeF(sz.Width + o, sz.Height + o));
            }
        }

        // Omtrek van een item: voor selecteren, verplaatsen en het bijwerken van het scherm
        public static RectangleF Bounds(Stroke s)
        {
            if (s.Pts.Count == 0) return RectangleF.Empty;
            // onthouden zolang de lijn niet verandert (tekst opmeten is vrij traag)
            if (s.CacheValid()) return s.BCache;
            RectangleF r = ComputeBounds(s);
            s.SetCache(r);
            return r;
        }

        static RectangleF ComputeBounds(Stroke s)
        {
            if (s.Tool == Tool.Text) return TextBounds(s);
            if (s.Tool == Tool.Stamp)
            {
                float z = s.FontSize > 0 ? s.FontSize : StampSize(s.Width);
                return new RectangleF(s.Pts[0].X - z / 2, s.Pts[0].Y - z / 2, z, z);
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (PointF p in s.Pts)
            {
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y;
            }
            // een vloeiende lijn kan in een scherpe bocht iets buiten de punten lopen: de stuurpunten meetellen
            if ((s.Tool == Tool.Pen || s.Tool == Tool.Marker || s.Tool == Tool.Laser) && s.Pts.Count >= 3)
                for (int i = 0; i < s.Pts.Count - 1; i++)
                {
                    RectangleF sb = SegmentBox(s.Pts, i);
                    if (sb.Left < minX) minX = sb.Left; if (sb.Top < minY) minY = sb.Top;
                    if (sb.Right > maxX) maxX = sb.Right; if (sb.Bottom > maxY) maxY = sb.Bottom;
                }
            float pad = s.Tool == Tool.Marker ? MarkerWidth(s.Width) / 2 : s.Width / 2;
            return RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
        }

        public static bool Hit(Stroke s, PointF p, float r)
        {
            if (s.Pts.Count == 0) return false;
            if (s.Tool == Tool.Text || s.Tool == Tool.Stamp) { RectangleF tb = Bounds(s); tb.Inflate(r, r); return tb.Contains(p); }
            float w = (s.Tool == Tool.Marker ? MarkerWidth(s.Width) : s.Width) + r * 2;
            if (s.Pts.Count == 1)
            {
                float dx = p.X - s.Pts[0].X, dy = p.Y - s.Pts[0].Y;
                return Math.Sqrt(dx * dx + dy * dy) <= w / 2;
            }
            // snel vooraf: ligt het punt wel in de buurt van deze lijn?
            RectangleF bb = Bounds(s);
            float m = w + (s.Tool == Tool.Arrow ? s.Width * 4 + 12 : 0);
            if (p.X < bb.Left - m || p.X > bb.Right + m || p.Y < bb.Top - m || p.Y > bb.Bottom + m) return false;
            if (s.Tool != Tool.Rect && s.Tool != Tool.Ellipse)
            {
                // pen, markeerstift, lijn en pijl: afstand tot elk stukje lijn, zonder iets aan te maken
                int last = s.Tool == Tool.Line || s.Tool == Tool.Arrow ? 1 : s.Pts.Count - 1;
                PointF first = s.Pts[0], end = s.Pts[s.Pts.Count - 1];
                float half = w / 2, h2 = half * half;
                for (int i = 0; i < last; i++)
                {
                    PointF a0 = last == 1 ? first : s.Pts[i], b0 = last == 1 ? end : s.Pts[i + 1];
                    float vx = b0.X - a0.X, vy = b0.Y - a0.Y, len = vx * vx + vy * vy;
                    float tt = len > 0 ? Math.Max(0, Math.Min(1, ((p.X - a0.X) * vx + (p.Y - a0.Y) * vy) / len)) : 0;
                    float ex = a0.X + tt * vx - p.X, ey = a0.Y + tt * vy - p.Y;
                    if (ex * ex + ey * ey <= h2) return true;
                }
                return false;
            }
            using (GraphicsPath gp = new GraphicsPath())
            {
                PointF a = s.Pts[0], b = s.Pts[s.Pts.Count - 1];
                switch (s.Tool)
                {
                    case Tool.Line: case Tool.Arrow: gp.AddLine(a, b); break;
                    case Tool.Rect: gp.AddRectangle(Norm(a, b)); break;
                    case Tool.Ellipse: gp.AddEllipse(Norm(a, b)); break;
                    default: gp.AddLines(s.Pts.ToArray()); break;
                }
                using (Pen pen = new Pen(Color.Black, w)) return gp.IsOutlineVisible(p, pen);
            }
        }

        public static void Move(Stroke s, float dx, float dy)
        {
            for (int i = 0; i < s.Pts.Count; i++) s.Pts[i] = new PointF(s.Pts[i].X + dx, s.Pts[i].Y + dy);
        }
    }
}
