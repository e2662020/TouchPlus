using System;
using System.Globalization;

namespace TouchPlus.Core
{
    public enum PatchState
    {
        Stopped = 0,
        Running = 1,
        Paused = 2,
        Error = 3
    }

    /// <summary>
    /// 补丁总控：把"设置"翻译成"系统层参数 + 输入层钩子"的实际动作。
    /// 界面与托盘都只跟它打交道。
    /// </summary>
    public sealed class PatchController : IDisposable
    {
        public readonly AppSettings Settings;
        public readonly RepairEngine Engine;
        public readonly MouseHook Hook;

        private PatchState _state = PatchState.Stopped;
        private string _lastError = string.Empty;
        private bool _disposed;

        public event EventHandler StateChanged;
        public event EventHandler<PairReport> PairReported;

        public PatchController(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            Settings = settings;
            Engine = new RepairEngine(Log.Instance);
            Hook = new MouseHook(Engine);
            Hook.ActionPerformed += HookActionPerformed;
            Engine.PairReported += EnginePairReported;
            Engine.Options.CopyFrom(Settings);
        }

        public PatchState State { get { return _state; } }
        public string LastError { get { return _lastError; } }

        public bool IsActive
        {
            get { return _state == PatchState.Running && Settings.PatchEnabled; }
        }

        public string StateText
        {
            get
            {
                switch (_state)
                {
                    case PatchState.Running: return Settings.PatchEnabled ? "补丁运行中" : "补丁已暂停";
                    case PatchState.Paused: return "补丁已暂停";
                    case PatchState.Error: return "补丁启动异常";
                    default: return "补丁已停止";
                }
            }
        }

        /* ------------------------------------------------------------------ */

        public bool Start()
        {
            Settings.Normalize();
            Engine.Options.CopyFrom(Settings);
            RefreshSystemValues(false);

            bool ok = true;
            _lastError = string.Empty;

            if (Settings.PatchEnabled && Settings.SystemLayerEnabled)
                ok &= ApplySystemLayer();

            if (Settings.PatchEnabled && Settings.HookLayerEnabled)
            {
                if (!Hook.Install())
                {
                    ok = false;
                    _lastError = "鼠标钩子安装失败（错误码 " + Hook.LastError + "）";
                    Log.Instance.Error(_lastError);
                }
                else
                {
                    Log.Instance.Info("鼠标钩子已安装");
                }
            }

            _state = ok ? PatchState.Running : PatchState.Error;
            RaiseStateChanged();
            return ok;
        }

        public void Stop()
        {
            Hook.Uninstall();
            _state = PatchState.Stopped;
            RaiseStateChanged();
        }

        public void SetPaused(bool paused)
        {
            Settings.PatchEnabled = !paused;
            Engine.Options.CopyFrom(Settings);

            if (paused)
            {
                // 暂停时把系统层的改动收回去，避免"暂停了却还在起作用"
                if (Settings.SystemLayerEnabled && Settings.OriginalsCaptured)
                    RestoreSystemLayer();
                _state = PatchState.Paused;
            }
            else
            {
                if (Settings.SystemLayerEnabled) ApplySystemLayer();
                if (Settings.HookLayerEnabled && !Hook.IsInstalled && !Hook.Install())
                {
                    _lastError = "鼠标钩子安装失败（错误码 " + Hook.LastError + "）";
                    Log.Instance.Error(_lastError);
                }
                _state = PatchState.Running;
            }
            RaiseStateChanged();
        }

        public bool Toggle()
        {
            SetPaused(Settings.PatchEnabled);
            return Settings.PatchEnabled;
        }

        /// <summary>把设置同步到引擎/钩子/系统层。</summary>
        public void ApplySettings(bool touchSystemLayer, bool touchHook)
        {
            Settings.Normalize();
            Engine.Options.CopyFrom(Settings);

            if (touchSystemLayer)
            {
                if (Settings.PatchEnabled && Settings.SystemLayerEnabled) ApplySystemLayer();
                else if (Settings.OriginalsCaptured) RestoreSystemLayer();
            }

            if (touchHook)
            {
                if (Settings.PatchEnabled && Settings.HookLayerEnabled)
                {
                    if (!Hook.IsInstalled && !Hook.Install())
                    {
                        _lastError = "鼠标钩子安装失败（错误码 " + Hook.LastError + "）";
                        Log.Instance.Error(_lastError);
                    }
                }
                else
                {
                    Hook.Uninstall();
                }
            }

            RefreshSystemValues(false);
            // 状态直接由配置决定，不要用 IsActive（它本身依赖 _state，会永远判成"暂停"）
            if (_state != PatchState.Error)
                _state = Settings.PatchEnabled ? PatchState.Running : PatchState.Paused;
            RaiseStateChanged();
        }

