using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CamboBIM.LicenseConfigSetupGui
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string prefillUrl = "";
            if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                prefillUrl = args[0].Trim().Trim('"');
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(prefillUrl));
        }
    }

    internal sealed class SetupForm : Form
    {
        private const string ProductCode = "CBIM_RVT2024_EXTENSION";
        private readonly TextBox _serverUrlTextBox;
        private readonly TextBox _targetPathTextBox;
        private readonly Label _statusLabel;
        private readonly Button _testButton;
        private readonly Button _saveButton;

        public SetupForm(string prefillUrl)
        {
            Text = "MHNK License Config Setup";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 430);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var title = new Label
            {
                Text = "MHNK Online License Setup",
                AutoSize = true,
                Location = new Point(18, 16),
                Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point)
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "Paste the Google Apps Script Web App URL, then click Save Config.",
                AutoSize = true,
                Location = new Point(20, 46),
                ForeColor = Color.DimGray
            };
            Controls.Add(subtitle);

            var urlLabel = new Label
            {
                Text = "Web App URL (server_url)",
                AutoSize = true,
                Location = new Point(20, 82)
            };
            Controls.Add(urlLabel);

            _serverUrlTextBox = new TextBox
            {
                Location = new Point(20, 104),
                Width = 720,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_serverUrlTextBox);

            var tips = new Label
            {
                Text = "Use the URL from Apps Script: Deploy > Manage deployments > Web app > URL (do not use Deployment ID or Library URL).",
                AutoSize = false,
                Width = 720,
                Height = 36,
                Location = new Point(20, 132),
                ForeColor = Color.DimGray
            };
            Controls.Add(tips);

            var targetLabel = new Label
            {
                Text = "Target config file",
                AutoSize = true,
                Location = new Point(20, 176)
            };
            Controls.Add(targetLabel);

            _targetPathTextBox = new TextBox
            {
                Location = new Point(20, 198),
                Width = 720,
                ReadOnly = true,
                BackColor = Color.WhiteSmoke
            };
            Controls.Add(_targetPathTextBox);

            var instructionBox = new TextBox
            {
                Location = new Point(20, 235),
                Width = 720,
                Height = 95,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.WhiteSmoke,
                Text =
                    "Admin gives user:\r\n" +
                    "1) This setup tool (.exe)\r\n" +
                    "2) Web App URL\r\n" +
                    "3) User Name (email)\r\n" +
                    "4) Temporary Password\r\n\r\n" +
                    "After saving config, user opens Revit > MHNK and logs in, then changes password."
            };
            Controls.Add(instructionBox);

            var pasteButton = new Button
            {
                Text = "Paste From Clipboard",
                Location = new Point(20, 344),
                Width = 150,
                Height = 32
            };
            pasteButton.Click += OnPasteFromClipboardClick;
            Controls.Add(pasteButton);

            _testButton = new Button
            {
                Text = "Test URL",
                Location = new Point(180, 344),
                Width = 100,
                Height = 32
            };
            _testButton.Click += OnTestUrlClick;
            Controls.Add(_testButton);

            _saveButton = new Button
            {
                Text = "Save Config",
                Location = new Point(290, 344),
                Width = 110,
                Height = 32,
                BackColor = Color.FromArgb(46, 117, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Standard
            };
            _saveButton.Click += OnSaveConfigClick;
            Controls.Add(_saveButton);

            var openFolderButton = new Button
            {
                Text = "Open Folder",
                Location = new Point(410, 344),
                Width = 110,
                Height = 32
            };
            openFolderButton.Click += OnOpenFolderClick;
            Controls.Add(openFolderButton);

            var closeButton = new Button
            {
                Text = "Close",
                Location = new Point(630, 344),
                Width = 110,
                Height = 32
            };
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            _statusLabel = new Label
            {
                AutoSize = false,
                Width = 720,
                Height = 30,
                Location = new Point(20, 388),
                ForeColor = Color.FromArgb(50, 50, 50),
                Text = "Ready."
            };
            Controls.Add(_statusLabel);

            _targetPathTextBox.Text = GetTargetConfigPath();
            PrefillServerUrl(prefillUrl);
        }

        private void PrefillServerUrl(string prefillUrl)
        {
            string url = prefillUrl ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                url = TryReadServerUrlFromExistingConfig();
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clipboard = (Clipboard.GetText() ?? "").Trim();
                        if (clipboard.IndexOf("script.google.com/macros", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            url = clipboard;
                        }
                    }
                }
                catch
                {
                }
            }

            _serverUrlTextBox.Text = url ?? "";
        }

        private void OnPasteFromClipboardClick(object sender, EventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _serverUrlTextBox.Text = (Clipboard.GetText() ?? "").Trim();
                    SetStatus("Pasted URL from clipboard.", false);
                }
                else
                {
                    SetStatus("Clipboard does not contain text.", true);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Clipboard error: " + ex.Message, true);
            }
        }

        private void OnTestUrlClick(object sender, EventArgs e)
        {
            string serverUrl;
            if (!TryValidateServerUrl(out serverUrl))
            {
                return;
            }

            UseWaitCursor = true;
            _testButton.Enabled = false;
            _saveButton.Enabled = false;
            try
            {
                string message;
                bool ok = TestWebAppUrl(serverUrl, out message);
                SetStatus(message, !ok);
                MessageBox.Show(
                    this,
                    message,
                    ok ? "Test URL OK" : "Test URL Failed",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                _testButton.Enabled = true;
                _saveButton.Enabled = true;
            }
        }

        private void OnSaveConfigClick(object sender, EventArgs e)
        {
            string serverUrl;
            if (!TryValidateServerUrl(out serverUrl))
            {
                return;
            }

            try
            {
                string targetPath = GetTargetConfigPath();
                string targetDir = Path.GetDirectoryName(targetPath) ?? "";
                if (string.IsNullOrWhiteSpace(targetDir))
                {
                    throw new InvalidOperationException("Could not resolve target directory.");
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string backupPath = "";
                if (File.Exists(targetPath))
                {
                    backupPath = Path.Combine(
                        targetDir,
                        "online-license.config.backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                    File.Copy(targetPath, backupPath, true);
                }

                string json = BuildConfigJson(serverUrl);
                File.WriteAllText(targetPath, json, new UTF8Encoding(false));

                var sb = new StringBuilder();
                sb.AppendLine("Config saved successfully.");
                sb.AppendLine();
                sb.AppendLine("Target:");
                sb.AppendLine(targetPath);
                if (!string.IsNullOrWhiteSpace(backupPath))
                {
                    sb.AppendLine();
                    sb.AppendLine("Backup created:");
                    sb.AppendLine(backupPath);
                }
                sb.AppendLine();
                sb.AppendLine("Next:");
                sb.AppendLine("1. Open Revit 2024");
                sb.AppendLine("2. Open MHNK add-in");
                sb.AppendLine("3. Login with User Name + temporary Password from admin");
                sb.AppendLine("4. Change password after login (recommended)");

                SetStatus("Config saved: " + targetPath, false);
                MessageBox.Show(this, sb.ToString(), "MHNK License Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message, true);
                MessageBox.Show(this, "Failed to save config.\r\n\r\n" + ex.Message, "MHNK License Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnOpenFolderClick(object sender, EventArgs e)
        {
            try
            {
                string targetDir = Path.GetDirectoryName(GetTargetConfigPath()) ?? "";
                if (string.IsNullOrWhiteSpace(targetDir))
                {
                    throw new InvalidOperationException("Could not resolve target directory.");
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                System.Diagnostics.Process.Start("explorer.exe", targetDir);
            }
            catch (Exception ex)
            {
                SetStatus("Open folder failed: " + ex.Message, true);
            }
        }

        private bool TryValidateServerUrl(out string serverUrl)
        {
            serverUrl = (_serverUrlTextBox.Text ?? "").Trim().Trim('"').Trim('\'');
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                SetStatus("Please paste the Web App URL first.", true);
                MessageBox.Show(this, "Please paste the Google Apps Script Web App URL first.", "Missing URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out uri))
            {
                SetStatus("Invalid URL format.", true);
                MessageBox.Show(this, "Invalid URL format.\r\nPlease paste the full Web App URL.", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("URL must use HTTPS.", true);
                MessageBox.Show(this, "URL must use HTTPS.", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void SetStatus(string message, bool isError)
        {
            _statusLabel.Text = message ?? "";
            _statusLabel.ForeColor = isError ? Color.Firebrick : Color.FromArgb(50, 50, 50);
        }

        private static string GetTargetConfigPath()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                programData = @"C:\ProgramData";
            }

            return Path.Combine(programData, "CamboBIM", "online-license.config.json");
        }

        private static bool TestWebAppUrl(string url, out string message)
        {
            message = "";
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;

                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    string content = client.DownloadString(url);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        message = "URL responded but returned empty content.";
                        return false;
                    }

                    try
                    {
                        var serializer = new JavaScriptSerializer();
                        var map = serializer.DeserializeObject(content) as Dictionary<string, object>;
                        if (map != null && map.ContainsKey("success"))
                        {
                            bool success = false;
                            object successRaw = map["success"];
                            if (successRaw is bool)
                            {
                                success = (bool)successRaw;
                            }
                            else if (successRaw != null)
                            {
                                bool.TryParse(Convert.ToString(successRaw), out success);
                            }

                            string responseMessage = map.ContainsKey("message") ? Convert.ToString(map["message"]) : "No message.";
                            if (success)
                            {
                                message = "Web app test OK.\r\n" + responseMessage;
                                return true;
                            }

                            message = "Web app responded but reports failure.\r\n" + responseMessage;
                            return false;
                        }

                        message = "URL responded with JSON, but not the expected license API format.";
                        return false;
                    }
                    catch
                    {
                        message = "URL responded, but the content is not valid JSON.\r\nMake sure this is the Web app URL.";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                message = "Web app test failed.\r\n" + ex.Message;
                return false;
            }
        }

        private static string TryReadServerUrlFromExistingConfig()
        {
            string path = GetTargetConfigPath();
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                string json = File.ReadAllText(path);
                var serializer = new JavaScriptSerializer();
                var map = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (map == null || !map.ContainsKey("server_url"))
                {
                    return "";
                }

                return (Convert.ToString(map["server_url"]) ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string BuildConfigJson(string serverUrl)
        {
            var config = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "enabled", true },
                { "server_url", serverUrl },
                { "product_code", ProductCode },
                { "activate_endpoint", "?action=activate" },
                { "heartbeat_endpoint", "?action=heartbeat" },
                { "release_endpoint", "?action=release" },
                { "change_password_endpoint", "?action=change_password" },
                { "support_contact_email", "support@cambobim.com" },
                { "support_contact_phone", "+855 69 901 004" },
                { "support_contact_telegram", "@CamboBIMSupport" },
                { "timeout_seconds", 20 },
                { "heartbeat_seconds", 300 }
            };

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            // Pretty-print manually to keep the config readable for support staff.
            return "{\r\n" +
                   "  \"enabled\": true,\r\n" +
                   "  \"server_url\": \"" + EscapeJson(serverUrl) + "\",\r\n" +
                   "  \"product_code\": \"" + ProductCode + "\",\r\n" +
                   "  \"activate_endpoint\": \"?action=activate\",\r\n" +
                   "  \"heartbeat_endpoint\": \"?action=heartbeat\",\r\n" +
                   "  \"release_endpoint\": \"?action=release\",\r\n" +
                   "  \"change_password_endpoint\": \"?action=change_password\",\r\n" +
                   "  \"support_contact_email\": \"support@cambobim.com\",\r\n" +
                   "  \"support_contact_phone\": \"+855 69 901 004\",\r\n" +
                   "  \"support_contact_telegram\": \"@CamboBIMSupport\",\r\n" +
                   "  \"timeout_seconds\": 20,\r\n" +
                   "  \"heartbeat_seconds\": 300\r\n" +
                   "}\r\n";
            // `config` and `serializer` intentionally kept for future extension / validation.
        }

        private static string EscapeJson(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
