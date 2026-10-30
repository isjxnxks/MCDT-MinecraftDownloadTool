using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class InstallTarget
    {
        public string Title { get; private set; }
        public string Subtitle { get; private set; }
        public string DirectoryPath { get; private set; }
        public TargetIconKind Icon { get; private set; }
        public string IconPath { get; private set; }

        public InstallTarget(string title, string subtitle, string directoryPath, TargetIconKind icon)
            : this(title, subtitle, directoryPath, icon, null)
        {
        }

        public InstallTarget(string title, string subtitle, string directoryPath, TargetIconKind icon, string iconPath)
        {
            Title = title;
            Subtitle = subtitle;
            DirectoryPath = directoryPath;
            Icon = icon;
            IconPath = iconPath;
        }

        public override string ToString()
        {
            return Title;
        }
    }

    public sealed class TargetPickerForm : Form
    {
        private static Color PageColor { get { return Theme.PageAlt; } }
        private static Color AccentColor { get { return Theme.Accent; } }
        private static Color AccentHover { get { return Theme.AccentHover; } }
        private static Color AccentDown { get { return Theme.AccentDown; } }
        private static Color TextColor { get { return Theme.Text; } }
        private static Color MutedColor { get { return Theme.Muted; } }
        private static Color BorderColor { get { return Theme.BorderStrong; } }
        private static Color SelectedBack { get { return Theme.SelectedBack; } }
        private static Color HoverBack { get { return Theme.HoverBack; } }
        private static Color TitleColor { get { return Theme.TitleColor; } }

        private readonly List<InstallTarget> _allTargets;
        private readonly List<InstallTarget> _filtered = new List<InstallTarget>();
        private readonly ListBox _list;
        private readonly TextBox _searchBox;
        private readonly Label _emptyLabel;
        private readonly Label _countLabel;
        private readonly Button _confirmButton;
        private readonly Font _titleFont;
        private readonly Font _subtitleFont;
        private int _hoverIndex = -1;

        public InstallTarget SelectedTarget { get; private set; }

        public TargetPickerForm(IList<InstallTarget> targets, string subtitle)
        {
            _allTargets = new List<InstallTarget>();
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                    _allTargets.Add(targets[i]);
            }

            Text = "选择安装位置";
            ClientSize = new Size(520, 520);
            BackColor = PageColor;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            KeyPreview = true;

            _titleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            _subtitleFont = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Regular);

            Label title = new Label();
            title.Text = "选择安装位置";
            title.Location = new Point(20, 14);
            title.AutoSize = true;
            title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            title.ForeColor = TextColor;
            Controls.Add(title);

            Label hint = new Label();
            hint.Text = subtitle ?? "";
            hint.Location = new Point(20, 44);
            hint.Size = new Size(480, 18);
            hint.AutoEllipsis = true;
            hint.ForeColor = MutedColor;
            Controls.Add(hint);

            Label searchLabel = new Label();
            searchLabel.Text = "搜索";
            searchLabel.Location = new Point(20, 76);
            searchLabel.AutoSize = true;
            searchLabel.ForeColor = TextColor;
            Controls.Add(searchLabel);

            _searchBox = new TextBox();
            _searchBox.Location = new Point(66, 71);
            _searchBox.Size = new Size(434, 26);
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.BackColor = Theme.Surface;
            _searchBox.ForeColor = TextColor;
            _searchBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            _searchBox.TextChanged += delegate { ApplyFilter(); };
            _searchBox.KeyDown += SearchBox_KeyDown;
            Controls.Add(_searchBox);

            _list = new ListBox();
            _list.Location = new Point(20, 108);
            _list.Size = new Size(480, 320);
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 52;
            _list.IntegralHeight = false;
            _list.BackColor = Theme.Surface;
            _list.ForeColor = TextColor;
            _list.Font = new Font("Microsoft YaHei UI", 9F);
            _list.DrawItem += List_DrawItem;
            _list.MouseMove += List_MouseMove;
            _list.MouseLeave += List_MouseLeave;
            _list.KeyDown += List_KeyDown;
            _list.DoubleClick += delegate { Confirm(); };
            Controls.Add(_list);

            _emptyLabel = new Label();
            _emptyLabel.Location = new Point(20, 108);
            _emptyLabel.Size = new Size(480, 320);
            _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            _emptyLabel.ForeColor = MutedColor;
            _emptyLabel.BackColor = Theme.Surface;
            _emptyLabel.Text = "没有匹配的安装位置";
            _emptyLabel.Visible = false;
            Controls.Add(_emptyLabel);
            _emptyLabel.BringToFront();

            _countLabel = new Label();
            _countLabel.Location = new Point(20, 438);
            _countLabel.Size = new Size(240, 20);
            _countLabel.ForeColor = MutedColor;
            _countLabel.Font = new Font("Microsoft YaHei UI", 8.25F);
            Controls.Add(_countLabel);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(306, 466);
            cancel.Size = new Size(90, 34);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = BorderColor;
            cancel.BackColor = Theme.Surface;
            cancel.ForeColor = Theme.TextStrong;
            cancel.Font = new Font("Microsoft YaHei UI", 9F);
            cancel.Cursor = Cursors.Hand;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            _confirmButton = new Button();
            _confirmButton.Text = "确认下载";
            _confirmButton.Location = new Point(404, 466);
            _confirmButton.Size = new Size(96, 34);
            _confirmButton.FlatStyle = FlatStyle.Flat;
            _confirmButton.FlatAppearance.BorderSize = 0;
            _confirmButton.FlatAppearance.MouseOverBackColor = AccentHover;
            _confirmButton.FlatAppearance.MouseDownBackColor = AccentDown;
            _confirmButton.BackColor = AccentColor;
            _confirmButton.ForeColor = Color.White;
            _confirmButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _confirmButton.Cursor = Cursors.Hand;
            _confirmButton.Click += delegate { Confirm(); };
            Controls.Add(_confirmButton);

            CancelButton = cancel;
            AcceptButton = _confirmButton;

            ApplyFilter();
            Shown += delegate
            {
                _searchBox.Focus();
                if (_allTargets.Count > 0)
                    _list.SelectedIndex = 0;
            };
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
                _titleFont.Dispose();
                _subtitleFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ApplyFilter()
        {
            string query = _searchBox.Text == null ? "" : _searchBox.Text.Trim();
            InstallTarget previous = _list.SelectedItem as InstallTarget;

            _filtered.Clear();
            for (int i = 0; i < _allTargets.Count; i++)
            {
                InstallTarget target = _allTargets[i];
                if (Matches(target, query))
                    _filtered.Add(target);
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                for (int i = 0; i < _filtered.Count; i++)
                    _list.Items.Add(_filtered[i]);
            }
            finally
            {
                _list.EndUpdate();
            }

            int select = -1;
            if (previous != null)
            {
                for (int i = 0; i < _filtered.Count; i++)
                {
                    if (string.Equals(_filtered[i].DirectoryPath, previous.DirectoryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        select = i;
                        break;
                    }
                }
            }
            if (select < 0 && _filtered.Count > 0)
                select = 0;
            _list.SelectedIndex = select;

            _emptyLabel.Visible = _filtered.Count == 0;
            if (_filtered.Count == 0)
                _emptyLabel.Text = string.IsNullOrEmpty(query) ? "没有可用的安装位置" : "没有匹配“" + query + "”的安装位置";
            _countLabel.Text = string.IsNullOrEmpty(query)
                ? "共 " + _allTargets.Count + " 个安装位置"
                : "匹配 " + _filtered.Count + " / " + _allTargets.Count + " 个安装位置";
            _confirmButton.Enabled = _filtered.Count > 0;
        }

        private static bool Matches(InstallTarget target, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
            return (target.Title != null && target.Title.IndexOf(query, ignoreCase) >= 0) ||
                   (target.Subtitle != null && target.Subtitle.IndexOf(query, ignoreCase) >= 0) ||
                   (target.DirectoryPath != null && target.DirectoryPath.IndexOf(query, ignoreCase) >= 0);
        }

        private void Confirm()
        {
            InstallTarget target = _list.SelectedItem as InstallTarget;
            if (target == null)
                return;
            SelectedTarget = target;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && _filtered.Count > 0)
            {
                _list.Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Confirm();
            }
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Confirm();
            }
        }

        private void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count)
                return;
            InstallTarget item = _list.Items[e.Index] as InstallTarget;
            if (item == null)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color background = selected ? SelectedBack : (e.Index == _hoverIndex ? HoverBack : Theme.Surface);
            using (SolidBrush brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);

            TargetIcons.Draw(e.Graphics, new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 10, 32, 32),
                item.Icon, item.IconPath);

            int textLeft = e.Bounds.Left + 54;
            int textWidth = e.Bounds.Width - 54 - 12;
            Rectangle titleRect = new Rectangle(textLeft, e.Bounds.Top + 5, textWidth, 22);
            Rectangle subtitleRect = new Rectangle(textLeft, e.Bounds.Top + 26, textWidth, 19);
            TextRenderer.DrawText(e.Graphics, item.Title, _titleFont, titleRect,
                selected ? TitleColor : TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, item.Subtitle, _subtitleFont, subtitleRect,
                MutedColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void List_MouseMove(object sender, MouseEventArgs e)
        {
            int index = _list.IndexFromPoint(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                _list.Invalidate();
            }
        }

        private void List_MouseLeave(object sender, EventArgs e)
        {
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                _list.Invalidate();
            }
        }
    }
}
