using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MCDT
{
    public sealed class HttpSourceException : Exception
    {
        public int StatusCode { get; private set; }
        public string Url { get; private set; }
        public string Detail { get; private set; }

        public HttpSourceException(int statusCode, string url)
            : this(statusCode, url, null)
        {
        }

        public HttpSourceException(int statusCode, string url, string detail)
            : base("HTTP " + statusCode + "：" + url + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"))
        {
            StatusCode = statusCode;
            Url = url;
            Detail = detail;
        }
    }

    public static class SourceMapper
    {
        private const string BmclRoot = "https://bmclapi2.bangbang93.com";

        public static List<string> MirrorThenOfficial(string officialUrl)
        {
            List<string> result = new List<string>();
            string mirror = ToMirror(officialUrl);
            if (!string.IsNullOrEmpty(mirror) && !string.Equals(mirror, officialUrl, StringComparison.OrdinalIgnoreCase))
                result.Add(mirror);
            if (!string.IsNullOrEmpty(officialUrl))
                result.Add(officialUrl);
            return result;
        }

        public static string ToMirror(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                return url;

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath;
            string query = string.IsNullOrEmpty(uri.Query) ? "" : uri.Query;

            switch (host)
            {
                case "piston-meta.mojang.com":
                case "launchermeta.mojang.com":
                case "launcher.mojang.com":
                case "piston-data.mojang.com":
                    return BmclRoot + path + query;

                case "resources.download.minecraft.net":
                    return BmclRoot + "/assets" + path + query;

                case "libraries.minecraft.net":
                case "maven.minecraftforge.net":
                case "maven.fabricmc.net":
                    return BmclRoot + "/maven" + path + query;

                case "files.minecraftforge.net":
                    if (path.StartsWith("/maven/", StringComparison.OrdinalIgnoreCase))
                        return BmclRoot + "/maven/" + path.Substring("/maven/".Length) + query;
                    return url;

                case "maven.neoforged.net":
                    if (path.StartsWith("/releases/", StringComparison.OrdinalIgnoreCase))
                        return BmclRoot + "/maven/" + path.Substring("/releases/".Length) + query;
                    if (path.StartsWith("/snapshots/", StringComparison.OrdinalIgnoreCase))
                        return BmclRoot + "/maven/" + path.Substring("/snapshots/".Length) + query;
                    return BmclRoot + "/maven" + path + query;

                case "maven.quiltmc.org":
                    if (path.StartsWith("/repository/release/", StringComparison.OrdinalIgnoreCase))
                        return BmclRoot + "/maven/" + path.Substring("/repository/release/".Length) + query;
                    if (path.StartsWith("/repository/snapshots/", StringComparison.OrdinalIgnoreCase))
                        return BmclRoot + "/maven/" + path.Substring("/repository/snapshots/".Length) + query;
                    return BmclRoot + "/maven" + path + query;

                case "repo1.maven.org":
                case "repo.maven.apache.org":
                    if (path.StartsWith("/maven2/", StringComparison.OrdinalIgnoreCase))
                        return BmclRoot + "/maven/" + path.Substring("/maven2/".Length) + query;
                    return url;

                default:
                    return url;
            }
        }
    }

    public static class HttpUtil
    {
        private static readonly HttpClient _client = CreateClient();

        public static HttpClient Client
        {
            get { return _client; }
        }

        private static HttpClient CreateClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 32;
            }
            catch
            {
            }

            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.MaxConnectionsPerServer = 32;
            HttpClient client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MineCraftDownloadTool/1.0");
            return client;
        }

        public static async Task<string> GetStringAsync(IList<string> urls, Action<string> log, CancellationToken cancellationToken)
        {
            return await GetStringAsync(urls, null, log, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<string> GetStringAsync(IList<string> urls, IDictionary<string, string> headers,
            Action<string> log, CancellationToken cancellationToken)
        {
            byte[] bytes = await GetBytesCoreAsync(urls, headers, log, cancellationToken).ConfigureAwait(false);
            int start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                start = 3;
            return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }

        public static Task<byte[]> GetBytesAsync(IList<string> urls, Action<string> log, CancellationToken cancellationToken)
        {
            return GetBytesCoreAsync(urls, null, log, cancellationToken);
        }

        private static async Task<byte[]> GetBytesCoreAsync(IList<string> urls, IDictionary<string, string> headers,
            Action<string> log, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            for (int sourceIndex = 0; sourceIndex < urls.Count; sourceIndex++)
            {
                string url = urls[sourceIndex];
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(35));
                    try
                    {
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            if (headers != null)
                            {
                                foreach (KeyValuePair<string, string> header in headers)
                                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                            using (HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                if (!response.IsSuccessStatusCode)
                                {
                                    string detail = null;
                                    try
                                    {
                                        if (response.Content != null)
                                        {
                                            byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                            if (body != null && body.Length > 0)
                                                detail = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 500));
                                        }
                                    }
                                    catch
                                    {
                                    }
                                    throw new HttpSourceException((int)response.StatusCode, url, detail);
                                }

                                using (timeout.Token.Register(delegate { try { response.Dispose(); } catch { } }))
                                using (System.IO.Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (System.IO.MemoryStream memory = new System.IO.MemoryStream())
                                {
                                    byte[] buffer = new byte[16384];
                                    while (true)
                                    {
                                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                                        if (read <= 0)
                                            break;
                                        memory.Write(buffer, 0, read);
                                    }
                                    return memory.ToArray();
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lastError = new TimeoutException("请求超时：" + url, ex);
                    }
                    catch (HttpSourceException ex)
                    {
                        lastError = ex;
                        if (!IsTransient(ex.StatusCode))
                            break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                    finally
                    {
                        timeout.Dispose();
                    }

                    if (attempt == 0)
                        await Task.Delay(350, cancellationToken).ConfigureAwait(false);
                }

                if (sourceIndex + 1 < urls.Count)
                    SafeLog(log, "镜像源请求失败，正在改用官方源…");
            }

            string source = lastError == null ? "" : "：" + lastError.Message;
            throw new InstallException("无法获取需要的文件，请检查网络连接" + source, lastError);
        }

        public static async Task DownloadToFileAsync(IList<string> urls, string destination, Action<string> log, CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            Exception lastError = null;
            for (int sourceIndex = 0; sourceIndex < urls.Count; sourceIndex++)
            {
                string url = urls[sourceIndex];
                bool allowSlowAbort = sourceIndex + 1 < urls.Count;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string part = destination + ".part";
                    CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(10));
                    CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
                    stall.CancelAfter(TimeSpan.FromSeconds(60));
                    try
                    {
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                        using (HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                                throw new HttpSourceException((int)response.StatusCode, url);

                            using (stall.Token.Register(delegate { try { response.Dispose(); } catch { } }))
                            using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (FileStream output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                            {
                                byte[] buffer = new byte[65536];
                                long totalRead = 0;
                                long speedWindowBytes = 0;
                                int slowWindows = 0;
                                System.Diagnostics.Stopwatch speedWindow = System.Diagnostics.Stopwatch.StartNew();
                                while (true)
                                {
                                    int read = await input.ReadAsync(buffer, 0, buffer.Length, stall.Token).ConfigureAwait(false);
                                    if (read <= 0)
                                        break;
                                    stall.CancelAfter(TimeSpan.FromSeconds(60));
                                    await output.WriteAsync(buffer, 0, read, stall.Token).ConfigureAwait(false);
                                    totalRead += read;

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

                        if (File.Exists(destination))
                            File.Delete(destination);
                        File.Move(part, destination);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lastError = new TimeoutException(!timeout.IsCancellationRequested
                            ? "连接长时间无数据：" + url
                            : "下载超时：" + url);
                    }
                    catch (HttpSourceException ex)
                    {
                        lastError = ex;
                        if (!IsTransient(ex.StatusCode))
                            break;
                    }
                    catch (Exception ex)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (stall.IsCancellationRequested)
                        {
                            lastError = new TimeoutException(!timeout.IsCancellationRequested
                                ? "连接长时间无数据：" + url
                                : "下载超时：" + url);
                        }
                        else
                        {
                            lastError = ex;
                        }
                    }
                    finally
                    {
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

                    if (attempt == 0)
                        await Task.Delay(350, cancellationToken).ConfigureAwait(false);
                }

                if (sourceIndex + 1 < urls.Count)
                    SafeLog(log, "镜像源下载失败，正在改用官方源…");
            }

            string source = lastError == null ? "" : "：" + lastError.Message;
            throw new InstallException("文件下载失败" + source, lastError);
        }

        public static bool IsTransient(int statusCode)
        {
            return statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        public static void SafeLog(Action<string> log, string message)
        {
            if (log == null) return;
            try
            {
                log(message);
            }
            catch
            {
            }
        }
    }
}
