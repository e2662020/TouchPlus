using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using TouchPlus.Core;

namespace TouchPlus.Verify
{
    /// <summary>
    /// 可行性验证工具 —— 纯观察模式。
    ///
    /// 安全承诺（本工具的设计前提）：
    ///   1. 绝不注入任何输入（不调用 SendInput / mouse_event）；
    ///   2. 钩子处于 ObserveOnly 模式：不吞事件、不移动光标、不改坐标，每个事件原样放行；
    ///   3. 不设置 TopMost、不创建无边框窗口，任何时候都能正常关闭；
    ///   4. 退出时还原系统双击参数（可勾选保留）。
    ///
    /// 它只做三件事：
    ///   ① 读环境（系统版本、数字化仪、当前双击矩形/时间）；
    ///   ② 允许一键放宽系统双击矩形与时间窗，然后由你自己去桌面双击图标验证；
    ///   ③ 观察并记录手指点击的真实数据（落点、偏离、间隔、来源，以及系统是否识别为双击）。
    /// </summary>
    internal sealed class VerifyForm : Form
    {
        private readonly PatchController _controller;
        private readonly List<string> _reportLines = new List<string>();

        private TextBox _envBox;
        private TextBox _logBox;
        private CheckBox _chkSystemLayer;
        private CheckBox _chkObserve;
        private CheckBox _chkKeepOnExit;
        private NumericUpDown _numRect;
        private NumericUpDown _numTime;
        private TestPadPanel _pad;
        private Label _padInfo;
        private TextBox _remarkBox;
        private Button _btnApply;
        private Button _btnRestore;
        private Button _btnExport;
        private Button _btnOk;
        private Button _btnBad;

        private string _desktopVerdict = "（未记录）";
        private int _tapCount;
        private DateTime _lastDoubleClickAt = DateTime.MinValue;
        private int _nativeDoubleClicks;
        private string _autoReportPath;
        private int _autoCloseSeconds;
        private DateTime _closeAt = DateTime.MinValue;

        public VerifyForm(string[] args)
        {
            Text = "Touch+ 双击修复 · 观察式验证工具 v1.1（不会注入任何输入）";
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 700);
            MinimumSize = new Size(900, 640);
            BackColor = Color.FromArgb(244, 246, 249);

            ParseArgs(args);

            SettingsLoadResult loaded = SettingsStore.Load();
            _controller = new PatchController(loaded.Settings);
            _controller.Engine.PairReported += OnPairReported;
            _controller.Hook.ButtonDownObserved += OnButtonDownObserved;

            BuildUi();
            Log.Instance.EntryAdded += OnLogEntry;

            Application.Idle += OnIdle;

            RefreshEnvironment();
            AppendLog("工具已启动（纯观察模式：不会修改你的点击，也不会注入任何输入）。");
            AppendLog("建议流程：① 点【放宽系统双击参数】→ ② 去桌面双击图标试 3 次 → ③ 回来记录结论 → ④ 导出报告。");
            AppendLog("中途也可以在本窗口的测试区点两下，看看两次落点实际差多少像素。");

            if (_autoCloseSeconds > 0) _closeAt = DateTime.Now.AddSeconds(_autoCloseSeconds);
            ApplyObserve();
        }

        private void ParseArgs(string[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--report" && i + 1 < args.Length) _autoReportPath = args[i + 1];
                else if (a == "--autoclose" && i + 1 < args.Length)
                {
                    int sec;
                    if (int.TryParse(args[i + 1], out sec)) _autoCloseSeconds = sec;
                }
            }
        }

        /* ------------------------------------------------------------------ */

