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
        // ---------- opstarten
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            W.SetProp(Handle, "MicrosoftTabletPenServiceProperty", new IntPtr(W.TABLET_FLAGS));
            int mods, vk;
            if (!Settings.ParseHotkey(Cfg.HotkeyDraw, out mods, out vk) || !W.RegisterHotKey(Handle, 1, mods | W.MOD_NOREPEAT, vk))
                Log.Info("Sneltoets " + Cfg.HotkeyDraw + " kon niet worden ingesteld (ongeldig of al in gebruik door een ander programma)");
            if (!Settings.ParseHotkey(Cfg.HotkeyHide, out mods, out vk) || !W.RegisterHotKey(Handle, 2, mods | W.MOD_NOREPEAT, vk))
                Log.Info("Sneltoets " + Cfg.HotkeyHide + " kon niet worden ingesteld (ongeldig of al in gebruik door een ander programma)");
            ApplyKeyHook();   // sneltoetsen overal
        }

        // Na het wijzigen in Instellingen: de twee sneltoetsen die overal werken opnieuw vastleggen. Geeft een foutmelding terug of null.
        public string RegisterGlobalHotkeys()
        {
            if (!IsHandleCreated) return null;
            W.UnregisterHotKey(Handle, 1); W.UnregisterHotKey(Handle, 2);
            string err = null;
            int mods, vk;
            if (!Settings.ParseHotkey(Cfg.HotkeyDraw, out mods, out vk) || !W.RegisterHotKey(Handle, 1, mods | W.MOD_NOREPEAT, vk))
                err = L.F("{0} is al in gebruik door een ander programma.", Cfg.HotkeyDraw);
            if (!Settings.ParseHotkey(Cfg.HotkeyHide, out mods, out vk) || !W.RegisterHotKey(Handle, 2, mods | W.MOD_NOREPEAT, vk))
                err = L.F("{0} is al in gebruik door een ander programma.", Cfg.HotkeyHide);
            SetupTrayMenu();
            return err;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            toolbar = new Toolbar(this);
            toolbar.Owner = this;
            toolbar.Show();
            SetupTray();
            UpdateTrayIcon();
            L.Changed += delegate { SetupTrayMenu(); if (toolbar != null) toolbar.Restyle(); };
            Rebuild();
            RenderFrame();
            Usage.Log("start", Program.Version, Screen.AllScreens.Length + " scherm(en)");
            // Even later, als alles staat: vensters klaarzetten en een keer onzichtbaar tekenen,
            // dan hapert de eerste echte klik en de eerste lijn niet
            System.Windows.Forms.Timer warm = new System.Windows.Forms.Timer();
            warm.Interval = 400;
            warm.Tick += delegate { warm.Stop(); warm.Dispose(); WarmUp(); };
            warm.Start();
            if (StartCollapsed) CollapseAll();
            if (updateFailed)
                tray.ShowBalloonTip(5000, "NexusPen", L.T("Bijwerken is niet gelukt. Je werkt nog met de vorige versie. Probeer het later nog eens."), ToolTipIcon.Warning);
            else if (justUpdated)
                tray.ShowBalloonTip(4000, "NexusPen", L.F("NexusPen is bijgewerkt naar {0}. Je tekening staat er nog.", Program.Version), ToolTipIcon.Info);
            else if (restoredAfterCrash)
                tray.ShowBalloonTip(4000, "NexusPen", L.T("Je tekening van de vorige keer is teruggezet, want NexusPen was onverwacht gestopt. Alles wissen kan met de prullenbak."), ToolTipIcon.Info);
            else if (restoredSaved)
                tray.ShowBalloonTip(4000, "NexusPen", L.T("Je opgeslagen tekening staat er weer. Liever leeg beginnen? Klik op de prullenbak en daarna op Opslaan."), ToolTipIcon.Info);

            if (Cfg.UpdateCheck && !Program.SelfTest)
                Updates.CheckLater(20000, delegate(Updates.Info info)
                {
                    try { BeginInvoke((Action)delegate { UpdateFound(info, false); }); } catch { }
                });
        }

        // ---------- bijwerken met een klik
        public Updates.Info PendingUpdate;
        bool updating, justUpdated, updateFailed, balloonIsUpdate;
        public bool Updating { get { return updating; } }

        void UpdateFound(Updates.Info info, bool fromSettings)
        {
            PendingUpdate = info;
            SetupTrayMenu();
            RefreshSettingsWindow();
            if (!fromSettings)
            {
                updateUrl = info.PageUrl; updateShownAt = DateTime.Now; balloonIsUpdate = true;
                tray.ShowBalloonTip(8000, "NexusPen", L.F("Er is een nieuwe versie: {0}. Klik hier om bij te werken.", info.Version), ToolTipIcon.Info);
            }
        }

        // Nu kijken (vanuit Instellingen). result: 1 = er is iets nieuws, 0 = je hebt de nieuwste, -1 = kon niet kijken
        public void CheckForUpdateNow(Action<int> result)
        {
            Updates.CheckLater(0, delegate(Updates.Info info)
            {
                try { BeginInvoke((Action)delegate { UpdateFound(info, true); if (result != null) result(1); }); } catch { }
            }, delegate
            {
                try { BeginInvoke((Action)delegate { if (result != null) result(0); }); } catch { }
            }, delegate
            {
                try { BeginInvoke((Action)delegate { if (result != null) result(-1); }); } catch { }
            });
        }

        // Downloaden, controleren, je tekening bewaren, de installer stil laten lopen; die start NexusPen daarna weer
        public void InstallUpdate()
        {
            Updates.Info info = PendingUpdate;
            if (info == null || updating) return;
            if (info.SetupUrl == null || info.Sha256 == null)
            {
                // geen controlecode bij deze versie: dan niet zelf installeren, alleen de pagina openen
                try { if (info.PageUrl.StartsWith("https://github.com/")) System.Diagnostics.Process.Start(info.PageUrl); } catch { }
                return;
            }
            updating = true;   // meteen: dan kan er niet een tweede vraag of download bij komen
            if (MessageBox.Show(this, L.F("NexusPen bijwerken naar {0}? NexusPen sluit even en start daarna vanzelf weer. Je tekening blijft staan.", info.Version), "NexusPen",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { updating = false; return; }
            if (toolbar != null) toolbar.ShowToast(L.T("De nieuwe versie wordt opgehaald..."), "", null, "download");
            Usage.Log("bijwerken", info.Version, "");
            Updates.Download(info, delegate(string file)
            {
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        updating = false;
                        if (file == null)
                        {
                            tray.ShowBalloonTip(6000, "NexusPen", L.T("Bijwerken lukte niet. Probeer het later nog eens, of download hem zelf van de downloadpagina."), ToolTipIcon.Warning);
                            return;
                        }
                        try
                        {
                            CloseTextInput(true);
                            SaveNow();
                            Updates.WriteFlag(info.Version);   // bij de volgende start: tekening terugzetten (of melden dat het niet lukte)
                            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /BIJWERKEN");
                            psi.UseShellExecute = true;
                            System.Diagnostics.Process.Start(psi);
                            Close();   // de installer vervangt het programma en start het daarna weer
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Installer starten", ex);
                            try { File.Delete(Updates.FlagPath); } catch { }
                            tray.ShowBalloonTip(6000, "NexusPen", L.T("Bijwerken lukte niet. Probeer het later nog eens, of download hem zelf van de downloadpagina."), ToolTipIcon.Warning);
                        }
                    });
                }
                catch (Exception ex) { Quiet("Bijwerken starten", ex); }
            });
        }

        void WarmUp()
        {
            try
            {
                if (toolbar != null) toolbar.Prewarm();
                using (Bitmap b = new Bitmap(96, 96, PixelFormat.Format32bppPArgb))
                using (Graphics g = Graphics.FromImage(b))
                {
                    Render.Setup(g);
                    Stroke s = new Stroke(); s.Tool = Tool.Pen; s.Color = CurrentColor; s.Width = 4;
                    s.Pts.Add(new PointF(5, 5)); s.Pts.Add(new PointF(40, 30)); s.Pts.Add(new PointF(80, 10));
                    Render.Draw(g, s, 1f);
                    Render.DrawWet(g, s, new Rectangle(0, 0, 96, 96));
                    Render.Bounds(s);
                    Stroke t = new Stroke(); t.Tool = Tool.Text; t.Text = "Ab"; t.FontSize = 20; t.FontName = Cfg.FontName; t.Color = Color.Black; t.Outline = Cfg.TextOutline;
                    t.Pts.Add(new PointF(5, 40));
                    Render.Draw(g, t, 1f); Render.Bounds(t);
                    Stroke st = new Stroke(); st.Tool = Tool.Stamp; st.Text = "vink"; st.FontSize = 28; st.Color = Render.StampColor("vink");
                    st.Pts.Add(new PointF(60, 60));
                    Render.Draw(g, st, 1f);
                }
            }
            catch (Exception ex) { Log.Error("Opwarmen", ex); }
        }

        string updateUrl;
        DateTime updateShownAt;

        // Instellingen opnieuw opbouwen als de balk ergens anders is veranderd
        public void RefreshTimer() { if (lesTimer != null && !lesTimer.IsDisposed && lesTimer.Visible) lesTimer.Invalidate(); }
        public void RefreshSettingsWindow() { if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Rebuild(); }

        void OnBalloonClicked(object sender, EventArgs e)
        {
            if (!balloonIsUpdate || updateUrl == null || (DateTime.Now - updateShownAt).TotalSeconds > 12) return;   // alleen de melding over de nieuwe versie
            updateUrl = null; balloonIsUpdate = false;
            InstallUpdate();
        }
    }
}
