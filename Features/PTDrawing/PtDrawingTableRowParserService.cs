using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CamboBIM.Revit2024.Addin
{
    internal delegate bool PtDrawingTryParseDoubleDelegate(string raw, out double value);

    internal static class PtDrawingTableRowParserService
    {
        public static AdaptTendonImportReadResult ReadRows(
            IReadOnlyList<string[]> rows,
            Func<string, string> normalizeImportedHeaderCell,
            PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            var result = new AdaptTendonImportReadResult();
            if (rows == null || rows.Count == 0)
            {
                return result;
            }

            int headerRow = FindHeaderRow(rows, normalizeImportedHeaderCell, out AdaptTendonHeaderMap map);
            if (headerRow < 0 || map == null || !map.HasAnyGeometry)
            {
                return result;
            }

            string[] headers = rows[headerRow] ?? Array.Empty<string>();
            result.Mode = map.Mode;
            result.InferredUnit = InferDefaultUnit(rows, headerRow + 1, map, tryParseDouble);

            if (map.HasModelSegments || map.HasProfileSegments)
            {
                AppendSegments(rows, headerRow + 1, headers, map, result, tryParseDouble);
            }
            else
            {
                AppendPointSegments(rows, headerRow + 1, headers, map, result, tryParseDouble);
            }

            result.ProfileCount = result.Segments
                .Select(BuildSegmentGroupKey)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return result;
        }

        public static double ConvertLengthToFeet(double value, AdaptTendonLengthUnit unit)
        {
            switch (unit)
            {
                case AdaptTendonLengthUnit.Foot: return value;
                case AdaptTendonLengthUnit.Inch: return value / 12.0;
                case AdaptTendonLengthUnit.Meter: return value / 0.3048;
                case AdaptTendonLengthUnit.Centimeter: return value / 30.48;
                case AdaptTendonLengthUnit.Millimeter:
                default: return value / 304.8;
            }
        }

        public static string DescribeUnit(AdaptTendonLengthUnit unit)
        {
            switch (unit)
            {
                case AdaptTendonLengthUnit.Foot: return "ft";
                case AdaptTendonLengthUnit.Inch: return "in";
                case AdaptTendonLengthUnit.Meter: return "m";
                case AdaptTendonLengthUnit.Centimeter: return "cm";
                case AdaptTendonLengthUnit.Millimeter:
                default: return "mm";
            }
        }

        public static string BuildGroupKey(string profile, string tendon)
        {
            string p = (profile ?? "").Trim();
            string t = (tendon ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(p) && !string.IsNullOrWhiteSpace(t) &&
                !string.Equals(p, t, StringComparison.OrdinalIgnoreCase))
            {
                return p + " / " + t;
            }

            if (!string.IsNullOrWhiteSpace(t)) return t;
            if (!string.IsNullOrWhiteSpace(p)) return p;
            return "";
        }

        public static string BuildSegmentGroupKey(AdaptTendonProfileSegmentPayload segment)
        {
            if (segment == null)
            {
                return "";
            }

            return BuildGroupKey(segment.ProfileName, segment.TendonName);
        }

        public static double GetDistanceFt(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            double dz = z1 - z0;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        public static List<string> SplitDelimitedLine(string line, Func<string, List<string>> parseCsvLine)
        {
            string text = line ?? "";
            int tabCount = text.Count(c => c == '\t');
            int commaCount = text.Count(c => c == ',');
            int semicolonCount = text.Count(c => c == ';');

            if (tabCount > 0 && tabCount >= commaCount && tabCount >= semicolonCount)
            {
                return text.Split('\t').Select(c => (c ?? "").Trim()).ToList();
            }

            if (semicolonCount > commaCount)
            {
                return text.Split(';').Select(c => (c ?? "").Trim()).ToList();
            }

            List<string> parsed = parseCsvLine == null ? new List<string> { text } : parseCsvLine(text);
            return parsed.Select(c => (c ?? "").Trim()).ToList();
        }

        private static void AppendSegments(IReadOnlyList<string[]> rows, int firstDataRow, IReadOnlyList<string> headers, AdaptTendonHeaderMap map, AdaptTendonImportReadResult result, PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            for (int i = firstDataRow; i < rows.Count; i++)
            {
                string[] cells = rows[i] ?? Array.Empty<string>();
                if (cells.Length == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                string profile = GetCell(cells, map.Profile);
                string tendon = GetCell(cells, map.Tendon);
                string sourceLabel = BuildSourceLabel(profile, tendon, i + 1);

                bool ok;
                AdaptTendonProfileSegmentPayload segment;
                if (map.HasModelSegments)
                {
                    ok = TryReadSegment(cells, headers, result.InferredUnit, map.StartX, map.StartY, map.StartZ, map.EndX, map.EndY, map.EndZ, out segment, tryParseDouble);
                }
                else
                {
                    ok = TryReadSegment(cells, headers, result.InferredUnit, map.StartStation, -1, map.StartElevation, map.EndStation, -1, map.EndElevation, out segment, tryParseDouble);
                }

                if (!ok)
                {
                    result.SkippedRows++;
                    continue;
                }

                segment.ProfileName = profile;
                segment.TendonName = tendon;
                segment.SourceLabel = sourceLabel;
                result.Segments.Add(segment);
                result.PointCount += 2;
            }
        }

        private static void AppendPointSegments(IReadOnlyList<string[]> rows, int firstDataRow, IReadOnlyList<string> headers, AdaptTendonHeaderMap map, AdaptTendonImportReadResult result, PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            var pointsByGroup = new Dictionary<string, List<AdaptTendonPointRow>>(StringComparer.OrdinalIgnoreCase);
            int rowOrder = 0;

            for (int i = firstDataRow; i < rows.Count; i++)
            {
                string[] cells = rows[i] ?? Array.Empty<string>();
                if (cells.Length == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                string profile = GetCell(cells, map.Profile);
                string tendon = GetCell(cells, map.Tendon);
                string groupKey = BuildGroupKey(profile, tendon);
                if (string.IsNullOrWhiteSpace(groupKey))
                {
                    groupKey = "ADAPT Profile";
                }

                if (!TryReadPoint(cells, headers, result.InferredUnit, map, rowOrder, profile, tendon, groupKey, out AdaptTendonPointRow point, tryParseDouble))
                {
                    result.SkippedRows++;
                    continue;
                }

                if (!pointsByGroup.TryGetValue(groupKey, out List<AdaptTendonPointRow> list))
                {
                    list = new List<AdaptTendonPointRow>();
                    pointsByGroup[groupKey] = list;
                }

                list.Add(point);
                result.PointCount++;
                rowOrder++;
            }

            foreach (KeyValuePair<string, List<AdaptTendonPointRow>> pair in pointsByGroup)
            {
                List<AdaptTendonPointRow> ordered = pair.Value.OrderBy(p => p.PointIndex).ThenBy(p => p.RowOrder).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    AdaptTendonPointRow a = ordered[i - 1];
                    AdaptTendonPointRow b = ordered[i];
                    if (GetDistanceFt(a.XFt, a.YFt, a.ZFt, b.XFt, b.YFt, b.ZFt) < 1.0e-6)
                    {
                        continue;
                    }

                    result.Segments.Add(new AdaptTendonProfileSegmentPayload
                    {
                        ProfileName = !string.IsNullOrWhiteSpace(a.ProfileName) ? a.ProfileName : b.ProfileName,
                        TendonName = !string.IsNullOrWhiteSpace(a.TendonName) ? a.TendonName : b.TendonName,
                        SourceLabel = pair.Key,
                        X0Ft = a.XFt,
                        Y0Ft = a.YFt,
                        Z0Ft = a.ZFt,
                        X1Ft = b.XFt,
                        Y1Ft = b.YFt,
                        Z1Ft = b.ZFt
                    });
                }
            }
        }

        private static bool TryReadPoint(string[] cells, IReadOnlyList<string> headers, AdaptTendonLengthUnit defaultUnit, AdaptTendonHeaderMap map, int rowOrder, string profile, string tendon, string groupKey, out AdaptTendonPointRow point, PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            point = null;
            int pointIndex = int.MaxValue;
            if (map.Point >= 0)
            {
                string pointRaw = GetCell(cells, map.Point);
                if (int.TryParse(pointRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
                {
                    pointIndex = parsedIndex;
                }
            }

            double xFt;
            double yFt;
            double zFt;
            if (map.HasModelPoints)
            {
                if (!TryReadLengthCell(cells, headers, map.X, defaultUnit, out xFt, tryParseDouble) ||
                    !TryReadLengthCell(cells, headers, map.Y, defaultUnit, out yFt, tryParseDouble) ||
                    !TryReadLengthCell(cells, headers, map.Z, defaultUnit, out zFt, tryParseDouble))
                {
                    return false;
                }
            }
            else if (map.HasProfilePoints)
            {
                if (!TryReadLengthCell(cells, headers, map.Station, defaultUnit, out xFt, tryParseDouble) ||
                    !TryReadLengthCell(cells, headers, map.Elevation, defaultUnit, out zFt, tryParseDouble))
                {
                    return false;
                }

                yFt = 0.0;
            }
            else
            {
                return false;
            }

            point = new AdaptTendonPointRow
            {
                ProfileName = profile ?? "",
                TendonName = tendon ?? "",
                GroupKey = groupKey ?? "",
                PointIndex = pointIndex,
                RowOrder = rowOrder,
                XFt = xFt,
                YFt = yFt,
                ZFt = zFt
            };
            return true;
        }

        private static bool TryReadSegment(string[] cells, IReadOnlyList<string> headers, AdaptTendonLengthUnit defaultUnit, int x0, int y0, int z0, int x1, int y1, int z1, out AdaptTendonProfileSegmentPayload segment, PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            segment = null;

            if (!TryReadLengthCell(cells, headers, x0, defaultUnit, out double sx, tryParseDouble) ||
                !TryReadLengthCell(cells, headers, z0, defaultUnit, out double sz, tryParseDouble) ||
                !TryReadLengthCell(cells, headers, x1, defaultUnit, out double ex, tryParseDouble) ||
                !TryReadLengthCell(cells, headers, z1, defaultUnit, out double ez, tryParseDouble))
            {
                return false;
            }

            double sy = 0.0;
            double ey = 0.0;
            if (y0 >= 0 && !TryReadLengthCell(cells, headers, y0, defaultUnit, out sy, tryParseDouble))
            {
                return false;
            }

            if (y1 >= 0 && !TryReadLengthCell(cells, headers, y1, defaultUnit, out ey, tryParseDouble))
            {
                return false;
            }

            if (GetDistanceFt(sx, sy, sz, ex, ey, ez) < 1.0e-6)
            {
                return false;
            }

            segment = new AdaptTendonProfileSegmentPayload
            {
                X0Ft = sx,
                Y0Ft = sy,
                Z0Ft = sz,
                X1Ft = ex,
                Y1Ft = ey,
                Z1Ft = ez
            };
            return true;
        }

        private static int FindHeaderRow(IReadOnlyList<string[]> rows, Func<string, string> normalizeImportedHeaderCell, out AdaptTendonHeaderMap map)
        {
            map = null;
            int scanMax = Math.Min(rows.Count - 1, 80);
            for (int i = 0; i <= scanMax; i++)
            {
                string[] headers = rows[i] ?? Array.Empty<string>();
                if (headers.Length == 0)
                {
                    continue;
                }

                AdaptTendonHeaderMap candidate = BuildHeaderMap(headers, normalizeImportedHeaderCell);
                if (candidate.HasAnyGeometry)
                {
                    map = candidate;
                    return i;
                }
            }

            return -1;
        }

        private static AdaptTendonHeaderMap BuildHeaderMap(IReadOnlyList<string> headers, Func<string, string> normalizeImportedHeaderCell)
        {
            return new AdaptTendonHeaderMap
            {
                Profile = IndexOfHeader(headers, normalizeImportedHeaderCell, "Profile", "Profile Name", "Tendon Profile", "Profile ID", "Group", "Group Name"),
                Tendon = IndexOfHeader(headers, normalizeImportedHeaderCell, "Tendon", "Tendon Name", "Tendon ID", "Tendon Mark", "Tendon No", "Cable", "Cable Name", "Name", "ID", "Support Line", "Strip", "Frame", "Span"),
                Point = IndexOfHeader(headers, normalizeImportedHeaderCell, "Point", "Point No", "Point Number", "Pt", "No", "Index", "Order", "Sequence", "Seq"),
                X = IndexOfHeader(headers, normalizeImportedHeaderCell, "X", "X Coord", "X Coordinate", "Coord X", "Point X", "Plan X", "Global X", "X Location", "Plan X Coordinate"),
                Y = IndexOfHeader(headers, normalizeImportedHeaderCell, "Y", "Y Coord", "Y Coordinate", "Coord Y", "Point Y", "Plan Y", "Global Y", "Y Location", "Plan Y Coordinate"),
                Z = IndexOfHeader(headers, normalizeImportedHeaderCell, "Z", "Z Coord", "Z Coordinate", "Coord Z", "Elevation", "Elev", "Tendon Elevation", "Profile Elevation", "Height", "CGS", "Tendon CGS", "Profile CGS"),
                Station = IndexOfHeader(headers, normalizeImportedHeaderCell, "Station", "Chainage", "Distance", "Distance From Start", "DistanceFromStart", "Location", "Sta", "Distance Along", "Along", "Position", "Profile Station"),
                Elevation = IndexOfHeader(headers, normalizeImportedHeaderCell, "Elevation", "Elev", "Profile Elevation", "Tendon Elevation", "Z", "Height", "CGS", "Tendon CGS", "Profile CGS", "Profile Height"),
                StartX = IndexOfHeader(headers, normalizeImportedHeaderCell, "Start X", "X Start", "Begin X", "X0", "X1"),
                StartY = IndexOfHeader(headers, normalizeImportedHeaderCell, "Start Y", "Y Start", "Begin Y", "Y0", "Y1"),
                StartZ = IndexOfHeader(headers, normalizeImportedHeaderCell, "Start Z", "Z Start", "Begin Z", "Start Elevation", "Start Elev", "Start Height", "Start CGS", "Z0", "Z1"),
                EndX = IndexOfHeader(headers, normalizeImportedHeaderCell, "End X", "X End", "Finish X", "X2"),
                EndY = IndexOfHeader(headers, normalizeImportedHeaderCell, "End Y", "Y End", "Finish Y", "Y2"),
                EndZ = IndexOfHeader(headers, normalizeImportedHeaderCell, "End Z", "Z End", "Finish Z", "End Elevation", "End Elev", "End Height", "End CGS", "Z2"),
                StartStation = IndexOfHeader(headers, normalizeImportedHeaderCell, "Start Station", "Station Start", "Begin Station", "Start Chainage", "Start Distance", "Start Distance Along"),
                StartElevation = IndexOfHeader(headers, normalizeImportedHeaderCell, "Start Elevation", "Start Elev", "Begin Elevation", "Begin Elev", "Start Z", "Start Height", "Start CGS"),
                EndStation = IndexOfHeader(headers, normalizeImportedHeaderCell, "End Station", "Station End", "Finish Station", "End Chainage", "End Distance", "End Distance Along"),
                EndElevation = IndexOfHeader(headers, normalizeImportedHeaderCell, "End Elevation", "End Elev", "Finish Elevation", "Finish Elev", "End Z", "End Height", "End CGS")
            };
        }

        private static AdaptTendonLengthUnit InferDefaultUnit(IReadOnlyList<string[]> rows, int firstDataRow, AdaptTendonHeaderMap map, PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            double maxAbs = 0.0;
            int[] indexes = map.Mode == AdaptTendonImportMode.Model3D
                ? new[] { map.X, map.Y, map.Z, map.StartX, map.StartY, map.StartZ, map.EndX, map.EndY, map.EndZ }
                : new[] { map.Station, map.Elevation, map.StartStation, map.StartElevation, map.EndStation, map.EndElevation };

            int scanMax = Math.Min(rows.Count - 1, firstDataRow + 200);
            for (int r = firstDataRow; r <= scanMax; r++)
            {
                string[] cells = rows[r] ?? Array.Empty<string>();
                foreach (int index in indexes)
                {
                    if (index < 0 || index >= cells.Length)
                    {
                        continue;
                    }

                    if (TryParseCleanNumber(CleanNumber(cells[index]), tryParseDouble, out double value))
                    {
                        maxAbs = Math.Max(maxAbs, Math.Abs(value));
                    }
                }
            }

            return maxAbs > 500.0 ? AdaptTendonLengthUnit.Millimeter : AdaptTendonLengthUnit.Meter;
        }

        private static bool TryReadLengthCell(IReadOnlyList<string> cells, IReadOnlyList<string> headers, int index, AdaptTendonLengthUnit defaultUnit, out double feet, PtDrawingTryParseDoubleDelegate tryParseDouble)
        {
            feet = 0.0;
            if (index < 0 || index >= cells.Count)
            {
                return false;
            }

            string raw = CleanNumber(cells[index]);
            if (!TryParseCleanNumber(raw, tryParseDouble, out double value))
            {
                return false;
            }

            string header = index >= 0 && index < headers.Count ? headers[index] : "";
            feet = ConvertLengthToFeet(value, ResolveUnit(header, defaultUnit));
            return true;
        }

        private static AdaptTendonLengthUnit ResolveUnit(string header, AdaptTendonLengthUnit defaultUnit)
        {
            string text = (header ?? "").Trim().ToLowerInvariant();
            if (text.Contains("mm") || text.Contains("millimeter") || text.Contains("millimetre")) return AdaptTendonLengthUnit.Millimeter;
            if (text.Contains("cm") || text.Contains("centimeter") || text.Contains("centimetre")) return AdaptTendonLengthUnit.Centimeter;
            if (text.Contains("inch") || text.Contains("(in)") || text.Contains("[in]")) return AdaptTendonLengthUnit.Inch;
            if (text.Contains("feet") || text.Contains("foot") || text.Contains("(ft)") || text.Contains("[ft]")) return AdaptTendonLengthUnit.Foot;
            if (text.Contains("meter") || text.Contains("metre") || text.Contains("(m)") || text.Contains("[m]")) return AdaptTendonLengthUnit.Meter;
            return defaultUnit;
        }

        private static string CleanNumber(string raw)
        {
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            text = Regex.Replace(text, "(mm|cm|ft|in|m)", "", RegexOptions.IgnoreCase)
                .Replace("\"", "")
                .Replace("'", "")
                .Trim();
            return text;
        }

        private static string GetCell(IReadOnlyList<string> cells, int index)
        {
            if (index < 0 || index >= cells.Count)
            {
                return "";
            }

            return (cells[index] ?? "").Trim();
        }

        private static int IndexOfHeader(IReadOnlyList<string> headers, Func<string, string> normalizeImportedHeaderCell, params string[] names)
        {
            if (headers == null || names == null)
            {
                return -1;
            }

            List<string> targetTokens = names.Select(item => NormalizeHeaderToken(item, normalizeImportedHeaderCell)).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
            for (int i = 0; i < headers.Count; i++)
            {
                string token = NormalizeHeaderToken(headers[i], normalizeImportedHeaderCell);
                if (targetTokens.Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase)))
                {
                    return i;
                }
            }

            for (int i = 0; i < headers.Count; i++)
            {
                string token = NormalizeHeaderToken(headers[i], normalizeImportedHeaderCell);
                if (targetTokens.Any(item => item.Length > 2 && token.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeHeaderToken(string raw, Func<string, string> normalizeImportedHeaderCell)
        {
            string normalized = normalizeImportedHeaderCell == null ? (raw ?? "") : normalizeImportedHeaderCell(raw ?? "");
            string text = normalized.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            int unitStart = text.IndexOf('(');
            if (unitStart >= 0)
            {
                text = text.Substring(0, unitStart);
            }

            unitStart = text.IndexOf('[');
            if (unitStart >= 0)
            {
                text = text.Substring(0, unitStart);
            }

            var builder = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string BuildSourceLabel(string profile, string tendon, int rowNumber)
        {
            string group = BuildGroupKey(profile, tendon);
            if (!string.IsNullOrWhiteSpace(group))
            {
                return group;
            }

            return "row " + rowNumber.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseCleanNumber(string raw, PtDrawingTryParseDoubleDelegate tryParseDouble, out double value)
        {
            value = 0.0;
            return tryParseDouble != null && tryParseDouble(raw, out value);
        }
    }
}
