using System;
using System.Drawing;
using System.Windows.Forms;

namespace TouchPlus.UI
{
    /// <summary>托盘图标与右键菜单（深色渲染）。</summary>
    public sealed class TrayIconManager : IDisposable
    {
        public event EventHandler OpenRequested;
        public event EventHandler ToggleRequested;
        public event EventHandler ExitRequested;
        public event EventHandler AutostartRequested;
        public event EventHandler SystemLayerRequested;
        public event EventHandler HookLayerRequested;

        private readonly NotifyIcon _notify;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _itemToggle;
        private readonly ToolStripMenuItem _itemSystem;
        private readonly ToolStripMenuItem _itemHook;
        private readonly ToolStripMenuItem _itemAutostart;

        public TrayIconManager()
        {
            _menu = new ContextMenuStrip();
            _menu.Renderer = new DarkRenderer();
            _menu.BackColor = Theme.Card;
            _menu.ForeColor = Theme.Text;
            _menu.ShowImageMargin = false;
            _menu.Font = Theme.Body;

            _itemToggle = AddItem("暂停双击修复", delegate { Raise(ToggleRequested); });
            _itemToggle.Font = Theme.BodyBold;
            _menu.Items.Add(new ToolStripSeparator());
            _itemSystem = AddItem("系统双击参数优化", delegate { Raise(SystemLayerRequested); });
            _itemHook = AddItem("输入层智能修复", delegate { Raise(HookLayerRequested); });
            _itemAutostart = AddItem("开机自动启动", delegate { Raise(AutostartRequested); });
            _menu.Items.Add(new ToolStripSeparator());
            AddItem("打开 Touch+ 主界面", delegate { Raise(OpenRequested); });
            AddItem("退出", delegate { Raise(ExitRequested); });

            _notify = new NotifyIcon();
            _notify.Icon = AppIcon.Get();
            _notify.Text = "Touch+ 触控屏双击修复";
            _notify.ContextMenuStrip = _menu;
            _notify.Visible = false;
            _notify.DoubleClick += delegate { Raise(OpenRequested); };
        }

        private ToolStripMenuItem AddItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Theme.Text;
            item.BackColor = Theme.Card;
            item.Click += delegate { if (handler != null) handler(this, EventArgs.Empty); };
            _menu.Items.Add(item);
            return item;
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public bool Visible
        {
            get { return _notify.Visible; }
            set { _notify.Visible = value; }
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                _notify.BalloonTipTitle = title;
                _notify.BalloonTipText = text;
                _notify.BalloonTipIcon = ToolTipIcon.Info;
                _notify.ShowBalloonTip(3000);
            }
            catch (Exception)
            {
            }
        }

        public void Update(bool patchRunning, bool systemLayer, bool hookLayer, bool autostart, string tooltip)
        {
            _itemToggle.Text = patchRunning ? "暂停双击修复" : "启用双击修复";
            _itemSystem.Checked = systemLayer;
            _itemHook.Checked = hookLayer;
            _itemAutostart.Checked = autostart;
            _notify.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            try { _notify.Icon = AppIcon.Get(); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            try
            {
                _notify.Visible = false;
                _notify.Dispose();
                _menu.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ? Theme.Text : (e.Item.Enabled ? Theme.Text : Theme.TextFaint);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (SolidBrush brush = new SolidBrush(Theme.Card))
                {
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Line))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                }
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Selected)
                {
                    using (SolidBrush brush = new SolidBrush(Theme.CardHover))
                    {
                        e.Graphics.FillRectangle(brush, new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
                    }
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Line))
                {
                    int y = e.Item.Height / 2;
                    e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
                }
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Accent, 2f))
                {
                    Rectangle r = new Rectangle(6, e.Item.Height / 2 - 5, 10, 6);
                    e.Graphics.DrawLines(pen, new Point[]
                    {
                        new Point(r.Left, r.Top + 2),
                        new Point(r.Left + 3, r.Bottom - 1),
                        new Point(r.Right, r.Top - 2)
                    });
                }
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.CardHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
            public override Color MenuItemBorder { get { return Theme.AccentSoft; } }
            public override Color MenuBorder { get { return Theme.Line; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
            public override Color SeparatorDark { get { return Theme.Line; } }
            public override Color SeparatorLight { get { return Theme.Line; } }
        }
    }
}
