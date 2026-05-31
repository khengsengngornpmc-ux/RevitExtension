using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal enum AdaptTendonLengthUnit
    {
        Millimeter,
        Meter,
        Foot,
        Inch,
        Centimeter
    }

    internal sealed class AdaptTendonImportReadResult
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
}
