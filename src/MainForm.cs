using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace MCDT
{
    public sealed class MainForm : Form
    {
        private static Color PageColor { get { return Theme.Page; } }
        private static Color AccentColor { get { return Theme.Accent; } }
        private static Color AccentHover { get { return Theme.AccentHover; } }
        private static Color AccentDown { get { return Theme.AccentDown; } }
        private static Color TextColor { get { return Theme.Text; } }
        private static Color MutedColor { get { return Theme.Muted; } }
        private static Color BorderColor { get { return Theme.Border; } }
        private static Color DangerColor { get { return Theme.Danger; } }

        private static readonly Font BrandFontBold = CreateBrandFont(20F, true);
        private static readonly Font BrandFontRegular = CreateBrandFont(20F, false);
        private static readonly string[] BrandSegments = new string[] { "M", "ine", "C", "raft", "D", "ownload", "T", "ool" };

        private readonly Label _pathHintLabel;
        private readonly Label _targetLabel;
        private readonly Label _targetSubLabel;
        private readonly Label _statusLabel;
        private readonly Label _detailLabel;
        private readonly Label _footerLabel;
        private readonly TextBox _pathBox;
        private readonly TextBox _logBox;
        private readonly ComboBox _gameCombo;
        private readonly ComboBox _loaderCombo;
        private readonly ComboBox _loaderVersionCombo;
        private readonly CheckBox _snapshotsCheck;
        private readonly Button _browseButton;
        private readonly Button _refreshButton;
        private readonly Button _startButton;
        private readonly ProgressBar _progressBar;
        private readonly Panel _minecraftPage;
        private readonly Panel _tabStrip;
        private readonly TaskPage _taskPage;
        private readonly DownloadTaskManager _taskManager;
        private readonly System.Windows.Forms.Timer _autoCloseTimer;
        private TabButton[] _tabs;
        private readonly ContentPage[] _contentPages;
        private Bitmap _headerIcon;
        private int _activeTab;

        private List<GameVersion> _allVersions = new List<GameVersion>();
        private string _gameDir;
        private bool _suppressEvents;
        private bool _manifestLoaded;
        private int _loaderRequestId;
        private int _autoCloseSeconds;
        private bool _autoClosing;
        private CancellationTokenSource _loaderCts;
        private CancellationTokenSource _manifestCts;

        public MainForm()
        {
            Text = "MineCraftDownloadTool";
            ClientSize = new Size(900, 600);
            BackColor = PageColor;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                using (System.Drawing.Icon appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    _headerIcon = appIcon.ToBitmap();
            }
            catch
            {
            }

            _taskManager = new DownloadTaskManager();
            _taskManager.TasksChanged += TaskManager_TasksChanged;
            _taskManager.QueueIdle += TaskManager_QueueIdle;

            Panel header = BuildHeader();
            Controls.Add(header);

            _pathBox = new TextBox();
            _pathBox.Name = "pathBox";
            _pathBox.Location = new Point(36, 132);
            _pathBox.Size = new Size(680, 28);
            _pathBox.ReadOnly = true;
            _pathBox.BackColor = Theme.Surface;
            _pathBox.ForeColor = TextColor;
            _pathBox.BorderStyle = BorderStyle.FixedSingle;
            _pathBox.Font = new Font("Microsoft YaHei UI", 9F);
            _pathBox.TabStop = false;
            Controls.Add(_pathBox);

            _browseButton = CreateSecondaryButton("浏览…", 728, 131, 136, 30);
            _browseButton.Name = "browseButton";
            _browseButton.Click += BrowseButton_Click;
            Controls.Add(_browseButton);

            _pathHintLabel = CreateLabel(this, "", 38, 164, 8.25F, FontStyle.Regular, MutedColor, false);
            _pathHintLabel.Name = "pathHintLabel";
            _pathHintLabel.AutoSize = true;

            Panel divider = new Panel();
            divider.Location = new Point(36, 186);
            divider.Size = new Size(828, 1);
            divider.BackColor = BorderColor;
            Controls.Add(divider);

            Label section = CreateLabel(this, "下载设置", 36, 190, 11F, FontStyle.Bold, TextColor, false);
            section.AutoSize = true;

            Label gameLabel = CreateLabel(this, "游戏版本", 36, 219, 9F, FontStyle.Regular, TextColor, false);
            gameLabel.AutoSize = true;

            _gameCombo = CreateComboBox(112, 214, 380);
            _gameCombo.Name = "gameVersionCombo";
            _gameCombo.SelectedIndexChanged += GameSelectionChanged;
            Controls.Add(_gameCombo);

            _snapshotsCheck = new ThemedCheckBox();
            _snapshotsCheck.Name = "snapshotsCheck";
            _snapshotsCheck.Text = "包含快照与旧版";
            _snapshotsCheck.Location = new Point(508, 216);
            _snapshotsCheck.Size = new Size(160, 24);
            _snapshotsCheck.ForeColor = TextColor;
            _snapshotsCheck.Font = new Font("Microsoft YaHei UI", 8.75F);
            _snapshotsCheck.Enabled = false;
            _snapshotsCheck.CheckedChanged += SnapshotsCheck_CheckedChanged;
            Controls.Add(_snapshotsCheck);

            _refreshButton = CreateSecondaryButton("刷新列表", 724, 214, 140, 32);
            _refreshButton.Name = "refreshButton";
            _refreshButton.Font = new Font("Microsoft YaHei UI", 8.5F);
            _refreshButton.Enabled = false;
            _refreshButton.Click += RefreshButton_Click;
            Controls.Add(_refreshButton);

            Label loaderLabel = CreateLabel(this, "模组加载器", 36, 255, 9F, FontStyle.Regular, TextColor, false);
            loaderLabel.AutoSize = true;

            _loaderCombo = CreateComboBox(112, 250, 180);
            _loaderCombo.Name = "loaderCombo";
            _loaderCombo.Items.Add(new LoaderChoice(LoaderKind.Vanilla, "原版"));
            _loaderCombo.Items.Add(new LoaderChoice(LoaderKind.Fabric, "Fabric"));
            _loaderCombo.Items.Add(new LoaderChoice(LoaderKind.Quilt, "Quilt"));
            _loaderCombo.Items.Add(new LoaderChoice(LoaderKind.Forge, "Forge"));
            _loaderCombo.Items.Add(new LoaderChoice(LoaderKind.NeoForge, "NeoForge"));
            _loaderCombo.SelectedIndex = 0;
            _loaderCombo.Enabled = false;
            _loaderCombo.SelectedIndexChanged += LoaderSelectionChanged;
            Controls.Add(_loaderCombo);

            Label loaderVersionLabel = CreateLabel(this, "加载器版本", 310, 255, 9F, FontStyle.Regular, TextColor, false);
            loaderVersionLabel.AutoSize = true;

            _loaderVersionCombo = CreateComboBox(394, 250, 470);
            _loaderVersionCombo.Name = "loaderVersionCombo";
            _loaderVersionCombo.Enabled = false;
            _loaderVersionCombo.SelectedIndexChanged += LoaderVersionChanged;
            Controls.Add(_loaderVersionCombo);

            Panel infoPanel = new Panel();
            infoPanel.Name = "targetPanel";
            infoPanel.Location = new Point(36, 288);
            infoPanel.Size = new Size(828, 52);
            infoPanel.BackColor = Theme.AccentSoft;
            infoPanel.Paint += InfoPanel_Paint;
            Controls.Add(infoPanel);

            _targetLabel = new Label();
            _targetLabel.Name = "targetLabel";
            _targetLabel.Location = new Point(16, 8);
            _targetLabel.Size = new Size(796, 21);
            _targetLabel.AutoEllipsis = true;
            _targetLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _targetLabel.ForeColor = Theme.InfoTitle;
            _targetLabel.Text = "安装位置：请选择游戏版本";
            _targetLabel.BackColor = Color.Transparent;
            infoPanel.Controls.Add(_targetLabel);

            _targetSubLabel = new Label();
            _targetSubLabel.Name = "targetSubLabel";
            _targetSubLabel.Location = new Point(16, 29);
            _targetSubLabel.Size = new Size(796, 17);
            _targetSubLabel.AutoEllipsis = true;
            _targetSubLabel.Font = new Font("Microsoft YaHei UI", 8.25F);
            _targetSubLabel.ForeColor = Theme.InfoSubtitle;
            _targetSubLabel.Text = "游戏本体和加载器都会写入 .minecraft\\versions";
            _targetSubLabel.BackColor = Color.Transparent;
            infoPanel.Controls.Add(_targetSubLabel);

            _startButton = new Button();
            _startButton.Name = "startButton";
            _startButton.Text = "开始下载";
            _startButton.Location = new Point(36, 348);
            _startButton.Size = new Size(220, 42);
            _startButton.FlatStyle = FlatStyle.Flat;
            _startButton.FlatAppearance.BorderSize = 0;
            _startButton.FlatAppearance.MouseOverBackColor = AccentHover;
            _startButton.FlatAppearance.MouseDownBackColor = AccentDown;
            _startButton.BackColor = AccentColor;
            _startButton.ForeColor = Color.White;
            _startButton.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            _startButton.Cursor = Cursors.Hand;
            _startButton.Enabled = false;
            _startButton.Click += StartButton_Click;
            Controls.Add(_startButton);

            Label queueHint = CreateLabel(this, "任务会加入下载队列，按顺序执行，可在“任务”页查看进度", 268, 360, 8.5F, FontStyle.Regular, MutedColor, false);
            queueHint.Size = new Size(596, 18);

            _statusLabel = CreateLabel(this, "准备就绪", 36, 400, 10F, FontStyle.Bold, TextColor, false);
            _statusLabel.Name = "statusLabel";
            _statusLabel.Size = new Size(828, 22);
            _statusLabel.AutoSize = false;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            _detailLabel = CreateLabel(this, "正在定位 .minecraft…", 36, 422, 8.25F, FontStyle.Regular, MutedColor, false);
            _detailLabel.Name = "detailLabel";
            _detailLabel.Size = new Size(828, 18);
            _detailLabel.AutoSize = false;
            _detailLabel.AutoEllipsis = true;

            _progressBar = new ThemedProgressBar();
            _progressBar.Name = "progressBar";
            _progressBar.Location = new Point(36, 444);
            _progressBar.Size = new Size(828, 12);
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 1000;
            _progressBar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_progressBar);

            Label logTitle = CreateLabel(this, "运行日志", 36, 462, 9F, FontStyle.Bold, TextColor, false);
            logTitle.AutoSize = true;

            _logBox = new TextBox();
            _logBox.Name = "logBox";
            _logBox.Location = new Point(36, 482);
            _logBox.Size = new Size(828, 80);
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BackColor = Theme.Surface;
            _logBox.ForeColor = Theme.LogText;
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.Font = new Font("Microsoft YaHei UI", 8.25F);
            _logBox.TabStop = false;
            Controls.Add(_logBox);

            _minecraftPage = new Panel();
            _minecraftPage.Name = "minecraftPage";
            _minecraftPage.Location = new Point(0, 120);
            _minecraftPage.Size = new Size(900, 452);
            _minecraftPage.BackColor = PageColor;
            MoveControlsIntoPage(header, _minecraftPage, 120);
            Controls.Add(_minecraftPage);

            _footerLabel = CreateLabel(this, "下载源：BMCLAPI 镜像优先 · 失败自动切换官方源　|　v1.0.0", 36, 578, 8.25F, FontStyle.Regular, MutedColor, false);
            _footerLabel.Name = "footerLabel";
            _footerLabel.Size = new Size(828, 18);
            _footerLabel.AutoSize = false;

            _taskPage = new TaskPage(_taskManager);
            _taskPage.Location = new Point(0, 120);
            _taskPage.Size = new Size(900, 452);
            _taskPage.Visible = false;
            Controls.Add(_taskPage);

            _contentPages = new ContentPage[5];
            ContentKind[] kinds = new ContentKind[]
            {
                ContentKind.Mod, ContentKind.Modpack, ContentKind.ResourcePack,
                ContentKind.Shader, ContentKind.DataPack
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                ContentPage page = new ContentPage(kinds[i], GetGameDir, OpenSettings, _taskManager);
                page.Location = new Point(0, 120);
                page.Size = new Size(900, 452);
                page.Visible = false;
                Controls.Add(page);
                _contentPages[i] = page;
            }

            _tabStrip = BuildTabStrip();
            Controls.Add(_tabStrip);
            _tabStrip.BringToFront();

            _activeTab = -1;
            ShowTab(0);

            _autoCloseTimer = new System.Windows.Forms.Timer();
            _autoCloseTimer.Interval = 1000;
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;

            AcceptButton = _startButton;
            Shown += MainForm_Shown;
            FormClosing += MainForm_FormClosing;
            FormClosed += MainForm_FormClosed;

            ApplyTheme();
        }

        private Panel BuildTabStrip()
        {
            Panel strip = new Panel();
            strip.Name = "tabStrip";
            strip.Location = new Point(0, 80);
            strip.Size = new Size(900, 40);
            strip.BackColor = Theme.Surface;
            strip.Paint += TabStrip_Paint;

            string[] names = new string[] { "游戏本体", "任务", "Mod", "整合包", "资源包", "光影包", "数据包" };
            int[] lefts = new int[] { 12, 128, 220, 312, 416, 520, 624 };
            int[] widths = new int[] { 92, 68, 68, 80, 80, 80, 80 };
            _tabs = new TabButton[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                TabButton tab = new TabButton(names[i]);
                tab.Location = new Point(lefts[i], 3);
                tab.Size = new Size(widths[i], 34);
                int index = i;
                tab.Click += delegate { ShowTab(index); };
                strip.Controls.Add(tab);
                _tabs[i] = tab;
            }

            Button settings = CreateSecondaryButton("设置", 794, 5, 82, 30);
            settings.Name = "settingsButton";
            settings.Click += delegate { OpenSettings(); };
            strip.Controls.Add(settings);
            return strip;
        }

        private void TabStrip_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(BorderColor))
                e.Graphics.DrawLine(pen, 0, _tabStrip.Height - 1, _tabStrip.Width, _tabStrip.Height - 1);
        }

        private void MoveControlsIntoPage(Control keep, Panel page, int offsetY)
        {
            List<Control> moving = new List<Control>();
            for (int i = 0; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                if (control == keep)
                    continue;
                moving.Add(control);
            }
            for (int i = 0; i < moving.Count; i++)
            {
                Control control = moving[i];
                control.Location = new Point(control.Left, control.Top - offsetY);
                page.Controls.Add(control);
            }
        }

        private void ShowTab(int index)
        {
            if (index < 0 || index > 6)
                return;
            if (_activeTab == index)
                return;

            _activeTab = index;
            _minecraftPage.Visible = index == 0;
            _taskPage.Visible = index == 1;
            for (int i = 0; i < _contentPages.Length; i++)
                _contentPages[i].Visible = (i + 2) == index;
            if (_tabs != null)
            {
                for (int i = 0; i < _tabs.Length; i++)
                    _tabs[i].SetSelected(i == index);
            }

            if (index == 0)
            {
                AcceptButton = _startButton;
                return;
            }
            if (index == 1)
            {
                AcceptButton = null;
                return;
            }

            ContentPage page = _contentPages[index - 2];
            AcceptButton = page.SearchButton;
            page.OnActivated();
        }

        private string GetGameDir()
        {
            return _gameDir;
        }

        private void OpenSettings()
        {
            using (SettingsForm form = new SettingsForm(ApplyTheme))
                form.ShowDialog(this);
            ApplyTheme();
            if (_contentPages != null)
            {
                for (int i = 0; i < _contentPages.Length; i++)
                    _contentPages[i].RefreshTargets();
            }
            if (!AppSettings.AutoCloseAfterDownload && _autoCloseTimer != null && _autoCloseTimer.Enabled)
            {
                _autoCloseTimer.Stop();
                _footerLabel.Text = DefaultFooterText();
                Text = "MineCraftDownloadTool";
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowChrome(this);
        }

        private void ApplyTheme()
        {
            Theme.Apply(this);
            if (_tabs != null)
            {
                for (int i = 0; i < _tabs.Length; i++)
                    _tabs[i].SetSelected(i == _activeTab);
            }
            if (_taskPage != null)
                _taskPage.ApplyTheme();
            if (_contentPages != null)
            {
                for (int i = 0; i < _contentPages.Length; i++)
                    _contentPages[i].ApplyTheme();
            }
            Theme.ApplyWindowChrome(this);
            Invalidate(true);
        }

        private void SyncGameDirToPages()
        {
            if (_contentPages == null)
                return;
            for (int i = 0; i < _contentPages.Length; i++)
                _contentPages[i].SetGameDir(_gameDir);
        }

        private void SyncGameVersionsToPages()
        {
            if (_contentPages == null)
                return;
            for (int i = 0; i < _contentPages.Length; i++)
                _contentPages[i].SetGameVersions(_allVersions);
        }

        private Panel BuildHeader()
        {
            Panel header = new Panel();
            header.Name = "header";
            header.Location = new Point(0, 0);
            header.Size = new Size(ClientSize.Width, 80);
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Paint += Header_Paint;
            header.BackColor = AccentColor;

            Label subtitle = new Label();
            subtitle.Text = "游戏本体 · Mod · 整合包 · 资源包 · 光影 · 数据包";
            subtitle.Location = new Point(98, 48);
            subtitle.AutoSize = true;
            subtitle.Font = new Font("Microsoft YaHei UI", 9F);
            subtitle.ForeColor = Theme.HeaderSubtitle;
            subtitle.BackColor = Color.Transparent;
            header.Controls.Add(subtitle);

            Label badge = new Label();
            badge.Text = "轻量单文件";
            badge.Location = new Point(783, 25);
            badge.Size = new Size(87, 24);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Font = new Font("Microsoft YaHei UI", 8.25F);
            badge.ForeColor = Theme.HeaderBadgeText;
            badge.BackColor = Theme.HeaderBadgeBack;
            header.Controls.Add(badge);

            return header;
        }

        private void Header_Paint(object sender, PaintEventArgs e)
        {
            Panel header = (Panel)sender;
            using (LinearGradientBrush brush = new LinearGradientBrush(header.ClientRectangle,
                Theme.HeaderStart, Theme.HeaderEnd, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, header.ClientRectangle);
            }

            DrawBrandTitle(e.Graphics, 96, 8);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle logo = new Rectangle(30, 15, 50, 50);
            using (GraphicsPath path = RoundedRectangle(logo, 12))
            using (SolidBrush fill = new SolidBrush(Theme.HeaderLogoBack))
            {
                e.Graphics.FillPath(fill, path);
            }

            if (_headerIcon != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(_headerIcon, new Rectangle(35, 20, 40, 40));
            }
        }

        private static Font CreateBrandFont(float size, bool emphasis)
        {
            string family = emphasis ? "Segoe UI" : "Segoe UI Semibold";
            FontStyle style = emphasis ? FontStyle.Bold : FontStyle.Regular;
            Font font = new Font(family, size, style, GraphicsUnit.Point);
            if (string.Equals(font.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
                return font;
            font.Dispose();
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        private static void DrawBrandTitle(Graphics graphics, int left, int top)
        {
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (StringFormat format = StringFormat.GenericTypographic)
            {
                float x = left;
                for (int i = 0; i < BrandSegments.Length; i++)
                {
                    Font font = i % 2 == 0 ? BrandFontBold : BrandFontRegular;
                    string segment = BrandSegments[i];
                    graphics.DrawString(segment, font, brush, x, top, format);
                    x += graphics.MeasureString(segment, BrandFontRegular, PointF.Empty, format).Width;
                }
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void InfoPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = (Panel)sender;
            using (Pen pen = new Pen(Theme.InfoBorder))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            }
        }

        private Label CreateLabel(Control parent, string text, int x, int y, float size, FontStyle style, Color color, bool autoSize)
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

        private ComboBox CreateComboBox(int x, int y, int width)
        {
            ComboBox combo = new ComboBox();
            combo.Location = new Point(x, y);
            combo.Size = new Size(width, 30);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = new Font("Microsoft YaHei UI", 9.5F);
            combo.BackColor = Theme.Surface;
            combo.ForeColor = TextColor;
            combo.DropDownHeight = 300;
            combo.IntegralHeight = false;
            return combo;
        }

        private Button CreateSecondaryButton(string text, int x, int y, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.BorderStrong;
            button.FlatAppearance.MouseOverBackColor = Theme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = Theme.ButtonDown;
            button.BackColor = Theme.Surface;
            button.ForeColor = Theme.TextStrong;
            button.Font = new Font("Microsoft YaHei UI", 9F);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            LocateGameDirectory();
            Log("程序已启动。下载源：BMCLAPI 镜像优先，失败时自动切换官方源。");
            await LoadManifestAsync();
        }

        private void LocateGameDirectory()
        {
            string exeDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            string mcDirectory = Path.Combine(exeDirectory, ".minecraft");
            bool existed = Directory.Exists(mcDirectory);
            try
            {
                if (!existed)
                    Directory.CreateDirectory(mcDirectory);
                _gameDir = mcDirectory;
                _pathBox.Text = mcDirectory;
                _pathHintLabel.Text = existed
                    ? "已自动识别 .minecraft，游戏版本将下载到 .minecraft\\versions"
                    : "未找到 .minecraft，已在程序目录自动创建，游戏版本将下载到 .minecraft\\versions";
                _pathHintLabel.ForeColor = Theme.Accent;
                Log(existed ? "已识别游戏目录：" + mcDirectory : "未找到 .minecraft，已自动创建：" + mcDirectory);
            }
            catch (Exception ex)
            {
                _gameDir = mcDirectory;
                _pathBox.Text = mcDirectory;
                _pathHintLabel.Text = "无法创建 .minecraft，请换到有写入权限的目录或手动选择";
                _pathHintLabel.ForeColor = DangerColor;
                Log("创建 .minecraft 失败：" + ex.Message);
            }
            SyncGameDirToPages();
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 .minecraft 文件夹，或选择包含 .minecraft 的目录";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(_gameDir))
                    dialog.SelectedPath = Directory.GetParent(_gameDir) != null ? Directory.GetParent(_gameDir).FullName : _gameDir;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string selected = dialog.SelectedPath;
                string candidate;
                if (PathUtil.LooksLikeMinecraftDirectory(selected))
                {
                    candidate = selected;
                }
                else
                {
                    string child = Path.Combine(selected, ".minecraft");
                    candidate = Directory.Exists(child) ? child : selected;
                }

                try
                {
                    Directory.CreateDirectory(candidate);
                    _gameDir = candidate;
                    _pathBox.Text = candidate;
                    _pathHintLabel.Text = "已手动选择游戏目录，游戏版本将下载到 versions 子目录";
                    _pathHintLabel.ForeColor = Theme.Accent;
                    Log("已切换下载目录：" + candidate);
                    UpdateTargetInfo();
                    SyncGameDirToPages();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法使用该目录：\n" + ex.Message, "目录不可用",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadManifestAsync();
        }

        private async Task LoadManifestAsync()
        {
            if (_manifestCts != null)
            {
                _manifestCts.Cancel();
                _manifestCts.Dispose();
            }
            _manifestCts = new CancellationTokenSource();
            CancellationToken token = _manifestCts.Token;

            _manifestLoaded = false;
            _snapshotsCheck.Enabled = false;
            _gameCombo.Enabled = false;
            _loaderCombo.Enabled = false;
            _loaderVersionCombo.Enabled = false;
            _refreshButton.Enabled = false;
            _startButton.Enabled = false;
            _statusLabel.Text = "正在获取版本列表…";
            _statusLabel.ForeColor = TextColor;
            _detailLabel.Text = "从 BMCLAPI 镜像获取，失败时自动切换官方源";
            SetProgressMarquee(true);

            try
            {
                List<GameVersion> versions = await VersionService.GetGameVersionsAsync(LogSafe, token);
                token.ThrowIfCancellationRequested();

                _allVersions = versions;
                _manifestLoaded = true;
                RepopulateGameVersions(null);
                SyncGameVersionsToPages();
                _snapshotsCheck.Enabled = true;
                _gameCombo.Enabled = true;
                _loaderCombo.Enabled = true;
                _refreshButton.Enabled = true;

                int releaseCount = 0;
                for (int i = 0; i < versions.Count; i++)
                {
                    if (versions[i].Type == "release")
                        releaseCount++;
                }
                Log("版本列表加载完成：正式版 " + releaseCount + " 个，总条目 " + versions.Count + " 个。");

                await LoadLoaderVersionsForSelectionAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    return;
                _statusLabel.Text = "获取版本列表失败";
                _statusLabel.ForeColor = DangerColor;
                _detailLabel.Text = "请检查网络后点击“刷新列表”重试";
                SetProgressMarquee(false);
                Log("获取版本列表失败：" + ex.Message);
                MessageBox.Show(this, "获取 Minecraft 版本列表失败：\n\n" + ex.Message +
                    "\n\n请检查网络连接，然后点击“刷新列表”重试。", "网络错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RepopulateGameVersions(string preferredId)
        {
            GameVersion current = GetSelectedGame();
            string preferred = preferredId;
            if (string.IsNullOrEmpty(preferred) && current != null)
                preferred = current.Id;

            int selected = -1;
            GameVersion latestRelease = null;
            _suppressEvents = true;
            try
            {
                _gameCombo.BeginUpdate();
                _gameCombo.Items.Clear();
                for (int i = 0; i < _allVersions.Count; i++)
                {
                    GameVersion version = _allVersions[i];
                    if (version.Type != "release" && !_snapshotsCheck.Checked)
                        continue;
                    if (latestRelease == null && version.Type == "release")
                        latestRelease = version;
                    int index = _gameCombo.Items.Add(version);
                    if (!string.IsNullOrEmpty(preferred) && string.Equals(version.Id, preferred, StringComparison.OrdinalIgnoreCase))
                        selected = index;
                }
                if (selected < 0 && _gameCombo.Items.Count > 0)
                {
                    if (latestRelease != null)
                        selected = _gameCombo.Items.IndexOf(latestRelease);
                    if (selected < 0)
                        selected = 0;
                }
                _gameCombo.SelectedIndex = selected;
            }
            finally
            {
                _gameCombo.EndUpdate();
                _suppressEvents = false;
            }
            UpdateTargetInfo();
        }

        private async void GameSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressEvents)
                return;
            UpdateTargetInfo();
            await LoadLoaderVersionsForSelectionAsync();
        }

        private async void SnapshotsCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (_suppressEvents || !_manifestLoaded)
                return;
            RepopulateGameVersions(null);
            await LoadLoaderVersionsForSelectionAsync();
        }

        private async void LoaderSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressEvents)
                return;
            UpdateTargetInfo();
            await LoadLoaderVersionsForSelectionAsync();
        }

        private void LoaderVersionChanged(object sender, EventArgs e)
        {
            if (_suppressEvents)
                return;
            UpdateTargetInfo();
            SetStartState();
        }

        private async Task LoadLoaderVersionsForSelectionAsync()
        {
            GameVersion game = GetSelectedGame();
            LoaderChoice choice = GetSelectedLoader();
            if (game == null || choice == null || !_manifestLoaded)
            {
                SetStartState();
                return;
            }

            if (_loaderCts != null)
            {
                _loaderCts.Cancel();
                _loaderCts.Dispose();
                _loaderCts = null;
            }
            _loaderRequestId++;
            int requestId = _loaderRequestId;
            CancellationTokenSource cts = new CancellationTokenSource();
            _loaderCts = cts;
            _startButton.Enabled = false;

            if (choice.Kind == LoaderKind.Vanilla)
            {
                _suppressEvents = true;
                _loaderVersionCombo.Items.Clear();
                _loaderVersionCombo.Items.Add("无需加载器");
                _loaderVersionCombo.SelectedIndex = 0;
                _suppressEvents = false;
                _loaderVersionCombo.Enabled = false;
                _statusLabel.Text = "准备就绪";
                _statusLabel.ForeColor = TextColor;
                _detailLabel.Text = "将下载正版游戏本体、运行库和全部资源文件。";
                SetProgressMarquee(false);
                UpdateTargetInfo();
                SetStartState();
                return;
            }

            _suppressEvents = true;
            _loaderVersionCombo.Items.Clear();
            _loaderVersionCombo.Items.Add("正在获取版本…");
            _loaderVersionCombo.SelectedIndex = 0;
            _suppressEvents = false;
            _loaderVersionCombo.Enabled = false;
            _statusLabel.Text = "正在获取 " + choice.Kind.DisplayName() + " 版本…";
            _statusLabel.ForeColor = TextColor;
            _detailLabel.Text = "从加载器官方元数据获取可用版本";
            SetProgressMarquee(true);

            try
            {
                List<LoaderVersionInfo> versions = await LoaderCatalog.GetLoaderVersionsAsync(
                    choice.Kind, game, LogSafe, cts.Token);
                if (requestId != _loaderRequestId || cts.IsCancellationRequested)
                    return;

                _suppressEvents = true;
                try
                {
                    _loaderVersionCombo.Items.Clear();
                    if (versions.Count == 0)
                    {
                        _loaderVersionCombo.Items.Add("该游戏版本暂不可用");
                        _loaderVersionCombo.SelectedIndex = 0;
                    }
                    else
                    {
                        for (int i = 0; i < versions.Count; i++)
                            _loaderVersionCombo.Items.Add(versions[i]);
                        _loaderVersionCombo.SelectedIndex = 0;
                    }
                }
                finally
                {
                    _suppressEvents = false;
                }

                if (versions.Count == 0)
                {
                    _loaderVersionCombo.Enabled = false;
                    _statusLabel.Text = "没有可用的 " + choice.Kind.DisplayName() + " 版本";
                    _statusLabel.ForeColor = DangerColor;
                    _detailLabel.Text = "请换一个游戏版本，或改用其他加载器。";
                    Log(choice.Kind.DisplayName() + "：游戏版本 " + game.Id + " 暂无可用版本。");
                }
                else
                {
                    _loaderVersionCombo.Enabled = true;
                    _statusLabel.Text = "准备就绪";
                    _statusLabel.ForeColor = TextColor;
                    _detailLabel.Text = choice.Kind.DisplayName() + " 共 " + versions.Count + " 个可选版本。";
                    Log("已获取 " + choice.Kind.DisplayName() + " 版本列表（" + versions.Count + " 个），默认 " +
                        versions[0].Version + "。");
                }
                SetProgressMarquee(false);
                UpdateTargetInfo();
                SetStartState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (requestId != _loaderRequestId)
                    return;
                _suppressEvents = true;
                _loaderVersionCombo.Items.Clear();
                _loaderVersionCombo.Items.Add("获取失败");
                _loaderVersionCombo.SelectedIndex = 0;
                _suppressEvents = false;
                _loaderVersionCombo.Enabled = false;
                _statusLabel.Text = "获取 " + choice.Kind.DisplayName() + " 版本失败";
                _statusLabel.ForeColor = DangerColor;
                _detailLabel.Text = "可以切换其他加载器，或稍后重试。";
                SetProgressMarquee(false);
                Log("获取加载器版本失败：" + ex.Message);
                SetStartState();
            }
            finally
            {
                if (_loaderCts == cts)
                    _loaderCts = null;
                cts.Dispose();
            }
        }

        private void UpdateTargetInfo()
        {
            if (_targetLabel == null)
                return;
            GameVersion game = GetSelectedGame();
            LoaderChoice loader = GetSelectedLoader();
            if (game == null || string.IsNullOrEmpty(_gameDir))
            {
                _targetLabel.Text = "安装位置：请选择游戏版本";
                _targetSubLabel.Text = "游戏本体和加载器都会写入 .minecraft\\versions";
                return;
            }

            if (loader == null || loader.Kind == LoaderKind.Vanilla)
            {
                _targetLabel.Text = "安装位置：" + Path.Combine(_gameDir, "versions", game.Id);
                _targetSubLabel.Text = "将下载原版 " + game.Id + " 的客户端、运行库与全部资源文件";
                return;
            }

            LoaderVersionInfo loaderVersion = GetSelectedLoaderVersion();
            string versionText = loaderVersion == null ? "（请选择加载器版本）" : " " + loaderVersion.Version;
            _targetLabel.Text = "安装位置：" + Path.Combine(_gameDir, "versions") + "\\<加载器版本目录>";
            _targetSubLabel.Text = "只安装自包含的 " + loader.Kind.DisplayName() + versionText +
                                   " 版本，不再单独保留原版版本目录";
        }

        private void SetStartState()
        {
            GameVersion game = GetSelectedGame();
            LoaderChoice loader = GetSelectedLoader();
            bool canStart = game != null && !string.IsNullOrEmpty(_gameDir);
            if (canStart && loader != null && loader.Kind != LoaderKind.Vanilla)
                canStart = _loaderVersionCombo.Enabled && GetSelectedLoaderVersion() != null;
            _startButton.Enabled = canStart;
            _startButton.Text = "加入下载队列";
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            GameVersion game = GetSelectedGame();
            LoaderChoice loaderChoice = GetSelectedLoader();
            if (game == null || loaderChoice == null)
            {
                MessageBox.Show(this, "请先选择要下载的游戏版本。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoaderVersionInfo loaderVersion = null;
            if (loaderChoice.Kind != LoaderKind.Vanilla)
            {
                loaderVersion = GetSelectedLoaderVersion();
                if (loaderVersion == null)
                {
                    MessageBox.Show(this, "请先选择可用的加载器版本。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(_gameDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法写入下载目录：\n" + ex.Message, "目录不可用",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            InstallOptions options = new InstallOptions();
            options.GameDir = _gameDir;
            options.Game = game;
            options.Loader = loaderChoice.Kind;
            options.LoaderVersion = loaderVersion == null ? null : loaderVersion.Version;

            string loaderText = loaderChoice.Kind == LoaderKind.Vanilla
                ? ""
                : " + " + loaderChoice.Kind.DisplayName() + " " + loaderVersion.Version;
            string title = "游戏本体 · " + game.Id + loaderText;
            string subtitle = "安装到 " + Path.Combine(_gameDir, "versions");
            string installedName = game.Id;
            if (loaderChoice.Kind != LoaderKind.Vanilla)
            {
                installedName += " " + loaderChoice.Kind.DisplayName();
                if (loaderVersion != null && !string.IsNullOrEmpty(loaderVersion.Version))
                    installedName += " " + loaderVersion.Version;
            }

            _taskManager.Enqueue(title, subtitle, LoaderTargetIcon(loaderChoice.Kind), null,
                delegate(DownloadTask current, CancellationToken token)
                {
                    MinecraftInstaller installer = new MinecraftInstaller(current.Log,
                        delegate(string text) { current.Stage = text; });
                    current.Stats = installer.Stats;
                    InstallResult result = installer.InstallAsync(options, token).GetAwaiter().GetResult();
                    current.ResultText = "已安装版本 " + installedName;
                    current.Log("版本已就绪：" + result.VersionDirectory);
                    return Task.FromResult(true);
                });

            Log("已加入下载队列：" + title);
            _statusLabel.Text = "已加入下载队列：" + title;
            _statusLabel.ForeColor = AccentColor;
            _detailLabel.Text = subtitle;
            _targetLabel.Text = "队列任务：" + title;
            _targetSubLabel.Text = "任务会按顺序执行，可在“任务”页查看进度或取消。";
            SetProgressMarquee(false);
            ShowTab(1);
        }

        private static TargetIconKind LoaderTargetIcon(LoaderKind kind)
        {
            switch (kind)
            {
                case LoaderKind.Fabric: return TargetIconKind.Fabric;
                case LoaderKind.Forge: return TargetIconKind.Forge;
                case LoaderKind.NeoForge: return TargetIconKind.NeoForge;
                case LoaderKind.Quilt: return TargetIconKind.Quilt;
                default: return TargetIconKind.Vanilla;
            }
        }

        private string DefaultFooterText()
        {
            return "下载源：BMCLAPI 镜像优先 · 失败自动切换官方源　|　v1.0.0";
        }

        private void TaskManager_TasksChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new EventHandler(TaskManager_TasksChanged), sender, e); } catch { }
                return;
            }
            UpdateTaskBadge();
            if (_taskManager.HasActive && _autoCloseTimer != null && _autoCloseTimer.Enabled)
            {
                _autoCloseTimer.Stop();
                _footerLabel.Text = DefaultFooterText();
                Text = "MineCraftDownloadTool";
            }
        }

        private void UpdateTaskBadge()
        {
            if (_tabs == null || _tabs.Length <= 1)
                return;
            _tabs[1].Badge = _taskManager.ActiveCount;
        }

        private void TaskManager_QueueIdle(object sender, QueueIdleEventArgs e)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new EventHandler<QueueIdleEventArgs>(TaskManager_QueueIdle), sender, e); } catch { }
                return;
            }
            UpdateTaskBadge();
            if (!AppSettings.AutoCloseAfterDownload || e.Completed <= 0 || e.Failed > 0 || e.Canceled > 0)
                return;
            if (_taskManager.HasActive)
                return;
            _autoCloseSeconds = 5;
            _autoCloseTimer.Start();
            _footerLabel.Text = "全部下载任务已完成，5 秒后自动关闭…（可在设置中关闭）";
            Text = "MineCraftDownloadTool — 5 秒后自动关闭";
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            _autoCloseSeconds--;
            if (_autoCloseSeconds <= 0)
            {
                _autoCloseTimer.Stop();
                _autoClosing = true;
                Close();
                return;
            }
            _footerLabel.Text = "全部下载任务已完成，" + _autoCloseSeconds + " 秒后自动关闭…";
            Text = "MineCraftDownloadTool — " + _autoCloseSeconds + " 秒后自动关闭";
        }

        private void SetProgressMarquee(bool enabled)
        {
            _progressBar.Style = enabled ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!enabled)
                _progressBar.Value = 0;
        }

        private GameVersion GetSelectedGame()
        {
            return _gameCombo == null ? null : _gameCombo.SelectedItem as GameVersion;
        }

        private LoaderChoice GetSelectedLoader()
        {
            return _loaderCombo == null ? null : _loaderCombo.SelectedItem as LoaderChoice;
        }

        private LoaderVersionInfo GetSelectedLoaderVersion()
        {
            return _loaderVersionCombo == null ? null : _loaderVersionCombo.SelectedItem as LoaderVersionInfo;
        }

        private void LogSafe(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(LogSafe), text); } catch { }
                return;
            }
            Log(text);
        }

        private void Log(string text)
        {
            if (_logBox == null || _logBox.IsDisposed)
                return;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (text ?? "") + Environment.NewLine;
            _logBox.AppendText(line);
            if (_logBox.TextLength > 180000)
                _logBox.Text = _logBox.Text.Substring(_logBox.TextLength - 120000);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_taskManager.HasActive && !_autoClosing)
            {
                DialogResult result = MessageBox.Show(this,
                    "下载队列中还有任务正在进行或等待，退出会中断这些任务。确定要退出吗？",
                    "确认退出", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (_autoCloseTimer != null) _autoCloseTimer.Stop();
            if (_manifestCts != null) { try { _manifestCts.Cancel(); } catch { } }
            if (_loaderCts != null) { try { _loaderCts.Cancel(); } catch { } }
            _taskManager.CancelAll();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_manifestCts != null) _manifestCts.Dispose();
            if (_loaderCts != null) _loaderCts.Dispose();
            _taskManager.Dispose();
        }

        private sealed class TabButton : Button
        {
            private bool _selected;
            private int _badge;

            public TabButton(string text)
            {
                Text = text;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = Theme.TabHover;
                FlatAppearance.MouseDownBackColor = Theme.ButtonDown;
                BackColor = Theme.Surface;
                ForeColor = MutedColor;
                Font = new Font("Microsoft YaHei UI", 9.5F);
                Cursor = Cursors.Hand;
                TabStop = false;
            }

            public int Badge
            {
                get { return _badge; }
                set
                {
                    int badge = value < 0 ? 0 : value;
                    if (_badge == badge)
                        return;
                    _badge = badge;
                    Invalidate();
                }
            }

            public void SetSelected(bool selected)
            {
                _selected = selected;
                ForeColor = selected ? AccentColor : MutedColor;
                Font = new Font("Microsoft YaHei UI", 9.5F, selected ? FontStyle.Bold : FontStyle.Regular);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (_selected)
                {
                    using (SolidBrush brush = new SolidBrush(AccentColor))
                        e.Graphics.FillRectangle(brush, 10, Height - 4, Width - 20, 3);
                }
                if (_badge <= 0)
                    return;
                string text = _badge > 99 ? "99+" : _badge.ToString();
                using (Font font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold))
                {
                    Size size = TextRenderer.MeasureText(text, font);
                    int width = Math.Max(15, size.Width + 5);
                    Rectangle badgeRect = new Rectangle(Width - width - 2, 1, width, 15);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(214, 88, 60)))
                        e.Graphics.FillEllipse(brush, badgeRect);
                    TextRenderer.DrawText(e.Graphics, text, font, badgeRect, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private sealed class LoaderChoice
        {
            public LoaderKind Kind { get; private set; }
            public string Name { get; private set; }

            public LoaderChoice(LoaderKind kind, string name)
            {
                Kind = kind;
                Name = name;
            }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
