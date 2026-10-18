using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TouchPlus.UI.Controls
{
    /// <summary>扁平风格开关，带滑动动画。</summary>
    public sealed class ToggleSwitch : ThemedControl
    {
        private bool _checked;
        private float _position;      // 0..1
        private float _target;
        private readonly Timer _timer;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            Size = new Size(Theme.S(56), Theme.S(30));
            Cursor = Cursors.Hand;
            TabStop = false;

            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += delegate
            {
                float diff = _target - _position;
                if (Math.Abs(diff) < 0.02f)
                {
                    _position = _target;
                    _timer.Stop();
                }
                else
                {
                    _position += diff * 0.28f;
                }
                Invalidate();
            };
            _position = 0f;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _target = value ? 1f : 0f;
                _timer.Start();
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public void SetCheckedSilently(bool value)
        {
            _checked = value;
            _target = value ? 1f : 0f;
            _position = _target;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Checked = !Checked;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            int h = Theme.S(26);
            int w = Math.Max(Theme.S(44), Width - Theme.S(2));
            Rectangle track = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
            int radius = h / 2;

            Color off = _hover ? Theme.Blend(Theme.Track, Theme.Line, 0.6f) : Theme.Track;
            Color on1 = Theme.ToggleTrackOn;
            Color on2 = Theme.ToggleTrackOn;

            Color baseColor = Theme.Blend(off, on1, _position);
            using (LinearGradientBrush brush = new LinearGradientBrush(track,
                Theme.Blend(off, on2, _position), baseColor, 0f))
            {
                Theme.FillRound(g, track, radius, brush);
            }



            int knobSize = h - Theme.S(6);
            int travel = track.Width - knobSize - Theme.S(6);
            int knobX = track.X + Theme.S(3) + (int)(travel * _position);
            Rectangle knob = new Rectangle(knobX, track.Y + Theme.S(3), knobSize, knobSize);

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillEllipse(shadow, knob.X, knob.Y + Theme.S(1), knob.Width, knob.Height);
            }
            using (SolidBrush brush = new SolidBrush(Theme.Card))
            {
                g.FillEllipse(brush, knob);
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
