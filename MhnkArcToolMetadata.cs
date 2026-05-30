using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkArcToolMetadata
    {
        private MhnkArcToolMetadata()
        {
            SupportedSourceModes = new List<MhnkArcSourceMode>();
            RequiredInputs = new List<string>();
            PreviewColumns = new List<string>();
            ResultColumns = new List<string>();
        }

        public string ToolId { get; private set; }
        public string GuidelineId { get; private set; }
        public string SourceGroup { get; private set; }
        public string Risk { get; private set; }
        public string RequiredInputText { get; private set; }
        public string ExpectedResultText { get; private set; }
        public string PanelKind { get; private set; }
        public string LiveRetrieveScope { get; private set; }
        public string UndoStrategy { get; private set; }
        public bool SupportsLiveRetrieve { get; private set; }
        public bool RequiresPreviewBeforeRun { get; private set; }
        public MhnkArcSourceMode RecommendedSourceMode { get; private set; }
        public IList<MhnkArcSourceMode> SupportedSourceModes { get; private set; }
        public IList<string> RequiredInputs { get; private set; }
        public IList<string> PreviewColumns { get; private set; }
        public IList<string> ResultColumns { get; private set; }

        public bool SupportsSourceMode(MhnkArcSourceMode sourceMode)
        {
            return SupportedSourceModes.Contains(sourceMode);
        }

        public string SupportedSourceModeText
        {
            get
            {
                return string.Join(", ", SupportedSourceModes.Select(MhnkArcToolMetadataRules.GetSourceModeLabel).ToArray());
            }
        }

        public static MhnkArcToolMetadata Create(
            string category,
            string title,
            string summary,
            bool isReady,
            string status)
        {
            string safeCategory = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            string safeTitle = title ?? "";
            string text = (safeTitle + " " + (summary ?? "")).ToLowerInvariant();
            string sourceGroup = MhnkArcToolMetadataRules.GetSourceGroupName(safeCategory, safeTitle, summary);
            string risk = MhnkArcToolMetadataRules.InferRisk(safeCategory, safeTitle, summary);
            MhnkArcSourceMode recommended = MhnkArcToolMetadataRules.GetRecommendedSourceMode(safeCategory, safeTitle, summary);
            IList<MhnkArcSourceMode> supported = MhnkArcToolMetadataRules.GetSupportedSourceModes(safeCategory, safeTitle, summary, recommended);

            return new MhnkArcToolMetadata
            {
                ToolId = MhnkArcToolMetadataRules.BuildToolId(safeCategory, safeTitle),
                GuidelineId = MhnkArcToolMetadataRules.BuildGuidelineId(safeCategory, safeTitle),
                SourceGroup = sourceGroup,
                Risk = risk,
                RequiredInputText = MhnkArcToolMetadataRules.InferRequiredInput(safeTitle, summary, recommended),
                ExpectedResultText = MhnkArcToolMetadataRules.InferExpectedResult(safeTitle, summary),
                PanelKind = MhnkArcToolMetadataRules.InferPanelKind(safeCategory, safeTitle, summary),
                LiveRetrieveScope = MhnkArcToolMetadataRules.InferLiveRetrieveScope(safeCategory, safeTitle, summary),
                UndoStrategy = MhnkArcToolMetadataRules.InferUndoStrategy(safeCategory, safeTitle, summary),
                SupportsLiveRetrieve = isReady && !string.Equals(status, "Next", StringComparison.OrdinalIgnoreCase),
                RequiresPreviewBeforeRun = MhnkArcToolMetadataRules.RequiresPreview(safeCategory, safeTitle, summary),
                RecommendedSourceMode = supported.Contains(recommended) ? recommended : supported.FirstOrDefault(),
                SupportedSourceModes = supported,
                RequiredInputs = MhnkArcToolMetadataRules.BuildRequiredInputs(safeCategory, safeTitle, summary, recommended),
                PreviewColumns = MhnkArcToolMetadataRules.BuildPreviewColumns(safeCategory, safeTitle, summary),
                ResultColumns = MhnkArcToolMetadataRules.BuildResultColumns(safeCategory, safeTitle, summary)
            };
        }
    }

    internal static class MhnkArcToolMetadataRules
    {
        public const string SourceGroupAutoCad = "AutoCAD";
        public const string SourceGroupRevitCategory = "Revit Category";
        public const string SourceGroupSelectedItems = "Selected Items";
        public const string SourceGroupReviewSetup = "Review / Setup";
        public const string SourceGroupAllTools = "All Tools";

        public static readonly string[] SourceGroupOrder =
        {
            SourceGroupAutoCad,
            SourceGroupRevitCategory,
            SourceGroupSelectedItems,
            SourceGroupReviewSetup,
            SourceGroupAllTools
        };

        public static string GetSourceGroupName(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (IsAutoCadTool(text))
            {
                return SourceGroupAutoCad;
            }

            if (IsSelectionTool(category, text))
            {
                return SourceGroupSelectedItems;
            }

            if (IsRevitCategoryTool(category, text))
            {
                return SourceGroupRevitCategory;
            }

            if (IsReviewOrSetupTool(text))
            {
                return SourceGroupReviewSetup;
            }

            return SourceGroupRevitCategory;
        }

        public static string GetSourceGroupDescription(string sourceGroup)
        {
            if (string.Equals(sourceGroup, SourceGroupAutoCad, StringComparison.OrdinalIgnoreCase))
            {
                return "CAD imports, CAD layers, mapping rules, and curve-based creation.";
            }

            if (string.Equals(sourceGroup, SourceGroupRevitCategory, StringComparison.OrdinalIgnoreCase))
            {
                return "Rooms, walls, floors, ceilings, doors, windows, solids, and active-view categories.";
            }

            if (string.Equals(sourceGroup, SourceGroupSelectedItems, StringComparison.OrdinalIgnoreCase))
            {
                return "Commands that should run on a controlled Revit selection.";
            }

            if (string.Equals(sourceGroup, SourceGroupReviewSetup, StringComparison.OrdinalIgnoreCase))
            {
                return "QA, diagnostics, settings, validation, reports, and setup workflows.";
            }

            return "Every ARC tool, sorted by source group and tool family.";
        }

        public static string GetDefaultSourceGroupForCommandCategory(string category)
        {
            if (string.Equals(category, "Creation", StringComparison.OrdinalIgnoreCase))
            {
                return SourceGroupAutoCad;
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                return SourceGroupSelectedItems;
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                return SourceGroupReviewSetup;
            }

            if (string.Equals(category, "Filter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                return SourceGroupRevitCategory;
            }

            return SourceGroupOrder.First();
        }

        public static MhnkArcSourceMode GetRecommendedSourceMode(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (IsAutoCadTool(text))
            {
                return MhnkArcSourceMode.ByLayer;
            }

            if (string.Equals(category, "Creation", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("room") || text.Contains("finish")))
            {
                return MhnkArcSourceMode.Category;
            }

            if (IsSelectionTool(category, text))
            {
                return MhnkArcSourceMode.FreeSelect;
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("check") || text.Contains("color") || text.Contains("select")))
            {
                return MhnkArcSourceMode.Category;
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("report") || text.Contains("dashboard") || text.Contains("warnings"))
            {
                return MhnkArcSourceMode.All;
            }

            return MhnkArcSourceMode.FreeSelect;
        }

        public static IList<MhnkArcSourceMode> GetSupportedSourceModes(
            string category,
            string title,
            string summary,
            MhnkArcSourceMode recommended)
        {
            string text = GetToolText(title, summary);
            bool cadDriven = IsAutoCadTool(text);
            bool highRisk = IsHighRiskEdit(category, text);
            var modes = new List<MhnkArcSourceMode> { MhnkArcSourceMode.FreeSelect };

            if (cadDriven)
            {
                modes.Add(MhnkArcSourceMode.ByLayer);
            }

            if ((!cadDriven || text.Contains("select by cad layer")) && !highRisk)
            {
                modes.Add(MhnkArcSourceMode.Category);
            }

            if (!highRisk)
            {
                modes.Add(MhnkArcSourceMode.All);
            }

            if (!modes.Contains(recommended))
            {
                modes.Insert(0, recommended);
            }

            return modes.Distinct().ToList();
        }

        public static string InferRisk(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (text.Contains("delete") || text.Contains("clean") || text.Contains("cut") || text.Contains("uncut") ||
                text.Contains("join") || text.Contains("unjoin") || text.Contains("switch") || text.Contains("batch") ||
                text.Contains("split") || text.Contains("lower") || text.Contains("rename"))
            {
                return "High";
            }

            if (text.Contains("create") || text.Contains("place") || text.Contains("convert") || text.Contains("pin") ||
                text.Contains("unpin") || text.Contains("set ") || text.Contains("reset") || text.Contains("color") ||
                text.Contains("hide") || text.Contains("isolate"))
            {
                return "Medium";
            }

            return "Low";
        }

        public static string InferRequiredInput(string title, string summary, MhnkArcSourceMode sourceMode)
        {
            string text = GetToolText(title, summary);
            if (text.Contains("settings") || text.Contains("manager") || text.Contains("dashboard") || text.Contains("report"))
            {
                return "No strict model selection required; source mode still controls CAD scan defaults where applicable.";
            }

            if (sourceMode == MhnkArcSourceMode.FreeSelect)
            {
                return "Select Item: freely select only the Revit elements that should be used.";
            }

            if (sourceMode == MhnkArcSourceMode.Category)
            {
                return "Category: collect applicable elements in the active view, such as rooms, walls, floors, or solids.";
            }

            if (sourceMode == MhnkArcSourceMode.ByLayer)
            {
                return "By Layer: use selected/visible CAD imports and filter by ARC Tool Settings or Mapping Manager layers.";
            }

            if (sourceMode == MhnkArcSourceMode.All)
            {
                return "All: collect all visible active-view candidates, or all bounded rooms for room tools.";
            }

            return "Check the guideline before running on a production model.";
        }

        public static string InferExpectedResult(string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (text.Contains("report") || text.Contains("dashboard") || text.Contains("check"))
            {
                return "Review report, selected candidates, or a validation window.";
            }

            if (text.Contains("create") || text.Contains("place") || text.Contains("convert"))
            {
                return "New model elements or candidate elements may be created.";
            }

            if (text.Contains("join") || text.Contains("cut") || text.Contains("change") || text.Contains("reset") ||
                text.Contains("hide") || text.Contains("isolate"))
            {
                return "Existing model/view state may be changed. Review scope before running.";
            }

            return "The selected workflow runs inside the current Revit document.";
        }

        public static string InferPanelKind(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (IsAutoCadTool(text))
            {
                return "AutoCAD Layer Production Panel";
            }

            if (text.Contains("room"))
            {
                return "Room Boundary Production Panel";
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                return "Selected Element Edition Panel";
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                return "Solids Coordination Panel";
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                return "Review and Setup Panel";
            }

            return "Revit Category Production Panel";
        }

        public static string InferLiveRetrieveScope(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (IsAutoCadTool(text))
            {
                return "Visible CAD imports, CAD layers, mapped curves, model/detail lines, and skipped layer reasons.";
            }

            if (text.Contains("room"))
            {
                return "Bounded rooms, boundary loops, levels, target types, invalid loops, and skipped rooms.";
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                return "Current selection, element order, editable state, join/cut capability, and skipped elements.";
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                return "Selected elements, interacting categories, clash/opening candidates, pair status, and skipped geometry.";
            }

            return "Active-view candidates, selected elements, model warnings, required types, and expected output rows.";
        }

        public static string InferUndoStrategy(string category, string title, string summary)
        {
            string risk = InferRisk(category, title, summary);
            return risk == "Low"
                ? "Report or selection workflow; model writes are not expected unless the command says so."
                : "Run inside one MHNK transaction group where Revit permits; use Undo immediately if scope is wrong.";
        }

        public static bool RequiresPreview(string category, string title, string summary)
        {
            return InferRisk(category, title, summary) != "Low" ||
                   GetToolText(title, summary).Contains("create") ||
                   GetToolText(title, summary).Contains("place");
        }

        public static IList<string> BuildRequiredInputs(string category, string title, string summary, MhnkArcSourceMode recommended)
        {
            string text = GetToolText(title, summary);
            var inputs = new List<string> { GetSourceModeLabel(recommended) };
            if (IsAutoCadTool(text)) inputs.Add("CAD layer/mapping rule");
            if (text.Contains("room")) inputs.Add("Bounded rooms");
            if (text.Contains("wall")) inputs.Add("Wall/category candidates");
            if (text.Contains("floor")) inputs.Add("Floor/category candidates");
            if (text.Contains("ceiling")) inputs.Add("Ceiling/category candidates");
            if (IsSelectionTool(category, text)) inputs.Add("Controlled Revit selection");
            return inputs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IList<string> BuildPreviewColumns(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (IsAutoCadTool(text))
            {
                return new[] { "Source", "Layer", "Action", "Target Type", "Level", "Status", "Skip Reason" };
            }

            if (text.Contains("room"))
            {
                return new[] { "Room", "Level", "Boundary", "Target Type", "Offset", "Status", "Skip Reason" };
            }

            if (IsSelectionTool(category, text))
            {
                return new[] { "Order", "Element Id", "Category", "Type", "Editable", "Status", "Skip Reason" };
            }

            return new[] { "Element Id", "Category", "Family/Type", "Level", "Status", "Skip Reason" };
        }

        public static IList<string> BuildResultColumns(string category, string title, string summary)
        {
            string text = GetToolText(title, summary);
            if (text.Contains("report") || text.Contains("dashboard") || text.Contains("check"))
            {
                return new[] { "Check", "Status", "Count", "Evidence", "Action" };
            }

            if (text.Contains("create") || text.Contains("place") || text.Contains("convert"))
            {
                return new[] { "Created Id", "Category", "Type", "Level", "Result", "Warning" };
            }

            return new[] { "Element Id", "Before", "After", "Result", "Warning" };
        }

        public static string BuildToolId(string category, string title)
        {
            return Slug((category ?? "General") + "-" + (title ?? "Tool"));
        }

        public static string BuildGuidelineId(string category, string title)
        {
            return "guide-" + BuildToolId(category, title);
        }

        public static string GetSourceModeLabel(MhnkArcSourceMode mode)
        {
            switch (mode)
            {
                case MhnkArcSourceMode.Category:
                    return "Category";
                case MhnkArcSourceMode.ByLayer:
                    return "By Layer";
                case MhnkArcSourceMode.All:
                    return "All";
                case MhnkArcSourceMode.FreeSelect:
                default:
                    return "Select Item";
            }
        }

        public static int GetSourceGroupIndex(string sourceGroup)
        {
            for (int i = 0; i < SourceGroupOrder.Length; i++)
            {
                if (string.Equals(SourceGroupOrder[i], sourceGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return SourceGroupOrder.Length;
        }

        private static bool IsAutoCadTool(string text)
        {
            text = text ?? "";
            return text.Contains("cad") ||
                   text.Contains("layer") ||
                   text.Contains("mapping") ||
                   text.Contains("marker");
        }

        private static bool IsSelectionTool(string category, string text)
        {
            text = text ?? "";
            return string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("selected") ||
                   text.Contains("selection") ||
                   text.Contains("join") ||
                   text.Contains("unjoin") ||
                   text.Contains("cut") ||
                   text.Contains("uncut") ||
                   text.Contains("pin") ||
                   text.Contains("align") ||
                   text.Contains("rename") ||
                   text.Contains("group from selection");
        }

        private static bool IsRevitCategoryTool(string category, string text)
        {
            text = text ?? "";
            return string.Equals(category, "Filter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("category") ||
                   text.Contains("room") ||
                   text.Contains("wall") ||
                   text.Contains("floor") ||
                   text.Contains("ceiling") ||
                   text.Contains("door") ||
                   text.Contains("window") ||
                   text.Contains("mep") ||
                   text.Contains("opening") ||
                   text.Contains("solid") ||
                   text.Contains("view");
        }

        private static bool IsReviewOrSetupTool(string text)
        {
            text = text ?? "";
            return text.Contains("settings") ||
                   text.Contains("dashboard") ||
                   text.Contains("report") ||
                   text.Contains("validation") ||
                   text.Contains("diagnostics") ||
                   text.Contains("health") ||
                   text.Contains("warnings") ||
                   text.Contains("missing") ||
                   text.Contains("prepare") ||
                   text.Contains("theme");
        }

        private static bool IsHighRiskEdit(string category, string text)
        {
            return string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("cut") || text.Contains("uncut") || text.Contains("join") ||
                   text.Contains("unjoin") || text.Contains("delete") || text.Contains("rename");
        }

        private static string GetToolText(string title, string summary)
        {
            return ((title ?? "") + " " + (summary ?? "")).ToLowerInvariant();
        }

        private static string Slug(string value)
        {
            var chars = new List<char>();
            bool previousDash = false;
            foreach (char c in (value ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars.Add(c);
                    previousDash = false;
                }
                else if (!previousDash)
                {
                    chars.Add('-');
                    previousDash = true;
                }
            }

            string slug = new string(chars.ToArray()).Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "arc-tool" : slug;
        }
    }
}
