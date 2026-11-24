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
    // Deel van de tekenlaag (Overlay). Zelfde klasse, opgesplitst per onderwerp zodat elk bestand overzichtelijk blijft.
    partial class Overlay
    {
        // ---------- selecteren
        void SelectStroke(Stroke s)
        {
            selected = s;
            dirty = true;
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect());
        }

        public void Deselect()
        {
            quietMove = false;   // stil verplaatsen van een tekst is hiermee ook klaar
            if (selected == null) return;
            selected = null;
            dragging = false;
            if (lifted) { lifted = false; Rebuild(); }
            dirty = true;
            if (toolbar != null) toolbar.HideMiniBar();
        }

        Rectangle SelectionScreenRect()
        {
            RectangleF r = SelectionBox();
            return new Rectangle((int)r.X + vs.X, (int)r.Y + vs.Y, (int)r.Width, (int)r.Height);
        }

        // Een wijziging aan het geselecteerde item: eerst een kopie, zodat Ongedaan maken de oude toestand terugzet
        void EditSelected()
        {
            Checkpoint();
            int i = strokes.IndexOf(selected);
            Stroke c = selected.Clone();
            if (i >= 0) strokes[i] = c;
            selected = c;
        }

        public void RecolorSelected(Color c)
        {
            if (selected == null) return;
            EditSelected();
            selected.Color = c;
            Rebuild();
            RefreshUi();
        }

        public void DeleteSelected()
        {
            if (selected == null) return;
            Checkpoint();
            strokes.Remove(selected);
            Deselect();
            Rebuild();
        }

        public void DuplicateSelected()
        {
            if (selected == null) return;
            Checkpoint();
            Stroke c = selected.Clone();
            Render.Move(c, 24, 24);
            strokes.Add(c);
            Rebuild();
            SelectStroke(c);
        }

        public Color SelectedColor { get { return selected == null ? Color.Empty : selected.Color; } }
        public bool SelectionIsText { get { return selected != null && selected.Tool == Tool.Text; } }
        public bool SelectionCanOutline { get { return selected != null && (selected.Tool == Tool.Text || (Render.CanOutline(selected.Tool) && !Render.IsVarWidth(selected))); } }
        public bool SelectionHasOutline { get { return selected != null && selected.Outline; } }

        // Getal in het selectiebalkje: lettergrootte, stempelgrootte of lijndikte
        public int SelectedSize
        {
            get
            {
                if (selected == null) return 0;
                if (selected.Tool == Tool.Text || selected.Tool == Tool.Stamp) return (int)Math.Round(selected.FontSize);
                return (int)Math.Round(selected.Width);
            }
        }

        public void ScaleSelected(float f)
        {
            if (selected == null) return;
            EditSelected();
            ScaleStroke(selected, f, Center(Render.Bounds(selected)));
            Rebuild();
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect(), true);
        }

        static PointF Center(RectangleF r) { return new PointF(r.X + r.Width / 2, r.Y + r.Height / 2); }

        // Groter of kleiner maken rond een vast punt
        static void ScaleStroke(Stroke s, float f, PointF about)
        {
            if (s.Tool == Tool.Text)
            {
                s.FontSize = Math.Max(8, Math.Min(400, s.FontSize * f));
                PointF p = s.Pts[0];
                s.Pts[0] = new PointF(about.X + (p.X - about.X) * f, about.Y + (p.Y - about.Y) * f);
                return;
            }
            if (s.Tool == Tool.Stamp)
            {
                float z = s.FontSize > 0 ? s.FontSize : Render.StampSize(s.Width);
                s.FontSize = Math.Max(10, Math.Min(400, z * f));
                return;
            }
            for (int i = 0; i < s.Pts.Count; i++)
            {
                PointF p = s.Pts[i];
                s.Pts[i] = new PointF(about.X + (p.X - about.X) * f, about.Y + (p.Y - about.Y) * f);
            }
        }

        public void ToggleOutlineSelected()
        {
            if (!SelectionCanOutline) return;   // kalligrafie en lijnen met druk krijgen geen rand (die past niet om een wisselende dikte)
            EditSelected();
            selected.Outline = !selected.Outline;
            if (selected.Outline) { selected.OutlineWidth = Cfg.OutlineWidth; selected.OutlineColor = Cfg.OutlineColor; }
            Rebuild();
            if (toolbar != null) toolbar.ShowMiniBar(SelectionScreenRect(), true);
        }

        public void SendSelectedToBack()
        {
            if (selected == null || strokes.IndexOf(selected) == 0) return;
            Checkpoint();
            strokes.Remove(selected);
            strokes.Insert(0, selected);
            Deselect();   // dan pak je met de volgende klik wat er nu bovenop ligt
            Rebuild();
        }

        public void BringSelectedToFront()
        {
            if (selected == null || strokes.IndexOf(selected) == strokes.Count - 1) return;
            Checkpoint();
            strokes.Remove(selected);
            strokes.Add(selected);
            Rebuild();
        }

        // Klik je in de buurt van een hoekje van de selectie? Dan maak je hem groter of kleiner.
        int CornerAt(PointF p)
        {
            if (selected == null) return -1;
            RectangleF r = SelectionBox();
            PointF[] c = { r.Location, new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) };
            for (int i = 0; i < 4; i++) if (Math.Abs(p.X - c[i].X) <= 9 && Math.Abs(p.Y - c[i].Y) <= 9) return i;
            return -1;
        }

        Stroke lastDot;
        DateTime lastDotAt = DateTime.MinValue;
        bool skipDoubleClick;

        // Het puntje van de eerste klik van een dubbelklik (of null)
        Stroke FirstClickDot()
        {
            if (lastDot == null || (DateTime.Now - lastDotAt).TotalMilliseconds > 800) return null;
            if (!strokes.Contains(lastDot) && !fading.Contains(lastDot)) return null;
            RectangleF b = Render.Bounds(lastDot);
            float w = lastDot.Tool == Tool.Marker ? Render.MarkerWidth(lastDot.Width) : lastDot.Width;
            return b.Width <= w + 8 && b.Height <= w + 8 ? lastDot : null;
        }

        void RemoveDot(Stroke dot)
        {
            if (dot == null) return;
            Rectangle area = StrokeBounds(dot);
            if (fading.Remove(dot)) { pendingDirty = U(pendingDirty, area); }
            else if (strokes.Remove(dot))
            {
                if (undo.Count > 0) undo.RemoveAt(undo.Count - 1);
                rebuildArea = U(rebuildArea, area);
                MarkChanged();
            }
            lastDot = null;
        }

        Stroke TopHit(PointF p, Stroke skip)
        {
            for (int i = strokes.Count - 1; i >= 0; i--)
                if (strokes[i] != skip && Render.Hit(strokes[i], p, 6)) return strokes[i];
            return null;
        }

        Stroke TopHit(PointF p)
        {
            for (int i = strokes.Count - 1; i >= 0; i--)
                if (Render.Hit(strokes[i], p, 6)) return strokes[i];
            return null;
        }
    }
}
