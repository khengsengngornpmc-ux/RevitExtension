using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class SiteProgressSummaryRow
    {
        public string GroupName { get; set; } = "";
        public string StructureElement { get; set; } = "";
        public string BuildingLevel { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string ElementTypeName { get; set; } = "";
        public int ElementCount { get; set; }
        public double ReinforcementKg { get; set; }
        public double FormworkM2 { get; set; }
        public double VolumeM3 { get; set; }
        public double SiteProgressReinforcementPercent { get; set; }
        public double SiteProgressFormworkPercent { get; set; }
        public double SiteProgressVolumePercent { get; set; }
        public double ReinforcementCompletedKg { get; set; }
        public double FormworkCompletedM2 { get; set; }
        public double VolumeCompletedM3 { get; set; }
        public string Status { get; set; } = "";
        public double ProgressPercent { get; set; }
        public double TotalBoq { get; set; }
        public double CompletedBoq { get; set; }
        public double RemainingBoq { get; set; }
        public List<int> ElementIds { get; set; } = new List<int>();
        public string ElementIdsText => string.Join(",",
            (ElementIds ?? new List<int>())
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id));
    }

    internal sealed class SiteProgressElementDetailRow
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; } = "";
        public string StructuralPlan { get; set; } = "";
        public string BuildingLevel { get; set; } = "";
        public string StructureElement { get; set; } = "";
        public string Category { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public double ReinforcementKg { get; set; }
        public double FormworkM2 { get; set; }
        public double VolumeM3 { get; set; }
        public double SiteProgressReinforcementPercent { get; set; }
        public double SiteProgressFormworkPercent { get; set; }
        public double SiteProgressVolumePercent { get; set; }
        public double ReinforcementCompletedKg { get; set; }
        public double FormworkCompletedM2 { get; set; }
        public double VolumeCompletedM3 { get; set; }
        public double ProgressPercent { get; set; }
        public string Status { get; set; } = "";
    }

    internal static class CBIM_SITE_PROGRESS
    {
        private const string SiteProgressParamName = "%Site_Progress";
        private const string SiteProgressReinforcementParamName = "%Site_Progress_Reinforcement";
        private const string SiteProgressFormworkParamName = "%Site_Progress_Formwork";
        private const string SiteProgressVolumeParamName = "%Site_Progress_Volume";
        private const string StructuralPlanParamName = "CBIM_StructuralPlan";
        private const string BuildingLevelParamName = "CBIM_BuildingLevel";
        private const string StructureTypeParamName = "CBIM_StructureType";
        private const string RevitElementIdParamName = "CBIM_RevitElementID";
        private const string FormworkAreaParamName = "CBIM_FormworkArea";
        private const string FormworkShapeFlagParamName = "CBIM.FWK.IsShape";
        private const string FormworkShapeMarkerParamName = "CBIM.FormworkShape";
        private const string FormworkShapeHostElementIdParamName = "CBIM.FWK.ElementId";
        private const string FormworkShapeFaceAreaParamName = "CBIM.FWK.FaceArea";
        private static readonly string[] StructureTypeParameterAliases =
        {
            StructureTypeParamName,
            "Structure Type",
            "Element Type",
            "TypeName",
            "Type Name"
        };

        private static readonly BuiltInCategory[] SummaryCategories =
        {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Stairs
        };

        private static readonly BuiltInCategory[] SiteProgressParameterCategories =
        {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_SpecialityEquipment
        };

        public static void ApplyFromSelection(UIApplication uiapp, UIDocument uidoc, Document doc, CadToModelRequest request, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null || request == null) return;

            EnsureSiteProgressParameter(uiapp?.Application, doc, request, window);

            ICollection<ElementId> selectedIds = uidoc.Selection?.GetElementIds() ?? new List<ElementId>();
            if (selectedIds.Count == 0)
            {
                window?.ShowStatus("Site Progress: select elements first.");
                return;
            }

            double reinforcementInput = ClampPercent(request.SiteProgressReinforcementPercent);
            double formworkInput = ClampPercent(request.SiteProgressFormworkPercent);
            double volumeInput = ClampPercent(request.SiteProgressVolumePercent);
            bool addMode = request.SiteProgressIsAddMode;

            int changed = 0;
            int missingOrReadOnly = 0;
            int unchanged = 0;

            using (Transaction t = new Transaction(doc, "CamboBIM - Update Site Progress"))
            {
                t.Start();

                foreach (ElementId id in selectedIds)
                {
                    Element element = doc.GetElement(id);
                    if (element == null || element is ElementType)
                    {
                        continue;
                    }

                    bool changedAny = false;
                    bool hadWritable = false;

                    changedAny |= TryApplyMetricPercent(element, SiteProgressReinforcementParamName, reinforcementInput, addMode, ref hadWritable);
                    changedAny |= TryApplyMetricPercent(element, SiteProgressFormworkParamName, formworkInput, addMode, ref hadWritable);
                    changedAny |= TryApplyMetricPercent(element, SiteProgressVolumeParamName, volumeInput, addMode, ref hadWritable);

                    // Keep legacy %Site_Progress synchronized with volume progress.
                    changedAny |= TryApplyMetricPercent(element, SiteProgressParamName, volumeInput, addMode, ref hadWritable);
                    changedAny |= TrySetTextParameter(element, StructureTypeParamName, GetTypeName(doc, element));
                    changedAny |= TrySetTextParameter(
                        element,
                        RevitElementIdParamName,
                        element.Id.Value.ToString(CultureInfo.InvariantCulture));

                    if (!hadWritable)
                    {
                        missingOrReadOnly++;
                    }
                    else if (changedAny)
                    {
                        changed++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }

                t.Commit();
            }

            string mode = addMode ? "add" : "set";
            window?.ShowStatus(
                $"Site Progress: {mode} R={reinforcementInput:0.##}% F={formworkInput:0.##}% V={volumeInput:0.##}% -> changed {changed}, missing/locked {missingOrReadOnly}, unchanged {unchanged}.");

            RefreshSummary(uiapp, uidoc, doc, request, window);
        }

        public static void ApplyFromImportRows(UIApplication uiapp, UIDocument uidoc, Document doc, CadToModelRequest request, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null || request == null) return;

            EnsureSiteProgressParameter(uiapp?.Application, doc, request, window);

            List<SiteProgressImportRowPayload> importedRows = (request.SiteProgressImportRows ?? new List<SiteProgressImportRowPayload>())
                .Where(r => r != null)
                .Select(NormalizeImportedProgressRow)
                .ToList();

            if (importedRows.Count == 0)
            {
                window?.ShowStatus("Site Progress import: no rows to apply.");
                return;
            }

            var byExact = new Dictionary<string, SiteProgressImportRowPayload>(StringComparer.OrdinalIgnoreCase);
            var byLevel = new Dictionary<string, SiteProgressImportRowPayload>(StringComparer.OrdinalIgnoreCase);
            var byStructure = new Dictionary<string, SiteProgressImportRowPayload>(StringComparer.OrdinalIgnoreCase);

            int skippedInputRows = 0;
            foreach (SiteProgressImportRowPayload row in importedRows)
            {
                if (!row.ReinforcementPercent.HasValue &&
                    !row.FormworkPercent.HasValue &&
                    !row.VolumePercent.HasValue)
                {
                    skippedInputRows++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.StructureElement))
                {
                    skippedInputRows++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.BuildingLevel) &&
                    !string.IsNullOrWhiteSpace(row.TypeName))
                {
                    byExact[BuildImportExactKey(row.StructureElement, row.BuildingLevel, row.TypeName)] = row;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.BuildingLevel))
                {
                    byLevel[BuildImportLevelKey(row.StructureElement, row.BuildingLevel)] = row;
                    continue;
                }

                byStructure[BuildImportStructureKey(row.StructureElement)] = row;
            }

            if (byExact.Count == 0 && byLevel.Count == 0 && byStructure.Count == 0)
            {
                window?.ShowStatus("Site Progress import: no valid mapping rows found.");
                return;
            }

            QsScope scope = request.SiteProgressScope;
            ICollection<ElementId> selectedIds = uidoc.Selection?.GetElementIds() ?? new List<ElementId>();
            if (scope == QsScope.CurrentSelection && selectedIds.Count == 0)
            {
                scope = QsScope.EntireModel;
            }

            List<Element> elements = CollectElements(uidoc, doc, scope);
            if (elements.Count == 0)
            {
                window?.ShowStatus("Site Progress import: no elements found in scope.");
                return;
            }

            int matched = 0;
            int changed = 0;
            int unchanged = 0;
            int missingOrReadOnly = 0;
            int noMapping = 0;

            using (Transaction t = new Transaction(doc, "CamboBIM - Import Site Progress"))
            {
                t.Start();

                foreach (Element element in elements)
                {
                    if (element == null || element is ElementType)
                    {
                        continue;
                    }

                    string structure = NormalizeImportText(ResolveStructureElementName(element));
                    if (string.IsNullOrWhiteSpace(structure))
                    {
                        structure = "Unassigned";
                    }

                    string level = NormalizeImportText(ResolveGroupName(doc, element, BuildingLevelParamName));
                    if (string.IsNullOrWhiteSpace(level))
                    {
                        level = "Unassigned";
                    }

                    string type = NormalizeImportText(GetTypeName(doc, element));
                    SiteProgressImportRowPayload row = ResolveImportedProgressRow(byExact, byLevel, byStructure, structure, level, type);
                    if (row == null)
                    {
                        noMapping++;
                        continue;
                    }

                    matched++;
                    bool hadWritable = false;
                    bool changedAny = false;

                    if (row.ReinforcementPercent.HasValue)
                    {
                        changedAny |= TryApplyMetricPercent(
                            element,
                            SiteProgressReinforcementParamName,
                            row.ReinforcementPercent.Value,
                            addMode: false,
                            ref hadWritable);
                    }

                    if (row.FormworkPercent.HasValue)
                    {
                        changedAny |= TryApplyMetricPercent(
                            element,
                            SiteProgressFormworkParamName,
                            row.FormworkPercent.Value,
                            addMode: false,
                            ref hadWritable);
                    }

                    if (row.VolumePercent.HasValue)
                    {
                        changedAny |= TryApplyMetricPercent(
                            element,
                            SiteProgressVolumeParamName,
                            row.VolumePercent.Value,
                            addMode: false,
                            ref hadWritable);

                        // Keep legacy %Site_Progress synchronized with volume progress.
                        changedAny |= TryApplyMetricPercent(
                            element,
                            SiteProgressParamName,
                            row.VolumePercent.Value,
                            addMode: false,
                            ref hadWritable);
                    }

                    changedAny |= TrySetTextParameter(element, StructureTypeParamName, type);
                    changedAny |= TrySetTextParameter(
                        element,
                        RevitElementIdParamName,
                        element.Id.Value.ToString(CultureInfo.InvariantCulture));

                    if (!hadWritable)
                    {
                        missingOrReadOnly++;
                    }
                    else if (changedAny)
                    {
                        changed++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }

                t.Commit();
            }

            window?.ShowStatus(
                $"Site Progress import applied: input={importedRows.Count}, skipped={skippedInputRows}, matched={matched}, changed={changed}, missing/locked={missingOrReadOnly}, unchanged={unchanged}, unmatched={noMapping}.");

            request.SiteProgressScope = QsScope.EntireModel;
            request.SiteProgressStructureFilter = "All";
            request.SiteProgressBuildingLevelFilter = "All";
            RefreshSummary(uiapp, uidoc, doc, request, window);
        }

        private static SiteProgressImportRowPayload ResolveImportedProgressRow(
            IReadOnlyDictionary<string, SiteProgressImportRowPayload> byExact,
            IReadOnlyDictionary<string, SiteProgressImportRowPayload> byLevel,
            IReadOnlyDictionary<string, SiteProgressImportRowPayload> byStructure,
            string structure,
            string level,
            string type)
        {
            if (byExact != null &&
                !string.IsNullOrWhiteSpace(type) &&
                byExact.TryGetValue(BuildImportExactKey(structure, level, type), out SiteProgressImportRowPayload exact))
            {
                return exact;
            }

            if (byLevel != null &&
                byLevel.TryGetValue(BuildImportLevelKey(structure, level), out SiteProgressImportRowPayload levelMatch))
            {
                return levelMatch;
            }

            if (byStructure != null &&
                byStructure.TryGetValue(BuildImportStructureKey(structure), out SiteProgressImportRowPayload structureMatch))
            {
                return structureMatch;
            }

            return null;
        }

        private static SiteProgressImportRowPayload NormalizeImportedProgressRow(SiteProgressImportRowPayload row)
        {
            if (row == null)
            {
                return new SiteProgressImportRowPayload();
            }

            return new SiteProgressImportRowPayload
            {
                StructureElement = NormalizeImportText(row.StructureElement),
                BuildingLevel = NormalizeImportText(row.BuildingLevel),
                TypeName = NormalizeImportText(row.TypeName),
                ReinforcementPercent = row.ReinforcementPercent.HasValue ? ClampPercent(row.ReinforcementPercent.Value) : (double?)null,
                FormworkPercent = row.FormworkPercent.HasValue ? ClampPercent(row.FormworkPercent.Value) : (double?)null,
                VolumePercent = row.VolumePercent.HasValue ? ClampPercent(row.VolumePercent.Value) : (double?)null
            };
        }

        private static string BuildImportExactKey(string structure, string level, string type)
        {
            return NormalizeImportText(structure) + "|" +
                   NormalizeImportText(level) + "|" +
                   NormalizeImportText(type);
        }

        private static string BuildImportLevelKey(string structure, string level)
        {
            return NormalizeImportText(structure) + "|" +
                   NormalizeImportText(level);
        }

        private static string BuildImportStructureKey(string structure)
        {
            return NormalizeImportText(structure);
        }

        private static string NormalizeImportText(string value)
        {
            return (value ?? "").Trim();
        }

        public static void RefreshSummary(UIApplication uiapp, UIDocument uidoc, Document doc, CadToModelRequest request, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null || request == null) return;

            EnsureSiteProgressParameter(uiapp?.Application, doc, request, window);

            QsScope scope = request.SiteProgressScope;
            List<Element> elements = CollectElements(uidoc, doc, scope);
            elements = ApplyFilters(doc, elements, request);
            SyncElementIdSharedParameterValues(doc, elements);
            Dictionary<int, FormworkShapeAggregate> formworkByHost = BuildFormworkShapeAggregateByHost(doc, scope, elements);

            List<SiteProgressSummaryRow> byElementAndFloor = BuildSummaryRowsByElementAndFloor(doc, elements, formworkByHost);
            List<SiteProgressSummaryRow> overallByBuilding = BuildOverallBuildingRows(elements, formworkByHost);
            List<SiteProgressElementDetailRow> elementDetails = BuildElementRows(doc, elements, formworkByHost);

            int counted = elementDetails.Count;
            double totalBoq = elementDetails.Sum(r => r.VolumeM3);
            double completedBoq = elementDetails.Sum(r => r.VolumeCompletedM3);
            double overallPercent = totalBoq > 1e-9 ? (completedBoq * 100.0 / totalBoq) : 0.0;
            string scopeLabel = GetScopeLabel(scope);
            string structureFilter = NormalizeFilter(request.SiteProgressStructureFilter);
            string buildingFilter = NormalizeFilter(request.SiteProgressBuildingLevelFilter);
            string message = $"Site Progress summary ({scopeLabel}): {counted} element(s) with {SiteProgressParamName}. " +
                             $"Filters: Structure={structureFilter}, BuildingLevel={buildingFilter}. " +
                             $"Completed {completedBoq:0.###}/{totalBoq:0.###} ({overallPercent:0.##}%).";

            window?.UpdateSiteProgressSummary(byElementAndFloor, overallByBuilding, message, elementDetails);
        }

        private static void SyncElementIdSharedParameterValues(Document doc, IEnumerable<Element> elements)
        {
            if (doc == null)
            {
                return;
            }

            List<Element> targets = (elements ?? Enumerable.Empty<Element>())
                .Where(e => e != null && !(e is ElementType) && IsSummaryCategory(e.Category))
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "CamboBIM - Sync Revit ElementID Parameter"))
                {
                    tx.Start();
                    foreach (Element element in targets)
                    {
                        TrySetTextParameter(
                            element,
                            RevitElementIdParamName,
                            element.Id.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    tx.Commit();
                }
            }
            catch
            {
                // Keep Site Progress refresh running even if this metadata sync is blocked.
            }
        }

        public static List<ElementId> CollectFilteredElementIds(UIDocument uidoc, Document doc, CadToModelRequest request)
        {
            if (uidoc == null || doc == null || request == null)
            {
                return new List<ElementId>();
            }

            List<Element> elements = CollectElements(uidoc, doc, request.SiteProgressScope);
            elements = ApplyFilters(doc, elements, request);

            return elements
                .Where(e => e != null && e.Id != ElementId.InvalidElementId)
                .Select(e => e.Id)
                .Distinct()
                .OrderBy(id => id.Value)
                .ToList();
        }

        private static void EnsureSiteProgressParameter(
            Autodesk.Revit.ApplicationServices.Application app,
            Document doc,
            CadToModelRequest request,
            CamboBIMWindow window)
        {
            if (app == null || doc == null) return;

            string requestedPath = request?.QsSharedParameterFilePath?.Trim() ?? "";
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string filePath = SharedParameterPathResolver.ResolveWritableSharedParameterPath(requestedPath, baseDir);
            string groupName = string.IsNullOrWhiteSpace(request?.QsSharedParameterGroupName)
                ? "CBIM-QS"
                : request.QsSharedParameterGroupName.Trim();

            string originalFile = app.SharedParametersFilename;
            try
            {
                EnsureSharedParameterFile(filePath);
                app.SharedParametersFilename = filePath;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    window?.ShowStatus($"Site Progress: cannot open shared parameter file: {filePath}");
                    return;
                }

                DefinitionGroup group = defFile.Groups.get_Item(groupName) ?? defFile.Groups.Create(groupName);
                if (group == null)
                {
                    window?.ShowStatus("Site Progress: cannot create shared parameter group.");
                    return;
                }

                Definition defLegacy = EnsureSiteProgressDefinition(group, SiteProgressParamName, window);
                Definition defReinf = EnsureSiteProgressDefinition(group, SiteProgressReinforcementParamName, window);
                Definition defForm = EnsureSiteProgressDefinition(group, SiteProgressFormworkParamName, window);
                Definition defVol = EnsureSiteProgressDefinition(group, SiteProgressVolumeParamName, window);
                Definition defStructureType = EnsureSiteProgressTextDefinition(group, StructureTypeParamName, window);
                Definition defElementId = EnsureSiteProgressTextDefinition(group, RevitElementIdParamName, window);
                if (defLegacy == null || defReinf == null || defForm == null || defVol == null || defStructureType == null || defElementId == null) return;

                CategorySet set = app.Create.NewCategorySet();
                foreach (BuiltInCategory bic in SiteProgressParameterCategories)
                {
                    try
                    {
                        Category cat = doc.Settings.Categories.get_Item(bic);
                        if (cat != null) set.Insert(cat);
                    }
                    catch
                    {
                    }
                }

                if (set.IsEmpty)
                {
                    return;
                }

                using (Transaction t = new Transaction(doc, "CamboBIM - Bind Site Progress Parameter"))
                {
                    t.Start();
                    BindingMap map = doc.ParameterBindings;

                    EnsureDefinitionBinding(app, map, defLegacy, set);
                    EnsureDefinitionBinding(app, map, defReinf, set);
                    EnsureDefinitionBinding(app, map, defForm, set);
                    EnsureDefinitionBinding(app, map, defVol, set);
                    EnsureDefinitionBinding(app, map, defStructureType, set);
                    EnsureDefinitionBinding(app, map, defElementId, set);

                    t.Commit();
                }
            }
            catch
            {
                // keep Site Progress workflow usable even if binding fails.
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        private static string GetDefaultSharedParameterPath()
        {
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            return SharedParameterPathResolver.ResolveDefaultSharedParameterPath(baseDir);
        }

        private static string TryFindExistingSharedParameterPath(string startDir)
        {
            try
            {
                DirectoryInfo dir = string.IsNullOrWhiteSpace(startDir) ? null : new DirectoryInfo(startDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "database", "Shared Parameter", "CBIM-QS.txt");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    dir = dir.Parent;
                }
            }
            catch
            {
            }

            return "";
        }

        private static void EnsureSharedParameterFile(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath) ?? "";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            {
                File.WriteAllText(filePath,
                    "# This is a Revit shared parameter file." + Environment.NewLine +
                    "# Do not edit manually unless you know the format." + Environment.NewLine +
                    "*META\tVERSION\tMINVERSION" + Environment.NewLine +
                    "META\t2\t1" + Environment.NewLine +
                    "*GROUP\tID\tNAME" + Environment.NewLine +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE" + Environment.NewLine);
            }
        }

        private static List<Element> CollectElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            if (doc == null) return new List<Element>();

            if (scope == QsScope.CurrentSelection)
            {
                ICollection<ElementId> ids = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();
                return ids
                    .Select(doc.GetElement)
                    .Where(e => e != null && !(e is ElementType) && IsSummaryCategory(e.Category))
                    .ToList();
            }

            FilteredElementCollector collector;
            if (scope == QsScope.CurrentView && doc.ActiveView != null)
            {
                collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }

            return collector
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e != null && IsSummaryCategory(e.Category))
                .ToList();
        }

        private static List<Element> ApplyFilters(Document doc, List<Element> elements, CadToModelRequest request)
        {
            if (doc == null || elements == null || request == null) return elements ?? new List<Element>();

            string structureFilter = NormalizeFilter(request.SiteProgressStructureFilter);
            string buildingFilter = NormalizeFilter(request.SiteProgressBuildingLevelFilter);
            bool allStructure = IsAllFilter(structureFilter);
            bool allBuilding = IsAllFilter(buildingFilter);

            if (allStructure && allBuilding)
            {
                return elements;
            }

            return elements
                .Where(e => e != null)
                .Where(e =>
                {
                    if (!allStructure)
                    {
                        string structure = ResolveStructureElementName(e);
                        if (!string.Equals(structure, structureFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }

                    if (!allBuilding)
                    {
                        string building = ResolveGroupName(doc, e, BuildingLevelParamName);
                        if (string.IsNullOrWhiteSpace(building))
                        {
                            building = "Unassigned";
                        }

                        if (!string.Equals(building, buildingFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .ToList();
        }

        private static string NormalizeFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "All";
            return value.Trim();
        }

        private static bool IsAllFilter(string value)
        {
            return string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<int, FormworkShapeAggregate> BuildFormworkShapeAggregateByHost(
            Document doc,
            QsScope scope,
            IEnumerable<Element> hostElements)
        {
            var result = new Dictionary<int, FormworkShapeAggregate>();
            if (doc == null)
            {
                return result;
            }

            var hostIds = new HashSet<int>(
                (hostElements ?? Enumerable.Empty<Element>())
                    .Where(e => e != null && e.Id != ElementId.InvalidElementId)
                    .Select(e => (int)e.Id.Value));
            if (hostIds.Count == 0)
            {
                return result;
            }

            List<Element> shapes = CollectFormworkShapeElements(doc, scope);
            foreach (Element shape in shapes)
            {
                if (shape == null)
                {
                    continue;
                }

                int hostId = ResolveFormworkShapeHostElementId(shape);
                if (hostId <= 0 || !hostIds.Contains(hostId))
                {
                    continue;
                }

                double area = GetFormworkShapeArea(shape);
                if (area <= 1e-9)
                {
                    continue;
                }

                bool hasShapeProgress = TryReadExplicitMetricPercent(shape, SiteProgressFormworkParamName, out double shapePercent);

                if (!result.TryGetValue(hostId, out FormworkShapeAggregate agg))
                {
                    agg = new FormworkShapeAggregate();
                    result[hostId] = agg;
                }

                agg.FormworkM2 += area;
                if (hasShapeProgress)
                {
                    agg.CoveredAreaM2 += area;
                    agg.CompletedM2 += area * shapePercent / 100.0;
                }
            }

            return result;
        }

        private static List<Element> CollectFormworkShapeElements(Document doc, QsScope scope)
        {
            if (doc == null)
            {
                return new List<Element>();
            }

            FilteredElementCollector collector;
            if (scope == QsScope.CurrentView && doc.ActiveView != null)
            {
                collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            }
            else
            {
                // For current selection and entire model scopes, resolve host-linked shapes from the full document.
                collector = new FilteredElementCollector(doc);
            }

            return collector
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(IsFormworkShapeElement)
                .ToList();
        }

        private static bool IsFormworkShapeElement(Element element)
        {
            if (element?.Category?.Id == null)
            {
                return false;
            }

            if (element.Category.Id.Value != (long)BuiltInCategory.OST_SpecialityEquipment)
            {
                return false;
            }

            Parameter shapeFlag = element.LookupParameter(FormworkShapeFlagParamName);
            if (shapeFlag != null &&
                shapeFlag.StorageType == StorageType.Integer &&
                shapeFlag.AsInteger() == 1)
            {
                return true;
            }

            Parameter marker = element.LookupParameter(FormworkShapeMarkerParamName);
            if (marker != null && marker.StorageType == StorageType.String)
            {
                string value = marker.AsString();
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.IndexOf("FormworkShape", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ResolveFormworkShapeHostElementId(Element shape)
        {
            if (shape == null)
            {
                return 0;
            }

            Parameter hostIdParam = shape.LookupParameter(FormworkShapeHostElementIdParamName);
            if (hostIdParam != null)
            {
                if (hostIdParam.StorageType == StorageType.String)
                {
                    string text = (hostIdParam.AsString() ?? hostIdParam.AsValueString() ?? "").Trim();
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        return parsed;
                    }
                }
                else if (hostIdParam.StorageType == StorageType.Integer)
                {
                    int parsed = hostIdParam.AsInteger();
                    if (parsed > 0)
                    {
                        return parsed;
                    }
                }
                else if (hostIdParam.StorageType == StorageType.ElementId)
                {
                    ElementId id = hostIdParam.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        return (int)id.Value;
                    }
                }
            }

            // Fallback for legacy shapes that only store host id in ApplicationDataId (e.g. "12345:Column:0").
            if (shape is DirectShape directShape)
            {
                string appData = (directShape.ApplicationDataId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(appData))
                {
                    string firstToken = appData.Split(':').FirstOrDefault() ?? "";
                    if (int.TryParse(firstToken.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        return parsed;
                    }
                }
            }

            return 0;
        }

        private static double GetFormworkShapeArea(Element shape)
        {
            if (shape == null)
            {
                return 0.0;
            }

            double area = GetParamDoubleByName(shape, FormworkShapeFaceAreaParamName, FormworkAreaParamName);
            if (area > 1e-9)
            {
                return area;
            }

            Parameter hostArea = shape.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (hostArea != null && hostArea.StorageType == StorageType.Double)
            {
                double value = hostArea.AsDouble();
                if (value > 1e-9)
                {
                    return value;
                }
            }

            return 0.0;
        }

        private static bool TryReadExplicitMetricPercent(Element element, string metricParamName, out double percent)
        {
            percent = 0.0;
            if (element == null || string.IsNullOrWhiteSpace(metricParamName))
            {
                return false;
            }

            Parameter metric = element.LookupParameter(metricParamName);
            if (TryReadProgressPercent(metric, out double metricPercent) && HasExplicitProgressValue(metric))
            {
                percent = ClampPercent(metricPercent);
                return true;
            }

            Parameter legacy = element.LookupParameter(SiteProgressParamName);
            if (TryReadProgressPercent(legacy, out double legacyPercent) && HasExplicitProgressValue(legacy))
            {
                percent = ClampPercent(legacyPercent);
                return true;
            }

            return false;
        }

        private static void ResolveElementFormworkContribution(
            Element element,
            double hostFormworkPercent,
            IReadOnlyDictionary<int, FormworkShapeAggregate> formworkByHost,
            out double formworkBoq,
            out double formworkCompleted,
            out double effectiveFormworkPercent)
        {
            double hostPercent = ClampPercent(hostFormworkPercent);
            formworkBoq = GetFormworkBoq(element);
            formworkCompleted = formworkBoq * hostPercent / 100.0;
            effectiveFormworkPercent = formworkBoq > 1e-9 ? hostPercent : 0.0;

            int elementId = element?.Id == null || element.Id == ElementId.InvalidElementId
                ? 0
                : (int)element.Id.Value;
            if (elementId <= 0 || formworkByHost == null)
            {
                return;
            }

            if (!formworkByHost.TryGetValue(elementId, out FormworkShapeAggregate shapeAgg) ||
                shapeAgg == null ||
                shapeAgg.FormworkM2 <= 1e-9)
            {
                return;
            }

            double uncoveredArea = Math.Max(0.0, shapeAgg.FormworkM2 - shapeAgg.CoveredAreaM2);
            double completedFromShapes = shapeAgg.CompletedM2 + uncoveredArea * hostPercent / 100.0;

            formworkBoq = shapeAgg.FormworkM2;
            formworkCompleted = Math.Max(0.0, Math.Min(formworkBoq, completedFromShapes));
            effectiveFormworkPercent = formworkBoq > 1e-9
                ? ClampPercent(formworkCompleted * 100.0 / formworkBoq)
                : 0.0;
        }

        private static List<SiteProgressSummaryRow> BuildSummaryRowsByElementAndFloor(
            Document doc,
            IEnumerable<Element> elements,
            IReadOnlyDictionary<int, FormworkShapeAggregate> formworkByHost)
        {
            var map = new Dictionary<string, SiteProgressDetailAggregate>(StringComparer.OrdinalIgnoreCase);
            if (elements == null) return new List<SiteProgressSummaryRow>();

            foreach (Element element in elements)
            {
                if (element == null) continue;

                string structure = ResolveStructureElementName(element);
                if (string.IsNullOrWhiteSpace(structure)) structure = "Unassigned";
                string level = ResolveGroupName(doc, element, BuildingLevelParamName);
                if (string.IsNullOrWhiteSpace(level)) level = "Unassigned";
                string type = GetTypeName(doc, element);
                if (string.IsNullOrWhiteSpace(type)) type = "(No Type)";
                string elementType = ResolveElementTypeName(doc, element);
                if (string.IsNullOrWhiteSpace(elementType)) elementType = type;
                string key = structure + "|" + level + "|" + type + "|" + elementType;

                double pReinf = ReadMetricPercent(element, SiteProgressReinforcementParamName);
                double pVol = ReadMetricPercent(element, SiteProgressVolumeParamName);
                double hostFormworkPercent = ReadMetricPercent(element, SiteProgressFormworkParamName);

                double volumeBoq = GetVolumeBoq(element);
                double reinforcementBoq = GetReinforcementKg(element);
                ResolveElementFormworkContribution(
                    element,
                    hostFormworkPercent,
                    formworkByHost,
                    out double formworkBoq,
                    out double formworkCompleted,
                    out _);

                SiteProgressDetailAggregate agg;
                if (!map.TryGetValue(key, out agg))
                {
                    agg = new SiteProgressDetailAggregate
                    {
                        StructureElement = structure,
                        BuildingLevel = level,
                        TypeName = type,
                        ElementTypeName = elementType
                    };
                    map[key] = agg;
                }

                agg.ElementCount++;
                agg.ElementIds.Add((int)element.Id.Value);
                agg.ReinforcementKg += reinforcementBoq;
                agg.FormworkM2 += formworkBoq;
                agg.VolumeM3 += volumeBoq;
                agg.ReinforcementCompletedKg += reinforcementBoq * pReinf / 100.0;
                agg.FormworkCompletedM2 += formworkCompleted;
                agg.VolumeCompletedM3 += volumeBoq * pVol / 100.0;
            }

            return map
                .Select(kvp =>
                {
                    SiteProgressDetailAggregate agg = kvp.Value;
                    double pReinf = agg.ReinforcementKg > 1e-9 ? (agg.ReinforcementCompletedKg * 100.0 / agg.ReinforcementKg) : 0.0;
                    double pForm = agg.FormworkM2 > 1e-9 ? (agg.FormworkCompletedM2 * 100.0 / agg.FormworkM2) : 0.0;
                    double pVol = agg.VolumeM3 > 1e-9 ? (agg.VolumeCompletedM3 * 100.0 / agg.VolumeM3) : 0.0;
                    double overall = ResolveOverallPercent(pReinf, pForm, pVol, agg);

                    return new SiteProgressSummaryRow
                    {
                        StructureElement = agg.StructureElement,
                        BuildingLevel = agg.BuildingLevel,
                        TypeName = agg.TypeName,
                        ElementTypeName = agg.ElementTypeName,
                        GroupName = kvp.Key,
                        ElementCount = agg.ElementCount,
                        ReinforcementKg = agg.ReinforcementKg,
                        FormworkM2 = agg.FormworkM2,
                        VolumeM3 = agg.VolumeM3,
                        SiteProgressReinforcementPercent = pReinf,
                        SiteProgressFormworkPercent = pForm,
                        SiteProgressVolumePercent = pVol,
                        ReinforcementCompletedKg = agg.ReinforcementCompletedKg,
                        FormworkCompletedM2 = agg.FormworkCompletedM2,
                        VolumeCompletedM3 = agg.VolumeCompletedM3,
                        Status = BuildStatus(pReinf, pForm, pVol),
                        ProgressPercent = overall,
                        TotalBoq = agg.VolumeM3,
                        CompletedBoq = agg.VolumeCompletedM3,
                        RemainingBoq = Math.Max(0.0, agg.VolumeM3 - agg.VolumeCompletedM3),
                        ElementIds = agg.ElementIds
                            .Distinct()
                            .OrderBy(id => id)
                            .ToList()
                    };
                })
                .OrderBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => GetStructureOrder(r.StructureElement))
                .ThenBy(r => r.StructureElement, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.SiteProgressReinforcementPercent)
                .ThenBy(r => r.SiteProgressFormworkPercent)
                .ThenBy(r => r.SiteProgressVolumePercent)
                .ThenBy(r => r.Status, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<SiteProgressSummaryRow> BuildOverallBuildingRows(
            IEnumerable<Element> elements,
            IReadOnlyDictionary<int, FormworkShapeAggregate> formworkByHost)
        {
            if (elements == null) return new List<SiteProgressSummaryRow>();

            var detailElements = elements
                .Where(e => e != null)
                .ToList();

            var byLevel = detailElements
                .GroupBy(e =>
                {
                    string level = ResolveGroupName(e.Document, e, BuildingLevelParamName);
                    return string.IsNullOrWhiteSpace(level) ? "Unassigned" : level.Trim();
                }, StringComparer.OrdinalIgnoreCase)
                .Select(g => BuildOverallAggregateRow(g.Key, g, formworkByHost))
                .OrderBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SiteProgressSummaryRow overall = BuildOverallAggregateRow("Entire Building", detailElements, formworkByHost);
            byLevel.Add(overall);
            return byLevel;
        }

        private static SiteProgressSummaryRow BuildOverallAggregateRow(
            string groupName,
            IEnumerable<Element> elements,
            IReadOnlyDictionary<int, FormworkShapeAggregate> formworkByHost)
        {
            int count = 0;
            double reinforcementKg = 0.0;
            double formworkM2 = 0.0;
            double volumeM3 = 0.0;
            double reinforcementCompletedKg = 0.0;
            double formworkCompletedM2 = 0.0;
            double volumeCompletedM3 = 0.0;
            var elementIds = new List<int>();

            foreach (Element element in elements ?? Enumerable.Empty<Element>())
            {
                if (element == null) continue;
                count++;
                elementIds.Add((int)element.Id.Value);

                double pReinf = ReadMetricPercent(element, SiteProgressReinforcementParamName);
                double pVol = ReadMetricPercent(element, SiteProgressVolumeParamName);
                double hostFormworkPercent = ReadMetricPercent(element, SiteProgressFormworkParamName);

                double reinf = GetReinforcementKg(element);
                double vol = GetVolumeBoq(element);
                ResolveElementFormworkContribution(
                    element,
                    hostFormworkPercent,
                    formworkByHost,
                    out double form,
                    out double formCompleted,
                    out _);
                reinforcementKg += reinf;
                formworkM2 += form;
                volumeM3 += vol;
                reinforcementCompletedKg += reinf * pReinf / 100.0;
                formworkCompletedM2 += formCompleted;
                volumeCompletedM3 += vol * pVol / 100.0;
            }

            double overallPReinf = reinforcementKg > 1e-9 ? reinforcementCompletedKg * 100.0 / reinforcementKg : 0.0;
            double overallPForm = formworkM2 > 1e-9 ? formworkCompletedM2 * 100.0 / formworkM2 : 0.0;
            double overallPVol = volumeM3 > 1e-9 ? volumeCompletedM3 * 100.0 / volumeM3 : 0.0;
            double overall = ResolveOverallPercent(overallPReinf, overallPForm, overallPVol, null);

            return new SiteProgressSummaryRow
            {
                GroupName = groupName,
                StructureElement = "(All)",
                BuildingLevel = groupName,
                TypeName = "(All Types)",
                ElementTypeName = "(All Types)",
                ElementCount = count,
                ReinforcementKg = reinforcementKg,
                FormworkM2 = formworkM2,
                VolumeM3 = volumeM3,
                SiteProgressReinforcementPercent = overallPReinf,
                SiteProgressFormworkPercent = overallPForm,
                SiteProgressVolumePercent = overallPVol,
                ReinforcementCompletedKg = reinforcementCompletedKg,
                FormworkCompletedM2 = formworkCompletedM2,
                VolumeCompletedM3 = volumeCompletedM3,
                Status = BuildStatus(overallPReinf, overallPForm, overallPVol),
                ProgressPercent = overall,
                TotalBoq = volumeM3,
                CompletedBoq = volumeCompletedM3,
                RemainingBoq = Math.Max(0.0, volumeM3 - volumeCompletedM3),
                ElementIds = elementIds
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList()
            };
        }

        private static List<SiteProgressElementDetailRow> BuildElementRows(
            Document doc,
            IEnumerable<Element> elements,
            IReadOnlyDictionary<int, FormworkShapeAggregate> formworkByHost)
        {
            var rows = new List<SiteProgressElementDetailRow>();
            foreach (Element element in elements ?? Enumerable.Empty<Element>())
            {
                if (element == null) continue;

                string structure = ResolveStructureElementName(element);
                if (string.IsNullOrWhiteSpace(structure)) structure = "Unassigned";

                string level = ResolveGroupName(doc, element, BuildingLevelParamName);
                if (string.IsNullOrWhiteSpace(level)) level = "Unassigned";

                string building = ResolveGroupName(doc, element, StructuralPlanParamName);
                if (string.IsNullOrWhiteSpace(building)) building = "Unassigned";

                string type = GetTypeName(doc, element);
                if (string.IsNullOrWhiteSpace(type)) type = "(No Type)";
                string elementType = ResolveElementTypeName(doc, element);
                if (string.IsNullOrWhiteSpace(elementType)) elementType = type;

                double pReinf = ReadMetricPercent(element, SiteProgressReinforcementParamName);
                double pVol = ReadMetricPercent(element, SiteProgressVolumeParamName);
                double hostFormworkPercent = ReadMetricPercent(element, SiteProgressFormworkParamName);

                double reinforcementBoq = GetReinforcementKg(element);
                double volumeBoq = GetVolumeBoq(element);
                double reinforcementCompleted = reinforcementBoq * pReinf / 100.0;
                ResolveElementFormworkContribution(
                    element,
                    hostFormworkPercent,
                    formworkByHost,
                    out double formworkBoq,
                    out double formworkCompleted,
                    out double pForm);
                double volumeCompleted = volumeBoq * pVol / 100.0;
                double progress = ResolveOverallPercent(pReinf, pForm, pVol, null);

                rows.Add(new SiteProgressElementDetailRow
                {
                    ElementId = (int)element.Id.Value,
                    UniqueId = element.UniqueId ?? "",
                    StructuralPlan = building,
                    BuildingLevel = level,
                    StructureElement = structure,
                    Category = element.Category?.Name ?? "",
                    FamilyName = GetFamilyName(doc, element),
                    TypeName = elementType,
                    ReinforcementKg = reinforcementBoq,
                    FormworkM2 = formworkBoq,
                    VolumeM3 = volumeBoq,
                    SiteProgressReinforcementPercent = pReinf,
                    SiteProgressFormworkPercent = pForm,
                    SiteProgressVolumePercent = pVol,
                    ReinforcementCompletedKg = reinforcementCompleted,
                    FormworkCompletedM2 = formworkCompleted,
                    VolumeCompletedM3 = volumeCompleted,
                    ProgressPercent = progress,
                    Status = BuildStatus(pReinf, pForm, pVol)
                });
            }

            return rows
                .OrderBy(r => r.StructuralPlan, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => GetStructureOrder(r.StructureElement))
                .ThenBy(r => r.StructureElement, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ElementId)
                .ToList();
        }

        private static string GetFamilyName(Document doc, Element element)
        {
            if (element is FamilyInstance familyInstance)
            {
                if (!string.IsNullOrWhiteSpace(familyInstance.Symbol?.FamilyName))
                {
                    return familyInstance.Symbol.FamilyName.Trim();
                }
            }

            if (doc != null && element != null)
            {
                ElementId typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    ElementType type = doc.GetElement(typeId) as ElementType;
                    if (type != null && !string.IsNullOrWhiteSpace(type.FamilyName))
                    {
                        return type.FamilyName.Trim();
                    }
                }
            }

            return "";
        }

        private static string ResolveGroupName(Document doc, Element element, string groupParamName)
        {
            if (doc == null || element == null) return "";

            string fromShared = ReadParameterDisplayString(doc, element, groupParamName);
            if (!string.IsNullOrWhiteSpace(fromShared))
            {
                return fromShared.Trim();
            }

            string alternateParamName = string.Equals(groupParamName, StructuralPlanParamName, StringComparison.OrdinalIgnoreCase)
                ? BuildingLevelParamName
                : StructuralPlanParamName;
            string alternate = ReadParameterDisplayString(doc, element, alternateParamName);
            if (!string.IsNullOrWhiteSpace(alternate))
            {
                return alternate.Trim();
            }

            ElementId levelId;
            if (TryGetLevelId(element, out levelId) && levelId != ElementId.InvalidElementId)
            {
                Level level = doc.GetElement(levelId) as Level;
                if (level != null && !string.IsNullOrWhiteSpace(level.Name))
                {
                    return level.Name.Trim();
                }
            }

            foreach (string name in new[] { "Base Constraint", "Base Level", "Reference Level", "Level" })
            {
                Parameter p = element.LookupParameter(name);
                if (p == null) continue;

                if (p.StorageType == StorageType.String)
                {
                    string value = p.AsString();
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
                else if (p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    Level level = doc.GetElement(id) as Level;
                    if (level != null && !string.IsNullOrWhiteSpace(level.Name))
                    {
                        return level.Name.Trim();
                    }
                }
            }

            return "";
        }

        private static string ReadParameterDisplayString(Document doc, Element element, string parameterName)
        {
            if (doc == null || element == null || string.IsNullOrWhiteSpace(parameterName)) return "";

            Parameter p = element.LookupParameter(parameterName);
            if (p == null) return "";

            if (p.StorageType == StorageType.String)
            {
                return p.AsString() ?? "";
            }

            if (p.StorageType == StorageType.ElementId)
            {
                ElementId id = p.AsElementId();
                if (id == null || id == ElementId.InvalidElementId) return "";

                Level level = doc.GetElement(id) as Level;
                if (level != null && !string.IsNullOrWhiteSpace(level.Name))
                {
                    return level.Name;
                }

                string value = p.AsValueString();
                return value ?? "";
            }

            return p.AsValueString() ?? "";
        }

        private static string ResolveStructureElementName(Element element)
        {
            if (element?.Category?.Id == null) return "";

            long categoryId = element.Category.Id.Value;
            if (categoryId == (int)BuiltInCategory.OST_StructuralFoundation) return "Structural Foundation";
            if (categoryId == (int)BuiltInCategory.OST_StructuralColumns) return "Structural Column";
            if (categoryId == (int)BuiltInCategory.OST_Walls) return "Wall";
            if (categoryId == (int)BuiltInCategory.OST_StructuralFraming) return "Structural Framing";
            if (categoryId == (int)BuiltInCategory.OST_Floors) return "Floor";
            if (categoryId == (int)BuiltInCategory.OST_Stairs) return "Stair";

            return element.Category.Name ?? "";
        }

        private static bool IsSummaryCategory(Category category)
        {
            if (category?.Id == null) return false;
            long categoryId = category.Id.Value;
            foreach (BuiltInCategory bic in SummaryCategories)
            {
                if (categoryId == (int)bic) return true;
            }

            return false;
        }

        private static int GetStructureOrder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 999;

            if (string.Equals(name, "Structural Foundation", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(name, "Structural Column", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(name, "Wall", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(name, "Structural Framing", StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(name, "Floor", StringComparison.OrdinalIgnoreCase)) return 5;
            if (string.Equals(name, "Stair", StringComparison.OrdinalIgnoreCase)) return 6;

            return 999;
        }

        private static bool TryGetLevelId(Element element, out ElementId levelId)
        {
            levelId = ElementId.InvalidElementId;
            if (element == null) return false;

            foreach (BuiltInParameter bip in new[]
                     {
                         BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
                         BuiltInParameter.WALL_BASE_CONSTRAINT,
                         BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                         BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
                         BuiltInParameter.LEVEL_PARAM,
                         BuiltInParameter.SCHEDULE_LEVEL_PARAM
                     })
            {
                Parameter p = element.get_Parameter(bip);
                if (p == null || p.StorageType != StorageType.ElementId) continue;

                ElementId id = p.AsElementId();
                if (id == null || id == ElementId.InvalidElementId) continue;

                levelId = id;
                return true;
            }

            return false;
        }

        private static double GetElementWeight(Element element)
        {
            if (element == null) return 1.0;

            Parameter pVol = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (pVol != null && pVol.StorageType == StorageType.Double)
            {
                double v = pVol.AsDouble();
                if (v > 1e-9) return v;
            }

            Parameter pArea = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (pArea != null && pArea.StorageType == StorageType.Double)
            {
                double a = pArea.AsDouble();
                if (a > 1e-9) return a;
            }

            Parameter pLen = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (pLen != null && pLen.StorageType == StorageType.Double)
            {
                double l = pLen.AsDouble();
                if (l > 1e-9) return l;
            }

            if (element.Location is LocationCurve lc && lc.Curve != null)
            {
                double l = lc.Curve.Length;
                if (l > 1e-9) return l;
            }

            return 1.0;
        }

        private static double GetVolumeBoq(Element element)
        {
            if (element == null) return 0.0;
            Parameter pVol = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (pVol != null && pVol.StorageType == StorageType.Double)
            {
                double v = pVol.AsDouble();
                if (v > 1e-9) return v;
            }

            return 0.0;
        }

        private static double GetFormworkBoq(Element element)
        {
            if (element == null) return 0.0;
            double sharedArea = GetParamDoubleByName(element, FormworkAreaParamName);
            if (sharedArea > 1e-9)
            {
                return sharedArea;
            }

            Parameter pArea = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (pArea != null && pArea.StorageType == StorageType.Double)
            {
                double a = pArea.AsDouble();
                if (a > 1e-9) return a;
            }

            return 0.0;
        }

        private static double GetReinforcementKg(Element element)
        {
            if (element == null) return 0.0;
            return GetParamDoubleByName(element,
                "Reinforcement(Kg)",
                "Reinforcement (Kg)",
                "Reinforcement Kg",
                "Rebar Weight",
                "RebarWeight",
                "Weight");
        }

        private static double GetParamDoubleByName(Element element, params string[] names)
        {
            if (element == null || names == null) return 0.0;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                Parameter p = element.LookupParameter(name);
                if (p == null) continue;
                if (p.StorageType == StorageType.Double)
                {
                    double d = p.AsDouble();
                    if (Math.Abs(d) > 1e-9) return d;
                }
                else if (p.StorageType == StorageType.Integer)
                {
                    int i = p.AsInteger();
                    if (i != 0) return i;
                }
                else if (p.StorageType == StorageType.String)
                {
                    double parsed;
                    if (TryParsePercent(p.AsString(), out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return 0.0;
        }

        private static string GetTypeName(Document doc, Element element)
        {
            if (doc == null || element == null) return "";

            foreach (string alias in StructureTypeParameterAliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                Parameter typeParameter = element.LookupParameter(alias);
                if (typeParameter == null)
                {
                    continue;
                }

                if (typeParameter.StorageType == StorageType.String)
                {
                    string fromParameter = (typeParameter.AsString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(fromParameter))
                    {
                        return fromParameter;
                    }
                }
                else
                {
                    string fromDisplay = (typeParameter.AsValueString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(fromDisplay))
                    {
                        return fromDisplay;
                    }
                }
            }

            string name = element.Name ?? "";
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();

            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                ElementType t = doc.GetElement(typeId) as ElementType;
                if (t != null && !string.IsNullOrWhiteSpace(t.Name))
                {
                    return t.Name.Trim();
                }
            }

            return "";
        }

        private static string ResolveElementTypeName(Document doc, Element element)
        {
            if (doc == null || element == null)
            {
                return "";
            }

            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                ElementType type = doc.GetElement(typeId) as ElementType;
                if (type != null && !string.IsNullOrWhiteSpace(type.Name))
                {
                    return type.Name.Trim();
                }
            }

            string name = (element.Name ?? "").Trim();
            return name;
        }

        private static double ResolveOverallPercent(double pReinf, double pForm, double pVol, SiteProgressDetailAggregate agg)
        {
            if (agg != null)
            {
                double w = 0.0;
                double sum = 0.0;
                if (agg.ReinforcementKg > 1e-9) { w += agg.ReinforcementKg; sum += agg.ReinforcementCompletedKg; }
                if (agg.FormworkM2 > 1e-9) { w += agg.FormworkM2; sum += agg.FormworkCompletedM2; }
                if (agg.VolumeM3 > 1e-9) { w += agg.VolumeM3; sum += agg.VolumeCompletedM3; }
                if (w > 1e-9) return ClampPercent(sum * 100.0 / w);
            }

            if (pVol > 0) return ClampPercent(pVol);
            if (pForm > 0) return ClampPercent(pForm);
            return ClampPercent(pReinf);
        }

        private static string BuildStatus(double pReinf, double pForm, double pVol)
        {
            double max = Math.Max(pReinf, Math.Max(pForm, pVol));
            double min = Math.Min(pReinf, Math.Min(pForm, pVol));
            if (max <= 0.0) return "Not Started";
            if (min >= 99.99 && max >= 99.99) return "Completed";
            return "In Progress";
        }

        private static bool TryReadProgressPercent(Parameter p, out double percent)
        {
            percent = 0.0;
            if (p == null) return false;

            if (p.StorageType == StorageType.Double)
            {
                double raw = p.AsDouble();
                bool showPercent = IsDisplayPercent(p);
                percent = showPercent ? (raw * 100.0) : raw;
                return true;
            }

            if (p.StorageType == StorageType.Integer)
            {
                percent = p.AsInteger();
                return true;
            }

            if (p.StorageType == StorageType.String)
            {
                return TryParsePercent(p.AsString(), out percent);
            }

            return false;
        }

        private static double ReadMetricPercent(Element element, string metricParamName)
        {
            if (element == null) return 0.0;

            Parameter metric = element.LookupParameter(metricParamName);
            double metricPercent = 0.0;
            bool hasMetric = TryReadProgressPercent(metric, out metricPercent);
            bool metricHasValue = HasExplicitProgressValue(metric);

            Parameter legacy = element.LookupParameter(SiteProgressParamName);
            double legacyPercent = 0.0;
            bool hasLegacy = TryReadProgressPercent(legacy, out legacyPercent);
            bool legacyHasValue = HasExplicitProgressValue(legacy);

            if (hasMetric && metricHasValue)
            {
                // Backward compatibility for migrated models:
                // If dedicated volume parameter is effectively empty/0 but legacy %Site_Progress has a value,
                // use legacy value so existing per-element progress is detected correctly.
                if (string.Equals(metricParamName, SiteProgressVolumeParamName, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(metricPercent) <= 1e-9 &&
                    hasLegacy &&
                    legacyHasValue &&
                    legacyPercent > 0.0)
                {
                    return ClampPercent(legacyPercent);
                }

                return ClampPercent(metricPercent);
            }

            if (hasLegacy && legacyHasValue)
            {
                return ClampPercent(legacyPercent);
            }

            if (hasMetric)
            {
                return ClampPercent(metricPercent);
            }

            if (hasLegacy)
            {
                return ClampPercent(legacyPercent);
            }

            return 0.0;
        }

        private static bool HasExplicitProgressValue(Parameter p)
        {
            if (p == null) return false;
            try
            {
                if (!p.HasValue) return false;
            }
            catch
            {
                // Some parameter implementations may not support HasValue reliably.
            }

            if (p.StorageType == StorageType.String)
            {
                string raw = p.AsString();
                if (!string.IsNullOrWhiteSpace(raw)) return true;
                string display = p.AsValueString();
                return !string.IsNullOrWhiteSpace(display);
            }

            return true;
        }

        private static bool TryApplyMetricPercent(Element element, string paramName, double inputPercent, bool addMode, ref bool hadWritable)
        {
            if (element == null || string.IsNullOrWhiteSpace(paramName)) return false;
            Parameter p = element.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;

            hadWritable = true;
            double currentPercent = 0.0;
            TryReadProgressPercent(p, out currentPercent);
            double targetPercent = addMode ? (currentPercent + inputPercent) : inputPercent;
            targetPercent = ClampPercent(targetPercent);
            return TrySetProgressPercent(p, targetPercent);
        }

        private static bool TrySetTextParameter(Element element, string paramName, string value)
        {
            if (element == null || string.IsNullOrWhiteSpace(paramName))
            {
                return false;
            }

            Parameter parameter = element.LookupParameter(paramName);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                return false;
            }

            string current = (parameter.AsString() ?? "").Trim();
            string next = (value ?? "").Trim();
            if (string.Equals(current, next, StringComparison.Ordinal))
            {
                return false;
            }

            return parameter.Set(next);
        }

        private static Definition EnsureSiteProgressDefinition(DefinitionGroup group, string name, CamboBIMWindow window)
        {
            if (group == null || string.IsNullOrWhiteSpace(name)) return null;
            Definition def = group.Definitions.get_Item(name);
            if (def == null)
            {
                var options = new ExternalDefinitionCreationOptions(name, SpecTypeId.Number)
                {
                    Visible = true
                };
                def = group.Definitions.Create(options);
            }

            if (def == null)
            {
                window?.ShowStatus($"Site Progress: cannot create {name}.");
            }

            return def;
        }

        private static Definition EnsureSiteProgressTextDefinition(DefinitionGroup group, string name, CamboBIMWindow window)
        {
            if (group == null || string.IsNullOrWhiteSpace(name)) return null;
            Definition def = group.Definitions.get_Item(name);
            if (def == null)
            {
                var options = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text)
                {
                    Visible = true
                };
                def = group.Definitions.Create(options);
            }

            if (def == null)
            {
                window?.ShowStatus($"Site Progress: cannot create {name}.");
            }

            return def;
        }

        private static void EnsureDefinitionBinding(
            Autodesk.Revit.ApplicationServices.Application app,
            BindingMap map,
            Definition def,
            CategorySet set)
        {
            if (app == null || map == null || def == null || set == null || set.IsEmpty) return;

            ElementBinding existing = map.get_Item(def) as ElementBinding;
            if (existing == null)
            {
                InstanceBinding binding = app.Create.NewInstanceBinding(set);
                map.Insert(def, binding, GroupTypeId.Data);
                return;
            }

            foreach (Category c in existing.Categories)
            {
                if (c != null) set.Insert(c);
            }

            InstanceBinding rebind = app.Create.NewInstanceBinding(set);
            map.ReInsert(def, rebind, GroupTypeId.Data);
        }

        private static bool TrySetProgressPercent(Parameter p, double targetPercent)
        {
            if (p == null || p.IsReadOnly) return false;

            if (p.StorageType == StorageType.Double)
            {
                double currentRaw = p.AsDouble();
                bool showPercent = IsDisplayPercent(p);
                double targetRaw = showPercent ? (targetPercent / 100.0) : targetPercent;
                if (Math.Abs(currentRaw - targetRaw) <= 1e-8)
                {
                    return false;
                }

                return p.Set(targetRaw);
            }

            if (p.StorageType == StorageType.Integer)
            {
                int target = (int)Math.Round(targetPercent);
                if (p.AsInteger() == target)
                {
                    return false;
                }

                return p.Set(target);
            }

            if (p.StorageType == StorageType.String)
            {
                string target = targetPercent.ToString("0.##", CultureInfo.InvariantCulture);
                string current = p.AsString() ?? "";
                if (string.Equals(current.Trim(), target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current.Trim().TrimEnd('%').Trim(), target, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return p.Set(target);
            }

            return false;
        }

        private static bool IsDisplayPercent(Parameter p)
        {
            if (p == null) return false;
            try
            {
                string value = p.AsValueString();
                return !string.IsNullOrWhiteSpace(value) &&
                       value.IndexOf("%", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParsePercent(string raw, out double percent)
        {
            percent = 0.0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string normalized = raw.Trim().TrimEnd('%').Trim();
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
            {
                return true;
            }

            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out percent);
        }

        private static double ClampPercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 100.0) return 100.0;
            return value;
        }

        private static string GetScopeLabel(QsScope scope)
        {
            if (scope == QsScope.CurrentSelection) return "Current Selection";
            if (scope == QsScope.CurrentView) return "Current View";
            return "Entire Model";
        }

        private sealed class SiteProgressAggregate
        {
            public int ElementCount { get; set; }
            public double WeightTotal { get; set; }
            public double WeightedPercentSum { get; set; }
        }

        private sealed class SiteProgressDetailAggregate
        {
            public string StructureElement { get; set; } = "";
            public string BuildingLevel { get; set; } = "";
            public string TypeName { get; set; } = "";
            public string ElementTypeName { get; set; } = "";
            public int ElementCount { get; set; }
            public double ReinforcementKg { get; set; }
            public double FormworkM2 { get; set; }
            public double VolumeM3 { get; set; }
            public double ReinforcementCompletedKg { get; set; }
            public double FormworkCompletedM2 { get; set; }
            public double VolumeCompletedM3 { get; set; }
            public HashSet<int> ElementIds { get; } = new HashSet<int>();
        }

        private sealed class FormworkShapeAggregate
        {
            public double FormworkM2 { get; set; }
            public double CompletedM2 { get; set; }
            public double CoveredAreaM2 { get; set; }
        }
    }
}
