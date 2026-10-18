using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TouchPlus.Core;

namespace TouchPlus.UI.Controls
{
    /// <summary>
    /// 容差场（WPF 渲染）。
    ///
    /// 换掉 GDI+ 自绘的原因：这个控件是每帧都在动的可视化，GDI+ 是 CPU 逐像素抗锯齿；
    /// WPF 走 DirectX（D3D9Ex）+ DWM 合成，Win7 SP1 到 Win11 都有，不需要任何第三方库。
    ///
    /// 宿主方式：WinForms 控件里创建 HwndSource 子窗口承载 WPF 视觉树（不需要 WindowsFormsIntegration）。
    /// </summary>
    public sealed class WpfToleranceField : System.Windows.Forms.Control
    {
        private sealed class Mark
        {
            public double X1, Y1, X2, Y2;
            public int Distance;
            public uint DeltaMs;
            public bool Ok;
            public DateTime At;
        }

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        private HwndSource _source;
        private Canvas _root;
        private System.Windows.Shapes.Rectangle _zone;
        private TextBlock _distanceText;
        private TextBlock _unitText;
        private TextBlock _deltaText;
        private TextBlock _verdictText;
        private TextBlock _hintText;
        private readonly List<Mark> _marks = new List<Mark>();
        private readonly List<UIElement> _markVisuals = new List<UIElement>();

        private System.Drawing.Point _prevTap;
        private DateTime _prevAt = DateTime.MinValue;
        private bool _hasPrevTap;
        private int _doubleClickBaseline;
        private bool _lastNative;
        private int _lastDistance;
        private uint _lastDelta;
        private System.Windows.Threading.DispatcherTimer _fadeTimer;

        public int NativeDoubleClicks;
        public int Taps;

        public event EventHandler<TestTapEventArgs> Tapped;

        public WpfToleranceField()
        {
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
                     System.Windows.Forms.ControlStyles.Opaque, true);
            BackColor = Theme.Well;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CreateWpfSurface();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
            if (IsHandleCreated) CreateWpfSurface();
        }

