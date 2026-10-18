using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TouchPlus.UI.Controls
{
    /// <summary>圆角卡片容器：带标题/副标题，内容区用 Padding 控制。</summary>
    public class CardPanel : Panel
    {
        private string _title = "";
        private string _subtitle = "";
        private int _radius = 2;
        private int _titleHeight = 54;
        private bool _accentTop;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            BackColor = Theme.Bg;
            Padding = new Padding(Theme.S(20), Theme.S(_titleHeight), Theme.S(20), Theme.S(16));
        }

        public string Title { get { return _title; } set { _title = value ?? ""; Invalidate(); } }
        public string Subtitle { get { return _subtitle; } set { _subtitle = value ?? ""; Invalidate(); } }
        public int Radius { get { return _radius; } set { _radius = value; Invalidate(); } }
        public bool AccentTop { get { return _accentTop; } set { _accentTop = value; Invalidate(); } }

        public int TitleHeight
        {
            get { return _titleHeight; }
            set
            {
                _titleHeight = value;
                Padding = new Padding(Theme.S(20), Theme.S(value), Theme.S(20), Theme.S(16));
                Invalidate();
            }
        }

        /// <summary>内容区内缩（设计像素）：卡片内所有子控件都要离开边缘，不允许贴边。</summary>
        public int InnerInset = 12;

        public Rectangle ContentBounds
        {
            get
            {
                int inset = Theme.S(InnerInset);
                Rectangle inner = new Rectangle(Padding.Left + inset, Padding.Top + inset,
                    Math.Max(0, Width - Padding.Horizontal - inset * 2),
                    Math.Max(0, Height - Padding.Vertical - inset * 2));
                return inner;
            }
        }

        public void SetTitleHeight(int px)
        {
            TitleHeight = Theme.S(px);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            // 文档区块：不画底色和边框，只用一条顶部规则线 + 一个小墨标（证书的段落感）
            Theme.Rule(g, 0, 0, Width);
            using (SolidBrush mark = new SolidBrush(Theme.Accent))
            {
                g.FillRectangle(mark, 0, 0, Theme.S(2), Theme.S(10));
            }

            int textLeft = Theme.S(20);
            if (_title.Length > 0)
            {
                Rectangle titleRect = new Rectangle(textLeft, Theme.S(14), Width - textLeft - Theme.S(16), Theme.S(22));
                using (StringFormat format = Theme.FormatLeft())
                {
                    Theme.DrawString(g, _title, Theme.H3, Theme.Text, titleRect, format);
                }
                if (_subtitle.Length > 0)
                {
                    Rectangle subRect = new Rectangle(textLeft, Theme.S(32), Width - textLeft - Theme.S(16), Theme.S(18));
                    using (StringFormat format = Theme.FormatLeft())
                    {
                        Theme.DrawString(g, _subtitle, Theme.Small, Theme.TextFaint, subRect, format);
                    }
                }
            }
        }
    }

    /// <summary>数据小卡片：上面一行标签，下面一个大数字。</summary>
    public sealed class StatTile : ThemedControl
    {
        private string _label = "";
        private string _value = "-";
        private string _unit = "";
        private Color _accent = Theme.Accent;

        public StatTile()
        {
            Height = Theme.S(78);
        }

        public string Label { get { return _label; } set { _label = value ?? ""; Invalidate(); } }
        public string Value { get { return _value; } set { _value = value ?? ""; Invalidate(); } }
        public string Unit { get { return _unit; } set { _unit = value ?? ""; Invalidate(); } }
        public Color Accent { get { return _accent; } set { _accent = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            Theme.Rule(g, 0, 0, Width);

            using (StringFormat format = Theme.FormatLeft())
            {
                Theme.DrawString(g, _label, Theme.Small, Theme.TextFaint,
                    new Rectangle(Theme.S(16), Theme.S(10), Width - Theme.S(32), Theme.S(20)), format);
            }

            float valueWidth = Width - Theme.S(28);
            SizeF valueSize = g.MeasureString(_value, Theme.Num);
            using (StringFormat format = Theme.FormatLeft())
            {
                Theme.DrawString(g, _value, Theme.Num, _accent,
                    new Rectangle(Theme.S(16), Theme.S(32), Width - Theme.S(32), Theme.S(34)), format);
            }
            if (_unit.Length > 0)
            {
                int x = Theme.S(14) + (int)valueSize.Width + Theme.S(4);
                using (StringFormat format = Theme.FormatLeft())
                {
                    Theme.DrawString(g, _unit, Theme.Small, Theme.TextFaint,
                        new Rectangle(x, Theme.S(34), Width - x - Theme.S(10), Theme.S(20)), format);
                }
            }
        }
    }

    /// <summary>状态标签（圆角小药丸）。</summary>
    public sealed class PillLabel : ThemedControl
    {
        private Color _fill = Theme.OkSoft;
        private Color _fore = Theme.Ok;

        public PillLabel()
        {
            Height = Theme.S(24);
            Font = Theme.Small;
        }

        public Color Fill { get { return _fill; } set { _fill = value; Invalidate(); } }
        public Color ForeColorOverride { get { return _fore; } set { _fore = value; Invalidate(); } }

        public void SetState(string text, Color fill, Color fore)
        {
            Text = text;
            _fill = fill;
            _fore = fore;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(2), _fill);
            Theme.StrokeRound(g, r, Theme.S(2), Color.FromArgb(120, _fore), 1f);
            using (StringFormat format = Theme.FormatCenter())
            {
                Theme.DrawString(g, Text, Font, _fore, new RectangleF(0, 0, Width, Height), format);
            }
        }
    }
}
