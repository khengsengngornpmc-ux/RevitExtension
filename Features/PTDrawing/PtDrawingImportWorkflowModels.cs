using System;

namespace CamboBIM.Revit2024.Addin
{
    internal enum PtDrawingImportSourceKind
    {
        Unknown,
        AdaptProject,
        CadDrawing,
        ProfileTable
    }

    internal sealed class PtDrawingValidatedSource
    {
        public string SourcePath { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public PtDrawingImportSourceKind SourceKind { get; set; } = PtDrawingImportSourceKind.Unknown;

        public string Extension { get; set; } = "";

        public bool IsCadWorkflow
        {
            get { return SourceKind == PtDrawingImportSourceKind.CadDrawing; }
        }
    }

    internal sealed class PtDrawingCadFallbackResult
    {
        public bool Found { get; set; }

        public string ExportPath { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public DateTime? LastWriteTime { get; set; }

        public string FreshnessNote { get; set; } = "";
    }
}