        /// <summary>WPF 视觉树：一个 Canvas，所有图形都是矢量的，由 GPU 合成。</summary>
        private void CreateWpfSurface()
        {
            try
            {
                HwndSourceParameters p = new HwndSourceParameters("TouchPlusToleranceField");
                p.ParentWindow = Handle;
                p.WindowStyle = WS_CHILD | WS_VISIBLE;
                p.Width = Math.Max(1, Width);
                p.Height = Math.Max(1, Height);
                _source = new HwndSource(p);
                _source.AddHook(WndProcHook);

                _root = new Canvas();
                _root.Background = Brush(Theme.Card);
                _root.IsHitTestVisible = true;
                _root.MouseLeftButtonDown += OnRootMouseDown;
                _source.RootVisual = _root;

                BuildStatic();
                RenderMarks();
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex, "创建 WPF 容差场失败");
            }
        }

        private static SolidColorBrush Brush(System.Drawing.Color c)
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        }

        private double Dip(double physical)
        {
            return Theme.Scale > 0.01f ? physical / Theme.Scale : physical;
        }

        /// <summary>静态层：方格纸 + 判定框 + 读数占位。只在尺寸/主题变化时重建。</summary>
        private void BuildStatic()
        {
            if (_root == null) return;
            _root.Children.Clear();

            double w = Dip(Width), h = Dip(Height);

            // 方格纸：主格 50 DIP（= 判定框大小），次格 10 DIP
            GeometryDrawing minor = new GeometryDrawing(null,
                new System.Windows.Media.Pen(Brush(System.Drawing.Color.FromArgb(90, Theme.Grid)), 1),
                new RectangleGeometry(new System.Windows.Rect(0, 0, 10, 10)));
            DrawingBrush minorBrush = new DrawingBrush(minor);
            minorBrush.TileMode = TileMode.Tile;
            minorBrush.Viewport = new System.Windows.Rect(0, 0, 10, 10);
            minorBrush.ViewportUnits = BrushMappingMode.Absolute;
            System.Windows.Shapes.Rectangle grid = new System.Windows.Shapes.Rectangle();
            grid.Width = w; grid.Height = h;
            grid.Fill = minorBrush;
            _root.Children.Add(grid);

            // 判定框：50 DIP × 50 DIP，虚线，锚定在场中心（有落点时跟随最新落点）
            _zone = new System.Windows.Shapes.Rectangle();
            _zone.Width = 50;
            _zone.Height = 50;
            _zone.Stroke = Brush(Theme.TextDim);
            _zone.StrokeThickness = 1;
            _zone.StrokeDashArray = new DoubleCollection(new double[] { 3, 3 });
            _zone.Fill = Brush(System.Drawing.Color.FromArgb(18, Theme.Accent));
            Canvas.SetLeft(_zone, w / 2 - 25);
            Canvas.SetTop(_zone, h / 2 - 25);
            _root.Children.Add(_zone);

            // 读数
            _distanceText = MakeText(26, FontWeights.Bold, "Consolas", Theme.Text);
            _unitText = MakeText(11, FontWeights.Normal, "Microsoft YaHei UI", Theme.TextFaint);
            _deltaText = MakeText(12, FontWeights.Normal, "Consolas", Theme.TextDim);
            _verdictText = MakeText(11, FontWeights.Normal, "Microsoft YaHei UI", Theme.TextDim);
            _hintText = MakeText(11, FontWeights.Normal, "Microsoft YaHei UI", Theme.TextFaint);

            Canvas.SetLeft(_distanceText, 14);
            Canvas.SetTop(_distanceText, h - 44);
            Canvas.SetLeft(_unitText, 60);
            Canvas.SetTop(_unitText, h - 32);
            Canvas.SetLeft(_deltaText, 96);
            Canvas.SetTop(_deltaText, h - 32);
            Canvas.SetLeft(_verdictText, 190);
            Canvas.SetTop(_verdictText, h - 32);
            Canvas.SetLeft(_hintText, 14);
            Canvas.SetTop(_hintText, h / 2 + 18);

            _root.Children.Add(_distanceText);
            _root.Children.Add(_unitText);
            _root.Children.Add(_deltaText);
            _root.Children.Add(_verdictText);
            _root.Children.Add(_hintText);

            _hintText.Text = "在这里连续点两下，看看两次落点差多少";
            _distanceText.Text = "--";
        }

        private TextBlock MakeText(double size, FontWeight weight, string family, System.Drawing.Color color)
        {
            TextBlock t = new TextBlock();
            t.FontSize = size;
            t.FontWeight = weight;
            t.FontFamily = new System.Windows.Media.FontFamily(family);
            t.Foreground = Brush(color);
            return t;
        }

        /// <summary>清理旧的落点图形，按最新数据重画（只重建动态层）。</summary>
        private void RenderMarks()
        {
            if (_root == null) return;
            foreach (UIElement e in _markVisuals) _root.Children.Remove(e);
            _markVisuals.Clear();

            double w = Dip(Width), h = Dip(Height);
            for (int i = 0; i < _marks.Count; i++)
            {
                Mark m = _marks[i];
                int age = _marks.Count - 1 - i;
                double op = age == 0 ? 1.0 : Math.Max(0.25, 0.9 - age * 0.12);
                SolidColorBrush color = Brush(m.Ok ? Theme.Ok : Theme.Err);
                color.Opacity = op;

                Line link = new Line();
                link.X1 = m.X1; link.Y1 = m.Y1; link.X2 = m.X2; link.Y2 = m.Y2;
                link.Stroke = color; link.StrokeThickness = 1.2;
                _root.Children.Add(link);
                _markVisuals.Add(link);

                Ellipse a = new Ellipse();
                a.Width = 8; a.Height = 8;
                a.Stroke = Brush(Theme.TextDim); a.StrokeThickness = 1.2;
                a.Opacity = op;
                Canvas.SetLeft(a, m.X1 - 4); Canvas.SetTop(a, m.Y1 - 4);
                _root.Children.Add(a);
                _markVisuals.Add(a);

                Ellipse b = new Ellipse();
                b.Width = 10; b.Height = 10;
                b.Fill = color;
                Canvas.SetLeft(b, m.X2 - 5); Canvas.SetTop(b, m.Y2 - 5);
                _root.Children.Add(b);
                _markVisuals.Add(b);
            }

            if (_marks.Count > 0)
            {
                Mark last = _marks[_marks.Count - 1];
                Canvas.SetLeft(_zone, last.X1 - 25);
                Canvas.SetTop(_zone, last.Y1 - 25);
                _distanceText.Text = last.Distance.ToString();
                _distanceText.Foreground = Brush(last.Ok ? Theme.Ok : Theme.Err);
                _unitText.Text = "px";
                _deltaText.Text = last.DeltaMs + " ms";
                _verdictText.Text = _lastNative ? "系统已识别为双击"
                    : (last.Ok ? "判定范围内，但系统没合成双击" : "未识别：偏离超出判定范围");
                _verdictText.Foreground = Brush(_lastNative ? Theme.Ok : (last.Ok ? Theme.Warn : Theme.Err));
                _hintText.Text = "";
            }
            else
            {
                double cx = w / 2, cy = h / 2;
                Canvas.SetLeft(_zone, cx - 25);
                Canvas.SetTop(_zone, cy - 25);
                _distanceText.Text = "--";
                _unitText.Text = "";
                _deltaText.Text = "";
                _verdictText.Text = "";
                _hintText.Text = "在这里连续点两下，看看两次落点差多少";
            }
        }

        private void OnRootMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Taps++;
            System.Windows.Point pt = e.GetPosition(_root);
            double scale = Theme.Scale > 0.01f ? Theme.Scale : 1.0;
            System.Drawing.Point physical = new System.Drawing.Point((int)(pt.X * scale), (int)(pt.Y * scale));

            DateTime now = DateTime.Now;
            if (_hasPrevTap && (now - _prevAt).TotalMilliseconds <= 2000.0)
            {
                _lastNative = NativeDoubleClicks > _doubleClickBaseline;
                AddPair(_prevTap, physical, now - _prevAt, _lastNative);
                _hasPrevTap = false;
            }
            else
            {
                _prevTap = physical;
                _prevAt = now;
                _doubleClickBaseline = NativeDoubleClicks;
                _hasPrevTap = true;
            }

            EventHandler<TestTapEventArgs> handler = Tapped;
            if (handler != null)
            {
                TestTapEventArgs args = new TestTapEventArgs();
                args.Point = physical;
                args.At = now;
                handler(this, args);
            }
        }

        private void AddPair(System.Drawing.Point a, System.Drawing.Point b, TimeSpan elapsed, bool native)
        {
            int rect = Math.Max(4, DoubleClickParams.GetRectWidth());
            int dx = b.X - a.X;
            int dy = b.Y - a.Y;
            int distance = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));
            bool inside = distance <= rect;

            Mark m = new Mark();
            m.X1 = Dip(a.X); m.Y1 = Dip(a.Y);
            m.X2 = Dip(b.X); m.Y2 = Dip(b.Y);
            m.Distance = distance;
            m.DeltaMs = (uint)Math.Round(elapsed.TotalMilliseconds);
            m.Ok = inside || native;
            m.At = DateTime.Now;
            _marks.Add(m);
            while (_marks.Count > 7) _marks.RemoveAt(0);

            _lastDistance = distance;
            _lastDelta = m.DeltaMs;
            RenderMarks();

            if (_fadeTimer == null)
            {
                _fadeTimer = new System.Windows.Threading.DispatcherTimer();
                _fadeTimer.Interval = TimeSpan.FromMilliseconds(600);
                _fadeTimer.Tick += delegate
                {
                    bool alive = false;
                    for (int i = _marks.Count - 1; i >= 0; i--)
                    {
                        if ((DateTime.Now - _marks[i].At).TotalSeconds > 8) _marks.RemoveAt(i);
                        else alive = true;
                    }
                    RenderMarks();
                    if (!alive && _fadeTimer != null) _fadeTimer.Stop();
                };
            }
            _fadeTimer.Start();
        }

        /// <summary>钩子/WM_LBUTTONDBLCLK：判断系统有没有真的把两次点击合成双击。</summary>
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0203) NativeDoubleClicks++;   // WM_LBUTTONDBLCLK
            return IntPtr.Zero;
        }

        public void Reset()
        {
            _marks.Clear();
            _hasPrevTap = false;
            NativeDoubleClicks = 0;
            Taps = 0;
            _lastNative = false;
            RenderMarks();
        }

        /// <summary>引擎（输入层开启时）报来的配对：换算成控件内坐标后绘制。</summary>
        public void ReportPair(PairReport report, bool nativeDoubleClick)
        {
            if (report == null || !report.HasPair) return;
            System.Drawing.Point a = PointToClient(new System.Drawing.Point(report.X1, report.Y1));
            System.Drawing.Point b = PointToClient(new System.Drawing.Point(report.X2, report.Y2));
            _lastNative = nativeDoubleClick || report.NativeOk;
            AddPair(a, b, TimeSpan.FromMilliseconds(report.DeltaMs), _lastNative);
        }

        /// <summary>主题切换时重建静态层（WPF 画刷不会自动跟随 WinForms 主题）。</summary>
        public void ApplyTheme()
        {
            BackColor = Theme.Well;
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
                CreateWpfSurface();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _source != null)
            {
                try { _source.Dispose(); }
                catch (Exception) { }
                _source = null;
            }
            base.Dispose(disposing);
        }
    }
}
