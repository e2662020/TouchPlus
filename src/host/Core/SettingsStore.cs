using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TouchPlus.Core
{
    /// <summary>排除程序的进程名匹配（支持 * 与 ? 通配）。</summary>
    public static class ProcessRules
    {
        public static string NormalizeRule(string rule)
        {
            if (rule == null) return string.Empty;
            string s = rule.Trim().Trim('"');
            if (s.Length == 0) return string.Empty;
            int slash = s.LastIndexOfAny(new char[] { '\\', '/' });
            if (slash >= 0 && slash < s.Length - 1) s = s.Substring(slash + 1);
            s = s.ToLowerInvariant();
            if (s == "*.exe" || s == "*") s = "*";
            return s;
        }

        public static bool IsExcluded(IEnumerable<string> rules, string processName)
        {
            if (rules == null || string.IsNullOrEmpty(processName)) return false;
            string name = processName.ToLowerInvariant();
            string baseName = name.EndsWith(".exe", StringComparison.Ordinal) ? name.Substring(0, name.Length - 4) : name;

            foreach (string raw in rules)
            {
                string rule = NormalizeRule(raw);
                if (rule.Length == 0) continue;
                string ruleBase = rule.EndsWith(".exe", StringComparison.Ordinal) ? rule.Substring(0, rule.Length - 4) : rule;
                if (rule == name || rule == baseName) return true;
                if (ruleBase == baseName) return true;
                if (rule.IndexOf('*') >= 0 || rule.IndexOf('?') >= 0)
                {
                    if (Wildcard(rule, name) || Wildcard(rule, baseName) || Wildcard(ruleBase, baseName)) return true;
                }
            }
            return false;
        }

        private static bool Wildcard(string pattern, string text)
        {
            int p = 0, t = 0, star = -1, mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t])) { p++; t++; continue; }
                if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; continue; }
                if (star >= 0) { p = star + 1; t = ++mark; continue; }
                return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }

    public enum SettingsLoadState
    {
        Loaded,
        CreatedDefault,
        RecoveredFromBackup,
        FailedUsedDefault
    }

    public sealed class SettingsLoadResult
    {
        public AppSettings Settings;
        public SettingsLoadState State;
        public string Message;
        public string Path;
    }

    /// <summary>设置的持久化：%APPDATA%\TouchPlus\settings.json，原子写入 + 损坏自动回退。</summary>
    public static class SettingsStore
    {
        public static string DataDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
                return Path.Combine(root, "TouchPlus");
            }
        }

        public static string SettingsPath { get { return Path.Combine(DataDirectory, "settings.json"); } }
        public static string BackupPath { get { return BackupPathFor(SettingsPath); } }
        public static string LogDirectory { get { return Path.Combine(DataDirectory, "logs"); } }

        /// <summary>备份文件与主文件同目录同名（只差扩展名），避免多份配置互相覆盖。</summary>
        public static string BackupPathFor(string path)
        {
            return path + ".bak";
        }

        public static void EnsureDirectory()
        {
            try
            {
                if (!Directory.Exists(DataDirectory)) Directory.CreateDirectory(DataDirectory);
            }
            catch (Exception)
            {
                // 目录不可用时静默降级：程序仍能在内存里运行
            }
        }

        public static SettingsLoadResult Load()
        {
            return Load(SettingsPath);
        }

        public static SettingsLoadResult Load(string path)
        {
            SettingsLoadResult result = new SettingsLoadResult();
            result.Path = path;
            result.Settings = new AppSettings();
            result.State = SettingsLoadState.CreatedDefault;

            string text = null;
            if (File.Exists(path))
            {
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch (Exception ex) { text = null; result.Message = "读取失败：" + ex.Message; }
            }

            if (text != null)
            {
                object node;
                string error;
                if (Json.TryParse(text, out node, out error))
                {
                    try
                    {
                        result.Settings = AppSettings.FromJson(node);
                        result.State = SettingsLoadState.Loaded;
                        return result;
                    }
                    catch (Exception ex)
                    {
                        result.Message = "解析失败：" + ex.Message;
                    }
                }
                else
                {
                    result.Message = error;
                }

                // 主文件坏了 → 尝试备份
                try
                {
                    string backup = BackupPathFor(path);
                    if (File.Exists(backup))
                    {
                        object backupNode;
                        string backupError;
                        string backupText = File.ReadAllText(backup, Encoding.UTF8);
                        if (Json.TryParse(backupText, out backupNode, out backupError))
                        {
                            result.Settings = AppSettings.FromJson(backupNode);
                            result.State = SettingsLoadState.RecoveredFromBackup;
                            result.Message = "主配置损坏，已从备份恢复：" + result.Message;
                            return result;
                        }
                    }
                }
                catch (Exception)
                {
                }

                try { File.Copy(path, path + ".bad", true); }
                catch (Exception) { }
                result.Settings = new AppSettings();
                result.State = SettingsLoadState.FailedUsedDefault;
                result.Settings.Normalize();
                return result;
            }

            result.Settings.Normalize();
            return result;
        }

        public static bool Save(AppSettings settings)
        {
            return Save(settings, SettingsPath);
        }

        public static bool Save(AppSettings settings, string path)
        {
            if (settings == null) return false;
            settings.Normalize();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = Json.Write(settings.ToJson(), true) + Environment.NewLine;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    try { File.Copy(path, BackupPathFor(path), true); }
                    catch (Exception) { }
                    try { File.Delete(path); }
                    catch (Exception) { }
                }
                File.Move(tmp, path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
