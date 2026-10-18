using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace TouchPlus.UI.Controls
{
    /// <summary>扁平滑块：拖动改值，右侧实时显示数值。</summary>
    public sealed class FlatSlider : ThemedControl
    {
        private int _min = 0;
        private int _max = 100;
        private int _value = 50;
        private int _step = 1;
        private bool _dragging;
        private bool _hover;
        private string _unit = "";

        public event EventHandler ValueChanged;

        public FlatSlider()
        {
            Height = Theme.S(30);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public int Minimum { get { return _min; } set { _min = value; if (_value < _min) Value = _min; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = value; if (_value > _max) Value = _max; Invalidate(); } }
        public int Step { get { return _step; } set { _step = Math.Max(1, value); } }
        public string Unit { get { return _unit; } set { _unit = value ?? ""; Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (_step > 1) v = _min + (int)Math.Round((double)(v - _min) / _step) * _step;
                if (v > _max) v = _max;
                if (_value == v) return;
                _value = v;
                Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public void SetValueSilently(int value)
        {
            _value = Math.Max(_min, Math.Min(_max, value));
            Invalidate();
        }

        private int ValueAreaWidth { get { return Theme.S(66); } }

        private int TrackRight { get { return Math.Max(Theme.S(20), Width - ValueAreaWidth); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Capture = true;
                UpdateFromMouse(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) UpdateFromMouse(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
            }
            base.OnMouseUp(e);
        }

        private void UpdateFromMouse(int x)
        {
            int left = Theme.S(7);
            int right = TrackRight - Theme.S(7);
            if (right <= left) return;
            double ratio = (double)(x - left) / (right - left);
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            Value = _min + (int)Math.Round(ratio * (_max - _min));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            int trackHeight = Theme.S(6);
            int left = Theme.S(7);
            int right = TrackRight;
            int cy = Height / 2;
            Rectangle track = new Rectangle(left, cy - trackHeight / 2, Math.Max(Theme.S(10), right - left), trackHeight);

            Theme.FillRound(g, track, trackHeight / 2, Theme.Track);

            double ratio = _max > _min ? (double)(_value - _min) / (_max - _min) : 0;
            int filled = (int)(track.Width * ratio);
            if (filled > 0)
            {
                Rectangle fill = new Rectangle(track.X, track.Y, filled, track.Height);
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(track.X, track.Y, Math.Max(2, track.Width), track.Height),
                    Theme.Accent, Theme.Accent, 0f))
                {
                    Theme.FillRound(g, fill, trackHeight / 2, brush);
                }
            }

            int knobSize = Theme.S(_dragging || _hover ? 18 : 15);
            int knobX = left + filled;
            Rectangle knob = new Rectangle(knobX - knobSize / 2, cy - knobSize / 2, knobSize, knobSize);

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            {
                g.FillEllipse(shadow, knob.X, knob.Y + Theme.S(1), knob.Width, knob.Height);
            }
            using (SolidBrush brush = new SolidBrush(Theme.Card))
            {
                g.FillEllipse(brush, knob);
            }
            using (Pen pen = new Pen(Theme.Accent, Theme.S(2)))
            {
                g.DrawEllipse(pen, knob.X + Theme.S(3), knob.Y + Theme.S(3), knob.Width - Theme.S(6), knob.Height - Theme.S(6));
            }

            string text = _value.ToString(CultureInfo.InvariantCulture) + _unit;
            Rectangle valRect = new Rectangle(right, 0, Width - right, Height);
            using (StringFormat format = Theme.FormatRight())
            {
                Theme.DrawString(g, text, Theme.BodyBold, Theme.Text, valRect, format);
            }
        }
    }
}
