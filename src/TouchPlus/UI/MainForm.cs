using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TouchPlus.Core;
using TouchPlus.UI.Controls;

namespace TouchPlus.UI
{
    public sealed class MainForm : ChromeForm
    {
        private const int TitleH = 52;          // 设计像素（使用时统一走 Theme.S）
        private const int RailW = 190;
        private const int FooterH = 40;
        private const int HotkeyId = 0x5450;

        private readonly PatchController _controller;
        private TrayIconManager _tray;
        private readonly bool _startMinimized;
        private bool _reallyExit;
        private bool _balloonShown;

        /* 外壳 */
        private ChromeButton _btnMin;
        private ChromeButton _btnMax;
        private ChromeButton _btnClose;
        private readonly List<NavEntry> _nav = new List<NavEntry>();
        private int _activeNav;
        private int _hoverNav = -1;


        /* 概览页 */
        private Panel _pageOverview;
        private ToggleSwitch _swMaster;
        private Label _heroTitle;
        private Label _heroSub;
        private PillLabel _heroPill;
        private StatTile _tileRepairs;
        private StatTile _tileDistance;
        private StatTile _tileUptime;
        private ToggleSwitch _swSystem;
        private ToggleSwitch _swHook;
        private PillLabel _pillSystem;
        private PillLabel _pillHook;
        private ToleranceField _pad;

        /* 参数页 */
        private Panel _pageParams;
        private FlatSlider _slRect;
        private FlatSlider _slTime;
        private FlatSlider _slTolerance;
        private FlatSlider _slWindow;
        private FlatSlider _slDrag;
        private SegmentedControl _segSource;
        private ToggleSwitch _swSameWindow;
        private ToggleSwitch _swLongPress;

        /* 排除页 */

        /* 日志页 */
        private Panel _pageLog;
        private LogView _logView;
        private ToggleSwitch _swAutoScroll;

        /* 关于页 */
        private Panel _pageAbout;
        private AboutSheet _aboutSheet;


        /* 定时器 */
        private readonly Timer _tick;
        private readonly Timer _applyTimer;
        private readonly Timer _padTimer;
        private readonly Timer _saveTimer;
        private DateTime _startedAt = DateTime.Now;
        private PairReport _pendingReport;
        private int _padBaseline;
        private bool _loadingUi;
        private float _pageShift;
        private Timer _pageTimer;

        /* 切页动画：淡出 → 换页 → 淡入 + 缓动上移（三段式，不再是单纯的指数衰减） */
        private VeilPanel _fadeVeil;
        private float _fadeAlpha;
        private int _animPhase;          // 0 空闲 / 1 淡出 / 2 淡入
        private float _animT;
        private int _pendingNav = -1;

        /* 外壳尺寸（设计像素 → 按 DPI 缩放） */
        private int RailWidth { get { return Theme.S(RailW); } }
        private int TitleAreaH { get { return Theme.S(TitleH); } }
        private int FooterH2 { get { return Theme.S(FooterH); } }

        public int InitialPage = 0;

        private Core.Plugins.IPatchPlugin _plugin;
        private bool _asSettings;

        /// <summary>作为某个插件的设置窗口打开（从插件中心进来）。</summary>
        public MainForm(PatchController controller, bool startMinimized, Core.Plugins.IPatchPlugin plugin)
            : this(controller, startMinimized)
        {
            _plugin = plugin;
            _asSettings = plugin != null;
            if (_asSettings)
            {
                Text = plugin.Name + " · 设置";
                if (_tray != null)
                {
                    try { _tray.Visible = false; _tray.Dispose(); } catch (Exception) { }
                    _tray = null;
                }
                BuildBackButton();
                if (_cardAbout != null)
                {
                    _cardAbout.Title = "关于 " + plugin.Name;
                    _cardAbout.Subtitle = "插件信息与它对本机做的改动";
                }
                if (_btnAdmin != null) _btnAdmin.Visible = false;
            }
        }

        private void BuildBackButton()
        {
            AccentButton back = new AccentButton();
            back.Text = "← 插件列表";
            back.Kind = ButtonKind.Ghost;
            back.SetBounds(RailWidth + Theme.S(16) + Theme.S(116), Theme.S(11), Theme.S(112), Theme.S(32));
            back.Click += delegate { Close(); };
            Controls.Add(back);
            back.BringToFront();
        }

        private static Theme.ThemeMode ParseThemeMode(string text)
        {
            if (string.Equals(text, "Dark", StringComparison.OrdinalIgnoreCase)) return Theme.ThemeMode.Dark;
            if (string.Equals(text, "Light", StringComparison.OrdinalIgnoreCase)) return Theme.ThemeMode.Light;
            return Theme.ThemeMode.Auto;
        }

        private void ApplyTheme()
        {
            if (IsDisposed) return;
            BackColor = Theme.Bg;
            ApplyThemeTo(this);
            Invalidate(true);
        }

        private static void ApplyThemeTo(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                TextBox box = c as TextBox;
                if (box != null && !box.ReadOnly) { box.BackColor = Theme.Card; box.ForeColor = Theme.Text; }
                ListBox list = c as ListBox;
                if (list != null) { list.BackColor = Theme.Card; list.ForeColor = Theme.Text; }
                if (c.HasChildren) ApplyThemeTo(c);
            }
        }

        public MainForm(PatchController controller, bool startMinimized)
        {
            _controller = controller;
            _startMinimized = startMinimized;

            Text = "Touch+ 触控屏双击修复";
            AutoScaleMode = AutoScaleMode.None;       // 自绘界面自己按 DPI 缩放，禁止 WinForms 二次缩放
            Theme.InitScale(this);
            Theme.ApplyMode(ParseThemeMode(_controller.Settings.ThemeMode));
            Theme.Changed += delegate { ApplyTheme(); };
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(1000), Theme.S(720));
            MinimumSize = new Size(Theme.S(940), Theme.S(660));
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = AppIcon.Get();

            Theme.InitScale(this);
            _controller.StateChanged += delegate { RefreshState(); };

            BuildChrome();
            BuildOverviewPage();
            BuildParamsPage();
            BuildLogPage();
            BuildAboutPage();
            LayoutChrome();
            SelectNav(0);

            _tray = new TrayIconManager();
            _tray.OpenRequested += delegate { RestoreFromTray(); };
            _tray.ToggleRequested += delegate { ToggleMaster(); };
            _tray.ExitRequested += delegate { ExitApplication(); };
            _tray.AutostartRequested += delegate { SetAutostart(!_controller.Settings.AutoStart); };
            _tray.SystemLayerRequested += delegate { SetSystemLayer(!_controller.Settings.SystemLayerEnabled); };
            _tray.HookLayerRequested += delegate { SetHookLayer(!_controller.Settings.HookLayerEnabled); };
            _tray.Visible = true;

            Log.Instance.EntryAdded += OnLogEntry;

            _pageTimer = new Timer();
            _pageTimer.Interval = 16;
            _pageTimer.Tick += delegate { TickPageAnimation(); };

            _tick = new Timer();
            _tick.Interval = 1000;
            _tick.Tick += delegate { RefreshStats(); };
            _tick.Start();

            _applyTimer = new Timer();
            _applyTimer.Interval = 400;
            _applyTimer.Tick += delegate { _applyTimer.Stop(); ApplySystemLayerNow(); };

            _padTimer = new Timer();
            _padTimer.Interval = 220;
            _padTimer.Tick += OnPadTimer;

            _saveTimer = new Timer();
            _saveTimer.Interval = 700;
            _saveTimer.Tick += delegate { _saveTimer.Stop(); SaveSettings(); };

            _controller.Engine.PairReported += OnPairReported;

            LoadUiFromSettings();
            RefreshState();
            RefreshStats();

