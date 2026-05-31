using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CamboBIM.Revit2024.Addin
{
    internal static class MhnkArcPreviewService
    {
        private const int MaxPreviewRows = 80;
        private const double MinimumRoomArea = 1e-6;

        public static MhnkArcPreviewResult Retrieve(MhnkArcContext context)
        {
            MhnkArcCommandOption option = context?.CommandOption;
            var result = new MhnkArcPreviewResult
            {
                HasData = context?.Document != null,
                ToolId = option?.Metadata?.ToolId ?? "",
                ToolTitle = option?.Title ?? "",
                PanelKind = option?.Metadata?.PanelKind ?? "",
                SourceMode = context?.SourceMode ?? MhnkArcSourceMode.FreeSelect,
                SourceModeName = context?.SourceModeName ?? "",
                ModelLabel = GetModelLabel(context),
                Summary = "No Revit model is available for live preview."
            };

            if (!result.HasData)
            {
                result.AddWarning("Open a Revit model before retrieving tool candidates.");
                return result;
            }

            try
            {
                string text = GetToolText(option);
                AddModelContextRows(context, result);

                if (IsCadTool(text) || result.SourceMode == MhnkArcSourceMode.ByLayer)
                {
                    BuildCadPreview(context, option, result, text);
                }
                else if (IsRoomTool(text))
                {
                    BuildRoomPreview(context, option, result, text);
                }
                else if (IsSelectionTool(option?.Category, text))
                {
                    BuildSelectionPreview(context, option, result, text);
                }
                else if (string.Equals(option?.Category, "Solids", StringComparison.OrdinalIgnoreCase))
                {
                    BuildSolidsPreview(context, option, result, text);
                }
                else if (string.Equals(option?.Category, "Xpress", StringComparison.OrdinalIgnoreCase) || IsReviewTool(text))
                {
                    BuildReviewPreview(context, option, result, text);
                }
                else
                {
                    BuildCategoryPreview(context, option, result, text);
                }

                AddPreviewFooterRows(context, option, result, text);
            }
            catch (Exception ex)
            {
                result.Summary = "Live retrieve completed with a warning.";
                result.AddWarning(ex.Message);
            }

            return result;
        }

        private static void AddModelContextRows(MhnkArcContext context, MhnkArcPreviewResult result)
        {
            result.SourceRows.Add(new MhnkArcPreviewRow
            {
                Kind = MhnkArcPreviewRowKind.Source,
                Item = "Open model",
                Source = "Active",
                Category = "Document",
                Status = "Live",
                Detail = result.ModelLabel
            });

            int selectionCount = SafeCount(() => context.UiDocument.Selection.GetElementIds().Count);
            int visibleCount = SafeCount(() => new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .GetElementCount());
            result.SourceRows.Add(new MhnkArcPreviewRow
            {
                Kind = MhnkArcPreviewRowKind.Source,
                Item = "Selection scope",
                Source = result.SourceModeName,
                Category = "Active view",
                Status = selectionCount > 0 ? "Ready" : "Empty",
                Detail = selectionCount.ToString(CultureInfo.InvariantCulture) + " selected / " +
                         visibleCount.ToString(CultureInfo.InvariantCulture) + " visible element(s)"
            });
        }

        private static void BuildCadPreview(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            IList<MhnkArcSmartMappingRule> rules = MhnkArcSmartMappingRules.Load().Rules;
            IList<ImportInstance> imports = GetScopedCadImports(context);
            IList<CurveElement> curves = GetScopedCurveElements(context);

            result.Summary = "CAD retrieve found " + imports.Count.ToString(CultureInfo.InvariantCulture) +
                             " import(s) and " + curves.Count.ToString(CultureInfo.InvariantCulture) +
                             " model/detail curve source(s).";

            if (imports.Count == 0 && curves.Count == 0)
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "CAD source",
                    "By Layer",
                    "",
                    "",
                    "",
                    "",
                    "Skip",
                    "Select CAD imports or use a view that contains CAD imports/model lines.",
                    ""));
            }

            int layerRows = 0;
            foreach (ImportInstance import in imports.Take(20))
            {
                IList<string> layers = GetImportLayerNames(import);
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "CAD import",
                    "ImportInstance",
                    GetElementIdText(import.Id),
                    SafeCategoryName(import),
                    "",
                    "",
                    layers.Count > 0 ? "Ready" : "Skip",
                    import.Name + " / " + layers.Count.ToString(CultureInfo.InvariantCulture) + " layer(s)",
                    layers.Count > 0 ? "" : "No CAD layer categories detected."));

                foreach (string layer in layers.Take(Math.Max(0, MaxPreviewRows - layerRows)))
                {
                    layerRows++;
                    MhnkArcSmartMappingRule rule = FindMappingRule(layer, rules);
                    string action = ResolveCadAction(layer, rule, settings);
                    bool accepted = IsCadActionAcceptedByTool(text, action);
                    string status = accepted && !string.Equals(action, "Unmapped", StringComparison.OrdinalIgnoreCase) ? "Mapped" : "Skip";
                    string skip = status == "Mapped" ? "" : GetCadSkipReason(action, text);
                    result.SourceRows.Add(Row(
                        MhnkArcPreviewRowKind.Source,
                        "CAD layer",
                        "Layer",
                        GetElementIdText(import.Id),
                        "CAD",
                        layer,
                        ResolveCadTargetType(context.Document, action, rule, settings),
                        status,
                        GetRuleLabel(rule, action),
                        skip));
                }
            }

            foreach (CurveElement curveElement in curves.Take(30))
            {
                string layer = GetCurveLayerName(curveElement);
                MhnkArcSmartMappingRule rule = FindMappingRule(layer, rules);
                string action = ResolveCadAction(layer, rule, settings);
                bool accepted = IsCadActionAcceptedByTool(text, action);
                string status = accepted ? "Ready" : "Skip";
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Curve source",
                    "Model/Detail",
                    GetElementIdText(curveElement.Id),
                    SafeCategoryName(curveElement),
                    layer,
                    ResolveCadTargetType(context.Document, action, rule, settings),
                    status,
                    GetCurveDetail(curveElement),
                    status == "Ready" ? "" : GetCadSkipReason(action, text)));
            }

            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Mapping rules",
                "Settings",
                "",
                "AutoCAD",
                "",
                "",
                rules.Any(x => x.Enabled) ? "Ready" : "Skip",
                "Active mapping rules: " + rules.Count(x => x.Enabled).ToString(CultureInfo.InvariantCulture),
                rules.Any(x => x.Enabled) ? "" : "No enabled mapping rules."));
            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Wall height",
                "Settings",
                "",
                "Wall",
                "",
                settings.WallHeightMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m",
                "Ready",
                "Default unconnected wall height from ARC Tool Settings.",
                ""));
        }

        private static void BuildRoomPreview(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            IList<Room> rooms = GetScopedRooms(context);
            string target = GetRoomTargetType(text);
            result.Summary = "Room retrieve found " + rooms.Count.ToString(CultureInfo.InvariantCulture) +
                             " bounded room candidate(s) for " + target + ".";

            if (rooms.Count == 0)
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Rooms",
                    context.SourceModeName,
                    "",
                    "Rooms",
                    "",
                    target,
                    "Skip",
                    "No bounded rooms were found for the current source mode.",
                    "Select rooms, use Category, or switch to All."));
            }

            foreach (Room room in rooms.Take(MaxPreviewRows))
            {
                IList<IList<BoundarySegment>> boundaryGroups = GetBoundaryGroups(room);
                int segmentCount = boundaryGroups.Sum(x => x?.Count ?? 0);
                bool ready = SafeRoomArea(room) > MinimumRoomArea && segmentCount > 0;
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Room",
                    context.SourceModeName,
                    GetElementIdText(room.Id),
                    SafeCategoryName(room),
                    "",
                    target,
                    ready ? "Ready" : "Skip",
                    GetRoomLabel(room) + " / loops " + boundaryGroups.Count.ToString(CultureInfo.InvariantCulture) +
                    " / segments " + segmentCount.ToString(CultureInfo.InvariantCulture) +
                    " / area " + FormatAreaSquareMeters(SafeRoomArea(room)),
                    ready ? "" : "Room is unplaced, unbounded, or has no usable boundary."));
            }

            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Target type",
                "Revit type",
                "",
                target,
                "",
                ResolveRoomTargetType(context.Document, text),
                "Ready",
                "Type availability is checked before the options window writes model elements.",
                ""));
            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Boundary review",
                "Preview",
                "",
                "Room boundary",
                "",
                target,
                "Required",
                "Invalid/open loops and skipped rooms must be reviewed before model creation.",
                ""));
        }

        private static void BuildSelectionPreview(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            IList<Element> selected = GetSelectedElements(context);
            result.Summary = "Selection retrieve found " + selected.Count.ToString(CultureInfo.InvariantCulture) + " selected element(s).";

            if (selected.Count == 0)
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Selected item",
                    context.SourceModeName,
                    "",
                    "",
                    "",
                    "",
                    "Skip",
                    "No selected Revit elements.",
                    "Select the source elements before running this tool."));
            }

            int order = 1;
            foreach (Element element in selected.Take(MaxPreviewRows))
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Selected " + order.ToString(CultureInfo.InvariantCulture),
                    "Select Item",
                    GetElementIdText(element.Id),
                    SafeCategoryName(element),
                    "",
                    SafeTypeName(context.Document, element),
                    CanEditElement(element) ? "Selected" : "Skip",
                    SafeElementName(element) + " / level " + GetLevelName(context.Document, element),
                    CanEditElement(element) ? "" : "Element is not editable or is read-only."));
                order++;
            }

            if (text.Contains("category") && selected.Count > 0)
            {
                Element seed = selected.First();
                int matchCount = CountVisibleElementsByCategory(context, seed.Category);
                result.TargetRows.Add(Row(
                    MhnkArcPreviewRowKind.Target,
                    "Category candidates",
                    "Active view",
                    "",
                    SafeCategoryName(seed),
                    "",
                    SafeCategoryName(seed),
                    matchCount > 0 ? "Ready" : "Empty",
                    matchCount.ToString(CultureInfo.InvariantCulture) + " visible element(s) match the seed category.",
                    ""));
            }

            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Undo",
                "Transaction",
                "",
                option?.Category ?? "",
                "",
                option?.Metadata?.UndoStrategy ?? "",
                "Ready",
                "Run uses the command runtime preflight and report evidence.",
                ""));
        }

        private static void BuildSolidsPreview(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            IList<Element> scoped = GetSelectedElements(context);
            if (scoped.Count == 0 && context.SourceMode != MhnkArcSourceMode.FreeSelect)
            {
                scoped = GetVisibleCategoryElements(context, GetSolidsCategories(), MaxPreviewRows);
            }

            result.Summary = "Solids retrieve found " + scoped.Count.ToString(CultureInfo.InvariantCulture) +
                             " selected/visible coordination candidate(s).";

            foreach (Element element in scoped.Take(MaxPreviewRows))
            {
                BoundingBoxXYZ box = SafeBoundingBox(element, context.ActiveView);
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Solid candidate",
                    context.SourceModeName,
                    GetElementIdText(element.Id),
                    SafeCategoryName(element),
                    "",
                    SafeTypeName(context.Document, element),
                    box != null ? "Checked" : "Skip",
                    SafeElementName(element) + " / " + GetBoundingBoxText(box),
                    box != null ? "" : "No active-view bounding box was available."));
            }

            if (scoped.Count == 0)
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Solid scope",
                    context.SourceModeName,
                    "",
                    "Solids",
                    "",
                    "",
                    "Empty",
                    "No selected or visible solid-capable candidates were found.",
                    "Select model elements or use Category/All in a coordination view."));
            }

            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Clash tolerance",
                "Settings",
                "",
                "Solids",
                "",
                MhnkArcToolSettings.Load().ClashToleranceMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm",
                "Ready",
                "Tolerance used by solid clash/opening checks.",
                ""));
        }

        private static void BuildReviewPreview(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            int warnings = SafeCount(() => context.Document.GetWarnings().Count);
            int cadImports = SafeCount(() => new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .GetElementCount());
            int rooms = SafeCount(() => new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Count(IsUsableRoom));

            result.Summary = "Review retrieve captured model-health metrics for the active ARC view.";
            result.SourceRows.Add(Row(MhnkArcPreviewRowKind.Source, "Warnings", "Model health", "", "Document", "", "", warnings == 0 ? "Ready" : "Review", warnings.ToString(CultureInfo.InvariantCulture) + " warning(s)", ""));
            result.SourceRows.Add(Row(MhnkArcPreviewRowKind.Source, "CAD imports", "Active view", "", "AutoCAD", "", "", cadImports > 0 ? "Ready" : "Empty", cadImports.ToString(CultureInfo.InvariantCulture) + " visible CAD import(s)", ""));
            result.SourceRows.Add(Row(MhnkArcPreviewRowKind.Source, "Bounded rooms", "Active view", "", "Rooms", "", "", rooms > 0 ? "Ready" : "Empty", rooms.ToString(CultureInfo.InvariantCulture) + " visible bounded room(s)", ""));

            foreach (MhnkArcWorkspaceCategoryCount category in CaptureTopCategories(context.Document, context.ActiveView).Take(8))
            {
                result.TargetRows.Add(Row(
                    MhnkArcPreviewRowKind.Target,
                    "Category",
                    "Active view",
                    "",
                    category.Name,
                    "",
                    "",
                    "Checked",
                    category.Count.ToString(CultureInfo.InvariantCulture) + " visible element(s)",
                    ""));
            }
        }

        private static void BuildCategoryPreview(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            IList<BuiltInCategory> categories = InferBuiltInCategories(text);
            IList<Element> elements = categories.Count == 0
                ? GetVisibleElements(context, MaxPreviewRows)
                : GetVisibleCategoryElements(context, categories, MaxPreviewRows);

            result.Summary = "Category retrieve found " + elements.Count.ToString(CultureInfo.InvariantCulture) +
                             " active-view candidate(s).";

            foreach (Element element in elements.Take(MaxPreviewRows))
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Category item",
                    "Category",
                    GetElementIdText(element.Id),
                    SafeCategoryName(element),
                    "",
                    SafeTypeName(context.Document, element),
                    "Ready",
                    SafeElementName(element) + " / level " + GetLevelName(context.Document, element),
                    ""));
            }

            if (elements.Count == 0)
            {
                result.SourceRows.Add(Row(
                    MhnkArcPreviewRowKind.Source,
                    "Category scope",
                    "Category",
                    "",
                    string.Join(", ", categories.Select(x => GetCategoryName(context.Document, x)).ToArray()),
                    "",
                    "",
                    "Empty",
                    "No active-view candidates were found for this tool.",
                    "Change view, select elements, or use All where supported."));
            }
        }

        private static void AddPreviewFooterRows(
            MhnkArcContext context,
            MhnkArcCommandOption option,
            MhnkArcPreviewResult result,
            string text)
        {
            result.TargetRows.Add(Row(
                MhnkArcPreviewRowKind.Target,
                "Preview contract",
                option?.Metadata?.PanelKind ?? "Production panel",
                "",
                option?.Category ?? "",
                "",
                option?.Metadata?.ToolId ?? "",
                "Live",
                "Ready " + result.ReadyCount.ToString(CultureInfo.InvariantCulture) +
                " / skipped " + result.SkippedCount.ToString(CultureInfo.InvariantCulture) +
                " / retrieved " + result.RetrievedAtText,
                ""));

            foreach (string warning in result.Warnings.Take(4))
            {
                result.TargetRows.Add(Row(
                    MhnkArcPreviewRowKind.Warning,
                    "Warning",
                    "Retrieve",
                    "",
                    "",
                    "",
                    "",
                    "Review",
                    warning,
                    ""));
            }
        }

        private static IList<ImportInstance> GetScopedCadImports(MhnkArcContext context)
        {
            IList<ImportInstance> selected = GetSelectedElements(context).OfType<ImportInstance>().ToList();
            if ((context.SourceMode == MhnkArcSourceMode.FreeSelect || context.SourceMode == MhnkArcSourceMode.ByLayer) &&
                selected.Count > 0)
            {
                return selected;
            }

            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .OfType<ImportInstance>()
                .ToList();
        }

        private static IList<CurveElement> GetScopedCurveElements(MhnkArcContext context)
        {
            IList<CurveElement> selected = GetSelectedElements(context).OfType<CurveElement>().ToList();
            if (context.SourceMode == MhnkArcSourceMode.FreeSelect && selected.Count > 0)
            {
                return selected;
            }

            if (context.SourceMode == MhnkArcSourceMode.ByLayer && selected.Count > 0)
            {
                return selected;
            }

            if (context.SourceMode == MhnkArcSourceMode.FreeSelect)
            {
                return selected;
            }

            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .OfType<CurveElement>()
                .Take(MaxPreviewRows)
                .ToList();
        }

        private static IList<Room> GetScopedRooms(MhnkArcContext context)
        {
            IList<Room> selected = GetSelectedElements(context)
                .OfType<Room>()
                .Where(IsUsableRoom)
                .OrderBy(x => GetLevelName(context.Document, x))
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Name)
                .ToList();

            if (context.SourceMode == MhnkArcSourceMode.FreeSelect)
            {
                return selected;
            }

            FilteredElementCollector collector = context.SourceMode == MhnkArcSourceMode.All
                ? new FilteredElementCollector(context.Document)
                : new FilteredElementCollector(context.Document, context.ActiveView.Id);

            return collector
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(IsUsableRoom)
                .OrderBy(x => GetLevelName(context.Document, x))
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Name)
                .Take(MaxPreviewRows)
                .ToList();
        }

        private static IList<Element> GetSelectedElements(MhnkArcContext context)
        {
            try
            {
                return context.UiDocument.Selection.GetElementIds()
                    .Select(id => context.Document.GetElement(id))
                    .Where(x => x != null)
                    .ToList();
            }
            catch
            {
                return new List<Element>();
            }
        }

        private static IList<Element> GetVisibleElements(MhnkArcContext context, int maximum)
        {
            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Take(Math.Max(1, maximum))
                .ToList();
        }

        private static IList<Element> GetVisibleCategoryElements(
            MhnkArcContext context,
            IList<BuiltInCategory> categories,
            int maximum)
        {
            var elements = new List<Element>();
            foreach (BuiltInCategory category in categories ?? new List<BuiltInCategory>())
            {
                try
                {
                    foreach (Element element in new FilteredElementCollector(context.Document, context.ActiveView.Id)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .Take(Math.Max(1, maximum - elements.Count)))
                    {
                        elements.Add(element);
                        if (elements.Count >= maximum)
                        {
                            return elements;
                        }
                    }
                }
                catch
                {
                }
            }

            return elements;
        }

        private static IList<string> GetImportLayerNames(ImportInstance import)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (import?.Category?.SubCategories != null)
                {
                    foreach (Category subCategory in import.Category.SubCategories)
                    {
                        if (!string.IsNullOrWhiteSpace(subCategory?.Name))
                        {
                            names.Add(subCategory.Name);
                        }
                    }
                }
            }
            catch
            {
            }

            return names.ToList();
        }

        private static MhnkArcSmartMappingRule FindMappingRule(string layerName, IList<MhnkArcSmartMappingRule> rules)
        {
            foreach (MhnkArcSmartMappingRule rule in rules ?? new List<MhnkArcSmartMappingRule>())
            {
                if (rule == null || !rule.Enabled)
                {
                    continue;
                }

                if (LayerMatches(layerName, MhnkArcSmartMappingRules.SplitKeywords(rule.LayerKeywords)))
                {
                    return rule;
                }
            }

            return null;
        }

        private static string ResolveCadAction(
            string layerName,
            MhnkArcSmartMappingRule rule,
            MhnkArcToolSettings settings)
        {
            if (rule != null)
            {
                return MhnkArcSmartMappingRules.NormalizeAction(rule.Action);
            }

            if (LayerMatches(layerName, settings.SplitKeywords(settings.WallLayerKeywords))) return "Wall";
            if (LayerMatches(layerName, settings.SplitKeywords(settings.FloorLayerKeywords))) return "Floor";
            if (LayerMatches(layerName, settings.SplitKeywords(settings.CeilingLayerKeywords))) return "Ceiling";
            if (LayerMatches(layerName, settings.SplitKeywords(settings.RoomBoundaryLayerKeywords))) return "RoomBoundary";
            if (LayerMatches(layerName, settings.SplitKeywords(settings.OpeningLayerKeywords))) return "Opening";
            if (LayerMatches(layerName, settings.SplitKeywords(settings.DoorLayerKeywords))) return "Door";
            if (LayerMatches(layerName, settings.SplitKeywords(settings.WindowLayerKeywords))) return "Window";
            return "Unmapped";
        }

        private static bool IsCadActionAcceptedByTool(string text, string action)
        {
            text = text ?? "";
            action = action ?? "";
            if (text.Contains("manager") || text.Contains("cad to model"))
            {
                return !string.Equals(action, "Unmapped", StringComparison.OrdinalIgnoreCase);
            }

            if (text.Contains("wall")) return string.Equals(action, "Wall", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("floor")) return string.Equals(action, "Floor", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("ceiling")) return string.Equals(action, "Ceiling", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("room boundaries")) return string.Equals(action, "RoomBoundary", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("room")) return string.Equals(action, "Room", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("opening")) return string.Equals(action, "Opening", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("door") || text.Contains("window"))
            {
                return string.Equals(action, "Door", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(action, "Window", StringComparison.OrdinalIgnoreCase);
            }

            return !string.Equals(action, "Unmapped", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCadSkipReason(string action, string text)
        {
            if (string.Equals(action, "Unmapped", StringComparison.OrdinalIgnoreCase))
            {
                return "Layer is not mapped in Mapping Manager or ARC Tool Settings.";
            }

            return "Mapped action " + action + " does not match this tool.";
        }

        private static string ResolveCadTargetType(
            Document document,
            string action,
            MhnkArcSmartMappingRule rule,
            MhnkArcToolSettings settings)
        {
            IList<string> keywords = rule == null || string.IsNullOrWhiteSpace(rule.RevitTypeKeywords)
                ? new List<string>()
                : MhnkArcSmartMappingRules.SplitKeywords(rule.RevitTypeKeywords);

            if (string.Equals(action, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                return FindElementTypeName<WallType>(document, keywords.Count > 0 ? keywords : settings.SplitKeywords(settings.PreferredWallTypeKeywords));
            }

            if (string.Equals(action, "Floor", StringComparison.OrdinalIgnoreCase))
            {
                return FindElementTypeName<FloorType>(document, keywords);
            }

            if (string.Equals(action, "Ceiling", StringComparison.OrdinalIgnoreCase))
            {
                return FindElementTypeName<CeilingType>(document, keywords);
            }

            if (string.Equals(action, "Door", StringComparison.OrdinalIgnoreCase))
            {
                return FindFamilySymbolName(document, BuiltInCategory.OST_Doors, keywords.Count > 0 ? keywords : settings.SplitKeywords(settings.DoorTypeKeywords));
            }

            if (string.Equals(action, "Window", StringComparison.OrdinalIgnoreCase))
            {
                return FindFamilySymbolName(document, BuiltInCategory.OST_Windows, keywords.Count > 0 ? keywords : settings.SplitKeywords(settings.WindowTypeKeywords));
            }

            if (string.Equals(action, "Opening", StringComparison.OrdinalIgnoreCase))
            {
                double depth = rule != null && rule.DepthMeters > 0.0 ? rule.DepthMeters : settings.OpeningDepthMeters;
                return "Opening depth " + depth.ToString("0.###", CultureInfo.InvariantCulture) + " m";
            }

            if (string.Equals(action, "RoomBoundary", StringComparison.OrdinalIgnoreCase))
            {
                return "Room separation line";
            }

            if (string.Equals(action, "Room", StringComparison.OrdinalIgnoreCase))
            {
                return "Room placement";
            }

            return "Unmapped";
        }

        private static string FindElementTypeName<T>(Document document, IList<string> keywords) where T : ElementType
        {
            try
            {
                IList<T> types = new FilteredElementCollector(document)
                    .OfClass(typeof(T))
                    .Cast<T>()
                    .OrderBy(x => x.Name)
                    .ToList();

                T match = FindNameMatch(types, keywords);
                return match == null ? (types.FirstOrDefault()?.Name ?? "Missing type") : match.Name;
            }
            catch
            {
                return "Type lookup failed";
            }
        }

        private static T FindNameMatch<T>(IList<T> elements, IList<string> keywords) where T : Element
        {
            if (keywords == null || keywords.Count == 0)
            {
                return elements.FirstOrDefault();
            }

            foreach (string keyword in keywords)
            {
                T match = elements.FirstOrDefault(x => TextContains(x.Name, keyword));
                if (match != null)
                {
                    return match;
                }
            }

            return elements.FirstOrDefault();
        }

        private static string FindFamilySymbolName(Document document, BuiltInCategory category, IList<string> keywords)
        {
            try
            {
                IList<FamilySymbol> symbols = new FilteredElementCollector(document)
                    .OfCategory(category)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .OrderBy(x => x.FamilyName)
                    .ThenBy(x => x.Name)
                    .ToList();

                FamilySymbol match = null;
                foreach (string keyword in keywords ?? new List<string>())
                {
                    match = symbols.FirstOrDefault(x => TextContains(x.FamilyName, keyword) || TextContains(x.Name, keyword));
                    if (match != null)
                    {
                        break;
                    }
                }

                match = match ?? symbols.FirstOrDefault();
                return match == null ? "Missing family symbol" : match.FamilyName + " : " + match.Name;
            }
            catch
            {
                return "Family lookup failed";
            }
        }

        private static IList<IList<BoundarySegment>> GetBoundaryGroups(Room room)
        {
            try
            {
                var options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                return room.GetBoundarySegments(options) ?? new List<IList<BoundarySegment>>();
            }
            catch
            {
                return new List<IList<BoundarySegment>>();
            }
        }

        private static string ResolveRoomTargetType(Document document, string text)
        {
            if (text.Contains("wall")) return FindElementTypeName<WallType>(document, MhnkArcToolSettings.Load().SplitKeywords(MhnkArcToolSettings.Load().PreferredWallTypeKeywords));
            if (text.Contains("floor")) return FindElementTypeName<FloorType>(document, new List<string>());
            if (text.Contains("ceiling")) return FindElementTypeName<CeilingType>(document, new List<string>());
            return "Room boundary / room placement";
        }

        private static string GetRoomTargetType(string text)
        {
            if ((text ?? "").Contains("wall")) return "Walls";
            if ((text ?? "").Contains("floor")) return "Floors";
            if ((text ?? "").Contains("ceiling")) return "Ceilings";
            return "Rooms";
        }

        private static IList<BuiltInCategory> InferBuiltInCategories(string text)
        {
            text = text ?? "";
            var categories = new List<BuiltInCategory>();
            if (text.Contains("wall")) categories.Add(BuiltInCategory.OST_Walls);
            if (text.Contains("floor")) categories.Add(BuiltInCategory.OST_Floors);
            if (text.Contains("ceiling")) categories.Add(BuiltInCategory.OST_Ceilings);
            if (text.Contains("door")) categories.Add(BuiltInCategory.OST_Doors);
            if (text.Contains("window")) categories.Add(BuiltInCategory.OST_Windows);
            if (text.Contains("room")) categories.Add(BuiltInCategory.OST_Rooms);
            if (text.Contains("cad")) categories.Add(BuiltInCategory.OST_ImportObjectStyles);
            return categories.Distinct().ToList();
        }

        private static IList<BuiltInCategory> GetSolidsCategories()
        {
            return new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_GenericModel
            };
        }

        private static int CountVisibleElementsByCategory(MhnkArcContext context, Category category)
        {
            if (context == null || category == null)
            {
                return 0;
            }

            try
            {
                ElementId categoryId = category.Id;
                return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Count(x => x.Category != null && ElementIdEquals(x.Category.Id, categoryId));
            }
            catch
            {
                return 0;
            }
        }

        private static IList<MhnkArcWorkspaceCategoryCount> CaptureTopCategories(Document doc, View view)
        {
            try
            {
                return new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e.Category != null && !string.IsNullOrWhiteSpace(e.Category.Name))
                    .GroupBy(e => e.Category.Name)
                    .Select(g => new MhnkArcWorkspaceCategoryCount(g.Key, g.Count()))
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Name)
                    .Take(10)
                    .ToList();
            }
            catch
            {
                return new List<MhnkArcWorkspaceCategoryCount>();
            }
        }

        private static MhnkArcPreviewRow Row(
            MhnkArcPreviewRowKind kind,
            string item,
            string source,
            string elementId,
            string category,
            string layer,
            string targetType,
            string status,
            string detail,
            string skipReason)
        {
            return new MhnkArcPreviewRow
            {
                Kind = kind,
                Item = item ?? "",
                Source = source ?? "",
                ElementIdText = string.IsNullOrWhiteSpace(elementId) ? "" : "Id " + elementId,
                Category = category ?? "",
                Layer = layer ?? "",
                TargetType = targetType ?? "",
                Level = "",
                Status = status ?? "",
                Detail = detail ?? "",
                SkipReason = skipReason ?? "",
                Include = !string.Equals(status, "Skip", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(status, "Empty", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string GetModelLabel(MhnkArcContext context)
        {
            string document = string.IsNullOrWhiteSpace(context?.Document?.Title) ? "Untitled model" : context.Document.Title;
            string view = string.IsNullOrWhiteSpace(context?.ActiveView?.Name) ? "active view" : context.ActiveView.Name;
            return document + " / " + view;
        }

        private static string GetToolText(MhnkArcCommandOption option)
        {
            return ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
        }

        private static bool IsCadTool(string text)
        {
            text = text ?? "";
            return text.Contains("cad") || text.Contains("layer") || text.Contains("mapping") || text.Contains("marker");
        }

        private static bool IsRoomTool(string text)
        {
            text = text ?? "";
            return text.Contains("room") &&
                   (text.Contains("create") || text.Contains("floor") || text.Contains("ceiling") || text.Contains("wall"));
        }

        private static bool IsSelectionTool(string category, string text)
        {
            text = text ?? "";
            return string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("selected") ||
                   text.Contains("selection") ||
                   text.Contains("select by") ||
                   text.Contains("join") ||
                   text.Contains("unjoin") ||
                   text.Contains("cut") ||
                   text.Contains("uncut") ||
                   text.Contains("pin") ||
                   text.Contains("align") ||
                   text.Contains("rename");
        }

        private static bool IsReviewTool(string text)
        {
            text = text ?? "";
            return text.Contains("report") ||
                   text.Contains("dashboard") ||
                   text.Contains("check") ||
                   text.Contains("validation") ||
                   text.Contains("diagnostics") ||
                   text.Contains("warnings");
        }

        private static bool IsUsableRoom(Room room)
        {
            return room != null && SafeRoomArea(room) > MinimumRoomArea && GetBoundaryGroups(room).Count > 0;
        }

        private static double SafeRoomArea(Room room)
        {
            try
            {
                return room?.Area ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static string FormatAreaSquareMeters(double squareFeet)
        {
            return (squareFeet * 0.09290304).ToString("0.###", CultureInfo.InvariantCulture) + " m2";
        }

        private static string GetRoomLabel(Room room)
        {
            if (room == null)
            {
                return "Room";
            }

            string number = string.IsNullOrWhiteSpace(room.Number) ? "" : room.Number.Trim();
            string name = string.IsNullOrWhiteSpace(room.Name) ? "Room" : room.Name.Trim();
            return string.IsNullOrWhiteSpace(number) ? name : number + " - " + name;
        }

        private static string GetCurveLayerName(CurveElement curveElement)
        {
            try
            {
                Element style = curveElement?.LineStyle;
                if (!string.IsNullOrWhiteSpace(style?.Name))
                {
                    return style.Name;
                }
            }
            catch
            {
            }

            return "(model/detail)";
        }

        private static string GetCurveDetail(CurveElement curveElement)
        {
            try
            {
                Curve curve = curveElement?.GeometryCurve;
                if (curve != null)
                {
                    return "Length " + (curve.Length / 3.280839895013123).ToString("0.###", CultureInfo.InvariantCulture) + " m";
                }
            }
            catch
            {
            }

            return SafeElementName(curveElement);
        }

        private static string GetRuleLabel(MhnkArcSmartMappingRule rule, string action)
        {
            if (rule != null)
            {
                return rule.RuleName + " -> " + MhnkArcSmartMappingRules.NormalizeAction(rule.Action);
            }

            return action == "Unmapped" ? "No mapping rule" : "Settings fallback -> " + action;
        }

        private static bool LayerMatches(string layerName, IList<string> keywords)
        {
            string layer = layerName ?? "";
            foreach (string keyword in keywords ?? new List<string>())
            {
                if (TextContains(layer, keyword))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TextContains(string source, string value)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SafeCategoryName(Element element)
        {
            try
            {
                return element?.Category?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string SafeElementName(Element element)
        {
            try
            {
                return string.IsNullOrWhiteSpace(element?.Name) ? element?.GetType().Name ?? "" : element.Name;
            }
            catch
            {
                return element?.GetType().Name ?? "";
            }
        }

        private static string SafeTypeName(Document document, Element element)
        {
            try
            {
                ElementId typeId = element?.GetTypeId() ?? ElementId.InvalidElementId;
                Element type = typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);
                return string.IsNullOrWhiteSpace(type?.Name) ? "" : type.Name;
            }
            catch
            {
                return "";
            }
        }

        private static string GetLevelName(Document document, Element element)
        {
            try
            {
                ElementId levelId = element?.LevelId ?? ElementId.InvalidElementId;
                Element level = levelId == ElementId.InvalidElementId ? null : document.GetElement(levelId);
                if (!string.IsNullOrWhiteSpace(level?.Name))
                {
                    return level.Name;
                }
            }
            catch
            {
            }

            try
            {
                Parameter parameter = element?.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                ElementId id = parameter?.AsElementId() ?? ElementId.InvalidElementId;
                Element level = id == ElementId.InvalidElementId ? null : document.GetElement(id);
                return level?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string GetCategoryName(Document document, BuiltInCategory category)
        {
            try
            {
                return document.Settings.Categories.get_Item(category)?.Name ?? category.ToString();
            }
            catch
            {
                return category.ToString();
            }
        }

        private static bool CanEditElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            try
            {
                return !element.Pinned || element.CanBeHidden(null);
            }
            catch
            {
                return true;
            }
        }

        private static BoundingBoxXYZ SafeBoundingBox(Element element, View view)
        {
            try
            {
                return element?.get_BoundingBox(view);
            }
            catch
            {
                return null;
            }
        }

        private static string GetBoundingBoxText(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return "No bounding box";
            }

            XYZ size = box.Max - box.Min;
            return "Box " + FormatFeet(size.X) + " x " + FormatFeet(size.Y) + " x " + FormatFeet(size.Z);
        }

        private static string FormatFeet(double feet)
        {
            return (feet / 3.280839895013123).ToString("0.###", CultureInfo.InvariantCulture) + " m";
        }

        private static string GetElementIdText(ElementId id)
        {
            return id == null ? "" : id.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool ElementIdEquals(ElementId first, ElementId second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return first.Value == second.Value;
        }

        private static int SafeCount(Func<int> read)
        {
            try
            {
                return read == null ? 0 : Math.Max(0, read());
            }
            catch
            {
                return 0;
            }
        }
    }
}
