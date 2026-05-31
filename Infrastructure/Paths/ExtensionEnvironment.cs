using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ExtensionEnvironment
    {
        public static string LocalRoot
        {
            get
            {
                return BuildRoot(
                    Environment.SpecialFolder.LocalApplicationData,
                    Path.Combine(Path.GetTempPath(), "MHNK", "RevitExtension"));
            }
        }

        public static string RoamingRoot
        {
            get
            {
                return BuildRoot(
                    Environment.SpecialFolder.ApplicationData,
                    Path.Combine(LocalRoot, "Roaming"));
            }
        }

        public static string SharedRoot
        {
            get
            {
                return BuildRoot(
                    Environment.SpecialFolder.CommonApplicationData,
                    Path.Combine(LocalRoot, "Shared"));
            }
        }

        public static string GetLogsDirectory()
        {
            return Path.Combine(LocalRoot, "Logs");
        }

        public static string GetFeatureDirectory(string featureId, string area, bool roaming)
        {
            string root = roaming ? RoamingRoot : LocalRoot;
            string normalizedFeatureId = NormalizeSegment(featureId, "General");
            string normalizedArea = NormalizeSegment(area, "Data");
            return Path.Combine(root, normalizedFeatureId, normalizedArea);
        }

        public static string GetFeatureLogDirectory(string featureId)
        {
            return Path.Combine(GetLogsDirectory(), NormalizeSegment(featureId, "General"));
        }

        public static string GetCurrentLogFilePath(string logPrefix)
        {
            string fileName = NormalizeSegment(logPrefix, "MHNK") + "-" + DateTime.Now.ToString("yyyyMMdd") + ".log";
            return Path.Combine(GetLogsDirectory(), fileName);
        }

        public static string EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            Directory.CreateDirectory(path);
            return path;
        }

        private static string BuildRoot(Environment.SpecialFolder folder, string fallbackRoot)
        {
            string value = string.Empty;

            try
            {
                value = Environment.GetFolderPath(folder);
            }
            catch
            {
                value = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return fallbackRoot;
            }

            return Path.Combine(value, "MHNK", "RevitExtension");
        }

        private static string NormalizeSegment(string value, string fallback)
        {
            string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(candidate.Length);

            for (int i = 0; i < candidate.Length; i++)
            {
                char current = candidate[i];
                bool isInvalid = false;
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (current == invalidChars[j])
                    {
                        isInvalid = true;
                        break;
                    }
                }

                if (isInvalid || char.IsWhiteSpace(current))
                {
                    builder.Append('_');
                    continue;
                }

                builder.Append(current);
            }

            string normalized = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }
    }
}
