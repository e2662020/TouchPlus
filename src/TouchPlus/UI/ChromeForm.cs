using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TouchPlus.UI.Controls;

namespace TouchPlus.UI
{
    /// <summary>
    /// 无边框窗体基类：提供拖动标题栏与八方向缩放（通过 WM_NCHITTEST），
    /// 同时屏蔽双击标题栏最大化（窗口尺寸是设计好的，不需要最大化）。
    /// </summary>
    public class ChromeForm : Form
    {
        public int ResizeBorderPx = 6;

        /// <summary>
        /// 渲染方式按系统分叉：
        ///   Win10/11 —— 开 WS_EX_COMPOSITED，让 DWM 统一合成子控件，动画不撕裂；
        ///   Win7/8   —— WS_EX_COMPOSITED 在老系统上会明显掉帧，改用经典双缓冲。
        /// </summary>
        private readonly bool _dwmComposition;

        public ChromeForm()
        {
            bool modern = Environment.OSVersion.Version.Major >= 10;
            _dwmComposition = modern;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
            if (!modern) SetStyle(ControlStyles.Opaque, true);
            UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (_dwmComposition) cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED
                return cp;
            }
        }
        public int TitleDragHeightPx = 52;
        public bool AllowResize = true;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;

        private bool _regionRounded;

