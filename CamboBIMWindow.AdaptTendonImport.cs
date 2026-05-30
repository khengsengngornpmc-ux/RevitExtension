using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private static string _adaptLastFolder = "";

        private enum AdaptTendonLengthUnit
        {
            Millimeter,
            Meter,
            Foot,
            Inch,
            Centimeter
        }

        private sealed class AdaptTendonImportReadResult
        {
            public AdaptTendonImportMode Mode { get; set; } = AdaptTendonImportMode.Model3D;
            public AdaptTendonLengthUnit InferredUnit { get; set; } = AdaptTendonLengthUnit.Millimeter;
            public List<AdaptTendonProfileSegmentPayload> Segments { get; } =
                new List<AdaptTendonProfileSegmentPayload>();
            public int SheetCount { get; set; }
            public int PointCount { get; set; }
            public int SkippedRows { get; set; }
            public int ProfileCount { get; set; }
        }

        private sealed class AdaptTendonPointRow
        {
            public string ProfileName { get; set; } = "";
            public string TendonName { get; set; } = "";
            public string GroupKey { get; set; } = "";
            public int PointIndex { get; set; } = int.MaxValue;
            public int RowOrder { get; set; }
            public double XFt { get; set; }
            public double YFt { get; set; }
            public double ZFt { get; set; }
        }

        private sealed class AdaptTendonHeaderMap
        {
            public int Profile { get; set; } = -1;
            public int Tendon { get; set; } = -1;
            public int Point { get; set; } = -1;
            public int X { get; set; } = -1;
            public int Y { get; set; } = -1;
            public int Z { get; set; } = -1;
            public int Station { get; set; } = -1;
            public int Elevation { get; set; } = -1;
            public int StartX { get; set; } = -1;
            public int StartY { get; set; } = -1;
            public int StartZ { get; set; } = -1;
            public int EndX { get; set; } = -1;
            public int EndY { get; set; } = -1;
            public int EndZ { get; set; } = -1;
            public int StartStation { get; set; } = -1;
            public int StartElevation { get; set; } = -1;
            public int EndStation { get; set; } = -1;
            public int EndElevation { get; set; } = -1;
            public bool HasModelSegments => StartX >= 0 && StartY >= 0 && StartZ >= 0 && EndX >= 0 && EndY >= 0 && EndZ >= 0;
            public bool HasProfileSegments => StartStation >= 0 && StartElevation >= 0 && EndStation >= 0 && EndElevation >= 0;
            public bool HasModelPoints => X >= 0 && Y >= 0 && Z >= 0;
            public bool HasProfilePoints => Station >= 0 && Elevation >= 0;
            public AdaptTendonImportMode Mode =>
                HasModelSegments || HasModelPoints
                    ? AdaptTendonImportMode.Model3D
                    : AdaptTendonImportMode.ProfileDetail;
            public bool HasAnyGeometry => HasModelSegments || HasProfileSegments || HasModelPoints || HasProfilePoints;
        }

        private void OnCad2ModelTasRibbonImportAdaptClick(object sender, RoutedEventArgs e)
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("ADAPT import is available only inside Revit.");
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Import ADAPT Export",
                Filter = "ADAPT files (*.adm;*.dwg;*.dxf;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls)|*.adm;*.dwg;*.dxf;*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls|ADAPT CAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf|ADAPT table files (*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls)|*.csv;*.txt;*.tsv;*.xlsx;*.xlsm;*.xls|ADAPT project files (*.adm)|*.adm|All files (*.*)|*.*",
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

                if (IsAdaptProjectPath(selectedPath))
                {
                    OfferAdaptBuilderHandoff(selectedPath);
                    return;
                }

                if (IsAdaptCadDrawingPath(selectedPath))
                {
                    QueueAdaptCadDrawingImport(selectedPath);
                    return;
                }

                AdaptTendonImportReadResult result = ReadAdaptTendonProfileFile(selectedPath);
                if (result.Segments.Count == 0)
                {
                    ShowStatus("ADAPT import: no tendon/profile segments found. Expected X/Y/Z or Station/Elevation columns.");
                    return;
                }

                _handler.Request.AdaptTendonSourcePath = selectedPath;
                _handler.Request.AdaptTendonImportMode = result.Mode;
                _handler.Request.AdaptTendonProfileSegments = result.Segments;
                _handler.Request.RequestType = CadToModelRequestType.ImportAdaptTendonProfiles;
                _externalEvent.Raise();

                string modeText = result.Mode == AdaptTendonImportMode.Model3D ? "3D model lines" : "profile detail lines";
                ShowStatus(
                    "ADAPT import: queued " +
                    result.Segments.Count.ToString(CultureInfo.InvariantCulture) +
                    " segment(s) from " + Path.GetFileName(selectedPath) +
                    " as " + modeText +
                    " (profiles: " + result.ProfileCount.ToString(CultureInfo.InvariantCulture) +
                    ", points: " + result.PointCount.ToString(CultureInfo.InvariantCulture) +
                    ", units: " + DescribeAdaptUnit(result.InferredUnit) + ").");
            }
            catch (Exception ex)
            {
                ShowStatus("ADAPT import failed: " + ex.Message);
            }
        }

        private void QueueAdaptCadDrawingImport(string path)
        {
            if (_handler == null || _externalEvent == null)
            {
                ShowStatus("ADAPT CAD link is available only inside Revit.");
                return;
            }

            _handler.Request.AdaptCadSourcePath = path;
            _handler.Request.AdaptTendonSourcePath = "";
            _handler.Request.AdaptTendonProfileSegments = new List<AdaptTendonProfileSegmentPayload>();
            _handler.Request.RequestType = CadToModelRequestType.ImportAdaptCadDrawing;
            _externalEvent.Raise();

            ShowStatus("ADAPT CAD: queued link/import for " + Path.GetFileName(path) + ".");
        }

        private void OfferAdaptBuilderHandoff(string path)
        {
            string fileName = Path.GetFileName(path);
            if (TryFindLatestAdaptCadExport(path, out string exportPath))
            {
                FileInfo exportInfo = new FileInfo(exportPath);
                string exportFreshness = BuildAdaptExportFreshnessNote(path, exportInfo);
                MessageBoxResult importExisting = MessageBox.Show(
                    this,
                    "Found a CAD export near this ADAPT project:\n\n" +
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

            MessageBoxResult result = MessageBox.Show(
                this,
                "ADAPT project files cannot be read directly by Revit.\n\nOpen this model in ADAPT-Builder now?\n\nAfter it opens, export the tendon/profile drawing as DWG/DXF, then return to Revit and click Import ADAPT again.",
                "Open ADAPT-Builder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                ShowStatus("ADAPT handoff: export " + fileName + " from ADAPT-Builder as DWG/DXF, then use Import ADAPT again.");
                return;
            }

            if (TryOpenAdaptBuilderProject(path, out string status))
            {
                ShowStatus(status);
            }
            else
            {
                ShowStatus("ADAPT handoff failed: " + status);
            }
        }

        private static bool TryOpenAdaptBuilderProject(string path, out string status)
        {
            string fileName = Path.GetFileName(path);
            try
            {
                var shellStart = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(shellStart);
                status = "ADAPT handoff: opened " + fileName + ". Export DWG/DXF, then run Import ADAPT again.";
                return true;
            }
            catch
            {
            }

            string builderPath = FindAdaptBuilderExecutable();
            if (string.IsNullOrWhiteSpace(builderPath))
            {
                status = "ADAPT-Builder was not found. Open the .adm manually, export DWG/DXF, then run Import ADAPT again.";
                return false;
            }

            try
            {
                var builderStart = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = builderPath,
                    Arguments = QuoteAdaptCommandArgument(path),
                    WorkingDirectory = Path.GetDirectoryName(builderPath),
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(builderStart);
                status = "ADAPT handoff: opened " + fileName + " in ADAPT-Builder. Export DWG/DXF, then run Import ADAPT again.";
                return true;
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        private static string FindAdaptBuilderExecutable()
        {
            string[] candidates =
            {
                @"C:\Program Files (x86)\ADAPT\ADAPT-Builder 2018\builder.exe",
                @"C:\Program Files (x86)\ADAPT\ADAPT-Builder 2019\builder.exe",
                @"C:\Program Files\ADAPT\ADAPT-Builder 2018\builder.exe",
                @"C:\Program Files\ADAPT\ADAPT-Builder 2019\builder.exe"
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "";
        }

        private static string QuoteAdaptCommandArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static bool IsAdaptProjectPath(string path)
        {
            string ext = Path.GetExtension(path) ?? "";
            return string.Equals(ext, ".adm", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildAdaptExportFreshnessNote(string projectPath, FileInfo exportInfo)
        {
            try
            {
                FileInfo projectInfo = new FileInfo(projectPath);
                if (projectInfo.Exists &&
                    exportInfo != null &&
                    exportInfo.Exists &&
                    exportInfo.LastWriteTimeUtc < projectInfo.LastWriteTimeUtc)
                {
                    return "\nNote: this export is older than the .adm model. Re-export from ADAPT if the model changed.";
                }
            }
            catch
            {
            }

            return "";
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
            }
            catch
            {
            }
        }

        private static string GetAdaptInitialDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_adaptLastFolder) && Directory.Exists(_adaptLastFolder))
            {
                return _adaptLastFolder;
            }

            return "";
        }

        private static bool TryFindLatestAdaptCadExport(string projectPath, out string exportPath)
        {
            exportPath = "";
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return false;
            }

            string projectDirectory;
            try
            {
                projectDirectory = Path.GetDirectoryName(projectPath);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                return false;
            }

            string projectName = Path.GetFileNameWithoutExtension(projectPath) ?? "";
            List<string> folders = BuildAdaptExportSearchFolders(projectDirectory);
            List<string> files = new List<string>();
            foreach (string folder in folders)
            {
                AddAdaptCadFiles(folder, files);
            }

            if (files.Count == 0)
            {
                return false;
            }

            string projectToken = NormalizeAdaptFileToken(projectName);
            var best = files
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .Select(info => new
                {
                    Info = info,
                    ProjectNameMatch = !string.IsNullOrWhiteSpace(projectToken) &&
                        NormalizeAdaptFileToken(Path.GetFileNameWithoutExtension(info.Name)).Contains(projectToken)
                })
                .OrderByDescending(item => item.ProjectNameMatch)
                .ThenByDescending(item => item.Info.LastWriteTimeUtc)
                .FirstOrDefault();

            if (best == null)
            {
                return false;
            }

            exportPath = best.Info.FullName;
            return true;
        }

        private static List<string> BuildAdaptExportSearchFolders(string projectDirectory)
        {
            var folders = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFolder(string folder)
            {
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && seen.Add(folder))
                {
                    folders.Add(folder);
                }
            }

            AddFolder(projectDirectory);
            string[] names =
            {
                "Export",
                "Exports",
                "DWG",
                "DXF",
                "CAD",
                "Drawings",
                "Drawing"
            };

            foreach (string name in names)
            {
                AddFolder(Path.Combine(projectDirectory, name));
            }

            return folders;
        }

        private static void AddAdaptCadFiles(string folder, List<string> files)
        {
            if (string.IsNullOrWhiteSpace(folder) || files == null)
            {
                return;
            }

            try
            {
                files.AddRange(Directory.GetFiles(folder, "*.dwg", SearchOption.TopDirectoryOnly));
                files.AddRange(Directory.GetFiles(folder, "*.dxf", SearchOption.TopDirectoryOnly));
            }
            catch
            {
            }
        }

        private static string NormalizeAdaptFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "");
        }

        private static bool IsAdaptCadDrawingPath(string path)
        {
            string ext = Path.GetExtension(path) ?? "";
            return string.Equals(ext, ".dwg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".dxf", StringComparison.OrdinalIgnoreCase);
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new AdaptTendonImportReadResult();
            }

            string ext = Path.GetExtension(path) ?? "";
            if (string.Equals(ext, ".adm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "ADAPT .adm project files are not imported directly. In ADAPT-Builder, export the tendon plan as DWG/DXF or export/copy a tendon profile table to CSV/XLSX, then import that file.");
            }

            if (string.Equals(ext, ".dwg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".dxf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "ADAPT DWG/DXF drawings are handled by the CAD link path. Select the drawing with Import ADAPT so it can be linked into Revit and selected as the CAD2MODEL source.");
            }

            if (string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                return ReadAdaptTendonProfileExcel(path);
            }

            return ReadAdaptTendonProfileText(path);
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileText(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            var rows = new List<string[]>();
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    rows.Add(new string[0]);
                    continue;
                }

                rows.Add(SplitAdaptDelimitedLine(line).ToArray());
            }

            AdaptTendonImportReadResult result = ReadAdaptTendonProfileRows(rows);
            result.SheetCount = result.Segments.Count > 0 ? 1 : 0;
            return result;
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileExcel(string path)
        {
            var result = new AdaptTendonImportReadResult();
            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    throw new InvalidOperationException("Microsoft Excel is not available. Export from ADAPT as CSV/TXT, or install Excel.");
                }

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
                            object[,] values = NormalizeExcelRangeToMatrix(usedRange.Value2);
                            if (values == null)
                            {
                                continue;
                            }

                            AdaptTendonImportReadResult sheetResult = ReadAdaptTendonProfileRows(BuildRowsFromExcelMatrix(values));
                            if (sheetResult.Segments.Count == 0)
                            {
                                result.SkippedRows += sheetResult.SkippedRows;
                                continue;
                            }

                            if (result.Segments.Count > 0 && result.Mode != sheetResult.Mode)
                            {
                                continue;
                            }

                            result.Mode = sheetResult.Mode;
                            result.InferredUnit = sheetResult.InferredUnit;
                            result.PointCount += sheetResult.PointCount;
                            result.SkippedRows += sheetResult.SkippedRows;
                            result.Segments.AddRange(sheetResult.Segments);
                            result.SheetCount++;
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

            result.ProfileCount = result.Segments
                .Select(BuildAdaptSegmentGroupKey)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return result;
        }

        private static List<string[]> BuildRowsFromExcelMatrix(object[,] values)
        {
            var rows = new List<string[]>();
            if (values == null)
            {
                return rows;
            }

            int rMin = values.GetLowerBound(0);
            int rMax = values.GetUpperBound(0);
            int cMin = values.GetLowerBound(1);
            int cMax = values.GetUpperBound(1);

            for (int r = rMin; r <= rMax; r++)
            {
                var cells = new List<string>();
                bool any = false;
                for (int c = cMin; c <= cMax; c++)
                {
                    string text = ToCellString(values[r, c]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        any = true;
                    }

                    cells.Add(text);
                }

                rows.Add(any ? cells.ToArray() : new string[0]);
            }

            return rows;
        }

        private static AdaptTendonImportReadResult ReadAdaptTendonProfileRows(IReadOnlyList<string[]> rows)
        {
            var result = new AdaptTendonImportReadResult();
            if (rows == null || rows.Count == 0)
            {
                return result;
            }

            int headerRow = FindAdaptTendonHeaderRow(rows, out AdaptTendonHeaderMap map);
            if (headerRow < 0 || map == null || !map.HasAnyGeometry)
            {
                return result;
            }

            string[] headers = rows[headerRow] ?? new string[0];
            result.Mode = map.Mode;
            result.InferredUnit = InferAdaptDefaultUnit(rows, headerRow + 1, map);

            if (map.HasModelSegments || map.HasProfileSegments)
            {
                AppendAdaptTendonSegments(rows, headerRow + 1, headers, map, result);
            }
            else
            {
                AppendAdaptTendonPointSegments(rows, headerRow + 1, headers, map, result);
            }

            result.ProfileCount = result.Segments
                .Select(BuildAdaptSegmentGroupKey)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return result;
        }

        private static void AppendAdaptTendonSegments(
            IReadOnlyList<string[]> rows,
            int firstDataRow,
            IReadOnlyList<string> headers,
            AdaptTendonHeaderMap map,
            AdaptTendonImportReadResult result)
        {
            for (int i = firstDataRow; i < rows.Count; i++)
            {
                string[] cells = rows[i] ?? new string[0];
                if (cells.Length == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                string profile = GetAdaptCell(cells, map.Profile);
                string tendon = GetAdaptCell(cells, map.Tendon);
                string sourceLabel = BuildAdaptSourceLabel(profile, tendon, i + 1);

                bool ok;
                AdaptTendonProfileSegmentPayload segment;
                if (map.HasModelSegments)
                {
                    ok = TryReadAdaptSegment(cells, headers, result.InferredUnit, map.StartX, map.StartY, map.StartZ, map.EndX, map.EndY, map.EndZ, out segment);
                }
                else
                {
                    ok = TryReadAdaptSegment(cells, headers, result.InferredUnit, map.StartStation, -1, map.StartElevation, map.EndStation, -1, map.EndElevation, out segment);
                }

                if (!ok)
                {
                    result.SkippedRows++;
                    continue;
                }

                segment.ProfileName = profile;
                segment.TendonName = tendon;
                segment.SourceLabel = sourceLabel;
                result.Segments.Add(segment);
                result.PointCount += 2;
            }
        }

        private static void AppendAdaptTendonPointSegments(
            IReadOnlyList<string[]> rows,
            int firstDataRow,
            IReadOnlyList<string> headers,
            AdaptTendonHeaderMap map,
            AdaptTendonImportReadResult result)
        {
            var pointsByGroup = new Dictionary<string, List<AdaptTendonPointRow>>(StringComparer.OrdinalIgnoreCase);
            int rowOrder = 0;

            for (int i = firstDataRow; i < rows.Count; i++)
            {
                string[] cells = rows[i] ?? new string[0];
                if (cells.Length == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                string profile = GetAdaptCell(cells, map.Profile);
                string tendon = GetAdaptCell(cells, map.Tendon);
                string groupKey = BuildAdaptGroupKey(profile, tendon);
                if (string.IsNullOrWhiteSpace(groupKey))
                {
                    groupKey = "ADAPT Profile";
                }

                if (!TryReadAdaptPoint(cells, headers, result.InferredUnit, map, rowOrder, profile, tendon, groupKey, out AdaptTendonPointRow point))
                {
                    result.SkippedRows++;
                    continue;
                }

                if (!pointsByGroup.TryGetValue(groupKey, out List<AdaptTendonPointRow> list))
                {
                    list = new List<AdaptTendonPointRow>();
                    pointsByGroup[groupKey] = list;
                }

                list.Add(point);
                result.PointCount++;
                rowOrder++;
            }

            foreach (KeyValuePair<string, List<AdaptTendonPointRow>> pair in pointsByGroup)
            {
                List<AdaptTendonPointRow> ordered = pair.Value
                    .OrderBy(p => p.PointIndex)
                    .ThenBy(p => p.RowOrder)
                    .ToList();

                for (int i = 1; i < ordered.Count; i++)
                {
                    AdaptTendonPointRow a = ordered[i - 1];
                    AdaptTendonPointRow b = ordered[i];
                    if (GetAdaptDistanceFt(a.XFt, a.YFt, a.ZFt, b.XFt, b.YFt, b.ZFt) < 1.0e-6)
                    {
                        continue;
                    }

                    result.Segments.Add(new AdaptTendonProfileSegmentPayload
                    {
                        ProfileName = !string.IsNullOrWhiteSpace(a.ProfileName) ? a.ProfileName : b.ProfileName,
                        TendonName = !string.IsNullOrWhiteSpace(a.TendonName) ? a.TendonName : b.TendonName,
                        SourceLabel = pair.Key,
                        X0Ft = a.XFt,
                        Y0Ft = a.YFt,
                        Z0Ft = a.ZFt,
                        X1Ft = b.XFt,
                        Y1Ft = b.YFt,
                        Z1Ft = b.ZFt
                    });
                }
            }
        }

        private static bool TryReadAdaptPoint(
            string[] cells,
            IReadOnlyList<string> headers,
            AdaptTendonLengthUnit defaultUnit,
            AdaptTendonHeaderMap map,
            int rowOrder,
            string profile,
            string tendon,
            string groupKey,
            out AdaptTendonPointRow point)
        {
            point = null;
            int pointIndex = int.MaxValue;
            if (map.Point >= 0)
            {
                string pointRaw = GetAdaptCell(cells, map.Point);
                if (int.TryParse(pointRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
                {
                    pointIndex = parsedIndex;
                }
            }

            double xFt;
            double yFt;
            double zFt;
            if (map.HasModelPoints)
            {
                if (!TryReadAdaptLengthCell(cells, headers, map.X, defaultUnit, out xFt) ||
                    !TryReadAdaptLengthCell(cells, headers, map.Y, defaultUnit, out yFt) ||
                    !TryReadAdaptLengthCell(cells, headers, map.Z, defaultUnit, out zFt))
                {
                    return false;
                }
            }
            else if (map.HasProfilePoints)
            {
                if (!TryReadAdaptLengthCell(cells, headers, map.Station, defaultUnit, out xFt) ||
                    !TryReadAdaptLengthCell(cells, headers, map.Elevation, defaultUnit, out zFt))
                {
                    return false;
                }

                yFt = 0.0;
            }
            else
            {
                return false;
            }

            point = new AdaptTendonPointRow
            {
                ProfileName = profile ?? "",
                TendonName = tendon ?? "",
                GroupKey = groupKey ?? "",
                PointIndex = pointIndex,
                RowOrder = rowOrder,
                XFt = xFt,
                YFt = yFt,
                ZFt = zFt
            };
            return true;
        }

        private static bool TryReadAdaptSegment(
            string[] cells,
            IReadOnlyList<string> headers,
            AdaptTendonLengthUnit defaultUnit,
            int x0,
            int y0,
            int z0,
            int x1,
            int y1,
            int z1,
            out AdaptTendonProfileSegmentPayload segment)
        {
            segment = null;

            if (!TryReadAdaptLengthCell(cells, headers, x0, defaultUnit, out double sx) ||
                !TryReadAdaptLengthCell(cells, headers, z0, defaultUnit, out double sz) ||
                !TryReadAdaptLengthCell(cells, headers, x1, defaultUnit, out double ex) ||
                !TryReadAdaptLengthCell(cells, headers, z1, defaultUnit, out double ez))
            {
                return false;
            }

            double sy = 0.0;
            double ey = 0.0;
            if (y0 >= 0 && !TryReadAdaptLengthCell(cells, headers, y0, defaultUnit, out sy))
            {
                return false;
            }

            if (y1 >= 0 && !TryReadAdaptLengthCell(cells, headers, y1, defaultUnit, out ey))
            {
                return false;
            }

            if (GetAdaptDistanceFt(sx, sy, sz, ex, ey, ez) < 1.0e-6)
            {
                return false;
            }

            segment = new AdaptTendonProfileSegmentPayload
            {
                X0Ft = sx,
                Y0Ft = sy,
                Z0Ft = sz,
                X1Ft = ex,
                Y1Ft = ey,
                Z1Ft = ez
            };
            return true;
        }

        private static int FindAdaptTendonHeaderRow(IReadOnlyList<string[]> rows, out AdaptTendonHeaderMap map)
        {
            map = null;
            int scanMax = Math.Min(rows.Count - 1, 80);
            for (int i = 0; i <= scanMax; i++)
            {
                string[] headers = rows[i] ?? new string[0];
                if (headers.Length == 0)
                {
                    continue;
                }

                AdaptTendonHeaderMap candidate = BuildAdaptTendonHeaderMap(headers);
                if (candidate.HasAnyGeometry)
                {
                    map = candidate;
                    return i;
                }
            }

            return -1;
        }

        private static AdaptTendonHeaderMap BuildAdaptTendonHeaderMap(IReadOnlyList<string> headers)
        {
            return new AdaptTendonHeaderMap
            {
                Profile = IndexOfAdaptHeader(headers, "Profile", "Profile Name", "Tendon Profile", "Profile ID", "Group", "Group Name"),
                Tendon = IndexOfAdaptHeader(headers, "Tendon", "Tendon Name", "Tendon ID", "Tendon Mark", "Tendon No", "Cable", "Cable Name", "Name", "ID", "Support Line", "Strip", "Frame", "Span"),
                Point = IndexOfAdaptHeader(headers, "Point", "Point No", "Point Number", "Pt", "No", "Index", "Order", "Sequence", "Seq"),
                X = IndexOfAdaptHeader(headers, "X", "X Coord", "X Coordinate", "Coord X", "Point X", "Plan X", "Global X", "X Location", "Plan X Coordinate"),
                Y = IndexOfAdaptHeader(headers, "Y", "Y Coord", "Y Coordinate", "Coord Y", "Point Y", "Plan Y", "Global Y", "Y Location", "Plan Y Coordinate"),
                Z = IndexOfAdaptHeader(headers, "Z", "Z Coord", "Z Coordinate", "Coord Z", "Elevation", "Elev", "Tendon Elevation", "Profile Elevation", "Height", "CGS", "Tendon CGS", "Profile CGS"),
                Station = IndexOfAdaptHeader(headers, "Station", "Chainage", "Distance", "Distance From Start", "DistanceFromStart", "Location", "Sta", "Distance Along", "Along", "Position", "Profile Station"),
                Elevation = IndexOfAdaptHeader(headers, "Elevation", "Elev", "Profile Elevation", "Tendon Elevation", "Z", "Height", "CGS", "Tendon CGS", "Profile CGS", "Profile Height"),
                StartX = IndexOfAdaptHeader(headers, "Start X", "X Start", "Begin X", "X0", "X1"),
                StartY = IndexOfAdaptHeader(headers, "Start Y", "Y Start", "Begin Y", "Y0", "Y1"),
                StartZ = IndexOfAdaptHeader(headers, "Start Z", "Z Start", "Begin Z", "Start Elevation", "Start Elev", "Start Height", "Start CGS", "Z0", "Z1"),
                EndX = IndexOfAdaptHeader(headers, "End X", "X End", "Finish X", "X2"),
                EndY = IndexOfAdaptHeader(headers, "End Y", "Y End", "Finish Y", "Y2"),
                EndZ = IndexOfAdaptHeader(headers, "End Z", "Z End", "Finish Z", "End Elevation", "End Elev", "End Height", "End CGS", "Z2"),
                StartStation = IndexOfAdaptHeader(headers, "Start Station", "Station Start", "Begin Station", "Start Chainage", "Start Distance", "Start Distance Along"),
                StartElevation = IndexOfAdaptHeader(headers, "Start Elevation", "Start Elev", "Begin Elevation", "Begin Elev", "Start Z", "Start Height", "Start CGS"),
                EndStation = IndexOfAdaptHeader(headers, "End Station", "Station End", "Finish Station", "End Chainage", "End Distance", "End Distance Along"),
                EndElevation = IndexOfAdaptHeader(headers, "End Elevation", "End Elev", "Finish Elevation", "Finish Elev", "End Z", "End Height", "End CGS")
            };
        }

        private static AdaptTendonLengthUnit InferAdaptDefaultUnit(
            IReadOnlyList<string[]> rows,
            int firstDataRow,
            AdaptTendonHeaderMap map)
        {
            double maxAbs = 0.0;
            int[] indexes = map.Mode == AdaptTendonImportMode.Model3D
                ? new[] { map.X, map.Y, map.Z, map.StartX, map.StartY, map.StartZ, map.EndX, map.EndY, map.EndZ }
                : new[] { map.Station, map.Elevation, map.StartStation, map.StartElevation, map.EndStation, map.EndElevation };

            int scanMax = Math.Min(rows.Count - 1, firstDataRow + 200);
            for (int r = firstDataRow; r <= scanMax; r++)
            {
                string[] cells = rows[r] ?? new string[0];
                foreach (int index in indexes)
                {
                    if (index < 0 || index >= cells.Length)
                    {
                        continue;
                    }

                    if (TryParseDouble(CleanAdaptNumber(cells[index]), out double value))
                    {
                        maxAbs = Math.Max(maxAbs, Math.Abs(value));
                    }
                }
            }

            return maxAbs > 500.0 ? AdaptTendonLengthUnit.Millimeter : AdaptTendonLengthUnit.Meter;
        }

        private static bool TryReadAdaptLengthCell(
            IReadOnlyList<string> cells,
            IReadOnlyList<string> headers,
            int index,
            AdaptTendonLengthUnit defaultUnit,
            out double feet)
        {
            feet = 0.0;
            if (index < 0 || index >= cells.Count)
            {
                return false;
            }

            string raw = CleanAdaptNumber(cells[index]);
            if (!TryParseDouble(raw, out double value))
            {
                return false;
            }

            string header = index >= 0 && index < headers.Count ? headers[index] : "";
            feet = ConvertAdaptLengthToFeet(value, ResolveAdaptUnit(header, defaultUnit));
            return true;
        }

        private static AdaptTendonLengthUnit ResolveAdaptUnit(string header, AdaptTendonLengthUnit defaultUnit)
        {
            string text = (header ?? "").Trim().ToLowerInvariant();
            if (text.Contains("mm") || text.Contains("millimeter") || text.Contains("millimetre")) return AdaptTendonLengthUnit.Millimeter;
            if (text.Contains("cm") || text.Contains("centimeter") || text.Contains("centimetre")) return AdaptTendonLengthUnit.Centimeter;
            if (text.Contains("inch") || text.Contains("(in)") || text.Contains("[in]")) return AdaptTendonLengthUnit.Inch;
            if (text.Contains("feet") || text.Contains("foot") || text.Contains("(ft)") || text.Contains("[ft]")) return AdaptTendonLengthUnit.Foot;
            if (text.Contains("meter") || text.Contains("metre") || text.Contains("(m)") || text.Contains("[m]")) return AdaptTendonLengthUnit.Meter;
            return defaultUnit;
        }

        private static double ConvertAdaptLengthToFeet(double value, AdaptTendonLengthUnit unit)
        {
            switch (unit)
            {
                case AdaptTendonLengthUnit.Foot:
                    return value;
                case AdaptTendonLengthUnit.Inch:
                    return value / 12.0;
                case AdaptTendonLengthUnit.Meter:
                    return value / 0.3048;
                case AdaptTendonLengthUnit.Centimeter:
                    return value / 30.48;
                case AdaptTendonLengthUnit.Millimeter:
                default:
                    return value / 304.8;
            }
        }

        private static string DescribeAdaptUnit(AdaptTendonLengthUnit unit)
        {
            switch (unit)
            {
                case AdaptTendonLengthUnit.Foot:
                    return "ft";
                case AdaptTendonLengthUnit.Inch:
                    return "in";
                case AdaptTendonLengthUnit.Meter:
                    return "m";
                case AdaptTendonLengthUnit.Centimeter:
                    return "cm";
                case AdaptTendonLengthUnit.Millimeter:
                default:
                    return "mm";
            }
        }

        private static string CleanAdaptNumber(string raw)
        {
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            text = Regex.Replace(text, "(mm|cm|ft|in|m)", "", RegexOptions.IgnoreCase)
                .Replace("\"", "")
                .Replace("'", "")
                .Trim();
            return text;
        }

        private static string GetAdaptCell(IReadOnlyList<string> cells, int index)
        {
            if (index < 0 || index >= cells.Count)
            {
                return "";
            }

            return (cells[index] ?? "").Trim();
        }

        private static List<string> SplitAdaptDelimitedLine(string line)
        {
            string text = line ?? "";
            int tabCount = text.Count(c => c == '\t');
            int commaCount = text.Count(c => c == ',');
            int semicolonCount = text.Count(c => c == ';');

            if (tabCount > 0 && tabCount >= commaCount && tabCount >= semicolonCount)
            {
                return text.Split('\t').Select(c => (c ?? "").Trim()).ToList();
            }

            if (semicolonCount > commaCount)
            {
                return text.Split(';').Select(c => (c ?? "").Trim()).ToList();
            }

            return ParseCsvLine(text).Select(c => (c ?? "").Trim()).ToList();
        }

        private static int IndexOfAdaptHeader(IReadOnlyList<string> headers, params string[] names)
        {
            if (headers == null || names == null)
            {
                return -1;
            }

            var targetTokens = names
                .Select(NormalizeAdaptHeaderToken)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            for (int i = 0; i < headers.Count; i++)
            {
                string token = NormalizeAdaptHeaderToken(headers[i]);
                if (targetTokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase)))
                {
                    return i;
                }
            }

            for (int i = 0; i < headers.Count; i++)
            {
                string token = NormalizeAdaptHeaderToken(headers[i]);
                if (targetTokens.Any(t => t.Length > 2 && token.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeAdaptHeaderToken(string raw)
        {
            string text = NormalizeImportedHeaderCell(raw ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            int unitStart = text.IndexOf('(');
            if (unitStart >= 0)
            {
                text = text.Substring(0, unitStart);
            }

            unitStart = text.IndexOf('[');
            if (unitStart >= 0)
            {
                text = text.Substring(0, unitStart);
            }

            var builder = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string BuildAdaptSourceLabel(string profile, string tendon, int rowNumber)
        {
            string group = BuildAdaptGroupKey(profile, tendon);
            if (!string.IsNullOrWhiteSpace(group))
            {
                return group;
            }

            return "row " + rowNumber.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildAdaptGroupKey(string profile, string tendon)
        {
            string p = (profile ?? "").Trim();
            string t = (tendon ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(p) && !string.IsNullOrWhiteSpace(t) &&
                !string.Equals(p, t, StringComparison.OrdinalIgnoreCase))
            {
                return p + " / " + t;
            }

            if (!string.IsNullOrWhiteSpace(t)) return t;
            if (!string.IsNullOrWhiteSpace(p)) return p;
            return "";
        }

        private static string BuildAdaptSegmentGroupKey(AdaptTendonProfileSegmentPayload segment)
        {
            if (segment == null)
            {
                return "";
            }

            return BuildAdaptGroupKey(segment.ProfileName, segment.TendonName);
        }

        private static double GetAdaptDistanceFt(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            double dz = z1 - z0;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }
}
