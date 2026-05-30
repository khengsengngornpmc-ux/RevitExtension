using System;
using System.Windows.Forms;

namespace CamboBIM.Revit2024.Addin.Licensing
{
    internal sealed class OnlineLicenseLoginResult
    {
        public bool Accepted { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool RememberLogin { get; set; }

        public OnlineLicenseLoginResult()
        {
            Accepted = false;
            Username = "";
            Password = "";
            RememberLogin = false;
        }
    }

    internal sealed class OnlineLicenseLoginForm : Form
    {
        private readonly TextBox _usernameTextBox;
        private readonly TextBox _passwordTextBox;
        private readonly Label _messageLabel;
        private readonly CheckBox _rememberCheckBox;
        private readonly CheckBox _showPasswordCheckBox;
        private readonly Button _requestAccountButton;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly string _accountRequestText;

        public OnlineLicenseLoginForm(string message, string defaultUsername, string defaultPassword, bool lockUsername, bool rememberLogin)
            : this(message, defaultUsername, defaultPassword, lockUsername, rememberLogin, "")
        {
        }

        public OnlineLicenseLoginForm(
            string message,
            string defaultUsername,
            string defaultPassword,
            bool lockUsername,
            bool rememberLogin,
            string accountRequestText)
        {
            _accountRequestText = accountRequestText ?? "";

            Text = "MHNK License";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 460;
            Height = 300;

            _messageLabel = new Label();
            _messageLabel.Left = 16;
            _messageLabel.Top = 12;
            _messageLabel.Width = 410;
            _messageLabel.Height = 48;
            _messageLabel.Text = string.IsNullOrWhiteSpace(message)
                ? "Sign in to activate this PC license seat."
                : message;

            var userLabel = new Label();
            userLabel.Left = 16;
            userLabel.Top = 72;
            userLabel.Width = 90;
            userLabel.Text = "MHNK User:";

            _usernameTextBox = new TextBox();
            _usernameTextBox.Left = 100;
            _usernameTextBox.Top = 68;
            _usernameTextBox.Width = 326;
            _usernameTextBox.Text = defaultUsername ?? "";
            _usernameTextBox.ReadOnly = lockUsername;
            if (lockUsername)
            {
                _usernameTextBox.TabStop = false;
            }

            var passLabel = new Label();
            passLabel.Left = 16;
            passLabel.Top = 108;
            passLabel.Width = 80;
            passLabel.Text = "Password:";

            _passwordTextBox = new TextBox();
            _passwordTextBox.Left = 100;
            _passwordTextBox.Top = 104;
            _passwordTextBox.Width = 326;
            _passwordTextBox.UseSystemPasswordChar = true;
            _passwordTextBox.Text = defaultPassword ?? "";

            _rememberCheckBox = new CheckBox();
            _rememberCheckBox.Left = 100;
            _rememberCheckBox.Top = 136;
            _rememberCheckBox.Width = 150;
            _rememberCheckBox.Text = "Remember on this PC";
            _rememberCheckBox.Checked = rememberLogin;

            _showPasswordCheckBox = new CheckBox();
            _showPasswordCheckBox.Left = 260;
            _showPasswordCheckBox.Top = 136;
            _showPasswordCheckBox.Width = 166;
            _showPasswordCheckBox.Text = "View Password";
            _showPasswordCheckBox.CheckedChanged += OnShowPasswordCheckedChanged;

            _requestAccountButton = new Button();
            _requestAccountButton.Left = 16;
            _requestAccountButton.Top = 178;
            _requestAccountButton.Width = 120;
            _requestAccountButton.Text = "Request Account";
            _requestAccountButton.Click += OnRequestAccountClicked;
            _requestAccountButton.Enabled = !string.IsNullOrWhiteSpace(_accountRequestText);

            _okButton = new Button();
            _okButton.Left = 270;
            _okButton.Top = 178;
            _okButton.Width = 75;
            _okButton.Text = "Login";
            _okButton.DialogResult = DialogResult.OK;

            _cancelButton = new Button();
            _cancelButton.Left = 351;
            _cancelButton.Top = 178;
            _cancelButton.Width = 75;
            _cancelButton.Text = "Cancel";
            _cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(_messageLabel);
            Controls.Add(userLabel);
            Controls.Add(_usernameTextBox);
            Controls.Add(passLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(_rememberCheckBox);
            Controls.Add(_showPasswordCheckBox);
            Controls.Add(_requestAccountButton);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public OnlineLicenseLoginResult RunAndGetResult()
        {
            var result = new OnlineLicenseLoginResult();
            DialogResult dialog = ShowDialog();
            if (dialog == DialogResult.OK)
            {
                result.Accepted = true;
                result.Username = (_usernameTextBox.Text ?? "").Trim();
                result.Password = _passwordTextBox.Text ?? "";
                result.RememberLogin = _rememberCheckBox.Checked;
            }

            return result;
        }

        private void OnShowPasswordCheckedChanged(object sender, EventArgs e)
        {
            _passwordTextBox.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;
        }

        private void OnRequestAccountClicked(object sender, EventArgs e)
        {
            string text = string.IsNullOrWhiteSpace(_accountRequestText)
                ? "Support contact is not configured."
                : _accountRequestText;

            MessageBox.Show(this, text, "MHNK Account", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
