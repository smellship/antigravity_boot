using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AntigravityBootOverlay
{
    public static class Native
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumProc cb, IntPtr l);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr h);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr h);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr h, uint cmd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr h, out RECT r);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern int GetWindowLong32(IntPtr h, int i);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern int SetWindowLong32(IntPtr h, int i, int v);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);

        [DllImport("user32.dll")]
        public static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);

        [DllImport("user32.dll")]
        public static extern bool UnregisterPowerSettingNotification(IntPtr h);

        public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");

        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8;

        public const long WS_EX_TRANSPARENT = 0x20;
        public const long WS_EX_TOOLWINDOW = 0x80;
        public const long WS_EX_APPWINDOW = 0x40000;
        public const long WS_EX_NOACTIVATE = 0x8000000;

        public const uint SWP_NOSIZE = 1;
        public const uint SWP_NOMOVE = 2;
        public const uint SWP_NOZORDER = 4;
        public const uint SWP_NOACTIVATE = 0x10;
        public const uint SWP_SHOWWINDOW = 0x40;
        public const uint SWP_NOOWNERZORDER = 0x200;

        public const uint GW_OWNER = 4;

        public static void InitDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch { }
            try { SetProcessDPIAware(); }
            catch { }
        }

        public static long GetLong(IntPtr h, int i)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(h, i).ToInt64() : GetWindowLong32(h, i);
        }

        public static void SetLong(IntPtr h, int i, long v)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, i, new IntPtr(v));
            else SetWindowLong32(h, i, (int)v);
        }

        public static bool Bounds(IntPtr h, out RECT r)
        {
            if (DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Right > r.Left)
                return true;
            return GetWindowRect(h, out r);
        }

        public static bool Cloaked(IntPtr h)
        {
            int v;
            return DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out v, 4) == 0 && v != 0;
        }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            return sb.ToString();
        }

        public static string TitleOf(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, 512);
            return sb.ToString();
        }

        public static double IdleSeconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref info)) return 0;
            uint elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
            return elapsedMs / 1000.0;
        }

        public static bool IsBatterySaverActive(out bool onBattery)
        {
            onBattery = false;
            SYSTEM_POWER_STATUS sps;
            if (!GetSystemPowerStatus(out sps)) return false;
            onBattery = (sps.ACLineStatus == 0);
            return sps.SystemStatusFlag == 1;
        }

        public static double GetWindowDpiScale(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi >= 72 && dpi <= 480) return dpi / 96.0;
                }
            }
            catch { }
            return 1.0;
        }
    }
}
