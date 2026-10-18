using System;
using System.Collections.Generic;
using System.Globalization;

namespace TouchPlus.Core
{
    public enum RepairStrategy
    {
        /// <summary>坐标对齐：把第二次点击的位置对齐到锚点，由系统自己判定双击。不注入任何输入。</summary>
        Reposition = 0,
        /// <summary>合成双击：拦截第二次点击，在锚点合成一次完整双击。可处理间隔超时的慢速双击。</summary>
        Synthesize = 1
    }

    public enum AnchorMode
    {
        First = 0,
        Second = 1,
        Midpoint = 2
    }

    public enum SourceMode
    {
        /// <summary>自动：优先处理触摸/笔输入；若系统有数字化仪但一直没出现触摸签名，则回退为处理全部输入。</summary>
        Auto = 0,
        TouchPenOnly = 1,
        All = 2
    }

    /// <summary>
    /// 全部可持久化设置。字段保持简单类型，便于 JSON 读写与向后兼容。
    /// </summary>
    public sealed class AppSettings
    {
        public const int SchemaVersion = 1;

        /// <summary>运行时标志：--safe 模式下一律不写回配置文件（不参与序列化）。</summary>
        public bool ReadOnlySession = false;

        /// <summary>外观：Auto（跟随 Windows）/ Light / Dark。</summary>
        public string ThemeMode = "Auto";

        /// <summary>用户添加的外部插件包路径（.dll）。</summary>
        public List<string> PluginPaths = new List<string>();

        /// <summary>配置版本号：用于把旧版本（或验证工具留下）的参数迁移到新默认值。</summary>
        public int ConfigVersion = 0;

        /* ---- 补丁总开关 ---- */
        public bool PatchEnabled = true;

        /* ---- 补丁机制一：系统层（放宽系统双击矩形与时间窗） ---- */
        // 默认 50px / 800ms：实测 40px 已能让 12~38px 的抖动被系统原生识别，
        // 触控面板手指抖动更大，所以默认再放宽一档；界面上可 4~200px 自由调整。
        public bool SystemLayerEnabled = true;
        public int SystemDoubleClickWidth = 50;
        public int SystemDoubleClickHeight = 50;
        public int SystemDoubleClickTime = 800;

        /* ---- 补丁机制二：输入层（低级鼠标钩子纠正抖动点击） ---- */
        // 默认关闭：机制一已验证可在系统层原生解决，钩子只在极端情况下才需要。
        public bool HookLayerEnabled = false;
        public int Tolerance = 80;
        public int TimeWindow = 1000;
        public RepairStrategy Strategy = RepairStrategy.Synthesize;
        public AnchorMode Anchor = AnchorMode.Second;
        public SourceMode Source = SourceMode.Auto;
        public bool RequireSameWindow = true;
        public int DragResetPx = 16;
        public bool EmulateLongPress = true;
        public int LongPressGuardMs = 450;

        /* ---- 排除程序（进程名，支持 * 通配） ---- */
        public List<string> ExcludedProcesses = new List<string>();

        /* ---- 行为 ---- */
        public bool AutoStart = false;
        public bool StartMinimized = true;
        public bool ShowTrayTips = true;
        public bool VerboseLog = false;
        public bool HotkeyEnabled = true;
        public bool ExitRestoreSystem = false;
        public bool FirstRun = true;

        /* ---- 统计（跨会话累计） ---- */
        public long TotalRepairs = 0;
        public long TotalPairs = 0;

        /* ---- 系统原值备份，用于"还原系统默认" ---- */
        public bool OriginalsCaptured = false;
        public int OriginalDoubleClickWidth = 4;
        public int OriginalDoubleClickHeight = 4;
        public int OriginalDoubleClickTime = 500;

        public AppSettings Clone()
        {
            AppSettings c = (AppSettings)MemberwiseClone();
            c.ExcludedProcesses = new List<string>(ExcludedProcesses);
            c.PluginPaths = new List<string>(PluginPaths);
            return c;
        }

