using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace NexusPen
{
    // ------------------------------------------------------------------ Waar staat elke letter van een tekst?
    // Alle tekst wordt als vorm getekend (met of zonder rand), met precies deze maten. Daardoor weten we voor
    // het knipperende streepje, het selecteren en klikken met de muis exact waar elke letter staat.
    class TextLayout : IDisposable
    {
        static readonly Graphics measureG = MakeMeasure();
        static Graphics MakeMeasure()
        {
            Graphics g = Graphics.FromImage(new Bitmap(1, 1));
            g.TextRenderingHint = TextRenderingHint.AntiAlias;   // geen hinting: dezelfde breedtes als de getekende vorm
            g.PageUnit = GraphicsUnit.Pixel;
            return g;
        }

        public readonly string[] Lines;
        public readonly PointF Origin;     // linksboven van de eerste regel letters
        public readonly float LineH, Size;
        public readonly FontFamily Family;
        public readonly FontStyle Style;
        readonly Font font;
        readonly StringFormat typo;

        public TextLayout(Stroke s)
        {
            string text = s.Text ?? "";
            Lines = text.Split('\n');
            Size = Math.Max(1, s.FontSize);
            font = Render.MakeFont(Size, s.FontName);
            Family = font.FontFamily; Style = font.Style;
            typo = (StringFormat)StringFormat.GenericTypographic.Clone();
            typo.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            float lh = Size * 1.2f;
            try { float em = Family.GetEmHeight(Style), ls = Family.GetLineSpacing(Style); if (em > 0 && ls > 0) lh = Size * ls / em; }
            catch { try { lh = font.GetHeight(measureG); } catch { } }
            LineH = lh;
            float ow = Render.OutlineWidth(s);
            // zonder rand liet DrawString vroeger links een klein randje; dat houden we, dan schuiven oude teksten niet
            float pad = 0;
            if (!s.Outline) try { pad = (measureG.MeasureString("I", font).Width - measureG.MeasureString("I", font, PointF.Empty, typo).Width) / 2; } catch { }
            Origin = new PointF(s.Pts[0].X + ow + pad, s.Pts[0].Y + ow);
        }

        public void Dispose() { font.Dispose(); typo.Dispose(); }

        public float Width(string part)
        {
            if (string.IsNullOrEmpty(part)) return 0;
            try { return measureG.MeasureString(part, font, PointF.Empty, typo).Width; }
            catch { return part.Length * Size * 0.5f; }
        }

        public float X(int line, int col)
        {
            string l = Lines[Math.Max(0, Math.Min(Lines.Length - 1, line))];
            return Origin.X + Width(l.Substring(0, Math.Max(0, Math.Min(l.Length, col))));
        }

        public float LineTop(int line) { return Origin.Y + line * LineH; }

        public void ToLineCol(int index, out int line, out int col)
        {
            int i = Math.Max(0, index);
            for (line = 0; line < Lines.Length; line++)
            {
                if (i <= Lines[line].Length) { col = i; return; }
                i -= Lines[line].Length + 1;
            }
            line = Lines.Length - 1; col = Lines[line].Length;
        }

        public int ToIndex(int line, int col)
        {
            line = Math.Max(0, Math.Min(Lines.Length - 1, line));
            int idx = 0;
            for (int i = 0; i < line; i++) idx += Lines[i].Length + 1;
            return idx + Math.Max(0, Math.Min(Lines[line].Length, col));
        }

        // Op welke letterpositie zit dit punt (voor klikken en slepen met de muis)
        public int Hit(PointF p)
        {
            int line = (int)Math.Floor((p.Y - Origin.Y) / LineH);
            line = Math.Max(0, Math.Min(Lines.Length - 1, line));
            return ToIndex(line, ColAt(line, p.X));
        }

        // Dichtstbijzijnde letterpositie: zoeken door steeds te halveren (ook lange regels blijven snel)
        public int ColAt(int line, float x)
        {
            string l = Lines[line];
            int lo = 0, hi = l.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (X(line, mid) < x) lo = mid + 1; else hi = mid;
            }
            // lo is de eerste positie op of na x; kies tussen lo en de positie ervoor
            if (lo > 0 && Math.Abs(X(line, lo - 1) - x) <= Math.Abs(X(line, lo) - x)) return lo - 1;
            return lo;
        }

        public RectangleF CaretRect(int index, float w)
        {
            int line, col; ToLineCol(index, out line, out col);
            return new RectangleF(X(line, col) - w / 2, LineTop(line) + LineH * 0.08f, w, LineH * 0.86f);
        }

        public List<RectangleF> SelectionRects(int a, int b)
        {
            List<RectangleF> r = new List<RectangleF>();
            if (a == b) return r;
            int s = Math.Min(a, b), e = Math.Max(a, b), l1, c1, l2, c2;
            ToLineCol(s, out l1, out c1); ToLineCol(e, out l2, out c2);
            for (int l = l1; l <= l2; l++)
            {
                int from = l == l1 ? c1 : 0, to = l == l2 ? c2 : Lines[l].Length;
                float x0 = X(l, from), x1 = X(l, to);
                if (l != l2) x1 += Size * 0.25f;   // het einde van de regel hoort er ook bij
                r.Add(new RectangleF(x0, LineTop(l), Math.Max(2, x1 - x0), LineH));
            }
            return r;
        }

        // Omtrek van de letters (zonder rand)
        public RectangleF InkBounds()
        {
            float w = 0;
            foreach (string l in Lines) w = Math.Max(w, Width(l));
            return new RectangleF(Origin.X, Origin.Y, Math.Max(Size * 0.3f, w), Lines.Length * LineH);
        }

        // De tekst als vorm, regel voor regel op precies deze plekken
        public GraphicsPath Path()
        {
            GraphicsPath gp = new GraphicsPath(FillMode.Winding);
            for (int i = 0; i < Lines.Length; i++)
                if (Lines[i].Length > 0)
                {
                    try { gp.AddString(Lines[i], Family, (int)Style, Size, new PointF(Origin.X, LineTop(i)), typo); }
                    catch { gp.AddString(Lines[i], FontFamily.GenericSansSerif, (int)FontStyle.Bold, Size, new PointF(Origin.X, LineTop(i)), typo); }
                }
            return gp;
        }
    }
}
