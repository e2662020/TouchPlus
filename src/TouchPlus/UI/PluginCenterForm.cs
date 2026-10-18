using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TouchPlus.Core;
using TouchPlus.Core.Plugins;
using TouchPlus.Plugins;
using TouchPlus.UI.Controls;

namespace TouchPlus.UI
{
    /// <summary>
    /// Touch+ 主窗口：插件中心。
    ///
    /// 布局按一条明确的栅格来排：
    ///   左右外边距 M；每行内部三列 —— 文本列 / 状态列 / 操作列（右对齐到同一条右边线）。
    /// 添加插件、外观选择都在工具条同一水平线上，避免"每个元素各自飘"。
    /// </summary>
    public sealed class PluginCenterForm : ChromeForm
    {
        private const int HeaderH = 56;
        private const int ToolbarH = 56;
        private const int FooterH = 36;
        private const int RowH = 108;
        private const int Margin = 24;
        private const int StampW = 76;
        private const int ActionW = 64;
        private const int ToggleSize = 24;
        private const int ToggleWidth = 44;
        private const int ActionGap = 16;

        private readonly PluginHost _host;
        private readonly PluginContext _context;
        private readonly TrayIconManager _tray;
        private readonly bool _startMinimized;
        private bool _reallyExit;

        private ChromeButton _btnMin;
        private ChromeButton _btnMax;
        private ChromeButton _btnClose;
        private AccentButton _btnAdd;
        private AccentButton _btnAbout;
        private SegmentedControl _segTheme;

        private int _hoverRow = -1;
        private int _hoverZone;   // 1=设置 2=启用
        private int _pressedRow = -1;
        private float _actionAnim;      // 「设置」按钮 hover 渐入

        /* 动画状态：每行一个，0..1 */
        private float[] _toggleAnim = new float[0];
        private float[] _toggleTarget = new float[0];
        private float[] _hoverAnim = new float[0];
        private float[] _hoverTarget = new float[0];
        private float[] _pulseAnim = new float[0];
        private Timer _animTimer;

        public PluginCenterForm(PluginHost host, PluginContext context, bool startMinimized)
        {
            _host = host;
            _context = context;
            _startMinimized = startMinimized;

            Text = "Touch+ 插件";
            AutoScaleMode = AutoScaleMode.None;
            Theme.InitScale(this);
            Theme.ApplyMode(ParseTheme(_context.Settings.ThemeMode));
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(900), Theme.S(640));
            MinimumSize = new Size(Theme.S(820), Theme.S(560));
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Icon = AppIcon.Get();

            _btnMin = new ChromeButton();
            _btnMin.IconGlyph = ChromeButton.Glyph.Minimize;
            _btnMin.Click += delegate { HideToTray(); };
            Controls.Add(_btnMin);

            _btnMax = new ChromeButton();
            _btnMax.IconGlyph = ChromeButton.Glyph.Maximize;
            _btnMax.Click += delegate
            {
                ToggleMaximize();
                _btnMax.IconGlyph = WindowState == FormWindowState.Maximized
                    ? ChromeButton.Glyph.Restore : ChromeButton.Glyph.Maximize;
            };
            Controls.Add(_btnMax);

            _btnClose = new ChromeButton();
            _btnClose.IconGlyph = ChromeButton.Glyph.Close;
            _btnClose.Danger = true;
            _btnClose.Click += delegate { Close(); };
            Controls.Add(_btnClose);

            _btnAdd = new AccentButton();
            _btnAdd.Text = "添加插件…";
            _btnAdd.Kind = ButtonKind.Primary;
            _btnAdd.Click += delegate { AddPlugin(); };
            Controls.Add(_btnAdd);

            _btnAbout = new AccentButton();
            _btnAbout.Text = "关于 Touch+";
            _btnAbout.Kind = ButtonKind.Ghost;
            _btnAbout.Click += delegate { ShowAbout(); };
            Controls.Add(_btnAbout);



