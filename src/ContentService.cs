using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCDT
{
    public sealed class ContentApiException : Exception
    {
        public ContentApiException(string message) : base(message) { }
        public ContentApiException(string message, Exception inner) : base(message, inner) { }
    }

    public static class ContentService
    {
        private const string ModrinthApi = "https://api.modrinth.com/v2";
        private const string CurseForgeApi = "https://api.curseforge.com/v1";
        private const string GameId = "432";
        private static readonly string[] KnownLoaders = new string[] { "fabric", "forge", "neoforge", "quilt", "liteloader", "rift" };

        public static async Task<ContentSearchResult> SearchAsync(ContentSource source, ContentKind kind,
            string query, string gameVersion, string loader, int offset, int limit,
            Action<string> log, CancellationToken cancellationToken)
        {
            try
            {
                if (source == ContentSource.CurseForge)
                    return await SearchCurseForgeAsync(kind, query, gameVersion, loader, offset, limit, log, cancellationToken).ConfigureAwait(false);
                return await SearchModrinthAsync(kind, query, gameVersion, loader, offset, limit, log, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContentApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Wrap(ex, source);
            }
        }

        public static async Task<List<ContentVersion>> GetVersionsAsync(ContentProject project,
            string gameVersion, string loader, Action<string> log, CancellationToken cancellationToken)
        {
            try
            {
                if (project.Source == ContentSource.CurseForge)
                    return await GetCurseForgeVersionsAsync(project.Id, project.Kind, gameVersion, loader, log, cancellationToken).ConfigureAwait(false);
                return await GetModrinthVersionsAsync(project.Id, project.Kind, gameVersion, loader, log, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContentApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Wrap(ex, project.Source);
            }
        }

        public static async Task<ContentProject> GetProjectAsync(ContentSource source, string projectId,
            ContentKind kind, Action<string> log, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(projectId))
                throw new ContentApiException("缺少项目 ID。");
            try
            {
                if (source == ContentSource.CurseForge)
                    return await GetCurseForgeProjectAsync(projectId, kind, log, cancellationToken).ConfigureAwait(false);
                return await GetModrinthProjectAsync(projectId, kind, log, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContentApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Wrap(ex, source);
            }
        }

        private static async Task<ContentProject> GetModrinthProjectAsync(string projectId, ContentKind kind,
            Action<string> log, CancellationToken cancellationToken)
        {
            string url = ModrinthApi + "/project/" + Uri.EscapeDataString(projectId);
            string json = await GetJsonAsync(url, false, log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> data = JsonUtil.AsObject(JsonUtil.Deserialize(json));

            ContentProject project = new ContentProject();
            project.Source = ContentSource.Modrinth;
            project.Kind = kind;
            project.Id = JsonUtil.GetString(data, "id", projectId);
            project.Slug = JsonUtil.GetString(data, "slug");
            project.Title = JsonUtil.GetString(data, "title", project.Slug ?? project.Id);
            project.Description = JsonUtil.GetString(data, "description");
            project.IconUrl = JsonUtil.GetString(data, "icon_url");
            project.Downloads = JsonUtil.GetLong(data, "downloads", 0);
            project.Updated = JsonUtil.ParseTime(JsonUtil.GetString(data, "updated"));
            project.Categories = JsonUtil.GetStringList(data, "categories");
            project.WebUrl = "https://modrinth.com/" + kind.ModrinthType() + "/" +
                             Uri.EscapeDataString(project.Slug ?? project.Id);
            return project;
        }

        private static async Task<ContentProject> GetCurseForgeProjectAsync(string projectId, ContentKind kind,
            Action<string> log, CancellationToken cancellationToken)
        {
            string url = CurseForgeApi + "/mods/" + Uri.EscapeDataString(projectId);
            string json = await GetJsonAsync(url, true, log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
            Dictionary<string, object> data = JsonUtil.GetObject(root, "data");
            ContentProject project = ParseCurseForgeProject(data, kind);
            if (project == null)
                throw new ContentApiException("CurseForge 没有返回该 mod 的信息。");
            return project;
        }

        public static async Task<List<ModDependency>> ResolveModDependenciesAsync(ContentVersion selected,
            string modsDirectory, string gameVersion, string loader,
            Action<string> log, CancellationToken cancellationToken)
        {
            List<ModDependency> result = new List<ModDependency>();
            if (selected == null)
                return result;

            string effectiveGame = IsRealFilter(gameVersion) ? gameVersion : FirstOrEmpty(selected.GameVersions);
            string effectiveLoader = IsRealFilter(loader) ? loader : FirstLoader(selected.Loaders);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(selected.ProjectId))
                visited.Add(selected.ProjectId);
            ModFileIndex index = ModFileIndex.Create(modsDirectory);

            try
            {
                await CollectDependenciesAsync(selected, result, index, effectiveGame, effectiveLoader,
                    visited, 0, log, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContentApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Wrap(ex, selected.Source);
            }

            if (result.Count == 0)
                LogSafe(log, "必需的依赖都已安装或没有额外依赖。");
            else
                LogSafe(log, "有 " + result.Count + " 个必需依赖尚未安装。");
            return result;
        }

        private static async Task CollectDependenciesAsync(ContentVersion parent, List<ModDependency> result,
            ModFileIndex index, string gameVersion, string loader, HashSet<string> visited, int depth,
            Action<string> log, CancellationToken cancellationToken)
        {
            if (parent.Dependencies == null || parent.Dependencies.Count == 0 || depth >= 8 || result.Count >= 40)
                return;

            for (int i = 0; i < parent.Dependencies.Count; i++)
            {
                ContentDependency dep = parent.Dependencies[i];
                string key = !string.IsNullOrEmpty(dep.ProjectId) ? dep.ProjectId : dep.VersionId;
                if (string.IsNullOrEmpty(key) || visited.Contains(key))
                    continue;
                visited.Add(key);
                cancellationToken.ThrowIfCancellationRequested();

                ContentVersion resolved = null;
                string error = null;
                try
                {
                    resolved = await ResolveDependencyAsync(parent.Source, dep, gameVersion, loader,
                        log, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    LogSafe(log, "依赖解析失败：" + ex.Message);
                }

                string projectId = dep.ProjectId;
                if (string.IsNullOrEmpty(projectId) && resolved != null)
                    projectId = resolved.ProjectId;

                ContentProject project = null;
                if (!string.IsNullOrEmpty(projectId))
                {
                    try
                    {
                        project = await GetProjectAsync(parent.Source, projectId, ContentKind.Mod,
                            log, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        LogSafe(log, "依赖项目信息获取失败：" + ex.Message);
                    }
                }
                if (project == null && !string.IsNullOrEmpty(projectId))
                {
                    project = new ContentProject();
                    project.Source = parent.Source;
                    project.Kind = ContentKind.Mod;
                    project.Id = projectId;
                    project.Title = resolved != null && !string.IsNullOrEmpty(resolved.Name)
                        ? resolved.Name
                        : projectId;
                }

                string title = project != null && !string.IsNullOrEmpty(project.Title)
                    ? project.Title
                    : (resolved != null && !string.IsNullOrEmpty(resolved.Name) ? resolved.Name : key);
                string installed = index == null ? null : index.FindInstalled(resolved, project);
                if (!string.IsNullOrEmpty(installed))
                {
                    LogSafe(log, "必需依赖已安装：" + title + "（" + installed + "），跳过。");
                }
                else
                {
                    ModDependency item = new ModDependency();
                    item.Version = resolved;
                    item.Project = project;
                    item.Title = title;
                    item.Error = resolved == null
                        ? (string.IsNullOrEmpty(error) ? "未找到兼容版本" : error)
                        : null;
                    item.Subtitle = resolved == null
                        ? "无法自动下载，请手动安装"
                        : ((string.IsNullOrEmpty(resolved.VersionNumber) ? "" : resolved.VersionNumber + " · ") +
                           parent.Source.DisplayName());
                    result.Add(item);
                }

                if (resolved != null)
                {
                    await CollectDependenciesAsync(resolved, result, index, gameVersion, loader,
                        visited, depth + 1, log, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task<ContentVersion> ResolveDependencyAsync(ContentSource source,
            ContentDependency dep, string gameVersion, string loader,
            Action<string> log, CancellationToken cancellationToken)
        {
            if (source == ContentSource.Modrinth)
            {
                if (!string.IsNullOrEmpty(dep.VersionId))
                {
                    string url = ModrinthApi + "/version/" + Uri.EscapeDataString(dep.VersionId);
                    string json = await GetJsonAsync(url, false, log, cancellationToken).ConfigureAwait(false);
                    return ParseModrinthVersion(JsonUtil.AsObject(JsonUtil.Deserialize(json)),
                        dep.ProjectId, ContentKind.Mod);
                }
                if (!string.IsNullOrEmpty(dep.ProjectId))
                {
                    List<ContentVersion> versions = await GetModrinthVersionsAsync(dep.ProjectId,
                        ContentKind.Mod, gameVersion, loader, log, cancellationToken).ConfigureAwait(false);
                    if (versions.Count > 0)
                        return versions[0];
                }
                return null;
            }

            if (!string.IsNullOrEmpty(dep.ProjectId))
            {
                if (!string.IsNullOrEmpty(dep.FileId))
                {
                    CurseForgeFile file = await GetCurseForgeFileAsync(dep.ProjectId, dep.FileId,
                        log, cancellationToken).ConfigureAwait(false);
                    return CurseForgeFileToVersion(file);
                }
                List<ContentVersion> versions = await GetCurseForgeVersionsAsync(dep.ProjectId,
                    ContentKind.Mod, gameVersion, loader, log, cancellationToken).ConfigureAwait(false);
                if (versions.Count > 0)
                    return versions[0];
            }
            return null;
        }

        public static async Task<DownloadItem> CreateFileItemAsync(ContentVersion version, string destDirectory,
            Action<string> log, CancellationToken cancellationToken)
        {
            if (version == null)
                throw new ContentApiException("没有选择要下载的版本。");
            if (string.IsNullOrEmpty(destDirectory))
                throw new ContentApiException("下载目录为空。");

            try
            {
                if (version.Source == ContentSource.CurseForge && string.IsNullOrEmpty(version.DownloadUrl))
                {
                    CurseForgeFile detail = await GetCurseForgeFileAsync(version.ProjectId, version.Id, log, cancellationToken).ConfigureAwait(false);
                    version.DownloadUrl = detail.DownloadUrl;
                    if (string.IsNullOrEmpty(version.FileName))
                        version.FileName = detail.FileName;
                    if (version.Size <= 0)
                        version.Size = detail.Size;
                    if (string.IsNullOrEmpty(version.Sha1))
                        version.Sha1 = detail.Sha1;
                }
                if (string.IsNullOrEmpty(version.DownloadUrl))
                    throw new ContentApiException("这个文件没有可用的下载地址（作者可能关闭了第三方下载）。");

                string fileName = SanitizeFileName(version.FileName);
                List<string> urls = new List<string>();
                urls.Add(version.DownloadUrl);
                if (version.Source == ContentSource.CurseForge)
                {
                    string fallback = CurseForgeCdnUrl(version.Id, fileName);
                    if (!string.IsNullOrEmpty(fallback) && !urls.Contains(fallback))
                        urls.Add(fallback);
                }

                DownloadItem item = new DownloadItem();
                item.Name = fileName;
                item.DestPath = Path.Combine(destDirectory, fileName);
                item.Sha1 = version.Sha1;
                item.Size = version.Size;
                item.Urls = urls;
                return item;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContentApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Wrap(ex, version.Source);
            }
        }

        public static async Task<CurseForgeFile> GetCurseForgeFileAsync(string modId, string fileId,
            Action<string> log, CancellationToken cancellationToken)
        {
            try
            {
                string url = CurseForgeApi + "/mods/" + Uri.EscapeDataString(modId) + "/files/" + Uri.EscapeDataString(fileId);
                string json = await GetJsonAsync(url, true, log, cancellationToken).ConfigureAwait(false);
                Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
                Dictionary<string, object> data = JsonUtil.GetObject(root, "data");
                if (data == null)
                    throw new ContentApiException("CurseForge 没有返回文件信息。");
                CurseForgeFile file = ParseCurseForgeFile(data);
                await EnsureCurseForgeUrlAsync(file, log, cancellationToken).ConfigureAwait(false);
                return file;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ContentApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw Wrap(ex, ContentSource.CurseForge);
            }
        }

        public static async Task TestCurseForgeKeyAsync(string apiKey, CancellationToken cancellationToken)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["x-api-key"] = (apiKey ?? "").Trim();
            headers["Accept"] = "application/json";
            string url = CurseForgeApi + "/mods/search?gameId=" + GameId + "&classId=6&pageSize=1";
            try
            {
                await HttpUtil.GetStringAsync(new List<string> { url }, headers, null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HttpSourceException http = FindHttpSource(ex);
                if (http != null && (http.StatusCode == 401 || http.StatusCode == 403))
                    throw new ContentApiException("API Key 无效或没有权限（HTTP " + http.StatusCode + "）。", ex);
                if (http != null)
                    throw new ContentApiException("CurseForge 返回 HTTP " + http.StatusCode + "。", ex);
                throw new ContentApiException("连接 CurseForge 失败：" + ex.Message, ex);
            }
        }

        public static string GetProjectWebUrl(ContentProject project)
        {
            if (project == null)
                return "";
            if (!string.IsNullOrEmpty(project.WebUrl))
                return project.WebUrl;
            if (project.Source == ContentSource.Modrinth)
                return "https://modrinth.com/" + project.Kind.ModrinthType() + "/" + Uri.EscapeDataString(project.Slug ?? project.Id);
            return CurseForgeWebUrl(project.Kind, project.Slug ?? project.Id);
        }

        public static string GetMcmodSearchUrl(string title)
        {
            return "https://search.mcmod.cn/s?key=" + Uri.EscapeDataString(title ?? "");
        }

        private static async Task<ContentSearchResult> SearchModrinthAsync(ContentKind kind, string query,
            string gameVersion, string loader, int offset, int limit, Action<string> log, CancellationToken cancellationToken)
        {
            limit = Math.Max(1, Math.Min(50, limit));
            List<string> facets = new List<string>();
            facets.Add("[\"project_type:" + kind.ModrinthType() + "\"]");
            if (IsRealFilter(gameVersion))
                facets.Add("[\"versions:" + EscapeJson(gameVersion) + "\"]");
            if (kind.HasLoaderFilter() && IsRealFilter(loader))
                facets.Add("[\"categories:" + loader.ToLowerInvariant() + "\"]");
            string facetsJson = "[" + string.Join(",", facets.ToArray()) + "]";
            string index = string.IsNullOrEmpty((query ?? "").Trim()) ? "downloads" : "relevance";
            string url = ModrinthApi + "/search?query=" + Uri.EscapeDataString((query ?? "").Trim()) +
                         "&limit=" + limit.ToString(CultureInfo.InvariantCulture) +
                         "&offset=" + offset.ToString(CultureInfo.InvariantCulture) +
                         "&index=" + index + "&facets=" + Uri.EscapeDataString(facetsJson);

            string json = await GetJsonAsync(url, false, log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
            object[] hits = JsonUtil.GetArray(root, "hits");
            ContentSearchResult result = new ContentSearchResult();
            if (hits != null)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Dictionary<string, object> hit = hits[i] as Dictionary<string, object>;
                    if (hit == null)
                        continue;
                    string id = JsonUtil.GetString(hit, "project_id");
                    if (string.IsNullOrEmpty(id))
                        continue;
                    string slug = JsonUtil.GetString(hit, "slug");
                    string type = JsonUtil.GetString(hit, "project_type", kind.ModrinthType());
                    ContentProject project = new ContentProject();
                    project.Source = ContentSource.Modrinth;
                    project.Kind = kind;
                    project.Id = id;
                    project.Slug = slug;
                    project.Title = JsonUtil.GetString(hit, "title", slug);
                    project.Author = JsonUtil.GetString(hit, "author");
                    project.Description = JsonUtil.GetString(hit, "description");
                    project.IconUrl = JsonUtil.GetString(hit, "icon_url");
                    project.Downloads = JsonUtil.GetLong(hit, "downloads", 0);
                    project.Updated = JsonUtil.ParseTime(JsonUtil.GetString(hit, "date_modified"));
                    project.Categories = JsonUtil.GetStringList(hit, "categories");
                    project.WebUrl = "https://modrinth.com/" + type + "/" + (slug ?? id);
                    project.CompatText = BuildModrinthCompat(project.Categories, JsonUtil.GetStringList(hit, "versions"));
                    result.Projects.Add(project);
                }
            }
            result.Total = (int)JsonUtil.GetLong(root, "total_hits", result.Projects.Count);
            result.HasMore = offset + result.Projects.Count < result.Total;
            return result;
        }

        private static async Task<ContentSearchResult> SearchCurseForgeAsync(ContentKind kind, string query,
            string gameVersion, string loader, int offset, int limit, Action<string> log, CancellationToken cancellationToken)
        {
            limit = Math.Max(1, Math.Min(50, limit));
            StringBuilder url = new StringBuilder();
            url.Append(CurseForgeApi).Append("/mods/search?gameId=").Append(GameId);
            url.Append("&classId=").Append(kind.CurseForgeClassId().ToString(CultureInfo.InvariantCulture));
            url.Append("&index=").Append(offset.ToString(CultureInfo.InvariantCulture));
            url.Append("&pageSize=").Append(limit.ToString(CultureInfo.InvariantCulture));
            url.Append("&sortField=2&sortOrder=desc");
            if (!string.IsNullOrEmpty((query ?? "").Trim()))
                url.Append("&searchFilter=").Append(Uri.EscapeDataString(query.Trim()));
            if (IsRealFilter(gameVersion))
                url.Append("&gameVersion=").Append(Uri.EscapeDataString(gameVersion));
            int loaderType = CurseForgeLoaderType(loader);
            if (kind.HasLoaderFilter() && loaderType > 0)
                url.Append("&modLoaderType=").Append(loaderType.ToString(CultureInfo.InvariantCulture));

            string json = await GetJsonAsync(url.ToString(), true, log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
            object[] data = JsonUtil.GetArray(root, "data");
            ContentSearchResult result = new ContentSearchResult();
            if (data != null)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    Dictionary<string, object> item = data[i] as Dictionary<string, object>;
                    if (item == null)
                        continue;
                    ContentProject project = ParseCurseForgeProject(item, kind);
                    if (project != null)
                        result.Projects.Add(project);
                }
            }
            Dictionary<string, object> pagination = JsonUtil.GetObject(root, "pagination");
            result.Total = (int)JsonUtil.GetLong(pagination, "totalCount", result.Projects.Count);
            result.HasMore = offset + result.Projects.Count < result.Total;
            return result;
        }

        private static ContentProject ParseCurseForgeProject(Dictionary<string, object> item, ContentKind kind)
        {
            string id = Convert.ToString(JsonUtil.GetValue(item, "id"), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(id))
                return null;
            ContentProject project = new ContentProject();
            project.Source = ContentSource.CurseForge;
            project.Kind = kind;
            project.Id = id;
            project.Slug = JsonUtil.GetString(item, "slug");
            project.Title = JsonUtil.GetString(item, "name", project.Slug);
            project.Description = JsonUtil.GetString(item, "summary");
            project.Downloads = JsonUtil.GetLong(item, "downloadCount", 0);
            project.Updated = JsonUtil.ParseTime(JsonUtil.GetString(item, "dateModified"));

            Dictionary<string, object> links = JsonUtil.GetObject(item, "links");
            project.WebUrl = JsonUtil.GetString(links, "websiteUrl");
            if (string.IsNullOrEmpty(project.WebUrl))
                project.WebUrl = CurseForgeWebUrl(kind, project.Slug ?? id);

            Dictionary<string, object> logo = JsonUtil.GetObject(item, "logo");
            project.IconUrl = JsonUtil.GetString(logo, "thumbnailUrl");
            if (string.IsNullOrEmpty(project.IconUrl))
                project.IconUrl = JsonUtil.GetString(logo, "url");

            List<Dictionary<string, object>> authors = JsonUtil.GetObjectList(item, "authors");
            if (authors.Count > 0)
                project.Author = JsonUtil.GetString(authors[0], "name");

            object[] categories = JsonUtil.GetArray(item, "categories");
            if (categories != null)
            {
                for (int i = 0; i < categories.Length; i++)
                {
                    Dictionary<string, object> category = categories[i] as Dictionary<string, object>;
                    string name = JsonUtil.GetString(category, "name");
                    if (!string.IsNullOrEmpty(name))
                        project.Categories.Add(name);
                }
            }

            List<Dictionary<string, object>> indexes = JsonUtil.GetObjectList(item, "latestFilesIndexes");
            List<string> gameVersions = new List<string>();
            List<string> loaders = new List<string>();
            for (int i = 0; i < indexes.Count; i++)
            {
                string game = JsonUtil.GetString(indexes[i], "gameVersion");
                if (!string.IsNullOrEmpty(game) && !gameVersions.Contains(game))
                    gameVersions.Add(game);
                int loaderType = (int)JsonUtil.GetLong(indexes[i], "modLoader", 0);
                string loaderName = CurseForgeLoaderName(loaderType);
                if (!string.IsNullOrEmpty(loaderName) && !loaders.Contains(loaderName))
                    loaders.Add(loaderName);
                if (gameVersions.Count >= 4 && loaders.Count >= 3)
                    break;
            }
            project.CompatText = JoinParts(loaders, gameVersions);
            return project;
        }

        private static async Task<List<ContentVersion>> GetModrinthVersionsAsync(string projectId, ContentKind kind,
            string gameVersion, string loader, Action<string> log, CancellationToken cancellationToken)
        {
            string url = ModrinthApi + "/project/" + Uri.EscapeDataString(projectId) + "/version";
            List<string> query = new List<string>();
            if (IsRealFilter(gameVersion))
                query.Add("game_versions=" + Uri.EscapeDataString("[\"" + EscapeJson(gameVersion) + "\"]"));
            if (kind.HasLoaderFilter() && IsRealFilter(loader))
                query.Add("loaders=" + Uri.EscapeDataString("[\"" + EscapeJson(loader.ToLowerInvariant()) + "\"]"));
            if (query.Count > 0)
                url += "?" + string.Join("&", query.ToArray());
            string json = await GetJsonAsync(url, false, log, cancellationToken).ConfigureAwait(false);
            object[] data = JsonUtil.AsArray(JsonUtil.Deserialize(json));
            List<ContentVersion> result = new List<ContentVersion>();
            if (data == null)
                return result;

            for (int i = 0; i < data.Length; i++)
            {
                Dictionary<string, object> item = data[i] as Dictionary<string, object>;
                if (item == null)
                    continue;
                ContentVersion version = ParseModrinthVersion(item, projectId, kind);
                if (version == null)
                    continue;
                if (!MatchesVersion(version, kind, gameVersion, loader))
                    continue;
                result.Add(version);
            }
            result.Sort(delegate(ContentVersion a, ContentVersion b) { return b.Published.CompareTo(a.Published); });
            if (result.Count > 150)
                result.RemoveRange(150, result.Count - 150);
            if (result.Count == 0)
                LogSafe(log, "没有找到符合当前游戏版本 / 加载器筛选的版本，可以试试“全部”。");
            return result;
        }

        private static ContentVersion ParseModrinthVersion(Dictionary<string, object> item, string projectId, ContentKind kind)
        {
            object[] files = JsonUtil.GetArray(item, "files");
            Dictionary<string, object> file = null;
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    Dictionary<string, object> candidate = files[i] as Dictionary<string, object>;
                    if (candidate == null)
                        continue;
                    if (file == null)
                        file = candidate;
                    if (JsonUtil.GetBool(candidate, "primary", false))
                    {
                        file = candidate;
                        break;
                    }
                }
            }
            if (file == null)
                return null;

            ContentVersion version = new ContentVersion();
            version.Source = ContentSource.Modrinth;
            version.ProjectId = !string.IsNullOrEmpty(projectId)
                ? projectId
                : JsonUtil.GetString(item, "project_id");
            version.Id = JsonUtil.GetString(item, "id");
            version.Name = JsonUtil.GetString(item, "name");
            version.VersionNumber = JsonUtil.GetString(item, "version_number", version.Name);
            version.GameVersions = JsonUtil.GetStringList(item, "game_versions");
            version.Loaders = JsonUtil.GetStringList(item, "loaders");
            version.Published = JsonUtil.ParseTime(JsonUtil.GetString(item, "date_published"));
            version.Downloads = JsonUtil.GetLong(item, "downloads", 0);
            version.ReleaseType = JsonUtil.GetString(item, "version_type", "release");
            version.FileName = JsonUtil.GetString(file, "filename");
            version.DownloadUrl = JsonUtil.GetString(file, "url");
            version.Size = JsonUtil.GetLong(file, "size", 0);
            Dictionary<string, object> hashes = JsonUtil.GetObject(file, "hashes");
            version.Sha1 = JsonUtil.GetString(hashes, "sha1");
            if (string.IsNullOrEmpty(version.DownloadUrl) || string.IsNullOrEmpty(version.FileName))
                return null;

            List<Dictionary<string, object>> deps = JsonUtil.GetObjectList(item, "dependencies");
            for (int i = 0; i < deps.Count; i++)
            {
                string type = JsonUtil.GetString(deps[i], "dependency_type", "required");
                if (!string.Equals(type, "required", StringComparison.OrdinalIgnoreCase))
                    continue;
                ContentDependency dep = new ContentDependency();
                dep.ProjectId = JsonUtil.GetString(deps[i], "project_id");
                dep.VersionId = JsonUtil.GetString(deps[i], "version_id");
                dep.Required = true;
                if (!string.IsNullOrEmpty(dep.ProjectId) || !string.IsNullOrEmpty(dep.VersionId))
                    version.Dependencies.Add(dep);
            }
            return version;
        }

        private static async Task<List<ContentVersion>> GetCurseForgeVersionsAsync(string modId, ContentKind kind,
            string gameVersion, string loader, Action<string> log, CancellationToken cancellationToken)
        {
            List<ContentVersion> result = new List<ContentVersion>();
            int loaderType = CurseForgeLoaderType(loader);
            string baseUrl = CurseForgeApi + "/mods/" + Uri.EscapeDataString(modId) + "/files?pageSize=50";
            if (IsRealFilter(gameVersion))
                baseUrl += "&gameVersion=" + Uri.EscapeDataString(gameVersion);
            if (loaderType > 0)
                baseUrl += "&modLoaderType=" + loaderType.ToString(CultureInfo.InvariantCulture);

            for (int page = 0; page < 2; page++)
            {
                string url = baseUrl + "&index=" + (page * 50).ToString(CultureInfo.InvariantCulture);
                string json = await GetJsonAsync(url, true, log, cancellationToken).ConfigureAwait(false);
                Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
                object[] data = JsonUtil.GetArray(root, "data");
                if (data == null || data.Length == 0)
                    break;
                for (int i = 0; i < data.Length; i++)
                {
                    Dictionary<string, object> item = data[i] as Dictionary<string, object>;
                    if (item == null)
                        continue;
                    ContentVersion version = ParseCurseForgeVersion(item, modId);
                    if (version == null)
                        continue;
                    if (!MatchesVersion(version, kind, gameVersion, loader))
                        continue;
                    result.Add(version);
                }
                if (data.Length < 50)
                    break;
            }

            result.Sort(delegate(ContentVersion a, ContentVersion b) { return b.Published.CompareTo(a.Published); });
            if (result.Count == 0)
                LogSafe(log, "没有找到符合当前游戏版本 / 加载器筛选的版本，可以试试“全部”。");
            return result;
        }

        private static ContentVersion ParseCurseForgeVersion(Dictionary<string, object> item, string modId)
        {
            string url = JsonUtil.GetString(item, "downloadUrl");
            string fileName = JsonUtil.GetString(item, "fileName");
            if (string.IsNullOrEmpty(fileName))
                return null;

            ContentVersion version = new ContentVersion();
            version.Source = ContentSource.CurseForge;
            version.ProjectId = modId;
            version.Id = Convert.ToString(JsonUtil.GetValue(item, "id"), CultureInfo.InvariantCulture);
            version.Name = JsonUtil.GetString(item, "displayName", fileName);
            version.VersionNumber = version.Name;
            version.FileName = fileName;
            version.DownloadUrl = url;
            version.Size = JsonUtil.GetLong(item, "fileLength", 0);
            version.Downloads = JsonUtil.GetLong(item, "downloadCount", 0);
            version.Published = JsonUtil.ParseTime(JsonUtil.GetString(item, "fileDate"));
            version.ReleaseType = CurseForgeReleaseType((int)JsonUtil.GetLong(item, "releaseType", 1));
            version.Sha1 = FindCurseForgeHash(item, 1);

            List<string> allVersions = JsonUtil.GetStringList(item, "gameVersions");
            for (int i = 0; i < allVersions.Count; i++)
            {
                string value = allVersions[i];
                string loaderName = CanonicalLoader(value);
                if (!string.IsNullOrEmpty(loaderName))
                {
                    if (!version.Loaders.Contains(loaderName))
                        version.Loaders.Add(loaderName);
                }
                else if (!string.Equals(value, "Client", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(value, "Server", StringComparison.OrdinalIgnoreCase) &&
                         !version.GameVersions.Contains(value))
                {
                    version.GameVersions.Add(value);
                }
            }

            List<Dictionary<string, object>> deps = JsonUtil.GetObjectList(item, "dependencies");
            for (int i = 0; i < deps.Count; i++)
            {
                int relation = (int)JsonUtil.GetLong(deps[i], "relationType", 0);
                if (relation != 3 && relation != 6)
                    continue;
                ContentDependency dep = new ContentDependency();
                dep.ProjectId = Convert.ToString(JsonUtil.GetValue(deps[i], "modId"), CultureInfo.InvariantCulture);
                dep.Required = true;
                if (!string.IsNullOrEmpty(dep.ProjectId))
                    version.Dependencies.Add(dep);
            }
            return version;
        }

        private static CurseForgeFile ParseCurseForgeFile(Dictionary<string, object> item)
        {
            CurseForgeFile file = new CurseForgeFile();
            file.FileId = Convert.ToString(JsonUtil.GetValue(item, "id"), CultureInfo.InvariantCulture);
            file.ModId = Convert.ToString(JsonUtil.GetValue(item, "modId"), CultureInfo.InvariantCulture);
            file.DisplayName = JsonUtil.GetString(item, "displayName");
            file.FileName = JsonUtil.GetString(item, "fileName");
            file.DownloadUrl = JsonUtil.GetString(item, "downloadUrl");
            file.Size = JsonUtil.GetLong(item, "fileLength", 0);
            file.Published = JsonUtil.ParseTime(JsonUtil.GetString(item, "fileDate"));
            file.Downloads = JsonUtil.GetLong(item, "downloadCount", 0);
            file.ReleaseType = (int)JsonUtil.GetLong(item, "releaseType", 1);
            file.Sha1 = FindCurseForgeHash(item, 1);
            file.GameVersions = JsonUtil.GetStringList(item, "gameVersions");

            List<Dictionary<string, object>> deps = JsonUtil.GetObjectList(item, "dependencies");
            for (int i = 0; i < deps.Count; i++)
            {
                int relation = (int)JsonUtil.GetLong(deps[i], "relationType", 0);
                if (relation != 3 && relation != 6)
                    continue;
                ContentDependency dep = new ContentDependency();
                dep.ProjectId = Convert.ToString(JsonUtil.GetValue(deps[i], "modId"), CultureInfo.InvariantCulture);
                dep.Required = true;
                if (!string.IsNullOrEmpty(dep.ProjectId))
                    file.Dependencies.Add(dep);
            }
            return file;
        }

        private static ContentVersion CurseForgeFileToVersion(CurseForgeFile file)
        {
            ContentVersion version = new ContentVersion();
            version.Source = ContentSource.CurseForge;
            version.ProjectId = file.ModId;
            version.Id = file.FileId;
            version.Name = file.DisplayName;
            version.VersionNumber = file.DisplayName;
            version.FileName = file.FileName;
            version.DownloadUrl = file.DownloadUrl;
            version.Size = file.Size;
            version.Sha1 = file.Sha1;
            version.Published = file.Published;
            version.Downloads = file.Downloads;
            version.Dependencies = file.Dependencies;
            version.GameVersions = ParseCurseForgeGameVersions(file.GameVersions);
            version.Loaders = ParseCurseForgeLoaders(file.GameVersions);
            return version;
        }

        private static async Task EnsureCurseForgeUrlAsync(CurseForgeFile file, Action<string> log, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(file.DownloadUrl))
                return;
            file.DownloadUrl = await GetCurseForgeDownloadUrlAsync(file.ModId, file.FileId, log, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> GetCurseForgeDownloadUrlAsync(string modId, string fileId,
            Action<string> log, CancellationToken cancellationToken)
        {
            string url = CurseForgeApi + "/mods/" + Uri.EscapeDataString(modId) + "/files/" +
                         Uri.EscapeDataString(fileId) + "/download-url";
            string json = await GetJsonAsync(url, true, log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
            string download = JsonUtil.GetString(root, "data");
            if (string.IsNullOrEmpty(download))
                throw new ContentApiException("CurseForge 没有返回这个文件的下载地址（作者可能关闭了第三方下载）。");
            return download;
        }

        private static async Task<string> GetJsonAsync(string url, bool curseForge, Action<string> log, CancellationToken cancellationToken)
        {
            Dictionary<string, string> headers = null;
            if (curseForge)
            {
                string key = AppSettings.CurseForgeApiKey;
                if (string.IsNullOrEmpty(key))
                    throw new ContentApiException("尚未设置 CurseForge API Key。请点击右上角“设置”填写后重试。");
                headers = new Dictionary<string, string>();
                headers["x-api-key"] = key;
                headers["Accept"] = "application/json";
            }
            return await HttpUtil.GetStringAsync(new List<string> { url }, headers, log, cancellationToken).ConfigureAwait(false);
        }

        private static bool MatchesVersion(ContentVersion version, ContentKind kind, string gameVersion, string loader)
        {
            if (IsRealFilter(gameVersion) && !ContainsIgnoreCase(version.GameVersions, gameVersion))
                return false;
            if (kind.HasLoaderFilter() && IsRealFilter(loader) && !ContainsIgnoreCase(version.Loaders, loader))
                return false;
            return true;
        }

        private static bool ContainsIgnoreCase(List<string> values, string value)
        {
            if (values == null)
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsRealFilter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return !string.Equals(value, "全部", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(value, "所有", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstOrEmpty(List<string> values)
        {
            return values != null && values.Count > 0 ? values[0] : "";
        }

        private static string FirstLoader(List<string> values)
        {
            if (values == null)
                return "";
            for (int i = 0; i < values.Count; i++)
            {
                string canonical = CanonicalLoader(values[i]);
                if (!string.IsNullOrEmpty(canonical))
                    return canonical;
            }
            return "";
        }

        private static string CanonicalLoader(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            for (int i = 0; i < KnownLoaders.Length; i++)
            {
                if (string.Equals(value, KnownLoaders[i], StringComparison.OrdinalIgnoreCase))
                    return KnownLoaders[i];
            }
            if (string.Equals(value, "NeoForge", StringComparison.OrdinalIgnoreCase))
                return "neoforge";
            if (string.Equals(value, "Fabric", StringComparison.OrdinalIgnoreCase))
                return "fabric";
            if (string.Equals(value, "Forge", StringComparison.OrdinalIgnoreCase))
                return "forge";
            if (string.Equals(value, "Quilt", StringComparison.OrdinalIgnoreCase))
                return "quilt";
            return "";
        }

        private static int CurseForgeLoaderType(string loader)
        {
            if (string.IsNullOrEmpty(loader))
                return 0;
            if (string.Equals(loader, "Forge", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(loader, "Fabric", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (string.Equals(loader, "Quilt", StringComparison.OrdinalIgnoreCase))
                return 5;
            if (string.Equals(loader, "NeoForge", StringComparison.OrdinalIgnoreCase))
                return 6;
            return 0;
        }

        private static string CurseForgeLoaderName(int type)
        {
            switch (type)
            {
                case 1: return "Forge";
                case 4: return "Fabric";
                case 5: return "Quilt";
                case 6: return "NeoForge";
                default: return "";
            }
        }

        private static string CurseForgeReleaseType(int type)
        {
            switch (type)
            {
                case 2: return "beta";
                case 3: return "alpha";
                default: return "release";
            }
        }

        private static string FindCurseForgeHash(Dictionary<string, object> item, int algorithm)
        {
            object[] hashes = JsonUtil.GetArray(item, "hashes");
            if (hashes == null)
                return null;
            for (int i = 0; i < hashes.Length; i++)
            {
                Dictionary<string, object> hash = hashes[i] as Dictionary<string, object>;
                if (hash == null)
                    continue;
                if ((int)JsonUtil.GetLong(hash, "algo", 0) == algorithm)
                    return JsonUtil.GetString(hash, "value");
            }
            return null;
        }

        private static List<string> ParseCurseForgeGameVersions(List<string> values)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                string loader = CanonicalLoader(values[i]);
                if (!string.IsNullOrEmpty(loader))
                    continue;
                if (string.Equals(values[i], "Client", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(values[i], "Server", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(values[i]);
            }
            return result;
        }

        private static List<string> ParseCurseForgeLoaders(List<string> values)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                string loader = CanonicalLoader(values[i]);
                if (!string.IsNullOrEmpty(loader) && !result.Contains(loader))
                    result.Add(loader);
            }
            return result;
        }

        private static string BuildModrinthCompat(List<string> categories, List<string> versions)
        {
            List<string> loaders = new List<string>();
            for (int i = 0; i < categories.Count; i++)
            {
                string loader = CanonicalLoader(categories[i]);
                if (!string.IsNullOrEmpty(loader) && !loaders.Contains(loader))
                    loaders.Add(loader);
            }
            return JoinParts(loaders, versions);
        }

        private static string JoinParts(List<string> loaders, List<string> versions)
        {
            List<string> parts = new List<string>();
            if (loaders != null && loaders.Count > 0)
            {
                List<string> shown = new List<string>();
                for (int i = 0; i < loaders.Count && i < 3; i++)
                    shown.Add(DisplayLoader(loaders[i]));
                string text = string.Join(" / ", shown.ToArray());
                if (loaders.Count > 3)
                    text += " +" + (loaders.Count - 3);
                parts.Add(text);
            }
            if (versions != null && versions.Count > 0)
            {
                List<string> shown = new List<string>();
                for (int i = 0; i < versions.Count && i < 3; i++)
                    shown.Add(versions[i]);
                string text = string.Join(", ", shown.ToArray());
                if (versions.Count > 3)
                    text += " …";
                parts.Add(text);
            }
            return string.Join(" · ", parts.ToArray());
        }

        private static string DisplayLoader(string loader)
        {
            if (string.Equals(loader, "fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
            if (string.Equals(loader, "forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
            if (string.Equals(loader, "neoforge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
            if (string.Equals(loader, "quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
            if (string.Equals(loader, "liteloader", StringComparison.OrdinalIgnoreCase)) return "LiteLoader";
            return loader;
        }

        private static string CurseForgeWebUrl(ContentKind kind, string slug)
        {
            string section;
            switch (kind)
            {
                case ContentKind.Modpack: section = "modpacks"; break;
                case ContentKind.ResourcePack: section = "texture-packs"; break;
                case ContentKind.Shader: section = "shaders"; break;
                case ContentKind.DataPack: section = "data-packs"; break;
                default: section = "mc-mods"; break;
            }
            return "https://www.curseforge.com/minecraft/" + section + "/" + Uri.EscapeDataString(slug ?? "");
        }

        private static string CurseForgeCdnUrl(string fileId, string fileName)
        {
            long id;
            if (!long.TryParse(fileId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return null;
            return "https://edge.forgecdn.net/files/" + (id / 1000).ToString(CultureInfo.InvariantCulture) +
                   "/" + (id % 1000).ToString(CultureInfo.InvariantCulture) + "/" + Uri.EscapeDataString(fileName);
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

        private static string EscapeJson(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static HttpSourceException FindHttpSource(Exception ex)
        {
            while (ex != null)
            {
                HttpSourceException http = ex as HttpSourceException;
                if (http != null)
                    return http;
                ex = ex.InnerException;
            }
            return null;
        }

        private static ContentApiException Wrap(Exception ex, ContentSource source)
        {
            HttpSourceException http = FindHttpSource(ex);
            if (http != null)
            {
                if (source == ContentSource.CurseForge && (http.StatusCode == 401 || http.StatusCode == 403))
                    return new ContentApiException("CurseForge API Key 无效或没有权限。请在右上角“设置”中更新 Key。", ex);
                if (http.StatusCode == 429)
                    return new ContentApiException(source.DisplayName() + " 请求过于频繁，请稍等再试。", ex);
                if (http.StatusCode == 404)
                    return new ContentApiException(source.DisplayName() + " 上找不到这个资源，可能已被删除。", ex);
                return new ContentApiException(source.DisplayName() + " 请求失败（HTTP " + http.StatusCode + "）。", ex);
            }
            return new ContentApiException("连接 " + source.DisplayName() + " 失败：" + ex.Message, ex);
        }

        private static void LogSafe(Action<string> log, string message)
        {
            HttpUtil.SafeLog(log, message);
        }
    }
}
