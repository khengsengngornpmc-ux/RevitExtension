using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal enum MhnkArcSourceMode
    {
        FreeSelect,
        Category,
        ByLayer,
        All
    }

    [Transaction(TransactionMode.Manual)]
    public abstract class MhnkArchitectureToolCommand : IExternalCommand
    {
        private const double ShortCurveTolerance = 0.01;

        protected abstract string ToolName { get; }
        protected abstract string MainInstruction { get; }
        protected abstract IList<MhnkArcCommandOption> BuildOptions(MhnkArcContext context);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string autodeskUserId = commandData?.Application?.Application?.LoginUserId ?? "";
            if (!App.EnsureLicenseActivated(out string licenseFailure, autodeskUserId))
            {
                if (!string.IsNullOrWhiteSpace(licenseFailure))
                {
                    TaskDialog.Show("MHNK License", licenseFailure);
                }

                return Result.Cancelled;
            }

            if (commandData?.Application?.ActiveUIDocument == null)
            {
                TaskDialog.Show("MHNK " + ToolName, "Open a Revit model before running this tool.");
                return Result.Cancelled;
            }

            var context = new MhnkArcContext(commandData);
            IList<MhnkArcCommandOption> options = BuildOptions(context);
            if (options == null || options.Count == 0)
            {
                TaskDialog.Show("MHNK " + ToolName, "No actions are available for the current Revit context.");
                return Result.Cancelled;
            }

            try
            {
                MhnkArcWorkspaceController.Show(options, ToolName, commandData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                MhnkLogger.Error("ARC " + ToolName + " workspace failed.", ex);
                message = ex.Message;
                TaskDialog.Show("MHNK " + ToolName, ex.Message);
                return Result.Failed;
            }
        }

        protected static IList<MhnkArcCommandOption> BuildFullOptions()
        {
            return new List<MhnkArcCommandOption>
            {
                Ready("Filter", "Select by Category", "Select visible elements matching the first selected element category.", SelectSameCategoryAsSelection),
                Ready("Filter", "Select by Family / Type", "Select visible elements matching the first selected element type.", SelectSameTypeAsSelection),
                Ready("Filter", "Select by Level", "Select visible elements by selected/active level.", SelectByLevel),
                Ready("Filter", "Select by Workset", "Select visible elements by selected element workset.", SelectByWorkset),
                Ready("Filter", "Select by Phase", "Select visible elements by selected element created phase.", SelectByPhase),
                Ready("Filter", "Select by Parameter", "Select visible elements matching a useful seed parameter.", SelectByParameter),
                Ready("Filter", "Select by Material", "Select visible elements sharing material with the seed element.", SelectByMaterial),
                Ready("Filter", "Select by CAD Layer", "Report CAD layers from selected imports and select visible CAD imports.", SelectByCadLayer),
                Ready("Filter", "Select MEP Elements in ARC View", "Select visible MEP coordination elements in the active view.", SelectMepElementsInActiveView),
                Ready("Filter", "Isolate Selection", "Temporarily isolate selected elements.", IsolateCurrentSelection),
                Ready("Filter", "Hide Selection", "Temporarily hide selected elements.", HideCurrentSelection),
                Ready("Filter", "Reset Temporary View", "Clear active view temporary hide/isolate.", ResetTemporaryHideIsolate),
                Ready("Filter", "Apply Color Review", "Apply ARC review colors to visible ARC/MEP categories.", ApplyArcColorReview),
                Ready("Filter", "Save / Load Filter Preset", "Save current selection preset, or load the last preset when nothing is selected.", SaveOrLoadFilterPreset),

                Ready("Creation", "CAD to Model Manager", "Scan CAD layers, mapping rules, and runnable creation actions before building.", ShowCadToModelManager),
                Ready("Creation", "Create Walls by Room", "Create walls from selected or visible room boundary segments.", CreateWallsFromRooms),
                Ready("Creation", "Create Floors by Room", "Create one floor per selected or visible bounded room.", CreateFloorsFromRooms),
                Ready("Creation", "Create Ceilings by Room", "Create one ceiling per selected or visible bounded room.", CreateCeilingsFromRooms),
                Ready("Creation", "Create Multiple Ceilings", "Create ceilings from all selected/reviewed bounded rooms, grouped by level.", CreateCeilingsFromRooms),
                Ready("Creation", "CAD to Walls", "Create basic walls from selected CAD/model/detail curves.", CreateWallsFromSelectedCurves),
                Ready("Creation", "CAD to Floors", "Create floors from closed selected model/detail curve boundaries.", CreateFloorsFromSelectedCurves),
                Ready("Creation", "CAD to Ceilings", "Create ceilings from closed selected model/detail curve boundaries.", CreateCeilingsFromSelectedCurves),
                Ready("Creation", "CAD to Rooms", "Create rooms at the centroid of selected closed boundaries.", CreateRoomsFromSelectedCurves),
                Ready("Creation", "CAD to Room Boundaries", "Create room boundary lines from selected curves.", CreateRoomBoundaryLinesFromSelectedCurves),
                Ready("Creation", "CAD to Openings", "Create MHNK opening candidate solids from selected closed boundaries.", CreateOpeningCandidatesFromSelectedCurves),
                Ready("Creation", "CAD to Doors / Windows", "Place loaded non-hosted door/window symbols at selected curve midpoints.", PlaceDoorWindowCandidatesFromSelectedCurves),
                Ready("Creation", "Doors / Windows by Wall Side", "Place hosted doors/windows on the nearest wall and orient them toward the selected marker side.", PlaceDoorWindowCandidatesByWallSide),
                Ready("Creation", "Doors / Windows by Position", "Place hosted doors/windows at selected positions on the nearest wall.", PlaceDoorWindowCandidatesByPosition),
                Ready("Creation", "Create ARC 3D View", "Create and open a fine-detail ARC coordination view.", CreateArcCoordinationView),
                Ready("Creation", "Create Drafting View", "Create and open an ARC drafting view.", CreateArcDraftingView),
                Ready("Creation", "Create Working Plans", "Create coordinated ARC working plan views for project levels.", CreateWorkingPlans),
                Ready("Creation", "Create External Wall Finishes", "Create exterior finish wall layers from selected or visible host walls.", CreateWallFinishCandidates),
                Ready("Creation", "Create Internal Wall Finishes", "Create interior finish wall layers from selected or visible host walls.", CreateInternalWallFinishCandidates),
                Ready("Creation", "Create Floor Finishes", "Generate a floor-finish candidate report from rooms and floors.", CreateFloorFinishCandidates),
                Ready("Creation", "Create Ceiling Finishes", "Generate a ceiling-finish candidate report from rooms and ceilings.", CreateCeilingFinishCandidates),
                Ready("Creation", "Create Model Group from Selection", "Create a Revit model group from the selected elements.", CreateModelGroupFromSelection),

                Ready("Edition", "Pin Selection", "Pin selected model elements.", PinCurrentSelection),
                Ready("Edition", "Unpin Selection", "Unpin selected model elements.", UnpinCurrentSelection),
                Ready("Edition", "Toggle Join / Unjoin", "Join or unjoin the first two selected model elements.", ToggleJoinFirstTwoSelected),
                Ready("Edition", "Join Selected Pairs", "Join all intersecting selected model element pairs where Revit allows it.", JoinSelectedIntersectingPairs),
                Ready("Edition", "Unjoin Selected Pairs", "Unjoin all joined pairs found inside the current selection.", UnjoinSelectedJoinedPairs),
                Ready("Edition", "Cut / Uncut", "Cut or uncut the first two selected solid-capable elements.", ToggleSolidCutFirstTwoSelected),
                Ready("Edition", "Cut / Uncut Selected Pairs", "Toggle solid cuts for intersecting selected pairs where Revit allows it.", ToggleSolidCutSelectedPairs),
                Ready("Edition", "Cut Selected by First", "Use the first selected solid-capable element to cut the other selected elements.", CutSelectedByFirstElement),
                Ready("Edition", "Uncut Selected Pairs", "Remove solid cuts found between selected element pairs.", UncutSelectedSolidPairs),
                Ready("Edition", "Switch Join Order", "Switch join order for the first two selected joined elements.", SwitchJoinOrderFirstTwoSelected),
                Ready("Edition", "Switch Join Order Selected", "Switch join order for every joined pair found inside the current selection.", SwitchJoinOrderSelectedPairs),
                Ready("Edition", "Set Room Bounding On", "Turn on Room Bounding where selected elements support it.", SetRoomBoundingOn),
                Ready("Edition", "Set Room Bounding Off", "Turn off Room Bounding where selected elements support it.", SetRoomBoundingOff),
                Ready("Edition", "Batch Type Change", "Change selected elements to the first selected element type.", BatchTypeChangeFromSeed),
                Ready("Edition", "Split Walls Horizontally", "Split selected or visible walls at a height and apply a new wall type to one segment.", SplitWallsHorizontally),
                Ready("Edition", "Lower Walls to Ceilings", "Lower selected or visible wall tops to matching ceiling heights.", LowerWallsToCeilings),
                Ready("Edition", "Batch Level Change", "Move selected elements to the selected/active level where supported.", BatchLevelChange),
                Ready("Edition", "Batch Offset Change", "Copy the first selected offset value to the rest of the selection.", BatchOffsetChange),
                Ready("Edition", "Batch Parameter Edit", "Copy Mark/Comments style values from the first selected element.", BatchParameterEdit),
                Ready("Edition", "Align Elements", "Align selected element centers to the first selected element center.", AlignElementsToSeedCenter),
                Ready("Edition", "Copy / Mirror / Array Presets", "Create a one-step 1m offset copy of the current selection.", CopyArrayPreset),
                Ready("Edition", "Clean Duplicate Elements", "Select duplicate candidates for review without deleting them.", SelectDuplicateCandidates),
                Ready("Edition", "Rename Views / Sheets", "Prefix selected views/sheets with MHNK where supported.", RenameSelectedViewsSheets),
                Ready("Edition", "Reset Graphic Overrides", "Clear active view overrides for selected or visible ARC elements.", ResetGraphicOverrides),

                Ready("Solids", "Solids Interaction Center", "Run eTLipse-style retrieve, check, join, cut, switch, review, and CSV workflows.", ShowSolidsInteractionCenter),
                Ready("Solids", "Create Bounding Solids", "Generate DirectShape bounding boxes around selected elements.", CreateBoundingSolidsFromSelection),
                Ready("Solids", "Select MHNK Solids", "Select MHNK generated solids in the active view.", SelectMhnkGeneratedSolids),
                Ready("Solids", "Solid Volume Report", "Report selected solid volume.", ReportSelectedSolidMetrics),
                Ready("Solids", "Solid Area Report", "Report selected solid surface area.", ReportSelectedSolidMetrics),
                Ready("Solids", "Intersection Check", "Find intersecting selected/visible ARC coordination elements.", IntersectionCheck),
                Ready("Solids", "Clash Candidate Check", "Find probable ARC/MEP clash pairs for review.", ClashCandidateCheck),
                Ready("Solids", "Opening Candidate Check", "Find wall/floor opening candidates from MEP intersections.", OpeningCandidateCheck),
                Ready("Solids", "Cut Host by Solid", "Toggle solid cut between the first two selected elements where Revit allows it.", ToggleSolidCutFirstTwoSelected),
                Ready("Solids", "Cut / Uncut Selected Pairs", "Toggle solid cuts for intersecting selected pairs where Revit allows it.", ToggleSolidCutSelectedPairs),
                Ready("Solids", "Cut Selected by First", "Use the first selected solid-capable element to cut all other selected solid-capable targets.", CutSelectedByFirstElement),
                Ready("Solids", "Uncut Selected Pairs", "Remove solid cuts found between selected element pairs.", UncutSelectedSolidPairs),
                Ready("Solids", "Convert Solid to DirectShape", "Convert selected solid geometry into MHNK DirectShape copies.", ConvertSelectedSolidsToDirectShape),
                Ready("Solids", "Color Solid Review", "Apply ARC review colors to generated/visible elements.", ApplyArcColorReview),
                Ready("Solids", "Delete MHNK Generated Solids", "Delete generated MHNK solids from the active model.", DeleteMhnkGeneratedSolids),
                Ready("Solids", "Export Solid Report", "Export solid quantity/review results.", ExportSolidReport),

                Ready("Xpress", "Model Health Report", "Show warnings, CAD imports, views, sheets, and ARC element counts.", ModelHealthReport),
                Ready("Xpress", "QA Dashboard", "Review model health, CAD imports, warnings, required types, and mapping rules.", ShowArcQaDashboard),
                Ready("Xpress", "Workflow Validation Center", "Record per-tool eTLipse parity results and export an HTML validation report.", ShowArcWorkflowValidationCenter),
                Ready("Xpress", "CAD to Model Manager", "Scan selected CAD and run mapped creation tools.", ShowCadToModelManager),
                Ready("Xpress", "Tool Settings", "Configure ARC CAD layer mapping, type hints, offsets, and clash tolerance.", ShowArcToolSettings),
                Ready("Xpress", "Mapping Manager", "Edit CAD layer to Revit action/type rules.", ShowSmartMappingManager),
                Ready("Xpress", "Select CAD Imports", "Select CAD imports visible in the active view.", SelectCadImportsInActiveView),
                Ready("Xpress", "Clean CAD Imports", "Select visible CAD imports, or temporarily hide selected CAD imports.", CleanCadImports),
                Ready("Xpress", "Prepare ARC Workspace", "Create a coordination view and show model health.", PrepareArcCoordinationWorkspace),
                Ready("Xpress", "Create Coordination View", "Create and open an ARC coordination 3D view.", CreateArcCoordinationView),
                Ready("Xpress", "Check Warnings", "Show the current model warning report.", ShowWarningsReport),
                Ready("Xpress", "Check Missing Types", "Check for missing required ARC family/system types.", CheckMissingTypes),
                Ready("Xpress", "Check Unjoined Walls", "Find wall join warnings and walls with no joined neighbors.", CheckUnjoinedWalls),
                Ready("Xpress", "Check Room Boundary Issues", "Find likely room boundary problems.", CheckRoomBoundaryIssues),
                Ready("Xpress", "Check Openings", "Find opening coordination issues.", CheckOpenings),
                Ready("Xpress", "Auto Join ARC Elements", "Run automatic ARC join on selected or visible candidates.", AutoJoinArcElements),
                Ready("Xpress", "Auto Color Review", "Apply ARC review colors to visible categories.", ApplyArcColorReview),
                Ready("Xpress", "Toggle Dark Theme", "Switch MHNK ARC plugin windows between light and dark theme.", ToggleDarkTheme),
                Ready("Xpress", "Diagnostics Report", "Show deployment and support diagnostics.", ShowDiagnosticsReport),
                Ready("Xpress", "Export QA Report", "Export model QA results.", ExportQaReport)
            };
        }

        private static MhnkArcCommandOption Ready(
            string category,
            string title,
            string summary,
            Func<MhnkArcContext, Result> run)
        {
            return new MhnkArcCommandOption(category, title, summary, run, true, "Ready");
        }

        private static MhnkArcCommandOption Planned(string category, string title, string summary)
        {
            return new MhnkArcCommandOption(
                category,
                title,
                summary,
                context => ShowPlannedFeature(category, title),
                false,
                "Next");
        }

        protected static Result SelectArcCoordinationElements(MhnkArcContext context)
        {
            IList<BuiltInCategory> categories = GetArcCoordinationCategories();
            var filter = new ElementMulticategoryFilter(categories);
            ICollection<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(filter)
                .ToElementIds();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " ARC coordination element(s) in the active view.");
            return Result.Succeeded;
        }

        protected static Result SelectSameCategoryAsSelection(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed?.Category == null)
            {
                ShowResult("MHNK Filter", "Select one element first, then run Select by Category.");
                return Result.Cancelled;
            }

            ElementId categoryId = seed.Category.Id;
            IList<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(x => x.Category != null && x.Category.Id == categoryId)
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible element(s) in category: " + seed.Category.Name);
            return Result.Succeeded;
        }

        protected static Result SelectSameTypeAsSelection(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed == null)
            {
                ShowResult("MHNK Filter", "Select one element first, then run Select by Family / Type.");
                return Result.Cancelled;
            }

            ElementId typeId = seed.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                ShowResult("MHNK Filter", "The selected element does not have a type id.");
                return Result.Cancelled;
            }

            IList<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(x => x.GetTypeId() == typeId)
                .Select(x => x.Id)
                .ToList();

            Element typeElement = context.Document.GetElement(typeId);
            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible element(s) matching type: " + (typeElement?.Name ?? typeId.Value.ToString()));
            return Result.Succeeded;
        }

        protected static Result SelectByLevel(MhnkArcContext context)
        {
            Level level = GetSeedOrActiveLevel(context);
            if (level == null)
            {
                ShowResult("MHNK Filter", "Select an element/level first, or open a plan view with a level.");
                return Result.Cancelled;
            }

            IList<ElementId> ids = GetVisibleElements(context)
                .Where(x => ElementIdEquals(GetElementLevelId(x), level.Id))
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible element(s) on level: " + level.Name);
            return Result.Succeeded;
        }

        protected static Result SelectByWorkset(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed == null)
            {
                ShowResult("MHNK Filter", "Select one element first, then run Select by Workset.");
                return Result.Cancelled;
            }

            WorksetId worksetId = seed.WorksetId;
            IList<ElementId> ids = GetVisibleElements(context)
                .Where(x => x.WorksetId != null && x.WorksetId.Equals(worksetId))
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible element(s) on workset: " + GetWorksetName(context.Document, worksetId));
            return Result.Succeeded;
        }

        protected static Result SelectByPhase(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed == null)
            {
                ShowResult("MHNK Filter", "Select one element first, then run Select by Phase.");
                return Result.Cancelled;
            }

            ElementId phaseId = GetCreatedPhaseId(seed);
            if (phaseId == ElementId.InvalidElementId)
            {
                ShowResult("MHNK Filter", "The selected element does not expose a created phase.");
                return Result.Cancelled;
            }

            IList<ElementId> ids = GetVisibleElements(context)
                .Where(x => ElementIdEquals(GetCreatedPhaseId(x), phaseId))
                .Select(x => x.Id)
                .ToList();

            Element phase = context.Document.GetElement(phaseId);
            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible element(s) created in phase: " + (phase?.Name ?? phaseId.Value.ToString()));
            return Result.Succeeded;
        }

        protected static Result SelectByParameter(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed == null)
            {
                ShowResult("MHNK Filter", "Select one seed element first, then run Select by Parameter.");
                return Result.Cancelled;
            }

            ParameterSignature signature = GetBestParameterSignature(seed);
            if (signature == null)
            {
                ShowResult("MHNK Filter", "No useful readable parameter value was found on the selected seed element.");
                return Result.Cancelled;
            }

            IList<ElementId> ids = GetVisibleElements(context)
                .Where(x => ParameterMatches(x, signature))
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult(
                "MHNK Filter",
                "Selected " + ids.Count + " visible element(s)." + Environment.NewLine +
                "Parameter: " + signature.Name + Environment.NewLine +
                "Value: " + signature.DisplayValue);
            return Result.Succeeded;
        }

        protected static Result SelectByMaterial(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed == null)
            {
                ShowResult("MHNK Filter", "Select one seed element first, then run Select by Material.");
                return Result.Cancelled;
            }

            HashSet<string> materialIds = new HashSet<string>(
                seed.GetMaterialIds(false).Select(GetElementIdText),
                StringComparer.OrdinalIgnoreCase);

            if (materialIds.Count == 0)
            {
                ShowResult("MHNK Filter", "The selected seed element does not expose material ids.");
                return Result.Cancelled;
            }

            IList<ElementId> ids = GetVisibleElements(context)
                .Where(x => x.GetMaterialIds(false).Any(id => materialIds.Contains(GetElementIdText(id))))
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible element(s) sharing material with the seed element.");
            return Result.Succeeded;
        }

        protected static Result SelectByCadLayer(MhnkArcContext context)
        {
            IList<ImportInstance> selectedImports = context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id))
                .OfType<ImportInstance>()
                .ToList();

            IList<ImportInstance> visibleImports = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .Cast<ImportInstance>()
                .ToList();

            IList<string> layers = selectedImports.Count > 0
                ? GetCadLayerNames(context.Document, selectedImports)
                : GetCadLayerNames(context.Document, visibleImports);

            context.UiDocument.Selection.SetElementIds(visibleImports.Select(x => x.Id).ToList());
            ShowResult(
                "MHNK Filter",
                "Selected " + visibleImports.Count + " visible CAD import(s)." + Environment.NewLine +
                "Detected layer/style names: " + layers.Count + Environment.NewLine +
                string.Join(Environment.NewLine, layers.Take(30).Select(x => "- " + x).ToArray()));
            return Result.Succeeded;
        }

        protected static Result SaveOrLoadFilterPreset(MhnkArcContext context)
        {
            ICollection<ElementId> selectedIds = context.UiDocument.Selection.GetElementIds();
            string presetPath = GetFilterPresetPath(context.Document);

            if (selectedIds != null && selectedIds.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(presetPath));
                Element seed = context.Document.GetElement(selectedIds.First());
                string categoryId = seed?.Category == null ? "" : GetElementIdText(seed.Category.Id);
                string typeId = seed == null ? "" : GetElementIdText(seed.GetTypeId());

                File.WriteAllLines(
                    presetPath,
                    new[]
                    {
                        "CategoryId=" + categoryId,
                        "TypeId=" + typeId,
                        "Saved=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        "View=" + context.ActiveView.Name,
                        "Count=" + selectedIds.Count
                    });

                ShowResult("MHNK Filter", "Saved filter preset from current selection:" + Environment.NewLine + presetPath);
                return Result.Succeeded;
            }

            if (!File.Exists(presetPath))
            {
                ShowResult("MHNK Filter", "No selection is active and no saved preset was found.");
                return Result.Cancelled;
            }

            Dictionary<string, string> preset = ReadKeyValueFile(presetPath);
            string savedCategoryId = preset.ContainsKey("CategoryId") ? preset["CategoryId"] : "";
            string savedTypeId = preset.ContainsKey("TypeId") ? preset["TypeId"] : "";

            IList<ElementId> ids = GetVisibleElements(context)
                .Where(x =>
                    (string.IsNullOrWhiteSpace(savedCategoryId) || (x.Category != null && string.Equals(GetElementIdText(x.Category.Id), savedCategoryId, StringComparison.OrdinalIgnoreCase))) &&
                    (string.IsNullOrWhiteSpace(savedTypeId) || string.Equals(GetElementIdText(x.GetTypeId()), savedTypeId, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Loaded preset and selected " + ids.Count + " visible element(s)." + Environment.NewLine + presetPath);
            return Result.Succeeded;
        }

        protected static Result SelectMepElementsInActiveView(MhnkArcContext context)
        {
            var filter = new ElementMulticategoryFilter(GetMepCoordinationCategories());
            IList<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(filter)
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Filter", "Selected " + ids.Count + " visible MEP coordination element(s) in the active ARC view.");
            return Result.Succeeded;
        }

        protected static Result ApplyArcColorReview(MhnkArcContext context)
        {
            var colors = new Dictionary<BuiltInCategory, Color>
            {
                { BuiltInCategory.OST_Walls, new Color(53, 132, 228) },
                { BuiltInCategory.OST_Floors, new Color(77, 175, 124) },
                { BuiltInCategory.OST_Ceilings, new Color(152, 97, 217) },
                { BuiltInCategory.OST_Roofs, new Color(211, 118, 48) },
                { BuiltInCategory.OST_Doors, new Color(231, 76, 60) },
                { BuiltInCategory.OST_Windows, new Color(35, 188, 212) },
                { BuiltInCategory.OST_DuctCurves, new Color(245, 166, 35) },
                { BuiltInCategory.OST_PipeCurves, new Color(46, 204, 113) },
                { BuiltInCategory.OST_CableTray, new Color(247, 220, 111) },
                { BuiltInCategory.OST_Conduit, new Color(149, 165, 166) }
            };

            int changed = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - ARC Color Review"))
            {
                t.Start();
                foreach (KeyValuePair<BuiltInCategory, Color> pair in colors)
                {
                    IList<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .OfCategory(pair.Key)
                        .Select(x => x.Id)
                        .ToList();

                    if (ids.Count == 0)
                    {
                        continue;
                    }

                    var overrideSettings = new OverrideGraphicSettings();
                    overrideSettings.SetProjectionLineColor(pair.Value);
                    overrideSettings.SetSurfaceForegroundPatternColor(pair.Value);

                    foreach (ElementId id in ids)
                    {
                        context.ActiveView.SetElementOverrides(id, overrideSettings);
                        changed++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Xpress", "Applied ARC color review overrides to " + changed + " visible element(s).");
            return Result.Succeeded;
        }

        protected static Result IsolateCurrentSelection(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select elements first, then run ARC > Filter > Isolate Selection.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            using (Transaction t = new Transaction(context.Document, "MHNK - Isolate Selection"))
            {
                t.Start();
                TryDisableTemporaryHideIsolate(context.ActiveView);
                context.ActiveView.IsolateElementsTemporary(ids);
                t.Commit();
            }

            context.UiDocument.RefreshActiveView();
            ShowResult("MHNK Filter", "Temporarily isolated " + ids.Count + " selected element(s).");
            return Result.Succeeded;
        }

        protected static Result HideCurrentSelection(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select elements first, then run ARC > Filter > Hide Selection.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            using (Transaction t = new Transaction(context.Document, "MHNK - Hide Selection"))
            {
                t.Start();
                context.ActiveView.HideElementsTemporary(ids);
                t.Commit();
            }

            context.UiDocument.RefreshActiveView();
            ShowResult("MHNK Filter", "Temporarily hidden " + ids.Count + " selected element(s).");
            return Result.Succeeded;
        }

        protected static Result ResetTemporaryHideIsolate(MhnkArcContext context)
        {
            using (Transaction t = new Transaction(context.Document, "MHNK - Reset Temporary View"))
            {
                t.Start();
                TryDisableTemporaryHideIsolate(context.ActiveView);
                t.Commit();
            }

            context.UiDocument.RefreshActiveView();
            ShowResult("MHNK Filter", "Temporary hide/isolate has been reset for the active view.");
            return Result.Succeeded;
        }

        protected static Result CreateArcCoordinationView(MhnkArcContext context)
        {
            ViewFamilyType viewType = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewType == null)
            {
                ShowResult("MHNK Creation", "No 3D view family type is available in this model.");
                return Result.Cancelled;
            }

            View3D view;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create ARC 3D View"))
            {
                t.Start();
                view = View3D.CreateIsometric(context.Document, viewType.Id);
                view.Name = CreateUniqueViewName(context.Document, "MHNK_ARC_Coordination_3D");
                view.DetailLevel = ViewDetailLevel.Fine;
                view.DisplayStyle = DisplayStyle.Shading;
                t.Commit();
            }

            context.UiDocument.ActiveView = view;
            ShowResult("MHNK Creation", "Created and opened view: " + view.Name);
            return Result.Succeeded;
        }

        protected static Result CreateArcDraftingView(MhnkArcContext context)
        {
            ViewFamilyType viewType = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);

            if (viewType == null)
            {
                ShowResult("MHNK Creation", "No drafting view family type is available in this model.");
                return Result.Cancelled;
            }

            ViewDrafting view;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create ARC Drafting View"))
            {
                t.Start();
                view = ViewDrafting.Create(context.Document, viewType.Id);
                view.Name = CreateUniqueViewName(context.Document, "MHNK_ARC_Working_Detail");
                t.Commit();
            }

            context.UiDocument.ActiveView = view;
            ShowResult("MHNK Creation", "Created and opened view: " + view.Name);
            return Result.Succeeded;
        }

        protected static Result CreateWallsFromSelectedCurves(MhnkArcContext context)
        {
            IList<ElementId> ids = GetCurveSourceElementIds(context, MhnkCadCurvePurpose.Walls);
            if (ids.Count == 0)
            {
                ShowResult(
                    "MHNK Creation",
                    "No wall curve source was found." + Environment.NewLine +
                    "Select a CAD import, model line, detail line, grid, or curve-based wall guide first." + Environment.NewLine +
                    "For CAD layers, set Source to By Layer, keep the CAD visible in the active view, then click Retrieve before Preview / Run.");
                return Result.Cancelled;
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            List<CadCurveCandidate> candidates = GetSelectedCurveCandidates(context.Document, ids, settings, MhnkCadCurvePurpose.Walls);
            if (candidates.Count == 0)
            {
                ShowResult(
                    "MHNK Creation",
                    "No usable wall line or arc curves were found." + Environment.NewLine +
                    "Check that the CAD layer matches Wall CAD layers or Smart Mapping rules." + Environment.NewLine +
                    "For model/detail lines, use Select Item or All so the selected curves are included.");
                return Result.Cancelled;
            }

            WallType defaultWallType = GetPreferredWallType(context.Document, settings, null);
            Level level = GetSeedOrActiveLevel(context);

            if (defaultWallType == null || level == null)
            {
                ShowResult("MHNK Creation", "A wall type and level are required before walls can be created.");
                return Result.Cancelled;
            }

            IList<MhnkCadPreviewItem> previewItems = BuildCurvePreviewItems(
                candidates,
                c => "Wall: " + GetPreferredWallType(context.Document, settings, c.MappingRule).Name,
                c => ProjectCurveToLevel(c.Curve, level.Elevation) != null ? "Ready" : "Skip",
                c => ProjectCurveToLevel(c.Curve, level.Elevation) != null
                    ? "Length " + FormatLengthMeters(c.Curve.Length) + "; height " + GetWallHeightMeters(settings, c.MappingRule).ToString("0.###") + " m"
                    : "Unsupported curve");
            int readyPreviewCount = previewItems.Count(x => string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase));
            int skippedPreviewCount = previewItems.Count - readyPreviewCount;

            if (!ConfirmCadPreview(
                context,
                "CAD to Walls Preview",
                BuildCadPreviewSummary(
                    "CAD to Walls",
                    ids.Count,
                    candidates.Count,
                    readyPreviewCount,
                    level.Name,
                    "Source mode: " + context.SourceModeName + Environment.NewLine +
                    "Candidate status: " + readyPreviewCount + " ready / " + skippedPreviewCount + " skipped" + Environment.NewLine +
                    "Default wall type: " + defaultWallType.Name + Environment.NewLine +
                    "Default wall height: " + settings.WallHeightMeters.ToString("0.###") + " m" + Environment.NewLine +
                    "Wall layer keywords: " + settings.WallLayerKeywords + Environment.NewLine +
                    "Detected layers: " + GetCadLayerSummary(candidates) + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(candidates)),
                previewItems))
            {
                return Result.Cancelled;
            }

            int created = 0;
            int skipped = 0;
            List<ElementId> createdIds = new List<ElementId>();
            using (Transaction t = new Transaction(context.Document, "MHNK - Create Walls From Curves"))
            {
                t.Start();
                foreach (CadCurveCandidate candidate in candidates)
                {
                    Curve wallCurve = ProjectCurveToLevel(candidate.Curve, level.Elevation);
                    if (wallCurve == null || wallCurve.Length < ShortCurveTolerance)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        WallType wallType = GetPreferredWallType(context.Document, settings, candidate.MappingRule);
                        Wall wall = Wall.Create(context.Document, wallCurve, wallType.Id, level.Id, MetersToFeet(GetWallHeightMeters(settings, candidate.MappingRule)), 0.0, false, false);
                        if (wall != null)
                        {
                            createdIds.Add(wall.Id);
                            created++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            if (createdIds.Count > 0)
            {
                context.UiDocument.Selection.SetElementIds(createdIds);
            }

            ShowResult(
                "MHNK Creation",
                "Created " + created + " wall(s)." + Environment.NewLine +
                "Skipped " + skipped + " curve(s)." + Environment.NewLine +
                "Selected created walls: " + createdIds.Count + Environment.NewLine +
                "Level: " + level.Name + Environment.NewLine +
                "Default wall type: " + defaultWallType.Name + Environment.NewLine +
                "Source mode: " + context.SourceModeName + Environment.NewLine +
                "Detected layers: " + GetCadLayerSummary(candidates) + Environment.NewLine +
                "Mapping: " + GetMappingRuleSummary(candidates));
            return created > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CreateWallsFromRooms(MhnkArcContext context)
        {
            IList<Room> rooms = GetRoomCreationCandidateRooms(context, out string roomSource, true);
            if (rooms.Count == 0)
            {
                ShowResult("MHNK Creation", "No bounded rooms match the selected Source Mode. Use Select Item for picked rooms, Category for visible rooms, or All for all bounded rooms.");
                return Result.Cancelled;
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            WallType defaultWallType = GetPreferredWallType(context.Document, settings, null);
            if (defaultWallType == null)
            {
                ShowResult("MHNK Creation", "A wall type is required before walls can be created from rooms.");
                return Result.Cancelled;
            }

            MhnkRoomCreationOptions options = ShowRoomCreationOptions(
                context,
                MhnkRoomCreationMode.Walls,
                rooms,
                roomSource,
                GetWallTypeOptions(context.Document),
                defaultWallType.Id,
                settings.WallHeightMeters,
                0.0);
            if (options == null)
            {
                return Result.Cancelled;
            }

            rooms = FilterRoomsBySelectedLevel(context.Document, rooms, options);
            if (rooms.Count == 0)
            {
                ShowResult("MHNK Creation", "No bounded rooms were found for the selected level option.");
                return Result.Cancelled;
            }

            IList<RoomBoundaryCurveCandidate> candidates = GetRoomWallBoundaryCandidates(context.Document, rooms, options);
            if (candidates.Count == 0)
            {
                ShowResult("MHNK Creation", "No usable room boundary segments were found for wall creation.");
                return Result.Cancelled;
            }

            candidates = ShowRoomBoundaryLineReview(context, candidates, options);
            if (candidates == null || candidates.Count == 0)
            {
                return Result.Cancelled;
            }

            IList<MhnkCadPreviewItem> previewItems = BuildRoomWallPreviewItems(rooms, candidates, options);
            if (!ConfirmCadPreview(
                context,
                "Create Walls by Room Preview",
                BuildCadPreviewSummary(
                    "Create Walls by Room",
                    rooms.Count,
                    candidates.Count,
                    previewItems.Count(x => x.Status == "Ready"),
                    GetRoomLevelsSummary(context.Document, rooms),
                    "Source: " + roomSource + Environment.NewLine +
                    "Level option: " + GetSelectedLevelSummary(context.Document, options) + Environment.NewLine +
                    "Mode: Lines from Rooms" + Environment.NewLine +
                    "Wall type: " + options.TypeName + Environment.NewLine +
                    GetWallHeightSummary(context.Document, options) + Environment.NewLine +
                    "Base offset: " + options.BaseOffsetMillimeters.ToString("0.###") + " mm" + Environment.NewLine +
                    "Wall type change: " + (options.AutoInternalExternalWallTypes ? "Auto -EXT+ / -INT+" : "Off") + Environment.NewLine +
                    "Join type: " + GetWallJoinSummary(options)),
                previewItems))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            var createdWalls = new List<Wall>();
            int skipped = 0;
            int joined = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create Walls By Room"))
            {
                t.Start();
                WallType selectedWallType = context.Document.GetElement(options.TypeId) as WallType;
                WallType exteriorWallType = selectedWallType;
                WallType interiorWallType = selectedWallType;
                if (options.AutoInternalExternalWallTypes && selectedWallType != null)
                {
                    exteriorWallType = GetOrCreateWallFunctionType(context.Document, selectedWallType, WallFunction.Exterior, "-EXT+");
                    interiorWallType = GetOrCreateWallFunctionType(context.Document, selectedWallType, WallFunction.Interior, "-INT+");
                }

                foreach (RoomBoundaryCurveCandidate candidate in candidates)
                {
                    try
                    {
                        ElementId wallTypeId = options.TypeId;
                        if (options.AutoInternalExternalWallTypes)
                        {
                            WallType classifiedType = candidate.IsExteriorBoundary ? exteriorWallType : interiorWallType;
                            if (classifiedType != null)
                            {
                                wallTypeId = classifiedType.Id;
                            }
                        }

                        Wall wall = Wall.Create(
                            context.Document,
                            candidate.Curve,
                            wallTypeId,
                            candidate.Level.Id,
                            GetInitialWallCreationHeight(context.Document, candidate, options),
                            MillimetersToFeet(options.BaseOffsetMillimeters),
                            false,
                            false);

                        if (wall == null)
                        {
                            skipped++;
                            continue;
                        }

                        SetGeneratedWallOptions(context.Document, wall, options, candidate);
                        createdIds.Add(wall.Id);
                        createdWalls.Add(wall);
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                if (options.JoinGeneratedWalls && createdWalls.Count > 1)
                {
                    context.Document.Regenerate();
                    joined = JoinGeneratedTouchingWalls(context.Document, createdWalls);
                }

                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(createdIds);
            ShowResult(
                "MHNK Creation",
                "Created " + createdIds.Count + " wall(s) from " + rooms.Count + " room(s)." + Environment.NewLine +
                "Skipped " + skipped + " boundary segment(s)." + Environment.NewLine +
                "Joined pairs: " + joined + Environment.NewLine +
                "Wall type: " + options.TypeName);
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CreateFloorsFromRooms(MhnkArcContext context)
        {
            return CreateHorizontalElementsFromRooms(context, MhnkRoomCreationMode.Floors);
        }

        protected static Result CreateCeilingsFromRooms(MhnkArcContext context)
        {
            return CreateHorizontalElementsFromRooms(context, MhnkRoomCreationMode.Ceilings);
        }

        private static Result CreateHorizontalElementsFromRooms(MhnkArcContext context, MhnkRoomCreationMode mode)
        {
            IList<Room> rooms = GetRoomCreationCandidateRooms(context, out string roomSource, includeAllModelRooms: true);
            if (rooms.Count == 0)
            {
                ShowResult("MHNK Creation", "No bounded rooms match the selected Source Mode. Use Select Item for picked rooms, Category for visible rooms, or All for all bounded rooms.");
                return Result.Cancelled;
            }

            bool createFloors = mode == MhnkRoomCreationMode.Floors;
            IList<MhnkElementTypeOption> typeOptions = createFloors
                ? GetFloorTypeOptions(context.Document)
                : GetCeilingTypeOptions(context.Document);
            ElementId defaultTypeId = createFloors
                ? GetPreferredFloorType(context.Document, new List<CadCurveCandidate>())?.Id
                : GetPreferredCeilingType(context.Document, new List<CadCurveCandidate>())?.Id;

            if (typeOptions.Count == 0 || defaultTypeId == null)
            {
                ShowResult("MHNK Creation", (createFloors ? "A floor type" : "A ceiling type") + " is required before creating from rooms.");
                return Result.Cancelled;
            }

            MhnkRoomCreationOptions options = ShowRoomCreationOptions(
                context,
                mode,
                rooms,
                roomSource,
                typeOptions,
                defaultTypeId,
                3.0,
                createFloors ? 0.0 : 2500.0);
            if (options == null)
            {
                return Result.Cancelled;
            }

            rooms = FilterRoomsBySelectedLevel(context.Document, rooms, options);
            if (rooms.Count == 0)
            {
                ShowResult("MHNK Creation", "No bounded rooms were found for the selected level option.");
                return Result.Cancelled;
            }

            IList<RoomLoopCandidate> roomLoops = GetRoomLoopCandidates(context.Document, rooms);
            if (roomLoops.Count == 0)
            {
                ShowResult("MHNK Creation", "No usable closed room boundaries were found.");
                return Result.Cancelled;
            }

            string actionTitle = createFloors ? "Create Floors by Room" : "Create Ceilings by Room";
            roomLoops = ReviewRoomLoopCandidates(context, roomLoops, roomSource, createFloors ? "Floor" : "Ceiling");
            if (roomLoops.Count == 0)
            {
                ShowResult("MHNK Creation", "No collected rooms were selected for " + (createFloors ? "floor" : "ceiling") + " creation.");
                return Result.Cancelled;
            }

            rooms = roomLoops.Select(x => x.Room).ToList();
            IList<MhnkCadPreviewItem> previewItems = BuildRoomLoopPreviewItems(rooms, roomLoops, options, createFloors ? "Floor" : "Ceiling");
            if (!ConfirmCadPreview(
                context,
                actionTitle + " Preview",
                BuildCadPreviewSummary(
                    actionTitle,
                    rooms.Count,
                    roomLoops.Sum(x => x.Loops.Count),
                    roomLoops.Count,
                    GetRoomLevelsSummary(context.Document, rooms),
                    "Source: " + roomSource + Environment.NewLine +
                    "Room level option: " + GetSelectedLevelSummary(context.Document, options) + Environment.NewLine +
                    (createFloors ? "Floor type: " : "Ceiling type: ") + options.TypeName + Environment.NewLine +
                    "Height offset: " + options.OffsetMillimeters.ToString("0.###") + " mm"),
                previewItems))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - " + actionTitle))
            {
                t.Start();
                foreach (RoomLoopCandidate candidate in roomLoops)
                {
                    try
                    {
                        Element element;
                        if (createFloors)
                        {
                            element = Floor.Create(context.Document, candidate.Loops, options.TypeId, candidate.Level.Id);
                            SetBuiltInDoubleParameter(element, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, MillimetersToFeet(options.OffsetMillimeters));
                        }
                        else
                        {
                            element = Ceiling.Create(context.Document, candidate.Loops, options.TypeId, candidate.Level.Id);
                            SetBuiltInDoubleParameter(element, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM, MillimetersToFeet(options.OffsetMillimeters));
                        }

                        if (element == null)
                        {
                            skipped++;
                            continue;
                        }

                        createdIds.Add(element.Id);
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(createdIds);
            ShowResult(
                "MHNK Creation",
                "Created " + createdIds.Count + " " + (createFloors ? "floor" : "ceiling") + "(s) from " + rooms.Count + " room(s)." + Environment.NewLine +
                "Skipped " + skipped + " room(s)." + Environment.NewLine +
                "Type: " + options.TypeName);
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result ShowCadToModelManager(MhnkArcContext context)
        {
            IList<ElementId> sourceIds = GetCadManagerSourceIds(context);
            if (sourceIds.Count == 0)
            {
                ShowResult("MHNK Creation", "No CAD/model/detail source was found. Use Free Select, By Layer, or All in the ARC Design Panel.");
                return Result.Cancelled;
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            IList<CadCurveCandidate> candidates = GetSelectedCurveCandidates(context.Document, sourceIds, settings, MhnkCadCurvePurpose.Any, false);
            IList<MhnkCadToModelScanItem> scanItems = BuildCadToModelScanItems(context.Document, candidates, settings);
            if (scanItems.Count == 0)
            {
                ShowResult("MHNK Creation", "No usable CAD/model/detail curves were found in the selected source.");
                return Result.Cancelled;
            }

            string summary =
                "Source elements: " + sourceIds.Count + Environment.NewLine +
                "Source mode: " + context.SourceModeName + Environment.NewLine +
                "Detected curves: " + candidates.Count + Environment.NewLine +
                "Runnable groups: " + scanItems.Count(x => string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase)) + Environment.NewLine +
                "Settings: " + MhnkArcToolSettings.GetSettingsPath() + Environment.NewLine +
                "Mapping rules: " + MhnkArcSmartMappingRules.GetRulesPath();

            var window = new MhnkCadToModelManagerWindow(summary, scanItems, context.UiApplication.MainWindowHandle);
            if (window.ShowDialog() != true || window.SelectedScanItems == null || window.SelectedScanItems.Count == 0)
            {
                return Result.Cancelled;
            }

            return RunCadToModelScanItems(context, window.SelectedScanItems);
        }

        protected static Result CreateRoomBoundaryLinesFromSelectedCurves(MhnkArcContext context)
        {
            if (!(context.ActiveView is ViewPlan))
            {
                ShowResult("MHNK Creation", "Room boundary lines must be created from a plan view.");
                return Result.Cancelled;
            }

            IList<ElementId> ids = GetCurveSourceElementIds(context, MhnkCadCurvePurpose.RoomBoundaries);
            if (ids.Count == 0)
            {
                ShowResult("MHNK Creation", "No room-boundary curve source was found. Use Free Select, By Layer, or All in the ARC Design Panel.");
                return Result.Cancelled;
            }

            Level level = GetActiveOrFirstLevel(context.Document, context.ActiveView);
            if (level == null)
            {
                ShowResult("MHNK Creation", "A level is required before room boundaries can be created.");
                return Result.Cancelled;
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            List<CadCurveCandidate> candidates = GetSelectedCurveCandidates(context.Document, ids, settings, MhnkCadCurvePurpose.RoomBoundaries);
            List<Curve> curves = candidates.Select(x => x.Curve).ToList();
            var array = new CurveArray();
            foreach (Curve curve in curves)
            {
                Curve projected = ProjectCurveToLevel(curve, level.Elevation);
                if (projected != null && projected.Length >= ShortCurveTolerance)
                {
                    array.Append(projected);
                }
            }

            if (array.Size == 0)
            {
                ShowResult("MHNK Creation", "No usable line or arc curves were found in the current selection.");
                return Result.Cancelled;
            }

            IList<MhnkCadPreviewItem> previewItems = BuildCurvePreviewItems(
                candidates,
                c => "Room Boundary Line",
                c => ProjectCurveToLevel(c.Curve, level.Elevation) != null ? "Ready" : "Skip",
                c => ProjectCurveToLevel(c.Curve, level.Elevation) != null ? "Length " + FormatLengthMeters(c.Curve.Length) : "Unsupported curve");

            if (!ConfirmCadPreview(
                context,
                "CAD to Room Boundaries Preview",
                BuildCadPreviewSummary(
                    "CAD to Room Boundaries",
                    ids.Count,
                    candidates.Count,
                    array.Size,
                    level.Name,
                    "Target: Revit room separation lines" + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(candidates)),
                previewItems))
            {
                return Result.Cancelled;
            }

            using (Transaction t = new Transaction(context.Document, "MHNK - Create Room Boundaries"))
            {
                t.Start();
                SketchPlane sketchPlane = context.ActiveView.SketchPlane;
                if (sketchPlane == null)
                {
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                    sketchPlane = SketchPlane.Create(context.Document, plane);
                }

                context.Document.Create.NewRoomBoundaryLines(sketchPlane, array, context.ActiveView);
                t.Commit();
            }

            ShowResult("MHNK Creation", "Created room boundary lines from " + array.Size + " selected curve(s).");
            return Result.Succeeded;
        }

        protected static Result CreateFloorsFromSelectedCurves(MhnkArcContext context)
        {
            CadLoopPreviewData previewData = GetClosedLoopPreviewData(context, MhnkCadCurvePurpose.Floors);
            IList<CurveLoop> loops = previewData.Loops;
            if (loops.Count == 0)
            {
                ShowResult("MHNK Creation", "Select closed floor boundary curves or CAD imports first. Check ARC Tool Settings layer keywords.");
                return Result.Cancelled;
            }

            FloorType floorType = GetPreferredFloorType(context.Document, previewData.Candidates);
            Level level = GetSeedOrActiveLevel(context);
            if (floorType == null || level == null)
            {
                ShowResult("MHNK Creation", "A floor type and level are required before floors can be created.");
                return Result.Cancelled;
            }

            if (!ConfirmCadPreview(
                context,
                "CAD to Floors Preview",
                BuildCadPreviewSummary(
                    "CAD to Floors",
                    previewData.SourceElementCount,
                    previewData.Candidates.Count,
                    loops.Count,
                    level.Name,
                    "Source mode: " + context.SourceModeName + Environment.NewLine +
                    "Floor type: " + floorType.Name + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(previewData.Candidates)),
                BuildLoopPreviewItems(loops, "Floor: " + floorType.Name, "Closed floor boundary")))
            {
                return Result.Cancelled;
            }

            Floor floor;
            using (Transaction t = new Transaction(context.Document, "MHNK - CAD To Floors"))
            {
                t.Start();
                floor = Floor.Create(context.Document, loops, floorType.Id, level.Id);
                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(new List<ElementId> { floor.Id });
            ShowResult("MHNK Creation", "Created floor from " + loops.Count + " loop(s) using type: " + floorType.Name);
            return Result.Succeeded;
        }

        protected static Result CreateCeilingsFromSelectedCurves(MhnkArcContext context)
        {
            CadLoopPreviewData previewData = GetClosedLoopPreviewData(context, MhnkCadCurvePurpose.Ceilings);
            IList<CurveLoop> loops = previewData.Loops;
            if (loops.Count == 0)
            {
                ShowResult("MHNK Creation", "Select closed ceiling boundary curves or CAD imports first. Check ARC Tool Settings layer keywords.");
                return Result.Cancelled;
            }

            CeilingType ceilingType = GetPreferredCeilingType(context.Document, previewData.Candidates);
            Level level = GetSeedOrActiveLevel(context);
            if (ceilingType == null || level == null)
            {
                ShowResult("MHNK Creation", "A ceiling type and level are required before ceilings can be created.");
                return Result.Cancelled;
            }

            if (!ConfirmCadPreview(
                context,
                "CAD to Ceilings Preview",
                BuildCadPreviewSummary(
                    "CAD to Ceilings",
                    previewData.SourceElementCount,
                    previewData.Candidates.Count,
                    loops.Count,
                    level.Name,
                    "Source mode: " + context.SourceModeName + Environment.NewLine +
                    "Ceiling type: " + ceilingType.Name + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(previewData.Candidates)),
                BuildLoopPreviewItems(loops, "Ceiling: " + ceilingType.Name, "Closed ceiling boundary")))
            {
                return Result.Cancelled;
            }

            Ceiling ceiling;
            using (Transaction t = new Transaction(context.Document, "MHNK - CAD To Ceilings"))
            {
                t.Start();
                ceiling = Ceiling.Create(context.Document, loops, ceilingType.Id, level.Id);
                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(new List<ElementId> { ceiling.Id });
            ShowResult("MHNK Creation", "Created ceiling from " + loops.Count + " loop(s) using type: " + ceilingType.Name);
            return Result.Succeeded;
        }

        protected static Result CreateRoomsFromSelectedCurves(MhnkArcContext context)
        {
            if (!(context.ActiveView is ViewPlan))
            {
                ShowResult("MHNK Creation", "Rooms must be created from a plan view.");
                return Result.Cancelled;
            }

            CadLoopPreviewData previewData = GetClosedLoopPreviewData(context, MhnkCadCurvePurpose.RoomBoundaries);
            IList<CurveLoop> loops = previewData.Loops;
            if (loops.Count == 0)
            {
                ShowResult("MHNK Creation", "Select closed room boundary curves or CAD imports first. Check ARC Tool Settings layer keywords.");
                return Result.Cancelled;
            }

            Level level = GetSeedOrActiveLevel(context);
            if (level == null)
            {
                ShowResult("MHNK Creation", "A level is required before rooms can be created.");
                return Result.Cancelled;
            }

            if (!ConfirmCadPreview(
                context,
                "CAD to Rooms Preview",
                BuildCadPreviewSummary(
                    "CAD to Rooms",
                    previewData.SourceElementCount,
                    previewData.Candidates.Count,
                    loops.Count,
                    level.Name,
                    "Source mode: " + context.SourceModeName + Environment.NewLine +
                    "Target: Revit rooms at loop center points" + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(previewData.Candidates)),
                BuildLoopPreviewItems(loops, "Room", "Room at loop centroid")))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - CAD To Rooms"))
            {
                t.Start();
                foreach (CurveLoop loop in loops)
                {
                    XYZ center = GetCurveLoopCenter(loop);
                    if (center == null)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        Room room = context.Document.Create.NewRoom(level, new UV(center.X, center.Y));
                        if (room != null)
                        {
                            createdIds.Add(room.Id);
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(createdIds);
            ShowResult("MHNK Creation", "Created " + createdIds.Count + " room(s)." + Environment.NewLine + "Skipped " + skipped + " loop(s).");
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CreateOpeningCandidatesFromSelectedCurves(MhnkArcContext context)
        {
            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            CadLoopPreviewData previewData = GetClosedLoopPreviewData(context, MhnkCadCurvePurpose.Openings);
            IList<CurveLoop> loops = previewData.Loops;
            if (loops.Count == 0)
            {
                ShowResult("MHNK Creation", "Select closed opening boundary curves or CAD imports first. Check ARC Tool Settings layer keywords.");
                return Result.Cancelled;
            }

            double openingDepthMeters = GetOpeningDepthMeters(settings, previewData.Candidates);
            if (!ConfirmCadPreview(
                context,
                "CAD to Openings Preview",
                BuildCadPreviewSummary(
                    "CAD to Openings",
                    previewData.SourceElementCount,
                    previewData.Candidates.Count,
                    loops.Count,
                    context.ActiveView.Name,
                    "Source mode: " + context.SourceModeName + Environment.NewLine +
                    "Opening candidate depth: " + openingDepthMeters.ToString("0.###") + " m" + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(previewData.Candidates)),
                BuildLoopPreviewItems(loops, "Opening Candidate Solid", "DirectShape candidate, host cut not applied")))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            using (Transaction t = new Transaction(context.Document, "MHNK - CAD To Opening Candidates"))
            {
                t.Start();
                for (int i = 0; i < loops.Count; i++)
                {
                    Solid solid = CreateCandidateSolidFromLoop(loops[i], MetersToFeet(openingDepthMeters));
                    if (solid == null)
                    {
                        continue;
                    }

                    DirectShape shape = DirectShape.CreateElement(context.Document, new ElementId(BuiltInCategory.OST_GenericModel));
                    shape.ApplicationId = "MHNK";
                    shape.ApplicationDataId = "MHNK_ARC_OPENING_CANDIDATE_" + DateTime.Now.Ticks + "_" + i;
                    shape.Name = "MHNK ARC Opening Candidate";
                    shape.SetShape(new List<GeometryObject> { solid });
                    createdIds.Add(shape.Id);
                }

                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(createdIds);
            ShowResult("MHNK Creation", "Created " + createdIds.Count + " opening candidate solid(s).");
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result PlaceDoorWindowCandidatesFromSelectedCurves(MhnkArcContext context)
        {
            IList<ElementId> ids = GetCurveSourceElementIds(context, MhnkCadCurvePurpose.DoorsWindows);
            if (ids.Count == 0)
            {
                ShowResult("MHNK Creation", "No door/window curve source was found. Use Free Select, By Layer, or All in the ARC Design Panel.");
                return Result.Cancelled;
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            List<CadCurveCandidate> candidates = GetSelectedCurveCandidates(context.Document, ids, settings, MhnkCadCurvePurpose.DoorsWindows);
            if (candidates.Count == 0)
            {
                ShowResult("MHNK Creation", "No usable door/window curves were found. Check selection and ARC mapping rules.");
                return Result.Cancelled;
            }

            FamilySymbol doorSymbol = GetPreferredFamilySymbol(context.Document, BuiltInCategory.OST_Doors, settings.SplitKeywords(settings.DoorTypeKeywords));
            FamilySymbol windowSymbol = GetPreferredFamilySymbol(context.Document, BuiltInCategory.OST_Windows, settings.SplitKeywords(settings.WindowTypeKeywords));
            Level level = GetSeedOrActiveLevel(context);
            if ((doorSymbol == null && windowSymbol == null) || level == null)
            {
                ShowResult("MHNK Creation", "A loaded non-hosted door/window symbol and level are required.");
                return Result.Cancelled;
            }

            IList<MhnkCadPreviewItem> previewItems = BuildCurvePreviewItems(
                candidates,
                c => GetDoorWindowTargetName(context.Document, c, settings, doorSymbol, windowSymbol),
                c => GetDoorWindowTargetSymbol(context.Document, c, settings, doorSymbol, windowSymbol) != null ? "Ready" : "Skip",
                c => GetDoorWindowTargetSymbol(context.Document, c, settings, doorSymbol, windowSymbol) != null ? "Placement point at curve midpoint" : "No matching loaded symbol");

            if (!ConfirmCadPreview(
                context,
                "CAD to Doors / Windows Preview",
                BuildCadPreviewSummary(
                    "CAD to Doors / Windows",
                    ids.Count,
                    candidates.Count,
                    previewItems.Count(x => x.Status == "Ready"),
                    level.Name,
                    "Door symbol: " + (doorSymbol?.Name ?? "Missing") + Environment.NewLine +
                    "Window symbol: " + (windowSymbol?.Name ?? "Missing") + Environment.NewLine +
                    "Mapping: " + GetMappingRuleSummary(candidates)),
                previewItems))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - CAD To Doors Windows"))
            {
                t.Start();
                ActivateSymbol(context.Document, doorSymbol);
                ActivateSymbol(context.Document, windowSymbol);

                foreach (CadCurveCandidate candidate in candidates)
                {
                    FamilySymbol symbol = GetDoorWindowTargetSymbol(context.Document, candidate, settings, doorSymbol, windowSymbol);
                    if (symbol == null)
                    {
                        skipped++;
                        continue;
                    }

                    XYZ point = candidate.Curve.Evaluate(0.5, true);
                    point = ProjectPointToZ(point, level.Elevation);
                    try
                    {
                        ActivateSymbol(context.Document, symbol);
                        FamilyInstance instance = context.Document.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
                        if (instance != null)
                        {
                            createdIds.Add(instance.Id);
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(createdIds);
            ShowResult("MHNK Creation", "Placed " + createdIds.Count + " door/window candidate(s)." + Environment.NewLine + "Skipped " + skipped + " hosted/incompatible point(s).");
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result PlaceDoorWindowCandidatesByWallSide(MhnkArcContext context)
        {
            return PlaceHostedDoorWindowCandidates(context, true);
        }

        protected static Result PlaceDoorWindowCandidatesByPosition(MhnkArcContext context)
        {
            return PlaceHostedDoorWindowCandidates(context, false);
        }

        private static Result PlaceHostedDoorWindowCandidates(MhnkArcContext context, bool wallSideMode)
        {
            IList<ElementId> selectedIds = GetCurveSourceElementIds(context, MhnkCadCurvePurpose.DoorsWindows);
            if (selectedIds.Count == 0)
            {
                ShowResult("MHNK Creation", "No door/window marker source was found. Use Free Select, By Layer, or All in the ARC Design Panel.");
                return Result.Cancelled;
            }

            IList<ElementId> markerIds = selectedIds
                .Where(id => !(context.Document.GetElement(id) is Wall))
                .ToList();
            IList<ElementId> sourceIds = markerIds.Count > 0 ? markerIds : selectedIds;

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            List<CadCurveCandidate> candidates = GetSelectedCurveCandidates(context.Document, sourceIds, settings, MhnkCadCurvePurpose.DoorsWindows);
            if (candidates.Count == 0)
            {
                ShowResult("MHNK Creation", "No door/window marker curves were found. Select CAD/model/detail markers or walls.");
                return Result.Cancelled;
            }

            IList<Wall> hostWalls = GetDoorWindowHostWalls(context, out string hostSource);
            if (hostWalls.Count == 0)
            {
                ShowResult("MHNK Creation", "No host walls were found in the selection or active view.");
                return Result.Cancelled;
            }

            FamilySymbol doorSymbol = GetPreferredFamilySymbol(context.Document, BuiltInCategory.OST_Doors, settings.SplitKeywords(settings.DoorTypeKeywords));
            FamilySymbol windowSymbol = GetPreferredFamilySymbol(context.Document, BuiltInCategory.OST_Windows, settings.SplitKeywords(settings.WindowTypeKeywords));
            if (doorSymbol == null && windowSymbol == null)
            {
                ShowResult("MHNK Creation", "Load at least one wall-hosted door or window family type before running this command.");
                return Result.Cancelled;
            }

            MhnkDoorWindowPlacementOptions options = ShowDoorWindowPlacementOptions(
                context,
                wallSideMode,
                candidates,
                hostWalls,
                hostSource,
                doorSymbol,
                windowSymbol);
            if (options == null)
            {
                return Result.Cancelled;
            }

            IList<HostedDoorWindowPlacementCandidate> placements = BuildHostedDoorWindowPlacementCandidates(
                context,
                candidates,
                hostWalls,
                settings,
                doorSymbol,
                windowSymbol,
                options);

            IList<MhnkCadPreviewItem> previewItems = BuildHostedDoorWindowPreviewItems(placements, wallSideMode);
            int readyCount = previewItems.Count(x => x.Status == "Ready");
            if (readyCount == 0)
            {
                ShowResult("MHNK Creation", "No marker is close enough to a wall host for placement.");
                return Result.Cancelled;
            }

            string actionTitle = wallSideMode ? "Doors / Windows by Wall Side" : "Doors / Windows by Position";
            if (!ConfirmCadPreview(
                context,
                actionTitle + " Preview",
                BuildCadPreviewSummary(
                    actionTitle,
                    selectedIds.Count,
                    candidates.Count,
                    readyCount,
                    context.ActiveView.Name,
                    "Host source: " + hostSource + Environment.NewLine +
                    "Door symbol: " + (doorSymbol?.Name ?? "Missing") + Environment.NewLine +
                    "Window symbol: " + (windowSymbol?.Name ?? "Missing") + Environment.NewLine +
                    "Max host distance: " + options.MaxHostDistanceMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm" + Environment.NewLine +
                    "Window sill: " + options.WindowSillHeightMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm"),
                previewItems))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Hosted Doors Windows"))
            {
                t.Start();
                ActivateSymbol(context.Document, doorSymbol);
                ActivateSymbol(context.Document, windowSymbol);

                foreach (HostedDoorWindowPlacementCandidate placement in placements)
                {
                    if (!placement.CanPlace)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        ActivateSymbol(context.Document, placement.Symbol);
                        FamilyInstance instance = context.Document.Create.NewFamilyInstance(
                            placement.HostPoint,
                            placement.Symbol,
                            placement.HostWall,
                            placement.Level,
                            StructuralType.NonStructural);
                        if (instance == null)
                        {
                            skipped++;
                            continue;
                        }

                        if (placement.IsWindow)
                        {
                            SetBuiltInDoubleParameter(instance, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM, placement.WindowSillHeight);
                        }

                        if (wallSideMode && options.FlipToMarkerSide)
                        {
                            TryFaceHostedInstanceToMarkerSide(instance, placement);
                        }

                        SetStringParameterIfWritable(instance, "Comments", "MHNK hosted placement from " + placement.SourceLabel);
                        createdIds.Add(instance.Id);
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            if (createdIds.Count > 0)
            {
                context.UiDocument.Selection.SetElementIds(createdIds);
            }

            ShowResult(
                "MHNK Creation",
                "Placed " + createdIds.Count + " hosted door/window instance(s)." + Environment.NewLine +
                "Skipped: " + skipped + Environment.NewLine +
                "Mode: " + (wallSideMode ? "Wall side" : "Position"));
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CreateWorkingPlans(MhnkArcContext context)
        {
            ViewFamilyType floorPlanType = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan);

            if (floorPlanType == null)
            {
                ShowResult("MHNK Creation", "No floor plan view family type is available.");
                return Result.Cancelled;
            }

            IList<Level> levels = new FilteredElementCollector(context.Document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ToList();

            int created = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create Working Plans"))
            {
                t.Start();
                foreach (Level level in levels)
                {
                    try
                    {
                        ViewPlan view = ViewPlan.Create(context.Document, floorPlanType.Id, level.Id);
                        view.Name = CreateUniqueViewName(context.Document, "MHNK_ARC_Working_" + SanitizeName(level.Name));
                        view.DetailLevel = ViewDetailLevel.Fine;
                        created++;
                    }
                    catch
                    {
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Creation", "Created " + created + " ARC working plan view(s).");
            return created > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CreateWallFinishCandidates(MhnkArcContext context)
        {
            return CreateWallFinishes(context, true);
        }

        protected static Result CreateInternalWallFinishCandidates(MhnkArcContext context)
        {
            return CreateWallFinishes(context, false);
        }

        private static Result CreateWallFinishes(MhnkArcContext context, bool exteriorSide)
        {
            string sideName = exteriorSide ? "External" : "Internal";
            string sideLower = exteriorSide ? "external" : "internal";
            string faceLower = exteriorSide ? "exterior" : "interior";
            IList<Wall> hostWalls = GetExternalWallFinishHostWalls(context, out string source, exteriorSide);
            if (hostWalls.Count == 0)
            {
                ShowResult("MHNK Creation", "Select " + faceLower + "/basic host walls first, or open a view with visible " + faceLower + " walls.");
                return Result.Cancelled;
            }

            hostWalls = ReviewCollectedWallFinishHosts(context, hostWalls, source, exteriorSide);
            if (hostWalls.Count == 0)
            {
                ShowResult("MHNK Creation", "No collected " + sideLower + " host walls were selected.");
                return Result.Cancelled;
            }

            IList<MhnkElementTypeOption> typeOptions = GetBasicWallFinishTypeOptions(context.Document);
            if (typeOptions.Count == 0)
            {
                ShowResult("MHNK Creation", "A basic wall type is required before " + sideLower + " wall finishes can be created.");
                return Result.Cancelled;
            }

            WallType defaultType = GetPreferredWallFinishType(context.Document, exteriorSide);
            MhnkWallFinishOptions options = ShowExternalWallFinishOptions(
                context,
                hostWalls,
                source,
                typeOptions,
                defaultType?.Id ?? typeOptions[0].Id,
                exteriorSide);
            if (options == null)
            {
                return Result.Cancelled;
            }

            WallType selectedType = context.Document.GetElement(options.TypeId) as WallType;
            if (selectedType == null)
            {
                ShowResult("MHNK Creation", "The selected finish wall type is no longer available.");
                return Result.Cancelled;
            }

            double previewFinishWidth = GetExternalFinishWidthFeet(selectedType, options);
            IList<WallFinishCandidate> candidates = BuildExternalWallFinishCandidates(context.Document, hostWalls, previewFinishWidth, options, exteriorSide);
            if (candidates.Count == 0)
            {
                ShowResult("MHNK Creation", "No usable linear host wall paths were found for " + sideLower + " finish creation.");
                return Result.Cancelled;
            }

            IList<MhnkCadPreviewItem> previewItems = BuildExternalWallFinishPreviewItems(candidates, options, exteriorSide);
            if (!ConfirmCadPreview(
                context,
                "Create " + sideName + " Wall Finishes Preview",
                BuildCadPreviewSummary(
                    "Create " + sideName + " Wall Finishes",
                    hostWalls.Count,
                    candidates.Count,
                    previewItems.Count(x => x.Status == "Ready"),
                    GetWallLevelsSummary(context.Document, hostWalls),
                    "Source: " + source + Environment.NewLine +
                    "Finish type: " + options.TypeName + Environment.NewLine +
                    "Material: " + (options.UseSelectedTypeMaterial ? "selected type material" : options.MaterialName) + Environment.NewLine +
                    "Thickness: " + (options.UseSelectedTypeMaterial ? FormatLengthMeters(previewFinishWidth) : options.ThicknessMillimeters.ToString("0.###") + " mm") + Environment.NewLine +
                    sideName + " gap: " + options.GapMillimeters.ToString("0.###") + " mm" + Environment.NewLine +
                    "Height: " + GetWallFinishHeightSummary(context.Document, options)),
                previewItems))
            {
                return Result.Cancelled;
            }

            var createdIds = new List<ElementId>();
            var createdWalls = new List<Wall>();
            int skipped = 0;
            int skippedExisting = 0;
            int joined = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create " + sideName + " Wall Finishes"))
            {
                t.Start();
                WallType finishType = ResolveExternalFinishWallType(context.Document, selectedType, options, exteriorSide);
                double finishWidth = GetExternalFinishWidthFeet(finishType, options);
                candidates = BuildExternalWallFinishCandidates(context.Document, hostWalls, finishWidth, options, exteriorSide);
                SetWallTypeFunction(finishType, exteriorSide ? WallFunction.Exterior : WallFunction.Interior);

                foreach (WallFinishCandidate candidate in candidates)
                {
                    try
                    {
                        if (options.SkipExistingFinishWalls && HasExistingFinishWall(context.Document, candidate, finishType.Id))
                        {
                            skippedExisting++;
                            continue;
                        }

                        Wall finishWall = Wall.Create(
                            context.Document,
                            candidate.Curve,
                            finishType.Id,
                            candidate.Level.Id,
                            candidate.InitialHeight,
                            candidate.BaseOffset,
                            false,
                            false);
                        if (finishWall == null)
                        {
                            skipped++;
                            continue;
                        }

                        ApplyExternalFinishWallOptions(context.Document, finishWall, candidate, options, exteriorSide);
                        createdIds.Add(finishWall.Id);
                        createdWalls.Add(finishWall);
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                if (options.JoinGeneratedWalls && createdWalls.Count > 1)
                {
                    context.Document.Regenerate();
                    joined = JoinGeneratedTouchingWalls(context.Document, createdWalls);
                }

                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(createdIds);
            ShowResult(
                "MHNK Creation",
                "Created " + createdIds.Count + " " + sideLower + " finish wall(s)." + Environment.NewLine +
                "Host walls: " + hostWalls.Count + Environment.NewLine +
                "Skipped existing: " + skippedExisting + Environment.NewLine +
                "Skipped failed: " + skipped + Environment.NewLine +
                "Joined pairs: " + joined + Environment.NewLine +
                "Height: " + GetWallFinishHeightSummary(context.Document, options) + Environment.NewLine +
                "Finish type: " + options.TypeName);
            return createdIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CreateFloorFinishCandidates(MhnkArcContext context)
        {
            return ShowFinishCandidateReport(context, "Floor", BuiltInCategory.OST_Floors);
        }

        protected static Result CreateCeilingFinishCandidates(MhnkArcContext context)
        {
            return ShowFinishCandidateReport(context, "Ceiling", BuiltInCategory.OST_Ceilings);
        }

        protected static Result CreateModelGroupFromSelection(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select elements first, then run Create Model Group from Selection.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            Group group;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create Model Group"))
            {
                t.Start();
                group = context.Document.Create.NewGroup(ids);
                group.GroupType.Name = CreateUniqueGroupTypeName(context.Document, "MHNK_ARC_Group");
                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(new List<ElementId> { group.Id });
            ShowResult("MHNK Creation", "Created model group: " + group.GroupType.Name);
            return Result.Succeeded;
        }

        protected static Result PinCurrentSelection(MhnkArcContext context)
        {
            return SetPinnedState(context, true);
        }

        protected static Result UnpinCurrentSelection(MhnkArcContext context)
        {
            return SetPinnedState(context, false);
        }

        protected static Result SetRoomBoundingOn(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select walls or room-bounding-capable elements first.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            int changed = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Set Room Bounding"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Element element = context.Document.GetElement(id);
                    Parameter parameter = GetRoomBoundingParameter(element);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Integer)
                    {
                        if (parameter.AsInteger() != 1)
                        {
                            parameter.Set(1);
                            changed++;
                        }
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Room Bounding turned on for " + changed + " selected element(s).");
            return Result.Succeeded;
        }

        protected static Result SetRoomBoundingOff(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select walls or room-bounding-capable elements first.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            int changed = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Set Room Bounding Off"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Element element = context.Document.GetElement(id);
                    Parameter parameter = GetRoomBoundingParameter(element);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Integer)
                    {
                        if (parameter.AsInteger() != 0)
                        {
                            parameter.Set(0);
                            changed++;
                        }
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Room Bounding turned off for " + changed + " selected element(s).");
            return Result.Succeeded;
        }

        protected static Result ToggleJoinFirstTwoSelected(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().Take(2).ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select exactly two model elements first, then run Toggle Join.");
                return Result.Cancelled;
            }

            Element first = context.Document.GetElement(ids[0]);
            Element second = context.Document.GetElement(ids[1]);
            string action;
            using (Transaction t = new Transaction(context.Document, "MHNK - Toggle Join Geometry"))
            {
                t.Start();
                if (JoinGeometryUtils.AreElementsJoined(context.Document, first, second))
                {
                    JoinGeometryUtils.UnjoinGeometry(context.Document, first, second);
                    action = "Unjoined";
                }
                else
                {
                    JoinGeometryUtils.JoinGeometry(context.Document, first, second);
                    action = "Joined";
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", action + " the first two selected elements.");
            return Result.Succeeded;
        }

        protected static Result ToggleSolidCutFirstTwoSelected(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().Take(2).ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select two solid-capable elements first.");
                return Result.Cancelled;
            }

            Element first = context.Document.GetElement(ids[0]);
            Element second = context.Document.GetElement(ids[1]);
            if (first == null || second == null)
            {
                ShowResult("MHNK Edition", "The first two selected elements are not valid.");
                return Result.Cancelled;
            }

            string action = "";
            using (Transaction t = new Transaction(context.Document, "MHNK - Toggle Solid Cut"))
            {
                t.Start();
                bool firstCutsSecond;
                if (SolidSolidCutUtils.CutExistsBetweenElements(first, second, out firstCutsSecond))
                {
                    SolidSolidCutUtils.RemoveCutBetweenSolids(context.Document, first, second);
                    action = "Removed existing solid cut";
                }
                else
                {
                    CutFailureReason reason;
                    if (SolidSolidCutUtils.CanElementCutElement(first, second, out reason))
                    {
                        SolidSolidCutUtils.AddCutBetweenSolids(context.Document, first, second);
                        action = "Added solid cut: first cuts second";
                    }
                    else if (SolidSolidCutUtils.CanElementCutElement(second, first, out reason))
                    {
                        SolidSolidCutUtils.AddCutBetweenSolids(context.Document, second, first);
                        action = "Added solid cut: second cuts first";
                    }
                    else
                    {
                        t.RollBack();
                        ShowResult("MHNK Edition", "Revit does not allow a solid cut between the first two selected elements.");
                        return Result.Cancelled;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", action + ".");
            return Result.Succeeded;
        }

        protected static Result SwitchJoinOrderFirstTwoSelected(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().Take(2).ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select two joined elements first.");
                return Result.Cancelled;
            }

            Element first = context.Document.GetElement(ids[0]);
            Element second = context.Document.GetElement(ids[1]);
            if (first == null || second == null || !JoinGeometryUtils.AreElementsJoined(context.Document, first, second))
            {
                ShowResult("MHNK Edition", "The first two selected elements are not joined.");
                return Result.Cancelled;
            }

            using (Transaction t = new Transaction(context.Document, "MHNK - Switch Join Order"))
            {
                t.Start();
                JoinGeometryUtils.SwitchJoinOrder(context.Document, first, second);
                t.Commit();
            }

            ShowResult("MHNK Edition", "Switched join order for the first two selected elements.");
            return Result.Succeeded;
        }

        protected static Result JoinSelectedIntersectingPairs(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectedGeometryOperationElements(
                context,
                "Select at least two model elements first, then run Join Selected Pairs.",
                180);
            if (elements == null)
            {
                return Result.Cancelled;
            }

            IList<ElementPair> pairs = FindIntersectingPairs(elements, elements, 350, false);
            if (pairs.Count == 0)
            {
                ShowResult("MHNK Edition", "No intersecting selected element pairs were found.");
                return Result.Cancelled;
            }

            int joined = 0;
            int alreadyJoined = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Join Selected Pairs"))
            {
                t.Start();
                foreach (ElementPair pair in pairs)
                {
                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(context.Document, pair.First, pair.Second))
                        {
                            alreadyJoined++;
                            continue;
                        }

                        JoinGeometryUtils.JoinGeometry(context.Document, pair.First, pair.Second);
                        joined++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult(
                "MHNK Edition",
                "Intersecting selected pairs: " + pairs.Count + Environment.NewLine +
                "Joined: " + joined + Environment.NewLine +
                "Already joined: " + alreadyJoined + Environment.NewLine +
                "Skipped: " + skipped);
            return joined > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result UnjoinSelectedJoinedPairs(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectedGeometryOperationElements(
                context,
                "Select at least two joined model elements first, then run Unjoin Selected Pairs.",
                180);
            if (elements == null)
            {
                return Result.Cancelled;
            }

            IList<ElementPair> pairs = BuildAllUniquePairs(elements, 600);
            int unjoined = 0;
            int notJoined = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Unjoin Selected Pairs"))
            {
                t.Start();
                foreach (ElementPair pair in pairs)
                {
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(context.Document, pair.First, pair.Second))
                        {
                            notJoined++;
                            continue;
                        }

                        JoinGeometryUtils.UnjoinGeometry(context.Document, pair.First, pair.Second);
                        unjoined++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult(
                "MHNK Edition",
                "Selected pairs checked: " + pairs.Count + Environment.NewLine +
                "Unjoined: " + unjoined + Environment.NewLine +
                "Not joined: " + notJoined + Environment.NewLine +
                "Skipped: " + skipped);
            return unjoined > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result SwitchJoinOrderSelectedPairs(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectedGeometryOperationElements(
                context,
                "Select at least two joined model elements first, then run Switch Join Order Selected.",
                180);
            if (elements == null)
            {
                return Result.Cancelled;
            }

            IList<ElementPair> pairs = BuildAllUniquePairs(elements, 600);
            int switched = 0;
            int notJoined = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Switch Join Order Selected"))
            {
                t.Start();
                foreach (ElementPair pair in pairs)
                {
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(context.Document, pair.First, pair.Second))
                        {
                            notJoined++;
                            continue;
                        }

                        JoinGeometryUtils.SwitchJoinOrder(context.Document, pair.First, pair.Second);
                        switched++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult(
                "MHNK Edition",
                "Selected pairs checked: " + pairs.Count + Environment.NewLine +
                "Join order switched: " + switched + Environment.NewLine +
                "Not joined: " + notJoined + Environment.NewLine +
                "Skipped: " + skipped);
            return switched > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result ToggleSolidCutSelectedPairs(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectedGeometryOperationElements(
                context,
                "Select at least two solid-capable model elements first, then run Cut / Uncut Selected Pairs.",
                120);
            if (elements == null)
            {
                return Result.Cancelled;
            }

            IList<ElementPair> pairs = FindIntersectingPairs(elements, elements, 300, false);
            if (pairs.Count == 0)
            {
                ShowResult("MHNK Solids", "No intersecting selected element pairs were found.");
                return Result.Cancelled;
            }

            int added = 0;
            int removed = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Toggle Selected Solid Cuts"))
            {
                t.Start();
                foreach (ElementPair pair in pairs)
                {
                    try
                    {
                        bool firstCutsSecond;
                        if (SolidSolidCutUtils.CutExistsBetweenElements(pair.First, pair.Second, out firstCutsSecond))
                        {
                            SolidSolidCutUtils.RemoveCutBetweenSolids(context.Document, pair.First, pair.Second);
                            removed++;
                            continue;
                        }

                        CutFailureReason reason;
                        if (SolidSolidCutUtils.CanElementCutElement(pair.First, pair.Second, out reason))
                        {
                            SolidSolidCutUtils.AddCutBetweenSolids(context.Document, pair.First, pair.Second);
                            added++;
                        }
                        else if (SolidSolidCutUtils.CanElementCutElement(pair.Second, pair.First, out reason))
                        {
                            SolidSolidCutUtils.AddCutBetweenSolids(context.Document, pair.Second, pair.First);
                            added++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult(
                "MHNK Solids",
                "Intersecting selected pairs: " + pairs.Count + Environment.NewLine +
                "Cuts added: " + added + Environment.NewLine +
                "Cuts removed: " + removed + Environment.NewLine +
                "Skipped: " + skipped);
            return added > 0 || removed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CutSelectedByFirstElement(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectedGeometryOperationElements(
                context,
                "Select a cutter element first, then one or more target elements.",
                120);
            if (elements == null)
            {
                return Result.Cancelled;
            }

            Element cutter = elements.First();
            IList<Element> targets = elements.Skip(1).ToList();
            int added = 0;
            int alreadyCut = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Cut Selected By First"))
            {
                t.Start();
                foreach (Element target in targets)
                {
                    try
                    {
                        bool firstCutsSecond;
                        if (SolidSolidCutUtils.CutExistsBetweenElements(cutter, target, out firstCutsSecond))
                        {
                            alreadyCut++;
                            continue;
                        }

                        CutFailureReason reason;
                        if (SolidSolidCutUtils.CanElementCutElement(cutter, target, out reason))
                        {
                            SolidSolidCutUtils.AddCutBetweenSolids(context.Document, cutter, target);
                            added++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult(
                "MHNK Solids",
                "Cutter: " + GetElementLabel(cutter) + Environment.NewLine +
                "Targets checked: " + targets.Count + Environment.NewLine +
                "Cuts added: " + added + Environment.NewLine +
                "Already cut: " + alreadyCut + Environment.NewLine +
                "Skipped: " + skipped);
            return added > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result UncutSelectedSolidPairs(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectedGeometryOperationElements(
                context,
                "Select at least two solid-cut model elements first, then run Uncut Selected Pairs.",
                120);
            if (elements == null)
            {
                return Result.Cancelled;
            }

            IList<ElementPair> pairs = BuildAllUniquePairs(elements, 500);
            int removed = 0;
            int notCut = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Uncut Selected Pairs"))
            {
                t.Start();
                foreach (ElementPair pair in pairs)
                {
                    try
                    {
                        bool firstCutsSecond;
                        if (!SolidSolidCutUtils.CutExistsBetweenElements(pair.First, pair.Second, out firstCutsSecond))
                        {
                            notCut++;
                            continue;
                        }

                        SolidSolidCutUtils.RemoveCutBetweenSolids(context.Document, pair.First, pair.Second);
                        removed++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult(
                "MHNK Solids",
                "Selected pairs checked: " + pairs.Count + Environment.NewLine +
                "Cuts removed: " + removed + Environment.NewLine +
                "Not cut: " + notCut + Environment.NewLine +
                "Skipped: " + skipped);
            return removed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result BatchTypeChangeFromSeed(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select a seed element first, then target elements to change type.");
                return Result.Cancelled;
            }

            Element seed = context.Document.GetElement(ids[0]);
            ElementId targetTypeId = seed?.GetTypeId() ?? ElementId.InvalidElementId;
            if (targetTypeId == ElementId.InvalidElementId)
            {
                ShowResult("MHNK Edition", "The first selected element does not have a valid type.");
                return Result.Cancelled;
            }

            int changed = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Batch Type Change"))
            {
                t.Start();
                foreach (ElementId id in ids.Skip(1))
                {
                    Element element = context.Document.GetElement(id);
                    if (element == null)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        if (element.GetTypeId() != targetTypeId)
                        {
                            element.ChangeTypeId(targetTypeId);
                            changed++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Changed type for " + changed + " element(s)." + Environment.NewLine + "Skipped " + skipped + " element(s).");
            return changed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result SplitWallsHorizontally(MhnkArcContext context)
        {
            IList<Wall> walls = GetHorizontalSplitCandidateWalls(context, out string source);
            if (walls.Count == 0)
            {
                ShowResult("MHNK Edition", "Select walls first, or open a view with visible walls that can be split.");
                return Result.Cancelled;
            }

            walls = ReviewHorizontalSplitWalls(context, walls, source);
            if (walls.Count == 0)
            {
                ShowResult("MHNK Edition", "No walls were selected for horizontal split.");
                return Result.Cancelled;
            }

            IList<MhnkElementTypeOption> typeOptions = GetWallTypeOptions(context.Document);
            if (typeOptions.Count == 0)
            {
                ShowResult("MHNK Edition", "No wall types are available in this model.");
                return Result.Cancelled;
            }

            MhnkWallHorizontalSplitOptions options = ShowHorizontalSplitOptions(context, walls, source, typeOptions);
            if (options == null)
            {
                return Result.Cancelled;
            }

            WallType newType = context.Document.GetElement(options.NewTypeId) as WallType;
            if (newType == null)
            {
                ShowResult("MHNK Edition", "The selected wall type is no longer available.");
                return Result.Cancelled;
            }

            IList<HorizontalWallSplitCandidate> candidates = BuildHorizontalSplitCandidates(context.Document, walls, options);
            IList<MhnkCadPreviewItem> previewItems = BuildHorizontalSplitPreviewItems(candidates, options);
            int readyCount = previewItems.Count(x => x.Status == "Ready");
            if (readyCount == 0)
            {
                ShowResult("MHNK Edition", "No selected walls are tall enough for the requested division height.");
                return Result.Cancelled;
            }

            if (!ConfirmCadPreview(
                context,
                "Split Walls Horizontally Preview",
                BuildCadPreviewSummary(
                    "Split Walls Horizontally",
                    walls.Count,
                    candidates.Count,
                    readyCount,
                    GetWallLevelsSummary(context.Document, walls),
                    "Source: " + source + Environment.NewLine +
                    "Division height: " + options.SplitHeightMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m" + Environment.NewLine +
                    "New type: " + options.NewTypeName + Environment.NewLine +
                    "New type segment: " + (options.ApplyNewTypeToLowerSegment ? "Lower" : "Upper")),
                previewItems))
            {
                return Result.Cancelled;
            }

            var affectedIds = new List<ElementId>();
            int split = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Split Walls Horizontally"))
            {
                t.Start();
                foreach (HorizontalWallSplitCandidate candidate in candidates)
                {
                    if (!candidate.CanSplit)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        Wall created = SplitSingleWallHorizontally(context.Document, candidate, newType, options);
                        if (created == null)
                        {
                            skipped++;
                            continue;
                        }

                        affectedIds.Add(candidate.HostWall.Id);
                        affectedIds.Add(created.Id);
                        split++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            if (affectedIds.Count > 0)
            {
                context.UiDocument.Selection.SetElementIds(affectedIds);
            }

            ShowResult(
                "MHNK Edition",
                "Horizontally split " + split + " wall(s)." + Environment.NewLine +
                "Skipped: " + skipped + Environment.NewLine +
                "Division height: " + options.SplitHeightMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m" + Environment.NewLine +
                "New type: " + options.NewTypeName + Environment.NewLine +
                "New type segment: " + (options.ApplyNewTypeToLowerSegment ? "Lower" : "Upper"));
            return split > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result LowerWallsToCeilings(MhnkArcContext context)
        {
            IList<Wall> walls = GetHorizontalSplitCandidateWalls(context, out string wallSource);
            if (walls.Count == 0)
            {
                ShowResult("MHNK Edition", "Select walls first, or open a view with visible walls to lower to ceilings.");
                return Result.Cancelled;
            }

            walls = ReviewHorizontalSplitWalls(context, walls, wallSource);
            if (walls.Count == 0)
            {
                ShowResult("MHNK Edition", "No walls were selected for ceiling-height lowering.");
                return Result.Cancelled;
            }

            IList<Ceiling> ceilings = GetCeilingTrimCandidates(context, out string ceilingSource);
            if (ceilings.Count == 0)
            {
                ShowResult("MHNK Edition", "Select ceilings too, or open a view with visible ceilings.");
                return Result.Cancelled;
            }

            MhnkWallCeilingTrimOptions options = ShowWallCeilingTrimOptions(context, walls, wallSource, ceilings, ceilingSource);
            if (options == null)
            {
                return Result.Cancelled;
            }

            IList<WallCeilingTrimCandidate> candidates = BuildWallCeilingTrimCandidates(context.Document, walls, ceilings, options);
            IList<MhnkCadPreviewItem> previewItems = BuildWallCeilingTrimPreviewItems(candidates, options);
            int readyCount = previewItems.Count(x => x.Status == "Ready");
            if (readyCount == 0)
            {
                ShowResult("MHNK Edition", "No selected wall has a matching ceiling below its current top.");
                return Result.Cancelled;
            }

            if (!ConfirmCadPreview(
                context,
                "Lower Walls to Ceilings Preview",
                BuildCadPreviewSummary(
                    "Lower Walls to Ceilings",
                    walls.Count,
                    ceilings.Count,
                    readyCount,
                    GetWallLevelsSummary(context.Document, walls),
                    "Wall source: " + wallSource + Environment.NewLine +
                    "Ceiling source: " + ceilingSource + Environment.NewLine +
                    "Top offset: " + options.TopOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm" + Environment.NewLine +
                    "Only lower: " + (options.OnlyLowerWalls ? "Yes" : "No")),
                previewItems))
            {
                return Result.Cancelled;
            }

            var affectedIds = new List<ElementId>();
            int changed = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Lower Walls To Ceilings"))
            {
                t.Start();
                foreach (WallCeilingTrimCandidate candidate in candidates)
                {
                    if (!candidate.CanApply)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        SetWallSegmentHeight(
                            candidate.Wall,
                            candidate.Level,
                            candidate.BaseOffset,
                            candidate.TargetHeight,
                            ElementId.InvalidElementId,
                            0.0,
                            false);
                        SetStringParameterIfWritable(candidate.Wall, "Comments", "MHNK lowered to ceiling " + GetElementLabel(candidate.Ceiling));
                        affectedIds.Add(candidate.Wall.Id);
                        changed++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            if (affectedIds.Count > 0)
            {
                context.UiDocument.Selection.SetElementIds(affectedIds);
            }

            ShowResult(
                "MHNK Edition",
                "Lowered " + changed + " wall(s) to ceiling height." + Environment.NewLine +
                "Skipped: " + skipped + Environment.NewLine +
                "Ceilings checked: " + ceilings.Count + Environment.NewLine +
                "Top offset: " + options.TopOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm");
            return changed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result BatchLevelChange(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select elements first, then run Batch Level Change.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            Level level = GetSeedOrActiveLevel(context);
            if (level == null)
            {
                ShowResult("MHNK Edition", "No selected or active level could be resolved.");
                return Result.Cancelled;
            }

            int changed = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Batch Level Change"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Element element = context.Document.GetElement(id);
                    if (element is Level)
                    {
                        continue;
                    }

                    if (TrySetLevelParameter(element, level.Id))
                    {
                        changed++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Moved " + changed + " element(s) to level: " + level.Name + Environment.NewLine + "Skipped " + skipped + " element(s).");
            return changed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result BatchOffsetChange(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select a seed element first, then target elements to receive the offset value.");
                return Result.Cancelled;
            }

            Element seed = context.Document.GetElement(ids[0]);
            Parameter seedOffset = GetBestOffsetParameter(seed);
            if (seedOffset == null || seedOffset.StorageType != StorageType.Double)
            {
                ShowResult("MHNK Edition", "The first selected element does not expose a writable/readable offset parameter.");
                return Result.Cancelled;
            }

            double value = seedOffset.AsDouble();
            int changed = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Batch Offset Change"))
            {
                t.Start();
                foreach (ElementId id in ids.Skip(1))
                {
                    Element element = context.Document.GetElement(id);
                    Parameter target = GetBestOffsetParameter(element);
                    if (target != null && !target.IsReadOnly && target.StorageType == StorageType.Double)
                    {
                        target.Set(value);
                        changed++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Copied offset to " + changed + " selected element(s).");
            return changed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result BatchParameterEdit(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select a seed element first, then target elements to receive Mark/Comments style values.");
                return Result.Cancelled;
            }

            Element seed = context.Document.GetElement(ids[0]);
            IList<ParameterSignature> signatures = GetEditableSeedParameterSignatures(seed);
            if (signatures.Count == 0)
            {
                ShowResult("MHNK Edition", "No useful seed parameters were found. Try Mark or Comments.");
                return Result.Cancelled;
            }

            int changed = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Batch Parameter Edit"))
            {
                t.Start();
                foreach (ElementId id in ids.Skip(1))
                {
                    Element element = context.Document.GetElement(id);
                    foreach (ParameterSignature signature in signatures)
                    {
                        Parameter target = element?.LookupParameter(signature.Name);
                        if (SetParameterFromSignature(target, signature))
                        {
                            changed++;
                        }
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Updated " + changed + " parameter value(s) across selected elements.");
            return changed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result AlignElementsToSeedCenter(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().ToList();
            if (ids.Count < 2)
            {
                ShowResult("MHNK Edition", "Select a reference element first, then target elements to align.");
                return Result.Cancelled;
            }

            Element seed = context.Document.GetElement(ids[0]);
            XYZ targetCenter = GetElementCenter(seed);
            if (targetCenter == null)
            {
                ShowResult("MHNK Edition", "Could not resolve the center point of the first selected element.");
                return Result.Cancelled;
            }

            int moved = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Align Elements"))
            {
                t.Start();
                foreach (ElementId id in ids.Skip(1))
                {
                    Element element = context.Document.GetElement(id);
                    XYZ center = GetElementCenter(element);
                    if (center == null)
                    {
                        continue;
                    }

                    XYZ move = new XYZ(targetCenter.X - center.X, targetCenter.Y - center.Y, 0);
                    if (move.GetLength() < 1e-9)
                    {
                        continue;
                    }

                    try
                    {
                        ElementTransformUtils.MoveElement(context.Document, id, move);
                        moved++;
                    }
                    catch
                    {
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Aligned " + moved + " element(s) to the first selected element center.");
            return moved > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result CopyArrayPreset(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select elements first, then run Copy / Mirror / Array Presets.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            ICollection<ElementId> copied;
            using (Transaction t = new Transaction(context.Document, "MHNK - Copy Array Preset"))
            {
                t.Start();
                copied = ElementTransformUtils.CopyElements(context.Document, ids, new XYZ(MetersToFeet(settings.CopyOffsetMeters), 0, 0));
                t.Commit();
            }

            context.UiDocument.Selection.SetElementIds(copied);
            ShowResult("MHNK Edition", "Created " + copied.Count + " copied element(s) " + settings.CopyOffsetMeters.ToString("0.###") + "m to the right.");
            return copied.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result SelectDuplicateCandidates(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectionElements(context).Count > 1
                ? GetSelectionElements(context)
                : GetVisibleElements(context);

            Dictionary<string, List<Element>> groups = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
            foreach (Element element in elements)
            {
                string key = GetDuplicateKey(element);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!groups.ContainsKey(key))
                {
                    groups[key] = new List<Element>();
                }

                groups[key].Add(element);
            }

            IList<ElementId> duplicateIds = groups.Values
                .Where(x => x.Count > 1)
                .SelectMany(x => x.Skip(1))
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(duplicateIds);
            ShowResult("MHNK Edition", "Selected " + duplicateIds.Count + " duplicate candidate(s) for review. No elements were deleted.");
            return duplicateIds.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result RenameSelectedViewsSheets(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectionElements(context)
                .Where(x => x is View || x is ViewSheet)
                .ToList();

            if (elements.Count == 0)
            {
                ShowResult("MHNK Edition", "Select views or sheets in the Project Browser first.");
                return Result.Cancelled;
            }

            int renamed = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Rename Views Sheets"))
            {
                t.Start();
                foreach (Element element in elements)
                {
                    View view = element as View;
                    if (view == null)
                    {
                        continue;
                    }

                    if (view.Name.StartsWith("MHNK_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        view.Name = CreateUniqueViewName(context.Document, "MHNK_" + SanitizeName(view.Name));
                        renamed++;
                    }
                    catch
                    {
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Renamed " + renamed + " selected view/sheet item(s).");
            return renamed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result ResetGraphicOverrides(MhnkArcContext context)
        {
            IList<ElementId> ids = context.UiDocument.Selection.GetElementIds().ToList();
            if (ids.Count == 0)
            {
                ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(GetArcCoordinationCategories()))
                    .Select(x => x.Id)
                    .ToList();
            }

            var clear = new OverrideGraphicSettings();
            using (Transaction t = new Transaction(context.Document, "MHNK - Reset Graphic Overrides"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    try
                    {
                        context.ActiveView.SetElementOverrides(id, clear);
                    }
                    catch
                    {
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", "Reset active view graphic overrides for " + ids.Count + " element(s).");
            return Result.Succeeded;
        }

        protected static Result CreateBoundingSolidsFromSelection(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select host elements first, then run ARC > Solids > Bounding Solids.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            int created = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Create Bounding Solids"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Element element = context.Document.GetElement(id);
                    BoundingBoxXYZ box = element?.get_BoundingBox(null);
                    Solid solid = CreateBoxSolid(box);
                    if (solid == null)
                    {
                        skipped++;
                        continue;
                    }

                    DirectShape shape = DirectShape.CreateElement(context.Document, new ElementId(BuiltInCategory.OST_GenericModel));
                    shape.ApplicationId = "MHNK";
                    shape.ApplicationDataId = "MHNK_ARC_BOUNDING_SOLID_" + id.Value;
                    shape.Name = "MHNK ARC Bounding Solid";
                    shape.SetShape(new List<GeometryObject> { solid });
                    created++;
                }

                t.Commit();
            }

            ShowResult("MHNK Solids", "Created " + created + " bounding solid(s)." + Environment.NewLine + "Skipped " + skipped + " element(s).");
            return created > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result SelectMhnkGeneratedSolids(MhnkArcContext context)
        {
            IList<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(IsMhnkArcSolid)
                .Select(x => x.Id)
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Solids", "Selected " + ids.Count + " MHNK generated solid(s) in the active view.");
            return Result.Succeeded;
        }

        protected static Result ReportSelectedSolidMetrics(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select model elements first, then run ARC > Solids > Solid Metrics.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            int solidCount = 0;
            double volumeFt3 = 0;
            double areaFt2 = 0;

            foreach (ElementId id in ids)
            {
                Element element = context.Document.GetElement(id);
                AccumulateSolidMetrics(element, ref solidCount, ref volumeFt3, ref areaFt2);
            }

            double volumeM3 = volumeFt3 * 0.028316846592;
            double areaM2 = areaFt2 * 0.09290304;
            string report =
                "Selected elements: " + ids.Count + Environment.NewLine +
                "Detected solids: " + solidCount + Environment.NewLine +
                "Volume: " + volumeM3.ToString("N3") + " m3" + Environment.NewLine +
                "Surface area: " + areaM2.ToString("N3") + " m2";

            ShowResult("MHNK Solids", report);
            return Result.Succeeded;
        }

        protected static Result IntersectionCheck(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectionElements(context).Count > 1
                ? GetSelectionElements(context)
                : GetVisibleElements(context)
                    .Where(x => x.Category != null && IsCategoryInList(x.Category.Id, GetArcCoordinationCategories()))
                    .Take(120)
                    .ToList();

            IList<ElementPair> pairs = FindIntersectingPairs(elements, elements, 40, false);
            IList<ElementId> ids = pairs.SelectMany(x => new[] { x.First.Id, x.Second.Id }).Distinct(new ElementIdComparer()).ToList();
            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Solids", BuildPairReport("Intersection candidates", pairs, "Selected " + ids.Count + " element(s) from candidate pairs."));
            return pairs.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result ClashCandidateCheck(MhnkArcContext context)
        {
            IList<Element> arc = GetVisibleElements(context)
                .Where(x => x.Category != null && IsCategoryInList(x.Category.Id, GetArcHostCategories()))
                .Take(120)
                .ToList();
            IList<Element> mep = GetVisibleElements(context)
                .Where(x => x.Category != null && IsCategoryInList(x.Category.Id, GetMepCoordinationCategories()))
                .Take(120)
                .ToList();

            IList<ElementPair> pairs = FindIntersectingPairs(arc, mep, 60, true);
            IList<ElementId> ids = pairs.SelectMany(x => new[] { x.First.Id, x.Second.Id }).Distinct(new ElementIdComparer()).ToList();
            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Solids", BuildPairReport("ARC/MEP clash candidates", pairs, "Selected " + ids.Count + " element(s) from candidate pairs."));
            return pairs.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result OpeningCandidateCheck(MhnkArcContext context)
        {
            IList<BuiltInCategory> hostCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs
            };

            IList<Element> hosts = GetVisibleElements(context)
                .Where(x => x.Category != null && IsCategoryInList(x.Category.Id, hostCategories))
                .Take(140)
                .ToList();
            IList<Element> mep = GetVisibleElements(context)
                .Where(x => x.Category != null && IsCategoryInList(x.Category.Id, GetMepCoordinationCategories()))
                .Take(140)
                .ToList();

            IList<ElementPair> pairs = FindIntersectingPairs(hosts, mep, 80, true);
            IList<ElementId> ids = pairs.SelectMany(x => new[] { x.First.Id, x.Second.Id }).Distinct(new ElementIdComparer()).ToList();
            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Solids", BuildPairReport("Opening candidates", pairs, "Selected host/MEP elements from candidate pairs."));
            return pairs.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result ConvertSelectedSolidsToDirectShape(MhnkArcContext context)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select model elements first, then run Convert Solid to DirectShape.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            int created = 0;
            int skipped = 0;
            using (Transaction t = new Transaction(context.Document, "MHNK - Convert Solids To DirectShape"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Element element = context.Document.GetElement(id);
                    IList<Solid> solids = CollectElementSolids(element);
                    IList<GeometryObject> geometry = solids.Where(x => x != null && x.Volume > 1e-9).Cast<GeometryObject>().ToList();
                    if (geometry.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        DirectShape shape = DirectShape.CreateElement(context.Document, new ElementId(BuiltInCategory.OST_GenericModel));
                        shape.ApplicationId = "MHNK";
                        shape.ApplicationDataId = "MHNK_ARC_DIRECTSHAPE_COPY_" + id.Value;
                        shape.Name = "MHNK ARC DirectShape Copy";
                        shape.SetShape(geometry);
                        created++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Solids", "Created " + created + " DirectShape copy/copies." + Environment.NewLine + "Skipped " + skipped + " element(s).");
            return created > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result DeleteMhnkGeneratedSolids(MhnkArcContext context)
        {
            IList<ElementId> ids = new FilteredElementCollector(context.Document)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(IsMhnkGeneratedDirectShape)
                .Select(x => x.Id)
                .ToList();

            if (ids.Count == 0)
            {
                ShowResult("MHNK Solids", "No MHNK generated DirectShape solids were found.");
                return Result.Cancelled;
            }

            using (Transaction t = new Transaction(context.Document, "MHNK - Delete Generated Solids"))
            {
                t.Start();
                context.Document.Delete(ids);
                t.Commit();
            }

            ShowResult("MHNK Solids", "Deleted " + ids.Count + " MHNK generated DirectShape solid(s).");
            return Result.Succeeded;
        }

        protected static Result ExportSolidReport(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectionElements(context).Count > 0
                ? GetSelectionElements(context)
                : GetVisibleElements(context).Where(x => x is DirectShape || x is FamilyInstance || x is HostObject).Take(300).ToList();

            var report = new StringBuilder();
            report.AppendLine("MHNK Solid Report");
            report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Document: " + context.Document.Title);
            report.AppendLine("Active view: " + context.ActiveView.Name);
            report.AppendLine("Elements reviewed: " + elements.Count);
            report.AppendLine();

            double totalVolume = 0;
            double totalArea = 0;
            int totalSolids = 0;
            foreach (Element element in elements)
            {
                int solidCount = 0;
                double volumeFt3 = 0;
                double areaFt2 = 0;
                AccumulateSolidMetrics(element, ref solidCount, ref volumeFt3, ref areaFt2);
                if (solidCount == 0)
                {
                    continue;
                }

                totalSolids += solidCount;
                totalVolume += volumeFt3;
                totalArea += areaFt2;
                report.AppendLine(GetElementLabel(element) + " | solids=" + solidCount + " | volume(m3)=" + (volumeFt3 * 0.028316846592).ToString("N3") + " | area(m2)=" + (areaFt2 * 0.09290304).ToString("N3"));
            }

            report.AppendLine();
            report.AppendLine("Total solids: " + totalSolids);
            report.AppendLine("Total volume: " + (totalVolume * 0.028316846592).ToString("N3") + " m3");
            report.AppendLine("Total surface area: " + (totalArea * 0.09290304).ToString("N3") + " m2");

            string path = WriteReportFile("MHNK_Solid_Report", report.ToString());
            ShowResult("MHNK Solids", "Exported solid report:" + Environment.NewLine + path);
            return Result.Succeeded;
        }

        protected static Result ModelHealthReport(MhnkArcContext context)
        {
            int warningCount = context.Document.GetWarnings().Count;
            int importCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .GetElementCount();
            int activeViewImportCount = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .GetElementCount();
            int viewCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(x => !x.IsTemplate);
            int sheetCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ViewSheet))
                .GetElementCount();
            int arcCoordinationCount = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(GetArcCoordinationCategories()))
                .GetElementCount();

            string report =
                "Active view: " + context.ActiveView.Name + Environment.NewLine +
                "Warnings: " + warningCount + Environment.NewLine +
                "CAD imports in model: " + importCount + Environment.NewLine +
                "CAD imports in active view: " + activeViewImportCount + Environment.NewLine +
                "Views: " + viewCount + Environment.NewLine +
                "Sheets: " + sheetCount + Environment.NewLine +
                "ARC coordination elements in active view: " + arcCoordinationCount + Environment.NewLine +
                "Current selection: " + context.UiDocument.Selection.GetElementIds().Count;

            ShowResult("MHNK Xpress", report);
            return Result.Succeeded;
        }

        protected static Result ShowArcQaDashboard(MhnkArcContext context)
        {
            MhnkArcQaDashboardData data = BuildQaDashboardData(context);
            var window = new MhnkArcQaDashboardWindow(data, context.UiApplication.MainWindowHandle);
            window.ShowDialog();
            return Result.Succeeded;
        }

        protected static Result ShowArcWorkflowValidationCenter(MhnkArcContext context)
        {
            var window = new MhnkArcValidationWindow(
                BuildFullOptions(),
                context.Document.Title,
                context.ActiveView.Name,
                context.UiApplication.MainWindowHandle);
            window.ShowDialog();
            return Result.Succeeded;
        }

        protected static Result ShowSolidsInteractionCenter(MhnkArcContext context)
        {
            var window = new MhnkSolidsInteractionWindow(
                context,
                context.UiApplication.MainWindowHandle);
            window.ShowDialog();
            return Result.Succeeded;
        }

        protected static Result SelectCadImportsInActiveView(MhnkArcContext context)
        {
            IList<ElementId> ids = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();

            context.UiDocument.Selection.SetElementIds(ids);
            ShowResult("MHNK Xpress", "Selected " + ids.Count + " CAD import(s) in the active view.");
            return Result.Succeeded;
        }

        protected static Result ShowArcToolSettings(MhnkArcContext context)
        {
            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            var window = new MhnkArcToolSettingsWindow(settings, context.UiApplication.MainWindowHandle);
            bool? saved = window.ShowDialog();
            if (saved == true)
            {
                ShowResult(
                    "MHNK Xpress",
                    "ARC tool settings saved." + Environment.NewLine +
                    MhnkArcToolSettings.GetSettingsPath());
                return Result.Succeeded;
            }

            return Result.Cancelled;
        }

        protected static Result ToggleDarkTheme(MhnkArcContext context)
        {
            bool dark = MhnkUiTheme.Toggle();
            ShowResult(
                "MHNK Xpress",
                "MHNK ARC theme is now " + (dark ? "Dark" : "Light") + "." + Environment.NewLine +
                "Theme file: " + MhnkUiTheme.GetThemePath() + Environment.NewLine +
                "Open the ARC menu again to see the updated theme.");
            return Result.Succeeded;
        }

        protected static Result ShowSmartMappingManager(MhnkArcContext context)
        {
            MhnkArcSmartMappingRules rules = MhnkArcSmartMappingRules.Load();
            var window = new MhnkArcSmartMappingWindow(rules, context.UiApplication.MainWindowHandle);
            bool? saved = window.ShowDialog();
            if (saved == true)
            {
                ShowResult(
                    "MHNK Xpress",
                    "Smart mapping rules saved." + Environment.NewLine +
                    MhnkArcSmartMappingRules.GetRulesPath());
                return Result.Succeeded;
            }

            return Result.Cancelled;
        }

        protected static Result CleanCadImports(MhnkArcContext context)
        {
            IList<ElementId> selectedCadIds = context.UiDocument.Selection.GetElementIds()
                .Where(id => context.Document.GetElement(id) is ImportInstance)
                .ToList();

            if (selectedCadIds.Count > 0)
            {
                using (Transaction t = new Transaction(context.Document, "MHNK - Temporarily Hide CAD Imports"))
                {
                    t.Start();
                    context.ActiveView.HideElementsTemporary(selectedCadIds);
                    t.Commit();
                }

                context.UiDocument.RefreshActiveView();
                ShowResult("MHNK Xpress", "Temporarily hidden " + selectedCadIds.Count + " selected CAD import(s).");
                return Result.Succeeded;
            }

            return SelectCadImportsInActiveView(context);
        }

        protected static Result PrepareArcCoordinationWorkspace(MhnkArcContext context)
        {
            CreateArcCoordinationView(context);
            ModelHealthReport(context);
            return Result.Succeeded;
        }

        protected static Result CheckMissingTypes(MhnkArcContext context)
        {
            var report = new StringBuilder();
            report.AppendLine("Required ARC type check");
            report.AppendLine();
            AppendTypeCheck(report, "Wall type", HasElementType<WallType>(context.Document));
            AppendTypeCheck(report, "Floor type", HasElementType<FloorType>(context.Document));
            AppendTypeCheck(report, "Ceiling type", HasElementType<CeilingType>(context.Document));
            AppendTypeCheck(report, "Door family symbol", HasFamilySymbol(context.Document, BuiltInCategory.OST_Doors));
            AppendTypeCheck(report, "Window family symbol", HasFamilySymbol(context.Document, BuiltInCategory.OST_Windows));
            AppendTypeCheck(report, "Room objects", new FilteredElementCollector(context.Document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount() > 0);

            ShowResult("MHNK Xpress", report.ToString());
            return Result.Succeeded;
        }

        protected static Result CheckUnjoinedWalls(MhnkArcContext context)
        {
            IList<Wall> walls = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Take(300)
                .ToList();

            IList<ElementId> unjoined = new List<ElementId>();
            foreach (Wall wall in walls)
            {
                try
                {
                    if (JoinGeometryUtils.GetJoinedElements(context.Document, wall).Count == 0)
                    {
                        unjoined.Add(wall.Id);
                    }
                }
                catch
                {
                }
            }

            int joinWarningCount = context.Document.GetWarnings()
                .Count(x => (x.GetDescriptionText() ?? "").IndexOf("join", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (x.GetDescriptionText() ?? "").IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0);

            context.UiDocument.Selection.SetElementIds(unjoined);
            ShowResult(
                "MHNK Xpress",
                "Walls checked in active view: " + walls.Count + Environment.NewLine +
                "Walls with no joined neighbor selected: " + unjoined.Count + Environment.NewLine +
                "Join/wall-related warnings: " + joinWarningCount);
            return Result.Succeeded;
        }

        protected static Result CheckRoomBoundaryIssues(MhnkArcContext context)
        {
            IList<Room> rooms = new FilteredElementCollector(context.Document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            IList<ElementId> issueIds = rooms
                .Where(x => x.Area <= 1e-6 || string.Equals(x.Location?.ToString(), "", StringComparison.Ordinal))
                .Select(x => x.Id)
                .ToList();

            int boundaryCapableVisible = GetVisibleElements(context)
                .Count(x => GetRoomBoundingParameter(x) != null);

            context.UiDocument.Selection.SetElementIds(issueIds);
            ShowResult(
                "MHNK Xpress",
                "Rooms checked: " + rooms.Count + Environment.NewLine +
                "Rooms with zero/no area selected: " + issueIds.Count + Environment.NewLine +
                "Visible room-bounding-capable elements: " + boundaryCapableVisible);
            return Result.Succeeded;
        }

        protected static Result CheckOpenings(MhnkArcContext context)
        {
            IList<ElementId> openingIds = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(Opening))
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();

            IList<ElementId> candidateIds = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(x => (x.ApplicationDataId ?? "").StartsWith("MHNK_ARC_OPENING_CANDIDATE_", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .ToList();

            IList<ElementId> all = openingIds.Concat(candidateIds).Distinct(new ElementIdComparer()).ToList();
            context.UiDocument.Selection.SetElementIds(all);
            ShowResult(
                "MHNK Xpress",
                "Revit openings in active view: " + openingIds.Count + Environment.NewLine +
                "MHNK opening candidate solids: " + candidateIds.Count + Environment.NewLine +
                "Selected review elements: " + all.Count);
            return Result.Succeeded;
        }

        protected static Result AutoJoinArcElements(MhnkArcContext context)
        {
            IList<Element> elements = GetSelectionElements(context).Count > 1
                ? GetSelectionElements(context)
                : GetVisibleElements(context)
                    .Where(x => x.Category != null && IsCategoryInList(x.Category.Id, GetArcHostCategories()))
                    .Take(90)
                    .ToList();

            IList<ElementPair> pairs = FindIntersectingPairs(elements, elements, 120, false);
            int joined = 0;
            int skipped = 0;

            using (Transaction t = new Transaction(context.Document, "MHNK - Auto Join ARC Elements"))
            {
                t.Start();
                foreach (ElementPair pair in pairs)
                {
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(context.Document, pair.First, pair.Second))
                        {
                            JoinGeometryUtils.JoinGeometry(context.Document, pair.First, pair.Second);
                            joined++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Xpress", "Auto-joined " + joined + " ARC candidate pair(s)." + Environment.NewLine + "Skipped " + skipped + " pair(s).");
            return joined > 0 ? Result.Succeeded : Result.Cancelled;
        }

        protected static Result ExportQaReport(MhnkArcContext context)
        {
            string report = BuildQaDashboardData(context).Report;
            string path = WriteReportFile("MHNK_QA_Report", report);
            ShowResult("MHNK Xpress", "Exported QA report:" + Environment.NewLine + path);
            return Result.Succeeded;
        }

        protected static Result ShowDiagnosticsReport(MhnkArcContext context)
        {
            TaskDialog.Show("MHNK Diagnostics", MhnkDiagnostics.BuildReport(context.CommandData));
            return Result.Succeeded;
        }

        protected static Result ShowWarningsReport(MhnkArcContext context)
        {
            IList<FailureMessage> warnings = context.Document.GetWarnings();
            var report = new StringBuilder();
            report.AppendLine("Warnings: " + warnings.Count);

            foreach (FailureMessage warning in warnings.Take(20))
            {
                report.AppendLine();
                report.AppendLine("- " + warning.GetDescriptionText());
                ICollection<ElementId> failingIds = warning.GetFailingElements();
                if (failingIds != null && failingIds.Count > 0)
                {
                    report.AppendLine("  Elements: " + string.Join(", ", failingIds.Take(8).Select(x => x.Value.ToString()).ToArray()));
                }
            }

            if (warnings.Count > 20)
            {
                report.AppendLine();
                report.AppendLine("Showing first 20 warnings.");
            }

            ShowResult("MHNK Xpress", report.ToString());
            return Result.Succeeded;
        }

        private static IList<Element> GetVisibleElements(MhnkArcContext context)
        {
            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
        }

        private static IList<Element> GetSelectionElements(MhnkArcContext context)
        {
            if (context.SourceMode == MhnkArcSourceMode.All)
            {
                return GetVisibleElements(context);
            }

            if (context.SourceMode == MhnkArcSourceMode.ByLayer)
            {
                IList<Element> layerElements = GetLayerSourceElementIds(context)
                    .Select(id => context.Document.GetElement(id))
                    .Where(x => x != null)
                    .ToList();
                if (layerElements.Count > 0)
                {
                    return layerElements;
                }
            }

            if (context.SourceMode == MhnkArcSourceMode.Category)
            {
                IList<Element> categoryElements = GetCategoryScopeElementIds(context)
                    .Select(id => context.Document.GetElement(id))
                    .Where(x => x != null)
                    .ToList();
                if (categoryElements.Count > 0)
                {
                    return categoryElements;
                }
            }

            return context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id))
                .Where(x => x != null)
                .ToList();
        }

        private static Level GetSeedOrActiveLevel(MhnkArcContext context)
        {
            Element seed = GetFirstSelectedElement(context);
            if (seed is Level seedLevel)
            {
                return seedLevel;
            }

            ElementId seedLevelId = GetElementLevelId(seed);
            Level level = seedLevelId == ElementId.InvalidElementId ? null : context.Document.GetElement(seedLevelId) as Level;
            if (level != null)
            {
                return level;
            }

            if (context.ActiveView is ViewPlan viewPlan && viewPlan.GenLevel != null)
            {
                return viewPlan.GenLevel;
            }

            return GetActiveOrFirstLevel(context.Document, context.ActiveView);
        }

        private static ElementId GetElementLevelId(Element element)
        {
            if (element == null)
            {
                return ElementId.InvalidElementId;
            }

            try
            {
                if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
                {
                    return element.LevelId;
                }
            }
            catch
            {
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter?.Definition == null ||
                    parameter.StorageType != StorageType.ElementId)
                {
                    continue;
                }

                string name = parameter.Definition.Name ?? "";
                if (name.IndexOf("Level", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Base Constraint", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                ElementId id = parameter.AsElementId();
                if (id != null && id != ElementId.InvalidElementId)
                {
                    return id;
                }
            }

            return ElementId.InvalidElementId;
        }

        private static bool ElementIdEquals(ElementId first, ElementId second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return string.Equals(GetElementIdText(first), GetElementIdText(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetElementIdText(ElementId id)
        {
            return id == null ? "" : id.Value.ToString();
        }

        private static string GetWorksetName(Document document, WorksetId id)
        {
            try
            {
                Workset workset = document.GetWorksetTable().GetWorkset(id);
                return workset?.Name ?? id.ToString();
            }
            catch
            {
                return id == null ? "" : id.ToString();
            }
        }

        private static ElementId GetCreatedPhaseId(Element element)
        {
            if (element == null)
            {
                return ElementId.InvalidElementId;
            }

            try
            {
                return element.CreatedPhaseId ?? ElementId.InvalidElementId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        private static ParameterSignature GetBestParameterSignature(Element element)
        {
            if (element == null)
            {
                return null;
            }

            string[] preferredNames =
            {
                "Mark",
                "Type Mark",
                "Comments",
                "Description",
                "Fire Rating",
                "Level",
                "Base Constraint",
                "Reference Level"
            };

            foreach (string name in preferredNames)
            {
                Parameter parameter = element.LookupParameter(name);
                ParameterSignature signature = CreateParameterSignature(parameter);
                if (signature != null)
                {
                    return signature;
                }
            }

            foreach (Parameter parameter in element.Parameters)
            {
                ParameterSignature signature = CreateParameterSignature(parameter);
                if (signature != null)
                {
                    return signature;
                }
            }

            return null;
        }

        private static ParameterSignature CreateParameterSignature(Parameter parameter)
        {
            if (parameter == null || parameter.Definition == null)
            {
                return null;
            }

            string value = GetParameterComparableValue(parameter);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return new ParameterSignature(
                parameter.Definition.Name,
                parameter.StorageType,
                value,
                GetParameterDisplayValue(parameter));
        }

        private static bool ParameterMatches(Element element, ParameterSignature signature)
        {
            if (element == null || signature == null)
            {
                return false;
            }

            Parameter parameter = element.LookupParameter(signature.Name);
            if (parameter == null || parameter.StorageType != signature.StorageType)
            {
                return false;
            }

            string value = GetParameterComparableValue(parameter);
            return string.Equals(value, signature.ComparableValue, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetParameterComparableValue(Parameter parameter)
        {
            if (parameter == null)
            {
                return "";
            }

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        return (parameter.AsString() ?? "").Trim();
                    case StorageType.Integer:
                        return parameter.AsInteger().ToString();
                    case StorageType.Double:
                        return parameter.AsDouble().ToString("R");
                    case StorageType.ElementId:
                        return GetElementIdText(parameter.AsElementId());
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static string GetParameterDisplayValue(Parameter parameter)
        {
            if (parameter == null)
            {
                return "";
            }

            try
            {
                string display = parameter.AsValueString();
                if (!string.IsNullOrWhiteSpace(display))
                {
                    return display;
                }
            }
            catch
            {
            }

            return GetParameterComparableValue(parameter);
        }

        private static IList<string> GetCadLayerNames(Document document, IList<ImportInstance> imports)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            foreach (ImportInstance import in imports)
            {
                GeometryElement geometry = import.get_Geometry(options);
                CollectCadLayerNames(document, geometry, names);
            }

            return names.ToList();
        }

        private static void CollectCadLayerNames(Document document, GeometryElement geometry, ISet<string> names)
        {
            if (geometry == null)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject == null)
                {
                    continue;
                }

                try
                {
                    GraphicsStyle style = document.GetElement(geometryObject.GraphicsStyleId) as GraphicsStyle;
                    string name = style?.GraphicsStyleCategory?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
                catch
                {
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance != null)
                {
                    CollectCadLayerNames(document, instance.GetInstanceGeometry(), names);
                }
            }
        }

        private static string GetFilterPresetPath(Document document)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "ArcPresets");
            return Path.Combine(root, SanitizeName(document.Title) + "_filter.txt");
        }

        private static Dictionary<string, string> ReadKeyValueFile(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                values[line.Substring(0, index).Trim()] = line.Substring(index + 1).Trim();
            }

            return values;
        }

        private static bool ConfirmCadPreview(
            MhnkArcContext context,
            string title,
            string summary,
            IList<MhnkCadPreviewItem> items)
        {
            var window = new MhnkCadToModelPreviewWindow(title, summary, items, context.UiApplication.MainWindowHandle);
            return window.ShowDialog() == true;
        }

        private static string BuildCadPreviewSummary(
            string action,
            int selectedElementCount,
            int detectedCurveCount,
            int readyCount,
            string levelOrView,
            string targetDetails)
        {
            return
                "Action: " + action + Environment.NewLine +
                "Selected source elements: " + selectedElementCount + Environment.NewLine +
                "Detected source curves: " + detectedCurveCount + Environment.NewLine +
                "Ready items: " + readyCount + Environment.NewLine +
                "Level / View: " + levelOrView + Environment.NewLine +
                targetDetails + Environment.NewLine +
                "Settings: " + MhnkArcToolSettings.GetSettingsPath() + Environment.NewLine +
                "Mapping rules: " + MhnkArcSmartMappingRules.GetRulesPath();
        }

        private static IList<ElementId> GetCadManagerSourceIds(MhnkArcContext context)
        {
            return GetCurveSourceElementIds(context, MhnkCadCurvePurpose.Any);
        }

        private static IList<ElementId> GetCurveSourceElementIds(MhnkArcContext context, MhnkCadCurvePurpose purpose)
        {
            if (context == null)
            {
                return new List<ElementId>();
            }

            if (context.SourceMode == MhnkArcSourceMode.ByLayer)
            {
                return GetLayerSourceElementIds(context);
            }

            if (context.SourceMode == MhnkArcSourceMode.Category)
            {
                return GetCategoryCurveSourceElementIds(context, purpose);
            }

            if (context.SourceMode == MhnkArcSourceMode.All)
            {
                return GetVisibleCurveSourceElementIds(context);
            }

            return context.UiDocument.Selection.GetElementIds()
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .ToList();
        }

        private static IList<ElementId> GetLayerSourceElementIds(MhnkArcContext context)
        {
            IList<ElementId> selectedImports = context.UiDocument.Selection.GetElementIds()
                .Where(id => context.Document.GetElement(id) is ImportInstance)
                .ToList();
            if (selectedImports.Count > 0)
            {
                return selectedImports;
            }

            return GetVisibleCadImportIds(context);
        }

        private static IList<ElementId> GetVisibleCadImportIds(MhnkArcContext context)
        {
            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();
        }

        private static IList<ElementId> GetCategoryCurveSourceElementIds(
            MhnkArcContext context,
            MhnkCadCurvePurpose purpose)
        {
            if (purpose == MhnkCadCurvePurpose.Any)
            {
                return GetVisibleCurveSourceElementIds(context);
            }

            return GetVisibleCurveSourceElementIds(context);
        }

        private static IList<ElementId> GetVisibleCurveSourceElementIds(MhnkArcContext context)
        {
            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(IsCurveSourceElement)
                .Select(x => x.Id)
                .ToList();
        }

        private static bool IsCurveSourceElement(Element element)
        {
            return element is ImportInstance ||
                   element is CurveElement ||
                   element is Grid;
        }

        private static string GetSourceModeSummary(MhnkArcContext context)
        {
            return context == null ? "" : "Source mode: " + context.SourceModeName;
        }

        private static IList<MhnkCadToModelScanItem> BuildCadToModelScanItems(
            Document document,
            IList<CadCurveCandidate> candidates,
            MhnkArcToolSettings settings)
        {
            var groups = new Dictionary<string, List<CadCurveCandidate>>(StringComparer.OrdinalIgnoreCase);
            foreach (CadCurveCandidate candidate in candidates ?? new List<CadCurveCandidate>())
            {
                string action = GetCadManagerAction(candidate, settings);
                string ruleName = GetMappingRuleName(candidate.MappingRule);
                string layer = string.IsNullOrWhiteSpace(candidate.LayerName) ? "(model)" : candidate.LayerName;
                string key = action + "|" + ruleName + "|" + layer;
                List<CadCurveCandidate> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<CadCurveCandidate>();
                    groups[key] = group;
                }

                group.Add(candidate);
            }

            var items = new List<MhnkCadToModelScanItem>();
            foreach (List<CadCurveCandidate> group in groups.Values)
            {
                CadCurveCandidate first = group.First();
                string action = GetCadManagerAction(first, settings);
                IList<ElementId> sourceIds = group
                    .Select(x => x.SourceElementId)
                    .Distinct(new ElementIdComparer())
                    .ToList();

                bool mapped = first.MappingRule != null || !string.Equals(action, "Unmapped", StringComparison.OrdinalIgnoreCase);
                string status = mapped && IsCadManagerActionRunnable(action) ? "Ready" : "Review";
                items.Add(new MhnkCadToModelScanItem
                {
                    Include = string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase),
                    Action = GetCadManagerActionLabel(action),
                    Rule = GetMappingRuleName(first.MappingRule),
                    Layer = string.IsNullOrWhiteSpace(first.LayerName) ? "(model)" : first.LayerName,
                    Target = GetCadManagerTarget(document, first, settings, action),
                    SourceElements = sourceIds.Count.ToString(CultureInfo.InvariantCulture),
                    CurveCount = group.Count,
                    Status = status,
                    Notes = GetCadManagerNotes(first, settings, action),
                    SourceElementIds = sourceIds
                });
            }

            return items
                .OrderBy(x => GetCadManagerActionOrder(GetCadManagerActionKeyFromLabel(x.Action)))
                .ThenBy(x => x.Layer, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetCadManagerAction(CadCurveCandidate candidate, MhnkArcToolSettings settings)
        {
            if (candidate == null)
            {
                return "Unmapped";
            }

            if (candidate.MappingRule != null)
            {
                string mappedAction = MhnkArcSmartMappingRules.NormalizeAction(candidate.MappingRule.Action);
                return string.Equals(mappedAction, "Door", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(mappedAction, "Window", StringComparison.OrdinalIgnoreCase)
                    ? "DoorsWindows"
                    : mappedAction;
            }

            string layer = candidate.LayerName ?? "";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.WallLayerKeywords))) return "Wall";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.FloorLayerKeywords))) return "Floor";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.CeilingLayerKeywords))) return "Ceiling";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.OpeningLayerKeywords))) return "Opening";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.WindowLayerKeywords))) return "DoorsWindows";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.DoorLayerKeywords))) return "DoorsWindows";
            if (IsCadLayerMatch(layer, settings.SplitKeywords(settings.RoomBoundaryLayerKeywords))) return "RoomBoundary";
            return "Unmapped";
        }

        private static string GetCadManagerActionLabel(string action)
        {
            switch (action)
            {
                case "Wall": return "Walls";
                case "Floor": return "Floors";
                case "Ceiling": return "Ceilings";
                case "Room": return "Rooms";
                case "RoomBoundary": return "Room Boundaries";
                case "Opening": return "Openings";
                case "DoorsWindows": return "Doors / Windows";
                default: return "Unmapped";
            }
        }

        private static string GetCadManagerActionKeyFromLabel(string label)
        {
            switch (label)
            {
                case "Walls": return "Wall";
                case "Floors": return "Floor";
                case "Ceilings": return "Ceiling";
                case "Rooms": return "Room";
                case "Room Boundaries": return "RoomBoundary";
                case "Openings": return "Opening";
                case "Doors / Windows": return "DoorsWindows";
                default: return "Unmapped";
            }
        }

        private static bool IsCadManagerActionRunnable(string action)
        {
            return !string.Equals(action, "Unmapped", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetCadManagerActionOrder(string action)
        {
            switch (action)
            {
                case "Wall": return 10;
                case "RoomBoundary": return 20;
                case "Floor": return 30;
                case "Ceiling": return 40;
                case "Room": return 50;
                case "Opening": return 60;
                case "DoorsWindows": return 70;
                default: return 999;
            }
        }

        private static string GetCadManagerTarget(Document document, CadCurveCandidate candidate, MhnkArcToolSettings settings, string action)
        {
            switch (action)
            {
                case "Wall":
                    WallType wallType = GetPreferredWallType(document, settings, candidate.MappingRule);
                    return wallType == null ? "Missing wall type" : wallType.Name;
                case "Floor":
                    FloorType floorType = GetPreferredFloorType(document, new List<CadCurveCandidate> { candidate });
                    return floorType == null ? "Missing floor type" : floorType.Name;
                case "Ceiling":
                    CeilingType ceilingType = GetPreferredCeilingType(document, new List<CadCurveCandidate> { candidate });
                    return ceilingType == null ? "Missing ceiling type" : ceilingType.Name;
                case "DoorsWindows":
                    FamilySymbol door = GetPreferredFamilySymbol(document, BuiltInCategory.OST_Doors, settings.SplitKeywords(settings.DoorTypeKeywords));
                    FamilySymbol window = GetPreferredFamilySymbol(document, BuiltInCategory.OST_Windows, settings.SplitKeywords(settings.WindowTypeKeywords));
                    return GetDoorWindowTargetName(document, candidate, settings, door, window);
                case "Room":
                    return "Room";
                case "RoomBoundary":
                    return "Room separation lines";
                case "Opening":
                    return "Opening candidate solid";
                default:
                    return "No target";
            }
        }

        private static string GetCadManagerNotes(CadCurveCandidate candidate, MhnkArcToolSettings settings, string action)
        {
            if (candidate.MappingRule == null)
            {
                return "Fallback keyword settings";
            }

            if (string.Equals(action, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                return "Height " + GetWallHeightMeters(settings, candidate.MappingRule).ToString("0.###") + " m";
            }

            if (string.Equals(action, "Opening", StringComparison.OrdinalIgnoreCase))
            {
                double depth = candidate.MappingRule.DepthMeters > 0.0 ? candidate.MappingRule.DepthMeters : settings.OpeningDepthMeters;
                return "Depth " + depth.ToString("0.###") + " m";
            }

            if (!string.IsNullOrWhiteSpace(candidate.MappingRule.RevitTypeKeywords))
            {
                return "Type keywords: " + candidate.MappingRule.RevitTypeKeywords;
            }

            return string.IsNullOrWhiteSpace(candidate.MappingRule.Notes) ? "Mapped" : candidate.MappingRule.Notes;
        }

        private static Result RunCadToModelScanItems(MhnkArcContext context, IList<MhnkCadToModelScanItem> items)
        {
            bool anySucceeded = false;
            foreach (string action in GetCadManagerSelectedActions(items))
            {
                IList<ElementId> ids = items
                    .Where(x => string.Equals(GetCadManagerActionKeyFromLabel(x.Action), action, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.SourceElementIds ?? new List<ElementId>())
                    .Distinct(new ElementIdComparer())
                    .ToList();

                if (ids.Count == 0)
                {
                    continue;
                }

                context.UiDocument.Selection.SetElementIds(ids);
                Result result = RunCadManagerAction(context, action);
                if (result == Result.Succeeded)
                {
                    anySucceeded = true;
                }
                else if (result == Result.Cancelled)
                {
                    break;
                }
            }

            return anySucceeded ? Result.Succeeded : Result.Cancelled;
        }

        private static IList<string> GetCadManagerSelectedActions(IList<MhnkCadToModelScanItem> items)
        {
            return (items ?? new List<MhnkCadToModelScanItem>())
                .Select(x => GetCadManagerActionKeyFromLabel(x.Action))
                .Where(IsCadManagerActionRunnable)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetCadManagerActionOrder)
                .ToList();
        }

        private static Result RunCadManagerAction(MhnkArcContext context, string action)
        {
            switch (action)
            {
                case "Wall":
                    return CreateWallsFromSelectedCurves(context);
                case "Floor":
                    return CreateFloorsFromSelectedCurves(context);
                case "Ceiling":
                    return CreateCeilingsFromSelectedCurves(context);
                case "Room":
                    return CreateRoomsFromSelectedCurves(context);
                case "RoomBoundary":
                    return CreateRoomBoundaryLinesFromSelectedCurves(context);
                case "Opening":
                    return CreateOpeningCandidatesFromSelectedCurves(context);
                case "DoorsWindows":
                    return PlaceDoorWindowCandidatesFromSelectedCurves(context);
                default:
                    return Result.Cancelled;
            }
        }

        private static IList<MhnkCadPreviewItem> BuildCurvePreviewItems(
            IList<CadCurveCandidate> candidates,
            Func<CadCurveCandidate, string> target,
            Func<CadCurveCandidate, string> status,
            Func<CadCurveCandidate, string> notes)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (CadCurveCandidate candidate in candidates ?? new List<CadCurveCandidate>())
            {
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(),
                    Source = "Element " + GetElementIdText(candidate.SourceElementId),
                    Layer = string.IsNullOrWhiteSpace(candidate.LayerName) ? "(model)" : candidate.LayerName,
                    Rule = GetMappingRuleName(candidate.MappingRule),
                    Target = target(candidate),
                    Quantity = "1",
                    Status = status(candidate),
                    Notes = notes(candidate)
                });
                index++;
            }

            return items;
        }

        private static IList<MhnkCadPreviewItem> BuildLoopPreviewItems(IList<CurveLoop> loops, string target, string notes)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (CurveLoop loop in loops ?? new List<CurveLoop>())
            {
                int curveCount = loop.Count();
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(),
                    Source = "Closed loop",
                    Layer = "(mixed)",
                    Rule = "",
                    Target = target,
                    Quantity = curveCount.ToString(),
                    Status = "Ready",
                    Notes = notes + "; curves " + curveCount
                });
                index++;
            }

            return items;
        }

        private static MhnkRoomCreationOptions ShowRoomCreationOptions(
            MhnkArcContext context,
            MhnkRoomCreationMode mode,
            IList<Room> rooms,
            string roomSource,
            IList<MhnkElementTypeOption> typeOptions,
            ElementId defaultTypeId,
            double defaultWallHeightMeters,
            double defaultOffsetMillimeters)
        {
            string summary =
                "Rooms: " + (rooms?.Count ?? 0) + Environment.NewLine +
                "Source: " + (roomSource ?? "") + Environment.NewLine +
                "Levels: " + GetRoomLevelsSummary(context.Document, rooms ?? new List<Room>()) + Environment.NewLine +
                "Bounded area: " + FormatAreaSquareMeters((rooms ?? new List<Room>()).Sum(x => SafeRoomArea(x)));

            var window = new MhnkRoomCreationOptionsWindow(
                mode,
                summary,
                typeOptions ?? new List<MhnkElementTypeOption>(),
                defaultTypeId,
                defaultWallHeightMeters,
                defaultOffsetMillimeters,
                context.UiApplication.MainWindowHandle,
                GetLevelOptions(context.Document),
                mode == MhnkRoomCreationMode.Walls ? GetDefaultRoomWallLevelId(context) : null);

            return window.ShowDialog() == true ? window.SelectedOptions : null;
        }

        private static IList<Room> GetRoomCreationCandidateRooms(MhnkArcContext context, out string source, bool includeAllModelRooms = false)
        {
            IList<Room> selectedRooms = context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id) as Room)
                .Where(IsUsableRoom)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetRoomLevelName(context.Document, x))
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Name)
                .ToList();

            if (context.SourceMode == MhnkArcSourceMode.FreeSelect && selectedRooms.Count > 0)
            {
                source = "Selected rooms (" + context.SourceModeName + ")";
                return selectedRooms;
            }

            if (context.SourceMode == MhnkArcSourceMode.FreeSelect)
            {
                source = "Free Select - no selected rooms";
                return new List<Room>();
            }

            if (includeAllModelRooms)
            {
                if (context.SourceMode == MhnkArcSourceMode.All)
                {
                    IList<Room> modelRooms = new FilteredElementCollector(context.Document)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(IsUsableRoom)
                        .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .OrderBy(x => GetRoomLevelName(context.Document, x))
                        .ThenBy(x => x.Number)
                        .ThenBy(x => x.Name)
                        .ToList();

                    source = modelRooms.Count > 0 ? "All bounded rooms in model" : "No bounded rooms";
                    return modelRooms;
                }
            }

            IList<Room> visibleRooms = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(IsUsableRoom)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetRoomLevelName(context.Document, x))
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Name)
                .ToList();

            source = visibleRooms.Count > 0
                ? "Visible bounded rooms in active view (" + context.SourceModeName + ")"
                : "No bounded rooms";
            return visibleRooms;
        }

        private static IList<Room> FilterRoomsBySelectedLevel(
            Document document,
            IList<Room> rooms,
            MhnkRoomCreationOptions options)
        {
            if (options == null || options.UseAllLevels || options.SelectedLevelId == null || options.SelectedLevelId == ElementId.InvalidElementId)
            {
                return rooms ?? new List<Room>();
            }

            return (rooms ?? new List<Room>())
                .Where(x => ElementIdEquals(GetElementLevelId(x), options.SelectedLevelId))
                .OrderBy(x => GetRoomLevelName(document, x))
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Name)
                .ToList();
        }

        private static bool IsUsableRoom(Room room)
        {
            return room != null && SafeRoomArea(room) > 1e-6 && GetRoomBoundarySegmentGroups(room).Count > 0;
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

        private static IList<RoomBoundaryCurveCandidate> GetRoomWallBoundaryCandidates(
            Document document,
            IList<Room> rooms,
            MhnkRoomCreationOptions options)
        {
            var boundaries = new Dictionary<string, RoomBoundaryAggregate>(StringComparer.OrdinalIgnoreCase);
            foreach (Room room in rooms ?? new List<Room>())
            {
                Level level = GetRoomLevel(document, room);
                if (level == null)
                {
                    continue;
                }

                foreach (BoundarySegment segment in GetRoomBoundarySegmentGroups(room).SelectMany(x => x))
                {
                    if (segment == null)
                    {
                        continue;
                    }

                    ElementId boundaryElementId = segment.ElementId;
                    Element boundaryElement = boundaryElementId == null || boundaryElementId == ElementId.InvalidElementId
                        ? null
                        : document.GetElement(boundaryElementId);
                    if (options != null && options.SkipExistingWallSegments && boundaryElement is Wall)
                    {
                        continue;
                    }

                    Curve curve = ProjectCurveToLevel(segment.GetCurve(), level.Elevation);
                    if (curve == null || curve.Length < ShortCurveTolerance)
                    {
                        continue;
                    }

                    string key = GetCurveDedupeKey(curve);
                    if (boundaries.TryGetValue(key, out RoomBoundaryAggregate existing))
                    {
                        existing.AddRoom(room);
                        continue;
                    }

                    boundaries[key] = new RoomBoundaryAggregate(room, level, curve, boundaryElementId, boundaryElement?.Name ?? "");
                }
            }

            return boundaries
                .Select(x => new RoomBoundaryCurveCandidate(
                    x.Value.Room,
                    x.Value.Level,
                    x.Value.Curve,
                    x.Value.BoundaryElementId,
                    x.Value.BoundaryElementName,
                    x.Value.AdjacentRoomCount))
                .OrderBy(x => x.Level.Elevation)
                .ThenBy(x => x.IsExteriorBoundary ? 0 : 1)
                .ThenBy(x => x.Room.Number)
                .ToList();
        }

        private static IList<RoomBoundaryCurveCandidate> ShowRoomBoundaryLineReview(
            MhnkArcContext context,
            IList<RoomBoundaryCurveCandidate> candidates,
            MhnkRoomCreationOptions options)
        {
            var items = (candidates ?? new List<RoomBoundaryCurveCandidate>())
                .Select((candidate, index) => new MhnkRoomBoundaryLineReviewItem
                {
                    IsSelected = true,
                    Index = index + 1,
                    Room = GetRoomLabel(candidate.Room),
                    Level = candidate.Level?.Name ?? "",
                    Boundary = string.IsNullOrWhiteSpace(candidate.BoundaryElementName) ? "Room line" : candidate.BoundaryElementName,
                    Kind = candidate.IsExteriorBoundary ? "Exterior" : "Interior",
                    Length = FormatLengthMeters(candidate.Curve.Length),
                    Notes = candidate.AdjacentRoomCount > 1
                        ? "Shared by " + candidate.AdjacentRoomCount + " rooms"
                        : "Single room boundary"
                })
                .ToList();

            string summary =
                "Mode: Lines from Rooms" + Environment.NewLine +
                "Retrieved lines: " + items.Count + Environment.NewLine +
                "Level option: " + GetSelectedLevelSummary(context.Document, options) + Environment.NewLine +
                "Clear any boundary lines that should not create a wall.";
            var window = new MhnkRoomBoundaryLineReviewWindow(
                "Create Walls - Retrieved Lines",
                summary,
                items,
                context.UiApplication.MainWindowHandle);
            if (window.ShowDialog() != true)
            {
                return null;
            }

            HashSet<int> selectedIndices = items
                .Where(x => x.IsSelected)
                .Select(x => x.Index)
                .ToHashSet();
            return (candidates ?? new List<RoomBoundaryCurveCandidate>())
                .Where((candidate, index) => selectedIndices.Contains(index + 1))
                .ToList();
        }

        private static IList<RoomLoopCandidate> GetRoomLoopCandidates(Document document, IList<Room> rooms)
        {
            var candidates = new List<RoomLoopCandidate>();
            foreach (Room room in rooms ?? new List<Room>())
            {
                Level level = GetRoomLevel(document, room);
                if (level == null)
                {
                    continue;
                }

                IList<CurveLoop> loops = GetRoomBoundaryCurveLoops(room, level.Elevation);
                if (loops.Count == 0)
                {
                    continue;
                }

                candidates.Add(new RoomLoopCandidate(room, level, loops));
            }

            return candidates;
        }

        private static IList<RoomLoopCandidate> ReviewRoomLoopCandidates(
            MhnkArcContext context,
            IList<RoomLoopCandidate> candidates,
            string source,
            string elementKind)
        {
            var items = new List<MhnkRoomBoundaryLineReviewItem>();
            int index = 1;
            foreach (RoomLoopCandidate candidate in candidates ?? new List<RoomLoopCandidate>())
            {
                items.Add(new MhnkRoomBoundaryLineReviewItem
                {
                    IsSelected = true,
                    Index = index,
                    Room = GetRoomLabel(candidate.Room),
                    Level = candidate.Level?.Name ?? "",
                    Boundary = "Closed loops",
                    Kind = elementKind ?? "Element",
                    Length = (candidate.Loops?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                    Notes = GetRoomPreviewNotes(candidate.Room)
                });
                index++;
            }

            string lowerKind = (elementKind ?? "element").ToLowerInvariant();
            string summary =
                "Collected rooms: " + items.Count + Environment.NewLine +
                "Source: " + (source ?? "") + Environment.NewLine +
                "Select rooms that will receive " + lowerKind + " elements.";
            var window = new MhnkRoomBoundaryLineReviewWindow(
                "Review Rooms for " + (elementKind ?? "Elements"),
                summary,
                items,
                context.UiApplication.MainWindowHandle);
            if (window.ShowDialog() != true)
            {
                return new List<RoomLoopCandidate>();
            }

            HashSet<int> selectedIndices = items
                .Where(x => x.IsSelected)
                .Select(x => x.Index)
                .ToHashSet();
            return (candidates ?? new List<RoomLoopCandidate>())
                .Where((candidate, candidateIndex) => selectedIndices.Contains(candidateIndex + 1))
                .ToList();
        }

        private static IList<IList<BoundarySegment>> GetRoomBoundarySegmentGroups(Room room)
        {
            if (room == null)
            {
                return new List<IList<BoundarySegment>>();
            }

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

        private static IList<CurveLoop> GetRoomBoundaryCurveLoops(Room room, double elevation)
        {
            var loops = new List<CurveLoop>();
            foreach (IList<BoundarySegment> group in GetRoomBoundarySegmentGroups(room))
            {
                var curves = new List<Curve>();
                foreach (BoundarySegment segment in group ?? new List<BoundarySegment>())
                {
                    Curve curve = ProjectCurveToLevel(segment.GetCurve(), elevation);
                    if (curve != null && curve.Length >= ShortCurveTolerance)
                    {
                        curves.Add(curve);
                    }
                }

                if (curves.Count < 3)
                {
                    continue;
                }

                try
                {
                    loops.Add(CurveLoop.Create(curves));
                }
                catch
                {
                    IList<CurveLoop> rebuilt = BuildClosedLoopsFromCurves(curves);
                    foreach (CurveLoop rebuiltLoop in rebuilt)
                    {
                        loops.Add(rebuiltLoop);
                    }
                }
            }

            return loops;
        }

        private static IList<MhnkCadPreviewItem> BuildRoomWallPreviewItems(
            IList<Room> rooms,
            IList<RoomBoundaryCurveCandidate> candidates,
            MhnkRoomCreationOptions options)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (Room room in rooms ?? new List<Room>())
            {
                IList<RoomBoundaryCurveCandidate> roomCandidates = (candidates ?? new List<RoomBoundaryCurveCandidate>())
                    .Where(x => ElementIdEquals(x.Room.Id, room.Id))
                    .ToList();
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Source = GetRoomLabel(room),
                    Layer = "Room",
                    Rule = "Lines from Rooms",
                    Target = "Wall: " + (options?.TypeName ?? "") + (options != null && options.AutoInternalExternalWallTypes ? " +/-TYPE" : ""),
                    Quantity = roomCandidates.Count.ToString(CultureInfo.InvariantCulture),
                    Status = roomCandidates.Count > 0 ? "Ready" : "Skip",
                    Notes = GetRoomPreviewNotes(room) + "; lines " + roomCandidates.Count +
                            "; ext " + roomCandidates.Count(x => x.IsExteriorBoundary) +
                            "; int " + roomCandidates.Count(x => !x.IsExteriorBoundary)
                });
                index++;
            }

            return items;
        }

        private static IList<MhnkCadPreviewItem> BuildRoomLoopPreviewItems(
            IList<Room> rooms,
            IList<RoomLoopCandidate> candidates,
            MhnkRoomCreationOptions options,
            string elementKind)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (Room room in rooms ?? new List<Room>())
            {
                RoomLoopCandidate candidate = (candidates ?? new List<RoomLoopCandidate>())
                    .FirstOrDefault(x => ElementIdEquals(x.Room.Id, room.Id));
                int loopCount = candidate?.Loops?.Count ?? 0;
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Source = GetRoomLabel(room),
                    Layer = "Room",
                    Rule = "Boundary loops",
                    Target = elementKind + ": " + (options?.TypeName ?? ""),
                    Quantity = loopCount.ToString(CultureInfo.InvariantCulture),
                    Status = loopCount > 0 ? "Ready" : "Skip",
                    Notes = GetRoomPreviewNotes(room) + "; closed loops " + loopCount
                });
                index++;
            }

            return items;
        }

        private static IList<MhnkElementTypeOption> GetWallTypeOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(x => x.Kind.ToString())
                .ThenBy(x => x.Name)
                .Select(x => new MhnkElementTypeOption(x.Id, x.Kind + " Wall / " + x.Name))
                .ToList();
        }

        private static IList<Wall> GetHorizontalSplitCandidateWalls(MhnkArcContext context, out string source)
        {
            IList<Wall> selectedWalls = context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id) as Wall)
                .Where(IsUsableHorizontalSplitWall)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetWallLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            if (selectedWalls.Count > 0)
            {
                source = "Selected walls";
                return selectedWalls;
            }

            IList<Wall> visibleWalls = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(IsUsableHorizontalSplitWall)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetWallLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            source = visibleWalls.Count > 0 ? "Visible walls in active view" : "No split-capable walls";
            return visibleWalls;
        }

        private static bool IsUsableHorizontalSplitWall(Wall wall)
        {
            if (wall == null || !(wall.Location is LocationCurve locationCurve) || locationCurve.Curve == null)
            {
                return false;
            }

            WallType type = wall.Document.GetElement(wall.GetTypeId()) as WallType;
            return type != null && type.Kind != WallKind.Stacked;
        }

        private static IList<Wall> ReviewHorizontalSplitWalls(MhnkArcContext context, IList<Wall> walls, string source)
        {
            var items = new List<MhnkWallFinishReviewItem>();
            int index = 1;
            foreach (Wall wall in walls ?? new List<Wall>())
            {
                WallType wallType = context.Document.GetElement(wall.GetTypeId()) as WallType;
                LocationCurve location = wall.Location as LocationCurve;
                double baseOffset = GetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_BASE_OFFSET, 0.0);
                double height = GetHostWallInitialHeight(context.Document, wall, GetWallLevel(context.Document, wall), baseOffset, null);
                items.Add(new MhnkWallFinishReviewItem
                {
                    IsSelected = true,
                    WallId = wall.Id,
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Wall = GetElementLabel(wall),
                    Level = GetWallLevelName(context.Document, wall),
                    Type = wallType?.Name ?? wall.Name ?? "",
                    Function = GetWallFunctionLabel(wallType),
                    Length = location?.Curve == null ? "" : FormatLengthMeters(location.Curve.Length),
                    Notes = "Height " + FormatLengthMeters(height)
                });
                index++;
            }

            string summary =
                "Collected walls: " + items.Count + Environment.NewLine +
                "Source: " + (source ?? "") + Environment.NewLine +
                "Select the walls to split into lower and upper vertical segments.";
            var window = new MhnkWallFinishReviewWindow(
                "Review Walls to Split",
                summary,
                items,
                context.UiApplication.MainWindowHandle);
            if (window.ShowDialog() != true)
            {
                return new List<Wall>();
            }

            HashSet<string> selectedIds = new HashSet<string>(
                items.Where(x => x.IsSelected && x.WallId != null).Select(x => GetElementIdText(x.WallId)),
                StringComparer.OrdinalIgnoreCase);
            return (walls ?? new List<Wall>())
                .Where(x => x != null && selectedIds.Contains(GetElementIdText(x.Id)))
                .ToList();
        }

        private static MhnkWallHorizontalSplitOptions ShowHorizontalSplitOptions(
            MhnkArcContext context,
            IList<Wall> walls,
            string source,
            IList<MhnkElementTypeOption> typeOptions)
        {
            string summary =
                "Walls to split: " + (walls?.Count ?? 0) + Environment.NewLine +
                "Source: " + (source ?? "") + Environment.NewLine +
                "Levels: " + GetWallLevelsSummary(context.Document, walls ?? new List<Wall>());
            ElementId defaultTypeId = GetDefaultHorizontalSplitTypeId(context.Document, walls);
            var window = new MhnkWallHorizontalSplitOptionsWindow(
                summary,
                typeOptions ?? new List<MhnkElementTypeOption>(),
                defaultTypeId,
                context.UiApplication.MainWindowHandle);
            return window.ShowDialog() == true ? window.SelectedOptions : null;
        }

        private static ElementId GetDefaultHorizontalSplitTypeId(Document document, IList<Wall> walls)
        {
            WallType preferred = new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(x => (x.Name ?? "").IndexOf("curtain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     (x.Name ?? "").IndexOf("cortina", StringComparison.OrdinalIgnoreCase) >= 0);
            if (preferred != null)
            {
                return preferred.Id;
            }

            Wall firstWall = (walls ?? new List<Wall>()).FirstOrDefault();
            if (firstWall != null)
            {
                return firstWall.GetTypeId();
            }

            return new FilteredElementCollector(document)
                       .OfClass(typeof(WallType))
                       .Cast<WallType>()
                       .OrderBy(x => x.Name)
                       .Select(x => x.Id)
                       .FirstOrDefault() ?? ElementId.InvalidElementId;
        }

        private static IList<HorizontalWallSplitCandidate> BuildHorizontalSplitCandidates(
            Document document,
            IList<Wall> walls,
            MhnkWallHorizontalSplitOptions options)
        {
            var candidates = new List<HorizontalWallSplitCandidate>();
            double splitHeight = MetersToFeet(Math.Max(0.01, options?.SplitHeightMeters ?? 2.55));
            foreach (Wall wall in walls ?? new List<Wall>())
            {
                Level level = GetWallLevel(document, wall);
                if (level == null || !(wall.Location is LocationCurve locationCurve) || locationCurve.Curve == null)
                {
                    continue;
                }

                Curve projected = ProjectCurveToLevel(locationCurve.Curve, level.Elevation);
                if (projected == null || projected.Length < ShortCurveTolerance)
                {
                    continue;
                }

                double baseOffset = GetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_BASE_OFFSET, 0.0);
                double originalHeight = GetHostWallInitialHeight(document, wall, level, baseOffset, null);
                ElementId topLevelId = GetHostTopConstraintLevelId(wall);
                double topOffset = GetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_TOP_OFFSET, 0.0);
                candidates.Add(new HorizontalWallSplitCandidate(
                    wall,
                    level,
                    projected,
                    baseOffset,
                    originalHeight,
                    splitHeight,
                    topLevelId,
                    topOffset));
            }

            return candidates;
        }

        private static IList<MhnkCadPreviewItem> BuildHorizontalSplitPreviewItems(
            IList<HorizontalWallSplitCandidate> candidates,
            MhnkWallHorizontalSplitOptions options)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (HorizontalWallSplitCandidate candidate in candidates ?? new List<HorizontalWallSplitCandidate>())
            {
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Source = GetElementLabel(candidate.HostWall),
                    Layer = candidate.Level?.Name ?? "",
                    Rule = "Horizontal split",
                    Target = "New " + (options.ApplyNewTypeToLowerSegment ? "lower" : "upper") + ": " + (options.NewTypeName ?? ""),
                    Quantity = "2 segments",
                    Status = candidate.CanSplit ? "Ready" : "Skip",
                    Notes = candidate.CanSplit
                        ? "Split " + FormatLengthMeters(candidate.SplitHeight) + "; upper " + FormatLengthMeters(candidate.UpperHeight)
                        : "Wall height " + FormatLengthMeters(candidate.OriginalHeight) + " is too short"
                });
                index++;
            }

            return items;
        }

        private static MhnkWallCeilingTrimOptions ShowWallCeilingTrimOptions(
            MhnkArcContext context,
            IList<Wall> walls,
            string wallSource,
            IList<Ceiling> ceilings,
            string ceilingSource)
        {
            string summary =
                "Walls: " + (walls?.Count ?? 0) + Environment.NewLine +
                "Wall source: " + (wallSource ?? "") + Environment.NewLine +
                "Ceilings: " + (ceilings?.Count ?? 0) + Environment.NewLine +
                "Ceiling source: " + (ceilingSource ?? "") + Environment.NewLine +
                "Levels: " + GetWallLevelsSummary(context.Document, walls ?? new List<Wall>());

            var window = new MhnkWallCeilingTrimOptionsWindow(summary, context.UiApplication.MainWindowHandle);
            return window.ShowDialog() == true ? window.SelectedOptions : null;
        }

        private static IList<Ceiling> GetCeilingTrimCandidates(MhnkArcContext context, out string source)
        {
            IList<Ceiling> selectedCeilings = context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id) as Ceiling)
                .Where(x => x != null)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetElementLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            if (selectedCeilings.Count > 0)
            {
                source = "Selected ceilings";
                return selectedCeilings;
            }

            IList<Ceiling> visibleCeilings = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .Cast<Ceiling>()
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetElementLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            source = visibleCeilings.Count > 0 ? "Visible ceilings in active view" : "No visible ceilings";
            return visibleCeilings;
        }

        private static IList<WallCeilingTrimCandidate> BuildWallCeilingTrimCandidates(
            Document document,
            IList<Wall> walls,
            IList<Ceiling> ceilings,
            MhnkWallCeilingTrimOptions options)
        {
            var candidates = new List<WallCeilingTrimCandidate>();
            double topOffset = MillimetersToFeet(options?.TopOffsetMillimeters ?? 0.0);
            foreach (Wall wall in walls ?? new List<Wall>())
            {
                Level wallLevel = GetWallLevel(document, wall);
                if (wallLevel == null || !(wall?.Location is LocationCurve locationCurve) || locationCurve.Curve == null)
                {
                    continue;
                }

                double baseOffset = GetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_BASE_OFFSET, 0.0);
                double originalHeight = GetHostWallInitialHeight(document, wall, wallLevel, baseOffset, null);
                double wallBaseZ = wallLevel.Elevation + baseOffset;
                double wallTopZ = wallBaseZ + originalHeight;

                Ceiling bestCeiling = null;
                double bestTargetZ = double.NaN;
                foreach (Ceiling ceiling in ceilings ?? new List<Ceiling>())
                {
                    double ceilingZ = GetCeilingHeightZ(document, ceiling);
                    if (double.IsNaN(ceilingZ))
                    {
                        continue;
                    }

                    double targetZ = ceilingZ + topOffset;
                    if (targetZ <= wallBaseZ + ShortCurveTolerance)
                    {
                        continue;
                    }

                    if (options != null && options.OnlyLowerWalls && targetZ >= wallTopZ - ShortCurveTolerance)
                    {
                        continue;
                    }

                    if (!WallOverlapsCeilingXY(wall, ceiling))
                    {
                        continue;
                    }

                    if (bestCeiling == null || targetZ < bestTargetZ)
                    {
                        bestCeiling = ceiling;
                        bestTargetZ = targetZ;
                    }
                }

                double targetHeight = double.IsNaN(bestTargetZ) ? 0.0 : bestTargetZ - wallBaseZ;
                bool canApply = bestCeiling != null &&
                                targetHeight > ShortCurveTolerance &&
                                (options == null || !options.OnlyLowerWalls || targetHeight < originalHeight - ShortCurveTolerance);
                candidates.Add(new WallCeilingTrimCandidate(
                    wall,
                    wallLevel,
                    bestCeiling,
                    baseOffset,
                    originalHeight,
                    targetHeight,
                    canApply));
            }

            return candidates;
        }

        private static IList<MhnkCadPreviewItem> BuildWallCeilingTrimPreviewItems(
            IList<WallCeilingTrimCandidate> candidates,
            MhnkWallCeilingTrimOptions options)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (WallCeilingTrimCandidate candidate in candidates ?? new List<WallCeilingTrimCandidate>())
            {
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Source = GetElementLabel(candidate.Wall),
                    Layer = candidate.Level?.Name ?? "",
                    Rule = candidate.Ceiling == null ? "No ceiling match" : "Ceiling " + GetElementIdText(candidate.Ceiling.Id),
                    Target = candidate.CanApply ? "Wall top " + FormatLengthMeters(candidate.TargetHeight) : "Skip",
                    Quantity = "1",
                    Status = candidate.CanApply ? "Ready" : "Skip",
                    Notes = candidate.CanApply
                        ? "Original " + FormatLengthMeters(candidate.OriginalHeight) + "; offset " + (options?.TopOffsetMillimeters ?? 0.0).ToString("0.###", CultureInfo.InvariantCulture) + " mm"
                        : "No overlapping ceiling below current top"
                });
                index++;
            }

            return items;
        }

        private static double GetCeilingHeightZ(Document document, Ceiling ceiling)
        {
            if (ceiling == null)
            {
                return double.NaN;
            }

            Level level = GetElementLevel(document, ceiling);
            double offset = GetBuiltInDoubleParameter(ceiling, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM, double.NaN);
            if (level != null && !double.IsNaN(offset))
            {
                return level.Elevation + offset;
            }

            BoundingBoxXYZ box = ceiling.get_BoundingBox(null);
            if (box != null && box.Min != null)
            {
                return box.Min.Z;
            }

            return double.NaN;
        }

        private static bool WallOverlapsCeilingXY(Wall wall, Ceiling ceiling)
        {
            BoundingBoxXYZ wallBox = wall?.get_BoundingBox(null);
            BoundingBoxXYZ ceilingBox = ceiling?.get_BoundingBox(null);
            if (ceilingBox == null || ceilingBox.Min == null || ceilingBox.Max == null)
            {
                return false;
            }

            double pad = Math.Max(0.50, (wall?.Width ?? 0.0) + 0.10);
            if (wallBox != null && wallBox.Min != null && wallBox.Max != null &&
                BoxesOverlapXY(wallBox, ceilingBox, pad))
            {
                return true;
            }

            LocationCurve location = wall?.Location as LocationCurve;
            Curve curve = location?.Curve;
            if (curve == null)
            {
                return false;
            }

            return IsPointInsideBoxXY(curve.GetEndPoint(0), ceilingBox, pad) ||
                   IsPointInsideBoxXY(curve.GetEndPoint(1), ceilingBox, pad) ||
                   IsPointInsideBoxXY(curve.Evaluate(0.5, true), ceilingBox, pad);
        }

        private static bool BoxesOverlapXY(BoundingBoxXYZ first, BoundingBoxXYZ second, double padding)
        {
            return first.Min.X <= second.Max.X + padding &&
                   first.Max.X >= second.Min.X - padding &&
                   first.Min.Y <= second.Max.Y + padding &&
                   first.Max.Y >= second.Min.Y - padding;
        }

        private static bool IsPointInsideBoxXY(XYZ point, BoundingBoxXYZ box, double padding)
        {
            return point != null &&
                   box != null &&
                   point.X >= box.Min.X - padding &&
                   point.X <= box.Max.X + padding &&
                   point.Y >= box.Min.Y - padding &&
                   point.Y <= box.Max.Y + padding;
        }

        private static Wall SplitSingleWallHorizontally(
            Document document,
            HorizontalWallSplitCandidate candidate,
            WallType newType,
            MhnkWallHorizontalSplitOptions options)
        {
            if (document == null || candidate == null || candidate.HostWall == null || newType == null || options == null)
            {
                return null;
            }

            ElementId originalTypeId = candidate.HostWall.GetTypeId();
            bool flip = IsWallFlipped(candidate.HostWall);
            bool structural = IsWallStructural(candidate.HostWall);
            Wall created;
            if (options.ApplyNewTypeToLowerSegment)
            {
                created = CreateWallSegment(
                    document,
                    candidate,
                    newType.Id,
                    candidate.BaseOffset,
                    candidate.SplitHeight,
                    flip,
                    structural);
                if (created == null)
                {
                    return null;
                }

                CopyWallSplitParameters(candidate.HostWall, created, options);
                SetWallSegmentHeight(candidate.HostWall, candidate.Level, candidate.BaseOffset + candidate.SplitHeight, candidate.UpperHeight, candidate.TopConstraintLevelId, candidate.TopOffset, options.KeepOriginalTopConstraint);
                SetStringParameterIfWritable(created, "Comments", "MHNK lower wall split from " + GetElementLabel(candidate.HostWall));
                SetStringParameterIfWritable(candidate.HostWall, "Comments", "MHNK upper wall split; lower segment " + GetElementLabel(created));
                return created;
            }

            created = CreateWallSegment(
                document,
                candidate,
                newType.Id,
                candidate.BaseOffset + candidate.SplitHeight,
                candidate.UpperHeight,
                flip,
                structural);
            if (created == null)
            {
                return null;
            }

            CopyWallSplitParameters(candidate.HostWall, created, options);
            SetWallSegmentHeight(created, candidate.Level, candidate.BaseOffset + candidate.SplitHeight, candidate.UpperHeight, candidate.TopConstraintLevelId, candidate.TopOffset, options.KeepOriginalTopConstraint);
            SetWallSegmentHeight(candidate.HostWall, candidate.Level, candidate.BaseOffset, candidate.SplitHeight, ElementId.InvalidElementId, 0.0, false);
            SetStringParameterIfWritable(candidate.HostWall, "Comments", "MHNK lower wall split; upper segment " + GetElementLabel(created));
            SetStringParameterIfWritable(created, "Comments", "MHNK upper wall split from " + GetElementLabel(candidate.HostWall));
            return created;
        }

        private static Wall CreateWallSegment(
            Document document,
            HorizontalWallSplitCandidate candidate,
            ElementId typeId,
            double baseOffset,
            double height,
            bool flip,
            bool structural)
        {
            try
            {
                Wall wall = Wall.Create(
                    document,
                    candidate.Curve,
                    typeId,
                    candidate.Level.Id,
                    Math.Max(ShortCurveTolerance, height),
                    baseOffset,
                    flip,
                    structural);
                return wall;
            }
            catch
            {
                return null;
            }
        }

        private static void SetWallSegmentHeight(
            Wall wall,
            Level baseLevel,
            double baseOffset,
            double unconnectedHeight,
            ElementId topConstraintLevelId,
            double topOffset,
            bool keepTopConstraint)
        {
            if (wall == null)
            {
                return;
            }

            SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_BASE_OFFSET, baseOffset);
            bool topApplied = false;
            if (keepTopConstraint &&
                baseLevel != null &&
                topConstraintLevelId != null &&
                topConstraintLevelId != ElementId.InvalidElementId)
            {
                Level topLevel = wall.Document.GetElement(topConstraintLevelId) as Level;
                if (topLevel != null && topLevel.Elevation + topOffset > baseLevel.Elevation + baseOffset + ShortCurveTolerance)
                {
                    topApplied = SetBuiltInElementIdParameter(wall, BuiltInParameter.WALL_HEIGHT_TYPE, topConstraintLevelId);
                    if (topApplied)
                    {
                        SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_TOP_OFFSET, topOffset);
                    }
                }
            }

            if (!topApplied)
            {
                SetBuiltInElementIdParameter(wall, BuiltInParameter.WALL_HEIGHT_TYPE, ElementId.InvalidElementId);
                SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, Math.Max(ShortCurveTolerance, unconnectedHeight));
            }
        }

        private static void CopyWallSplitParameters(Wall source, Wall target, MhnkWallHorizontalSplitOptions options)
        {
            if (source == null || target == null)
            {
                return;
            }

            CopyBuiltInIntegerParameter(source, target, BuiltInParameter.WALL_KEY_REF_PARAM);
            if (options != null && options.CopyRoomBounding)
            {
                Parameter sourceRoomBounding = GetRoomBoundingParameter(source);
                Parameter targetRoomBounding = GetRoomBoundingParameter(target);
                if (sourceRoomBounding != null && targetRoomBounding != null &&
                    !targetRoomBounding.IsReadOnly &&
                    sourceRoomBounding.StorageType == StorageType.Integer &&
                    targetRoomBounding.StorageType == StorageType.Integer)
                {
                    try { targetRoomBounding.Set(sourceRoomBounding.AsInteger()); }
                    catch { }
                }
            }
        }

        private static bool IsWallFlipped(Wall wall)
        {
            try
            {
                return wall != null && wall.Flipped;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWallStructural(Wall wall)
        {
            Parameter parameter = wall?.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            if (parameter == null || parameter.StorageType != StorageType.Integer)
            {
                return false;
            }

            try
            {
                return parameter.AsInteger() != 0;
            }
            catch
            {
                return false;
            }
        }

        private static IList<MhnkElementTypeOption> GetBasicWallFinishTypeOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(x => x.Kind == WallKind.Basic)
                .OrderBy(x => GetWallTypeWidthMillimeters(x))
                .ThenBy(x => x.Name)
                .Select(x => new MhnkElementTypeOption(x.Id, "Basic Wall / " + x.Name + " (" + GetWallTypeWidthMillimeters(x).ToString("0.#") + " mm)"))
                .ToList();
        }

        private static IList<MhnkMaterialOption> GetMaterialOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .OrderBy(x => x.Name)
                .Select(x => new MhnkMaterialOption(x.Id, x.Name))
                .ToList();
        }

        private static WallType GetPreferredWallFinishType(Document document, bool exteriorSide)
        {
            IList<WallType> basicTypes = new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(x => x.Kind == WallKind.Basic)
                .ToList();

            return basicTypes
                       .OrderBy(x => GetWallFinishTypePreferenceScore(x, exteriorSide))
                       .ThenBy(x => GetWallTypeWidthMillimeters(x))
                       .ThenBy(x => x.Name)
                       .FirstOrDefault(x => GetWallFinishTypePreferenceScore(x, exteriorSide) < 100) ??
                   basicTypes
                       .Where(x => GetWallTypeWidthMillimeters(x) > 0.0 && GetWallTypeWidthMillimeters(x) <= 80.0)
                       .OrderBy(x => GetWallTypeWidthMillimeters(x))
                       .FirstOrDefault() ??
                   basicTypes.OrderBy(x => x.Name).FirstOrDefault();
        }

        private static int GetWallFinishTypePreferenceScore(WallType wallType, bool exteriorSide)
        {
            string name = (wallType?.Name ?? "").ToLowerInvariant();
            double widthMm = GetWallTypeWidthMillimeters(wallType);
            bool thin = widthMm > 0.0 && widthMm <= 80.0;
            bool finish = name.Contains("fin") ||
                          name.Contains("finish") ||
                          name.Contains("revest") ||
                          name.Contains("paint") ||
                          name.Contains("plaster") ||
                          name.Contains("render") ||
                          name.Contains("coat") ||
                          name.Contains("ceramic") ||
                          name.Contains("tile") ||
                          name.Contains("gypsum");

            if (exteriorSide)
            {
                if (name.Contains("fin-ext")) return 0;
                if (finish && (name.Contains("ext") || name.Contains("exterior") || name.Contains("external"))) return 1;
                if (thin && (name.Contains("ext") || name.Contains("exterior") || name.Contains("external"))) return 2;
                if (finish && thin) return 5;
                return thin ? 10 : 100;
            }

            if (name.Contains("fin-int")) return 0;
            if (finish && (name.Contains("int") || name.Contains("interior") || name.Contains("internal"))) return 1;
            if (thin && (name.Contains("int") || name.Contains("interior") || name.Contains("internal"))) return 2;
            if (finish && thin) return 5;
            return thin ? 10 : 100;
        }

        private static IList<MhnkLevelOption> GetLevelOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ThenBy(x => x.Name)
                .Select(x => new MhnkLevelOption(x.Id, x.Name))
                .ToList();
        }

        private static ElementId GetDefaultRoomWallLevelId(MhnkArcContext context)
        {
            Level activeLevel = GetActiveOrFirstLevel(context.Document, context.ActiveView);
            return activeLevel?.Id ?? ElementId.InvalidElementId;
        }

        private static MhnkWallFinishOptions ShowExternalWallFinishOptions(
            MhnkArcContext context,
            IList<Wall> hostWalls,
            string source,
            IList<MhnkElementTypeOption> typeOptions,
            ElementId defaultTypeId,
            bool exteriorSide)
        {
            string sideLower = exteriorSide ? "exterior" : "interior";
            string summary =
                "Host walls: " + (hostWalls?.Count ?? 0) + Environment.NewLine +
                "Source: " + (source ?? "") + Environment.NewLine +
                "Levels: " + GetWallLevelsSummary(context.Document, hostWalls ?? new List<Wall>()) + Environment.NewLine +
                "Mode: " + sideLower + " finish layer from host wall " + sideLower + " side";

            var window = new MhnkWallFinishOptionsWindow(
                summary,
                typeOptions ?? new List<MhnkElementTypeOption>(),
                defaultTypeId,
                GetMaterialOptions(context.Document),
                context.UiApplication.MainWindowHandle,
                exteriorSide,
                GetLevelOptions(context.Document));

            return window.ShowDialog() == true ? window.SelectedOptions : null;
        }

        private static IList<Wall> GetExternalWallFinishHostWalls(MhnkArcContext context, out string source, bool exteriorSide)
        {
            IList<Wall> selectedWalls = context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id) as Wall)
                .Where(IsUsableWallFinishHost)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetWallLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            if (context.SourceMode == MhnkArcSourceMode.FreeSelect && selectedWalls.Count > 0)
            {
                source = "Selected host walls";
                return selectedWalls;
            }

            IList<Wall> visibleWalls = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(IsUsableWallFinishHost)
                .Where(x => IsWallHostForFinishSide(x, exteriorSide))
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetWallLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            string sideLower = exteriorSide ? "exterior" : "interior";
            source = visibleWalls.Count > 0 ? "Visible " + sideLower + " host walls in active view" : "No " + sideLower + " host walls";
            return visibleWalls;
        }

        private static IList<Wall> ReviewCollectedWallFinishHosts(
            MhnkArcContext context,
            IList<Wall> hostWalls,
            string source,
            bool exteriorSide)
        {
            string sideName = exteriorSide ? "External" : "Internal";
            var items = new List<MhnkWallFinishReviewItem>();
            int index = 1;
            foreach (Wall wall in hostWalls ?? new List<Wall>())
            {
                WallType wallType = context.Document.GetElement(wall.GetTypeId()) as WallType;
                LocationCurve location = wall.Location as LocationCurve;
                items.Add(new MhnkWallFinishReviewItem
                {
                    IsSelected = true,
                    WallId = wall.Id,
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Wall = GetElementLabel(wall),
                    Level = GetWallLevelName(context.Document, wall),
                    Type = wallType?.Name ?? wall.Name ?? "",
                    Function = GetWallFunctionLabel(wallType),
                    Length = location?.Curve == null ? "" : FormatLengthMeters(location.Curve.Length),
                    Notes = GetWallFinishCollectionNotes(wallType, exteriorSide)
                });
                index++;
            }

            string summary =
                "Collected walls: " + items.Count + Environment.NewLine +
                "Source: " + (source ?? "") + Environment.NewLine +
                "Select the wall paths that will receive " + sideName.ToLowerInvariant() + " finish walls.";
            var window = new MhnkWallFinishReviewWindow(
                "Review " + sideName + " Collected Walls",
                summary,
                items,
                context.UiApplication.MainWindowHandle);
            if (window.ShowDialog() != true)
            {
                return new List<Wall>();
            }

            HashSet<string> selectedIds = new HashSet<string>(
                items.Where(x => x.IsSelected && x.WallId != null).Select(x => GetElementIdText(x.WallId)),
                StringComparer.OrdinalIgnoreCase);
            return (hostWalls ?? new List<Wall>())
                .Where(x => x != null && selectedIds.Contains(GetElementIdText(x.Id)))
                .ToList();
        }

        private static string GetWallFunctionLabel(WallType wallType)
        {
            Parameter parameter = wallType?.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            if (parameter == null || parameter.StorageType != StorageType.Integer)
            {
                return "";
            }

            try
            {
                return ((WallFunction)parameter.AsInteger()).ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string GetWallFinishCollectionNotes(WallType wallType, bool exteriorSide)
        {
            string name = wallType?.Name ?? "";
            string targetSuffix = exteriorSide ? "-EXT+" : "-INT+";
            if (name.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return "MHNK onion-wall " + (exteriorSide ? "exterior" : "interior") + " host";
            }

            return "Basic host wall";
        }

        private static bool IsUsableWallFinishHost(Wall wall)
        {
            if (wall == null || wall.Location as LocationCurve == null)
            {
                return false;
            }

            WallType type = wall.Document.GetElement(wall.GetTypeId()) as WallType;
            return type != null && type.Kind == WallKind.Basic;
        }

        private static bool IsWallHostForFinishSide(Wall wall, bool exteriorSide)
        {
            WallType type = wall?.Document.GetElement(wall.GetTypeId()) as WallType;
            Parameter parameter = type?.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            if (parameter != null && parameter.StorageType == StorageType.Integer)
            {
                try
                {
                    return parameter.AsInteger() == (int)(exteriorSide ? WallFunction.Exterior : WallFunction.Interior);
                }
                catch
                {
                }
            }

            string typeName = type?.Name ?? "";
            return exteriorSide
                ? typeName.IndexOf("ext", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  typeName.IndexOf("exterior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  typeName.IndexOf("external", StringComparison.OrdinalIgnoreCase) >= 0
                : typeName.IndexOf("int", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  typeName.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  typeName.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IList<WallFinishCandidate> BuildExternalWallFinishCandidates(
            Document document,
            IList<Wall> hostWalls,
            double finishWidthFeet,
            MhnkWallFinishOptions options,
            bool exteriorSide)
        {
            var candidates = new List<WallFinishCandidate>();
            foreach (Wall hostWall in hostWalls ?? new List<Wall>())
            {
                Level level = GetWallLevel(document, hostWall);
                if (level == null)
                {
                    continue;
                }

                Curve curve = CreateExternalFinishCurve(hostWall, level, finishWidthFeet, options, exteriorSide);
                if (curve == null || curve.Length < ShortCurveTolerance)
                {
                    continue;
                }

                double baseOffset = options != null && !options.MatchHostConstraints
                    ? MillimetersToFeet(options.BaseOffsetMillimeters)
                    : GetBuiltInDoubleParameter(hostWall, BuiltInParameter.WALL_BASE_OFFSET, 0.0);
                double initialHeight = GetHostWallInitialHeight(document, hostWall, level, baseOffset, options);
                ElementId topLevelId = options != null && !options.MatchHostConstraints
                    ? GetWallFinishTopConstraintLevel(document, level, options)?.Id ?? ElementId.InvalidElementId
                    : GetHostTopConstraintLevelId(hostWall);
                double topOffset = options != null && !options.MatchHostConstraints
                    ? MillimetersToFeet(options.TopOffsetMillimeters)
                    : GetBuiltInDoubleParameter(hostWall, BuiltInParameter.WALL_TOP_OFFSET, 0.0);

                candidates.Add(new WallFinishCandidate(hostWall, level, curve, baseOffset, initialHeight, topLevelId, topOffset));
            }

            return candidates;
        }

        private static Curve CreateExternalFinishCurve(
            Wall hostWall,
            Level level,
            double finishWidthFeet,
            MhnkWallFinishOptions options,
            bool exteriorSide)
        {
            if (!(hostWall?.Location is LocationCurve locationCurve) || locationCurve.Curve == null || level == null)
            {
                return null;
            }

            Curve projected = ProjectCurveToLevel(locationCurve.Curve, level.Elevation);
            if (projected == null)
            {
                return null;
            }

            XYZ sideNormal = GetWallFinishSideNormal(hostWall, projected, exteriorSide);
            if (sideNormal == null)
            {
                return null;
            }

            double offset = GetHostFaceOffsetFeet(hostWall, exteriorSide) +
                            Math.Max(0.0, finishWidthFeet) * 0.5 +
                            MillimetersToFeet(options?.GapMillimeters ?? 0.0);

            if (Math.Abs(offset) < 1e-9)
            {
                return projected;
            }

            if (projected is Line)
            {
                return projected.CreateTransformed(Transform.CreateTranslation(sideNormal.Multiply(offset)));
            }

            try
            {
                Curve offsetCurve = projected.CreateOffset(offset, XYZ.BasisZ);
                if (offsetCurve != null)
                {
                    XYZ originalMid = projected.Evaluate(0.5, true);
                    XYZ offsetMid = offsetCurve.Evaluate(0.5, true);
                    if (offsetMid != null && originalMid != null && (offsetMid - originalMid).DotProduct(sideNormal) < 0.0)
                    {
                        offsetCurve = projected.CreateOffset(-offset, XYZ.BasisZ);
                    }

                    return ProjectCurveToLevel(offsetCurve, level.Elevation);
                }
            }
            catch
            {
            }

            return projected.CreateTransformed(Transform.CreateTranslation(sideNormal.Multiply(offset)));
        }

        private static XYZ GetWallFinishSideNormal(Wall wall, Curve projectedCurve, bool exteriorSide)
        {
            XYZ normal = null;
            try
            {
                normal = wall?.Orientation;
            }
            catch
            {
                normal = null;
            }

            if (normal == null || normal.GetLength() < 1e-9)
            {
                try
                {
                    XYZ start = projectedCurve.GetEndPoint(0);
                    XYZ end = projectedCurve.GetEndPoint(1);
                    XYZ direction = end - start;
                    normal = new XYZ(-direction.Y, direction.X, 0.0);
                }
                catch
                {
                    return null;
                }
            }

            normal = new XYZ(normal.X, normal.Y, 0.0);
            if (normal.GetLength() < 1e-9)
            {
                return null;
            }

            normal = normal.Normalize();
            return exteriorSide ? normal : normal.Multiply(-1.0);
        }

        private static double GetHostFaceOffsetFeet(Wall hostWall, bool exteriorSide)
        {
            WallType type = hostWall?.Document.GetElement(hostWall.GetTypeId()) as WallType;
            double fallback = Math.Max(0.0, hostWall?.Width ?? type?.Width ?? 0.0) * 0.5;
            CompoundStructure structure = type?.GetCompoundStructure();
            if (structure == null)
            {
                return fallback;
            }

            try
            {
                WallLocationLine current = GetWallLocationLine(hostWall);
                double faceOffset = structure.GetOffsetForLocationLine(exteriorSide ? WallLocationLine.FinishFaceExterior : WallLocationLine.FinishFaceInterior);
                double currentOffset = structure.GetOffsetForLocationLine(current);
                return Math.Abs(faceOffset - currentOffset);
            }
            catch
            {
                return fallback;
            }
        }

        private static WallLocationLine GetWallLocationLine(Wall wall)
        {
            Parameter parameter = wall?.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
            if (parameter != null && parameter.StorageType == StorageType.Integer)
            {
                try
                {
                    int value = parameter.AsInteger();
                    if (Enum.IsDefined(typeof(WallLocationLine), value))
                    {
                        return (WallLocationLine)value;
                    }
                }
                catch
                {
                }
            }

            return WallLocationLine.WallCenterline;
        }

        private static double GetHostWallInitialHeight(
            Document document,
            Wall hostWall,
            Level baseLevel,
            double baseOffset,
            MhnkWallFinishOptions options)
        {
            if (options != null && !options.MatchHostConstraints)
            {
                double defaultHeight = MetersToFeet(Math.Max(0.01, options.CreationHeightMeters));
                Level chosenTopLevel = GetWallFinishTopConstraintLevel(document, baseLevel, options);
                if (chosenTopLevel == null)
                {
                    return defaultHeight;
                }

                double constrainedHeight = chosenTopLevel.Elevation + MillimetersToFeet(options.TopOffsetMillimeters) -
                                           (baseLevel.Elevation + baseOffset);
                return constrainedHeight > ShortCurveTolerance ? constrainedHeight : defaultHeight;
            }

            double height = GetBuiltInDoubleParameter(hostWall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, 0.0);
            if (height > ShortCurveTolerance)
            {
                return height;
            }

            ElementId topLevelId = options != null && options.MatchHostConstraints ? GetHostTopConstraintLevelId(hostWall) : ElementId.InvalidElementId;
            Level topLevel = topLevelId == null || topLevelId == ElementId.InvalidElementId ? null : document.GetElement(topLevelId) as Level;
            if (topLevel != null && baseLevel != null)
            {
                double topOffset = GetBuiltInDoubleParameter(hostWall, BuiltInParameter.WALL_TOP_OFFSET, 0.0);
                double constrainedHeight = topLevel.Elevation + topOffset - (baseLevel.Elevation + baseOffset);
                if (constrainedHeight > ShortCurveTolerance)
                {
                    return constrainedHeight;
                }
            }

            BoundingBoxXYZ box = hostWall?.get_BoundingBox(null);
            if (box != null && box.Max != null && box.Min != null && box.Max.Z - box.Min.Z > ShortCurveTolerance)
            {
                return box.Max.Z - box.Min.Z;
            }

            return MetersToFeet(3.0);
        }

        private static Level GetWallFinishTopConstraintLevel(
            Document document,
            Level baseLevel,
            MhnkWallFinishOptions options)
        {
            if (document == null || baseLevel == null || options == null || options.MatchHostConstraints)
            {
                return null;
            }

            if (!options.UseLevelAboveTopConstraint &&
                options.TopConstraintLevelId != null &&
                options.TopConstraintLevelId != ElementId.InvalidElementId)
            {
                return document.GetElement(options.TopConstraintLevelId) as Level;
            }

            if (!options.UseLevelAboveTopConstraint)
            {
                return null;
            }

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(x => x.Elevation > baseLevel.Elevation + ShortCurveTolerance)
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static ElementId GetHostTopConstraintLevelId(Wall wall)
        {
            Parameter topConstraint = wall?.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            if (topConstraint != null && topConstraint.StorageType == StorageType.ElementId)
            {
                try
                {
                    ElementId id = topConstraint.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        return id;
                    }
                }
                catch
                {
                }
            }

            return ElementId.InvalidElementId;
        }

        private static IList<MhnkCadPreviewItem> BuildExternalWallFinishPreviewItems(
            IList<WallFinishCandidate> candidates,
            MhnkWallFinishOptions options,
            bool exteriorSide)
        {
            var items = new List<MhnkCadPreviewItem>();
            string sideLabel = exteriorSide ? "Exterior side" : "Interior side";
            int index = 1;
            foreach (WallFinishCandidate candidate in candidates ?? new List<WallFinishCandidate>())
            {
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Source = GetElementLabel(candidate.HostWall),
                    Layer = candidate.Level?.Name ?? "",
                    Rule = sideLabel,
                    Target = "Finish: " + (options?.TypeName ?? ""),
                    Quantity = "1",
                    Status = "Ready",
                    Notes = "Length " + FormatLengthMeters(candidate.Curve.Length)
                });
                index++;
            }

            return items;
        }

        private static WallType ResolveExternalFinishWallType(
            Document document,
            WallType selectedType,
            MhnkWallFinishOptions options,
            bool exteriorSide)
        {
            if (document == null || selectedType == null || options == null || !options.CreateDedicatedMaterialType ||
                options.MaterialId == null || options.MaterialId == ElementId.InvalidElementId)
            {
                return selectedType;
            }

            string materialName = SanitizeName(options.MaterialName);
            string typeName = "MHNK " + (exteriorSide ? "EXT" : "INT") + " FINISH - " + materialName + " - " + options.ThicknessMillimeters.ToString("0.#", CultureInfo.InvariantCulture) + "mm";
            WallType existing = new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(x => string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            WallType finishType = existing;
            if (finishType == null)
            {
                try
                {
                    finishType = selectedType.Duplicate(typeName) as WallType;
                }
                catch
                {
                    finishType = selectedType;
                }
            }

            if (finishType != null)
            {
                try
                {
                    CompoundStructure structure = CompoundStructure.CreateSingleLayerCompoundStructure(
                        MaterialFunctionAssignment.Finish1,
                        MillimetersToFeet(Math.Max(1.0, options.ThicknessMillimeters)),
                        options.MaterialId);
                    finishType.SetCompoundStructure(structure);
                }
                catch
                {
                }
            }

            return finishType ?? selectedType;
        }

        private static double GetExternalFinishWidthFeet(WallType finishType, MhnkWallFinishOptions options)
        {
            if (options != null && options.CreateDedicatedMaterialType && !options.UseSelectedTypeMaterial)
            {
                return MillimetersToFeet(Math.Max(1.0, options.ThicknessMillimeters));
            }

            double width = finishType?.Width ?? 0.0;
            return width > ShortCurveTolerance ? width : MillimetersToFeet(Math.Max(1.0, options?.ThicknessMillimeters ?? 10.0));
        }

        private static bool HasExistingFinishWall(Document document, WallFinishCandidate candidate, ElementId finishTypeId)
        {
            if (document == null || candidate == null || finishTypeId == null || finishTypeId == ElementId.InvalidElementId)
            {
                return false;
            }

            string candidateKey = GetCurveDedupeKey(candidate.Curve);
            foreach (Wall wall in new FilteredElementCollector(document)
                         .OfClass(typeof(Wall))
                         .Cast<Wall>()
                         .Where(x => ElementIdEquals(x.GetTypeId(), finishTypeId)))
            {
                if (!(wall.Location is LocationCurve locationCurve) || locationCurve.Curve == null)
                {
                    continue;
                }

                Curve existing = ProjectCurveToLevel(locationCurve.Curve, candidate.Level.Elevation);
                if (existing == null)
                {
                    continue;
                }

                if (string.Equals(GetCurveDedupeKey(existing), candidateKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyExternalFinishWallOptions(
            Document document,
            Wall finishWall,
            WallFinishCandidate candidate,
            MhnkWallFinishOptions options,
            bool exteriorSide)
        {
            if (finishWall == null || candidate == null || options == null)
            {
                return;
            }

            SetBuiltInDoubleParameter(finishWall, BuiltInParameter.WALL_BASE_OFFSET, candidate.BaseOffset);
            if (candidate.TopConstraintLevelId != null &&
                candidate.TopConstraintLevelId != ElementId.InvalidElementId)
            {
                Parameter topConstraint = finishWall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                if (topConstraint != null && !topConstraint.IsReadOnly && topConstraint.StorageType == StorageType.ElementId)
                {
                    try { topConstraint.Set(candidate.TopConstraintLevelId); }
                    catch { }
                }

                SetBuiltInDoubleParameter(finishWall, BuiltInParameter.WALL_TOP_OFFSET, candidate.TopOffset);
            }
            else
            {
                SetBuiltInDoubleParameter(finishWall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, candidate.InitialHeight);
            }

            Parameter roomBounding = GetRoomBoundingParameter(finishWall);
            if (roomBounding != null && !roomBounding.IsReadOnly)
            {
                try { roomBounding.Set(options.SetRoomBounding ? 1 : 0); }
                catch { }
            }

            SetStringParameterIfWritable(finishWall, "Comments", "MHNK " + (exteriorSide ? "External" : "Internal") + " Wall Finish from " + GetElementLabel(candidate.HostWall));

            if (!options.AllowWallJoinsAtEnds)
            {
                try { WallUtils.DisallowWallJoinAtEnd(finishWall, 0); }
                catch { }
                try { WallUtils.DisallowWallJoinAtEnd(finishWall, 1); }
                catch { }
            }
        }

        private static void SetStringParameterIfWritable(Element element, string parameterName, string value)
        {
            Parameter parameter = element?.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                return;
            }

            try
            {
                parameter.Set(value ?? "");
            }
            catch
            {
            }
        }

        private static double GetBuiltInDoubleParameter(Element element, BuiltInParameter builtInParameter, double fallback)
        {
            Parameter parameter = element?.get_Parameter(builtInParameter);
            if (parameter == null || parameter.StorageType != StorageType.Double)
            {
                return fallback;
            }

            try
            {
                return parameter.AsDouble();
            }
            catch
            {
                return fallback;
            }
        }

        private static double GetWallTypeWidthMillimeters(WallType wallType)
        {
            double width = wallType?.Width ?? 0.0;
            return width <= 0.0 ? 0.0 : width * 304.8;
        }

        private static Level GetWallLevel(Document document, Wall wall)
        {
            ElementId levelId = GetElementLevelId(wall);
            return levelId == ElementId.InvalidElementId ? null : document.GetElement(levelId) as Level;
        }

        private static Level GetElementLevel(Document document, Element element)
        {
            ElementId levelId = GetElementLevelId(element);
            return levelId == ElementId.InvalidElementId ? null : document.GetElement(levelId) as Level;
        }

        private static string GetElementLevelName(Document document, Element element)
        {
            return GetElementLevel(document, element)?.Name ?? "";
        }

        private static string GetWallLevelName(Document document, Wall wall)
        {
            return GetWallLevel(document, wall)?.Name ?? "";
        }

        private static string GetWallLevelsSummary(Document document, IList<Wall> walls)
        {
            IList<string> names = (walls ?? new List<Wall>())
                .Select(x => GetWallLevelName(document, x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            if (names.Count == 0)
            {
                return "(no level)";
            }

            string summary = string.Join(", ", names.Take(4));
            if (names.Count > 4)
            {
                summary += " +" + (names.Count - 4).ToString(CultureInfo.InvariantCulture);
            }

            return summary;
        }

        private static IList<MhnkElementTypeOption> GetFloorTypeOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(x => x.Name)
                .Select(x => new MhnkElementTypeOption(x.Id, x.Name))
                .ToList();
        }

        private static IList<MhnkElementTypeOption> GetCeilingTypeOptions(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .OrderBy(x => x.Name)
                .Select(x => new MhnkElementTypeOption(x.Id, x.Name))
                .ToList();
        }

        private static Level GetRoomLevel(Document document, Room room)
        {
            ElementId levelId = GetElementLevelId(room);
            return levelId == ElementId.InvalidElementId ? null : document.GetElement(levelId) as Level;
        }

        private static string GetRoomLevelsSummary(Document document, IList<Room> rooms)
        {
            IList<string> names = (rooms ?? new List<Room>())
                .Select(x => GetRoomLevelName(document, x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            if (names.Count == 0)
            {
                return "(no level)";
            }

            string summary = string.Join(", ", names.Take(4));
            if (names.Count > 4)
            {
                summary += " +" + (names.Count - 4).ToString(CultureInfo.InvariantCulture);
            }

            return summary;
        }

        private static string GetSelectedLevelSummary(Document document, MhnkRoomCreationOptions options)
        {
            if (options == null || options.UseAllLevels || options.SelectedLevelId == null || options.SelectedLevelId == ElementId.InvalidElementId)
            {
                return "All Levels";
            }

            return (document.GetElement(options.SelectedLevelId) as Level)?.Name ?? "(selected level)";
        }

        private static string GetWallHeightSummary(Document document, MhnkRoomCreationOptions options)
        {
            if (options == null || !options.UseTopConstraint)
            {
                return "Wall height: " + (options?.WallHeightMeters ?? 0.0).ToString("0.###") + " m (Unconnected)";
            }

            string target = options.UseLevelAboveTopConstraint
                ? "Level Above"
                : (document.GetElement(options.TopConstraintLevelId) as Level)?.Name ?? "(top level)";
            return "Top constraint: " + target + "; top offset: " + options.TopOffsetMillimeters.ToString("0.###") + " mm";
        }

        private static string GetWallFinishHeightSummary(Document document, MhnkWallFinishOptions options)
        {
            if (options == null || options.MatchHostConstraints)
            {
                return "Match host wall base/top constraints";
            }

            if (!options.UseLevelAboveTopConstraint &&
                (options.TopConstraintLevelId == null || options.TopConstraintLevelId == ElementId.InvalidElementId))
            {
                return options.CreationHeightMeters.ToString("0.###", CultureInfo.InvariantCulture) +
                       " m (Unconnected); base offset " +
                       options.BaseOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm";
            }

            string target = options.UseLevelAboveTopConstraint
                ? "Level Above"
                : (document.GetElement(options.TopConstraintLevelId) as Level)?.Name ?? "(top level)";
            return "Top constraint " + target +
                   "; base offset " + options.BaseOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture) +
                   " mm; top offset " + options.TopOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture) + " mm";
        }

        private static string GetWallJoinSummary(MhnkRoomCreationOptions options)
        {
            if (options == null || !options.AllowWallJoinsAtEnds)
            {
                return "Joins not allowed";
            }

            return options.UseMiterWallJoins ? "Miter / Chamfer" : "Butt / Abut";
        }

        private static string GetRoomLevelName(Document document, Room room)
        {
            return GetRoomLevel(document, room)?.Name ?? "";
        }

        private static string GetRoomLabel(Room room)
        {
            if (room == null)
            {
                return "(room)";
            }

            string number = string.IsNullOrWhiteSpace(room.Number) ? "" : room.Number.Trim();
            string name = string.IsNullOrWhiteSpace(room.Name) ? "" : room.Name.Trim();
            return string.IsNullOrWhiteSpace(number)
                ? name
                : number + " " + name;
        }

        private static string GetRoomPreviewNotes(Room room)
        {
            return "Area " + FormatAreaSquareMeters(SafeRoomArea(room));
        }

        private static string FormatAreaSquareMeters(double squareFeet)
        {
            return (squareFeet * 0.09290304).ToString("0.###") + " m2";
        }

        private static string GetCurveDedupeKey(Curve curve)
        {
            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ mid = curve.Evaluate(0.5, true);
            string first = GetRoundedPointKey(start);
            string second = GetRoundedPointKey(end);
            string pair = string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0
                ? first + "|" + second
                : second + "|" + first;
            return curve.GetType().Name + "|" + pair + "|" + GetRoundedPointKey(mid);
        }

        private static string GetRoundedPointKey(XYZ point)
        {
            if (point == null)
            {
                return "";
            }

            return Math.Round(point.X, 4).ToString(CultureInfo.InvariantCulture) + "," +
                   Math.Round(point.Y, 4).ToString(CultureInfo.InvariantCulture) + "," +
                   Math.Round(point.Z, 4).ToString(CultureInfo.InvariantCulture);
        }

        private static double GetInitialWallCreationHeight(
            Document document,
            RoomBoundaryCurveCandidate candidate,
            MhnkRoomCreationOptions options)
        {
            double defaultHeight = MetersToFeet(Math.Max(0.01, options?.WallHeightMeters ?? 3.0));
            if (document == null || candidate?.Level == null || options == null || !options.UseTopConstraint)
            {
                return defaultHeight;
            }

            Level topLevel = GetWallTopConstraintLevel(document, candidate.Level, options);
            if (topLevel == null)
            {
                return defaultHeight;
            }

            double constrainedHeight = topLevel.Elevation + MillimetersToFeet(options.TopOffsetMillimeters) -
                                       (candidate.Level.Elevation + MillimetersToFeet(options.BaseOffsetMillimeters));
            return constrainedHeight > ShortCurveTolerance ? constrainedHeight : defaultHeight;
        }

        private static Level GetWallTopConstraintLevel(
            Document document,
            Level baseLevel,
            MhnkRoomCreationOptions options)
        {
            if (document == null || baseLevel == null || options == null || !options.UseTopConstraint)
            {
                return null;
            }

            if (!options.UseLevelAboveTopConstraint &&
                options.TopConstraintLevelId != null &&
                options.TopConstraintLevelId != ElementId.InvalidElementId)
            {
                return document.GetElement(options.TopConstraintLevelId) as Level;
            }

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(x => x.Elevation > baseLevel.Elevation + ShortCurveTolerance)
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static WallType GetOrCreateWallFunctionType(
            Document document,
            WallType sourceType,
            WallFunction function,
            string suffix)
        {
            if (document == null || sourceType == null)
            {
                return sourceType;
            }

            string targetName = RemoveAutomaticWallFunctionSuffix(sourceType.Name) + suffix;
            WallType wallType = new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(x => string.Equals(x.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (wallType == null)
            {
                try
                {
                    wallType = sourceType.Duplicate(targetName) as WallType;
                }
                catch
                {
                    wallType = sourceType;
                }
            }

            SetWallTypeFunction(wallType, function);
            return wallType ?? sourceType;
        }

        private static string RemoveAutomaticWallFunctionSuffix(string name)
        {
            string result = string.IsNullOrWhiteSpace(name) ? "Wall" : name.Trim();
            string[] suffixes = { "-EXT+", "-INT+" };
            bool changed;
            do
            {
                changed = false;
                foreach (string suffix in suffixes)
                {
                    if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result = result.Substring(0, result.Length - suffix.Length).Trim();
                        changed = true;
                    }
                }
            }
            while (changed);

            return string.IsNullOrWhiteSpace(result) ? "Wall" : result;
        }

        private static void SetWallTypeFunction(WallType wallType, WallFunction function)
        {
            Parameter parameter = wallType?.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
            {
                return;
            }

            try
            {
                parameter.Set((int)function);
            }
            catch
            {
            }
        }

        private static void SetGeneratedWallOptions(
            Document document,
            Wall wall,
            MhnkRoomCreationOptions options,
            RoomBoundaryCurveCandidate candidate)
        {
            if (wall == null || options == null)
            {
                return;
            }

            SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_BASE_OFFSET, MillimetersToFeet(options.BaseOffsetMillimeters));
            if (options.UseTopConstraint)
            {
                Level topLevel = GetWallTopConstraintLevel(document, candidate?.Level, options);
                bool validTopConstraint = topLevel != null &&
                                          candidate?.Level != null &&
                                          topLevel.Elevation + MillimetersToFeet(options.TopOffsetMillimeters) >
                                          candidate.Level.Elevation + MillimetersToFeet(options.BaseOffsetMillimeters) + ShortCurveTolerance;
                Parameter topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                if (validTopConstraint && topConstraint != null && !topConstraint.IsReadOnly && topConstraint.StorageType == StorageType.ElementId)
                {
                    try { topConstraint.Set(topLevel.Id); }
                    catch { }
                    SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_TOP_OFFSET, MillimetersToFeet(options.TopOffsetMillimeters));
                }
                else
                {
                    SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, MetersToFeet(options.WallHeightMeters));
                }
            }
            else
            {
                SetBuiltInDoubleParameter(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, MetersToFeet(options.WallHeightMeters));
            }

            if (options.SetRoomBounding)
            {
                Parameter roomBounding = GetRoomBoundingParameter(wall);
                if (roomBounding != null && !roomBounding.IsReadOnly)
                {
                    try { roomBounding.Set(1); }
                    catch { }
                }
            }

            if (!options.AllowWallJoinsAtEnds)
            {
                try { WallUtils.DisallowWallJoinAtEnd(wall, 0); }
                catch { }
                try { WallUtils.DisallowWallJoinAtEnd(wall, 1); }
                catch { }
                return;
            }

            try { WallUtils.AllowWallJoinAtEnd(wall, 0); }
            catch { }
            try { WallUtils.AllowWallJoinAtEnd(wall, 1); }
            catch { }

            LocationCurve locationCurve = wall.Location as LocationCurve;
            if (locationCurve == null)
            {
                return;
            }

            JoinType targetJoinType = options.UseMiterWallJoins ? JoinType.Miter : JoinType.Abut;
            try { locationCurve.set_JoinType(0, targetJoinType); }
            catch { }
            try { locationCurve.set_JoinType(1, targetJoinType); }
            catch { }
        }

        private static int JoinGeneratedTouchingWalls(Document document, IList<Wall> walls)
        {
            int joined = 0;
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = i + 1; j < walls.Count; j++)
                {
                    Wall first = walls[i];
                    Wall second = walls[j];
                    if (first == null || second == null)
                    {
                        continue;
                    }

                    BoundingBoxXYZ firstBox = ExpandBoundingBox(first.get_BoundingBox(null), 0.05);
                    BoundingBoxXYZ secondBox = ExpandBoundingBox(second.get_BoundingBox(null), 0.05);
                    if (firstBox == null || secondBox == null || !BoundingBoxesIntersect(firstBox, secondBox))
                    {
                        continue;
                    }

                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(document, first, second))
                        {
                            JoinGeometryUtils.JoinGeometry(document, first, second);
                            joined++;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return joined;
        }

        private static void SetBuiltInDoubleParameter(Element element, BuiltInParameter builtInParameter, double value)
        {
            if (element == null)
            {
                return;
            }

            Parameter parameter = element.get_Parameter(builtInParameter);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            {
                return;
            }

            try
            {
                parameter.Set(value);
            }
            catch
            {
            }
        }

        private static bool SetBuiltInElementIdParameter(Element element, BuiltInParameter builtInParameter, ElementId value)
        {
            if (element == null)
            {
                return false;
            }

            Parameter parameter = element.get_Parameter(builtInParameter);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.ElementId)
            {
                return false;
            }

            try
            {
                parameter.Set(value ?? ElementId.InvalidElementId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CopyBuiltInIntegerParameter(Element source, Element target, BuiltInParameter builtInParameter)
        {
            Parameter sourceParameter = source?.get_Parameter(builtInParameter);
            Parameter targetParameter = target?.get_Parameter(builtInParameter);
            if (sourceParameter == null ||
                targetParameter == null ||
                targetParameter.IsReadOnly ||
                sourceParameter.StorageType != StorageType.Integer ||
                targetParameter.StorageType != StorageType.Integer)
            {
                return;
            }

            try
            {
                targetParameter.Set(sourceParameter.AsInteger());
            }
            catch
            {
            }
        }

        private static string FormatLengthMeters(double feet)
        {
            return (feet / 3.280839895013123).ToString("0.###") + " m";
        }

        private static FamilySymbol GetDoorWindowTargetSymbol(
            Document document,
            CadCurveCandidate candidate,
            MhnkArcToolSettings settings,
            FamilySymbol doorSymbol,
            FamilySymbol windowSymbol)
        {
            if (IsMappingAction(candidate?.MappingRule, "Window"))
            {
                FamilySymbol mappedWindow = GetPreferredFamilySymbol(document, BuiltInCategory.OST_Windows, GetRuleTypeKeywords(candidate.MappingRule, settings.SplitKeywords(settings.WindowTypeKeywords)));
                return mappedWindow ?? windowSymbol ?? doorSymbol;
            }

            if (IsMappingAction(candidate?.MappingRule, "Door"))
            {
                FamilySymbol mappedDoor = GetPreferredFamilySymbol(document, BuiltInCategory.OST_Doors, GetRuleTypeKeywords(candidate.MappingRule, settings.SplitKeywords(settings.DoorTypeKeywords)));
                return mappedDoor ?? doorSymbol ?? windowSymbol;
            }

            return IsCadLayerMatch(candidate.LayerName, settings.SplitKeywords(settings.WindowLayerKeywords))
                ? windowSymbol ?? doorSymbol
                : doorSymbol ?? windowSymbol;
        }

        private static string GetDoorWindowTargetName(
            Document document,
            CadCurveCandidate candidate,
            MhnkArcToolSettings settings,
            FamilySymbol doorSymbol,
            FamilySymbol windowSymbol)
        {
            FamilySymbol symbol = GetDoorWindowTargetSymbol(document, candidate, settings, doorSymbol, windowSymbol);
            if (symbol == null)
            {
                return "Missing family symbol";
            }

            string kind = IsDoorWindowCandidateWindow(candidate, settings) ? "Window" : "Door";
            return kind + ": " + symbol.Name;
        }

        private static bool IsDoorWindowCandidateWindow(CadCurveCandidate candidate, MhnkArcToolSettings settings)
        {
            if (IsMappingAction(candidate?.MappingRule, "Window"))
            {
                return true;
            }

            if (IsMappingAction(candidate?.MappingRule, "Door"))
            {
                return false;
            }

            return IsCadLayerMatch(candidate?.LayerName, settings.SplitKeywords(settings.WindowLayerKeywords));
        }

        private static MhnkDoorWindowPlacementOptions ShowDoorWindowPlacementOptions(
            MhnkArcContext context,
            bool wallSideMode,
            IList<CadCurveCandidate> candidates,
            IList<Wall> hostWalls,
            string hostSource,
            FamilySymbol doorSymbol,
            FamilySymbol windowSymbol)
        {
            string summary =
                "Markers: " + (candidates?.Count ?? 0) + Environment.NewLine +
                "Host walls: " + (hostWalls?.Count ?? 0) + Environment.NewLine +
                "Host source: " + (hostSource ?? "") + Environment.NewLine +
                "Door symbol: " + (doorSymbol?.Name ?? "Missing") + Environment.NewLine +
                "Window symbol: " + (windowSymbol?.Name ?? "Missing");
            var window = new MhnkDoorWindowPlacementOptionsWindow(summary, wallSideMode, context.UiApplication.MainWindowHandle);
            return window.ShowDialog() == true ? window.SelectedOptions : null;
        }

        private static IList<Wall> GetDoorWindowHostWalls(MhnkArcContext context, out string source)
        {
            IList<Wall> selectedWalls = context.UiDocument.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id) as Wall)
                .Where(IsUsableHorizontalSplitWall)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetWallLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();
            if (selectedWalls.Count > 0)
            {
                source = "Selected host walls";
                return selectedWalls;
            }

            IList<Wall> visibleWalls = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(IsUsableHorizontalSplitWall)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => GetWallLevelName(context.Document, x))
                .ThenBy(x => x.Name)
                .ToList();

            source = visibleWalls.Count > 0 ? "Visible walls in active view" : "No visible host walls";
            return visibleWalls;
        }

        private static IList<HostedDoorWindowPlacementCandidate> BuildHostedDoorWindowPlacementCandidates(
            MhnkArcContext context,
            IList<CadCurveCandidate> candidates,
            IList<Wall> hostWalls,
            MhnkArcToolSettings settings,
            FamilySymbol doorSymbol,
            FamilySymbol windowSymbol,
            MhnkDoorWindowPlacementOptions options)
        {
            var placements = new List<HostedDoorWindowPlacementCandidate>();
            double maxDistance = MillimetersToFeet(Math.Max(1.0, options?.MaxHostDistanceMillimeters ?? 1500.0));
            foreach (CadCurveCandidate candidate in candidates ?? new List<CadCurveCandidate>())
            {
                FamilySymbol symbol = GetDoorWindowTargetSymbol(context.Document, candidate, settings, doorSymbol, windowSymbol);
                bool isWindow = IsDoorWindowCandidateWindow(candidate, settings);
                XYZ markerPoint = candidate.Curve.Evaluate(0.5, true);
                WallProjection projection = FindNearestWallProjection(hostWalls, markerPoint, maxDistance);
                Level level = projection?.Wall == null ? null : GetWallLevel(context.Document, projection.Wall);
                XYZ hostPoint = projection == null || level == null
                    ? null
                    : new XYZ(projection.Point.X, projection.Point.Y, level.Elevation);

                double sillHeight = MillimetersToFeet(Math.Max(0.0, options?.WindowSillHeightMillimeters ?? 900.0));
                if (isWindow && options != null && options.UseCadHeightForWindowSill && level != null && markerPoint.Z > level.Elevation + ShortCurveTolerance)
                {
                    sillHeight = markerPoint.Z - level.Elevation;
                }

                placements.Add(new HostedDoorWindowPlacementCandidate(
                    candidate,
                    symbol,
                    isWindow,
                    projection?.Wall,
                    level,
                    markerPoint,
                    hostPoint,
                    projection == null ? double.NaN : projection.Distance,
                    sillHeight));
            }

            return placements;
        }

        private static IList<MhnkCadPreviewItem> BuildHostedDoorWindowPreviewItems(
            IList<HostedDoorWindowPlacementCandidate> placements,
            bool wallSideMode)
        {
            var items = new List<MhnkCadPreviewItem>();
            int index = 1;
            foreach (HostedDoorWindowPlacementCandidate placement in placements ?? new List<HostedDoorWindowPlacementCandidate>())
            {
                items.Add(new MhnkCadPreviewItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Source = placement.SourceLabel,
                    Layer = placement.SourceLayer,
                    Rule = wallSideMode ? "Wall side" : "Position",
                    Target = placement.Symbol == null
                        ? "Missing type"
                        : (placement.IsWindow ? "Window: " : "Door: ") + placement.Symbol.Name,
                    Quantity = "1",
                    Status = placement.CanPlace ? "Ready" : "Skip",
                    Notes = placement.CanPlace
                        ? "Host " + GetElementLabel(placement.HostWall) + "; distance " + FormatLengthMeters(placement.HostDistance)
                        : placement.SkipReason
                });
                index++;
            }

            return items;
        }

        private static WallProjection FindNearestWallProjection(IList<Wall> hostWalls, XYZ markerPoint, double maxDistance)
        {
            WallProjection best = null;
            foreach (Wall wall in hostWalls ?? new List<Wall>())
            {
                LocationCurve location = wall?.Location as LocationCurve;
                Curve curve = location?.Curve;
                if (curve == null)
                {
                    continue;
                }

                XYZ projected = ProjectPointToCurve(curve, markerPoint);
                if (projected == null)
                {
                    continue;
                }

                double distance = DistanceXY(markerPoint, projected);
                if (distance > maxDistance)
                {
                    continue;
                }

                if (best == null || distance < best.Distance)
                {
                    best = new WallProjection(wall, projected, distance);
                }
            }

            return best;
        }

        private static XYZ ProjectPointToCurve(Curve curve, XYZ point)
        {
            if (curve == null || point == null)
            {
                return null;
            }

            try
            {
                IntersectionResult result = curve.Project(point);
                if (result != null && result.XYZPoint != null)
                {
                    return result.XYZPoint;
                }
            }
            catch
            {
            }

            try
            {
                return curve.Evaluate(0.5, true);
            }
            catch
            {
                return null;
            }
        }

        private static double DistanceXY(XYZ first, XYZ second)
        {
            if (first == null || second == null)
            {
                return double.PositiveInfinity;
            }

            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void TryFaceHostedInstanceToMarkerSide(FamilyInstance instance, HostedDoorWindowPlacementCandidate placement)
        {
            if (instance == null || placement == null || placement.MarkerPoint == null || placement.HostPoint == null)
            {
                return;
            }

            try
            {
                XYZ markerSide = new XYZ(
                    placement.MarkerPoint.X - placement.HostPoint.X,
                    placement.MarkerPoint.Y - placement.HostPoint.Y,
                    0.0);
                if (markerSide.GetLength() < 1e-9)
                {
                    return;
                }

                markerSide = markerSide.Normalize();
                XYZ facing = instance.FacingOrientation;
                if (facing == null)
                {
                    return;
                }

                facing = new XYZ(facing.X, facing.Y, 0.0);
                if (facing.GetLength() < 1e-9)
                {
                    return;
                }

                facing = facing.Normalize();
                if (facing.DotProduct(markerSide) < 0.0 && instance.CanFlipFacing)
                {
                    instance.flipFacing();
                }
            }
            catch
            {
            }
        }

        private static bool IsMappingAction(MhnkArcSmartMappingRule rule, string action)
        {
            return rule != null &&
                   string.Equals(MhnkArcSmartMappingRules.NormalizeAction(rule.Action), action, StringComparison.OrdinalIgnoreCase);
        }

        private static IList<string> GetRuleTypeKeywords(MhnkArcSmartMappingRule rule, IList<string> fallbackKeywords)
        {
            IList<string> keywords = rule == null
                ? new List<string>()
                : MhnkArcSmartMappingRules.SplitKeywords(rule.RevitTypeKeywords);
            return keywords.Count > 0 ? keywords : fallbackKeywords ?? new List<string>();
        }

        private static double GetWallHeightMeters(MhnkArcToolSettings settings, MhnkArcSmartMappingRule rule)
        {
            if (rule != null && rule.HeightMeters > 0.0)
            {
                return rule.HeightMeters;
            }

            return settings.WallHeightMeters;
        }

        private static double GetOpeningDepthMeters(MhnkArcToolSettings settings, IList<CadCurveCandidate> candidates)
        {
            MhnkArcSmartMappingRule rule = (candidates ?? new List<CadCurveCandidate>())
                .Select(x => x.MappingRule)
                .FirstOrDefault(x => IsMappingAction(x, "Opening") && x.DepthMeters > 0.0);
            return rule != null ? rule.DepthMeters : settings.OpeningDepthMeters;
        }

        private static string GetMappingRuleName(MhnkArcSmartMappingRule rule)
        {
            return rule == null || string.IsNullOrWhiteSpace(rule.RuleName) ? "Fallback" : rule.RuleName;
        }

        private static string GetMappingRuleSummary(IList<CadCurveCandidate> candidates)
        {
            IList<string> names = (candidates ?? new List<CadCurveCandidate>())
                .Where(x => x.MappingRule != null)
                .Select(x => GetMappingRuleName(x.MappingRule))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                return "Fallback keyword settings";
            }

            string summary = string.Join(", ", names.Take(5));
            if (names.Count > 5)
            {
                summary += " +" + (names.Count - 5).ToString(CultureInfo.InvariantCulture);
            }

            return summary;
        }

        private static string GetCadLayerSummary(IList<CadCurveCandidate> candidates)
        {
            var layers = (candidates ?? new List<CadCurveCandidate>())
                .GroupBy(x => string.IsNullOrWhiteSpace(x.LayerName) ? "(model/detail)" : x.LayerName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { Layer = x.Key, Count = x.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Layer, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (layers.Count == 0)
            {
                return "None";
            }

            string summary = string.Join(", ", layers.Take(5).Select(x => x.Layer + " (" + x.Count.ToString(CultureInfo.InvariantCulture) + ")"));
            if (layers.Count > 5)
            {
                summary += " +" + (layers.Count - 5).ToString(CultureInfo.InvariantCulture);
            }

            return summary;
        }

        private static CadLoopPreviewData GetClosedLoopPreviewData(MhnkArcContext context, MhnkCadCurvePurpose purpose)
        {
            IList<ElementId> ids = GetCurveSourceElementIds(context, purpose);
            if (ids == null || ids.Count == 0)
            {
                return new CadLoopPreviewData(
                    new List<CadCurveCandidate>(),
                    new List<CurveLoop>(),
                    0,
                    GetSourceModeSummary(context));
            }

            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            List<CadCurveCandidate> candidates = GetSelectedCurveCandidates(context.Document, ids, settings, purpose);
            Level level = GetSeedOrActiveLevel(context);
            double elevation = level?.Elevation ?? 0.0;
            List<Curve> curves = candidates
                .Select(x => ProjectCurveToLevel(x.Curve, elevation))
                .Where(x => x != null && x.Length >= ShortCurveTolerance)
                .ToList();

            return new CadLoopPreviewData(
                candidates,
                BuildClosedLoopsFromCurves(curves),
                ids.Count,
                GetSourceModeSummary(context));
        }

        private static IList<CurveLoop> GetClosedLoopsFromSelection(MhnkArcContext context, MhnkCadCurvePurpose purpose)
        {
            return GetClosedLoopPreviewData(context, purpose).Loops;
        }

        private static XYZ GetCurveLoopCenter(CurveLoop loop)
        {
            if (loop == null)
            {
                return null;
            }

            var points = new List<XYZ>();
            foreach (Curve curve in loop)
            {
                points.Add(curve.GetEndPoint(0));
                points.Add(curve.GetEndPoint(1));
            }

            if (points.Count == 0)
            {
                return null;
            }

            return new XYZ(points.Average(x => x.X), points.Average(x => x.Y), points.Average(x => x.Z));
        }

        private static Solid CreateCandidateSolidFromLoop(CurveLoop loop, double thicknessFt)
        {
            if (loop == null)
            {
                return null;
            }

            try
            {
                return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, Math.Max(0.05, thicknessFt));
            }
            catch
            {
                return null;
            }
        }

        private static FamilySymbol GetFirstFamilySymbol(Document document, BuiltInCategory category)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.Category != null && ElementIdEquals(x.Category.Id, new ElementId(category)));
        }

        private static FamilySymbol GetPreferredFamilySymbol(Document document, BuiltInCategory category, IList<string> keywords)
        {
            IList<FamilySymbol> symbols = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && ElementIdEquals(x.Category.Id, new ElementId(category)))
                .ToList();

            if (symbols.Count == 0)
            {
                return null;
            }

            FamilySymbol preferred = symbols.FirstOrDefault(x => NameMatchesAnyKeyword((x.Family?.Name ?? "") + " " + x.Name, keywords));
            return preferred ?? symbols.FirstOrDefault();
        }

        private static void ActivateSymbol(Document document, FamilySymbol symbol)
        {
            if (symbol == null || symbol.IsActive)
            {
                return;
            }

            symbol.Activate();
            document.Regenerate();
        }

        private static FloorType GetPreferredFloorType(Document document, IList<CadCurveCandidate> candidates)
        {
            IList<string> keywords = GetFirstRuleTypeKeywords(candidates, "Floor");
            IList<FloorType> floorTypes = new FilteredElementCollector(document)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .ToList();

            FloorType preferred = floorTypes.FirstOrDefault(x => NameMatchesAnyKeyword(x.Name, keywords));
            string[] roomFloorKeywords =
            {
                "asphalt",
                "piso",
                "floor",
                "finish",
                "screed",
                "cer",
                "tile",
                "granite",
                "porce",
                "vinyl"
            };
            return preferred ??
                   floorTypes
                       .OrderBy(x => GetFloorTypePreferenceScore(x, roomFloorKeywords))
                       .ThenBy(x => x.Name)
                       .FirstOrDefault();
        }

        private static int GetFloorTypePreferenceScore(FloorType floorType, IList<string> keywords)
        {
            string name = (floorType?.Name ?? "").ToLowerInvariant();
            for (int i = 0; i < (keywords?.Count ?? 0); i++)
            {
                if (name.Contains(keywords[i]))
                {
                    return i;
                }
            }

            return 100;
        }

        private static CeilingType GetPreferredCeilingType(Document document, IList<CadCurveCandidate> candidates)
        {
            IList<string> keywords = GetFirstRuleTypeKeywords(candidates, "Ceiling");
            IList<CeilingType> ceilingTypes = new FilteredElementCollector(document)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .ToList();

            CeilingType preferred = ceilingTypes.FirstOrDefault(x => NameMatchesAnyKeyword(x.Name, keywords));
            string[] ceilingKeywords =
            {
                "forro",
                "ceiling",
                "gesso",
                "gypsum",
                "drywall",
                "suspended",
                "acoustic"
            };
            return preferred ??
                   ceilingTypes
                       .OrderBy(x => GetCeilingTypePreferenceScore(x, ceilingKeywords))
                       .ThenBy(x => x.Name)
                       .FirstOrDefault();
        }

        private static int GetCeilingTypePreferenceScore(CeilingType ceilingType, IList<string> keywords)
        {
            string name = (ceilingType?.Name ?? "").ToLowerInvariant();
            for (int i = 0; i < (keywords?.Count ?? 0); i++)
            {
                if (name.Contains(keywords[i]))
                {
                    return i;
                }
            }

            return 100;
        }

        private static WallType GetPreferredWallType(Document document, MhnkArcToolSettings settings, MhnkArcSmartMappingRule mappingRule)
        {
            IList<string> keywords = GetRuleTypeKeywords(mappingRule, settings.SplitKeywords(settings.PreferredWallTypeKeywords));
            IList<WallType> wallTypes = new FilteredElementCollector(document)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(x => x.Kind == WallKind.Basic)
                .ToList();

            WallType preferred = wallTypes.FirstOrDefault(x => NameMatchesAnyKeyword(x.Name, keywords));
            return preferred ??
                   wallTypes.FirstOrDefault() ??
                   new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault();
        }

        private static IList<string> GetFirstRuleTypeKeywords(IList<CadCurveCandidate> candidates, string action)
        {
            foreach (CadCurveCandidate candidate in candidates ?? new List<CadCurveCandidate>())
            {
                if (candidate.MappingRule == null || !IsMappingAction(candidate.MappingRule, action))
                {
                    continue;
                }

                IList<string> keywords = MhnkArcSmartMappingRules.SplitKeywords(candidate.MappingRule.RevitTypeKeywords);
                if (keywords.Count > 0)
                {
                    return keywords;
                }
            }

            return new List<string>();
        }

        private static bool NameMatchesAnyKeyword(string name, IList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return false;
            }

            return keywords.Any(keyword => (name ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unnamed";
            }

            char[] invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' }).Distinct().ToArray();
            string result = value;
            foreach (char c in invalid)
            {
                result = result.Replace(c, '_');
            }

            return result.Trim();
        }

        private static Result ShowFinishCandidateReport(MhnkArcContext context, string finishName, BuiltInCategory hostCategory)
        {
            int roomCount = new FilteredElementCollector(context.Document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .GetElementCount();
            int hostCount = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfCategory(hostCategory)
                .WhereElementIsNotElementType()
                .GetElementCount();

            string report =
                finishName + " finish candidate report" + Environment.NewLine +
                "Rooms in model: " + roomCount + Environment.NewLine +
                "Visible host elements: " + hostCount + Environment.NewLine +
                "Recommendation: assign finish rules by room name/department, then generate host-based finish elements.";

            string path = WriteReportFile("MHNK_" + finishName + "_Finish_Candidates", report);
            ShowResult("MHNK Creation", report + Environment.NewLine + Environment.NewLine + "Report: " + path);
            return Result.Succeeded;
        }

        private static bool TrySetLevelParameter(Element element, ElementId levelId)
        {
            if (element == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return false;
            }

            string[] preferredNames =
            {
                "Level",
                "Base Constraint",
                "Reference Level",
                "Schedule Level",
                "Base Level"
            };

            foreach (string name in preferredNames)
            {
                Parameter parameter = element.LookupParameter(name);
                if (TrySetElementIdParameter(parameter, levelId))
                {
                    return true;
                }
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter?.Definition == null)
                {
                    continue;
                }

                string name = parameter.Definition.Name ?? "";
                if ((name.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Base Constraint", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    TrySetElementIdParameter(parameter, levelId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySetElementIdParameter(Parameter parameter, ElementId id)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.ElementId)
            {
                return false;
            }

            try
            {
                if (!ElementIdEquals(parameter.AsElementId(), id))
                {
                    parameter.Set(id);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Parameter GetBestOffsetParameter(Element element)
        {
            if (element == null)
            {
                return null;
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter?.Definition == null || parameter.StorageType != StorageType.Double)
                {
                    continue;
                }

                string name = parameter.Definition.Name ?? "";
                if (name.IndexOf("Offset", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return parameter;
                }
            }

            return null;
        }

        private static IList<ParameterSignature> GetEditableSeedParameterSignatures(Element seed)
        {
            var signatures = new List<ParameterSignature>();
            if (seed == null)
            {
                return signatures;
            }

            string[] names = { "Mark", "Comments", "Type Comments", "Description" };
            foreach (string name in names)
            {
                Parameter parameter = seed.LookupParameter(name);
                ParameterSignature signature = CreateParameterSignature(parameter);
                if (signature != null)
                {
                    signatures.Add(signature);
                }
            }

            return signatures;
        }

        private static bool SetParameterFromSignature(Parameter target, ParameterSignature signature)
        {
            if (target == null || signature == null || target.IsReadOnly || target.StorageType != signature.StorageType)
            {
                return false;
            }

            try
            {
                switch (target.StorageType)
                {
                    case StorageType.String:
                        target.Set(signature.ComparableValue);
                        return true;
                    case StorageType.Integer:
                        int intValue;
                        if (int.TryParse(signature.ComparableValue, out intValue))
                        {
                            target.Set(intValue);
                            return true;
                        }
                        break;
                    case StorageType.Double:
                        double doubleValue;
                        if (double.TryParse(signature.ComparableValue, out doubleValue))
                        {
                            target.Set(doubleValue);
                            return true;
                        }
                        break;
                }
            }
            catch
            {
            }

            return false;
        }

        private static XYZ GetElementCenter(Element element)
        {
            if (element == null)
            {
                return null;
            }

            LocationPoint point = element.Location as LocationPoint;
            if (point != null)
            {
                return point.Point;
            }

            LocationCurve curve = element.Location as LocationCurve;
            if (curve?.Curve != null)
            {
                return curve.Curve.Evaluate(0.5, true);
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                return null;
            }

            return (box.Min + box.Max) * 0.5;
        }

        private static string GetDuplicateKey(Element element)
        {
            if (element?.Category == null)
            {
                return "";
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null)
            {
                return "";
            }

            return GetElementIdText(element.Category.Id) + "|" +
                   GetElementIdText(element.GetTypeId()) + "|" +
                   RoundForKey(box.Min.X) + "," + RoundForKey(box.Min.Y) + "," + RoundForKey(box.Min.Z) + "|" +
                   RoundForKey(box.Max.X) + "," + RoundForKey(box.Max.Y) + "," + RoundForKey(box.Max.Z);
        }

        private static string RoundForKey(double value)
        {
            return Math.Round(value, 4).ToString("R");
        }

        private static bool IsCategoryInList(ElementId categoryId, IList<BuiltInCategory> categories)
        {
            if (categoryId == null)
            {
                return false;
            }

            string idText = GetElementIdText(categoryId);
            return categories.Any(x => string.Equals(idText, GetElementIdText(new ElementId(x)), StringComparison.OrdinalIgnoreCase));
        }

        private static IList<BuiltInCategory> GetArcHostCategories()
        {
            return new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Railings
            };
        }

        private static IList<ElementPair> FindIntersectingPairs(IList<Element> firstSet, IList<Element> secondSet, int maxPairs, bool allowSameId)
        {
            var pairs = new List<ElementPair>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            double toleranceFt = MillimetersToFeet(MhnkArcToolSettings.Load().ClashToleranceMillimeters);

            foreach (Element first in firstSet)
            {
                BoundingBoxXYZ firstBox = first?.get_BoundingBox(null);
                if (firstBox == null)
                {
                    continue;
                }

                BoundingBoxXYZ expandedFirstBox = ExpandBoundingBox(firstBox, toleranceFt);

                foreach (Element second in secondSet)
                {
                    if (second == null || (!allowSameId && ElementIdEquals(first.Id, second.Id)))
                    {
                        continue;
                    }

                    string key = GetOrderedPairKey(first.Id, second.Id);
                    if (seen.Contains(key))
                    {
                        continue;
                    }

                    BoundingBoxXYZ secondBox = second.get_BoundingBox(null);
                    if (secondBox == null || !BoundingBoxesIntersect(expandedFirstBox, ExpandBoundingBox(secondBox, toleranceFt)))
                    {
                        continue;
                    }

                    seen.Add(key);
                    pairs.Add(new ElementPair(first, second));
                    if (pairs.Count >= maxPairs)
                    {
                        return pairs;
                    }
                }
            }

            return pairs;
        }

        private static IList<Element> GetSelectedGeometryOperationElements(MhnkArcContext context, string emptyMessage, int maxElements)
        {
            IList<Element> elements = GetSelectionElements(context)
                .Where(x => x != null && !(x is ElementType) && x.Category != null)
                .GroupBy(x => GetElementIdText(x.Id), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .Take(Math.Max(2, maxElements))
                .ToList();

            if (elements.Count < 2)
            {
                ShowResult("MHNK ARC", emptyMessage);
                return null;
            }

            return elements;
        }

        private static IList<ElementPair> BuildAllUniquePairs(IList<Element> elements, int maxPairs)
        {
            var pairs = new List<ElementPair>();
            if (elements == null || elements.Count < 2)
            {
                return pairs;
            }

            int pairLimit = Math.Max(1, maxPairs);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < elements.Count; i++)
            {
                Element first = elements[i];
                if (first == null)
                {
                    continue;
                }

                for (int j = i + 1; j < elements.Count; j++)
                {
                    Element second = elements[j];
                    if (second == null || ElementIdEquals(first.Id, second.Id))
                    {
                        continue;
                    }

                    string key = GetOrderedPairKey(first.Id, second.Id);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    pairs.Add(new ElementPair(first, second));
                    if (pairs.Count >= pairLimit)
                    {
                        return pairs;
                    }
                }
            }

            return pairs;
        }

        private static string GetOrderedPairKey(ElementId first, ElementId second)
        {
            string a = GetElementIdText(first);
            string b = GetElementIdText(second);
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static bool BoundingBoxesIntersect(BoundingBoxXYZ first, BoundingBoxXYZ second)
        {
            return first.Min.X <= second.Max.X && first.Max.X >= second.Min.X &&
                   first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y &&
                   first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;
        }

        private static BoundingBoxXYZ ExpandBoundingBox(BoundingBoxXYZ box, double amount)
        {
            if (box == null || amount <= 0)
            {
                return box;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(box.Min.X - amount, box.Min.Y - amount, box.Min.Z - amount),
                Max = new XYZ(box.Max.X + amount, box.Max.Y + amount, box.Max.Z + amount)
            };
        }

        private static double MetersToFeet(double value)
        {
            return value * 3.280839895013123;
        }

        private static double MillimetersToFeet(double value)
        {
            return value / 304.8;
        }

        private static string BuildPairReport(string title, IList<ElementPair> pairs, string footer)
        {
            var report = new StringBuilder();
            report.AppendLine(title + ": " + pairs.Count);
            foreach (ElementPair pair in pairs.Take(20))
            {
                report.AppendLine("- " + GetElementLabel(pair.First) + " <-> " + GetElementLabel(pair.Second));
            }

            if (pairs.Count > 20)
            {
                report.AppendLine("Showing first 20 pair(s).");
            }

            report.AppendLine();
            report.AppendLine(footer);
            return report.ToString();
        }

        private static IList<Solid> CollectElementSolids(Element element)
        {
            var solids = new List<Solid>();
            if (element == null)
            {
                return solids;
            }

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            CollectSolids(element.get_Geometry(options), solids);
            return solids;
        }

        private static void CollectSolids(GeometryElement geometry, IList<Solid> solids)
        {
            if (geometry == null)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                Solid solid = geometryObject as Solid;
                if (solid != null && solid.Volume > 1e-9)
                {
                    solids.Add(solid);
                    continue;
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance != null)
                {
                    CollectSolids(instance.GetInstanceGeometry(), solids);
                }
            }
        }

        private static bool IsMhnkGeneratedDirectShape(DirectShape shape)
        {
            if (shape == null || !string.Equals(shape.ApplicationId, "MHNK", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string dataId = shape.ApplicationDataId ?? "";
            return dataId.StartsWith("MHNK_ARC_BOUNDING_SOLID_", StringComparison.OrdinalIgnoreCase) ||
                   dataId.StartsWith("MHNK_ARC_OPENING_CANDIDATE_", StringComparison.OrdinalIgnoreCase) ||
                   dataId.StartsWith("MHNK_ARC_DIRECTSHAPE_COPY_", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetElementLabel(Element element)
        {
            if (element == null)
            {
                return "<null>";
            }

            string category = element.Category?.Name ?? "No category";
            return category + " " + element.Id.Value + " " + element.Name;
        }

        private static string WriteReportFile(string baseName, string content)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "Reports");
            Directory.CreateDirectory(folder);
            string fileName = SanitizeName(baseName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, content ?? "");
            return path;
        }

        private static void AppendTypeCheck(StringBuilder report, string label, bool ok)
        {
            report.AppendLine((ok ? "[OK] " : "[Missing] ") + label);
        }

        private static bool HasElementType<T>(Document document) where T : ElementType
        {
            return new FilteredElementCollector(document).OfClass(typeof(T)).GetElementCount() > 0;
        }

        private static bool HasFamilySymbol(Document document, BuiltInCategory category)
        {
            return GetFirstFamilySymbol(document, category) != null;
        }

        private static MhnkArcQaDashboardData BuildQaDashboardData(MhnkArcContext context)
        {
            IList<FailureMessage> warnings = context.Document.GetWarnings();
            int importCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .GetElementCount();
            int activeViewImportCount = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .GetElementCount();
            int roomCount = new FilteredElementCollector(context.Document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .GetElementCount();
            int zeroAreaRooms = new FilteredElementCollector(context.Document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Count(x => x.Area <= 1e-6);
            int viewCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(x => !x.IsTemplate);
            int sheetCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ViewSheet))
                .GetElementCount();
            int levelCount = new FilteredElementCollector(context.Document)
                .OfClass(typeof(Level))
                .GetElementCount();
            int arcCoordinationCount = new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(GetArcCoordinationCategories()))
                .GetElementCount();
            int mhnkSolids = new FilteredElementCollector(context.Document)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Count(IsMhnkGeneratedDirectShape);

            MhnkArcSmartMappingRules mappingRules = MhnkArcSmartMappingRules.Load();
            int activeMappingRules = mappingRules.Rules.Count(x => x.Enabled);
            int invalidMappingRules = mappingRules.Rules.Count(x => IsMappingRuleInvalid(x));
            int duplicateMappingKeywords = CountDuplicateMappingKeywords(mappingRules.Rules);

            var data = new MhnkArcQaDashboardData
            {
                Title = "MHNK ARC QA Dashboard"
            };

            data.Metrics.Add(Metric("Model", "Warnings", warnings.Count.ToString(), warnings.Count == 0 ? "OK" : "Review", "Open Warnings tab for details."));
            data.Metrics.Add(Metric("Model", "CAD imports", importCount.ToString(), importCount == 0 ? "OK" : "Review", "Keep only needed CAD imports for stable model performance."));
            data.Metrics.Add(Metric("Active View", "CAD imports", activeViewImportCount.ToString(), activeViewImportCount == 0 ? "OK" : "Review", "Visible CAD can slow active ARC views."));
            data.Metrics.Add(Metric("Model", "Views", viewCount.ToString(), "Info", "Non-template views."));
            data.Metrics.Add(Metric("Model", "Sheets", sheetCount.ToString(), "Info", "Project sheet count."));
            data.Metrics.Add(Metric("Model", "Levels", levelCount.ToString(), levelCount > 0 ? "OK" : "Action", "Creation tools need project levels."));
            data.Metrics.Add(Metric("Rooms", "Rooms", roomCount.ToString(), roomCount > 0 ? "OK" : "Review", "Rooms are required for room and finish QA."));
            data.Metrics.Add(Metric("Rooms", "Zero-area rooms", zeroAreaRooms.ToString(), zeroAreaRooms == 0 ? "OK" : "Action", "Fix or remove unbounded rooms."));
            data.Metrics.Add(Metric("ARC", "ARC coordination elements in active view", arcCoordinationCount.ToString(), "Info", "Visible ARC/MEP coordination categories."));
            data.Metrics.Add(Metric("Solids", "MHNK generated solids", mhnkSolids.ToString(), "Info", "Bounding, opening, and copied DirectShape solids."));
            data.Metrics.Add(Metric("Mapping", "Active mapping rules", activeMappingRules.ToString(), activeMappingRules > 0 ? "OK" : "Action", "CAD-to-model tools use these rules first."));
            data.Metrics.Add(Metric("Mapping", "Invalid mapping rules", invalidMappingRules.ToString(), invalidMappingRules == 0 ? "OK" : "Action", "Rules need names, layers, and valid actions."));
            data.Metrics.Add(Metric("Mapping", "Duplicate mapping keywords", duplicateMappingKeywords.ToString(), duplicateMappingKeywords == 0 ? "OK" : "Review", "Overlapping keywords can map one CAD layer to the wrong action."));

            data.Checks.Add(Check(1, "Required Types", HasElementType<WallType>(context.Document) ? "OK" : "Action", "Wall type available", ""));
            data.Checks.Add(Check(2, "Required Types", HasElementType<FloorType>(context.Document) ? "OK" : "Action", "Floor type available", ""));
            data.Checks.Add(Check(3, "Required Types", HasElementType<CeilingType>(context.Document) ? "OK" : "Action", "Ceiling type available", ""));
            data.Checks.Add(Check(4, "Required Families", HasFamilySymbol(context.Document, BuiltInCategory.OST_Doors) ? "OK" : "Action", "Door family symbol loaded", ""));
            data.Checks.Add(Check(5, "Required Families", HasFamilySymbol(context.Document, BuiltInCategory.OST_Windows) ? "OK" : "Action", "Window family symbol loaded", ""));
            data.Checks.Add(Check(6, "Rooms", zeroAreaRooms == 0 ? "OK" : "Action", "Zero-area room check", zeroAreaRooms.ToString()));
            data.Checks.Add(Check(7, "Mapping", activeMappingRules > 0 ? "OK" : "Action", "Smart mapping rules active", activeMappingRules.ToString()));
            data.Checks.Add(Check(8, "Mapping", invalidMappingRules == 0 ? "OK" : "Action", "Invalid mapping rule check", invalidMappingRules.ToString()));
            data.Checks.Add(Check(9, "Mapping", duplicateMappingKeywords == 0 ? "OK" : "Review", "Duplicate mapping keyword check", duplicateMappingKeywords.ToString()));
            data.Checks.Add(Check(10, "Performance", importCount <= 5 ? "OK" : "Review", "CAD import count", importCount.ToString()));
            data.Checks.Add(Check(11, "Warnings", warnings.Count == 0 ? "OK" : "Review", "Document warnings", warnings.Count.ToString()));

            data.Warnings = BuildWarningQaItems(warnings);
            data.CadImports = BuildCadImportQaItems(context);
            data.MappingRules = BuildMappingRuleQaItems(mappingRules.Rules);

            int actionCount = data.Checks.Count(x => string.Equals(x.Status, "Action", StringComparison.OrdinalIgnoreCase));
            int reviewCount = data.Checks.Count(x => string.Equals(x.Status, "Review", StringComparison.OrdinalIgnoreCase));
            string overall = actionCount == 0 && reviewCount == 0 ? "Ready" : actionCount > 0 ? "Action needed" : "Review";

            data.Summary =
                "Document: " + context.Document.Title + Environment.NewLine +
                "Active view: " + context.ActiveView.Name + Environment.NewLine +
                "Overall: " + overall + " | Action: " + actionCount + " | Review: " + reviewCount + Environment.NewLine +
                "Settings: " + MhnkArcToolSettings.GetSettingsPath() + Environment.NewLine +
                "Mapping rules: " + MhnkArcSmartMappingRules.GetRulesPath();
            data.Report = BuildQaReportFromDashboard(data, context);
            return data;
        }

        private static MhnkArcQaMetric Metric(string area, string name, string value, string status, string notes)
        {
            return new MhnkArcQaMetric
            {
                Area = area,
                Name = name,
                Value = value,
                Status = status,
                Notes = notes
            };
        }

        private static MhnkArcQaItem Check(int index, string category, string status, string detail, string elementIds)
        {
            return new MhnkArcQaItem
            {
                Index = index.ToString(CultureInfo.InvariantCulture),
                Category = category,
                Status = status,
                Detail = detail,
                ElementIds = elementIds
            };
        }

        private static IList<MhnkArcQaItem> BuildWarningQaItems(IList<FailureMessage> warnings)
        {
            var items = new List<MhnkArcQaItem>();
            int index = 1;
            foreach (FailureMessage warning in (warnings ?? new List<FailureMessage>()).Take(100))
            {
                ICollection<ElementId> failingIds = warning.GetFailingElements();
                items.Add(new MhnkArcQaItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Category = "Warning",
                    Status = "Review",
                    Detail = warning.GetDescriptionText() ?? "",
                    ElementIds = failingIds == null ? "" : string.Join(", ", failingIds.Take(12).Select(GetElementIdText).ToArray())
                });
                index++;
            }

            return items;
        }

        private static IList<MhnkArcQaItem> BuildCadImportQaItems(MhnkArcContext context)
        {
            var items = new List<MhnkArcQaItem>();
            IList<ImportInstance> imports = new FilteredElementCollector(context.Document)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .Cast<ImportInstance>()
                .Take(200)
                .ToList();
            HashSet<ElementId> activeImportIds = new HashSet<ElementId>(
                new FilteredElementCollector(context.Document, context.ActiveView.Id)
                    .OfClass(typeof(ImportInstance))
                    .WhereElementIsNotElementType()
                    .ToElementIds(),
                new ElementIdComparer());

            int index = 1;
            foreach (ImportInstance import in imports)
            {
                bool visibleInActiveView = activeImportIds.Contains(import.Id);

                items.Add(new MhnkArcQaItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Category = "CAD Import",
                    Status = visibleInActiveView ? "Review" : "Info",
                    Detail = (import.Name ?? "CAD Import") + (visibleInActiveView ? " | visible in active view" : " | not visible in active view"),
                    ElementIds = GetElementIdText(import.Id)
                });
                index++;
            }

            return items;
        }

        private static IList<MhnkArcQaItem> BuildMappingRuleQaItems(IList<MhnkArcSmartMappingRule> rules)
        {
            var items = new List<MhnkArcQaItem>();
            int index = 1;
            foreach (MhnkArcSmartMappingRule rule in rules ?? new List<MhnkArcSmartMappingRule>())
            {
                bool invalid = IsMappingRuleInvalid(rule);
                items.Add(new MhnkArcQaItem
                {
                    Index = index.ToString(CultureInfo.InvariantCulture),
                    Category = MhnkArcSmartMappingRules.NormalizeAction(rule.Action),
                    Status = !rule.Enabled ? "Off" : invalid ? "Action" : "OK",
                    Detail = (rule.RuleName ?? "") + " | layers: " + (rule.LayerKeywords ?? "") + " | types: " + (rule.RevitTypeKeywords ?? ""),
                    ElementIds = ""
                });
                index++;
            }

            return items;
        }

        private static bool IsMappingRuleInvalid(MhnkArcSmartMappingRule rule)
        {
            if (rule == null || !rule.Enabled)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(rule.RuleName) ||
                   string.IsNullOrWhiteSpace(rule.LayerKeywords) ||
                   string.IsNullOrWhiteSpace(rule.Action);
        }

        private static int CountDuplicateMappingKeywords(IList<MhnkArcSmartMappingRule> rules)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int duplicates = 0;

            foreach (MhnkArcSmartMappingRule rule in rules ?? new List<MhnkArcSmartMappingRule>())
            {
                if (rule == null || !rule.Enabled)
                {
                    continue;
                }

                foreach (string keyword in MhnkArcSmartMappingRules.SplitKeywords(rule.LayerKeywords))
                {
                    string action = MhnkArcSmartMappingRules.NormalizeAction(rule.Action);
                    string previousAction;
                    if (seen.TryGetValue(keyword, out previousAction))
                    {
                        if (!string.Equals(previousAction, action, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicates++;
                        }
                    }
                    else
                    {
                        seen[keyword] = action;
                    }
                }
            }

            return duplicates;
        }

        private static string BuildQaReportFromDashboard(MhnkArcQaDashboardData data, MhnkArcContext context)
        {
            var report = new StringBuilder();
            report.AppendLine("MHNK QA Report");
            report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Document: " + context.Document.Title);
            report.AppendLine("Active view: " + context.ActiveView.Name);
            report.AppendLine("Settings: " + MhnkArcToolSettings.GetSettingsPath());
            report.AppendLine("Mapping rules: " + MhnkArcSmartMappingRules.GetRulesPath());
            report.AppendLine();
            report.AppendLine(data.Summary ?? "");
            report.AppendLine();

            AppendQaMetricSection(report, "Overview", data.Metrics);
            AppendQaItemSection(report, "Checks", data.Checks);
            AppendQaItemSection(report, "Warnings", data.Warnings);
            AppendQaItemSection(report, "CAD Imports", data.CadImports);
            AppendQaItemSection(report, "Mapping Rules", data.MappingRules);
            return report.ToString();
        }

        private static void AppendQaMetricSection(StringBuilder report, string title, IList<MhnkArcQaMetric> metrics)
        {
            report.AppendLine(title + ":");
            foreach (MhnkArcQaMetric metric in metrics ?? new List<MhnkArcQaMetric>())
            {
                report.AppendLine("- [" + metric.Status + "] " + metric.Area + " | " + metric.Name + " = " + metric.Value + " | " + metric.Notes);
            }

            report.AppendLine();
        }

        private static void AppendQaItemSection(StringBuilder report, string title, IList<MhnkArcQaItem> items)
        {
            report.AppendLine(title + ":");
            foreach (MhnkArcQaItem item in items ?? new List<MhnkArcQaItem>())
            {
                report.AppendLine("- [" + item.Status + "] " + item.Category + " | " + item.Detail + (string.IsNullOrWhiteSpace(item.ElementIds) ? "" : " | IDs: " + item.ElementIds));
            }

            if (items == null || items.Count == 0)
            {
                report.AppendLine("- None");
            }

            report.AppendLine();
        }

        private static string BuildQaReport(MhnkArcContext context)
        {
            int warnings = context.Document.GetWarnings().Count;
            int imports = new FilteredElementCollector(context.Document).OfClass(typeof(ImportInstance)).WhereElementIsNotElementType().GetElementCount();
            int rooms = new FilteredElementCollector(context.Document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().GetElementCount();
            int zeroAreaRooms = new FilteredElementCollector(context.Document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Count(x => x.Area <= 1e-6);
            int mhnkSolids = new FilteredElementCollector(context.Document)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Count(IsMhnkGeneratedDirectShape);

            var report = new StringBuilder();
            report.AppendLine("MHNK QA Report");
            report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Document: " + context.Document.Title);
            report.AppendLine("Active view: " + context.ActiveView.Name);
            report.AppendLine("Settings: " + MhnkArcToolSettings.GetSettingsPath());
            report.AppendLine();
            report.AppendLine("Warnings: " + warnings);
            report.AppendLine("CAD imports: " + imports);
            report.AppendLine("Rooms: " + rooms);
            report.AppendLine("Zero-area rooms: " + zeroAreaRooms);
            report.AppendLine("MHNK generated solids: " + mhnkSolids);
            report.AppendLine("Selection count: " + context.UiDocument.Selection.GetElementIds().Count);
            report.AppendLine();
            report.AppendLine("Missing type check:");
            AppendTypeCheck(report, "Wall type", HasElementType<WallType>(context.Document));
            AppendTypeCheck(report, "Floor type", HasElementType<FloorType>(context.Document));
            AppendTypeCheck(report, "Ceiling type", HasElementType<CeilingType>(context.Document));
            AppendTypeCheck(report, "Door family symbol", HasFamilySymbol(context.Document, BuiltInCategory.OST_Doors));
            AppendTypeCheck(report, "Window family symbol", HasFamilySymbol(context.Document, BuiltInCategory.OST_Windows));
            return report.ToString();
        }

        private enum MhnkCadCurvePurpose
        {
            Any,
            Walls,
            Floors,
            Ceilings,
            RoomBoundaries,
            Openings,
            DoorsWindows
        }

        private sealed class CadCurveCandidate
        {
            public CadCurveCandidate(Curve curve, string layerName, ElementId sourceElementId, MhnkArcSmartMappingRule mappingRule)
            {
                Curve = curve;
                LayerName = layerName ?? "";
                SourceElementId = sourceElementId;
                MappingRule = mappingRule;
            }

            public Curve Curve { get; }
            public string LayerName { get; }
            public ElementId SourceElementId { get; }
            public MhnkArcSmartMappingRule MappingRule { get; }
        }

        private sealed class CadLoopPreviewData
        {
            public CadLoopPreviewData(IList<CadCurveCandidate> candidates, IList<CurveLoop> loops)
                : this(candidates, loops, 0, "")
            {
            }

            public CadLoopPreviewData(
                IList<CadCurveCandidate> candidates,
                IList<CurveLoop> loops,
                int sourceElementCount,
                string sourceDescription)
            {
                Candidates = candidates ?? new List<CadCurveCandidate>();
                Loops = loops ?? new List<CurveLoop>();
                SourceElementCount = sourceElementCount;
                SourceDescription = sourceDescription ?? "";
            }

            public IList<CadCurveCandidate> Candidates { get; }
            public IList<CurveLoop> Loops { get; }
            public int SourceElementCount { get; }
            public string SourceDescription { get; }
        }

        private sealed class RoomBoundaryCurveCandidate
        {
            public RoomBoundaryCurveCandidate(
                Room room,
                Level level,
                Curve curve,
                ElementId boundaryElementId,
                string boundaryElementName,
                int adjacentRoomCount)
            {
                Room = room;
                Level = level;
                Curve = curve;
                BoundaryElementId = boundaryElementId;
                BoundaryElementName = boundaryElementName ?? "";
                AdjacentRoomCount = Math.Max(1, adjacentRoomCount);
            }

            public Room Room { get; }
            public Level Level { get; }
            public Curve Curve { get; }
            public ElementId BoundaryElementId { get; }
            public string BoundaryElementName { get; }
            public int AdjacentRoomCount { get; }
            public bool IsExteriorBoundary => AdjacentRoomCount <= 1;
        }

        private sealed class RoomBoundaryAggregate
        {
            private readonly HashSet<string> _roomIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public RoomBoundaryAggregate(Room room, Level level, Curve curve, ElementId boundaryElementId, string boundaryElementName)
            {
                Room = room;
                Level = level;
                Curve = curve;
                BoundaryElementId = boundaryElementId;
                BoundaryElementName = boundaryElementName ?? "";
                AddRoom(room);
            }

            public Room Room { get; }
            public Level Level { get; }
            public Curve Curve { get; }
            public ElementId BoundaryElementId { get; }
            public string BoundaryElementName { get; }
            public int AdjacentRoomCount => _roomIds.Count;

            public void AddRoom(Room room)
            {
                if (room?.Id != null)
                {
                    _roomIds.Add(GetElementIdText(room.Id));
                }
            }
        }

        private sealed class WallFinishCandidate
        {
            public WallFinishCandidate(
                Wall hostWall,
                Level level,
                Curve curve,
                double baseOffset,
                double initialHeight,
                ElementId topConstraintLevelId,
                double topOffset)
            {
                HostWall = hostWall;
                Level = level;
                Curve = curve;
                BaseOffset = baseOffset;
                InitialHeight = initialHeight;
                TopConstraintLevelId = topConstraintLevelId ?? ElementId.InvalidElementId;
                TopOffset = topOffset;
            }

            public Wall HostWall { get; }
            public Level Level { get; }
            public Curve Curve { get; }
            public double BaseOffset { get; }
            public double InitialHeight { get; }
            public ElementId TopConstraintLevelId { get; }
            public double TopOffset { get; }
        }

        private sealed class HorizontalWallSplitCandidate
        {
            public HorizontalWallSplitCandidate(
                Wall hostWall,
                Level level,
                Curve curve,
                double baseOffset,
                double originalHeight,
                double splitHeight,
                ElementId topConstraintLevelId,
                double topOffset)
            {
                HostWall = hostWall;
                Level = level;
                Curve = curve;
                BaseOffset = baseOffset;
                OriginalHeight = originalHeight;
                SplitHeight = splitHeight;
                TopConstraintLevelId = topConstraintLevelId ?? ElementId.InvalidElementId;
                TopOffset = topOffset;
            }

            public Wall HostWall { get; }
            public Level Level { get; }
            public Curve Curve { get; }
            public double BaseOffset { get; }
            public double OriginalHeight { get; }
            public double SplitHeight { get; }
            public ElementId TopConstraintLevelId { get; }
            public double TopOffset { get; }
            public double UpperHeight => OriginalHeight - SplitHeight;
            public bool CanSplit => SplitHeight > ShortCurveTolerance && UpperHeight > ShortCurveTolerance;
        }

        private sealed class WallCeilingTrimCandidate
        {
            public WallCeilingTrimCandidate(
                Wall wall,
                Level level,
                Ceiling ceiling,
                double baseOffset,
                double originalHeight,
                double targetHeight,
                bool canApply)
            {
                Wall = wall;
                Level = level;
                Ceiling = ceiling;
                BaseOffset = baseOffset;
                OriginalHeight = originalHeight;
                TargetHeight = targetHeight;
                CanApply = canApply;
            }

            public Wall Wall { get; }
            public Level Level { get; }
            public Ceiling Ceiling { get; }
            public double BaseOffset { get; }
            public double OriginalHeight { get; }
            public double TargetHeight { get; }
            public bool CanApply { get; }
        }

        private sealed class WallProjection
        {
            public WallProjection(Wall wall, XYZ point, double distance)
            {
                Wall = wall;
                Point = point;
                Distance = distance;
            }

            public Wall Wall { get; }
            public XYZ Point { get; }
            public double Distance { get; }
        }

        private sealed class HostedDoorWindowPlacementCandidate
        {
            public HostedDoorWindowPlacementCandidate(
                CadCurveCandidate source,
                FamilySymbol symbol,
                bool isWindow,
                Wall hostWall,
                Level level,
                XYZ markerPoint,
                XYZ hostPoint,
                double hostDistance,
                double windowSillHeight)
            {
                Source = source;
                Symbol = symbol;
                IsWindow = isWindow;
                HostWall = hostWall;
                Level = level;
                MarkerPoint = markerPoint;
                HostPoint = hostPoint;
                HostDistance = hostDistance;
                WindowSillHeight = windowSillHeight;
            }

            public CadCurveCandidate Source { get; }
            public FamilySymbol Symbol { get; }
            public bool IsWindow { get; }
            public Wall HostWall { get; }
            public Level Level { get; }
            public XYZ MarkerPoint { get; }
            public XYZ HostPoint { get; }
            public double HostDistance { get; }
            public double WindowSillHeight { get; }
            public string SourceLayer => string.IsNullOrWhiteSpace(Source?.LayerName) ? "(model)" : Source.LayerName;
            public string SourceLabel => Source == null ? "Marker" : "Element " + GetElementIdText(Source.SourceElementId);
            public bool CanPlace => Symbol != null && HostWall != null && Level != null && HostPoint != null;
            public string SkipReason
            {
                get
                {
                    if (Symbol == null) return "No matching loaded symbol";
                    if (HostWall == null) return "No wall host within max distance";
                    if (Level == null) return "Host wall has no level";
                    if (HostPoint == null) return "Could not project marker onto host wall";
                    return "Not ready";
                }
            }
        }

        private sealed class RoomLoopCandidate
        {
            public RoomLoopCandidate(Room room, Level level, IList<CurveLoop> loops)
            {
                Room = room;
                Level = level;
                Loops = loops ?? new List<CurveLoop>();
            }

            public Room Room { get; }
            public Level Level { get; }
            public IList<CurveLoop> Loops { get; }
        }

        private sealed class ParameterSignature
        {
            public ParameterSignature(string name, StorageType storageType, string comparableValue, string displayValue)
            {
                Name = name ?? "";
                StorageType = storageType;
                ComparableValue = comparableValue ?? "";
                DisplayValue = string.IsNullOrWhiteSpace(displayValue) ? ComparableValue : displayValue;
            }

            public string Name { get; }
            public StorageType StorageType { get; }
            public string ComparableValue { get; }
            public string DisplayValue { get; }
        }

        private sealed class ElementPair
        {
            public ElementPair(Element first, Element second)
            {
                First = first;
                Second = second;
            }

            public Element First { get; }
            public Element Second { get; }
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                return ElementIdEquals(x, y);
            }

            public int GetHashCode(ElementId obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(GetElementIdText(obj));
            }
        }

        private static Result ShowPlannedFeature(string category, string title)
        {
            ShowResult(
                "MHNK " + category,
                title + " is included in the full eTLipse-style MHNK option set." + Environment.NewLine +
                "The command surface is ready; the production engine for this workflow is the next implementation step.");
            return Result.Cancelled;
        }

        private static Result SetPinnedState(MhnkArcContext context, bool pinned)
        {
            ICollection<ElementId> ids = GetSelectionOrCancel(context, "Select elements first, then run ARC > Edition.");
            if (ids == null)
            {
                return Result.Cancelled;
            }

            int changed = 0;
            using (Transaction t = new Transaction(context.Document, pinned ? "MHNK - Pin Selection" : "MHNK - Unpin Selection"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Element element = context.Document.GetElement(id);
                    if (element != null && element.Pinned != pinned)
                    {
                        element.Pinned = pinned;
                        changed++;
                    }
                }

                t.Commit();
            }

            ShowResult("MHNK Edition", (pinned ? "Pinned " : "Unpinned ") + changed + " selected element(s).");
            return Result.Succeeded;
        }

        private static ICollection<ElementId> GetSelectionOrCancel(MhnkArcContext context, string message)
        {
            ICollection<ElementId> ids;
            if (context.SourceMode == MhnkArcSourceMode.All)
            {
                ids = GetVisibleElements(context).Select(x => x.Id).ToList();
            }
            else if (context.SourceMode == MhnkArcSourceMode.ByLayer)
            {
                ids = GetLayerSourceElementIds(context);
                if (ids.Count == 0)
                {
                    ids = context.UiDocument.Selection.GetElementIds();
                }
            }
            else if (context.SourceMode == MhnkArcSourceMode.Category)
            {
                ids = GetCategoryScopeElementIds(context);
            }
            else
            {
                ids = context.UiDocument.Selection.GetElementIds();
            }

            if (ids == null || ids.Count == 0)
            {
                ShowResult("MHNK ARC", message);
                return null;
            }

            return ids;
        }

        private static IList<ElementId> GetCategoryScopeElementIds(MhnkArcContext context)
        {
            IList<BuiltInCategory> categories = GetToolScopeCategories(context?.CommandOption);
            if (categories.Count > 0)
            {
                return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(categories))
                    .ToElementIds()
                    .ToList();
            }

            Element seed = context?.UiDocument?.Selection.GetElementIds()
                .Select(id => context.Document.GetElement(id))
                .FirstOrDefault(x => x?.Category != null);
            if (seed?.Category == null)
            {
                return new List<ElementId>();
            }

            ElementId categoryId = seed.Category.Id;
            return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(x => x.Category != null && ElementIdEquals(x.Category.Id, categoryId))
                .Select(x => x.Id)
                .ToList();
        }

        private static IList<BuiltInCategory> GetToolScopeCategories(MhnkArcCommandOption option)
        {
            string text = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (string.Equals(option?.Category, "Solids", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("intersection") || text.Contains("clash") || text.Contains("opening candidate")))
            {
                return GetArcCoordinationCategories();
            }

            if (text.Contains("room"))
            {
                return new List<BuiltInCategory> { BuiltInCategory.OST_Rooms };
            }

            if (text.Contains("door") || text.Contains("window"))
            {
                return new List<BuiltInCategory> { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows };
            }

            if (text.Contains("wall"))
            {
                return new List<BuiltInCategory> { BuiltInCategory.OST_Walls };
            }

            if (text.Contains("floor"))
            {
                return new List<BuiltInCategory> { BuiltInCategory.OST_Floors };
            }

            if (text.Contains("ceiling"))
            {
                return new List<BuiltInCategory> { BuiltInCategory.OST_Ceilings };
            }

            if (text.Contains("mep"))
            {
                return GetMepCoordinationCategories();
            }

            if (text.Contains("solid") || text.Contains("opening"))
            {
                return new List<BuiltInCategory> { BuiltInCategory.OST_GenericModel };
            }

            return new List<BuiltInCategory>();
        }

        private static Element GetFirstSelectedElement(MhnkArcContext context)
        {
            ElementId id = context.UiDocument.Selection.GetElementIds().FirstOrDefault();
            return id == null ? null : context.Document.GetElement(id);
        }

        private static IList<BuiltInCategory> GetArcCoordinationCategories()
        {
            return new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
                BuiltInCategory.OST_Rooms,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Railings,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_Sprinklers
            };
        }

        private static IList<BuiltInCategory> GetMepCoordinationCategories()
        {
            return new List<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_Sprinklers
            };
        }

        private static void TryDisableTemporaryHideIsolate(View view)
        {
            try
            {
                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            }
            catch
            {
            }
        }

        private static List<Curve> GetSelectedCurves(Document document, ICollection<ElementId> ids)
        {
            var curves = new List<Curve>();
            foreach (ElementId id in ids)
            {
                Element element = document.GetElement(id);
                if (element is ModelCurve modelCurve && modelCurve.GeometryCurve != null)
                {
                    curves.Add(modelCurve.GeometryCurve);
                    continue;
                }

                if (element is DetailCurve detailCurve && detailCurve.GeometryCurve != null)
                {
                    curves.Add(detailCurve.GeometryCurve);
                    continue;
                }

                if (element is Grid grid && grid.Curve != null)
                {
                    curves.Add(grid.Curve);
                    continue;
                }

                if (element?.Location is LocationCurve locationCurve && locationCurve.Curve != null)
                {
                    curves.Add(locationCurve.Curve);
                }
            }

            return curves;
        }

        private static List<CadCurveCandidate> GetSelectedCurveCandidates(
            Document document,
            ICollection<ElementId> ids,
            MhnkArcToolSettings settings,
            MhnkCadCurvePurpose purpose,
            bool allowPurposeFallback = true)
        {
            var candidates = new List<CadCurveCandidate>();
            if (ids == null)
            {
                return candidates;
            }

            IList<MhnkArcSmartMappingRule> mappingRules = MhnkArcSmartMappingRules.Load().Rules;
            foreach (ElementId id in ids)
            {
                Element element = document.GetElement(id);
                AddElementCurveCandidates(document, element, settings, mappingRules, purpose, candidates);
            }

            if (allowPurposeFallback && candidates.Count == 0 && purpose != MhnkCadCurvePurpose.Any)
            {
                foreach (ElementId id in ids)
                {
                    Element element = document.GetElement(id);
                    AddElementCurveCandidates(document, element, settings, mappingRules, MhnkCadCurvePurpose.Any, candidates);
                }
            }

            return candidates;
        }

        private static void AddElementCurveCandidates(
            Document document,
            Element element,
            MhnkArcToolSettings settings,
            IList<MhnkArcSmartMappingRule> mappingRules,
            MhnkCadCurvePurpose purpose,
            IList<CadCurveCandidate> candidates)
        {
            if (element == null)
            {
                return;
            }

            if (element is ModelCurve modelCurve && modelCurve.GeometryCurve != null)
            {
                AddCurveCandidate(modelCurve.GeometryCurve, "", element.Id, settings, mappingRules, purpose, candidates);
                return;
            }

            if (element is DetailCurve detailCurve && detailCurve.GeometryCurve != null)
            {
                AddCurveCandidate(detailCurve.GeometryCurve, "", element.Id, settings, mappingRules, purpose, candidates);
                return;
            }

            if (element is Grid grid && grid.Curve != null)
            {
                AddCurveCandidate(grid.Curve, "", element.Id, settings, mappingRules, purpose, candidates);
                return;
            }

            if (element?.Location is LocationCurve locationCurve && locationCurve.Curve != null)
            {
                AddCurveCandidate(locationCurve.Curve, "", element.Id, settings, mappingRules, purpose, candidates);
            }

            ImportInstance import = element as ImportInstance;
            if (import == null)
            {
                return;
            }

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            CollectImportCurveCandidates(document, import.get_Geometry(options), import.Id, settings, mappingRules, purpose, candidates);
        }

        private static void CollectImportCurveCandidates(
            Document document,
            GeometryElement geometry,
            ElementId sourceId,
            MhnkArcToolSettings settings,
            IList<MhnkArcSmartMappingRule> mappingRules,
            MhnkCadCurvePurpose purpose,
            IList<CadCurveCandidate> candidates)
        {
            if (geometry == null)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject == null)
                {
                    continue;
                }

                string layerName = GetGeometryLayerName(document, geometryObject);
                Curve curve = geometryObject as Curve;
                if (curve != null)
                {
                    AddCurveCandidate(curve, layerName, sourceId, settings, mappingRules, purpose, candidates);
                    continue;
                }

                PolyLine polyLine = geometryObject as PolyLine;
                if (polyLine != null)
                {
                    AddPolyLineCandidates(polyLine, layerName, sourceId, settings, mappingRules, purpose, candidates);
                    continue;
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance != null)
                {
                    CollectImportCurveCandidates(document, instance.GetInstanceGeometry(), sourceId, settings, mappingRules, purpose, candidates);
                }
            }
        }

        private static void AddPolyLineCandidates(
            PolyLine polyLine,
            string layerName,
            ElementId sourceId,
            MhnkArcToolSettings settings,
            IList<MhnkArcSmartMappingRule> mappingRules,
            MhnkCadCurvePurpose purpose,
            IList<CadCurveCandidate> candidates)
        {
            IList<XYZ> points = polyLine.GetCoordinates();
            for (int i = 0; i < points.Count - 1; i++)
            {
                XYZ start = points[i];
                XYZ end = points[i + 1];
                if (start == null || end == null || start.DistanceTo(end) < ShortCurveTolerance)
                {
                    continue;
                }

                AddCurveCandidate(Line.CreateBound(start, end), layerName, sourceId, settings, mappingRules, purpose, candidates);
            }
        }

        private static void AddCurveCandidate(
            Curve curve,
            string layerName,
            ElementId sourceId,
            MhnkArcToolSettings settings,
            IList<MhnkArcSmartMappingRule> mappingRules,
            MhnkCadCurvePurpose purpose,
            IList<CadCurveCandidate> candidates)
        {
            if (curve == null || !curve.IsBound || curve.Length < ShortCurveTolerance)
            {
                return;
            }

            MhnkArcSmartMappingRule mappingRule = GetCadMappingRule(layerName, mappingRules, purpose);
            if (mappingRule == null && HasAnyCadMappingRuleForLayer(layerName, mappingRules) && purpose != MhnkCadCurvePurpose.Any)
            {
                return;
            }

            if (mappingRule == null && !IsCadCurvePurposeMatch(layerName, settings, purpose))
            {
                return;
            }

            candidates.Add(new CadCurveCandidate(curve, layerName ?? "", sourceId, mappingRule));
        }

        private static string GetGeometryLayerName(Document document, GeometryObject geometryObject)
        {
            if (document == null || geometryObject == null)
            {
                return "";
            }

            try
            {
                GraphicsStyle style = document.GetElement(geometryObject.GraphicsStyleId) as GraphicsStyle;
                return style?.GraphicsStyleCategory?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static MhnkArcSmartMappingRule GetCadMappingRule(string layerName, IList<MhnkArcSmartMappingRule> mappingRules, MhnkCadCurvePurpose purpose)
        {
            if (string.IsNullOrWhiteSpace(layerName) || mappingRules == null)
            {
                return null;
            }

            foreach (MhnkArcSmartMappingRule rule in mappingRules)
            {
                if (rule == null || !rule.Enabled || !IsStrictCadLayerMatch(layerName, MhnkArcSmartMappingRules.SplitKeywords(rule.LayerKeywords)))
                {
                    continue;
                }

                if (purpose == MhnkCadCurvePurpose.Any || MappingRuleMatchesPurpose(rule, purpose))
                {
                    return rule.Normalize();
                }
            }

            return null;
        }

        private static bool HasAnyCadMappingRuleForLayer(string layerName, IList<MhnkArcSmartMappingRule> mappingRules)
        {
            if (string.IsNullOrWhiteSpace(layerName) || mappingRules == null)
            {
                return false;
            }

            return mappingRules.Any(rule =>
                rule != null &&
                rule.Enabled &&
                IsStrictCadLayerMatch(layerName, MhnkArcSmartMappingRules.SplitKeywords(rule.LayerKeywords)));
        }

        private static bool MappingRuleMatchesPurpose(MhnkArcSmartMappingRule rule, MhnkCadCurvePurpose purpose)
        {
            if (rule == null)
            {
                return false;
            }

            string action = MhnkArcSmartMappingRules.NormalizeAction(rule.Action);
            switch (purpose)
            {
                case MhnkCadCurvePurpose.Walls:
                    return string.Equals(action, "Wall", StringComparison.OrdinalIgnoreCase);
                case MhnkCadCurvePurpose.Floors:
                    return string.Equals(action, "Floor", StringComparison.OrdinalIgnoreCase);
                case MhnkCadCurvePurpose.Ceilings:
                    return string.Equals(action, "Ceiling", StringComparison.OrdinalIgnoreCase);
                case MhnkCadCurvePurpose.RoomBoundaries:
                    return string.Equals(action, "RoomBoundary", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(action, "Room", StringComparison.OrdinalIgnoreCase);
                case MhnkCadCurvePurpose.Openings:
                    return string.Equals(action, "Opening", StringComparison.OrdinalIgnoreCase);
                case MhnkCadCurvePurpose.DoorsWindows:
                    return string.Equals(action, "Door", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(action, "Window", StringComparison.OrdinalIgnoreCase);
                case MhnkCadCurvePurpose.Any:
                default:
                    return true;
            }
        }

        private static bool IsStrictCadLayerMatch(string layerName, IList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(layerName) || keywords == null || keywords.Count == 0)
            {
                return false;
            }

            return keywords.Any(keyword => layerName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsCadCurvePurposeMatch(string layerName, MhnkArcToolSettings settings, MhnkCadCurvePurpose purpose)
        {
            if (purpose == MhnkCadCurvePurpose.Any || string.IsNullOrWhiteSpace(layerName))
            {
                return true;
            }

            switch (purpose)
            {
                case MhnkCadCurvePurpose.Walls:
                    return IsCadLayerMatch(layerName, settings.SplitKeywords(settings.WallLayerKeywords));
                case MhnkCadCurvePurpose.Floors:
                    return IsCadLayerMatch(layerName, settings.SplitKeywords(settings.FloorLayerKeywords));
                case MhnkCadCurvePurpose.Ceilings:
                    return IsCadLayerMatch(layerName, settings.SplitKeywords(settings.CeilingLayerKeywords));
                case MhnkCadCurvePurpose.RoomBoundaries:
                    return IsCadLayerMatch(layerName, settings.SplitKeywords(settings.RoomBoundaryLayerKeywords));
                case MhnkCadCurvePurpose.Openings:
                    return IsCadLayerMatch(layerName, settings.SplitKeywords(settings.OpeningLayerKeywords));
                case MhnkCadCurvePurpose.DoorsWindows:
                    return IsCadLayerMatch(layerName, settings.SplitKeywords(settings.DoorLayerKeywords)) ||
                           IsCadLayerMatch(layerName, settings.SplitKeywords(settings.WindowLayerKeywords));
                default:
                    return true;
            }
        }

        private static bool IsCadLayerMatch(string layerName, IList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return true;
            }

            if (keywords == null || keywords.Count == 0)
            {
                return true;
            }

            return keywords.Any(keyword => layerName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IList<CurveLoop> BuildClosedLoopsFromCurves(List<Curve> curves)
        {
            var loops = new List<CurveLoop>();
            var remaining = new List<Curve>(curves ?? new List<Curve>());

            while (remaining.Count >= 3)
            {
                CurveLoop loop = TryBuildOneClosedLoop(remaining);
                if (loop == null)
                {
                    break;
                }

                loops.Add(loop);
            }

            return loops;
        }

        private static CurveLoop TryBuildOneClosedLoop(List<Curve> remaining)
        {
            if (remaining == null || remaining.Count < 3)
            {
                return null;
            }

            var ordered = new List<Curve>();
            Curve first = remaining[0];
            remaining.RemoveAt(0);
            ordered.Add(first);

            XYZ start = first.GetEndPoint(0);
            XYZ end = first.GetEndPoint(1);
            const double endpointTolerance = 0.02;

            while (remaining.Count > 0 && !PointsAlmostEqual(end, start, endpointTolerance))
            {
                int foundIndex = -1;
                bool reverse = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    Curve candidate = remaining[i];
                    if (PointsAlmostEqual(candidate.GetEndPoint(0), end, endpointTolerance))
                    {
                        foundIndex = i;
                        reverse = false;
                        break;
                    }

                    if (PointsAlmostEqual(candidate.GetEndPoint(1), end, endpointTolerance))
                    {
                        foundIndex = i;
                        reverse = true;
                        break;
                    }
                }

                if (foundIndex < 0)
                {
                    break;
                }

                Curve next = remaining[foundIndex];
                remaining.RemoveAt(foundIndex);
                if (reverse)
                {
                    next = next.CreateReversed();
                }

                ordered.Add(next);
                end = next.GetEndPoint(1);
            }

            if (ordered.Count < 3 || !PointsAlmostEqual(end, start, endpointTolerance))
            {
                remaining.InsertRange(0, ordered);
                return null;
            }

            try
            {
                return CurveLoop.Create(ordered);
            }
            catch
            {
                remaining.InsertRange(0, ordered);
                return null;
            }
        }

        private static bool PointsAlmostEqual(XYZ first, XYZ second, double tolerance)
        {
            return first != null && second != null && first.DistanceTo(second) <= tolerance;
        }

        private static Curve ProjectCurveToLevel(Curve curve, double elevation)
        {
            if (curve == null || !curve.IsBound)
            {
                return null;
            }

            if (curve is Line)
            {
                XYZ start = ProjectPointToZ(curve.GetEndPoint(0), elevation);
                XYZ end = ProjectPointToZ(curve.GetEndPoint(1), elevation);
                if (start.DistanceTo(end) < ShortCurveTolerance)
                {
                    return null;
                }

                return Line.CreateBound(start, end);
            }

            if (curve is Arc)
            {
                XYZ start = ProjectPointToZ(curve.GetEndPoint(0), elevation);
                XYZ end = ProjectPointToZ(curve.GetEndPoint(1), elevation);
                XYZ mid = ProjectPointToZ(curve.Evaluate(0.5, true), elevation);
                if (start.DistanceTo(end) < ShortCurveTolerance)
                {
                    return null;
                }

                return Arc.Create(start, end, mid);
            }

            return null;
        }

        private static XYZ ProjectPointToZ(XYZ point, double elevation)
        {
            return new XYZ(point.X, point.Y, elevation);
        }

        private static Level GetActiveOrFirstLevel(Document document, View view)
        {
            Level level = null;
            if (view is ViewPlan viewPlan)
            {
                level = viewPlan.GenLevel;
            }

            return level ?? new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static string CreateUniqueViewName(Document document, string baseName)
        {
            HashSet<string> existing = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }

            return baseName + " " + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static string CreateUniqueGroupTypeName(Document document, string baseName)
        {
            HashSet<string> existing = new FilteredElementCollector(document)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>()
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }

            return baseName + " " + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static Parameter GetRoomBoundingParameter(Element element)
        {
            if (element == null)
            {
                return null;
            }

            Parameter parameter = element.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
            if (parameter != null)
            {
                return parameter;
            }

            foreach (Parameter candidate in element.Parameters)
            {
                if (candidate?.Definition != null &&
                    string.Equals(candidate.Definition.Name, "Room Bounding", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Solid CreateBoxSolid(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return null;
            }

            XYZ min = box.Min;
            XYZ max = box.Max;
            if (min == null || max == null)
            {
                return null;
            }

            double minX = Math.Min(min.X, max.X);
            double minY = Math.Min(min.Y, max.Y);
            double minZ = Math.Min(min.Z, max.Z);
            double maxX = Math.Max(min.X, max.X);
            double maxY = Math.Max(min.Y, max.Y);
            double maxZ = Math.Max(min.Z, max.Z);

            if (maxX - minX < ShortCurveTolerance || maxY - minY < ShortCurveTolerance)
            {
                return null;
            }

            if (maxZ - minZ < ShortCurveTolerance)
            {
                maxZ = minZ + 0.1;
            }

            var loop = new CurveLoop();
            XYZ p1 = new XYZ(minX, minY, minZ);
            XYZ p2 = new XYZ(maxX, minY, minZ);
            XYZ p3 = new XYZ(maxX, maxY, minZ);
            XYZ p4 = new XYZ(minX, maxY, minZ);
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                maxZ - minZ);
        }

        private static bool IsMhnkArcSolid(DirectShape shape)
        {
            return shape != null &&
                   string.Equals(shape.ApplicationId, "MHNK", StringComparison.OrdinalIgnoreCase) &&
                   (shape.ApplicationDataId ?? "").StartsWith("MHNK_ARC_BOUNDING_SOLID_", StringComparison.OrdinalIgnoreCase);
        }

        private static void AccumulateSolidMetrics(Element element, ref int solidCount, ref double volumeFt3, ref double areaFt2)
        {
            if (element == null)
            {
                return;
            }

            Options options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = element.get_Geometry(options);
            AccumulateSolidMetrics(geometry, ref solidCount, ref volumeFt3, ref areaFt2);
        }

        private static void AccumulateSolidMetrics(GeometryElement geometry, ref int solidCount, ref double volumeFt3, ref double areaFt2)
        {
            if (geometry == null)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                if (geometryObject is Solid solid && solid.Volume > 1e-9)
                {
                    solidCount++;
                    volumeFt3 += solid.Volume;
                    areaFt2 += solid.SurfaceArea;
                }
                else if (geometryObject is GeometryInstance instance)
                {
                    AccumulateSolidMetrics(instance.GetInstanceGeometry(), ref solidCount, ref volumeFt3, ref areaFt2);
                }
            }
        }

        private static void ShowResult(string title, string message)
        {
            TaskDialog.Show(title, message ?? "");
        }
    }

    internal static class MhnkArcWorkspaceController
    {
        private static MhnkArcToolsWindow _window;
        private static MhnkArcWorkspaceExternalEventHandler _handler;
        private static ExternalEvent _externalEvent;

        public static void Show(
            IList<MhnkArcCommandOption> options,
            string initialCategory,
            ExternalCommandData commandData)
        {
            if (_handler == null)
            {
                _handler = new MhnkArcWorkspaceExternalEventHandler();
                _externalEvent = ExternalEvent.Create(_handler);
            }

            _handler.SetCommandData(commandData);
            MhnkArcWorkspaceSnapshot snapshot = CaptureWorkspaceSnapshot(commandData);

            if (_window == null || !_window.IsVisible)
            {
                _window = new MhnkArcToolsWindow(
                    options,
                    initialCategory,
                    commandData.Application.MainWindowHandle,
                    (option, sourceMode) => RequestRun(option, sourceMode),
                    sourceMode => RequestRefresh(sourceMode),
                    snapshot);
                _handler.SetWindow(_window);
                _window.Closed += (_, __) =>
                {
                    _handler.SetWindow(null);
                    _window = null;
                };
                _window.Show();
            }
            else
            {
                _window.UpdateSnapshot(snapshot);
                _window.SelectCategory(initialCategory);
            }

            if (_window.WindowState == System.Windows.WindowState.Minimized)
            {
                _window.WindowState = System.Windows.WindowState.Normal;
            }

            _window.Activate();
        }

        private static void RequestRun(MhnkArcCommandOption option, MhnkArcSourceMode sourceMode)
        {
            _handler.RequestRun(option, sourceMode);
            _externalEvent.Raise();
        }

        private static void RequestRefresh(MhnkArcSourceMode sourceMode)
        {
            _handler.RequestRefresh(sourceMode);
            _externalEvent.Raise();
        }

        private static MhnkArcWorkspaceSnapshot CaptureWorkspaceSnapshot(ExternalCommandData commandData)
        {
            try
            {
                if (commandData?.Application?.ActiveUIDocument == null)
                {
                    return null;
                }

                return MhnkArcWorkspaceSnapshot.Capture(new MhnkArcContext(commandData));
            }
            catch (Exception ex)
            {
                MhnkLogger.Warn("ARC workspace snapshot failed: " + ex.Message);
                return null;
            }
        }
    }

    internal sealed class MhnkArcWorkspaceExternalEventHandler : IExternalEventHandler
    {
        private MhnkArcToolsWindow _window;
        private ExternalCommandData _commandData;
        private MhnkArcCommandOption _pendingOption;
        private MhnkArcSourceMode _pendingSourceMode = MhnkArcSourceMode.FreeSelect;
        private bool _pendingRefresh;

        public void SetWindow(MhnkArcToolsWindow window)
        {
            _window = window;
        }

        public void SetCommandData(ExternalCommandData commandData)
        {
            _commandData = commandData;
        }

        public void RequestRun(MhnkArcCommandOption option, MhnkArcSourceMode sourceMode)
        {
            _pendingOption = option;
            _pendingSourceMode = sourceMode;
            _pendingRefresh = false;
        }

        public void RequestRefresh(MhnkArcSourceMode sourceMode)
        {
            _pendingOption = null;
            _pendingSourceMode = sourceMode;
            _pendingRefresh = true;
        }

        public string GetName()
        {
            return "MHNK ARC Workspace Handler";
        }

        public void Execute(UIApplication application)
        {
            MhnkArcCommandOption option = _pendingOption;
            MhnkArcSourceMode sourceMode = _pendingSourceMode;
            bool refresh = _pendingRefresh;
            _pendingOption = null;
            _pendingSourceMode = MhnkArcSourceMode.FreeSelect;
            _pendingRefresh = false;

            try
            {
                if (refresh)
                {
                    if (application?.ActiveUIDocument == null)
                    {
                        TaskDialog.Show("MHNK ARC Workspace", "Open a Revit model before retrieving workspace data.");
                        return;
                    }

                    var refreshContext = new MhnkArcContext(application, _commandData, sourceMode, _window?.SelectedOption);
                    _window?.UpdateSnapshot(MhnkArcWorkspaceSnapshot.Capture(refreshContext));
                    return;
                }

                if (option == null)
                {
                    return;
                }

                if (application?.ActiveUIDocument == null)
                {
                    TaskDialog.Show("MHNK " + option.Category, "Open a Revit model before running this tool.");
                    return;
                }

                var context = new MhnkArcContext(application, _commandData, sourceMode, option);
                MhnkLogger.Info("Running ARC " + option.Category + ": " + option.Title);
                MhnkArcCommandRuntime.Execute(
                    context,
                    option.Category,
                    option,
                    application.MainWindowHandle);
            }
            catch (Exception ex)
            {
                string category = option == null ? "Workspace" : option.Category;
                MhnkLogger.Error("ARC " + category + " failed.", ex);
                TaskDialog.Show("MHNK " + category, ex.Message);
            }
            finally
            {
                _window?.CompleteRunRequest();
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OpenMhnkFilterCommand : MhnkArchitectureToolCommand
    {
        protected override string ToolName => "Filter";
        protected override string MainInstruction => "ARC Filter";

        protected override IList<MhnkArcCommandOption> BuildOptions(MhnkArcContext context)
        {
            return BuildFullOptions();
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OpenMhnkCreationCommand : MhnkArchitectureToolCommand
    {
        protected override string ToolName => "Creation";
        protected override string MainInstruction => "ARC Creation";

        protected override IList<MhnkArcCommandOption> BuildOptions(MhnkArcContext context)
        {
            return BuildFullOptions();
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OpenMhnkEditionCommand : MhnkArchitectureToolCommand
    {
        protected override string ToolName => "Edition";
        protected override string MainInstruction => "ARC Edition";

        protected override IList<MhnkArcCommandOption> BuildOptions(MhnkArcContext context)
        {
            return BuildFullOptions();
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OpenMhnkSolidsCommand : MhnkArchitectureToolCommand
    {
        protected override string ToolName => "Solids";
        protected override string MainInstruction => "ARC Solids";

        protected override IList<MhnkArcCommandOption> BuildOptions(MhnkArcContext context)
        {
            return BuildFullOptions();
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OpenMhnkXpressCommand : MhnkArchitectureToolCommand
    {
        protected override string ToolName => "Xpress";
        protected override string MainInstruction => "ARC Xpress";

        protected override IList<MhnkArcCommandOption> BuildOptions(MhnkArcContext context)
        {
            return BuildFullOptions();
        }
    }

    public sealed class MhnkArcContext
    {
        public MhnkArcContext(ExternalCommandData commandData)
            : this(commandData?.Application, commandData, MhnkArcSourceMode.FreeSelect, null)
        {
        }

        internal MhnkArcContext(
            UIApplication uiApplication,
            ExternalCommandData commandData,
            MhnkArcSourceMode sourceMode = MhnkArcSourceMode.FreeSelect,
            MhnkArcCommandOption commandOption = null)
        {
            CommandData = commandData;
            UiApplication = uiApplication ?? throw new ArgumentNullException(nameof(uiApplication));
            UiDocument = UiApplication.ActiveUIDocument;
            if (UiDocument == null)
            {
                throw new InvalidOperationException("Open a Revit model before running an ARC tool.");
            }

            Document = UiDocument.Document;
            ActiveView = Document.ActiveView;
            SourceMode = sourceMode;
            CommandOption = commandOption;
        }

        public ExternalCommandData CommandData { get; }
        public UIApplication UiApplication { get; }
        public UIDocument UiDocument { get; }
        public Document Document { get; }
        public View ActiveView { get; }
        internal MhnkArcSourceMode SourceMode { get; }
        internal MhnkArcCommandOption CommandOption { get; }
        internal string SourceModeName => GetSourceModeName(SourceMode);

        internal static string GetSourceModeName(MhnkArcSourceMode sourceMode)
        {
            switch (sourceMode)
            {
                case MhnkArcSourceMode.ByLayer:
                    return "By Layer";
                case MhnkArcSourceMode.Category:
                    return "Category";
                case MhnkArcSourceMode.All:
                    return "All";
                case MhnkArcSourceMode.FreeSelect:
                default:
                    return "Free Select";
            }
        }
    }

    internal sealed class MhnkArcWorkspaceSnapshot
    {
        public bool HasData { get; private set; }
        public string DocumentTitle { get; private set; }
        public string ActiveViewName { get; private set; }
        public string ModelLabel
        {
            get
            {
                string document = string.IsNullOrWhiteSpace(DocumentTitle) ? "Untitled model" : DocumentTitle;
                string view = string.IsNullOrWhiteSpace(ActiveViewName) ? "active view" : ActiveViewName;
                return document + " / " + view;
            }
        }

        public int SelectionCount { get; private set; }
        public int VisibleElementCount { get; private set; }
        public int WarningCount { get; private set; }
        public int VisibleRoomCount { get; private set; }
        public int AllRoomCount { get; private set; }
        public int VisibleCadImportCount { get; private set; }
        public int AllCadImportCount { get; private set; }
        public int LevelCount { get; private set; }
        public int WallTypeCount { get; private set; }
        public int FloorTypeCount { get; private set; }
        public int CeilingTypeCount { get; private set; }
        public int DoorTypeCount { get; private set; }
        public int WindowTypeCount { get; private set; }
        public int VisibleWallCount { get; private set; }
        public int VisibleFloorCount { get; private set; }
        public int VisibleCeilingCount { get; private set; }
        public int VisibleDoorCount { get; private set; }
        public int VisibleWindowCount { get; private set; }
        public IList<MhnkArcWorkspaceCategoryCount> TopCategories { get; private set; } =
            new List<MhnkArcWorkspaceCategoryCount>();
        public MhnkArcPreviewResult Preview { get; private set; }

        public string TopCategorySummary
        {
            get
            {
                if (TopCategories == null || TopCategories.Count == 0)
                {
                    return "No visible model categories were found in the active view.";
                }

                return string.Join(", ", TopCategories.Select(x => x.Name + " " + x.Count.ToString(CultureInfo.InvariantCulture)));
            }
        }

        public static MhnkArcWorkspaceSnapshot Capture(MhnkArcContext context)
        {
            var snapshot = new MhnkArcWorkspaceSnapshot();
            if (context == null || context.Document == null)
            {
                return snapshot;
            }

            Document doc = context.Document;
            View view = context.ActiveView;
            snapshot.HasData = true;
            snapshot.DocumentTitle = doc.Title ?? "";
            snapshot.ActiveViewName = view?.Name ?? "";
            snapshot.SelectionCount = SafeCount(() => context.UiDocument.Selection.GetElementIds().Count);
            snapshot.VisibleElementCount = SafeCount(() => new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .GetElementCount());
            snapshot.WarningCount = SafeCount(() => doc.GetWarnings().Count);
            snapshot.VisibleRoomCount = SafeCount(() => CountRooms(doc, view, true));
            snapshot.AllRoomCount = SafeCount(() => CountRooms(doc, null, false));
            snapshot.VisibleCadImportCount = SafeCount(() => CountElements(doc, view, typeof(ImportInstance), null));
            snapshot.AllCadImportCount = SafeCount(() => CountElements(doc, null, typeof(ImportInstance), null));
            snapshot.LevelCount = SafeCount(() => new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount());
            snapshot.WallTypeCount = SafeCount(() => new FilteredElementCollector(doc).OfClass(typeof(WallType)).GetElementCount());
            snapshot.FloorTypeCount = SafeCount(() => new FilteredElementCollector(doc).OfClass(typeof(FloorType)).GetElementCount());
            snapshot.CeilingTypeCount = SafeCount(() => new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).GetElementCount());
            snapshot.DoorTypeCount = SafeCount(() => CountFamilySymbols(doc, BuiltInCategory.OST_Doors));
            snapshot.WindowTypeCount = SafeCount(() => CountFamilySymbols(doc, BuiltInCategory.OST_Windows));
            snapshot.VisibleWallCount = SafeCount(() => CountElements(doc, view, null, BuiltInCategory.OST_Walls));
            snapshot.VisibleFloorCount = SafeCount(() => CountElements(doc, view, null, BuiltInCategory.OST_Floors));
            snapshot.VisibleCeilingCount = SafeCount(() => CountElements(doc, view, null, BuiltInCategory.OST_Ceilings));
            snapshot.VisibleDoorCount = SafeCount(() => CountElements(doc, view, null, BuiltInCategory.OST_Doors));
            snapshot.VisibleWindowCount = SafeCount(() => CountElements(doc, view, null, BuiltInCategory.OST_Windows));
            snapshot.TopCategories = CaptureTopCategories(doc, view);
            snapshot.Preview = MhnkArcPreviewService.Retrieve(context);
            return snapshot;
        }

        private static int CountRooms(Document doc, View view, bool activeViewOnly)
        {
            FilteredElementCollector collector = activeViewOnly && view != null
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            return collector
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Count(room => room.Area > 0.0 || room.Location != null);
        }

        private static int CountFamilySymbols(Document doc, BuiltInCategory category)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .GetElementCount();
        }

        private static int CountElements(Document doc, View view, Type elementClass, BuiltInCategory? category)
        {
            FilteredElementCollector collector = view != null
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            if (elementClass != null)
            {
                collector = collector.OfClass(elementClass);
            }

            if (category.HasValue)
            {
                collector = collector.OfCategory(category.Value);
            }

            return collector.WhereElementIsNotElementType().GetElementCount();
        }

        private static IList<MhnkArcWorkspaceCategoryCount> CaptureTopCategories(Document doc, View view)
        {
            try
            {
                if (doc == null || view == null)
                {
                    return new List<MhnkArcWorkspaceCategoryCount>();
                }

                return new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e.Category != null && !string.IsNullOrWhiteSpace(e.Category.Name))
                    .GroupBy(e => e.Category.Name)
                    .Select(g => new MhnkArcWorkspaceCategoryCount(g.Key, g.Count()))
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Name)
                    .Take(6)
                    .ToList();
            }
            catch
            {
                return new List<MhnkArcWorkspaceCategoryCount>();
            }
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

    internal sealed class MhnkArcWorkspaceCategoryCount
    {
        public MhnkArcWorkspaceCategoryCount(string name, int count)
        {
            Name = name ?? "";
            Count = count;
        }

        public string Name { get; }
        public int Count { get; }
    }

    public sealed class MhnkArcCommandOption
    {
        public MhnkArcCommandOption(string title, string summary, Func<MhnkArcContext, Result> run)
            : this("General", title, summary, run, true, "Ready")
        {
        }

        public MhnkArcCommandOption(
            string category,
            string title,
            string summary,
            Func<MhnkArcContext, Result> run,
            bool isReady,
            string status)
            : this(category, title, summary, run, isReady, status, null)
        {
        }

        private MhnkArcCommandOption(
            string category,
            string title,
            string summary,
            Func<MhnkArcContext, Result> run,
            bool isReady,
            string status,
            MhnkArcToolMetadata metadata)
        {
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
            Title = title ?? "";
            Summary = summary ?? "";
            Run = run ?? (_ => Result.Cancelled);
            IsReady = isReady;
            Status = string.IsNullOrWhiteSpace(status) ? (isReady ? "Ready" : "Next") : status.Trim();
            Metadata = metadata ?? MhnkArcToolMetadata.Create(Category, Title, Summary, IsReady, Status);
        }

        public string Category { get; }
        public string Title { get; }
        public string Summary { get; }
        public Func<MhnkArcContext, Result> Run { get; }
        public bool IsReady { get; }
        public string Status { get; }
        internal MhnkArcToolMetadata Metadata { get; }

        public string DisplayText
        {
            get { return Title + "    [" + Status + "]"; }
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}
