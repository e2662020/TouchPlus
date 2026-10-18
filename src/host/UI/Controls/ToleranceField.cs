using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using TouchPlus.Core;

namespace TouchPlus.UI.Controls
{
    public sealed class TestTapEventArgs : EventArgs
    {
        public Point Point;
        public DateTime At;
    }

    /// <summary>
    /// 容差场 —— 这个程序的记忆点。
    ///
    /// 在方格纸上按比例画出 Windows 的双击判定矩形，把最近几次真实双击的两个落点标出来：
    /// 矩形内 = 系统认（合规绿），矩形外 = 系统不认（越界红）。
    /// 用户一眼就能看懂"为什么双击打不开"以及"补丁把容差放宽了多少"。
    /// </summary>
    public sealed class ToleranceField : Control
    {
        private sealed class PairMark
        {
            public PointF A;
            public PointF B;
            public int Distance;
            public uint DeltaMs;
            public Color Color;
            public DateTime At;
        }

        private const int CS_DBLCLKS = 0x0008;
        private const int MaxMarks = 7;

        private readonly List<PairMark> _marks = new List<PairMark>();
        private readonly Timer _timer;

        private bool _hasPair;
        private int _distance;
        private uint _deltaMs;
        private string _verdict = "";
        private Color _verdictColor = Theme.TextDim;
        private bool _nativeDoubleClick;

        public int NativeDoubleClicks;

        /* 本地配对：不依赖输入层钩子，自己测量两次点按的偏离与间隔 */
        private Point _prevTap;
        private DateTime _prevAt = DateTime.MinValue;
        private bool _hasPrevTap;
        private int _doubleClickBaseline;
        public int Taps;

        public event EventHandler<TestTapEventArgs> Tapped;

        public ToleranceField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            BackColor = Theme.Well;

            _timer = new Timer();
            _timer.Interval = 40;
            _timer.Tick += delegate
            {
                bool alive = false;
                for (int i = _marks.Count - 1; i >= 0; i--)
                {
                    if ((DateTime.Now - _marks[i].At).TotalSeconds > 6) _marks.RemoveAt(i);
                    else alive = true;
                }
                if (!alive) _timer.Stop();
                Invalidate();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DBLCLKS;   // 否则收不到 WM_LBUTTONDBLCLK，就测不出"系统认不认"
                return cp;
            }
        }

        public void Reset()
        {
            _hasPair = false;
            _marks.Clear();
            NativeDoubleClicks = 0;
            Taps = 0;
            _verdict = "";
            Invalidate();
        }

        /// <summary>引擎报来一次成对点击：把两个落点按比例画进容差场。</summary>
        public void ReportPair(PairReport report, bool nativeDoubleClick)
        {
            if (report == null || !report.HasPair) return;

            _hasPair = true;
            _distance = report.Distance;
            _deltaMs = report.DeltaMs;
            _nativeDoubleClick = nativeDoubleClick;

            if (report.NativeOk || nativeDoubleClick)
            {
                _verdict = "系统已识别为双击";
                _verdictColor = Theme.Ok;
            }
            else if (report.Repaired)
            {
                _verdict = "补丁已修复，系统识别为双击";
                _verdictColor = Theme.Ok;
            }
            else
            {
                _verdict = "系统未识别（偏离超出判定范围）";
                _verdictColor = Theme.Err;
            }

            int rect = Math.Max(4, DoubleClickParams.GetRectWidth());
            bool inside = report.Distance <= rect;
            Color markColor = inside ? Theme.Ok : Theme.Err;

            PointF a = MapToField(new Point(report.X1, report.Y1), rect);
            PointF b = MapToField(new Point(report.X2, report.Y2), rect);

            PairMark mark = new PairMark();
            mark.A = a;
            mark.B = b;
            mark.Distance = report.Distance;
            mark.DeltaMs = report.DeltaMs;
            mark.Color = markColor;
            mark.At = DateTime.Now;
            _marks.Add(mark);
            while (_marks.Count > MaxMarks) _marks.RemoveAt(0);

            _timer.Start();
            Invalidate();
        }