            _segTheme = new SegmentedControl();
            _segTheme.SetItems("跟随系统", "浅色", "深色");
            _segTheme.SelectedIndexChanged += delegate
            {
                if (_settingTheme) return;
                AppSettings s = _context.Settings;
                s.ThemeMode = _segTheme.SelectedIndex == 1 ? "Light" : (_segTheme.SelectedIndex == 2 ? "Dark" : "Auto");
                if (!s.ReadOnlySession) SettingsStore.Save(s);
                Theme.ApplyMode(ParseTheme(s.ThemeMode));
            };
            Controls.Add(_segTheme);

            _tray = new TrayIconManager();
            _tray.AutostartRequested += delegate { ToggleAutostart(); };
            _tray.OpenRequested += delegate { RestoreFromTray(); };
            _tray.ToggleRequested += delegate { ToggleAllPlugins(); };
            _tray.ExitRequested += delegate { ExitApplication(); };
            _tray.Visible = true;
            RefreshTray();

            _host.Changed += delegate { SyncAnim(); RefreshTray(); Invalidate(); };
            Theme.Changed += delegate { OnThemeChanged(); };

            _animTimer = new Timer();
            _animTimer.Interval = 16;
            _animTimer.Tick += delegate { TickAnim(); };

            LayoutChrome();
            SyncThemeSegment();

            if (_startMinimized)
            {
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
            }
        }

        private bool _settingTheme;

        private static Theme.ThemeMode ParseTheme(string text)
        {
            if (string.Equals(text, "Dark", StringComparison.OrdinalIgnoreCase)) return Theme.ThemeMode.Dark;
            if (string.Equals(text, "Light", StringComparison.OrdinalIgnoreCase)) return Theme.ThemeMode.Light;
            return Theme.ThemeMode.Auto;
        }

        private void SyncThemeSegment()
        {
            if (_segTheme == null) return;
            _settingTheme = true;
            _segTheme.SetSelectedSilently(Theme.Mode == Theme.ThemeMode.Dark ? 2 : (Theme.Mode == Theme.ThemeMode.Light ? 1 : 0));
            _settingTheme = false;
        }

        /* ------------------------------------------------------------------ */
        /* 栅格                                                                */
        /* ------------------------------------------------------------------ */

        private int LeftEdge { get { return Theme.S(Margin); } }
        private int RightEdge { get { return ClientSize.Width - Theme.S(Margin); } }

        private int ToggleX { get { return RightEdge - RowPad - Theme.S(ToggleWidth); } }
        private int ToggleW { get { return Theme.S(ToggleWidth); } }
        private int ToggleSize2 { get { return Theme.S(ToggleSize); } }
        private int ActionX { get { return ToggleX - Theme.S(ActionGap) - Theme.S(ActionW); } }
        private int StampX { get { return ActionX - Theme.S(ActionGap) - Theme.S(StampW); } }
        private int TextRight { get { return StampX - Theme.S(16); } }
        private int IconSize2 { get { return Theme.S(40); } }
        /// <summary>行内边距：行内容与行边界之间必须留出呼吸，图标/开关都不许贴边。</summary>
        private int RowPad { get { return Theme.S(16); } }
        private int TextLeft { get { return LeftEdge + RowPad + IconSize2 + Theme.S(14); } }
        private int TextWidth { get { return Math.Max(Theme.S(160), TextRight - TextLeft); } }

        private int RowsTop { get { return HeaderH + ToolbarH; } }

        private Rectangle RowBounds(int index)
        {
            int top = Theme.S(RowsTop) + index * Theme.S(RowH);
            return new Rectangle(LeftEdge, top, RightEdge - LeftEdge, Theme.S(RowH));
        }

        private Rectangle ToggleBounds(Rectangle row)
        {
            int h = Theme.S(ToggleSize);
            return new Rectangle(ToggleX, row.Y + (row.Height - h) / 2, ToggleW, h);
        }

        private Rectangle ActionBounds(Rectangle row)
        {
            return new Rectangle(ActionX, row.Y + (row.Height - Theme.S(28)) / 2, Theme.S(ActionW), Theme.S(28));
        }

        private Rectangle StampBounds(Rectangle row)
        {
            return new Rectangle(StampX, row.Y + (row.Height - Theme.S(24)) / 2, Theme.S(StampW), Theme.S(24));
        }

