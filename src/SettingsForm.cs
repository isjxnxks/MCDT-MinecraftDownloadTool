using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class SettingsForm : Form
    {
        private static Color Accent { get { return Theme.Accent; } }
        private static Color TextColor { get { return Theme.Text; } }
        private static Color Muted { get { return Theme.Muted; } }
        private static Color Danger { get { return Theme.Danger; } }

        private readonly Action _themeChanged;
        private readonly RadioButton _isolationRadio;
        private readonly RadioButton _sharedRadio;
        private readonly Label _modeStatusLabel;
        private readonly CheckBox _autoCloseCheck;
        private readonly RadioButton _lightRadio;
        private readonly RadioButton _darkRadio;
        private readonly Label _themeHintLabel;
        private readonly TextBox _keyBox;
        private readonly CheckBox _showCheck;
        private readonly Label _statusLabel;
        private readonly Button _saveButton;
        private readonly Button _clearButton;
        private readonly ThemeColorSwatch[] _colorSwatches;
        private readonly Button _customColorButton;
        private readonly ToolTip _toolTip;
        private string _selectedColorHex;
        private bool _suppressThemeEvents;

        public SettingsForm()
            : this(null)
        {
        }

        public SettingsForm(Action themeChanged)
        {
            _themeChanged = themeChanged;
            Text = "设置";
            ClientSize = new Size(500, 592);
            BackColor = Theme.Page;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            Label modeTitle = new Label();
            modeTitle.Text = "版本管理方式";
            modeTitle.Location = new Point(24, 16);
            modeTitle.AutoSize = true;
            modeTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            modeTitle.ForeColor = TextColor;
            Controls.Add(modeTitle);

            Panel versionGroup = new Panel();
            versionGroup.Location = new Point(24, 48);
            versionGroup.Size = new Size(452, 92);
            versionGroup.BackColor = Theme.Page;
            Controls.Add(versionGroup);

            _isolationRadio = new ThemedRadioButton();
            _isolationRadio.Text = "版本隔离";
            _isolationRadio.Location = new Point(0, 0);
            _isolationRadio.Size = new Size(120, 22);
            _isolationRadio.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _isolationRadio.ForeColor = TextColor;
            versionGroup.Controls.Add(_isolationRadio);

            Label isolationHint = new Label();
            isolationHint.Text = "Mod、光影、资源包和数据包装到 .minecraft\\versions\\<版本> 各自的目录。";
            isolationHint.Location = new Point(45, 72);
            isolationHint.Size = new Size(430, 18);
            isolationHint.ForeColor = Muted;
            Controls.Add(isolationHint);

            _sharedRadio = new ThemedRadioButton();
            _sharedRadio.Text = "不隔离";
            _sharedRadio.Location = new Point(0, 50);
            _sharedRadio.Size = new Size(120, 22);
            _sharedRadio.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _sharedRadio.ForeColor = TextColor;
            versionGroup.Controls.Add(_sharedRadio);

            Label sharedHint = new Label();
            sharedHint.Text = "所有版本共用 .minecraft\\mods、shaderpacks、resourcepacks 和 saves。";
            sharedHint.Location = new Point(45, 122);
            sharedHint.Size = new Size(430, 18);
            sharedHint.ForeColor = Muted;
            Controls.Add(sharedHint);

            _modeStatusLabel = new Label();
            _modeStatusLabel.Location = new Point(24, 146);
            _modeStatusLabel.Size = new Size(452, 18);
            _modeStatusLabel.ForeColor = Muted;
            _modeStatusLabel.Text = "切换后立即保存，下载页的安装位置会在关闭设置后刷新。";
            Controls.Add(_modeStatusLabel);

            _autoCloseCheck = new ThemedCheckBox();
            _autoCloseCheck.Text = "所有下载任务完成后自动关闭程序";
            _autoCloseCheck.Location = new Point(24, 174);
            _autoCloseCheck.Size = new Size(300, 24);
            _autoCloseCheck.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _autoCloseCheck.ForeColor = TextColor;
            _autoCloseCheck.Checked = AppSettings.AutoCloseAfterDownload;
            _autoCloseCheck.CheckedChanged += AutoCloseChanged;
            Controls.Add(_autoCloseCheck);

            Label autoCloseHint = new Label();
            autoCloseHint.Text = "默认关闭。开启后任务队列全部成功完成时倒计时 5 秒关闭；中途添加新任务会取消倒计时。";
            autoCloseHint.Location = new Point(45, 199);
            autoCloseHint.Size = new Size(430, 18);
            autoCloseHint.ForeColor = Muted;
            Controls.Add(autoCloseHint);

            Panel autoCloseSeparator = CreateSeparator(226);
            Controls.Add(autoCloseSeparator);

            Label themeTitle = new Label();
            themeTitle.Text = "外观主题";
            themeTitle.Location = new Point(24, 240);
            themeTitle.AutoSize = true;
            themeTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            themeTitle.ForeColor = TextColor;
            Controls.Add(themeTitle);

            Panel themeGroup = new Panel();
            themeGroup.Location = new Point(24, 270);
            themeGroup.Size = new Size(158, 22);
            themeGroup.BackColor = Theme.Page;
            Controls.Add(themeGroup);

            _lightRadio = new ThemedRadioButton();
            _lightRadio.Text = "浅色";
            _lightRadio.Location = new Point(0, 0);
            _lightRadio.Size = new Size(64, 22);
            _lightRadio.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _lightRadio.ForeColor = TextColor;
            themeGroup.Controls.Add(_lightRadio);

            _darkRadio = new ThemedRadioButton();
            _darkRadio.Text = "深色";
            _darkRadio.Location = new Point(94, 0);
            _darkRadio.Size = new Size(64, 22);
            _darkRadio.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _darkRadio.ForeColor = TextColor;
            themeGroup.Controls.Add(_darkRadio);

            _themeHintLabel = new Label();
            _themeHintLabel.Location = new Point(45, 296);
            _themeHintLabel.Size = new Size(430, 18);
            _themeHintLabel.ForeColor = Muted;
            _themeHintLabel.Text = "切换后立即生效，并保存到本机设置。";
            Controls.Add(_themeHintLabel);

            Label colorLabel = new Label();
            colorLabel.Text = "主题色";
            colorLabel.Location = new Point(24, 324);
            colorLabel.AutoSize = true;
            colorLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            colorLabel.ForeColor = TextColor;
            Controls.Add(colorLabel);

            _selectedColorHex = Theme.ToHex(Theme.ParseHex(AppSettings.ThemeColor));
            _colorSwatches = new ThemeColorSwatch[Theme.ColorPresets.Length];
            _toolTip = new ToolTip();
            for (int i = 0; i < Theme.ColorPresets.Length; i++)
            {
                ThemeColorSwatch swatch = new ThemeColorSwatch(Theme.ColorPresets[i].Color,
                    "themeColor_" + i);
                swatch.Location = new Point(88 + i * 32, 319);
                swatch.Click += ColorSwatch_Click;
                _toolTip.SetToolTip(swatch, Theme.ColorPresets[i].Name);
                Controls.Add(swatch);
                _colorSwatches[i] = swatch;
            }

            _customColorButton = new Button();
            _customColorButton.Location = new Point(286, 317);
            _customColorButton.Size = new Size(96, 30);
            _customColorButton.FlatStyle = FlatStyle.Flat;
            _customColorButton.Font = new Font("Microsoft YaHei UI", 9F);
            _customColorButton.Cursor = Cursors.Hand;
            _customColorButton.Click += CustomColor_Click;
            Controls.Add(_customColorButton);

            Panel themeSeparator = CreateSeparator(362);
            Controls.Add(themeSeparator);

            Label title = new Label();
            title.Text = "CurseForge API Key";
            title.Location = new Point(24, 376);
            title.AutoSize = true;
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = TextColor;
            Controls.Add(title);

            Label hint = new Label();
            hint.Text = "搜索和下载 CurseForge 内容需要 Key。Key 只保存在本机，不会上传。";
            hint.Location = new Point(24, 404);
            hint.Size = new Size(450, 20);
            hint.ForeColor = Muted;
            Controls.Add(hint);

            _keyBox = new TextBox();
            _keyBox.Location = new Point(24, 430);
            _keyBox.Size = new Size(452, 28);
            _keyBox.UseSystemPasswordChar = true;
            _keyBox.BorderStyle = BorderStyle.FixedSingle;
            _keyBox.BackColor = Theme.Surface;
            _keyBox.ForeColor = TextColor;
            _keyBox.Text = AppSettings.CurseForgeApiKey;
            Controls.Add(_keyBox);

            _showCheck = new ThemedCheckBox();
            _showCheck.Text = "显示 Key";
            _showCheck.Location = new Point(24, 466);
            _showCheck.Size = new Size(100, 22);
            _showCheck.ForeColor = TextColor;
            _showCheck.CheckedChanged += delegate { _keyBox.UseSystemPasswordChar = !_showCheck.Checked; };
            Controls.Add(_showCheck);

            LinkLabel applyLink = new LinkLabel();
            applyLink.Text = "在 CurseForge 控制台免费申请 API Key";
            applyLink.Location = new Point(132, 466);
            applyLink.AutoSize = true;
            applyLink.LinkColor = Accent;
            applyLink.ActiveLinkColor = Theme.AccentDown;
            applyLink.LinkClicked += delegate { OpenUrl("https://console.curseforge.com/?#/api-keys"); };
            Controls.Add(applyLink);

            _statusLabel = new Label();
            _statusLabel.Location = new Point(24, 494);
            _statusLabel.Size = new Size(452, 38);
            _statusLabel.ForeColor = Muted;
            Controls.Add(_statusLabel);

            _saveButton = CreateButton("测试并保存", 24, 540, 150, 38, true);
            _saveButton.Click += SaveButton_Click;
            Controls.Add(_saveButton);

            _clearButton = CreateButton("清除 Key", 186, 540, 100, 38, false);
            _clearButton.Click += ClearButton_Click;
            Controls.Add(_clearButton);

            Button cancel = CreateButton("取消", 298, 540, 90, 38, false);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = _saveButton;
            CancelButton = cancel;

            bool isolation = AppSettings.VersionIsolation;
            _isolationRadio.Checked = isolation;
            _sharedRadio.Checked = !isolation;
            _isolationRadio.CheckedChanged += ModeChanged;
            _sharedRadio.CheckedChanged += ModeChanged;

            bool dark = AppSettings.DarkTheme;
            _darkRadio.Checked = dark;
            _lightRadio.Checked = !dark;
            _darkRadio.CheckedChanged += ThemeModeChanged;
            _lightRadio.CheckedChanged += ThemeModeChanged;

            versionGroup.SendToBack();
            themeGroup.SendToBack();
            ApplyTheme();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowChrome(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _toolTip != null)
                _toolTip.Dispose();
            base.Dispose(disposing);
        }

        private static Panel CreateSeparator(int y)
        {
            Panel separator = new Panel();
            separator.Location = new Point(24, y);
            separator.Size = new Size(452, 1);
            separator.BackColor = Theme.Border;
            return separator;
        }

        private static Button CreateButton(string text, int x, int y, int width, int height, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Theme.BorderStrong;
            button.BackColor = primary ? Accent : Theme.Surface;
            button.ForeColor = primary ? Color.White : Theme.TextStrong;
            button.Font = new Font("Microsoft YaHei UI", primary ? 9.5F : 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void ApplyTheme()
        {
            Theme.Apply(this);
            BackColor = Theme.Page;
            _keyBox.BackColor = Theme.Surface;
            _keyBox.ForeColor = Theme.Text;
            UpdateColorSelection();
            Theme.ApplyWindowChrome(this);
            Invalidate(true);
        }

        private void UpdateColorSelection()
        {
            bool matched = false;
            for (int i = 0; i < _colorSwatches.Length; i++)
            {
                bool selected = string.Equals(Theme.ColorPresets[i].Hex, _selectedColorHex,
                    StringComparison.OrdinalIgnoreCase);
                _colorSwatches[i].Selected = selected;
                if (selected)
                    matched = true;
            }

            if (!matched)
            {
                Color custom = Theme.ParseHex(_selectedColorHex);
                _customColorButton.Text = "自定义色";
                _customColorButton.BackColor = custom;
                _customColorButton.ForeColor = Theme.IsLight(custom) ? Theme.Text : Color.White;
                _customColorButton.FlatAppearance.BorderSize = 2;
                _customColorButton.FlatAppearance.BorderColor = Theme.Text;
                _customColorButton.FlatAppearance.MouseOverBackColor = custom;
                _customColorButton.FlatAppearance.MouseDownBackColor = custom;
                _toolTip.SetToolTip(_customColorButton, _selectedColorHex);
            }
            else
            {
                _customColorButton.Text = "自定义…";
                _customColorButton.BackColor = Theme.Surface;
                _customColorButton.ForeColor = Theme.TextStrong;
                _customColorButton.FlatAppearance.BorderSize = 1;
                _customColorButton.FlatAppearance.BorderColor = Theme.BorderStrong;
                _customColorButton.FlatAppearance.MouseOverBackColor = Theme.ButtonHover;
                _customColorButton.FlatAppearance.MouseDownBackColor = Theme.ButtonDown;
                _toolTip.SetToolTip(_customColorButton, "选择任意颜色");
            }
        }

        private void ColorSwatch_Click(object sender, EventArgs e)
        {
            ThemeColorSwatch swatch = sender as ThemeColorSwatch;
            if (swatch == null)
                return;
            for (int i = 0; i < _colorSwatches.Length; i++)
            {
                if (_colorSwatches[i] != swatch)
                    continue;
                ThemeColorPreset preset = Theme.ColorPresets[i];
                ApplyColor(preset.Hex, preset.Name);
                return;
            }
        }

        private void CustomColor_Click(object sender, EventArgs e)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                dialog.Color = Theme.ParseHex(_selectedColorHex);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                string hex = Theme.ToHex(dialog.Color);
                ApplyColor(hex, "自定义色 " + hex);
            }
        }

        private void ApplyColor(string hex, string label)
        {
            Color color = Theme.ParseHex(hex);
            string normalized = Theme.ToHex(color);
            string previous = AppSettings.ThemeColor;
            AppSettings.ThemeColor = normalized;
            try
            {
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                AppSettings.ThemeColor = previous;
                MessageBox.Show(this, "保存主题色设置失败：" + ex.Message, "设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Theme.SetAccent(color);
            _selectedColorHex = normalized;
            ApplyTheme();
            _themeHintLabel.ForeColor = Accent;
            _themeHintLabel.Text = "已切换主题色：" + label + "，设置已保存。";
            try
            {
                if (_themeChanged != null)
                    _themeChanged();
            }
            finally
            {
                Theme.EndColorTransition();
            }
        }

        private void ThemeModeChanged(object sender, EventArgs e)
        {
            RadioButton radio = sender as RadioButton;
            if (radio == null || !radio.Checked || _suppressThemeEvents)
                return;
            bool dark = _darkRadio.Checked;
            if (dark == AppSettings.DarkTheme)
            {
                Theme.SetDark(dark);
                return;
            }

            AppSettings.DarkTheme = dark;
            try
            {
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                _suppressThemeEvents = true;
                try
                {
                    _darkRadio.Checked = AppSettings.DarkTheme;
                    _lightRadio.Checked = !AppSettings.DarkTheme;
                }
                finally
                {
                    _suppressThemeEvents = false;
                }
                MessageBox.Show(this, "保存主题设置失败：" + ex.Message, "设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Theme.SetDark(dark);
            ApplyTheme();
            _themeHintLabel.ForeColor = Accent;
            _themeHintLabel.Text = dark ? "已切换为深色主题，设置已保存。" : "已切换为浅色主题，设置已保存。";
            if (_themeChanged != null)
                _themeChanged();
        }

        private void AutoCloseChanged(object sender, EventArgs e)
        {
            AppSettings.AutoCloseAfterDownload = _autoCloseCheck.Checked;
            try
            {
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                _autoCloseCheck.Checked = !_autoCloseCheck.Checked;
                MessageBox.Show(this, "保存自动关闭设置失败：" + ex.Message, "设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ModeChanged(object sender, EventArgs e)
        {
            RadioButton radio = sender as RadioButton;
            if (radio == null || !radio.Checked)
                return;
            AppSettings.VersionIsolation = _isolationRadio.Checked;
            try
            {
                AppSettings.Save();
                _modeStatusLabel.ForeColor = Accent;
                _modeStatusLabel.Text = "版本管理方式已保存，关闭设置后下载位置会刷新。";
            }
            catch (Exception ex)
            {
                _modeStatusLabel.ForeColor = Danger;
                _modeStatusLabel.Text = "保存失败：" + ex.Message;
            }
        }

        private async void SaveButton_Click(object sender, EventArgs e)
        {
            string key = _keyBox.Text.Trim();
            if (key.Length == 0)
            {
                _statusLabel.Text = "Key 为空。如需使用 CurseForge，请填写后再保存。";
                _statusLabel.ForeColor = Danger;
                return;
            }

            _saveButton.Enabled = false;
            _clearButton.Enabled = false;
            _statusLabel.ForeColor = Muted;
            _statusLabel.Text = "正在验证 Key…";
            try
            {
                await ContentService.TestCurseForgeKeyAsync(key, System.Threading.CancellationToken.None);
                AppSettings.CurseForgeApiKey = key;
                AppSettings.Save();
                _statusLabel.ForeColor = Accent;
                _statusLabel.Text = "验证成功，Key 已保存。";
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _statusLabel.ForeColor = Danger;
                _statusLabel.Text = "验证失败：" + ex.Message;
            }
            finally
            {
                _saveButton.Enabled = true;
                _clearButton.Enabled = true;
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            AppSettings.CurseForgeApiKey = "";
            try
            {
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "清除失败：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _statusLabel.ForeColor = Accent;
            _statusLabel.Text = "已清除本机保存的 Key。";
            _keyBox.Text = "";
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url));
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开浏览器：" + ex.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
