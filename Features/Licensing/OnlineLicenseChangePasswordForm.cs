using System;
using System.Windows.Forms;

namespace CamboBIM.Revit2024.Addin.Licensing
{
    internal sealed class OnlineLicenseChangePasswordResult
    {
        public bool Accepted { get; set; }
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }

        public OnlineLicenseChangePasswordResult()
        {
            Accepted = false;
            CurrentPassword = "";
            NewPassword = "";
        }
    }

    internal sealed class OnlineLicenseChangePasswordForm : Form
    {
        private readonly TextBox _usernameTextBox;
        private readonly TextBox _currentPasswordTextBox;
        private readonly TextBox _newPasswordTextBox;
        private readonly TextBox _confirmPasswordTextBox;
        private readonly Button _changeButton;
        private readonly Button _cancelButton;

        public OnlineLicenseChangePasswordForm(string username)
        {
            Text = "MHNK License - Change Password";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 500;
            Height = 300;

            var userLabel = new Label();
            userLabel.Left = 16;
            userLabel.Top = 24;
            userLabel.Width = 120;
            userLabel.Text = "MHNK User:";

            _usernameTextBox = new TextBox();
            _usernameTextBox.Left = 150;
            _usernameTextBox.Top = 20;
            _usernameTextBox.Width = 314;
            _usernameTextBox.ReadOnly = true;
            _usernameTextBox.Text = username ?? "";

            var currentPassLabel = new Label();
            currentPassLabel.Left = 16;
            currentPassLabel.Top = 62;
            currentPassLabel.Width = 120;
            currentPassLabel.Text = "Current Password:";

            _currentPasswordTextBox = new TextBox();
            _currentPasswordTextBox.Left = 150;
            _currentPasswordTextBox.Top = 58;
            _currentPasswordTextBox.Width = 314;
            _currentPasswordTextBox.UseSystemPasswordChar = true;

            var newPassLabel = new Label();
            newPassLabel.Left = 16;
            newPassLabel.Top = 100;
            newPassLabel.Width = 120;
            newPassLabel.Text = "New Password:";

            _newPasswordTextBox = new TextBox();
            _newPasswordTextBox.Left = 150;
            _newPasswordTextBox.Top = 96;
            _newPasswordTextBox.Width = 314;
            _newPasswordTextBox.UseSystemPasswordChar = true;

            var confirmPassLabel = new Label();
            confirmPassLabel.Left = 16;
            confirmPassLabel.Top = 138;
            confirmPassLabel.Width = 120;
            confirmPassLabel.Text = "Confirm Password:";

            _confirmPasswordTextBox = new TextBox();
            _confirmPasswordTextBox.Left = 150;
            _confirmPasswordTextBox.Top = 134;
            _confirmPasswordTextBox.Width = 314;
            _confirmPasswordTextBox.UseSystemPasswordChar = true;

            _changeButton = new Button();
            _changeButton.Left = 308;
            _changeButton.Top = 186;
            _changeButton.Width = 75;
            _changeButton.Text = "Change";
            _changeButton.Click += OnChangeClick;

            _cancelButton = new Button();
            _cancelButton.Left = 389;
            _cancelButton.Top = 186;
            _cancelButton.Width = 75;
            _cancelButton.Text = "Cancel";
            _cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(userLabel);
            Controls.Add(_usernameTextBox);
            Controls.Add(currentPassLabel);
            Controls.Add(_currentPasswordTextBox);
            Controls.Add(newPassLabel);
            Controls.Add(_newPasswordTextBox);
            Controls.Add(confirmPassLabel);
            Controls.Add(_confirmPasswordTextBox);
            Controls.Add(_changeButton);
            Controls.Add(_cancelButton);

            AcceptButton = _changeButton;
            CancelButton = _cancelButton;
        }

        public OnlineLicenseChangePasswordResult RunAndGetResult()
        {
            var result = new OnlineLicenseChangePasswordResult();
            DialogResult dialog = ShowDialog();
            if (dialog == DialogResult.OK)
            {
                result.Accepted = true;
                result.CurrentPassword = _currentPasswordTextBox.Text ?? "";
                result.NewPassword = _newPasswordTextBox.Text ?? "";
            }

            return result;
        }

        private void OnChangeClick(object sender, EventArgs e)
        {
            string currentPassword = _currentPasswordTextBox.Text ?? "";
            string newPassword = _newPasswordTextBox.Text ?? "";
            string confirmPassword = _confirmPasswordTextBox.Text ?? "";

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                MessageBox.Show("Current password is required.", "MHNK License", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                MessageBox.Show("New password is required.", "MHNK License", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (newPassword.Length < 8)
            {
                MessageBox.Show("New password must be at least 8 characters.", "MHNK License", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                MessageBox.Show("New password and confirmation do not match.", "MHNK License", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
