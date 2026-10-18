using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TouchPlus.Core.Plugins
{
    /// <summary>
    /// 创建插件时由宿主注入的上下文。插件只能拿到这里给的东西，
    /// 这样"插件能对系统做什么"由宿主控制，而不是插件自己说了算。
    /// </summary>
    public sealed class PluginContext
    {
        /// <summary>宿主已装配好的双击容差引擎（内置插件使用）。</summary>
        public PatchController Controller;

        public AppSettings Settings;

        /// <summary>宿主授权的安全边界，插件必须遵守。</summary>
        public PluginCapabilities Capabilities = new PluginCapabilities();

        public Log Log = Log.Instance;
    }

    /// <summary>
    /// 插件工厂：外部插件包只需暴露一个实现了该接口的类型（公共无参构造函数），
    /// 宿主就能创建它。这样插件不必依赖宿主的构造顺序。
    /// </summary>
    public interface IPluginFactory
    {
        string Id { get; }
        string Name { get; }
        IPatchPlugin Create(PluginContext context);
    }

    /// <summary>
    /// 插件加载器：从 .dll 里找出 IPluginFactory（优先）或可直接构造的 IPatchPlugin。
    /// 只做接口校验，不做任何"信任"假设 —— 校验不过就把原因返回给界面。
    /// </summary>
    public static class PluginLoader
    {
        public static IPatchPlugin Load(string path, PluginContext context, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "插件文件不存在：" + path;
                return null;
            }

            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                Type[] types = assembly.GetTypes();

                // 1) 优先找工厂
                foreach (Type type in types)
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IPluginFactory).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        error = "插件工厂 " + type.Name + " 缺少公共无参构造函数";
                        return null;
                    }

                    IPluginFactory factory = (IPluginFactory)Activator.CreateInstance(type);
                    IPatchPlugin plugin = factory.Create(context);
                    if (plugin == null)
                    {
                        error = "插件工厂 " + type.Name + " 返回了 null";
                        return null;
                    }
                    return plugin;
                }

                // 2) 退一步：直接找可构造的插件
                foreach (Type type in types)
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IPatchPlugin).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    return (IPatchPlugin)Activator.CreateInstance(type);
                }

                error = "这个文件没有实现 IPatchPlugin 或 IPluginFactory 接口";
                return null;
            }
            catch (Exception ex)
            {
                error = "加载失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>校验插件是否越界使用了宿主没授权的能力。</summary>
        public static bool CheckCapabilities(IPatchPlugin plugin, PluginCapabilities granted, out string error)
        {
            error = null;
            PluginCapabilities wanted = plugin.Capabilities;
            if (wanted == null) return true;
            if (wanted.AllowSystemParams && !granted.AllowSystemParams)
            {
                error = "插件声明需要修改系统参数，但宿主未授权";
                return false;
            }
            if (wanted.AllowInputHook && !granted.AllowInputHook)
            {
                error = "插件声明需要安装输入钩子，但宿主未授权";
                return false;
            }
            if (wanted.AllowInputInjection && !granted.AllowInputInjection)
            {
                error = "插件声明需要合成输入，但宿主未授权";
                return false;
            }
            return true;
        }
    }

    /// <summary>内置插件注册表：宿主自带的插件由这里创建（与外部插件走同一契约）。</summary>

}
