using System;
using System.Drawing;
using System.Windows.Forms;
using TouchPlus.Core;
using TouchPlus.UI.Controls;

namespace TouchPlus.UI
{
    /// <summary>
    /// Touch+ 宿主自己的"关于"——只讲宿主：版本、运行环境、数据目录、插件接口版本。
    /// 具体插件的介绍在插件设置窗口的"关于"页里，两边不混。
    /// </summary>
    public sealed class AboutForm : ChromeForm
    {
        private readonly PluginHostInfo _info;
        private AccentButton _btnClose;

        public AboutForm(PluginHostInfo info)
        {
            _info = info ?? new PluginHostInfo();
            Text = "关于 Touch+";
            AutoScaleMode = AutoScaleMode.None;
            Theme.InitScale(this);
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(520), Theme.S(360));
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Icon = AppIcon.Get();

            _btnClose = new AccentButton();
            _btnClose.Text = "关闭";
            _btnClose.Kind = ButtonKind.Ghost;
            _btnClose.Click += delegate { Close(); };
            Controls.Add(_btnClose);
            _btnClose.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(96), ClientSize.Height - Theme.S(24) - Theme.S(34), Theme.S(96), Theme.S(34));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush brush = new SolidBrush(Theme.Bg)) g.FillRectangle(brush, ClientRectangle);

            // 图标 + 产品名
            using (Bitmap icon = AppIcon.Get().ToBitmap())
            {
                Rectangle target = new Rectangle(Theme.S(24), Theme.S(22), Theme.S(40), Theme.S(40));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, target);
            }
            using (StringFormat left = Theme.FormatLeft())
            {
                Theme.DrawString(g, "Touch+", Theme.H2, Theme.Text,
                    new Rectangle(Theme.S(76), Theme.S(18), Theme.S(300), Theme.S(30)), left);
                Theme.DrawString(g, "希沃触控屏补丁 · 插件宿主", Theme.Small, Theme.TextFaint,
                    new Rectangle(Theme.S(76), Theme.S(46), Theme.S(380), Theme.S(20)), left);
            }

            Rectangle content = new Rectangle(Theme.S(24), Theme.S(84), ClientSize.Width - Theme.S(48), ClientSize.Height - Theme.S(150));
            Theme.Rule(g, content.X, content.Y, content.Right);

            string[] rows = new string[]
            {
                "版本", "v" + Program.Version,
                "系统", SystemInfo.VersionText,
                "插件接口", PluginApiInfo.Version + "（IPatchPlugin）",
                "已装插件", _info.PluginCount + " 个 · 已启用 " + _info.RunningCount + " 个",
                "数据目录", SettingsStore.DataDirectory
            };

            int y = content.Y + Theme.S(14);
            using (StringFormat left = Theme.FormatLeft())
            {
                for (int i = 0; i < rows.Length; i += 2)
                {
                    Theme.DrawString(g, rows[i], Theme.Small, Theme.TextFaint,
                        new Rectangle(content.X, y, Theme.S(90), Theme.S(26)), left);
                    Theme.DrawString(g, rows[i + 1], Theme.Mono, Theme.Text,
                        new Rectangle(content.X + Theme.S(98), y, content.Width - Theme.S(98), Theme.S(26)), left);
                    y += Theme.S(30);
                }
            }

            using (StringFormat left = Theme.FormatLeft())
            {
                Theme.DrawString(g, "插件是独立的小补丁，每个插件自己声明安全级别、自己负责还原对系统的改动。",
                    Theme.Small, Theme.TextDim,
                    new Rectangle(Theme.S(24), ClientSize.Height - Theme.S(96), ClientSize.Width - Theme.S(48), Theme.S(20)), left);
            }
        }
    }

    /// <summary>宿主给"关于"窗口用的只读信息。</summary>
    public sealed class PluginHostInfo
    {
        public int PluginCount;
        public int RunningCount;
    }

    public static class PluginApiInfo
    {
        /// <summary>插件接口版本。插件声明兼容的接口版本，宿主加载时比对。</summary>
        public const string Version = "1.0";
    }
}
