using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NexusPen
{
    // ------------------------------------------------------------------ Gebruik bijhouden (alleen op deze pc)
    // Wat er wordt bijgehouden: op welke knop je klikt, welk gereedschap en hoe lang, kleuren en dikte,
    // menu's en instellingen, twijfel (direct ongedaan maken), lessen en techniek.
    // NOOIT: wat je schrijft of tekent, wat er op je scherm staat, namen of bestanden.
    static class Usage
    {
        public const int Clicks = 1, Tools = 2, Colors = 4, Menus = 8, Doubt = 16, Lessons = 32, Tech = 64;
        public static readonly int[] Categories = { Clicks, Tools, Colors, Menus, Doubt, Lessons, Tech };

        static Settings cfg;
        static readonly List<string> buffer = new List<string>();
        static readonly object gate = new object();
        static DateTime lastFlush = DateTime.Now;
        static string currentTool;
        static DateTime toolSince = DateTime.Now;

        public static string Dir { get { return Path.Combine(StrokeStore.Dir, "gebruik"); } }
        static string FileFor(DateTime d) { return Path.Combine(Dir, d.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".log"); }

        public static void Init(Settings c) { cfg = c; }

        static int CategoryOf(string kind)
        {
            switch (kind)
            {
                case "klik": case "bord": case "stempel": case "sneltoets": case "verberg": case "inkorten": return Clicks;
                case "gereedschap": case "timer": case "vergrootglas": case "stand": return Tools;
                case "kleur": case "dikte": case "palet": return Colors;
                case "instelling": case "menu": return Menus;
                case "ongedaan": case "gum-na-tekenen": case "zoeken": return Doubt;
                case "start": case "stop": case "inklappen": case "uitklappen": case "wegschuiven": case "opslaan": case "foto": case "afsluiten": return Lessons;
                default: return Tech;
            }
        }

        // Eén gebeurtenis: soort, wat, en hoe (balk, sneltoets, rechtsklik, menu)
        public static void Log(string kind, string what, string via)
        {
            if (cfg == null || !cfg.Usage) return;
            if ((cfg.UsageCats & CategoryOf(kind)) == 0) return;
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" + kind + "\t" + Clean(what) + "\t" + Clean(via);
            lock (gate) buffer.Add(line);
            if ((DateTime.Now - lastFlush).TotalSeconds > 15 || buffer.Count > 200) Flush();
        }

        static string Clean(string s) { return string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }

        // Hoe lang je met een gereedschap werkt
        public static void ToolChanged(string tool)
        {
            DateTime now = DateTime.Now;
            if (currentTool != null)
            {
                double sec = (now - toolSince).TotalSeconds;
                if (sec >= 1) Log("gereedschap", currentTool, ((int)sec).ToString(CultureInfo.InvariantCulture));
            }
            currentTool = tool;
            toolSince = now;
        }

        public static void Flush()
        {
            List<string> lines;
            lock (gate) { if (buffer.Count == 0) return; lines = new List<string>(buffer); buffer.Clear(); }
            if (cfg != null && !cfg.Usage) return;   // bijhouden is net uitgezet: niets meer wegschrijven
            lastFlush = DateTime.Now;
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllLines(FileFor(DateTime.Now), lines, Encoding.UTF8);
            }
            catch (Exception ex) { Log2("Gebruik bewaren mislukt", ex); }
        }

        static void Log2(string msg, Exception ex) { NexusPen.Log.Error(msg, ex); }

        public static void Clear()
        {
            lock (gate) buffer.Clear();
            try { if (Directory.Exists(Dir)) foreach (string f in Directory.GetFiles(Dir, "*.log")) File.Delete(f); }
            catch (Exception ex) { Log2("Gebruik wissen mislukt", ex); }
        }

        // ---- overzicht
        public class Summary
        {
            public int Lessons, Screenshots, Days;
            public double DrawMinutes;
            public List<KeyValuePair<string, int>> TopClicks = new List<KeyValuePair<string, int>>();
            public List<string> NeverUsed = new List<string>();
        }

        static IEnumerable<string[]> Read(int days)
        {
            Flush();
            DateTime from = DateTime.Now.AddDays(-days);
            List<string[]> rows = new List<string[]>();
            if (!Directory.Exists(Dir)) return rows;
            foreach (string f in Directory.GetFiles(Dir, "*.log"))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        DateTime t;
                        if (p.Length < 4 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                        if (t >= from) rows.Add(p);
                    }
                }
                catch { }
            }
            return rows;
        }

        public static Summary Make(int days)
        {
            Summary s = new Summary();
            s.Days = days;
            Dictionary<string, int> clicks = new Dictionary<string, int>();
            HashSet<string> dates = new HashSet<string>();
            foreach (string[] p in Read(days))
            {
                string kind = p[1], what = p[2], via = p[3];
                if (kind == "start") { s.Lessons++; dates.Add(p[0].Substring(0, 10)); }
                else if (kind == "foto") s.Screenshots++;
                else if (kind == "gereedschap" && what != "Mouse") { int sec; if (int.TryParse(via, out sec)) s.DrawMinutes += sec / 60.0; }
                if (kind == "klik" || kind == "sneltoets")
                {
                    int n; clicks.TryGetValue(what, out n); clicks[what] = n + 1;
                }
            }
            s.TopClicks = new List<KeyValuePair<string, int>>(clicks);
            s.TopClicks.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            foreach (string k in new string[] { "pen", "markeer", "laser", "vormen", "tekst", "gum", "stempel", "focus", "bord", "foto", "selecteer" })
                if (!clicks.ContainsKey(k)) s.NeverUsed.Add(k);
            return s;
        }

        // Eén tekstbestand op het bureaublad: overzicht plus het logboek, om aan Claude te geven
        public static string Export()
        {
            Flush();
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string file = Path.Combine(desk, "NexusPen-gebruik-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NexusPen " + Program.Version + ", gebruiksgegevens. Alleen klikken, gereedschap, tijden en instellingen. Geen inhoud van het scherm.");
            Summary s = Make(30);
            sb.AppendLine("Laatste 30 dagen: " + s.Lessons + " keer gestart, " + Math.Round(s.DrawMinutes) + " minuten getekend, " + s.Screenshots + " schermafbeeldingen.");
            sb.AppendLine("Meest gebruikt: " + string.Join(", ", s.TopClicks.ConvertAll(delegate(KeyValuePair<string, int> kv) { return kv.Key + " " + kv.Value; }).ToArray()));
            sb.AppendLine("Nooit gebruikt: " + string.Join(", ", s.NeverUsed.ToArray()));
            sb.AppendLine();
            if (Directory.Exists(Dir))
            {
                string[] files = Directory.GetFiles(Dir, "*.log");
                Array.Sort(files);
                foreach (string f in files) sb.Append(File.ReadAllText(f, Encoding.UTF8));
            }
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }
    }
}
