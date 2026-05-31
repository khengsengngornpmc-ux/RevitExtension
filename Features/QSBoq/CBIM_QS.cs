using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CamboBIM.Revit2024.Addin
{
    internal static class CBIM_QS
    {
        private const double FormworkHalfThicknessFt = 0.00328084; // 1 mm half-thickness (~2 mm total)
        private const string UnifiedLevelParamName = "CBIM_StructuralPlan";
        private const string UnifiedBuildingLevelParamName = "CBIM_BuildingLevel";
        private const string SoilExcavationVolumeParamName = "CBIM_SoilExcavationVolume";
        private const string SoilBackfilledVolumeParamName = "CBIM_SoilBackfilledVolume";
        private const int BeamStrutPersistedStageLimit = 15;
        private const int ColumnStrutPersistedStageLimit = 15;
        private const int SlabStrutPersistedStageLimit = 15;
        private const int WallEdgePersistedStageLimit = 15;
        private const int FoundationSidePersistedStageLimit = 15;
        private static readonly BuiltInCategory[] QsHostCategories =
        {
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Stairs
        };
        private static readonly BuiltInCategory[] QsMeasuredCategories =
        {
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Ceilings
        };

        public static void Calculate(UIApplication uiapp, UIDocument uidoc, Document doc, CadToModelRequest request, CamboBIMWindow window)
        {
            if (uiapp == null || uidoc == null || doc == null || request == null) return;

            string sharedParamFile = request.QsSharedParameterFilePath;
            if (string.IsNullOrWhiteSpace(sharedParamFile))
            {
                sharedParamFile = GetDefaultQsSharedParameterPath();
            }
            else
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                sharedParamFile = SharedParameterPathResolver.ResolveWritableSharedParameterPath(sharedParamFile, baseDir);
            }

            string groupName = string.IsNullOrWhiteSpace(request.QsSharedParameterGroupName)
                ? "CBIM-QS"
                : request.QsSharedParameterGroupName.Trim();

            // Always bind all QS shared parameters to all QS categories,
            // so they are always available in Schedules/Quantities fields.
            List<QsSharedParamSpec> specs = GetQsSharedParameterSpecs(
                includeBeam: true,
                includeColumn: true,
                includeWall: true,
                includeFloor: true,
                includeStair: true,
                includeFoundation: true);

            List<BuiltInCategory> bindCats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_SpecialityEquipment
            };

            if (!EnsureSharedParameters(uiapp.Application, doc, sharedParamFile, groupName, specs, bindCats))
            {
                window?.ShowStatus("Failed to create/bind QS shared parameters.");
                return;
            }

            int updated = 0;
            int candidateCount = 0;
            int processedCount = 0;
            List<Element> beams = CollectStructuralFramingElements(uidoc, doc, request.QsScope);
            List<Element> columns = CollectStructuralColumnElements(uidoc, doc, request.QsScope);
            List<Element> walls = CollectStructuralWallElements(uidoc, doc, request.QsScope);
            List<Element> floors = CollectStructuralFloorElements(uidoc, doc, request.QsScope);
            List<Element> foundations = CollectStructuralFoundationElements(uidoc, doc, request.QsScope);
            List<Element> stairs = CollectStairElements(uidoc, doc, request.QsScope);
            List<Element> generics = CollectGenericModelElements(uidoc, doc, request.QsScope);
            List<Element> ceilings = CollectCeilingElements(uidoc, doc, request.QsScope);
            Dictionary<ElementId, string> structuralPlanMap = BuildStructuralPlanNameMap(doc);
            QsMeasurementRuntimeRules measurementRules = QsMeasurementRuntimeRules.FromRequest(request);
            List<Element> lintels = CollectLintelElements(beams, generics);
            List<Element> dropPanels = CollectDropPanelElements(floors, generics);
            List<Element> eaves = CollectEaveElements(floors, generics);
            List<Element> kerbs = CollectKerbElements(floors, generics);
            List<Element> otherConcretes = CollectOtherConcreteElements(floors, generics, kerbs, dropPanels, eaves);
            List<QsFinishMeasurementCandidate> finishCandidates = CollectFinishMeasurementCandidates(walls, floors, ceilings, generics, measurementRules);
            HashSet<long> nonStructuralIds = BuildElementIdSet(lintels, dropPanels, eaves, kerbs, otherConcretes, finishCandidates.Select(c => c.Element).ToList());
            List<Element> structuralBeams = beams
                .Where(e => !ContainsElementId(nonStructuralIds, e))
                .ToList();
            List<Element> structuralWalls = walls
                .Where(e => !ContainsElementId(nonStructuralIds, e))
                .ToList();
            List<Element> structuralFloors = floors
                .Where(e => !ContainsElementId(nonStructuralIds, e))
                .ToList();
            Dictionary<long, double> rebarWeightByHost = BuildRebarWeightByHost(doc);

            if (!request.QsIncludeStructuralFraming &&
                !request.QsIncludeStructuralColumn &&
                !request.QsIncludeStructuralWall &&
                !request.QsIncludeStructuralFloor &&
                !request.QsIncludeStructuralStair &&
                !request.QsIncludeFoundation &&
                !measurementRules.LintelCalculationEnabled &&
                !measurementRules.DropPanelCalculationEnabled &&
                !measurementRules.EaveCalculationEnabled &&
                !measurementRules.KerbCalculationEnabled &&
                !measurementRules.OtherConcreteCalculationEnabled &&
                !measurementRules.FinishCalculationEnabled)
            {
                window?.ShowStatus("Select at least one QS category.");
                return;
            }

            if (request.QsIncludeStructuralFraming) candidateCount += structuralBeams.Count;
            if (request.QsIncludeStructuralColumn) candidateCount += columns.Count;
            if (request.QsIncludeStructuralWall) candidateCount += structuralWalls.Count;
            if (request.QsIncludeStructuralFloor) candidateCount += structuralFloors.Count;
            if (request.QsIncludeStructuralStair) candidateCount += stairs.Count;
            if (request.QsIncludeFoundation) candidateCount += foundations.Count;
            if (measurementRules.LintelCalculationEnabled) candidateCount += lintels.Count;
            if (measurementRules.DropPanelCalculationEnabled) candidateCount += dropPanels.Count;
            if (measurementRules.EaveCalculationEnabled) candidateCount += eaves.Count;
            if (measurementRules.KerbCalculationEnabled) candidateCount += kerbs.Count;
            if (measurementRules.OtherConcreteCalculationEnabled) candidateCount += otherConcretes.Count;
            candidateCount += finishCandidates.Count;

            if (candidateCount == 0)
            {
                string emptyMessage = "QS Formwork: updated 0 element(s). Candidates in scope: 0. Check categories and scope.";
                window?.UpdateQsSummary(emptyMessage);
                return;
            }

            void UpdateProgress(string title)
            {
                processedCount++;
                window?.UpdateProgress(processedCount, candidateCount, title + $" ({processedCount}/{candidateCount})");
            }

            window?.BeginProgress("CALCULATE FORMWORK AREA", candidateCount, "Preparing...");

            try
            {
                using (Transaction t = new Transaction(doc, "CamboBIM - QS Formwork"))
                {
                    t.Start();

                    if (request.QsCreateFormworkShape)
                    {
                        ClearExistingFormworkShapes(doc);
                    }

                    if (request.QsIncludeStructuralFraming)
                    {
                        foreach (Element element in structuralBeams)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            if (element is FamilyInstance instance)
                            {
                                bool changed =
                                    WriteBeamQsParameters(doc, instance, measurementRules, foundations, structuralBeams, columns, structuralWalls, structuralFloors, generics, request.QsCreateFormworkShape);
                                changed |= SetUnifiedStructuralPlanParam(doc, instance, structuralPlanMap);
                                if (changed)
                                {
                                    updated++;
                                }
                            }

                            UpdateProgress("Structural Framing");
                        }
                    }

                    if (request.QsIncludeStructuralColumn)
                    {
                        foreach (Element element in columns)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteColumnQsParameters(
                                doc,
                                element,
                                foundations,
                                beams,
                                columns,
                                structuralWalls,
                                structuralFloors,
                                generics,
                                measurementRules,
                                request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Structural Column");
                        }
                    }

                    if (request.QsIncludeStructuralWall)
                    {
                        foreach (Element element in structuralWalls)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteWallQsParameters(
                                doc,
                                element,
                                measurementRules,
                                foundations,
                                beams,
                                columns,
                                structuralWalls,
                                structuralFloors,
                                generics,
                                request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Structural Wall");
                        }
                    }

                    if (measurementRules.LintelCalculationEnabled)
                    {
                        foreach (Element element in lintels)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteLintelQsParameters(doc, element, measurementRules, structuralWalls, request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Lintel");
                        }
                    }

                    if (request.QsIncludeStructuralFloor)
                    {
                        foreach (Element element in structuralFloors)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteFloorQsParameters(
                                doc,
                                element,
                                measurementRules,
                                foundations,
                                beams,
                                columns,
                                structuralWalls,
                                structuralFloors,
                                stairs,
                                generics,
                                request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Structural Floor");
                        }
                    }

                    if (request.QsIncludeFoundation)
                    {
                        foreach (Element element in foundations)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteFoundationQsParameters(doc, element, measurementRules, foundations, beams, columns, structuralWalls, structuralFloors, generics, request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Foundation");
                        }
                    }

                    if (request.QsIncludeStructuralStair)
                    {
                        foreach (Element stair in stairs)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteStairQsParameters(
                                doc,
                                stair,
                                measurementRules,
                                foundations,
                                beams,
                                columns,
                                structuralWalls,
                                structuralFloors,
                                generics,
                                request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, stair, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Structural Stair");
                        }
                    }

                    if (measurementRules.DropPanelCalculationEnabled)
                    {
                        foreach (Element element in dropPanels)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteDropPanelQsParameters(doc, element, measurementRules, structuralFloors, request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Drop Panel");
                        }
                    }

                    if (measurementRules.EaveCalculationEnabled)
                    {
                        foreach (Element element in eaves)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteEaveQsParameters(doc, element, measurementRules, request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Eave");
                        }
                    }

                    WriteRebarWeightParameters(
                        rebarWeightByHost,
                        structuralBeams,
                        columns,
                        structuralWalls,
                        structuralFloors,
                        foundations,
                        stairs);

                    if (measurementRules.KerbCalculationEnabled)
                    {
                        foreach (Element element in kerbs)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteKerbQsParameters(
                                doc,
                                element,
                                measurementRules,
                                structuralWalls,
                                columns,
                                request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Kerb");
                        }
                    }

                    if (measurementRules.OtherConcreteCalculationEnabled)
                    {
                        foreach (Element element in otherConcretes)
                        {
                            if (window?.IsProgressCancellationRequested() == true)
                            {
                                throw new System.OperationCanceledException("Cancelled by user.");
                            }

                            bool changed = WriteOtherConcreteQsParameters(
                                doc,
                                element,
                                measurementRules,
                                foundations,
                                beams,
                                columns,
                                structuralWalls,
                                structuralFloors,
                                request.QsCreateFormworkShape);
                            changed |= SetUnifiedStructuralPlanParam(doc, element, structuralPlanMap);
                            if (changed)
                            {
                                updated++;
                            }

                            UpdateProgress("Other Concrete");
                        }
                    }

                    foreach (QsFinishMeasurementCandidate candidate in finishCandidates)
                    {
                        if (window?.IsProgressCancellationRequested() == true)
                        {
                            throw new System.OperationCanceledException("Cancelled by user.");
                        }

                        bool changed = WriteFinishQsParameters(doc, candidate, measurementRules);
                        changed |= SetUnifiedStructuralPlanParam(doc, candidate.Element, structuralPlanMap);
                        if (changed)
                        {
                            updated++;
                        }

                        UpdateProgress(candidate.ProgressTitle);
                    }

                    t.Commit();
                }

                string message = updated > 0
                    ? $"QS Formwork: updated {updated} element(s)."
                    : $"QS Formwork: updated 0 element(s). Candidates in scope: {candidateCount}. Check categories and scope.";
                window?.UpdateQsSummary(message);
                // Keep BOQ tab and optional CSV auto-sync aligned with latest Generate result.
                CBIM_BOQ.BuildTable(uidoc, doc, request, window);
            }
            finally
            {
                window?.EndProgress();
            }
        }

        public static void HideFormworkShapesInActiveView(UIDocument uidoc, Document doc, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null)
            {
                return;
            }

            View view = doc.ActiveView;
            if (view == null)
            {
                window?.ShowStatus("No active view.");
                return;
            }

            List<ElementId> ids = CollectVisibleFormworkShapeIdsInView(doc, view);

            if (ids.Count == 0)
            {
                window?.ShowStatus("No formwork shapes found in active view.");
                return;
            }

            using (Transaction t = new Transaction(doc, "CamboBIM - Hide Formwork Shapes"))
            {
                t.Start();
                view.HideElements(ids);
                t.Commit();
            }

            window?.ShowStatus($"Hidden {ids.Count} formwork shape(s) in active view.");
        }

        public static void ToggleFormworkShapesInActiveView(UIDocument uidoc, Document doc, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null)
            {
                return;
            }

            View view = doc.ActiveView;
            if (view == null)
            {
                window?.ShowStatus("No active view.");
                return;
            }

            List<ElementId> visibleFormwork = CollectVisibleFormworkShapeIdsInView(doc, view);
            if (visibleFormwork.Count > 0)
            {
                using (Transaction t = new Transaction(doc, "CamboBIM - Hide Formwork Shapes"))
                {
                    t.Start();
                    view.HideElements(visibleFormwork);
                    t.Commit();
                }

                window?.ShowStatus($"Hidden {visibleFormwork.Count} visible formwork shape(s) in active view.");
                return;
            }

            List<ElementId> hiddenFormwork = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(IsCbimFormworkShape)
                .Where(e => e.IsHidden(view))
                .Select(e => e.Id)
                .ToList();

            if (hiddenFormwork.Count > 0)
            {
                using (Transaction t = new Transaction(doc, "CamboBIM - Show Formwork Shapes"))
                {
                    t.Start();
                    view.UnhideElements(hiddenFormwork);
                    t.Commit();
                }

                window?.ShowStatus($"Shown {hiddenFormwork.Count} formwork shape(s) in active view.");
                return;
            }

            HideFormworkShapesInActiveView(uidoc, doc, window);
        }

        private static List<ElementId> CollectVisibleFormworkShapeIdsInView(Document doc, View view)
        {
            if (doc == null || view == null) return new List<ElementId>();
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .WhereElementIsNotElementType()
                .Where(IsCbimFormworkShape)
                .Where(e => !e.IsHidden(view))
                .Select(e => e.Id)
                .ToList();
        }

        public static void DeleteFormworkShapes(Document doc, CamboBIMWindow window)
        {
            if (doc == null) return;

            int deleted;
            using (Transaction t = new Transaction(doc, "CamboBIM - Delete Formwork Shapes"))
            {
                t.Start();
                deleted = ClearExistingFormworkShapes(doc);
                t.Commit();
            }

            window?.ShowStatus(deleted > 0
                ? $"Deleted {deleted} formwork shape(s)."
                : "No formwork shapes found to delete.");
        }

        public static void ExportCsv(UIDocument uidoc, Document doc, CadToModelRequest request, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null || request == null) return;

            string path = request.QsExportCsvPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                window?.ShowStatus("CSV path is empty.");
                return;
            }

            var rows = new List<(string Category, Element Element)>();
            List<Element> walls = CollectStructuralWallElements(uidoc, doc, request.QsScope);
            List<Element> floors = CollectStructuralFloorElements(uidoc, doc, request.QsScope);
            List<Element> generics = CollectGenericModelElements(uidoc, doc, request.QsScope);
            List<Element> ceilings = CollectCeilingElements(uidoc, doc, request.QsScope);
            QsMeasurementRuntimeRules measurementRules = QsMeasurementRuntimeRules.FromRequest(request);
            List<Element> kerbs = CollectKerbElements(floors, generics);
            List<Element> otherConcretes = CollectOtherConcreteElements(floors, generics, kerbs);
            List<QsFinishMeasurementCandidate> finishCandidates = CollectFinishMeasurementCandidates(walls, floors, ceilings, generics, measurementRules);
            HashSet<long> nonStructuralIds = BuildElementIdSet(kerbs, otherConcretes, finishCandidates.Select(c => c.Element).ToList());
            if (request.QsIncludeStructuralFraming)
            {
                rows.AddRange(CollectStructuralFramingElements(uidoc, doc, request.QsScope).Select(e => ("Structural Framing", e)));
            }
            if (request.QsIncludeStructuralColumn)
            {
                rows.AddRange(CollectStructuralColumnElements(uidoc, doc, request.QsScope).Select(e => ("Structural Column", e)));
            }
            if (request.QsIncludeStructuralWall)
            {
                rows.AddRange(walls.Where(e => !ContainsElementId(nonStructuralIds, e)).Select(e => ("Structural Wall", e)));
            }
            if (request.QsIncludeStructuralFloor)
            {
                rows.AddRange(floors.Where(e => !ContainsElementId(nonStructuralIds, e)).Select(e => ("Structural Floor", e)));
            }
            if (request.QsIncludeStructuralStair)
            {
                rows.AddRange(CollectStairElements(uidoc, doc, request.QsScope).Select(e => ("Structural Stair", e)));
            }
            if (request.QsIncludeFoundation)
            {
                rows.AddRange(CollectStructuralFoundationElements(uidoc, doc, request.QsScope).Select(e => ("Structural Foundation", e)));
            }
            if (measurementRules.KerbCalculationEnabled)
            {
                rows.AddRange(kerbs.Select(e => ("Kerb", e)));
            }
            if (measurementRules.OtherConcreteCalculationEnabled)
            {
                rows.AddRange(otherConcretes.Select(e => ("Other Concrete", e)));
            }
            rows.AddRange(finishCandidates.Select(c => (c.BoqLabel, c.Element)));

            if (rows.Count == 0)
            {
                window?.ShowStatus("No elements in scope to export.");
                return;
            }

            string[] paramColumns = GetQsCsvParameterColumns();
            int total = rows.Count;
            int processed = 0;
            // Keep UI responsive for large exports by throttling progress updates.
            int updateStep = Math.Max(50, total / 100);

            window?.BeginProgress("EXPORT QS CSV", total, "Preparing CSV...");
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
                {
                    var header = new List<string> { "Category", "ElementId", "TypeName", "Level" };
                    header.AddRange(paramColumns);
                    writer.WriteLine(string.Join(",", header.Select(EscapeCsv)));

                    foreach ((string category, Element element) in rows)
                    {
                        if (window?.IsProgressCancellationRequested() == true)
                        {
                            throw new System.OperationCanceledException("Cancelled by user.");
                        }

                        var line = new List<string>
                        {
                            category,
                            element?.Id?.Value.ToString() ?? "",
                            GetElementTypeName(doc, element),
                            GetElementLevelName(doc, element)
                        };

                        foreach (string paramName in paramColumns)
                        {
                            line.Add(GetCsvParameterValue(element, paramName));
                        }

                        writer.WriteLine(string.Join(",", line.Select(EscapeCsv)));

                        processed++;
                        if (processed == total || processed % updateStep == 0)
                        {
                            window?.UpdateProgress(processed, total, $"Exporting row {processed}/{total}");
                        }
                    }
                }

                window?.UpdateQsSummary($"QS CSV exported: {total} row(s) to {path}");
            }
            finally
            {
                window?.EndProgress();
            }
        }

        public static void ViewExpression(UIDocument uidoc, Document doc, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null) return;

            if (!TryResolveExpressionHost(uidoc, doc, window, out Element element, out string selectionNote, out string error))
            {
                window?.ShowStatus(error);
                return;
            }

            List<QsExpressionRow> rows = BuildQsExpressionRows(doc, element, selectionNote);
            if (rows.Count == 0)
            {
                window?.ShowStatus("No QS expression found. Run Calculate first, then select one measured element or formwork shape.");
                return;
            }

            ShowQsExpressionWindow(window, uidoc, doc, element, rows, selectionNote);
            window?.ShowStatus("QS expression displayed for element " + (element?.Id?.Value.ToString(CultureInfo.InvariantCulture) ?? ""));
        }

        private static bool TryResolveExpressionHost(
            UIDocument uidoc,
            Document doc,
            CamboBIMWindow window,
            out Element element,
            out string selectionNote,
            out string error)
        {
            element = null;
            selectionNote = "";
            error = "";

            ICollection<ElementId> selectedIds = uidoc?.Selection?.GetElementIds();
            if (selectedIds == null || selectedIds.Count != 1)
            {
                if (!TryPickExpressionElement(uidoc, doc, window, out Element picked, out error))
                {
                    return false;
                }

                selectedIds = new List<ElementId> { picked.Id };
            }

            Element selected = doc.GetElement(selectedIds.First());
            if (selected == null)
            {
                error = "Selected element was not found.";
                return false;
            }

            if (IsCbimFormworkShape(selected))
            {
                string hostIdText = GetCsvParameterValue(selected, "CBIM.FWK.ElementId");
                if (long.TryParse(hostIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long hostId) && hostId > 0)
                {
                    Element host = doc.GetElement(new ElementId(hostId));
                    if (host != null)
                    {
                        element = host;
                        selectionNote = "Selected 3D formwork shape " +
                            selected.Id.Value.ToString(CultureInfo.InvariantCulture) +
                            "; showing host element " +
                            host.Id.Value.ToString(CultureInfo.InvariantCulture) + ".";
                        return true;
                    }
                }

                error = "Selected formwork shape has no valid CBIM.FWK.ElementId host link.";
                return false;
            }

            element = selected;
            return true;
        }

        private static bool TryPickExpressionElement(
            UIDocument uidoc,
            Document doc,
            CamboBIMWindow window,
            out Element element,
            out string error)
        {
            element = null;
            error = "";
            bool restoreWindow = window != null && window.IsVisible;

            try
            {
                if (restoreWindow)
                {
                    window.Hide();
                }

                Reference picked = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new QsExpressionSelectionFilter(),
                    "Pick one QS element or CBIM formwork shape to view expression.");
                if (picked == null || picked.ElementId == ElementId.InvalidElementId)
                {
                    error = "No element was picked.";
                    return false;
                }

                element = doc.GetElement(picked.ElementId);
                if (element == null)
                {
                    error = "Picked element was not found.";
                    return false;
                }

                uidoc.Selection.SetElementIds(new List<ElementId> { element.Id });
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                error = "View Expression cancelled.";
                return false;
            }
            finally
            {
                if (restoreWindow)
                {
                    window.Show();
                    window.Activate();
                }
            }
        }

        private static List<QsExpressionRow> BuildQsExpressionRows(Document doc, Element element, string selectionNote)
        {
            var rows = new List<QsExpressionRow>();
            if (doc == null || element == null) return rows;

            string ruleCode = GetCsvParameterValue(element, "CBIM_QsRuleCode");
            string formula = GetCsvParameterValue(element, "CBIM_QsFormula");
            string breakdown = GetCsvParameterValue(element, "CBIM_QsBreakdown");
            string netRemarks = BuildExpressionRemarks(ruleCode, breakdown);
            List<Element> formworkShapes = CollectFormworkShapesForHost(doc, element);

            bool useTasRows =
                AddBeamTasExpressionRows(rows, doc, element, formworkShapes, ruleCode) ||
                AddColumnTasExpressionRows(rows, doc, element, formworkShapes, ruleCode) ||
                AddWallTasExpressionRows(rows, doc, element, formworkShapes, ruleCode) ||
                AddSlabTasExpressionRows(rows, doc, element, formworkShapes, ruleCode) ||
                AddStairTasExpressionRows(rows, doc, element, formworkShapes, ruleCode);
            if (!useTasRows)
            {
                AddTasQuantityRows(rows, doc, element);
                AddHostQuantityRows(rows, doc, element);

                if (TryGetParamDouble(element, "CBIM_FormworkArea", out double netFormworkArea))
                {
                    string netExpression = string.IsNullOrWhiteSpace(formula)
                        ? "CBIM_FormworkArea"
                        : "CBIM_FormworkArea = " + formula;
                    AddQuantityRow(
                        rows,
                        "Area of formwork",
                        netExpression,
                        ToSquareMeters(netFormworkArea),
                        "m2",
                        "Net",
                        netRemarks);
                }
            }

            AddFoundationTopStatusRow(rows, element, ruleCode);
            AddKnownQsComponentRows(rows, element);

            AddFormworkShapeRows(rows, formworkShapes);
            AddLive3dDeductionRows(doc, element, formworkShapes, rows);

            if (!string.IsNullOrWhiteSpace(selectionNote) && rows.Count > 0)
            {
                rows[0].Remarks = CombineRemarks(rows[0].Remarks, selectionNote);
            }

            ApplyTasLikeFormworkTargets(rows, formworkShapes);
            AddDefaultExpressionTargets(rows, element);
            return rows;
        }

        private static void AddTasQuantityRows(List<QsExpressionRow> rows, Document doc, Element element)
        {
            if (rows == null || doc == null || element == null) return;

            QsMeasurementRulesProfile profile = LoadMeasurementRulesForExpression();
            HashSet<string> categories = ResolveTasRuleCategories(element);
            if (profile?.Rules == null || categories.Count == 0) return;

            foreach (QsMeasurementRuleRow rule in profile.Rules
                         .Where(r => IsExpressionQuantityRule(r, categories))
                         .OrderBy(r => r.SortOrder))
            {
                if (!TryEvaluateTasRuleQuantity(doc, element, rule, out double quantity, out string expression, out string unit)) continue;
                if (Math.Abs(quantity) <= 1e-9 && !ShouldShowZeroTasRule(rule)) continue;

                AddQuantityRow(
                    rows,
                    CleanTasQuantityName(rule.Description),
                    expression,
                    quantity,
                    unit,
                    BuildTasCountTag(rule),
                    BuildTasRemarks(rule));
            }
        }

        private static QsMeasurementRulesProfile LoadMeasurementRulesForExpression()
        {
            try
            {
                string path = GetDefaultMeasurementRulesPath();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    QsMeasurementRulesProfile profile = CamboBimJson.Deserialize<QsMeasurementRulesProfile>(json);
                    if (profile?.Rules != null && profile.Rules.Count > 0)
                    {
                        profile.Normalize();
                        return profile;
                    }
                }
            }
            catch
            {
                // Fall back to the built-in TAS-aligned rule catalog.
            }

            return QsMeasurementRulesProfile.CreateDefault();
        }

        private static string GetDefaultMeasurementRulesPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            return Path.Combine(appData, "MHNK", "RevitExtension", "QS", "measurement-rules.json");
        }

        private static bool IsExpressionQuantityRule(QsMeasurementRuleRow rule, HashSet<string> categories)
        {
            if (rule == null || !rule.IsEnabled) return false;
            if (string.IsNullOrWhiteSpace(rule.Category) || !categories.Contains(rule.Category)) return false;
            if (string.Equals(rule.Method, "Classification", StringComparison.OrdinalIgnoreCase)) return false;
            return string.Equals(rule.Method, "Quantity", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rule.Method, "Stage Bucket", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rule.Method, "Deduction", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> ResolveTasRuleCategories(Element element)
        {
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (element == null) return categories;

            if (HasAnyPositiveParam(element, "FWK.Kerb.Total", "FWK.Kerb.Sides", "FWK.Kerb.Top"))
            {
                categories.Add("Kerb");
                return categories;
            }

            if (HasAnyPositiveParam(element, "FWK.Other.Total", "FWK.Other.Sides", "FWK.Other.Bottom"))
            {
                categories.Add("Others");
                return categories;
            }

            if (HasAnyPositiveParam(element, "FWK.Lintel.Sides", "FWK.Lintel.Bottom"))
            {
                categories.Add("Lintel");
                return categories;
            }

            if (HasAnyPositiveParam(element, "FWK.Drop.Soffit", "FWK.Drop.Stage.1"))
            {
                categories.Add("Drop Panel");
                return categories;
            }

            if (HasAnyPositiveParam(element, "FWK.Eave.Bottom", "FWK.Eave.Edge"))
            {
                categories.Add("Eave");
                return categories;
            }

            if (HasAnyPositiveParam(element, "FIN.WF.Area", "FIN.WF.Gross")) categories.Add("Wall Finish");
            if (HasAnyPositiveParam(element, "FIN.CF.Area", "FIN.CF.Gross")) categories.Add("Ceiling Finish");
            if (HasAnyPositiveParam(element, "FIN.SC.Area", "FIN.SC.Gross")) categories.Add("Suspended Ceiling");
            if (HasAnyPositiveParam(element, "FIN.FF.Area", "FIN.FF.Gross")) categories.Add("Floor Finish");
            if (HasAnyPositiveParam(element, "FIN.WP.Area", "FIN.WP.Gross")) categories.Add("Waterproof");
            if (categories.Count > 0) return categories;

            long categoryId = element.Category?.Id?.Value ?? 0;
            if (categoryId == (long)BuiltInCategory.OST_StructuralFoundation)
            {
                categories.Add("Foundation");
            }
            else if (categoryId == (long)BuiltInCategory.OST_StructuralColumns)
            {
                categories.Add("Column");
            }
            else if (categoryId == (long)BuiltInCategory.OST_StructuralFraming)
            {
                categories.Add("Beam");
            }
            else if (categoryId == (long)BuiltInCategory.OST_Walls)
            {
                categories.Add("Wall");
            }
            else if (categoryId == (long)BuiltInCategory.OST_Floors)
            {
                categories.Add("Slab");
                if (HasAnyPositiveParam(element, "FWK.Floor.Opening.Count", "FWK.Floor.Opening.Area", "FWK.Floor.Opening.Girth"))
                {
                    categories.Add("Slab Opening");
                }
            }
            else if (categoryId == (long)BuiltInCategory.OST_Stairs)
            {
                categories.Add("Staircase");
            }
            else if (categoryId == (long)BuiltInCategory.OST_GenericModel &&
                     HasAnyPositiveParam(element, SoilExcavationVolumeParamName, SoilBackfilledVolumeParamName))
            {
                categories.Add("Excavation");
            }

            return categories;
        }

        private static bool HasAnyPositiveParam(Element element, params string[] names)
        {
            foreach (string name in names ?? Array.Empty<string>())
            {
                if (TryGetParamDouble(element, name, out double value) && Math.Abs(value) > 1e-9)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryEvaluateTasRuleQuantity(
            Document doc,
            Element element,
            QsMeasurementRuleRow rule,
            out double quantity,
            out string expression,
            out string unit)
        {
            quantity = 0.0;
            expression = rule?.Value ?? "";
            unit = NormalizeTasUnit(rule?.Unit);
            if (doc == null || element == null || rule == null) return false;

            string mapping = (rule.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(mapping) || mapping == "-") return false;

            double rawValue;
            if (TryGetDirectExpressionValue(element, mapping, out rawValue))
            {
                quantity = ConvertExpressionValue(rawValue, unit);
                expression = mapping;
                if (string.Equals(rule.Method, "Deduction", StringComparison.OrdinalIgnoreCase))
                {
                    quantity = -Math.Abs(quantity);
                    expression = "-" + expression;
                }

                return true;
            }

            if (mapping.IndexOf("HOST_VOLUME_COMPUTED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("solid volume", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rawValue = GetElementVolume(element);
                if (rawValue <= 1e-9) return false;
                quantity = ToCubicMeters(rawValue);
                expression = mapping.IndexOf("HOST_VOLUME_COMPUTED", StringComparison.OrdinalIgnoreCase) >= 0
                    ? mapping
                    : "HOST_VOLUME_COMPUTED";
                return true;
            }

            if (mapping.IndexOf("HOST_AREA_COMPUTED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("projected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("section area", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rawValue = GetElementArea(element);
                if (rawValue <= 1e-9) return false;
                quantity = ToSquareMeters(rawValue);
                expression = mapping.IndexOf("HOST_AREA_COMPUTED", StringComparison.OrdinalIgnoreCase) >= 0
                    ? mapping
                    : "HOST_AREA_COMPUTED";
                return true;
            }

            if (mapping.IndexOf("Element count", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                quantity = 1.0;
                expression = "Element count";
                unit = string.IsNullOrWhiteSpace(unit) || unit == "-" ? "pc" : unit;
                return true;
            }

            if (mapping.IndexOf("2 x", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("Section perimeter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!TryGetSectionPerimeter(element, out rawValue)) return false;
                quantity = ToMeters(rawValue);
                expression = "2 * (section width + section depth)";
                unit = "m";
                return true;
            }

            if (mapping.IndexOf("location curve length", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("axis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("Pile depth/length", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rawValue = GetElementLength(element);
                if (rawValue <= 1e-9)
                {
                    rawValue = GetElementVerticalHeight(element);
                }

                if (rawValue <= 1e-9) return false;
                quantity = ToMeters(rawValue);
                expression = "Element length";
                unit = "m";
                return true;
            }

            if (mapping.IndexOf("width/thickness", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapping.IndexOf("Floor thickness", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                rawValue = GetElementThickness(element);
                if (rawValue <= 1e-9) return false;
                quantity = ToMeters(rawValue);
                expression = mapping.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Floor thickness"
                    : "Wall width/thickness";
                unit = "m";
                return true;
            }

            if (mapping.IndexOf("Excavation volume", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (TryGetParamDouble(element, SoilBackfilledVolumeParamName, out rawValue) && rawValue > 1e-9)
                {
                    quantity = ToCubicMeters(rawValue);
                    expression = SoilBackfilledVolumeParamName;
                    unit = "m3";
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetDirectExpressionValue(Element element, string mapping, out double rawValue)
        {
            rawValue = 0.0;
            if (element == null || string.IsNullOrWhiteSpace(mapping)) return false;

            string directName = mapping.Trim();
            if (TryGetParamDouble(element, directName, out rawValue)) return true;

            if (string.Equals(directName, "HOST_VOLUME_COMPUTED", StringComparison.OrdinalIgnoreCase))
            {
                rawValue = GetElementVolume(element);
                return rawValue > 1e-9;
            }

            if (string.Equals(directName, "HOST_AREA_COMPUTED", StringComparison.OrdinalIgnoreCase))
            {
                rawValue = GetElementArea(element);
                return rawValue > 1e-9;
            }

            return false;
        }

        private static double ConvertExpressionValue(double rawValue, string unit)
        {
            if (string.Equals(unit, "m2", StringComparison.OrdinalIgnoreCase)) return ToSquareMeters(rawValue);
            if (string.Equals(unit, "m3", StringComparison.OrdinalIgnoreCase)) return ToCubicMeters(rawValue);
            if (string.Equals(unit, "m", StringComparison.OrdinalIgnoreCase)) return ToMeters(rawValue);
            return rawValue;
        }

        private static bool TryGetSectionPerimeter(Element element, out double perimeter)
        {
            perimeter = 0.0;
            if (element == null) return false;

            double width = GetParamDouble(element, new[] { "CBIM_Beam_Width", "Width", "b", "B", "Diameter", "D" });
            double depth = GetParamDouble(element, new[] { "CBIM_Beam_Depth", "Depth", "Height", "h", "H", "Diameter", "D" });

            ElementType type = GetElementType(element);
            if (width <= 1e-9)
            {
                width = GetParamDouble(type, new[] { "Width", "b", "B", "Diameter", "D", "Column Width", "Section Width" });
            }

            if (depth <= 1e-9)
            {
                depth = GetParamDouble(type, new[] { "Depth", "Height", "h", "H", "Diameter", "D", "Column Depth", "Section Height" });
            }

            if (width <= 1e-9 || depth <= 1e-9) return false;
            perimeter = 2.0 * (width + depth);
            return perimeter > 1e-9;
        }

        private static double GetElementThickness(Element element)
        {
            if (element == null) return 0.0;

            if (element is Wall wall && wall.Width > 1e-9)
            {
                return wall.Width;
            }

            double thickness = GetParamDouble(element, new[] { "Thickness", "Width", "Default Thickness", "Structural Thickness" });
            if (thickness > 1e-9) return thickness;

            ElementType type = GetElementType(element);
            thickness = GetParamDouble(type, new[] { "Thickness", "Width", "Default Thickness", "Structural Thickness" });
            if (thickness > 1e-9) return thickness;

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return 0.0;

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double dz = Math.Abs(box.Max.Z - box.Min.Z);
            double min = new[] { dx, dy, dz }.Where(v => v > 1e-9).DefaultIfEmpty(0.0).Min();
            return min;
        }

        private static string NormalizeTasUnit(string unit)
        {
            string text = (unit ?? "").Trim();
            if (string.Equals(text, "pc", StringComparison.OrdinalIgnoreCase)) return "pc";
            return text;
        }

        private static string CleanTasQuantityName(string description)
        {
            string text = (description ?? "").Trim();
            string[] prefixes = { "Quantity:", "Stage Bucket:", "Deduction:" };
            foreach (string prefix in prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(prefix.Length).Trim();
                    break;
                }
            }

            return string.IsNullOrWhiteSpace(text) ? "Quantity" : text;
        }

        private static string BuildTasCountTag(QsMeasurementRuleRow rule)
        {
            string method = string.IsNullOrWhiteSpace(rule?.Method) ? "Quantity" : rule.Method.Trim();
            string code = rule?.Code ?? "";
            return string.IsNullOrWhiteSpace(code) ? method : method + " | " + code;
        }

        private static string BuildTasRemarks(QsMeasurementRuleRow rule)
        {
            return CombineRemarks(
                string.IsNullOrWhiteSpace(rule?.Option) ? "" : "TAS sheet: " + rule.Option,
                string.IsNullOrWhiteSpace(rule?.Value) ? "" : "Mapping: " + rule.Value);
        }

        private static bool ShouldShowZeroTasRule(QsMeasurementRuleRow rule)
        {
            return rule != null &&
                   !string.IsNullOrWhiteSpace(rule.Code) &&
                   rule.Code.IndexOf(".NUMBER", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddFoundationTopStatusRow(List<QsExpressionRow> rows, Element element, string ruleCode)
        {
            if (rows == null || element == null) return;
            if (!ResolveTasRuleCategories(element).Contains("Foundation")) return;
            if (!TryGetParamDouble(element, "FWK.Foun.Top", out double topArea)) return;
            if (topArea > 1e-9) return;

            bool topIsActive = (ruleCode ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(p => string.Equals(p.Trim(), "FOUN.TOP", StringComparison.OrdinalIgnoreCase));
            AddQuantityRow(
                rows,
                "Area of top(m2)",
                topIsActive ? "FWK.Foun.Top (enabled, no measured area)" : "0 (FOUN.TOP setting is not active)",
                0.0,
                "m2",
                topIsActive ? "Zero" : "Excluded",
                topIsActive
                    ? "Foundation top rule is enabled, but the measured top area is zero."
                    : "Foundation top formwork is excluded from the net formula.");
        }

        private static void AddHostQuantityRows(List<QsExpressionRow> rows, Document doc, Element element)
        {
            if (rows == null || element == null) return;

            if (TryGetParamDouble(element, "CBIM_Beam_Volume", out double beamVolume) && beamVolume > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Concrete volume",
                    "CBIM_Beam_Length * CBIM_Beam_Width * CBIM_Beam_Depth",
                    ToCubicMeters(beamVolume),
                    "m3",
                    "Base",
                    "CBIM_Beam_Volume");
            }
            else
            {
                double volume = GetElementVolume(element);
                if (volume > 1e-9)
                {
                    AddQuantityRow(rows, "Concrete volume", "HOST_VOLUME_COMPUTED", ToCubicMeters(volume), "m3", "Base", "");
                }
            }

            double area = GetElementArea(element);
            if (area > 1e-9)
            {
                AddQuantityRow(rows, "Host projected area", "HOST_AREA_COMPUTED", ToSquareMeters(area), "m2", "Base", "");
            }

            double length = GetElementLength(element);
            if (length > 1e-9)
            {
                AddQuantityRow(rows, "Element length", "CURVE_ELEM_LENGTH", ToMeters(length), "m", "Base", "");
            }

            string typeName = GetElementTypeName(doc, element);
            if (!string.IsNullOrWhiteSpace(typeName) && rows.Count > 0)
            {
                rows[0].Remarks = CombineRemarks(rows[0].Remarks, "Type: " + typeName);
            }
        }

        private static bool AddBeamTasExpressionRows(
            List<QsExpressionRow> rows,
            Document doc,
            Element element,
            IList<Element> formworkShapes,
            string ruleCode)
        {
            if (rows == null || doc == null || element == null) return false;
            if (!ResolveTasRuleCategories(element).Contains("Beam")) return false;

            bool includeSide = IsRuleActive(ruleCode, "BEAM.SIDE", true);
            bool includeBottom = IsRuleActive(ruleCode, "BEAM.BOTTOM", true);
            bool includeTop = IsRuleActive(ruleCode, "BEAM.TOP", false);
            bool deductColumn = IsRuleActive(ruleCode, "BEAM.DEDUCT.COL", true);
            List<Element> columns = CollectElementsOfCategory(doc, BuiltInCategory.OST_StructuralColumns);
            if (!TryBuildBeamTasTakeoff(
                    doc,
                    element,
                    columns,
                    includeSide,
                    includeBottom,
                    includeTop,
                    deductColumn,
                    out BeamTasTakeoff takeoff))
            {
                return false;
            }

            AddQuantityRow(
                rows,
                "Volume",
                BuildBeamTasVolumeExpression(takeoff),
                ToCubicMeters(takeoff.VolumeNet),
                "m3",
                "Count",
                "TAS beam method: section x length, deduct column length.",
                new[] { element.Id });

            AddQuantityRow(
                rows,
                BuildBeamTasFormworkName(element),
                BuildBeamTasFormworkExpression(takeoff),
                ToSquareMeters(takeoff.FormworkNetArea),
                "m2",
                "Count",
                "TAS beam method: side + soffit" + (includeTop ? " + top" : "") + " - column deduction.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes));

            if (includeSide)
            {
                AddQuantityRow(
                    rows,
                    BuildBeamTasSideName(element),
                    BuildBeamTasSideExpression(takeoff),
                    ToSquareMeters(takeoff.SideNetArea),
                    "m2",
                    "Count",
                    "Left side + right side - column side deduction.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "Side"));
            }

            if (includeBottom)
            {
                AddQuantityRow(
                    rows,
                    BuildBeamTasSoffitName(element),
                    BuildBeamTasSoffitExpression(takeoff),
                    ToSquareMeters(takeoff.BottomNetArea),
                    "m2",
                    "Count",
                    "Soffit/bottom formwork - column soffit deduction.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "Bottom"));
            }

            if (includeTop && takeoff.TopNetArea > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Area of formwork to top of beam",
                    BuildBeamTasTopExpression(takeoff),
                    ToSquareMeters(takeoff.TopNetArea),
                    "m2",
                    "Count",
                    "Top formwork - column top deduction.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "Top", "TopSlope"));
            }

            AddQuantityRow(
                rows,
                "Girth of section",
                "(" + FormatTasLength(takeoff.Width) + "<Width> + " + FormatTasLength(takeoff.Depth) + "<Height>) * 2",
                ToMeters(takeoff.Girth),
                "m",
                "Count",
                "TAS section girth.",
                new[] { element.Id });

            AddQuantityRow(
                rows,
                "Net length",
                FormatTasLength(takeoff.AxisLength) + "<Length of beam> - " + FormatTasLength(takeoff.ColumnDeductLength) + "<Deduct column>",
                ToMeters(takeoff.NetLength),
                "m",
                "Count",
                "Axis length less column overlap length.",
                new[] { element.Id });

            AddQuantityRow(
                rows,
                "Number",
                "Element count",
                1.0,
                "pc",
                "Count",
                "",
                new[] { element.Id });

            if (TryGetParamDouble(element, "Rebar.Weight", out double rebarWeight) && rebarWeight > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Weight of rebar",
                    FormatTasVolume(takeoff.VolumeNet) + "<Volume> * Rebar ratio",
                    rebarWeight,
                    "kg",
                    "Count",
                    "Rebar.Weight",
                    new[] { element.Id });
            }

            AddQuantityRow(
                rows,
                "Length of axis",
                "Length of beam centerline",
                ToMeters(takeoff.AxisLength),
                "m",
                "Count",
                "",
                new[] { element.Id });

            return true;
        }

        private static bool AddColumnTasExpressionRows(
            List<QsExpressionRow> rows,
            Document doc,
            Element element,
            IList<Element> formworkShapes,
            string ruleCode)
        {
            if (rows == null || doc == null || element == null) return false;
            if (!ResolveTasRuleCategories(element).Contains("Column")) return false;
            if (!TryBuildColumnTasTakeoff(element, ruleCode, out ColumnTasTakeoff takeoff)) return false;

            AddQuantityRow(
                rows,
                "Volume",
                "(" + FormatTasLength(takeoff.Width) + "<Length> * " +
                FormatTasLength(takeoff.Depth) + "<Width> * " +
                FormatTasLength(takeoff.Height) + "<Height>)",
                ToCubicMeters(takeoff.Volume),
                "m3",
                "Count",
                "TAS column method: section x height.",
                new[] { element.Id });

            AddQuantityRow(
                rows,
                "Area of formwork",
                FormatTasArea(takeoff.FormworkArea) + "<Original area of formwork to column>",
                ToSquareMeters(takeoff.FormworkArea),
                "m2",
                "Count",
                "TAS column priority: keep column formwork gross; deduct beam formwork from the beam.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Side"));

            AddQuantityRow(
                rows,
                "Number",
                "Element count",
                1.0,
                "pc",
                "Count",
                "",
                new[] { element.Id });

            if (TryGetParamDouble(element, "Rebar.Weight", out double rebarWeight) && rebarWeight > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Weight of rebar",
                    FormatTasVolume(takeoff.Volume) + "<Volume> * Rebar ratio",
                    rebarWeight,
                    "kg",
                    "Count",
                    "Rebar.Weight",
                    new[] { element.Id });
            }

            AddQuantityRow(
                rows,
                "Girth",
                "(" + FormatTasLength(takeoff.Width) + "<Length> + " +
                FormatTasLength(takeoff.Depth) + "<Width>) * 2",
                ToMeters(takeoff.Girth),
                "m",
                "Count",
                "TAS section girth.",
                new[] { element.Id });

            return true;
        }

        private static bool AddSlabTasExpressionRows(
            List<QsExpressionRow> rows,
            Document doc,
            Element element,
            IList<Element> formworkShapes,
            string ruleCode)
        {
            if (rows == null || doc == null || element == null) return false;
            if (!ResolveTasRuleCategories(element).Contains("Slab")) return false;
            if (!TryBuildSlabTasTakeoff(element, ruleCode, out SlabTasTakeoff takeoff)) return false;

            AddQuantityRow(
                rows,
                "Volume",
                BuildSlabTasVolumeExpression(takeoff),
                ToCubicMeters(takeoff.VolumeNet),
                "m3",
                "Count",
                "TAS slab method: original projected area x thickness, deduct column/beam/straight-flight volumes.",
                new[] { element.Id });

            AddQuantityRow(
                rows,
                "Area",
                BuildSlabTasFormworkExpression(takeoff),
                ToSquareMeters(takeoff.FormworkNetArea),
                "m2",
                "Count",
                "TAS slab method: soffit area less columns, beams, walls, foundations, slab overlaps, straight flights, and other concrete.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Bottom", "TopSlope"));

            AddQuantityRow(
                rows,
                BuildSlabTasSoffitName(element),
                BuildSlabTasFormworkExpression(takeoff),
                ToSquareMeters(takeoff.FormworkNetArea),
                "m2",
                "Count",
                "Same TAS net quantity as slab formwork area.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Bottom", "TopSlope"));

            if (takeoff.EdgeBreakLength > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Length of formwork to edge and break of slab in stages(0.1~0.2m)",
                    BuildSlabTasEdgeExpression(takeoff),
                    ToMeters(takeoff.EdgeBreakLength),
                    "m",
                    "Count",
                    "Derived from slab edge/break side formwork.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "Side"));
            }

            AddQuantityRow(
                rows,
                "Projected area",
                BuildSlabTasProjectedAreaExpression(takeoff),
                ToSquareMeters(takeoff.ProjectedAreaNet),
                "m2",
                "Count",
                "TAS projected area deducts wall and straight-flight footprints.",
                new[] { element.Id });

            if (TryGetParamDouble(element, "Rebar.Weight", out double rebarWeight) && rebarWeight > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Weight of rebar",
                    FormatTasVolume(takeoff.VolumeNet) + "<Volume> * Rebar ratio",
                    rebarWeight,
                    "kg",
                    "Count",
                    "Rebar.Weight",
                    new[] { element.Id });
            }

            AddQuantityRow(
                rows,
                "Number",
                "Element count",
                1.0,
                "pc",
                "Count",
                "",
                new[] { element.Id });

            return true;
        }

        private static bool AddStairTasExpressionRows(
            List<QsExpressionRow> rows,
            Document doc,
            Element element,
            IList<Element> formworkShapes,
            string ruleCode)
        {
            if (rows == null || doc == null || element == null) return false;
            if (!ResolveTasRuleCategories(element).Contains("Staircase")) return false;
            if (!TryBuildStairTasTakeoff(element, out StairTasTakeoff takeoff)) return false;

            if (takeoff.Volume > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Volume",
                    "HOST_VOLUME_COMPUTED",
                    ToCubicMeters(takeoff.Volume),
                    "m3",
                    "Count",
                    "Revit host volume.",
                    new[] { element.Id });
            }

            AddQuantityRow(
                rows,
                "Area of formwork",
                BuildStairTasFormworkExpression(takeoff),
                ToSquareMeters(takeoff.NetArea),
                "m2",
                "Count",
                "TAS stair method: side/riser faces plus bottom/soffit faces; top/tread walking surfaces are not included.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Painting", "PaintingSide", "PaintingBottom", "Side", "Bottom"));

            AddQuantityRow(
                rows,
                "Area of stair painting/formwork",
                BuildStairTasFormworkExpression(takeoff),
                ToSquareMeters(takeoff.NetArea),
                "m2",
                "Count",
                "Same TAS net quantity as stair painting/formwork area.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Painting", "PaintingSide", "PaintingBottom", "Side", "Bottom"));

            AddQuantityRow(
                rows,
                "Gross stair painting faces",
                FormatTasArea(takeoff.PaintingArea) + "<Original stair painting faces>",
                ToSquareMeters(takeoff.PaintingArea),
                "m2",
                "Gross",
                "Exposed stair side/riser faces plus underside/soffit faces.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Painting", "PaintingSide", "PaintingBottom", "Side", "Bottom"));

            if (takeoff.PaintingSideArea > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Stair painting side/riser faces",
                    FormatTasArea(takeoff.PaintingSideArea) + "<Original stair side/riser painting faces>",
                    ToSquareMeters(takeoff.PaintingSideArea),
                    "m2",
                    "Gross",
                    "Vertical stair side panels and risers.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "PaintingSide", "Side"));
            }

            if (takeoff.PaintingBottomArea > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Stair painting bottom/soffit faces",
                    FormatTasArea(takeoff.PaintingBottomArea) + "<Original stair bottom/soffit painting faces>",
                    ToSquareMeters(takeoff.PaintingBottomArea),
                    "m2",
                    "Gross",
                    "Downward and sloped underside faces under stair flights and landings.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "PaintingBottom", "Bottom"));
            }

            if (takeoff.StepCount > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Number of steps",
                    "FWK.Stair.StepCount",
                    takeoff.StepCount,
                    "pc",
                    "Count",
                    "",
                    new[] { element.Id });
            }

            AddQuantityRow(
                rows,
                "Number",
                "Element count",
                1.0,
                "pc",
                "Count",
                "",
                new[] { element.Id });

            if (TryGetParamDouble(element, "Rebar.Weight", out double rebarWeight) && rebarWeight > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Weight of rebar",
                    FormatTasVolume(takeoff.Volume) + "<Volume> * Rebar ratio",
                    rebarWeight,
                    "kg",
                    "Count",
                    "Rebar.Weight",
                    new[] { element.Id });
            }

            return true;
        }

        private static bool TryBuildStairTasTakeoff(Element element, out StairTasTakeoff takeoff)
        {
            takeoff = null;
            if (element == null) return false;

            double paintingSideArea = GetParamDouble(element, new[] { "FWK.Stair.PaintingSide", "FWK.Stair.Sides" });
            double paintingBottomArea = GetParamDouble(element, new[] { "FWK.Stair.PaintingBottom" });
            double paintingArea = GetParamDouble(element, new[] { "FWK.Stair.Painting" });
            double totalArea = GetParamDouble(element, new[] { "FWK.Stair.Total", "CBIM_FormworkArea" });
            if (paintingArea <= 1e-9)
            {
                paintingArea = paintingSideArea + paintingBottomArea;
            }

            if (paintingArea <= 1e-9)
            {
                paintingArea = totalArea;
            }

            if (paintingSideArea <= 1e-9 && paintingBottomArea <= 1e-9)
            {
                paintingSideArea = paintingArea;
            }

            double subFoun = GetParamDouble(element, new[] { "FWK.Stair.SubFoun" });
            double subBeam = GetParamDouble(element, new[] { "FWK.Stair.SubBeam" });
            double subCol = GetParamDouble(element, new[] { "FWK.Stair.SubCol" });
            double subWall = GetParamDouble(element, new[] { "FWK.Stair.SubWall" });
            double subFloor = GetParamDouble(element, new[] { "FWK.Stair.SubFloor" });
            double subGeneric = GetParamDouble(element, new[] { "FWK.Stair.SubGeneric" });

            double netArea = GetParamDouble(element, new[] { "CBIM_FormworkArea" });
            if (netArea <= 1e-9 && paintingArea > 1e-9)
            {
                netArea = Math.Max(0.0, paintingArea - subFoun - subBeam - subCol - subWall - subFloor - subGeneric);
            }

            if (paintingArea <= 1e-9 && netArea <= 1e-9) return false;

            takeoff = new StairTasTakeoff
            {
                PaintingArea = Math.Max(0.0, paintingArea),
                PaintingSideArea = Math.Max(0.0, paintingSideArea),
                PaintingBottomArea = Math.Max(0.0, paintingBottomArea),
                NetArea = Math.Max(0.0, netArea),
                SubFoundation = Math.Max(0.0, subFoun),
                SubBeam = Math.Max(0.0, subBeam),
                SubColumn = Math.Max(0.0, subCol),
                SubWall = Math.Max(0.0, subWall),
                SubFloor = Math.Max(0.0, subFloor),
                SubGeneric = Math.Max(0.0, subGeneric),
                StepCount = GetParamDouble(element, new[] { "FWK.Stair.StepCount" }),
                Volume = GetElementVolume(element)
            };
            return true;
        }

        private static string BuildStairTasFormworkExpression(StairTasTakeoff takeoff)
        {
            if (takeoff == null) return "0";

            string expression = FormatTasArea(takeoff.PaintingSideArea) + "<Original stair side/riser painting faces>";
            if (takeoff.PaintingBottomArea > 1e-9)
            {
                expression += " + " + FormatTasArea(takeoff.PaintingBottomArea) + "<Original stair bottom/soffit painting faces>";
            }
            AddStairTasAreaDeduction(ref expression, takeoff.SubFoundation, "Deduct foundation");
            AddStairTasAreaDeduction(ref expression, takeoff.SubBeam, "Deduct beam");
            AddStairTasAreaDeduction(ref expression, takeoff.SubColumn, "Deduct column");
            AddStairTasAreaDeduction(ref expression, takeoff.SubWall, "Deduct wall");
            AddStairTasAreaDeduction(ref expression, takeoff.SubFloor, "Deduct slab/floor");
            AddStairTasAreaDeduction(ref expression, takeoff.SubGeneric, "Deduct generic model");
            return expression;
        }

        private static void AddStairTasAreaDeduction(ref string expression, double area, string label)
        {
            if (area <= 1e-9) return;
            expression += " - " + FormatTasArea(area) + "<" + label + ">";
        }

        private static bool AddWallTasExpressionRows(
            List<QsExpressionRow> rows,
            Document doc,
            Element element,
            IList<Element> formworkShapes,
            string ruleCode)
        {
            if (rows == null || doc == null || element == null) return false;
            if (!ResolveTasRuleCategories(element).Contains("Wall")) return false;
            if (!TryBuildWallTasTakeoff(element, out WallTasTakeoff takeoff)) return false;

            AddQuantityRow(
                rows,
                "Volume",
                BuildWallTasVolumeExpression(takeoff),
                ToCubicMeters(takeoff.VolumeNet),
                "m3",
                "Count",
                "TAS wall method: original length x height x thickness, deduct joined wall length.",
                new[] { element.Id });

            AddQuantityRow(
                rows,
                "Area of formwork",
                BuildWallTasFormworkExpression(takeoff),
                ToSquareMeters(takeoff.FormworkNetArea),
                "m2",
                "Count",
                "TAS wall method: both side lengths x height, deduct joined wall once.",
                GetFormworkShapeIdsByFaceTypes(formworkShapes, "Side"));

            if (takeoff.StrutFormworkArea > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Area of formwork for strutting high",
                    BuildWallTasStrutExpression(takeoff),
                    ToSquareMeters(takeoff.StrutFormworkArea),
                    "m2",
                    "Count",
                    "TAS wall high-support area above 3.5m.",
                    GetFormworkShapeIdsByFaceTypes(formworkShapes, "Side"));
            }

            AddQuantityRow(
                rows,
                "Area",
                BuildWallTasAreaExpression(takeoff),
                ToSquareMeters(takeoff.ProjectedAreaNet),
                "m2",
                "Count",
                "TAS one-side wall area.",
                new[] { element.Id });

            if (TryGetParamDouble(element, "Rebar.Weight", out double rebarWeight) && rebarWeight > 1e-9)
            {
                AddQuantityRow(
                    rows,
                    "Weight of rebar",
                    FormatTasVolume(takeoff.VolumeNet) + "<Volume> * Rebar ratio",
                    rebarWeight,
                    "kg",
                    "Count",
                    "Rebar.Weight",
                    new[] { element.Id });
            }

            AddQuantityRow(rows, "Number", "Element count", 1.0, "pc", "Count", "", new[] { element.Id });
            AddQuantityRow(
                rows,
                "Net length of wall",
                FormatTasLength(takeoff.OriginalLength) + "<Original net length> - " +
                FormatTasLength(takeoff.WallDeductLength) + "<Deduct wall>",
                ToMeters(takeoff.NetLength),
                "m",
                "Count",
                "",
                new[] { element.Id });
            AddQuantityRow(rows, "Original thickness of wall", "Wall width/thickness", ToMeters(takeoff.Thickness), "m", "Count", "", new[] { element.Id });
            AddQuantityRow(rows, "Original height of wall", "FWK.Wall.OriginalHeight", ToMeters(takeoff.Height), "m", "Count", "", new[] { element.Id });
            AddQuantityRow(rows, "Original length of wall", "TAS average side length", ToMeters(takeoff.OriginalLength), "m", "Count", "", new[] { element.Id });

            return true;
        }

        private static bool TryBuildWallTasTakeoff(Element element, out WallTasTakeoff takeoff)
        {
            takeoff = null;
            if (element == null) return false;

            double sideArea = GetParamDouble(element, new[] { "FWK.Wall.Sides" });
            if (sideArea <= 1e-9)
            {
                sideArea = GetParamDouble(element, new[] { "FWK.Wall.Total", "CBIM_FormworkArea" });
            }

            double height = GetParamDouble(element, new[] { "FWK.Wall.OriginalHeight" });
            if (height <= 1e-9) height = GetElementVerticalHeight(element);

            double thickness = GetElementThickness(element);
            if (sideArea <= 1e-9 || height <= 1e-9 || thickness <= 1e-9) return false;

            double joinedEndCapArea = Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.JoinedEndCap" }));
            double wallDeductArea = joinedEndCapArea + Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.SubWall" }));
            double originalFormworkArea = sideArea + joinedEndCapArea;
            double sideLengthSum = originalFormworkArea / height;

            double locationLength = GetElementLength(element);
            double leftLength = sideLengthSum * 0.5;
            double rightLength = leftLength;
            if (locationLength > 1e-9 && sideLengthSum - locationLength > 1e-9)
            {
                leftLength = Math.Max(locationLength, sideLengthSum - locationLength);
                rightLength = Math.Min(locationLength, sideLengthSum - locationLength);
            }

            double originalLength = sideLengthSum * 0.5;
            double wallDeductLength = height > 1e-9 ? wallDeductArea / height : 0.0;
            double otherDeductArea =
                Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.SubFoun" })) +
                Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.SubBeam" })) +
                Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.SubCol" })) +
                Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.SubFloor" })) +
                Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Wall.SubGeneric" }));

            double strutStartHeight = 3.5 / 0.3048;
            double strutHeight = Math.Max(0.0, height - strutStartHeight);
            takeoff = new WallTasTakeoff
            {
                LeftSideLength = leftLength,
                RightSideLength = rightLength,
                OriginalLength = originalLength,
                Height = height,
                Thickness = thickness,
                WallDeductArea = wallDeductArea,
                OtherDeductArea = otherDeductArea,
                WallDeductLength = wallDeductLength,
                StrutHeight = strutHeight
            };

            return takeoff.FormworkNetArea > 1e-9 || takeoff.VolumeNet > 1e-9;
        }

        private static string BuildWallTasVolumeExpression(WallTasTakeoff takeoff)
        {
            string expression =
                "(" + FormatTasLength(takeoff.OriginalLength) + "<Length> * " +
                FormatTasLength(takeoff.Height) + "<Height of wall> * " +
                FormatTasLength(takeoff.Thickness) + "<Thickness of wall>)";
            AddSlabTasVolumeDeduction(ref expression, takeoff.WallDeductArea, takeoff.Thickness, "Deduct wall");
            return expression;
        }

        private static string BuildWallTasFormworkExpression(WallTasTakeoff takeoff)
        {
            string expression =
                "(" + FormatTasLength(takeoff.LeftSideLength) + "<Length of left side> + " +
                FormatTasLength(takeoff.RightSideLength) + "<Length of right side>) * " +
                FormatTasLength(takeoff.Height) + "<Height of formwork to wall>";
            AddSlabTasAreaDeduction(ref expression, takeoff.WallDeductArea, "Deduct wall");
            AddSlabTasAreaDeduction(ref expression, takeoff.OtherDeductArea, "Deduct other");
            return expression;
        }

        private static string BuildWallTasStrutExpression(WallTasTakeoff takeoff)
        {
            string expression =
                "((" + FormatTasLength(takeoff.LeftSideLength) + " + " +
                FormatTasLength(takeoff.RightSideLength) + ") * " +
                FormatTasLength(takeoff.StrutHeight) + "<Original area of formwork for strutting high>";
            AddSlabTasAreaDeduction(ref expression, takeoff.StrutWallDeductArea, "Deduct wall");
            return expression + ")";
        }

        private static string BuildWallTasAreaExpression(WallTasTakeoff takeoff)
        {
            string expression =
                "(" + FormatTasLength(takeoff.OriginalLength) + "<Length> * " +
                FormatTasLength(takeoff.Height) + "<Height of wall>)";
            AddSlabTasAreaDeduction(ref expression, takeoff.WallDeductArea, "Deduct wall");
            return expression;
        }

        private static bool TryBuildSlabTasTakeoff(Element element, string ruleCode, out SlabTasTakeoff takeoff)
        {
            takeoff = null;
            if (element == null) return false;

            double bottomArea = GetParamDouble(element, new[] { "FWK.Floor.Bottom" });
            if (bottomArea <= 1e-9)
            {
                bottomArea = GetElementArea(element);
            }

            double topSlopeArea = IsRuleActive(ruleCode, "SLAB.TOP.SLOPE", false)
                ? GetParamDouble(element, new[] { "FWK.Floor.TopSlope" })
                : 0.0;
            double originalArea = Math.Max(0.0, bottomArea) + Math.Max(0.0, topSlopeArea);
            if (originalArea <= 1e-9) return false;

            double thickness = GetElementThickness(element);
            if (thickness <= 1e-9)
            {
                double volume = GetElementVolume(element);
                if (volume > 1e-9)
                {
                    thickness = volume / originalArea;
                }
            }

            double edgeLength = GetSlabEdgeBreakLength(element, thickness);

            takeoff = new SlabTasTakeoff
            {
                OriginalArea = originalArea,
                Thickness = Math.Max(0.0, thickness),
                SubFoundation = GetParamDouble(element, new[] { "FWK.Floor.SubFoun" }),
                SubBeam = GetParamDouble(element, new[] { "FWK.Floor.SubBeam" }),
                SubColumn = GetParamDouble(element, new[] { "FWK.Floor.SubCol" }),
                SubWall = GetParamDouble(element, new[] { "FWK.Floor.SubWall" }),
                SubFloor = GetParamDouble(element, new[] { "FWK.Floor.SubFloor" }),
                SubStair = GetParamDouble(element, new[] { "FWK.Floor.SubStair" }),
                SubGeneric = GetParamDouble(element, new[] { "FWK.Floor.SubGeneric" }),
                EdgeBreakLength = edgeLength
            };

            return true;
        }

        private static double GetSlabEdgeBreakLength(Element element, double thickness)
        {
            if (element == null || thickness <= 1e-9) return 0.0;

            double area = 0.0;
            area += Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Floor.EdgeBreak.Lte250" }));
            area += Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Floor.EdgeBreak.Lte500" }));
            area += Math.Max(0.0, GetParamDouble(element, new[] { "FWK.Floor.EdgeBreak.Lte1000" }));
            if (area <= 1e-9) return 0.0;

            return area / thickness;
        }

        private static string BuildSlabTasVolumeExpression(SlabTasTakeoff takeoff)
        {
            string expression =
                "(" + FormatTasArea(takeoff.OriginalArea) + "<Original area> * " +
                FormatTasLength(takeoff.Thickness) + "<Thickness>)";

            AddSlabTasVolumeDeduction(ref expression, takeoff.SubColumn, takeoff.Thickness, "Deduct column");
            AddSlabTasVolumeDeduction(ref expression, takeoff.SubBeam, takeoff.Thickness, "Deduct beam");
            AddSlabTasVolumeDeduction(ref expression, takeoff.SubStair, takeoff.Thickness, "Deduct straight flight");
            return expression;
        }

        private static void AddSlabTasVolumeDeduction(ref string expression, double area, double thickness, string label)
        {
            if (area <= 1e-9 || thickness <= 1e-9) return;
            expression += " - " + FormatTasVolume(area * thickness) + "<" + label + ">";
        }

        private static string BuildSlabTasFormworkExpression(SlabTasTakeoff takeoff)
        {
            string expression = FormatTasArea(takeoff.OriginalArea) + "<Original area>";
            AddSlabTasAreaDeduction(ref expression, takeoff.SubFoundation, "Deduct foundation");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubColumn, "Deduct column");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubBeam, "Deduct beam");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubWall, "Deduct wall");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubFloor, "Deduct slab");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubStair, "Deduct straight flight");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubGeneric, "Deduct concrete");
            return expression;
        }

        private static string BuildSlabTasProjectedAreaExpression(SlabTasTakeoff takeoff)
        {
            string expression = FormatTasArea(takeoff.OriginalArea) + "<Original projected area>";
            AddSlabTasAreaDeduction(ref expression, takeoff.SubWall, "Deduct wall");
            AddSlabTasAreaDeduction(ref expression, takeoff.SubStair, "Deduct straight flight");
            return expression;
        }

        private static string BuildSlabTasEdgeExpression(SlabTasTakeoff takeoff)
        {
            double edgeArea = takeoff.EdgeBreakLength * takeoff.Thickness;
            return FormatTasArea(edgeArea) + "<Edge/break side formwork> / " +
                   FormatTasLength(takeoff.Thickness) + "<Thickness>";
        }

        private static string BuildSlabTasSoffitName(Element element)
        {
            double strutHeight = GetParamDouble(element, new[] { "FWK.Floor.StrutHeight" });
            double meters = strutHeight > 1e-9 ? ToMeters(strutHeight) : 0.0;
            if (meters <= 1e-9) return "Area of formwork to soffit";

            double halfMeterLimit = Math.Ceiling(meters * 2.0 - 1e-9) / 2.0;
            return "Area of formwork to soffit(<=" + halfMeterLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m)";
        }

        private static void AddSlabTasAreaDeduction(ref string expression, double area, string label)
        {
            if (area <= 1e-9) return;
            expression += " - " + FormatTasArea(area) + "<" + label + ">";
        }

        private static bool TryBuildColumnTasTakeoff(Element element, string ruleCode, out ColumnTasTakeoff takeoff)
        {
            takeoff = null;
            if (element == null) return false;
            if (!TryGetColumnSectionDimensions(element, out double width, out double depth)) return false;

            double height = GetElementVerticalHeight(element);
            if (height <= 1e-9)
            {
                double volume = GetElementVolume(element);
                if (volume > 1e-9 && width > 1e-9 && depth > 1e-9)
                {
                    height = volume / (width * depth);
                }
            }

            if (height <= 1e-9) return false;

            double sideArea = 2.0 * (width + depth) * height;
            if (TryGetParamDouble(element, "FWK.Col.Sides", out double storedSideArea) && storedSideArea > 1e-9)
            {
                sideArea = storedSideArea;
            }

            bool includeTopBottom = IsRuleActive(ruleCode, "COL.TOPBOTTOM", false);
            double topBottomArea = includeTopBottom && TryGetParamDouble(element, "FWK.Col.TopBottom", out double storedTopBottom)
                ? Math.Max(0.0, storedTopBottom)
                : 0.0;

            takeoff = new ColumnTasTakeoff
            {
                Width = width,
                Depth = depth,
                Height = height,
                SideArea = Math.Max(0.0, sideArea),
                TopBottomArea = Math.Max(0.0, topBottomArea)
            };

            return takeoff.FormworkArea > 1e-9 || takeoff.Volume > 1e-9;
        }

        private static bool TryGetColumnSectionDimensions(Element element, out double width, out double depth)
        {
            width = 0.0;
            depth = 0.0;
            if (element == null) return false;

            width = GetParamDouble(element, new[] { "CBIM_Column_Width", "Column Width", "Section Width", "Width", "b", "B", "Diameter", "D" });
            depth = GetParamDouble(element, new[] { "CBIM_Column_Depth", "Column Depth", "Section Depth", "Section Height", "Depth", "d", "D" });

            ElementType type = GetElementType(element);
            if (width <= 1e-9)
            {
                width = GetParamDouble(type, new[] { "CBIM_Column_Width", "Column Width", "Section Width", "Width", "b", "B", "Diameter", "D" });
            }

            if (depth <= 1e-9)
            {
                depth = GetParamDouble(type, new[] { "CBIM_Column_Depth", "Column Depth", "Section Depth", "Section Height", "Depth", "h", "H", "d", "D" });
            }

            if ((width <= 1e-9 || depth <= 1e-9) && TryGetColumnDimensionsFromBoundingBox(element, out double boxWidth, out double boxDepth))
            {
                if (width <= 1e-9) width = boxWidth;
                if (depth <= 1e-9) depth = boxDepth;
            }

            if ((width <= 1e-9 || depth <= 1e-9) &&
                TryParseColumnTypeDimensions(GetElementTypeName(element.Document, element), out double parsedWidth, out double parsedDepth))
            {
                if (width <= 1e-9) width = parsedWidth;
                if (depth <= 1e-9) depth = parsedDepth;
            }

            if (width <= 1e-9 && depth > 1e-9) width = depth;
            if (depth <= 1e-9 && width > 1e-9) depth = width;
            return width > 1e-9 && depth > 1e-9;
        }

        private static bool TryGetColumnDimensionsFromBoundingBox(Element element, out double width, out double depth)
        {
            width = 0.0;
            depth = 0.0;
            BoundingBoxXYZ box = element?.get_BoundingBox(null);
            if (box == null) return false;

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            if (dx <= 1e-9 || dy <= 1e-9) return false;

            width = Math.Min(dx, dy);
            depth = Math.Max(dx, dy);
            return true;
        }

        private static bool TryParseColumnTypeDimensions(string typeName, out double width, out double depth)
        {
            width = 0.0;
            depth = 0.0;
            if (string.IsNullOrWhiteSpace(typeName)) return false;

            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                typeName,
                @"(?<w>\d+(?:[\.,]\d+)?)\s*[xX]\s*(?<d>\d+(?:[\.,]\d+)?)");
            if (!match.Success) return false;

            if (!double.TryParse(match.Groups["w"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double widthMeters)) return false;
            if (!double.TryParse(match.Groups["d"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double depthMeters)) return false;
            if (widthMeters <= 0.0 || depthMeters <= 0.0) return false;

            try
            {
                width = UnitUtils.ConvertToInternalUnits(widthMeters, UnitTypeId.Meters);
                depth = UnitUtils.ConvertToInternalUnits(depthMeters, UnitTypeId.Meters);
            }
            catch
            {
                width = widthMeters / 0.3048;
                depth = depthMeters / 0.3048;
            }

            return width > 1e-9 && depth > 1e-9;
        }

        private static bool IsRuleActive(string ruleCode, string code, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(code)) return fallback;
            if (string.IsNullOrWhiteSpace(ruleCode)) return fallback;
            return ruleCode
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part.Trim(), code, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildBeamTasFormworkName(Element element)
        {
            return "Area of formwork" + BuildBeamTasHeightLimitSuffix(element);
        }

        private static string BuildBeamTasSideName(Element element)
        {
            return BuildBeamTasComponentName(element, "Area of formwork to side of beam");
        }

        private static string BuildBeamTasSoffitName(Element element)
        {
            return BuildBeamTasComponentName(element, "Area of formwork to soffit of beam");
        }

        private static string BuildBeamTasComponentName(Element element, string baseName)
        {
            return baseName + BuildBeamTasHeightLimitSuffix(element);
        }

        private static string BuildBeamTasHeightLimitSuffix(Element element)
        {
            double strutHeight = GetParamDouble(element, new[] { "FWK.Beam.StrutHeight" });
            if (strutHeight <= 1e-9) return "";

            double meters = ToMeters(strutHeight);
            if (meters <= 1e-9) return "";

            double halfMeterLimit = Math.Ceiling(meters * 2.0 - 1e-9) / 2.0;
            return "(<=" + halfMeterLimit.ToString("0.#", CultureInfo.InvariantCulture) + "m)";
        }

        private static string BuildBeamTasVolumeExpression(BeamTasTakeoff takeoff)
        {
            string expression =
                "(" + FormatTasLength(takeoff.Width) + "<Width> * " +
                FormatTasLength(takeoff.Depth) + "<Height> * " +
                FormatTasLength(takeoff.AxisLength) + "<Length of centerline>)";
            if (takeoff.ColumnVolumeDeduction > 1e-9)
            {
                expression += " - " + FormatTasVolume(takeoff.ColumnVolumeDeduction) + "<Deduct column>";
            }

            return expression;
        }

        private static string BuildBeamTasFormworkExpression(BeamTasTakeoff takeoff)
        {
            var terms = new List<string>();
            if (takeoff.LeftSideGrossArea > 1e-9)
            {
                terms.Add(FormatTasArea(takeoff.LeftSideGrossArea) + "<Original area of formwork to left side>");
            }

            if (takeoff.RightSideGrossArea > 1e-9)
            {
                terms.Add(FormatTasArea(takeoff.RightSideGrossArea) + "<Original area of formwork to right side>");
            }

            if (takeoff.BottomGrossArea > 1e-9)
            {
                terms.Add(FormatTasArea(takeoff.BottomGrossArea) + "<Original area of formwork to soffit of beam>");
            }

            if (takeoff.TopGrossArea > 1e-9)
            {
                terms.Add(FormatTasArea(takeoff.TopGrossArea) + "<Original area of formwork to top of beam>");
            }

            string expression = string.Join(" + ", terms);
            if (takeoff.ColumnFormworkDeductionArea > 1e-9)
            {
                expression += " - " + FormatTasArea(takeoff.ColumnFormworkDeductionArea) + "<Deduct column>";
            }

            return string.IsNullOrWhiteSpace(expression) ? "0" : expression;
        }

        private static string BuildBeamTasSideExpression(BeamTasTakeoff takeoff)
        {
            string expression =
                FormatTasArea(takeoff.LeftSideGrossArea) + "<Original area of formwork to left side> + " +
                FormatTasArea(takeoff.RightSideGrossArea) + "<Original area of formwork to right side>";
            if (takeoff.ColumnSideDeductionArea > 1e-9)
            {
                expression += " - " + FormatTasArea(takeoff.ColumnSideDeductionArea) + "<Deduct column>";
            }

            return expression;
        }

        private static string BuildBeamTasSoffitExpression(BeamTasTakeoff takeoff)
        {
            string expression = FormatTasArea(takeoff.BottomGrossArea) + "<Original area of formwork to soffit of beam>";
            if (takeoff.ColumnBottomDeductionArea > 1e-9)
            {
                expression += " - " + FormatTasArea(takeoff.ColumnBottomDeductionArea) + "<Deduct column>";
            }

            return expression;
        }

        private static string BuildBeamTasTopExpression(BeamTasTakeoff takeoff)
        {
            string expression = FormatTasArea(takeoff.TopGrossArea) + "<Original area of formwork to top of beam>";
            if (takeoff.ColumnTopDeductionArea > 1e-9)
            {
                expression += " - " + FormatTasArea(takeoff.ColumnTopDeductionArea) + "<Deduct column>";
            }

            return expression;
        }

        private static IEnumerable<ElementId> GetFormworkShapeIdsByFaceTypes(IList<Element> formworkShapes, params string[] faceTypes)
        {
            if (formworkShapes == null) return Enumerable.Empty<ElementId>();
            HashSet<string> filter = faceTypes == null || faceTypes.Length == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(faceTypes.Where(f => !string.IsNullOrWhiteSpace(f)), StringComparer.OrdinalIgnoreCase);

            return formworkShapes
                .Where(shape => shape?.Id != null && shape.Id != ElementId.InvalidElementId)
                .Where(shape => filter.Count == 0 || filter.Contains(GetCsvParameterValue(shape, "CBIM.FWK.FaceType")))
                .Select(shape => shape.Id)
                .ToList();
        }

        private static bool TryBuildBeamTasTakeoff(
            Document doc,
            Element element,
            IList<Element> columns,
            bool includeSide,
            bool includeBottom,
            bool includeTop,
            bool deductColumn,
            out BeamTasTakeoff takeoff)
        {
            takeoff = null;
            if (doc == null || element == null) return false;

            double length = GetParamDouble(element, new[] { "CBIM_Beam_Length" });
            if (length <= 1e-9)
            {
                length = GetElementLength(element);
            }

            double width = GetParamDouble(element, new[] { "CBIM_Beam_Width" });
            double depth = GetParamDouble(element, new[] { "CBIM_Beam_Depth" });
            if (element is FamilyInstance fi && fi.Symbol != null)
            {
                if (width <= 1e-9) width = GetTypeWidth(fi.Symbol);
                if (depth <= 1e-9) depth = GetTypeDepth(fi.Symbol);
            }

            if (length <= 1e-9 || width <= 1e-9 || depth <= 1e-9) return false;

            double columnDeductLength = deductColumn
                ? ComputeBeamColumnDeductLength(element, columns)
                : 0.0;
            columnDeductLength = Math.Min(length, Math.Max(0.0, columnDeductLength));

            double leftSideGross = includeSide ? depth * length : 0.0;
            double rightSideGross = includeSide ? depth * length : 0.0;
            double bottomGross = includeBottom ? width * length : 0.0;
            double topGross = includeTop ? width * length : 0.0;

            double sideDeduct = includeSide ? 2.0 * depth * columnDeductLength : 0.0;
            double bottomDeduct = includeBottom ? width * columnDeductLength : 0.0;
            double topDeduct = includeTop ? width * columnDeductLength : 0.0;
            double volumeDeduct = width * depth * columnDeductLength;

            takeoff = new BeamTasTakeoff
            {
                AxisLength = length,
                Width = width,
                Depth = depth,
                ColumnDeductLength = columnDeductLength,
                LeftSideGrossArea = leftSideGross,
                RightSideGrossArea = rightSideGross,
                BottomGrossArea = bottomGross,
                TopGrossArea = topGross,
                ColumnSideDeductionArea = sideDeduct,
                ColumnBottomDeductionArea = bottomDeduct,
                ColumnTopDeductionArea = topDeduct,
                ColumnVolumeDeduction = volumeDeduct
            };

            return true;
        }

        private static double ComputeBeamColumnDeductLength(Element beam, IList<Element> columns)
        {
            List<Tuple<double, double>> intervals = GetBeamColumnDeductionHits(beam, columns)
                .Select(hit => Tuple.Create(hit.StartDistance, hit.EndDistance))
                .ToList();
            return SumMergedIntervals(intervals, 0.02);
        }

        private static List<BeamColumnDeductionHit> GetBeamColumnDeductionHits(Element beam, IList<Element> columns)
        {
            var hits = new List<BeamColumnDeductionHit>();
            if (beam == null || columns == null || columns.Count == 0) return hits;
            if (!TryGetBeamAxis(beam, out XYZ start, out XYZ direction, out double length)) return hits;

            BoundingBoxXYZ hostBox = beam.get_BoundingBox(null);
            const double tol = 0.02;

            foreach (Element column in columns)
            {
                if (column == null || column.Id == null || beam.Id == null) continue;
                if (column.Id.Value == beam.Id.Value) continue;

                BoundingBoxXYZ columnBox = column.get_BoundingBox(null);
                if (!BoundingBoxesIntersectOrTouch(hostBox, columnBox, tol)) continue;

                double min = double.MaxValue;
                double max = double.MinValue;
                foreach (XYZ corner in GetBoundingBoxCorners(columnBox))
                {
                    double t = (corner - start).DotProduct(direction);
                    if (t < min) min = t;
                    if (t > max) max = t;
                }

                double a = Math.Max(0.0, min);
                double b = Math.Min(length, max);
                if (b - a > tol)
                {
                    hits.Add(new BeamColumnDeductionHit(column, a, b));
                }
            }

            return hits;
        }

        private static bool TryGetBeamAxis(Element beam, out XYZ start, out XYZ direction, out double length)
        {
            start = null;
            direction = null;
            length = 0.0;

            LocationCurve lc = beam?.Location as LocationCurve;
            Curve curve = lc?.Curve;
            if (curve == null) return false;

            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);
            if (p0 == null || p1 == null) return false;

            XYZ vector = p1 - p0;
            length = curve.Length > 1e-9 ? curve.Length : vector.GetLength();
            if (length <= 1e-9) return false;

            start = p0;
            direction = vector.GetLength() > 1e-9 ? vector.Normalize() : null;
            return direction != null;
        }

        private static bool BoundingBoxesIntersectOrTouch(BoundingBoxXYZ a, BoundingBoxXYZ b, double tolerance)
        {
            if (a == null || b == null) return false;
            double tol = Math.Max(0.0, tolerance);
            return a.Min.X <= b.Max.X + tol && a.Max.X + tol >= b.Min.X &&
                   a.Min.Y <= b.Max.Y + tol && a.Max.Y + tol >= b.Min.Y &&
                   a.Min.Z <= b.Max.Z + tol && a.Max.Z + tol >= b.Min.Z;
        }

        private static double SumMergedIntervals(List<Tuple<double, double>> intervals, double tolerance)
        {
            if (intervals == null || intervals.Count == 0) return 0.0;

            List<Tuple<double, double>> sorted = intervals
                .Where(i => i != null && i.Item2 > i.Item1)
                .OrderBy(i => i.Item1)
                .ToList();
            if (sorted.Count == 0) return 0.0;

            double tol = Math.Max(0.0, tolerance);
            double total = 0.0;
            double currentStart = sorted[0].Item1;
            double currentEnd = sorted[0].Item2;

            for (int i = 1; i < sorted.Count; i++)
            {
                Tuple<double, double> next = sorted[i];
                if (next.Item1 <= currentEnd + tol)
                {
                    currentEnd = Math.Max(currentEnd, next.Item2);
                    continue;
                }

                total += Math.Max(0.0, currentEnd - currentStart);
                currentStart = next.Item1;
                currentEnd = next.Item2;
            }

            total += Math.Max(0.0, currentEnd - currentStart);
            return total;
        }

        private static string FormatTasLength(double internalLength)
        {
            return ToMeters(internalLength).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatTasArea(double internalArea)
        {
            return ToSquareMeters(internalArea).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatTasVolume(double internalVolume)
        {
            return ToCubicMeters(internalVolume).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void AddKnownQsComponentRows(List<QsExpressionRow> rows, Element element)
        {
            if (rows == null || element == null) return;

            AddAreaParam(rows, element, "Beam gross formwork", "FWK.Beam.Total", "Gross");
            AddAreaParam(rows, element, "Beam side formwork", "FWK.Beam.Sides", "Gross");
            AddAreaParam(rows, element, "Beam bottom formwork", "FWK.Beam.Bottom", "Gross");
            AddAreaParam(rows, element, "Beam top formwork", "FWK.Beam.Top", "Gross");
            AddAreaParam(rows, element, "Deduct foundation from beam", "FWK.Beam.SubFoun", "Deduction", true);
            AddAreaParam(rows, element, "Deduct beam from beam", "FWK.Beam.SubBeam", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from beam", "FWK.Beam.SubCol", "Deduction", true);
            AddAreaParam(rows, element, "Deduct wall from beam", "FWK.Beam.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct slab/floor from beam", "FWK.Beam.SubFloor", "Deduction", true);
            AddAreaParam(rows, element, "Deduct generic from beam", "FWK.Beam.SubGeneric", "Deduction", true);
            AddLengthParam(rows, element, "Beam strut height", "FWK.Beam.StrutHeight", "Stage");
            AddNumberParam(rows, element, "Beam strut stage", "FWK.Beam.StrutStage", "Stage");
            AddAreaParam(rows, element, "Beam strut basic", "FWK.Beam.Stage.Basic", "Stage");
            AddStageAreaParams(rows, element, "Beam strut stage ", "FWK.Beam.Stage.", BeamStrutPersistedStageLimit);

            AddAreaParam(rows, element, "Column gross formwork", "FWK.Col.Total", "Gross");
            AddAreaParam(rows, element, "Column side formwork", "FWK.Col.Sides", "Gross");
            AddAreaParam(rows, element, "Column top/bottom formwork", "FWK.Col.TopBottom", "Gross");
            AddAreaParam(rows, element, "Deduct foundation from column", "FWK.Col.SubFoun", "Deduction", true);
            AddAreaParam(rows, element, "Deduct beam from column", "FWK.Col.SubBeam", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from column", "FWK.Col.SubCol", "Deduction", true);
            AddAreaParam(rows, element, "Deduct wall from column", "FWK.Col.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct slab/floor from column", "FWK.Col.SubFloor", "Deduction", true);
            AddAreaParam(rows, element, "Deduct generic from column", "FWK.Col.SubGeneric", "Deduction", true);
            AddLengthParam(rows, element, "Column strut height", "FWK.Col.StrutHeight", "Stage");
            AddNumberParam(rows, element, "Column strut stage", "FWK.Col.StrutStage", "Stage");
            AddAreaParam(rows, element, "Column strut basic", "FWK.Col.Stage.Basic", "Stage");
            AddStageAreaParams(rows, element, "Column strut stage ", "FWK.Col.Stage.", ColumnStrutPersistedStageLimit);

            AddAreaParam(rows, element, "Wall gross formwork", "FWK.Wall.Total", "Gross");
            AddAreaParam(rows, element, "Wall side formwork", "FWK.Wall.Sides", "Gross");
            AddAreaParam(rows, element, "Wall opening side formwork", "FWK.Wall.OpeningSide", "Opening");
            AddAreaParam(rows, element, "Wall opening bottom formwork", "FWK.Wall.OpeningBottom", "Opening");
            AddAreaParam(rows, element, "Wall joined end cap already excluded", "FWK.Wall.JoinedEndCap", "Priority");
            AddAreaParam(rows, element, "Deduct foundation from wall", "FWK.Wall.SubFoun", "Deduction", true);
            AddAreaParam(rows, element, "Deduct beam from wall", "FWK.Wall.SubBeam", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from wall", "FWK.Wall.SubCol", "Deduction", true);
            AddAreaParam(rows, element, "Deduct wall from wall", "FWK.Wall.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct slab/floor from wall", "FWK.Wall.SubFloor", "Deduction", true);
            AddAreaParam(rows, element, "Deduct generic from wall", "FWK.Wall.SubGeneric", "Deduction", true);
            AddLengthParam(rows, element, "Wall original height", "FWK.Wall.OriginalHeight", "Base");
            AddStageLengthParams(rows, element, "Wall edge length stage ", "FWK.Wall.EdgeLength.Stage.", WallEdgePersistedStageLimit);
            AddStageAreaParams(rows, element, "Wall edge area stage ", "FWK.Wall.EdgeArea.Stage.", WallEdgePersistedStageLimit);

            AddAreaParam(rows, element, "Slab gross formwork", "FWK.Floor.Total", "Gross");
            AddAreaParam(rows, element, "Slab soffit formwork", "FWK.Floor.Bottom", "Gross");
            AddAreaParam(rows, element, "Slab side formwork", "FWK.Floor.Sides", "Gross");
            AddAreaParam(rows, element, "Slab opening side formwork", "FWK.Floor.OpeningSide", "Opening");
            AddNumberParam(rows, element, "Slab opening count", "FWK.Floor.Opening.Count", "Opening");
            AddAreaParam(rows, element, "Slab opening area", "FWK.Floor.Opening.Area", "Opening");
            AddLengthParam(rows, element, "Slab opening girth", "FWK.Floor.Opening.Girth", "Opening");
            AddAreaParam(rows, element, "Slab edge <= 250", "FWK.Floor.EdgeBreak.Lte250", "Edge");
            AddAreaParam(rows, element, "Slab edge <= 500", "FWK.Floor.EdgeBreak.Lte500", "Edge");
            AddAreaParam(rows, element, "Slab edge <= 1000", "FWK.Floor.EdgeBreak.Lte1000", "Edge");
            AddAreaParam(rows, element, "Slab edge > 1000", "FWK.Floor.EdgeBreak.Over1000", "Edge");
            AddAreaParam(rows, element, "Slab top slope formwork", "FWK.Floor.TopSlope", "Gross");
            AddLengthParam(rows, element, "Slab strut height", "FWK.Floor.StrutHeight", "Stage");
            AddAreaParam(rows, element, "Slab strut basic", "FWK.Floor.StrutBasic", "Stage");
            AddAreaParam(rows, element, "Slab strut soffit", "FWK.Floor.StrutSoffit", "Stage");
            AddNumberParam(rows, element, "Slab strut stage count", "FWK.Floor.StrutStageCount", "Stage");
            AddAreaParam(rows, element, "Slab strut stage area", "FWK.Floor.StrutStageArea", "Stage");
            AddStageAreaParams(rows, element, "Slab strut stage ", "FWK.Floor.StrutStage.", SlabStrutPersistedStageLimit);
            AddAreaParam(rows, element, "Slab strut edge", "FWK.Floor.StrutEdge", "Stage");
            AddAreaParam(rows, element, "Slab strut edge stage", "FWK.Floor.StrutEdgeStageArea", "Stage");
            AddAreaParam(rows, element, "Slab strut top", "FWK.Floor.StrutTop", "Stage");
            AddAreaParam(rows, element, "Slab strut top stage", "FWK.Floor.StrutTopStageArea", "Stage");
            AddAreaParam(rows, element, "Deduct foundation from slab", "FWK.Floor.SubFoun", "Deduction", true);
            AddAreaParam(rows, element, "Deduct beam from slab", "FWK.Floor.SubBeam", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from slab", "FWK.Floor.SubCol", "Deduction", true);
            AddAreaParam(rows, element, "Deduct wall from slab", "FWK.Floor.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct slab/floor from slab", "FWK.Floor.SubFloor", "Deduction", true);
            AddAreaParam(rows, element, "Deduct straight flight from slab", "FWK.Floor.SubStair", "Deduction", true);
            AddAreaParam(rows, element, "Deduct generic from slab", "FWK.Floor.SubGeneric", "Deduction", true);

            AddAreaParam(rows, element, "Stair gross formwork", "FWK.Stair.Total", "Gross");
            AddAreaParam(rows, element, "Stair painting/formwork", "FWK.Stair.Painting", "Gross");
            AddAreaParam(rows, element, "Stair side formwork", "FWK.Stair.Sides", "Gross");
            AddAreaParam(rows, element, "Stair painting side/riser faces", "FWK.Stair.PaintingSide", "Gross");
            AddAreaParam(rows, element, "Stair painting bottom/soffit faces", "FWK.Stair.PaintingBottom", "Gross");
            AddAreaParam(rows, element, "Stair bottom formwork", "FWK.Stair.Bottom", "Gross");
            AddAreaParam(rows, element, "Stair top formwork", "FWK.Stair.Top", "Gross");
            AddNumberParam(rows, element, "Stair step count", "FWK.Stair.StepCount", "Base");
            AddAreaParam(rows, element, "Deduct foundation from stair", "FWK.Stair.SubFoun", "Deduction", true);
            AddAreaParam(rows, element, "Deduct beam from stair", "FWK.Stair.SubBeam", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from stair", "FWK.Stair.SubCol", "Deduction", true);
            AddAreaParam(rows, element, "Deduct wall from stair", "FWK.Stair.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct slab/floor from stair", "FWK.Stair.SubFloor", "Deduction", true);
            AddAreaParam(rows, element, "Deduct generic from stair", "FWK.Stair.SubGeneric", "Deduction", true);

            AddAreaParam(rows, element, "Foundation gross formwork", "FWK.Foun.Total", "Gross");
            AddAreaParam(rows, element, "Foundation side formwork", "FWK.Foun.Sides", "Gross");
            AddAreaParam(rows, element, "Foundation top formwork", "FWK.Foun.Top", "Gross");
            AddLengthParam(rows, element, "Foundation side length total", "FWK.Foun.SideLength.Total", "Stage");
            AddAreaParam(rows, element, "Foundation side area staged", "FWK.Foun.SideArea.Staged", "Stage");
            AddStageLengthParams(rows, element, "Foundation side length stage ", "FWK.Foun.SideLength.Stage.", FoundationSidePersistedStageLimit);
            AddStageAreaParams(rows, element, "Foundation side area stage ", "FWK.Foun.SideArea.Stage.", FoundationSidePersistedStageLimit);
            AddAreaParam(rows, element, "Deduct foundation from foundation", "FWK.Foun.SubFoun", "Deduction", true);
            AddAreaParam(rows, element, "Deduct beam from foundation", "FWK.Foun.SubBeam", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from foundation", "FWK.Foun.SubCol", "Deduction", true);
            AddAreaParam(rows, element, "Deduct wall from foundation", "FWK.Foun.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct slab/floor from foundation", "FWK.Foun.SubFloor", "Deduction", true);
            AddAreaParam(rows, element, "Deduct generic from foundation", "FWK.Foun.SubGeneric", "Deduction", true);

            AddAreaParam(rows, element, "Kerb gross formwork", "FWK.Kerb.Total", "Gross");
            AddAreaParam(rows, element, "Kerb side formwork", "FWK.Kerb.Sides", "Gross");
            AddAreaParam(rows, element, "Kerb top formwork", "FWK.Kerb.Top", "Gross");
            AddLengthParam(rows, element, "Kerb length", "FWK.Kerb.Length", "Base");
            AddAreaParam(rows, element, "Deduct wall from kerb", "FWK.Kerb.SubWall", "Deduction", true);
            AddAreaParam(rows, element, "Deduct column from kerb", "FWK.Kerb.SubCol", "Deduction", true);

            AddAreaParam(rows, element, "Other concrete gross formwork", "FWK.Other.Total", "Gross");
            AddAreaParam(rows, element, "Other concrete side formwork", "FWK.Other.Sides", "Gross");
            AddAreaParam(rows, element, "Other concrete bottom formwork", "FWK.Other.Bottom", "Gross");
            AddAreaParam(rows, element, "Deduct structure from other concrete", "FWK.Other.SubStructure", "Deduction", true);

            AddAreaParam(rows, element, "Lintel side formwork", "FWK.Lintel.Sides", "Gross");
            AddAreaParam(rows, element, "Lintel bottom formwork", "FWK.Lintel.Bottom", "Gross");
            AddLengthParam(rows, element, "Lintel length", "FWK.Lintel.Length", "Base");
            AddAreaParam(rows, element, "Drop panel soffit", "FWK.Drop.Soffit", "Gross");
            AddAreaParam(rows, element, "Drop panel stage 1", "FWK.Drop.Stage.1", "Stage");
            AddAreaParam(rows, element, "Eave bottom formwork", "FWK.Eave.Bottom", "Gross");
            AddAreaParam(rows, element, "Eave edge formwork", "FWK.Eave.Edge", "Gross");

            AddAreaParam(rows, element, "Finish gross area", "CBIM_QsFinishGrossArea", "Finish");
            AddAreaParam(rows, element, "Finish net area", "CBIM_QsFinishArea", "Finish");
            AddVolumeParam(rows, element, "Soil excavation volume", SoilExcavationVolumeParamName, "Soil");
            AddVolumeParam(rows, element, "Soil backfilled volume", SoilBackfilledVolumeParamName, "Soil");
        }

        private static void AddFormworkShapeRows(List<QsExpressionRow> rows, IList<Element> formworkShapes)
        {
            if (rows == null || formworkShapes == null || formworkShapes.Count == 0) return;

            foreach (IGrouping<string, Element> group in formworkShapes.GroupBy(s =>
                     {
                         string faceType = GetCsvParameterValue(s, "CBIM.FWK.FaceType");
                         return string.IsNullOrWhiteSpace(faceType) ? "Unknown" : faceType.Trim();
                     }).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                double area = 0.0;
                int count = 0;
                foreach (Element shape in group)
                {
                    if (TryGetParamDouble(shape, "CBIM.FWK.FaceArea", out double faceArea))
                    {
                        area += faceArea;
                    }

                    count++;
                }

                if (area <= 1e-9) continue;

                AddQuantityRow(
                    rows,
                    "3D formwork shape: " + group.Key,
                    "Sum(CBIM.FWK.FaceArea where FaceType = " + group.Key + ")",
                    ToSquareMeters(area),
                    "m2",
                    "3D Shape",
                    count.ToString(CultureInfo.InvariantCulture) + " shape(s)",
                    group.Select(s => s?.Id));
            }
        }

        private static void AddLive3dDeductionRows(Document doc, Element host, IList<Element> formworkShapes, List<QsExpressionRow> rows)
        {
            if (doc == null || host == null || formworkShapes == null || formworkShapes.Count == 0 || rows == null) return;

            List<QsDeductionProbe> probes = GetActiveDeductionProbes(host).ToList();
            if (probes.Count == 0) return;

            var opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            List<FaceSlab> slabs = BuildFormworkShapeSlabs(formworkShapes, opt);
            if (slabs.Count == 0) return;

            BoundingBoxXYZ hostBox = host.get_BoundingBox(null);
            foreach (QsDeductionProbe probe in probes)
            {
                if (IsBeamTasColumnDeduction(host, probe))
                {
                    AddBeamTasColumnDeductionRows(doc, host, probe, rows);
                    continue;
                }

                List<Element> candidates = CollectElementsOfCategory(doc, probe.Category);
                foreach (Element candidate in candidates)
                {
                    if (candidate == null || candidate.Id == null) continue;
                    if (host.Id != null && candidate.Id.Value == host.Id.Value) continue;
                    if (hostBox != null && !BoundingBoxesIntersect(hostBox, candidate.get_BoundingBox(null))) continue;

                    List<Solid> candidateSolids = GetElementSolids(candidate, opt);
                    if (candidateSolids.Count == 0) continue;

                    double hitArea = 0.0;
                    foreach (FaceSlab slab in slabs)
                    {
                        foreach (Solid solid in candidateSolids)
                        {
                            hitArea += IntersectAreaFromSlab(slab, solid);
                        }
                    }

                    if (hitArea <= 1e-9) continue;

                    string idText = candidate.Id.Value.ToString(CultureInfo.InvariantCulture);
                    string typeName = GetElementTypeName(doc, candidate);
                    AddQuantityRow(
                        rows,
                        "3D deduct " + probe.Label + " #" + idText,
                        "-Intersect(3D formwork, " + probe.Label + " #" + idText + ")",
                        -ToSquareMeters(hitArea),
                        "m2",
                        "3D Deduct",
                        CombineRemarks("Stored bucket: " + probe.ParamName, string.IsNullOrWhiteSpace(typeName) ? "" : "Type: " + typeName),
                        new[] { candidate.Id });
                }
            }
        }

        private static bool IsBeamTasColumnDeduction(Element host, QsDeductionProbe probe)
        {
            if (host == null || probe == null) return false;
            if (!ResolveTasRuleCategories(host).Contains("Beam")) return false;
            return string.Equals(probe.ParamName, "FWK.Beam.SubCol", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddBeamTasColumnDeductionRows(Document doc, Element host, QsDeductionProbe probe, List<QsExpressionRow> rows)
        {
            if (doc == null || host == null || rows == null) return;

            string ruleCode = GetCsvParameterValue(host, "CBIM_QsRuleCode");
            bool includeSide = IsRuleActive(ruleCode, "BEAM.SIDE", true);
            bool includeBottom = IsRuleActive(ruleCode, "BEAM.BOTTOM", true);
            bool includeTop = IsRuleActive(ruleCode, "BEAM.TOP", false);

            double width = GetParamDouble(host, new[] { "CBIM_Beam_Width" });
            double depth = GetParamDouble(host, new[] { "CBIM_Beam_Depth" });
            if (host is FamilyInstance fi && fi.Symbol != null)
            {
                if (width <= 1e-9) width = GetTypeWidth(fi.Symbol);
                if (depth <= 1e-9) depth = GetTypeDepth(fi.Symbol);
            }

            if (width <= 1e-9 || depth <= 1e-9) return;

            double areaPerLength = 0.0;
            if (includeSide) areaPerLength += 2.0 * depth;
            if (includeBottom) areaPerLength += width;
            if (includeTop) areaPerLength += width;
            if (areaPerLength <= 1e-9) return;

            foreach (BeamColumnDeductionHit hit in GetBeamColumnDeductionHits(host, CollectElementsOfCategory(doc, BuiltInCategory.OST_StructuralColumns)))
            {
                if (hit?.Column == null || hit.Length <= 1e-9) continue;

                string idText = hit.Column.Id.Value.ToString(CultureInfo.InvariantCulture);
                string typeName = GetElementTypeName(doc, hit.Column);
                double hitArea = areaPerLength * hit.Length;
                string expression = "-((";
                bool hasPrevious = false;
                if (includeSide)
                {
                    expression += "2 * " + FormatTasLength(depth) + "<Height>";
                    hasPrevious = true;
                }

                if (includeBottom)
                {
                    expression += (hasPrevious ? " + " : "") + FormatTasLength(width) + "<Soffit width>";
                    hasPrevious = true;
                }

                if (includeTop)
                {
                    expression += (hasPrevious ? " + " : "") + FormatTasLength(width) + "<Top width>";
                }

                expression += ") * " + FormatTasLength(hit.Length) + "<Deduct column length>)";

                AddQuantityRow(
                    rows,
                    "TAS deduct Column #" + idText,
                    expression,
                    -ToSquareMeters(hitArea),
                    "m2",
                    "3D Deduct",
                    CombineRemarks("Stored bucket: " + probe.ParamName, string.IsNullOrWhiteSpace(typeName) ? "" : "Type: " + typeName),
                    new[] { hit.Column.Id });
            }
        }

        private static IEnumerable<QsDeductionProbe> GetActiveDeductionProbes(Element element)
        {
            if (element == null) yield break;

            foreach (QsDeductionProbe probe in GetDeductionProbeDefinitions())
            {
                if (!TryGetParamDouble(element, probe.ParamName, out double value)) continue;
                if (value <= 1e-9) continue;
                yield return probe;
            }
        }

        private static IEnumerable<QsDeductionProbe> GetDeductionProbeDefinitions()
        {
            string[] prefixes =
            {
                "FWK.Beam",
                "FWK.Col",
                "FWK.Wall",
                "FWK.Floor",
                "FWK.Stair",
                "FWK.Foun"
            };

            foreach (string prefix in prefixes)
            {
                yield return new QsDeductionProbe(BuiltInCategory.OST_StructuralFoundation, "Foundation", prefix + ".SubFoun");
                yield return new QsDeductionProbe(BuiltInCategory.OST_StructuralFraming, "Beam", prefix + ".SubBeam");
                yield return new QsDeductionProbe(BuiltInCategory.OST_StructuralColumns, "Column", prefix + ".SubCol");
                yield return new QsDeductionProbe(BuiltInCategory.OST_Walls, "Wall", prefix + ".SubWall");
                yield return new QsDeductionProbe(BuiltInCategory.OST_Floors, "Slab/Floor", prefix + ".SubFloor");
                yield return new QsDeductionProbe(BuiltInCategory.OST_GenericModel, "Generic", prefix + ".SubGeneric");
            }

            yield return new QsDeductionProbe(BuiltInCategory.OST_Stairs, "Straight Flight", "FWK.Floor.SubStair");
            yield return new QsDeductionProbe(BuiltInCategory.OST_Walls, "Wall", "FWK.Kerb.SubWall");
            yield return new QsDeductionProbe(BuiltInCategory.OST_StructuralColumns, "Column", "FWK.Kerb.SubCol");
        }

        private static List<FaceSlab> BuildFormworkShapeSlabs(IList<Element> formworkShapes, Options opt)
        {
            var slabs = new List<FaceSlab>();
            if (formworkShapes == null) return slabs;

            double thickness = FormworkHalfThicknessFt * 2.0;
            foreach (Element shape in formworkShapes)
            {
                string faceType = GetCsvParameterValue(shape, "CBIM.FWK.FaceType");
                double faceArea = TryGetParamDouble(shape, "CBIM.FWK.FaceArea", out double storedArea) ? storedArea : 0.0;
                foreach (Solid solid in GetElementSolids(shape, opt))
                {
                    if (solid == null || solid.Volume <= 1e-9) continue;
                    slabs.Add(new FaceSlab(solid, thickness, faceType, faceArea));
                }
            }

            return slabs;
        }

        private static List<Element> CollectElementsOfCategory(Document doc, BuiltInCategory category)
        {
            if (doc == null) return new List<Element>();
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e != null)
                    .ToList();
            }
            catch
            {
                return new List<Element>();
            }
        }

        private static List<Element> CollectFormworkShapesForHost(Document doc, Element host)
        {
            if (doc == null || host == null || host.Id == null) return new List<Element>();

            string hostId = host.Id.Value.ToString(CultureInfo.InvariantCulture);
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(IsCbimFormworkShape)
                .Where(e => string.Equals(GetCsvParameterValue(e, "CBIM.FWK.ElementId"), hostId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static void AddStageAreaParams(List<QsExpressionRow> rows, Element element, string labelPrefix, string paramPrefix, int maxStage)
        {
            for (int stage = 1; stage <= maxStage; stage++)
            {
                AddAreaParam(
                    rows,
                    element,
                    labelPrefix + stage.ToString(CultureInfo.InvariantCulture),
                    paramPrefix + stage.ToString(CultureInfo.InvariantCulture),
                    "Stage");
            }
        }

        private static void AddStageLengthParams(List<QsExpressionRow> rows, Element element, string labelPrefix, string paramPrefix, int maxStage)
        {
            for (int stage = 1; stage <= maxStage; stage++)
            {
                AddLengthParam(
                    rows,
                    element,
                    labelPrefix + stage.ToString(CultureInfo.InvariantCulture),
                    paramPrefix + stage.ToString(CultureInfo.InvariantCulture),
                    "Stage");
            }
        }

        private static void AddAreaParam(
            List<QsExpressionRow> rows,
            Element element,
            string quantityName,
            string paramName,
            string countTag,
            bool deduction = false,
            bool includeZero = false)
        {
            if (!TryGetParamDouble(element, paramName, out double internalValue)) return;
            if (!includeZero && Math.Abs(internalValue) <= 1e-9) return;

            double quantity = ToSquareMeters(internalValue);
            string expression = paramName;
            if (deduction)
            {
                quantity = -Math.Abs(quantity);
                expression = "-" + paramName;
            }

            AddQuantityRow(rows, quantityName, expression, quantity, "m2", countTag, paramName);
        }

        private static void AddLengthParam(
            List<QsExpressionRow> rows,
            Element element,
            string quantityName,
            string paramName,
            string countTag)
        {
            if (!TryGetParamDouble(element, paramName, out double internalValue)) return;
            if (Math.Abs(internalValue) <= 1e-9) return;
            AddQuantityRow(rows, quantityName, paramName, ToMeters(internalValue), "m", countTag, paramName);
        }

        private static void AddVolumeParam(
            List<QsExpressionRow> rows,
            Element element,
            string quantityName,
            string paramName,
            string countTag)
        {
            if (!TryGetParamDouble(element, paramName, out double internalValue)) return;
            if (Math.Abs(internalValue) <= 1e-9) return;
            AddQuantityRow(rows, quantityName, paramName, ToCubicMeters(internalValue), "m3", countTag, paramName);
        }

        private static void AddNumberParam(
            List<QsExpressionRow> rows,
            Element element,
            string quantityName,
            string paramName,
            string countTag)
        {
            if (!TryGetParamDouble(element, paramName, out double value)) return;
            if (Math.Abs(value) <= 1e-9) return;
            AddQuantityRow(rows, quantityName, paramName, value, "ea", countTag, paramName);
        }

        private static void AddQuantityRow(
            List<QsExpressionRow> rows,
            string quantityName,
            string expression,
            double quantity,
            string unit,
            string countTag,
            string remarks,
            IEnumerable<ElementId> modelElementIds = null)
        {
            if (rows == null) return;
            rows.Add(new QsExpressionRow
            {
                QuantityName = quantityName ?? "",
                QuantityExpression = expression ?? "",
                Quantity = FormatExpressionQuantity(quantity),
                Unit = unit ?? "",
                CountTag = countTag ?? "",
                Remarks = remarks ?? "",
                RevitElementIds = NormalizeExpressionElementIds(modelElementIds)
            });
        }

        private static List<long> NormalizeExpressionElementIds(IEnumerable<ElementId> ids)
        {
            if (ids == null) return new List<long>();
            return ids
                .Where(id => id != null && id != ElementId.InvalidElementId && id.Value > 0)
                .Select(id => id.Value)
                .Distinct()
                .ToList();
        }

        private static void AddDefaultExpressionTargets(List<QsExpressionRow> rows, Element element)
        {
            if (rows == null || element?.Id == null || element.Id == ElementId.InvalidElementId) return;

            long hostId = element.Id.Value;
            if (hostId <= 0) return;
            foreach (QsExpressionRow row in rows)
            {
                if (row == null) continue;
                if (row.RevitElementIds == null)
                {
                    row.RevitElementIds = new List<long>();
                }

                if (row.RevitElementIds.Count == 0)
                {
                    row.RevitElementIds.Add(hostId);
                }
            }
        }

        private static void ApplyTasLikeFormworkTargets(List<QsExpressionRow> rows, IList<Element> formworkShapes)
        {
            if (rows == null || formworkShapes == null || formworkShapes.Count == 0) return;

            foreach (QsExpressionRow row in rows)
            {
                if (row == null) continue;
                if (row.RevitElementIds != null && row.RevitElementIds.Count > 0) continue;

                string paramName = ResolveExpressionTargetParamName(row.QuantityExpression);
                List<ElementId> targetIds = ResolveFormworkShapeTargets(formworkShapes, paramName, row.QuantityName, row.CountTag);
                if (targetIds.Count > 0)
                {
                    row.RevitElementIds = NormalizeExpressionElementIds(targetIds);
                }
            }
        }

        private static string ResolveExpressionTargetParamName(string expression)
        {
            string value = (expression ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return "";

            if (value.StartsWith("-", StringComparison.Ordinal))
            {
                value = value.Substring(1).Trim();
            }

            int equalsIndex = value.IndexOf('=');
            if (equalsIndex >= 0)
            {
                value = value.Substring(0, equalsIndex).Trim();
            }

            int spaceIndex = value.IndexOf(' ');
            if (spaceIndex > 0)
            {
                value = value.Substring(0, spaceIndex).Trim();
            }

            return value;
        }

        private static List<ElementId> ResolveFormworkShapeTargets(
            IList<Element> formworkShapes,
            string paramName,
            string quantityName,
            string countTag)
        {
            var result = new List<ElementId>();
            if (formworkShapes == null || formworkShapes.Count == 0) return result;

            string key = ((paramName ?? "") + " " + (quantityName ?? "") + " " + (countTag ?? "")).Trim();
            if (string.IsNullOrWhiteSpace(key)) return result;
            if (ContainsIgnoreCase(key, ".Sub") || ContainsIgnoreCase(key, "Deduct") || ContainsIgnoreCase(key, "Finish") || ContainsIgnoreCase(key, "Soil"))
            {
                return result;
            }

            HashSet<string> faceTypes = ResolveTargetFaceTypes(key);
            IEnumerable<Element> targets = formworkShapes;
            if (faceTypes.Count > 0)
            {
                targets = targets.Where(shape => MatchesTargetFaceType(GetCsvParameterValue(shape, "CBIM.FWK.FaceType"), faceTypes));
            }
            else if (!ContainsIgnoreCase(key, "CBIM_FormworkArea") && !ContainsIgnoreCase(key, ".Total") && !ContainsIgnoreCase(key, "gross formwork"))
            {
                return result;
            }

            foreach (Element target in targets)
            {
                if (target?.Id != null && target.Id != ElementId.InvalidElementId)
                {
                    result.Add(target.Id);
                }
            }

            return result;
        }

        private static HashSet<string> ResolveTargetFaceTypes(string key)
        {
            var faceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (ContainsIgnoreCase(key, "TopBottom"))
            {
                faceTypes.Add("Top");
                faceTypes.Add("TopSlope");
                faceTypes.Add("Bottom");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "OpeningBottom"))
            {
                faceTypes.Add("OpeningBottom");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "OpeningSide"))
            {
                faceTypes.Add("OpeningSide");
                faceTypes.Add("Side");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "Painting"))
            {
                faceTypes.Add("Painting");
                faceTypes.Add("PaintingSide");
                faceTypes.Add("PaintingBottom");
                faceTypes.Add("Side");
                faceTypes.Add("Bottom");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "Bottom") || ContainsIgnoreCase(key, "Soffit"))
            {
                faceTypes.Add("Bottom");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "TopSlope"))
            {
                faceTypes.Add("TopSlope");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "Top"))
            {
                faceTypes.Add("Top");
                faceTypes.Add("TopSlope");
                return faceTypes;
            }

            if (ContainsIgnoreCase(key, "Sides") ||
                ContainsIgnoreCase(key, "Side") ||
                ContainsIgnoreCase(key, "Edge") ||
                ContainsIgnoreCase(key, "Stage"))
            {
                faceTypes.Add("Side");
            }

            return faceTypes;
        }

        private static bool MatchesTargetFaceType(string faceType, HashSet<string> targetFaceTypes)
        {
            if (targetFaceTypes == null || targetFaceTypes.Count == 0) return true;
            string normalized = string.IsNullOrWhiteSpace(faceType) ? "Unknown" : faceType.Trim();
            return targetFaceTypes.Contains(normalized);
        }

        private static bool ContainsIgnoreCase(string value, string token)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(token) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ShowQsExpressionWindow(
            CamboBIMWindow owner,
            UIDocument uidoc,
            Document doc,
            Element element,
            List<QsExpressionRow> rows,
            string selectionNote)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                SelectionMode = DataGridSelectionMode.Extended,
                ItemsSource = rows,
                Margin = new Thickness(0, 8, 0, 8)
            };

            AddExpressionColumn(grid, "Quantity Name", nameof(QsExpressionRow.QuantityName), 180, true);
            AddExpressionColumn(grid, "Quantity Expression", nameof(QsExpressionRow.QuantityExpression), 330, true);
            AddExpressionColumn(grid, "Quantity", nameof(QsExpressionRow.Quantity), 90, false);
            AddExpressionColumn(grid, "Unit", nameof(QsExpressionRow.Unit), 60, false);
            AddExpressionColumn(grid, "Count Tag", nameof(QsExpressionRow.CountTag), 90, true);

            string categoryName = element?.Category?.Name ?? "";
            string elementId = element?.Id?.Value.ToString(CultureInfo.InvariantCulture) ?? "";
            string typeName = GetElementTypeName(doc, element);
            string titleText = "Element " + elementId;
            if (!string.IsNullOrWhiteSpace(categoryName)) titleText += " | " + categoryName;
            if (!string.IsNullOrWhiteSpace(typeName)) titleText += " | " + typeName;

            var title = new TextBlock
            {
                Text = titleText,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black,
                TextWrapping = TextWrapping.Wrap
            };

            var note = new TextBlock
            {
                Text = selectionNote ?? "",
                Foreground = System.Windows.Media.Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = string.IsNullOrWhiteSpace(selectionNote) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible
            };

            var top = new StackPanel();
            top.Children.Add(title);
            top.Children.Add(note);

            var copyButton = new Button
            {
                Width = 100,
                Height = 26,
                Content = "Copy All",
                Margin = new Thickness(0, 0, 8, 0)
            };

            var closeButton = new Button
            {
                Width = 90,
                Height = 26,
                Content = "Close"
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(copyButton);
            buttons.Children.Add(closeButton);

            TextBlock detailName = CreateExpressionDetailValue();
            TextBlock detailExpression = CreateExpressionDetailValue();
            TextBlock detailQuantity = CreateExpressionDetailValue();
            TextBlock detailTargets = CreateExpressionDetailValue();
            TextBlock detailRemarks = CreateExpressionDetailValue();
            var selectRowButton = new Button
            {
                Width = 92,
                Height = 26,
                Content = "Select",
                Margin = new Thickness(0, 0, 8, 0)
            };
            var copyRowButton = new Button
            {
                Width = 92,
                Height = 26,
                Content = "Copy Row",
                Margin = new Thickness(0, 0, 8, 0)
            };
            var toggleShapesButton = new Button
            {
                Width = 150,
                Height = 26,
                Content = "Hide/Show Shapes"
            };

            var detailActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            detailActions.Children.Add(selectRowButton);
            detailActions.Children.Add(copyRowButton);
            detailActions.Children.Add(toggleShapesButton);

            var detailPanel = new StackPanel
            {
                Margin = new Thickness(10)
            };
            detailPanel.Children.Add(new TextBlock
            {
                Text = "Selected Quantity",
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black,
                Margin = new Thickness(0, 0, 0, 10)
            });
            detailPanel.Children.Add(detailActions);
            detailPanel.Children.Add(CreateExpressionDetailBlock("Quantity", detailName));
            detailPanel.Children.Add(CreateExpressionDetailBlock("Expression", detailExpression));
            detailPanel.Children.Add(CreateExpressionDetailBlock("Result", detailQuantity));
            detailPanel.Children.Add(CreateExpressionDetailBlock("Model Targets", detailTargets));
            detailPanel.Children.Add(CreateExpressionDetailBlock("Remarks", detailRemarks));

            var detailScroll = new ScrollViewer
            {
                Content = detailPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var detailBorder = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.LightSteelBlue,
                BorderThickness = new Thickness(1),
                Background = System.Windows.Media.Brushes.WhiteSmoke,
                Margin = new Thickness(8, 8, 0, 8),
                Child = detailScroll
            };

            var body = new System.Windows.Controls.Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 620 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370), MinWidth = 300 });
            System.Windows.Controls.Grid.SetColumn(grid, 0);
            System.Windows.Controls.Grid.SetColumn(detailBorder, 2);
            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = System.Windows.Media.Brushes.Gainsboro
            };
            System.Windows.Controls.Grid.SetColumn(splitter, 1);
            body.Children.Add(grid);
            body.Children.Add(splitter);
            body.Children.Add(detailBorder);

            var root = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(12)
            };
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(top);
            root.Children.Add(buttons);
            root.Children.Add(body);

            var dialog = new Window
            {
                Title = "CBIM-QS View Expression",
                Width = 1280,
                Height = 640,
                MinWidth = 940,
                MinHeight = 440,
                Content = root,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
            };

            if (owner != null)
            {
                dialog.Owner = owner;
            }

            copyButton.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(BuildExpressionClipboard(rows));
                    owner?.ShowStatus("QS expression copied to clipboard.");
                }
                catch (Exception ex)
                {
                    owner?.ShowStatus("Copy expression failed: " + ex.Message);
                }
            };

            var selectionHandler = new QsExpressionSelectionExternalEventHandler();
            ExternalEvent selectionEvent = ExternalEvent.Create(selectionHandler);
            var visibilityHandler = new QsFormworkVisibilityExternalEventHandler();
            ExternalEvent visibilityEvent = ExternalEvent.Create(visibilityHandler);
            grid.SelectionChanged += (s, e) =>
            {
                QueueExpressionRowsInModel(selectionHandler, selectionEvent, grid.SelectedItems.OfType<QsExpressionRow>(), owner);
                UpdateExpressionDetailPanel(
                    grid.SelectedItem as QsExpressionRow,
                    detailName,
                    detailExpression,
                    detailQuantity,
                    detailTargets,
                    detailRemarks,
                    selectRowButton,
                    copyRowButton);
            };

            selectRowButton.Click += (s, e) =>
            {
                QueueExpressionRowsInModel(selectionHandler, selectionEvent, grid.SelectedItems.OfType<QsExpressionRow>(), owner);
            };
            copyRowButton.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(BuildExpressionRowClipboard(grid.SelectedItem as QsExpressionRow));
                    owner?.ShowStatus("QS expression row copied to clipboard.");
                }
                catch (Exception ex)
                {
                    owner?.ShowStatus("Copy expression row failed: " + ex.Message);
                }
            };
            toggleShapesButton.Click += (s, e) =>
            {
                visibilityHandler.SetOwner(owner);
                visibilityEvent.Raise();
            };

            closeButton.Click += (s, e) => dialog.Close();
            dialog.Show();
            if (rows.Count > 0)
            {
                grid.SelectedIndex = 0;
            }
            else
            {
                UpdateExpressionDetailPanel(null, detailName, detailExpression, detailQuantity, detailTargets, detailRemarks, selectRowButton, copyRowButton);
            }
        }

        private static TextBlock CreateExpressionDetailValue()
        {
            return new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
        }

        private static FrameworkElement CreateExpressionDetailBlock(string label, TextBlock value)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.DimGray
            });
            panel.Children.Add(value);
            return panel;
        }

        private static void UpdateExpressionDetailPanel(
            QsExpressionRow row,
            TextBlock detailName,
            TextBlock detailExpression,
            TextBlock detailQuantity,
            TextBlock detailTargets,
            TextBlock detailRemarks,
            Button selectRowButton,
            Button copyRowButton)
        {
            if (detailName == null ||
                detailExpression == null ||
                detailQuantity == null ||
                detailTargets == null ||
                detailRemarks == null)
            {
                return;
            }

            bool hasRow = row != null;
            detailName.Text = hasRow ? row.QuantityName : "";
            detailExpression.Text = hasRow ? row.QuantityExpression : "";
            detailQuantity.Text = hasRow
                ? string.Join(" ", new[] { row.Quantity, row.Unit }.Where(v => !string.IsNullOrWhiteSpace(v)))
                : "";
            detailTargets.Text = hasRow ? FormatExpressionTargets(row) : "";
            detailRemarks.Text = hasRow ? row.Remarks : "";

            if (selectRowButton != null)
            {
                selectRowButton.IsEnabled = hasRow && row.RevitElementIds != null && row.RevitElementIds.Count > 0;
            }

            if (copyRowButton != null)
            {
                copyRowButton.IsEnabled = hasRow;
            }
        }

        private static string FormatExpressionTargets(QsExpressionRow row)
        {
            if (row?.RevitElementIds == null || row.RevitElementIds.Count == 0)
            {
                return "No linked model target.";
            }

            List<long> ids = row.RevitElementIds
                .Where(id => id > 0)
                .Distinct()
                .Take(10)
                .ToList();
            string suffix = row.RevitElementIds.Distinct().Count() > ids.Count ? " ..." : "";
            return row.RevitElementIds.Distinct().Count().ToString(CultureInfo.InvariantCulture) +
                   " item(s): " +
                   string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))) +
                   suffix;
        }

        private static void QueueExpressionRowsInModel(
            QsExpressionSelectionExternalEventHandler selectionHandler,
            ExternalEvent selectionEvent,
            IEnumerable<QsExpressionRow> rows,
            CamboBIMWindow owner)
        {
            if (selectionHandler == null || selectionEvent == null || rows == null) return;

            try
            {
                List<long> ids = rows
                    .Where(row => row?.RevitElementIds != null)
                    .SelectMany(row => row.RevitElementIds)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                if (ids.Count == 0) return;

                selectionHandler.SetElementIds(ids, owner);
                selectionEvent.Raise();
            }
            catch (Exception ex)
            {
                owner?.ShowStatus("Select expression row failed: " + ex.Message);
            }
        }

        private static void AddExpressionColumn(DataGrid grid, string header, string propertyName, double width, bool wrap)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(propertyName),
                Width = width
            };

            if (wrap)
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
                style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2)));
                column.ElementStyle = style;
            }

            grid.Columns.Add(column);
        }

        private static string BuildExpressionClipboard(IEnumerable<QsExpressionRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Quantity Name\tQuantity Expression\tQuantity\tUnit\tCount Tag\tRemarks");
            foreach (QsExpressionRow row in rows ?? Enumerable.Empty<QsExpressionRow>())
            {
                sb.Append(EscapeTsv(row.QuantityName)).Append('\t')
                  .Append(EscapeTsv(row.QuantityExpression)).Append('\t')
                  .Append(EscapeTsv(row.Quantity)).Append('\t')
                  .Append(EscapeTsv(row.Unit)).Append('\t')
                  .Append(EscapeTsv(row.CountTag)).Append('\t')
                  .Append(EscapeTsv(row.Remarks)).AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildExpressionRowClipboard(QsExpressionRow row)
        {
            if (row == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("Quantity Name\tQuantity Expression\tQuantity\tUnit\tCount Tag\tRemarks");
            sb.Append(EscapeTsv(row.QuantityName)).Append('\t')
              .Append(EscapeTsv(row.QuantityExpression)).Append('\t')
              .Append(EscapeTsv(row.Quantity)).Append('\t')
              .Append(EscapeTsv(row.Unit)).Append('\t')
              .Append(EscapeTsv(row.CountTag)).Append('\t')
              .Append(EscapeTsv(row.Remarks)).AppendLine();
            return sb.ToString();
        }

        private static string EscapeTsv(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static bool TryGetParamDouble(Element element, string name, out double value)
        {
            value = 0.0;
            Parameter p = element?.LookupParameter(name);
            if (p == null) return false;

            if (p.StorageType == StorageType.Double)
            {
                value = p.AsDouble();
                return true;
            }

            if (p.StorageType == StorageType.Integer)
            {
                value = p.AsInteger();
                return true;
            }

            return false;
        }

        private static double ToSquareMeters(double internalArea)
        {
            if (Math.Abs(internalArea) <= 1e-12) return 0.0;
            try
            {
                return UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);
            }
            catch
            {
                return internalArea * 0.09290304;
            }
        }

        private static double ToMeters(double internalLength)
        {
            if (Math.Abs(internalLength) <= 1e-12) return 0.0;
            try
            {
                return UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters);
            }
            catch
            {
                return internalLength * 0.3048;
            }
        }

        private static double ToCubicMeters(double internalVolume)
        {
            if (Math.Abs(internalVolume) <= 1e-12) return 0.0;
            try
            {
                return UnitUtils.ConvertFromInternalUnits(internalVolume, UnitTypeId.CubicMeters);
            }
            catch
            {
                return internalVolume * 0.028316846592;
            }
        }

        private static string FormatExpressionQuantity(double value)
        {
            if (Math.Abs(value) < 0.0005) value = 0.0;
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string BuildExpressionRemarks(string ruleCode, string breakdown)
        {
            return CombineRemarks(
                string.IsNullOrWhiteSpace(ruleCode) ? "" : "Rules: " + ruleCode,
                breakdown);
        }

        private static string CombineRemarks(params string[] parts)
        {
            return string.Join("; ", (parts ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private sealed class QsExpressionRow
        {
            public string QuantityName { get; set; } = "";
            public string QuantityExpression { get; set; } = "";
            public string Quantity { get; set; } = "";
            public string Unit { get; set; } = "";
            public string CountTag { get; set; } = "";
            public string Remarks { get; set; } = "";
            public List<long> RevitElementIds { get; set; } = new List<long>();
        }

        private sealed class BeamTasTakeoff
        {
            public double AxisLength { get; set; }
            public double Width { get; set; }
            public double Depth { get; set; }
            public double ColumnDeductLength { get; set; }
            public double LeftSideGrossArea { get; set; }
            public double RightSideGrossArea { get; set; }
            public double BottomGrossArea { get; set; }
            public double TopGrossArea { get; set; }
            public double ColumnSideDeductionArea { get; set; }
            public double ColumnBottomDeductionArea { get; set; }
            public double ColumnTopDeductionArea { get; set; }
            public double ColumnVolumeDeduction { get; set; }

            public double GrossSideArea => LeftSideGrossArea + RightSideGrossArea;
            public double GrossArea => GrossSideArea + BottomGrossArea + TopGrossArea;
            public double ColumnFormworkDeductionArea => ColumnSideDeductionArea + ColumnBottomDeductionArea + ColumnTopDeductionArea;
            public double SideNetArea => Math.Max(0.0, GrossSideArea - ColumnSideDeductionArea);
            public double BottomNetArea => Math.Max(0.0, BottomGrossArea - ColumnBottomDeductionArea);
            public double TopNetArea => Math.Max(0.0, TopGrossArea - ColumnTopDeductionArea);
            public double FormworkNetArea => Math.Max(0.0, GrossArea - ColumnFormworkDeductionArea);
            public double VolumeGross => Width * Depth * AxisLength;
            public double VolumeNet => Math.Max(0.0, VolumeGross - ColumnVolumeDeduction);
            public double NetLength => Math.Max(0.0, AxisLength - ColumnDeductLength);
            public double Girth => 2.0 * (Width + Depth);
        }

        private sealed class ColumnTasTakeoff
        {
            public double Width { get; set; }
            public double Depth { get; set; }
            public double Height { get; set; }
            public double SideArea { get; set; }
            public double TopBottomArea { get; set; }

            public double Girth => 2.0 * (Width + Depth);
            public double Volume => Width * Depth * Height;
            public double FormworkArea => SideArea + TopBottomArea;
        }

        private sealed class SlabTasTakeoff
        {
            public double OriginalArea { get; set; }
            public double Thickness { get; set; }
            public double SubFoundation { get; set; }
            public double SubBeam { get; set; }
            public double SubColumn { get; set; }
            public double SubWall { get; set; }
            public double SubFloor { get; set; }
            public double SubStair { get; set; }
            public double SubGeneric { get; set; }
            public double EdgeBreakLength { get; set; }

            public double FormworkDeductionArea =>
                Math.Max(0.0, SubFoundation) +
                Math.Max(0.0, SubBeam) +
                Math.Max(0.0, SubColumn) +
                Math.Max(0.0, SubWall) +
                Math.Max(0.0, SubFloor) +
                Math.Max(0.0, SubStair) +
                Math.Max(0.0, SubGeneric);

            public double FormworkNetArea => Math.Max(0.0, OriginalArea - FormworkDeductionArea);
            public double ProjectedAreaNet => Math.Max(0.0, OriginalArea - Math.Max(0.0, SubWall) - Math.Max(0.0, SubStair));
            public double VolumeDeduction => (Math.Max(0.0, SubColumn) + Math.Max(0.0, SubBeam) + Math.Max(0.0, SubStair)) * Math.Max(0.0, Thickness);
            public double VolumeNet => Math.Max(0.0, OriginalArea * Math.Max(0.0, Thickness) - VolumeDeduction);
        }

        private sealed class StairTasTakeoff
        {
            public double PaintingArea { get; set; }
            public double PaintingSideArea { get; set; }
            public double PaintingBottomArea { get; set; }
            public double NetArea { get; set; }
            public double SubFoundation { get; set; }
            public double SubBeam { get; set; }
            public double SubColumn { get; set; }
            public double SubWall { get; set; }
            public double SubFloor { get; set; }
            public double SubGeneric { get; set; }
            public double StepCount { get; set; }
            public double Volume { get; set; }
        }

        private sealed class WallTasTakeoff
        {
            public double LeftSideLength { get; set; }
            public double RightSideLength { get; set; }
            public double OriginalLength { get; set; }
            public double Height { get; set; }
            public double Thickness { get; set; }
            public double WallDeductArea { get; set; }
            public double OtherDeductArea { get; set; }
            public double WallDeductLength { get; set; }
            public double StrutHeight { get; set; }

            public double OriginalFormworkArea => (LeftSideLength + RightSideLength) * Height;
            public double FormworkNetArea => Math.Max(0.0, OriginalFormworkArea - WallDeductArea - OtherDeductArea);
            public double ProjectedAreaNet => Math.Max(0.0, OriginalLength * Height - WallDeductArea);
            public double VolumeNet => Math.Max(0.0, OriginalLength * Height * Thickness - WallDeductArea * Thickness);
            public double NetLength => Math.Max(0.0, OriginalLength - WallDeductLength);
            public double StrutWallDeductArea => Height > 1e-9 ? WallDeductArea * (StrutHeight / Height) : 0.0;
            public double StrutFormworkArea => Math.Max(0.0, (LeftSideLength + RightSideLength) * StrutHeight - StrutWallDeductArea);
        }

        private sealed class BeamColumnDeductionHit
        {
            public Element Column { get; }
            public double StartDistance { get; }
            public double EndDistance { get; }
            public double Length => Math.Max(0.0, EndDistance - StartDistance);

            public BeamColumnDeductionHit(Element column, double startDistance, double endDistance)
            {
                Column = column;
                StartDistance = startDistance;
                EndDistance = endDistance;
            }
        }

        private sealed class QsDeductionProbe
        {
            public BuiltInCategory Category { get; }
            public string Label { get; }
            public string ParamName { get; }

            public QsDeductionProbe(BuiltInCategory category, string label, string paramName)
            {
                Category = category;
                Label = label ?? "";
                ParamName = paramName ?? "";
            }
        }

        private sealed class QsExpressionSelectionExternalEventHandler : IExternalEventHandler
        {
            private readonly object _sync = new object();
            private List<long> _elementIds = new List<long>();
            private CamboBIMWindow _owner;

            public void SetElementIds(IEnumerable<long> elementIds, CamboBIMWindow owner)
            {
                lock (_sync)
                {
                    _elementIds = (elementIds ?? Enumerable.Empty<long>())
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();
                    _owner = owner;
                }
            }

            public void Execute(UIApplication app)
            {
                List<long> elementIds;
                CamboBIMWindow owner;
                lock (_sync)
                {
                    elementIds = new List<long>(_elementIds);
                    owner = _owner;
                }

                if (elementIds.Count == 0) return;

                try
                {
                    UIDocument uidoc = app?.ActiveUIDocument;
                    Document doc = uidoc?.Document;
                    if (uidoc == null || doc == null) return;

                    List<ElementId> revitIds = elementIds
                        .Select(id => new ElementId(id))
                        .Where(id => doc.GetElement(id) != null)
                        .ToList();

                    if (revitIds.Count == 0) return;

                    uidoc.Selection.SetElementIds(revitIds);
                    try
                    {
                        uidoc.ShowElements(revitIds);
                    }
                    catch
                    {
                        // Selection highlight is the primary action; zoom-to-fit can fail in some views.
                    }

                    owner?.ShowStatus("Selected " + revitIds.Count.ToString(CultureInfo.InvariantCulture) + " item(s) from QS expression.");
                }
                catch (Exception ex)
                {
                    owner?.ShowStatus("Select expression row failed: " + ex.Message);
                }
            }

            public string GetName()
            {
                return "CBIM-QS expression row selection";
            }
        }

        private sealed class QsFormworkVisibilityExternalEventHandler : IExternalEventHandler
        {
            private CamboBIMWindow _owner;

            public void SetOwner(CamboBIMWindow owner)
            {
                _owner = owner;
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    UIDocument uidoc = app?.ActiveUIDocument;
                    Document doc = uidoc?.Document;
                    ToggleFormworkShapesInActiveView(uidoc, doc, _owner);
                }
                catch (Exception ex)
                {
                    _owner?.ShowStatus("Hide/Show formwork shapes failed: " + ex.Message);
                }
            }

            public string GetName()
            {
                return "CBIM-QS formwork shape visibility";
            }
        }

        private sealed class QsExpressionSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element == null) return false;
                if (IsCbimFormworkShape(element)) return true;
                if (HasAnyPositiveParam(element, "CBIM_FormworkArea", "CBIM_QsFinishArea", SoilExcavationVolumeParamName, SoilBackfilledVolumeParamName))
                {
                    return true;
                }

                long categoryId = element.Category?.Id?.Value ?? 0;
                return QsMeasuredCategories.Any(category => categoryId == (long)category);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        private static string[] GetQsCsvParameterColumns()
        {
            return new[]
            {
                UnifiedLevelParamName,
                UnifiedBuildingLevelParamName,
                "CBIM_FormworkArea",
                "CBIM_QsRuleCode",
                "CBIM_QsFormula",
                "CBIM_QsBreakdown",
                "CBIM_QsFinishGrossArea",
                "CBIM_QsFinishArea",
                "FWK.Enable",
                "FWK.EnableSelf",
                "CBIM_Beam_Length",
                "CBIM_Beam_Width",
                "CBIM_Beam_Depth",
                "CBIM_Beam_Volume",
                "CBIM_Beam_Type",
                "CBIM_Beam_Level",
                "FWK.Beam.Total",
                "FWK.Beam.Sides",
                "FWK.Beam.Bottom",
                "FWK.Beam.Top",
                "FWK.Beam.SubFoun",
                "FWK.Beam.SubBeam",
                "FWK.Beam.SubCol",
                "FWK.Beam.SubWall",
                "FWK.Beam.SubFloor",
                "FWK.Beam.SubGeneric",
                "FWK.Beam.StrutHeight",
                "FWK.Beam.StrutStage",
                "FWK.Beam.Stage.Basic",
                "FWK.Beam.Stage.1",
                "FWK.Beam.Stage.2",
                "FWK.Beam.Stage.3",
                "FWK.Beam.Stage.4",
                "FWK.Beam.Stage.5",
                "FWK.Beam.Stage.6",
                "FWK.Beam.Stage.7",
                "FWK.Beam.Stage.8",
                "FWK.Beam.Stage.9",
                "FWK.Beam.Stage.10",
                "FWK.Beam.Stage.11",
                "FWK.Beam.Stage.12",
                "FWK.Beam.Stage.13",
                "FWK.Beam.Stage.14",
                "FWK.Beam.Stage.15",
                "FWK.Col.Total",
                "FWK.Col.Sides",
                "FWK.Col.TopBottom",
                "FWK.Col.SubFoun",
                "FWK.Col.SubBeam",
                "FWK.Col.SubCol",
                "FWK.Col.SubWall",
                "FWK.Col.SubFloor",
                "FWK.Col.SubGeneric",
                "FWK.Wall.Total",
                "FWK.Wall.Sides",
                "FWK.Wall.OpeningSide",
                "FWK.Wall.OpeningBottom",
                "FWK.Wall.JoinedEndCap",
                "FWK.Wall.SubFoun",
                "FWK.Wall.SubBeam",
                "FWK.Wall.SubCol",
                "FWK.Wall.SubWall",
                "FWK.Wall.SubFloor",
                "FWK.Wall.SubGeneric",
                "FWK.Floor.Total",
                "FWK.Floor.Bottom",
                "FWK.Floor.Sides",
                "FWK.Floor.OpeningSide",
                "FWK.Floor.Opening.Count",
                "FWK.Floor.Opening.Area",
                "FWK.Floor.Opening.Girth",
                "FWK.Floor.EdgeBreak.Lte250",
                "FWK.Floor.EdgeBreak.Lte500",
                "FWK.Floor.EdgeBreak.Lte1000",
                "FWK.Floor.EdgeBreak.Over1000",
                "FWK.Floor.TopSlope",
                "FWK.Floor.StrutHeight",
                "FWK.Floor.StrutSoffit",
                "FWK.Floor.StrutStageCount",
                "FWK.Floor.StrutStageArea",
                "FWK.Floor.StrutStage.1",
                "FWK.Floor.StrutStage.2",
                "FWK.Floor.StrutStage.3",
                "FWK.Floor.StrutStage.4",
                "FWK.Floor.StrutStage.5",
                "FWK.Floor.StrutStage.6",
                "FWK.Floor.StrutStage.7",
                "FWK.Floor.StrutStage.8",
                "FWK.Floor.StrutStage.9",
                "FWK.Floor.StrutStage.10",
                "FWK.Floor.StrutStage.11",
                "FWK.Floor.StrutStage.12",
                "FWK.Floor.StrutStage.13",
                "FWK.Floor.StrutStage.14",
                "FWK.Floor.StrutStage.15",
                "FWK.Floor.StrutEdge",
                "FWK.Floor.StrutEdgeStageArea",
                "FWK.Floor.StrutTop",
                "FWK.Floor.StrutTopStageArea",
                "FWK.Floor.SubFoun",
                "FWK.Floor.SubBeam",
                "FWK.Floor.SubCol",
                "FWK.Floor.SubWall",
                "FWK.Floor.SubFloor",
                "FWK.Floor.SubStair",
                "FWK.Floor.SubGeneric",
                "FWK.Stair.Total",
                "FWK.Stair.Painting",
                "FWK.Stair.Sides",
                "FWK.Stair.PaintingSide",
                "FWK.Stair.PaintingBottom",
                "FWK.Stair.Bottom",
                "FWK.Stair.Top",
                "FWK.Stair.SubFoun",
                "FWK.Stair.SubBeam",
                "FWK.Stair.SubCol",
                "FWK.Stair.SubWall",
                "FWK.Stair.SubFloor",
                "FWK.Stair.SubGeneric",
                "FWK.Foun.Total",
                "FWK.Foun.Sides",
                "FWK.Foun.Top",
                "FWK.Foun.SubFoun",
                "FWK.Foun.SubBeam",
                "FWK.Foun.SubCol",
                "FWK.Foun.SubWall",
                "FWK.Foun.SubFloor",
                "FWK.Foun.SubGeneric",
                "FWK.Foun.SideLength.Total",
                "FWK.Foun.SideArea.Staged",
                "FWK.Foun.SideLength.Stage.1",
                "FWK.Foun.SideLength.Stage.2",
                "FWK.Foun.SideLength.Stage.3",
                "FWK.Foun.SideLength.Stage.4",
                "FWK.Foun.SideLength.Stage.5",
                "FWK.Foun.SideLength.Stage.6",
                "FWK.Foun.SideLength.Stage.7",
                "FWK.Foun.SideLength.Stage.8",
                "FWK.Foun.SideLength.Stage.9",
                "FWK.Foun.SideLength.Stage.10",
                "FWK.Foun.SideLength.Stage.11",
                "FWK.Foun.SideLength.Stage.12",
                "FWK.Foun.SideLength.Stage.13",
                "FWK.Foun.SideLength.Stage.14",
                "FWK.Foun.SideLength.Stage.15",
                "FWK.Foun.SideArea.Stage.1",
                "FWK.Foun.SideArea.Stage.2",
                "FWK.Foun.SideArea.Stage.3",
                "FWK.Foun.SideArea.Stage.4",
                "FWK.Foun.SideArea.Stage.5",
                "FWK.Foun.SideArea.Stage.6",
                "FWK.Foun.SideArea.Stage.7",
                "FWK.Foun.SideArea.Stage.8",
                "FWK.Foun.SideArea.Stage.9",
                "FWK.Foun.SideArea.Stage.10",
                "FWK.Foun.SideArea.Stage.11",
                "FWK.Foun.SideArea.Stage.12",
                "FWK.Foun.SideArea.Stage.13",
                "FWK.Foun.SideArea.Stage.14",
                "FWK.Foun.SideArea.Stage.15",
                "FWK.Kerb.Total",
                "FWK.Kerb.Sides",
                "FWK.Kerb.Top",
                "FWK.Kerb.SubWall",
                "FWK.Kerb.SubCol",
                "FWK.Kerb.Length",
                "FWK.Other.Total",
                "FWK.Other.Sides",
                "FWK.Other.Bottom",
                "FWK.Other.SubStructure",
                "FIN.Category",
                "FIN.Room",
                "FIN.RoomNumber",
                "FIN.RoomName",
                "FIN.WF.Gross",
                "FIN.WF.Area",
                "FIN.WF.OpeningDeduct",
                "FIN.WF.Return",
                "FIN.CF.Gross",
                "FIN.CF.Area",
                "FIN.CF.OpeningDeduct",
                "FIN.CF.Return",
                "FIN.SC.Gross",
                "FIN.SC.Area",
                "FIN.SC.OpeningDeduct",
                "FIN.SC.Return",
                "FIN.FF.Gross",
                "FIN.FF.Area",
                "FIN.FF.OpeningDeduct",
                "FIN.FF.Return",
                "FIN.WP.Gross",
                "FIN.WP.Area",
                "FIN.WP.OpeningDeduct",
                "FIN.WP.Return",
                "FIN.WP.Upturn"
            };
        }

        private static IEnumerable<string> GetManagedParameterNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in GetQsCsvParameterColumns())
            {
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
            }

            names.Add("CBIM.FWK.IsShape");
            names.Add("CBIM.FWK.Category");
            names.Add("CBIM.FWK.ElementId");
            names.Add("CBIM.FWK.FaceType");
            names.Add("CBIM.FWK.FaceArea");
            names.Add("CBIM.FWK.Level");
            names.Add("CBIM_FormworkArea");
            names.Add(UnifiedLevelParamName);
            names.Add(UnifiedBuildingLevelParamName);
            names.Add(SoilExcavationVolumeParamName);
            names.Add(SoilBackfilledVolumeParamName);
            return names;
        }

        private static string GetCsvParameterValue(Element element, string paramName)
        {
            Parameter p = element?.LookupParameter(paramName);
            if (p == null) return "";

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.AsString() ?? "";
                    case StorageType.Integer:
                        return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        string asValue = p.AsValueString();
                        if (!string.IsNullOrWhiteSpace(asValue)) return asValue;
                        return p.AsDouble().ToString("0.########", CultureInfo.InvariantCulture);
                    case StorageType.ElementId:
                        ElementId id = p.AsElementId();
                        if (id == null || id == ElementId.InvalidElementId) return "";
                        return id.Value.ToString(CultureInfo.InvariantCulture);
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static string EscapeCsv(string value)
        {
            if (value == null) return "";
            bool needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\r") || value.Contains("\n");
            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string GetElementTypeName(Document doc, Element element)
        {
            if (doc == null || element == null) return "";
            if (element is FamilyInstance fi && fi.Symbol != null)
            {
                return fi.Symbol.Name ?? "";
            }

            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                ElementType type = doc.GetElement(typeId) as ElementType;
                if (type != null) return type.Name ?? "";
            }

            return element.Name ?? "";
        }

        private static ElementType GetElementType(Element element)
        {
            if (element == null || element.Document == null) return null;

            ElementId typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId) return null;
            return element.Document.GetElement(typeId) as ElementType;
        }

        private static string GetStringParameterValue(Element element, string name)
        {
            if (element == null || string.IsNullOrWhiteSpace(name)) return "";

            Parameter parameter = element.LookupParameter(name);
            if (parameter == null) return "";

            try
            {
                if (parameter.StorageType == StorageType.String)
                {
                    return parameter.AsString() ?? "";
                }

                return parameter.AsValueString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string GetDefaultQsSharedParameterPath()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            return SharedParameterPathResolver.ResolveDefaultSharedParameterPath(baseDir);
        }

        private static string TryFindExistingQsSharedParameterPath(string startDir)
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

        private static List<Element> CollectStructuralFramingElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_StructuralFraming);
        }

        private static List<Element> CollectStructuralFoundationElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_StructuralFoundation);
        }

        private static List<Element> CollectStructuralColumnElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_StructuralColumns);
        }

        private static List<Element> CollectStructuralFloorElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Floors);
        }

        private static List<Element> CollectStructuralWallElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            // Do not filter by structural-significant flag here.
            // In many projects this flag is off even for walls that must be included in QS/formwork.
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Walls);
        }

        private static List<Element> CollectStairElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Stairs);
        }

        private static List<Element> CollectGenericModelElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_GenericModel);
        }

        private static List<Element> CollectCeilingElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Ceilings);
        }

        private static List<Element> CollectKerbElements(IList<Element> floors, IList<Element> generics)
        {
            var results = new List<Element>();
            AddClassifiedElements(results, floors, IsKerbElement);
            AddClassifiedElements(results, generics, IsKerbElement);
            return results;
        }

        private static List<Element> CollectLintelElements(IList<Element> beams, IList<Element> generics)
        {
            var results = new List<Element>();
            AddClassifiedElements(results, beams, IsLintelElement);
            AddClassifiedElements(results, generics, IsLintelElement);
            return results;
        }

        private static List<Element> CollectDropPanelElements(IList<Element> floors, IList<Element> generics)
        {
            var results = new List<Element>();
            AddClassifiedElements(results, floors, IsDropPanelElement);
            AddClassifiedElements(results, generics, IsDropPanelElement);
            return results;
        }

        private static List<Element> CollectEaveElements(IList<Element> floors, IList<Element> generics)
        {
            var results = new List<Element>();
            AddClassifiedElements(results, floors, IsEaveElement);
            AddClassifiedElements(results, generics, IsEaveElement);
            return results;
        }

        private static List<Element> CollectOtherConcreteElements(IList<Element> floors, IList<Element> generics, params IList<Element>[] excluded)
        {
            var results = new List<Element>();
            HashSet<long> excludedIds = BuildElementIdSet(excluded);
            AddClassifiedElements(results, floors, e => !ContainsElementId(excludedIds, e) && IsOtherConcreteElement(e));
            AddClassifiedElements(results, generics, e => !ContainsElementId(excludedIds, e) && IsOtherConcreteElement(e));
            return results;
        }

        private static void AddClassifiedElements(IList<Element> results, IList<Element> source, Func<Element, bool> predicate)
        {
            if (results == null || source == null || predicate == null) return;

            HashSet<long> existing = BuildElementIdSet(results);
            foreach (Element element in source)
            {
                if (element == null || element.Id == null) continue;
                if (existing.Contains(element.Id.Value)) continue;
                if (!predicate(element)) continue;

                results.Add(element);
                existing.Add(element.Id.Value);
            }
        }

        private static HashSet<long> BuildElementIdSet(params IList<Element>[] groups)
        {
            var ids = new HashSet<long>();
            if (groups == null) return ids;

            foreach (IList<Element> group in groups)
            {
                if (group == null) continue;
                foreach (Element element in group)
                {
                    if (element?.Id == null) continue;
                    ids.Add(element.Id.Value);
                }
            }

            return ids;
        }

        private static bool ContainsElementId(HashSet<long> ids, Element element)
        {
            return ids != null && element?.Id != null && ids.Contains(element.Id.Value);
        }

        private static bool IsKerbElement(Element element)
        {
            if (IsExcludedAuxiliaryConcreteElement(element)) return false;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            return ContainsAnyClassificationPhrase(text, "kerb", "curb", "upstand", "up stand");
        }

        private static bool IsLintelElement(Element element)
        {
            if (IsExcludedAuxiliaryConcreteElement(element)) return false;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            return ContainsAnyClassificationPhrase(text, "lintel", "lntel", "door head", "window head", "head beam");
        }

        private static bool IsDropPanelElement(Element element)
        {
            if (IsExcludedAuxiliaryConcreteElement(element)) return false;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            return ContainsAnyClassificationPhrase(text, "drop panel", "droppanel", "drop-panel", "drop head", "column head");
        }

        private static bool IsEaveElement(Element element)
        {
            if (IsExcludedAuxiliaryConcreteElement(element)) return false;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            return ContainsAnyClassificationPhrase(text, "eave", "eaves", "soffit edge", "roof overhang", "overhang");
        }

        private static bool IsOtherConcreteElement(Element element)
        {
            if (IsExcludedAuxiliaryConcreteElement(element)) return false;
            if (IsKerbElement(element)) return false;
            if (IsLintelElement(element) || IsDropPanelElement(element) || IsEaveElement(element)) return false;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            if (ContainsAnyClassificationPhrase(
                    text,
                    "other concrete",
                    "lean concrete",
                    "blinding concrete",
                    "blinding",
                    "mass concrete",
                    "plain concrete",
                    "pcc",
                    "plinth",
                    "concrete plinth"))
            {
                return true;
            }

            bool isGenericModel = element.Category?.Id?.Value == (long)BuiltInCategory.OST_GenericModel;
            if (!isGenericModel) return false;

            return ContainsAnyClassificationPhrase(text, "concrete", "reinforced concrete", "precast concrete", "cast in place", "rc", "r c") &&
                   !ContainsAnyClassificationPhrase(
                       text,
                       "beam",
                       "column",
                       "wall",
                       "slab",
                       "floor",
                       "foundation",
                       "footing",
                       "pile",
                       "stair",
                       "soil",
                       "excavation",
                       "backfill");
        }

        internal static string GetAuxiliaryConcreteBoqLabel(Element element)
        {
            if (IsKerbElement(element)) return "Kerb";
            if (IsOtherConcreteElement(element)) return "Other Concrete";
            return "";
        }

        internal static string GetExtendedStructureBoqLabel(Element element)
        {
            if (IsLintelElement(element)) return "Lintel";
            if (IsDropPanelElement(element)) return "Drop Panel";
            if (IsEaveElement(element)) return "Eave";
            return "";
        }

        internal static string GetFinishBoqLabel(Element element)
        {
            QsFinishMeasurementKind kind;
            return TryClassifyFinishElement(element, out kind) ? GetFinishBoqLabel(kind) : "";
        }

        private static List<QsFinishMeasurementCandidate> CollectFinishMeasurementCandidates(
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> ceilings,
            IList<Element> generics,
            QsMeasurementRuntimeRules rules)
        {
            var results = new List<QsFinishMeasurementCandidate>();
            if (rules == null || !rules.FinishCalculationEnabled) return results;

            AddFinishCandidates(results, walls, rules);
            AddFinishCandidates(results, floors, rules);
            AddFinishCandidates(results, ceilings, rules);
            AddFinishCandidates(results, generics, rules);
            return results;
        }

        private static void AddFinishCandidates(
            IList<QsFinishMeasurementCandidate> results,
            IList<Element> source,
            QsMeasurementRuntimeRules rules)
        {
            if (results == null || source == null || rules == null) return;

            HashSet<long> existing = BuildElementIdSet(results.Select(c => c.Element).ToList());
            foreach (Element element in source)
            {
                if (element?.Id == null || existing.Contains(element.Id.Value)) continue;

                QsFinishMeasurementKind kind;
                if (!TryClassifyFinishElement(element, out kind)) continue;
                if (!rules.IsFinishKindEnabled(kind)) continue;

                results.Add(new QsFinishMeasurementCandidate(element, kind));
                existing.Add(element.Id.Value);
            }
        }

        private static bool TryClassifyFinishElement(Element element, out QsFinishMeasurementKind kind)
        {
            kind = QsFinishMeasurementKind.Unknown;
            if (IsExcludedFinishElement(element)) return false;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            long categoryId = element.Category?.Id?.Value ?? 0;
            bool isWall = categoryId == (long)BuiltInCategory.OST_Walls;
            bool isFloor = categoryId == (long)BuiltInCategory.OST_Floors;
            bool isCeiling = categoryId == (long)BuiltInCategory.OST_Ceilings;
            bool isGeneric = categoryId == (long)BuiltInCategory.OST_GenericModel;

            if (ContainsAnyClassificationPhrase(text, "waterproof", "water proof", "membrane", "tanking", "bitumen", "damp proof", "dpm"))
            {
                kind = QsFinishMeasurementKind.Waterproof;
                return true;
            }

            if ((isCeiling || isGeneric) &&
                ContainsAnyClassificationPhrase(text, "suspended ceiling", "drop ceiling", "dropped ceiling", "grid ceiling", "acoustic ceiling", "t bar", "tbar", "act ceiling"))
            {
                kind = QsFinishMeasurementKind.SuspendedCeiling;
                return true;
            }

            if (isCeiling)
            {
                kind = QsFinishMeasurementKind.CeilingFinish;
                return true;
            }

            if ((isWall || isGeneric) &&
                ContainsAnyClassificationPhrase(text, "wall finish", "finish wall", "plaster", "skim coat", "render", "wall tile", "cladding", "paint finish"))
            {
                kind = QsFinishMeasurementKind.WallFinish;
                return true;
            }

            if ((isFloor || isGeneric) &&
                ContainsAnyClassificationPhrase(text, "floor finish", "finish floor", "flooring", "tile", "vinyl", "carpet", "terrazzo", "screed", "parquet", "epoxy floor"))
            {
                kind = QsFinishMeasurementKind.FloorFinish;
                return true;
            }

            if (isFloor &&
                ContainsAnyClassificationPhrase(text, "finish") &&
                !ContainsAnyClassificationPhrase(text, "structural", "slab", "foundation", "footing", "lean concrete", "blinding", "kerb", "curb"))
            {
                kind = QsFinishMeasurementKind.FloorFinish;
                return true;
            }

            if (isWall &&
                ContainsAnyClassificationPhrase(text, "finish") &&
                !ContainsAnyClassificationPhrase(text, "structural", "foundation", "retaining", "shear wall"))
            {
                kind = QsFinishMeasurementKind.WallFinish;
                return true;
            }

            return false;
        }

        private static bool IsExcludedFinishElement(Element element)
        {
            if (element == null || element.Category == null || element is ElementType) return true;
            if (IsCbimFormworkShape(element)) return true;
            if (IsKerbElement(element) || IsOtherConcreteElement(element)) return true;

            long categoryId = element.Category.Id.Value;
            if (categoryId != (long)BuiltInCategory.OST_Walls &&
                categoryId != (long)BuiltInCategory.OST_Floors &&
                categoryId != (long)BuiltInCategory.OST_Ceilings &&
                categoryId != (long)BuiltInCategory.OST_GenericModel)
            {
                return true;
            }

            string text = BuildAuxiliaryConcreteClassificationText(element);
            return ContainsAnyClassificationPhrase(
                text,
                "soil excavation",
                "soil backfilled",
                "backfill",
                "excavation",
                "opening candidate",
                "opening solid",
                "bounding solid",
                "boundary solid",
                "formwork shape",
                "cbim fwk");
        }

        private static string GetFinishBoqLabel(QsFinishMeasurementKind kind)
        {
            switch (kind)
            {
                case QsFinishMeasurementKind.WallFinish:
                    return "Wall Finish";
                case QsFinishMeasurementKind.CeilingFinish:
                    return "Ceiling Finish";
                case QsFinishMeasurementKind.SuspendedCeiling:
                    return "Suspended Ceiling";
                case QsFinishMeasurementKind.FloorFinish:
                    return "Floor Finish";
                case QsFinishMeasurementKind.Waterproof:
                    return "Waterproof";
                default:
                    return "";
            }
        }

        private static string GetFinishParameterPrefix(QsFinishMeasurementKind kind)
        {
            switch (kind)
            {
                case QsFinishMeasurementKind.WallFinish:
                    return "WF";
                case QsFinishMeasurementKind.CeilingFinish:
                    return "CF";
                case QsFinishMeasurementKind.SuspendedCeiling:
                    return "SC";
                case QsFinishMeasurementKind.FloorFinish:
                    return "FF";
                case QsFinishMeasurementKind.Waterproof:
                    return "WP";
                default:
                    return "";
            }
        }

        private static bool IsExcludedAuxiliaryConcreteElement(Element element)
        {
            if (element == null || element.Category == null || element is ElementType) return true;

            long categoryId = element.Category.Id.Value;
            if (categoryId != (long)BuiltInCategory.OST_GenericModel &&
                categoryId != (long)BuiltInCategory.OST_Floors)
            {
                return true;
            }

            if (IsCbimFormworkShape(element)) return true;

            string text = BuildAuxiliaryConcreteClassificationText(element);
            return ContainsAnyClassificationPhrase(
                text,
                "soil excavation",
                "soil backfilled",
                "backfill",
                "excavation",
                "opening candidate",
                "opening solid",
                "bounding solid",
                "boundary solid",
                "formwork shape",
                "cbim fwk");
        }

        private static string BuildAuxiliaryConcreteClassificationText(Element element)
        {
            if (element == null) return "";

            var parts = new List<string>();
            AddClassificationPart(parts, element.Name);
            AddClassificationPart(parts, element.Category?.Name);
            AddClassificationPart(parts, GetElementTypeName(element.Document, element));

            if (element is FamilyInstance fi && fi.Symbol != null)
            {
                AddClassificationPart(parts, fi.Symbol.FamilyName);
                AddClassificationPart(parts, fi.Symbol.Name);
            }

            ElementType type = null;
            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                type = element.Document?.GetElement(typeId) as ElementType;
            }

            if (type != null)
            {
                AddClassificationPart(parts, type.FamilyName);
                AddClassificationPart(parts, type.Name);
                AddClassificationParameterValues(parts, type);
            }

            AddClassificationParameterValues(parts, element);
            return NormalizeClassificationText(string.Join(" ", parts));
        }

        private static void AddClassificationParameterValues(IList<string> parts, Element element)
        {
            if (parts == null || element == null) return;

            foreach (string name in new[]
                     {
                         "CBIM_StructureType",
                         "Structure Type",
                         "Element Type",
                         "TypeName",
                         "Type Name",
                         "BOQ Type",
                         "BOQ Code",
                         "Mark",
                         "Comments",
                         "Description"
                     })
            {
                Parameter p = element.LookupParameter(name);
                if (p == null) continue;

                string value = "";
                if (p.StorageType == StorageType.String)
                {
                    value = p.AsString() ?? "";
                }
                else
                {
                    try
                    {
                        value = p.AsValueString() ?? "";
                    }
                    catch
                    {
                        value = "";
                    }
                }

                AddClassificationPart(parts, value);
            }
        }

        private static void AddClassificationPart(IList<string> parts, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(value)) return;
            parts.Add(value.Trim());
        }

        private static string NormalizeClassificationText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var sb = new StringBuilder(text.Length);
            foreach (char ch in text.ToLowerInvariant())
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool ContainsAnyClassificationPhrase(string normalizedText, params string[] phrases)
        {
            if (string.IsNullOrWhiteSpace(normalizedText) || phrases == null) return false;

            string paddedText = " " + normalizedText + " ";
            foreach (string phrase in phrases)
            {
                string normalizedPhrase = NormalizeClassificationText(phrase);
                if (string.IsNullOrWhiteSpace(normalizedPhrase)) continue;

                if (normalizedPhrase.Length <= 3)
                {
                    if (paddedText.Contains(" " + normalizedPhrase + " "))
                    {
                        return true;
                    }
                }
                else if (normalizedText.Contains(normalizedPhrase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStructuralWall(Element element)
        {
            if (element == null) return false;
            Parameter p = element.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            if (p != null && p.StorageType == StorageType.Integer)
            {
                return p.AsInteger() == 1;
            }

            return true;
        }

        private static List<Element> CollectElementsByCategory(UIDocument uidoc, Document doc, QsScope scope, BuiltInCategory category)
        {
            var results = new List<Element>();
            if (doc == null) return results;

            if (scope == QsScope.CurrentSelection)
            {
                ICollection<ElementId> ids = uidoc.Selection.GetElementIds();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null || el.Category == null) continue;
                    if (el.Category.Id.Value != (long)category) continue;
                    if (el is ElementType) continue;
                    results.Add(el);
                }

                return results;
            }

            if (scope == QsScope.CurrentView && doc.ActiveView != null)
            {
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
            }

            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
        }

        private static List<QsSharedParamSpec> GetQsSharedParameterSpecs(bool includeBeam, bool includeColumn, bool includeWall, bool includeFloor, bool includeStair, bool includeFoundation)
        {
            var specs = new List<QsSharedParamSpec>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddSpec(string name, ForgeTypeId typeId, ForgeTypeId groupTypeId, params BuiltInCategory[] targetCategories)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (names.Add(name))
                {
                    specs.Add(new QsSharedParamSpec(name, typeId, groupTypeId, targetCategories));
                }
            }

            AddSpec("FWK.Enable", SpecTypeId.Boolean.YesNo, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec("FWK.EnableSelf", SpecTypeId.Boolean.YesNo, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec("CBIM_FormworkArea", SpecTypeId.Area, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec("Rebar.Weight", SpecTypeId.Number, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec("CBIM_QsRuleCode", SpecTypeId.String.Text, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec("CBIM_QsFormula", SpecTypeId.String.Text, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec("CBIM_QsBreakdown", SpecTypeId.String.Text, GroupTypeId.Data, QsMeasuredCategories);
            AddSpec(
                UnifiedLevelParamName,
                SpecTypeId.String.Text,
                GroupTypeId.Data,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Ceilings);
            AddSpec(
                UnifiedBuildingLevelParamName,
                SpecTypeId.String.Text,
                GroupTypeId.Data,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Ceilings);
            AddSpec(SoilExcavationVolumeParamName, SpecTypeId.Volume, GroupTypeId.Data, BuiltInCategory.OST_GenericModel);
            AddSpec(SoilBackfilledVolumeParamName, SpecTypeId.Volume, GroupTypeId.Data, BuiltInCategory.OST_GenericModel);
            AddSpec("CBIM.FormworkShape", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);
            AddSpec("CBIM.FWK.IsShape", SpecTypeId.Boolean.YesNo, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);
            AddSpec("CBIM.FWK.Category", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);
            AddSpec("CBIM.FWK.ElementId", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);
            AddSpec("CBIM.FWK.FaceType", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);
            AddSpec("CBIM.FWK.FaceArea", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);
            AddSpec("CBIM.FWK.Level", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_SpecialityEquipment);

            if (includeBeam)
            {
                AddSpec("CBIM_Beam_Length", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("CBIM_Beam_Width", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("CBIM_Beam_Depth", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("CBIM_Beam_Volume", SpecTypeId.Volume, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("CBIM_Beam_Type", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("CBIM_Beam_Level", SpecTypeId.String.Text, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);

                AddSpec("FWK.Beam.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.Bottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.Top", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.SubFoun", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.SubBeam", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.SubFloor", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.SubGeneric", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.StrutHeight", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.StrutStage", SpecTypeId.Number, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                AddSpec("FWK.Beam.Stage.Basic", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                for (int stage = 1; stage <= BeamStrutPersistedStageLimit; stage++)
                {
                    AddSpec("FWK.Beam.Stage." + stage.ToString(CultureInfo.InvariantCulture), SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming);
                }
            }

            if (includeColumn)
            {
                AddSpec("FWK.Col.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.TopBottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.SubFoun", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.SubBeam", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.SubFloor", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.SubGeneric", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.StrutHeight", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.StrutStage", SpecTypeId.Number, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                AddSpec("FWK.Col.Stage.Basic", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                for (int stage = 1; stage <= ColumnStrutPersistedStageLimit; stage++)
                {
                    AddSpec("FWK.Col.Stage." + stage.ToString(CultureInfo.InvariantCulture), SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralColumns);
                }
            }

            if (includeWall)
            {
                AddSpec("FWK.Wall.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.OpeningSide", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.OpeningBottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.JoinedEndCap", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.SubFoun", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.SubBeam", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.SubFloor", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.SubGeneric", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                AddSpec("FWK.Wall.OriginalHeight", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                for (int stage = 0; stage <= WallEdgePersistedStageLimit; stage++)
                {
                    string stageText = stage.ToString(CultureInfo.InvariantCulture);
                    AddSpec("FWK.Wall.EdgeLength.Stage." + stageText, SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                    AddSpec("FWK.Wall.EdgeArea.Stage." + stageText, SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Walls);
                }
            }

            if (includeFloor)
            {
                AddSpec("FWK.Floor.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.Bottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.OpeningSide", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.Opening.Count", SpecTypeId.Number, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.Opening.Area", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.Opening.Girth", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.EdgeBreak.Lte250", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.EdgeBreak.Lte500", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.EdgeBreak.Lte1000", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.EdgeBreak.Over1000", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.TopSlope", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutHeight", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutBasic", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutSoffit", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutStageCount", SpecTypeId.Number, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutStageArea", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                for (int stage = 1; stage <= SlabStrutPersistedStageLimit; stage++)
                {
                    AddSpec("FWK.Floor.StrutStage." + stage.ToString(CultureInfo.InvariantCulture), SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                }
                AddSpec("FWK.Floor.StrutEdge", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutEdgeStageArea", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutTop", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.StrutTopStageArea", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubFoun", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubBeam", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubFloor", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubStair", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
                AddSpec("FWK.Floor.SubGeneric", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors);
            }

            if (includeStair)
            {
                AddSpec("FWK.Stair.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.Painting", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.PaintingSide", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.PaintingBottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.Bottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.Top", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.SubFoun", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.SubBeam", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.SubFloor", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.SubGeneric", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
                AddSpec("FWK.Stair.StepCount", SpecTypeId.Number, GroupTypeId.Data, BuiltInCategory.OST_Stairs);
            }

            if (includeFoundation)
            {
                AddSpec("FWK.Foun.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.Top", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SubFoun", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SubBeam", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SubFloor", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SubGeneric", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SideLength.Total", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                AddSpec("FWK.Foun.SideArea.Staged", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                for (int stage = 1; stage <= FoundationSidePersistedStageLimit; stage++)
                {
                    AddSpec("FWK.Foun.SideLength.Stage." + stage.ToString(CultureInfo.InvariantCulture), SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                    AddSpec("FWK.Foun.SideArea.Stage." + stage.ToString(CultureInfo.InvariantCulture), SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFoundation);
                }
            }

            AddSpec("FWK.Kerb.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Kerb.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Kerb.Top", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Kerb.SubWall", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Kerb.SubCol", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Kerb.Length", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);

            AddSpec("FWK.Other.Total", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Other.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Other.Bottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);
            AddSpec("FWK.Other.SubStructure", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Floors);

            AddSpec("FWK.Lintel.Sides", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel);
            AddSpec("FWK.Lintel.Bottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel);
            AddSpec("FWK.Lintel.Length", SpecTypeId.Length, GroupTypeId.Data, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel);
            AddSpec("FWK.Drop.Soffit", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors, BuiltInCategory.OST_GenericModel);
            AddSpec("FWK.Drop.Stage.1", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors, BuiltInCategory.OST_GenericModel);
            AddSpec("FWK.Eave.Bottom", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors, BuiltInCategory.OST_GenericModel);
            AddSpec("FWK.Eave.Edge", SpecTypeId.Area, GroupTypeId.Data, BuiltInCategory.OST_Floors, BuiltInCategory.OST_GenericModel);

            BuiltInCategory[] finishCategories =
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };
            AddSpec("CBIM_QsFinishGrossArea", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("CBIM_QsFinishArea", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.Category", SpecTypeId.String.Text, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.Room", SpecTypeId.String.Text, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.RoomNumber", SpecTypeId.String.Text, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.RoomName", SpecTypeId.String.Text, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WF.Gross", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WF.Area", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WF.OpeningDeduct", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WF.Return", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.CF.Gross", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.CF.Area", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.CF.OpeningDeduct", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.CF.Return", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.SC.Gross", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.SC.Area", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.SC.OpeningDeduct", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.SC.Return", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.FF.Gross", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.FF.Area", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.FF.OpeningDeduct", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.FF.Return", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WP.Gross", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WP.Area", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WP.OpeningDeduct", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WP.Return", SpecTypeId.Area, GroupTypeId.Data, finishCategories);
            AddSpec("FIN.WP.Upturn", SpecTypeId.Area, GroupTypeId.Data, finishCategories);

            return specs;
        }

        private static bool EnsureSharedParameters(Autodesk.Revit.ApplicationServices.Application app, Document doc, string filePath, string groupName, List<QsSharedParamSpec> specs, IList<BuiltInCategory> categories)
        {
            if (app == null || doc == null || string.IsNullOrWhiteSpace(filePath)) return false;

            EnsureSharedParameterFile(filePath);

            string originalFile = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = filePath;
                DefinitionFile defFile = TryOpenSharedParameterFile(app);
                if (defFile == null)
                {
                    BackupSharedParameterFile(filePath);
                    EnsureSharedParameterFile(filePath, overwrite: true);
                    defFile = TryOpenSharedParameterFile(app);
                }
                if (defFile == null) return false;

                DefinitionGroup group = defFile.Groups.get_Item(groupName) ?? defFile.Groups.Create(groupName);
                List<Category> fallbackCategories = ResolveCategories(doc, categories);
                if (fallbackCategories.Count == 0) return false;

                using (Transaction t = new Transaction(doc, "CamboBIM - Bind QS Parameters"))
                {
                    t.Start();
                    BindingMap map = doc.ParameterBindings;
                    RemoveLegacyFwBindings(map);

                    // Global one-time cleanup: keep only one bound definition per managed name.
                    // This prevents repeated duplicate fields in schedules when legacy GUIDs exist.
                    foreach (string managedName in GetManagedParameterNames())
                    {
                        List<Definition> existingByName = GetBoundDefinitionsByName(map, managedName);
                        if (existingByName.Count > 1)
                        {
                            RemoveDuplicateBindingsByName(map, managedName, existingByName[0]);
                        }
                    }

                    foreach (QsSharedParamSpec spec in specs)
                    {
                        Definition def = FindSharedDefinitionByName(defFile, spec.Name) ?? group.Definitions.get_Item(spec.Name);
                        if (def == null)
                        {
                            var options = new ExternalDefinitionCreationOptions(spec.Name, spec.TypeId)
                            {
                                Visible = true
                            };
                            def = group.Definitions.Create(options);
                        }

                        // Keep only one binding per managed shared parameter name.
                        // Previous runs may have produced duplicated names with different GUIDs.
                        List<Definition> boundDefsByName = GetBoundDefinitionsByName(map, spec.Name);
                        Definition boundDefToKeep = boundDefsByName.FirstOrDefault();
                        Definition bindDef = boundDefToKeep ?? def;
                        RemoveDuplicateBindingsByName(map, spec.Name, bindDef);

                        List<Category> targetCategories = ResolveCategories(doc, spec.TargetCategories);
                        if (targetCategories.Count == 0)
                        {
                            targetCategories = fallbackCategories;
                        }

                        CategorySet set = app.Create.NewCategorySet();
                        foreach (Category c in targetCategories)
                        {
                            set.Insert(c);
                        }

                        ElementBinding existing = map.get_Item(bindDef) as ElementBinding;
                        if (existing == null)
                        {
                            InstanceBinding binding = app.Create.NewInstanceBinding(set);
                            if (!map.Insert(bindDef, binding, spec.ParamGroupTypeId))
                            {
                                map.ReInsert(bindDef, binding, spec.ParamGroupTypeId);
                            }
                        }
                        else if (!CategorySetEquals(existing.Categories, targetCategories))
                        {
                            InstanceBinding binding = app.Create.NewInstanceBinding(set);
                            map.ReInsert(bindDef, binding, spec.ParamGroupTypeId);
                        }
                    }

                    t.Commit();
                }

                return true;
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        private static DefinitionFile TryOpenSharedParameterFile(Autodesk.Revit.ApplicationServices.Application app)
        {
            if (app == null) return null;
            try
            {
                return app.OpenSharedParameterFile();
            }
            catch
            {
                return null;
            }
        }

        private static Definition FindSharedDefinitionByName(DefinitionFile defFile, string name)
        {
            if (defFile == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (DefinitionGroup g in defFile.Groups)
            {
                if (g == null || g.Definitions == null) continue;
                Definition d = g.Definitions.get_Item(name);
                if (d != null) return d;
            }

            return null;
        }

        private static List<Definition> GetBoundDefinitionsByName(BindingMap map, string name)
        {
            var result = new List<Definition>();
            if (map == null || string.IsNullOrWhiteSpace(name)) return result;

            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition current = it.Key;
                if (current == null) continue;
                if (!string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(current);
            }

            return result;
        }

        private static void RemoveDuplicateBindingsByName(BindingMap map, string name, Definition keepDefinition)
        {
            if (map == null || string.IsNullOrWhiteSpace(name)) return;

            var toRemove = new List<Definition>();
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition current = it.Key;
                if (current == null) continue;
                if (!string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (ReferenceEquals(current, keepDefinition)) continue;
                toRemove.Add(current);
            }

            foreach (Definition d in toRemove)
            {
                try
                {
                    map.Remove(d);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        private static void BackupSharedParameterFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                string dir = Path.GetDirectoryName(filePath) ?? "";
                string name = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string backup = Path.Combine(dir, $"{name}.corrupt.{stamp}{ext}.bak");
                File.Copy(filePath, backup, true);
            }
            catch
            {
                // Best effort backup only.
            }
        }

        private static void EnsureSharedParameterFile(string filePath, bool overwrite = false)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!overwrite && File.Exists(filePath))
            {
                if (IsCorruptSharedParameterFile(filePath))
                {
                    BackupSharedParameterFile(filePath);
                    overwrite = true;
                }
            }

            if (overwrite || !File.Exists(filePath))
            {
                const string header =
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Do not edit manually.\r\n" +
                    "*META\tVERSION\tMINVERSION\r\n" +
                    "META\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n";
                File.WriteAllText(filePath, header, new UTF8Encoding(false));
            }
        }

        private static bool IsCorruptSharedParameterFile(string filePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0) return true;
                if (bytes.Any(b => b == 0)) return true;

                string text = Encoding.UTF8.GetString(bytes);
                if (!text.Contains("*META\tVERSION\tMINVERSION")) return true;
                if (!text.Contains("*GROUP\tID\tNAME")) return true;
                if (!text.Contains("*PARAM\tGUID\tNAME\tDATATYPE")) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool CategorySetContains(CategorySet set, Category category)
        {
            if (set == null || category == null) return false;
            foreach (Category c in set)
            {
                if (c != null && category != null && c.Id.Value == category.Id.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CategorySetContainsAll(CategorySet set, IList<Category> categories)
        {
            if (set == null || categories == null || categories.Count == 0) return false;
            foreach (Category category in categories)
            {
                if (!CategorySetContains(set, category))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CategorySetEquals(CategorySet set, IList<Category> categories)
        {
            if (set == null || categories == null || categories.Count == 0) return false;

            int count = 0;
            foreach (Category _ in set) count++;
            if (count != categories.Count) return false;

            return CategorySetContainsAll(set, categories);
        }

        private static List<Category> ResolveCategories(Document doc, IList<BuiltInCategory> categories)
        {
            var result = new List<Category>();
            if (doc == null || categories == null) return result;

            foreach (BuiltInCategory bic in categories.Distinct())
            {
                Category cat = null;
                try
                {
                    cat = doc.Settings.Categories.get_Item(bic);
                }
                catch
                {
                    cat = null;
                }

                if (cat != null)
                {
                    result.Add(cat);
                }
            }

            return result;
        }

        private static void RemoveLegacyFwBindings(BindingMap map)
        {
            if (map == null) return;

            var defsToRemove = new List<Definition>();
            DefinitionBindingMapIterator iterator = map.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition def = iterator.Key;
                if (def == null) continue;

                string name = def.Name ?? "";
                if (name.StartsWith("FW.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("CBIM.FW.", StringComparison.OrdinalIgnoreCase))
                {
                    defsToRemove.Add(def);
                }
            }

            foreach (Definition def in defsToRemove)
            {
                try
                {
                    map.Remove(def);
                }
                catch
                {
                    // ignore legacy cleanup failures
                }
            }
        }

        private static bool WriteBeamQsParameters(
            Document doc,
            FamilyInstance instance,
            QsMeasurementRuntimeRules rules,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> generics,
            bool createShape)
        {
            if (instance == null || instance.Symbol == null) return false;

            double length = GetElementLength(instance);
            double width = GetTypeWidth(instance.Symbol);
            double depth = GetTypeDepth(instance.Symbol);
            double volume = GetElementVolume(instance);
            if (volume <= 1e-9 && length > 0 && width > 0 && depth > 0)
            {
                volume = length * width * depth;
            }

            double sideArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                instance,
                rules.BeamIncludeSide,
                rules.BeamIncludeBottom,
                rules.BeamIncludeTop,
                out sideArea,
                out bottomArea,
                out topArea);
            double totalArea = sideArea + bottomArea + topArea;
            bool hasTasBeamTakeoff = TryBuildBeamTasTakeoff(
                doc,
                instance,
                columns,
                rules.BeamIncludeSide,
                rules.BeamIncludeBottom,
                rules.BeamIncludeTop,
                rules.BeamDeductColumn,
                out BeamTasTakeoff tasBeamTakeoff);
            if (hasTasBeamTakeoff)
            {
                sideArea = tasBeamTakeoff.GrossSideArea;
                bottomArea = tasBeamTakeoff.BottomGrossArea;
                topArea = tasBeamTakeoff.TopGrossArea;
                totalArea = tasBeamTakeoff.GrossArea;
                volume = tasBeamTakeoff.VolumeNet;
            }

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };
            BoundingBoxXYZ hostBox = instance.get_BoundingBox(null);
            double subFoun = rules.BeamDeductFoundation ? SumIntersectionArea(formworkSlabs, hostBox, foundations, opt, instance.Id) : 0.0;
            double subBeam = rules.BeamDeductBeam ? SumIntersectionArea(formworkSlabs, hostBox, beams, opt, instance.Id) : 0.0;
            double subCol = rules.BeamDeductColumn ? SumIntersectionArea(formworkSlabs, hostBox, columns, opt, instance.Id) : 0.0;
            double subWall = rules.BeamDeductWall ? SumIntersectionArea(formworkSlabs, hostBox, walls, opt, instance.Id) : 0.0;
            double subFloor = rules.BeamDeductFloor && rules.BeamIncludeTop ? SumIntersectionArea(formworkSlabs, hostBox, floors, opt, instance.Id) : 0.0;
            double subGeneric = rules.BeamDeductGeneric ? SumIntersectionArea(formworkSlabs, hostBox, generics, opt, instance.Id) : 0.0;
            if (hasTasBeamTakeoff)
            {
                subCol = Math.Max(subCol, tasBeamTakeoff.ColumnFormworkDeductionArea);
            }

            double finalArea = totalArea - subFoun - subBeam - subCol - subWall - subFloor - subGeneric;
            if (finalArea < 0) finalArea = 0;

            double strutHeight = 0.0;
            int strutStage = 0;
            double strutBasicArea = 0.0;
            double[] strutStageAreas = new double[BeamStrutPersistedStageLimit + 1];
            ComputeBeamStrutQuantities(
                doc,
                instance,
                floors,
                rules,
                finalArea,
                opt,
                out strutHeight,
                out strutStage,
                out strutBasicArea,
                strutStageAreas);

            bool changed = false;
            changed |= SetParamYesNo(instance, "FWK.Enable", true);
            changed |= SetParamYesNo(instance, "FWK.EnableSelf", true);
            changed |= SetParamDouble(instance, "CBIM_Beam_Length", length);
            changed |= SetParamDouble(instance, "CBIM_Beam_Width", width);
            changed |= SetParamDouble(instance, "CBIM_Beam_Depth", depth);
            changed |= SetParamDouble(instance, "CBIM_Beam_Volume", volume);

            string typeName = instance.Symbol.Name ?? "";
            changed |= SetParamString(instance, "CBIM_Beam_Type", typeName);

            string levelName = "";
            if (instance.LevelId != ElementId.InvalidElementId)
            {
                Level level = doc.GetElement(instance.LevelId) as Level;
                if (level != null)
                {
                    levelName = level.Name ?? "";
                }
            }
            changed |= SetParamString(instance, "CBIM_Beam_Level", levelName);

            changed |= SetParamDouble(instance, "FWK.Beam.Total", totalArea);
            changed |= SetParamDouble(instance, "FWK.Beam.Sides", sideArea);
            changed |= SetParamDouble(instance, "FWK.Beam.Bottom", bottomArea);
            changed |= SetParamDouble(instance, "FWK.Beam.Top", topArea);
            changed |= SetParamDouble(instance, "FWK.Beam.SubFoun", subFoun);
            changed |= SetParamDouble(instance, "FWK.Beam.SubBeam", subBeam);
            changed |= SetParamDouble(instance, "FWK.Beam.SubCol", subCol);
            changed |= SetParamDouble(instance, "FWK.Beam.SubWall", subWall);
            changed |= SetParamDouble(instance, "FWK.Beam.SubFloor", subFloor);
            changed |= SetParamDouble(instance, "FWK.Beam.SubGeneric", subGeneric);
            changed |= SetParamDouble(instance, "FWK.Beam.StrutHeight", strutHeight);
            changed |= SetParamDouble(instance, "FWK.Beam.StrutStage", strutStage);
            changed |= SetParamDouble(instance, "FWK.Beam.Stage.Basic", strutBasicArea);
            for (int stage = 1; stage <= BeamStrutPersistedStageLimit; stage++)
            {
                changed |= SetParamDouble(instance, "FWK.Beam.Stage." + stage.ToString(CultureInfo.InvariantCulture), strutStageAreas[stage]);
            }

            changed |= SetParamDouble(instance, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(instance, "CBIM_QsRuleCode", BuildRuleCode(
                ("BEAM.SIDE", rules.BeamIncludeSide),
                ("BEAM.BOTTOM", rules.BeamIncludeBottom),
                ("BEAM.TOP", rules.BeamIncludeTop),
                ("BEAM.DEDUCT.FOUN", rules.BeamDeductFoundation),
                ("BEAM.DEDUCT.BEAM", rules.BeamDeductBeam),
                ("BEAM.DEDUCT.COL", rules.BeamDeductColumn),
                ("BEAM.DEDUCT.WALL", rules.BeamDeductWall),
                ("BEAM.DEDUCT.FLOOR", rules.BeamDeductFloor),
                ("BEAM.DEDUCT.OTHER", rules.BeamDeductGeneric),
                ("BEAM.STRUT.METHOD", rules.BeamStrutEnabled),
                ("BEAM.STRUT.STAGED", rules.BeamStrutStaged)));
            changed |= SetParamString(instance, "CBIM_QsFormula", BuildBeamFormworkFormula(rules));
            string beamStrutBucketName = strutStage > 0
                ? "Stage." + strutStage.ToString(CultureInfo.InvariantCulture)
                : "Stage.Basic";
            double beamStrutBucketArea = strutStage > 0 ? strutStageAreas[strutStage] : strutBasicArea;
            changed |= SetParamString(instance, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Beam",
                ("Sides", sideArea),
                ("Bottom", bottomArea),
                ("Top", topArea),
                ("SubFoun", -subFoun),
                ("SubBeam", -subBeam),
                ("SubCol", -subCol),
                ("SubWall", -subWall),
                ("SubFloor", -subFloor),
                ("SubGeneric", -subGeneric),
                (beamStrutBucketName, beamStrutBucketArea),
                ("Net", finalArea)));

            if (createShape)
            {
                if (rules.BeamDeductColumn)
                {
                    formworkSlabs = ClipFormworkSlabsByPriorityElements(formworkSlabs, columns, opt, hostBox, instance.Id);
                }

                CreateFormworkShapes(doc, instance, "Beam", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteColumnQsParameters(
            Document doc,
            Element element,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> generics,
            QsMeasurementRuntimeRules rules,
            bool createShape)
        {
            if (doc == null || element == null) return false;

            double sidesArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                rules.ColumnIncludeSide,
                rules.ColumnIncludeTopBottom,
                rules.ColumnIncludeTopBottom,
                out sidesArea,
                out bottomArea,
                out topArea);
            double topBottomArea = bottomArea + topArea;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };
            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            double subFoun = rules.ColumnDeductFoundation ? SumIntersectionArea(formworkSlabs, hostBox, foundations, opt, element.Id) : 0.0;

            double subBeam = 0.0;
            if (rules.ColumnDeductBeam)
            {
                Func<Element, bool> beamFilter = null;
                if (rules.ColumnBeamGreaterOrEqual)
                {
                    double colSize = GetElementSectionMaxSize(element);
                    beamFilter = b =>
                    {
                        if (b == null) return false;
                        return GetElementSectionMaxSize(b) + 1e-9 >= colSize;
                    };
                }

                subBeam = SumIntersectionArea(formworkSlabs, hostBox, beams, opt, element.Id, beamFilter);
            }

            double subCol = rules.ColumnDeductColumn ? SumIntersectionArea(formworkSlabs, hostBox, columns, opt, element.Id) : 0.0;
            double subFloor = rules.ColumnDeductFloor ? SumIntersectionArea(formworkSlabs, hostBox, floors, opt, element.Id) : 0.0;
            double subWall = rules.ColumnDeductWall ? SumIntersectionArea(formworkSlabs, hostBox, walls, opt, element.Id) : 0.0;
            double subGeneric = rules.ColumnDeductGeneric ? SumIntersectionArea(formworkSlabs, hostBox, generics, opt, element.Id) : 0.0;

            double totalArea = sidesArea + topBottomArea;
            double finalArea = totalArea - subFoun - subBeam - subCol - subWall - subFloor - subGeneric;
            if (finalArea < 0) finalArea = 0;

            double columnStrutHeight = 0.0;
            int columnStrutStage = 0;
            double columnStrutBasicArea = 0.0;
            double[] columnStrutStageAreas = new double[ColumnStrutPersistedStageLimit + 1];
            ComputeColumnStrutQuantities(
                element,
                formworkSlabs,
                rules,
                finalArea,
                out columnStrutHeight,
                out columnStrutStage,
                out columnStrutBasicArea,
                columnStrutStageAreas);

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Col.Total", totalArea);
            changed |= SetParamDouble(element, "FWK.Col.Sides", sidesArea);
            changed |= SetParamDouble(element, "FWK.Col.TopBottom", topBottomArea);
            changed |= SetParamDouble(element, "FWK.Col.SubFoun", subFoun);
            changed |= SetParamDouble(element, "FWK.Col.SubBeam", subBeam);
            changed |= SetParamDouble(element, "FWK.Col.SubCol", subCol);
            changed |= SetParamDouble(element, "FWK.Col.SubWall", subWall);
            changed |= SetParamDouble(element, "FWK.Col.SubFloor", subFloor);
            changed |= SetParamDouble(element, "FWK.Col.SubGeneric", subGeneric);
            changed |= SetParamDouble(element, "FWK.Col.StrutHeight", columnStrutHeight);
            changed |= SetParamDouble(element, "FWK.Col.StrutStage", columnStrutStage);
            changed |= SetParamDouble(element, "FWK.Col.Stage.Basic", columnStrutBasicArea);
            for (int stage = 1; stage <= ColumnStrutPersistedStageLimit; stage++)
            {
                changed |= SetParamDouble(element, "FWK.Col.Stage." + stage.ToString(CultureInfo.InvariantCulture), columnStrutStageAreas[stage]);
            }
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("COL.SIDE", rules.ColumnIncludeSide),
                ("COL.TOPBOTTOM", rules.ColumnIncludeTopBottom),
                ("COL.STRUT.METHOD", rules.ColumnStrutEnabled),
                ("COL.STRUT.STAGED", rules.ColumnStrutStaged),
                ("COL.DEDUCT.FOUN", rules.ColumnDeductFoundation),
                ("COL.DEDUCT.BEAM", rules.ColumnDeductBeam),
                ("COL.DEDUCT.BEAM.SIZE", rules.ColumnBeamGreaterOrEqual),
                ("COL.DEDUCT.COL", rules.ColumnDeductColumn),
                ("COL.DEDUCT.FLOOR", rules.ColumnDeductFloor),
                ("COL.DEDUCT.WALL", rules.ColumnDeductWall),
                ("COL.DEDUCT.OTHER", rules.ColumnDeductGeneric)));
            changed |= SetParamString(element, "CBIM_QsFormula", BuildColumnFormworkFormula(rules));
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Column",
                ("Sides", sidesArea),
                ("TopBottom", topBottomArea),
                ("SubFoun", -subFoun),
                ("SubBeam", -subBeam),
                ("SubCol", -subCol),
                ("SubWall", -subWall),
                ("SubFloor", -subFloor),
                ("SubGeneric", -subGeneric),
                ("Stage.Basic", columnStrutBasicArea),
                ("Stage." + columnStrutStage.ToString(CultureInfo.InvariantCulture), columnStrutStage > 0 && columnStrutStage < columnStrutStageAreas.Length ? columnStrutStageAreas[columnStrutStage] : 0.0),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Column", formworkSlabs);
            }
            return changed;
        }

        private static bool WriteFloorQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> stairs,
            IList<Element> generics,
            bool createShape)
        {
            if (doc == null || element == null) return false;

            double sidesArea = 0.0;
            double openingSideArea = 0.0;
            double bottomArea = 0.0;
            double topSlopeArea = 0.0;
            double topSlopeThreshold = rules.SlabTopSlopeEnabled ? rules.SlabTopSlopeDegrees : -1.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                rules.SlabIncludeSide,
                rules.SlabIncludeBottom,
                false,
                out sidesArea,
                out bottomArea,
                out topSlopeArea,
                topSlopeThreshold);
            ApplySlabOpeningSideThreshold(element, formworkSlabs, rules, ref sidesArea, ref openingSideArea);

            List<SlabOpeningMeasure> slabOpenings = GetSlabOpeningMeasures(element, 0.0);
            double openingCount = slabOpenings.Count;
            double openingArea = slabOpenings.Sum(opening => opening?.ProjectedAreaFt2 ?? 0.0);
            double openingGirth = slabOpenings.Sum(EstimateHorizontalOpeningPerimeterFt);

            double edgeBreakLte250 = 0.0;
            double edgeBreakLte500 = 0.0;
            double edgeBreakLte1000 = 0.0;
            double edgeBreakOver1000 = 0.0;
            ComputeSlabEdgeBreakAreas(
                formworkSlabs,
                rules,
                out edgeBreakLte250,
                out edgeBreakLte500,
                out edgeBreakLte1000,
                out edgeBreakOver1000);

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double strutHeight = 0.0;
            double strutSoffitArea = 0.0;
            double strutStageCount = 0.0;
            double strutStageArea = 0.0;
            double strutEdgeArea = 0.0;
            double strutEdgeStageArea = 0.0;
            double strutTopArea = 0.0;
            double strutTopStageArea = 0.0;
            double[] strutStageAreas = new double[SlabStrutPersistedStageLimit + 1];
            ComputeSlabStrutQuantities(
                doc,
                element,
                floors,
                rules,
                sidesArea,
                bottomArea,
                topSlopeArea,
                opt,
                out strutHeight,
                out strutSoffitArea,
                out strutStageCount,
                out strutStageArea,
                out strutEdgeArea,
                out strutEdgeStageArea,
                out strutTopArea,
                out strutTopStageArea,
                strutStageAreas);
            double strutBasicArea = rules.SlabIncludeBottom && strutStageCount <= 1e-9 ? bottomArea : 0.0;

            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            List<FaceSlab> soffitSlabs = FilterFaceSlabsByType(formworkSlabs, "Bottom", "TopSlope");
            double subFoun = rules.SlabDeductFoundation ? SumSlabSoffitContactArea(soffitSlabs, hostBox, foundations, opt, element.Id) : 0.0;
            double subBeam = rules.SlabDeductBeam ? SumSlabSoffitContactArea(soffitSlabs, hostBox, beams, opt, element.Id) : 0.0;
            double subCol = rules.SlabDeductColumn ? SumSlabSoffitContactArea(soffitSlabs, hostBox, columns, opt, element.Id) : 0.0;
            double subWall = rules.SlabDeductWall ? SumSlabSoffitContactArea(soffitSlabs, hostBox, walls, opt, element.Id) : 0.0;
            double subFloor = rules.SlabDeductSlab ? SumSlabSoffitContactArea(soffitSlabs, hostBox, floors, opt, element.Id) : 0.0;
            double subStair = rules.SlabDeductStair ? SumSlabSoffitContactArea(soffitSlabs, hostBox, stairs, opt, element.Id) : 0.0;
            double subGeneric = rules.SlabDeductGeneric ? SumSlabSoffitContactArea(soffitSlabs, hostBox, generics, opt, element.Id) : 0.0;

            double totalArea = sidesArea + bottomArea + topSlopeArea;
            double soffitArea = (rules.SlabIncludeBottom ? bottomArea : 0.0) + topSlopeArea;
            double finalArea = soffitArea - subFoun - subBeam - subCol - subWall - subFloor - subStair - subGeneric;
            if (finalArea < 0) finalArea = 0;

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Floor.Total", totalArea);
            changed |= SetParamDouble(element, "FWK.Floor.Bottom", rules.SlabIncludeBottom ? bottomArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Floor.Sides", sidesArea);
            changed |= SetParamDouble(element, "FWK.Floor.OpeningSide", openingSideArea);
            changed |= SetParamDouble(element, "FWK.Floor.Opening.Count", openingCount);
            changed |= SetParamDouble(element, "FWK.Floor.Opening.Area", openingArea);
            changed |= SetParamDouble(element, "FWK.Floor.Opening.Girth", openingGirth);
            changed |= SetParamDouble(element, "FWK.Floor.EdgeBreak.Lte250", edgeBreakLte250);
            changed |= SetParamDouble(element, "FWK.Floor.EdgeBreak.Lte500", edgeBreakLte500);
            changed |= SetParamDouble(element, "FWK.Floor.EdgeBreak.Lte1000", edgeBreakLte1000);
            changed |= SetParamDouble(element, "FWK.Floor.EdgeBreak.Over1000", edgeBreakOver1000);
            changed |= SetParamDouble(element, "FWK.Floor.TopSlope", topSlopeArea);
            changed |= SetParamDouble(element, "FWK.Floor.StrutHeight", strutHeight);
            changed |= SetParamDouble(element, "FWK.Floor.StrutBasic", strutBasicArea);
            changed |= SetParamDouble(element, "FWK.Floor.StrutSoffit", strutSoffitArea);
            changed |= SetParamDouble(element, "FWK.Floor.StrutStageCount", strutStageCount);
            changed |= SetParamDouble(element, "FWK.Floor.StrutStageArea", strutStageArea);
            for (int stage = 1; stage <= SlabStrutPersistedStageLimit; stage++)
            {
                changed |= SetParamDouble(element, "FWK.Floor.StrutStage." + stage.ToString(CultureInfo.InvariantCulture), strutStageAreas[stage]);
            }
            changed |= SetParamDouble(element, "FWK.Floor.StrutEdge", strutEdgeArea);
            changed |= SetParamDouble(element, "FWK.Floor.StrutEdgeStageArea", strutEdgeStageArea);
            changed |= SetParamDouble(element, "FWK.Floor.StrutTop", strutTopArea);
            changed |= SetParamDouble(element, "FWK.Floor.StrutTopStageArea", strutTopStageArea);
            changed |= SetParamDouble(element, "FWK.Floor.SubFoun", subFoun);
            changed |= SetParamDouble(element, "FWK.Floor.SubBeam", subBeam);
            changed |= SetParamDouble(element, "FWK.Floor.SubCol", subCol);
            changed |= SetParamDouble(element, "FWK.Floor.SubWall", subWall);
            changed |= SetParamDouble(element, "FWK.Floor.SubFloor", subFloor);
            changed |= SetParamDouble(element, "FWK.Floor.SubStair", subStair);
            changed |= SetParamDouble(element, "FWK.Floor.SubGeneric", subGeneric);
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("SLAB.SIDE", rules.SlabIncludeSide),
                ("SLAB.BOTTOM", rules.SlabIncludeBottom),
                ("SLAB.TOP.SLOPE", rules.SlabTopSlopeEnabled),
                ("SLAB.OPENING.SIDE", rules.SlabOpeningSideRuleEnabled),
                ("SLAB.EDGE.METHOD", rules.SlabEdgeSegmentationEnabled),
                ("SLAB.STRUT.SOFFIT", rules.SlabStrutSoffitEnabled),
                ("SLAB.STRUT.EDGE", rules.SlabStrutEdgeEnabled),
                ("SLAB.STRUT.TOPFWK", rules.SlabStrutTopFormworkEnabled),
                ("SLAB.DEDUCT.FOUN", rules.SlabDeductFoundation),
                ("SLAB.DEDUCT.BEAM", rules.SlabDeductBeam),
                ("SLAB.DEDUCT.COL", rules.SlabDeductColumn),
                ("SLAB.DEDUCT.WALL", rules.SlabDeductWall),
                ("SLAB.DEDUCT.SLAB", rules.SlabDeductSlab),
                ("SLAB.DEDUCT.STAIR", rules.SlabDeductStair),
                ("SLAB.DEDUCT.OTHER", rules.SlabDeductGeneric)));
            changed |= SetParamString(element, "CBIM_QsFormula", BuildSlabFormworkFormula(rules));
            string slabBreakdown = BuildAreaAuditBreakdown(
                "Slab",
                ("Soffit", soffitArea),
                ("Sides", sidesArea),
                ("Bottom", rules.SlabIncludeBottom ? bottomArea : 0.0),
                ("OpeningSide", openingSideArea),
                ("OpeningArea", openingArea),
                ("TopSlope", topSlopeArea),
                ("Edge<=250", edgeBreakLte250),
                ("Edge<=500", edgeBreakLte500),
                ("Edge<=1000", edgeBreakLte1000),
                ("Edge>1000", edgeBreakOver1000),
                ("StrutBasic", strutBasicArea),
                ("StrutSoffit", strutSoffitArea),
                ("StrutStageArea", strutStageArea),
                ("StrutStage." + ((int)strutStageCount).ToString(CultureInfo.InvariantCulture), strutStageCount > 0 && strutStageCount < strutStageAreas.Length ? strutStageAreas[(int)strutStageCount] : 0.0),
                ("SubFoun", -subFoun),
                ("SubBeam", -subBeam),
                ("SubCol", -subCol),
                ("SubWall", -subWall),
                ("SubFloor", -subFloor),
                ("SubStair", -subStair),
                ("SubGeneric", -subGeneric),
                ("Net", finalArea));
            slabBreakdown += "; OpeningCount=" + openingCount.ToString("0", CultureInfo.InvariantCulture) +
                             "; OpeningGirth=" + FormatAuditLength(openingGirth);
            changed |= SetParamString(element, "CBIM_QsBreakdown", slabBreakdown);

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Floor", formworkSlabs);
            }
            return changed;
        }

        private static bool WriteStairQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> generics,
            bool createShape)
        {
            if (doc == null || element == null) return false;

            List<FaceSlab> formworkSlabs = BuildStairPaintingFaceSlabs(
                element,
                rules.StairPaintingEnabled,
                out double paintingSideArea,
                out double paintingBottomArea);
            double paintingArea = paintingSideArea + paintingBottomArea;

            double bottomArea = 0.0;
            double topArea = 0.0;
            if (rules.StairIncludeBottom || rules.StairIncludeTop)
            {
                List<FaceSlab> legacySlabs = BuildFormworkFaceSlabs(
                    element,
                    includeSides: false,
                    includeBottom: rules.StairIncludeBottom,
                    includeTop: rules.StairIncludeTop,
                    out _,
                    out bottomArea,
                    out topArea);
                formworkSlabs.AddRange(legacySlabs);
            }

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };
            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);

            double subBeam = rules.StairDeductBeam ? SumIntersectionArea(formworkSlabs, hostBox, beams, opt, element.Id) : 0.0;
            double subFoun = rules.StairDeductOther ? SumIntersectionArea(formworkSlabs, hostBox, foundations, opt, element.Id) : 0.0;
            double subCol = rules.StairDeductOther ? SumIntersectionArea(formworkSlabs, hostBox, columns, opt, element.Id) : 0.0;
            double subWall = rules.StairDeductOther ? SumIntersectionArea(formworkSlabs, hostBox, walls, opt, element.Id) : 0.0;
            double subFloor = rules.StairDeductOther ? SumIntersectionArea(formworkSlabs, hostBox, floors, opt, element.Id) : 0.0;
            double subGeneric = rules.StairDeductOther ? SumIntersectionArea(formworkSlabs, hostBox, generics, opt, element.Id) : 0.0;

            double grossArea = (rules.StairPaintingEnabled ? paintingArea : 0.0) +
                               (rules.StairIncludeBottom ? bottomArea : 0.0) +
                               (rules.StairIncludeTop ? topArea : 0.0);
            double finalArea = grossArea -
                               subFoun - subBeam - subCol - subWall - subFloor - subGeneric;
            if (finalArea < 0) finalArea = 0;

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Stair.Total", grossArea);
            changed |= SetParamDouble(element, "FWK.Stair.Painting", rules.StairPaintingEnabled ? paintingArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Stair.Sides", rules.StairPaintingEnabled ? paintingSideArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Stair.PaintingSide", rules.StairPaintingEnabled ? paintingSideArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Stair.PaintingBottom", rules.StairPaintingEnabled ? paintingBottomArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Stair.Bottom", rules.StairIncludeBottom ? bottomArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Stair.Top", rules.StairIncludeTop ? topArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Stair.SubFoun", subFoun);
            changed |= SetParamDouble(element, "FWK.Stair.SubBeam", subBeam);
            changed |= SetParamDouble(element, "FWK.Stair.SubCol", subCol);
            changed |= SetParamDouble(element, "FWK.Stair.SubWall", subWall);
            changed |= SetParamDouble(element, "FWK.Stair.SubFloor", subFloor);
            changed |= SetParamDouble(element, "FWK.Stair.SubGeneric", subGeneric);
            changed |= SetParamDouble(element, "FWK.Stair.StepCount", GetStairStepCount(element));
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("STAIR.PAINTING", rules.StairPaintingEnabled),
                ("STAIR.BOTTOM", rules.StairIncludeBottom),
                ("STAIR.TOP", rules.StairIncludeTop),
                ("STAIR.DEDUCT.BEAM", rules.StairDeductBeam),
                ("STAIR.DEDUCT.OTHER", rules.StairDeductOther)));
            changed |= SetParamString(element, "CBIM_QsFormula", BuildStairFormworkFormula(rules));
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Stair",
                ("Painting", rules.StairPaintingEnabled ? paintingArea : 0.0),
                ("PaintingSide", rules.StairPaintingEnabled ? paintingSideArea : 0.0),
                ("PaintingBottom", rules.StairPaintingEnabled ? paintingBottomArea : 0.0),
                ("Bottom", rules.StairIncludeBottom ? bottomArea : 0.0),
                ("Top", rules.StairIncludeTop ? topArea : 0.0),
                ("SubFoun", -subFoun),
                ("SubBeam", -subBeam),
                ("SubCol", -subCol),
                ("SubWall", -subWall),
                ("SubFloor", -subFloor),
                ("SubGeneric", -subGeneric),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Stair", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteWallQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> generics,
            bool createShape)
        {
            if (doc == null || element == null) return false;

            double sideArea = 0.0;
            double openingSideArea = 0.0;
            double openingBottomArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildWallFormworkFaceSlabs(
                element,
                rules,
                out sideArea,
                out openingSideArea,
                out openingBottomArea,
                out double joinedEndCapArea);
            double totalArea = sideArea + openingBottomArea;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            double subFoun = rules.WallDeductFoundation ? SumIntersectionArea(formworkSlabs, hostBox, foundations, opt, element.Id) : 0.0;
            double subBeam = rules.WallDeductBeam ? SumIntersectionArea(formworkSlabs, hostBox, beams, opt, element.Id) : 0.0;
            double subCol = rules.WallDeductColumn ? SumIntersectionArea(formworkSlabs, hostBox, columns, opt, element.Id) : 0.0;
            double rawSubWall = rules.WallDeductWall ? SumIntersectionArea(formworkSlabs, hostBox, walls, opt, element.Id) : 0.0;
            double subWall = rules.WallDeductWall ? Math.Max(0.0, rawSubWall - joinedEndCapArea) : 0.0;
            double subFloor = rules.WallDeductFloor ? SumIntersectionArea(formworkSlabs, hostBox, floors, opt, element.Id) : 0.0;
            double subGeneric = rules.WallDeductGeneric ? SumIntersectionArea(formworkSlabs, hostBox, generics, opt, element.Id) : 0.0;

            double finalArea = totalArea - subFoun - subBeam - subCol - subWall - subFloor - subGeneric;
            if (finalArea < 0) finalArea = 0;

            double[] wallEdgeLengthStages = new double[WallEdgePersistedStageLimit + 1];
            double[] wallEdgeAreaStages = new double[WallEdgePersistedStageLimit + 1];
            ComputeWallEdgeBreakQuantities(
                element,
                rules,
                finalArea,
                wallEdgeLengthStages,
                wallEdgeAreaStages);

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Wall.Total", totalArea);
            changed |= SetParamDouble(element, "FWK.Wall.Sides", sideArea);
            changed |= SetParamDouble(element, "FWK.Wall.OpeningSide", openingSideArea);
            changed |= SetParamDouble(element, "FWK.Wall.OpeningBottom", openingBottomArea);
            changed |= SetParamDouble(element, "FWK.Wall.JoinedEndCap", joinedEndCapArea);
            changed |= SetParamDouble(element, "FWK.Wall.SubFoun", subFoun);
            changed |= SetParamDouble(element, "FWK.Wall.SubBeam", subBeam);
            changed |= SetParamDouble(element, "FWK.Wall.SubCol", subCol);
            changed |= SetParamDouble(element, "FWK.Wall.SubWall", subWall);
            changed |= SetParamDouble(element, "FWK.Wall.SubFloor", subFloor);
            changed |= SetParamDouble(element, "FWK.Wall.SubGeneric", subGeneric);
            changed |= SetParamDouble(element, "FWK.Wall.OriginalHeight", GetElementVerticalHeight(element));
            for (int stage = 0; stage <= WallEdgePersistedStageLimit; stage++)
            {
                string stageText = stage.ToString(CultureInfo.InvariantCulture);
                changed |= SetParamDouble(element, "FWK.Wall.EdgeLength.Stage." + stageText, wallEdgeLengthStages[stage]);
                changed |= SetParamDouble(element, "FWK.Wall.EdgeArea.Stage." + stageText, wallEdgeAreaStages[stage]);
            }
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("WALL.SIDE", rules.WallIncludeSide),
                ("WALL.ENDCAP", rules.WallExcludeJoinedEndCaps),
                ("WALL.OPENING.SIDE", rules.WallOpeningSideRuleEnabled),
                ("WALL.OPENING.BOTTOM", rules.WallIncludeOpeningBottom),
                ("WALL.EDGE.METHOD", rules.WallEdgeSegmentationEnabled),
                ("WALL.DEDUCT.FOUN", rules.WallDeductFoundation),
                ("WALL.DEDUCT.BEAM", rules.WallDeductBeam),
                ("WALL.DEDUCT.COL", rules.WallDeductColumn),
                ("WALL.DEDUCT.WALL", rules.WallDeductWall),
                ("WALL.DEDUCT.FLOOR", rules.WallDeductFloor),
                ("WALL.DEDUCT.OTHER", rules.WallDeductGeneric)));
            changed |= SetParamString(element, "CBIM_QsFormula", "Net = Sides + OpeningBottom - SubFoun - SubBeam - SubCol - SubWall - SubFloor - SubGeneric");
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Wall",
                ("Sides", sideArea),
                ("OpeningSide", openingSideArea),
                ("OpeningBottom", openingBottomArea),
                ("JoinedEndCap", joinedEndCapArea),
                ("SubFoun", -subFoun),
                ("SubBeam", -subBeam),
                ("SubCol", -subCol),
                ("SubWall", -subWall),
                ("SubFloor", -subFloor),
                ("SubGeneric", -subGeneric),
                ("EdgeLength.Stage.0", wallEdgeLengthStages[0]),
                ("EdgeLength.Stage.1", wallEdgeLengthStages[1]),
                ("EdgeArea.Stage.3", wallEdgeAreaStages.Length > 3 ? wallEdgeAreaStages[3] : 0.0),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Wall", formworkSlabs);
            }
            return changed;
        }

        private static List<FaceSlab> BuildWallFormworkFaceSlabs(
            Element element,
            QsMeasurementRuntimeRules rules,
            out double sideArea,
            out double openingSideArea,
            out double openingBottomArea,
            out double joinedEndCapArea)
        {
            sideArea = 0.0;
            openingSideArea = 0.0;
            openingBottomArea = 0.0;
            joinedEndCapArea = 0.0;
            bool includeSides = rules?.WallIncludeSide ?? true;

            // Start from all side faces.
            List<FaceSlab> slabs = BuildFormworkFaceSlabs(element, includeSides: includeSides, includeBottom: false, includeTop: false, out sideArea, out _, out _);
            if (element is Wall wall)
            {
                if (rules == null || rules.WallExcludeJoinedEndCaps)
                {
                    joinedEndCapArea = RemoveWallJoinedEndCapSlabs(wall, slabs, ref sideArea);
                }

                ApplyWallOpeningSideThreshold(wall, slabs, rules, ref sideArea, ref openingSideArea);
            }

            if (rules == null || !rules.WallIncludeOpeningBottom || element == null)
            {
                return slabs;
            }

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            List<Solid> solids = GetElementSolids(element, opt);
            const double zTol = 0.02;
            double halfThickness = FormworkHalfThicknessFt;

            bool hasDown = false;
            double minDownZ = double.MaxValue;
            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;
                    if (pf.FaceNormal.Z < -0.9)
                    {
                        hasDown = true;
                        if (pf.Origin.Z < minDownZ) minDownZ = pf.Origin.Z;
                    }
                }
            }

            if (!hasDown) return slabs;

            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;
                    if (pf.FaceNormal.Z >= -0.9) continue;
                    if (pf.Origin.Z <= minDownZ + zTol) continue; // skip base underside, keep opening bottoms

                    double area = pf.Area;
                    if (area <= 1e-9) continue;
                    openingBottomArea += area;

                    Solid slab = CreateFaceSlab(pf, halfThickness);
                    if (slab != null && slab.Volume > 1e-9)
                    {
                        slabs.Add(new FaceSlab(slab, halfThickness * 2.0, "OpeningBottom", area, pf.FaceNormal, pf.Origin));
                    }
                }
            }

            return slabs;
        }

        private static void ApplyWallOpeningSideThreshold(
            Wall wall,
            List<FaceSlab> slabs,
            QsMeasurementRuntimeRules rules,
            ref double sideArea,
            ref double openingSideArea)
        {
            if (wall == null || slabs == null || slabs.Count == 0 || rules == null) return;
            if (!rules.WallOpeningSideRuleEnabled)
            {
                RemoveAllMatchedWallOpeningSideSlabs(wall, slabs, ref sideArea);
                return;
            }

            List<WallOpeningMeasure> openings = GetWallOpeningMeasures(wall, rules.WallOpeningSideThresholdFt2);
            if (openings.Count == 0) return;

            XYZ wallDir = GetWallDirection(wall);
            if (wallDir == null) return;

            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                FaceSlab slab = slabs[i];
                if (!IsCandidateWallOpeningSideSlab(slab, wallDir)) continue;

                WallOpeningMeasure opening = FindContainingOpening(openings, slab.FacePoint);
                if (opening == null) continue;

                if (opening.IncludeSide)
                {
                    openingSideArea += slab.FaceArea;
                }
                else
                {
                    sideArea -= slab.FaceArea;
                    slabs.RemoveAt(i);
                }
            }

            if (sideArea < 0) sideArea = 0;
        }

        private static void RemoveAllMatchedWallOpeningSideSlabs(Wall wall, List<FaceSlab> slabs, ref double sideArea)
        {
            List<WallOpeningMeasure> openings = GetWallOpeningMeasures(wall, 0.0);
            if (openings.Count == 0) return;

            XYZ wallDir = GetWallDirection(wall);
            if (wallDir == null) return;

            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                FaceSlab slab = slabs[i];
                if (!IsCandidateWallOpeningSideSlab(slab, wallDir)) continue;
                if (FindContainingOpening(openings, slab.FacePoint) == null) continue;

                sideArea -= slab.FaceArea;
                slabs.RemoveAt(i);
            }

            if (sideArea < 0) sideArea = 0;
        }

        private static List<WallOpeningMeasure> GetWallOpeningMeasures(Wall wall, double thresholdFt2)
        {
            var result = new List<WallOpeningMeasure>();
            if (wall == null || wall.Document == null) return result;

            XYZ wallDir = GetWallDirection(wall);
            if (wallDir == null) return result;

            IList<ElementId> insertIds;
            try
            {
                insertIds = wall.FindInserts(true, true, true, true);
            }
            catch
            {
                return result;
            }

            if (insertIds == null || insertIds.Count == 0) return result;

            foreach (ElementId id in insertIds)
            {
                Element insert = wall.Document.GetElement(id);
                BoundingBoxXYZ box = insert?.get_BoundingBox(null);
                if (box == null) continue;

                double area = EstimateOpeningProjectedAreaFt2(box, wallDir);
                if (area <= 1e-9) continue;

                result.Add(new WallOpeningMeasure(box, area > thresholdFt2 + 1e-9));
            }

            return result;
        }

        private static double EstimateOpeningProjectedAreaFt2(BoundingBoxXYZ box, XYZ wallDir)
        {
            if (box == null || wallDir == null) return 0.0;

            double minAlong = double.MaxValue;
            double maxAlong = double.MinValue;
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;

            foreach (XYZ p in GetBoundingBoxCorners(box))
            {
                double along = p.DotProduct(wallDir);
                if (along < minAlong) minAlong = along;
                if (along > maxAlong) maxAlong = along;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }

            double width = maxAlong - minAlong;
            double height = maxZ - minZ;
            if (width <= 1e-9 || height <= 1e-9) return 0.0;
            return width * height;
        }

        private static IEnumerable<XYZ> GetBoundingBoxCorners(BoundingBoxXYZ box)
        {
            if (box == null) yield break;

            Transform transform = box.Transform ?? Transform.Identity;
            for (int ix = 0; ix <= 1; ix++)
            {
                for (int iy = 0; iy <= 1; iy++)
                {
                    for (int iz = 0; iz <= 1; iz++)
                    {
                        XYZ local = new XYZ(
                            ix == 0 ? box.Min.X : box.Max.X,
                            iy == 0 ? box.Min.Y : box.Max.Y,
                            iz == 0 ? box.Min.Z : box.Max.Z);
                        yield return transform.OfPoint(local);
                    }
                }
            }
        }

        private static bool IsCandidateWallOpeningSideSlab(FaceSlab slab, XYZ wallDir)
        {
            if (slab == null) return false;
            if (!string.Equals(slab.FaceType, "Side", StringComparison.OrdinalIgnoreCase)) return false;
            if (slab.FaceNormal == null || slab.FacePoint == null || wallDir == null) return false;

            XYZ nxy = new XYZ(slab.FaceNormal.X, slab.FaceNormal.Y, 0.0);
            if (nxy.GetLength() <= 1e-9) return false;
            nxy = nxy.Normalize();

            return Math.Abs(nxy.DotProduct(wallDir)) >= 0.70;
        }

        private static WallOpeningMeasure FindContainingOpening(IList<WallOpeningMeasure> openings, XYZ point)
        {
            if (openings == null || point == null) return null;

            foreach (WallOpeningMeasure opening in openings)
            {
                if (opening != null && IsPointInsideBox(opening.Box, point, 0.05))
                {
                    return opening;
                }
            }

            return null;
        }

        private static bool IsPointInsideBox(BoundingBoxXYZ box, XYZ point, double tol)
        {
            if (box == null || point == null) return false;

            Transform inverse = null;
            try
            {
                inverse = (box.Transform ?? Transform.Identity).Inverse;
            }
            catch
            {
                inverse = Transform.Identity;
            }

            XYZ p = inverse.OfPoint(point);
            return p.X >= box.Min.X - tol && p.X <= box.Max.X + tol &&
                   p.Y >= box.Min.Y - tol && p.Y <= box.Max.Y + tol &&
                   p.Z >= box.Min.Z - tol && p.Z <= box.Max.Z + tol;
        }

        private static XYZ GetWallDirection(Wall wall)
        {
            if (wall == null || !(wall.Location is LocationCurve lc) || lc.Curve == null) return null;
            return GetCurveHorizontalDirection(lc.Curve);
        }

        private static double RemoveWallJoinedEndCapSlabs(Wall wall, List<FaceSlab> slabs, ref double sideArea)
        {
            double removedArea = 0.0;
            if (wall == null || slabs == null || slabs.Count == 0) return removedArea;
            if (!(wall.Location is LocationCurve lc) || lc.Curve == null) return removedArea;

            Curve curve = lc.Curve;
            XYZ end0;
            XYZ end1;
            try
            {
                end0 = curve.GetEndPoint(0);
                end1 = curve.GetEndPoint(1);
            }
            catch
            {
                return removedArea;
            }

            XYZ wallDir = GetCurveHorizontalDirection(curve);
            if (wallDir == null || wallDir.GetLength() <= 1e-9) return removedArea;

            bool join0 = IsWallEndJoinedToWall(wall, lc, 0, end0);
            bool join1 = IsWallEndJoinedToWall(wall, lc, 1, end1);
            if (!join0 && !join1) return removedArea;

            double endTol = Math.Max(wall.Width * 0.75, 0.08); // ~24 mm minimum in feet.
            const double dirTol = 0.92;

            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                FaceSlab slab = slabs[i];
                if (slab == null) continue;
                if (!string.Equals(slab.FaceType, "Side", StringComparison.OrdinalIgnoreCase)) continue;
                if (slab.FaceNormal == null || slab.FacePoint == null) continue;

                XYZ nxy = new XYZ(slab.FaceNormal.X, slab.FaceNormal.Y, 0.0);
                if (nxy.GetLength() <= 1e-9) continue;
                nxy = nxy.Normalize();

                // End-cap side faces are parallel to wall direction.
                if (Math.Abs(nxy.DotProduct(wallDir)) < dirTol) continue;

                bool atEnd0 = DistanceXY(slab.FacePoint, end0) <= endTol;
                bool atEnd1 = DistanceXY(slab.FacePoint, end1) <= endTol;

                if ((join0 && atEnd0) || (join1 && atEnd1))
                {
                    sideArea -= slab.FaceArea;
                    removedArea += slab.FaceArea;
                    slabs.RemoveAt(i);
                }
            }

            if (sideArea < 0) sideArea = 0;
            return removedArea;
        }

        private static XYZ GetCurveHorizontalDirection(Curve curve)
        {
            if (curve == null) return null;

            XYZ dir = null;
            if (curve is Line line)
            {
                dir = line.Direction;
            }
            else
            {
                try
                {
                    double p0 = curve.GetEndParameter(0);
                    double p1 = curve.GetEndParameter(1);
                    double pm = (p0 + p1) * 0.5;
                    Transform d = curve.ComputeDerivatives(pm, false);
                    dir = d?.BasisX;
                }
                catch
                {
                    dir = null;
                }
            }

            if (dir == null || dir.GetLength() <= 1e-9) return null;
            XYZ horizontal = new XYZ(dir.X, dir.Y, 0.0);
            if (horizontal.GetLength() <= 1e-9) return null;
            return horizontal.Normalize();
        }

        private static bool IsWallEndJoinedToWall(Wall wall, LocationCurve lc, int endIndex, XYZ endPoint)
        {
            if (wall == null || lc == null) return false;

            // Preferred: use Revit join data when available.
            try
            {
                MethodInfo method = lc.GetType().GetMethod("get_ElementsAtJoin", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    object value = method.Invoke(lc, new object[] { endIndex });
                    if (value is ElementArray arr)
                    {
                        foreach (Element e in arr)
                        {
                            if (e is Wall other && other.Id.Value != wall.Id.Value)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback below.
            }

            return HasNearbyWallAtPoint(wall, endPoint);
        }

        private static bool HasNearbyWallAtPoint(Wall wall, XYZ point)
        {
            if (wall == null || point == null || wall.Document == null) return false;

            const double tol = 0.05; // ~15 mm
            BoundingBoxXYZ bb = wall.get_BoundingBox(null);
            double zMin = bb?.Min.Z ?? (point.Z - tol);
            double zMax = bb?.Max.Z ?? (point.Z + tol);

            Outline outline = new Outline(
                new XYZ(point.X - tol, point.Y - tol, zMin - tol),
                new XYZ(point.X + tol, point.Y + tol, zMax + tol));

            var candidates = new FilteredElementCollector(wall.Document)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .Cast<Wall>();

            foreach (Wall other in candidates)
            {
                if (other == null || other.Id.Value == wall.Id.Value) continue;
                if (!HasVerticalOverlap(wall, other, tol)) continue;
                if (!(other.Location is LocationCurve olc) || olc.Curve == null) continue;

                try
                {
                    IntersectionResult ir = olc.Curve.Project(point);
                    if (ir != null && ir.Distance <= tol)
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore projection failures for this candidate
                }
            }

            return false;
        }

        private static bool HasVerticalOverlap(Element a, Element b, double tol)
        {
            BoundingBoxXYZ bbA = a?.get_BoundingBox(null);
            BoundingBoxXYZ bbB = b?.get_BoundingBox(null);
            if (bbA == null || bbB == null) return true;

            double overlap = Math.Min(bbA.Max.Z, bbB.Max.Z) - Math.Max(bbA.Min.Z, bbB.Min.Z);
            return overlap >= -tol;
        }

        private static double DistanceXY(XYZ a, XYZ b)
        {
            if (a == null || b == null) return double.MaxValue;
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static List<FaceSlab> BuildFormworkFaceSlabs(
            Element element,
            bool includeSides,
            bool includeBottom,
            bool includeTop,
            out double sideArea,
            out double bottomArea,
            out double topArea,
            double slopedTopMinAngleDegrees = -1.0)
        {
            sideArea = 0.0;
            bottomArea = 0.0;
            topArea = 0.0;
            var slabs = new List<FaceSlab>();
            if (element == null) return slabs;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            List<Solid> solids = GetElementSolids(element, opt);
            double halfThickness = FormworkHalfThicknessFt;
            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    if (face == null) continue;
                    double area = face.Area;
                    if (area <= 1e-9) continue;

                    XYZ normal = GetFaceNormal(face);
                    if (normal == null) continue;

                    bool isSide = includeSides && Math.Abs(normal.Z) < 0.1;
                    bool isBottom = includeBottom && normal.Z < -0.9;
                    bool isSlopedTop = IsSlopedTopFormworkFace(normal, slopedTopMinAngleDegrees);
                    bool isTop = (includeTop && normal.Z > 0.9) || isSlopedTop;
                    if (!isSide && !isBottom && !isTop) continue;

                    if (isSide) sideArea += area;
                    if (isBottom) bottomArea += area;
                    if (isTop) topArea += area;
                    string faceType = isBottom ? "Bottom" : (isTop ? (isSlopedTop ? "TopSlope" : "Top") : "Side");

                    Solid slab = null;
                    XYZ slabNormal = normal;
                    XYZ slabPoint = null;
                    if (face is PlanarFace pf)
                    {
                        slab = CreateFaceSlab(pf, halfThickness);
                        slabNormal = pf.FaceNormal;
                        slabPoint = pf.Origin;
                    }

                    if (slab != null && slab.Volume > 1e-9)
                    {
                        double faceHeight = isSide ? EstimateFaceVerticalHeight(face) : 0.0;
                        slabs.Add(new FaceSlab(slab, halfThickness * 2.0, faceType, area, slabNormal, slabPoint, faceHeight));
                    }
                    else
                    {
                        // Fallback for non-planar faces or failed planar extrusion:
                        // triangulate and create thin solids per triangle.
                        slabs.AddRange(CreateTriangulatedFaceSlabs(face, halfThickness, faceType));
                    }
                }
            }

            // Some floors/walls can expose only mesh geometry in specific states
            // (shape-edited, imported/converted, or complex joins). In that case,
            // fallback to mesh-triangle slabs so formwork shapes still get generated.
            if (slabs.Count == 0)
            {
                GeometryElement ge = element.get_Geometry(opt);
                if (ge != null)
                {
                    foreach (GeometryObject go in ge)
                    {
                        AddFaceSlabsFromMeshGeometry(
                            go,
                            Transform.Identity,
                            includeSides,
                            includeBottom,
                            includeTop,
                            slopedTopMinAngleDegrees,
                            halfThickness,
                            slabs,
                            ref sideArea,
                            ref bottomArea,
                            ref topArea);
                    }
                }
            }

            return slabs;
        }

        private static List<FaceSlab> BuildStairPaintingFaceSlabs(
            Element element,
            bool includePainting,
            out double paintingSideArea,
            out double paintingBottomArea)
        {
            paintingSideArea = 0.0;
            paintingBottomArea = 0.0;
            var paintingSlabs = new List<FaceSlab>();
            if (!includePainting || element == null) return paintingSlabs;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double halfThickness = FormworkHalfThicknessFt;
            foreach (Solid solid in GetElementSolids(element, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    if (face == null || face.Area <= 1e-9) continue;

                    XYZ normal = GetFaceNormal(face);
                    if (normal == null) continue;

                    // Stair painting follows the visible painted surfaces:
                    // include side/riser faces and downward/sloped soffit faces,
                    // exclude only upward tread/landing walking surfaces.
                    if (normal.Z > 0.1) continue;

                    bool isBottom = normal.Z < -0.1;
                    string faceType = isBottom ? "PaintingBottom" : "PaintingSide";
                    if (isBottom) paintingBottomArea += face.Area;
                    else paintingSideArea += face.Area;

                    Solid slab = null;
                    XYZ slabNormal = normal;
                    XYZ slabPoint = null;
                    if (face is PlanarFace pf)
                    {
                        slab = CreateFaceSlab(pf, halfThickness);
                        slabNormal = pf.FaceNormal;
                        slabPoint = pf.Origin;
                    }

                    if (slab != null && slab.Volume > 1e-9)
                    {
                        double faceHeight = isBottom ? 0.0 : EstimateFaceVerticalHeight(face);
                        paintingSlabs.Add(new FaceSlab(slab, halfThickness * 2.0, faceType, face.Area, slabNormal, slabPoint, faceHeight));
                    }
                    else
                    {
                        paintingSlabs.AddRange(CreateTriangulatedFaceSlabs(face, halfThickness, faceType));
                    }
                }
            }

            return paintingSlabs;
        }

        private static bool IsSlopedTopFormworkFace(XYZ normal, double minSlopeAngleDegrees)
        {
            if (normal == null || minSlopeAngleDegrees < 0.0) return false;
            if (normal.Z <= 0.1 || normal.Z >= 0.999999) return false;

            double slopeAngle = GetSlopeAngleFromHorizontalDegrees(normal);
            return slopeAngle + 1e-9 >= minSlopeAngleDegrees;
        }

        private static double GetSlopeAngleFromHorizontalDegrees(XYZ normal)
        {
            if (normal == null) return 0.0;

            double z = normal.Z;
            if (z < -1.0) z = -1.0;
            if (z > 1.0) z = 1.0;
            return Math.Acos(Math.Abs(z)) * 180.0 / Math.PI;
        }

        private static double EstimateFaceVerticalHeight(Face face)
        {
            if (face == null) return 0.0;

            double minZ = double.MaxValue;
            double maxZ = double.MinValue;
            bool found = false;

            try
            {
                IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
                if (loops != null)
                {
                    foreach (CurveLoop loop in loops)
                    {
                        if (loop == null) continue;
                        foreach (Curve curve in loop)
                        {
                            if (curve == null) continue;

                            IList<XYZ> points = null;
                            try
                            {
                                points = curve.Tessellate();
                            }
                            catch
                            {
                                points = null;
                            }

                            if (points == null || points.Count == 0)
                            {
                                TrackVerticalRange(curve.GetEndPoint(0), ref minZ, ref maxZ, ref found);
                                TrackVerticalRange(curve.GetEndPoint(1), ref minZ, ref maxZ, ref found);
                                continue;
                            }

                            foreach (XYZ point in points)
                            {
                                TrackVerticalRange(point, ref minZ, ref maxZ, ref found);
                            }
                        }
                    }
                }
            }
            catch
            {
                found = false;
                minZ = double.MaxValue;
                maxZ = double.MinValue;
            }

            if (!found)
            {
                try
                {
                    Mesh mesh = face.Triangulate();
                    if (mesh != null)
                    {
                        for (int i = 0; i < mesh.NumTriangles; i++)
                        {
                            MeshTriangle tri = mesh.get_Triangle(i);
                            TrackVerticalRange(tri.get_Vertex(0), ref minZ, ref maxZ, ref found);
                            TrackVerticalRange(tri.get_Vertex(1), ref minZ, ref maxZ, ref found);
                            TrackVerticalRange(tri.get_Vertex(2), ref minZ, ref maxZ, ref found);
                        }
                    }
                }
                catch
                {
                    return 0.0;
                }
            }

            return found && maxZ >= minZ ? Math.Max(0.0, maxZ - minZ) : 0.0;
        }

        private static double EstimateTriangleVerticalHeight(XYZ p0, XYZ p1, XYZ p2)
        {
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;
            bool found = false;

            TrackVerticalRange(p0, ref minZ, ref maxZ, ref found);
            TrackVerticalRange(p1, ref minZ, ref maxZ, ref found);
            TrackVerticalRange(p2, ref minZ, ref maxZ, ref found);

            return found && maxZ >= minZ ? Math.Max(0.0, maxZ - minZ) : 0.0;
        }

        private static void TrackVerticalRange(XYZ point, ref double minZ, ref double maxZ, ref bool found)
        {
            if (point == null) return;
            if (point.Z < minZ) minZ = point.Z;
            if (point.Z > maxZ) maxZ = point.Z;
            found = true;
        }

        private static bool IsSideFaceType(string faceType)
        {
            return string.Equals(faceType, "Side", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(faceType, "PaintingSide", StringComparison.OrdinalIgnoreCase);
        }

        private static bool WriteFoundationQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> generics,
            bool createShape)
        {
            if (doc == null || element == null) return false;

            double sidesArea = 0.0;
            double topArea = 0.0;
            ComputeFoundationFormworkAreas(element, rules.FoundationIncludeSide, rules.FoundationIncludeTop, out sidesArea, out topArea);

            double subFoun = 0.0;
            double subBeam = 0.0;
            double subCol = 0.0;
            double subWall = 0.0;
            double subFloor = 0.0;
            double subGeneric = 0.0;
            ComputeFoundationIntersectionAreas(
                doc,
                element,
                foundations,
                beams,
                columns,
                walls,
                floors,
                generics,
                rules,
                out subFoun,
                out subBeam,
                out subCol,
                out subWall,
                out subFloor,
                out subGeneric);

            double grossTotal = sidesArea + (rules.FoundationIncludeTop ? topArea : 0.0);
            double finalTotal = grossTotal - (subFoun + subBeam + subCol + subWall + subFloor + subGeneric);
            if (finalTotal < 0) finalTotal = 0;

            double foundationSideLengthTotal = 0.0;
            double foundationSideAreaStaged = 0.0;
            double[] foundationSideLengthStages = new double[FoundationSidePersistedStageLimit + 1];
            double[] foundationSideAreaStages = new double[FoundationSidePersistedStageLimit + 1];
            ComputeFoundationSideStageQuantities(
                element,
                rules,
                out foundationSideLengthTotal,
                out foundationSideAreaStaged,
                foundationSideLengthStages,
                foundationSideAreaStages);

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Foun.Total", grossTotal);
            changed |= SetParamDouble(element, "FWK.Foun.Sides", sidesArea);
            changed |= SetParamDouble(element, "FWK.Foun.Top", rules.FoundationIncludeTop ? topArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Foun.SubFoun", subFoun);
            changed |= SetParamDouble(element, "FWK.Foun.SubBeam", subBeam);
            changed |= SetParamDouble(element, "FWK.Foun.SubCol", subCol);
            changed |= SetParamDouble(element, "FWK.Foun.SubWall", subWall);
            changed |= SetParamDouble(element, "FWK.Foun.SubFloor", subFloor);
            changed |= SetParamDouble(element, "FWK.Foun.SubGeneric", subGeneric);
            changed |= SetParamDouble(element, "FWK.Foun.SideLength.Total", foundationSideLengthTotal);
            changed |= SetParamDouble(element, "FWK.Foun.SideArea.Staged", foundationSideAreaStaged);
            for (int stage = 1; stage <= FoundationSidePersistedStageLimit; stage++)
            {
                string stageText = stage.ToString(CultureInfo.InvariantCulture);
                changed |= SetParamDouble(element, "FWK.Foun.SideLength.Stage." + stageText, foundationSideLengthStages[stage]);
                changed |= SetParamDouble(element, "FWK.Foun.SideArea.Stage." + stageText, foundationSideAreaStages[stage]);
            }

            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalTotal);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("FOUN.SIDE", rules.FoundationIncludeSide),
                ("FOUN.TOP", rules.FoundationIncludeTop),
                ("FOUN.DEDUCT.FOUN", rules.FoundationDeductFoundation),
                ("FOUN.DEDUCT.BEAM", rules.FoundationDeductBeam),
                ("FOUN.DEDUCT.COL", rules.FoundationDeductColumn),
                ("FOUN.DEDUCT.WALL", rules.FoundationDeductWall),
                ("FOUN.DEDUCT.FLOOR", rules.FoundationDeductFloor),
                ("FOUN.DEDUCT.OTHER", rules.FoundationDeductGeneric),
                ("FOUN.SIDE.SETTINGS", rules.FoundationSideSettingsEnabled)));
            changed |= SetParamString(element, "CBIM_QsFormula", BuildFoundationFormworkFormula(rules));

            var foundationBreakdownEntries = new List<(string Name, double InternalArea)>();
            if (rules.FoundationIncludeSide)
            {
                foundationBreakdownEntries.Add(("Sides", sidesArea));
            }

            if (rules.FoundationIncludeTop)
            {
                foundationBreakdownEntries.Add(("Top", topArea));
            }

            foundationBreakdownEntries.Add(("SubFoun", -subFoun));
            foundationBreakdownEntries.Add(("SubBeam", -subBeam));
            foundationBreakdownEntries.Add(("SubCol", -subCol));
            foundationBreakdownEntries.Add(("SubWall", -subWall));
            foundationBreakdownEntries.Add(("SubFloor", -subFloor));
            foundationBreakdownEntries.Add(("SubGeneric", -subGeneric));
            foundationBreakdownEntries.Add(("Net", finalTotal));

            string foundationBreakdown = BuildAreaAuditBreakdown("Foundation", foundationBreakdownEntries.ToArray());
            foundationBreakdown += BuildFoundationSideStageAudit(foundationSideLengthTotal, foundationSideAreaStaged, foundationSideLengthStages, foundationSideAreaStages);
            changed |= SetParamString(element, "CBIM_QsBreakdown", foundationBreakdown);

            if (createShape)
            {
                List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                    element,
                    includeSides: rules.FoundationIncludeSide,
                    includeBottom: false,
                    includeTop: rules.FoundationIncludeTop,
                    out _,
                    out _,
                    out _);
                CreateFormworkShapes(doc, element, "Foundation", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteKerbQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> walls,
            IList<Element> columns,
            bool createShape)
        {
            if (doc == null || element == null || rules == null) return false;

            double sideArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                includeSides: rules.KerbIncludeSide,
                includeBottom: false,
                includeTop: rules.KerbIncludeTop,
                out sideArea,
                out bottomArea,
                out topArea);

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            double subWall = rules.KerbDeductWall ? SumIntersectionArea(formworkSlabs, hostBox, walls, opt, element.Id) : 0.0;
            double subCol = rules.KerbDeductWall ? SumIntersectionArea(formworkSlabs, hostBox, columns, opt, element.Id) : 0.0;
            double totalArea = (rules.KerbIncludeSide ? sideArea : 0.0) + (rules.KerbIncludeTop ? topArea : 0.0);
            double finalArea = Math.Max(0.0, totalArea - subWall - subCol);

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Kerb.Total", totalArea);
            changed |= SetParamDouble(element, "FWK.Kerb.Sides", rules.KerbIncludeSide ? sideArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Kerb.Top", rules.KerbIncludeTop ? topArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Kerb.SubWall", subWall);
            changed |= SetParamDouble(element, "FWK.Kerb.SubCol", subCol);
            changed |= SetParamDouble(element, "FWK.Kerb.Length", GetKerbLength(element));
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildKerbRuleCode(rules));
            changed |= SetParamString(element, "CBIM_QsFormula", "Net = Sides + Top - SubWall - SubCol");
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Kerb",
                ("Sides", rules.KerbIncludeSide ? sideArea : 0.0),
                ("Top", rules.KerbIncludeTop ? topArea : 0.0),
                ("SubWall", -subWall),
                ("SubCol", -subCol),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Kerb", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteOtherConcreteQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            bool createShape)
        {
            if (doc == null || element == null || rules == null) return false;

            double sideArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                includeSides: rules.OtherConcreteIncludeSide,
                includeBottom: rules.OtherConcreteIncludeBottom,
                includeTop: false,
                out sideArea,
                out bottomArea,
                out topArea);

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            double subStructure = 0.0;
            if (rules.OtherConcreteDeductStructure)
            {
                subStructure += SumIntersectionArea(formworkSlabs, hostBox, foundations, opt, element.Id);
                subStructure += SumIntersectionArea(formworkSlabs, hostBox, beams, opt, element.Id);
                subStructure += SumIntersectionArea(formworkSlabs, hostBox, columns, opt, element.Id);
                subStructure += SumIntersectionArea(formworkSlabs, hostBox, walls, opt, element.Id);
                subStructure += SumIntersectionArea(formworkSlabs, hostBox, floors, opt, element.Id);
            }

            double totalArea = (rules.OtherConcreteIncludeSide ? sideArea : 0.0) +
                               (rules.OtherConcreteIncludeBottom ? bottomArea : 0.0);
            double finalArea = Math.Max(0.0, totalArea - subStructure);

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Other.Total", totalArea);
            changed |= SetParamDouble(element, "FWK.Other.Sides", rules.OtherConcreteIncludeSide ? sideArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Other.Bottom", rules.OtherConcreteIncludeBottom ? bottomArea : 0.0);
            changed |= SetParamDouble(element, "FWK.Other.SubStructure", subStructure);
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildOtherConcreteRuleCode(rules));
            changed |= SetParamString(element, "CBIM_QsFormula", "Net = Sides + Bottom - SubStructure");
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Other Concrete",
                ("Sides", rules.OtherConcreteIncludeSide ? sideArea : 0.0),
                ("Bottom", rules.OtherConcreteIncludeBottom ? bottomArea : 0.0),
                ("SubStructure", -subStructure),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "OtherConcrete", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteLintelQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> walls,
            bool createShape)
        {
            if (doc == null || element == null || rules == null) return false;

            double sideArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                includeSides: rules.LintelIncludeSide,
                includeBottom: rules.LintelIncludeBottom,
                includeTop: false,
                out sideArea,
                out bottomArea,
                out topArea);

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            double subWall = rules.LintelDeductWall ? SumIntersectionArea(formworkSlabs, hostBox, walls, opt, element.Id) : 0.0;
            double grossArea = (rules.LintelIncludeSide ? sideArea : 0.0) + (rules.LintelIncludeBottom ? bottomArea : 0.0);
            double finalArea = Math.Max(0.0, grossArea - subWall);
            double finalSide = Math.Max(0.0, (rules.LintelIncludeSide ? sideArea : 0.0) - subWall);
            double finalBottom = rules.LintelIncludeBottom ? bottomArea : 0.0;

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Lintel.Sides", finalSide);
            changed |= SetParamDouble(element, "FWK.Lintel.Bottom", finalBottom);
            changed |= SetParamDouble(element, "FWK.Lintel.Length", GetElementLength(element));
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("LINTEL.SIDE", rules.LintelIncludeSide),
                ("LINTEL.BOTTOM", rules.LintelIncludeBottom),
                ("LINTEL.DEDUCT.WALL", rules.LintelDeductWall)));
            changed |= SetParamString(element, "CBIM_QsFormula", "Net = Sides + Bottom - SubWall");
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Lintel",
                ("Sides", rules.LintelIncludeSide ? sideArea : 0.0),
                ("Bottom", finalBottom),
                ("SubWall", -subWall),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Lintel", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteDropPanelQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            IList<Element> floors,
            bool createShape)
        {
            if (doc == null || element == null || rules == null) return false;

            double sideArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                includeSides: rules.DropPanelSoffitEnabled,
                includeBottom: rules.DropPanelSoffitEnabled,
                includeTop: false,
                out sideArea,
                out bottomArea,
                out topArea);

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            BoundingBoxXYZ hostBox = element.get_BoundingBox(null);
            double subSlab = rules.DropPanelDeductSlab ? SumIntersectionArea(formworkSlabs, hostBox, floors, opt, element.Id) : 0.0;
            double soffitArea = rules.DropPanelSoffitEnabled ? Math.Max(0.0, sideArea + bottomArea - subSlab) : 0.0;
            double stageOneArea = 0.0;
            if (rules.DropPanelStrutStaged)
            {
                double strutHeight = GetElementVerticalHeight(element);
                if (strutHeight + 1e-9 >= rules.DropPanelStrutJudgeHeightFt)
                {
                    stageOneArea = soffitArea;
                }
            }

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Drop.Soffit", soffitArea);
            changed |= SetParamDouble(element, "FWK.Drop.Stage.1", stageOneArea);
            changed |= SetParamDouble(element, "CBIM_FormworkArea", soffitArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("DROP.SOFFIT", rules.DropPanelSoffitEnabled),
                ("DROP.STRUT.METHOD", rules.DropPanelStrutEnabled),
                ("DROP.STRUT.STAGED", rules.DropPanelStrutStaged),
                ("DROP.DEDUCT.SLAB", rules.DropPanelDeductSlab)));
            changed |= SetParamString(element, "CBIM_QsFormula", "Net = Sides + Bottom - SubSlab");
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Drop Panel",
                ("Sides", rules.DropPanelSoffitEnabled ? sideArea : 0.0),
                ("Bottom", rules.DropPanelSoffitEnabled ? bottomArea : 0.0),
                ("SubSlab", -subSlab),
                ("Stage.1", stageOneArea),
                ("Net", soffitArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "DropPanel", formworkSlabs);
            }

            return changed;
        }

        private static bool WriteEaveQsParameters(
            Document doc,
            Element element,
            QsMeasurementRuntimeRules rules,
            bool createShape)
        {
            if (doc == null || element == null || rules == null) return false;

            double edgeArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            List<FaceSlab> formworkSlabs = BuildFormworkFaceSlabs(
                element,
                includeSides: rules.EaveIncludeEdge,
                includeBottom: rules.EaveIncludeBottom,
                includeTop: false,
                out edgeArea,
                out bottomArea,
                out topArea);

            double finalBottom = rules.EaveIncludeBottom ? bottomArea : 0.0;
            double finalEdge = rules.EaveIncludeEdge ? edgeArea : 0.0;
            double finalArea = finalBottom + finalEdge;

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamDouble(element, "FWK.Eave.Bottom", finalBottom);
            changed |= SetParamDouble(element, "FWK.Eave.Edge", finalEdge);
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildRuleCode(
                ("EAVE.BOTTOM", rules.EaveIncludeBottom),
                ("EAVE.EDGE", rules.EaveIncludeEdge)));
            changed |= SetParamString(element, "CBIM_QsFormula", "Net = Bottom + Edge");
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildAreaAuditBreakdown(
                "Eave",
                ("Bottom", finalBottom),
                ("Edge", finalEdge),
                ("Net", finalArea)));

            if (createShape)
            {
                CreateFormworkShapes(doc, element, "Eave", formworkSlabs);
            }

            return changed;
        }

        private static Dictionary<long, double> BuildRebarWeightByHost(Document doc)
        {
            var result = new Dictionary<long, double>();
            if (doc == null) return result;

            IList<Element> rebars;
            try
            {
                rebars = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rebar)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }
            catch
            {
                return result;
            }

            foreach (Element rebar in rebars)
            {
                ElementId hostId = TryGetRebarHostId(rebar);
                if (hostId == null || hostId == ElementId.InvalidElementId) continue;

                double weightKg = EstimateRebarWeightKg(rebar);
                if (weightKg <= 1e-9) continue;

                long key = hostId.Value;
                double current;
                result.TryGetValue(key, out current);
                result[key] = current + weightKg;
            }

            return result;
        }

        private static void WriteRebarWeightParameters(Dictionary<long, double> weightByHost, params IList<Element>[] groups)
        {
            if (groups == null) return;

            foreach (IList<Element> group in groups)
            {
                if (group == null) continue;

                foreach (Element element in group)
                {
                    if (element?.Id == null) continue;

                    double value = 0.0;
                    weightByHost?.TryGetValue(element.Id.Value, out value);
                    SetParamDouble(element, "Rebar.Weight", value);
                }
            }
        }

        private static ElementId TryGetRebarHostId(Element rebar)
        {
            if (rebar == null) return ElementId.InvalidElementId;

            try
            {
                MethodInfo method = rebar.GetType().GetMethod("GetHostId", Type.EmptyTypes);
                if (method != null)
                {
                    object value = method.Invoke(rebar, null);
                    if (value is ElementId hostId)
                    {
                        return hostId;
                    }
                }
            }
            catch
            {
            }

            foreach (string name in new[] { "Host Id", "HostID", "Host Element Id", "Host ElementID" })
            {
                Parameter parameter = rebar.LookupParameter(name);
                if (parameter == null) continue;

                if (parameter.StorageType == StorageType.ElementId)
                {
                    ElementId id = parameter.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId) return id;
                }

                if (parameter.StorageType == StorageType.Integer)
                {
                    int id = parameter.AsInteger();
                    if (id > 0) return new ElementId((long)id);
                }
            }

            return ElementId.InvalidElementId;
        }

        private static double EstimateRebarWeightKg(Element rebar)
        {
            double weight = GetRebarWeightParameterKg(rebar);
            if (weight > 1e-9) return weight;

            double totalLengthM = GetRebarTotalLengthMeters(rebar);
            double diameterM = GetRebarDiameterMeters(rebar);
            if (totalLengthM <= 1e-9 || diameterM <= 1e-9) return 0.0;

            const double steelDensityKgPerM3 = 7850.0;
            double areaM2 = Math.PI * diameterM * diameterM / 4.0;
            return totalLengthM * areaM2 * steelDensityKgPerM3;
        }

        private static double GetRebarWeightParameterKg(Element rebar)
        {
            foreach (string name in new[]
                     {
                         "Total Weight",
                         "Weight Total",
                         "Rebar Weight",
                         "Bar Weight",
                         "Weight",
                         "Mass",
                         "Total Mass"
                     })
            {
                Parameter parameter = rebar?.LookupParameter(name);
                double value;
                if (TryReadWeightParameterKg(parameter, out value) && value > 1e-9)
                {
                    return value;
                }
            }

            return 0.0;
        }

        private static bool TryReadWeightParameterKg(Parameter parameter, out double value)
        {
            value = 0.0;
            if (parameter == null) return false;

            string display = "";
            try
            {
                display = parameter.AsValueString() ?? "";
            }
            catch
            {
                display = "";
            }

            if (TryParseDisplayedWeightKg(display, out value))
            {
                return true;
            }

            if (parameter.StorageType == StorageType.Double)
            {
                double raw = parameter.AsDouble();
                if (raw <= 1e-9) return false;

                try
                {
                    value = UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Kilograms);
                    if (value > 1e-9) return true;
                }
                catch
                {
                    value = raw;
                    return value > 1e-9;
                }
            }
            else if (parameter.StorageType == StorageType.Integer)
            {
                value = parameter.AsInteger();
                return value > 1e-9;
            }

            return false;
        }

        private static bool TryParseDisplayedWeightKg(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string normalized = text.Trim().ToLowerInvariant();
            double number;
            if (!TryParseFirstNumber(normalized, out number) || number <= 1e-9)
            {
                return false;
            }

            if (normalized.Contains(" ton") || normalized.Contains(" tonne") || normalized.Contains(" t "))
            {
                value = number * 1000.0;
                return true;
            }

            value = number;
            return normalized.Contains("kg") || normalized.Contains("kilogram") || normalized.Contains("mass") || normalized.Contains("weight");
        }

        private static double GetRebarTotalLengthMeters(Element rebar)
        {
            double totalLength = GetRebarLengthMeters(rebar, true);
            if (totalLength > 1e-9) return totalLength;

            double singleLength = GetRebarLengthMeters(rebar, false);
            if (singleLength <= 1e-9) return 0.0;

            double quantity = GetRebarQuantity(rebar);
            return singleLength * Math.Max(1.0, quantity);
        }

        private static double GetRebarLengthMeters(Element rebar, bool total)
        {
            string[] names = total
                ? new[] { "Total Bar Length", "Total Length", "Bar Total Length", "Overall Length" }
                : new[] { "Bar Length", "Length", "A" };

            foreach (string name in names)
            {
                Parameter parameter = rebar?.LookupParameter(name);
                double length;
                if (TryReadLengthParameterMeters(parameter, out length) && length > 1e-9)
                {
                    return length;
                }
            }

            return 0.0;
        }

        private static double GetRebarDiameterMeters(Element rebar)
        {
            foreach (string name in new[] { "Bar Diameter", "Diameter", "Model Bar Diameter", "Nominal Diameter" })
            {
                Parameter parameter = rebar?.LookupParameter(name);
                double length;
                if (TryReadLengthParameterMeters(parameter, out length) && length > 1e-9)
                {
                    return length;
                }
            }

            ElementId typeId = rebar?.GetTypeId();
            ElementType type = typeId != null && typeId != ElementId.InvalidElementId
                ? rebar.Document?.GetElement(typeId) as ElementType
                : null;
            foreach (string name in new[] { "Bar Diameter", "Diameter", "Model Bar Diameter", "Nominal Diameter" })
            {
                Parameter parameter = type?.LookupParameter(name);
                double length;
                if (TryReadLengthParameterMeters(parameter, out length) && length > 1e-9)
                {
                    return length;
                }
            }

            return 0.0;
        }

        private static bool TryReadLengthParameterMeters(Parameter parameter, out double value)
        {
            value = 0.0;
            if (parameter == null) return false;

            string display = "";
            try
            {
                display = parameter.AsValueString() ?? "";
            }
            catch
            {
                display = "";
            }

            if (TryParseDisplayedLengthMeters(display, out value))
            {
                return true;
            }

            if (parameter.StorageType == StorageType.Double)
            {
                double raw = parameter.AsDouble();
                if (raw <= 1e-9) return false;

                try
                {
                    value = UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Meters);
                }
                catch
                {
                    value = raw * 0.3048;
                }

                return value > 1e-9;
            }

            return false;
        }

        private static bool TryParseDisplayedLengthMeters(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string normalized = text.Trim().ToLowerInvariant();
            double number;
            if (!TryParseFirstNumber(normalized, out number) || number <= 1e-9)
            {
                return false;
            }

            if (normalized.Contains("mm"))
            {
                value = number / 1000.0;
                return true;
            }

            if (normalized.Contains("cm"))
            {
                value = number / 100.0;
                return true;
            }

            if (normalized.Contains("m"))
            {
                value = number;
                return true;
            }

            return false;
        }

        private static double GetRebarQuantity(Element rebar)
        {
            foreach (string name in new[] { "Quantity", "Number of Bars", "Bar Quantity", "Bars" })
            {
                Parameter parameter = rebar?.LookupParameter(name);
                if (parameter == null) continue;

                if (parameter.StorageType == StorageType.Integer)
                {
                    int count = parameter.AsInteger();
                    if (count > 0) return count;
                }

                string display = "";
                try
                {
                    display = parameter.AsValueString() ?? parameter.AsString() ?? "";
                }
                catch
                {
                    display = "";
                }

                double parsed;
                if (TryParseFirstNumber(display, out parsed) && parsed > 0.0)
                {
                    return Math.Round(parsed, MidpointRounding.AwayFromZero);
                }
            }

            return 1.0;
        }

        private static bool TryParseFirstNumber(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var builder = new StringBuilder();
            bool started = false;
            foreach (char ch in text)
            {
                if (char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-' || ch == '+')
                {
                    builder.Append(ch == ',' ? '.' : ch);
                    started = true;
                }
                else if (started)
                {
                    break;
                }
            }

            string numberText = builder.ToString();
            return numberText.Length > 0 &&
                   double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool WriteFinishQsParameters(
            Document doc,
            QsFinishMeasurementCandidate candidate,
            QsMeasurementRuntimeRules rules)
        {
            if (doc == null || candidate == null || candidate.Element == null || rules == null) return false;

            Element element = candidate.Element;
            string prefix = GetFinishParameterPrefix(candidate.Kind);
            if (string.IsNullOrWhiteSpace(prefix)) return false;

            double measuredArea = EstimateFinishArea(element, candidate.Kind);
            double grossArea = EstimateFinishGrossArea(element, candidate.Kind, measuredArea);
            double openingDeduction = Math.Min(grossArea, ComputeFinishOpeningDeduction(doc, element, candidate.Kind, rules));
            double returnArea = ComputeFinishReturnArea(doc, element, candidate.Kind, rules);
            double upturnArea = candidate.Kind == QsFinishMeasurementKind.Waterproof
                ? ComputeWaterproofUpturnArea(element, rules)
                : 0.0;
            double finalArea = Math.Max(0.0, grossArea - openingDeduction + returnArea + upturnArea);
            bool roomGroupingEnabled = rules.IsFinishRoomGroupingEnabled(candidate.Kind);
            FinishRoomIdentity roomIdentity = roomGroupingEnabled
                ? ResolveFinishRoomIdentity(doc, element, candidate.Kind)
                : FinishRoomIdentity.Empty;
            bool openingRuleEnabled;
            double openingThresholdFt2;
            TryGetFinishOpeningRule(candidate.Kind, rules, out openingRuleEnabled, out openingThresholdFt2);
            bool returnRuleEnabled;
            double returnThresholdFt2;
            TryGetFinishReturnRule(candidate.Kind, rules, out returnRuleEnabled, out returnThresholdFt2);
            bool upturnRuleEnabled = candidate.Kind == QsFinishMeasurementKind.Waterproof && rules.WaterproofUpturnEnabled;

            bool changed = false;
            changed |= SetParamYesNo(element, "FWK.Enable", true);
            changed |= SetParamYesNo(element, "FWK.EnableSelf", true);
            changed |= SetParamString(element, "FIN.Category", candidate.BoqLabel);
            changed |= SetParamString(element, "FIN.Room", roomIdentity.Label);
            changed |= SetParamString(element, "FIN.RoomNumber", roomIdentity.Number);
            changed |= SetParamString(element, "FIN.RoomName", roomIdentity.Name);
            changed |= SetParamDouble(element, "CBIM_QsFinishGrossArea", grossArea);
            changed |= SetParamDouble(element, "CBIM_QsFinishArea", finalArea);
            changed |= SetParamDouble(element, "CBIM_FormworkArea", finalArea);
            changed |= SetParamDouble(element, $"FIN.{prefix}.Gross", grossArea);
            changed |= SetParamDouble(element, $"FIN.{prefix}.Area", finalArea);
            changed |= SetParamDouble(element, $"FIN.{prefix}.OpeningDeduct", openingDeduction);
            changed |= SetParamDouble(element, $"FIN.{prefix}.Return", returnArea);
            changed |= SetParamString(element, "CBIM_QsRuleCode", BuildFinishRuleCode(
                candidate.Kind,
                openingRuleEnabled,
                returnRuleEnabled,
                upturnRuleEnabled,
                roomGroupingEnabled));
            changed |= SetParamString(element, "CBIM_QsFormula", BuildFinishFormula(candidate.Kind));
            changed |= SetParamString(element, "CBIM_QsBreakdown", BuildFinishAuditBreakdown(
                candidate.BoqLabel,
                grossArea,
                openingDeduction,
                returnArea,
                upturnArea,
                finalArea,
                roomIdentity));

            if (candidate.Kind == QsFinishMeasurementKind.Waterproof)
            {
                changed |= SetParamDouble(element, "FIN.WP.Upturn", upturnArea);
            }

            return changed;
        }

        private static string BuildRuleCode(params (string Code, bool Enabled)[] rules)
        {
            var parts = new List<string>();
            if (rules == null) return "";

            foreach ((string code, bool enabled) in rules)
            {
                if (!enabled || string.IsNullOrWhiteSpace(code)) continue;
                parts.Add(code.Trim());
            }

            return string.Join(";", parts);
        }

        private static string BuildKerbRuleCode(QsMeasurementRuntimeRules rules)
        {
            var parts = new List<string> { "KERB.CLASSIFY" };
            if (rules?.KerbIncludeSide == true) parts.Add("KERB.SIDE");
            if (rules?.KerbIncludeTop == true) parts.Add("KERB.TOP");
            if (rules?.KerbDeductWall == true)
            {
                parts.Add("KERB.DEDUCT.WALL");
                parts.Add("KERB.DEDUCT.COL");
            }

            return string.Join(";", parts);
        }

        private static string BuildOtherConcreteRuleCode(QsMeasurementRuntimeRules rules)
        {
            var parts = new List<string> { "OTHER.CLASSIFY" };
            if (rules?.OtherConcreteIncludeSide == true) parts.Add("OTHER.SIDE");
            if (rules?.OtherConcreteIncludeBottom == true) parts.Add("OTHER.BOTTOM");
            if (rules?.OtherConcreteDeductStructure == true) parts.Add("OTHER.DEDUCT.STRUCTURE");
            return string.Join(";", parts);
        }

        private static string BuildFinishRuleCode(
            QsFinishMeasurementKind kind,
            bool openingRuleEnabled,
            bool returnRuleEnabled,
            bool upturnRuleEnabled,
            bool roomGroupingEnabled)
        {
            string prefix = GetFinishParameterPrefix(kind);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                parts.Add(prefix + ".AREA");
                if (openingRuleEnabled) parts.Add(prefix + ".OPENING");
                if (returnRuleEnabled) parts.Add(prefix + ".RETURN");
                if (roomGroupingEnabled) parts.Add(prefix + ".ROOM");
            }

            if (upturnRuleEnabled)
            {
                parts.Add("WP.UPTURN");
            }

            return string.Join(";", parts);
        }

        private static string BuildFinishFormula(QsFinishMeasurementKind kind)
        {
            return kind == QsFinishMeasurementKind.Waterproof
                ? "Net = Gross - OpeningDeduct + Return + Upturn"
                : "Net = Gross - OpeningDeduct + Return";
        }

        private static string BuildFoundationFormworkFormula(QsMeasurementRuntimeRules rules)
        {
            var additions = new List<string>();
            if (rules?.FoundationIncludeSide == true) additions.Add("Sides");
            if (rules?.FoundationIncludeTop == true) additions.Add("Top");

            string grossExpression = additions.Count == 0 ? "0" : string.Join(" + ", additions);
            return "Net = " + grossExpression + " - SubFoun - SubBeam - SubCol - SubWall - SubFloor - SubGeneric";
        }

        private static string BuildBeamFormworkFormula(QsMeasurementRuntimeRules rules)
        {
            var additions = new List<string>();
            if (rules?.BeamIncludeSide == true) additions.Add("Sides");
            if (rules?.BeamIncludeBottom == true) additions.Add("Bottom");
            if (rules?.BeamIncludeTop == true) additions.Add("Top");

            var deductions = new List<string>();
            if (rules?.BeamDeductFoundation == true) deductions.Add("SubFoun");
            if (rules?.BeamDeductBeam == true) deductions.Add("SubBeam");
            if (rules?.BeamDeductColumn == true) deductions.Add("SubCol");
            if (rules?.BeamDeductWall == true) deductions.Add("SubWall");
            if (rules?.BeamDeductFloor == true && rules?.BeamIncludeTop == true) deductions.Add("SubFloor");
            if (rules?.BeamDeductGeneric == true) deductions.Add("SubGeneric");

            string expression = additions.Count == 0 ? "0" : string.Join(" + ", additions);
            if (deductions.Count > 0)
            {
                expression += " - " + string.Join(" - ", deductions);
            }

            return "Net = " + expression;
        }

        private static string BuildColumnFormworkFormula(QsMeasurementRuntimeRules rules)
        {
            var additions = new List<string>();
            if (rules?.ColumnIncludeSide == true) additions.Add("Sides");
            if (rules?.ColumnIncludeTopBottom == true) additions.Add("TopBottom");

            var deductions = new List<string>();
            if (rules?.ColumnDeductFoundation == true) deductions.Add("SubFoun");
            if (rules?.ColumnDeductBeam == true) deductions.Add("SubBeam");
            if (rules?.ColumnDeductColumn == true) deductions.Add("SubCol");
            if (rules?.ColumnDeductWall == true) deductions.Add("SubWall");
            if (rules?.ColumnDeductFloor == true) deductions.Add("SubFloor");
            if (rules?.ColumnDeductGeneric == true) deductions.Add("SubGeneric");

            string expression = additions.Count == 0 ? "0" : string.Join(" + ", additions);
            if (deductions.Count > 0)
            {
                expression += " - " + string.Join(" - ", deductions);
            }

            return "Net = " + expression;
        }

        private static string BuildSlabFormworkFormula(QsMeasurementRuntimeRules rules)
        {
            var additions = new List<string>();
            if (rules?.SlabIncludeBottom == true) additions.Add("Bottom");
            if (rules?.SlabTopSlopeEnabled == true) additions.Add("TopSlope");

            var deductions = new List<string>();
            if (rules?.SlabDeductFoundation == true) deductions.Add("SubFoun");
            if (rules?.SlabDeductBeam == true) deductions.Add("SubBeam");
            if (rules?.SlabDeductColumn == true) deductions.Add("SubCol");
            if (rules?.SlabDeductWall == true) deductions.Add("SubWall");
            if (rules?.SlabDeductSlab == true) deductions.Add("SubFloor");
            if (rules?.SlabDeductStair == true) deductions.Add("SubStair");
            if (rules?.SlabDeductGeneric == true) deductions.Add("SubGeneric");

            string expression = additions.Count == 0 ? "0" : string.Join(" + ", additions);
            if (deductions.Count > 0)
            {
                expression += " - " + string.Join(" - ", deductions);
            }

            return "Net = " + expression;
        }

        private static string BuildStairFormworkFormula(QsMeasurementRuntimeRules rules)
        {
            var additions = new List<string>();
            if (rules?.StairPaintingEnabled == true) additions.Add("Painting");
            if (rules?.StairIncludeBottom == true) additions.Add("Bottom");
            if (rules?.StairIncludeTop == true) additions.Add("Top");

            var deductions = new List<string>();
            if (rules?.StairDeductOther == true) deductions.Add("SubFoun");
            if (rules?.StairDeductBeam == true) deductions.Add("SubBeam");
            if (rules?.StairDeductOther == true)
            {
                deductions.Add("SubCol");
                deductions.Add("SubWall");
                deductions.Add("SubFloor");
                deductions.Add("SubGeneric");
            }

            string expression = additions.Count == 0 ? "0" : string.Join(" + ", additions);
            if (deductions.Count > 0)
            {
                expression += " - " + string.Join(" - ", deductions);
            }

            return "Net = " + expression;
        }

        private static string BuildFinishAuditBreakdown(
            string label,
            double grossArea,
            double openingDeduction,
            double returnArea,
            double upturnArea,
            double finalArea,
            FinishRoomIdentity roomIdentity)
        {
            var parts = new List<string>
            {
                string.IsNullOrWhiteSpace(label) ? "Finish" : label.Trim(),
                "Gross=" + FormatAuditArea(grossArea),
                "OpeningDeduct=" + FormatAuditArea(openingDeduction),
                "Return=" + FormatAuditArea(returnArea)
            };

            if (upturnArea > 1e-9)
            {
                parts.Add("Upturn=" + FormatAuditArea(upturnArea));
            }

            parts.Add("Net=" + FormatAuditArea(finalArea));
            if (roomIdentity != null && !string.IsNullOrWhiteSpace(roomIdentity.Label))
            {
                parts.Add("Room=" + roomIdentity.Label.Trim());
            }

            return string.Join("; ", parts);
        }

        private static string BuildAreaAuditBreakdown(string label, params (string Name, double InternalArea)[] entries)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(label))
            {
                parts.Add(label.Trim());
            }

            foreach ((string name, double internalArea) in entries ?? new (string, double)[0])
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                parts.Add(name.Trim() + "=" + FormatSignedAuditArea(internalArea));
            }

            return string.Join("; ", parts);
        }

        private static string BuildFoundationSideStageAudit(
            double totalLength,
            double stagedArea,
            double[] lengthStages,
            double[] areaStages)
        {
            var parts = new List<string>();
            if (totalLength > 1e-9)
            {
                parts.Add("SideLength=" + FormatAuditLength(totalLength));
            }

            if (stagedArea > 1e-9)
            {
                parts.Add("SideAreaStaged=" + FormatAuditArea(stagedArea));
            }

            if (lengthStages != null)
            {
                int max = Math.Min(FoundationSidePersistedStageLimit, lengthStages.Length - 1);
                for (int stage = 1; stage <= max; stage++)
                {
                    if (lengthStages[stage] <= 1e-9) continue;
                    parts.Add("SideLength.Stage." + stage.ToString(CultureInfo.InvariantCulture) + "=" + FormatAuditLength(lengthStages[stage]));
                }
            }

            if (areaStages != null)
            {
                int max = Math.Min(FoundationSidePersistedStageLimit, areaStages.Length - 1);
                for (int stage = 1; stage <= max; stage++)
                {
                    if (areaStages[stage] <= 1e-9) continue;
                    parts.Add("SideArea.Stage." + stage.ToString(CultureInfo.InvariantCulture) + "=" + FormatAuditArea(areaStages[stage]));
                }
            }

            return parts.Count == 0 ? "" : "; " + string.Join("; ", parts);
        }

        private static string FormatAuditArea(double internalArea)
        {
            double areaM2 = 0.0;
            try
            {
                areaM2 = UnitUtils.ConvertFromInternalUnits(Math.Max(0.0, internalArea), UnitTypeId.SquareMeters);
            }
            catch
            {
                areaM2 = Math.Max(0.0, internalArea) * 0.09290304;
            }

            return areaM2.ToString("0.###", CultureInfo.InvariantCulture) + " m2";
        }

        private static string FormatAuditLength(double internalLength)
        {
            double lengthM = 0.0;
            try
            {
                lengthM = UnitUtils.ConvertFromInternalUnits(Math.Max(0.0, internalLength), UnitTypeId.Meters);
            }
            catch
            {
                lengthM = Math.Max(0.0, internalLength) * 0.3048;
            }

            return lengthM.ToString("0.###", CultureInfo.InvariantCulture) + " m";
        }

        private static string FormatSignedAuditArea(double internalArea)
        {
            string sign = internalArea < -1e-9 ? "-" : "";
            return sign + FormatAuditArea(Math.Abs(internalArea));
        }

        private static FinishRoomIdentity ResolveFinishRoomIdentity(
            Document doc,
            Element element,
            QsFinishMeasurementKind kind)
        {
            if (doc == null || element == null) return FinishRoomIdentity.Empty;

            var rooms = new Dictionary<long, Room>();
            foreach (XYZ point in GetFinishRoomSamplePoints(element, kind))
            {
                if (point == null) continue;

                Room room = null;
                try
                {
                    room = doc.GetRoomAtPoint(point);
                }
                catch
                {
                    room = null;
                }

                if (!IsValidFinishRoom(room) || rooms.ContainsKey(room.Id.Value)) continue;
                rooms.Add(room.Id.Value, room);
            }

            if (rooms.Count == 0) return FinishRoomIdentity.Empty;

            List<Room> found = rooms.Values
                .OrderBy(room => room.Number ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(room => room.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> labels = found.Select(GetFinishRoomLabel).Where(label => !string.IsNullOrWhiteSpace(label)).ToList();
            string number = string.Join("; ", found.Select(room => (room.Number ?? "").Trim()).Where(value => value.Length > 0));
            string name = string.Join("; ", found.Select(room => (room.Name ?? "").Trim()).Where(value => value.Length > 0));
            string label = string.Join("; ", labels);

            if (found.Count > 1)
            {
                label = "(Multiple Rooms) " + label;
            }

            return new FinishRoomIdentity(label.Trim(), number, name);
        }

        private static bool IsValidFinishRoom(Room room)
        {
            if (room == null || room.Id == null) return false;

            try
            {
                return room.Area > 1e-9;
            }
            catch
            {
                return false;
            }
        }

        private static string GetFinishRoomLabel(Room room)
        {
            if (room == null) return "";

            string number = (room.Number ?? "").Trim();
            string name = (room.Name ?? "").Trim();
            if (number.Length == 0) return name;
            if (name.Length == 0) return number;
            return number + " " + name;
        }

        private static IEnumerable<XYZ> GetFinishRoomSamplePoints(Element element, QsFinishMeasurementKind kind)
        {
            var points = new List<XYZ>();
            BoundingBoxXYZ box = element?.get_BoundingBox(null);
            if (element == null || box == null) return points;

            double centerZ = (box.Min.Z + box.Max.Z) * 0.5;
            Wall wall = element as Wall;
            if (wall != null && wall.Location is LocationCurve location && location.Curve != null)
            {
                XYZ normal = GetWallRoomSampleNormal(wall);
                if (normal == null) return points;

                double offset = Math.Max(GetFinishThicknessFt(element, kind) * 0.75, 0.10);
                foreach (double fraction in new[] { 0.20, 0.50, 0.80 })
                {
                    XYZ basePoint = GetCurvePointAtNormalizedParameter(location.Curve, fraction);
                    if (basePoint == null) continue;

                    basePoint = new XYZ(basePoint.X, basePoint.Y, centerZ);
                    points.Add(basePoint + normal.Multiply(offset));
                    points.Add(basePoint - normal.Multiply(offset));
                }

                return points;
            }

            double z = centerZ;
            if (kind == QsFinishMeasurementKind.CeilingFinish ||
                kind == QsFinishMeasurementKind.SuspendedCeiling)
            {
                z = box.Min.Z - 0.10;
            }
            else if (kind == QsFinishMeasurementKind.FloorFinish ||
                     kind == QsFinishMeasurementKind.Waterproof)
            {
                z = box.Max.Z + 0.10;
            }

            double x0 = box.Min.X + ((box.Max.X - box.Min.X) * 0.25);
            double x1 = (box.Min.X + box.Max.X) * 0.5;
            double x2 = box.Min.X + ((box.Max.X - box.Min.X) * 0.75);
            double y0 = box.Min.Y + ((box.Max.Y - box.Min.Y) * 0.25);
            double y1 = (box.Min.Y + box.Max.Y) * 0.5;
            double y2 = box.Min.Y + ((box.Max.Y - box.Min.Y) * 0.75);

            points.Add(new XYZ(x1, y1, z));
            points.Add(new XYZ(x0, y0, z));
            points.Add(new XYZ(x0, y2, z));
            points.Add(new XYZ(x2, y0, z));
            points.Add(new XYZ(x2, y2, z));
            return points;
        }

        private static XYZ GetWallRoomSampleNormal(Wall wall)
        {
            if (wall == null) return null;

            try
            {
                XYZ orientation = wall.Orientation;
                XYZ normal = orientation == null ? null : new XYZ(orientation.X, orientation.Y, 0.0);
                if (normal != null && normal.GetLength() > 1e-9)
                {
                    return normal.Normalize();
                }
            }
            catch
            {
            }

            XYZ direction = GetWallDirection(wall);
            if (direction == null) return null;
            return new XYZ(-direction.Y, direction.X, 0.0);
        }

        private static XYZ GetCurvePointAtNormalizedParameter(Curve curve, double fraction)
        {
            if (curve == null) return null;

            try
            {
                return curve.Evaluate(Math.Max(0.0, Math.Min(1.0, fraction)), true);
            }
            catch
            {
                return null;
            }
        }

        private static double EstimateFinishGrossArea(
            Element element,
            QsFinishMeasurementKind kind,
            double measuredArea)
        {
            if (element == null) return 0.0;

            if (IsHorizontalFinishKind(kind, element))
            {
                double horizontalGross = EstimateHorizontalFinishGrossArea(element);
                if (horizontalGross > 1e-9)
                {
                    return horizontalGross;
                }
            }

            double modeledOpeningArea = ComputeModeledFinishOpeningArea(element, kind);
            return Math.Max(0.0, measuredArea + modeledOpeningArea);
        }

        private static double ComputeModeledFinishOpeningArea(Element element, QsFinishMeasurementKind kind)
        {
            if (element == null) return 0.0;

            bool wallKind = kind == QsFinishMeasurementKind.WallFinish ||
                            (kind == QsFinishMeasurementKind.Waterproof &&
                             element.Category?.Id?.Value == (long)BuiltInCategory.OST_Walls);
            Wall wall = wallKind ? element as Wall : null;
            if (wall != null)
            {
                XYZ wallDir = GetWallDirection(wall);
                if (wallDir == null) return 0.0;

                double area = 0.0;
                foreach (WallOpeningMeasure opening in GetWallOpeningMeasures(wall, 0.0))
                {
                    if (opening == null || !opening.IncludeSide) continue;
                    area += EstimateOpeningProjectedAreaFt2(opening.Box, wallDir);
                }

                return area;
            }

            if (!IsHorizontalFinishKind(kind, element)) return 0.0;

            return GetSlabOpeningMeasures(element, 0.0)
                .Where(opening => opening != null && opening.IncludeSide)
                .Sum(opening => opening.ProjectedAreaFt2);
        }

        private static double ComputeFinishOpeningDeduction(
            Document doc,
            Element element,
            QsFinishMeasurementKind kind,
            QsMeasurementRuntimeRules rules)
        {
            bool enabled;
            double thresholdFt2;
            if (!TryGetFinishOpeningRule(kind, rules, out enabled, out thresholdFt2) || !enabled) return 0.0;

            Wall wall = GetFinishWallOpeningSource(doc, element, kind);
            if (wall != null)
            {
                XYZ wallDir = GetWallDirection(wall);
                if (wallDir == null) return 0.0;

                double area = 0.0;
                foreach (WallOpeningMeasure opening in GetWallOpeningMeasures(wall, thresholdFt2))
                {
                    if (opening == null || !opening.IncludeSide) continue;
                    area += EstimateOpeningProjectedAreaFt2(opening.Box, wallDir);
                }

                return area;
            }

            if (!IsHorizontalFinishKind(kind, element)) return 0.0;

            double horizontalArea = 0.0;
            foreach (SlabOpeningMeasure opening in GetSlabOpeningMeasures(element, thresholdFt2))
            {
                if (opening == null || !opening.IncludeSide) continue;
                horizontalArea += opening.ProjectedAreaFt2;
            }

            return horizontalArea;
        }

        private static double ComputeFinishReturnArea(
            Document doc,
            Element element,
            QsFinishMeasurementKind kind,
            QsMeasurementRuntimeRules rules)
        {
            bool enabled;
            double thresholdFt2;
            if (!TryGetFinishReturnRule(kind, rules, out enabled, out thresholdFt2) || !enabled) return 0.0;

            double returnDepthFt = GetFinishReturnDepthFt(doc, element, kind);
            if (returnDepthFt <= 1e-9) return 0.0;

            Wall wall = GetFinishWallOpeningSource(doc, element, kind);
            if (wall != null)
            {
                XYZ wallDir = GetWallDirection(wall);
                if (wallDir == null) return 0.0;

                double area = 0.0;
                foreach (WallOpeningMeasure opening in GetWallOpeningMeasures(wall, thresholdFt2))
                {
                    if (opening == null || !opening.IncludeSide) continue;

                    double width;
                    double height;
                    if (!TryGetWallOpeningProjectedDimensions(opening.Box, wallDir, out width, out height)) continue;
                    area += (width + (2.0 * height)) * returnDepthFt;
                }

                return area;
            }

            if (!IsHorizontalFinishKind(kind, element)) return 0.0;

            double horizontalArea = 0.0;
            foreach (SlabOpeningMeasure opening in GetSlabOpeningMeasures(element, thresholdFt2))
            {
                if (opening == null || !opening.IncludeSide) continue;
                horizontalArea += EstimateHorizontalOpeningPerimeterFt(opening) * returnDepthFt;
            }

            return horizontalArea;
        }

        private static double GetFinishReturnDepthFt(Document doc, Element element, QsFinishMeasurementKind kind)
        {
            bool wallKind = kind == QsFinishMeasurementKind.WallFinish ||
                            (kind == QsFinishMeasurementKind.Waterproof &&
                             element?.Category?.Id?.Value == (long)BuiltInCategory.OST_Walls);
            if (wallKind)
            {
                Wall hostWall = TryResolveHostWallFromComments(doc, element);
                if (hostWall != null && hostWall.Width > 1e-9)
                {
                    return hostWall.Width;
                }
            }

            return GetFinishThicknessFt(element, kind);
        }

        private static bool TryGetFinishOpeningRule(
            QsFinishMeasurementKind kind,
            QsMeasurementRuntimeRules rules,
            out bool enabled,
            out double thresholdFt2)
        {
            enabled = false;
            thresholdFt2 = 0.0;
            if (rules == null) return false;

            switch (kind)
            {
                case QsFinishMeasurementKind.WallFinish:
                    enabled = rules.WallFinishOpeningEnabled;
                    thresholdFt2 = rules.WallFinishOpeningThresholdFt2;
                    return true;
                case QsFinishMeasurementKind.CeilingFinish:
                    enabled = rules.CeilingFinishOpeningEnabled;
                    thresholdFt2 = rules.CeilingFinishOpeningThresholdFt2;
                    return true;
                case QsFinishMeasurementKind.SuspendedCeiling:
                    enabled = rules.SuspendedCeilingOpeningEnabled;
                    thresholdFt2 = rules.SuspendedCeilingOpeningThresholdFt2;
                    return true;
                case QsFinishMeasurementKind.FloorFinish:
                    enabled = rules.FloorFinishOpeningEnabled;
                    thresholdFt2 = rules.FloorFinishOpeningThresholdFt2;
                    return true;
                case QsFinishMeasurementKind.Waterproof:
                    enabled = rules.WaterproofOpeningEnabled;
                    thresholdFt2 = rules.WaterproofOpeningThresholdFt2;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetFinishReturnRule(
            QsFinishMeasurementKind kind,
            QsMeasurementRuntimeRules rules,
            out bool enabled,
            out double thresholdFt2)
        {
            bool openingEnabled;
            if (!TryGetFinishOpeningRule(kind, rules, out openingEnabled, out thresholdFt2))
            {
                enabled = false;
                return false;
            }

            switch (kind)
            {
                case QsFinishMeasurementKind.WallFinish:
                    enabled = rules.WallFinishReturnEnabled;
                    return true;
                case QsFinishMeasurementKind.CeilingFinish:
                    enabled = rules.CeilingFinishReturnEnabled;
                    return true;
                case QsFinishMeasurementKind.SuspendedCeiling:
                    enabled = rules.SuspendedCeilingReturnEnabled;
                    return true;
                case QsFinishMeasurementKind.FloorFinish:
                    enabled = rules.FloorFinishReturnEnabled;
                    return true;
                case QsFinishMeasurementKind.Waterproof:
                    enabled = rules.WaterproofReturnEnabled;
                    return true;
                default:
                    enabled = false;
                    return false;
            }
        }

        private static Wall GetFinishWallOpeningSource(Document doc, Element element, QsFinishMeasurementKind kind)
        {
            if (element == null) return null;

            bool isWallKind = kind == QsFinishMeasurementKind.WallFinish ||
                              (kind == QsFinishMeasurementKind.Waterproof &&
                               element.Category?.Id?.Value == (long)BuiltInCategory.OST_Walls);
            if (!isWallKind) return null;

            Wall selfWall = element as Wall;
            if (selfWall != null && GetWallOpeningMeasures(selfWall, 0.0).Count > 0)
            {
                return selfWall;
            }

            Wall hostWall = TryResolveHostWallFromComments(doc, element);
            if (hostWall != null)
            {
                return hostWall;
            }

            return selfWall;
        }

        private static Wall TryResolveHostWallFromComments(Document doc, Element element)
        {
            if (doc == null || element == null) return null;

            string comments = GetStringParameterValue(element, "Comments");
            if (string.IsNullOrWhiteSpace(comments) ||
                comments.IndexOf("wall finish from", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(comments, @"\b\d+\b"))
            {
                long idValue;
                if (!long.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idValue)) continue;
                if (idValue <= 0) continue;

                Wall wall = doc.GetElement(new ElementId(idValue)) as Wall;
                if (wall != null)
                {
                    return wall;
                }
            }

            return null;
        }

        private static bool IsHorizontalFinishKind(QsFinishMeasurementKind kind, Element element)
        {
            if (element == null) return false;
            if (element.Category?.Id?.Value == (long)BuiltInCategory.OST_Walls) return false;

            return kind == QsFinishMeasurementKind.CeilingFinish ||
                   kind == QsFinishMeasurementKind.SuspendedCeiling ||
                   kind == QsFinishMeasurementKind.FloorFinish ||
                   kind == QsFinishMeasurementKind.Waterproof;
        }

        private static double ComputeWaterproofUpturnArea(Element element, QsMeasurementRuntimeRules rules)
        {
            if (element == null || rules == null || !rules.WaterproofUpturnEnabled) return 0.0;
            if (rules.WaterproofUpturnHeightFt <= 1e-9) return 0.0;
            if (element.Category?.Id?.Value == (long)BuiltInCategory.OST_Walls) return 0.0;

            double perimeter = EstimateHorizontalFinishPerimeter(element);
            if (perimeter <= 1e-9) return 0.0;

            return perimeter * rules.WaterproofUpturnHeightFt;
        }

        private static double EstimateFinishArea(Element element, QsFinishMeasurementKind kind)
        {
            if (element == null) return 0.0;

            double scheduleArea = GetElementArea(element);
            if (scheduleArea > 1e-9) return scheduleArea;

            switch (kind)
            {
                case QsFinishMeasurementKind.WallFinish:
                    return EstimateVerticalFinishArea(element);
                case QsFinishMeasurementKind.CeilingFinish:
                case QsFinishMeasurementKind.SuspendedCeiling:
                case QsFinishMeasurementKind.FloorFinish:
                    return EstimateHorizontalFinishArea(element);
                case QsFinishMeasurementKind.Waterproof:
                    return EstimateWaterproofFinishArea(element);
                default:
                    return 0.0;
            }
        }

        private static double EstimateWaterproofFinishArea(Element element)
        {
            long categoryId = element?.Category?.Id?.Value ?? 0;
            if (categoryId == (long)BuiltInCategory.OST_Walls)
            {
                return EstimateVerticalFinishArea(element);
            }

            return EstimateHorizontalFinishArea(element);
        }

        private static double EstimateVerticalFinishArea(Element element)
        {
            double sideArea = 0.0;
            double bottomArea = 0.0;
            double topArea = 0.0;
            BuildFormworkFaceSlabs(
                element,
                includeSides: true,
                includeBottom: false,
                includeTop: false,
                out sideArea,
                out bottomArea,
                out topArea);

            if (sideArea <= 1e-9) return 0.0;
            return sideArea * 0.5;
        }

        private static double EstimateHorizontalFinishArea(Element element)
        {
            if (element == null) return 0.0;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double best = 0.0;
            foreach (Solid solid in GetElementSolids(element, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    if (face == null) continue;
                    double area = face.Area;
                    if (area <= best) continue;

                    XYZ normal = GetFaceNormal(face);
                    if (normal == null || Math.Abs(normal.Z) < 0.85) continue;
                    best = area;
                }
            }

            return best;
        }

        private static double EstimateHorizontalFinishGrossArea(Element element)
        {
            if (element == null) return 0.0;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double best = 0.0;
            foreach (Solid solid in GetElementSolids(element, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null || pf.FaceNormal == null || Math.Abs(pf.FaceNormal.Z) < 0.85) continue;

                    IList<CurveLoop> loops;
                    try
                    {
                        loops = pf.GetEdgesAsCurveLoops();
                    }
                    catch
                    {
                        continue;
                    }

                    double outerArea = GetLargestProjectedLoopAreaFt2(loops);
                    if (outerArea > best)
                    {
                        best = outerArea;
                    }
                }
            }

            return best;
        }

        private static double EstimateHorizontalFinishPerimeter(Element element)
        {
            if (element == null) return 0.0;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double bestArea = 0.0;
            double bestPerimeter = 0.0;
            foreach (Solid solid in GetElementSolids(element, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null || pf.FaceNormal == null || Math.Abs(pf.FaceNormal.Z) < 0.85) continue;

                    IList<CurveLoop> loops;
                    try
                    {
                        loops = pf.GetEdgesAsCurveLoops();
                    }
                    catch
                    {
                        continue;
                    }

                    double perimeter = GetLargestCurveLoopLength(loops);
                    if (perimeter <= 1e-9) continue;

                    double area = Math.Max(pf.Area, GetLargestProjectedLoopAreaFt2(loops));
                    if (area > bestArea + 1e-6 || (Math.Abs(area - bestArea) <= 1e-6 && perimeter > bestPerimeter))
                    {
                        bestArea = area;
                        bestPerimeter = perimeter;
                    }
                }
            }

            if (bestPerimeter > 1e-9) return bestPerimeter;

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            double width;
            double depth;
            return TryGetHorizontalProjectedDimensions(box, out width, out depth)
                ? 2.0 * (width + depth)
                : 0.0;
        }

        private static double GetLargestProjectedLoopAreaFt2(IList<CurveLoop> loops)
        {
            if (loops == null || loops.Count == 0) return 0.0;

            double best = 0.0;
            foreach (CurveLoop loop in loops)
            {
                List<XYZ> points = TessellateCurveLoop(loop);
                if (points.Count < 3) continue;

                double area = Math.Abs(ProjectedLoopAreaFt2(points));
                if (area > best) best = area;
            }

            return best;
        }

        private static double EstimateHorizontalOpeningPerimeterFt(SlabOpeningMeasure opening)
        {
            if (opening == null) return 0.0;

            if (opening.BoundaryPoints != null && opening.BoundaryPoints.Count >= 3)
            {
                double length = 0.0;
                for (int i = 0; i < opening.BoundaryPoints.Count; i++)
                {
                    XYZ a = opening.BoundaryPoints[i];
                    XYZ b = opening.BoundaryPoints[(i + 1) % opening.BoundaryPoints.Count];
                    if (a == null || b == null) continue;
                    length += DistanceXY(a, b);
                }

                return length;
            }

            double width;
            double depth;
            return TryGetHorizontalProjectedDimensions(opening.Box, out width, out depth)
                ? 2.0 * (width + depth)
                : 0.0;
        }

        private static bool TryGetHorizontalProjectedDimensions(BoundingBoxXYZ box, out double width, out double depth)
        {
            width = 0.0;
            depth = 0.0;
            if (box == null) return false;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (XYZ p in GetBoundingBoxCorners(box))
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            width = maxX - minX;
            depth = maxY - minY;
            return width > 1e-9 && depth > 1e-9;
        }

        private static bool TryGetWallOpeningProjectedDimensions(
            BoundingBoxXYZ box,
            XYZ wallDir,
            out double width,
            out double height)
        {
            width = 0.0;
            height = 0.0;
            if (box == null || wallDir == null) return false;

            double minAlong = double.MaxValue;
            double maxAlong = double.MinValue;
            double minZ = double.MaxValue;
            double maxZ = double.MinValue;

            foreach (XYZ p in GetBoundingBoxCorners(box))
            {
                double along = p.DotProduct(wallDir);
                if (along < minAlong) minAlong = along;
                if (along > maxAlong) maxAlong = along;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }

            width = maxAlong - minAlong;
            height = maxZ - minZ;
            return width > 1e-9 && height > 1e-9;
        }

        private static double GetFinishThicknessFt(Element element, QsFinishMeasurementKind kind)
        {
            if (element == null) return 0.0;

            Wall wall = element as Wall;
            if (wall != null && wall.Width > 1e-9)
            {
                return wall.Width;
            }

            ElementType type = GetElementType(element);
            HostObjAttributes hostType = type as HostObjAttributes;
            if (hostType != null)
            {
                try
                {
                    CompoundStructure compound = hostType.GetCompoundStructure();
                    if (compound != null && compound.GetWidth() > 1e-9)
                    {
                        return compound.GetWidth();
                    }
                }
                catch
                {
                }
            }

            double parameterThickness = GetParamDouble(element, new[]
            {
                "Finish Thickness",
                "Thickness",
                "Layer Thickness",
                "Width",
                "Depth"
            });
            if (parameterThickness > 1e-9) return parameterThickness;

            parameterThickness = GetParamDouble(type, new[]
            {
                "Finish Thickness",
                "Thickness",
                "Layer Thickness",
                "Width",
                "Depth"
            });
            if (parameterThickness > 1e-9) return parameterThickness;

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return 0.0;

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double dz = Math.Abs(box.Max.Z - box.Min.Z);

            if (IsHorizontalFinishKind(kind, element) && dz > 1e-9)
            {
                return dz;
            }

            double minPlan = Math.Min(dx > 1e-9 ? dx : double.MaxValue, dy > 1e-9 ? dy : double.MaxValue);
            if (minPlan < double.MaxValue) return minPlan;
            return dz > 1e-9 ? dz : 0.0;
        }

        private static void ComputeFoundationFormworkAreas(Element element, bool includeSides, bool includeTop, out double sidesArea, out double topArea)
        {
            sidesArea = 0.0;
            topArea = 0.0;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            GeometryElement ge = element.get_Geometry(opt);
            if (ge == null) return;

            Transform identity = Transform.Identity;
            foreach (GeometryObject go in ge)
            {
                AddFormworkAreasFromGeometry(go, identity, includeSides, includeTop, ref sidesArea, ref topArea);
            }
        }

        private static void ComputeFoundationSideStageQuantities(
            Element element,
            QsMeasurementRuntimeRules rules,
            out double totalLength,
            out double stagedArea,
            double[] lengthStages,
            double[] areaStages)
        {
            totalLength = 0.0;
            stagedArea = 0.0;
            if (lengthStages != null) Array.Clear(lengthStages, 0, lengthStages.Length);
            if (areaStages != null) Array.Clear(areaStages, 0, areaStages.Length);

            if (element == null || rules == null || !rules.FoundationIncludeSide || !rules.FoundationSideSettingsEnabled)
            {
                return;
            }

            string key = ResolveFoundationSideSettingsKey(element);
            FoundationSideStageRule rule = rules.GetFoundationSideRule(key);
            if (rule == null || !rule.CalculateInStages)
            {
                return;
            }

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            List<FaceSlab> sideSlabs = BuildSideFaceSlabs(GetElementSolids(element, opt));
            foreach (FaceSlab slab in sideSlabs)
            {
                if (slab == null || slab.FaceArea <= 1e-9) continue;

                double height = slab.FaceHeight;
                if (height <= 1e-9)
                {
                    height = EstimateFaceHeightFromAreaAndBox(slab);
                }
                if (height <= 1e-9) continue;

                double sideLength = slab.FaceArea / height;
                if (sideLength <= 1e-9) continue;

                totalLength += sideLength;
                int stage = CalculateFoundationSideStageIndex(height, rule);
                if (stage <= 0 || stage > FoundationSidePersistedStageLimit)
                {
                    continue;
                }

                bool calculateByArea = rule.CalculateAreaStages ||
                                       (rule.UseLengthOnlyBelowCondition && height > rule.LengthConditionFt + 1e-9);
                if (calculateByArea)
                {
                    if (areaStages != null && stage < areaStages.Length)
                    {
                        areaStages[stage] += slab.FaceArea;
                    }
                    stagedArea += slab.FaceArea;
                }
                else if (lengthStages != null && stage < lengthStages.Length)
                {
                    lengthStages[stage] += sideLength;
                }
            }
        }

        private static double EstimateFaceHeightFromAreaAndBox(FaceSlab slab)
        {
            if (slab == null || slab.SlabSolid == null) return 0.0;
            try
            {
                BoundingBoxXYZ box = slab.SlabSolid.GetBoundingBox();
                if (box == null) return 0.0;
                return Math.Max(0.0, box.Max.Z - box.Min.Z);
            }
            catch
            {
                return 0.0;
            }
        }

        private static string ResolveFoundationSideSettingsKey(Element element)
        {
            string text = BuildAuxiliaryConcreteClassificationText(element);
            if (string.IsNullOrWhiteSpace(text)) return "PAD";

            if (text.Contains("blinding")) return "BLINDING";
            if (text.Contains("pile cap") || text.Contains("pilecap")) return "PILECAP";
            if (text.Contains("ground beam") || text.Contains("groundbeam")) return "GROUNDBEAM";
            if (text.Contains("strip")) return "STRIP";
            if (text.Contains("raft") || text.Contains("foundation slab")) return "RAFT";
            if (text.Contains("pad") || text.Contains("machinebase") || text.Contains("rectangular")) return "PAD";
            return "PAD";
        }

        private static int CalculateFoundationSideStageIndex(double height, FoundationSideStageRule rule)
        {
            if (height <= 1e-9 || rule == null || rule.SegmentThresholdsFt.Count == 0) return 0;

            for (int i = 0; i < rule.SegmentThresholdsFt.Count; i++)
            {
                if (height <= rule.SegmentThresholdsFt[i] + 1e-9)
                {
                    return Math.Min(i + 1, FoundationSidePersistedStageLimit);
                }
            }

            double last = rule.SegmentThresholdsFt[rule.SegmentThresholdsFt.Count - 1];
            if (rule.ThereafterFt <= 1e-9)
            {
                return Math.Min(rule.SegmentThresholdsFt.Count, FoundationSidePersistedStageLimit);
            }

            int thereafterStage = rule.SegmentThresholdsFt.Count +
                                  (int)Math.Ceiling((height - last - 1e-9) / rule.ThereafterFt);
            if (thereafterStage < 1) thereafterStage = 1;
            if (thereafterStage > FoundationSidePersistedStageLimit) thereafterStage = FoundationSidePersistedStageLimit;
            return thereafterStage;
        }

        private static void AddFormworkAreasFromGeometry(GeometryObject go, Transform trf, bool includeSides, bool includeTop, ref double sidesArea, ref double topArea)
        {
            if (go == null) return;

            if (go is GeometryInstance inst)
            {
                Transform instTrf = trf.Multiply(inst.Transform);
                GeometryElement instGeom = inst.GetInstanceGeometry();
                if (instGeom != null)
                {
                    foreach (GeometryObject igo in instGeom)
                    {
                        AddFormworkAreasFromGeometry(igo, instTrf, includeSides, includeTop, ref sidesArea, ref topArea);
                    }
                }
                return;
            }

            if (go is Solid solid && solid.Faces.Size > 0)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face == null) continue;
                    double area = face.Area;
                    if (area <= 1e-9) continue;

                    XYZ normal = GetFaceNormal(face);
                    if (normal == null) continue;
                    normal = trf.OfVector(normal).Normalize();

                    double z = normal.Z;
                    if (includeSides && Math.Abs(z) < 0.1)
                    {
                        sidesArea += area;
                    }
                    else if (includeTop && z > 0.9)
                    {
                        topArea += area;
                    }
                }
            }
        }

        private static XYZ GetFaceNormal(Face face)
        {
            if (face is PlanarFace pf)
            {
                return pf.FaceNormal?.Normalize();
            }

            try
            {
                BoundingBoxUV bb = face.GetBoundingBox();
                UV uv = bb != null
                    ? new UV((bb.Min.U + bb.Max.U) * 0.5, (bb.Min.V + bb.Max.V) * 0.5)
                    : new UV(0, 0);
                XYZ n = face.ComputeNormal(uv);
                if (n == null || n.GetLength() <= 1e-9) return null;
                return n.Normalize();
            }
            catch
            {
                return null;
            }
        }

        private static void ComputeFoundationIntersectionAreas(
            Document doc,
            Element foundation,
            IList<Element> foundations,
            IList<Element> beams,
            IList<Element> columns,
            IList<Element> walls,
            IList<Element> floors,
            IList<Element> generics,
            QsMeasurementRuntimeRules rules,
            out double subFoun,
            out double subBeam,
            out double subCol,
            out double subWall,
            out double subFloor,
            out double subGeneric)
        {
            subFoun = 0.0;
            subBeam = 0.0;
            subCol = 0.0;
            subWall = 0.0;
            subFloor = 0.0;
            subGeneric = 0.0;

            if (doc == null || foundation == null) return;
            if (rules == null || !rules.FoundationIncludeSide) return;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            List<Solid> foundationSolids = GetElementSolids(foundation, opt);
            if (foundationSolids.Count == 0) return;

            List<FaceSlab> sideSlabs = BuildSideFaceSlabs(foundationSolids);
            if (sideSlabs.Count == 0) return;

            BoundingBoxXYZ foundationBox = foundation.get_BoundingBox(null);

            subFoun = rules.FoundationDeductFoundation ? SumIntersectionArea(sideSlabs, foundationBox, foundations, opt, foundation.Id) : 0.0;
            subBeam = rules.FoundationDeductBeam ? SumIntersectionArea(sideSlabs, foundationBox, beams, opt, foundation.Id) : 0.0;
            subCol = rules.FoundationDeductColumn ? SumIntersectionArea(sideSlabs, foundationBox, columns, opt, foundation.Id) : 0.0;
            subWall = rules.FoundationDeductWall ? SumIntersectionArea(sideSlabs, foundationBox, walls, opt, foundation.Id) : 0.0;
            subFloor = rules.FoundationDeductFloor ? SumIntersectionArea(sideSlabs, foundationBox, floors, opt, foundation.Id) : 0.0;
            subGeneric = rules.FoundationDeductGeneric ? SumIntersectionArea(sideSlabs, foundationBox, generics, opt, foundation.Id) : 0.0;
        }

        private static double SumIntersectionArea(
            List<FaceSlab> faceSlabs,
            BoundingBoxXYZ hostBox,
            IList<Element> elements,
            Options opt,
            ElementId hostId,
            Func<Element, bool> elementFilter = null)
        {
            if (elements == null || elements.Count == 0) return 0.0;
            double area = 0.0;

            foreach (Element element in elements)
            {
                if (element == null) continue;
                if (hostId != null && element.Id != null && element.Id.Value == hostId.Value) continue;
                if (elementFilter != null && !elementFilter(element)) continue;
                if (hostBox != null)
                {
                    BoundingBoxXYZ otherBox = element.get_BoundingBox(null);
                    if (!BoundingBoxesIntersect(hostBox, otherBox))
                    {
                        continue;
                    }
                }

                List<Solid> solids = GetElementSolids(element, opt);
                if (solids.Count == 0) continue;

                foreach (FaceSlab slab in faceSlabs)
                {
                    foreach (Solid solid in solids)
                    {
                        double hitArea = IntersectAreaFromSlab(slab, solid);
                        if (hitArea > 0)
                        {
                            area += hitArea;
                        }
                    }
                }
            }

            return area;
        }

        private static List<FaceSlab> FilterFaceSlabsByType(IList<FaceSlab> slabs, params string[] faceTypes)
        {
            var result = new List<FaceSlab>();
            if (slabs == null || slabs.Count == 0) return result;

            HashSet<string> types = new HashSet<string>(
                (faceTypes ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)),
                StringComparer.OrdinalIgnoreCase);
            if (types.Count == 0)
            {
                result.AddRange(slabs.Where(s => s != null));
                return result;
            }

            foreach (FaceSlab slab in slabs)
            {
                if (slab == null) continue;
                if (types.Contains(slab.FaceType ?? ""))
                {
                    result.Add(slab);
                }
            }

            return result;
        }

        private static double SumSlabSoffitContactArea(
            IList<FaceSlab> soffitSlabs,
            BoundingBoxXYZ hostBox,
            IList<Element> elements,
            Options opt,
            ElementId hostId)
        {
            if (soffitSlabs == null || soffitSlabs.Count == 0 || elements == null || elements.Count == 0) return 0.0;

            double area = 0.0;
            foreach (Element element in elements)
            {
                if (element == null) continue;
                if (hostId != null && element.Id != null && element.Id.Value == hostId.Value) continue;

                BoundingBoxXYZ otherBox = element.get_BoundingBox(null);
                if (hostBox != null && otherBox != null && !BoundingBoxesIntersectOrTouch(hostBox, otherBox, FormworkHalfThicknessFt * 8.0))
                {
                    continue;
                }

                List<Solid> solids = GetElementSolids(element, opt);
                if (solids.Count == 0) continue;

                foreach (FaceSlab slab in soffitSlabs)
                {
                    foreach (Solid solid in solids)
                    {
                        double hitArea = IntersectContactAreaFromSlab(slab, solid, otherBox);
                        if (hitArea > 0.0)
                        {
                            area += hitArea;
                        }
                    }
                }
            }

            return area;
        }

        private static double IntersectContactAreaFromSlab(FaceSlab slab, Solid other, BoundingBoxXYZ otherBox)
        {
            if (slab == null || slab.SlabSolid == null || other == null) return 0.0;

            try
            {
                Solid inter = BooleanOperationsUtils.ExecuteBooleanOperation(slab.SlabSolid, other, BooleanOperationsType.Intersect);
                if (inter == null || inter.Volume <= 1e-9) return 0.0;

                double thickness = GetContactDivisorThickness(slab, otherBox);
                if (thickness <= 1e-9) thickness = slab.Thickness;
                return inter.Volume / thickness;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetContactDivisorThickness(FaceSlab slab, BoundingBoxXYZ otherBox)
        {
            if (slab == null || slab.Thickness <= 1e-9) return 0.0;
            if (slab.FaceNormal == null || slab.FacePoint == null || otherBox == null) return slab.Thickness;

            double minDistance = double.MaxValue;
            double maxDistance = double.MinValue;
            foreach (XYZ corner in GetBoundingBoxCorners(otherBox))
            {
                double distance = (corner - slab.FacePoint).DotProduct(slab.FaceNormal);
                if (distance < minDistance) minDistance = distance;
                if (distance > maxDistance) maxDistance = distance;
            }

            if (minDistance == double.MaxValue || maxDistance == double.MinValue) return slab.Thickness;
            double tol = FormworkHalfThicknessFt * 2.0;
            bool singleSidedContact = minDistance >= -tol || maxDistance <= tol;
            return singleSidedContact ? slab.Thickness * 0.5 : slab.Thickness;
        }

        private static List<FaceSlab> BuildSideFaceSlabs(List<Solid> solids)
        {
            var slabs = new List<FaceSlab>();
            if (solids == null) return slabs;

            double halfThickness = FormworkHalfThicknessFt;

            foreach (Solid solid in solids)
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;
                    XYZ normal = pf.FaceNormal;
                    if (Math.Abs(normal.Z) > 0.1) continue;

                    Solid slab = CreateFaceSlab(pf, halfThickness);
                    if (slab != null && slab.Volume > 1e-9)
                    {
                        slabs.Add(new FaceSlab(slab, halfThickness * 2.0, "Side", pf.Area, pf.FaceNormal, pf.Origin, EstimateFaceVerticalHeight(pf)));
                    }
                }
            }

            return slabs;
        }

        private static Solid CreateFaceSlab(PlanarFace face, double halfThickness)
        {
            try
            {
                IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0) return null;

                XYZ normal = face.FaceNormal;
                Solid pos = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal, halfThickness);
                Solid neg = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal.Negate(), halfThickness);

                Solid union = BooleanOperationsUtils.ExecuteBooleanOperation(pos, neg, BooleanOperationsType.Union);
                return union;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<FaceSlab> CreateTriangulatedFaceSlabs(Face face, double halfThickness, string faceType)
        {
            var slabs = new List<FaceSlab>();
            if (face == null) return slabs;

            try
            {
                Mesh mesh = face.Triangulate();
                if (mesh == null || mesh.NumTriangles <= 0) return slabs;

                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle tri = mesh.get_Triangle(i);
                    XYZ p0 = tri.get_Vertex(0);
                    XYZ p1 = tri.get_Vertex(1);
                    XYZ p2 = tri.get_Vertex(2);
                    if (p0 == null || p1 == null || p2 == null) continue;

                    XYZ v1 = p1 - p0;
                    XYZ v2 = p2 - p0;
                    XYZ cross = v1.CrossProduct(v2);
                    double crossLen = cross.GetLength();
                    double triArea = 0.5 * crossLen;
                    if (triArea <= 1e-9) continue;
                    XYZ normal = crossLen > 1e-9 ? cross.Normalize() : XYZ.BasisZ;

                    Solid slab = CreateTriangleSlab(p0, p1, p2, halfThickness);
                    if (slab != null && slab.Volume > 1e-9)
                    {
                        XYZ centroid = new XYZ((p0.X + p1.X + p2.X) / 3.0, (p0.Y + p1.Y + p2.Y) / 3.0, (p0.Z + p1.Z + p2.Z) / 3.0);
                        double faceHeight = IsSideFaceType(faceType) ? EstimateTriangleVerticalHeight(p0, p1, p2) : 0.0;
                        slabs.Add(new FaceSlab(slab, halfThickness * 2.0, faceType, triArea, normal, centroid, faceHeight));
                    }
                }
            }
            catch
            {
                // ignore; caller falls back to quantity-only behavior.
            }

            return slabs;
        }

        private static Solid CreateTriangleSlab(XYZ p0, XYZ p1, XYZ p2, double halfThickness)
        {
            try
            {
                XYZ normal = (p1 - p0).CrossProduct(p2 - p0);
                if (normal == null || normal.GetLength() <= 1e-9) return null;
                normal = normal.Normalize();

                CurveLoop loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p0));

                IList<CurveLoop> loops = new List<CurveLoop> { loop };
                Solid pos = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal, halfThickness);
                Solid neg = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal.Negate(), halfThickness);
                return BooleanOperationsUtils.ExecuteBooleanOperation(pos, neg, BooleanOperationsType.Union);
            }
            catch
            {
                return null;
            }
        }

        private static void AddFaceSlabsFromMeshGeometry(
            GeometryObject go,
            Transform trf,
            bool includeSides,
            bool includeBottom,
            bool includeTop,
            double slopedTopMinAngleDegrees,
            double halfThickness,
            List<FaceSlab> slabs,
            ref double sideArea,
            ref double bottomArea,
            ref double topArea)
        {
            if (go == null) return;
            if (trf == null) trf = Transform.Identity;

            if (go is GeometryInstance inst)
            {
                Transform instTrf = trf.Multiply(inst.Transform);
                GeometryElement instGeom = inst.GetInstanceGeometry();
                if (instGeom != null)
                {
                    foreach (GeometryObject igo in instGeom)
                    {
                        AddFaceSlabsFromMeshGeometry(
                            igo,
                            instTrf,
                            includeSides,
                            includeBottom,
                            includeTop,
                            slopedTopMinAngleDegrees,
                            halfThickness,
                            slabs,
                            ref sideArea,
                            ref bottomArea,
                            ref topArea);
                    }
                }
                return;
            }

            if (go is GeometryElement ge)
            {
                foreach (GeometryObject child in ge)
                {
                    AddFaceSlabsFromMeshGeometry(
                        child,
                        trf,
                        includeSides,
                        includeBottom,
                        includeTop,
                        slopedTopMinAngleDegrees,
                        halfThickness,
                        slabs,
                        ref sideArea,
                        ref bottomArea,
                        ref topArea);
                }
                return;
            }

            if (!(go is Mesh mesh) || mesh.NumTriangles <= 0)
            {
                return;
            }

            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle tri = mesh.get_Triangle(i);
                XYZ p0 = trf.OfPoint(tri.get_Vertex(0));
                XYZ p1 = trf.OfPoint(tri.get_Vertex(1));
                XYZ p2 = trf.OfPoint(tri.get_Vertex(2));

                XYZ normal = (p1 - p0).CrossProduct(p2 - p0);
                double len = normal.GetLength();
                if (len <= 1e-9) continue;
                normal = normal.Normalize();

                double triArea = 0.5 * len;
                if (triArea <= 1e-9) continue;

                bool isSide = includeSides && Math.Abs(normal.Z) < 0.1;
                bool isBottom = includeBottom && normal.Z < -0.9;
                bool isSlopedTop = IsSlopedTopFormworkFace(normal, slopedTopMinAngleDegrees);
                bool isTop = (includeTop && normal.Z > 0.9) || isSlopedTop;
                if (!isSide && !isBottom && !isTop) continue;

                if (isSide) sideArea += triArea;
                if (isBottom) bottomArea += triArea;
                if (isTop) topArea += triArea;

                string faceType = isBottom ? "Bottom" : (isTop ? (isSlopedTop ? "TopSlope" : "Top") : "Side");
                Solid slab = CreateTriangleSlab(p0, p1, p2, halfThickness);
                if (slab != null && slab.Volume > 1e-9)
                {
                    XYZ centroid = new XYZ((p0.X + p1.X + p2.X) / 3.0, (p0.Y + p1.Y + p2.Y) / 3.0, (p0.Z + p1.Z + p2.Z) / 3.0);
                    double faceHeight = isSide ? EstimateTriangleVerticalHeight(p0, p1, p2) : 0.0;
                    slabs.Add(new FaceSlab(slab, halfThickness * 2.0, faceType, triArea, normal, centroid, faceHeight));
                }
            }
        }

        private static void ApplySlabOpeningSideThreshold(
            Element element,
            List<FaceSlab> slabs,
            QsMeasurementRuntimeRules rules,
            ref double sideArea,
            ref double openingSideArea)
        {
            if (element == null || slabs == null || slabs.Count == 0 || rules == null) return;

            List<SlabOpeningMeasure> openings = GetSlabOpeningMeasures(element, rules.SlabOpeningSideThresholdFt2);
            if (openings.Count == 0) return;

            for (int i = slabs.Count - 1; i >= 0; i--)
            {
                FaceSlab slab = slabs[i];
                if (!IsCandidateSlabOpeningSideSlab(slab)) continue;

                SlabOpeningMeasure opening = FindContainingSlabOpening(openings, slab.FacePoint);
                if (opening == null) continue;

                if (rules.SlabOpeningSideRuleEnabled && opening.IncludeSide)
                {
                    openingSideArea += slab.FaceArea;
                }
                else
                {
                    sideArea -= slab.FaceArea;
                    slabs.RemoveAt(i);
                }
            }

            if (sideArea < 0) sideArea = 0;
        }

        private static void ComputeSlabEdgeBreakAreas(
            List<FaceSlab> slabs,
            QsMeasurementRuntimeRules rules,
            out double lte250,
            out double lte500,
            out double lte1000,
            out double over1000)
        {
            lte250 = 0.0;
            lte500 = 0.0;
            lte1000 = 0.0;
            over1000 = 0.0;

            if (slabs == null || slabs.Count == 0 || rules == null) return;
            if (!rules.SlabIncludeSide || !rules.SlabEdgeSegmentationEnabled) return;

            foreach (FaceSlab slab in slabs)
            {
                if (!IsSlabSideFormworkFace(slab)) continue;

                double area = slab.FaceArea;
                if (area <= 1e-9) continue;

                double height = slab.FaceHeight;
                if (height <= 1e-9)
                {
                    height = rules.SlabEdgeBreakThirdThresholdFt + 1.0;
                }

                if (height <= rules.SlabEdgeBreakFirstThresholdFt + 1e-9)
                {
                    lte250 += area;
                }
                else if (height <= rules.SlabEdgeBreakSecondThresholdFt + 1e-9)
                {
                    lte500 += area;
                }
                else if (height <= rules.SlabEdgeBreakThirdThresholdFt + 1e-9)
                {
                    lte1000 += area;
                }
                else
                {
                    over1000 += area;
                }
            }
        }

        private static void ComputeColumnStrutQuantities(
            Element column,
            IList<FaceSlab> slabs,
            QsMeasurementRuntimeRules rules,
            double netArea,
            out double strutHeight,
            out int strutStage,
            out double basicArea,
            double[] stageAreas)
        {
            strutHeight = 0.0;
            strutStage = 0;
            basicArea = 0.0;
            if (stageAreas != null)
            {
                Array.Clear(stageAreas, 0, stageAreas.Length);
            }

            if (netArea <= 1e-9)
            {
                return;
            }

            strutHeight = GetElementVerticalHeight(column);
            if (rules == null || !rules.ColumnStrutEnabled || !rules.ColumnStrutStaged)
            {
                basicArea = netArea;
                return;
            }

            if (strutHeight + 1e-9 < rules.ColumnStrutJudgeHeightFt)
            {
                basicArea = netArea;
                return;
            }

            int maxStage = Math.Min(ColumnStrutPersistedStageLimit, rules.ColumnStrutMaxStageCount);
            double stageHeight = rules.ColumnStrutStageHeightFt;
            if (maxStage <= 0 || stageHeight <= 1e-9 || stageAreas == null || stageAreas.Length <= 1)
            {
                basicArea = netArea;
                return;
            }

            double basicHeight = rules.ColumnStrutStartHeightFt > 1e-9
                ? rules.ColumnStrutStartHeightFt
                : stageHeight;
            if (basicHeight <= 1e-9)
            {
                basicHeight = stageHeight;
            }

            double grossBasic = 0.0;
            double[] grossStages = new double[stageAreas.Length];

            if (slabs != null)
            {
                foreach (FaceSlab slab in slabs)
                {
                    if (!IsSlabSideFormworkFace(slab)) continue;

                    double faceArea = slab.FaceArea;
                    if (faceArea <= 1e-9) continue;

                    double faceHeight = slab.FaceHeight;
                    if (faceHeight <= 1e-9)
                    {
                        faceHeight = strutHeight;
                    }

                    SplitVerticalFormworkIntoStages(
                        faceArea,
                        faceHeight,
                        basicHeight,
                        stageHeight,
                        maxStage,
                        ref grossBasic,
                        grossStages);

                    if (faceHeight > strutHeight)
                    {
                        strutHeight = faceHeight;
                    }
                }
            }

            double grossTotal = grossBasic;
            for (int stage = 1; stage <= maxStage && stage < grossStages.Length; stage++)
            {
                grossTotal += grossStages[stage];
            }

            if (grossTotal <= 1e-9)
            {
                SplitVerticalFormworkIntoStages(
                    netArea,
                    strutHeight,
                    basicHeight,
                    stageHeight,
                    maxStage,
                    ref grossBasic,
                    grossStages);
                grossTotal = grossBasic;
                for (int stage = 1; stage <= maxStage && stage < grossStages.Length; stage++)
                {
                    grossTotal += grossStages[stage];
                }
            }

            if (grossTotal <= 1e-9)
            {
                basicArea = netArea;
                return;
            }

            double scale = netArea / grossTotal;
            basicArea = grossBasic * scale;
            for (int stage = 1; stage <= maxStage && stage < stageAreas.Length; stage++)
            {
                stageAreas[stage] = grossStages[stage] * scale;
                if (stageAreas[stage] > 1e-9)
                {
                    strutStage = stage;
                }
            }
        }

        private static void SplitVerticalFormworkIntoStages(
            double area,
            double height,
            double basicHeight,
            double stageHeight,
            int maxStage,
            ref double basicArea,
            double[] stageAreas)
        {
            if (area <= 1e-9 || height <= 1e-9)
            {
                return;
            }

            double basicOverlap = Math.Min(height, Math.Max(0.0, basicHeight));
            if (basicOverlap > 1e-9)
            {
                basicArea += area * (basicOverlap / height);
            }

            if (stageAreas == null || stageHeight <= 1e-9 || maxStage <= 0)
            {
                return;
            }

            for (int stage = 1; stage <= maxStage && stage < stageAreas.Length; stage++)
            {
                double low = basicHeight + ((stage - 1) * stageHeight);
                double high = basicHeight + (stage * stageHeight);
                double overlap = Math.Max(0.0, Math.Min(height, high) - Math.Max(0.0, low));
                if (stage == maxStage && height > high)
                {
                    overlap += height - high;
                }

                if (overlap > 1e-9)
                {
                    stageAreas[stage] += area * (overlap / height);
                }
            }
        }

        private static void ComputeWallEdgeBreakQuantities(
            Element wallElement,
            QsMeasurementRuntimeRules rules,
            double netArea,
            double[] lengthStages,
            double[] areaStages)
        {
            if (lengthStages != null)
            {
                Array.Clear(lengthStages, 0, lengthStages.Length);
            }

            if (areaStages != null)
            {
                Array.Clear(areaStages, 0, areaStages.Length);
            }

            if (wallElement == null || rules == null || !rules.WallIncludeSide || !rules.WallEdgeSegmentationEnabled)
            {
                return;
            }

            double width = GetWallFormworkWidth(wallElement);
            if (width <= 1e-9)
            {
                return;
            }

            int stage = GetEdgeBreakStageIndex(
                width,
                rules.WallEdgeBreakFirstThresholdFt,
                rules.WallEdgeBreakSecondThresholdFt,
                rules.WallEdgeBreakThirdThresholdFt);

            bool useArea = rules.WallEdgeMeasureAreaStages ||
                           width > rules.WallEdgeLengthConditionFt + 1e-9;
            if (useArea)
            {
                if (areaStages == null || stage >= areaStages.Length) return;
                areaStages[stage] = Math.Max(0.0, netArea);
                return;
            }

            if (lengthStages == null || stage >= lengthStages.Length) return;
            double length = GetElementLength(wallElement);
            if (length <= 1e-9 && netArea > 1e-9)
            {
                length = netArea / width;
            }

            lengthStages[stage] = Math.Max(0.0, length);
        }

        private static int GetEdgeBreakStageIndex(double width, double first, double second, double third)
        {
            if (width <= first + 1e-9) return 0;
            if (width <= second + 1e-9) return 1;
            if (width <= third + 1e-9) return 2;
            return 3;
        }

        private static void ComputeBeamStrutQuantities(
            Document doc,
            Element beam,
            IList<Element> floors,
            QsMeasurementRuntimeRules rules,
            double netArea,
            Options opt,
            out double strutHeight,
            out int strutStage,
            out double basicArea,
            double[] stageAreas)
        {
            strutHeight = 0.0;
            strutStage = 0;
            basicArea = 0.0;
            if (stageAreas != null)
            {
                Array.Clear(stageAreas, 0, stageAreas.Length);
            }

            if (doc == null || beam == null || rules == null || netArea <= 1e-9 || !rules.BeamStrutEnabled)
            {
                return;
            }

            if (!rules.BeamStrutStaged)
            {
                basicArea = netArea;
                return;
            }

            Options safeOpt = opt ?? new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double beamBottomZ;
            double supportPlaneZ;
            if (!TryGetSlabBottomPlaneZ(beam, safeOpt, out beamBottomZ) ||
                !TryFindLowerSupportPlaneZ(doc, beam, floors, safeOpt, beamBottomZ, out supportPlaneZ))
            {
                basicArea = netArea;
                return;
            }

            strutHeight = Math.Max(0.0, beamBottomZ - supportPlaneZ);
            if (strutHeight + 1e-9 < rules.BeamStrutJudgeHeightFt ||
                strutHeight <= rules.BeamStrutStartHeightFt + 1e-9)
            {
                basicArea = netArea;
                return;
            }

            int maxStage = Math.Min(BeamStrutPersistedStageLimit, rules.BeamStrutMaxStageCount);
            if (maxStage <= 0 || stageAreas == null || stageAreas.Length <= 1)
            {
                basicArea = netArea;
                return;
            }

            strutStage = CalculateStrutBucketIndex(
                strutHeight,
                rules.BeamStrutStartHeightFt,
                rules.BeamStrutStageHeightFt,
                maxStage);
            if (strutStage <= 0 || strutStage >= stageAreas.Length)
            {
                strutStage = 0;
                basicArea = netArea;
                return;
            }

            stageAreas[strutStage] = netArea;
        }

        private static int CalculateStrutBucketIndex(double strutHeight, double startHeight, double stageHeight, int maxStageCount)
        {
            if (maxStageCount <= 0 || strutHeight <= startHeight + 1e-9) return 0;
            if (stageHeight <= 1e-9) return 1;

            int stage = (int)Math.Ceiling((strutHeight - startHeight - 1e-9) / stageHeight);
            if (stage < 1) stage = 1;
            if (stage > maxStageCount) stage = maxStageCount;
            return stage;
        }

        private static void ComputeSlabStrutQuantities(
            Document doc,
            Element slab,
            IList<Element> floors,
            QsMeasurementRuntimeRules rules,
            double sideArea,
            double bottomArea,
            double topArea,
            Options opt,
            out double strutHeight,
            out double strutSoffitArea,
            out double strutStageCount,
            out double strutStageArea,
            out double strutEdgeArea,
            out double strutEdgeStageArea,
            out double strutTopArea,
            out double strutTopStageArea,
            double[] stageAreas)
        {
            strutHeight = 0.0;
            strutSoffitArea = 0.0;
            strutStageCount = 0.0;
            strutStageArea = 0.0;
            strutEdgeArea = 0.0;
            strutEdgeStageArea = 0.0;
            strutTopArea = 0.0;
            strutTopStageArea = 0.0;
            if (stageAreas != null)
            {
                Array.Clear(stageAreas, 0, stageAreas.Length);
            }

            if (doc == null || slab == null || rules == null) return;
            bool needsStrut =
                (rules.SlabIncludeBottom && rules.SlabStrutSoffitEnabled && bottomArea > 1e-9) ||
                (rules.SlabIncludeSide && rules.SlabStrutEdgeEnabled && sideArea > 1e-9) ||
                (rules.SlabStrutTopFormworkEnabled && topArea > 1e-9);
            if (!needsStrut) return;

            Options safeOpt = opt ?? new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double topPlaneZ;
            if (!TryGetSlabBottomPlaneZ(slab, safeOpt, out topPlaneZ)) return;

            double bottomPlaneZ;
            if (!TryFindLowerSupportPlaneZ(doc, slab, floors, safeOpt, topPlaneZ, out bottomPlaneZ)) return;

            strutHeight = Math.Max(0.0, topPlaneZ - bottomPlaneZ);
            if (strutHeight + 1e-9 < rules.SlabStrutJudgeHeightFt) return;

            int stages = CalculateSlabStrutStageCount(
                strutHeight,
                rules.SlabStrutStartHeightFt,
                rules.SlabStrutStageHeightFt,
                rules.SlabStrutMaxStageCount);
            if (stages <= 0) return;

            strutSoffitArea = rules.SlabIncludeBottom && rules.SlabStrutSoffitEnabled ? bottomArea : 0.0;
            strutStageCount = stages;
            strutStageArea = strutSoffitArea * stages;
            if (stageAreas != null && stages > 0 && stages < stageAreas.Length)
            {
                stageAreas[stages] = strutSoffitArea;
            }
            strutEdgeArea = rules.SlabIncludeSide && rules.SlabStrutEdgeEnabled ? sideArea : 0.0;
            strutEdgeStageArea = strutEdgeArea * stages;
            strutTopArea = rules.SlabStrutTopFormworkEnabled ? topArea : 0.0;
            strutTopStageArea = strutTopArea * stages;
        }

        private static bool TryGetSlabBottomPlaneZ(Element slab, Options opt, out double z)
        {
            z = 0.0;
            if (slab == null) return false;

            bool found = false;
            double maxBottomZ = double.MinValue;
            foreach (Solid solid in GetElementSolids(slab, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null || pf.FaceNormal == null || pf.FaceNormal.Z >= -0.5) continue;

                    double minZ;
                    double maxZ;
                    if (TryGetFaceZRange(pf, out minZ, out maxZ))
                    {
                        if (maxZ > maxBottomZ) maxBottomZ = maxZ;
                        found = true;
                    }
                    else
                    {
                        if (pf.Origin.Z > maxBottomZ) maxBottomZ = pf.Origin.Z;
                        found = true;
                    }
                }
            }

            if (found)
            {
                z = maxBottomZ;
                return true;
            }

            BoundingBoxXYZ box = slab.get_BoundingBox(null);
            if (box == null) return false;
            z = box.Min.Z;
            return true;
        }

        private static bool TryGetSlabTopPlaneZ(Element slab, Options opt, out double z)
        {
            z = 0.0;
            if (slab == null) return false;

            bool found = false;
            double maxTopZ = double.MinValue;
            foreach (Solid solid in GetElementSolids(slab, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null || pf.FaceNormal == null || pf.FaceNormal.Z <= 0.5) continue;

                    double minZ;
                    double maxZ;
                    if (TryGetFaceZRange(pf, out minZ, out maxZ))
                    {
                        if (maxZ > maxTopZ) maxTopZ = maxZ;
                        found = true;
                    }
                    else
                    {
                        if (pf.Origin.Z > maxTopZ) maxTopZ = pf.Origin.Z;
                        found = true;
                    }
                }
            }

            if (found)
            {
                z = maxTopZ;
                return true;
            }

            BoundingBoxXYZ box = slab.get_BoundingBox(null);
            if (box == null) return false;
            z = box.Max.Z;
            return true;
        }

        private static bool TryFindLowerSupportPlaneZ(
            Document doc,
            Element slab,
            IList<Element> floors,
            Options opt,
            double slabBottomZ,
            out double supportZ)
        {
            supportZ = 0.0;
            bool foundFloor = false;
            double bestFloorTopZ = double.MinValue;
            const double verticalTol = 0.05; // roughly 15 mm

            if (slab != null && floors != null)
            {
                foreach (Element floor in floors)
                {
                    if (floor == null || floor.Id == null) continue;
                    if (floor.Id.Value == slab.Id.Value) continue;

                    double topZ;
                    if (!TryGetSlabTopPlaneZ(floor, opt, out topZ)) continue;
                    if (topZ >= slabBottomZ - verticalTol) continue;
                    if (topZ <= bestFloorTopZ) continue;

                    bestFloorTopZ = topZ;
                    foundFloor = true;
                }
            }

            if (foundFloor)
            {
                supportZ = bestFloorTopZ;
                return true;
            }

            return TryFindNearestLowerLevelElevation(doc, slabBottomZ, out supportZ);
        }

        private static bool TryFindNearestLowerLevelElevation(Document doc, double topPlaneZ, out double elevation)
        {
            elevation = 0.0;
            if (doc == null) return false;

            const double verticalTol = 0.05; // roughly 15 mm
            Level bestLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(level => level != null && level.Elevation < topPlaneZ - verticalTol)
                .OrderByDescending(level => level.Elevation)
                .FirstOrDefault();

            if (bestLevel == null) return false;
            elevation = bestLevel.Elevation;
            return true;
        }

        private static int CalculateSlabStrutStageCount(double strutHeight, double startHeight, double stageHeight, int maxStageCount)
        {
            if (maxStageCount <= 0) return 0;
            if (strutHeight + 1e-9 < startHeight) return 0;
            if (stageHeight <= 1e-9) return Math.Min(1, maxStageCount);

            double extraHeight = Math.Max(0.0, strutHeight - startHeight);
            int stages = 1 + (int)Math.Floor((extraHeight + 1e-9) / stageHeight);
            if (stages < 1) stages = 1;
            if (stages > maxStageCount) stages = maxStageCount;
            return stages;
        }

        private static bool TryGetFaceZRange(Face face, out double minZ, out double maxZ)
        {
            minZ = double.MaxValue;
            maxZ = double.MinValue;
            bool found = false;
            if (face == null) return false;

            try
            {
                IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
                if (loops != null)
                {
                    foreach (CurveLoop loop in loops)
                    {
                        if (loop == null) continue;
                        foreach (Curve curve in loop)
                        {
                            if (curve == null) continue;

                            IList<XYZ> points = null;
                            try
                            {
                                points = curve.Tessellate();
                            }
                            catch
                            {
                                points = null;
                            }

                            if (points != null && points.Count > 0)
                            {
                                foreach (XYZ point in points)
                                {
                                    TrackVerticalRange(point, ref minZ, ref maxZ, ref found);
                                }
                            }
                            else
                            {
                                try
                                {
                                    TrackVerticalRange(curve.GetEndPoint(0), ref minZ, ref maxZ, ref found);
                                    TrackVerticalRange(curve.GetEndPoint(1), ref minZ, ref maxZ, ref found);
                                }
                                catch
                                {
                                    // Ignore non-bound curve edge data.
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                found = false;
                minZ = double.MaxValue;
                maxZ = double.MinValue;
            }

            if (found) return true;

            try
            {
                Mesh mesh = face.Triangulate();
                if (mesh == null) return false;
                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle tri = mesh.get_Triangle(i);
                    TrackVerticalRange(tri.get_Vertex(0), ref minZ, ref maxZ, ref found);
                    TrackVerticalRange(tri.get_Vertex(1), ref minZ, ref maxZ, ref found);
                    TrackVerticalRange(tri.get_Vertex(2), ref minZ, ref maxZ, ref found);
                }
            }
            catch
            {
                return false;
            }

            return found;
        }

        private static bool IsSlabSideFormworkFace(FaceSlab slab)
        {
            if (slab == null) return false;
            if (!string.Equals(slab.FaceType, "Side", StringComparison.OrdinalIgnoreCase)) return false;
            return slab.FaceNormal == null || Math.Abs(slab.FaceNormal.Z) < 0.1;
        }

        private static List<SlabOpeningMeasure> GetSlabOpeningMeasures(Element element, double thresholdFt2)
        {
            List<SlabOpeningMeasure> result = GetSlabOpeningMeasuresFromFaceLoops(element, thresholdFt2);
            if (result.Count > 0)
            {
                return result;
            }

            AddSlabOpeningMeasuresFromHostedInserts(element, thresholdFt2, result);
            return result;
        }

        private static List<SlabOpeningMeasure> GetSlabOpeningMeasuresFromFaceLoops(Element element, double thresholdFt2)
        {
            var result = new List<SlabOpeningMeasure>();
            if (element == null) return result;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            foreach (Solid solid in GetElementSolids(element, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace topFace = face as PlanarFace;
                    if (topFace == null || topFace.FaceNormal == null) continue;
                    if (topFace.FaceNormal.Z <= 0.1) continue;

                    AddSlabOpeningMeasuresFromTopFace(topFace, thresholdFt2, result);
                }
            }

            return result;
        }

        private static void AddSlabOpeningMeasuresFromTopFace(
            PlanarFace face,
            double thresholdFt2,
            List<SlabOpeningMeasure> result)
        {
            if (face == null || result == null) return;

            IList<CurveLoop> loops;
            try
            {
                loops = face.GetEdgesAsCurveLoops();
            }
            catch
            {
                return;
            }

            if (loops == null || loops.Count < 2) return;

            var loopMeasures = new List<SlabOpeningMeasure>();
            foreach (CurveLoop loop in loops)
            {
                List<XYZ> points = TessellateCurveLoop(loop);
                if (points.Count < 3) continue;

                double projectedArea = Math.Abs(ProjectedLoopAreaFt2(points));
                if (projectedArea <= 1e-9) continue;

                loopMeasures.Add(new SlabOpeningMeasure(points, projectedArea > thresholdFt2 + 1e-9, projectedArea));
            }

            if (loopMeasures.Count < 2) return;

            double outerArea = loopMeasures.Max(x => x.ProjectedAreaFt2);
            foreach (SlabOpeningMeasure measure in loopMeasures)
            {
                if (Math.Abs(measure.ProjectedAreaFt2 - outerArea) <= 1e-6) continue;
                result.Add(measure);
            }
        }

        private static List<XYZ> TessellateCurveLoop(CurveLoop loop)
        {
            var points = new List<XYZ>();
            if (loop == null) return points;

            foreach (Curve curve in loop)
            {
                if (curve == null) continue;

                IList<XYZ> tessellated;
                try
                {
                    tessellated = curve.Tessellate();
                }
                catch
                {
                    tessellated = null;
                }

                if (tessellated == null || tessellated.Count == 0) continue;

                foreach (XYZ point in tessellated)
                {
                    if (point == null) continue;
                    if (points.Count > 0 && DistanceXY(points[points.Count - 1], point) <= 1e-9) continue;
                    points.Add(point);
                }
            }

            if (points.Count > 1 && DistanceXY(points[0], points[points.Count - 1]) <= 1e-9)
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }

        private static double ProjectedLoopAreaFt2(IList<XYZ> points)
        {
            if (points == null || points.Count < 3) return 0.0;

            double area2 = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                if (a == null || b == null) continue;
                area2 += a.X * b.Y - b.X * a.Y;
            }

            return area2 * 0.5;
        }

        private static void AddSlabOpeningMeasuresFromHostedInserts(
            Element element,
            double thresholdFt2,
            List<SlabOpeningMeasure> result)
        {
            HostObject host = element as HostObject;
            if (host == null || host.Document == null || result == null) return;

            IList<ElementId> insertIds;
            try
            {
                insertIds = host.FindInserts(true, true, true, true);
            }
            catch
            {
                return;
            }

            if (insertIds == null || insertIds.Count == 0) return;

            foreach (ElementId id in insertIds)
            {
                Element insert = host.Document.GetElement(id);
                BoundingBoxXYZ box = insert?.get_BoundingBox(null);
                if (box == null) continue;

                double area = EstimateHorizontalProjectedAreaFt2(box);
                if (area <= 1e-9) continue;

                result.Add(new SlabOpeningMeasure(box, area > thresholdFt2 + 1e-9, area));
            }
        }

        private static double EstimateHorizontalProjectedAreaFt2(BoundingBoxXYZ box)
        {
            if (box == null) return 0.0;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (XYZ p in GetBoundingBoxCorners(box))
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            double width = maxX - minX;
            double depth = maxY - minY;
            if (width <= 1e-9 || depth <= 1e-9) return 0.0;
            return width * depth;
        }

        private static bool IsCandidateSlabOpeningSideSlab(FaceSlab slab)
        {
            if (slab == null) return false;
            if (!string.Equals(slab.FaceType, "Side", StringComparison.OrdinalIgnoreCase)) return false;
            if (slab.FaceNormal == null || slab.FacePoint == null) return false;
            return Math.Abs(slab.FaceNormal.Z) < 0.1;
        }

        private static SlabOpeningMeasure FindContainingSlabOpening(IList<SlabOpeningMeasure> openings, XYZ point)
        {
            if (openings == null || point == null) return null;

            foreach (SlabOpeningMeasure opening in openings)
            {
                if (opening == null) continue;

                if (opening.BoundaryPoints != null && opening.BoundaryPoints.Count >= 3)
                {
                    if (IsPointNearLoopXY(point, opening.BoundaryPoints, 0.08))
                    {
                        return opening;
                    }
                }
                else if (opening.Box != null && IsPointInsideBoxXY(opening.Box, point, 0.08))
                {
                    return opening;
                }
            }

            return null;
        }

        private static bool IsPointNearLoopXY(XYZ point, IList<XYZ> loopPoints, double tol)
        {
            if (point == null || loopPoints == null || loopPoints.Count < 2) return false;

            for (int i = 0; i < loopPoints.Count; i++)
            {
                XYZ a = loopPoints[i];
                XYZ b = loopPoints[(i + 1) % loopPoints.Count];
                if (DistancePointToSegmentXY(point, a, b) <= tol)
                {
                    return true;
                }
            }

            return false;
        }

        private static double DistancePointToSegmentXY(XYZ point, XYZ a, XYZ b)
        {
            if (point == null || a == null || b == null) return double.MaxValue;

            double ax = a.X;
            double ay = a.Y;
            double bx = b.X;
            double by = b.Y;
            double px = point.X;
            double py = point.Y;
            double dx = bx - ax;
            double dy = by - ay;
            double len2 = dx * dx + dy * dy;
            if (len2 <= 1e-12)
            {
                double da = px - ax;
                double db = py - ay;
                return Math.Sqrt(da * da + db * db);
            }

            double t = ((px - ax) * dx + (py - ay) * dy) / len2;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            double cx = ax + t * dx;
            double cy = ay + t * dy;
            double ex = px - cx;
            double ey = py - cy;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private static bool IsPointInsideBoxXY(BoundingBoxXYZ box, XYZ point, double tol)
        {
            if (box == null || point == null) return false;

            Transform inverse = null;
            try
            {
                inverse = (box.Transform ?? Transform.Identity).Inverse;
            }
            catch
            {
                inverse = Transform.Identity;
            }

            XYZ p = inverse.OfPoint(point);
            return p.X >= box.Min.X - tol && p.X <= box.Max.X + tol &&
                   p.Y >= box.Min.Y - tol && p.Y <= box.Max.Y + tol;
        }

        private static double IntersectAreaFromSlab(FaceSlab slab, Solid other)
        {
            if (slab.SlabSolid == null || other == null) return 0.0;

            try
            {
                Solid inter = BooleanOperationsUtils.ExecuteBooleanOperation(slab.SlabSolid, other, BooleanOperationsType.Intersect);
                if (inter == null || inter.Volume <= 1e-9) return 0.0;

                return inter.Volume / slab.Thickness;
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<FaceSlab> ClipFormworkSlabsByPriorityElements(
            IList<FaceSlab> slabs,
            IList<Element> priorityElements,
            Options options,
            BoundingBoxXYZ hostBox,
            ElementId hostId)
        {
            var result = new List<FaceSlab>();
            if (slabs == null || slabs.Count == 0) return result;
            if (priorityElements == null || priorityElements.Count == 0)
            {
                result.AddRange(slabs.Where(s => s != null));
                return result;
            }

            foreach (FaceSlab slab in slabs)
            {
                if (slab == null || slab.SlabSolid == null || slab.SlabSolid.Volume <= 1e-9)
                {
                    continue;
                }

                Solid clipped = slab.SlabSolid;
                bool changed = false;

                foreach (Element priorityElement in priorityElements)
                {
                    if (priorityElement == null || priorityElement.Id == null) continue;
                    if (hostId != null && priorityElement.Id.Value == hostId.Value) continue;

                    BoundingBoxXYZ priorityBox = priorityElement.get_BoundingBox(null);
                    if (hostBox != null && priorityBox != null && !BoundingBoxesIntersectOrTouch(hostBox, priorityBox, FormworkHalfThicknessFt * 4.0))
                    {
                        continue;
                    }

                    foreach (Solid prioritySolid in GetElementSolids(priorityElement, options))
                    {
                        if (prioritySolid == null || prioritySolid.Volume <= 1e-9) continue;

                        try
                        {
                            Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(clipped, prioritySolid, BooleanOperationsType.Intersect);
                            if (intersection == null || intersection.Volume <= 1e-9) continue;

                            Solid difference = BooleanOperationsUtils.ExecuteBooleanOperation(clipped, prioritySolid, BooleanOperationsType.Difference);
                            if (difference == null || difference.Volume <= 1e-9)
                            {
                                clipped = null;
                                changed = true;
                                break;
                            }

                            clipped = difference;
                            changed = true;
                        }
                        catch
                        {
                            // If a specific boolean operation fails, keep the current slab geometry.
                        }
                    }

                    if (clipped == null || clipped.Volume <= 1e-9) break;
                }

                if (clipped == null || clipped.Volume <= 1e-9) continue;

                if (!changed)
                {
                    result.Add(slab);
                    continue;
                }

                double remainingArea = slab.FaceArea;
                if (slab.Thickness > 1e-9)
                {
                    remainingArea = Math.Max(0.0, clipped.Volume / slab.Thickness);
                    if (slab.FaceArea > 1e-9)
                    {
                        remainingArea = Math.Min(slab.FaceArea, remainingArea);
                    }
                }

                if (remainingArea <= 1e-9) continue;

                result.Add(new FaceSlab(
                    clipped,
                    slab.Thickness,
                    slab.FaceType,
                    remainingArea,
                    slab.FaceNormal,
                    slab.FacePoint,
                    slab.FaceHeight));
            }

            return result;
        }

        private static bool BoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return true;
            return !(a.Max.X < b.Min.X || a.Min.X > b.Max.X ||
                     a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y ||
                     a.Max.Z < b.Min.Z || a.Min.Z > b.Max.Z);
        }

        private static List<Solid> GetElementSolids(Element element, Options opt)
        {
            var solids = new List<Solid>();
            if (element == null) return solids;

            GeometryElement ge = element.get_Geometry(opt);
            if (ge == null) return solids;

            foreach (GeometryObject go in ge)
            {
                CollectSolidsFromGeometry(go, Transform.Identity, solids);
            }

            return solids;
        }

        private static void CollectSolidsFromGeometry(GeometryObject go, Transform trf, List<Solid> solids)
        {
            if (go == null) return;

            if (go is Solid solid)
            {
                if (solid.Volume > 1e-9)
                {
                    Solid s = trf != null ? SolidUtils.CreateTransformed(solid, trf) : solid;
                    solids.Add(s);
                }
                return;
            }

            if (go is GeometryInstance inst)
            {
                Transform instTrf = trf.Multiply(inst.Transform);
                GeometryElement instGeom = inst.GetInstanceGeometry();
                if (instGeom != null)
                {
                    foreach (GeometryObject igo in instGeom)
                    {
                        CollectSolidsFromGeometry(igo, instTrf, solids);
                    }
                }
                return;
            }

            if (go is GeometryElement ge)
            {
                foreach (GeometryObject child in ge)
                {
                    CollectSolidsFromGeometry(child, trf, solids);
                }
            }
        }

        private static int ClearExistingFormworkShapes(Document doc)
        {
            if (doc == null) return 0;

            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(IsCbimFormworkShape)
                .Select(e => e.Id)
                .ToList();

            if (ids.Count > 0)
            {
                doc.Delete(ids);
            }

            return ids.Count;
        }

        private static bool IsCbimFormworkShape(Element element)
        {
            if (element == null) return false;
            Parameter pFlag = element.LookupParameter("CBIM.FWK.IsShape");
            if (pFlag != null && pFlag.StorageType == StorageType.Integer && pFlag.AsInteger() == 1)
            {
                return true;
            }

            Parameter pMarker = element.LookupParameter("CBIM.FormworkShape");
            if (pMarker != null && pMarker.StorageType == StorageType.String)
            {
                string val = pMarker.AsString();
                if (!string.IsNullOrWhiteSpace(val) && val.IndexOf("FormworkShape", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CreateFormworkShapes(Document doc, Element hostElement, string hostCategory, IList<FaceSlab> faceSlabs)
        {
            if (doc == null || hostElement == null || faceSlabs == null || faceSlabs.Count == 0) return;

            string hostId = hostElement.Id?.Value.ToString() ?? "";
            string levelName = GetElementLevelName(doc, hostElement);
            ElementId materialId = EnsureCategoryFormworkMaterial(doc, hostCategory);

            int idx = 0;
            foreach (FaceSlab slab in faceSlabs)
            {
                if (slab == null || slab.SlabSolid == null || slab.SlabSolid.Volume <= 1e-9) continue;

                DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_SpecialityEquipment));
                ds.ApplicationId = "CamboBIM.CBIM-QS";
                ds.ApplicationDataId = $"{hostId}:{hostCategory}:{idx}";
                ds.Name = $"CBIM_FWK_{hostCategory}_{hostId}_{idx + 1}";
                ds.SetShape(new List<GeometryObject> { slab.SlabSolid });
                ApplyMaterialToShape(ds, materialId);

                SetParamString(ds, "CBIM.FormworkShape", "CBIM.FormworkShape");
                SetParamYesNo(ds, "CBIM.FWK.IsShape", true);
                SetParamString(ds, "CBIM.FWK.Category", hostCategory);
                SetParamString(ds, "CBIM.FWK.ElementId", hostId);
                SetParamString(ds, "CBIM.FWK.FaceType", slab.FaceType);
                SetParamDouble(ds, "CBIM.FWK.FaceArea", slab.FaceArea);
                SetParamString(ds, "CBIM.FWK.Level", levelName);

                idx++;
            }
        }

        private static string GetElementLevelName(Document doc, Element element)
        {
            if (doc == null || element == null) return "";

            return ResolveStructuralPlanLevelName(doc, element, null);
        }

        private static bool SetUnifiedStructuralPlanParam(Document doc, Element element, IDictionary<ElementId, string> structuralPlanMap)
        {
            if (doc == null || element == null) return false;
            string levelName = ResolveStructuralPlanLevelName(doc, element, structuralPlanMap);
            bool changed = false;
            changed |= SetParamString(element, UnifiedLevelParamName, levelName);
            changed |= SetParamString(element, UnifiedBuildingLevelParamName, levelName);
            return changed;
        }

        private static string ResolveStructuralPlanLevelName(Document doc, Element element, IDictionary<ElementId, string> structuralPlanMap)
        {
            if (doc == null || element == null) return "";

            if (TryGetElementLevelId(doc, element, out ElementId levelId) &&
                levelId != ElementId.InvalidElementId)
            {
                Level level = doc.GetElement(levelId) as Level;
                if (level != null && !string.IsNullOrWhiteSpace(level.Name))
                {
                    return level.Name;
                }

                if (structuralPlanMap != null &&
                    structuralPlanMap.TryGetValue(levelId, out string planName) &&
                    !string.IsNullOrWhiteSpace(planName))
                {
                    return planName;
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
                        return level.Name;
                    }
                }
            }

            return "";
        }

        private static void ApplyMaterialToShape(Element element, ElementId materialId)
        {
            if (element == null || materialId == null || materialId == ElementId.InvalidElementId) return;

            Parameter p = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
            {
                p.Set(materialId);
                return;
            }

            Parameter pByName = element.LookupParameter("Material");
            if (pByName != null && !pByName.IsReadOnly && pByName.StorageType == StorageType.ElementId)
            {
                pByName.Set(materialId);
            }
        }

        private static ElementId EnsureCategoryFormworkMaterial(Document doc, string hostCategory)
        {
            if (doc == null) return ElementId.InvalidElementId;

            string suffix = (hostCategory ?? "General").Trim();
            if (suffix.Length == 0) suffix = "General";
            string materialName = "CBIM_FWK_" + suffix;

            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId newId = Material.Create(doc, materialName);
                material = doc.GetElement(newId) as Material;
            }

            if (material == null) return ElementId.InvalidElementId;

            Color targetColor = GetFormworkColorByCategory(hostCategory);
            try
            {
                if (!IsSameColor(material.Color, targetColor))
                {
                    material.Color = targetColor;
                }
            }
            catch
            {
                // ignore material color assignment failures
            }

            try
            {
                material.Transparency = 35;
            }
            catch
            {
                // ignore transparency assignment failures
            }

            return material.Id;
        }

        private static Color GetFormworkColorByCategory(string hostCategory)
        {
            string key = (hostCategory ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "wall":
                    return new Color(230, 117, 32); // orange
                case "beam":
                    return new Color(91, 155, 213); // blue
                case "column":
                    return new Color(112, 173, 71); // green
                case "floor":
                    return new Color(0, 176, 240); // cyan
                case "foundation":
                    return new Color(165, 165, 165); // gray
                case "stair":
                    return new Color(255, 192, 0); // amber
                case "kerb":
                    return new Color(146, 208, 80); // light green
                case "otherconcrete":
                case "other concrete":
                    return new Color(191, 144, 0); // ochre
                default:
                    return new Color(128, 100, 162); // purple
            }
        }

        private static bool IsSameColor(Color a, Color b)
        {
            if (a == null || b == null) return false;
            return a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue;
        }

        private static bool TryGetElementLevelId(Document doc, Element element, out ElementId levelId)
        {
            levelId = ElementId.InvalidElementId;
            if (doc == null || element == null) return false;

            // Category-specific native level parameters (locale-safe via BuiltInParameter name parsing).
            long catId = element.Category?.Id?.Value ?? 0;
            if (catId == (long)BuiltInCategory.OST_StructuralColumns)
            {
                if (TryGetLevelIdByBuiltInNames(element, out levelId,
                    "FAMILY_BASE_LEVEL_PARAM",
                    "SCHEDULE_BASE_LEVEL_PARAM"))
                {
                    return true;
                }
            }
            else if (catId == (long)BuiltInCategory.OST_StructuralFraming)
            {
                if (TryGetLevelIdByBuiltInNames(element, out levelId,
                    "INSTANCE_REFERENCE_LEVEL_PARAM",
                    "SCHEDULE_LEVEL_PARAM"))
                {
                    return true;
                }
            }
            else if (catId == (long)BuiltInCategory.OST_Walls)
            {
                if (TryGetLevelIdByBuiltInNames(element, out levelId,
                    "WALL_BASE_CONSTRAINT",
                    "SCHEDULE_BASE_LEVEL_PARAM"))
                {
                    return true;
                }
            }
            else if (catId == (long)BuiltInCategory.OST_Floors ||
                     catId == (long)BuiltInCategory.OST_Ceilings ||
                     catId == (long)BuiltInCategory.OST_StructuralFoundation)
            {
                if (TryGetLevelIdByBuiltInNames(element, out levelId,
                    "LEVEL_PARAM",
                    "SCHEDULE_LEVEL_PARAM"))
                {
                    return true;
                }
            }
            else if (catId == (long)BuiltInCategory.OST_Stairs)
            {
                if (TryGetLevelIdByBuiltInNames(element, out levelId,
                    "STAIRS_BASE_LEVEL_PARAM",
                    "LEVEL_PARAM"))
                {
                    return true;
                }
            }

            foreach (string name in new[] { "Base Constraint", "Base Level", "Reference Level", "Level" })
            {
                Parameter p = element.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        levelId = id;
                        return true;
                    }
                }
            }

            if (element is FamilyInstance fi && fi.LevelId != ElementId.InvalidElementId)
            {
                levelId = fi.LevelId;
                return true;
            }
            if (element is Wall wall && wall.LevelId != ElementId.InvalidElementId)
            {
                levelId = wall.LevelId;
                return true;
            }
            if (element is Floor floor && floor.LevelId != ElementId.InvalidElementId)
            {
                levelId = floor.LevelId;
                return true;
            }

            Parameter pSched = element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM) ??
                               element.get_Parameter(BuiltInParameter.LEVEL_PARAM);
            if (pSched != null && pSched.StorageType == StorageType.ElementId)
            {
                ElementId id = pSched.AsElementId();
                if (id != null && id != ElementId.InvalidElementId)
                {
                    levelId = id;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetLevelIdByBuiltInNames(Element element, out ElementId levelId, params string[] builtInNames)
        {
            levelId = ElementId.InvalidElementId;
            if (element == null || builtInNames == null) return false;

            foreach (string raw in builtInNames)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!Enum.TryParse(raw, out BuiltInParameter bip)) continue;

                Parameter p = element.get_Parameter(bip);
                if (p == null || p.StorageType != StorageType.ElementId) continue;

                ElementId id = p.AsElementId();
                if (id == null || id == ElementId.InvalidElementId) continue;
                levelId = id;
                return true;
            }

            return false;
        }

        private static Dictionary<ElementId, string> BuildStructuralPlanNameMap(Document doc)
        {
            var map = new Dictionary<ElementId, string>();
            if (doc == null) return map;

            var plans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v != null && !v.IsTemplate && v.GenLevel != null)
                .ToList();

            foreach (ViewPlan plan in plans.Where(v => v.ViewType == ViewType.EngineeringPlan))
            {
                ElementId levelId = plan.GenLevel.Id;
                if (levelId == null || levelId == ElementId.InvalidElementId) continue;
                if (!map.ContainsKey(levelId) && !string.IsNullOrWhiteSpace(plan.Name))
                {
                    map[levelId] = plan.Name.Trim();
                }
            }

            return map;
        }

        private sealed class FoundationSideStageRule
        {
            public bool CalculateInStages { get; set; }
            public bool CalculateAreaStages { get; set; }
            public bool UseLengthOnlyBelowCondition { get; set; }
            public double LengthConditionFt { get; set; }
            public double ThereafterFt { get; set; }
            public List<double> SegmentThresholdsFt { get; } = new List<double>();
        }

        private sealed class QsMeasurementRuntimeRules
        {
            public bool FoundationIncludeSide { get; private set; }
            public bool FoundationIncludeTop { get; private set; }
            public bool FoundationDeductFoundation { get; private set; }
            public bool FoundationDeductBeam { get; private set; }
            public bool FoundationDeductColumn { get; private set; }
            public bool FoundationDeductWall { get; private set; }
            public bool FoundationDeductFloor { get; private set; }
            public bool FoundationDeductGeneric { get; private set; }
            public Dictionary<string, FoundationSideStageRule> FoundationSideRules { get; private set; }
            public bool FoundationSideSettingsEnabled
            {
                get
                {
                    return FoundationSideRules != null &&
                           FoundationSideRules.Values.Any(rule => rule != null && rule.CalculateInStages);
                }
            }

            public FoundationSideStageRule GetFoundationSideRule(string key)
            {
                if (FoundationSideRules == null || FoundationSideRules.Count == 0)
                {
                    return null;
                }

                string normalized = string.IsNullOrWhiteSpace(key) ? "PAD" : key.Trim().ToUpperInvariant();
                FoundationSideStageRule rule;
                if (FoundationSideRules.TryGetValue(normalized, out rule))
                {
                    return rule;
                }

                return FoundationSideRules.TryGetValue("PAD", out rule) ? rule : FoundationSideRules.Values.FirstOrDefault();
            }

            public bool ColumnIncludeSide { get; private set; }
            public bool ColumnIncludeTopBottom { get; private set; }
            public bool ColumnDeductFoundation { get; private set; }
            public bool ColumnDeductBeam { get; private set; }
            public bool ColumnBeamGreaterOrEqual { get; private set; }
            public bool ColumnDeductColumn { get; private set; }
            public bool ColumnDeductFloor { get; private set; }
            public bool ColumnDeductWall { get; private set; }
            public bool ColumnDeductGeneric { get; private set; }
            public bool ColumnStrutEnabled { get; private set; }
            public bool ColumnStrutStaged { get; private set; }
            public double ColumnStrutJudgeHeightFt { get; private set; }
            public double ColumnStrutStartHeightFt { get; private set; }
            public double ColumnStrutStageHeightFt { get; private set; }
            public int ColumnStrutMaxStageCount { get; private set; }

            public bool BeamIncludeSide { get; private set; }
            public bool BeamIncludeBottom { get; private set; }
            public bool BeamIncludeTop { get; private set; }
            public bool BeamDeductFoundation { get; private set; }
            public bool BeamDeductBeam { get; private set; }
            public bool BeamDeductColumn { get; private set; }
            public bool BeamDeductWall { get; private set; }
            public bool BeamDeductFloor { get; private set; }
            public bool BeamDeductGeneric { get; private set; }
            public bool BeamStrutEnabled { get; private set; }
            public bool BeamStrutStaged { get; private set; }
            public double BeamStrutJudgeHeightFt { get; private set; }
            public double BeamStrutStartHeightFt { get; private set; }
            public double BeamStrutStageHeightFt { get; private set; }
            public int BeamStrutMaxStageCount { get; private set; }

            public bool WallIncludeSide { get; private set; }
            public bool WallExcludeJoinedEndCaps { get; private set; }
            public bool WallIncludeOpeningBottom { get; private set; }
            public bool WallOpeningSideRuleEnabled { get; private set; }
            public double WallOpeningSideThresholdFt2 { get; private set; }
            public bool WallDeductFoundation { get; private set; }
            public bool WallDeductBeam { get; private set; }
            public bool WallDeductColumn { get; private set; }
            public bool WallDeductWall { get; private set; }
            public bool WallDeductFloor { get; private set; }
            public bool WallDeductGeneric { get; private set; }
            public bool WallEdgeSegmentationEnabled { get; private set; }
            public bool WallEdgeMeasureAreaStages { get; private set; }
            public double WallEdgeLengthConditionFt { get; private set; }
            public double WallEdgeBreakFirstThresholdFt { get; private set; }
            public double WallEdgeBreakSecondThresholdFt { get; private set; }
            public double WallEdgeBreakThirdThresholdFt { get; private set; }

            public bool SlabIncludeSide { get; private set; }
            public bool SlabIncludeBottom { get; private set; }
            public bool SlabDeductFoundation { get; private set; }
            public bool SlabDeductBeam { get; private set; }
            public bool SlabDeductColumn { get; private set; }
            public bool SlabDeductWall { get; private set; }
            public bool SlabDeductSlab { get; private set; }
            public bool SlabDeductStair { get; private set; }
            public bool SlabDeductGeneric { get; private set; }
            public bool SlabTopSlopeEnabled { get; private set; }
            public double SlabTopSlopeDegrees { get; private set; }
            public bool SlabOpeningSideRuleEnabled { get; private set; }
            public double SlabOpeningSideThresholdFt2 { get; private set; }
            public bool SlabEdgeSegmentationEnabled { get; private set; }
            public double SlabEdgeBreakFirstThresholdFt { get; private set; }
            public double SlabEdgeBreakSecondThresholdFt { get; private set; }
            public double SlabEdgeBreakThirdThresholdFt { get; private set; }
            public bool SlabStrutSoffitEnabled { get; private set; }
            public double SlabStrutJudgeHeightFt { get; private set; }
            public double SlabStrutStartHeightFt { get; private set; }
            public double SlabStrutStageHeightFt { get; private set; }
            public int SlabStrutMaxStageCount { get; private set; }
            public bool SlabStrutEdgeEnabled { get; private set; }
            public bool SlabStrutTopFormworkEnabled { get; private set; }

            public bool StairPaintingEnabled { get; private set; }
            public bool StairIncludeBottom { get; private set; }
            public bool StairIncludeTop { get; private set; }
            public bool StairDeductBeam { get; private set; }
            public bool StairDeductOther { get; private set; }

            public bool KerbIncludeSide { get; private set; }
            public bool KerbIncludeTop { get; private set; }
            public bool KerbDeductWall { get; private set; }
            public bool KerbCalculationEnabled { get { return KerbIncludeSide || KerbIncludeTop; } }

            public bool LintelIncludeSide { get; private set; }
            public bool LintelIncludeBottom { get; private set; }
            public bool LintelDeductWall { get; private set; }
            public bool LintelCalculationEnabled { get { return LintelIncludeSide || LintelIncludeBottom; } }

            public bool DropPanelSoffitEnabled { get; private set; }
            public bool DropPanelDeductSlab { get; private set; }
            public bool DropPanelStrutEnabled { get; private set; }
            public bool DropPanelStrutStaged { get; private set; }
            public double DropPanelStrutJudgeHeightFt { get; private set; }
            public bool DropPanelCalculationEnabled { get { return DropPanelSoffitEnabled; } }

            public bool EaveIncludeBottom { get; private set; }
            public bool EaveIncludeEdge { get; private set; }
            public bool EaveCalculationEnabled { get { return EaveIncludeBottom || EaveIncludeEdge; } }

            public bool OtherConcreteIncludeSide { get; private set; }
            public bool OtherConcreteIncludeBottom { get; private set; }
            public bool OtherConcreteDeductStructure { get; private set; }
            public bool OtherConcreteCalculationEnabled { get { return OtherConcreteIncludeSide || OtherConcreteIncludeBottom; } }

            public bool WallFinishEnabled { get; private set; }
            public bool WallFinishOpeningEnabled { get; private set; }
            public double WallFinishOpeningThresholdFt2 { get; private set; }
            public bool WallFinishReturnEnabled { get; private set; }
            public bool WallFinishRoomEnabled { get; private set; }
            public bool CeilingFinishEnabled { get; private set; }
            public bool CeilingFinishOpeningEnabled { get; private set; }
            public double CeilingFinishOpeningThresholdFt2 { get; private set; }
            public bool CeilingFinishReturnEnabled { get; private set; }
            public bool CeilingFinishRoomEnabled { get; private set; }
            public bool SuspendedCeilingEnabled { get; private set; }
            public bool SuspendedCeilingOpeningEnabled { get; private set; }
            public double SuspendedCeilingOpeningThresholdFt2 { get; private set; }
            public bool SuspendedCeilingReturnEnabled { get; private set; }
            public bool SuspendedCeilingRoomEnabled { get; private set; }
            public bool FloorFinishEnabled { get; private set; }
            public bool FloorFinishOpeningEnabled { get; private set; }
            public double FloorFinishOpeningThresholdFt2 { get; private set; }
            public bool FloorFinishReturnEnabled { get; private set; }
            public bool FloorFinishRoomEnabled { get; private set; }
            public bool WaterproofEnabled { get; private set; }
            public bool WaterproofOpeningEnabled { get; private set; }
            public double WaterproofOpeningThresholdFt2 { get; private set; }
            public bool WaterproofReturnEnabled { get; private set; }
            public bool WaterproofRoomEnabled { get; private set; }
            public bool WaterproofUpturnEnabled { get; private set; }
            public double WaterproofUpturnHeightFt { get; private set; }
            public bool FinishCalculationEnabled
            {
                get
                {
                    return WallFinishEnabled ||
                           CeilingFinishEnabled ||
                           SuspendedCeilingEnabled ||
                           FloorFinishEnabled ||
                           WaterproofEnabled;
                }
            }

            public bool IsFinishKindEnabled(QsFinishMeasurementKind kind)
            {
                switch (kind)
                {
                    case QsFinishMeasurementKind.WallFinish:
                        return WallFinishEnabled;
                    case QsFinishMeasurementKind.CeilingFinish:
                        return CeilingFinishEnabled;
                    case QsFinishMeasurementKind.SuspendedCeiling:
                        return SuspendedCeilingEnabled;
                    case QsFinishMeasurementKind.FloorFinish:
                        return FloorFinishEnabled;
                    case QsFinishMeasurementKind.Waterproof:
                        return WaterproofEnabled;
                    default:
                        return false;
                }
            }

            public bool IsFinishRoomGroupingEnabled(QsFinishMeasurementKind kind)
            {
                switch (kind)
                {
                    case QsFinishMeasurementKind.WallFinish:
                        return WallFinishRoomEnabled;
                    case QsFinishMeasurementKind.CeilingFinish:
                        return CeilingFinishRoomEnabled;
                    case QsFinishMeasurementKind.SuspendedCeiling:
                        return SuspendedCeilingRoomEnabled;
                    case QsFinishMeasurementKind.FloorFinish:
                        return FloorFinishRoomEnabled;
                    case QsFinishMeasurementKind.Waterproof:
                        return WaterproofRoomEnabled;
                    default:
                        return false;
                }
            }

            public static QsMeasurementRuntimeRules FromRequest(CadToModelRequest request)
            {
                QsMeasurementSettingsProfile profile = request?.QsMeasurementSettingsProfile;
                if (profile == null)
                {
                    profile = QsMeasurementSettingsProfile.CreateDefault();
                }
                else
                {
                    profile.Normalize();
                }

                bool slabOtherMaster = Use(profile, "SLAB.DEDUCT.OTHER", request?.QsFloorSubtractOthers ?? true);
                bool foundationIncludeTop =
                    Use(profile, "FOUN.TOP", request?.QsFoundationIncludeTop ?? true) &&
                    UseMeasurementRule(request?.QsMeasurementRulesProfile, true, "PAD.QTY.TOP", "RAFT.QTY.TOP");
                double[] slabEdgeThresholds = GetSlabEdgeThresholdsFt(profile);
                double[] wallEdgeThresholds = GetWallEdgeThresholdsFt(profile);

                QsMeasurementRuntimeRules rules = new QsMeasurementRuntimeRules
                {
                    FoundationIncludeSide = Use(profile, "FOUN.SIDE", true),
                    FoundationIncludeTop = foundationIncludeTop,
                    FoundationDeductFoundation = Use(profile, "FOUN.DEDUCT.FOUN", true),
                    FoundationDeductBeam = Use(profile, "FOUN.DEDUCT.BEAM", true),
                    FoundationDeductColumn = Use(profile, "FOUN.DEDUCT.COL", true),
                    FoundationDeductWall = Use(profile, "FOUN.DEDUCT.WALL", true),
                    FoundationDeductFloor = Use(profile, "FOUN.DEDUCT.FLOOR", true),
                    FoundationDeductGeneric = Use(profile, "FOUN.DEDUCT.OTHER", true),
                    FoundationSideRules = BuildFoundationSideRules(profile),

                    ColumnIncludeSide = Use(profile, "COL.SIDE", true),
                    ColumnIncludeTopBottom = Use(profile, "COL.TOPBOTTOM", false),
                    ColumnDeductFoundation = Use(profile, "COL.DEDUCT.FOUN", true),
                    ColumnDeductBeam = Use(profile, "COL.DEDUCT.BEAM", request?.QsColumnSubtractBeam ?? false),
                    ColumnBeamGreaterOrEqual = Use(profile, "COL.DEDUCT.BEAM.SIZE", request?.QsColumnSubtractBeamGreaterOrEqual ?? false),
                    ColumnDeductColumn = Use(profile, "COL.DEDUCT.COL", true),
                    ColumnDeductFloor = Use(profile, "COL.DEDUCT.FLOOR", true),
                    ColumnDeductWall = Use(profile, "COL.DEDUCT.WALL", true),
                    ColumnDeductGeneric = Use(profile, "COL.DEDUCT.OTHER", true),
                    ColumnStrutEnabled = !ChoiceValueStarts(profile, "COL.STRUT.METHOD", "3"),
                    ColumnStrutStaged = ChoiceValueStarts(profile, "COL.STRUT.METHOD", "1", "2"),
                    ColumnStrutJudgeHeightFt = MetersToInternalLength(profile.GetDouble("COL.STRUT.JUDGE.START", 0.0)),
                    ColumnStrutStartHeightFt = MetersToInternalLength(profile.GetDouble("COL.STRUT.CALC.START", 0.0)),
                    ColumnStrutStageHeightFt = MetersToInternalLength(profile.GetDouble("COL.STRUT.STAGE.HEIGHT", 1.5)),
                    ColumnStrutMaxStageCount = ClampStageCount(profile.GetDouble("COL.STRUT.MAX.STAGES", 10.0)),

                    BeamIncludeSide = Use(profile, "BEAM.SIDE", true),
                    BeamIncludeBottom = Use(profile, "BEAM.BOTTOM", request?.QsBeamIncludeBottom ?? true),
                    BeamIncludeTop = Use(profile, "BEAM.TOP", false),
                    BeamDeductFoundation = Use(profile, "BEAM.DEDUCT.FOUN", true),
                    BeamDeductBeam = Use(profile, "BEAM.DEDUCT.BEAM", true),
                    BeamDeductColumn = Use(profile, "BEAM.DEDUCT.COL", true),
                    BeamDeductWall = Use(profile, "BEAM.DEDUCT.WALL", true),
                    BeamDeductFloor = Use(profile, "BEAM.DEDUCT.FLOOR", true),
                    BeamDeductGeneric = Use(profile, "BEAM.DEDUCT.OTHER", true),
                    BeamStrutEnabled = UseUnlessValueStarts(profile, "BEAM.STRUT.METHOD", true, "2 Not calculate strutting high"),
                    BeamStrutStaged = profile.GetChoiceContains("BEAM.STRUT.METHOD", "1 Calculate strutting high in stages", true),
                    BeamStrutJudgeHeightFt = MetersToInternalLength(profile.GetDouble("BEAM.STRUT.JUDGE.START", 1.5)),
                    BeamStrutStartHeightFt = MetersToInternalLength(profile.GetDouble("BEAM.STRUT.CALC.START", 1.5)),
                    BeamStrutStageHeightFt = MetersToInternalLength(profile.GetDouble("BEAM.STRUT.STAGE.HEIGHT", 1.5)),
                    BeamStrutMaxStageCount = ClampStageCount(profile.GetDouble("BEAM.STRUT.MAX.STAGES", 10.0)),

                    WallIncludeSide = Use(profile, "WALL.SIDE", true),
                    WallExcludeJoinedEndCaps = profile.GetChoiceEnabled("WALL.ENDCAP", true, "Include end caps"),
                    WallIncludeOpeningBottom = Use(profile, "WALL.OPENING.BOTTOM", request?.QsWallIncludeOpeningBottom ?? true),
                    WallOpeningSideRuleEnabled = profile.IsRuleEnabled("WALL.OPENING.SIDE", true),
                    WallOpeningSideThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("WALL.OPENING.SIDE", 5.0)),
                    WallDeductFoundation = Use(profile, "WALL.DEDUCT.FOUN", true),
                    WallDeductBeam = Use(profile, "WALL.DEDUCT.BEAM", true),
                    WallDeductColumn = Use(profile, "WALL.DEDUCT.COL", true),
                    WallDeductWall = Use(profile, "WALL.DEDUCT.WALL", true),
                    WallDeductFloor = Use(profile, "WALL.DEDUCT.FLOOR", true),
                    WallDeductGeneric = Use(profile, "WALL.DEDUCT.OTHER", true),
                    WallEdgeSegmentationEnabled = profile.GetChoiceEnabled("WALL.EDGE.METHOD", true, "0 Not calculate in stages: calculate by area") && profile.IsRuleEnabled("WALL.EDGE.SEGMENT", true),
                    WallEdgeMeasureAreaStages = ChoiceValueStarts(profile, "WALL.EDGE.METHOD", "2"),
                    WallEdgeLengthConditionFt = MetersToInternalLength(profile.GetDouble("WALL.EDGE.CONDITION", 1.0)),
                    WallEdgeBreakFirstThresholdFt = wallEdgeThresholds[0],
                    WallEdgeBreakSecondThresholdFt = wallEdgeThresholds[1],
                    WallEdgeBreakThirdThresholdFt = wallEdgeThresholds[2],

                    SlabIncludeSide = Use(profile, "SLAB.SIDE", true),
                    SlabIncludeBottom = Use(profile, "SLAB.BOTTOM", request?.QsFloorIncludeBottom ?? true),
                    SlabDeductFoundation = Use(profile, "SLAB.DEDUCT.FOUN", request?.QsFloorSubtractFoundation ?? true),
                    SlabDeductBeam = Use(profile, "SLAB.DEDUCT.BEAM", request?.QsFloorSubtractBeam ?? true),
                    SlabDeductColumn = slabOtherMaster && Use(profile, "SLAB.DEDUCT.COL", true),
                    SlabDeductWall = slabOtherMaster && Use(profile, "SLAB.DEDUCT.WALL", true),
                    SlabDeductSlab = slabOtherMaster && Use(profile, "SLAB.DEDUCT.SLAB", true),
                    SlabDeductStair = slabOtherMaster && Use(profile, "SLAB.DEDUCT.STAIR", true),
                    SlabDeductGeneric = slabOtherMaster,
                    SlabTopSlopeEnabled = profile.IsRuleEnabled("SLAB.TOP.SLOPE", false),
                    SlabTopSlopeDegrees = ClampSlopeDegrees(profile.GetDouble("SLAB.TOP.SLOPE", 15.0)),
                    SlabOpeningSideRuleEnabled = profile.IsRuleEnabled("SLAB.OPENING.SIDE", true),
                    SlabOpeningSideThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("SLAB.OPENING.SIDE", 5.0)),
                    SlabEdgeSegmentationEnabled = profile.GetChoiceEnabled("SLAB.EDGE.METHOD", true, "0 Not calculate in stages: calculate by area") && profile.IsRuleEnabled("SLAB.EDGE.SEGMENT", true),
                    SlabEdgeBreakFirstThresholdFt = slabEdgeThresholds[0],
                    SlabEdgeBreakSecondThresholdFt = slabEdgeThresholds[1],
                    SlabEdgeBreakThirdThresholdFt = slabEdgeThresholds[2],
                    SlabStrutSoffitEnabled = UseUnlessValueStarts(profile, "SLAB.STRUT.SOFFIT", true, "2 Not calculate strutting high"),
                    SlabStrutJudgeHeightFt = MetersToInternalLength(profile.GetDouble("SLAB.STRUT.JUDGE", 1.5)),
                    SlabStrutStartHeightFt = MetersToInternalLength(profile.GetDouble("SLAB.STRUT.START", 1.5)),
                    SlabStrutStageHeightFt = MetersToInternalLength(profile.GetDouble("SLAB.STRUT.STAGEHEIGHT", 1.5)),
                    SlabStrutMaxStageCount = ClampStageCount(profile.GetDouble("SLAB.STRUT.MAXSTAGE", 10.0)),
                    SlabStrutEdgeEnabled = UseUnlessValueStarts(profile, "SLAB.STRUT.EDGE", false, "2 Not calculate strutting high"),
                    SlabStrutTopFormworkEnabled = UseUnlessValueStarts(profile, "SLAB.STRUT.TOPFWK", false, "2 Not calculate strutting high"),

                    StairPaintingEnabled = Use(profile, "STAIR.PAINTING", true) && profile.IsRuleEnabled("STAIR.SIDE.METHOD", true),
                    StairIncludeBottom = Use(profile, "STAIR.BOTTOM", false),
                    StairIncludeTop = Use(profile, "STAIR.TOP", request?.QsStairIncludeTop ?? false),
                    StairDeductBeam = Use(profile, "STAIR.DEDUCT.BEAM", request?.QsStairSubtractBeam ?? true),
                    StairDeductOther = Use(profile, "STAIR.DEDUCT.OTHER", request?.QsStairSubtractOthers ?? true),

                    KerbIncludeSide = Use(profile, "KERB.SIDE", true),
                    KerbIncludeTop = Use(profile, "KERB.TOP", false),
                    KerbDeductWall = Use(profile, "KERB.DEDUCT.WALL", true),

                    LintelIncludeSide = Use(profile, "LINTEL.SIDE", true),
                    LintelIncludeBottom = Use(profile, "LINTEL.BOTTOM", true),
                    LintelDeductWall = Use(profile, "LINTEL.DEDUCT.WALL", true),

                    DropPanelSoffitEnabled = Use(profile, "DROP.SOFFIT", true),
                    DropPanelDeductSlab = Use(profile, "DROP.DEDUCT.SLAB", true),
                    DropPanelStrutEnabled = !ChoiceValueStarts(profile, "DROP.STRUT.METHOD", "2"),
                    DropPanelStrutStaged = ChoiceValueStarts(profile, "DROP.STRUT.METHOD", "1"),
                    DropPanelStrutJudgeHeightFt = MetersToInternalLength(profile.GetDouble("DROP.STRUT.JUDGE.START", 3.5)),

                    EaveIncludeBottom = Use(profile, "EAVE.BOTTOM", true),
                    EaveIncludeEdge = Use(profile, "EAVE.EDGE", true),

                    OtherConcreteIncludeSide = Use(profile, "OTHER.SIDE", true),
                    OtherConcreteIncludeBottom = Use(profile, "OTHER.BOTTOM", true),
                    OtherConcreteDeductStructure = Use(profile, "OTHER.DEDUCT.STRUCTURE", true),

                    WallFinishEnabled = Use(profile, "WF.AREA", true),
                    WallFinishOpeningEnabled = Use(profile, "WF.OPENING", true),
                    WallFinishOpeningThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("WF.OPENING.MIN", 0.5)),
                    WallFinishReturnEnabled = Use(profile, "WF.RETURN", false),
                    WallFinishRoomEnabled = profile.GetChoiceContains("WF.ROOM", "Room", true),
                    CeilingFinishEnabled = Use(profile, "CF.AREA", true),
                    CeilingFinishOpeningEnabled = Use(profile, "CF.OPENING", true),
                    CeilingFinishOpeningThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("CF.OPENING.MIN", 0.5)),
                    CeilingFinishReturnEnabled = Use(profile, "CF.RETURN", false),
                    CeilingFinishRoomEnabled = profile.GetChoiceContains("CF.ROOM", "Room", true),
                    SuspendedCeilingEnabled = Use(profile, "SC.AREA", true),
                    SuspendedCeilingOpeningEnabled = Use(profile, "SC.OPENING", true),
                    SuspendedCeilingOpeningThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("SC.OPENING.MIN", 0.5)),
                    SuspendedCeilingReturnEnabled = Use(profile, "SC.RETURN", false),
                    SuspendedCeilingRoomEnabled = profile.GetChoiceContains("SC.ROOM", "Room", true),
                    FloorFinishEnabled = Use(profile, "FF.AREA", true),
                    FloorFinishOpeningEnabled = Use(profile, "FF.OPENING", true),
                    FloorFinishOpeningThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("FF.OPENING.MIN", 0.5)),
                    FloorFinishReturnEnabled = Use(profile, "FF.RETURN", false),
                    FloorFinishRoomEnabled = profile.GetChoiceContains("FF.ROOM", "Room", true),
                    WaterproofEnabled = Use(profile, "WP.AREA", true),
                    WaterproofOpeningEnabled = Use(profile, "WP.OPENING", true),
                    WaterproofOpeningThresholdFt2 = SquareMetersToInternalArea(profile.GetDouble("WP.OPENING.MIN", 0.5)),
                    WaterproofReturnEnabled = Use(profile, "WP.RETURN", false),
                    WaterproofRoomEnabled = profile.GetChoiceContains("WP.ROOM", "Room", true),
                    WaterproofUpturnEnabled = profile.IsRuleEnabled("WP.UPTURN", true),
                    WaterproofUpturnHeightFt = MetersToInternalLength(profile.GetDouble("WP.UPTURN", 0.3))
                };

                rules.ColumnBeamGreaterOrEqual = rules.ColumnDeductBeam && rules.ColumnBeamGreaterOrEqual;
                rules.ColumnStrutStaged = rules.ColumnStrutEnabled && rules.ColumnStrutStaged;
                rules.BeamStrutStaged = rules.BeamStrutEnabled && rules.BeamStrutStaged;
                rules.DropPanelStrutStaged = rules.DropPanelStrutEnabled && rules.DropPanelStrutStaged;
                return rules;
            }

            private static bool Use(QsMeasurementSettingsProfile profile, string code, bool fallback)
            {
                return profile == null ? fallback : profile.GetBoolean(code, fallback);
            }

            private static bool UseMeasurementRule(QsMeasurementRulesProfile profile, bool fallback, params string[] codes)
            {
                if (profile == null || codes == null || codes.Length == 0)
                {
                    return fallback;
                }

                profile.Normalize();
                bool found = false;
                foreach (string code in codes)
                {
                    QsMeasurementRuleRow row = profile.FindRule(code);
                    if (row == null)
                    {
                        continue;
                    }

                    found = true;
                    if (!row.IsEnabled)
                    {
                        return false;
                    }
                }

                return found ? true : fallback;
            }

            private static bool UseUnlessValueStarts(QsMeasurementSettingsProfile profile, string code, bool fallback, params string[] disabledPrefixes)
            {
                QsMeasurementSettingRow row = profile?.FindRule(code);
                if (row == null)
                {
                    return fallback;
                }

                if (!row.IsEnabled)
                {
                    return false;
                }

                string text = (row.Value ?? "").Trim();
                if (text.Length == 0)
                {
                    return fallback;
                }

                foreach (string prefix in disabledPrefixes ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(prefix) &&
                        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool ChoiceValueStarts(QsMeasurementSettingsProfile profile, string code, params string[] prefixes)
            {
                QsMeasurementSettingRow row = profile?.FindRule(code);
                if (row == null || !row.IsEnabled)
                {
                    return false;
                }

                string text = (row.Value ?? "").Trim();
                if (text.Length == 0)
                {
                    return false;
                }

                foreach (string prefix in prefixes ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(prefix) &&
                        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static Dictionary<string, FoundationSideStageRule> BuildFoundationSideRules(QsMeasurementSettingsProfile profile)
            {
                var result = new Dictionary<string, FoundationSideStageRule>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in new[] { "BLINDING", "PAD", "PILECAP", "RAFT", "GROUNDBEAM", "STRIP" })
                {
                    result[key] = BuildFoundationSideRule(profile, key);
                }

                return result;
            }

            private static FoundationSideStageRule BuildFoundationSideRule(QsMeasurementSettingsProfile profile, string key)
            {
                string prefix = "FOUN.SIDE.SETTING." + key + ".";
                QsMeasurementSettingRow methodRow = profile?.FindRule(prefix + "METHOD");
                string method = (methodRow?.Value ?? QsMeasurementSettingsProfile.FoundationSideDefaultMethodValue()).Trim();
                bool methodEnabled = methodRow == null || methodRow.IsEnabled;

                var rule = new FoundationSideStageRule
                {
                    CalculateInStages = methodEnabled && !method.StartsWith("0", StringComparison.OrdinalIgnoreCase),
                    CalculateAreaStages = method.StartsWith("2", StringComparison.OrdinalIgnoreCase),
                    UseLengthOnlyBelowCondition = !method.StartsWith("2", StringComparison.OrdinalIgnoreCase),
                    LengthConditionFt = MetersToInternalLength(profile?.GetDouble(prefix + "CONDITION", 1.0) ?? 1.0),
                    ThereafterFt = MetersToInternalLength(profile?.GetDouble(prefix + "THEREAFTER", 0.5) ?? 0.5)
                };

                for (int segment = 1; segment <= FoundationSidePersistedStageLimit; segment++)
                {
                    QsMeasurementSettingRow segmentRow = profile?.FindRule(prefix + "SEG." + segment.ToString(CultureInfo.InvariantCulture));
                    if (segmentRow == null || !segmentRow.IsEnabled) continue;

                    double meters;
                    if (QsMeasurementSettingsProfile.TryParseMeasurementDouble(segmentRow.Value, out meters) && meters > 1e-9)
                    {
                        rule.SegmentThresholdsFt.Add(MetersToInternalLength(meters));
                    }
                }

                if (rule.SegmentThresholdsFt.Count == 0)
                {
                    rule.SegmentThresholdsFt.Add(MetersToInternalLength(0.25));
                    rule.SegmentThresholdsFt.Add(MetersToInternalLength(0.50));
                    rule.SegmentThresholdsFt.Add(MetersToInternalLength(1.00));
                }

                rule.SegmentThresholdsFt.Sort();
                for (int i = rule.SegmentThresholdsFt.Count - 1; i > 0; i--)
                {
                    if (Math.Abs(rule.SegmentThresholdsFt[i] - rule.SegmentThresholdsFt[i - 1]) <= 1e-9)
                    {
                        rule.SegmentThresholdsFt.RemoveAt(i);
                    }
                }

                return rule;
            }

            private static double ClampSlopeDegrees(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return 15.0;
                if (value < 0.0) return 0.0;
                if (value > 89.0) return 89.0;
                return value;
            }

            private static int ClampStageCount(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return 10;
                int count = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                if (count < 0) return 0;
                if (count > BeamStrutPersistedStageLimit) return BeamStrutPersistedStageLimit;
                return count;
            }

            private static double[] GetSlabEdgeThresholdsFt(QsMeasurementSettingsProfile profile)
            {
                return GetEdgeThresholdsFt(profile, "SLAB.EDGE.SEGMENT");
            }

            private static double[] GetWallEdgeThresholdsFt(QsMeasurementSettingsProfile profile)
            {
                return GetEdgeThresholdsFt(profile, "WALL.EDGE.SEGMENT");
            }

            private static double[] GetEdgeThresholdsFt(QsMeasurementSettingsProfile profile, string codePrefix)
            {
                double[] defaultsMeters = { 0.25, 0.50, 1.00 };
                double[] thresholdsMeters = { defaultsMeters[0], defaultsMeters[1], defaultsMeters[2] };

                List<double> values = ExtractEdgeSegmentSettings(profile, codePrefix);
                if (values.Count < 3)
                {
                    QsMeasurementSettingRow row = profile?.FindRule(codePrefix);
                    values = ExtractPositiveNumbers(row?.Value);
                }

                if (values.Count >= 3)
                {
                    values.Sort();
                    List<double> unique = new List<double>();
                    foreach (double value in values)
                    {
                        if (value <= 1e-9) continue;
                        if (unique.Count == 0 || Math.Abs(unique[unique.Count - 1] - value) > 1e-6)
                        {
                            unique.Add(value);
                        }
                    }

                    if (unique.Count >= 3)
                    {
                        thresholdsMeters[0] = unique[0];
                        thresholdsMeters[1] = unique[1];
                        thresholdsMeters[2] = unique[2];
                    }
                }

                return new[]
                {
                    MetersToInternalLength(thresholdsMeters[0]),
                    MetersToInternalLength(thresholdsMeters[1]),
                    MetersToInternalLength(thresholdsMeters[2])
                };
            }

            private static List<double> ExtractEdgeSegmentSettings(QsMeasurementSettingsProfile profile, string codePrefix)
            {
                List<double> values = new List<double>();
                if (profile == null)
                {
                    return values;
                }

                for (int segment = 1; segment <= 12; segment++)
                {
                    QsMeasurementSettingRow row = profile.FindRule(codePrefix + "." + segment.ToString(CultureInfo.InvariantCulture));
                    if (row == null || !row.IsEnabled)
                    {
                        continue;
                    }

                    double value;
                    if (QsMeasurementSettingsProfile.TryParseMeasurementDouble(row.Value, out value) && value > 0.0)
                    {
                        values.Add(value);
                    }
                }

                return values;
            }

            private static List<double> ExtractPositiveNumbers(string text)
            {
                List<double> values = new List<double>();
                if (string.IsNullOrWhiteSpace(text)) return values;

                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"[-+]?\d+(?:[\.,]\d+)?"))
                {
                    string token = (match.Value ?? "").Replace(',', '.');
                    double value;
                    if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0.0)
                    {
                        values.Add(value);
                    }
                }

                return values;
            }

            private static double MetersToInternalLength(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0) value = 0.0;
                try
                {
                    return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
                }
                catch
                {
                    return value / 0.3048;
                }
            }

            private static double SquareMetersToInternalArea(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0) value = 0.0;
                try
                {
                    return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);
                }
                catch
                {
                    return value / 0.09290304;
                }
            }
        }

        private sealed class FaceSlab
        {
            public Solid SlabSolid { get; }
            public double Thickness { get; }
            public string FaceType { get; }
            public double FaceArea { get; }
            public XYZ FaceNormal { get; }
            public XYZ FacePoint { get; }
            public double FaceHeight { get; }

            public FaceSlab(
                Solid slabSolid,
                double thickness,
                string faceType = "Unknown",
                double faceArea = 0.0,
                XYZ faceNormal = null,
                XYZ facePoint = null,
                double faceHeight = 0.0)
            {
                SlabSolid = slabSolid;
                Thickness = thickness;
                FaceType = faceType ?? "Unknown";
                FaceArea = faceArea;
                FaceNormal = faceNormal;
                FacePoint = facePoint;
                FaceHeight = faceHeight;
            }
        }

        private sealed class WallOpeningMeasure
        {
            public BoundingBoxXYZ Box { get; }
            public bool IncludeSide { get; }

            public WallOpeningMeasure(BoundingBoxXYZ box, bool includeSide)
            {
                Box = box;
                IncludeSide = includeSide;
            }
        }

        private sealed class SlabOpeningMeasure
        {
            public BoundingBoxXYZ Box { get; }
            public List<XYZ> BoundaryPoints { get; }
            public bool IncludeSide { get; }
            public double ProjectedAreaFt2 { get; }

            public SlabOpeningMeasure(BoundingBoxXYZ box, bool includeSide, double projectedAreaFt2)
            {
                Box = box;
                BoundaryPoints = null;
                IncludeSide = includeSide;
                ProjectedAreaFt2 = projectedAreaFt2;
            }

            public SlabOpeningMeasure(List<XYZ> boundaryPoints, bool includeSide, double projectedAreaFt2)
            {
                Box = null;
                BoundaryPoints = boundaryPoints ?? new List<XYZ>();
                IncludeSide = includeSide;
                ProjectedAreaFt2 = projectedAreaFt2;
            }
        }

        private enum QsFinishMeasurementKind
        {
            Unknown,
            WallFinish,
            CeilingFinish,
            SuspendedCeiling,
            FloorFinish,
            Waterproof
        }

        private sealed class QsFinishMeasurementCandidate
        {
            public Element Element { get; }
            public QsFinishMeasurementKind Kind { get; }
            public string BoqLabel { get; }
            public string ProgressTitle { get; }

            public QsFinishMeasurementCandidate(Element element, QsFinishMeasurementKind kind)
            {
                Element = element;
                Kind = kind;
                BoqLabel = GetFinishBoqLabel(kind);
                ProgressTitle = string.IsNullOrWhiteSpace(BoqLabel) ? "Finish" : BoqLabel;
            }
        }

        private sealed class FinishRoomIdentity
        {
            public static readonly FinishRoomIdentity Empty = new FinishRoomIdentity("", "", "");

            public string Label { get; }
            public string Number { get; }
            public string Name { get; }

            public FinishRoomIdentity(string label, string number, string name)
            {
                Label = label ?? "";
                Number = number ?? "";
                Name = name ?? "";
            }
        }

        private static double GetElementArea(Element element)
        {
            double area = GetParamDouble(element, BuiltInParameter.HOST_AREA_COMPUTED);
            if (area > 1e-9) return area;

            string[] names = { "Area", "Net Area", "Finish Area" };
            return GetParamDouble(element, names);
        }

        private static double GetKerbLength(Element element)
        {
            double modelLength = GetElementLength(element);
            if (modelLength > 1e-9) return modelLength;

            double topPerimeter = EstimateTopFacePerimeter(element);
            if (topPerimeter > 1e-9) return topPerimeter;

            BoundingBoxXYZ box = element?.get_BoundingBox(null);
            if (box == null) return 0.0;

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            if (dx <= 1e-9 && dy <= 1e-9) return 0.0;
            return 2.0 * (dx + dy);
        }

        private static double EstimateTopFacePerimeter(Element element)
        {
            if (element == null) return 0.0;

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            double bestZ = double.MinValue;
            double bestPerimeter = 0.0;
            foreach (Solid solid in GetElementSolids(element, opt))
            {
                if (solid == null || solid.Faces.Size == 0) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace pf = face as PlanarFace;
                    if (pf == null || pf.FaceNormal == null || pf.FaceNormal.Z <= 0.9) continue;

                    double perimeter = GetLargestCurveLoopLength(pf.GetEdgesAsCurveLoops());
                    if (perimeter <= 1e-9) continue;

                    double z = pf.Origin?.Z ?? 0.0;
                    if (z > bestZ + 1e-6 || (Math.Abs(z - bestZ) <= 1e-6 && perimeter > bestPerimeter))
                    {
                        bestZ = z;
                        bestPerimeter = perimeter;
                    }
                }
            }

            return bestPerimeter;
        }

        private static double GetLargestCurveLoopLength(IList<CurveLoop> loops)
        {
            if (loops == null || loops.Count == 0) return 0.0;

            double best = 0.0;
            foreach (CurveLoop loop in loops)
            {
                if (loop == null) continue;

                double length = 0.0;
                foreach (Curve curve in loop)
                {
                    if (curve == null) continue;
                    length += Math.Max(0.0, curve.Length);
                }

                if (length > best) best = length;
            }

            return best;
        }

        private static double GetElementLength(Element element)
        {
            double length = GetParamDouble(element, BuiltInParameter.CURVE_ELEM_LENGTH);
            if (length > 1e-9) return length;

            if (element.Location is LocationCurve lc && lc.Curve != null)
            {
                return lc.Curve.Length;
            }

            return 0.0;
        }

        private static double GetElementVerticalHeight(Element element)
        {
            BoundingBoxXYZ box = element?.get_BoundingBox(null);
            if (box == null) return 0.0;
            return Math.Max(0.0, box.Max.Z - box.Min.Z);
        }

        private static double GetStairStepCount(Element element)
        {
            double value = GetParamDouble(element, new[]
            {
                "Actual Number of Risers",
                "Number of Risers",
                "Riser Number",
                "Riser Count",
                "Number of Treads",
                "Tread Number",
                "Tread Count",
                "Steps",
                "Step Count"
            });
            if (value > 1e-9) return Math.Round(value, MidpointRounding.AwayFromZero);

            string[] names =
            {
                "Actual Number of Risers",
                "Number of Risers",
                "Riser Number",
                "Riser Count",
                "Number of Treads",
                "Tread Number",
                "Tread Count",
                "Steps",
                "Step Count"
            };
            foreach (string name in names)
            {
                Parameter p = element?.LookupParameter(name);
                if (p == null) continue;

                if (p.StorageType == StorageType.Integer)
                {
                    int count = p.AsInteger();
                    if (count > 0) return count;
                }

                string text = (p.AsValueString() ?? p.AsString() ?? "").Trim();
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0.0)
                {
                    return Math.Round(parsed, MidpointRounding.AwayFromZero);
                }
            }

            return 0.0;
        }

        private static double GetWallFormworkWidth(Element element)
        {
            Wall wall = element as Wall;
            if (wall != null && wall.Width > 1e-9)
            {
                return wall.Width;
            }

            double width = GetParamDouble(element, new[] { "Width", "Thickness", "Wall Thickness", "W" });
            if (width > 1e-9)
            {
                return width;
            }

            ElementType type = element?.Document?.GetElement(element.GetTypeId()) as ElementType;
            width = GetParamDouble(type, new[] { "Width", "Thickness", "Wall Thickness", "W" });
            if (width > 1e-9)
            {
                return width;
            }

            BoundingBoxXYZ box = element?.get_BoundingBox(null);
            if (box == null) return 0.0;

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double length = GetElementLength(element);
            if (length > 1e-9)
            {
                double shorter = Math.Min(dx, dy);
                double longer = Math.Max(dx, dy);
                if (Math.Abs(longer - length) < Math.Abs(shorter - length))
                {
                    return shorter;
                }
            }

            return Math.Min(dx, dy);
        }

        private static double GetElementVolume(Element element)
        {
            double volume = GetParamDouble(element, BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (volume > 1e-9) return volume;

            string[] names = { "Volume", "Vol" };
            return GetParamDouble(element, names);
        }

        private static double GetTypeWidth(ElementType type)
        {
            string[] names = { "b", "B", "Width", "BF", "Beam Width" };
            return GetParamDouble(type, names);
        }

        private static double GetTypeDepth(ElementType type)
        {
            string[] names = { "h", "H", "Depth", "Height", "Beam Depth", "Beam Height" };
            return GetParamDouble(type, names);
        }

        private static double GetElementSectionMaxSize(Element element)
        {
            if (element == null) return 0.0;

            if (element is FamilyInstance fi && fi.Symbol != null)
            {
                ElementType type = fi.Symbol;
                double w = GetTypeWidth(type);
                if (w <= 1e-9)
                {
                    w = GetParamDouble(type, new[] { "Diameter", "D", "d", "Column Width", "Section Width" });
                }

                double d = GetTypeDepth(type);
                if (d <= 1e-9)
                {
                    d = GetParamDouble(type, new[] { "Diameter", "D", "d", "Column Depth", "Section Height" });
                }

                double s = Math.Max(w, d);
                if (s > 1e-9) return s;
            }

            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb != null)
            {
                double dx = Math.Abs(bb.Max.X - bb.Min.X);
                double dy = Math.Abs(bb.Max.Y - bb.Min.Y);
                double s = Math.Max(dx, dy);
                if (s > 1e-9) return s;
            }

            return 0.0;
        }

        private static double GetParamDouble(Element element, BuiltInParameter bip)
        {
            Parameter p = element?.get_Parameter(bip);
            if (p == null || p.StorageType != StorageType.Double) return 0.0;
            return p.AsDouble();
        }

        private static double GetParamDouble(Element element, string[] names)
        {
            if (element == null || names == null) return 0.0;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                Parameter p = element.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    return p.AsDouble();
                }
            }

            return 0.0;
        }

        private static bool SetParamDouble(Element element, string name, double value)
        {
            Parameter p = element?.LookupParameter(name);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
            p.Set(value);
            return true;
        }

        private static bool SetParamString(Element element, string name, string value)
        {
            Parameter p = element?.LookupParameter(name);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
            p.Set(value ?? "");
            return true;
        }

        private static bool SetParamYesNo(Element element, string name, bool value)
        {
            Parameter p = element?.LookupParameter(name);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) return false;
            p.Set(value ? 1 : 0);
            return true;
        }

        private sealed class QsSharedParamSpec
        {
            public string Name { get; }
            public ForgeTypeId TypeId { get; }
            public ForgeTypeId ParamGroupTypeId { get; }
            public IList<BuiltInCategory> TargetCategories { get; }

            public QsSharedParamSpec(string name, ForgeTypeId typeId, ForgeTypeId groupTypeId, params BuiltInCategory[] targetCategories)
            {
                Name = name;
                TypeId = typeId;
                ParamGroupTypeId = groupTypeId ?? GroupTypeId.Data;
                TargetCategories = (targetCategories ?? Array.Empty<BuiltInCategory>()).ToList();
            }
        }
    }
}
