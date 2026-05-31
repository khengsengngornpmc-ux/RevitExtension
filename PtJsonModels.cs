using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtJsonSchema
    {
        public const string Version1 = "mhnk.pt.import/1.0";
    }

    internal class PtImportJsonDocument
    {
        public string Schema { get; set; } = PtJsonSchema.Version1;
        public string ImportMethod { get; set; } = "";
        public PtImportJsonSource Source { get; set; } = new PtImportJsonSource();
        public PtImportJsonUnits Units { get; set; } = new PtImportJsonUnits();
        public List<PtImportJsonTendon> Tendons { get; set; } = new List<PtImportJsonTendon>();
        public List<PtImportJsonCadReference> CadReferences { get; set; } = new List<PtImportJsonCadReference>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    internal class PtImportJsonSource
    {
        public string SourceType { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Generator { get; set; } = "";
        public string ProjectName { get; set; } = "";
    }

    internal class PtImportJsonUnits
    {
        public string Geometry { get; set; } = "ft";
        public string DisplayLength { get; set; } = "mm";
        public string Station { get; set; } = "ft";
        public string Elevation { get; set; } = "ft";
    }

    internal class PtImportJsonCadReference
    {
        public string FilePath { get; set; } = "";
        public string Format { get; set; } = "";
        public string ImportMode { get; set; } = "";
        public List<string> CandidateLayers { get; set; } = new List<string>();
    }

    internal class PtImportJsonTendon
    {
        public string TendonId { get; set; } = "";
        public string ShopMark { get; set; } = "";
        public string GroupKey { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public string TendonName { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public string SourceLayer { get; set; } = "";
        public string TendonType { get; set; } = "";
        public int? StrandCount { get; set; }
        public double? DuctDiameterMm { get; set; }
        public double? RenderedDiameterMm { get; set; }
        public List<PtImportJsonPoint> CenterlinePoints { get; set; } = new List<PtImportJsonPoint>();
        public PtImportJsonProfileHints ProfileHints { get; set; } = new PtImportJsonProfileHints();
        public List<string> Tags { get; set; } = new List<string>();
    }

    internal class PtImportJsonPoint
    {
        public double XFt { get; set; }
        public double YFt { get; set; }
        public double ZFt { get; set; }
        public double? StationFt { get; set; }
        public double? ElevationFt { get; set; }
    }

    internal class PtImportJsonProfileHints
    {
        public double? LowPointStationFt { get; set; }
        public double? LowPointElevationFt { get; set; }
        public double? HighPointStationFt { get; set; }
        public double? HighPointElevationFt { get; set; }
        public double? TotalLengthFt { get; set; }
    }
}
