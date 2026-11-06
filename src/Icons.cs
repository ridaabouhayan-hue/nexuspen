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
    // ------------------------------------------------------------------ Icoontjes (Lucide, 24x24, lijndikte 2, ronde uiteinden)
    static class Icons
    {
        static readonly Dictionary<string, GraphicsPath[]> Cache = new Dictionary<string, GraphicsPath[]>();

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        static float[] Nums(string s)
        {
            string[] p = s.Replace(',', ' ').Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float[] n = new float[p.Length];
            for (int i = 0; i < p.Length; i++) n[i] = F(p[i]);
            return n;
        }

        public static bool Has(string name) { return IconData.All.ContainsKey(name); }

        static GraphicsPath[] Get(string name)
        {
            GraphicsPath[] g;
            if (Cache.TryGetValue(name, out g)) return g;
            string[] parts;
            if (!IconData.All.TryGetValue(name, out parts)) parts = new string[0];
            List<GraphicsPath> list = new List<GraphicsPath>();
            foreach (string part in parts)
            {
                int bar = part.IndexOf('|');
                string kind = part.Substring(0, bar), body = part.Substring(bar + 1);
                GraphicsPath gp;
                try
                {
                    if (kind == "p") gp = Svg.Path(body);
                    else
                    {
                        float[] n = Nums(body);
                        gp = new GraphicsPath();
                        if (kind == "c") gp.AddEllipse(n[0] - n[2], n[1] - n[2], 2 * n[2], 2 * n[2]);
                        else if (kind == "e") gp.AddEllipse(n[0] - n[2], n[1] - n[3], 2 * n[2], 2 * n[3]);
                        else if (kind == "r") { gp.Dispose(); gp = Svg.RoundRect(new RectangleF(n[0], n[1], n[2], n[3]), n[4]); }
                        else if (kind == "l") gp.AddLine(n[0], n[1], n[2], n[3]);
                        else
                        {
                            PointF[] pts = new PointF[n.Length / 2];
                            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(n[2 * i], n[2 * i + 1]);
                            if (kind == "g") gp.AddPolygon(pts); else gp.AddLines(pts);
                        }
                    }
                }
                catch (Exception ex) { Log.Error("Icoon " + name + " kon niet gelezen worden", ex); continue; }
                list.Add(gp);
            }
            g = list.ToArray();
            Cache[name] = g;
            return g;
        }

        // Kant-en-klare plaatjes per icoon, grootte en kleur: tekenen is dan alleen nog kopieren (snel en altijd gelijk)
        static readonly Dictionary<string, Bitmap> Ready = new Dictionary<string, Bitmap>();

        // Lijndikte in hele pixels: 1,67 pixel wordt 2, zo zijn er geen wazige halve pixels
        public static float DeviceStroke(float stroke, float scale)
        {
            float w = Math.Max(2, stroke) * scale;
            if (w < 1) return 1;
            return (float)Math.Floor(w + 0.6f);
        }

        // Zet de hoekpunten van het icoon precies op de pixels (net als bij letters op het scherm).
        // Een lijn van 2 pixels hoort op de rand tussen twee pixels, een lijn van 1 of 3 pixels midden op een pixel.
        public static GraphicsPath Snapped(GraphicsPath src, float scale, float ox, float oy, float devStroke)
        {
            PointF[] pts;
            byte[] types;
            using (GraphicsPath gp = (GraphicsPath)src.Clone())
            {
                using (Matrix m = new Matrix()) { m.Translate(ox, oy); m.Scale(scale, scale); gp.Transform(m); }
                if (gp.PointCount == 0) return new GraphicsPath();
                pts = gp.PathPoints; types = gp.PathTypes;
            }
            float off = ((int)devStroke % 2 == 1) ? 0.5f : 0f;
            int n = pts.Length;
            float[] dx = new float[n], dy = new float[n];
            // eerst de ankerpunten verschuiven
            for (int i = 0; i < n; i++)
            {
                int kind = types[i] & 0x07;
                bool anchor = kind != 3 || IsBezierEnd(types, i);
                if (!anchor) continue;
                float sx = (float)Math.Round(pts[i].X - off) + off, sy = (float)Math.Round(pts[i].Y - off) + off;
                dx[i] = sx - pts[i].X; dy[i] = sy - pts[i].Y;
            }
            // dan de stuurpunten van bochten even ver meenemen als hun ankerpunt, zodat bochten rond blijven
            for (int i = 0; i < n; i++)
            {
                if ((types[i] & 0x07) != 3 || IsBezierEnd(types, i)) continue;
                int pos = BezierPos(types, i);   // 0 = eerste stuurpunt, 1 = tweede
                int anchorIdx = pos == 0 ? i - 1 : i + 1;
                if (anchorIdx >= 0 && anchorIdx < n) { dx[i] = dx[anchorIdx]; dy[i] = dy[anchorIdx]; }
            }
            PointF[] outPts = new PointF[n];
            for (int i = 0; i < n; i++) outPts[i] = new PointF(pts[i].X + dx[i], pts[i].Y + dy[i]);
            return new GraphicsPath(outPts, types);
        }

        // Plek binnen een bocht van 3 punten (stuurpunt, stuurpunt, eindpunt)
        static int BezierPos(byte[] types, int i)
        {
            int k = 0;
            int j = i;
            while (j > 0 && (types[j - 1] & 0x07) == 3) { j--; k++; }
            return k % 3;
        }

        static bool IsBezierEnd(byte[] types, int i) { return BezierPos(types, i) == 2; }

        // Tekent icoon 'name' gecentreerd in r, 'size' pixels groot. 'stroke' is de lijndikte op het 24-raster (standaard 2).
        public static void Draw(Graphics g, string name, RectangleF r, float size, Color c, float stroke)
        {
            int dev = Math.Max(4, (int)Math.Round(size));
            float sc = dev / 24f;
            float ds = DeviceStroke(stroke, sc);
            int margin = (int)Math.Ceiling(ds) + 1;
            int x = (int)Math.Round(r.X + (r.Width - dev) / 2), y = (int)Math.Round(r.Y + (r.Height - dev) / 2);
            if (!g.Transform.IsIdentity || dev > 64)
            {
                // getekend met een vergroting, of groot (een stempel die je groter sleept): gewoon als vorm tekenen,
                // anders vult de voorraad plaatjes zich met elke tussenmaat
                DrawVector(g, name, x, y, sc, c, ds);
                return;
            }
            string key = name + "|" + dev + "|" + c.ToArgb() + "|" + ds;
            Bitmap b;
            if (!Ready.TryGetValue(key, out b))
            {
                b = new Bitmap(dev + 2 * margin, dev + 2 * margin, PixelFormat.Format32bppPArgb);
                using (Graphics bg = Graphics.FromImage(b))
                {
                    bg.Clear(Color.Transparent);
                    DrawVector(bg, name, margin, margin, sc, c, ds);
                }
                if (Ready.Count > 600) { foreach (Bitmap old in Ready.Values) old.Dispose(); Ready.Clear(); }
                Ready[key] = b;
            }
            GraphicsState st = g.Save();
            g.CompositingMode = CompositingMode.SourceOver;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(b, new Rectangle(x - margin, y - margin, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel);
            g.Restore(st);
        }

        static void DrawVector(Graphics g, string name, float x, float y, float sc, Color c, float ds)
        {
            GraphicsPath[] paths = Get(name);
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (Pen p = new Pen(c, ds))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                foreach (GraphicsPath gp in paths)
                    using (GraphicsPath sp = Snapped(gp, sc, x, y, ds)) g.DrawPath(p, sp);
            }
            g.Restore(st);
        }

        // Gevuld tekenen (voor de ster-stempel)
        public static void Fill(Graphics g, string name, RectangleF r, Color c)
        {
            GraphicsPath[] paths = Get(name);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (SolidBrush b = new SolidBrush(c)) foreach (GraphicsPath gp in paths) g.FillPath(b, gp);
            g.Restore(st);
        }

        public static GraphicsPath[] Paths(string name) { return Get(name); }
    }
}
