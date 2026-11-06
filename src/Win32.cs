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
    // ------------------------------------------------------------------ Win32
    static class W
    {
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8,
            WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, GWL_EXSTYLE = -20,
            WM_HOTKEY = 0x0312, WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, ULW_ALPHA = 2,
            MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000,
            WM_DPICHANGED = 0x02E0, WM_DISPLAYCHANGE = 0x007E, WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02CC,
            SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, VK_ESCAPE = 0x1B;
        // Windows Ink: geen 'lang drukken = rechtermuisklik', geen tikrondjes, geen flicks
        public const int TABLET_FLAGS = 0x00000001 | 0x00000008 | 0x00000010 | 0x00010000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "UpdateLayeredWindow")]
        public static extern bool UpdateLayeredWindowSize(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
            out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr hWnd, string name, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        // Venster wel of niet zichtbaar in schermdelen (Zoom, Teams). Werkt vanaf Windows 10 versie 2004.
        public static void HideFromCapture(IntPtr hwnd, bool hide)
        {
            try
            {
                if (!SetWindowDisplayAffinity(hwnd, hide ? 0x11u : 0u) && hide) SetWindowDisplayAffinity(hwnd, 1u);
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct UPDATELAYEREDWINDOWINFO
        {
            public int cbSize; public IntPtr hdcDst, pptDst, psize, hdcSrc, pptSrc;
            public int crKey; public IntPtr pblend; public int dwFlags; public IntPtr prcDirty;
        }
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindowIndirect(IntPtr hwnd, ref UPDATELAYEREDWINDOWINFO info);

        static IntPtr ulwBuf = IntPtr.Zero;

        // Werkt alleen het gewijzigde stuk van het scherm bij (veel sneller op grote schermen)
        public static bool UpdateLayeredDirty(IntPtr hwnd, POINT dst, SIZE size, IntPtr hdcSrc, BLENDFUNCTION bf, Rectangle dirty)
        {
            if (ulwBuf == IntPtr.Zero) ulwBuf = Marshal.AllocHGlobal(64);
            POINT src = new POINT(0, 0);
            RECT rc = new RECT(); rc.Left = dirty.Left; rc.Top = dirty.Top; rc.Right = dirty.Right; rc.Bottom = dirty.Bottom;
            Marshal.StructureToPtr(dst, ulwBuf, false);
            Marshal.StructureToPtr(size, new IntPtr(ulwBuf.ToInt64() + 8), false);
            Marshal.StructureToPtr(src, new IntPtr(ulwBuf.ToInt64() + 16), false);
            Marshal.StructureToPtr(bf, new IntPtr(ulwBuf.ToInt64() + 24), false);
            Marshal.StructureToPtr(rc, new IntPtr(ulwBuf.ToInt64() + 32), false);
            UPDATELAYEREDWINDOWINFO info = new UPDATELAYEREDWINDOWINFO();
            info.cbSize = Marshal.SizeOf(typeof(UPDATELAYEREDWINDOWINFO));
            info.pptDst = ulwBuf; info.psize = new IntPtr(ulwBuf.ToInt64() + 8); info.hdcSrc = hdcSrc;
            info.pptSrc = new IntPtr(ulwBuf.ToInt64() + 16); info.pblend = new IntPtr(ulwBuf.ToInt64() + 24);
            info.dwFlags = ULW_ALPHA; info.prcDirty = new IntPtr(ulwBuf.ToInt64() + 32);
            return UpdateLayeredWindowIndirect(hwnd, ref info);
        }

        // Windows 11: gladde ronde hoeken en een randkleur van Windows zelf. Geeft false op Windows 10.
        public static bool RoundCorners(IntPtr hwnd, Color border)
        {
            try
            {
                int pref = 2;   // DWMWCP_ROUND
                if (DwmSetWindowAttribute(hwnd, 33, ref pref, 4) != 0) return false;
                int c = border.R | (border.G << 8) | (border.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref c, 4);
                return true;
            }
            catch { return false; }
        }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr hCursor);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hWnd);

        // Het toetsenbord echt naar dit venster halen. Windows weigert dat soms als een ander programma
        // vooraan stond; dan koppelen we heel even aan dat programma, zodat het wel mag.
        public static void ForceForeground(IntPtr h)
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == h) { SetFocus(h); return; }
                uint me = GetCurrentThreadId(), other = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, IntPtr.Zero) : 0;
                // nooit koppelen aan een programma dat vastzit: dan zou NexusPen meehangen
                bool attached = other != 0 && other != me && !IsHungAppWindow(fg) && AttachThreadInput(me, other, true);
                try { SetForegroundWindow(h); SetFocus(h); }
                finally { if (attached) AttachThreadInput(me, other, false); }
            }
            catch { }
        }

        // Staat er nu een ander programma bovenop dat zelf 'altijd bovenop' wil zijn (Knipprogramma, Win+Shift+S,
        // schermopname)? Dan gaan wij er niet overheen, anders botsen we met het maken van een schermafbeelding.
        static uint myPid;

        public static bool OtherTopmostInFront()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (myPid == 0) myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (pid == myPid) return false;
                if ((GetWindowLong(fg, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) return false;
                // de taakbalk en het startmenu zijn ook 'bovenop'; daar willen we wel weer overheen zodra je verder gaat
                System.Text.StringBuilder cls = new System.Text.StringBuilder(64);
                GetClassName(fg, cls, 64);
                string c = cls.ToString();
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
                return true;
            }
            catch { return false; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

        public static void KeepOnTop(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // Op een groot scherm (meer dan 1080 punten hoog na de schaal van Windows) groeit de werkbalk mee, tot 1,5 keer
        public static float ScreenFactorAt(Point p, float dpiScale)
        {
            try
            {
                Rectangle b = Screen.FromPoint(p).Bounds;
                float logical = b.Height / Math.Max(1f, dpiScale);
                return Math.Max(1f, Math.Min(1.5f, logical / 1080f));
            }
            catch { return 1f; }
        }

        // Schaal voor de werkbalk en de kleine vensters: schaal van Windows maal de grootte van het scherm
        public static float UiScaleAt(Point p)
        {
            float d = DpiScaleAt(p);
            return d * ScreenFactorAt(p, d);
        }

        public static float DpiScaleAt(Point p)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT(p.X, p.Y), 2);
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return Math.Max(1f, dx / 96f);
            }
            catch { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f);
        }
    }
}
