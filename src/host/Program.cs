using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using TouchPlus.Core;
using TouchPlus.Core.Plugins;
using TouchPlus.UI;

namespace TouchPlus
{
    /// <summary>
    /// 入口：WinForms 界面（此前已验收的视觉设计）。
    /// WPF 版本保留在 Wpf/ 目录下，等视觉对齐后再逐块切换。
    /// </summary>
    internal static class Program
    {
        public const string Version = "1.1.0";

        private static Mutex _mutex;

        [STAThread]
        private static int Main(string[] args)
        {
            bool safeMode = HasFlag(args, "--safe");
            bool startMinimized = HasFlag(args, "--tray") || HasFlag(args, "--minimized");
            bool resetAll = HasFlag(args, "--reset");
            bool settingsMode = HasFlag(args, "--settings");
            string themeArg = GetArg(args, "--theme", "");
            int autoClose = GetIntArg(args, "--autoclose", 0);
            string addPlugin = GetArg(args, "--add-plugin", "");

            bool createdNew;
            _mutex = new Mutex(true, "TouchPlus.SingleInstance.v2", out createdNew);
            if (!createdNew) return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                SettingsLoadResult loaded = SettingsStore.Load();
                AppSettings settings = loaded.Settings;
                if (resetAll)
                {
                    settings = new AppSettings();
                    if (!settings.ReadOnlySession) SettingsStore.Save(settings);
                }
                if (string.IsNullOrEmpty(themeArg)) themeArg = settings.ThemeMode;
                Theme.ApplyMode(ParseTheme(themeArg));

                PatchController controller = new PatchController(settings);
                PluginContext context = new PluginContext();
                context.Settings = settings;
                context.Controller = controller;
                context.Capabilities.AllowInputHook = true;
                context.Capabilities.AllowInputInjection = true;
                context.Capabilities.AllowSystemParams = true;

                Log.Instance.ConfigureFile(SettingsStore.LogDirectory, true, settings.VerboseLog);
                Log.Instance.Info("Touch+ v" + Version + " 启动（" + SystemInfo.VersionText + "）");

                PluginHost host = new PluginHost(context.Capabilities);
                // 插件与宿主分开编译：启动时扫描 exe 同级的 plugins 目录
                string pluginDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "plugins");
                if (!Directory.Exists(pluginDir)) Directory.CreateDirectory(pluginDir);
                foreach (string dll in Directory.GetFiles(pluginDir, "*.dll"))
                {
                    string loadError;
                    IPatchPlugin plugin = PluginLoader.Load(dll, context, out loadError);
                    if (plugin == null)
                    {
                        Log.Instance.Warn("插件加载失败：" + Path.GetFileName(dll) + "：" + loadError);
                        continue;
                    }
                    if (host.Find(plugin.Id) != null) continue;
                    host.Add(plugin);
                    Log.Instance.Info("插件已加载：" + plugin.Name + "（" + Path.GetFileName(dll) + "）");
                }

                foreach (string path in new List<string>(settings.PluginPaths))
                {
                    string error;
                    IPatchPlugin external = PluginLoader.Load(path, context, out error);
                    if (external == null) { Log.Instance.Warn("外部插件加载失败：" + path + "：" + error); continue; }
                    if (host.Find(external.Id) != null) continue;
                    host.Add(external);
                }

                if (!string.IsNullOrEmpty(addPlugin))
                {
                    string error;
                    IPatchPlugin plugin = PluginLoader.Load(addPlugin, context, out error);
                    if (plugin == null) { Log.Instance.Error("添加插件失败：" + error); return 2; }
                    if (host.Find(plugin.Id) == null)
                    {
                        host.Add(plugin);
                        settings.PluginPaths.Add(addPlugin);
                        if (!settings.ReadOnlySession) SettingsStore.Save(settings);
                    }
                    return 0;
                }

                if (!safeMode)
                {
                    host.RestoreAll();
                    controller.Start();
                }

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Instance.Error(e.Exception, "界面线程异常");
                    TraceStartupError(e.Exception);
                    MessageBox.Show("Touch+ 发生异常：\r\n" + e.Exception.Message, "Touch+",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };

                Form form;
                if (settingsMode)
                {
                    MainForm settingsForm = host.Plugins.Length > 0
                        ? new MainForm(controller, startMinimized, host.Plugins[0])
                        : new MainForm(controller, startMinimized);
                    settingsForm.InitialPage = GetIntArg(args, "--page", 0);
                    form = settingsForm;
                }
                else
                {
                    form = new PluginCenterForm(host, context, startMinimized);
                }

                if (autoClose > 0)
                {
                    System.Windows.Forms.Timer closer = new System.Windows.Forms.Timer();
                    closer.Interval = autoClose * 1000;
                    closer.Tick += delegate { closer.Stop(); form.Close(); };
                    closer.Start();
                }

                Application.Run(form);

                try { host.StopAll(); } catch (Exception) { }
                try { controller.RestoreSystemLayer(); } catch (Exception) { }
                Log.Instance.Info("Touch+ 退出");
                return 0;
            }
            catch (Exception ex)
            {
                TraceStartupError(ex);
                MessageBox.Show("Touch+ 启动失败：\r\n\r\n" + ex.Message, "Touch+",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static Theme.ThemeMode ParseTheme(string value)
        {
            if (string.IsNullOrEmpty(value)) return Theme.ThemeMode.Auto;
            string v = value.Trim().ToLowerInvariant();
            if (v == "light") return Theme.ThemeMode.Light;
            if (v == "dark") return Theme.ThemeMode.Dark;
            return Theme.ThemeMode.Auto;
        }

        internal static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string GetArg(string[] args, string key, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }

        internal static int GetIntArg(string[] args, string key, int fallback)
        {
            int value;
            return int.TryParse(GetArg(args, key, ""), out value) ? value : fallback;
        }

        internal static void TraceStartupError(Exception ex)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "touchplus-startup-error.txt");
                File.WriteAllText(path, DateTime.Now + "\r\n" + ex);
            }
            catch (Exception)
            {
            }
        }
    }
}