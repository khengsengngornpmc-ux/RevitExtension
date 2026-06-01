using System;
using System.IO;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingImportWorkflowService
    {
        private static readonly string[] AllSupportedSourceExtensions =
        {
            ".adm",
            ".dwg",
            ".dxf",
            ".xlsx",
            ".xlsm",
            ".xls",
            ".csv",
            ".tsv",
            ".txt"
        };

        private static readonly string[] DirectProfileSourceExtensions =
        {
            ".adm",
            ".xlsx",
            ".xlsm",
            ".xls",
            ".csv",
            ".tsv",
            ".txt"
        };

        private static readonly string[] CadSourceExtensions =
        {
            ".dwg",
            ".dxf"
        };

        public static OperationResult<PtDrawingValidatedSource> ValidateAnySource(string path)
        {
            return ValidateSource(path, AllSupportedSourceExtensions);
        }

        public static OperationResult<PtDrawingValidatedSource> ValidateDirectProfileSource(string path)
        {
            return ValidateSource(path, DirectProfileSourceExtensions);
        }

        public static OperationResult<PtDrawingValidatedSource> ValidateCadSource(string path)
        {
            OperationResult<PtDrawingValidatedSource> result = ValidateSource(path, CadSourceExtensions);
            if (!result.Succeeded)
            {
                return result;
            }

            if (result.Value.SourceKind != PtDrawingImportSourceKind.CadDrawing)
            {
                return OperationResult<PtDrawingValidatedSource>.Failure(
                    "Expected an ADAPT DWG/DXF CAD drawing.",
                    "PT_CAD_SOURCE_EXPECTED");
            }

            return result;
        }

        public static PtDrawingCadFallbackResult FindCadFallbackForProject(string projectPath)
        {
            var fallback = new PtDrawingCadFallbackResult();
            OperationResult<PtDrawingValidatedSource> validation = ValidateSource(projectPath, ".adm");
            if (!validation.Succeeded)
            {
                FeatureTraceWriter.WriteStage("PT_DRAWING", "CadFallbackValidation", validation.ToString());
                return fallback;
            }

            if (!PtDrawingSourceService.TryFindLatestCadExport(validation.Value.SourcePath, out string exportPath))
            {
                return fallback;
            }

            try
            {
                var exportInfo = new FileInfo(exportPath);
                fallback.Found = exportInfo.Exists;
                fallback.ExportPath = exportInfo.FullName;
                fallback.DisplayName = exportInfo.Name;
                fallback.LastWriteTime = exportInfo.Exists ? exportInfo.LastWriteTime : (DateTime?)null;
                fallback.FreshnessNote = PtDrawingSourceService.BuildExportFreshnessNote(validation.Value.SourcePath, exportInfo);
            }
            catch
            {
                fallback.Found = !string.IsNullOrWhiteSpace(exportPath);
                fallback.ExportPath = exportPath ?? "";
                fallback.DisplayName = string.IsNullOrWhiteSpace(exportPath) ? "" : Path.GetFileName(exportPath);
            }

            return fallback;
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

            OperationResult<PtDrawingValidatedSource> validation = ValidateDirectProfileSource(sourcePath);
            if (!validation.Succeeded)
            {
                FeatureTraceWriter.WriteStage("PT_DRAWING", "AuditSourceValidation", validation.ToString());
                return null;
            }

            try
            {
                AdaptTendonImportReadResult result = readSource(validation.Value.SourcePath);
                if (result == null || result.Segments.Count == 0)
                {
                    return null;
                }

                PtImportJsonDocument document = PtJsonMapper.CreateFromAdaptSegments(
                    validation.Value.SourcePath,
                    result.Mode,
                    result.Segments);
                if (document == null)
                {
                    return null;
                }

                document.Notes.Add("Generated directly from the source file for audit preview.");
                return document;
            }
            catch (Exception ex)
            {
                FeatureTraceWriter.WriteStage("PT_DRAWING", "AuditSourceReadFailed", ex.Message);
                return null;
            }
        }

        private static OperationResult<PtDrawingValidatedSource> ValidateSource(string path, params string[] allowedExtensions)
        {
            OperationResult<string> validation = ExternalInputValidator.ValidateReadableFile(
                path,
                "PT_DRAWING",
                allowedExtensions);
            if (!validation.Succeeded)
            {
                return OperationResult<PtDrawingValidatedSource>.Failure(
                    validation.Message,
                    validation.ErrorCode,
                    validation.Exception);
            }

            string fullPath = validation.Value;
            string ext = (Path.GetExtension(fullPath) ?? "").ToLowerInvariant();
            return OperationResult<PtDrawingValidatedSource>.Success(
                new PtDrawingValidatedSource
                {
                    SourcePath = fullPath,
                    DisplayName = Path.GetFileName(fullPath),
                    SourceKind = GetSourceKind(ext),
                    Extension = ext
                },
                "Validated PT source: " + fullPath);
        }

        private static PtDrawingImportSourceKind GetSourceKind(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".adm":
                    return PtDrawingImportSourceKind.AdaptProject;
                case ".dwg":
                case ".dxf":
                    return PtDrawingImportSourceKind.CadDrawing;
                case ".xlsx":
                case ".xlsm":
                case ".xls":
                case ".csv":
                case ".tsv":
                case ".txt":
                    return PtDrawingImportSourceKind.ProfileTable;
                default:
                    return PtDrawingImportSourceKind.Unknown;
            }
        }
    }
}
