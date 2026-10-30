using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MCDT
{
    public sealed class MinecraftInstaller
    {
        private const string Bmcl = "https://bmclapi2.bangbang93.com";
        private static readonly Regex SafeIdRegex = new Regex("^[A-Za-z0-9._+\\- ]+$", RegexOptions.Compiled);
        private static readonly Regex SafePackIdRegex = new Regex(@"^[\p{L}\p{Nd}._+\- ]+$", RegexOptions.Compiled);

        private readonly Action<string> _log;
        private readonly Action<string> _stage;
        private readonly DownloadStats _stats = new DownloadStats();
        private int _parallelism = 12;

        public MinecraftInstaller(Action<string> log, Action<string> stage)
        {
            _log = log;
            _stage = stage;
        }

        public DownloadStats Stats
        {
            get { return _stats; }
        }

        public async Task<InstallResult> InstallAsync(InstallOptions options, CancellationToken cancellationToken)
        {
            if (options == null || options.Game == null)
                throw new InstallException("没有选择要下载的游戏版本。");
            if (!Directory.Exists(options.GameDir))
                Directory.CreateDirectory(options.GameDir);

            SetStage("正在读取版本信息…");
            _stats.Indeterminate = true;

            List<string> versionUrls = SourceMapper.MirrorThenOfficial(options.Game.Url);
            string versionJsonText = await HttpUtil.GetStringAsync(versionUrls, _log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> versionJson = JsonUtil.AsObject(JsonUtil.Deserialize(versionJsonText));
            string baseId = JsonUtil.GetString(versionJson, "id", options.Game.Id);
            EnsureSafeId(baseId);

            string versionsRoot = Path.Combine(options.GameDir, "versions");
            string baseDir = PathUtil.SafeChild(versionsRoot, baseId);
            string baseJsonPath = Path.Combine(baseDir, baseId + ".json");
            bool baseVersionExisted = HasUserVersionFiles(baseDir);
            string targetId = options.TargetVersionId;
            if (!string.IsNullOrEmpty(targetId))
            {
                EnsureSafePackId(targetId);
                if (string.Equals(targetId, baseId, StringComparison.OrdinalIgnoreCase))
                    throw new InstallException("整合包名称不能与游戏版本名称相同。");
            }
            HashSet<string> versionDirsBefore = SnapshotVersionDirectories(versionsRoot);
            if (baseVersionExisted && IsUsableBaseVersionJson(baseJsonPath, baseId))
                Log("检测到原版 " + baseId + " 已存在，已保留其版本文件。");
            else
                PathUtil.WriteAllTextAtomic(baseJsonPath, versionJsonText);

            Dictionary<string, object> clientDownload = JsonUtil.GetObject(JsonUtil.GetObject(versionJson, "downloads"), "client");
            if (clientDownload == null)
                throw new InstallException("这个版本没有可下载的客户端文件（可能是服务端专用版本）。");

            JavaInfo java = null;
            int preferredJava = GetPreferredJavaMajor(versionJson);
            if (options.Loader.NeedsJavaInstaller())
            {
                SetStage("正在查找 Java…");
                java = await Task.Run(delegate
                {
                    return JavaLocator.FindBest(options.GameDir, preferredJava, _log);
                }, cancellationToken).ConfigureAwait(false);

                if (java == null)
                {
                    throw new InstallException(
                        "没有检测到 Java。Forge / NeoForge 的官方安装器需要 Java 才能运行。\n\n" +
                        "请先安装 Java " + preferredJava + " 或更高版本；也可以把 java.exe 放到工具目录后重试。" +
                        "如果只想安装模组加载器，可改用 Fabric 或 Quilt（无需 Java）。");
                }
                if (java.Major < preferredJava)
                {
                    throw new InstallException(
                        "检测到的 Java 版本过低（当前 Java " + java.Major + "，此游戏版本需要 Java " +
                        preferredJava + " 或更高版本）。请安装新版 Java 后重试。");
                }
                Log("安装器将使用：" + java.ToString());
            }

            SetStage("正在获取资源索引…");
            List<DownloadItem> items = new List<DownloadItem>();
            HashSet<string> itemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddItem(items, itemPaths, new DownloadItem
            {
                Name = "客户端核心",
                DestPath = Path.Combine(baseDir, baseId + ".jar"),
                Sha1 = JsonUtil.GetString(clientDownload, "sha1"),
                Size = JsonUtil.GetLong(clientDownload, "size", 0),
                Urls = SourceMapper.MirrorThenOfficial(JsonUtil.GetString(clientDownload, "url"))
            });

            AddLibraries(items, itemPaths, options.GameDir, versionJson);

            Dictionary<string, object> logging = JsonUtil.GetObject(versionJson, "logging");
            Dictionary<string, object> clientLogging = logging == null ? null : JsonUtil.GetObject(logging, "client");
            Dictionary<string, object> logFile = clientLogging == null ? null : JsonUtil.GetObject(clientLogging, "file");
            if (logFile != null)
            {
                string logUrl = JsonUtil.GetString(logFile, "url");
                if (!string.IsNullOrEmpty(logUrl))
                {
                    string logId = SanitizeFileName(JsonUtil.GetString(logFile, "id", "client-log.xml"));
                    AddItem(items, itemPaths, new DownloadItem
                    {
                        Name = "日志配置",
                        DestPath = Path.Combine(options.GameDir, "assets", "log_configs", logId),
                        Sha1 = JsonUtil.GetString(logFile, "sha1"),
                        Size = JsonUtil.GetLong(logFile, "size", 0),
                        Urls = OfficialFirstForPistonData(logUrl)
                    });
                }
            }

            Dictionary<string, object> assetIndexMeta = JsonUtil.GetObject(versionJson, "assetIndex");
            if (assetIndexMeta != null)
            {
                string assetIndexUrl = JsonUtil.GetString(assetIndexMeta, "url");
                string assetIndexId = JsonUtil.GetString(assetIndexMeta, "id");
                string assetIndexSha1 = JsonUtil.GetString(assetIndexMeta, "sha1");
                long assetIndexSize = JsonUtil.GetLong(assetIndexMeta, "size", 0);
                if (string.IsNullOrEmpty(assetIndexId))
                    assetIndexId = versionJsonText == null ? "legacy" : JsonUtil.GetString(versionJson, "assets", "legacy");
                EnsureSafeId(assetIndexId);

                byte[] indexBytes = await HttpUtil.GetBytesAsync(
                    SourceMapper.MirrorThenOfficial(assetIndexUrl), _log, cancellationToken).ConfigureAwait(false);
                VerifySha1(indexBytes, assetIndexSha1, "资源索引");
                string indexPath = Path.Combine(options.GameDir, "assets", "indexes", assetIndexId + ".json");
                PathUtil.WriteAllBytesAtomic(indexPath, indexBytes);

                if (!string.IsNullOrEmpty(assetIndexUrl))
                {
                    AddItem(items, itemPaths, new DownloadItem
                    {
                        Name = "资源索引",
                        DestPath = indexPath,
                        Sha1 = assetIndexSha1,
                        Size = assetIndexSize,
                        Urls = SourceMapper.MirrorThenOfficial(assetIndexUrl)
                    });
                }

                Dictionary<string, object> indexRoot = JsonUtil.AsObject(JsonUtil.Deserialize(Encoding.UTF8.GetString(indexBytes)));
                Dictionary<string, object> objects = JsonUtil.GetObject(indexRoot, "objects");
                if (objects != null)
                {
                    int assetCount = 0;
                    foreach (KeyValuePair<string, object> pair in objects)
                    {
                        Dictionary<string, object> obj = pair.Value as Dictionary<string, object>;
                        if (obj == null) continue;
                        string hash = JsonUtil.GetString(obj, "hash");
                        long size = JsonUtil.GetLong(obj, "size", 0);
                        if (string.IsNullOrEmpty(hash) || hash.Length < 4) continue;
                        string rel = Path.Combine("assets", "objects", hash.Substring(0, 2), hash);
                        string dest = Path.Combine(options.GameDir, rel);
                        if (itemPaths.Contains(dest)) continue;
                        string assetUrl = "https://resources.download.minecraft.net/" + hash.Substring(0, 2) + "/" + hash;
                        AddItem(items, itemPaths, new DownloadItem
                        {
                            Name = "资源 " + hash.Substring(0, 8),
                            DestPath = dest,
                            Sha1 = hash,
                            Size = size,
                            Urls = SourceMapper.MirrorThenOfficial(assetUrl)
                        });
                        assetCount++;
                    }
                    Log("资源文件数量：" + assetCount);
                }
            }

            SetStage("正在下载游戏文件，共 " + items.Count + " 个文件…");
            DownloadEngine engine = new DownloadEngine(_log, _stats, _parallelism);
            await engine.RunAsync(items, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(Path.Combine(baseDir, baseId + ".jar")))
                throw new InstallException("客户端核心下载后未找到，请重试。");

            InstallResult result = new InstallResult
            {
                InstalledVersionId = baseId,
                VersionDirectory = baseDir
            };

            if (options.Loader == LoaderKind.Vanilla)
            {
                if (!string.IsNullOrEmpty(targetId))
                    AdoptTargetVersion(options.GameDir, baseId, baseVersionExisted, targetId, versionDirsBefore, result);
                SetStage("下载完成");
                Log("原版 " + baseId + " 已安装到：" + result.VersionDirectory);
                _stats.Indeterminate = false;
                return result;
            }

            if (!string.IsNullOrEmpty(options.LoaderVersion))
                Log("加载器：" + options.Loader.DisplayName() + " " + options.LoaderVersion);

            if (options.Loader == LoaderKind.Fabric || options.Loader == LoaderKind.Quilt)
            {
                result = await InstallFabricLikeAsync(options, baseId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await InstallByForgeInstallerAsync(options, baseId, java, cancellationToken).ConfigureAwait(false);
            }

            MakeLoaderVersionStandalone(options.GameDir, baseId, baseVersionExisted, result);
            if (!string.IsNullOrEmpty(targetId))
                AdoptTargetVersion(options.GameDir, baseId, baseVersionExisted, targetId, versionDirsBefore, result);

            SetStage("安装完成");
            _stats.Indeterminate = false;
            Log(options.Loader.DisplayName() + " 已安装完成：" + result.InstalledVersionId);
            return result;
        }

        private void AddLibraries(List<DownloadItem> items, HashSet<string> itemPaths, string gameDir, Dictionary<string, object> versionJson)
        {
            object[] libraries = JsonUtil.GetArray(versionJson, "libraries");
            if (libraries == null) return;

            for (int i = 0; i < libraries.Length; i++)
            {
                Dictionary<string, object> library = libraries[i] as Dictionary<string, object>;
                if (library == null || !IsLibraryAllowed(library))
                    continue;

                Dictionary<string, object> downloads = JsonUtil.GetObject(library, "downloads");
                Dictionary<string, object> artifact = downloads == null ? null : JsonUtil.GetObject(downloads, "artifact");
                if (artifact != null)
                    AddLibraryArtifact(items, itemPaths, gameDir, artifact);

                Dictionary<string, object> natives = JsonUtil.GetObject(library, "natives");
                if (natives != null && downloads != null)
                {
                    string classifier = GetWindowsClassifier(natives);
                    if (!string.IsNullOrEmpty(classifier))
                    {
                        Dictionary<string, object> classifiers = JsonUtil.GetObject(downloads, "classifiers");
                        Dictionary<string, object> nativeArtifact = classifiers == null ? null : JsonUtil.GetObject(classifiers, classifier);
                        if (nativeArtifact != null)
                            AddLibraryArtifact(items, itemPaths, gameDir, nativeArtifact);
                    }
                }
            }
        }

        private void AddLibraryArtifact(List<DownloadItem> items, HashSet<string> itemPaths, string gameDir, Dictionary<string, object> artifact)
        {
            string path = JsonUtil.GetString(artifact, "path");
            string url = JsonUtil.GetString(artifact, "url");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url))
                return;
            string dest = PathUtil.SafeChild(Path.Combine(gameDir, "libraries"), path);
            AddItem(items, itemPaths, new DownloadItem
            {
                Name = Path.GetFileName(path),
                DestPath = dest,
                Sha1 = JsonUtil.GetString(artifact, "sha1"),
                Size = JsonUtil.GetLong(artifact, "size", 0),
                Urls = SourceMapper.MirrorThenOfficial(url)
            });
        }

        private static bool IsLibraryAllowed(Dictionary<string, object> library)
        {
            object[] rules = JsonUtil.GetArray(library, "rules");
            if (rules == null || rules.Length == 0)
                return true;

            bool allowed = false;
            for (int i = 0; i < rules.Length; i++)
            {
                Dictionary<string, object> rule = rules[i] as Dictionary<string, object>;
                if (rule == null || !RuleMatches(rule))
                    continue;
                string action = JsonUtil.GetString(rule, "action", "allow");
                allowed = action == "allow";
            }
            return allowed;
        }

        private static bool RuleMatches(Dictionary<string, object> rule)
        {
            Dictionary<string, object> os = JsonUtil.GetObject(rule, "os");
            if (os != null)
            {
                string name = JsonUtil.GetString(os, "name");
                if (!string.IsNullOrEmpty(name) && !string.Equals(name, "windows", StringComparison.OrdinalIgnoreCase))
                    return false;
                string arch = JsonUtil.GetString(os, "arch");
                if (!string.IsNullOrEmpty(arch))
                {
                    bool is64 = Environment.Is64BitOperatingSystem;
                    if (string.Equals(arch, "x86", StringComparison.OrdinalIgnoreCase) && is64)
                        return false;
                    if (string.Equals(arch, "x86_64", StringComparison.OrdinalIgnoreCase) && !is64)
                        return false;
                    if (string.Equals(arch, "arm64", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            Dictionary<string, object> features = JsonUtil.GetObject(rule, "features");
            if (features != null && features.Count > 0)
                return false;
            return true;
        }

        private static string GetWindowsClassifier(Dictionary<string, object> natives)
        {
            string[] keys = Environment.Is64BitOperatingSystem
                ? new string[] { "windows-64", "windows" }
                : new string[] { "windows-32", "windows" };
            for (int i = 0; i < keys.Length; i++)
            {
                object value = JsonUtil.GetValue(natives, keys[i]);
                string classifier = value as string;
                if (!string.IsNullOrEmpty(classifier))
                    return classifier;
            }
            return null;
        }

        private async Task<InstallResult> InstallFabricLikeAsync(InstallOptions options, string baseId, CancellationToken cancellationToken)
        {
            bool isFabric = options.Loader == LoaderKind.Fabric;
            SetStage("正在获取 " + options.Loader.DisplayName() + " 配置…");
            _stats.Indeterminate = true;

            List<string> profileUrls = isFabric
                ? LoaderCatalog.GetFabricProfileUrls(options.Game.Id, options.LoaderVersion)
                : LoaderCatalog.GetQuiltProfileUrls(options.Game.Id, options.LoaderVersion);
            string profileText = await HttpUtil.GetStringAsync(profileUrls, _log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> profile = JsonUtil.AsObject(JsonUtil.Deserialize(profileText));
            string profileId = JsonUtil.GetString(profile, "id");
            if (string.IsNullOrEmpty(profileId))
                profileId = (isFabric ? "fabric-loader-" : "quilt-loader-") + options.LoaderVersion + "-" + baseId;
            EnsureSafeId(profileId);

            string versionsRoot = Path.Combine(options.GameDir, "versions");
            string versionDir = PathUtil.SafeChild(versionsRoot, profileId);
            string profilePath = Path.Combine(versionDir, profileId + ".json");
            PathUtil.WriteAllTextAtomic(profilePath, profileText);

            List<DownloadItem> items = new List<DownloadItem>();
            HashSet<string> itemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object[] libraries = JsonUtil.GetArray(profile, "libraries");
            if (libraries != null)
            {
                for (int i = 0; i < libraries.Length; i++)
                {
                    Dictionary<string, object> library = libraries[i] as Dictionary<string, object>;
                    if (library == null) continue;

                    string name = JsonUtil.GetString(library, "name");
                    Dictionary<string, object> downloads = JsonUtil.GetObject(library, "downloads");
                    Dictionary<string, object> artifact = downloads == null ? null : JsonUtil.GetObject(downloads, "artifact");
                    string path = null;
                    string url = null;
                    string sha1 = null;
                    long size = 0;

                    if (artifact != null)
                    {
                        path = JsonUtil.GetString(artifact, "path");
                        url = JsonUtil.GetString(artifact, "url");
                        sha1 = JsonUtil.GetString(artifact, "sha1");
                        size = JsonUtil.GetLong(artifact, "size", 0);
                    }

                    if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(name))
                    {
                        string extension;
                        path = MavenPathFromName(name, out extension);
                    }
                    if (string.IsNullOrEmpty(path))
                        continue;

                    if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        string baseUrl = JsonUtil.GetString(library, "url");
                        if (string.IsNullOrEmpty(baseUrl))
                            baseUrl = LoaderCatalog.GetDefaultMavenForLoader(options.Loader);
                        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                            baseUrl += "/";
                        url = baseUrl + path;
                    }

                    string dest = PathUtil.SafeChild(Path.Combine(options.GameDir, "libraries"), path);
                    AddItem(items, itemPaths, new DownloadItem
                    {
                        Name = Path.GetFileName(path),
                        DestPath = dest,
                        Sha1 = sha1,
                        Size = size,
                        Urls = SourceMapper.MirrorThenOfficial(url)
                    });
                }
            }

            SetStage("正在下载 " + options.Loader.DisplayName() + " 组件…");
            DownloadEngine engine = new DownloadEngine(_log, _stats, _parallelism);
            await engine.RunAsync(items, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(profilePath))
                throw new InstallException("加载器配置写入失败。");
            Log(options.Loader.DisplayName() + " 配置已写入：" + profilePath);
            _stats.Indeterminate = false;
            return new InstallResult { InstalledVersionId = profileId, VersionDirectory = versionDir };
        }

        private async Task<InstallResult> InstallByForgeInstallerAsync(
            InstallOptions options, string baseId, JavaInfo java, CancellationToken cancellationToken)
        {
            if (java == null)
                throw new InstallException("没有可用的 Java，无法运行 Forge / NeoForge 安装器。");

            SetStage("正在下载 " + options.Loader.DisplayName() + " 安装器…");
            _stats.Indeterminate = true;

            List<string> installerUrls = options.Loader == LoaderKind.Forge
                ? LoaderCatalog.GetForgeInstallerUrls(options.Game.Id, options.LoaderVersion)
                : LoaderCatalog.GetNeoForgeInstallerUrls(options.Game.Id, options.LoaderVersion);

            string tempDir = Path.Combine(Path.GetTempPath(), "MCVersionDownloader");
            Directory.CreateDirectory(tempDir);
            string installerPath = Path.Combine(tempDir, "installer-" + Guid.NewGuid().ToString("N") + ".jar");
            string mirroredInstallerPath = null;
            DateTime startTime = DateTime.UtcNow;

            try
            {
                await HttpUtil.DownloadToFileAsync(installerUrls, installerPath, _log, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(installerPath) || new FileInfo(installerPath).Length < 100000)
                    throw new InstallException("安装器文件下载不完整，请重试。");

                EnsureLauncherProfiles(options.GameDir);
                await PreloadInstallerLibrariesAsync(installerPath, options.GameDir, options.Loader, cancellationToken)
                    .ConfigureAwait(false);
                Log("正在运行 " + options.Loader.DisplayName() + " 官方安装器（可能需要几分钟）…");

                try
                {
                    mirroredInstallerPath = CreateMirroredInstaller(installerPath);
                }
                catch (Exception ex)
                {
                    Log("镜像安装器准备失败，将直接使用官方安装器：" + ex.Message);
                }

                ProcessResult first = mirroredInstallerPath != null
                    ? await RunInstallerAsync(java.Path, mirroredInstallerPath, options.GameDir, false, "镜像源",
                        options.Loader, cancellationToken).ConfigureAwait(false)
                    : await RunInstallerAsync(java.Path, installerPath, options.GameDir, true, "镜像源",
                        options.Loader, cancellationToken).ConfigureAwait(false);

                if (!first.Success)
                {
                    Log("镜像安装失败（" + first.Summary + "），正在重试镜像源…");
                    SetStage("镜像安装失败，正在重试镜像源…");
                    ProcessResult retry = mirroredInstallerPath != null
                        ? await RunInstallerAsync(java.Path, mirroredInstallerPath, options.GameDir, false, "镜像源",
                            options.Loader, cancellationToken).ConfigureAwait(false)
                        : await RunInstallerAsync(java.Path, installerPath, options.GameDir, true, "镜像源",
                            options.Loader, cancellationToken).ConfigureAwait(false);

                    if (!retry.Success)
                    {
                        Log("镜像重试失败（" + retry.Summary + "），正在改用官方源重试…");
                        SetStage("镜像重试失败，正在改用官方源重试…");
                        ProcessResult second = await RunInstallerAsync(
                            java.Path, installerPath, options.GameDir, false, "官方源",
                            options.Loader, cancellationToken).ConfigureAwait(false);
                        if (!second.Success)
                            throw new InstallException(
                                options.Loader.DisplayName() + " 安装器执行失败。\n\n" +
                                "退出代码：" + second.ExitCode + "\n" + second.Detail);
                    }
                }

                string installedId = FindInstalledLoaderVersion(
                    options.GameDir, options.Game.Id, options.Loader, options.LoaderVersion, startTime);
                if (string.IsNullOrEmpty(installedId))
                    throw new InstallException("安装器已结束，但没有找到生成的版本文件。请检查磁盘空间与文件权限后重试。");

                _stats.Indeterminate = false;
                return new InstallResult
                {
                    InstalledVersionId = installedId,
                    VersionDirectory = Path.Combine(options.GameDir, "versions", installedId)
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(installerPath))
                        File.Delete(installerPath);
                    if (File.Exists(installerPath + ".log"))
                        File.Delete(installerPath + ".log");
                    if (mirroredInstallerPath != null)
                    {
                        if (File.Exists(mirroredInstallerPath))
                            File.Delete(mirroredInstallerPath);
                        if (File.Exists(mirroredInstallerPath + ".log"))
                            File.Delete(mirroredInstallerPath + ".log");
                    }
                }
                catch
                {
                }
                _stats.Indeterminate = false;
            }
        }

        private static string CreateMirroredInstaller(string installerPath)
        {
            string mirroredPath = installerPath + ".mirror.jar";
            File.Copy(installerPath, mirroredPath, true);
            using (FileStream stream = new FileStream(mirroredPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Update))
            {
                RewriteInstallerJson(archive, "version.json");
                RewriteInstallerJson(archive, "install_profile.json");
            }
            return mirroredPath;
        }

        private static void RewriteInstallerJson(ZipArchive archive, string entryName)
        {
            ZipArchiveEntry entry = archive.GetEntry(entryName);
            if (entry == null)
                return;

            string text;
            using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                text = reader.ReadToEnd();

            string mirrored = MirrorMavenUrls(text);
            if (string.Equals(mirrored, text, StringComparison.Ordinal))
                return;

            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (StreamWriter writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false)))
                writer.Write(mirrored);
        }

        private static string MirrorMavenUrls(string text)
        {
            string[] sources =
            {
                "https://maven.neoforged.net/releases/",
                "https://maven.neoforged.net/snapshots/",
                "https://maven.minecraftforge.net/",
                "https://files.minecraftforge.net/maven/",
                "http://files.minecraftforge.net/maven/",
                "https://libraries.minecraft.net/",
                "http://libraries.minecraft.net/"
            };
            for (int i = 0; i < sources.Length; i++)
                text = text.Replace(sources[i], Bmcl + "/maven/");
            return text;
        }

        private async Task PreloadInstallerLibrariesAsync(string installerPath, string gameDir,
            LoaderKind loader, CancellationToken cancellationToken)
        {
            SetStage("正在准备 " + loader.DisplayName() + " 组件…");
            List<DownloadItem> items = new List<DownloadItem>();
            HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            int ready = 0;

            try
            {
                using (FileStream stream = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    AddInstallerLibraries(archive, "install_profile.json", gameDir, items, destinations,
                        ref total, ref ready);
                    AddInstallerLibraries(archive, "version.json", gameDir, items, destinations,
                        ref total, ref ready);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("读取安装器组件清单失败，将直接运行安装器：" + ex.Message);
                return;
            }

            if (total == 0)
                return;

            Log("安装器组件：共 " + total + " 个，本地已就绪 " + ready + " 个，待下载 " + items.Count + " 个。");
            if (items.Count == 0)
                return;

            try
            {
                DownloadEngine engine = new DownloadEngine(_log, _stats, _parallelism);
                await engine.RunAsync(items, cancellationToken).ConfigureAwait(false);
                Log(loader.DisplayName() + " 安装组件已全部准备完成。");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("部分 " + loader.DisplayName() + " 组件预下载失败，将由安装器继续尝试：" + ex.Message);
            }
        }

        private static void AddInstallerLibraries(ZipArchive archive, string entryName, string gameDir,
            List<DownloadItem> items, HashSet<string> destinations, ref int total, ref int ready)
        {
            ZipArchiveEntry entry = archive.GetEntry(entryName);
            if (entry == null)
                return;

            string text;
            using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                text = reader.ReadToEnd();

            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(text));
            object[] libraries = root == null ? null : JsonUtil.GetArray(root, "libraries");
            if (libraries == null)
                return;

            for (int i = 0; i < libraries.Length; i++)
            {
                Dictionary<string, object> library = libraries[i] as Dictionary<string, object>;
                Dictionary<string, object> downloads = library == null ? null : JsonUtil.GetObject(library, "downloads");
                Dictionary<string, object> artifact = downloads == null ? null : JsonUtil.GetObject(downloads, "artifact");
                if (artifact == null)
                    continue;

                string path = JsonUtil.GetString(artifact, "path");
                string sha1 = JsonUtil.GetString(artifact, "sha1");
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(sha1))
                    continue;

                string destination;
                try
                {
                    destination = PathUtil.SafeChild(Path.Combine(gameDir, "libraries"), path);
                }
                catch
                {
                    continue;
                }
                if (!destinations.Add(destination))
                    continue;

                long size = JsonUtil.GetLong(artifact, "size", 0);
                if (FileMatchesSha1(destination, sha1, size))
                {
                    total++;
                    ready++;
                    continue;
                }

                try
                {
                    if (File.Exists(destination))
                        File.Delete(destination);
                }
                catch
                {
                    continue;
                }

                total++;
                items.Add(new DownloadItem
                {
                    Name = Path.GetFileName(path),
                    DestPath = destination,
                    Sha1 = sha1,
                    Size = size,
                    Urls = BuildInstallerArtifactUrls(path, JsonUtil.GetString(artifact, "url"))
                });
            }
        }

        private static bool FileMatchesSha1(string path, string expectedSha1, long expectedSize)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                    return false;
                if (expectedSize > 0 && info.Length != expectedSize)
                    return false;

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                using (SHA1 sha1 = SHA1.Create())
                {
                    string actual = BitConverter.ToString(sha1.ComputeHash(stream)).Replace("-", "");
                    return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static List<string> BuildInstallerArtifactUrls(string path, string embeddedUrl)
        {
            List<string> urls = new List<string>();
            AddInstallerUrl(urls, Bmcl + "/maven/" + path);
            AddInstallerUrl(urls, "https://maven.minecraftforge.net/" + path);
            AddInstallerUrl(urls, "https://repo1.maven.org/maven2/" + path);
            AddInstallerUrl(urls, "https://libraries.minecraft.net/" + path);
            AddInstallerUrl(urls, "https://maven.neoforged.net/releases/" + path);
            AddInstallerUrl(urls, embeddedUrl);
            return urls;
        }

        private static void AddInstallerUrl(List<string> urls, string url)
        {
            if (string.IsNullOrEmpty(url))
                return;

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return;

            for (int i = 0; i < urls.Count; i++)
            {
                if (string.Equals(urls[i], url, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            urls.Add(url);
        }

        private async Task<ProcessResult> RunInstallerAsync(
            string javaPath, string installerPath, string gameDir, bool addMirrorArgument, string sourceName,
            LoaderKind loader, CancellationToken cancellationToken)
        {
            SetStage("正在运行 " + loader.DisplayName() + " 安装器（" + sourceName + "）…");
            _stats.Indeterminate = true;

            string arguments = "-Dfile.encoding=UTF-8 -jar " + Quote(installerPath) +
                               " --installClient " + Quote(gameDir);
            if (addMirrorArgument)
                arguments += " --mirror " + Bmcl + "/maven/";

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = javaPath;
            info.Arguments = arguments;
            info.WorkingDirectory = Path.GetDirectoryName(installerPath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            ConcurrentLineBuffer buffer = new ConcurrentLineBuffer(80);
            int forwarded = 0;
            DateTime lastStage = DateTime.MinValue;
            object stageLock = new object();

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    buffer.Add(e.Data);
                    string trimmed = e.Data.Trim();
                    if (trimmed.Length == 0) return;
                    bool noisy = trimmed.StartsWith("Patching ", StringComparison.OrdinalIgnoreCase);
                    if (!noisy && Interlocked.Increment(ref forwarded) <= 30)
                    {
                        if (trimmed.StartsWith("Successfully", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Installing", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0)
                            Log(trimmed);
                    }
                    lock (stageLock)
                    {
                        DateTime now = DateTime.UtcNow;
                        if (!noisy && (now - lastStage).TotalMilliseconds > 900)
                        {
                            lastStage = now;
                            SetStage("正在运行 " + loader.DisplayName() + " 安装器：" + TrimForStatus(trimmed, 60));
                        }
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    buffer.Add(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(25));
                CancellationTokenRegistration killRegistration = timeout.Token.Register(delegate
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch
                    {
                    }
                });

                try
                {
                    await Task.Run(delegate { process.WaitForExit(); }).ConfigureAwait(false);
                }
                finally
                {
                    killRegistration.Dispose();
                    timeout.Dispose();
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    string detail = buffer.GetDetail();
                    Log(loader.DisplayName() + " 安装器失败：" + detail);
                    return new ProcessResult
                    {
                        Success = false,
                        ExitCode = process.ExitCode,
                        Detail = detail,
                        Summary = "退出代码 " + process.ExitCode
                    };
                }

                string detailSuccess = buffer.GetDetail();
                if (detailSuccess.IndexOf("Successfully", StringComparison.OrdinalIgnoreCase) < 0)
                    Log("安装器输出：" + TrimForStatus(detailSuccess, 160));
                return new ProcessResult { Success = true, ExitCode = 0, Detail = "", Summary = "成功" };
            }
        }

        private void EnsureLauncherProfiles(string gameDir)
        {
            string path = Path.Combine(gameDir, "launcher_profiles.json");
            if (File.Exists(path))
                return;
            PathUtil.WriteAllTextAtomic(path, "{\"profiles\":{},\"settings\":{},\"version\":3}");
        }

        private string FindInstalledLoaderVersion(
            string gameDir, string baseId, LoaderKind loader, string loaderVersion, DateTime startTime)
        {
            string versionsRoot = Path.Combine(gameDir, "versions");
            List<string> candidates = new List<string>();
            if (loader == LoaderKind.Forge)
            {
                candidates.Add(baseId + "-forge-" + loaderVersion);
                candidates.Add("forge-" + loaderVersion);
            }
            else
            {
                candidates.Add("neoforge-" + loaderVersion);
                candidates.Add(baseId + "-neoforge-" + loaderVersion);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string dir = Path.Combine(versionsRoot, candidates[i]);
                if (File.Exists(Path.Combine(dir, candidates[i] + ".json")))
                    return candidates[i];
            }

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            try
            {
                string[] dirs = Directory.GetDirectories(versionsRoot);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string name = Path.GetFileName(dirs[i]);
                    if (string.Equals(name, baseId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.IndexOf("forge", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    string json = Path.Combine(dirs[i], name + ".json");
                    if (!File.Exists(json))
                        continue;
                    DateTime modified = File.GetLastWriteTimeUtc(json);
                    if (modified < startTime.AddMinutes(-2))
                        continue;
                    if (modified > bestTime)
                    {
                        best = name;
                        bestTime = modified;
                    }
                }
            }
            catch
            {
            }
            return best;
        }

        private static List<string> OfficialFirstForPistonData(string url)
        {
            List<string> result = new List<string>();
            if (url.IndexOf("piston-data.mojang.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(url);
            }
            else
            {
                result.AddRange(SourceMapper.MirrorThenOfficial(url));
            }
            return result;
        }

        private static int GetPreferredJavaMajor(Dictionary<string, object> versionJson)
        {
            Dictionary<string, object> javaVersion = JsonUtil.GetObject(versionJson, "javaVersion");
            if (javaVersion != null)
            {
                int major = (int)JsonUtil.GetLong(javaVersion, "majorVersion", 0);
                if (major > 0) return major;
            }
            return 8;
        }

        private static void AddItem(List<DownloadItem> items, HashSet<string> itemPaths, DownloadItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.DestPath) || item.Urls == null || item.Urls.Count == 0)
                return;
            if (!itemPaths.Add(item.DestPath))
                return;
            items.Add(item);
        }

        private static string MavenPathFromName(string name, out string extension)
        {
            extension = "jar";
            if (string.IsNullOrEmpty(name))
                return null;
            string coordinate = name;
            int at = coordinate.IndexOf('@');
            if (at >= 0)
            {
                extension = coordinate.Substring(at + 1);
                coordinate = coordinate.Substring(0, at);
            }
            string[] parts = coordinate.Split(':');
            if (parts.Length < 3)
                return null;
            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length > 3 ? parts[3] : null;
            string fileName = artifact + "-" + version + (string.IsNullOrEmpty(classifier) ? "" : "-" + classifier) + "." + extension;
            return group.Replace('.', '/') + "/" + artifact + "/" + version + "/" + fileName;
        }

        private static bool HasUserVersionFiles(string versionDirectory)
        {
            try
            {
                return Directory.Exists(versionDirectory) &&
                       Directory.GetFileSystemEntries(versionDirectory).Length > 0;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsUsableBaseVersionJson(string path, string baseId)
        {
            try
            {
                if (!File.Exists(path))
                    return false;
                Dictionary<string, object> json = JsonUtil.AsObject(
                    JsonUtil.Deserialize(File.ReadAllText(path, Encoding.UTF8)));
                if (json == null)
                    return false;
                if (!string.Equals(JsonUtil.GetString(json, "id"), baseId, StringComparison.OrdinalIgnoreCase))
                    return false;
                return !string.IsNullOrEmpty(JsonUtil.GetString(json, "mainClass"));
            }
            catch
            {
                return false;
            }
        }

        private static HashSet<string> SnapshotVersionDirectories(string versionsRoot)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(versionsRoot))
                    return names;
                string[] dirs = Directory.GetDirectories(versionsRoot);
                for (int i = 0; i < dirs.Length; i++)
                    names.Add(Path.GetFileName(dirs[i]));
            }
            catch
            {
            }
            return names;
        }

        private void AdoptTargetVersion(string gameDir, string baseId, bool baseVersionExisted,
            string targetId, HashSet<string> versionDirsBefore, InstallResult result)
        {
            string versionsRoot = Path.Combine(gameDir, "versions");
            string sourceId = result.InstalledVersionId;
            string sourceDir = PathUtil.SafeChild(versionsRoot, sourceId);
            string sourceJsonPath = Path.Combine(sourceDir, sourceId + ".json");
            if (!File.Exists(sourceJsonPath))
                throw new InstallException("未找到安装生成的版本文件，无法创建整合包实例。");

            Dictionary<string, object> json = JsonUtil.AsObject(
                JsonUtil.Deserialize(File.ReadAllText(sourceJsonPath, Encoding.UTF8)));
            if (json == null || string.IsNullOrEmpty(JsonUtil.GetString(json, "mainClass")))
                throw new InstallException("版本信息缺少启动参数，无法创建整合包实例。");
            if (!string.IsNullOrEmpty(JsonUtil.GetString(json, "inheritsFrom")))
                throw new InstallException("加载器版本未能整理为独立版本，无法创建整合包实例。");

            string sourceJar = Path.Combine(sourceDir, sourceId + ".jar");
            if (!File.Exists(sourceJar))
                throw new InstallException("未找到客户端核心文件，无法创建整合包实例。");

            string targetDir = PathUtil.SafeChild(versionsRoot, targetId);
            string targetJsonPath = Path.Combine(targetDir, targetId + ".json");
            if (HasUserVersionFiles(targetDir))
                throw new InstallException("目标版本目录已存在：" + targetId);
            Directory.CreateDirectory(targetDir);

            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                if (string.Equals(name, sourceId + ".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                string destination;
                if (string.Equals(name, sourceId + ".jar", StringComparison.OrdinalIgnoreCase))
                    destination = Path.Combine(targetDir, targetId + ".jar");
                else
                    destination = Path.Combine(targetDir, name);
                File.Copy(files[i], destination, true);
            }

            string[] dirs = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < dirs.Length; i++)
                CopyDirectory(dirs[i], Path.Combine(targetDir, Path.GetFileName(dirs[i])));

            json["id"] = targetId;
            json.Remove("inheritsFrom");
            json.Remove("jar");
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            PathUtil.WriteAllTextAtomic(targetJsonPath, serializer.Serialize(json));

            bool sourceWasTransient = !versionDirsBefore.Contains(sourceId);
            if (sourceWasTransient && !string.Equals(sourceId, baseId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Delete(sourceDir, true);
                    Log("已移除临时的加载器版本目录：" + sourceId);
                }
                catch (Exception ex)
                {
                    Log("临时加载器版本目录未能删除，已保留：" + ex.Message);
                }
            }
            else if (string.Equals(sourceId, baseId, StringComparison.OrdinalIgnoreCase) && !baseVersionExisted)
            {
                try
                {
                    Directory.Delete(sourceDir, true);
                    Log("已移除安装过程中临时创建的原版版本目录：" + sourceId);
                }
                catch (Exception ex)
                {
                    Log("临时原版版本目录未能删除，已保留：" + ex.Message);
                }
            }

            result.InstalledVersionId = targetId;
            result.VersionDirectory = targetDir;
            Log("已创建独立整合包实例：" + targetId);
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
                File.Copy(files[i], Path.Combine(targetDir, Path.GetFileName(files[i])), true);
            string[] dirs = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < dirs.Length; i++)
                CopyDirectory(dirs[i], Path.Combine(targetDir, Path.GetFileName(dirs[i])));
        }

        private void MakeLoaderVersionStandalone(string gameDir, string baseId, bool baseVersionExisted, InstallResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.InstalledVersionId) ||
                string.Equals(result.InstalledVersionId, baseId, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                string versionsRoot = Path.Combine(gameDir, "versions");
                string baseDir = PathUtil.SafeChild(versionsRoot, baseId);
                string baseJsonPath = Path.Combine(baseDir, baseId + ".json");
                string loaderDir = PathUtil.SafeChild(versionsRoot, result.InstalledVersionId);
                string loaderJsonPath = Path.Combine(loaderDir, result.InstalledVersionId + ".json");
                if (!File.Exists(baseJsonPath) || !File.Exists(loaderJsonPath))
                    return;

                Dictionary<string, object> baseJson = JsonUtil.AsObject(
                    JsonUtil.Deserialize(File.ReadAllText(baseJsonPath, Encoding.UTF8)));
                Dictionary<string, object> loaderJson = JsonUtil.AsObject(
                    JsonUtil.Deserialize(File.ReadAllText(loaderJsonPath, Encoding.UTF8)));

                string baseJarPath = Path.Combine(baseDir, baseId + ".jar");
                string loaderJarPath = Path.Combine(loaderDir, result.InstalledVersionId + ".jar");
                if (!File.Exists(loaderJarPath) && File.Exists(baseJarPath))
                    File.Copy(baseJarPath, loaderJarPath, false);
                if (!File.Exists(loaderJarPath))
                {
                    Log("未找到可合并的原版客户端文件，保留原版版本目录。");
                    return;
                }

                Dictionary<string, object> merged = BuildStandaloneVersionJson(baseJson, loaderJson, result.InstalledVersionId);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                PathUtil.WriteAllTextAtomic(loaderJsonPath, serializer.Serialize(merged));
                Log("已将原版运行文件合并进加载器版本：" + result.InstalledVersionId);

                if (!baseVersionExisted)
                {
                    try
                    {
                        Directory.Delete(baseDir, true);
                        Log("已移除安装过程中临时创建的原版版本目录：" + baseId);
                    }
                    catch (Exception ex)
                    {
                        Log("临时原版版本目录未能删除，已保留：" + ex.Message);
                    }
                }
                else
                {
                    Log("检测到原本已安装原版 " + baseId + "，已保留该版本。");
                }
            }
            catch (Exception ex)
            {
                Log("整理自包含加载器版本失败，已保留原版依赖目录：" + ex.Message);
            }
        }

        private static Dictionary<string, object> BuildStandaloneVersionJson(
            Dictionary<string, object> baseJson, Dictionary<string, object> loaderJson, string loaderId)
        {
            Dictionary<string, object> merged = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in baseJson)
            {
                if (string.Equals(pair.Key, "inheritsFrom", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "libraries", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "arguments", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "id", StringComparison.OrdinalIgnoreCase))
                    continue;
                merged[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<string, object> pair in loaderJson)
            {
                if (string.Equals(pair.Key, "inheritsFrom", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "libraries", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, "arguments", StringComparison.OrdinalIgnoreCase) ||
                    pair.Value == null)
                    continue;
                merged[pair.Key] = pair.Value;
            }

            merged["id"] = loaderId;
            merged.Remove("inheritsFrom");
            merged.Remove("jar");

            object[] libraries = MergeLibraries(
                JsonUtil.GetArray(baseJson, "libraries"), JsonUtil.GetArray(loaderJson, "libraries"));
            if (libraries != null)
                merged["libraries"] = libraries;

            Dictionary<string, object> arguments = MergeArguments(
                JsonUtil.GetObject(baseJson, "arguments"), JsonUtil.GetObject(loaderJson, "arguments"));
            if (arguments != null)
                merged["arguments"] = arguments;
            else
                merged.Remove("arguments");
            return merged;
        }

        private static object[] MergeLibraries(object[] baseLibraries, object[] loaderLibraries)
        {
            if (baseLibraries == null || baseLibraries.Length == 0)
                return loaderLibraries;
            if (loaderLibraries == null || loaderLibraries.Length == 0)
                return baseLibraries;

            List<object> merged = new List<object>();
            Dictionary<string, int> positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < baseLibraries.Length; i++)
            {
                string name = LibraryName(baseLibraries[i]);
                if (!string.IsNullOrEmpty(name))
                    positions[name] = merged.Count;
                merged.Add(baseLibraries[i]);
            }
            for (int i = 0; i < loaderLibraries.Length; i++)
            {
                string name = LibraryName(loaderLibraries[i]);
                int position;
                if (!string.IsNullOrEmpty(name) && positions.TryGetValue(name, out position))
                    merged[position] = loaderLibraries[i];
                else
                {
                    if (!string.IsNullOrEmpty(name))
                        positions[name] = merged.Count;
                    merged.Add(loaderLibraries[i]);
                }
            }
            return merged.ToArray();
        }

        private static string LibraryName(object library)
        {
            Dictionary<string, object> item = library as Dictionary<string, object>;
            return item == null ? null : JsonUtil.GetString(item, "name");
        }

        private static Dictionary<string, object> MergeArguments(
            Dictionary<string, object> baseArguments, Dictionary<string, object> loaderArguments)
        {
            if (baseArguments == null && loaderArguments == null)
                return null;
            if (baseArguments == null)
                return loaderArguments;
            if (loaderArguments == null)
                return baseArguments;

            Dictionary<string, object> merged = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> pair in baseArguments)
                merged[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, object> pair in loaderArguments)
            {
                string key = pair.Key;
                object[] baseValues = merged.ContainsKey(key) ? merged[key] as object[] : null;
                object[] loaderValues = pair.Value as object[];
                if (baseValues != null && loaderValues != null)
                {
                    object[] combined = new object[baseValues.Length + loaderValues.Length];
                    Array.Copy(baseValues, 0, combined, 0, baseValues.Length);
                    Array.Copy(loaderValues, 0, combined, baseValues.Length, loaderValues.Length);
                    merged[key] = combined;
                }
                else
                {
                    merged[key] = pair.Value;
                }
            }
            return merged;
        }

        private static void VerifySha1(byte[] data, string expected, string label)
        {
            if (string.IsNullOrEmpty(expected))
                return;
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                if (!string.Equals(builder.ToString(), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InstallException(label + " SHA1 校验失败，请重试。");
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "client-log.xml";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString();
        }

        private static void EnsureSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "." || id == ".." ||
                !SafeIdRegex.IsMatch(id) || id.EndsWith(".", StringComparison.Ordinal))
                throw new InstallException("服务端返回了不合法的版本名称。");
        }

        private static void EnsureSafePackId(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "." || id == ".." ||
                !SafePackIdRegex.IsMatch(id) || id.EndsWith(".", StringComparison.Ordinal))
                throw new InstallException("整合包名称不是合法的版本名称。");
        }

        private static string TrimForStatus(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim().Replace("\r", " ").Replace("\n", " ");
            if (text.Length <= length) return text;
            return text.Substring(0, length) + "…";
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private void SetStage(string text)
        {
            _stats.Indeterminate = true;
            HttpUtil.SafeLog(_stage, text);
        }

        private void Log(string text)
        {
            HttpUtil.SafeLog(_log, text);
        }

        private sealed class ProcessResult
        {
            public bool Success;
            public int ExitCode;
            public string Detail;
            public string Summary;
        }

        private sealed class ConcurrentLineBuffer
        {
            private readonly object _sync = new object();
            private readonly Queue<string> _lines = new Queue<string>();
            private readonly int _capacity;

            public ConcurrentLineBuffer(int capacity)
            {
                _capacity = capacity;
            }

            public void Add(string line)
            {
                lock (_sync)
                {
                    _lines.Enqueue(line);
                    while (_lines.Count > _capacity)
                        _lines.Dequeue();
                }
            }

            public string GetDetail()
            {
                lock (_sync)
                {
                    List<string> meaningful = new List<string>();
                    foreach (string line in _lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith("Patching ", StringComparison.OrdinalIgnoreCase))
                            continue;
                        meaningful.Add(trimmed);
                    }
                    if (meaningful.Count > 8)
                        meaningful = meaningful.Skip(meaningful.Count - 8).ToList();
                    return string.Join("\n", meaningful.ToArray());
                }
            }
        }
    }
}
