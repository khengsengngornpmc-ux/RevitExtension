using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private sealed class TasExportSheetSpec
        {
            public string SheetName { get; set; } = "";
            public string Category { get; set; } = "";
            public List<QsMeasurementRuleRow> Rules { get; } = new List<QsMeasurementRuleRow>();
        }

        private sealed class TasExportRow
        {
            public object[] Values { get; set; } = new object[0];
            public bool[] HasQuantityValue { get; set; } = new bool[0];
            public string SortFloor { get; set; } = "";
            public string SortName { get; set; } = "";
            public string AuditSheet { get; set; } = "";
            public string AuditStructureElement { get; set; } = "";
            public string AuditType { get; set; } = "";
            public string AuditRuleCode { get; set; } = "";
            public string AuditBreakdown { get; set; } = "";
        }

        private void OnBoqExportTasExcelClick(object sender, RoutedEventArgs e)
        {
            List<BoqTableRow> rows = GetBoqRowsForTransfer();
            if (rows.Count == 0)
            {
                ShowStatus("No BOQ rows to export. Click Refresh in BOQ table first.");
                return;
            }

            QsMeasurementRulesProfile profile = _qsMeasurementRulesProfile != null
                ? _qsMeasurementRulesProfile.Clone()
                : QsMeasurementRulesProfile.CreateDefault();
            profile.Normalize();

            QsMeasurementSettingsProfile settingsProfile = _qsMeasurementSettingsProfile != null
                ? _qsMeasurementSettingsProfile.Clone()
                : QsMeasurementSettingsProfile.CreateDefault();
            settingsProfile.Normalize();

            var dialog = new SaveFileDialog
            {
                Title = "Export Cubicost TAS Measurement Workbook",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                FileName = "MHNK-TAS-Measurement_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx",
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
                ExportTasMeasurementWorkbook(rows, profile, settingsProfile, dialog.FileName);
                ShowStatus($"Cubicost TAS workbook exported: {dialog.FileName} ({rows.Count} BOQ row(s)).");
            }
            catch (Exception ex)
            {
                ShowStatus("Cubicost TAS workbook export failed: " + ex.Message);
            }
        }

        private void ExportTasMeasurementWorkbook(
            IReadOnlyList<BoqTableRow> sourceRows,
            QsMeasurementRulesProfile profile,
            QsMeasurementSettingsProfile settingsProfile,
            string filePath)
        {
            if (sourceRows == null || sourceRows.Count == 0)
            {
                throw new InvalidOperationException("No BOQ rows to export.");
            }

            if (profile == null)
            {
                profile = QsMeasurementRulesProfile.CreateDefault();
            }

            profile.Normalize();
            if (settingsProfile == null)
            {
                settingsProfile = QsMeasurementSettingsProfile.CreateDefault();
            }

            settingsProfile.Normalize();
            List<TasExportSheetSpec> sheets = BuildTasExportSheetSpecs(profile);
            if (sheets.Count == 0)
            {
                throw new InvalidOperationException("Measurement Rules does not contain any enabled TAS sheet rules.");
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
                workbookObj = workbooks.Add();
                dynamic workbook = workbookObj;

                int targetSheetCount = sheets.Count + 3;
                while (workbook.Worksheets.Count < targetSheetCount)
                {
                    workbook.Worksheets.Add(After: workbook.Worksheets[workbook.Worksheets.Count]);
                }

                while (workbook.Worksheets.Count > targetSheetCount)
                {
                    workbook.Worksheets[workbook.Worksheets.Count].Delete();
                }

                var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sheets.Count; i++)
                {
                    object worksheetObj = null;
                    try
                    {
                        worksheetObj = workbook.Worksheets[i + 1];
                        dynamic worksheet = worksheetObj;
                        worksheet.Name = GetUniqueExcelSheetName(sheets[i].SheetName, usedSheetNames);

                        List<TasExportRow> sheetRows = BuildTasExportRows(sourceRows, sheets[i]);
                        WriteTasSheet(worksheet, sheets[i], sheetRows);
                    }
                    finally
                    {
                        SafeReleaseCom(worksheetObj);
                    }
                }

                WriteTasSourceAuditSheet(workbook, sheets.Count + 1, sourceRows, sheets, usedSheetNames);
                WriteTasSettingsAuditSheet(workbook, sheets.Count + 2, settingsProfile, usedSheetNames);
                WriteTasRulesAuditSheet(workbook, sheets.Count + 3, profile, settingsProfile, usedSheetNames);

                workbook.SaveAs(filePath, 51);
                workbook.Close(true);
                app.Quit();
            }
            finally
            {
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private static List<TasExportSheetSpec> BuildTasExportSheetSpecs(QsMeasurementRulesProfile profile)
        {
            var specs = new List<TasExportSheetSpec>();
            IEnumerable<QsMeasurementRuleRow> rules = profile.EnabledRules()
                .Select(NormalizeTasExportRule)
                .Where(ShouldIncludeTasExportRule)
                .Where(r => !string.IsNullOrWhiteSpace(r.Option) && !string.IsNullOrWhiteSpace(r.Category));

            foreach (IGrouping<string, QsMeasurementRuleRow> group in rules.GroupBy(
                         r => r.Option.Trim() + "\u001f" + r.Category.Trim(),
                         StringComparer.OrdinalIgnoreCase))
            {
                QsMeasurementRuleRow first = group.First();
                var spec = new TasExportSheetSpec
                {
                    SheetName = first.Option.Trim(),
                    Category = first.Category.Trim()
                };

                spec.Rules.AddRange(group
                    .OrderBy(r => r.SortOrder)
                    .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase));
                specs.Add(spec);
            }

            return specs
                .OrderBy(s => GetQsMeasurementCategoryOrder(s.Category))
                .ThenBy(s => s.SheetName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static QsMeasurementRuleRow NormalizeTasExportRule(QsMeasurementRuleRow source)
        {
            QsMeasurementRuleRow row = source?.Clone() ?? new QsMeasurementRuleRow();
            string code = row.Code ?? "";
            row.Option = ResolveTasExportSheetName(row.Option, row.Category, code);

            if (code.EndsWith(".QTY.REBAR", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Weight of rebar(kg)";
                row.Value = "Rebar.Weight";
            }
            else if (string.Equals(code, "COL.QTY.FORMWORK.BASIC", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of formwork for strutting high(0~1.5m)(m2)";
                row.Value = "FWK.Col.Stage.Basic";
            }
            else if (string.Equals(code, "COL.QTY.FORMWORK.STAGE.1", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork for strutting high(1.5~3m)(m2)";
                row.Value = "FWK.Col.Stage.1";
            }
            else if (string.Equals(code, "COL.QTY.FORMWORK.STAGE.2", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork for strutting high(3~4.5m)(m2)";
                row.Value = "FWK.Col.Stage.2";
            }
            else if (string.Equals(code, "COL.QTY.FORMWORK.STAGE.3", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork for strutting high(4.5~6m)(m2)";
                row.Value = "FWK.Col.Stage.3";
            }
            else if (string.Equals(code, "COL.QTY.FORMWORK.STAGE.4", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork for strutting high(6~7.5m)(m2)";
                row.Value = "FWK.Col.Stage.4";
            }
            else if (string.Equals(code, "WALL.QTY.EDGE.STAGE.0", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Length of formwork to edge and break in stages(0~0.25m)(m)";
                row.Value = "FWK.Wall.EdgeLength.Stage.0";
            }
            else if (string.Equals(code, "WALL.QTY.EDGE.STAGE.1", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Length of formwork to edge and break in stages(0.25~0.5m)(m)";
                row.Value = "FWK.Wall.EdgeLength.Stage.1";
            }
            else if (string.Equals(code, "WALL.QTY.EDGE.STAGE.2", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Length of formwork to edge and break in stages(0.5~1m)(m)";
                row.Value = "FWK.Wall.EdgeLength.Stage.2";
            }
            else if (string.Equals(code, "WALL.QTY.EDGE.AREA.STAGE.3", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork to edge and break in stages(>1m)(m2)";
                row.Value = "FWK.Wall.EdgeArea.Stage.3";
            }
            else if (string.Equals(code, "SLAB.QTY.SOFFIT.BASIC", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of formwork to soffit(<=1.5m)(m2)";
                row.Value = "FWK.Floor.StrutBasic";
            }
            else if (string.Equals(code, "WALL.QTY.ORIGINAL.HEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Original height of wall(m)";
                row.Value = "FWK.Wall.OriginalHeight";
            }
            else if (string.Equals(code, "LINTEL.QTY.FORMWORK.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of side formwork(m2)";
                row.Value = "FWK.Lintel.Sides";
            }
            else if (string.Equals(code, "LINTEL.QTY.FORMWORK.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of bottom formwork(m2)";
                row.Value = "FWK.Lintel.Bottom";
            }
            else if (string.Equals(code, "LINTEL.QTY.LENGTH", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Length(m)";
                row.Value = "FWK.Lintel.Length";
            }
            else if (string.Equals(code, "DROP.QTY.SOFFIT", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of soffit formwork(m2)";
                row.Value = "FWK.Drop.Soffit";
            }
            else if (string.Equals(code, "DROP.QTY.STRUT.STAGE.1", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork for strutting high stage 1(m2)";
                row.Value = "FWK.Drop.Stage.1";
            }
            else if (string.Equals(code, "EAVE.QTY.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of bottom formwork(m2)";
                row.Value = "FWK.Eave.Bottom";
            }
            else if (string.Equals(code, "EAVE.QTY.EDGE", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of edge/break formwork(m2)";
                row.Value = "FWK.Eave.Edge";
            }
            else if (string.Equals(code, "STAIR.QTY.STEPS", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Number of steps(pc)";
                row.Value = "FWK.Stair.StepCount";
            }
            else if (string.Equals(code, "SLAB.QTY.SOFFIT.STAGE.1", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork to soffit for strutting high(1.5~3m)(m2)";
                row.Value = "FWK.Floor.StrutStage.1";
            }
            else if (string.Equals(code, "SLAB.QTY.SOFFIT.STAGE.2", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork to soffit for strutting high(3~4.5m)(m2)";
                row.Value = "FWK.Floor.StrutStage.2";
            }
            else if (string.Equals(code, "SLAB.QTY.SOFFIT.STAGE.3", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Stage Bucket: Area of formwork to soffit for strutting high(4.5~6m)(m2)";
                row.Value = "FWK.Floor.StrutStage.3";
            }
            else if (string.Equals(code, "WF.CLASS.ROOM", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Classification Condition: Associated Room";
                row.Value = "FIN.Room";
            }
            else if (string.Equals(code, "WF.QTY.AREA", StringComparison.OrdinalIgnoreCase))
            {
                row.Description = "Quantity: Area of finish to wall finish(m2)";
                row.Value = "FIN.WF.Area";
            }

            return row;
        }

        private static bool ShouldIncludeTasExportRule(QsMeasurementRuleRow row)
        {
            if (row == null) return false;
            string sheet = NormalizeTasMatchText(row.Option);
            string code = row.Code ?? "";

            if (sheet == "straightflightdef")
            {
                return string.Equals(code, "STAIR.CLASS.FLOOR", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "STAIR.CLASS.NAME", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "STAIR.QTY.NUMBER", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "STAIR.QTY.VOLUME", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "STAIR.QTY.STEPS", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "STAIR.QTY.REBAR", StringComparison.OrdinalIgnoreCase);
            }

            if (sheet == "wallfinishdef")
            {
                return string.Equals(code, "WF.CLASS.FLOOR", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "WF.CLASS.NAME", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "WF.CLASS.ROOM", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "WF.CLASS.PARENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "WF.QTY.AREA", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(code, "WF.QTY.SURFACE.OTHER", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static string ResolveTasExportSheetName(string sheetName, string category, string code)
        {
            string sheet = (sheetName ?? "").Trim();
            if (string.Equals(sheet, "PadFoundationUnit-def", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sheet, "PadFoundation-def", StringComparison.OrdinalIgnoreCase))
            {
                return "PileCap-def";
            }

            if (string.Equals(sheet, "Staircase-def", StringComparison.OrdinalIgnoreCase))
            {
                return "StraightFlight-def";
            }

            return sheet;
        }

        private static List<TasExportRow> BuildTasExportRows(
            IReadOnlyList<BoqTableRow> sourceRows,
            TasExportSheetSpec spec)
        {
            var map = new Dictionary<string, TasExportRow>(StringComparer.OrdinalIgnoreCase);
            if (sourceRows == null || spec == null || spec.Rules.Count == 0)
            {
                return new List<TasExportRow>();
            }

            for (int i = 0; i < sourceRows.Count; i++)
            {
                BoqTableRow source = sourceRows[i];
                if (!TasBoqRowMatchesSheet(source, spec))
                {
                    continue;
                }

                var keyParts = new List<string>();
                object[] classValues = new object[spec.Rules.Count];
                for (int c = 0; c < spec.Rules.Count; c++)
                {
                    QsMeasurementRuleRow rule = spec.Rules[c];
                    if (!IsTasClassificationRule(rule))
                    {
                        continue;
                    }

                    object value = ResolveTasRuleValue(source, rule);
                    string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    classValues[c] = text;
                    keyParts.Add(GetTasHeaderFromRule(rule) + "=" + text);
                }

                if (keyParts.Count == 0)
                {
                    keyParts.Add(source.BuildingLevel ?? "");
                    keyParts.Add(source.TypeName ?? "");
                }

                string key = string.Join("\u001f", keyParts);
                if (!map.TryGetValue(key, out TasExportRow target))
                {
                    target = new TasExportRow
                    {
                        Values = new object[spec.Rules.Count],
                        HasQuantityValue = new bool[spec.Rules.Count],
                        SortFloor = source.BuildingLevel ?? "",
                        SortName = source.TypeName ?? "",
                        AuditSheet = spec.SheetName ?? "",
                        AuditStructureElement = source.StructureElement ?? "",
                        AuditType = source.TypeName ?? "",
                        AuditRuleCode = source.QsRuleCode ?? "",
                        AuditBreakdown = source.QsBreakdown ?? ""
                    };

                    for (int c = 0; c < classValues.Length; c++)
                    {
                        if (classValues[c] != null)
                        {
                            target.Values[c] = classValues[c];
                        }
                    }

                    map[key] = target;
                }
                else
                {
                    target.AuditStructureElement = MergeTasAuditText(target.AuditStructureElement, source.StructureElement, 480);
                    target.AuditType = MergeTasAuditText(target.AuditType, source.TypeName, 600);
                    target.AuditRuleCode = MergeTasAuditText(target.AuditRuleCode, source.QsRuleCode, 600);
                    target.AuditBreakdown = MergeTasAuditText(target.AuditBreakdown, source.QsBreakdown, 1200);
                }

                for (int c = 0; c < spec.Rules.Count; c++)
                {
                    QsMeasurementRuleRow rule = spec.Rules[c];
                    if (IsTasClassificationRule(rule))
                    {
                        continue;
                    }

                    object value = ResolveTasRuleValue(source, rule);
                    double numeric;
                    if (TryConvertTasNumber(value, out numeric))
                    {
                        double current = 0.0;
                        TryConvertTasNumber(target.Values[c], out current);
                        target.Values[c] = current + numeric;
                        target.HasQuantityValue[c] = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)) &&
                             string.IsNullOrWhiteSpace(Convert.ToString(target.Values[c], CultureInfo.InvariantCulture)))
                    {
                        target.Values[c] = value;
                    }
                }
            }

            return map.Values
                .OrderBy(r => r.SortFloor, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.SortName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void WriteTasSheet(dynamic worksheet, TasExportSheetSpec spec, IReadOnlyList<TasExportRow> rows)
        {
            List<QsMeasurementRuleRow> rules = spec.Rules;
            int colCount = rules.Count;
            int dataRowCount = rows?.Count ?? 0;
            bool includeTotalRow = dataRowCount > 0;
            int rowCount = 2 + dataRowCount + (includeTotalRow ? 1 : 0);
            var matrix = new object[rowCount, colCount];

            for (int c = 0; c < rules.Count; c++)
            {
                matrix[0, c] = IsTasClassificationRule(rules[c]) ? "Classification Condition" : "Quantity";
                matrix[1, c] = GetTasHeaderFromRule(rules[c]);
            }

            for (int r = 0; r < dataRowCount; r++)
            {
                TasExportRow row = rows[r];
                for (int c = 0; c < rules.Count; c++)
                {
                    object value = row.Values[c];
                    if (row.HasQuantityValue[c] && TryConvertTasNumber(value, out double numeric))
                    {
                        matrix[r + 2, c] = Math.Round(numeric, 3);
                    }
                    else
                    {
                        matrix[r + 2, c] = value ?? "";
                    }
                }
            }

            if (includeTotalRow)
            {
                int totalRow = rowCount - 1;
                for (int c = 0; c < rules.Count; c++)
                {
                    if (IsTasClassificationRule(rules[c]))
                    {
                        matrix[totalRow, c] = "Total";
                        continue;
                    }

                    double total = 0.0;
                    bool hasValue = false;
                    for (int r = 0; r < dataRowCount; r++)
                    {
                        if (!rows[r].HasQuantityValue[c]) continue;
                        if (TryConvertTasNumber(rows[r].Values[c], out double numeric))
                        {
                            total += numeric;
                            hasValue = true;
                        }
                    }

                    matrix[totalRow, c] = hasValue ? (object)Math.Round(total, 3) : "";
                }
            }

            object topLeftObj = null;
            object bottomRightObj = null;
            object rangeObj = null;
            object headerRangeObj = null;
            try
            {
                topLeftObj = worksheet.Cells[1, 1];
                bottomRightObj = worksheet.Cells[rowCount, colCount];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Value2 = matrix;

                headerRangeObj = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[2, colCount]];
                dynamic headerRange = headerRangeObj;
                headerRange.Font.Bold = true;
                headerRange.Interior.Color = 14277081;

                try
                {
                    worksheet.Rows[2].AutoFilter();
                    worksheet.Columns.AutoFit();
                }
                catch
                {
                    // Formatting should not block the export.
                }
            }
            finally
            {
                SafeReleaseCom(headerRangeObj);
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
            }
        }

        private static bool IsTasClassificationRule(QsMeasurementRuleRow rule)
        {
            if (rule == null) return false;
            string method = rule.Method ?? "";
            string header = GetTasHeaderFromRule(rule);
            return string.Equals(method, "Classification", StringComparison.OrdinalIgnoreCase) ||
                   header.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Grade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Entity Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Thickness", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   header.IndexOf("Parent Entity Attribute Value", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryConvertTasNumber(object value, out double number)
        {
            number = 0.0;
            if (value == null) return false;
            if (value is double d)
            {
                number = d;
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }

            if (value is float f)
            {
                number = f;
                return !double.IsNaN(number) && !double.IsInfinity(number);
            }

            if (value is int i)
            {
                number = i;
                return true;
            }

            if (value is long l)
            {
                number = l;
                return true;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static string MergeTasAuditText(string current, string next, int maxLength)
        {
            string value = (next ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return current ?? "";
            string existing = (current ?? "").Trim();
            if (string.IsNullOrWhiteSpace(existing)) return TruncateTasAuditText(value, maxLength);
            if (existing.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return existing;
            return TruncateTasAuditText(existing + " | " + value, maxLength);
        }

        private static string TruncateTasAuditText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? "";
            return value.Substring(0, Math.Max(0, maxLength - 4)) + " ...";
        }

        private static void WriteTasSettingsAuditSheet(
            dynamic workbook,
            int sheetIndex,
            QsMeasurementSettingsProfile settingsProfile,
            ISet<string> usedSheetNames)
        {
            object worksheetObj = null;
            try
            {
                worksheetObj = workbook.Worksheets[sheetIndex];
                dynamic worksheet = worksheetObj;
                worksheet.Name = GetUniqueExcelSheetName("MHNK_Settings", usedSheetNames);

                List<QsMeasurementSettingRow> rows = (settingsProfile?.Rules ?? new List<QsMeasurementSettingRow>())
                    .Where(r => r != null)
                    .OrderBy(r => GetQsMeasurementCategoryOrder(r.Category))
                    .ThenBy(r => r.SortOrder)
                    .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int rowCount = rows.Count + 3;
                const int colCount = 10;
                var matrix = new object[rowCount, colCount];
                matrix[0, 0] = "Measurement Settings Profile";
                matrix[0, 1] = settingsProfile?.ProfileName ?? "";
                matrix[1, 0] = "Updated";
                matrix[1, 1] = settingsProfile?.UpdatedAtLocal ?? "";
                matrix[2, 0] = "Active";
                matrix[2, 1] = "Element";
                matrix[2, 2] = "Setting Code";
                matrix[2, 3] = "Description";
                matrix[2, 4] = "Option / Theory";
                matrix[2, 5] = "Value";
                matrix[2, 6] = "Unit";
                matrix[2, 7] = "Condition";
                matrix[2, 8] = "Sort Order";
                matrix[2, 9] = "Cubicost TAS Purpose";

                for (int i = 0; i < rows.Count; i++)
                {
                    QsMeasurementSettingRow row = rows[i];
                    int r = i + 3;
                    matrix[r, 0] = row.IsEnabled ? "Yes" : "No";
                    matrix[r, 1] = row.Category ?? "";
                    matrix[r, 2] = row.Code ?? "";
                    matrix[r, 3] = row.Description ?? "";
                    matrix[r, 4] = row.Option ?? "";
                    matrix[r, 5] = row.Value ?? "";
                    matrix[r, 6] = row.Unit ?? "";
                    matrix[r, 7] = row.Method ?? "";
                    matrix[r, 8] = row.SortOrder;
                    matrix[r, 9] = DescribeTasSettingPurpose(row);
                }

                WriteTasAuditMatrix(worksheet, matrix, rowCount, colCount, 3);
            }
            finally
            {
                SafeReleaseCom(worksheetObj);
            }
        }

        private static void WriteTasSourceAuditSheet(
            dynamic workbook,
            int sheetIndex,
            IReadOnlyList<BoqTableRow> sourceRows,
            IReadOnlyList<TasExportSheetSpec> sheets,
            ISet<string> usedSheetNames)
        {
            object worksheetObj = null;
            try
            {
                worksheetObj = workbook.Worksheets[sheetIndex];
                dynamic worksheet = worksheetObj;
                worksheet.Name = GetUniqueExcelSheetName("MHNK_Audit", usedSheetNames);

                var auditRows = new List<object[]>();
                foreach (TasExportSheetSpec sheet in sheets ?? new List<TasExportSheetSpec>())
                {
                    foreach (BoqTableRow row in sourceRows ?? new List<BoqTableRow>())
                    {
                        if (!TasBoqRowMatchesSheet(row, sheet)) continue;
                        auditRows.Add(new object[]
                        {
                            sheet.SheetName ?? "",
                            row.StructureElement ?? "",
                            row.BuildingLevel ?? "",
                            row.Room ?? "",
                            row.TypeName ?? "",
                            row.Quantity,
                            Math.Round(row.TotalVolumeM3, 3),
                            Math.Round(row.TotalFormworkAreaM2, 3),
                            row.QsRuleCode ?? "",
                            row.QsBreakdown ?? ""
                        });
                    }
                }

                int rowCount = auditRows.Count + 1;
                const int colCount = 10;
                var matrix = new object[rowCount, colCount];
                matrix[0, 0] = "TAS Sheet";
                matrix[0, 1] = "MHNK Structure Element";
                matrix[0, 2] = "Floor";
                matrix[0, 3] = "Room";
                matrix[0, 4] = "MHNK Type";
                matrix[0, 5] = "Source Count";
                matrix[0, 6] = "Source Volume(m3)";
                matrix[0, 7] = "Source Formwork(m2)";
                matrix[0, 8] = "MHNK Rule Code";
                matrix[0, 9] = "MHNK Breakdown";

                for (int i = 0; i < auditRows.Count; i++)
                {
                    object[] row = auditRows[i];
                    for (int c = 0; c < colCount; c++)
                    {
                        matrix[i + 1, c] = row[c];
                    }
                }

                WriteTasAuditMatrix(worksheet, matrix, rowCount, colCount, 1);
            }
            finally
            {
                SafeReleaseCom(worksheetObj);
            }
        }

        private static void WriteTasRulesAuditSheet(
            dynamic workbook,
            int sheetIndex,
            QsMeasurementRulesProfile rulesProfile,
            QsMeasurementSettingsProfile settingsProfile,
            ISet<string> usedSheetNames)
        {
            object worksheetObj = null;
            try
            {
                worksheetObj = workbook.Worksheets[sheetIndex];
                dynamic worksheet = worksheetObj;
                worksheet.Name = GetUniqueExcelSheetName("MHNK_Rules", usedSheetNames);

                List<QsMeasurementRuleRow> rows = (rulesProfile?.Rules ?? new List<QsMeasurementRuleRow>())
                    .Where(r => r != null)
                    .OrderBy(r => GetQsMeasurementCategoryOrder(r.Category))
                    .ThenBy(r => r.SortOrder)
                    .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int rowCount = rows.Count + 3;
                const int colCount = 12;
                var matrix = new object[rowCount, colCount];
                matrix[0, 0] = "Measurement Rules Profile";
                matrix[0, 1] = rulesProfile?.ProfileName ?? "";
                matrix[1, 0] = "Updated";
                matrix[1, 1] = rulesProfile?.UpdatedAtLocal ?? "";
                matrix[2, 0] = "Active";
                matrix[2, 1] = "Settings Sync";
                matrix[2, 2] = "Element";
                matrix[2, 3] = "Rule Code";
                matrix[2, 4] = "TAS Sheet";
                matrix[2, 5] = "TAS Header / Rule";
                matrix[2, 6] = "MHNK Mapping / Formula";
                matrix[2, 7] = "Unit";
                matrix[2, 8] = "Rule Type";
                matrix[2, 9] = "Sort Order";
                matrix[2, 10] = "Exported";
                matrix[2, 11] = "Cubicost TAS Purpose";

                for (int i = 0; i < rows.Count; i++)
                {
                    QsMeasurementRuleRow row = rows[i];
                    bool enabledBySettings = ResolveTasRuleEnabledFromSettings(row, settingsProfile);
                    int r = i + 3;
                    matrix[r, 0] = row.IsEnabled ? "Yes" : "No";
                    matrix[r, 1] = row.IsEnabled == enabledBySettings
                        ? "Matches settings"
                        : (enabledBySettings ? "Disabled manually" : "Settings recommend disabled");
                    matrix[r, 2] = row.Category ?? "";
                    matrix[r, 3] = row.Code ?? "";
                    matrix[r, 4] = row.Option ?? "";
                    matrix[r, 5] = GetTasHeaderFromRule(row);
                    matrix[r, 6] = row.Value ?? "";
                    matrix[r, 7] = row.Unit ?? "";
                    matrix[r, 8] = row.Method ?? "";
                    matrix[r, 9] = row.SortOrder;
                    matrix[r, 10] = row.IsEnabled ? "Yes" : "No";
                    matrix[r, 11] = DescribeTasRulePurpose(row);
                }

                WriteTasAuditMatrix(worksheet, matrix, rowCount, colCount, 3);
            }
            finally
            {
                SafeReleaseCom(worksheetObj);
            }
        }

        private static void WriteTasAuditMatrix(dynamic worksheet, object[,] matrix, int rowCount, int colCount, int headerRow)
        {
            object topLeftObj = null;
            object bottomRightObj = null;
            object rangeObj = null;
            object headerRangeObj = null;
            try
            {
                topLeftObj = worksheet.Cells[1, 1];
                bottomRightObj = worksheet.Cells[rowCount, colCount];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Value2 = matrix;

                headerRangeObj = worksheet.Range[worksheet.Cells[headerRow, 1], worksheet.Cells[headerRow, colCount]];
                dynamic headerRange = headerRangeObj;
                headerRange.Font.Bold = true;
                headerRange.Interior.Color = 14277081;

                try
                {
                    worksheet.Rows[headerRow].AutoFilter();
                    worksheet.Columns.AutoFit();
                }
                catch
                {
                    // Audit formatting is optional.
                }
            }
            finally
            {
                SafeReleaseCom(headerRangeObj);
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
            }
        }

        private static string DescribeTasSettingPurpose(QsMeasurementSettingRow row)
        {
            if (row == null) return "";
            string method = row.Method ?? "";
            string code = row.Code ?? "";

            if (string.Equals(method, "Deduction", StringComparison.OrdinalIgnoreCase)) return "Controls whether TAS net quantities deduct intersecting elements, openings, or voids.";
            if (string.Equals(method, "Segmentation", StringComparison.OrdinalIgnoreCase)) return "Defines Cubicost-style staged ranges for side, edge, break, or vertical-surface quantities.";
            if (string.Equals(method, "Elevation", StringComparison.OrdinalIgnoreCase)) return "Defines the lower/upper reference plane used by high-support measurement.";
            if (string.Equals(method, "Numeric", StringComparison.OrdinalIgnoreCase)) return "Defines a Cubicost threshold, stage height, slope angle, percentage, or set value.";
            if (string.Equals(method, "Classification", StringComparison.OrdinalIgnoreCase)) return "Controls how quantities are grouped in TAS-style output.";
            if (string.Equals(method, "Area", StringComparison.OrdinalIgnoreCase)) return "Controls whether this finish/area quantity is measured.";
            if (code.IndexOf("STRUT", StringComparison.OrdinalIgnoreCase) >= 0) return "Controls Cubicost high-support/strutting-high measurement behavior.";
            if (code.IndexOf("OPENING", StringComparison.OrdinalIgnoreCase) >= 0) return "Controls opening deduction, side, bottom, or reveal behavior.";
            return "Controls how MHNK calculates and maps quantities into Cubicost TAS sheets.";
        }

        private static string DescribeTasRulePurpose(QsMeasurementRuleRow row)
        {
            if (row == null) return "";
            string method = row.Method ?? "";
            string code = row.Code ?? "";

            if (string.Equals(method, "Classification", StringComparison.OrdinalIgnoreCase)) return "TAS grouping/classification column.";
            if (string.Equals(method, "Stage Bucket", StringComparison.OrdinalIgnoreCase)) return "TAS staged quantity column driven by high-support or side/edge range settings.";
            if (string.Equals(method, "Deduction", StringComparison.OrdinalIgnoreCase)) return "TAS deduction quantity column.";
            if (code.IndexOf(".QTY.VOLUME", StringComparison.OrdinalIgnoreCase) >= 0) return "TAS concrete volume quantity column.";
            if (code.IndexOf("FORMWORK", StringComparison.OrdinalIgnoreCase) >= 0) return "TAS formwork quantity column.";
            if (code.IndexOf("OPENING", StringComparison.OrdinalIgnoreCase) >= 0) return "TAS opening quantity column.";
            if (code.IndexOf("FIN.", StringComparison.OrdinalIgnoreCase) >= 0 || code.StartsWith("WF.", StringComparison.OrdinalIgnoreCase) || code.StartsWith("CF.", StringComparison.OrdinalIgnoreCase) || code.StartsWith("SC.", StringComparison.OrdinalIgnoreCase) || code.StartsWith("FF.", StringComparison.OrdinalIgnoreCase) || code.StartsWith("WP.", StringComparison.OrdinalIgnoreCase)) return "TAS finish/waterproof output column.";
            return "Maps an MHNK calculated parameter or formula into a Cubicost TAS output header.";
        }

        private static object ResolveTasRuleValue(BoqTableRow row, QsMeasurementRuleRow rule)
        {
            if (row == null || rule == null) return "";

            string header = GetTasHeaderFromRule(rule);
            string mapping = (rule.Value ?? "").Trim();
            string method = (rule.Method ?? "").Trim();

            if (string.Equals(method, "Classification", StringComparison.OrdinalIgnoreCase) ||
                header.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Grade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Entity Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Thickness", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ResolveTasClassificationValue(row, header, mapping);
            }

            double qsValue;
            if (TryResolveTasQsValue(row, rule, mapping, header, out qsValue))
            {
                return Math.Round(qsValue, 3);
            }

            double auditValue;
            if (TryResolveTasAuditValue(row, mapping, header, out auditValue))
            {
                return Math.Round(auditValue, 3);
            }

            if (mapping.IndexOf("Element count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Number", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.Quantity;
            }

            if (mapping.IndexOf("HOST_VOLUME", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("CBIM_Beam_Volume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Math.Round(row.TotalVolumeM3, 3);
            }

            if (mapping.IndexOf("CBIM_FormworkArea", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("CBIM_QsFinishArea", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("Area", StringComparison.OrdinalIgnoreCase) >= 0 ||
                header.IndexOf("formwork", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Math.Round(row.TotalFormworkAreaM2, 3);
            }

            return "";
        }

        private static string ResolveTasClassificationValue(BoqTableRow row, string header, string mapping)
        {
            if (header.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("CBIM_BuildingLevel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.BuildingLevel ?? "";
            }

            if (header.IndexOf("Associated Room", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.IsNullOrWhiteSpace(row.Room) ? "[Null]" : row.Room;
            }

            if (header.IndexOf("Parent Entity Attribute Value", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "-";
            }

            if (header.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("FIN.Room", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.Room ?? "";
            }

            if (header.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("Element/type name", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return NormalizeTasElementName(row.TypeName);
            }

            if (header.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.IsNullOrWhiteSpace(mapping) ? "In-situ Concrete" : mapping;
            }

            if (header.IndexOf("Grade", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ResolveTasConcreteGrade(row);
            }

            if (header.IndexOf("Entity Type", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ResolveTasEntityType(row);
            }

            if (header.IndexOf("Thickness", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ResolveTasThickness(row);
            }

            return NormalizeTasElementName(row.TypeName);
        }

        private static string NormalizeTasElementName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return Regex.Replace(value.Trim(), @"\s*:\s*", ":");
        }

        private static string ResolveTasConcreteGrade(BoqTableRow row)
        {
            string combined = ((row?.TypeName ?? "") + " " + (row?.QsBreakdown ?? "")).Trim();
            Match grade = Regex.Match(combined, @"\bC\s*([1-9]\d{1,2})\b", RegexOptions.IgnoreCase);
            if (grade.Success)
            {
                return "C" + grade.Groups[1].Value;
            }

            string label = NormalizeTasMatchText(row?.StructureElement);
            string name = NormalizeTasMatchText(row?.TypeName);
            if (label.Contains("floor") || label.Contains("slab") || name.Contains("slab"))
            {
                return "C25";
            }

            if (label.Contains("column") || label.Contains("wall") || label.Contains("framing") ||
                label.Contains("beam") || label.Contains("foundation") || IsFoundationLikeRow(row))
            {
                return "C30";
            }

            return "";
        }

        private static string ResolveTasEntityType(BoqTableRow row)
        {
            string label = NormalizeTasMatchText(row?.StructureElement);
            string name = NormalizeTasMatchText(row?.TypeName);

            if (label.Contains("column") || label == "wall" || label.Contains("structuralwall"))
            {
                return "Vertical";
            }

            if (label.Contains("framing") || label.Contains("beam"))
            {
                if (name.Contains("slope") || name.Contains("sloping") || name.Contains("ramp")) return "Sloping";
                return IsLikelyNonConcreteFraming(row) ? "Others" : "Horizontal";
            }

            if (label.Contains("floor") || label.Contains("slab"))
            {
                if (name.Contains("slope") || name.Contains("sloping") || name.Contains("ramp")) return "Sloping";
                return "Horizontal";
            }

            return "Others";
        }

        private static string ResolveTasThickness(BoqTableRow row)
        {
            double thicknessMm;
            if (TryParseThicknessMillimeters(row?.TypeName, out thicknessMm))
            {
                return Math.Round(thicknessMm, 0).ToString("0", CultureInfo.InvariantCulture);
            }

            return "";
        }

        private static bool TryParseThicknessMillimeters(string typeName, out double thicknessMm)
        {
            thicknessMm = 0.0;
            if (string.IsNullOrWhiteSpace(typeName)) return false;

            Match mm = Regex.Match(typeName, @"(?<!\d)(\d{2,4})(?:\.\d+)?\s*mm\b", RegexOptions.IgnoreCase);
            if (mm.Success &&
                double.TryParse(mm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out thicknessMm) &&
                thicknessMm > 0.0)
            {
                return true;
            }

            Match wall = Regex.Match(typeName, @"(?:wall|slab|floor|bb|pts|drop)\D*(\d{2,4})(?!\d)", RegexOptions.IgnoreCase);
            if (wall.Success &&
                double.TryParse(wall.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out thicknessMm) &&
                thicknessMm > 0.0)
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveTasQsValue(BoqTableRow row, QsMeasurementRuleRow rule, string mapping, string header, out double value)
        {
            value = 0.0;
            if (row?.QsValues == null || row.QsValues.Count == 0)
            {
                return false;
            }

            if ((mapping.IndexOf("CBIM_Beam_Width", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 mapping.IndexOf("CBIM_Beam_Depth", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (header.IndexOf("Girth of section", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 row.QsValues.ContainsKey("CBIM_Beam_Width") &&
                 row.QsValues.ContainsKey("CBIM_Beam_Depth")))
            {
                double width;
                double depth;
                row.QsValues.TryGetValue("CBIM_Beam_Width", out width);
                row.QsValues.TryGetValue("CBIM_Beam_Depth", out depth);
                value = 2.0 * (width + depth);
                return true;
            }

            if (header.IndexOf("Girth", StringComparison.OrdinalIgnoreCase) >= 0 &&
                NormalizeTasMatchText(row.StructureElement).Contains("column"))
            {
                double perimeter;
                if (TryParseRectangularPerimeterMeters(row.TypeName, out perimeter))
                {
                    value = perimeter * Math.Max(0, row.Quantity);
                    return true;
                }
            }

            List<string> keys = GetTasQsCandidateKeys(rule, mapping, header);
            bool hasExactQsMapping = false;
            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                hasExactQsMapping = true;
                if (TryGetTasQsValue(row, key, out value))
                {
                    return true;
                }
            }

            if (hasExactQsMapping)
            {
                value = 0.0;
                return true;
            }

            return false;
        }

        private static List<string> GetTasQsCandidateKeys(QsMeasurementRuleRow rule, string mapping, string header)
        {
            var keys = new List<string>();
            string code = rule?.Code ?? "";
            string text = (mapping ?? "") + " " + (header ?? "");

            Match slabStage = Regex.Match(code, @"^SLAB\.QTY\.SOFFIT\.STAGE\.(\d+)$", RegexOptions.IgnoreCase);
            if (!slabStage.Success)
            {
                slabStage = Regex.Match(text, @"FWK\.Floor\.StrutStageArea\s+when\s+stage\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            }

            if (slabStage.Success)
            {
                AddTasQsCandidateKey(keys, "FWK.Floor.StrutStage." + slabStage.Groups[1].Value);
            }

            MatchCollection matches = Regex.Matches(
                text,
                @"\b(?:FWK|FIN|CBIM|HOST|Rebar)[A-Za-z0-9_\.]*\b",
                RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                AddTasQsCandidateKey(keys, match.Value);
            }

            return keys;
        }

        private static void AddTasQsCandidateKey(IList<string> keys, string key)
        {
            if (keys == null || string.IsNullOrWhiteSpace(key)) return;
            string normalized = key.Trim();
            if (keys.Any(k => string.Equals(k, normalized, StringComparison.OrdinalIgnoreCase))) return;
            keys.Add(normalized);
        }

        private static bool TryGetTasQsValue(BoqTableRow row, string key, out double value)
        {
            value = 0.0;
            if (row?.QsValues == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (row.QsValues.TryGetValue(key.Trim(), out value))
            {
                return true;
            }

            if (string.Equals(key.Trim(), "FWK.Floor.StrutBasic", StringComparison.OrdinalIgnoreCase))
            {
                double stageCount;
                if (row.QsValues.TryGetValue("FWK.Floor.StrutStageCount", out stageCount) &&
                    Math.Abs(stageCount) > 1e-9)
                {
                    value = 0.0;
                    return true;
                }

                if (row.QsValues.TryGetValue("FWK.Floor.Bottom", out value))
                {
                    return true;
                }
            }

            Match slabStage = Regex.Match(key.Trim(), @"^FWK\.Floor\.StrutStage\.(\d+)$", RegexOptions.IgnoreCase);
            if (slabStage.Success)
            {
                int stage;
                if (int.TryParse(slabStage.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out stage) && stage > 0)
                {
                    double stageCount;
                    double stageArea;
                    if (row.QsValues.TryGetValue("FWK.Floor.StrutStageCount", out stageCount) &&
                        Math.Abs(Math.Round(stageCount, MidpointRounding.AwayFromZero) - stage) <= 1e-9 &&
                        row.QsValues.TryGetValue("FWK.Floor.StrutStageArea", out stageArea))
                    {
                        value = stageArea / Math.Max(1, stage);
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryResolveTasAuditValue(BoqTableRow row, string mapping, string header, out double value)
        {
            value = 0.0;
            var keys = new List<string>();
            string text = (mapping ?? "") + " " + (header ?? "");

            if (text.IndexOf("Opening.Area", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "OpeningArea", "Area");
            if (text.IndexOf("Opening.Girth", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Girth", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "OpeningGirth", "Girth");
            if (text.IndexOf("Opening.Count", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "OpeningCount");
            if (text.IndexOf("Gross", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Gross");
            if (text.IndexOf("OpeningDeduct", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "OpeningDeduct");
            if (text.IndexOf("Return", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Return");
            if (text.IndexOf("Upturn", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Upturn");
            if (text.IndexOf("Net", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf(".Area", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Net");
            for (int stage = 1; stage <= 15; stage++)
            {
                string stageText = stage.ToString(CultureInfo.InvariantCulture);
                if (text.IndexOf("SideLength.Stage." + stageText, StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "SideLength.Stage." + stageText);
                if (text.IndexOf("SideArea.Stage." + stageText, StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "SideArea.Stage." + stageText);
                if (text.IndexOf("Stage." + stageText, StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Stage." + stageText, "StrutStage." + stageText);
            }
            if (text.IndexOf("SideLength", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "SideLength");
            if (text.IndexOf("SideAreaStaged", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "SideAreaStaged");
            if (text.IndexOf("Sides", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Sides");
            if (text.IndexOf("Bottom", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Soffit", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Bottom");
            if (text.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Top", "TopSlope");
            if (text.IndexOf("Stage.Basic", StringComparison.OrdinalIgnoreCase) >= 0) AddTasAuditKeys(keys, "Stage.Basic");

            foreach (string key in keys)
            {
                if (TrySumAuditValue(row.QsBreakdown, key, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddTasAuditKeys(IList<string> keys, params string[] values)
        {
            if (keys == null || values == null) return;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (keys.Any(k => string.Equals(k, value, StringComparison.OrdinalIgnoreCase))) continue;
                keys.Add(value);
            }
        }

        private static bool TrySumAuditValue(string breakdown, string key, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(breakdown) || string.IsNullOrWhiteSpace(key)) return false;

            string pattern = @"(?:^|[;|]\s*)" + Regex.Escape(key.Trim()) + @"\s*=\s*([+-]?\d+(?:\.\d+)?)";
            MatchCollection matches = Regex.Matches(breakdown, pattern, RegexOptions.IgnoreCase);
            if (matches.Count == 0) return false;

            foreach (Match match in matches)
            {
                double parsed;
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    value += parsed;
                }
            }

            return true;
        }

        private static string GetTasHeaderFromRule(QsMeasurementRuleRow rule)
        {
            string text = rule?.Description ?? "";
            string[] prefixes =
            {
                "Classification Condition:",
                "Stage Bucket:",
                "Deduction:",
                "Quantity:"
            };

            foreach (string prefix in prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return text.Substring(prefix.Length).Trim();
                }
            }

            return string.IsNullOrWhiteSpace(text) ? (rule?.Code ?? "") : text.Trim();
        }

        private static bool TryParseRectangularPerimeterMeters(string typeName, out double perimeter)
        {
            perimeter = 0.0;
            if (string.IsNullOrWhiteSpace(typeName)) return false;

            Match dims = Regex.Match(
                typeName,
                @"(?<!\d)(\d{2,5}(?:\.\d+)?)\s*[xX×]\s*(\d{2,5}(?:\.\d+)?)(?!\d)",
                RegexOptions.IgnoreCase);
            if (!dims.Success) return false;

            double widthMm;
            double depthMm;
            if (!double.TryParse(dims.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out widthMm) ||
                !double.TryParse(dims.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out depthMm) ||
                widthMm <= 0.0 || depthMm <= 0.0)
            {
                return false;
            }

            perimeter = 2.0 * ((widthMm / 1000.0) + (depthMm / 1000.0));
            return true;
        }

        private static bool TasBoqRowMatchesSheet(BoqTableRow row, TasExportSheetSpec spec)
        {
            if (row == null || spec == null) return false;

            string sheet = NormalizeTasMatchText(spec.SheetName);
            if (sheet == "pilecapdef")
            {
                return IsPileCapLikeRow(row) && IsMeasuredConcreteRow(row);
            }

            if (sheet == "raftfoundationdef")
            {
                return IsRaftFoundationLikeRow(row) && IsMeasuredConcreteRow(row);
            }

            if (sheet == "insituslabdef")
            {
                string label = NormalizeTasMatchText(row.StructureElement);
                return (label == "floor" || label.Contains("slab")) &&
                       !IsFoundationLikeRow(row) &&
                       IsMeasuredConcreteRow(row);
            }

            if (sheet == "beamdef")
            {
                string label = NormalizeTasMatchText(row.StructureElement);
                return (label.Contains("framing") || label.Contains("beam")) &&
                       IsMeasuredConcreteRow(row) &&
                       !IsLikelyNonConcreteFraming(row);
            }

            if (sheet == "columndef" || sheet == "walldef")
            {
                return TasBoqRowMatchesCategory(row, spec.Category) && IsMeasuredConcreteRow(row);
            }

            return TasBoqRowMatchesCategory(row, spec.Category);
        }

        private static bool TasBoqRowMatchesCategory(BoqTableRow row, string category)
        {
            if (row == null) return false;
            string label = NormalizeTasMatchText(row.StructureElement);
            string cat = NormalizeTasMatchText(category);
            if (string.IsNullOrWhiteSpace(cat)) return false;

            if (cat == "excavation") return label.Contains("soil") || label.Contains("excavation") || label.Contains("backfilled");
            if (cat == "foundation") return label.Contains("foundation");
            if (cat == "pile") return label.Contains("pile");
            if (cat == "column") return label.Contains("column");
            if (cat == "beam") return label.Contains("framing") || label.Contains("beam");
            if (cat == "wallfinish") return label == "wallfinish";
            if (cat == "wall") return label == "wall" || label == "structuralwall";
            if (cat == "slabopening") return label == "slabopening";
            if (cat == "slab") return label == "floor" || label.Contains("slab");
            if (cat == "kerb") return label == "kerb";
            if (cat == "others") return label == "otherconcrete" || label == "others";
            if (cat == "ceilingfinish") return label == "ceilingfinish";
            if (cat == "suspendedceiling") return label == "suspendedceiling";
            if (cat == "floorfinish") return label == "floorfinish";
            if (cat == "waterproof") return label == "waterproof";
            if (cat == "staircase") return label.Contains("stair");
            if (cat == "lintel") return label.Contains("lintel");
            if (cat == "droppanel") return label.Contains("droppanel");
            if (cat == "eave") return label.Contains("eave");

            return label == cat;
        }

        private static bool IsMeasuredConcreteRow(BoqTableRow row)
        {
            if (row == null) return false;
            if (Math.Abs(row.TotalVolumeM3) > 1e-9 || Math.Abs(row.TotalFormworkAreaM2) > 1e-9) return true;
            if (row.QsValues == null) return false;

            string[] keys =
            {
                "HOST_VOLUME_COMPUTED",
                "CBIM_Beam_Volume",
                "CBIM_FormworkArea",
                "FWK.Col.Sides",
                "FWK.Wall.Sides",
                "FWK.Beam.Stage.Basic",
                "FWK.Floor.Bottom",
                "FWK.Foun.Sides"
            };

            foreach (string key in keys)
            {
                double value;
                if (row.QsValues.TryGetValue(key, out value) && Math.Abs(value) > 1e-9)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFoundationLikeRow(BoqTableRow row)
        {
            string label = NormalizeTasMatchText(row?.StructureElement);
            string name = NormalizeTasMatchText(row?.TypeName);
            return label.Contains("foundation") ||
                   name.Contains("foundation") ||
                   name.Contains("footing") ||
                   name.Contains("pilecap") ||
                   name.Contains("pilecap") ||
                   name.Contains("raft");
        }

        private static bool IsPileCapLikeRow(BoqTableRow row)
        {
            string name = NormalizeTasMatchText(row?.TypeName);
            return IsFoundationLikeRow(row) &&
                   (name.Contains("pilecap") ||
                    name.Contains("foundationrectangular") ||
                    name.Contains("padfoundation") ||
                    Regex.IsMatch(name, @"(^|[^a-z])pc\d", RegexOptions.IgnoreCase));
        }

        private static bool IsRaftFoundationLikeRow(BoqTableRow row)
        {
            string name = NormalizeTasMatchText(row?.TypeName);
            if (!IsFoundationLikeRow(row)) return false;
            if (IsPileCapLikeRow(row)) return false;
            return name.Contains("foundationslab") ||
                   name.Contains("raft") ||
                   name.Contains("footing") ||
                   NormalizeTasMatchText(row?.StructureElement).Contains("foundation");
        }

        private static bool IsLikelyNonConcreteFraming(BoqTableRow row)
        {
            string name = NormalizeTasMatchText(row?.TypeName);
            if (string.IsNullOrWhiteSpace(name)) return false;
            string[] tokens =
            {
                "washer",
                "nut",
                "locknut",
                "purlin",
                "sagrod",
                "cleat",
                "stayplate",
                "stay",
                "bracing",
                "rafter",
                "struttube",
                "bracket",
                "plate",
                "cable"
            };

            return tokens.Any(t => name.Contains(t)) &&
                   !name.Contains("concrete") &&
                   !name.Contains("beamconcreterectangular");
        }

        private static string NormalizeTasMatchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return Regex.Replace(value, @"[^A-Za-z0-9]+", "").ToLowerInvariant();
        }

        private static string GetUniqueExcelSheetName(string requestedName, ISet<string> usedNames)
        {
            string baseName = SanitizeExcelSheetName(requestedName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "TAS";
            }

            string candidate = baseName;
            int suffix = 2;
            while (usedNames.Contains(candidate))
            {
                string suffixText = "_" + suffix.ToString(CultureInfo.InvariantCulture);
                int keep = Math.Max(1, 31 - suffixText.Length);
                candidate = baseName.Length > keep ? baseName.Substring(0, keep) + suffixText : baseName + suffixText;
                suffix++;
            }

            usedNames.Add(candidate);
            return candidate;
        }

        private static string SanitizeExcelSheetName(string requestedName)
        {
            string name = string.IsNullOrWhiteSpace(requestedName) ? "TAS" : requestedName.Trim();
            foreach (char invalid in new[] { '[', ']', ':', '*', '?', '/', '\\' })
            {
                name = name.Replace(invalid, '_');
            }

            if (name.Length > 31)
            {
                name = name.Substring(0, 31);
            }

            return name;
        }
    }
}
