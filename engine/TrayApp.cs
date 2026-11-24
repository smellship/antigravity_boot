using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace AntigravityBootOverlay
{
    public class TrayApp : Application
    {
        readonly string[] args;
        public AppConfig Config;
        public List<ThemeDefinition> Themes;

        OverlayStage stage;
        ControlServer server;
        WF.NotifyIcon tray;
        WF.ToolStripMenuItem introItem, loopItem, stopItem;
        DispatcherTimer watchTimer;
        DispatcherTimer parentTimer;

        readonly HashSet<uint> seenPids = new HashSet<uint>();
        readonly Random rng = new Random();

        bool managedMode;
        int parentPid;
        bool autoPlayOnStart;
        bool selfTestMode;
        bool noTrayFlag;
        string testTitle;

        public TrayApp(string[] args)
        {
            this.args = args;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        bool Has(string name) { return Array.IndexOf(args, name) >= 0; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string dd = Arg("--data-dir");
            if (!string.IsNullOrEmpty(dd)) AppPaths.SetDataDir(dd);

            managedMode = Has("--managed");
            autoPlayOnStart = Has("--auto-play-on-start");
            selfTestMode = Has("--self-test");
            noTrayFlag = Has("--no-tray");
            testTitle = Arg("--target-title");
            string pp = Arg("--parent-pid");
            if (!string.IsNullOrEmpty(pp)) int.TryParse(pp, out parentPid);

            Config = ConfigStore.LoadConfig();
            Themes = ConfigStore.LoadThemes();

            if (selfTestMode)
            {
                RunSelfTest();
                return;
            }

            Log.W("Overlay 引擎启动：" + string.Join(" ", args) + " | 数据目录=" + AppPaths.DataDir);

            server = new ControlServer(this, Dispatcher, managedMode);
            try { server.Start(); }
            catch (Exception ex)
            {
                Log.W("ControlServer 启动失败：" + ex.Message);
            }

            UpdateTrayVisibility();

            // 如果启用极速自启且没有 --auto-play-on-start，开机前已在跑的进程记入 seenPids
            var initialWin = WindowFinder.FindBest(Config, testTitle);
            if (initialWin.Valid)
            {
                if (autoPlayOnStart && (Config.PlayIntro || Config.LoopBackground))
                {
                    seenPids.Add(initialWin.Pid);
                    StartStage(initialWin, PickBootTheme(), Config.PlayIntro);
                }
                else if (string.IsNullOrEmpty(testTitle))
                {
                    seenPids.Add(initialWin.Pid);
                }
                else
                {
                    StartStage(initialWin, PickBootTheme(), Config.PlayIntro);
                }
            }

            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            watchTimer.Tick += delegate { WatchTargetWindows(); };
            watchTimer.Start();

            if (managedMode && parentPid > 0)
            {
                parentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                parentTimer.Tick += delegate { CheckParentAlive(); };
                parentTimer.Start();
            }
        }

        void CheckParentAlive()
        {
            try
            {
                var p = Process.GetProcessById(parentPid);
                if (p.HasExited)
                {
                    Log.W("宿主 Sidecar 进程 (" + parentPid + ") 已退出，托管模式优雅关闭");
                    ShutdownApp();
                }
            }
            catch
            {
                Log.W("宿主 Sidecar 进程 (" + parentPid + ") 不复存在，托管模式优雅关闭");
                ShutdownApp();
            }
        }

        void WatchTargetWindows()
        {
            var t = WindowFinder.FindBest(Config, testTitle);
            if (!t.Valid || seenPids.Contains(t.Pid)) return;
            seenPids.Add(t.Pid);
            Config = ConfigStore.LoadConfig();
            Themes = ConfigStore.LoadThemes();
            Log.W("检测到新 Antigravity 实例 pid=" + t.Pid + " title=" + t.Title);
            if (!Config.PlayIntro && !Config.LoopBackground) return;
            StartStage(t, PickBootTheme(), Config.PlayIntro);
        }

        ThemeDefinition PickBootTheme()
        {
            if (Config.ShuffleOnBoot && Themes != null)
            {
                var playable = new List<ThemeDefinition>();
                for (int i = 0; i < Themes.Count; i++)
                {
                    if (ConfigStore.IsThemePlayable(Themes[i])) playable.Add(Themes[i]);
                }
                if (playable.Count > 1) return playable[rng.Next(playable.Count)];
            }
            return ConfigStore.ResolveTheme(Themes, Config.ActiveThemeId);
        }

        void StartStage(WindowInfo target, ThemeDefinition theme, bool withIntro)
        {
            if (stage != null) stage.Teardown();
            OverlayStage created = null;
            created = new OverlayStage(target, theme, Config, withIntro, delegate
            {
                if (stage == created) stage = null;
                RefreshTrayMenu();
            });
            created.OnReplayRequested = delegate { HandlePlay(true, Config.ActiveThemeId, target.Hwnd); };
            created.OnNextThemeRequested = delegate { CycleNextTheme(); };
            created.OnToggleModeRequested = delegate { ToggleDisplayMode(); };
            created.OnGeometryCommitted = delegate(AppConfig updated) { ConfigStore.SaveConfig(updated); };
            stage = created;
            RefreshTrayMenu();
        }

        public Dictionary<string, object> CycleNextTheme()
        {
            Themes = ConfigStore.LoadThemes();
            var playable = new List<ThemeDefinition>();
            for (int i = 0; i < Themes.Count; i++)
            {
                if (ConfigStore.IsThemePlayable(Themes[i])) playable.Add(Themes[i]);
            }
            if (playable.Count == 0) playable.Add(ThemeDefinition.BuiltinStellarAurora());

            int curIdx = -1;
            for (int i = 0; i < playable.Count; i++)
            {
                if (string.Equals(playable[i].Id, Config.ActiveThemeId, StringComparison.OrdinalIgnoreCase)) { curIdx = i; break; }
            }
            var next = playable[(curIdx + 1) % playable.Count];
            Config.ActiveThemeId = next.Id;
            ConfigStore.SaveConfig(Config);

            var win = stage != null ? stage.Target : WindowFinder.FindBest(Config, testTitle);
            if (win != null && win.Valid)
            {
                bool introOnlyVideo = next.Kind == "video" && (string.IsNullOrEmpty(next.LoopPath) || !File.Exists(next.LoopPath));
                StartStage(win, next, introOnlyVideo);
            }
            return GetStatusMap();
        }

        public void ToggleDisplayMode()
        {
            Config.DisplayMode = (Config.DisplayMode == "fullscreen") ? "widget" : "fullscreen";
            Config.Clamp();
            ConfigStore.SaveConfig(Config);
            if (stage != null) stage.ApplyConfig(Config, true);
        }

        public Dictionary<string, object> ResetWidgetLayout()
        {
            Config.DisplayMode = "widget";
            Config.WidgetAnchor = "bottom-left";
            Config.WidgetOffsetX = 12;
            Config.WidgetOffsetY = 52;
            Config.WidgetWidth = 234;
            Config.WidgetHeight = 132;
            Config.WidgetCornerRadius = 12;
            Config.Clamp();
            ConfigStore.SaveConfig(Config);
            if (stage != null) stage.ApplyConfig(Config, true);
            return GetStatusMap();
        }

        public Dictionary<string, object> HandlePlay(bool withIntro, string themeId, long explicitHwnd)
        {
            ReloadAllFromDisk();
            WindowInfo target = null;
            var all = WindowFinder.FindAll(Config.TargetProcesses, testTitle);
            if (explicitHwnd != 0)
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Hwnd == explicitHwnd) { target = all[i]; break; }
            }
            if (target == null) target = WindowFinder.FindBest(Config, testTitle);

            var res = new Dictionary<string, object>();
            if (target == null || !target.Valid)
            {
                res["ok"] = false;
                res["error"] = "未检测到可见的 Antigravity 主窗口";
                return res;
            }
            seenPids.Add(target.Pid);
            string id = !string.IsNullOrEmpty(themeId) ? themeId : Config.ActiveThemeId;
            var theme = ConfigStore.ResolveTheme(Themes, id);
            StartStage(target, theme, withIntro);

            res["ok"] = true;
            res["themeId"] = theme.Id;
            res["targetHwnd"] = target.Hwnd;
            return res;
        }

        public void SkipCurrent()
        {
            if (stage != null) stage.SkipIntro();
        }

        public void StopCurrent(bool fast)
        {
            if (stage != null) stage.Finish(fast);
        }

        public void ReloadAllFromDisk()
        {
            string prevThemeId = Config != null ? Config.ActiveThemeId : "";
            Config = ConfigStore.LoadConfig();
            Themes = ConfigStore.LoadThemes();
            if (stage != null)
            {
                stage.ApplyConfig(Config, false);
                if (!string.Equals(prevThemeId, Config.ActiveThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.W("主题已切换为 " + Config.ActiveThemeId + "，重新应用浮层");
                    var t = stage.Target;
                    var theme = ConfigStore.ResolveTheme(Themes, Config.ActiveThemeId);
                    bool keepLoopOnly = !Config.PlayIntro;
                    StartStage(t, theme, !keepLoopOnly && false); // 切主题默认无缝按新循环背景展示，或等用户点播开屏
                }
            }
            UpdateTrayVisibility();
            RefreshTrayMenu();
        }

        public Dictionary<string, object> ApplyLivePatch(Dictionary<string, object> patch)
        {
            string oldThemeId = Config.ActiveThemeId;
            object v;
            if (patch.TryGetValue("activeThemeId", out v) && v != null) Config.ActiveThemeId = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("shuffleOnBoot", out v) && v is bool) Config.ShuffleOnBoot = (bool)v;
            if (patch.TryGetValue("playIntro", out v) && v is bool) Config.PlayIntro = (bool)v;
            if (patch.TryGetValue("loopBackground", out v) && v is bool) Config.LoopBackground = (bool)v;
            if (patch.TryGetValue("residualAlpha", out v) && v != null) Config.ResidualAlpha = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("introVolume", out v) && v != null) Config.IntroVolume = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("stretchMode", out v) && v != null) Config.StretchMode = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("focusDimEnabled", out v) && v is bool) Config.FocusDimEnabled = (bool)v;
            if (patch.TryGetValue("focusDimFactor", out v) && v != null) Config.FocusDimFactor = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("focusIdleSeconds", out v) && v != null) Config.FocusIdleSeconds = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("pauseOnBatterySaver", out v) && v is bool) Config.PauseOnBatterySaver = (bool)v;
            if (patch.TryGetValue("showTrayIcon", out v) && v is bool) Config.ShowTrayIcon = (bool)v;
            if (patch.TryGetValue("lockedWindowHwnd", out v) && v != null) Config.LockedWindowHwnd = Convert.ToInt64(v, CultureInfo.InvariantCulture);

            if (patch.TryGetValue("displayMode", out v) && v != null) Config.DisplayMode = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetAnchor", out v) && v != null) Config.WidgetAnchor = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetOffsetX", out v) && v != null) Config.WidgetOffsetX = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetOffsetY", out v) && v != null) Config.WidgetOffsetY = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetWidth", out v) && v != null) Config.WidgetWidth = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetHeight", out v) && v != null) Config.WidgetHeight = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetCornerRadius", out v) && v != null) Config.WidgetCornerRadius = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetOpacity", out v) && v != null) Config.WidgetOpacity = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (patch.TryGetValue("widgetClickThrough", out v) && v is bool) Config.WidgetClickThrough = (bool)v;

            Config.Clamp();
            ConfigStore.SaveConfig(Config);
            Themes = ConfigStore.LoadThemes();

            if (stage != null)
            {
                bool sliderTouch = patch.ContainsKey("residualAlpha") || patch.ContainsKey("introVolume") || patch.ContainsKey("widgetOpacity");
                stage.ApplyConfig(Config, sliderTouch);
                if (!string.Equals(oldThemeId, Config.ActiveThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    var win = WindowFinder.FindBest(Config, testTitle);
                    if (win.Valid) StartStage(win, ConfigStore.ResolveTheme(Themes, Config.ActiveThemeId), false);
                }
            }

            UpdateTrayVisibility();
            RefreshTrayMenu();
            return GetStatusMap();
        }

        public List<Dictionary<string, object>> ListWindowsMaps()
        {
            var list = new List<Dictionary<string, object>>();
            var wins = WindowFinder.FindAll(Config.TargetProcesses, testTitle);
            for (int i = 0; i < wins.Count; i++) list.Add(wins[i].ToMap());
            return list;
        }

        public Dictionary<string, object> GetStatusMap()
        {
            var res = new Dictionary<string, object>();
            res["ok"] = true;
            res["pid"] = Process.GetCurrentProcess().Id;
            res["managed"] = managedMode;
            res["config"] = ConfigStore.ConfigToMap(Config);
            var activeTheme = ConfigStore.ResolveTheme(Themes, Config.ActiveThemeId);
            res["activeTheme"] = ConfigStore.ThemeToMap(activeTheme);
            res["windows"] = ListWindowsMaps();

            if (stage != null)
            {
                var snap = stage.GetSnapshot();
                var sd = new Dictionary<string, object>();
                sd["active"] = true;
                sd["phase"] = snap.Phase;
                sd["displayMode"] = snap.DisplayMode;
                sd["themeId"] = snap.ThemeId;
                sd["themeName"] = snap.ThemeName;
                sd["kind"] = snap.Kind;
                sd["targetHwnd"] = snap.TargetHwnd;
                sd["targetTitle"] = snap.TargetTitle;
                sd["targetSize"] = snap.TargetSize;
                sd["laps"] = snap.Laps;
                sd["recoveries"] = snap.Recoveries;
                sd["effectiveAlpha"] = snap.EffectiveAlpha;
                sd["pausedReason"] = snap.PausedReason;
                sd["memoryMb"] = snap.MemoryMb;
                res["stage"] = sd;
            }
            else
            {
                var sd = new Dictionary<string, object>();
                sd["active"] = false;
                sd["phase"] = "idle";
                sd["displayMode"] = Config.DisplayMode;
                sd["themeId"] = activeTheme.Id;
                sd["themeName"] = activeTheme.Name;
                sd["kind"] = activeTheme.Kind;
                sd["laps"] = 0;
                sd["recoveries"] = 0;
                sd["effectiveAlpha"] = 0.0;
                sd["pausedReason"] = "";
                try { using (var pr = Process.GetCurrentProcess()) sd["memoryMb"] = pr.WorkingSet64 / (1024 * 1024); }
                catch { sd["memoryMb"] = 0; }
                res["stage"] = sd;
            }

            res["logTail"] = Log.ReadTail(40).ToArray();
            return res;
        }

        void UpdateTrayVisibility()
        {
            bool shouldShow = !noTrayFlag && !selfTestMode && Config.ShowTrayIcon;
            if (shouldShow && tray == null)
            {
                SetupTray();
            }
            else if (!shouldShow && tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
        }

        static SD.Icon MakeStarIcon()
        {
            var bmp = new SD.Bitmap(32, 32);
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                var pts = new[]
                {
                    new SD.PointF(16, 2), new SD.PointF(19.5f, 12.5f), new SD.PointF(30, 16), new SD.PointF(19.5f, 19.5f),
                    new SD.PointF(16, 30), new SD.PointF(12.5f, 19.5f), new SD.PointF(2, 16), new SD.PointF(12.5f, 12.5f)
                };
                using (var br = new SD.Drawing2D.LinearGradientBrush(new SD.Point(2, 2), new SD.Point(30, 30),
                    SD.Color.FromArgb(255, 125, 211, 252), SD.Color.FromArgb(255, 168, 85, 247)))
                {
                    g.FillPolygon(br, pts);
                }
            }
            return SD.Icon.FromHandle(bmp.GetHicon());
        }

        void SetupTray()
        {
            tray = new WF.NotifyIcon { Icon = MakeStarIcon(), Text = "Antigravity Boot Studio", Visible = true };
            var m = new WF.ContextMenuStrip();
            m.Items.Add(new WF.ToolStripLabel("Antigravity 启动时") { Enabled = false });
            introItem = new WF.ToolStripMenuItem("播放开屏动画");
            introItem.Click += delegate { Config.PlayIntro = !Config.PlayIntro; ConfigStore.SaveConfig(Config); RefreshTrayMenu(); };
            loopItem = new WF.ToolStripMenuItem("保留动态背景");
            loopItem.Click += delegate { Config.LoopBackground = !Config.LoopBackground; ConfigStore.SaveConfig(Config); RefreshTrayMenu(); };
            m.Items.Add(introItem);
            m.Items.Add(loopItem);
            m.Items.Add(new WF.ToolStripSeparator());
            m.Items.Add("播放一次开屏动画", null, delegate { HandlePlay(true, null, 0); });
            m.Items.Add("开启循环背景", null, delegate { HandlePlay(false, null, 0); });
            stopItem = new WF.ToolStripMenuItem("关掉动画 / 背景");
            stopItem.Click += delegate { StopCurrent(true); };
            m.Items.Add(stopItem);
            m.Items.Add(new WF.ToolStripSeparator());
            m.Items.Add("隐藏托盘图标（仍可在侧边栏控制）", null, delegate { Config.ShowTrayIcon = false; ConfigStore.SaveConfig(Config); UpdateTrayVisibility(); });
            m.Items.Add("退出浮层引擎", null, delegate { ShutdownApp(); });
            m.Opening += delegate { Config = ConfigStore.LoadConfig(); RefreshTrayMenu(); };
            tray.ContextMenuStrip = m;
            RefreshTrayMenu();
        }

        void RefreshTrayMenu()
        {
            if (tray == null) return;
            if (introItem != null) introItem.Checked = Config.PlayIntro;
            if (loopItem != null) loopItem.Checked = Config.LoopBackground;
            if (stopItem != null) stopItem.Enabled = stage != null;
        }

        public void ShutdownApp()
        {
            if (watchTimer != null) watchTimer.Stop();
            if (parentTimer != null) parentTimer.Stop();
            if (stage != null) stage.Teardown();
            if (server != null) server.Stop();
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            Shutdown(0);
        }

        void RunSelfTest()
        {
            int exitCode = 1;
            var report = new Dictionary<string, object>();
            try
            {
                // 1. Config & Themes persistence check
                ConfigStore.SaveConfig(Config);
                var cfg2 = ConfigStore.LoadConfig();
                var th2 = ConfigStore.LoadThemes();

                // 2. Offscreen procedural rendering check
                string pngPath = Path.Combine(AppPaths.DataDir, "selftest_procedural.png");
                ProceduralStage.RenderPreviewPng(pngPath, 640, 360, 2.5);
                var fi = new FileInfo(pngPath);
                bool pngOk = fi.Exists && fi.Length > 5000;

                // 3. ControlServer loopback round-trip check (run HTTP calls off UI thread so Dispatcher.Invoke responds)
                var srv = new ControlServer(this, Dispatcher, true);
                string healthJson = "";
                string statusJson = "";
                Exception bgErr = null;
                srv.Start();
                try
                {
                    var frame = new DispatcherFrame();
                    var bg = new Thread(delegate()
                    {
                        try
                        {
                            using (var wc = new WebClient())
                            {
                                wc.Proxy = null;
                                wc.Encoding = Encoding.UTF8;
                                healthJson = wc.DownloadString("http://127.0.0.1:" + srv.Port + "/health");
                            }
                            using (var wc = new WebClient())
                            {
                                wc.Proxy = null;
                                wc.Encoding = Encoding.UTF8;
                                wc.Headers.Add("Authorization", "Bearer " + srv.Token);
                                statusJson = wc.DownloadString("http://127.0.0.1:" + srv.Port + "/status");
                            }
                        }
                        catch (Exception ex) { bgErr = ex; }
                        finally
                        {
                            Dispatcher.BeginInvoke(new Action(delegate { frame.Continue = false; }));
                        }
                    }) { IsBackground = true };
                    bg.Start();
                    Dispatcher.PushFrame(frame);
                    if (bgErr != null) throw bgErr;
                }
                finally
                {
                    srv.Stop();
                }

                bool ipcOk = healthJson.Contains("\"ok\":true") && statusJson.Contains("stellar-aurora");
                var simWin = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
                var wRect = OverlayStage.ComputeWidgetRect(simWin, cfg2, 1.0);
                bool widgetOk = wRect.Left == 12 && (wRect.Right - wRect.Left) == 234 && (wRect.Bottom - wRect.Top) == 132 && wRect.Bottom == 1028;
                bool allOk = pngOk && ipcOk && widgetOk && th2.Count >= 1 && cfg2 != null;

                report["ok"] = allOk;
                report["pngBytes"] = fi.Exists ? fi.Length : 0;
                report["ipcOk"] = ipcOk;
                report["widgetOk"] = widgetOk;
                report["widgetRect"] = wRect.Left + "," + wRect.Top + "," + wRect.Right + "," + wRect.Bottom;
                report["themeCount"] = th2.Count;
                report["message"] = allOk ? "SELF_TEST_OK" : "SELF_TEST_FAILED";

                Log.W("SELF_TEST_OK pngBytes=" + (fi.Exists ? fi.Length : 0) + " widgetOk=" + widgetOk + " themes=" + th2.Count);
                exitCode = allOk ? 0 : 1;
            }
            catch (Exception ex)
            {
                report["ok"] = false;
                report["error"] = ex.ToString();
                Log.W("SELF_TEST_FAILED: " + ex.ToString());
                exitCode = 1;
            }

            try
            {
                var js = new JavaScriptSerializer();
                ConfigStore.AtomicWriteText(AppPaths.SelfTestPath, ConfigStore.PrettyJson(js.Serialize(report)));
            }
            catch { }

            Shutdown(exitCode);
        }
    }
}
