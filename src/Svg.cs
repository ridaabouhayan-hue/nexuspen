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
    // ------------------------------------------------------------------ SVG-paden lezen (icoontjes en logo uit het ontwerp)
    static class Svg
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        static List<string> Tokens(string d)
        {
            List<string> t = new List<string>();
            int i = 0;
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsLetter(ch)) { t.Add(ch.ToString()); i++; continue; }
                if (ch == ',' || char.IsWhiteSpace(ch)) { i++; continue; }
                int start = i;
                bool dot = false, exp = false;
                if (ch == '-' || ch == '+') i++;
                while (i < d.Length)
                {
                    char c = d[i];
                    if (char.IsDigit(c)) { i++; continue; }
                    if (c == '.' && !dot && !exp) { dot = true; i++; continue; }
                    if ((c == 'e' || c == 'E') && !exp) { exp = true; i++; if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++; continue; }
                    break;
                }
                if (i == start) { i++; continue; }
                t.Add(d.Substring(start, i - start));
            }
            return t;
        }

        // Kleine boogvlaggen in paden als "a2 2 0 01-2 2" staan direct achter elkaar
        static float Flag(List<string> t, ref int k)
        {
            string s = t[k];
            if (s.Length > 1 && (s[0] == '0' || s[0] == '1'))
            {
                t[k] = s.Substring(1);
                return s[0] == '1' ? 1 : 0;
            }
            k++;
            return float.Parse(s, C);
        }

        public static GraphicsPath Path(string d)
        {
            GraphicsPath gp = new GraphicsPath();
            List<string> t = Tokens(d);
            int k = 0;
            char cmd = 'M';
            PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
            char lastCmd = ' ';
            bool open = false;
            while (k < t.Count)
            {
                if (char.IsLetter(t[k][0]) && t[k].Length == 1) { cmd = t[k][0]; k++; }
                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);
                if (up == 'Z')
                {
                    gp.CloseFigure(); cur = start; open = false; lastCmd = 'Z';
                    continue;
                }
                if (k >= t.Count) break;
                float ox = rel ? cur.X : 0, oy = rel ? cur.Y : 0;
                switch (up)
                {
                    case 'M':
                    {
                        float x = N(t, ref k) + ox, y = N(t, ref k) + oy;
                        if (open) gp.StartFigure();
                        cur = start = new PointF(x, y);
                        open = true;
                        cmd = rel ? 'l' : 'L';
                        break;
                    }
                    case 'L':
                    {
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        gp.AddLine(cur, p); cur = p; break;
                    }
                    case 'H':
                    {
                        PointF p = new PointF(N(t, ref k) + ox, cur.Y);
                        gp.AddLine(cur, p); cur = p; break;
                    }
                    case 'V':
                    {
                        PointF p = new PointF(cur.X, N(t, ref k) + oy);
                        gp.AddLine(cur, p); cur = p; break;
                    }
                    case 'C':
                    {
                        PointF a = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF b = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        gp.AddBezier(cur, a, b, p); lastCtrl = b; cur = p; break;
                    }
                    case 'S':
                    {
                        PointF a = (lastCmd == 'C' || lastCmd == 'S') ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                        PointF b = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        gp.AddBezier(cur, a, b, p); lastCtrl = b; cur = p; break;
                    }
                    case 'Q':
                    {
                        PointF q = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        Quad(gp, cur, q, p); lastCtrl = q; cur = p; break;
                    }
                    case 'A':
                    {
                        float rx = N(t, ref k), ry = N(t, ref k), rot = N(t, ref k);
                        float large = Flag(t, ref k), sweep = Flag(t, ref k);
                        PointF p = new PointF(N(t, ref k) + ox, N(t, ref k) + oy);
                        Arc(gp, cur, rx, ry, rot, large != 0, sweep != 0, p);
                        cur = p; break;
                    }
                    default: k++; break;
                }
                lastCmd = up;
            }
            return gp;
        }

        static float N(List<string> t, ref int k) { return float.Parse(t[k++], C); }

        public static void Quad(GraphicsPath gp, PointF a, PointF q, PointF b)
        {
            PointF c1 = new PointF(a.X + 2f / 3 * (q.X - a.X), a.Y + 2f / 3 * (q.Y - a.Y));
            PointF c2 = new PointF(b.X + 2f / 3 * (q.X - b.X), b.Y + 2f / 3 * (q.Y - b.Y));
            gp.AddBezier(a, c1, c2, b);
        }

        // SVG-boog (eindpunten) omzetten naar bezierkrommen
        static void Arc(GraphicsPath gp, PointF p0, float rx, float ry, float rotDeg, bool large, bool sweep, PointF p1)
        {
            if (rx == 0 || ry == 0 || (p0.X == p1.X && p0.Y == p1.Y)) { gp.AddLine(p0, p1); return; }
            double phi = rotDeg * Math.PI / 180, cp = Math.Cos(phi), sp = Math.Sin(phi);
            double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
            double x1 = cp * dx + sp * dy, y1 = -sp * dx + cp * dy;
            double arx = Math.Abs(rx), ary = Math.Abs(ry);
            double lam = (x1 * x1) / (arx * arx) + (y1 * y1) / (ary * ary);
            if (lam > 1) { double s = Math.Sqrt(lam); arx *= s; ary *= s; }
            double num = arx * arx * ary * ary - arx * arx * y1 * y1 - ary * ary * x1 * x1;
            double den = arx * arx * y1 * y1 + ary * ary * x1 * x1;
            double co = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cx1 = co * arx * y1 / ary, cy1 = -co * ary * x1 / arx;
            double cx = cp * cx1 - sp * cy1 + (p0.X + p1.X) / 2.0, cy = sp * cx1 + cp * cy1 + (p0.Y + p1.Y) / 2.0;
            double th1 = Ang(1, 0, (x1 - cx1) / arx, (y1 - cy1) / ary);
            double dth = Ang((x1 - cx1) / arx, (y1 - cy1) / ary, (-x1 - cx1) / arx, (-y1 - cy1) / ary);
            if (!sweep && dth > 0) dth -= 2 * Math.PI;
            if (sweep && dth < 0) dth += 2 * Math.PI;
            int segs = (int)Math.Ceiling(Math.Abs(dth) / (Math.PI / 2));
            double step = dth / segs, kk = 4.0 / 3 * Math.Tan(step / 4);
            double th = th1;
            PointF prev = p0;
            for (int i = 0; i < segs; i++)
            {
                double a = th, b = th + step;
                double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b);
                PointF e1 = Pt(cx, cy, arx, ary, cp, sp, ca - kk * sa, sa + kk * ca);
                PointF e2 = Pt(cx, cy, arx, ary, cp, sp, cb + kk * sb, sb - kk * cb);
                PointF e = i == segs - 1 ? p1 : Pt(cx, cy, arx, ary, cp, sp, cb, sb);
                gp.AddBezier(prev, e1, e2, e);
                prev = e; th = b;
            }
        }

        static PointF Pt(double cx, double cy, double rx, double ry, double cp, double sp, double ux, double uy)
        {
            double x = rx * ux, y = ry * uy;
            return new PointF((float)(cp * x - sp * y + cx), (float)(sp * x + cp * y + cy));
        }

        static double Ang(double ux, double uy, double vx, double vy)
        {
            double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            return a;
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath gp = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
            if (rad <= 0.01f) { gp.AddRectangle(r); return gp; }
            float d = rad * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }
}
