using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class PtDrawingAuditPreviewPackage
    {
        public IList<MhnkCadPreviewItem> Items { get; set; } = new List<MhnkCadPreviewItem>();

        public string Summary { get; set; } = "";

        public int TendonCount { get; set; }

        public int ChangeCount { get; set; }
    }
}
