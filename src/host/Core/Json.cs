using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TouchPlus.Core
{
    /// <summary>
    /// 极简 JSON 读写器（只支持本程序需要的子集：对象/数组/字符串/数字/布尔/null）。
    /// 自己实现是为了：配置可读可手改、格式化输出美观、零外部依赖、Win7 上行为一致。
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("输入为空");
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new JsonException("JSON 尾部有多余字符，位置 " + i);
            return v;
        }

        public static bool TryParse(string text, out object value, out string error)
        {
            value = null;
            error = null;
            try { value = Parse(text); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static string Write(object value, bool pretty)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        /* ------------------------------------------------------------------ */

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new JsonException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true"); return true;
                case 'f':
                    Expect(s, ref i, "false"); return false;
                case 'n':
                    Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new JsonException("位置 " + i + " 处期望 " + word);
            i += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new JsonException("位置 " + i + " 处期望字段名");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new JsonException("位置 " + i + " 处期望 ':'");
                i++;
                object val = ParseValue(s, ref i);
                map[key] = val;
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonException("对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new JsonException("位置 " + i + " 处期望 ',' 或 '}'");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonException("数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new JsonException("位置 " + i + " 处期望 ',' 或 ']'");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= s.Length) throw new JsonException("字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new JsonException("转义未完成");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new JsonException("\\u 转义不完整");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new JsonException("未知转义 \\" + e);
                }
            }
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') { i++; continue; }
                break;
            }
            string raw = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonException("位置 " + start + " 处不是合法数字：'" + raw + "'");
            return d;
        }

        /* ------------------------------------------------------------------ */

        private static void WriteValue(StringBuilder sb, object value, bool pretty, int indent)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }
            if (value is IDictionary<string, object>)
            {
                WriteObject(sb, (IDictionary<string, object>)value, pretty, indent);
                return;
            }
            if (value is System.Collections.IEnumerable && !(value is string))
            {
                WriteArray(sb, (System.Collections.IEnumerable)value, pretty, indent);
                return;
            }
            double d;
            if (value is double) d = (double)value;
            else if (value is float) d = (float)value;
            else if (value is int) d = (int)value;
            else if (value is long) d = (long)value;
            else if (value is uint) d = (uint)value;
            else
            {
                d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map, bool pretty, int indent)
        {
            if (map.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in map)
            {
                if (!first) sb.Append(',');
                if (pretty) { sb.Append('\n'); Indent(sb, indent + 1); }
                WriteString(sb, kv.Key);
                sb.Append(pretty ? ": " : ":");
                WriteValue(sb, kv.Value, pretty, indent + 1);
                first = false;
            }
            if (pretty) { sb.Append('\n'); Indent(sb, indent); }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, System.Collections.IEnumerable items, bool pretty, int indent)
        {
            List<object> list = new List<object>();
            foreach (object o in items) list.Add(o);
            if (list.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty) { sb.Append('\n'); Indent(sb, indent + 1); }
                WriteValue(sb, list[i], pretty, indent + 1);
            }
            if (pretty) { sb.Append('\n'); Indent(sb, indent); }
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int n)
        {
            for (int i = 0; i < n; i++) sb.Append("  ");
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /* ---------------- 取值助手 ---------------- */

        public static Dictionary<string, object> Obj(object node)
        {
            return node as Dictionary<string, object>;
        }

        public static object Get(Dictionary<string, object> o, string key)
        {
            if (o == null) return null;
            object v;
            return o.TryGetValue(key, out v) ? v : null;
        }

        public static string GetString(Dictionary<string, object> o, string key, string fallback)
        {
            object v = Get(o, key);
            return v is string ? (string)v : fallback;
        }

        public static bool GetBool(Dictionary<string, object> o, string key, bool fallback)
        {
            object v = Get(o, key);
            if (v is bool) return (bool)v;
            if (v is double) return ((double)v) != 0;
            if (v is string)
            {
                bool b;
                if (bool.TryParse((string)v, out b)) return b;
            }
            return fallback;
        }

        public static int GetInt(Dictionary<string, object> o, string key, int fallback)
        {
            object v = Get(o, key);
            if (v is double) return (int)Math.Round((double)v);
            if (v is bool) return ((bool)v) ? 1 : 0;
            if (v is string)
            {
                int i;
                if (int.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            }
            return fallback;
        }

        public static long GetLong(Dictionary<string, object> o, string key, long fallback)
        {
            object v = Get(o, key);
            if (v is double) return (long)Math.Round((double)v);
            if (v is string)
            {
                long i;
                if (long.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            }
            return fallback;
        }

        public static List<object> Array(Dictionary<string, object> o, string key)
        {
            object v = Get(o, key);
            List<object> list = v as List<object>;
            return list ?? new List<object>();
        }

        public static List<string> StringArray(Dictionary<string, object> o, string key)
        {
            List<string> result = new List<string>();
            foreach (object item in Array(o, key))
            {
                string s = item as string;
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
            return result;
        }
    }

    public class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}
