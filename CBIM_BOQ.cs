using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal static class CBIM_BOQ
    {
        private const string LevelParamName = "CBIM_BuildingLevel";
        private const string StructuralPlanParamName = "CBIM_StructuralPlan";
        private const string StructureTypeParamName = "CBIM_StructureType";
        private const string SoilExcavationLabel = "Soil Excavation";
        private const string SoilBackfilledLabel = "Soil Backfilled";
        private const string KerbLabel = "Kerb";
        private const string OtherConcreteLabel = "Other Concrete";
        private const string LintelLabel = "Lintel";
        private const string DropPanelLabel = "Drop Panel";
        private const string EaveLabel = "Eave";
        private const string WallFinishLabel = "Wall Finish";
        private const string CeilingFinishLabel = "Ceiling Finish";
        private const string SuspendedCeilingLabel = "Suspended Ceiling";
        private const string FloorFinishLabel = "Floor Finish";
        private const string WaterproofLabel = "Waterproof";
        private const string SlabOpeningLabel = "Slab Opening";
        private const int KerbOrder = 7;
        private const int OtherConcreteOrder = 8;
        private const int LintelOrder = 9;
        private const int DropPanelOrder = 10;
        private const int EaveOrder = 11;
        private const int WallFinishOrder = 12;
        private const int CeilingFinishOrder = 13;
        private const int SuspendedCeilingOrder = 14;
        private const int FloorFinishOrder = 15;
        private const int WaterproofOrder = 16;
        private const int SoilExcavationOrder = 17;
        private const int SoilBackfilledOrder = 18;
        private const int SlabOpeningOrder = 6;
        private static readonly string[] StructureTypeParameterAliases =
        {
            StructureTypeParamName,
            "Structure Type",
            "Element Type",
            "TypeName",
            "Type Name"
        };

        private enum QsValueUnitKind
        {
            Area,
            Length,
            Volume,
            Number
        }

        private static readonly (BuiltInCategory Category, string Label, int Order)[] CategoryMap =
        {
            (BuiltInCategory.OST_StructuralFoundation, "Foundation", 1),
            (BuiltInCategory.OST_StructuralColumns, "Structural Column", 2),
            (BuiltInCategory.OST_Walls, "Wall", 3),
            (BuiltInCategory.OST_StructuralFraming, "Structural Framing", 4),
            (BuiltInCategory.OST_Floors, "Floor", 5),
            (BuiltInCategory.OST_Stairs, "Stair", 6)
        };

        public static void BuildTable(UIDocument uidoc, Document doc, CadToModelRequest request, CamboBIMWindow window)
        {
            if (uidoc == null || doc == null || request == null) return;

            QsScope scope = request.BoqScope;
            List<(Element Element, string Label, int Order)> source = CollectSourceElements(uidoc, doc, scope);
            List<BoqTableRow> rows = AggregateRows(doc, source, request);
            AppendSlabOpeningRows(doc, source, rows);
            AppendSoilRows(uidoc, doc, scope, request, rows);
            rows = rows
                .OrderBy(r => r.StructureOrder)
                .ThenBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Room, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            window?.UpdateBoqRows(rows);
            window?.ShowStatus($"BOQ table loaded: {rows.Count} grouped row(s), {source.Count} element(s).");
        }

        private static List<(Element Element, string Label, int Order)> CollectSourceElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            var rows = new List<(Element Element, string Label, int Order)>();
            List<Element> wallElements = CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Walls).ToList();
            List<Element> floorElements = CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Floors).ToList();
            List<Element> ceilingElements = CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_Ceilings).ToList();
            List<Element> genericElements = CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_GenericModel).ToList();
            HashSet<long> measuredSpecialIds = AddAuxiliaryConcreteRows(rows, floorElements, genericElements);
            AddExtendedStructureRows(rows, measuredSpecialIds, floorElements, genericElements, CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_StructuralFraming).ToList());
            AddFinishRows(rows, measuredSpecialIds, wallElements, floorElements, ceilingElements, genericElements);

            foreach (var map in CategoryMap)
            {
                IEnumerable<Element> elements;
                if (map.Category == BuiltInCategory.OST_Walls)
                {
                    elements = wallElements;
                }
                else if (map.Category == BuiltInCategory.OST_Floors)
                {
                    elements = floorElements;
                }
                else
                {
                    elements = CollectElementsByCategory(uidoc, doc, scope, map.Category);
                }

                foreach (Element element in elements)
                {
                    if (element?.Id != null && measuredSpecialIds.Contains(element.Id.Value))
                    {
                        continue;
                    }

                    rows.Add((element, map.Label, map.Order));
                }
            }

            return rows;
        }

        private static void AddFinishRows(
            List<(Element Element, string Label, int Order)> rows,
            HashSet<long> addedIds,
            IList<Element> wallElements,
            IList<Element> floorElements,
            IList<Element> ceilingElements,
            IList<Element> genericElements)
        {
            AddFinishRowsFromSource(rows, wallElements, addedIds);
            AddFinishRowsFromSource(rows, floorElements, addedIds);
            AddFinishRowsFromSource(rows, ceilingElements, addedIds);
            AddFinishRowsFromSource(rows, genericElements, addedIds);
        }

        private static void AddFinishRowsFromSource(
            List<(Element Element, string Label, int Order)> rows,
            IList<Element> source,
            HashSet<long> addedIds)
        {
            if (rows == null || source == null || addedIds == null) return;

            foreach (Element element in source)
            {
                if (element?.Id == null || addedIds.Contains(element.Id.Value)) continue;

                string label = CBIM_QS.GetFinishBoqLabel(element);
                if (string.IsNullOrWhiteSpace(label)) continue;

                rows.Add((element, label, GetFinishOrder(label)));
                addedIds.Add(element.Id.Value);
            }
        }

        private static int GetFinishOrder(string label)
        {
            if (string.Equals(label, WallFinishLabel, StringComparison.OrdinalIgnoreCase)) return WallFinishOrder;
            if (string.Equals(label, CeilingFinishLabel, StringComparison.OrdinalIgnoreCase)) return CeilingFinishOrder;
            if (string.Equals(label, SuspendedCeilingLabel, StringComparison.OrdinalIgnoreCase)) return SuspendedCeilingOrder;
            if (string.Equals(label, FloorFinishLabel, StringComparison.OrdinalIgnoreCase)) return FloorFinishOrder;
            if (string.Equals(label, WaterproofLabel, StringComparison.OrdinalIgnoreCase)) return WaterproofOrder;
            return WaterproofOrder;
        }

        private static HashSet<long> AddAuxiliaryConcreteRows(
            List<(Element Element, string Label, int Order)> rows,
            IList<Element> floorElements,
            IList<Element> genericElements)
        {
            var addedIds = new HashSet<long>();
            AddAuxiliaryConcreteRowsFromSource(rows, floorElements, addedIds);
            AddAuxiliaryConcreteRowsFromSource(rows, genericElements, addedIds);
            return addedIds;
        }

        private static void AddExtendedStructureRows(
            List<(Element Element, string Label, int Order)> rows,
            HashSet<long> addedIds,
            IList<Element> floorElements,
            IList<Element> genericElements,
            IList<Element> framingElements)
        {
            AddExtendedStructureRowsFromSource(rows, framingElements, addedIds);
            AddExtendedStructureRowsFromSource(rows, floorElements, addedIds);
            AddExtendedStructureRowsFromSource(rows, genericElements, addedIds);
        }

        private static void AddExtendedStructureRowsFromSource(
            List<(Element Element, string Label, int Order)> rows,
            IList<Element> source,
            HashSet<long> addedIds)
        {
            if (rows == null || source == null || addedIds == null) return;

            foreach (Element element in source)
            {
                if (element?.Id == null || addedIds.Contains(element.Id.Value)) continue;

                string label = CBIM_QS.GetExtendedStructureBoqLabel(element);
                if (string.IsNullOrWhiteSpace(label)) continue;

                rows.Add((element, label, GetExtendedStructureOrder(label)));
                addedIds.Add(element.Id.Value);
            }
        }

        private static int GetExtendedStructureOrder(string label)
        {
            if (string.Equals(label, LintelLabel, StringComparison.OrdinalIgnoreCase)) return LintelOrder;
            if (string.Equals(label, DropPanelLabel, StringComparison.OrdinalIgnoreCase)) return DropPanelOrder;
            if (string.Equals(label, EaveLabel, StringComparison.OrdinalIgnoreCase)) return EaveOrder;
            return EaveOrder;
        }

        private static void AddAuxiliaryConcreteRowsFromSource(
            List<(Element Element, string Label, int Order)> rows,
            IList<Element> source,
            HashSet<long> addedIds)
        {
            if (rows == null || source == null || addedIds == null) return;

            foreach (Element element in source)
            {
                if (element?.Id == null || addedIds.Contains(element.Id.Value)) continue;

                string label = CBIM_QS.GetAuxiliaryConcreteBoqLabel(element);
                if (string.IsNullOrWhiteSpace(label)) continue;

                int order = string.Equals(label, KerbLabel, StringComparison.OrdinalIgnoreCase)
                    ? KerbOrder
                    : OtherConcreteOrder;
                rows.Add((element, label, order));
                addedIds.Add(element.Id.Value);
            }
        }

        private static void AppendSoilRows(
            UIDocument uidoc,
            Document doc,
            QsScope scope,
            CadToModelRequest request,
            List<BoqTableRow> rows)
        {
            if (doc == null || request == null || rows == null)
            {
                return;
            }

            bool includeSoilExcavation = request.QsIncludeSoilExcavationInBoq;
            bool includeSoilBackfilled = request.QsIncludeSoilBackfilledInBoq;
            if (!includeSoilExcavation && !includeSoilBackfilled)
            {
                return;
            }

            List<Element> soilElements = CollectSoilExcavationElements(uidoc, doc, scope);
            if (soilElements.Count == 0)
            {
                return;
            }

            Options opt = CreateSolidReadOptions();
            List<Solid> soilSolids = soilElements
                .SelectMany(e => GetElementSolids(e, opt))
                .Where(s => s != null && s.Volume > 1e-9)
                .ToList();

            if (soilSolids.Count == 0)
            {
                return;
            }

            double soilExcavationFt3 = ComputeUnionVolumeFt3(soilSolids, out _, out _);
            if (soilExcavationFt3 <= 1e-9)
            {
                return;
            }

            string levelLabel = ResolveDerivedSoilLevelLabel(doc, soilElements);
            int sourceCount = soilElements.Count;
            double soilExcavationM3 = ToCubicMeters(soilExcavationFt3);

            if (includeSoilExcavation)
            {
                rows.Add(new BoqTableRow
                {
                    StructureElement = SoilExcavationLabel,
                    BuildingLevel = levelLabel,
                    TypeName = "CamboBIM DirectShape (Union Net)",
                    Quantity = sourceCount,
                    TotalVolumeM3 = soilExcavationM3,
                    TotalFormworkAreaM2 = 0.0,
                    QsRuleCode = "SOIL.EXCAVATION",
                    QsFormula = "Volume = union volume of soil excavation solids",
                    QsBreakdown = "Union=" + FormatBoqAuditVolume(soilExcavationM3) + " m3",
                    StructureOrder = SoilExcavationOrder
                });
            }

            if (includeSoilBackfilled)
            {
                double deductedStructureFt3 = 0.0;
                if (request.QsSoilBackfilledSubtractStructures)
                {
                    deductedStructureFt3 = ComputeSoilBackfillStructuralDeductionFt3(uidoc, doc, scope, soilElements, soilSolids, opt);
                }

                double soilBackfilledFt3 = Math.Max(0.0, soilExcavationFt3 - Math.Max(0.0, deductedStructureFt3));
                double deductedStructureM3 = ToCubicMeters(deductedStructureFt3);
                double soilBackfilledM3 = ToCubicMeters(soilBackfilledFt3);
                rows.Add(new BoqTableRow
                {
                    StructureElement = SoilBackfilledLabel,
                    BuildingLevel = levelLabel,
                    TypeName = request.QsSoilBackfilledSubtractStructures
                        ? "Soil Excavation - Intersecting Structures"
                        : "Soil Excavation (No Structure Deduction)",
                    Quantity = sourceCount,
                    TotalVolumeM3 = soilBackfilledM3,
                    TotalFormworkAreaM2 = 0.0,
                    QsRuleCode = request.QsSoilBackfilledSubtractStructures
                        ? "SOIL.BACKFILL;SOIL.BACKFILL.DEDUCT.STRUCTURE"
                        : "SOIL.BACKFILL",
                    QsFormula = request.QsSoilBackfilledSubtractStructures
                        ? "Net = Soil Excavation - Intersecting Structures"
                        : "Net = Soil Excavation",
                    QsBreakdown = request.QsSoilBackfilledSubtractStructures
                        ? "Excavation=" + FormatBoqAuditVolume(soilExcavationM3) + " m3; StructureDeduct=-" + FormatBoqAuditVolume(deductedStructureM3) + " m3; Net=" + FormatBoqAuditVolume(soilBackfilledM3) + " m3"
                        : "Excavation=" + FormatBoqAuditVolume(soilExcavationM3) + " m3; Net=" + FormatBoqAuditVolume(soilBackfilledM3) + " m3",
                    StructureOrder = SoilBackfilledOrder
                });
            }
        }

        private static IEnumerable<Element> CollectElementsByCategory(UIDocument uidoc, Document doc, QsScope scope, BuiltInCategory category)
        {
            if (doc == null) return Enumerable.Empty<Element>();

            if (scope == QsScope.CurrentSelection)
            {
                var selected = new List<Element>();
                ICollection<ElementId> ids = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();
                foreach (ElementId id in ids)
                {
                    Element element = doc.GetElement(id);
                    if (element == null || element.Category == null || element is ElementType) continue;
                    if (element.Category.Id.Value != (long)category) continue;
                    selected.Add(element);
                }

                return selected;
            }

            if (scope == QsScope.CurrentView && doc.ActiveView != null)
            {
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }

            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToElements();
        }

        private static List<BoqTableRow> AggregateRows(Document doc, List<(Element Element, string Label, int Order)> source, CadToModelRequest request)
        {
            var map = new Dictionary<string, BoqTableRow>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, bool> finishRoomRules = BuildFinishRoomRuleMap(request);

            foreach ((Element element, string label, int order) in source)
            {
                if (element == null) continue;

                string level = ResolveBuildingLevel(doc, element);
                if (string.IsNullOrWhiteSpace(level)) level = "Unassigned";

                string typeName = GetTypeName(doc, element);
                if (string.IsNullOrWhiteSpace(typeName)) typeName = "(No Type)";

                string room = ResolveBoqRoom(element, label, finishRoomRules);
                double volumeM3 = ToCubicMeters(GetParamDouble(element, BuiltInParameter.HOST_VOLUME_COMPUTED, "Volume", "Vol"));
                double formworkM2 = ToSquareMeters(GetParamDouble(element, "CBIM_FormworkArea"));
                string ruleCode = GetParamString(element, "CBIM_QsRuleCode");
                string formula = GetParamString(element, "CBIM_QsFormula");
                string breakdown = GetParamString(element, "CBIM_QsBreakdown");

                string key = label + "|" + level + "|" + room + "|" + typeName;
                if (!map.TryGetValue(key, out BoqTableRow row))
                {
                    row = new BoqTableRow
                    {
                        StructureElement = label,
                        BuildingLevel = level,
                        Room = room,
                        TypeName = typeName,
                        Quantity = 0,
                        TotalVolumeM3 = 0.0,
                        TotalFormworkAreaM2 = 0.0,
                        QsRuleCode = "",
                        QsFormula = "",
                        QsBreakdown = "",
                        StructureOrder = order
                    };
                    map[key] = row;
                }

                row.Quantity += 1;
                row.TotalVolumeM3 += volumeM3;
                row.TotalFormworkAreaM2 += formworkM2;
                row.QsRuleCode = MergeAuditText(row.QsRuleCode, ruleCode, 360);
                row.QsFormula = MergeAuditText(row.QsFormula, formula, 420);
                row.QsBreakdown = MergeAuditText(row.QsBreakdown, breakdown, 1000);
                AccumulateQsValues(row, element, volumeM3, formworkM2);
            }

            return map.Values
                .OrderBy(r => r.StructureOrder)
                .ThenBy(r => r.BuildingLevel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Room, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AppendSlabOpeningRows(
            Document doc,
            IEnumerable<(Element Element, string Label, int Order)> source,
            IList<BoqTableRow> rows)
        {
            if (doc == null || source == null || rows == null) return;

            var map = new Dictionary<string, BoqTableRow>(StringComparer.OrdinalIgnoreCase);
            var areaTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var girthTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach ((Element element, string label, int _) in source)
            {
                if (element == null || !string.Equals(label, "Floor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double count = GetParamDouble(element, "FWK.Floor.Opening.Count");
                if (count <= 1e-9) continue;

                string level = ResolveBuildingLevel(doc, element);
                if (string.IsNullOrWhiteSpace(level)) level = "Unassigned";

                double areaM2 = ToSquareMeters(GetParamDouble(element, "FWK.Floor.Opening.Area"));
                double girthM = ToMeters(GetParamDouble(element, "FWK.Floor.Opening.Girth"));
                string key = level;
                if (!map.TryGetValue(key, out BoqTableRow row))
                {
                    row = new BoqTableRow
                    {
                        StructureElement = SlabOpeningLabel,
                        BuildingLevel = level,
                        TypeName = "Opening in slab",
                        Quantity = 0,
                        TotalVolumeM3 = 0.0,
                        TotalFormworkAreaM2 = 0.0,
                        QsRuleCode = "SLAB.OPENING",
                        QsFormula = "Area and girth measured from slab internal openings",
                        StructureOrder = SlabOpeningOrder
                    };
                    map[key] = row;
                    areaTotals[key] = 0.0;
                    girthTotals[key] = 0.0;
                }

                row.Quantity += Math.Max(0, (int)Math.Round(count, MidpointRounding.AwayFromZero));
                areaTotals[key] += areaM2;
                girthTotals[key] += girthM;
                AccumulateQsValue(row, "FWK.Floor.Opening.Count", count);
                AccumulateQsValue(row, "FWK.Floor.Opening.Area", areaM2);
                AccumulateQsValue(row, "FWK.Floor.Opening.Girth", girthM);
            }

            foreach (KeyValuePair<string, BoqTableRow> pair in map)
            {
                BoqTableRow row = pair.Value;
                row.QsBreakdown =
                    "Area=" + FormatBoqAuditVolume(areaTotals[pair.Key]) + " m2; Girth=" +
                    FormatBoqAuditVolume(girthTotals[pair.Key]) + " m";
                rows.Add(row);
            }
        }

        private static string ResolveBoqRoom(Element element, string label, IReadOnlyDictionary<string, bool> finishRoomRules)
        {
            if (!ShouldGroupBoqByFinishRoom(label, finishRoomRules))
            {
                return "";
            }

            string room = GetParamString(element, "FIN.Room");
            return string.IsNullOrWhiteSpace(room) ? "Unassigned Room" : room.Trim();
        }

        private static bool ShouldGroupBoqByFinishRoom(string label, IReadOnlyDictionary<string, bool> finishRoomRules)
        {
            string code = GetFinishRoomRuleCode(label);
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            return finishRoomRules != null &&
                   finishRoomRules.TryGetValue(code, out bool enabled) &&
                   enabled;
        }

        private static Dictionary<string, bool> BuildFinishRoomRuleMap(CadToModelRequest request)
        {
            QsMeasurementSettingsProfile profile = request?.QsMeasurementSettingsProfile;
            if (profile == null)
            {
                profile = QsMeasurementSettingsProfile.CreateDefault();
            }

            profile.Normalize();
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["WF.ROOM"] = profile.GetChoiceContains("WF.ROOM", "Room", true),
                ["CF.ROOM"] = profile.GetChoiceContains("CF.ROOM", "Room", true),
                ["SC.ROOM"] = profile.GetChoiceContains("SC.ROOM", "Room", true),
                ["FF.ROOM"] = profile.GetChoiceContains("FF.ROOM", "Room", true),
                ["WP.ROOM"] = profile.GetChoiceContains("WP.ROOM", "Room", true)
            };
        }

        private static string GetFinishRoomRuleCode(string label)
        {
            if (string.Equals(label, WallFinishLabel, StringComparison.OrdinalIgnoreCase)) return "WF.ROOM";
            if (string.Equals(label, CeilingFinishLabel, StringComparison.OrdinalIgnoreCase)) return "CF.ROOM";
            if (string.Equals(label, SuspendedCeilingLabel, StringComparison.OrdinalIgnoreCase)) return "SC.ROOM";
            if (string.Equals(label, FloorFinishLabel, StringComparison.OrdinalIgnoreCase)) return "FF.ROOM";
            if (string.Equals(label, WaterproofLabel, StringComparison.OrdinalIgnoreCase)) return "WP.ROOM";
            return "";
        }

        private static void AccumulateQsValues(BoqTableRow row, Element element, double volumeM3, double formworkM2)
        {
            if (row == null || element == null) return;

            AccumulateQsValue(row, "HOST_VOLUME_COMPUTED", volumeM3);
            AccumulateQsValue(row, "CBIM_FormworkArea", formworkM2);
            AccumulateBuiltInQsParam(row, element, "HOST_AREA_COMPUTED", QsValueUnitKind.Area, BuiltInParameter.HOST_AREA_COMPUTED, "Area");

            AccumulateElementQsParam(row, element, "CBIM_QsFinishGrossArea", QsValueUnitKind.Area);
            AccumulateElementQsParam(row, element, "CBIM_QsFinishArea", QsValueUnitKind.Area);
            AccumulateElementQsParam(row, element, "CBIM_Beam_Length", QsValueUnitKind.Length);
            AccumulateElementQsParam(row, element, "CBIM_Beam_Width", QsValueUnitKind.Length);
            AccumulateElementQsParam(row, element, "CBIM_Beam_Depth", QsValueUnitKind.Length);
            AccumulateElementQsParam(row, element, "CBIM_Beam_Volume", QsValueUnitKind.Volume);

            string[] areaParams =
            {
                "FWK.Col.Total",
                "FWK.Col.Sides",
                "FWK.Col.TopBottom",
                "FWK.Col.SubFoun",
                "FWK.Col.SubBeam",
                "FWK.Col.SubCol",
                "FWK.Col.SubWall",
                "FWK.Col.SubFloor",
                "FWK.Col.SubGeneric",
                "FWK.Col.Stage.Basic",
                "FWK.Wall.Total",
                "FWK.Wall.Sides",
                "FWK.Wall.OpeningSide",
                "FWK.Wall.OpeningBottom",
                "FWK.Wall.SubFoun",
                "FWK.Wall.SubBeam",
                "FWK.Wall.SubCol",
                "FWK.Wall.SubWall",
                "FWK.Wall.SubFloor",
                "FWK.Wall.SubGeneric",
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
                "FWK.Beam.Stage.Basic",
                "FWK.Floor.Total",
                "FWK.Floor.Bottom",
                "FWK.Floor.Sides",
                "FWK.Floor.OpeningSide",
                "FWK.Floor.Opening.Area",
                "FWK.Floor.EdgeBreak.Lte250",
                "FWK.Floor.EdgeBreak.Lte500",
                "FWK.Floor.EdgeBreak.Lte1000",
                "FWK.Floor.EdgeBreak.Over1000",
                "FWK.Floor.TopSlope",
                "FWK.Floor.StrutBasic",
                "FWK.Floor.StrutSoffit",
                "FWK.Floor.StrutStageArea",
                "FWK.Floor.StrutEdge",
                "FWK.Floor.StrutEdgeStageArea",
                "FWK.Floor.StrutTop",
                "FWK.Floor.StrutTopStageArea",
                "FWK.Floor.SubFoun",
                "FWK.Floor.SubBeam",
                "FWK.Floor.SubCol",
                "FWK.Floor.SubWall",
                "FWK.Floor.SubFloor",
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
                "FWK.Foun.SideArea.Staged",
                "FWK.Kerb.Total",
                "FWK.Kerb.Sides",
                "FWK.Kerb.Top",
                "FWK.Kerb.SubWall",
                "FWK.Kerb.SubCol",
                "FWK.Other.Total",
                "FWK.Other.Sides",
                "FWK.Other.Bottom",
                "FWK.Other.SubStructure",
                "FWK.Lintel.Sides",
                "FWK.Lintel.Bottom",
                "FWK.Drop.Soffit",
                "FWK.Drop.Stage.1",
                "FWK.Eave.Bottom",
                "FWK.Eave.Edge",
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

            foreach (string name in areaParams)
            {
                AccumulateElementQsParam(row, element, name, QsValueUnitKind.Area);
            }

            for (int stage = 1; stage <= 15; stage++)
            {
                string stageText = stage.ToString(CultureInfo.InvariantCulture);
                AccumulateElementQsParam(row, element, "FWK.Col.Stage." + stageText, QsValueUnitKind.Area);
                AccumulateElementQsParam(row, element, "FWK.Beam.Stage." + stageText, QsValueUnitKind.Area);
                AccumulateElementQsParam(row, element, "FWK.Floor.StrutStage." + stageText, QsValueUnitKind.Area);
                AccumulateElementQsParam(row, element, "FWK.Foun.SideLength.Stage." + stageText, QsValueUnitKind.Length);
                AccumulateElementQsParam(row, element, "FWK.Foun.SideArea.Stage." + stageText, QsValueUnitKind.Area);
            }

            for (int stage = 0; stage <= 15; stage++)
            {
                string stageText = stage.ToString(CultureInfo.InvariantCulture);
                AccumulateElementQsParam(row, element, "FWK.Wall.EdgeLength.Stage." + stageText, QsValueUnitKind.Length);
                AccumulateElementQsParam(row, element, "FWK.Wall.EdgeArea.Stage." + stageText, QsValueUnitKind.Area);
            }

            string[] lengthParams =
            {
                "FWK.Col.StrutHeight",
                "FWK.Beam.StrutHeight",
                "FWK.Floor.Opening.Girth",
                "FWK.Floor.StrutHeight",
                "FWK.Foun.SideLength.Total",
                "FWK.Kerb.Length",
                "FWK.Lintel.Length",
                "FWK.Wall.OriginalHeight"
            };

            foreach (string name in lengthParams)
            {
                AccumulateElementQsParam(row, element, name, QsValueUnitKind.Length);
            }

            string[] numberParams =
            {
                "Rebar.Weight",
                "FWK.Col.StrutStage",
                "FWK.Beam.StrutStage",
                "FWK.Floor.Opening.Count",
                "FWK.Floor.StrutStageCount",
                "FWK.Stair.StepCount"
            };

            foreach (string name in numberParams)
            {
                AccumulateElementQsParam(row, element, name, QsValueUnitKind.Number);
            }
        }

        private static void AccumulateElementQsParam(BoqTableRow row, Element element, string key, QsValueUnitKind unitKind)
        {
            double internalValue = GetParamDouble(element, key);
            AccumulateConvertedQsValue(row, key, internalValue, unitKind);
        }

        private static void AccumulateBuiltInQsParam(
            BoqTableRow row,
            Element element,
            string key,
            QsValueUnitKind unitKind,
            BuiltInParameter bip,
            params string[] fallbackNames)
        {
            double internalValue = GetParamDouble(element, bip, fallbackNames);
            AccumulateConvertedQsValue(row, key, internalValue, unitKind);
        }

        private static void AccumulateConvertedQsValue(BoqTableRow row, string key, double internalValue, QsValueUnitKind unitKind)
        {
            if (Math.Abs(internalValue) <= 1e-9) return;

            double value;
            switch (unitKind)
            {
                case QsValueUnitKind.Length:
                    value = ToMeters(internalValue);
                    break;
                case QsValueUnitKind.Volume:
                    value = ToCubicMeters(internalValue);
                    break;
                case QsValueUnitKind.Number:
                    value = internalValue;
                    break;
                default:
                    value = ToSquareMeters(internalValue);
                    break;
            }

            AccumulateQsValue(row, key, value);
        }

        private static void AccumulateQsValue(BoqTableRow row, string key, double value)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || Math.Abs(value) <= 1e-9) return;

            Dictionary<string, double> values = row.QsValues;
            if (values == null)
            {
                values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                row.QsValues = values;
            }

            double current;
            values.TryGetValue(key, out current);
            values[key] = current + value;
        }

        private static string MergeAuditText(string current, string next, int maxLength)
        {
            string value = (next ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return current ?? "";
            }

            string existing = (current ?? "").Trim();
            if (string.IsNullOrWhiteSpace(existing))
            {
                return value.Length <= maxLength ? value : value.Substring(0, Math.Max(0, maxLength - 4)) + " ...";
            }

            List<string> parts = existing
                .Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (parts.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)))
            {
                return existing;
            }

            if (existing.EndsWith("...", StringComparison.Ordinal))
            {
                return existing;
            }

            string merged = existing + " | " + value;
            if (merged.Length <= maxLength)
            {
                return merged;
            }

            int keep = Math.Max(0, maxLength - 6);
            return existing.Length <= keep ? existing + " | ..." : existing.Substring(0, keep) + " ...";
        }

        private static List<Element> CollectSoilExcavationElements(UIDocument uidoc, Document doc, QsScope scope)
        {
            return CollectElementsByCategory(uidoc, doc, scope, BuiltInCategory.OST_GenericModel)
                .Where(IsSoilExcavationDirectShape)
                .ToList();
        }

        private static bool IsSoilExcavationDirectShape(Element element)
        {
            if (!(element is DirectShape ds))
            {
                return false;
            }

            string appDataId = "";
            try
            {
                appDataId = ds.ApplicationDataId ?? "";
            }
            catch
            {
                appDataId = "";
            }

            if (!string.IsNullOrWhiteSpace(appDataId) &&
                appDataId.IndexOf("SoilExcavation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string appId = "";
            try
            {
                appId = ds.ApplicationId ?? "";
            }
            catch
            {
                appId = "";
            }

            if (!string.IsNullOrWhiteSpace(appId) &&
                appId.IndexOf("CamboBIM", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string name = (element.Name ?? "").Trim();
                if (name.IndexOf("Soil", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("Excav", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveDerivedSoilLevelLabel(Document doc, IList<Element> soilElements)
        {
            if (soilElements == null || soilElements.Count == 0)
            {
                return "(All Levels)";
            }

            List<string> levels = soilElements
                .Select(e => ResolveBuildingLevel(doc, e))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (levels.Count == 1)
            {
                return levels[0];
            }

            if (levels.Count > 1)
            {
                return "(Multi-Level)";
            }

            return "(All Levels)";
        }

        private static double ComputeSoilBackfillStructuralDeductionFt3(
            UIDocument uidoc,
            Document doc,
            QsScope scope,
            IList<Element> soilElements,
            IList<Solid> soilSolids,
            Options opt)
        {
            if (doc == null || soilElements == null || soilSolids == null || soilSolids.Count == 0)
            {
                return 0.0;
            }

            List<Element> structures = CollectSourceElements(uidoc, doc, scope)
                .Select(x => x.Element)
                .Where(e => e != null)
                .GroupBy(e => e.Id.Value)
                .Select(g => g.First())
                .ToList();

            if (structures.Count == 0)
            {
                return 0.0;
            }

            List<BoundingBoxXYZ> soilElementBoxes = soilElements
                .Select(e => e?.get_BoundingBox(null))
                .Where(b => b != null)
                .ToList();

            var soilSolidEntries = soilSolids
                .Where(s => s != null && s.Volume > 1e-9)
                .Select(s => new SoilSolidEntry
                {
                    Solid = s,
                    BoundingBox = GetBoundingBoxSafe(s)
                })
                .ToList();

            var intersectionSolids = new List<Solid>();
            foreach (Element structure in structures)
            {
                BoundingBoxXYZ elementBox = structure.get_BoundingBox(null);
                if (elementBox != null && soilElementBoxes.Count > 0 && !soilElementBoxes.Any(b => BoxesIntersect(b, elementBox)))
                {
                    continue;
                }

                List<Solid> structureSolids = GetElementSolids(structure, opt);
                if (structureSolids.Count == 0)
                {
                    continue;
                }

                foreach (Solid structureSolid in structureSolids)
                {
                    if (structureSolid == null || structureSolid.Volume <= 1e-9)
                    {
                        continue;
                    }

                    BoundingBoxXYZ structureSolidBox = GetBoundingBoxSafe(structureSolid) ?? elementBox;
                    foreach (SoilSolidEntry soil in soilSolidEntries)
                    {
                        if (soil?.Solid == null || soil.Solid.Volume <= 1e-9)
                        {
                            continue;
                        }

                        if (structureSolidBox != null &&
                            soil.BoundingBox != null &&
                            !BoxesIntersect(structureSolidBox, soil.BoundingBox))
                        {
                            continue;
                        }

                        try
                        {
                            Solid inter = BooleanOperationsUtils.ExecuteBooleanOperation(
                                soil.Solid,
                                structureSolid,
                                BooleanOperationsType.Intersect);
                            if (inter != null && inter.Volume > 1e-9)
                            {
                                intersectionSolids.Add(inter);
                            }
                        }
                        catch
                        {
                            // Ignore failed boolean operations and continue best-effort deduction.
                        }
                    }
                }
            }

            return ComputeUnionVolumeFt3(intersectionSolids, out _, out _);
        }

        private static Options CreateSolidReadOptions()
        {
            return new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };
        }

        private static List<Solid> GetElementSolids(Element element, Options opt)
        {
            var solids = new List<Solid>();
            if (element == null)
            {
                return solids;
            }

            GeometryElement ge = element.get_Geometry(opt);
            if (ge == null)
            {
                return solids;
            }

            foreach (GeometryObject go in ge)
            {
                CollectSolidsFromGeometry(go, Transform.Identity, solids);
            }

            return solids;
        }

        private static void CollectSolidsFromGeometry(GeometryObject go, Transform trf, List<Solid> solids)
        {
            if (go == null || solids == null)
            {
                return;
            }

            if (go is Solid solid)
            {
                if (solid.Volume > 1e-9)
                {
                    try
                    {
                        solids.Add(trf != null ? SolidUtils.CreateTransformed(solid, trf) : solid);
                    }
                    catch
                    {
                        // Ignore invalid transformed solids.
                    }
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

        private static BoundingBoxXYZ GetBoundingBoxSafe(Solid solid)
        {
            if (solid == null || solid.Volume <= 1e-9)
            {
                return null;
            }

            bool hasPoint = false;
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            double maxZ = double.NegativeInfinity;

            void include(XYZ p)
            {
                if (p == null)
                {
                    return;
                }

                hasPoint = true;
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                maxZ = Math.Max(maxZ, p.Z);
            }

            try
            {
                foreach (Edge edge in solid.Edges)
                {
                    IList<XYZ> pts = edge?.Tessellate();
                    if (pts == null)
                    {
                        continue;
                    }

                    foreach (XYZ p in pts)
                    {
                        include(p);
                    }
                }
            }
            catch
            {
                // Ignore and try faces below.
            }

            if (!hasPoint)
            {
                try
                {
                    foreach (Face face in solid.Faces)
                    {
                        Mesh mesh = face?.Triangulate();
                        if (mesh == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < mesh.NumTriangles; i++)
                        {
                            MeshTriangle tri = mesh.get_Triangle(i);
                            if (tri == null)
                            {
                                continue;
                            }

                            include(tri.get_Vertex(0));
                            include(tri.get_Vertex(1));
                            include(tri.get_Vertex(2));
                        }
                    }
                }
                catch
                {
                    return null;
                }
            }

            if (!hasPoint)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private static bool BoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return !(a.Max.X < b.Min.X || a.Min.X > b.Max.X ||
                     a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y ||
                     a.Max.Z < b.Min.Z || a.Min.Z > b.Max.Z);
        }

        private static double ComputeUnionVolumeFt3(IList<Solid> solids, out int mergedPairs, out int booleanFailures)
        {
            mergedPairs = 0;
            booleanFailures = 0;

            if (solids == null || solids.Count == 0)
            {
                return 0.0;
            }

            var unioned = new List<Solid>();
            foreach (Solid source in solids)
            {
                if (source == null || source.Volume <= 1e-9)
                {
                    continue;
                }

                Solid current = source;
                BoundingBoxXYZ currentBox = GetBoundingBoxSafe(current);
                bool merged;
                do
                {
                    merged = false;

                    for (int i = 0; i < unioned.Count; i++)
                    {
                        Solid existing = unioned[i];
                        if (existing == null || existing.Volume <= 1e-9)
                        {
                            unioned.RemoveAt(i);
                            i--;
                            continue;
                        }

                        BoundingBoxXYZ existingBox = GetBoundingBoxSafe(existing);
                        if (currentBox != null && existingBox != null && !BoxesIntersect(currentBox, existingBox))
                        {
                            continue;
                        }

                        try
                        {
                            Solid union = BooleanOperationsUtils.ExecuteBooleanOperation(existing, current, BooleanOperationsType.Union);
                            if (union != null && union.Volume > 1e-9)
                            {
                                unioned.RemoveAt(i);
                                current = union;
                                currentBox = GetBoundingBoxSafe(current);
                                mergedPairs++;
                                merged = true;
                                break;
                            }
                        }
                        catch
                        {
                            booleanFailures++;
                        }
                    }
                } while (merged);

                unioned.Add(current);
            }

            return unioned.Sum(s => s != null && s.Volume > 1e-9 ? s.Volume : 0.0);
        }

        private static string ResolveBuildingLevel(Document doc, Element element)
        {
            if (doc == null || element == null) return "";

            ElementId levelId;
            if (TryGetLevelId(element, out levelId) && levelId != ElementId.InvalidElementId)
            {
                Level level = doc.GetElement(levelId) as Level;
                if (level != null && !string.IsNullOrWhiteSpace(level.Name))
                {
                    return level.Name.Trim();
                }
            }

            string shared = GetParamString(element, LevelParamName);
            if (!string.IsNullOrWhiteSpace(shared)) return shared.Trim();

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

            string structuralPlan = GetParamString(element, StructuralPlanParamName);
            if (!string.IsNullOrWhiteSpace(structuralPlan)) return structuralPlan.Trim();

            return "";
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

            if (element is FamilyInstance fi && fi.Symbol != null)
            {
                string fam = fi.Symbol.FamilyName ?? "";
                string type = fi.Symbol.Name ?? "";
                if (!string.IsNullOrWhiteSpace(fam) || !string.IsNullOrWhiteSpace(type))
                {
                    return string.IsNullOrWhiteSpace(fam) ? type : (fam + " : " + type);
                }
            }

            ElementId typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                ElementType t = doc.GetElement(typeId) as ElementType;
                if (t != null)
                {
                    string fam = t.FamilyName ?? "";
                    string name = t.Name ?? "";
                    if (!string.IsNullOrWhiteSpace(fam) || !string.IsNullOrWhiteSpace(name))
                    {
                        return string.IsNullOrWhiteSpace(fam) ? name : (fam + " : " + name);
                    }
                }
            }

            return element.Name ?? "";
        }

        private static string GetParamString(Element element, string name)
        {
            Parameter p = element?.LookupParameter(name);
            if (p == null) return "";

            if (p.StorageType == StorageType.String)
            {
                return p.AsString() ?? "";
            }

            return p.AsValueString() ?? "";
        }

        private static double GetParamDouble(Element element, params string[] names)
        {
            if (element == null || names == null) return 0.0;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                Parameter p = element.LookupParameter(name);
                if (p == null || p.StorageType != StorageType.Double) continue;
                return p.AsDouble();
            }

            return 0.0;
        }

        private static double GetParamDouble(Element element, BuiltInParameter bip, params string[] fallbackNames)
        {
            Parameter p = element?.get_Parameter(bip);
            if (p != null && p.StorageType == StorageType.Double)
            {
                return p.AsDouble();
            }

            return GetParamDouble(element, fallbackNames);
        }

        private static double ToSquareMeters(double internalArea)
        {
            if (internalArea <= 1e-9) return 0.0;
            return UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);
        }

        private static double ToMeters(double internalLength)
        {
            if (internalLength <= 1e-9) return 0.0;
            return UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters);
        }

        private static double ToCubicMeters(double internalVolume)
        {
            if (internalVolume <= 1e-9) return 0.0;
            return UnitUtils.ConvertFromInternalUnits(internalVolume, UnitTypeId.CubicMeters);
        }

        private static string FormatBoqAuditVolume(double value)
        {
            return value.ToString("N3", CultureInfo.InvariantCulture);
        }

        private sealed class SoilSolidEntry
        {
            public Solid Solid { get; set; }
            public BoundingBoxXYZ BoundingBox { get; set; }
        }
    }

    internal sealed class BoqTableRow
    {
        public string StructureElement { get; set; } = "";
        public string BuildingLevel { get; set; } = "";
        public string Room { get; set; } = "";
        public string TypeName { get; set; } = "";
        public int Quantity { get; set; }
        public double TotalVolumeM3 { get; set; }
        public double TotalFormworkAreaM2 { get; set; }
        public string QsRuleCode { get; set; } = "";
        public string QsFormula { get; set; } = "";
        public string QsBreakdown { get; set; } = "";
        public Dictionary<string, double> QsValues { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public int StructureOrder { get; set; }
    }
}
