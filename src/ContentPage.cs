using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class ContentPage : UserControl
    {
        private static Color PageColor { get { return Theme.Page; } }
        private static Color AccentColor { get { return Theme.Accent; } }
        private static Color AccentHover { get { return Theme.AccentHover; } }
        private static Color TextColor { get { return Theme.Text; } }
        private static Color MutedColor { get { return Theme.Muted; } }
        private static Color BorderColor { get { return Theme.Border; } }
        private static Color DangerColor { get { return Theme.Danger; } }

        private readonly ContentKind _kind;
        private readonly Func<string> _gameDirProvider;
        private readonly Action _openSettings;
        private readonly DownloadTaskManager _taskManager;

        private Panel _filtersPanel;
        private TextBox _queryBox;
        private ComboBox _sourceCombo;
        private ComboBox _gameCombo;
        private ComboBox _loaderCombo;
        private Label _loaderLabel;
        private Label _targetHintLabel;
        private Button _importButton;
        private Button _searchButton;
        private Button _resetButton;
        private FlowLayoutPanel _resultsPanel;
        private Button _moreButton;
        private Label _moreHintLabel;

        private Panel _detailPanel;
        private Button _backButton;
        private Panel _detailHeaderPanel;
        private PictureBox _detailIcon;
        private Label _detailTitle;
        private Label _detailMeta;
        private Label _detailSummary;
        private Label _detailCount;
        private Button _openWebButton;
        private Button _openMcmodButton;
        private Button _copyButton;
        private ComboBox _detailGameCombo;
        private ComboBox _detailLoaderCombo;
        private Label _detailLoaderLabel;
        private FlowLayoutPanel _versionsPanel;

        private Panel _statusPanel;
        private Label _statusLabel;

        private List<GameVersion> _gameVersions = new List<GameVersion>();
        private readonly List<ContentProject> _projects = new List<ContentProject>();
        private List<ContentVersion> _allProjectVersions = new List<ContentVersion>();
        private ContentProject _currentProject;
        private string _gameDir;
        private bool _firstActivation = true;
        private bool _suppressComboEvents;
        private bool _dependencyCheckActive;
        private int _offset;
        private bool _hasMore;
        private int _searchRequestId;
        private int _versionRequestId;
        private int _prereqRequestId;
        private CancellationTokenSource _searchCts;
        private CancellationTokenSource _versionCts;
        private CancellationTokenSource _prereqCts;
        private Control _prereqSection;
        private readonly Stack<DetailHistoryEntry> _detailHistory = new Stack<DetailHistoryEntry>();
        private readonly Dictionary<string, List<ModDependency>> _prereqCache =
            new Dictionary<string, List<ModDependency>>(StringComparer.OrdinalIgnoreCase);

        public ContentPage(ContentKind kind, Func<string> gameDirProvider, Action openSettings,
            DownloadTaskManager taskManager)
        {
            _kind = kind;
            _gameDirProvider = gameDirProvider;
            _openSettings = openSettings;
            _taskManager = taskManager;
            BackColor = PageColor;
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = new Size(900, 452);

            BuildUi();
        }

        public Button SearchButton
        {
            get { return _searchButton; }
        }

        public void SetGameVersions(IList<GameVersion> versions)
        {
            _gameVersions = new List<GameVersion>();
            if (versions != null)
            {
                for (int i = 0; i < versions.Count; i++)
                    _gameVersions.Add(versions[i]);
            }
            RefreshGameVersionItems(_gameCombo);
            RefreshGameVersionItems(_detailGameCombo);
        }

        public void SetGameDir(string gameDir)
        {
            _gameDir = gameDir;
            RefreshTargets();
        }

        public void ApplyTheme()
        {
            Theme.Apply(this);
            if (_detailHistory != null)
            {
                foreach (DetailHistoryEntry entry in _detailHistory)
                {
                    if (entry == null || entry.Rows == null)
                        continue;
                    for (int i = 0; i < entry.Rows.Count; i++)
                        Theme.Apply(entry.Rows[i]);
                }
            }
            Invalidate(true);
        }

        public void OnActivated()
        {
            _gameDir = _gameDirProvider();
            RefreshGameVersionItems(_gameCombo);
            RefreshGameVersionItems(_detailGameCombo);
            RefreshTargets();
            if (_firstActivation)
            {
                _firstActivation = false;
                AutoSearch();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_searchCts != null) { try { _searchCts.Cancel(); } catch { } _searchCts.Dispose(); }
                if (_versionCts != null) { try { _versionCts.Cancel(); } catch { } _versionCts.Dispose(); }
            }
            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            _filtersPanel = new Panel();
            _filtersPanel.Location = new Point(0, 0);
            _filtersPanel.Size = new Size(900, 104);
            _filtersPanel.BackColor = PageColor;
            Controls.Add(_filtersPanel);

            Label title = AddLabel(_filtersPanel, _kind.DisplayName() + " 搜索", 24, 8, 11F, FontStyle.Bold, TextColor, true);

            AddLabel(_filtersPanel, "名称", 24, 40, 9F, FontStyle.Regular, TextColor, true);
            _queryBox = new TextBox();
            _queryBox.Location = new Point(70, 35);
            _queryBox.Size = new Size(386, 28);
            _queryBox.BorderStyle = BorderStyle.FixedSingle;
            _queryBox.BackColor = Theme.Surface;
            _queryBox.ForeColor = TextColor;
            _queryBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            _queryBox.KeyDown += QueryBox_KeyDown;
            _filtersPanel.Controls.Add(_queryBox);

            AddLabel(_filtersPanel, "来源", 468, 40, 9F, FontStyle.Regular, TextColor, true);
            _sourceCombo = CreateCombo(512, 35, 140);
            _sourceCombo.Items.Add("Modrinth");
            _sourceCombo.Items.Add("CurseForge");
            _sourceCombo.Items.Add("全部");
            _sourceCombo.SelectedIndex = 0;
            _filtersPanel.Controls.Add(_sourceCombo);

            _searchButton = CreateButton("搜索", 668, 34, 82, 30, true);
            _searchButton.Name = "contentSearchButton";
            _searchButton.Click += SearchButton_Click;
            _filtersPanel.Controls.Add(_searchButton);

            _resetButton = CreateButton("重置条件", 762, 34, 102, 30, false);
            _resetButton.Click += ResetButton_Click;
            _filtersPanel.Controls.Add(_resetButton);

            AddLabel(_filtersPanel, "游戏版本", 24, 78, 9F, FontStyle.Regular, TextColor, true);
            _gameCombo = CreateCombo(94, 73, 150);
            _gameCombo.Items.Add("全部");
            _gameCombo.SelectedIndex = 0;
            _filtersPanel.Controls.Add(_gameCombo);

            _loaderLabel = AddLabel(_filtersPanel, "加载器", 258, 78, 9F, FontStyle.Regular, TextColor, true);
            _loaderCombo = CreateCombo(312, 73, 110);
            _loaderCombo.Items.Add("全部");
            _loaderCombo.Items.Add("Fabric");
            _loaderCombo.Items.Add("Forge");
            _loaderCombo.Items.Add("NeoForge");
            _loaderCombo.Items.Add("Quilt");
            _loaderCombo.SelectedIndex = 0;
            _filtersPanel.Controls.Add(_loaderCombo);

            _targetHintLabel = AddLabel(_filtersPanel, "", 436, 78, 8.75F, FontStyle.Regular, MutedColor, true);

            if (_kind == ContentKind.Modpack)
            {
                _importButton = CreateButton("导入本地整合包", 730, 73, 146, 30, false);
                _importButton.Name = "importModpackButton";
                _importButton.Click += ImportModpack_Click;
                _filtersPanel.Controls.Add(_importButton);
            }

            _resultsPanel = new FlowLayoutPanel();
            _resultsPanel.Location = new Point(0, 104);
            _resultsPanel.Size = new Size(900, 280);
            _resultsPanel.BackColor = PageColor;
            _resultsPanel.AutoScroll = true;
            _resultsPanel.FlowDirection = FlowDirection.TopDown;
            _resultsPanel.WrapContents = false;
            _resultsPanel.Padding = new Padding(12, 8, 12, 8);
            Controls.Add(_resultsPanel);

            _moreButton = CreateButton("加载更多", 24, 386, 150, 30, false);
            _moreButton.Visible = false;
            _moreButton.Click += MoreButton_Click;
            Controls.Add(_moreButton);
            _moreHintLabel = AddLabel(this, "已显示全部结果", 186, 393, 8.25F, FontStyle.Regular, MutedColor, true);
            _moreHintLabel.Visible = false;

            BuildDetailUi();
            BuildDownloadUi();
            ApplyKindUi();
        }

        private void BuildDetailUi()
        {
            _detailPanel = new Panel();
            _detailPanel.Location = new Point(0, 0);
            _detailPanel.Size = new Size(900, 420);
            _detailPanel.BackColor = PageColor;
            _detailPanel.Visible = false;
            Controls.Add(_detailPanel);

            _backButton = CreateButton("← 返回", 20, 8, 90, 30, false);
            _backButton.Click += delegate { NavigateBack(); };
            _detailPanel.Controls.Add(_backButton);

            _detailHeaderPanel = new Panel();
            _detailHeaderPanel.Location = new Point(0, 42);
            _detailHeaderPanel.Size = new Size(900, 130);
            _detailHeaderPanel.BackColor = Theme.Surface;
            _detailHeaderPanel.Paint += DetailHeader_Paint;
            _detailPanel.Controls.Add(_detailHeaderPanel);

            _detailIcon = new PictureBox();
            _detailIcon.Location = new Point(16, 14);
            _detailIcon.Size = new Size(60, 60);
            _detailIcon.SizeMode = PictureBoxSizeMode.Zoom;
            _detailIcon.BackColor = Theme.AccentSoft;
            _detailIcon.Image = ImageCache.Placeholder;
            _detailHeaderPanel.Controls.Add(_detailIcon);

            _detailTitle = AddLabel(_detailHeaderPanel, "", 92, 8, 12F, FontStyle.Bold, TextColor, false);
            _detailTitle.Size = new Size(780, 26);
            _detailTitle.AutoEllipsis = true;

            _detailMeta = AddLabel(_detailHeaderPanel, "", 92, 34, 8.75F, FontStyle.Regular, MutedColor, false);
            _detailMeta.Size = new Size(780, 18);
            _detailMeta.AutoEllipsis = true;

            _detailSummary = AddLabel(_detailHeaderPanel, "", 92, 52, 9F, FontStyle.Regular, TextColor, false);
            _detailSummary.Size = new Size(780, 36);

            _openWebButton = CreateButton("转到 Modrinth", 92, 94, 130, 30, false);
            _openWebButton.Click += OpenWebButton_Click;
            _detailHeaderPanel.Controls.Add(_openWebButton);

            _openMcmodButton = CreateButton("转到 MC 百科", 230, 94, 110, 30, false);
            _openMcmodButton.Click += OpenMcmodButton_Click;
            _detailHeaderPanel.Controls.Add(_openMcmodButton);

            _copyButton = CreateButton("复制名称", 348, 94, 90, 30, false);
            _copyButton.Click += CopyButton_Click;
            _detailHeaderPanel.Controls.Add(_copyButton);

            Panel versionFilter = new Panel();
            versionFilter.Location = new Point(0, 176);
            versionFilter.Size = new Size(900, 38);
            versionFilter.BackColor = PageColor;
            _detailPanel.Controls.Add(versionFilter);

            AddLabel(versionFilter, "版本", 20, 8, 9F, FontStyle.Regular, TextColor, true);
            _detailGameCombo = CreateCombo(58, 4, 150);
            _detailGameCombo.Items.Add("全部");
            _detailGameCombo.SelectedIndex = 0;
            _detailGameCombo.SelectedIndexChanged += DetailFilterChanged;
            versionFilter.Controls.Add(_detailGameCombo);

            _detailLoaderLabel = AddLabel(versionFilter, "加载器", 220, 8, 9F, FontStyle.Regular, TextColor, true);
            _detailLoaderCombo = CreateCombo(268, 4, 110);
            _detailLoaderCombo.Items.Add("全部");
            _detailLoaderCombo.Items.Add("Fabric");
            _detailLoaderCombo.Items.Add("Forge");
            _detailLoaderCombo.Items.Add("NeoForge");
            _detailLoaderCombo.Items.Add("Quilt");
            _detailLoaderCombo.SelectedIndex = 0;
            _detailLoaderCombo.SelectedIndexChanged += DetailFilterChanged;
            versionFilter.Controls.Add(_detailLoaderCombo);

            _detailCount = AddLabel(versionFilter, "", 750, 8, 8.5F, FontStyle.Regular, MutedColor, false);
            _detailCount.Size = new Size(130, 20);
            _detailCount.TextAlign = ContentAlignment.MiddleRight;

            _versionsPanel = new FlowLayoutPanel();
            _versionsPanel.Location = new Point(0, 216);
            _versionsPanel.Size = new Size(900, 204);
            _versionsPanel.BackColor = PageColor;
            _versionsPanel.AutoScroll = true;
            _versionsPanel.FlowDirection = FlowDirection.TopDown;
            _versionsPanel.WrapContents = false;
            _versionsPanel.Padding = new Padding(12, 6, 12, 6);
            _detailPanel.Controls.Add(_versionsPanel);
        }

        private void BuildDownloadUi()
        {
            _statusPanel = new Panel();
            _statusPanel.Location = new Point(0, 420);
            _statusPanel.Size = new Size(900, 32);
            _statusPanel.BackColor = PageColor;
            Controls.Add(_statusPanel);

            Panel line = new Panel();
            line.Location = new Point(0, 0);
            line.Size = new Size(900, 1);
            line.BackColor = BorderColor;
            _statusPanel.Controls.Add(line);

            _statusLabel = AddLabel(_statusPanel, "准备就绪", 24, 8, 9F, FontStyle.Bold, TextColor, false);
            _statusLabel.Size = new Size(852, 20);
            _statusLabel.AutoEllipsis = true;
        }

        private void ApplyKindUi()
        {
            bool hasLoader = _kind.HasLoaderFilter();
            _loaderLabel.Visible = hasLoader;
            _loaderCombo.Visible = hasLoader;
            _detailLoaderLabel.Visible = hasLoader;
            _detailLoaderCombo.Visible = hasLoader;
            RefreshTargets();
        }

        public void RefreshTargets()
        {
            if (_targetHintLabel == null)
                return;
            string gameDir = string.IsNullOrEmpty(_gameDir) ? _gameDirProvider() : _gameDir;
            if (_kind == ContentKind.Modpack)
            {
                _targetHintLabel.Text = "安装为独立版本（版本隔离）";
                return;
            }
            bool isolation = AppSettings.VersionIsolation;
            if (!isolation && _kind != ContentKind.DataPack)
            {
                _targetHintLabel.Text = "下载到 .minecraft\\" + _kind.InstallFolder() + "（所有版本共用）";
                return;
            }
            int count = BuildInstallTargets(gameDir).Count;
            _targetHintLabel.Text = count > 0
                ? "点击下载后选择安装位置（" + count + " 个可选）"
                : "未找到可用的存档或版本";
        }

        private void RefreshGameVersionItems(ComboBox combo)
        {
            if (combo == null)
                return;
            string previous = combo.SelectedItem as string;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _suppressComboEvents = true;
            try
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                combo.Items.Add("全部");
                seen.Add("全部");
                for (int i = 0; i < _gameVersions.Count; i++)
                {
                    string id = _gameVersions[i].Id;
                    if (string.IsNullOrEmpty(id) || seen.Contains(id))
                        continue;
                    seen.Add(id);
                    combo.Items.Add(id);
                }
                string gameDir = string.IsNullOrEmpty(_gameDir) ? _gameDirProvider() : _gameDir;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    string versionsRoot = Path.Combine(gameDir, "versions");
                    try
                    {
                        if (Directory.Exists(versionsRoot))
                        {
                            string[] dirs = Directory.GetDirectories(versionsRoot);
                            for (int i = 0; i < dirs.Length; i++)
                            {
                                string id = Path.GetFileName(dirs[i]);
                                if (!seen.Contains(id))
                                {
                                    seen.Add(id);
                                    combo.Items.Add(id);
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                int index = 0;
                if (!string.IsNullOrEmpty(previous))
                {
                    for (int i = 0; i < combo.Items.Count; i++)
                    {
                        if (string.Equals(combo.Items[i] as string, previous, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                combo.SelectedIndex = index;
            }
            finally
            {
                combo.EndUpdate();
                _suppressComboEvents = false;
            }
        }

        private List<InstallTarget> BuildInstallTargets(string gameDir)
        {
            List<InstallTarget> targets = new List<InstallTarget>();
            if (string.IsNullOrEmpty(gameDir))
                return targets;
            bool isolation = AppSettings.VersionIsolation;

            if (_kind == ContentKind.DataPack)
            {
                targets.Add(new InstallTarget(
                    "仅下载到 .minecraft\\datapacks",
                    "不装入存档，供手动使用",
                    Path.Combine(gameDir, "datapacks"),
                    TargetIconKind.Folder));
                if (isolation)
                {
                    string versionsRoot = Path.Combine(gameDir, "versions");
                    List<string> versions = GetInstalledVersionIds(gameDir);
                    for (int i = 0; i < versions.Count; i++)
                    {
                        string savesRoot = Path.Combine(Path.Combine(versionsRoot, versions[i]), "saves");
                        List<string> worlds = GetSortedSubdirectories(savesRoot);
                        for (int j = 0; j < worlds.Count; j++)
                        {
                            string worldDir = Path.Combine(savesRoot, worlds[j]);
                            string path = Path.Combine(worldDir, "datapacks");
                            targets.Add(new InstallTarget(
                                worlds[j],
                                "存档 · " + versions[i],
                                path,
                                TargetIconKind.Save,
                                Path.Combine(worldDir, "icon.png")));
                        }
                    }
                }
                else
                {
                    string savesRoot = Path.Combine(gameDir, "saves");
                    List<string> worlds = GetSortedSubdirectories(savesRoot);
                    for (int i = 0; i < worlds.Count; i++)
                    {
                        string worldDir = Path.Combine(savesRoot, worlds[i]);
                        string path = Path.Combine(worldDir, "datapacks");
                        targets.Add(new InstallTarget(
                            worlds[i],
                            "存档 · 共用存档目录",
                            path,
                            TargetIconKind.Save,
                            Path.Combine(worldDir, "icon.png")));
                    }
                }
                return targets;
            }

            if (!isolation)
                return targets;

            string folder = _kind.InstallFolder();
            List<string> installed = GetInstalledVersionIds(gameDir);
            for (int i = 0; i < installed.Count; i++)
            {
                string path = Path.Combine(Path.Combine(Path.Combine(gameDir, "versions"), installed[i]), folder);
                VersionPresentation presentation = DescribeVersion(gameDir, installed[i]);
                targets.Add(new InstallTarget(
                    presentation.Title,
                    presentation.Subtitle,
                    path,
                    presentation.Icon));
            }
            return targets;
        }

        private sealed class VersionPresentation
        {
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public TargetIconKind Icon { get; set; }
        }

        private sealed class DetailHistoryEntry
        {
            public ContentProject Project { get; set; }
            public string GameFilter { get; set; }
            public string LoaderFilter { get; set; }
            public List<ContentVersion> Versions { get; set; }
            public List<Control> Rows { get; set; }
            public ContentVersion FirstVisible { get; set; }
            public int ShownCount { get; set; }
            public int ScrollY { get; set; }
        }

        private VersionPresentation DescribeVersion(string gameDir, string versionId)
        {
            string loaderName = null;
            string loaderVersion = null;
            string type = null;
            string gameVersion = null;
            string current = versionId;
            bool hasVanillaData = false;

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int depth = 0; depth < 8; depth++)
            {
                if (string.IsNullOrEmpty(current) || !visited.Add(current))
                {
                    if (string.IsNullOrEmpty(gameVersion))
                        gameVersion = current;
                    break;
                }

                Dictionary<string, object> json = ReadVersionJson(gameDir, current);
                if (json == null)
                {
                    if (string.IsNullOrEmpty(gameVersion))
                        gameVersion = current;
                    break;
                }

                string jsonType = JsonUtil.GetString(json, "type");
                if (!string.IsNullOrEmpty(jsonType))
                    type = jsonType;
                if (string.IsNullOrEmpty(loaderName))
                    DetectLoader(json, ref loaderName, ref loaderVersion);
                if (JsonUtil.GetObject(json, "downloads") != null || JsonUtil.GetValue(json, "assetIndex") != null)
                    hasVanillaData = true;

                string parent = JsonUtil.GetString(json, "inheritsFrom");
                if (string.IsNullOrEmpty(parent))
                {
                    gameVersion = current;
                    break;
                }
                current = parent;
            }

            if (string.IsNullOrEmpty(gameVersion))
                gameVersion = versionId;
            bool gameVersionIsPlaceholder = string.IsNullOrEmpty(gameVersion) ||
                string.Equals(gameVersion, versionId, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(loaderName) || string.IsNullOrEmpty(loaderVersion) || gameVersionIsPlaceholder)
            {
                string parsedGame = null;
                DetectLoaderFromId(versionId, ref loaderName, ref loaderVersion, ref parsedGame);
                if (!string.IsNullOrEmpty(parsedGame) &&
                    (string.IsNullOrEmpty(gameVersion) ||
                     string.Equals(gameVersion, versionId, StringComparison.OrdinalIgnoreCase)))
                    gameVersion = parsedGame;
            }
            if (!string.IsNullOrEmpty(loaderVersion) && !string.IsNullOrEmpty(gameVersion) &&
                loaderVersion.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase))
                loaderVersion = loaderVersion.Substring(gameVersion.Length + 1);

            bool gameVersionUseful = !string.IsNullOrEmpty(gameVersion) &&
                (!string.Equals(gameVersion, versionId, StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrEmpty(loaderName));
            string title = versionId;
            if (!string.IsNullOrEmpty(loaderName) && IsLoaderVersionId(versionId, loaderName))
            {
                title = (gameVersionUseful ? gameVersion + " " : "") + loaderName;
                if (!string.IsNullOrEmpty(loaderVersion))
                    title += " " + loaderVersion;
            }
            else if (string.IsNullOrEmpty(loaderName) && !string.IsNullOrEmpty(gameVersion) &&
                     string.Equals(versionId, gameVersion, StringComparison.OrdinalIgnoreCase))
            {
                title = gameVersion;
            }
            if (string.IsNullOrEmpty(title))
                title = versionId;

            string typeLabel = TypeLabel(type);
            string subtitle;
            if (!string.IsNullOrEmpty(typeLabel))
                subtitle = gameVersionUseful ? typeLabel + " " + gameVersion : typeLabel;
            else if (gameVersionUseful)
                subtitle = "已安装 · " + gameVersion;
            else
                subtitle = "已安装版本";
            if (!string.IsNullOrEmpty(loaderName))
            {
                subtitle += "，" + loaderName;
                if (!string.IsNullOrEmpty(loaderVersion))
                    subtitle += " " + loaderVersion;
            }

            TargetIconKind icon;
            if (!string.IsNullOrEmpty(loaderName))
                icon = LoaderIcon(loaderName);
            else if (hasVanillaData || IsKnownGameVersion(versionId))
                icon = TargetIconKind.Vanilla;
            else
                icon = TargetIconKind.Generic;

            return new VersionPresentation { Title = title, Subtitle = subtitle, Icon = icon };
        }

        private static bool IsLoaderVersionId(string versionId, string loaderName)
        {
            if (string.IsNullOrEmpty(versionId) || string.IsNullOrEmpty(loaderName))
                return false;

            string lower = versionId.ToLowerInvariant();
            if (string.Equals(loaderName, "Fabric", StringComparison.OrdinalIgnoreCase))
                return lower.StartsWith("fabric-loader-");
            if (string.Equals(loaderName, "Quilt", StringComparison.OrdinalIgnoreCase))
                return lower.StartsWith("quilt-loader-");
            if (string.Equals(loaderName, "NeoForge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(loaderName, "Forge", StringComparison.OrdinalIgnoreCase))
            {
                return Regex.IsMatch(versionId, @"(?i)^(.+?)[-_](?:neoforge|forge)[_-](.+)$") ||
                       Regex.IsMatch(versionId, @"(?i)^(?:neoforge|forge)[_-](.+)$");
            }
            return false;
        }

        private bool IsKnownGameVersion(string versionId)
        {
            for (int i = 0; i < _gameVersions.Count; i++)
            {
                if (string.Equals(_gameVersions[i].Id, versionId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static Dictionary<string, object> ReadVersionJson(string gameDir, string versionId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameDir) || string.IsNullOrEmpty(versionId))
                    return null;
                string path = Path.Combine(Path.Combine(Path.Combine(gameDir, "versions"), versionId), versionId + ".json");
                if (!File.Exists(path))
                    return null;
                return JsonUtil.AsObject(JsonUtil.Deserialize(File.ReadAllText(path, Encoding.UTF8)));
            }
            catch
            {
                return null;
            }
        }

        private static void DetectLoader(Dictionary<string, object> json, ref string loaderName, ref string loaderVersion)
        {
            object[] libraries = JsonUtil.GetArray(json, "libraries");
            bool hasNeoForge = false;
            string neoForgeVersion = null;
            bool hasForge = false;
            string forgeVersion = null;
            if (libraries != null)
            {
                for (int i = 0; i < libraries.Length; i++)
                {
                    Dictionary<string, object> library = libraries[i] as Dictionary<string, object>;
                    if (library == null)
                        continue;
                    string name = JsonUtil.GetString(library, "name");
                    if (string.IsNullOrEmpty(name))
                        continue;
                    string[] parts = name.Split(':');
                    if (parts.Length < 3)
                        continue;
                    string group = parts[0];
                    string artifact = parts[1];
                    string version = parts[2];
                    if (string.Equals(group, "net.fabricmc", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(artifact, "fabric-loader", StringComparison.OrdinalIgnoreCase))
                    {
                        loaderName = "Fabric";
                        loaderVersion = version;
                        return;
                    }
                    if (string.Equals(group, "org.quiltmc", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(artifact, "quilt-loader", StringComparison.OrdinalIgnoreCase))
                    {
                        loaderName = "Quilt";
                        loaderVersion = version;
                        return;
                    }
                    if (group.StartsWith("net.neoforged", StringComparison.OrdinalIgnoreCase))
                    {
                        hasNeoForge = true;
                        if (string.IsNullOrEmpty(neoForgeVersion) &&
                            (string.Equals(artifact, "neoforge", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(artifact, "fmlloader", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(artifact, "forge", StringComparison.OrdinalIgnoreCase)))
                            neoForgeVersion = version;
                        continue;
                    }
                    if (string.Equals(group, "net.minecraftforge", StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(artifact, "forge", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(artifact, "fmlloader", StringComparison.OrdinalIgnoreCase)))
                    {
                        hasForge = true;
                        if (string.IsNullOrEmpty(forgeVersion))
                            forgeVersion = version;
                    }
                }
            }

            if (hasNeoForge)
            {
                loaderName = "NeoForge";
                loaderVersion = neoForgeVersion;
                return;
            }
            if (hasForge)
            {
                loaderName = "Forge";
                loaderVersion = forgeVersion;
                return;
            }

            if (!string.IsNullOrEmpty(loaderName))
                return;
            string mainClass = JsonUtil.GetString(json, "mainClass");
            if (string.IsNullOrEmpty(mainClass))
                return;
            string lower = mainClass.ToLowerInvariant();
            if (lower.Contains("quilt"))
                loaderName = "Quilt";
            else if (lower.Contains("fabric"))
                loaderName = "Fabric";
            else if (lower.Contains("neoforge"))
                loaderName = "NeoForge";
            else if (lower.Contains("forge"))
                loaderName = "Forge";
        }

        private static void DetectLoaderFromId(string versionId, ref string loaderName, ref string loaderVersion, ref string gameVersion)
        {
            if (string.IsNullOrEmpty(versionId))
                return;
            string lower = versionId.ToLowerInvariant();
            bool fabricId = lower.StartsWith("fabric-loader-");
            bool quiltId = lower.StartsWith("quilt-loader-");

            if (!string.IsNullOrEmpty(loaderName))
            {
                if (string.Equals(loaderName, "Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    if (fabricId)
                        FillFabricVersionId(versionId, ref loaderVersion, ref gameVersion);
                    return;
                }
                if (string.Equals(loaderName, "Quilt", StringComparison.OrdinalIgnoreCase))
                {
                    if (quiltId)
                        FillQuiltVersionId(versionId, ref loaderVersion, ref gameVersion);
                    return;
                }
                if (string.Equals(loaderName, "NeoForge", StringComparison.OrdinalIgnoreCase))
                {
                    FillNeoForgeVersionId(versionId, ref loaderVersion, ref gameVersion);
                    return;
                }
                if (string.Equals(loaderName, "Forge", StringComparison.OrdinalIgnoreCase))
                {
                    FillForgeVersionId(versionId, ref loaderVersion, ref gameVersion);
                    return;
                }
                return;
            }

            if (fabricId)
            {
                loaderName = "Fabric";
                FillFabricVersionId(versionId, ref loaderVersion, ref gameVersion);
                return;
            }
            if (quiltId)
            {
                loaderName = "Quilt";
                FillQuiltVersionId(versionId, ref loaderVersion, ref gameVersion);
                return;
            }

            Match match = Regex.Match(versionId, @"(?i)^(.+?)[-_]neoforge[_-](.+)$");
            if (match.Success)
            {
                loaderName = "NeoForge";
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = match.Groups[1].Value;
                loaderVersion = match.Groups[2].Value;
                return;
            }
            match = Regex.Match(versionId, @"(?i)^neoforge[_-](.+)$");
            if (match.Success)
            {
                loaderName = "NeoForge";
                loaderVersion = match.Groups[1].Value;
                return;
            }
            match = Regex.Match(versionId, @"(?i)^(.+?)[-_]forge[_-](.+)$");
            if (match.Success)
            {
                loaderName = "Forge";
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = match.Groups[1].Value;
                loaderVersion = match.Groups[2].Value;
                return;
            }
            match = Regex.Match(versionId, @"(?i)^forge-(\d[\d.]*)-(.+)$");
            if (match.Success)
            {
                loaderName = "Forge";
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = match.Groups[1].Value;
                loaderVersion = match.Groups[2].Value;
                return;
            }

            if (lower.Contains("neoforge"))
                loaderName = "NeoForge";
            else if (lower.Contains("forge"))
                loaderName = "Forge";
        }

        private static void FillFabricVersionId(string versionId, ref string loaderVersion, ref string gameVersion)
        {
            string rest = versionId.Substring("fabric-loader-".Length);
            int dash = rest.LastIndexOf('-');
            if (dash > 0)
            {
                if (string.IsNullOrEmpty(loaderVersion))
                    loaderVersion = rest.Substring(0, dash);
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = rest.Substring(dash + 1);
            }
            else if (string.IsNullOrEmpty(loaderVersion))
            {
                loaderVersion = rest;
            }
        }

        private static void FillQuiltVersionId(string versionId, ref string loaderVersion, ref string gameVersion)
        {
            string rest = versionId.Substring("quilt-loader-".Length);
            int dash = rest.LastIndexOf('-');
            if (dash > 0)
            {
                if (string.IsNullOrEmpty(loaderVersion))
                    loaderVersion = rest.Substring(0, dash);
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = rest.Substring(dash + 1);
            }
            else if (string.IsNullOrEmpty(loaderVersion))
            {
                loaderVersion = rest;
            }
        }

        private static void FillNeoForgeVersionId(string versionId, ref string loaderVersion, ref string gameVersion)
        {
            Match match = Regex.Match(versionId, @"(?i)^(.+?)[-_](?:neoforge|forge)[_-](.+)$");
            if (match.Success)
            {
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = match.Groups[1].Value;
                if (string.IsNullOrEmpty(loaderVersion))
                    loaderVersion = match.Groups[2].Value;
                return;
            }
            match = Regex.Match(versionId, @"(?i)^(?:neoforge|forge)[_-](.+)$");
            if (match.Success && string.IsNullOrEmpty(loaderVersion))
                loaderVersion = match.Groups[1].Value;
        }

        private static void FillForgeVersionId(string versionId, ref string loaderVersion, ref string gameVersion)
        {
            Match match = Regex.Match(versionId, @"(?i)^(.+?)[-_]forge[_-](.+)$");
            if (match.Success)
            {
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = match.Groups[1].Value;
                if (string.IsNullOrEmpty(loaderVersion))
                    loaderVersion = match.Groups[2].Value;
                return;
            }
            match = Regex.Match(versionId, @"(?i)^forge-(\d[\d.]*)-(.+)$");
            if (match.Success)
            {
                if (string.IsNullOrEmpty(gameVersion))
                    gameVersion = match.Groups[1].Value;
                if (string.IsNullOrEmpty(loaderVersion))
                    loaderVersion = match.Groups[2].Value;
            }
        }

        private static TargetIconKind LoaderIcon(string loaderName)
        {
            if (string.Equals(loaderName, "Fabric", StringComparison.OrdinalIgnoreCase))
                return TargetIconKind.Fabric;
            if (string.Equals(loaderName, "Forge", StringComparison.OrdinalIgnoreCase))
                return TargetIconKind.Forge;
            if (string.Equals(loaderName, "NeoForge", StringComparison.OrdinalIgnoreCase))
                return TargetIconKind.NeoForge;
            if (string.Equals(loaderName, "Quilt", StringComparison.OrdinalIgnoreCase))
                return TargetIconKind.Quilt;
            return TargetIconKind.Generic;
        }

        private static string TypeLabel(string type)
        {
            if (string.IsNullOrEmpty(type))
                return "";
            if (string.Equals(type, "release", StringComparison.OrdinalIgnoreCase))
                return "正式版";
            if (string.Equals(type, "snapshot", StringComparison.OrdinalIgnoreCase))
                return "快照";
            if (string.Equals(type, "old_beta", StringComparison.OrdinalIgnoreCase))
                return "旧版 Beta";
            if (string.Equals(type, "old_alpha", StringComparison.OrdinalIgnoreCase))
                return "旧版 Alpha";
            return type;
        }

        private List<string> GetInstalledVersionIds(string gameDir)
        {
            List<string> result = new List<string>();
            string versionsRoot = Path.Combine(gameDir, "versions");
            if (!Directory.Exists(versionsRoot))
                return result;

            HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string[] dirs = Directory.GetDirectories(versionsRoot);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string id = Path.GetFileName(dirs[i]);
                    if (File.Exists(Path.Combine(dirs[i], id + ".json")))
                        installed.Add(id);
                }
            }
            catch
            {
            }

            for (int i = 0; i < _gameVersions.Count; i++)
            {
                string id = _gameVersions[i].Id;
                if (!string.IsNullOrEmpty(id) && installed.Remove(id))
                    result.Add(id);
            }
            List<string> rest = new List<string>(installed);
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            result.AddRange(rest);
            return result;
        }

        private static List<string> GetSortedSubdirectories(string root)
        {
            List<string> names = new List<string>();
            try
            {
                if (!Directory.Exists(root))
                    return names;
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++)
                    names.Add(Path.GetFileName(dirs[i]));
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
            }
            return names;
        }

        private string GetSelectedGameFilter(ComboBox combo)
        {
            if (combo == null)
                return "";
            string value = combo.SelectedItem as string;
            return value ?? "";
        }

        private string GetSelectedLoaderFilter()
        {
            if (!_kind.HasLoaderFilter())
                return "";
            string value = _loaderCombo.SelectedItem as string;
            return value ?? "";
        }

        private void AutoSearch()
        {
            AutoSearchAsync();
        }

        private async void AutoSearchAsync()
        {
            try
            {
                await RunSearchAsync(false, false);
            }
            catch
            {
            }
        }

        private async void SearchButton_Click(object sender, EventArgs e)
        {
            await RunSearchAsync(false, true);
        }

        private void QueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SearchButton_Click(sender, EventArgs.Empty);
            }
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            _queryBox.Text = "";
            _sourceCombo.SelectedIndex = 0;
            _gameCombo.SelectedIndex = 0;
            if (_loaderCombo.Visible && _loaderCombo.Items.Count > 0)
                _loaderCombo.SelectedIndex = 0;
        }

        private async void MoreButton_Click(object sender, EventArgs e)
        {
            await RunSearchAsync(true, true);
        }

        private bool EnsureCurseForgeKey()
        {
            if (AppSettings.HasCurseForgeKey)
                return true;
            DialogResult result = MessageBox.Show(FindForm(),
                "CurseForge 搜索需要 API Key。\n\n是否现在打开设置填写？（可免费申请）",
                "需要 CurseForge API Key", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes && _openSettings != null)
                _openSettings();
            return AppSettings.HasCurseForgeKey;
        }

        private async Task RunSearchAsync(bool append, bool userInitiated)
        {
            int sourceIndex = _sourceCombo.SelectedIndex;
            if (sourceIndex == 1 && !EnsureCurseForgeKey())
            {
                SetStatus("未设置 CurseForge API Key", DangerColor);
                return;
            }

            if (_searchCts != null)
            {
                try { _searchCts.Cancel(); } catch { }
                _searchCts.Dispose();
                _searchCts = null;
            }
            CancellationTokenSource cts = new CancellationTokenSource();
            _searchCts = cts;
            _searchRequestId++;
            int requestId = _searchRequestId;

            _searchButton.Enabled = false;
            _resetButton.Enabled = false;
            _queryBox.Enabled = false;
            _sourceCombo.Enabled = false;
            _gameCombo.Enabled = false;
            _loaderCombo.Enabled = false;
            _moreButton.Enabled = false;
            SetStatus("正在搜索 " + _kind.DisplayName() + "…", TextColor);

            string query = _queryBox.Text.Trim();
            string game = GetSelectedGameFilter(_gameCombo);
            string loader = GetSelectedLoaderFilter();
            int offset = append ? _offset : 0;
            int added = 0;
            bool hasMore = false;
            int total = 0;

            try
            {
                if (!append)
                {
                    ClearResults();
                    _projects.Clear();
                    _offset = 0;
                }

                if (sourceIndex == 2)
                {
                    Exception firstError = null;
                    bool anySuccess = false;
                    try
                    {
                        ContentSearchResult r = await ContentService.SearchAsync(ContentSource.Modrinth, _kind,
                            query, game, loader, offset, 20, LogSafe, cts.Token);
                        anySuccess = true;
                        AddResults(r.Projects);
                        added += r.Projects.Count;
                        total += r.Total;
                        hasMore = hasMore || r.HasMore;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        firstError = ex;
                        AppendLog("Modrinth 搜索失败：" + ex.Message);
                    }

                    if (AppSettings.HasCurseForgeKey)
                    {
                        try
                        {
                            ContentSearchResult r = await ContentService.SearchAsync(ContentSource.CurseForge, _kind,
                                query, game, loader, offset, 20, LogSafe, cts.Token);
                            anySuccess = true;
                            AddResults(r.Projects);
                            added += r.Projects.Count;
                            total += r.Total;
                            hasMore = hasMore || r.HasMore;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (firstError == null)
                                firstError = ex;
                            AppendLog("CurseForge 搜索失败：" + ex.Message);
                        }
                    }
                    else
                    {
                        AppendLog("未设置 CurseForge API Key，本次仅搜索 Modrinth。");
                    }

                    if (!anySuccess && firstError != null)
                        throw firstError;
                }
                else
                {
                    ContentSource source = sourceIndex == 1 ? ContentSource.CurseForge : ContentSource.Modrinth;
                    ContentSearchResult r = await ContentService.SearchAsync(source, _kind, query, game, loader,
                        offset, 20, LogSafe, cts.Token);
                    AddResults(r.Projects);
                    added = r.Projects.Count;
                    total = r.Total;
                    hasMore = r.HasMore;
                }

                if (requestId != _searchRequestId || cts.IsCancellationRequested)
                    return;

                _offset = offset + 20;
                _hasMore = hasMore;
                _moreButton.Visible = hasMore;
                _moreHintLabel.Visible = !hasMore && _projects.Count > 0;
                if (_projects.Count == 0)
                {
                    AddEmptyLabel("没有找到结果，换个关键词或把筛选条件改成“全部”试试。");
                    SetStatus("没有找到结果", MutedColor);
                }
                else
                {
                    SetStatus("找到 " + _projects.Count + " 个结果" +
                        (total > _projects.Count ? "（共约 " + total + " 个）" : ""), TextColor);
                    AppendLog("搜索完成：" + _kind.DisplayName() + " " + _projects.Count + " 个结果。");
                }
            }
            catch (OperationCanceledException)
            {
                if (requestId == _searchRequestId)
                    SetStatus("搜索已取消", MutedColor);
            }
            catch (Exception ex)
            {
                if (requestId != _searchRequestId)
                    return;
                SetStatus("搜索失败", DangerColor);
                AppendLog("搜索失败：" + ex.Message);
                if (userInitiated)
                {
                    MessageBox.Show(FindForm(), "搜索失败：\n\n" + ex.Message,
                        "搜索失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (requestId == _searchRequestId)
                {
                    _searchButton.Enabled = true;
                    _resetButton.Enabled = true;
                    _queryBox.Enabled = true;
                    _sourceCombo.Enabled = true;
                    _gameCombo.Enabled = true;
                    _loaderCombo.Enabled = true;
                    _moreButton.Enabled = true;
                }
                if (_searchCts == cts)
                    _searchCts = null;
                cts.Dispose();
            }
        }

        private void AddResults(List<ContentProject> projects)
        {
            if (projects == null)
                return;
            for (int i = 0; i < projects.Count; i++)
            {
                ContentProject project = projects[i];
                if (project == null)
                    continue;
                _projects.Add(project);
                ProjectRow row = new ProjectRow(project, OpenProject);
                _resultsPanel.Controls.Add(row);
                LoadRowIconAsync(row.IconBox, project.IconUrl);
            }
        }

        private async void LoadRowIconAsync(PictureBox box, string url)
        {
            if (box == null || box.IsDisposed)
                return;
            Image image = await ImageCache.GetAsync(url, CancellationToken.None);
            if (!box.IsDisposed)
                box.Image = image;
        }

        private void ClearResults()
        {
            while (_resultsPanel.Controls.Count > 0)
            {
                Control control = _resultsPanel.Controls[0];
                _resultsPanel.Controls.RemoveAt(0);
                control.Dispose();
            }
        }

        private void AddEmptyLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(840, 60);
            label.Margin = new Padding(0, 12, 0, 0);
            label.Font = new Font("Microsoft YaHei UI", 9F);
            label.ForeColor = MutedColor;
            label.TextAlign = ContentAlignment.MiddleCenter;
            _resultsPanel.Controls.Add(label);
        }

        private void OpenProject(ContentProject project)
        {
            if (project == null)
                return;
            ClearDetailHistory();
            string gameFilter = GetSelectedGameFilter(_gameCombo);
            string loaderFilter = _kind.HasLoaderFilter() ? GetSelectedLoaderFilter() : "";
            OpenProjectCore(project, gameFilter, loaderFilter, null);
        }

        private void OpenDependencyProject(ContentProject project)
        {
            if (project == null)
                return;
            if (_currentProject != null)
                _detailHistory.Push(CaptureDetailState());
            string gameFilter = GetSelectedGameFilter(_detailGameCombo);
            string loaderFilter = _kind.HasLoaderFilter() ? GetSelectedGameFilter(_detailLoaderCombo) : "";
            OpenProjectCore(project, gameFilter, loaderFilter, null);
        }

        private DetailHistoryEntry CaptureDetailState()
        {
            DetailHistoryEntry entry = new DetailHistoryEntry();
            entry.Project = _currentProject;
            entry.GameFilter = GetSelectedGameFilter(_detailGameCombo);
            entry.LoaderFilter = _kind.HasLoaderFilter() ? GetSelectedGameFilter(_detailLoaderCombo) : "";
            entry.Versions = new List<ContentVersion>(_allProjectVersions);
            entry.ScrollY = Math.Max(0, -_versionsPanel.AutoScrollPosition.Y);
            CancelPrerequisiteLoad();

            List<Control> rows = new List<Control>();
            List<Control> stale = new List<Control>();
            for (int i = 0; i < _versionsPanel.Controls.Count; i++)
            {
                Control control = _versionsPanel.Controls[i];
                if (control is VersionRow)
                    rows.Add(control);
                else
                    stale.Add(control);
            }
            _versionsPanel.SuspendLayout();
            try
            {
                _versionsPanel.Controls.Clear();
            }
            finally
            {
                _versionsPanel.ResumeLayout(false);
            }
            for (int i = 0; i < stale.Count; i++)
                stale[i].Dispose();
            _prereqSection = null;
            entry.Rows = rows;
            entry.ShownCount = rows.Count;
            entry.FirstVisible = rows.Count > 0 ? ((VersionRow)rows[0]).Version : null;
            return entry;
        }

        private async void OpenProjectCore(ContentProject project, string gameFilter, string loaderFilter,
            DetailHistoryEntry snapshot)
        {
            _currentProject = project;
            _filtersPanel.Visible = false;
            _resultsPanel.Visible = false;
            _moreButton.Visible = false;
            _moreHintLabel.Visible = false;
            _detailPanel.Visible = true;
            _detailPanel.BringToFront();

            _detailTitle.Text = project.Title;
            _detailMeta.Text = (string.IsNullOrEmpty(project.Author) ? "未知作者" : "by " + project.Author) +
                               " · " + project.Source.DisplayName() +
                               (string.IsNullOrEmpty(project.CompatText) ? "" : " · " + project.CompatText);
            _detailSummary.Text = string.IsNullOrEmpty(project.Description) ? "暂无简介。" : TrimText(project.Description, 360);
            _openWebButton.Text = project.Source == ContentSource.CurseForge ? "转到 CurseForge" : "转到 Modrinth";
            _detailIcon.Image = ImageCache.Placeholder;

            _suppressComboEvents = true;
            try
            {
                SelectComboText(_detailGameCombo, gameFilter);
                if (_kind.HasLoaderFilter())
                    SelectComboText(_detailLoaderCombo, string.IsNullOrEmpty(loaderFilter) ? "全部" : loaderFilter);
            }
            finally
            {
                _suppressComboEvents = false;
            }
            RefreshTargets();
            LoadDetailIconAsync(project);

            if (snapshot != null && snapshot.Versions != null && snapshot.Versions.Count > 0)
            {
                CancelVersionLoad();
                _allProjectVersions = new List<ContentVersion>(snapshot.Versions);
                if (snapshot.Rows != null && snapshot.Rows.Count > 0)
                    RestoreVersionRows(snapshot);
                else
                    ApplyVersionFilter();
                RestoreScrollPosition(snapshot.ScrollY);
                return;
            }

            await LoadProjectVersionsAsync(project);
        }

        private async void LoadDetailIconAsync(ContentProject project)
        {
            Image image = await ImageCache.GetAsync(project.IconUrl, CancellationToken.None);
            if (!IsDisposed && _currentProject == project)
                _detailIcon.Image = image;
        }

        private void RestoreScrollPosition(int scrollY)
        {
            if (scrollY <= 0)
                return;
            Action restore = delegate
            {
                if (!_versionsPanel.IsDisposed)
                    _versionsPanel.AutoScrollPosition = new Point(0, scrollY);
            };
            try
            {
                BeginInvoke(restore);
            }
            catch
            {
            }
        }

        private void NavigateBack()
        {
            while (_detailHistory.Count > 0)
            {
                DetailHistoryEntry entry = _detailHistory.Pop();
                if (entry.Project == null)
                {
                    DisposeHistoryRows(entry);
                    continue;
                }
                OpenProjectCore(entry.Project, entry.GameFilter, entry.LoaderFilter, entry);
                return;
            }
            CloseDetail();
        }

        private void ClearDetailHistory()
        {
            while (_detailHistory.Count > 0)
                DisposeHistoryRows(_detailHistory.Pop());
        }

        private static void DisposeHistoryRows(DetailHistoryEntry entry)
        {
            if (entry == null || entry.Rows == null)
                return;
            for (int i = 0; i < entry.Rows.Count; i++)
            {
                if (entry.Rows[i] != null && !entry.Rows[i].IsDisposed)
                    entry.Rows[i].Dispose();
            }
            entry.Rows = null;
        }

        private static void SelectComboText(ComboBox combo, string text)
        {
            if (combo == null)
                return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i] as string, text, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private void CloseDetail()
        {
            CancelVersionLoad();
            CancelPrerequisiteLoad();
            ClearDetailHistory();
            _currentProject = null;
            _detailPanel.Visible = false;
            _filtersPanel.Visible = true;
            _resultsPanel.Visible = true;
            _moreButton.Visible = _hasMore;
            _moreHintLabel.Visible = !_hasMore && _projects.Count > 0;
            SetStatus("准备就绪", TextColor);
        }

        private void CancelVersionLoad()
        {
            if (_versionCts != null)
            {
                try { _versionCts.Cancel(); } catch { }
                _versionCts.Dispose();
                _versionCts = null;
            }
            _versionRequestId++;
        }

        private void CancelPrerequisiteLoad()
        {
            if (_prereqCts != null)
            {
                try { _prereqCts.Cancel(); } catch { }
                _prereqCts.Dispose();
                _prereqCts = null;
            }
            _prereqRequestId++;
        }

        private async Task LoadProjectVersionsAsync(ContentProject project)
        {
            CancelVersionLoad();
            CancelPrerequisiteLoad();
            CancellationTokenSource cts = new CancellationTokenSource();
            _versionCts = cts;
            int requestId = _versionRequestId;

            ClearVersions();
            _detailCount.Text = "正在获取版本…";
            SetStatus("正在获取 " + project.Title + " 的版本…", TextColor);
            try
            {
                List<ContentVersion> versions = await ContentService.GetVersionsAsync(project,
                    GetSelectedGameFilter(_detailGameCombo),
                    _kind.HasLoaderFilter() ? GetSelectedGameFilter(_detailLoaderCombo) : "", LogSafe, cts.Token);
                if (requestId != _versionRequestId || cts.IsCancellationRequested)
                    return;
                _allProjectVersions = versions;
                ApplyVersionFilter();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (requestId != _versionRequestId)
                    return;
                _allProjectVersions = new List<ContentVersion>();
                ClearVersions();
                AddVersionEmpty("获取版本失败：" + ex.Message);
                _detailCount.Text = "获取失败";
                SetStatus("获取版本失败", DangerColor);
                AppendLog("获取版本失败：" + ex.Message);
            }
            finally
            {
                if (requestId == _versionRequestId)
                {
                    if (_detailPanel.Visible)
                        SetStatus("准备下载", TextColor);
                }
                if (_versionCts == cts)
                    _versionCts = null;
                cts.Dispose();
            }
        }

        private void DetailFilterChanged(object sender, EventArgs e)
        {
            if (_suppressComboEvents || _currentProject == null)
                return;
            LoadProjectVersionsForCurrentFilters();
        }

        private async void LoadProjectVersionsForCurrentFilters()
        {
            if (_currentProject == null)
                return;
            await LoadProjectVersionsAsync(_currentProject);
        }

        private void ApplyVersionFilter()
        {
            string game = GetSelectedGameFilter(_detailGameCombo);
            string loader = _kind.HasLoaderFilter() ? GetSelectedGameFilter(_detailLoaderCombo) : "";
            int shown = 0;
            ContentVersion firstVisible = null;
            List<Control> rows = new List<Control>();
            for (int i = 0; i < _allProjectVersions.Count; i++)
            {
                ContentVersion version = _allProjectVersions[i];
                if (!Matches(version.GameVersions, game))
                    continue;
                if (!Matches(version.Loaders, loader))
                    continue;
                if (firstVisible == null)
                    firstVisible = version;
                rows.Add(new VersionRow(version, _kind == ContentKind.Modpack ? "安装" : "下载",
                    StartDownload_Click));
                shown++;
            }

            CancelPrerequisiteLoad();
            ReplaceVersionRows(rows, shown == 0
                ? "没有符合当前筛选的版本，把游戏版本或加载器改成“全部”试试。"
                : null);

            _detailCount.Text = shown + " / " + _allProjectVersions.Count + " 个版本";
            SchedulePrerequisiteLoad(firstVisible, game, loader);
        }

        private void ReplaceVersionRows(List<Control> rows, string emptyText)
        {
            _versionsPanel.SuspendLayout();
            try
            {
                _prereqSection = null;
                Control[] old = new Control[_versionsPanel.Controls.Count];
                _versionsPanel.Controls.CopyTo(old, 0);
                _versionsPanel.Controls.Clear();
                if (rows != null && rows.Count > 0)
                    _versionsPanel.Controls.AddRange(rows.ToArray());
                if (emptyText != null)
                    _versionsPanel.Controls.Add(CreateVersionEmpty(emptyText));
                for (int i = 0; i < old.Length; i++)
                    old[i].Dispose();
            }
            finally
            {
                _versionsPanel.ResumeLayout(false);
                _versionsPanel.PerformLayout();
            }
        }

        private void RestoreVersionRows(DetailHistoryEntry snapshot)
        {
            CancelPrerequisiteLoad();
            _versionsPanel.SuspendLayout();
            try
            {
                _prereqSection = null;
                Control[] old = new Control[_versionsPanel.Controls.Count];
                _versionsPanel.Controls.CopyTo(old, 0);
                _versionsPanel.Controls.Clear();
                _versionsPanel.Controls.AddRange(snapshot.Rows.ToArray());
                for (int i = 0; i < old.Length; i++)
                    old[i].Dispose();
            }
            finally
            {
                _versionsPanel.ResumeLayout(false);
                _versionsPanel.PerformLayout();
            }
            snapshot.Rows = null;
            _detailCount.Text = snapshot.ShownCount + " / " + _allProjectVersions.Count + " 个版本";

            string game = GetSelectedGameFilter(_detailGameCombo);
            string loader = _kind.HasLoaderFilter() ? GetSelectedGameFilter(_detailLoaderCombo) : "";
            SchedulePrerequisiteLoad(snapshot.FirstVisible, game, loader);
        }

        private static bool Matches(List<string> values, string filter)
        {
            if (!IsRealFilter(filter))
                return true;
            if (values == null)
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], filter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsRealFilter(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.Equals(value, "全部", StringComparison.OrdinalIgnoreCase);
        }

        private void ClearVersions()
        {
            ReplaceVersionRows(null, null);
        }

        private Label CreateVersionEmpty(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Size = new Size(840, 50);
            label.Margin = new Padding(0, 8, 0, 0);
            label.ForeColor = MutedColor;
            label.TextAlign = ContentAlignment.MiddleCenter;
            return label;
        }

        private void AddVersionEmpty(string text)
        {
            _versionsPanel.Controls.Add(CreateVersionEmpty(text));
        }

        private static string PrerequisiteCacheKey(ContentProject project, ContentVersion version,
            string game, string loader)
        {
            return (project == null ? "" : project.Id) + "|" +
                   (version == null ? "" : version.Id) + "|" + game + "|" + loader;
        }

        private void SchedulePrerequisiteLoad(ContentVersion version, string game, string loader)
        {
            if (_kind != ContentKind.Mod || version == null || version.Dependencies == null ||
                version.Dependencies.Count == 0 || _currentProject == null)
                return;

            CancelPrerequisiteLoad();
            string cacheKey = PrerequisiteCacheKey(_currentProject, version, game, loader);
            List<ModDependency> cached;
            if (_prereqCache.TryGetValue(cacheKey, out cached))
            {
                InsertPrerequisiteSection(version, cached);
                return;
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            _prereqCts = cts;
            int requestId = _prereqRequestId;
            LoadPrerequisitesAsync(version, game, loader, cacheKey, requestId, cts);
        }

        private async void LoadPrerequisitesAsync(ContentVersion version, string game, string loader,
            string cacheKey, int requestId, CancellationTokenSource cts)
        {
            try
            {
                List<ModDependency> dependencies = await ContentService.ResolveModDependenciesAsync(
                    version, null, game, loader, LogSafe, cts.Token);
                if (requestId != _prereqRequestId || cts.IsCancellationRequested || _currentProject == null)
                    return;

                CachePrerequisites(cacheKey, dependencies);
                InsertPrerequisiteSection(version, dependencies);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (requestId == _prereqRequestId)
                {
                    AppendLog("前置资源获取失败：" + ex.Message);
                    InsertPrerequisiteError(version, "前置资源获取失败，请到源站查看依赖。");
                }
            }
            finally
            {
                if (_prereqCts == cts)
                    _prereqCts = null;
                cts.Dispose();
            }
        }

        private void CachePrerequisites(string cacheKey, List<ModDependency> dependencies)
        {
            if (string.IsNullOrEmpty(cacheKey))
                return;
            if (_prereqCache.Count >= 40)
                _prereqCache.Clear();
            _prereqCache[cacheKey] = dependencies ?? new List<ModDependency>();
        }

        private void InsertPrerequisiteSection(ContentVersion version, List<ModDependency> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0 || _currentProject == null ||
                _versionsPanel.IsDisposed)
                return;

            ClearPrerequisiteSection();
            Panel section = BuildPrerequisiteSection(version, dependencies);
            _prereqSection = section;
            _versionsPanel.Controls.Add(section);
            _versionsPanel.Controls.SetChildIndex(section, 0);
        }

        private void InsertPrerequisiteError(ContentVersion version, string message)
        {
            if (_currentProject == null || _versionsPanel.IsDisposed)
                return;
            ClearPrerequisiteSection();

            Panel section = new Panel();
            section.Width = 850;
            section.Height = 58;
            section.BackColor = Theme.SurfaceAlt;
            section.Margin = new Padding(0, 0, 0, 10);
            section.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawRectangle(pen, 0, 0, section.Width - 1, section.Height - 1);
            };

            Label header = new Label();
            header.Text = "前置资源";
            header.Location = new Point(12, 8);
            header.AutoSize = true;
            header.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            header.ForeColor = AccentColor;
            section.Controls.Add(header);

            Label note = new Label();
            note.Text = message ?? "前置资源获取失败。";
            note.Location = new Point(92, 10);
            note.Size = new Size(730, 18);
            note.Font = new Font("Microsoft YaHei UI", 8.25F);
            note.ForeColor = DangerColor;
            note.AutoEllipsis = true;
            section.Controls.Add(note);

            Label versionLabel = new Label();
            string versionText = version == null ? "" : version.ToString();
            versionLabel.Text = string.IsNullOrEmpty(versionText) ? "" : "版本 " + versionText;
            versionLabel.Location = new Point(12, 31);
            versionLabel.Size = new Size(810, 18);
            versionLabel.Font = new Font("Microsoft YaHei UI", 8.25F);
            versionLabel.ForeColor = MutedColor;
            versionLabel.AutoEllipsis = true;
            section.Controls.Add(versionLabel);

            _prereqSection = section;
            _versionsPanel.Controls.Add(section);
            _versionsPanel.Controls.SetChildIndex(section, 0);
        }

        private void ClearPrerequisiteSection()
        {
            if (_prereqSection != null && !_prereqSection.IsDisposed)
            {
                _versionsPanel.Controls.Remove(_prereqSection);
                _prereqSection.Dispose();
            }
            _prereqSection = null;
        }

        private Panel BuildPrerequisiteSection(ContentVersion version, List<ModDependency> dependencies)
        {
            const int headerHeight = 34;
            const int rowHeight = 58;

            Panel section = new Panel();
            section.Width = 850;
            section.Height = headerHeight + dependencies.Count * rowHeight + 6;
            section.BackColor = Theme.SurfaceAlt;
            section.Margin = new Padding(0, 0, 0, 10);
            section.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawRectangle(pen, 0, 0, section.Width - 1, section.Height - 1);
            };

            Label header = new Label();
            header.Text = "前置资源";
            header.Location = new Point(12, 7);
            header.AutoSize = true;
            header.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            header.ForeColor = AccentColor;
            section.Controls.Add(header);

            Label note = new Label();
            string versionText = version == null ? "" : version.ToString();
            note.Text = string.IsNullOrEmpty(versionText)
                ? "以下为最新匹配版本需要的前置 mod，点击可查看详情"
                : "以下为最新匹配版本 " + versionText + " 需要的前置 mod，点击可查看详情";
            note.Location = new Point(92, 9);
            note.Size = new Size(610, 18);
            note.Font = new Font("Microsoft YaHei UI", 8.25F);
            note.ForeColor = MutedColor;
            note.AutoEllipsis = true;
            section.Controls.Add(note);

            Label count = new Label();
            count.Text = "共 " + dependencies.Count + " 个";
            count.Location = new Point(708, 9);
            count.Size = new Size(130, 18);
            count.TextAlign = ContentAlignment.MiddleRight;
            count.Font = new Font("Microsoft YaHei UI", 8.25F);
            count.ForeColor = MutedColor;
            section.Controls.Add(count);

            for (int i = 0; i < dependencies.Count; i++)
            {
                ModDependency dependency = dependencies[i];
                DependencyRow row = new DependencyRow(dependency, OpenDependencyProject);
                row.Location = new Point(1, headerHeight + i * rowHeight);
                section.Controls.Add(row);
                LoadRowIconAsync(row.IconBox,
                    dependency != null && dependency.Project != null ? dependency.Project.IconUrl : null);
            }
            return section;
        }

        private void StartDownload_Click(ContentVersion version)
        {
            PrepareAndEnqueueDownload(version);
        }

        private async void PrepareAndEnqueueDownload(ContentVersion version)
        {
            if (version == null)
                return;
            string gameDir = _gameDirProvider();
            if (string.IsNullOrEmpty(gameDir))
            {
                MessageBox.Show(FindForm(), "还没有选择 .minecraft 目录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ContentProject project = _currentProject;
            string projectTitle = project != null && !string.IsNullOrEmpty(project.Title)
                ? project.Title
                : (version.FileName ?? _kind.DisplayName());
            string versionText = string.IsNullOrEmpty(version.VersionNumber) ? version.Name : version.VersionNumber;
            string pickerSubtitle = _kind.DisplayName() + " · " + projectTitle +
                                    (string.IsNullOrEmpty(versionText) ? "" : " " + versionText);

            string targetDir = null;
            string targetTitle = "";
            if (_kind == ContentKind.Modpack)
            {
                targetTitle = "独立版本";
            }
            else if (_kind == ContentKind.DataPack || AppSettings.VersionIsolation)
            {
                List<InstallTarget> targets = BuildInstallTargets(gameDir);
                if (targets.Count == 0)
                {
                    MessageBox.Show(FindForm(),
                        "没有可用的安装位置。请先在“游戏本体”页下载游戏版本，或到设置中把版本管理方式改为“不隔离”。",
                        "没有安装目标", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (TargetPickerForm picker = new TargetPickerForm(targets, pickerSubtitle))
                {
                    if (picker.ShowDialog(FindForm()) != DialogResult.OK || picker.SelectedTarget == null)
                        return;
                    targetTitle = picker.SelectedTarget.Title;
                    targetDir = picker.SelectedTarget.DirectoryPath;
                }
            }
            else
            {
                targetDir = Path.Combine(gameDir, _kind.InstallFolder());
                targetTitle = "所有版本共用";
            }

            string gameFilter = GetSelectedGameFilter(_detailPanel.Visible ? _detailGameCombo : _gameCombo);
            string loaderFilter = _kind.HasLoaderFilter()
                ? GetSelectedGameFilter(_detailPanel.Visible ? _detailLoaderCombo : _loaderCombo)
                : "";

            if (_kind == ContentKind.Mod && !string.IsNullOrEmpty(targetDir))
            {
                if (_dependencyCheckActive)
                {
                    SetStatus("正在检查上一个 mod 的依赖，请稍候…", MutedColor);
                    return;
                }
                List<ModDependency> dependencies = null;
                _dependencyCheckActive = true;
                try
                {
                    SetStatus("正在检查必需依赖…", TextColor);
                    UseWaitCursor = true;
                    dependencies = await ContentService.ResolveModDependenciesAsync(version, targetDir,
                        gameFilter, loaderFilter, LogSafe, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LogSafe("依赖检查失败，直接下载当前 mod：" + ex.Message);
                }
                finally
                {
                    UseWaitCursor = false;
                    _dependencyCheckActive = false;
                }

                if (IsDisposed)
                    return;

                if (dependencies != null && dependencies.Count > 0)
                {
                    ModDependency navigate = null;
                    SetStatus("此mod需要其他mod，请选择处理方式", TextColor);
                    using (ModDependencyForm dialog = new ModDependencyForm(projectTitle, dependencies,
                        delegate(ModDependency dep) { return EnqueueDependencyDownload(dep, targetDir, targetTitle); }))
                    {
                        DialogResult dialogResult = dialog.ShowDialog(FindForm());
                        navigate = dialog.NavigateToDependency;
                        if (navigate == null && dialogResult != DialogResult.OK)
                        {
                            SetStatus("已取消下载", MutedColor);
                            return;
                        }
                    }
                    if (navigate != null)
                    {
                        SetStatus("已跳转到依赖 mod：" + navigate.Title, AccentColor);
                        if (navigate.Project != null)
                            OpenDependencyProject(navigate.Project);
                        return;
                    }
                }
            }

            IList<GameVersion> knownVersions = _gameVersions;
            string taskTitle = _kind.DisplayName() + " · " + projectTitle +
                               (string.IsNullOrEmpty(versionText) ? "" : " " + versionText);
            string subtitle = _kind == ContentKind.Modpack
                ? "安装为独立启动实例"
                : (string.IsNullOrEmpty(targetTitle) ? targetDir : targetTitle + " · " + targetDir);

            _taskManager.Enqueue(taskTitle, subtitle, TaskIconForKind(), null,                delegate(DownloadTask current, CancellationToken token)
                {
                    if (_kind == ContentKind.Modpack)
                    {
                        current.Stage = "正在准备整合包…";
                        ModpackInstallResult result = ModpackInstaller.InstallAsync(gameDir, project, version,
                            knownVersions, current.Log, delegate(string text) { current.Stage = text; },
                            current.Stats, token).GetAwaiter().GetResult();
                        current.ResultText = "已安装为版本 " + result.VersionId;
                        current.Log("整合包实例目录：" + result.VersionDirectory);
                        return Task.FromResult(true);
                    }

                    Directory.CreateDirectory(targetDir);
                    current.Stage = "正在获取下载地址…";
                    current.Stats.Indeterminate = true;
                    DownloadItem item = ContentService.CreateFileItemAsync(version, targetDir,
                        current.Log, token).GetAwaiter().GetResult();
                    current.Stage = "正在下载 " + item.Name;
                    DownloadEngine fileEngine = new DownloadEngine(current.Log, current.Stats, 6);
                    fileEngine.RunAsync(new List<DownloadItem> { item }, token).GetAwaiter().GetResult();
                    current.ResultText = "已下载到 " + targetDir;
                    return Task.FromResult(true);
                });

            SetStatus("已加入下载队列：" + taskTitle + "（共 " + _taskManager.ActiveCount + " 个进行中或等待任务）",
                AccentColor);
        }

        private void ImportModpack_Click(object sender, EventArgs e)
        {
            string gameDir = string.IsNullOrEmpty(_gameDir) ? _gameDirProvider() : _gameDir;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                MessageBox.Show(FindForm(), "还没有选择 .minecraft 目录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string archivePath;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入本地整合包";
                dialog.Filter = "整合包 (*.mrpack;*.zip)|*.mrpack;*.zip|Modrinth 整合包 (*.mrpack)|*.mrpack|CurseForge 整合包 (*.zip)|*.zip";
                dialog.CheckFileExists = true;
                dialog.RestoreDirectory = true;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                    return;
                archivePath = dialog.FileName;
            }

            ModpackArchiveInfo info;
            try
            {
                info = ModpackInstaller.InspectArchive(archivePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "无法识别这个整合包：\n\n" + ex.Message, "导入本地整合包",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (info.IsCurseForge && !AppSettings.HasCurseForgeKey)
            {
                MessageBox.Show(FindForm(),
                    "这个 CurseForge 整合包需要 CurseForge API Key 才能下载其中的 mod。\n\n请先在设置中填写 API Key 后重试，或使用 Modrinth (.mrpack) 整合包。",
                    "需要 API Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = string.IsNullOrEmpty(info.Name) ? Path.GetFileNameWithoutExtension(archivePath) : info.Name;
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("名称：" + name);
            if (!string.IsNullOrEmpty(info.Version))
                summary.AppendLine("版本：" + info.Version);
            summary.AppendLine("格式：" + info.FormatName);
            summary.AppendLine("Minecraft：" + (string.IsNullOrEmpty(info.GameVersion) ? "未知" : info.GameVersion));
            summary.AppendLine("加载器：" + info.LoaderName +
                               (string.IsNullOrEmpty(info.LoaderVersion) ? "" : " " + info.LoaderVersion));
            summary.AppendLine();
            summary.AppendLine("将安装为 .minecraft\\versions 下的独立版本，源文件不会被移动或删除。");
            summary.Append("是否开始导入？");

            if (MessageBox.Show(FindForm(), summary.ToString(), "导入本地整合包",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            IList<GameVersion> knownVersions = _gameVersions;
            string taskTitle = "整合包导入 · " + name +
                               (string.IsNullOrEmpty(info.Version) ? "" : " " + info.Version);
            string subtitle = "本地导入 · " + info.FormatName + " · Minecraft " + info.GameVersion;
            string importPath = archivePath;
            _taskManager.Enqueue(taskTitle, subtitle, TargetIconKind.Generic, null,
                delegate(DownloadTask current, CancellationToken token)
                {
                    current.Stage = "正在导入整合包…";
                    ModpackInstallResult result = ModpackInstaller.ImportAsync(gameDir, importPath, name,
                        knownVersions, current.Log, delegate(string text) { current.Stage = text; },
                        current.Stats, token).GetAwaiter().GetResult();
                    current.ResultText = "已导入为版本 " + result.VersionId;
                    current.Log("整合包实例目录：" + result.VersionDirectory);
                    return Task.FromResult(true);
                });

            SetStatus("已加入导入队列：" + taskTitle + "（共 " + _taskManager.ActiveCount + " 个进行中或等待任务）",
                AccentColor);
        }

        private bool EnqueueDependencyDownload(ModDependency dependency, string targetDir, string targetTitle)
        {
            if (dependency == null || !dependency.CanDownload || string.IsNullOrEmpty(targetDir))
                return false;

            string name = string.IsNullOrEmpty(dependency.Title) ? "依赖 mod" : dependency.Title;
            string versionText = string.IsNullOrEmpty(dependency.Version.VersionNumber)
                ? dependency.Version.Name
                : dependency.Version.VersionNumber;
            string taskTitle = "Mod · " + name + (string.IsNullOrEmpty(versionText) ? "" : " " + versionText) + "（依赖）";
            string subtitle = (string.IsNullOrEmpty(targetTitle) ? "" : targetTitle + " · ") + targetDir;
            ContentVersion dependencyVersion = dependency.Version;

            _taskManager.Enqueue(taskTitle, subtitle, TargetIconKind.Generic, null,
                delegate(DownloadTask current, CancellationToken token)
                {
                    current.Stage = "正在获取下载地址…";
                    current.Stats.Indeterminate = true;
                    DownloadItem item = ContentService.CreateFileItemAsync(dependencyVersion, targetDir,
                        current.Log, token).GetAwaiter().GetResult();
                    current.Stage = "正在下载 " + item.Name;
                    DownloadEngine engine = new DownloadEngine(current.Log, current.Stats, 6);
                    engine.RunAsync(new List<DownloadItem> { item }, token).GetAwaiter().GetResult();
                    current.ResultText = "已下载到 " + targetDir;
                    return Task.FromResult(true);
                });

            SetStatus("已加入依赖下载：" + name + "（共 " + _taskManager.ActiveCount + " 个进行中或等待任务）",
                AccentColor);
            return true;
        }

        private TargetIconKind TaskIconForKind()
        {
            switch (_kind)
            {
                case ContentKind.DataPack: return TargetIconKind.Save;
                case ContentKind.Modpack: return TargetIconKind.Generic;
                case ContentKind.ResourcePack: return TargetIconKind.Folder;
                case ContentKind.Shader: return TargetIconKind.Generic;
                default: return TargetIconKind.Generic;
            }
        }

        private void SetStatus(string text, Color color)
        {
            if (_statusLabel == null || _statusLabel.IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string, Color>(SetStatus), text, color); } catch { }
                return;
            }
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void LogSafe(string text)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(LogSafe), text); } catch { }
                return;
            }
            AppendLog(text);
        }

        private void AppendLog(string text)
        {
            Debug.WriteLine("[Content] " + (text ?? ""));
        }

        private void OpenWebButton_Click(object sender, EventArgs e)
        {
            if (_currentProject == null)
                return;
            OpenUrl(ContentService.GetProjectWebUrl(_currentProject));
        }

        private void OpenMcmodButton_Click(object sender, EventArgs e)
        {
            if (_currentProject == null)
                return;
            OpenUrl(ContentService.GetMcmodSearchUrl(_currentProject.Title));
        }

        private void CopyButton_Click(object sender, EventArgs e)
        {
            if (_currentProject == null)
                return;
            try
            {
                Clipboard.SetText(_currentProject.Title ?? "");
                SetStatus("已复制名称：" + _currentProject.Title, AccentColor);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "复制失败：" + ex.Message, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(url));
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开浏览器：" + ex.Message, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string TrimText(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= max)
                return text;
            return text.Substring(0, max) + "…";
        }

        private static string FormatCount(long value)
        {
            if (value >= 100000000)
                return (value / 100000000.0).ToString("0.0") + " 亿";
            if (value >= 10000)
                return (value / 10000.0).ToString("0.0") + " 万";
            return value.ToString("N0");
        }

        private Label AddLabel(Control parent, string text, int x, int y, float size, FontStyle style, Color color, bool autoSize)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Font = new Font("Microsoft YaHei UI", size, style);
            label.ForeColor = color;
            label.AutoSize = autoSize;
            label.BackColor = Color.Transparent;
            parent.Controls.Add(label);
            return label;
        }

        private ComboBox CreateCombo(int x, int y, int width)
        {
            ComboBox combo = new ComboBox();
            combo.Location = new Point(x, y);
            combo.Size = new Size(width, 28);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = new Font("Microsoft YaHei UI", 9F);
            combo.BackColor = Theme.Surface;
            combo.ForeColor = TextColor;
            combo.DropDownHeight = 300;
            combo.IntegralHeight = false;
            return combo;
        }

        private Button CreateButton(string text, int x, int y, int width, int height, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Theme.BorderStrong;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Theme.ButtonHover;
            button.BackColor = primary ? AccentColor : Theme.Surface;
            button.ForeColor = primary ? Color.White : Theme.TextStrong;
            button.Font = new Font("Microsoft YaHei UI", primary ? 9.5F : 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void DetailHeader_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, _detailHeaderPanel.Width - 1, _detailHeaderPanel.Height - 1);
        }

        private sealed class ProjectRow : Panel
        {
            private readonly ContentProject _project;
            private readonly Action<ContentProject> _open;
            private readonly PictureBox _icon;

            public PictureBox IconBox
            {
                get { return _icon; }
            }

            public ProjectRow(ContentProject project, Action<ContentProject> open)
            {
                _project = project;
                _open = open;
                DoubleBuffered = true;
                BackColor = Theme.Surface;
                Width = 850;
                Height = 96;
                Margin = new Padding(0, 0, 0, 8);
                Cursor = Cursors.Hand;

                _icon = new PictureBox();
                _icon.Location = new Point(12, 14);
                _icon.Size = new Size(64, 64);
                _icon.SizeMode = PictureBoxSizeMode.Zoom;
                _icon.BackColor = Theme.AccentSoft;
                _icon.Image = ImageCache.Placeholder;
                _icon.Cursor = Cursors.Hand;
                Controls.Add(_icon);

                Label title = new Label();
                title.Text = project.Title ?? "";
                title.Location = new Point(88, 9);
                title.Size = new Size(650, 24);
                title.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                title.ForeColor = TextColor;
                title.AutoEllipsis = true;
                title.Cursor = Cursors.Hand;
                Controls.Add(title);

                Label badge = new Label();
                badge.Text = project.SourceLabel;
                badge.Location = new Point(746, 11);
                badge.Size = new Size(86, 20);
                badge.TextAlign = ContentAlignment.MiddleCenter;
                badge.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
                badge.ForeColor = Color.White;
                badge.BackColor = project.Source == ContentSource.CurseForge
                    ? Color.FromArgb(216, 121, 42)
                    : Theme.Accent;
                badge.Cursor = Cursors.Hand;
                Controls.Add(badge);

                Label description = new Label();
                description.Text = TrimText(project.Description, 170);
                description.Location = new Point(88, 34);
                description.Size = new Size(750, 20);
                description.Font = new Font("Microsoft YaHei UI", 8.75F);
                description.ForeColor = MutedColor;
                description.AutoEllipsis = true;
                description.Cursor = Cursors.Hand;
                Controls.Add(description);

                Label meta = new Label();
                string author = string.IsNullOrEmpty(project.Author) ? "未知作者" : project.Author;
                meta.Text = author + " · " + FormatCount(project.Downloads) + " 次下载" +
                            (project.Updated > DateTime.MinValue ? " · " + project.Updated.ToString("yyyy-MM-dd") : "");
                meta.Location = new Point(88, 56);
                meta.Size = new Size(660, 18);
                meta.Font = new Font("Microsoft YaHei UI", 8.25F);
                meta.ForeColor = Theme.MetaText;
                meta.AutoEllipsis = true;
                meta.Cursor = Cursors.Hand;
                Controls.Add(meta);

                Label compat = new Label();
                compat.Text = project.CompatText ?? "";
                compat.Location = new Point(88, 75);
                compat.Size = new Size(750, 17);
                compat.Font = new Font("Microsoft YaHei UI", 8F);
                compat.ForeColor = Theme.MetaText;
                compat.AutoEllipsis = true;
                compat.Cursor = Cursors.Hand;
                Controls.Add(compat);

                Label detail = new Label();
                detail.Text = "详情 ›";
                detail.Location = new Point(774, 72);
                detail.Size = new Size(60, 18);
                detail.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                detail.ForeColor = AccentColor;
                detail.TextAlign = ContentAlignment.MiddleRight;
                detail.Cursor = Cursors.Hand;
                Controls.Add(detail);

                AttachClick(this);
                AttachClick(title);
                AttachClick(description);
                AttachClick(meta);
                AttachClick(compat);
                AttachClick(badge);
                AttachClick(detail);
                AttachClick(_icon);
            }

            private void AttachClick(Control control)
            {
                control.Click += delegate { _open(_project); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private sealed class DependencyRow : Panel
        {
            private readonly ModDependency _dependency;
            private readonly PictureBox _icon;
            private readonly Action<ContentProject> _open;

            public PictureBox IconBox
            {
                get { return _icon; }
            }

            public DependencyRow(ModDependency dependency, Action<ContentProject> open)
            {
                _dependency = dependency;
                _open = open;
                DoubleBuffered = true;
                BackColor = Theme.Surface;
                Width = 848;
                Height = 58;
                bool clickable = dependency != null && dependency.Project != null && open != null;
                Cursor = clickable ? Cursors.Hand : Cursors.Default;

                _icon = new PictureBox();
                _icon.Location = new Point(12, 11);
                _icon.Size = new Size(36, 36);
                _icon.SizeMode = PictureBoxSizeMode.Zoom;
                _icon.BackColor = Theme.AccentSoft;
                _icon.Image = ImageCache.Placeholder;
                _icon.Cursor = Cursor;
                Controls.Add(_icon);

                Label title = new Label();
                title.Text = dependency == null || string.IsNullOrEmpty(dependency.Title)
                    ? "前置 mod"
                    : dependency.Title;
                title.Location = new Point(58, 7);
                title.Size = new Size(590, 20);
                title.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                title.ForeColor = TextColor;
                title.AutoEllipsis = true;
                title.Cursor = Cursor;
                Controls.Add(title);

                Label subtitle = new Label();
                string subtitleText = "";
                if (dependency != null)
                {
                    if (!string.IsNullOrEmpty(dependency.Error))
                        subtitleText = dependency.Error;
                    else if (!string.IsNullOrEmpty(dependency.Subtitle))
                        subtitleText = dependency.Subtitle;
                    else if (dependency.Version != null)
                        subtitleText = dependency.Version.ToString();
                }
                subtitle.Text = subtitleText;
                subtitle.Location = new Point(58, 28);
                subtitle.Size = new Size(590, 18);
                subtitle.Font = new Font("Microsoft YaHei UI", 8.25F);
                subtitle.ForeColor = dependency != null && !string.IsNullOrEmpty(dependency.Error)
                    ? DangerColor
                    : MutedColor;
                subtitle.AutoEllipsis = true;
                subtitle.Cursor = Cursor;
                Controls.Add(subtitle);

                if (dependency != null && dependency.Project != null)
                {
                    Label badge = new Label();
                    badge.Text = dependency.Project.SourceLabel;
                    badge.Location = new Point(660, 8);
                    badge.Size = new Size(84, 20);
                    badge.TextAlign = ContentAlignment.MiddleCenter;
                    badge.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
                    badge.ForeColor = Color.White;
                    badge.BackColor = dependency.Project.Source == ContentSource.CurseForge
                        ? Color.FromArgb(216, 121, 42)
                        : Theme.Accent;
                    badge.Cursor = Cursor;
                    Controls.Add(badge);

                    Label detail = new Label();
                    detail.Text = clickable ? "详情 ›" : "";
                    detail.Location = new Point(748, 31);
                    detail.Size = new Size(88, 18);
                    detail.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                    detail.ForeColor = clickable ? AccentColor : MutedColor;
                    detail.TextAlign = ContentAlignment.MiddleRight;
                    detail.Cursor = Cursor;
                    Controls.Add(detail);

                    if (clickable)
                    {
                        AttachClick(this);
                        AttachClick(_icon);
                        AttachClick(title);
                        AttachClick(subtitle);
                        AttachClick(badge);
                        AttachClick(detail);
                    }
                }
            }

            private void AttachClick(Control control)
            {
                control.Click += delegate { _open(_dependency.Project); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawLine(pen, 1, Height - 1, Width - 2, Height - 1);
            }
        }

        private sealed class VersionRow : Panel
        {
            private static readonly Font TitleFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            private static readonly Font MetaFont = new Font("Microsoft YaHei UI", 8.25F);
            private static readonly Font FileFont = new Font("Microsoft YaHei UI", 8F);
            private static readonly Font ButtonFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

            public ContentVersion Version { get; private set; }

            public VersionRow(ContentVersion version, string buttonText, Action<ContentVersion> download)
            {
                Version = version;
                DoubleBuffered = true;
                BackColor = Theme.Surface;
                Width = 850;
                Height = 82;
                Margin = new Padding(0, 0, 0, 6);

                string titleText = version.ToString();
                if (string.Equals(version.ReleaseType, "beta", StringComparison.OrdinalIgnoreCase))
                    titleText = "[Beta] " + titleText;
                else if (string.Equals(version.ReleaseType, "alpha", StringComparison.OrdinalIgnoreCase))
                    titleText = "[Alpha] " + titleText;

                Label title = new Label();
                title.Text = titleText;
                title.Location = new Point(12, 7);
                title.Size = new Size(700, 22);
                title.Font = TitleFont;
                title.ForeColor = TextColor;
                title.AutoEllipsis = true;
                Controls.Add(title);

                Label meta = new Label();
                string metaText = version.CompatibilityText;
                if (version.Published > DateTime.MinValue)
                    metaText += (metaText.Length > 0 ? " · " : "") + version.Published.ToString("yyyy-MM-dd");
                if (version.Downloads > 0)
                    metaText += " · " + FormatCount(version.Downloads) + " 次下载";
                if (version.Dependencies.Count > 0)
                    metaText += " · 必需依赖 " + version.Dependencies.Count;
                meta.Text = metaText;
                meta.Location = new Point(12, 29);
                meta.Size = new Size(710, 18);
                meta.Font = MetaFont;
                meta.ForeColor = MutedColor;
                meta.AutoEllipsis = true;
                Controls.Add(meta);

                Label file = new Label();
                string fileText = version.FileName ?? "";
                if (version.Size > 0)
                    fileText += (fileText.Length > 0 ? " · " : "") + PathUtil.FormatBytes(version.Size);
                if (string.IsNullOrEmpty(version.DownloadUrl))
                    fileText += (fileText.Length > 0 ? " · " : "") + "下载时获取地址";
                file.Text = fileText;
                file.Location = new Point(12, 50);
                file.Size = new Size(710, 18);
                file.Font = FileFont;
                file.ForeColor = Theme.FileText;
                file.AutoEllipsis = true;
                Controls.Add(file);

                Button button = new Button();
                button.Text = buttonText;
                button.Location = new Point(738, 23);
                button.Size = new Size(96, 34);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = AccentColor;
                button.ForeColor = Color.White;
                button.Font = ButtonFont;
                button.Cursor = Cursors.Hand;
                button.Click += delegate { download(version); };
                Controls.Add(button);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
