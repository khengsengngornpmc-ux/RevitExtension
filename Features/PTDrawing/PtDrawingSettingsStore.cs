using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingSettingsStore
    {
        private const string FeatureId = "DRAWING_PT";
        private const string SettingsFileName = "adapt-import.settings";

        public static string LoadValue(string key)
        {
            try
            {
                string settingsPath = GetSettingsPath();
                if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                {
                    return "";
                }

                string prefix = key + "=";
                foreach (string line in File.ReadAllLines(settingsPath, Encoding.UTF8))
                {
                    if (line != null && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Substring(prefix.Length).Trim();
                        if (string.Equals(key, "LastFolder", StringComparison.OrdinalIgnoreCase))
                        {
                            return Directory.Exists(value) ? value : "";
                        }

                        if (string.Equals(key, "LastProject", StringComparison.OrdinalIgnoreCase))
                        {
                            return File.Exists(value) ? value : "";
                        }

                        return value;
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        public static void SaveValues(
            string lastFolder,
            string lastProjectPath,
            AdaptCadImportMode importMode,
            string shopMarkPrefix,
            int shopMarkStartNumber,
            int shopMarkDigits,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool preserveCadShopMarks)
        {
            try
            {
                string settingsPath = GetSettingsPath();
                if (string.IsNullOrWhiteSpace(settingsPath))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    ExtensionEnvironment.EnsureDirectory(directory);
                }

                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
                {
                    lines.Add("LastFolder=" + lastFolder);
                }

                if (!string.IsNullOrWhiteSpace(lastProjectPath) && File.Exists(lastProjectPath))
                {
                    lines.Add("LastProject=" + lastProjectPath);
                }

                lines.Add("CadImportMode=" + importMode);
                lines.Add("ShopMarkPrefix=" + (string.IsNullOrWhiteSpace(shopMarkPrefix) ? "PT" : shopMarkPrefix));
                lines.Add("ShopMarkStartNumber=" + Math.Max(1, shopMarkStartNumber).ToString(CultureInfo.InvariantCulture));
                lines.Add("ShopMarkDigits=" + Math.Max(1, Math.Min(6, shopMarkDigits)).ToString(CultureInfo.InvariantCulture));
                lines.Add("ShopMarkSequenceMode=" + sequenceMode.ToString());
                lines.Add("PreserveCadShopMarks=" + (preserveCadShopMarks ? "true" : "false"));

                File.WriteAllLines(settingsPath, lines, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(ExtensionEnvironment.RoamingRoot, FeatureId, SettingsFileName);
        }
    }
}
