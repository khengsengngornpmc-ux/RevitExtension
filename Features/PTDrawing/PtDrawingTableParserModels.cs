using System;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class AdaptTendonPointRow
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

    internal sealed class AdaptTendonHeaderMap
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

        public bool HasModelSegments
        {
            get { return StartX >= 0 && StartY >= 0 && StartZ >= 0 && EndX >= 0 && EndY >= 0 && EndZ >= 0; }
        }

        public bool HasProfileSegments
        {
            get { return StartStation >= 0 && StartElevation >= 0 && EndStation >= 0 && EndElevation >= 0; }
        }

        public bool HasModelPoints
        {
            get { return X >= 0 && Y >= 0 && Z >= 0; }
        }

        public bool HasProfilePoints
        {
            get { return Station >= 0 && Elevation >= 0; }
        }

        public AdaptTendonImportMode Mode
        {
            get
            {
                return HasModelSegments || HasModelPoints
                    ? AdaptTendonImportMode.Model3D
                    : AdaptTendonImportMode.ProfileDetail;
            }
        }

        public bool HasAnyGeometry
        {
            get { return HasModelSegments || HasProfileSegments || HasModelPoints || HasProfilePoints; }
        }
    }
}
