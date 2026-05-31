using System.Text.RegularExpressions;

namespace CamboBIM.Revit2024.Addin
{
    internal static class ExtensionTextUtility
    {
        private static readonly Regex MultiWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex NonSheetPrefixCharsRegex = new Regex(@"[^A-Z0-9_-]+", RegexOptions.Compiled);
        private static readonly Regex NonAdaptLayerCharsRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex NonShopMarkPrefixCharsRegex = new Regex(@"[^A-Za-z0-9_\-]+", RegexOptions.Compiled);

        public static string NormalizeWhitespace(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : MultiWhitespaceRegex.Replace(value, " ").Trim();
        }

        public static string TruncateWithEllipsis(string value, int maxLength)
        {
            string text = value ?? "";
            if (maxLength <= 3 || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength - 3) + "...";
        }

        public static string NormalizeShopDrawingViewportText(string value, int maxLength)
        {
            return TruncateWithEllipsis(NormalizeWhitespace(value), maxLength);
        }

        public static string NormalizeShopDrawingIssueText(string value, string fallback, int maxLength)
        {
            string text = DefaultText(value, fallback);
            text = NormalizeWhitespace(text);
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
            }

            return string.IsNullOrWhiteSpace(text) ? (fallback ?? "") : text;
        }

        public static string NormalizeMetadataText(string value, string fallback, int maxLength)
        {
            string text = DefaultText(value, fallback);
            text = NormalizeWhitespace(text);
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
            }

            return string.IsNullOrWhiteSpace(text) ? (fallback ?? "") : text;
        }

        public static string NormalizeSheetPrefix(string value, string fallback, int maxLength)
        {
            string text = DefaultText(value, fallback).ToUpperInvariant();
            text = NonSheetPrefixCharsRegex.Replace(text, "-").Trim('-', '_');
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).Trim('-', '_');
            }

            return string.IsNullOrWhiteSpace(text) ? (fallback ?? "") : text;
        }

        public static string NormalizeAdaptLayerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return NonAdaptLayerCharsRegex.Replace(value.ToLowerInvariant(), "");
        }

        public static string NormalizeAdaptCadChainMark(string value, int maxLength)
        {
            string text = NormalizeWhitespace(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).Trim();
            }

            return text;
        }

        public static string NormalizeAdaptShopMarkPrefix(string prefix, string fallback)
        {
            string text = NonShopMarkPrefixCharsRegex.Replace((prefix ?? "").Trim(), "");
            return string.IsNullOrWhiteSpace(text) ? (fallback ?? "") : text;
        }

        public static string NormalizeOcrText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            string normalized = text
                .Trim()
                .Replace("（", "(")
                .Replace("）", ")")
                .Replace("×", "x")
                .Replace("X", "x")
                .Replace("*", "x");

            normalized = Regex.Replace(normalized, @"(?<=\d)[Oo](?=\d)", "0");
            normalized = Regex.Replace(normalized, @"(?<=\d)[Oo](?=\s*(?:x|\)|m|$))", "0");
            normalized = Regex.Replace(normalized, @"(?<=(?:x|\())\s*[Oo](?=\d)", "0");
            normalized = Regex.Replace(normalized, @"(?<=\d)[Cc](?=\d|x|\)|m|$)", "0");
            normalized = Regex.Replace(normalized, @"(?<=(?:x|\())\s*[Cc](?=\d)", "0");
            normalized = Regex.Replace(normalized, @"(?<=[A-Za-z])(?:[Il\|])(?=\b)", "1");
            return normalized;
        }

        public static string CleanScheduleName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            string cleaned = text.Trim();
            int paren = cleaned.IndexOf('(');
            if (paren > 0)
            {
                cleaned = cleaned.Substring(0, paren);
            }

            return cleaned.Replace(" ", "");
        }

        private static string DefaultText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? (fallback ?? "") : value.Trim();
        }
    }
}