        /// <summary>读取系统当前的双击参数（外部改过也要能反映出来）。</summary>
        public void RefreshSystemValues(bool raiseEvent)
        {
            Engine.Options.SystemHasDigitizer = SystemInfo.HasTouchOrPen;
            Engine.Options.SystemRectWidth = DoubleClickParams.GetRectWidth();
            Engine.Options.SystemRectHeight = DoubleClickParams.GetRectHeight();
            Engine.Options.SystemTime = (uint)DoubleClickParams.GetTime();
            if (raiseEvent) RaiseStateChanged();
        }

        public bool ApplySystemLayer()
        {
            CaptureOriginals();
            bool ok = DoubleClickParams.SetRect(Settings.SystemDoubleClickWidth, Settings.SystemDoubleClickHeight);
            ok &= DoubleClickParams.SetTime(Settings.SystemDoubleClickTime);
            RefreshSystemValues(false);
            Log.Instance.Info("系统双击参数已应用：{0}", DoubleClickParams.DescribeCurrent());
            if (!ok) Log.Instance.Warn("部分系统双击参数写入失败（可能被组策略或安全软件限制）");
            return ok;
        }

        public void RestoreSystemLayer()
        {
            if (!Settings.OriginalsCaptured)
            {
                Log.Instance.Warn("没有原始参数备份，无法还原系统双击参数");
                return;
            }
            DoubleClickParams.Apply(Settings.OriginalDoubleClickWidth, Settings.OriginalDoubleClickHeight,
                Settings.OriginalDoubleClickTime);
            RefreshSystemValues(false);
            Log.Instance.Info("系统双击参数已还原：{0}", DoubleClickParams.DescribeCurrent());
        }

        /// <summary>首次改动系统参数前，把原值记下来（只记一次）。</summary>
        public void CaptureOriginals()
        {
            if (Settings.OriginalsCaptured) return;

            int w = DoubleClickParams.GetRectWidth();
            int h = DoubleClickParams.GetRectHeight();
            int t = DoubleClickParams.GetPersistedTime();

            // 如果当前值已经是我们自己的默认值（上次运行留下的），就按系统默认值记录
            if (w == Settings.SystemDoubleClickWidth && h == Settings.SystemDoubleClickHeight &&
                t == Settings.SystemDoubleClickTime)
            {
                w = 4; h = 4; t = 500;
                Log.Instance.Info("检测到系统参数等于本程序默认值，按 Windows 默认值 4×4 / 500ms 记录原值");
            }

            Settings.OriginalDoubleClickWidth = w;
            Settings.OriginalDoubleClickHeight = h;
            Settings.OriginalDoubleClickTime = t;
            Settings.OriginalsCaptured = true;
            Log.Instance.Info("已备份系统双击参数原值：{0}×{1}px / {2}ms", w, h, t);
        }

        public string SystemParamsText
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, "双击矩形 {0}×{1}px · 时间窗 {2}ms",
                    DoubleClickParams.GetRectWidth(), DoubleClickParams.GetRectHeight(), DoubleClickParams.GetTime());
            }
        }

        private void HookActionPerformed(object sender, EngineAction action)
        {
            if (action == null || !action.Repaired) return;
            if (!string.IsNullOrEmpty(action.Reason)) Log.Instance.Repair(action.Reason);
        }

        private void EnginePairReported(object sender, PairReport report)
        {
            if (report == null) return;
            if (report.Repaired)
            {
                Settings.TotalRepairs++;
            }
            Settings.TotalPairs++;

            EventHandler<PairReport> handler = PairReported;
            if (handler != null)
            {
                try { handler(this, report); }
                catch (Exception) { }
            }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Hook.Dispose(); }
            catch (Exception) { }
        }
    }
}
