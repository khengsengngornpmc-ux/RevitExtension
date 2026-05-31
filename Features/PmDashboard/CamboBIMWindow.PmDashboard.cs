using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.ComponentModel;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Media3D = System.Windows.Media.Media3D;
using WpfColor = System.Windows.Media.Color;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {

        private void OnExportMsProjectPackageClick(object sender, RoutedEventArgs e)
        {
            string folder = ResolveMsProjectSyncFolder();
            bool ok = TryExportMsProjectSyncPackage(folder, out string summary);
            SetPowerBiExportSummary(summary);
            ShowStatus(summary);
            if (!ok)
            {
                return;
            }

            UpdatePowerBiRuntimeInfo();
            RefreshPmDashboardProjectOptions();
            RefreshPmDashboardSummaryAndChart();
        }

        private void OnImportMsProjectProgressClick(object sender, RoutedEventArgs e)
        {
            string initialFolder = ResolveMsProjectSyncFolder();
            var dialog = new OpenFileDialog
            {
                Title = "Import MS Project Progress CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (Directory.Exists(initialFolder))
            {
                dialog.InitialDirectory = initialFolder;
            }

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                List<SiteProgressImportRowPayload> rows = ReadMsProjectProgressRowsFromCsv(
                    dialog.FileName,
                    out int parsedRows,
                    out int skippedRows);

                if (rows.Count == 0)
                {
                    ShowStatus("MS Project import: no valid progress rows found.");
                    return;
                }

                _handler.Request.SiteProgress.ImportRows = rows;
                _handler.Request.SiteProgress.Scope = QsScope.EntireModel;
                _handler.Request.SiteProgress.StructureFilter = SiteProgressFilterAll;
                _handler.Request.SiteProgress.BuildingLevelFilter = SiteProgressFilterAll;
                _handler.Request.RequestType = CadToModelRequestType.ApplySiteProgressFromImport;
                _externalEvent.Raise();

                string message = "MS Project progress import queued: " +
                                 rows.Count.ToString(CultureInfo.InvariantCulture) +
                                 " row(s), parsed=" +
                                 parsedRows.ToString(CultureInfo.InvariantCulture) +
                                 ", skipped=" +
                                 skippedRows.ToString(CultureInfo.InvariantCulture) + ".";
                SetPowerBiExportSummary(message);
                ShowStatus(message);
            }
            catch (Exception ex)
            {
                string message = "MS Project import failed: " + ex.Message;
                SetPowerBiExportSummary(message);
                ShowStatus(message);
            }
        }

        private void OnOpenMsProjectDashboardClick(object sender, RoutedEventArgs e)
        {
            string dashboardPath = ResolveMsProjectDashboardPath(out string expectedPath);
            if (string.IsNullOrWhiteSpace(dashboardPath))
            {
                ShowStatus("MS Project dashboard not found. Expected: " + expectedPath);
                return;
            }

            string workingDir = Path.GetDirectoryName(dashboardPath) ?? "";
            string syncFolder = ResolveMsProjectSyncFolder();

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = dashboardPath,
                    UseShellExecute = true
                };

                if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
                {
                    info.WorkingDirectory = workingDir;
                }

                if (Directory.Exists(syncFolder))
                {
                    info.Arguments = "\"" + syncFolder + "\"";
                }

                Process.Start(info);
                ShowStatus("Opened CamboBIM Dashboard.");
            }
            catch (Exception ex)
            {
                ShowStatus("Failed to open CamboBIM Dashboard: " + ex.Message);
            }
        }

        private string ResolveMsProjectSyncFolder()
        {
            string databaseFolder = GetDatabaseFolder();
            if (string.IsNullOrWhiteSpace(databaseFolder))
            {
                return "";
            }

            return Path.Combine(databaseFolder, DefaultMsProjectSyncFolderName);
        }

        private string ResolveMsProjectDashboardPath(out string expectedPath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(DefaultMsProjectDashboardAbsolutePath))
            {
                candidates.Add(DefaultMsProjectDashboardAbsolutePath);
            }

            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                candidates.Add(Path.Combine(baseDir, "PmDashboard", "CamboBIM_Dashboard.exe"));
                candidates.Add(Path.Combine(baseDir, "PmDashboard", "win-x64", "CamboBIM_Dashboard.exe"));
                candidates.Add(Path.Combine(baseDir, DefaultMsProjectDashboardRelativePath));
                candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", DefaultMsProjectDashboardRelativePath)));
                candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "MSP_Extension", "CamboBIM_Dashboard", "bin", "x64", "Release", "CamboBIM_Dashboard.exe")));
            }

            expectedPath = DefaultMsProjectDashboardAbsolutePath;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate);
                }
                catch
                {
                    fullPath = candidate;
                }

                if (!seen.Add(fullPath))
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return "";
        }

        private bool TryExportMsProjectSyncPackage(string folderPath, out string summary)
        {
            summary = "";
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    summary = "MS Project export failed: sync folder is not available.";
                    return false;
                }

                Directory.CreateDirectory(folderPath);

                DateTime snapshotUtc = DateTime.UtcNow;
                TryGetActiveMsProjectContext(out MsProjectActiveContext activeMsProjectContext);
                List<BoqTableRow> boqRows = (_boqAllRows ?? new List<BoqTableRow>())
                    .Where(r => r != null)
                    .Select(CloneBoqTableRowShallow)
                    .ToList();
                if (boqRows.Count == 0)
                {
                    boqRows = (_boqRows ?? new ObservableCollection<BoqTableRow>())
                        .Where(r => r != null)
                        .Select(CloneBoqTableRowShallow)
                        .ToList();
                }

                List<SiteProgressSummaryRow> summaryRows = (_siteProgressCombinedSourceRows ?? new List<SiteProgressSummaryRow>())
                    .Where(r => r != null)
                    .Select(CloneSiteProgressRowShallow)
                    .ToList();
                List<SiteProgressElementDetailRow> elementRows = (_siteProgressElementRows ?? new List<SiteProgressElementDetailRow>())
                    .Where(r => r != null)
                    .Select(CloneSiteProgressElementRowShallow)
                    .ToList();

                DataTable boqTable = BuildPowerBiQsBoqTable(boqRows, summaryRows, elementRows);
                DataTable summaryTable = BuildPowerBiSiteProgressSummaryFact(snapshotUtc, summaryRows);
                DataTable elementTable = BuildPowerBiSiteProgressElementFact(snapshotUtc, elementRows);
                DataTable importTemplate = BuildMsProjectProgressTemplateTable(boqTable);
                DataTable manifest = BuildMsProjectSyncManifestTable(
                    snapshotUtc,
                    boqTable.Rows.Count,
                    summaryTable.Rows.Count,
                    elementTable.Rows.Count,
                    importTemplate.Rows.Count,
                    activeMsProjectContext?.ProjectName ?? "",
                    activeMsProjectContext?.ProjectPath ?? "",
                    activeMsProjectContext?.DataDateLocal,
                    activeMsProjectContext?.DataDateSource ?? "");

                var files = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase)
                {
                    ["msp_revit_boq.csv"] = boqTable,
                    ["msp_revit_site_progress_summary.csv"] = summaryTable,
                    ["msp_revit_site_progress_element.csv"] = elementTable,
                    ["msp_progress_import_template.csv"] = importTemplate,
                    [MsProjectManifestFileName] = manifest
                };

                int totalRows = 0;
                foreach (KeyValuePair<string, DataTable> file in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string path = Path.Combine(folderPath, file.Key);
                    WriteDataTableToCsv(file.Value, path);
                    totalRows += file.Value?.Rows?.Count ?? 0;
                }

                summary = "MS Project sync package exported: " +
                          files.Count.ToString(CultureInfo.InvariantCulture) +
                          " file(s), " +
                          totalRows.ToString(CultureInfo.InvariantCulture) +
                          " row(s). Folder: " +
                          folderPath;

                if (activeMsProjectContext != null)
                {
                    if (!string.IsNullOrWhiteSpace(activeMsProjectContext.ProjectName))
                    {
                        summary += " Active MS Project: " + activeMsProjectContext.ProjectName + ".";
                    }

                    if (activeMsProjectContext.DataDateLocal.HasValue)
                    {
                        summary += " Data Date: " +
                                   activeMsProjectContext.DataDateLocal.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) +
                                   ".";
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                summary = "MS Project export failed: " + ex.Message;
                return false;
            }
        }

        private static DataTable BuildMsProjectProgressTemplateTable(DataTable boqTable)
        {
            var table = new DataTable("msp_progress_import_template");
            table.Columns.Add("task_key", typeof(string));
            table.Columns.Add("structure_element", typeof(string));
            table.Columns.Add("building_level", typeof(string));
            table.Columns.Add("type", typeof(string));
            table.Columns.Add("progress_reinforcement_pct", typeof(double));
            table.Columns.Add("progress_formwork_pct", typeof(double));
            table.Columns.Add("progress_volume_pct", typeof(double));
            table.Columns.Add("status", typeof(string));
            table.Columns.Add("notes", typeof(string));

            if (boqTable == null || boqTable.Rows.Count == 0)
            {
                return table;
            }

            foreach (DataRow row in boqTable.Rows)
            {
                if (row == null || row.RowState == DataRowState.Deleted)
                {
                    continue;
                }

                string structure = Convert.ToString(row["structure_element"], CultureInfo.InvariantCulture)?.Trim() ?? "";
                string level = Convert.ToString(row["building_level"], CultureInfo.InvariantCulture)?.Trim() ?? "";
                string type = Convert.ToString(row["type"], CultureInfo.InvariantCulture)?.Trim() ?? "";

                double rebarRatio = ConvertToDoubleSafe(row["progress_reinforcement_ratio"]);
                double formworkRatio = ConvertToDoubleSafe(row["progress_formwork_ratio"]);
                double volumeRatio = ConvertToDoubleSafe(row["progress_volume_ratio"]);

                DataRow output = table.NewRow();
                output["task_key"] = BuildBoqKey(structure, level, type);
                output["structure_element"] = structure;
                output["building_level"] = level;
                output["type"] = type;
                output["progress_reinforcement_pct"] = rebarRatio * 100.0;
                output["progress_formwork_pct"] = formworkRatio * 100.0;
                output["progress_volume_pct"] = volumeRatio * 100.0;
                output["status"] = Convert.ToString(row["status"], CultureInfo.InvariantCulture) ?? "";
                output["notes"] = "";
                table.Rows.Add(output);
            }

            return table;
        }

        private static DataTable BuildMsProjectSyncManifestTable(
            DateTime snapshotUtc,
            int boqRows,
            int summaryRows,
            int elementRows,
            int importTemplateRows,
            string projectName,
            string projectPath,
            DateTime? dataDateLocal,
            string dataDateSource)
        {
            var table = new DataTable("msp_sync_manifest");
            table.Columns.Add("key", typeof(string));
            table.Columns.Add("value", typeof(string));

            void Add(string key, string value)
            {
                DataRow row = table.NewRow();
                row["key"] = key ?? "";
                row["value"] = value ?? "";
                table.Rows.Add(row);
            }

            Add(MsProjectManifestSnapshotUtcKey, snapshotUtc.ToString("o", CultureInfo.InvariantCulture));
            Add(MsProjectManifestProjectNameKey, projectName ?? "");
            Add(MsProjectManifestProjectPathKey, projectPath ?? "");
            Add(MsProjectManifestDataDateKey,
                dataDateLocal.HasValue
                    ? dataDateLocal.Value.ToString("o", CultureInfo.InvariantCulture)
                    : "");
            Add(MsProjectManifestDataDateSourceKey, dataDateSource ?? "");
            Add("boq_rows", boqRows.ToString(CultureInfo.InvariantCulture));
            Add("site_progress_summary_rows", summaryRows.ToString(CultureInfo.InvariantCulture));
            Add("site_progress_element_rows", elementRows.ToString(CultureInfo.InvariantCulture));
            Add("import_template_rows", importTemplateRows.ToString(CultureInfo.InvariantCulture));

            return table;
        }

        private bool TryGetActiveMsProjectContext(out MsProjectActiveContext context)
        {
            context = null;
            object appObj = null;
            object projectObj = null;
            object summaryTaskObj = null;

            try
            {
                appObj = Marshal.GetActiveObject(MsProjectApplicationProgId);
                if (appObj == null)
                {
                    return false;
                }

                dynamic app = appObj;
                projectObj = app.ActiveProject;
                if (projectObj == null)
                {
                    return false;
                }

                dynamic project = projectObj;
                string projectName = Convert.ToString(project.Name, CultureInfo.InvariantCulture) ?? "";
                string projectPath = Convert.ToString(project.FullName, CultureInfo.InvariantCulture) ?? "";
                string summaryTaskName = "";

                try
                {
                    summaryTaskObj = project.ProjectSummaryTask;
                    if (summaryTaskObj != null)
                    {
                        dynamic summaryTask = summaryTaskObj;
                        summaryTaskName = Convert.ToString(summaryTask.Name, CultureInfo.InvariantCulture) ?? "";
                    }
                }
                catch
                {
                    summaryTaskName = "";
                }

                var result = new MsProjectActiveContext
                {
                    ProjectPath = (projectPath ?? "").Trim(),
                    ProjectName = ResolveMsProjectDisplayName(summaryTaskName, projectName, projectPath)
                };

                object statusDateObj = null;
                try
                {
                    statusDateObj = project.StatusDate;
                    if (TryConvertComDateToLocal(statusDateObj, out DateTime statusDate))
                    {
                        result.DataDateLocal = statusDate;
                        result.DataDateSource = "status_date";
                    }
                }
                catch
                {
                    // Ignore and continue with fallback date sources.
                }
                finally
                {
                    SafeReleaseCom(statusDateObj);
                }

                if (!result.DataDateLocal.HasValue)
                {
                    object appDateObj = null;
                    try
                    {
                        appDateObj = app.Date;
                        if (TryConvertComDateToLocal(appDateObj, out DateTime appDate))
                        {
                            result.DataDateLocal = appDate;
                            result.DataDateSource = "application_date";
                        }
                    }
                    catch
                    {
                        // Keep null if no date can be captured from active MS Project.
                    }
                    finally
                    {
                        SafeReleaseCom(appDateObj);
                    }
                }

                context = result;
                return !string.IsNullOrWhiteSpace(result.ProjectName) || result.DataDateLocal.HasValue;
            }
            catch
            {
                return false;
            }
            finally
            {
                SafeReleaseCom(summaryTaskObj);
                SafeReleaseCom(projectObj);
                SafeReleaseCom(appObj);
            }
        }

        private static string ResolveMsProjectDisplayName(string summaryTaskName, string projectName, string projectPath)
        {
            string summary = (summaryTaskName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            string name = (projectName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                string fromName = Path.GetFileNameWithoutExtension(name);
                return string.IsNullOrWhiteSpace(fromName) ? name : fromName.Trim();
            }

            string path = (projectPath ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                string fromPath = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(fromPath))
                {
                    return fromPath.Trim();
                }
            }

            return "";
        }

        private static List<SiteProgressImportRowPayload> ReadMsProjectProgressRowsFromCsv(
            string path,
            out int parsedRows,
            out int skippedRows)
        {
            parsedRows = 0;
            skippedRows = 0;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("Progress CSV file not found.", path ?? "");
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                return new List<SiteProgressImportRowPayload>();
            }

            int headerIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
            if (headerIndex < 0)
            {
                return new List<SiteProgressImportRowPayload>();
            }

            List<string> headers = ParseCsvLine(lines[headerIndex]);
            int idxStructure = IndexOfHeader(headers, "structure_element", "Structure Element", "structure", "element");
            int idxBuilding = IndexOfHeader(headers, "building_level", "Building Level", "level");
            int idxType = IndexOfHeader(headers, "type", "Type", "type_name");
            int idxRebar = IndexOfHeader(headers,
                "progress_reinforcement_pct",
                "rebar_progress_pct",
                "Rebar Progress %",
                "Site Progress Rebar %",
                "progress_reinforcement_ratio");
            int idxFormwork = IndexOfHeader(headers,
                "progress_formwork_pct",
                "formwork_progress_pct",
                "Formwork Progress %",
                "Site Progress Formwork %",
                "progress_formwork_ratio");
            int idxVolume = IndexOfHeader(headers,
                "progress_volume_pct",
                "volume_progress_pct",
                "Volume Progress %",
                "Site Progress Volume %",
                "progress_pct",
                "progress_volume_ratio");

            if (idxStructure < 0 || (idxRebar < 0 && idxFormwork < 0 && idxVolume < 0))
            {
                throw new InvalidOperationException(
                    "CSV must contain 'structure_element' and at least one progress column (rebar/formwork/volume).");
            }

            var rows = new List<SiteProgressImportRowPayload>();
            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                parsedRows++;
                List<string> cells = ParseCsvLine(lines[i]);
                string structure = ValueAt(cells, idxStructure);
                if (string.IsNullOrWhiteSpace(structure))
                {
                    skippedRows++;
                    continue;
                }

                bool hasRebar = TryReadImportedPercent(cells, idxRebar, out double? rebarPercent);
                bool hasFormwork = TryReadImportedPercent(cells, idxFormwork, out double? formworkPercent);
                bool hasVolume = TryReadImportedPercent(cells, idxVolume, out double? volumePercent);
                if (!hasRebar && !hasFormwork && !hasVolume)
                {
                    skippedRows++;
                    continue;
                }

                var row = new SiteProgressImportRowPayload
                {
                    StructureElement = structure,
                    BuildingLevel = ValueAt(cells, idxBuilding),
                    TypeName = ValueAt(cells, idxType),
                    ReinforcementPercent = rebarPercent,
                    FormworkPercent = formworkPercent,
                    VolumePercent = volumePercent
                };
                rows.Add(row);
            }

            return rows;
        }

        private T GetPmDashboardNamedControl<T>(string name) where T : class
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return FindName(name) as T;
        }

        private void InitializePmDashboardDefaults()
        {
            LoadPmStageMappingRules();
            LoadPmAutoMapMatchOptions();

            var pmSubTabControl = GetPmDashboardNamedControl<TabControl>("PmDashboardSubTabControl");
            var pmShowSCurveCheck = GetPmDashboardNamedControl<CheckBox>("PmDashboardShowSCurveCheck");
            var pmShowColumnChartCheck = GetPmDashboardNamedControl<CheckBox>("PmDashboardShowColumnChartCheck");
            var pmMultiSubTabCheck = GetPmDashboardNamedControl<CheckBox>("PmDashboardMultiSubTabCheck");

            _pmDashboardUiUpdateInProgress = true;
            try
            {
                RefreshPmDashboardProjectOptions();

                if (pmSubTabControl != null)
                {
                    TabItem targetTab = pmSubTabControl.Items
                        .OfType<TabItem>()
                        .FirstOrDefault(t =>
                            string.Equals(
                                Convert.ToString(t.Header, CultureInfo.InvariantCulture)?.Trim(),
                                "Project Dashboard",
                                StringComparison.OrdinalIgnoreCase));
                    if (targetTab != null)
                    {
                        pmSubTabControl.SelectedItem = targetTab;
                    }
                }

                if (pmShowSCurveCheck != null && pmShowSCurveCheck.IsChecked != true)
                {
                    pmShowSCurveCheck.IsChecked = true;
                }

                if (pmShowColumnChartCheck != null && pmShowColumnChartCheck.IsChecked != true)
                {
                    pmShowColumnChartCheck.IsChecked = true;
                }

                if (pmMultiSubTabCheck != null && pmMultiSubTabCheck.IsChecked == true)
                {
                    pmMultiSubTabCheck.IsChecked = false;
                }
            }
            finally
            {
                _pmDashboardUiUpdateInProgress = false;
            }

            SetPmDashboardMultiSubTabMode(false);
            RefreshPmDashboardSummaryAndChart();
            RefreshPmUpdateProgressGrid(null);
            InitializePmDashboardReportsDefaults();
        }

        private void OnPmDashboardMultiSubTabCheckChanged(object sender, RoutedEventArgs e)
        {
            bool enable = false;
            if (sender is CheckBox check)
            {
                enable = check.IsChecked == true;
            }

            SetPmDashboardMultiSubTabMode(enable);
        }

        private void SetPmDashboardMultiSubTabMode(bool enable)
        {
            if (_pmDashboardMultiSubTabMode == enable)
            {
                return;
            }

            var pmSubTabControl = GetPmDashboardNamedControl<TabControl>("PmDashboardSubTabControl");
            var pmMultiHost = GetPmDashboardNamedControl<StackPanel>("PmDashboardMultiTabHost");
            var pmMultiScroll = GetPmDashboardNamedControl<ScrollViewer>("PmDashboardMultiTabScrollViewer");
            if (pmSubTabControl == null || pmMultiHost == null || pmMultiScroll == null)
            {
                return;
            }

            if (enable)
            {
                pmMultiHost.Children.Clear();
                _pmDashboardTabContentCache.Clear();

                foreach (TabItem tab in pmSubTabControl.Items.OfType<TabItem>())
                {
                    if (tab == null)
                    {
                        continue;
                    }

                    object content = tab.Content;
                    _pmDashboardTabContentCache[tab] = content;
                    tab.Content = null;

                    var expander = new Expander
                    {
                        Header = tab.Header ?? "Sub-Tab",
                        IsExpanded = true,
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    if (content is UIElement contentElement)
                    {
                        expander.Content = contentElement;
                    }
                    else
                    {
                        expander.Content = new TextBlock
                        {
                            Text = Convert.ToString(content, CultureInfo.InvariantCulture) ?? "",
                            Margin = new Thickness(8)
                        };
                    }

                    pmMultiHost.Children.Add(expander);
                }

                pmSubTabControl.Visibility = System.Windows.Visibility.Collapsed;
                pmMultiScroll.Visibility = System.Windows.Visibility.Visible;
                _pmDashboardMultiSubTabMode = true;
                ShowStatus("PM Dashboard: multi sub-tab mode is ON.");
                return;
            }

            foreach (TabItem tab in pmSubTabControl.Items.OfType<TabItem>())
            {
                if (tab == null)
                {
                    continue;
                }

                if (_pmDashboardTabContentCache.TryGetValue(tab, out object content))
                {
                    tab.Content = content;
                }
            }

            pmMultiHost.Children.Clear();
            _pmDashboardTabContentCache.Clear();
            pmMultiScroll.Visibility = System.Windows.Visibility.Collapsed;
            pmSubTabControl.Visibility = System.Windows.Visibility.Visible;
            _pmDashboardMultiSubTabMode = false;
            ShowStatus("PM Dashboard: multi sub-tab mode is OFF.");
        }

        private void RefreshPmDashboardProjectOptions()
        {
            var pmProjectCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardProjectCombo");
            if (pmProjectCombo == null)
            {
                return;
            }

            string selected = Convert.ToString(pmProjectCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "";
            List<string> options = BuildPmDashboardProjectOptions();
            if (options.Count == 0)
            {
                options.Add("No Active MS Project");
            }

            pmProjectCombo.ItemsSource = options;
            if (!string.IsNullOrWhiteSpace(selected) &&
                options.Any(o => string.Equals(o, selected, StringComparison.OrdinalIgnoreCase)))
            {
                pmProjectCombo.SelectedItem = options.First(o =>
                    string.Equals(o, selected, StringComparison.OrdinalIgnoreCase));
                return;
            }

            string preferred = GetPmDashboardPreferredProjectName();
            if (!string.IsNullOrWhiteSpace(preferred) &&
                options.Any(o => string.Equals(o, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                pmProjectCombo.SelectedItem = options.First(o =>
                    string.Equals(o, preferred, StringComparison.OrdinalIgnoreCase));
                return;
            }

            pmProjectCombo.SelectedIndex = 0;
        }

        private List<string> BuildPmDashboardProjectOptions()
        {
            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TryReadPmDashboardActiveProjectName(out string projectName))
            {
                options.Add(projectName);
            }
            else if (TryGetActiveMsProjectContext(out MsProjectActiveContext activeContext) &&
                     !string.IsNullOrWhiteSpace(activeContext?.ProjectName))
            {
                options.Add(activeContext.ProjectName);
            }

            return options
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetPmDashboardPreferredProjectName()
        {
            if (TryReadPmDashboardActiveProjectName(out string projectName))
            {
                return projectName;
            }

            if (TryGetActiveMsProjectContext(out MsProjectActiveContext activeContext) &&
                !string.IsNullOrWhiteSpace(activeContext.ProjectName))
            {
                return activeContext.ProjectName;
            }

            return "";
        }

        private bool TryReadPmDashboardActiveProjectName(out string projectName)
        {
            projectName = "";
            object appObj = null;
            object projectObj = null;
            object summaryTaskObj = null;

            try
            {
                appObj = Marshal.GetActiveObject(MsProjectApplicationProgId);
                if (appObj == null)
                {
                    return false;
                }

                projectObj = ReadPmDashboardComProperty(appObj, "ActiveProject");
                if (projectObj == null)
                {
                    return false;
                }

                string projectNameRaw =
                    Convert.ToString(ReadPmDashboardComProperty(projectObj, "Name"), CultureInfo.InvariantCulture) ?? "";
                string projectPathRaw =
                    Convert.ToString(ReadPmDashboardComProperty(projectObj, "FullName"), CultureInfo.InvariantCulture) ?? "";
                string summaryTaskNameRaw = "";

                summaryTaskObj = ReadPmDashboardComProperty(projectObj, "ProjectSummaryTask");
                if (summaryTaskObj != null)
                {
                    summaryTaskNameRaw =
                        Convert.ToString(ReadPmDashboardComProperty(summaryTaskObj, "Name"), CultureInfo.InvariantCulture) ?? "";
                }

                string resolvedName = ResolveMsProjectDisplayName(summaryTaskNameRaw, projectNameRaw, projectPathRaw);
                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    return false;
                }

                projectName = resolvedName.Trim();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                SafeReleaseCom(summaryTaskObj);
                SafeReleaseCom(projectObj);
                SafeReleaseCom(appObj);
            }
        }

        private bool TryReadPmDashboardManifest(out Dictionary<string, string> manifestValues)
        {
            manifestValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string folder = ResolveMsProjectSyncFolder();
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return false;
                }

                string path = Path.Combine(folder, MsProjectManifestFileName);
                if (!File.Exists(path))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 2)
                {
                    return false;
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    List<string> cells = ParseCsvLine(lines[i]);
                    if (cells.Count < 2)
                    {
                        continue;
                    }

                    string key = (cells[0] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    manifestValues[key] = (cells[1] ?? "").Trim();
                }

                return manifestValues.Count > 0;
            }
            catch
            {
                manifestValues.Clear();
                return false;
            }
        }

        private bool TryReadPmDashboardManifestValue(string key, out string value)
        {
            value = "";
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!TryReadPmDashboardManifest(out Dictionary<string, string> manifestValues))
            {
                return false;
            }

            if (!manifestValues.TryGetValue(key, out string raw))
            {
                return false;
            }

            value = (raw ?? "").Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private void OnPmDashboardProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _pmDashboardUiUpdateInProgress)
            {
                return;
            }

            RefreshPmDashboardSummaryAndChart();
        }

        private void OnPmDashboardRefreshDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowStatus("PM Dashboard: refreshing live MS Project data...");
                RefreshPmDashboardProjectOptions();
                RefreshPmDashboardSummaryAndChart();
                if (_pmDashboardLiveDataAvailable)
                {
                    AppendPmDashboardLog("PM Dashboard live data refreshed from active MS Project.");
                    ShowStatus("PM Dashboard refresh complete (live MS Project).");
                }
                else
                {
                    AppendPmDashboardLog("PM Dashboard refresh: no active MS Project data available.");
                    ShowStatus("PM Dashboard: no active MS Project project found.");
                }
            }
            catch (Exception ex)
            {
                string message = "PM Dashboard refresh failed: " + ex.Message;
                AppendPmDashboardLog(message);
                ShowStatus(message);
            }
        }

        private void OnPmDashboardSCurveOptionChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _pmDashboardUiUpdateInProgress)
            {
                return;
            }

            RefreshPmDashboardSummaryAndChart();
        }

        private void OnPmDashboardSCurveOptionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _pmDashboardUiUpdateInProgress)
            {
                return;
            }

            RefreshPmDashboardSummaryAndChart();
        }

        private void OnPmDashboardExportSCurveExcelClick(object sender, RoutedEventArgs e)
        {
            if (_pmDashboardCurvePoints.Count == 0)
            {
                ShowStatus("PM Dashboard: no chart data to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export PM S-Curve Data",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "pm_dashboard_scurve_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv",
                OverwritePrompt = true
            };

            bool? accepted = dialog.ShowDialog(this);
            if (accepted != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                var table = new DataTable("pm_dashboard_scurve");
                table.Columns.Add("period", typeof(string));
                table.Columns.Add("planned_cumulative", typeof(double));
                table.Columns.Add("actual_cumulative", typeof(double));
                table.Columns.Add("actual_cost_cumulative", typeof(double));
                table.Columns.Add("forecast_cumulative", typeof(double));
                table.Columns.Add("period_planned", typeof(double));
                table.Columns.Add("period_actual", typeof(double));
                table.Columns.Add("period_actual_cost", typeof(double));

                foreach (PmDashboardCurvePoint point in _pmDashboardCurvePoints)
                {
                    if (point == null)
                    {
                        continue;
                    }

                    DataRow row = table.NewRow();
                    row["period"] = point.Label ?? "";
                    row["planned_cumulative"] = point.PlannedCumulative;
                    row["actual_cumulative"] = point.ActualCumulative;
                    row["actual_cost_cumulative"] = point.ActualCostCumulative;
                    row["forecast_cumulative"] = point.ForecastCumulative;
                    row["period_planned"] = point.PeriodPlanned;
                    row["period_actual"] = point.PeriodActual;
                    row["period_actual_cost"] = point.PeriodActualCost;
                    table.Rows.Add(row);
                }

                WriteDataTableToCsv(table, dialog.FileName);
                string message = "PM Dashboard S-Curve exported: " + dialog.FileName;
                AppendPmDashboardLog(message);
                ShowStatus(message);
            }
            catch (Exception ex)
            {
                string message = "PM Dashboard export failed: " + ex.Message;
                AppendPmDashboardLog(message);
                ShowStatus(message);
            }
        }

        private void OnPmDashboardExportSCurvePdfClick(object sender, RoutedEventArgs e)
        {
            var pmPlotView = GetPmDashboardNamedControl<OxyPlot.Wpf.PlotView>("PmDashboardSCurvePlotView");
            if (pmPlotView == null || pmPlotView.Model == null)
            {
                ShowStatus("PM Dashboard: no S-Curve chart available to print.");
                return;
            }

            try
            {
                var dialog = new PrintDialog();
                bool? accepted = dialog.ShowDialog();
                if (accepted != true)
                {
                    return;
                }

                pmPlotView.UpdateLayout();
                dialog.PrintVisual(pmPlotView, "PM Dashboard S-Curve");
                string message = "PM Dashboard S-Curve sent to printer/PDF.";
                AppendPmDashboardLog(message);
                ShowStatus(message);
            }
            catch (Exception ex)
            {
                string message = "PM Dashboard PDF export failed: " + ex.Message;
                AppendPmDashboardLog(message);
                ShowStatus(message);
            }
        }

        private void RefreshPmDashboardSummaryAndChart()
        {
            PmDashboardLiveSnapshot liveSnapshot = TryBuildPmDashboardLiveSnapshot();
            if (liveSnapshot == null || liveSnapshot.Tasks.Count == 0)
            {
                _reportsLatestSnapshot = null;
                _pmDashboardLiveDataAvailable = false;
                _pmDashboardCurrentStatusDate = null;
                UpdatePmDashboardDataDateDisplay(null);
                UpdatePmDashboardKpis(null);
                _pmDashboardCurvePoints.Clear();
                RenderPmDashboardSCurvePlot();
                RefreshPmUpdateProgressGrid(null);
                RefreshReportsFromLiveSnapshot(null);
                return;
            }

            _reportsLatestSnapshot = liveSnapshot;
            _pmDashboardLiveDataAvailable = true;
            _pmDashboardCurrentStatusDate = liveSnapshot.StatusDate.Date;
            SetPmDashboardProjectNameFromLiveSnapshot(liveSnapshot.ProjectName);
            UpdatePmDashboardDataDateDisplay(liveSnapshot.StatusDate);
            UpdatePmDashboardKpis(liveSnapshot);

            _pmDashboardCurvePoints.Clear();
            _pmDashboardCurvePoints.AddRange(BuildPmDashboardCurvePoints(liveSnapshot));
            RenderPmDashboardSCurvePlot();
            RefreshPmUpdateProgressGrid(liveSnapshot);
            RefreshReportsFromLiveSnapshot(liveSnapshot);
        }

        private void SetPmDashboardProjectNameFromLiveSnapshot(string liveProjectName)
        {
            string safeName = (liveProjectName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return;
            }

            var pmProjectCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardProjectCombo");
            if (pmProjectCombo == null)
            {
                return;
            }

            _pmDashboardUiUpdateInProgress = true;
            try
            {
                pmProjectCombo.ItemsSource = new List<string> { safeName };
                pmProjectCombo.SelectedIndex = 0;
            }
            finally
            {
                _pmDashboardUiUpdateInProgress = false;
            }
        }

        private void RefreshPmMsProjectFilterOptions()
        {
            var buildingCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectBuildingLevelFilterCombo");
            var structureCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectStructureElementFilterCombo");
            var typeCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectStructureTypeFilterCombo");
            var revitActiveCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectRevitActiveFilterCombo");
            var grid = GetPmDashboardNamedControl<DataGrid>("PmUpdateProgressDataGrid");
            if (buildingCombo == null ||
                structureCombo == null ||
                typeCombo == null ||
                revitActiveCombo == null ||
                !(grid?.ItemsSource is DataView view))
            {
                return;
            }

            EnsurePmMsProjectFilterColumns(view);
            List<DataRow> rows = view.Table.Rows
                .Cast<DataRow>()
                .Where(r => r != null && r.RowState != DataRowState.Deleted)
                .ToList();
            SetPmUpdateProgressFilterComboOptions(
                buildingCombo,
                rows.Select(r => ReadPmUpdateProgressRowString(r, "pm_filter_building_level")),
                sortByBuildingLevel: true);
            SetPmUpdateProgressFilterComboOptions(
                structureCombo,
                rows.Select(r => ReadPmUpdateProgressRowString(r, "pm_filter_structure_element")),
                sortByBuildingLevel: false);
            SetPmUpdateProgressFilterComboOptions(
                typeCombo,
                rows.Select(r => ReadPmUpdateProgressRowString(r, "pm_filter_structure_type")),
                sortByBuildingLevel: false);
            SetPmUpdateProgressRevitLinkFilterComboOptions(revitActiveCombo);
        }

        private void OnPmMsProjectFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pmUpdateProgressFilterUiUpdateInProgress)
            {
                return;
            }

            ApplyPmMsProjectTableFilters();
        }

        private void ApplyPmMsProjectTableFilters()
        {
            var grid = GetPmDashboardNamedControl<DataGrid>("PmUpdateProgressDataGrid");
            var buildingCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectBuildingLevelFilterCombo");
            var structureCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectStructureElementFilterCombo");
            var typeCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectStructureTypeFilterCombo");
            var revitActiveCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmMsProjectRevitActiveFilterCombo");
            if (grid == null ||
                buildingCombo == null ||
                structureCombo == null ||
                typeCombo == null ||
                revitActiveCombo == null ||
                !(grid.ItemsSource is DataView dataView))
            {
                return;
            }

            HashSet<string> selectedBuildings = GetPmUpdateProgressFilterValueSet(buildingCombo);
            HashSet<string> selectedStructures = GetPmUpdateProgressFilterValueSet(structureCombo);
            HashSet<string> selectedTypes = GetPmUpdateProgressFilterValueSet(typeCombo);
            HashSet<string> selectedRevitStates = GetPmUpdateProgressFilterValueSet(revitActiveCombo);

            EnsurePmMsProjectFilterColumns(dataView);

            var conditions = new List<string>();
            if (!IsPmUpdateProgressAllFilter(selectedBuildings))
            {
                conditions.Add(BuildPmMsProjectMultiValueFilterCondition("pm_filter_building_level", selectedBuildings));
            }
            if (!IsPmUpdateProgressAllFilter(selectedStructures))
            {
                conditions.Add(BuildPmMsProjectMultiValueFilterCondition("pm_filter_structure_element", selectedStructures));
            }
            if (!IsPmUpdateProgressAllFilter(selectedTypes))
            {
                conditions.Add(BuildPmMsProjectMultiValueFilterCondition("pm_filter_structure_type", selectedTypes));
            }
            if (!IsPmUpdateProgressAllFilter(selectedRevitStates))
            {
                conditions.Add(BuildPmMsProjectMultiValueFilterCondition("pm_filter_revit_link_state", selectedRevitStates));
            }

            dataView.RowFilter = conditions.Count == 0
                ? ""
                : string.Join(" AND ", conditions.Where(c => !string.IsNullOrWhiteSpace(c)));
        }

        private static string BuildPmMsProjectMultiValueFilterCondition(string columnName, HashSet<string> values)
        {
            if (string.IsNullOrWhiteSpace(columnName) || values == null || values.Count == 0)
            {
                return "";
            }

            List<string> safeValues = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => EscapePmMsProjectRowFilterLiteral(v.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (safeValues.Count == 0)
            {
                return "";
            }

            if (safeValues.Count == 1)
            {
                return "[" + columnName + "] = '" + safeValues[0] + "'";
            }

            return "(" + string.Join(
                       " OR ",
                       safeValues.Select(v => "[" + columnName + "] = '" + v + "'")) + ")";
        }

        private static string ResolvePmMsProjectFilterBuildingLevel(DataRowView rowView)
        {
            return ResolvePmMsProjectFilterBuildingLevel(rowView?.Row);
        }

        private static string ResolvePmMsProjectFilterStructureElement(DataRowView rowView)
        {
            return ResolvePmMsProjectFilterStructureElement(rowView?.Row);
        }

        private static string ResolvePmMsProjectFilterStructureType(DataRowView rowView)
        {
            return ResolvePmMsProjectFilterStructureType(rowView?.Row);
        }

        private static string ResolvePmMsProjectFilterBuildingLevel(DataRow row)
        {
            if (row == null)
            {
                return "";
            }

            string fromRevit = ReadPmUpdateProgressRowString(row, "revit_building_level");
            if (!string.IsNullOrWhiteSpace(fromRevit))
            {
                return fromRevit;
            }

            ParsePmUpdateProgressTaskKeyParts(
                ReadPmUpdateProgressRowString(row, "task_key"),
                out _,
                out string levelFromTaskKey,
                out _);
            if (!string.IsNullOrWhiteSpace(levelFromTaskKey))
            {
                return levelFromTaskKey;
            }

            return ReadPmUpdateProgressRowString(row, "pm_filter_building_level");
        }

        private static string ResolvePmMsProjectFilterStructureElement(DataRow row)
        {
            if (row == null)
            {
                return "";
            }

            string fromRevit = ReadPmUpdateProgressRowString(row, "revit_structure_element");
            if (!string.IsNullOrWhiteSpace(fromRevit))
            {
                return fromRevit;
            }

            ParsePmUpdateProgressTaskKeyParts(
                ReadPmUpdateProgressRowString(row, "task_key"),
                out string structureFromTaskKey,
                out _,
                out _);
            if (!string.IsNullOrWhiteSpace(structureFromTaskKey))
            {
                return structureFromTaskKey;
            }

            return ReadPmUpdateProgressRowString(row, "pm_filter_structure_element");
        }

        private static string ResolvePmMsProjectFilterStructureType(DataRow row)
        {
            if (row == null)
            {
                return "";
            }

            string fromRevit = ReadPmUpdateProgressRowString(row, "revit_type");
            if (!string.IsNullOrWhiteSpace(fromRevit))
            {
                return fromRevit;
            }

            ParsePmUpdateProgressTaskKeyParts(
                ReadPmUpdateProgressRowString(row, "task_key"),
                out _,
                out _,
                out string typeFromTaskKey);
            if (!string.IsNullOrWhiteSpace(typeFromTaskKey))
            {
                return typeFromTaskKey;
            }

            string fromFilter = ReadPmUpdateProgressRowString(row, "pm_filter_structure_type");
            if (!string.IsNullOrWhiteSpace(fromFilter))
            {
                return fromFilter;
            }

            return ResolvePmStageDisplayNameFromTaskText(ReadPmUpdateProgressRowString(row, "task_name"));
        }

        private static string ResolvePmMsProjectRevitLinkState(DataRow row)
        {
            if (row == null)
            {
                return PmUpdateProgressRevitLinkNotActive;
            }

            string elementIds = ReadPmUpdateProgressRowString(row, "revit_element_ids");
            return ParsePmUpdateProgressElementIds(elementIds).Count > 0
                ? PmUpdateProgressRevitLinkActive
                : PmUpdateProgressRevitLinkNotActive;
        }

        private static void EnsurePmMsProjectFilterColumns(DataView dataView)
        {
            if (dataView?.Table == null)
            {
                return;
            }

            DataTable table = dataView.Table;
            if (!table.Columns.Contains("pm_filter_building_level"))
            {
                table.Columns.Add("pm_filter_building_level", typeof(string));
            }
            if (!table.Columns.Contains("pm_filter_structure_element"))
            {
                table.Columns.Add("pm_filter_structure_element", typeof(string));
            }
            if (!table.Columns.Contains("pm_filter_structure_type"))
            {
                table.Columns.Add("pm_filter_structure_type", typeof(string));
            }
            if (!table.Columns.Contains("pm_filter_revit_link_state"))
            {
                table.Columns.Add("pm_filter_revit_link_state", typeof(string));
            }

            foreach (DataRow row in table.Rows)
            {
                if (row == null || row.RowState == DataRowState.Deleted)
                {
                    continue;
                }

                row["pm_filter_building_level"] = ResolvePmMsProjectFilterBuildingLevel(row);
                row["pm_filter_structure_element"] = ResolvePmMsProjectFilterStructureElement(row);
                row["pm_filter_structure_type"] = ResolvePmMsProjectFilterStructureType(row);
                row["pm_filter_revit_link_state"] = ResolvePmMsProjectRevitLinkState(row);
            }
        }

        private static string EscapePmMsProjectRowFilterLiteral(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private static string FormatPmDashboardDate(DateTime date)
        {
            if (!IsValidPmDashboardProjectDate(date))
            {
                return "";
            }

            return date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        private void OnPmUpdateProgressApplyToMsProjectClick(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (_pmUpdateProgressApplyInProgress)
            {
                ShowStatus("Update Progress: apply is already running.");
                return;
            }

            var grid = GetPmDashboardNamedControl<DataGrid>("PmUpdateProgressDataGrid");
            if (!(grid?.ItemsSource is DataView view) || view.Table == null || view.Table.Rows.Count == 0)
            {
                ShowStatus("Update Progress: no rows to apply.");
                return;
            }

            // Ensure in-place edits are committed before reading DataTable values.
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
                // Ignore; apply can continue using current committed values.
            }

            try
            {
                _pmUpdateProgressApplyInProgress = true;
                List<DataRow> applyRows = CollectPmUpdateProgressRowsForApply(
                    grid,
                    view,
                    out string applyScopeLabel,
                    out int scopeRowCount);
                if (applyRows.Count == 0)
                {
                    ShowStatus(
                        "Update Progress: no mapped Revit row(s) found in " +
                        applyScopeLabel +
                        " (" +
                        scopeRowCount.ToString(CultureInfo.InvariantCulture) +
                        " row(s) checked). Drag/drop Detail Progress first.");
                    return;
                }

                DataTable applyTable = BuildPmUpdateProgressApplyTable(view.Table, applyRows);
                if (TryApplyPmUpdateProgressToMsProject(applyTable, out string summary))
                {
                    summary += " Scope: applied " +
                               applyRows.Count.ToString(CultureInfo.InvariantCulture) +
                               " mapped row(s) from " +
                               applyScopeLabel +
                               " (" +
                               scopeRowCount.ToString(CultureInfo.InvariantCulture) +
                               " row(s) checked).";
                    ShowStatus(summary);
                    RefreshPmDashboardSummaryAndChart();
                }
                else
                {
                    ShowStatus(summary);
                }
            }
            finally
            {
                _pmUpdateProgressApplyInProgress = false;
            }
        }

        private bool TryApplyPmUpdateProgressToMsProject(DataTable table, out string summary)
        {
            summary = "";
            if (table == null || table.Rows.Count == 0)
            {
                summary = "Update Progress: no rows available.";
                return false;
            }

            var updateById = new Dictionary<int, PmUpdateProgressTaskApplyRow>();
            var updateByUniqueId = new Dictionary<int, PmUpdateProgressTaskApplyRow>();
            int skippedInputRows = 0;

            foreach (DataRow row in table.Rows)
            {
                if (row == null || row.RowState == DataRowState.Deleted)
                {
                    continue;
                }

                int taskId = ConvertToIntSafe(row["task_id"]);
                int taskUniqueId = ConvertToIntSafe(row["task_unique_id"]);
                if (taskId <= 0 && taskUniqueId <= 0)
                {
                    skippedInputRows++;
                    continue;
                }

                double targetPercent = ResolvePmUpdateProgressTargetPercent(row);
                PmUpdateProgressTaskApplyRow applyRow = BuildPmUpdateProgressTaskApplyRow(row, targetPercent);
                if (table.Columns.Contains("status"))
                {
                    row["status"] = ResolvePmProgressStatusFromPercent(targetPercent);
                }
                if (table.Columns.Contains("percent_complete"))
                {
                    row["percent_complete"] = targetPercent;
                }
                if (taskId > 0)
                {
                    updateById[taskId] = applyRow;
                }
                else
                {
                    updateByUniqueId[taskUniqueId] = applyRow;
                }
            }

            if (updateById.Count == 0 && updateByUniqueId.Count == 0)
            {
                summary = "Update Progress: no valid task rows to apply.";
                return false;
            }

            object appObj = null;
            object projectObj = null;
            object tasksObj = null;
            int changed = 0;
            int unchanged = 0;
            int matched = 0;
            int metadataUpdated = 0;

            try
            {
                appObj = Marshal.GetActiveObject(MsProjectApplicationProgId);
                if (appObj == null)
                {
                    summary = "Update Progress: active MS Project is not available.";
                    return false;
                }

                projectObj = ReadPmDashboardComProperty(appObj, "ActiveProject");
                if (projectObj == null)
                {
                    summary = "Update Progress: active MS Project project was not found.";
                    return false;
                }

                EnsureMsProjectTaskMetadataFields(appObj);

                tasksObj = ReadPmDashboardComProperty(projectObj, "Tasks");
                if (tasksObj == null)
                {
                    summary = "Update Progress: no tasks found in active MS Project.";
                    return false;
                }

                foreach (object rawTask in (dynamic)tasksObj)
                {
                    object taskObj = rawTask;
                    try
                    {
                        if (taskObj == null || ReadPmDashboardComBool(taskObj, "Summary"))
                        {
                            continue;
                        }

                        int taskId = Math.Max(0, ReadPmDashboardComInt(taskObj, "ID"));
                        int taskUniqueId = Math.Max(0, ReadPmDashboardComInt(taskObj, "UniqueID"));

                        bool hasTarget = false;
                        PmUpdateProgressTaskApplyRow applyRow = null;
                        if (taskId > 0 && updateById.TryGetValue(taskId, out applyRow))
                        {
                            hasTarget = true;
                            updateById.Remove(taskId);
                        }
                        else if (taskUniqueId > 0 && updateByUniqueId.TryGetValue(taskUniqueId, out applyRow))
                        {
                            hasTarget = true;
                            updateByUniqueId.Remove(taskUniqueId);
                        }

                        if (!hasTarget || applyRow == null)
                        {
                            continue;
                        }

                        matched++;
                        bool taskChanged = false;
                        int targetInt = Math.Max(0, Math.Min(100, Convert.ToInt32(Math.Round(applyRow.PercentComplete, MidpointRounding.AwayFromZero))));
                        int currentInt = Math.Max(0, Math.Min(100, Convert.ToInt32(Math.Round(ReadPmDashboardComDouble(taskObj, "PercentComplete"), MidpointRounding.AwayFromZero))));

                        if (currentInt != targetInt &&
                            WritePmDashboardComProperty(taskObj, "PercentComplete", targetInt))
                        {
                            taskChanged = true;
                        }

                        bool metadataChangedThisTask = false;
                        metadataChangedThisTask |= WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldBuildingLevel, applyRow.BuildingLevel);
                        metadataChangedThisTask |= WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldStructureElement, applyRow.StructureElement);
                        metadataChangedThisTask |= WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldStructureType, applyRow.StructureType);
                        metadataChangedThisTask |= WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldRevitElementIds, applyRow.RevitElementIds);
                        metadataChangedThisTask |= WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldUnit, applyRow.Unit);
                        if (applyRow.HasBoqValue)
                        {
                            metadataChangedThisTask |= WritePmDashboardTaskNumberFieldIfNeeded(taskObj, MsProjectTaskNumberFieldBoq, applyRow.Boq);
                        }
                        if (metadataChangedThisTask)
                        {
                            metadataUpdated++;
                            taskChanged = true;
                        }

                        if (taskChanged)
                        {
                            changed++;
                        }
                        else
                        {
                            unchanged++;
                        }
                    }
                    finally
                    {
                        SafeReleaseCom(taskObj);
                    }
                }

                int unmatched = updateById.Count + updateByUniqueId.Count;
                summary = "Update Progress applied to MS Project: changed=" +
                          changed.ToString(CultureInfo.InvariantCulture) +
                          ", unchanged=" +
                          unchanged.ToString(CultureInfo.InvariantCulture) +
                          ", metadata_updated=" +
                          metadataUpdated.ToString(CultureInfo.InvariantCulture) +
                          ", matched=" +
                          matched.ToString(CultureInfo.InvariantCulture) +
                          ", unmatched=" +
                          unmatched.ToString(CultureInfo.InvariantCulture) +
                          ", skipped input rows=" +
                          skippedInputRows.ToString(CultureInfo.InvariantCulture) + ".";

                return true;
            }
            catch (Exception ex)
            {
                summary = "Update Progress apply failed: " + ex.Message;
                return false;
            }
            finally
            {
                SafeReleaseCom(tasksObj);
                SafeReleaseCom(projectObj);
                SafeReleaseCom(appObj);
            }
        }

        private static bool WritePmDashboardTaskTextFieldIfNeeded(object taskObj, string fieldPropertyName, string targetValue)
        {
            if (taskObj == null || string.IsNullOrWhiteSpace(fieldPropertyName))
            {
                return false;
            }

            string next = (targetValue ?? "").Trim();
            if (string.IsNullOrWhiteSpace(next))
            {
                return false;
            }

            string current = (Convert.ToString(ReadPmDashboardComProperty(taskObj, fieldPropertyName), CultureInfo.InvariantCulture) ?? "").Trim();
            if (string.Equals(current, next, StringComparison.Ordinal))
            {
                return false;
            }

            return WritePmDashboardComProperty(taskObj, fieldPropertyName, next);
        }

        private static bool WritePmDashboardTaskNumberFieldIfNeeded(object taskObj, string fieldPropertyName, double targetValue)
        {
            if (taskObj == null || string.IsNullOrWhiteSpace(fieldPropertyName))
            {
                return false;
            }

            double next = RoundPmBoqValue(Math.Max(0.0, targetValue));
            double current = Math.Max(0.0, ConvertToDoubleSafe(ReadPmDashboardComProperty(taskObj, fieldPropertyName)));
            if (Math.Abs(current - next) <= 1e-6)
            {
                return false;
            }

            return WritePmDashboardComProperty(taskObj, fieldPropertyName, next);
        }

        private static void EnsureMsProjectTaskMetadataFields(object appObj)
        {
            if (appObj == null)
            {
                return;
            }

            EnsureMsProjectTaskTextFieldAlias(appObj, MsProjectTaskTextFieldBuildingLevel, MsProjectTaskTextAliasBuildingLevel);
            EnsureMsProjectTaskTextFieldAlias(appObj, MsProjectTaskTextFieldStructureElement, MsProjectTaskTextAliasStructureElement);
            EnsureMsProjectTaskTextFieldAlias(appObj, MsProjectTaskTextFieldStructureType, MsProjectTaskTextAliasStructureType);
            EnsureMsProjectTaskTextFieldAlias(appObj, MsProjectTaskTextFieldRevitElementIds, MsProjectTaskTextAliasRevitElementIds);
            EnsureMsProjectTaskTextFieldAlias(appObj, MsProjectTaskTextFieldUnit, MsProjectTaskTextAliasUnit);
            EnsureMsProjectTaskNumberFieldAlias(appObj, MsProjectTaskNumberFieldBoq, MsProjectTaskNumberAliasBoq);
        }

        private static void EnsureMsProjectTaskNumberFieldAlias(object appObj, string numberFieldName, string alias)
        {
            EnsureMsProjectTaskTextFieldAlias(appObj, numberFieldName, alias);
        }

        private static void EnsureMsProjectTaskTextFieldAlias(object appObj, string textFieldName, string alias)
        {
            if (appObj == null ||
                string.IsNullOrWhiteSpace(textFieldName) ||
                string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            int fieldConstant = ResolveMsProjectTaskFieldConstant(appObj, textFieldName);
            if (fieldConstant <= 0)
            {
                return;
            }

            try
            {
                appObj.GetType().InvokeMember(
                    "CustomFieldRename",
                    BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public,
                    null,
                    appObj,
                    new object[] { fieldConstant, alias.Trim() });
            }
            catch
            {
                // Keep PM workflow running even if rename is not allowed in this MS Project setup.
            }
        }

        private static int ResolveMsProjectTaskFieldConstant(object appObj, string fieldName)
        {
            if (appObj == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return 0;
            }

            try
            {
                object result = appObj.GetType().InvokeMember(
                    "FieldNameToFieldConstant",
                    BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public,
                    null,
                    appObj,
                    new object[] { fieldName.Trim(), MsProjectFieldTypeTask });
                return ConvertToIntSafe(result);
            }
            catch
            {
                return 0;
            }
        }

        private static bool WritePmDashboardComProperty(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                target.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.Public,
                    null,
                    target,
                    new[] { value });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void UpdatePmDashboardDataDateDisplay(DateTime? liveDataDate)
        {
            var pmDataDateText = GetPmDashboardNamedControl<TextBlock>("PmDashboardDataDateText");
            if (pmDataDateText == null)
            {
                return;
            }

            if (liveDataDate.HasValue && IsValidPmDashboardProjectDate(liveDataDate.Value))
            {
                pmDataDateText.Text = liveDataDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                return;
            }

            if (TryGetActiveMsProjectContext(out MsProjectActiveContext activeContext) &&
                activeContext?.DataDateLocal.HasValue == true)
            {
                pmDataDateText.Text = activeContext.DataDateLocal.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
                return;
            }

            pmDataDateText.Text = "--";
        }

        private bool TryReadPmDashboardDataDateFromManifest(out DateTime dataDateLocal)
        {
            dataDateLocal = DateTime.MinValue;
            try
            {
                if (!TryReadPmDashboardManifest(out Dictionary<string, string> manifestValues))
                {
                    return false;
                }

                if (manifestValues.TryGetValue(MsProjectManifestDataDateKey, out string dataDateRaw) &&
                    TryParsePmDashboardManifestDate(dataDateRaw, out dataDateLocal))
                {
                    return true;
                }

                return manifestValues.TryGetValue(MsProjectManifestSnapshotUtcKey, out string snapshotRaw) &&
                       TryParsePmDashboardManifestDate(snapshotRaw, out dataDateLocal);
            }
            catch
            {
                return false;
            }

        }

        private static bool TryParsePmDashboardManifestDate(string raw, out DateTime dataDateLocal)
        {
            dataDateLocal = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string trimmed = raw.Trim();

            if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset dto))
            {
                dataDateLocal = dto.LocalDateTime;
                return true;
            }

            if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTime parsed))
            {
                dataDateLocal = parsed.Kind == DateTimeKind.Utc ? parsed.ToLocalTime() : parsed;
                return true;
            }

            return false;
        }

        private void UpdatePmDashboardKpis(PmDashboardLiveSnapshot liveSnapshot)
        {
            var pmKpiTotalBaselineCostText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiTotalBaselineCostText");
            var pmKpiPlannedDurationText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiPlannedDurationText");
            var pmKpiActualDurationText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiActualDurationText");
            var pmKpiVarianceDurationText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiVarianceDurationText");
            var pmKpiPlannedCostText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiPlannedCostText");
            var pmKpiActualCostText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiActualCostText");
            var pmKpiVarianceCostText = GetPmDashboardNamedControl<TextBlock>("PmDashboardKpiVarianceCostText");
            var pmFinancialProgressText = GetPmDashboardNamedControl<TextBlock>("PmDashboardFinancialProgressText");
            var pmCurrentProgressText = GetPmDashboardNamedControl<TextBlock>("PmDashboardCurrentProgressText");
            var pmSummaryProgressText = GetPmDashboardNamedControl<TextBlock>("PmDashboardSummaryProgressText");
            var pmPerformanceText = GetPmDashboardNamedControl<TextBlock>("PmDashboardPerformanceText");
            string dataDateDisplay = GetPmDashboardNamedControl<TextBlock>("PmDashboardDataDateText")?.Text ?? "--";

            bool hasLiveData = liveSnapshot != null && liveSnapshot.Tasks.Count > 0;

            double bac;
            double pv;
            double ev;
            double ac;
            double remainingCost;
            double plannedDurationPct;
            double actualDurationPct;
            int totalItems;
            int completedItems;
            int inProgressItems;
            int notStartedItems;
            string itemLabel;

            if (hasLiveData)
            {
                bac = Math.Max(0.0, liveSnapshot.Bac);
                pv = Math.Max(0.0, liveSnapshot.PvToDate);
                ev = Math.Max(0.0, liveSnapshot.EvToDate);
                ac = Math.Max(0.0, liveSnapshot.AcToDate);
                remainingCost = Math.Max(0.0, liveSnapshot.RemainingCost);
                plannedDurationPct = liveSnapshot.BaselineDurationTotalDays > 1e-9
                    ? Math.Max(0.0, Math.Min(100.0, (liveSnapshot.PlannedDurationToDateDays / liveSnapshot.BaselineDurationTotalDays) * 100.0))
                    : 0.0;
                actualDurationPct = liveSnapshot.BaselineDurationTotalDays > 1e-9
                    ? Math.Max(0.0, Math.Min(100.0, (liveSnapshot.EarnedDurationToDateDays / liveSnapshot.BaselineDurationTotalDays) * 100.0))
                    : 0.0;
                totalItems = Math.Max(0, liveSnapshot.TotalTasks);
                completedItems = Math.Max(0, liveSnapshot.CompletedTasks);
                inProgressItems = Math.Max(0, liveSnapshot.InProgressTasks);
                notStartedItems = Math.Max(0, liveSnapshot.NotStartedTasks);
                itemLabel = "Tasks";
            }
            else
            {
                bac = 0.0;
                pv = 0.0;
                ev = 0.0;
                ac = 0.0;
                remainingCost = 0.0;
                plannedDurationPct = 0.0;
                actualDurationPct = 0.0;
                totalItems = 0;
                completedItems = 0;
                inProgressItems = 0;
                notStartedItems = 0;
                itemLabel = "Tasks";
            }

            if (bac <= 1e-9)
            {
                bac = Math.Max(Math.Max(pv, ev), ac);
            }

            double plannedCostPct = bac > 1e-9 ? (pv / bac) * 100.0 : 0.0;
            double actualCostPct = bac > 1e-9 ? (ev / bac) * 100.0 : 0.0;

            double durationVariance = actualDurationPct - plannedDurationPct;
            double costVariancePct = actualCostPct - plannedCostPct;

            string durationVarianceLabel = durationVariance >= 0.0
                ? Math.Abs(durationVariance).ToString("0.##", CultureInfo.InvariantCulture) + "% Ahead"
                : Math.Abs(durationVariance).ToString("0.##", CultureInfo.InvariantCulture) + "% Behind";
            string costVarianceLabel = costVariancePct >= 0.0
                ? Math.Abs(costVariancePct).ToString("0.##", CultureInfo.InvariantCulture) + "% Ahead"
                : Math.Abs(costVariancePct).ToString("0.##", CultureInfo.InvariantCulture) + "% Behind";

            if (pmKpiTotalBaselineCostText != null)
            {
                pmKpiTotalBaselineCostText.Text = "$" + bac.ToString("N2", CultureInfo.InvariantCulture);
            }
            if (pmKpiPlannedDurationText != null)
            {
                pmKpiPlannedDurationText.Text = plannedDurationPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
            if (pmKpiActualDurationText != null)
            {
                pmKpiActualDurationText.Text = actualDurationPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
            if (pmKpiVarianceDurationText != null)
            {
                pmKpiVarianceDurationText.Text = durationVarianceLabel;
            }
            if (pmKpiPlannedCostText != null)
            {
                pmKpiPlannedCostText.Text = plannedCostPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
            if (pmKpiActualCostText != null)
            {
                pmKpiActualCostText.Text = actualCostPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
            if (pmKpiVarianceCostText != null)
            {
                pmKpiVarianceCostText.Text = costVarianceLabel;
            }

            double cpi = ac > 1e-9 ? (ev / ac) : 0.0;
            double spi = pv > 1e-9 ? (ev / pv) : 0.0;

            if (pmFinancialProgressText != null)
            {
                pmFinancialProgressText.Text =
                    "Financial Progress Data" + Environment.NewLine +
                    "Budget At Completion (BAC)   " + "$" + bac.ToString("N2", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Planned Value (PV)           " + "$" + pv.ToString("N2", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Actual Cost (AC)             " + "$" + ac.ToString("N2", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Earned Value (EV)            " + "$" + ev.ToString("N2", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Remaining Cost               " + "$" + remainingCost.ToString("N2", CultureInfo.InvariantCulture);
            }

            if (pmCurrentProgressText != null)
            {
                pmCurrentProgressText.Text =
                    "Current Progress Data" + Environment.NewLine +
                    "Total " + itemLabel + "               " + totalItems.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Completed " + itemLabel + "           " + completedItems.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "In Progress " + itemLabel + "         " + inProgressItems.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Not Started " + itemLabel + "         " + notStartedItems.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Data Date                    " + dataDateDisplay;
            }

            if (pmSummaryProgressText != null)
            {
                pmSummaryProgressText.Text =
                    "Summary Progress" + Environment.NewLine +
                    "Planned Completion (Dur.%)   " + plannedDurationPct.ToString("0.##", CultureInfo.InvariantCulture) + "%" + Environment.NewLine +
                    "Actual Completion (Dur.%)    " + actualDurationPct.ToString("0.##", CultureInfo.InvariantCulture) + "%" + Environment.NewLine +
                    "Complete Variance (Dur.%)    " + durationVariance.ToString("0.##", CultureInfo.InvariantCulture) + "%" + Environment.NewLine +
                    "Planned Completion (Cost%)   " + plannedCostPct.ToString("0.##", CultureInfo.InvariantCulture) + "%" + Environment.NewLine +
                    "Actual Completion (Cost%)    " + actualCostPct.ToString("0.##", CultureInfo.InvariantCulture) + "%" + Environment.NewLine +
                    "Complete Variance (Cost%)    " + costVariancePct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }

            if (pmPerformanceText != null)
            {
                pmPerformanceText.Text =
                    "Project Performance" + Environment.NewLine +
                    "CPI                         " + cpi.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "SPI                         " + spi.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Progress (Cost%)            " + actualCostPct.ToString("0.##", CultureInfo.InvariantCulture) + "%" + Environment.NewLine +
                    "Progress (Dur.%)            " + actualDurationPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
        }

        private List<PmDashboardCurvePoint> BuildPmDashboardCurvePoints(
            PmDashboardLiveSnapshot liveSnapshot)
        {
            if (liveSnapshot != null && liveSnapshot.Tasks.Count > 0)
            {
                return BuildPmDashboardCurvePointsFromMsProject(liveSnapshot);
            }

            return new List<PmDashboardCurvePoint>();
        }

        private List<PmDashboardCurvePoint> BuildPmDashboardCurvePointsFromMsProject(PmDashboardLiveSnapshot liveSnapshot)
        {
            var pmSCurveTypeCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardSCurveTypeCombo");
            var pmSCurveViewCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardSCurveViewCombo");
            var pmSCurveValueCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardSCurveValueCombo");

            string metricType = GetPmDashboardComboValue(pmSCurveTypeCombo, "Cost");
            string viewMode = GetPmDashboardComboValue(pmSCurveViewCombo, "Monthly");
            bool percentMode = string.Equals(
                GetPmDashboardComboValue(pmSCurveValueCombo, "Amount"),
                "Percent",
                StringComparison.OrdinalIgnoreCase);

            PmDashboardTimeGranularity granularity = ParsePmDashboardGranularity(viewMode);
            return BuildPmDashboardCurvePointsFromSnapshot(
                liveSnapshot,
                metricType,
                granularity,
                percentMode);
        }

        private List<PmDashboardCurvePoint> BuildPmDashboardCurvePointsFromSnapshot(
            PmDashboardLiveSnapshot liveSnapshot,
            string metricType,
            PmDashboardTimeGranularity granularity,
            bool percentMode)
        {
            if (liveSnapshot == null || liveSnapshot.Tasks.Count == 0)
            {
                return new List<PmDashboardCurvePoint>();
            }

            DateTime projectStart = liveSnapshot.ProjectStart.Date;
            DateTime projectFinish = liveSnapshot.ProjectFinish.Date;
            DateTime statusDate = liveSnapshot.StatusDate.Date;

            if (!IsValidPmDashboardProjectDate(projectStart) ||
                !IsValidPmDashboardProjectDate(projectFinish) ||
                projectFinish < projectStart)
            {
                projectStart = statusDate;
                projectFinish = statusDate;
            }

            List<PmDashboardTimeBucket> buckets = BuildPmDashboardBuckets(granularity, projectStart, projectFinish);
            if (buckets.Count == 0)
            {
                return new List<PmDashboardCurvePoint>();
            }

            double[] plannedPeriod = new double[buckets.Count];
            double[] earnedPeriod = new double[buckets.Count];
            double[] actualCostPeriod = new double[buckets.Count];
            double[] forecastPeriod = new double[buckets.Count];

            foreach (PmDashboardTaskSnapshot task in liveSnapshot.Tasks)
            {
                if (task == null)
                {
                    continue;
                }

                DateTime planStart = task.PlanStart;
                DateTime planFinish = task.PlanFinish;
                DateTime actualStart = task.ActualStart;
                DateTime actualFinish = task.ActualFinish;
                DateTime taskStart = task.TaskStart;
                DateTime taskFinish = task.TaskFinish;

                if (!IsValidPmDashboardProjectDate(taskStart) || !IsValidPmDashboardProjectDate(taskFinish) || taskFinish < taskStart)
                {
                    continue;
                }
                if (!IsValidPmDashboardProjectDate(planStart) || !IsValidPmDashboardProjectDate(planFinish) || planFinish < planStart)
                {
                    planStart = taskStart;
                    planFinish = taskFinish;
                }
                if (!IsValidPmDashboardProjectDate(actualStart) || !IsValidPmDashboardProjectDate(actualFinish) || actualFinish < actualStart)
                {
                    actualStart = taskStart;
                    actualFinish = taskFinish;
                }

                double plannedTotal;
                double earnedToDate;
                double actualCostToDate;
                double forecastTotal;

                if (string.Equals(metricType, "Duration", StringComparison.OrdinalIgnoreCase))
                {
                    plannedTotal = Math.Max(0.0, task.BaselineDurationDays);
                    if (plannedTotal <= 1e-9)
                    {
                        plannedTotal = Math.Max(0.0, (planFinish - planStart).TotalDays + 1.0);
                    }

                    earnedToDate = plannedTotal * Math.Max(0.0, Math.Min(100.0, task.PercentComplete)) / 100.0;
                    actualCostToDate = Math.Max(0.0, task.ActualDurationDays);
                    if (actualCostToDate <= 1e-9)
                    {
                        actualCostToDate = earnedToDate;
                    }

                    forecastTotal = actualCostToDate + Math.Max(0.0, task.RemainingDurationDays);
                    if (forecastTotal <= 1e-9)
                    {
                        forecastTotal = Math.Max(plannedTotal, actualCostToDate);
                    }
                }
                else if (string.Equals(metricType, "Resource", StringComparison.OrdinalIgnoreCase))
                {
                    plannedTotal = Math.Max(0.0, task.BaselineWorkDays);
                    if (plannedTotal <= 1e-9)
                    {
                        plannedTotal = Math.Max(0.0, task.BaselineDurationDays);
                    }

                    earnedToDate = plannedTotal * Math.Max(0.0, Math.Min(100.0, task.PercentComplete)) / 100.0;
                    actualCostToDate = Math.Max(0.0, task.ActualWorkDays);
                    if (actualCostToDate <= 1e-9)
                    {
                        actualCostToDate = earnedToDate;
                    }

                    forecastTotal = actualCostToDate + Math.Max(0.0, task.RemainingWorkDays);
                    if (forecastTotal <= 1e-9)
                    {
                        forecastTotal = Math.Max(plannedTotal, actualCostToDate);
                    }
                }
                else
                {
                    plannedTotal = Math.Max(0.0, task.BaselineCost);
                    if (plannedTotal <= 1e-9)
                    {
                        plannedTotal = Math.Max(task.BcwsToDate, Math.Max(task.BcwpToDate, task.AcwpToDate));
                    }

                    earnedToDate = Math.Max(0.0, task.BcwpToDate);
                    if (earnedToDate <= 1e-9)
                    {
                        earnedToDate = plannedTotal * Math.Max(0.0, Math.Min(100.0, task.PercentComplete)) / 100.0;
                    }

                    actualCostToDate = Math.Max(0.0, task.AcwpToDate);
                    if (actualCostToDate <= 1e-9)
                    {
                        actualCostToDate = Math.Max(0.0, task.ActualCostToDate);
                    }

                    forecastTotal = Math.Max(0.0, task.ActualCostToDate + task.RemainingCost);
                    if (forecastTotal <= 1e-9)
                    {
                        forecastTotal = Math.Max(plannedTotal, Math.Max(earnedToDate, actualCostToDate));
                    }
                }

                if (plannedTotal > 1e-9)
                {
                    AllocatePmDashboardValueToBuckets(planStart, planFinish, plannedTotal, buckets, plannedPeriod);
                }

                if (earnedToDate > 1e-9)
                {
                    DateTime earnedFinish = planFinish < statusDate ? planFinish : statusDate;
                    if (earnedFinish >= planStart)
                    {
                        AllocatePmDashboardValueToBuckets(planStart, earnedFinish, earnedToDate, buckets, earnedPeriod);
                    }
                }

                if (actualCostToDate > 1e-9)
                {
                    DateTime actualFinishToDate = actualFinish < statusDate ? actualFinish : statusDate;
                    if (actualFinishToDate >= actualStart)
                    {
                        AllocatePmDashboardValueToBuckets(actualStart, actualFinishToDate, actualCostToDate, buckets, actualCostPeriod);
                    }
                }

                if (forecastTotal > 1e-9)
                {
                    AllocatePmDashboardValueToBuckets(taskStart, taskFinish, forecastTotal, buckets, forecastPeriod);
                }
            }

            var points = new List<PmDashboardCurvePoint>(buckets.Count);
            double cumulativePlanned = 0.0;
            double cumulativeEarned = 0.0;
            double cumulativeActualCost = 0.0;
            double cumulativeForecast = 0.0;

            int statusIndex = GetPmDashboardBucketIndex(buckets, statusDate);
            for (int i = 0; i < buckets.Count; i++)
            {
                cumulativePlanned += plannedPeriod[i];
                cumulativeEarned += earnedPeriod[i];
                cumulativeActualCost += actualCostPeriod[i];
                cumulativeForecast += forecastPeriod[i];

                double forecastDisplay = i <= statusIndex ? cumulativeActualCost : cumulativeForecast;
                if (forecastDisplay < cumulativeActualCost)
                {
                    forecastDisplay = cumulativeActualCost;
                }

                points.Add(new PmDashboardCurvePoint
                {
                    Label = buckets[i].Label,
                    BucketStart = buckets[i].Start,
                    BucketEnd = buckets[i].End,
                    PlannedCumulative = cumulativePlanned,
                    ActualCumulative = cumulativeEarned,
                    ActualCostCumulative = cumulativeActualCost,
                    ForecastCumulative = forecastDisplay,
                    PeriodPlanned = plannedPeriod[i],
                    PeriodActual = earnedPeriod[i],
                    PeriodActualCost = actualCostPeriod[i]
                });
            }

            if (percentMode)
            {
                double denominator = points.Count == 0 ? 0.0 : points[points.Count - 1].PlannedCumulative;
                if (denominator <= 1e-9)
                {
                    denominator = points.Count == 0 ? 0.0 : points[points.Count - 1].ForecastCumulative;
                }

                if (denominator > 1e-9)
                {
                    foreach (PmDashboardCurvePoint point in points)
                    {
                        point.PlannedCumulative = (point.PlannedCumulative / denominator) * 100.0;
                        point.ActualCumulative = (point.ActualCumulative / denominator) * 100.0;
                        point.ActualCostCumulative = (point.ActualCostCumulative / denominator) * 100.0;
                        point.ForecastCumulative = (point.ForecastCumulative / denominator) * 100.0;
                        point.PeriodPlanned = (point.PeriodPlanned / denominator) * 100.0;
                        point.PeriodActual = (point.PeriodActual / denominator) * 100.0;
                        point.PeriodActualCost = (point.PeriodActualCost / denominator) * 100.0;
                    }
                }
            }

            return points;
        }

        private static PmDashboardTimeGranularity ParsePmDashboardGranularity(string viewMode)
        {
            if (string.Equals(viewMode, "Daily", StringComparison.OrdinalIgnoreCase))
            {
                return PmDashboardTimeGranularity.Daily;
            }
            if (string.Equals(viewMode, "Week", StringComparison.OrdinalIgnoreCase))
            {
                return PmDashboardTimeGranularity.Weekly;
            }

            return PmDashboardTimeGranularity.Monthly;
        }

        private static List<PmDashboardTimeBucket> BuildPmDashboardBuckets(
            PmDashboardTimeGranularity granularity,
            DateTime start,
            DateTime finish)
        {
            var buckets = new List<PmDashboardTimeBucket>();
            DateTime rangeStart = start.Date;
            DateTime rangeFinish = finish.Date;
            DateTime cursor;

            switch (granularity)
            {
                case PmDashboardTimeGranularity.Daily:
                    cursor = rangeStart;
                    while (cursor <= rangeFinish)
                    {
                        buckets.Add(new PmDashboardTimeBucket
                        {
                            Start = cursor,
                            End = cursor,
                            Label = cursor.ToString("dd MMM yy", CultureInfo.InvariantCulture)
                        });
                        cursor = cursor.AddDays(1);
                    }
                    break;
                case PmDashboardTimeGranularity.Weekly:
                    int dayOffset = ((int)rangeStart.DayOfWeek + 6) % 7;
                    cursor = rangeStart.AddDays(-dayOffset);
                    while (cursor <= rangeFinish)
                    {
                        DateTime end = cursor.AddDays(6);
                        DateTime bucketStart = cursor < rangeStart ? rangeStart : cursor;
                        DateTime bucketEnd = end > rangeFinish ? rangeFinish : end;
                        if (bucketEnd < bucketStart)
                        {
                            cursor = cursor.AddDays(7);
                            continue;
                        }

                        buckets.Add(new PmDashboardTimeBucket
                        {
                            Start = bucketStart,
                            End = bucketEnd,
                            Label = bucketStart.ToString("dd MMM", CultureInfo.InvariantCulture) + " - " +
                                    bucketEnd.ToString("dd MMM", CultureInfo.InvariantCulture)
                        });
                        cursor = cursor.AddDays(7);
                    }
                    break;
                default:
                    cursor = new DateTime(rangeStart.Year, rangeStart.Month, 1);
                    DateTime monthEnd = new DateTime(rangeFinish.Year, rangeFinish.Month, 1);
                    while (cursor <= monthEnd)
                    {
                        DateTime end = cursor.AddMonths(1).AddDays(-1);
                        DateTime bucketStart = cursor < rangeStart ? rangeStart : cursor;
                        DateTime bucketEnd = end > rangeFinish ? rangeFinish : end;
                        if (bucketEnd < bucketStart)
                        {
                            cursor = cursor.AddMonths(1);
                            continue;
                        }

                        buckets.Add(new PmDashboardTimeBucket
                        {
                            Start = bucketStart,
                            End = bucketEnd,
                            Label = cursor.ToString("MMM yy", CultureInfo.InvariantCulture)
                        });
                        cursor = cursor.AddMonths(1);
                    }
                    break;
            }

            return buckets;
        }

        private static void AllocatePmDashboardValueToBuckets(
            DateTime start,
            DateTime finish,
            double totalValue,
            List<PmDashboardTimeBucket> buckets,
            double[] target)
        {
            if (buckets == null || target == null || buckets.Count == 0 || target.Length < buckets.Count)
            {
                return;
            }

            int totalDays = (int)(finish.Date - start.Date).TotalDays + 1;
            if (totalValue <= 1e-9 || totalDays <= 0)
            {
                return;
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                int overlapDays = GetPmDashboardOverlapDays(start, finish, buckets[i].Start, buckets[i].End);
                if (overlapDays <= 0)
                {
                    continue;
                }

                target[i] += totalValue * overlapDays / totalDays;
            }
        }

        private static int GetPmDashboardOverlapDays(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
        {
            DateTime start = aStart > bStart ? aStart : bStart;
            DateTime end = aEnd < bEnd ? aEnd : bEnd;
            if (end < start)
            {
                return 0;
            }

            return (int)(end.Date - start.Date).TotalDays + 1;
        }

        private static int GetPmDashboardBucketIndex(List<PmDashboardTimeBucket> buckets, DateTime target)
        {
            if (buckets == null || buckets.Count == 0)
            {
                return 0;
            }

            int best = 0;
            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Start <= target.Date)
                {
                    best = i;
                }
                else
                {
                    break;
                }
            }

            return best;
        }

        private static double GetPmDashboardDurationDays(object durationValue, double minutesPerDay)
        {
            double minutes = ConvertToDoubleSafe(durationValue);
            if (minutesPerDay <= 0.0)
            {
                minutesPerDay = 480.0;
            }

            return Math.Max(0.0, minutes / minutesPerDay);
        }

        private static bool IsValidPmDashboardProjectDate(DateTime date)
        {
            return date > new DateTime(1899, 12, 30);
        }

        private PmDashboardLiveSnapshot TryBuildPmDashboardLiveSnapshot()
        {
            object appObj = null;
            object projectObj = null;
            object tasksObj = null;
            object summaryTaskObj = null;

            try
            {
                appObj = Marshal.GetActiveObject(MsProjectApplicationProgId);
                if (appObj == null)
                {
                    return null;
                }

                EnsureMsProjectTaskMetadataFields(appObj);

                projectObj = ReadPmDashboardComProperty(appObj, "ActiveProject");
                if (projectObj == null)
                {
                    return null;
                }

                DateTime statusDate = ReadPmDashboardComDate(projectObj, "StatusDate", DateTime.Now);
                if (!IsValidPmDashboardProjectDate(statusDate))
                {
                    statusDate = DateTime.Now.Date;
                }

                DateTime projectStart = ReadPmDashboardComDate(projectObj, "ProjectStart", statusDate);
                DateTime projectFinish = ReadPmDashboardComDate(projectObj, "ProjectFinish", statusDate);
                if (!IsValidPmDashboardProjectDate(projectStart) ||
                    !IsValidPmDashboardProjectDate(projectFinish) ||
                    projectFinish < projectStart)
                {
                    projectStart = statusDate.Date;
                    projectFinish = statusDate.Date;
                }

                double minutesPerDay = ReadPmDashboardComDouble(projectObj, "MinutesPerDay");
                if (minutesPerDay <= 0.0)
                {
                    minutesPerDay = ReadPmDashboardComDouble(appObj, "MinutesPerDay");
                }
                if (minutesPerDay <= 0.0)
                {
                    minutesPerDay = 480.0;
                }

                var snapshot = new PmDashboardLiveSnapshot
                {
                    StatusDate = statusDate.Date,
                    ProjectStart = projectStart.Date,
                    ProjectFinish = projectFinish.Date
                };

                string projectNameRaw =
                    Convert.ToString(ReadPmDashboardComProperty(projectObj, "Name"), CultureInfo.InvariantCulture) ?? "";
                string projectPathRaw =
                    Convert.ToString(ReadPmDashboardComProperty(projectObj, "FullName"), CultureInfo.InvariantCulture) ?? "";
                string summaryTaskNameRaw = "";
                summaryTaskObj = ReadPmDashboardComProperty(projectObj, "ProjectSummaryTask");
                if (summaryTaskObj != null)
                {
                    summaryTaskNameRaw =
                        Convert.ToString(ReadPmDashboardComProperty(summaryTaskObj, "Name"), CultureInfo.InvariantCulture) ?? "";
                }

                snapshot.ProjectName = ResolveMsProjectDisplayName(summaryTaskNameRaw, projectNameRaw, projectPathRaw);

                tasksObj = ReadPmDashboardComProperty(projectObj, "Tasks");
                if (tasksObj == null)
                {
                    return null;
                }

                foreach (object rawTask in (dynamic)tasksObj)
                {
                    object taskObj = rawTask;
                    try
                    {
                        if (taskObj == null || ReadPmDashboardComBool(taskObj, "Summary"))
                        {
                            continue;
                        }

                        DateTime taskStart = ReadPmDashboardComDate(taskObj, "Start", snapshot.ProjectStart);
                        DateTime taskFinish = ReadPmDashboardComDate(taskObj, "Finish", snapshot.ProjectFinish);
                        if (!IsValidPmDashboardProjectDate(taskStart) ||
                            !IsValidPmDashboardProjectDate(taskFinish) ||
                            taskFinish < taskStart)
                        {
                            continue;
                        }

                        DateTime baselineStart = ReadPmDashboardComDate(taskObj, "BaselineStart", taskStart);
                        DateTime baselineFinish = ReadPmDashboardComDate(taskObj, "BaselineFinish", taskFinish);
                        DateTime planStart = IsValidPmDashboardProjectDate(baselineStart) ? baselineStart : taskStart;
                        DateTime planFinish = IsValidPmDashboardProjectDate(baselineFinish) ? baselineFinish : taskFinish;
                        if (planFinish < planStart)
                        {
                            planStart = taskStart;
                            planFinish = taskFinish;
                        }

                        DateTime actualStart = ReadPmDashboardComDate(taskObj, "ActualStart", taskStart);
                        DateTime actualFinish = ReadPmDashboardComDate(taskObj, "ActualFinish", taskFinish);
                        if (actualFinish < actualStart)
                        {
                            actualFinish = actualStart;
                        }

                        int taskId = Math.Max(0, ReadPmDashboardComInt(taskObj, "ID"));
                        int taskUniqueId = Math.Max(0, ReadPmDashboardComInt(taskObj, "UniqueID"));
                        string taskName = (Convert.ToString(
                            ReadPmDashboardComProperty(taskObj, "Name"),
                            CultureInfo.InvariantCulture) ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(taskName))
                        {
                            taskName = taskId > 0
                                ? "Task " + taskId.ToString(CultureInfo.InvariantCulture)
                                : "Task";
                        }
                        string taskLinkKey = ReadPmDashboardTaskLinkKey(taskObj, taskName);
                        string taskStructureElement =
                            (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldStructureElement), CultureInfo.InvariantCulture) ?? "").Trim();
                        string taskBuildingLevel =
                            (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldBuildingLevel), CultureInfo.InvariantCulture) ?? "").Trim();
                        string taskStructureType =
                            (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldStructureType), CultureInfo.InvariantCulture) ?? "").Trim();
                        string taskRevitElementIds = NormalizePmUpdateProgressElementIdsText(
                            Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldRevitElementIds), CultureInfo.InvariantCulture) ?? "");
                        string taskUnit =
                            (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldUnit), CultureInfo.InvariantCulture) ?? "").Trim();
                        double taskBoq =
                            RoundPmBoqValue(Math.Max(0.0, ConvertToDoubleSafe(ReadPmDashboardComProperty(taskObj, MsProjectTaskNumberFieldBoq))));
                        if (!string.IsNullOrWhiteSpace(taskLinkKey))
                        {
                            ParsePmUpdateProgressTaskKeyParts(taskLinkKey, out string keyStructure, out string keyLevel, out string keyType);
                            if (string.IsNullOrWhiteSpace(taskStructureElement))
                            {
                                taskStructureElement = keyStructure;
                                WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldStructureElement, keyStructure);
                            }
                            if (string.IsNullOrWhiteSpace(taskBuildingLevel))
                            {
                                taskBuildingLevel = keyLevel;
                                WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldBuildingLevel, keyLevel);
                            }
                            if (string.IsNullOrWhiteSpace(taskStructureType))
                            {
                                taskStructureType = keyType;
                                WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldStructureType, keyType);
                            }
                        }

                        if (string.IsNullOrWhiteSpace(taskStructureType))
                        {
                            string stageFromTaskName = ResolvePmStageDisplayNameFromTaskText(taskName);
                            if (!string.IsNullOrWhiteSpace(stageFromTaskName))
                            {
                                taskStructureType = stageFromTaskName;
                                WritePmDashboardTaskTextFieldIfNeeded(taskObj, MsProjectTaskTextFieldStructureType, stageFromTaskName);
                            }
                        }

                        double percentComplete = Math.Max(0.0, Math.Min(100.0, ReadPmDashboardComDouble(taskObj, "PercentComplete")));
                        double baselineCost = Math.Max(0.0, ReadPmDashboardComDouble(taskObj, "BaselineCost"));
                        double bcws = Math.Max(0.0, ReadPmDashboardComDouble(taskObj, "BCWS"));
                        double bcwp = Math.Max(0.0, ReadPmDashboardComDouble(taskObj, "BCWP"));
                        double acwp = Math.Max(0.0, ReadPmDashboardComDouble(taskObj, "ACWP"));
                        double actualCost = Math.Max(0.0, ReadPmDashboardComDouble(taskObj, "ActualCost"));
                        double remainingCost = Math.Max(0.0, ReadPmDashboardComDouble(taskObj, "RemainingCost"));

                        if (baselineCost <= 1e-9)
                        {
                            baselineCost = Math.Max(Math.Max(bcws, bcwp), Math.Max(acwp, actualCost + remainingCost));
                        }

                        double baselineDurationDays = GetPmDashboardDurationDays(ReadPmDashboardComProperty(taskObj, "BaselineDuration"), minutesPerDay);
                        if (baselineDurationDays <= 1e-9)
                        {
                            baselineDurationDays = Math.Max(0.0, (planFinish - planStart).TotalDays + 1.0);
                        }

                        double actualDurationDays = GetPmDashboardDurationDays(ReadPmDashboardComProperty(taskObj, "ActualDuration"), minutesPerDay);
                        if (actualDurationDays <= 1e-9)
                        {
                            DateTime actualToDate = actualFinish < snapshot.StatusDate ? actualFinish : snapshot.StatusDate;
                            if (actualToDate >= actualStart)
                            {
                                actualDurationDays = (actualToDate - actualStart).TotalDays + 1.0;
                            }
                            else
                            {
                                actualDurationDays = baselineDurationDays * percentComplete / 100.0;
                            }
                        }
                        if (actualDurationDays < 0.0)
                        {
                            actualDurationDays = 0.0;
                        }

                        double remainingDurationDays = Math.Max(0.0, GetPmDashboardDurationDays(ReadPmDashboardComProperty(taskObj, "RemainingDuration"), minutesPerDay));
                        double baselineWorkDays = Math.Max(0.0, GetPmDashboardDurationDays(ReadPmDashboardComProperty(taskObj, "BaselineWork"), minutesPerDay));
                        if (baselineWorkDays <= 1e-9)
                        {
                            baselineWorkDays = baselineDurationDays;
                        }

                        double actualWorkDays = Math.Max(0.0, GetPmDashboardDurationDays(ReadPmDashboardComProperty(taskObj, "ActualWork"), minutesPerDay));
                        if (actualWorkDays <= 1e-9)
                        {
                            actualWorkDays = baselineWorkDays * percentComplete / 100.0;
                        }

                        double remainingWorkDays = Math.Max(0.0, GetPmDashboardDurationDays(ReadPmDashboardComProperty(taskObj, "RemainingWork"), minutesPerDay));
                        if (remainingWorkDays <= 1e-9 && baselineWorkDays > actualWorkDays)
                        {
                            remainingWorkDays = baselineWorkDays - actualWorkDays;
                        }

                        snapshot.Tasks.Add(new PmDashboardTaskSnapshot
                        {
                            TaskId = taskId,
                            TaskUniqueId = taskUniqueId,
                            TaskName = taskName,
                            TaskLinkKey = taskLinkKey,
                            TaskStructureElement = NormalizePmUpdateProgressTaskKeyPart(taskStructureElement),
                            TaskBuildingLevel = NormalizePmUpdateProgressTaskKeyPart(taskBuildingLevel),
                            TaskStructureType = NormalizePmUpdateProgressTaskKeyPart(taskStructureType),
                            TaskRevitElementIds = taskRevitElementIds,
                            TaskUnit = taskUnit,
                            TaskBoq = taskBoq,
                            TaskStart = taskStart.Date,
                            TaskFinish = taskFinish.Date,
                            PlanStart = planStart.Date,
                            PlanFinish = planFinish.Date,
                            ActualStart = actualStart.Date,
                            ActualFinish = actualFinish.Date,
                            PercentComplete = percentComplete,
                            BaselineCost = baselineCost,
                            BcwsToDate = bcws,
                            BcwpToDate = bcwp,
                            AcwpToDate = acwp,
                            ActualCostToDate = actualCost,
                            RemainingCost = remainingCost,
                            BaselineDurationDays = baselineDurationDays,
                            ActualDurationDays = actualDurationDays,
                            RemainingDurationDays = remainingDurationDays,
                            BaselineWorkDays = baselineWorkDays,
                            ActualWorkDays = actualWorkDays,
                            RemainingWorkDays = remainingWorkDays
                        });

                        snapshot.TotalTasks++;
                        if (percentComplete >= 99.99)
                        {
                            snapshot.CompletedTasks++;
                        }
                        else if (percentComplete > 0.01)
                        {
                            snapshot.InProgressTasks++;
                        }

                        snapshot.Bac += baselineCost;
                        snapshot.PvToDate += bcws;
                        snapshot.EvToDate += bcwp;
                        snapshot.AcToDate += acwp > 1e-9 ? acwp : actualCost;
                        snapshot.RemainingCost += remainingCost;

                        snapshot.BaselineDurationTotalDays += baselineDurationDays;
                        snapshot.EarnedDurationToDateDays += baselineDurationDays * percentComplete / 100.0;
                        if (baselineDurationDays > 1e-9 && planFinish >= planStart)
                        {
                            double calendarTotalDays = Math.Max(1.0, (planFinish - planStart).TotalDays + 1.0);
                            double elapsedDays;
                            if (snapshot.StatusDate < planStart)
                            {
                                elapsedDays = 0.0;
                            }
                            else if (snapshot.StatusDate > planFinish)
                            {
                                elapsedDays = calendarTotalDays;
                            }
                            else
                            {
                                elapsedDays = (snapshot.StatusDate - planStart).TotalDays + 1.0;
                            }

                            elapsedDays = Math.Max(0.0, Math.Min(calendarTotalDays, elapsedDays));
                            snapshot.PlannedDurationToDateDays += baselineDurationDays * (elapsedDays / calendarTotalDays);
                        }

                        if (taskStart < snapshot.ProjectStart)
                        {
                            snapshot.ProjectStart = taskStart.Date;
                        }
                        if (taskFinish > snapshot.ProjectFinish)
                        {
                            snapshot.ProjectFinish = taskFinish.Date;
                        }
                    }
                    finally
                    {
                        SafeReleaseCom(taskObj);
                    }
                }

                snapshot.NotStartedTasks = Math.Max(0, snapshot.TotalTasks - snapshot.CompletedTasks - snapshot.InProgressTasks);
                if (snapshot.Bac <= 1e-9)
                {
                    snapshot.Bac = Math.Max(snapshot.PvToDate, Math.Max(snapshot.EvToDate, snapshot.AcToDate + snapshot.RemainingCost));
                }

                return snapshot.TotalTasks > 0 ? snapshot : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                SafeReleaseCom(summaryTaskObj);
                SafeReleaseCom(tasksObj);
                SafeReleaseCom(projectObj);
                SafeReleaseCom(appObj);
            }
        }

        private static object ReadPmDashboardComProperty(object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            try
            {
                return target.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public,
                    null,
                    target,
                    null);
            }
            catch
            {
                return null;
            }
        }

        private static double ReadPmDashboardComDouble(object target, string propertyName)
        {
            return ConvertToDoubleSafe(ReadPmDashboardComProperty(target, propertyName));
        }

        private static int ReadPmDashboardComInt(object target, string propertyName)
        {
            object value = ReadPmDashboardComProperty(target, propertyName);
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }

            try
            {
                if (value is int i)
                {
                    return i;
                }

                if (value is short s)
                {
                    return s;
                }

                if (value is long l)
                {
                    if (l > int.MaxValue) return int.MaxValue;
                    if (l < int.MinValue) return int.MinValue;
                    return (int)l;
                }

                if (value is double d)
                {
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        return 0;
                    }

                    if (d > int.MaxValue) return int.MaxValue;
                    if (d < int.MinValue) return int.MinValue;
                    return Convert.ToInt32(Math.Round(d, MidpointRounding.AwayFromZero));
                }

                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
                {
                    if (numeric > int.MaxValue) return int.MaxValue;
                    if (numeric < int.MinValue) return int.MinValue;
                    return Convert.ToInt32(Math.Round(numeric, MidpointRounding.AwayFromZero));
                }
            }
            catch
            {
                // Ignore and return zero.
            }

            return 0;
        }

        private static bool ReadPmDashboardComBool(object target, string propertyName)
        {
            object value = ReadPmDashboardComProperty(target, propertyName);
            if (value is bool b)
            {
                return b;
            }

            string text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Trim();
            if (bool.TryParse(text, out b))
            {
                return b;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
            {
                return numeric > 0.0;
            }

            return false;
        }

        private static DateTime ReadPmDashboardComDate(object target, string propertyName, DateTime fallback)
        {
            object value = ReadPmDashboardComProperty(target, propertyName);
            if (TryConvertComDateToLocal(value, out DateTime dateLocal) && IsValidPmDashboardProjectDate(dateLocal))
            {
                return dateLocal.Date;
            }

            return fallback.Date;
        }

        private static string ReadPmDashboardTaskLinkKey(object taskObj, string taskName)
        {
            string[] candidates =
            {
                MsProjectTaskTextFieldBuildingLevel,
                MsProjectTaskTextFieldStructureElement,
                MsProjectTaskTextFieldStructureType,
                "Text10",
                "Text9",
                "Text8",
                "Text7",
                "Text6",
                "Text5",
                "Text4",
                "Text3",
                "Text2",
                "Text1"
            };

            foreach (string field in candidates)
            {
                string raw = (Convert.ToString(ReadPmDashboardComProperty(taskObj, field), CultureInfo.InvariantCulture) ?? "").Trim();
                string key = NormalizePmTaskKey(raw);
                if (!string.IsNullOrWhiteSpace(key) && key.IndexOf('|') >= 0)
                {
                    return key;
                }
            }

            string structure = (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldStructureElement), CultureInfo.InvariantCulture) ?? "").Trim();
            string level = (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldBuildingLevel), CultureInfo.InvariantCulture) ?? "").Trim();
            string type = (Convert.ToString(ReadPmDashboardComProperty(taskObj, MsProjectTaskTextFieldStructureType), CultureInfo.InvariantCulture) ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(structure) ||
                !string.IsNullOrWhiteSpace(level) ||
                !string.IsNullOrWhiteSpace(type))
            {
                string key = BuildBoqKey(structure, level, type);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }
            }

            string fromName = NormalizePmTaskKey(taskName);
            return fromName.IndexOf('|') >= 0 ? fromName : "";
        }

        private void RenderPmDashboardSCurvePlot()
        {
            var pmPlotView = GetPmDashboardNamedControl<OxyPlot.Wpf.PlotView>("PmDashboardSCurvePlotView");
            var pmSCurveTypeCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardSCurveTypeCombo");
            var pmSCurveViewCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardSCurveViewCombo");
            var pmSCurveValueCombo = GetPmDashboardNamedControl<System.Windows.Controls.ComboBox>("PmDashboardSCurveValueCombo");
            var pmShowSCurveCheck = GetPmDashboardNamedControl<CheckBox>("PmDashboardShowSCurveCheck");
            var pmShowColumnChartCheck = GetPmDashboardNamedControl<CheckBox>("PmDashboardShowColumnChartCheck");

            if (pmPlotView == null)
            {
                return;
            }

            string metricType = GetPmDashboardComboValue(pmSCurveTypeCombo, "Cost");
            string viewMode = GetPmDashboardComboValue(pmSCurveViewCombo, "Monthly");
            bool percentMode = string.Equals(
                GetPmDashboardComboValue(pmSCurveValueCombo, "Amount"),
                "Percent",
                StringComparison.OrdinalIgnoreCase);

            bool showSCurve = pmShowSCurveCheck?.IsChecked != false;
            bool showColumns = pmShowColumnChartCheck?.IsChecked != false;
            if (!showSCurve && !showColumns)
            {
                showSCurve = true;
            }

            var model = new PlotModel
            {
                Title = "S-Curve (" + metricType + ")",
                Subtitle = "View: " + viewMode + " | Value: " + (percentMode ? "Percent" : "Amount"),
                PlotMargins = new OxyThickness(64, 26, 20, 46),
                PlotAreaBorderColor = OxyColor.Parse("#D9E0EC"),
                PlotAreaBorderThickness = new OxyThickness(1)
            };

            var xAxis = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                GapWidth = _pmDashboardCurvePoints.Count <= 8 ? 0.08 : 0.2,
                Angle = _pmDashboardCurvePoints.Count > 12 ? 35 : 0,
                TextColor = OxyColor.Parse("#2E3B4E"),
                IsPanEnabled = false,
                IsZoomEnabled = false
            };
            foreach (PmDashboardCurvePoint point in _pmDashboardCurvePoints)
            {
                xAxis.Labels.Add(point.Label ?? "");
            }
            model.Axes.Add(xAxis);

            double yMax = 1.0;
            foreach (PmDashboardCurvePoint point in _pmDashboardCurvePoints)
            {
                if (point == null)
                {
                    continue;
                }

                double[] values =
                {
                    point.PlannedCumulative,
                    point.ActualCumulative,
                    point.ActualCostCumulative,
                    point.ForecastCumulative,
                    point.PeriodPlanned,
                    point.PeriodActual,
                    point.PeriodActualCost
                };

                foreach (double value in values)
                {
                    if (double.IsNaN(value) || double.IsInfinity(value))
                    {
                        continue;
                    }

                    if (value > yMax)
                    {
                        yMax = value;
                    }
                }
            }
            if (yMax < 1.0) yMax = 1.0;
            yMax *= 1.1;

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0,
                Maximum = yMax,
                MinimumPadding = 0,
                MaximumPadding = 0.04,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.Parse("#D9E0EC"),
                MinorGridlineColor = OxyColor.Parse("#EEF2F9"),
                AxislineStyle = LineStyle.Solid,
                AxislineColor = OxyColor.Parse("#7D8CA5"),
                StringFormat = percentMode ? "0'%'" : "#,0.##"
            };
            model.Axes.Add(yAxis);

            if (_pmDashboardCurrentStatusDate.HasValue && _pmDashboardCurvePoints.Count > 0)
            {
                DateTime dataDate = _pmDashboardCurrentStatusDate.Value.Date;
                int dataDateIndex = -1;

                for (int i = 0; i < _pmDashboardCurvePoints.Count; i++)
                {
                    PmDashboardCurvePoint point = _pmDashboardCurvePoints[i];
                    if (point == null)
                    {
                        continue;
                    }

                    if (IsValidPmDashboardProjectDate(point.BucketStart) &&
                        IsValidPmDashboardProjectDate(point.BucketEnd) &&
                        dataDate >= point.BucketStart.Date &&
                        dataDate <= point.BucketEnd.Date)
                    {
                        dataDateIndex = i;
                        break;
                    }
                }

                if (dataDateIndex < 0)
                {
                    for (int i = 0; i < _pmDashboardCurvePoints.Count; i++)
                    {
                        PmDashboardCurvePoint point = _pmDashboardCurvePoints[i];
                        if (point != null &&
                            IsValidPmDashboardProjectDate(point.BucketStart) &&
                            point.BucketStart.Date <= dataDate)
                        {
                            dataDateIndex = i;
                        }
                    }
                }

                if (dataDateIndex >= 0)
                {
                    model.Annotations.Add(new LineAnnotation
                    {
                        Type = LineAnnotationType.Vertical,
                        X = dataDateIndex,
                        Color = OxyColor.Parse("#C0504D"),
                        LineStyle = LineStyle.Dash,
                        StrokeThickness = 2.0,
                        Text = "Data Date",
                        TextOrientation = AnnotationTextOrientation.Vertical,
                        TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right,
                        Layer = AnnotationLayer.AboveSeries
                    });
                }
            }

            if (showColumns)
            {
                var plannedColumn = new RectangleBarSeries
                {
                    Title = "Period Planned",
                    FillColor = OxyColor.Parse("#A9C8A9"),
                    StrokeColor = OxyColor.Parse("#7EA07E"),
                    StrokeThickness = 1
                };
                var actualColumn = new RectangleBarSeries
                {
                    Title = "Period EV",
                    FillColor = OxyColor.Parse("#D8B58A"),
                    StrokeColor = OxyColor.Parse("#B58E61"),
                    StrokeThickness = 1
                };

                for (int i = 0; i < _pmDashboardCurvePoints.Count; i++)
                {
                    PmDashboardCurvePoint point = _pmDashboardCurvePoints[i];
                    double leftPlanned = i - 0.33;
                    double rightPlanned = i - 0.03;
                    double leftActual = i + 0.03;
                    double rightActual = i + 0.33;

                    plannedColumn.Items.Add(new RectangleBarItem(leftPlanned, 0.0, rightPlanned, point.PeriodPlanned));
                    actualColumn.Items.Add(new RectangleBarItem(leftActual, 0.0, rightActual, point.PeriodActual));
                }

                model.Series.Add(plannedColumn);
                model.Series.Add(actualColumn);
            }

            if (showSCurve)
            {
                var plannedLine = new LineSeries
                {
                    Title = "PV",
                    Color = OxyColor.Parse("#2A8F58"),
                    StrokeThickness = 2.8,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 3.8
                };
                var earnedLine = new LineSeries
                {
                    Title = "EV",
                    Color = OxyColor.Parse("#C27A2C"),
                    StrokeThickness = 2.8,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 3.8
                };
                var actualCostLine = new LineSeries
                {
                    Title = "AC",
                    Color = OxyColor.Parse("#3B6FB6"),
                    StrokeThickness = 2.4,
                    LineStyle = LineStyle.Dash,
                    MarkerType = MarkerType.Square,
                    MarkerSize = 3.4
                };
                var forecastLine = new LineSeries
                {
                    Title = "Forecast",
                    Color = OxyColor.Parse("#C0504D"),
                    StrokeThickness = 2.4,
                    LineStyle = LineStyle.Dash,
                    MarkerType = MarkerType.None
                };

                for (int i = 0; i < _pmDashboardCurvePoints.Count; i++)
                {
                    PmDashboardCurvePoint point = _pmDashboardCurvePoints[i];
                    plannedLine.Points.Add(new DataPoint(i, point.PlannedCumulative));
                    earnedLine.Points.Add(new DataPoint(i, point.ActualCumulative));
                    actualCostLine.Points.Add(new DataPoint(i, point.ActualCostCumulative));
                    forecastLine.Points.Add(new DataPoint(i, point.ForecastCumulative));
                }

                model.Series.Add(plannedLine);
                model.Series.Add(earnedLine);
                model.Series.Add(actualCostLine);
                model.Series.Add(forecastLine);
            }

            AddPmDashboardColumnAndCumulativeLabels(model, yMax, percentMode, showColumns, showSCurve);

            pmPlotView.Model = model;
            pmPlotView.InvalidatePlot(true);
        }

        private void AddPmDashboardColumnAndCumulativeLabels(
            PlotModel model,
            double yMax,
            bool percentMode,
            bool showColumns,
            bool showSCurve)
        {
            if (model == null || _pmDashboardCurvePoints.Count == 0 || _pmDashboardCurvePoints.Count > 24)
            {
                return;
            }

            string FormatValue(double value)
            {
                if (percentMode)
                {
                    return value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
                }

                return value.ToString("#,0.##", CultureInfo.InvariantCulture);
            }

            double labelOffset = Math.Max(0.01, yMax * 0.012);
            for (int i = 0; i < _pmDashboardCurvePoints.Count; i++)
            {
                PmDashboardCurvePoint point = _pmDashboardCurvePoints[i];
                if (point == null)
                {
                    continue;
                }

                if (showColumns && point.PeriodPlanned > 1e-9)
                {
                    model.Annotations.Add(new TextAnnotation
                    {
                        Text = "Column: " + FormatValue(point.PeriodPlanned),
                        TextPosition = new DataPoint(i - 0.18, point.PeriodPlanned + labelOffset),
                        Stroke = OxyColors.Transparent,
                        TextColor = OxyColor.Parse("#2E3B4E"),
                        FontSize = 9,
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
                    });
                }

                if (showSCurve && point.ActualCumulative > 1e-9)
                {
                    model.Annotations.Add(new TextAnnotation
                    {
                        Text = "Cumulative: " + FormatValue(point.ActualCumulative),
                        TextPosition = new DataPoint(i + 0.14, point.ActualCumulative + (labelOffset * 1.2)),
                        Stroke = OxyColors.Transparent,
                        TextColor = OxyColor.Parse("#1F4F88"),
                        FontSize = 9,
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
                    });
                }
            }
        }

        private static string GetPmDashboardComboValue(System.Windows.Controls.ComboBox combo, string fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem item)
            {
                string content = Convert.ToString(item.Content, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content.Trim();
                }
            }

            string text = combo?.Text ?? "";
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            return fallback ?? "";
        }

        private static int GetPmDashboardBuildingLevelOrder(string label)
        {
            string value = (label ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return int.MaxValue;
            }

            if (value.IndexOf("basement", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return -10;
            }
            if (value.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }
            if (value.IndexOf("pilecap", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return -5;
            }

            Match match = Regex.Match(value, @"\d+");
            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int floor))
            {
                return floor;
            }

            return int.MaxValue;
        }

        private void AppendPmDashboardLog(string message)
        {
            var pmActivityLogText = GetPmDashboardNamedControl<TextBlock>("PmDashboardActivityLogText");
            string safe = (message ?? "").Trim();
            if (string.IsNullOrWhiteSpace(safe))
            {
                return;
            }

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string next = stamp + " | " + safe;
            _pmDashboardLastLogMessage = next;

            if (pmActivityLogText == null)
            {
                return;
            }

            string existing = pmActivityLogText.Text ?? "";
            if (string.IsNullOrWhiteSpace(existing))
            {
                pmActivityLogText.Text = next;
                return;
            }

            string combined = next + Environment.NewLine + existing;
            string[] lines = combined
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                .Take(16)
                .ToArray();
            pmActivityLogText.Text = string.Join(Environment.NewLine, lines);
        }

        private void InitializePmDashboardReportsDefaults()
        {
            _reportsLatestSnapshot = null;
            _reportsActivityTasks = new List<PmReportsActivityTaskRow>();
            _reportsFilteredActivityTasks = new List<PmReportsActivityTaskRow>();
            _reportsSCurvePoints = new List<PmDashboardCurvePoint>();
            _reportsDurationCurvePoints = new List<PmDashboardCurvePoint>();
            _reportsResourceCurvePoints = new List<PmDashboardCurvePoint>();

            if (lookaheadPeriodCombo != null && lookaheadPeriodCombo.SelectedIndex < 0)
            {
                lookaheadPeriodCombo.SelectedIndex = 1;
            }
            if (lookaheadViewCombo != null && lookaheadViewCombo.SelectedIndex < 0)
            {
                lookaheadViewCombo.SelectedIndex = 0;
            }
            if (sCurveGranularityCombo != null && sCurveGranularityCombo.SelectedIndex < 0)
            {
                sCurveGranularityCombo.SelectedIndex = 0;
            }
            if (sCurveValueModeCombo != null && sCurveValueModeCombo.SelectedIndex < 0)
            {
                sCurveValueModeCombo.SelectedIndex = 0;
            }
            if (durationGranularityCombo != null && durationGranularityCombo.SelectedIndex < 0)
            {
                durationGranularityCombo.SelectedIndex = 0;
            }
            if (durationValueModeCombo != null && durationValueModeCombo.SelectedIndex < 0)
            {
                durationValueModeCombo.SelectedIndex = 0;
            }
            if (resourceGranularityCombo != null && resourceGranularityCombo.SelectedIndex < 0)
            {
                resourceGranularityCombo.SelectedIndex = 0;
            }
            if (resourceValueModeCombo != null && resourceValueModeCombo.SelectedIndex < 0)
            {
                resourceValueModeCombo.SelectedIndex = 0;
            }
            if (progressReportModeCombo != null && progressReportModeCombo.SelectedIndex < 0)
            {
                progressReportModeCombo.SelectedIndex = 1;
            }
            if (activityTaskFilterCombo != null && activityTaskFilterCombo.SelectedIndex < 0)
            {
                activityTaskFilterCombo.SelectedIndex = 0;
            }

            SetProgressReportPlaceholder();
            UpdateLookaheadViewColumns();
            RefreshReportsCurveVisuals();
            RefreshReportsDashboardAndQuality(null);
            RefreshReportsLookaheadPreview(GetSelectedLookaheadDays());
            ApplyReportsActivityTaskFilter();
        }

        private static DataTable BuildProjectInfoSummaryTable(DataView filteredView)
        {
            var summary = new DataTable("Project_Info_Summary");
            summary.Columns.Add("HOUSE-TYPE", typeof(string));
            if (filteredView?.Table == null)
            {
                return summary;
            }

            DataTable source = filteredView.Table;
            List<DataRow> rows = filteredView
                .Cast<DataRowView>()
                .Select(rv => rv?.Row)
                .Where(r => r != null && r.RowState != DataRowState.Deleted)
                .ToList();
            if (rows.Count == 0)
            {
                return summary;
            }

            string zoneColumn = ResolveFilterColumn(source, "ZONE");
            string houseTypeColumn = ResolveFilterColumn(source, "HOUSE-TYPE");
            string landLotsColumn = ResolveFilterColumn(source, "LAND LOTS");
            string soldOnlyLandColumn = ResolveFilterColumn(source, "Sold Only Land");
            string dataSoldOutColumn = ResolveFilterColumn(source, "Data_Sold_Out");
            string soldUnsoldColumn = ResolveFilterColumn(source, "Sold/Unsold");
            string handoverStatusColumn = ResolveFilterColumn(source, "HANDOVERED STATUS");
            string houseUnitsColumn = ResolveFilterColumn(source, "House-Units");

            bool hasUnspecifiedZoneRows = rows.Any(row => string.IsNullOrWhiteSpace(GetProjectInfoCellText(row, zoneColumn)));
            List<string> zones = SortProjectInfoCodes(
                rows
                    .Select(row => GetProjectInfoCellText(row, zoneColumn))
                    .Where(v => !string.IsNullOrWhiteSpace(v) && !IsInfraStructureZone(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            if (zones.Count == 0 && hasUnspecifiedZoneRows)
            {
                zones.Add("Unspecified");
            }

            foreach (string zone in zones)
            {
                summary.Columns.Add(BuildProjectInfoMetricColumnName(zone, "LAND LOTS"), typeof(string));
                summary.Columns.Add(BuildProjectInfoMetricColumnName(zone, "Sold Only Land"), typeof(string));
                summary.Columns.Add(BuildProjectInfoMetricColumnName(zone, "House-Units"), typeof(string));
            }

            summary.Columns.Add(BuildProjectInfoMetricColumnName("Total", "LAND LOTS"), typeof(string));
            summary.Columns.Add(BuildProjectInfoMetricColumnName("Total", "Sold Only Land"), typeof(string));
            summary.Columns.Add(BuildProjectInfoMetricColumnName("Total", "House-Units"), typeof(string));

            var metricsByHouseType = new Dictionary<string, Dictionary<string, ProjectInfoSummaryMetrics>>(StringComparer.OrdinalIgnoreCase);
            var totalsByZone = new Dictionary<string, ProjectInfoSummaryMetrics>(StringComparer.OrdinalIgnoreCase);
            var grandTotal = new ProjectInfoSummaryMetrics();

            foreach (DataRow row in rows)
            {
                string zone = GetProjectInfoCellText(row, zoneColumn);
                if (string.IsNullOrWhiteSpace(zone))
                {
                    zone = "Unspecified";
                }

                string houseType = GetProjectInfoCellText(row, houseTypeColumn);
                if (string.IsNullOrWhiteSpace(houseType))
                {
                    houseType = "(Blank)";
                }

                double landLots = GetProjectInfoLandLotsValue(row, landLotsColumn);
                double soldOnlyLand = GetProjectInfoSoldOnlyLandValue(row, soldOnlyLandColumn, dataSoldOutColumn, soldUnsoldColumn, handoverStatusColumn);
                double houseUnits = GetProjectInfoHouseUnitsValue(row, houseUnitsColumn, landLots);

                if (!metricsByHouseType.TryGetValue(houseType, out Dictionary<string, ProjectInfoSummaryMetrics> zoneMap))
                {
                    zoneMap = new Dictionary<string, ProjectInfoSummaryMetrics>(StringComparer.OrdinalIgnoreCase);
                    metricsByHouseType[houseType] = zoneMap;
                }

                if (!zoneMap.TryGetValue(zone, out ProjectInfoSummaryMetrics zoneMetrics))
                {
                    zoneMetrics = new ProjectInfoSummaryMetrics();
                    zoneMap[zone] = zoneMetrics;
                }

                zoneMetrics.Add(landLots, soldOnlyLand, houseUnits);

                if (!totalsByZone.TryGetValue(zone, out ProjectInfoSummaryMetrics zoneTotalMetrics))
                {
                    zoneTotalMetrics = new ProjectInfoSummaryMetrics();
                    totalsByZone[zone] = zoneTotalMetrics;
                }

                zoneTotalMetrics.Add(landLots, soldOnlyLand, houseUnits);
                grandTotal.Add(landLots, soldOnlyLand, houseUnits);
            }

            List<string> houseTypes = metricsByHouseType.Keys
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string houseType in houseTypes)
            {
                DataRow outputRow = summary.NewRow();
                outputRow["HOUSE-TYPE"] = houseType;
                Dictionary<string, ProjectInfoSummaryMetrics> zoneMap = metricsByHouseType[houseType];

                var rowTotal = new ProjectInfoSummaryMetrics();
                foreach (ProjectInfoSummaryMetrics metrics in zoneMap.Values)
                {
                    if (metrics == null)
                    {
                        continue;
                    }

                    rowTotal.Add(metrics.LandLots, metrics.SoldOnlyLand, metrics.HouseUnits);
                }

                foreach (string zone in zones)
                {
                    if (zoneMap.TryGetValue(zone, out ProjectInfoSummaryMetrics metrics))
                    {
                        outputRow[BuildProjectInfoMetricColumnName(zone, "LAND LOTS")] =
                            FormatProjectInfoMetric("LAND LOTS", metrics.LandLots);
                        outputRow[BuildProjectInfoMetricColumnName(zone, "Sold Only Land")] =
                            FormatProjectInfoMetric("Sold Only Land", metrics.SoldOnlyLand);
                        outputRow[BuildProjectInfoMetricColumnName(zone, "House-Units")] =
                            FormatProjectInfoMetric("House-Units", metrics.HouseUnits);
                    }
                }

                outputRow[BuildProjectInfoMetricColumnName("Total", "LAND LOTS")] =
                    FormatProjectInfoMetric("LAND LOTS", rowTotal.LandLots);
                outputRow[BuildProjectInfoMetricColumnName("Total", "Sold Only Land")] =
                    FormatProjectInfoMetric("Sold Only Land", rowTotal.SoldOnlyLand);
                outputRow[BuildProjectInfoMetricColumnName("Total", "House-Units")] =
                    FormatProjectInfoMetric("House-Units", rowTotal.HouseUnits);

                summary.Rows.Add(outputRow);
            }

            DataRow totalRow = summary.NewRow();
            totalRow["HOUSE-TYPE"] = "Total";
            foreach (string zone in zones)
            {
                if (totalsByZone.TryGetValue(zone, out ProjectInfoSummaryMetrics metrics))
                {
                    totalRow[BuildProjectInfoMetricColumnName(zone, "LAND LOTS")] =
                        FormatProjectInfoMetric("LAND LOTS", metrics.LandLots);
                    totalRow[BuildProjectInfoMetricColumnName(zone, "Sold Only Land")] =
                        FormatProjectInfoMetric("Sold Only Land", metrics.SoldOnlyLand);
                    totalRow[BuildProjectInfoMetricColumnName(zone, "House-Units")] =
                        FormatProjectInfoMetric("House-Units", metrics.HouseUnits);
                }
            }

            totalRow[BuildProjectInfoMetricColumnName("Total", "LAND LOTS")] =
                FormatProjectInfoMetric("LAND LOTS", grandTotal.LandLots);
            totalRow[BuildProjectInfoMetricColumnName("Total", "Sold Only Land")] =
                FormatProjectInfoMetric("Sold Only Land", grandTotal.SoldOnlyLand);
            totalRow[BuildProjectInfoMetricColumnName("Total", "House-Units")] =
                FormatProjectInfoMetric("House-Units", grandTotal.HouseUnits);
            summary.Rows.Add(totalRow);

            return summary;
        }

        private static string BuildProjectInfoMetricColumnName(string groupName, string metricName)
        {
            string group = (groupName ?? "").Trim();
            string metric = (metricName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(group))
            {
                return metric;
            }

            if (string.Equals(group, "Total", StringComparison.OrdinalIgnoreCase))
            {
                return "Total" + Environment.NewLine + metric;
            }

            return group + Environment.NewLine + metric;
        }

        private static string GetProjectInfoCellText(DataRow row, string columnName)
        {
            if (row == null || string.IsNullOrWhiteSpace(columnName))
            {
                return "";
            }

            return Convert.ToString(row[columnName], CultureInfo.InvariantCulture)?.Trim() ?? "";
        }

        private static double GetProjectInfoLandLotsValue(DataRow row, string landLotsColumn)
        {
            return ParseBoreyNumericValue(string.IsNullOrWhiteSpace(landLotsColumn) ? null : row[landLotsColumn]);
        }

        private static double GetProjectInfoHouseUnitsValue(DataRow row, string houseUnitsColumn, double landLots)
        {
            double value = ParseBoreyNumericValue(string.IsNullOrWhiteSpace(houseUnitsColumn) ? null : row[houseUnitsColumn]);
            if (value > 0.0)
            {
                return value;
            }

            if (landLots > 0.0)
            {
                return landLots;
            }

            return 1.0;
        }

        private static double GetProjectInfoSoldOnlyLandValue(
            DataRow row,
            string soldOnlyLandColumn,
            string dataSoldOutColumn,
            string soldUnsoldColumn,
            string handoverStatusColumn)
        {
            double numeric = ParseBoreyNumericValue(string.IsNullOrWhiteSpace(soldOnlyLandColumn) ? null : row[soldOnlyLandColumn]);
            if (numeric > 0.0)
            {
                return numeric;
            }

            numeric = ParseBoreyNumericValue(string.IsNullOrWhiteSpace(dataSoldOutColumn) ? null : row[dataSoldOutColumn]);
            if (numeric > 0.0)
            {
                return numeric;
            }

            string soldUnsold = GetProjectInfoCellText(row, soldUnsoldColumn);
            if (string.Equals(soldUnsold, "Sold Only Land", StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            string status = GetProjectInfoCellText(row, handoverStatusColumn);
            if (string.Equals(status, "Sold Only Land", StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            return 0.0;
        }

        private static string FormatProjectInfoMetric(string metricName, double value)
        {
            if (string.Equals(metricName, "House-Units", StringComparison.OrdinalIgnoreCase))
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static List<string> SortProjectInfoCodes(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(v =>
                {
                    SplitProjectInfoCodeForSort(v, out string prefix, out int number, out string suffix);
                    return new
                    {
                        Value = v,
                        Prefix = prefix,
                        Number = number,
                        Suffix = suffix
                    };
                })
                .OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Suffix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Value)
                .ToList();
        }

        private static void SplitProjectInfoCodeForSort(string value, out string prefix, out int number, out string suffix)
        {
            prefix = "";
            number = int.MaxValue;
            suffix = "";

            string text = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            int firstDigit = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    firstDigit = i;
                    break;
                }
            }

            if (firstDigit < 0)
            {
                prefix = text;
                return;
            }

            prefix = text.Substring(0, firstDigit);
            int j = firstDigit;
            while (j < text.Length && char.IsDigit(text[j]))
            {
                j++;
            }

            string numberToken = text.Substring(firstDigit, j - firstDigit);
            if (!int.TryParse(numberToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                number = int.MaxValue;
            }

            suffix = j < text.Length ? text.Substring(j) : "";
        }
        private const string PmUpdateProgressRevitLinkActive = "Active with Revit";
        private const string PmUpdateProgressRevitLinkNotActive = "Not Active with Revit";
        private const string PmStageMappingRulesFileName = "pm_stage_mapping_rules.txt";
        private const string PmStageMappingRulesReinforcementKey = "reinforcement";
        private const string PmStageMappingRulesFormworkKey = "formwork";
        private const string PmStageMappingRulesVolumeKey = "volume";
        private const string PmAutoMapRulesFileName = "pm_auto_map_rules.txt";
        private const string PmAutoMapRulesMatchBuildingLevelKey = "match_building_level";
        private const string PmAutoMapRulesMatchStructureElementKey = "match_structure_element";
        private const string PmAutoMapRulesMatchStructureTypeKey = "match_structure_type";
        private bool _pmUpdateProgressSelectionSyncInProgress;
        private PmStageMappingRules _pmStageMappingRules = CreateDefaultPmStageMappingRules();
        private PmAutoMapMatchOptions _pmAutoMapMatchOptions = CreateDefaultPmAutoMapMatchOptions();
        private static readonly Regex PmUpdateProgressElementIdRegex =
            new Regex("\\d+", RegexOptions.Compiled);
        private const string DefaultMsProjectSyncFolderName = "MS Project Sync";
        private const string DefaultMsProjectDashboardAbsolutePath =
            @"D:\CamboBIM\20260125_CamboBIM\MSP_Extension\CamboBIM_Dashboard\bin\x64\Release\CamboBIM_Dashboard.exe";
        private const string DefaultMsProjectDashboardRelativePath =
            @"MSP_Extension\CamboBIM_Dashboard\bin\x64\Release\CamboBIM_Dashboard.exe";
        private const string MsProjectApplicationProgId = "MSProject.Application";
        private const string MsProjectManifestFileName = "msp_sync_manifest.csv";
        private const string MsProjectManifestSnapshotUtcKey = "snapshot_utc";
        private const string MsProjectManifestProjectNameKey = "msp_project_name";
        private const string MsProjectManifestProjectPathKey = "msp_project_path";
        private const string MsProjectManifestDataDateKey = "msp_data_date";
        private const string MsProjectManifestDataDateSourceKey = "msp_data_date_source";
        private const string MsProjectTaskTextFieldStructureType = "Text28";
        private const string MsProjectTaskTextFieldStructureElement = "Text29";
        private const string MsProjectTaskTextFieldBuildingLevel = "Text30";
        private const string MsProjectTaskTextFieldRevitElementIds = "Text27";
        private const string MsProjectTaskTextFieldUnit = "Text26";
        private const string MsProjectTaskTextAliasStructureType = "Structure Type";
        private const string MsProjectTaskTextAliasStructureElement = "Structure Element";
        private const string MsProjectTaskTextAliasBuildingLevel = "BuildingLevel";
        private const string MsProjectTaskTextAliasRevitElementIds = "Revit Element IDs";
        private const string MsProjectTaskTextAliasUnit = "Unit";
        private const int MsProjectFieldTypeTask = 0;
        private readonly List<PmDashboardCurvePoint> _pmDashboardCurvePoints = new List<PmDashboardCurvePoint>();
        private bool _pmDashboardUiUpdateInProgress;
        private bool _pmDashboardLiveDataAvailable;
        private bool _pmUpdateProgressApplyInProgress;
        private bool _pmUpdateProgressFilterUiUpdateInProgress;
        private readonly Dictionary<string, HashSet<string>> _pmUpdateProgressFilterSelectedValuesByCombo =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _pmUpdateProgressColumnVisibilityInitialized;
        private DateTime? _pmDashboardCurrentStatusDate;
        private string _pmDashboardLastLogMessage = "PM Dashboard ready.";
        private readonly HashSet<string> _pmUpdateProgressHiddenColumnKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pmUpdateProgressKnownColumnKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] PmUpdateProgressDefaultVisibleColumnKeys =
        {
            "revit_element_ids",
            "task_id",
            "task_unique_id",
            "task_name",
            "revit_structure_element",
            "revit_building_level",
            "revit_type",
            "status",
            "percent_complete",
            "unit",
            "boq",
            "start",
            "finish"
        };
        private static readonly HashSet<string> PmUpdateProgressInternalColumnKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pm_filter_structure_element",
                "pm_filter_building_level",
                "pm_filter_structure_type",
                "pm_filter_revit_link_state",
                "percent_complete_live"
            };
        private static readonly string[][] PmUpdateProgressPreferredLeadColumnGroups =
        {
            new[] { "revit_element_ids" },
            new[] { "revit_building_level" },
            new[] { "revit_structure_element" },
            new[] { "revit_type" },
            new[] { "task_id" },
            new[] { "task_unique_id" },
            new[] { "task_name" },
            new[] { "status" },
            new[] { "percent_complete" },
            new[] { "unit" },
            new[] { "boq" },
            new[] { "start" },
            new[] { "finish" }
        };
        private static readonly HashSet<string> PmUpdateProgressWritableColumnKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "unit",
                "boq",
                "percent_complete",
                "status",
                "revit_structure_element",
                "revit_building_level",
                "revit_type",
                "revit_element_ids",
                "pm_filter_structure_element",
                "pm_filter_building_level",
                "pm_filter_structure_type"
            };
        private readonly Dictionary<TabItem, object> _pmDashboardTabContentCache =
            new Dictionary<TabItem, object>();
        private bool _pmDashboardMultiSubTabMode;
        private PmDashboardLiveSnapshot _reportsLatestSnapshot;
        private DateTime? _reportsLookaheadProjectStart;
        private DateTime? _reportsLookaheadProjectFinish;
        private bool _reportsLookaheadSuppressUpdate;
        private bool _reportsCashflowSyncInProgress;
        private List<PmReportsActivityTaskRow> _reportsActivityTasks = new List<PmReportsActivityTaskRow>();
        private List<PmReportsActivityTaskRow> _reportsFilteredActivityTasks = new List<PmReportsActivityTaskRow>();
        private PmReportsProgressReportContent _reportsProgressCostContent;
        private PmReportsProgressReportContent _reportsProgressDurationContent;
        private List<PmDashboardCurvePoint> _reportsSCurvePoints = new List<PmDashboardCurvePoint>();
        private List<PmDashboardCurvePoint> _reportsDurationCurvePoints = new List<PmDashboardCurvePoint>();
        private List<PmDashboardCurvePoint> _reportsResourceCurvePoints = new List<PmDashboardCurvePoint>();

        private enum PmReportsLookaheadViewMode
        {
            Timeline,
            Wbs
        }

        private enum PmReportsProgressMode
        {
            Duration,
            Cost
        }

        private enum PmReportsActivityTaskFilterMode
        {
            AllTasks,
            Completed,
            InProgress,
            NotStarted,
            StartLate,
            TookLonger,
            FinishedLater,
            FinishedEarlier,
            FinishedOnTime,
            TookLess,
            StartingThisWeek,
            FinishingThisWeek,
            InProgressThisWeek
        }

        private sealed class PmReportsLookaheadRow
        {
            public string Id { get; set; } = "";
            public string Activity { get; set; } = "";
            public string ActivityDisplay { get; set; } = "";
            public string Start { get; set; } = "";
            public string Finish { get; set; } = "";
            public string Actual { get; set; } = "";
            public string Remaining { get; set; } = "";
            public DateTime StartDate { get; set; }
            public string Wbs { get; set; } = "";
            public string BaselineCost { get; set; } = "";
            public string ActualCost { get; set; } = "";
            public string RemainingCost { get; set; } = "";
            public bool IsTotal { get; set; }
        }

        private sealed class PmReportsActivityTaskRow
        {
            public string Id { get; set; } = "";
            public string Activity { get; set; } = "";
            public string PercentComplete { get; set; } = "";
            public string PlannedPercent { get; set; } = "";
            public string Start { get; set; } = "";
            public string Finish { get; set; } = "";
            public string Duration { get; set; } = "";
            public string TotalFloat { get; set; } = "";
            public string BaselineStart { get; set; } = "";
            public string BaselineFinish { get; set; } = "";
            public string BaselineDuration { get; set; } = "";
            public string Slip { get; set; } = "";
            public double PercentCompleteValue { get; set; }
            public double PlannedPercentValue { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime FinishDate { get; set; }
            public DateTime BaselineStartDate { get; set; }
            public DateTime BaselineFinishDate { get; set; }
            public DateTime ActualStartDate { get; set; }
            public DateTime ActualFinishDate { get; set; }
            public double DurationDays { get; set; }
            public double BaselineDurationDays { get; set; }
            public double ActualDurationDays { get; set; }
            public double TotalFloatDays { get; set; }
            public double SlipDays { get; set; }
        }

        private sealed class PmReportsProgressMetrics
        {
            public double ActualToDate { get; set; }
            public double BaselineToDate { get; set; }
            public double ForecastTotal { get; set; }
            public double PlannedTotal { get; set; }
            public double ActualPercent { get; set; }
            public double PlannedPercent { get; set; }
            public double PercentVariance { get; set; }
            public double Ptq { get; set; }
            public DateTime? BaselineDateForActual { get; set; }
            public int SlipDays { get; set; }
        }

        private sealed class PmReportsProgressReportContent
        {
            public string Title { get; set; } = "";
            public string ReportDate { get; set; } = "";
            public string SummaryActual { get; set; } = "-";
            public string SummaryBaseline { get; set; } = "-";
            public string SummaryVariance { get; set; } = "-";
            public string SummaryPercentComplete { get; set; } = "-";
            public string SummaryPlannedPercent { get; set; } = "-";
            public string SummaryPercentVariance { get; set; } = "-";
            public string SummaryPtq { get; set; } = "-";
            public string SummarySlip { get; set; } = "-";
            public string ForecastFinishDate { get; set; } = "-";
            public string PlannedFinishDate { get; set; } = "-";
            public string ProjectSlip { get; set; } = "-";
            public string ForecastTotal { get; set; } = "-";
            public string PlannedTotal { get; set; } = "-";
            public string ProjectGrowth { get; set; } = "-";
            public string SCurveLine1 { get; set; } = "";
            public string SCurveLine2 { get; set; } = "";
            public string SCurveLine3 { get; set; } = "";
            public string GrowthLine1 { get; set; } = "";
            public string GrowthLine2 { get; set; } = "";
            public string GrowthLine3 { get; set; } = "";
            public string SlipLine1 { get; set; } = "";
            public string SlipLine2 { get; set; } = "";
            public string SlipLine3 { get; set; } = "";
            public string ToDateLine1 { get; set; } = "";
            public string ToDateLine2 { get; set; } = "";
            public string ToDateLine3 { get; set; } = "";
            public string ToDateLine4 { get; set; } = "";
            public string ToDateLine5 { get; set; } = "";
            public string ForecastLine1 { get; set; } = "";
            public string ForecastLine2 { get; set; } = "";
        }

        private sealed class PmReportsPieStatus
        {
            public int FirstCount { get; set; }
            public int SecondCount { get; set; }
            public int ThirdCount { get; set; }
        }

        private sealed class PmReportsCurveGridRow
        {
            public string Period { get; set; } = "";
            public string PVDisplay { get; set; } = "";
            public string ACDisplay { get; set; } = "";
            public string EVDisplay { get; set; } = "";
            public string ForecastDisplay { get; set; } = "";
            public string PVTotalDisplay { get; set; } = "";
            public string ACTotalDisplay { get; set; } = "";
            public string EVTotalDisplay { get; set; } = "";
            public string ForecastTotalDisplay { get; set; } = "";
        }

        private sealed class MsProjectActiveContext
        {
            public string ProjectName { get; set; } = "";
            public string ProjectPath { get; set; } = "";
            public DateTime? DataDateLocal { get; set; }
            public string DataDateSource { get; set; } = "";
        }

        private sealed class PmUpdateProgressTaskApplyRow
        {
            public double PercentComplete { get; set; }
            public string BuildingLevel { get; set; } = "";
            public string StructureElement { get; set; } = "";
            public string StructureType { get; set; } = "";
            public string RevitElementIds { get; set; } = "";
            public string Unit { get; set; } = "";
            public double Boq { get; set; }
            public bool HasBoqValue { get; set; }
        }

        private sealed class PmStageMappingRules
        {
            public List<string> ReinforcementKeywords { get; } = new List<string>();
            public List<string> FormworkKeywords { get; } = new List<string>();
            public List<string> VolumeKeywords { get; } = new List<string>();

            public PmStageMappingRules Clone()
            {
                var clone = new PmStageMappingRules();
                clone.ReinforcementKeywords.AddRange(ReinforcementKeywords);
                clone.FormworkKeywords.AddRange(FormworkKeywords);
                clone.VolumeKeywords.AddRange(VolumeKeywords);
                return clone;
            }
        }

        private sealed class PmAutoMapMatchOptions
        {
            public bool MatchBuildingLevel { get; set; } = true;
            public bool MatchStructureElement { get; set; } = true;
            public bool MatchStructureType { get; set; } = true;

            public int ActiveCount =>
                (MatchBuildingLevel ? 1 : 0) +
                (MatchStructureElement ? 1 : 0) +
                (MatchStructureType ? 1 : 0);

            public PmAutoMapMatchOptions Clone()
            {
                return new PmAutoMapMatchOptions
                {
                    MatchBuildingLevel = MatchBuildingLevel,
                    MatchStructureElement = MatchStructureElement,
                    MatchStructureType = MatchStructureType
                };
            }
        }

        private sealed class PmUpdateProgressPreviewRow
        {
            public int TaskId { get; set; }
            public int TaskUniqueId { get; set; }
            public string TaskName { get; set; } = "";
            public string Stage { get; set; } = "";
            public double CurrentPercent { get; set; }
            public double TargetPercent { get; set; }
            public double DeltaPercent { get; set; }
            public string BuildingLevel { get; set; } = "";
            public string StructureElement { get; set; } = "";
            public string StructureType { get; set; } = "";
            public int LinkedElementCount { get; set; }
            public string RevitLinkState { get; set; } = "";
        }

        private sealed class PmUpdateProgressConflictRow
        {
            public int ElementId { get; set; }
            public string ConflictType { get; set; } = "";
            public string Detail { get; set; } = "";
            public string TaskRefs { get; set; } = "";
        }

        private sealed class PmUpdateProgressFilterOption : INotifyPropertyChanged
        {
            private bool _isSelected;

            public string Value { get; set; } = "";

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                    {
                        return;
                    }

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private sealed class PmElementStageAssignment
        {
            public int TaskId { get; set; }
            public int TaskUniqueId { get; set; }
            public string TaskName { get; set; } = "";
            public PmTaskProgressStage Stage { get; set; }
        }

        private enum PmTaskProgressStage
        {
            Overall,
            Reinforcement,
            Formwork,
            Volume
        }

        private sealed class PmDashboardCurvePoint
        {
            public string Label { get; set; } = "";
            public DateTime BucketStart { get; set; }
            public DateTime BucketEnd { get; set; }
            public double PlannedCumulative { get; set; }
            public double ActualCumulative { get; set; }
            public double ActualCostCumulative { get; set; }
            public double ForecastCumulative { get; set; }
            public double PeriodPlanned { get; set; }
            public double PeriodActual { get; set; }
            public double PeriodActualCost { get; set; }
        }

        private enum PmDashboardTimeGranularity
        {
            Daily,
            Weekly,
            Monthly
        }

        private sealed class PmDashboardTimeBucket
        {
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public string Label { get; set; } = "";
        }

        private sealed class PmDashboardTaskSnapshot
        {
            public int TaskId { get; set; }
            public int TaskUniqueId { get; set; }
            public string TaskName { get; set; } = "";
            public string TaskLinkKey { get; set; } = "";
            public string TaskStructureElement { get; set; } = "";
            public string TaskBuildingLevel { get; set; } = "";
            public string TaskStructureType { get; set; } = "";
            public string TaskRevitElementIds { get; set; } = "";
            public string TaskUnit { get; set; } = "";
            public double TaskBoq { get; set; }
            public DateTime TaskStart { get; set; }
            public DateTime TaskFinish { get; set; }
            public DateTime PlanStart { get; set; }
            public DateTime PlanFinish { get; set; }
            public DateTime ActualStart { get; set; }
            public DateTime ActualFinish { get; set; }
            public double PercentComplete { get; set; }
            public double BaselineCost { get; set; }
            public double BcwsToDate { get; set; }
            public double BcwpToDate { get; set; }
            public double AcwpToDate { get; set; }
            public double ActualCostToDate { get; set; }
            public double RemainingCost { get; set; }
            public double BaselineDurationDays { get; set; }
            public double ActualDurationDays { get; set; }
            public double RemainingDurationDays { get; set; }
            public double BaselineWorkDays { get; set; }
            public double ActualWorkDays { get; set; }
            public double RemainingWorkDays { get; set; }
        }

        private sealed class PmDashboardLiveSnapshot
        {
            public string ProjectName { get; set; } = "";
            public DateTime StatusDate { get; set; }
            public DateTime ProjectStart { get; set; }
            public DateTime ProjectFinish { get; set; }
            public double Bac { get; set; }
            public double PvToDate { get; set; }
            public double EvToDate { get; set; }
            public double AcToDate { get; set; }
            public double RemainingCost { get; set; }
            public double BaselineDurationTotalDays { get; set; }
            public double PlannedDurationToDateDays { get; set; }
            public double EarnedDurationToDateDays { get; set; }
            public int TotalTasks { get; set; }
            public int CompletedTasks { get; set; }
            public int InProgressTasks { get; set; }
            public int NotStartedTasks { get; set; }
            public List<PmDashboardTaskSnapshot> Tasks { get; } = new List<PmDashboardTaskSnapshot>();
        }

        private sealed class PmUpdateProgressColumnToggleTag
        {
            public DataGrid Grid { get; set; }
            public string ColumnKey { get; set; } = "";
        }

        private sealed class ProjectInfoSummaryMetrics
        {
            public double LandLots { get; set; }
            public double SoldOnlyLand { get; set; }
            public double HouseUnits { get; set; }

            public void Add(double landLots, double soldOnlyLand, double houseUnits)
            {
                LandLots += landLots;
                SoldOnlyLand += soldOnlyLand;
                HouseUnits += houseUnits;
            }
        }
    }
}

