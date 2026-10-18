using System;
using TouchPlus.Core.Plugins;

namespace TouchPlus.Plugins
{
    /// <summary>
    /// 插件包入口：宿主通过反射找到实现 IPluginFactory 的类型并调用 Create。
    /// 这样插件编译成独立 DLL，宿主不需要在编译期认识插件类型。
    /// </summary>
    public sealed class DoubleClickPluginFactory : IPluginFactory
    {
        public string Id { get { return "touchplus.tolerance.doubleclick"; } }

        public string Name { get { return "触控双击容差"; } }

        public IPatchPlugin Create(PluginContext context)
        {
            if (context == null || context.Controller == null) return null;
            return new DoubleClickPlugin(context.Controller);
        }
    }
}
