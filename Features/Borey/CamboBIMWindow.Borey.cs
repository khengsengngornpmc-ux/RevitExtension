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

        private DataTable LoadPowerBiBoreySourceTable(string logicalTableName, out string warning)
        {
            warning = "";
            string logical = string.IsNullOrWhiteSpace(logicalTableName) ? "Project_Info" : logicalTableName.Trim();

            if (_boreyRevitTableCache.TryGetValue(logical, out DataTable cached) && cached != null)
            {
                try
                {
                    return SanitizePowerBiBoreySourceTable(cached.Copy());
                }
                catch
                {
                    // Fall back to DB query below.
                }
            }

            string dbPath = ResolveBoreyDbPathForTableCached(logical, out string physicalTableName);
            if (string.IsNullOrWhiteSpace(dbPath) || string.IsNullOrWhiteSpace(physicalTableName))
            {
                warning = logical + ": source DB table not found.";
                return CreateEmptyPowerBiBoreyTable(logical);
            }

            try
            {
                DataTable queried = QueryBoreyProjectedTable(
                    dbPath,
                    physicalTableName,
                    logical,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                return SanitizePowerBiBoreySourceTable(queried);
            }
            catch (Exception ex)
            {
                warning = logical + ": failed to load source table (" + ex.Message + ").";
                return CreateEmptyPowerBiBoreyTable(logical);
            }
        }

        private static DataTable CreateEmptyPowerBiBoreyTable(string logicalTableName)
        {
            string logical = string.IsNullOrWhiteSpace(logicalTableName) ? "Project_Info" : logicalTableName.Trim();
            var table = new DataTable(logical);
            foreach (string column in GetBoreyLogicalColumns(logical))
            {
                if (!table.Columns.Contains(column))
                {
                    table.Columns.Add(column, typeof(string));
                }
            }

            return table;
        }

        private static DataTable BuildPowerBiBoreyFactTable(string logicalTableName, DataTable source, DateTime snapshotUtc)
        {
            var fact = new DataTable("fact_borey_" + NormalizePowerBiName(logicalTableName));
            fact.Columns.Add("snapshot_utc", typeof(string));
            fact.Columns.Add("source_table", typeof(string));
            fact.Columns.Add("house_key", typeof(string));

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "snapshot_utc",
                "source_table",
                "house_key"
            };
            var mapped = new List<KeyValuePair<DataColumn, string>>();
            if (source != null)
            {
                foreach (DataColumn col in source.Columns)
                {
                    string normalized = EnsureUniquePowerBiName(used, NormalizePowerBiName(col.ColumnName));
                    fact.Columns.Add(normalized, typeof(string));
                    mapped.Add(new KeyValuePair<DataColumn, string>(col, normalized));
                }

                foreach (DataRow row in source.Rows)
                {
                    if (row == null || row.RowState == DataRowState.Deleted)
                    {
                        continue;
                    }

                    if (IsPowerBiBoreyHeaderLikeRow(row, mapped.Select(m => m.Key)))
                    {
                        continue;
                    }

                    DataRow output = fact.NewRow();
                    output["snapshot_utc"] = snapshotUtc.ToString("o", CultureInfo.InvariantCulture);
                    output["source_table"] = logicalTableName ?? "";
                    output["house_key"] = ResolvePowerBiHouseKey(row);
                    foreach (KeyValuePair<DataColumn, string> map in mapped)
                    {
                        object value = row[map.Key];
                        string rawText = value == null || value == DBNull.Value
                            ? ""
                            : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                        output[map.Value] = NormalizePowerBiBoreyFactCell(map.Value, rawText);
                    }

                    fact.Rows.Add(output);
                }
            }

            return fact;
        }

        private static DataTable SanitizePowerBiBoreySourceTable(DataTable source)
        {
            if (source == null)
            {
                return null;
            }

            DataTable cleaned = source.Clone();
            List<DataColumn> columns = source.Columns.Cast<DataColumn>().ToList();
            foreach (DataRow row in source.Rows)
            {
                if (row == null || row.RowState == DataRowState.Deleted)
                {
                    continue;
                }

                if (IsPowerBiBoreyHeaderLikeRow(row, columns))
                {
                    continue;
                }

                DataRow newRow = cleaned.NewRow();
                foreach (DataColumn col in columns)
                {
                    object value = row[col];
                    if (value == null || value == DBNull.Value)
                    {
                        newRow[col.ColumnName] = DBNull.Value;
                    }
                    else if (value is string text)
                    {
                        newRow[col.ColumnName] = text.Trim();
                    }
                    else
                    {
                        newRow[col.ColumnName] = value;
                    }
                }

                cleaned.Rows.Add(newRow);
            }

            return cleaned;
        }

        private static bool IsPowerBiBoreyHeaderLikeRow(DataRow row, IEnumerable<DataColumn> columns)
        {
            if (row == null)
            {
                return true;
            }

            List<DataColumn> sourceColumns = (columns ?? Enumerable.Empty<DataColumn>()).ToList();
            if (sourceColumns.Count == 0)
            {
                return true;
            }

            int nonEmpty = 0;
            int headerMatches = 0;
            foreach (DataColumn col in sourceColumns)
            {
                if (col == null)
                {
                    continue;
                }

                object value = row[col];
                string text = value == null || value == DBNull.Value
                    ? ""
                    : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                nonEmpty++;
                string normalizedValue = NormalizePowerBiName(text);
                string normalizedColumn = NormalizePowerBiName(col.ColumnName);
                if (!string.IsNullOrWhiteSpace(normalizedValue) &&
                    !string.IsNullOrWhiteSpace(normalizedColumn) &&
                    string.Equals(normalizedValue, normalizedColumn, StringComparison.OrdinalIgnoreCase))
                {
                    headerMatches++;
                }
            }

            if (nonEmpty == 0)
            {
                return true;
            }

            int minMatchThreshold = Math.Max(3, (int)Math.Ceiling(sourceColumns.Count * 0.4));
            return headerMatches >= minMatchThreshold;
        }

        private static string NormalizePowerBiBoreyFactCell(string normalizedColumnName, string rawText)
        {
            string column = NormalizePowerBiName(normalizedColumnName ?? "");
            string value = (rawText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(column))
            {
                string normalizedValue = NormalizePowerBiName(value);
                if (!string.IsNullOrWhiteSpace(normalizedValue) &&
                    string.Equals(normalizedValue, column, StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }
            }

            if (PowerBiBoreyDateColumns.Contains(column))
            {
                if (TryParsePowerBiDate(value, out DateTime parsedDate) ||
                    DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedDate) ||
                    DateTime.TryParse(value, out parsedDate))
                {
                    return parsedDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                return "";
            }

            if (PowerBiBoreyIntegerColumns.Contains(column))
            {
                if (TryParsePowerBiNumeric(value, out double parsedInt))
                {
                    long rounded = (long)Math.Round(parsedInt, MidpointRounding.AwayFromZero);
                    return rounded.ToString(CultureInfo.InvariantCulture);
                }

                return "";
            }

            if (PowerBiBoreyNumberColumns.Contains(column))
            {
                if (TryParsePowerBiNumeric(value, out double parsedNumber))
                {
                    return parsedNumber.ToString("0.############################", CultureInfo.InvariantCulture);
                }

                return "";
            }

            return value;
        }

        private System.Windows.Controls.ComboBox GetBoreyNamedCombo(string name)
        {
            return FindName(name) as System.Windows.Controls.ComboBox;
        }

        private DataGrid GetBoreyNamedGrid(string name)
        {
            return FindName(name) as DataGrid;
        }

        private TextBlock GetBoreyNamedTextBlock(string name)
        {
            return FindName(name) as TextBlock;
        }

        private OxyPlot.Wpf.PlotView GetBoreyNamedPlotView(string name)
        {
            return FindName(name) as OxyPlot.Wpf.PlotView;
        }

        private ContextMenu GetBoreyColumnPickerContextMenu()
        {
            return BoreyColumnPickerButton?.ContextMenu as ContextMenu;
        }

        private static string NormalizeBoreyColumnKey(string key)
        {
            string text = (key ?? "").Trim();
            if (text.Length > 2 && text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            return text;
        }

        private HashSet<string> GetBoreyHiddenColumnSet(string gridName, bool createIfMissing)
        {
            string name = (gridName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (_boreyHiddenColumnKeysByGrid.TryGetValue(name, out HashSet<string> existing))
            {
                return existing;
            }

            if (!createIfMissing)
            {
                return null;
            }

            var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _boreyHiddenColumnKeysByGrid[name] = created;
            return created;
        }

        private HashSet<string> GetBoreyExplicitVisibleColumnSet(string gridName, bool createIfMissing)
        {
            string name = (gridName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (_boreyExplicitVisibleColumnKeysByGrid.TryGetValue(name, out HashSet<string> existing))
            {
                return existing;
            }

            if (!createIfMissing)
            {
                return null;
            }

            var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _boreyExplicitVisibleColumnKeysByGrid[name] = created;
            return created;
        }

        private bool IsBoreyColumnHidden(string gridName, string columnKey)
        {
            string key = NormalizeBoreyColumnKey(columnKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            HashSet<string> hidden = GetBoreyHiddenColumnSet(gridName, createIfMissing: false);
            return hidden != null && hidden.Contains(key);
        }

        private static bool IsBoreyRawFloorGrid(string gridName)
        {
            return
                string.Equals(gridName, "SaleInfoDataGrid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gridName, "SiteInfoDataGrid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gridName, "PlaningInfoDataGrid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gridName, "HandoverDataGrid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gridName, "SubcontractorDataGrid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gridName, "ContractLoaBlcDataGrid", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> GetBoreyDefaultVisibleColumnKeysForGrid(DataGrid grid)
        {
            if (grid == null || !IsBoreyRawFloorGrid(grid.Name))
            {
                return null;
            }

            string logicalTable = "";
            if (string.Equals(grid.Name, "SaleInfoDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                logicalTable = "Sale_Info";
            }
            else if (string.Equals(grid.Name, "SiteInfoDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                logicalTable = "Site_Info";
            }
            else if (string.Equals(grid.Name, "PlaningInfoDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                logicalTable = "Planing_Info";
            }
            else if (string.Equals(grid.Name, "HandoverDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                logicalTable = "Planing_Info";
            }
            else if (string.Equals(grid.Name, "SubcontractorDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                logicalTable = "Project_Info";
            }
            else if (string.Equals(grid.Name, "ContractLoaBlcDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                logicalTable = "Project_Info";
            }

            if (string.IsNullOrWhiteSpace(logicalTable))
            {
                return null;
            }

            var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string column in CBIM_BOREY.GetLogicalColumns(logicalTable))
            {
                string normalized = NormalizeBoreyColumnKey(column);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    defaults.Add(normalized);
                }
            }

            // SaleInfo default visibility excludes these by request.
            if (string.Equals(grid.Name, "SaleInfoDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                defaults.Remove("Name");
                defaults.Remove("Description");
                defaults.Remove("House No(Sell)");
                defaults.Remove("House No (Sell)");
            }
            else if (string.Equals(grid.Name, "SubcontractorDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                defaults.Clear();

                string[] subBaseColumns =
                {
                    "Code_Item",
                    "ZONE",
                    "BLOCK",
                    "SUB-BLOCK",
                    "HOUSE-TYPE",
                    "House Code",
                    "House-Units",
                    "HANDOVERED STATUS"
                };

                bool IsSubcontractorItemColumn(string candidate)
                {
                    string text = (candidate ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }

                    if (TryParseSubcontractorItemOrder(text, out _, out _))
                    {
                        return true;
                    }

                    return char.IsDigit(text[0]);
                }

                foreach (DataGridColumn column in grid.Columns.Cast<DataGridColumn>())
                {
                    if (column == null)
                    {
                        continue;
                    }

                    string headerText = GetBoreyGridColumnHeaderText(column);
                    string key = GetBoreyGridColumnKey(grid, column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        key = NormalizeBoreyColumnKey(headerText);
                    }
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    bool isBaseColumn = subBaseColumns.Any(logical =>
                        IsBoreyColumnMatchHeader(logical, headerText) ||
                        IsBoreyColumnMatchHeader(logical, key));
                    if (isBaseColumn || IsSubcontractorItemColumn(headerText) || IsSubcontractorItemColumn(key))
                    {
                        defaults.Add(key);
                    }
                }

                if (defaults.Count == 0)
                {
                    foreach (string column in subBaseColumns)
                    {
                        string normalized = NormalizeBoreyColumnKey(column);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            defaults.Add(normalized);
                        }
                    }
                }
            }
            else if (string.Equals(grid.Name, "ContractLoaBlcDataGrid", StringComparison.OrdinalIgnoreCase))
            {
                defaults.Clear();

                string[] contractBaseColumns =
                {
                    "Code_Item",
                    "ZONE",
                    "BLOCK",
                    "SUB-BLOCK",
                    "HOUSE-TYPE",
                    "House Code",
                    "House-Units",
                    "HANDOVERED STATUS"
                };

                bool IsContractItemColumn(string candidate)
                {
                    string text = (candidate ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }

                    if (TryParseContractItemOrder(text, out _, out _))
                    {
                        return true;
                    }

                    return text.Length >= 2 && char.IsLetter(text[0]) && char.IsDigit(text[1]);
                }

                foreach (DataGridColumn column in grid.Columns.Cast<DataGridColumn>())
                {
                    if (column == null)
                    {
                        continue;
                    }

                    string headerText = GetBoreyGridColumnHeaderText(column);
                    string key = GetBoreyGridColumnKey(grid, column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        key = NormalizeBoreyColumnKey(headerText);
                    }
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    bool isBaseColumn = contractBaseColumns.Any(logical =>
                        IsBoreyColumnMatchHeader(logical, headerText) ||
                        IsBoreyColumnMatchHeader(logical, key));
                    if (isBaseColumn || IsContractItemColumn(headerText) || IsContractItemColumn(key))
                    {
                        defaults.Add(key);
                    }
                }

                if (defaults.Count == 0)
                {
                    foreach (string column in contractBaseColumns)
                    {
                        string normalized = NormalizeBoreyColumnKey(column);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            defaults.Add(normalized);
                        }
                    }
                }
            }

            return defaults;
        }

        private static string GetBoreyGridColumnHeaderText(DataGridColumn column)
        {
            string text = Convert.ToString(column?.Header, CultureInfo.InvariantCulture)?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            text = Convert.ToString(column?.SortMemberPath, CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(text) ? "(Unnamed Column)" : text;
        }

        private static string GetBoreyGridColumnKey(DataGrid grid, DataGridColumn column)
        {
            if (column == null)
            {
                return "";
            }

            string key = Convert.ToString(column.SortMemberPath, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(key) && grid?.ItemsSource is DataView view && view.Table != null)
            {
                key = ResolveBoreyGridColumnName(view.Table, column);
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                key = Convert.ToString(column.Header, CultureInfo.InvariantCulture);
            }

            return NormalizeBoreyColumnKey(key);
        }

        private string GetBoreyGridDisplayName(DataGrid grid)
        {
            if (grid == null)
            {
                return "";
            }

            switch (grid.Name)
            {
                case "ProjectInfoDataGrid":
                    return "ProjectInfo";
                case "ProjectStatusDataGrid":
                    return "Project Status";
                case "SaleInfoDataGrid":
                    return "SaleInfo";
                case "SiteInfoDataGrid":
                    return "SiteInfo";
                case "PlaningInfoDataGrid":
                    return "Monthly Plan";
                case "HandoverDataGrid":
                    return "HANDOVER";
                case "PlanVsActualPlanDataGrid":
                    return "Plan Vs Actual - Monthly Plan";
                case "PlanVsActualActualDataGrid":
                    return "Plan Vs Actual - Actual";
                case "PlanVsActualRemainingDataGrid":
                    return "Plan Vs Actual - Remaining";
                case "SubcontractorDataGrid":
                    return "SUBCONTRACTOR";
                case "ContractLoaBlcDataGrid":
                    return "CONTRACT/LOA/BLC No.";
                default:
                    return grid.Name;
            }
        }

        private IEnumerable<DataGrid> GetBoreyTargetGridsForCurrentSubTab()
        {
            if (!(BoreySubTabControl?.SelectedItem is TabItem tab))
            {
                yield break;
            }

            string header = tab.Header?.ToString() ?? "";
            switch (header)
            {
                case "ProjectInfo":
                    if (ProjectInfoDataGrid != null) yield return ProjectInfoDataGrid;
                    break;
                case "ProjectStatus":
                case "Project Status":
                    {
                        DataGrid status = GetBoreyNamedGrid("ProjectStatusDataGrid");
                        if (status != null) yield return status;
                        break;
                    }
                case "SaleInfo":
                    if (SaleInfoDataGrid != null) yield return SaleInfoDataGrid;
                    break;
                case "SiteInfo":
                    if (SiteInfoDataGrid != null) yield return SiteInfoDataGrid;
                    break;
                case "Monthly Plan":
                    if (PlaningInfoDataGrid != null) yield return PlaningInfoDataGrid;
                    break;
                case "HANDOVER":
                    if (HandoverDataGrid != null) yield return HandoverDataGrid;
                    break;
                case "PLAN VS ACTUAL TO REMAINNING":
                case "PLAN VS ACTUAL TO REMAINING":
                    {
                        DataGrid plan = GetBoreyNamedGrid("PlanVsActualPlanDataGrid");
                        DataGrid actual = GetBoreyNamedGrid("PlanVsActualActualDataGrid");
                        DataGrid remaining = GetBoreyNamedGrid("PlanVsActualRemainingDataGrid");
                        if (plan != null) yield return plan;
                        if (actual != null) yield return actual;
                        if (remaining != null) yield return remaining;
                        break;
                    }
                case "SUBCONTRACTOR":
                    {
                        DataGrid subcontractor = GetBoreyNamedGrid("SubcontractorDataGrid");
                        if (subcontractor != null) yield return subcontractor;
                        break;
                    }
                case "CONTRACT/LOA/BLC No.":
                case "CONTRACT/LOA/BLC No":
                case "CONTRACT/LOA/BLC NO.":
                case "CONTRACT/LOA/BLC NO":
                case "Contract/LOA/BLC No.":
                case "Contract/LOA/BLC No":
                    {
                        DataGrid contract = GetBoreyNamedGrid("ContractLoaBlcDataGrid");
                        if (contract != null) yield return contract;
                        break;
                    }
            }
        }

        private void ApplyBoreyColumnVisibilityPreferences(DataGrid grid)
        {
            if (grid == null)
            {
                return;
            }

            Action apply = () =>
            {
                HashSet<string> hidden = GetBoreyHiddenColumnSet(grid.Name, createIfMissing: true);
                HashSet<string> explicitVisible = GetBoreyExplicitVisibleColumnSet(grid.Name, createIfMissing: true);
                HashSet<string> defaultVisible = GetBoreyDefaultVisibleColumnKeysForGrid(grid);

                bool hasDefaultRule = defaultVisible != null && defaultVisible.Count > 0;
                if (hasDefaultRule)
                {
                    // Keep tab defaults checked, and hide extra shared-parameter columns until user explicitly checks them.
                    foreach (DataGridColumn column in grid.Columns)
                    {
                        if (column == null)
                        {
                            continue;
                        }

                        string key = GetBoreyGridColumnKey(grid, column);
                        if (string.IsNullOrWhiteSpace(key) || defaultVisible.Contains(key))
                        {
                            continue;
                        }

                        if (explicitVisible != null && explicitVisible.Contains(key))
                        {
                            hidden.Remove(key);
                        }
                        else
                        {
                            hidden.Add(key);
                        }
                    }
                }

                if (hidden == null || hidden.Count == 0)
                {
                    foreach (DataGridColumn column in grid.Columns)
                    {
                        if (column != null)
                        {
                            column.Visibility = System.Windows.Visibility.Visible;
                        }
                    }

                    return;
                }

                foreach (DataGridColumn column in grid.Columns)
                {
                    if (column == null)
                    {
                        continue;
                    }

                    string key = GetBoreyGridColumnKey(grid, column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    column.Visibility = hidden.Contains(key) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                }
            };

            if (grid.Columns != null && grid.Columns.Count > 0)
            {
                apply();
            }
            else
            {
                grid.Dispatcher.BeginInvoke(apply, DispatcherPriority.Loaded);
            }
        }

        private void RefreshBoreyColumnPickerMenu()
        {
            ContextMenu menu = GetBoreyColumnPickerContextMenu();
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();
            if (!IsBoreyTab())
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "Open BOREY tab to customize columns.",
                    IsEnabled = false
                });
                return;
            }

            List<DataGrid> targetGrids = GetBoreyTargetGridsForCurrentSubTab().Where(g => g != null).ToList();
            if (targetGrids.Count == 0)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "No table available.",
                    IsEnabled = false
                });
                return;
            }

            bool useGroupMenus = targetGrids.Count > 1;
            foreach (DataGrid grid in targetGrids)
            {
                ItemCollection destination = menu.Items;
                if (useGroupMenus)
                {
                    var group = new MenuItem
                    {
                        Header = GetBoreyGridDisplayName(grid)
                    };
                    menu.Items.Add(group);
                    destination = group.Items;
                }

                List<DataGridColumn> columns = grid.Columns
                    .Cast<DataGridColumn>()
                    .Where(c => c != null)
                    .OrderBy(c => c.DisplayIndex)
                    .ToList();

                if (columns.Count == 0)
                {
                    destination.Add(new MenuItem
                    {
                        Header = "(No columns yet)",
                        IsEnabled = false
                    });
                    continue;
                }

                foreach (DataGridColumn column in columns)
                {
                    string key = GetBoreyGridColumnKey(grid, column);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var item = new MenuItem
                    {
                        Header = GetBoreyGridColumnHeaderText(column),
                        IsCheckable = true,
                        IsChecked = column.Visibility == System.Windows.Visibility.Visible,
                        StaysOpenOnClick = true,
                        Tag = new BoreyColumnToggleTag
                        {
                            Grid = grid,
                            GridName = grid.Name ?? "",
                            ColumnKey = key
                        }
                    };
                    item.Click += OnBoreyColumnVisibilityMenuItemClick;
                    destination.Add(item);
                }
            }
        }

        private void OnBoreyColumnPickerButtonClick(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = GetBoreyColumnPickerContextMenu();
            if (menu == null)
            {
                return;
            }

            RefreshBoreyColumnPickerMenu();
            menu.PlacementTarget = sender as UIElement ?? BoreyColumnPickerButton;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void OnBoreyColumnPickerMenuOpened(object sender, RoutedEventArgs e)
        {
            RefreshBoreyColumnPickerMenu();
        }

        private void OnBoreyColumnVisibilityMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem item) || !(item.Tag is BoreyColumnToggleTag tag) || tag.Grid == null)
            {
                return;
            }

            string key = NormalizeBoreyColumnKey(tag.ColumnKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            HashSet<string> hidden = GetBoreyHiddenColumnSet(tag.GridName, createIfMissing: true);
            HashSet<string> explicitVisible = GetBoreyExplicitVisibleColumnSet(tag.GridName, createIfMissing: true);
            HashSet<string> defaultVisible = GetBoreyDefaultVisibleColumnKeysForGrid(tag.Grid);
            bool isDefaultColumn = defaultVisible != null && defaultVisible.Contains(key);
            if (item.IsChecked)
            {
                hidden.Remove(key);
                if (!isDefaultColumn)
                {
                    explicitVisible?.Add(key);
                }
                else
                {
                    explicitVisible?.Remove(key);
                }

                if (hidden.Count == 0)
                {
                    _boreyHiddenColumnKeysByGrid.Remove(tag.GridName);
                }
            }
            else
            {
                hidden.Add(key);
                explicitVisible?.Remove(key);
            }

            ApplyBoreyColumnVisibilityPreferences(tag.Grid);
        }

        private void UpdateBoreyBottomCount(string header, int rowCount)
        {
            if (string.Equals(header, "SaleInfo", StringComparison.OrdinalIgnoreCase))
            {
                SetBoreyBottomCountText("SaleInfoCountText", rowCount);
                return;
            }

            if (string.Equals(header, "Monthly Plan", StringComparison.OrdinalIgnoreCase))
            {
                SetBoreyBottomCountText("PlaningInfoCountText", rowCount);
                return;
            }

            if (string.Equals(header, "HANDOVER", StringComparison.OrdinalIgnoreCase))
            {
                SetBoreyBottomCountText("HandoverCountText", rowCount);
            }
        }

        private void SetBoreyBottomCountText(string textBlockName, int rowCount)
        {
            TextBlock textBlock = GetBoreyNamedTextBlock(textBlockName);
            if (textBlock == null)
            {
                return;
            }

            textBlock.Text = "Count: " + Math.Max(0, rowCount).ToString(CultureInfo.InvariantCulture);
        }

        private void ResetBoreyBottomCounts()
        {
            SetBoreyBottomCountText("SaleInfoCountText", 0);
            SetBoreyBottomCountText("PlaningInfoCountText", 0);
            SetBoreyBottomCountText("HandoverCountText", 0);
        }

        private void InitializeBoreyDefaults()
        {
            _boreyTableCache.Clear();
            _boreyRevitTableCache.Clear();
            _boreyHiddenColumnKeysByGrid.Clear();
            _boreyExplicitVisibleColumnKeysByGrid.Clear();
            _boreyUiSyncInProgress = false;
            _boreySelectionSyncInProgress = false;
            _boreyInitialLoadRequested = false;
            _cachedBoreyDbPath = "";
            _cachedBoreyPhysicalTable = "";
            _cachedBoreySchemaPath = "";

            SetComboOptions(ProjectInfoZoneFilterCombo, new List<string>());
            SetComboOptions(ProjectInfoBlockFilterCombo, new List<string>());
            SetComboOptions(ProjectInfoSubBlockFilterCombo, new List<string>());
            SetComboOptions(ProjectInfoHouseTypeFilterCombo, new List<string>());
            SetComboOptions(ProjectInfoHouseCodeFilterCombo, new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ProjectInfoHandoverStatusFilterCombo"), new List<string>());

            SetComboOptions(GetBoreyNamedCombo("ProjectStatusZoneFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ProjectStatusBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ProjectStatusSubBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ProjectStatusHouseTypeFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ProjectStatusHouseCodeFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ProjectStatusHandoverStatusFilterCombo"), new List<string>());

            SetComboOptions(SaleInfoZoneFilterCombo, new List<string>());
            SetComboOptions(SaleInfoBlockFilterCombo, new List<string>());
            SetComboOptions(SaleInfoSubBlockFilterCombo, new List<string>());
            SetComboOptions(SaleInfoHouseTypeFilterCombo, new List<string>());
            SetComboOptions(SaleInfoHouseCodeFilterCombo, new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SaleInfoHandoverStatusFilterCombo"), new List<string>());

            SetComboOptions(SiteInfoZoneFilterCombo, new List<string>());
            SetComboOptions(SiteInfoBlockFilterCombo, new List<string>());
            SetComboOptions(SiteInfoSubBlockFilterCombo, new List<string>());
            SetComboOptions(SiteInfoHouseTypeFilterCombo, new List<string>());
            SetComboOptions(SiteInfoHouseCodeFilterCombo, new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SiteInfoHandoverStatusFilterCombo"), new List<string>());

            SetComboOptions(PlaningInfoZoneFilterCombo, new List<string>());
            SetComboOptions(PlaningInfoBlockFilterCombo, new List<string>());
            SetComboOptions(PlaningInfoSubBlockFilterCombo, new List<string>());
            SetComboOptions(PlaningInfoHouseTypeFilterCombo, new List<string>());
            SetComboOptions(PlaningInfoHouseCodeFilterCombo, new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlaningInfoHandoverStatusFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlaningInfoTocFilterCombo"), new List<string>());

            SetComboOptions(GetBoreyNamedCombo("PlanVsActualZoneFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualSubBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualHouseTypeFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualHouseIdFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualProjectStatusFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualPlanDescriptionFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("PlanVsActualTargetCompleteFilterCombo"), new List<string>());

            SetComboOptions(GetBoreyNamedCombo("SubcontractorZoneFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SubcontractorBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SubcontractorSubBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SubcontractorHouseTypeFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SubcontractorHouseIdFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SubcontractorNameFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("SubcontractorHandoverStatusFilterCombo"), new List<string>());

            SetComboOptions(GetBoreyNamedCombo("ContractLoaBlcZoneFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ContractLoaBlcBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ContractLoaBlcSubBlockFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ContractLoaBlcHouseTypeFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ContractLoaBlcHouseIdFilterCombo"), new List<string>());
            SetComboOptions(GetBoreyNamedCombo("ContractLoaBlcHandoverStatusFilterCombo"), new List<string>());

            SetComboOptions(HandoverZoneFilterCombo, new List<string>());
            SetComboOptions(HandoverBlockFilterCombo, new List<string>());
            SetComboOptions(HandoverSubBlockFilterCombo, new List<string>());
            SetComboOptions(HandoverHouseTypeFilterCombo, new List<string>());
            SetComboOptions(HandoverHouseIdFilterCombo, new List<string>());
            SetComboOptions(HandoverStatusFilterCombo, new List<string>());
            ResetBoreyProjectOverallDashboard();
            ResetHandoverColumnChart();
            ResetBoreyBottomCounts();
        }

        private void InitializeBoreyUiEvents()
        {
            if (MainTabControl != null)
            {
                MainTabControl.SelectionChanged -= OnMainTabSelectionChanged;
                MainTabControl.SelectionChanged += OnMainTabSelectionChanged;
            }

            if (BoreySubTabControl != null)
            {
                BoreySubTabControl.SelectionChanged -= OnBoreySubTabSelectionChanged;
                BoreySubTabControl.SelectionChanged += OnBoreySubTabSelectionChanged;
            }

            foreach (System.Windows.Controls.ComboBox combo in GetBoreyFilterCombos())
            {
                if (combo == null)
                {
                    continue;
                }

                combo.SelectionChanged -= OnBoreyFilterChanged;
                combo.SelectionChanged += OnBoreyFilterChanged;
            }

            foreach (DataGrid grid in GetBoreyDataGrids())
            {
                if (grid == null)
                {
                    continue;
                }

                grid.AutoGeneratingColumn -= OnBoreyGridAutoGeneratingColumn;
                grid.AutoGeneratingColumn += OnBoreyGridAutoGeneratingColumn;
                grid.SelectionChanged -= OnBoreyGridSelectionChanged;
                grid.SelectionChanged += OnBoreyGridSelectionChanged;
            }
        }

        private IEnumerable<System.Windows.Controls.ComboBox> GetBoreyFilterCombos()
        {
            yield return ProjectInfoZoneFilterCombo;
            yield return ProjectInfoBlockFilterCombo;
            yield return ProjectInfoSubBlockFilterCombo;
            yield return ProjectInfoHouseTypeFilterCombo;
            yield return ProjectInfoHouseCodeFilterCombo;
            yield return GetBoreyNamedCombo("ProjectInfoHandoverStatusFilterCombo");
            yield return GetBoreyNamedCombo("ProjectStatusZoneFilterCombo");
            yield return GetBoreyNamedCombo("ProjectStatusBlockFilterCombo");
            yield return GetBoreyNamedCombo("ProjectStatusSubBlockFilterCombo");
            yield return GetBoreyNamedCombo("ProjectStatusHouseTypeFilterCombo");
            yield return GetBoreyNamedCombo("ProjectStatusHouseCodeFilterCombo");
            yield return GetBoreyNamedCombo("ProjectStatusHandoverStatusFilterCombo");
            yield return SaleInfoZoneFilterCombo;
            yield return SaleInfoBlockFilterCombo;
            yield return SaleInfoSubBlockFilterCombo;
            yield return SaleInfoHouseTypeFilterCombo;
            yield return SaleInfoHouseCodeFilterCombo;
            yield return GetBoreyNamedCombo("SaleInfoHandoverStatusFilterCombo");
            yield return SiteInfoZoneFilterCombo;
            yield return SiteInfoBlockFilterCombo;
            yield return SiteInfoSubBlockFilterCombo;
            yield return SiteInfoHouseTypeFilterCombo;
            yield return SiteInfoHouseCodeFilterCombo;
            yield return GetBoreyNamedCombo("SiteInfoHandoverStatusFilterCombo");
            yield return PlaningInfoZoneFilterCombo;
            yield return PlaningInfoBlockFilterCombo;
            yield return PlaningInfoSubBlockFilterCombo;
            yield return PlaningInfoHouseTypeFilterCombo;
            yield return PlaningInfoHouseCodeFilterCombo;
            yield return GetBoreyNamedCombo("PlaningInfoHandoverStatusFilterCombo");
            yield return GetBoreyNamedCombo("PlaningInfoTocFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualZoneFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualBlockFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualSubBlockFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualHouseTypeFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualHouseIdFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualProjectStatusFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualPlanDescriptionFilterCombo");
            yield return GetBoreyNamedCombo("PlanVsActualTargetCompleteFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorZoneFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorBlockFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorSubBlockFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorHouseTypeFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorHouseIdFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorNameFilterCombo");
            yield return GetBoreyNamedCombo("SubcontractorHandoverStatusFilterCombo");
            yield return GetBoreyNamedCombo("ContractLoaBlcZoneFilterCombo");
            yield return GetBoreyNamedCombo("ContractLoaBlcBlockFilterCombo");
            yield return GetBoreyNamedCombo("ContractLoaBlcSubBlockFilterCombo");
            yield return GetBoreyNamedCombo("ContractLoaBlcHouseTypeFilterCombo");
            yield return GetBoreyNamedCombo("ContractLoaBlcHouseIdFilterCombo");
            yield return GetBoreyNamedCombo("ContractLoaBlcHandoverStatusFilterCombo");
            yield return HandoverZoneFilterCombo;
            yield return HandoverBlockFilterCombo;
            yield return HandoverSubBlockFilterCombo;
            yield return HandoverHouseTypeFilterCombo;
            yield return HandoverHouseIdFilterCombo;
            yield return HandoverStatusFilterCombo;
        }

        private IEnumerable<DataGrid> GetBoreyDataGrids()
        {
            yield return ProjectInfoDataGrid;
            yield return GetBoreyNamedGrid("ProjectStatusDataGrid");
            yield return SaleInfoDataGrid;
            yield return SiteInfoDataGrid;
            yield return PlaningInfoDataGrid;
            yield return GetBoreyNamedGrid("PlanVsActualPlanDataGrid");
            yield return GetBoreyNamedGrid("PlanVsActualActualDataGrid");
            yield return GetBoreyNamedGrid("PlanVsActualRemainingDataGrid");
            yield return GetBoreyNamedGrid("SubcontractorDataGrid");
            yield return GetBoreyNamedGrid("ContractLoaBlcDataGrid");
            yield return HandoverDataGrid;
        }

        private void GenerateBoreyData()
        {
            if (!(BoreySubTabControl?.SelectedItem is TabItem tab))
            {
                ShowStatus("BOREY: select a sub-tab first.");
                return;
            }

            string header = tab.Header?.ToString() ?? "";
            if (!TryGetBoreySubTabContext(
                header,
                out string logicalTable,
                out _,
                out _,
                out _,
                out _,
                out _,
                out DataGrid grid))
            {
                ShowStatus("BOREY: unknown sub-tab.");
                return;
            }

            List<BoreyGridRowPayload> changedRows = BuildChangedBoreyGridRows(grid);
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            _handler.Request.Borey.SharedParameterFilePath = SharedParameterPathResolver.ResolveDefaultSharedParameterPath(baseDir);
            _handler.Request.Borey.SharedParameterGroupName = "CBIM-BOREY";
            _handler.Request.Borey.EnsureSharedParameters = true;
            _handler.Request.Borey.LogicalTableName = logicalTable;
            _handler.Request.Borey.Rows = changedRows;
            _handler.Request.Borey.ApplyGridChanges = changedRows.Count > 0;
            _handler.Request.RequestType = CadToModelRequestType.SyncBoreyFloorData;

            if (changedRows.Count > 0)
            {
                ShowStatus($"BOREY: applying {changedRows.Count} edited row(s) to Floors and refreshing...");
            }
            else
            {
                ShowStatus("BOREY: syncing floor data and verifying shared parameters...");
            }

            _boreyInitialLoadRequested = true;
            QueueExternalRequest();
        }

        private bool TryGetBoreySubTabContext(
            string header,
            out string logicalTable,
            out System.Windows.Controls.ComboBox zoneCombo,
            out System.Windows.Controls.ComboBox blockCombo,
            out System.Windows.Controls.ComboBox subBlockCombo,
            out System.Windows.Controls.ComboBox houseTypeCombo,
            out System.Windows.Controls.ComboBox houseCodeCombo,
            out DataGrid grid)
        {
            logicalTable = "";
            zoneCombo = null;
            blockCombo = null;
            subBlockCombo = null;
            houseTypeCombo = null;
            houseCodeCombo = null;
            grid = null;

            switch (header)
            {
                case "ProjectInfo":
                    logicalTable = "Project_Info";
                    zoneCombo = ProjectInfoZoneFilterCombo;
                    blockCombo = ProjectInfoBlockFilterCombo;
                    subBlockCombo = ProjectInfoSubBlockFilterCombo;
                    houseTypeCombo = ProjectInfoHouseTypeFilterCombo;
                    houseCodeCombo = ProjectInfoHouseCodeFilterCombo;
                    grid = ProjectInfoDataGrid;
                    return true;
                case "ProjectStatus":
                case "Project Status":
                    logicalTable = "Project_Info";
                    zoneCombo = GetBoreyNamedCombo("ProjectStatusZoneFilterCombo");
                    blockCombo = GetBoreyNamedCombo("ProjectStatusBlockFilterCombo");
                    subBlockCombo = GetBoreyNamedCombo("ProjectStatusSubBlockFilterCombo");
                    houseTypeCombo = GetBoreyNamedCombo("ProjectStatusHouseTypeFilterCombo");
                    houseCodeCombo = GetBoreyNamedCombo("ProjectStatusHouseCodeFilterCombo");
                    grid = GetBoreyNamedGrid("ProjectStatusDataGrid");
                    return true;
                case "SaleInfo":
                    logicalTable = "Sale_Info";
                    zoneCombo = SaleInfoZoneFilterCombo;
                    blockCombo = SaleInfoBlockFilterCombo;
                    subBlockCombo = SaleInfoSubBlockFilterCombo;
                    houseTypeCombo = SaleInfoHouseTypeFilterCombo;
                    houseCodeCombo = SaleInfoHouseCodeFilterCombo;
                    grid = SaleInfoDataGrid;
                    return true;
                case "SiteInfo":
                    logicalTable = "Site_Info";
                    zoneCombo = SiteInfoZoneFilterCombo;
                    blockCombo = SiteInfoBlockFilterCombo;
                    subBlockCombo = SiteInfoSubBlockFilterCombo;
                    houseTypeCombo = SiteInfoHouseTypeFilterCombo;
                    houseCodeCombo = SiteInfoHouseCodeFilterCombo;
                    grid = SiteInfoDataGrid;
                    return true;
                case "Monthly Plan":
                case "PlaningInfo":
                    logicalTable = "Planing_Info";
                    zoneCombo = PlaningInfoZoneFilterCombo;
                    blockCombo = PlaningInfoBlockFilterCombo;
                    subBlockCombo = PlaningInfoSubBlockFilterCombo;
                    houseTypeCombo = PlaningInfoHouseTypeFilterCombo;
                    houseCodeCombo = PlaningInfoHouseCodeFilterCombo;
                    grid = PlaningInfoDataGrid;
                    return true;
                case "HANDOVER":
                    logicalTable = "Planing_Info";
                    zoneCombo = HandoverZoneFilterCombo;
                    blockCombo = HandoverBlockFilterCombo;
                    subBlockCombo = HandoverSubBlockFilterCombo;
                    houseTypeCombo = HandoverHouseTypeFilterCombo;
                    houseCodeCombo = HandoverHouseIdFilterCombo;
                    grid = HandoverDataGrid;
                    return true;
                case "PLAN VS ACTUAL TO REMAINNING":
                case "PLAN VS ACTUAL TO REMAINING":
                case "Plan Vs Actual To Remainning":
                    logicalTable = "Planing_Info";
                    zoneCombo = GetBoreyNamedCombo("PlanVsActualZoneFilterCombo");
                    blockCombo = GetBoreyNamedCombo("PlanVsActualBlockFilterCombo");
                    subBlockCombo = GetBoreyNamedCombo("PlanVsActualSubBlockFilterCombo");
                    houseTypeCombo = GetBoreyNamedCombo("PlanVsActualHouseTypeFilterCombo");
                    houseCodeCombo = GetBoreyNamedCombo("PlanVsActualHouseIdFilterCombo");
                    grid = GetBoreyNamedGrid("PlanVsActualPlanDataGrid");
                    return true;
                case "SUBCONTRACTOR":
                    logicalTable = "Project_Info";
                    zoneCombo = GetBoreyNamedCombo("SubcontractorZoneFilterCombo");
                    blockCombo = GetBoreyNamedCombo("SubcontractorBlockFilterCombo");
                    subBlockCombo = GetBoreyNamedCombo("SubcontractorSubBlockFilterCombo");
                    houseTypeCombo = GetBoreyNamedCombo("SubcontractorHouseTypeFilterCombo");
                    houseCodeCombo = GetBoreyNamedCombo("SubcontractorHouseIdFilterCombo");
                    grid = GetBoreyNamedGrid("SubcontractorDataGrid");
                    return true;
                case "CONTRACT/LOA/BLC No.":
                case "CONTRACT/LOA/BLC No":
                case "CONTRACT/LOA/BLC NO.":
                case "CONTRACT/LOA/BLC NO":
                case "Contract/LOA/BLC No.":
                case "Contract/LOA/BLC No":
                    logicalTable = "Project_Info";
                    zoneCombo = GetBoreyNamedCombo("ContractLoaBlcZoneFilterCombo");
                    blockCombo = GetBoreyNamedCombo("ContractLoaBlcBlockFilterCombo");
                    subBlockCombo = GetBoreyNamedCombo("ContractLoaBlcSubBlockFilterCombo");
                    houseTypeCombo = GetBoreyNamedCombo("ContractLoaBlcHouseTypeFilterCombo");
                    houseCodeCombo = GetBoreyNamedCombo("ContractLoaBlcHouseIdFilterCombo");
                    grid = GetBoreyNamedGrid("ContractLoaBlcDataGrid");
                    return true;
                default:
                    return false;
            }
        }

        private static List<BoreyGridRowPayload> BuildChangedBoreyGridRows(DataGrid grid)
        {
            var payloads = new List<BoreyGridRowPayload>();
            if (grid == null)
            {
                return payloads;
            }

            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);

            if (!(grid.ItemsSource is DataView view) || view.Table == null)
            {
                return payloads;
            }

            foreach (DataRow row in view.Table.Rows)
            {
                if (row == null ||
                    (row.RowState != DataRowState.Added && row.RowState != DataRowState.Modified))
                {
                    continue;
                }

                var payload = new BoreyGridRowPayload();
                foreach (DataColumn col in view.Table.Columns)
                {
                    if (col == null)
                    {
                        continue;
                    }

                    object raw = row[col];
                    if (raw == null || raw == DBNull.Value)
                    {
                        continue;
                    }

                    string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    payload.Values[col.ColumnName] = text;
                }

                if (payload.Values.Count > 0)
                {
                    payloads.Add(payload);
                }
            }

            return payloads;
        }

        private static List<BoreyGridRowPayload> BuildSelectedBoreyGridRows(DataGrid grid)
        {
            var payloads = new List<BoreyGridRowPayload>();
            if (grid == null)
            {
                return payloads;
            }

            List<DataRowView> selectedRows = grid.SelectedItems
                .OfType<DataRowView>()
                .Where(r => r?.Row != null)
                .ToList();

            if (selectedRows.Count == 0 && grid.SelectedCells != null && grid.SelectedCells.Count > 0)
            {
                var seen = new HashSet<DataRow>();
                foreach (DataGridCellInfo cell in grid.SelectedCells)
                {
                    if (!(cell.Item is DataRowView cellRowView) || cellRowView.Row == null)
                    {
                        continue;
                    }

                    if (seen.Add(cellRowView.Row))
                    {
                        selectedRows.Add(cellRowView);
                    }
                }
            }

            if (selectedRows.Count == 0 && grid.SelectedItem is DataRowView singleSelected && singleSelected.Row != null)
            {
                selectedRows.Add(singleSelected);
            }

            foreach (DataRowView rowView in selectedRows)
            {
                if (rowView?.Row == null || rowView.Row.Table == null)
                {
                    continue;
                }

                var payload = new BoreyGridRowPayload();
                foreach (DataColumn col in rowView.Row.Table.Columns)
                {
                    if (col == null)
                    {
                        continue;
                    }

                    object raw = rowView.Row[col];
                    if (raw == null || raw == DBNull.Value)
                    {
                        continue;
                    }

                    string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    payload.Values[col.ColumnName] = text;
                }

                if (payload.Values.Count > 0)
                {
                    payloads.Add(payload);
                }
            }

            return payloads;
        }

        internal void UpdateBoreyTablesFromRevit(Dictionary<string, DataTable> tables, string message)
        {
            void apply()
            {
                _boreyInitialLoadRequested = true;
                if (tables != null)
                {
                    foreach (KeyValuePair<string, DataTable> entry in tables)
                    {
                        string logicalName = entry.Key ?? "";
                        if (string.IsNullOrWhiteSpace(logicalName))
                        {
                            continue;
                        }

                        _boreyRevitTableCache[logicalName] = entry.Value?.Copy() ?? CreateEmptyBoreyTable(logicalName);
                    }
                }

                ApplyCurrentBoreyRevitView(refreshFilterOptions: true);
            }

            if (Dispatcher.CheckAccess())
            {
                apply();
            }
            else
            {
                Dispatcher.Invoke(apply);
            }

            ShowStatus(message);
        }

        private bool TryGetCurrentBoreyLogicalTable(out string logicalTable)
        {
            logicalTable = "";
            if (!(BoreySubTabControl?.SelectedItem is TabItem tab))
            {
                return false;
            }

            return TryGetBoreySubTabContext(
                tab.Header?.ToString() ?? "",
                out logicalTable,
                out System.Windows.Controls.ComboBox _,
                out System.Windows.Controls.ComboBox _,
                out System.Windows.Controls.ComboBox _,
                out System.Windows.Controls.ComboBox _,
                out System.Windows.Controls.ComboBox _,
                out DataGrid _);
        }

        private DataTable CreateEmptyBoreyTable(string logicalTable)
        {
            var table = new DataTable(logicalTable ?? "Project_Info");
            foreach (string column in CBIM_BOREY.GetLogicalColumns(logicalTable ?? "Project_Info"))
            {
                if (!table.Columns.Contains(column))
                {
                    table.Columns.Add(column, typeof(string));
                }
            }

            return table;
        }

        private void ApplyCurrentBoreyRevitView(bool refreshFilterOptions)
        {
            if (!(BoreySubTabControl?.SelectedItem is TabItem tab))
            {
                return;
            }

            string header = tab.Header?.ToString() ?? "";
            if (!TryGetBoreySubTabContext(
                header,
                out string logicalTable,
                out System.Windows.Controls.ComboBox zoneCombo,
                out System.Windows.Controls.ComboBox blockCombo,
                out System.Windows.Controls.ComboBox subBlockCombo,
                out System.Windows.Controls.ComboBox houseTypeCombo,
                out System.Windows.Controls.ComboBox houseCodeCombo,
                out DataGrid grid))
            {
                return;
            }

            if (!_boreyRevitTableCache.TryGetValue(logicalTable, out DataTable source) || source == null)
            {
                source = CreateEmptyBoreyTable(logicalTable);
            }

            bool isProjectInfoTab = string.Equals(header, "ProjectInfo", StringComparison.OrdinalIgnoreCase);
            bool isProjectStatusTab = string.Equals(header, "ProjectStatus", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(header, "Project Status", StringComparison.OrdinalIgnoreCase);
            bool isSaleInfoTab = string.Equals(header, "SaleInfo", StringComparison.OrdinalIgnoreCase);
            bool isSiteInfoTab = string.Equals(header, "SiteInfo", StringComparison.OrdinalIgnoreCase);
            bool isMonthlyPlanTab = string.Equals(header, "Monthly Plan", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(header, "PlaningInfo", StringComparison.OrdinalIgnoreCase);
            bool isHandoverSubTab = string.Equals(header, "HANDOVER", StringComparison.OrdinalIgnoreCase);
            bool isPlanVsActualTab = string.Equals(header, "PLAN VS ACTUAL TO REMAINNING", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(header, "PLAN VS ACTUAL TO REMAINING", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(header, "Plan Vs Actual To Remainning", StringComparison.OrdinalIgnoreCase);
            bool isSubcontractorTab = string.Equals(header, "SUBCONTRACTOR", StringComparison.OrdinalIgnoreCase);
            bool isContractLoaBlcTab = string.Equals(header, "CONTRACT/LOA/BLC No.", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(header, "CONTRACT/LOA/BLC No", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(header, "CONTRACT/LOA/BLC NO.", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(header, "CONTRACT/LOA/BLC NO", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(header, "Contract/LOA/BLC No.", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(header, "Contract/LOA/BLC No", StringComparison.OrdinalIgnoreCase);
            bool isProjectLogicalTable = string.Equals(logicalTable, "Project_Info", StringComparison.OrdinalIgnoreCase);
            string houseFilterLogicalColumn = (isProjectLogicalTable || isPlanVsActualTab || isHandoverSubTab)
                ? "HOUSE ID"
                : "House Code";
            System.Windows.Controls.ComboBox handoverStatusCombo = isProjectInfoTab
                ? GetBoreyNamedCombo("ProjectInfoHandoverStatusFilterCombo")
                : (isProjectStatusTab
                    ? GetBoreyNamedCombo("ProjectStatusHandoverStatusFilterCombo")
                    : (isSaleInfoTab
                        ? GetBoreyNamedCombo("SaleInfoHandoverStatusFilterCombo")
                        : (isSiteInfoTab
                            ? GetBoreyNamedCombo("SiteInfoHandoverStatusFilterCombo")
                            : (isMonthlyPlanTab
                                ? GetBoreyNamedCombo("PlaningInfoHandoverStatusFilterCombo")
                                : (isHandoverSubTab
                                    ? HandoverStatusFilterCombo
                                : (isPlanVsActualTab
                                    ? GetBoreyNamedCombo("PlanVsActualProjectStatusFilterCombo")
                                    : (isSubcontractorTab
                                        ? GetBoreyNamedCombo("SubcontractorHandoverStatusFilterCombo")
                                        : (isContractLoaBlcTab ? GetBoreyNamedCombo("ContractLoaBlcHandoverStatusFilterCombo") : null))))))));
            System.Windows.Controls.ComboBox planDescriptionCombo = isPlanVsActualTab
                ? GetBoreyNamedCombo("PlanVsActualPlanDescriptionFilterCombo")
                : null;
            System.Windows.Controls.ComboBox tocCombo = isPlanVsActualTab
                ? GetBoreyNamedCombo("PlanVsActualTargetCompleteFilterCombo")
                : (isMonthlyPlanTab
                    ? GetBoreyNamedCombo("PlaningInfoTocFilterCombo")
                    : null);
            System.Windows.Controls.ComboBox subcontractorCombo = isSubcontractorTab
                ? GetBoreyNamedCombo("SubcontractorNameFilterCombo")
                : null;

            if (refreshFilterOptions)
            {
                _boreyUiSyncInProgress = true;
                try
                {
                    RefreshBoreyFilterOptionsFromData(
                        source,
                        zoneCombo,
                        blockCombo,
                        subBlockCombo,
                        houseTypeCombo,
                        houseCodeCombo,
                        tocCombo: tocCombo,
                        houseCodeLogicalColumn: houseFilterLogicalColumn,
                        handoverStatusCombo: handoverStatusCombo,
                        handoverStatusLogicalColumn: "HANDOVERED STATUS",
                        planDescriptionCombo: planDescriptionCombo,
                        planDescriptionLogicalColumn: "Plan Description",
                        subcontractorCombo: subcontractorCombo,
                        subcontractorLogicalColumn: "SUBCONTRACTOR");
                }
                finally
                {
                    _boreyUiSyncInProgress = false;
                }
            }

            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ZONE", GetSelectedFilterValue(zoneCombo) },
                { "BLOCK", GetSelectedFilterValue(blockCombo) },
                { "SUB-BLOCK", GetSelectedFilterValue(subBlockCombo) },
                { "HOUSE-TYPE", GetSelectedFilterValue(houseTypeCombo) },
                { houseFilterLogicalColumn, GetSelectedFilterValue(houseCodeCombo) }
            };
            if (handoverStatusCombo != null)
            {
                filters["HANDOVERED STATUS"] = GetSelectedFilterValue(handoverStatusCombo);
            }
            if (planDescriptionCombo != null)
            {
                filters["Plan Description"] = GetSelectedFilterValue(planDescriptionCombo);
            }
            if (subcontractorCombo != null)
            {
                filters["SUBCONTRACTOR"] = GetSelectedFilterValue(subcontractorCombo);
            }

            if (tocCombo != null)
            {
                filters[BoreyTocLogicalColumn] = GetSelectedFilterValue(tocCombo);
            }

            DataView filtered = BuildBoreyFilteredView(source, filters);
            int displayedRowCount = filtered?.Count ?? 0;
            if (grid != null)
            {
                if (isProjectInfoTab)
                {
                    DataTable summaryTable = BuildProjectInfoSummaryTable(filtered);
                    grid.ItemsSource = summaryTable.DefaultView;
                    displayedRowCount = summaryTable.Rows.Count;
                }
                else if (isProjectStatusTab)
                {
                    DataTable statusTable = BuildProjectStatusSummaryTable(filtered);
                    grid.ItemsSource = statusTable.DefaultView;
                    displayedRowCount = statusTable.Rows.Count;
                }
                else if (isPlanVsActualTab)
                {
                    BuildPlanVsActualTables(
                        filtered,
                        out DataTable planTable,
                        out DataTable actualTable,
                        out DataTable remainingTable);
                    DataGrid planGrid = GetBoreyNamedGrid("PlanVsActualPlanDataGrid");
                    DataGrid actualGrid = GetBoreyNamedGrid("PlanVsActualActualDataGrid");
                    DataGrid remainingGrid = GetBoreyNamedGrid("PlanVsActualRemainingDataGrid");
                    if (planGrid != null)
                    {
                        planGrid.ItemsSource = planTable.DefaultView;
                    }
                    if (actualGrid != null)
                    {
                        actualGrid.ItemsSource = actualTable.DefaultView;
                    }
                    if (remainingGrid != null)
                    {
                        remainingGrid.ItemsSource = remainingTable.DefaultView;
                    }

                    ApplyBoreyColumnVisibilityPreferences(planGrid);
                    ApplyBoreyColumnVisibilityPreferences(actualGrid);
                    ApplyBoreyColumnVisibilityPreferences(remainingGrid);

                    displayedRowCount = filtered.Count;
                    UpdatePlanVsActualColumnChart(planTable, actualTable, remainingTable);
                }
                else
                {
                    grid.ItemsSource = filtered;
                    displayedRowCount = filtered.Count;
                }

                if (!isPlanVsActualTab)
                {
                    ApplyBoreyColumnDisplayOrder(grid, logicalTable);
                    ApplyBoreyColumnVisibilityPreferences(grid);
                    ResetPlanVsActualColumnChart();
                }
            }

            if (isHandoverSubTab)
            {
                UpdateHandoverColumnChart(filtered);
            }
            else
            {
                ResetHandoverColumnChart();
            }

            UpdateBoreyBottomCount(header, displayedRowCount);
            UpdateBoreyProjectStatusDonutChart(isProjectStatusTab ? filtered : null);
            UpdateBoreyProjectOverallDashboard(filtered, logicalTable);
            ContextMenu pickerMenu = GetBoreyColumnPickerContextMenu();
            if (pickerMenu != null && pickerMenu.IsOpen)
            {
                RefreshBoreyColumnPickerMenu();
            }
        }

        private static void ApplyBoreyColumnDisplayOrder(DataGrid grid, string logicalTable)
        {
            if (grid == null)
            {
                return;
            }

            bool isMonthlyPlanGrid =
                string.Equals(logicalTable, "Planing_Info", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(grid.Name, "PlaningInfoDataGrid", StringComparison.OrdinalIgnoreCase);
            bool isSubcontractorGrid = string.Equals(grid.Name, "SubcontractorDataGrid", StringComparison.OrdinalIgnoreCase);
            bool isContractGrid = string.Equals(grid.Name, "ContractLoaBlcDataGrid", StringComparison.OrdinalIgnoreCase);
            if (!isMonthlyPlanGrid && !isSubcontractorGrid && !isContractGrid)
            {
                return;
            }

            Action apply = () =>
            {
                if (grid.Columns == null || grid.Columns.Count == 0)
                {
                    return;
                }

                int targetIndex = 0;
                if (isMonthlyPlanGrid)
                {
                    targetIndex = ApplyPreferredBoreyColumnOrder(grid, BoreyMonthlyPlanColumnOrder, targetIndex);
                }

                if (isSubcontractorGrid || isContractGrid)
                {
                    targetIndex = ApplyPreferredBoreyColumnOrder(grid, BoreySubcontractorBaseColumnOrder, targetIndex);
                    if (isSubcontractorGrid)
                    {
                        ApplySubcontractorItemColumnOrder(grid, targetIndex);
                    }
                    else
                    {
                        ApplyContractItemColumnOrder(grid, targetIndex);
                    }
                }
            };

            if (grid.Columns != null && grid.Columns.Count > 0)
            {
                apply();
            }
            else
            {
                grid.Dispatcher.BeginInvoke(apply, DispatcherPriority.Loaded);
            }
        }

        private static int ApplyPreferredBoreyColumnOrder(DataGrid grid, IEnumerable<string> preferredOrder, int startIndex)
        {
            int targetIndex = Math.Max(0, startIndex);
            if (grid == null || preferredOrder == null)
            {
                return targetIndex;
            }

            foreach (string logicalName in preferredOrder)
            {
                DataGridColumn column = grid.Columns
                    .FirstOrDefault(c => IsBoreyColumnMatchHeader(logicalName, Convert.ToString(c?.Header, CultureInfo.InvariantCulture)));
                if (column == null)
                {
                    continue;
                }

                try
                {
                    column.DisplayIndex = targetIndex++;
                }
                catch
                {
                    // Ignore invalid display index transitions.
                }
            }

            return targetIndex;
        }

        private static bool IsBoreyColumnMatchHeader(string logicalName, string headerText)
        {
            string header = (headerText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(logicalName) || string.IsNullOrWhiteSpace(header))
            {
                return false;
            }

            if (string.Equals(logicalName, header, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (BoreyFilterColumnAliases.TryGetValue(logicalName, out string[] aliases))
            {
                return aliases.Any(a => string.Equals(a, header, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        private void UpdateBoreyProjectOverallDashboard(DataView view, string logicalTable)
        {
            if (!string.Equals(logicalTable, "Planing_Info", StringComparison.OrdinalIgnoreCase))
            {
                ResetBoreyProjectOverallDashboard();
                return;
            }

            DataTable table = view?.Table;
            if (table == null)
            {
                ResetBoreyProjectOverallDashboard();
                return;
            }

            string houseUnitsColumn = ResolveFilterColumn(table, BoreyHouseUnitsLogicalColumn);
            string handoveredUnitsColumn = ResolveFilterColumn(table, BoreyHandoveredUnitsLogicalColumn);
            string tocColumn = ResolveTocColumnName(table);

            var monthly = new SortedDictionary<DateTime, (double Plan, double Actual)>();
            double totalPlan = 0.0;
            double totalActual = 0.0;
            int rowCount = 0;

            foreach (DataRowView rowView in view.Cast<DataRowView>())
            {
                if (rowView?.Row == null || rowView.Row.RowState == DataRowState.Deleted)
                {
                    continue;
                }

                rowCount++;

                double plan = ParseBoreyNumericValue(string.IsNullOrWhiteSpace(houseUnitsColumn) ? null : rowView.Row[houseUnitsColumn]);
                double actual = ParseBoreyNumericValue(string.IsNullOrWhiteSpace(handoveredUnitsColumn) ? null : rowView.Row[handoveredUnitsColumn]);
                totalPlan += plan;
                totalActual += actual;

                if (!string.IsNullOrWhiteSpace(tocColumn))
                {
                    string tocRaw = Convert.ToString(rowView.Row[tocColumn], CultureInfo.InvariantCulture)?.Trim();
                    if (TryGetTocMonthEnd(tocRaw, out DateTime monthEnd))
                    {
                        if (!monthly.TryGetValue(monthEnd.Date, out (double Plan, double Actual) current))
                        {
                            current = (0.0, 0.0);
                        }

                        monthly[monthEnd.Date] = (current.Plan + plan, current.Actual + actual);
                    }
                }
            }

            TextBlock planUnitsText = GetBoreyNamedTextBlock("BoreyProjectOverallPlanUnitsText");
            if (planUnitsText != null)
            {
                planUnitsText.Text = totalPlan.ToString("0.###", CultureInfo.InvariantCulture);
            }

            TextBlock actualUnitsText = GetBoreyNamedTextBlock("BoreyProjectOverallActualUnitsText");
            if (actualUnitsText != null)
            {
                actualUnitsText.Text = totalActual.ToString("0.###", CultureInfo.InvariantCulture);
            }

            TextBlock gapUnitsText = GetBoreyNamedTextBlock("BoreyProjectOverallGapUnitsText");
            if (gapUnitsText != null)
            {
                gapUnitsText.Text = (totalPlan - totalActual).ToString("0.###", CultureInfo.InvariantCulture);
            }

            TextBlock scopeText = GetBoreyNamedTextBlock("BoreyProjectOverallScopeText");
            if (scopeText != null)
            {
                scopeText.Text = "Rows: " + rowCount.ToString(CultureInfo.InvariantCulture) +
                                 " | Months: " + monthly.Count.ToString(CultureInfo.InvariantCulture);
            }

            OxyPlot.Wpf.PlotView plotView = GetBoreyNamedPlotView("BoreyProjectOverallPlotView");
            if (plotView == null)
            {
                return;
            }

            var model = new PlotModel
            {
                PlotAreaBorderColor = OxyColor.Parse("#D4DCE8"),
                IsLegendVisible = true
            };

            var axisX = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                GapWidth = 0.25
            };

            var planSeries = new RectangleBarSeries
            {
                Title = "Plan (House-Units)",
                FillColor = OxyColor.Parse("#4E79A7"),
                StrokeColor = OxyColor.Parse("#3A5E88"),
                StrokeThickness = 0.8
            };

            var actualSeries = new RectangleBarSeries
            {
                Title = "Actual (Handovered-Units)",
                FillColor = OxyColor.Parse("#59A14F"),
                StrokeColor = OxyColor.Parse("#4B8A42"),
                StrokeThickness = 0.8
            };

            var points = new List<(string Label, double Plan, double Actual)>();
            if (monthly.Count == 0)
            {
                points.Add(("Total", totalPlan, totalActual));
            }
            else
            {
                foreach (KeyValuePair<DateTime, (double Plan, double Actual)> item in monthly)
                {
                    points.Add((item.Key.ToString("MMM-yyyy", CultureInfo.InvariantCulture), item.Value.Plan, item.Value.Actual));
                }
            }

            double axisMax = points.Count == 0
                ? 1.0
                : Math.Max(1.0, points.Max(p => Math.Max(p.Plan, p.Actual)) * 1.28);
            var axisY = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Units",
                Minimum = 0,
                Maximum = axisMax
            };

            for (int i = 0; i < points.Count; i++)
            {
                axisX.Labels.Add(points[i].Label);
                const double groupWidth = 0.72;
                double barWidth = groupWidth / 2.0;
                double groupStart = i - (groupWidth * 0.5);

                double planX0 = groupStart;
                double planX1 = planX0 + barWidth;
                double actualX0 = planX1;
                double actualX1 = actualX0 + barWidth;

                planSeries.Items.Add(new RectangleBarItem(planX0, 0.0, planX1, points[i].Plan));
                actualSeries.Items.Add(new RectangleBarItem(actualX0, 0.0, actualX1, points[i].Actual));

                AddDashboardBarValueLabel(
                    model,
                    planX0,
                    planX1,
                    points[i].Plan,
                    axisMax,
                    points[i].Plan.ToString("0.###", CultureInfo.InvariantCulture),
                    OxyColor.Parse("#355D8A"),
                    percentMode: false,
                    laneIndex: 0,
                    showWhenZero: false);

                AddDashboardBarValueLabel(
                    model,
                    actualX0,
                    actualX1,
                    points[i].Actual,
                    axisMax,
                    points[i].Actual.ToString("0.###", CultureInfo.InvariantCulture),
                    OxyColor.Parse("#3E7D36"),
                    percentMode: false,
                    laneIndex: 0,
                    showWhenZero: false);
            }

            model.Axes.Add(axisX);
            model.Axes.Add(axisY);
            model.Series.Add(planSeries);
            model.Series.Add(actualSeries);
            plotView.Model = model;
        }

        private void ResetBoreyProjectOverallDashboard()
        {
            TextBlock planUnitsText = GetBoreyNamedTextBlock("BoreyProjectOverallPlanUnitsText");
            if (planUnitsText != null)
            {
                planUnitsText.Text = "0";
            }

            TextBlock actualUnitsText = GetBoreyNamedTextBlock("BoreyProjectOverallActualUnitsText");
            if (actualUnitsText != null)
            {
                actualUnitsText.Text = "0";
            }

            TextBlock gapUnitsText = GetBoreyNamedTextBlock("BoreyProjectOverallGapUnitsText");
            if (gapUnitsText != null)
            {
                gapUnitsText.Text = "0";
            }

            TextBlock scopeText = GetBoreyNamedTextBlock("BoreyProjectOverallScopeText");
            if (scopeText != null)
            {
                scopeText.Text = "Rows: 0 | Months: 0";
            }

            OxyPlot.Wpf.PlotView plotView = GetBoreyNamedPlotView("BoreyProjectOverallPlotView");
            if (plotView != null)
            {
                plotView.Model = new PlotModel();
            }
        }

        private static double ParseBoreyNumericValue(object raw)
        {
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0.0;
            }

            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value) ||
                double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            return 0.0;
        }

        private void UpdateBoreyProjectStatusDonutChart(DataView filteredView)
        {
            OxyPlot.Wpf.PlotView plotView = GetBoreyNamedPlotView("ProjectStatusDonutPlotView");
            TextBlock summaryText = GetBoreyNamedTextBlock("ProjectStatusDonutSummaryText");
            WrapPanel legendPanel = FindName("ProjectStatusDonutLegendPanel") as WrapPanel;
            if (plotView == null && summaryText == null && legendPanel == null)
            {
                return;
            }

            Dictionary<string, double> totals = BuildProjectStatusBucketTotals(filteredView);
            double totalUnits = totals.Values.Sum();
            int categoryCount = totals.Count(x => x.Value > 1e-9);

            if (summaryText != null)
            {
                summaryText.Text = "Total Units: " + totalUnits.ToString("0.###", CultureInfo.InvariantCulture) +
                                   " | Categories: " + categoryCount.ToString(CultureInfo.InvariantCulture);
            }

            UpdateBoreyProjectStatusDonutLegend(legendPanel, totals, totalUnits);

            if (plotView == null)
            {
                return;
            }

            var model = new PlotModel
            {
                Background = OxyColor.Parse("#F8FAFD"),
                PlotAreaBorderColor = OxyColors.Undefined,
                PlotAreaBorderThickness = new OxyThickness(0),
                // Reserve breathing room so outside labels are not clipped by the plot bounds.
                Padding = new OxyThickness(16, 10, 16, 10),
                PlotMargins = new OxyThickness(20, 12, 20, 12),
                TextColor = OxyColor.Parse("#3C4B61"),
                IsLegendVisible = false
            };

            var donutSeries = new PieSeries
            {
                StartAngle = 270,
                AngleSpan = 360,
                Diameter = 0.86,
                InnerDiameter = 0.62,
                Stroke = OxyColor.Parse("#FFFFFF"),
                StrokeThickness = 1.0,
                OutsideLabelFormat = "{1}: {2:0.##}",
                InsideLabelFormat = "",
                TickDistance = 2,
                TickHorizontalLength = 4,
                TickRadialLength = 4,
                TickLabelDistance = 2
            };

            foreach (string bucket in ProjectStatusBuckets)
            {
                if (!totals.TryGetValue(bucket, out double value) || value <= 1e-9)
                {
                    continue;
                }

                donutSeries.Slices.Add(new PieSlice(bucket, value)
                {
                    Fill = GetProjectStatusBucketColor(bucket)
                });
            }

            if (donutSeries.Slices.Count == 0)
            {
                donutSeries.Slices.Add(new PieSlice("No data", 1.0)
                {
                    Fill = OxyColor.Parse("#D9DFEA")
                });
            }

            model.Series.Add(donutSeries);
            plotView.Model = model;
        }

        private void UpdateBoreyProjectStatusDonutLegend(
            WrapPanel legendPanel,
            IDictionary<string, double> totals,
            double totalUnits)
        {
            if (legendPanel == null)
            {
                return;
            }

            legendPanel.Children.Clear();

            if (totals == null || totals.Count == 0)
            {
                return;
            }

            foreach (string bucket in ProjectStatusBuckets)
            {
                if (!totals.TryGetValue(bucket, out double value) || value <= 1e-9)
                {
                    continue;
                }

                legendPanel.Children.Add(CreateBoreyProjectStatusLegendItem(
                    bucket,
                    GetProjectStatusBucketColor(bucket),
                    value,
                    totalUnits));
            }

            if (legendPanel.Children.Count == 0)
            {
                legendPanel.Children.Add(CreateBoreyProjectStatusLegendItem(
                    "No data",
                    OxyColor.Parse("#D9DFEA"),
                    0.0,
                    0.0));
            }
        }

        private static FrameworkElement CreateBoreyProjectStatusLegendItem(
            string bucket,
            OxyColor color,
            double value,
            double totalUnits)
        {
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 14, 6),
                VerticalAlignment = global::System.Windows.VerticalAlignment.Center
            };

            var swatch = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = ToMediaBrush(color),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 153, 171)),
                BorderThickness = new Thickness(0.8)
            };

            string text = bucket ?? "";
            if (value > 1e-9)
            {
                text += ": " + value.ToString("0.##", CultureInfo.InvariantCulture);
                if (totalUnits > 1e-9)
                {
                    text += " (" + (value * 100.0 / totalUnits).ToString("0.##", CultureInfo.InvariantCulture) + "%)";
                }
            }

            var label = new TextBlock
            {
                Text = text,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 75, 97)),
                VerticalAlignment = global::System.Windows.VerticalAlignment.Center
            };

            itemPanel.Children.Add(swatch);
            itemPanel.Children.Add(label);
            return itemPanel;
        }

        private void RequestBoreySyncFromRevit(string logicalTableName = "")
        {
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            _handler.Request.Borey.SharedParameterFilePath = SharedParameterPathResolver.ResolveDefaultSharedParameterPath(baseDir);
            _handler.Request.Borey.SharedParameterGroupName = "CBIM-BOREY";
            _handler.Request.Borey.EnsureSharedParameters = false;
            _handler.Request.Borey.LogicalTableName = logicalTableName ?? "";
            _handler.Request.Borey.Rows = new List<BoreyGridRowPayload>();
            _handler.Request.Borey.ApplyGridChanges = false;
            _handler.Request.RequestType = CadToModelRequestType.SyncBoreyFloorData;
            _boreyInitialLoadRequested = true;
            QueueExternalRequest();
        }

        private void OnBoreySubTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, BoreySubTabControl) || !IsBoreyTab())
            {
                return;
            }

            if (_boreyRevitTableCache.Count == 0)
            {
                ShowStatus("BOREY: loading floor data...");
                if (!TryGetCurrentBoreyLogicalTable(out string firstLogicalTable))
                {
                    firstLogicalTable = "";
                }

                RequestBoreySyncFromRevit(firstLogicalTable);
                return;
            }

            if (TryGetCurrentBoreyLogicalTable(out string logicalTable) &&
                !_boreyRevitTableCache.ContainsKey(logicalTable))
            {
                ShowStatus("BOREY: loading selected tab data...");
                RequestBoreySyncFromRevit(logicalTable);
                return;
            }

            ApplyCurrentBoreyRevitView(refreshFilterOptions: true);
        }

        private void OnBoreyFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_boreyUiSyncInProgress || !IsBoreyTab())
            {
                return;
            }

            ApplyCurrentBoreyRevitView(refreshFilterOptions: true);
        }

        private void OnBoreyGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || !IsBoreyTab() || _boreyUiSyncInProgress || _boreySelectionSyncInProgress)
            {
                return;
            }

            if (!(sender is DataGrid grid))
            {
                return;
            }

            List<BoreyGridRowPayload> selectedRows = BuildSelectedBoreyGridRows(grid);
            if (selectedRows.Count == 0)
            {
                return;
            }

            string logicalTable = GetBoreyLogicalTableForGrid(grid);

            _boreySelectionSyncInProgress = true;
            try
            {
                _handler.Request.Borey.EnsureSharedParameters = false;
                _handler.Request.Borey.ApplyGridChanges = false;
                _handler.Request.Borey.LogicalTableName = logicalTable;
                _handler.Request.Borey.Rows = selectedRows;
                _handler.Request.RequestType = CadToModelRequestType.SelectBoreyElements;
                QueueExternalRequest();
            }
            finally
            {
                _boreySelectionSyncInProgress = false;
            }
        }

        private void OnBoreyGridAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.PropertyName))
            {
                return;
            }

            DataGrid grid = sender as DataGrid;
            string propertyName = e.PropertyName.Trim();

            if (grid != null &&
                string.Equals(grid.Name, "SaleInfoDataGrid", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(propertyName, "Name", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propertyName, "Description", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propertyName, "House No(Sell)", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(propertyName, "House No (Sell)", StringComparison.OrdinalIgnoreCase)))
            {
                e.Cancel = true;
                return;
            }

            bool isSubcontractorColumn =
                grid != null &&
                string.Equals(grid.Name, "SubcontractorDataGrid", StringComparison.OrdinalIgnoreCase) &&
                IsBoreyColumnMatchHeader("SUBCONTRACTOR", propertyName);
            if (isSubcontractorColumn)
            {
                List<string> subcontractorItems = GetBoreySubcontractorOptions(grid);
                var subcontractorComboColumn = new DataGridComboBoxColumn
                {
                    Header = e.Column?.Header ?? propertyName,
                    ItemsSource = subcontractorItems,
                    SelectedItemBinding = new System.Windows.Data.Binding("[" + e.PropertyName + "]")
                    {
                        Mode = System.Windows.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                    },
                    SortMemberPath = e.PropertyName
                };

                if (e.Column != null)
                {
                    subcontractorComboColumn.Width = e.Column.Width;
                }
                if (grid != null && IsBoreyColumnHidden(grid.Name, propertyName))
                {
                    subcontractorComboColumn.Visibility = System.Windows.Visibility.Collapsed;
                }

                e.Column = subcontractorComboColumn;
                return;
            }

            if (!string.Equals(propertyName, "HANDOVERED STATUS", StringComparison.OrdinalIgnoreCase))
            {
                if (grid != null && e.Column != null && IsBoreyColumnHidden(grid.Name, propertyName))
                {
                    e.Column.Visibility = System.Windows.Visibility.Collapsed;
                }
                return;
            }

            List<string> items = GetBoreyHandoverStatusOptions(grid);
            var comboColumn = new DataGridComboBoxColumn
            {
                Header = e.Column?.Header ?? "HANDOVERED STATUS",
                ItemsSource = items,
                SelectedItemBinding = new System.Windows.Data.Binding("[" + e.PropertyName + "]")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                },
                SortMemberPath = e.PropertyName
            };

            if (e.Column != null)
            {
                comboColumn.Width = e.Column.Width;
            }
            if (grid != null && IsBoreyColumnHidden(grid.Name, propertyName))
            {
                comboColumn.Visibility = System.Windows.Visibility.Collapsed;
            }

            e.Column = comboColumn;
        }

        private static List<string> GetBoreyHandoverStatusOptions(DataGrid grid)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string option in BoreyHandoverStatusDefaultOptions)
            {
                string text = (option ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }

            if (grid?.ItemsSource is DataView view && view.Table != null)
            {
                string columnName = view.Table.Columns
                    .Cast<DataColumn>()
                    .Select(c => c?.ColumnName ?? "")
                    .FirstOrDefault(n => string.Equals(n, "HANDOVERED STATUS", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    foreach (DataRow row in view.Table.Rows)
                    {
                        if (row == null || row.RowState == DataRowState.Deleted)
                        {
                            continue;
                        }

                        string value = Convert.ToString(row[columnName], CultureInfo.InvariantCulture)?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                }
            }

            var result = new List<string> { "" };
            foreach (string option in BoreyHandoverStatusDefaultOptions)
            {
                string text = (option ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(text) && values.Remove(text))
                {
                    result.Add(text);
                }
            }

            result.AddRange(values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        private static List<string> GetBoreySubcontractorOptions(DataGrid grid)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (grid?.ItemsSource is DataView view && view.Table != null)
            {
                string subcontractorColumn = ResolveFilterColumn(view.Table, "SUBCONTRACTOR");
                if (!string.IsNullOrWhiteSpace(subcontractorColumn))
                {
                    foreach (DataRow row in view.Table.Rows)
                    {
                        if (row == null || row.RowState == DataRowState.Deleted)
                        {
                            continue;
                        }

                        string value = Convert.ToString(row[subcontractorColumn], CultureInfo.InvariantCulture)?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                }
            }

            var result = new List<string> { "" };
            result.AddRange(values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        private bool PersistBoreyGridChanges(string logicalTableName, DataGrid grid, out string summary)
        {
            summary = "";
            if (grid == null || string.IsNullOrWhiteSpace(logicalTableName))
            {
                return true;
            }

            // Commit any in-progress cell edits before reading row states.
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);

            if (!(grid.ItemsSource is DataView view) || view.Table == null)
            {
                return true;
            }

            DataTable table = view.Table;
            List<DataRow> changedRows = table.Rows
                .Cast<DataRow>()
                .Where(r =>
                    r.RowState == DataRowState.Added ||
                    r.RowState == DataRowState.Modified ||
                    r.RowState == DataRowState.Deleted)
                .ToList();

            if (changedRows.Count == 0)
            {
                return true;
            }

            string boreyDbPath = ResolveBoreyDbPathForTableCached(logicalTableName, out string physicalTableName);
            if (string.IsNullOrWhiteSpace(boreyDbPath))
            {
                summary = "BOREY: cannot save. Target database not found.";
                return false;
            }

            if (!EnsureBoreyFloorsSchema(boreyDbPath, physicalTableName, out string schemaError))
            {
                summary = "BOREY: cannot save because schema update failed. " + schemaError;
                return false;
            }

            if (!TryUpsertBoreyRowsToFloors(
                boreyDbPath,
                physicalTableName,
                changedRows,
                out int inserted,
                out int updated,
                out int deleted,
                out int skipped,
                out int invalidValues,
                out string error))
            {
                summary = "BOREY: save failed. " + error;
                return false;
            }

            table.AcceptChanges();
            _boreyTableCache.Clear();
            summary = $"BOREY: saved to {Path.GetFileName(boreyDbPath)}:{physicalTableName} (inserted={inserted}, updated={updated}, deleted={deleted}, skipped={skipped}, invalid={invalidValues}).";
            return true;
        }

        private bool TryUpsertBoreyRowsToFloors(
            string boreyDbPath,
            string physicalTableName,
            List<DataRow> rows,
            out int inserted,
            out int updated,
            out int deleted,
            out int skipped,
            out int invalidValues,
            out string error)
        {
            inserted = 0;
            updated = 0;
            deleted = 0;
            skipped = 0;
            invalidValues = 0;
            error = "";

            if (rows == null || rows.Count == 0)
            {
                return true;
            }

            using (var conn = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + boreyDbPath + ";Persist Security Info=False;"))
            {
                conn.Open();
                HashSet<string> physicalColumns = GetTableColumnSet(conn, physicalTableName);
                if (physicalColumns.Count == 0)
                {
                    error = "Table '" + physicalTableName + "' not found or has no readable schema.";
                    return false;
                }

                Dictionary<string, Type> typeMap = GetTableColumnTypeMap(conn, physicalTableName);
                using (OleDbTransaction tx = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (DataRow row in rows)
                        {
                            if (row == null)
                            {
                                skipped++;
                                continue;
                            }

                            if (row.RowState == DataRowState.Deleted)
                            {
                                Dictionary<string, object> deletedMappedValues =
                                    MapBoreyRowToPhysicalValues(row, physicalColumns, typeMap, out int deletedRowInvalidCount, DataRowVersion.Original);
                                invalidValues += deletedRowInvalidCount;
                                if (!TryResolveBoreyRowKey(deletedMappedValues, out string deleteKeyColumn, out object deleteKeyValue))
                                {
                                    skipped++;
                                    continue;
                                }

                                int removed = DeleteBoreyRow(conn, tx, physicalTableName, deleteKeyColumn, deleteKeyValue);
                                if (removed > 0)
                                {
                                    deleted += removed;
                                }
                                else
                                {
                                    skipped++;
                                }

                                continue;
                            }

                            Dictionary<string, object> mappedValues =
                                MapBoreyRowToPhysicalValues(row, physicalColumns, typeMap, out int rowInvalidCount, DataRowVersion.Current);
                            invalidValues += rowInvalidCount;

                            if (mappedValues.Count == 0)
                            {
                                skipped++;
                                continue;
                            }

                            if (!TryResolveBoreyRowKey(mappedValues, out string keyColumn, out object keyValue))
                            {
                                skipped++;
                                continue;
                            }

                            bool exists = BoreyRowExists(conn, tx, physicalTableName, keyColumn, keyValue);
                            if (exists)
                            {
                                int updateCols = UpdateBoreyRow(conn, tx, physicalTableName, keyColumn, keyValue, mappedValues);
                                if (updateCols > 0)
                                {
                                    updated++;
                                }
                                else
                                {
                                    skipped++;
                                }
                            }
                            else
                            {
                                InsertBoreyRow(conn, tx, physicalTableName, mappedValues);
                                inserted++;
                            }
                        }

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            tx.Rollback();
                        }
                        catch
                        {
                        }

                        error = ex.Message;
                        return false;
                    }
                }
            }

            return true;
        }

        private static Dictionary<string, object> MapBoreyRowToPhysicalValues(
            DataRow row,
            HashSet<string> physicalColumns,
            Dictionary<string, Type> typeMap,
            out int invalidCount,
            DataRowVersion rowVersion)
        {
            var mapped = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            invalidCount = 0;
            if (row?.Table == null || physicalColumns == null || physicalColumns.Count == 0)
            {
                return mapped;
            }

            foreach (DataColumn sourceColumn in row.Table.Columns)
            {
                string logicalName = sourceColumn?.ColumnName ?? "";
                if (string.IsNullOrWhiteSpace(logicalName))
                {
                    continue;
                }

                string physicalName = ResolveFilterColumn(physicalColumns, logicalName.Trim());
                if (string.IsNullOrWhiteSpace(physicalName))
                {
                    continue;
                }

                if (!row.HasVersion(rowVersion))
                {
                    continue;
                }

                string raw = Convert.ToString(row[sourceColumn, rowVersion], CultureInfo.InvariantCulture)?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                Type targetType = typeMap != null && typeMap.TryGetValue(physicalName, out Type t) ? t : typeof(string);
                if (TryConvertToDbValue(raw, targetType, out object converted))
                {
                    mapped[physicalName] = converted;
                }
                else
                {
                    invalidCount++;
                }
            }

            return mapped;
        }

        private static bool TryResolveBoreyRowKey(Dictionary<string, object> mappedValues, out string keyColumn, out object keyValue)
        {
            keyColumn = "";
            keyValue = null;
            if (mappedValues == null || mappedValues.Count == 0)
            {
                return false;
            }

            string[] preferredKeys = { "Id", "HOUSE ID", "House No(Sell)", "House Code", "Code_Item", "Code", "Name", "Mark" };
            foreach (string key in preferredKeys)
            {
                if (!mappedValues.TryGetValue(key, out object candidate))
                {
                    continue;
                }

                if (IsDbNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                keyColumn = key;
                keyValue = candidate;
                return true;
            }

            return false;
        }

        private static bool BoreyRowExists(OleDbConnection conn, OleDbTransaction tx, string tableName, string keyColumn, object keyValue)
        {
            using (var cmd = new OleDbCommand(
                "SELECT COUNT(*) FROM " + QuoteAccessName(tableName) + " WHERE " + QuoteAccessName(keyColumn) + " = ?",
                conn,
                tx))
            {
                cmd.Parameters.AddWithValue("@p0", keyValue ?? DBNull.Value);
                object scalar = cmd.ExecuteScalar();
                int count = 0;
                if (scalar != null && scalar != DBNull.Value)
                {
                    count = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
                }

                return count > 0;
            }
        }

        private static int UpdateBoreyRow(
            OleDbConnection conn,
            OleDbTransaction tx,
            string tableName,
            string keyColumn,
            object keyValue,
            Dictionary<string, object> mappedValues)
        {
            List<KeyValuePair<string, object>> updates = mappedValues
                .Where(kv => !string.Equals(kv.Key, keyColumn, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (updates.Count == 0)
            {
                return 0;
            }

            string setClause = string.Join(", ", updates.Select(kv => QuoteAccessName(kv.Key) + " = ?"));
            string sql = "UPDATE " + QuoteAccessName(tableName) + " SET " + setClause + " WHERE " + QuoteAccessName(keyColumn) + " = ?";
            using (var cmd = new OleDbCommand(sql, conn, tx))
            {
                foreach (KeyValuePair<string, object> kv in updates)
                {
                    cmd.Parameters.AddWithValue("@p", kv.Value ?? DBNull.Value);
                }

                cmd.Parameters.AddWithValue("@pKey", keyValue ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            return updates.Count;
        }

        private static int DeleteBoreyRow(
            OleDbConnection conn,
            OleDbTransaction tx,
            string tableName,
            string keyColumn,
            object keyValue)
        {
            string sql = "DELETE FROM " + QuoteAccessName(tableName) + " WHERE " + QuoteAccessName(keyColumn) + " = ?";
            using (var cmd = new OleDbCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@pKey", keyValue ?? DBNull.Value);
                return cmd.ExecuteNonQuery();
            }
        }

        private static void InsertBoreyRow(
            OleDbConnection conn,
            OleDbTransaction tx,
            string tableName,
            Dictionary<string, object> mappedValues)
        {
            List<string> columns = mappedValues.Keys.ToList();
            string columnList = string.Join(", ", columns.Select(QuoteAccessName));
            string placeholders = string.Join(", ", columns.Select(_ => "?"));
            string sql = "INSERT INTO " + QuoteAccessName(tableName) + " (" + columnList + ") VALUES (" + placeholders + ")";
            using (var cmd = new OleDbCommand(sql, conn, tx))
            {
                foreach (string column in columns)
                {
                    cmd.Parameters.AddWithValue("@p", mappedValues[column] ?? DBNull.Value);
                }

                cmd.ExecuteNonQuery();
            }
        }

        private void LoadBoreyTableData(
            string tableName,
            System.Windows.Controls.ComboBox zoneCombo,
            System.Windows.Controls.ComboBox blockCombo,
            System.Windows.Controls.ComboBox subBlockCombo,
            System.Windows.Controls.ComboBox houseTypeCombo,
            System.Windows.Controls.ComboBox houseCodeCombo,
            DataGrid targetGrid,
            bool forceRefreshFilters = false)
        {
            string boreyDbPath = ResolveBoreyDbPathForTableCached(tableName, out string physicalTableName);
            if (string.IsNullOrWhiteSpace(boreyDbPath))
            {
                ShowStatus("BOREY: table '" + tableName + "' not found in DB candidates: " + string.Join(" | ", BoreyDbCandidatePaths));
                return;
            }

            try
            {
                if (string.Equals(physicalTableName, BoreyFallbackPhysicalTable, StringComparison.OrdinalIgnoreCase) &&
                    !EnsureBoreyFloorsSchema(boreyDbPath, physicalTableName, out string schemaError))
                {
                    ShowStatus("BOREY: failed to prepare Floors schema. " + schemaError);
                    return;
                }

                var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ZONE", GetSelectedFilterValue(zoneCombo) },
                    { "BLOCK", GetSelectedFilterValue(blockCombo) },
                    { "SUB-BLOCK", GetSelectedFilterValue(subBlockCombo) },
                    { "HOUSE-TYPE", GetSelectedFilterValue(houseTypeCombo) },
                    { "House Code", GetSelectedFilterValue(houseCodeCombo) }
                };

                bool refreshFilters = forceRefreshFilters || ShouldRefreshBoreyFilterOptions(zoneCombo, blockCombo, subBlockCombo, houseTypeCombo, houseCodeCombo);
                DataTable table;
                if (string.Equals(physicalTableName, BoreyFallbackPhysicalTable, StringComparison.OrdinalIgnoreCase))
                {
                    if (refreshFilters)
                    {
                        RefreshBoreyFilterOptions(boreyDbPath, physicalTableName, zoneCombo, blockCombo, subBlockCombo, houseTypeCombo, houseCodeCombo);
                    }

                    table = QueryBoreyProjectedTable(boreyDbPath, physicalTableName, tableName, filters);
                }
                else
                {
                    if (refreshFilters)
                    {
                        RefreshBoreyFilterOptions(boreyDbPath, physicalTableName, zoneCombo, blockCombo, subBlockCombo, houseTypeCombo, houseCodeCombo);
                    }

                    table = QueryBoreyTable(boreyDbPath, physicalTableName, filters);
                }

                if (targetGrid != null)
                {
                    targetGrid.ItemsSource = table.DefaultView;
                }

                ShowStatus($"BOREY: loaded {table.Rows.Count} row(s) from {tableName} (physical: {physicalTableName}). Source: {boreyDbPath}");
            }
            catch (Exception ex)
            {
                ShowStatus("BOREY: failed to load data. " + ex.Message);
            }
        }

        private bool EnsureBoreyFloorsSchema(string boreyDbPath, string physicalTableName, out string error)
        {
            error = "";
            if (!string.Equals(physicalTableName, BoreyFallbackPhysicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(boreyDbPath) || !File.Exists(boreyDbPath))
            {
                error = "Database file not found.";
                return false;
            }

            if (string.Equals(_cachedBoreySchemaPath, boreyDbPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Keep mostly text columns for flexible paste/import. Existing typed columns are preserved.
            var requiredColumns = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Code_Item", "TEXT(255)"),
                new KeyValuePair<string, string>("Code", "TEXT(255)"),
                new KeyValuePair<string, string>("NÃƒâ€šÃ‚Âº", "TEXT(255)"),
                new KeyValuePair<string, string>("ZONE", "TEXT(255)"),
                new KeyValuePair<string, string>("BLOCK", "TEXT(255)"),
                new KeyValuePair<string, string>("SUB-BLOCK", "TEXT(255)"),
                new KeyValuePair<string, string>("HOUSE-TYPE", "TEXT(255)"),
                new KeyValuePair<string, string>("H_Tp", "TEXT(255)"),
                new KeyValuePair<string, string>("HOUSE ID", "TEXT(255)"),
                new KeyValuePair<string, string>("House No(Sell)", "TEXT(255)"),
                new KeyValuePair<string, string>("House Code", "TEXT(255)"),
                new KeyValuePair<string, string>("%Site_Progress", "DOUBLE"),
                new KeyValuePair<string, string>("LAND LOTS", "TEXT(255)"),
                new KeyValuePair<string, string>("House-Units", "TEXT(255)"),
                new KeyValuePair<string, string>("Data_Sold_Out", "TEXT(255)"),
                new KeyValuePair<string, string>("Sold Only Land", "TEXT(255)"),
                new KeyValuePair<string, string>("Handovered-Units", "TEXT(255)"),
                new KeyValuePair<string, string>("HANDOVERED", "LONG"),
                new KeyValuePair<string, string>("Sold/Unsold", "TEXT(255)"),
                new KeyValuePair<string, string>("Construction Type", "TEXT(255)"),
                new KeyValuePair<string, string>("Plan Description", "TEXT(255)"),
                new KeyValuePair<string, string>("HANDOVERED STATUS", "TEXT(255)"),
                new KeyValuePair<string, string>("Plan Handover", "TEXT(255)"),
                new KeyValuePair<string, string>("TOC (Lyna)", "TEXT(255)"),
                new KeyValuePair<string, string>("SPA Date", "TEXT(255)"),
                new KeyValuePair<string, string>("SPA (HO Date)", "TEXT(255)"),
                new KeyValuePair<string, string>("SPA  (HO Date)", "TEXT(255)"),
                new KeyValuePair<string, string>("SPA Duration", "TEXT(255)"),
                new KeyValuePair<string, string>("Duration GP", "TEXT(255)"),
                new KeyValuePair<string, string>("SPA+GP", "TEXT(255)"),
                new KeyValuePair<string, string>("Grace Period Date", "TEXT(255)"),
                new KeyValuePair<string, string>("End Date SPA+ GP", "TEXT(255)"),
                new KeyValuePair<string, string>("Priority Type", "TEXT(255)"),
                new KeyValuePair<string, string>("Priority", "TEXT(255)"),
                new KeyValuePair<string, string>("Priority build", "TEXT(255)"),
                new KeyValuePair<string, string>("House Priority", "TEXT(255)"),
                new KeyValuePair<string, string>("Plan Date", "TEXT(255)"),
                new KeyValuePair<string, string>("PlanDate", "TEXT(255)"),
                new KeyValuePair<string, string>("In Months", "TEXT(255)"),
                new KeyValuePair<string, string>("In Month", "TEXT(255)"),
                new KeyValuePair<string, string>("InMonths", "TEXT(255)")
            };

            try
            {
                using (var conn = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + boreyDbPath + ";Persist Security Info=False;"))
                {
                    conn.Open();
                    HashSet<string> existing = GetTableColumnSet(conn, physicalTableName);
                    foreach (KeyValuePair<string, string> col in requiredColumns)
                    {
                        if (existing.Contains(col.Key))
                        {
                            continue;
                        }

                        string sql = "ALTER TABLE " + QuoteAccessName(physicalTableName) +
                                     " ADD COLUMN " + QuoteAccessName(col.Key) + " " + col.Value;
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        existing.Add(col.Key);
                    }
                }

                _cachedBoreySchemaPath = boreyDbPath;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool ShouldRefreshBoreyFilterOptions(params System.Windows.Controls.ComboBox[] combos)
        {
            if (combos == null || combos.Length == 0)
            {
                return true;
            }

            foreach (System.Windows.Controls.ComboBox combo in combos)
            {
                if (combo == null || combo.Items.Count <= 1)
                {
                    return true;
                }
            }

            return false;
        }

        private DataTable GetOrLoadBoreyProjectedTable(string boreyDbPath, string physicalTableName, string logicalTableName)
        {
            string key = boreyDbPath + "|" + physicalTableName + "|" + logicalTableName;
            if (_boreyTableCache.TryGetValue(key, out DataTable cached) && cached != null)
            {
                return cached;
            }

            DataTable loaded = QueryBoreyProjectedTable(
                boreyDbPath,
                physicalTableName,
                logicalTableName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            _boreyTableCache[key] = loaded;
            return loaded;
        }

        private void RefreshBoreyFilterOptionsFromData(
            DataTable table,
            System.Windows.Controls.ComboBox zoneCombo,
            System.Windows.Controls.ComboBox blockCombo,
            System.Windows.Controls.ComboBox subBlockCombo,
            System.Windows.Controls.ComboBox houseTypeCombo,
            System.Windows.Controls.ComboBox houseCodeCombo,
            System.Windows.Controls.ComboBox tocCombo = null,
            string houseCodeLogicalColumn = "House Code",
            System.Windows.Controls.ComboBox handoverStatusCombo = null,
            string handoverStatusLogicalColumn = "HANDOVERED STATUS",
            System.Windows.Controls.ComboBox planDescriptionCombo = null,
            string planDescriptionLogicalColumn = "Plan Description",
            System.Windows.Controls.ComboBox subcontractorCombo = null,
            string subcontractorLogicalColumn = "SUBCONTRACTOR")
        {
            DataTable source = table ?? new DataTable();

            // 1) ZONE list comes from all rows.
            SetComboOptions(zoneCombo, FilterOutInfraStructureZoneValues(ExtractDistinctLogicalColumnValues(source, "ZONE")));
            string zone = GetSelectedFilterValue(zoneCombo);

            // 2) BLOCK list depends on selected ZONE.
            var blockScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ZONE", zone }
            };
            DataView blockView = BuildBoreyFilteredView(source, blockScope);
            SetComboOptions(blockCombo, ExtractDistinctLogicalColumnValues(blockView, "BLOCK"));
            string block = GetSelectedFilterValue(blockCombo);

            // 3) SUB-BLOCK list depends on selected ZONE + BLOCK.
            var subBlockScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ZONE", zone },
                { "BLOCK", block }
            };
            DataView subBlockView = BuildBoreyFilteredView(source, subBlockScope);
            SetComboOptions(subBlockCombo, ExtractDistinctLogicalColumnValues(subBlockView, "SUB-BLOCK"));
            string subBlock = GetSelectedFilterValue(subBlockCombo);

            // 4) HOUSE-TYPE list depends on ZONE + BLOCK + SUB-BLOCK.
            var houseTypeScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ZONE", zone },
                { "BLOCK", block },
                { "SUB-BLOCK", subBlock }
            };
            DataView houseTypeView = BuildBoreyFilteredView(source, houseTypeScope);
            SetComboOptions(houseTypeCombo, ExtractDistinctLogicalColumnValues(houseTypeView, "HOUSE-TYPE"));
            string houseType = GetSelectedFilterValue(houseTypeCombo);

            // 5) House list depends on ZONE + BLOCK + SUB-BLOCK + HOUSE-TYPE.
            var houseCodeScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ZONE", zone },
                { "BLOCK", block },
                { "SUB-BLOCK", subBlock },
                { "HOUSE-TYPE", houseType }
            };
            DataView houseCodeView = BuildBoreyFilteredView(source, houseCodeScope);
            SetComboOptions(houseCodeCombo, ExtractDistinctLogicalColumnValues(houseCodeView, houseCodeLogicalColumn));
            string houseCode = GetSelectedFilterValue(houseCodeCombo);
            string subcontractor = BoreyFilterAll;

            if (subcontractorCombo != null)
            {
                // 6) Subcontractor list depends on selected base filters including house id/code.
                var subcontractorScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ZONE", zone },
                    { "BLOCK", block },
                    { "SUB-BLOCK", subBlock },
                    { "HOUSE-TYPE", houseType },
                    { houseCodeLogicalColumn, houseCode }
                };
                DataView subcontractorView = BuildBoreyFilteredView(source, subcontractorScope);
                SetComboOptions(subcontractorCombo, ExtractDistinctLogicalColumnValues(subcontractorView, subcontractorLogicalColumn));
                subcontractor = GetSelectedFilterValue(subcontractorCombo);
            }

            if (handoverStatusCombo != null)
            {
                // 7) HANDOVER STATUS depends on all previous filters including house id/code.
                var statusScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ZONE", zone },
                    { "BLOCK", block },
                    { "SUB-BLOCK", subBlock },
                    { "HOUSE-TYPE", houseType },
                    { houseCodeLogicalColumn, houseCode }
                };
                if (!string.IsNullOrWhiteSpace(subcontractor) &&
                    !string.Equals(subcontractor, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    statusScope[subcontractorLogicalColumn] = subcontractor;
                }
                DataView statusView = BuildBoreyFilteredView(source, statusScope);
                List<string> handoverStatusOptions = ExtractDistinctLogicalColumnValues(statusView, handoverStatusLogicalColumn);
                if (ReferenceEquals(handoverStatusCombo, HandoverStatusFilterCombo))
                {
                    handoverStatusOptions = BoreyHandoverTabStatusFilterOptions.ToList();
                }

                SetComboOptions(handoverStatusCombo, handoverStatusOptions);
            }

            string handoverStatus = GetSelectedFilterValue(handoverStatusCombo);
            if (planDescriptionCombo != null)
            {
                var descriptionScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ZONE", zone },
                    { "BLOCK", block },
                    { "SUB-BLOCK", subBlock },
                    { "HOUSE-TYPE", houseType },
                    { houseCodeLogicalColumn, houseCode }
                };
                if (!string.IsNullOrWhiteSpace(subcontractor) &&
                    !string.Equals(subcontractor, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    descriptionScope[subcontractorLogicalColumn] = subcontractor;
                }
                if (!string.IsNullOrWhiteSpace(handoverStatus) &&
                    !string.Equals(handoverStatus, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    descriptionScope[handoverStatusLogicalColumn] = handoverStatus;
                }

                DataView descriptionView = BuildBoreyFilteredView(source, descriptionScope);
                SetComboOptions(planDescriptionCombo, ExtractDistinctLogicalColumnValues(descriptionView, planDescriptionLogicalColumn));
            }

            string planDescription = GetSelectedFilterValue(planDescriptionCombo);

            if (tocCombo != null)
            {
                var tocScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ZONE", zone },
                    { "BLOCK", block },
                    { "SUB-BLOCK", subBlock },
                    { "HOUSE-TYPE", houseType },
                    { houseCodeLogicalColumn, houseCode }
                };
                if (!string.IsNullOrWhiteSpace(subcontractor) &&
                    !string.Equals(subcontractor, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    tocScope[subcontractorLogicalColumn] = subcontractor;
                }

                if (!string.IsNullOrWhiteSpace(handoverStatus) &&
                    !string.Equals(handoverStatus, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    tocScope[handoverStatusLogicalColumn] = handoverStatus;
                }
                if (!string.IsNullOrWhiteSpace(planDescription) &&
                    !string.Equals(planDescription, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    tocScope[planDescriptionLogicalColumn] = planDescription;
                }

                DataView tocView = BuildBoreyFilteredView(source, tocScope);
                string tocColumn = ResolveTocColumnName(tocView?.Table);
                SetComboOptions(tocCombo, ExtractDistinctTocMonthEndValues(tocView, tocColumn));
            }
        }

        private static DataTable ApplyBoreyFiltersInMemory(DataTable source, IDictionary<string, string> filters)
        {
            if (source == null)
            {
                return new DataTable();
            }

            if (filters == null || filters.Count == 0)
            {
                return source.Copy();
            }

            var view = new DataView(source);
            var clauses = new List<string>();
            foreach (KeyValuePair<string, string> filter in filters)
            {
                string value = filter.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string resolvedColumn = ResolveFilterColumn(source, filter.Key);
                if (string.IsNullOrWhiteSpace(resolvedColumn))
                {
                    continue;
                }

                string escaped = value.Replace("'", "''");
                clauses.Add("[" + resolvedColumn + "] = '" + escaped + "'");
            }

            if (clauses.Count > 0)
            {
                view.RowFilter = string.Join(" AND ", clauses);
            }

            return view.ToTable();
        }

        private static DataView BuildBoreyFilteredView(DataTable source, IDictionary<string, string> filters)
        {
            if (source == null)
            {
                return new DataView(new DataTable());
            }

            var view = new DataView(source);
            if (filters == null || filters.Count == 0)
            {
                return FilterOutInfraStructureRows(source);
            }

            string tocFilter = "";
            bool hasTocFilter = false;
            string tocColumn = ResolveTocColumnName(source);
            if (TryGetTocFilterValue(filters, out string tocRaw))
            {
                tocFilter = (tocRaw ?? "").Trim();
                hasTocFilter = !string.IsNullOrWhiteSpace(tocFilter) &&
                               !string.Equals(tocFilter, BoreyFilterAll, StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrWhiteSpace(tocColumn);
            }

            if (hasTocFilter)
            {
                DataTable filteredTable = source.Clone();
                foreach (DataRow row in source.Rows)
                {
                    if (row == null || row.RowState == DataRowState.Deleted)
                    {
                        continue;
                    }

                    bool match = true;
                    foreach (KeyValuePair<string, string> filter in filters)
                    {
                        string key = filter.Key ?? "";
                        string value = filter.Value?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (IsTocColumnName(key))
                        {
                            continue;
                        }

                        string resolvedColumn = ResolveFilterColumn(source, key);
                        if (string.IsNullOrWhiteSpace(resolvedColumn))
                        {
                            continue;
                        }

                        string rowValue = Convert.ToString(row[resolvedColumn], CultureInfo.InvariantCulture)?.Trim() ?? "";
                        if (!string.Equals(rowValue, value, StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }

                    if (!match)
                    {
                        continue;
                    }

                    string rowToc = string.IsNullOrWhiteSpace(tocColumn)
                        ? ""
                        : Convert.ToString(row[tocColumn], CultureInfo.InvariantCulture)?.Trim() ?? "";
                    if (!MatchesTocMonthFilter(rowToc, tocFilter))
                    {
                        continue;
                    }

                    filteredTable.ImportRow(row);
                }

                return FilterOutInfraStructureRows(filteredTable);
            }

            var clauses = new List<string>();
            foreach (KeyValuePair<string, string> filter in filters)
            {
                string value = filter.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string resolvedColumn = ResolveFilterColumn(source, filter.Key);
                if (string.IsNullOrWhiteSpace(resolvedColumn))
                {
                    continue;
                }

                string escaped = value.Replace("'", "''");
                clauses.Add("[" + resolvedColumn + "] = '" + escaped + "'");
            }

            if (clauses.Count > 0)
            {
                view.RowFilter = string.Join(" AND ", clauses);
            }

            return FilterOutInfraStructureRows(view.ToTable());
        }

        private void RefreshBoreyFilterOptions(
            string boreyDbPath,
            string tableName,
            System.Windows.Controls.ComboBox zoneCombo,
            System.Windows.Controls.ComboBox blockCombo,
            System.Windows.Controls.ComboBox subBlockCombo,
            System.Windows.Controls.ComboBox houseTypeCombo,
            System.Windows.Controls.ComboBox houseCodeCombo)
        {
            using (var conn = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + boreyDbPath + ";Persist Security Info=False;"))
            {
                conn.Open();
                HashSet<string> columns = GetTableColumnSet(conn, tableName);

                SetComboOptions(zoneCombo, FilterOutInfraStructureZoneValues(LoadDistinctValues(conn, tableName, ResolveFilterColumn(columns, "ZONE"))));
                SetComboOptions(blockCombo, LoadDistinctValues(conn, tableName, ResolveFilterColumn(columns, "BLOCK")));
                SetComboOptions(subBlockCombo, LoadDistinctValues(conn, tableName, ResolveFilterColumn(columns, "SUB-BLOCK")));
                SetComboOptions(houseTypeCombo, LoadDistinctValues(conn, tableName, ResolveFilterColumn(columns, "HOUSE-TYPE")));
                SetComboOptions(houseCodeCombo, LoadDistinctValues(conn, tableName, ResolveFilterColumn(columns, "House Code")));
            }
        }

        private static DataTable QueryBoreyTable(string boreyDbPath, string tableName, IDictionary<string, string> filters)
        {
            var result = new DataTable();
            using (var conn = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + boreyDbPath + ";Persist Security Info=False;"))
            {
                conn.Open();
                HashSet<string> columns = GetTableColumnSet(conn, tableName);

                var sql = new StringBuilder();
                sql.Append("SELECT * FROM [").Append(tableName).Append("]");

                var clauses = new List<string>();
                using (var cmd = new OleDbCommand())
                {
                    cmd.Connection = conn;

                    foreach (KeyValuePair<string, string> filter in filters)
                    {
                        string value = filter.Value?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string physicalColumn = ResolveFilterColumn(columns, filter.Key);
                        if (string.IsNullOrWhiteSpace(physicalColumn))
                        {
                            continue;
                        }

                        clauses.Add("[" + physicalColumn + "] = ?");
                        cmd.Parameters.AddWithValue("@p", value);
                    }

                    if (clauses.Count > 0)
                    {
                        sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
                    }

                    cmd.CommandText = sql.ToString();
                    using (var adapter = new OleDbDataAdapter(cmd))
                    {
                        adapter.Fill(result);
                    }
                }
            }

            return result;
        }

        private static DataTable QueryBoreyProjectedTable(
            string boreyDbPath,
            string physicalTableName,
            string logicalTableName,
            IDictionary<string, string> filters)
        {
            var result = new DataTable();
            using (var conn = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=" + boreyDbPath + ";Persist Security Info=False;"))
            {
                conn.Open();
                HashSet<string> available = GetTableColumnSet(conn, physicalTableName);
                List<string> logicalColumns = GetBoreyLogicalColumns(logicalTableName);

                var selectList = new List<string>();
                var addedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string logical in logicalColumns)
                {
                    string physical = ResolveFilterColumn(available, logical);
                    if (string.IsNullOrWhiteSpace(physical))
                    {
                        continue;
                    }

                    if (addedAliases.Contains(logical))
                    {
                        continue;
                    }

                    selectList.Add("[" + physical + "] AS [" + logical + "]");
                    addedAliases.Add(logical);
                }

                if (selectList.Count == 0)
                {
                    // No mapped physical columns: return an empty logical-shaped table
                    // so users can still paste data for this sub-tab.
                    foreach (string logical in logicalColumns)
                    {
                        if (!result.Columns.Contains(logical))
                        {
                            result.Columns.Add(logical, typeof(string));
                        }
                    }

                    return result;
                }

                var sql = new StringBuilder();
                sql.Append("SELECT ").Append(string.Join(", ", selectList))
                   .Append(" FROM [").Append(physicalTableName).Append("]");
                using (var cmd = new OleDbCommand())
                {
                    cmd.Connection = conn;
                    var clauses = new List<string>();

                    foreach (KeyValuePair<string, string> filter in filters ?? new Dictionary<string, string>())
                    {
                        string value = filter.Value?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string physicalFilterColumn = ResolveFilterColumn(available, filter.Key);
                        if (string.IsNullOrWhiteSpace(physicalFilterColumn))
                        {
                            continue;
                        }

                        clauses.Add("[" + physicalFilterColumn + "] = ?");
                        cmd.Parameters.AddWithValue("@p", value);
                    }

                    if (clauses.Count > 0)
                    {
                        sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
                    }

                    cmd.CommandText = sql.ToString();
                    using (var adapter = new OleDbDataAdapter(cmd))
                    {
                        adapter.Fill(result);
                    }
                }
            }

            return result;
        }

        private static List<string> GetBoreyLogicalColumns(string logicalTableName)
        {
            return CBIM_BOREY.GetLogicalColumns(logicalTableName ?? "Project_Info")
                .ToList();
        }

        private static string ResolveBoreyDbPath()
        {
            foreach (string candidate in BoreyDbCandidatePaths)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "";
        }

        private static string ResolveBoreyDbPathForTable(string tableName, out string physicalTableName)
        {
            physicalTableName = "";
            foreach (string candidate in BoreyDbCandidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                {
                    continue;
                }

                if (TableExistsInAccessDb(candidate, tableName))
                {
                    physicalTableName = tableName;
                    return candidate;
                }

                if (TableExistsInAccessDb(candidate, BoreyFallbackPhysicalTable))
                {
                    physicalTableName = BoreyFallbackPhysicalTable;
                    return candidate;
                }
            }

            return "";
        }

        private string ResolveBoreyDbPathForTableCached(string tableName, out string physicalTableName)
        {
            physicalTableName = "";

            if (!string.IsNullOrWhiteSpace(_cachedBoreyDbPath) &&
                File.Exists(_cachedBoreyDbPath) &&
                !string.IsNullOrWhiteSpace(_cachedBoreyPhysicalTable))
            {
                // Re-resolve physical table per logical tab in cached DB.
                if (TableExistsInAccessDb(_cachedBoreyDbPath, tableName))
                {
                    physicalTableName = tableName;
                    _cachedBoreyPhysicalTable = physicalTableName;
                    return _cachedBoreyDbPath;
                }

                if (TableExistsInAccessDb(_cachedBoreyDbPath, BoreyFallbackPhysicalTable))
                {
                    physicalTableName = BoreyFallbackPhysicalTable;
                    _cachedBoreyPhysicalTable = physicalTableName;
                    return _cachedBoreyDbPath;
                }
            }

            string resolvedPath = ResolveBoreyDbPathForTable(tableName, out string resolvedPhysical);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && !string.IsNullOrWhiteSpace(resolvedPhysical))
            {
                _cachedBoreyDbPath = resolvedPath;
                _cachedBoreyPhysicalTable = resolvedPhysical;
                physicalTableName = resolvedPhysical;
                return resolvedPath;
            }

            return "";
        }

        private void OnBoreyGridPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is DataGrid grid))
            {
                return;
            }

            if ((string.Equals(grid.Name, "ProjectInfoDataGrid", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(grid.Name, "ProjectStatusDataGrid", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(grid.Name, "PlanVsActualPlanDataGrid", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(grid.Name, "PlanVsActualActualDataGrid", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(grid.Name, "PlanVsActualRemainingDataGrid", StringComparison.OrdinalIgnoreCase)) &&
                e.Key == Key.V &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowStatus("BOREY: summary view is read-only. Paste is disabled for this tab.");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (TryBuildBoreyClipboardPayload(grid, out string tsv, out int rowCount, out int colCount))
                {
                    Clipboard.SetDataObject(tsv, true);
                    ShowStatus($"BOREY: copied {rowCount} row(s) x {colCount} column(s) to clipboard.");
                }
                else
                {
                    ShowStatus("BOREY: nothing selected to copy.");
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                try
                {
                    int pasted = PasteClipboardRowsToBoreyGrid(grid);
                    if (pasted > 0)
                    {
                        if (string.Equals(grid.Name, "PlaningInfoDataGrid", StringComparison.OrdinalIgnoreCase) &&
                            grid.ItemsSource is DataView planView)
                        {
                            UpdateBoreyProjectOverallDashboard(planView, "Planing_Info");
                        }

                        ShowStatus($"BOREY: pasted {pasted} row(s). Click Generate to save to DB.");
                    }
                    else
                    {
                        ShowStatus("BOREY: clipboard has no table rows to paste.");
                    }
                }
                catch (Exception ex)
                {
                    ShowStatus("BOREY paste failed: " + ex.Message);
                }

                e.Handled = true;
            }
        }

        private int PasteClipboardRowsToBoreyGrid(DataGrid grid)
        {
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string[] rawLines = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (rawLines.Length == 0)
            {
                return 0;
            }

            DataTable table = EnsureBoreyInputTable(grid);
            if (table.Columns.Count == 0)
            {
                return 0;
            }

            List<string[]> parsedLines = rawLines
                .Select(SplitClipboardLine)
                .ToList();

            // Single-column paste mode:
            // copy one Excel column and paste into currently selected DataGrid column.
            if (parsedLines.Count > 0 && parsedLines.All(cells => cells.Length <= 1))
            {
                int targetColumnIndex = GetBoreyPasteTargetColumnIndex(grid, table);
                if (targetColumnIndex >= 0)
                {
                    return PasteSingleColumnToBoreyGrid(grid, table, parsedLines, targetColumnIndex);
                }
            }

            string[] firstCells = SplitClipboardLine(rawLines[0]);
            Dictionary<int, int> mapping = BuildPasteColumnMapping(table, firstCells, out bool hasHeaderRow);

            int startIndex = hasHeaderRow ? 1 : 0;
            int inserted = 0;
            for (int i = startIndex; i < rawLines.Length; i++)
            {
                string[] cells = SplitClipboardLine(rawLines[i]);
                if (cells.Length == 0)
                {
                    continue;
                }

                DataRow row = table.NewRow();
                bool hasValue = false;
                foreach (KeyValuePair<int, int> pair in mapping)
                {
                    int sourceIndex = pair.Key;
                    int targetIndex = pair.Value;
                    if (sourceIndex < 0 || sourceIndex >= cells.Length || targetIndex < 0 || targetIndex >= table.Columns.Count)
                    {
                        continue;
                    }

                    string value = cells[sourceIndex]?.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    row[targetIndex] = value;
                    hasValue = true;
                }

                if (hasValue)
                {
                    table.Rows.Add(row);
                    inserted++;
                }
            }

            return inserted;
        }

        private static int PasteSingleColumnToBoreyGrid(
            DataGrid grid,
            DataTable table,
            List<string[]> parsedLines,
            int targetColumnIndex)
        {
            if (grid == null || table == null || parsedLines == null || parsedLines.Count == 0 || targetColumnIndex < 0 || targetColumnIndex >= table.Columns.Count)
            {
                return 0;
            }

            int startRowIndex = GetBoreyPasteStartRowIndex(grid, table);
            if (startRowIndex < 0)
            {
                startRowIndex = 0;
            }

            int startLine = 0;
            string firstValue = parsedLines[0].Length > 0 ? (parsedLines[0][0] ?? "").Trim() : "";
            string targetColumnName = table.Columns[targetColumnIndex].ColumnName ?? "";
            if (!string.IsNullOrWhiteSpace(firstValue) &&
                string.Equals(firstValue, targetColumnName, StringComparison.OrdinalIgnoreCase))
            {
                startLine = 1;
            }

            int pasted = 0;
            for (int i = startLine; i < parsedLines.Count; i++)
            {
                string value = parsedLines[i].Length > 0 ? parsedLines[i][0] : "";
                int rowIndex = startRowIndex + (i - startLine);
                while (table.Rows.Count <= rowIndex)
                {
                    table.Rows.Add(table.NewRow());
                }

                table.Rows[rowIndex][targetColumnIndex] = (value ?? "").Trim();
                pasted++;
            }

            return pasted;
        }

        private bool TryBuildBoreyClipboardPayload(DataGrid grid, out string tsv, out int rowCount, out int colCount)
        {
            tsv = "";
            rowCount = 0;
            colCount = 0;

            if (grid == null || !(grid.ItemsSource is DataView view) || view.Table == null)
            {
                return false;
            }

            DataTable table = view.Table;
            var selectedCells = grid.SelectedCells?
                .Where(c => c.Item is DataRowView && c.Column != null)
                .ToList() ?? new List<DataGridCellInfo>();

            if (selectedCells.Count > 0)
            {
                var viewRows = view.Cast<DataRowView>().ToList();
                var rowIndexMap = new Dictionary<DataRow, int>();
                for (int i = 0; i < viewRows.Count; i++)
                {
                    if (viewRows[i]?.Row != null)
                    {
                        rowIndexMap[viewRows[i].Row] = i;
                    }
                }

                var selectedRowIndexes = new SortedSet<int>();
                var selectedColumnDisplayIndexes = new SortedSet<int>();
                var selectedCellSet = new HashSet<(int Row, int Col)>();
                foreach (DataGridCellInfo cell in selectedCells)
                {
                    if (!(cell.Item is DataRowView rowView) || rowView.Row == null || !rowIndexMap.TryGetValue(rowView.Row, out int rowIndex))
                    {
                        continue;
                    }

                    int colIndex = cell.Column.DisplayIndex;
                    selectedRowIndexes.Add(rowIndex);
                    selectedColumnDisplayIndexes.Add(colIndex);
                    selectedCellSet.Add((rowIndex, colIndex));
                }

                var orderedColumns = grid.Columns
                    .Where(c => c != null &&
                                c.Visibility == System.Windows.Visibility.Visible &&
                                selectedColumnDisplayIndexes.Contains(c.DisplayIndex))
                    .OrderBy(c => c.DisplayIndex)
                    .Select(c => new
                    {
                        Column = c,
                        Name = ResolveBoreyGridColumnName(table, c),
                        Header = Convert.ToString(c.Header, CultureInfo.InvariantCulture)?.Trim() ?? ""
                    })
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .ToList();

                if (selectedRowIndexes.Count > 0 && orderedColumns.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join("\t", orderedColumns.Select(c => EscapeForTsv(c.Header))));

                    foreach (int rowIndex in selectedRowIndexes)
                    {
                        if (rowIndex < 0 || rowIndex >= viewRows.Count)
                        {
                            continue;
                        }

                        DataRow row = viewRows[rowIndex].Row;
                        var values = new List<string>(orderedColumns.Count);
                        foreach (var col in orderedColumns)
                        {
                            string cellValue = selectedCellSet.Contains((rowIndex, col.Column.DisplayIndex))
                                ? EscapeForTsv(Convert.ToString(row[col.Name], CultureInfo.InvariantCulture) ?? "")
                                : "";
                            values.Add(cellValue);
                        }

                        sb.AppendLine(string.Join("\t", values));
                    }

                    tsv = sb.ToString();
                    rowCount = selectedRowIndexes.Count;
                    colCount = orderedColumns.Count;
                    return rowCount > 0 && colCount > 0;
                }
            }

            List<DataRowView> selectedRows = grid.SelectedItems
                .OfType<DataRowView>()
                .Where(r => r?.Row != null)
                .ToList();
            if (selectedRows.Count == 0 && grid.SelectedItem is DataRowView one && one.Row != null)
            {
                selectedRows.Add(one);
            }

            if (selectedRows.Count == 0)
            {
                return false;
            }

            List<(string ColumnName, string Header)> columns = GetBoreyExportColumns(grid, table);
            if (columns.Count == 0)
            {
                return false;
            }

            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join("\t", columns.Select(c => EscapeForTsv(c.Header))));
                foreach (DataRowView rowView in selectedRows)
                {
                    var values = new List<string>(columns.Count);
                    foreach (var (ColumnName, Header) in columns)
                    {
                        values.Add(EscapeForTsv(Convert.ToString(rowView.Row[ColumnName], CultureInfo.InvariantCulture) ?? ""));
                    }

                    sb.AppendLine(string.Join("\t", values));
                }

                tsv = sb.ToString();
                rowCount = selectedRows.Count;
                colCount = columns.Count;
                return rowCount > 0 && colCount > 0;
            }
        }

        private static string ResolveBoreyGridColumnName(DataTable table, DataGridColumn column)
        {
            if (table == null || column == null)
            {
                return "";
            }

            string header = Convert.ToString(column.Header, CultureInfo.InvariantCulture)?.Trim() ?? "";
            string exact = FindDataTableColumnName(table, header);
            if (!string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }

            string sortMember = (column.SortMemberPath ?? "").Trim();
            exact = FindDataTableColumnName(table, sortMember);
            if (!string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }

            return "";
        }

        private static int GetBoreyPasteTargetColumnIndex(DataGrid grid, DataTable table)
        {
            if (grid == null || table == null || table.Columns.Count == 0)
            {
                return -1;
            }

            DataGridColumn column = grid.CurrentCell.Column;
            if (column == null && grid.SelectedCells != null && grid.SelectedCells.Count > 0)
            {
                column = grid.SelectedCells[0].Column;
            }

            if (column == null)
            {
                return 0;
            }

            string header = Convert.ToString(column.Header, CultureInfo.InvariantCulture)?.Trim();
            if (!string.IsNullOrWhiteSpace(header))
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    if (string.Equals(table.Columns[i].ColumnName, header, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            int displayIndex = column.DisplayIndex;
            if (displayIndex >= 0 && displayIndex < table.Columns.Count)
            {
                return displayIndex;
            }

            return -1;
        }

        private static int GetBoreyPasteStartRowIndex(DataGrid grid, DataTable table)
        {
            if (grid == null || table == null)
            {
                return 0;
            }

            if (grid.CurrentCell.Item is DataRowView currentView)
            {
                int idx = table.Rows.IndexOf(currentView.Row);
                if (idx >= 0)
                {
                    return idx;
                }
            }

            if (grid.SelectedCells != null && grid.SelectedCells.Count > 0 && grid.SelectedCells[0].Item is DataRowView selectedView)
            {
                int idx = table.Rows.IndexOf(selectedView.Row);
                if (idx >= 0)
                {
                    return idx;
                }
            }

            return 0;
        }

        private DataTable EnsureBoreyInputTable(DataGrid grid)
        {
            if (grid?.ItemsSource is DataView view && view.Table != null)
            {
                return view.Table;
            }

            string logicalTable = GetBoreyLogicalTableForGrid(grid);
            var table = new DataTable(logicalTable);
            foreach (string columnName in GetBoreyDefaultInputColumns(logicalTable))
            {
                if (!table.Columns.Contains(columnName))
                {
                    table.Columns.Add(columnName, typeof(string));
                }
            }

            grid.ItemsSource = table.DefaultView;
            return table;
        }

        private static string GetBoreyLogicalTableForGrid(DataGrid grid)
        {
            if (grid == null) return "Project_Info";
            if (string.Equals(grid.Name, "SaleInfoDataGrid", StringComparison.OrdinalIgnoreCase)) return "Sale_Info";
            if (string.Equals(grid.Name, "SiteInfoDataGrid", StringComparison.OrdinalIgnoreCase)) return "Site_Info";
            if (string.Equals(grid.Name, "PlaningInfoDataGrid", StringComparison.OrdinalIgnoreCase)) return "Planing_Info";
            if (string.Equals(grid.Name, "HandoverDataGrid", StringComparison.OrdinalIgnoreCase)) return "Planing_Info";
            if (string.Equals(grid.Name, "PlanVsActualPlanDataGrid", StringComparison.OrdinalIgnoreCase)) return "Planing_Info";
            if (string.Equals(grid.Name, "PlanVsActualActualDataGrid", StringComparison.OrdinalIgnoreCase)) return "Planing_Info";
            if (string.Equals(grid.Name, "PlanVsActualRemainingDataGrid", StringComparison.OrdinalIgnoreCase)) return "Planing_Info";
            if (string.Equals(grid.Name, "SubcontractorDataGrid", StringComparison.OrdinalIgnoreCase)) return "Project_Info";
            if (string.Equals(grid.Name, "ContractLoaBlcDataGrid", StringComparison.OrdinalIgnoreCase)) return "Project_Info";
            return "Project_Info";
        }

        private static List<string> GetBoreyDefaultInputColumns(string logicalTable)
        {
            return CBIM_BOREY.GetLogicalColumns(logicalTable ?? "Project_Info")
                .ToList();
        }

        private void OnClearBoreyFiltersClick(object sender, RoutedEventArgs e)
        {
            if (!IsBoreyTab())
            {
                ShowStatus("Clear Filters is currently available in BOREY tab.");
                return;
            }

            if (!(BoreySubTabControl?.SelectedItem is TabItem selectedTab))
            {
                ShowStatus("BOREY: select a sub-tab first.");
                return;
            }

            string header = selectedTab.Header?.ToString() ?? "";
            if (!TryGetBoreySubTabContext(
                header,
                out string _,
                out System.Windows.Controls.ComboBox zoneCombo,
                out System.Windows.Controls.ComboBox blockCombo,
                out System.Windows.Controls.ComboBox subBlockCombo,
                out System.Windows.Controls.ComboBox houseTypeCombo,
                out System.Windows.Controls.ComboBox houseCodeCombo,
                out DataGrid _))
            {
                ShowStatus("BOREY: unknown sub-tab.");
                return;
            }

            var combos = new List<System.Windows.Controls.ComboBox>();
            AddBoreyFilterCombo(combos, zoneCombo);
            AddBoreyFilterCombo(combos, blockCombo);
            AddBoreyFilterCombo(combos, subBlockCombo);
            AddBoreyFilterCombo(combos, houseTypeCombo);
            AddBoreyFilterCombo(combos, houseCodeCombo);

            if (string.Equals(header, "ProjectInfo", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("ProjectInfoHandoverStatusFilterCombo"));
            }
            else if (string.Equals(header, "ProjectStatus", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "Project Status", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("ProjectStatusHandoverStatusFilterCombo"));
            }
            else if (string.Equals(header, "SaleInfo", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("SaleInfoHandoverStatusFilterCombo"));
            }
            else if (string.Equals(header, "SiteInfo", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("SiteInfoHandoverStatusFilterCombo"));
            }
            else if (string.Equals(header, "Monthly Plan", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "PlaningInfo", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("PlaningInfoHandoverStatusFilterCombo"));
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("PlaningInfoTocFilterCombo"));
            }
            else if (string.Equals(header, "HANDOVER", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, HandoverStatusFilterCombo);
            }
            else if (string.Equals(header, "PLAN VS ACTUAL TO REMAINNING", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "PLAN VS ACTUAL TO REMAINING", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "Plan Vs Actual To Remainning", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("PlanVsActualProjectStatusFilterCombo"));
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("PlanVsActualPlanDescriptionFilterCombo"));
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("PlanVsActualTargetCompleteFilterCombo"));
            }
            else if (string.Equals(header, "SUBCONTRACTOR", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("SubcontractorNameFilterCombo"));
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("SubcontractorHandoverStatusFilterCombo"));
            }
            else if (string.Equals(header, "CONTRACT/LOA/BLC No.", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "CONTRACT/LOA/BLC No", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "CONTRACT/LOA/BLC NO.", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "CONTRACT/LOA/BLC NO", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "Contract/LOA/BLC No.", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(header, "Contract/LOA/BLC No", StringComparison.OrdinalIgnoreCase))
            {
                AddBoreyFilterCombo(combos, GetBoreyNamedCombo("ContractLoaBlcHandoverStatusFilterCombo"));
            }

            _boreyUiSyncInProgress = true;
            try
            {
                foreach (System.Windows.Controls.ComboBox combo in combos)
                {
                    SetBoreyFilterComboToAll(combo);
                }
            }
            finally
            {
                _boreyUiSyncInProgress = false;
            }

            ApplyCurrentBoreyRevitView(refreshFilterOptions: true);
            ShowStatus("BOREY: all filters reset to All.");
        }

        private static void AddBoreyFilterCombo(
            List<System.Windows.Controls.ComboBox> combos,
            System.Windows.Controls.ComboBox combo)
        {
            if (combos == null || combo == null)
            {
                return;
            }

            if (combos.Contains(combo))
            {
                return;
            }

            combos.Add(combo);
        }

        private static void SetBoreyFilterComboToAll(System.Windows.Controls.ComboBox combo)
        {
            if (combo == null)
            {
                return;
            }

            if (combo.Items != null && combo.Items.Count > 0)
            {
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    object item = combo.Items[i];
                    string text = (item as string) ?? Convert.ToString(item, CultureInfo.InvariantCulture) ?? "";
                    if (string.Equals(text.Trim(), BoreyFilterAll, StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedIndex = i;
                        return;
                    }
                }

                combo.SelectedIndex = 0;
                return;
            }

            combo.Text = BoreyFilterAll;
        }

        private static List<BoreyGridRowPayload> ReadBoreyRowsFromFile(
            string path,
            out int importedSheets,
            out int scannedRows,
            out int skippedNoKeyRows)
        {
            importedSheets = 0;
            scannedRows = 0;
            skippedNoKeyRows = 0;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new List<BoreyGridRowPayload>();
            }

            string ext = Path.GetExtension(path) ?? "";
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                importedSheets = 1;
                return ReadBoreyRowsFromCsv(path, ref scannedRows, ref skippedNoKeyRows);
            }

            return ReadBoreyRowsFromExcel(path, out importedSheets, ref scannedRows, ref skippedNoKeyRows);
        }

        private static List<BoreyGridRowPayload> ReadBoreyRowsFromCsv(
            string path,
            ref int scannedRows,
            ref int skippedNoKeyRows)
        {
            var result = new List<BoreyGridRowPayload>();
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                return result;
            }

            int headerLineIndex = FindBoreyHeaderLineIndex(lines);

            if (headerLineIndex < 0)
            {
                return result;
            }

            List<string> headers = ParseCsvLine(lines[headerLineIndex])
                .Select(h => NormalizeImportedHeaderCell((h ?? "").Trim()))
                .ToList();
            if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            {
                return result;
            }

            for (int i = headerLineIndex + 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string[] cells = ParseCsvLine(lines[i]).ToArray();
                if (IsLikelyBoreyDescriptorRow(cells))
                {
                    skippedNoKeyRows++;
                    continue;
                }

                BoreyGridRowPayload payload = BuildBoreyPayloadFromCells(headers, cells);
                if (payload == null)
                {
                    continue;
                }

                scannedRows++;
                if (!HasBoreyPayloadMatchKey(payload.Values))
                {
                    skippedNoKeyRows++;
                    continue;
                }

                result.Add(payload);
            }

            return result;
        }

        private static int FindBoreyHeaderLineIndex(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return -1;
            }

            int scanMax = Math.Min(lines.Count - 1, 80);
            int bestLine = -1;
            int bestScore = int.MinValue;

            for (int i = 0; i <= scanMax; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<string> headers = ParseCsvLine(line)
                    .Select(h => NormalizeImportedHeaderCell((h ?? "").Trim()))
                    .ToList();

                int nonEmpty = headers.Count(h => !string.IsNullOrWhiteSpace(h));
                if (nonEmpty == 0)
                {
                    continue;
                }

                int knownHeaders = headers.Count(IsKnownBoreyHeader);
                int keyHeaders = headers.Count(h => BoreyImportMatchKeyColumns.Any(k => string.Equals(k, h, StringComparison.OrdinalIgnoreCase)));
                int score = (keyHeaders * 120) + (knownHeaders * 25) + Math.Min(nonEmpty, 20);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestLine = i;
                }
            }

            if (bestLine >= 0)
            {
                return bestLine;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<BoreyGridRowPayload> ReadBoreyRowsFromExcel(
            string path,
            out int importedSheets,
            ref int scannedRows,
            ref int skippedNoKeyRows)
        {
            importedSheets = 0;
            var result = new List<BoreyGridRowPayload>();

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

                            int before = result.Count;
                            AppendBoreyRowsFromExcelMatrix(values, result, ref scannedRows, ref skippedNoKeyRows);
                            if (result.Count > before)
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

        private static void AppendBoreyRowsFromExcelMatrix(
            object[,] values,
            IList<BoreyGridRowPayload> target,
            ref int scannedRows,
            ref int skippedNoKeyRows)
        {
            if (values == null || target == null)
            {
                return;
            }

            int rMin = values.GetLowerBound(0);
            int rMax = values.GetUpperBound(0);
            int cMin = values.GetLowerBound(1);
            int cMax = values.GetUpperBound(1);
            if (rMax < rMin || cMax < cMin)
            {
                return;
            }

            int headerRow = FindBoreyHeaderRow(values, rMin, rMax, cMin, cMax);

            if (headerRow < rMin)
            {
                return;
            }

            var headers = new List<string>();
            for (int c = cMin; c <= cMax; c++)
            {
                string normalized = NormalizeImportedHeaderCell((ToCellString(values[headerRow, c]) ?? "").Trim());
                headers.Add(normalized);
            }

            if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            {
                return;
            }

            for (int r = headerRow + 1; r <= rMax; r++)
            {
                var cells = new string[headers.Count];
                for (int c = cMin; c <= cMax; c++)
                {
                    cells[c - cMin] = ToCellString(values[r, c]);
                }

                if (IsLikelyBoreyDescriptorRow(cells))
                {
                    skippedNoKeyRows++;
                    continue;
                }

                BoreyGridRowPayload payload = BuildBoreyPayloadFromCells(headers, cells);
                if (payload == null)
                {
                    continue;
                }

                scannedRows++;
                if (!HasBoreyPayloadMatchKey(payload.Values))
                {
                    skippedNoKeyRows++;
                    continue;
                }

                target.Add(payload);
            }
        }

        private static int FindBoreyHeaderRow(object[,] values, int rMin, int rMax, int cMin, int cMax)
        {
            int scanMax = Math.Min(rMax, rMin + 80);
            int bestRow = -1;
            int bestScore = int.MinValue;

            for (int r = rMin; r <= scanMax; r++)
            {
                int nonEmpty = 0;
                int knownHeaders = 0;
                int keyHeaders = 0;

                for (int c = cMin; c <= cMax; c++)
                {
                    string header = NormalizeImportedHeaderCell((ToCellString(values[r, c]) ?? "").Trim());
                    if (string.IsNullOrWhiteSpace(header))
                    {
                        continue;
                    }

                    nonEmpty++;
                    if (IsKnownBoreyHeader(header))
                    {
                        knownHeaders++;
                    }
                    if (BoreyImportMatchKeyColumns.Any(k => string.Equals(k, header, StringComparison.OrdinalIgnoreCase)))
                    {
                        keyHeaders++;
                    }
                }

                if (nonEmpty == 0)
                {
                    continue;
                }

                int score = (keyHeaders * 120) + (knownHeaders * 25) + Math.Min(nonEmpty, 20);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRow = r;
                }
            }

            if (bestRow >= 0)
            {
                return bestRow;
            }

            for (int r = rMin; r <= scanMax; r++)
            {
                int nonEmpty = 0;
                for (int c = cMin; c <= cMax; c++)
                {
                    if (!string.IsNullOrWhiteSpace(ToCellString(values[r, c])))
                    {
                        nonEmpty++;
                    }
                }

                if (nonEmpty > 1)
                {
                    return r;
                }
            }

            return -1;
        }

        private static bool IsKnownBoreyHeader(string header)
        {
            string text = (header ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (BoreyFilterColumnAliases.ContainsKey(text))
            {
                return true;
            }

            foreach (string[] aliases in BoreyFilterColumnAliases.Values)
            {
                if (aliases == null)
                {
                    continue;
                }

                foreach (string alias in aliases)
                {
                    if (string.Equals(alias, text, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsLikelyBoreyDescriptorRow(IReadOnlyList<string> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                return false;
            }

            int nonEmpty = 0;
            int descriptorHits = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                string value = (cells[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                nonEmpty++;
                if (IsBoreyDescriptorToken(value))
                {
                    descriptorHits++;
                }
            }

            if (nonEmpty < 3)
            {
                return false;
            }

            return descriptorHits >= Math.Max(2, nonEmpty / 2);
        }

        private static bool IsBoreyDescriptorToken(string value)
        {
            string token = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token.StartsWith("{", StringComparison.Ordinal) && token.EndsWith("}", StringComparison.Ordinal))
            {
                return true;
            }

            switch (token.ToLowerInvariant())
            {
                case "string":
                case "double":
                case "integer":
                case "instance":
                case "type":
                case "read-only":
                case "readonly":
                case "custom parameter":
                case "text":
                case "data":
                case "construction":
                case "constraints":
                    return true;
                default:
                    return false;
            }
        }

        private static BoreyGridRowPayload BuildBoreyPayloadFromCells(IReadOnlyList<string> headers, IReadOnlyList<string> cells)
        {
            if (headers == null || headers.Count == 0 || cells == null || cells.Count == 0)
            {
                return null;
            }

            var payload = new BoreyGridRowPayload();
            bool hasData = false;
            int count = Math.Min(headers.Count, cells.Count);
            for (int i = 0; i < count; i++)
            {
                string header = (headers[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                string value = (cells[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                payload.Values[header] = value;
                hasData = true;
            }

            return hasData ? payload : null;
        }

        private static bool HasBoreyPayloadMatchKey(IDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return false;
            }

            foreach (string key in BoreyImportMatchKeyColumns)
            {
                if (TryGetBoreyPayloadValue(values, key, out string value) && !string.IsNullOrWhiteSpace(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetBoreyPayloadValue(IDictionary<string, string> values, string logicalName, out string value)
        {
            value = "";
            if (values == null || string.IsNullOrWhiteSpace(logicalName))
            {
                return false;
            }

            if (values.TryGetValue(logicalName, out string direct))
            {
                value = (direct ?? "").Trim();
                return true;
            }

            if (BoreyFilterColumnAliases.TryGetValue(logicalName, out string[] aliases))
            {
                foreach (string alias in aliases)
                {
                    if (values.TryGetValue(alias, out string aliasValue))
                    {
                        value = (aliasValue ?? "").Trim();
                        return true;
                    }
                }
            }

            if (string.Equals(logicalName, "Id", StringComparison.OrdinalIgnoreCase) &&
                (values.TryGetValue("ID", out string idValue) ||
                 values.TryGetValue("Element ID", out idValue) ||
                 values.TryGetValue("__ELEMENT_ID__", out idValue)))
            {
                value = (idValue ?? "").Trim();
                return true;
            }

            return false;
        }

        private void ExportBoreyViewToExcel(DataGrid grid, DataView view, string logicalTable, string filePath)
        {
            if (view == null || view.Table == null)
            {
                throw new InvalidOperationException("No Borey table data to export.");
            }

            DataTable table = view.Table;
            List<(string ColumnName, string Header)> columns = GetBoreyExportColumns(grid, table);
            if (columns.Count == 0)
            {
                throw new InvalidOperationException("No exportable columns in Borey grid.");
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
                worksheet.Name = ToExcelSheetName(string.IsNullOrWhiteSpace(logicalTable) ? "Borey" : logicalTable);

                int dataCount = view.Count;
                int rowCount = Math.Max(1, dataCount) + 1;
                int colCount = columns.Count;
                var matrix = new object[rowCount, colCount];

                for (int c = 0; c < columns.Count; c++)
                {
                    matrix[0, c] = columns[c].Header;
                }

                for (int r = 0; r < dataCount; r++)
                {
                    DataRow row = view[r].Row;
                    for (int c = 0; c < columns.Count; c++)
                    {
                        string columnName = columns[c].ColumnName;
                        matrix[r + 1, c] = Convert.ToString(row[columnName], CultureInfo.InvariantCulture) ?? "";
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

        private static List<(string ColumnName, string Header)> GetBoreyExportColumns(DataGrid grid, DataTable table)
        {
            var result = new List<(string ColumnName, string Header)>();
            if (table == null)
            {
                return result;
            }

            if (grid?.Columns != null && grid.Columns.Count > 0)
            {
                foreach (DataGridColumn gridColumn in grid.Columns
                             .Where(c => c != null && c.Visibility == System.Windows.Visibility.Visible)
                             .OrderBy(c => c.DisplayIndex))
                {
                    string columnName = ResolveBoreyGridColumnName(table, gridColumn);
                    if (string.IsNullOrWhiteSpace(columnName) ||
                        result.Any(x => string.Equals(x.ColumnName, columnName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string header = Convert.ToString(gridColumn.Header, CultureInfo.InvariantCulture)?.Trim() ?? columnName;
                    result.Add((columnName, header));
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            foreach (DataColumn col in table.Columns)
            {
                if (col == null)
                {
                    continue;
                }

                result.Add((col.ColumnName, col.ColumnName));
            }

            return result;
        }

        private bool IsBoreyTab()
        {
            if (MainTabControl?.SelectedItem is System.Windows.Controls.TabItem item)
            {
                return string.Equals(item.Header?.ToString(), "BOREY", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        private readonly Dictionary<string, DataTable> _boreyTableCache =
            new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DataTable> _boreyRevitTableCache =
            new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _boreyHiddenColumnKeysByGrid =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _boreyExplicitVisibleColumnKeysByGrid =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _boreyUiSyncInProgress;
        private bool _boreySelectionSyncInProgress;
        private bool _boreyInitialLoadRequested;
        private string _cachedBoreyDbPath = "";
        private string _cachedBoreyPhysicalTable = "";
        private string _cachedBoreySchemaPath = "";
        private const string BoreyFilterAll = "All";
        private const string BoreyFallbackPhysicalTable = "Floors";
        private const string BoreyTocLogicalColumn = "TOC (Lyna)";
        private static readonly string[] BoreyTocColumnAliases = { "TOC (Lyna)", "TOC(Lyna)" };
        private static readonly HashSet<string> PowerBiBoreyDateColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "snapshot_utc",
                "plan_handover",
                "spa_date",
                "spa_ho_date",
                "grace_period_date",
                "plan_date"
            };
        private static readonly HashSet<string> PowerBiBoreyIntegerColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "house_units",
                "handovered_units",
                "handovered",
                "spa_duration",
                "duration_gp",
                "in_months"
            };
        private static readonly HashSet<string> PowerBiBoreyNumberColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "land_lots",
                "site_progress"
            };
        private const string BoreyHouseUnitsLogicalColumn = "House-Units";
        private const string BoreyHandoveredUnitsLogicalColumn = "Handovered-Units";
        private static readonly string[] BoreyMonthlyPlanColumnOrder =
        {
            "Code_Item",
            "ZONE",
            "BLOCK",
            "SUB-BLOCK",
            "HOUSE-TYPE",
            "House Code",
            "House-Units",
            "Handovered-Units",
            "HANDOVERED",
            "HANDOVERED STATUS",
            "TOC (Lyna)"
        };
        private static readonly string[] BoreySubcontractorBaseColumnOrder =
        {
            "Code_Item",
            "Code",
            "ZONE",
            "BLOCK",
            "SUB-BLOCK",
            "HOUSE-TYPE",
            "HOUSE ID",
            "House Code",
            "House-Units",
            "HANDOVERED STATUS",
            "SUBCONTRACTOR",
            "CONTRACT/LOA/BLC No."
        };
        private static readonly List<string> BoreyHandoverStatusDefaultOptions = new List<string>
        {
            "To Client",
            "Hand Over",
            "Under Construction",
            "Stock In",
            "Mockup",
            "Unplan"
        };
        private static readonly List<string> BoreyHandoverTabStatusFilterOptions = new List<string>
        {
            "To Client",
            "Hand over team",
            "Mockup"
        };
        private static readonly string[] BoreyImportMatchKeyColumns =
        {
            "Id",
            "Element ID",
            "HOUSE ID",
            "House No(Sell)",
            "House Code",
            "Code",
            "Code_Item",
            "Mark"
        };
        private static readonly string[] BoreyDbCandidatePaths =
        {
            @"E:\CamboBIM\20260211_CamboBIM.Revit2024.Addin\CamboBIM.Revit2024.Addin\database\RevitDBLink_FullTemplate.mdb",
            @"H:\Site Operation Team\07. CONSTRUCTION MASTER PLAN\DBLINK\20260131a_DBLINK.mdb"
        };
        private static readonly Dictionary<string, string[]> BoreyFilterColumnAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "ZONE", new[] { "ZONE" } },
                { "BLOCK", new[] { "BLOCK" } },
                { "SUB-BLOCK", new[] { "SUB-BLOCK" } },
                { "HOUSE-TYPE", new[] { "HOUSE-TYPE" } },
                { "H_Tp", new[] { "HOUSE-TYPE", "H_Tp" } },
                { "House Code", new[] { "House code(FN)", "House Code", "HOUSE ID", "House No(Sell)" } },
                { "Code", new[] { "Code_Item", "Code", "HOUSE ID", "House No(Sell)" } },
                { "Code_Item", new[] { "Code_Item", "CodeItem" } },
                { "NÃ‚Âº", new[] { "NÃ‚Âº", "No", "NO" } },
                { "HOUSE ID", new[] { "HOUSE ID", "House No(Sell)", "House Code", "House code(FN)" } },
                { "House No(Sell)", new[] { "House No(Sell)" } },
                { "%Site_Progress", new[] { "%Site_Progress" } },
                { "LAND LOTS", new[] { "LAND LOTS" } },
                { "House-Units", new[] { "House-Units" } },
                { "Data_Sold_Out", new[] { "Data_Sold_Out" } },
                { "Sold Only Land", new[] { "Sold Only Land" } },
                { "Handovered-Units", new[] { "Handovered-Units" } },
                { "HANDOVERED", new[] { "HANDOVERED" } },
                { "Sold/Unsold", new[] { "Sold/Unsold" } },
                { "Construction Type", new[] { "Construction Type" } },
                { "Plan Description", new[] { "Plan Description" } },
                { "HANDOVERED STATUS", new[] { "HANDOVERED STATUS", "HANDOVER STATUS" } },
                { "Plan Handover", new[] { "Plan Handover" } },
                { "TOC (Lyna)", new[] { "TOC (Lyna)", "TOC(Lyna)" } },
                { "TOC(Lyna)", new[] { "TOC(Lyna)", "TOC (Lyna)" } },
                { "SPA Date", new[] { "SPA Date" } },
                { "SPA (HO Date)", new[] { "SPA (HO Date)", "SPA  (HO Date)" } },
                { "SPA  (HO Date)", new[] { "SPA (HO Date)", "SPA  (HO Date)" } },
                { "SPA Duration", new[] { "SPA Duration" } },
                { "Duration GP", new[] { "Duration GP", "SPA+GP" } },
                { "Grace Period Date", new[] { "Grace Period Date", "End Date SPA+ GP" } },
                { "Priority Type", new[] { "Priority Type", "Priority", "Priority build", "House Priority" } },
                { "Plan Date", new[] { "Plan Date", "PlanDate" } },
                { "In Months", new[] { "In Months", "In Month", "InMonths" } },
                { "SUBCONTRACTOR", new[] { "SUBCONTRACTOR", "Subcontractor", "SUB CONTRACTOR" } },
                { "CONTRACT/LOA/BLC No.", new[] { "CONTRACT/LOA/BLC No.", "CONTRACT/LOA/BLC NO.", "CONTRACT/LOA/BLC No" } }
            };

        private sealed class BoreyColumnToggleTag
        {
            public DataGrid Grid { get; set; }
            public string GridName { get; set; } = "";
            public string ColumnKey { get; set; } = "";
        }
    }
}


