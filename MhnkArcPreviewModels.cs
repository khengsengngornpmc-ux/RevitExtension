using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamboBIM.Revit2024.Addin
{
    internal enum MhnkArcPreviewRowKind
    {
        Source,
        Target,
        Warning
    }

    internal sealed class MhnkArcPreviewResult
    {
        public MhnkArcPreviewResult()
        {
            SourceRows = new List<MhnkArcPreviewRow>();
            TargetRows = new List<MhnkArcPreviewRow>();
            Warnings = new List<string>();
            RetrievedAtUtc = DateTime.UtcNow;
        }

        public bool HasData { get; set; }
        public string ToolId { get; set; }
        public string ToolTitle { get; set; }
        public string PanelKind { get; set; }
        public MhnkArcSourceMode SourceMode { get; set; }
        public string SourceModeName { get; set; }
        public string ModelLabel { get; set; }
        public string Summary { get; set; }
        public DateTime RetrievedAtUtc { get; set; }
        public IList<MhnkArcPreviewRow> SourceRows { get; }
        public IList<MhnkArcPreviewRow> TargetRows { get; }
        public IList<string> Warnings { get; }

        public int ReadyCount
        {
            get
            {
                return SourceRows.Concat(TargetRows).Count(x => IsReadyStatus(x.Status));
            }
        }

        public int SkippedCount
        {
            get
            {
                return SourceRows.Concat(TargetRows).Count(x => IsSkippedStatus(x.Status));
            }
        }

        public string RetrievedAtText
        {
            get
            {
                return RetrievedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        public bool Matches(MhnkArcCommandOption option, MhnkArcSourceMode sourceMode)
        {
            string optionToolId = option?.Metadata?.ToolId ?? "";
            return HasData &&
                   string.Equals(ToolId ?? "", optionToolId, StringComparison.OrdinalIgnoreCase) &&
                   SourceMode == sourceMode;
        }

        public void AddWarning(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Warnings.Add(message.Trim());
            }
        }

        private static bool IsReadyStatus(string status)
        {
            string value = (status ?? "").Trim();
            return string.Equals(value, "Ready", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Selected", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Mapped", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Checked", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Live", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSkippedStatus(string status)
        {
            string value = (status ?? "").Trim();
            return string.Equals(value, "Skip", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Skipped", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Empty", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Blocked", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class MhnkArcPreviewRow
    {
        public MhnkArcPreviewRow()
        {
            Include = true;
            Kind = MhnkArcPreviewRowKind.Source;
            Item = "";
            Source = "";
            ElementIdText = "";
            Category = "";
            Layer = "";
            TargetType = "";
            Level = "";
            Status = "";
            SkipReason = "";
            Detail = "";
        }

        public bool Include { get; set; }
        public MhnkArcPreviewRowKind Kind { get; set; }
        public string Item { get; set; }
        public string Source { get; set; }
        public string ElementIdText { get; set; }
        public string Category { get; set; }
        public string Layer { get; set; }
        public string TargetType { get; set; }
        public string Level { get; set; }
        public string Status { get; set; }
        public string SkipReason { get; set; }
        public string Detail { get; set; }

        public string ModeText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Layer))
                {
                    return Layer;
                }

                if (!string.IsNullOrWhiteSpace(Category))
                {
                    return Category;
                }

                return Source;
            }
        }

        public string DetailText
        {
            get
            {
                var parts = new List<string>();
                AddPart(parts, Detail);
                AddPart(parts, TargetType);
                AddPart(parts, Level);
                AddPart(parts, SkipReason);
                AddPart(parts, ElementIdText);
                return string.Join("; ", parts.ToArray());
            }
        }

        private static void AddPart(IList<string> parts, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value.Trim());
            }
        }
    }
}
