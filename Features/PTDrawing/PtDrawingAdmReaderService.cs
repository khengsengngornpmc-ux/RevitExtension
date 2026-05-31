using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingAdmReaderService
    {
        public static AdaptTendonImportReadResult ReadGeometry(string path)
        {
            var result = new AdaptTendonImportReadResult
            {
                Mode = AdaptTendonImportMode.Model3D,
                InferredUnit = AdaptTendonLengthUnit.Meter,
                SheetCount = 1
            };

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return result;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Dictionary<string, string> layerNames = BuildLayerNameMap(bytes);
            List<AdaptAdmTendonRecord> records = ReadTendonRecords(bytes, layerNames);
            if (records.Count == 0)
            {
                return result;
            }

            double maxAbs = 0.0;
            foreach (AdaptAdmTendonRecord record in records)
            {
                foreach (AdaptAdmPoint point in record.Points)
                {
                    maxAbs = Math.Max(maxAbs, Math.Abs(point.X));
                    maxAbs = Math.Max(maxAbs, Math.Abs(point.Y));
                    maxAbs = Math.Max(maxAbs, Math.Abs(point.Z));
                }
            }

            result.InferredUnit = maxAbs > 500.0
                ? AdaptTendonLengthUnit.Millimeter
                : AdaptTendonLengthUnit.Meter;

            foreach (AdaptAdmTendonRecord record in records)
            {
                for (int i = 1; i < record.Points.Count; i++)
                {
                    AdaptAdmPoint a = record.Points[i - 1];
                    AdaptAdmPoint b = record.Points[i];
                    ConvertPointToRevitFeet(a, result.InferredUnit, out double ax, out double ay, out double az);
                    ConvertPointToRevitFeet(b, result.InferredUnit, out double bx, out double by, out double bz);

                    if (PtDrawingTableRowParserService.GetDistanceFt(ax, ay, az, bx, by, bz) < 1.0e-6)
                    {
                        continue;
                    }

                    result.Segments.Add(new AdaptTendonProfileSegmentPayload
                    {
                        ProfileName = !string.IsNullOrWhiteSpace(record.LayerName) ? record.LayerName : record.LayerToken,
                        TendonName = record.TendonName,
                        SourceLabel = PtDrawingTableRowParserService.BuildGroupKey(record.LayerName, record.TendonName),
                        X0Ft = ax,
                        Y0Ft = ay,
                        Z0Ft = az,
                        X1Ft = bx,
                        Y1Ft = by,
                        Z1Ft = bz
                    });
                }

                result.PointCount += record.Points.Count;
            }

            result.ProfileCount = result.Segments
                .Select(PtDrawingTableRowParserService.BuildSegmentGroupKey)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return result;
        }

        private static List<AdaptAdmTendonRecord> ReadTendonRecords(byte[] bytes, IDictionary<string, string> layerNames)
        {
            var records = new List<AdaptAdmTendonRecord>();
            if (bytes == null || bytes.Length == 0)
            {
                return records;
            }

            byte[] recordName = Encoding.ASCII.GetBytes("ADPTTendonE");
            List<int> offsets = FindAsciiOccurrences(bytes, recordName).ToList();
            for (int i = 0; i < offsets.Count; i++)
            {
                int offset = offsets[i];
                int endOffset = i + 1 < offsets.Count
                    ? offsets[i + 1]
                    : Math.Min(bytes.Length, offset + 80000);

                if (!TryReadTendonRecords(bytes, offset, endOffset, layerNames, out List<AdaptAdmTendonRecord> tendonRecords))
                {
                    continue;
                }

                records.AddRange(tendonRecords);
            }

            return records;
        }

        private static bool TryReadTendonRecords(
            byte[] bytes,
            int recordNameOffset,
            int recordEndOffset,
            IDictionary<string, string> layerNames,
            out List<AdaptAdmTendonRecord> records)
        {
            records = null;
            if (bytes == null || recordNameOffset < 0 || recordNameOffset >= bytes.Length)
            {
                return false;
            }

            byte[] continuous = Encoding.ASCII.GetBytes("CONTINUOUS");
            if (!TryFindAscii(bytes, continuous, recordNameOffset, Math.Min(bytes.Length, recordNameOffset + 220), out int continuousOffset))
            {
                return false;
            }

            int position = continuousOffset + continuous.Length;
            if (!TryReadString(bytes, position, out string layerToken, out position) ||
                !TryReadString(bytes, position, out string tendonName, out position))
            {
                return false;
            }

            string layerName = "";
            if (layerNames != null && !string.IsNullOrWhiteSpace(layerToken))
            {
                layerNames.TryGetValue(layerToken, out layerName);
            }

            if (!IsTendonLayer(layerToken, layerName))
            {
                return false;
            }

            AdaptAdmPointBlock planBlock = null;
            if (!TryReadPointBlock(bytes, position + 48, recordEndOffset, out planBlock))
            {
                for (int countOffset = position + 32; countOffset <= position + 90; countOffset++)
                {
                    if (TryReadPointBlock(bytes, countOffset, recordEndOffset, out planBlock))
                    {
                        break;
                    }
                }
            }

            if (planBlock == null || planBlock.Points.Count < 2)
            {
                return false;
            }

            List<AdaptAdmPointBlock> profileBlocks = ReadProfilePointBlocks(
                bytes,
                position + 90,
                recordEndOffset,
                planBlock);

            if (profileBlocks.Count == 0)
            {
                return false;
            }

            records = new List<AdaptAdmTendonRecord>();
            int profileIndex = 1;
            foreach (AdaptAdmPointBlock profileBlock in profileBlocks)
            {
                var record = new AdaptAdmTendonRecord
                {
                    LayerToken = layerToken ?? "",
                    LayerName = layerName ?? "",
                    TendonName = BuildProfileName(tendonName, recordNameOffset, profileIndex)
                };
                record.Points.AddRange(profileBlock.Points);
                records.Add(record);
                profileIndex++;
            }

            return true;
        }

        private static Dictionary<string, string> BuildLayerNameMap(byte[] bytes)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bytes == null || bytes.Length == 0)
            {
                return map;
            }

            byte[] layerPrefix = Encoding.ASCII.GetBytes("ADPTLayer");
            foreach (int offset in FindAsciiOccurrences(bytes, layerPrefix))
            {
                if (!TryReadStringAtTextOffset(bytes, offset, out string layerToken, out int nextOffset) ||
                    !TryReadString(bytes, nextOffset, out string displayName, out _))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(layerToken) || string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                string normalized = PtDrawingSourceService.NormalizeFileToken(displayName);
                if (!normalized.Contains("current") &&
                    !normalized.Contains("tendon") &&
                    !normalized.Contains("band") &&
                    !normalized.Contains("distributed") &&
                    !normalized.Contains("harped") &&
                    !normalized.Contains("profile") &&
                    !normalized.Contains("boundary") &&
                    !normalized.Contains("support") &&
                    !normalized.Contains("dimension") &&
                    !normalized.Contains("template"))
                {
                    continue;
                }

                if (!map.ContainsKey(layerToken))
                {
                    map[layerToken] = displayName.Trim();
                }
            }

            return map;
        }

        private static string BuildProfileName(string tendonName, int recordNameOffset, int profileIndex)
        {
            string baseName = string.IsNullOrWhiteSpace(tendonName)
                ? "Tendon @" + recordNameOffset.ToString(CultureInfo.InvariantCulture)
                : tendonName.Trim();
            return baseName + " Profile " + profileIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static List<AdaptAdmPointBlock> ReadProfilePointBlocks(
            byte[] bytes,
            int scanStartOffset,
            int recordEndOffset,
            AdaptAdmPointBlock planBlock)
        {
            var blocks = new List<AdaptAdmPointBlock>();
            if (bytes == null || planBlock == null)
            {
                return blocks;
            }

            int start = Math.Max(0, scanStartOffset);
            int end = Math.Min(bytes.Length, recordEndOffset);
            for (int offset = start; offset < end - 40;)
            {
                if (TryReadPointBlock(bytes, offset, end, out AdaptAdmPointBlock block) &&
                    IsProfilePointBlock(block, planBlock))
                {
                    blocks.Add(block);
                    offset = Math.Max(offset + 1, block.EndOffset);
                    continue;
                }

                offset++;
            }

            return blocks;
        }

        private static bool IsProfilePointBlock(AdaptAdmPointBlock block, AdaptAdmPointBlock planBlock)
        {
            if (block == null || planBlock == null || block.Points.Count < 3)
            {
                return false;
            }

            double xRange = block.MaxX - block.MinX;
            double yRange = block.MaxY - block.MinY;
            double zRange = block.MaxZ - block.MinZ;
            double horizontalRange = Math.Max(xRange, zRange);
            double crossRange = Math.Min(xRange, zRange);

            double planRangeX = Math.Max(0.0, planBlock.MaxX - planBlock.MinX);
            double planRangeZ = Math.Max(0.0, planBlock.MaxZ - planBlock.MinZ);
            double planHorizontalRange = Math.Max(planRangeX, planRangeZ);
            double adaptivePlanTolerance = Math.Max(2.0, planHorizontalRange * 0.08);
            double adaptiveElevationTolerance = Math.Max(2.5, Math.Max(yRange, planHorizontalRange) * 0.08);

            if (yRange < 0.02 ||
                horizontalRange < 1.0 ||
                block.PathLength < 1.0 ||
                crossRange > Math.Max(adaptivePlanTolerance, horizontalRange * 0.35))
            {
                return false;
            }

            if (!RangesOverlap(block.MinX, block.MaxX, planBlock.MinX, planBlock.MaxX, adaptivePlanTolerance) ||
                !RangesOverlap(block.MinZ, block.MaxZ, planBlock.MinZ, planBlock.MaxZ, adaptivePlanTolerance))
            {
                return false;
            }

            double planElevation = (planBlock.MinY + planBlock.MaxY) * 0.5;
            double profileElevation = (block.MinY + block.MaxY) * 0.5;
            return Math.Abs(profileElevation - planElevation) <= adaptiveElevationTolerance;
        }

        private static bool RangesOverlap(double minA, double maxA, double minB, double maxB, double tolerance)
        {
            return minA <= maxB + tolerance && maxA >= minB - tolerance;
        }

        private static bool TryReadPointBlock(byte[] bytes, int countOffset, int endOffset, out AdaptAdmPointBlock block)
        {
            block = null;
            if (bytes == null || countOffset < 0 || countOffset + 16 > endOffset)
            {
                return false;
            }

            int pointCount = BitConverter.ToInt32(bytes, countOffset);
            if (pointCount < 2 || pointCount > 400)
            {
                return false;
            }

            int dataOffset = countOffset + 12;
            if (dataOffset < 0 || dataOffset + (pointCount * 24) > endOffset)
            {
                return false;
            }

            double polylineLength = 0.0;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;
            var parsed = new List<AdaptAdmPoint>();
            AdaptAdmPoint previous = null;
            for (int i = 0; i < pointCount; i++)
            {
                int pointOffset = dataOffset + (i * 24);
                double x = BitConverter.ToDouble(bytes, pointOffset);
                double y = BitConverter.ToDouble(bytes, pointOffset + 8);
                double z = BitConverter.ToDouble(bytes, pointOffset + 16);
                if (!IsReasonableCoordinate(x) || !IsReasonableCoordinate(y) || !IsReasonableCoordinate(z))
                {
                    return false;
                }

                var point = new AdaptAdmPoint { X = x, Y = y, Z = z };

                if (previous != null)
                {
                    double dx = point.X - previous.X;
                    double dy = point.Y - previous.Y;
                    double dz = point.Z - previous.Z;
                    polylineLength += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                }

                parsed.Add(point);
                previous = point;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }

            if (polylineLength < 0.01)
            {
                return false;
            }

            block = new AdaptAdmPointBlock
            {
                CountOffset = countOffset,
                EndOffset = dataOffset + (pointCount * 24),
                PathLength = polylineLength,
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                MinZ = minZ,
                MaxZ = maxZ
            };
            block.Points.AddRange(parsed);
            return true;
        }

        private static bool IsReasonableCoordinate(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   Math.Abs(value) <= 100000.0;
        }

        private static bool IsTendonLayer(string layerToken, string layerName)
        {
            string token = PtDrawingSourceService.NormalizeFileToken(layerToken);
            string name = PtDrawingSourceService.NormalizeFileToken(layerName);
            if (name.Contains("ctrl") || name.Contains("supportbar") || name.Contains("txt") || name.Contains("template"))
            {
                return false;
            }

            if (name.Contains("tendon") ||
                name.Contains("band") ||
                name.Contains("distributed") ||
                name.Contains("harped") ||
                name.Contains("profile"))
            {
                return true;
            }

            return token.StartsWith("adaptlayer", StringComparison.OrdinalIgnoreCase);
        }

        private static void ConvertPointToRevitFeet(
            AdaptAdmPoint point,
            AdaptTendonLengthUnit unit,
            out double xFt,
            out double yFt,
            out double zFt)
        {
            xFt = PtDrawingTableRowParserService.ConvertLengthToFeet(point.X, unit);
            yFt = PtDrawingTableRowParserService.ConvertLengthToFeet(point.Z, unit);
            zFt = PtDrawingTableRowParserService.ConvertLengthToFeet(point.Y, unit);
        }

        private static IEnumerable<int> FindAsciiOccurrences(byte[] bytes, byte[] needle)
        {
            if (bytes == null || needle == null || bytes.Length == 0 || needle.Length == 0 || needle.Length > bytes.Length)
            {
                yield break;
            }

            for (int i = 0; i <= bytes.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (bytes[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    yield return i;
                }
            }
        }

        private static bool TryFindAscii(byte[] bytes, byte[] needle, int startOffset, int endOffset, out int offset)
        {
            offset = -1;
            if (bytes == null || needle == null || needle.Length == 0)
            {
                return false;
            }

            int start = Math.Max(0, startOffset);
            int end = Math.Min(bytes.Length - needle.Length, Math.Max(start, endOffset - needle.Length));
            for (int i = start; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (bytes[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    offset = i;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadStringAtTextOffset(byte[] bytes, int textOffset, out string text, out int nextOffset)
        {
            text = "";
            nextOffset = textOffset;
            if (bytes == null || textOffset < 4)
            {
                return false;
            }

            return TryReadString(bytes, textOffset - 4, out text, out nextOffset);
        }

        private static bool TryReadString(byte[] bytes, int lengthOffset, out string text, out int nextOffset)
        {
            text = "";
            nextOffset = lengthOffset;
            if (bytes == null || lengthOffset < 0 || lengthOffset + 4 > bytes.Length)
            {
                return false;
            }

            int length = BitConverter.ToInt32(bytes, lengthOffset);
            if (length < 0 || length > 512 || lengthOffset + 4 + length > bytes.Length)
            {
                return false;
            }

            string value = Encoding.ASCII.GetString(bytes, lengthOffset + 4, length);
            if (value.Any(ch => ch < 32 || ch > 126))
            {
                return false;
            }

            text = value;
            nextOffset = lengthOffset + 4 + length;
            return true;
        }
    }
}
