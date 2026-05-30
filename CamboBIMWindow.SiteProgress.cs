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

        private void OnSiteProgressApplyClick(object sender, RoutedEventArgs e)
        {
            if (!TryGetSiteProgressPercents(out double reinforcementPercent, out double formworkPercent, out double volumePercent))
            {
                return;
            }

            _handler.Request.SiteProgress.ReinforcementPercent = reinforcementPercent;
            _handler.Request.SiteProgress.FormworkPercent = formworkPercent;
            _handler.Request.SiteProgress.VolumePercent = volumePercent;
            _handler.Request.SiteProgress.Percent = volumePercent;
            _handler.Request.SiteProgress.IsAddMode = SiteProgressAddModeRadio?.IsChecked == true;
            _handler.Request.SiteProgress.Scope = GetSiteProgressScope();
            ApplySiteProgressFiltersToRequest();
            _handler.Request.RequestType = CadToModelRequestType.ApplySiteProgress;
            QueueExternalRequest();
        }

        private void OnSiteProgressColorViewClick(object sender, RoutedEventArgs e)
        {
            RequestSiteProgressColorView(clearOnly: false);
        }

        private void OnSiteProgressClearColorViewClick(object sender, RoutedEventArgs e)
        {
            RequestSiteProgressColorView(clearOnly: true);
        }

        private void RequestSiteProgressColorView(bool clearOnly)
        {
            _handler.Request.SiteProgress.Scope = GetSiteProgressScope();
            ApplySiteProgressFiltersToRequest();
            _handler.Request.RequestType = clearOnly
                ? CadToModelRequestType.ClearSiteProgressColorView
                : CadToModelRequestType.ColorizeSiteProgressView;
            QueueExternalRequest();
        }

        private void OnSiteProgressRefreshClick(object sender, RoutedEventArgs e)
        {
            RequestSiteProgressRefresh(focusFilteredView: false);
        }

        private void OnSiteProgressDashboardClearClick(object sender, RoutedEventArgs e)
        {
            _siteProgressSelectionSyncInProgress = true;
            try
            {
                SiteProgressCombinedGrid?.SelectedItems?.Clear();
                SiteProgressByBuildingGrid?.SelectedItems?.Clear();
                SiteProgressDashboardStructureGrid?.SelectedItems?.Clear();
            }
            finally
            {
                _siteProgressSelectionSyncInProgress = false;
            }

            _siteProgressDashboardStructureRows.Clear();
            if (SiteProgressDashboardTotalsText != null)
            {
                SiteProgressDashboardTotalsText.Text = BuildSiteProgressDashboardTotalsText(_siteProgressDashboardStructureRows);
            }

            UpdateProgressDashboardCharts();
            ShowStatus("Progress Dashboard cleared. Click Refresh to reload data.");
        }

        private void OnSiteProgressDashboardResetClick(object sender, RoutedEventArgs e)
        {
            _siteProgressDashboardUiUpdateInProgress = true;
            try
            {
                if (SiteProgressSetModeRadio != null)
                {
                    SiteProgressSetModeRadio.IsChecked = true;
                }

                if (SiteProgressScopeAllRadio != null)
                {
                    SiteProgressScopeAllRadio.IsChecked = true;
                }

                if (SiteProgressReinforcementPercentTextBox != null) SiteProgressReinforcementPercentTextBox.Text = "0";
                if (SiteProgressFormworkPercentTextBox != null) SiteProgressFormworkPercentTextBox.Text = "0";
                if (SiteProgressVolumePercentTextBox != null) SiteProgressVolumePercentTextBox.Text = "0";

                if (SiteProgressStructureFilterCombo != null) SiteProgressStructureFilterCombo.SelectedIndex = 0;
                if (SiteProgressBuildingLevelFilterCombo != null) SiteProgressBuildingLevelFilterCombo.SelectedIndex = 0;
                if (SiteProgressPivotModeCombo != null) SiteProgressPivotModeCombo.SelectedIndex = 0;
                if (SiteProgressPivotMetricCombo != null) SiteProgressPivotMetricCombo.SelectedIndex = 0;
                if (SiteProgressDashboardValueModeCombo != null) SiteProgressDashboardValueModeCombo.SelectedIndex = 0;

                if (SiteProgressDashboardFocusChartToggle != null)
                {
                    SiteProgressDashboardFocusChartToggle.IsChecked = false;
                }

                if (SiteProgressDashboardZoomXSlider != null) SiteProgressDashboardZoomXSlider.Value = 1.0;
                if (SiteProgressDashboardZoomYSlider != null) SiteProgressDashboardZoomYSlider.Value = 1.0;
            }
            finally
            {
                _siteProgressDashboardUiUpdateInProgress = false;
            }

            ApplySiteProgressDashboardFocusLayout(false);
            ApplySiteProgressDashboardChartZoom();
            ApplySiteProgressPivot();
            UpdateProgressDashboardStructureRows();
            UpdateProgressDashboardCharts();
            RequestSiteProgressRefresh(focusFilteredView: false);
            ShowStatus("Progress Dashboard reset to default and refreshing data.");
        }

        private void OnSiteProgressFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (_siteProgressDashboardUiUpdateInProgress) return;
            if (SiteProgressStructureFilterCombo == null || SiteProgressBuildingLevelFilterCombo == null) return;
            RequestSiteProgressRefresh(focusFilteredView: true);
        }

        private void RequestSiteProgressRefresh(bool focusFilteredView)
        {
            _handler.Request.SiteProgress.Scope = GetSiteProgressScope();
            ApplySiteProgressFiltersToRequest();
            _handler.Request.SiteProgress.FocusFilteredView = focusFilteredView;
            _handler.Request.RequestType = CadToModelRequestType.RefreshSiteProgressSummary;
            QueueExternalRequest();
        }

        private void OnSiteProgressPivotChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (_siteProgressDashboardUiUpdateInProgress) return;
            ApplySiteProgressPivot();
            UpdateProgressDashboardStructureRows();
            UpdateProgressDashboardCharts();
        }

        private void OnSiteProgressDashboardValueModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (_siteProgressDashboardUiUpdateInProgress) return;
            UpdateProgressDashboardCharts();
        }

        private void OnSiteProgressDashboardFocusChartChanged(object sender, RoutedEventArgs e)
        {
            if (_siteProgressDashboardUiUpdateInProgress) return;
            ApplySiteProgressDashboardFocusLayout(SiteProgressDashboardFocusChartToggle?.IsChecked == true);
        }

        private void ApplySiteProgressDashboardFocusLayout(bool focusChart)
        {
            if (SiteProgressDashboardRightGapColumn != null)
            {
                SiteProgressDashboardRightGapColumn.Width = new GridLength(focusChart ? 0.0 : DashboardRightGapWidth);
            }

            if (SiteProgressDashboardRightColumn != null)
            {
                SiteProgressDashboardRightColumn.Width = new GridLength(focusChart ? 0.0 : DashboardRightPanelWidth);
            }

            if (SiteProgressDashboardDonutGroup != null)
            {
                SiteProgressDashboardDonutGroup.Visibility = focusChart ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }

            if (SiteProgressDashboardTableGroup != null)
            {
                SiteProgressDashboardTableGroup.Visibility = focusChart ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }
        }

        private void OnSiteProgressDashboardZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            ApplySiteProgressDashboardChartZoom();
        }

        private void OnSiteProgressDashboardResetZoomClick(object sender, RoutedEventArgs e)
        {
            if (SiteProgressDashboardZoomXSlider != null)
            {
                SiteProgressDashboardZoomXSlider.Value = 1.0;
            }
            if (SiteProgressDashboardZoomYSlider != null)
            {
                SiteProgressDashboardZoomYSlider.Value = 1.0;
            }

            ApplySiteProgressDashboardChartZoom();
        }

        private void OnSiteProgressDashboardChartPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            bool zoomXY = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool zoomX = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool zoomY = (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            double delta = e.Delta > 0 ? DashboardChartZoomStep : -DashboardChartZoomStep;
            if (!zoomXY && !zoomX && !zoomY)
            {
                System.Windows.Point cursor = e.GetPosition(SiteProgressDashboardChartScrollViewer ?? (IInputElement)sender);
                double viewportWidth = SiteProgressDashboardChartScrollViewer?.ViewportWidth ?? 0.0;
                double viewportHeight = SiteProgressDashboardChartScrollViewer?.ViewportHeight ?? 0.0;
                if (viewportWidth <= 1.0) _ = SiteProgressDashboardChartScrollViewer?.ActualWidth ?? 0.0;
                if (viewportHeight <= 1.0) viewportHeight = SiteProgressDashboardChartScrollViewer?.ActualHeight ?? 0.0;

                // Plot-style interaction:
                // - near left axis => Y zoom
                // - near bottom axis => X zoom
                // - inside plot area => XY zoom
                bool nearLeftAxis = cursor.X <= 72.0;
                bool nearBottomAxis = viewportHeight > 0.0 && cursor.Y >= (viewportHeight - 42.0);
                if (nearLeftAxis && !nearBottomAxis)
                {
                    zoomY = true;
                }
                else if (nearBottomAxis && !nearLeftAxis)
                {
                    zoomX = true;
                }
                else
                {
                    zoomXY = true;
                }
            }

            if (zoomXY || (zoomX && zoomY))
            {
                AdjustSiteProgressDashboardChartZoom(delta, delta);
            }
            else if (zoomX)
            {
                AdjustSiteProgressDashboardChartZoom(delta, 0.0);
            }
            else if (zoomY)
            {
                AdjustSiteProgressDashboardChartZoom(0.0, delta);
            }

            e.Handled = true;
        }

        private void AdjustSiteProgressDashboardChartZoom(double deltaX, double deltaY)
        {
            if (SiteProgressDashboardZoomXSlider == null || SiteProgressDashboardZoomYSlider == null)
            {
                return;
            }

            SiteProgressDashboardZoomXSlider.Value = ClampDashboardChartZoom(SiteProgressDashboardZoomXSlider.Value + deltaX);
            SiteProgressDashboardZoomYSlider.Value = ClampDashboardChartZoom(SiteProgressDashboardZoomYSlider.Value + deltaY);
            ApplySiteProgressDashboardChartZoom();
        }

        private void ApplySiteProgressDashboardChartZoom()
        {
            double scaleX = ClampDashboardChartZoom(SiteProgressDashboardZoomXSlider?.Value ?? 1.0);
            double scaleY = ClampDashboardChartZoom(SiteProgressDashboardZoomYSlider?.Value ?? 1.0);

            if (SiteProgressDashboardChartScaleTransform != null)
            {
                SiteProgressDashboardChartScaleTransform.ScaleX = scaleX;
                SiteProgressDashboardChartScaleTransform.ScaleY = scaleY;
            }

            if (SiteProgressDashboardZoomXText != null)
            {
                SiteProgressDashboardZoomXText.Text = (scaleX * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
            }
            if (SiteProgressDashboardZoomYText != null)
            {
                SiteProgressDashboardZoomYText.Text = (scaleY * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";
            }
        }

        private void OnSiteProgressDashboardStructureSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _siteProgressSelectionSyncInProgress) return;
            HandleSiteProgressSelectionFromGrid(sender as DataGrid, syncInputPercentEditors: false);
        }

        private void OnSiteProgressGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _siteProgressSelectionSyncInProgress) return;
            HandleSiteProgressSelectionFromGrid(sender as DataGrid, syncInputPercentEditors: true);
        }

        private void HandleSiteProgressSelectionFromGrid(DataGrid grid, bool syncInputPercentEditors)
        {
            if (grid == null) return;

            List<SiteProgressSummaryRow> selectedRows = grid.SelectedItems
                .OfType<SiteProgressSummaryRow>()
                .Where(r => r != null)
                .ToList();

            if (selectedRows.Count == 0 && grid.SelectedItem is SiteProgressSummaryRow singleRow)
            {
                selectedRows.Add(singleRow);
            }

            if (selectedRows.Count == 0)
            {
                return;
            }

            if (syncInputPercentEditors && selectedRows.Count == 1)
            {
                SyncSiteProgressInputFromSelectedRow(selectedRows[0]);
            }

            List<int> targetIds = selectedRows
                .SelectMany(r => r.ElementIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (targetIds.Count == 0)
            {
                ShowStatus("Site Progress: selected row(s) have no linked elements.");
                return;
            }

            _siteProgressSelectionSyncInProgress = true;
            try
            {
                _handler.Request.SiteProgress.TargetElementIds = targetIds;
                _handler.Request.RequestType = CadToModelRequestType.SelectSiteProgressElements;
                QueueExternalRequest();
            }
            finally
            {
                _siteProgressSelectionSyncInProgress = false;
            }

            if (selectedRows.Count > 1)
            {
                ShowStatus("Site Progress: selected " + selectedRows.Count.ToString(CultureInfo.InvariantCulture) +
                           " rows (" + targetIds.Count.ToString(CultureInfo.InvariantCulture) + " element(s)).");
            }
        }

        private void SyncSiteProgressInputFromSelectedRow(SiteProgressSummaryRow row)
        {
            if (row == null) return;

            if (SiteProgressReinforcementPercentTextBox != null)
            {
                SiteProgressReinforcementPercentTextBox.Text =
                    Math.Max(0.0, Math.Min(100.0, row.SiteProgressReinforcementPercent))
                        .ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (SiteProgressFormworkPercentTextBox != null)
            {
                SiteProgressFormworkPercentTextBox.Text =
                    Math.Max(0.0, Math.Min(100.0, row.SiteProgressFormworkPercent))
                        .ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (SiteProgressVolumePercentTextBox != null)
            {
                SiteProgressVolumePercentTextBox.Text =
                    Math.Max(0.0, Math.Min(100.0, row.SiteProgressVolumePercent))
                        .ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        private void OnSiteProgressCopyExcelClick(object sender, RoutedEventArgs e)
        {
            if (!TryBuildSiteProgressClipboardPayload(out string tsv, out int rowCount))
            {
                ShowStatus("Site Progress: no summary rows to copy.");
                return;
            }

            try
            {
                Clipboard.SetDataObject(tsv, true);
                ShowStatus($"Site Progress copied to clipboard ({rowCount} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus($"Copy failed: {ex.Message}");
            }
        }

        private void OnSiteProgressExportExcelClick(object sender, RoutedEventArgs e)
        {
            List<SiteProgressSummaryRow> combined = _siteProgressCombinedRows.ToList();
            List<SiteProgressSummaryRow> byBuilding = _siteProgressByBuildingRows.ToList();
            List<SiteProgressElementDetailRow> elementRows = _siteProgressElementRows.ToList();
            if (combined.Count == 0 && byBuilding.Count == 0 && elementRows.Count == 0)
            {
                ShowStatus("Site Progress: no summary rows to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Site Progress Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"SiteProgress_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                ExportSiteProgressRowsToExcel(combined, byBuilding, elementRows, dialog.FileName);
                int count = combined.Count + byBuilding.Count + elementRows.Count;
                ShowStatus($"Site Progress exported: {dialog.FileName} ({count} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus($"Export failed: {ex.Message}");
            }
        }

        private void OnSiteProgressImportExcelClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Site Progress Excel",
                Filter = "Excel files (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                ReadSiteProgressFromExcel(dialog.FileName, out List<SiteProgressSummaryRow> combined, out List<SiteProgressSummaryRow> byBuilding, out int importedSheets);

                int rowCount = (combined?.Count ?? 0) + (byBuilding?.Count ?? 0);
                if (rowCount == 0)
                {
                    ShowStatus("Site Progress import: no valid rows found in Excel.");
                    return;
                }

                string message = $"Site Progress imported: {rowCount} row(s), sheets: {importedSheets}.";
                UpdateSiteProgressSummary(combined, byBuilding, message);
            }
            catch (Exception ex)
            {
                ShowStatus("Site Progress import failed: " + ex.Message);
            }
        }

        private static DataTable BuildPowerBiSiteProgressElementFact(DateTime snapshotUtc, IEnumerable<SiteProgressElementDetailRow> rows)
        {
            var fact = new DataTable("fact_site_progress_element");
            fact.Columns.Add("snapshot_utc", typeof(string));
            fact.Columns.Add("element_id", typeof(int));
            fact.Columns.Add("unique_id", typeof(string));
            fact.Columns.Add("structural_plan", typeof(string));
            fact.Columns.Add("building_level", typeof(string));
            fact.Columns.Add("structure_element", typeof(string));
            fact.Columns.Add("category", typeof(string));
            fact.Columns.Add("family_name", typeof(string));
            fact.Columns.Add("type_name", typeof(string));
            fact.Columns.Add("reinforcement_kg", typeof(double));
            fact.Columns.Add("formwork_m2", typeof(double));
            fact.Columns.Add("volume_m3", typeof(double));
            fact.Columns.Add("reinforcement_percent", typeof(double));
            fact.Columns.Add("formwork_percent", typeof(double));
            fact.Columns.Add("volume_percent", typeof(double));
            fact.Columns.Add("reinforcement_completed_kg", typeof(double));
            fact.Columns.Add("formwork_completed_m2", typeof(double));
            fact.Columns.Add("volume_completed_m3", typeof(double));
            fact.Columns.Add("progress_percent", typeof(double));
            fact.Columns.Add("status", typeof(string));

            foreach (SiteProgressElementDetailRow item in rows ?? Enumerable.Empty<SiteProgressElementDetailRow>())
            {
                if (item == null)
                {
                    continue;
                }

                DataRow row = fact.NewRow();
                row["snapshot_utc"] = snapshotUtc.ToString("o", CultureInfo.InvariantCulture);
                row["element_id"] = item.ElementId;
                row["unique_id"] = item.UniqueId ?? "";
                row["structural_plan"] = item.StructuralPlan ?? "";
                row["building_level"] = item.BuildingLevel ?? "";
                row["structure_element"] = item.StructureElement ?? "";
                row["category"] = item.Category ?? "";
                row["family_name"] = item.FamilyName ?? "";
                row["type_name"] = item.TypeName ?? "";
                row["reinforcement_kg"] = item.ReinforcementKg;
                row["formwork_m2"] = item.FormworkM2;
                row["volume_m3"] = item.VolumeM3;
                row["reinforcement_percent"] = item.SiteProgressReinforcementPercent;
                row["formwork_percent"] = item.SiteProgressFormworkPercent;
                row["volume_percent"] = item.SiteProgressVolumePercent;
                row["reinforcement_completed_kg"] = item.ReinforcementCompletedKg;
                row["formwork_completed_m2"] = item.FormworkCompletedM2;
                row["volume_completed_m3"] = item.VolumeCompletedM3;
                row["progress_percent"] = item.ProgressPercent;
                row["status"] = item.Status ?? "";
                fact.Rows.Add(row);
            }

            return fact;
        }

        private static DataTable BuildPowerBiSiteProgressSummaryFact(DateTime snapshotUtc, IEnumerable<SiteProgressSummaryRow> rows)
        {
            var fact = new DataTable("fact_site_progress_summary");
            fact.Columns.Add("snapshot_utc", typeof(string));
            fact.Columns.Add("group_name", typeof(string));
            fact.Columns.Add("building_level", typeof(string));
            fact.Columns.Add("structure_element", typeof(string));
            fact.Columns.Add("type_name", typeof(string));
            fact.Columns.Add("element_count", typeof(int));
            fact.Columns.Add("reinforcement_kg", typeof(double));
            fact.Columns.Add("formwork_m2", typeof(double));
            fact.Columns.Add("volume_m3", typeof(double));
            fact.Columns.Add("reinforcement_percent", typeof(double));
            fact.Columns.Add("formwork_percent", typeof(double));
            fact.Columns.Add("volume_percent", typeof(double));
            fact.Columns.Add("reinforcement_completed_kg", typeof(double));
            fact.Columns.Add("formwork_completed_m2", typeof(double));
            fact.Columns.Add("volume_completed_m3", typeof(double));
            fact.Columns.Add("total_boq", typeof(double));
            fact.Columns.Add("completed_boq", typeof(double));
            fact.Columns.Add("remaining_boq", typeof(double));
            fact.Columns.Add("progress_percent", typeof(double));
            fact.Columns.Add("status", typeof(string));

            foreach (SiteProgressSummaryRow item in rows ?? Enumerable.Empty<SiteProgressSummaryRow>())
            {
                if (item == null)
                {
                    continue;
                }

                DataRow row = fact.NewRow();
                row["snapshot_utc"] = snapshotUtc.ToString("o", CultureInfo.InvariantCulture);
                row["group_name"] = item.GroupName ?? "";
                row["building_level"] = item.BuildingLevel ?? "";
                row["structure_element"] = item.StructureElement ?? "";
                row["type_name"] = item.TypeName ?? "";
                row["element_count"] = item.ElementCount;
                row["reinforcement_kg"] = item.ReinforcementKg;
                row["formwork_m2"] = item.FormworkM2;
                row["volume_m3"] = item.VolumeM3;
                row["reinforcement_percent"] = item.SiteProgressReinforcementPercent;
                row["formwork_percent"] = item.SiteProgressFormworkPercent;
                row["volume_percent"] = item.SiteProgressVolumePercent;
                row["reinforcement_completed_kg"] = item.ReinforcementCompletedKg;
                row["formwork_completed_m2"] = item.FormworkCompletedM2;
                row["volume_completed_m3"] = item.VolumeCompletedM3;
                row["total_boq"] = item.TotalBoq;
                row["completed_boq"] = item.CompletedBoq;
                row["remaining_boq"] = item.RemainingBoq;
                row["progress_percent"] = item.ProgressPercent;
                row["status"] = item.Status ?? "";
                fact.Rows.Add(row);
            }

            return fact;
        }

        private static DataTable BuildPowerBiSiteProgress3DBridgeFact(DateTime snapshotUtc, IEnumerable<SiteProgressElementDetailRow> rows)
        {
            var fact = new DataTable("fact_site_progress_3d_bridge");
            fact.Columns.Add("snapshot_utc", typeof(string));
            fact.Columns.Add("element_id", typeof(int));
            fact.Columns.Add("element_id_text", typeof(string));
            fact.Columns.Add("unique_id", typeof(string));
            fact.Columns.Add("application_id", typeof(string));
            fact.Columns.Add("object_id", typeof(string));
            fact.Columns.Add("building_level", typeof(string));
            fact.Columns.Add("structure_element", typeof(string));
            fact.Columns.Add("category", typeof(string));
            fact.Columns.Add("family_name", typeof(string));
            fact.Columns.Add("type_name", typeof(string));
            fact.Columns.Add("status", typeof(string));
            fact.Columns.Add("progress_ratio", typeof(double));
            fact.Columns.Add("progress_percent", typeof(double));
            fact.Columns.Add("progress_color_hex", typeof(string));
            fact.Columns.Add("reinforcement_kg", typeof(double));
            fact.Columns.Add("formwork_m2", typeof(double));
            fact.Columns.Add("volume_m3", typeof(double));
            fact.Columns.Add("reinforcement_completed_kg", typeof(double));
            fact.Columns.Add("formwork_completed_m2", typeof(double));
            fact.Columns.Add("volume_completed_m3", typeof(double));
            fact.Columns.Add("reinforcement_remaining_kg", typeof(double));
            fact.Columns.Add("formwork_remaining_m2", typeof(double));
            fact.Columns.Add("volume_remaining_m3", typeof(double));
            fact.Columns.Add("rebar_progress_ratio", typeof(double));
            fact.Columns.Add("formwork_progress_ratio", typeof(double));
            fact.Columns.Add("volume_progress_ratio", typeof(double));

            foreach (SiteProgressElementDetailRow item in rows ?? Enumerable.Empty<SiteProgressElementDetailRow>())
            {
                if (item == null)
                {
                    continue;
                }

                double totalRebar = Math.Max(0.0, item.ReinforcementKg);
                double totalFormwork = Math.Max(0.0, item.FormworkM2);
                double totalVolume = Math.Max(0.0, item.VolumeM3);

                double completedRebar = Math.Max(0.0, Math.Min(totalRebar, item.ReinforcementCompletedKg));
                double completedFormwork = Math.Max(0.0, Math.Min(totalFormwork, item.FormworkCompletedM2));
                double completedVolume = Math.Max(0.0, Math.Min(totalVolume, item.VolumeCompletedM3));

                double rebarRatio = ResolvePowerBiProgressRatio(completedRebar, totalRebar, item.SiteProgressReinforcementPercent);
                double formworkRatio = ResolvePowerBiProgressRatio(completedFormwork, totalFormwork, item.SiteProgressFormworkPercent);
                double volumeRatio = ResolvePowerBiProgressRatio(completedVolume, totalVolume, item.SiteProgressVolumePercent);
                double progressRatio = NormalizePowerBiProgressRatio((rebarRatio + formworkRatio + volumeRatio) / 3.0);

                string status = string.IsNullOrWhiteSpace(item.Status)
                    ? BuildPowerBiSiteProgressStatus(rebarRatio, formworkRatio, volumeRatio)
                    : item.Status.Trim();

                string progressColorHex = GetPowerBiProgressColorHex(progressRatio, status);
                int elementId = Math.Max(0, item.ElementId);
                string elementIdText = elementId > 0
                    ? elementId.ToString(CultureInfo.InvariantCulture)
                    : "";
                string uniqueId = (item.UniqueId ?? "").Trim();

                DataRow row = fact.NewRow();
                row["snapshot_utc"] = snapshotUtc.ToString("o", CultureInfo.InvariantCulture);
                row["element_id"] = elementId;
                row["element_id_text"] = elementIdText;
                row["unique_id"] = uniqueId;
                row["application_id"] = uniqueId;
                row["object_id"] = "";
                row["building_level"] = item.BuildingLevel ?? "";
                row["structure_element"] = item.StructureElement ?? "";
                row["category"] = item.Category ?? "";
                row["family_name"] = item.FamilyName ?? "";
                row["type_name"] = item.TypeName ?? "";
                row["status"] = status;
                row["progress_ratio"] = progressRatio;
                row["progress_percent"] = progressRatio * 100.0;
                row["progress_color_hex"] = progressColorHex;
                row["reinforcement_kg"] = totalRebar;
                row["formwork_m2"] = totalFormwork;
                row["volume_m3"] = totalVolume;
                row["reinforcement_completed_kg"] = completedRebar;
                row["formwork_completed_m2"] = completedFormwork;
                row["volume_completed_m3"] = completedVolume;
                row["reinforcement_remaining_kg"] = Math.Max(0.0, totalRebar - completedRebar);
                row["formwork_remaining_m2"] = Math.Max(0.0, totalFormwork - completedFormwork);
                row["volume_remaining_m3"] = Math.Max(0.0, totalVolume - completedVolume);
                row["rebar_progress_ratio"] = rebarRatio;
                row["formwork_progress_ratio"] = formworkRatio;
                row["volume_progress_ratio"] = volumeRatio;
                fact.Rows.Add(row);
            }

            return fact;
        }

        private static DataTable BuildPowerBiSiteProgressFlatTable(
            IEnumerable<SiteProgressSummaryRow> summaryRows,
            IEnumerable<SiteProgressElementDetailRow> elementRows)
        {
            var flat = new DataTable("site_progress_flat");
            flat.Columns.Add("building_level", typeof(string));
            flat.Columns.Add("structure_element", typeof(string));
            flat.Columns.Add("type", typeof(string));
            flat.Columns.Add("element_qty", typeof(int));
            flat.Columns.Add("boq_reinforcement_kg", typeof(double));
            flat.Columns.Add("boq_formwork_m2", typeof(double));
            flat.Columns.Add("boq_volume_m3", typeof(double));
            flat.Columns.Add("site_progress_reinforcement_pct", typeof(double));
            flat.Columns.Add("site_progress_formwork_pct", typeof(double));
            flat.Columns.Add("site_progress_volume_pct", typeof(double));
            flat.Columns.Add("boq_completed_reinforcement_kg", typeof(double));
            flat.Columns.Add("boq_completed_formwork_m2", typeof(double));
            flat.Columns.Add("boq_completed_volume_m3", typeof(double));
            flat.Columns.Add("status", typeof(string));

            List<SiteProgressSummaryRow> summaries = (summaryRows ?? Enumerable.Empty<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .ToList();

            if (summaries.Count > 0)
            {
                foreach (SiteProgressSummaryRow item in summaries)
                {
                    DataRow row = flat.NewRow();
                    row["building_level"] = item.BuildingLevel ?? "";
                    row["structure_element"] = item.StructureElement ?? "";
                    row["type"] = item.TypeName ?? "";
                    row["element_qty"] = Math.Max(0, item.ElementCount);
                    row["boq_reinforcement_kg"] = Math.Max(0.0, item.ReinforcementKg);
                    row["boq_formwork_m2"] = Math.Max(0.0, item.FormworkM2);
                    row["boq_volume_m3"] = Math.Max(0.0, item.VolumeM3);
                    double pReinf = ResolvePowerBiProgressRatio(item.ReinforcementCompletedKg, item.ReinforcementKg, item.SiteProgressReinforcementPercent);
                    double pForm = ResolvePowerBiProgressRatio(item.FormworkCompletedM2, item.FormworkM2, item.SiteProgressFormworkPercent);
                    double pVol = ResolvePowerBiProgressRatio(item.VolumeCompletedM3, item.VolumeM3, item.SiteProgressVolumePercent);
                    row["site_progress_reinforcement_pct"] = pReinf;
                    row["site_progress_formwork_pct"] = pForm;
                    row["site_progress_volume_pct"] = pVol;
                    row["boq_completed_reinforcement_kg"] = Math.Max(0.0, item.ReinforcementCompletedKg);
                    row["boq_completed_formwork_m2"] = Math.Max(0.0, item.FormworkCompletedM2);
                    row["boq_completed_volume_m3"] = Math.Max(0.0, item.VolumeCompletedM3);
                    row["status"] = string.IsNullOrWhiteSpace(item.Status)
                        ? BuildPowerBiSiteProgressStatus(pReinf, pForm, pVol)
                        : item.Status.Trim();
                    flat.Rows.Add(row);
                }

                return flat;
            }

            var grouped = (elementRows ?? Enumerable.Empty<SiteProgressElementDetailRow>())
                .Where(r => r != null)
                .GroupBy(
                    r => new
                    {
                        BuildingLevel = r.BuildingLevel ?? "",
                        StructureElement = r.StructureElement ?? "",
                        TypeName = r.TypeName ?? ""
                    })
                .OrderBy(g => g.Key.StructureElement, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.TypeName, StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                List<SiteProgressElementDetailRow> items = group.ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                double boqReinf = items.Sum(x => Math.Max(0.0, x.ReinforcementKg));
                double boqForm = items.Sum(x => Math.Max(0.0, x.FormworkM2));
                double boqVol = items.Sum(x => Math.Max(0.0, x.VolumeM3));
                double completedReinf = items.Sum(x => Math.Max(0.0, x.ReinforcementCompletedKg));
                double completedForm = items.Sum(x => Math.Max(0.0, x.FormworkCompletedM2));
                double completedVol = items.Sum(x => Math.Max(0.0, x.VolumeCompletedM3));

                double pReinf = ResolvePowerBiProgressRatio(completedReinf, boqReinf, items.Average(x => x.SiteProgressReinforcementPercent));
                double pForm = ResolvePowerBiProgressRatio(completedForm, boqForm, items.Average(x => x.SiteProgressFormworkPercent));
                double pVol = ResolvePowerBiProgressRatio(completedVol, boqVol, items.Average(x => x.SiteProgressVolumePercent));

                SiteProgressElementDetailRow first = items[0];
                DataRow row = flat.NewRow();
                row["building_level"] = first.BuildingLevel ?? "";
                row["structure_element"] = first.StructureElement ?? "";
                row["type"] = first.TypeName ?? "";
                row["element_qty"] = items.Count;
                row["boq_reinforcement_kg"] = boqReinf;
                row["boq_formwork_m2"] = boqForm;
                row["boq_volume_m3"] = boqVol;
                row["site_progress_reinforcement_pct"] = pReinf;
                row["site_progress_formwork_pct"] = pForm;
                row["site_progress_volume_pct"] = pVol;
                row["boq_completed_reinforcement_kg"] = completedReinf;
                row["boq_completed_formwork_m2"] = completedForm;
                row["boq_completed_volume_m3"] = completedVol;
                row["status"] = BuildPowerBiSiteProgressStatus(pReinf, pForm, pVol);
                flat.Rows.Add(row);
            }

            return flat;
        }

        private static string BuildPowerBiSiteProgressStatus(double reinforcementRatio, double formworkRatio, double volumeRatio)
        {
            double max = Math.Max(reinforcementRatio, Math.Max(formworkRatio, volumeRatio));
            double min = Math.Min(reinforcementRatio, Math.Min(formworkRatio, volumeRatio));
            if (max <= 1e-9)
            {
                return "Not Started";
            }

            if (min >= 0.9999 && max >= 0.9999)
            {
                return "Completed";
            }

            return "In Progress";
        }

        private bool TryGetSiteProgressPercents(out double reinforcementPercent, out double formworkPercent, out double volumePercent)
        {
            formworkPercent = 0.0;
            volumePercent = 0.0;

            if (!TryParseSiteProgressPercent(SiteProgressReinforcementPercentTextBox?.Text, out reinforcementPercent))
            {
                ShowStatus("Site Progress: Reinforcement(%) must be a number between 0 and 100.");
                return false;
            }
            if (!TryParseSiteProgressPercent(SiteProgressFormworkPercentTextBox?.Text, out formworkPercent))
            {
                ShowStatus("Site Progress: Formwork(%) must be a number between 0 and 100.");
                return false;
            }
            if (!TryParseSiteProgressPercent(SiteProgressVolumePercentTextBox?.Text, out volumePercent))
            {
                ShowStatus("Site Progress: Volume(%) must be a number between 0 and 100.");
                return false;
            }

            return true;
        }

        private static bool TryParseSiteProgressPercent(string raw, out double percent)
        {
            string normalized = (raw ?? "").Trim().TrimEnd('%').Trim();
            if (string.IsNullOrWhiteSpace(normalized)) normalized = "0";

            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out percent) &&
                !double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out percent))
            {
                return false;
            }

            if (percent < 0.0 || percent > 100.0)
            {
                return false;
            }

            return true;
        }

        private QsScope GetSiteProgressScope()
        {
            if (SiteProgressScopeSelectionRadio?.IsChecked == true)
            {
                return QsScope.CurrentSelection;
            }
            if (SiteProgressScopeViewRadio?.IsChecked == true)
            {
                return QsScope.CurrentView;
            }

            return QsScope.EntireModel;
        }

        private void InitializeSiteProgressDefaults()
        {
            if (SiteProgressReinforcementPercentTextBox != null && string.IsNullOrWhiteSpace(SiteProgressReinforcementPercentTextBox.Text))
            {
                SiteProgressReinforcementPercentTextBox.Text = "0";
            }
            if (SiteProgressFormworkPercentTextBox != null && string.IsNullOrWhiteSpace(SiteProgressFormworkPercentTextBox.Text))
            {
                SiteProgressFormworkPercentTextBox.Text = "0";
            }
            if (SiteProgressVolumePercentTextBox != null && string.IsNullOrWhiteSpace(SiteProgressVolumePercentTextBox.Text))
            {
                SiteProgressVolumePercentTextBox.Text = "0";
            }

            if (SiteProgressSetModeRadio != null && SiteProgressAddModeRadio != null)
            {
                if (SiteProgressSetModeRadio.IsChecked != true && SiteProgressAddModeRadio.IsChecked != true)
                {
                    SiteProgressSetModeRadio.IsChecked = true;
                }
            }

            if (SiteProgressScopeSelectionRadio != null &&
                SiteProgressScopeViewRadio != null &&
                SiteProgressScopeAllRadio != null)
            {
                if (SiteProgressScopeSelectionRadio.IsChecked != true &&
                    SiteProgressScopeViewRadio.IsChecked != true &&
                    SiteProgressScopeAllRadio.IsChecked != true)
                {
                    SiteProgressScopeAllRadio.IsChecked = true;
                }
            }

            if (SiteProgressSummaryText != null)
            {
                SiteProgressSummaryText.Text = "Select summary scope and click Refresh.";
            }

            if (SiteProgressStructureFilterCombo != null)
            {
                SiteProgressStructureFilterCombo.ItemsSource = SiteProgressStructureFilterOptions;
                SiteProgressStructureFilterCombo.SelectedIndex = 0;
            }

            if (SiteProgressBuildingLevelFilterCombo != null)
            {
                SiteProgressBuildingLevelFilterCombo.ItemsSource = new List<string> { SiteProgressFilterAll };
                SiteProgressBuildingLevelFilterCombo.SelectedIndex = 0;
            }

            if (SiteProgressPivotModeCombo != null)
            {
                SiteProgressPivotModeCombo.ItemsSource = new List<string>
                {
                    "Pivot by Floor + Element",
                    "Pivot by BuildingLevel",
                    "Pivot by Structure Element"
                };
                SiteProgressPivotModeCombo.SelectedIndex = 0;
            }

            if (SiteProgressPivotMetricCombo != null)
            {
                SiteProgressPivotMetricCombo.ItemsSource = new List<string>
                {
                    "Volume(%)",
                    "Formwork(%)",
                    "Reinforcement(%)"
                };
                SiteProgressPivotMetricCombo.SelectedIndex = 0;
            }

            if (SiteProgressDashboardValueModeCombo != null)
            {
                SiteProgressDashboardValueModeCombo.ItemsSource = new List<string>
                {
                    "Absolute BOQ",
                    "Progress (%)"
                };
                SiteProgressDashboardValueModeCombo.SelectedIndex = 0;
            }

            if (SiteProgressDashboardZoomXSlider != null)
            {
                SiteProgressDashboardZoomXSlider.Value = 1.0;
            }
            if (SiteProgressDashboardZoomYSlider != null)
            {
                SiteProgressDashboardZoomYSlider.Value = 1.0;
            }
            ApplySiteProgressDashboardChartZoom();

            if (SiteProgressDashboardFocusChartToggle != null)
            {
                SiteProgressDashboardFocusChartToggle.IsChecked = false;
            }
            ApplySiteProgressDashboardFocusLayout(false);

            UpdateProgressDashboardStructureRows();
            UpdateProgressDashboardCharts();
            AutoFitSiteProgressGridHeaders();
        }

        private int ApplySiteProgressRowsToSpecificPmTaskRows(
            DataTable table,
            IEnumerable<DataRow> targetRows,
            IEnumerable<SiteProgressSummaryRow> sourceRows,
            bool updatePercentComplete)
        {
            if (table == null)
            {
                return 0;
            }

            List<DataRow> normalizedTargets = (targetRows ?? Enumerable.Empty<DataRow>())
                .Where(r => r != null && r.RowState != DataRowState.Deleted)
                .Distinct()
                .ToList();
            if (normalizedTargets.Count == 0)
            {
                return 0;
            }

            int mapped = 0;
            foreach (DataRow target in normalizedTargets)
            {
                mapped += ApplySiteProgressRowsToSpecificPmTaskRow(
                    table,
                    target,
                    sourceRows,
                    updatePercentComplete);
            }

            return mapped;
        }

        private void OnSiteProgressCombinedGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is DataGrid sourceGrid))
            {
                return;
            }

            _siteProgressDragStartPoint = e.GetPosition(sourceGrid);
        }

        private void OnSiteProgressCombinedGridPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is DataGrid sourceGrid) || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            System.Windows.Point current = e.GetPosition(sourceGrid);
            if (Math.Abs(current.X - _siteProgressDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _siteProgressDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            List<SiteProgressSummaryRow> selectedRows = sourceGrid.SelectedItems
                .OfType<SiteProgressSummaryRow>()
                .Where(r => r != null)
                .Select(CloneSiteProgressRowShallow)
                .ToList();
            if (selectedRows.Count == 0 && sourceGrid.SelectedItem is SiteProgressSummaryRow single)
            {
                selectedRows.Add(CloneSiteProgressRowShallow(single));
            }

            List<SiteProgressSummaryRow> selectedRowsFromCells = GetSiteProgressRowsFromSelectedCells(sourceGrid);
            if (selectedRowsFromCells.Count > selectedRows.Count)
            {
                selectedRows = selectedRowsFromCells;
            }

            bool includeRowPayload = selectedRows.Count > 0 && IsSiteProgressGridSelectionLikelyRowBased(sourceGrid, selectedRows.Count);
            if (!includeRowPayload &&
                selectedRows.Count > 0 &&
                ShouldPreferSiteProgressRowPayloadForSelectedCells(sourceGrid))
            {
                includeRowPayload = true;
            }
            string textPayload = "";
            if (TryBuildPmDetailProgressClipboardPayload(sourceGrid, out string selectedTsv, out _, out _))
            {
                textPayload = selectedTsv;
            }
            else if (selectedRows.Count > 0)
            {
                textPayload = BuildSiteProgressDragText(selectedRows);
            }

            if (!includeRowPayload && string.IsNullOrWhiteSpace(textPayload))
            {
                return;
            }
            if (includeRowPayload && selectedRows.Count == 0 && string.IsNullOrWhiteSpace(textPayload))
            {
                return;
            }

            try
            {
                var data = new DataObject();
                if (includeRowPayload && selectedRows.Count > 0)
                {
                    data.SetData(SiteProgressInternalDragRowsFormat, selectedRows);
                }
                if (!string.IsNullOrWhiteSpace(textPayload))
                {
                    data.SetText(textPayload);
                }
                DragDrop.DoDragDrop(sourceGrid, data, DragDropEffects.Copy);
            }
            catch (Exception ex)
            {
                ShowStatus("Detail Progress drag failed: " + ex.Message);
            }
        }

        private static bool IsSiteProgressGridSelectionLikelyRowBased(DataGrid grid, int selectedRowCount)
        {
            if (grid == null || selectedRowCount <= 0)
            {
                return false;
            }

            if (grid.SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                return true;
            }

            int selectedCellCount = grid.SelectedCells?.Count ?? 0;
            if (selectedCellCount <= 0)
            {
                return true;
            }

            int visibleColumnCount = grid.Columns.Count(c => c != null && c.Visibility == System.Windows.Visibility.Visible);
            if (visibleColumnCount <= 0)
            {
                visibleColumnCount = Math.Max(1, grid.Columns.Count);
            }

            int rowLikeThreshold = Math.Max(1, selectedRowCount * Math.Max(1, visibleColumnCount - 1));
            return selectedCellCount >= rowLikeThreshold;
        }

        private static List<SiteProgressSummaryRow> GetSiteProgressRowsFromSelectedCells(DataGrid grid)
        {
            var rows = new List<SiteProgressSummaryRow>();
            if (grid?.SelectedCells == null || grid.SelectedCells.Count == 0)
            {
                return rows;
            }

            List<SiteProgressSummaryRow> selectedRows = grid.SelectedCells
                .Select(cell => cell.Item as SiteProgressSummaryRow)
                .Where(r => r != null)
                .ToList();
            if (selectedRows.Count == 0)
            {
                return rows;
            }

            List<SiteProgressSummaryRow> viewRows = System.Windows.Data.CollectionViewSource
                .GetDefaultView(grid.ItemsSource)?
                .Cast<object>()
                .OfType<SiteProgressSummaryRow>()
                .Where(r => r != null)
                .ToList() ?? new List<SiteProgressSummaryRow>();
            if (viewRows.Count == 0)
            {
                viewRows = grid.Items
                    .OfType<SiteProgressSummaryRow>()
                    .Where(r => r != null)
                    .ToList();
            }

            if (viewRows.Count > 0)
            {
                var rowIndexMap = new Dictionary<SiteProgressSummaryRow, int>();
                for (int i = 0; i < viewRows.Count; i++)
                {
                    SiteProgressSummaryRow viewRow = viewRows[i];
                    if (viewRow != null && !rowIndexMap.ContainsKey(viewRow))
                    {
                        rowIndexMap[viewRow] = i;
                    }
                }

                var selectedIndexes = new SortedSet<int>();
                foreach (SiteProgressSummaryRow selectedRow in selectedRows)
                {
                    if (selectedRow != null && rowIndexMap.TryGetValue(selectedRow, out int rowIndex))
                    {
                        selectedIndexes.Add(rowIndex);
                    }
                }

                if (selectedIndexes.Count > 0)
                {
                    foreach (int rowIndex in selectedIndexes)
                    {
                        if (rowIndex >= 0 && rowIndex < viewRows.Count && viewRows[rowIndex] != null)
                        {
                            rows.Add(CloneSiteProgressRowShallow(viewRows[rowIndex]));
                        }
                    }

                    if (rows.Count > 0)
                    {
                        return rows;
                    }
                }
            }

            foreach (SiteProgressSummaryRow selectedRow in selectedRows.Distinct())
            {
                if (selectedRow != null)
                {
                    rows.Add(CloneSiteProgressRowShallow(selectedRow));
                }
            }

            return rows;
        }

        private static bool ShouldPreferSiteProgressRowPayloadForSelectedCells(DataGrid grid)
        {
            if (grid?.SelectedCells == null || grid.SelectedCells.Count == 0)
            {
                return false;
            }

            List<DataGridCellInfo> selectedCells = grid.SelectedCells
                .Where(c => c.Column != null && c.Item is SiteProgressSummaryRow)
                .ToList();
            if (selectedCells.Count == 0)
            {
                return false;
            }

            List<DataGridColumn> selectedColumns = selectedCells
                .Select(c => c.Column)
                .Where(c => c != null)
                .Distinct()
                .ToList();
            if (selectedColumns.Count != 1)
            {
                return false;
            }

            string key = ResolvePmDetailGridColumnKey(selectedColumns[0]);
            return IsPmDetailPriorityDragColumnKey(key);
        }

        private static string BuildSiteProgressDragText(IEnumerable<SiteProgressSummaryRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Structure Element\tBuildingLevel\tType\tReinforcement(Kg)\tFormwork(m2)\tVolume(m3)\tReinforcement(%)\tFormwork(%)\tVolume(%)\tProgress(%)\tElement IDs");
            foreach (SiteProgressSummaryRow row in rows ?? Enumerable.Empty<SiteProgressSummaryRow>())
            {
                ResolveSiteProgressStagePercents(row, out double reinforcement, out double formwork, out double volume);
                double progress = ResolveSiteProgressRowPercent(row);
                string elementIds = NormalizePmUpdateProgressElementIdsText(
                    string.Join(",", (row.ElementIds ?? new List<int>()).Where(id => id > 0)));
                sb.Append((row.StructureElement ?? "").Trim()).Append('\t')
                  .Append((row.BuildingLevel ?? "").Trim()).Append('\t')
                  .Append((row.TypeName ?? "").Trim()).Append('\t')
                  .Append(Math.Max(0.0, row.ReinforcementKg).ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Math.Max(0.0, row.FormworkM2).ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Math.Max(0.0, row.VolumeM3).ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(reinforcement.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(formwork.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(volume.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(progress.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(elementIds)
                  .AppendLine();
            }

            return sb.ToString();
        }

        private static bool TryGetDroppedSiteProgressRows(DragEventArgs e, out List<SiteProgressSummaryRow> rows)
        {
            rows = new List<SiteProgressSummaryRow>();
            if (e?.Data == null)
            {
                return false;
            }

            try
            {
                if (e.Data.GetDataPresent(SiteProgressInternalDragRowsFormat))
                {
                    object raw = e.Data.GetData(SiteProgressInternalDragRowsFormat);
                    if (raw is IEnumerable<SiteProgressSummaryRow> typed)
                    {
                        rows = typed
                            .Where(r => r != null)
                            .Select(CloneSiteProgressRowShallow)
                            .ToList();
                    }
                }

                List<SiteProgressSummaryRow> textRows = null;
                if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string text = Convert.ToString(e.Data.GetData(DataFormats.Text), CultureInfo.InvariantCulture) ?? "";
                    textRows = ParseSiteProgressRowsFromDroppedText(text);
                }

                if (textRows != null && textRows.Count > rows.Count)
                {
                    rows = textRows;
                }

                if (rows.Count > 0)
                {
                    return true;
                }
            }
            catch
            {
                rows = new List<SiteProgressSummaryRow>();
                return false;
            }

            return false;
        }

        private static List<SiteProgressSummaryRow> ParseSiteProgressRowsFromDroppedText(string text)
        {
            var rows = new List<SiteProgressSummaryRow>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return rows;
            }

            string[] lines = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return rows;
            }

            string[] headers = lines[0].Split('\t');
            int idxStructure = IndexOfDroppedHeader(headers, "Structure Element", "structure_element", "structure");
            int idxLevel = IndexOfDroppedHeader(headers, "BuildingLevel", "building_level", "level");
            int idxType = IndexOfDroppedHeader(headers, "Type", "type", "type_name", "Structure Type");
            int idxReinforcement = IndexOfDroppedHeader(headers, "Reinforcement(%)", "Reinforcement %", "reinforcement_percent", "reinforcement");
            int idxFormwork = IndexOfDroppedHeader(headers, "Formwork(%)", "Formwork %", "formwork_percent", "formwork");
            int idxVolume = IndexOfDroppedHeader(headers, "Volume(%)", "Volume %", "volume_percent", "volume");
            int idxProgress = IndexOfDroppedHeader(headers, "Progress(%)", "Progress %", "progress_percent", "progress");
            int idxReinforcementKg = IndexOfDroppedHeader(headers, "Reinforcement(Kg)", "Reinforcement (Kg)", "Reinforcement Kg");
            int idxFormworkM2 = IndexOfDroppedHeader(headers, "Formwork(m2)", "Formwork (m2)", "Formwork m2");
            int idxVolumeM3 = IndexOfDroppedHeader(headers, "Volume(m3)", "Volume (m3)", "Volume m3");
            int idxReinforcementCompletedKg = IndexOfDroppedHeader(headers, "Reinforcement Completed(Kg)", "Reinforcement Completed");
            int idxFormworkCompletedM2 = IndexOfDroppedHeader(headers, "Formwork Completed(m2)", "Formwork Completed");
            int idxVolumeCompletedM3 = IndexOfDroppedHeader(headers, "Volume Completed(m3)", "Volume Completed");
            int idxElementIds = IndexOfDroppedHeader(headers, "ElementID", "Element IDs", "ElementIds", "Element IDs (Revit)", "Revit Element IDs");

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split('\t');
                if (cells.Length == 0)
                {
                    continue;
                }

                string structure = idxStructure >= 0 && idxStructure < cells.Length ? (cells[idxStructure] ?? "").Trim() : "";
                string level = idxLevel >= 0 && idxLevel < cells.Length ? (cells[idxLevel] ?? "").Trim() : "";
                string type = idxType >= 0 && idxType < cells.Length ? (cells[idxType] ?? "").Trim() : "";
                double reinforcement = idxReinforcement >= 0 && idxReinforcement < cells.Length
                    ? ConvertToDoubleSafe(cells[idxReinforcement])
                    : double.NaN;
                double formwork = idxFormwork >= 0 && idxFormwork < cells.Length
                    ? ConvertToDoubleSafe(cells[idxFormwork])
                    : double.NaN;
                double volume = idxVolume >= 0 && idxVolume < cells.Length
                    ? ConvertToDoubleSafe(cells[idxVolume])
                    : double.NaN;
                double progress = idxProgress >= 0 && idxProgress < cells.Length
                    ? ConvertToDoubleSafe(cells[idxProgress])
                    : double.NaN;
                double reinforcementKg = idxReinforcementKg >= 0 && idxReinforcementKg < cells.Length
                    ? Math.Max(0.0, ConvertToDoubleSafe(cells[idxReinforcementKg]))
                    : 0.0;
                double formworkM2 = idxFormworkM2 >= 0 && idxFormworkM2 < cells.Length
                    ? Math.Max(0.0, ConvertToDoubleSafe(cells[idxFormworkM2]))
                    : 0.0;
                double volumeM3 = idxVolumeM3 >= 0 && idxVolumeM3 < cells.Length
                    ? Math.Max(0.0, ConvertToDoubleSafe(cells[idxVolumeM3]))
                    : 0.0;
                double reinforcementCompletedKg = idxReinforcementCompletedKg >= 0 && idxReinforcementCompletedKg < cells.Length
                    ? Math.Max(0.0, ConvertToDoubleSafe(cells[idxReinforcementCompletedKg]))
                    : 0.0;
                double formworkCompletedM2 = idxFormworkCompletedM2 >= 0 && idxFormworkCompletedM2 < cells.Length
                    ? Math.Max(0.0, ConvertToDoubleSafe(cells[idxFormworkCompletedM2]))
                    : 0.0;
                double volumeCompletedM3 = idxVolumeCompletedM3 >= 0 && idxVolumeCompletedM3 < cells.Length
                    ? Math.Max(0.0, ConvertToDoubleSafe(cells[idxVolumeCompletedM3]))
                    : 0.0;
                string elementIdsText = idxElementIds >= 0 && idxElementIds < cells.Length
                    ? (cells[idxElementIds] ?? "").Trim()
                    : "";

                if (double.IsNaN(reinforcement) && reinforcementKg > 1e-9 && reinforcementCompletedKg > 1e-9)
                {
                    reinforcement = reinforcementCompletedKg * 100.0 / reinforcementKg;
                }
                if (double.IsNaN(formwork) && formworkM2 > 1e-9 && formworkCompletedM2 > 1e-9)
                {
                    formwork = formworkCompletedM2 * 100.0 / formworkM2;
                }
                if (double.IsNaN(volume) && volumeM3 > 1e-9 && volumeCompletedM3 > 1e-9)
                {
                    volume = volumeCompletedM3 * 100.0 / volumeM3;
                }

                if (double.IsNaN(reinforcement))
                {
                    reinforcement = 0.0;
                }
                if (double.IsNaN(formwork))
                {
                    formwork = 0.0;
                }
                if (double.IsNaN(volume))
                {
                    volume = 0.0;
                }
                if (double.IsNaN(progress))
                {
                    progress = 0.0;
                }

                List<int> elementIds = ParsePmUpdateProgressElementIds(elementIdsText);
                bool hasStructureKeys =
                    !string.IsNullOrWhiteSpace(structure) ||
                    !string.IsNullOrWhiteSpace(level) ||
                    !string.IsNullOrWhiteSpace(type);
                bool hasProgressData =
                    progress > 1e-9 ||
                    reinforcement > 1e-9 ||
                    formwork > 1e-9 ||
                    volume > 1e-9 ||
                    reinforcementKg > 1e-9 ||
                    formworkM2 > 1e-9 ||
                    volumeM3 > 1e-9 ||
                    reinforcementCompletedKg > 1e-9 ||
                    formworkCompletedM2 > 1e-9 ||
                    volumeCompletedM3 > 1e-9;

                if (!hasStructureKeys && elementIds.Count == 0 && !hasProgressData)
                {
                    continue;
                }

                reinforcement = Math.Max(0.0, Math.Min(100.0, reinforcement));
                formwork = Math.Max(0.0, Math.Min(100.0, formwork));
                volume = Math.Max(0.0, Math.Min(100.0, volume));
                progress = Math.Max(0.0, Math.Min(100.0, progress));
                if (progress <= 1e-9)
                {
                    var values = new List<double> { reinforcement, formwork, volume }
                        .Where(v => v > 1e-9)
                        .ToList();
                    progress = values.Count > 0 ? values.Average() : 0.0;
                }
                if (reinforcement <= 1e-9 && formwork <= 1e-9 && volume <= 1e-9 && progress > 1e-9)
                {
                    reinforcement = progress;
                    formwork = progress;
                    volume = progress;
                }

                rows.Add(new SiteProgressSummaryRow
                {
                    StructureElement = structure,
                    BuildingLevel = level,
                    TypeName = type,
                    ElementTypeName = type,
                    ProgressPercent = progress,
                    ReinforcementKg = reinforcementKg,
                    FormworkM2 = formworkM2,
                    VolumeM3 = volumeM3,
                    SiteProgressReinforcementPercent = reinforcement,
                    SiteProgressFormworkPercent = formwork,
                    SiteProgressVolumePercent = volume,
                    ReinforcementCompletedKg = reinforcementCompletedKg,
                    FormworkCompletedM2 = formworkCompletedM2,
                    VolumeCompletedM3 = volumeCompletedM3,
                    ElementIds = elementIds
                });
            }

            return rows;
        }

        private static bool HasPmSiteProgressPayload(SiteProgressSummaryRow row)
        {
            if (row == null)
            {
                return false;
            }

            if (Math.Abs(row.ProgressPercent) > 1e-9)
            {
                return true;
            }

            if (Math.Abs(row.SiteProgressReinforcementPercent) > 1e-9 ||
                Math.Abs(row.SiteProgressFormworkPercent) > 1e-9 ||
                Math.Abs(row.SiteProgressVolumePercent) > 1e-9)
            {
                return true;
            }

            if (Math.Abs(row.ReinforcementCompletedKg) > 1e-9 ||
                Math.Abs(row.FormworkCompletedM2) > 1e-9 ||
                Math.Abs(row.VolumeCompletedM3) > 1e-9)
            {
                return true;
            }

            return string.Equals((row.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase);
        }

        private int ApplySiteProgressRowsToSpecificPmTaskRow(
            DataTable table,
            DataRow targetRow,
            IEnumerable<SiteProgressSummaryRow> sourceRows,
            bool updatePercentComplete)
        {
            if (table == null || targetRow == null || targetRow.RowState == DataRowState.Deleted)
            {
                return 0;
            }

            List<SiteProgressSummaryRow> rows = (sourceRows ?? Enumerable.Empty<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .ToList();
            if (rows.Count == 0)
            {
                return 0;
            }

            var elementIds = new HashSet<int>();
            var structures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            double weightedOverallProgressSum = 0.0;
            double weightedReinforcementProgressSum = 0.0;
            double weightedFormworkProgressSum = 0.0;
            double weightedVolumeProgressSum = 0.0;
            double totalWeight = 0.0;
            bool hasProgressPayload = false;
            double totalReinforcementBoq = 0.0;
            double totalFormworkBoq = 0.0;
            double totalVolumeBoq = 0.0;
            bool hasBoqPayload = false;

            foreach (SiteProgressSummaryRow row in rows)
            {
                if (HasPmSiteProgressPayload(row))
                {
                    ResolveSiteProgressStagePercents(row, out double reinforcement, out double formwork, out double volume);
                    double progress = ResolveSiteProgressRowPercent(row);
                    int elementCount = (row.ElementIds != null && row.ElementIds.Count > 0)
                        ? row.ElementIds.Count
                        : Math.Max(1, row.ElementCount);
                    double weight = Math.Max(1.0, elementCount);
                    weightedOverallProgressSum += progress * weight;
                    weightedReinforcementProgressSum += reinforcement * weight;
                    weightedFormworkProgressSum += formwork * weight;
                    weightedVolumeProgressSum += volume * weight;
                    totalWeight += weight;
                    hasProgressPayload = true;
                }

                double reinforcementBoq = Math.Max(0.0, row.ReinforcementKg);
                double formworkBoq = Math.Max(0.0, row.FormworkM2);
                double volumeBoq = Math.Max(0.0, row.VolumeM3);
                totalReinforcementBoq += reinforcementBoq;
                totalFormworkBoq += formworkBoq;
                totalVolumeBoq += volumeBoq;
                if (reinforcementBoq > 1e-9 || formworkBoq > 1e-9 || volumeBoq > 1e-9)
                {
                    hasBoqPayload = true;
                }

                foreach (int id in row.ElementIds ?? new List<int>())
                {
                    if (id > 0)
                    {
                        elementIds.Add(id);
                    }
                }

                string structure = (row.StructureElement ?? "").Trim();
                string level = (row.BuildingLevel ?? "").Trim();
                string type = (row.TypeName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(structure)) structures.Add(structure);
                if (!string.IsNullOrWhiteSpace(level)) levels.Add(level);
                if (!string.IsNullOrWhiteSpace(type)) types.Add(type);
            }

            double aggregatedOverallProgress = totalWeight > 1e-9
                ? Math.Max(0.0, Math.Min(100.0, weightedOverallProgressSum / totalWeight))
                : 0.0;
            double aggregatedReinforcementProgress = totalWeight > 1e-9
                ? Math.Max(0.0, Math.Min(100.0, weightedReinforcementProgressSum / totalWeight))
                : 0.0;
            double aggregatedFormworkProgress = totalWeight > 1e-9
                ? Math.Max(0.0, Math.Min(100.0, weightedFormworkProgressSum / totalWeight))
                : 0.0;
            double aggregatedVolumeProgress = totalWeight > 1e-9
                ? Math.Max(0.0, Math.Min(100.0, weightedVolumeProgressSum / totalWeight))
                : 0.0;
            double targetProgress = ResolvePmTaskStageProgressPercent(
                ResolvePmTaskProgressStage(targetRow),
                aggregatedReinforcementProgress,
                aggregatedFormworkProgress,
                aggregatedVolumeProgress,
                aggregatedOverallProgress);

            string existingStructureValue = ReadPmUpdateProgressRowString(targetRow, "revit_structure_element");
            string existingLevelValue = ReadPmUpdateProgressRowString(targetRow, "revit_building_level");
            string existingTypeValue = ReadPmUpdateProgressRowString(targetRow, "revit_type");

            string structureValue = !string.IsNullOrWhiteSpace(existingStructureValue)
                ? existingStructureValue
                : ResolvePmMsProjectFilterStructureElement(targetRow);
            if (string.IsNullOrWhiteSpace(structureValue) && structures.Count == 1)
            {
                structureValue = structures.First();
            }

            string levelValue = !string.IsNullOrWhiteSpace(existingLevelValue)
                ? existingLevelValue
                : ResolvePmMsProjectFilterBuildingLevel(targetRow);
            if (string.IsNullOrWhiteSpace(levelValue) && levels.Count == 1)
            {
                levelValue = levels.First();
            }

            string typeValue = !string.IsNullOrWhiteSpace(existingTypeValue)
                ? existingTypeValue
                : ResolvePmMsProjectFilterStructureType(targetRow);
            if (string.IsNullOrWhiteSpace(typeValue) && types.Count == 1)
            {
                typeValue = types.First();
            }

            string existingElementIds = table.Columns.Contains("revit_element_ids")
                ? (Convert.ToString(targetRow["revit_element_ids"], CultureInfo.InvariantCulture) ?? "")
                : "";
            targetRow["revit_structure_element"] = structureValue ?? "";
            targetRow["revit_building_level"] = levelValue ?? "";
            targetRow["revit_type"] = typeValue ?? "";
            if (table.Columns.Contains("revit_element_ids"))
            {
                targetRow["revit_element_ids"] = MergePmUpdateProgressElementIds(existingElementIds, elementIds);
            }

            if (hasBoqPayload)
            {
                ResolvePmTaskBoqValue(
                    targetRow,
                    totalReinforcementBoq,
                    totalFormworkBoq,
                    totalVolumeBoq,
                    out string targetUnit,
                    out double targetBoq);
                if (table.Columns.Contains("unit"))
                {
                    targetRow["unit"] = targetUnit;
                }
                if (table.Columns.Contains("boq"))
                {
                    targetRow["boq"] = targetBoq;
                }
            }

            if (hasProgressPayload && updatePercentComplete)
            {
                targetRow["percent_complete"] = targetProgress;
                targetRow["status"] = ResolvePmProgressStatusFromPercent(targetProgress);
            }

            return 1;
        }

        private int ApplySiteProgressRowsToPmUpdateProgressTable(
            DataTable table,
            IEnumerable<SiteProgressSummaryRow> sourceRows,
            bool updatePercentComplete,
            bool allowFuzzyNameMatch,
            bool requireStrictThreeFieldMatch = false,
            PmAutoMapMatchOptions strictMatchOptions = null)
        {
            if (table == null)
            {
                return 0;
            }

            List<SiteProgressSummaryRow> rows = (sourceRows ?? Enumerable.Empty<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .ToList();
            if (rows.Count == 0)
            {
                return 0;
            }

            PmAutoMapMatchOptions normalizedStrictMatchOptions = NormalizePmAutoMapMatchOptions(strictMatchOptions);

            var byTaskKey = table.Rows
                .Cast<DataRow>()
                .Where(r => r != null && r.RowState != DataRowState.Deleted)
                .Select(r => new
                {
                    Row = r,
                    Key = NormalizePmTaskKey(Convert.ToString(r["task_key"], CultureInfo.InvariantCulture))
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList(), StringComparer.OrdinalIgnoreCase);

            var byStrictMatchKey = table.Rows
                .Cast<DataRow>()
                .Where(r => r != null && r.RowState != DataRowState.Deleted)
                .Select(r => new
                {
                    Row = r,
                    Key = BuildPmAutoMapMatchKey(r, normalizedStrictMatchOptions)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList(), StringComparer.OrdinalIgnoreCase);

            PmAutoMapMatchOptions fallbackStrictMatchOptions = null;
            Dictionary<string, List<DataRow>> byStrictMatchKeyFallback = null;
            if (normalizedStrictMatchOptions.MatchStructureType)
            {
                fallbackStrictMatchOptions = normalizedStrictMatchOptions.Clone();
                fallbackStrictMatchOptions.MatchStructureType = false;
                if (fallbackStrictMatchOptions.ActiveCount > 0)
                {
                    byStrictMatchKeyFallback = table.Rows
                        .Cast<DataRow>()
                        .Where(r => r != null && r.RowState != DataRowState.Deleted)
                        .Select(r => new
                        {
                            Row = r,
                            Key = BuildPmAutoMapMatchKey(r, fallbackStrictMatchOptions)
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList(), StringComparer.OrdinalIgnoreCase);
                }
            }

            int mappedRows = 0;
            var touchedRows = new HashSet<DataRow>();
            var progressByTarget = new Dictionary<DataRow, (double WeightedOverallSum, double WeightedReinforcementSum, double WeightedFormworkSum, double WeightedVolumeSum, double Weight)>();
            var boqByTarget = new Dictionary<DataRow, (double ReinforcementBoq, double FormworkBoq, double VolumeBoq)>();

            foreach (SiteProgressSummaryRow source in rows)
            {
                string exactKey = BuildBoqKey(source.StructureElement, source.BuildingLevel, source.TypeName);
                string levelKey = BuildBoqKey(source.StructureElement, source.BuildingLevel, "(All Types)");
                string structureKey = BuildBoqKey(source.StructureElement, "(All Levels)", "(All Types)");

                List<DataRow> targets = null;
                if (requireStrictThreeFieldMatch)
                {
                    targets = ResolvePmStrictMatchTargets(
                        byStrictMatchKey,
                        BuildPmAutoMapMatchKeysForSource(source, normalizedStrictMatchOptions));

                    if ((targets == null || targets.Count == 0) &&
                        byStrictMatchKeyFallback != null &&
                        fallbackStrictMatchOptions != null)
                    {
                        targets = ResolvePmStrictMatchTargets(
                            byStrictMatchKeyFallback,
                            BuildPmAutoMapMatchKeysForSource(source, fallbackStrictMatchOptions));
                    }
                }
                else if (!byTaskKey.TryGetValue(exactKey, out targets))
                {
                    if (!byTaskKey.TryGetValue(levelKey, out targets))
                    {
                        byTaskKey.TryGetValue(structureKey, out targets);
                    }
                }

                if ((targets == null || targets.Count == 0) && allowFuzzyNameMatch)
                {
                    DataRow best = FindBestPmTaskRowByName(table, source);
                    if (best != null)
                    {
                        targets = new List<DataRow> { best };
                    }
                }

                if (requireStrictThreeFieldMatch && targets != null && targets.Count > 1)
                {
                    List<DataRow> filteredTargets = targets
                        .Where(t => IsPmTaskStructureCompatibleWithSourceRow(t, source))
                        .Distinct()
                        .ToList();
                    if (filteredTargets.Count > 0)
                    {
                        targets = filteredTargets;
                    }

                    targets = SelectBestPmTargetsForSource(source, targets);
                }

                if (targets == null || targets.Count == 0)
                {
                    continue;
                }

                bool sourceHasProgressPayload = HasPmSiteProgressPayload(source);
                double sourceReinforcementBoq = Math.Max(0.0, source.ReinforcementKg);
                double sourceFormworkBoq = Math.Max(0.0, source.FormworkM2);
                double sourceVolumeBoq = Math.Max(0.0, source.VolumeM3);
                bool sourceHasBoqPayload =
                    sourceReinforcementBoq > 1e-9 ||
                    sourceFormworkBoq > 1e-9 ||
                    sourceVolumeBoq > 1e-9;
                double sourceReinforcement = 0.0;
                double sourceFormwork = 0.0;
                double sourceVolume = 0.0;
                double revitProgress = 0.0;
                double sourceWeight = 0.0;
                if (sourceHasProgressPayload)
                {
                    ResolveSiteProgressStagePercents(source, out sourceReinforcement, out sourceFormwork, out sourceVolume);
                    revitProgress = ResolveSiteProgressRowPercent(source);
                    int sourceElementCount = (source.ElementIds != null && source.ElementIds.Count > 0)
                        ? source.ElementIds.Count
                        : Math.Max(1, source.ElementCount);
                    sourceWeight = Math.Max(1.0, sourceElementCount);
                }

                string sourceStructure = (source.StructureElement ?? "").Trim();
                string sourceLevel = (source.BuildingLevel ?? "").Trim();
                string sourceType = (source.TypeName ?? "").Trim();
                foreach (DataRow target in targets)
                {
                    if (target == null || target.RowState == DataRowState.Deleted)
                    {
                        continue;
                    }

                    if (sourceHasProgressPayload)
                    {
                        if (!progressByTarget.TryGetValue(target, out (double WeightedOverallSum, double WeightedReinforcementSum, double WeightedFormworkSum, double WeightedVolumeSum, double Weight) progressAggregate))
                        {
                            progressAggregate = (0.0, 0.0, 0.0, 0.0, 0.0);
                        }
                        progressAggregate.WeightedOverallSum += revitProgress * sourceWeight;
                        progressAggregate.WeightedReinforcementSum += sourceReinforcement * sourceWeight;
                        progressAggregate.WeightedFormworkSum += sourceFormwork * sourceWeight;
                        progressAggregate.WeightedVolumeSum += sourceVolume * sourceWeight;
                        progressAggregate.Weight += sourceWeight;
                        progressByTarget[target] = progressAggregate;
                        double aggregatedOverallProgress = progressAggregate.Weight > 1e-9
                            ? progressAggregate.WeightedOverallSum / progressAggregate.Weight
                            : revitProgress;
                        double aggregatedReinforcementProgress = progressAggregate.Weight > 1e-9
                            ? progressAggregate.WeightedReinforcementSum / progressAggregate.Weight
                            : sourceReinforcement;
                        double aggregatedFormworkProgress = progressAggregate.Weight > 1e-9
                            ? progressAggregate.WeightedFormworkSum / progressAggregate.Weight
                            : sourceFormwork;
                        double aggregatedVolumeProgress = progressAggregate.Weight > 1e-9
                            ? progressAggregate.WeightedVolumeSum / progressAggregate.Weight
                            : sourceVolume;
                        double targetProgress = ResolvePmTaskStageProgressPercent(
                            ResolvePmTaskProgressStage(target),
                            aggregatedReinforcementProgress,
                            aggregatedFormworkProgress,
                            aggregatedVolumeProgress,
                            aggregatedOverallProgress);

                        if (updatePercentComplete)
                        {
                            target["percent_complete"] = targetProgress;
                            target["status"] = ResolvePmProgressStatusFromPercent(targetProgress);
                        }
                    }

                    if (sourceHasBoqPayload)
                    {
                        if (!boqByTarget.TryGetValue(target, out (double ReinforcementBoq, double FormworkBoq, double VolumeBoq) boqAggregate))
                        {
                            boqAggregate = (0.0, 0.0, 0.0);
                        }

                        boqAggregate.ReinforcementBoq += sourceReinforcementBoq;
                        boqAggregate.FormworkBoq += sourceFormworkBoq;
                        boqAggregate.VolumeBoq += sourceVolumeBoq;
                        boqByTarget[target] = boqAggregate;

                        ResolvePmTaskBoqValue(
                            target,
                            boqAggregate.ReinforcementBoq,
                            boqAggregate.FormworkBoq,
                            boqAggregate.VolumeBoq,
                            out string targetUnit,
                            out double targetBoq);
                        if (table.Columns.Contains("unit"))
                        {
                            target["unit"] = targetUnit;
                        }
                        if (table.Columns.Contains("boq"))
                        {
                            target["boq"] = targetBoq;
                        }
                    }

                    TrySetPmUpdateProgressRowStringIfEmpty(target, "revit_structure_element", sourceStructure);
                    TrySetPmUpdateProgressRowStringIfEmpty(target, "revit_building_level", sourceLevel);
                    TrySetPmUpdateProgressRowStringIfEmpty(target, "revit_type", sourceType);
                    if (table.Columns.Contains("revit_element_ids"))
                    {
                        string existingElementIds = Convert.ToString(target["revit_element_ids"], CultureInfo.InvariantCulture) ?? "";
                        target["revit_element_ids"] = MergePmUpdateProgressElementIds(existingElementIds, source.ElementIds);
                    }

                    if (touchedRows.Add(target))
                    {
                        mappedRows++;
                    }
                }
            }

            return mappedRows;
        }

        private static void ResolveSiteProgressStagePercents(
            SiteProgressSummaryRow row,
            out double reinforcement,
            out double formwork,
            out double volume)
        {
            reinforcement = ClampPmPercent(row?.SiteProgressReinforcementPercent ?? 0.0);
            formwork = ClampPmPercent(row?.SiteProgressFormworkPercent ?? 0.0);
            volume = ClampPmPercent(row?.SiteProgressVolumePercent ?? 0.0);

            if (reinforcement <= 1e-9 && formwork <= 1e-9 && volume <= 1e-9)
            {
                double fallback = ResolveSiteProgressRowPercent(row);
                reinforcement = fallback;
                formwork = fallback;
                volume = fallback;
            }
        }

        private static double ResolveSiteProgressRowPercent(SiteProgressSummaryRow row)
        {
            if (row == null)
            {
                return 0.0;
            }

            double percent = Math.Max(0.0, Math.Min(100.0, row.ProgressPercent));
            if (percent > 0.0 || string.Equals((row.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase))
            {
                if (percent <= 0.0 && string.Equals((row.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return 100.0;
                }

                return percent;
            }

            var values = new List<double>
            {
                Math.Max(0.0, Math.Min(100.0, row.SiteProgressReinforcementPercent)),
                Math.Max(0.0, Math.Min(100.0, row.SiteProgressFormworkPercent)),
                Math.Max(0.0, Math.Min(100.0, row.SiteProgressVolumePercent))
            };
            values = values.Where(v => v > 1e-9).ToList();
            if (values.Count == 0)
            {
                if (string.Equals((row.Status ?? "").Trim(), "Not Started", StringComparison.OrdinalIgnoreCase))
                {
                    return 0.0;
                }

                return 0.0;
            }

            return Math.Max(0.0, Math.Min(100.0, values.Average()));
        }

        private List<PmDashboardCurvePoint> BuildPmDashboardCurvePointsFromSiteProgress(List<SiteProgressSummaryRow> rows)
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

            var groups = (rows ?? new List<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .Select(r =>
                {
                    GetPmDashboardSiteProgressMetricPair(r, metricType, out double planned, out double actual);
                    return new
                    {
                        Label = GetPmDashboardSiteProgressGroupLabel(r, viewMode),
                        Planned = planned,
                        Actual = actual
                    };
                })
                .Where(v => !string.IsNullOrWhiteSpace(v.Label))
                .GroupBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Label = g.Key,
                    Planned = g.Sum(v => Math.Max(0.0, v.Planned)),
                    Actual = g.Sum(v => Math.Max(0.0, v.Actual))
                })
                .ToList();

            if (string.Equals(viewMode, "Monthly", StringComparison.OrdinalIgnoreCase))
            {
                groups = groups
                    .OrderBy(g => GetPmDashboardBuildingLevelOrder(g.Label))
                    .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else if (string.Equals(viewMode, "Week", StringComparison.OrdinalIgnoreCase))
            {
                groups = groups
                    .OrderBy(g => GetSiteProgressStructureOrder(g.Label))
                    .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                groups = groups
                    .OrderByDescending(g => g.Planned)
                    .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .ToList();
            }

            double totalPlanned = groups.Sum(g => g.Planned);
            double totalActual = groups.Sum(g => Math.Min(g.Planned, g.Actual));
            double remainingTotal = Math.Max(0.0, totalPlanned - totalActual);

            var points = new List<PmDashboardCurvePoint>();
            double cumulativePlanned = 0.0;
            double cumulativeActual = 0.0;
            int count = Math.Max(1, groups.Count);

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                double periodPlanned = group.Planned;
                double periodActual = Math.Max(0.0, Math.Min(group.Planned, group.Actual));

                cumulativePlanned += periodPlanned;
                cumulativeActual += periodActual;

                double ratio = (i + 1.0) / count;
                double forecast = cumulativeActual + (remainingTotal * ratio);
                if (forecast < cumulativeActual) forecast = cumulativeActual;
                if (forecast > totalPlanned) forecast = totalPlanned;

                points.Add(new PmDashboardCurvePoint
                {
                    Label = group.Label,
                    PlannedCumulative = cumulativePlanned,
                    ActualCumulative = cumulativeActual,
                    ActualCostCumulative = cumulativeActual,
                    ForecastCumulative = forecast,
                    PeriodPlanned = periodPlanned,
                    PeriodActual = periodActual,
                    PeriodActualCost = periodActual
                });
            }

            if (percentMode && totalPlanned > 1e-9)
            {
                foreach (PmDashboardCurvePoint point in points)
                {
                    point.PlannedCumulative = (point.PlannedCumulative / totalPlanned) * 100.0;
                    point.ActualCumulative = (point.ActualCumulative / totalPlanned) * 100.0;
                    point.ActualCostCumulative = (point.ActualCostCumulative / totalPlanned) * 100.0;
                    point.ForecastCumulative = (point.ForecastCumulative / totalPlanned) * 100.0;
                    point.PeriodPlanned = (point.PeriodPlanned / totalPlanned) * 100.0;
                    point.PeriodActual = (point.PeriodActual / totalPlanned) * 100.0;
                    point.PeriodActualCost = (point.PeriodActualCost / totalPlanned) * 100.0;
                }
            }

            return points;
        }

        private static string GetPmDashboardSiteProgressGroupLabel(SiteProgressSummaryRow row, string viewMode)
        {
            if (row == null)
            {
                return "Unassigned";
            }

            string label;
            if (string.Equals(viewMode, "Week", StringComparison.OrdinalIgnoreCase))
            {
                label = row.StructureElement;
            }
            else if (string.Equals(viewMode, "Daily", StringComparison.OrdinalIgnoreCase))
            {
                label = row.TypeName;
            }
            else
            {
                label = row.BuildingLevel;
            }

            return string.IsNullOrWhiteSpace(label) ? "Unassigned" : label.Trim();
        }

        private static void GetPmDashboardSiteProgressMetricPair(SiteProgressSummaryRow row, string metricType, out double planned, out double actual)
        {
            planned = 0.0;
            actual = 0.0;
            if (row == null)
            {
                return;
            }

            if (string.Equals(metricType, "Duration", StringComparison.OrdinalIgnoreCase))
            {
                planned = Math.Max(0.0, row.ElementCount);
                actual = planned * Math.Max(0.0, Math.Min(100.0, row.ProgressPercent)) / 100.0;
                return;
            }

            if (string.Equals(metricType, "Resource", StringComparison.OrdinalIgnoreCase))
            {
                planned = Math.Max(0.0, row.ReinforcementKg);
                actual = Math.Max(0.0, Math.Min(planned, row.ReinforcementCompletedKg));
                if (planned <= 1e-9)
                {
                    planned = Math.Max(0.0, row.FormworkM2);
                    actual = Math.Max(0.0, Math.Min(planned, row.FormworkCompletedM2));
                }

                return;
            }

            planned = Math.Max(0.0, row.TotalBoq);
            actual = Math.Max(0.0, Math.Min(planned, row.CompletedBoq));
        }

        internal void UpdateSiteProgressSummary(
            List<SiteProgressSummaryRow> combined,
            List<SiteProgressSummaryRow> byBuilding,
            string message,
            List<SiteProgressElementDetailRow> elementRows = null)
        {
            void update()
            {
                _siteProgressCombinedSourceRows = (combined ?? new List<SiteProgressSummaryRow>())
                    .Select(CloneSiteProgressRowShallow)
                    .ToList();
                _siteProgressElementRows = (elementRows ?? new List<SiteProgressElementDetailRow>())
                    .Select(CloneSiteProgressElementRowShallow)
                    .ToList();
                ApplySiteProgressPivot();

                _siteProgressByBuildingRows.Clear();
                foreach (SiteProgressSummaryRow row in byBuilding ?? new List<SiteProgressSummaryRow>())
                {
                    _siteProgressByBuildingRows.Add(row);
                }
                _siteProgressLastRefreshLocal = DateTime.Now;
                UpdateProgressDashboardStructureRows();

                if (SiteProgressSummaryText != null)
                {
                    SiteProgressSummaryText.Text = message ?? "";
                }

                PopulateSiteProgressBuildingFilterOptions();
                UpdateProgressDashboardCharts();
                UpdateSiteProgressTabTotals();
                RefreshPmDashboardSummaryAndChart();
            }

            if (Dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                Dispatcher.Invoke(update);
            }

            ShowStatus(message);

            if (_powerBiRefreshPipelinePending)
            {
                _powerBiRefreshPipelinePending = false;
                ExecutePowerBiRefreshPipelineFromCurrentRows();
            }

            if (_powerBiSummaryAllElementsPending)
            {
                _powerBiSummaryAllElementsPending = false;
                string summary = BuildPowerBiAllElementsSummaryText();
                SetPowerBiExportSummary(summary);
                ShowStatus("Power BI: All Elements summary updated.");
            }

            UpdatePowerBiRuntimeInfo();
        }

        private void ApplySiteProgressFiltersToRequest()
        {
            string structure = (SiteProgressStructureFilterCombo?.SelectedItem as string) ?? (SiteProgressStructureFilterCombo?.Text ?? "");
            string building = (SiteProgressBuildingLevelFilterCombo?.SelectedItem as string) ?? (SiteProgressBuildingLevelFilterCombo?.Text ?? "");

            if (string.IsNullOrWhiteSpace(structure)) structure = SiteProgressFilterAll;
            if (string.IsNullOrWhiteSpace(building)) building = SiteProgressFilterAll;

            _handler.Request.SiteProgress.StructureFilter = structure.Trim();
            _handler.Request.SiteProgress.BuildingLevelFilter = building.Trim();
        }

        private void PopulateSiteProgressBuildingFilterOptions()
        {
            if (SiteProgressBuildingLevelFilterCombo == null) return;

            string selected = SiteProgressBuildingLevelFilterCombo.SelectedItem as string;
            var options = new List<string> { SiteProgressFilterAll };
            options.AddRange(_siteProgressCombinedSourceRows
                .Select(r => r.BuildingLevel)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            SiteProgressBuildingLevelFilterCombo.ItemsSource = options;
            if (!string.IsNullOrWhiteSpace(selected) &&
                options.Any(o => string.Equals(o, selected, StringComparison.OrdinalIgnoreCase)))
            {
                SiteProgressBuildingLevelFilterCombo.SelectedItem =
                    options.First(o => string.Equals(o, selected, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SiteProgressBuildingLevelFilterCombo.SelectedIndex = 0;
            }
        }

        private void ApplySiteProgressPivot()
        {
            List<SiteProgressSummaryRow> source = _siteProgressCombinedSourceRows ?? new List<SiteProgressSummaryRow>();
            SiteProgressPivotMode mode = GetSiteProgressPivotMode();
            SiteProgressPivotMetric metric = GetSiteProgressPivotMetric();

            List<SiteProgressSummaryRow> rows = BuildSiteProgressPivotRows(source, mode, metric);

            _siteProgressCombinedRows.Clear();
            foreach (SiteProgressSummaryRow row in rows)
            {
                _siteProgressCombinedRows.Add(row);
            }

            UpdateSiteProgressTabTotals();
            RefreshPmDetailProgressFilterOptions();
            ApplyPmDetailProgressFilters();
        }

        private SiteProgressPivotMode GetSiteProgressPivotMode()
        {
            string mode = SiteProgressPivotModeCombo?.SelectedItem as string;
            if (string.Equals(mode, "Pivot by BuildingLevel", StringComparison.OrdinalIgnoreCase)) return SiteProgressPivotMode.ByBuildingLevel;
            if (string.Equals(mode, "Pivot by Structure Element", StringComparison.OrdinalIgnoreCase)) return SiteProgressPivotMode.ByStructureElement;
            return SiteProgressPivotMode.ByFloorAndElement;
        }

        private SiteProgressPivotMetric GetSiteProgressPivotMetric()
        {
            string mode = SiteProgressPivotMetricCombo?.SelectedItem as string;
            if (string.Equals(mode, "Formwork(%)", StringComparison.OrdinalIgnoreCase)) return SiteProgressPivotMetric.Formwork;
            if (string.Equals(mode, "Reinforcement(%)", StringComparison.OrdinalIgnoreCase)) return SiteProgressPivotMetric.Reinforcement;
            return SiteProgressPivotMetric.Volume;
        }

        private SiteProgressDashboardValueMode GetSiteProgressDashboardValueMode()
        {
            string mode = SiteProgressDashboardValueModeCombo?.SelectedItem as string;
            if (string.Equals(mode, "Progress (%)", StringComparison.OrdinalIgnoreCase))
            {
                return SiteProgressDashboardValueMode.ProgressPercent;
            }

            return SiteProgressDashboardValueMode.AbsoluteBoq;
        }

        private static List<SiteProgressSummaryRow> BuildSiteProgressPivotRows(
            List<SiteProgressSummaryRow> rows,
            SiteProgressPivotMode pivotMode,
            SiteProgressPivotMetric metric)
        {
            rows = rows ?? new List<SiteProgressSummaryRow>();
            if (rows.Count == 0) return rows;

            if (pivotMode == SiteProgressPivotMode.ByFloorAndElement)
            {
                return rows
                    .Select(CloneSiteProgressRowShallow)
                    .OrderBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => GetSiteProgressStructureOrder(r.StructureElement))
                    .ThenBy(r => r.StructureElement, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            IEnumerable<IGrouping<string, SiteProgressSummaryRow>> groups = pivotMode == SiteProgressPivotMode.ByBuildingLevel
                ? rows.GroupBy(r => r.BuildingLevel ?? "", StringComparer.OrdinalIgnoreCase)
                : rows.GroupBy(r => r.StructureElement ?? "", StringComparer.OrdinalIgnoreCase);

            var result = new List<SiteProgressSummaryRow>();
            foreach (IGrouping<string, SiteProgressSummaryRow> g in groups)
            {
                List<SiteProgressSummaryRow> items = g.ToList();
                if (items.Count == 0) continue;

                double reinfBoq = items.Sum(x => x.ReinforcementKg);
                double formBoq = items.Sum(x => x.FormworkM2);
                double volBoq = items.Sum(x => x.VolumeM3);
                double reinfCompleted = items.Sum(x => x.ReinforcementCompletedKg);
                double formCompleted = items.Sum(x => x.FormworkCompletedM2);
                double volCompleted = items.Sum(x => x.VolumeCompletedM3);

                double pReinf = reinfBoq > 1e-9 ? (reinfCompleted * 100.0 / reinfBoq) : 0.0;
                double pForm = formBoq > 1e-9 ? (formCompleted * 100.0 / formBoq) : 0.0;
                double pVol = volBoq > 1e-9 ? (volCompleted * 100.0 / volBoq) : 0.0;
                double progress = ResolveSiteProgressMetricPercent(metric, pReinf, pForm, pVol);
                string level = pivotMode == SiteProgressPivotMode.ByBuildingLevel ? g.Key : "(All Levels)";
                string element = pivotMode == SiteProgressPivotMode.ByStructureElement ? g.Key : "(All Elements)";
                if (string.IsNullOrWhiteSpace(level)) level = "Unassigned";
                if (string.IsNullOrWhiteSpace(element)) element = "Unassigned";

                result.Add(new SiteProgressSummaryRow
                {
                    GroupName = g.Key,
                    BuildingLevel = level,
                    StructureElement = element,
                    TypeName = "(All Types)",
                    ElementCount = items.Sum(x => x.ElementCount),
                    ReinforcementKg = reinfBoq,
                    FormworkM2 = formBoq,
                    VolumeM3 = volBoq,
                    SiteProgressReinforcementPercent = pReinf,
                    SiteProgressFormworkPercent = pForm,
                    SiteProgressVolumePercent = pVol,
                    ReinforcementCompletedKg = reinfCompleted,
                    FormworkCompletedM2 = formCompleted,
                    VolumeCompletedM3 = volCompleted,
                    ProgressPercent = progress,
                    Status = ResolveSiteProgressStatus(progress),
                    TotalBoq = volBoq,
                    CompletedBoq = volCompleted,
                    RemainingBoq = Math.Max(0.0, volBoq - volCompleted),
                    ElementIds = items
                        .SelectMany(x => x.ElementIds ?? new List<int>())
                        .Where(id => id > 0)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList()
                });
            }

            if (pivotMode == SiteProgressPivotMode.ByBuildingLevel)
            {
                return result
                    .OrderBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => GetSiteProgressStructureOrder(r.StructureElement))
                    .ToList();
            }

            return result
                .OrderBy(r => GetSiteProgressStructureOrder(r.StructureElement))
                .ThenBy(r => r.StructureElement, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double ResolveSiteProgressMetricPercent(SiteProgressPivotMetric metric, double pReinf, double pForm, double pVol)
        {
            if (metric == SiteProgressPivotMetric.Reinforcement) return Math.Max(0.0, Math.Min(100.0, pReinf));
            if (metric == SiteProgressPivotMetric.Formwork) return Math.Max(0.0, Math.Min(100.0, pForm));
            return Math.Max(0.0, Math.Min(100.0, pVol));
        }

        private static string ResolveSiteProgressStatus(double progress)
        {
            if (progress >= 99.999) return "Completed";
            if (progress <= 0.001) return "Not Started";
            return "In Progress";
        }

        private static int GetSiteProgressStructureOrder(string structure)
        {
            if (string.IsNullOrWhiteSpace(structure)) return 999;
            string s = structure.Trim();
            if (s.Equals("Structural Foundation", StringComparison.OrdinalIgnoreCase)) return 1;
            if (s.Equals("Structural Column", StringComparison.OrdinalIgnoreCase)) return 2;
            if (s.Equals("Wall", StringComparison.OrdinalIgnoreCase)) return 3;
            if (s.Equals("Structural Framing", StringComparison.OrdinalIgnoreCase)) return 4;
            if (s.Equals("Floor", StringComparison.OrdinalIgnoreCase)) return 5;
            if (s.Equals("Stair", StringComparison.OrdinalIgnoreCase)) return 6;
            return 999;
        }

        private static SiteProgressSummaryRow CloneSiteProgressRowShallow(SiteProgressSummaryRow row)
        {
            if (row == null) return new SiteProgressSummaryRow();
            return new SiteProgressSummaryRow
            {
                GroupName = row.GroupName,
                StructureElement = row.StructureElement,
                BuildingLevel = row.BuildingLevel,
                TypeName = row.TypeName,
                ElementTypeName = row.ElementTypeName,
                ElementCount = row.ElementCount,
                ReinforcementKg = row.ReinforcementKg,
                FormworkM2 = row.FormworkM2,
                VolumeM3 = row.VolumeM3,
                SiteProgressReinforcementPercent = row.SiteProgressReinforcementPercent,
                SiteProgressFormworkPercent = row.SiteProgressFormworkPercent,
                SiteProgressVolumePercent = row.SiteProgressVolumePercent,
                ReinforcementCompletedKg = row.ReinforcementCompletedKg,
                FormworkCompletedM2 = row.FormworkCompletedM2,
                VolumeCompletedM3 = row.VolumeCompletedM3,
                Status = row.Status,
                ProgressPercent = row.ProgressPercent,
                TotalBoq = row.TotalBoq,
                CompletedBoq = row.CompletedBoq,
                RemainingBoq = row.RemainingBoq,
                ElementIds = (row.ElementIds ?? new List<int>()).ToList()
            };
        }

        private static SiteProgressElementDetailRow CloneSiteProgressElementRowShallow(SiteProgressElementDetailRow row)
        {
            if (row == null) return new SiteProgressElementDetailRow();
            return new SiteProgressElementDetailRow
            {
                ElementId = row.ElementId,
                UniqueId = row.UniqueId,
                StructuralPlan = row.StructuralPlan,
                BuildingLevel = row.BuildingLevel,
                StructureElement = row.StructureElement,
                Category = row.Category,
                FamilyName = row.FamilyName,
                TypeName = row.TypeName,
                ReinforcementKg = row.ReinforcementKg,
                FormworkM2 = row.FormworkM2,
                VolumeM3 = row.VolumeM3,
                SiteProgressReinforcementPercent = row.SiteProgressReinforcementPercent,
                SiteProgressFormworkPercent = row.SiteProgressFormworkPercent,
                SiteProgressVolumePercent = row.SiteProgressVolumePercent,
                ReinforcementCompletedKg = row.ReinforcementCompletedKg,
                FormworkCompletedM2 = row.FormworkCompletedM2,
                VolumeCompletedM3 = row.VolumeCompletedM3,
                ProgressPercent = row.ProgressPercent,
                Status = row.Status
            };
        }

        private void UpdateSiteProgressTabTotals()
        {
            if (SiteProgressCombinedTotalsText != null)
            {
                SiteProgressCombinedTotalsText.Text = BuildSiteProgressTotalsText(_siteProgressCombinedRows);
            }
            if (SiteProgressByBuildingTotalsText != null)
            {
                SiteProgressByBuildingTotalsText.Text = BuildSiteProgressTotalsText(_siteProgressByBuildingRows);
            }
            if (SiteProgressDashboardTotalsText != null)
            {
                SiteProgressDashboardTotalsText.Text = BuildSiteProgressDashboardTotalsText(_siteProgressDashboardStructureRows);
            }

            AutoFitSiteProgressGridHeaders();
        }

        private void AutoFitSiteProgressGridHeaders()
        {
            AutoFitDataGridHeaderWidths(SiteProgressCombinedGrid);
            AutoFitDataGridHeaderWidths(SiteProgressByBuildingGrid);
        }

        private static string BuildSiteProgressTotalsText(IEnumerable<SiteProgressSummaryRow> rows)
        {
            List<SiteProgressSummaryRow> items = (rows ?? new List<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .ToList();

            int rowCount = items.Count;
            int elementCount = items.Sum(r => Math.Max(0, r.ElementCount));

            double reinfBoq = items.Sum(r => Math.Max(0.0, r.ReinforcementKg));
            double formBoq = items.Sum(r => Math.Max(0.0, r.FormworkM2));
            double volBoq = items.Sum(r => Math.Max(0.0, r.VolumeM3));

            double reinfCompleted = items.Sum(r => Math.Max(0.0, Math.Min(Math.Max(0.0, r.ReinforcementKg), r.ReinforcementCompletedKg)));
            double formCompleted = items.Sum(r => Math.Max(0.0, Math.Min(Math.Max(0.0, r.FormworkM2), r.FormworkCompletedM2)));
            double volCompleted = items.Sum(r => Math.Max(0.0, Math.Min(Math.Max(0.0, r.VolumeM3), r.VolumeCompletedM3)));

            double reinfProgress = reinfBoq > 1e-9 ? (reinfCompleted * 100.0 / reinfBoq) : 0.0;
            double formProgress = formBoq > 1e-9 ? (formCompleted * 100.0 / formBoq) : 0.0;
            double volProgress = volBoq > 1e-9 ? (volCompleted * 100.0 / volBoq) : 0.0;

            return "Sum: Rows=" + rowCount.ToString(CultureInfo.InvariantCulture) +
                   " | Elements=" + elementCount.ToString(CultureInfo.InvariantCulture) +
                   " | Reinforcement: " + reinfCompleted.ToString("0.###", CultureInfo.InvariantCulture) + "/" + reinfBoq.ToString("0.###", CultureInfo.InvariantCulture) + " Kg (" + reinfProgress.ToString("0.##", CultureInfo.InvariantCulture) + "%)" +
                   " | Formwork: " + formCompleted.ToString("0.###", CultureInfo.InvariantCulture) + "/" + formBoq.ToString("0.###", CultureInfo.InvariantCulture) + " m2 (" + formProgress.ToString("0.##", CultureInfo.InvariantCulture) + "%)" +
                   " | Volume: " + volCompleted.ToString("0.###", CultureInfo.InvariantCulture) + "/" + volBoq.ToString("0.###", CultureInfo.InvariantCulture) + " m3 (" + volProgress.ToString("0.##", CultureInfo.InvariantCulture) + "%)";
        }

        private static string BuildSiteProgressDashboardTotalsText(IEnumerable<SiteProgressSummaryRow> rows)
        {
            List<SiteProgressSummaryRow> items = (rows ?? new List<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .ToList();

            int rowCount = items.Count;
            int elementCount = items.Sum(r => Math.Max(0, r.ElementCount));
            double totalBoq = items.Sum(r => Math.Max(0.0, r.TotalBoq));
            double completedBoq = items.Sum(r => Math.Max(0.0, Math.Min(Math.Max(0.0, r.TotalBoq), r.CompletedBoq)));
            double remainingBoq = Math.Max(0.0, totalBoq - completedBoq);
            double progress = totalBoq > 1e-9 ? (completedBoq * 100.0 / totalBoq) : 0.0;
            progress = Math.Max(0.0, Math.Min(100.0, progress));

            return "Sum: Rows=" + rowCount.ToString(CultureInfo.InvariantCulture) +
                   " | Elements=" + elementCount.ToString(CultureInfo.InvariantCulture) +
                   " | Total BOQ=" + totalBoq.ToString("0.###", CultureInfo.InvariantCulture) +
                   " | Completed BOQ=" + completedBoq.ToString("0.###", CultureInfo.InvariantCulture) +
                   " | Remaining BOQ=" + remainingBoq.ToString("0.###", CultureInfo.InvariantCulture) +
                   " | Progress=" + progress.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        private void RenderSiteProgressDonutChart(
            double completedBoq,
            double remainingBoq,
            OxyColor completedColor,
            OxyColor remainingColor)
        {
            if (SiteProgressDashboardDonutPlotView == null)
            {
                return;
            }

            double safeCompleted = Math.Max(0.0, completedBoq);
            double safeRemaining = Math.Max(0.0, remainingBoq);
            if (safeCompleted <= 1e-9 && safeRemaining <= 1e-9)
            {
                safeRemaining = 1.0;
            }

            var model = new PlotModel
            {
                Background = OxyColor.Parse("#F8FAFD"),
                PlotAreaBorderColor = OxyColors.Undefined,
                PlotAreaBorderThickness = new OxyThickness(0),
                Padding = new OxyThickness(0),
                PlotMargins = new OxyThickness(0),
                TextColor = OxyColor.Parse("#3C4B61"),
                IsLegendVisible = false
            };

            var donutSeries = new PieSeries
            {
                StartAngle = 270,
                AngleSpan = 360,
                InnerDiameter = 0.62,
                Stroke = OxyColor.Parse("#FFFFFF"),
                StrokeThickness = 1.0,
                OutsideLabelFormat = "",
                InsideLabelFormat = "",
                TickHorizontalLength = 0,
                TickRadialLength = 0
            };

            donutSeries.Slices.Add(new PieSlice("Complete", safeCompleted)
            {
                Fill = completedColor
            });
            donutSeries.Slices.Add(new PieSlice("Remaining", safeRemaining)
            {
                Fill = remainingColor
            });

            model.Series.Add(donutSeries);
            SiteProgressDashboardDonutPlotView.Model = model;
        }

        private void RenderSiteProgressPlotChart(List<SiteProgressSummaryRow> structureRows, SiteProgressDashboardValueMode valueMode)
        {
            if (SiteProgressDashboardPlotView == null)
            {
                return;
            }

            bool percentMode = valueMode == SiteProgressDashboardValueMode.ProgressPercent;

            var points = new List<SiteProgressDashboardPlotPoint>();
            foreach (SiteProgressSummaryRow row in structureRows ?? new List<SiteProgressSummaryRow>())
            {
                double rawTotal = Math.Max(0.0, row.TotalBoq);
                double rawCompleted = Math.Max(0.0, Math.Min(rawTotal, row.CompletedBoq));
                double rawRemaining = Math.Max(0.0, rawTotal - rawCompleted);
                double progress = rawTotal > 1e-9 ? (rawCompleted * 100.0 / rawTotal) : 0.0;
                progress = Math.Max(0.0, Math.Min(100.0, progress));

                bool hasBoqBase = rawTotal > 1e-9;
                double total = percentMode ? (hasBoqBase ? 100.0 : 0.0) : rawTotal;
                double completed = percentMode ? (hasBoqBase ? progress : 0.0) : rawCompleted;
                double remaining = percentMode
                    ? (hasBoqBase ? Math.Max(0.0, 100.0 - completed) : 0.0)
                    : rawRemaining;

                points.Add(new SiteProgressDashboardPlotPoint
                {
                    Label = string.IsNullOrWhiteSpace(row.StructureElement) ? "Unassigned" : row.StructureElement,
                    TotalBoq = total,
                    CompletedBoq = completed,
                    RemainingBoq = remaining,
                    RawTotalBoq = rawTotal,
                    RawCompletedBoq = rawCompleted,
                    RawRemainingBoq = rawRemaining,
                    ProgressPercent = progress
                });
            }

            const double canvasHeight = 420.0;
            const double itemStep = 120.0;
            int itemCount = Math.Max(1, points.Count);
            double canvasWidth = Math.Max(900.0, 140.0 + (itemCount * itemStep));

            SiteProgressDashboardPlotView.Width = canvasWidth;
            SiteProgressDashboardPlotView.Height = canvasHeight;
            if (SiteProgressDashboardChartZoomHost != null)
            {
                SiteProgressDashboardChartZoomHost.Width = canvasWidth;
                SiteProgressDashboardChartZoomHost.MinHeight = canvasHeight;
            }

            bool hasData = points.Count > 0;
            if (SiteProgressDashboardChartScrollViewer != null)
            {
                SiteProgressDashboardChartScrollViewer.HorizontalScrollBarVisibility = hasData
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
                SiteProgressDashboardChartScrollViewer.VerticalScrollBarVisibility = hasData
                    ? ScrollBarVisibility.Auto
                    : ScrollBarVisibility.Disabled;
            }

            double maxSeriesValue = points.Count == 0
                ? 1.0
                : points.Max(p => Math.Max(p.TotalBoq, Math.Max(p.CompletedBoq, p.RemainingBoq)));
            double axisMax = percentMode ? 100.0 : GetDashboardNiceUpperBound(maxSeriesValue);
            if (!(axisMax > 0.0))
            {
                axisMax = 1.0;
            }

            var model = new PlotModel
            {
                PlotAreaBorderColor = OxyColor.Parse("#D8E0EA"),
                PlotAreaBorderThickness = new OxyThickness(1),
                Background = OxyColor.Parse("#F8FAFD"),
                TextColor = OxyColor.Parse("#3C4B61"),
                PlotMargins = new OxyThickness(56, 14, 18, 58),
                IsLegendVisible = false
            };

            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = -0.5,
                Maximum = Math.Max(0.5, points.Count - 0.5),
                MajorStep = 1.0,
                MinorStep = 1.0,
                TicklineColor = OxyColor.Parse("#B6C3D4"),
                AxislineColor = OxyColor.Parse("#77879A"),
                TextColor = OxyColor.Parse("#3C4B61"),
                FontSize = 10,
                IsPanEnabled = false,
                IsZoomEnabled = false,
                LabelFormatter = x =>
                {
                    int idx = (int)Math.Round(x);
                    if (idx < 0 || idx >= points.Count)
                    {
                        return "";
                    }

                    if (Math.Abs(x - idx) > 0.25)
                    {
                        return "";
                    }

                    return ShortenDashboardStructureLabel(points[idx].Label);
                }
            };
            model.Axes.Add(xAxis);

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0.0,
                Maximum = axisMax,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.Parse("#D8E0EA"),
                MinorGridlineStyle = LineStyle.None,
                TicklineColor = OxyColor.Parse("#B6C3D4"),
                AxislineColor = OxyColor.Parse("#77879A"),
                TextColor = OxyColor.Parse("#4B5A73"),
                FontSize = 10,
                IsPanEnabled = false,
                IsZoomEnabled = false,
                LabelFormatter = v => percentMode
                    ? v.ToString("0", CultureInfo.InvariantCulture) + "%"
                    : FormatDashboardChartValue(v)
            };
            model.Axes.Add(valueAxis);

            if (!hasData)
            {
                if (SiteProgressDashboardZoomXSlider != null && Math.Abs(SiteProgressDashboardZoomXSlider.Value - 1.0) > 1e-6)
                {
                    SiteProgressDashboardZoomXSlider.Value = 1.0;
                }
                if (SiteProgressDashboardZoomYSlider != null && Math.Abs(SiteProgressDashboardZoomYSlider.Value - 1.0) > 1e-6)
                {
                    SiteProgressDashboardZoomYSlider.Value = 1.0;
                }
                ApplySiteProgressDashboardChartZoom();

                model.Annotations.Add(new TextAnnotation
                {
                    Text = "No progress data available.",
                    TextPosition = new DataPoint(Math.Max(0.0, (points.Count - 1) * 0.5), axisMax * 0.5),
                    TextColor = OxyColor.Parse("#6E7C90"),
                    FontSize = 13,
                    Stroke = OxyColors.Undefined,
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
                });

                SiteProgressDashboardPlotView.Model = model;
                return;
            }

            var totalSeries = new RectangleBarSeries
            {
                Title = "Total BOQ",
                FillColor = OxyColor.Parse("#2F75B5"),
                StrokeColor = OxyColor.Parse("#255D91"),
                StrokeThickness = 0.6,
                TrackerFormatString = "{0}" + Environment.NewLine + "X: {2:0.##}" + Environment.NewLine + "Y: {4:0.###}"
            };
            var completedSeries = new RectangleBarSeries
            {
                Title = "Completed BOQ",
                FillColor = OxyColor.Parse("#5B9BD5"),
                StrokeColor = OxyColor.Parse("#3D79AF"),
                StrokeThickness = 0.6,
                TrackerFormatString = "{0}" + Environment.NewLine + "X: {2:0.##}" + Environment.NewLine + "Y: {4:0.###}"
            };
            var remainingSeries = new RectangleBarSeries
            {
                Title = "Remaining BOQ",
                FillColor = OxyColor.Parse("#C0504D"),
                StrokeColor = OxyColor.Parse("#8E3A38"),
                StrokeThickness = 0.6,
                TrackerFormatString = "{0}" + Environment.NewLine + "X: {2:0.##}" + Environment.NewLine + "Y: {4:0.###}"
            };

            for (int i = 0; i < points.Count; i++)
            {
                SiteProgressDashboardPlotPoint point = points[i];
                const double groupWidth = 0.72;
                double barWidth = groupWidth / 3.0;
                double groupStart = i - (groupWidth * 0.5);

                double totalX0 = groupStart;
                double totalX1 = totalX0 + barWidth;
                double completedX0 = totalX1;
                double completedX1 = completedX0 + barWidth;
                double remainingX0 = completedX1;
                double remainingX1 = remainingX0 + barWidth;

                OxyColor totalColor = GetDashboardTotalBarColor(point.ProgressPercent);
                OxyColor completedColor = GetDashboardCompletedBarColor(point.ProgressPercent);
                OxyColor remainingColor = GetDashboardRemainingBarColor(point.ProgressPercent);

                totalSeries.Items.Add(new RectangleBarItem(totalX0, 0.0, totalX1, point.TotalBoq)
                {
                    Color = totalColor
                });
                completedSeries.Items.Add(new RectangleBarItem(completedX0, 0.0, completedX1, point.CompletedBoq)
                {
                    Color = completedColor
                });
                remainingSeries.Items.Add(new RectangleBarItem(remainingX0, 0.0, remainingX1, point.RemainingBoq)
                {
                    Color = remainingColor
                });

                bool hasAnySeriesValue = (point.TotalBoq > 1e-9) || (point.CompletedBoq > 1e-9) || (point.RemainingBoq > 1e-9);
                if (hasAnySeriesValue)
                {
                    string totalLabel = BuildDashboardTotalBarLabel(point, percentMode);
                    string completedLabel = BuildDashboardSeriesBarLabel("C", point.CompletedBoq, percentMode);
                    string remainingLabel = BuildDashboardSeriesBarLabel("R", point.RemainingBoq, percentMode);

                    AddDashboardBarValueLabel(
                        model,
                        totalX0,
                        totalX1,
                        point.TotalBoq,
                        axisMax,
                        totalLabel,
                        OxyColor.Parse("#1F3552"),
                        percentMode,
                        0,
                        true);
                    AddDashboardBarValueLabel(
                        model,
                        completedX0,
                        completedX1,
                        point.CompletedBoq,
                        axisMax,
                        completedLabel,
                        OxyColor.Parse("#2A5D8F"),
                        percentMode,
                        1,
                        true);
                    AddDashboardBarValueLabel(
                        model,
                        remainingX0,
                        remainingX1,
                        point.RemainingBoq,
                        axisMax,
                        remainingLabel,
                        OxyColor.Parse("#8B3A36"),
                        percentMode,
                        2,
                        true);
                }
            }

            model.Series.Add(totalSeries);
            model.Series.Add(completedSeries);
            model.Series.Add(remainingSeries);
            SiteProgressDashboardPlotView.Model = model;
        }

        private static void ReadSiteProgressFromExcel(
            string path,
            out List<SiteProgressSummaryRow> combined,
            out List<SiteProgressSummaryRow> byBuilding,
            out int importedSheets)
        {
            combined = new List<SiteProgressSummaryRow>();
            byBuilding = new List<SiteProgressSummaryRow>();
            importedSheets = 0;

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Microsoft Excel is not available.");
                appObj = Activator.CreateInstance(excelType);
                dynamic app = appObj;
                app.DisplayAlerts = false;
                app.Visible = false;

                workbooksObj = app.Workbooks;
                dynamic workbooks = workbooksObj;
                workbookObj = workbooks.Open(path, Type.Missing, true);
                dynamic workbook = workbookObj;
                object worksheetsObj = null;
                try
                {
                    worksheetsObj = workbook.Worksheets;
                    dynamic worksheets = worksheetsObj;
                    int sheetCount = Convert.ToInt32(worksheets.Count, CultureInfo.InvariantCulture);

                    for (int wsIndex = 1; wsIndex <= sheetCount; wsIndex++)
                    {
                        object worksheetObj = null;
                        try
                        {
                            worksheetObj = worksheets[wsIndex];
                            dynamic worksheet = worksheetObj;
                            string sheetName = (Convert.ToString(worksheet.Name, CultureInfo.InvariantCulture) ?? "").Trim();
                            List<SiteProgressSummaryRow> rows = ReadSiteProgressRowsFromWorksheet(worksheet);
                            if (rows.Count == 0)
                            {
                                continue;
                            }

                            string key = sheetName.Replace(" ", "").ToLowerInvariant();
                            if (key.Contains("overall") || key.Contains("building"))
                            {
                                byBuilding = rows;
                            }
                            else if (key.Contains("elementfloor") || key.Contains("elementandfloor") || key.Contains("element_floor"))
                            {
                                combined = rows;
                            }
                            else if (key.Contains("element") || key.Contains("floor"))
                            {
                                if (combined.Count == 0) combined = rows;
                            }
                            else if (combined.Count == 0)
                            {
                            }
                            else if (byBuilding.Count == 0)
                            {
                                byBuilding = rows;
                            }

                            importedSheets++;
                        }
                        finally
                        {
                            SafeReleaseCom(worksheetObj);
                        }
                    }
                }
                finally
                {
                    SafeReleaseCom(worksheetsObj);
                }

                workbook.Close(false);
                app.Quit();
            }
            finally
            {
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private static List<SiteProgressSummaryRow> ReadSiteProgressRowsFromWorksheet(dynamic worksheet)
        {
            var rows = new List<SiteProgressSummaryRow>();
            if (worksheet == null) return rows;

            object usedRangeObj = null;
            try
            {
                usedRangeObj = worksheet.UsedRange;
                dynamic usedRange = usedRangeObj;
                object valuesObj = usedRange.Value2;
                object[,] values = NormalizeExcelRangeToMatrix(valuesObj);
                if (values == null)
                {
                    return rows;
                }

                int rMin = values.GetLowerBound(0);
                int rMax = values.GetUpperBound(0);
                int cMin = values.GetLowerBound(1);
                int cMax = values.GetUpperBound(1);

                int headerRow = -1;
                int scanMax = Math.Min(rMax, rMin + 30);
                for (int r = rMin; r <= scanMax; r++)
                {
                    var headers = new List<string>();
                    for (int c = cMin; c <= cMax; c++)
                    {
                        headers.Add(ToCellString(values[r, c]).Trim());
                    }

                    int idxProgressHeader = IndexOfHeader(headers, "Volume (%)", "Progress (%)", "Progress");
                    int idxGroupHeader = IndexOfHeader(headers, "Structure Element", "Element Structure", "BuildingLevel", "Building Level", "Building", "Group", "Name");
                    if (idxProgressHeader >= 0 && idxGroupHeader >= 0)
                    {
                        headerRow = r;
                        break;
                    }
                }

                if (headerRow < rMin)
                {
                    return rows;
                }

                var headerNames = new List<string>();
                for (int c = cMin; c <= cMax; c++)
                {
                    headerNames.Add(ToCellString(values[headerRow, c]).Trim());
                }

                int idxLevel = IndexOfHeader(headerNames, "BuildingLevel", "Building Level", "Floor", "Level");
                int idxStructure = IndexOfHeader(headerNames, "Structure Element", "Element Structure");
                int idxType = IndexOfHeader(headerNames, "Type");
                int idxGroup = IndexOfHeader(headerNames, "Building", "Group", "Name");
                int idxElements = IndexOfHeader(headerNames, "Element qty", "Elements", "Element Count", "Count");

                int idxReinfBoq = IndexOfHeader(headerNames, "Reinforcement(Kg)", "Reinforcement (Kg)");
                int idxFormBoq = IndexOfHeader(headerNames, "Formwork(m2)", "Formwork (m2)", "Formwork");
                int idxVolBoq = IndexOfHeader(headerNames, "Volume(m3)", "Volume (m3)", "Volume");
                int idxReinfPct = IndexOfHeader(headerNames, "Reinforcement(%)", "Reinforcement (%)");
                int idxFormPct = IndexOfHeader(headerNames, "Formwork(%)", "Formwork (%)");
                int idxVolPct = IndexOfHeader(headerNames, "Volume(%)", "Volume (%)", "Progress (%)");
                int idxReinfComp = IndexOfHeader(headerNames, "Reinforcement Completed(Kg)", "Reinforcement Completed");
                int idxFormComp = IndexOfHeader(headerNames, "Formwork Completed(m2)", "Formwork Completed");
                int idxVolComp = IndexOfHeader(headerNames, "Volume Completed(m3)", "Volume Completed", "Completed BOQ");
                int idxStatus = IndexOfHeader(headerNames, "Status");

                for (int r = headerRow + 1; r <= rMax; r++)
                {
                    string level = (idxLevel >= 0 && idxLevel + cMin <= cMax) ? ToCellString(values[r, cMin + idxLevel]).Trim() : "";
                    string structure = (idxStructure >= 0 && idxStructure + cMin <= cMax) ? ToCellString(values[r, cMin + idxStructure]).Trim() : "";
                    string type = (idxType >= 0 && idxType + cMin <= cMax) ? ToCellString(values[r, cMin + idxType]).Trim() : "";
                    string group = (idxGroup >= 0 && idxGroup + cMin <= cMax) ? ToCellString(values[r, cMin + idxGroup]).Trim() : "";
                    if (string.IsNullOrWhiteSpace(level) && string.IsNullOrWhiteSpace(structure) && string.IsNullOrWhiteSpace(group))
                    {
                        continue;
                    }

                    int elementCount = idxElements >= 0 && idxElements + cMin <= cMax ? ToCellInt(values[r, cMin + idxElements]) : 0;
                    double reinfBoq = idxReinfBoq >= 0 && idxReinfBoq + cMin <= cMax ? ToCellDouble(values[r, cMin + idxReinfBoq]) : 0.0;
                    double formBoq = idxFormBoq >= 0 && idxFormBoq + cMin <= cMax ? ToCellDouble(values[r, cMin + idxFormBoq]) : 0.0;
                    double volBoq = idxVolBoq >= 0 && idxVolBoq + cMin <= cMax ? ToCellDouble(values[r, cMin + idxVolBoq]) : 0.0;
                    double pReinf = idxReinfPct >= 0 && idxReinfPct + cMin <= cMax ? ToCellDouble(values[r, cMin + idxReinfPct]) : 0.0;
                    double pForm = idxFormPct >= 0 && idxFormPct + cMin <= cMax ? ToCellDouble(values[r, cMin + idxFormPct]) : 0.0;
                    double pVol = idxVolPct >= 0 && idxVolPct + cMin <= cMax ? ToCellDouble(values[r, cMin + idxVolPct]) : 0.0;
                    double reinfComp = idxReinfComp >= 0 && idxReinfComp + cMin <= cMax ? ToCellDouble(values[r, cMin + idxReinfComp]) : 0.0;
                    double formComp = idxFormComp >= 0 && idxFormComp + cMin <= cMax ? ToCellDouble(values[r, cMin + idxFormComp]) : 0.0;
                    double volComp = idxVolComp >= 0 && idxVolComp + cMin <= cMax ? ToCellDouble(values[r, cMin + idxVolComp]) : 0.0;
                    if (pReinf <= 1.0 && pReinf > 0.0) pReinf *= 100.0;
                    if (pForm <= 1.0 && pForm > 0.0) pForm *= 100.0;
                    if (pVol <= 1.0 && pVol > 0.0) pVol *= 100.0;
                    if (reinfBoq > 1e-9 && reinfComp <= 1e-9 && pReinf > 0.0) reinfComp = reinfBoq * pReinf / 100.0;
                    if (formBoq > 1e-9 && formComp <= 1e-9 && pForm > 0.0) formComp = formBoq * pForm / 100.0;
                    if (volBoq > 1e-9 && volComp <= 1e-9 && pVol > 0.0) volComp = volBoq * pVol / 100.0;
                    if (reinfBoq > 1e-9 && pReinf <= 0.0 && reinfComp > 0.0) pReinf = reinfComp * 100.0 / reinfBoq;
                    if (formBoq > 1e-9 && pForm <= 0.0 && formComp > 0.0) pForm = formComp * 100.0 / formBoq;
                    if (volBoq > 1e-9 && pVol <= 0.0 && volComp > 0.0) pVol = volComp * 100.0 / volBoq;
                    string status = idxStatus >= 0 && idxStatus + cMin <= cMax ? ToCellString(values[r, cMin + idxStatus]).Trim() : "";
                    double progress = pVol > 0.0 ? pVol : (pForm > 0.0 ? pForm : pReinf);
                    if (string.IsNullOrWhiteSpace(status))
                    {
                        status = progress <= 0.0 ? "Not Started" : (progress >= 99.99 ? "Completed" : "In Progress");
                    }

                    rows.Add(new SiteProgressSummaryRow
                    {
                        GroupName = group,
                        StructureElement = structure,
                        BuildingLevel = level,
                        TypeName = type,
                        ElementCount = elementCount,
                        ReinforcementKg = reinfBoq,
                        FormworkM2 = formBoq,
                        VolumeM3 = volBoq,
                        SiteProgressReinforcementPercent = pReinf,
                        SiteProgressFormworkPercent = pForm,
                        SiteProgressVolumePercent = pVol,
                        ReinforcementCompletedKg = reinfComp,
                        FormworkCompletedM2 = formComp,
                        VolumeCompletedM3 = volComp,
                        Status = status,
                        ProgressPercent = progress,
                        TotalBoq = volBoq,
                        CompletedBoq = volComp
                    });
                }
            }
            finally
            {
                SafeReleaseCom(usedRangeObj);
            }

            return rows;
        }

        private bool TryBuildSiteProgressClipboardPayload(out string tsv, out int rowCount)
        {
            tsv = "";
            rowCount = 0;

            List<SiteProgressSummaryRow> combined = _siteProgressCombinedRows.ToList();
            List<SiteProgressSummaryRow> byBuilding = _siteProgressByBuildingRows.ToList();
            if (combined.Count == 0 && byBuilding.Count == 0)
            {
                return false;
            }

            var sb = new StringBuilder();
            sb.AppendLine("By Element + Floor");
            sb.AppendLine("BuildingLevel\tStructure Element\tType\tElement qty\tReinforcement(Kg)\tFormwork(m2)\tVolume(m3)\tReinforcement(%)\tFormwork(%)\tVolume(%)\tReinforcement Completed(Kg)\tFormwork Completed(m2)\tVolume Completed(m3)\tStatus");
            foreach (SiteProgressSummaryRow row in combined)
            {
                sb.Append(SanitizeForTab(row.BuildingLevel)).Append('\t')
                  .Append(SanitizeForTab(row.StructureElement)).Append('\t')
                  .Append(SanitizeForTab(row.TypeName)).Append('\t')
                  .Append(row.ElementCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.ReinforcementKg.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.FormworkM2.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.VolumeM3.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.SiteProgressReinforcementPercent.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.SiteProgressFormworkPercent.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.SiteProgressVolumePercent.ToString("0.##", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.ReinforcementCompletedKg.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.FormworkCompletedM2.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.VolumeCompletedM3.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(SanitizeForTab(row.Status))
                  .AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Overall (By Building)");
            sb.AppendLine("Building\tElements\tTotal BOQ\tCompleted BOQ\tProgress (%)");
            foreach (SiteProgressSummaryRow row in byBuilding)
            {
                sb.Append(SanitizeForTab(row.GroupName)).Append('\t')
                  .Append(row.ElementCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.TotalBoq.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.CompletedBoq.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.ProgressPercent.ToString("0.##", CultureInfo.InvariantCulture))
                  .AppendLine();
            }

            rowCount = combined.Count + byBuilding.Count;
            tsv = sb.ToString();
            return true;
        }

        private void ExportSiteProgressRowsToExcel(
            List<SiteProgressSummaryRow> combined,
            List<SiteProgressSummaryRow> byBuilding,
            List<SiteProgressElementDetailRow> elementRows,
            string filePath)
        {
            combined = combined ?? new List<SiteProgressSummaryRow>();
            byBuilding = byBuilding ?? new List<SiteProgressSummaryRow>();
            elementRows = elementRows ?? new List<SiteProgressElementDetailRow>();

            var byBuildingFloor = elementRows
                .GroupBy(r => new
                {
                    Building = string.IsNullOrWhiteSpace(r.StructuralPlan) ? "Unassigned" : r.StructuralPlan,
                    Floor = string.IsNullOrWhiteSpace(r.BuildingLevel) ? "Unassigned" : r.BuildingLevel
                })
                .Select(g =>
                {
                    double total = g.Sum(x => x.VolumeM3);
                    double completed = g.Sum(x => x.VolumeCompletedM3);
                    if (completed < 0.0) completed = 0.0;
                    if (completed > total && total > 0.0) completed = total;
                    double remaining = Math.Max(0.0, total - completed);
                    double progress = total > 1e-9 ? (completed * 100.0 / total) : 0.0;
                    progress = Math.Max(0.0, Math.Min(100.0, progress));
                    string status = progress <= 0.0 ? "Not Started" : (progress >= 99.99 ? "Completed" : "In Progress");
                    return new
                    {
                        g.Key.Building,
                        g.Key.Floor,
                        ElementCount = g.Count(),
                        TotalBoq = total,
                        CompletedBoq = completed,
                        RemainingBoq = remaining,
                        ProgressPercent = progress,
                        Status = status
                    };
                })
                .OrderBy(r => r.Building, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Floor, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SiteProgressSummaryRow overallRow = byBuilding.FirstOrDefault(r =>
                string.Equals(r.GroupName, "Entire Building", StringComparison.OrdinalIgnoreCase));

            double totalBoq;
            double completedBoq;
            int totalElements;
            if (elementRows.Count > 0)
            {
                totalBoq = elementRows.Sum(r => r.VolumeM3);
                completedBoq = elementRows.Sum(r => r.VolumeCompletedM3);
                totalElements = elementRows.Count;
            }
            else if (overallRow != null)
            {
                totalBoq = overallRow.TotalBoq;
                completedBoq = overallRow.CompletedBoq;
                totalElements = overallRow.ElementCount;
            }
            else
            {
                totalBoq = combined.Sum(r => r.VolumeM3);
                completedBoq = combined.Sum(r => r.VolumeCompletedM3);
                totalElements = combined.Sum(r => r.ElementCount);
            }

            if (completedBoq < 0.0) completedBoq = 0.0;
            if (completedBoq > totalBoq && totalBoq > 0.0) completedBoq = totalBoq;
            double remainingBoq = Math.Max(0.0, totalBoq - completedBoq);
            double overallProgress = totalBoq > 1e-9 ? (completedBoq * 100.0 / totalBoq) : 0.0;
            overallProgress = Math.Max(0.0, Math.Min(100.0, overallProgress));

            int statusNotStarted = elementRows.Count(r => string.Equals(r.Status, "Not Started", StringComparison.OrdinalIgnoreCase));
            int statusInProgress = elementRows.Count(r => string.Equals(r.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            int statusCompleted = elementRows.Count(r => string.Equals(r.Status, "Completed", StringComparison.OrdinalIgnoreCase));

            string scopeLabel = GetSiteProgressScope().ToString();
            string structureFilter = (SiteProgressStructureFilterCombo?.SelectedItem as string) ?? SiteProgressFilterAll;
            string buildingFilter = (SiteProgressBuildingLevelFilterCombo?.SelectedItem as string) ?? SiteProgressFilterAll;

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            object summarySheetObj = null;
            object byFloorSheetObj = null;
            object combinedSheetObj = null;
            object detailSheetObj = null;
            object summaryRangeObj = null;
            object byFloorRangeObj = null;
            object combinedRangeObj = null;
            object detailRangeObj = null;
            object summaryHeaderRangeObj = null;
            object byFloorHeaderRangeObj = null;
            object combinedHeaderRangeObj = null;
            object detailHeaderRangeObj = null;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Microsoft Excel is not available.");
                appObj = Activator.CreateInstance(excelType);
                dynamic app = appObj;
                app.DisplayAlerts = false;
                app.Visible = false;

                workbooksObj = app.Workbooks;
                dynamic workbooks = workbooksObj;
                workbookObj = workbooks.Add();
                dynamic workbook = workbookObj;

                summarySheetObj = workbook.Worksheets[1];
                dynamic summarySheet = summarySheetObj;
                summarySheet.Name = "ManagementSummary";

                while (workbook.Worksheets.Count < 4)
                {
                    workbook.Worksheets.Add(After: workbook.Worksheets[workbook.Worksheets.Count]);
                }

                byFloorSheetObj = workbook.Worksheets[2];
                dynamic byFloorSheet = byFloorSheetObj;
                byFloorSheet.Name = "ByBuildingFloor";

                combinedSheetObj = workbook.Worksheets[3];
                dynamic combinedSheet = combinedSheetObj;
                combinedSheet.Name = "ByElementFloor";

                detailSheetObj = workbook.Worksheets[4];
                dynamic detailSheet = detailSheetObj;
                detailSheet.Name = "ElementDetail";

                const int summaryColCount = 4;
                var summaryMatrix = new object[20, summaryColCount];
                summaryMatrix[0, 0] = "Management Progress Report";
                summaryMatrix[1, 0] = "Generated";
                summaryMatrix[1, 1] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                summaryMatrix[2, 0] = "Scope";
                summaryMatrix[2, 1] = scopeLabel;
                summaryMatrix[3, 0] = "Structure Filter";
                summaryMatrix[3, 1] = structureFilter;
                summaryMatrix[4, 0] = "BuildingLevel Filter";
                summaryMatrix[4, 1] = buildingFilter;
                summaryMatrix[6, 0] = "KPI";
                summaryMatrix[6, 1] = "Value";
                summaryMatrix[7, 0] = "Elements";
                summaryMatrix[7, 1] = totalElements;
                summaryMatrix[8, 0] = "Total BOQ (Volume m3)";
                summaryMatrix[8, 1] = Math.Round(totalBoq, 3);
                summaryMatrix[9, 0] = "Completed BOQ (Volume m3)";
                summaryMatrix[9, 1] = Math.Round(completedBoq, 3);
                summaryMatrix[10, 0] = "Remaining BOQ (Volume m3)";
                summaryMatrix[10, 1] = Math.Round(remainingBoq, 3);
                summaryMatrix[11, 0] = "Progress (%)";
                summaryMatrix[11, 1] = Math.Round(overallProgress, 2);
                summaryMatrix[13, 0] = "Status";
                summaryMatrix[13, 1] = "Element Count";
                summaryMatrix[14, 0] = "Not Started";
                summaryMatrix[14, 1] = statusNotStarted;
                summaryMatrix[15, 0] = "In Progress";
                summaryMatrix[15, 1] = statusInProgress;
                summaryMatrix[16, 0] = "Completed";
                summaryMatrix[16, 1] = statusCompleted;
                summaryMatrix[18, 0] = "Notes";
                summaryMatrix[18, 1] = "Based on current Site Progress filters and %Site_Progress values.";

                int byFloorDataCount = byBuildingFloor.Count;
                int byFloorRowCount = Math.Max(1, byFloorDataCount) + 1;
                const int byFloorColCount = 8;
                var byFloorMatrix = new object[byFloorRowCount, byFloorColCount];
                byFloorMatrix[0, 0] = "Building";
                byFloorMatrix[0, 1] = "Floor";
                byFloorMatrix[0, 2] = "Elements";
                byFloorMatrix[0, 3] = "Total BOQ";
                byFloorMatrix[0, 4] = "Completed BOQ";
                byFloorMatrix[0, 5] = "Remaining BOQ";
                byFloorMatrix[0, 6] = "Progress (%)";
                byFloorMatrix[0, 7] = "Status";
                for (int i = 0; i < byFloorDataCount; i++)
                {
                    var row = byBuildingFloor[i];
                    byFloorMatrix[i + 1, 0] = row.Building;
                    byFloorMatrix[i + 1, 1] = row.Floor;
                    byFloorMatrix[i + 1, 2] = row.ElementCount;
                    byFloorMatrix[i + 1, 3] = Math.Round(row.TotalBoq, 3);
                    byFloorMatrix[i + 1, 4] = Math.Round(row.CompletedBoq, 3);
                    byFloorMatrix[i + 1, 5] = Math.Round(row.RemainingBoq, 3);
                    byFloorMatrix[i + 1, 6] = Math.Round(row.ProgressPercent, 2);
                    byFloorMatrix[i + 1, 7] = row.Status;
                }

                int combinedDataCount = combined.Count;
                int combinedRowCount = Math.Max(1, combinedDataCount) + 2;
                const int combinedColCount = 14;
                var combinedMatrix = new object[combinedRowCount, combinedColCount];
                combinedMatrix[0, 4] = "Boq";
                combinedMatrix[0, 7] = "Site Progress";
                combinedMatrix[0, 10] = "Boq Completed";
                combinedMatrix[1, 0] = "BuildingLevel";
                combinedMatrix[1, 1] = "Structure Element";
                combinedMatrix[1, 2] = "Type";
                combinedMatrix[1, 3] = "Element qty";
                combinedMatrix[1, 4] = "Reinforcement(Kg)";
                combinedMatrix[1, 5] = "Formwork(m2)";
                combinedMatrix[1, 6] = "Volume(m3)";
                combinedMatrix[1, 7] = "Reinforcement(%)";
                combinedMatrix[1, 8] = "Formwork(%)";
                combinedMatrix[1, 9] = "Volume(%)";
                combinedMatrix[1, 10] = "Reinforcement(Kg)";
                combinedMatrix[1, 11] = "Formwork(m2)";
                combinedMatrix[1, 12] = "Volume(m3)";
                combinedMatrix[1, 13] = "Status";
                for (int i = 0; i < combinedDataCount; i++)
                {
                    SiteProgressSummaryRow row = combined[i];
                    combinedMatrix[i + 2, 0] = row.BuildingLevel ?? "";
                    combinedMatrix[i + 2, 1] = row.StructureElement ?? "";
                    combinedMatrix[i + 2, 2] = row.TypeName ?? "";
                    combinedMatrix[i + 2, 3] = row.ElementCount;
                    combinedMatrix[i + 2, 4] = Math.Round(row.ReinforcementKg, 3);
                    combinedMatrix[i + 2, 5] = Math.Round(row.FormworkM2, 3);
                    combinedMatrix[i + 2, 6] = Math.Round(row.VolumeM3, 3);
                    combinedMatrix[i + 2, 7] = Math.Round(row.SiteProgressReinforcementPercent, 2);
                    combinedMatrix[i + 2, 8] = Math.Round(row.SiteProgressFormworkPercent, 2);
                    combinedMatrix[i + 2, 9] = Math.Round(row.SiteProgressVolumePercent, 2);
                    combinedMatrix[i + 2, 10] = Math.Round(row.ReinforcementCompletedKg, 3);
                    combinedMatrix[i + 2, 11] = Math.Round(row.FormworkCompletedM2, 3);
                    combinedMatrix[i + 2, 12] = Math.Round(row.VolumeCompletedM3, 3);
                    combinedMatrix[i + 2, 13] = row.Status ?? "";
                }

                int detailDataCount = elementRows.Count;
                int detailRowCount = Math.Max(1, detailDataCount) + 1;
                const int detailColCount = 19;
                var detailMatrix = new object[detailRowCount, detailColCount];
                detailMatrix[0, 0] = "ElementId";
                detailMatrix[0, 1] = "UniqueId";
                detailMatrix[0, 2] = "Building";
                detailMatrix[0, 3] = "Floor";
                detailMatrix[0, 4] = "Structure Element";
                detailMatrix[0, 5] = "Category";
                detailMatrix[0, 6] = "Family";
                detailMatrix[0, 7] = "Type";
                detailMatrix[0, 8] = "Reinforcement(Kg)";
                detailMatrix[0, 9] = "Formwork(m2)";
                detailMatrix[0, 10] = "Volume(m3)";
                detailMatrix[0, 11] = "Reinforcement(%)";
                detailMatrix[0, 12] = "Formwork(%)";
                detailMatrix[0, 13] = "Volume(%)";
                detailMatrix[0, 14] = "Reinforcement Completed(Kg)";
                detailMatrix[0, 15] = "Formwork Completed(m2)";
                detailMatrix[0, 16] = "Volume Completed(m3)";
                detailMatrix[0, 17] = "Progress (%)";
                detailMatrix[0, 18] = "Status";
                for (int i = 0; i < detailDataCount; i++)
                {
                    SiteProgressElementDetailRow row = elementRows[i];
                    detailMatrix[i + 1, 0] = row.ElementId;
                    detailMatrix[i + 1, 1] = row.UniqueId ?? "";
                    detailMatrix[i + 1, 2] = row.StructuralPlan ?? "";
                    detailMatrix[i + 1, 3] = row.BuildingLevel ?? "";
                    detailMatrix[i + 1, 4] = row.StructureElement ?? "";
                    detailMatrix[i + 1, 5] = row.Category ?? "";
                    detailMatrix[i + 1, 6] = row.FamilyName ?? "";
                    detailMatrix[i + 1, 7] = row.TypeName ?? "";
                    detailMatrix[i + 1, 8] = Math.Round(row.ReinforcementKg, 3);
                    detailMatrix[i + 1, 9] = Math.Round(row.FormworkM2, 3);
                    detailMatrix[i + 1, 10] = Math.Round(row.VolumeM3, 3);
                    detailMatrix[i + 1, 11] = Math.Round(row.SiteProgressReinforcementPercent, 2);
                    detailMatrix[i + 1, 12] = Math.Round(row.SiteProgressFormworkPercent, 2);
                    detailMatrix[i + 1, 13] = Math.Round(row.SiteProgressVolumePercent, 2);
                    detailMatrix[i + 1, 14] = Math.Round(row.ReinforcementCompletedKg, 3);
                    detailMatrix[i + 1, 15] = Math.Round(row.FormworkCompletedM2, 3);
                    detailMatrix[i + 1, 16] = Math.Round(row.VolumeCompletedM3, 3);
                    detailMatrix[i + 1, 17] = Math.Round(row.ProgressPercent, 2);
                    detailMatrix[i + 1, 18] = row.Status ?? "";
                }

                dynamic summaryTopLeft = summarySheet.Cells[1, 1];
                dynamic summaryBottomRight = summarySheet.Cells[20, summaryColCount];
                summaryRangeObj = summarySheet.Range[summaryTopLeft, summaryBottomRight];
                dynamic summaryRange = summaryRangeObj;
                summaryRange.Value2 = summaryMatrix;

                dynamic byFloorTopLeft = byFloorSheet.Cells[1, 1];
                dynamic byFloorBottomRight = byFloorSheet.Cells[byFloorRowCount, byFloorColCount];
                byFloorRangeObj = byFloorSheet.Range[byFloorTopLeft, byFloorBottomRight];
                dynamic byFloorRange = byFloorRangeObj;
                byFloorRange.Value2 = byFloorMatrix;

                dynamic combinedTopLeft = combinedSheet.Cells[1, 1];
                dynamic combinedBottomRight = combinedSheet.Cells[combinedRowCount, combinedColCount];
                combinedRangeObj = combinedSheet.Range[combinedTopLeft, combinedBottomRight];
                dynamic combinedRange = combinedRangeObj;
                combinedRange.Value2 = combinedMatrix;

                dynamic detailTopLeft = detailSheet.Cells[1, 1];
                dynamic detailBottomRight = detailSheet.Cells[detailRowCount, detailColCount];
                detailRangeObj = detailSheet.Range[detailTopLeft, detailBottomRight];
                dynamic detailRange = detailRangeObj;
                detailRange.Value2 = detailMatrix;

                summaryHeaderRangeObj = summarySheet.Range[summarySheet.Cells[1, 1], summarySheet.Cells[1, 2]];
                dynamic summaryHeaderRange = summaryHeaderRangeObj;
                summaryHeaderRange.Font.Bold = true;

                byFloorHeaderRangeObj = byFloorSheet.Range[byFloorSheet.Cells[1, 1], byFloorSheet.Cells[1, byFloorColCount]];
                dynamic byFloorHeader = byFloorHeaderRangeObj;
                byFloorHeader.Font.Bold = true;

                combinedHeaderRangeObj = combinedSheet.Range[combinedSheet.Cells[1, 1], combinedSheet.Cells[2, combinedColCount]];
                dynamic combinedHeader = combinedHeaderRangeObj;
                combinedHeader.Font.Bold = true;

                detailHeaderRangeObj = detailSheet.Range[detailSheet.Cells[1, 1], detailSheet.Cells[1, detailColCount]];
                dynamic detailHeader = detailHeaderRangeObj;
                detailHeader.Font.Bold = true;

                try
                {
                    object boqL = combinedSheet.Cells[1, 5];
                    object boqR = combinedSheet.Cells[1, 7];
                    dynamic boqRange = combinedSheet.Range[boqL, boqR];
                    boqRange.Merge();
                    boqRange.HorizontalAlignment = -4108; // xlCenter
                    SafeReleaseCom(boqRange);
                    SafeReleaseCom(boqR);
                    SafeReleaseCom(boqL);

                    object spL = combinedSheet.Cells[1, 8];
                    object spR = combinedSheet.Cells[1, 10];
                    dynamic spRange = combinedSheet.Range[spL, spR];
                    spRange.Merge();
                    spRange.HorizontalAlignment = -4108;
                    SafeReleaseCom(spRange);
                    SafeReleaseCom(spR);
                    SafeReleaseCom(spL);

                    object bcL = combinedSheet.Cells[1, 11];
                    object bcR = combinedSheet.Cells[1, 13];
                    dynamic bcRange = combinedSheet.Range[bcL, bcR];
                    bcRange.Merge();
                    bcRange.HorizontalAlignment = -4108;
                    SafeReleaseCom(bcRange);
                    SafeReleaseCom(bcR);
                    SafeReleaseCom(bcL);
                }
                catch
                {
                }

                try
                {
                    summarySheet.Columns.AutoFit();
                    byFloorSheet.Columns.AutoFit();
                    combinedSheet.Columns.AutoFit();
                    detailSheet.Columns.AutoFit();
                }
                catch
                {
                    // ignore formatting issues
                }

                workbook.SaveAs(filePath, 51);
                workbook.Close(true);
                app.Quit();
            }
            finally
            {
                SafeReleaseCom(detailHeaderRangeObj);
                SafeReleaseCom(combinedHeaderRangeObj);
                SafeReleaseCom(byFloorHeaderRangeObj);
                SafeReleaseCom(summaryHeaderRangeObj);
                SafeReleaseCom(detailRangeObj);
                SafeReleaseCom(combinedRangeObj);
                SafeReleaseCom(byFloorRangeObj);
                SafeReleaseCom(summaryRangeObj);
                SafeReleaseCom(detailSheetObj);
                SafeReleaseCom(combinedSheetObj);
                SafeReleaseCom(byFloorSheetObj);
                SafeReleaseCom(summarySheetObj);
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private bool IsSiteProgressTab()
        {
            if (MainTabControl?.SelectedItem is System.Windows.Controls.TabItem item)
            {
                return string.Equals(item.Header?.ToString(), "SITE PROGRESS", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        private readonly ObservableCollection<SiteProgressSummaryRow> _siteProgressCombinedRows = new ObservableCollection<SiteProgressSummaryRow>();
        private readonly ObservableCollection<SiteProgressSummaryRow> _siteProgressByBuildingRows = new ObservableCollection<SiteProgressSummaryRow>();
        private readonly ObservableCollection<SiteProgressSummaryRow> _siteProgressDashboardStructureRows = new ObservableCollection<SiteProgressSummaryRow>();
        private readonly ObservableCollection<SiteProgressDashboardChartItem> _siteProgressDashboardChartItems = new ObservableCollection<SiteProgressDashboardChartItem>();
        private List<SiteProgressSummaryRow> _siteProgressCombinedSourceRows = new List<SiteProgressSummaryRow>();
        private List<SiteProgressElementDetailRow> _siteProgressElementRows = new List<SiteProgressElementDetailRow>();
        private const string SiteProgressFilterAll = "All";
        private static readonly List<string> SiteProgressStructureFilterOptions = new List<string>
        {
            SiteProgressFilterAll,
            "Structural Foundation",
            "Structural Column",
            "Wall",
            "Structural Framing",
            "Floor",
            "Stair"
        };
        private const string SiteProgressInternalDragRowsFormat = "CamboBIM.SiteProgressRows";
        private System.Windows.Point _siteProgressDragStartPoint;
        private bool _siteProgressSelectionSyncInProgress;
        private bool _siteProgressDashboardUiUpdateInProgress;
        private DateTime _siteProgressLastRefreshLocal = DateTime.MinValue;
        private string _powerBiLastExportFolder = "";
        private bool _powerBiRefreshPipelinePending;
        private bool _powerBiOpenProjectAfterRefreshPending;
        private bool _powerBiSummaryAllElementsPending;
        private string _powerBiLastSummaryMessage = "";
        private DateTime _powerBiLastActionLocal = DateTime.MinValue;
        private const string DefaultPowerBiReportUrl = "https://app.powerbi.com/";
        private const string DefaultPowerBiProjectFileName = "Power BI Project.pbip";
        private const string DefaultPowerBiProjectRelativePath = @"Power BI\Power BI Project.pbip";
        private const string DefaultPowerBiProjectAbsolutePath =
            @"D:\CamboBIM\20260214_CamboBIM.Revit2024.Addin\CamboBIM.Revit2024.Addin\database\Power BI\Power BI Project.pbip";
        private const string DefaultSiteProgressExcelFileName = "Site_Progress.xlsx";
        private static readonly string[] RemovedPowerBiLegacyCsvFiles =
        {
            "fact_borey_planing_info.csv",
            "fact_borey_project_info.csv",
            "fact_borey_sale_info.csv",
            "fact_borey_site_info.csv",
            "dim_house.csv",
            "dim_date.csv",
            "export_manifest.csv"
        };

        private enum SiteProgressPivotMode
        {
            ByFloorAndElement,
            ByBuildingLevel,
            ByStructureElement
        }

        private enum SiteProgressPivotMetric
        {
            Volume,
            Formwork,
            Reinforcement
        }

        private enum SiteProgressDashboardValueMode
        {
            AbsoluteBoq,
            ProgressPercent
        }

        private sealed class SiteProgressDashboardChartItem
        {
            public string StructureElement { get; set; } = "";
            public double TotalHeight { get; set; }
            public double CompletedHeight { get; set; }
            public double RemainingHeight { get; set; }
            public string TotalLabel { get; set; } = "0";
            public string CompletedLabel { get; set; } = "0";
            public string RemainingLabel { get; set; } = "0";
            public string ProgressLabel { get; set; } = "";
        }

        private sealed class SiteProgressDashboardPlotPoint
        {
            public string Label { get; set; } = "";
            public double TotalBoq { get; set; }
            public double CompletedBoq { get; set; }
            public double RemainingBoq { get; set; }
            public double RawTotalBoq { get; set; }
            public double RawCompletedBoq { get; set; }
            public double RawRemainingBoq { get; set; }
            public double ProgressPercent { get; set; }
        }
    }
}




