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
    // ------------------------------------------------------------------ Het NexusPen-logo (zelfde maten als het logopakket)
    static class Logo
    {
        static readonly Color Inkt = ColorTranslator.FromHtml("#1A0F00"),
            GrafL = ColorTranslator.FromHtml("#4B3019"), Glans = ColorTranslator.FromHtml("#9C7A58");

        // De N en het potloodlijf zijn wit, behalve op een heel lichte accentkleur: dan donker zodat je ze blijft zien
        public static Color LetterColor(Color tile, Color onAccent) { return Theme.Luma(tile) > 0.72 ? onAccent : Color.White; }

        public static void Draw(Graphics g, RectangleF r, Color tile, Color letter)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // hele pixels: de tegel staat dan scherp, ook op 125% en 150%
            float z = Math.Max(8, (float)Math.Round(Math.Min(r.Width, r.Height)));
            float sc = z / 64f;
            g.TranslateTransform((float)Math.Round(r.X + (r.Width - z) / 2), (float)Math.Round(r.Y + (r.Height - z) / 2));
            g.ScaleTransform(sc, sc);
            bool small = r.Width <= 24.5f;
            using (GraphicsPath gp = Svg.RoundRect(new RectangleF(0, 0, 64, 64), 14))
            using (SolidBrush b = new SolidBrush(tile)) g.FillPath(b, gp);
            using (SolidBrush wit = new SolidBrush(letter))
            {
                if (small)
                {
                    Stroke9(g, letter, "M20.00 34.00 L20.00 22.37 Q20.00 13.37 24.87 20.93 L39.13 43.07 Q44.00 50.63 44.00 41.63 L44.00 17.50", 9f);
                    g.FillRectangle(wit, 15.5f, 29.5f, 9f, 9f);
                    using (GraphicsPath gp = Svg.Path("M15.5 38.0 H24.5 L20.0 52.0 Z")) g.FillPath(wit, gp);
                }
                else
                {
                    Stroke9(g, letter, "M19.50 34.50 L19.50 21.87 Q19.50 12.87 24.42 20.41 L39.58 43.59 Q44.50 51.13 44.50 42.13 L44.50 17.00", 8f);
                    g.FillRectangle(wit, 15.5f, 30.5f, 8f, 8f);
                    const string cone = "M15.50 38.50 Q16.10 44.50 19.05 50.55 Q19.50 51.47 19.95 50.55 Q22.90 44.50 23.50 38.50 Z";
                    const string graf = "M17.30 46.40 Q18.04 48.47 19.05 50.55 Q19.50 51.47 19.95 50.55 Q20.96 48.47 21.70 46.40 Z";
                    using (GraphicsPath cp = Svg.Path(cone))
                    {
                        using (SolidBrush b = new SolidBrush(letter)) g.FillPath(b, cp);   // wit potlood: alleen de grafietpunt heeft kleur
                        Region old = g.Clip;
                        g.SetClip(cp, CombineMode.Intersect);
                        using (GraphicsPath gp = Svg.Path(graf))
                        {
                            using (SolidBrush b = new SolidBrush(Inkt)) g.FillPath(b, gp);
                            g.SetClip(gp, CombineMode.Intersect);
                            Fill(g, GrafL, "M16.80 46.40 H18.51 L19.50 51.01 L16.80 51.01 Z");
                            using (Pen p = new Pen(Color.FromArgb(102, Glans), 0.35f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, 18.2f, 47.0f, 18.6f, 48.81f); }
                        }
                        g.Clip = old;
                        old.Dispose();
                    }
                    using (GraphicsPath gp = Svg.Path("M15.5 38.1 V38.5 A2.0 2.0 0 0 0 19.5 38.5 A2.0 2.0 0 0 0 23.5 38.5 V38.1 Z")) g.FillPath(wit, gp);
                }
            }
            g.Restore(st);
        }

        static void Stroke9(Graphics g, Color c, string d, float w)
        {
            using (GraphicsPath gp = Svg.Path(d))
            using (Pen p = new Pen(c, w))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                g.DrawPath(p, gp);
            }
        }

        static void Fill(Graphics g, Color c, string d)
        {
            using (GraphicsPath gp = Svg.Path(d)) using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, gp);
        }

        public static Bitmap Bitmap(int size, Color tile, Color letter)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b)) Draw(g, new RectangleF(0, 0, size, size), tile, letter);
            return b;
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static Icon MakeIcon(int size, Color tile, Color letter)
        {
            using (Bitmap b = Bitmap(size, tile, letter))
            {
                IntPtr h = b.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
        }
    }
}
