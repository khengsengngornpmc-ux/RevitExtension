using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json.Serialization;
#endif
using CamboBIM.Revit2024.Addin;

namespace CamboBIM.Revit2024.Addin.Licensing
{
    internal sealed class OnlineLicenseSession
    {
        public string SessionToken { get; set; }
        public string Username { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime LicenseExpiresUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }

        public OnlineLicenseSession()
        {
            SessionToken = "";
            Username = "";
            ExpiresUtc = DateTime.MinValue;
            LicenseExpiresUtc = DateTime.MinValue;
            UpdatedUtc = DateTime.MinValue;
        }

        public bool IsUsable()
        {
            if (string.IsNullOrWhiteSpace(SessionToken))
            {
                return false;
            }

            if (ExpiresUtc == DateTime.MinValue)
            {
                return true;
            }

            return ExpiresUtc > DateTime.UtcNow.AddMinutes(-1);
        }
    }

    internal sealed class OnlineLicenseLoginPreferences
    {
        public bool RememberLogin { get; set; }
        public string Username { get; set; }
        public string PasswordCipherText { get; set; }
        public DateTime UpdatedUtc { get; set; }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public string Password { get; set; }

        public OnlineLicenseLoginPreferences()
        {
            RememberLogin = false;
            Username = "";
            PasswordCipherText = "";
            UpdatedUtc = DateTime.MinValue;
            Password = "";
        }
    }

    internal static class OnlineLicenseCache
    {
        private const string SessionCacheFileName = "online-license.session.json";
        private const string LoginPrefsFileName = "online-license.login.json";
        private static readonly byte[] LoginPrefsEntropy = Encoding.UTF8.GetBytes("CamboBIM.OnlineLicense.LoginPrefs.v1");

        public static string ResolveCachePath()
        {
            string dir = ResolveCacheDirectory();
            return string.IsNullOrWhiteSpace(dir) ? "" : Path.Combine(dir, SessionCacheFileName);
        }

        public static string ResolveLoginPreferencesPath()
        {
            string dir = ResolveCacheDirectory();
            return string.IsNullOrWhiteSpace(dir) ? "" : Path.Combine(dir, LoginPrefsFileName);
        }

        public static OnlineLicenseSession Load()
        {
            string path = ResolveCachePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new OnlineLicenseSession();
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return CamboBimJson.Deserialize<OnlineLicenseSession>(json) ?? new OnlineLicenseSession();
            }
            catch
            {
                return new OnlineLicenseSession();
            }
        }

        public static void Save(OnlineLicenseSession session)
        {
            if (session == null)
            {
                return;
            }

            string path = ResolveCachePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                session.UpdatedUtc = DateTime.UtcNow;
                string json = CamboBimJson.Serialize(session);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static void Clear()
        {
            string path = ResolveCachePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        public static OnlineLicenseLoginPreferences LoadLoginPreferences()
        {
            string path = ResolveLoginPreferencesPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new OnlineLicenseLoginPreferences();
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                OnlineLicenseLoginPreferences prefs =
                    CamboBimJson.Deserialize<OnlineLicenseLoginPreferences>(json) ?? new OnlineLicenseLoginPreferences();

                if (!prefs.RememberLogin)
                {
                    prefs.Username = "";
                    prefs.Password = "";
                    prefs.PasswordCipherText = "";
                    return prefs;
                }

                prefs.Username = prefs.Username ?? "";
                prefs.Password = UnprotectPassword(prefs.PasswordCipherText);
                return prefs;
            }
            catch
            {
                return new OnlineLicenseLoginPreferences();
            }
        }

        public static void SaveLoginPreferences(OnlineLicenseLoginPreferences prefs)
        {
            if (prefs == null)
            {
                return;
            }

            if (!prefs.RememberLogin)
            {
                ClearLoginPreferences();
                return;
            }

            string path = ResolveLoginPreferencesPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var persist = new OnlineLicenseLoginPreferences
                {
                    RememberLogin = true,
                    Username = (prefs.Username ?? "").Trim(),
                    PasswordCipherText = ProtectPassword(prefs.Password ?? ""),
                    UpdatedUtc = DateTime.UtcNow
                };

                string json = CamboBimJson.Serialize(persist);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static void ClearLoginPreferences()
        {
            string path = ResolveLoginPreferencesPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        public static DateTime TryParseUtc(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return DateTime.MinValue;
            }

            DateTime microsoftJsonDate;
            if (TryParseMicrosoftJsonDate(text, out microsoftJsonDate))
            {
                return microsoftJsonDate;
            }

            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        private static bool TryParseMicrosoftJsonDate(string text, out DateTime value)
        {
            value = DateTime.MinValue;
            string trimmed = (text ?? "").Trim();
            if (!trimmed.StartsWith("/Date(", StringComparison.OrdinalIgnoreCase) ||
                !trimmed.EndsWith(")/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string inner = trimmed.Substring(6, trimmed.Length - 8);
            int offsetIndex = inner.IndexOfAny(new[] { '+', '-' });
            if (offsetIndex > 0)
            {
                inner = inner.Substring(0, offsetIndex);
            }

            long milliseconds;
            if (!long.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds))
            {
                return false;
            }

            value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
            return true;
        }

        private static string ResolveCacheDirectory()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                return "";
            }

            string dir = Path.Combine(programData, "CamboBIM");
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch
            {
            }

            return dir;
        }

        private static string ProtectPassword(string password)
        {
            string text = password ?? "";
            if (text.Length == 0)
            {
                return "";
            }

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(text);
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, LoginPrefsEntropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return "";
            }
        }

        private static string UnprotectPassword(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                return "";
            }

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, LoginPrefsEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes) ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
