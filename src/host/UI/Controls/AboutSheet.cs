using System;
using System.Drawing;
using System.Windows.Forms;
using TouchPlus.Core;
using TouchPlus.Core.Plugins;

namespace TouchPlus.UI.Controls
{
    /// <summary>
    /// 插件"关于"页的规格表：像计量证书的字段区，而不是一坨纯文本。
    /// 分区靠细线切分，键用弱色小字、值用等宽字，改动项用短横线列表。
    /// </summary>
    public sealed class AboutSheet : ThemedControl
    {
        private IPatchPlugin _plugin;
        private bool _enabled;
        private string _hostVersion = "";
        private string _apiVersion = "";
        private int _rect;
        private int _time;

        public AboutSheet()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        public void Bind(IPatchPlugin plugin, bool enabled, string hostVersion, string apiVersion, int rect, int time)
        {
            _plugin = plugin;
            _enabled = enabled;
            _hostVersion = hostVersion ?? "";
            _apiVersion = apiVersion ?? "";
            _rect = rect;
            _time = time;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush brush = new SolidBrush(Theme.Card)) g.FillRectangle(brush, ClientRectangle);

            Rectangle c = ClientRectangle;
            int pad = Theme.S(12);
            int y = c.Y + Theme.S(6);
            int labelW = Theme.S(104);

            /* 没有插件上下文（例如直接 --settings 打开）时，就讲宿主自己的规格 */
            bool hostOnly = _plugin == null;
            string name = hostOnly ? "Touch+ 宿主" : _plugin.Name;
            string id = hostOnly ? "touchplus.host" : _plugin.Id;
            string safety = hostOnly
                ? "普通用户权限 · 不装服务、不写系统目录"
                : (_plugin.Safety == PluginSafety.Safe
                    ? "安全 · 只改 Windows 参数，可一键还原"
                    : "高级 · 需要全局钩子或合成输入");
            string state = hostOnly ? "运行中" : (_enabled ? "已启用" : "未启用");

            /* 身份区 */
            y = Field(g, c, y, labelW, "对象", name, false);
            y = Field(g, c, y, labelW, "标识", id, true);
            y = Field(g, c, y, labelW, "安全级别", safety, false);
            y = Field(g, c, y, labelW, "状态", state, false);

            y += Theme.S(6);
            Rule(g, c, pad, y);
            y += Theme.S(14);

            /* 它的判定规则 */
            Theme.DrawString(g, "判定规则", Theme.Small, Theme.TextDim,
                new Rectangle(c.X + pad, y, c.Width - pad * 2, Theme.S(20)), Theme.FormatLeft());
            y += Theme.S(24);
            Theme.DrawString(g,
                "Windows 只把落在 " + _rect + "×" + _rect + "px 矩形内、间隔 " + _time + "ms 内的两次点击算作双击。"
                + "手指双击时的落点偏差通常远大于这个矩形，所以系统会把一次双击拆成两次单击。",
                Theme.Small, Theme.TextFaint,
                new Rectangle(c.X + pad, y, c.Width - pad * 2, Theme.S(44)), Theme.FormatLeft());
            y += Theme.S(50);

            Rule(g, c, pad, y);
            y += Theme.S(14);

            /* 它对本机做的改动 */
            Theme.DrawString(g, "对本机做的改动", Theme.Small, Theme.TextDim,
                new Rectangle(c.X + pad, y, c.Width - pad * 2, Theme.S(20)), Theme.FormatLeft());
            y += Theme.S(24);
            y = Bullet(g, c, y, "放宽 Windows 双击矩形与时间窗（HKCU\\Control Panel\\Mouse）");
            y = Bullet(g, c, y, "原值在首次修改前已备份，停用插件或还原默认都会收回");
            y = Bullet(g, c, y, "不写系统目录、不装服务、不驻留驱动、不联网");
            y = Bullet(g, c, y, "输入层钩子默认关闭，只有显式开启才会驻留");

            y += Theme.S(4);
            Rule(g, c, pad, y);
            y += Theme.S(12);

            Theme.DrawString(g, "运行于 Touch+ v" + _hostVersion + " · 插件接口 " + _apiVersion,
                Theme.MonoMicro, Theme.TextFaint,
                new Rectangle(c.X + pad, y, c.Width - pad * 2, Theme.S(18)), Theme.FormatLeft());
        }

        private int Field(Graphics g, Rectangle c, int y, int labelW, string label, string value, bool mono)
        {
            Rectangle labelRect = new Rectangle(c.X + Theme.S(12), y, labelW, Theme.S(22));
            Theme.DrawString(g, label, Theme.Small, Theme.TextFaint, labelRect, Theme.FormatLeft());

            Rectangle valueRect = new Rectangle(c.X + Theme.S(12) + labelW, y, c.Width - labelW - Theme.S(36), Theme.S(22));
            Theme.DrawString(g, value, mono ? Theme.Mono : Theme.Small,
                Theme.Text, valueRect, Theme.FormatLeft());
            return y + Theme.S(24);
        }

        private int Bullet(Graphics g, Rectangle c, int y, string text)
        {
            Rectangle rect = new Rectangle(c.X + Theme.S(12), y, c.Width - Theme.S(24), Theme.S(22));
            Theme.DrawString(g, "· " + text, Theme.Small, Theme.TextDim, rect, Theme.FormatLeft());
            return y + Theme.S(23);
        }

        private void Rule(Graphics g, Rectangle c, int pad, int y)
        {
            Theme.Rule(g, c.X + pad, y, c.Right - pad);
        }

        private void Rule(Graphics g, int left, int y, int right)
        {
            using (Pen pen = new Pen(Theme.Line, 1f))
                g.DrawLine(pen, left, y, right, y);
        }
    }
}