        /// <summary>Win11 用 DWM 圆角；Win7/10 没有该属性时退回 Region 圆角。</summary>
        private void TryRoundCorners()
        {
            try
            {
                int preference = 2;   // DWMWCP_ROUND（有边框时系统会再圆一次，无害）
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch (Exception)
            {
            }
            // 无边框窗口上 DWM 圆角不保证生效，始终由 Region 兜底：
            // 这样浅色/深色、Win7~Win11 的窗口轮廓和右上角"关闭键红块"裁切都一致。
            ApplyRegionCorners();
        }

        private void ApplyRegionCorners()
        {
            try
            {
                int radius = Theme.S(10);
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width, Height), radius))
                {
                    Region = new Region(path);
                }
                _regionRounded = true;
            }
            catch (Exception)
            {
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryRoundCorners();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!_regionRounded) return;
            if (WindowState == FormWindowState.Maximized) { Region = null; return; }
            int radius = Theme.S(10);
            using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width, Height), radius))
            {
                Region = new Region(path);
            }
        }

        /// <summary>最大/还原（窗口按钮与双击标题栏共用）。</summary>
        public void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                return;   // 不响应标题栏双击（避免最大化）
            }

            if (m.Msg == WM_NCHITTEST && AllowResize)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1 /* HTCLIENT */)
                {
                    int lp = m.LParam.ToInt32();
                    int sx = (short)(lp & 0xFFFF);
                    int sy = (short)((lp >> 16) & 0xFFFF);
                    Point p = PointToClient(new Point(sx, sy));

                    int b = Theme.S(ResizeBorderPx);
                    bool left = p.X <= b;
                    bool right = p.X >= ClientSize.Width - b;
                    bool top = p.Y <= b;
                    bool bottom = p.Y >= ClientSize.Height - b;

                    if (top && left) m.Result = (IntPtr)13;         // HTTOPLEFT
                    else if (top && right) m.Result = (IntPtr)14;   // HTTOPRIGHT
                    else if (bottom && left) m.Result = (IntPtr)16; // HTBOTTOMLEFT
                    else if (bottom && right) m.Result = (IntPtr)17;// HTBOTTOMRIGHT
                    else if (left) m.Result = (IntPtr)10;           // HTLEFT
                    else if (right) m.Result = (IntPtr)11;          // HTRIGHT
                    else if (top) m.Result = (IntPtr)12;            // HTTOP
                    else if (bottom) m.Result = (IntPtr)15;         // HTBOTTOM
                    else if (p.Y <= Theme.S(TitleDragHeightPx)) m.Result = (IntPtr)2; // HTCAPTION
                    return;
                }
                return;
            }

            base.WndProc(ref m);
        }
    }

    /// <summary>标题栏上的小圆角按钮（最小化 / 关闭）。</summary>
    public sealed class ChromeButton : TouchPlus.UI.Controls.ThemedControl
    {
        public enum Glyph
        {
            Minimize,
            Maximize,
            Restore,
            Close,
            Settings
        }

        /// <summary>和系统窗口按钮一致的尺寸（46×36 DIP）。</summary>
        public static readonly Size ButtonSize = new Size(46, 36);

        private bool _hover;
        private bool _pressed;
        private float _hoverAnim;          // 0→1 悬停淡入
        private Timer _hoverTimer;
        private Glyph _glyph = Glyph.Minimize;
        private bool _danger;

        public ChromeButton()
        {
            Size = new Size(Theme.S(ButtonSize.Width), Theme.S(ButtonSize.Height));
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public Glyph IconGlyph { get { return _glyph; } set { _glyph = value; Invalidate(); } }
        public bool Danger { get { return _danger; } set { _danger = value; Invalidate(); } }

        /// <summary>悬停淡入淡出：16ms 帧、指数逼近，约 120ms 到位。</summary>
        private void AnimateHover(float target)
        {
            if (_hoverTimer == null)
            {
                _hoverTimer = new Timer();
                _hoverTimer.Interval = 16;
                _hoverTimer.Tick += delegate
                {
                    float target2 = _hover ? 1f : 0f;
                    float delta = target2 - _hoverAnim;
                    if (Math.Abs(delta) < 0.02f)
                    {
                        _hoverAnim = target2;
                        _hoverTimer.Stop();
                    }
                    else
                    {
                        _hoverAnim += delta * 0.35f;
                    }
                    Invalidate();
                };
            }
            _hoverTimer.Start();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; AnimateHover(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; AnimateHover(0f); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        private static Color Lerp(Color from, Color to, float t)
        {
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }

        /// <summary>圆角矩形描边：窗口按钮的"方框"字形用圆角，和系统一致。</summary>
        private static void DrawRoundRect(Graphics g, Pen pen, int x, int y, int w, int h, int radius)
        {
            int r = Math.Max(1, Math.Min(radius, Math.Min(w, h) / 2));
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = r * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            Rectangle r = new Rectangle(0, 0, Width, Height);

            // 悬停底色按动画值淡入（关闭键=红、其余=浅灰），不再瞬间切换
            float anim = _pressed ? 1f : _hoverAnim;
            if (anim > 0.01f)
            {
                Color baseColor = _danger
                    ? (_pressed ? Color.FromArgb(176, 26, 18) : Color.FromArgb(196, 43, 28))
                    : (_pressed ? Theme.CardAlt : Theme.CardHover);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(anim * 255), baseColor)))
                    g.FillRectangle(brush, r);
            }

            // 字形几何按 Windows 原生窗口按钮的度量：10×10 DIP 方框、1 DIP 线宽
            Color fore = _danger ? Lerp(Theme.Text, Color.White, anim) : Theme.Text;
            float stroke = Math.Max(1f, Theme.S(1f));
            using (Pen pen = new Pen(fore, stroke))
            {
                int cx = Width / 2;
                int cy = Height / 2;
                int half = Theme.S(5);              // 半边长 5 DIP → 总 10 DIP
                int edge = Theme.S(10);             // 最大化方框 10 DIP
                int gap = Theme.S(1);               // 描边内缩，避免像素糊边

                if (_glyph == Glyph.Minimize)
                {
                    g.DrawLine(pen, cx - half, cy, cx + half, cy);
                }
                else if (_glyph == Glyph.Close)
                {
                    g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                    g.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
                }
                else if (_glyph == Glyph.Restore)
                {
                    int s = Theme.S(8);             // 两个 8 DIP 圆角方框错开 3 DIP
                    int off = Theme.S(3);
                    int rr = Theme.S(2);
                    DrawRoundRect(g, pen, cx - s / 2 + off, cy - s / 2 + off, s - gap, s - gap, rr);
                    DrawRoundRect(g, pen, cx - s / 2 - off + gap, cy - s / 2 - off + gap, s - gap, s - gap, rr);
                }
                else
                {
                    // 最大化：圆角方框
                    int rr = Theme.S(2);
                    DrawRoundRect(g, pen, cx - edge / 2, cy - edge / 2, edge - gap, edge - gap, rr);
                }
            }
        }
    }
}
