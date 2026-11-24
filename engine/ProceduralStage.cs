using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AntigravityBootOverlay
{
    /// 离线内置的 Gemini「星穹极光 (Stellar Aurora)」矢量动画渲染器：
    /// 不依赖任何外部 MP4 文件即可在 Antigravity 上渲染极光流线、双弧星芒、星尘场与文字开屏，并在透出后保留轻盈呼吸动态氛围。
    public class ProceduralStage : FrameworkElement
    {
        struct StarSeed
        {
            public double Nx;
            public double Ny;
            public double Phase;
            public double Speed;
            public double Radius;
            public byte Tone;
        }

        readonly List<StarSeed> stars = new List<StarSeed>();

        public double ElapsedSeconds;
        public double FadeStartSeconds;
        public double FadeDurationSeconds;
        public bool IsLoopOnly;
        public bool IsDockedWidget;
        public double BackdropOpacity;

        static readonly FontFamily TitleFont = new FontFamily("Segoe UI");
        static readonly Typeface TitleTypeface = new Typeface(TitleFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        static readonly SolidColorBrush WhiteCoreBrush = CreateFrozenBrush(Color.FromArgb(255, 255, 255, 255));
        static readonly LinearGradientBrush WidgetBackdropBrush = CreateFrozenBackdrop();

        static SolidColorBrush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static LinearGradientBrush CreateFrozenBackdrop()
        {
            var b = new LinearGradientBrush(
                Color.FromArgb(236, 7, 11, 25),
                Color.FromArgb(238, 15, 23, 52),
                new Point(0, 0), new Point(1, 1));
            b.Freeze();
            return b;
        }

        public ProceduralStage()
        {
            IsHitTestVisible = false;
            FadeStartSeconds = 4.2;
            FadeDurationSeconds = 1.2;
            BackdropOpacity = 1.0;
            var rng = new Random(202604);
            for (int i = 0; i < 52; i++)
            {
                var st = new StarSeed();
                st.Nx = rng.NextDouble();
                st.Ny = rng.NextDouble();
                st.Phase = rng.NextDouble() * Math.PI * 2.0;
                st.Speed = 0.6 + rng.NextDouble() * 1.4;
                st.Radius = 1.0 + rng.NextDouble() * 2.2;
                st.Tone = (byte)(i % 3);
                stars.Add(st);
            }
        }

        static double Clamp01(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        static double Smooth(double edge0, double edge1, double x)
        {
            double t = Clamp01((x - edge0) / Math.Max(0.001, edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 20 || h < 20) return;

            double t = ElapsedSeconds;
            double minDim = Math.Min(w, h);
            double cx = w * 0.5;
            double cy = h * 0.48;

            // 开屏阶段底色不透明，全屏透出阶段底色完全透明；挂件模式下保留深邃星穹底色
            double bgOpacity = IsDockedWidget ? 0.94 : (IsLoopOnly ? 0.0 : (1.0 - Smooth(FadeStartSeconds, FadeStartSeconds + Math.Max(0.2, FadeDurationSeconds), t)));
            bgOpacity *= Clamp01(BackdropOpacity);

            if (bgOpacity > 0.005)
            {
                if (IsDockedWidget && bgOpacity > 0.9)
                {
                    dc.DrawRectangle(WidgetBackdropBrush, null, new Rect(0, 0, w, h));
                }
                else
                {
                    var bgBrush = new LinearGradientBrush(
                        Color.FromArgb((byte)(bgOpacity * 246), 7, 11, 25),
                        Color.FromArgb((byte)(bgOpacity * 248), 15, 23, 52),
                        new Point(0, 0), new Point(1, 1));
                    dc.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));
                }
            }

            // 极光星云光晕
            double auraPulse = 0.85 + 0.15 * Math.Sin(t * 0.9);
            double auraAlpha = IsLoopOnly ? 0.42 : (0.55 + 0.35 * Smooth(0.2, 1.4, t) - 0.35 * Smooth(FadeStartSeconds, FadeStartSeconds + 1.0, t));
            auraAlpha = Clamp01(auraAlpha);

            DrawGlow(dc, w * (0.36 + 0.06 * Math.Sin(t * 0.23)), h * (0.42 + 0.05 * Math.Cos(t * 0.19)),
                minDim * 0.46 * auraPulse, Color.FromArgb((byte)(auraAlpha * 110), 56, 189, 248));
            DrawGlow(dc, w * (0.64 + 0.06 * Math.Cos(t * 0.21)), h * (0.56 + 0.05 * Math.Sin(t * 0.17)),
                minDim * 0.44 * auraPulse, Color.FromArgb((byte)(auraAlpha * 95), 168, 85, 247));

            // 流光极光丝带
            double ribbonThick = Math.Max(1.4, minDim * 0.0045);
            DrawAuroraRibbon(dc, w, h, t, 0.36, 0.08, 0.9, Color.FromArgb((byte)(auraAlpha * 115), 56, 189, 248), ribbonThick);
            DrawAuroraRibbon(dc, w, h, t + 1.9, 0.64, -0.07, 0.7, Color.FromArgb((byte)(auraAlpha * 95), 192, 132, 252), ribbonThick * 0.8);

            // 闪烁星尘场
            double starSizeScale = Math.Max(0.55, Math.Min(1.2, minDim / 360.0));
            for (int i = 0; i < stars.Count; i++)
            {
                var st = stars[i];
                double sx = ((st.Nx + t * 0.008 * st.Speed) % 1.0) * w;
                double sy = ((st.Ny - t * 0.004 * st.Speed + 10.0) % 1.0) * h;
                double tw = 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(t * st.Speed * 2.2 + st.Phase));
                byte a = (byte)(Clamp01(tw * (IsLoopOnly ? 0.78 : 0.95)) * 220);
                Color col = st.Tone == 0 ? Color.FromArgb(a, 186, 230, 253)
                          : (st.Tone == 1 ? Color.FromArgb(a, 221, 214, 254) : Color.FromArgb(a, 254, 240, 138));
                double r = st.Radius * starSizeScale;
                dc.DrawEllipse(new SolidColorBrush(col), null, new Point(sx, sy), r, r);
            }

            // 轨道环
            double ringStrength = IsLoopOnly ? (IsDockedWidget ? 0.72 : 0.48) : Smooth(0.4, 1.8, t) * (1.0 - 0.52 * Smooth(FadeStartSeconds, FadeStartSeconds + 1.0, t));
            if (ringStrength > 0.01)
            {
                double rx = minDim * (IsDockedWidget ? 0.32 : 0.25);
                double ry = minDim * (IsDockedWidget ? 0.12 : 0.095);
                double rot = -22.0 + 8.0 * Math.Sin(t * 0.35);
                dc.PushTransform(new RotateTransform(rot, cx, cy));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(ringStrength * 165), 125, 211, 252)), Math.Max(1.1, minDim * 0.004));
                pen.DashStyle = new DashStyle(new double[] { 8, 6 }, t * 18.0);
                dc.DrawEllipse(null, pen, new Point(cx, cy), rx, ry);
                dc.Pop();
            }

            // 双光刃弧线 + 四芒星核心（开屏期强烈绽放，进入循环后收束为温和灵动星徽）
            double introFactor = IsLoopOnly ? 0.0 : (1.0 - Smooth(FadeStartSeconds, FadeStartSeconds + 1.1, t));
            double starScale = IsLoopOnly ? (IsDockedWidget ? (0.68 + 0.06 * Math.Sin(t * 1.4)) : (0.42 + 0.04 * Math.Sin(t * 1.4)))
                : (0.35 + 0.65 * Smooth(0.3, 1.5, t) - 0.52 * Smooth(FadeStartSeconds, FadeStartSeconds + 1.1, t));
            double starAlpha = IsLoopOnly ? (IsDockedWidget ? 0.90 : 0.68) : Smooth(0.15, 1.0, t);

            if (introFactor > 0.02)
            {
                double sweep = Smooth(0.6, 2.2, t);
                double arcLen = minDim * 0.22 * sweep;
                double bladeThick = Math.Max(1.8, minDim * 0.008);
                var bladePenA = new Pen(new SolidColorBrush(Color.FromArgb((byte)(introFactor * 220), 125, 211, 252)), bladeThick);
                bladePenA.StartLineCap = PenLineCap.Round;
                bladePenA.EndLineCap = PenLineCap.Round;
                dc.DrawLine(bladePenA, new Point(cx - arcLen, cy + arcLen * 0.72), new Point(cx + arcLen, cy - arcLen * 0.72));

                var bladePenB = new Pen(new SolidColorBrush(Color.FromArgb((byte)(introFactor * 210), 216, 180, 254)), bladeThick);
                bladePenB.StartLineCap = PenLineCap.Round;
                bladePenB.EndLineCap = PenLineCap.Round;
                dc.DrawLine(bladePenB, new Point(cx - arcLen, cy - arcLen * 0.72), new Point(cx + arcLen, cy + arcLen * 0.72));
            }

            // 四芒星
            double starR = minDim * 0.14 * starScale;
            DrawFourPointStar(dc, cx, cy, starR, starAlpha);

            // 开屏期字标："A N T I G R A V I T Y"
            if (!IsLoopOnly)
            {
                double titleAlpha = Smooth(1.4, 2.1, t) * (1.0 - Smooth(FadeStartSeconds - 0.3, FadeStartSeconds + 0.7, t));
                if (titleAlpha > 0.01)
                {
                    double fontSize = Math.Max(10.0, Math.Min(48.0, minDim * 0.058));
#pragma warning disable 618
                    var ft = new FormattedText(
                        "A N T I G R A V I T Y",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        TitleTypeface,
                        fontSize,
                        new SolidColorBrush(Color.FromArgb((byte)(titleAlpha * 245), 240, 249, 255)));
#pragma warning restore 618
                    dc.DrawText(ft, new Point(cx - ft.Width / 2.0, cy + minDim * 0.19));
                }
            }
        }

        static void DrawGlow(DrawingContext dc, double x, double y, double radius, Color centerColor)
        {
            var b = new RadialGradientBrush(centerColor, Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B));
            dc.DrawEllipse(b, null, new Point(x, y), radius, radius);
        }

        static void DrawAuroraRibbon(DrawingContext dc, double w, double h, double t, double yRatio, double ampRatio, double freq, Color color, double thickness)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                int steps = 20;
                for (int i = 0; i <= steps; i++)
                {
                    double u = (double)i / steps;
                    double x = u * w;
                    double y = h * (yRatio + ampRatio * Math.Sin(u * Math.PI * 2.0 * freq + t * 0.55) + 0.025 * Math.Cos(u * 9.0 - t * 0.9));
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, true);
                }
            }
            g.Freeze();
            var pen = new Pen(new SolidColorBrush(color), thickness);
            dc.DrawGeometry(null, pen, g);
        }

        static void DrawFourPointStar(DrawingContext dc, double cx, double cy, double r, double alpha)
        {
            double inner = r * 0.22;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx, cy - r), true, true);
                ctx.QuadraticBezierTo(new Point(cx + inner * 0.35, cy - inner * 0.35), new Point(cx + r, cy), true, true);
                ctx.QuadraticBezierTo(new Point(cx + inner * 0.35, cy + inner * 0.35), new Point(cx, cy + r), true, true);
                ctx.QuadraticBezierTo(new Point(cx - inner * 0.35, cy + inner * 0.35), new Point(cx - r, cy), true, true);
                ctx.QuadraticBezierTo(new Point(cx - inner * 0.35, cy - inner * 0.35), new Point(cx, cy - r), true, true);
            }
            g.Freeze();
            var fill = new RadialGradientBrush(
                Color.FromArgb((byte)(alpha * 255), 255, 255, 255),
                Color.FromArgb((byte)(alpha * 235), 147, 197, 253));
            dc.DrawGeometry(fill, null, g);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 255, 255, 255)), null, new Point(cx, cy), Math.Max(2, r * 0.14), Math.Max(2, r * 0.14));
        }

        public static void RenderPreviewPng(string outPath, int width, int height, double sampleTimeSec)
        {
            var stage = new ProceduralStage();
            stage.Width = width;
            stage.Height = height;
            stage.ElapsedSeconds = sampleTimeSec;
            stage.IsLoopOnly = false;
            stage.Measure(new Size(width, height));
            stage.Arrange(new Rect(0, 0, width, height));
            stage.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(stage);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var fs = File.Create(outPath))
                enc.Save(fs);
        }
    }
}
