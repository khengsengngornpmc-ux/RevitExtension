using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private readonly ObservableCollection<QsMeasurementRuleRow> _qsMeasurementRulesRows =
            new ObservableCollection<QsMeasurementRuleRow>();
        private readonly ObservableCollection<QsMeasurementRuleRow> _qsMeasurementRulesTasRows =
            new ObservableCollection<QsMeasurementRuleRow>();

        private QsMeasurementRulesProfile _qsMeasurementRulesProfile =
            QsMeasurementRulesProfile.CreateDefault();

        private bool _qsMeasurementRulesDirty;
        private bool _qsMeasurementRulesLoading;

        private void InitializeQsMeasurementRules()
        {
            QsMeasurementRulesProfile profile = TryLoadQsMeasurementRulesFromDefaultPath();
            if (profile == null)
            {
                profile = QsMeasurementRulesProfile.CreateDefault();
            }

            LoadQsMeasurementRulesProfile(profile, false);
        }

        private QsMeasurementRulesProfile TryLoadQsMeasurementRulesFromDefaultPath()
        {
            string path = GetQsMeasurementRulesDefaultPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                QsMeasurementRulesProfile profile = CamboBimJson.Deserialize<QsMeasurementRulesProfile>(json);
                if (profile == null || profile.Rules == null || profile.Rules.Count == 0)
                {
                    return null;
                }

                return profile;
            }
            catch
            {
                return null;
            }
        }

        private void LoadQsMeasurementRulesProfile(QsMeasurementRulesProfile profile, bool markDirty)
        {
            _qsMeasurementRulesLoading = true;

            foreach (QsMeasurementRuleRow oldRow in GetQsMeasurementRulesAllRows())
            {
                oldRow.PropertyChanged -= OnQsMeasurementRuleRowChanged;
            }

            _qsMeasurementRulesProfile = profile ?? QsMeasurementRulesProfile.CreateDefault();
            _qsMeasurementRulesProfile.Normalize();

            foreach (QsMeasurementRuleRow row in GetQsMeasurementRulesAllRows())
            {
                row.PropertyChanged += OnQsMeasurementRuleRowChanged;
            }

            RefreshQsMeasurementRuleCategoryList();
            RefreshQsMeasurementRuleConditionList();
            ApplyQsMeasurementRulesFilter();
            if (QsMeasurementRuleProfileNameTextBox != null)
            {
                QsMeasurementRuleProfileNameTextBox.Text = _qsMeasurementRulesProfile.ProfileName ?? "";
            }

            _qsMeasurementRulesDirty = markDirty;
            _qsMeasurementRulesLoading = false;
            UpdateQsMeasurementRulesKpis();
        }

        private IEnumerable<QsMeasurementRuleRow> GetQsMeasurementRulesAllRows()
        {
            if (_qsMeasurementRulesProfile == null || _qsMeasurementRulesProfile.Rules == null)
            {
                return Enumerable.Empty<QsMeasurementRuleRow>();
            }

            return _qsMeasurementRulesProfile.Rules.Where(r => r != null);
        }

        private void RefreshQsMeasurementRuleCategoryList()
        {
            if (QsMeasurementRuleCategoryCombo == null) return;

            string current = QsMeasurementRuleCategoryCombo.SelectedItem as string;
            List<string> categories = GetQsMeasurementRulesAllRows()
                .Select(r => r.Category ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetQsMeasurementCategoryOrder)
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            categories.Insert(0, "All Elements");
            QsMeasurementRuleCategoryCombo.ItemsSource = categories;
            if (!string.IsNullOrWhiteSpace(current) &&
                categories.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                QsMeasurementRuleCategoryCombo.SelectedItem = current;
            }
            else
            {
                QsMeasurementRuleCategoryCombo.SelectedIndex = 0;
            }
        }

        private void RefreshQsMeasurementRuleConditionList()
        {
            if (QsMeasurementRuleConditionCombo == null) return;

            string current = QsMeasurementRuleConditionCombo.SelectedItem as string;
            string selectedCategory = QsMeasurementRuleCategoryCombo?.SelectedItem as string;
            bool allElements = IsAllMeasurementElementSelection(selectedCategory);

            List<string> conditions = GetQsMeasurementRulesAllRows()
                .Where(r => allElements || string.Equals(r.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Method ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetQsMeasurementConditionOrder)
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            conditions.Insert(0, "All Rule Types");
            QsMeasurementRuleConditionCombo.ItemsSource = conditions;
            if (!string.IsNullOrWhiteSpace(current) &&
                conditions.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                QsMeasurementRuleConditionCombo.SelectedItem = current;
            }
            else
            {
                QsMeasurementRuleConditionCombo.SelectedIndex = 0;
            }
        }

        private void ApplyQsMeasurementRulesFilter()
        {
            string selectedCategory = QsMeasurementRuleCategoryCombo?.SelectedItem as string;
            string selectedCondition = QsMeasurementRuleConditionCombo?.SelectedItem as string;
            bool allElements = IsAllMeasurementElementSelection(selectedCategory);
            bool allConditions = IsAllMeasurementConditionSelection(selectedCondition);

            _qsMeasurementRulesRows.Clear();
            _qsMeasurementRulesTasRows.Clear();
            foreach (QsMeasurementRuleRow row in GetQsMeasurementRulesAllRows()
                         .OrderBy(r => GetQsMeasurementCategoryOrder(r.Category))
                         .ThenBy(r => r.SortOrder)
                         .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase))
            {
                bool elementMatches = allElements ||
                                      string.Equals(row.Category, selectedCategory, StringComparison.OrdinalIgnoreCase);
                bool conditionMatches = allConditions ||
                                        string.Equals(row.Method, selectedCondition, StringComparison.OrdinalIgnoreCase);

                if (!elementMatches || !conditionMatches)
                {
                    continue;
                }

                row.DisplayIndex = _qsMeasurementRulesRows.Count + 1;
                _qsMeasurementRulesRows.Add(row);
                _qsMeasurementRulesTasRows.Add(row);
            }

            if (QsMeasurementRulesGrid != null)
            {
                QsMeasurementRulesGrid.ItemsSource = _qsMeasurementRulesRows;
            }

            if (QsMeasurementRulesTasGrid != null)
            {
                QsMeasurementRulesTasGrid.ItemsSource = _qsMeasurementRulesTasRows;
            }

            UpdateQsMeasurementRulesKpis();
        }

        private void UpdateQsMeasurementRulesKpis()
        {
            int total = GetQsMeasurementRulesAllRows().Count();
            int active = GetQsMeasurementRulesAllRows().Count(r => r.IsEnabled);
            int visible = _qsMeasurementRulesRows.Count;

            if (QsMeasurementRuleProfileBadgeText != null)
            {
                QsMeasurementRuleProfileBadgeText.Text = string.IsNullOrWhiteSpace(_qsMeasurementRulesProfile?.ProfileName)
                    ? "MHNK Measurement Rules - Cubicost TAS"
                    : _qsMeasurementRulesProfile.ProfileName;
            }

            if (QsMeasurementRulesCountText != null)
            {
                QsMeasurementRulesCountText.Text = visible.ToString(CultureInfo.InvariantCulture) + " shown / " +
                                                   total.ToString(CultureInfo.InvariantCulture);
            }

            if (QsMeasurementRuleActiveCountText != null)
            {
                QsMeasurementRuleActiveCountText.Text = active.ToString(CultureInfo.InvariantCulture) + " active";
            }

            if (QsMeasurementRuleStatusText != null)
            {
                QsMeasurementRuleStatusText.Text = _qsMeasurementRulesDirty ? "Modified" : "Saved";
            }
        }

        private void MarkQsMeasurementRulesDirty()
        {
            if (_qsMeasurementRulesLoading) return;

            _qsMeasurementRulesDirty = true;
            if (_qsMeasurementRulesProfile != null)
            {
                _qsMeasurementRulesProfile.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            UpdateQsMeasurementRulesKpis();
        }

        private void OnQsMeasurementRuleRowChanged(object sender, PropertyChangedEventArgs e)
        {
            bool refresh = false;
            if (!_qsMeasurementRulesLoading && string.Equals(e?.PropertyName, "Method", StringComparison.OrdinalIgnoreCase))
            {
                RefreshQsMeasurementRuleConditionList();
                refresh = true;
            }

            if (refresh)
            {
                ApplyQsMeasurementRulesFilter();
            }

            MarkQsMeasurementRulesDirty();
        }

        private void OnQsMeasurementRuleProfileNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_qsMeasurementRulesLoading || _qsMeasurementRulesProfile == null) return;
            _qsMeasurementRulesProfile.ProfileName = QsMeasurementRuleProfileNameTextBox?.Text ?? "";
            MarkQsMeasurementRulesDirty();
        }

        private void OnQsMeasurementRuleCategoryChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_qsMeasurementRulesLoading) return;
            RefreshQsMeasurementRuleConditionList();
            ApplyQsMeasurementRulesFilter();
        }

        private void OnQsMeasurementRuleConditionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_qsMeasurementRulesLoading) return;
            ApplyQsMeasurementRulesFilter();
        }

        private void OnQsMeasurementRulesSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveQsMeasurementRulesProfile(GetQsMeasurementRulesDefaultPath());
                ShowStatus("Measurement Rules saved.");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Rules save failed: " + ex.Message);
            }
        }

        private void OnQsMeasurementRulesImportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Measurement Rules",
                Filter = "Measurement Rules (*.json;*.csv)|*.json;*.csv|JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName)) return;

            try
            {
                QsMeasurementRulesProfile profile = ReadQsMeasurementRulesProfile(dialog.FileName);
                LoadQsMeasurementRulesProfile(profile, true);
                ShowStatus($"Measurement Rules imported: {profile.Rules.Count} row(s).");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Rules import failed: " + ex.Message);
            }
        }

        private void OnQsMeasurementRulesExportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Measurement Rules",
                Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "MHNK-Measurement-Rules-TAS.json",
                AddExtension = true,
                DefaultExt = ".json"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName)) return;

            try
            {
                ExportQsMeasurementRulesProfile(dialog.FileName);
                ShowStatus($"Measurement Rules exported: {dialog.FileName}.");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Rules export failed: " + ex.Message);
            }
        }

        private void OnQsMeasurementRulesRestoreDefaultClick(object sender, RoutedEventArgs e)
        {
            LoadQsMeasurementRulesProfile(QsMeasurementRulesProfile.CreateDefault(), true);
            ShowStatus("Measurement Rules restored to Cubicost TAS defaults.");
        }

        private void ApplyQsMeasurementRulesToRequest()
        {
            QsMeasurementRulesProfile profile = _qsMeasurementRulesProfile != null
                ? _qsMeasurementRulesProfile.Clone()
                : QsMeasurementRulesProfile.CreateDefault();

            profile.Normalize();
            _handler.Request.QsMeasurementRulesProfile = profile;
        }

        private void SaveQsMeasurementRulesProfile(string filePath)
        {
            if (_qsMeasurementRulesProfile == null)
            {
                _qsMeasurementRulesProfile = QsMeasurementRulesProfile.CreateDefault();
            }

            if (QsMeasurementRuleProfileNameTextBox != null)
            {
                _qsMeasurementRulesProfile.ProfileName = QsMeasurementRuleProfileNameTextBox.Text ?? "";
            }

            string validationMessage;
            if (!TryValidateQsMeasurementRulesProfile(_qsMeasurementRulesProfile, out validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            _qsMeasurementRulesProfile.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, CamboBimJson.Serialize(_qsMeasurementRulesProfile), new UTF8Encoding(false));
            _qsMeasurementRulesDirty = false;
            UpdateQsMeasurementRulesKpis();
        }

        private QsMeasurementRulesProfile ReadQsMeasurementRulesProfile(string filePath)
        {
            string ext = Path.GetExtension(filePath) ?? "";
            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                QsMeasurementRulesProfile profile = CamboBimJson.Deserialize<QsMeasurementRulesProfile>(json);
                profile?.Normalize();
                return profile;
            }

            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ReadQsMeasurementRulesFromCsv(filePath);
            }

            throw new InvalidOperationException("Unsupported Measurement Rules file type.");
        }

        private void ExportQsMeasurementRulesProfile(string filePath)
        {
            if (_qsMeasurementRulesProfile == null)
            {
                _qsMeasurementRulesProfile = QsMeasurementRulesProfile.CreateDefault();
            }

            _qsMeasurementRulesProfile.Normalize();
            string validationMessage;
            if (!TryValidateQsMeasurementRulesProfile(_qsMeasurementRulesProfile, out validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            string ext = Path.GetExtension(filePath) ?? "";
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(filePath, BuildQsMeasurementRulesCsv(), new UTF8Encoding(false));
                return;
            }

            File.WriteAllText(filePath, CamboBimJson.Serialize(_qsMeasurementRulesProfile), new UTF8Encoding(false));
        }

        private string BuildQsMeasurementRulesCsv()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("Active,Element,Rule Code,Description,TAS Sheet,Mapping / Formula,Unit,Rule Type,Sort Order");
            foreach (QsMeasurementRuleRow row in GetQsMeasurementRulesAllRows()
                         .OrderBy(r => GetQsMeasurementCategoryOrder(r.Category))
                         .ThenBy(r => r.SortOrder)
                         .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(row.IsEnabled ? "1" : "0").Append(',')
                  .Append(EscapeCsv(row.Category)).Append(',')
                  .Append(EscapeCsv(row.Code)).Append(',')
                  .Append(EscapeCsv(row.Description)).Append(',')
                  .Append(EscapeCsv(row.Option)).Append(',')
                  .Append(EscapeCsv(row.Value)).Append(',')
                  .Append(EscapeCsv(row.Unit)).Append(',')
                  .Append(EscapeCsv(row.Method)).Append(',')
                  .Append(row.SortOrder.ToString(CultureInfo.InvariantCulture))
                  .AppendLine();
            }

            return sb.ToString();
        }

        private static QsMeasurementRulesProfile ReadQsMeasurementRulesFromCsv(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
            {
                throw new InvalidOperationException("No Measurement Rules rows were found.");
            }

            List<string> headers = ParseCsvLine(lines[0]);
            var importedRows = new List<QsMeasurementRuleRow>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                QsMeasurementRuleRow row = BuildQsMeasurementRuleRowFromCells(ParseCsvLine(lines[i]), headers);
                if (row != null)
                {
                    importedRows.Add(row);
                }
            }

            return BuildMergedQsMeasurementRulesProfile(importedRows);
        }

        private static QsMeasurementRulesProfile BuildMergedQsMeasurementRulesProfile(IEnumerable<QsMeasurementRuleRow> importedRows)
        {
            QsMeasurementRulesProfile profile = QsMeasurementRulesProfile.CreateDefault();
            profile.Normalize();
            var byCode = profile.Rules
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Code))
                .ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
            int nextSort = profile.Rules.Count == 0 ? 10 : profile.Rules.Max(r => r.SortOrder) + 10;
            int importedCount = 0;

            foreach (QsMeasurementRuleRow imported in importedRows ?? Enumerable.Empty<QsMeasurementRuleRow>())
            {
                if (imported == null || string.IsNullOrWhiteSpace(imported.Code)) continue;
                importedCount++;

                QsMeasurementRuleRow target;
                if (!byCode.TryGetValue(imported.Code, out target))
                {
                    target = new QsMeasurementRuleRow
                    {
                        Code = imported.Code,
                        SortOrder = imported.SortOrder > 0 ? imported.SortOrder : nextSort
                    };
                    nextSort += 10;
                    profile.Rules.Add(target);
                    byCode[target.Code] = target;
                }

                target.IsEnabled = imported.IsEnabled;
                if (!string.IsNullOrWhiteSpace(imported.Category)) target.Category = imported.Category;
                if (!string.IsNullOrWhiteSpace(imported.Description)) target.Description = imported.Description;
                if (!string.IsNullOrWhiteSpace(imported.Option)) target.Option = imported.Option;
                if (!string.IsNullOrWhiteSpace(imported.Value)) target.Value = imported.Value;
                if (!string.IsNullOrWhiteSpace(imported.Unit)) target.Unit = imported.Unit;
                if (!string.IsNullOrWhiteSpace(imported.Method)) target.Method = imported.Method;
                if (imported.SortOrder > 0) target.SortOrder = imported.SortOrder;
            }

            if (importedCount == 0)
            {
                throw new InvalidOperationException("No Measurement Rules rows were found.");
            }

            profile.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            profile.Normalize();
            return profile;
        }

        private static QsMeasurementRuleRow BuildQsMeasurementRuleRowFromCells(IReadOnlyList<string> cells, IReadOnlyList<string> headers)
        {
            int idxActive = IndexOfHeader(headers, "Active", "Use", "Enabled");
            int idxElement = IndexOfHeader(headers, "Element", "Category");
            int idxCode = IndexOfHeader(headers, "Rule Code", "RuleCode", "Code");
            int idxDescription = IndexOfHeader(headers, "Description", "TAS Header");
            int idxSheet = IndexOfHeader(headers, "TAS Sheet", "Sheet");
            int idxMapping = IndexOfHeader(headers, "Mapping / Formula", "Mapping", "Formula", "Value");
            int idxUnit = IndexOfHeader(headers, "Unit", "UOM");
            int idxMethod = IndexOfHeader(headers, "Rule Type", "Condition", "Method");
            int idxSort = IndexOfHeader(headers, "Sort Order", "SortOrder", "Order");

            string code = ValueAt(cells, idxCode);
            if (string.IsNullOrWhiteSpace(code)) return null;

            var row = new QsMeasurementRuleRow
            {
                IsEnabled = BoolAt(cells, idxActive, true),
                Category = ValueAt(cells, idxElement),
                Code = code,
                Description = ValueAt(cells, idxDescription),
                Option = ValueAt(cells, idxSheet),
                Value = ValueAt(cells, idxMapping),
                Unit = ValueAt(cells, idxUnit),
                Method = ValueAt(cells, idxMethod),
                SortOrder = IntAt(cells, idxSort)
            };
            row.RefreshChoices();
            return row;
        }

        private static bool TryValidateQsMeasurementRulesProfile(QsMeasurementRulesProfile profile, out string message)
        {
            message = "";
            if (profile == null) return true;

            profile.Normalize();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QsMeasurementRuleRow row in profile.Rules ?? new List<QsMeasurementRuleRow>())
            {
                if (row == null) continue;
                if (string.IsNullOrWhiteSpace(row.Code))
                {
                    message = row.IsEnabled
                        ? "Measurement Rules contains an active row without a rule code."
                        : "Measurement Rules contains a row without a rule code.";
                    return false;
                }

                if (!seenCodes.Add(row.Code.Trim()))
                {
                    message = $"Measurement Rules contains duplicate rule code {row.Code}.";
                    return false;
                }

                if (!row.IsEnabled) continue;

                if (string.IsNullOrWhiteSpace(row.Option))
                {
                    message = $"Measurement Rules row {row.Code} needs a TAS sheet name.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(row.Value))
                {
                    message = $"Measurement Rules row {row.Code} needs a mapping or formula.";
                    return false;
                }
            }

            return true;
        }

        private static string GetQsMeasurementRulesDefaultPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            return Path.Combine(appData, "MHNK", "RevitExtension", "QS", "measurement-rules.json");
        }
    }
}
