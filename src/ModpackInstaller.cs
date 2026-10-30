using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MCDT
{
    public sealed class ModpackArchiveInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string GameVersion { get; set; }
        public string LoaderName { get; set; }
        public string LoaderVersion { get; set; }
        public string FormatName { get; set; }
        public bool IsCurseForge { get; set; }
    }

    public static class ModpackInstaller
    {
        public static async Task<ModpackInstallResult> InstallAsync(string gameDir, ContentProject project,
            ContentVersion version, IList<GameVersion> knownVersions,
            Action<string> log, Action<string> stage, DownloadStats stats, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                throw new InstallException("游戏目录不存在。");
            if (version == null)
                throw new InstallException("没有选择整合包版本。");

            string packUrl = version.DownloadUrl;
            if (string.IsNullOrEmpty(packUrl) && version.Source == ContentSource.CurseForge)
            {
                CurseForgeFile file = await ContentService.GetCurseForgeFileAsync(
                    version.ProjectId, version.Id, log, cancellationToken).ConfigureAwait(false);
                packUrl = file.DownloadUrl;
                if (string.IsNullOrEmpty(version.FileName))
                    version.FileName = file.FileName;
                if (version.Size <= 0)
                    version.Size = file.Size;
                if (string.IsNullOrEmpty(version.Sha1))
                    version.Sha1 = file.Sha1;
            }
            if (string.IsNullOrEmpty(packUrl))
                throw new InstallException("这个整合包没有可用的下载地址。");

            string tempDir = Path.Combine(Path.GetTempPath(), "MineCraftDownloadTool");
            Directory.CreateDirectory(tempDir);
            string extension = ".zip";
            if (version.FileName != null && version.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                extension = ".mrpack";
            string archivePath = Path.Combine(tempDir, "pack-" + Guid.NewGuid().ToString("N") + extension);

            try
            {
                stage("正在下载整合包文件…");
                stats.Indeterminate = false;
                List<DownloadItem> archiveItems = new List<DownloadItem>();
                DownloadItem archiveItem = new DownloadItem();
                archiveItem.Name = "整合包 · " + (project.Title ?? "modpack");
                archiveItem.DestPath = archivePath;
                archiveItem.Urls = new List<string> { packUrl };
                archiveItem.Size = version.Size;
                archiveItem.Sha1 = version.Sha1;
                archiveItems.Add(archiveItem);
                DownloadEngine archiveEngine = new DownloadEngine(log, stats, 1);
                await archiveEngine.RunAsync(archiveItems, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
                    throw new InstallException("整合包文件下载不完整，请重试。");

                return await InstallArchiveAsync(gameDir, project, archivePath, knownVersions,
                    log, stage, stats, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (File.Exists(archivePath))
                        File.Delete(archivePath);
                }
                catch
                {
                }
                stats.Indeterminate = false;
            }
        }

        public static async Task<ModpackInstallResult> ImportAsync(string gameDir, string archivePath,
            string displayName, IList<GameVersion> knownVersions, Action<string> log, Action<string> stage,
            DownloadStats stats, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                throw new InstallException("游戏目录不存在。");
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                throw new InstallException("找不到要导入的整合包文件。");

            string name = displayName == null ? "" : displayName.Trim();
            if (name.Length == 0)
                name = Path.GetFileNameWithoutExtension(archivePath);
            if (name.Length == 0)
                name = "本地整合包";

            ContentProject project = new ContentProject();
            project.Source = ContentSource.Modrinth;
            project.Kind = ContentKind.Modpack;
            project.Id = "local";
            project.Slug = name;
            project.Title = name;
            project.Author = "本地导入";

            try
            {
                log("开始导入本地整合包：" + archivePath);
                return await InstallArchiveAsync(gameDir, project, archivePath, knownVersions,
                    log, stage, stats, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                stats.Indeterminate = false;
            }
        }

        public static ModpackArchiveInfo InspectArchive(string archivePath)
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                throw new InstallException("找不到要导入的整合包文件。");
            PackPlan plan = ReadPlan(archivePath, delegate { });
            ModpackArchiveInfo info = new ModpackArchiveInfo();
            info.Name = plan.PackName;
            if (string.IsNullOrEmpty(info.Name))
                info.Name = Path.GetFileNameWithoutExtension(archivePath);
            info.Version = plan.PackVersion;
            info.GameVersion = plan.GameVersion;
            info.LoaderName = plan.Loader == LoaderKind.Vanilla ? "原版" : plan.Loader.DisplayName();
            info.LoaderVersion = plan.LoaderVersion;
            info.FormatName = plan.Format == PackFormat.Modrinth ? "Modrinth (.mrpack)" : "CurseForge (.zip)";
            info.IsCurseForge = plan.Format == PackFormat.CurseForge;
            return info;
        }

        private static async Task<ModpackInstallResult> InstallArchiveAsync(string gameDir, ContentProject project,
            string archivePath, IList<GameVersion> knownVersions, Action<string> log, Action<string> stage,
            DownloadStats stats, CancellationToken cancellationToken)
        {
            stats.Indeterminate = true;
            stage("正在解析整合包…");
            PackPlan plan = ReadPlan(archivePath, log);
            GameVersion game = await FindGameVersionAsync(knownVersions, plan.GameVersion, log, cancellationToken).ConfigureAwait(false);
            if (game == null)
                throw new InstallException("整合包需要的 Minecraft 版本 " + plan.GameVersion + " 不在版本列表中。");
            if (!string.IsNullOrEmpty(plan.LoaderVersion) &&
                plan.LoaderVersion.StartsWith(plan.GameVersion + "-", StringComparison.OrdinalIgnoreCase))
            {
                plan.LoaderVersion = plan.LoaderVersion.Substring(plan.GameVersion.Length + 1);
            }

            string packId = AllocatePackId(gameDir, project, plan);
            stage("正在安装整合包所需的游戏与加载器…");
            log("整合包游戏版本：" + plan.GameVersion + (plan.Loader == LoaderKind.Vanilla
                ? "（原版）"
                : " + " + plan.Loader.DisplayName() + " " + plan.LoaderVersion));
            MinecraftInstaller installer = new MinecraftInstaller(log, stage);
            InstallResult loaderResult = await installer.InstallAsync(new InstallOptions
            {
                GameDir = gameDir,
                Game = game,
                Loader = plan.Loader,
                LoaderVersion = plan.LoaderVersion,
                TargetVersionId = packId
            }, cancellationToken).ConfigureAwait(false);
            if (loaderResult == null || string.IsNullOrEmpty(loaderResult.InstalledVersionId))
                throw new InstallException("游戏或加载器安装失败。");
            if (!string.Equals(loaderResult.InstalledVersionId, packId, StringComparison.OrdinalIgnoreCase))
                throw new InstallException("整合包实例未能正确生成，请重试。");

            string packDir = loaderResult.VersionDirectory;
            Directory.CreateDirectory(packDir);
            log("整合包实例目录：" + packDir);

            stage("正在解压整合包配置…");
            ExtractOverrides(archivePath, plan, packDir, log);

            List<DownloadItem> items = new List<DownloadItem>();
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plan.Format == PackFormat.Modrinth)
            {
                for (int i = 0; i < plan.ModrinthFiles.Count; i++)
                {
                    PackFile file = plan.ModrinthFiles[i];
                    if (!file.Required)
                        continue;
                    string dest = PathUtil.SafeChild(packDir, file.Path);
                    if (paths.Contains(dest))
                        continue;
                    paths.Add(dest);
                    DownloadItem item = new DownloadItem();
                    item.Name = Path.GetFileName(file.Path);
                    item.DestPath = dest;
                    item.Sha1 = file.Sha1;
                    item.Size = file.Size;
                    item.Urls = new List<string> { file.Url };
                    items.Add(item);
                }
            }
            else
            {
                for (int i = 0; i < plan.CurseForgeFiles.Count; i++)
                {
                    CurseForgePackFile file = plan.CurseForgeFiles[i];
                    if (!file.Required)
                        continue;
                    CurseForgeFile detail = await ContentService.GetCurseForgeFileAsync(
                        file.ProjectId, file.FileId, log, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(detail.DownloadUrl) || string.IsNullOrEmpty(detail.FileName))
                    {
                        log("跳过无法获取下载地址的整合包文件：" + file.ProjectId + "/" + file.FileId);
                        continue;
                    }
                    string dest = PathUtil.SafeChild(packDir, Path.Combine("mods", SanitizeFileName(detail.FileName)));
                    if (paths.Contains(dest))
                        continue;
                    paths.Add(dest);
                    DownloadItem item = new DownloadItem();
                    item.Name = detail.FileName;
                    item.DestPath = dest;
                    item.Sha1 = detail.Sha1;
                    item.Size = detail.Size;
                    item.Urls = new List<string> { detail.DownloadUrl };
                    items.Add(item);
                }
            }

            if (items.Count > 0)
            {
                stage("正在下载整合包内容，共 " + items.Count + " 个文件…");
                DownloadEngine engine = new DownloadEngine(log, stats, 8);
                await engine.RunAsync(items, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                log("整合包没有需要额外下载的文件。");
            }

            stage("正在生成启动实例…");
            TryWriteLauncherProfile(gameDir, packId, project.Title, packDir, log);

            ModpackInstallResult result = new ModpackInstallResult();
            result.VersionId = packId;
            result.VersionDirectory = packDir;
            result.LoaderVersionId = plan.LoaderVersion;
            stage("整合包安装完成");
            log("整合包已安装为独立版本：" + packId);
            log("启动器中的版本目录：" + packDir);
            return result;
        }

        private static PackPlan ReadPlan(string archivePath, Action<string> log)
        {
            PackPlan plan = new PackPlan();
            using (FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry indexEntry = archive.GetEntry("modrinth.index.json");
                if (indexEntry != null)
                {
                    plan.Format = PackFormat.Modrinth;
                    Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(ReadEntry(indexEntry)));
                    plan.PackName = JsonUtil.GetString(root, "name");
                    plan.PackVersion = JsonUtil.GetString(root, "versionId");
                    Dictionary<string, object> dependencies = JsonUtil.GetObject(root, "dependencies");
                    plan.GameVersion = JsonUtil.GetString(dependencies, "minecraft");
                    plan.LoaderVersion = JsonUtil.GetString(dependencies, "fabric-loader");
                    if (!string.IsNullOrEmpty(plan.LoaderVersion))
                        plan.Loader = LoaderKind.Fabric;
                    if (string.IsNullOrEmpty(plan.LoaderVersion))
                    {
                        plan.LoaderVersion = JsonUtil.GetString(dependencies, "quilt-loader");
                        if (!string.IsNullOrEmpty(plan.LoaderVersion))
                            plan.Loader = LoaderKind.Quilt;
                    }
                    if (string.IsNullOrEmpty(plan.LoaderVersion))
                    {
                        plan.LoaderVersion = JsonUtil.GetString(dependencies, "forge");
                        if (!string.IsNullOrEmpty(plan.LoaderVersion))
                            plan.Loader = LoaderKind.Forge;
                    }
                    if (string.IsNullOrEmpty(plan.LoaderVersion))
                    {
                        plan.LoaderVersion = JsonUtil.GetString(dependencies, "neoforge");
                        if (!string.IsNullOrEmpty(plan.LoaderVersion))
                            plan.Loader = LoaderKind.NeoForge;
                    }

                    bool hasClientOverrides = false;
                    object[] files = JsonUtil.GetArray(root, "files");
                    if (files != null)
                    {
                        for (int i = 0; i < files.Length; i++)
                        {
                            Dictionary<string, object> item = files[i] as Dictionary<string, object>;
                            if (item == null)
                                continue;
                            PackFile file = new PackFile();
                            file.Path = JsonUtil.GetString(item, "path");
                            file.Size = JsonUtil.GetLong(item, "fileSize", 0);
                            Dictionary<string, object> hashes = JsonUtil.GetObject(item, "hashes");
                            file.Sha1 = JsonUtil.GetString(hashes, "sha1");
                            string[] downloads = JsonUtil.GetStringList(item, "downloads").ToArray();
                            file.Url = downloads.Length > 0 ? downloads[0] : null;
                            Dictionary<string, object> env = JsonUtil.GetObject(item, "env");
                            string client = JsonUtil.GetString(env, "client", "required");
                            file.Required = !string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase) &&
                                            !string.IsNullOrEmpty(file.Url) && !string.IsNullOrEmpty(file.Path);
                            if (file.Required)
                                plan.ModrinthFiles.Add(file);
                        }
                    }
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase))
                        {
                            hasClientOverrides = true;
                            break;
                        }
                    }
                    plan.HasClientOverrides = hasClientOverrides;
                }
                else
                {
                    ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json");
                    if (manifestEntry == null)
                        throw new InstallException("无法识别这个整合包：缺少 modrinth.index.json 或 manifest.json。");
                    plan.Format = PackFormat.CurseForge;
                    Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(ReadEntry(manifestEntry)));
                    plan.PackName = JsonUtil.GetString(root, "name");
                    plan.PackVersion = JsonUtil.GetString(root, "version");
                    Dictionary<string, object> minecraft = JsonUtil.GetObject(root, "minecraft");
                    plan.GameVersion = JsonUtil.GetString(minecraft, "version");
                    plan.OverridesFolder = JsonUtil.GetString(root, "overrides", "overrides");

                    object[] loaders = JsonUtil.GetArray(minecraft, "modLoaders");
                    Dictionary<string, object> primary = null;
                    if (loaders != null)
                    {
                        for (int i = 0; i < loaders.Length; i++)
                        {
                            Dictionary<string, object> candidate = loaders[i] as Dictionary<string, object>;
                            if (candidate == null)
                                continue;
                            if (primary == null)
                                primary = candidate;
                            if (JsonUtil.GetBool(candidate, "primary", false))
                            {
                                primary = candidate;
                                break;
                            }
                        }
                    }
                    if (primary != null)
                    {
                        string id = JsonUtil.GetString(primary, "id");
                        if (!string.IsNullOrEmpty(id))
                            ParseCurseForgeLoader(id, out plan.Loader, out plan.LoaderVersion);
                    }

                    object[] files = JsonUtil.GetArray(root, "files");
                    if (files != null)
                    {
                        for (int i = 0; i < files.Length; i++)
                        {
                            Dictionary<string, object> item = files[i] as Dictionary<string, object>;
                            if (item == null)
                                continue;
                            CurseForgePackFile file = new CurseForgePackFile();
                            file.ProjectId = Convert.ToString(JsonUtil.GetValue(item, "projectID"), CultureInfo.InvariantCulture);
                            file.FileId = Convert.ToString(JsonUtil.GetValue(item, "fileID"), CultureInfo.InvariantCulture);
                            file.Required = JsonUtil.GetBool(item, "required", false);
                            if (!string.IsNullOrEmpty(file.ProjectId) && !string.IsNullOrEmpty(file.FileId))
                                plan.CurseForgeFiles.Add(file);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(plan.GameVersion))
                throw new InstallException("整合包没有写明需要的 Minecraft 版本。");
            if (string.IsNullOrEmpty(plan.LoaderVersion))
                plan.Loader = LoaderKind.Vanilla;
            log("整合包格式：" + (plan.Format == PackFormat.Modrinth ? "Modrinth (.mrpack)" : "CurseForge (.zip)"));
            return plan;
        }

        private static void ExtractOverrides(string archivePath, PackPlan plan, string packDir, Action<string> log)
        {
            using (FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                if (plan.Format == PackFormat.Modrinth)
                {
                    ExtractPrefix(archive, "overrides/", packDir);
                    if (plan.HasClientOverrides)
                        ExtractPrefix(archive, "client-overrides/", packDir);
                }
                else
                {
                    string prefix = (plan.OverridesFolder ?? "overrides").Trim('/') + "/";
                    ExtractPrefix(archive, prefix, packDir);
                }
            }
            log("已解压整合包配置文件。");
        }

        private static void ExtractPrefix(ZipArchive archive, string prefix, string packDir)
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                string relative = name.Substring(prefix.Length);
                if (string.IsNullOrEmpty(relative) || name.EndsWith("/", StringComparison.Ordinal))
                    continue;
                string dest = PathUtil.SafeChild(packDir, relative);
                PathUtil.EnsureParent(dest);
                using (Stream input = entry.Open())
                using (FileStream output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }
            }
        }

        private static async Task<GameVersion> FindGameVersionAsync(IList<GameVersion> known, string id,
            Action<string> log, CancellationToken cancellationToken)
        {
            if (known != null)
            {
                for (int i = 0; i < known.Count; i++)
                {
                    if (string.Equals(known[i].Id, id, StringComparison.OrdinalIgnoreCase))
                        return known[i];
                }
            }
            try
            {
                List<GameVersion> versions = await VersionService.GetGameVersionsAsync(log, cancellationToken).ConfigureAwait(false);
                for (int i = 0; i < versions.Count; i++)
                {
                    if (string.Equals(versions[i].Id, id, StringComparison.OrdinalIgnoreCase))
                        return versions[i];
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
            return null;
        }

        private static string AllocatePackId(string gameDir, ContentProject project, PackPlan plan)
        {
            string baseId = SanitizeVersionId(project.Slug);
            if (string.IsNullOrEmpty(baseId))
                baseId = SanitizeVersionId(project.Title);
            if (string.IsNullOrEmpty(baseId))
                baseId = "modpack-" + project.Id;
            if (baseId.Length > 50)
                baseId = baseId.Substring(0, 50).TrimEnd('-', ' ', '.');

            string versionsRoot = Path.Combine(gameDir, "versions");
            string candidate = baseId;
            int suffix = 2;
            while (true)
            {
                bool reserved = !string.IsNullOrEmpty(plan.GameVersion) &&
                                string.Equals(candidate, plan.GameVersion, StringComparison.OrdinalIgnoreCase);
                string dir = Path.Combine(versionsRoot, candidate);
                bool occupied = true;
                try
                {
                    occupied = Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0;
                }
                catch
                {
                }
                if (!reserved && !occupied)
                    return candidate;
                candidate = baseId + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }
        }

        private static string SanitizeVersionId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '+' || c == '-' || c == ' ';
                builder.Append(ok ? c : '-');
            }
            return builder.ToString().Trim().Trim('.');
        }

        private static void TryWriteLauncherProfile(string gameDir, string packId, string title, string packDir, Action<string> log)
        {
            string path = Path.Combine(gameDir, "launcher_profiles.json");
            try
            {
                Dictionary<string, object> root;
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(existing.Trim()))
                    {
                        try
                        {
                            root = JsonUtil.AsObject(serializer.DeserializeObject(existing));
                        }
                        catch
                        {
                            log("launcher_profiles.json 格式无法识别，已跳过启动器配置写入。");
                            return;
                        }
                    }
                    else
                    {
                        root = new Dictionary<string, object>();
                    }
                }
                else
                {
                    root = new Dictionary<string, object>();
                }

                Dictionary<string, object> profiles = JsonUtil.GetObject(root, "profiles");
                if (profiles == null)
                {
                    profiles = new Dictionary<string, object>();
                    root["profiles"] = profiles;
                }
                if (!root.ContainsKey("settings"))
                    root["settings"] = new Dictionary<string, object>();
                if (!root.ContainsKey("version"))
                    root["version"] = 3;

                string now = DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z";
                Dictionary<string, object> profile = new Dictionary<string, object>();
                profile["name"] = string.IsNullOrEmpty(title) ? packId : title;
                profile["type"] = "custom";
                profile["created"] = now;
                profile["lastUsed"] = now;
                profile["lastVersionId"] = packId;
                profile["gameDir"] = packDir;
                profiles[packId] = profile;

                PathUtil.WriteAllTextAtomic(path, serializer.Serialize(root));
                log("已写入启动器配置：" + path);
            }
            catch (Exception ex)
            {
                log("写入启动器配置失败（不影响文件安装）：" + ex.Message);
            }
        }

        private static void ParseCurseForgeLoader(string id, out LoaderKind kind, out string version)
        {
            kind = LoaderKind.Vanilla;
            version = null;
            if (string.IsNullOrEmpty(id))
                return;
            int dash = id.IndexOf('-');
            if (dash <= 0 || dash + 1 >= id.Length)
                return;
            string prefix = id.Substring(0, dash);
            version = id.Substring(dash + 1);
            if (string.Equals(prefix, "forge", StringComparison.OrdinalIgnoreCase))
                kind = LoaderKind.Forge;
            else if (string.Equals(prefix, "fabric", StringComparison.OrdinalIgnoreCase))
                kind = LoaderKind.Fabric;
            else if (string.Equals(prefix, "quilt", StringComparison.OrdinalIgnoreCase))
                kind = LoaderKind.Quilt;
            else if (string.Equals(prefix, "neoforge", StringComparison.OrdinalIgnoreCase))
                kind = LoaderKind.NeoForge;
            else
                version = null;
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using (Stream stream = entry.Open())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "download.bin";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j])
                    {
                        bad = true;
                        break;
                    }
                }
                builder.Append(bad ? '_' : c);
            }
            string result = builder.ToString().Trim().TrimEnd('.');
            return result.Length == 0 ? "download.bin" : Path.GetFileName(result);
        }

        private enum PackFormat
        {
            Modrinth,
            CurseForge
        }

        private sealed class PackFile
        {
            public string Path;
            public string Url;
            public string Sha1;
            public long Size;
            public bool Required;
        }

        private sealed class CurseForgePackFile
        {
            public string ProjectId;
            public string FileId;
            public bool Required;
        }

        private sealed class PackPlan
        {
            public PackFormat Format;
            public LoaderKind Loader;
            public string LoaderVersion;
            public string GameVersion;
            public string OverridesFolder;
            public string PackName;
            public string PackVersion;
            public bool HasClientOverrides;
            public List<PackFile> ModrinthFiles = new List<PackFile>();
            public List<CurseForgePackFile> CurseForgeFiles = new List<CurseForgePackFile>();
        }
    }
}
