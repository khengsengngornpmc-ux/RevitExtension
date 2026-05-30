using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CamboBIM.Revit2024.Addin;

namespace CamboBIM.Revit2024.Addin.Licensing
{
    internal sealed class OnlineLicenseConfig
    {
        public bool Enabled { get; private set; }
        public string ServerUrl { get; private set; }
        public string ProductCode { get; private set; }
        public string ActivateEndpoint { get; private set; }
        public string HeartbeatEndpoint { get; private set; }
        public string ReleaseEndpoint { get; private set; }
        public string ChangePasswordEndpoint { get; private set; }
        public string SupportContactEmail { get; private set; }
        public string SupportContactPhone { get; private set; }
        public string SupportContactTelegram { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public int HeartbeatSeconds { get; private set; }
        public string ConfigPath { get; private set; }

        private OnlineLicenseConfig()
        {
            Enabled = true;
            ServerUrl = "";
            ProductCode = CamboBimRuntime.DefaultProductCode;
            ActivateEndpoint = "?action=activate";
            HeartbeatEndpoint = "?action=heartbeat";
            ReleaseEndpoint = "?action=release";
            ChangePasswordEndpoint = "?action=change_password";
            SupportContactEmail = "";
            SupportContactPhone = "";
            SupportContactTelegram = "";
            TimeoutSeconds = 15;
            HeartbeatSeconds = 300;
            ConfigPath = "";
        }

        public static OnlineLicenseConfig Load(string baseDir)
        {
            var cfg = new OnlineLicenseConfig();
            string configPath = ResolveConfigPath(baseDir);
            cfg.ConfigPath = configPath;

            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return cfg;
            }

            try
            {
                string json = File.ReadAllText(configPath);
                Dictionary<string, object> map = CamboBimJson.DeserializeMap(json);
                if (map == null)
                {
                    return cfg;
                }

                cfg.Enabled = ReadBool(map, "enabled", cfg.Enabled);
                cfg.ServerUrl = ReadString(map, "server_url", "");
                cfg.ProductCode = ReadString(map, "product_code", cfg.ProductCode);
                cfg.ActivateEndpoint = ReadString(map, "activate_endpoint", cfg.ActivateEndpoint);
                cfg.HeartbeatEndpoint = ReadString(map, "heartbeat_endpoint", cfg.HeartbeatEndpoint);
                cfg.ReleaseEndpoint = ReadString(map, "release_endpoint", cfg.ReleaseEndpoint);
                cfg.ChangePasswordEndpoint = ReadString(map, "change_password_endpoint", cfg.ChangePasswordEndpoint);
                cfg.SupportContactEmail = ReadString(map, "support_contact_email", cfg.SupportContactEmail);
                cfg.SupportContactPhone = ReadString(map, "support_contact_phone", cfg.SupportContactPhone);
                cfg.SupportContactTelegram = ReadString(map, "support_contact_telegram", cfg.SupportContactTelegram);
                cfg.TimeoutSeconds = Clamp(ReadInt(map, "timeout_seconds", cfg.TimeoutSeconds), 5, 120);
                cfg.HeartbeatSeconds = Clamp(ReadInt(map, "heartbeat_seconds", cfg.HeartbeatSeconds), 30, 3600);
            }
            catch
            {
            }

            return cfg;
        }

        public bool IsValidForOnlineCheck()
        {
            if (!Enabled)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(ServerUrl);
        }

        public Uri BuildEndpointUri(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                return null;
            }

            Uri baseUri;
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out baseUri))
            {
                return null;
            }

            Uri full;
            if (!Uri.TryCreate(baseUri, endpoint ?? "", out full))
            {
                return null;
            }

            return full;
        }

        private static string ResolveConfigPath(string baseDir)
        {
            var candidates = new List<string>();

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                candidates.Add(Path.Combine(programData, "CamboBIM", "online-license.config.json"));
            }

            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                candidates.Add(Path.Combine(baseDir, "online-license.config.json"));
                candidates.Add(Path.Combine(baseDir, "license", "online-license.config.json"));
            }

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Return first candidate for user guidance even if file doesn't exist.
            return candidates.Count > 0 ? candidates[0] : "";
        }

        private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static string ReadString(Dictionary<string, object> map, string key, string fallback)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            string str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return string.IsNullOrWhiteSpace(str) ? fallback : str.Trim();
        }

        private static int ReadInt(Dictionary<string, object> map, string key, int fallback)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }

            int parsed;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
