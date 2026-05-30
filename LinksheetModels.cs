using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal enum LinksheetParameterKind
    {
        Instance,
        Type,
        ReadOnly
    }

    internal static class LinksheetFieldKeys
    {
        public const string Guid = "__GUID__";
        public const string ElementId = "__ELEMENT_ID__";
    }

    internal sealed class LinksheetScheduleInfo
    {
        public string Name { get; set; } = "";
        public int ElementCount { get; set; }
    }

    internal sealed class LinksheetParameterInfo
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public LinksheetParameterKind Kind { get; set; } = LinksheetParameterKind.ReadOnly;
    }

    internal sealed class LinksheetPreviewRow
    {
        public long ElementId { get; set; }
        public string UniqueId { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    internal sealed class LinksheetSchedulePreview
    {
        public string ScheduleName { get; set; } = "";
        public List<string> ColumnKeys { get; set; } = new List<string>();
        public Dictionary<string, string> ColumnHeaders { get; set; } = new Dictionary<string, string>();
        public List<LinksheetPreviewRow> Rows { get; set; } = new List<LinksheetPreviewRow>();
    }

    internal sealed class LinksheetCellEdit
    {
        public string ScheduleName { get; set; } = "";
        public long ElementId { get; set; }
        public string UniqueId { get; set; } = "";
        public string ParameterKey { get; set; } = "";
        public LinksheetParameterKind Kind { get; set; } = LinksheetParameterKind.Instance;
        public string Value { get; set; } = "";
    }
}
