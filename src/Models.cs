using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace MCDT
{
    public enum LoaderKind
    {
        Vanilla,
        Fabric,
        Quilt,
        Forge,
        NeoForge
    }

    public static class LoaderKindExtensions
    {
        public static string DisplayName(this LoaderKind kind)
        {
            switch (kind)
            {
                case LoaderKind.Fabric: return "Fabric";
                case LoaderKind.Quilt: return "Quilt";
                case LoaderKind.Forge: return "Forge";
                case LoaderKind.NeoForge: return "NeoForge";
                default: return "原版";
            }
        }

        public static bool NeedsJavaInstaller(this LoaderKind kind)
        {
            return kind == LoaderKind.Forge || kind == LoaderKind.NeoForge;
        }
    }

    public sealed class GameVersion
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
        public DateTime ReleaseTime { get; set; }
        public string Sha1 { get; set; }

        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case "release": return "正式版";
                    case "snapshot": return "快照";
                    case "old_beta": return "远古 Beta";
                    case "old_alpha": return "远古 Alpha";
                    default: return Type;
                }
            }
        }

        public override string ToString()
        {
            return Id + "      " + TypeLabel + " · " + ReleaseTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    public sealed class LoaderVersionInfo
    {
        public string Version { get; set; }
        public bool Stable { get; set; }
        public bool Latest { get; set; }

        public override string ToString()
        {
            string suffix = Stable ? "" : "（测试版）";
            if (Latest)
                suffix += "  最新";
            return Version + suffix;
        }
    }

    public sealed class DownloadItem
    {
        public string Name { get; set; }
        public string DestPath { get; set; }
        public string Sha1 { get; set; }
        public long Size { get; set; }
        public List<string> Urls { get; set; }
        public bool Optional { get; set; }
        public int Index { get; set; }
        public long Weight { get; set; }

        public DownloadItem()
        {
            Urls = new List<string>();
        }
    }

    public sealed class InstallOptions
    {
        public string GameDir { get; set; }
        public GameVersion Game { get; set; }
        public LoaderKind Loader { get; set; }
        public string LoaderVersion { get; set; }
        public string TargetVersionId { get; set; }
    }

    public sealed class InstallResult
    {
        public string InstalledVersionId { get; set; }
        public string VersionDirectory { get; set; }
        public bool MirrorFallbackUsed { get; set; }
    }

    public sealed class InstallException : Exception
    {
        public InstallException(string message) : base(message) { }
        public InstallException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class JavaInfo
    {
        public string Path { get; set; }
        public int Major { get; set; }
        public string VersionText { get; set; }

        public override string ToString()
        {
            return "Java " + Major + " (" + Path + ")";
        }
    }

    /// <summary>Thread-safe counters shared by the download workers and the UI timer.</summary>
    public sealed class DownloadStats
    {
        private readonly object _sync = new object();
        private readonly Dictionary<int, long> _active = new Dictionary<int, long>();
        private int _totalFiles;
        private int _doneFiles;
        private long _receivedBytes;
        private long _totalUnits;
        private long _doneUnits;
        private long _plannedBytes;
        private volatile bool _indeterminate;

        public bool Indeterminate
        {
            get { return _indeterminate; }
            set { _indeterminate = value; }
        }

        public int TotalFiles
        {
            get { lock (_sync) { return _totalFiles; } }
        }

        public int DoneFiles
        {
            get { lock (_sync) { return _doneFiles; } }
        }

        public long ReceivedBytes
        {
            get { lock (_sync) { return _receivedBytes; } }
        }

        public long PlannedBytes
        {
            get { lock (_sync) { return _plannedBytes; } }
        }

        public double Fraction
        {
            get
            {
                lock (_sync)
                {
                    if (_totalUnits <= 0)
                        return 0;
                    long active = 0;
                    foreach (long value in _active.Values)
                        active += value;
                    double value2 = (double)(_doneUnits + active) / _totalUnits;
                    if (value2 < 0) return 0;
                    if (value2 > 1) return 1;
                    return value2;
                }
            }
        }

        public void Plan(IList<DownloadItem> items)
        {
            lock (_sync)
            {
                _totalFiles = items.Count;
                _doneFiles = 0;
                _receivedBytes = 0;
                _totalUnits = 0;
                _doneUnits = 0;
                _plannedBytes = 0;
                _active.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    DownloadItem item = items[i];
                    item.Index = i;
                    item.Weight = item.Size > 0 ? item.Size : 262144L;
                    _totalUnits += item.Weight;
                    if (item.Size > 0)
                        _plannedBytes += item.Size;
                }
            }
        }

        public void ReportProgress(int index, long bytes, long weight)
        {
            lock (_sync)
            {
                _active[index] = Math.Min(Math.Max(bytes, 0), weight);
            }
        }

        public void AddReceived(long bytes)
        {
            if (bytes <= 0) return;
            Interlocked.Add(ref _receivedBytes, bytes);
        }

        public void Complete(int index, long weight)
        {
            lock (_sync)
            {
                _active.Remove(index);
                _doneUnits += weight;
                _doneFiles++;
            }
        }

        public void ResetActive(int index)
        {
            lock (_sync)
            {
                _active.Remove(index);
            }
        }

        public void SetDoneAll()
        {
            lock (_sync)
            {
                _active.Clear();
                _doneFiles = _totalFiles;
                _doneUnits = _totalUnits;
            }
        }
    }
}
