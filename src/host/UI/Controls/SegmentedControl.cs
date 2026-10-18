using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace TouchPlus.UI.Controls
{
    /// <summary>分段选择器（互斥选项卡）。</summary>
    public sealed class SegmentedControl : ThemedControl
    {
        private readonly List<string> _items = new List<string>();
        private int _selected;
        private int _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedControl()
        {
            Height = Theme.S(34);
            Cursor = Cursors.Hand;
            Font = Theme.Small;
        }

        public void SetItems(params string[] items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            if (_selected >= _items.Count) _selected = 0;
            Invalidate();
        }

        public void SetEnabledState(bool enabled, string tooltip)
        {
            Enabled = enabled;
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v >= _items.Count) v = _items.Count - 1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public void SetSelectedSilently(int index)
        {
            if (index < 0) index = 0;
            if (index >= _items.Count) index = _items.Count - 1;
            _selected = index;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.X);
            if (index != _hover) { _hover = index; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int index = IndexAt(e.X);
            if (index >= 0) SelectedIndex = index;
            base.OnMouseDown(e);
        }

        private int IndexAt(int x)
        {
            if (_items.Count == 0 || Width <= 0) return -1;
            int seg = Width / _items.Count;
            if (seg <= 0) return -1;
            int index = x / seg;
            if (index < 0) index = 0;
            if (index >= _items.Count) index = _items.Count - 1;
            return index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(2), Theme.Card);
            Theme.StrokeRound(g, r, Theme.S(2), Theme.Line, 1f);

            if (_items.Count == 0) return;
            int seg = Width / _items.Count;
            int pad = Theme.S(3);

            for (int i = 0; i < _items.Count; i++)
            {
                Rectangle cell = new Rectangle(i * seg + pad, pad, seg - pad * 2, Height - pad * 2);
                bool selected = i == _selected;

                if (selected)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(cell, Theme.Accent, Theme.Accent2, 0f))
                    {
                        Theme.FillRound(g, cell, Theme.S(2), brush);
                    }
                }
                else if (i == _hover && Enabled)
                {
                    Theme.FillRound(g, cell, Theme.S(3), Theme.CardHover);
                }

                Color textColor = selected ? Theme.Card : (Enabled ? Theme.TextDim : Theme.TextFaint);
                using (StringFormat format = Theme.FormatCenter())
                {
                    Theme.DrawString(g, _items[i], Font, textColor,
                        new RectangleF(cell.X, cell.Y, cell.Width, cell.Height), format);
                }
            }
        }
    }

    /// <summary>自绘日志列表：支持滚动、自动滚到底、双击复制。</summary>
    public sealed class LogView : ThemedControl
    {
        public sealed class Row
        {
            public string Time;
            public string Level;
            public string Text;
            public Color Color;
        }

        private readonly List<Row> _rows = new List<Row>();
        private int _scroll;
        private bool _autoScroll = true;
        private int _hoverRow = -1;
        /// <summary>
        /// 行高由字体实测高度推导：字体多大行就多高，杜绝"行高小于字高"造成的裁切。
        /// 不用构造期缓存，因为构造时 DPI 可能尚未就绪。
        /// </summary>
        private int RowHeight
        {
            get
            {
                int textH = TextRenderer.MeasureText("国Ag", Theme.Small).Height;
                return Math.Max(Theme.S(24), textH + Theme.S(12));
            }
        }

        public LogView()
        {
            BackColor = Theme.Card;
        }

        public bool AutoScrollToEnd
        {
            get { return _autoScroll; }
            set { _autoScroll = value; Invalidate(); }
        }

        public void Add(string time, string level, string text, Color color)
        {
            Row row = new Row();
            row.Time = time;
            row.Level = level;
            row.Text = text;
            row.Color = color;
            _rows.Add(row);
            if (_rows.Count > 2000) _rows.RemoveRange(0, 500);
            if (_autoScroll) _scroll = int.MaxValue;
            Invalidate();
        }

        public void Clear()
        {
            _rows.Clear();
            _scroll = 0;
            Invalidate();
        }

        public int RowCount { get { return _rows.Count; } }

        public string[] SnapshotLines()
        {
            List<string> lines = new List<string>();
            foreach (Row r in _rows)
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} [{1}] {2}", r.Time, r.Level, r.Text));
            return lines.ToArray();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta / 120 * RowHeight * 3);
            base.OnMouseWheel(e);
        }

        private void ScrollBy(int delta)
        {
            _autoScroll = false;
            int max = Math.Max(0, _rows.Count * RowHeight - Height);
            _scroll += delta;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > max) _scroll = max;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = (_scroll + e.Y) / RowHeight;
            if (index != _hoverRow) { _hoverRow = index; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverRow = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int index = (_scroll + e.Y) / RowHeight;
            if (index >= 0 && index < _rows.Count)
            {
                try { Clipboard.SetText(_rows[index].Text); } catch (Exception) { }
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Card);

            if (_rows.Count == 0)
            {
                using (StringFormat format = Theme.FormatCenter())
                {
                    Theme.DrawString(g, "暂无记录", Theme.Small, Theme.TextFaint, new RectangleF(0, 0, Width, Height), format);
                }
                return;
            }

            int rowH = RowHeight;
            int maxScroll = Math.Max(0, _rows.Count * rowH - Height);
            if (_autoScroll) _scroll = maxScroll;
            if (_scroll > maxScroll) _scroll = maxScroll;

            int first = _scroll / rowH;
            int offset = _scroll % rowH;
            int count = Height / rowH + 2;

            for (int i = 0; i < count; i++)
            {
                int index = first + i;
                if (index < 0 || index >= _rows.Count) break;
                Row row = _rows[index];
                int y = i * rowH - offset;

                if (index == _hoverRow)
                {
                    using (SolidBrush brush = new SolidBrush(Theme.CardAlt))
                    {
                        g.FillRectangle(brush, 0, y, Width, rowH);
                    }
                }
                else
                {
                    Theme.Rule(g, Theme.S(12), y + rowH - 1, Width - Theme.S(12));
                }

                // 日志用 GDI 文本（TextRenderer）：等宽数字更锐利，也不会被 GDI+ 的矩形裁切
                Rectangle timeRect = new Rectangle(Theme.S(12), y, Theme.S(106), rowH);
                TextRenderer.DrawText(g, row.Time, Theme.Mono, timeRect, Theme.TextFaint,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                Rectangle textRect = new Rectangle(Theme.S(126), y, Width - Theme.S(142), rowH);
                TextRenderer.DrawText(g, row.Text, Theme.Small, textRect, row.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // 细滚动条
            if (maxScroll > 0)
            {
                int trackHeight = Height;
                int thumbHeight = Math.Max(Theme.S(24), trackHeight * trackHeight / (_rows.Count * rowH));
                int thumbY = (int)((double)_scroll / maxScroll * (trackHeight - thumbHeight));
                Rectangle thumb = new Rectangle(Width - Theme.S(5), thumbY, Theme.S(3), thumbHeight);
                Theme.FillRound(g, thumb, Theme.S(2), Color.FromArgb(90, Theme.Line));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            Invalidate();
            base.OnResize(e);
        }
    }
}
