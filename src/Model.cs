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

        public Stroke Clone()
        {
            Stroke s = (Stroke)MemberwiseClone();
            s.Pts = new List<PointF>(Pts);
            return s;
        }
    }
}
