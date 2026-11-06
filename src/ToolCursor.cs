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
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref W.BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        // 32-bits plaatje met gewone (niet voorvermenigvuldigde) doorzichtigheid, zoals Windows dat voor cursors wil
        static IntPtr StraightAlpha(Bitmap b)
        {
            W.BITMAPINFOHEADER bi = new W.BITMAPINFOHEADER();
            bi.biSize = (uint)Marshal.SizeOf(typeof(W.BITMAPINFOHEADER));
            bi.biWidth = b.Width; bi.biHeight = -b.Height; bi.biPlanes = 1; bi.biBitCount = 32;
            IntPtr bits;
            IntPtr h = CreateDIBSection(IntPtr.Zero, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            BitmapData bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[b.Width];
                for (int y = 0; y < b.Height; y++)
                {
                    Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, b.Width);
                    Marshal.Copy(row, 0, new IntPtr(bits.ToInt64() + (long)y * b.Width * 4), b.Width);
                }
            }
            finally { b.UnlockBits(bd); }
            return h;
        }

        // Masker met nullen: bij gewone (niet-alfa) weergave, zoals via Extern bureaublad, telt dan het kleurplaatje
        static IntPtr ZeroMask(int w, int h)
        {
            int stride = ((w + 15) / 16) * 2;
            IntPtr buf = Marshal.AllocHGlobal(stride * h);
            try
            {
                byte[] zeros = new byte[stride * h];
                Marshal.Copy(zeros, 0, buf, zeros.Length);
                return CreateBitmap(w, h, 1, 1, buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

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
                    IntPtr color = StraightAlpha(b);
                    if (color == IntPtr.Zero) return null;
                    IntPtr mask = ZeroMask(b.Width, b.Height);
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
