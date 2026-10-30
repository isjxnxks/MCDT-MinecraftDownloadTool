using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MCDT
{
    public sealed class DownloadEngine
    {
        private const int MaxConnections = 16;
        private const long SegmentThreshold = 4L * 1024 * 1024;
        private readonly Action<string> _log;
        private readonly DownloadStats _stats;
        private readonly int _parallelism;
        private readonly SemaphoreSlim _connections = new SemaphoreSlim(MaxConnections, MaxConnections);
        private int _rerouteLogs;
        private int _failureLogs;
        private int _segmentLogs;

        public DownloadEngine(Action<string> log, DownloadStats stats, int parallelism)
        {
            _log = log;
            _stats = stats;
            _parallelism = Math.Max(1, parallelism);
        }

        public async Task RunAsync(IList<DownloadItem> sourceItems, CancellationToken cancellationToken)
        {
            List<DownloadItem> uniqueItems = Deduplicate(sourceItems);
            _stats.Plan(uniqueItems);
            _stats.Indeterminate = false;

            List<DownloadItem> pending = new List<DownloadItem>();
            for (int i = 0; i < uniqueItems.Count; i++)
            {
                DownloadItem item = uniqueItems[i];
                bool exists = false;
                try
                {
                    if (File.Exists(item.DestPath))
                    {
                        long length = new FileInfo(item.DestPath).Length;
                        exists = item.Size <= 0 || length == item.Size;
                    }
                }
                catch
                {
                }

                if (exists)
                    _stats.Complete(item.Index, item.Weight);
                else
                    pending.Add(item);
            }

            if (pending.Count == 0)
            {
                CheckCancellation(cancellationToken);
                Log("本地文件已完整，无需重复下载。");
                return;
            }

            pending.Sort(delegate(DownloadItem a, DownloadItem b) { return b.Size.CompareTo(a.Size); });
            ConcurrentQueue<DownloadItem> queue = new ConcurrentQueue<DownloadItem>(pending);
            ConcurrentQueue<string> failures = new ConcurrentQueue<string>();
            int workerCount = Math.Min(_parallelism, pending.Count);
            Task[] workers = new Task[workerCount];

            for (int i = 0; i < workerCount; i++)
            {
                workers[i] = Task.Run(async delegate
                {
                    DownloadItem item;
                    while (!cancellationToken.IsCancellationRequested && queue.TryDequeue(out item))
                    {
                        try
                        {
                            await DownloadOneAsync(item, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                return;
                            failures.Enqueue(item.Name + "：下载超时");
                        }
                        catch (Exception ex)
                        {
                            failures.Enqueue(item.Name + "：" + ex.Message);
                            if (Interlocked.Increment(ref _failureLogs) <= 5)
                                Log("下载失败：" + item.Name + "（" + ShortError(ex.Message) + "）");
                        }
                    }
                });
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (failures.Count > 0)
            {
                string first;
                failures.TryPeek(out first);
                throw new InstallException("有 " + failures.Count + " 个文件下载失败。首个错误：" + first + "。请检查网络后重试。");
            }

            if (_rerouteLogs > 8)
                Log("已从镜像源回退到官方源 " + _rerouteLogs + " 次。");
        }

        private async Task DownloadOneAsync(DownloadItem item, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            for (int sourceIndex = 0; sourceIndex < item.Urls.Count; sourceIndex++)
            {
                string url = item.Urls[sourceIndex];
                bool transient = true;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _stats.ResetActive(item.Index);
                    try
                    {
                        await DownloadFromUrlAsync(item, url, sourceIndex + 1 < item.Urls.Count, cancellationToken).ConfigureAwait(false);
                        _stats.Complete(item.Index, item.Weight);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lastError = new TimeoutException("下载超时");
                    }
                    catch (HttpSourceException ex)
                    {
                        lastError = ex;
                        transient = HttpUtil.IsTransient(ex.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        transient = !(ex is InvalidDataException);
                    }
                    finally
                    {
                        _stats.ResetActive(item.Index);
                    }

                    if (!transient || attempt == 1)
                        break;
                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                }

                if (sourceIndex + 1 < item.Urls.Count)
                {
                    int count = Interlocked.Increment(ref _rerouteLogs);
                    if (count <= 8)
                        Log("镜像下载失败，切换官方源：" + item.Name);
                }
            }

            throw new InstallException(lastError == null ? "未知网络错误" : lastError.Message, lastError);
        }

        private async Task DownloadFromUrlAsync(DownloadItem item, string url, bool allowSlowAbort, CancellationToken cancellationToken)
        {
            PathUtil.EnsureParent(item.DestPath);
            if (item.Size >= SegmentThreshold)
            {
                bool segmented = await TryDownloadSegmentedAsync(item, url, allowSlowAbort, cancellationToken).ConfigureAwait(false);
                if (segmented)
                    return;
            }
            await DownloadSingleStreamFromUrlAsync(item, url, allowSlowAbort, cancellationToken).ConfigureAwait(false);
        }

        private async Task DownloadSingleStreamFromUrlAsync(DownloadItem item, string url, bool allowSlowAbort, CancellationToken cancellationToken)
        {
            PathUtil.EnsureParent(item.DestPath);
            string part = item.DestPath + ".part";
            if (File.Exists(part))
                File.Delete(part);

            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            stall.CancelAfter(TimeSpan.FromSeconds(30));
            long totalRead = 0;
            long speedWindowBytes = 0;
            int slowWindows = 0;
            bool connectionHeld = false;
            Stopwatch speedWindow = Stopwatch.StartNew();
            try
            {
                await _connections.WaitAsync(stall.Token).ConfigureAwait(false);
                connectionHeld = true;
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                using (HttpResponseMessage response = await HttpUtil.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpSourceException((int)response.StatusCode, url);

                    using (stall.Token.Register(delegate { try { response.Dispose(); } catch { } }))
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                    {
                        byte[] buffer = new byte[131072];
                        while (true)
                        {
                            int read = await input.ReadAsync(buffer, 0, buffer.Length, stall.Token).ConfigureAwait(false);
                            if (read <= 0)
                                break;
                            stall.CancelAfter(TimeSpan.FromSeconds(30));
                            await output.WriteAsync(buffer, 0, read, stall.Token).ConfigureAwait(false);
                            totalRead += read;
                            _stats.AddReceived(read);
                            _stats.ReportProgress(item.Index, totalRead, item.Weight);

                            if (allowSlowAbort && speedWindow.Elapsed.TotalSeconds >= 5.0)
                            {
                                if (totalRead - speedWindowBytes < 160 * 1024)
                                {
                                    slowWindows++;
                                    if (slowWindows >= 3)
                                        throw new TimeoutException("镜像速度持续过慢：" + url);
                                }
                                else
                                {
                                    slowWindows = 0;
                                }
                                speedWindowBytes = totalRead;
                                speedWindow.Reset();
                                speedWindow.Start();
                            }
                        }
                    }
                }

                VerifyAndCommit(part, item, totalRead);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!timeout.IsCancellationRequested)
                    throw new TimeoutException("连接长时间无数据：" + url);
                throw new TimeoutException("下载超时：" + url);
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stall.IsCancellationRequested)
                {
                    if (!timeout.IsCancellationRequested)
                        throw new TimeoutException("连接长时间无数据：" + url);
                    throw new TimeoutException("下载超时：" + url);
                }
                throw;
            }
            finally
            {
                if (connectionHeld)
                    _connections.Release();
                stall.Dispose();
                timeout.Dispose();
                try
                {
                    if (File.Exists(part))
                        File.Delete(part);
                }
                catch
                {
                }
            }
        }

        private async Task<bool> TryDownloadSegmentedAsync(DownloadItem item, string url, bool allowSlowAbort,
            CancellationToken cancellationToken)
        {
            if (item == null || item.Size < SegmentThreshold)
                return false;

            int segmentCount = item.Size >= 96L * 1024 * 1024 ? 8 :
                               item.Size >= 32L * 1024 * 1024 ? 6 :
                               item.Size >= 12L * 1024 * 1024 ? 4 : 2;
            string part = item.DestPath + ".part";
            string[] segmentPaths = new string[segmentCount];
            long[] starts = new long[segmentCount];
            long[] lengths = new long[segmentCount];
            long[] received = new long[segmentCount];
            long baseLength = item.Size / segmentCount;
            long remainder = item.Size % segmentCount;
            long position = 0;
            for (int i = 0; i < segmentCount; i++)
            {
                long length = baseLength + (i < remainder ? 1 : 0);
                starts[i] = position;
                lengths[i] = length;
                position += length;
                segmentPaths[i] = part + "." + i.ToString();
                try
                {
                    if (File.Exists(segmentPaths[i]))
                        File.Delete(segmentPaths[i]);
                }
                catch
                {
                }
            }

            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            object errorLock = new object();
            Exception firstError = null;
            int unsupported = 0;
            Task[] tasks = new Task[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                int index = i;
                tasks[i] = Task.Run(async delegate
                {
                    try
                    {
                        await DownloadSegmentWithRetryAsync(item, url, starts[index], lengths[index],
                            segmentPaths[index], received, index, linked.Token).ConfigureAwait(false);
                    }
                    catch (RangeNotSupportedException)
                    {
                        Interlocked.Exchange(ref unsupported, 1);
                        try { linked.Cancel(); } catch { }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        lock (errorLock)
                        {
                            if (firstError == null)
                                firstError = ex;
                        }
                        try { linked.Cancel(); } catch { }
                    }
                });
            }

            Task all = Task.WhenAll(tasks);
            long speedWindowBytes = 0;
            int slowWindows = 0;
            Stopwatch speedWindow = Stopwatch.StartNew();
            bool committed = false;
            try
            {
                while (!all.IsCompleted)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { linked.Cancel(); } catch { }
                        break;
                    }

                    Task finished = await Task.WhenAny(all, Task.Delay(500)).ConfigureAwait(false);
                    if (finished == all)
                        break;
                    if (!allowSlowAbort || speedWindow.Elapsed.TotalSeconds < 5.0)
                        continue;

                    long total = 0;
                    for (int i = 0; i < received.Length; i++)
                        total += Interlocked.Read(ref received[i]);
                    if (total - speedWindowBytes < 160 * 1024)
                    {
                        slowWindows++;
                        if (slowWindows >= 3)
                        {
                            lock (errorLock)
                            {
                                if (firstError == null)
                                    firstError = new TimeoutException("镜像速度持续过慢：" + url);
                            }
                            try { linked.Cancel(); } catch { }
                        }
                    }
                    else
                    {
                        slowWindows = 0;
                    }
                    speedWindowBytes = total;
                    speedWindow.Reset();
                    speedWindow.Start();
                }

                await all.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref unsupported) != 0)
                    return false;
                if (firstError != null)
                    throw firstError;

                for (int i = 0; i < segmentCount; i++)
                {
                    if (!File.Exists(segmentPaths[i]) || new FileInfo(segmentPaths[i]).Length != lengths[i])
                        throw new InvalidDataException("分段下载文件不完整");
                }

                using (FileStream output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 262144, true))
                {
                    for (int i = 0; i < segmentCount; i++)
                    {
                        using (FileStream input = new FileStream(segmentPaths[i], FileMode.Open, FileAccess.Read,
                            FileShare.None, 262144, true))
                        {
                            await input.CopyToAsync(output, 262144, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                if (new FileInfo(part).Length != item.Size)
                    return false;

                VerifyAndCommit(part, item, item.Size);
                committed = true;
                _stats.ReportProgress(item.Index, item.Size, item.Weight);
                int logs = Interlocked.Increment(ref _segmentLogs);
                if (logs <= 8)
                    Log("多线程下载：" + item.Name + "（" + segmentCount + " 段）");
                return true;
            }
            finally
            {
                try { linked.Cancel(); } catch { }
                linked.Dispose();
                for (int i = 0; i < segmentPaths.Length; i++)
                {
                    try
                    {
                        if (File.Exists(segmentPaths[i]))
                            File.Delete(segmentPaths[i]);
                    }
                    catch
                    {
                    }
                }
                if (!committed)
                {
                    try
                    {
                        if (File.Exists(part))
                            File.Delete(part);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private async Task DownloadSegmentWithRetryAsync(DownloadItem item, string url, long start, long length,
            string partPath, long[] received, int index, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await DownloadSegmentAttemptAsync(item, url, start, length, partPath, received, index,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (RangeNotSupportedException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastError = new TimeoutException("分段下载超时：" + url);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                if (attempt == 0)
                    await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }
            throw lastError ?? new InstallException("分段下载失败");
        }

        private async Task DownloadSegmentAttemptAsync(DownloadItem item, string url, long start, long length,
            string partPath, long[] received, int index, CancellationToken cancellationToken)
        {
            long already = Interlocked.Read(ref received[index]);
            if (already < 0 || already > length)
                throw new InvalidDataException("分段下载进度异常");
            if (already == length && File.Exists(partPath))
                return;

            long from = start + already;
            long to = start + length - 1;
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            stall.CancelAfter(TimeSpan.FromSeconds(30));
            bool connectionHeld = false;
            try
            {
                await _connections.WaitAsync(stall.Token).ConfigureAwait(false);
                connectionHeld = true;
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(from, to);
                    using (HttpResponseMessage response = await HttpUtil.Client.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode != HttpStatusCode.PartialContent)
                            throw new RangeNotSupportedException();
                        if (response.Content.Headers.ContentRange != null &&
                            response.Content.Headers.ContentRange.Length.HasValue &&
                            response.Content.Headers.ContentRange.Length.Value != item.Size)
                            throw new RangeNotSupportedException();

                        using (stall.Token.Register(delegate { try { response.Dispose(); } catch { } }))
                        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream output = new FileStream(partPath,
                            already > 0 ? FileMode.Append : FileMode.Create,
                            FileAccess.Write, FileShare.None, 131072, true))
                        {
                            byte[] buffer = new byte[131072];
                            while (already < length)
                            {
                                int read = await input.ReadAsync(buffer, 0, buffer.Length, stall.Token).ConfigureAwait(false);
                                if (read <= 0)
                                    break;
                                if (already + read > length)
                                    read = (int)(length - already);
                                stall.CancelAfter(TimeSpan.FromSeconds(30));
                                await output.WriteAsync(buffer, 0, read, stall.Token).ConfigureAwait(false);
                                already += read;
                                Interlocked.Exchange(ref received[index], already);
                                _stats.AddReceived(read);
                                long total = 0;
                                for (int i = 0; i < received.Length; i++)
                                    total += Interlocked.Read(ref received[i]);
                                _stats.ReportProgress(item.Index, total, item.Weight);
                            }
                        }
                    }
                }

                if (already != length)
                    throw new InvalidDataException("分段下载未完成");
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!timeout.IsCancellationRequested)
                    throw new TimeoutException("连接长时间无数据：" + url);
                throw new TimeoutException("下载超时：" + url);
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stall.IsCancellationRequested)
                {
                    if (!timeout.IsCancellationRequested)
                        throw new TimeoutException("连接长时间无数据：" + url);
                    throw new TimeoutException("下载超时：" + url);
                }
                throw;
            }
            finally
            {
                if (connectionHeld)
                    _connections.Release();
                stall.Dispose();
                timeout.Dispose();
            }
        }

        private sealed class RangeNotSupportedException : Exception
        {
        }

        private static void VerifyAndCommit(string part, DownloadItem item, long actualSize)
        {
            if (item.Size > 0 && actualSize != item.Size)
                throw new InvalidDataException("文件大小不正确（期望 " + item.Size + "，实际 " + actualSize + "）");

            if (!string.IsNullOrEmpty(item.Sha1))
            {
                string actual;
                using (FileStream verifyStream = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                using (SHA1 sha1 = SHA1.Create())
                {
                    actual = ToHex(sha1.ComputeHash(verifyStream));
                }
                if (!string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA1 校验失败");
            }

            if (File.Exists(item.DestPath))
                File.Delete(item.DestPath);
            File.Move(part, item.DestPath);
        }

        private static List<DownloadItem> Deduplicate(IList<DownloadItem> items)
        {
            Dictionary<string, DownloadItem> seen = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
            List<DownloadItem> result = new List<DownloadItem>();
            for (int i = 0; i < items.Count; i++)
            {
                DownloadItem item = items[i];
                if (item == null || string.IsNullOrEmpty(item.DestPath))
                    continue;
                if (seen.ContainsKey(item.DestPath))
                    continue;
                seen[item.DestPath] = item;
                result.Add(item);
            }
            return result;
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null) return "";
            char[] chars = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(chars);
        }

        private static string ShortError(string text)
        {
            if (string.IsNullOrEmpty(text)) return "未知错误";
            if (text.Length <= 90) return text;
            return text.Substring(0, 90) + "…";
        }

        private static void CheckCancellation(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void Log(string message)
        {
            HttpUtil.SafeLog(_log, message);
        }
    }
}
