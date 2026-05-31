using System;
using System.IO;

namespace CamboBIM.Revit2024.Addin
{
    internal static class SharedParameterPathResolver
    {
        private const string SharedParameterEnvVar = "CBIM_SHARED_PARAMETER_FILE";
        private const string SharedParameterFileName = "CBIM-QS.txt";

        public static string ResolveDefaultSharedParameterPath(string baseDir)
        {
            string envPath = System.Environment.GetEnvironmentVariable(SharedParameterEnvVar);
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                return ResolveWritableSharedParameterPath(envPath, baseDir);
            }

            string candidate = TryFindExistingSharedParameterPath(baseDir);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                if (CanUseSharedParameterPath(candidate))
                {
                    return candidate;
                }

                string userPath = GetPerUserSharedParameterPath();
                TryCopySharedParameterFile(candidate, userPath);
                return userPath;
            }

            string basePath = Path.Combine(baseDir ?? "", "database", "Shared Parameter", SharedParameterFileName);
            if (CanUseSharedParameterPath(basePath))
            {
                return basePath;
            }

            return GetPerUserSharedParameterPath();
        }

        public static string ResolveWritableSharedParameterPath(string preferredPath, string baseDir = "")
        {
            if (string.IsNullOrWhiteSpace(preferredPath))
            {
                return ResolveDefaultSharedParameterPath(baseDir);
            }

            string path = preferredPath.Trim();
            if (CanUseSharedParameterPath(path))
            {
                return path;
            }

            string userPath = GetPerUserSharedParameterPath();
            TryCopySharedParameterFile(path, userPath);
            return userPath;
        }

        private static string TryFindExistingSharedParameterPath(string startDir)
        {
            try
            {
                DirectoryInfo dir = string.IsNullOrWhiteSpace(startDir) ? null : new DirectoryInfo(startDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "database", "Shared Parameter", SharedParameterFileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    dir = dir.Parent;
                }
            }
            catch
            {
            }

            return "";
        }

        private static string GetPerUserSharedParameterPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }

            return Path.Combine(root, "MHNK", "RevitExtension", "Shared Parameter", SharedParameterFileName);
        }

        private static bool CanUseSharedParameterPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            if (IsProtectedInstallPath(filePath))
            {
                return false;
            }

            string dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".mhnk-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProtectedInstallPath(string filePath)
        {
            string fullPath = GetFullPathOrEmpty(filePath);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            return IsUnderDirectory(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ||
                   IsUnderDirectory(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)) ||
                   IsUnderDirectory(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }

        private static bool IsUnderDirectory(string fullPath, string root)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            string normalizedRoot = GetFullPathOrEmpty(root);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                return false;
            }

            normalizedRoot = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFullPathOrEmpty(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return "";
            }
        }

        private static void TryCopySharedParameterFile(string sourcePath, string targetPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) ||
                    string.IsNullOrWhiteSpace(targetPath) ||
                    !File.Exists(sourcePath) ||
                    File.Exists(targetPath))
                {
                    return;
                }

                string targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(sourcePath, targetPath, false);
            }
            catch
            {
                // The caller will create a fresh shared parameter file if copying is not possible.
            }
        }
    }
}
