using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MCDT
{
    public static class ImageCache
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static Image _placeholder;

        public static Image Placeholder
        {
            get
            {
                lock (_sync)
                {
                    if (_placeholder == null)
                        _placeholder = CreatePlaceholder();
                    return _placeholder;
                }
            }
        }

        public static async Task<Image> GetAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
                return Placeholder;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url.Substring(7);
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return Placeholder;

            lock (_sync)
            {
                Image cached;
                if (_cache.TryGetValue(url, out cached))
                    return cached;
            }

            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                using (HttpResponseMessage response = await HttpUtil.Client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return Placeholder;
                    using (timeout.Token.Register(delegate { try { response.Dispose(); } catch { } }))
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (MemoryStream memory = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        while (true)
                        {
                            int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                            if (read <= 0)
                                break;
                            memory.Write(buffer, 0, read);
                        }
                        memory.Position = 0;
                        Image image = Decode(memory);
                        if (image == null)
                            return Placeholder;
                        lock (_sync)
                            _cache[url] = image;
                        return image;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Placeholder;
            }
            catch
            {
                return Placeholder;
            }
            finally
            {
                timeout.Dispose();
            }
        }

        private static Image Decode(Stream stream)
        {
            try
            {
                using (Image loaded = Image.FromStream(stream))
                    return new Bitmap(loaded);
            }
            catch
            {
            }

            try
            {
                stream.Position = 0;
                return DecodeWithWic(stream);
            }
            catch
            {
                return null;
            }
        }

        private static Image DecodeWithWic(Stream stream)
        {
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
            using (MemoryStream png = new MemoryStream())
            {
                encoder.Save(png);
                png.Position = 0;
                using (Image loaded = Image.FromStream(png))
                    return new Bitmap(loaded);
            }
        }

        private static Image CreatePlaceholder()
        {
            Bitmap bitmap = new Bitmap(64, 64);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.FromArgb(232, 235, 238));
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(100, 110, 120)))
                using (Font font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString("M", font, brush, new RectangleF(0, 0, 64, 64), format);
                }
            }
            return bitmap;
        }
    }
}
