using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin.Licensing
{
    internal sealed class OnlineLicenseService
    {
        private readonly OnlineLicenseConfig _config;
        private readonly OnlineLicenseClient _client;
        private OnlineLicenseSession _session;
        private Timer _heartbeatTimer;
        private readonly string _logPath;

        public OnlineLicenseService()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            _config = OnlineLicenseConfig.Load(baseDir);
            _client = new OnlineLicenseClient(_config);
            _session = OnlineLicenseCache.Load();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(appData, "CamboBIM");
            try
            {
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
            }
            catch
            {
            }

            _logPath = Path.Combine(logDir, "online-license.log");
        }

        public bool ValidateOrLogin(string autodeskLoginUserId, out string failureMessage, bool forceFreshLogin = false)
        {
            failureMessage = "";
            string autodeskUser = (autodeskLoginUserId ?? "").Trim();
            string cachedUsername = _session != null ? (_session.Username ?? "").Trim() : "";
            OnlineLicenseLoginPreferences loginPrefs = OnlineLicenseCache.LoadLoginPreferences();

            if (!_config.Enabled)
            {
                ApplyUnlockedTestSession();
                WriteLog("Startup allowed: online license disabled for test mode.");
                return true;
            }

            if (!_config.IsValidForOnlineCheck())
            {
                failureMessage = "Online license is enabled but configuration is invalid." + Environment.NewLine +
                                 "Config file: " + (_config.ConfigPath ?? "<missing>") + Environment.NewLine +
                                 "Set server_url in config.";
                return false;
            }

            if (!forceFreshLogin && _session != null && _session.IsUsable())
            {
                OnlineLicenseResult heartbeat = _client.Heartbeat(_session.SessionToken);
                if (heartbeat.Success)
                {
                    if (heartbeat.Session != null && !string.IsNullOrWhiteSpace(heartbeat.Session.SessionToken))
                    {
                        _session.SessionToken = heartbeat.Session.SessionToken;
                    }

                    if (heartbeat.Session != null && heartbeat.Session.ExpiresUtc > DateTime.MinValue)
                    {
                        _session.ExpiresUtc = heartbeat.Session.ExpiresUtc;
                    }

                    if (heartbeat.Session != null && heartbeat.Session.LicenseExpiresUtc > DateTime.MinValue)
                    {
                        _session.LicenseExpiresUtc = heartbeat.Session.LicenseExpiresUtc;
                    }

                    OnlineLicenseCache.Save(_session);
                    StartHeartbeat();
                    WriteLog("Heartbeat success with cached session.");
                    return true;
                }

                WriteLog("Cached session heartbeat failed: " + (heartbeat.Message ?? ""));
            }

            if (forceFreshLogin && _session != null && !string.IsNullOrWhiteSpace(_session.SessionToken))
            {
                try
                {
                    OnlineLicenseResult release = _client.Release(_session.SessionToken);
                    WriteLog("Force-login release result: " + (release.Success ? "success" : "failed") + " - " + (release.Message ?? ""));
                }
                catch (Exception ex)
                {
                    WriteLog("Force-login release exception: " + ex.Message);
                }
            }

            // Force fresh login
            OnlineLicenseCache.Clear();
            _session = new OnlineLicenseSession();

            string prefillUser = (loginPrefs != null && loginPrefs.RememberLogin && !string.IsNullOrWhiteSpace(loginPrefs.Username))
                ? loginPrefs.Username.Trim()
                : (!string.IsNullOrWhiteSpace(cachedUsername)
                    ? cachedUsername
                    : (IsLikelyEmail(autodeskUser) ? autodeskUser : ""));
            string prefillPassword = (loginPrefs != null && loginPrefs.RememberLogin)
                ? (loginPrefs.Password ?? "")
                : "";
            bool rememberLoginDefault = loginPrefs != null && loginPrefs.RememberLogin;

            int attempts = 0;
            while (attempts < 3)
            {
                attempts++;

                var form = new OnlineLicenseLoginForm(
                    "",
                    prefillUser,
                    prefillPassword,
                    lockUsername: false,
                    rememberLogin: rememberLoginDefault,
                    accountRequestText: BuildAccountRequestText());
                OnlineLicenseLoginResult login = form.RunAndGetResult();
                if (!login.Accepted)
                {
                    failureMessage = "Login canceled. Online license is required to use this extension.";
                    return false;
                }

                string username = (login.Username ?? "").Trim();
                if (string.IsNullOrWhiteSpace(username))
                {
                    TaskDialog.Show("MHNK License", "MHNK user email is required.");
                    prefillUser = username;
                    prefillPassword = login.Password ?? "";
                    rememberLoginDefault = login.RememberLogin;
                    continue;
                }

                if (!IsLikelyEmail(username))
                {
                    TaskDialog.Show("MHNK License", "MHNK user must be an email address.");
                    prefillUser = username;
                    prefillPassword = login.Password ?? "";
                    rememberLoginDefault = login.RememberLogin;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(login.Password))
                {
                    TaskDialog.Show("MHNK License", "Password is required.");
                    prefillUser = username;
                    prefillPassword = login.Password ?? "";
                    rememberLoginDefault = login.RememberLogin;
                    continue;
                }

                OnlineLicenseResult activate = _client.Activate(username, login.Password, Assembly.GetExecutingAssembly().GetName().Version.ToString());
                if (activate.Success && activate.Session != null && !string.IsNullOrWhiteSpace(activate.Session.SessionToken))
                {
                    _session = activate.Session;
                    _session.Username = username;
                    OnlineLicenseCache.Save(_session);
                    OnlineLicenseCache.SaveLoginPreferences(new OnlineLicenseLoginPreferences
                    {
                        RememberLogin = login.RememberLogin,
                        Username = username,
                        Password = login.Password ?? ""
                    });
                    StartHeartbeat();
                    WriteLog("Activate success for user: " + username);
                    return true;
                }

                string message = "License activation failed." + Environment.NewLine + (activate.Message ?? "Unknown error.");
                TaskDialog.Show("MHNK License", message);
                WriteLog("Activate failed: " + (activate.Message ?? ""));
                prefillUser = username;
                prefillPassword = login.Password ?? "";
                rememberLoginDefault = login.RememberLogin;
            }

            failureMessage = "License activation failed after multiple attempts.";
            return false;
        }

        public bool RefreshSessionStatus(out string failureMessage)
        {
            failureMessage = "";

            if (!_config.Enabled)
            {
                ApplyUnlockedTestSession();
                return true;
            }

            if (_session == null || string.IsNullOrWhiteSpace(_session.SessionToken))
            {
                failureMessage = "No active license session found.";
                return false;
            }

            OnlineLicenseResult heartbeat = _client.Heartbeat(_session.SessionToken);
            if (!heartbeat.Success)
            {
                failureMessage = heartbeat.Message ?? "Heartbeat failed.";
                WriteLog("Refresh session status failed: " + failureMessage);
                return false;
            }

            if (heartbeat.Session != null && !string.IsNullOrWhiteSpace(heartbeat.Session.SessionToken))
            {
                _session.SessionToken = heartbeat.Session.SessionToken;
            }

            if (heartbeat.Session != null && heartbeat.Session.ExpiresUtc > DateTime.MinValue)
            {
                _session.ExpiresUtc = heartbeat.Session.ExpiresUtc;
            }

            if (heartbeat.Session != null && heartbeat.Session.LicenseExpiresUtc > DateTime.MinValue)
            {
                _session.LicenseExpiresUtc = heartbeat.Session.LicenseExpiresUtc;
            }

            OnlineLicenseCache.Save(_session);
            return true;
        }

        public OnlineLicenseSession GetCurrentSessionSnapshot()
        {
            if (_session == null)
            {
                return new OnlineLicenseSession();
            }

            return new OnlineLicenseSession
            {
                SessionToken = _session.SessionToken ?? "",
                Username = _session.Username ?? "",
                ExpiresUtc = _session.ExpiresUtc,
                LicenseExpiresUtc = _session.LicenseExpiresUtc,
                UpdatedUtc = _session.UpdatedUtc
            };
        }

        public bool IsUnlockedTestMode()
        {
            return !_config.Enabled;
        }

        public bool ChangePassword(string username, string currentPassword, string newPassword, out string failureMessage)
        {
            failureMessage = "";

            string effectiveUsername = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(effectiveUsername) && _session != null)
            {
                effectiveUsername = (_session.Username ?? "").Trim();
            }

            if (string.IsNullOrWhiteSpace(effectiveUsername))
            {
                failureMessage = "Username is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                failureMessage = "Current password is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                failureMessage = "New password is required.";
                return false;
            }

            OnlineLicenseResult result = _client.ChangePassword(effectiveUsername, currentPassword, newPassword);
            if (!result.Success)
            {
                failureMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "Password change failed."
                    : result.Message;
                WriteLog("Change password failed for user " + effectiveUsername + ": " + failureMessage);
                return false;
            }

            if (_session == null)
            {
                _session = new OnlineLicenseSession();
            }

            _session.Username = effectiveUsername;
            OnlineLicenseCache.Save(_session);
            OnlineLicenseLoginPreferences prefs = OnlineLicenseCache.LoadLoginPreferences();
            if (prefs != null && prefs.RememberLogin &&
                string.Equals((prefs.Username ?? "").Trim(), effectiveUsername, StringComparison.OrdinalIgnoreCase))
            {
                prefs.Username = effectiveUsername;
                prefs.Password = newPassword ?? "";
                OnlineLicenseCache.SaveLoginPreferences(prefs);
            }
            WriteLog("Password changed successfully for user: " + effectiveUsername);
            return true;
        }

        public string BuildSupportContactText()
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(_config.SupportContactEmail))
            {
                lines.Add("Email: " + _config.SupportContactEmail.Trim());
            }

            if (!string.IsNullOrWhiteSpace(_config.SupportContactPhone))
            {
                lines.Add("Phone: " + _config.SupportContactPhone.Trim());
            }

            if (!string.IsNullOrWhiteSpace(_config.SupportContactTelegram))
            {
                lines.Add("Telegram: " + _config.SupportContactTelegram.Trim());
            }

            if (lines.Count == 0)
            {
                return "Support contact is not configured.";
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        public string BuildAccountRequestText()
        {
            var lines = new List<string>();
            lines.Add("To create or request an MHNK account, send this information to support:");
            lines.Add("");
            lines.Add("Product: " + (_config.ProductCode ?? CamboBimRuntime.DefaultProductCode));
            lines.Add("Revit year: " + CamboBimRuntime.RevitYear);
            lines.Add("License mode: " + (_config.Enabled ? "Online license" : "All Users Test Mode"));
            lines.Add("Machine name: " + (Environment.MachineName ?? ""));
            lines.Add("Machine ID: " + BuildShortMachineId());
            lines.Add("Config path: " + (string.IsNullOrWhiteSpace(_config.ConfigPath) ? "<not resolved>" : _config.ConfigPath));
            lines.Add("");
            lines.Add("Support contact:");
            lines.Add(BuildSupportContactText());
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        public void ReleaseOnShutdown(bool releaseSeat = true)
        {
            StopHeartbeat();

            if (!releaseSeat)
            {
                return;
            }

            if (!_config.Enabled)
            {
                return;
            }

            if (_session == null || string.IsNullOrWhiteSpace(_session.SessionToken))
            {
                return;
            }

            try
            {
                OnlineLicenseResult release = _client.Release(_session.SessionToken);
                WriteLog("Release result: " + (release.Success ? "success" : "failed") + " - " + (release.Message ?? ""));
            }
            catch (Exception ex)
            {
                WriteLog("Release exception: " + ex.Message);
            }
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();

            int periodSeconds = _config.HeartbeatSeconds;
            _heartbeatTimer = new Timer(_ =>
            {
                try
                {
                    if (_session == null || string.IsNullOrWhiteSpace(_session.SessionToken))
                    {
                        return;
                    }

                    OnlineLicenseResult heartbeat = _client.Heartbeat(_session.SessionToken);
                    if (heartbeat.Success)
                    {
                        if (heartbeat.Session != null && !string.IsNullOrWhiteSpace(heartbeat.Session.SessionToken))
                        {
                            _session.SessionToken = heartbeat.Session.SessionToken;
                        }

                        if (heartbeat.Session != null && heartbeat.Session.ExpiresUtc > DateTime.MinValue)
                        {
                            _session.ExpiresUtc = heartbeat.Session.ExpiresUtc;
                        }

                        if (heartbeat.Session != null && heartbeat.Session.LicenseExpiresUtc > DateTime.MinValue)
                        {
                            _session.LicenseExpiresUtc = heartbeat.Session.LicenseExpiresUtc;
                        }

                        OnlineLicenseCache.Save(_session);
                    }
                    else
                    {
                        WriteLog("Heartbeat warning: " + (heartbeat.Message ?? ""));
                    }
                }
                catch (Exception ex)
                {
                    WriteLog("Heartbeat exception: " + ex.Message);
                }
            }, null, TimeSpan.FromSeconds(periodSeconds), TimeSpan.FromSeconds(periodSeconds));
        }

        private void StopHeartbeat()
        {
            if (_heartbeatTimer == null)
            {
                return;
            }

            try
            {
                _heartbeatTimer.Dispose();
            }
            catch
            {
            }

            _heartbeatTimer = null;
        }

        private void ApplyUnlockedTestSession()
        {
            _session = new OnlineLicenseSession
            {
                SessionToken = "TEST-UNLOCKED",
                Username = "All Users Test",
                ExpiresUtc = DateTime.MaxValue,
                LicenseExpiresUtc = DateTime.MaxValue,
                UpdatedUtc = DateTime.UtcNow
            };
        }

        private string BuildShortMachineId()
        {
            string fingerprint = _client != null ? (_client.MachineFingerprint ?? "") : "";
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return "<not available>";
            }

            return fingerprint.Length <= 16 ? fingerprint : fingerprint.Substring(0, 16);
        }

        private void WriteLog(string message)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            try
            {
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + (message ?? "") + Environment.NewLine;
                File.AppendAllText(_logPath, line);
            }
            catch
            {
            }
        }

        private static bool IsLikelyEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            int atIndex = trimmed.IndexOf('@');
            if (atIndex <= 0 || atIndex >= trimmed.Length - 1)
            {
                return false;
            }

            int dotAfterAt = trimmed.IndexOf('.', atIndex + 1);
            return dotAfterAt > atIndex + 1 && dotAfterAt < trimmed.Length - 1;
        }
    }
}
