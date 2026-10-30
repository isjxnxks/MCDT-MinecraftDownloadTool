using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MCDT
{
    public static class AppSettings
    {
        private static readonly object _sync = new object();
        private static string _curseForgeApiKey = "";
        private static bool _versionIsolation;
        private static bool _autoCloseAfterDownload;
        private static bool _darkTheme;
        private static string _themeColor = "#3C8527";
        private static bool _loaded;

        public static string CurseForgeApiKey
        {
            get
            {
                EnsureLoaded();
                lock (_sync)
                    return _curseForgeApiKey;
            }
            set
            {
                EnsureLoaded();
                lock (_sync)
                    _curseForgeApiKey = (value ?? "").Trim();
            }
        }

        public static bool HasCurseForgeKey
        {
            get { return CurseForgeApiKey.Length > 0; }
        }

        public static bool VersionIsolation
        {
            get
            {
                EnsureLoaded();
                lock (_sync)
                    return _versionIsolation;
            }
            set
            {
                EnsureLoaded();
                lock (_sync)
                    _versionIsolation = value;
            }
        }

        public static bool AutoCloseAfterDownload
        {
            get
            {
                EnsureLoaded();
                lock (_sync)
                    return _autoCloseAfterDownload;
            }
            set
            {
                EnsureLoaded();
                lock (_sync)
                    _autoCloseAfterDownload = value;
            }
        }

        public static bool DarkTheme
        {
            get
            {
                EnsureLoaded();
                lock (_sync)
                    return _darkTheme;
            }
            set
            {
                EnsureLoaded();
                lock (_sync)
                    _darkTheme = value;
            }
        }

        public static string ThemeColor
        {
            get
            {
                EnsureLoaded();
                lock (_sync)
                    return _themeColor;
            }
            set
            {
                EnsureLoaded();
                lock (_sync)
                    _themeColor = string.IsNullOrEmpty(value) ? "#3C8527" : value.Trim();
            }
        }

        public static string SettingsPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(appData, "MineCraftDownloadTool"), "settings.json");
            }
        }

        public static void Save()
        {
            EnsureLoaded();
            Dictionary<string, object> root = new Dictionary<string, object>();
            lock (_sync)
            {
                root["curseForgeApiKey"] = _curseForgeApiKey;
                root["versionIsolation"] = _versionIsolation;
                root["autoCloseAfterDownload"] = _autoCloseAfterDownload;
                root["darkTheme"] = _darkTheme;
                root["themeColor"] = _themeColor;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(root);
            PathUtil.WriteAllTextAtomic(SettingsPath, json);
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            lock (_sync)
            {
                if (_loaded)
                    return;
                _loaded = true;
                try
                {
                    string path = SettingsPath;
                    if (!File.Exists(path))
                        return;
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    if (string.IsNullOrEmpty(json))
                        return;
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                    if (root == null)
                        return;
                    object value;
                    if (root.TryGetValue("curseForgeApiKey", out value) && value != null)
                        _curseForgeApiKey = Convert.ToString(value).Trim();
                    if (root.TryGetValue("versionIsolation", out value) && value != null)
                        _versionIsolation = Convert.ToBoolean(value);
                    if (root.TryGetValue("autoCloseAfterDownload", out value) && value != null)
                        _autoCloseAfterDownload = Convert.ToBoolean(value);
                    if (root.TryGetValue("darkTheme", out value) && value != null)
                        _darkTheme = Convert.ToBoolean(value);
                    if (root.TryGetValue("themeColor", out value) && value != null)
                        _themeColor = Convert.ToString(value).Trim();
                }
                catch
                {
                }
            }
        }
    }
}
