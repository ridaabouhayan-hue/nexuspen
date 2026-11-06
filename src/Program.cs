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

[assembly: System.Reflection.AssemblyTitle("NexusPen")]
[assembly: System.Reflection.AssemblyProduct("NexusPen")]
[assembly: System.Reflection.AssemblyCompany("Nexus of Language")]
[assembly: System.Reflection.AssemblyVersion("1.2.6.0")]

namespace NexusPen
{
    // ------------------------------------------------------------------ Start
    static class Program
    {
        static long uiPing;
        public static void Ping() { Interlocked.Exchange(ref uiPing, DateTime.UtcNow.Ticks); }

        static void SetDpiAwareness()
        {
            // Per scherm de juiste schaal (100%, 125%, 150% door elkaar). Het manifest regelt dit ook al.
            // Lukt de aanroep niet omdat het manifest het al geregeld heeft, dan is dat goed en stoppen we.
            try { W.SetProcessDpiAwarenessContext(new IntPtr(-4)); return; } catch (EntryPointNotFoundException) { }
            try { W.SetProcessDpiAwareness(2); return; } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
            try { W.SetProcessDPIAware(); } catch { }
        }

        // Waakhond: als het programma in tekenmodus vastloopt, blokkeert het je hele scherm.
        // Dan stoppen we het programma, zodat je scherm altijd vrij komt. De tekening is al bewaard.
        static void StartWatchdog()
        {
            Thread t = new Thread(delegate()
            {
                long lastLoop = DateTime.UtcNow.Ticks;
                int strikes = 0;
                while (true)
                {
                    Thread.Sleep(1000);
                    long now = DateTime.UtcNow.Ticks;
                    bool resumed = (now - lastLoop) / 1e7 > 3; // pc kwam net uit slaapstand: niet meetellen
                    lastLoop = now;
                    if (resumed) { strikes = 0; Ping(); continue; }
                    long last = Interlocked.Read(ref uiPing);
                    Overlay ov = Overlay.Instance;
                    if (last == 0 || ov == null || !ov.DrawMode) { strikes = 0; continue; }
                    double sec = (now - last) / 1e7;
                    strikes = sec > 8 ? strikes + 1 : 0;
                    if (strikes >= 2)
                    {
                        Log.Info("Programma reageerde " + sec.ToString("0") + " s niet in tekenmodus: noodstop zodat het scherm vrij komt");
                        try { Process.GetCurrentProcess().Kill(); } catch { }
                    }
                }
            });
            t.IsBackground = true;
            t.Name = "NexusPen waakhond";
            t.Start();
        }

        // .NET onthoudt welke code bij het opstarten nodig is en zet die de volgende keer alvast klaar (sneller starten)
        static void StartProfile()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen");
                Directory.CreateDirectory(dir);
                System.Runtime.ProfileOptimization.SetProfileRoot(dir);
                System.Runtime.ProfileOptimization.StartProfile("opstarten.profiel");
            }
            catch { }
        }

        public static bool SelfTest;
        public static int SelfTestErrors;

        [STAThread]
        static int Main(string[] args)
        {
            SelfTest = Array.IndexOf(args, "--zelftest") >= 0;
            Overlay.StartCollapsed = Array.IndexOf(args, "--opstarten") >= 0;   // bij het opstarten van Windows: ingeklapt beginnen
            bool created;
            using (Mutex m = new Mutex(true, SelfTest ? "NexusPen_Zelftest" : "NexusPen_EenKeerStarten", out created))
            {
                if (!created) { MessageBox.Show("NexusPen draait al. Kijk rechtsonder bij de klok.", "NexusPen"); return 0; }
                SetDpiAwareness();
                StartProfile();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    SelfTestErrors++;
                    Log.Error("Onverwachte fout", e.Exception);
                    if (SelfTest) { Console.Error.WriteLine(e.Exception); return; }
                    Overlay ov = Overlay.Instance;
                    if (ov != null) { ov.EmergencyMouseMode(); ov.ReportError(); }
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Error("Fatale fout", e.ExceptionObject as Exception);
                };
                Log.Info("Start NexusPen " + Version + (SelfTest ? " (zelftest)" : "") + " op " + Environment.OSVersion + ", .NET " + Environment.Version + ", " + Screen.AllScreens.Length + " scherm(en)");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (!SelfTest) StartWatchdog();
                Overlay main = new Overlay();
                if (SelfTest) main.RunSelfTest();
                Application.Run(main);
                return SelfTest ? (SelfTestErrors == 0 ? 0 : 1) : 0;
            }
        }

        public const string Version = "1.2.6";
    }
}
