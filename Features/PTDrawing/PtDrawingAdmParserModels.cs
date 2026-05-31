using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class AdaptAdmPoint
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }

    internal sealed class AdaptAdmTendonRecord
    {
        public string LayerToken { get; set; } = "";

        public string LayerName { get; set; } = "";

        public string TendonName { get; set; } = "";

        public List<AdaptAdmPoint> Points { get; } = new List<AdaptAdmPoint>();
    }

    internal sealed class AdaptAdmPointBlock
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
}
