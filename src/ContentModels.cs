using System;
using System.Collections.Generic;
using System.Globalization;

namespace MCDT
{
    public enum ContentSource
    {
        Modrinth,
        CurseForge
    }

    public enum ContentKind
    {
        Mod,
        Modpack,
        ResourcePack,
        Shader,
        DataPack
    }

    public static class ContentKindExtensions
    {
        public static string DisplayName(this ContentKind kind)
        {
            switch (kind)
            {
                case ContentKind.Modpack: return "整合包";
                case ContentKind.ResourcePack: return "资源包";
                case ContentKind.Shader: return "光影包";
                case ContentKind.DataPack: return "数据包";
                default: return "Mod";
            }
        }

        public static string ModrinthType(this ContentKind kind)
        {
            switch (kind)
            {
                case ContentKind.Modpack: return "modpack";
                case ContentKind.ResourcePack: return "resourcepack";
                case ContentKind.Shader: return "shader";
                case ContentKind.DataPack: return "datapack";
                default: return "mod";
            }
        }

        public static int CurseForgeClassId(this ContentKind kind)
        {
            switch (kind)
            {
                case ContentKind.Modpack: return 4471;
                case ContentKind.ResourcePack: return 12;
                case ContentKind.Shader: return 6552;
                case ContentKind.DataPack: return 6945;
                default: return 6;
            }
        }

        public static string InstallFolder(this ContentKind kind)
        {
            switch (kind)
            {
                case ContentKind.ResourcePack: return "resourcepacks";
                case ContentKind.Shader: return "shaderpacks";
                case ContentKind.DataPack: return "datapacks";
                case ContentKind.Mod: return "mods";
                default: return "";
            }
        }

        public static bool HasLoaderFilter(this ContentKind kind)
        {
            return kind == ContentKind.Mod || kind == ContentKind.Modpack;
        }
    }

    public static class ContentSourceExtensions
    {
        public static string DisplayName(this ContentSource source)
        {
            return source == ContentSource.CurseForge ? "CurseForge" : "Modrinth";
        }

        public static string WebRoot(this ContentSource source)
        {
            return source == ContentSource.CurseForge
                ? "https://www.curseforge.com"
                : "https://modrinth.com";
        }
    }

    public sealed class ContentProject
    {
        public ContentSource Source { get; set; }
        public ContentKind Kind { get; set; }
        public string Id { get; set; }
        public string Slug { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
        public long Downloads { get; set; }
        public DateTime Updated { get; set; }
        public List<string> Categories { get; set; }
        public string WebUrl { get; set; }
        public string CompatText { get; set; }

        public ContentProject()
        {
            Categories = new List<string>();
        }

        public string SourceLabel
        {
            get { return Source.DisplayName(); }
        }

        public override string ToString()
        {
            return Title;
        }
    }

    public sealed class ContentDependency
    {
        public string ProjectId { get; set; }
        public string VersionId { get; set; }
        public string FileId { get; set; }
        public bool Required { get; set; }
    }

    public sealed class ModDependency
    {
        public ContentVersion Version { get; set; }
        public ContentProject Project { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Error { get; set; }

        public bool CanDownload
        {
            get { return Version != null && string.IsNullOrEmpty(Error); }
        }
    }

    public sealed class ContentVersion
    {
        public ContentSource Source { get; set; }
        public string ProjectId { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string VersionNumber { get; set; }
        public List<string> GameVersions { get; set; }
        public List<string> Loaders { get; set; }
        public DateTime Published { get; set; }
        public long Downloads { get; set; }
        public string FileName { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
        public string Sha1 { get; set; }
        public string ReleaseType { get; set; }
        public List<ContentDependency> Dependencies { get; set; }

        public ContentVersion()
        {
            GameVersions = new List<string>();
            Loaders = new List<string>();
            Dependencies = new List<ContentDependency>();
        }

        public string CompatibilityText
        {
            get
            {
                string versions = JoinLimited(GameVersions, 4);
                string loaders = JoinLimited(Loaders, 4);
                if (versions.Length == 0)
                    return loaders;
                if (loaders.Length == 0)
                    return versions;
                return loaders + " · " + versions;
            }
        }

        public override string ToString()
        {
            string label = string.IsNullOrEmpty(VersionNumber) ? Name : VersionNumber;
            if (string.IsNullOrEmpty(label))
                label = FileName;
            return label;
        }

        private static string JoinLimited(List<string> values, int limit)
        {
            if (values == null || values.Count == 0)
                return "";
            List<string> shown = new List<string>();
            for (int i = 0; i < values.Count && i < limit; i++)
                shown.Add(values[i]);
            string text = string.Join(" / ", shown.ToArray());
            if (values.Count > limit)
                text += " +" + (values.Count - limit).ToString(CultureInfo.InvariantCulture);
            return text;
        }
    }

    public sealed class ContentSearchResult
    {
        public List<ContentProject> Projects { get; set; }
        public bool HasMore { get; set; }
        public int Total { get; set; }

        public ContentSearchResult()
        {
            Projects = new List<ContentProject>();
        }
    }

    public sealed class CurseForgeFile
    {
        public string ModId { get; set; }
        public string FileId { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
        public string Sha1 { get; set; }
        public DateTime Published { get; set; }
        public long Downloads { get; set; }
        public int ReleaseType { get; set; }
        public List<string> GameVersions { get; set; }
        public List<string> Loaders { get; set; }
        public List<ContentDependency> Dependencies { get; set; }

        public CurseForgeFile()
        {
            GameVersions = new List<string>();
            Loaders = new List<string>();
            Dependencies = new List<ContentDependency>();
        }
    }

    public sealed class ModpackInstallResult
    {
        public string VersionId { get; set; }
        public string VersionDirectory { get; set; }
        public string LoaderVersionId { get; set; }
    }
}
