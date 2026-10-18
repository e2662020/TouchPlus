using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using TouchPlus.Core;

namespace TouchPlus.Test
{
    internal static class CoreTests
    {
        public static void RunAll()
        {
            RunAll(true);
        }

        /// <summary>includeSystemParams=false 时完全不碰系统设置（纯内存测试，绝对安全）。</summary>
        public static void RunAll(bool includeSystemParams)
        {
            JsonTests();
            SettingsTests();
            RuleTests();
            EngineTests();
            if (includeSystemParams) SystemParamTests();
            SystemInfoTests();
            if (includeSystemParams) AutostartTests();
        }

        /* ------------------------------------------------------------------ */

        private static void JsonTests()
        {
            T.Section("JSON 读写");

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["s"] = "中文 \"引号\" \\ 反斜杠 \n 换行";
            root["n"] = 42d;
            root["f"] = 1.5d;
            root["b"] = true;
            root["nil"] = null;
            root["arr"] = new List<object>(new object[] { 1d, "two", false });
            Dictionary<string, object> nested = new Dictionary<string, object>();
            nested["k"] = "v";
            root["obj"] = nested;

            string text = Json.Write(root, true);
            object parsed;
            string error;
            bool ok = Json.TryParse(text, out parsed, out error);
            T.Check(ok, "格式化输出可被解析", error);

            Dictionary<string, object> back = Json.Obj(parsed);
            T.Eq("中文 \"引号\" \\ 反斜杠 \n 换行", Json.GetString(back, "s", null), "字符串转义往返");
            T.Eq(42, Json.GetInt(back, "n", -1), "整数往返");
            T.Eq(true, Json.GetBool(back, "b", false), "布尔往返");
            T.Eq("v", Json.GetString(Json.Obj(Json.Get(back, "obj")), "k", null), "嵌套对象往返");
            T.Eq(3, Json.Array(back, "arr").Count, "数组长度");
            T.Eq("two", Json.Array(back, "arr")[1] as string, "数组元素");

            object bad;
            string badError;
            T.Check(!Json.TryParse("{\"a\": }", out bad, out badError), "非法 JSON 被拒绝", badError);
            T.Check(!Json.TryParse("", out bad, out badError), "空文本被拒绝");
        }

        /* ------------------------------------------------------------------ */

