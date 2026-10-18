using System;
using System.Globalization;
using Microsoft.Win32;

namespace TouchPlus.Core
{
    /// <summary>
    /// 补丁机制一：系统层双击参数。
    ///
    /// Windows 判定双击需要同时满足"两次点击落在同一个 4×4 像素矩形内"和"间隔小于双击时间"。
    /// 触控屏手指抖动动辄十几像素，因此系统永远识别不到。
    /// 通过 SystemParametersInfo(SPI_SETDOUBLECLKWIDTH/HEIGHT) 把矩形放大、用 SetDoubleClickTime
    /// 放宽时间窗，系统就会原生识别抖动双击（无需钩子、无需注入，全系统生效）。
    ///
    /// 实测结论（Win11 26200 / 亦适用于 Win7 SP1+）：
    ///  * SPI_SETDOUBLECLKWIDTH / HEIGHT 的数值必须放在 uiParam 里（放 pvParam 会写入 0）；
    ///  * 带 SPIF_UPDATEINIFILE 时会写注册表并即时生效，GetSystemMetrics(36/37) 立刻变化；
    ///  * SetDoubleClickTime 只改内存值，不写注册表，所以需要自己补写 DoubleClickSpeed 以便重启后仍然生效。
    /// </summary>
    public static class DoubleClickParams
    {
        public const int MinRect = 2;
        public const int MaxRect = 200;
        public const int MinTime = 200;
        public const int MaxTime = 2000;

        private const string MouseKeyPath = @"Control Panel\Mouse";
        private const string TimeValueName = "DoubleClickSpeed";

        public static int GetRectWidth()
        {
            int v = Win32.GetSystemMetrics(Win32.SM_CXDOUBLECLK);
            return v > 0 ? v : 4;
        }

        public static int GetRectHeight()
        {
            int v = Win32.GetSystemMetrics(Win32.SM_CYDOUBLECLK);
            return v > 0 ? v : 4;
        }

        public static int GetTime()
        {
            uint t = Win32.GetDoubleClickTime();
            return t > 0 ? (int)t : 500;
        }

        public static string DescribeCurrent()
        {
            return string.Format(CultureInfo.InvariantCulture, "系统当前：双击矩形 {0}×{1}px，双击时间 {2}ms",
                GetRectWidth(), GetRectHeight(), GetTime());
        }

        /// <summary>设置双击矩形（同时写入用户配置，重启后仍然有效）。</summary>
        public static bool SetRect(int width, int height)
        {
            width = AppSettings.Clamp(width, MinRect, MaxRect);
            height = AppSettings.Clamp(height, MinRect, MaxRect);

            bool a = Win32.SystemParametersInfo(Win32.SPI_SETDOUBLECLKWIDTH, (uint)width, IntPtr.Zero,
                Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE);
            bool b = Win32.SystemParametersInfo(Win32.SPI_SETDOUBLECLKHEIGHT, (uint)height, IntPtr.Zero,
                Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE);
            return a && b;
        }

        /// <summary>设置双击时间窗（内存即时生效 + 补写注册表保证持久）。</summary>
        public static bool SetTime(int milliseconds)
        {
            milliseconds = AppSettings.Clamp(milliseconds, MinTime, MaxTime);
            bool ok = Win32.SetDoubleClickTime((uint)milliseconds);
            bool persisted = WriteTimeToRegistry(milliseconds);
            BroadcastMouseSettingsChanged();
            return ok && persisted;
        }

        public static void Apply(int width, int height, int milliseconds)
        {
            SetRect(width, height);
            SetTime(milliseconds);
        }

        private static bool WriteTimeToRegistry(int milliseconds)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MouseKeyPath, true))
                {
                    if (key == null) return false;
                    object existing = key.GetValue(TimeValueName);
                    string text = milliseconds.ToString(CultureInfo.InvariantCulture);
                    if (existing is string && (string)existing == text) return true;
                    key.SetValue(TimeValueName, text, RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void BroadcastMouseSettingsChanged()
        {
            try
            {
                IntPtr result;
                Win32.SendMessageTimeout(Win32.HWND_BROADCAST, (uint)Win32.WM_SETTINGCHANGE, IntPtr.Zero,
                    "Control Panel\\Mouse", Win32.SMTO_ABORTIFHUNG, 300, out result);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>读注册表里的双击时间（用于判断"用户配置"与"当前内存值"是否一致）。</summary>
        public static int GetPersistedTime()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MouseKeyPath, false))
                {
                    if (key == null) return GetTime();
                    object v = key.GetValue(TimeValueName);
                    string s = v as string;
                    int parsed;
                    if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                        return parsed;
                }
            }
            catch (Exception)
            {
            }
            return GetTime();
        }
    }
}
