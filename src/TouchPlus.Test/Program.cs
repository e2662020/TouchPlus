using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using TouchPlus.Core;

namespace TouchPlus.Test
{
    internal static class T
    {
        public static int Passed;
        public static int Failed;
        public static readonly List<string> Failures = new List<string>();

        public static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("== " + name + " " + new string('=', Math.Max(2, 60 - name.Length)));
        }

        public static void Check(bool ok, string name, string detail)
        {
            if (ok)
            {
                Passed++;
                Console.WriteLine("  [PASS] " + name + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
            }
            else
            {
                Failed++;
                string line = name + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")");
                Failures.Add(line);
                Console.WriteLine("  [FAIL] " + line);
            }
        }

        public static void Check(bool ok, string name)
        {
            Check(ok, name, null);
        }

        public static void Eq(object expected, object actual, string name)
        {
            bool ok = expected == null ? actual == null : expected.Equals(actual);
            Check(ok, name, ok ? null : string.Format(CultureInfo.InvariantCulture, "期望 {0}，实际 {1}", expected, actual));
        }

        public static void Contains(string haystack, string needle, string name)
        {
            bool ok = haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            Check(ok, name, ok ? null : "未包含 \"" + needle + "\"：" + haystack);
        }

        public static void Info(string format, params object[] args)
        {
            Console.WriteLine("  · " + string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public static int Report()
        {
            Console.WriteLine();
            Console.WriteLine(new string('-', 66));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "通过 {0} 项，失败 {1} 项", Passed, Failed));
            if (Failed > 0)
            {
                Console.WriteLine("失败明细：");
                foreach (string f in Failures) Console.WriteLine("  - " + f);
            }
            return Failed == 0 ? 0 : 1;
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (Exception) { }

            try { SetProcessDPIAware(); }
            catch (Exception) { }

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
            Console.WriteLine("Touch+ 自检程序  v" + ProgramVersion.Value);
            Console.WriteLine("系统：" + SystemInfo.VersionText + "  管理员：" + (SystemInfo.IsElevated ? "是" : "否"));
            Console.WriteLine("模式：" + mode);

            bool runAll = mode == "all" || mode == "selftest";
            if (runAll) CoreTests.RunAll();
            if (mode == "logic") CoreTests.RunAll(false);   // 纯内存测试，不碰系统设置
            if (mode == "all" || mode == "hooktest") HookTests.RunEngineThroughHook();
            if (mode == "recttest") HookTests.RunRectExperiment();
            if (mode == "experiment")
            {
                HookTests.RunRepositionExperiment("坐标对齐（仅 SetCursorPos）", false);
                HookTests.RunSynthesizeExperiment();
            }

            return T.Report();
        }
    }

    internal static class ProgramVersion
    {
        public const string Value = "1.0.0";
    }
}
