using System;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class CamboBIMWindowCommand : IExternalCommand
    {
        private static CamboBIMWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            return ShowWindow(commandData, ref message, null, null, false);
        }

        internal static Result ShowWindow(
            ExternalCommandData commandData,
            ref string message,
            string mainTabHeader,
            string qsSubTabHeader,
            bool focusedMainTabOnly)
        {
            string autodeskUserId = commandData?.Application?.Application?.LoginUserId ?? "";
            App.SetAutodeskLoginUserId(autodeskUserId);

            if (!App.EnsureLicenseActivated(out string licenseFailure, autodeskUserId))
            {
                TaskDialog.Show("MHNK License", licenseFailure);
                return Result.Cancelled;
            }

            try
            {
                string effectiveMainTabHeader = NormalizeMainTabHeader(mainTabHeader);
                bool forceCad2ModelFocusedMode =
                    string.Equals(mainTabHeader?.Trim(), "CAD2MODEL", StringComparison.OrdinalIgnoreCase);
                bool effectiveFocusedMainTabOnly = focusedMainTabOnly || forceCad2ModelFocusedMode;

                bool isNewWindow = _window == null || !_window.IsVisible;
                if (isNewWindow)
                {
                    IntPtr revitHandle = commandData.Application.MainWindowHandle;
                    _window = new CamboBIMWindow(revitHandle);
                    _window.Closed += (_, __) => _window = null;
                }

                // Apply tab visibility/selection before first show to avoid flash of all tabs.
                _window.SetMainTabMode(effectiveMainTabHeader, effectiveFocusedMainTabOnly);
                _window.SelectTab(effectiveMainTabHeader, qsSubTabHeader);

                if (isNewWindow)
                {
                    _window.Show();
                }

                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }

                _window.Activate();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MHNK", $"Failed to open window:\n{ex}");
                return Result.Failed;
            }
        }

        private static string NormalizeMainTabHeader(string mainTabHeader)
        {
            return string.Equals(mainTabHeader?.Trim(), "CAD2MODEL", StringComparison.OrdinalIgnoreCase)
                ? "IDENTIFY"
                : mainTabHeader;
        }
    }
}
