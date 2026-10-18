using System;
using System.Globalization;
using Microsoft.Win32;

namespace TouchPlus.Core
{
    /// <summary>开机自启：写 HKCU\...\Run（重装系统/换用户也不影响，且不需要管理员权限）。</summary>
    public static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TouchPlus";

        public static string BuildCommand(string exePath, bool minimized)
        {
            string quoted = "\"" + exePath + "\"";
            return minimized ? quoted + " --tray" : quoted + " --show";
        }

        public static string CurrentCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return null;
                    return key.GetValue(ValueName) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsEnabled()
        {
            string cmd = CurrentCommand();
            return !string.IsNullOrEmpty(cmd);
        }

        public static bool Enable(string exePath, bool minimized)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    key.SetValue(ValueName, BuildCommand(exePath, minimized), RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool Disable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return true;
                    if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>自启项指向的路径是否就是当前 exe（换目录后需要重写）。</summary>
        public static bool PointsTo(string exePath)
        {
            string cmd = CurrentCommand();
            if (string.IsNullOrEmpty(cmd)) return false;
            return cmd.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string Describe()
        {
            string cmd = CurrentCommand();
            if (string.IsNullOrEmpty(cmd)) return "未开启";
            return string.Format(CultureInfo.InvariantCulture, "HKCU\\...\\Run\\{0} = {1}", ValueName, cmd);
        }
    }
}
