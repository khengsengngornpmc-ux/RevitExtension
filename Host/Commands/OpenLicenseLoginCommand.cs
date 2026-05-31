using System;
using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CamboBIM.Revit2024.Addin.Licensing;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class OpenLicenseLoginCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string autodeskUserId = commandData?.Application?.Application?.LoginUserId ?? "";
            App.SetAutodeskLoginUserId(autodeskUserId);

            if (!App.EnsureLicenseActivated(out string licenseFailure, autodeskUserId, forceFreshLogin: true))
            {
                if (!string.IsNullOrWhiteSpace(licenseFailure))
                {
                    TaskDialog.Show("MHNK License", licenseFailure);
                }

                return Result.Cancelled;
            }

            ShowLicenseToolDialog();
            return Result.Succeeded;
        }

        private static void ShowLicenseToolDialog()
        {
            OnlineLicenseSession session;
            string refreshMessage;
            bool refreshed = App.TryRefreshLicenseStatus(out session, out refreshMessage);
            bool testMode = App.IsLicenseTestModeUnlocked();
            if (!refreshed)
            {
                session = App.GetLicenseSessionSnapshot();
            }

            while (true)
            {
                var dialog = new TaskDialog("MHNK License");
                dialog.MainInstruction = testMode
                    ? "MHNK is unlocked for all users test."
                    : "License login successful.";
                dialog.MainContent = BuildLicenseInfoContent(session, refreshMessage, testMode);
                if (!testMode)
                {
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Change Password");
                }

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Request / Create Account");
                dialog.CommonButtons = TaskDialogCommonButtons.Close;
                dialog.DefaultButton = TaskDialogResult.Close;

                TaskDialogResult result = dialog.Show();
                if (result == TaskDialogResult.CommandLink2)
                {
                    TaskDialog.Show("MHNK Account", App.GetLicenseAccountRequestText());
                    refreshMessage = "Account request information shown.";
                    continue;
                }

                if (result != TaskDialogResult.CommandLink1)
                {
                    return;
                }

                if (testMode)
                {
                    refreshMessage = "Password change is disabled in All Users Test Mode.";
                    continue;
                }

                var changeForm = new OnlineLicenseChangePasswordForm(session != null ? session.Username : "");
                OnlineLicenseChangePasswordResult change = changeForm.RunAndGetResult();
                if (!change.Accepted)
                {
                    refreshMessage = "Password change canceled.";
                    continue;
                }

                string changeFailure;
                if (!App.ChangeLicensePassword(session != null ? session.Username : "", change.CurrentPassword, change.NewPassword, out changeFailure))
                {
                    TaskDialog.Show("MHNK License", "Change password failed." + Environment.NewLine + (changeFailure ?? "Unknown error."));
                    refreshMessage = string.IsNullOrWhiteSpace(changeFailure) ? "Password change failed." : changeFailure;
                    continue;
                }

                TaskDialog.Show("MHNK License", "Password changed successfully.");
                refreshMessage = "Password changed successfully.";
            }
        }

        private static string BuildLicenseInfoContent(OnlineLicenseSession session, string statusMessage, bool testMode)
        {
            string username = (session != null ? session.Username : "") ?? "";
            if (string.IsNullOrWhiteSpace(username))
            {
                username = "<unknown>";
            }

            DateTime seatLeaseExpiresUtc = session != null ? session.ExpiresUtc : DateTime.MinValue;
            DateTime licenseExpiresUtc = session != null ? session.LicenseExpiresUtc : DateTime.MinValue;
            DateTime displayExpiresUtc = licenseExpiresUtc > DateTime.MinValue ? licenseExpiresUtc : seatLeaseExpiresUtc;

            string expiresText = displayExpiresUtc > DateTime.MinValue
                ? displayExpiresUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                : "Not provided by server";

            if (testMode)
            {
                expiresText = "No expiration in unlocked test mode";
            }

            string content = "Mode: " + (testMode ? "All Users Test Mode" : "Online License") + Environment.NewLine +
                             "User: " + username + Environment.NewLine +
                             "License expires: " + expiresText + Environment.NewLine +
                             "Remaining: " + (testMode ? "Unlimited for testing" : BuildRemainingText(displayExpiresUtc)) + Environment.NewLine +
                             Environment.NewLine +
                             "Contact info:" + Environment.NewLine +
                             App.GetLicenseSupportContactText();

            if (!testMode && licenseExpiresUtc > DateTime.MinValue && seatLeaseExpiresUtc > DateTime.MinValue)
            {
                content += Environment.NewLine + "Seat lease expires: " +
                           seatLeaseExpiresUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                content += Environment.NewLine + Environment.NewLine + "Status: " + statusMessage;
            }

            return content;
        }

        private static string BuildRemainingText(DateTime expiresUtc)
        {
            if (expiresUtc <= DateTime.MinValue)
            {
                return "Unknown";
            }

            TimeSpan remaining = expiresUtc - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                return "Expired";
            }

            double days = remaining.TotalDays;
            double months = days / 30.4375d;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} days (~{1:0.0} months)", days, months);
        }
    }
}
