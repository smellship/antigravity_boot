using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace AntigravityBootOverlay
{
    public static class AppPaths
    {
        static string customDataDir;

        public static void SetDataDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir)) customDataDir = Path.GetFullPath(dir);
        }

        public static string DataDir
        {
            get
            {
                if (!string.IsNullOrEmpty(customDataDir)) return customDataDir;
                string envDir = Environment.GetEnvironmentVariable("ANTIGRAVITY_BOOT_DATA_DIR");
                if (!string.IsNullOrEmpty(envDir)) return Path.GetFullPath(envDir);
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AntigravityBootStudio");
            }
        }

        public static string ConfigPath { get { return Path.Combine(DataDir, "config.json"); } }
        public static string ThemesPath { get { return Path.Combine(DataDir, "themes.json"); } }
        public static string PacksDir { get { return Path.Combine(DataDir, "packs"); } }
        public static string RuntimePath { get { return Path.Combine(DataDir, "runtime.json"); } }
        public static string LogPath { get { return Path.Combine(DataDir, "overlay.log"); } }
        public static string SelfTestPath { get { return Path.Combine(DataDir, "selftest.json"); } }
    }

    public static class Log
    {
        static readonly object Sync = new object();

        public static void W(string msg)
        {
            try
            {
                lock (Sync)
                {
                    string path = AppPaths.LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    {
                        string backup = path + ".1";
                        try { if (File.Exists(backup)) File.Delete(backup); fi.MoveTo(backup); }
                        catch { try { fi.Delete(); } catch { } }
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + msg + "\r\n";
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static List<string> ReadTail(int maxLines)
        {
            var result = new List<string>();
            try
            {
                string path = AppPaths.LogPath;
                if (!File.Exists(path)) return result;
                string[] all;
                lock (Sync)
                {
                    all = File.ReadAllLines(path, Encoding.UTF8);
                }
                int start = Math.Max(0, all.Length - maxLines);
                for (int i = start; i < all.Length; i++) result.Add(all[i]);
            }
            catch { }
            return result;
        }
    }

    public static class GpuPref
    {
        const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";

        public static void Ensure()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (k == null) return;
                    var cur = k.GetValue(exe) as string;
                    if (cur != null)
                    {
                        Log.W("显卡偏好已配置（" + cur + "），保持不变");
                        return;
                    }
                    var gpus = ListGpus();
                    if (gpus.Count < 2)
                    {
                        Log.W("检测到显卡：" + (gpus.Count == 0 ? "未识别" : string.Join("、", gpus)));
                        return;
                    }
                    k.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
                    Log.W("检测到多显卡（" + string.Join("、", gpus) + "），已设为高性能显卡渲染");
                }
            }
            catch (Exception ex)
            {
                Log.W("设置显卡偏好失败：" + ex.Message);
            }
        }

        static List<string> ListGpus()
        {
            var list = new List<string>();
            using (var q = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
            {
                foreach (ManagementBaseObject o in q.Get())
                {
                    var name = (o["Name"] as string ?? "").Trim();
                    if (Regex.IsMatch(name, "NVIDIA|GeForce|Quadro|RTX|AMD|Radeon|Intel|Arc", RegexOptions.IgnoreCase))
                        list.Add(name);
                }
            }
            return list;
        }
    }

    public class SubtitleItem
    {
        public string Text;
        public double Start;
        public double End;

        public SubtitleItem() { }
        public SubtitleItem(string t, double s, double e) { Text = t; Start = s; End = e; }
    }

    public class ThemeDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public string Kind; // procedural | video | image
        public bool Builtin;
        public string IntroPath;
        public string LoopPath;
        public string ImagePath;
        public string PreviewPath;
        public string FontPath;
        public double FadeStartSeconds;
        public double FadeDurationSeconds;
        public bool ShowSubtitles;
        public double SubtitleFadeSeconds;
        public double SubtitleWidthRatio;
        public string AccentColor;
        public List<SubtitleItem> Subtitles;

        public ThemeDefinition()
        {
            Kind = "procedural";
            FadeStartSeconds = 4.2;
            FadeDurationSeconds = 1.2;
            ShowSubtitles = true;
            SubtitleFadeSeconds = 0.5;
            SubtitleWidthRatio = 0.6;
            AccentColor = "#60A5FA";
            Subtitles = new List<SubtitleItem>();
        }

        public static ThemeDefinition BuiltinStellarAurora()
        {
            var t = new ThemeDefinition();
            t.Id = "stellar-aurora";
            t.Name = "✦ 星穹极光 · Stellar Aurora";
            t.Description = "内置离线矢量粒子主题：双弧星芒开屏 + 透出后星尘与极光流线呼吸律动，零外链依赖。";
            t.Kind = "procedural";
            t.Builtin = true;
            t.FadeStartSeconds = 4.2;
            t.FadeDurationSeconds = 1.2;
            t.ShowSubtitles = true;
            t.SubtitleFadeSeconds = 0.5;
            t.SubtitleWidthRatio = 0.56;
            t.AccentColor = "#60A5FA";
            t.Subtitles.Add(new SubtitleItem("Twin stars aligned · Ready to build.", 1.6, 4.1));
            return t;
        }
    }

    public class AppConfig
    {
        public string ActiveThemeId;
        public bool ShuffleOnBoot;
        public bool PlayIntro;
        public bool LoopBackground;
        public double ResidualAlpha;
        public double IntroVolume;
        public string StretchMode;
        public bool FocusDimEnabled;
        public double FocusDimFactor;
        public double FocusIdleSeconds;
        public bool PauseOnBatterySaver;
        public bool ShowTrayIcon;
        public List<string> TargetProcesses;
        public long LockedWindowHwnd;

        // 动态挂件布局配置
        public string DisplayMode;       // widget | widget-always | fullscreen
        public string WidgetAnchor;      // bottom-left | bottom-right | top-left | top-right
        public double WidgetOffsetX;
        public double WidgetOffsetY;
        public double WidgetWidth;
        public double WidgetHeight;
        public double WidgetCornerRadius;
        public double WidgetOpacity;
        public bool WidgetClickThrough;
        public bool WidgetLockAspectRatio;

        public AppConfig()
        {
            ActiveThemeId = "stellar-aurora";
            ShuffleOnBoot = false;
            PlayIntro = true;
            LoopBackground = true;
            ResidualAlpha = 0.24;
            IntroVolume = 0.90;
            StretchMode = "UniformToFill";
            FocusDimEnabled = true;
            FocusDimFactor = 0.45;
            FocusIdleSeconds = 4.0;
            PauseOnBatterySaver = true;
            ShowTrayIcon = false;
            TargetProcesses = new List<string> { "Antigravity" };
            LockedWindowHwnd = 0;

            DisplayMode = "widget";
            WidgetAnchor = "bottom-left";
            WidgetOffsetX = 12;
            WidgetOffsetY = 52;
            WidgetWidth = 234;
            WidgetHeight = 132;
            WidgetCornerRadius = 12;
            WidgetOpacity = 0.94;
            WidgetClickThrough = false;
            WidgetLockAspectRatio = true;
        }

        public void Clamp()
        {
            if (string.IsNullOrEmpty(ActiveThemeId)) ActiveThemeId = "stellar-aurora";
            ResidualAlpha = Math.Max(0.0, Math.Min(0.80, ResidualAlpha));
            IntroVolume = Math.Max(0.0, Math.Min(1.0, IntroVolume));
            if (StretchMode != "Uniform" && StretchMode != "UniformToFill") StretchMode = "UniformToFill";
            FocusDimFactor = Math.Max(0.10, Math.Min(1.0, FocusDimFactor));
            FocusIdleSeconds = Math.Max(1.0, Math.Min(30.0, FocusIdleSeconds));
            if (TargetProcesses == null || TargetProcesses.Count == 0)
                TargetProcesses = new List<string> { "Antigravity" };

            if (DisplayMode != "widget" && DisplayMode != "widget-always" && DisplayMode != "fullscreen")
                DisplayMode = "widget";
            if (WidgetAnchor != "bottom-left" && WidgetAnchor != "bottom-right" && WidgetAnchor != "top-left" && WidgetAnchor != "top-right")
                WidgetAnchor = "bottom-left";
            WidgetOffsetX = Math.Max(0.0, Math.Min(1600.0, WidgetOffsetX));
            WidgetOffsetY = Math.Max(0.0, Math.Min(1200.0, WidgetOffsetY));
            WidgetWidth = Math.Max(140.0, Math.Min(720.0, WidgetWidth));
            if (WidgetLockAspectRatio) WidgetHeight = Math.Round(WidgetWidth * 9.0 / 16.0);
            WidgetHeight = Math.Max(78.0, Math.Min(480.0, WidgetHeight));
            WidgetCornerRadius = Math.Max(0.0, Math.Min(32.0, WidgetCornerRadius));
            WidgetOpacity = Math.Max(0.20, Math.Min(1.0, WidgetOpacity));
        }
    }

    public static class ConfigStore
    {
        static double D(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null)
            {
                try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
                catch { }
            }
            return def;
        }

        static long L(Dictionary<string, object> d, string k, long def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null)
            {
                try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
                catch { }
            }
            return def;
        }

        static bool B(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is bool) return (bool)v;
            return def;
        }

        static string S(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null)
            {
                string str = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(str)) return str;
            }
            return def;
        }

        static string SAllowEmpty(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public static void AtomicWriteText(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + "." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch { try { File.Copy(tmp, path, true); File.Delete(tmp); return; } catch { } }
            }
            try { File.Move(tmp, path); }
            catch { File.Copy(tmp, path, true); try { File.Delete(tmp); } catch { } }
        }

        public static AppConfig LoadConfig()
        {
            var c = new AppConfig();
            try
            {
                if (File.Exists(AppPaths.ConfigPath))
                {
                    var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(AppPaths.ConfigPath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (d != null)
                    {
                        c.ActiveThemeId = S(d, "activeThemeId", c.ActiveThemeId);
                        c.ShuffleOnBoot = B(d, "shuffleOnBoot", c.ShuffleOnBoot);
                        c.PlayIntro = B(d, "playIntro", c.PlayIntro);
                        c.LoopBackground = B(d, "loopBackground", c.LoopBackground);
                        c.ResidualAlpha = D(d, "residualAlpha", c.ResidualAlpha);
                        c.IntroVolume = D(d, "introVolume", c.IntroVolume);
                        c.StretchMode = S(d, "stretchMode", c.StretchMode);
                        c.FocusDimEnabled = B(d, "focusDimEnabled", c.FocusDimEnabled);
                        c.FocusDimFactor = D(d, "focusDimFactor", c.FocusDimFactor);
                        c.FocusIdleSeconds = D(d, "focusIdleSeconds", c.FocusIdleSeconds);
                        c.PauseOnBatterySaver = B(d, "pauseOnBatterySaver", c.PauseOnBatterySaver);
                        c.ShowTrayIcon = B(d, "showTrayIcon", c.ShowTrayIcon);
                        c.LockedWindowHwnd = L(d, "lockedWindowHwnd", c.LockedWindowHwnd);
                        c.DisplayMode = S(d, "displayMode", c.DisplayMode);
                        c.WidgetAnchor = S(d, "widgetAnchor", c.WidgetAnchor);
                        c.WidgetOffsetX = D(d, "widgetOffsetX", c.WidgetOffsetX);
                        c.WidgetOffsetY = D(d, "widgetOffsetY", c.WidgetOffsetY);
                        c.WidgetWidth = D(d, "widgetWidth", c.WidgetWidth);
                        c.WidgetHeight = D(d, "widgetHeight", c.WidgetHeight);
                        c.WidgetCornerRadius = D(d, "widgetCornerRadius", c.WidgetCornerRadius);
                        c.WidgetOpacity = D(d, "widgetOpacity", c.WidgetOpacity);
                        c.WidgetClickThrough = B(d, "widgetClickThrough", c.WidgetClickThrough);
                        c.WidgetLockAspectRatio = B(d, "widgetLockAspectRatio", c.WidgetLockAspectRatio);
                        object procs;
                        if (d.TryGetValue("targetProcesses", out procs) && procs is object[])
                        {
                            var list = new List<string>();
                            foreach (var item in (object[])procs)
                            {
                                string s = Convert.ToString(item, CultureInfo.InvariantCulture);
                                if (!string.IsNullOrEmpty(s)) list.Add(s.Trim());
                            }
                            if (list.Count > 0) c.TargetProcesses = list;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.W("读取 config.json 失败，使用默认值：" + ex.Message);
            }
            c.Clamp();
            if (!File.Exists(AppPaths.ConfigPath)) SaveConfig(c);
            return c;
        }

        public static void SaveConfig(AppConfig c)
        {
            if (c == null) return;
            c.Clamp();
            try
            {
                var js = new JavaScriptSerializer();
                var dict = ConfigToMap(c);
                AtomicWriteText(AppPaths.ConfigPath, PrettyJson(js.Serialize(dict)));
            }
            catch (Exception ex)
            {
                Log.W("保存 config.json 失败：" + ex.Message);
            }
        }

        public static Dictionary<string, object> ConfigToMap(AppConfig c)
        {
            var dict = new Dictionary<string, object>();
            dict["activeThemeId"] = c.ActiveThemeId;
            dict["shuffleOnBoot"] = c.ShuffleOnBoot;
            dict["playIntro"] = c.PlayIntro;
            dict["loopBackground"] = c.LoopBackground;
            dict["residualAlpha"] = Math.Round(c.ResidualAlpha, 3);
            dict["introVolume"] = Math.Round(c.IntroVolume, 3);
            dict["stretchMode"] = c.StretchMode;
            dict["focusDimEnabled"] = c.FocusDimEnabled;
            dict["focusDimFactor"] = Math.Round(c.FocusDimFactor, 3);
            dict["focusIdleSeconds"] = Math.Round(c.FocusIdleSeconds, 2);
            dict["pauseOnBatterySaver"] = c.PauseOnBatterySaver;
            dict["showTrayIcon"] = c.ShowTrayIcon;
            dict["targetProcesses"] = c.TargetProcesses.ToArray();
            dict["lockedWindowHwnd"] = c.LockedWindowHwnd;
            dict["displayMode"] = c.DisplayMode;
            dict["widgetAnchor"] = c.WidgetAnchor;
            dict["widgetOffsetX"] = Math.Round(c.WidgetOffsetX, 1);
            dict["widgetOffsetY"] = Math.Round(c.WidgetOffsetY, 1);
            dict["widgetWidth"] = Math.Round(c.WidgetWidth, 1);
            dict["widgetHeight"] = Math.Round(c.WidgetHeight, 1);
            dict["widgetCornerRadius"] = Math.Round(c.WidgetCornerRadius, 1);
            dict["widgetOpacity"] = Math.Round(c.WidgetOpacity, 3);
            dict["widgetClickThrough"] = c.WidgetClickThrough;
            dict["widgetLockAspectRatio"] = c.WidgetLockAspectRatio;
            return dict;
        }

        public static List<ThemeDefinition> LoadThemes()
        {
            var list = new List<ThemeDefinition>();
            bool hasBuiltin = false;
            try
            {
                if (File.Exists(AppPaths.ThemesPath))
                {
                    var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(AppPaths.ThemesPath, Encoding.UTF8)) as Dictionary<string, object>;
                    object arrObj;
                    if (root != null && root.TryGetValue("themes", out arrObj) && arrObj is object[])
                    {
                        foreach (var item in (object[])arrObj)
                        {
                            var d = item as Dictionary<string, object>;
                            if (d == null) continue;
                            var t = ParseTheme(d);
                            if (t == null) continue;
                            if (t.Id == "stellar-aurora")
                            {
                                hasBuiltin = true;
                                t.Builtin = true;
                            }
                            list.Add(t);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.W("读取 themes.json 失败：" + ex.Message);
            }

            if (!hasBuiltin)
            {
                list.Insert(0, ThemeDefinition.BuiltinStellarAurora());
                if (!File.Exists(AppPaths.ThemesPath)) SaveThemes(list);
            }
            return list;
        }

        static ThemeDefinition ParseTheme(Dictionary<string, object> d)
        {
            string id = S(d, "id", "");
            if (string.IsNullOrEmpty(id)) return null;
            var t = new ThemeDefinition();
            t.Id = id;
            t.Name = S(d, "name", id);
            t.Description = SAllowEmpty(d, "description", "");
            t.Kind = S(d, "kind", "video");
            t.Builtin = B(d, "builtin", id == "stellar-aurora");
            t.IntroPath = SAllowEmpty(d, "introPath", "");
            t.LoopPath = SAllowEmpty(d, "loopPath", "");
            t.ImagePath = SAllowEmpty(d, "imagePath", "");
            t.PreviewPath = SAllowEmpty(d, "previewPath", "");
            t.FontPath = SAllowEmpty(d, "fontPath", "");
            t.FadeStartSeconds = D(d, "fadeStartSeconds", 10.0);
            t.FadeDurationSeconds = D(d, "fadeDurationSeconds", 1.2);
            t.ShowSubtitles = B(d, "showSubtitles", false);
            t.SubtitleFadeSeconds = D(d, "subtitleFadeSeconds", 0.5);
            t.SubtitleWidthRatio = D(d, "subtitleWidthRatio", 0.6);
            t.AccentColor = S(d, "accentColor", "#60A5FA");
            object subs;
            if (d.TryGetValue("subtitles", out subs) && subs is object[])
            {
                foreach (var o in (object[])subs)
                {
                    var sd = o as Dictionary<string, object>;
                    if (sd == null) continue;
                    string txt = SAllowEmpty(sd, "text", "");
                    if (string.IsNullOrEmpty(txt)) continue;
                    t.Subtitles.Add(new SubtitleItem(txt, D(sd, "start", 0), D(sd, "end", 0)));
                }
            }
            return t;
        }

        public static void SaveThemes(List<ThemeDefinition> themes)
        {
            try
            {
                var arr = new List<Dictionary<string, object>>();
                foreach (var t in themes) arr.Add(ThemeToMap(t));
                var root = new Dictionary<string, object>();
                root["themes"] = arr.ToArray();
                var js = new JavaScriptSerializer();
                AtomicWriteText(AppPaths.ThemesPath, PrettyJson(js.Serialize(root)));
            }
            catch (Exception ex)
            {
                Log.W("保存 themes.json 失败：" + ex.Message);
            }
        }

        public static Dictionary<string, object> ThemeToMap(ThemeDefinition t)
        {
            var d = new Dictionary<string, object>();
            d["id"] = t.Id;
            d["name"] = t.Name;
            d["description"] = t.Description ?? "";
            d["kind"] = t.Kind;
            d["builtin"] = t.Builtin;
            d["introPath"] = t.IntroPath ?? "";
            d["loopPath"] = t.LoopPath ?? "";
            d["imagePath"] = t.ImagePath ?? "";
            d["previewPath"] = t.PreviewPath ?? "";
            d["fontPath"] = t.FontPath ?? "";
            d["fadeStartSeconds"] = Math.Round(t.FadeStartSeconds, 3);
            d["fadeDurationSeconds"] = Math.Round(t.FadeDurationSeconds, 3);
            d["showSubtitles"] = t.ShowSubtitles;
            d["subtitleFadeSeconds"] = Math.Round(t.SubtitleFadeSeconds, 3);
            d["subtitleWidthRatio"] = Math.Round(t.SubtitleWidthRatio, 3);
            d["accentColor"] = t.AccentColor ?? "#60A5FA";
            var subs = new List<Dictionary<string, object>>();
            if (t.Subtitles != null)
            {
                foreach (var s in t.Subtitles)
                {
                    var sd = new Dictionary<string, object>();
                    sd["text"] = s.Text ?? "";
                    sd["start"] = Math.Round(s.Start, 3);
                    sd["end"] = Math.Round(s.End, 3);
                    subs.Add(sd);
                }
            }
            d["subtitles"] = subs.ToArray();
            return d;
        }

        public static ThemeDefinition ResolveTheme(List<ThemeDefinition> themes, string themeId)
        {
            if (themes != null)
            {
                foreach (var t in themes)
                    if (string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase) && IsThemePlayable(t))
                        return t;
                foreach (var t in themes)
                    if (t.Kind == "procedural")
                        return t;
            }
            return ThemeDefinition.BuiltinStellarAurora();
        }

        public static bool IsThemePlayable(ThemeDefinition t)
        {
            if (t == null) return false;
            if (t.Kind == "procedural") return true;
            if (t.Kind == "image") return !string.IsNullOrEmpty(t.ImagePath) && File.Exists(t.ImagePath);
            if (t.Kind == "video")
            {
                bool hasIntro = !string.IsNullOrEmpty(t.IntroPath) && File.Exists(t.IntroPath);
                bool hasLoop = !string.IsNullOrEmpty(t.LoopPath) && File.Exists(t.LoopPath);
                return hasIntro || hasLoop;
            }
            return false;
        }

        public static string PrettyJson(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inQuotes = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\')) inQuotes = !inQuotes;
                if (!inQuotes)
                {
                    if (ch == '{' || ch == '[')
                    {
                        sb.Append(ch);
                        if (i + 1 < json.Length && json[i + 1] != '}' && json[i + 1] != ']')
                        {
                            sb.Append("\n");
                            indent++;
                            sb.Append(new string(' ', indent * 2));
                        }
                    }
                    else if (ch == '}' || ch == ']')
                    {
                        if (i > 0 && json[i - 1] != '{' && json[i - 1] != '[')
                        {
                            sb.Append("\n");
                            indent = Math.Max(0, indent - 1);
                            sb.Append(new string(' ', indent * 2));
                        }
                        sb.Append(ch);
                    }
                    else if (ch == ',')
                    {
                        sb.Append(",\n");
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (ch == ':')
                    {
                        sb.Append(": ");
                    }
                    else if (!char.IsWhiteSpace(ch))
                    {
                        sb.Append(ch);
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            sb.Append("\n");
            return sb.ToString();
        }
    }
}