        private void BuildUi()
        {
            int m = 12;
            int leftWidth = 430;
            int rightX = m * 2 + leftWidth;
            int rightWidth = ClientSize.Width - rightX - m;

            /* ---------- 左列：环境 ---------- */
            GroupBox envGroup = new GroupBox();
            envGroup.Text = "① 环境自检";
            envGroup.Bounds = new Rectangle(m, m, leftWidth, 158);
            envGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(envGroup);

            _envBox = new TextBox();
            _envBox.Multiline = true;
            _envBox.ReadOnly = true;
            _envBox.ScrollBars = ScrollBars.Vertical;
            _envBox.BorderStyle = BorderStyle.None;
            _envBox.BackColor = Color.White;
            _envBox.Font = new Font("Consolas", 9f);
            _envBox.Bounds = new Rectangle(10, 22, leftWidth - 24, 128);
            envGroup.Controls.Add(_envBox);

            /* ---------- 左列：系统层 ---------- */
            GroupBox sysGroup = new GroupBox();
            sysGroup.Text = "② 系统层：放宽双击矩形与时间窗（推荐先试这个）";
            sysGroup.Bounds = new Rectangle(m, m + 166, leftWidth, 152);
            Controls.Add(sysGroup);

            _chkSystemLayer = new CheckBox();
            _chkSystemLayer.Text = "启用（改完立刻全系统生效，点【还原】即可恢复）";
            _chkSystemLayer.Checked = true;
            _chkSystemLayer.Bounds = new Rectangle(12, 24, leftWidth - 30, 22);
            sysGroup.Controls.Add(_chkSystemLayer);

            Label l1 = new Label();
            l1.Text = "双击矩形(px)：";
            l1.Bounds = new Rectangle(26, 54, 100, 22);
            sysGroup.Controls.Add(l1);

            _numRect = new NumericUpDown();
            _numRect.Minimum = 4;
            _numRect.Maximum = 200;
            _numRect.Increment = 4;
            _numRect.Value = 50;
            _numRect.Bounds = new Rectangle(128, 52, 70, 24);
            sysGroup.Controls.Add(_numRect);

            Label l2 = new Label();
            l2.Text = "双击时间(ms)：";
            l2.Bounds = new Rectangle(212, 54, 100, 22);
            sysGroup.Controls.Add(l2);

            _numTime = new NumericUpDown();
            _numTime.Minimum = 200;
            _numTime.Maximum = 2000;
            _numTime.Increment = 50;
            _numTime.Value = 800;
            _numTime.Bounds = new Rectangle(318, 52, 74, 24);
            sysGroup.Controls.Add(_numTime);

            _btnApply = new Button();
            _btnApply.Text = "放宽系统双击参数";
            _btnApply.Bounds = new Rectangle(26, 88, 160, 32);
            _btnApply.Click += delegate { ApplySystemLayer(); };
            sysGroup.Controls.Add(_btnApply);

            _btnRestore = new Button();
            _btnRestore.Text = "还原系统默认";
            _btnRestore.Bounds = new Rectangle(196, 88, 130, 32);
            _btnRestore.Click += delegate { RestoreSystemLayer(); };
            sysGroup.Controls.Add(_btnRestore);

            _chkKeepOnExit = new CheckBox();
            _chkKeepOnExit.Text = "退出时保留（默认退出即还原）";
            _chkKeepOnExit.Bounds = new Rectangle(26, 124, leftWidth - 40, 22);
            sysGroup.Controls.Add(_chkKeepOnExit);

            /* ---------- 左列：结论 ---------- */
            GroupBox verdictGroup = new GroupBox();
            verdictGroup.Text = "④ 去桌面双击图标的结论（试 3 次再回来点）";
            verdictGroup.Bounds = new Rectangle(m, m + 326, leftWidth, 150);
            Controls.Add(verdictGroup);

            _btnOk = new Button();
            _btnOk.Text = "能打开了 ✔";
            _btnOk.Bounds = new Rectangle(14, 26, 130, 32);
            _btnOk.Click += delegate { SetDesktopVerdict("能打开 ✔"); };
            verdictGroup.Controls.Add(_btnOk);

            _btnBad = new Button();
            _btnBad.Text = "还是打不开 ✘";
            _btnBad.Bounds = new Rectangle(154, 26, 130, 32);
            _btnBad.Click += delegate { SetDesktopVerdict("还是打不开 ✘"); };
            verdictGroup.Controls.Add(_btnBad);

            Label lr = new Label();
            lr.Text = "备注（例如：要点几次才开、抖动大概多大、哪个软件里不行）：";
            lr.Bounds = new Rectangle(14, 64, leftWidth - 30, 20);
            verdictGroup.Controls.Add(lr);

            _remarkBox = new TextBox();
            _remarkBox.Multiline = true;
            _remarkBox.Bounds = new Rectangle(14, 86, leftWidth - 34, 52);
            verdictGroup.Controls.Add(_remarkBox);

            /* ---------- 右列：测试区 ---------- */
            GroupBox padGroup = new GroupBox();
            padGroup.Text = "③ 触控观察区（用手指/笔在这里点两下，只观察不干预）";
            padGroup.Bounds = new Rectangle(rightX, m, rightWidth, 300);
            padGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(padGroup);

            _pad = new TestPadPanel();
            _pad.Bounds = new Rectangle(12, 24, rightWidth - 28, 180);
            _pad.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            padGroup.Controls.Add(_pad);

            _padInfo = new Label();
            _padInfo.Bounds = new Rectangle(14, 210, rightWidth - 32, 80);
            _padInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _padInfo.Text = "等待第一次点击…";
            padGroup.Controls.Add(_padInfo);

            /* ---------- 右列：日志 ---------- */
            GroupBox logGroup = new GroupBox();
            logGroup.Text = "⑤ 事件记录";
            logGroup.Bounds = new Rectangle(rightX, m + 308, rightWidth, ClientSize.Height - m * 2 - 308 - 44);
            logGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(logGroup);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.WordWrap = false;
            _logBox.Font = new Font("Consolas", 8.5f);
            _logBox.BackColor = Color.White;
            _logBox.Bounds = new Rectangle(10, 20, rightWidth - 24, logGroup.Height - 34);
            _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logGroup.Controls.Add(_logBox);

            /* ---------- 底部按钮 ---------- */
            _chkObserve = new CheckBox();
            _chkObserve.Text = "启用点击观察（只读，绝不修改点击）";
            _chkObserve.Checked = true;
            _chkObserve.Bounds = new Rectangle(rightX, ClientSize.Height - m - 30, 300, 24);
            _chkObserve.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _chkObserve.CheckedChanged += delegate { ApplyObserve(); };
            Controls.Add(_chkObserve);

            _btnExport = new Button();
            _btnExport.Text = "导出报告";
            _btnExport.Bounds = new Rectangle(ClientSize.Width - m - 250, ClientSize.Height - m - 34, 110, 34);
            _btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnExport.Click += delegate { ExportReport(); };
            Controls.Add(_btnExport);

            Button btnExit = new Button();
            btnExit.Text = "退出";
            btnExit.Bounds = new Rectangle(ClientSize.Width - m - 130, ClientSize.Height - m - 34, 110, 34);
            btnExit.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnExit.Click += delegate { Close(); };
            Controls.Add(btnExit);
        }

