using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MCDT
{
    internal sealed class ModFileIndex
    {
        private static readonly Regex TomlModId = new Regex(
            "^\\s*modId\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        private readonly string[] _jars;
        private readonly Dictionary<string, string> _hashCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _modIds;

        private ModFileIndex(string[] jars)
        {
            _jars = jars;
        }

        public static ModFileIndex Create(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return null;
            try
            {
                if (!Directory.Exists(directory))
                    return null;
                return new ModFileIndex(Directory.GetFiles(directory, "*.jar"));
            }
            catch
            {
                return null;
            }
        }

        public string FindInstalled(ContentVersion version, ContentProject project)
        {
            string fileName = version == null ? null : Path.GetFileName(version.FileName);
            if (!string.IsNullOrEmpty(fileName))
            {
                for (int i = 0; i < _jars.Length; i++)
                {
                    if (string.Equals(Path.GetFileName(_jars[i]), fileName, StringComparison.OrdinalIgnoreCase))
                        return Path.GetFileName(_jars[i]);
                }
            }

            List<string> ids = BuildIds(version, project);
            if (ids.Count > 0)
            {
                EnsureModIds();
                for (int i = 0; i < ids.Count; i++)
                {
                    string jar;
                    if (_modIds.TryGetValue(ids[i], out jar))
                        return jar;
                }
            }

            if (version != null && !string.IsNullOrEmpty(version.Sha1))
                return FindByHash(version.Sha1, version.Size);
            return null;
        }

        private static List<string> BuildIds(ContentVersion version, ContentProject project)
        {
            List<string> ids = new List<string>();
            if (project != null)
            {
                AddId(ids, project.Slug);
                AddId(ids, project.Id);
            }
            if (version != null)
                AddId(ids, version.ProjectId);
            return ids;
        }

        private static void AddId(List<string> ids, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            ids.Add(value);
        }

        private void EnsureModIds()
        {
            if (_modIds != null)
                return;
            _modIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _jars.Length; i++)
            {
                List<string> ids = ReadModIds(_jars[i]);
                string name = Path.GetFileName(_jars[i]);
                for (int j = 0; j < ids.Count; j++)
                    _modIds[ids[j]] = name;
            }
        }

        private static List<string> ReadModIds(string jarPath)
        {
            List<string> ids = new List<string>();
            try
            {
                using (FileStream stream = new FileStream(jarPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = archive.GetEntry("fabric.mod.json");
                    if (entry != null)
                    {
                        string text = ReadEntry(entry);
                        AddId(ids, JsonUtil.GetString(JsonUtil.AsObject(JsonUtil.Deserialize(text)), "id"));
                    }

                    entry = archive.GetEntry("quilt.mod.json");
                    if (entry != null)
                    {
                        string text = ReadEntry(entry);
                        Dictionary<string, object> quilt = JsonUtil.GetObject(
                            JsonUtil.AsObject(JsonUtil.Deserialize(text)), "quilt_loader");
                        AddId(ids, JsonUtil.GetString(quilt, "id"));
                    }

                    AddTomlIds(ids, archive.GetEntry("META-INF/mods.toml"));
                    AddTomlIds(ids, archive.GetEntry("META-INF/neoforge.mods.toml"));
                }
            }
            catch
            {
            }
            return ids;
        }

        private static void AddTomlIds(List<string> ids, ZipArchiveEntry entry)
        {
            if (entry == null)
                return;
            string text = ReadEntry(entry);
            if (string.IsNullOrEmpty(text))
                return;
            MatchCollection matches = TomlModId.Matches(text);
            for (int i = 0; i < matches.Count; i++)
                AddId(ids, matches[i].Groups[1].Value);
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            try
            {
                using (Stream stream = entry.Open())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        private string FindByHash(string sha1, long size)
        {
            for (int i = 0; i < _jars.Length; i++)
            {
                string path = _jars[i];
                try
                {
                    if (size > 0 && new FileInfo(path).Length != size)
                        continue;
                }
                catch
                {
                    continue;
                }
                if (string.Equals(ComputeSha1(path), sha1, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileName(path);
            }
            return null;
        }

        private string ComputeSha1(string path)
        {
            string cached;
            if (_hashCache.TryGetValue(path, out cached))
                return cached;
            string hash = null;
            try
            {
                using (SHA1 sha = SHA1.Create())
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] data = sha.ComputeHash(stream);
                    StringBuilder builder = new StringBuilder(data.Length * 2);
                    for (int i = 0; i < data.Length; i++)
                        builder.Append(data[i].ToString("x2"));
                    hash = builder.ToString();
                }
            }
            catch
            {
            }
            _hashCache[path] = hash;
            return hash;
        }
    }
}
