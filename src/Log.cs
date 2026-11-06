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
    // ------------------------------------------------------------------ Foutlog
    static class Log
    {
        static readonly object gate = new object();
        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusPen", "foutlog.txt"); }
        }

        public static void Info(string msg) { Write("INFO ", msg); }
        public static void Error(string msg, Exception ex) { Write("FOUT ", msg + (ex != null ? Environment.NewLine + Anon(ex.ToString()) : "")); }

        // het foutlog wordt soms gedeeld: je Windows-naam uit de paden halen
        static string Anon(string t)
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) t = t.Replace(home, "~");
            }
            catch { }
            return t;
        }

        static void Write(string level, string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