        /// <summary>把越界/非法值收敛到安全范围。任何时候读入配置都会调用。</summary>
        public void Normalize()
        {
            SystemDoubleClickWidth = Clamp(SystemDoubleClickWidth, 2, 200);
            SystemDoubleClickHeight = Clamp(SystemDoubleClickHeight, 2, 200);
            SystemDoubleClickTime = Clamp(SystemDoubleClickTime, 200, 2000);
            Tolerance = Clamp(Tolerance, 4, 200);
            TimeWindow = Clamp(TimeWindow, 150, 2500);
            DragResetPx = Clamp(DragResetPx, 2, 80);
            LongPressGuardMs = Clamp(LongPressGuardMs, 120, 1500);
            if (Strategy != RepairStrategy.Reposition && Strategy != RepairStrategy.Synthesize) Strategy = RepairStrategy.Reposition;
            if (Anchor != AnchorMode.First && Anchor != AnchorMode.Second && Anchor != AnchorMode.Midpoint) Anchor = AnchorMode.First;
            if (Source != SourceMode.Auto && Source != SourceMode.TouchPenOnly && Source != SourceMode.All) Source = SourceMode.Auto;

            List<string> cleaned = new List<string>();
            if (ExcludedProcesses != null)
            {
                foreach (string p in ExcludedProcesses)
                {
                    string s = ProcessRules.NormalizeRule(p);
                    if (s.Length > 0 && !cleaned.Contains(s)) cleaned.Add(s);
                }
            }
            if (cleaned.Count > 200) cleaned.RemoveRange(200, cleaned.Count - 200);
            ExcludedProcesses = cleaned;

            if (PluginPaths == null) PluginPaths = new List<string>();
            if (string.IsNullOrEmpty(ThemeMode)) ThemeMode = "Auto";
            if (ThemeMode != "Auto" && ThemeMode != "Light" && ThemeMode != "Dark") ThemeMode = "Auto";
            if (TotalRepairs < 0) TotalRepairs = 0;
            if (TotalPairs < 0) TotalPairs = 0;
        }

        public static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /* ---------------- JSON ---------------- */

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["schema"] = SchemaVersion;
            o["configVersion"] = ConfigVersion;
            o["themeMode"] = ThemeMode;
            o["pluginPaths"] = new List<object>(PluginPaths.ToArray());
            o["patchEnabled"] = PatchEnabled;
            o["systemLayerEnabled"] = SystemLayerEnabled;
            o["systemDoubleClickWidth"] = SystemDoubleClickWidth;
            o["systemDoubleClickHeight"] = SystemDoubleClickHeight;
            o["systemDoubleClickTime"] = SystemDoubleClickTime;
            o["hookLayerEnabled"] = HookLayerEnabled;
            o["tolerance"] = Tolerance;
            o["timeWindow"] = TimeWindow;
            o["strategy"] = Strategy.ToString();
            o["anchor"] = Anchor.ToString();
            o["source"] = Source.ToString();
            o["requireSameWindow"] = RequireSameWindow;
            o["dragResetPx"] = DragResetPx;
            o["emulateLongPress"] = EmulateLongPress;
            o["longPressGuardMs"] = LongPressGuardMs;
            o["excludedProcesses"] = new List<object>(ExcludedProcesses.ToArray());
            o["autoStart"] = AutoStart;
            o["startMinimized"] = StartMinimized;
            o["showTrayTips"] = ShowTrayTips;
            o["verboseLog"] = VerboseLog;
            o["hotkeyEnabled"] = HotkeyEnabled;
            o["exitRestoreSystem"] = ExitRestoreSystem;
            o["firstRun"] = FirstRun;
            o["totalRepairs"] = TotalRepairs;
            o["totalPairs"] = TotalPairs;
            o["originalsCaptured"] = OriginalsCaptured;
            o["originalDoubleClickWidth"] = OriginalDoubleClickWidth;
            o["originalDoubleClickHeight"] = OriginalDoubleClickHeight;
            o["originalDoubleClickTime"] = OriginalDoubleClickTime;
            return o;
        }