        private static void SettingsTests()
        {
            T.Section("设置持久化");

            AppSettings s = new AppSettings();
            s.Normalize();
            T.Eq(50, s.SystemDoubleClickWidth, "默认双击矩形宽 50");
            T.Eq(800, s.SystemDoubleClickTime, "默认双击时间 800ms");
            T.Eq(80, s.Tolerance, "默认容差 80px");
            T.Eq(RepairStrategy.Synthesize, s.Strategy, "默认修复方式：合成双击（坐标对齐已实测无效）");
            T.Eq(false, s.HookLayerEnabled, "输入层默认关闭（系统层已够用）");

            s.Tolerance = 99999;
            s.TimeWindow = 1;
            s.ExcludedProcesses = new List<string>(new string[] { "  C:\\Program Files\\Game\\game.exe  ", "GAME.EXE", "", "  " });
            s.Normalize();
            T.Eq(200, s.Tolerance, "容差被夹到上限");
            T.Eq(150, s.TimeWindow, "时间窗被夹到下限");
            T.Eq(1, s.ExcludedProcesses.Count, "排除列表去重并去掉空项");
            T.Eq("game.exe", s.ExcludedProcesses[0], "排除项归一化（取文件名、小写）");

            string dir = Path.Combine(Path.GetTempPath(), "touchplus-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "settings.json");
            try
            {
                s.Tolerance = 77;
                s.Anchor = AnchorMode.Midpoint;
                s.Source = SourceMode.All;
                T.Check(SettingsStore.Save(s, path), "保存配置成功");
                T.Check(File.Exists(path), "配置文件已生成");
                string raw = File.ReadAllText(path, Encoding.UTF8);
                T.Check(raw.Contains("\"tolerance\": 77"), "配置文件是可读的 JSON");
                T.Check(raw.Contains("Midpoint"), "枚举以文本保存");

                SettingsLoadResult loaded = SettingsStore.Load(path);
                T.Eq(SettingsLoadState.Loaded, loaded.State, "读取状态正常");
                T.Eq(77, loaded.Settings.Tolerance, "容差往返一致");
                T.Eq(AnchorMode.Midpoint, loaded.Settings.Anchor, "锚点往返一致");
                T.Eq(SourceMode.All, loaded.Settings.Source, "输入来源往返一致");

                File.WriteAllText(path, "这不是 JSON {{{", Encoding.UTF8);
                try { File.Delete(path + ".bak"); } catch (Exception) { }
                SettingsLoadResult broken = SettingsStore.Load(path);
                T.Eq(SettingsLoadState.FailedUsedDefault, broken.State, "没有备份时损坏配置回退默认值");
                T.Check(File.Exists(path + ".bad"), "损坏配置留档 .bad");

                // 有备份时：应当从备份恢复，而不是丢配置
                SettingsStore.Save(s, path);
                SettingsStore.Save(s, path);            // 第二次保存会生成 .bak
                T.Check(File.Exists(path + ".bak"), "保存时会生成同目录备份");
                File.WriteAllText(path, "{坏掉了", Encoding.UTF8);
                SettingsLoadResult recovered = SettingsStore.Load(path);
                T.Eq(SettingsLoadState.RecoveredFromBackup, recovered.State, "有备份时从备份恢复");
                T.Eq(77, recovered.Settings.Tolerance, "备份里的配置被正确恢复");

                // 未知字段应该被忽略（向前兼容）
                File.WriteAllText(path, "{\"tolerance\": 55, \"futureField\": {\"a\":1}}", Encoding.UTF8);
                SettingsLoadResult tolerant = SettingsStore.Load(path);
                T.Eq(SettingsLoadState.Loaded, tolerant.State, "含未知字段仍可读取");
                T.Eq(55, tolerant.Settings.Tolerance, "未知字段不影响已知字段");
            }
            finally
            {
                try { Directory.Delete(dir, true); }
                catch (Exception) { }
            }
        }

        private static void RuleTests()
        {
            T.Section("排除程序匹配");

            List<string> rules = new List<string>(new string[] { "game.exe", "seewo*", "?ote*" });
            T.Check(ProcessRules.IsExcluded(rules, "game"), "按进程名匹配（不含扩展名）");
            T.Check(ProcessRules.IsExcluded(rules, "GAME.EXE"), "大小写不敏感");
            T.Check(ProcessRules.IsExcluded(rules, "seewoboard"), "前缀通配");
            T.Check(ProcessRules.IsExcluded(rules, "notepad"), "单字符通配");
            T.Check(!ProcessRules.IsExcluded(rules, "explorer"), "不相关进程不匹配");
            T.Check(!ProcessRules.IsExcluded(null, "explorer"), "空规则表安全");
            T.Eq("game.exe", ProcessRules.NormalizeRule("C:\\Games\\Game.exe"), "完整路径归一化为进程名");
        }

        /* ------------------------------------------------------------------ */

        private static ClickSample Sample(int x, int y, uint time)
        {
            return Sample(x, y, time, InputKind.Touch, new IntPtr(1), "explorer");
        }

        private static ClickSample Sample(int x, int y, uint time, InputKind kind, IntPtr root, string process)
        {
            ClickSample s = new ClickSample();
            s.X = x;
            s.Y = y;
            s.Time = time;
            s.Kind = kind;
            s.RootWindow = root;
            s.HitWindow = root;
            s.ProcessId = 1000;
            s.ProcessName = process;
            return s;
        }

        private static RepairEngine NewEngine()
        {
            RepairEngine e = new RepairEngine(Log.Instance);
            e.Options.Enabled = true;
            e.Options.TolerancePx = 60;
            e.Options.TimeWindowMs = 900;
            e.Options.Strategy = RepairStrategy.Reposition;
            e.Options.Anchor = AnchorMode.Second;
            e.Options.Source = SourceMode.All;
            e.Options.RequireSameWindow = true;
            e.Options.DragResetPx = 16;
            e.Options.EmulateLongPress = true;
            e.Options.LongPressGuardMs = 450;
            e.Options.SystemRectWidth = 4;
            e.Options.SystemRectHeight = 4;
            e.Options.SystemTime = 500;
            e.Options.SystemHasDigitizer = true;
            e.Options.Excluded = new List<string>();
            return e;
        }

        private static void EngineTests()
        {
            T.Section("双击修复引擎");

            /* 1. 单击不应产生任何动作 */
            RepairEngine e = NewEngine();
            EngineAction a1 = e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonUp(Sample(101, 101, 1060));
            T.Eq(EngineActionKind.None, a1.Kind, "第一次点击不触发修复");
            T.Eq(0L, e.RepairedCount, "单击不计数");

            /* 2. 系统本来就能识别的点击对 → 不插手 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a2 = e.OnButtonDown(Sample(102, 101, 1100));
            T.Eq(EngineActionKind.None, a2.Kind, "系统原生可识别时不干预");
            T.Contains(a2.Reason, "原生", "给出原生识别原因");
            T.Eq(0L, e.RepairedCount, "原生识别不计入修复");
            T.Eq(1L, e.NativePairCount, "原生识别计数");

            /* 3. 抖动点击（默认策略：坐标对齐到首次点击） */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a3 = e.OnButtonDown(Sample(125, 120, 1150));
            T.Eq(EngineActionKind.AlignCursor, a3.Kind, "抖动点击 → 坐标对齐");
            T.Eq(false, a3.Swallow, "坐标对齐不吞事件（不注入输入）");
            T.Eq(100, a3.X, "锚定首次点击 X");
            T.Eq(100, a3.Y, "锚定首次点击 Y");
            T.Eq(32, a3.Distance, "偏离距离计算正确");
            T.Eq((uint)150, a3.DeltaMs, "间隔计算正确");
            T.Eq(1L, e.RepairedCount, "修复计数 +1");
            T.Eq(32, e.MaxDistance, "最大偏离被记录");

            /* 4. 超出容差 → 不修复 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a4 = e.OnButtonDown(Sample(300, 400, 1100));
            T.Eq(EngineActionKind.None, a4.Kind, "超出容差不修复");
            T.Contains(a4.Reason, "容差", "原因说明是容差超限");
            T.Eq(1L, e.RejectedCount, "拒绝计数");

            /* 5. 超出时间窗 → 不修复 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a5 = e.OnButtonDown(Sample(105, 105, 2200));
            T.Eq(EngineActionKind.None, a5.Kind, "超出时间窗不修复");
            T.Contains(a5.Reason, "时间窗", "原因说明是时间窗超限");

            /* 6. 不在同一窗口 → 不修复 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000, InputKind.Touch, new IntPtr(1), "explorer"));
            EngineAction a6 = e.OnButtonDown(Sample(110, 105, 1100, InputKind.Touch, new IntPtr(2), "explorer"));
            T.Eq(EngineActionKind.None, a6.Kind, "跨窗口不修复");
            T.Contains(a6.Reason, "同一窗口", "原因说明是跨窗口");

            /* 7. 排除程序 → 不修复 */
            e = NewEngine();
            e.Options.Excluded = new List<string>(new string[] { "game.exe" });
            e.OnButtonDown(Sample(100, 100, 1000, InputKind.Touch, new IntPtr(1), "game"));
            EngineAction a7 = e.OnButtonDown(Sample(120, 110, 1100, InputKind.Touch, new IntPtr(1), "game"));
            T.Eq(EngineActionKind.None, a7.Kind, "排除程序不修复");
            T.Contains(a7.Reason, "排除", "原因说明是排除列表");

            /* 8. 坐标对齐策略下锚点固定为首次点击（第二次/中点无效） */
            e = NewEngine();
            e.Options.Anchor = AnchorMode.Second;
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a8 = e.OnButtonDown(Sample(140, 100, 1100));
            T.Eq(100, a8.X, "坐标对齐策略忽略锚点设置，锚定首次点击");

            /* 9. 合成双击：拦截第二次点击 */
            e = NewEngine();
            e.Options.Strategy = RepairStrategy.Synthesize;
            e.Options.Anchor = AnchorMode.Second;
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a9 = e.OnButtonDown(Sample(140, 100, 1200));
            T.Eq(EngineActionKind.SwallowDown, a9.Kind, "合成双击 → 吞掉第二次按下");
            T.Eq(true, a9.Swallow, "标记为需要吞掉");
            T.Eq(140, a9.X, "锚定第二次点击 X");
            EngineAction a9u = e.OnButtonUp(Sample(140, 100, 1260));
            T.Eq(EngineActionKind.SwallowUpAndInject, a9u.Kind, "抬起时注入合成双击");
            T.Eq(2, a9u.InjectPairs, "锚点与首次点击不同 → 注入两对（自洽双击）");
            T.Eq(false, a9u.EmulateLongPress, "短按不触发长按保护");

            /* 10. 合成双击 + 锚定首次点击 + 快速抬起 → 只需一对 */
            e = NewEngine();
            e.Options.Strategy = RepairStrategy.Synthesize;
            e.Options.Anchor = AnchorMode.First;
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonDown(Sample(140, 100, 1200));
            EngineAction a10 = e.OnButtonUp(Sample(140, 100, 1260));
            T.Eq(1, a10.InjectPairs, "锚点即首次点击且间隔内 → 注入一对，与首次点击配对");

            /* 11. 长按保护 */
            e = NewEngine();
            e.Options.Strategy = RepairStrategy.Synthesize;
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonDown(Sample(140, 100, 1200));
            EngineAction a11 = e.OnButtonUp(Sample(141, 101, 2000));
            T.Eq(true, a11.EmulateLongPress, "长按（800ms）触发长按保护");
            T.Eq(800, a11.LongPressMs, "按原时长重放长按");

            /* 12. 拖动不应被当成双击的第一击 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonUp(Sample(160, 140, 1100));   // 拖了 72px
            EngineAction a12 = e.OnButtonDown(Sample(105, 105, 1200));
            T.Eq(0L, e.RepairedCount, "拖动之后的点击不被误判为双击第二击");
            T.Contains(a12.Reason, "首次点击", "拖动后重新开始配对");

            /* 13. 超时单击再点（不作对） */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonUp(Sample(100, 100, 1040));
            e.OnButtonDown(Sample(102, 100, 3000));  // 距第一次 2000ms
            T.Eq(0L, e.RepairedCount, "间隔过远的两击不作为双击");

            /* 14. 右键/滚轮打断配对 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnOtherInput("右键");
            e.OnButtonDown(Sample(110, 105, 1100));
            T.Eq(0L, e.RepairedCount, "右键打断后不配对");

            /* 15. 时间戳回绕（49.7 天） */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 4294967000u));
            EngineAction a15 = e.OnButtonDown(Sample(101, 101, 200u));
            T.Eq((uint)496, a15.DeltaMs, "时间戳回绕后间隔计算仍然正确");
            T.Eq(1L, e.NativePairCount, "回绕后的小间隔被判定为系统原生可识别");