        /* ------------------------------------------------------------------ */

        private void RefreshEnvironment()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("系统版本    : " + SystemInfo.VersionText);
            sb.AppendLine("程序架构    : " + (IntPtr.Size == 8 ? "x64" : "x86") + " / .NET " + Environment.Version);
            sb.AppendLine("管理员权限  : " + (SystemInfo.IsElevated ? "是" : "否（建议右键以管理员身份运行）"));
            sb.AppendLine("数字化仪    : " + SystemInfo.DescribeDigitizer());
            sb.AppendLine("屏幕范围    : " + SystemInfo.VirtualScreenWidth + "×" + SystemInfo.VirtualScreenHeight);
            sb.AppendLine("当前双击参数: " + DoubleClickParams.DescribeCurrent());
            sb.AppendLine("观察钩子    : " + (_controller.Hook.IsInstalled ? "已安装（只读模式）" : "未安装"));
            _envBox.Text = sb.ToString();
        }

        private void ApplySystemLayer()
        {
            AppSettings s = _controller.Settings;
            s.PatchEnabled = true;
            s.SystemLayerEnabled = _chkSystemLayer.Checked;
            s.SystemDoubleClickWidth = (int)_numRect.Value;
            s.SystemDoubleClickHeight = (int)_numRect.Value;
            s.SystemDoubleClickTime = (int)_numTime.Value;
            s.HookLayerEnabled = false;   // 本工具绝不启用会改写点击的输入层
            s.Normalize();
            _controller.CaptureOriginals();

            if (s.SystemLayerEnabled) _controller.ApplySystemLayer();
            else _controller.RestoreSystemLayer();

            SettingsStore.Save(s);
            AppendLog("── 已应用系统层参数 ──");
            AppendLog("   " + DoubleClickParams.DescribeCurrent());
            AppendLog("   现在去桌面双击图标试 3 次，再回来点「能打开 / 还是打不开」。");
            RefreshEnvironment();
        }

        private void RestoreSystemLayer()
        {
            _controller.CaptureOriginals();
            _controller.RestoreSystemLayer();
            AppendLog("── 已还原系统双击参数 ── " + DoubleClickParams.DescribeCurrent());
            RefreshEnvironment();
        }

        private void ApplyObserve()
        {
            RepairEngine engine = _controller.Engine;
            engine.Options.Enabled = true;
            engine.Options.TolerancePx = 200;      // 观察模式下放宽，只为多记录一点数据
            engine.Options.TimeWindowMs = 1500;
            engine.Options.Source = SourceMode.All;
            engine.Options.RequireSameWindow = false;
            engine.Options.Strategy = RepairStrategy.Reposition;
            engine.Options.SystemHasDigitizer = SystemInfo.HasTouchOrPen;
            engine.Options.SystemRectWidth = DoubleClickParams.GetRectWidth();
            engine.Options.SystemRectHeight = DoubleClickParams.GetRectHeight();
            engine.Options.SystemTime = (uint)DoubleClickParams.GetTime();
            engine.Options.Excluded = new List<string>();

            // 观察模式：只读，绝不吞事件、绝不注入
            _controller.Hook.ObserveOnly = true;
            _controller.Hook.DryRun = true;
            _controller.Hook.SwallowAll = false;
            _controller.Hook.TreatInjectedAsPhysical = false;

            if (_chkObserve == null) return;
            if (_chkObserve.Checked)
            {
                if (!_controller.Hook.IsInstalled && !_controller.Hook.Install())
                    AppendLog("观察钩子安装失败（错误码 " + _controller.Hook.LastError + "）");
            }
            else if (_controller.Hook.IsInstalled)
            {
                _controller.Hook.Uninstall();
            }
            RefreshEnvironment();
        }

        private void SetDesktopVerdict(string verdict)
        {
            _desktopVerdict = verdict;
            AppendLog("【桌面双击图标结论】" + verdict);
            RefreshEnvironment();
        }

        /* ------------------------------------------------------------------ */

        private void OnButtonDownObserved(object sender, ClickSample sample)
        {
            _tapCount++;
            AppendLog(string.Format(CultureInfo.InvariantCulture,
                "{0} 点击 #{1}  坐标({2},{3})  来源={4}  目标进程={5}",
                DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                _tapCount, sample.X, sample.Y, KindText(sample.Kind),
                string.IsNullOrEmpty(sample.ProcessName) ? "-" : sample.ProcessName));
        }

        private void OnPairReported(object sender, PairReport report)
        {
            if (report == null || !report.HasPair) return;
            string verdict = report.Repaired
                ? "按设定应当修复（本工具未启用修复）"
                : (report.NativeOk ? "系统原生就能识别" : "系统识别不了（正是要修的场景）");
            AppendLog(string.Format(CultureInfo.InvariantCulture,
                "    配对：偏离 {0}px，间隔 {1}ms，来源 {2}/{3} → {4}；{5}",
                report.Distance, report.DeltaMs, KindText(report.Kind1), KindText(report.Kind2),
                verdict, report.Reason));
        }

        private void OnLogEntry(object sender, LogEntry entry)
        {
            if (entry == null) return;
            if (entry.Level == LogLevel.Warn || entry.Level == LogLevel.Error)
                AppendLog("    [日志]" + entry.Message);
        }

        private void OnIdle(object sender, EventArgs e)
        {
            if (_pad == null || _padInfo == null) return;

            if (_pad.NativeDoubleClicks != _nativeDoubleClicks)
            {
                _nativeDoubleClicks = _pad.NativeDoubleClicks;
                _lastDoubleClickAt = DateTime.Now;
            }

            PairReport p = _controller.Engine.LastPair;
            if (p != null && p.HasPair)
            {
                bool native = (DateTime.Now - _lastDoubleClickAt).TotalMilliseconds < 600;
                _padInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "最近两次点击：({0},{1}) → ({2},{3})   偏离 {4}px，间隔 {5}ms，来源 {6}/{7}\r\n"
                    + "系统判定：{8}    Touch+ 判定：{9}",
                    p.X1, p.Y1, p.X2, p.Y2, p.Distance, p.DeltaMs,
                    KindText(p.Kind1), KindText(p.Kind2),
                    native ? "识别为双击 ✔" : "未识别为双击 ✘",
                    p.NativeOk ? "系统原生可识别，无需介入" : (p.Repaired ? "会自动修复" : "超出容差/时间窗，不修复"));
            }
            else
            {
                _padInfo.Text = "等待第一次点击…（已记录 " + _tapCount + " 次点击）";
            }

            if (_closeAt != DateTime.MinValue && DateTime.Now >= _closeAt)
            {
                AppendLog("已到自动关闭时间，退出。");
                Close();
            }
        }

        private static string KindText(InputKind kind)
        {
            switch (kind)
            {
                case InputKind.Touch: return "触控";
                case InputKind.Pen: return "笔";
                case InputKind.Mouse: return "鼠标";
                default: return "未知";
            }
        }

        private void AppendLog(string text)
        {
            _reportLines.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + text);
            if (_logBox.Lines.Length > 600)
            {
                List<string> keep = new List<string>(_logBox.Lines);
                keep.RemoveRange(0, 200);
                _logBox.Lines = keep.ToArray();
            }
            _logBox.AppendText(text + Environment.NewLine);
        }

        private void ExportReport()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Touch+ 触控屏双击修复 · 观察式验证报告");
                sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine(new string('=', 72));
                sb.AppendLine("【环境】");
                sb.AppendLine(_envBox.Text);
                sb.AppendLine("【系统层设置】");
                sb.AppendLine("开关：" + (_chkSystemLayer.Checked ? "开" : "关") +
                    "，双击矩形 " + _numRect.Value + "px，双击时间 " + _numTime.Value + "ms");
                sb.AppendLine("【桌面双击图标结论】" + _desktopVerdict);
                sb.AppendLine("备注：" + (_remarkBox.Text.Length > 0 ? _remarkBox.Text : "（无）"));
                sb.AppendLine();
                sb.AppendLine("【统计】");
                sb.AppendLine("记录点击数：" + _tapCount);
                sb.AppendLine("测试区收到系统双击：" + _pad.NativeDoubleClicks + " 次");
                sb.AppendLine("配对次数：" + _controller.Engine.PairCount +
                    "（系统原生识别 " + _controller.Engine.NativePairCount +
                    "，超出容差 " + _controller.Engine.RejectedCount + "）");
                sb.AppendLine("触控/笔点击：" + _controller.Engine.TouchClickCount +
                    "，鼠标点击：" + _controller.Engine.MouseClickCount);
                if (_controller.Engine.PairCount > 0)
                {
                    sb.AppendLine("平均偏离：" + _controller.Engine.AverageDistance + "px，最大偏离：" +
                        _controller.Engine.MaxDistance + "px");
                }
                sb.AppendLine();
                sb.AppendLine("【事件明细】");
                foreach (string line in _reportLines) sb.AppendLine(line);

                string path = _autoReportPath;
                if (string.IsNullOrEmpty(path))
                    path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Touch+验证报告.txt");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                AppendLog("报告已导出：" + path);
            }
            catch (Exception ex)
            {
                AppendLog("导出报告失败：" + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                _controller.Hook.Uninstall();
                if (!_chkKeepOnExit.Checked)
                {
                    _controller.CaptureOriginals();
                    _controller.RestoreSystemLayer();
                }
                _controller.Dispose();
            }
            catch (Exception)
            {
            }
            base.OnFormClosing(e);
        }
    }

    /// <summary>测试面板：只统计收到多少次按下、多少次系统双击，不修改任何输入。</summary>
    internal sealed class TestPadPanel : Panel
    {
        public int Downs;
        public int Ups;
        public int NativeDoubleClicks;
        private readonly Label _hint;

        public TestPadPanel()
        {
            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;

            _hint = new Label();
            _hint.Text = "在这里用手指/笔连续点两下\r\n（本面板只观察，不改变你的点击）";
            _hint.TextAlign = ContentAlignment.MiddleCenter;
            _hint.Dock = DockStyle.Fill;
            _hint.ForeColor = Color.FromArgb(120, 128, 140);
            _hint.Font = new Font("Microsoft YaHei UI", 12f);
            _hint.Enabled = false;   // 让点击穿透到面板本身
            Controls.Add(_hint);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_LBUTTONUP = 0x0202;
            const int WM_LBUTTONDBLCLK = 0x0203;

            if (m.Msg == WM_LBUTTONDOWN) Downs++;
            else if (m.Msg == WM_LBUTTONUP) Ups++;
            else if (m.Msg == WM_LBUTTONDBLCLK) NativeDoubleClicks++;

            base.WndProc(ref m);
        }
    }
}
