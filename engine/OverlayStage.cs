using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AntigravityBootOverlay
{
    public class SubtitleElement : FrameworkElement
    {
        public FontFamily SubFont;
        string text;
        public double WidthRatio;
        public string RefText;

        public SubtitleElement()
        {
            SubFont = new FontFamily("Georgia");
            WidthRatio = 0.6;
            RefText = "Twin stars aligned · Ready to build.";
            IsHitTestVisible = false;
            Opacity = 0;
        }

        public void LoadCustomFont(string fontPath)
        {
            if (string.IsNullOrEmpty(fontPath) || !File.Exists(fontPath)) return;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(fontPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    SubFont = new FontFamily(new Uri(dir.TrimEnd('\\') + "\\"), "./#Libre Bodoni");
                }
            }
            catch (Exception ex)
            {
                Log.W("加载字幕字体失败，回退 Georgia：" + ex.Message);
            }
        }

        public void SetText(string t)
        {
            text = t;
            InvalidateVisual();
        }

        FormattedText Make(string s, double size)
        {
#pragma warning disable 618
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(SubFont, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal), size, Brushes.White);
#pragma warning restore 618
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(text) || ActualWidth < 10) return;
            double w = ActualWidth;
            double h = ActualHeight;
            double refW = Make(RefText, 100).WidthIncludingTrailingWhitespace;
            double size = refW > 0 ? w * WidthRatio * 100.0 / refW : 24.0;
            size = Math.Max(11.0, Math.Min(size, h * 0.095));
            var ft = Make(text, size);
            double x = (w - ft.Width) / 2.0;
            double y = h * 0.85 - ft.Height / 2.0;
            var geo = ft.BuildGeometry(new Point(x, y));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)), size * 0.1) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
            dc.DrawGeometry(Brushes.White, null, geo);
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = size * 0.25, ShadowDepth = 0, Opacity = 0.75 };
        }
    }

    public class StageSnapshot
    {
        public string Phase;
        public string DisplayMode;
        public string ThemeId;
        public string ThemeName;
        public string Kind;
        public long TargetHwnd;
        public string TargetTitle;
        public string TargetSize;
        public int Laps;
        public int Recoveries;
        public double EffectiveAlpha;
        public string PausedReason;
        public long MemoryMb;
    }

    public class OverlayStage
    {
        public enum PlayPhase { Intro, Revealed, Ending, Done }

        PlayPhase phase = PlayPhase.Intro;
        public readonly WindowInfo Target;
        public ThemeDefinition Theme;
        AppConfig config;
        readonly bool withIntro;
        readonly Action onDone;

        public Action OnReplayRequested;
        public Action OnNextThemeRequested;
        public Action OnToggleModeRequested;
        public Action<AppConfig> OnGeometryCommitted;

        Window overlay;
        IntPtr overlayH;
        Grid root, innerHost, mediaHost, contentGrid;
        Border cardFrame, skipBtn, hoverHud, resizeGrip;
        int styleFixes;

        // Video elements
        MediaElement introMedia, loopA, loopB, activeLoop;
        MediaElement swapFrom, swapTo;
        DateTime swapT0;
        bool swapKicked;
        MediaElement retiring;
        long retireT0;
        bool hasVideoIntro;
        bool hasVideoLoop;

        // Procedural & image elements
        ProceduralStage procStage;
        Image imgView;
        ScaleTransform imgScale;

        // Watchdog & power events
        readonly Stopwatch clock = Stopwatch.StartNew();
        long nextSampleMs, lastSampleMs, lastMoveMs, lastRecoverMs = -1, pendingAt;
        long nextPowerCheckMs, nextProcRenderMs;
        double lastPos = -1;
        int laps, recoveries, failStreak, missedEnds;
        bool verifying;
        string pendingWhy;
        Dispatcher disp;
        SessionSwitchEventHandler onSession;
        EventHandler onDisplay;
        IntPtr powerNotify;
        int displayState = -1;

        // UI & fades
        SubtitleElement sub;
        DispatcherTimer tick;
        int currentSub = -1;
        bool introGone, hidden, batteryPaused;
        string pausedReason = "";

        Native.RECT lastTargetBounds;
        Native.RECT currentOverlayRect;
        double currentCornerRadius;

        // Smooth morph state
        bool isMorphing;
        bool morphToDocked;
        Native.RECT morphFromRect;
        double morphFromCorner;
        double morphToCorner;
        DateTime morphT0;
        double morphDur = 0.85;

        // Drag / resize state
        bool isDragging;
        string dragMode;
        Native.POINT dragStartCursor;
        Native.RECT dragStartRect;

        double alpha, alphaFrom, alphaTo;
        DateTime alphaT0;
        double alphaDur = -1;
        Action alphaThen;
        bool clickThrough;

        double volFrom, volTo;
        DateTime volT0;
        double volDur = -1;

        DateTime sliderBoostUntil = DateTime.MinValue;
        long elapsedBaseMs;
        DateTime? pausedClockAt;

        static readonly SolidColorBrush InteractiveHitBrush = CreateHitBrush();
        static readonly SolidColorBrush CardBorderBrush = CreateCardBorderBrush();

        static SolidColorBrush CreateHitBrush()
        {
            var b = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            b.Freeze();
            return b;
        }

        static SolidColorBrush CreateCardBorderBrush()
        {
            var b = new SolidColorBrush(Color.FromArgb(105, 148, 163, 184));
            b.Freeze();
            return b;
        }

        public static Native.RECT ComputeWidgetRect(Native.RECT targetRect, AppConfig cfg, double dpiScale)
        {
            int tw = Math.Max(180, targetRect.Right - targetRect.Left);
            int th = Math.Max(140, targetRect.Bottom - targetRect.Top);
            int w = Math.Max(120, Math.Min(tw - 16, (int)Math.Round(cfg.WidgetWidth * dpiScale)));
            int h = Math.Max(72, Math.Min(th - 16, (int)Math.Round(cfg.WidgetHeight * dpiScale)));
            int ox = (int)Math.Round(cfg.WidgetOffsetX * dpiScale);
            int oy = (int)Math.Round(cfg.WidgetOffsetY * dpiScale);

            int left = targetRect.Left + ox;
            int top = targetRect.Bottom - oy - h;
            string anchor = cfg.WidgetAnchor ?? "bottom-left";
            if (anchor == "bottom-right")
            {
                left = targetRect.Right - ox - w;
                top = targetRect.Bottom - oy - h;
            }
            else if (anchor == "top-left")
            {
                left = targetRect.Left + ox;
                top = targetRect.Top + oy;
            }
            else if (anchor == "top-right")
            {
                left = targetRect.Right - ox - w;
                top = targetRect.Top + oy;
            }

            left = Math.Max(targetRect.Left + 4, Math.Min(targetRect.Right - w - 4, left));
            top = Math.Max(targetRect.Top + 4, Math.Min(targetRect.Bottom - h - 4, top));

            var res = new Native.RECT();
            res.Left = left;
            res.Top = top;
            res.Right = left + w;
            res.Bottom = top + h;
            return res;
        }

        bool IsDockedNow()
        {
            if (config.DisplayMode == "fullscreen") return false;
            if (config.DisplayMode == "widget-always") return true;
            return phase != PlayPhase.Intro || isMorphing && morphToDocked;
        }

        bool IsInteractiveDocked()
        {
            return IsDockedNow() && !config.WidgetClickThrough && phase != PlayPhase.Ending && phase != PlayPhase.Done;
        }

        public OverlayStage(WindowInfo target, ThemeDefinition theme, AppConfig cfg, bool playIntroMode, Action onDoneCallback)
        {
            Target = target;
            Theme = theme ?? ThemeDefinition.BuiltinStellarAurora();
            config = cfg;
            withIntro = playIntroMode;
            onDone = onDoneCallback;

            contentGrid = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
            mediaHost = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
            mediaHost.Children.Add(contentGrid);
            mediaHost.SizeChanged += delegate { UpdateCardClip(currentCornerRadius); };

            innerHost = new Grid { Background = Brushes.Transparent };
            innerHost.Children.Add(mediaHost);

            cardFrame = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Child = innerHost
            };

            root = new Grid { Background = Brushes.Transparent };
            root.Children.Add(cardFrame);

            SetupVisualLayer();
            SetupControls();

            overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = Brushes.Transparent,
                Content = root,
                Left = -32000,
                Top = -32000,
                Width = 10,
                Height = 10,
                Title = "Antigravity Boot Studio Overlay"
            };

            bool startDocked = (config.DisplayMode == "widget-always") || (!withIntro && config.DisplayMode == "widget");
            if (startDocked)
            {
                currentCornerRadius = config.WidgetCornerRadius;
                clickThrough = config.WidgetClickThrough;
                alpha = withIntro ? 1.0 : 0.0;
            }
            else
            {
                currentCornerRadius = 0;
                clickThrough = !withIntro;
                alpha = withIntro ? 1.0 : 0.0;
            }
            contentGrid.Opacity = alpha;

            overlay.SourceInitialized += delegate
            {
                overlayH = new WindowInteropHelper(overlay).Handle;
                EnsureOverlayStyle(true);
                var hs = HwndSource.FromHwnd(overlayH);
                if (hs != null) hs.AddHook(WndProc);
                try
                {
                    Guid g = Native.GUID_CONSOLE_DISPLAY_STATE;
                    powerNotify = Native.RegisterPowerSettingNotification(overlayH, ref g, 0);
                }
                catch (Exception ex) { Log.W("注册显示器电源通知失败：" + ex.Message); }
                Native.SetLong(overlayH, Native.GWLP_HWNDPARENT, Target.Handle.ToInt64());
            };

            overlay.Show();
            EnsureOverlayStyle(true);
            UpdateDockDecorations();
            Follow(true);
            UpdateCardClip(currentCornerRadius);

            StartPlayback();

            disp = Dispatcher.CurrentDispatcher;
            onSession = delegate(object s, SessionSwitchEventArgs e)
            {
                var r = e.Reason;
                if (r == SessionSwitchReason.SessionUnlock || r == SessionSwitchReason.ConsoleConnect || r == SessionSwitchReason.RemoteConnect)
                    disp.BeginInvoke(new Action(delegate { LaterRebuild("解锁 / 切回桌面（" + r + "）", 2000); }));
            };
            onDisplay = delegate
            {
                disp.BeginInvoke(new Action(delegate { LaterRebuild("显示器配置变化", 1500); }));
            };
            try
            {
                SystemEvents.SessionSwitch += onSession;
                SystemEvents.DisplaySettingsChanged += onDisplay;
            }
            catch (Exception ex) { Log.W("订阅系统事件失败：" + ex.Message); }

            tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            tick.Tick += delegate { Tick(); };
            tick.Start();
        }

        void SetupControls()
        {
            if (withIntro)
            {
                sub = new SubtitleElement { WidthRatio = Theme.SubtitleWidthRatio };
                if (!string.IsNullOrEmpty(Theme.FontPath)) sub.LoadCustomFont(Theme.FontPath);
                if (Theme.Subtitles != null && Theme.Subtitles.Count > 0) sub.RefText = Theme.Subtitles[0].Text;
                innerHost.Children.Add(sub);

                bool compactSkip = (config.DisplayMode == "widget-always");
                skipBtn = new Border
                {
                    Width = compactSkip ? 58 : 76,
                    Height = compactSkip ? 22 : 28,
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(145, 12, 16, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(115, 186, 230, 253)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = compactSkip ? new Thickness(0, 8, 8, 0) : new Thickness(0, 44, 18, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "跳过 ›",
                        Foreground = Brushes.White,
                        FontSize = compactSkip ? 11 : 13,
                        FontWeight = FontWeights.Medium,
                        FontFamily = new FontFamily("Microsoft YaHei UI"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                skipBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
                skipBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; SkipIntro(); };
                innerHost.Children.Add(skipBtn);
            }

            // 悬停快捷播控栏（仅挂件交互模式可见）
            var hudPanel = new StackPanel { Orientation = Orientation.Horizontal };
            hudPanel.Children.Add(MakeHudButton("▶", "重播开屏动画", delegate { if (OnReplayRequested != null) OnReplayRequested(); }));
            hudPanel.Children.Add(MakeHudButton("⤢", "切换 挂件 / 全屏模式", delegate { if (OnToggleModeRequested != null) OnToggleModeRequested(); }));
            hudPanel.Children.Add(MakeHudButton("⏭", "切换下一个主题", delegate { if (OnNextThemeRequested != null) OnNextThemeRequested(); }));
            hudPanel.Children.Add(MakeHudButton("✕", "关闭浮层", delegate { Finish(true); }));

            hoverHud = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(195, 10, 15, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(95, 186, 230, 253)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 3, 5, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8),
                Opacity = 0,
                Visibility = Visibility.Collapsed,
                Child = hudPanel
            };
            hoverHud.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            innerHost.Children.Add(hoverHud);

            // 右下角缩放手柄
            var gripPath = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 11 3 L 11 11 L 3 11 Z"),
                Fill = new SolidColorBrush(Color.FromArgb(185, 226, 232, 240)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 4)
            };
            resizeGrip = new Border
            {
                Width = 18,
                Height = 18,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                ToolTip = "拖动调整挂件大小（按住 Shift 锁定 16:9）",
                Opacity = 0,
                Visibility = Visibility.Collapsed,
                Child = gripPath
            };
            resizeGrip.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (!IsInteractiveDocked()) return;
                e.Handled = true;
                StartDrag("resize", resizeGrip);
            };
            innerHost.Children.Add(resizeGrip);

            // 鼠标悬停显示 HUD 与拖动挂件位置
            cardFrame.MouseEnter += delegate { SetHudVisible(true); };
            cardFrame.MouseLeave += delegate { if (!isDragging) SetHudVisible(false); };
            cardFrame.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (!IsInteractiveDocked() || e.Handled) return;
                StartDrag("move", cardFrame);
            };
        }

        Border MakeHudButton(string glyph, string tip, Action onClick)
        {
            var tb = new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.White,
                FontSize = 11.5,
                FontFamily = new FontFamily("Segoe UI Symbol, Microsoft YaHei UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Border
            {
                Width = 24,
                Height = 20,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Margin = new Thickness(1, 0, 1, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = tb
            };
            b.MouseEnter += delegate { b.Background = new SolidColorBrush(Color.FromArgb(95, 96, 165, 250)); };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; onClick(); };
            return b;
        }

        void SetHudVisible(bool show)
        {
            if (hoverHud == null || resizeGrip == null) return;
            if (!IsInteractiveDocked()) show = false;
            double target = show ? 1.0 : 0.0;
            var dur = TimeSpan.FromSeconds(show ? 0.14 : 0.22);
            hoverHud.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(target, dur));
            resizeGrip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(target, dur));
        }

        void UpdateDockDecorations()
        {
            bool docked = IsDockedNow();
            bool interactive = IsInteractiveDocked();
            cardFrame.Background = interactive ? InteractiveHitBrush : Brushes.Transparent;
            cardFrame.Cursor = interactive ? Cursors.SizeAll : Cursors.Arrow;
            if (hoverHud != null) hoverHud.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
            if (resizeGrip != null) resizeGrip.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
            if (procStage != null) procStage.IsDockedWidget = docked;
            UpdateCardClip(currentCornerRadius);
        }

        void UpdateCardClip(double radiusDip)
        {
            currentCornerRadius = Math.Max(0, radiusDip);
            if (cardFrame != null)
            {
                cardFrame.CornerRadius = new CornerRadius(currentCornerRadius);
                cardFrame.BorderThickness = new Thickness(currentCornerRadius > 0.5 ? 1.0 : 0.0);
            }
            if (mediaHost != null)
            {
                double w = mediaHost.ActualWidth;
                double h = mediaHost.ActualHeight;
                if (w > 2 && h > 2 && currentCornerRadius > 0.5)
                {
                    mediaHost.Clip = new RectangleGeometry(new Rect(0, 0, w, h), currentCornerRadius, currentCornerRadius);
                }
                else
                {
                    mediaHost.Clip = null;
                }
            }
        }

        void StartDrag(string mode, UIElement captureEl)
        {
            isDragging = true;
            dragMode = mode;
            Native.GetCursorPos(out dragStartCursor);
            dragStartRect = currentOverlayRect;
            captureEl.CaptureMouse();
        }

        void UpdateDrag()
        {
            if (!isDragging) return;
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                CommitDrag();
                return;
            }
            Native.POINT cur;
            Native.GetCursorPos(out cur);
            int dx = cur.X - dragStartCursor.X;
            int dy = cur.Y - dragStartCursor.Y;

            Native.RECT bounds = lastTargetBounds;
            if (bounds.Right <= bounds.Left) return;

            if (dragMode == "move")
            {
                int w = dragStartRect.Right - dragStartRect.Left;
                int h = dragStartRect.Bottom - dragStartRect.Top;
                int nx = Math.Max(bounds.Left + 4, Math.Min(bounds.Right - w - 4, dragStartRect.Left + dx));
                int ny = Math.Max(bounds.Top + 4, Math.Min(bounds.Bottom - h - 4, dragStartRect.Top + dy));
                ApplyOverlayPos(nx, ny, w, h);
            }
            else if (dragMode == "resize")
            {
                double scale = Native.GetWindowDpiScale(Target.Handle);
                int curW = dragStartRect.Right - dragStartRect.Left;
                int curH = dragStartRect.Bottom - dragStartRect.Top;
                int minW = (int)Math.Round(140 * scale);
                int maxW = Math.Max(minW, Math.Min(bounds.Right - dragStartRect.Left - 4, (int)Math.Round(720 * scale)));
                int minH = (int)Math.Round(80 * scale);
                int maxH = Math.Max(minH, Math.Min(bounds.Bottom - dragStartRect.Top - 4, (int)Math.Round(480 * scale)));
                int nw = Math.Max(minW, Math.Min(maxW, curW + dx));
                int nh = Math.Max(minH, Math.Min(maxH, curH + dy));
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    nh = Math.Max(minH, Math.Min(maxH, (int)Math.Round(nw * 9.0 / 16.0)));
                }
                ApplyOverlayPos(dragStartRect.Left, dragStartRect.Top, nw, nh);
                UpdateCardClip(currentCornerRadius);
            }
        }

        void CommitDrag()
        {
            if (!isDragging) return;
            isDragging = false;
            try { cardFrame.ReleaseMouseCapture(); } catch { }
            try { if (resizeGrip != null) resizeGrip.ReleaseMouseCapture(); } catch { }

            Native.RECT bounds = lastTargetBounds;
            if (bounds.Right <= bounds.Left || currentOverlayRect.Right <= currentOverlayRect.Left) return;

            double scale = Native.GetWindowDpiScale(Target.Handle);
            if (scale <= 0) scale = 1.0;

            int w = currentOverlayRect.Right - currentOverlayRect.Left;
            int h = currentOverlayRect.Bottom - currentOverlayRect.Top;
            double cx = (currentOverlayRect.Left + currentOverlayRect.Right) * 0.5;
            double cy = (currentOverlayRect.Top + currentOverlayRect.Bottom) * 0.5;
            double tcx = (bounds.Left + bounds.Right) * 0.5;
            double tcy = (bounds.Top + bounds.Bottom) * 0.5;

            bool rightHalf = cx > tcx;
            bool topHalf = cy < tcy;
            config.WidgetAnchor = (topHalf ? "top-" : "bottom-") + (rightHalf ? "right" : "left");
            config.WidgetOffsetX = Math.Round((rightHalf ? (bounds.Right - currentOverlayRect.Right) : (currentOverlayRect.Left - bounds.Left)) / scale, 1);
            config.WidgetOffsetY = Math.Round((topHalf ? (currentOverlayRect.Top - bounds.Top) : (bounds.Bottom - currentOverlayRect.Bottom)) / scale, 1);
            config.WidgetWidth = Math.Round(w / scale, 1);
            config.WidgetHeight = Math.Round(h / scale, 1);
            config.Clamp();

            if (OnGeometryCommitted != null) OnGeometryCommitted(config);
            Log.W("挂件位置/尺寸已保存：" + config.WidgetAnchor + " offset=(" + config.WidgetOffsetX + "," + config.WidgetOffsetY + ") size=" + config.WidgetWidth + "x" + config.WidgetHeight);
        }

        void SetupVisualLayer()
        {
            if (Theme.Kind == "procedural")
            {
                procStage = new ProceduralStage();
                procStage.FadeStartSeconds = Theme.FadeStartSeconds;
                procStage.FadeDurationSeconds = Theme.FadeDurationSeconds;
                procStage.IsLoopOnly = !withIntro;
                procStage.IsDockedWidget = IsDockedNow();
                contentGrid.Children.Add(procStage);
                return;
            }

            if (Theme.Kind == "image")
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(System.IO.Path.GetFullPath(Theme.ImagePath), UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    imgScale = new ScaleTransform(1.0, 1.0);
                    imgView = new Image
                    {
                        Source = bmp,
                        Stretch = ParseStretch(config.StretchMode),
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = imgScale
                    };
                    contentGrid.Children.Add(imgView);
                    if (withIntro) contentGrid.Background = Brushes.Black;
                }
                catch (Exception ex)
                {
                    Log.W("加载图片主题失败，回退内置星穹极光：" + ex.Message);
                    Theme = ThemeDefinition.BuiltinStellarAurora();
                    procStage = new ProceduralStage { IsLoopOnly = !withIntro, IsDockedWidget = IsDockedNow() };
                    contentGrid.Children.Add(procStage);
                }
                return;
            }

            // Video theme
            hasVideoIntro = !string.IsNullOrEmpty(Theme.IntroPath) && File.Exists(Theme.IntroPath);
            hasVideoLoop = !string.IsNullOrEmpty(Theme.LoopPath) && File.Exists(Theme.LoopPath);

            if (!hasVideoIntro && !hasVideoLoop)
            {
                Log.W("视频主题媒体文件不存在，回退内置星穹极光：" + Theme.Id);
                Theme = ThemeDefinition.BuiltinStellarAurora();
                procStage = new ProceduralStage { IsLoopOnly = !withIntro, IsDockedWidget = IsDockedNow() };
                contentGrid.Children.Add(procStage);
                return;
            }

            if (hasVideoLoop)
            {
                loopA = MakeLoopPlayer(Theme.LoopPath);
                loopB = MakeLoopPlayer(Theme.LoopPath);
                Panel.SetZIndex(loopA, 1);
                Panel.SetZIndex(loopB, 0);
                contentGrid.Children.Add(loopA);
                contentGrid.Children.Add(loopB);
                activeLoop = loopA;
            }

            if (withIntro && hasVideoIntro)
            {
                contentGrid.Background = Brushes.Black;
                introMedia = MakePlayer(Theme.IntroPath, false);
                introMedia.Volume = config.IntroVolume;
                Panel.SetZIndex(introMedia, 2);
                contentGrid.Children.Add(introMedia);
                introMedia.MediaEnded += delegate { IntroEnded(); };
                introMedia.MediaFailed += delegate(object s, ExceptionRoutedEventArgs e)
                {
                    Log.W("开屏视频播不了：" + (e.ErrorException != null ? e.ErrorException.Message : "未知错误"));
                    IntroEnded();
                };
            }
            else
            {
                introGone = true;
            }
        }

        void StartPlayback()
        {
            double revealedAlpha = IsDockedNow() ? config.WidgetOpacity : config.ResidualAlpha;
            if (Theme.Kind == "procedural" || Theme.Kind == "image")
            {
                if (withIntro)
                {
                    Log.W("开始播放开屏主题：" + Theme.Id + "（模式=" + config.DisplayMode + "），目标窗口 " + Target.Hwnd);
                }
                else
                {
                    introGone = true;
                    phase = PlayPhase.Revealed;
                    UpdateDockDecorations();
                    RampAlpha(revealedAlpha, Theme.FadeDurationSeconds, null);
                    Log.W("直接开启循环背景主题：" + Theme.Id + "（模式=" + config.DisplayMode + "），目标窗口 " + Target.Hwnd);
                }
                return;
            }

            if (withIntro && introMedia != null)
            {
                introMedia.Play();
                Log.W("开始播放视频开屏：" + Theme.Id + "（模式=" + config.DisplayMode + "），目标窗口 " + Target.Hwnd);
            }
            else if (hasVideoLoop && loopA != null)
            {
                introGone = true;
                loopA.Play();
                activeLoop = loopA;
                phase = PlayPhase.Revealed;
                UpdateDockDecorations();
                RampAlpha(revealedAlpha, Theme.FadeDurationSeconds, null);
                Log.W("直接开启视频循环背景：" + Theme.Id + "（模式=" + config.DisplayMode + "），目标窗口 " + Target.Hwnd);
            }
            else
            {
                Finish(true);
            }
        }

        static Stretch ParseStretch(string mode)
        {
            return string.Equals(mode, "Uniform", StringComparison.OrdinalIgnoreCase) ? Stretch.Uniform : Stretch.UniformToFill;
        }

        MediaElement MakePlayer(string path, bool muted)
        {
            var m = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = ParseStretch(config.StretchMode),
                IsMuted = muted,
                ScrubbingEnabled = muted,
                Source = new Uri(System.IO.Path.GetFullPath(path), UriKind.Absolute)
            };
            if (muted)
            {
                bool primed = false;
                m.MediaOpened += delegate
                {
                    if (primed) return;
                    primed = true;
                    if (m != activeLoop || (phase == PlayPhase.Intro && withIntro && hasVideoIntro))
                    {
                        m.Pause();
                        m.Position = TimeSpan.Zero;
                    }
                };
                m.Play();
            }
            return m;
        }

        MediaElement MakeLoopPlayer(string path)
        {
            var m = MakePlayer(path, true);
            m.MediaEnded += OnLoopEnded;
            m.MediaFailed += OnLoopFailed;
            return m;
        }

        MediaElement OtherLoop(MediaElement m) { return m == loopA ? loopB : loopA; }

        void OnLoopEnded(object s, RoutedEventArgs e)
        {
            var m = s as MediaElement;
            if (m == null || m != activeLoop || phase == PlayPhase.Done) return;
            SwapLoop(m, OtherLoop(m));
        }

        void OnLoopFailed(object s, ExceptionRoutedEventArgs e)
        {
            if (s != loopA && s != loopB) return;
            Log.W("循环播放器报错：" + (e.ErrorException != null ? e.ErrorException.Message : "未知"));
            LaterRebuild("播放器报错", 600);
        }

        void DisposePlayer(MediaElement m)
        {
            if (m == null) return;
            m.MediaEnded -= OnLoopEnded;
            m.MediaFailed -= OnLoopFailed;
            try { m.Stop(); m.Close(); } catch { }
            contentGrid.Children.Remove(m);
        }

        void SwapLoop(MediaElement done, MediaElement next)
        {
            if (phase == PlayPhase.Done || done == null || next == null) return;
            next.Play();
            activeLoop = next;
            BeginSwap(done, next);
        }

        void BeginSwap(MediaElement from, MediaElement to)
        {
            if (swapFrom != null && swapFrom != from) FinishSwap();
            swapFrom = from;
            swapTo = to;
            swapT0 = DateTime.Now;
            swapKicked = false;
        }

        void CheckSwap()
        {
            if (swapFrom == null || swapTo == null) return;
            if (swapTo.Position.TotalSeconds > 0.06) { FinishSwap(); return; }
            if (!swapKicked && (DateTime.Now - swapT0).TotalSeconds > 1.5)
            {
                swapKicked = true;
                Log.W("接缝：新播放器 1.5 秒还没动，再催一次（第 " + laps + " 圈）");
                swapTo.Play();
            }
        }

        void FinishSwap()
        {
            var from = swapFrom;
            var to = swapTo;
            swapFrom = null;
            swapTo = null;
            if (from == introMedia)
            {
                if (introMedia != null)
                {
                    introMedia.Visibility = Visibility.Collapsed;
                    try { introMedia.Stop(); introMedia.Close(); } catch { }
                }
                return;
            }
            if (to != null) Panel.SetZIndex(to, 1);
            if (from != null) Panel.SetZIndex(from, 0);
            laps++;
            if (laps % 10 == 0 && from != null) ReplaceStandby(from);
            else if (from != null) { from.Pause(); from.Position = TimeSpan.Zero; }
        }

        void ReplaceStandby(MediaElement old)
        {
            if (string.IsNullOrEmpty(Theme.LoopPath) || !File.Exists(Theme.LoopPath)) return;
            var fresh = MakeLoopPlayer(Theme.LoopPath);
            Panel.SetZIndex(fresh, 0);
            contentGrid.Children.Add(fresh);
            if (old == loopA) loopA = fresh; else loopB = fresh;
            DisposePlayer(old);
        }

        void LaterRebuild(string why, int delayMs)
        {
            if (phase == PlayPhase.Done || Theme.Kind != "video" || !hasVideoLoop) return;
            pendingWhy = why;
            pendingAt = clock.ElapsedMilliseconds + delayMs;
        }

        bool RebuildLoops(string why)
        {
            if (phase == PlayPhase.Done || phase == PlayPhase.Ending || Theme.Kind != "video" || !hasVideoLoop) return true;
            long now = clock.ElapsedMilliseconds;
            long gap = failStreak < 3 ? 8000 : 60000;
            if (lastRecoverMs >= 0 && now - lastRecoverMs < gap) return false;
            failStreak++;
            recoveries++;
            lastRecoverMs = now;

            Log.W("循环背景自愈恢复（" + why + "）：第 " + recoveries + " 次，已循环 " + laps + " 圈");
            if (swapFrom != null && swapFrom == introMedia) FinishSwap();

            var old = swapFrom != null ? swapFrom : activeLoop;
            swapFrom = null;
            swapTo = null;
            DisposePlayer(retiring);
            retiring = null;
            if (loopA != old) DisposePlayer(loopA);
            if (loopB != old) DisposePlayer(loopB);
            if (old != null)
            {
                old.MediaEnded -= OnLoopEnded;
                old.MediaFailed -= OnLoopFailed;
                Panel.SetZIndex(old, 3);
                retiring = old;
                retireT0 = now;
            }
            loopA = MakeLoopPlayer(Theme.LoopPath);
            loopB = MakeLoopPlayer(Theme.LoopPath);
            Panel.SetZIndex(loopA, 1);
            Panel.SetZIndex(loopB, 0);
            contentGrid.Children.Add(loopA);
            contentGrid.Children.Add(loopB);
            activeLoop = loopA;
            verifying = true;
            lastMoveMs = now;
            lastPos = -1;
            return true;
        }

        void CheckRetire(long now)
        {
            if (retiring == null) return;
            if ((activeLoop != null && activeLoop.Position.TotalSeconds > 0.06) || now - retireT0 > 2500)
            {
                DisposePlayer(retiring);
                retiring = null;
            }
        }

        void Watchdog(long now)
        {
            if (Theme.Kind != "video" || !hasVideoLoop) return;
            if (now < nextSampleMs) return;
            nextSampleMs = now + 250;

            bool timerGap = lastSampleMs > 0 && now - lastSampleMs > 2000;
            lastSampleMs = now;
            if (timerGap || phase != PlayPhase.Revealed || !introGone || hidden || batteryPaused || activeLoop == null)
            {
                lastMoveMs = now;
                lastPos = -1;
                return;
            }

            double p = activeLoop.Position.TotalSeconds;
            if (Math.Abs(p - lastPos) > 0.0005)
            {
                lastPos = p;
                lastMoveMs = now;
                if (verifying && p > 0.3)
                {
                    verifying = false;
                    failStreak = 0;
                    Log.W("恢复成功：循环背景已恢复播放");
                }
                return;
            }
            long stuck = now - lastMoveMs;
            if (stuck >= 400 && swapFrom == null && activeLoop.NaturalDuration.HasTimeSpan
                && p >= activeLoop.NaturalDuration.TimeSpan.TotalSeconds - 0.08)
            {
                missedEnds++;
                if (missedEnds <= 20 || missedEnds % 100 == 0)
                    Log.W("循环段到末尾未收到 MediaEnded，手动无缝接上（第 " + missedEnds + " 次）");
                SwapLoop(activeLoop, OtherLoop(activeLoop));
                lastMoveMs = now;
                lastPos = -1;
                return;
            }
            if (stuck < 3000) return;
            RebuildLoops("画面 " + (stuck / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒未动");
        }

        void IntroEnded()
        {
            if (introGone) return;
            introGone = true;
            Log.W("开屏播放结束");
            if (!config.LoopBackground || (!hasVideoLoop && Theme.Kind == "video"))
            {
                if (phase == PlayPhase.Intro) Reveal(false);
                if (config.LoopBackground && !hasVideoLoop) Finish(false);
                CloseSkipButton();
                return;
            }
            if (hasVideoLoop && loopA != null)
            {
                loopA.Play();
                activeLoop = loopA;
                BeginSwap(introMedia, loopA);
            }
            if (phase == PlayPhase.Intro) Reveal(false);
            CloseSkipButton();
        }

        void CloseSkipButton()
        {
            if (skipBtn != null) skipBtn.Visibility = Visibility.Collapsed;
        }

        double CurrentIntroSeconds()
        {
            if (Theme.Kind == "video" && introMedia != null) return introMedia.Position.TotalSeconds;
            return GetClockSeconds();
        }

        double GetClockSeconds()
        {
            if (pausedClockAt.HasValue) return elapsedBaseMs / 1000.0;
            return (clock.ElapsedMilliseconds + elapsedBaseMs) / 1000.0;
        }

        void PauseOrResumePlayback(bool pause, string reason)
        {
            if (pause)
            {
                pausedReason = reason;
                if (!pausedClockAt.HasValue)
                {
                    elapsedBaseMs += clock.ElapsedMilliseconds;
                    clock.Reset();
                    pausedClockAt = DateTime.UtcNow;
                }
                if (introMedia != null && !introGone) { try { introMedia.Pause(); } catch { } }
                if (activeLoop != null) { try { activeLoop.Pause(); } catch { } }
            }
            else
            {
                pausedReason = "";
                if (pausedClockAt.HasValue)
                {
                    pausedClockAt = null;
                    clock.Start();
                }
                if (introMedia != null && !introGone) { try { introMedia.Play(); } catch { } }
                if (activeLoop != null && introGone) { try { activeLoop.Play(); } catch { } }
            }
        }

        void StartMorph(bool toDocked, double durationSec)
        {
            isMorphing = true;
            morphToDocked = toDocked;
            morphFromRect = currentOverlayRect;
            morphFromCorner = currentCornerRadius;
            morphToCorner = toDocked ? config.WidgetCornerRadius : 0.0;
            morphT0 = DateTime.UtcNow;
            morphDur = Math.Max(0.1, durationSec);
            UpdateDockDecorations();
        }

        void TickMorph()
        {
            if (!isMorphing) return;
            double p = Math.Min(1.0, (DateTime.UtcNow - morphT0).TotalSeconds / morphDur);
            double inv = 1.0 - p;
            double k = 1.0 - inv * inv * inv; // CubicEaseOut

            Native.RECT targetBounds;
            if (!Native.Bounds(Target.Handle, out targetBounds)) targetBounds = lastTargetBounds;
            else lastTargetBounds = targetBounds;

            double scale = Native.GetWindowDpiScale(Target.Handle);
            Native.RECT goal = morphToDocked ? ComputeWidgetRect(targetBounds, config, scale) : targetBounds;

            int l = (int)Math.Round(morphFromRect.Left + (goal.Left - morphFromRect.Left) * k);
            int t = (int)Math.Round(morphFromRect.Top + (goal.Top - morphFromRect.Top) * k);
            int r = (int)Math.Round(morphFromRect.Right + (goal.Right - morphFromRect.Right) * k);
            int b = (int)Math.Round(morphFromRect.Bottom + (goal.Bottom - morphFromRect.Bottom) * k);

            ApplyOverlayPos(l, t, Math.Max(40, r - l), Math.Max(40, b - t));
            UpdateCardClip(morphFromCorner + (morphToCorner - morphFromCorner) * k);

            if (p >= 1.0)
            {
                isMorphing = false;
                ApplyOverlayPos(goal.Left, goal.Top, goal.Right - goal.Left, goal.Bottom - goal.Top);
                UpdateCardClip(morphToCorner);
                UpdateDockDecorations();
            }
        }

        void Tick()
        {
            if (phase == PlayPhase.Done) return;

            if (isDragging)
            {
                UpdateDrag();
                return;
            }

            Follow(false);
            if (phase == PlayPhase.Done) return;
            TickMorph();
            EnsureOverlayStyle(false);

            long nowMs = clock.ElapsedMilliseconds;

            // 省电模式自动暂停检查（每 2 秒一次）
            if (nowMs >= nextPowerCheckMs)
            {
                nextPowerCheckMs = nowMs + 2000;
                bool onBattery;
                bool saver = Native.IsBatterySaverActive(out onBattery);
                bool shouldPauseBattery = config.PauseOnBatterySaver && saver && phase == PlayPhase.Revealed;
                if (shouldPauseBattery != batteryPaused)
                {
                    batteryPaused = shouldPauseBattery;
                    if (!hidden)
                    {
                        PauseOrResumePlayback(batteryPaused, batteryPaused ? "battery-saver" : "");
                        Log.W(batteryPaused ? "系统省电模式已开启，自动暂停动态背景解码" : "省电模式退出，恢复动态背景");
                    }
                }
            }

            if (hidden || batteryPaused) return;

            if (Theme.Kind == "video")
            {
                CheckSwap();
                CheckRetire(nowMs);
                if (pendingAt > 0 && nowMs >= pendingAt && phase == PlayPhase.Revealed && introGone)
                {
                    pendingAt = RebuildLoops(pendingWhy) ? 0 : nowMs + 1000;
                }
                Watchdog(nowMs);
            }
            else if (Theme.Kind == "procedural" && procStage != null)
            {
                // 透出后节流到 30fps（33ms），开屏期全速 60fps
                int interval = (phase == PlayPhase.Revealed && !isMorphing) ? 33 : 0;
                if (nowMs >= nextProcRenderMs)
                {
                    nextProcRenderMs = nowMs + interval;
                    procStage.ElapsedSeconds = GetClockSeconds();
                    procStage.InvalidateVisual();
                }
            }
            else if (Theme.Kind == "image" && imgScale != null)
            {
                double tImg = GetClockSeconds();
                double s = 1.018 + 0.018 * Math.Sin(tImg * 0.35);
                imgScale.ScaleX = s;
                imgScale.ScaleY = s;
            }

            if (!introGone)
            {
                double t = CurrentIntroSeconds();
                if (phase == PlayPhase.Intro && t >= Theme.FadeStartSeconds)
                {
                    if (Theme.Kind == "image") contentGrid.Background = Brushes.Transparent;
                    Reveal(false);
                    if (Theme.Kind != "video") introGone = true;
                }
                UpdateSubtitle(t);
            }

            if (alphaDur >= 0)
            {
                double k = alphaDur <= 0 ? 1.0 : Math.Min(1.0, (DateTime.Now - alphaT0).TotalSeconds / alphaDur);
                k = k * k * (3.0 - 2.0 * k);
                SetAlpha(alphaFrom + (alphaTo - alphaFrom) * k);
                if (k >= 1.0)
                {
                    alphaDur = -1;
                    var then = alphaThen;
                    alphaThen = null;
                    if (then != null) then();
                }
            }
            else if (phase == PlayPhase.Revealed)
            {
                double targetA = ComputeTargetAmbientAlpha();
                if (Math.Abs(alpha - targetA) > 0.004)
                {
                    SetAlpha(alpha + (targetA - alpha) * 0.14);
                }
            }

            if (volDur >= 0 && introMedia != null)
            {
                double k = volDur <= 0 ? 1.0 : Math.Min(1.0, (DateTime.Now - volT0).TotalSeconds / volDur);
                introMedia.Volume = volFrom + (volTo - volFrom) * k;
                if (k >= 1.0) volDur = -1;
            }
        }

        double ComputeTargetAmbientAlpha()
        {
            double baseAlpha = IsDockedNow() ? config.WidgetOpacity : config.ResidualAlpha;
            if (IsDockedNow()) return baseAlpha; // 挂件本身在侧边栏角落，不挡代码，专注时无需压暗
            if (!config.FocusDimEnabled) return baseAlpha;
            if (DateTime.UtcNow < sliderBoostUntil) return baseAlpha;
            bool isFocused = Native.GetForegroundWindow() == Target.Handle;
            if (!isFocused) return baseAlpha;
            double idle = Native.IdleSeconds();
            if (idle < config.FocusIdleSeconds) return baseAlpha * config.FocusDimFactor;
            return baseAlpha;
        }

        void ApplyOverlayPos(int left, int top, int width, int height)
        {
            currentOverlayRect.Left = left;
            currentOverlayRect.Top = top;
            currentOverlayRect.Right = left + width;
            currentOverlayRect.Bottom = top + height;
            if (overlayH != IntPtr.Zero)
            {
                uint f = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
                Native.SetWindowPos(overlayH, IntPtr.Zero, left, top, width, height, f);
            }
        }

        void Follow(bool force)
        {
            if (!Native.IsWindow(Target.Handle))
            {
                Log.W("目标窗口已关闭，收工退出");
                Teardown();
                return;
            }
            bool vis = Native.IsWindowVisible(Target.Handle) && !Native.IsIconic(Target.Handle) && !Native.Cloaked(Target.Handle);
            if (!vis)
            {
                if (!hidden)
                {
                    hidden = true;
                    ShowAll(false);
                    PauseOrResumePlayback(true, "window-hidden");
                }
                return;
            }
            if (hidden)
            {
                hidden = false;
                ShowAll(true);
                if (!batteryPaused) PauseOrResumePlayback(false, "");
                force = true;
            }

            if (isMorphing || isDragging) return;

            Native.RECT targetR;
            if (!Native.Bounds(Target.Handle, out targetR)) return;
            lastTargetBounds = targetR;

            double scale = Native.GetWindowDpiScale(Target.Handle);
            Native.RECT desired = IsDockedNow() ? ComputeWidgetRect(targetR, config, scale) : targetR;

            if (!force && desired.Left == currentOverlayRect.Left && desired.Top == currentOverlayRect.Top
                && desired.Right == currentOverlayRect.Right && desired.Bottom == currentOverlayRect.Bottom)
                return;

            ApplyOverlayPos(desired.Left, desired.Top, desired.Right - desired.Left, desired.Bottom - desired.Top);
        }

        void ShowAll(bool show)
        {
            if (overlayH != IntPtr.Zero) Native.ShowWindow(overlayH, show ? 4 /* SW_SHOWNOACTIVATE */ : 0 /* SW_HIDE */);
        }

        void EnsureOverlayStyle(bool force)
        {
            if (overlayH == IntPtr.Zero) return;
            long ex = Native.GetLong(overlayH, Native.GWL_EXSTYLE);
            long want = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
            if (clickThrough) want |= Native.WS_EX_TRANSPARENT;
            else want &= ~Native.WS_EX_TRANSPARENT;
            if (want != ex && styleFixes < 40)
            {
                if (!force) styleFixes++;
                Native.SetLong(overlayH, Native.GWL_EXSTYLE, want);
            }
        }

        void SetAlpha(double a)
        {
            alpha = Math.Max(0.0, Math.Min(1.0, a));
            contentGrid.Opacity = alpha;
        }

        void RampAlpha(double to, double seconds, Action then)
        {
            alphaFrom = alpha;
            alphaTo = to;
            alphaT0 = DateTime.Now;
            alphaDur = Math.Max(0.0, seconds);
            alphaThen = then;
        }

        void RampVolume(double to, double seconds)
        {
            if (introMedia == null) return;
            volFrom = introMedia.Volume;
            volTo = to;
            volT0 = DateTime.Now;
            volDur = seconds;
        }

        void UpdateSubtitle(double t)
        {
            if (sub == null || !Theme.ShowSubtitles || Theme.Subtitles == null) return;
            double fade = Math.Max(0.01, Theme.SubtitleFadeSeconds);
            int idx = -1;
            for (int i = 0; i < Theme.Subtitles.Count; i++)
            {
                if (t >= Theme.Subtitles[i].Start && t <= Theme.Subtitles[i].End) { idx = i; break; }
            }
            if (idx < 0)
            {
                sub.Opacity = 0;
                return;
            }
            var s = Theme.Subtitles[idx];
            if (idx != currentSub)
            {
                currentSub = idx;
                sub.SetText(s.Text);
            }
            sub.Opacity = Math.Max(0.0, Math.Min(1.0, Math.Min((t - s.Start) / fade, (s.End - t) / fade)));
        }

        void HideSubtitles()
        {
            if (sub == null) return;
            sub.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.4)));
            sub = null;
        }

        void Reveal(bool quick)
        {
            if (phase != PlayPhase.Intro) return;
            phase = PlayPhase.Revealed;
            CloseSkipButton();
            contentGrid.Background = Brushes.Transparent;

            if (!config.LoopBackground)
            {
                clickThrough = true;
                EnsureOverlayStyle(true);
                Log.W("到达透出节点且未启用循环背景，淡出退出");
                Finish(false);
                return;
            }
            if (Theme.Kind == "video" && !hasVideoLoop)
            {
                clickThrough = true;
                EnsureOverlayStyle(true);
                Log.W("视频主题无循环片段，淡出退出");
                Finish(false);
                return;
            }

            if (config.DisplayMode == "widget")
            {
                clickThrough = config.WidgetClickThrough;
                EnsureOverlayStyle(true);
                Log.W("开屏结束，平滑收缩至左下角挂件位");
                StartMorph(true, quick ? 0.55 : 0.85);
                RampAlpha(config.WidgetOpacity, quick ? 0.55 : 0.85, null);
            }
            else if (config.DisplayMode == "widget-always")
            {
                clickThrough = config.WidgetClickThrough;
                EnsureOverlayStyle(true);
                UpdateDockDecorations();
                RampAlpha(config.WidgetOpacity, 0.45, null);
            }
            else
            {
                clickThrough = true;
                EnsureOverlayStyle(true);
                UpdateDockDecorations();
                Log.W("透出 Antigravity 界面并切换为全屏点击穿透循环背景");
                RampAlpha(config.ResidualAlpha, quick ? 0.9 : Theme.FadeDurationSeconds, null);
            }
        }

        public void SkipIntro()
        {
            if (phase != PlayPhase.Intro) return;
            Log.W("用户点击/命令跳过开屏动画");
            if (!config.LoopBackground || (Theme.Kind == "video" && !hasVideoLoop))
            {
                Finish(true);
                return;
            }
            HideSubtitles();
            introGone = true;

            if (Theme.Kind == "procedural" && procStage != null)
            {
                procStage.IsLoopOnly = true;
                procStage.InvalidateVisual();
            }
            else if (Theme.Kind == "image")
            {
                contentGrid.Background = Brushes.Transparent;
            }
            else if (hasVideoLoop && loopA != null)
            {
                loopA.Play();
                activeLoop = loopA;
                if (introMedia != null)
                {
                    var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.8)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                    var iv = introMedia;
                    fade.Completed += delegate { iv.Visibility = Visibility.Collapsed; try { iv.Stop(); iv.Close(); } catch { } };
                    introMedia.BeginAnimation(UIElement.OpacityProperty, fade);
                    RampVolume(0, 0.8);
                }
            }
            Reveal(true);
        }

        public void ApplyConfig(AppConfig cfg, bool fromSlider)
        {
            bool wasDocked = IsDockedNow();
            config = cfg;
            if (fromSlider) sliderBoostUntil = DateTime.UtcNow.AddSeconds(3.0);
            if (phase == PlayPhase.Ending || phase == PlayPhase.Done) return;

            var st = ParseStretch(cfg.StretchMode);
            if (introMedia != null) introMedia.Stretch = st;
            if (loopA != null) loopA.Stretch = st;
            if (loopB != null) loopB.Stretch = st;
            if (imgView != null) imgView.Stretch = st;

            bool shouldDocked = IsDockedNow();
            if (phase == PlayPhase.Revealed)
            {
                clickThrough = shouldDocked ? cfg.WidgetClickThrough : true;
                EnsureOverlayStyle(true);
                if (shouldDocked != wasDocked)
                {
                    StartMorph(shouldDocked, 0.45);
                }
                else
                {
                    UpdateDockDecorations();
                    if (shouldDocked)
                    {
                        UpdateCardClip(cfg.WidgetCornerRadius);
                        Follow(true);
                    }
                }
                if (alphaDur < 0)
                {
                    SetAlpha(fromSlider ? (shouldDocked ? cfg.WidgetOpacity : cfg.ResidualAlpha) : ComputeTargetAmbientAlpha());
                }
            }
            else if (phase == PlayPhase.Intro && shouldDocked != wasDocked)
            {
                clickThrough = shouldDocked ? cfg.WidgetClickThrough : false;
                EnsureOverlayStyle(true);
                StartMorph(shouldDocked, 0.45);
            }

            if (introMedia != null && !introGone)
            {
                volDur = -1;
                introMedia.Volume = cfg.IntroVolume;
            }
            if (!cfg.PauseOnBatterySaver && batteryPaused)
            {
                batteryPaused = false;
                if (!hidden) PauseOrResumePlayback(false, "");
            }
        }

        public void Finish(bool fast)
        {
            if (phase == PlayPhase.Ending || phase == PlayPhase.Done) return;
            phase = PlayPhase.Ending;
            clickThrough = true;
            EnsureOverlayStyle(true);
            UpdateDockDecorations();
            double d = fast ? 0.35 : 1.1;
            CloseSkipButton();
            HideSubtitles();
            RampVolume(0, d);
            RampAlpha(0, d, Teardown);
        }

        public void Teardown()
        {
            if (phase == PlayPhase.Done) return;
            phase = PlayPhase.Done;
            if (tick != null) tick.Stop();
            try
            {
                if (onSession != null) SystemEvents.SessionSwitch -= onSession;
                if (onDisplay != null) SystemEvents.DisplaySettingsChanged -= onDisplay;
            }
            catch { }
            if (powerNotify != IntPtr.Zero)
            {
                try { Native.UnregisterPowerSettingNotification(powerNotify); } catch { }
                powerNotify = IntPtr.Zero;
            }
            DisposePlayer(introMedia);
            DisposePlayer(loopA);
            DisposePlayer(loopB);
            DisposePlayer(retiring);
            CloseSkipButton();
            try { overlay.Close(); } catch { }
            Log.W("浮层生命周期结束");
            if (onDone != null) onDone();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != 0x218 /* WM_POWERBROADCAST */) return IntPtr.Zero;
            try
            {
                long ev = wParam.ToInt64();
                if (ev == 0x7 || ev == 0x12) LaterRebuild("系统唤醒", 2000);
                else if (ev == 0x8013 && lParam != IntPtr.Zero)
                {
                    var g = (Guid)Marshal.PtrToStructure(lParam, typeof(Guid));
                    if (g == Native.GUID_CONSOLE_DISPLAY_STATE)
                    {
                        int state = Marshal.ReadInt32(lParam, 20);
                        if (state == 1 && displayState >= 0 && displayState != 1) LaterRebuild("显示器点亮", 1500);
                        displayState = state;
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        public StageSnapshot GetSnapshot()
        {
            var snap = new StageSnapshot();
            snap.Phase = phase.ToString().ToLowerInvariant();
            snap.DisplayMode = config != null ? config.DisplayMode : "widget";
            snap.ThemeId = Theme != null ? Theme.Id : "";
            snap.ThemeName = Theme != null ? Theme.Name : "";
            snap.Kind = Theme != null ? Theme.Kind : "";
            snap.TargetHwnd = Target != null ? Target.Hwnd : 0;
            snap.TargetTitle = Target != null ? (Target.Title ?? "") : "";
            snap.TargetSize = (currentOverlayRect.Right - currentOverlayRect.Left) + "x" + (currentOverlayRect.Bottom - currentOverlayRect.Top);
            snap.Laps = laps;
            snap.Recoveries = recoveries;
            snap.EffectiveAlpha = Math.Round(alpha, 3);
            snap.PausedReason = pausedReason ?? "";
            try { using (var pr = Process.GetCurrentProcess()) snap.MemoryMb = pr.WorkingSet64 / (1024 * 1024); }
            catch { snap.MemoryMb = 0; }
            return snap;
        }
    }
}