            /* 16. 输入来源过滤 */
            e = NewEngine();
            e.Options.Source = SourceMode.TouchPenOnly;
            EngineAction a16 = e.OnButtonDown(Sample(100, 100, 1000, InputKind.Mouse, new IntPtr(1), "explorer"));
            e.OnButtonDown(Sample(110, 100, 1100, InputKind.Mouse, new IntPtr(1), "explorer"));
            T.Eq(0L, e.RepairedCount, "仅触控模式忽略鼠标点击");

            e = NewEngine();
            e.Options.Source = SourceMode.Auto;
            e.Options.SystemHasDigitizer = false;
            e.OnButtonDown(Sample(100, 100, 1000, InputKind.Mouse, new IntPtr(1), "explorer"));
            e.OnButtonDown(Sample(130, 100, 1100, InputKind.Mouse, new IntPtr(1), "explorer"));
            T.Eq(1L, e.RepairedCount, "自动模式：无数字化仪时按普通输入处理");

            e = NewEngine();
            e.Options.Source = SourceMode.Auto;
            e.Options.SystemHasDigitizer = true;
            e.OnButtonDown(Sample(100, 100, 1000, InputKind.Touch, new IntPtr(1), "explorer"));
            e.OnButtonDown(Sample(130, 100, 1100, InputKind.Touch, new IntPtr(1), "explorer"));
            T.Eq(1L, e.RepairedCount, "自动模式：触摸输入始终处理");
            e.OnButtonDown(Sample(200, 200, 1200, InputKind.Mouse, new IntPtr(1), "explorer"));
            e.OnButtonDown(Sample(230, 200, 1300, InputKind.Mouse, new IntPtr(1), "explorer"));
            T.Eq(1L, e.RepairedCount, "自动模式：见过触摸签名后忽略鼠标点击");

