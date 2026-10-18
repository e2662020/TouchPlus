using System;
using System.Collections.Generic;

namespace TouchPlus.Core.Plugins
{
    /// <summary>插件对系统的侵入程度。宿主据此决定要不要额外确认、能不能默认开启。</summary>
    public enum PluginSafety
    {
        /// <summary>只改 Windows 自己的参数（可一键还原），不注入、不装钩子。</summary>
        Safe = 0,
        /// <summary>需要全局钩子 / 合成输入，可能被安全软件关注。</summary>
        Advanced = 1
    }

    public enum PluginState
    {
        Disabled = 0,
        Running = 1,
        Error = 2
    }

    /// <summary>
    /// 插件向界面汇报的读数。宿主不认识具体插件，只按这些字段渲染，
    /// 所以任何插件都能自动出现在概览页的读数行里。
    /// </summary>
    public sealed class PluginStats
    {
        public string CountLabel = "触发次数";
        public string Count = "0";
        public string AverageLabel = "平均偏离";
        public string Average = "0";
        public string AverageUnit = "";
        public string Detail = "";
    }

    /// <summary>
    /// 补丁插件契约。宿主只依赖这个接口 —— 新增一个补丁必须实现它，
    /// 并且只通过 PluginHost 暴露给界面，界面里不出现任何"双击"专用的硬编码。
    /// </summary>
    public interface IPatchPlugin
    {
        /// <summary>稳定标识，用于配置持久化与去重（发布后不要改）。</summary>
        string Id { get; }

        string Name { get; }

        /// <summary>一句话说明这个补丁解决什么问题（面向使用者，不是面向代码）。</summary>
        string Summary { get; }

        PluginSafety Safety { get; }

        /// <summary>界面用的图标标识：tolerance / patch…（宿主按 key 画图标，插件不依赖绘图代码）。</summary>
        string IconKey { get; }

        /// <summary>宿主给这个插件的安全边界。</summary>
        PluginCapabilities Capabilities { get; }

        PluginState State { get; }

        bool Enabled { get; }

        /// <summary>启用；失败时通过 error 返回可直接展示给用户的原因。</summary>
        bool Enable(out string error);

        /// <summary>停用，并且必须把自己对系统的改动收回去。</summary>
        void Disable();

        PluginStats GetStats();

        /// <summary>把自身配置写回持久化存储（宿主在退出/改动后调用）。</summary>
        void SaveConfig();

        event EventHandler Changed;
    }

    /// <summary>插件能拿到什么能力，由宿主决定，不由插件自己声明。</summary>
    public sealed class PluginCapabilities
    {
        /// <summary>是否允许安装全局钩子。</summary>
        public bool AllowInputHook;
        /// <summary>是否允许在钩子里合成鼠标事件（高风险，宿主可整体关闭）。</summary>
        public bool AllowInputInjection;
        /// <summary>是否允许修改系统参数（双击矩形等）。</summary>
        public bool AllowSystemParams = true;
    }

    /// <summary>插件基类：统一处理状态变更与事件通知，插件只实现 Apply/Revert。</summary>
    public abstract class PatchPluginBase : IPatchPlugin
    {
        private PluginState _state = PluginState.Disabled;
        private bool _enabled;
        private string _lastError = "";

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Summary { get; }
        public virtual PluginSafety Safety { get { return PluginSafety.Safe; } }
        public virtual PluginCapabilities Capabilities { get { return new PluginCapabilities(); } }

        public virtual string IconKey { get { return "patch"; } }

        public PluginState State { get { return _state; } }
        public bool Enabled { get { return _enabled; } }
        public string LastError { get { return _lastError; } }

        /// <summary>上次退出时是否处于启用状态（由插件自己从配置里读）。</summary>
        public virtual bool WasEnabledOnLastRun { get { return false; } }

        public event EventHandler Changed;

