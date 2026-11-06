using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace NexusPen
{
    // ------------------------------------------------------------------ Snelheidsmeter
    // Meet hoe snel NexusPen reageert: van muisbeweging tot inkt op het scherm, en van klik tot knop bijgewerkt.
    // Alleen tijden in milliseconden, nooit wat er getekend wordt. Blijft in het geheugen en komt bij afsluiten in het foutlog.
    static class Perf
    {
        static readonly Stopwatch clock = Stopwatch.StartNew();

        public static double Now { get { return clock.Elapsed.TotalMilliseconds; } }

        public class Meter
        {
            readonly double[] ring;
            int count, next;
            public readonly string Name;

            public Meter(string name, int size) { Name = name; ring = new double[size]; }

            public void Add(double ms)
            {
                if (ms < 0 || double.IsNaN(ms)) return;
                lock (ring) { ring[next] = ms; next = (next + 1) % ring.Length; if (count < ring.Length) count++; }
            }

            public int Count { get { return count; } }

            public void Clear() { lock (ring) { count = 0; next = 0; } }

            // p = 0.5 is de gewone tijd, p = 0.95 de trage uitschieters
            public double Percentile(double p)
            {
                double[] copy;
                lock (ring) { copy = new double[count]; Array.Copy(ring, copy, count); }
                if (copy.Length == 0) return 0;
                Array.Sort(copy);
                int i = (int)Math.Ceiling(p * copy.Length) - 1;
                return copy[Math.Max(0, Math.Min(copy.Length - 1, i))];
            }

            public double Max()
            {
                double m = 0;
                lock (ring) for (int i = 0; i < count; i++) if (ring[i] > m) m = ring[i];
                return m;
            }

            public string Summary()
            {
                if (count == 0) return Name + ": nog niet gemeten";
                return string.Format(CultureInfo.InvariantCulture, "{0}: gewoon {1:0.0} ms, traag (p95) {2:0.0} ms, traagst {3:0.0} ms ({4} keer gemeten)",
                    Name, Percentile(0.5), Percentile(0.95), Max(), count);
            }
        }

        public static readonly Meter Ink = new Meter("Inkt (muisbeweging tot scherm)", 4000);
        public static readonly Meter Frame = new Meter("Tekenen van een beeldje", 4000);
        public static readonly Meter Click = new Meter("Klik op een knop tot zichtbaar", 500);
        public static readonly Meter Erase = new Meter("Gummen per beweging", 2000);

        public static IEnumerable<Meter> All { get { return new Meter[] { Ink, Frame, Click, Erase }; } }

        public static void LogSummary()
        {
            foreach (Meter m in All) if (m.Count > 0) Log.Info("Snelheid. " + m.Summary());
        }
    }
}
