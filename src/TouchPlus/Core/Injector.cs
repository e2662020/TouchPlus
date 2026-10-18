using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TouchPlus.Core
{
    /// <summary>输入注入：优先 SendInput，失败自动退回 mouse_event（Win7 上也稳）。</summary>
    public static class Injector
    {
        /// <summary>
        /// 本程序自己注入的输入会带上这个 dwExtraInfo 标记，钩子见到它就跳过，
        /// 避免"自己注入的事件再次触发修复"造成递归。
        /// </summary>
        public const long InjectionTag = 0x7A5A7A5AL;

        public static int InputStructSize
        {
            get { return Marshal.SizeOf(typeof(Win32.INPUT)); }
        }

        public static int ExpectedInputStructSize
        {
            get { return IntPtr.Size == 8 ? 40 : 28; }
        }

        public static bool MoveTo(int x, int y)
        {
            if (!SystemInfo.IsPointOnVirtualScreen(x, y)) return false;
            try { return Win32.SetCursorPos(x, y); }
            catch (Exception) { return false; }
        }

        public static bool GetCursor(out int x, out int y)
        {
            Win32.POINT p;
            if (Win32.GetCursorPos(out p)) { x = p.X; y = p.Y; return true; }
            x = 0; y = 0;
            return false;
        }

        public static bool SendMouse(uint flags)
        {
            return SendMouse(flags, true);
        }

        public static bool SendMouse(uint flags, bool tag)
        {
            try
            {
                Win32.INPUT[] inputs = new Win32.INPUT[1];
                inputs[0].type = Win32.INPUT_MOUSE;
                inputs[0].mi.dx = 0;
                inputs[0].mi.dy = 0;
                inputs[0].mi.mouseData = 0;
                inputs[0].mi.dwFlags = flags;
                inputs[0].mi.time = 0;
                inputs[0].mi.dwExtraInfo = tag ? new IntPtr(InjectionTag) : IntPtr.Zero;

                uint sent = Win32.SendInput(1, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
                if (sent == 1) return true;
            }
            catch (Exception)
            {
            }

            try
            {
                Win32.mouse_event(flags, 0, 0, 0, tag ? new UIntPtr((ulong)InjectionTag) : UIntPtr.Zero);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool LeftDown() { return SendMouse(Win32.MOUSEEVENTF_LEFTDOWN, true); }
        public static bool LeftUp() { return SendMouse(Win32.MOUSEEVENTF_LEFTUP, true); }

        /// <summary>在指定坐标完成一次完整单击（先定位光标，再按下抬起）。</summary>
        public static bool ClickAt(int x, int y, int holdMs)
        {
            return ClickAt(x, y, holdMs, true);
        }

        public static bool ClickAt(int x, int y, int holdMs, bool tag)
        {
            if (!MoveTo(x, y)) return false;
            // 移动与按下之间留一点时间：某些系统会把"移动+按下"合并成一个报文，
            // 导致低级钩子收不到这次按下。
            Thread.Sleep(15);
            if (!SendMouse(Win32.MOUSEEVENTF_LEFTDOWN, tag)) return false;
            if (holdMs > 0) Thread.Sleep(holdMs);
            return SendMouse(Win32.MOUSEEVENTF_LEFTUP, tag);
        }

        /// <summary>
        /// 在指定坐标合成双击。
        /// pairs=1 表示只补一对（此时希望它与"第一次物理点击"配成双击）；
        /// pairs=2 表示补两对，自成一次完整双击（间隔超过系统双击时间时的兜底）。
        /// </summary>
        public static void DoubleClickAt(int x, int y, int pairs)
        {
            if (pairs < 1) pairs = 1;
            MoveTo(x, y);
            Thread.Sleep(6);
            for (int i = 0; i < pairs; i++)
            {
                LeftDown();
                Thread.Sleep(12);
                LeftUp();
                if (i + 1 < pairs) Thread.Sleep(45);
            }
        }

        /// <summary>长按重放：立刻按下，delayMs 后在后台线程抬起。</summary>
        public static void LongPressAt(int x, int y, int delayMs)
        {
            MoveTo(x, y);
            LeftDown();
            if (delayMs < 0) delayMs = 0;
            Timer timer = null;
            timer = new Timer(delegate(object state)
            {
                try { LeftUp(); }
                catch (Exception) { }
                try { if (timer != null) timer.Dispose(); }
                catch (Exception) { }
            }, null, delayMs, Timeout.Infinite);
        }
    }
}
