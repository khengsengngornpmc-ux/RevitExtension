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
        private const string AdaptSettingsDirectoryName = "DRAWING_PT";
        private const string AdaptSettingsFileName = "adapt-import.settings";
        private static string _adaptLastFolder = LoadAdaptSettingValue("LastFolder");
        private static string _adaptLastProjectPath = LoadAdaptSettingValue("LastProject");
        private static AdaptCadImportMode _adaptCadImportMode = LoadAdaptCadImportMode();

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

        private sealed class AdaptAdmPoint
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        private sealed class AdaptAdmTendonRecord
        {
            public string LayerToken { get; set; } = "";
            public string LayerName { get; set; } = "";
            public string TendonName { get; set; } = "";
            public List<AdaptAdmPoint> Points { get; } = new List<AdaptAdmPoint>();
        }

        private sealed class AdaptAdmPointBlock
        {
            public int CountOffset { get; set; }
            public int EndOffset { get; set; }
            public double PathLength { get; set; }
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
            public double MinZ { get; set; }
            public double MaxZ { get; set; }
            public List<AdaptAdmPoint> Points { get; } = new List<AdaptAdmPoint>();
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
                    HandleAdaptProjectWithoutLaunchingBuilder(selectedPath);
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

                QueueAdaptTendonProfileImport(selectedPath, result, "ADAPT import");
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
                    if (TryOfferRecentAdaptProjectCadExport())
                    {
                        return;
                    }

                    OnCad2ModelTasRibbonImportAdaptClick(this, new RoutedEventArgs());
                }),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void QueueAdaptCadDrawingImport(string path)
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

            RememberAdaptPath(path);
            _handler.Request.AdaptCadSourcePath = path;
            _handler.Request.AdaptCadImportMode = importMode;
            _handler.Request.AdaptTendonSourcePath = "";
            _handler.Request.AdaptTendonProfileSegments = new List<AdaptTendonProfileSegmentPayload>();
            _handler.Request.RequestType = CadToModelRequestType.ImportAdaptCadDrawing;
            _externalEvent.Raise();

            ShowStatus("ADAPT CAD: queued " + DescribeAdaptCadImportMode(importMode) + " for " + Path.GetFileName(path) + ".");
        }

        private void QueueAdaptTendonProfileImport(string path, AdaptTendonImportReadResult result, string statusPrefix)
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

            _handler.Request.AdaptTendonSourcePath = path;
            _handler.Request.AdaptTendonImportMode = result.Mode;
            _handler.Request.AdaptTendonProfileSegments = result.Segments;
            _handler.Request.AdaptCadSourcePath = "";
            _handler.Request.RequestType = CadToModelRequestType.ImportAdaptTendonProfiles;
            _externalEvent.Raise();

            string modeText = IsAdaptProjectPath(path)
                ? "3D tendon profile segments"
                : (result.Mode == AdaptTendonImportMode.Model3D ? "3D model lines" : "profile detail lines");
            string prefix = string.IsNullOrWhiteSpace(statusPrefix) ? "ADAPT import" : statusPrefix.Trim();
            ShowStatus(
                prefix + ": queued " +
                result.Segments.Count.ToString(CultureInfo.InvariantCulture) +
                " segment(s) from " + Path.GetFileName(path) +
                " as " + modeText +
                " (profiles: " + result.ProfileCount.ToString(CultureInfo.InvariantCulture) +
                ", points: " + result.PointCount.ToString(CultureInfo.InvariantCulture) +
                ", units: " + DescribeAdaptUnit(result.InferredUnit) + ").");
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
                !TryFindLatestAdaptCadExport(_adaptLastProjectPath, out string exportPath))
            {
                return false;
            }

            FileInfo exportInfo = new FileInfo(exportPath);
            string projectName = Path.GetFileName(_adaptLastProjectPath);
            string exportFreshness = BuildAdaptExportFreshnessNote(_adaptLastProjectPath, exportInfo);
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

            if (TryFindLatestAdaptCadExport(path, out string exportPath))
            {
                FileInfo exportInfo = new FileInfo(exportPath);
                string exportFreshness = BuildAdaptExportFreshnessNote(path, exportInfo);
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

                if (IsAdaptProjectPath(path))
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
        }

        private static AdaptCadImportMode LoadAdaptCadImportMode()
        {
            string value = LoadAdaptSettingValue("CadImportMode");
            return string.Equals(value, "ImportOnly", StringComparison.OrdinalIgnoreCase)
                ? AdaptCadImportMode.ImportOnly
                : AdaptCadImportMode.LinkPreferred;
        }

        private static string LoadAdaptSettingValue(string key)
        {
            try
            {
                string settingsPath = GetAdaptSettingsPath();
                if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                {
                    return "";
                }

                string prefix = key + "=";
                foreach (string line in File.ReadAllLines(settingsPath, Encoding.UTF8))
                {
                    if (line != null && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Substring(prefix.Length).Trim();
                        if (string.Equals(key, "LastFolder", StringComparison.OrdinalIgnoreCase))
                        {
                            return Directory.Exists(value) ? value : "";
                        }

                        if (string.Equals(key, "LastProject", StringComparison.OrdinalIgnoreCase))
                        {
                            return File.Exists(value) ? value : "";
                        }

                        return value;
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static void SaveAdaptSettings()
        {
            try
            {
                string settingsPath = GetAdaptSettingsPath();
                if (string.IsNullOrWhiteSpace(settingsPath))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(_adaptLastFolder) && Directory.Exists(_adaptLastFolder))
                {
                    lines.Add("LastFolder=" + _adaptLastFolder);
                }

                if (!string.IsNullOrWhiteSpace(_adaptLastProjectPath) && File.Exists(_adaptLastProjectPath))
                {
                    lines.Add("LastProject=" + _adaptLastProjectPath);
                }

                lines.Add("CadImportMode=" + _adaptCadImportMode);

                File.WriteAllLines(settingsPath, lines, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string GetAdaptSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                return "";
            }

            return Path.Combine(appData, "MHNK", "RevitExtension", AdaptSettingsDirectoryName, AdaptSettingsFileName);
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

        private static string DescribeAdaptCadImportMode(AdaptCadImportMode mode)
        {
            return mode == AdaptCadImportMode.ImportOnly ? "import into model" : "link preferred";
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
                return ReadAdaptAdmTendonGeometry(path);
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

        private static AdaptTendonImportReadResult ReadAdaptAdmTendonGeometry(string path)
        {
            var result = new AdaptTendonImportReadResult
            {
                Mode = AdaptTendonImportMode.Model3D,
                InferredUnit = AdaptTendonLengthUnit.Meter,
                SheetCount = 1
            };

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return result;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Dictionary<string, string> layerNames = BuildAdaptAdmLayerNameMap(bytes);
            List<AdaptAdmTendonRecord> records = ReadAdaptAdmTendonRecords(bytes, layerNames);
            if (records.Count == 0)
            {
                return result;
            }

            double maxAbs = 0.0;
            foreach (AdaptAdmTendonRecord record in records)
            {
                foreach (AdaptAdmPoint point in record.Points)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(point.X));
                    maxAbs = Math.Max(maxAbs, Math.Abs(point.Y));
                    maxAbs = Math.Max(maxAbs, Math.Abs(point.Z));
                }
            }

            result.InferredUnit = maxAbs > 500.0
                ? AdaptTendonLengthUnit.Millimeter
                : AdaptTendonLengthUnit.Meter;

            foreach (AdaptAdmTendonRecord record in records)
            {
                for (int i = 1; i < record.Points.Count; i++)
                {
                    AdaptAdmPoint a = record.Points[i - 1];
                    AdaptAdmPoint b = record.Points[i];
                    ConvertAdaptAdmPointToRevitFeet(a, result.InferredUnit, out double ax, out double ay, out double az);
                    ConvertAdaptAdmPointToRevitFeet(b, result.InferredUnit, out double bx, out double by, out double bz);

                    if (GetAdaptDistanceFt(ax, ay, az, bx, by, bz) < 1.0e-6)
                    {
                        continue;
                    }

                    result.Segments.Add(new AdaptTendonProfileSegmentPayload
                    {
                        ProfileName = !string.IsNullOrWhiteSpace(record.LayerName) ? record.LayerName : record.LayerToken,
                        TendonName = record.TendonName,
                        SourceLabel = BuildAdaptGroupKey(record.LayerName, record.TendonName),
                        X0Ft = ax,
                        Y0Ft = ay,
                        Z0Ft = az,
                        X1Ft = bx,
                        Y1Ft = by,
                        Z1Ft = bz
                    });
                }

                result.PointCount += record.Points.Count;
            }

            result.ProfileCount = result.Segments
                .Select(BuildAdaptSegmentGroupKey)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return result;
        }

        private static List<AdaptAdmTendonRecord> ReadAdaptAdmTendonRecords(byte[] bytes, IDictionary<string, string> layerNames)
        {
            var records = new List<AdaptAdmTendonRecord>();
            if (bytes == null || bytes.Length == 0)
            {
                return records;
            }

            byte[] recordName = Encoding.ASCII.GetBytes("ADPTTendonE");
            List<int> offsets = FindAdaptAdmAsciiOccurrences(bytes, recordName).ToList();
            for (int i = 0; i < offsets.Count; i++)
            {
                int offset = offsets[i];
                int endOffset = i + 1 < offsets.Count
                    ? offsets[i + 1]
                    : Math.Min(bytes.Length, offset + 80000);

                if (!TryReadAdaptAdmTendonRecords(bytes, offset, endOffset, layerNames, out List<AdaptAdmTendonRecord> tendonRecords))
                {
                    continue;
                }

                records.AddRange(tendonRecords);
            }

            return records;
        }

        private static bool TryReadAdaptAdmTendonRecords(
            byte[] bytes,
            int recordNameOffset,
            int recordEndOffset,
            IDictionary<string, string> layerNames,
            out List<AdaptAdmTendonRecord> records)
        {
            records = null;
            if (bytes == null || recordNameOffset < 0 || recordNameOffset >= bytes.Length)
            {
                return false;
            }

            byte[] continuous = Encoding.ASCII.GetBytes("CONTINUOUS");
            if (!TryFindAdaptAdmAscii(bytes, continuous, recordNameOffset, Math.Min(bytes.Length, recordNameOffset + 220), out int continuousOffset))
            {
                return false;
            }

            int position = continuousOffset + continuous.Length;
            if (!TryReadAdaptAdmString(bytes, position, out string layerToken, out position) ||
                !TryReadAdaptAdmString(bytes, position, out string tendonName, out position))
            {
                return false;
            }

            string layerName = "";
            if (layerNames != null && !string.IsNullOrWhiteSpace(layerToken))
            {
                layerNames.TryGetValue(layerToken, out layerName);
            }

            if (!IsAdaptAdmTendonLayer(layerToken, layerName))
            {
                return false;
            }

            AdaptAdmPointBlock planBlock = null;
            if (!TryReadAdaptAdmPointBlock(bytes, position + 48, recordEndOffset, out planBlock))
            {
                for (int countOffset = position + 32; countOffset <= position + 90; countOffset++)
                {
                    if (TryReadAdaptAdmPointBlock(bytes, countOffset, recordEndOffset, out planBlock))
                    {
                        break;
                    }
                }
            }

            if (planBlock == null || planBlock.Points.Count < 2)
            {
                return false;
            }

            List<AdaptAdmPointBlock> profileBlocks = ReadAdaptAdmProfilePointBlocks(
                bytes,
                position + 90,
                recordEndOffset,
                planBlock);

            if (profileBlocks.Count == 0)
            {
                return false;
            }

            records = new List<AdaptAdmTendonRecord>();
            int profileIndex = 1;
            foreach (AdaptAdmPointBlock profileBlock in profileBlocks)
            {
                var record = new AdaptAdmTendonRecord
                {
                    LayerToken = layerToken ?? "",
                    LayerName = layerName ?? "",
                    TendonName = BuildAdaptAdmProfileName(tendonName, recordNameOffset, profileIndex)
                };
                record.Points.AddRange(profileBlock.Points);
                records.Add(record);
                profileIndex++;
            }

            return true;
        }

        private static Dictionary<string, string> BuildAdaptAdmLayerNameMap(byte[] bytes)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bytes == null || bytes.Length == 0)
            {
                return map;
            }

            byte[] layerPrefix = Encoding.ASCII.GetBytes("ADPTLayer");
            foreach (int offset in FindAdaptAdmAsciiOccurrences(bytes, layerPrefix))
            {
                if (!TryReadAdaptAdmStringAtTextOffset(bytes, offset, out string layerToken, out int nextOffset) ||
                    !TryReadAdaptAdmString(bytes, nextOffset, out string displayName, out _))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layerToken) || string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                string normalized = NormalizeAdaptFileToken(displayName);
                if (!normalized.Contains("current") &&
                    !normalized.Contains("tendon") &&
                    !normalized.Contains("boundary") &&
                    !normalized.Contains("support") &&
                    !normalized.Contains("dimension") &&
                    !normalized.Contains("template"))
                {
                    continue;
                }

                if (!map.ContainsKey(layerToken))
                {
                    map[layerToken] = displayName.Trim();
                }
            }

            return map;
        }

        private static string BuildAdaptAdmProfileName(string tendonName, int recordNameOffset, int profileIndex)
        {
            string baseName = string.IsNullOrWhiteSpace(tendonName)
                ? "Tendon @" + recordNameOffset.ToString(CultureInfo.InvariantCulture)
                : tendonName.Trim();
            return baseName + " Profile " + profileIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static List<AdaptAdmPointBlock> ReadAdaptAdmProfilePointBlocks(
            byte[] bytes,
            int scanStartOffset,
            int recordEndOffset,
            AdaptAdmPointBlock planBlock)
        {
            var blocks = new List<AdaptAdmPointBlock>();
            if (bytes == null || planBlock == null)
            {
                return blocks;
            }

            int start = Math.Max(0, scanStartOffset);
            int end = Math.Min(bytes.Length, recordEndOffset);
            for (int offset = start; offset < end - 40;)
            {
                if (TryReadAdaptAdmPointBlock(bytes, offset, end, out AdaptAdmPointBlock block) &&
                    IsAdaptAdmProfilePointBlock(block, planBlock))
                {
                    blocks.Add(block);
                    offset = Math.Max(offset + 1, block.EndOffset);
                    continue;
                }

                offset++;
            }

            return blocks;
        }

        private static bool IsAdaptAdmProfilePointBlock(AdaptAdmPointBlock block, AdaptAdmPointBlock planBlock)
        {
            if (block == null || planBlock == null || block.Points.Count < 3)
            {
                return false;
            }

            double xRange = block.MaxX - block.MinX;
            double yRange = block.MaxY - block.MinY;
            double zRange = block.MaxZ - block.MinZ;
            double horizontalRange = Math.Max(xRange, zRange);
            double crossRange = Math.Min(xRange, zRange);

            if (yRange < 0.02 ||
                horizontalRange < 1.0 ||
                block.PathLength < 1.0 ||
                crossRange > Math.Max(2.0, horizontalRange * 0.35))
            {
                return false;
            }

            const double planTolerance = 2.0;
            if (!AdaptAdmRangesOverlap(block.MinX, block.MaxX, planBlock.MinX, planBlock.MaxX, planTolerance) ||
                !AdaptAdmRangesOverlap(block.MinZ, block.MaxZ, planBlock.MinZ, planBlock.MaxZ, planTolerance))
            {
                return false;
            }

            double planElevation = (planBlock.MinY + planBlock.MaxY) * 0.5;
            double profileElevation = (block.MinY + block.MaxY) * 0.5;
            return Math.Abs(profileElevation - planElevation) <= 2.5;
        }

        private static bool AdaptAdmRangesOverlap(double minA, double maxA, double minB, double maxB, double tolerance)
        {
            return minA <= maxB + tolerance && maxA >= minB - tolerance;
        }

        private static bool TryReadAdaptAdmPointBlock(byte[] bytes, int countOffset, int endOffset, out AdaptAdmPointBlock block)
        {
            block = null;
            if (bytes == null || countOffset < 0 || countOffset + 16 > endOffset)
            {
                return false;
            }

            int pointCount = BitConverter.ToInt32(bytes, countOffset);
            if (pointCount < 2 || pointCount > 100)
            {
                return false;
            }

            int dataOffset = countOffset + 12;
            if (dataOffset < 0 || dataOffset + (pointCount * 24) > endOffset)
            {
                return false;
            }

            double polylineLength = 0.0;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;
            var parsed = new List<AdaptAdmPoint>();
            AdaptAdmPoint previous = null;
            for (int i = 0; i < pointCount; i++)
            {
                int pointOffset = dataOffset + (i * 24);
                double x = BitConverter.ToDouble(bytes, pointOffset);
                double y = BitConverter.ToDouble(bytes, pointOffset + 8);
                double z = BitConverter.ToDouble(bytes, pointOffset + 16);
                if (!IsReasonableAdaptAdmCoordinate(x) ||
                    !IsReasonableAdaptAdmCoordinate(y) ||
                    !IsReasonableAdaptAdmCoordinate(z))
                {
                    return false;
                }

                var point = new AdaptAdmPoint
                {
                    X = x,
                    Y = y,
                    Z = z
                };

                if (previous != null)
                {
                    double dx = point.X - previous.X;
                    double dy = point.Y - previous.Y;
                    double dz = point.Z - previous.Z;
                    polylineLength += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                }

                parsed.Add(point);
                previous = point;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }

            if (polylineLength < 0.01)
            {
                return false;
            }

            block = new AdaptAdmPointBlock
            {
                CountOffset = countOffset,
                EndOffset = dataOffset + (pointCount * 24),
                PathLength = polylineLength,
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                MinZ = minZ,
                MaxZ = maxZ
            };
            block.Points.AddRange(parsed);
            return true;
        }

        private static bool IsReasonableAdaptAdmCoordinate(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   Math.Abs(value) <= 100000.0;
        }

        private static bool IsAdaptAdmTendonLayer(string layerToken, string layerName)
        {
            string token = NormalizeAdaptFileToken(layerToken);
            string name = NormalizeAdaptFileToken(layerName);
            if (name.Contains("ctrl") || name.Contains("supportbar") || name.Contains("txt") || name.Contains("template"))
            {
                return false;
            }

            if (name.Contains("tendon"))
            {
                return true;
            }

            return token.StartsWith("adaptlayer", StringComparison.OrdinalIgnoreCase);
        }

        private static void ConvertAdaptAdmPointToRevitFeet(
            AdaptAdmPoint point,
            AdaptTendonLengthUnit unit,
            out double xFt,
            out double yFt,
            out double zFt)
        {
            // ADAPT-Builder tendon records store plan coordinates on X/Z and the up/elevation axis on Y.
            xFt = ConvertAdaptLengthToFeet(point.X, unit);
            yFt = ConvertAdaptLengthToFeet(point.Z, unit);
            zFt = ConvertAdaptLengthToFeet(point.Y, unit);
        }

        private static IEnumerable<int> FindAdaptAdmAsciiOccurrences(byte[] bytes, byte[] needle)
        {
            if (bytes == null || needle == null || bytes.Length == 0 || needle.Length == 0 || needle.Length > bytes.Length)
            {
                yield break;
            }

            for (int i = 0; i <= bytes.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (bytes[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    yield return i;
                }
            }
        }

        private static bool TryFindAdaptAdmAscii(byte[] bytes, byte[] needle, int startOffset, int endOffset, out int offset)
        {
            offset = -1;
            if (bytes == null || needle == null || needle.Length == 0)
            {
                return false;
            }

            int start = Math.Max(0, startOffset);
            int end = Math.Min(bytes.Length - needle.Length, Math.Max(start, endOffset - needle.Length));
            for (int i = start; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (bytes[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    offset = i;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadAdaptAdmStringAtTextOffset(byte[] bytes, int textOffset, out string text, out int nextOffset)
        {
            text = "";
            nextOffset = textOffset;
            if (bytes == null || textOffset < 4)
            {
                return false;
            }

            return TryReadAdaptAdmString(bytes, textOffset - 4, out text, out nextOffset);
        }

        private static bool TryReadAdaptAdmString(byte[] bytes, int lengthOffset, out string text, out int nextOffset)
        {
            text = "";
            nextOffset = lengthOffset;
            if (bytes == null || lengthOffset < 0 || lengthOffset + 4 > bytes.Length)
            {
                return false;
            }

            int length = BitConverter.ToInt32(bytes, lengthOffset);
            if (length < 0 || length > 512 || lengthOffset + 4 + length > bytes.Length)
            {
                return false;
            }

            string value = Encoding.ASCII.GetString(bytes, lengthOffset + 4, length);
            if (value.Any(ch => ch < 32 || ch > 126))
            {
                return false;
            }

            text = value;
            nextOffset = lengthOffset + 4 + length;
            return true;
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
