using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MCDT
{
    public static class JavaLocator
    {
        private static readonly Regex VersionRegex = new Regex("version\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        private static readonly Regex VersionRegex2 = new Regex("\\b([0-9]+(?:\\.[0-9]+){1,4})\\b", RegexOptions.Compiled);

        public static JavaInfo FindBest(string gameDirectory, int preferredMajor, Action<string> log)
        {
            List<string> candidates = CollectCandidates(gameDirectory);
            List<JavaInfo> javas = new List<JavaInfo>();
            for (int i = 0; i < candidates.Count; i++)
            {
                JavaInfo info = Probe(candidates[i]);
                if (info != null)
                    javas.Add(info);
            }

            if (javas.Count == 0)
            {
                HttpUtil.SafeLog(log, "没有检测到可用的 Java。");
                return null;
            }

            javas.Sort(delegate(JavaInfo a, JavaInfo b)
            {
                int compared = a.Major.CompareTo(b.Major);
                if (compared != 0) return compared;
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });

            JavaInfo best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < javas.Count; i++)
            {
                JavaInfo java = javas[i];
                int distance = Math.Abs(java.Major - preferredMajor);
                if (best == null || distance < bestDistance ||
                    (distance == bestDistance && java.Major > best.Major))
                {
                    best = java;
                    bestDistance = distance;
                }
            }

            HttpUtil.SafeLog(log, "已检测到 " + javas.Count + " 个 Java，选用 " + best.ToString() + "。");
            return best;
        }

        private static List<string> CollectCandidates(string gameDirectory)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddEnvironmentPaths(paths);
            AddRegistryJavas(paths);
            AddCommonFolders(paths);
            AddRuntimeFolder(paths, gameDirectory);
            AddRuntimeFolder(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".hmcl", "java"));
            AddRuntimeFolder(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher", "java"));
            AddRuntimeFolder(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"));
            AddRuntimeFolder(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "runtime"));

            List<string> result = new List<string>();
            foreach (string path in paths)
            {
                if (File.Exists(path))
                    result.Add(path);
            }
            return result;
        }

        private static void AddEnvironmentPaths(HashSet<string> paths)
        {
            AddJavaHome(paths, Environment.GetEnvironmentVariable("JAVA_HOME"));
            string pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVariable))
            {
                string[] parts = pathVariable.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    string dir = parts[i].Trim().Trim('"');
                    if (dir.Length > 0)
                        AddJavaInDirectory(paths, dir);
                }
            }
        }

        private static void AddJavaHome(HashSet<string> paths, string home)
        {
            if (string.IsNullOrEmpty(home))
                return;
            AddJavaInDirectory(paths, home);
            AddJavaInDirectory(paths, Path.Combine(home, "bin"));
        }

        private static void AddJavaInDirectory(HashSet<string> paths, string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return;
            paths.Add(Path.Combine(directory, "java.exe"));
            paths.Add(Path.Combine(directory, "bin", "java.exe"));
        }

        private static void AddRegistryJavas(HashSet<string> paths)
        {
            string[] roots = new string[]
            {
                @"SOFTWARE\JavaSoft\JDK",
                @"SOFTWARE\JavaSoft\Java Runtime Environment",
                @"SOFTWARE\JavaSoft\JRE",
                @"SOFTWARE\Eclipse Adoptium\JDK",
                @"SOFTWARE\Eclipse Adoptium\JRE",
                @"SOFTWARE\Microsoft\JDK",
                @"SOFTWARE\Amazon Corretto",
                @"SOFTWARE\Zulu",
                @"SOFTWARE\WOW6432Node\JavaSoft\JDK",
                @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment",
                @"SOFTWARE\WOW6432Node\Eclipse Adoptium\JDK"
            };

            for (int r = 0; r < roots.Length; r++)
            {
                ReadRegistryRoot(Registry.LocalMachine, roots[r], paths);
                ReadRegistryRoot(Registry.CurrentUser, roots[r], paths);
            }
        }

        private static void ReadRegistryRoot(RegistryKey root, string subPath, HashSet<string> paths)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subPath))
                {
                    if (key == null) return;
                    string[] names = key.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        using (RegistryKey child = key.OpenSubKey(names[i]))
                        {
                            if (child == null) continue;
                            string home = child.GetValue("JavaHome") as string;
                            if (string.IsNullOrEmpty(home))
                                home = child.GetValue("Path") as string;
                            if (string.IsNullOrEmpty(home))
                                home = child.GetValue("InstallDir") as string;
                            AddJavaHome(paths, home);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddCommonFolders(HashSet<string> paths)
        {
            List<string> roots = new List<string>();
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BellSoft"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Semeru"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Eclipse Adoptium"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"));

            for (int i = 0; i < roots.Count; i++)
            {
                try
                {
                    if (!Directory.Exists(roots[i])) continue;
                    string[] children = Directory.GetDirectories(roots[i]);
                    for (int j = 0; j < children.Length; j++)
                    {
                        AddJavaHome(paths, children[j]);
                        AddJavaInDirectory(paths, Path.Combine(children[j], "jre"));
                    }
                }
                catch
                {
                }
            }
        }

        private static void AddRuntimeFolder(HashSet<string> paths, string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;
            try
            {
                WalkForJava(root, 0, 7, paths);
            }
            catch
            {
            }
        }

        private static void WalkForJava(string directory, int depth, int maxDepth, HashSet<string> paths)
        {
            if (depth > maxDepth || paths.Count > 300)
                return;
            try
            {
                string java = Path.Combine(directory, "bin", "java.exe");
                if (File.Exists(java))
                {
                    paths.Add(java);
                    return;
                }
                string[] children = Directory.GetDirectories(directory);
                for (int i = 0; i < children.Length; i++)
                {
                    string name = Path.GetFileName(children[i]);
                    if (name.Equals("jre", StringComparison.OrdinalIgnoreCase) && depth > 0)
                        continue;
                    WalkForJava(children[i], depth + 1, maxDepth, paths);
                }
            }
            catch
            {
            }
        }

        private static JavaInfo Probe(string path)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = path;
                info.Arguments = "-version";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    System.Threading.Tasks.Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }
                    string output = outputTask.Result + "\n" + errorTask.Result;
                    int major = ParseMajor(output);
                    if (major < 1)
                        return null;
                    string firstLine = output.Trim().Split('\n')[0].Trim();
                    return new JavaInfo { Path = path, Major = major, VersionText = firstLine };
                }
            }
            catch
            {
                return null;
            }
        }

        private static int ParseMajor(string output)
        {
            Match match = VersionRegex.Match(output ?? "");
            string version = match.Success ? match.Groups[1].Value : null;
            if (string.IsNullOrEmpty(version))
            {
                match = VersionRegex2.Match(output ?? "");
                if (!match.Success) return 0;
                version = match.Groups[1].Value;
            }

            string[] parts = version.Split('.');
            int major;
            if (parts.Length > 0 && parts[0] == "1" && parts.Length > 1)
                int.TryParse(parts[1], out major);
            else
                int.TryParse(parts[0], out major);
            return major;
        }
    }
}
