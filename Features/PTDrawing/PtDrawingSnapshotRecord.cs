using System;
using System.IO;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class PtDrawingSnapshotRecord
    {
        public string JsonPath { get; set; } = "";

        public DateTime SnapshotTimeLocal { get; set; }

        public PtImportJsonDocument Document { get; set; } = new PtImportJsonDocument();

        public string SourcePath
        {
            get { return (Document?.Source?.SourcePath ?? "").Trim(); }
        }

        public string DisplayName
        {
            get
            {
                string displayName = (Document?.Source?.DisplayName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }

                if (!string.IsNullOrWhiteSpace(SourcePath))
                {
                    return Path.GetFileName(SourcePath);
                }

                return Path.GetFileName(JsonPath ?? "");
            }
        }

        public bool IsCadWorkflow
        {
            get
            {
                return string.Equals(Document?.ImportMethod, "cad-dwg-dxf", StringComparison.OrdinalIgnoreCase) ||
                       IsCadDrawingPath(SourcePath);
            }
        }

        private static bool IsCadDrawingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".dxf", StringComparison.OrdinalIgnoreCase);
        }
    }
}
