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
    // ------------------------------------------------------------------ Tekening bewaren
    static class StrokeStore
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen"); } }
        public static string CurrentPath { get { return Path.Combine(Dir, "tekening.dat"); } }
        public static string PreviousPath { get { return Path.Combine(Dir, "vorige-tekening.dat"); } }
        public static string LockPath { get { return Path.Combine(Dir, "sessie.lock"); } }
        // Bestaat dit bestand, dan heb je op Opslaan gedrukt: de tekening komt bij de volgende start terug
        public static string BoardPath(int page) { return Path.Combine(Dir, "bord-" + (page + 1) + ".dat"); }

        // Alle bordpagina's bewaren (bord-1.dat, bord-2.dat, ...). Oude pagina's die er niet meer zijn worden opgeruimd.
        public static void SaveBoard(List<List<Stroke>> pages)
        {
            for (int i = 0; i < pages.Count; i++) Save(pages[i], BoardPath(i));
            for (int i = pages.Count; i < 200; i++)
            {
                string p = BoardPath(i);
                if (!File.Exists(p)) break;
                File.Delete(p);
            }
        }

        public static List<List<Stroke>> LoadBoard()
        {
            List<List<Stroke>> pages = new List<List<Stroke>>();
            for (int i = 0; i < 200 && File.Exists(BoardPath(i)); i++) pages.Add(Load(BoardPath(i)));
            if (pages.Count == 0) pages.Add(new List<Stroke>());
            return pages;
        }

        public static string KeepPath { get { return Path.Combine(Dir, "bewaren.vlag"); } }

        static string B64(string t) { return string.IsNullOrEmpty(t) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(t)); }
        static string UnB64(string t) { return t.Length == 0 ? null : Encoding.UTF8.GetString(Convert.FromBase64String(t)); }

        public static void Save(List<Stroke> list, string path)
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NEXUSPEN2");
            foreach (Stroke st in list)
            {
                sb.Append(st.Tool).Append('|').Append(st.Color.ToArgb().ToString(C)).Append('|')
                  .Append(st.Width.ToString(C)).Append('|').Append(st.FontSize.ToString(C)).Append('|')
                  .Append(B64(st.Text)).Append('|');
                for (int i = 0; i < st.Pts.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(st.Pts[i].X.ToString("0.#", C)).Append(',').Append(st.Pts[i].Y.ToString("0.#", C));
                }
                sb.Append('|').Append(st.Outline ? "1" : "0").Append('|').Append(B64(st.FontName))
                  .Append('|').Append(st.OutlineWidth).Append('|').Append(st.OutlineColor).Append('|').Append(st.Style);
                sb.AppendLine();
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch { File.Delete(path); }
            }
            File.Move(tmp, path);
        }

        public static List<Stroke> Load(string path)
        {
            List<Stroke> list = new List<Stroke>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int n = 1; n < lines.Length; n++)
            {
                try
                {
                    string[] f = lines[n].Split('|');
                    if (f.Length < 6) continue;
                    Stroke st = new Stroke();
                    st.Tool = (Tool)Enum.Parse(typeof(Tool), f[0]);
                    st.Color = Color.FromArgb(int.Parse(f[1], C));
                    st.Width = float.Parse(f[2], C);
                    st.FontSize = float.Parse(f[3], C);
                    st.Text = UnB64(f[4]);
                    foreach (string pt in f[5].Split(' '))
                    {
                        int k = pt.IndexOf(',');
                        if (k > 0) st.Pts.Add(new PointF(float.Parse(pt.Substring(0, k), C), float.Parse(pt.Substring(k + 1), C)));
                    }
                    if (f.Length > 6) st.Outline = f[6] == "1";
                    if (f.Length > 7) st.FontName = UnB64(f[7]);
                    if (f.Length > 10) { st.OutlineWidth = int.Parse(f[8], C); st.OutlineColor = int.Parse(f[9], C); st.Style = int.Parse(f[10], C); }
                    if (st.Pts.Count > 0) list.Add(st);
                }
                catch { }
            }
            return list;
        }
    }
}
