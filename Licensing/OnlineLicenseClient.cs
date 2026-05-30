using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CamboBIM.Revit2024.Addin;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin.Licensing
{
    internal sealed class OnlineLicenseResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public OnlineLicenseSession Session { get; set; }

        public OnlineLicenseResult()
        {
            Success = false;
            Message = "";
            Session = new OnlineLicenseSession();
        }
    }

    internal sealed class OnlineLicenseClient
    {
        private readonly OnlineLicenseConfig _config;
        private readonly string _fingerprint;

        public OnlineLicenseClient(OnlineLicenseConfig config)
        {
            _config = config;
            _fingerprint = BuildMachineFingerprint();
        }

        public OnlineLicenseResult Activate(string username, string password, string addinVersion)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "action", "activate" },
                { "product_code", _config.ProductCode },
                { "username", username ?? "" },
                { "password", password ?? "" },
                { "machine_fingerprint", _fingerprint },
                { "machine_name", Environment.MachineName ?? "" },
                { "addon_version", addinVersion ?? "" },
                { "revit_year", CamboBimRuntime.RevitYear }
            };

            return Post(_config.ActivateEndpoint, payload);
        }

        public OnlineLicenseResult Heartbeat(string sessionToken)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "action", "heartbeat" },
                { "session_token", sessionToken ?? "" },
                { "machine_fingerprint", _fingerprint },
                { "machine_name", Environment.MachineName ?? "" }
            };

            return Post(_config.HeartbeatEndpoint, payload);
        }

        public OnlineLicenseResult Release(string sessionToken)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "action", "release" },
                { "session_token", sessionToken ?? "" },
                { "machine_fingerprint", _fingerprint },
                { "machine_name", Environment.MachineName ?? "" }
            };

            return Post(_config.ReleaseEndpoint, payload);
        }

        public OnlineLicenseResult ChangePassword(string username, string currentPassword, string newPassword)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "action", "change_password" },
                { "product_code", _config.ProductCode },
                { "username", username ?? "" },
                { "password", currentPassword ?? "" },
                { "new_password", newPassword ?? "" },
                { "machine_fingerprint", _fingerprint },
                { "machine_name", Environment.MachineName ?? "" }
            };

            return Post(_config.ChangePasswordEndpoint, payload);
        }

        public string MachineFingerprint
        {
            get { return _fingerprint; }
        }

        private OnlineLicenseResult Post(string endpoint, Dictionary<string, object> payload)
        {
            var result = new OnlineLicenseResult();

            Uri uri = _config.BuildEndpointUri(endpoint);
            if (uri == null)
            {
                result.Message = "Invalid license server URL or endpoint.";
                return result;
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch
            {
            }

            try
            {
                string body = CamboBimJson.Serialize(payload ?? new Dictionary<string, object>());

                using (var handler = new HttpClientHandler())
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
                    using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = client.PostAsync(uri, content).GetAwaiter().GetResult())
                    {
                        string raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            result.Message = "Empty response from license server.";
                            return result;
                        }

                        Dictionary<string, object> map = CamboBimJson.DeserializeMap(raw);
                        if (map == null)
                        {
                            result.Message = "Invalid response from license server.";
                            return result;
                        }

                        result.Success = ReadBool(map, "success", response.IsSuccessStatusCode);
                        result.Message = ReadString(map, "message", response.IsSuccessStatusCode ? "OK" : "License request failed.");

                        var session = new OnlineLicenseSession();
                        session.SessionToken = ReadString(map, "session_token", "");
                        session.Username = ReadString(map, "username", ReadString(payload, "username", ""));
                        session.ExpiresUtc = OnlineLicenseCache.TryParseUtc(ReadAny(map, "expires_utc"));
                        session.LicenseExpiresUtc = OnlineLicenseCache.TryParseUtc(ReadAny(map, "license_expires_utc"));
                        session.UpdatedUtc = DateTime.UtcNow;

                        if (string.IsNullOrWhiteSpace(session.SessionToken))
                        {
                            session.SessionToken = ReadString(payload, "session_token", "");
                        }

                        result.Session = session;

                        if (!result.Success && response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(result.Message))
                        {
                            result.Message = "License server rejected request.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "License server connection failed: " + ex.Message;
            }

            return result;
        }

        private static string BuildMachineFingerprint()
        {
            string machineGuid = "";

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("MachineGuid");
                        machineGuid = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    }
                }
            }
            catch
            {
            }

            string raw = (Environment.MachineName ?? "") + "|" + (machineGuid ?? "") + "|" + (Environment.UserDomainName ?? "");
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            byte[] hash;

            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(bytes);
            }

            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
        {
            object value = ReadAny(map, key);
            if (value == null)
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
            object value = ReadAny(map, key);
            if (value == null)
            {
                return fallback;
            }

            string str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return string.IsNullOrWhiteSpace(str) ? fallback : str.Trim();
        }

        private static string ReadString(Dictionary<string, object> map, string key)
        {
            return ReadString(map, key, "");
        }

        private static object ReadAny(Dictionary<string, object> map, string key)
        {
            if (map == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            object value;
            if (map.TryGetValue(key, out value))
            {
                return value;
            }

            return null;
        }
    }
}
