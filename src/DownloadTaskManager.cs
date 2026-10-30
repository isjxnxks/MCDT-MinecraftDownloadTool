using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCDT
{
    public enum DownloadTaskState
    {
        Waiting,
        Running,
        Completed,
        Failed,
        Canceled
    }

    public sealed class DownloadTask
    {
        private readonly object _sync = new object();
        private readonly List<string> _log = new List<string>();
        private DownloadTaskState _state;
        private string _stage;
        private string _error;
        private string _resultText;

        internal Func<DownloadTask, CancellationToken, Task> Work;
        internal CancellationTokenSource Cancellation;

        public DownloadTask(string title, string subtitle, TargetIconKind icon, string iconPath)
        {
            Id = Guid.NewGuid().ToString("N");
            Title = string.IsNullOrEmpty(title) ? "下载任务" : title;
            Subtitle = subtitle ?? "";
            Icon = icon;
            IconPath = iconPath;
            CreatedAt = DateTime.Now;
            _state = DownloadTaskState.Waiting;
            _stage = "等待中";
            Stats = new DownloadStats();
            Stats.Indeterminate = true;
        }

        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Subtitle { get; private set; }
        public TargetIconKind Icon { get; private set; }
        public string IconPath { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public volatile DownloadStats Stats;

        public DownloadTaskState State
        {
            get { lock (_sync) return _state; }
            internal set
            {
                lock (_sync)
                    _state = value;
            }
        }

        public string Stage
        {
            get { lock (_sync) return _stage; }
            set
            {
                lock (_sync)
                    _stage = value ?? "";
            }
        }

        public string Error
        {
            get { lock (_sync) return _error; }
            internal set
            {
                lock (_sync)
                    _error = value;
            }
        }

        public string ResultText
        {
            get { lock (_sync) return _resultText; }
            set
            {
                lock (_sync)
                    _resultText = value;
            }
        }

        public string LogText
        {
            get
            {
                lock (_sync)
                    return string.Join(Environment.NewLine, _log.ToArray());
            }
        }

        public int LogLength
        {
            get
            {
                lock (_sync)
                {
                    int length = 0;
                    for (int i = 0; i < _log.Count; i++)
                        length += _log[i].Length + 2;
                    return length;
                }
            }
        }

        public bool IsActive
        {
            get
            {
                DownloadTaskState state = State;
                return state == DownloadTaskState.Waiting || state == DownloadTaskState.Running;
            }
        }

        public bool IsFinished
        {
            get
            {
                DownloadTaskState state = State;
                return state == DownloadTaskState.Completed ||
                       state == DownloadTaskState.Failed ||
                       state == DownloadTaskState.Canceled;
            }
        }

        public void Log(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (text ?? "");
            lock (_sync)
            {
                _log.Add(line);
                while (_log.Count > 600)
                    _log.RemoveAt(0);
            }
        }

        internal void ResetForRetry()
        {
            lock (_sync)
            {
                _state = DownloadTaskState.Waiting;
                _stage = "等待重试";
                _error = null;
                _resultText = null;
                _log.Add("------------------------------------------------------------");
                _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  重新加入下载队列");
            }
            Stats = new DownloadStats();
            Stats.Indeterminate = true;
        }
    }

    public sealed class QueueIdleEventArgs : EventArgs
    {
        public int Completed { get; private set; }
        public int Failed { get; private set; }
        public int Canceled { get; private set; }

        public QueueIdleEventArgs(int completed, int failed, int canceled)
        {
            Completed = completed;
            Failed = failed;
            Canceled = canceled;
        }
    }

    public sealed class DownloadTaskManager : IDisposable
    {
        private readonly object _sync = new object();
        private readonly List<DownloadTask> _tasks = new List<DownloadTask>();
        private Task _worker;
        private bool _disposed;
        private int _batchCompleted;
        private int _batchFailed;
        private int _batchCanceled;

        public event EventHandler TasksChanged;
        public event EventHandler<QueueIdleEventArgs> QueueIdle;

        public List<DownloadTask> Snapshot()
        {
            lock (_sync)
                return new List<DownloadTask>(_tasks);
        }

        public DownloadTask Enqueue(string title, string subtitle, TargetIconKind icon, string iconPath,
            Func<DownloadTask, CancellationToken, Task> work)
        {
            if (work == null)
                throw new ArgumentNullException("work");
            DownloadTask task = new DownloadTask(title, subtitle, icon, iconPath);
            task.Work = work;
            lock (_sync)
            {
                if (_disposed)
                    throw new ObjectDisposedException("DownloadTaskManager");
                _tasks.Add(task);
            }
            RaiseTasksChanged();
            EnsureWorker();
            return task;
        }

        public bool HasActive
        {
            get
            {
                lock (_sync)
                {
                    for (int i = 0; i < _tasks.Count; i++)
                    {
                        if (_tasks[i].IsActive)
                            return true;
                    }
                    return false;
                }
            }
        }

        public int ActiveCount
        {
            get { return CountByState(true); }
        }

        private int CountByState(bool active)
        {
            lock (_sync)
            {
                int count = 0;
                for (int i = 0; i < _tasks.Count; i++)
                {
                    if (_tasks[i].IsActive == active)
                        count++;
                }
                return count;
            }
        }

        public void Cancel(DownloadTask task)
        {
            if (task == null)
                return;
            bool markWaitingCanceled = false;
            CancellationTokenSource cts = null;
            lock (_sync)
            {
                DownloadTaskState state = task.State;
                if (state == DownloadTaskState.Waiting)
                {
                    task.State = DownloadTaskState.Canceled;
                    task.Stage = "已取消";
                    markWaitingCanceled = true;
                }
                else if (state == DownloadTaskState.Running)
                {
                    cts = task.Cancellation;
                }
            }
            if (markWaitingCanceled)
            {
                lock (_sync)
                    _batchCanceled++;
                RaiseTasksChanged();
            }
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
            }
        }

        public void CancelAll()
        {
            List<DownloadTask> tasks = Snapshot();
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].IsActive)
                    Cancel(tasks[i]);
            }
        }

        public DownloadTask Retry(DownloadTask task)
        {
            if (task == null)
                return null;
            lock (_sync)
            {
                if (task.State != DownloadTaskState.Failed && task.State != DownloadTaskState.Canceled)
                    return task;
                if (task.Work == null)
                    return task;
                task.ResetForRetry();
            }
            RaiseTasksChanged();
            EnsureWorker();
            return task;
        }

        public void Remove(DownloadTask task)
        {
            if (task == null)
                return;
            lock (_sync)
            {
                if (!_tasks.Remove(task))
                    return;
            }
            RaiseTasksChanged();
        }

        public void ClearFinished()
        {
            lock (_sync)
            {
                for (int i = _tasks.Count - 1; i >= 0; i--)
                {
                    DownloadTaskState state = _tasks[i].State;
                    if (state == DownloadTaskState.Completed || state == DownloadTaskState.Failed ||
                        state == DownloadTaskState.Canceled)
                        _tasks.RemoveAt(i);
                }
            }
            RaiseTasksChanged();
        }

        private void EnsureWorker()
        {
            lock (_sync)
            {
                if (_disposed || _worker != null)
                    return;
                _worker = Task.Factory.StartNew(WorkerLoop, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                DownloadTask task = null;
                lock (_sync)
                {
                    if (_disposed)
                        break;
                    for (int i = 0; i < _tasks.Count; i++)
                    {
                        if (_tasks[i].State == DownloadTaskState.Waiting)
                        {
                            task = _tasks[i];
                            break;
                        }
                    }
                    if (task == null)
                    {
                        _worker = null;
                        break;
                    }
                    task.State = DownloadTaskState.Running;
                    task.Stage = "准备中";
                    task.Cancellation = new CancellationTokenSource();
                }

                RaiseTasksChanged();

                CancellationToken token = task.Cancellation.Token;
                bool canceled = false;
                Exception failure = null;
                try
                {
                    task.Work(task, token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                        canceled = true;
                    else
                        failure = ex;
                }

                lock (_sync)
                {
                    if (canceled)
                    {
                        task.State = DownloadTaskState.Canceled;
                        task.Stage = "已取消";
                        _batchCanceled++;
                    }
                    else if (failure != null)
                    {
                        task.State = DownloadTaskState.Failed;
                        string message = failure.Message;
                        task.Error = string.IsNullOrEmpty(message) ? "未知错误" : message;
                        task.Stage = "下载失败";
                        _batchFailed++;
                    }
                    else
                    {
                        task.State = DownloadTaskState.Completed;
                        task.Stage = string.IsNullOrEmpty(task.ResultText) ? "已完成" : task.ResultText;
                        task.ResultText = null;
                        _batchCompleted++;
                    }
                    if (task.Cancellation != null)
                    {
                        task.Cancellation.Dispose();
                        task.Cancellation = null;
                    }
                }
                RaiseTasksChanged();
            }

            int completedCount;
            int failedCount;
            int canceledCount;
            lock (_sync)
            {
                completedCount = _batchCompleted;
                failedCount = _batchFailed;
                canceledCount = _batchCanceled;
                _batchCompleted = 0;
                _batchFailed = 0;
                _batchCanceled = 0;
            }
            if (completedCount + failedCount + canceledCount > 0)
            {
                EventHandler<QueueIdleEventArgs> handler = QueueIdle;
                if (handler != null)
                {
                    try { handler(this, new QueueIdleEventArgs(completedCount, failedCount, canceledCount)); } catch { }
                }
            }
        }

        private void RaiseTasksChanged()
        {
            EventHandler handler = TasksChanged;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); } catch { }
            }
        }

        public void Dispose()
        {
            List<DownloadTask> tasks;
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                tasks = new List<DownloadTask>(_tasks);
            }
            for (int i = 0; i < tasks.Count; i++)
            {
                DownloadTaskState state = tasks[i].State;
                if (state == DownloadTaskState.Waiting)
                    tasks[i].State = DownloadTaskState.Canceled;
                else if (state == DownloadTaskState.Running && tasks[i].Cancellation != null)
                {
                    try { tasks[i].Cancellation.Cancel(); } catch { }
                }
            }
        }
    }
}
