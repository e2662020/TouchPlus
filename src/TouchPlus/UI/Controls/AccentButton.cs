using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TouchPlus.UI.Controls
{
    public enum ButtonKind
    {
        Primary = 0,
        Ghost = 1,
        Danger = 2,
        Subtle = 3
    }

    /// <summary>圆角扁平按钮，带悬停/按下反馈。</summary>
    public sealed class AccentButton : ThemedControl
    {
        private bool _hover;
        private bool _pressed;
        private ButtonKind _kind = ButtonKind.Ghost;

        public ButtonKind Kind
        {
            get { return _kind; }
            set { _kind = value; Invalidate(); }
        }

        public AccentButton()
        {
            Height = Theme.S(36);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            TabStop = false;
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        private float _hoverAnim;
        private Timer _hoverTimer;

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            StartHoverAnim();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            StartHoverAnim();
            base.OnMouseLeave(e);
        }

        private void StartHoverAnim()
        {
            if (_hoverTimer == null)
            {
                _hoverTimer = new Timer();
                _hoverTimer.Interval = 16;
                _hoverTimer.Tick += delegate
                {
                    float target = _hover ? 1f : 0f;
                    float delta = target - _hoverAnim;
                    if (Math.Abs(delta) < 0.02f) { _hoverAnim = target; _hoverTimer.Stop(); }
                    else _hoverAnim += delta * 0.30f;
                    Invalidate();
                };
            }
            _hoverTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintCore(e);

            // 悬停淡入：所有按钮共用一层极淡的高光，避免颜色瞬间跳变
            float hf = _pressed ? 1f : _hoverAnim;
            if (hf > 0.01f)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(22 * hf), Color.White)))
                {
                    Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width, Height), Theme.S(3), brush);
                }
            }
        }

        private void PaintCore(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Theme.S(2);

            Color fill, border, textColor;
            switch (_kind)
            {
                case ButtonKind.Primary:
                    fill = _pressed ? Theme.Blend(Theme.Accent, Color.Black, 0.22f)
                         : (_hover ? Theme.Blend(Theme.Accent, Color.Black, 0.12f) : Theme.Accent);
                    border = Color.Transparent;
                    textColor = Theme.Card;
                    break;
                case ButtonKind.Danger:
                    fill = _pressed ? Theme.Blend(Theme.ErrSoft, Color.Black, 0.2f)
                         : (_hover ? Theme.Blend(Theme.ErrSoft, Theme.Err, 0.35f) : Theme.ErrSoft);
                    border = Color.FromArgb(90, Theme.Err);
                    textColor = Theme.Err;
                    break;
                case ButtonKind.Subtle:
                    fill = _pressed ? Theme.CardAlt : (_hover ? Theme.CardHover : Color.Transparent);
                    border = Theme.Line;
                    textColor = Theme.TextDim;
                    break;
                default:
                    fill = _pressed ? Theme.CardHover : (_hover ? Theme.CardAlt : Theme.CardAlt);
                    border = _hover ? Theme.Blend(Theme.Line, Theme.Accent, 0.5f) : Theme.Line;
                    textColor = _hover ? Theme.Text : Theme.TextDim;
                    break;
            }

            if (_kind == ButtonKind.Primary)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(r, Theme.Accent, Theme.Accent, 20f))
                {
                    Theme.FillRound(g, r, radius, brush);
                }
            }
            else if (fill != Color.Transparent)
            {
                Theme.FillRound(g, r, radius, fill);
            }

            if (border != Color.Transparent)
            {
                Theme.StrokeRound(g, r, radius, border, 1f);
            }

            if (!Enabled)
            {
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, Theme.Bg)))
                {
                    Theme.FillRound(g, r, radius, dim);
                }
                textColor = Theme.TextFaint;
            }

            using (StringFormat format = Theme.FormatCenter())
            {
                Theme.DrawString(g, Text, Font, textColor, new RectangleF(0, 0, Width, Height), format);
            }
        }
    }
}
