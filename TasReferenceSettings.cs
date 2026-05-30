using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class TasReferenceSettings
    {
        public const string DefaultTasExePath = @"C:\Program Files\Cubicost\Cubicost TAS_x64\bin\TAS.exe";

        public bool IsInstalled { get; private set; }
        public string TasExePath { get; private set; } = DefaultTasExePath;
        public string BinPath { get; private set; } = "";
        public string AiConfigPath { get; private set; } = "";
        public string ProductName { get; private set; } = "Cubicost TAS";
        public string ProductVersion { get; private set; } = "";
        public string FileVersion { get; private set; } = "";
        public string FileDescription { get; private set; } = "";
        public bool AiConfigLoaded { get; private set; }

        public string CadFilterTypes { get; private set; } = "6";
        public bool RepairPolyline { get; private set; } = true;
        public double ApproxPolylineEpsilon { get; private set; } = 0.1;
        public double RdpEpsilonMm { get; private set; } = 5.0;
        public double CadLineParallelAngleEpsilonDeg { get; private set; } = 5.0;
        public double CadLineSearchMaxDistanceMm { get; private set; } = 200.0;
        public double UnitAxisAngleEpsilonDeg { get; private set; } = 10.0;
        public double ParallelApexAngleEpsilonDeg { get; private set; } = 10.0;
        public double ShortLineMinLengthMm { get; private set; } = 200.0;
        public int ImageFixedSizePx { get; private set; } = 512;

        public string DisplayVersion
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ProductVersion) && !string.IsNullOrWhiteSpace(FileVersion))
                {
                    return ProductVersion + " / " + FileVersion;
                }

                if (!string.IsNullOrWhiteSpace(ProductVersion))
                {
                    return ProductVersion;
                }

                return string.IsNullOrWhiteSpace(FileVersion) ? "Not detected" : FileVersion;
            }
        }

        public string DisplayStatus
        {
            get
            {
                if (!IsInstalled)
                {
                    return "Not found";
                }

                return AiConfigLoaded ? "Installed + AIConfig loaded" : "Installed";
            }
        }

        public static TasReferenceSettings Load()
        {
            TasReferenceSettings settings = new TasReferenceSettings();
            settings.LoadFromInstalledTas();
            return settings;
        }

        private void LoadFromInstalledTas()
        {
            string exePath = ResolveTasExePath();
            TasExePath = string.IsNullOrWhiteSpace(exePath) ? DefaultTasExePath : exePath;
            IsInstalled = File.Exists(TasExePath);

            if (!IsInstalled)
            {
                BinPath = Path.GetDirectoryName(DefaultTasExePath) ?? "";
                AiConfigPath = Path.Combine(BinPath, "AIConfig.ini");
                return;
            }

            BinPath = Path.GetDirectoryName(TasExePath) ?? "";
            AiConfigPath = Path.Combine(BinPath, "AIConfig.ini");

            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(TasExePath);
                ProductName = string.IsNullOrWhiteSpace(versionInfo.ProductName) ? ProductName : versionInfo.ProductName;
                ProductVersion = versionInfo.ProductVersion ?? "";
                FileVersion = versionInfo.FileVersion ?? "";
                FileDescription = versionInfo.FileDescription ?? "";
            }
            catch
            {
                ProductVersion = "";
                FileVersion = "";
                FileDescription = "";
            }

            LoadAiConfig();
        }

        private static string ResolveTasExePath()
        {
            if (File.Exists(DefaultTasExePath))
            {
                return DefaultTasExePath;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string candidate = Path.Combine(programFiles ?? "", "Cubicost", "Cubicost TAS_x64", "bin", "TAS.exe");
            return File.Exists(candidate) ? candidate : DefaultTasExePath;
        }

        private void LoadAiConfig()
        {
            if (string.IsNullOrWhiteSpace(AiConfigPath) || !File.Exists(AiConfigPath))
            {
                return;
            }

            Dictionary<string, string> values = ReadIniValues(AiConfigPath);
            if (values.Count == 0)
            {
                return;
            }

            CadFilterTypes = GetString(values, "CAD", "filter_types", CadFilterTypes);
            ImageFixedSizePx = GetInt(values, "Image", "image_fixed_size", ImageFixedSizePx);
            RepairPolyline = GetBool(values, "Poly", "is_repair_poly", RepairPolyline);
            ApproxPolylineEpsilon = GetDouble(values, "Infer", "approx_poly_dp_epsilon", ApproxPolylineEpsilon);
            RdpEpsilonMm = GetDouble(values, "Infer", "rdp_epsilon", RdpEpsilonMm);
            CadLineParallelAngleEpsilonDeg = GetDouble(values, "Fit_CAD_Line", "cad_line_parallel_angle_epsilon", CadLineParallelAngleEpsilonDeg);
            CadLineSearchMaxDistanceMm = GetDouble(values, "Fit_By_CAD_Line", "search_cad_line_max_distance", CadLineSearchMaxDistanceMm);
            UnitAxisAngleEpsilonDeg = GetDouble(values, "Fine_Tuning_Poly_Line", "unitX_unitY_angle_epsilon", UnitAxisAngleEpsilonDeg);
            ParallelApexAngleEpsilonDeg = GetDouble(values, "Fine_Tuning_Poly_Line", "parallel_apeak_angle_epsilon", ParallelApexAngleEpsilonDeg);
            ShortLineMinLengthMm = GetDouble(values, "Fine_Tuning_Poly_Line", "short_line_min_length_epsilon", ShortLineMinLengthMm);
            AiConfigLoaded = true;
        }

        private static Dictionary<string, string> ReadIniValues(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = (rawLine ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                values[BuildKey(section, key)] = value;
            }

            return values;
        }

        private static string BuildKey(string section, string key)
        {
            return (section ?? "") + "." + (key ?? "");
        }

        private static string GetString(Dictionary<string, string> values, string section, string key, string defaultValue)
        {
            string value;
            return values.TryGetValue(BuildKey(section, key), out value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : defaultValue;
        }

        private static double GetDouble(Dictionary<string, string> values, string section, string key, double defaultValue)
        {
            string value;
            double parsed;
            return values.TryGetValue(BuildKey(section, key), out value) &&
                   double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : defaultValue;
        }

        private static int GetInt(Dictionary<string, string> values, string section, string key, int defaultValue)
        {
            string value;
            int parsed;
            return values.TryGetValue(BuildKey(section, key), out value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : defaultValue;
        }

        private static bool GetBool(Dictionary<string, string> values, string section, string key, bool defaultValue)
        {
            string value;
            if (!values.TryGetValue(BuildKey(section, key), out value) || string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return defaultValue;
        }
    }
}
