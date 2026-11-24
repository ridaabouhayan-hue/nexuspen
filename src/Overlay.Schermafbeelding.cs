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
        // ---------- schermafbeelding
        bool screenshotBusy;
        public static string DefaultScreenshotDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NexusPen"); } }

        // Je eigen map (Instellingen > Opslaan), anders Afbeeldingen\NexusPen. Bestaat je map niet meer, dan de gewone.
        public static string ScreenshotDir
        {
            get
            {
                try
                {
                    string own = Instance != null ? Instance.Cfg.ShotFolder : "";
                    if (!string.IsNullOrEmpty(own) && Path.IsPathRooted(own) && Directory.Exists(own)) return own;
                }
                catch { }
                return DefaultScreenshotDir;
            }
        }

        public void Screenshot()
        {
            if (Program.SelfTest) Console.WriteLine("Schermafbeelding: gevraagd (bezig " + screenshotBusy + ")");
            if (screenshotBusy) return;
            screenshotBusy = true;
            try
            {
                if (selected != null) Deselect();
                if (toolbar != null) toolbar.HideForShot();
                RenderFrame();
            }
            catch (Exception ex) { Log.Error("Schermafbeelding voorbereiden", ex); }
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 150;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                if (Program.SelfTest) Console.WriteLine("Schermafbeelding: maken");
                try { CaptureScreen(); }
                finally
                {
                    screenshotBusy = false;
                    if (toolbar != null && !allHidden) toolbar.ShowAfterShot();
                }
            };
            t.Start();
        }

        void CaptureScreen()
        {
            try
            {
                Rectangle r = SystemInformation.VirtualScreen;
                using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        // met de tekenlaag en het bord erbij; lukt dat niet, dan de gewone manier van .NET
                        bool copied = false;
                        try { copied = W.CopyScreen(g, r); } catch (Exception ex) { Log.Error("Schermafbeelding met BitBlt", ex); }
                        if (!copied) { Log.Info("Schermafbeelding: BitBlt lukte niet, de gewone manier"); g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy); }
                    }
                    if (Program.SelfTest) Console.WriteLine("Schermafbeelding: scherm gekopieerd " + r.Size);
                    bool onClipboard = false;
                    try { Clipboard.SetImage(bmp); onClipboard = true; } catch (Exception ex) { Log.Error("Schermafbeelding naar klembord", ex); }   // eerst op het klembord: dat lukt ook als de map niet werkt
                    string name = "NexusPen_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
                    // je eigen map, anders Afbeeldingen\NexusPen, anders Downloads\NexusPen
                    // (Windows-beveiliging of OneDrive houdt Afbeeldingen soms op slot)
                    string dir = null;
                    foreach (string d in new string[] { ScreenshotDir, DefaultScreenshotDir, DownloadsShotDir })
                        if (d != null && TrySaveShot(bmp, d, name)) { dir = d; break; }
                    if (dir == null) { ShotFailed(onClipboard); return; }
                    LastShotPath = Path.Combine(dir, name);
                    Usage.Log("foto", "", "");
                    string shown = dir == DefaultScreenshotDir ? L.T("Afbeeldingen") + " > NexusPen" : dir == DownloadsShotDir ? "Downloads > NexusPen" : Path.GetFileName(dir.TrimEnd('\\'));
                    string more = onClipboard ? " " + L.T("en gekopieerd naar het klembord") : "";
                    if (toolbar != null) toolbar.ShowToast(L.T("Schermafbeelding opgeslagen in"), shown + more, dir, "vink");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Schermafbeelding mislukt", ex);
                ShotFailed(false);
            }
        }

        public string LastShotPath;
        static string DownloadsShotDir
        {
            get { try { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "NexusPen"); } catch { return null; } }
        }

        static bool TrySaveShot(Bitmap bmp, string dir, string name)
        {
            try { Directory.CreateDirectory(dir); bmp.Save(Path.Combine(dir, name), ImageFormat.Png); return true; }
            catch (Exception ex) { Log.Error("Schermafbeelding opslaan in " + dir, ex); return false; }
        }

        // mislukt: altijd een melding die je ziet (een ballonnetje van Windows zie je vaak niet)
        void ShotFailed(bool onClipboard)
        {
            string bold = onClipboard ? L.T("wel gekopieerd naar het klembord") : "";
            if (toolbar != null && !allHidden) toolbar.ShowToast(L.T("Schermafbeelding mislukt"), bold, null, "info");
            else if (tray != null) tray.ShowBalloonTip(3000, "NexusPen", L.T("Schermafbeelding mislukt"), ToolTipIcon.Warning);
        }
    }
}
