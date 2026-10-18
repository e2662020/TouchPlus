using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TouchPlus.UI
{
    /// <summary>
    /// 视觉语言：校准证书 / 计量报告。
    ///
    /// 为什么是浅色：这是给教室大屏做"精度校准"的工具，环境是高亮度的，而且
    /// 所有同类工具都做成深色仪表盘 —— 浅色技术绘图纸反而更像专业校准设备。
    ///
    /// 三条纪律：
    ///   1. 界面单色墨：选择、激活、开关一律用墨色，不用彩色装饰；
    ///   2. 红色只作数据信号（越界/异常），绿色只作"合规"，别处不用；
    ///   3. 不用圆角卡片、阴影、渐变；层级由粗细两组规则线与留白承担。
    /// </summary>
    public static class Theme
    {
        public static float Scale = 1f;
        public static float CurrentDpi = 96f;

        static Theme() { ApplyLight(); }

        /* ---------------- 颜色：技术绘图纸 + 墨 ---------------- */
        public static Color Bg;        // 纸
        public static Color BgDeep;    // 侧栏
        public static Color Card;      // 面板
        public static Color CardAlt;
        public static Color CardHover;
        public static Color Line;      // 规则线
        public static Color LineSoft;

        public static Color Text;         // 墨
        public static Color TextDim;
        public static Color TextFaint;

        public static Color Accent;       // 墨：选择/激活/开关
        public static Color Accent2;
        public static Color AccentSoft;

        public static Color Ok;          // 合规
        public static Color OkSoft;
        public static Color Warn;       // 提示
        public static Color WarnSoft;
        public static Color Err;         // 越界
        public static Color ErrSoft;

        public static Color Track;

        /// <summary>开关打开时的轨道色：深色模式白轨、浅色模式黑轨（高对比，无彩色）。</summary>
        public static Color ToggleTrackOn
        {
            get { return Mode == ThemeMode.Dark ? Color.FromArgb(242, 243, 238) : Color.FromArgb(25, 28, 30); }
        }

        /// <summary>开关打开时的圆钮色：与轨道反相（深色模式黑钮、浅色模式白钮）。</summary>
        public static Color ToggleKnobOn
        {
            get { return Mode == ThemeMode.Dark ? Color.FromArgb(18, 20, 23) : Color.FromArgb(247, 248, 243); }
        }
        public static Color Well;      // 容差场底
        public static Color Grid;      // 方格纸

        private static Font _body, _bodyBold, _small, _tiny, _h1, _h2, _h3, _num, _numSmall, _mono, _monoBig, _monoMicro;

        public static int S(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static void InitScale(float dpi)
        {
            if (dpi <= 0) dpi = 96f;
            CurrentDpi = dpi;
            float scale = dpi / 96f;
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;
            if (Math.Abs(scale - Scale) > 0.001f)
            {
                Scale = scale;
                ResetFonts();
            }
        }

        public static void InitScale(Control control)
        {
            InitScale(DetectDpi(control));
        }

        /// <summary>
        /// 探测屏幕 DPI。优先 GetDpiForSystem（Win10 1607+），
        /// 老系统退回屏幕 DC 的 DpiX（Win7 起可用）。
        /// </summary>
        public static float DetectDpi(Control control)
        {
            try
            {
                uint dpi = GetDpiForSystemSafe();
                if (dpi > 0) return dpi;
            }
            catch (Exception)
            {
            }

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (g.DpiX > 0) return g.DpiX;
                }
            }
            catch (Exception)
            {
            }

            if (control != null)
            {
                try
                {
                    using (Graphics g = control.CreateGraphics())
                    {
                        return g.DpiX;
                    }
                }
                catch (Exception)
                {
                }
            }
            return 96f;
        }

        private static uint GetDpiForSystemSafe()
        {
            if (Environment.OSVersion.Version.Major < 10) return 0;
            try { return GetDpiForSystem(); }
            catch (Exception) { return 0; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private static Font _iconFont;
        private static bool _iconFontProbed;

        /// <summary>系统图标字体（Win10/11 自带 Segoe MDL2 Assets）；没有就返回 null。</summary>
        public static Font IconFont
        {
            get
            {
                if (_iconFontProbed) return _iconFont;
                _iconFontProbed = true;
                try
                {
                    Font f = new Font("Segoe MDL2 Assets", 16f * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
                    if (string.Compare(f.Name, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        f.Dispose();
                        return null;
                    }
                    _iconFont = f;
                }
                catch (Exception)
                {
                    _iconFont = null;
                }
                return _iconFont;
            }
        }

        /// <summary>
        /// 画导航图标：优先用系统图标字体的码位；字体不存在（Win7/8）则用 Segoe UI Symbol 的字形回退，
        /// 保证任何系统上侧栏都有图标，而不是缺字方块。
        /// </summary>
        public static void DrawNavIcon(Graphics g, Rectangle rect, int codepoint, string fallbackText, Color color)
        {
            Font icons = IconFont;
            if (icons != null)
            {
                using (StringFormat center = FormatCenter())
                {
                    DrawString(g, char.ConvertFromUtf32(codepoint), icons, color,
                        new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), center);
                }
                return;
            }

            try
            {
                using (Font symbol = new Font("Segoe UI Symbol", 13f * Scale, FontStyle.Regular, GraphicsUnit.Pixel))
                using (StringFormat center = FormatCenter())
                {
                    DrawString(g, fallbackText, symbol, color,
                        new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), center);
                }
            }
            catch (Exception)
            {
            }
        }

        public static void ResetFonts()
        {
            _body = _bodyBold = _small = _tiny = _h1 = _h2 = _h3 = null;
            _num = _numSmall = _mono = _monoBig = _monoMicro = null;
            _iconFont = null;
            _iconFontProbed = false;
        }

        /// <summary>
        /// 按"像素"创建字体（不是磅）：布局是自己算像素的，用磅会被 GDI+ 按 DPI 再放大一次。
        /// </summary>
        private static Font Make(float pixelSize, FontStyle style)
        {
            float scaled = pixelSize * Scale;
            if (scaled < 6f) scaled = 6f;
            try
            {
                Font f = new Font("Microsoft YaHei UI", scaled, style, GraphicsUnit.Pixel);
                if (string.Compare(f.Name, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    f.Dispose();
                    f = new Font(FontFamily.GenericSansSerif, scaled, style, GraphicsUnit.Pixel);
                }
                return f;
            }
            catch (Exception)
            {
                return new Font(FontFamily.GenericSansSerif, scaled, style, GraphicsUnit.Pixel);
            }
        }

        /// <summary>测量数值一律等宽：数字要能按位对齐，这是功能需要。</summary>
        private static Font MakeMono(float pixelSize, FontStyle style)
        {
            float scaled = pixelSize * Scale;
            if (scaled < 6f) scaled = 6f;
            try { return new Font("Consolas", scaled, style, GraphicsUnit.Pixel); }
            catch (Exception) { return Make(pixelSize, style); }
        }

        public static Font Body { get { if (_body == null) _body = Make(13f, FontStyle.Regular); return _body; } }
        public static Font BodyBold { get { if (_bodyBold == null) _bodyBold = Make(13f, FontStyle.Bold); return _bodyBold; } }
        public static Font Small { get { if (_small == null) _small = Make(11.5f, FontStyle.Regular); return _small; } }
        public static Font Tiny { get { if (_tiny == null) _tiny = Make(10.5f, FontStyle.Regular); return _tiny; } }
        public static Font H1 { get { if (_h1 == null) _h1 = Make(31f, FontStyle.Bold); return _h1; } }
        public static Font H2 { get { if (_h2 == null) _h2 = Make(18f, FontStyle.Bold); return _h2; } }
        public static Font H3 { get { if (_h3 == null) _h3 = Make(14.5f, FontStyle.Bold); return _h3; } }
        public static Font Num { get { if (_num == null) _num = MakeMono(26f, FontStyle.Bold); return _num; } }
        public static Font NumSmall { get { if (_numSmall == null) _numSmall = MakeMono(17f, FontStyle.Bold); return _numSmall; } }
        public static Font Mono { get { if (_mono == null) _mono = MakeMono(12.5f, FontStyle.Regular); return _mono; } }
        public static Font MonoBig { get { if (_monoBig == null) _monoBig = MakeMono(44f, FontStyle.Bold); return _monoBig; } }
        public static Font MonoMicro { get { if (_monoMicro == null) _monoMicro = MakeMono(11f, FontStyle.Regular); return _monoMicro; } }

        /* ---------------- 绘图助手 ---------------- */

        /// <summary>
        /// 品牌标记：直接绘制程序图标（与桌面图标同一份像素），不再用"方块 + T+"的临时画法。
        /// 图标自带透明通道，什么底色上都成立。
        /// </summary>
        public static void DrawBrandMark(Graphics g, Rectangle rect)
        {
            try
            {
                using (Bitmap icon = AppIcon.Get().ToBitmap())
                {
                    InterpolationMode previous = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, rect);
                    g.InterpolationMode = previous;
                }
            }
            catch (Exception)
            {
                FillRound(g, rect, S(4), Accent);
            }
        }

        /// <summary>1px 水平规则线。</summary>
        public static void Rule(Graphics g, int x1, int y, int x2)
        {
            using (Pen pen = new Pen(Line, 1f))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawLine(pen, x1, y + 0.5f, x2, y + 0.5f);
            }
        }

        public static void RuleV(Graphics g, int x, int y1, int y2)
        {
            using (Pen pen = new Pen(Line, 1f))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawLine(pen, x + 0.5f, y1, x + 0.5f, y2);
            }
        }

        /// <summary>方格纸：给容差场用的 10px 主格 + 5px 次格。</summary>
        public static void DrawGrid(Graphics g, Rectangle area, int minor, int major)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            using (Pen minorPen = new Pen(Color.FromArgb(120, Grid), 1f))
            using (Pen majorPen = new Pen(Grid, 1f))
            {
                int i = 0;
                for (int x = area.X + minor; x < area.Right; x += minor, i++)
                    g.DrawLine((i % 5 == 4) ? majorPen : minorPen, x, area.Y, x, area.Bottom);
                i = 0;
                for (int y = area.Y + minor; y < area.Bottom; y += minor, i++)
                    g.DrawLine((i % 5 == 4) ? majorPen : minorPen, area.X, y, area.Right, y);
            }
        }

        /// <summary>状态圆点（无光晕，纸面上就是一个实心点）。</summary>
        public static void StatusDot(Graphics g, int cx, int cy, int radius, Color color)
        {
            using (SolidBrush dot = new SolidBrush(color))
            {
                g.FillEllipse(dot, cx - radius, cy - radius, radius * 2, radius * 2);
            }
        }

        /// <summary>橡皮图章式标签：细边框 + 墨字，用来表示状态，而不是彩色药丸。</summary>
        public static void Stamp(Graphics g, Rectangle r, string text, Color border, Color textColor, Font font)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (Pen pen = new Pen(border, 1f))
            {
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            using (StringFormat format = FormatCenter())
            {
                DrawString(g, text, font, textColor, new RectangleF(r.X, r.Y, r.Width, r.Height), format);
            }
        }

        public static StringFormat FormatTopLeft()
        {
            StringFormat f = new StringFormat();
            f.Alignment = StringAlignment.Near;
            f.LineAlignment = StringAlignment.Near;
            f.Trimming = StringTrimming.EllipsisWord;
            return f;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            if (d <= 2)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPath(b, p);
            }
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Brush brush)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            {
                g.FillPath(brush, p);
            }
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            Rectangle inner = new Rectangle(r.X, r.Y, Math.Max(1, r.Width - 1), Math.Max(1, r.Height - 1));
            using (GraphicsPath p = Round(inner, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, p);
            }
        }

        public static LinearGradientBrush AccentBrush(Rectangle r, float angle)
        {
            return new LinearGradientBrush(r, Accent, Accent2, angle);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static void DrawString(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (SolidBrush b = new SolidBrush(color))
            {
                g.DrawString(text, font, b, x, y);
            }
        }

        public static void DrawString(Graphics g, string text, Font font, Color color, RectangleF rect, StringFormat format)
        {
            using (SolidBrush b = new SolidBrush(color))
            {
                g.DrawString(text, font, b, rect, format);
            }
        }

        public static StringFormat FormatLeft()
        {
            StringFormat f = new StringFormat();
            f.Alignment = StringAlignment.Near;
            f.LineAlignment = StringAlignment.Center;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags = StringFormatFlags.NoWrap;
            return f;
        }

        public static StringFormat FormatCenter()
        {
            StringFormat f = new StringFormat();
            f.Alignment = StringAlignment.Center;
            f.LineAlignment = StringAlignment.Center;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags = StringFormatFlags.NoWrap;
            return f;
        }

        public static StringFormat FormatRight()
        {
            StringFormat f = new StringFormat();
            f.Alignment = StringAlignment.Far;
            f.LineAlignment = StringAlignment.Center;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags = StringFormatFlags.NoWrap;
            return f;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /* ---------------- 深浅色：Auto 跟随 Windows，可手动覆盖 ---------------- */
        public enum ThemeMode { Auto = 0, Light = 1, Dark = 2 }

        public static ThemeMode Mode = ThemeMode.Auto;
        public static bool IsDark { get; private set; }

        public static event EventHandler Changed;

        public static void ApplyMode(ThemeMode mode)
        {
            Mode = mode;
            IsDark = ResolveDark(mode);
            if (IsDark) ApplyDark(); else ApplyLight();
            ResetFonts();
            EventHandler handler = Changed;
            if (handler != null)
            {
                try { handler(null, EventArgs.Empty); }
                catch (Exception) { }
            }
        }

        /// <summary>系统主题变了（WM_SETTINGCHANGE / ImmersiveColorSet）时调用。</summary>
        public static void RefreshAuto()
        {
            if (Mode != ThemeMode.Auto) return;
            bool dark = !SystemUsesLightTheme();
            if (dark != IsDark) ApplyMode(ThemeMode.Auto);
        }

        private static bool ResolveDark(ThemeMode mode)
        {
            if (mode == ThemeMode.Light) return false;
            if (mode == ThemeMode.Dark) return true;
            return !SystemUsesLightTheme();
        }

        /// <summary>读 HKCU\...\Themes\Personalize\AppsUseLightTheme：1 = 浅色。</summary>
        private static bool SystemUsesLightTheme()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("AppsUseLightTheme");
                        if (v is int) return (int)v != 0;
                    }
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        private static void ApplyLight()
        {
            Bg = Color.FromArgb(236, 237, 233);
            BgDeep = Color.FromArgb(227, 229, 224);
            Card = Color.FromArgb(247, 248, 243);
            CardAlt = Color.FromArgb(237, 238, 232);
            CardHover = Color.FromArgb(231, 232, 225);
            Line = Color.FromArgb(196, 200, 194);
            LineSoft = Color.FromArgb(216, 219, 213);
            Text = Color.FromArgb(25, 28, 30);
            TextDim = Color.FromArgb(78, 85, 90);
            TextFaint = Color.FromArgb(139, 147, 151);
            Accent = Color.FromArgb(29, 33, 36);
            Accent2 = Color.FromArgb(58, 65, 71);
            AccentSoft = Color.FromArgb(220, 222, 216);
            Ok = Color.FromArgb(47, 125, 92);
            OkSoft = Color.FromArgb(221, 234, 226);
            Warn = Color.FromArgb(154, 105, 21);
            WarnSoft = Color.FromArgb(240, 230, 208);
            Err = Color.FromArgb(192, 57, 43);
            ErrSoft = Color.FromArgb(242, 222, 218);
            Track = Color.FromArgb(207, 211, 204);
            Well = Color.FromArgb(244, 245, 239);
            Grid = Color.FromArgb(206, 211, 203);
        }

        private static void ApplyDark()
        {
            Accent = Color.FromArgb(233, 235, 230);
            Accent2 = Color.FromArgb(185, 190, 186);
            AccentSoft = Color.FromArgb(46, 53, 58);
            Bg = Color.FromArgb(21, 24, 27);
            BgDeep = Color.FromArgb(16, 19, 21);
            Card = Color.FromArgb(28, 32, 36);
            CardAlt = Color.FromArgb(35, 40, 45);
            CardHover = Color.FromArgb(40, 46, 52);
            Err = Color.FromArgb(224, 107, 96);
            ErrSoft = Color.FromArgb(46, 29, 27);
            Grid = Color.FromArgb(44, 50, 56);
            Line = Color.FromArgb(58, 65, 71);
            LineSoft = Color.FromArgb(43, 49, 54);
            Ok = Color.FromArgb(79, 191, 143);
            OkSoft = Color.FromArgb(27, 46, 38);
            Text = Color.FromArgb(233, 235, 230);
            TextDim = Color.FromArgb(167, 174, 178);
            TextFaint = Color.FromArgb(113, 120, 125);
            Track = Color.FromArgb(58, 65, 71);
            Warn = Color.FromArgb(199, 144, 53);
            WarnSoft = Color.FromArgb(46, 38, 23);
            Well = Color.FromArgb(24, 28, 31);
        }
    }
}