        /// <summary>插件数量变化时同步动画数组。</summary>
        private void SyncAnim()
        {
            int n = _host.Count;
            if (_toggleAnim.Length == n) return;
            float[] toggle = new float[n], target = new float[n], hover = new float[n], hoverT = new float[n], pulse = new float[n];
            for (int i = 0; i < n; i++)
            {
                toggle[i] = target[i] = _host.Plugins[i].Enabled ? 1f : 0f;
            }
            _toggleAnim = toggle; _toggleTarget = target; _hoverAnim = hover; _hoverTarget = hoverT; _pulseAnim = pulse;
        }

        /// <summary>每帧把当前值往目标值推进（指数逼近，接近"跟手"的阻尼感）。</summary>
        private void TickAnim()
        {
            bool alive = false;
            SyncAnim();
            for (int i = 0; i < _toggleAnim.Length; i++)
            {
                float d = _toggleTarget[i] - _toggleAnim[i];
                if (Math.Abs(d) > 0.004f) { _toggleAnim[i] += d * 0.32f; alive = true; }
                else _toggleAnim[i] = _toggleTarget[i];

                float h = _hoverTarget[i] - _hoverAnim[i];
                if (Math.Abs(h) > 0.01f) { _hoverAnim[i] += h * 0.25f; alive = true; }
                else _hoverAnim[i] = _hoverTarget[i];

                if (_pulseAnim[i] > 0.01f) { _pulseAnim[i] *= 0.86f; alive = true; }
                else _pulseAnim[i] = 0f;

                float actionTarget = (_hoverZone == 1) ? 1f : 0f;
                float actionDelta = actionTarget - _actionAnim;
                if (Math.Abs(actionDelta) > 0.01f) { _actionAnim += actionDelta * 0.30f; alive = true; }
                else _actionAnim = actionTarget;
            }
            // 只让正在动的行失效，不再整窗重绘：2000×1440 全量 GDI+ 重画是掉帧主因
            SyncAnim();
            for (int i = 0; i < _toggleAnim.Length; i++)
            {
                bool moving = Math.Abs(_toggleTarget[i] - _toggleAnim[i]) > 0.004f
                              || Math.Abs(_hoverTarget[i] - _hoverAnim[i]) > 0.01f
                              || _pulseAnim[i] > 0.01f
                              || _pressedRow == i;
                if (moving)
                {
                    Rectangle row = RowBounds(i);
                    Invalidate(new Rectangle(row.X - Theme.S(8), row.Y - Theme.S(8), row.Width + Theme.S(16), row.Height + Theme.S(16)));
                }
            }
            if (!alive && _pressedRow < 0) _animTimer.Stop();
        }

