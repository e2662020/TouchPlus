using System;
using System.Collections.Generic;
using System.Globalization;

namespace TouchPlus.Core
{
    public enum InputKind
    {
        Unknown = 0,
        Mouse = 1,
        Touch = 2,
        Pen = 3
    }

    public enum EngineActionKind
    {
        None = 0,
        /// <summary>把光标挪到锚点，让紧随其后的这次点击落在锚点上（不注入输入）。</summary>
        AlignCursor = 1,
        /// <summary>吞掉这次按下（以及配对的抬起），稍后在锚点合成双击。</summary>
        SwallowDown = 2,
        /// <summary>吞掉抬起，并按参数在锚点注入合成事件。</summary>
        SwallowUpAndInject = 3
    }

    public struct ClickSample
    {
        public int X;
        public int Y;
        public uint Time;
        public InputKind Kind;
        public IntPtr HitWindow;
        public IntPtr RootWindow;
        public uint ProcessId;
        public string ProcessName;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0},{1}) t={2} {3} pid={4} {5}",
                X, Y, Time, Kind, ProcessId, ProcessName ?? "-");
        }
    }

    public sealed class EngineAction
    {
        public EngineActionKind Kind = EngineActionKind.None;
        public bool Swallow;
        public int X;
        public int Y;
        public int InjectPairs = 1;
        public bool EmulateLongPress;
        public int LongPressMs;
        public bool Repaired;
        public string Reason = string.Empty;
        public int Distance;
        public uint DeltaMs;

        public bool IsNoOp { get { return Kind == EngineActionKind.None && !Swallow; } }
    }

    /// <summary>供界面"自检区"与日志使用的一次点击对报告。</summary>
    public sealed class PairReport
    {
        public bool HasPair;
        public int X1, Y1, X2, Y2;
        public int Distance;
        public uint DeltaMs;
        public bool Repaired;
        public bool NativeOk;
        public bool SameWindow;
        public string Reason = string.Empty;
        public InputKind Kind1;
        public InputKind Kind2;
        public DateTime At = DateTime.Now;

        public string Verdict
        {
            get
            {
                if (!HasPair) return "等待第二次点击";
                if (Repaired) return "Touch+ 已修复并识别为双击";
                if (NativeOk) return "系统原生识别为双击";
                return "未识别为双击";
            }
        }
    }

    /// <summary>引擎运行时读取的选项（由 App 层从 AppSettings + 实时系统值同步过来）。</summary>
    public sealed class EngineOptions
    {
        public bool Enabled = true;
        public int TolerancePx = 60;
        public int TimeWindowMs = 900;
        public RepairStrategy Strategy = RepairStrategy.Reposition;
        public AnchorMode Anchor = AnchorMode.Second;
        public SourceMode Source = SourceMode.Auto;
        public bool RequireSameWindow = true;
        public int DragResetPx = 16;
        public bool EmulateLongPress = true;
        public int LongPressGuardMs = 450;
        public List<string> Excluded = new List<string>();

        /* 实时系统值：用于判断"系统本来就能识别"，避免画蛇添足 */
        public bool SystemHasDigitizer;
        public int SystemRectWidth = 4;
        public int SystemRectHeight = 4;
        public uint SystemTime = 500;

        public void CopyFrom(AppSettings s)
        {
            if (s == null) return;
            Enabled = s.PatchEnabled && s.HookLayerEnabled;
            TolerancePx = s.Tolerance;
            TimeWindowMs = s.TimeWindow;
            Strategy = s.Strategy;
            Anchor = s.Anchor;
            Source = s.Source;
            RequireSameWindow = s.RequireSameWindow;
            DragResetPx = s.DragResetPx;
            EmulateLongPress = s.EmulateLongPress;
            LongPressGuardMs = s.LongPressGuardMs;
            Excluded = s.ExcludedProcesses;
        }
    }

    /// <summary>
    /// 双击识别修复引擎：纯逻辑，不做任何 Win32 调用，因此可以被完整单元测试。
    ///
    /// 设计要点：
    ///  1. 系统本来就能识别的点击对（在系统双击矩形与时间窗内）一律不插手，交由系统原生处理；
    ///  2. 只有"位置偏离"或"间隔超时"导致系统识别失败时，才由本引擎补救；
    ///  3. 默认策略是"坐标对齐"——不注入任何输入，只把第二次点击的位置对齐到锚点；
    ///  4. "合成双击"策略才会拦截并注入，同时用长按保护避免破坏触控长按（右键）。
    /// </summary>
    public sealed class RepairEngine
    {
        public readonly EngineOptions Options = new EngineOptions();
        public readonly Log Logger;

        private bool _hasFirst;
        private ClickSample _first;
        private bool _hasCurrentDown;
        private ClickSample _currentDown;

        private bool _swallowPending;
        private ClickSample _swallowFirst;
        private int _swallowAnchorX;
        private int _swallowAnchorY;
        private uint _swallowDownTime;

        private bool _sawTouchSignature;

        /* ---------------- 统计 ---------------- */
        public long RepairedCount;
        public long PairCount;
        public long RejectedCount;
        public long NativePairCount;
        public long LongPressCount;
        public long DistanceSum;
        public int MaxDistance;
        public long TouchClickCount;
        public long MouseClickCount;
        public DateTime LastRepairAt = DateTime.MinValue;
        public PairReport LastPair = new PairReport();

        public event EventHandler<PairReport> PairReported;

        public RepairEngine(Log logger)
        {
            Logger = logger;
        }

        public bool SawTouchSignature { get { return _sawTouchSignature; } }
        public bool SwallowPending { get { return _swallowPending; } }

        public int AverageDistance
        {
            get { return RepairedCount > 0 ? (int)(DistanceSum / RepairedCount) : 0; }
        }

        public void Reset()
        {
            _hasFirst = false;
            _hasCurrentDown = false;
            _swallowPending = false;
        }

        public void ResetSessionStats()
        {
            RepairedCount = 0;
            PairCount = 0;
            RejectedCount = 0;
            NativePairCount = 0;
            LongPressCount = 0;
            DistanceSum = 0;
            MaxDistance = 0;
            TouchClickCount = 0;
            MouseClickCount = 0;
            LastPair = new PairReport();
        }

        public EngineAction OnButtonDown(ClickSample sample)
        {
            EngineAction action = new EngineAction();

            if (sample.Kind == InputKind.Touch || sample.Kind == InputKind.Pen) TouchClickCount++;
            else MouseClickCount++;

            if (!Options.Enabled)
            {
                ClearPair();
                action.Reason = "补丁未启用";
                return action;
            }

            if (!SourceAllowed(sample))
            {
                ClearPair();
                action.Reason = "输入来源被过滤（" + Options.Source + "）";
                return action;
            }

            if (ProcessRules.IsExcluded(Options.Excluded, sample.ProcessName))
            {
                ClearPair();
                action.Reason = "程序在排除列表：" + sample.ProcessName;
                return action;
            }

            _hasCurrentDown = true;
            _currentDown = sample;

            if (_hasFirst)
            {
                ClickSample first = _first;
                uint dt = sample.Time - first.Time;
                int dx = Math.Abs(sample.X - first.X);
                int dy = Math.Abs(sample.Y - first.Y);
                int distance = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));
                bool sameWindow = !Options.RequireSameWindow || first.RootWindow == sample.RootWindow;
                bool nativeOk = dt <= Options.SystemTime
                                && dx <= Options.SystemRectWidth
                                && dy <= Options.SystemRectHeight;

                bool withinTolerance = dt <= (uint)Options.TimeWindowMs && distance <= Options.TolerancePx;
                bool firstExcluded = ProcessRules.IsExcluded(Options.Excluded, first.ProcessName);

                if (nativeOk)
                {
                    NativePairCount++;
                    action.Reason = "系统原生可识别（无需干预）";
                    action.Distance = distance;
                    action.DeltaMs = dt;
                    ReportPair(first, sample, distance, dt, true, false, action.Reason, sameWindow);
                    _first = sample;
                    return action;
                }

                if (withinTolerance && sameWindow && !firstExcluded)
                {
                    int ax, ay;
                    ComputeAnchor(first, sample, out ax, out ay);

                    RepairedCount++;
                    PairCount++;
                    DistanceSum += distance;
                    if (distance > MaxDistance) MaxDistance = distance;
                    LastRepairAt = DateTime.Now;

                    action.Repaired = true;
                    action.Distance = distance;
                    action.DeltaMs = dt;
                    action.X = ax;
                    action.Y = ay;

                    if (Options.Strategy == RepairStrategy.Reposition)
                    {
                        action.Kind = EngineActionKind.AlignCursor;
                        action.Swallow = false;
                        action.Reason = string.Format(CultureInfo.InvariantCulture,
                            "坐标对齐 → ({0},{1})，偏离 {2}px / {3}ms", ax, ay, distance, dt);
                    }
                    else
                    {
                        action.Kind = EngineActionKind.SwallowDown;
                        action.Swallow = true;
                        action.Reason = string.Format(CultureInfo.InvariantCulture,
                            "合成双击 → ({0},{1})，偏离 {2}px / {3}ms", ax, ay, distance, dt);

                        _swallowPending = true;
                        _swallowFirst = first;
                        _swallowAnchorX = ax;
                        _swallowAnchorY = ay;
                        _swallowDownTime = sample.Time;
                    }

                    ReportPair(first, sample, distance, dt, false, true, action.Reason, sameWindow);
                    _hasFirst = false;
                    return action;
                }

                string reason;
                if (!withinTolerance)
                {
                    if (dt > (uint)Options.TimeWindowMs) reason = string.Format(CultureInfo.InvariantCulture, "间隔 {0}ms 超过时间窗 {1}ms", dt, Options.TimeWindowMs);
                    else reason = string.Format(CultureInfo.InvariantCulture, "偏离 {0}px 超过容差 {1}px", distance, Options.TolerancePx);
                }
                else if (!sameWindow) reason = "两次点击不在同一窗口";
                else reason = "首次点击所在程序被排除";

                RejectedCount++;
                action.Reason = "不修复：" + reason;
                ReportPair(first, sample, distance, dt, false, false, reason, sameWindow);
                _first = sample;
                return action;
            }

            _first = sample;
            _hasFirst = true;
            action.Reason = "记录首次点击";
            return action;
        }

        public EngineAction OnButtonUp(ClickSample sample)
        {
            EngineAction action = new EngineAction();

            if (_swallowPending)
            {
                _swallowPending = false;
                uint pressMs = sample.Time - _swallowDownTime;

                action.Kind = EngineActionKind.SwallowUpAndInject;
                action.Swallow = true;
                action.Repaired = true;
                action.X = _swallowAnchorX;
                action.Y = _swallowAnchorY;

                if (Options.EmulateLongPress && pressMs >= (uint)Options.LongPressGuardMs)
                {
                    action.EmulateLongPress = true;
                    action.LongPressMs = (int)Math.Min(pressMs, 1500u);
                    action.Reason = string.Format(CultureInfo.InvariantCulture, "长按保护：按 {0}ms 重放长按", action.LongPressMs);
                }
                else
                {
                    uint sinceFirst = sample.Time - _swallowFirst.Time;
                    int dx = Math.Abs(_swallowAnchorX - _swallowFirst.X);
                    int dy = Math.Abs(_swallowAnchorY - _swallowFirst.Y);
                    uint budget = (uint)(Options.SystemTime * 0.85);
                    bool canPairWithFirst = sinceFirst <= budget
                                            && dx <= Options.SystemRectWidth
                                            && dy <= Options.SystemRectHeight;
                    action.InjectPairs = canPairWithFirst ? 1 : 2;
                    action.Reason += canPairWithFirst
                        ? "；注入 1 对（与首次点击配对）"
                        : "；注入 2 对（自洽双击）";
                }

                _hasCurrentDown = false;
                return action;
            }

            if (_hasCurrentDown)
            {
                int dx = Math.Abs(sample.X - _currentDown.X);
                int dy = Math.Abs(sample.Y - _currentDown.Y);
                int moved = (int)Math.Round(Math.Sqrt((double)dx * dx + (double)dy * dy));
                if (moved > Options.DragResetPx)
                {
                    // 按下与抬起位置差得远 → 这是一次拖动，不能当作双击的第一击
                    _hasFirst = false;
                }
                _hasCurrentDown = false;
            }

            return action;
        }

        /// <summary>其它按键（右键/中键/滚轮）会打断双击配对。</summary>
        public void OnOtherInput(string reason)
        {
            ClearPair();
            if (Logger != null && Logger.Verbose)
                Logger.Debug("配对状态被重置：{0}", reason);
        }

        private void ClearPair()
        {
            _hasFirst = false;
            _hasCurrentDown = false;
        }

        private void ComputeAnchor(ClickSample first, ClickSample second, out int x, out int y)
        {
            // 坐标对齐策略只能锚定"首次点击"：第一次点击的落点已经进入系统队列，无法追溯修改。
            if (Options.Strategy == RepairStrategy.Reposition)
            {
                x = first.X;
                y = first.Y;
                return;
            }

            switch (Options.Anchor)
            {
                case AnchorMode.Second:
                    x = second.X;
                    y = second.Y;
                    break;
                case AnchorMode.Midpoint:
                    x = (first.X + second.X) / 2;
                    y = (first.Y + second.Y) / 2;
                    break;
                default:
                    x = first.X;
                    y = first.Y;
                    break;
            }
        }

        private bool SourceAllowed(ClickSample sample)
        {
            bool isTouchLike = sample.Kind == InputKind.Touch || sample.Kind == InputKind.Pen;

            switch (Options.Source)
            {
                case SourceMode.All:
                    return true;

                case SourceMode.TouchPenOnly:
                    return isTouchLike;

                default: /* Auto */
                    if (isTouchLike)
                    {
                        _sawTouchSignature = true;
                        return true;
                    }
                    // 没有数字化仪（纯鼠标环境）→ 允许处理，否则补丁形同虚设
                    if (!Options.SystemHasDigitizer) return true;
                    // 有数字化仪但从未见过触摸签名 → 触摸屏驱动以普通鼠标报文上报，允许处理
                    return !_sawTouchSignature;
            }
        }

        private void ReportPair(ClickSample first, ClickSample second, int distance, uint dt,
                                bool nativeOk, bool repaired, string reason, bool sameWindow)
        {
            PairReport report = new PairReport();
            report.HasPair = true;
            report.X1 = first.X;
            report.Y1 = first.Y;
            report.X2 = second.X;
            report.Y2 = second.Y;
            report.Distance = distance;
            report.DeltaMs = dt;
            report.NativeOk = nativeOk;
            report.Repaired = repaired;
            report.SameWindow = sameWindow;
            report.Reason = reason;
            report.Kind1 = first.Kind;
            report.Kind2 = second.Kind;
            report.At = DateTime.Now;
            LastPair = report;

            EventHandler<PairReport> handler = PairReported;
            if (handler != null)
            {
                try { handler(this, report); }
                catch (Exception) { }
            }
        }

        /// <summary>把状态机内部状态导出成一行文字，便于日志与自检。</summary>
        public string DescribeState()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "first={0} currentDown={1} swallowPending={2} sawTouch={3}",
                _hasFirst ? _first.ToString() : "-",
                _hasCurrentDown ? _currentDown.ToString() : "-",
                _swallowPending, _sawTouchSignature);
        }
    }
}
