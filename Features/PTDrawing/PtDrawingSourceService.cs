using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingSourceService
    {
        public static bool IsProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string ext = Path.GetExtension(path) ?? "";
            return string.Equals(ext, ".adm", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCadDrawingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string ext = Path.GetExtension(path) ?? "";
            return string.Equals(ext, ".dwg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".dxf", StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildExportFreshnessNote(string projectPath, FileInfo exportInfo)
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

        public static bool TryFindLatestCadExport(string projectPath, out string exportPath)
        {
            exportPath = "";
            OperationResult<string> validation = ExternalInputValidator.ValidateReadableFile(
                projectPath,
                "PT_DRAWING",
                ".adm");
            if (!validation.Succeeded)
            {
                FeatureTraceWriter.WriteStage("PT_DRAWING", "InputValidation", validation.ToString());
                return false;
            }

            string projectDirectory;
            try
            {
                projectPath = validation.Value;
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
            List<string> folders = BuildExportSearchFolders(projectDirectory);
            List<string> files = new List<string>();
            foreach (string folder in folders)
            {
                AddCadFiles(folder, files);
            }

            if (files.Count == 0)
            {
                return false;
            }

            string projectToken = NormalizeFileToken(projectName);
            var best = files
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .Select(info => new
                {
                    Info = info,
                    ProjectNameMatch = !string.IsNullOrWhiteSpace(projectToken) &&
                        NormalizeFileToken(Path.GetFileNameWithoutExtension(info.Name)).Contains(projectToken)
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

        public static string NormalizeFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string sanitized = ExternalInputValidator.SanitizeImportedLabel(value, 128);
            return Regex.Replace(sanitized.ToLowerInvariant(), @"[^a-z0-9]+", "");
        }

        public static AdaptTendonImportReadResult ReadTendonProfileFile(
            string path,
            Func<string, AdaptTendonImportReadResult> readAdmGeometry,
            Func<string, AdaptTendonImportReadResult> readExcel,
            Func<string, AdaptTendonImportReadResult> readText)
        {
            OperationResult<string> validation = ExternalInputValidator.ValidateReadableFile(
                path,
                "PT_DRAWING",
                ".adm",
                ".dwg",
                ".dxf",
                ".xlsx",
                ".xlsm",
                ".xls",
                ".csv",
                ".tsv",
                ".txt");
            if (!validation.Succeeded)
            {
                FeatureTraceWriter.WriteStage("PT_DRAWING", "InputValidation", validation.ToString());
                return new AdaptTendonImportReadResult();
            }

            path = validation.Value;
            string ext = Path.GetExtension(path) ?? "";
            if (string.Equals(ext, ".adm", StringComparison.OrdinalIgnoreCase))
            {
                return readAdmGeometry == null ? new AdaptTendonImportReadResult() : readAdmGeometry(path);
            }

            if (IsCadDrawingPath(path))
            {
                throw new InvalidOperationException(
                    "ADAPT DWG/DXF drawings are handled by the CAD link path. Select the drawing with Import ADAPT so it can be linked into Revit and selected as the CAD2MODEL source.");
            }

            if (string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                return readExcel == null ? new AdaptTendonImportReadResult() : readExcel(path);
            }

            return readText == null ? new AdaptTendonImportReadResult() : readText(path);
        }

        public static PtImportJsonDocument TryBuildAuditDocumentFromSource(
            string sourcePath,
            bool isCadWorkflow,
            Func<string, AdaptTendonImportReadResult> readSource)
        {
            if (isCadWorkflow || readSource == null)
            {
                return null;
            }

            try
            {
                AdaptTendonImportReadResult result = readSource(sourcePath);
                if (result == null || result.Segments.Count == 0)
                {
                    return null;
                }

                PtImportJsonDocument document = PtJsonMapper.CreateFromAdaptSegments(sourcePath, result.Mode, result.Segments);
                if (document == null)
                {
                    return null;
                }

                document.Notes.Add("Generated directly from the source file for audit preview.");
                return document;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> BuildExportSearchFolders(string projectDirectory)
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

        private static void AddCadFiles(string folder, List<string> files)
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
    }
}
