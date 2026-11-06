using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NexusPen
{
    // ------------------------------------------------------------------ Cursor per gereedschap
    // Pen en markeerstift: een rondje zo groot en zo gekleurd als je lijn. Gum: een rondje zo groot als de gum.
    // Stempel: de stempel licht doorzichtig. Tekst: een streepje zo hoog als je tekst.
    static class ToolCursor
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bits, IntPtr data);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        static IntPtr current = IntPtr.Zero, previous = IntPtr.Zero;
        static string currentKey;
        static Cursor currentCursor;

        // Geeft een cursor terug; de vorige wordt netjes opgeruimd. null = gewone cursor gebruiken.
        public static Cursor For(string key, Func<Bitmap> paint)
        {
            if (key == currentKey && currentCursor != null) return currentCursor;
            Cursor made = null;
            IntPtr handle = IntPtr.Zero;
            try
            {
                using (Bitmap b = paint())
                {
                    if (b == null) return null;
                    IntPtr color = b.GetHbitmap(Color.FromArgb(0));
                    IntPtr mask = CreateBitmap(b.Width, b.Height, 1, 1, IntPtr.Zero);
                    ICONINFO ii = new ICONINFO();
                    ii.fIcon = false; ii.xHotspot = b.Width / 2; ii.yHotspot = b.Height / 2;
                    ii.hbmColor = color; ii.hbmMask = mask;
                    handle = CreateIconIndirect(ref ii);
                    DeleteObject(color); DeleteObject(mask);
                }
                if (handle == IntPtr.Zero) return null;
                made = new Cursor(handle);
            }
            catch (Exception ex) { Log.Error("Cursor maken", ex); return null; }
            // de cursor van twee keer terug opruimen; de vorige kan Windows nog even tonen
            if (previous != IntPtr.Zero) DestroyIcon(previous);
            previous = current;
            current = handle; currentKey = key; currentCursor = made;
            return made;
        }

        static Bitmap Canvas(float d, out Graphics g, out float c)
        {
            int size = (int)Math.Ceiling(d) + 6;
            if (size % 2 == 0) size++;
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            c = size / 2f;
            return b;
        }

        // Rondje met een witte en een donkere rand, dan zie je het op elke achtergrond
        public static Bitmap Circle(float diameter, Color fill)
        {
            float d = Math.Max(7, Math.Min(150, diameter));
            Graphics g; float c;
            Bitmap b = Canvas(d + 2, out g, out c);
            using (g)
            {
                RectangleF r = new RectangleF(c - d / 2, c - d / 2, d, d);
                if (fill.A > 0) using (SolidBrush br = new SolidBrush(fill)) g.FillEllipse(br, r);
                using (Pen p = new Pen(Color.FromArgb(230, 255, 255, 255), 2.2f)) g.DrawEllipse(p, r);
                using (Pen p = new Pen(Color.FromArgb(200, 20, 20, 24), 1f)) g.DrawEllipse(p, r);
                // klein puntje in het midden: daar komt de inkt
                using (SolidBrush br = new SolidBrush(Color.FromArgb(200, 20, 20, 24))) g.FillRectangle(br, c - 0.5f, c - 0.5f, 1, 1);
            }
            return b;
        }

        // Streepje zo hoog als een regel tekst
        public static Bitmap TextBar(float height, Color color)
        {
            float h = Math.Max(14, Math.Min(150, height));
            Graphics g; float c;
            Bitmap b = Canvas(h, out g, out c);
            using (g)
            {
                float w = Math.Max(2, h / 22), serif = Math.Max(5, h / 6);
                using (Pen p = new Pen(Color.FromArgb(230, 255, 255, 255), w + 2.5f)) { g.DrawLine(p, c, c - h / 2, c, c + h / 2); g.DrawLine(p, c - serif, c - h / 2, c + serif, c - h / 2); g.DrawLine(p, c - serif, c + h / 2, c + serif, c + h / 2); }
                using (Pen p = new Pen(Color.FromArgb(255, color), w)) { g.DrawLine(p, c, c - h / 2, c, c + h / 2); g.DrawLine(p, c - serif, c - h / 2, c + serif, c - h / 2); g.DrawLine(p, c - serif, c + h / 2, c + serif, c + h / 2); }
            }
            return b;
        }

        // De stempel zelf, licht doorzichtig
        public static Bitmap Stamp(Stroke st)
        {
            float d = Math.Max(14, Math.Min(150, st.FontSize));
            Graphics g; float c;
            Bitmap b = Canvas(d + 8, out g, out c);
            using (g)
            using (Bitmap full = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics fg = Graphics.FromImage(full))
                {
                    Render.Setup(fg);
                    Stroke s = st.Clone(); s.Pts.Clear(); s.Pts.Add(new PointF(c, c)); s.FontSize = d;
                    Render.Draw(fg, s, 1f);
                }
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = 0.6f;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(full, new Rectangle(0, 0, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return b;
        }
    }
}