        private void LayoutChrome()
        {
            if (_btnClose == null) return;
            // 三键贴右上角，关闭键整块红底并与窗口圆角对齐（系统风格）
            int bw = Theme.S(ChromeButton.ButtonSize.Width);
            int bh = Theme.S(HeaderH);   // HeaderH 是设计像素，这里缩放一次
            _btnClose.Size = new Size(bw, bh);
            _btnMax.Size = new Size(bw, bh);
            _btnMin.Size = new Size(bw, bh);
            _btnClose.Location = new Point(ClientSize.Width - bw, 0);
            _btnMax.Location = new Point(_btnClose.Left - bw, 0);
            _btnMin.Location = new Point(_btnMax.Left - bw, 0);

            int toolbarY = Theme.S(HeaderH) + (Theme.S(ToolbarH) - Theme.S(34)) / 2;
            if (_btnAdd != null) _btnAdd.SetBounds(RightEdge - Theme.S(124), toolbarY, Theme.S(124), Theme.S(34));
            if (_btnAbout != null) _btnAbout.SetBounds(RightEdge - Theme.S(124) - Theme.S(12) - Theme.S(120), toolbarY, Theme.S(120), Theme.S(34));
            if (_segTheme != null)
            {
                int w = Theme.S(228);
                _segTheme.SetBounds(RightEdge - Theme.S(124) - Theme.S(12) - Theme.S(120) - Theme.S(12) - w, toolbarY, w, Theme.S(34));
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChrome();
            Invalidate();
        }

        private void OnThemeChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(OnThemeChanged)); return; }
            BackColor = Theme.Bg;
            SyncThemeSegment();
            Invalidate(true);
        }

        /* ------------------------------------------------------------------ */
        /* 行为                                                                */
        /* ------------------------------------------------------------------ */

        private void AddPlugin()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择插件包";
                dialog.Filter = "Touch+ 插件 (*.dll)|*.dll|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string error;
                IPatchPlugin plugin = PluginLoader.Load(dialog.FileName, _context, out error);
                if (plugin == null)
                {
                    MessageBox.Show(this, "这个文件不能作为 Touch+ 插件使用：\r\n\r\n" + error,
                        "添加插件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!PluginLoader.CheckCapabilities(plugin, _host.Capabilities, out error))
                {
                    MessageBox.Show(this, "插件「" + plugin.Name + "」要求的权限超出宿主授权：\r\n\r\n" + error,
                        "添加插件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_host.Find(plugin.Id) != null)
                {
                    MessageBox.Show(this, "已经安装过同 ID 的插件：" + plugin.Id,
                        "添加插件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _host.Add(plugin);
                AppSettings s = _context.Settings;
                if (!s.PluginPaths.Contains(dialog.FileName)) s.PluginPaths.Add(dialog.FileName);
                if (!s.ReadOnlySession) SettingsStore.Save(s);
                Log.Instance.Info("已添加插件：{0}（{1}）", plugin.Name, plugin.Id);
                RefreshTray();
                Invalidate();
            }
        }

        /// <summary>开机自启开关（底部状态栏点击 / F2 / 托盘菜单共用）。</summary>
        private void ToggleAutostart()
        {
            bool on = !_context.Settings.AutoStart;
            bool ok = on
                ? Autostart.Enable(Application.ExecutablePath, _context.Settings.StartMinimized)
                : Autostart.Disable();
            _context.Settings.AutoStart = on && ok;
            if (!_context.Settings.ReadOnlySession) SettingsStore.Save(_context.Settings);
            RefreshTray();
            Invalidate();
        }

        private void ShowAbout()
        {
            PluginHostInfo info = new PluginHostInfo();
            info.PluginCount = _host.Count;
            info.RunningCount = _host.RunningCount;
            using (AboutForm form = new AboutForm(info))
            {
                form.ShowDialog(this);
            }
        }

        private void OpenPluginSettings(IPatchPlugin plugin)
        {
            DoubleClickPlugin typed = plugin as DoubleClickPlugin;
            PatchController controller = typed != null ? typed.Controller : _context.Controller;
            if (controller == null) return;
            using (MainForm form = new MainForm(controller, false, plugin))
            {
                form.ShowDialog(this);
            }
            Invalidate();
        }

        private void TogglePlugin(IPatchPlugin plugin)
        {
            if (plugin == null) return;
            string error;
            if (plugin.Enabled)
            {
                _host.SetEnabled(plugin, false, out error);
            }
            else if (!_host.SetEnabled(plugin, true, out error))
            {
                MessageBox.Show(this, "启用失败：\r\n\r\n" + error, "启用插件",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            plugin.SaveConfig();
            Invalidate();
        }

        private void ToggleAllPlugins()
        {
            bool anyRunning = _host.RunningCount > 0;
            string error;
            foreach (IPatchPlugin p in _host.Plugins)
            {
                if (anyRunning && p.Enabled) _host.SetEnabled(p, false, out error);
                if (!anyRunning && !p.Enabled) _host.SetEnabled(p, true, out error);
                p.SaveConfig();
            }
            Invalidate();
        }



        private void RefreshTray()
        {
            if (_tray == null) return;
            _tray.Update(_host.RunningCount > 0, false, false, _context.Settings.AutoStart,
                "Touch+：已启用 " + _host.RunningCount + "/" + _host.Count + " 个插件");
        }

        public void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
        }

        public void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        public void ShowFromOtherInstance()
        {
            RestoreFromTray();
        }

        private void ExitApplication()
        {
            _reallyExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            try
            {
                _host.SaveAll();
                _host.StopAll();
                _tray.Dispose();
            }
            catch (Exception)
            {
            }
            base.OnFormClosing(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _pressedRow >= 0)
            {
                int row = _pressedRow;
                _pressedRow = -1;
                _animTimer.Start();
                Invalidate();
                if (row < _host.Plugins.Length && ToggleBounds(RowBounds(row)).Contains(e.Location))
                    TogglePluginAnimated(_host.Plugins[row], row);
            }
            base.OnMouseUp(e);
        }

        /// <summary>乐观切换：先把动画目标设成新状态（立刻有反馈），失败再弹回。</summary>
        private void TogglePluginAnimated(IPatchPlugin plugin, int index)
        {
            SyncAnim();
            if (index < 0 || index >= _toggleTarget.Length) { TogglePlugin(plugin); return; }

            bool target = !plugin.Enabled;
            _toggleTarget[index] = target ? 1f : 0f;
            _animTimer.Start();
            Invalidate();

            string error;
            bool ok = _host.SetEnabled(plugin, target, out error);
            if (!ok && target)
            {
                _toggleTarget[index] = 0f;
                _animTimer.Start();
                MessageBox.Show(this, "启用失败：\r\n\r\n" + error, "启用插件",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (ok)
            {
                _pulseAnim[index] = 1f;
                _animTimer.Start();
            }
            plugin.SaveConfig();
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F2) { ToggleAutostart(); return true; }
            if (keyData == Keys.Escape) { HideToTray(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /* ------------------------------------------------------------------ */
        /* 绘制：全部元素对齐到同一套 x 坐标                                    */
        /* ------------------------------------------------------------------ */

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            using (SolidBrush brush = new SolidBrush(Theme.Bg)) g.FillRectangle(brush, ClientRectangle);

            // 静态层优化：只画落进本次脏矩形里的区块。
            // 动画时脏矩形只覆盖"正在动的那一行"，页头/工具条/页脚/其它行直接跳过，
            // 省掉大量 DrawString 排版与抗锯齿描边（这是 GDI+ 下最有效的帧率手段）。
            Rectangle clip = e.ClipRectangle;

            /* ---- 头部 ---- */
            if (clip.IntersectsWith(new Rectangle(0, 0, ClientSize.Width, Theme.S(HeaderH))))
            {
            int markSize = Theme.S(24);
            Rectangle mark = new Rectangle(LeftEdge, (Theme.S(HeaderH) - markSize) / 2, markSize, markSize);
            Theme.DrawBrandMark(g, mark);

            using (StringFormat left = Theme.FormatLeft())
            {
                Theme.DrawString(g, "Touch+", Theme.H3, Theme.Text,
                    new Rectangle(mark.Right + Theme.S(10), 0, Theme.S(90), Theme.S(HeaderH)), left);
                int nameX = mark.Right + Theme.S(10) + (int)g.MeasureString("Touch+", Theme.H3).Width + Theme.S(14);
                Theme.DrawString(g, "插件中心", Theme.Small, Theme.TextFaint,
                    new Rectangle(nameX, 0, Theme.S(90), Theme.S(HeaderH)), left);
            }
            Theme.Rule(g, 0, Theme.S(HeaderH), ClientSize.Width);
            }

            /* ---- 工具条 ---- */
            if (clip.IntersectsWith(new Rectangle(0, Theme.S(HeaderH), ClientSize.Width, Theme.S(ToolbarH))))
            {
            using (StringFormat left = Theme.FormatLeft())
            {
                Theme.DrawString(g, _host.Count + " 个插件 · " + _host.RunningCount + " 个已启用",
                    Theme.Small, Theme.TextDim,
                    new Rectangle(LeftEdge + RowPad, Theme.S(HeaderH), Math.Max(Theme.S(200), TextWidth - Theme.S(360)), Theme.S(ToolbarH)), left);
            }
            }

            /* ---- 列表 ---- */
            IPatchPlugin[] plugins = _host.Plugins;
            if (plugins.Length == 0)
            {
                using (StringFormat center = Theme.FormatCenter())
                {
                    Theme.DrawString(g, "还没有安装插件", Theme.H3, Theme.TextDim,
                        new RectangleF(LeftEdge, ClientSize.Height / 2f - Theme.S(46), TextWidth, Theme.S(28)), center);
                    Theme.DrawString(g, "点右上角「添加插件…」选择插件包（.dll）；内置的触控双击容差插件随程序提供。",
                        Theme.Small, Theme.TextFaint,
                        new RectangleF(LeftEdge, ClientSize.Height / 2f - Theme.S(12), TextWidth, Theme.S(22)), center);
                }
            }

            for (int i = 0; i < plugins.Length; i++)
            {
                Rectangle row = RowBounds(i);
                if (!clip.IntersectsWith(new Rectangle(row.X - Theme.S(8), row.Y - Theme.S(8), row.Width + Theme.S(16), row.Height + Theme.S(16))))
                    continue;
                Theme.Rule(g, row.X, row.Y, row.Right);
                float hoverAnim = i < _hoverAnim.Length ? _hoverAnim[i] : 0f;
                if (hoverAnim > 0.02f)
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(70 * hoverAnim), Theme.CardAlt)))
                        g.FillRectangle(brush, new Rectangle(row.X + Theme.S(6), row.Y + 1, row.Width - Theme.S(12), row.Height - 1));
                }

                IPatchPlugin p = plugins[i];

                /* 左侧插件图标 */
                Rectangle iconRect = new Rectangle(LeftEdge + RowPad, row.Y + (row.Height - IconSize2) / 2, IconSize2, IconSize2);
                DrawPluginIcon(g, iconRect, p.IconKey);

                PluginStats stats = null;
                try { stats = p.GetStats(); } catch (Exception) { }

                /* 文本列 */
                using (StringFormat left = Theme.FormatLeft())
                {
                    Theme.DrawString(g, p.Name, Theme.H3, Theme.Text,
                        new Rectangle(TextLeft, row.Y + Theme.S(18), TextWidth, Theme.S(26)), left);
                    int badgeX = TextLeft + (int)g.MeasureString(p.Name, Theme.H3).Width + Theme.S(10);
                    Theme.DrawString(g, p.Safety == PluginSafety.Safe ? "安全" : "高级", Theme.Tiny,
                        p.Safety == PluginSafety.Safe ? Theme.Ok : Theme.Warn,
                        new Rectangle(badgeX, row.Y + Theme.S(21), Theme.S(48), Theme.S(20)), left);
                    Theme.DrawString(g, p.Summary, Theme.Small, Theme.TextDim,
                        new Rectangle(TextLeft, row.Y + Theme.S(50), TextWidth, Theme.S(22)), left);
                    if (stats != null && !string.IsNullOrEmpty(stats.Detail))
                    {
                        Theme.DrawString(g, stats.Detail, Theme.MonoMicro, Theme.TextFaint,
                            new Rectangle(TextLeft, row.Y + Theme.S(76), TextWidth, Theme.S(20)), left);
                    }
                }

                /* 状态列：橡皮图章 */
                Theme.Stamp(g, StampBounds(row), p.Enabled ? "已启用" : "未启用",
                    p.Enabled ? Theme.Ok : Theme.Line,
                    p.Enabled ? Theme.Ok : Theme.TextFaint, Theme.Tiny);

                /* 操作列：设置（同宽同高，右边线与启用框对齐） */
                Rectangle action = ActionBounds(row);
                bool actionHover = i == _hoverRow && _hoverZone == 1;
                if (_actionAnim > 0.01f)
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(255 * _actionAnim), Theme.CardHover)))
                        Theme.FillRound(g, action, Theme.S(2), brush);
                }
                using (Pen pen = new Pen(Theme.Blend(Theme.Line, Theme.Text, 0.25f * _actionAnim), 1f))
                    g.DrawRectangle(pen, action.X, action.Y, action.Width - 1, action.Height - 1);
                using (StringFormat center = Theme.FormatCenter())
                    Theme.DrawString(g, "设置", Theme.Small, Theme.Blend(Theme.TextDim, Theme.Text, _actionAnim),
                        new RectangleF(action.X, action.Y, action.Width, action.Height), center);

                /* 启用：滑动开关（按下即反馈，状态乐观切换后动画到位） */
                Rectangle toggle = ToggleBounds(row);
                float at = i < _toggleAnim.Length ? _toggleAnim[i] : (p.Enabled ? 1f : 0f);
                float hoverGlow = i < _hoverAnim.Length ? _hoverAnim[i] : 0f;
                bool pressed = _pressedRow == i;
                float pulse = i < _pulseAnim.Length ? _pulseAnim[i] : 0f;

                // 白轨/黑钮（深色）· 黑轨/白钮（浅色）：不画任何外圈描边
                Color trackOff = Theme.Blend(Theme.Card, Theme.Line, 0.45f);
                Color track = Theme.Blend(trackOff, Theme.ToggleTrackOn, at);
                if (pressed) track = Theme.Blend(track, Theme.Text, 0.10f);
                int radius = toggle.Height / 2;
                Theme.FillRound(g, toggle, radius, track);

                if (pulse > 0.02f)
                {
                    using (Pen pen = new Pen(Color.FromArgb((int)(90 * pulse), Theme.Ok), Math.Max(1f, Theme.S(1.4f))))
                        Theme.StrokeRound(g, new Rectangle(toggle.X - Theme.S(3), toggle.Y - Theme.S(3), toggle.Width + Theme.S(6), toggle.Height + Theme.S(6)), radius + Theme.S(3), pen.Color, Math.Max(1f, Theme.S(1.4f)));
                }

                int knob = toggle.Height - Theme.S(6) + (pressed ? Theme.S(2) : 0);
                int travel = toggle.Width - knob - Theme.S(6);
                int knobX = toggle.X + Theme.S(3) + (int)(travel * at) - (pressed ? Theme.S(1) : 0);
                Rectangle knobRect = new Rectangle(knobX, toggle.Y + (toggle.Height - knob) / 2, knob, knob);
                Color knobOff = Theme.Blend(Theme.Card, Color.White, 0.55f + hoverGlow * 0.2f);
                using (SolidBrush knobBrush = new SolidBrush(Theme.Blend(knobOff, Theme.ToggleKnobOn, at)))
                    g.FillEllipse(knobBrush, knobRect);
            }

            /* ---- 底栏 ---- */
            Rectangle footer = new Rectangle(0, ClientSize.Height - Theme.S(FooterH), ClientSize.Width, Theme.S(FooterH));
            Theme.Rule(g, 0, footer.Y, ClientSize.Width);
            using (StringFormat left = Theme.FormatLeft())
            {
                // 开机自启（F2）：宿主级设置，放在底部状态栏
                string autoText = "开机自启  " + (_context.Settings.AutoStart ? "开" : "关") + "（F2）";
                Rectangle autoRect = new Rectangle(LeftEdge + RowPad, footer.Y, Theme.S(220), Theme.S(FooterH));
                Theme.DrawString(g, autoText, Theme.Small,
                    _context.Settings.AutoStart ? Theme.Text : Theme.TextFaint, autoRect, left);

                Theme.DrawString(g, SettingsStore.DataDirectory, Theme.MonoMicro, Theme.TextFaint,
                    new Rectangle(LeftEdge + RowPad + Theme.S(240), footer.Y,
                        ClientSize.Width - Theme.S(620), Theme.S(FooterH)), left);

                Theme.DrawString(g, "Ctrl+Alt+T 暂停/启用    v" + Program.Version, Theme.MonoMicro, Theme.TextFaint,
                    new Rectangle(ClientSize.Width - Theme.S(300), footer.Y, Theme.S(280), Theme.S(FooterH)), left);
            }
            
        }

        /// <summary>插件图标：渐变圆角方块 + 白色字形（容差插件画判定框与两个落点）。</summary>
        private static void DrawPluginIcon(Graphics g, Rectangle rect, string key)
        {
            // 和程序图标同一套：黑色渐变底 + 白色字形 + 一点青色信号（不再用蓝色块）
            using (System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                rect, Color.FromArgb(27, 31, 36), Color.FromArgb(8, 9, 11), 90f))
            {
                Theme.FillRound(g, rect, Theme.S(9), brush);
            }
            using (Pen edge = new Pen(Color.FromArgb(38, 255, 255, 255), 1f))
            {
                Theme.StrokeRound(g, rect, Theme.S(9), edge.Color, 1f);
            }

            Color ink = Color.White;
            if (key == "tolerance")
            {
                float inset = rect.Width * 0.22f;
                RectangleF zone = new RectangleF(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Width - inset * 2);
                using (Pen dash = new Pen(Color.FromArgb(235, ink), Math.Max(1f, rect.Width * 0.04f)))
                {
                    dash.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    dash.DashPattern = new float[] { 2.4f, 1.8f };
                    g.DrawRectangle(dash, zone.X, zone.Y, zone.Width, zone.Height);
                }
                float dot = Math.Max(1.6f, rect.Width * 0.09f);
                PointF a = new PointF(zone.X + zone.Width * 0.30f, zone.Y + zone.Height * 0.62f);
                PointF b = new PointF(zone.X + zone.Width * 1.02f, zone.Y + zone.Height * 0.26f);
                using (Pen link = new Pen(Color.FromArgb(220, ink), Math.Max(1f, rect.Width * 0.035f))) g.DrawLine(link, a, b);
                using (SolidBrush ok = new SolidBrush(Color.FromArgb(247, 248, 243))) g.FillEllipse(ok, a.X - dot, a.Y - dot, dot * 2, dot * 2);
                using (SolidBrush bad = new SolidBrush(Color.FromArgb(63, 224, 236))) g.FillEllipse(bad, b.X - dot, b.Y - dot, dot * 2, dot * 2);
            }
            else
            {
                using (Pen pen = new Pen(ink, Math.Max(1.6f, rect.Width * 0.09f)))
                {
                    float inset = rect.Width * 0.30f;
                    g.DrawRectangle(pen, rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Width - inset * 2);
                }
            }
        }

        /* ------------------------------------------------------------------ */
        /* 命中测试                                                            */
        /* ------------------------------------------------------------------ */

        private int HitRow(Point point)
        {
            IPatchPlugin[] plugins = _host.Plugins;
            for (int i = 0; i < plugins.Length; i++)
                if (RowBounds(i).Contains(point)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int row = HitRow(e.Location);
            int zone = 0;
            if (row >= 0)
            {
                Rectangle bounds = RowBounds(row);
                if (ActionBounds(bounds).Contains(e.Location)) zone = 1;
                else if (ToggleBounds(bounds).Contains(e.Location)) zone = 2;
            }
            if (row != _hoverRow || zone != _hoverZone)
            {
                _hoverRow = row;
                _hoverZone = zone;
                Cursor = zone > 0 ? Cursors.Hand : Cursors.Default;
                SyncAnim();
                for (int i = 0; i < _hoverTarget.Length; i++) _hoverTarget[i] = i == row ? 1f : 0f;
                _animTimer.Start();
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverRow = -1;
            _hoverZone = 0;
            _pressedRow = -1;
            Cursor = Cursors.Default;
            SyncAnim();
            for (int i = 0; i < _hoverTarget.Length; i++) _hoverTarget[i] = 0f;
            _animTimer.Start();
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Rectangle footerHit = new Rectangle(LeftEdge + RowPad, ClientSize.Height - Theme.S(FooterH), Theme.S(230), Theme.S(FooterH));
                if (footerHit.Contains(e.Location)) { ToggleAutostart(); return; }
                int row = HitRow(e.Location);
                if (row >= 0 && row < _host.Plugins.Length)
                {
                    Rectangle bounds = RowBounds(row);
                    if (ToggleBounds(bounds).Contains(e.Location))
                    {
                        _pressedRow = row;          // 按下立刻给视觉反馈，不等抬起
                        _animTimer.Start();
                        Invalidate();
                        return;
                    }
                    if (ActionBounds(bounds).Contains(e.Location)) { OpenPluginSettings(_host.Plugins[row]); return; }
                }
                Rectangle auto = new Rectangle(LeftEdge + RowPad, ClientSize.Height - Theme.S(FooterH), Theme.S(220), Theme.S(FooterH));
                }
            base.OnMouseDown(e);
        }
    }
}