        public bool Enable(out string error)
        {
            error = null;
            try
            {
                if (!Apply(out error))
                {
                    _state = PluginState.Error;
                    _lastError = error ?? "插件启用失败";
                    _enabled = false;
                    RaiseChanged();
                    return false;
                }
                _state = PluginState.Running;
                _enabled = true;
                _lastError = "";
                RaiseChanged();
                return true;
            }
            catch (Exception ex)
            {
                _state = PluginState.Error;
                _lastError = ex.Message;
                _enabled = false;
                error = ex.Message;
                RaiseChanged();
                return false;
            }
        }

        public void Disable()
        {
            try
            {
                Revert();
            }
            catch (Exception)
            {
            }
            _state = PluginState.Disabled;
            _enabled = false;
            RaiseChanged();
        }

        public abstract PluginStats GetStats();
        public abstract void SaveConfig();

        protected abstract bool Apply(out string error);
        protected abstract void Revert();

        protected void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception) { }
            }
        }
    }

    /// <summary>
    /// 插件宿主：持有全部补丁插件，负责开关、状态汇总、配置保存与对外事件。
    /// 界面只跟它打交道；新增补丁不需要改界面代码。
    /// </summary>
    public sealed class PluginHost
    {
        private readonly List<IPatchPlugin> _plugins = new List<IPatchPlugin>();
        private readonly PluginCapabilities _capabilities;

        public PluginHost(PluginCapabilities capabilities)
        {
            _capabilities = capabilities ?? new PluginCapabilities();
        }

        public event EventHandler Changed;

        public PluginCapabilities Capabilities { get { return _capabilities; } }

        public IPatchPlugin[] Plugins
        {
            get { return _plugins.ToArray(); }
        }

        public int Count { get { return _plugins.Count; } }

        public int RunningCount
        {
            get
            {
                int n = 0;
                foreach (IPatchPlugin p in _plugins) if (p.Enabled) n++;
                return n;
            }
        }

        public IPatchPlugin Find(string id)
        {
            foreach (IPatchPlugin p in _plugins)
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public void Add(IPatchPlugin plugin)
        {
            if (plugin == null || Find(plugin.Id) != null) return;
            _plugins.Add(plugin);
            plugin.Changed += delegate { RaiseChanged(); };
        }

        /// <summary>启动时按配置恢复各插件（配置由插件自己读）。</summary>
        public void RestoreAll()
        {
            foreach (IPatchPlugin p in _plugins)
            {
                if (p.Enabled) continue;
                PatchPluginBase typed = p as PatchPluginBase;
                if (typed == null || !typed.WasEnabledOnLastRun) continue;
                string error;
                if (!p.Enable(out error)) Log.Instance.Warn("插件 {0} 恢复失败：{1}", p.Name, error);
            }
            RaiseChanged();
        }

        public bool SetEnabled(IPatchPlugin plugin, bool enabled, out string error)
        {
            error = null;
            if (plugin == null) return false;
            if (enabled)
            {
                bool ok = plugin.Enable(out error);
                if (ok) Log.Instance.Info("插件已启用：{0}", plugin.Name);
                else Log.Instance.Warn("插件启用失败：{0}（{1}）", plugin.Name, error);
                RaiseChanged();
                return ok;
            }

            plugin.Disable();
            Log.Instance.Info("插件已停用：{0}", plugin.Name);
            RaiseChanged();
            return true;
        }

        public void StopAll()
        {
            foreach (IPatchPlugin p in _plugins)
            {
                if (p.Enabled) p.Disable();
            }
            RaiseChanged();
        }

        public void SaveAll()
        {
            foreach (IPatchPlugin p in _plugins) p.SaveConfig();
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception) { }
            }
        }
    }

    /// <summary>供插件判断"上次退出时是否是启用状态"（由 AppSettings 提供，避免插件直接读全局配置）。</summary>
    public interface IPluginStateStore
    {
        bool GetEnabled(string pluginId);
        void SetEnabled(string pluginId, bool enabled);
    }
}