            /* 17. 平均偏离统计：四次点击修好两对 */
            e = NewEngine();
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonDown(Sample(140, 100, 1100));   // 40px
            e.OnButtonDown(Sample(160, 100, 1200));
            e.OnButtonDown(Sample(200, 100, 1300));   // 40px
            T.Eq(2L, e.RepairedCount, "四次点击修复两对");
            T.Eq(40, e.AverageDistance, "平均偏离统计正确");
            T.Eq(40, e.MaxDistance, "最大偏离统计正确");

            /* 18. 关闭补丁后不动作 */
            e = NewEngine();
            e.Options.Enabled = false;
            e.OnButtonDown(Sample(100, 100, 1000));
            EngineAction a18 = e.OnButtonDown(Sample(130, 100, 1100));
            T.Eq(0L, e.RepairedCount, "补丁关闭时不修复");
            T.Contains(a18.Reason, "未启用", "给出未启用原因");

            /* 19. 触摸签名解析 */
            T.Eq(InputKind.Mouse, MouseHook.KindFromExtraInfo(IntPtr.Zero), "普通鼠标事件识别");
            T.Eq(InputKind.Touch, MouseHook.KindFromExtraInfo(new IntPtr(unchecked((long)0xFF515700))), "触摸签名识别");
            T.Eq(InputKind.Pen, MouseHook.KindFromExtraInfo(new IntPtr(unchecked((long)0xFF515780))), "笔签名识别");

