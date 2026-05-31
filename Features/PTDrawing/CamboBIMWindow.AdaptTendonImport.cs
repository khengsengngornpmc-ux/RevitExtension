using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private static string _adaptLastFolder = LoadAdaptSettingValue("LastFolder");
        private static string _adaptLastProjectPath = LoadAdaptSettingValue("LastProject");
        private static AdaptCadImportMode _adaptCadImportMode = LoadAdaptCadImportMode();
        private static string _adaptShopMarkPrefix = LoadAdaptShopMarkPrefix();
        private static int _adaptShopMarkStartNumber = LoadAdaptShopMarkStartNumber();
        private static int _adaptShopMarkDigits = LoadAdaptShopMarkDigits();
        private static AdaptPtShopMarkSequenceMode _adaptShopMarkSequenceMode = LoadAdaptShopMarkSequenceMode();
        private static bool _adaptPreserveCadShopMarks = LoadAdaptPreserveCadShopMarks();

        private enum AdaptImportWorkflowOption
        {
            DirectAdaptImport,
            CadDrawingImport
        }

        private enum AdaptImportEntryAction
        {
            ImportNewPt,
            RenumberAuditExistingPt
        }

        private sealed class AdaptShopMarkSettings
        {
            public string Prefix { get; set; } = "PT";
            public int StartNumber { get; set; } = 1;
            public int Digits { get; set; } = 3;
            public AdaptPtShopMarkSequenceMode SequenceMode { get; set; } = AdaptPtShopMarkSequenceMode.SourceAndName;
            public bool PreserveCadShopMarks { get; set; } = true;
        }

        private sealed class AdaptShopMarkSettingsDialog : Window
        {
            private readonly TextBox _prefixTextBox;
            private readonly TextBox _startNumberTextBox;
            private readonly TextBox _digitsTextBox;
            private readonly ComboBox _sequenceComboBox;
            private readonly CheckBox _preserveCadMarksCheckBox;

            public AdaptShopMarkSettingsDialog(string title, string message, AdaptShopMarkSettings current, string confirmButtonText = null)
            {
                Title = string.IsNullOrWhiteSpace(title) ? "PT Mark Settings" : title;
                Width = 420;
                Height = 360;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowInTaskbar = false;
                Background = SystemColors.WindowBrush;

                AdaptShopMarkSettings seed = current ?? new AdaptShopMarkSettings();
                var root = new Grid
                {
                    Margin = new Thickness(16)
                };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var messageBlock = new TextBlock
                {
                    Text = message ?? "Choose the PT mark pattern for this import.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                Grid.SetRow(messageBlock, 0);
                root.Children.Add(messageBlock);

                var form = new Grid();
                form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(form, 1);
                root.Children.Add(form);

                AddLabel(form, 0, "Prefix");
                _prefixTextBox = AddTextBox(form, 0, seed.Prefix ?? "PT");

                AddLabel(form, 1, "Start Number");
                _startNumberTextBox = AddTextBox(form, 1, Math.Max(1, seed.StartNumber).ToString(CultureInfo.InvariantCulture));

                AddLabel(form, 2, "Digits");
                _digitsTextBox = AddTextBox(form, 2, Math.Max(1, seed.Digits).ToString(CultureInfo.InvariantCulture));

                AddLabel(form, 3, "Sequence");
                _sequenceComboBox = AddSequenceComboBox(form, 3, seed.SequenceMode);

                var previewBlock = new TextBlock
                {
                    Text = "Example: " + BuildPreview(seed.Prefix, seed.StartNumber, seed.Digits),
                    Margin = new Thickness(0, 12, 0, 0)
                };
                Grid.SetRow(previewBlock, 2);
                root.Children.Add(previewBlock);

                _prefixTextBox.TextChanged += (s, e) => previewBlock.Text = "Example: " + BuildPreview(_prefixTextBox.Text, ParsePositiveIntOrDefault(_startNumberTextBox.Text, 1), ParsePositiveIntOrDefault(_digitsTextBox.Text, 3));
                _startNumberTextBox.TextChanged += (s, e) => previewBlock.Text = "Example: " + BuildPreview(_prefixTextBox.Text, ParsePositiveIntOrDefault(_startNumberTextBox.Text, 1), ParsePositiveIntOrDefault(_digitsTextBox.Text, 3));
                _digitsTextBox.TextChanged += (s, e) => previewBlock.Text = "Example: " + BuildPreview(_prefixTextBox.Text, ParsePositiveIntOrDefault(_startNumberTextBox.Text, 1), ParsePositiveIntOrDefault(_digitsTextBox.Text, 3));

                var noteBlock = new TextBlock
                {
                    Text = "These settings are saved and reused for the next PT import.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                };
                Grid.SetRow(noteBlock, 3);
                root.Children.Add(noteBlock);

                _preserveCadMarksCheckBox = new CheckBox
                {
                    Content = "Preserve CAD text labels as marks when available",
                    IsChecked = seed.PreserveCadShopMarks,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                Grid.SetRow(_preserveCadMarksCheckBox, 4);
                root.Children.Add(_preserveCadMarksCheckBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                Grid.SetRow(buttonPanel, 5);
                root.Children.Add(buttonPanel);

                var okButton = new Button
                {
                    Content = string.IsNullOrWhiteSpace(confirmButtonText) ? "Import" : confirmButtonText.Trim(),
                    Width = 88,
                    IsDefault = true,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                okButton.Click += (s, e) => ConfirmAndClose();
                buttonPanel.Children.Add(okButton);

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 88,
                    IsCancel = true
                };
                buttonPanel.Children.Add(cancelButton);

                Content = root;
            }

            public AdaptShopMarkSettings Settings { get; private set; }

            private void ConfirmAndClose()
            {
                string prefix = NormalizePrefix(_prefixTextBox.Text);
                int startNumber = ParsePositiveIntOrDefault(_startNumberTextBox.Text, 1);
                int digits = ParsePositiveIntOrDefault(_digitsTextBox.Text, 3);
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    MessageBox.Show(this, "Prefix cannot be empty.", "PT Mark Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                    _prefixTextBox.Focus();
                    _prefixTextBox.SelectAll();
                    return;
                }

                Settings = new AdaptShopMarkSettings
                {
                    Prefix = prefix,
                    StartNumber = Math.Max(1, startNumber),
                    Digits = Math.Max(1, Math.Min(6, digits)),
                    SequenceMode = GetSelectedSequenceMode(),
                    PreserveCadShopMarks = _preserveCadMarksCheckBox?.IsChecked != false
                };
                DialogResult = true;
                Close();
            }

            private static void AddLabel(Grid form, int row, string text)
            {
                var label = new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 8, 4)
                };
                Grid.SetColumn(label, 0);
                Grid.SetRow(label, row);
                form.Children.Add(label);
            }

            private static TextBox AddTextBox(Grid form, int row, string value)
            {
                var box = new TextBox
                {
                    Text = value ?? "",
                    Margin = new Thickness(0, 4, 0, 4),
                    MinWidth = 180
                };
                Grid.SetColumn(box, 1);
                Grid.SetRow(box, row);
                form.Children.Add(box);
                return box;
            }

            private static ComboBox AddSequenceComboBox(Grid form, int row, AdaptPtShopMarkSequenceMode selectedMode)
            {
                var box = new ComboBox
                {
                    Margin = new Thickness(0, 4, 0, 4),
                    MinWidth = 180
                };
                box.Items.Add(new ComboBoxItem { Content = "Source / Name", Tag = AdaptPtShopMarkSequenceMode.SourceAndName });
                box.Items.Add(new ComboBoxItem { Content = "Left to Right", Tag = AdaptPtShopMarkSequenceMode.LeftToRight });
                box.Items.Add(new ComboBoxItem { Content = "Bottom to Top", Tag = AdaptPtShopMarkSequenceMode.BottomToTop });
                box.Items.Add(new ComboBoxItem { Content = "Top to Bottom", Tag = AdaptPtShopMarkSequenceMode.TopToBottom });
                box.Items.Add(new ComboBoxItem { Content = "Long to Short", Tag = AdaptPtShopMarkSequenceMode.LongToShort });
                foreach (ComboBoxItem item in box.Items)
                {
                    if (item != null && item.Tag is AdaptPtShopMarkSequenceMode mode && mode == selectedMode)
                    {
                        box.SelectedItem = item;
                        break;
                    }
                }

                if (box.SelectedIndex < 0)
                {
                    box.SelectedIndex = 0;
                }

                Grid.SetColumn(box, 1);
                Grid.SetRow(box, row);
                form.Children.Add(box);
                return box;
            }

            private AdaptPtShopMarkSequenceMode GetSelectedSequenceMode()
            {
                if (_sequenceComboBox?.SelectedItem is ComboBoxItem item &&
                    item.Tag is AdaptPtShopMarkSequenceMode mode)
                {
                    return mode;
                }

                return AdaptPtShopMarkSequenceMode.SourceAndName;
            }

            private static int ParsePositiveIntOrDefault(string text, int fallback)
            {
                return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                    ? parsed
                    : fallback;
            }

            private static string NormalizePrefix(string value)
            {
                string text = Regex.Replace((value ?? "").Trim(), @"[^A-Za-z0-9_\-]+", "");
                return string.IsNullOrWhiteSpace(text) ? "" : text;
            }

            private static string BuildPreview(string prefix, int startNumber, int digits)
            {
                string safePrefix = NormalizePrefix(prefix);
                if (string.IsNullOrWhiteSpace(safePrefix))
                {
                    safePrefix = "PT";
                }

                int safeStart = Math.Max(1, startNumber);
                int safeDigits = Math.Max(1, Math.Min(6, digits));
                return safePrefix + "-" + safeStart.ToString("D" + safeDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
        }

        private void OnCad2ModelTasRibbonImportAdaptClick(object sender, RoutedEventArgs e)
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("ADAPT import is available only inside Revit.");
                return;
            }

            if (!TryChooseAdaptImportEntryAction(out AdaptImportEntryAction entryAction))
            {
                ShowStatus("DRAWING PT: cancelled.");
                return;
            }

            if (entryAction == AdaptImportEntryAction.RenumberAuditExistingPt)
            {
                StartAdaptRenumberAuditWorkflow();
                return;
            }

            if (!TryChooseAdaptImportWorkflow(out AdaptImportWorkflowOption workflow))
            {
                ShowStatus("DRAWING PT: cancelled.");
                return;
            }

            StartAdaptImportWorkflow(workflow);
        }

        private void StartAdaptImportWorkflow(AdaptImportWorkflowOption workflow)
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("ADAPT import is available only inside Revit.");
                return;
            }

            if (workflow == AdaptImportWorkflowOption.CadDrawingImport && TryOfferRecentAdaptProjectCadExport())
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = workflow == AdaptImportWorkflowOption.DirectAdaptImport
                    ? "Direct ADAPT Import"
                    : "Import ADAPT CAD Drawing",
                Filter = workflow == AdaptImportWorkflowOption.DirectAdaptImport
                    ? "ADAPT direct sources (*.adm;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls)|*.adm;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls|ADAPT project files (*.adm)|*.adm|ADAPT table files (*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls)|*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls|All files (*.*)|*.*"
                    : "ADAPT CAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            string initialDirectory = GetAdaptInitialDirectory();
            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            bool? ok = dialog.ShowDialog(this);
            if (ok != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                string selectedPath = dialog.FileName;
                RememberAdaptPath(selectedPath);
                PtDrawingTraceService.WriteTrace(
                    "ImportWorkflowSelection",
                    "DRAWING PT source selected.",
                    selectedPath,
                    new[]
                    {
                        "Workflow=" + workflow.ToString(),
                        "FileName=" + Path.GetFileName(selectedPath)
                    });

                if (workflow == AdaptImportWorkflowOption.CadDrawingImport)
                {
                    if (!PtDrawingSourceService.IsCadDrawingPath(selectedPath))
                    {
                        MessageBox.Show(
                            this,
                            "DWG / DXF Import expects an ADAPT-exported `.dwg` or `.dxf` file.\n\n" +
                            "Use `Direct ADAPT Import` for `.adm` and tendon/profile table sources.",
                            "DRAWING PT",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        ShowStatus("DRAWING PT: DWG / DXF import expects a .dwg or .dxf file.");
                        return;
                    }

                    QueueAdaptCadDrawingImport(selectedPath);
                    return;
                }

                if (PtDrawingSourceService.IsProjectPath(selectedPath))
                {
                    HandleAdaptProjectWithoutLaunchingBuilder(selectedPath);
                    return;
                }

                if (PtDrawingSourceService.IsCadDrawingPath(selectedPath))
                {
                    MessageBox.Show(
                        this,
                        "Direct ADAPT Import is intended for `.adm` files or tendon/profile tables.\n\n" +
                        "Choose `DWG / DXF Import` when the source is an ADAPT-exported CAD drawing.",
                        "DRAWING PT",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    ShowStatus("DRAWING PT: DWG / DXF file selected under Direct ADAPT Import.");
                    return;
                }

                AdaptTendonImportReadResult result = ReadAdaptTendonProfileFile(selectedPath);
                if (result.Segments.Count == 0)
                {
                    ShowStatus("ADAPT direct import: no tendon/profile segments found. Expected X/Y/Z or Station/Elevation columns.");
                    return;
                }

                QueueAdaptTendonProfileImport(selectedPath, result, "ADAPT direct import");
            }
            catch (Exception ex)
            {
                ShowStatus("ADAPT import failed: " + ex.Message);
            }
        }

        internal void StartAdaptImportFromRibbon()
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    OnCad2ModelTasRibbonImportAdaptClick(this, new RoutedEventArgs());
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private bool TryChooseAdaptImportEntryAction(out AdaptImportEntryAction action)
        {
            action = AdaptImportEntryAction.ImportNewPt;

            MessageBoxResult result = MessageBox.Show(
                this,
                "Choose the DRAWING PT action.\n\n" +
                "Yes = Import New PT\n" +
                "No = Renumber / Audit Existing PT\n" +
                "Cancel = stop",
                "DRAWING PT",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            action = result == MessageBoxResult.No
                ? AdaptImportEntryAction.RenumberAuditExistingPt
                : AdaptImportEntryAction.ImportNewPt;
            return true;
        }

        private bool TryChooseAdaptImportWorkflow(out AdaptImportWorkflowOption workflow)
        {
            workflow = AdaptImportWorkflowOption.DirectAdaptImport;

            MessageBoxResult result = MessageBox.Show(
                this,
                "Choose the PT import method for DRAWING PT.\n\n" +
                "Yes = Direct ADAPT Import (recommended)\n" +
                "Create Revit tendon geometry and profile views from ADAPT source data such as `.adm` or tendon/profile tables.\n\n" +
                "No = DWG / DXF Import\n" +
                "Link or import an ADAPT-exported CAD drawing for appearance-based reference.\n\n" +
                "Cancel = stop",
                "DRAWING PT Import Method",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            workflow = result == MessageBoxResult.No
                ? AdaptImportWorkflowOption.CadDrawingImport
                : AdaptImportWorkflowOption.DirectAdaptImport;
            return true;
        }

        private void StartAdaptRenumberAuditWorkflow()
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("DRAWING PT renumber/audit is available only inside Revit.");
                return;
            }

            if (!TryChooseAdaptAuditSource(out string sourcePath, out AdaptImportWorkflowOption workflow, out PtDrawingSnapshotRecord snapshot))
            {
                ShowStatus("DRAWING PT renumber/audit: cancelled.");
                return;
            }

            PtDrawingTraceService.WriteTrace(
                "RenumberAuditSource",
                "Renumber / audit source selected.",
                sourcePath,
                new[]
                {
                    "Workflow=" + workflow.ToString(),
                    "SnapshotFound=" + ((snapshot != null).ToString())
                });

            if (!TryChooseAdaptShopMarkSettings(
                sourcePath,
                out AdaptShopMarkSettings markSettings,
                "PT Renumber / Audit",
                "Choose the PT mark pattern for " + Path.GetFileName(sourcePath) + ".\n\n" +
                "This workflow audits the current PT package and then regenerates it with the updated numbering.",
                "Preview"))
            {
                ShowStatus("DRAWING PT renumber/audit: mark setup cancelled.");
                return;
            }

            PtImportJsonDocument auditDocument = snapshot?.Document;
            if ((auditDocument?.Tendons?.Count ?? 0) == 0)
            {
                auditDocument = TryBuildAdaptAuditDocumentFromSource(sourcePath, workflow);
            }

            if ((auditDocument?.Tendons?.Count ?? 0) > 0)
            {
                PtDrawingAuditPreviewPackage preview = PtDrawingAuditService.BuildPreview(
                    sourcePath,
                    workflow == AdaptImportWorkflowOption.CadDrawingImport,
                    snapshot,
                    auditDocument,
                    markSettings.Prefix,
                    markSettings.StartNumber,
                    markSettings.Digits,
                    markSettings.SequenceMode,
                    markSettings.PreserveCadShopMarks);
                var previewWindow = new MhnkCadToModelPreviewWindow(
                    "DRAWING PT Renumber / Audit",
                    preview.Summary,
                    preview.Items,
                    _revitMainWindowHandle);

                if (previewWindow.ShowDialog() != true)
                {
                    ShowStatus("DRAWING PT renumber/audit: cancelled.");
                    return;
                }
            }
            else
            {
                MessageBoxResult continueWithoutPreview = MessageBox.Show(
                    this,
                    "No saved PT snapshot was available to build an audit preview.\n\n" +
                    "DRAWING PT can still regenerate the PT package with the new mark settings.\n\n" +
                    "Continue?",
                    "DRAWING PT Renumber / Audit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (continueWithoutPreview != MessageBoxResult.Yes)
                {
                    ShowStatus("DRAWING PT renumber/audit: cancelled.");
                    return;
                }
            }

            QueueAdaptAuditRegeneration(sourcePath, workflow, markSettings);
        }

        private void QueueAdaptCadDrawingImport(string path)
        {
            QueueAdaptCadDrawingImport(path, null, null);
        }

        private void QueueAdaptCadDrawingImport(string path, AdaptShopMarkSettings overrideSettings, string statusPrefix)
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("ADAPT CAD link is available only inside Revit.");
                return;
            }

            if (!TryChooseAdaptCadImportMode(path, out AdaptCadImportMode importMode))
            {
                ShowStatus("DRAWING PT: CAD import cancelled.");
                return;
            }

            AdaptShopMarkSettings markSettings = overrideSettings;
            if (markSettings == null && !TryChooseAdaptShopMarkSettings(path, out markSettings))
            {
                ShowStatus("DRAWING PT: PT mark setup cancelled.");
                return;
            }

            RememberAdaptPath(path);
            _handler.Request.AdaptCadSourcePath = path;
            _handler.Request.AdaptCadImportMode = importMode;
            _handler.Request.AdaptTendonSourcePath = "";
            _handler.Request.AdaptShopMarkPrefix = markSettings.Prefix;
            _handler.Request.AdaptShopMarkStartNumber = markSettings.StartNumber;
            _handler.Request.AdaptShopMarkDigits = markSettings.Digits;
            _handler.Request.AdaptShopMarkSequenceMode = markSettings.SequenceMode;
            _handler.Request.AdaptPreserveCadShopMarks = markSettings.PreserveCadShopMarks;
            _handler.Request.AdaptTendonProfileSegments = new List<AdaptTendonProfileSegmentPayload>();
            _handler.Request.RequestType = CadToModelRequestType.ImportAdaptCadDrawing;
            _externalEvent.Raise();
            PtDrawingTraceService.WriteTrace(
                "QueueCadImport",
                "Queued DRAWING PT CAD import.",
                path,
                new[]
                {
                    "CadImportMode=" + importMode.ToString(),
                    "Prefix=" + markSettings.Prefix,
                    "StartNumber=" + markSettings.StartNumber.ToString(CultureInfo.InvariantCulture),
                    "Digits=" + markSettings.Digits.ToString(CultureInfo.InvariantCulture),
                    "SequenceMode=" + markSettings.SequenceMode.ToString(),
                    "PreserveCadMarks=" + markSettings.PreserveCadShopMarks.ToString()
                });

            string statusLead = string.IsNullOrWhiteSpace(statusPrefix) ? "ADAPT CAD" : statusPrefix.Trim();
            ShowStatus(
                statusLead + ": queued " +
                DescribeAdaptCadImportMode(importMode) +
                " for " +
                Path.GetFileName(path) +
                " with PT marks " +
                markSettings.Prefix +
                "-" +
                markSettings.StartNumber.ToString("D" + Math.Max(1, markSettings.Digits).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) +
                " | Sequence: " + DescribeAdaptShopMarkSequenceMode(markSettings.SequenceMode) +
                (markSettings.PreserveCadShopMarks ? " | CAD labels kept." : " | CAD labels renumbered.") +
                ".");
        }

        private void QueueAdaptTendonProfileImport(string path, AdaptTendonImportReadResult result, string statusPrefix)
        {
            QueueAdaptTendonProfileImport(path, result, statusPrefix, null);
        }

        private void QueueAdaptTendonProfileImport(string path, AdaptTendonImportReadResult result, string statusPrefix, AdaptShopMarkSettings overrideSettings)
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("ADAPT import is available only inside Revit.");
                return;
            }

            if (result == null || result.Segments.Count == 0)
            {
                ShowStatus("ADAPT import: no tendon/profile segments found.");
                return;
            }

            AdaptShopMarkSettings markSettings = overrideSettings;
            if (markSettings == null && !TryChooseAdaptShopMarkSettings(path, out markSettings))
            {
                ShowStatus("DRAWING PT: PT mark setup cancelled.");
                return;
            }

            // This is the handoff point from file parsing into the Revit-side creation pipeline.
            _handler.Request.AdaptTendonSourcePath = path;
            _handler.Request.AdaptTendonImportMode = result.Mode;
            _handler.Request.AdaptTendonProfileSegments = result.Segments;
            _handler.Request.AdaptCadSourcePath = "";
            _handler.Request.AdaptShopMarkPrefix = markSettings.Prefix;
            _handler.Request.AdaptShopMarkStartNumber = markSettings.StartNumber;
            _handler.Request.AdaptShopMarkDigits = markSettings.Digits;
            _handler.Request.AdaptShopMarkSequenceMode = markSettings.SequenceMode;
            _handler.Request.AdaptPreserveCadShopMarks = markSettings.PreserveCadShopMarks;
            _handler.Request.RequestType = CadToModelRequestType.ImportAdaptTendonProfiles;
            _externalEvent.Raise();
            PtDrawingTraceService.WriteTrace(
                "QueueDirectImport",
                "Queued DRAWING PT direct tendon import.",
                path,
                new[]
                {
                    "ImportMode=" + result.Mode.ToString(),
                    "SegmentCount=" + result.Segments.Count.ToString(CultureInfo.InvariantCulture),
                    "ProfileCount=" + result.ProfileCount.ToString(CultureInfo.InvariantCulture),
                    "PointCount=" + result.PointCount.ToString(CultureInfo.InvariantCulture),
                    "Prefix=" + markSettings.Prefix,
                    "StartNumber=" + markSettings.StartNumber.ToString(CultureInfo.InvariantCulture),
                    "Digits=" + markSettings.Digits.ToString(CultureInfo.InvariantCulture),
                    "SequenceMode=" + markSettings.SequenceMode.ToString()
                });

            string modeText = PtDrawingSourceService.IsProjectPath(path)
                ? "3D tendon profile segments"
                : (result.Mode == AdaptTendonImportMode.Model3D ? "3D tendon solids and centerlines" : "profile elements and drafting profile views");
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ADAPT import" : statusPrefix.Trim();
            ShowStatus(
                prefix + ": queued " +
                result.Segments.Count.ToString(CultureInfo.InvariantCulture) +
                " segment(s) from " + Path.GetFileName(path) +
                " as " + modeText +
                " (profiles: " + result.ProfileCount.ToString(CultureInfo.InvariantCulture) +
                ", points: " + result.PointCount.ToString(CultureInfo.InvariantCulture) +
                ", units: " + PtDrawingTableRowParserService.DescribeUnit(result.InferredUnit) +
                ", marks: " + markSettings.Prefix +
                ", sequence: " + DescribeAdaptShopMarkSequenceMode(markSettings.SequenceMode) + ").");
        }

        private bool TryChooseAdaptCadImportMode(string path, out AdaptCadImportMode importMode)
        {
            importMode = _adaptCadImportMode;
            string fileName = Path.GetFileName(path);
            string currentMode = DescribeAdaptCadImportMode(_adaptCadImportMode);
            MessageBoxResult result = MessageBox.Show(
                this,
                "How should DRAWING PT bring this ADAPT CAD export into Revit?\n\n" +
                fileName +
                "\n\nYes = Link DWG/DXF (recommended, keeps source external)\nNo = Import DWG/DXF into the model\nCancel = stop\n\nCurrent saved preference: " + currentMode + ".",
                "DRAWING PT CAD Mode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                _adaptCadImportMode == AdaptCadImportMode.ImportOnly ? MessageBoxResult.No : MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            importMode = result == MessageBoxResult.No
                ? AdaptCadImportMode.ImportOnly
                : AdaptCadImportMode.LinkPreferred;
            _adaptCadImportMode = importMode;
            SaveAdaptSettings();
            return true;
        }

        private bool TryOfferRecentAdaptProjectCadExport()
        {
            EnsureAdaptSettingsLoaded();
            if (string.IsNullOrWhiteSpace(_adaptLastProjectPath) ||
                !File.Exists(_adaptLastProjectPath) ||
                !PtDrawingSourceService.TryFindLatestCadExport(_adaptLastProjectPath, out string exportPath))
            {
                return false;
            }

            FileInfo exportInfo = new FileInfo(exportPath);
            string projectName = Path.GetFileName(_adaptLastProjectPath);
            string exportFreshness = PtDrawingSourceService.BuildExportFreshnessNote(_adaptLastProjectPath, exportInfo);
            MessageBoxResult result = MessageBox.Show(
                this,
                "Use the latest CAD export from the previous ADAPT project?\n\n" +
                "Project: " + projectName +
                "\nExport: " + Path.GetFileName(exportPath) +
                "\nModified: " + exportInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                exportFreshness +
                "\n\nYes = import this export\nNo = choose another ADAPT file\nCancel = stop",
                "DRAWING PT",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                QueueAdaptCadDrawingImport(exportPath);
                return true;
            }

            if (result == MessageBoxResult.Cancel)
            {
                ShowStatus("DRAWING PT: cancelled.");
                return true;
            }

            return false;
        }

        private void HandleAdaptProjectWithoutLaunchingBuilder(string path)
        {
            string fileName = Path.GetFileName(path);
            try
            {
                AdaptTendonImportReadResult result = ReadAdaptAdmTendonGeometry(path);
                if (result.Segments.Count > 0)
                {
                    QueueAdaptTendonProfileImport(path, result, "ADAPT ADM direct import");
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowStatus("ADAPT ADM direct import did not find usable tendon geometry: " + ex.Message);
            }

            if (PtDrawingSourceService.TryFindLatestCadExport(path, out string exportPath))
            {
                FileInfo exportInfo = new FileInfo(exportPath);
                string exportFreshness = PtDrawingSourceService.BuildExportFreshnessNote(path, exportInfo);
                MessageBoxResult importExisting = MessageBox.Show(
                    this,
                    "Direct .adm import did not find usable tendon geometry, but a CAD export was found near this ADAPT project:\n\n" +
                    Path.GetFileName(exportPath) +
                    "\nModified: " + exportInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                    exportFreshness +
                    "\n\nImport this DWG/DXF into Revit now?",
                    "Import ADAPT CAD Export",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (importExisting == MessageBoxResult.Yes)
                {
                    QueueAdaptCadDrawingImport(exportPath);
                    return;
                }
            }

            MessageBox.Show(
                this,
                "DRAWING PT does not open ADAPT-Builder.\n\n" +
                "Direct .adm import did not find usable tendon geometry and no nearby DWG/DXF export was found for:\n" +
                fileName +
                "\n\nSelect an ADAPT-exported DWG/DXF or tendon/profile table instead.",
                "DRAWING PT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ShowStatus("DRAWING PT: no direct .adm tendon geometry or nearby DWG/DXF export found for " + fileName + ".");
        }

        private bool TryChooseAdaptShopMarkSettings(string path, out AdaptShopMarkSettings settings)
        {
            return TryChooseAdaptShopMarkSettings(path, out settings, null, null, null);
        }

        private bool TryChooseAdaptShopMarkSettings(
            string path,
            out AdaptShopMarkSettings settings,
            string title,
            string message,
            string confirmButtonText)
        {
            EnsureAdaptSettingsLoaded();
            var current = new AdaptShopMarkSettings
            {
                Prefix = string.IsNullOrWhiteSpace(_adaptShopMarkPrefix) ? "PT" : _adaptShopMarkPrefix,
                StartNumber = Math.Max(1, _adaptShopMarkStartNumber),
                Digits = Math.Max(1, _adaptShopMarkDigits),
                SequenceMode = _adaptShopMarkSequenceMode,
                PreserveCadShopMarks = _adaptPreserveCadShopMarks
            };

            var dialog = new AdaptShopMarkSettingsDialog(
                string.IsNullOrWhiteSpace(title) ? "PT Mark Settings" : title.Trim(),
                string.IsNullOrWhiteSpace(message)
                    ? "Choose the PT mark pattern for " + Path.GetFileName(path) + ".\n\nThese marks will be used across views, takeoff, and sheets for this import."
                    : message,
                current,
                confirmButtonText)
            {
                Owner = this
            };

            bool? result = dialog.ShowDialog();
            if (result != true || dialog.Settings == null)
            {
                settings = null;
                return false;
            }

            settings = dialog.Settings;
            _adaptShopMarkPrefix = settings.Prefix;
            _adaptShopMarkStartNumber = Math.Max(1, settings.StartNumber);
            _adaptShopMarkDigits = Math.Max(1, settings.Digits);
            _adaptShopMarkSequenceMode = settings.SequenceMode;
            _adaptPreserveCadShopMarks = settings.PreserveCadShopMarks;
            SaveAdaptSettings();
            return true;
        }

        private bool TryChooseAdaptAuditSource(
            out string sourcePath,
            out AdaptImportWorkflowOption workflow,
            out PtDrawingSnapshotRecord snapshot)
        {
            sourcePath = "";
            workflow = AdaptImportWorkflowOption.DirectAdaptImport;
            snapshot = null;

            List<PtDrawingSnapshotRecord> snapshots = PtDrawingSnapshotStore.LoadSnapshots();
            PtDrawingSnapshotRecord latestSnapshot = snapshots
                .Where(item => item != null)
                .OrderByDescending(item => item.SnapshotTimeLocal)
                .FirstOrDefault();

            if (latestSnapshot != null)
            {
                string latestSourcePath = latestSnapshot.SourcePath;
                string sourceState = File.Exists(latestSourcePath) ? "available" : "missing";
                MessageBoxResult useLatest = MessageBox.Show(
                    this,
                    "Use the latest PT snapshot for renumber / audit?\n\n" +
                    "Source: " + latestSnapshot.DisplayName +
                    "\nMethod: " + (latestSnapshot.IsCadWorkflow ? "DWG / DXF" : "Direct ADAPT") +
                    "\nSnapshot: " + latestSnapshot.SnapshotTimeLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                    "\nOriginal source: " + sourceState +
                    "\n\nYes = use this PT source\nNo = choose another PT source file\nCancel = stop",
                    "DRAWING PT Renumber / Audit",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (useLatest == MessageBoxResult.Cancel)
                {
                    return false;
                }

                if (useLatest == MessageBoxResult.Yes)
                {
                    if (File.Exists(latestSourcePath))
                    {
                        sourcePath = latestSourcePath;
                        workflow = latestSnapshot.IsCadWorkflow
                            ? AdaptImportWorkflowOption.CadDrawingImport
                            : AdaptImportWorkflowOption.DirectAdaptImport;
                        snapshot = latestSnapshot;
                        RememberAdaptPath(sourcePath);
                        return true;
                    }

                    MessageBox.Show(
                        this,
                        "The original source file from the latest PT snapshot is no longer available.\n\n" +
                        "Choose the replacement PT source file to continue the renumber / audit run.",
                        "DRAWING PT Renumber / Audit",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }

            if (!TryChooseAdaptAuditSourceFile(out sourcePath, out workflow))
            {
                return false;
            }

            snapshot = PtDrawingSnapshotStore.FindLatestSnapshotForSourcePath(sourcePath, snapshots);
            RememberAdaptPath(sourcePath);
            return true;
        }

        private bool TryChooseAdaptAuditSourceFile(out string sourcePath, out AdaptImportWorkflowOption workflow)
        {
            sourcePath = "";
            workflow = AdaptImportWorkflowOption.DirectAdaptImport;

            var dialog = new OpenFileDialog
            {
                Title = "Choose PT Source for Renumber / Audit",
                Filter =
                    "PT sources (*.adm;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls;*.dwg;*.dxf)|*.adm;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls;*.dwg;*.dxf|" +
                    "ADAPT direct sources (*.adm;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls)|*.adm;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls|" +
                    "ADAPT CAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf|" +
                    "All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            string initialDirectory = GetAdaptInitialDirectory();
            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            bool? ok = dialog.ShowDialog(this);
            if (ok != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return false;
            }

            sourcePath = dialog.FileName;
            workflow = PtDrawingSourceService.IsCadDrawingPath(sourcePath)
                ? AdaptImportWorkflowOption.CadDrawingImport
                : AdaptImportWorkflowOption.DirectAdaptImport;
            return true;
        }

        private bool QueueAdaptAuditRegeneration(
            string sourcePath,
            AdaptImportWorkflowOption workflow,
            AdaptShopMarkSettings markSettings)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                ShowStatus("DRAWING PT renumber/audit: source path is empty.");
                return false;
            }

            try
            {
                RememberAdaptPath(sourcePath);

                if (workflow == AdaptImportWorkflowOption.CadDrawingImport)
                {
                    QueueAdaptCadDrawingImport(sourcePath, markSettings, "DRAWING PT renumber/audit");
                    return true;
                }

                if (PtDrawingSourceService.IsProjectPath(sourcePath))
                {
                    try
                    {
                        AdaptTendonImportReadResult result = ReadAdaptAdmTendonGeometry(sourcePath);
                        if (result?.Segments?.Count > 0)
                        {
                            QueueAdaptTendonProfileImport(sourcePath, result, "DRAWING PT renumber/audit", markSettings);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowStatus("DRAWING PT renumber/audit: direct ADM read did not find usable tendon geometry: " + ex.Message);
                    }

                    if (PtDrawingSourceService.TryFindLatestCadExport(sourcePath, out string exportPath))
                    {
                        FileInfo exportInfo = new FileInfo(exportPath);
                        string exportFreshness = PtDrawingSourceService.BuildExportFreshnessNote(sourcePath, exportInfo);
                        MessageBoxResult useCadFallback = MessageBox.Show(
                            this,
                            "Direct .adm tendon geometry was not available for this renumber / audit run, but a nearby ADAPT CAD export was found:\n\n" +
                            Path.GetFileName(exportPath) +
                            "\nModified: " + exportInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                            exportFreshness +
                            "\n\nUse this DWG / DXF fallback now?",
                            "DRAWING PT Renumber / Audit",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question,
                            MessageBoxResult.Yes);

                        if (useCadFallback == MessageBoxResult.Yes)
                        {
                            QueueAdaptCadDrawingImport(exportPath, markSettings, "DRAWING PT renumber/audit");
                            return true;
                        }
                    }

                    ShowStatus("DRAWING PT renumber/audit: no direct ADM tendon geometry or CAD fallback export was available.");
                    return false;
                }

                AdaptTendonImportReadResult fileResult = ReadAdaptTendonProfileFile(sourcePath);
                if (fileResult.Segments.Count == 0)
                {
                    ShowStatus("DRAWING PT renumber/audit: no tendon/profile segments found. Expected X/Y/Z or Station/Elevation columns.");
                    return false;
                }

                QueueAdaptTendonProfileImport(sourcePath, fileResult, "DRAWING PT renumber/audit", markSettings);
                return true;
            }
            catch (Exception ex)
            {
                ShowStatus("DRAWING PT renumber/audit failed: " + ex.Message);
                return false;
            }
        }

        private static void RememberAdaptPath(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    _adaptLastFolder = directory;
                }

                if (PtDrawingSourceService.IsProjectPath(path))
                {
                    _adaptLastProjectPath = path;
                }

                SaveAdaptSettings();
            }
            catch
            {
            }
        }

        private static string GetAdaptInitialDirectory()
        {
            EnsureAdaptSettingsLoaded();
            if (!string.IsNullOrWhiteSpace(_adaptLastFolder) && Directory.Exists(_adaptLastFolder))
            {
                return _adaptLastFolder;
            }

            return "";
        }

        private static void EnsureAdaptSettingsLoaded()
        {
            if (string.IsNullOrWhiteSpace(_adaptLastFolder))
            {
                _adaptLastFolder = LoadAdaptSettingValue("LastFolder");
            }

            if (string.IsNullOrWhiteSpace(_adaptLastProjectPath))
            {
                _adaptLastProjectPath = LoadAdaptSettingValue("LastProject");
            }

            _adaptCadImportMode = LoadAdaptCadImportMode();
            _adaptShopMarkPrefix = LoadAdaptShopMarkPrefix();
            _adaptShopMarkStartNumber = LoadAdaptShopMarkStartNumber();
            _adaptShopMarkDigits = LoadAdaptShopMarkDigits();
            _adaptShopMarkSequenceMode = LoadAdaptShopMarkSequenceMode();
            _adaptPreserveCadShopMarks = LoadAdaptPreserveCadShopMarks();
        }

        private static AdaptCadImportMode LoadAdaptCadImportMode()
        {
            string value = LoadAdaptSettingValue("CadImportMode");
            return string.Equals(value, "ImportOnly", StringComparison.OrdinalIgnoreCase)
                ? AdaptCadImportMode.ImportOnly
                : AdaptCadImportMode.LinkPreferred;
        }

        private static string LoadAdaptShopMarkPrefix()
        {
            string value = LoadAdaptSettingValue("ShopMarkPrefix");
            string normalized = Regex.Replace((value ?? "").Trim(), @"[^A-Za-z0-9_\-]+", "");
            return string.IsNullOrWhiteSpace(normalized) ? "PT" : normalized;
        }

        private static int LoadAdaptShopMarkStartNumber()
        {
            string value = LoadAdaptSettingValue("ShopMarkStartNumber");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : 1;
        }

        private static int LoadAdaptShopMarkDigits()
        {
            string value = LoadAdaptSettingValue("ShopMarkDigits");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? Math.Min(6, parsed)
                : 3;
        }

        private static AdaptPtShopMarkSequenceMode LoadAdaptShopMarkSequenceMode()
        {
            string value = LoadAdaptSettingValue("ShopMarkSequenceMode");
            if (Enum.TryParse(value, true, out AdaptPtShopMarkSequenceMode mode))
            {
                return mode;
            }

            return AdaptPtShopMarkSequenceMode.SourceAndName;
        }

        private static bool LoadAdaptPreserveCadShopMarks()
        {
            string value = LoadAdaptSettingValue("PreserveCadShopMarks");
            return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static string LoadAdaptSettingValue(string key)
        {
            return PtDrawingSettingsStore.LoadValue(key);
        }

        private static void SaveAdaptSettings()
        {
            PtDrawingSettingsStore.SaveValues(
                _adaptLastFolder,
                _adaptLastProjectPath,
                _adaptCadImportMode,
                _adaptShopMarkPrefix,
                _adaptShopMarkStartNumber,
                _adaptShopMarkDigits,
                _adaptShopMarkSequenceMode,
                _adaptPreserveCadShopMarks);
        }

        private static PtImportJsonDocument TryBuildAdaptAuditDocumentFromSource(
            string sourcePath,
            AdaptImportWorkflowOption workflow)
        {
            return PtDrawingSourceService.TryBuildAuditDocumentFromSource(
                sourcePath,
                workflow == AdaptImportWorkflowOption.CadDrawingImport,
                ReadAdaptTendonProfileFile);
        }

        private static string DescribeAdaptCadImportMode(AdaptCadImportMode mode)
        {
            return mode == AdaptCadImportMode.ImportOnly ? "import into model" : "link preferred";
        }

        private static string DescribeAdaptShopMarkSequenceMode(AdaptPtShopMarkSequenceMode mode)
        {
            switch (mode)
            {
                case AdaptPtShopMarkSequenceMode.LeftToRight:
                    return "Left to Right";
                case AdaptPtShopMarkSequenceMode.BottomToTop:
                    return "Bottom to Top";
                case AdaptPtShopMarkSequenceMode.TopToBottom:
                    return "Top to Bottom";
                case AdaptPtShopMarkSequenceMode.LongToShort:
                    return "Long to Short";
                default:
                    return "Source / Name";
            }
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileFile(string path)
        {
            return PtDrawingSourceService.ReadTendonProfileFile(
                path,
                ReadAdaptAdmTendonGeometry,
                ReadAdaptTendonProfileExcel,
                ReadAdaptTendonProfileText);
        }

        private static AdaptTendonImportReadResult ReadAdaptAdmTendonGeometry(string path)
        {
            return PtDrawingAdmReaderService.ReadGeometry(path);
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileText(string path)
        {
            return PtDrawingTableReaderService.ReadText(
                path,
                line => PtDrawingTableRowParserService.SplitDelimitedLine(line, ParseCsvLine),
                ReadAdaptTendonProfileRows);
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileExcel(string path)
        {
            return PtDrawingTableReaderService.ReadExcel(
                path,
                NormalizeExcelRangeToMatrix,
                ToCellString,
                ReadAdaptTendonProfileRows,
                SafeReleaseCom,
                PtDrawingTableRowParserService.BuildSegmentGroupKey);
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileRows(IReadOnlyList<string[]> rows)
        {
            return PtDrawingTableRowParserService.ReadRows(
                rows,
                NormalizeImportedHeaderCell,
                TryParseDouble);
        }
    }
}
