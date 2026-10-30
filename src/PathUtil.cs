using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MCDT
{
    public static class PathUtil
    {
        public static string SafeChild(string baseDirectory, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new InstallException("下载路径为空。");

            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) || normalized.IndexOf(':') >= 0)
                throw new InstallException("非法的文件路径：" + relativePath);

            string baseFull = Path.GetFullPath(baseDirectory);
            if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                baseFull += Path.DirectorySeparatorChar;

            string full = Path.GetFullPath(Path.Combine(baseFull, normalized));
            if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                throw new InstallException("非法的文件路径：" + relativePath);
            return full;
        }

        public static void EnsureParent(string filename)
        {
            string directory = Path.GetDirectoryName(filename);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        public static void WriteAllTextAtomic(string filename, string content)
        {
            EnsureParent(filename);
            string temp = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(filename))
                File.Delete(filename);
            File.Move(temp, filename);
        }

        public static void WriteAllBytesAtomic(string filename, byte[] content)
        {
            EnsureParent(filename);
            string temp = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, content);
            if (File.Exists(filename))
                File.Delete(filename);
            File.Move(temp, filename);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            if (unit == 0)
                return value.ToString("0", CultureInfo.InvariantCulture) + " " + units[unit];
            return value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        public static bool LooksLikeMinecraftDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;
            return string.Equals(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), ".minecraft", StringComparison.OrdinalIgnoreCase);
        }
    }
}
