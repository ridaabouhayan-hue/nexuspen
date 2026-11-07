using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;

namespace NexusPen
{
    // ------------------------------------------------------------------ Nieuwe versie? Alleen een vraag aan GitHub, er gaat niets van jou mee.
    static class Updates
    {
        public const string Api = "https://api.github.com/repos/ridaabouhayan-hue/nexuspen/releases/latest";
        public const string Page = "https://github.com/ridaabouhayan-hue/nexuspen/releases/latest";
        const string DownloadPrefix = "https://github.com/ridaabouhayan-hue/nexuspen/releases/download/";

        // Wat we over de nieuwe versie weten: nummer, pagina, de installer en zijn controlecode
        public class Info { public string Version, PageUrl, SetupUrl, Sha256; }

        // Na het bijwerken: de tekening van net weer laten zien
        public static string FlagPath { get { return Path.Combine(StrokeStore.Dir, "bijwerken.vlag"); } }

        // Kijkt op de achtergrond. found krijgt de nieuwe versie (bijvoorbeeld "1.2.1") en het adres van de pagina.
        public static void CheckLater(int delayMs, Action<Info> found) { CheckLater(delayMs, found, null); }

        // nothing: wordt aangeroepen als er geen nieuwe versie is (of geen internet), voor "Nu kijken"
        public static void CheckLater(int delayMs, Action<Info> found, Action nothing)
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
                    Info info = Parse(body);
                    if (info != null && IsNewer(info.Version, Program.Version)) found(info);
                    else if (nothing != null) nothing();
                }
                catch (WebException) { if (nothing != null) nothing(); }      // geen internet: gewoon niets melden
                catch (Exception ex) { Log.Error("Zoeken naar een nieuwe versie", ex); if (nothing != null) nothing(); }
            });
            th.IsBackground = true;
            th.Start();
        }

        // Het antwoord van GitHub lezen: versie, pagina, en (als die er is) de installer met zijn controlecode
        public static Info Parse(string body)
        {
            Match tag = Regex.Match(body ?? "", "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
            if (!tag.Success) return null;
            Match url = Regex.Match(body, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/[^\"]+)\"");
            Info info = new Info();
            info.Version = tag.Groups[1].Value;
            info.PageUrl = url.Success ? url.Groups[1].Value : Page;
            // het stuk van de lijst dat over NexusPen-Setup.exe gaat (tot de naam van het volgende bestand)
            int at = body.IndexOf("\"name\":\"NexusPen-Setup.exe\"", StringComparison.Ordinal);
            if (at >= 0)
            {
                int end = body.IndexOf("\"name\":", at + 10, StringComparison.Ordinal);
                string seg = body.Substring(at, (end < 0 ? body.Length : end) - at);
                Match dg = Regex.Match(seg, "\"digest\"\\s*:\\s*\"sha256:([0-9a-f]{64})\"");
                Match dl = Regex.Match(seg, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                if (dg.Success && dl.Success && dl.Groups[1].Value.StartsWith(DownloadPrefix) && dl.Groups[1].Value.EndsWith("/NexusPen-Setup.exe"))
                { info.Sha256 = dg.Groups[1].Value; info.SetupUrl = dl.Groups[1].Value; }
            }
            return info;
        }

        // De nieuwe installer ophalen en controleren. done(pad) met het bestand als alles klopt, done(null) als iets misging.
        public static void Download(Info info, Action<string> done)
        {
            Thread th = new Thread(delegate()
            {
                string file = null;
                try
                {
                    if (info == null || info.SetupUrl == null || info.Sha256 == null) { done(null); return; }
                    string dir = Path.Combine(Path.GetTempPath(), "NexusPen-bijwerken");
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, "NexusPen-Setup-" + Regex.Replace(info.Version, "[^0-9.]", "") + ".exe");
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(info.SetupUrl);
                    req.UserAgent = "NexusPen/" + Program.Version;
                    req.Timeout = 30000; req.ReadWriteTimeout = 30000;
                    using (WebResponse resp = req.GetResponse())
                    using (Stream src = resp.GetResponseStream())
                    using (FileStream dst = File.Create(file))
                    {
                        byte[] buf = new byte[81920];
                        long total = 0; int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            total += n;
                            if (total > 60L * 1024 * 1024) throw new IOException("Download te groot");   // een installer is een paar MB
                            dst.Write(buf, 0, n);
                        }
                    }
                    // klopt de controlecode? Dan is het echt het bestand van GitHub en is er onderweg niets aan veranderd
                    string got;
                    using (SHA256 sha = SHA256.Create()) using (FileStream fs = File.OpenRead(file)) got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    if (got != info.Sha256) { Log.Info("Bijwerken: controlecode klopt niet, bestand weggegooid"); try { File.Delete(file); } catch { } done(null); return; }
                    done(file);
                }
                catch (Exception ex) { Log.Error("Nieuwe versie downloaden", ex); try { if (file != null) File.Delete(file); } catch { } done(null); }
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
