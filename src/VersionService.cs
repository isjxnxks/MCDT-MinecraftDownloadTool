using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MCDT
{
    public static class VersionService
    {
        private const string MirrorManifest = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";
        private const string OfficialManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        public static async Task<List<GameVersion>> GetGameVersionsAsync(Action<string> log, CancellationToken cancellationToken)
        {
            List<string> urls = new List<string> { MirrorManifest, OfficialManifest };
            string json = await HttpUtil.GetStringAsync(urls, log, cancellationToken).ConfigureAwait(false);
            Dictionary<string, object> root = JsonUtil.AsObject(JsonUtil.Deserialize(json));
            object[] versions = JsonUtil.GetArray(root, "versions");
            if (versions == null)
                throw new InstallException("版本列表格式不正确。");

            List<GameVersion> result = new List<GameVersion>();
            for (int i = 0; i < versions.Length; i++)
            {
                Dictionary<string, object> item = versions[i] as Dictionary<string, object>;
                if (item == null) continue;
                string id = JsonUtil.GetString(item, "id");
                string url = JsonUtil.GetString(item, "url");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url))
                    continue;
                result.Add(new GameVersion
                {
                    Id = id,
                    Type = JsonUtil.GetString(item, "type", "release"),
                    Url = url,
                    ReleaseTime = JsonUtil.ParseTime(JsonUtil.GetString(item, "releaseTime")),
                    Sha1 = JsonUtil.GetString(item, "sha1")
                });
            }

            result.Sort(delegate(GameVersion a, GameVersion b) { return b.ReleaseTime.CompareTo(a.ReleaseTime); });
            if (result.Count == 0)
                throw new InstallException("没有获取到可用的游戏版本。");
            return result;
        }
    }
}
