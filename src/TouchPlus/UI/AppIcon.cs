using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TouchPlus.Core;

namespace TouchPlus.UI
{
    /// <summary>程序图标：优先用 exe 内嵌图标，取不到时用代码画一个（保证永不缺图标）。</summary>
    public static class AppIcon
    {
        private static Icon _cached;

        public static Icon Get()
        {
            if (_cached != null) return _cached;

            try
            {
                // 优先用编译时嵌入的 .ico 资源：alpha 通道权威，不受系统图标提取影响
                System.IO.Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream("TouchPlus.ico");
                if (stream != null)
                {
                    using (stream)
                    {
                        Icon embedded = new Icon(stream, 32, 32);
                        _cached = (Icon)embedded.Clone();
                        return _cached;
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                string exe = Application.ExecutablePath;
                Icon extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null)
                {
                    _cached = extracted;
                    return _cached;
                }
            }
            catch (Exception)
            {
            }

            _cached = Draw(64);
            return _cached;
        }

        /// <summary>代码绘制图标：圆角渐变方块 + "T+"。</summary>
        public static Icon Draw(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    int radius = Math.Max(2, size / 5);
                    using (GraphicsPath body = RoundedPath(new Rectangle(0, 0, size - 1, size - 1), radius))
                    using (SolidBrush ink = new SolidBrush(Color.FromArgb(24, 27, 30)))
                        g.FillPath(ink, body);

                    Color paper = Color.FromArgb(240, 241, 236);
                    float inner = size * 0.54f;
                    RectangleF zone = new RectangleF((size - inner) / 2f, (size - inner) / 2f, inner, inner);
                    using (Pen dash = new Pen(paper, Math.Max(1f, size * 0.045f)))
                    {
                        dash.DashStyle = DashStyle.Dash;
                        dash.DashPattern = new float[] { 2.2f, 1.8f };
                        g.DrawRectangle(dash, zone.X, zone.Y, zone.Width, zone.Height);
                    }

                    float dotR = Math.Max(1.4f, size * 0.075f);
                    PointF a = new PointF(zone.X + zone.Width * 0.32f, zone.Y + zone.Height * 0.62f);
                    PointF b = new PointF(zone.X + zone.Width * 1.02f, zone.Y + zone.Height * 0.28f);
                    using (Pen link = new Pen(Color.FromArgb(190, paper), Math.Max(1f, size * 0.028f)))
                        g.DrawLine(link, a, b);
                    using (SolidBrush ok = new SolidBrush(Color.FromArgb(47, 190, 138)))
                        g.FillEllipse(ok, a.X - dotR, a.Y - dotR, dotR * 2, dotR * 2);
                    using (SolidBrush bad = new SolidBrush(Color.FromArgb(216, 88, 74)))
                        g.FillEllipse(bad, b.X - dotR, b.Y - dotR, dotR * 2, dotR * 2);
                }
                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeIcon.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static class NativeIcon
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool DestroyIcon(IntPtr hIcon);
        }
    }
}
