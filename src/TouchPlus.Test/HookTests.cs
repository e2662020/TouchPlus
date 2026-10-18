using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using TouchPlus.Core;

namespace TouchPlus.Test
{
    /// <summary>用于端到端实验的探针面板：记录收到的左键按下/抬起/双击。</summary>
    internal sealed class ProbePanel : Panel
    {
        public int Downs;
        public int Ups;
        public int DoubleClicks;
        public readonly List<string> Trace = new List<string>();

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_LBUTTONUP = 0x0202;
            const int WM_LBUTTONDBLCLK = 0x0203;

            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
            {
                int lp = m.LParam.ToInt32();
                int cx = (short)(lp & 0xFFFF);
                int cy = (short)((lp >> 16) & 0xFFFF);
                Point screen = PointToScreen(new Point(cx, cy));
                string label = m.Msg == WM_LBUTTONDBLCLK ? "双击" : "按下";
                if (m.Msg == WM_LBUTTONDOWN) Downs++; else DoubleClicks++;
                Trace.Add(string.Format(CultureInfo.InvariantCulture, "{0} 客户区({1},{2}) 屏幕({3},{4})",
                    label, cx, cy, screen.X, screen.Y));
            }
            else if (m.Msg == WM_LBUTTONUP)
            {
                Ups++;
            }

            base.WndProc(ref m);
        }

        public string Summary()
        {
            return string.Format(CultureInfo.InvariantCulture, "按下 {0} / 抬起 {1} / 双击 {2}", Downs, Ups, DoubleClicks);
        }

