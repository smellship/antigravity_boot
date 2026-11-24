using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AntigravityBootOverlay
{
    public class WindowInfo
    {
        public long Hwnd;
        public uint Pid;
        public string ProcessName;
        public string Title;
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public bool IsForeground;

        public IntPtr Handle { get { return new IntPtr(Hwnd); } }
        public long Area { get { return (long)Width * Height; } }
        public bool Valid { get { return Hwnd != 0; } }

        public Dictionary<string, object> ToMap()
        {
            var d = new Dictionary<string, object>();
            d["hwnd"] = Hwnd;
            d["pid"] = Pid;
            d["processName"] = ProcessName ?? "";
            d["title"] = Title ?? "";
            d["left"] = Left;
            d["top"] = Top;
            d["width"] = Width;
            d["height"] = Height;
            d["isForeground"] = IsForeground;
            return d;
        }
    }

    public static class WindowFinder
    {
        public static string TestTitle;
        static readonly Dictionary<uint, string> NameCache = new Dictionary<uint, string>();

        static string ProcName(uint pid)
        {
            string n;
            if (NameCache.TryGetValue(pid, out n)) return n;
            try { n = Process.GetProcessById((int)pid).ProcessName; }
            catch { n = ""; }
            if (NameCache.Count > 500) NameCache.Clear();
            NameCache[pid] = n;
            return n;
        }

        static bool MatchesProcess(string procName, List<string> targets)
        {
            if (string.IsNullOrEmpty(procName) || targets == null) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (string.Equals(procName, targets[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static List<WindowInfo> FindAll(List<string> targetProcesses, string testTitle)
        {
            var results = new List<WindowInfo>();
            uint selfPid = (uint)Process.GetCurrentProcess().Id;
            IntPtr fg = Native.GetForegroundWindow();

            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.Cloaked(h)) return true;
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;

                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == selfPid) return true;

                string proc = ProcName(pid);
                string title = Native.TitleOf(h);

                if (!string.IsNullOrEmpty(testTitle))
                {
                    if (title.IndexOf(testTitle, StringComparison.OrdinalIgnoreCase) < 0) return true;
                }
                else
                {
                    if (!MatchesProcess(proc, targetProcesses)) return true;
                    if (string.IsNullOrEmpty(title)) return true;
                }

                Native.RECT r;
                if (!Native.Bounds(h, out r)) return true;
                int w = r.Right - r.Left;
                int ht = r.Bottom - r.Top;
                if (w < 360 || ht < 240) return true;

                var info = new WindowInfo();
                info.Hwnd = h.ToInt64();
                info.Pid = pid;
                info.ProcessName = proc;
                info.Title = title;
                info.Left = r.Left;
                info.Top = r.Top;
                info.Width = w;
                info.Height = ht;
                info.IsForeground = (h == fg);
                results.Add(info);
                return true;
            }, IntPtr.Zero);

            results.Sort(delegate(WindowInfo a, WindowInfo b)
            {
                return b.Area.CompareTo(a.Area);
            });
            return results;
        }

        public static WindowInfo FindBest(AppConfig cfg, string testTitle)
        {
            var all = FindAll(cfg != null ? cfg.TargetProcesses : null, testTitle);
            if (cfg != null && cfg.LockedWindowHwnd != 0)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Hwnd == cfg.LockedWindowHwnd) return all[i];
                }
            }
            return all.Count > 0 ? all[0] : new WindowInfo();
        }
    }
}
