using System;
using System.Globalization;
using System.Security.Principal;

namespace TouchPlus.Core
{
    /// <summary>运行环境探测：系统版本、是否为触控设备、是否有管理员权限、虚拟桌面范围。</summary>
    public static class SystemInfo
    {
        private static bool _versionCached;
        private static string _versionText = "";
        private static uint _major, _minor, _build;

        public static int DigitizerFlags
        {
            get { return Win32.GetSystemMetrics(Win32.SM_DIGITIZER); }
        }

        public static bool HasTouchOrPen
        {
            get
            {
                int f = DigitizerFlags;
                return (f & (Win32.NID_INTEGRATED_TOUCH | Win32.NID_EXTERNAL_TOUCH | Win32.NID_INTEGRATED_PEN | Win32.NID_EXTERNAL_PEN)) != 0;
            }
        }

        public static bool HasTouch
        {
            get
            {
                int f = DigitizerFlags;
                return (f & (Win32.NID_INTEGRATED_TOUCH | Win32.NID_EXTERNAL_TOUCH)) != 0;
            }
        }

        public static string DescribeDigitizer()
        {
            int f = DigitizerFlags;
            if (f == 0) return "未检测到数字化仪（可能是普通鼠标环境，或触摸由鼠标报文模拟）";
            string s = "数字化仪：";
            if ((f & Win32.NID_READY) != 0) s += "就绪 ";
            if ((f & Win32.NID_INTEGRATED_TOUCH) != 0) s += "内置触摸 ";
            if ((f & Win32.NID_EXTERNAL_TOUCH) != 0) s += "外接触摸 ";
            if ((f & Win32.NID_INTEGRATED_PEN) != 0) s += "内置笔 ";
            if ((f & Win32.NID_EXTERNAL_PEN) != 0) s += "外接笔 ";
            if ((f & Win32.NID_MULTI_INPUT) != 0) s += "多点输入 ";
            return s.Trim();
        }

        public static void ReadVersion()
        {
            if (_versionCached) return;
            _versionCached = true;
            try
            {
                Win32.OSVERSIONINFOEX info = new Win32.OSVERSIONINFOEX();
                info.dwOSVersionInfoSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.OSVERSIONINFOEX));
                if (Win32.RtlGetVersion(ref info) == 0)
                {
                    _major = info.dwMajorVersion;
                    _minor = info.dwMinorVersion;
                    _build = info.dwBuildNumber;
                }
            }
            catch (Exception)
            {
            }

            if (_major == 0)
            {
                _major = (uint)Environment.OSVersion.Version.Major;
                _minor = (uint)Environment.OSVersion.Version.Minor;
                _build = (uint)Environment.OSVersion.Version.Build;
            }

            string name;
            if (_major == 6 && _minor == 1) name = "Windows 7";
            else if (_major == 6 && _minor == 2) name = "Windows 8";
            else if (_major == 6 && _minor == 3) name = "Windows 8.1";
            else if (_major == 10 && _build >= 22000) name = "Windows 11";
            else if (_major == 10) name = "Windows 10";
            else name = "Windows";

            _versionText = string.Format(CultureInfo.InvariantCulture, "{0}（{1}.{2}.{3}）", name, _major, _minor, _build);
        }

        public static string VersionText
        {
            get { ReadVersion(); return _versionText; }
        }

        public static bool IsWindows7
        {
            get { ReadVersion(); return _major == 6 && _minor == 1; }
        }

        public static bool IsWindows8OrOlder
        {
            get { ReadVersion(); return _major < 10; }
        }

        private static bool? _elevated;

        public static bool IsElevated
        {
            get
            {
                if (_elevated.HasValue) return _elevated.Value;
                bool value = false;
                try
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        WindowsPrincipal principal = new WindowsPrincipal(identity);
                        value = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch (Exception)
                {
                    value = false;
                }
                _elevated = value;
                return value;
            }
        }

        public static int VirtualScreenLeft { get { return Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN); } }
        public static int VirtualScreenTop { get { return Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN); } }
        public static int VirtualScreenWidth { get { return Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN); } }
        public static int VirtualScreenHeight { get { return Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN); } }

        public static bool IsPointOnVirtualScreen(int x, int y)
        {
            int left = VirtualScreenLeft;
            int top = VirtualScreenTop;
            return x >= left && y >= top && x < left + VirtualScreenWidth && y < top + VirtualScreenHeight;
        }
    }
}