            /* 20. INPUT 结构尺寸（SendInput 的前提） */
            T.Eq(Injector.ExpectedInputStructSize, Injector.InputStructSize, "INPUT 结构体尺寸正确");

            /* 21. 配对报告（供自检区显示） */
            e = NewEngine();
            PairReport captured = null;
            e.PairReported += delegate(object sender, PairReport r) { captured = r; };
            e.OnButtonDown(Sample(100, 100, 1000));
            e.OnButtonDown(Sample(135, 110, 1200));
            T.Check(captured != null && captured.HasPair, "产生配对报告");
            T.Eq(36, captured.Distance, "报告中的偏离距离");
            T.Eq(true, captured.Repaired, "报告标记为已修复");
        }

        /* ------------------------------------------------------------------ */

        private static void SystemParamTests()
        {
            T.Section("系统双击参数（补丁机制一）");

            int w0 = DoubleClickParams.GetRectWidth();
            int h0 = DoubleClickParams.GetRectHeight();
            int t0 = DoubleClickParams.GetTime();
            T.Info("原始：{0}×{1}px / {2}ms", w0, h0, t0);

            try
            {
                DoubleClickParams.Apply(48, 52, 650);
                T.Eq(48, DoubleClickParams.GetRectWidth(), "双击矩形宽度已实时生效");
                T.Eq(52, DoubleClickParams.GetRectHeight(), "双击矩形高度已实时生效");
                T.Eq(650, DoubleClickParams.GetTime(), "双击时间已实时生效");
                T.Eq(650, DoubleClickParams.GetPersistedTime(), "双击时间已写入注册表（重启后仍有效）");

                AppSettings s = new AppSettings();
                s.SystemDoubleClickWidth = 48;
                s.SystemDoubleClickHeight = 52;
                s.SystemDoubleClickTime = 650;
                s.Normalize();
                T.Eq(48, s.SystemDoubleClickWidth, "设置值合法");
            }
            finally
            {
                DoubleClickParams.Apply(w0, h0, t0);
            }

            T.Eq(w0, DoubleClickParams.GetRectWidth(), "已还原双击矩形宽度");
            T.Eq(h0, DoubleClickParams.GetRectHeight(), "已还原双击矩形高度");
            T.Eq(t0, DoubleClickParams.GetTime(), "已还原双击时间");
        }

