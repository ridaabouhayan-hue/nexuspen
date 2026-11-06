using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace NexusPen
{
    // ------------------------------------------------------------------ Nieuwe versie? Alleen een vraag aan GitHub, er gaat niets van jou mee.
    static class Updates
    {
        public const string Api = "https://api.github.com/repos/ridaabouhayan-hue/nexuspen/releases/latest";
        public const string Page = "https://github.com/ridaabouhayan-hue/nexuspen/releases/latest";

        // Kijkt op de achtergrond. found krijgt de nieuwe versie (bijvoorbeeld "1.2.1") en het adres van de pagina.
        public static void CheckLater(int delayMs, Action<string, string> found)
        {
            Thread th = new Thread(delegate()
            {
                try
                {
                    Thread.Sleep(delayMs);
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Api);
                    req.UserAgent = "NexusPen/" + Program.Version;
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 8000; req.ReadWriteTimeout = 8000;
                    string body;
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader r = new StreamReader(resp.GetResponseStream()))
                    {
                        char[] buf = new char[65536];
                        int n = r.ReadBlock(buf, 0, buf.Length);  // nooit meer dan 64 kB lezen
                        body = new string(buf, 0, n);
                    }
                    Match tag = Regex.Match(body, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                    Match url = Regex.Match(body, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/[^\"]+)\"");
                    if (tag.Success && IsNewer(tag.Groups[1].Value, Program.Version))
                        found(tag.Groups[1].Value, url.Success ? url.Groups[1].Value : Page);
                }
                catch (WebException) { }      // geen internet of de pagina is (nog) niet openbaar: gewoon niets melden
                catch (Exception ex) { Log.Error("Zoeken naar een nieuwe versie", ex); }
            });
            th.IsBackground = true;
            th.Start();
        }

        public static bool IsNewer(string other, string mine)
        {
            Version a, b;
            try { a = new Version(Pad(other)); b = new Version(Pad(mine)); } catch { return false; }
            return a > b;
        }

        static string Pad(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            int dots = v.Split('.').Length - 1;
            while (dots++ < 1) v += ".0";
            return v;
        }
    }
}
