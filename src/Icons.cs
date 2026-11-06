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

        // Tekent icoon 'name' gecentreerd in r, 'size' pixels groot. 'stroke' is de lijndikte op het 24-raster (standaard 2).
        public static void Draw(Graphics g, string name, RectangleF r, float size, Color c, float stroke)
        {
            GraphicsPath[] paths = Get(name);
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float sc = size / 24f;
            // op hele pixels zetten, dan blijven de lijnen scherp
            float x = (float)Math.Round(r.X + (r.Width - size) / 2), y = (float)Math.Round(r.Y + (r.Height - size) / 2);
            g.TranslateTransform(x, y);
            g.ScaleTransform(sc, sc);
            float w = stroke < 2 ? 2 : stroke;
            using (Pen p = new Pen(c, w))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                foreach (GraphicsPath gp in paths) g.DrawPath(p, gp);
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