            if (_startMinimized)
            {
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
            }
        }

        /* ================================================================== */

        private sealed class NavEntry
        {
            public string Title;
            public int Glyph;               // Segoe MDL2 Assets 码位
            public string GlyphFallback;    // 老系统回退字形
            public Panel Page;
            public Rectangle Bounds;
            public Rectangle PillBounds;
        }

        private void BuildChrome()
        {
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

            LayoutChrome();
        }

        private void LayoutChrome()
        {
            if (_btnClose == null) return;
            int bw = Theme.S(ChromeButton.ButtonSize.Width);
            int bh = TitleAreaH;   // 与标题栏同高（该值已含 DPI 缩放）
            _btnClose.Size = new Size(bw, bh);
            _btnMax.Size = new Size(bw, bh);
            _btnMin.Size = new Size(bw, bh);
            _btnClose.Location = new Point(ClientSize.Width - bw, 0);
            _btnMax.Location = new Point(_btnClose.Left - bw, 0);
            _btnMin.Location = new Point(_btnMax.Left - bw, 0);

            int pageX = RailWidth + Theme.S(14);
            int pageY = TitleAreaH + Theme.S(12);
            int pageW = ClientSize.Width - pageX - Theme.S(14);
            int pageH = ClientSize.Height - pageY - FooterH2 - Theme.S(12);

            for (int i = 0; i < _nav.Count; i++)
            {
                Rectangle bounds = new Rectangle(Theme.S(12), TitleAreaH + Theme.S(14) + i * Theme.S(46),
                    RailWidth - Theme.S(24), Theme.S(40));
                _nav[i].Bounds = bounds;
                Rectangle pill = new Rectangle(bounds.X, bounds.Y + Theme.S(8), Theme.S(2), bounds.Height - Theme.S(16));
                _nav[i].PillBounds = pill;
                if (_nav[i].Page != null)
                {
                    int shift = (i == _activeNav) ? (int)_pageShift : 0;
                _nav[i].Page.Bounds = new Rectangle(pageX, pageY + shift, pageW, pageH);
                }
            }

            if (_pageOverview != null)
            {
                LayoutOverviewPage();
                LayoutParamsPage();
                LayoutLogPage();
                LayoutAboutPage();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChrome();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            /* 背景 */
            using (SolidBrush brush = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            /* 侧栏 */
            Rectangle rail = new Rectangle(0, 0, RailWidth, ClientSize.Height);
            using (SolidBrush brush = new SolidBrush(Theme.BgDeep))
            {
                g.FillRectangle(brush, rail);
            }
            using (Pen pen = new Pen(Theme.LineSoft))
            {
                g.DrawLine(pen, RailWidth, TitleAreaH, RailWidth, ClientSize.Height - FooterH2);
            }

            /* 品牌标记：程序图标本体 */
            Rectangle logo = new Rectangle(Theme.S(20), Theme.S(12), Theme.S(28), Theme.S(28));
            Theme.DrawBrandMark(g, logo);
            using (StringFormat format = Theme.FormatLeft())
            {
                Theme.DrawString(g, "Touch+", Theme.H3, Theme.Text,
                    new Rectangle(Theme.S(56), Theme.S(11), Theme.S(120), Theme.S(20)), format);
                Theme.DrawString(g, "希沃触控屏补丁", Theme.Tiny, Theme.TextFaint,
                    new Rectangle(Theme.S(56), Theme.S(29), Theme.S(120), Theme.S(16)), format);
            }

            /* 导航 */
            for (int i = 0; i < _nav.Count; i++)
            {
                NavEntry entry = _nav[i];
                bool active = i == _activeNav;
                bool hover = i == _hoverNav;

                if (active)
                {
                    Theme.FillRound(g, entry.Bounds, Theme.S(0), Theme.Card);
                    using (SolidBrush bar = new SolidBrush(Theme.Accent))
                    {
                        g.FillRectangle(bar, entry.PillBounds);
                    }
                }
                else if (hover)
                {
                    Theme.FillRound(g, entry.Bounds, Theme.S(0), Theme.CardHover);
                }

                using (StringFormat format = Theme.FormatLeft())
                {
                    Theme.DrawNavIcon(g,
                        new Rectangle(entry.Bounds.X + Theme.S(14), entry.Bounds.Y, Theme.S(24), entry.Bounds.Height),
                        entry.Glyph, entry.GlyphFallback, active ? Theme.Accent : Theme.TextFaint);
                    Theme.DrawString(g, entry.Title, active ? Theme.BodyBold : Theme.Body,
                        active ? Theme.Text : Theme.TextDim,
                        new Rectangle(entry.Bounds.X + Theme.S(44), entry.Bounds.Y, entry.Bounds.Width - Theme.S(52), entry.Bounds.Height), format);
                }
            }

            /* 标题栏 */
            Rectangle titleArea = new Rectangle(RailWidth, 0, ClientSize.Width - RailWidth, TitleAreaH);
            using (SolidBrush brush = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(brush, titleArea);
            }
            using (Pen pen = new Pen(Theme.LineSoft))
            {
                g.DrawLine(pen, RailWidth, TitleAreaH, ClientSize.Width, TitleAreaH);
            }
            using (StringFormat format = Theme.FormatLeft())
            {
                Theme.DrawString(g, CurrentPageTitle(), Theme.H3, Theme.Text,
                    new Rectangle(RailWidth + Theme.S(16), 0, Theme.S(320), TitleAreaH), format);
            }

            /* 标题栏右侧：系统层实时读数（仪器面板的做法——把关键参数常驻在头栏） */
            bool sysOn = _controller.Settings.SystemLayerEnabled && _controller.Settings.PatchEnabled;
            int dotX = _btnMin.Left - Theme.S(230);
            Theme.StatusDot(g, dotX, TitleAreaH / 2, Theme.S(3), sysOn ? Theme.Ok : Theme.TextFaint);
            using (StringFormat fmt = Theme.FormatLeft())
            {
                Theme.DrawString(g, sysOn ? "系统层生效" : "系统层未生效", Theme.Small,
                    sysOn ? Theme.TextDim : Theme.TextFaint,
                    new Rectangle(dotX + Theme.S(12), 0, Theme.S(92), TitleAreaH), fmt);
                Theme.DrawString(g,
                    _controller.Settings.SystemDoubleClickWidth + "px / " + _controller.Settings.SystemDoubleClickTime + "ms",
                    Theme.Mono, Theme.TextFaint,
                    new Rectangle(dotX + Theme.S(108), 0, Theme.S(120), TitleAreaH), fmt);
            }

            /* 底部栏 */
            Rectangle footer = new Rectangle(0, ClientSize.Height - FooterH2, ClientSize.Width, FooterH2);
            using (SolidBrush brush = new SolidBrush(Theme.BgDeep))
            {
                g.FillRectangle(brush, footer);
            }
            using (Pen pen = new Pen(Theme.LineSoft))
            {
                g.DrawLine(pen, 0, footer.Y, ClientSize.Width, footer.Y);
            }

            using (StringFormat format = Theme.FormatLeft())
            {
                Theme.DrawString(g, "配置已自动保存  " + SettingsStore.DataDirectory, Theme.Tiny, Theme.TextFaint,
                    new Rectangle(Theme.S(12), footer.Y, ClientSize.Width - Theme.S(340), FooterH2), format);
                Theme.DrawString(g, "Ctrl+Alt+T 暂停/启用   v" + Program.Version, Theme.Mono, Theme.TextFaint,
                    new Rectangle(ClientSize.Width - Theme.S(300), footer.Y, Theme.S(288), FooterH2), Theme.FormatRight());
            }
        }



        private string CurrentPageTitle()
        {
            if (_activeNav >= 0 && _activeNav < _nav.Count) return _nav[_activeNav].Title;
            return "Touch+";
        }

        /* ================================================================== */
        /* 页面构建                                                            */
        /* ================================================================== */

        private Label MakeLabel(string text, Font font, Color color, int x, int y, int w, int h)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Bounds = new Rectangle(x, y, w, h);
            return label;
        }

        private Panel NewPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Bg;
            page.Visible = false;
            Controls.Add(page);
            return page;
        }

        private void AddNav(string title, int glyph, string glyphFallback, Panel page)
        {
            NavEntry entry = new NavEntry();
            entry.Title = title;
            entry.Glyph = glyph;
            entry.GlyphFallback = glyphFallback;
            entry.Page = page;
            _nav.Add(entry);
        }

        /* ---------------- 概览 ---------------- */

        private void BuildOverviewPage()
        {
            _pageOverview = NewPage();
            AddNav("概览", 0xE80F, "◎", _pageOverview);

            CardPanel hero = new CardPanel();
            hero.Title = "";
            hero.TitleHeight = 0;
            hero.Padding = new Padding(0);
            hero.AccentTop = true;
            _pageOverview.Controls.Add(hero);

            _heroTitle = MakeLabel("正在检查补丁状态…", Theme.H1, Theme.Text, Theme.S(24), Theme.S(42), Theme.S(520), Theme.S(40));
            hero.Controls.Add(_heroTitle);

            _heroSub = MakeLabel("", Theme.Body, Theme.TextDim, Theme.S(24), Theme.S(88), Theme.S(620), Theme.S(42));
            hero.Controls.Add(_heroSub);

            _heroPill = new PillLabel();
            _heroPill.Bounds = new Rectangle(Theme.S(24), Theme.S(14), Theme.S(96), Theme.S(24));
            hero.Controls.Add(_heroPill);

            _swMaster = new ToggleSwitch();
            _swMaster.CheckedChanged += delegate { if (!_loadingUi) ToggleMaster(); };
            hero.Controls.Add(_swMaster);

            Label masterLabel = MakeLabel("双击修复", Theme.Small, Theme.TextDim, 0, 0, Theme.S(60), Theme.S(18));
            masterLabel.TextAlign = ContentAlignment.MiddleRight;
            hero.Controls.Add(masterLabel);
            hero.Tag = masterLabel;
            _heroMasterLabel = masterLabel;

            _pageOverview.Tag = hero;
            _heroCard = hero;

            _tileRepairs = new StatTile();
            _tileRepairs.Label = "输入层修复次数";
            _tileRepairs.Accent = Theme.Accent;
            _pageOverview.Controls.Add(_tileRepairs);

            _tileDistance = new StatTile();
            _tileDistance.Label = "输入层平均偏离";
            _tileDistance.Unit = "px";
            _tileDistance.Accent = Theme.Accent2;
            _pageOverview.Controls.Add(_tileDistance);

            _tileUptime = new StatTile();
            _tileUptime.Label = "已运行";
            _tileUptime.Accent = Theme.Ok;
            _pageOverview.Controls.Add(_tileUptime);

            CardPanel patch = new CardPanel();
            patch.Title = "补丁";
            patch.Subtitle = "模块化修复项，可分别开关";
            _pageOverview.Controls.Add(patch);
            _cardPatch = patch;

            /* 系统层行 */
            _lblSysTitle = MakeLabel("系统双击参数优化", Theme.BodyBold, Theme.Text, 0, 0, Theme.S(320), Theme.S(22));
            patch.Controls.Add(_lblSysTitle);
            _lblSysDesc = MakeLabel("放宽系统双击矩形与时间窗，让系统原生识别手指抖动（不注入任何输入，全系统生效）",
                Theme.Small, Theme.TextFaint, 0, 0, Theme.S(430), Theme.S(36));
            patch.Controls.Add(_lblSysDesc);
            _pillSystem = new PillLabel();
            patch.Controls.Add(_pillSystem);
            _swSystem = new ToggleSwitch();
            _swSystem.CheckedChanged += delegate { if (!_loadingUi) SetSystemLayer(_swSystem.Checked); };
            patch.Controls.Add(_swSystem);

            /* 输入层行 */
            _lblHookTitle = MakeLabel("输入层智能修复", Theme.BodyBold, Theme.Text, 0, 0, Theme.S(320), Theme.S(22));
            patch.Controls.Add(_lblHookTitle);
            _lblHookDesc = MakeLabel("偏离超过系统矩形的第二次点击，由钩子拦截后在锚点合成一次完整双击（高级，默认关闭）",
                Theme.Small, Theme.TextFaint, 0, 0, Theme.S(430), Theme.S(36));
            patch.Controls.Add(_lblHookDesc);
            _pillHook = new PillLabel();
            patch.Controls.Add(_pillHook);
            _swHook = new ToggleSwitch();
            _swHook.CheckedChanged += delegate { if (!_loadingUi) SetHookLayer(_swHook.Checked); };
            patch.Controls.Add(_swHook);

            CardPanel testCard = new CardPanel();
            testCard.Title = "自检";
            testCard.Subtitle = "在这里点两下，验证补丁是否生效";
            _pageOverview.Controls.Add(testCard);
            _cardTest = testCard;

            _pad = new ToleranceField();
            testCard.Controls.Add(_pad);
        }

        private CardPanel _heroCard;
        private CardPanel _cardPatch;
        private CardPanel _cardTest;
        private Label _heroMasterLabel;
        private Label _lblSysTitle;
        private Label _lblSysDesc;
        private Label _lblHookTitle;
        private Label _lblHookDesc;

        private void LayoutOverviewPage()
        {
            if (_pageOverview == null) return;
            int w = _pageOverview.Width;
            int h = _pageOverview.Height;

            int heroH = Theme.S(124);
            _heroCard.SetBounds(0, 0, w, heroH);
            _swMaster.Location = new Point(w - Theme.S(56) - Theme.S(24), Theme.S(26));
            _heroMasterLabel.Bounds = new Rectangle(w - Theme.S(56) - Theme.S(24) - Theme.S(14) - Theme.S(150), Theme.S(36), Theme.S(150), Theme.S(22));

            int gap = Theme.S(12);
            int tileW = (w - gap * 2) / 3;
            int tileY = heroH + Theme.S(14);
            int tileH = Theme.S(86);
            _tileRepairs.SetBounds(0, tileY, tileW, tileH);
            _tileDistance.SetBounds(tileW + gap, tileY, tileW, tileH);
            _tileUptime.SetBounds((tileW + gap) * 2, tileY, tileW, tileH);

            int patchY = tileY + tileH + gap;
            int patchH = Theme.S(190);
            _cardPatch.SetBounds(0, patchY, w, patchH);

            Rectangle content = _cardPatch.ContentBounds;
            int rowH = Theme.S(64);
            LayoutPatchRow(content, 0, rowH);   // 该函数内部已排好两行

            int testY = patchY + patchH + gap;
            int testH = Math.Max(Theme.S(120), h - testY);
            _cardTest.SetBounds(0, testY, w, testH);
            Rectangle testContent = _cardTest.ContentBounds;
            _pad.SetBounds(testContent.X, testContent.Y, testContent.Width, Math.Max(Theme.S(140), testContent.Height));
        }

        private void LayoutPatchRow(Rectangle content, int offsetY, int rowH)
        {
            int pillW = Theme.S(72);
            int toggleW = _swSystem.Width;
            int textW = content.Width - pillW - toggleW - Theme.S(30);
            int y = content.Y + offsetY;

            _lblSysTitle.Bounds = new Rectangle(content.X, y, textW, Theme.S(22));
            _lblSysDesc.Bounds = new Rectangle(content.X, y + Theme.S(22), textW, Theme.S(34));

            int hookY = y + rowH + Theme.S(8);
            _lblHookTitle.Bounds = new Rectangle(content.X, hookY, textW, Theme.S(22));
            _lblHookDesc.Bounds = new Rectangle(content.X, hookY + Theme.S(22), textW, Theme.S(34));

            int pillX = content.X + textW + Theme.S(10);
            _pillSystem.Bounds = new Rectangle(pillX, y + Theme.S(14), pillW, Theme.S(24));
            _pillHook.Bounds = new Rectangle(pillX, hookY + Theme.S(14), pillW, Theme.S(24));

            int toggleX = content.Right - toggleW;
            _swSystem.Location = new Point(toggleX, y + Theme.S(11));
            _swHook.Location = new Point(toggleX, hookY + Theme.S(11));
        }

        /* ---------------- 参数 ---------------- */

        private CardPanel _cardSysParams;
        private CardPanel _cardHookTrigger;
        private CardPanel _cardHookPolicy;
        private CardPanel _cardSafety;
        private AccentButton _btnRestoreSystem;
        private Label _lblRect, _lblTime, _lblTolerance, _lblWindow, _lblDrag;
        private Label _lblSource, _lblSameWindow, _lblLongPress;

        private void BuildParamsPage()
        {
            _pageParams = NewPage();
            AddNav("参数", 0xE713, "⚙", _pageParams);

            _cardSysParams = new CardPanel();
            _cardSysParams.Title = "系统层参数（推荐）";
            _cardSysParams.Subtitle = "改完立即全系统生效，可一键还原";
            _pageParams.Controls.Add(_cardSysParams);

            _lblRect = MakeLabel("双击矩形：两次点击允许的最大偏移", Theme.Small, Theme.TextDim, 0, 0, Theme.S(300), Theme.S(18));
            _cardSysParams.Controls.Add(_lblRect);
            _slRect = new FlatSlider();
            _slRect.Minimum = 4;
            _slRect.Maximum = 200;
            _slRect.Step = 2;
            _slRect.Unit = "px";
            _slRect.ValueChanged += delegate { if (!_loadingUi) { _lblRect.Text = RectHint(); QueueApply(); QueueSave(); } };
            _cardSysParams.Controls.Add(_slRect);

            _lblTime = MakeLabel("双击时间窗：两次点击允许的最大间隔", Theme.Small, Theme.TextDim, 0, 0, Theme.S(300), Theme.S(18));
            _cardSysParams.Controls.Add(_lblTime);
            _slTime = new FlatSlider();
            _slTime.Minimum = 200;
            _slTime.Maximum = 1500;
            _slTime.Step = 50;
            _slTime.Unit = "ms";
            _slTime.ValueChanged += delegate { if (!_loadingUi) { QueueApply(); QueueSave(); } };
            _cardSysParams.Controls.Add(_slTime);

            _btnRestoreSystem = new AccentButton();
            _btnRestoreSystem.Text = "还原系统默认";
            _btnRestoreSystem.Kind = ButtonKind.Ghost;
            _btnRestoreSystem.Click += delegate { RestoreSystemDefaults(); };
            _cardSysParams.Controls.Add(_btnRestoreSystem);

            _cardHookTrigger = new CardPanel();
            _cardHookTrigger.Title = "输入层参数";
            _cardHookTrigger.Subtitle = "仅在启用「输入层智能修复」时生效";
            _pageParams.Controls.Add(_cardHookTrigger);

            _lblTolerance = MakeLabel("容差：超过系统矩形但仍要救回的偏移", Theme.Small, Theme.TextDim, 0, 0, Theme.S(300), Theme.S(18));
            _cardHookTrigger.Controls.Add(_lblTolerance);
            _slTolerance = new FlatSlider();
            _slTolerance.Minimum = 8;
            _slTolerance.Maximum = 200;
            _slTolerance.Step = 2;
            _slTolerance.Unit = "px";
            _slTolerance.ValueChanged += delegate { if (!_loadingUi) { SyncHookEngine(); QueueSave(); } };
            _cardHookTrigger.Controls.Add(_slTolerance);

            _lblWindow = MakeLabel("时间窗：两次点击允许的最大间隔", Theme.Small, Theme.TextDim, 0, 0, Theme.S(300), Theme.S(18));
            _cardHookTrigger.Controls.Add(_lblWindow);
            _slWindow = new FlatSlider();
            _slWindow.Minimum = 200;
            _slWindow.Maximum = 2000;
            _slWindow.Step = 50;
            _slWindow.Unit = "ms";
            _slWindow.ValueChanged += delegate { if (!_loadingUi) { SyncHookEngine(); QueueSave(); } };
            _cardHookTrigger.Controls.Add(_slWindow);

            _lblDrag = MakeLabel("拖动判定：按下与抬起超过此距离即视为拖动，不作双击", Theme.Small, Theme.TextDim, 0, 0, Theme.S(360), Theme.S(18));
            _cardHookTrigger.Controls.Add(_lblDrag);
            _slDrag = new FlatSlider();
            _slDrag.Minimum = 4;
            _slDrag.Maximum = 60;
            _slDrag.Step = 2;
            _slDrag.Unit = "px";
            _slDrag.ValueChanged += delegate { if (!_loadingUi) { SyncHookEngine(); QueueSave(); } };
            _cardHookTrigger.Controls.Add(_slDrag);

            _cardHookPolicy = new CardPanel();
            _cardHookPolicy.Title = "输入层策略";
            _cardHookPolicy.Subtitle = "修复方式固定为「合成双击」——坐标对齐（移动光标）已实测在 Win10/11 上无效";
            _pageParams.Controls.Add(_cardHookPolicy);

            _lblSource = MakeLabel("输入来源过滤", Theme.Small, Theme.TextDim, 0, 0, Theme.S(200), Theme.S(18));
            _cardHookPolicy.Controls.Add(_lblSource);
            _segSource = new SegmentedControl();
            _segSource.SetItems("自动", "仅触控/笔", "全部输入");
            _segSource.SelectedIndexChanged += delegate { if (!_loadingUi) { SyncHookEngine(); QueueSave(); } };
            _cardHookPolicy.Controls.Add(_segSource);

            _lblSameWindow = MakeLabel("仅在同一窗口内生效（更安全）", Theme.Small, Theme.TextDim, 0, 0, Theme.S(300), Theme.S(24));
            _cardHookPolicy.Controls.Add(_lblSameWindow);
            _swSameWindow = new ToggleSwitch();
            _swSameWindow.CheckedChanged += delegate { if (!_loadingUi) { SyncHookEngine(); QueueSave(); } };
            _cardHookPolicy.Controls.Add(_swSameWindow);

            _lblLongPress = MakeLabel("长按保护（避免破坏触控长按/右键）", Theme.Small, Theme.TextDim, 0, 0, Theme.S(320), Theme.S(24));
            _cardHookPolicy.Controls.Add(_lblLongPress);
            _swLongPress = new ToggleSwitch();
            _swLongPress.CheckedChanged += delegate { if (!_loadingUi) { SyncHookEngine(); QueueSave(); } };
            _cardHookPolicy.Controls.Add(_swLongPress);

            _cardSafety = new CardPanel();
            _cardSafety.Title = "为什么默认只用系统层？";
            _pageParams.Controls.Add(_cardSafety);
            Label safety = MakeLabel(
                "· 系统层：改的是 Windows 自己的双击判定参数，系统原生识别，不注入、不拦截，Win7 ~ Win11 通用，实测 40px 即可救回 12~38px 的抖动。\r\n"
                + "· 输入层：需要全局鼠标钩子，会拦截并合成点击，可能被安全软件关注；只在抖动超过系统矩形上限时才需要，默认关闭。\r\n"
                + "· 两者可以同时开启：系统层优先处理，超出矩形范围的部分才交给输入层。",
                Theme.Small, Theme.TextFaint, 0, 0, Theme.S(360), Theme.S(150));
            _cardSafety.Controls.Add(safety);
            _safetyLabel = safety;
        }

        private Label _safetyLabel;

        private string RectHint()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "双击矩形：两次点击允许的最大偏移（当前 {0}px）", _slRect == null ? 50 : _slRect.Value);
        }

        private void LayoutParamsPage()
        {
            if (_pageParams == null) return;
            int w = _pageParams.Width;
            int h = _pageParams.Height;
            int gap = Theme.S(12);
            int colW = (w - gap) / 2;

            int sysH = Theme.S(206);
            int trigH = Math.Max(Theme.S(150), h - sysH - gap);
            _cardSysParams.SetBounds(0, 0, colW, sysH);
            _cardHookTrigger.SetBounds(0, sysH + gap, colW, trigH);

            int poliH = Theme.S(250);
            _cardHookPolicy.SetBounds(colW + gap, 0, w - colW - gap, poliH);
            _cardSafety.SetBounds(colW + gap, poliH + gap, w - colW - gap, Math.Max(Theme.S(140), h - poliH - gap));

            LayoutSysParamsCard();
            LayoutHookTriggerCard();
            LayoutHookPolicyCard();
            LayoutSafetyCard();
        }

        private void LayoutSysParamsCard()
        {
            Rectangle c = _cardSysParams.ContentBounds;
            int y = c.Y;
            _lblRect.Bounds = new Rectangle(c.X, y, c.Width - Theme.S(80), Theme.S(18));
            _slRect.SetBounds(c.X, y + Theme.S(18), c.Width, Theme.S(28));
            _lblTime.Bounds = new Rectangle(c.X, y + Theme.S(52), c.Width - Theme.S(80), Theme.S(18));
            _slTime.SetBounds(c.X, y + Theme.S(70), c.Width, Theme.S(28));
            _btnRestoreSystem.SetBounds(c.X, y + Theme.S(104), Theme.S(130), Theme.S(34));
        }

        private void LayoutHookTriggerCard()
        {
            Rectangle c = _cardHookTrigger.ContentBounds;
            int y = c.Y;
            _lblTolerance.Bounds = new Rectangle(c.X, y, c.Width, Theme.S(18));
            _slTolerance.SetBounds(c.X, y + Theme.S(18), c.Width, Theme.S(28));
            _lblWindow.Bounds = new Rectangle(c.X, y + Theme.S(52), c.Width, Theme.S(18));
            _slWindow.SetBounds(c.X, y + Theme.S(70), c.Width, Theme.S(28));
            _lblDrag.Bounds = new Rectangle(c.X, y + Theme.S(104), c.Width, Theme.S(18));
            _slDrag.SetBounds(c.X, y + Theme.S(122), c.Width, Theme.S(28));
        }

        private void LayoutHookPolicyCard()
        {
            Rectangle c = _cardHookPolicy.ContentBounds;
            int y = c.Y;
            _lblSource.Bounds = new Rectangle(c.X, y, c.Width, Theme.S(18));
            _segSource.SetBounds(c.X, y + Theme.S(20), Math.Min(c.Width, Theme.S(300)), Theme.S(34));

            int rowY = y + Theme.S(64);
            _lblSameWindow.Bounds = new Rectangle(c.X, rowY + Theme.S(3), c.Width - Theme.S(70), Theme.S(24));
            _swSameWindow.Location = new Point(c.Right - _swSameWindow.Width, rowY);

            rowY += Theme.S(38);
            _lblLongPress.Bounds = new Rectangle(c.X, rowY + Theme.S(3), c.Width - Theme.S(70), Theme.S(24));
            _swLongPress.Location = new Point(c.Right - _swLongPress.Width, rowY);
        }

        private void LayoutSafetyCard()
        {
            Rectangle c = _cardSafety.ContentBounds;
            _safetyLabel.Bounds = new Rectangle(c.X, c.Y, c.Width, c.Height);
        }

        /* ---------------- 日志 ---------------- */

        private CardPanel _cardLog;
        private AccentButton _btnClearLog;
        private AccentButton _btnExportLog;

        private void BuildLogPage()
        {
            _pageLog = NewPage();
            AddNav("日志", 0xE8FD, "≡", _pageLog);

            _cardLog = new CardPanel();
            _cardLog.Title = "运行日志";
            _cardLog.Subtitle = "记录每一次修复判定；双击某行可复制该行文本";
            _pageLog.Controls.Add(_cardLog);

            _logView = new LogView();
            _cardLog.Controls.Add(_logView);

            _btnClearLog = new AccentButton();
            _btnClearLog.Text = "清空";
            _btnClearLog.Kind = ButtonKind.Ghost;
            _btnClearLog.Click += delegate { _logView.Clear(); };
            _cardLog.Controls.Add(_btnClearLog);

            _btnExportLog = new AccentButton();
            _btnExportLog.Text = "导出";
            _btnExportLog.Kind = ButtonKind.Ghost;
            _btnExportLog.Click += delegate { ExportLog(); };
            _cardLog.Controls.Add(_btnExportLog);

            _swAutoScroll = new ToggleSwitch();
            _swAutoScroll.SetCheckedSilently(true);
            _swAutoScroll.CheckedChanged += delegate
            {
                if (_logView != null) _logView.AutoScrollToEnd = _swAutoScroll.Checked;
            };
            _cardLog.Controls.Add(_swAutoScroll);
            _lblAutoScroll = MakeLabel("自动滚动", Theme.Small, Theme.TextDim, 0, 0, Theme.S(70), Theme.S(20));
            _cardLog.Controls.Add(_lblAutoScroll);
        }

        private Label _lblAutoScroll;

        private void LayoutLogPage()
        {
            if (_cardLog == null) return;
            _cardLog.SetBounds(0, 0, _pageLog.Width, _pageLog.Height);
            Rectangle c = _cardLog.ContentBounds;

            int top = c.Y;
            _btnClearLog.SetBounds(c.Right - Theme.S(200), top - Theme.S(44), Theme.S(88), Theme.S(30));
            _btnExportLog.SetBounds(c.Right - Theme.S(104), top - Theme.S(44), Theme.S(88), Theme.S(30));
            _lblAutoScroll.Bounds = new Rectangle(c.Right - Theme.S(360), top - Theme.S(40), Theme.S(80), Theme.S(20));
            _swAutoScroll.Location = new Point(c.Right - Theme.S(284), top - Theme.S(45));

            int listTop = c.Y;
            _logView.SetBounds(c.X, listTop, c.Width, Math.Max(Theme.S(120), c.Bottom - listTop));
        }

        /* ---------------- 关于 ---------------- */

        private CardPanel _cardAbout;

        private void BuildAboutPage()
        {
            _pageAbout = NewPage();
            AddNav("关于", 0xE946, "ⓘ", _pageAbout);

            _cardAbout = new CardPanel();
            _cardAbout.Title = "关于 Touch+";
            _cardAbout.Subtitle = "希沃触控屏补丁 · 双击识别修复";
            _pageAbout.Controls.Add(_cardAbout);

            _aboutSheet = new AboutSheet();
            _cardAbout.Controls.Add(_aboutSheet);
            _btnOpenData = new AccentButton();
            _btnOpenData.Text = "打开数据目录";
            _btnOpenData.Kind = ButtonKind.Ghost;
            _btnOpenData.Click += delegate { OpenDataDirectory(); };
            _cardAbout.Controls.Add(_btnOpenData);

            _btnAdmin = new AccentButton();
            _btnAdmin.Text = "以管理员身份重启";
            _btnAdmin.Kind = ButtonKind.Ghost;
            _btnAdmin.Click += delegate { RestartAsAdmin(); };
            _cardAbout.Controls.Add(_btnAdmin);

            _btnResetAll = new AccentButton();
            _btnResetAll.Text = "恢复全部默认设置";
            _btnResetAll.Kind = ButtonKind.Danger;
            _btnResetAll.Click += delegate { ResetAllSettings(); };
            _cardAbout.Controls.Add(_btnResetAll);
        }


        private AccentButton _btnOpenData;
        private AccentButton _btnAdmin;
        private AccentButton _btnResetAll;

        private void LayoutAboutPage()
        {
            if (_cardAbout == null) return;
            _cardAbout.SetBounds(0, 0, _pageAbout.Width, _pageAbout.Height);
            Rectangle c = _cardAbout.ContentBounds;

            _aboutSheet.SetBounds(c.X, c.Y + Theme.S(4), c.Width, Math.Max(Theme.S(240), c.Height - Theme.S(84)));

            int y = Math.Max(c.Y + Theme.S(250), _aboutSheet.Bottom + Theme.S(16));
            _btnOpenData.SetBounds(c.X, y, Theme.S(140), Theme.S(36));
            _btnAdmin.SetBounds(c.X + Theme.S(152), y, Theme.S(170), Theme.S(36));
            _btnResetAll.SetBounds(c.X + Theme.S(334), y, Theme.S(180), Theme.S(36));
        }

        /* ================================================================== */
        /* 交互                                                                */
        /* ================================================================== */

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hover = -1;
            for (int i = 0; i < _nav.Count; i++)
            {
                if (_nav[i].Bounds.Contains(e.Location)) { hover = i; break; }
            }
            if (hover != _hoverNav) { _hoverNav = hover; Invalidate(); }


            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverNav = -1;

            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < _nav.Count; i++)
            {
                if (_nav[i].Bounds.Contains(e.Location)) { SelectNav(i); return; }
            }
            if (false)
            {
                SetAutostart(!_controller.Settings.AutoStart);
                return;
            }
            base.OnMouseDown(e);
        }

        /// <summary>内容区矩形：动画/重绘只作用于这块，避免整窗失效。</summary>
        private Rectangle ContentArea()
        {
            return new Rectangle(Theme.S(RailW), Theme.S(TitleH),
                Math.Max(1, ClientSize.Width - Theme.S(RailW)),
                Math.Max(1, ClientSize.Height - Theme.S(TitleH) - Theme.S(FooterH)));
        }

        /// <summary>三次贝塞尔近似（ease-out）：速度先快后慢，收尾不突兀。</summary>
        private static float EaseOut(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private Panel Veil()
        {
            if (_fadeVeil == null)
            {
                _fadeVeil = new VeilPanel();
                _fadeVeil.BackColor = Color.FromArgb(0, Theme.Bg);
                _fadeVeil.Visible = false;
                Controls.Add(_fadeVeil);
            }
            return _fadeVeil;
        }

        private Rectangle ContentRect()
        {
            return new Rectangle(Theme.S(RailW), 0,
                Math.Max(1, ClientSize.Width - Theme.S(RailW)), ClientSize.Height);
        }

        private void ApplyNavNow(int index)
        {
            if (index < 0 || index >= _nav.Count) return;
            _activeNav = index;
            for (int i = 0; i < _nav.Count; i++)
            {
                if (_nav[i].Page != null) _nav[i].Page.Visible = i == index;
            }
            Invalidate(ContentArea());
            Invalidate(new Rectangle(0, 0, Theme.S(RailW), ClientSize.Height));
        }

        private void TickPageAnimation()
        {
            _pageShift *= 0.62f;
            if (_pageShift < 0.8f) { _pageShift = 0f; _pageTimer.Stop(); }
            LayoutChrome();
        }
        public void SelectNav(int index)
        {
            if (index < 0 || index >= _nav.Count) return;
            _activeNav = index;
            for (int i = 0; i < _nav.Count; i++)
            {
                if (_nav[i].Page != null) _nav[i].Page.Visible = i == index;
            }
            _pageShift = 0f;   // 直接切换，不做过渡动画

            // 侧栏高亮 + 标题栏页名 + 内容区都要重绘
            Invalidate(ContentArea());
            Invalidate(new Rectangle(0, 0, Theme.S(RailW), ClientSize.Height));
            Invalidate(new Rectangle(Theme.S(RailW), 0, Math.Max(1, ClientSize.Width - Theme.S(RailW)), Theme.S(TitleH)));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                HideToTray();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /* ================================================================== */
        /* 设置同步                                                            */
        /* ================================================================== */

        private void LoadUiFromSettings()
        {
            _loadingUi = true;
            try
            {
                AppSettings s = _controller.Settings;
                _swSystem.SetCheckedSilently(s.SystemLayerEnabled);
                _swHook.SetCheckedSilently(s.HookLayerEnabled);
                _swMaster.SetCheckedSilently(s.PatchEnabled);
                _slRect.SetValueSilently(s.SystemDoubleClickWidth);
                _slTime.SetValueSilently(s.SystemDoubleClickTime);
                _slTolerance.SetValueSilently(s.Tolerance);
                _slWindow.SetValueSilently(s.TimeWindow);
                _slDrag.SetValueSilently(s.DragResetPx);
                _segSource.SetSelectedSilently((int)s.Source);
                _swSameWindow.SetCheckedSilently(s.RequireSameWindow);
                _swLongPress.SetCheckedSilently(s.EmulateLongPress);
                
                
                _lblRect.Text = RectHint();
            }
            finally
            {
                _loadingUi = false;
            }
        }

        private void RefreshState()
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(RefreshState)); return; }

            bool running = _controller.Settings.PatchEnabled && _controller.State != PatchState.Stopped;
            _heroTitle.Text = running ? "双击修复运行中" : "双击修复已暂停";
            // 副标题说"对你意味着什么"，而不是把标题栏已经有的参数再念一遍
            _heroSub.Text = running
                ? "两次点击落在 " + _controller.Settings.SystemDoubleClickWidth + "px 内、间隔 "
                  + _controller.Settings.SystemDoubleClickTime + "ms 内，系统就会识别为双击。\r\n输入层："
                  + (_controller.Hook.IsInstalled ? "已启用（超出范围时合成双击）" : "未启用，系统层已经够用")
                : "补丁已暂停，系统双击参数已还原为默认值。";

            _heroPill.SetState(running ? "运行中" : "已暂停",
                running ? Theme.OkSoft : Theme.WarnSoft, running ? Theme.Ok : Theme.Warn);

            _pillSystem.SetState(_controller.Settings.SystemLayerEnabled ? "已启用" : "已关闭",
                _controller.Settings.SystemLayerEnabled ? Theme.OkSoft : Theme.CardAlt,
                _controller.Settings.SystemLayerEnabled ? Theme.Ok : Theme.TextFaint);

            _pillHook.SetState(_controller.Settings.HookLayerEnabled ? "已启用" : "已关闭",
                _controller.Settings.HookLayerEnabled ? Theme.AccentSoft : Theme.CardAlt,
                _controller.Settings.HookLayerEnabled ? Theme.Accent : Theme.TextFaint);

            if (!_loadingUi)
            {
                _swMaster.SetCheckedSilently(_controller.Settings.PatchEnabled);
                _swSystem.SetCheckedSilently(_controller.Settings.SystemLayerEnabled);
                _swHook.SetCheckedSilently(_controller.Settings.HookLayerEnabled);
    
            }

            if (_tray != null)
            {
                _tray.Update(running, _controller.Settings.SystemLayerEnabled, _controller.Settings.HookLayerEnabled,
                    _controller.Settings.AutoStart,
                    "Touch+： " + (running ? "双击修复运行中" : "已暂停") + " ｜ " + DoubleClickParams.DescribeCurrent());
            }
            Invalidate();
        }

        private void RefreshStats()
        {
            RepairEngine engine = _controller.Engine;
            _tileRepairs.Value = engine.RepairedCount.ToString(CultureInfo.InvariantCulture);
            _tileDistance.Value = engine.AverageDistance.ToString(CultureInfo.InvariantCulture);
            TimeSpan span = DateTime.Now - _startedAt;
            _tileUptime.Value = span.TotalHours >= 1
                ? string.Format(CultureInfo.InvariantCulture, "{0:F1}", span.TotalHours)
                : ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture);
            _tileUptime.Unit = span.TotalHours >= 1 ? "小时" : "分钟";
            _tileRepairs.Invalidate();
            _tileDistance.Invalidate();
            _tileUptime.Invalidate();
        }

        private void SyncHookEngine()
        {
            AppSettings s = _controller.Settings;
            s.Tolerance = _slTolerance.Value;
            s.TimeWindow = _slWindow.Value;
            s.DragResetPx = _slDrag.Value;
            s.Source = (SourceMode)_segSource.SelectedIndex;
            s.RequireSameWindow = _swSameWindow.Checked;
            s.EmulateLongPress = _swLongPress.Checked;
            _controller.ApplySettings(false, true);
        }

        private void QueueApply()
        {
            _applyTimer.Stop();
            _applyTimer.Start();
        }

        private void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void ApplySystemLayerNow()
        {
            AppSettings s = _controller.Settings;
            s.SystemDoubleClickWidth = _slRect.Value;
            s.SystemDoubleClickHeight = _slRect.Value;
            s.SystemDoubleClickTime = _slTime.Value;
            _controller.ApplySettings(true, false);
            RefreshState();
            SaveSettings();
        }

        private void SaveSettings()
        {
            if (_controller.Settings.ReadOnlySession) return;   // --safe 模式不落盘
            SettingsStore.Save(_controller.Settings);
        }

        /* ---------------- 开关动作 ---------------- */

        private void ToggleMaster()
        {
            bool turnOn = !_controller.Settings.PatchEnabled;
            _controller.SetPaused(!turnOn);
            _controller.Settings.PatchEnabled = turnOn;
            SaveSettings();
            RefreshState();
            AppendLog((turnOn ? "已启用" : "已暂停") + "双击修复");
        }

        private void SetSystemLayer(bool enabled)
        {
            _controller.Settings.SystemLayerEnabled = enabled;
            _controller.ApplySettings(true, false);
            SaveSettings();
            RefreshState();
            AppendLog(enabled ? "已启用系统双击参数优化" : "已关闭系统双击参数优化（已还原系统默认）");
        }

        private void SetHookLayer(bool enabled)
        {
            if (enabled)
            {
                DialogResult result = MessageBox.Show(this,
                    "输入层修复需要安装全局鼠标钩子，并会在必要时拦截第二次点击、"
                    + "在锚点合成一次完整双击。\r\n\r\n"
                    + "· 已做过安全处理：注入完全异步，回调带超时看门狗，不会卡住系统输入；\r\n"
                    + "· 但部分安全软件可能会提示“模拟点击”，这是正常现象；\r\n"
                    + "· 如果系统层已经解决问题，建议保持关闭。\r\n\r\n"
                    + "确定要启用吗？",
                    "启用输入层修复", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (result != DialogResult.OK)
                {
                    _loadingUi = true;
                    _swHook.SetCheckedSilently(false);
                    _loadingUi = false;
                    return;
                }
            }

            _controller.Settings.HookLayerEnabled = enabled;
            SyncHookEngine();
            SaveSettings();
            RefreshState();
            AppendLog(enabled ? "已启用输入层智能修复" : "已关闭输入层智能修复");
        }

        private void SetAutostart(bool enabled)
        {
            bool ok = enabled
                ? Autostart.Enable(Application.ExecutablePath, _controller.Settings.StartMinimized)
                : Autostart.Disable();
            _controller.Settings.AutoStart = enabled && ok;
            SaveSettings();
            RefreshState();
            AppendLog(_controller.Settings.AutoStart ? "已设置开机自启" : "已取消开机自启");
        }

        private void RestoreSystemDefaults()
        {
            _controller.CaptureOriginals();
            _controller.RestoreSystemLayer();
            _controller.Settings.SystemDoubleClickWidth = 50;
            _controller.Settings.SystemDoubleClickHeight = 50;
            _controller.Settings.SystemDoubleClickTime = 800;
            _loadingUi = true;
            _slRect.SetValueSilently(50);
            _slTime.SetValueSilently(800);
            _lblRect.Text = RectHint();
            _loadingUi = false;
            SaveSettings();
            RefreshState();
            AppendLog("已还原为 Windows 默认双击参数（4×4px / 500ms）");
        }

        private void ResetAllSettings()
        {
            if (MessageBox.Show(this, "确定要把所有设置恢复为默认值吗？", "恢复默认设置",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            _controller.CaptureOriginals();
            _controller.RestoreSystemLayer();

            AppSettings fresh = new AppSettings();
            fresh.OriginalsCaptured = _controller.Settings.OriginalsCaptured;
            fresh.OriginalDoubleClickWidth = _controller.Settings.OriginalDoubleClickWidth;
            fresh.OriginalDoubleClickHeight = _controller.Settings.OriginalDoubleClickHeight;
            fresh.OriginalDoubleClickTime = _controller.Settings.OriginalDoubleClickTime;

            CopySettings(fresh, _controller.Settings);
            _controller.ApplySettings(true, true);
            _loadingUi = true;
            LoadUiFromSettings();
            _loadingUi = false;
            SaveSettings();
            RefreshState();
            AppendLog("已恢复全部默认设置");
        }

        private static void CopySettings(AppSettings from, AppSettings to)
        {
            to.PatchEnabled = from.PatchEnabled;
            to.SystemLayerEnabled = from.SystemLayerEnabled;
            to.SystemDoubleClickWidth = from.SystemDoubleClickWidth;
            to.SystemDoubleClickHeight = from.SystemDoubleClickHeight;
            to.SystemDoubleClickTime = from.SystemDoubleClickTime;
            to.HookLayerEnabled = from.HookLayerEnabled;
            to.Tolerance = from.Tolerance;
            to.TimeWindow = from.TimeWindow;
            to.Strategy = from.Strategy;
            to.Anchor = from.Anchor;
            to.Source = from.Source;
            to.RequireSameWindow = from.RequireSameWindow;
            to.DragResetPx = from.DragResetPx;
            to.EmulateLongPress = from.EmulateLongPress;
            to.LongPressGuardMs = from.LongPressGuardMs;
            to.ExcludedProcesses = new List<string>(from.ExcludedProcesses);
            to.AutoStart = from.AutoStart;
            to.StartMinimized = from.StartMinimized;
            to.ShowTrayTips = from.ShowTrayTips;
            to.VerboseLog = from.VerboseLog;
            to.HotkeyEnabled = from.HotkeyEnabled;
            to.ExitRestoreSystem = from.ExitRestoreSystem;
            to.OriginalsCaptured = from.OriginalsCaptured;
            to.OriginalDoubleClickWidth = from.OriginalDoubleClickWidth;
            to.OriginalDoubleClickHeight = from.OriginalDoubleClickHeight;
            to.OriginalDoubleClickTime = from.OriginalDoubleClickTime;
            to.Normalize();
        }

        /* ---------------- 日志 ---------------- */

        private void OnPairReported(object sender, PairReport report)
        {
            _pendingReport = report;
            _padBaseline = _pad == null ? 0 : _pad.NativeDoubleClicks;
            if (_padTimer != null)
            {
                _padTimer.Stop();
                _padTimer.Start();
            }
        }

        private void OnPadTimer(object sender, EventArgs e)
        {
            _padTimer.Stop();
            if (_pendingReport == null || _pad == null) return;
            bool native = _pad.NativeDoubleClicks > _padBaseline;
            _pad.ReportPair(_pendingReport, native);
            _pendingReport = null;
        }

        private void OnLogEntry(object sender, LogEntry entry)
        {
            if (entry == null || _logView == null) return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(delegate { OnLogEntry(sender, entry); })); }
                catch (Exception) { }
                return;
            }

            Color color = Theme.TextDim;
            switch (entry.Level)
            {
                case LogLevel.Repair: color = Theme.Accent; break;
                case LogLevel.Warn: color = Theme.Warn; break;
                case LogLevel.Error: color = Theme.Err; break;
                case LogLevel.Info: color = Theme.TextDim; break;
                default: color = Theme.TextFaint; break;
            }
            _logView.Add(entry.TimeText, entry.LevelText, entry.Message, color);
        }

        private void AppendLog(string text)
        {
            Log.Instance.Info(text);
        }

        private void ExportLog()
        {
            try
            {
                string path = Path.Combine(SettingsStore.DataDirectory,
                    "touchplus-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
                List<string> lines = new List<string>(_logView.SnapshotLines());
                lines.Insert(0, "Touch+ 运行日志导出  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
                AppendLog("日志已导出：" + path);
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                AppendLog("导出日志失败：" + ex.Message);
            }
        }

        private void OpenDataDirectory()
        {
            try
            {
                SettingsStore.EnsureDirectory();
                Process.Start(SettingsStore.DataDirectory);
            }
            catch (Exception ex)
            {
                AppendLog("打开数据目录失败：" + ex.Message);
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, "--show");
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                ExitApplication();
            }
            catch (Exception ex)
            {
                AppendLog("以管理员身份重启失败（可能被取消）：" + ex.Message);
            }
        }

        /* ---------------- 托盘 / 退出 ---------------- */

        public void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
            if (!_balloonShown && _controller.Settings.ShowTrayTips && _tray != null)
            {
                _balloonShown = true;
                _tray.ShowBalloon("Touch+ 已在后台运行", "双击托盘图标可重新打开主界面；右键可暂停或退出。");
            }
        }

        public void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ExitApplication()
        {
            _reallyExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 作为插件设置窗口打开时，关闭就是关闭，不要再最小化到托盘（那里没有托盘图标）
            if (!_reallyExit && !_asSettings && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            try
            {
                if (_controller.Settings.ExitRestoreSystem)
                {
                    _controller.CaptureOriginals();
                    _controller.RestoreSystemLayer();
                }
                SaveSettings();
                _controller.Dispose();
                if (_tray != null) _tray.Dispose();
            }
            catch (Exception)
            {
            }

            base.OnFormClosing(e);
        }

        /* ---------------- 热键 ---------------- */

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_controller.Settings.HotkeyEnabled)
            {
                Win32.RegisterHotKey(Handle, HotkeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x54);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { Win32.UnregisterHotKey(Handle, HotkeyId); }
            catch (Exception) { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                ToggleMaster();
                return;
            }
            base.WndProc(ref m);
        }

        private string _aboutTextCache;

        private static int GetDoubleClickTimeForAbout()
        {
            try { return (int)Win32.GetDoubleClickTime(); }
            catch (Exception) { return 0; }
        }

        private void RefreshAboutText()
        {
            if (_aboutSheet == null) return;
            if (_cardAbout != null)
            {
                _cardAbout.Title = _plugin != null ? "关于 " + _plugin.Name : "关于 Touch+";
                _cardAbout.Subtitle = _plugin != null
                    ? "这个插件做什么 · 它改了本机什么 · 怎么收回"
                    : "希沃触控屏补丁 · 双击识别修复";
            }
            StringBuilder sb = new StringBuilder();
            if (_asSettings && _plugin != null)
            {
                // 这里是"插件设置窗口"，关于页只讲这个插件；宿主信息在插件中心的"关于 Touch+"里
                sb.AppendLine("插件：" + _plugin.Name);
                sb.AppendLine("标识：" + _plugin.Id);
                sb.AppendLine("安全级别：" + (_plugin.Safety == Core.Plugins.PluginSafety.Safe
                    ? "安全（只改 Windows 自己的参数，可一键还原）"
                    : "高级（需要钩子或合成输入）"));
                sb.AppendLine("状态：" + (_plugin.Enabled ? "已启用" : "未启用"));
                sb.AppendLine();
                sb.AppendLine("它解决什么：" + _plugin.Summary);
                sb.AppendLine();
                sb.AppendLine("对本机做的改动：");
                sb.AppendLine("  · 放宽 Windows 双击矩形与时间窗（HKCU\\Control Panel\\Mouse）");
                sb.AppendLine("  · 原值已备份，点「还原系统默认」或停用插件都会收回");
                sb.AppendLine("  · 不写系统目录、不装服务、不驻留驱动");
                sb.AppendLine();
                sb.AppendLine("运行于 Touch+ v" + Program.Version + "，插件接口 " + PluginApiInfo.Version);
                _aboutTextCache = sb.ToString();
                _aboutSheet.Bind(_plugin, _plugin != null && _plugin.Enabled, Program.Version, PluginApiInfo.Version,
                DoubleClickParams.GetRectWidth(), (int)Math.Max(1, GetDoubleClickTimeForAbout()));
                return;
            }
            sb.AppendLine("版本：" + Program.Version);
            sb.AppendLine("系统：" + SystemInfo.VersionText);
            sb.AppendLine("权限：" + (SystemInfo.IsElevated ? "管理员" : "普通用户（补丁本身不需要管理员）"));
            sb.AppendLine("数字化仪：" + SystemInfo.DescribeDigitizer());
            sb.AppendLine("当前系统双击参数：" + DoubleClickParams.DescribeCurrent());
            sb.AppendLine("程序目录：" + Application.ExecutablePath);
            sb.AppendLine("数据目录：" + SettingsStore.DataDirectory);
            sb.AppendLine("日志文件：" + (Log.Instance.FilePath ?? "（未启用文件日志）"));
            sb.AppendLine();
            sb.AppendLine("本程序解决的问题：触控屏手指双击时两次落点偏差较大，");
            sb.AppendLine("Windows 要求两次点击落在同一个 4×4 像素矩形内才算双击，");
            sb.AppendLine("因此第二下常常被识别成独立的单击，软件打不开。");
            sb.AppendLine();
            sb.AppendLine("修复方式：放宽 Windows 自己的双击矩形与时间窗（系统原生识别），");
            sb.AppendLine("必要时再由输入层钩子兜底（默认关闭）。");
            _aboutTextCache = sb.ToString();
            _aboutSheet.Bind(_plugin, _plugin != null && _plugin.Enabled, Program.Version, PluginApiInfo.Version,
                DoubleClickParams.GetRectWidth(), (int)Math.Max(1, GetDoubleClickTimeForAbout()));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Log.Instance.Debug("界面：DPI={0:F0} 缩放={1:F2} 客户区={2}x{3}",
                Theme.CurrentDpi, Theme.Scale, ClientSize.Width, ClientSize.Height);
            SelectNav(InitialPage);
            RefreshAboutText();
            AppendLog("Touch+ 已启动（" + (SystemInfo.IsElevated ? "管理员" : "普通用户") + "）");
            AppendLog(DoubleClickParams.DescribeCurrent());
            if (!SystemInfo.HasTouchOrPen)
            {
                AppendLog("提示：本机未检测到触摸数字化仪，补丁仍然可用，但如果是普通鼠标环境可能不需要。");
            }
        }
    }
}
namespace TouchPlus.UI
{
    /// <summary>切页淡入淡出用的遮罩：支持透明背景色，只作为视觉蒙层存在。</summary>
    internal sealed class VeilPanel : System.Windows.Forms.Panel
    {
        public VeilPanel()
        {
            SetStyle(System.Windows.Forms.ControlStyles.SupportsTransparentBackColor
                   | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer
                   | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint, true);
            TabStop = false;
            Enabled = true;
        }
    }
}