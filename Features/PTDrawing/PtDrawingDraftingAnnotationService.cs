using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingDraftingAnnotationService
    {
        private const double FeetPerMillimeter = 1.0 / 304.8;
        private const double CharacterWidthMm = 42.0;
        private const double MinimumRowGapMm = 120.0;
        private const double MinimumVerticalSeparationMm = 180.0;
        private const double MinimumHorizontalInfluenceMm = 800.0;
        private const double MinimumVerticalRangeMm = 40.0;
        private const int MaxLayoutSteps = 16;

        private static readonly Regex MultiWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex ColonPunctuationRegex = new Regex(@"\s*([:;,])\s*", RegexOptions.Compiled);
        private static readonly Regex SlashRegex = new Regex(@"\s*/\s*", RegexOptions.Compiled);
        private static readonly Regex PipeRegex = new Regex(@"\s*\|\s*", RegexOptions.Compiled);
        private static readonly Regex OpenBracketSpacingRegex = new Regex(@"([(\[])\s+", RegexOptions.Compiled);
        private static readonly Regex CloseBracketSpacingRegex = new Regex(@"\s+([)\]])", RegexOptions.Compiled);
        private static readonly Regex DuplicatePunctuationRegex = new Regex(@"([:;,|])\1+", RegexOptions.Compiled);

        public static double EstimateTextWidthFt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0.0;
            }

            int length = Math.Max(1, NormalizeDraftingLine(text).Length);
            return (CharacterWidthMm * length) * FeetPerMillimeter;
        }

        public static IEnumerable<string> BuildWrappedLines(IEnumerable<string> lines, double maxWidthFt)
        {
            foreach (string line in lines ?? Enumerable.Empty<string>())
            {
                foreach (string wrapped in WrapLine(line, maxWidthFt))
                {
                    if (!string.IsNullOrWhiteSpace(wrapped))
                    {
                        yield return wrapped;
                    }
                }
            }
        }

        public static string NormalizeDraftingLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            string normalized = text
                .Replace('\u00A0', ' ')
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('：', ':')
                .Replace('；', ';')
                .Replace('，', ',')
                .Replace('／', '/')
                .Replace('｜', '|')
                .Replace('–', '-')
                .Replace('—', '-')
                .Replace('−', '-');

            normalized = MultiWhitespaceRegex.Replace(normalized, " ");
            normalized = OpenBracketSpacingRegex.Replace(normalized, "$1");
            normalized = CloseBracketSpacingRegex.Replace(normalized, "$1");
            normalized = DuplicatePunctuationRegex.Replace(normalized, "$1");
            normalized = ColonPunctuationRegex.Replace(normalized, "$1 ");
            normalized = SlashRegex.Replace(normalized, " / ");
            normalized = PipeRegex.Replace(normalized, " | ");
            normalized = MultiWhitespaceRegex.Replace(normalized, " ");
            return normalized.Trim();
        }

        public static string TrimTextToWidth(string text, double maxWidthFt)
        {
            string normalized = NormalizeDraftingLine(text);
            if (string.IsNullOrWhiteSpace(normalized) || maxWidthFt <= 1.0e-6)
            {
                return normalized;
            }

            if (EstimateTextWidthFt(normalized) <= maxWidthFt)
            {
                return normalized;
            }

            string candidate = normalized;
            while (candidate.Length > 4 && EstimateTextWidthFt(candidate + "...") > maxWidthFt)
            {
                candidate = candidate.Substring(0, candidate.Length - 1).TrimEnd();
            }

            return candidate.Length < normalized.Length ? candidate + "..." : candidate;
        }

        public static XYZ BuildAlignedBubbleCenter(
            XYZ anchor,
            double preferredX,
            double preferredY,
            double rowGapFt,
            double minimumSeparationFt,
            double horizontalInfluenceFt,
            IList<XYZ> placedCenters)
        {
            double safeRowGapFt = Math.Max(MinimumRowGapMm * FeetPerMillimeter, rowGapFt);
            double safeMinSeparationFt = Math.Max(MinimumVerticalSeparationMm * FeetPerMillimeter, minimumSeparationFt);
            double safeHorizontalInfluenceFt = Math.Max(MinimumHorizontalInfluenceMm * FeetPerMillimeter, horizontalInfluenceFt);

            foreach (XYZ candidate in BuildLayoutCandidates(preferredX, preferredY, safeRowGapFt))
            {
                if (CanPlaceCenter(candidate, placedCenters, safeMinSeparationFt, safeHorizontalInfluenceFt))
                {
                    return candidate;
                }
            }

            return new XYZ(preferredX, preferredY + (MaxLayoutSteps * safeRowGapFt), 0.0);
        }

        public static XYZ BuildAlignedTextCenter(
            XYZ anchor,
            double preferredX,
            double preferredY,
            double rowGapFt,
            double minimumSeparationFt,
            double horizontalInfluenceFt,
            IList<XYZ> placedCenters)
        {
            return BuildAlignedBubbleCenter(
                anchor,
                preferredX,
                preferredY,
                rowGapFt,
                minimumSeparationFt,
                horizontalInfluenceFt,
                placedCenters);
        }

        public static XYZ BuildAlignedTextCenter(
            XYZ anchor,
            double preferredX,
            double preferredY,
            double rowGapFt,
            double minimumSeparationFt,
            double horizontalInfluenceFt,
            IList<XYZ> placedCenters,
            double minY,
            double maxY)
        {
            return BuildAlignedTextCenter(
                anchor,
                preferredX,
                preferredY,
                rowGapFt,
                minimumSeparationFt,
                horizontalInfluenceFt,
                placedCenters,
                minY,
                maxY,
                0.0);
        }

        public static XYZ BuildAlignedTextCenter(
            XYZ anchor,
            double preferredX,
            double preferredY,
            double rowGapFt,
            double minimumSeparationFt,
            double horizontalInfluenceFt,
            IList<XYZ> placedCenters,
            double minY,
            double maxY,
            double minimumAnchorClearanceFt)
        {
            double safeMinY = Math.Min(minY, maxY);
            double safeMaxY = Math.Max(minY, maxY);
            double safeRowGapFt = Math.Max(MinimumRowGapMm * FeetPerMillimeter, rowGapFt);
            double safeMinSeparationFt = Math.Max(MinimumVerticalSeparationMm * FeetPerMillimeter, minimumSeparationFt);
            double safeHorizontalInfluenceFt = Math.Max(MinimumHorizontalInfluenceMm * FeetPerMillimeter, horizontalInfluenceFt);
            double safeAnchorClearanceFt = Math.Max(0.0, minimumAnchorClearanceFt);

            if (safeMaxY - safeMinY < (MinimumVerticalRangeMm * FeetPerMillimeter))
            {
                return new XYZ(preferredX, Clamp(preferredY, safeMinY, safeMaxY), 0.0);
            }

            foreach (XYZ rawCandidate in BuildLayoutCandidates(preferredX, preferredY, safeRowGapFt))
            {
                double candidateY = Clamp(rawCandidate.Y, safeMinY, safeMaxY);
                XYZ candidate = new XYZ(preferredX, candidateY, 0.0);
                if (anchor != null && Math.Abs(candidateY - anchor.Y) < safeAnchorClearanceFt)
                {
                    continue;
                }

                if (CanPlaceCenter(candidate, placedCenters, safeMinSeparationFt, safeHorizontalInfluenceFt))
                {
                    return candidate;
                }
            }

            return new XYZ(preferredX, Clamp(preferredY, safeMinY, safeMaxY), 0.0);
        }

        public static IEnumerable<string> BuildProfileMetadataSummaryLines(
            AdaptTendonProfileSegmentPayload metadataSegment,
            IEnumerable<AdaptTendonProfileSegmentPayload> segments,
            double diameterFt,
            Func<IEnumerable<AdaptTendonProfileSegmentPayload>, int?> tryExtractStrandCount,
            Func<IEnumerable<AdaptTendonProfileSegmentPayload>, string> inferTendonType)
        {
            string tendonMark = BuildMetadataLabel(metadataSegment);
            if (string.IsNullOrWhiteSpace(tendonMark))
            {
                tendonMark = "ADAPT Tendon";
            }

            int? strands = tryExtractStrandCount != null ? tryExtractStrandCount(segments) : null;
            string tendonType = inferTendonType != null ? NormalizeDraftingLine(inferTendonType(segments)) : "";
            string duct = Math.Round(diameterFt * 304.8, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) + " mm";
            string summary = "Mark: " + tendonMark;
            if (strands.HasValue)
            {
                summary += " | Strands: " + strands.Value.ToString(CultureInfo.InvariantCulture);
            }

            yield return NormalizeDraftingLine(summary);

            string secondary = !string.IsNullOrWhiteSpace(tendonType)
                ? "Type: " + tendonType + " | Duct: " + duct
                : "Duct: " + duct;
            yield return NormalizeDraftingLine(secondary);
        }

        public static string BuildMetadataLabel(AdaptTendonProfileSegmentPayload segment)
        {
            string profile = NormalizeDraftingLine(segment != null ? segment.ProfileName : "");
            string tendon = NormalizeDraftingLine(segment != null ? segment.TendonName : "");
            if (!string.IsNullOrWhiteSpace(profile) &&
                !string.IsNullOrWhiteSpace(tendon) &&
                !string.Equals(profile, tendon, StringComparison.OrdinalIgnoreCase))
            {
                return profile + " / " + tendon;
            }

            if (!string.IsNullOrWhiteSpace(tendon))
            {
                return tendon;
            }

            return profile;
        }

        private static IEnumerable<string> WrapLine(string text, double maxWidthFt)
        {
            string normalized = NormalizeDraftingLine(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                yield break;
            }

            if (maxWidthFt <= 1.0e-6 || EstimateTextWidthFt(normalized) <= maxWidthFt)
            {
                yield return normalized;
                yield break;
            }

            List<string> words = Regex.Split(normalized, @"\s+")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
            if (words.Count == 0)
            {
                yield break;
            }

            string currentLine = "";
            foreach (string word in words)
            {
                string candidate = string.IsNullOrWhiteSpace(currentLine)
                    ? word
                    : currentLine + " " + word;
                if (!string.IsNullOrWhiteSpace(currentLine) &&
                    EstimateTextWidthFt(candidate) > maxWidthFt)
                {
                    yield return currentLine;
                    currentLine = TrimTextToWidth(word, maxWidthFt);
                }
                else
                {
                    currentLine = TrimTextToWidth(candidate, maxWidthFt);
                }
            }

            if (!string.IsNullOrWhiteSpace(currentLine))
            {
                yield return currentLine;
            }
        }

        private static IEnumerable<XYZ> BuildLayoutCandidates(double preferredX, double preferredY, double rowGapFt)
        {
            yield return new XYZ(preferredX, preferredY, 0.0);

            for (int step = 1; step <= MaxLayoutSteps; step++)
            {
                double primaryOffsetFt = step * rowGapFt;
                yield return new XYZ(preferredX, preferredY + primaryOffsetFt, 0.0);
                yield return new XYZ(preferredX, preferredY - primaryOffsetFt, 0.0);

                double secondaryOffsetFt = (step - 0.5) * rowGapFt;
                yield return new XYZ(preferredX, preferredY + secondaryOffsetFt, 0.0);
                yield return new XYZ(preferredX, preferredY - secondaryOffsetFt, 0.0);
            }
        }

        private static bool CanPlaceCenter(
            XYZ candidate,
            IList<XYZ> placedCenters,
            double minimumSeparationFt,
            double horizontalInfluenceFt)
        {
            foreach (XYZ placed in placedCenters ?? Enumerable.Empty<XYZ>())
            {
                if (placed == null)
                {
                    continue;
                }

                if (Math.Abs(candidate.X - placed.X) > horizontalInfluenceFt)
                {
                    continue;
                }

                if (Math.Abs(candidate.Y - placed.Y) < minimumSeparationFt)
                {
                    return false;
                }
            }

            return true;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
