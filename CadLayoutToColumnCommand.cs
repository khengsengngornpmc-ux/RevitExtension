using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.Manual)]
    public class CadLayoutToColumnCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1) Pick CAD import/link instance (DWG)
                Reference r = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new ImportInstanceSelectionFilter(),
                    "Select AutoCAD Import/Link (DWG) instance");

                ImportInstance cad = doc.GetElement(r) as ImportInstance;
                if (cad == null)
                {
                    TaskDialog.Show("CAD2MODEL", "Selected element is not an ImportInstance (DWG).");
                    return Result.Cancelled;
                }

                // 2) (Layout/Layer filter) - v1: no UI input, uses all layers.
                // If you want: we can implement a WPF dialog to pick a layer.
                string layerFilter = ""; // empty = all layers

                // 3) Pick Column Type + Levels (v1 uses defaults; you can upgrade to a picker UI later)
                FamilySymbol columnType = PickFirstStructuralColumnType(doc);
                if (columnType == null)
                {
                    TaskDialog.Show("CAD2MODEL", "No Structural Column family types found in this project.");
                    return Result.Cancelled;
                }

                Level baseLevel = PickBaseLevel(doc);
                if (baseLevel == null)
                {
                    TaskDialog.Show("CAD2MODEL", "No Levels found in this project.");
                    return Result.Cancelled;
                }

                TopMode topMode = AskTopMode();
                if (topMode == TopMode.Cancelled)
                    return Result.Cancelled;

                Level topLevel = null;
                double unconnectedHeightFt = MmToFeet(3000); // default 3000 mm

                if (topMode == TopMode.TopLevel)
                {
                    topLevel = PickTopLevel(doc, baseLevel);
                    if (topLevel == null)
                        return Result.Cancelled;

                    if (topLevel.Elevation <= baseLevel.Elevation)
                    {
                        TaskDialog.Show("CAD2MODEL", "Top Level must be above Base Level.");
                        return Result.Cancelled;
                    }
                }

                // 4) Extract circle centers from CAD geometry
                List<CadCircleHit> hits = ExtractCircleCentersFromImport(doc, cad, layerFilter);

                if (hits.Count == 0)
                {
                    TaskDialog.Show("CAD2MODEL",
                        "No circles found in the selected CAD import.\n\n" +
                        "This v1 command detects columns from CAD circles (center marks).\n" +
                        "If your CAD columns are Blocks or Polylines, tell me and I’ll adapt it.");
                    return Result.Cancelled;
                }

                // 5) De-duplicate points (tolerance in mm)
                double tolFt = MmToFeet(10.0); // 10mm tolerance
                List<XYZ> uniquePts = DeduplicatePoints(hits.Select(h => h.Center).ToList(), tolFt);

                // 6) Place columns
                int created = 0;
                using (Transaction t = new Transaction(doc, "CAD2MODEL - CAD Layout -> Column"))
                {
                    t.Start();

                    if (!columnType.IsActive) columnType.Activate();

                    foreach (XYZ p in uniquePts)
                    {
                        FamilyInstance fi = doc.Create.NewFamilyInstance(
                            p,
                            columnType,
                            baseLevel,
                            StructuralType.Column);

                        ApplyColumnTop(fi, topMode, topLevel, unconnectedHeightFt);

                        created++;
                    }

                    t.Commit();
                }

                TaskDialog.Show("CAD2MODEL",
                    "Created Structural Columns: " + created +
                    "\nCAD circles found: " + hits.Count +
                    "\nUnique points: " + uniquePts.Count +
                    "\nLayer filter: " + (string.IsNullOrWhiteSpace(layerFilter) ? "(none)" : layerFilter) +
                    "\nColumn type: " + columnType.FamilyName + " : " + columnType.Name +
                    "\nBase level: " + baseLevel.Name +
                    "\nTop mode: " + (topMode == TopMode.TopLevel ? ("Top Level: " + topLevel.Name) : "Unconnected Height (3000mm default)")
                );

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        // ---------------------------
        // Geometry extraction (CIRCLES)
        // ---------------------------

        private static List<CadCircleHit> ExtractCircleCentersFromImport(Document doc, ImportInstance cad, string layerFilter)
        {
            var results = new List<CadCircleHit>();

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            GeometryElement ge = cad.get_Geometry(opt);
            if (ge == null) return results;

            Transform importTrf = cad.GetTotalTransform() ?? Transform.Identity;

            foreach (GeometryObject go in ge)
            {
                ExtractFromGeomObj(doc, go, importTrf, layerFilter, results);
            }

            return results;
        }

        private static void ExtractFromGeomObj(Document doc, GeometryObject go, Transform trf, string layerFilter, List<CadCircleHit> results)
        {
            if (go == null) return;

            // Handle nested instances
            GeometryInstance gi = go as GeometryInstance;
            if (gi != null)
            {
                Transform instTrf = trf.Multiply(gi.Transform);

                GeometryElement instGeom = gi.GetInstanceGeometry();
                if (instGeom != null)
                {
                    foreach (GeometryObject igo in instGeom)
                    {
                        ExtractFromGeomObj(doc, igo, instTrf, layerFilter, results);
                    }
                }
                return;
            }

            // Curves (DWG often comes as curves/arcs)
            Curve c = go as Curve;
            if (c != null)
            {
                string layer = GetCadLayer(doc, go);

                // layer filter (optional)
                if (!string.IsNullOrWhiteSpace(layerFilter) &&
                    !string.Equals(layerFilter, layer, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Arc a = c as Arc;
                if (a != null && a.IsCyclic) // circle
                {
                    XYZ center = trf.OfPoint(a.Center);
                    results.Add(new CadCircleHit(center, layer, a.Radius));
                }

                return;
            }

            // (Optional) handle PolyLine later if needed
            // PolyLine pl = go as PolyLine; ...
        }

        private static string GetCadLayer(Document doc, GeometryObject go)
        {
            try
            {
                if (go.GraphicsStyleId == ElementId.InvalidElementId) return "";
                GraphicsStyle gs = doc.GetElement(go.GraphicsStyleId) as GraphicsStyle;
                if (gs == null) return "";
                return (gs.GraphicsStyleCategory != null) ? gs.GraphicsStyleCategory.Name : gs.Name;
            }
            catch
            {
                return "";
            }
        }

        // ---------------------------
        // Column placement helpers
        // ---------------------------

        private static void ApplyColumnTop(FamilyInstance fi, TopMode topMode, Level topLevel, double unconnectedHeightFt)
        {
            // For many column families:
            // - FAMILY_TOP_LEVEL_PARAM sets top constraint
            // - FAMILY_TOP_LEVEL_OFFSET_PARAM sets offset
            // - FAMILY_HEIGHT_PARAM sets unconnected height

            if (topMode == TopMode.TopLevel && topLevel != null)
            {
                Parameter pTop = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                if (pTop != null && !pTop.IsReadOnly)
                    pTop.Set(topLevel.Id);

                Parameter pTopOffset = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                if (pTopOffset != null && !pTopOffset.IsReadOnly)
                    pTopOffset.Set(0.0);
            }
            else if (topMode == TopMode.UnconnectedHeight)
            {
                Parameter pUnconn = fi.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM);
                if (pUnconn != null && !pUnconn.IsReadOnly)
                    pUnconn.Set(unconnectedHeightFt);
            }
        }

        // ---------------------------
        // Pickers (v1: simple defaults)
        // ---------------------------

        private static FamilySymbol PickFirstStructuralColumnType(Document doc)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             fs.Category.Id.Value == (int)BuiltInCategory.OST_StructuralColumns) // Value (not IntegerValue)
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (symbols.Count == 0) return null;
            return symbols[0]; // v1: first available
        }

        private static Level PickBaseLevel(Document doc)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0) return null;

            // Try use active view's level if it exists
            Level viewLevel = TryGetViewLevel(doc.ActiveView, doc);
            if (viewLevel != null) return viewLevel;

            return levels[0];
        }

        private static Level PickTopLevel(Document doc, Level baseLevel)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            // Choose the next level above base as default
            foreach (Level l in levels)
            {
                if (l.Elevation > baseLevel.Elevation)
                    return l;
            }

            // If none above, user should create one; cancel
            TaskDialog.Show("CAD2MODEL", "No Level found above Base Level. Create a top level first.");
            return null;
        }

        private static Level TryGetViewLevel(View v, Document doc)
        {
            try
            {
                Parameter p = v.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    return doc.GetElement(p.AsElementId()) as Level;
                }
            }
            catch { }
            return null;
        }

        private static TopMode AskTopMode()
        {
            TaskDialog td = new TaskDialog("CAD2MODEL - Column Top");
            td.MainInstruction = "Column Top Constraint";
            td.MainContent = "Choose how to set column top:";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Top Level");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Unconnected Height (default 3000 mm)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            TaskDialogResult res = td.Show();

            if (res == TaskDialogResult.CommandLink1) return TopMode.TopLevel;
            if (res == TaskDialogResult.CommandLink2) return TopMode.UnconnectedHeight;

            return TopMode.Cancelled; // user cancelled
        }

        // ---------------------------
        // Utilities
        // ---------------------------

        private static List<XYZ> DeduplicatePoints(List<XYZ> points, double tolFt)
        {
            var result = new List<XYZ>();
            foreach (XYZ p in points)
            {
                bool exists = false;
                foreach (XYZ q in result)
                {
                    if (p.DistanceTo(q) <= tolFt)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) result.Add(p);
            }
            return result;
        }

        private static double MmToFeet(double mm)
        {
            return mm / 304.8;
        }

        private enum TopMode
        {
            TopLevel,
            UnconnectedHeight,
            Cancelled
        }

        private class CadCircleHit
        {
            public XYZ Center;
            public string Layer;
            public double RadiusFt;

            public CadCircleHit(XYZ center, string layer, double radiusFt)
            {
                Center = center;
                Layer = layer ?? "";
                RadiusFt = radiusFt;
            }
        }

        private class ImportInstanceSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is ImportInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
