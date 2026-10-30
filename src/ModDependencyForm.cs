using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class ModDependencyForm : Form
    {
        private static Color PageColor { get { return Theme.PageAlt; } }
        private static Color AccentColor { get { return Theme.Accent; } }
        private static Color AccentHover { get { return Theme.AccentHover; } }
        private static Color AccentDown { get { return Theme.AccentDown; } }
        private static Color TextColor { get { return Theme.Text; } }
        private static Color MutedColor { get { return Theme.Muted; } }
        private static Color BorderColor { get { return Theme.BorderStrong; } }
        private static Color CardColor { get { return Theme.Surface; } }
        private static Color WarningColor { get { return Theme.Warning; } }

        private readonly List<ModDependency> _dependencies;
        private readonly Func<ModDependency, bool> _downloadDependency;
        private readonly Font _rowTitleFont;
        private readonly Font _rowSubtitleFont;
        private readonly Font _hintFont;

        public ModDependency NavigateToDependency { get; private set; }

        public ModDependencyForm(string parentTitle, IList<ModDependency> dependencies,
            Func<ModDependency, bool> downloadDependency)
        {
            _dependencies = new List<ModDependency>();
            if (dependencies != null)
            {
                for (int i = 0; i < dependencies.Count; i++)
                    _dependencies.Add(dependencies[i]);
            }
            _downloadDependency = downloadDependency;
            bool single = _dependencies.Count <= 1;

            _rowTitleFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _rowSubtitleFont = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular);
            _hintFont = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular);

            Text = "需要依赖";
            BackColor = PageColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            KeyPreview = true;

            Label caption = new Label();
            caption.Text = "需要依赖";
            caption.Location = new Point(20, 14);
            caption.AutoSize = true;
            caption.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            caption.ForeColor = TextColor;
            Controls.Add(caption);

            string parent = string.IsNullOrEmpty(parentTitle) ? "这个mod" : "《" + parentTitle + "》";
            Label headline = new Label();
            headline.Text = single ? "此mod需要其他mod" : "此mod需要 " + _dependencies.Count + " 个其他mod";
            headline.Location = new Point(20, 50);
            headline.AutoSize = true;
            headline.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            headline.ForeColor = WarningColor;
            Controls.Add(headline);

            Label summary = new Label();
            summary.Text = single
                ? parent + " 需要先安装以下 mod 才能正常运行。已自动跳过已安装的必需依赖。"
                : parent + " 还有以下必需依赖尚未安装。已自动跳过已安装的 mod。";
            summary.Location = new Point(20, 78);
            summary.Size = new Size(500, 36);
            summary.ForeColor = MutedColor;
            Controls.Add(summary);

            int listTop = 118;
            int listHeight;
            if (single)
            {
                listHeight = 72;
                Panel card = BuildCard(_dependencies.Count > 0 ? _dependencies[0] : null, 500, 72, -1);
                if (card != null)
                {
                    card.Location = new Point(20, listTop);
                    Controls.Add(card);
                }
            }
            else
            {
                listHeight = Math.Min(300, _dependencies.Count * 60);
                Panel list = new Panel();
                list.Location = new Point(20, listTop);
                list.Size = new Size(500, listHeight + 2);
                list.BackColor = CardColor;
                list.BorderStyle = BorderStyle.FixedSingle;
                list.AutoScroll = true;
                for (int i = 0; i < _dependencies.Count; i++)
                {
                    Panel row = BuildCard(_dependencies[i], 480, 58, i);
                    if (row == null)
                        continue;
                    row.Location = new Point(0, i * 60);
                    list.Controls.Add(row);
                }
                Controls.Add(list);
            }

            int hintTop = listTop + listHeight + 12;
            Label hint = new Label();
            hint.Text = single
                ? "“下载依赖mod”会跳转到依赖 mod 的详情页；选择“仅下载此mod”则只加入当前文件。"
                : "右侧“下载”会把该依赖加入任务列表；也可以先只下载当前 mod。";
            hint.Location = new Point(20, hintTop);
            hint.Size = new Size(500, 20);
            hint.Font = _hintFont;
            hint.ForeColor = MutedColor;
            Controls.Add(hint);

            int buttonTop = hintTop + 32;
            ClientSize = new Size(540, buttonTop + 54);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Size = new Size(90, 36);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = BorderColor;
            cancel.BackColor = Theme.Surface;
            cancel.ForeColor = Theme.TextStrong;
            cancel.Font = new Font("Microsoft YaHei UI", 9F);
            cancel.Cursor = Cursors.Hand;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            if (single)
            {
                Button only = new Button();
                only.Text = "仅下载此mod";
                only.Size = new Size(126, 36);
                only.Location = new Point(ClientSize.Width - 20 - 148 - 10 - 126, buttonTop);
                only.FlatStyle = FlatStyle.Flat;
                only.FlatAppearance.BorderColor = BorderColor;
                only.BackColor = Theme.Surface;
                only.ForeColor = Theme.TextStrong;
                only.Font = new Font("Microsoft YaHei UI", 9F);
                only.Cursor = Cursors.Hand;
                only.Click += delegate
                {
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(only);

                Button dependency = new Button();
                dependency.Text = "下载依赖mod";
                dependency.Size = new Size(148, 36);
                dependency.Location = new Point(ClientSize.Width - 20 - 148, buttonTop);
                dependency.FlatStyle = FlatStyle.Flat;
                dependency.FlatAppearance.BorderSize = 0;
                dependency.FlatAppearance.MouseOverBackColor = AccentHover;
                dependency.FlatAppearance.MouseDownBackColor = AccentDown;
                dependency.BackColor = AccentColor;
                dependency.ForeColor = Color.White;
                dependency.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                dependency.Cursor = Cursors.Hand;
                bool canNavigate = _dependencies.Count > 0 && _dependencies[0].Project != null;
                dependency.Enabled = canNavigate;
                dependency.Click += delegate
                {
                    if (_dependencies.Count == 0)
                        return;
                    NavigateToDependency = _dependencies[0];
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(dependency);

                cancel.Location = new Point(ClientSize.Width - 20 - 148 - 10 - 126 - 10 - 90, buttonTop);
                AcceptButton = only;
            }
            else
            {
                Button confirm = new Button();
                confirm.Text = "下载此mod";
                confirm.Size = new Size(126, 36);
                confirm.Location = new Point(ClientSize.Width - 20 - 126, buttonTop);
                confirm.FlatStyle = FlatStyle.Flat;
                confirm.FlatAppearance.BorderSize = 0;
                confirm.FlatAppearance.MouseOverBackColor = AccentHover;
                confirm.FlatAppearance.MouseDownBackColor = AccentDown;
                confirm.BackColor = AccentColor;
                confirm.ForeColor = Color.White;
                confirm.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                confirm.Cursor = Cursors.Hand;
                confirm.Click += delegate
                {
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(confirm);

                cancel.Location = new Point(ClientSize.Width - 20 - 126 - 10 - 90, buttonTop);
                AcceptButton = confirm;
            }
            CancelButton = cancel;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowChrome(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rowTitleFont.Dispose();
                _rowSubtitleFont.Dispose();
                _hintFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private Panel BuildCard(ModDependency dependency, int width, int height, int rowIndex)
        {
            Panel card = new Panel();
            card.Size = new Size(width, height);
            card.BackColor = CardColor;

            if (dependency == null)
            {
                Label empty = new Label();
                empty.Text = "依赖信息不可用";
                empty.Dock = DockStyle.Fill;
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.ForeColor = MutedColor;
                card.Controls.Add(empty);
                return card;
            }

            PictureBox icon = new PictureBox();
            icon.Location = new Point(12, Math.Max(8, (height - 40) / 2));
            icon.Size = new Size(40, 40);
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.Image = ImageCache.Placeholder;
            icon.BackColor = CardColor;
            card.Controls.Add(icon);
            string iconUrl = dependency.Project == null ? null : dependency.Project.IconUrl;
            LoadIconAsync(icon, iconUrl);

            int textLeft = 62;
            int buttonWidth = rowIndex >= 0 ? 74 : 0;
            int textWidth = width - textLeft - buttonWidth - 14;
            Label title = new Label();
            title.Text = dependency.Title ?? "依赖 mod";
            title.Location = new Point(textLeft, height >= 60 ? 9 : 12);
            title.Size = new Size(textWidth, 20);
            title.AutoEllipsis = true;
            title.Font = _rowTitleFont;
            title.ForeColor = TextColor;
            card.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = string.IsNullOrEmpty(dependency.Error)
                ? (dependency.Subtitle ?? "")
                : "无法自动下载：" + dependency.Error;
            subtitle.Location = new Point(textLeft, height >= 60 ? 30 : 34);
            subtitle.Size = new Size(textWidth, 18);
            subtitle.AutoEllipsis = true;
            subtitle.Font = _rowSubtitleFont;
            subtitle.ForeColor = string.IsNullOrEmpty(dependency.Error) ? MutedColor : WarningColor;
            card.Controls.Add(subtitle);

            if (rowIndex >= 0)
            {
                Button download = new Button();
                download.Size = new Size(buttonWidth, 30);
                download.Location = new Point(width - buttonWidth - 10, (height - 30) / 2);
                download.Text = dependency.CanDownload ? "下载" : "手动安装";
                download.Enabled = dependency.CanDownload;
                download.FlatStyle = FlatStyle.Flat;
                download.FlatAppearance.BorderColor = BorderColor;
                download.BackColor = Theme.Surface;
                download.ForeColor = Theme.TextStrong;
                download.Font = new Font("Microsoft YaHei UI", 8.25F);
                download.Cursor = dependency.CanDownload ? Cursors.Hand : Cursors.Default;
                download.Click += delegate { DownloadRow(dependency, download); };
                card.Controls.Add(download);
            }

            return card;
        }

        private async void LoadIconAsync(PictureBox box, string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            Image image = await ImageCache.GetAsync(url, System.Threading.CancellationToken.None);
            if (box != null && !box.IsDisposed)
                box.Image = image;
        }

        private void DownloadRow(ModDependency dependency, Button button)
        {
            if (dependency == null || _downloadDependency == null || !dependency.CanDownload)
                return;
            try
            {
                if (_downloadDependency(dependency))
                {
                    button.Text = "已加入";
                    button.Enabled = false;
                    button.ForeColor = AccentColor;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "加入下载队列失败：" + ex.Message, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
