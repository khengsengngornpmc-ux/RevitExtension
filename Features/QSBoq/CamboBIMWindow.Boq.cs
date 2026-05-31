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

        private static Dictionary<string, string> CreateDefaultPrepareBoqHeaderMap()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Use"] = "Use",
                ["Sheet"] = "Sheet",
                ["ItemCode"] = "Item Code",
                ["Description"] = "Description",
                ["Unit"] = "Unit",
                ["StructureElement"] = "Structure Element",
                ["BuildingLevel"] = "BuildingLevel",
                ["Type"] = "Type",
                ["Qty"] = "Qty",
                ["Volume"] = "Volume (m3)",
                ["Formwork"] = "Formwork (m2)"
            };
        }

        private void ApplyPrepareBoqColumnHeaders(IReadOnlyDictionary<string, string> map)
        {
            if (PrepareBoqGrid == null)
            {
                return;
            }

            PrepareBoqGrid.Columns.Clear();

            PrepareBoqGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = HeaderFromMap(map, "Use", "Use"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.IsEnabled)),
                Width = 46
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Sheet", "Sheet"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.SourceSheet)),
                Width = 90
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "ItemCode", "Item Code"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.ItemCode)),
                Width = 92
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Description", "Description"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.Description)),
                Width = 220
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Unit", "Unit"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.Unit)),
                Width = 64
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "StructureElement", "Structure Element"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.StructureElement)),
                Width = 130
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "BuildingLevel", "BuildingLevel"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.BuildingLevel)),
                Width = 130
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Type", "Type"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.TypeName)),
                Width = 220
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Qty", "Qty"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.Quantity)),
                Width = 64
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Volume", "Volume (m3)"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.TotalVolumeM3))
                {
                    StringFormat = "N3"
                },
                Width = 110
            });

            PrepareBoqGrid.Columns.Add(new DataGridTextColumn
            {
                Header = HeaderFromMap(map, "Formwork", "Formwork (m2)"),
                Binding = new System.Windows.Data.Binding(nameof(PrepareBoqRow.TotalFormworkAreaM2))
                {
                    StringFormat = "N3"
                },
                Width = 120
            });
        }

        private void OnBoqRefreshClick(object sender, RoutedEventArgs e)
        {
            _handler.Request.Boq.Scope = GetBoqScope();
            SyncQsSoilRulesToRequest();
            _handler.Request.RequestType = CadToModelRequestType.RefreshBoqTable;
            _externalEvent.Raise();
        }

        private void OnBoqFilterChanged(object sender, RoutedEventArgs e)
        {
            ApplyBoqFilter();
            _ = TryAutoSyncBoqExcelLinkAsync();
        }

        private void OnBoqCopyClick(object sender, RoutedEventArgs e)
        {
            if (!TryBuildBoqClipboardPayload(includeHeader: false, out string tsv, out _, out int rowCount, out int colCount))
            {
                ShowStatus("No BOQ data to copy.");
                return;
            }

            try
            {
                Clipboard.SetDataObject(tsv, true);
                ShowStatus($"Copied {rowCount} row(s) x {colCount} column(s) to clipboard for Excel.");
                if (!TryCaptureBoqExcelLink(rowCount, colCount))
                {
                    ShowStatus("Copied. Excel link not captured yet. Click target cell in Excel and drag/drop once.");
                }
            }
            catch (Exception ex)
            {
                ShowStatus("Copy failed: " + ex.Message);
            }
        }

        private async void OnBoqExportCsvClick(object sender, RoutedEventArgs e)
        {
            List<BoqTableRow> rows = GetBoqRowsForTransfer();
            if (rows.Count == 0)
            {
                ShowStatus("No BOQ rows to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export BOQ CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "CBIM-BOQ_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            string csv = BuildBoqCsv(rows, true);
            try
            {
                await Task.Run(() => File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(false)));
                _boqLastCsvExportPath = dialog.FileName;
                ShowStatus($"BOQ CSV exported: {dialog.FileName} ({rows.Count} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus("BOQ export failed: " + ex.Message);
            }
        }

        private void OnBoqExportExcelClick(object sender, RoutedEventArgs e)
        {
            List<BoqTableRow> rows = GetBoqRowsForTransfer();
            if (rows.Count == 0)
            {
                ShowStatus("No BOQ rows to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export BOQ Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                FileName = "CBIM-BOQ_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                ExportBoqRowsToExcel(rows, dialog.FileName);
                ShowStatus($"BOQ Excel exported: {dialog.FileName} ({rows.Count} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus("BOQ Excel export failed: " + ex.Message);
            }
        }

        private void OnPrepareBoqCreateFromBoqClick(object sender, RoutedEventArgs e)
        {
            List<BoqTableRow> rows = GetBoqRowsForTransfer();
            if (rows.Count == 0)
            {
                ShowStatus("No BOQ rows to prepare.");
                return;
            }

            _prepareBoqRows.Clear();
            foreach (BoqTableRow row in rows)
            {
                string key = BuildBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName);
                _prepareBoqRows.Add(new PrepareBoqRow
                {
                    IsEnabled = true,
                    SourceSheet = "(BOQ)",
                    ItemCode = "",
                    Description = row.TypeName ?? "",
                    Unit = "EA",
                    StructureElement = row.StructureElement ?? "",
                    BuildingLevel = row.BuildingLevel ?? "",
                    TypeName = row.TypeName ?? "",
                    Quantity = row.Quantity,
                    TotalVolumeM3 = row.TotalVolumeM3,
                    TotalFormworkAreaM2 = row.TotalFormworkAreaM2,
                    LinkedBoqKey = key
                });
            }

            UpdatePrepareBoqSummary();
            ShowStatus($"Prepare BOQ created from current BOQ: {_prepareBoqRows.Count} row(s).");
        }

        private void OnPrepareBoqSyncFromBoqClick(object sender, RoutedEventArgs e)
        {
            if (_prepareBoqRows.Count == 0)
            {
                ShowStatus("Prepare BOQ is empty.");
                return;
            }

            int updated = SyncPrepareBoqWithCurrentBoq(setMissingToZero: true);
            ShowStatus($"Prepare BOQ synced from BOQ table: {updated} row(s) updated.");
        }

        private void OnPrepareBoqImportCsvClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Prepare BOQ (Excel/CSV)",
                Filter = "Excel files (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
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
                PrepareBoqImportResult import = ReadPrepareBoqFromFile(dialog.FileName);
                _prepareBoqRows.Clear();
                foreach (PrepareBoqRow row in import.Rows)
                {
                    _prepareBoqRows.Add(row);
                }

                _prepareBoqHeaderMap = new Dictionary<string, string>(import.HeaderMap, StringComparer.OrdinalIgnoreCase);
                ApplyPrepareBoqColumnHeaders(_prepareBoqHeaderMap);

                int synced = SyncPrepareBoqWithCurrentBoq(setMissingToZero: false);
                string sheetInfo = import.SheetCount > 0 ? $", sheets: {import.SheetCount}" : "";
                ShowStatus($"Prepare BOQ imported: {import.Rows.Count} row(s){sheetInfo}, synced: {synced} row(s).");
            }
            catch (Exception ex)
            {
                ShowStatus("Prepare BOQ import failed: " + ex.Message);
            }
        }

        private void OnPrepareBoqExportCsvClick(object sender, RoutedEventArgs e)
        {
            List<PrepareBoqRow> rows = _prepareBoqRows.Where(r => r.IsEnabled).ToList();
            if (rows.Count == 0)
            {
                ShowStatus("No Prepare BOQ rows to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Prepare BOQ CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "CBIM-PrepareBOQ_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv",
                AddExtension = true,
                DefaultExt = ".csv"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, BuildPrepareBoqCsv(rows), new UTF8Encoding(false));
                ShowStatus($"Prepare BOQ CSV exported: {dialog.FileName} ({rows.Count} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus("Prepare BOQ export failed: " + ex.Message);
            }
        }

        private void OnPrepareBoqExportExcelClick(object sender, RoutedEventArgs e)
        {
            List<PrepareBoqRow> rows = _prepareBoqRows.Where(r => r.IsEnabled).ToList();
            if (rows.Count == 0)
            {
                ShowStatus("No Prepare BOQ rows to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Prepare BOQ Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                FileName = "CBIM-PrepareBOQ_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                ExportPrepareBoqRowsToExcel(rows, dialog.FileName);
                ShowStatus($"Prepare BOQ Excel exported: {dialog.FileName} ({rows.Count} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus("Prepare BOQ Excel export failed: " + ex.Message);
            }
        }

        private void OnPrepareBoqClearClick(object sender, RoutedEventArgs e)
        {
            _prepareBoqRows.Clear();
            UpdatePrepareBoqSummary();
            ShowStatus("Prepare BOQ cleared.");
        }

        private void OnPrepareBoqGridPreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnPrepareBoqTabPreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                if (PrepareBoqTab != null && !PrepareBoqTab.IsSelected)
                {
                    PrepareBoqTab.IsSelected = true;
                }

                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void OnPrepareBoqTabDrop(object sender, DragEventArgs e)
        {
            if (!TryGetDroppedBoqRows(e, out List<BoqTableRow> droppedRows))
            {
                return;
            }

            int start = GetPrepareBoqSelectedRowIndex();
            if (start < 0)
            {
                start = FindFirstPrepareBoqUnmappedRowIndex(0);
            }
            if (start < 0)
            {
                start = _prepareBoqRows.Count;
            }

            MapBoqRowsIntoPrepareBoq(droppedRows, start);
        }

        private void OnPrepareBoqGridDrop(object sender, DragEventArgs e)
        {
            if (!TryGetDroppedBoqRows(e, out List<BoqTableRow> droppedRows))
            {
                return;
            }

            int start = GetPrepareBoqDropRowIndex(e.GetPosition(PrepareBoqGrid));
            if (start < 0)
            {
                start = FindFirstPrepareBoqUnmappedRowIndex(0);
            }
            if (start < 0)
            {
                start = _prepareBoqRows.Count;
            }

            MapBoqRowsIntoPrepareBoq(droppedRows, start);
        }

        private bool TryGetDroppedBoqRows(DragEventArgs e, out List<BoqTableRow> droppedRows)
        {
            droppedRows = new List<BoqTableRow>();

            try
            {
                if (e.Data.GetDataPresent(BoqInternalDragRowsFormat))
                {
                    object raw = e.Data.GetData(BoqInternalDragRowsFormat);
                    if (raw is IEnumerable<BoqTableRow> rowsFromDrag)
                    {
                        droppedRows = rowsFromDrag
                            .Where(r => r != null)
                            .Select(r => new BoqTableRow
                            {
                                StructureElement = r.StructureElement ?? "",
                                BuildingLevel = r.BuildingLevel ?? "",
                                TypeName = r.TypeName ?? "",
                                Quantity = r.Quantity,
                                TotalVolumeM3 = r.TotalVolumeM3,
                                TotalFormworkAreaM2 = r.TotalFormworkAreaM2
                            })
                            .ToList();

                        if (droppedRows.Count > 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore and fall back to text parsing.
            }

            string text = e.Data.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                text = e.Data.GetData(DataFormats.Text) as string;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowStatus("Drop data is empty.");
                return false;
            }

            droppedRows = ParseBoqRowsFromDroppedText(text);
            if (droppedRows.Count == 0)
            {
                ShowStatus("No BOQ rows detected in dropped data. Drag full BOQ row(s).");
                return false;
            }

            return true;
        }

        private void MapBoqRowsIntoPrepareBoq(IReadOnlyList<BoqTableRow> droppedRows, int startIndex)
        {
            if (droppedRows == null || droppedRows.Count == 0)
            {
                return;
            }

            int start = Math.Max(0, startIndex);
            var existingByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _prepareBoqRows.Count; i++)
            {
                PrepareBoqRow existing = _prepareBoqRows[i];
                string existingKey = existing.LinkedBoqKey;
                if (string.IsNullOrWhiteSpace(existingKey) &&
                    !string.IsNullOrWhiteSpace(existing.StructureElement) &&
                    !string.IsNullOrWhiteSpace(existing.BuildingLevel) &&
                    !string.IsNullOrWhiteSpace(existing.TypeName))
                {
                    existingKey = BuildBoqKey(existing.StructureElement, existing.BuildingLevel, existing.TypeName);
                }

                if (string.IsNullOrWhiteSpace(existingKey))
                {
                    continue;
                }

                if (!existingByKey.ContainsKey(existingKey))
                {
                    existingByKey[existingKey] = i;
                }
            }

            for (int i = 0; i < droppedRows.Count; i++)
            {
                BoqTableRow src = droppedRows[i];
                string srcKey = BuildBoqKey(src.StructureElement, src.BuildingLevel, src.TypeName);
                int target = start + i;
                PrepareBoqRow row;

                bool hasExplicitTarget = target >= 0 && target < _prepareBoqRows.Count;
                if (existingByKey.TryGetValue(srcKey, out int existingIndex) &&
                    !string.IsNullOrWhiteSpace(src.StructureElement) &&
                    !string.IsNullOrWhiteSpace(src.BuildingLevel) &&
                    !string.IsNullOrWhiteSpace(src.TypeName))
                {
                    if (!hasExplicitTarget ||
                        IsPrepareBoqMappingEmpty(_prepareBoqRows[target]) ||
                        target == existingIndex)
                    {
                        target = existingIndex;
                        hasExplicitTarget = true;
                    }
                }

                if (!hasExplicitTarget)
                {
                    int firstUnmapped = FindFirstPrepareBoqUnmappedRowIndex(target);
                    if (firstUnmapped >= 0)
                    {
                        target = firstUnmapped;
                        hasExplicitTarget = true;
                    }
                }

                if (hasExplicitTarget)
                {
                    row = _prepareBoqRows[target];
                }
                else
                {
                    row = new PrepareBoqRow
                    {
                        IsEnabled = true,
                        Unit = "EA"
                    };
                    _prepareBoqRows.Add(row);
                }

                row.StructureElement = src.StructureElement ?? "";
                row.BuildingLevel = src.BuildingLevel ?? "";
                row.TypeName = src.TypeName ?? "";
                if (string.IsNullOrWhiteSpace(row.Description))
                {
                    row.Description = src.TypeName ?? "";
                }
                row.Quantity = src.Quantity;
                row.TotalVolumeM3 = src.TotalVolumeM3;
                row.TotalFormworkAreaM2 = src.TotalFormworkAreaM2;
                row.LinkedBoqKey = srcKey;
                if (string.IsNullOrWhiteSpace(row.SourceSheet))
                {
                    row.SourceSheet = "(BOQ)";
                }

                if (!string.IsNullOrWhiteSpace(row.StructureElement) &&
                    !string.IsNullOrWhiteSpace(row.BuildingLevel) &&
                    !string.IsNullOrWhiteSpace(row.TypeName))
                {
                    string finalKey = BuildBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName);
                    int resolvedIndex = hasExplicitTarget ? target : _prepareBoqRows.Count - 1;
                    existingByKey[finalKey] = resolvedIndex;
                }
            }

            PrepareBoqGrid?.Items.Refresh();
            UpdatePrepareBoqSummary();
            ShowStatus($"Mapped {droppedRows.Count} BOQ row(s) into Prepare BOQ.");
        }

        private int FindFirstPrepareBoqUnmappedRowIndex(int startIndex)
        {
            int start = Math.Max(0, startIndex);
            for (int i = start; i < _prepareBoqRows.Count; i++)
            {
                if (IsPrepareBoqMappingEmpty(_prepareBoqRows[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsPrepareBoqMappingEmpty(PrepareBoqRow row)
        {
            if (row == null) return true;

            bool hasLinkedKey = !string.IsNullOrWhiteSpace(row.LinkedBoqKey);
            bool hasMappingFields =
                !string.IsNullOrWhiteSpace(row.StructureElement) ||
                !string.IsNullOrWhiteSpace(row.BuildingLevel) ||
                !string.IsNullOrWhiteSpace(row.TypeName);

            return !hasLinkedKey && !hasMappingFields;
        }

        private void OnBoqGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (BoqTableGrid == null) return;
            _boqDragStartPoint = e.GetPosition(BoqTableGrid);
        }

        private void OnBoqGridPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (BoqTableGrid == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            System.Windows.Point current = e.GetPosition(BoqTableGrid);
            if (Math.Abs(current.X - _boqDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _boqDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (!TryBuildBoqClipboardPayload(includeHeader: false, out string tsv, out string csv, out int dataRows, out int dataCols))
            {
                return;
            }

            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, tsv);
                data.SetData(DataFormats.Text, tsv);
                data.SetData(DataFormats.CommaSeparatedValue, csv);
                List<BoqTableRow> dragRows = GetBoqRowsForPayload(out bool _, out int _);
                if (dragRows.Count > 0)
                {
                    data.SetData(BoqInternalDragRowsFormat, dragRows);
                }

                TryCaptureBoqExcelLink(dataRows, dataCols);
                DragDrop.DoDragDrop(BoqTableGrid, data, DragDropEffects.Copy);
                _ = CaptureBoqExcelLinkAfterDropAsync(dataRows, dataCols);
            }
            catch (Exception ex)
            {
                ShowStatus("Drag-to-Excel failed: " + ex.Message);
            }
        }

        private async Task CaptureBoqExcelLinkAfterDropAsync(int writeRows, int writeCols)
        {
            for (int i = 0; i < 10; i++)
            {
                if (TryCaptureBoqExcelLink(writeRows, writeCols))
                {
                    return;
                }

                await Task.Delay(120);
            }
        }

        private QsScope GetBoqScope()
        {
            if (BoqScopeSelectionRadio?.IsChecked == true)
            {
                return QsScope.CurrentSelection;
            }
            if (BoqScopeAllRadio?.IsChecked == true)
            {
                return QsScope.EntireModel;
            }

            return QsScope.CurrentView;
        }

        private static DataTable BuildPowerBiQsBoqTable(
            IEnumerable<BoqTableRow> boqRows,
            IEnumerable<SiteProgressSummaryRow> summaryRows,
            IEnumerable<SiteProgressElementDetailRow> elementRows)
        {
            var table = new DataTable("fact_qs_boq");
            table.Columns.Add("building_level", typeof(string));
            table.Columns.Add("structure_element", typeof(string));
            table.Columns.Add("type", typeof(string));
            table.Columns.Add("qty", typeof(int));
            table.Columns.Add("boq_reinforcement_kg", typeof(double));
            table.Columns.Add("boq_formwork_m2", typeof(double));
            table.Columns.Add("boq_volume_m3", typeof(double));
            table.Columns.Add("progress_reinforcement_ratio", typeof(double));
            table.Columns.Add("progress_formwork_ratio", typeof(double));
            table.Columns.Add("progress_volume_ratio", typeof(double));
            table.Columns.Add("completed_qty", typeof(double));
            table.Columns.Add("completed_reinforcement_kg", typeof(double));
            table.Columns.Add("completed_formwork_m2", typeof(double));
            table.Columns.Add("completed_volume_m3", typeof(double));
            table.Columns.Add("status", typeof(string));

            List<BoqTableRow> boqSource = (boqRows ?? Enumerable.Empty<BoqTableRow>())
                .Where(r => r != null)
                .ToList();

            if (boqSource.Count == 0)
            {
                boqSource = (summaryRows ?? Enumerable.Empty<SiteProgressSummaryRow>())
                    .Where(r => r != null)
                    .GroupBy(r => BuildPowerBiBoqKey(r.StructureElement, r.BuildingLevel, r.TypeName), StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        SiteProgressSummaryRow first = g.First();
                        return new BoqTableRow
                        {
                            StructureElement = first.StructureElement ?? "",
                            BuildingLevel = first.BuildingLevel ?? "",
                            TypeName = first.TypeName ?? "",
                            Quantity = g.Sum(x => Math.Max(0, x.ElementCount)),
                            TotalVolumeM3 = g.Sum(x => Math.Max(0.0, x.VolumeM3)),
                            TotalFormworkAreaM2 = g.Sum(x => Math.Max(0.0, x.FormworkM2)),
                            StructureOrder = GetSiteProgressStructureOrder(first.StructureElement)
                        };
                    })
                    .OrderBy(r => r.StructureOrder)
                    .ThenBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var progressByType = new Dictionary<string, PowerBiBoqProgressAggregate>(StringComparer.OrdinalIgnoreCase);
            var progressByLevel = new Dictionary<string, PowerBiBoqProgressAggregate>(StringComparer.OrdinalIgnoreCase);
            var progressByStructure = new Dictionary<string, PowerBiBoqProgressAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (SiteProgressSummaryRow row in summaryRows ?? Enumerable.Empty<SiteProgressSummaryRow>())
            {
                if (row == null)
                {
                    continue;
                }

                double boqRebar = Math.Max(0.0, row.ReinforcementKg);
                double boqForm = Math.Max(0.0, row.FormworkM2);
                double boqVol = Math.Max(0.0, row.VolumeM3);
                double doneRebar = Math.Max(0.0, row.ReinforcementCompletedKg);
                double doneForm = Math.Max(0.0, row.FormworkCompletedM2);
                double doneVol = Math.Max(0.0, row.VolumeCompletedM3);

                AddPowerBiBoqProgress(
                    progressByType,
                    BuildPowerBiBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName),
                    boqRebar,
                    doneRebar,
                    boqForm,
                    doneForm,
                    boqVol,
                    doneVol);
                AddPowerBiBoqProgress(
                    progressByLevel,
                    BuildPowerBiBoqLevelKey(row.StructureElement, row.BuildingLevel),
                    boqRebar,
                    doneRebar,
                    boqForm,
                    doneForm,
                    boqVol,
                    doneVol);
                AddPowerBiBoqProgress(
                    progressByStructure,
                    BuildPowerBiBoqStructureKey(row.StructureElement),
                    boqRebar,
                    doneRebar,
                    boqForm,
                    doneForm,
                    boqVol,
                    doneVol);
            }

            if (progressByType.Count == 0)
            {
                foreach (SiteProgressElementDetailRow row in elementRows ?? Enumerable.Empty<SiteProgressElementDetailRow>())
                {
                    if (row == null)
                    {
                        continue;
                    }

                    double boqRebar = Math.Max(0.0, row.ReinforcementKg);
                    double boqForm = Math.Max(0.0, row.FormworkM2);
                    double boqVol = Math.Max(0.0, row.VolumeM3);
                    double doneRebar = Math.Max(0.0, row.ReinforcementCompletedKg);
                    double doneForm = Math.Max(0.0, row.FormworkCompletedM2);
                    double doneVol = Math.Max(0.0, row.VolumeCompletedM3);

                    AddPowerBiBoqProgress(
                        progressByType,
                        BuildPowerBiBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName),
                        boqRebar,
                        doneRebar,
                        boqForm,
                        doneForm,
                        boqVol,
                        doneVol);
                    AddPowerBiBoqProgress(
                        progressByLevel,
                        BuildPowerBiBoqLevelKey(row.StructureElement, row.BuildingLevel),
                        boqRebar,
                        doneRebar,
                        boqForm,
                        doneForm,
                        boqVol,
                        doneVol);
                    AddPowerBiBoqProgress(
                        progressByStructure,
                        BuildPowerBiBoqStructureKey(row.StructureElement),
                        boqRebar,
                        doneRebar,
                        boqForm,
                        doneForm,
                        boqVol,
                        doneVol);
                }
            }

            foreach (BoqTableRow row in boqSource)
            {
                if (row == null)
                {
                    continue;
                }

                string structure = (row.StructureElement ?? "").Trim();
                string level = (row.BuildingLevel ?? "").Trim();
                string type = (row.TypeName ?? "").Trim();
                int qty = Math.Max(0, row.Quantity);
                double boqRebar = 0.0;
                double boqForm = Math.Max(0.0, row.TotalFormworkAreaM2);
                double boqVol = Math.Max(0.0, row.TotalVolumeM3);

                progressByType.TryGetValue(BuildPowerBiBoqKey(structure, level, type), out PowerBiBoqProgressAggregate progress);
                if (progress == null)
                {
                    progressByLevel.TryGetValue(BuildPowerBiBoqLevelKey(structure, level), out progress);
                }
                if (progress == null)
                {
                    progressByStructure.TryGetValue(BuildPowerBiBoqStructureKey(structure), out progress);
                }

                double pRebar = 0.0;
                double pForm = 0.0;
                double pVol = 0.0;
                if (progress != null)
                {
                    pRebar = ResolvePowerBiProgressRatio(progress.CompletedRebarKg, progress.BoqRebarKg, 0.0);
                    pForm = ResolvePowerBiProgressRatio(progress.CompletedFormworkM2, progress.BoqFormworkM2, 0.0);
                    pVol = ResolvePowerBiProgressRatio(progress.CompletedVolumeM3, progress.BoqVolumeM3, 0.0);
                }

                double completedRebar = boqRebar * pRebar;
                double completedForm = boqForm * pForm;
                double completedVolume = boqVol * pVol;

                var qtyRatios = new List<double>(3);
                if (boqRebar > 1e-9) qtyRatios.Add(pRebar);
                if (boqForm > 1e-9) qtyRatios.Add(pForm);
                if (boqVol > 1e-9) qtyRatios.Add(pVol);
                if (qtyRatios.Count == 0 && progress != null)
                {
                    qtyRatios.Add(pRebar);
                    qtyRatios.Add(pForm);
                    qtyRatios.Add(pVol);
                }

                double qtyRatio = qtyRatios.Count > 0
                    ? NormalizePowerBiProgressRatio(qtyRatios.Average())
                    : 0.0;
                double completedQty = qty * qtyRatio;

                DataRow output = table.NewRow();
                output["building_level"] = level;
                output["structure_element"] = structure;
                output["type"] = type;
                output["qty"] = qty;
                output["boq_reinforcement_kg"] = boqRebar;
                output["boq_formwork_m2"] = boqForm;
                output["boq_volume_m3"] = boqVol;
                output["progress_reinforcement_ratio"] = pRebar;
                output["progress_formwork_ratio"] = pForm;
                output["progress_volume_ratio"] = pVol;
                output["completed_qty"] = completedQty;
                output["completed_reinforcement_kg"] = completedRebar;
                output["completed_formwork_m2"] = completedForm;
                output["completed_volume_m3"] = completedVolume;
                output["status"] = BuildPowerBiSiteProgressStatus(pRebar, pForm, pVol);
                table.Rows.Add(output);
            }

            return table;
        }

        private static void AddPowerBiBoqProgress(
            IDictionary<string, PowerBiBoqProgressAggregate> map,
            string key,
            double boqRebar,
            double completedRebar,
            double boqForm,
            double completedForm,
            double boqVolume,
            double completedVolume)
        {
            if (map == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!map.TryGetValue(key, out PowerBiBoqProgressAggregate item))
            {
                item = new PowerBiBoqProgressAggregate();
                map[key] = item;
            }

            item.BoqRebarKg += Math.Max(0.0, boqRebar);
            item.CompletedRebarKg += Math.Max(0.0, completedRebar);
            item.BoqFormworkM2 += Math.Max(0.0, boqForm);
            item.CompletedFormworkM2 += Math.Max(0.0, completedForm);
            item.BoqVolumeM3 += Math.Max(0.0, boqVolume);
            item.CompletedVolumeM3 += Math.Max(0.0, completedVolume);
        }

        private static string BuildPowerBiBoqKey(string structureElement, string buildingLevel, string typeName)
        {
            return NormalizePowerBiBoqKeyPart(structureElement) + "|" +
                   NormalizePowerBiBoqKeyPart(buildingLevel) + "|" +
                   NormalizePowerBiBoqKeyPart(typeName);
        }

        private static string BuildPowerBiBoqLevelKey(string structureElement, string buildingLevel)
        {
            return NormalizePowerBiBoqKeyPart(structureElement) + "|" +
                   NormalizePowerBiBoqKeyPart(buildingLevel);
        }

        private static string BuildPowerBiBoqStructureKey(string structureElement)
        {
            return NormalizePowerBiBoqKeyPart(structureElement);
        }

        private static string NormalizePowerBiBoqKeyPart(string raw)
        {
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }

            return sb.ToString();
        }

        private int ApplySiteProgressRowsToSpecificPmTaskRowsBoqOnly(
            DataTable table,
            IEnumerable<DataRow> targetRows,
            IEnumerable<SiteProgressSummaryRow> sourceRows)
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

            List<SiteProgressSummaryRow> rows = (sourceRows ?? Enumerable.Empty<SiteProgressSummaryRow>())
                .Where(r => r != null)
                .ToList();
            if (rows.Count == 0)
            {
                return 0;
            }

            double totalReinforcementBoq = rows.Sum(r => Math.Max(0.0, r.ReinforcementKg));
            double totalFormworkBoq = rows.Sum(r => Math.Max(0.0, r.FormworkM2));
            double totalVolumeBoq = rows.Sum(r => Math.Max(0.0, r.VolumeM3));

            int changed = 0;
            foreach (DataRow target in normalizedTargets)
            {
                ResolvePmTaskBoqValue(
                    target,
                    totalReinforcementBoq,
                    totalFormworkBoq,
                    totalVolumeBoq,
                    out string targetUnit,
                    out double targetBoq);
                targetBoq = RoundPmBoqValue(targetBoq);

                bool rowChanged = false;
                if (table.Columns.Contains("unit"))
                {
                    string currentUnit = (Convert.ToString(target["unit"], CultureInfo.InvariantCulture) ?? "").Trim();
                    string nextUnit = (targetUnit ?? "").Trim();
                    if (!string.Equals(currentUnit, nextUnit, StringComparison.Ordinal))
                    {
                        target["unit"] = nextUnit;
                        rowChanged = true;
                    }
                }

                if (table.Columns.Contains("boq"))
                {
                    object currentValue = target["boq"];
                    bool hasCurrentValue = currentValue != null && currentValue != DBNull.Value;
                    double currentBoq = hasCurrentValue ? Math.Max(0.0, ConvertToDoubleSafe(currentValue)) : 0.0;
                    if (!hasCurrentValue || Math.Abs(currentBoq - targetBoq) > 1e-6)
                    {
                        target["boq"] = targetBoq;
                        rowChanged = true;
                    }
                }

                if (rowChanged)
                {
                    changed++;
                }
            }

            return changed;
        }

        private PmTaskProgressStage ResolvePmTaskBoqStage(
            DataRow targetRow,
            double reinforcementBoq,
            double formworkBoq,
            double volumeBoq)
        {
            PmTaskProgressStage stage = ResolvePmTaskProgressStage(targetRow);
            if (stage != PmTaskProgressStage.Overall)
            {
                return stage;
            }

            if (volumeBoq > 1e-9)
            {
                return PmTaskProgressStage.Volume;
            }
            if (formworkBoq > 1e-9)
            {
                return PmTaskProgressStage.Formwork;
            }
            if (reinforcementBoq > 1e-9)
            {
                return PmTaskProgressStage.Reinforcement;
            }

            return PmTaskProgressStage.Volume;
        }

        private void ResolvePmTaskBoqValue(
            DataRow targetRow,
            double reinforcementBoq,
            double formworkBoq,
            double volumeBoq,
            out string unit,
            out double boq)
        {
            PmTaskProgressStage stage = ResolvePmTaskBoqStage(
                targetRow,
                reinforcementBoq,
                formworkBoq,
                volumeBoq);
            ResolvePmTaskStageDefaultUnit(stage, out unit);

            switch (stage)
            {
                case PmTaskProgressStage.Reinforcement:
                    boq = Math.Max(0.0, reinforcementBoq);
                    break;
                case PmTaskProgressStage.Formwork:
                    boq = Math.Max(0.0, formworkBoq);
                    break;
                case PmTaskProgressStage.Volume:
                default:
                    boq = Math.Max(0.0, volumeBoq);
                    break;
            }

            boq = RoundPmBoqValue(boq);
        }

        private static double RoundPmBoqValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }

            return Math.Round(Math.Max(0.0, value), 2, MidpointRounding.AwayFromZero);
        }

        private static BoqTableRow CloneBoqTableRowShallow(BoqTableRow row)
        {
            if (row == null)
            {
                return new BoqTableRow();
            }

            return new BoqTableRow
            {
                StructureElement = row.StructureElement ?? "",
                BuildingLevel = row.BuildingLevel ?? "",
                TypeName = row.TypeName ?? "",
                Quantity = row.Quantity,
                TotalVolumeM3 = row.TotalVolumeM3,
                TotalFormworkAreaM2 = row.TotalFormworkAreaM2,
                StructureOrder = row.StructureOrder
            };
        }

        private static double GetSiteProgressMetricBoq(SiteProgressSummaryRow row, SiteProgressPivotMetric metric)
        {
            if (row == null) return 0.0;
            if (metric == SiteProgressPivotMetric.Reinforcement) return row.ReinforcementKg;
            if (metric == SiteProgressPivotMetric.Formwork) return row.FormworkM2;
            return row.VolumeM3;
        }

        private static double GetSiteProgressMetricCompletedBoq(SiteProgressSummaryRow row, SiteProgressPivotMetric metric)
        {
            if (row == null) return 0.0;
            if (metric == SiteProgressPivotMetric.Reinforcement) return row.ReinforcementCompletedKg;
            if (metric == SiteProgressPivotMetric.Formwork) return row.FormworkCompletedM2;
            return row.VolumeCompletedM3;
        }

        internal void UpdateBoqRows(List<BoqTableRow> rows)
        {
            _boqAllRows = rows ?? new List<BoqTableRow>();
            PopulateBoqFilterOptions();
            ApplyBoqFilter();
            SyncPrepareBoqWithCurrentBoq(setMissingToZero: false);
            _ = TryAutoSyncBoqCsvAsync();
            _ = TryAutoSyncBoqExcelLinkAsync();
        }

        private void PopulateBoqFilterOptions()
        {
            string selectedElement = BoqElementFilterCombo?.SelectedItem as string;
            string selectedLevel = BoqBuildingLevelFilterCombo?.SelectedItem as string;

            var elementOptions = new List<string> { "All" };
            elementOptions.AddRange(_boqAllRows
                .Select(r => r.StructureElement)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => GetBoqStructureOrder(s))
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase));

            if (BoqElementFilterCombo != null)
            {
                BoqElementFilterCombo.ItemsSource = elementOptions;
                if (!string.IsNullOrWhiteSpace(selectedElement) &&
                    elementOptions.Any(s => string.Equals(s, selectedElement, StringComparison.OrdinalIgnoreCase)))
                {
                    BoqElementFilterCombo.SelectedItem = elementOptions.First(s => string.Equals(s, selectedElement, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    BoqElementFilterCombo.SelectedIndex = 0;
                }
            }

            var levelOptions = new List<string> { "All" };
            levelOptions.AddRange(_boqAllRows
                .Select(r => r.BuildingLevel)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            if (BoqBuildingLevelFilterCombo != null)
            {
                BoqBuildingLevelFilterCombo.ItemsSource = levelOptions;
                if (!string.IsNullOrWhiteSpace(selectedLevel) &&
                    levelOptions.Any(s => string.Equals(s, selectedLevel, StringComparison.OrdinalIgnoreCase)))
                {
                    BoqBuildingLevelFilterCombo.SelectedItem = levelOptions.First(s => string.Equals(s, selectedLevel, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    BoqBuildingLevelFilterCombo.SelectedIndex = 0;
                }
            }
        }

        private void ApplyBoqFilter()
        {
            string elementFilter = BoqElementFilterCombo?.SelectedItem as string;
            string levelFilter = BoqBuildingLevelFilterCombo?.SelectedItem as string;

            IEnumerable<BoqTableRow> query = _boqAllRows;
            if (!string.IsNullOrWhiteSpace(elementFilter) &&
                !string.Equals(elementFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => string.Equals(r.StructureElement, elementFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(levelFilter) &&
                !string.Equals(levelFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => string.Equals(r.BuildingLevel, levelFilter, StringComparison.OrdinalIgnoreCase));
            }

            List<BoqTableRow> filtered = query
                .ToList();

            BoqPivotMode pivotMode = GetBoqPivotMode();
            switch (pivotMode)
            {
                case BoqPivotMode.ByFloor:
                    filtered = filtered
                        .GroupBy(r => r.BuildingLevel ?? "", StringComparer.OrdinalIgnoreCase)
                        .Select(g => new BoqTableRow
                        {
                            StructureElement = "(All Elements)",
                            BuildingLevel = g.Key,
                            TypeName = "(All Types)",
                            Quantity = g.Sum(x => x.Quantity),
                            TotalVolumeM3 = g.Sum(x => x.TotalVolumeM3),
                            TotalFormworkAreaM2 = g.Sum(x => x.TotalFormworkAreaM2),
                            StructureOrder = 0
                        })
                        .OrderBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;

                case BoqPivotMode.ByElement:
                    filtered = filtered
                        .GroupBy(r => $"{r.StructureOrder}|{r.StructureElement}", StringComparer.OrdinalIgnoreCase)
                        .Select(g =>
                        {
                            BoqTableRow first = g.First();
                            return new BoqTableRow
                            {
                                StructureElement = first.StructureElement,
                                BuildingLevel = "(All Levels)",
                                TypeName = "(All Types)",
                                Quantity = g.Sum(x => x.Quantity),
                                TotalVolumeM3 = g.Sum(x => x.TotalVolumeM3),
                                TotalFormworkAreaM2 = g.Sum(x => x.TotalFormworkAreaM2),
                                StructureOrder = first.StructureOrder
                            };
                        })
                        .OrderBy(r => r.StructureOrder)
                        .ThenBy(r => r.StructureElement, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;

                case BoqPivotMode.ByFloorAndElement:
                    filtered = filtered
                        .GroupBy(r => $"{r.StructureOrder}|{r.StructureElement}|{r.BuildingLevel}", StringComparer.OrdinalIgnoreCase)
                        .Select(g =>
                        {
                            BoqTableRow first = g.First();
                            return new BoqTableRow
                            {
                                StructureElement = first.StructureElement,
                                BuildingLevel = first.BuildingLevel,
                                TypeName = "(All Types)",
                                Quantity = g.Sum(x => x.Quantity),
                                TotalVolumeM3 = g.Sum(x => x.TotalVolumeM3),
                                TotalFormworkAreaM2 = g.Sum(x => x.TotalFormworkAreaM2),
                                StructureOrder = first.StructureOrder
                            };
                        })
                        .OrderBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r.StructureOrder)
                        .ToList();
                    break;

                default:
                    filtered = filtered
                        .OrderBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r.StructureOrder)
                        .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
            }

            _boqRows.Clear();
            foreach (BoqTableRow row in filtered)
            {
                _boqRows.Add(row);
            }

            if (BoqSummaryText != null)
            {
                int levelCount = filtered
                    .Select(r => r.BuildingLevel)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                string modeText = pivotMode == BoqPivotMode.ByFloor
                    ? "Pivot Mode (Floor)"
                    : pivotMode == BoqPivotMode.ByElement
                        ? "Pivot Mode (Element)"
                        : pivotMode == BoqPivotMode.ByFloorAndElement
                            ? "Pivot Mode (Floor + Element)"
                            : "Detail Mode (with Type)";
                BoqSummaryText.Text = $"{modeText}. Tip: select cell(s) then Ctrl+C or drag to Excel / Prepare BOQ. Auto Sync writes to linked Excel or last CSV after Refresh/Generate.";
                UpdateBoqOverviewKpis(levelCount, modeText.Replace("Mode ", ""));
            }
            else
            {
                UpdateBoqOverviewKpis(levelCount: 0, modeText: "Detail (Type)");
            }
        }

        private BoqPivotMode GetBoqPivotMode()
        {
            string mode = BoqPivotModeCombo?.SelectedItem as string;
            if (string.Equals(mode, "Pivot by Floor", StringComparison.OrdinalIgnoreCase)) return BoqPivotMode.ByFloor;
            if (string.Equals(mode, "Pivot by Element", StringComparison.OrdinalIgnoreCase)) return BoqPivotMode.ByElement;
            if (string.Equals(mode, "Pivot by Floor + Element", StringComparison.OrdinalIgnoreCase)) return BoqPivotMode.ByFloorAndElement;
            return BoqPivotMode.DetailType;
        }

        private bool TryBuildBoqClipboardPayload(bool includeHeader, out string tsv, out string csv, out int rowCount, out int colCount)
        {
            tsv = "";
            csv = "";
            rowCount = 0;
            colCount = 0;

            if (BoqTableGrid == null) return false;

            List<BoqTableRow> rows = GetBoqRowsForPayload(out bool fromCellSelection, out int rowStartIndex);
            List<DataGridColumn> columns = GetBoqColumnsForPayload();

            if (rows.Count == 0 || columns.Count == 0) return false;

            _boqLastSelectionStartRowIndex = rowStartIndex;
            _boqLastSelectionRowCount = rows.Count;
            _boqLastSelectionColumnDisplayIndices = columns
                .Select(c => c.DisplayIndex)
                .OrderBy(i => i)
                .ToList();
            _boqLastSelectionIsCellBased = fromCellSelection;
            _boqLastSelectedCells = fromCellSelection ? GetSelectedBoqCellSet() : new HashSet<(int, int)>();

            var sbTsv = new StringBuilder(rows.Count * 64 + 128);
            var sbCsv = new StringBuilder(rows.Count * 64 + 128);
            if (includeHeader)
            {
                var headers = columns
                    .Select(c => SanitizeForTab(c?.Header?.ToString() ?? ""))
                    .ToList();
                sbTsv.AppendLine(string.Join("\t", headers));
                sbCsv.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            }

            for (int r = 0; r < rows.Count; r++)
            {
                BoqTableRow row = rows[r];
                int absoluteRowIndex = rowStartIndex + r;
                var vals = new List<string>(columns.Count);
                foreach (DataGridColumn col in columns)
                {
                    if (fromCellSelection && _boqLastSelectedCells != null)
                    {
                        if (!_boqLastSelectedCells.Contains((absoluteRowIndex, col.DisplayIndex)))
                        {
                            vals.Add("");
                            continue;
                        }
                    }

                    vals.Add(SanitizeForTab(GetBoqCellValue(row, col)));
                }
                sbTsv.AppendLine(string.Join("\t", vals));
                sbCsv.AppendLine(string.Join(",", vals.Select(EscapeCsv)));
            }

            tsv = sbTsv.ToString();
            csv = sbCsv.ToString();
            rowCount = rows.Count;
            colCount = columns.Count;
            return true;
        }

        private List<BoqTableRow> GetBoqRowsForPayload(out bool fromCellSelection, out int rowStartIndex)
        {
            fromCellSelection = false;
            rowStartIndex = 0;

            List<DataGridCellInfo> cells = BoqTableGrid.SelectedCells
                .Where(c => c.Column != null && c.Item is BoqTableRow)
                .ToList();

            if (cells.Count == 0)
            {
                List<BoqTableRow> selectedRows = GetSelectedBoqRows();
                if (selectedRows.Count > 0)
                {
                    BoqTableRow first = selectedRows.First();
                    rowStartIndex = Math.Max(0, _boqRows.IndexOf(first));
                    return selectedRows;
                }

                rowStartIndex = 0;
                return _boqRows.ToList();
            }

            var rowIndexes = new List<int>();
            foreach (DataGridCellInfo cell in cells)
            {
                if (cell.Item is BoqTableRow row)
                {
                    int idx = _boqRows.IndexOf(row);
                    if (idx >= 0) rowIndexes.Add(idx);
                }
            }

            if (rowIndexes.Count == 0)
            {
                rowStartIndex = 0;
                return _boqRows.ToList();
            }

            int min = rowIndexes.Min();
            int max = rowIndexes.Max();
            fromCellSelection = true;
            rowStartIndex = min;
            return _boqRows.Skip(min).Take(max - min + 1).ToList();
        }

        private List<DataGridColumn> GetBoqColumnsForPayload()
        {
            if (BoqTableGrid == null) return new List<DataGridColumn>();

            List<DataGridCellInfo> cells = BoqTableGrid.SelectedCells
                .Where(c => c.Column != null && c.Item is BoqTableRow)
                .ToList();

            if (cells.Count == 0)
            {
                return BoqTableGrid.Columns.OrderBy(c => c.DisplayIndex).ToList();
            }

            var colIndexes = new List<int>();
            foreach (DataGridCellInfo cell in cells)
            {
                colIndexes.Add(cell.Column.DisplayIndex);
            }

            if (colIndexes.Count == 0)
            {
                return BoqTableGrid.Columns.OrderBy(c => c.DisplayIndex).ToList();
            }

            int min = colIndexes.Min();
            int max = colIndexes.Max();
            return BoqTableGrid.Columns
                .Where(c => c.DisplayIndex >= min && c.DisplayIndex <= max)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
        }

        private HashSet<(int RowIndex, int ColDisplayIndex)> GetSelectedBoqCellSet()
        {
            var set = new HashSet<(int, int)>();
            if (BoqTableGrid == null) return set;

            foreach (DataGridCellInfo cell in BoqTableGrid.SelectedCells)
            {
                if (!(cell.Item is BoqTableRow row) || cell.Column == null) continue;
                int rowIndex = _boqRows.IndexOf(row);
                if (rowIndex < 0) continue;
                set.Add((rowIndex, cell.Column.DisplayIndex));
            }

            return set;
        }

        private static string GetBoqCellValue(BoqTableRow row, DataGridColumn column)
        {
            if (row == null || column == null) return "";

            string key = GetBoqColumnKey(column);
            switch (key)
            {
                case "StructureElement":
                case "Structure Element":
                    return row.StructureElement ?? "";
                case "BuildingLevel":
                case "Building Level":
                    return row.BuildingLevel ?? "";
                case "TypeName":
                case "Type":
                    return row.TypeName ?? "";
                case "Quantity":
                case "Qty":
                    return row.Quantity.ToString(CultureInfo.InvariantCulture);
                case "TotalVolumeM3":
                case "Volume (m3)":
                    return row.TotalVolumeM3.ToString("F3", CultureInfo.InvariantCulture);
                case "TotalFormworkAreaM2":
                case "Formwork (m2)":
                    return row.TotalFormworkAreaM2.ToString("F3", CultureInfo.InvariantCulture);
                default:
                    return "";
            }
        }

        private static string GetBoqColumnKey(DataGridColumn column)
        {
            if (column == null) return "";
            if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
            {
                return column.SortMemberPath.Trim();
            }

            if (column is DataGridBoundColumn bound &&
                bound.Binding is System.Windows.Data.Binding binding &&
                binding.Path != null &&
                !string.IsNullOrWhiteSpace(binding.Path.Path))
            {
                return binding.Path.Path.Trim();
            }

            return (column.Header?.ToString() ?? "").Trim();
        }

        private List<BoqTableRow> GetSelectedBoqRows()
        {
            if (BoqTableGrid == null || BoqTableGrid.SelectedItems == null)
            {
                return new List<BoqTableRow>();
            }

            var selected = new List<BoqTableRow>();
            foreach (object item in BoqTableGrid.SelectedItems)
            {
                if (item is BoqTableRow row)
                {
                    selected.Add(row);
                }
            }

            if (selected.Count == 0)
            {
                return selected;
            }

            var selectedSet = new HashSet<BoqTableRow>(selected);
            return _boqRows.Where(r => selectedSet.Contains(r)).ToList();
        }

        private List<BoqTableRow> GetBoqRowsForTransfer()
        {
            List<BoqTableRow> selected = GetSelectedBoqRows();
            if (selected.Count > 0)
            {
                return selected;
            }

            return _boqRows.ToList();
        }

        private static string BuildBoqTabSeparated(IReadOnlyList<BoqTableRow> rows, bool includeHeader)
        {
            var sb = new StringBuilder(rows.Count * 64 + 128);
            if (includeHeader)
            {
                sb.AppendLine("Structure Element\tBuildingLevel\tType\tQty\tVolume (m3)\tFormwork (m2)");
            }

            foreach (BoqTableRow row in rows)
            {
                sb.Append(SanitizeForTab(row.StructureElement)).Append('\t')
                  .Append(SanitizeForTab(row.BuildingLevel)).Append('\t')
                  .Append(SanitizeForTab(row.TypeName)).Append('\t')
                  .Append(row.Quantity.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.TotalVolumeM3.ToString("N3", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(row.TotalFormworkAreaM2.ToString("N3", CultureInfo.InvariantCulture))
                  .AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildBoqCsv(IReadOnlyList<BoqTableRow> rows, bool includeHeader)
        {
            var sb = new StringBuilder(rows.Count * 64 + 128);
            if (includeHeader)
            {
                sb.AppendLine("Structure Element,BuildingLevel,Type,Qty,Volume (m3),Formwork (m2)");
            }

            foreach (BoqTableRow row in rows)
            {
                sb.Append(EscapeCsv(row.StructureElement)).Append(',')
                  .Append(EscapeCsv(row.BuildingLevel)).Append(',')
                  .Append(EscapeCsv(row.TypeName)).Append(',')
                  .Append(row.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(row.TotalVolumeM3.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(row.TotalFormworkAreaM2.ToString("F3", CultureInfo.InvariantCulture))
                  .AppendLine();
            }

            return sb.ToString();
        }

        private string BuildPrepareBoqCsv(IReadOnlyList<PrepareBoqRow> rows)
        {
            var sb = new StringBuilder(rows.Count * 96 + 128);
            string hUse = HeaderFromMap(_prepareBoqHeaderMap, "Use", "Use");
            string hSheet = HeaderFromMap(_prepareBoqHeaderMap, "Sheet", "Sheet");
            string hItemCode = HeaderFromMap(_prepareBoqHeaderMap, "ItemCode", "Item Code");
            string hDescription = HeaderFromMap(_prepareBoqHeaderMap, "Description", "Description");
            string hUnit = HeaderFromMap(_prepareBoqHeaderMap, "Unit", "Unit");
            string hStructure = HeaderFromMap(_prepareBoqHeaderMap, "StructureElement", "Structure Element");
            string hLevel = HeaderFromMap(_prepareBoqHeaderMap, "BuildingLevel", "BuildingLevel");
            string hType = HeaderFromMap(_prepareBoqHeaderMap, "Type", "Type");
            string hQty = HeaderFromMap(_prepareBoqHeaderMap, "Qty", "Qty");
            string hVolume = HeaderFromMap(_prepareBoqHeaderMap, "Volume", "Volume (m3)");
            string hFormwork = HeaderFromMap(_prepareBoqHeaderMap, "Formwork", "Formwork (m2)");

            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(hUse),
                EscapeCsv(hSheet),
                EscapeCsv(hItemCode),
                EscapeCsv(hDescription),
                EscapeCsv(hUnit),
                EscapeCsv(hStructure),
                EscapeCsv(hLevel),
                EscapeCsv(hType),
                EscapeCsv(hQty),
                EscapeCsv(hVolume),
                EscapeCsv(hFormwork)
            }));

            foreach (PrepareBoqRow row in rows)
            {
                sb.Append(row.IsEnabled ? "1" : "0").Append(',')
                  .Append(EscapeCsv(row.SourceSheet)).Append(',')
                  .Append(EscapeCsv(row.ItemCode)).Append(',')
                  .Append(EscapeCsv(row.Description)).Append(',')
                  .Append(EscapeCsv(row.Unit)).Append(',')
                  .Append(EscapeCsv(row.StructureElement)).Append(',')
                  .Append(EscapeCsv(row.BuildingLevel)).Append(',')
                  .Append(EscapeCsv(row.TypeName)).Append(',')
                  .Append(row.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(row.TotalVolumeM3.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(row.TotalFormworkAreaM2.ToString("F3", CultureInfo.InvariantCulture))
                  .AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildBoqKey(string structureElement, string buildingLevel, string typeName)
        {
            return (structureElement ?? "").Trim().ToUpperInvariant() + "|" +
                   (buildingLevel ?? "").Trim().ToUpperInvariant() + "|" +
                   (typeName ?? "").Trim().ToUpperInvariant();
        }

        private Dictionary<string, BoqTableRow> BuildBoqIndexMapForSync()
        {
            var map = new Dictionary<string, BoqTableRow>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<BoqTableRow> source = (_boqAllRows != null && _boqAllRows.Count > 0)
                ? (IEnumerable<BoqTableRow>)_boqAllRows
                : (IEnumerable<BoqTableRow>)_boqRows;
            List<BoqTableRow> detailRows = source
                .Where(r =>
                    r != null &&
                    !string.IsNullOrWhiteSpace(r.StructureElement) &&
                    !string.IsNullOrWhiteSpace(r.BuildingLevel) &&
                    !string.IsNullOrWhiteSpace(r.TypeName))
                .ToList();

            foreach (BoqTableRow row in detailRows)
            {
                string key = BuildBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName);
                map[key] = row;
            }

            foreach (var g in detailRows.GroupBy(r => BuildBoqKey(r.StructureElement, r.BuildingLevel, "(All Types)"), StringComparer.OrdinalIgnoreCase))
            {
                map[g.Key] = new BoqTableRow
                {
                    StructureElement = g.First().StructureElement,
                    BuildingLevel = g.First().BuildingLevel,
                    TypeName = "(All Types)",
                    Quantity = g.Sum(x => x.Quantity),
                    TotalVolumeM3 = g.Sum(x => x.TotalVolumeM3),
                    TotalFormworkAreaM2 = g.Sum(x => x.TotalFormworkAreaM2),
                    StructureOrder = g.First().StructureOrder
                };
            }

            foreach (var g in detailRows.GroupBy(r => BuildBoqKey(r.StructureElement, "(All Levels)", "(All Types)"), StringComparer.OrdinalIgnoreCase))
            {
                map[g.Key] = new BoqTableRow
                {
                    StructureElement = g.First().StructureElement,
                    BuildingLevel = "(All Levels)",
                    TypeName = "(All Types)",
                    Quantity = g.Sum(x => x.Quantity),
                    TotalVolumeM3 = g.Sum(x => x.TotalVolumeM3),
                    TotalFormworkAreaM2 = g.Sum(x => x.TotalFormworkAreaM2),
                    StructureOrder = g.First().StructureOrder
                };
            }

            foreach (var g in detailRows.GroupBy(r => BuildBoqKey("(All Elements)", r.BuildingLevel, "(All Types)"), StringComparer.OrdinalIgnoreCase))
            {
                map[g.Key] = new BoqTableRow
                {
                    StructureElement = "(All Elements)",
                    BuildingLevel = g.First().BuildingLevel,
                    TypeName = "(All Types)",
                    Quantity = g.Sum(x => x.Quantity),
                    TotalVolumeM3 = g.Sum(x => x.TotalVolumeM3),
                    TotalFormworkAreaM2 = g.Sum(x => x.TotalFormworkAreaM2),
                    StructureOrder = 0
                };
            }

            return map;
        }

        private int SyncPrepareBoqWithCurrentBoq(bool setMissingToZero)
        {
            if (_prepareBoqRows.Count == 0)
            {
                UpdatePrepareBoqSummary();
                return 0;
            }

            Dictionary<string, BoqTableRow> index = BuildBoqIndexMapForSync();
            int updated = 0;
            foreach (PrepareBoqRow row in _prepareBoqRows)
            {
                string key = row.LinkedBoqKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = BuildBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName);
                }
                if (string.IsNullOrWhiteSpace(row.StructureElement) ||
                    string.IsNullOrWhiteSpace(row.BuildingLevel) ||
                    string.IsNullOrWhiteSpace(row.TypeName))
                {
                    continue;
                }

                if (index.TryGetValue(key, out BoqTableRow src))
                {
                    row.Quantity = src.Quantity;
                    row.TotalVolumeM3 = src.TotalVolumeM3;
                    row.TotalFormworkAreaM2 = src.TotalFormworkAreaM2;
                    row.LinkedBoqKey = key;
                    updated++;
                }
                else if (setMissingToZero)
                {
                    row.Quantity = 0;
                    row.TotalVolumeM3 = 0.0;
                    row.TotalFormworkAreaM2 = 0.0;
                }
            }

            PrepareBoqGrid?.Items.Refresh();
            UpdatePrepareBoqSummary();
            return updated;
        }

        private List<BoqTableRow> ParseBoqRowsFromDroppedText(string tsv)
        {
            var rows = new List<BoqTableRow>();
            if (string.IsNullOrWhiteSpace(tsv)) return rows;

            string[] lines = tsv.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw ?? "";
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 3) continue;

                string structure = (parts.Length > 0 ? parts[0] : "").Trim();
                string level = (parts.Length > 1 ? parts[1] : "").Trim();
                string type = (parts.Length > 2 ? parts[2] : "").Trim();
                if (string.IsNullOrWhiteSpace(structure) ||
                    string.IsNullOrWhiteSpace(level) ||
                    string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                int qty = 0;
                double vol = 0.0;
                double fwk = 0.0;
                if (parts.Length > 3)
                {
                    int.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out qty);
                    if (qty == 0) int.TryParse(parts[3], NumberStyles.Any, CultureInfo.CurrentCulture, out qty);
                }
                if (parts.Length > 4)
                {
                    double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out vol);
                    if (Math.Abs(vol) < 1e-9) double.TryParse(parts[4], NumberStyles.Any, CultureInfo.CurrentCulture, out vol);
                }
                if (parts.Length > 5)
                {
                    double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out fwk);
                    if (Math.Abs(fwk) < 1e-9) double.TryParse(parts[5], NumberStyles.Any, CultureInfo.CurrentCulture, out fwk);
                }

                rows.Add(new BoqTableRow
                {
                    StructureElement = structure,
                    BuildingLevel = level,
                    TypeName = type,
                    Quantity = qty,
                    TotalVolumeM3 = vol,
                    TotalFormworkAreaM2 = fwk
                });
            }

            return rows;
        }

        private int GetPrepareBoqDropRowIndex(System.Windows.Point point)
        {
            if (PrepareBoqGrid == null) return -1;
            DependencyObject hit = PrepareBoqGrid.InputHitTest(point) as DependencyObject;
            while (hit != null && !(hit is DataGridRow))
            {
                hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            }

            if (hit is DataGridRow row)
            {
                return row.GetIndex();
            }

            return -1;
        }

        private int GetPrepareBoqSelectedRowIndex()
        {
            if (PrepareBoqGrid == null)
            {
                return -1;
            }

            if (PrepareBoqGrid.CurrentItem is PrepareBoqRow current)
            {
                int idx = _prepareBoqRows.IndexOf(current);
                if (idx >= 0) return idx;
            }

            if (PrepareBoqGrid.SelectedItem is PrepareBoqRow selected)
            {
                int idx = _prepareBoqRows.IndexOf(selected);
                if (idx >= 0) return idx;
            }

            return -1;
        }

        private void UpdatePrepareBoqSummary()
        {
            int enabled = _prepareBoqRows.Count(r => r.IsEnabled);
            double sumVolume = _prepareBoqRows.Where(r => r.IsEnabled).Sum(r => r.TotalVolumeM3);
            double sumFormwork = _prepareBoqRows.Where(r => r.IsEnabled).Sum(r => r.TotalFormworkAreaM2);

            if (PrepareBoqSummaryText != null)
            {
                PrepareBoqSummaryText.Text = $"Prepare BOQ rows: {_prepareBoqRows.Count}, enabled: {enabled}, Volume: {sumVolume:N3} m3, Formwork: {sumFormwork:N3} m2.";
            }
            if (PrepareBoqKpiRowsText != null)
            {
                PrepareBoqKpiRowsText.Text = _prepareBoqRows.Count.ToString(CultureInfo.InvariantCulture);
            }
            if (PrepareBoqKpiEnabledText != null)
            {
                PrepareBoqKpiEnabledText.Text = enabled.ToString(CultureInfo.InvariantCulture);
            }
            if (PrepareBoqKpiVolumeText != null)
            {
                PrepareBoqKpiVolumeText.Text = sumVolume.ToString("N3", CultureInfo.InvariantCulture);
            }
            if (PrepareBoqKpiFormworkText != null)
            {
                PrepareBoqKpiFormworkText.Text = sumFormwork.ToString("N3", CultureInfo.InvariantCulture);
            }
            if (PrepareBoqEmptyStateText != null)
            {
                PrepareBoqEmptyStateText.Visibility = _prepareBoqRows.Count > 0
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;
            }
        }

        private static PrepareBoqImportResult ReadPrepareBoqFromFile(string path)
        {
            string ext = Path.GetExtension(path) ?? "";
            if (string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                return ReadPrepareBoqFromExcel(path);
            }

            return ReadPrepareBoqFromCsv(path);
        }

        private static PrepareBoqImportResult ReadPrepareBoqFromCsv(string path)
        {
            var result = new PrepareBoqImportResult();
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return result;

            List<string> headers = ParseCsvLine(lines[0]);
            int idxUse = IndexOfHeader(headers, "Use", "Enabled");
            int idxSheet = IndexOfHeader(headers, "Sheet", "Worksheet");
            int idxItemCode = IndexOfHeader(headers, "Item Code", "ItemCode", "Code");
            int idxDescription = IndexOfHeader(headers, "Description", "Desc");
            int idxUnit = IndexOfHeader(headers, "Unit", "UOM");
            int idxElement = IndexOfHeader(headers, "Structure Element", "StructureElement", "Element");
            int idxLevel = IndexOfHeader(headers, "BuildingLevel", "Building Level", "Level");
            int idxType = IndexOfHeader(headers, "Type", "Type Name", "TypeName");
            int idxQty = IndexOfHeader(headers, "Qty", "Quantity", "Tender Quantity", "Total Quantity");
            int idxVolume = IndexOfHeader(headers, "Volume (m3)", "Volume", "Concrete Volume", "Vol (m3)");
            int idxFormwork = IndexOfHeader(headers, "Formwork (m2)", "Formwork", "Formwork Area");

            if (idxUse >= 0) result.HeaderMap["Use"] = headers[idxUse];
            if (idxSheet >= 0) result.HeaderMap["Sheet"] = headers[idxSheet];
            if (idxItemCode >= 0) result.HeaderMap["ItemCode"] = headers[idxItemCode];
            if (idxDescription >= 0) result.HeaderMap["Description"] = headers[idxDescription];
            if (idxUnit >= 0) result.HeaderMap["Unit"] = headers[idxUnit];
            if (idxElement >= 0) result.HeaderMap["StructureElement"] = headers[idxElement];
            if (idxLevel >= 0) result.HeaderMap["BuildingLevel"] = headers[idxLevel];
            if (idxType >= 0) result.HeaderMap["Type"] = headers[idxType];
            if (idxQty >= 0) result.HeaderMap["Qty"] = headers[idxQty];
            if (idxVolume >= 0) result.HeaderMap["Volume"] = headers[idxVolume];
            if (idxFormwork >= 0) result.HeaderMap["Formwork"] = headers[idxFormwork];

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                List<string> cells = ParseCsvLine(lines[i]);
                var row = new PrepareBoqRow
                {
                    IsEnabled = BoolAt(cells, idxUse, true),
                    SourceSheet = ValueAt(cells, idxSheet),
                    ItemCode = ValueAt(cells, idxItemCode),
                    Description = ValueAt(cells, idxDescription),
                    Unit = string.IsNullOrWhiteSpace(ValueAt(cells, idxUnit)) ? "EA" : ValueAt(cells, idxUnit),
                    StructureElement = ValueAt(cells, idxElement),
                    BuildingLevel = ValueAt(cells, idxLevel),
                    TypeName = ValueAt(cells, idxType),
                    Quantity = IntAt(cells, idxQty),
                    TotalVolumeM3 = DoubleAt(cells, idxVolume),
                    TotalFormworkAreaM2 = DoubleAt(cells, idxFormwork)
                };
                if (!string.IsNullOrWhiteSpace(row.StructureElement) &&
                    !string.IsNullOrWhiteSpace(row.BuildingLevel) &&
                    !string.IsNullOrWhiteSpace(row.TypeName))
                {
                    row.LinkedBoqKey = BuildBoqKey(row.StructureElement, row.BuildingLevel, row.TypeName);
                }
                result.Rows.Add(row);
            }

            return result;
        }

        private static PrepareBoqImportResult ReadPrepareBoqFromExcel(string path)
        {
            var result = new PrepareBoqImportResult();

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
                    bool capturedHeader = false;
                    int importedSheets = 0;

                    for (int wsIndex = 1; wsIndex <= sheetCount; wsIndex++)
                    {
                        object worksheetObj = null;
                        object usedRangeObj = null;
                        try
                        {
                            worksheetObj = worksheets[wsIndex];
                            dynamic worksheet = worksheetObj;
                            usedRangeObj = worksheet.UsedRange;
                            dynamic usedRange = usedRangeObj;
                            object valuesObj = usedRange.Value2;
                            object[,] values = NormalizeExcelRangeToMatrix(valuesObj);
                            if (values == null)
                            {
                                continue;
                            }

                            string sheetName = Convert.ToString(worksheet.Name, CultureInfo.InvariantCulture) ?? ("Sheet" + wsIndex.ToString(CultureInfo.InvariantCulture));
                            int before = result.Rows.Count;
                            bool thisSheetCapturedHeader = AppendPrepareBoqRowsFromExcelMatrix(values, result, !capturedHeader, sheetName);
                            if (thisSheetCapturedHeader)
                            {
                                capturedHeader = true;
                            }

                            if (result.Rows.Count > before)
                            {
                                importedSheets++;
                            }
                        }
                        finally
                        {
                            SafeReleaseCom(usedRangeObj);
                            SafeReleaseCom(worksheetObj);
                        }
                    }

                    result.SheetCount = importedSheets;
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

            return result;
        }

        private static bool AppendPrepareBoqRowsFromExcelMatrix(object[,] values, PrepareBoqImportResult result, bool captureHeaderMap, string sheetName)
        {
            if (values == null || result == null)
            {
                return false;
            }

            int rMin = values.GetLowerBound(0);
            int rMax = values.GetUpperBound(0);
            int cMin = values.GetLowerBound(1);
            int cMax = values.GetUpperBound(1);

            int headerRow = -1;
            int scanMax = Math.Min(rMax, rMin + 120);
            for (int r = rMin; r <= scanMax; r++)
            {
                for (int c = cMin; c <= cMax; c++)
                {
                    string v = Convert.ToString(values[r, c], CultureInfo.InvariantCulture) ?? "";
                    if (v.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        headerRow = r;
                        break;
                    }
                }

                if (headerRow >= rMin) break;
            }

            int idxNo = cMin;
            int idxDescription = cMin + 1 <= cMax ? cMin + 1 : cMin;
            int idxUnit = cMin + 2 <= cMax ? cMin + 2 : cMin;
            int idxElement = -1;
            int idxLevel = -1;
            int idxType = -1;
            int idxQty = -1;
            int idxVolume = -1;
            int idxFormwork = -1;
            int startRow = rMin;
            bool headerCaptured = false;

            if (headerRow >= rMin)
            {
                startRow = headerRow + 1;
                for (int c = cMin; c <= cMax; c++)
                {
                    string h = Convert.ToString(values[headerRow, c], CultureInfo.InvariantCulture) ?? "";
                    string hc = h.Trim();
                    if (string.Equals(hc, "NO.", StringComparison.OrdinalIgnoreCase) || string.Equals(hc, "NO", StringComparison.OrdinalIgnoreCase))
                        idxNo = c;
                    else if (string.Equals(hc, "DESCRIPTION", StringComparison.OrdinalIgnoreCase) || string.Equals(hc, "Description", StringComparison.OrdinalIgnoreCase))
                        idxDescription = c;
                    else if (string.Equals(hc, "UNIT", StringComparison.OrdinalIgnoreCase) || string.Equals(hc, "Unit", StringComparison.OrdinalIgnoreCase))
                        idxUnit = c;
                    else if (string.Equals(hc, "Structure Element", StringComparison.OrdinalIgnoreCase))
                        idxElement = c;
                    else if (string.Equals(hc, "BuildingLevel", StringComparison.OrdinalIgnoreCase) || string.Equals(hc, "Building Level", StringComparison.OrdinalIgnoreCase))
                        idxLevel = c;
                    else if (string.Equals(hc, "Type", StringComparison.OrdinalIgnoreCase))
                        idxType = c;
                    else if (string.Equals(hc, "Qty", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(hc, "Quantity", StringComparison.OrdinalIgnoreCase) ||
                             hc.IndexOf("quantity", StringComparison.OrdinalIgnoreCase) >= 0)
                        idxQty = c;
                    else if (string.Equals(hc, "Volume (m3)", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(hc, "Volume", StringComparison.OrdinalIgnoreCase) ||
                             hc.IndexOf("volume", StringComparison.OrdinalIgnoreCase) >= 0)
                        idxVolume = c;
                    else if (string.Equals(hc, "Formwork (m2)", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(hc, "Formwork", StringComparison.OrdinalIgnoreCase) ||
                             hc.IndexOf("formwork", StringComparison.OrdinalIgnoreCase) >= 0)
                        idxFormwork = c;
                }

                if (captureHeaderMap)
                {
                    if (idxNo >= cMin) result.HeaderMap["ItemCode"] = ToCellString(values[headerRow, idxNo]);
                    if (idxDescription >= cMin) result.HeaderMap["Description"] = ToCellString(values[headerRow, idxDescription]);
                    if (idxUnit >= cMin) result.HeaderMap["Unit"] = ToCellString(values[headerRow, idxUnit]);
                    if (idxElement >= cMin) result.HeaderMap["StructureElement"] = ToCellString(values[headerRow, idxElement]);
                    if (idxLevel >= cMin) result.HeaderMap["BuildingLevel"] = ToCellString(values[headerRow, idxLevel]);
                    if (idxType >= cMin) result.HeaderMap["Type"] = ToCellString(values[headerRow, idxType]);
                    if (idxQty >= cMin) result.HeaderMap["Qty"] = ToCellString(values[headerRow, idxQty]);
                    if (idxVolume >= cMin) result.HeaderMap["Volume"] = ToCellString(values[headerRow, idxVolume]);
                    if (idxFormwork >= cMin) result.HeaderMap["Formwork"] = ToCellString(values[headerRow, idxFormwork]);
                    headerCaptured = true;
                }
            }

            for (int r = startRow; r <= rMax; r++)
            {
                string code = ToCellString(values[r, idxNo]);
                string desc = ToCellString(values[r, idxDescription]);
                string unit = ToCellString(values[r, idxUnit]);
                string element = idxElement >= cMin ? ToCellString(values[r, idxElement]) : "";
                string level = idxLevel >= cMin ? ToCellString(values[r, idxLevel]) : "";
                string type = idxType >= cMin ? ToCellString(values[r, idxType]) : "";
                int qty = idxQty >= cMin ? ToCellInt(values[r, idxQty]) : 0;
                double vol = idxVolume >= cMin ? ToCellDouble(values[r, idxVolume]) : 0.0;
                double fwk = idxFormwork >= cMin ? ToCellDouble(values[r, idxFormwork]) : 0.0;

                if (string.IsNullOrWhiteSpace(code) &&
                    string.IsNullOrWhiteSpace(desc) &&
                    string.IsNullOrWhiteSpace(element) &&
                    string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                result.Rows.Add(new PrepareBoqRow
                {
                    IsEnabled = true,
                    SourceSheet = sheetName ?? "",
                    ItemCode = code,
                    Description = desc,
                    Unit = string.IsNullOrWhiteSpace(unit) ? "EA" : unit,
                    StructureElement = element,
                    BuildingLevel = level,
                    TypeName = type,
                    Quantity = qty,
                    TotalVolumeM3 = vol,
                    TotalFormworkAreaM2 = fwk,
                    LinkedBoqKey =
                        !string.IsNullOrWhiteSpace(element) &&
                        !string.IsNullOrWhiteSpace(level) &&
                        !string.IsNullOrWhiteSpace(type)
                            ? BuildBoqKey(element, level, type)
                            : ""
                });
            }

            return headerCaptured;
        }

        private void ExportBoqRowsToExcel(List<BoqTableRow> rows, string filePath)
        {
            if (rows == null || rows.Count == 0)
            {
                throw new InvalidOperationException("No BOQ rows to export.");
            }

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            object worksheetObj = null;
            object rangeObj = null;
            object topLeftObj = null;
            object bottomRightObj = null;
            object headerRangeObj = null;

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

                worksheetObj = workbook.Worksheets[1];
                dynamic worksheet = worksheetObj;
                worksheet.Name = "BOQ";

                int rowCount = rows.Count + 1;
                const int colCount = 6;
                var matrix = new object[rowCount, colCount];

                matrix[0, 0] = "Structure Element";
                matrix[0, 1] = "BuildingLevel";
                matrix[0, 2] = "Type";
                matrix[0, 3] = "Qty";
                matrix[0, 4] = "Volume (m3)";
                matrix[0, 5] = "Formwork (m2)";

                for (int i = 0; i < rows.Count; i++)
                {
                    BoqTableRow row = rows[i];
                    matrix[i + 1, 0] = row.StructureElement ?? "";
                    matrix[i + 1, 1] = row.BuildingLevel ?? "";
                    matrix[i + 1, 2] = row.TypeName ?? "";
                    matrix[i + 1, 3] = row.Quantity;
                    matrix[i + 1, 4] = Math.Round(row.TotalVolumeM3, 3);
                    matrix[i + 1, 5] = Math.Round(row.TotalFormworkAreaM2, 3);
                }

                topLeftObj = worksheet.Cells[1, 1];
                bottomRightObj = worksheet.Cells[rowCount, colCount];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Value2 = matrix;

                headerRangeObj = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[1, colCount]];
                dynamic headerRange = headerRangeObj;
                headerRange.Font.Bold = true;

                try
                {
                    worksheet.Columns.AutoFit();
                }
                catch
                {
                    // ignore formatting issues
                }

                // 51 = xlOpenXMLWorkbook (*.xlsx)
                workbook.SaveAs(filePath, 51);
                workbook.Close(true);
                app.Quit();
            }
            finally
            {
                SafeReleaseCom(headerRangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(worksheetObj);
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private void ExportBoqReportRowsToExcel(List<BoqReportRow> rows, string filePath)
        {
            if (rows == null || rows.Count == 0)
            {
                throw new InvalidOperationException("No BOQ report rows to export.");
            }

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            object worksheetObj = null;
            object rangeObj = null;
            object topLeftObj = null;
            object bottomRightObj = null;
            object headerRangeObj = null;

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

                worksheetObj = workbook.Worksheets[1];
                dynamic worksheet = worksheetObj;
                worksheet.Name = "BOQ Report";

                try
                {
                    worksheet.Columns[3].NumberFormat = "@";
                }
                catch
                {
                    // Export should still continue if Excel refuses formatting.
                }

                List<BoqReportColumnSpec> columns = (_boqReportColumns != null && _boqReportColumns.Count > 0)
                    ? _boqReportColumns
                    : GetBoqReportColumnSpecs(_boqReportMode);
                int rowCount = rows.Count + 1;
                int colCount = Math.Max(1, columns.Count);
                var matrix = new object[rowCount, colCount];

                for (int c = 0; c < columns.Count; c++)
                {
                    matrix[0, c] = columns[c].Header ?? "";
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    BoqReportRow row = rows[i];
                    for (int c = 0; c < columns.Count; c++)
                    {
                        matrix[i + 1, c] = GetBoqReportExportValue(row, columns[c].BindingPath);
                    }
                }

                topLeftObj = worksheet.Cells[1, 1];
                bottomRightObj = worksheet.Cells[rowCount, colCount];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Value2 = matrix;

                headerRangeObj = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[1, colCount]];
                dynamic headerRange = headerRangeObj;
                headerRange.Font.Bold = true;

                try
                {
                    worksheet.Columns.AutoFit();
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
                SafeReleaseCom(headerRangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(worksheetObj);
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private void ExportPrepareBoqRowsToExcel(List<PrepareBoqRow> rows, string filePath)
        {
            if (rows == null || rows.Count == 0)
            {
                throw new InvalidOperationException("No Prepare BOQ rows to export.");
            }

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            object worksheetObj = null;
            object rangeObj = null;
            object topLeftObj = null;
            object bottomRightObj = null;
            object headerRangeObj = null;

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

                worksheetObj = workbook.Worksheets[1];
                dynamic worksheet = worksheetObj;
                worksheet.Name = "PrepareBOQ";

                int rowCount = rows.Count + 1;
                const int colCount = 15;
                var matrix = new object[rowCount, colCount];

                matrix[0, 0] = HeaderFromMap(_prepareBoqHeaderMap, "Use", "Use");
                matrix[0, 1] = HeaderFromMap(_prepareBoqHeaderMap, "Sheet", "Sheet");
                matrix[0, 2] = HeaderFromMap(_prepareBoqHeaderMap, "ItemCode", "Item Code");
                matrix[0, 3] = HeaderFromMap(_prepareBoqHeaderMap, "Description", "Description");
                matrix[0, 4] = HeaderFromMap(_prepareBoqHeaderMap, "Unit", "Unit");
                matrix[0, 5] = HeaderFromMap(_prepareBoqHeaderMap, "StructureElement", "Structure Element");
                matrix[0, 6] = HeaderFromMap(_prepareBoqHeaderMap, "BuildingLevel", "BuildingLevel");
                matrix[0, 7] = HeaderFromMap(_prepareBoqHeaderMap, "Room", "Room");
                matrix[0, 8] = HeaderFromMap(_prepareBoqHeaderMap, "Type", "Type");
                matrix[0, 9] = HeaderFromMap(_prepareBoqHeaderMap, "Qty", "Qty");
                matrix[0, 10] = HeaderFromMap(_prepareBoqHeaderMap, "Volume", "Volume (m3)");
                matrix[0, 11] = HeaderFromMap(_prepareBoqHeaderMap, "Formwork", "Formwork (m2)");
                matrix[0, 12] = HeaderFromMap(_prepareBoqHeaderMap, "QsRuleCode", "Rule Code");
                matrix[0, 13] = HeaderFromMap(_prepareBoqHeaderMap, "QsFormula", "Formula");
                matrix[0, 14] = HeaderFromMap(_prepareBoqHeaderMap, "QsBreakdown", "Breakdown");

                for (int i = 0; i < rows.Count; i++)
                {
                    PrepareBoqRow row = rows[i];
                    matrix[i + 1, 0] = row.IsEnabled ? 1 : 0;
                    matrix[i + 1, 1] = row.SourceSheet ?? "";
                    matrix[i + 1, 2] = row.ItemCode ?? "";
                    matrix[i + 1, 3] = row.Description ?? "";
                    matrix[i + 1, 4] = row.Unit ?? "";
                    matrix[i + 1, 5] = row.StructureElement ?? "";
                    matrix[i + 1, 6] = row.BuildingLevel ?? "";
                    matrix[i + 1, 7] = row.Room ?? "";
                    matrix[i + 1, 8] = row.TypeName ?? "";
                    matrix[i + 1, 9] = row.Quantity;
                    matrix[i + 1, 10] = Math.Round(row.TotalVolumeM3, 3);
                    matrix[i + 1, 11] = Math.Round(row.TotalFormworkAreaM2, 3);
                    matrix[i + 1, 12] = row.QsRuleCode ?? "";
                    matrix[i + 1, 13] = row.QsFormula ?? "";
                    matrix[i + 1, 14] = row.QsBreakdown ?? "";
                }

                topLeftObj = worksheet.Cells[1, 1];
                bottomRightObj = worksheet.Cells[rowCount, colCount];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Value2 = matrix;

                headerRangeObj = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[1, colCount]];
                dynamic headerRange = headerRangeObj;
                headerRange.Font.Bold = true;

                try
                {
                    worksheet.Columns.AutoFit();
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
                SafeReleaseCom(headerRangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(worksheetObj);
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private async Task TryAutoSyncBoqCsvAsync()
        {
            if (BoqAutoSyncCsvCheck?.IsChecked != true) return;
            if (string.IsNullOrWhiteSpace(_boqLastCsvExportPath)) return;

            List<BoqTableRow> rows = _boqRows.ToList();
            if (rows.Count == 0) return;

            string path = _boqLastCsvExportPath;
            try
            {
                string csv = await Task.Run(() => BuildBoqCsv(rows, true));
                await Task.Run(() => File.WriteAllText(path, csv, new UTF8Encoding(false)));
                ShowStatus($"BOQ auto-synced CSV: {path} ({rows.Count} row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus("BOQ auto-sync failed: " + ex.Message);
            }
        }

        private bool TryCaptureBoqExcelLink(int writeRows, int writeCols)
        {
            if (writeRows <= 0 || writeCols <= 0) return false;

            object appObj = null;
            object workbook = null;
            object worksheet = null;
            object activeCell = null;
            bool captured = false;

            try
            {
                appObj = ComActiveObject.GetActiveObject("Excel.Application");
                if (appObj == null) return false;

                dynamic app = appObj;
                workbook = app.ActiveWorkbook;
                worksheet = app.ActiveSheet;
                activeCell = app.ActiveCell;
                if (workbook == null || worksheet == null || activeCell == null) return false;

                string workbookName = Convert.ToString(((dynamic)workbook).FullName) ?? "";
                string worksheetName = Convert.ToString(((dynamic)worksheet).Name) ?? "";
                int startRow = Convert.ToInt32(((dynamic)activeCell).Row, CultureInfo.InvariantCulture);
                int startCol = Convert.ToInt32(((dynamic)activeCell).Column, CultureInfo.InvariantCulture);

                if (startRow <= 0 || startCol <= 0 || string.IsNullOrWhiteSpace(worksheetName)) return false;

                _boqExcelLink = new BoqExcelLinkTarget
                {
                    WorkbookFullName = workbookName,
                    WorksheetName = worksheetName,
                    StartRow = startRow,
                    StartColumn = startCol,
                    LastWriteRows = writeRows,
                    LastWriteCols = writeCols,
                    SelectionStartRowIndex = Math.Max(0, _boqLastSelectionStartRowIndex),
                    SelectionRowCount = Math.Max(1, _boqLastSelectionRowCount),
                    SelectionColumnDisplayIndices = (_boqLastSelectionColumnDisplayIndices ?? new List<int>())
                        .Distinct()
                        .OrderBy(i => i)
                        .ToList()
                };

                captured = true;
                ShowStatus($"Excel link captured: {worksheetName} R{startRow}C{startCol}.");
            }
            catch
            {
                // Excel may not be running or expose COM context from this session.
                captured = false;
            }
            finally
            {
                SafeReleaseCom(activeCell);
                SafeReleaseCom(worksheet);
                SafeReleaseCom(workbook);
                SafeReleaseCom(appObj);
            }

            return captured;
        }

        private async Task TryAutoSyncBoqExcelLinkAsync()
        {
            if (BoqAutoSyncCsvCheck?.IsChecked != true) return;
            if (_boqExcelLink == null || !_boqExcelLink.IsValid) return;
            if (_boqExcelSyncInProgress) return;

            _boqExcelSyncInProgress = true;
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    if (TryAutoSyncBoqExcelLinkOnce())
                    {
                        return;
                    }

                    await Task.Delay(150);
                }
            }
            finally
            {
                _boqExcelSyncInProgress = false;
            }
        }

        private bool TryAutoSyncBoqExcelLinkOnce()
        {
            if (_boqExcelLink == null || !_boqExcelLink.IsValid) return false;

            if (!TryBuildBoqMatrixForExcelLink(out object[,] matrix, out int rows, out int cols) || rows <= 0 || cols <= 0)
            {
                return false;
            }

            object appObj = null;
            object workbooks = null;
            object workbook = null;
            object worksheet = null;
            object oldRange = null;
            object range = null;
            object topLeft = null;
            object bottomRight = null;

            try
            {
                appObj = ComActiveObject.GetActiveObject("Excel.Application");
                if (appObj == null) return false;
                dynamic app = appObj;

                workbooks = app.Workbooks;
                workbook = FindExcelWorkbook(workbooks, _boqExcelLink.WorkbookFullName) ?? app.ActiveWorkbook;
                if (workbook == null) return false;

                worksheet = GetExcelWorksheet(workbook, _boqExcelLink.WorksheetName) ?? ((dynamic)workbook).ActiveSheet;
                if (worksheet == null) return false;

                int row0 = _boqExcelLink.StartRow;
                int col0 = _boqExcelLink.StartColumn;
                if (row0 <= 0 || col0 <= 0) return false;

                if (_boqExcelLink.LastWriteRows > 0 && _boqExcelLink.LastWriteCols > 0)
                {
                    dynamic wsOld = worksheet;
                    object oldTopLeft = wsOld.Cells[row0, col0];
                    object oldBottomRight = wsOld.Cells[row0 + _boqExcelLink.LastWriteRows - 1, col0 + _boqExcelLink.LastWriteCols - 1];
                    oldRange = wsOld.Range[oldTopLeft, oldBottomRight];
                    ((dynamic)oldRange).ClearContents();
                    SafeReleaseCom(oldBottomRight);
                    SafeReleaseCom(oldTopLeft);
                }

                dynamic ws = worksheet;
                topLeft = ws.Cells[row0, col0];
                bottomRight = ws.Cells[row0 + rows - 1, col0 + cols - 1];
                range = ws.Range[topLeft, bottomRight];
                for (int c = 0; c < cols; c++)
                {
                    string header = matrix[0, c] as string ?? "";
                    if (!string.Equals(header, "BuildingLevel", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(header, "Building Level", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        object columnTop = ws.Cells[row0, col0 + c];
                        object columnBottom = ws.Cells[row0 + rows - 1, col0 + c];
                        object columnRange = ws.Range[columnTop, columnBottom];
                        ((dynamic)columnRange).NumberFormat = "@";
                        SafeReleaseCom(columnRange);
                        SafeReleaseCom(columnBottom);
                        SafeReleaseCom(columnTop);
                    }
                    catch
                    {
                        // Auto-sync should continue even if Excel refuses formatting.
                    }
                }

                ((dynamic)range).Value2 = matrix;

                _boqExcelLink.LastWriteRows = rows;
                _boqExcelLink.LastWriteCols = cols;
                return true;
            }
            catch
            {
                // Skip hard errors to avoid breaking user workflow.
                return false;
            }
            finally
            {
                SafeReleaseCom(bottomRight);
                SafeReleaseCom(topLeft);
                SafeReleaseCom(range);
                SafeReleaseCom(oldRange);
                SafeReleaseCom(worksheet);
                SafeReleaseCom(workbook);
                SafeReleaseCom(workbooks);
                SafeReleaseCom(appObj);
            }
        }

        private bool TryBuildBoqMatrixForExcelLink(out object[,] matrix, out int rowCount, out int colCount)
        {
            matrix = null;
            rowCount = 0;
            colCount = 0;
            if (_boqExcelLink == null || BoqTableGrid == null) return false;

            List<DataGridColumn> columns = GetBoqColumnsForExcelLink();
            List<BoqTableRow> rows = GetBoqRowsForExcelLink();
            if (columns.Count == 0 || rows.Count == 0) return false;

            rowCount = rows.Count;
            colCount = columns.Count;
            matrix = new object[rowCount, colCount];

            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    if (_boqLastSelectionIsCellBased &&
                        _boqLastSelectedCells != null &&
                        _boqLastSelectedCells.Count > 0)
                    {
                        int rowIndex = _boqExcelLink.SelectionStartRowIndex + r;
                        int colIndex = columns[c].DisplayIndex;
                        if (!_boqLastSelectedCells.Contains((rowIndex, colIndex)))
                        {
                            matrix[r, c] = "";
                            continue;
                        }
                    }

                    matrix[r, c] = GetBoqCellValue(rows[r], columns[c]);
                }
            }

            return true;
        }

        private List<DataGridColumn> GetBoqColumnsForExcelLink()
        {
            if (BoqTableGrid == null) return new List<DataGridColumn>();

            List<int> display = _boqExcelLink?.SelectionColumnDisplayIndices?
                .Distinct()
                .OrderBy(i => i)
                .ToList() ?? new List<int>();

            if (display.Count == 0)
            {
                return BoqTableGrid.Columns.OrderBy(c => c.DisplayIndex).ToList();
            }

            var lookup = BoqTableGrid.Columns
                .GroupBy(c => c.DisplayIndex)
                .ToDictionary(g => g.Key, g => g.First());
            var cols = new List<DataGridColumn>();
            foreach (int idx in display)
            {
                if (lookup.TryGetValue(idx, out DataGridColumn col))
                {
                    cols.Add(col);
                }
            }

            return cols.Count > 0 ? cols : BoqTableGrid.Columns.OrderBy(c => c.DisplayIndex).ToList();
        }

        private List<BoqTableRow> GetBoqRowsForExcelLink()
        {
            if (_boqRows.Count == 0) return new List<BoqTableRow>();

            if (_boqExcelLink == null || _boqExcelLink.SelectionRowCount <= 0)
            {
                return _boqRows.ToList();
            }

            int start = Math.Max(0, _boqExcelLink.SelectionStartRowIndex);
            if (start >= _boqRows.Count) start = _boqRows.Count - 1;

            int count = Math.Max(1, _boqExcelLink.SelectionRowCount);
            if (start + count > _boqRows.Count)
            {
                count = _boqRows.Count - start;
            }

            return _boqRows.Skip(start).Take(count).ToList();
        }

        private static int GetBoqStructureOrder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return int.MaxValue;
            if (string.Equals(name, "Foundation", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(name, "Structural Column", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(name, "Wall", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(name, "Structural Framing", StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(name, "Floor", StringComparison.OrdinalIgnoreCase)) return 5;
            if (string.Equals(name, "Stair", StringComparison.OrdinalIgnoreCase)) return 6;
            if (string.Equals(name, "Soil Excavation", StringComparison.OrdinalIgnoreCase)) return 7;
            if (string.Equals(name, "Soil Backfilled", StringComparison.OrdinalIgnoreCase)) return 8;
            return 99;
        }

        private QsScope GetQsScope()
        {
            if (QsScopeSelectionRadio?.IsChecked == true)
            {
                return QsScope.CurrentSelection;
            }
            if (QsScopeAllRadio?.IsChecked == true)
            {
                return QsScope.EntireModel;
            }

            return QsScope.CurrentView;
        }

        private void UpdateQsOverviewKpis()
        {
            if (QsScopeBadgeText != null)
            {
                string scopeText = GetQsScope() == QsScope.CurrentSelection
                    ? "Current Selection"
                    : GetQsScope() == QsScope.EntireModel
                        ? "Entire Model"
                        : "Current View";
                QsScopeBadgeText.Text = scopeText;
            }

            if (QsCategoryCountText != null)
            {
                var categories = new[]
                {
                    QsStructuralFramingCheck,
                    QsStructuralColumnCheck,
                    QsStructuralWallCheck,
                    QsStructuralFloorCheck,
                    QsStructuralStairCheck,
                    QsFoundationCheck,
                    QsCreateFormworkShapeCheck
                };
                int selectedCount = categories.Count(cb => cb?.IsChecked == true);
                QsCategoryCountText.Text = selectedCount.ToString(CultureInfo.InvariantCulture) + " selected";
            }

            if (QsActiveRulesCountText != null)
            {
                System.Windows.Controls.CheckBox qsSoilExcavationIncludeCheck = GetQsSoilExcavationIncludeCheck();
                System.Windows.Controls.CheckBox qsSoilBackfilledIncludeCheck = GetQsSoilBackfilledIncludeCheck();
                System.Windows.Controls.CheckBox qsSoilBackfilledSubtractStructureCheck = GetQsSoilBackfilledSubtractStructureCheck();

                var rules = new[]
                {
                    QsFoundationTopCheck,
                    QsColumnSubtractBeamCheck,
                    QsColumnSubtractBeamGECheck,
                    QsWallOpeningBottomCheck,
                    QsBeamBottomCheck,
                    QsFloorBottomCheck,
                    QsFloorSubtractBeamCheck,
                    QsFloorSubtractFoundationCheck,
                    QsFloorSubtractOthersCheck,
                    QsStairTopCheck,
                    QsStairSubtractBeamCheck,
                    QsStairSubtractOthersCheck,
                    qsSoilExcavationIncludeCheck,
                    qsSoilBackfilledIncludeCheck,
                    qsSoilBackfilledSubtractStructureCheck
                };
                int enabledRules = rules.Count(cb => cb?.IsChecked == true);
                QsActiveRulesCountText.Text = enabledRules.ToString(CultureInfo.InvariantCulture) + " enabled";
            }
        }

        private void UpdateBoqOverviewKpis(int levelCount, string modeText)
        {
            if (BoqKpiRowsText != null)
            {
                BoqKpiRowsText.Text = _boqRows.Count.ToString(CultureInfo.InvariantCulture);
            }
            if (BoqKpiSourceRowsText != null)
            {
                BoqKpiSourceRowsText.Text = _boqAllRows.Count.ToString(CultureInfo.InvariantCulture);
            }
            if (BoqKpiLevelsText != null)
            {
                BoqKpiLevelsText.Text = levelCount.ToString(CultureInfo.InvariantCulture);
            }
            if (BoqKpiModeText != null)
            {
                BoqKpiModeText.Text = string.IsNullOrWhiteSpace(modeText) ? "Detail (Type)" : modeText;
            }
            if (BoqEmptyStateText != null)
            {
                BoqEmptyStateText.Visibility = _boqRows.Count > 0
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;
            }
        }

        private void InitializeQsDefaults()
        {
            // Do not hardcode machine-specific path. Leave empty so runtime resolver can find/create a local file.
            _handler.Request.QsSharedParameterFilePath = "";
            _handler.Request.QsSharedParameterGroupName = "CBIM-QS";

            if (QsSummaryText != null)
            {
                QsSummaryText.Text = "Ready to calculate formwork quantities.";
            }
            if (BoqSummaryText != null)
            {
                BoqSummaryText.Text = "Click Refresh to load BOQ table.";
            }
            if (BoqElementFilterCombo != null)
            {
                BoqElementFilterCombo.ItemsSource = new List<string> { "All" };
                BoqElementFilterCombo.SelectedIndex = 0;
            }
            if (BoqBuildingLevelFilterCombo != null)
            {
                BoqBuildingLevelFilterCombo.ItemsSource = new List<string> { "All" };
                BoqBuildingLevelFilterCombo.SelectedIndex = 0;
            }
            PopulateBoqReportFilterOptions();
            if (BoqPivotModeCombo != null)
            {
                BoqPivotModeCombo.ItemsSource = new List<string>
                {
                    "Detail (Type)",
                    "Pivot by Floor",
                    "Pivot by Element",
                    "Pivot by Floor + Element",
                    "Pivot by Floor + Room + Element"
                };
                BoqPivotModeCombo.SelectedIndex = 0;
            }
            if (BoqAutoSyncCsvCheck != null)
            {
                BoqAutoSyncCsvCheck.IsChecked = true;
            }
            InitializeQsMeasurementSettings();
            InitializeQsMeasurementRules();
            RegisterQsUiOptionHandlers();
            UpdateQsOverviewKpis();
            UpdateBoqOverviewKpis(levelCount: 0, modeText: "Detail (Type)");
            RefreshBoqReportCategoryTree();
            ApplyBoqReport();
            UpdatePrepareBoqSummary();
        }
        private readonly ObservableCollection<BoqTableRow> _boqRows = new ObservableCollection<BoqTableRow>();
        private readonly ObservableCollection<PrepareBoqRow> _prepareBoqRows = new ObservableCollection<PrepareBoqRow>();
        private Dictionary<string, string> _prepareBoqHeaderMap = CreateDefaultPrepareBoqHeaderMap();
        private List<BoqTableRow> _boqAllRows = new List<BoqTableRow>();
        private string _boqLastCsvExportPath = "";
        private const string BoqInternalDragRowsFormat = "CamboBIM.BoqRows";
        private System.Windows.Point _boqDragStartPoint;
        private BoqExcelLinkTarget _boqExcelLink;
        private int _boqLastSelectionStartRowIndex = -1;
        private int _boqLastSelectionRowCount = 0;
        private List<int> _boqLastSelectionColumnDisplayIndices = new List<int>();
        private bool _boqLastSelectionIsCellBased;
        private HashSet<(int RowIndex, int ColDisplayIndex)> _boqLastSelectedCells = new HashSet<(int, int)>();
        private bool _boqExcelSyncInProgress;
        private const string MsProjectTaskNumberFieldBoq = "Number20";
        private const string MsProjectTaskNumberAliasBoq = "Boq";

        private enum BoqPivotMode
        {
            DetailType,
            ByFloor,
            ByElement,
            ByFloorAndElement,
            ByFloorRoomElement
        }

        private sealed class BoqExcelLinkTarget
        {
            public string WorkbookFullName { get; set; } = "";
            public string WorksheetName { get; set; } = "";
            public int StartRow { get; set; }
            public int StartColumn { get; set; }
            public int LastWriteRows { get; set; }
            public int LastWriteCols { get; set; }
            public int SelectionStartRowIndex { get; set; }
            public int SelectionRowCount { get; set; }
            public List<int> SelectionColumnDisplayIndices { get; set; } = new List<int>();

            public bool IsValid =>
                StartRow > 0 &&
                StartColumn > 0 &&
                !string.IsNullOrWhiteSpace(WorksheetName);
        }

        private sealed class PrepareBoqRow
        {
            public bool IsEnabled { get; set; } = true;
            public string SourceSheet { get; set; } = "";
            public string ItemCode { get; set; } = "";
            public string Description { get; set; } = "";
            public string Unit { get; set; } = "EA";
            public string StructureElement { get; set; } = "";
            public string BuildingLevel { get; set; } = "";
            public string Room { get; set; } = "";
            public string TypeName { get; set; } = "";
            public int Quantity { get; set; }
            public double TotalVolumeM3 { get; set; }
            public double TotalFormworkAreaM2 { get; set; }
            public string QsRuleCode { get; set; } = "";
            public string QsFormula { get; set; } = "";
            public string QsBreakdown { get; set; } = "";
            public string LinkedBoqKey { get; set; } = "";
        }

        private sealed class PrepareBoqImportResult
        {
            public List<PrepareBoqRow> Rows { get; } = new List<PrepareBoqRow>();
            public Dictionary<string, string> HeaderMap { get; } = CreateDefaultPrepareBoqHeaderMap();
            public int SheetCount { get; set; }
        }

        private sealed class PowerBiBoqProgressAggregate
        {
            public double BoqRebarKg { get; set; }
            public double CompletedRebarKg { get; set; }
            public double BoqFormworkM2 { get; set; }
            public double CompletedFormworkM2 { get; set; }
            public double BoqVolumeM3 { get; set; }
            public double CompletedVolumeM3 { get; set; }
        }
    }
}