        public static AppSettings FromJson(object node)
        {
            AppSettings s = new AppSettings();
            Dictionary<string, object> o = Json.Obj(node);
            if (o == null) return s;

            s.ConfigVersion = Json.GetInt(o, "configVersion", 0);
            s.ThemeMode = Json.GetString(o, "themeMode", s.ThemeMode);
            s.PluginPaths = Json.StringArray(o, "pluginPaths");
            s.PatchEnabled = Json.GetBool(o, "patchEnabled", s.PatchEnabled);
            s.SystemLayerEnabled = Json.GetBool(o, "systemLayerEnabled", s.SystemLayerEnabled);
            s.SystemDoubleClickWidth = Json.GetInt(o, "systemDoubleClickWidth", s.SystemDoubleClickWidth);
            s.SystemDoubleClickHeight = Json.GetInt(o, "systemDoubleClickHeight", s.SystemDoubleClickHeight);
            s.SystemDoubleClickTime = Json.GetInt(o, "systemDoubleClickTime", s.SystemDoubleClickTime);
            s.HookLayerEnabled = Json.GetBool(o, "hookLayerEnabled", s.HookLayerEnabled);
            s.Tolerance = Json.GetInt(o, "tolerance", s.Tolerance);
            s.TimeWindow = Json.GetInt(o, "timeWindow", s.TimeWindow);
            s.Strategy = ParseEnum<RepairStrategy>(Json.GetString(o, "strategy", null), s.Strategy);
            s.Anchor = ParseEnum<AnchorMode>(Json.GetString(o, "anchor", null), s.Anchor);
            s.Source = ParseEnum<SourceMode>(Json.GetString(o, "source", null), s.Source);
            s.RequireSameWindow = Json.GetBool(o, "requireSameWindow", s.RequireSameWindow);
            s.DragResetPx = Json.GetInt(o, "dragResetPx", s.DragResetPx);
            s.EmulateLongPress = Json.GetBool(o, "emulateLongPress", s.EmulateLongPress);
            s.LongPressGuardMs = Json.GetInt(o, "longPressGuardMs", s.LongPressGuardMs);
            s.ExcludedProcesses = Json.StringArray(o, "excludedProcesses");
            s.AutoStart = Json.GetBool(o, "autoStart", s.AutoStart);
            s.StartMinimized = Json.GetBool(o, "startMinimized", s.StartMinimized);
            s.ShowTrayTips = Json.GetBool(o, "showTrayTips", s.ShowTrayTips);
            s.VerboseLog = Json.GetBool(o, "verboseLog", s.VerboseLog);
            s.HotkeyEnabled = Json.GetBool(o, "hotkeyEnabled", s.HotkeyEnabled);
            s.ExitRestoreSystem = Json.GetBool(o, "exitRestoreSystem", s.ExitRestoreSystem);
            s.FirstRun = Json.GetBool(o, "firstRun", s.FirstRun);
            s.TotalRepairs = Json.GetLong(o, "totalRepairs", s.TotalRepairs);
            s.TotalPairs = Json.GetLong(o, "totalPairs", s.TotalPairs);
            s.OriginalsCaptured = Json.GetBool(o, "originalsCaptured", s.OriginalsCaptured);
            s.OriginalDoubleClickWidth = Json.GetInt(o, "originalDoubleClickWidth", s.OriginalDoubleClickWidth);
            s.OriginalDoubleClickHeight = Json.GetInt(o, "originalDoubleClickHeight", s.OriginalDoubleClickHeight);
            s.OriginalDoubleClickTime = Json.GetInt(o, "originalDoubleClickTime", s.OriginalDoubleClickTime);

            s.Normalize();
            return s;
        }

        private static T ParseEnum<T>(string text, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            try
            {
                T parsed = (T)Enum.Parse(typeof(T), text, true);
                return parsed;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "patch={0} sys={1}({2}x{3},{4}ms) hook={5}(tol={6}px,win={7}ms,{8},{9},{10})",
                PatchEnabled, SystemLayerEnabled, SystemDoubleClickWidth, SystemDoubleClickHeight, SystemDoubleClickTime,
                HookLayerEnabled, Tolerance, TimeWindow, Strategy, Anchor, Source);
        }
    }
}
