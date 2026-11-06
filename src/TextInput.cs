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
    // ------------------------------------------------------------------ Tekstinvoer
    class TextInput : Form
    {
        TextBox tb;
        Action<string> done;
        bool finished;

        public TextInput(Point screenPt, Font font, Color color, Action<string> onDone)
        {
            done = onDone;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            bool light = color.GetBrightness() > 0.75f;
            BackColor = light ? Color.FromArgb(60, 60, 60) : Color.FromArgb(245, 245, 245);
            Padding = new Padding(1);
            tb = new TextBox();
            tb.Multiline = true;
            tb.AcceptsReturn = true;
            tb.WordWrap = false;
            tb.BorderStyle = BorderStyle.None;
            tb.Font = font;
            tb.ForeColor = color;
            tb.BackColor = BackColor;
            tb.Dock = DockStyle.Fill;
            Controls.Add(tb);
            Location = new Point(screenPt.X - 2, screenPt.Y - 2);
            Grow();
            tb.TextChanged += delegate { Grow(); };
            tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Finish(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
            };
            Deactivate += delegate { Finish(true); };
        }

        void Grow()
        {
            Size sz = TextRenderer.MeasureText((tb.Text.Length == 0 ? "Typ hier" : tb.Text) + "WW", tb.Font);
            Size = new Size(Math.Max(120, sz.Width + 8), Math.Max(tb.Font.Height + 8, sz.Height + 8));
        }

        public void SetText(string text) { tb.Text = text ?? ""; tb.SelectAll(); Grow(); }

        public void Finish(bool commit)
        {
            if (finished) return;
            finished = true;
            string text = commit ? tb.Text : null;
            Close();
            if (done != null) done(text);
        }
    }
}
