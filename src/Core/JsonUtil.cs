using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 只使用 JavaScriptSerializer 读取 JSON 数据。禁止按类型名反序列化，避免远程 JSON
    /// 触发任意类型构造。所有远程 JSON 都被当作纯数据。
    /// </summary>
    public static class JsonUtil
    {
        /// <summary>
        /// JSON 文本长度上限。HTTP 元数据另有 16MB 限制，本地配置文件应远小于此；
        /// 32MB 足以容纳现有全部数据，同时避免把超大本地文件整体读入内存。
        /// </summary>
        public const int MaxJsonLength = 32 * 1024 * 1024;

        public static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            // 收紧上限：HTTP 层已限制元数据 16MB，本地配置文件远小于此。
            // 旧值 int.MaxValue（约 2GB）在读取本地 config.json 时没有大小检查。
            serializer.MaxJsonLength = MaxJsonLength;
            serializer.RecursionLimit = 200;
            return serializer.DeserializeObject(json);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            object value = Parse(json);
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null)
            {
                throw new FormatException("JSON 顶层不是对象。");
            }
            return result;
        }

        public static string Serialize(object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 200;
            return serializer.Serialize(value);
        }

        public static IDictionary<string, object> AsObject(object value)
        {
            return value as IDictionary<string, object>;
        }

        public static IList AsList(object value)
        {
            return value as IList;
        }

        public static bool TryGet(IDictionary<string, object> obj, string key, out object value)
        {
            value = null;
            if (obj == null || key == null)
            {
                return false;
            }
            return obj.TryGetValue(key, out value);
        }

        public static string GetString(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
            {
                return "";
            }
            string text = value as string;
            if (text != null)
            {
                return text;
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        public static string GetString(IDictionary<string, object> obj, string key, string fallback)
        {
            string value = GetString(obj, key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        public static bool GetBool(IDictionary<string, object> obj, string key, bool fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            bool parsed;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        public static long GetLong(IDictionary<string, object> obj, string key, long fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static int GetInt(IDictionary<string, object> obj, string key, int fallback)
        {
            long value = GetLong(obj, key, fallback);
            if (value < int.MinValue)
            {
                return int.MinValue;
            }
            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }
            return (int)value;
        }

        public static IDictionary<string, object> GetObject(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value))
            {
                return null;
            }
            return AsObject(value);
        }

        public static IList GetArray(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value))
            {
                return null;
            }
            return AsList(value);
        }

        public static string ToJson(object value)
        {
            return Serialize(value);
        }
    }
}