        public void Reset()
        {
            Downs = 0;
            Ups = 0;
            DoubleClicks = 0;
            Trace.Clear();
        }
    }

    /// <summary>系统层实验用的居中探针窗口（完全不使用钩子）。</summary>
    internal sealed class RectProbeForm : Form
    {
        public readonly ProbePanel Pad;

        public RectProbeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(18, 20, 26);
            Size = new Size(600, 340);

            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);

            Pad = new ProbePanel();
            Pad.Bounds = new Rectangle(20, 20, 560, 300);
            Pad.BackColor = Color.FromArgb(28, 62, 82);
            Pad.Trace.Add("测试面板");
            Controls.Add(Pad);
        }
    }

    internal sealed class ProbeForm : Form
    {
        public readonly ProbePanel A;
        public readonly ProbePanel B;

        public ProbeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(16, 18, 24);
            Text = "Touch+ 自动化实验";

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Bounds = screen;

            Label caption = new Label();
            caption.AutoSize = false;
            caption.Bounds = new Rectangle(0, 0, screen.Width, 60);
            caption.TextAlign = ContentAlignment.MiddleCenter;
            caption.ForeColor = Color.FromArgb(200, 210, 225);
            caption.Font = new Font("Segoe UI", 14f);
            caption.Text = "Touch+ 自动化测试窗口（约 2 秒后自动关闭）";
            Controls.Add(caption);

            A = new ProbePanel();
            A.Bounds = new Rectangle(80, 120, 160, 160);
            A.BackColor = Color.FromArgb(24, 96, 116);
            A.Trace.Add("面板A");

            B = new ProbePanel();
            B.Bounds = new Rectangle(screen.Width - 320, screen.Height - 360, 160, 160);
            B.BackColor = Color.FromArgb(96, 52, 132);
            B.Trace.Add("面板B");

            Controls.Add(A);
            Controls.Add(B);
        }
    }

    internal static class HookTests
    {
        public static void Pump(int milliseconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Win32.MSG msg;
            while (sw.ElapsedMilliseconds < milliseconds)
            {
                while (Win32.PeekMessage(out msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
                {
                    Win32.TranslateMessage(ref msg);
                    Win32.DispatchMessage(ref msg);
                }
                Application.DoEvents();
                Thread.Sleep(2);
            }
        }

        private static RepairEngine NewEngine(RepairStrategy strategy, AnchorMode anchor)
        {
            AppSettings settings = new AppSettings();
            settings.Normalize();
            settings.Source = SourceMode.All;
            settings.Strategy = strategy;
            settings.Anchor = anchor;
            settings.Tolerance = 60;
            settings.TimeWindow = 900;

            RepairEngine engine = new RepairEngine(Log.Instance);
            engine.Options.CopyFrom(settings);
            engine.Options.SystemHasDigitizer = false;
            engine.Options.SystemRectWidth = 4;
            engine.Options.SystemRectHeight = 4;
            engine.Options.SystemTime = 500;
            return engine;
        }

        /* ------------------------------------------------------------------ */

        public static void RunEngineThroughHook()
        {
            T.Section("钩子链路：注入事件 → 引擎判定（隔离模式，不影响桌面）");

            RepairEngine engine = NewEngine(RepairStrategy.Reposition, AnchorMode.First);
            MouseHook hook = new MouseHook(engine);
            hook.TreatInjectedAsPhysical = true;
            hook.SwallowAll = true;
            hook.DryRun = true;

            T.Check(hook.Install(), "安装 WH_MOUSE_LL 全局鼠标钩子",
                hook.IsInstalled ? "句柄有效" : ("错误码 " + hook.LastError));

            int savedX, savedY;
            Injector.GetCursor(out savedX, out savedY);

            try
            {
                PairReport report = null;
                engine.PairReported += delegate(object sender, PairReport r) { report = r; };

                Rectangle screen = Screen.PrimaryScreen.Bounds;
                int baseX = screen.Left + screen.Width / 2;
                int baseY = screen.Top + screen.Height / 2;

                Injector.ClickAt(baseX, baseY, 20, false);
                Pump(70);
                Injector.ClickAt(baseX + 34, baseY + 16, 20, false);
                Pump(160);

                T.Check(hook.DownsObserved >= 2, "钩子收到两次左键按下", "收到 " + hook.DownsObserved + " 次");
                T.Check(hook.UpsObserved >= 2, "钩子收到两次左键抬起", "收到 " + hook.UpsObserved + " 次");
                T.Check(engine.RepairedCount == 1, "引擎判定为需要修复的抖动双击",
                    "修复 " + engine.RepairedCount + " 次 / 拒绝 " + engine.RejectedCount + " 次");
                T.Check(report != null && report.HasPair, "产生配对报告");
                if (report != null)
                {
                    T.Eq(true, report.Repaired, "报告标记已修复");
                    T.Check(report.Distance >= 30 && report.Distance <= 45, "偏离距离符合注入值 34,16",
                        report.Distance + "px");
                }

                // 间隔超出时间窗的两次点击不应触发修复
                long before = engine.RepairedCount;
                Injector.ClickAt(baseX - 200, baseY, 20, false);
                Pump(40);
                Thread.Sleep(1000);
                Injector.ClickAt(baseX - 160, baseY, 20, false);
                Pump(120);
                T.Eq(before, engine.RepairedCount, "间隔超过时间窗时不修复");
            }
            finally
            {
                hook.Uninstall();
                Injector.MoveTo(savedX, savedY);
            }

            T.Check(!hook.IsInstalled, "钩子已卸载");
        }

        /* ------------------------------------------------------------------ */

        public static void RunRepositionExperiment(string title, bool rewriteMessagePoint)
        {
            T.Section("端到端实验：" + title);
            Console.WriteLine("  （会短暂全屏显示一个测试窗口，用于截获合成点击）");

            RepairEngine engine = NewEngine(RepairStrategy.Reposition, AnchorMode.First);
            MouseHook hook = new MouseHook(engine);
            hook.TreatInjectedAsPhysical = true;
            hook.DryRun = false;
            hook.SwallowAll = false;

            int savedX, savedY;
            Injector.GetCursor(out savedX, out savedY);

            ProbeForm form = new ProbeForm();
            form.Show();
            Pump(250);

            Point aScreen = form.A.PointToScreen(new Point(form.A.Width / 2, form.A.Height / 2));
            Point bScreen = form.B.PointToScreen(new Point(form.B.Width / 2, form.B.Height / 2));
            Console.WriteLine("  面板A中心 {0}，面板B中心 {1}", aScreen, bScreen);

            bool installed = hook.Install();
            try
            {
                Injector.ClickAt(aScreen.X, aScreen.Y, 25, false);
                Pump(80);
                Injector.ClickAt(bScreen.X, bScreen.Y, 25, false);
                Pump(400);
            }
            finally
            {
                hook.Uninstall();
                Injector.MoveTo(savedX, savedY);
                form.Close();
                Pump(80);
            }

            Console.WriteLine("  面板A：" + form.A.Summary());
            foreach (string line in form.A.Trace) Console.WriteLine("      " + line);
            Console.WriteLine("  面板B：" + form.B.Summary());
            foreach (string line in form.B.Trace) Console.WriteLine("      " + line);
            Console.WriteLine("  引擎：修复 {0} 次，" + "最后原因：{1}", engine.RepairedCount, engine.LastPair.Reason);

            T.Check(installed, "实验期间钩子已安装");
            T.Check(engine.RepairedCount >= 1, "引擎判定第二次点击需要对齐");
            T.Check(form.A.DoubleClicks >= 1, "对齐生效：面板A 收到双击", form.A.Summary());
            T.Check(form.B.Downs == 0, "对齐生效：面板B 没有收到点击", form.B.Summary());
        }

        public static void RunSynthesizeExperiment()
        {
            T.Section("端到端实验：合成双击策略");
            Console.WriteLine("  （会短暂全屏显示一个测试窗口，用于截获合成点击）");

            RepairEngine engine = NewEngine(RepairStrategy.Synthesize, AnchorMode.Second);
            MouseHook hook = new MouseHook(engine);
            hook.TreatInjectedAsPhysical = true;
            hook.DryRun = false;
            hook.SwallowAll = false;

            int savedX, savedY;
            Injector.GetCursor(out savedX, out savedY);

            ProbeForm form = new ProbeForm();
            form.Show();
            Pump(250);

            Point aScreen = form.A.PointToScreen(new Point(form.A.Width / 2, form.A.Height / 2));
            Point bScreen = form.B.PointToScreen(new Point(form.B.Width / 2, form.B.Height / 2));

            bool installed = hook.Install();
            try
            {
                Injector.ClickAt(aScreen.X, aScreen.Y, 25, false);
                Pump(80);
                Injector.ClickAt(bScreen.X, bScreen.Y, 25, false);
                Pump(600);
            }
            finally
            {
                hook.Uninstall();
                Injector.MoveTo(savedX, savedY);
                form.Close();
                Pump(80);
            }

            Console.WriteLine("  面板A：" + form.A.Summary());
            foreach (string line in form.A.Trace) Console.WriteLine("      " + line);
            Console.WriteLine("  面板B：" + form.B.Summary());
            foreach (string line in form.B.Trace) Console.WriteLine("      " + line);
            Console.WriteLine("  引擎：修复 {0} 次，最后原因：{1}", engine.RepairedCount, engine.LastPair.Reason);

            T.Check(installed, "实验期间钩子已安装");
            T.Check(engine.RepairedCount >= 1, "引擎判定第二次点击需要合成");
            T.Check(form.B.DoubleClicks >= 1, "合成生效：面板B 收到双击", form.B.Summary());
            T.Check(form.B.DoubleClicks <= 1, "没有重复触发双击（只开一次）", form.B.Summary());
        }

        /// <summary>
        /// 系统层验证：完全不安装任何钩子，只通过 SystemParametersInfo 放宽双击矩形，
        /// 看系统是否真的会因此把"抖动的两次点击"识别成双击。
        /// </summary>
        public static void RunRectExperiment()
        {
            T.Section("系统层验证：放宽双击矩形 → 系统原生识别抖动点击（不使用任何钩子）");

            int w0 = DoubleClickParams.GetRectWidth();
            int h0 = DoubleClickParams.GetRectHeight();
            int t0 = DoubleClickParams.GetTime();
            T.Info("原始：{0}×{1}px / {2}ms", w0, h0, t0);

            int savedX, savedY;
            Injector.GetCursor(out savedX, out savedY);

            RectProbeForm form = new RectProbeForm();
            form.Show();
            Pump(400);

            int jitterPx = 30;   // 模拟手指抖动：两次落点相差 30px
            Point target = form.Pad.PointToScreen(new Point(
                form.Pad.Width / 2 - jitterPx / 2, form.Pad.Height / 2));

            T.Check(OwnedByUs(target.X, target.Y) && OwnedByUs(target.X + jitterPx, target.Y),
                "测试面板位于本程序窗口内（注入不会影响到其它程序）");

            try
            {
                /* A 组：Windows 默认 4×4 矩形 —— 30px 抖动应该识别不出双击 */
                DoubleClickParams.SetRect(4, 4);
                Pump(150);
                form.Pad.Reset();
                Injector.ClickAt(target.X, target.Y, 25, false);
                Pump(150);
                Injector.ClickAt(target.X + jitterPx, target.Y, 25, false);
                Pump(450);
                int controlDoubleClicks = form.Pad.DoubleClicks;
                T.Info("A 组（矩形 4×4px）：{0}", form.Pad.Summary());

                /* B 组：放宽到 40×40 矩形 —— 同样的 30px 抖动应该被系统原生识别为双击 */
                DoubleClickParams.SetRect(40, 40);
                Pump(150);
                form.Pad.Reset();
                Injector.ClickAt(target.X, target.Y, 25, false);
                Pump(150);
                Injector.ClickAt(target.X + jitterPx, target.Y, 25, false);
                Pump(450);
                int widenedDoubleClicks = form.Pad.DoubleClicks;
                T.Info("B 组（矩形 40×40px）：{0}", form.Pad.Summary());

                T.Eq(0, controlDoubleClicks, "默认矩形下 30px 抖动识别不出双击（复现用户的问题）");
                T.Check(widenedDoubleClicks >= 1, "放宽矩形后系统原生识别为双击（方案一有效）",
                    "双击 " + widenedDoubleClicks + " 次");
            }
            finally
            {
                DoubleClickParams.SetRect(w0, h0);
                Injector.MoveTo(savedX, savedY);
                form.Close();
                Pump(120);
            }

            T.Eq(w0, DoubleClickParams.GetRectWidth(), "实验后已还原双击矩形");
        }

        private static bool OwnedByUs(int x, int y)
        {
            try
            {
                Win32.POINT p = new Win32.POINT(x, y);
                IntPtr hwnd = Win32.WindowFromPoint(p);
                if (hwnd == IntPtr.Zero) return false;
                uint pid;
                Win32.GetWindowThreadProcessId(hwnd, out pid);
                return pid == (uint)Process.GetCurrentProcess().Id;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
