using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtJsonMapper
    {
        public static PtImportJsonDocument CreateFromAdaptSegments(
            string sourcePath,
            AdaptTendonImportMode importMode,
            IEnumerable<AdaptTendonProfileSegmentPayload> segments)
        {
            List<AdaptTendonProfileSegmentPayload> sourceSegments = (segments ?? Enumerable.Empty<AdaptTendonProfileSegmentPayload>())
                .Where(x => x != null)
                .ToList();

            var document = new PtImportJsonDocument
            {
                ImportMethod = IsCadDrawingPath(sourcePath) ? "cad-dwg-dxf" : "direct-adapt",
                Source = new PtImportJsonSource
                {
                    SourceType = GetSourceType(sourcePath),
                    SourcePath = sourcePath ?? "",
                    DisplayName = string.IsNullOrWhiteSpace(sourcePath) ? "" : Path.GetFileName(sourcePath),
                    Generator = "DRAWING PT",
                    ProjectName = string.IsNullOrWhiteSpace(sourcePath) ? "" : Path.GetFileNameWithoutExtension(sourcePath)
                }
            };

            foreach (IGrouping<string, AdaptTendonProfileSegmentPayload> group in sourceSegments.GroupBy(GetGroupKey, StringComparer.OrdinalIgnoreCase))
            {
                List<PtImportJsonPoint> orderedPoints = BuildOrderedPoints(group).ToList();
                if (orderedPoints.Count < 2)
                {
                    continue;
                }

                PtImportJsonTendon tendon = BuildTendonDocument(group.Key, group.ToList(), orderedPoints);
                document.Tendons.Add(tendon);
            }

            if (IsCadDrawingPath(sourcePath))
            {
                document.CadReferences.Add(new PtImportJsonCadReference
                {
                    FilePath = sourcePath ?? "",
                    Format = Path.GetExtension(sourcePath ?? "").Trim('.').ToLowerInvariant(),
                    ImportMode = "link-or-import"
                });
            }

            document.Notes.Add(
                importMode == AdaptTendonImportMode.Model3D
                    ? "Created from 3D tendon source segments."
                    : "Created from profile-detail tendon source segments.");
            return document;
        }

        private static PtImportJsonTendon BuildTendonDocument(
            string groupKey,
            IList<AdaptTendonProfileSegmentPayload> segments,
            IList<PtImportJsonPoint> orderedPoints)
        {
            AdaptTendonProfileSegmentPayload first = segments.FirstOrDefault();
            ApplyStationsAndProfileHints(orderedPoints, out PtImportJsonProfileHints hints);

            return new PtImportJsonTendon
            {
                TendonId = BuildTendonId(first, groupKey),
                GroupKey = groupKey ?? "",
                ProfileName = first?.ProfileName ?? "",
                TendonName = first?.TendonName ?? "",
                SourceLabel = first?.SourceLabel ?? "",
                SourceLayer = first?.ProfileName ?? "",
                RenderedDiameterMm = TryInferRenderedDiameterMm(segments),
                CenterlinePoints = orderedPoints.ToList(),
                ProfileHints = hints,
                Tags = new List<string> { "adapt-segment-import" }
            };
        }

        private static string BuildTendonId(AdaptTendonProfileSegmentPayload segment, string groupKey)
        {
            if (!string.IsNullOrWhiteSpace(segment?.TendonName))
            {
                return segment.TendonName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(segment?.ProfileName))
            {
                return segment.ProfileName.Trim();
            }

            return string.IsNullOrWhiteSpace(groupKey) ? "ADAPT-TENDON" : groupKey.Trim();
        }

        private static IEnumerable<PtImportJsonPoint> BuildOrderedPoints(IEnumerable<AdaptTendonProfileSegmentPayload> segments)
        {
            var ordered = new List<PtImportJsonPoint>();
            foreach (AdaptTendonProfileSegmentPayload segment in segments ?? Enumerable.Empty<AdaptTendonProfileSegmentPayload>())
            {
                PtImportJsonPoint a = new PtImportJsonPoint
                {
                    XFt = segment.X0Ft,
                    YFt = segment.Y0Ft,
                    ZFt = segment.Z0Ft
                };
                PtImportJsonPoint b = new PtImportJsonPoint
                {
                    XFt = segment.X1Ft,
                    YFt = segment.Y1Ft,
                    ZFt = segment.Z1Ft
                };

                if (ordered.Count == 0)
                {
                    ordered.Add(a);
                    ordered.Add(b);
                    continue;
                }

                PtImportJsonPoint last = ordered[ordered.Count - 1];
                if (AreNear(last, a))
                {
                    ordered.Add(b);
                }
                else if (AreNear(last, b))
                {
                    ordered.Add(a);
                }
                else
                {
                    ordered.Add(a);
                    ordered.Add(b);
                }
            }

            return CompactPoints(ordered);
        }

        private static void ApplyStationsAndProfileHints(
            IList<PtImportJsonPoint> points,
            out PtImportJsonProfileHints hints)
        {
            hints = new PtImportJsonProfileHints();
            if (points == null || points.Count == 0)
            {
                return;
            }

            double station = 0.0;
            points[0].StationFt = 0.0;
            points[0].ElevationFt = points[0].ZFt;

            int highIndex = 0;
            int lowIndex = 0;
            for (int i = 1; i < points.Count; i++)
            {
                double dx = points[i].XFt - points[i - 1].XFt;
                double dy = points[i].YFt - points[i - 1].YFt;
                station += Math.Sqrt((dx * dx) + (dy * dy));
                points[i].StationFt = station;
                points[i].ElevationFt = points[i].ZFt;

                if (points[i].ZFt > points[highIndex].ZFt)
                {
                    highIndex = i;
                }

                if (points[i].ZFt < points[lowIndex].ZFt)
                {
                    lowIndex = i;
                }
            }

            hints.TotalLengthFt = station;
            hints.HighPointStationFt = points[highIndex].StationFt;
            hints.HighPointElevationFt = points[highIndex].ElevationFt;
            hints.LowPointStationFt = points[lowIndex].StationFt;
            hints.LowPointElevationFt = points[lowIndex].ElevationFt;
        }

        private static IEnumerable<PtImportJsonPoint> CompactPoints(IEnumerable<PtImportJsonPoint> points)
        {
            var compact = new List<PtImportJsonPoint>();
            foreach (PtImportJsonPoint point in points ?? Enumerable.Empty<PtImportJsonPoint>())
            {
                if (point == null)
                {
                    continue;
                }

                if (compact.Count == 0 || !AreNear(compact[compact.Count - 1], point))
                {
                    compact.Add(point);
                }
            }

            return compact;
        }

        private static bool AreNear(PtImportJsonPoint a, PtImportJsonPoint b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            double dx = a.XFt - b.XFt;
            double dy = a.YFt - b.YFt;
            double dz = a.ZFt - b.ZFt;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) < 1.0e-6;
        }

        private static string GetGroupKey(AdaptTendonProfileSegmentPayload segment)
        {
            if (segment == null)
            {
                return "ADAPT Tendon";
            }

            if (!string.IsNullOrWhiteSpace(segment.SourceLabel))
            {
                return segment.SourceLabel.Trim();
            }

            if (!string.IsNullOrWhiteSpace(segment.ProfileName) && !string.IsNullOrWhiteSpace(segment.TendonName))
            {
                return segment.ProfileName.Trim() + " / " + segment.TendonName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(segment.TendonName))
            {
                return segment.TendonName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(segment.ProfileName))
            {
                return segment.ProfileName.Trim();
            }

            return "ADAPT Tendon";
        }

        private static double? TryInferRenderedDiameterMm(IEnumerable<AdaptTendonProfileSegmentPayload> segments)
        {
            foreach (AdaptTendonProfileSegmentPayload segment in segments ?? Enumerable.Empty<AdaptTendonProfileSegmentPayload>())
            {
                string text = string.Join(" | ", new[] { segment?.ProfileName ?? "", segment?.TendonName ?? "", segment?.SourceLabel ?? "" });
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                System.Text.RegularExpressions.Match mm = System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"(?<!\d)(\d+(?:\.\d+)?)\s*mm\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (mm.Success &&
                    double.TryParse(mm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }
            }

            return 50.0;
        }

        private static string GetSourceType(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".adm":
                    return "adapt-adm";
                case ".dwg":
                    return "adapt-dwg";
                case ".dxf":
                    return "adapt-dxf";
                case ".csv":
                case ".txt":
                case ".tsv":
                case ".xlsx":
                case ".xlsm":
                case ".xls":
                    return "adapt-table";
                default:
                    return "converted-json";
            }
        }

        private static bool IsCadDrawingPath(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath ?? "").ToLowerInvariant();
            return ext == ".dwg" || ext == ".dxf";
        }
    }
}
