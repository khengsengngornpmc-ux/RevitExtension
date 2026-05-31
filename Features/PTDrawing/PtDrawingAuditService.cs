using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingAuditService
    {
        private sealed class AuditCandidate
        {
            public PtImportJsonTendon Tendon { get; set; }
            public double AnchorXFt { get; set; }
            public double AnchorYFt { get; set; }
            public double LengthFt { get; set; }
            public string Signature { get; set; } = "";
            public string DefaultLabel { get; set; } = "";
            public bool IsCadLabel { get; set; }
        }

        private sealed class AuditAssignment
        {
            public int RowNumber { get; set; }
            public PtImportJsonTendon Tendon { get; set; }
            public string CurrentMark { get; set; } = "";
            public string ProposedMark { get; set; } = "";
            public double LengthFt { get; set; }
            public bool IsCadLabel { get; set; }
        }

        public static PtDrawingAuditPreviewPackage BuildPreview(
            string sourcePath,
            bool isCadWorkflow,
            PtDrawingSnapshotRecord snapshot,
            PtImportJsonDocument document,
            string prefix,
            int startNumber,
            int digits,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool preserveCadShopMarks)
        {
            List<AuditAssignment> assignments = BuildAssignments(
                document,
                isCadWorkflow,
                prefix,
                startNumber,
                digits,
                sequenceMode,
                preserveCadShopMarks);

            var items = new List<MhnkCadPreviewItem>();
            foreach (AuditAssignment assignment in assignments)
            {
                PtImportJsonTendon tendon = assignment.Tendon ?? new PtImportJsonTendon();
                string currentMark = (assignment.CurrentMark ?? "").Trim();
                string proposedMark = (assignment.ProposedMark ?? "").Trim();
                string label = BuildDefaultLabel(tendon);
                string layer = (tendon.SourceLayer ?? "").Trim();
                if (string.IsNullOrWhiteSpace(layer))
                {
                    layer = (tendon.ProfileName ?? "").Trim();
                }

                if (string.IsNullOrWhiteSpace(layer))
                {
                    layer = "-";
                }

                string markNote = string.IsNullOrWhiteSpace(currentMark)
                    ? "Current: -"
                    : (string.Equals(currentMark, proposedMark, StringComparison.OrdinalIgnoreCase)
                        ? "Current kept: " + currentMark
                        : "Current: " + currentMark);

                items.Add(new MhnkCadPreviewItem
                {
                    Index = assignment.RowNumber.ToString(CultureInfo.InvariantCulture),
                    Source = isCadWorkflow ? "DWG / DXF" : "Direct ADAPT",
                    Layer = layer,
                    Rule = TruncateText(label, 26),
                    Target = proposedMark,
                    Quantity = BuildQuantityText(assignment.LengthFt),
                    Status = "Ready",
                    Notes = markNote +
                        (assignment.IsCadLabel ? " | CAD label" : " | Renumber") +
                        (string.IsNullOrWhiteSpace(tendon.TendonName) ? "" : " | Tendon: " + TruncateText(tendon.TendonName, 24))
                });
            }

            int changeCount = assignments.Count(item => !string.Equals(item.CurrentMark ?? "", item.ProposedMark ?? "", StringComparison.OrdinalIgnoreCase));
            string snapshotText = snapshot == null
                ? "Live source preview"
                : snapshot.SnapshotTimeLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            return new PtDrawingAuditPreviewPackage
            {
                Items = items,
                TendonCount = assignments.Count,
                ChangeCount = changeCount,
                Summary =
                    "Action: Renumber / Audit Existing PT" + Environment.NewLine +
                    "Source: " + (string.IsNullOrWhiteSpace(sourcePath) ? "-" : System.IO.Path.GetFileName(sourcePath)) + Environment.NewLine +
                    "Method: " + (isCadWorkflow ? "DWG / DXF" : "Direct ADAPT") + Environment.NewLine +
                    "Snapshot: " + snapshotText + Environment.NewLine +
                    "Tendons audited: " + assignments.Count.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Marks changing: " + changeCount.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Proposed first mark: " + BuildShopMark(startNumber, prefix, digits) + Environment.NewLine +
                    "Sequence: " + DescribeSequenceMode(sequenceMode) +
                    (isCadWorkflow
                        ? Environment.NewLine + "CAD label reuse: " + (preserveCadShopMarks ? "Enabled" : "Disabled")
                        : "")
            };
        }

        private static List<AuditAssignment> BuildAssignments(
            PtImportJsonDocument document,
            bool isCadWorkflow,
            string prefix,
            int startNumber,
            int digits,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool preserveCadShopMarks)
        {
            List<AuditCandidate> orderedCandidates = OrderCandidates(
                BuildCandidates(document),
                sequenceMode,
                isCadWorkflow);

            var usedMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (isCadWorkflow && preserveCadShopMarks)
            {
                foreach (AuditCandidate candidate in orderedCandidates.Where(item => item != null && item.IsCadLabel))
                {
                    string mark = (candidate.Tendon?.ShopMark ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(mark))
                    {
                        usedMarks.Add(mark);
                    }
                }
            }

            int nextNumber = Math.Max(1, startNumber);
            var assignments = new List<AuditAssignment>();
            for (int i = 0; i < orderedCandidates.Count; i++)
            {
                AuditCandidate candidate = orderedCandidates[i];
                PtImportJsonTendon tendon = candidate?.Tendon ?? new PtImportJsonTendon();
                string currentMark = (tendon.ShopMark ?? "").Trim();
                string proposedMark;

                if (isCadWorkflow &&
                    preserveCadShopMarks &&
                    candidate != null &&
                    candidate.IsCadLabel &&
                    !string.IsNullOrWhiteSpace(currentMark))
                {
                    proposedMark = currentMark;
                }
                else
                {
                    proposedMark = BuildNextShopMark(usedMarks, ref nextNumber, prefix, digits);
                }

                assignments.Add(new AuditAssignment
                {
                    RowNumber = i + 1,
                    Tendon = tendon,
                    CurrentMark = currentMark,
                    ProposedMark = proposedMark,
                    LengthFt = candidate?.LengthFt ?? 0.0,
                    IsCadLabel = candidate != null && candidate.IsCadLabel
                });
            }

            return assignments;
        }

        private static List<AuditCandidate> BuildCandidates(PtImportJsonDocument document)
        {
            var candidates = new List<AuditCandidate>();
            foreach (PtImportJsonTendon tendon in (document?.Tendons ?? new List<PtImportJsonTendon>()).Where(item => item != null))
            {
                IList<PtImportJsonPoint> points = tendon.CenterlinePoints ?? new List<PtImportJsonPoint>();
                candidates.Add(new AuditCandidate
                {
                    Tendon = tendon,
                    AnchorXFt = GetAnchorCoordinate(points, true),
                    AnchorYFt = GetAnchorCoordinate(points, false),
                    LengthFt = GetLengthFt(tendon),
                    Signature = BuildSignature(points),
                    DefaultLabel = BuildDefaultLabel(tendon),
                    IsCadLabel = IsCadLabel(tendon)
                });
            }

            return candidates;
        }

        private static List<AuditCandidate> OrderCandidates(
            IEnumerable<AuditCandidate> candidates,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool isCadWorkflow)
        {
            IEnumerable<AuditCandidate> query = (candidates ?? Enumerable.Empty<AuditCandidate>())
                .Where(item => item != null);

            switch (sequenceMode)
            {
                case AdaptPtShopMarkSequenceMode.LeftToRight:
                    query = query
                        .OrderBy(item => item.AnchorXFt)
                        .ThenBy(item => item.AnchorYFt)
                        .ThenBy(item => item.Signature ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
                case AdaptPtShopMarkSequenceMode.BottomToTop:
                    query = query
                        .OrderBy(item => item.AnchorYFt)
                        .ThenBy(item => item.AnchorXFt)
                        .ThenBy(item => item.Signature ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
                case AdaptPtShopMarkSequenceMode.TopToBottom:
                    query = query
                        .OrderByDescending(item => item.AnchorYFt)
                        .ThenBy(item => item.AnchorXFt)
                        .ThenBy(item => item.Signature ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
                case AdaptPtShopMarkSequenceMode.LongToShort:
                    query = query
                        .OrderByDescending(item => item.LengthFt)
                        .ThenBy(item => item.AnchorYFt)
                        .ThenBy(item => item.AnchorXFt)
                        .ThenBy(item => item.Signature ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    query = isCadWorkflow
                        ? query
                            .OrderBy(item => item.IsCadLabel ? 0 : 1)
                            .ThenBy(item => item.IsCadLabel ? (item.Tendon?.ShopMark ?? "") : "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.Signature ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.LengthFt)
                        : query
                            .OrderBy(item => item.DefaultLabel ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.Signature ?? "", StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.LengthFt);
                    break;
            }

            return query.ToList();
        }

        private static string BuildDefaultLabel(PtImportJsonTendon tendon)
        {
            if (tendon == null)
            {
                return "";
            }

            string profile = (tendon.ProfileName ?? "").Trim();
            string tendonName = (tendon.TendonName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(profile) &&
                !string.IsNullOrWhiteSpace(tendonName) &&
                !string.Equals(profile, tendonName, StringComparison.OrdinalIgnoreCase))
            {
                return profile + " / " + tendonName;
            }

            if (!string.IsNullOrWhiteSpace(tendonName))
            {
                return tendonName;
            }

            if (!string.IsNullOrWhiteSpace(profile))
            {
                return profile;
            }

            if (!string.IsNullOrWhiteSpace(tendon.SourceLabel))
            {
                return tendon.SourceLabel.Trim();
            }

            return (tendon.TendonId ?? "").Trim();
        }

        private static bool IsCadLabel(PtImportJsonTendon tendon)
        {
            if (tendon == null)
            {
                return false;
            }

            if (string.Equals((tendon.TendonType ?? "").Trim(), "cad-labeled-chain", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals((tendon.SourceLabel ?? "").Trim(), "CAD label", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return (tendon.Tags ?? new List<string>())
                .Any(tag => string.Equals((tag ?? "").Trim(), "cad-label", StringComparison.OrdinalIgnoreCase));
        }

        private static double GetLengthFt(PtImportJsonTendon tendon)
        {
            if (tendon?.ProfileHints?.TotalLengthFt is double hintedLength && hintedLength > 1.0e-6)
            {
                return hintedLength;
            }

            IList<PtImportJsonPoint> points = tendon?.CenterlinePoints ?? new List<PtImportJsonPoint>();
            double length = 0.0;
            for (int i = 1; i < points.Count; i++)
            {
                double dx = points[i].XFt - points[i - 1].XFt;
                double dy = points[i].YFt - points[i - 1].YFt;
                double dz = points[i].ZFt - points[i - 1].ZFt;
                length += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }

            return length;
        }

        private static double GetAnchorCoordinate(IList<PtImportJsonPoint> points, bool useX)
        {
            if (points == null || points.Count == 0)
            {
                return 0.0;
            }

            double min = useX ? points.Min(point => point.XFt) : points.Min(point => point.YFt);
            double max = useX ? points.Max(point => point.XFt) : points.Max(point => point.YFt);
            return 0.5 * (min + max);
        }

        private static string BuildSignature(IList<PtImportJsonPoint> points)
        {
            if (points == null || points.Count == 0)
            {
                return "";
            }

            return string.Join(
                ";",
                points.Select(point =>
                    point.XFt.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    point.YFt.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    point.ZFt.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        private static string BuildNextShopMark(
            ISet<string> usedMarks,
            ref int nextNumber,
            string prefix,
            int digits)
        {
            ISet<string> registry = usedMarks ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int number = Math.Max(1, nextNumber);
            string mark;
            do
            {
                mark = BuildShopMark(number, prefix, digits);
                number++;
            }
            while (registry.Contains(mark));

            registry.Add(mark);
            nextNumber = number;
            return mark;
        }

        private static string BuildShopMark(int number, string prefix, int digits)
        {
            string safePrefix = NormalizePrefix(prefix);
            int safeDigits = Math.Max(1, Math.Min(6, digits));
            int safeNumber = Math.Max(1, number);
            return safePrefix + "-" + safeNumber.ToString("D" + safeDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static string NormalizePrefix(string prefix)
        {
            string normalized = Regex.Replace((prefix ?? "").Trim(), @"[^A-Za-z0-9_\-]+", "");
            return string.IsNullOrWhiteSpace(normalized) ? "PT" : normalized;
        }

        private static string BuildQuantityText(double lengthFt)
        {
            double lengthMeters = lengthFt * 0.3048;
            return lengthMeters.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }

        private static string TruncateText(string text, int maxLength)
        {
            string value = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value) || maxLength < 4 || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private static string DescribeSequenceMode(AdaptPtShopMarkSequenceMode mode)
        {
            switch (mode)
            {
                case AdaptPtShopMarkSequenceMode.LeftToRight:
                    return "Left to right";
                case AdaptPtShopMarkSequenceMode.BottomToTop:
                    return "Bottom to top";
                case AdaptPtShopMarkSequenceMode.TopToBottom:
                    return "Top to bottom";
                case AdaptPtShopMarkSequenceMode.LongToShort:
                    return "Long to short";
                default:
                    return "Source and tendon name";
            }
        }
    }
}
