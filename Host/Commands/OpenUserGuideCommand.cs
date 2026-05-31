using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using RevitDB = Autodesk.Revit.DB;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class OpenUserGuideCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, RevitDB.ElementSet elements)
        {
            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string guidePath = Path.Combine(assemblyDirectory, "docs", "CAMBOBIM_USER_GUIDE.html");

            if (!File.Exists(guidePath))
            {
                Autodesk.Revit.UI.TaskDialog.Show(
                    "MHNK User Guide",
                    "Guide file not found." + Environment.NewLine +
                    "Expected path:" + Environment.NewLine +
                    guidePath + Environment.NewLine + Environment.NewLine +
                    "Please redeploy the latest add-in package.");
                return Result.Cancelled;
            }

            using (var form = new UserGuideForm(guidePath))
            {
                form.ShowDialog();
            }

            return Result.Succeeded;
        }

        private sealed class UserGuideForm : System.Windows.Forms.Form
        {
            private readonly string _guidePath;
            private readonly WebBrowser _browser;

            public UserGuideForm(string guidePath)
            {
                _guidePath = guidePath;

                Text = "MHNK User Guide";
                Width = 1150;
                Height = 760;
                MinimumSize = new Size(920, 620);
                StartPosition = FormStartPosition.CenterScreen;

                var toolbar = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 44,
                    BackColor = Color.FromArgb(240, 240, 240)
                };

                var openInBrowserButton = new Button
                {
                    Text = "Open in Browser",
                    Width = 130,
                    Height = 28,
                    Left = 10,
                    Top = 8
                };
                openInBrowserButton.Click += (s, e) => OpenInBrowser();

                var refreshButton = new Button
                {
                    Text = "Refresh",
                    Width = 90,
                    Height = 28,
                    Left = 148,
                    Top = 8
                };
                refreshButton.Click += (s, e) => _browser.Refresh();

                var closeButton = new Button
                {
                    Text = "Close",
                    Width = 90,
                    Height = 28,
                    Left = 246,
                    Top = 8
                };
                closeButton.Click += (s, e) => Close();

                toolbar.Controls.Add(openInBrowserButton);
                toolbar.Controls.Add(refreshButton);
                toolbar.Controls.Add(closeButton);

                _browser = new WebBrowser
                {
                    Dock = DockStyle.Fill,
                    ScriptErrorsSuppressed = true,
                    AllowWebBrowserDrop = false,
                    IsWebBrowserContextMenuEnabled = true,
                    WebBrowserShortcutsEnabled = true
                };

                Controls.Add(_browser);
                Controls.Add(toolbar);

                Load += (s, e) => _browser.Navigate(new Uri(_guidePath));
            }

            private void OpenInBrowser()
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _guidePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("MHNK User Guide", "Cannot open browser." + Environment.NewLine + ex.Message);
                }
            }
        }
    }
}
