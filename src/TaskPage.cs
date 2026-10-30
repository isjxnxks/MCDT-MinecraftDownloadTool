using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class TaskPage : UserControl
    {
        private static Color PageColor { get { return Theme.Page; } }
        private static Color AccentColor { get { return Theme.Accent; } }
        private static Color TextColor { get { return Theme.Text; } }
        private static Color MutedColor { get { return Theme.Muted; } }
        private static Color BorderColor { get { return Theme.Border; } }
        private static Color DangerColor { get { return Theme.Danger; } }

        private readonly DownloadTaskManager _manager;
        private readonly FlowLayoutPanel _list;
        private readonly Label _summaryLabel;
        private readonly Label _logTitle;
        private readonly TextBox _logBox;
        private readonly Button _cancelButton;
        private readonly Button _clearButton;
        private readonly System.Windows.Forms.Timer _timer;
        private string _selectedId;
        private int _lastLogLength = -1;
        private int _lastTaskCount = -1;

        public TaskPage(DownloadTaskManager manager)
        {
            _manager = manager;
            BackColor = PageColor;
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = new Size(900, 452);

            Label title = CreateLabel("下载任务", 24, 8, 11F, FontStyle.Bold, TextColor, true);

            _summaryLabel = CreateLabel("", 24, 34, 8.75F, FontStyle.Regular, MutedColor, false);
            _summaryLabel.Size = new Size(610, 20);

            _cancelButton = CreateButton("取消当前任务", 662, 8, 110, 30, false);
            _cancelButton.Click += delegate { CancelCurrent(); };
            Controls.Add(_cancelButton);

            _clearButton = CreateButton("清除已结束", 780, 8, 96, 30, false);
            _clearButton.Click += delegate { _manager.ClearFinished(); };
            Controls.Add(_clearButton);

            _list = new FlowLayoutPanel();
            _list.Location = new Point(0, 62);
            _list.Size = new Size(900, 240);
            _list.BackColor = PageColor;
            _list.AutoScroll = true;
            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.Padding = new Padding(18, 4, 18, 6);
            Controls.Add(_list);

            _logTitle = CreateLabel("任务日志", 24, 310, 9F, FontStyle.Bold, TextColor, true);

            _logBox = new TextBox();
            _logBox.Location = new Point(24, 330);
            _logBox.Size = new Size(852, 110);
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.BackColor = Theme.Surface;
            _logBox.ForeColor = Theme.LogText;
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.Font = new Font("Microsoft YaHei UI", 8.25F);
            _logBox.TabStop = false;
            _logBox.Text = "选择任务后查看详细日志。";
            Controls.Add(_logBox);

            _manager.TasksChanged += Manager_TasksChanged;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += Timer_Tick;
            _timer.Start();
            Rebuild();
            UpdateSummary();
        }

        private void Manager_TasksChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new EventHandler(Manager_TasksChanged), sender, e); } catch { }
                return;
            }
            Rebuild();
            UpdateSummary();
            UpdateLog();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            UpdateSummary();
            for (int i = 0; i < _list.Controls.Count; i++)
            {
                TaskRow row = _list.Controls[i] as TaskRow;
                if (row != null)
                    row.UpdateFromTask();
            }
            UpdateLog();
        }

        private void UpdateSummary()
        {
            List<DownloadTask> tasks = _manager.Snapshot();
            int waiting = 0;
            int running = 0;
            int completed = 0;
            int failed = 0;
            int canceled = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                switch (tasks[i].State)
                {
                    case DownloadTaskState.Waiting: waiting++; break;
                    case DownloadTaskState.Running: running++; break;
                    case DownloadTaskState.Completed: completed++; break;
                    case DownloadTaskState.Failed: failed++; break;
                    case DownloadTaskState.Canceled: canceled++; break;
                }
            }

            if (tasks.Count == 0)
                _summaryLabel.Text = "任务队列为空。下载内容或游戏版本后会在这里按顺序执行。";
            else
            {
                string text = "等待 " + waiting + " · 进行中 " + running + " · 已完成 " + completed;
                if (failed > 0)
                    text += " · 失败 " + failed;
                if (canceled > 0)
                    text += " · 已取消 " + canceled;
                _summaryLabel.Text = text;
            }
            _summaryLabel.ForeColor = failed > 0 ? DangerColor : MutedColor;
            _cancelButton.Enabled = running > 0;
            _clearButton.Enabled = completed + failed + canceled > 0;
            if (_lastTaskCount != tasks.Count)
            {
                _lastTaskCount = tasks.Count;
                UpdateLog();
            }
        }

        private void Rebuild()
        {
            List<DownloadTask> tasks = _manager.Snapshot();
            bool hasSelected = false;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (string.Equals(tasks[i].Id, _selectedId, StringComparison.Ordinal))
                {
                    hasSelected = true;
                    break;
                }
            }
            if (!hasSelected)
            {
                _selectedId = null;
                for (int i = 0; i < tasks.Count; i++)
                {
                    if (tasks[i].State == DownloadTaskState.Running)
                    {
                        _selectedId = tasks[i].Id;
                        break;
                    }
                }
                if (_selectedId == null && tasks.Count > 0)
                    _selectedId = tasks[0].Id;
            }

            _lastLogLength = -1;
            while (_list.Controls.Count > 0)
            {
                Control control = _list.Controls[0];
                _list.Controls.RemoveAt(0);
                control.Dispose();
            }

            if (tasks.Count == 0)
            {
                Label empty = new Label();
                empty.Text = "还没有下载任务。\n\n在“游戏本体”或内容页面点击下载，任务会依次加入这里执行。";
                empty.Size = new Size(840, 120);
                empty.Margin = new Padding(0, 24, 0, 0);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                empty.ForeColor = MutedColor;
                empty.Font = new Font("Microsoft YaHei UI", 9.5F);
                _list.Controls.Add(empty);
                return;
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                DownloadTask task = tasks[i];
                TaskRow row = new TaskRow(task, this);
                row.SetSelected(string.Equals(task.Id, _selectedId, StringComparison.Ordinal));
                _list.Controls.Add(row);
            }
        }

        internal bool IsSelected(DownloadTask task)
        {
            return task != null && string.Equals(task.Id, _selectedId, StringComparison.Ordinal);
        }

        internal void Select(DownloadTask task)
        {
            if (task == null)
                return;
            _selectedId = task.Id;
            for (int i = 0; i < _list.Controls.Count; i++)
            {
                TaskRow row = _list.Controls[i] as TaskRow;
                if (row != null)
                    row.SetSelected(row.Task == task);
            }
            _lastLogLength = -1;
            UpdateLog();
        }

        internal void HandleAction(DownloadTask task)
        {
            Select(task);
            DownloadTaskState state = task.State;
            if (state == DownloadTaskState.Running)
                _manager.Cancel(task);
            else if (state == DownloadTaskState.Failed || state == DownloadTaskState.Canceled)
                _manager.Retry(task);
            else
                _manager.Remove(task);
        }

        private void CancelCurrent()
        {
            List<DownloadTask> tasks = _manager.Snapshot();
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].State == DownloadTaskState.Running)
                {
                    _manager.Cancel(tasks[i]);
                    return;
                }
            }
        }

        private void UpdateLog()
        {
            if (_logBox == null || _logBox.IsDisposed)
                return;
            DownloadTask selected = null;
            List<DownloadTask> tasks = _manager.Snapshot();
            for (int i = 0; i < tasks.Count; i++)
            {
                if (string.Equals(tasks[i].Id, _selectedId, StringComparison.Ordinal))
                {
                    selected = tasks[i];
                    break;
                }
            }
            if (selected == null)
            {
                _logTitle.Text = "任务日志";
                if (_logBox.Text != "选择任务后查看详细日志。")
                    _logBox.Text = "选择任务后查看详细日志。";
                _lastLogLength = -1;
                return;
            }

            _logTitle.Text = "任务日志 · " + selected.Title;
            int length = selected.LogLength;
            if (length == _lastLogLength)
                return;
            _lastLogLength = length;
            string text = selected.LogText;
            if (string.IsNullOrEmpty(text))
                text = "任务尚未产生日志。";
            _logBox.Text = text;
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                    _timer.Stop();
                if (_manager != null)
                    _manager.TasksChanged -= Manager_TasksChanged;
            }
            base.Dispose(disposing);
        }

        public void ApplyTheme()
        {
            Theme.Apply(this);
            Invalidate(true);
        }

        private Label CreateLabel(string text, int x, int y, float size, FontStyle style, Color color, bool autoSize)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Font = new Font("Microsoft YaHei UI", size, style);
            label.ForeColor = color;
            label.AutoSize = autoSize;
            label.BackColor = Color.Transparent;
            Controls.Add(label);
            return label;
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
            button.BackColor = primary ? AccentColor : Theme.Surface;
            button.ForeColor = primary ? Color.White : Theme.TextStrong;
            button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private sealed class TaskRow : Panel
        {
            private readonly TaskPage _owner;
            private readonly Label _title;
            private readonly Label _subtitle;
            private readonly Label _stage;
            private readonly Label _percent;
            private readonly ProgressBar _progress;
            private readonly Button _action;
            private readonly Font _titleFont;
            private bool _selected;
            private DownloadTaskState _lastState = (DownloadTaskState)(-1);
            private string _lastStage;
            private bool _lastIndeterminate;
            private int _lastValue = -1;

            public DownloadTask Task { get; private set; }

            public TaskRow(DownloadTask task, TaskPage owner)
            {
                Task = task;
                _owner = owner;
                DoubleBuffered = true;
                BackColor = Theme.Surface;
                Width = 840;
                Height = 92;
                Margin = new Padding(0, 0, 0, 8);
                Cursor = Cursors.Hand;
                _titleFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);

                _title = new Label();
                _title.Location = new Point(58, 7);
                _title.Size = new Size(660, 22);
                _title.Font = _titleFont;
                _title.ForeColor = TextColor;
                _title.AutoEllipsis = true;
                _title.BackColor = Color.Transparent;
                _title.Cursor = Cursors.Hand;
                _title.Click += delegate { _owner.Select(Task); };
                Controls.Add(_title);

                _subtitle = new Label();
                _subtitle.Location = new Point(58, 29);
                _subtitle.Size = new Size(660, 18);
                _subtitle.Font = new Font("Microsoft YaHei UI", 8.25F);
                _subtitle.ForeColor = MutedColor;
                _subtitle.AutoEllipsis = true;
                _subtitle.BackColor = Color.Transparent;
                _subtitle.Cursor = Cursors.Hand;
                _subtitle.Click += delegate { _owner.Select(Task); };
                Controls.Add(_subtitle);

                _stage = new Label();
                _stage.Location = new Point(58, 50);
                _stage.Size = new Size(600, 18);
                _stage.Font = new Font("Microsoft YaHei UI", 8.5F);
                _stage.ForeColor = Theme.StageText;
                _stage.AutoEllipsis = true;
                _stage.BackColor = Color.Transparent;
                _stage.Cursor = Cursors.Hand;
                _stage.Click += delegate { _owner.Select(Task); };
                Controls.Add(_stage);

                _progress = new ThemedProgressBar();
                _progress.Location = new Point(58, 73);
                _progress.Size = new Size(600, 8);
                _progress.Minimum = 0;
                _progress.Maximum = 1000;
                _progress.Style = ProgressBarStyle.Continuous;
                Controls.Add(_progress);

                _percent = new Label();
                _percent.Location = new Point(666, 66);
                _percent.Size = new Size(58, 18);
                _percent.Font = new Font("Microsoft YaHei UI", 8.25F, FontStyle.Bold);
                _percent.ForeColor = AccentColor;
                _percent.TextAlign = ContentAlignment.MiddleRight;
                _percent.BackColor = Color.Transparent;
                _percent.Cursor = Cursors.Hand;
                _percent.Click += delegate { _owner.Select(Task); };
                Controls.Add(_percent);

                _action = new Button();
                _action.Location = new Point(748, 30);
                _action.Size = new Size(80, 32);
                _action.FlatStyle = FlatStyle.Flat;
                _action.FlatAppearance.BorderColor = Theme.BorderStrong;
                _action.BackColor = Theme.Surface;
                _action.ForeColor = Theme.TextStrong;
                _action.Font = new Font("Microsoft YaHei UI", 8.75F);
                _action.Cursor = Cursors.Hand;
                _action.Click += delegate { _owner.HandleAction(Task); };
                Controls.Add(_action);

                _title.Text = task.Title;
                _subtitle.Text = task.Subtitle;
                Click += delegate { _owner.Select(Task); };
                UpdateFromTask();
            }

            public void SetSelected(bool selected)
            {
                _selected = selected;
                BackColor = selected ? Theme.SelectedBack : Theme.Surface;
                Invalidate();
            }

            public void UpdateFromTask()
            {
                DownloadTaskState state = Task.State;
                string stage = Task.Stage;
                if (state != _lastState || !string.Equals(stage, _lastStage, StringComparison.Ordinal))
                {
                    _lastState = state;
                    _lastStage = stage;
                    _stage.Text = BuildStageText(state, stage, Task.Error);
                    _stage.ForeColor = state == DownloadTaskState.Failed ? DangerColor : Theme.StageText;
                    ApplyAction(state);
                }

                DownloadStats stats = Task.Stats;
                if (stats == null)
                    return;
                bool indeterminate = stats.Indeterminate && state == DownloadTaskState.Running;
                if (indeterminate != _lastIndeterminate)
                {
                    _lastIndeterminate = indeterminate;
                    _progress.Style = indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                }

                if (state == DownloadTaskState.Completed)
                {
                    if (_lastValue != 1000)
                    {
                        _lastValue = 1000;
                        if (_progress.Style != ProgressBarStyle.Continuous)
                            _progress.Style = ProgressBarStyle.Continuous;
                        _progress.Value = 1000;
                    }
                    _percent.Text = "100%";
                    _percent.ForeColor = AccentColor;
                    return;
                }
                if (state == DownloadTaskState.Failed)
                {
                    _percent.Text = "失败";
                    _percent.ForeColor = DangerColor;
                    return;
                }
                if (state == DownloadTaskState.Canceled)
                {
                    _percent.Text = "已取消";
                    _percent.ForeColor = MutedColor;
                    return;
                }
                if (state == DownloadTaskState.Waiting)
                {
                    _percent.Text = "等待";
                    _percent.ForeColor = MutedColor;
                    return;
                }

                if (indeterminate)
                {
                    _percent.Text = "…";
                    _percent.ForeColor = AccentColor;
                    return;
                }
                int value = (int)Math.Round(stats.Fraction * 1000);
                if (value < 0) value = 0;
                if (value > 1000) value = 1000;
                if (value != _lastValue)
                {
                    _lastValue = value;
                    if (_progress.Style != ProgressBarStyle.Continuous)
                        _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Value = value;
                }
                _percent.Text = (value / 10.0).ToString("0.0") + "%";
                _percent.ForeColor = AccentColor;
            }

            private void ApplyAction(DownloadTaskState state)
            {
                _action.FlatAppearance.BorderSize = 1;
                _action.BackColor = Theme.Surface;
                _action.ForeColor = Theme.TextStrong;
                switch (state)
                {
                    case DownloadTaskState.Running:
                        _action.Text = "取消";
                        break;
                    case DownloadTaskState.Failed:
                        _action.Text = "重试";
                        _action.BackColor = AccentColor;
                        _action.ForeColor = Color.White;
                        _action.FlatAppearance.BorderSize = 0;
                        break;
                    case DownloadTaskState.Canceled:
                        _action.Text = "重试";
                        break;
                    case DownloadTaskState.Completed:
                        _action.Text = "移除";
                        break;
                    default:
                        _action.Text = "移除";
                        break;
                }
            }

            private static string BuildStageText(DownloadTaskState state, string stage, string error)
            {
                if (state == DownloadTaskState.Failed)
                {
                    string message = string.IsNullOrEmpty(error) ? "下载失败" : error;
                    message = message.Replace("\r", " ").Replace("\n", " ");
                    if (message.Length > 140)
                        message = message.Substring(0, 140) + "…";
                    return "失败：" + message;
                }
                return string.IsNullOrEmpty(stage) ? "等待中" : stage;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                TargetIcons.Draw(e.Graphics, new Rectangle(12, 26, 36, 36), Task.Icon, Task.IconPath);
                using (Pen pen = new Pen(_selected ? Theme.SelectedBorder : BorderColor))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _titleFont.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
