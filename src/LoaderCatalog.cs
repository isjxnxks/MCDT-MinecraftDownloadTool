using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MCDT
{
    public static class LoaderCatalog
    {
        private const string Bmcl = "https://bmclapi2.bangbang93.com";
        private static readonly Regex NumericVersion = new Regex("^[0-9]+(\\.[0-9]+)*$", RegexOptions.Compiled);

        public static async Task<List<LoaderVersionInfo>> GetLoaderVersionsAsync(
            LoaderKind kind, GameVersion game, Action<string> log, CancellationToken cancellationToken)
        {
            switch (kind)
            {
                case LoaderKind.Fabric:
                    return await GetFabricVersionsAsync(game, log, cancellationToken).ConfigureAwait(false);
                case LoaderKind.Quilt:
                    return await GetQuiltVersionsAsync(game, log, cancellationToken).ConfigureAwait(false);
                case LoaderKind.Forge:
                    return await GetForgeVersionsAsync(game, log, cancellationToken).ConfigureAwait(false);
                case LoaderKind.NeoForge:
                    return await GetNeoForgeVersionsAsync(game, log, cancellationToken).ConfigureAwait(false);
                default:
                    return new List<LoaderVersionInfo>();
            }
        }

        private static async Task<List<LoaderVersionInfo>> GetFabricVersionsAsync(
            GameVersion game, Action<string> log, CancellationToken cancellationToken)
        {
            string encodedGame = Uri.EscapeDataString(game.Id);
            List<string> urls = new List<string>
            {
                Bmcl + "/fabric-meta/v2/versions/loader/" + encodedGame,
                "https://meta.fabricmc.net/v2/versions/loader/" + encodedGame
            };
            string json = await HttpUtil.GetStringAsync(urls, log, cancellationToken).ConfigureAwait(false);
            object[] items = JsonUtil.AsArray(JsonUtil.Deserialize(json));

            List<LoaderVersionInfo> stable = new List<LoaderVersionInfo>();
            List<LoaderVersionInfo> unstable = new List<LoaderVersionInfo>();
            for (int i = 0; i < items.Length; i++)
            {
                Dictionary<string, object> item = items[i] as Dictionary<string, object>;
                if (item == null) continue;
                Dictionary<string, object> loader = JsonUtil.GetObject(item, "loader");
                string version = JsonUtil.GetString(loader, "version");
                if (string.IsNullOrEmpty(version))
                    continue;
                LoaderVersionInfo info = new LoaderVersionInfo
                {
                    Version = version,
                    Stable = JsonUtil.GetBool(loader, "stable", false)
                };
                if (info.Stable) stable.Add(info); else unstable.Add(info);
            }

            SortDescending(stable);
            SortDescending(unstable);
            List<LoaderVersionInfo> result = new List<LoaderVersionInfo>();
            result.AddRange(stable);
            result.AddRange(unstable);
            return result;
        }

        private static async Task<List<LoaderVersionInfo>> GetQuiltVersionsAsync(
            GameVersion game, Action<string> log, CancellationToken cancellationToken)
        {
            string encodedGame = Uri.EscapeDataString(game.Id);
            List<string> urls = new List<string>
            {
                Bmcl + "/quilt-meta/v3/versions/loader/" + encodedGame,
                "https://meta.quiltmc.org/v3/versions/loader/" + encodedGame
            };
            string json = await HttpUtil.GetStringAsync(urls, log, cancellationToken).ConfigureAwait(false);
            object[] items = JsonUtil.AsArray(JsonUtil.Deserialize(json));

            List<LoaderVersionInfo> stable = new List<LoaderVersionInfo>();
            List<LoaderVersionInfo> unstable = new List<LoaderVersionInfo>();
            for (int i = 0; i < items.Length; i++)
            {
                Dictionary<string, object> item = items[i] as Dictionary<string, object>;
                if (item == null) continue;
                Dictionary<string, object> loader = JsonUtil.GetObject(item, "loader");
                string version = JsonUtil.GetString(loader, "version");
                if (string.IsNullOrEmpty(version))
                    continue;
                LoaderVersionInfo info = new LoaderVersionInfo
                {
                    Version = version,
                    Stable = !LooksPrerelease(version)
                };
                if (info.Stable) stable.Add(info); else unstable.Add(info);
            }

            SortDescending(stable);
            SortDescending(unstable);
            List<LoaderVersionInfo> result = new List<LoaderVersionInfo>();
            result.AddRange(stable);
            result.AddRange(unstable);
            return result;
        }

        private static async Task<List<LoaderVersionInfo>> GetForgeVersionsAsync(
            GameVersion game, Action<string> log, CancellationToken cancellationToken)
        {
            string encodedGame = Uri.EscapeDataString(game.Id);
            List<string> versions = new List<string>();

            try
            {
                List<string> urls = new List<string> { Bmcl + "/forge/minecraft/" + encodedGame };
                string json = await HttpUtil.GetStringAsync(urls, log, cancellationToken).ConfigureAwait(false);
                object[] items = JsonUtil.AsArray(JsonUtil.Deserialize(json));
                for (int i = 0; i < items.Length; i++)
                {
                    Dictionary<string, object> item = items[i] as Dictionary<string, object>;
                    string version = JsonUtil.GetString(item, "version");
                    if (!string.IsNullOrEmpty(version))
                        versions.Add(version);
                }
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException) throw;
                HttpUtil.SafeLog(log, "镜像源获取 Forge 版本失败，尝试官方源…");
            }

            if (versions.Count == 0)
            {
                try
                {
                    string officialMeta = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
                    XDocument doc = XDocument.Parse(await HttpUtil.GetStringAsync(
                        new List<string> { officialMeta }, log, cancellationToken).ConfigureAwait(false));
                    string prefix = game.Id + "-";
                    foreach (XElement element in doc.Descendants("version"))
                    {
                        string full = element.Value;
                        if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            versions.Add(full.Substring(prefix.Length));
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException) throw;
                    HttpUtil.SafeLog(log, "官方 Maven 版本列表获取失败，改用 Forge 推荐版本…");
                }
            }

            if (versions.Count == 0)
            {
                try
                {
                    string promoUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
                    string json = await HttpUtil.GetStringAsync(new List<string> { promoUrl }, log, cancellationToken).ConfigureAwait(false);
                    Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
                    Dictionary<string, object> promos = JsonUtil.GetObject(root, "promos");
                    if (promos != null)
                    {
                        string recommendedKey = game.Id + "-recommended";
                        string latestKey = game.Id + "-latest";
                        if (promos.ContainsKey(recommendedKey))
                            versions.Add(Convert.ToString(promos[recommendedKey], CultureInfo.InvariantCulture));
                        if (promos.ContainsKey(latestKey))
                            versions.Add(Convert.ToString(promos[latestKey], CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException) throw;
                    HttpUtil.SafeLog(log, "获取 Forge 版本列表失败：" + ex.Message);
                }
            }

            List<LoaderVersionInfo> result = new List<LoaderVersionInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < versions.Count; i++)
            {
                string version = versions[i].Trim();
                if (version.Length == 0 || !seen.Add(version))
                    continue;
                result.Add(new LoaderVersionInfo { Version = version, Stable = true });
            }
            result.Sort(delegate(LoaderVersionInfo a, LoaderVersionInfo b) { return CompareVersions(b.Version, a.Version); });
            if (result.Count > 0)
                result[0].Latest = true;
            return result;
        }

        private static async Task<List<LoaderVersionInfo>> GetNeoForgeVersionsAsync(
            GameVersion game, Action<string> log, CancellationToken cancellationToken)
        {
            if (string.Equals(game.Id, "1.20.1", StringComparison.Ordinal))
                return await GetNeoForgeLegacyVersionsAsync(game, log, cancellationToken).ConfigureAwait(false);

            string prefix = GetNeoForgePrefix(game.Id);
            if (prefix == null)
                return new List<LoaderVersionInfo>();

            List<string> urls = new List<string>
            {
                Bmcl + "/maven/net/neoforged/neoforge/maven-metadata.xml",
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"
            };
            string xml = await HttpUtil.GetStringAsync(urls, log, cancellationToken).ConfigureAwait(false);
            XDocument doc = XDocument.Parse(xml);
            List<LoaderVersionInfo> result = new List<LoaderVersionInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement element in doc.Descendants("version"))
            {
                string version = element.Value.Trim();
                if (!version.StartsWith(prefix, StringComparison.Ordinal) || !NumericVersion.IsMatch(version))
                    continue;
                if (!seen.Add(version))
                    continue;
                result.Add(new LoaderVersionInfo { Version = version, Stable = true });
            }
            result.Sort(delegate(LoaderVersionInfo a, LoaderVersionInfo b) { return CompareVersions(b.Version, a.Version); });
            if (result.Count > 0)
                result[0].Latest = true;
            return result;
        }

        private static async Task<List<LoaderVersionInfo>> GetNeoForgeLegacyVersionsAsync(
            GameVersion game, Action<string> log, CancellationToken cancellationToken)
        {
            List<string> urls = new List<string>
            {
                Bmcl + "/maven/net/neoforged/forge/maven-metadata.xml",
                "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml"
            };
            string xml = await HttpUtil.GetStringAsync(urls, log, cancellationToken).ConfigureAwait(false);
            XDocument doc = XDocument.Parse(xml);
            string prefix = game.Id + "-";
            List<LoaderVersionInfo> result = new List<LoaderVersionInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement element in doc.Descendants("version"))
            {
                string full = element.Value.Trim();
                if (!full.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                string version = full.Substring(prefix.Length);
                if (!NumericVersion.IsMatch(version) || !seen.Add(version))
                    continue;
                result.Add(new LoaderVersionInfo { Version = version, Stable = true });
            }
            result.Sort(delegate(LoaderVersionInfo a, LoaderVersionInfo b) { return CompareVersions(b.Version, a.Version); });
            if (result.Count > 0)
                result[0].Latest = true;
            return result;
        }

        public static string GetNeoForgePrefix(string minecraftVersion)
        {
            if (string.IsNullOrEmpty(minecraftVersion))
                return null;

            if (!minecraftVersion.StartsWith("1.", StringComparison.Ordinal))
                return minecraftVersion + ".";

            string rest = minecraftVersion.Substring(2);
            if (rest.IndexOf('.') < 0)
                return rest + ".0.";
            return rest + ".";
        }

        public static List<string> GetFabricProfileUrls(string gameVersion, string loaderVersion)
        {
            string suffix = "/v2/versions/loader/" + Uri.EscapeDataString(gameVersion) + "/" +
                            Uri.EscapeDataString(loaderVersion) + "/profile/json";
            return new List<string>
            {
                Bmcl + "/fabric-meta" + suffix,
                "https://meta.fabricmc.net" + suffix
            };
        }

        public static List<string> GetQuiltProfileUrls(string gameVersion, string loaderVersion)
        {
            string suffix = "/v3/versions/loader/" + Uri.EscapeDataString(gameVersion) + "/" +
                            Uri.EscapeDataString(loaderVersion) + "/profile/json";
            return new List<string>
            {
                Bmcl + "/quilt-meta" + suffix,
                "https://meta.quiltmc.org" + suffix
            };
        }

        public static List<string> GetForgeInstallerUrls(string gameVersion, string forgeVersion)
        {
            string artifact = gameVersion + "-" + forgeVersion;
            string file = "forge-" + artifact + "-installer.jar";
            string path = "net/minecraftforge/forge/" + artifact + "/" + file;
            return new List<string>
            {
                Bmcl + "/maven/" + path,
                "https://maven.minecraftforge.net/" + path
            };
        }

        public static List<string> GetNeoForgeInstallerUrls(string gameVersion, string loaderVersion)
        {
            string file;
            string path;
            if (string.Equals(gameVersion, "1.20.1", StringComparison.Ordinal))
            {
                string artifact = gameVersion + "-" + loaderVersion;
                file = "forge-" + artifact + "-installer.jar";
                path = "net/neoforged/forge/" + artifact + "/" + file;
            }
            else
            {
                file = "neoforge-" + loaderVersion + "-installer.jar";
                path = "net/neoforged/neoforge/" + loaderVersion + "/" + file;
            }
            return new List<string>
            {
                Bmcl + "/maven/" + path,
                "https://maven.neoforged.net/releases/" + path
            };
        }

        public static string GetDefaultMavenForLoader(LoaderKind kind)
        {
            return kind == LoaderKind.Quilt ? "https://maven.quiltmc.org/repository/release/" : "https://maven.fabricmc.net/";
        }

        private static bool LooksPrerelease(string version)
        {
            if (string.IsNullOrEmpty(version)) return true;
            string lower = version.ToLowerInvariant();
            return lower.Contains("alpha") || lower.Contains("beta") || lower.Contains("snapshot") ||
                   lower.Contains("pre") || lower.Contains("rc");
        }

        private static void SortDescending(List<LoaderVersionInfo> items)
        {
            items.Sort(delegate(LoaderVersionInfo a, LoaderVersionInfo b) { return CompareVersions(b.Version, a.Version); });
        }

        public static int CompareVersions(string left, string right)
        {
            string[] a = Regex.Split(left ?? "", "[^0-9]+");
            string[] b = Regex.Split(right ?? "", "[^0-9]+");
            int count = Math.Max(a.Length, b.Length);
            for (int i = 0; i < count; i++)
            {
                long av = 0;
                long bv = 0;
                if (i < a.Length) long.TryParse(a[i], out av);
                if (i < b.Length) long.TryParse(b[i], out bv);
                int compare = av.CompareTo(bv);
                if (compare != 0)
                    return compare;
            }
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