        private static void SystemInfoTests()
        {
            T.Section("运行环境");
            T.Check(SystemInfo.VersionText.Length > 0, "系统版本可读", SystemInfo.VersionText);
            T.Info("{0}", SystemInfo.DescribeDigitizer());
            T.Info("虚拟桌面：{0},{1} {2}×{3}", SystemInfo.VirtualScreenLeft, SystemInfo.VirtualScreenTop,
                SystemInfo.VirtualScreenWidth, SystemInfo.VirtualScreenHeight);
            T.Check(SystemInfo.IsPointOnVirtualScreen(0, 0), "原点在虚拟桌面内");
            T.Check(!SystemInfo.IsPointOnVirtualScreen(-100000, -100000), "屏幕外坐标被拒绝");
            T.Info("管理员权限：{0}", SystemInfo.IsElevated ? "是" : "否");
        }

        private static void AutostartTests()
        {
            T.Section("开机自启（注册表 Run 项）");

            string previous = Autostart.CurrentCommand();
            string fakeExe = Path.Combine(Path.GetTempPath(), "Touch+.exe");
            try
            {
                T.Check(Autostart.Enable(fakeExe, true), "写入自启项成功");
                T.Check(Autostart.IsEnabled(), "自启项已存在");
                T.Check(Autostart.PointsTo(fakeExe), "自启项指向正确路径");
                T.Contains(Autostart.CurrentCommand(), "--tray", "默认最小化到托盘启动");
                T.Check(Autostart.Disable(), "删除自启项成功");
                T.Check(!Autostart.IsEnabled(), "自启项已移除");
            }
            finally
            {
                if (string.IsNullOrEmpty(previous)) Autostart.Disable();
                else Autostart.Enable(ExtractPath(previous), previous.Contains("--tray"));
            }
            T.Eq(previous, Autostart.CurrentCommand(), "已还原原自启项");
        }

        private static string ExtractPath(string command)
        {
            if (string.IsNullOrEmpty(command)) return command;
            string c = command.Trim();
            if (c.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = c.IndexOf('"', 1);
                if (end > 1) return c.Substring(1, end - 1);
            }
            int space = c.IndexOf(" --", StringComparison.Ordinal);
            return space > 0 ? c.Substring(0, space) : c;
        }
    }
}