        /// <summary>
        /// 本地配对报告：不经过引擎/钩子，直接把这次点按画进容差场。
        /// 这样即使"输入层"关着（默认），自检区也能测出抖动并显示系统有没有识别。
        /// </summary>
        private void ReportLocalPair(Point a, Point b, TimeSpan elapsed, bool nativeRecognized)
        {
            int rect = Math.Max(4, DoubleClickParams.GetRectWidth());
            Rectangle field = FieldArea();

            int dx = b.X - a.X;
            int dy = b.Y - a.Y;
            int distance = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));

            // 1:1 实尺：落点就画在被点的位置上，不做任何缩放/重定位，位置必须是真的
            float zoomScale = 1f;
            if (rect * Theme.Scale < Theme.S(20)) zoomScale = Theme.S(20) / (rect * Theme.Scale);
            PointF pa = new PointF(a.X, a.Y);
            PointF pb = new PointF(a.X + dx * zoomScale, a.Y + dy * zoomScale);
            pa = ClampToField(pa, field);
            pb = ClampToField(pb, field);

            bool inside = distance <= rect;
            Color color = nativeRecognized || inside ? Theme.Ok : Theme.Err;

            _hasPair = true;
            _distance = distance;
            _deltaMs = (uint)Math.Round(elapsed.TotalMilliseconds);
            _verdict = nativeRecognized
                ? "系统已识别为双击"
                : (inside ? "判定范围内，但系统没合成双击" : "未识别：偏离超出判定范围");
            _verdictColor = nativeRecognized ? Theme.Ok : (inside ? Theme.Warn : Theme.Err);

            PairMark mark = new PairMark();
            mark.A = pa;
            mark.B = pb;
            mark.Distance = distance;
            mark.DeltaMs = _deltaMs;
            mark.Color = color;
            mark.At = DateTime.Now;
            _marks.Add(mark);
            while (_marks.Count > MaxMarks) _marks.RemoveAt(0);

            _timer.Start();
            Invalidate();
        }

        private static PointF ClampToField(PointF p, Rectangle field)
        {
            return new PointF(
                Math.Max(field.X + 6, Math.Min(field.Right - 6, p.X)),
                Math.Max(field.Y + 6, Math.Min(field.Bottom - 6, p.Y)));
        }

        /// <summary>屏幕坐标 → 场内坐标：以场中心为原点，按"判定矩形占场宽 1/6"的比例缩放。</summary>
        private PointF MapToField(Point screen, int rectPx)
        {
            Point client = PointToClient(screen);
            Rectangle field = FieldArea();
            float cx = field.X + field.Width / 2f;
            float cy = field.Y + field.Height / 2f;
            // 1:1 实尺
            return new PointF(client.X, client.Y);

            // 场中心对应当前光标位置（物理上就是用户点击的位置附近）
            int cursorX, cursorY;
            if (!Injector.GetCursor(out cursorX, out cursorY))
            {
                cursorX = screen.X;
                cursorY = screen.Y;
            }
            Point center = PointToClient(new Point(cursorX, cursorY));

            float k = ZoomOf(rectPx);
            float x = cx + (client.X - center.X) * k;
            float y = cy + (client.Y - center.Y) * k;

            x = Math.Max(field.X + 6, Math.Min(field.Right - 6, x));
            y = Math.Max(field.Y + 6, Math.Min(field.Bottom - 6, y));
            return new PointF(x, y);
        }

        /// <summary>缩放倍数：让判定矩形在场里约占 1/6 宽，同时任何抖动都还看得见。</summary>
        private float ZoomOf(int rectPx)
        {
            Rectangle field = FieldArea();
            if (rectPx <= 0 || field.Width <= 0) return 2f;
            float k = field.Width / (float)(rectPx * 6);
            if (k < 1f) k = 1f;
            if (k > 10f) k = 10f;
            return k;
        }

        private Rectangle FieldArea()
        {
            int top = Theme.S(38);
            int bottom = Height - Theme.S(54);
            Rectangle r = new Rectangle(Theme.S(16), top, Width - Theme.S(32), Math.Max(Theme.S(40), bottom - top));
            return r;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Taps++;
                DateTime now = DateTime.Now;
                if (_hasPrevTap && (now - _prevAt).TotalMilliseconds <= 2000.0)
                {
                    bool native = NativeDoubleClicks > _doubleClickBaseline;
                    ReportLocalPair(_prevTap, e.Location, now - _prevAt, native);
                    _hasPrevTap = false;
                }
                else
                {
                    _prevTap = e.Location;
                    _prevAt = now;
                    _doubleClickBaseline = NativeDoubleClicks;
                    _hasPrevTap = true;
                    Invalidate();
                }
                EventHandler<TestTapEventArgs> handler = Tapped;
                if (handler != null)
                {
                    TestTapEventArgs args = new TestTapEventArgs();
                    args.Point = e.Location;
                    args.At = DateTime.Now;
                    handler(this, args);
                }
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0203) NativeDoubleClicks++;   // WM_LBUTTONDBLCLK
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            // 纸面
            using (SolidBrush paper = new SolidBrush(Theme.Card))
            {
                g.FillRectangle(paper, ClientRectangle);
            }
            using (Pen border = new Pen(Theme.Line, 1f))
            {
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            int rectPx = Math.Max(4, DoubleClickParams.GetRectWidth());
            Rectangle field = FieldArea();

            // 方格纸
            Theme.DrawGrid(g, field, Theme.S(10), Theme.S(50));   // 主格 = 判定框尺寸

            // 判定框锚定在最近一次点击的落点上（1:1 真尺寸），这样"点在不在框里"一眼可判
            float cx = field.X + field.Width / 2f;
            float cy = field.Y + field.Height / 2f;
            if (_marks.Count > 0) { cx = _marks[_marks.Count - 1].A.X; cy = _marks[_marks.Count - 1].A.Y; }
            float k = Theme.Scale;   // 判定范围是逻辑像素，物理绘制要乘 DPI 缩放（200% 下 50px → 100 物理像素）

            // 判定范围很小时（例如系统默认 4×4px）按比例放大显示，否则框只有几个像素、根本看不见
            float zoom = 1f;
            if (rectPx * k < Theme.S(20)) zoom = Theme.S(20) / (rectPx * k);

            float half = rectPx * k * zoom / 2f;
            RectangleF zone = new RectangleF(cx - half, cy - half, half * 2, half * 2);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(18, Theme.Accent)))
            {
                g.FillRectangle(fill, zone);
            }
            using (Pen dash = new Pen(Theme.TextDim, 1f))
            {
                dash.DashStyle = DashStyle.Dash;
                dash.DashPattern = new float[] { 3f, 3f };
                g.DrawRectangle(dash, zone.X, zone.Y, zone.Width, zone.Height);
            }

            // 十字准线
            using (Pen cross = new Pen(Color.FromArgb(70, Theme.TextDim), 1f))
            {
                g.DrawLine(cross, field.X, cy, field.Right, cy);
                g.DrawLine(cross, cx, field.Y, cx, field.Bottom);
            }

            // 历史落点：越早越淡
            for (int i = 0; i < _marks.Count; i++)
            {
                PairMark m = _marks[i];
                int age = _marks.Count - 1 - i;
                int alpha = age == 0 ? 255 : Math.Max(40, 200 - age * 28);

                using (Pen link = new Pen(Color.FromArgb(alpha / 2, m.Color), 1f))
                {
                    g.DrawLine(link, m.A, m.B);
                }
                DrawPoint(g, m.A, Color.FromArgb(alpha, Theme.TextDim), Theme.S(4), false);
                DrawPoint(g, m.B, Color.FromArgb(alpha, m.Color), Theme.S(5), true);
            }

            // 场标题（左上）与比例（右上）
            using (StringFormat left = Theme.FormatLeft())
            {
                Theme.DrawString(g, "容差场", Theme.Tiny, Theme.TextFaint,
                    new Rectangle(field.X, Theme.S(10), Theme.S(80), Theme.S(20)), left);
                string zoomCaption = (rectPx * Theme.Scale < Theme.S(20)
                ? "放大 ×" + (Theme.S(20) / (rectPx * Theme.Scale)).ToString("0.#", CultureInfo.InvariantCulture) + "  ·  "
                : "1:1 实尺  ·  ")
                + "判定范围 " + rectPx + "×" + rectPx + "px";
                Theme.DrawString(g, zoomCaption, Theme.MonoMicro, Theme.TextFaint,
                    new Rectangle(field.X, Theme.S(10), field.Width, Theme.S(20)), Theme.FormatRight());
            }

            // 读数（左下）：距离 + 间隔
            if (_hasPair)
            {
                int readoutY = Height - Theme.S(46);
                using (StringFormat left = Theme.FormatLeft())
                {
                    string dist = _distance.ToString(CultureInfo.InvariantCulture);
                    Rectangle numRect = new Rectangle(field.X, readoutY, Theme.S(120), Theme.S(36));
                    Theme.DrawString(g, dist, Theme.Num, _verdictColor, numRect, left);
                    SizeF numSize = g.MeasureString(dist, Theme.Num);
                    Theme.DrawString(g, "px", Theme.Small, Theme.TextFaint,
                        new Rectangle(field.X + (int)numSize.Width + Theme.S(4), readoutY + Theme.S(12), Theme.S(30), Theme.S(20)), left);
                    Theme.DrawString(g, _deltaMs.ToString(CultureInfo.InvariantCulture) + " ms", Theme.Mono, Theme.TextDim,
                        new Rectangle(field.X + Theme.S(150), readoutY + Theme.S(12), Theme.S(110), Theme.S(20)), left);
                    Theme.DrawString(g, _verdict, Theme.Small, _verdictColor,
                        new Rectangle(field.X + Theme.S(270), readoutY + Theme.S(12), field.Width - Theme.S(270), Theme.S(20)), left);
                }
            }
            else
            {
                int y = field.Y + field.Height / 2 + (int)half + Theme.S(18);
                using (StringFormat center = Theme.FormatCenter())
                {
                    Theme.DrawString(g, "在这里连续点两下，看看两次落点差多少", Theme.Small, Theme.TextFaint,
                        new RectangleF(field.X, y, field.Width, Theme.S(20)), center);
                }
            }

            // 图例（右下）
            using (StringFormat right = Theme.FormatRight())
            {
                int ly = Height - Theme.S(30);
                Theme.DrawString(g, "● 落点      ─ 一次双击      ▭ 系统判定范围", Theme.Tiny, Theme.TextFaint,
                    new Rectangle(field.X, ly, field.Width, Theme.S(18)), right);
            }
        }

        private static void DrawPoint(Graphics g, PointF p, Color color, int radius, bool filled)
        {
            RectangleF r = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
            if (filled)
            {
                using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, r);
            }
            else
            {
                using (Pen pen = new Pen(color, 1.4f)) g.DrawEllipse(pen, r);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
