using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace MCDT
{
    public enum TargetIconKind
    {
        Vanilla,
        Fabric,
        Forge,
        NeoForge,
        Quilt,
        Generic,
        Save,
        Folder
    }

    public static class TargetIcons
    {
        private static readonly Dictionary<TargetIconKind, Bitmap> Cache = new Dictionary<TargetIconKind, Bitmap>();
        private static readonly Dictionary<string, CustomIcon> CustomCache =
            new Dictionary<string, CustomIcon>(StringComparer.OrdinalIgnoreCase);

        private sealed class CustomIcon
        {
            public Bitmap Bitmap;
            public DateTime Stamp;
        }

        public static Bitmap Get(TargetIconKind kind)
        {
            lock (Cache)
            {
                Bitmap bitmap;
                if (Cache.TryGetValue(kind, out bitmap))
                    return bitmap;
                bitmap = Create(kind);
                Cache[kind] = bitmap;
                return bitmap;
            }
        }

        public static void Draw(Graphics graphics, Rectangle bounds, TargetIconKind kind)
        {
            Draw(graphics, bounds, kind, null);
        }

        public static void Draw(Graphics graphics, Rectangle bounds, TargetIconKind kind, string customPath)
        {
            Bitmap bitmap = GetCustom(customPath) ?? Get(kind);
            InterpolationMode oldInterpolation = graphics.InterpolationMode;
            PixelOffsetMode oldOffset = graphics.PixelOffsetMode;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(bitmap, bounds);
            graphics.InterpolationMode = oldInterpolation;
            graphics.PixelOffsetMode = oldOffset;
        }

        private static Bitmap GetCustom(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0)
                    return null;
                DateTime stamp = info.LastWriteTimeUtc;
                lock (CustomCache)
                {
                    CustomIcon cached;
                    if (CustomCache.TryGetValue(path, out cached) && cached.Stamp == stamp)
                        return cached.Bitmap;
                }

                Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image source = Image.FromStream(stream))
                    {
                        float scale = Math.Min(32f / source.Width, 32f / source.Height);
                        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                        graphics.DrawImage(source, new Rectangle((32 - width) / 2, (32 - height) / 2, width, height));
                    }
                }

                lock (CustomCache)
                {
                    CustomIcon previous;
                    if (CustomCache.TryGetValue(path, out previous))
                    {
                        if (previous.Bitmap != null)
                            previous.Bitmap.Dispose();
                    }
                    CustomCache[path] = new CustomIcon { Bitmap = bitmap, Stamp = stamp };
                    if (CustomCache.Count > 200)
                    {
                        List<string> remove = new List<string>();
                        foreach (KeyValuePair<string, CustomIcon> pair in CustomCache)
                        {
                            if (remove.Count >= 50)
                                break;
                            if (!string.Equals(pair.Key, path, StringComparison.OrdinalIgnoreCase))
                                remove.Add(pair.Key);
                        }
                        for (int i = 0; i < remove.Count; i++)
                        {
                            CustomIcon old;
                            if (CustomCache.TryGetValue(remove[i], out old) && old.Bitmap != null)
                                old.Bitmap.Dispose();
                            CustomCache.Remove(remove[i]);
                        }
                    }
                }
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap Create(TargetIconKind kind)
        {
            Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                switch (kind)
                {
                    case TargetIconKind.Vanilla:
                        DrawVanilla(graphics);
                        break;
                    case TargetIconKind.Fabric:
                        DrawFabric(graphics);
                        break;
                    case TargetIconKind.Forge:
                        DrawForge(graphics);
                        break;
                    case TargetIconKind.NeoForge:
                        DrawNeoForge(graphics);
                        break;
                    case TargetIconKind.Quilt:
                        DrawQuilt(graphics);
                        break;
                    case TargetIconKind.Save:
                        DrawSave(graphics);
                        break;
                    case TargetIconKind.Folder:
                        DrawFolder(graphics);
                        break;
                    default:
                        DrawGeneric(graphics);
                        break;
                }
            }
            return bitmap;
        }

        private static void DrawVanilla(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush top = new SolidBrush(Color.FromArgb(126, 187, 74)))
            using (SolidBrush left = new SolidBrush(Color.FromArgb(139, 110, 99)))
            using (SolidBrush right = new SolidBrush(Color.FromArgb(105, 76, 65)))
            using (SolidBrush fringeLeft = new SolidBrush(Color.FromArgb(104, 159, 56)))
            using (SolidBrush fringeRight = new SolidBrush(Color.FromArgb(85, 139, 47)))
            using (SolidBrush speck = new SolidBrush(Color.FromArgb(161, 136, 127)))
            using (SolidBrush speckDark = new SolidBrush(Color.FromArgb(121, 85, 72)))
            {
                g.FillPolygon(top, new Point[]
                {
                    new Point(16, 3), new Point(29, 10), new Point(16, 17), new Point(3, 10)
                });
                g.FillPolygon(left, new Point[]
                {
                    new Point(3, 10), new Point(16, 17), new Point(16, 30), new Point(3, 23)
                });
                g.FillPolygon(right, new Point[]
                {
                    new Point(29, 10), new Point(16, 17), new Point(16, 30), new Point(29, 23)
                });
                g.FillPolygon(fringeLeft, new Point[]
                {
                    new Point(3, 10), new Point(16, 17), new Point(16, 21), new Point(3, 14)
                });
                g.FillPolygon(fringeRight, new Point[]
                {
                    new Point(29, 10), new Point(16, 17), new Point(16, 21), new Point(29, 14)
                });
                g.FillRectangle(speck, 6, 17, 2, 2);
                g.FillRectangle(speck, 10, 23, 2, 2);
                g.FillRectangle(speck, 7, 26, 3, 2);
                g.FillRectangle(speckDark, 20, 18, 2, 2);
                g.FillRectangle(speckDark, 23, 23, 3, 2);
                g.FillRectangle(speckDark, 18, 26, 2, 2);
            }
            using (Pen outline = new Pen(Color.FromArgb(73, 54, 45), 1f))
            {
                g.DrawPolygon(outline, new Point[]
                {
                    new Point(16, 3), new Point(29, 10), new Point(29, 23),
                    new Point(16, 30), new Point(3, 23), new Point(3, 10)
                });
                g.DrawLine(outline, 3, 10, 16, 17);
                g.DrawLine(outline, 29, 10, 16, 17);
                g.DrawLine(outline, 16, 17, 16, 30);
            }
        }

        private static void DrawFabric(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath body = RoundRect(new Rectangle(9, 6, 14, 21), 4))
            using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(226, 211, 183)))
            using (Pen outline = new Pen(Color.FromArgb(143, 119, 86), 1.2f))
            {
                g.FillPath(bodyBrush, body);
                GraphicsState state = g.Save();
                g.SetClip(body);
                using (Pen thread = new Pen(Color.FromArgb(248, 242, 229), 2.4f))
                using (Pen shadowThread = new Pen(Color.FromArgb(198, 178, 143), 1f))
                {
                    for (int i = -12; i <= 24; i += 6)
                        g.DrawLine(thread, 8 + i, 29, 23 + i, 5);
                    for (int i = -9; i <= 24; i += 6)
                        g.DrawLine(shadowThread, 8 + i, 29, 23 + i, 5);
                }
                g.Restore(state);
                g.DrawPath(outline, body);
            }
            using (SolidBrush topBrush = new SolidBrush(Color.FromArgb(245, 237, 220)))
            using (SolidBrush bottomBrush = new SolidBrush(Color.FromArgb(214, 199, 170)))
            using (Pen capOutline = new Pen(Color.FromArgb(143, 119, 86), 1.2f))
            {
                g.FillEllipse(topBrush, 8, 3, 16, 6);
                g.DrawEllipse(capOutline, 8, 3, 16, 6);
                g.FillEllipse(bottomBrush, 8, 24, 16, 6);
                g.DrawEllipse(capOutline, 8, 24, 16, 6);
            }
            using (Pen threadTail = new Pen(Color.FromArgb(250, 246, 236), 1.6f))
            {
                threadTail.StartCap = LineCap.Round;
                threadTail.EndCap = LineCap.Round;
                g.DrawBezier(threadTail, 22, 14, 28, 12, 29, 20, 25, 25);
            }
        }

        private static void DrawForge(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            Color steel = Color.FromArgb(86, 92, 100);
            Color dark = Color.FromArgb(58, 63, 69);
            Color light = Color.FromArgb(123, 130, 139);
            Color outline = Color.FromArgb(38, 42, 46);
            using (SolidBrush steelBrush = new SolidBrush(steel))
            using (SolidBrush darkBrush = new SolidBrush(dark))
            using (SolidBrush lightBrush = new SolidBrush(light))
            using (Pen pen = new Pen(outline, 1f))
            {
                g.FillPolygon(steelBrush, new Point[]
                {
                    new Point(6, 11), new Point(2, 12), new Point(2, 15), new Point(6, 17)
                });
                g.FillRectangle(lightBrush, 5, 10, 22, 2);
                g.FillRectangle(steelBrush, 5, 12, 22, 5);
                g.FillRectangle(darkBrush, 5, 16, 22, 2);
                g.FillRectangle(steelBrush, 12, 18, 8, 6);
                g.FillRectangle(steelBrush, 8, 24, 16, 2);
                g.FillRectangle(darkBrush, 5, 26, 22, 3);
                g.DrawRectangle(pen, 5, 10, 22, 8);
                g.DrawRectangle(pen, 12, 18, 8, 6);
                g.DrawRectangle(pen, 8, 24, 16, 2);
                g.DrawRectangle(pen, 5, 26, 22, 3);
                g.FillRectangle(lightBrush, 8, 12, 7, 1);
            }
        }

        private static void DrawNeoForge(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush ear = new SolidBrush(Color.FromArgb(201, 84, 31)))
            using (SolidBrush innerEar = new SolidBrush(Color.FromArgb(242, 166, 90)))
            using (SolidBrush head = new SolidBrush(Color.FromArgb(232, 116, 43)))
            using (SolidBrush muzzle = new SolidBrush(Color.FromArgb(255, 217, 176)))
            using (SolidBrush dark = new SolidBrush(Color.FromArgb(58, 36, 24)))
            using (Pen outline = new Pen(Color.FromArgb(179, 78, 31), 1.2f))
            {
                g.FillPolygon(ear, new Point[]
                {
                    new Point(6, 13), new Point(9, 3), new Point(15, 11)
                });
                g.FillPolygon(ear, new Point[]
                {
                    new Point(17, 11), new Point(23, 3), new Point(26, 13)
                });
                g.FillPolygon(innerEar, new Point[]
                {
                    new Point(8, 12), new Point(10, 6), new Point(13, 11)
                });
                g.FillPolygon(innerEar, new Point[]
                {
                    new Point(19, 11), new Point(22, 6), new Point(24, 12)
                });
                using (GraphicsPath headPath = RoundRect(new Rectangle(5, 9, 22, 19), 6))
                {
                    g.FillPath(head, headPath);
                    g.DrawPath(outline, headPath);
                }
                g.FillEllipse(muzzle, 9, 16, 14, 10);
                g.FillEllipse(muzzle, 9, 16, 6, 8);
                g.FillEllipse(muzzle, 17, 16, 6, 8);
                g.FillEllipse(dark, 9, 13, 4, 4);
                g.FillEllipse(dark, 19, 13, 4, 4);
                g.FillPolygon(dark, new Point[]
                {
                    new Point(14, 20), new Point(18, 20), new Point(16, 23)
                });
            }
        }

        private static void DrawQuilt(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath body = RoundRect(new Rectangle(4, 5, 24, 22), 5))
            using (SolidBrush purple = new SolidBrush(Color.FromArgb(142, 124, 195)))
            using (SolidBrush teal = new SolidBrush(Color.FromArgb(77, 182, 172)))
            using (SolidBrush gold = new SolidBrush(Color.FromArgb(242, 201, 76)))
            using (SolidBrush pink = new SolidBrush(Color.FromArgb(229, 143, 168)))
            using (Pen stitch = new Pen(Color.FromArgb(255, 255, 255), 1.4f))
            using (Pen outline = new Pen(Color.FromArgb(109, 90, 140), 1.2f))
            {
                GraphicsState state = g.Save();
                g.SetClip(body);
                g.FillRectangle(purple, 4, 5, 12, 11);
                g.FillRectangle(teal, 16, 5, 12, 11);
                g.FillRectangle(gold, 4, 16, 12, 11);
                g.FillRectangle(pink, 16, 16, 12, 11);
                stitch.DashStyle = DashStyle.Dash;
                g.DrawLine(stitch, 16, 5, 16, 27);
                g.DrawLine(stitch, 4, 16, 28, 16);
                g.Restore(state);
                g.DrawPath(outline, body);
            }
        }

        private static void DrawGeneric(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (SolidBrush top = new SolidBrush(Color.FromArgb(144, 164, 174)))
            using (SolidBrush left = new SolidBrush(Color.FromArgb(120, 144, 156)))
            using (SolidBrush right = new SolidBrush(Color.FromArgb(96, 125, 139)))
            using (Pen outline = new Pen(Color.FromArgb(69, 90, 100), 1f))
            {
                g.FillPolygon(top, new Point[]
                {
                    new Point(16, 4), new Point(28, 10), new Point(16, 16), new Point(4, 10)
                });
                g.FillPolygon(left, new Point[]
                {
                    new Point(4, 10), new Point(16, 16), new Point(16, 28), new Point(4, 22)
                });
                g.FillPolygon(right, new Point[]
                {
                    new Point(28, 10), new Point(16, 16), new Point(16, 28), new Point(28, 22)
                });
                g.DrawPolygon(outline, new Point[]
                {
                    new Point(16, 4), new Point(28, 10), new Point(28, 22),
                    new Point(16, 28), new Point(4, 22), new Point(4, 10)
                });
                g.DrawLine(outline, 16, 16, 16, 28);
                g.DrawLine(outline, 4, 10, 16, 16);
                g.DrawLine(outline, 28, 10, 16, 16);
            }
        }

        private static void DrawSave(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath body = RoundRect(new Rectangle(3, 4, 26, 24), 5))
            using (SolidBrush sky = new SolidBrush(Color.FromArgb(191, 227, 242)))
            using (SolidBrush sun = new SolidBrush(Color.FromArgb(255, 217, 102)))
            using (SolidBrush cloud = new SolidBrush(Color.FromArgb(245, 250, 252)))
            using (SolidBrush farHill = new SolidBrush(Color.FromArgb(104, 159, 56)))
            using (SolidBrush nearHill = new SolidBrush(Color.FromArgb(124, 179, 66)))
            using (Pen outline = new Pen(Color.FromArgb(84, 125, 71), 1.2f))
            {
                GraphicsState state = g.Save();
                g.SetClip(body);
                g.FillRectangle(sky, 3, 4, 26, 24);
                g.FillEllipse(sun, 19, 6, 7, 7);
                g.FillEllipse(cloud, 6, 9, 8, 4);
                g.FillEllipse(cloud, 10, 7, 6, 4);
                g.FillEllipse(farHill, -4, 17, 22, 16);
                g.FillEllipse(nearHill, 8, 14, 24, 18);
                g.FillRectangle(nearHill, 3, 24, 26, 4);
                g.Restore(state);
                g.DrawPath(outline, body);
            }
        }

        private static void DrawFolder(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(Color.FromArgb(217, 174, 62)))
            using (SolidBrush front = new SolidBrush(Color.FromArgb(242, 201, 76)))
            using (Pen outline = new Pen(Color.FromArgb(176, 140, 47), 1.2f))
            {
                g.FillPolygon(back, new Point[]
                {
                    new Point(4, 8), new Point(13, 8), new Point(15, 11),
                    new Point(27, 11), new Point(27, 15), new Point(4, 15)
                });
                using (GraphicsPath body = RoundRect(new Rectangle(4, 12, 24, 14), 3))
                {
                    g.FillPath(front, body);
                    g.DrawPath(outline, body);
                }
                g.DrawPolygon(outline, new Point[]
                {
                    new Point(4, 8), new Point(13, 8), new Point(15, 11),
                    new Point(27, 11), new Point(27, 14)
                });
            }
            using (Pen shine = new Pen(Color.FromArgb(255, 235, 170), 1.2f))
                g.DrawLine(shine, 7, 15, 24, 15);
        }

        private static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
