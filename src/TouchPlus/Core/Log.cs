using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TouchPlus.Core
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Repair = 2,
        Warn = 3,
        Error = 4
    }

    public sealed class LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public string Message;

        public string TimeText
        {
            get { return Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture); }
        }

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Debug: return "调试";
                    case LogLevel.Info: return "信息";
                    case LogLevel.Repair: return "修复";
                    case LogLevel.Warn: return "警告";
                    default: return "错误";
                }
            }
        }
    }

    /// <summary>内存日志 + 可选文件落盘。写入方与 UI 同在一条消息线程上，锁开销可忽略。</summary>
    public sealed class Log
    {
        public static readonly Log Instance = new Log();

        private const int MaxEntries = 3000;
        private const long MaxFileBytes = 2L * 1024 * 1024;

        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private readonly object _sync = new object();
        private string _filePath;
        private bool _fileEnabled;
        private long _writtenSinceRotate;

        public bool Verbose = false;
        public long DroppedEntries;

        public event EventHandler<LogEntry> EntryAdded;

        private Log() { }

        public void ConfigureFile(string logDirectory, bool enabled, bool verbose)
        {
            lock (_sync)
            {
                Verbose = verbose;
                _fileEnabled = enabled;
                if (enabled && !string.IsNullOrEmpty(logDirectory))
                {
                    try
                    {
                        if (!Directory.Exists(logDirectory)) Directory.CreateDirectory(logDirectory);
                        _filePath = Path.Combine(logDirectory,
                            "touchplus-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                        PruneOldFiles(logDirectory, 10);
                    }
                    catch (Exception)
                    {
                        _fileEnabled = false;
                        _filePath = null;
                    }
                }
                else
                {
                    _filePath = null;
                }
            }
        }

        public bool FileEnabled { get { lock (_sync) { return _fileEnabled; } } }

        public string FilePath { get { lock (_sync) { return _filePath; } } }

        public void Debug(string format, params object[] args) { Write(LogLevel.Debug, Format(format, args)); }
        public void Info(string format, params object[] args) { Write(LogLevel.Info, Format(format, args)); }
        public void Repair(string format, params object[] args) { Write(LogLevel.Repair, Format(format, args)); }
        public void Warn(string format, params object[] args) { Write(LogLevel.Warn, Format(format, args)); }
        public void Error(string format, params object[] args) { Write(LogLevel.Error, Format(format, args)); }

        public void Error(Exception ex, string context)
        {
            Write(LogLevel.Error, context + "：" + (ex == null ? "未知异常" : ex.GetType().Name + " - " + ex.Message));
        }

        private static string Format(string format, object[] args)
        {
            if (args == null || args.Length == 0) return format;
            try { return string.Format(CultureInfo.InvariantCulture, format, args); }
            catch (Exception) { return format; }
        }

        public void Write(LogLevel level, string message)
        {
            if (level == LogLevel.Debug && !Verbose) return;

            LogEntry entry = new LogEntry();
            entry.Time = DateTime.Now;
            entry.Level = level;
            entry.Message = message;

            lock (_sync)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries)
                {
                    int remove = _entries.Count - MaxEntries;
                    _entries.RemoveRange(0, remove);
                    DroppedEntries += remove;
                }
                if (_fileEnabled && _filePath != null) AppendFile(entry);
            }

            EventHandler<LogEntry> handler = EntryAdded;
            if (handler != null)
            {
                try { handler(this, entry); }
                catch (Exception) { }
            }
        }

        private void AppendFile(LogEntry entry)
        {
            try
            {
                string line = string.Format(CultureInfo.InvariantCulture, "{0} [{1}] {2}{3}",
                    entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    entry.LevelText, entry.Message, Environment.NewLine);
                byte[] bytes = Encoding.UTF8.GetBytes(line);

                _writtenSinceRotate += bytes.Length;
                if (_writtenSinceRotate > MaxFileBytes)
                {
                    _writtenSinceRotate = 0;
                    string rotated = _filePath + "." + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
                    try { if (File.Exists(_filePath)) File.Move(_filePath, rotated); }
                    catch (Exception) { }
                }

                using (FileStream fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception)
            {
                _fileEnabled = false;
            }
        }

        private static void PruneOldFiles(string dir, int keep)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "touchplus-*.log*");
                if (files.Length <= keep) return;
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length - keep; i++)
                {
                    try { File.Delete(files[i]); }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        public LogEntry[] Snapshot()
        {
            lock (_sync) { return _entries.ToArray(); }
        }

        public int Count { get { lock (_sync) { return _entries.Count; } } }

        public void Clear()
        {
            lock (_sync) { _entries.Clear(); }
        }

        public bool ExportTo(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Touch+ 诊断日志导出");
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine(new string('-', 72));
                LogEntry[] snapshot = Snapshot();
                foreach (LogEntry e in snapshot)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} [{1}] {2}",
                        e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), e.LevelText, e.Message));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
