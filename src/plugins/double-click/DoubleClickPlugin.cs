using System;
using System.Globalization;
using TouchPlus.Core;
using TouchPlus.Core.Plugins;

namespace TouchPlus.Plugins
{
    /// <summary>
    /// 插件 01：触控双击容差修复。
    ///
    /// 这是 Touch+ 的第一个补丁插件，不是 Touch+ 本身 —— 宿主只通过 IPatchPlugin 认识它。
    /// 它内部有两种机制：
    ///   A. 系统层：放宽 Windows 自己的双击矩形与时间窗（安全，默认开）；
    ///   B. 输入层：钩子拦截偏离过大的第二击并在锚点合成双击（高级，默认关）。
    /// </summary>
    public sealed class DoubleClickPlugin : PatchPluginBase
    {
        public const string PluginId = "touchplus.tolerance.doubleclick";

        private readonly PatchController _controller;

        public DoubleClickPlugin(PatchController controller)
        {
            if (controller == null) throw new ArgumentNullException("controller");
            _controller = controller;
        }

        /// <summary>宿主界面需要绑定的配置/引擎入口（插件自己的实现细节）。</summary>
        public PatchController Controller { get { return _controller; } }

        public override string Id { get { return PluginId; } }

        public override string Name { get { return "触控双击容差"; } }

        public override string Summary
        {
            get { return "手指双击时两次落点总会偏一点，Windows 只认 4×4 像素内的双击。本插件放宽这个判定阈值。"; }
        }

        public override PluginSafety Safety { get { return PluginSafety.Safe; } }

        public override string IconKey { get { return "tolerance"; } }

        public override PluginCapabilities Capabilities
        {
            get
            {
                PluginCapabilities c = new PluginCapabilities();
                c.AllowSystemParams = true;
                c.AllowInputHook = true;
                c.AllowInputInjection = true;
                return c;
            }
        }

        public override bool WasEnabledOnLastRun
        {
            get { return _controller.Settings.PatchEnabled; }
        }

        protected override bool Apply(out string error)
        {
            error = null;
            AppSettings s = _controller.Settings;
            s.PatchEnabled = true;
            _controller.ApplySettings(true, true);
            if (_controller.State == PatchState.Error)
            {
                error = _controller.LastError;
                return false;
            }
            return true;
        }

        protected override void Revert()
        {
            // 停用必须收回系统改动：系统层还原、钩子卸载
            _controller.Settings.PatchEnabled = false;
            _controller.ApplySettings(true, true);
        }

        public override PluginStats GetStats()
        {
            RepairEngine engine = _controller.Engine;
            AppSettings s = _controller.Settings;

            PluginStats stats = new PluginStats();
            stats.CountLabel = "输入层修复次数";
            stats.Count = engine.RepairedCount.ToString(CultureInfo.InvariantCulture);
            stats.AverageLabel = "平均偏离";
            stats.Average = engine.AverageDistance.ToString(CultureInfo.InvariantCulture);
            stats.AverageUnit = "px";
            stats.Detail = string.Format(CultureInfo.InvariantCulture,
                "判定范围 {0}×{0}px · 时间窗 {1}ms · 输入层 {2}",
                s.SystemDoubleClickWidth,
                s.SystemDoubleClickTime,
                s.HookLayerEnabled ? "合成双击" : "关闭");
            return stats;
        }

        public override void SaveConfig()
        {
            if (_controller.Settings.ReadOnlySession) return;
            SettingsStore.Save(_controller.Settings);
        }

        /// <summary>运行时改配置（界面用）：改完立即生效并保存。</summary>
        public void ApplyConfigChange(bool touchSystemLayer, bool touchHook)
        {
            _controller.ApplySettings(touchSystemLayer, touchHook);
            SaveConfig();
            RaiseChanged();
        }
    }
}
