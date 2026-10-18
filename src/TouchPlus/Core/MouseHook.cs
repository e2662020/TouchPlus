using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace TouchPlus.Core
{
    internal static class ProcessNameCache
    {
        private static readonly Dictionary<uint, string> Map = new Dictionary<uint, string>();
        private static readonly object Sync = new object();

        public static string Get(uint pid)
        {
            if (pid == 0) return string.Empty;
            lock (Sync)
            {
                string cached;
                if (Map.TryGetValue(pid, out cached)) return cached;
            }

            string name = string.Empty;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    name = p.ProcessName;
                }
            }
            catch (Exception)
            {
                name = string.Empty;
            }

            lock (Sync)
            {
                if (Map.Count > 512) Map.Clear();
                Map[pid] = name;
            }
            return name;
        }

        public static void Invalidate(uint pid)
        {
            lock (Sync) { Map.Remove(pid); }
        }

        public static void Clear()
        {
            lock (Sync) { Map.Clear(); }
        }
    }

    /// <summary>
    /// 低级鼠标钩子（WH_MOUSE_LL）。所有判定交给 RepairEngine，本类只负责"取事件 → 执行动作"。
    /// 钩子回调里绝不抛异常（异常逃逸会影响整个桌面输入）。
    /// </summary>
    public sealed class MouseHook : IDisposable
    {
        private readonly RepairEngine _engine;
        private readonly Win32.LowLevelMouseProc _proc;
        private IntPtr _hook = IntPtr.Zero;
        private bool _swallowUpPending;
        private int _lastError;
        private bool _disposed;

        /// <summary>把注入事件当成真实事件处理（仅自动化测试使用）。</summary>
        public bool TreatInjectedAsPhysical;
        /// <summary>只做判定与记录，不移动光标、不注入（干跑）。</summary>
        public bool DryRun;
        /// <summary>
        /// 纯观察模式（最安全）：只读取事件，永不吞事件、永不移动光标、永不注入，
        /// 每个事件都原样放行 —— 效果等同于"没有安装钩子"。
        /// </summary>
        public bool ObserveOnly;
        /// <summary>吞掉所有事件，绝不泄漏到桌面（自动化测试隔离用）。</summary>
        public bool SwallowAll;

        public long EventsObserved;
        public long DownsObserved;
        public long UpsObserved;

        public event EventHandler<EngineAction> ActionPerformed;
        public event EventHandler<ClickSample> ButtonDownObserved;

        public MouseHook(RepairEngine engine)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            _engine = engine;
            _proc = HookCallback;
        }

        public RepairEngine Engine { get { return _engine; } }
        public bool IsInstalled { get { return _hook != IntPtr.Zero; } }
        public int LastError { get { return _lastError; } }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            try
            {
                IntPtr module = Win32.GetModuleHandle(null);
                _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, module, 0);
                _lastError = Marshal.GetLastWin32Error();
            }
            catch (Exception ex)
            {
                _lastError = Marshal.GetLastWin32Error();
                Log.Instance.Error(ex, "安装鼠标钩子时发生异常");
            }

            if (_hook == IntPtr.Zero)
            {
                Log.Instance.Error("安装鼠标钩子失败（Win32 错误码 {0}）", _lastError);
                return false;
            }
            return true;
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            try
            {
                Win32.UnhookWindowsHookEx(_hook);
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex, "卸载鼠标钩子时发生异常");
            }
            _hook = IntPtr.Zero;

            if (_swallowUpPending || _engine.SwallowPending)
            {
                Log.Instance.Warn("钩子卸载时仍有一次被吞掉的点击，已重置状态");
                _swallowUpPending = false;
                _engine.Reset();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Uninstall();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);

            long started = Stopwatch.GetTimestamp();
            try
            {
                return Handle(nCode, wParam, lParam);
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex, "鼠标钩子回调异常（已忽略该事件）");
                try { return Win32.CallNextHookEx(_hook, nCode, wParam, lParam); }
                catch (Exception) { return IntPtr.Zero; }
            }
            finally
            {
                // 看门狗：低级钩子回调一旦超时，整个系统的鼠标输入都会卡住。
                // 超过 1 秒说明出了严重问题，直接在后台卸载钩子，宁可补丁失效也不能卡死系统。
                try
                {
                    double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs > 250.0)
                    {
                        Log.Instance.Warn("鼠标钩子回调耗时 {0:F0}ms（偏高）", elapsedMs);
                        if (elapsedMs > 1000.0)
                        {
                            Log.Instance.Error("鼠标钩子回调超过 1 秒，已自动卸载钩子以保护系统输入");
                            ThreadPool.QueueUserWorkItem(delegate(object s) { try { Uninstall(); } catch (Exception) { } });
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private IntPtr Handle(int nCode, IntPtr wParam, IntPtr lParam)
        {
            Win32.MSLLHOOKSTRUCT data =
                (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));
            int message = wParam.ToInt32();
            EventsObserved++;

            // 本程序自己注入的合成事件（带标记）永不再次进入修复逻辑，避免递归
            if (data.dwExtraInfo.ToInt64() == Injector.InjectionTag)
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);

            bool injected = (data.flags & (Win32.LLMHF_INJECTED | Win32.LLMHF_LOWER_IL_INJECTED)) != 0;
            if (injected && !TreatInjectedAsPhysical)
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);

            bool isDown = message == Win32.WM_LBUTTONDOWN;
            bool isUp = message == Win32.WM_LBUTTONUP;

            if (isDown) DownsObserved++;
            else if (isUp) UpsObserved++;

            if (isDown || isUp)
            {
                ClickSample sample = BuildSample(data);
                if (isDown)
                {
                    EventHandler<ClickSample> downHandler = ButtonDownObserved;
                    if (downHandler != null)
                    {
                        try { downHandler(this, sample); }
                        catch (Exception) { }
                    }

                    EngineAction action = _engine.OnButtonDown(sample);
                    RaiseAction(action);

                    if (ObserveOnly) return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
                    if (SwallowAll) return (IntPtr)1;

                    if (action.Kind == EngineActionKind.AlignCursor)
                    {
                        if (!DryRun) AlignCursor(data, lParam, action.X, action.Y);
                        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
                    }
                    if (action.Kind == EngineActionKind.SwallowDown)
                    {
                        _swallowUpPending = true;
                        return (IntPtr)1;
                    }
                    return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                /* ---- 左键抬起 ---- */
                EngineAction upAction = _engine.OnButtonUp(sample);
                bool swallow = _swallowUpPending || upAction.Swallow;
                _swallowUpPending = false;
                RaiseAction(upAction);

                if (ObserveOnly) return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
                if (SwallowAll) return (IntPtr)1;

                if (swallow)
                {
                    if (!DryRun && upAction.Kind == EngineActionKind.SwallowUpAndInject)
                        QueueInjection(upAction);
                    return (IntPtr)1;
                }
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            if (message == Win32.WM_MOUSEMOVE)
            {
                if (SwallowAll && !ObserveOnly) return (IntPtr)1;
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            // 其余按键 / 滚轮：打断双击配对
            if (message == Win32.WM_RBUTTONDOWN || message == Win32.WM_MBUTTONDOWN ||
                message == Win32.WM_XBUTTONDOWN || message == Win32.WM_MOUSEWHEEL ||
                message == Win32.WM_MOUSEHWHEEL)
            {
                _engine.OnOtherInput("其它输入：0x" + message.ToString("X4", CultureInfo.InvariantCulture));
            }

            if (SwallowAll && !ObserveOnly) return (IntPtr)1;
            return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private ClickSample BuildSample(Win32.MSLLHOOKSTRUCT data)
        {
            ClickSample sample = new ClickSample();
            sample.X = data.pt.X;
            sample.Y = data.pt.Y;
            sample.Time = data.time;
            sample.Kind = KindFromExtraInfo(data.dwExtraInfo);

            IntPtr hit = Win32.WindowFromPoint(data.pt);
            sample.HitWindow = hit;
            sample.RootWindow = hit != IntPtr.Zero ? Win32.GetAncestor(hit, Win32.GA_ROOT) : IntPtr.Zero;

            uint pid;
            Win32.GetWindowThreadProcessId(sample.RootWindow, out pid);
            sample.ProcessId = pid;
            sample.ProcessName = ProcessNameCache.Get(pid);
            return sample;
        }

        public static InputKind KindFromExtraInfo(IntPtr extraInfo)
        {
            long value = extraInfo.ToInt64();
            if ((value & Win32.MI_WP_SIGNATURE_MASK) == Win32.MI_WP_SIGNATURE)
                return (value & Win32.MI_WP_PEN_FLAG) != 0 ? InputKind.Pen : InputKind.Touch;
            return InputKind.Mouse;
        }

        private void AlignCursor(Win32.MSLLHOOKSTRUCT data, IntPtr lParam, int x, int y)
        {
            if (!SystemInfo.IsPointOnVirtualScreen(x, y)) return;
            Win32.SetCursorPos(x, y);
        }

        /// <summary>
        /// 注入必须异步：低级钩子回调是在 win32k 的输入处理/或一次 SendInput 内部被调用的，
        /// 此时再同步调用 SendInput 会造成重入死锁，把整个系统的鼠标输入卡死。
        /// 因此这里丢到线程池，等钩子回调返回之后再注入。
        /// </summary>
        private void QueueInjection(EngineAction action)
        {
            int x = action.X;
            int y = action.Y;
            int pairs = action.InjectPairs;
            bool longPress = action.EmulateLongPress;
            int longPressMs = action.LongPressMs;

            try
            {
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    try
                    {
                        Thread.Sleep(15);   // 确保当前钩子回调已经返回
                        if (longPress)
                        {
                            Log.Instance.Info("长按保护：在 ({0},{1}) 重放 {2}ms 长按", x, y, longPressMs);
                            Injector.LongPressAt(x, y, longPressMs);
                        }
                        else
                        {
                            Injector.DoubleClickAt(x, y, pairs);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Error(ex, "异步注入合成点击失败");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Instance.Error(ex, "排队注入任务失败");
            }
        }

        private void RaiseAction(EngineAction action)
        {
            if (action == null) return;
            if (action.Kind == EngineActionKind.None && !action.Repaired && action.Reason.Length == 0) return;

            EventHandler<EngineAction> handler = ActionPerformed;
            if (handler != null)
            {
                try { handler(this, action); }
                catch (Exception) { }
            }
        }

        /// <summary>给界面用：报告当前光标下的进程名。</summary>
        public static string ProcessNameAt(int x, int y)
        {
            Win32.POINT p = new Win32.POINT(x, y);
            IntPtr hwnd = Win32.WindowFromPoint(p);
            if (hwnd == IntPtr.Zero) return string.Empty;
            uint pid;
            Win32.GetWindowThreadProcessId(hwnd, out pid);
            return ProcessNameCache.Get(pid);
        }
    }
}
