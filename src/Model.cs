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
    enum Tool { Mouse, Pen, Marker, Laser, Line, Arrow, Rect, Ellipse, Text, Eraser, Select, Stamp, Focus }
    enum Board { None, White, Black }

    class Stroke
    {
        public Tool Tool;
        public Color Color;
        public float Width;
        public List<PointF> Pts = new List<PointF>();
        public string Text;
        public float FontSize;
        public DateTime Born;
        public string FontName;      // lettertype van een tekst (leeg = standaard)
        public bool Outline;         // tekst met rand
        public int OutlineWidth = 1; // 0 dun, 1 middel, 2 dik
        public int OutlineColor = 0; // 0 zwart, 1 wit, 2 automatisch
        public int Style;            // 1 = krijtstreep

        public const int Chalk = 1;

        // Onthouden omtrek (zie Render.Bounds). Klopt zolang punten, dikte en tekst niet veranderen.
        internal RectangleF BCache;
        int bCount = -1, bOW;
        PointF bFirst, bLast;
        float bWidth, bFont;
        string bText, bFontName;
        bool bOutline;
        Tool bTool;

        internal bool CacheValid()
        {
            int n = Pts.Count;
            return n == bCount && n > 0 && Pts[0] == bFirst && Pts[n - 1] == bLast && Width == bWidth && FontSize == bFont
                && ReferenceEquals(Text, bText) && ReferenceEquals(FontName, bFontName) && Outline == bOutline && OutlineWidth == bOW && Tool == bTool;
        }

        internal void SetCache(RectangleF r)
        {
            int n = Pts.Count;
            BCache = r; bCount = n; bFirst = Pts[0]; bLast = Pts[n - 1]; bWidth = Width; bFont = FontSize;
            bText = Text; bFontName = FontName; bOutline = Outline; bOW = OutlineWidth; bTool = Tool;
        }

        public Stroke Clone()
        {
            Stroke s = (Stroke)MemberwiseClone();
            s.Pts = new List<PointF>(Pts);
            return s;
        }
    }
}
