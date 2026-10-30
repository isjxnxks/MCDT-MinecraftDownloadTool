using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace MCDT
{
    public static class JsonUtil
    {
        public static object Deserialize(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 512;
            return serializer.DeserializeObject(json);
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            Dictionary<string, object> dict = value as Dictionary<string, object>;
            if (dict == null)
                throw new FormatException("服务端返回了无法识别的 JSON 结构。");
            return dict;
        }

        public static object[] AsArray(object value)
        {
            object[] array = value as object[];
            if (array == null)
                throw new FormatException("服务端返回了无法识别的 JSON 列表。");
            return array;
        }

        public static object GetValue(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key))
                return null;
            return dict[key];
        }

        public static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            return GetValue(dict, key) as Dictionary<string, object>;
        }

        public static object[] GetArray(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return null;
            return value as object[];
        }

        public static string GetString(Dictionary<string, object> dict, string key)
        {
            object value = GetValue(dict, key);
            if (value == null)
                return null;
            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            string value = GetString(dict, key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        public static bool GetBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            object value = GetValue(dict, key);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed))
                return parsed;
            return fallback;
        }

        public static long GetLong(Dictionary<string, object> dict, string key, long fallback)
        {
            object value = GetValue(dict, key);
            if (value == null) return fallback;
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static List<string> GetStringList(Dictionary<string, object> dict, string key)
        {
            List<string> result = new List<string>();
            object[] array = GetArray(dict, key);
            if (array == null)
                return result;
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null)
                    continue;
                string value = array[i] as string;
                if (value == null)
                    value = Convert.ToString(array[i], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(value))
                    result.Add(value);
            }
            return result;
        }

        public static List<Dictionary<string, object>> GetObjectList(Dictionary<string, object> dict, string key)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            object[] array = GetArray(dict, key);
            if (array == null)
                return result;
            for (int i = 0; i < array.Length; i++)
            {
                Dictionary<string, object> item = array[i] as Dictionary<string, object>;
                if (item != null)
                    result.Add(item);
            }
            return result;
        }

        public static DateTime ParseTime(string value)
        {
            DateTime result;
            if (!string.IsNullOrEmpty(value) &&
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out result))
                return result;
            return DateTime.MinValue;
        }
    }
}
